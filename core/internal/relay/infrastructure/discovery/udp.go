package discovery

import (
	"context"
	"encoding/json"
	"net"
	"strings"
	"sync"
	"time"

	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/internal/relay/domain/ports"
	"github.com/crossrelay/core/kit/constants"
)

type BeaconPayload struct {
	ID          string `json:"id"`
	Name        string `json:"name"`
	OS          string `json:"os"`
	Version     string `json:"ver"`
	Fingerprint string `json:"fp"`
	Port        int    `json:"port"`
}

type peerRoute struct {
	peer     model.Peer
	lastSeen time.Time
	score    int
}

// UDPDiscovery handles LAN peer discovery via UDP multicast/broadcast and TLS ping probes
type UDPDiscovery struct {
	mu              sync.Mutex
	port            int
	eventSink       ports.EventSink
	conn            *net.UDPConn
	broadcastIP     *net.UDPAddr
	stopChan        chan struct{}
	discoveredPeers sync.Map // id -> time.Time
	peerRoutes      map[string]map[string]peerRoute
	lastProbed      sync.Map // ip -> time.Time
	localPeer       model.Peer
	ProbeFunc       func(ctx context.Context, ip string, port int, local model.Peer) (*model.Peer, error)
}

func NewUDPDiscovery(eventSink ports.EventSink) *UDPDiscovery {
	return NewUDPDiscoveryWithPort(eventSink, constants.UdpFallbackPort)
}

func NewUDPDiscoveryWithPort(eventSink ports.EventSink, port int) *UDPDiscovery {
	return &UDPDiscovery{
		port:       port,
		eventSink:  eventSink,
		stopChan:   make(chan struct{}),
		peerRoutes: make(map[string]map[string]peerRoute),
	}
}

// Start begins advertising our node and listening for other peers
func (d *UDPDiscovery) Start(ctx context.Context, me model.Peer) error {
	d.mu.Lock()
	d.localPeer = me
	d.mu.Unlock()

	// Start cleanup and ARP/TLS discovery loops immediately so discovery works even if UDP bind is delayed or blocked
	go d.cleanupLoop()
	go d.arpProbeLoop()

	go func() {
		addr := &net.UDPAddr{
			Port: d.port,
			IP:   net.IPv4zero,
		}

		conn, err := net.ListenUDP("udp4", addr)
		if err != nil {
			return
		}

		d.mu.Lock()
		select {
		case <-d.stopChan:
			d.mu.Unlock()
			_ = conn.Close()
			return
		default:
		}
		d.conn = conn
		d.broadcastIP = &net.UDPAddr{
			IP:   net.IPv4bcast,
			Port: d.port,
		}
		d.mu.Unlock()

		// 1. Goroutine to listen for incoming beacons
		go d.listenLoop(me.ID, conn)

		// 2. Goroutine to periodically broadcast our beacon
		go d.broadcastLoop(me, conn)
	}()

	return nil
}

func (d *UDPDiscovery) Stop() error {
	d.mu.Lock()
	defer d.mu.Unlock()

	select {
	case <-d.stopChan:
		return nil
	default:
		close(d.stopChan)
	}

	if d.conn != nil {
		return d.conn.Close()
	}
	return nil
}

func (d *UDPDiscovery) UpdateLocalPeer(me model.Peer) {
	d.mu.Lock()
	defer d.mu.Unlock()
	d.localPeer = me
}

func (d *UDPDiscovery) RecordDiscoveredPeer(peer model.Peer) {
	if peer.ID == "" {
		return
	}
	now := time.Now()
	peer.IsOnline = true
	peer.LastSeenAt = now

	d.mu.Lock()
	myID := d.localPeer.ID
	if peer.ID == myID {
		d.mu.Unlock()
		return
	}
	best := d.upsertRouteLocked(peer, now, scoreRoute(peer.IP))
	d.mu.Unlock()

	d.discoveredPeers.Store(peer.ID, now)
	if d.eventSink != nil {
		d.eventSink.OnPeerDiscovered(best)
	}
}

func (d *UDPDiscovery) upsertRouteLocked(peer model.Peer, now time.Time, score int) model.Peer {
	if d.peerRoutes == nil {
		d.peerRoutes = make(map[string]map[string]peerRoute)
	}
	routes, ok := d.peerRoutes[peer.ID]
	if !ok {
		routes = make(map[string]peerRoute)
		d.peerRoutes[peer.ID] = routes
	}
	routes[peer.IP] = peerRoute{
		peer:     peer,
		lastSeen: now,
		score:    score,
	}
	return selectBestRoute(routes)
}

