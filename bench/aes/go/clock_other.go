//go:build !windows

package main

import (
	"math"
	"time"
)

const resolutionSamples = 16

func newClock() clock {
	epoch := time.Now()
	nanos := func() int64 { return time.Since(epoch).Nanoseconds() }
	resolution := int64(math.MaxInt64)

	for i := 0; i < resolutionSamples; i++ {
		first := nanos()
		next := nanos()

		for next == first {
			next = nanos()
		}

		resolution = min(resolution, next-first)
	}

	return clock{nanos: nanos, resolution: resolution}
}
