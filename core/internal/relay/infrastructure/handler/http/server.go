package http

import (
	"context"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/crossrelay/core/internal/relay/application/pairing"
	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/internal/relay/domain/ports"
	"github.com/crossrelay/core/internal/relay/infrastructure/security"
	"github.com/crossrelay/core/kit/constants"
	"github.com/crossrelay/core/kit/utils"
)

// Server handles all inbound CrossRelay HTTP requests over TLS 1.3
type Server struct {
	httpServer     *http.Server
	ctrlServer     *http.Server
	certMgr        *security.CertManager
	pinMgr         *security.PinManager
	peersStore     ports.TrustedPeersStore
	settingsStore  ports.SettingsStore
	pairingService *pairing.Service
	eventSink      ports.EventSink
	activeSessions sync.Map // sessionID -> *incomingSessionState
	bufPool        sync.Pool
	activePort     int
	ctrlStarted    bool
	ExtraMuxRoutes     func(mux *http.ServeMux)
	OnPingReceived     func(remoteIP string)
	OnCallerDiscovered func(peer model.Peer)
}

type incomingSessionState struct {
	manifest       *model.TransferManifest
	fileIndex      map[string]model.FileItem
	pairingToken   string
	downloadDir    string
	mu             sync.Mutex
	bytesReceived  int64
	filesCompleted int
	canceled       bool
	startedAt      time.Time
	lastActivity   time.Time
	lastReport     time.Time
}

func (st *incomingSessionState) markCanceled() {
	st.mu.Lock()
	st.canceled = true
	st.mu.Unlock()
}

func (st *incomingSessionState) isCanceled() bool {
	st.mu.Lock()
	c := st.canceled
	st.mu.Unlock()
	return c
}

func (st *incomingSessionState) addProgress(n int64) (totalReceived int64, speedBps int64, shouldReport bool) {
	st.mu.Lock()
	defer st.mu.Unlock()
	st.bytesReceived += n
	now := time.Now()
	st.lastActivity = now
	elapsed := now.Sub(st.startedAt).Seconds()
	if elapsed > 0 {
		speedBps = int64(float64(st.bytesReceived) / elapsed)
	}
	if now.Sub(st.lastReport) >= 150*time.Millisecond {
		st.lastReport = now
		shouldReport = true
	}
	return st.bytesReceived, speedBps, shouldReport
}

type progressReader struct {
	r          io.Reader
	isCanceled func() bool
	onBytes    func(n int64)
}

func (pr *progressReader) Read(p []byte) (int, error) {
	if pr.isCanceled != nil && pr.isCanceled() {
		return 0, context.Canceled
	}
	n, err := pr.r.Read(p)
	if n > 0 && pr.onBytes != nil {
		pr.onBytes(int64(n))
	}
	if pr.isCanceled != nil && pr.isCanceled() {
		return n, context.Canceled
	}
	return n, err
}

func NewServer(
	certMgr *security.CertManager,
	pinMgr *security.PinManager,
	peersStore ports.TrustedPeersStore,
	settingsStore ports.SettingsStore,
	pairingService *pairing.Service,
	eventSink ports.EventSink,
) *Server {
	return &Server{
		certMgr:        certMgr,
		pinMgr:         pinMgr,
		peersStore:     peersStore,
		settingsStore:  settingsStore,
		pairingService: pairingService,
		eventSink:      eventSink,
		bufPool: sync.Pool{
			New: func() any {
				b := make([]byte, constants.DefaultChunkSize)
				return &b
			},
		},
	}
}

