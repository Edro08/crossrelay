package model

import "errors"

var (
	ErrPeerNotFound       = errors.New("peer not found")
	ErrPeerBlocked        = errors.New("peer is blocked")
	ErrPeerNotPaired      = errors.New("peer is not paired (pairing required)")
	ErrInvalidPairingToken = errors.New("invalid or revoked pairing token")
	ErrInvalidPin         = errors.New("invalid PIN entered")
	ErrPinExpired         = errors.New("pairing PIN expired")
	ErrPinMaxAttempts     = errors.New("maximum PIN attempts exceeded")
	ErrTransferRejected   = errors.New("transfer rejected by remote peer")
	ErrSessionBusy        = errors.New("receiver is busy with another transfer")
	ErrHashMismatch       = errors.New("file SHA-256 verification mismatch")
)
