package benchcommon

import (
	"fmt"
	"math"
	"os"
	"runtime"
	"strconv"
)

const (
	defaultRepeats = 5
	usageExit      = 2
	clockLine      = "clock"
	digestMask     = 0xFFFFFF
	byteMask       = 0xFF
)

type Result interface {
	Answer() int64
}

type Settings struct {
	Repeats int
	Divisor int
	Clock   Clock
}

func ParseSettings(program string) Settings {
	return Settings{
		Repeats: positiveArgument(program, 1, defaultRepeats),
		Divisor: positiveArgument(program, 2, 1),
		Clock:   newClock(),
	}
}

func positiveArgument(program string, position int, fallback int) int {
	if len(os.Args) <= position {
		return fallback
	}
	value, err := strconv.Atoi(os.Args[position])
	if err != nil || value < 1 {
		fmt.Fprintf(os.Stderr, "%s: argument %d is %q, which is not a positive integer\n", program, position, os.Args[position])
		os.Exit(usageExit)
	}
	return value
}

func Iterations(base, divisor int) int {
	return max(base/divisor, 1)
}

func Filled(count int, seed byte) []byte {
	b := make([]byte, count)
	for i := range b {
		b[i] = byte((i + int(seed)) & byteMask)
	}
	return b
}

func Digest(b []byte) int {
	sum := 0
	for i, v := range b {
		sum = (sum + int(v)*(i+1)) & digestMask
	}
	return sum
}

func (s Settings) PrintClock() {
	fmt.Printf("%s %d\n", clockLine, s.Clock.Resolution)
}

func (s Settings) Measure(name string, bytesPerOp, baseIterations int, run func(iterations int) Result) {
	iterations := Iterations(baseIterations, s.Divisor)
	best := int64(math.MaxInt64)
	answer := int64(0)
	for r := 0; r < s.Repeats; r++ {
		runtime.GC()
		start := s.Clock.Nanos()
		result := run(iterations)
		took := s.Clock.Nanos() - start
		best = min(best, took)
		answer = result.Answer()
		runtime.KeepAlive(result)
	}
	fmt.Printf("%s %d %d %d %d %d\n", name, bytesPerOp, iterations, s.Repeats, best, answer)
}

type Clock struct {
	Nanos      func() int64
	Resolution int64
}
