package model

import "time"

// TransferDirection specifies outgoing or incoming
type TransferDirection string

const (
	DirectionOutgoing TransferDirection = "outgoing"
	DirectionIncoming TransferDirection = "incoming"
)

// TransferStatus represents the current state of a transfer session
type TransferStatus string

const (
	StatusPending    TransferStatus = "pending"
	StatusInProgress TransferStatus = "in_progress"
	StatusCompleted  TransferStatus = "completed"
	StatusFailed     TransferStatus = "failed"
	StatusCancelled  TransferStatus = "cancelled"
)

// FileItem represents a single file inside a transfer manifest
type FileItem struct {
	FileID   string `json:"fileId"`
	Name     string `json:"name"`
	Path     string `json:"path,omitempty"` // Local path on sender/receiver
	Size     int64  `json:"size"`
	MimeType string `json:"mimeType"`
	SHA256   string `json:"sha256"`
}

// TransferManifest is the payload sent during POST /api/v1/session/request
type TransferManifest struct {
	SessionID            string     `json:"sessionId"`
	SenderID             string     `json:"senderId"`
	SenderName           string     `json:"senderName"`
	TotalFiles           int        `json:"totalFiles"`
	TotalBytes           int64      `json:"totalBytes"`
	RequiresConfirmation bool       `json:"requiresConfirmation"`
	Files                []FileItem `json:"files"`
}

// Session represents an active or past transfer session
type Session struct {
	ID            string            `json:"id"`
	Direction     TransferDirection `json:"direction"`
	RemotePeerID  string            `json:"remotePeerId"`
	RemotePeerName string           `json:"remotePeerName"`
	Manifest      TransferManifest  `json:"manifest"`
	BytesTransferred int64          `json:"bytesTransferred"`
	Status        TransferStatus    `json:"status"`
	ErrorMessage  string            `json:"errorMessage,omitempty"`
	CreatedAt     time.Time         `json:"createdAt"`
	CompletedAt   *time.Time        `json:"completedAt,omitempty"`
}

// HistoryEntry represents a record in transfer history
type HistoryEntry struct {
	ID             string            `json:"id"`
	Direction      TransferDirection `json:"direction"`
	RemoteDevice   string            `json:"remoteDevice"`
	FileCount      int               `json:"fileCount"`
	TotalBytes     int64             `json:"totalBytes"`
	Status         TransferStatus    `json:"status"`
	Timestamp      time.Time         `json:"timestamp"`
	DownloadFolder string            `json:"downloadFolder,omitempty"`
}