// Start launches the TLS 1.3 HTTP server on the configured port
func (s *Server) Start(port int) error {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /api/v1/ping", s.handlePing)
	mux.HandleFunc("POST /api/v1/pair/init", s.handlePairInit)
	mux.HandleFunc("POST /api/v1/pair/verify", s.handlePairVerify)
	mux.HandleFunc("POST /api/v1/session/request", s.handleSessionRequest)
	mux.HandleFunc("POST /api/v1/session/{sessionId}/upload/{fileId}", s.handleSessionUpload)
	mux.HandleFunc("POST /api/v1/session/{sessionId}/cancel", s.handleSessionCancel)

	if s.ExtraMuxRoutes != nil {
		s.ExtraMuxRoutes(mux)
	}

	addr := fmt.Sprintf("0.0.0.0:%d", port)
	s.httpServer = &http.Server{
		Addr:              addr,
		Handler:           mux,
		TLSConfig:         s.certMgr.ServerTLSConfig(),
		ReadHeaderTimeout: 10 * time.Second,
	}

	// Dedicated localhost control listener first so local UI control API is ready immediately
	// and avoids WSL2 localhostForwarding collisions on :4242.
	if !s.ctrlStarted && s.ExtraMuxRoutes != nil {
		ctrlMux := http.NewServeMux()
		s.ExtraMuxRoutes(ctrlMux)

		var ctrlPorts []int
		if ctrlEnv := os.Getenv("CROSSRELAY_CONTROL_PORT"); ctrlEnv != "" {
			if cp, convErr := strconv.Atoi(ctrlEnv); convErr == nil && cp > 0 {
				ctrlPorts = append(ctrlPorts, cp)
			}
		}
		for cp := constants.ControlPortStart; cp <= constants.ControlPortEnd; cp++ {
			ctrlPorts = append(ctrlPorts, cp)
		}

		for _, cp := range ctrlPorts {
			if cp == port {
				continue
			}
			ctrlAddr := fmt.Sprintf("127.0.0.1:%d", cp)
			if ctrlLn, lnErr := net.Listen("tcp", ctrlAddr); lnErr == nil {
				s.ctrlStarted = true
				s.ctrlServer = &http.Server{
					Addr:              ctrlAddr,
					Handler:           ctrlMux,
					TLSConfig:         s.certMgr.ServerTLSConfig(),
					ReadHeaderTimeout: 10 * time.Second,
				}
				go func() {
					_ = s.ctrlServer.Serve(tlsListenerShim(ctrlLn, s.certMgr.ServerTLSConfig()))
				}()
				break
			}
		}
	}

	listener, err := net.Listen("tcp", addr)
	if err == nil {
		s.activePort = port
		tlsListener := tlsListenerShim(listener, s.certMgr.ServerTLSConfig())
		go func() {
			_ = s.httpServer.Serve(tlsListener)
		}()
	} else {
		// Fallback when 127.0.0.1:port is held by WSL2 localhostForwarding (wslrelay.exe):
		// bind each active non-loopback IPv4 interface synchronously on the same port.
		var boundListeners []net.Listener
		if ifaces, ifaceErr := net.Interfaces(); ifaceErr == nil {
			for _, iface := range ifaces {
				if iface.Flags&net.FlagUp == 0 || iface.Flags&net.FlagLoopback != 0 {
					continue
				}
				addrs, _ := iface.Addrs()
				for _, a := range addrs {
					ipNet, ok := a.(*net.IPNet)
					if !ok {
						continue
					}
					v4 := ipNet.IP.To4()
					if v4 == nil {
						continue
					}
					if ifLn, lnErr := net.Listen("tcp", fmt.Sprintf("%s:%d", v4.String(), port)); lnErr == nil {
						boundListeners = append(boundListeners, ifLn)
					}
				}
			}
		}
		if len(boundListeners) == 0 {
			return fmt.Errorf("failed to bind port %d: %w", port, err)
		}
		s.activePort = port
		for _, ifLn := range boundListeners {
			go func(ln net.Listener) {
				_ = s.httpServer.Serve(tlsListenerShim(ln, s.certMgr.ServerTLSConfig()))
			}(ifLn)
		}
	}

	return nil
}

// ActivePort returns the P2P TCP port currently bound by the server.
func (s *Server) ActivePort() int {
	if s.activePort > 0 {
		return s.activePort
	}
	return constants.DefaultPort
}

// Stop shuts down the HTTP server gracefully
func (s *Server) Stop() error {
	if s.ctrlServer != nil {
		_ = s.ctrlServer.Close()
	}
	if s.httpServer != nil {
		return s.httpServer.Close()
	}
	return nil
}

