package main

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"path/filepath"
	"runtime"
	"syscall"

	"github.com/crossrelay/core/cmd/app"
	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/kit/constants"
)

func main() {
	// 1. Root context with OS signal handling
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	fmt.Println("CrossRelay Core (Go Engine) initializing...")

	// 2. Data directory aligned with Desktop UI StoragePaths (%APPDATA%/CrossRelay or ~/.config/CrossRelay)
	configDir, err := os.UserConfigDir()
	if err != nil {
		configDir = "."
	}
	dataDir := filepath.Join(configDir, "CrossRelay")
	_ = os.MkdirAll(dataDir, 0755)

	// 3. Composition root
	container, err := app.NewContainer(ctx, dataDir, "CrossRelay-Node", nil)
	if err != nil {
		fmt.Fprintf(os.Stderr, "Error initializing container: %v\n", err)
		os.Exit(1)
	}

	// 3. Start TLS 1.3 Server using configured port policy ("auto", "fixed", "range")
	settings, _ := container.SettingsStore.Get(ctx)
	candidatePorts := app.ResolveCandidatePorts(settings)
	var startErr error
	for _, p := range candidatePorts {
		if err := container.Server.Start(p); err != nil {
			startErr = err
			fmt.Fprintf(os.Stderr, "Port %d busy (%v), trying next in pool...\n", p, err)
			continue
		}
		startErr = nil
		break
	}
	if startErr != nil {
		fmt.Fprintf(os.Stderr, "Error starting server on port pool: %v\n", startErr)
		os.Exit(1)
	}
	activePort := container.Server.ActivePort()
	container.ControlHandler.SetActivePort(activePort)

	fmt.Printf("TLS 1.3 Ephemeral Cert ready: %s\n", container.CertManager.Fingerprint)
	fmt.Printf("CrossRelay Server listening on https://0.0.0.0:%d (TLS 1.3)\n", activePort)

	// 4. Start UDP Discovery Beacon Radar (fixed on port 4243)
	devName := ""
	devID := ""
	if settings != nil {
		devName = settings.DeviceName
		devID = settings.DeviceID
	}
	if devName == "" || devName == "CrossRelay Device" {
		if host, err := os.Hostname(); err == nil && host != "" {
			devName = host
		}
	}

	myPeer := model.Peer{
		ID:          devID,
		Name:        devName,
		OS:          runtime.GOOS,
		Version:     constants.ProtocolVersion,
		Fingerprint: container.CertManager.Fingerprint,
		Port:        activePort,
		IsOnline:    true,
	}

	if err := container.Discovery.Start(ctx, myPeer); err != nil {
		fmt.Fprintf(os.Stderr, "Warning: failed to start UDP discovery: %v\n", err)
	} else {
		defer container.Discovery.Stop()
		fmt.Println("CrossRelay UDP Discovery active on port 4243")
	}

	fmt.Println("Ready. Press Ctrl+C to terminate.")

	<-ctx.Done()
	fmt.Println("CrossRelay Core shutdown gracefully.")
}
