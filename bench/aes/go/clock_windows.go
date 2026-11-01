package main

import (
	"fmt"
	"syscall"
	"unsafe"
)

const nanosPerSecond = 1_000_000_000

// time.Now on Windows moves in 0.5 ms steps, which biases the short rows low.
func newClock() clock {
	kernel32 := syscall.NewLazyDLL("kernel32.dll")
	counter := kernel32.NewProc("QueryPerformanceCounter")
	var frequency int64

	ok, _, err := kernel32.NewProc("QueryPerformanceFrequency").Call(uintptr(unsafe.Pointer(&frequency)))

	if ok == 0 {
		panic(fmt.Sprintf("aes: QueryPerformanceFrequency failed: %v", err))
	}

	if frequency <= 0 {
		panic(fmt.Sprintf("aes: QueryPerformanceFrequency answered %d, not a positive frequency", frequency))
	}

	return clock{
		nanos: func() int64 {
			var ticks int64

			if ok, _, err := counter.Call(uintptr(unsafe.Pointer(&ticks))); ok == 0 {
				panic(fmt.Sprintf("aes: QueryPerformanceCounter failed: %v", err))
			}

			// Split so that ticks * nanosPerSecond cannot overflow int64.
			return ticks/frequency*nanosPerSecond + ticks%frequency*nanosPerSecond/frequency
		},
		resolution: (nanosPerSecond + frequency - 1) / frequency,
	}
}