// GET /api/v1/ping
func (s *Server) handlePing(w http.ResponseWriter, r *http.Request) {
	settings, _ := s.settingsStore.Get(r.Context())
	name := "CrossRelay Device"
	id := "unknown-node"
	if settings != nil {
		name = settings.DeviceName
		id = settings.DeviceID
	}

	resp := PingResponse{
		Status:      "ready",
		ID:          id,
		Name:        name,
		OS:          runtime.GOOS,
		Version:     constants.ProtocolVersion,
		Fingerprint: s.certMgr.Fingerprint,
	}
	s.writeJSON(w, http.StatusOK, resp)

	if host, _, err := net.SplitHostPort(r.RemoteAddr); err == nil {
		if host != "" && host != "127.0.0.1" && host != "::1" {
			if s.OnCallerDiscovered != nil {
				if callerID := strings.TrimSpace(r.Header.Get("X-CrossRelay-Id")); callerID != "" && callerID != id {
					callerPort := constants.DefaultPort
					if p, err := strconv.Atoi(strings.TrimSpace(r.Header.Get("X-CrossRelay-Port"))); err == nil && p > 0 {
						callerPort = p
					}
					callerName := strings.TrimSpace(r.Header.Get("X-CrossRelay-Name"))
					if callerName == "" {
						callerName = "Peer (" + host + ")"
					}
					s.OnCallerDiscovered(model.Peer{
						ID:          callerID,
						Name:        callerName,
						OS:          strings.TrimSpace(r.Header.Get("X-CrossRelay-OS")),
						Version:     constants.ProtocolVersion,
						Fingerprint: strings.TrimSpace(r.Header.Get("X-CrossRelay-Fp")),
						IP:          host,
						Port:        callerPort,
						IsOnline:    true,
						LastSeenAt:  time.Now(),
					})
				}
			}
			if s.OnPingReceived != nil {
				s.OnPingReceived(host)
			}
		}
	}
}

// POST /api/v1/pair/init
func (s *Server) handlePairInit(w http.ResponseWriter, r *http.Request) {
	r.Body = http.MaxBytesReader(w, r.Body, 64*1024)
	var req PairInitRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		s.writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}

	// Check if already blocked
	blocked, _ := s.peersStore.IsBlocked(r.Context(), req.Requester.ID)
	if blocked {
		s.writeError(w, http.StatusForbidden, "peer is blocked")
		return
	}

	requesterPeer := model.Peer{
		ID:          req.Requester.ID,
		Name:        req.Requester.Name,
		OS:          req.Requester.OS,
		Fingerprint: req.Requester.Fingerprint,
	}

	challenge, err := s.pinMgr.GeneratePin(requesterPeer)
	if err != nil {
		if errors.Is(err, model.ErrPinMaxAttempts) {
			s.writeError(w, http.StatusForbidden, "rate limit exceeded: too many failed attempts")
			return
		}
		s.writeError(w, http.StatusInternalServerError, "failed to generate pin")
		return
	}

	// Notify UI (Desktop/Android) so it displays the 6-digit PIN on screen
	if s.eventSink != nil {
		s.eventSink.OnPinChallengeReceived(*challenge)
	}

	s.writeJSON(w, http.StatusOK, PairInitResponse{
		Status:           "awaiting_pin",
		ExpiresInSeconds: constants.PinExpirationSec,
	})
}

// POST /api/v1/pair/verify
func (s *Server) handlePairVerify(w http.ResponseWriter, r *http.Request) {
	r.Body = http.MaxBytesReader(w, r.Body, 64*1024)
	var req PairVerifyRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		s.writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}

	challenge, err := s.pinMgr.VerifyPin(req.RequesterID, req.EnteredPin)
	if err != nil {
		if errors.Is(err, model.ErrPinExpired) {
			s.writeError(w, http.StatusRequestTimeout, "pin expired")
			return
		}
		if errors.Is(err, model.ErrPinMaxAttempts) {
			s.writeError(w, http.StatusForbidden, "maximum attempts exceeded")
			return
		}
		s.writeError(w, http.StatusUnauthorized, "invalid pin")
		return
	}

	// PIN correct -> Generate and seal pairing token
	token, _ := utils.GenerateRandomHex(32)
	peer := model.Peer{
		ID:          challenge.RequesterID,
		Name:        challenge.RequesterName,
		OS:          challenge.RequesterOS,
		Fingerprint: challenge.RequesterFP,
	}
	if peer.ID == "" {
		peer.ID = req.RequesterID
	}
	trusted, err := s.pairingService.CompletePairing(r.Context(), peer, token)
	if err != nil {
		s.writeError(w, http.StatusInternalServerError, "failed to store pairing")
		return
	}

	if s.eventSink != nil && trusted != nil {
		s.eventSink.OnPeerPaired(*trusted)
	}

	s.writeJSON(w, http.StatusOK, PairVerifyResponse{
		Status:       "paired",
		PairingToken: token,
	})
}

