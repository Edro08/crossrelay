package control

import (
	"context"
	"encoding/json"
	"fmt"
	"net"
	"net/http"
	"os/exec"
	"runtime"
	"strings"
	"time"

	"github.com/crossrelay/core/internal/relay/application/pairing"
	"github.com/crossrelay/core/internal/relay/application/send"
	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/internal/relay/domain/ports"
	"github.com/crossrelay/core/internal/relay/infrastructure/discovery"
	"github.com/crossrelay/core/internal/relay/infrastructure/repository/rest"
	"github.com/crossrelay/core/internal/relay/infrastructure/security"
	"github.com/crossrelay/core/kit/constants"
)

// Handler serves localhost-only control endpoints for the Desktop/Mobile UI
type Handler struct {
	certMgr        *security.CertManager
	peersStore     ports.TrustedPeersStore
	settingsStore  ports.SettingsStore
	discovery      *discovery.UDPDiscovery
	client         *rest.Client
	pairingService *pairing.Service
	sendService    *send.Service
	broker         *Broker
	activePort     int
	EventSink        *LocalEventSink
	OnCancelIncoming func() bool
}

func (h *Handler) SetActivePort(port int) {
	h.activePort = port
}

func NewHandler(
	certMgr *security.CertManager,
	peersStore ports.TrustedPeersStore,
	settingsStore ports.SettingsStore,
	discovery *discovery.UDPDiscovery,
	client *rest.Client,
	pairingService *pairing.Service,
	sendService *send.Service,
	broker *Broker,
) *Handler {
	return &Handler{
		certMgr:        certMgr,
		peersStore:     peersStore,
		settingsStore:  settingsStore,
		discovery:      discovery,
		client:         client,
		pairingService: pairingService,
		sendService:    sendService,
		broker:         broker,
	}
}

func (h *Handler) RegisterRoutes(mux *http.ServeMux) {
	mux.HandleFunc("GET /local/v1/status", h.handleStatus)
	mux.HandleFunc("GET /local/v1/settings", h.handleGetSettings)
	mux.HandleFunc("POST /local/v1/settings", h.handleSaveSettings)
	mux.HandleFunc("GET /local/v1/trusted", h.handleGetTrusted)
	mux.HandleFunc("POST /local/v1/trusted/update", h.handleUpdateTrusted)
	mux.HandleFunc("POST /local/v1/trusted/block", h.handleSetBlocked)
	mux.HandleFunc("DELETE /local/v1/trusted/{id}", h.handleDeleteTrusted)
	mux.HandleFunc("POST /local/v1/pair/init", h.handleLocalPairInit)
	mux.HandleFunc("POST /local/v1/pair/verify", h.handleLocalPairVerify)
	mux.HandleFunc("POST /local/v1/peers/probe", h.handleProbePeer)
	mux.HandleFunc("POST /local/v1/send", h.handleLocalSend)
	mux.HandleFunc("POST /local/v1/transfer/respond", h.handleTransferRespond)
	mux.HandleFunc("POST /local/v1/transfer/pause", h.handleTransferPause)
	mux.HandleFunc("POST /local/v1/transfer/cancel", h.handleTransferCancel)
	mux.HandleFunc("POST /local/v1/receive/cancel", h.handleReceiveCancel)
	mux.HandleFunc("POST /local/v1/diagnostics/ports", h.handleDiagnosePorts)
	mux.HandleFunc("GET /local/v1/events", h.broker.ServeHTTP)
}

func (h *Handler) handleGetSettings(w http.ResponseWriter, r *http.Request) {
	settings, err := h.settingsStore.Get(r.Context())
	if err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	writeJSON(w, http.StatusOK, settings)
}

func (h *Handler) handleSaveSettings(w http.ResponseWriter, r *http.Request) {
	var req model.AppSettings
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid settings body")
		return
	}

	curr, _ := h.settingsStore.Get(r.Context())
	if curr != nil {
		if req.DeviceID == "" {
			req.DeviceID = curr.DeviceID
		}
	}

	if err := h.settingsStore.Save(r.Context(), req); err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}

	if h.discovery != nil {
		myPeer := model.Peer{
			ID:          req.DeviceID,
			Name:        req.DeviceName,
			OS:          runtime.GOOS,
			Version:     constants.ProtocolVersion,
			Fingerprint: h.certMgr.Fingerprint,
			Port:        req.Port,
			IsOnline:    true,
		}
		h.discovery.UpdateLocalPeer(myPeer)
	}

	writeJSON(w, http.StatusOK, req)
}

