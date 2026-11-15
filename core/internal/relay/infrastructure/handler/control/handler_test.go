package control

import (
	"bytes"
	"encoding/json"
	"net"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestHandleDiagnosePorts(t *testing.T) {
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to bind ephemeral listener: %v", err)
	}
	defer ln.Close()
	occupiedPort := ln.Addr().(*net.TCPAddr).Port

	freeLn, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to probe free port: %v", err)
	}
	freePort := freeLn.Addr().(*net.TCPAddr).Port
	_ = freeLn.Close()

	activeSelfPort := 42421
	h := &Handler{activePort: activeSelfPort}

	body, _ := json.Marshal(map[string]any{
		"ports": []int{activeSelfPort, occupiedPort, freePort},
	})
	req := httptest.NewRequest(http.MethodPost, "/local/v1/diagnostics/ports", bytes.NewReader(body))
	rec := httptest.NewRecorder()

	h.handleDiagnosePorts(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected 200 OK, got %d", rec.Code)
	}

	var resp struct {
		ActivePort int `json:"activePort"`
		Ports      []struct {
			Port  int    `json:"port"`
			State string `json:"state"`
		} `json:"ports"`
	}
	if err := json.NewDecoder(rec.Body).Decode(&resp); err != nil {
		t.Fatalf("failed to decode response: %v", err)
	}
	if resp.ActivePort != activeSelfPort {
		t.Fatalf("expected activePort %d, got %d", activeSelfPort, resp.ActivePort)
	}

	gotStates := make(map[int]string, len(resp.Ports))
	for _, p := range resp.Ports {
		gotStates[p.Port] = p.State
	}

	if gotStates[activeSelfPort] != "active_self" {
		t.Fatalf("expected active_self for %d, got %q", activeSelfPort, gotStates[activeSelfPort])
	}
	if gotStates[occupiedPort] != "occupied" {
		t.Fatalf("expected occupied for %d, got %q", occupiedPort, gotStates[occupiedPort])
	}
	if gotStates[freePort] != "free" {
		t.Fatalf("expected free for %d, got %q", freePort, gotStates[freePort])
	}
}

func TestHandleDiagnosePortsCapsAt30(t *testing.T) {
	h := &Handler{}
	ports := make([]int, 40)
	for i := range ports {
		ports[i] = 55000 + i
	}
	body, _ := json.Marshal(map[string]any{"ports": ports})
	req := httptest.NewRequest(http.MethodPost, "/local/v1/diagnostics/ports", bytes.NewReader(body))
	rec := httptest.NewRecorder()

	h.handleDiagnosePorts(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected 200 OK, got %d", rec.Code)
	}

	var resp struct {
		Ports []struct {
			Port int `json:"port"`
		} `json:"ports"`
	}
	if err := json.NewDecoder(rec.Body).Decode(&resp); err != nil {
		t.Fatalf("failed to decode response: %v", err)
	}
	if len(resp.Ports) != 30 {
		t.Fatalf("expected 30 diagnosed ports max, got %d", len(resp.Ports))
	}
}
