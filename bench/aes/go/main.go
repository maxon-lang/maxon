package main

import (
	"crypto/aes"
	"crypto/cipher"
	"encoding/binary"
	"fmt"
	"os"
	"strconv"
	"time"
)

const (
	defaultRepeats = 5
	usageExit      = 2
	ctrCounter     = 2
	armStd         = "std"
	armBitsliced   = "bitsliced"
)

func filled(count int, seed byte) []byte {
	b := make([]byte, count)
	for i := range b {
		b[i] = byte(i + int(seed))
	}
	return b
}

func digest(b []byte) int {
	sum := 0
	for i, v := range b {
		sum = (sum + int(v)*(i+1)) & 0xFFFFFF
	}
	return sum
}

type impl struct {
	keySetup func(key []byte) []byte
	block128 func(in []byte) []byte
	ctr128   func(nonce []byte, data []byte) []byte
	ctr256   func(nonce []byte, data []byte) []byte
	seal128  func(nonce, data, aad []byte) []byte
	open128  func(nonce, sealed, aad []byte) []byte
	seal256  func(nonce, data, aad []byte) []byte
}

func ctr(block cipher.Block, nonce []byte, counter uint32, data []byte) []byte {
	iv := make([]byte, 16)
	copy(iv, nonce)
	binary.BigEndian.PutUint32(iv[12:], counter)
	out := make([]byte, len(data))
	cipher.NewCTR(block, iv).XORKeyStream(out, data)
	return out
}

func must[T any](value T, err error) T {
	if err != nil {
		panic(err)
	}
	return value
}

func stdImpl(key128, key256 []byte) impl {
	a128 := must(aes.NewCipher(key128))
	a256 := must(aes.NewCipher(key256))
	g128 := must(cipher.NewGCM(a128))
	g256 := must(cipher.NewGCM(a256))
	return impl{
		keySetup: func(key []byte) []byte {
			c := must(aes.NewCipher(key))
			out := make([]byte, 16)
			c.Encrypt(out, key)
			return out
		},
		block128: func(in []byte) []byte {
			out := make([]byte, 16)
			a128.Encrypt(out, in)
			return out
		},
		ctr128:  func(n, d []byte) []byte { return ctr(a128, n, ctrCounter, d) },
		ctr256:  func(n, d []byte) []byte { return ctr(a256, n, ctrCounter, d) },
		seal128: func(n, d, a []byte) []byte { return g128.Seal(nil, n, d, a) },
		open128: func(n, s, a []byte) []byte { return must(g128.Open(nil, n, s, a)) },
		seal256: func(n, d, a []byte) []byte { return g256.Seal(nil, n, d, a) },
	}
}

func bitslicedImpl(key128, key256 []byte) impl {
	a128, a256 := NewBitslicedAes(key128), NewBitslicedAes(key256)
	g128, g256 := NewBitslicedGcm(key128), NewBitslicedGcm(key256)
	return impl{
		keySetup: func(key []byte) []byte { return NewBitslicedAes(key).EncryptBlock(key) },
		block128: a128.EncryptBlock,
		ctr128:   func(n, d []byte) []byte { return a128.XorKeyStream(n, ctrCounter, d) },
		ctr256:   func(n, d []byte) []byte { return a256.XorKeyStream(n, ctrCounter, d) },
		seal128:  g128.Seal,
		open128:  g128.Open,
		seal256:  g256.Seal,
	}
}

type settings struct {
	repeats int
	divisor int
}

func positiveArgument(position int, fallback int) int {
	if len(os.Args) <= position {
		return fallback
	}
	value, err := strconv.Atoi(os.Args[position])
	if err != nil || value < 1 {
		fmt.Fprintf(os.Stderr, "aes: argument %d is %q, which is not a positive integer\n", position, os.Args[position])
		os.Exit(usageExit)
	}
	return value
}

func (s settings) iterations(base int) int {
	return max(base/s.divisor, 1)
}

func (s settings) measure(name string, bytesPerOp, baseIterations int, op func(prev []byte) []byte, seed []byte) {
	iterations := s.iterations(baseIterations)
	best := time.Duration(1<<63 - 1)
	var last []byte
	for r := 0; r < s.repeats; r++ {
		start := time.Now()
		last = seed
		for i := 0; i < iterations; i++ {
			last = op(last)
		}
		if took := time.Since(start); took < best {
			best = took
		}
	}
	fmt.Printf("%s %d %d %d %d %d\n", name, bytesPerOp, iterations, s.repeats, best.Nanoseconds(), digest(last))
}

func main() {
	if len(os.Args) < 2 {
		fmt.Fprintln(os.Stderr, "usage: aes std|bitsliced [repeats [divisor]]")
		os.Exit(usageExit)
	}

	run := settings{repeats: positiveArgument(2, defaultRepeats), divisor: positiveArgument(3, 1)}

	key128 := filled(16, 0)
	key256 := filled(32, 0)
	nonce := filled(12, 100)
	blk := filled(16, 200)
	aad := filled(13, 50)
	small := filled(64, 7)
	kib1 := filled(1024, 7)
	kib16 := filled(16384, 7)

	var m impl
	switch os.Args[1] {
	case armStd:
		m = stdImpl(key128, key256)
	case armBitsliced:
		m = bitslicedImpl(key128, key256)
	default:
		fmt.Fprintf(os.Stderr, "aes: unknown arm %q, expected %s or %s\n", os.Args[1], armStd, armBitsliced)
		os.Exit(usageExit)
	}

	sealed16k := m.seal128(nonce, kib16, aad)

	run.measure("keysetup-128", 0, 20000, func([]byte) []byte { return m.keySetup(key128) }, nil)
	run.measure("block-128", 16, 200000, m.block128, blk)
	run.measure("ctr-128-16k", 16384, 1000, func([]byte) []byte { return m.ctr128(nonce, kib16) }, nil)
	run.measure("ctr-256-16k", 16384, 1000, func([]byte) []byte { return m.ctr256(nonce, kib16) }, nil)
	run.measure("gcm-128-seal-64", 64, 100000, func([]byte) []byte { return m.seal128(nonce, small, aad) }, nil)
	run.measure("gcm-128-seal-1k", 1024, 10000, func([]byte) []byte { return m.seal128(nonce, kib1, aad) }, nil)
	run.measure("gcm-128-seal-16k", 16384, 1000, func([]byte) []byte { return m.seal128(nonce, kib16, aad) }, nil)
	run.measure("gcm-128-open-16k", 16384, 1000, func([]byte) []byte { return m.open128(nonce, sealed16k, aad) }, nil)
	run.measure("gcm-256-seal-16k", 16384, 1000, func([]byte) []byte { return m.seal256(nonce, kib16, aad) }, nil)
}
