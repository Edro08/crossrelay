package http

import (
	"crypto/tls"
	"net"
)

// tlsListenerShim wraps a generic net.Listener with crypto/tls.NewListener
func tlsListenerShim(inner net.Listener, config *tls.Config) net.Listener {
	return tls.NewListener(inner, config)
}
