package security

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/sha256"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/hex"
	"fmt"
	"math/big"
	"net"
	"time"
)

// CertManager generates and holds ephemeral TLS 1.3 certificates
type CertManager struct {
	Certificate *tls.Certificate
	Fingerprint string // SHA-256 fingerprint formatted as "sha256:abcd..."
}

// GenerateEphemeralCertificate creates a self-signed X.509 ECDSA P-256 cert in-memory
func GenerateEphemeralCertificate(deviceName string) (*CertManager, error) {
	priv, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		return nil, fmt.Errorf("failed to generate ecdsa key: %w", err)
	}

	serialNumberLimit := new(big.Int).Lsh(big.NewInt(1), 128)
	serialNumber, err := rand.Int(rand.Reader, serialNumberLimit)
	if err != nil {
		return nil, fmt.Errorf("failed to generate serial number: %w", err)
	}

	template := x509.Certificate{
		SerialNumber: serialNumber,
		Subject: pkix.Name{
			CommonName:   fmt.Sprintf("CrossRelay-%s", deviceName),
			Organization: []string{"CrossRelay"},
		},
		NotBefore:             time.Now().Add(-1 * time.Hour),
		NotAfter:              time.Now().Add(365 * 24 * time.Hour), // 1 year
		KeyUsage:              x509.KeyUsageKeyEncipherment | x509.KeyUsageDigitalSignature,
		ExtKeyUsage:           []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth, x509.ExtKeyUsageClientAuth},
		BasicConstraintsValid: true,
		IPAddresses:           []net.IP{net.ParseIP("127.0.0.1"), net.IPv4zero},
	}

	derBytes, err := x509.CreateCertificate(rand.Reader, &template, &template, &priv.PublicKey, priv)
	if err != nil {
		return nil, fmt.Errorf("failed to create certificate: %w", err)
	}

	// Compute SHA-256 fingerprint of DER bytes
	fpSum := sha256.Sum256(derBytes)
	fingerprint := "sha256:" + hex.EncodeToString(fpSum[:])

	tlsCert := tls.Certificate{
		Certificate: [][]byte{derBytes},
		PrivateKey:  priv,
	}

	return &CertManager{
		Certificate: &tlsCert,
		Fingerprint: fingerprint,
	}, nil
}

// ServerTLSConfig creates a strictly TLS 1.3 server config
func (cm *CertManager) ServerTLSConfig() *tls.Config {
	return &tls.Config{
		Certificates: []tls.Certificate{*cm.Certificate},
		MinVersion:   tls.VersionTLS13,
		MaxVersion:   tls.VersionTLS13,
	}
}

// ClientTLSConfig creates a TLS 1.3 client config that pins the peer's expected SHA-256 fingerprint
func ClientTLSConfig(expectedFingerprint string) *tls.Config {
	return &tls.Config{
		MinVersion:         tls.VersionTLS13,
		MaxVersion:         tls.VersionTLS13,
		InsecureSkipVerify: true, // We do custom certificate pinning via VerifyPeerCertificate
		VerifyPeerCertificate: func(rawCerts [][]byte, verifiedChains [][]*x509.Certificate) error {
			if len(rawCerts) == 0 {
				return fmt.Errorf("no peer certificates provided")
			}
			fpSum := sha256.Sum256(rawCerts[0])
			actualFP := "sha256:" + hex.EncodeToString(fpSum[:])
			if expectedFingerprint != "" && actualFP != expectedFingerprint {
				return fmt.Errorf("TLS certificate fingerprint mismatch: expected %s, got %s", expectedFingerprint, actualFP)
			}
			return nil
		},
	}
}