func selectBestRoute(routes map[string]peerRoute) model.Peer {
	var best peerRoute
	first := true
	for _, r := range routes {
		if first || r.score > best.score || (r.score == best.score && r.lastSeen.After(best.lastSeen)) {
			best = r
			first = false
		}
	}
	return best.peer
}

func scoreRoute(remoteIP string) int {
	parsed := net.ParseIP(strings.TrimSpace(remoteIP))
	if parsed == nil {
		return 1
	}
	v4 := parsed.To4()
	if v4 == nil {
		return 5
	}

	ifaces, err := net.Interfaces()
	if err != nil {
		return 10
	}
	for _, iface := range ifaces {
		if iface.Flags&net.FlagUp == 0 || iface.Flags&net.FlagLoopback != 0 {
			continue
		}
		addrs, err := iface.Addrs()
		if err != nil {
			continue
		}
		for _, a := range addrs {
			ipNet, ok := a.(*net.IPNet)
			if !ok || ipNet.IP.To4() == nil {
				continue
			}
			if ipNet.Contains(v4) {
				return ClassifyInterfaceScore(iface.Name, v4.IsLinkLocalUnicast())
			}
		}
	}
	return 10
}

func ClassifyInterfaceScore(name string, isLinkLocal bool) int {
	low := strings.ToLower(name)
	for _, virt := range []string{"vethernet", "default switch", "wsl", "docker", "virbr", "tun", "tap", "wg", "norton", "openvpn", "bluetooth", "tailscale", "zerotier", "vmware", "virtualbox"} {
		if strings.Contains(low, virt) {
			return 1
		}
	}
	for _, wifi := range []string{"wi-fi", "wifi", "wlan", "wlp"} {
		if strings.Contains(low, wifi) {
			if isLinkLocal {
				return 12
			}
			return 15
		}
	}
	for _, eth := range []string{"ethernet", "eth", "enp", "eno", "ens"} {
		if strings.Contains(low, eth) {
			if isLinkLocal {
				return 25
			}
			return 30
		}
	}
	if isLinkLocal {
		return 18
	}
	return 20
}

func (d *UDPDiscovery) NotifyRemoteIP(ip string) {
	cleanIP := strings.TrimSpace(ip)
	parsed := net.ParseIP(cleanIP)
	if parsed == nil || parsed.To4() == nil || parsed.IsLoopback() {
		return
	}
	if isOwnIP(cleanIP) {
		return
	}

	if last, ok := d.lastProbed.Load(cleanIP); ok && time.Since(last.(time.Time)) < 2500*time.Millisecond {
		return
	}
	d.lastProbed.Store(cleanIP, time.Now())

	go func(targetIP string) {
		d.mu.Lock()
		probe := d.ProbeFunc
		localPeer := d.localPeer
		d.mu.Unlock()
		if probe == nil {
			return
		}

		ctx, cancel := context.WithTimeout(context.Background(), 1500*time.Millisecond)
		defer cancel()

		peer, err := probe(ctx, targetIP, constants.DefaultPort, localPeer)
		if err != nil || peer == nil {
			return
		}

		d.RecordDiscoveredPeer(*peer)
	}(cleanIP)
}

func (d *UDPDiscovery) arpProbeLoop() {
	probeAll := func() {
		for _, ip := range getArpNeighborIPs() {
			d.NotifyRemoteIP(ip)
		}
	}

	probeAll()

	ticker := time.NewTicker(3 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-d.stopChan:
			return
		case <-ticker.C:
			probeAll()
		}
	}
}

func isOwnIP(target string) bool {
	ifaces, err := net.Interfaces()
	if err != nil {
		return false
	}
	for _, iface := range ifaces {
		addrs, err := iface.Addrs()
		if err != nil {
			continue
		}
		for _, a := range addrs {
			if ipNet, ok := a.(*net.IPNet); ok {
				if v4 := ipNet.IP.To4(); v4 != nil && v4.String() == target {
					return true
				}
			}
		}
	}
	return false
}

func getArpNeighborIPs() []string {
	return getPlatformArpNeighbors()
}

