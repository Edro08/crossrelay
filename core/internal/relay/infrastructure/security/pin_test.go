package security_test

import (
	"testing"

	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/internal/relay/infrastructure/security"
)

func TestPinManagerLifecycle(t *testing.T) {
	pm := security.NewPinManager()

	requester := model.Peer{
		ID:          "device-123",
		Name:        "Test Laptop",
		OS:          "linux",
		Fingerprint: "sha256:abc",
	}

	challenge, err := pm.GeneratePin(requester)
	if err != nil {
		t.Fatalf("GeneratePin failed: %v", err)
	}

	if len(challenge.PinCode) != 6 {
		t.Fatalf("expected 6-digit PIN, got %s", challenge.PinCode)
	}

	// Wrong PIN
	_, err = pm.VerifyPin("device-123", "000000")
	if err == nil {
		t.Fatalf("expected error for wrong PIN")
	}

	// Correct PIN
	verified, err := pm.VerifyPin("device-123", challenge.PinCode)
	if err != nil {
		t.Fatalf("VerifyPin with correct PIN failed: %v", err)
	}

	if verified.RequesterID != requester.ID {
		t.Errorf("expected requester ID %s, got %s", requester.ID, verified.RequesterID)
	}

	// Verifying again should return expired since it was cleared
	_, err = pm.VerifyPin("device-123", challenge.PinCode)
	if err != model.ErrPinExpired {
		t.Errorf("expected ErrPinExpired after clearance, got: %v", err)
	}
}
