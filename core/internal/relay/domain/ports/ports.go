package ports

import (
	"context"
	"io"

	"github.com/crossrelay/core/internal/relay/domain/model"
)

// TrustedPeersStore manages persistent storage of paired devices
type TrustedPeersStore interface {
	GetAll(ctx context.Context) ([]model.TrustedPeer, error)
	GetByID(ctx context.Context, id string) (*model.TrustedPeer, error)
	Save(ctx context.Context, peer model.TrustedPeer) error
	Delete(ctx context.Context, id string) error
	IsBlocked(ctx context.Context, id string) (bool, error)
	SetBlocked(ctx context.Context, id string, blocked bool) error
}

// SettingsStore manages persistent application settings
type SettingsStore interface {
	Get(ctx context.Context) (*model.AppSettings, error)
	Save(ctx context.Context, settings model.AppSettings) error
}

// HistoryStore manages transfer history logs
type HistoryStore interface {
	GetAll(ctx context.Context) ([]model.HistoryEntry, error)
	Add(ctx context.Context, entry model.HistoryEntry) error
	Clear(ctx context.Context) error
}

// EventSink pushes real-time notifications to UI (Desktop C# or Mobile Kotlin)
type EventSink interface {
	OnPeerDiscovered(peer model.Peer)
	OnPeerLost(peerID string)
	OnPeerPaired(peer model.TrustedPeer)
	OnPinChallengeReceived(challenge model.PinChallenge)
	OnIncomingTransferRequest(manifest model.TransferManifest) (bool, error)
	OnTransferProgress(sessionID string, bytesTransferred int64, totalBytes int64, speedBps int64)
	OnTransferCompleted(sessionID string, success bool, errMsg string)
}

// PeerTransmitter defines outbound network calls to remote peers
type PeerTransmitter interface {
	Ping(ctx context.Context, peer model.Peer) (int64, error)
	InitiatePairing(ctx context.Context, peer model.Peer, myInfo model.Peer) error
	VerifyPairing(ctx context.Context, peer model.Peer, myID string, pin string) (string, error)
	RequestTransfer(ctx context.Context, peer model.Peer, token string, manifest model.TransferManifest) (bool, error)
	StreamFile(ctx context.Context, peer model.Peer, token string, sessionID string, file model.FileItem, reader io.Reader, onProgress func(written int64)) error
}

// LocalDiscovery defines mDNS/UDP service advertisement and scanning
type LocalDiscovery interface {
	Start(ctx context.Context, me model.Peer) error
	Stop() error
}
