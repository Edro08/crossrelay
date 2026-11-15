package send

import (
	"context"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"sync"
	"time"

	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/internal/relay/domain/ports"
	"github.com/crossrelay/core/kit/utils"
)

type Service struct {
	peersStore    ports.TrustedPeersStore
	settingsStore ports.SettingsStore
	transmitter   ports.PeerTransmitter
	eventSink     ports.EventSink

	mu           sync.Mutex
	cond         *sync.Cond
	paused       bool
	pauseStarted time.Time
	pausedTotal  time.Duration
	cancelFn     context.CancelFunc
}

func NewService(store ports.TrustedPeersStore, settingsStore ports.SettingsStore, transmitter ports.PeerTransmitter, eventSink ports.EventSink) *Service {
	s := &Service{
		peersStore:    store,
		settingsStore: settingsStore,
		transmitter:   transmitter,
		eventSink:     eventSink,
	}
	s.cond = sync.NewCond(&s.mu)
	return s
}

func (s *Service) ensureCondLocked() {
	if s.cond == nil {
		s.cond = sync.NewCond(&s.mu)
	}
}

// SetPaused pauses or resumes the active outgoing transfer stream.
func (s *Service) SetPaused(paused bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.ensureCondLocked()
	if s.paused == paused {
		return
	}
	if paused {
		s.paused = true
		s.pauseStarted = time.Now()
		return
	}
	if !s.pauseStarted.IsZero() {
		s.pausedTotal += time.Since(s.pauseStarted)
		s.pauseStarted = time.Time{}
	}
	s.paused = false
	s.cond.Broadcast()
}

// IsPaused reports whether the outgoing transfer stream is currently paused.
func (s *Service) IsPaused() bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.paused
}

// CancelCurrent aborts the active outgoing transfer session if one is running.
func (s *Service) CancelCurrent() {
	s.mu.Lock()
	s.ensureCondLocked()
	s.paused = false
	s.pauseStarted = time.Time{}
	cancel := s.cancelFn
	s.cond.Broadcast()
	s.mu.Unlock()

	if cancel != nil {
		cancel()
	}
}

func (s *Service) waitIfPaused(ctx context.Context) error {
	s.mu.Lock()
	s.ensureCondLocked()
	for s.paused && ctx.Err() == nil {
		s.cond.Wait()
	}
	s.mu.Unlock()
	return ctx.Err()
}

func (s *Service) activeElapsed(startedAt time.Time) time.Duration {
	s.mu.Lock()
	pausedTotal := s.pausedTotal
	if s.paused && !s.pauseStarted.IsZero() {
		pausedTotal += time.Since(s.pauseStarted)
	}
	s.mu.Unlock()

	elapsed := time.Since(startedAt) - pausedTotal
	if elapsed < 0 {
		return 0
	}
	return elapsed
}

type pausableReader struct {
	ctx context.Context
	svc *Service
	r   io.Reader
}

func (pr *pausableReader) Read(p []byte) (int, error) {
	if err := pr.svc.waitIfPaused(pr.ctx); err != nil {
		return 0, err
	}
	if len(p) > 64*1024 {
		p = p[:64*1024]
	}
	n, err := pr.r.Read(p)
	if ctxErr := pr.ctx.Err(); ctxErr != nil {
		return n, ctxErr
	}
	return n, err
}

