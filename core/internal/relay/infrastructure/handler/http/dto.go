package http

// PingResponse payload for GET /api/v1/ping
type PingResponse struct {
	Status      string `json:"status"`
	ID          string `json:"id"`
	Name        string `json:"name"`
	OS          string `json:"os"`
	Version     string `json:"version"`
	Fingerprint string `json:"fingerprint"`
}

// RequesterDTO represents remote peer info in pair requests
type RequesterDTO struct {
	ID          string `json:"id"`
	Name        string `json:"name"`
	OS          string `json:"os"`
	Fingerprint string `json:"fingerprint"`
}

// PairInitRequest payload for POST /api/v1/pair/init
type PairInitRequest struct {
	Requester RequesterDTO `json:"requester"`
}

// PairInitResponse payload for POST /api/v1/pair/init
type PairInitResponse struct {
	Status           string `json:"status"`
	ExpiresInSeconds int    `json:"expiresInSeconds"`
}

// PairVerifyRequest payload for POST /api/v1/pair/verify
type PairVerifyRequest struct {
	RequesterID string `json:"requesterId"`
	EnteredPin  string `json:"enteredPin"`
}

// PairVerifyResponse payload for POST /api/v1/pair/verify
type PairVerifyResponse struct {
	Status       string `json:"status"`
	PairingToken string `json:"pairingToken"`
}

// ErrorResponse standard error payload
type ErrorResponse struct {
	Error             string `json:"error"`
	AttemptsRemaining int    `json:"attemptsRemaining,omitempty"`
}

// SessionUploadResponse payload for POST /api/v1/session/{sessionId}/upload/{fileId}
type SessionUploadResponse struct {
	FileID        string `json:"fileId"`
	Status        string `json:"status"`
	BytesReceived int64  `json:"bytesReceived"`
	Verified      bool   `json:"verified"`
}
