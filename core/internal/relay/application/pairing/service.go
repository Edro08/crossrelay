package pairing

import (
	"context"
	"time"

	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/internal/relay/domain/ports"
	"github.com/crossrelay/core/kit/utils"
)

type Service struct {
	peersStore ports.TrustedPeersStore
}

func NewService(store ports.TrustedPeersStore) *Service {
	return &Service{peersStore: store}
}

// CompletePairing registers a newly verified remote peer into local trusted peers
func (s *Service) CompletePairing(ctx context.Context, requester model.Peer, pairingToken string) (*model.TrustedPeer, error) {
	if pairingToken == "" {
		var err error
		pairingToken, err = utils.GenerateRandomHex(32)
		if err != nil {
			return nil, err
		}
	}

	trusted := model.TrustedPeer{
		ID:              requester.ID,
		DeviceName:      requester.Name,
		OS:              requester.OS,
		CertFingerprint: requester.Fingerprint,
		PairingToken:    pairingToken,
		Policy:          model.PolicyAskConfirmation,
		IsBlocked:       false,
		PairedAt:        time.Now(),
		LastSeenAt:      time.Now(),
	}

	if err := s.peersStore.Save(ctx, trusted); err != nil {
		return nil, err
	}
	return &trusted, nil
}

// ValidatePairingToken checks if the peer is paired and token matches
func (s *Service) ValidatePairingToken(ctx context.Context, peerID string, token string) (*model.TrustedPeer, error) {
	peer, err := s.peersStore.GetByID(ctx, peerID)
	if err != nil {
		return nil, model.ErrPeerNotPaired
	}
	if peer.IsBlocked {
		return nil, model.ErrPeerBlocked
	}
	if peer.PairingToken == "" || peer.PairingToken != token {
		return nil, model.ErrInvalidPairingToken
	}
	return peer, nil
}
