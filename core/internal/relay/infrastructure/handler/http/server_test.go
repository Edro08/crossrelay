package http_test

import (
	"context"
	"crypto/tls"
	"encoding/json"
	"net/http"
	"os"
	"testing"
	"time"

	"github.com/crossrelay/core/cmd/app"
	transport "github.com/crossrelay/core/internal/relay/infrastructure/handler/http"
)

func TestServerPingOverTLS13(t *testing.T) {
	tempDir, err := os.MkdirTemp("", "crossrelay_test_*")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(tempDir)

	ctx := context.Background()
	container, err := app.NewContainer(ctx, tempDir, "Test-Node", nil)
	if err != nil {
		t.Fatalf("failed to create container: %v", err)
	}

	testPort := 42429
	if err := container.Server.Start(testPort); err != nil {
		t.Fatalf("failed to start server: %v", err)
	}
	defer container.Server.Stop()

	// Wait 50ms for listener to be active
	time.Sleep(50 * time.Millisecond)

	// TLS 1.3 client connecting to server
	client := &http.Client{
		Transport: &http.Transport{
			TLSClientConfig: &tls.Config{
				InsecureSkipVerify: true,
				MinVersion:         tls.VersionTLS13,
			},
		},
	}

	resp, err := client.Get("https://127.0.0.1:42429/api/v1/ping")
	if err != nil {
		t.Fatalf("GET /ping failed: %v", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		t.Fatalf("expected 200 OK, got: %d", resp.StatusCode)
	}

	if resp.TLS == nil || resp.TLS.Version != tls.VersionTLS13 {
		t.Fatalf("expected TLS 1.3 handshake, got: %x", resp.TLS.Version)
	}

	var pingResp transport.PingResponse
	if err := json.NewDecoder(resp.Body).Decode(&pingResp); err != nil {
		t.Fatalf("failed to decode ping json: %v", err)
	}

	if pingResp.Status != "ready" {
		t.Fatalf("expected status 'ready', got: %s", pingResp.Status)
	}
}
