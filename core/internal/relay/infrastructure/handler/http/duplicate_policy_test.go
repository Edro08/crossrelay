package http

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/internal/relay/infrastructure/storage"
)

func TestUniqueDestinationPath(t *testing.T) {
	dir := t.TempDir()
	base := filepath.Join(dir, "video.mkv")
	if err := os.WriteFile(base, []byte("v0"), 0644); err != nil {
		t.Fatal(err)
	}
	first := uniqueDestinationPath(base)
	wantFirst := filepath.Join(dir, "video (1).mkv")
	if first != wantFirst {
		t.Fatalf("got %q, want %q", first, wantFirst)
	}

	if err := os.WriteFile(first, []byte("v1"), 0644); err != nil {
		t.Fatal(err)
	}
	second := uniqueDestinationPath(base)
	wantSecond := filepath.Join(dir, "video (2).mkv")
	if second != wantSecond {
		t.Fatalf("got %q, want %q", second, wantSecond)
	}
}

func TestHandleSessionUploadDuplicatePolicy(t *testing.T) {
	cases := []struct {
		name           string
		policy         string
		wantBaseData   string
		wantRenameFile bool
		wantRenameData string
	}{
		{
			name:           "rename keeps original and creates suffixed copy",
			policy:         "rename",
			wantBaseData:   "original-content",
			wantRenameFile: true,
			wantRenameData: "incoming-content",
		},
		{
			name:           "overwrite replaces existing file",
			policy:         "overwrite",
			wantBaseData:   "incoming-content",
			wantRenameFile: false,
		},
		{
			name:           "skip preserves existing file without creating copy",
			policy:         "skip",
			wantBaseData:   "original-content",
			wantRenameFile: false,
		},
	}

	incoming := []byte("incoming-content")
	sum := sha256.Sum256(incoming)
	incomingHash := hex.EncodeToString(sum[:])

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			root := t.TempDir()
			downloadDir := filepath.Join(root, "downloads")
			if err := os.MkdirAll(downloadDir, 0755); err != nil {
				t.Fatal(err)
			}

			existingPath := filepath.Join(downloadDir, "sample.txt")
			if err := os.WriteFile(existingPath, []byte("original-content"), 0644); err != nil {
				t.Fatal(err)
			}

			store := storage.NewFileSettingsStore(root)
			cfg, err := store.Get(context.Background())
			if err != nil {
				t.Fatal(err)
			}
			cfg.DownloadDirectory = downloadDir
			cfg.DuplicatePolicy = tc.policy
			if err := store.Save(context.Background(), *cfg); err != nil {
				t.Fatal(err)
			}

			srv := &Server{settingsStore: store}
			srv.activeSessions.Store("sess-1", &incomingSessionState{
				manifest: &model.TransferManifest{
					SessionID:  "sess-1",
					TotalBytes: int64(len(incoming)),
					Files: []model.FileItem{
						{
							FileID: "file-1",
							Name:   "sample.txt",
							Size:   int64(len(incoming)),
							SHA256: incomingHash,
						},
					},
				},
				downloadDir: downloadDir,
				startedAt:   time.Now(),
			})

			req := httptest.NewRequest(http.MethodPost, "/api/v1/session/upload/sess-1/file-1", bytes.NewReader(incoming))
			req.SetPathValue("sessionId", "sess-1")
			req.SetPathValue("fileId", "file-1")
			rec := httptest.NewRecorder()

			srv.handleSessionUpload(rec, req)

			if rec.Code != http.StatusOK {
				t.Fatalf("expected 200 OK, got %d (%s)", rec.Code, rec.Body.String())
			}

			gotBase, err := os.ReadFile(existingPath)
			if err != nil {
				t.Fatalf("failed reading base file: %v", err)
			}
			if string(gotBase) != tc.wantBaseData {
				t.Fatalf("base file content = %q, want %q", string(gotBase), tc.wantBaseData)
			}

			renamedPath := filepath.Join(downloadDir, "sample (1).txt")
			_, statErr := os.Stat(renamedPath)
			if tc.wantRenameFile {
				if statErr != nil {
					t.Fatalf("expected renamed file %q to exist: %v", renamedPath, statErr)
				}
				gotRenamed, _ := os.ReadFile(renamedPath)
				if string(gotRenamed) != tc.wantRenameData {
					t.Fatalf("renamed content = %q, want %q", string(gotRenamed), tc.wantRenameData)
				}
			} else if !os.IsNotExist(statErr) {
				t.Fatalf("did not expect renamed file %q to exist", renamedPath)
			}
		})
	}
}
