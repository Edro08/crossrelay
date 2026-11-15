package storage

import (
	"context"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"sync"
	"time"

	"github.com/crossrelay/core/internal/relay/domain/model"
)

// FileTrustedPeersStore persists trusted peers to trusted_peers.json
type FileTrustedPeersStore struct {
	mu       sync.RWMutex
	filePath string
}

func NewFileTrustedPeersStore(baseDir string) *FileTrustedPeersStore {
	return &FileTrustedPeersStore{
		filePath: filepath.Join(baseDir, "core_trusted_peers.json"),
	}
}

func (s *FileTrustedPeersStore) GetAll(ctx context.Context) ([]model.TrustedPeer, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()

	data, err := os.ReadFile(s.filePath)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return []model.TrustedPeer{}, nil
		}
		return nil, err
	}

	var peers []model.TrustedPeer
	if err := json.Unmarshal(data, &peers); err != nil {
		return []model.TrustedPeer{}, nil
	}
	return peers, nil
}

func (s *FileTrustedPeersStore) GetByID(ctx context.Context, id string) (*model.TrustedPeer, error) {
	peers, err := s.GetAll(ctx)
	if err != nil {
		return nil, err
	}
	for _, p := range peers {
		if p.ID == id {
			return &p, nil
		}
	}
	return nil, model.ErrPeerNotFound
}

func (s *FileTrustedPeersStore) Save(ctx context.Context, peer model.TrustedPeer) error {
	s.mu.Lock()
	defer s.mu.Unlock()

	peers, _ := s.loadUnlocked()
	found := false
	for i, p := range peers {
		if p.ID == peer.ID {
			peers[i] = peer
			found = true
			break
		}
	}
	if !found {
		peers = append(peers, peer)
	}

	return s.saveUnlocked(peers)
}

func (s *FileTrustedPeersStore) Delete(ctx context.Context, id string) error {
	s.mu.Lock()
	defer s.mu.Unlock()

	peers, _ := s.loadUnlocked()
	var updated []model.TrustedPeer
	for _, p := range peers {
		if p.ID != id {
			updated = append(updated, p)
		}
	}
	return s.saveUnlocked(updated)
}

func (s *FileTrustedPeersStore) IsBlocked(ctx context.Context, id string) (bool, error) {
	p, err := s.GetByID(ctx, id)
	if err != nil {
		return false, nil
	}
	return p.IsBlocked, nil
}

func (s *FileTrustedPeersStore) SetBlocked(ctx context.Context, id string, blocked bool) error {
	p, err := s.GetByID(ctx, id)
	if err != nil {
		return err
	}
	p.IsBlocked = blocked
	return s.Save(ctx, *p)
}

func (s *FileTrustedPeersStore) loadUnlocked() ([]model.TrustedPeer, error) {
	data, err := os.ReadFile(s.filePath)
	if err != nil {
		return []model.TrustedPeer{}, nil
	}
	var peers []model.TrustedPeer
	_ = json.Unmarshal(data, &peers)
	return peers, nil
}

func (s *FileTrustedPeersStore) saveUnlocked(peers []model.TrustedPeer) error {
	_ = os.MkdirAll(filepath.Dir(s.filePath), 0755)
	data, err := json.MarshalIndent(peers, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(s.filePath, data, 0644)
}

// FileSettingsStore persists app_settings.json
type FileSettingsStore struct {
	mu       sync.RWMutex
	filePath string
	cached   *model.AppSettings
}

func NewFileSettingsStore(baseDir string) *FileSettingsStore {
	return &FileSettingsStore{
		filePath: filepath.Join(baseDir, "core_settings.json"),
	}
}

func (s *FileSettingsStore) Get(ctx context.Context) (*model.AppSettings, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	if s.cached != nil {
		return s.cached, nil
	}

	data, err := os.ReadFile(s.filePath)
	if err == nil {
		var settings model.AppSettings
		if err := json.Unmarshal(data, &settings); err == nil && settings.DeviceID != "" {
			s.cached = &settings
			return s.cached, nil
		}
	}

	// First time initialization: generate unique and permanent device settings
	host, _ := os.Hostname()
	if host == "" {
		host = "CrossRelay Device"
	}
	s.cached = &model.AppSettings{
		DeviceID:          "device-" + time.Now().Format("20060102150405"),
		DeviceName:        host,
		Port:              4242,
		PortMode:          "auto",
		PortRangeEnd:      4248,
		DuplicatePolicy:   "rename",
		DownloadDirectory: "",
		Visibility:        model.VisibilityEveryone,
		SpeedLimit:        model.SpeedLimitUnlimited,
	}

	// Persist immediately so DeviceID never changes across calls or restarts
	_ = os.MkdirAll(filepath.Dir(s.filePath), 0755)
	if serialized, err := json.MarshalIndent(s.cached, "", "  "); err == nil {
		_ = os.WriteFile(s.filePath, serialized, 0644)
	}

	return s.cached, nil
}

func (s *FileSettingsStore) Save(ctx context.Context, settings model.AppSettings) error {
	s.mu.Lock()
	defer s.mu.Unlock()

	s.cached = &settings
	_ = os.MkdirAll(filepath.Dir(s.filePath), 0755)
	data, err := json.MarshalIndent(settings, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(s.filePath, data, 0644)
}
