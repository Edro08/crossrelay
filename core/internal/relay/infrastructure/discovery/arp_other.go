//go:build !windows

package discovery

import (
	"net"
	"os"
	"strings"
)

func getPlatformArpNeighbors() []string {
	data, err := os.ReadFile("/proc/net/arp")
	if err != nil {
		return nil
	}
	lines := strings.Split(string(data), "\n")
	var ips []string
	seen := make(map[string]bool)
	for i, line := range lines {
		if i == 0 {
			continue
		}
		fields := strings.Fields(line)
		if len(fields) < 3 || fields[2] == "0x0" {
			continue
		}
		parsed := net.ParseIP(fields[0])
		if parsed == nil {
			continue
		}
		v4 := parsed.To4()
		if v4 == nil || v4.IsLoopback() || v4.IsMulticast() || v4[3] == 255 {
			continue
		}
		s := v4.String()
		if !seen[s] {
			seen[s] = true
			ips = append(ips, s)
		}
	}
	return ips
}
