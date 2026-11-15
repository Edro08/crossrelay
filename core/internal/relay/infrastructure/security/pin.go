package security

import (
	"crypto/rand"
	"crypto/subtle"
	"fmt"
	"math/big"
	"sync"
	"time"

	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/kit/constants"
)

// PinManager handles active pairing challenges with rate-limiting
type PinManager struct {
	mu         sync.Mutex
	activePin  *model.PinChallenge
	cooldowns  map[string]time.Time // IP / RequesterID -> Cooldown end time
}

func NewPinManager() *PinManager {
	return &PinManager{
		cooldowns: make(map[string]time.Time),
	}
}

// GeneratePin creates a new 6-digit random PIN challenge
func (pm *PinManager) GeneratePin(requester model.Peer) (*model.PinChallenge, error) {
	pm.mu.Lock()
	defer pm.mu.Unlock()

	// Prune expired cooldowns and check active cooldown
	now := time.Now()
	for id, until := range pm.cooldowns {
		if !now.Before(until) {
			delete(pm.cooldowns, id)
		}
	}
	if until, ok := pm.cooldowns[requester.ID]; ok && now.Before(until) {
		return nil, model.ErrPinMaxAttempts
	}

	maxLimit := big.NewInt(1000000)
	n, err := rand.Int(rand.Reader, maxLimit)
	if err != nil {
		return nil, err
	}
	pinStr := fmt.Sprintf("%06d", n.Int64())

	challenge := &model.PinChallenge{
		RequesterID:   requester.ID,
		RequesterName: requester.Name,
		RequesterOS:   requester.OS,
		RequesterFP:   requester.Fingerprint,
		PinCode:       pinStr,
		Attempts:      0,
		ExpiresAt:     time.Now().Add(constants.PinExpirationSec * time.Second),
	}

	pm.activePin = challenge
	return challenge, nil
}

// VerifyPin validates an entered PIN from a requester and returns the verified challenge on success
func (pm *PinManager) VerifyPin(requesterID string, enteredPin string) (*model.PinChallenge, error) {
	pm.mu.Lock()
	defer pm.mu.Unlock()

	if pm.activePin == nil {
		return nil, model.ErrPinExpired
	}

	if pm.activePin.IsExpired() {
		pm.activePin = nil
		return nil, model.ErrPinExpired
	}

	if pm.activePin.RequesterID != "" && requesterID != pm.activePin.RequesterID {
		return nil, model.ErrInvalidPin
	}

	pm.activePin.Attempts++

	if subtle.ConstantTimeCompare([]byte(pm.activePin.PinCode), []byte(enteredPin)) != 1 {
		if pm.activePin.Attempts >= constants.MaxPinAttempts {
			// Trigger cooldown
			if requesterID != "" {
				pm.cooldowns[requesterID] = time.Now().Add(constants.PinCooldownDuration)
			}
			pm.activePin = nil
			return nil, model.ErrPinMaxAttempts
		}
		return nil, model.ErrInvalidPin
	}

	// Success, return challenge and clear active
	challenge := pm.activePin
	pm.activePin = nil
	return challenge, nil
}
