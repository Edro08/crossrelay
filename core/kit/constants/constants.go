package constants

import "time"

const (
	ProtocolVersion  = "1"
	ProtocolService  = "_crossrelay._tcp.local."
	DefaultPort      = 4242
	UdpFallbackPort  = 4243
	ControlPortStart = 14242
	ControlPortEnd   = 14250

	DefaultChunkSize = 512 * 1024       // 512 KB
	MinChunkSize     = 64 * 1024        // 64 KB
	MaxChunkSize     = 1024 * 1024      // 1 MB

	PinLength           = 6
	PinExpirationSec    = 60
	MaxPinAttempts      = 3
	PinCooldownDuration = 5 * time.Minute

	DefaultPingTimeout     = 3 * time.Second
	DefaultPairInitTimeout = 10 * time.Second

	PartFileSuffix = ".crossrelay.part"
)

// P2PFallbackPorts defines the primary P2P TLS 1.3 port (4242) followed by backup ports (4244..4248),
// keeping UDP discovery fixed on port 4243.
var P2PFallbackPorts = []int{4242, 4244, 4245, 4246, 4247, 4248}