// POST /api/v1/session/request
func (s *Server) handleSessionRequest(w http.ResponseWriter, r *http.Request) {
	r.Body = http.MaxBytesReader(w, r.Body, 64<<20)
	// Zero-Trust verification: X-CrossRelay-Pairing-Token
	token := r.Header.Get("X-CrossRelay-Pairing-Token")
	senderID := r.Header.Get("X-CrossRelay-Sender-Id")

	var manifest model.TransferManifest
	if err := json.NewDecoder(r.Body).Decode(&manifest); err != nil {
		s.writeError(w, http.StatusBadRequest, "invalid manifest body")
		return
	}

	if senderID == "" {
		senderID = manifest.SenderID
	}

	trusted, err := s.pairingService.ValidatePairingToken(r.Context(), senderID, token)
	if err != nil {
		s.writeError(w, http.StatusForbidden, err.Error())
		return
	}

	manifest.RequiresConfirmation = trusted.Policy == model.PolicyAskConfirmation

	// Notify UI/Engine of incoming manifest metadata (and check confirmation if required)
	if s.eventSink != nil {
		accepted, err := s.eventSink.OnIncomingTransferRequest(manifest)
		if manifest.RequiresConfirmation && (err != nil || !accepted) {
			s.writeError(w, http.StatusForbidden, "transfer rejected by user")
			return
		}
	}

	// Prune stale abandoned sessions older than 1 hour to bound memory
	now := time.Now()
	s.activeSessions.Range(func(k, v any) bool {
		if st, ok := v.(*incomingSessionState); ok {
			st.mu.Lock()
			last := st.lastActivity
			st.mu.Unlock()
			if !last.IsZero() && now.Sub(last) > time.Hour {
				s.activeSessions.Delete(k)
			}
		}
		return true
	})

	fileIndex := make(map[string]model.FileItem, len(manifest.Files))
	for _, f := range manifest.Files {
		fileIndex[f.FileID] = f
	}

	s.activeSessions.Store(manifest.SessionID, &incomingSessionState{
		manifest:     &manifest,
		fileIndex:    fileIndex,
		pairingToken: token,
		downloadDir:  trusted.DedicatedDownloadFolder,
		startedAt:    now,
		lastActivity: now,
	})
	s.writeJSON(w, http.StatusOK, map[string]string{
		"status":    "accepted",
		"sessionId": manifest.SessionID,
	})
}