func (h *Handler) handleStatus(w http.ResponseWriter, r *http.Request) {
	settings, _ := h.settingsStore.Get(r.Context())
	resp := map[string]any{
		"status":      "online",
		"os":          runtime.GOOS,
		"fingerprint": h.certMgr.Fingerprint,
		"activePort":  h.activePort,
		"settings":    settings,
	}
	writeJSON(w, http.StatusOK, resp)
}

func (h *Handler) handleGetTrusted(w http.ResponseWriter, r *http.Request) {
	peers, err := h.peersStore.GetAll(r.Context())
	if err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	writeJSON(w, http.StatusOK, peers)
}

func (h *Handler) handleSetBlocked(w http.ResponseWriter, r *http.Request) {
	var req struct {
		ID      string `json:"id"`
		Blocked bool   `json:"blocked"`
	}
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request")
		return
	}
	if err := h.peersStore.SetBlocked(r.Context(), req.ID, req.Blocked); err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
}

func (h *Handler) handleDeleteTrusted(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	if err := h.peersStore.Delete(r.Context(), id); err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
}

func (h *Handler) handleLocalPairInit(w http.ResponseWriter, r *http.Request) {
	var target model.Peer
	if err := json.NewDecoder(r.Body).Decode(&target); err != nil {
		writeError(w, http.StatusBadRequest, "invalid target peer")
		return
	}

	settings, _ := h.settingsStore.Get(r.Context())
	myInfo := model.Peer{
		ID:          settings.DeviceID,
		Name:        settings.DeviceName,
		OS:          runtime.GOOS,
		Fingerprint: h.certMgr.Fingerprint,
	}

	if err := h.client.InitiatePairing(r.Context(), target, myInfo); err != nil {
		writeError(w, http.StatusBadGateway, err.Error())
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"status": "awaiting_pin"})
}

func (h *Handler) handleLocalPairVerify(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Target model.Peer `json:"target"`
		Pin    string     `json:"pin"`
	}
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request")
		return
	}

	settings, _ := h.settingsStore.Get(r.Context())
	token, err := h.client.VerifyPairing(r.Context(), req.Target, settings.DeviceID, req.Pin)
	if err != nil {
		writeError(w, http.StatusUnauthorized, err.Error())
		return
	}

	trusted, err := h.pairingService.CompletePairing(r.Context(), req.Target, token)
	if err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}

	if h.broker != nil && trusted != nil {
		h.broker.Publish("peer_paired", trusted)
	}

	writeJSON(w, http.StatusOK, trusted)
}

func (h *Handler) handleLocalSend(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Target    model.Peer `json:"target"`
		FilePaths []string   `json:"filePaths"`
	}
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid send request")
		return
	}

	go func() {
		_, err := h.sendService.Execute(context.Background(), req.Target, req.FilePaths)
		if err != nil && h.broker != nil {
			h.broker.Publish("transfer_completed", map[string]any{
				"sessionId": "",
				"success":   false,
				"error":     err.Error(),
			})
		}
	}()

	writeJSON(w, http.StatusAccepted, map[string]string{"status": "queued"})
}

func (h *Handler) handleProbePeer(w http.ResponseWriter, r *http.Request) {
	var req struct {
		IP   string `json:"ip"`
		Port int    `json:"port"`
	}
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}
	if req.IP == "" {
		writeError(w, http.StatusBadRequest, "ip is required")
		return
	}

	var peer *model.Peer
	var err error
	if req.Port > 0 {
		peer, err = h.client.Probe(r.Context(), req.IP, req.Port)
	} else {
		for _, p := range constants.P2PFallbackPorts {
			probeCtx, cancel := context.WithTimeout(r.Context(), 1200*time.Millisecond)
			candidate, probeErr := h.client.Probe(probeCtx, req.IP, p)
			cancel()
			if probeErr == nil && candidate != nil {
				peer = candidate
				err = nil
				break
			}
			err = probeErr
		}
	}
	if err != nil || peer == nil {
		targetPort := req.Port
		if targetPort <= 0 {
			targetPort = constants.DefaultPort
		}
		writeError(w, http.StatusBadGateway, fmt.Sprintf("cannot reach node at %s:%d: %v", req.IP, targetPort, err))
		return
	}

	if settings, err := h.settingsStore.Get(r.Context()); err == nil && settings != nil && peer.ID == settings.DeviceID {
		writeError(w, http.StatusBadRequest, "cannot probe local device")
		return
	}

	if h.broker != nil {
		h.broker.Publish("peer_discovered", *peer)
	}

	writeJSON(w, http.StatusOK, peer)
}

