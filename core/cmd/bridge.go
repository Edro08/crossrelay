package main

/*
#include <stdlib.h>
typedef void (*EventCallback)(const char* eventType, const char* jsonPayload);
static inline void invokeCallback(EventCallback cb, const char* eventType, const char* jsonPayload) {
    if (cb != NULL) {
        cb(eventType, jsonPayload);
    }
}
*/
import "C"
import (
	"context"
	"encoding/json"
	"runtime"
	"sync"
	"unsafe"

	"github.com/crossrelay/core/cmd/app"
	"github.com/crossrelay/core/internal/relay/domain/model"
)

var (
	globalContainer *app.Container
	globalCancel    context.CancelFunc
	globalMu        sync.Mutex
	cCallback       C.EventCallback
)

type bridgeSink struct{}

func (b *bridgeSink) sendEvent(eventType string, payload any) {
	if cCallback == nil {
		return
	}
	data, _ := json.Marshal(payload)
	cType := C.CString(eventType)
	cPayload := C.CString(string(data))
	defer C.free(unsafe.Pointer(cType))
	defer C.free(unsafe.Pointer(cPayload))

	C.invokeCallback(cCallback, cType, cPayload)
}

func (b *bridgeSink) OnPeerDiscovered(p model.Peer) {
	b.sendEvent("peer_discovered", p)
}

func (b *bridgeSink) OnPeerLost(peerID string) {
	b.sendEvent("peer_lost", map[string]string{"id": peerID})
}

func (b *bridgeSink) OnPinChallengeReceived(c model.PinChallenge) {
	b.sendEvent("pin_challenge", c)
}

func (b *bridgeSink) OnIncomingTransferRequest(m model.TransferManifest) (bool, error) {
	b.sendEvent("transfer_requested", m)
	return true, nil
}

func (b *bridgeSink) OnTransferProgress(sessionID string, bTransferred, totalBytes, speed int64) {
	b.sendEvent("transfer_progress", map[string]any{
		"sessionId":        sessionID,
		"bytesTransferred": bTransferred,
		"totalBytes":       totalBytes,
		"speedBps":         speed,
	})
}

func (b *bridgeSink) OnTransferCompleted(sessionID string, success bool, errMsg string) {
	b.sendEvent("transfer_completed", map[string]any{
		"sessionId": sessionID,
		"success":   success,
		"error":     errMsg,
	})
}

//export CrossRelay_Init
func CrossRelay_Init(dataDir *C.char, deviceName *C.char, port C.int, cb C.EventCallback) C.int {
	globalMu.Lock()
	defer globalMu.Unlock()

	cCallback = cb
	ctx, cancel := context.WithCancel(context.Background())
	globalCancel = cancel

	dir := C.GoString(dataDir)
	name := C.GoString(deviceName)

	sink := &bridgeSink{}
	container, err := app.NewContainer(ctx, dir, name, sink)
	if err != nil {
		return -1
	}

	p := int(port)
	if p <= 0 {
		p = 4242
	}

	if err := container.Server.Start(p); err != nil {
		return -2
	}

	me := model.Peer{
		ID:          container.CertManager.Fingerprint,
		Name:        name,
		OS:          runtime.GOOS,
		Fingerprint: container.CertManager.Fingerprint,
		Port:        p,
	}
	_ = container.Discovery.Start(ctx, me)

	globalContainer = container
	return 0
}

//export CrossRelay_Shutdown
func CrossRelay_Shutdown() C.int {
	globalMu.Lock()
	defer globalMu.Unlock()

	if globalCancel != nil {
		globalCancel()
	}
	if globalContainer != nil {
		_ = globalContainer.Server.Stop()
		_ = globalContainer.Discovery.Stop()
		globalContainer = nil
	}
	return 0
}
