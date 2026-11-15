//go:build windows

package discovery

import (
	"net"
	"os/exec"
	"strings"
	"syscall"
)

func getPlatformArpNeighbors() []string {
	cmd := exec.Command("arp", "-a")
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	out, err := cmd.Output()
	if err != nil {
		return nil
	}
	lines := strings.Split(string(out), "\n")
	var ips []string
	seen := make(map[string]bool)
	for _, line := range lines {
		fields := strings.Fields(line)
		if len(fields) < 2 {
			continue
		}
		mac := strings.ToLower(fields[1])
		if strings.Count(mac, "-") != 5 || mac == "ff-ff-ff-ff-ff-ff" || strings.HasPrefix(mac, "01-00-5e-") || strings.HasPrefix(mac, "33-33-") {
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