func (d *UDPDiscovery) broadcastLoop(me model.Peer, conn *net.UDPConn) {
	ticker := time.NewTicker(2 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-d.stopChan:
			return
		case <-ticker.C:
			if conn != nil {
				d.mu.Lock()
				currentPeer := d.localPeer
				d.mu.Unlock()

				payload := BeaconPayload{
					ID:          currentPeer.ID,
					Name:        currentPeer.Name,
					OS:          currentPeer.OS,
					Version:     constants.ProtocolVersion,
					Fingerprint: currentPeer.Fingerprint,
					Port:        currentPeer.Port,
				}
				data, _ := json.Marshal(payload)

				targets := getAllBroadcastAddrs(d.port)
				for _, target := range targets {
					_, _ = conn.WriteToUDP(data, target)
				}
			}
		}
	}
}

func getAllBroadcastAddrs(port int) []*net.UDPAddr {
	var targets []*net.UDPAddr
	seen := make(map[string]bool)

	add := func(ip net.IP) {
		if ip == nil {
			return
		}
		key := ip.String()
		if !seen[key] {
			seen[key] = true
			targets = append(targets, &net.UDPAddr{IP: ip, Port: port})
		}
	}

	// 1. Global IPv4 broadcast
	add(net.IPv4bcast)

	// 2. Local Multicast (SSDP / LAN Multicast)
	add(net.IPv4(239, 255, 255, 250))

	// 3. Subnet broadcast for each active non-loopback network interface
	ifaces, err := net.Interfaces()
	if err == nil {
		for _, iface := range ifaces {
			if iface.Flags&net.FlagUp == 0 || iface.Flags&net.FlagLoopback != 0 {
				continue
			}
			addrs, err := iface.Addrs()
			if err != nil {
				continue
			}
			for _, a := range addrs {
				ipNet, ok := a.(*net.IPNet)
				if !ok {
					continue
				}
				v4 := ipNet.IP.To4()
				if v4 == nil {
					continue
				}
				mask := ipNet.Mask
				if len(mask) == 4 {
					bcast := net.IPv4(
						v4[0]|^mask[0],
						v4[1]|^mask[1],
						v4[2]|^mask[2],
						v4[3]|^mask[3],
					)
					add(bcast)
				}
			}
		}
	}

	// 4. Unicast directly to ARP neighbors (e.g., WSL2 -> Windows host)
	for _, ip := range getArpNeighborIPs() {
		if parsed := net.ParseIP(ip); parsed != nil {
			add(parsed.To4())
		}
	}

	return targets
}

func (d *UDPDiscovery) listenLoop(myID string, conn *net.UDPConn) {
	buf := make([]byte, 4096)
	for {
		select {
		case <-d.stopChan:
			return
		default:
		}

		n, remoteAddr, err := conn.ReadFromUDP(buf)
		if err != nil {
			return // conn closed
		}

		var beacon BeaconPayload
		if err := json.Unmarshal(buf[:n], &beacon); err != nil {
			continue
		}

		// Ignore our own beacon
		if beacon.ID == myID || beacon.ID == "" {
			continue
		}

		peer := model.Peer{
			ID:          beacon.ID,
			Name:        beacon.Name,
			OS:          beacon.OS,
			Version:     beacon.Version,
			Fingerprint: beacon.Fingerprint,
			IP:          remoteAddr.IP.String(),
			Port:        beacon.Port,
			IsOnline:    true,
			LastSeenAt:  time.Now(),
		}
		d.RecordDiscoveredPeer(peer)
	}
}

func (d *UDPDiscovery) cleanupLoop() {
	ticker := time.NewTicker(5 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-d.stopChan:
			return
		case <-ticker.C:
			now := time.Now()
			var updated []model.Peer
			var lost []string

			d.mu.Lock()
			for id, routes := range d.peerRoutes {
				removed := false
				for ip, r := range routes {
					if now.Sub(r.lastSeen) > 8*time.Second {
						delete(routes, ip)
						removed = true
					}
				}
				if len(routes) == 0 {
					delete(d.peerRoutes, id)
					d.discoveredPeers.Delete(id)
					lost = append(lost, id)
				} else if removed {
					updated = append(updated, selectBestRoute(routes))
				}
			}
			d.mu.Unlock()

			if d.eventSink != nil {
				for _, p := range updated {
					d.eventSink.OnPeerDiscovered(p)
				}
				for _, id := range lost {
					d.eventSink.OnPeerLost(id)
				}
			}
		}
	}
}
