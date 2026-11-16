package main

import (
	"bytes"
	"crypto/aes"
	"crypto/cipher"
	"encoding/binary"

	benchcommon "maxon.dev/bench/common"
)

const ctrCounter = 2

const blockBytesOut = 16

type impl struct {
	keySetup     func(key []byte) []byte
	keySetupInto func(output, key []byte) []byte
	block128     func(in []byte) []byte
	block128Into func(block []byte) []byte
	ctr128       func(nonce []byte, data []byte) []byte
	ctr256       func(nonce []byte, data []byte) []byte
	seal128      func(nonce, data, aad []byte) []byte
	seal128Into  func(output, nonce, data, aad []byte) []byte
	open128      func(nonce, sealed, aad []byte) []byte
	seal256      func(nonce, data, aad []byte) []byte
	seal256Into  func(output, nonce, data, aad []byte) []byte
}

func blockOutput(output []byte) []byte {
	if len(output) < blockBytesOut {
		return make([]byte, blockBytesOut)
	}
	return output
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
		keySetupInto: func(output, key []byte) []byte {
			c := must(aes.NewCipher(key))
			out := blockOutput(output)
			c.Encrypt(out, key)
			return out
		},
		block128Into: func(block []byte) []byte {
			a128.Encrypt(block, block)
			return block
		},
		ctr128:      func(n, d []byte) []byte { return ctr(a128, n, ctrCounter, d) },
		ctr256:      func(n, d []byte) []byte { return ctr(a256, n, ctrCounter, d) },
		seal128:     func(n, d, a []byte) []byte { return g128.Seal(nil, n, d, a) },
		seal128Into: func(o, n, d, a []byte) []byte { return g128.Seal(o[:0], n, d, a) },
		open128:     func(n, s, a []byte) []byte { return must(g128.Open(nil, n, s, a)) },
		seal256:     func(n, d, a []byte) []byte { return g256.Seal(nil, n, d, a) },
		seal256Into: func(o, n, d, a []byte) []byte { return g256.Seal(o[:0], n, d, a) },
	}
}

func main() {
	run := runner{benchcommon.ParseSettings("aes")}

	key128 := benchcommon.Filled(16, 0)
	key256 := benchcommon.Filled(32, 0)
	nonce := benchcommon.Filled(12, 100)
	blk := benchcommon.Filled(16, 200)
	aad := benchcommon.Filled(13, 50)
	small := benchcommon.Filled(64, 7)
	kib1 := benchcommon.Filled(1024, 7)
	kib16 := benchcommon.Filled(16384, 7)

	m := stdImpl(key128, key256)

	sealed16k := m.seal128(nonce, kib16, aad)

	run.PrintClock()

	run.measure("keysetup-128", 0, 20000, func([]byte) []byte { return m.keySetup(key128) }, nil)
	run.measure("keysetup-128-into", 0, 20000, func(prev []byte) []byte { return m.keySetupInto(prev, key128) }, nil)
	run.measure("block-128", 16, 200000, m.block128, blk)
	run.measure("block-128-into", 16, 200000, m.block128Into, blk)
	run.measure("ctr-128-16k", 16384, 1000, func([]byte) []byte { return m.ctr128(nonce, kib16) }, nil)
	run.measure("ctr-256-16k", 16384, 1000, func([]byte) []byte { return m.ctr256(nonce, kib16) }, nil)
	run.measure("gcm-128-seal-64", 64, 100000, func([]byte) []byte { return m.seal128(nonce, small, aad) }, nil)
	run.measure("gcm-128-seal-64-into", 64, 100000, func(prev []byte) []byte { return m.seal128Into(prev, nonce, small, aad) }, nil)
	run.measure("gcm-128-seal-1k", 1024, 10000, func([]byte) []byte { return m.seal128(nonce, kib1, aad) }, nil)
	run.measure("gcm-128-seal-1k-into", 1024, 10000, func(prev []byte) []byte { return m.seal128Into(prev, nonce, kib1, aad) }, nil)
	run.measure("gcm-128-seal-16k", 16384, 1000, func([]byte) []byte { return m.seal128(nonce, kib16, aad) }, nil)
	run.measure("gcm-128-seal-16k-into", 16384, 1000, func(prev []byte) []byte { return m.seal128Into(prev, nonce, kib16, aad) }, nil)
	run.measure("gcm-128-open-16k", 16384, 1000, func([]byte) []byte { return m.open128(nonce, sealed16k, aad) }, nil)
	run.measure("gcm-256-seal-16k", 16384, 1000, func([]byte) []byte { return m.seal256(nonce, kib16, aad) }, nil)
	run.measure("gcm-256-seal-16k-into", 16384, 1000, func(prev []byte) []byte { return m.seal256Into(prev, nonce, kib16, aad) }, nil)
}

type bytesResult []byte

func (b bytesResult) Answer() int64 {
	return int64(benchcommon.Digest(b))
}

type runner struct {
	benchcommon.Settings
}

func (r runner) measure(name string, bytesPerOp, baseIterations int, op func(prev []byte) []byte, seed []byte) {
	r.Measure(name, bytesPerOp, baseIterations, func(iterations int) benchcommon.Result {
		last := bytes.Clone(seed)
		for i := 0; i < iterations; i++ {
			last = op(last)
		}
		return bytesResult(last)
	})
}