// POST /api/v1/session/{sessionId}/upload/{fileId}
func (s *Server) handleSessionUpload(w http.ResponseWriter, r *http.Request) {
	sessionID := r.PathValue("sessionId")
	fileID := r.PathValue("fileId")

	// Validate session exists
	val, ok := s.activeSessions.Load(sessionID)
	if !ok {
		s.writeError(w, http.StatusNotFound, "session not found")
		return
	}
	state := val.(*incomingSessionState)
	if state.pairingToken != "" {
		reqToken := r.Header.Get("X-CrossRelay-Pairing-Token")
		if subtle.ConstantTimeCompare([]byte(reqToken), []byte(state.pairingToken)) != 1 {
			s.writeError(w, http.StatusForbidden, "invalid pairing token")
			return
		}
	}
	manifest := state.manifest

	var targetFile *model.FileItem
	if state.fileIndex != nil {
		if f, exists := state.fileIndex[fileID]; exists {
			targetFile = &f
		}
	} else {
		for _, f := range manifest.Files {
			if f.FileID == fileID {
				fc := f
				targetFile = &fc
				break
			}
		}
	}
	if targetFile == nil {
		s.writeError(w, http.StatusNotFound, "file id not found in manifest")
		return
	}

	// Prepare download location
	downloadDir := state.downloadDir
	if downloadDir == "" {
		settings, _ := s.settingsStore.Get(r.Context())
		if settings != nil {
			downloadDir = settings.DownloadDirectory
		}
	}
	if downloadDir == "" {
		if homeDir, err := os.UserHomeDir(); err == nil && homeDir != "" {
			downloadDir = filepath.Join(homeDir, "Downloads", "CrossRelay")
		} else {
			downloadDir = filepath.Join(os.TempDir(), "CrossRelayDownloads")
		}
	}
	downloadDir = filepath.Clean(downloadDir)

	relPath := filepath.Clean(filepath.FromSlash(strings.TrimLeft(targetFile.Name, "/\\")))
	if relPath == "." || relPath == ".." || filepath.IsAbs(relPath) ||
		strings.HasPrefix(relPath, ".."+string(filepath.Separator)) {
		s.writeError(w, http.StatusBadRequest, "invalid file path")
		return
	}

	finalPath := filepath.Join(downloadDir, relPath)
	if !strings.HasPrefix(finalPath, downloadDir+string(filepath.Separator)) {
		s.writeError(w, http.StatusBadRequest, "invalid file path")
		return
	}
	if err := os.MkdirAll(filepath.Dir(finalPath), 0755); err != nil {
		s.writeError(w, http.StatusInternalServerError, "failed to create download directory")
		return
	}

	partPath := finalPath + constants.PartFileSuffix

	partFile, err := os.Create(partPath)
	if err != nil {
		s.writeError(w, http.StatusInternalServerError, "failed to create local file")
		return
	}

	// Stream and compute SHA-256 simultaneously
	hasher := sha256.New()
	writer := io.MultiWriter(partFile, hasher)

	bodyReader := &progressReader{
		r:          r.Body,
		isCanceled: state.isCanceled,
		onBytes: func(n int64) {
			totalRecv, speedBps, shouldReport := state.addProgress(n)
			if shouldReport && s.eventSink != nil {
				s.eventSink.OnTransferProgress(sessionID, totalRecv, manifest.TotalBytes, speedBps)
			}
		},
	}

	var bufPtr *[]byte
	if pooled, ok := s.bufPool.Get().(*[]byte); ok && pooled != nil {
		bufPtr = pooled
	} else {
		b := make([]byte, constants.DefaultChunkSize)
		bufPtr = &b
	}
	_, err = io.CopyBuffer(writer, bodyReader, *bufPtr)
	s.bufPool.Put(bufPtr)
	_ = partFile.Close()

	if err != nil {
		_ = os.Remove(partPath)
		s.activeSessions.Delete(sessionID)
		if s.eventSink != nil {
			s.eventSink.OnTransferCompleted(sessionID, false, "streaming interrupted")
		}
		s.writeError(w, http.StatusInternalServerError, "streaming interrupted")
		return
	}

	// Verify SHA-256 hash integrity
	calculatedHash := hex.EncodeToString(hasher.Sum(nil))
	expectedHash := r.Header.Get("X-CrossRelay-Sha256")
	if expectedHash == "" {
		expectedHash = targetFile.SHA256
	}

	if expectedHash != "" && !strings.EqualFold(calculatedHash, expectedHash) {
		_ = os.Remove(partPath)
		s.writeError(w, http.StatusUnprocessableEntity, "hash mismatch: integrity check failed")
		return
	}

	// Hash verified: apply duplicate policy when finalizing .part file
	dupPolicy := "rename"
	if s.settingsStore != nil {
		if cfg, cfgErr := s.settingsStore.Get(r.Context()); cfgErr == nil && cfg != nil && cfg.DuplicatePolicy != "" {
			dupPolicy = strings.ToLower(cfg.DuplicatePolicy)
		}
	}

	if _, statErr := os.Stat(finalPath); statErr == nil {
		switch dupPolicy {
		case "skip":
			_ = os.Remove(partPath)
		case "overwrite":
			_ = os.Remove(finalPath)
			if err := os.Rename(partPath, finalPath); err != nil {
				s.writeError(w, http.StatusInternalServerError, "failed to finalize file")
				return
			}
		default: // "rename"
			resolvedPath := uniqueDestinationPath(finalPath)
			if err := os.Rename(partPath, resolvedPath); err != nil {
				s.writeError(w, http.StatusInternalServerError, "failed to finalize file")
				return
			}
		}
	} else {
		if err := os.Rename(partPath, finalPath); err != nil {
			s.writeError(w, http.StatusInternalServerError, "failed to finalize file")
			return
		}
	}

	state.mu.Lock()
	state.filesCompleted++
	completedAll := state.filesCompleted >= len(manifest.Files)
	totalRecv := state.bytesReceived
	now := time.Now()
	elapsed := now.Sub(state.startedAt).Seconds()
	var speedBps int64
	if elapsed > 0 {
		speedBps = int64(float64(totalRecv) / elapsed)
	}
	shouldReport := completedAll || now.Sub(state.lastReport) >= 150*time.Millisecond
	if shouldReport {
		state.lastReport = now
	}
	state.mu.Unlock()

	if s.eventSink != nil {
		if shouldReport {
			s.eventSink.OnTransferProgress(sessionID, totalRecv, manifest.TotalBytes, speedBps)
		}
		if completedAll {
			s.activeSessions.Delete(sessionID)
			s.eventSink.OnTransferCompleted(sessionID, true, "")
		}
	}

	s.writeJSON(w, http.StatusOK, SessionUploadResponse{
		FileID:        targetFile.FileID,
		Status:        "completed",
		BytesReceived: totalRecv,
		Verified:      true,
	})
}

