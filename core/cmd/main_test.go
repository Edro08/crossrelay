package main

import (
	"reflect"
	"testing"

	"github.com/crossrelay/core/cmd/app"
	"github.com/crossrelay/core/internal/relay/domain/model"
	"github.com/crossrelay/core/kit/constants"
)

func TestResolveCandidatePorts(t *testing.T) {
	tests := []struct {
		name     string
		settings *model.AppSettings
		want     []int
	}{
		{
			name:     "nil settings falls back to P2P pool",
			settings: nil,
			want:     constants.P2PFallbackPorts,
		},
		{
			name:     "auto mode returns P2P fallback pool",
			settings: &model.AppSettings{PortMode: "auto", Port: 5000},
			want:     constants.P2PFallbackPorts,
		},
		{
			name:     "fixed mode returns single configured port",
			settings: &model.AppSettings{PortMode: "fixed", Port: 4999},
			want:     []int{4999},
		},
		{
			name:     "fixed mode with invalid port falls back to pool",
			settings: &model.AppSettings{PortMode: "fixed", Port: 0},
			want:     constants.P2PFallbackPorts,
		},
		{
			name:     "range mode returns inclusive port sequence",
			settings: &model.AppSettings{PortMode: "range", Port: 4300, PortRangeEnd: 4303},
			want:     []int{4300, 4301, 4302, 4303},
		},
		{
			name:     "range mode with end lower than start clamps to single port",
			settings: &model.AppSettings{PortMode: "range", Port: 4300, PortRangeEnd: 4200},
			want:     []int{4300},
		},
		{
			name:     "range mode caps at 30 ports",
			settings: &model.AppSettings{PortMode: "range", Port: 5000, PortRangeEnd: 5100},
			want: func() []int {
				out := make([]int, 30)
				for i := range out {
					out[i] = 5000 + i
				}
				return out
			}(),
		},
	}

	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			got := app.ResolveCandidatePorts(tc.settings)
			if !reflect.DeepEqual(got, tc.want) {
				t.Fatalf("resolveCandidatePorts() = %v, want %v", got, tc.want)
			}
		})
	}
}
