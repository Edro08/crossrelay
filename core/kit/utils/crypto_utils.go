package utils

import (
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"sync"
)

var sha256BufPool = sync.Pool{
	New: func() any {
		b := make([]byte, 256*1024)
		return &b
	},
}

// GenerateUUIDv4 generates a compliant RFC 4122 UUID v4 using crypto/rand
func GenerateUUIDv4() string {
	var b [16]byte
	_, _ = rand.Read(b[:])
	b[6] = (b[6] & 0x0f) | 0x40 // Version 4
	b[8] = (b[8] & 0x3f) | 0x80 // Variant 10
	return fmt.Sprintf("%08x-%04x-%04x-%04x-%12x",
		b[0:4], b[4:6], b[6:8], b[8:10], b[10:16])
}

// GenerateRandomHex generates a secure random hex string of the specified byte length
func GenerateRandomHex(numBytes int) (string, error) {
	bytes := make([]byte, numBytes)
	if _, err := rand.Read(bytes); err != nil {
		return "", err
	}
	return hex.EncodeToString(bytes), nil
}

// ComputeReaderSHA256 reads an entire stream and returns its SHA-256 hex digest
func ComputeReaderSHA256(r io.Reader) (string, error) {
	h := sha256.New()
	bufPtr := sha256BufPool.Get().(*[]byte)
	_, err := io.CopyBuffer(h, r, *bufPtr)
	sha256BufPool.Put(bufPtr)
	if err != nil {
		return "", err
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

// ComputeBytesSHA256 returns the SHA-256 hex digest of a byte slice
func ComputeBytesSHA256(data []byte) string {
	h := sha256.Sum256(data)
	return hex.EncodeToString(h[:])
}