// POST /api/v1/session/{sessionId}/cancel
func (s *Server) handleSessionCancel(w http.ResponseWriter, r *http.Request) {
	sessionID := r.PathValue("sessionId")
	if val, ok := s.activeSessions.Load(sessionID); ok {
		st := val.(*incomingSessionState)
		if st.pairingToken != "" {
			reqToken := r.Header.Get("X-CrossRelay-Pairing-Token")
			if subtle.ConstantTimeCompare([]byte(reqToken), []byte(st.pairingToken)) != 1 {
				s.writeError(w, http.StatusForbidden, "invalid pairing token")
				return
			}
		}
		st.markCanceled()
		s.activeSessions.Delete(sessionID)
	}
	s.writeJSON(w, http.StatusOK, map[string]string{"status": "cancelled"})
}

// CancelActiveIncoming aborts any active incoming transfer sessions from the local UI.
func (s *Server) CancelActiveIncoming() bool {
	canceledAny := false
	s.activeSessions.Range(func(key, val any) bool {
		sessionID, _ := key.(string)
		if st, ok := val.(*incomingSessionState); ok {
			st.markCanceled()
			canceledAny = true
			if s.eventSink != nil && sessionID != "" {
				s.eventSink.OnTransferCompleted(sessionID, false, "canceled by receiver")
			}
		}
		s.activeSessions.Delete(key)
		return true
	})
	return canceledAny
}

func (s *Server) writeJSON(w http.ResponseWriter, statusCode int, data any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(statusCode)
	_ = json.NewEncoder(w).Encode(data)
}

func uniqueDestinationPath(path string) string {
	dir := filepath.Dir(path)
	ext := filepath.Ext(path)
	base := strings.TrimSuffix(filepath.Base(path), ext)
	for i := 1; i < 10000; i++ {
		candidate := filepath.Join(dir, fmt.Sprintf("%s (%d)%s", base, i, ext))
		if _, err := os.Stat(candidate); os.IsNotExist(err) {
			return candidate
		}
	}
	return filepath.Join(dir, fmt.Sprintf("%s (%d)%s", base, time.Now().UnixNano(), ext))
}

func (s *Server) writeError(w http.ResponseWriter, statusCode int, message string) {
	s.writeJSON(w, statusCode, ErrorResponse{Error: message})
}
