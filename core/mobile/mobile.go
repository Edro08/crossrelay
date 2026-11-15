package mobile

import (
	"context"
	"errors"
	"fmt"
	"net"
	"net/http"
	"os"
	"sync"
	"time"

	"github.com/crossrelay/core/cmd/app"
	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/kit/constants"
)

var (
	mu          sync.Mutex
	running     bool
	cancelFn    context.CancelFunc
	container   *app.Container
	localServer *http.Server
	localPort   int
	p2pPort     int
)

// StartNode initializes and starts the CrossRelay Go Core inside the Android process.
// It returns the local loopback HTTP control port (e.g., 14242) for /local/v1/* IPC calls.
func StartNode(dataDir string, deviceName string, defaultDownloadDir string) (int, error) {
	mu.Lock()
	defer mu.Unlock()

	if running {
		return localPort, nil
	}
	if dataDir == "" {
		return 0, errors.New("dataDir is required")
	}
	if err := os.MkdirAll(dataDir, 0755); err != nil {
		return 0, fmt.Errorf("create dataDir: %w", err)
	}
	if deviceName == "" {
		deviceName = "Android-Device"
	}

	ctx, cancel := context.WithCancel(context.Background())
	c, err := app.NewContainer(ctx, dataDir, deviceName, nil)
	if err != nil {
		cancel()
		return 0, err
	}

	settings, _ := c.SettingsStore.Get(ctx)
	if settings != nil && defaultDownloadDir != "" && settings.DownloadDirectory == "" {
		settings.DownloadDirectory = defaultDownloadDir
		_ = c.SettingsStore.Save(ctx, *settings)
	}

	// Start TLS 1.3 P2P server using configured port policy
	var startErr error
	for _, p := range app.ResolveCandidatePorts(settings) {
		if err := c.Server.Start(p); err != nil {
			startErr = err
			continue
		}
		startErr = nil
		break
	}
	if startErr != nil {
		cancel()
		return 0, fmt.Errorf("start TLS server: %w", startErr)
	}

	activeP2P := c.Server.ActivePort()
	c.ControlHandler.SetActivePort(activeP2P)

	// Start UDP Discovery Beacon Radar (port 4243)
	devID := ""
	if settings != nil {
		if settings.DeviceName != "" {
			deviceName = settings.DeviceName
		}
		devID = settings.DeviceID
	}
	myPeer := model.Peer{
		ID:          devID,
		Name:        deviceName,
		OS:          "android",
		Version:     constants.ProtocolVersion,
		Fingerprint: c.CertManager.Fingerprint,
		Port:        activeP2P,
		IsOnline:    true,
	}
	_ = c.Discovery.Start(ctx, myPeer)

	// Start loopback HTTP IPC server on 127.0.0.1:14242..14250
	mux := http.NewServeMux()
	c.ControlHandler.RegisterRoutes(mux)

	var ln net.Listener
	boundPort := 0
	for p := 14242; p <= 14250; p++ {
		l, err := net.Listen("tcp", fmt.Sprintf("127.0.0.1:%d", p))
		if err == nil {
			ln = l
			boundPort = p
			break
		}
	}
	if ln == nil {
		c.Discovery.Stop()
		_ = c.Server.Stop()
		cancel()
		return 0, errors.New("failed to bind local control port 14242-14250")
	}

	srv := &http.Server{
		Handler:           mux,
		ReadHeaderTimeout: 10 * time.Second,
	}
	go func() {
		_ = srv.Serve(ln)
	}()

	cancelFn = cancel
	container = c
	localServer = srv
	localPort = boundPort
	p2pPort = activeP2P
	running = true

	return boundPort, nil
}

// StopNode gracefully shuts down the CrossRelay Core engine.
func StopNode() {
	mu.Lock()
	defer mu.Unlock()

	if !running {
		return
	}
	if localServer != nil {
		_ = localServer.Close()
		localServer = nil
	}
	if container != nil {
		container.Discovery.Stop()
		_ = container.Server.Stop()
		container = nil
	}
	if cancelFn != nil {
		cancelFn()
		cancelFn = nil
	}
	localPort = 0
	p2pPort = 0
	running = false
}

// ActivePort returns the bound P2P TLS 1.3 port.
func ActivePort() int {
	mu.Lock()
	defer mu.Unlock()
	return p2pPort
}

// ControlPort returns the loopback HTTP IPC port.
func ControlPort() int {
	mu.Lock()
	defer mu.Unlock()
	return localPort
}

// IsRunning reports whether the Go Core node is currently active.
func IsRunning() bool {
	mu.Lock()
	defer mu.Unlock()
	return running
}
