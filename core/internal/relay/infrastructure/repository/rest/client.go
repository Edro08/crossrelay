package rest

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"net/http"
	"sync"
	"time"

	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/internal/relay/infrastructure/security"
	"github.com/crossrelay/core/kit/constants"
)

// Client implements ports.PeerTransmitter using net/http over TLS 1.3
type Client struct {
	transports sync.Map // expectedFingerprint -> *http.Transport
}

func NewClient() *Client {
	return &Client{}
}

func (c *Client) getHTTPClient(expectedFingerprint string, timeout time.Duration) *http.Client {
	if v, ok := c.transports.Load(expectedFingerprint); ok {
		return &http.Client{
			Timeout:   timeout,
			Transport: v.(*http.Transport),
		}
	}

	tr := &http.Transport{
		TLSClientConfig: security.ClientTLSConfig(expectedFingerprint),
		DialContext: (&net.Dialer{
			Timeout:   10 * time.Second,
			KeepAlive: 30 * time.Second,
		}).DialContext,
		TLSHandshakeTimeout: 10 * time.Second,
		MaxIdleConns:        16,
		MaxIdleConnsPerHost: 8,
		IdleConnTimeout:     90 * time.Second,
	}
	actual, loaded := c.transports.LoadOrStore(expectedFingerprint, tr)
	if loaded {
		tr.CloseIdleConnections()
	}

	return &http.Client{
		Timeout:   timeout,
		Transport: actual.(*http.Transport),
	}
}

// Ping measures RTT and checks peer readiness
func (c *Client) Ping(ctx context.Context, peer model.Peer) (int64, error) {
	client := c.getHTTPClient(peer.Fingerprint, constants.DefaultPingTimeout)
	port := peer.Port
	if port <= 0 {
		port = constants.DefaultPort
	}
	url := fmt.Sprintf("https://%s:%d/api/v1/ping", peer.IP, port)

	start := time.Now()
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return 0, err
	}

	resp, err := client.Do(req)
	if err != nil {
		return 0, err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return 0, fmt.Errorf("ping returned status %d", resp.StatusCode)
	}

	latency := time.Since(start).Milliseconds()
	return latency, nil
}

// Probe contacts an IP directly via TLS 1.3 ping and returns the discovered peer model
func (c *Client) Probe(ctx context.Context, ip string, port int) (*model.Peer, error) {
	return c.ProbeWithLocalInfo(ctx, ip, port, model.Peer{})
}

// ProbeWithLocalInfo contacts an IP directly via TLS 1.3 ping while advertising our local identity headers
func (c *Client) ProbeWithLocalInfo(ctx context.Context, ip string, port int, local model.Peer) (*model.Peer, error) {
	client := c.getHTTPClient("", constants.DefaultPingTimeout)
	url := fmt.Sprintf("https://%s:%d/api/v1/ping", ip, port)

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, err
	}
	if local.ID != "" {
		req.Header.Set("X-CrossRelay-Id", local.ID)
		req.Header.Set("X-CrossRelay-Name", local.Name)
		req.Header.Set("X-CrossRelay-OS", local.OS)
		req.Header.Set("X-CrossRelay-Fp", local.Fingerprint)
		if local.Port > 0 {
			req.Header.Set("X-CrossRelay-Port", fmt.Sprintf("%d", local.Port))
		}
	}

	resp, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("probe ping returned status %d", resp.StatusCode)
	}

	var raw map[string]any
	if err := json.NewDecoder(resp.Body).Decode(&raw); err != nil {
		return nil, err
	}

	getString := func(keys ...string) string {
		for _, k := range keys {
			if v, ok := raw[k]; ok {
				if s, ok := v.(string); ok && s != "" {
					return s
				}
			}
		}
		return ""
	}

	peerID := getString("id", "deviceId")
	if peerID == "" {
		peerID = "node-" + ip
	}

	peerName := getString("name", "deviceName")
	if peerName == "" {
		peerName = "Linux Device (" + ip + ")"
	}

	peerOS := getString("os")
	if peerOS == "" || peerOS == "desktop" {
		peerOS = "linux"
	}

	peer := &model.Peer{
		ID:          peerID,
		Name:        peerName,
		Fingerprint: getString("fingerprint", "fp"),
		Version:     getString("version", "ver"),
		OS:          peerOS,
		IP:          ip,
		Port:        port,
		IsOnline:    true,
		LastSeenAt:  time.Now(),
	}
	return peer, nil
}

