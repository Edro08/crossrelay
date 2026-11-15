package app

import (
	"context"

	"github.com/crossrelay/core/internal/relay/application/pairing"
	"github.com/crossrelay/core/internal/relay/application/send"
	"github.com/crossrelay/core/internal/relay/domain/ports"
	"github.com/crossrelay/core/internal/relay/infrastructure/discovery"
	"github.com/crossrelay/core/internal/relay/infrastructure/handler/control"
	"github.com/crossrelay/core/internal/relay/infrastructure/handler/http"
	"github.com/crossrelay/core/internal/relay/infrastructure/repository/rest"
	"github.com/crossrelay/core/internal/relay/infrastructure/security"
	"github.com/crossrelay/core/internal/relay/infrastructure/storage"
)

// Container holds the wired dependencies (Composition Root)
type Container struct {
	CertManager    *security.CertManager
	PinManager     *security.PinManager
	PeersStore     ports.TrustedPeersStore
	SettingsStore  ports.SettingsStore
	PairingService *pairing.Service
	SendService    *send.Service
	Server         *http.Server
	Client         *rest.Client
	Discovery      *discovery.UDPDiscovery
	ControlBroker  *control.Broker
	ControlHandler *control.Handler
}

// NewContainer wires domain and infrastructure according to architecture specs
func NewContainer(ctx context.Context, dataDir string, deviceName string, eventSink ports.EventSink) (*Container, error) {
	// 1. Storage
	peersStore := storage.NewFileTrustedPeersStore(dataDir)
	settingsStore := storage.NewFileSettingsStore(dataDir)

	// 2. Security (TLS 1.3 cert + PIN)
	certMgr, err := security.GenerateEphemeralCertificate(deviceName)
	if err != nil {
		return nil, err
	}
	pinMgr := security.NewPinManager()

	// 3. Infrastructure Client & Server & Discovery & Control Broker
	client := rest.NewClient()
	broker := control.NewBroker()
	if eventSink == nil {
		eventSink = control.NewLocalEventSink(broker)
	}

	disc := discovery.NewUDPDiscovery(eventSink)
	disc.ProbeFunc = client.ProbeWithLocalInfo

	// 4. Application Services (Use Cases)
	pairingService := pairing.NewService(peersStore)
	sendService := send.NewService(peersStore, settingsStore, client, eventSink)

	server := http.NewServer(certMgr, pinMgr, peersStore, settingsStore, pairingService, eventSink)
	server.OnPingReceived = disc.NotifyRemoteIP
	server.OnCallerDiscovered = disc.RecordDiscoveredPeer
	controlHandler := control.NewHandler(certMgr, peersStore, settingsStore, disc, client, pairingService, sendService, broker)
	if localSink, ok := eventSink.(*control.LocalEventSink); ok {
		controlHandler.EventSink = localSink
	}
	controlHandler.OnCancelIncoming = server.CancelActiveIncoming
	server.ExtraMuxRoutes = controlHandler.RegisterRoutes

	return &Container{
		CertManager:    certMgr,
		PinManager:     pinMgr,
		PeersStore:     peersStore,
		SettingsStore:  settingsStore,
		PairingService: pairingService,
		SendService:    sendService,
		Server:         server,
		Client:         client,
		Discovery:      disc,
		ControlBroker:  broker,
		ControlHandler: controlHandler,
	}, nil
}
