package app

import (
	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/kit/constants"
)

// ResolveCandidatePorts returns the ordered P2P TLS ports to try for the given settings.
func ResolveCandidatePorts(s *model.AppSettings) []int {
	if s == nil {
		return constants.P2PFallbackPorts
	}
	switch s.PortMode {
	case "fixed":
		if s.Port > 0 && s.Port <= 65535 {
			return []int{s.Port}
		}
	case "range":
		start := s.Port
		end := s.PortRangeEnd
		if start <= 0 || start > 65535 {
			start = constants.DefaultPort
		}
		if end < start {
			end = start
		}
		if end-start >= 30 {
			end = start + 29
		}
		ports := make([]int, 0, end-start+1)
		for p := start; p <= end; p++ {
			ports = append(ports, p)
		}
		return ports
	}
	return constants.P2PFallbackPorts
}