// Execute validates zero-trust pairing and transfers the batch of files
func (s *Service) Execute(ctx context.Context, targetPeer model.Peer, filePaths []string) (*model.Session, error) {
	ctx, cancel := context.WithCancel(ctx)
	s.mu.Lock()
	s.ensureCondLocked()
	s.cancelFn = cancel
	s.paused = false
	s.pauseStarted = time.Time{}
	s.pausedTotal = 0
	s.mu.Unlock()

	stopWake := context.AfterFunc(ctx, func() {
		s.mu.Lock()
		s.ensureCondLocked()
		s.cond.Broadcast()
		s.mu.Unlock()
	})
	defer func() {
		stopWake()
		cancel()
		s.mu.Lock()
		s.cancelFn = nil
		s.paused = false
		s.pauseStarted = time.Time{}
		s.mu.Unlock()
	}()
	// 1. Zero-Trust check: Target must be in local trusted store
	trusted, err := s.peersStore.GetByID(ctx, targetPeer.ID)
	if err != nil {
		return nil, model.ErrPeerNotPaired
	}
	if trusted.IsBlocked {
		return nil, model.ErrPeerBlocked
	}
	if trusted.PairingToken == "" {
		return nil, model.ErrInvalidPairingToken
	}

	if targetPeer.Fingerprint == "" && trusted.CertFingerprint != "" {
		targetPeer.Fingerprint = trusted.CertFingerprint
	}

	// 2. Build manifest using local settings identity
	senderID := "local-device"
	senderName := "Local Device"
	if s.settingsStore != nil {
		if settings, err := s.settingsStore.Get(ctx); err == nil && settings != nil {
			if settings.DeviceID != "" {
				senderID = settings.DeviceID
			}
			if settings.DeviceName != "" {
				senderName = settings.DeviceName
			}
		}
	}

	manifest := model.TransferManifest{
		SessionID:  utils.GenerateUUIDv4(),
		SenderID:   senderID,
		SenderName: senderName,
		Files:      make([]model.FileItem, 0, len(filePaths)),
	}

	for _, entry := range collectUploadEntries(filePaths) {
		if err := s.waitIfPaused(ctx); err != nil {
			return nil, err
		}
		f, err := os.Open(entry.localPath)
		if err != nil {
			continue
		}
		hash, hashErr := utils.ComputeReaderSHA256(&pausableReader{ctx: ctx, svc: s, r: f})
		_ = f.Close()
		if hashErr != nil && ctx.Err() != nil {
			return nil, ctx.Err()
		}

		manifest.Files = append(manifest.Files, model.FileItem{
			FileID:   utils.GenerateUUIDv4(),
			Name:     entry.manifestName,
			Path:     entry.localPath,
			Size:     entry.size,
			MimeType: "application/octet-stream",
			SHA256:   hash,
		})
		manifest.TotalBytes += entry.size
	}
	manifest.TotalFiles = len(manifest.Files)
	if manifest.TotalFiles == 0 {
		return nil, fmt.Errorf("no readable files found to send")
	}

	// 3. Propose session
	accepted, err := s.transmitter.RequestTransfer(ctx, targetPeer, trusted.PairingToken, manifest)
	if err != nil {
		return nil, err
	}
	if !accepted {
		return nil, model.ErrTransferRejected
	}

	// 4. Stream each file
	startedAt := time.Now()
	lastReport := time.Time{}
	var totalTransferred int64
	for _, fi := range manifest.Files {
		if err := s.waitIfPaused(ctx); err != nil {
			return nil, err
		}
		f, err := os.Open(fi.Path)
		if err != nil {
			return nil, err
		}

		pr := &pausableReader{ctx: ctx, svc: s, r: f}
		err = s.transmitter.StreamFile(ctx, targetPeer, trusted.PairingToken, manifest.SessionID, fi, pr, func(written int64) {
			totalTransferred += written
			if s.eventSink == nil {
				return
			}
			now := time.Now()
			if now.Sub(lastReport) < 150*time.Millisecond && totalTransferred < manifest.TotalBytes {
				return
			}
			lastReport = now
			var speedBps int64
			if !s.IsPaused() {
				if elapsed := s.activeElapsed(startedAt).Seconds(); elapsed > 0 {
					speedBps = int64(float64(totalTransferred) / elapsed)
				}
			}
			s.eventSink.OnTransferProgress(manifest.SessionID, totalTransferred, manifest.TotalBytes, speedBps)
		})
		_ = f.Close()
		if err != nil {
			return nil, err
		}
	}

	if s.eventSink != nil {
		s.eventSink.OnTransferCompleted(manifest.SessionID, true, "")
	}

	return &model.Session{
		ID:           manifest.SessionID,
		Direction:    model.DirectionOutgoing,
		RemotePeerID: targetPeer.ID,
		Manifest:     manifest,
		Status:       model.StatusCompleted,
	}, nil
}

type uploadEntry struct {
	localPath    string
	manifestName string
	size         int64
}

// collectUploadEntries expands files and directories recursively using forward-slash relative names.
func collectUploadEntries(paths []string) []uploadEntry {
	var entries []uploadEntry
	for _, raw := range paths {
		cleanPath := filepath.Clean(raw)
		info, err := os.Stat(cleanPath)
		if err != nil {
			continue
		}
		if !info.IsDir() {
			entries = append(entries, uploadEntry{
				localPath:    cleanPath,
				manifestName: info.Name(),
				size:         info.Size(),
			})
			continue
		}

		parentDir := filepath.Dir(cleanPath)
		_ = filepath.WalkDir(cleanPath, func(walkPath string, d fs.DirEntry, walkErr error) error {
			if walkErr != nil || d.IsDir() || !d.Type().IsRegular() {
				return nil
			}
			fInfo, err := d.Info()
			if err != nil {
				return nil
			}
			rel, err := filepath.Rel(parentDir, walkPath)
			if err != nil {
				return nil
			}
			entries = append(entries, uploadEntry{
				localPath:    walkPath,
				manifestName: filepath.ToSlash(rel),
				size:         fInfo.Size(),
			})
			return nil
		})
	}
	return entries
}
