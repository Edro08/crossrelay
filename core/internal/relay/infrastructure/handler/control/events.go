package control

import (
	"encoding/json"
	"fmt"
	"net/http"
	"sync"
	"time"

	"github.com/crossrelay/core/internal/relay/domain/model"
)

// Broker handles Server-Sent Events (SSE) to push real-time events to the local UI (Avalonia / Desktop)
type Broker struct {
	mu      sync.Mutex
	clients map[chan string]bool
}

func NewBroker() *Broker {
	return &Broker{
		clients: make(map[chan string]bool),
	}
}

func (b *Broker) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	flusher, ok := w.(http.Flusher)
	if !ok {
		http.Error(w, "Streaming unsupported", http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "text/event-stream")
	w.Header().Set("Cache-Control", "no-cache")
	w.Header().Set("Connection", "keep-alive")
	w.Header().Set("Access-Control-Allow-Origin", "*")

	msgChan := make(chan string, 16)
	b.mu.Lock()
	b.clients[msgChan] = true
	b.mu.Unlock()

	defer func() {
		b.mu.Lock()
		delete(b.clients, msgChan)
		close(msgChan)
		b.mu.Unlock()
	}()

	// Send initial connected event
	fmt.Fprintf(w, "event: connected\ndata: {}\n\n")
	flusher.Flush()

	for {
		select {
		case <-r.Context().Done():
			return
		case msg, ok := <-msgChan:
			if !ok {
				return
			}
			fmt.Fprint(w, msg)
			flusher.Flush()
		}
	}
}

// Publish sends an event to all connected local UI clients
func (b *Broker) Publish(eventType string, data any) {
	bytes, err := json.Marshal(data)
	if err != nil {
		return
	}
	msg := fmt.Sprintf("event: %s\ndata: %s\n\n", eventType, string(bytes))

	b.mu.Lock()
	defer b.mu.Unlock()

	for ch := range b.clients {
		select {
		case ch <- msg:
		default:
			// Non-blocking drop if client buffer is full
		}
	}
}

// LocalEventSink implements ports.EventSink forwarding events to the SSE broker
type LocalEventSink struct {
	Broker               *Broker
	pendingConfirmations sync.Map // sessionID -> chan bool
	progressMu           sync.Mutex
	lastProgressAt       map[string]time.Time
}

func NewLocalEventSink(broker *Broker) *LocalEventSink {
	return &LocalEventSink{
		Broker:         broker,
		lastProgressAt: make(map[string]time.Time),
	}
}

func (s *LocalEventSink) OnPeerDiscovered(p model.Peer) {
	s.Broker.Publish("peer_discovered", p)
}

func (s *LocalEventSink) OnPeerLost(peerID string) {
	s.Broker.Publish("peer_lost", map[string]string{"id": peerID})
}

func (s *LocalEventSink) OnPeerPaired(p model.TrustedPeer) {
	s.Broker.Publish("peer_paired", p)
}

func (s *LocalEventSink) OnPinChallengeReceived(c model.PinChallenge) {
	s.Broker.Publish("pin_challenge", c)
}

func (s *LocalEventSink) OnIncomingTransferRequest(manifest model.TransferManifest) (bool, error) {
	sseManifest := manifest
	if len(sseManifest.Files) > 200 {
		sseManifest.Files = sseManifest.Files[:200]
	}

	if !manifest.RequiresConfirmation {
		s.Broker.Publish("transfer_requested", sseManifest)
		return true, nil
	}

	ch := make(chan bool, 1)
	s.pendingConfirmations.Store(manifest.SessionID, ch)
	defer s.pendingConfirmations.Delete(manifest.SessionID)

	s.Broker.Publish("transfer_requested", sseManifest)

	select {
	case accepted := <-ch:
		return accepted, nil
	case <-time.After(60 * time.Second):
		s.OnTransferCompleted(manifest.SessionID, false, "Tiempo de espera agotado")
		return false, nil
	}
}

func (s *LocalEventSink) RespondTransfer(sessionID string, accepted bool) bool {
	val, ok := s.pendingConfirmations.Load(sessionID)
	if !ok {
		return false
	}
	ch := val.(chan bool)
	select {
	case ch <- accepted:
		return true
	default:
		return false
	}
}

func (s *LocalEventSink) OnTransferProgress(sessionID string, bTransferred, totalBytes, speed int64) {
	now := time.Now()
	s.progressMu.Lock()
	if s.lastProgressAt == nil {
		s.lastProgressAt = make(map[string]time.Time)
	}
	if last, ok := s.lastProgressAt[sessionID]; ok && now.Sub(last) < 150*time.Millisecond && (totalBytes == 0 || bTransferred < totalBytes) {
		s.progressMu.Unlock()
		return
	}
	s.lastProgressAt[sessionID] = now
	s.progressMu.Unlock()

	s.Broker.Publish("transfer_progress", map[string]any{
		"sessionId":        sessionID,
		"bytesTransferred": bTransferred,
		"totalBytes":       totalBytes,
		"speedBps":         speed,
	})
}

func (s *LocalEventSink) OnTransferCompleted(sessionID string, success bool, errMsg string) {
	s.progressMu.Lock()
	delete(s.lastProgressAt, sessionID)
	s.progressMu.Unlock()

	s.Broker.Publish("transfer_completed", map[string]any{
		"sessionId": sessionID,
		"success":   success,
		"error":     errMsg,
	})
}
