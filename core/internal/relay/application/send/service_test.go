package send

import (
	"bytes"
	"context"
	"errors"
	"io"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestCollectUploadEntries_ExpandsDirectoriesAndFiles(t *testing.T) {
	tmp := t.TempDir()

	singleFile := filepath.Join(tmp, "hello.txt")
	if err := os.WriteFile(singleFile, []byte("hello"), 0644); err != nil {
		t.Fatal(err)
	}

	rootDir := filepath.Join(tmp, "MyFolder")
	subDir := filepath.Join(rootDir, "Sub")
	if err := os.MkdirAll(subDir, 0755); err != nil {
		t.Fatal(err)
	}
	fileA := filepath.Join(rootDir, "a.bin")
	fileB := filepath.Join(subDir, "b.bin")
	if err := os.WriteFile(fileA, []byte("1234"), 0644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(fileB, []byte("123456"), 0644); err != nil {
		t.Fatal(err)
	}

	entries := collectUploadEntries([]string{singleFile, rootDir})
	if len(entries) != 3 {
		t.Fatalf("expected 3 entries, got %d", len(entries))
	}

	got := make(map[string]int64, len(entries))
	for _, e := range entries {
		got[e.manifestName] = e.size
	}

	want := map[string]int64{
		"hello.txt":        5,
		"MyFolder/a.bin":   4,
		"MyFolder/Sub/b.bin": 6,
	}
	for name, size := range want {
		if gotSize, ok := got[name]; !ok || gotSize != size {
			t.Errorf("entry %q: got (%v, %d), want (true, %d)", name, ok, gotSize, size)
		}
	}
}

func TestService_PauseAndResumeStream(t *testing.T) {
	svc := NewService(nil, nil, nil, nil)
	svc.SetPaused(true)
	if !svc.IsPaused() {
		t.Fatal("expected service to report paused")
	}

	payload := bytes.Repeat([]byte("CrossRelayStream"), 8192)
	pr := &pausableReader{
		ctx: context.Background(),
		svc: svc,
		r:   bytes.NewReader(payload),
	}

	type readResult struct {
		data []byte
		err  error
	}
	doneCh := make(chan readResult, 1)
	go func() {
		out, err := io.ReadAll(pr)
		doneCh <- readResult{data: out, err: err}
	}()

	select {
	case res := <-doneCh:
		t.Fatalf("expected stream read to block while paused, got %d bytes (err=%v)", len(res.data), res.err)
	case <-time.After(40 * time.Millisecond):
	}

	svc.SetPaused(false)
	if svc.IsPaused() {
		t.Fatal("expected service to report unpaused after resume")
	}

	select {
	case res := <-doneCh:
		if res.err != nil {
			t.Fatalf("unexpected error after resume: %v", res.err)
		}
		if !bytes.Equal(res.data, payload) {
			t.Fatalf("payload mismatch: got %d bytes, want %d bytes", len(res.data), len(payload))
		}
	case <-time.After(2 * time.Second):
		t.Fatal("timed out waiting for stream read after resume")
	}
}

func TestService_CancelPausedStream(t *testing.T) {
	svc := NewService(nil, nil, nil, nil)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	svc.mu.Lock()
	svc.cancelFn = cancel
	svc.mu.Unlock()
	svc.SetPaused(true)

	payload := bytes.Repeat([]byte("CrossRelayCancel"), 4096)
	pr := &pausableReader{
		ctx: ctx,
		svc: svc,
		r:   bytes.NewReader(payload),
	}

	errCh := make(chan error, 1)
	go func() {
		_, err := io.ReadAll(pr)
		errCh <- err
	}()

	select {
	case err := <-errCh:
		t.Fatalf("expected paused stream read to stay blocked before cancel, got %v", err)
	case <-time.After(40 * time.Millisecond):
	}

	svc.CancelCurrent()

	select {
	case err := <-errCh:
		if !errors.Is(err, context.Canceled) {
			t.Fatalf("expected context.Canceled, got %v", err)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("timed out waiting for paused stream read to cancel")
	}
}
