package model

import "time"

// PinChallenge represents an ongoing zero-trust pairing challenge on the receiver
type PinChallenge struct {
	RequesterID   string    `json:"requesterId"`
	RequesterName string    `json:"requesterName"`
	RequesterOS   string    `json:"requesterOs"`
	RequesterFP   string    `json:"requesterFp"`
	PinCode       string    `json:"pinCode"`
	Attempts      int       `json:"attempts"`
	ExpiresAt     time.Time `json:"expiresAt"`
}

func (p *PinChallenge) IsExpired() bool {
	return time.Now().After(p.ExpiresAt)
}
