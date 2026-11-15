package model

import "time"

// TransferPolicy indicates how transfers from a trusted peer are received
type TransferPolicy string

const (
	PolicyAutoAccept      TransferPolicy = "AutoAccept"
	PolicyAskConfirmation TransferPolicy = "AskConfirmation"
)

// TrustState represents the pairing status of a peer
type TrustState string

const (
	TrustStateUnpaired TrustState = "unpaired"
	TrustStatePaired   TrustState = "paired"
	TrustStateBlocked  TrustState = "blocked"
)

// Peer represents another CrossRelay node in the LAN
type Peer struct {
	ID          string     `json:"id"`
	Name        string     `json:"name"`
	OS          string     `json:"os"`
	Version     string     `json:"version"`
	Fingerprint string     `json:"fingerprint"` // SHA-256 fingerprint of peer's TLS cert
	IP          string     `json:"ip"`
	Port        int        `json:"port"`
	IsOnline    bool       `json:"isOnline"`
	TrustState  TrustState `json:"trustState"`
	LatencyMs   int64      `json:"latencyMs"`
	LastSeenAt  time.Time  `json:"lastSeenAt"`
}

// TrustedPeer represents a persistently paired device
type TrustedPeer struct {
	ID                     string         `json:"id"`
	DeviceName             string         `json:"deviceName"`
	CustomAlias            string         `json:"customAlias,omitempty"`
	OS                     string         `json:"os"`
	CertFingerprint        string         `json:"certFingerprint"`
	PairingToken           string         `json:"pairingToken"`
	Policy                 TransferPolicy `json:"policy"`
	DedicatedDownloadFolder string        `json:"dedicatedDownloadFolder,omitempty"`
	IsBlocked              bool           `json:"isBlocked"`
	PairedAt               time.Time      `json:"pairedAt"`
	LastSeenAt             time.Time      `json:"lastSeenAt"`

	// Metrics
	TotalBytesSent     int64 `json:"totalBytesSent"`
	TotalBytesReceived int64 `json:"totalBytesReceived"`
	FilesTransferred   int   `json:"filesTransferred"`
}

func (tp *TrustedPeer) DisplayName() string {
	if tp.CustomAlias != "" {
		return tp.CustomAlias
	}
	return tp.DeviceName
}