func (h *Handler) handleTransferRespond(w http.ResponseWriter, r *http.Request) {
	var req struct {
		SessionID string `json:"sessionId"`
		Accepted  bool   `json:"accepted"`
	}
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}
	if req.SessionID == "" {
		writeError(w, http.StatusBadRequest, "sessionId is required")
		return
	}
	if h.EventSink == nil {
		writeError(w, http.StatusServiceUnavailable, "event sink unavailable")
		return
	}
	if !h.EventSink.RespondTransfer(req.SessionID, req.Accepted) {
		writeError(w, http.StatusGone, "transfer confirmation expired or no longer active")
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
}

func (h *Handler) handleTransferPause(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Paused bool `json:"paused"`
	}
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}
	if h.sendService == nil {
		writeError(w, http.StatusServiceUnavailable, "send service unavailable")
		return
	}
	h.sendService.SetPaused(req.Paused)
	writeJSON(w, http.StatusOK, map[string]any{"status": "ok", "paused": req.Paused})
}

func (h *Handler) handleTransferCancel(w http.ResponseWriter, _ *http.Request) {
	if h.sendService == nil {
		writeError(w, http.StatusServiceUnavailable, "send service unavailable")
		return
	}
	h.sendService.CancelCurrent()
	writeJSON(w, http.StatusOK, map[string]string{"status": "canceled"})
}

func (h *Handler) handleReceiveCancel(w http.ResponseWriter, _ *http.Request) {
	canceled := false
	if h.OnCancelIncoming != nil {
		canceled = h.OnCancelIncoming()
	}
	writeJSON(w, http.StatusOK, map[string]any{"status": "canceled", "active": canceled})
}

func (h *Handler) handleUpdateTrusted(w http.ResponseWriter, r *http.Request) {
	var req struct {
		ID                      string `json:"id"`
		CustomAlias             string `json:"customAlias"`
		Policy                  string `json:"policy"`
		DedicatedDownloadFolder string `json:"dedicatedDownloadFolder"`
	}
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}
	if req.ID == "" {
		writeError(w, http.StatusBadRequest, "id is required")
		return
	}
	peer, err := h.peersStore.GetByID(r.Context(), req.ID)
	if err != nil || peer == nil {
		writeError(w, http.StatusNotFound, "trusted peer not found")
		return
	}
	peer.CustomAlias = req.CustomAlias
	switch req.Policy {
	case string(model.PolicyAutoAccept), "auto":
		peer.Policy = model.PolicyAutoAccept
	case string(model.PolicyAskConfirmation), "ask":
		peer.Policy = model.PolicyAskConfirmation
	}
	peer.DedicatedDownloadFolder = req.DedicatedDownloadFolder

	if err := h.peersStore.Save(r.Context(), *peer); err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	writeJSON(w, http.StatusOK, peer)
}

func (h *Handler) handleDiagnosePorts(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Ports []int `json:"ports"`
	}
	_ = json.NewDecoder(r.Body).Decode(&req)

	ports := req.Ports
	if len(ports) == 0 {
		ports = constants.P2PFallbackPorts
	}
	if len(ports) > 30 {
		ports = ports[:30]
	}

	type portDiag struct {
		Port  int    `json:"port"`
		State string `json:"state"` // "active_self", "free", "occupied"
	}
	results := make([]portDiag, 0, len(ports))
	for _, p := range ports {
		if p <= 0 || p > 65535 {
			continue
		}
		if p == h.activePort {
			results = append(results, portDiag{Port: p, State: "active_self"})
			continue
		}
		ln, err := net.Listen("tcp", fmt.Sprintf("127.0.0.1:%d", p))
		if err == nil {
			_ = ln.Close()
			results = append(results, portDiag{Port: p, State: "free"})
		} else {
			results = append(results, portDiag{Port: p, State: "occupied"})
		}
	}

	fwStatus := "check_manual"
	if runtime.GOOS == "windows" {
		fwCtx, cancel := context.WithTimeout(r.Context(), 2*time.Second)
		cmd := exec.CommandContext(fwCtx, "netsh", "advfirewall", "firewall", "show", "rule", "name=CrossRelay")
		out, err := cmd.CombinedOutput()
		cancel()
		if err == nil && strings.Contains(strings.ToLower(string(out)), "crossrelay") {
			fwStatus = "allowed"
		} else {
			fwStatus = "unknown"
		}
	}

	writeJSON(w, http.StatusOK, map[string]any{
		"os":             runtime.GOOS,
		"activePort":     h.activePort,
		"firewallStatus": fwStatus,
		"ports":          results,
	})
}

func writeJSON(w http.ResponseWriter, statusCode int, data any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(statusCode)
	_ = json.NewEncoder(w).Encode(data)
}

func writeError(w http.ResponseWriter, statusCode int, message string) {
	writeJSON(w, statusCode, map[string]string{"error": message})
}
