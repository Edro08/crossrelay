package model

type VisibilityMode string

const (
	VisibilityEveryone VisibilityMode = "everyone"
	VisibilityPaired   VisibilityMode = "paired"
	VisibilityHidden   VisibilityMode = "hidden"
)

type SpeedLimitMBps int

const (
	SpeedLimitUnlimited SpeedLimitMBps = 0
	SpeedLimit100MB     SpeedLimitMBps = 100
	SpeedLimit50MB      SpeedLimitMBps = 50
	SpeedLimit10MB      SpeedLimitMBps = 10
)

// AppSettings represents local configuration
type AppSettings struct {
	DeviceID          string         `json:"deviceId"`
	DeviceName        string         `json:"deviceName"`
	Port              int            `json:"port"`
	PortMode          string         `json:"portMode,omitempty"`     // "auto", "fixed", "range"
	PortRangeEnd      int            `json:"portRangeEnd,omitempty"` // end of custom port range
	DuplicatePolicy   string         `json:"duplicatePolicy,omitempty"` // "rename", "overwrite", "skip"
	DownloadDirectory string         `json:"downloadDirectory"`
	Visibility        VisibilityMode `json:"visibility"`
	SpeedLimit        SpeedLimitMBps `json:"speedLimit"`
}
