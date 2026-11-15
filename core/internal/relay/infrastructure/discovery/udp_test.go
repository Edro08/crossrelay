package discovery_test

import (
	"context"
	"testing"

	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/internal/relay/infrastructure/discovery"
)

type mockSink struct{}

func (m *mockSink) OnPeerDiscovered(p model.Peer)                                                   {}
func (m *mockSink) OnPeerLost(peerID string)                                                        {}
func (m *mockSink) OnPeerPaired(peer model.TrustedPeer)                                             {}
func (m *mockSink) OnPinChallengeReceived(c model.PinChallenge)                                      {}
func (m *mockSink) OnIncomingTransferRequest(manifest model.TransferManifest) (bool, error)        { return true, nil }
func (m *mockSink) OnTransferProgress(sessionID string, bTransferred, totalBytes, speed int64)      {}
func (m *mockSink) OnTransferCompleted(sessionID string, success bool, errMsg string)               {}

func TestUDPDiscoveryLifecycle(t *testing.T) {
	sink := &mockSink{}
	disc := discovery.NewUDPDiscoveryWithPort(sink, 42439)

	node := model.Peer{
		ID:          "test-node-1",
		Name:        "Test Node",
		OS:          "windows",
		Fingerprint: "sha256:abc",
		Port:        4242,
	}

	ctx := context.Background()
	if err := disc.Start(ctx, node); err != nil {
		t.Fatalf("disc.Start failed: %v", err)
	}

	if err := disc.Stop(); err != nil {
		t.Fatalf("disc.Stop failed: %v", err)
	}
}

func TestClassifyInterfaceScore(t *testing.T) {
	ethPrivate := discovery.ClassifyInterfaceScore("Ethernet", false)
	ethLinkLocal := discovery.ClassifyInterfaceScore("enp3s0", true)
	wifi := discovery.ClassifyInterfaceScore("Wi-Fi", false)
	vpn := discovery.ClassifyInterfaceScore("Norton VPN Wintun", false)
	hyperv := discovery.ClassifyInterfaceScore("vEthernet (Default Switch)", false)

	if !(ethPrivate > ethLinkLocal && ethLinkLocal > wifi && wifi > vpn && wifi > hyperv) {
		t.Fatalf("unexpected interface priority order: ethPrivate=%d ethLinkLocal=%d wifi=%d vpn=%d hyperv=%d",
			ethPrivate, ethLinkLocal, wifi, vpn, hyperv)
	}
}
