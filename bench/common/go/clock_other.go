//go:build !windows

package benchcommon

import (
	"math"
	"time"
)

const resolutionSamples = 16

func newClock() Clock {
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

	return Clock{Nanos: nanos, Resolution: resolution}
}