// InitiatePairing starts the 6-digit PIN challenge on the receiver
func (c *Client) InitiatePairing(ctx context.Context, peer model.Peer, myInfo model.Peer) error {
	client := c.getHTTPClient(peer.Fingerprint, constants.DefaultPairInitTimeout)
	port := peer.Port
	if port <= 0 {
		port = constants.DefaultPort
	}
	url := fmt.Sprintf("https://%s:%d/api/v1/pair/init", peer.IP, port)

	payload := map[string]any{
		"requester": map[string]string{
			"id":          myInfo.ID,
			"name":        myInfo.Name,
			"os":          myInfo.OS,
			"fingerprint": myInfo.Fingerprint,
		},
	}
	body, _ := json.Marshal(payload)

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(body))
	if err != nil {
		return err
	}
	req.Header.Set("Content-Type", "application/json")

	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("pairing init failed with status: %d", resp.StatusCode)
	}
	return nil
}

// VerifyPairing sends the 6-digit PIN typed by the user to the receiver
func (c *Client) VerifyPairing(ctx context.Context, peer model.Peer, myID string, pin string) (string, error) {
	client := c.getHTTPClient(peer.Fingerprint, constants.DefaultPairInitTimeout)
	port := peer.Port
	if port <= 0 {
		port = constants.DefaultPort
	}
	url := fmt.Sprintf("https://%s:%d/api/v1/pair/verify", peer.IP, port)

	payload := map[string]string{
		"requesterId": myID,
		"enteredPin":  pin,
	}
	body, _ := json.Marshal(payload)

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(body))
	if err != nil {
		return "", err
	}
	req.Header.Set("Content-Type", "application/json")

	resp, err := client.Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusUnauthorized {
		return "", model.ErrInvalidPin
	}
	if resp.StatusCode == http.StatusForbidden {
		return "", model.ErrPinMaxAttempts
	}
	if resp.StatusCode == http.StatusRequestTimeout {
		return "", model.ErrPinExpired
	}
	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("verification failed with status: %d", resp.StatusCode)
	}

	var res struct {
		Status       string `json:"status"`
		PairingToken string `json:"pairingToken"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&res); err != nil {
		return "", err
	}

	return res.PairingToken, nil
}

// RequestTransfer proposes a batch manifest to the remote peer
func (c *Client) RequestTransfer(ctx context.Context, peer model.Peer, token string, manifest model.TransferManifest) (bool, error) {
	client := c.getHTTPClient(peer.Fingerprint, 65*time.Second)
	port := peer.Port
	if port <= 0 {
		port = constants.DefaultPort
	}
	url := fmt.Sprintf("https://%s:%d/api/v1/session/request", peer.IP, port)

	body, _ := json.Marshal(manifest)
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(body))
	if err != nil {
		return false, err
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("X-CrossRelay-Pairing-Token", token)
	req.Header.Set("X-CrossRelay-Sender-Id", manifest.SenderID)

	resp, err := client.Do(req)
	if err != nil {
		return false, err
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusOK {
		return true, nil
	}
	if resp.StatusCode == http.StatusForbidden {
		return false, model.ErrTransferRejected
	}
	return false, fmt.Errorf("session request failed with status: %d", resp.StatusCode)
}

// StreamFile streams the raw binary file chunk-by-chunk to the remote peer
func (c *Client) StreamFile(
	ctx context.Context,
	peer model.Peer,
	token string,
	sessionID string,
	file model.FileItem,
	reader io.Reader,
	onProgress func(written int64),
) error {
	client := c.getHTTPClient(peer.Fingerprint, 0) // No timeout during large file uploads
	port := peer.Port
	if port <= 0 {
		port = constants.DefaultPort
	}
	url := fmt.Sprintf("https://%s:%d/api/v1/session/%s/upload/%s", peer.IP, port, sessionID, file.FileID)

	// Wrap reader to monitor progress
	progressR := &progressReader{
		reader:     reader,
		onProgress: onProgress,
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, progressR)
	if err != nil {
		return err
	}
	req.ContentLength = file.Size
	req.Header.Set("Content-Type", "application/octet-stream")
	req.Header.Set("X-CrossRelay-Pairing-Token", token)
	req.Header.Set("X-CrossRelay-Sha256", file.SHA256)

	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("file upload failed with status: %d", resp.StatusCode)
	}

	return nil
}

type progressReader struct {
	reader     io.Reader
	onProgress func(written int64)
}

func (pr *progressReader) Read(p []byte) (int, error) {
	n, err := pr.reader.Read(p)
	if n > 0 && pr.onProgress != nil {
		pr.onProgress(int64(n))
	}
	return n, err
}
