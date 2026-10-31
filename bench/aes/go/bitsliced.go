// The encrypt path of stdlib/helpers/crypto/AesBitsliced.maxon and the GHASH and GCM of stdlib/AesGcm.maxon,
// function for function. A change to either is ported here in the same commit: otherwise the
// bitsliced arm times an algorithm the stdlib no longer runs, and its digests still agree, because
// both are correct AES.
package main

import (
	"crypto/subtle"
	"encoding/binary"
)

type planes = [8]uint64

const (
	m1 = 0x5555555555555555
	m2 = 0x3333333333333333
	m4 = 0x0F0F0F0F0F0F0F0F

	byteLaneMask   = 0x00FF00FF00FF00FF
	rowLaneMask    = 0x0000FFFF0000FFFF
	secondByteMask = 0xFF00
	wordMask       = 0xFFFFFFFF
	rowMask        = 0xFFFF
	byteMask       = 0xFF
	row1Nibble0    = 0x00000000000F0000
	row1Nibble3    = 0x00000000F0000000
	row1First3     = 0x000000000FFF0000
	row1Last3      = 0x00000000FFF00000
	row2LowPair    = 0x000000FF00000000
	row2HighPair   = 0x0000FF0000000000
	row3Nibble0    = 0x000F000000000000
	row3Nibble3    = 0xF000000000000000
	row3First3     = 0x0FFF000000000000
	row3Last3      = 0xFFF0000000000000

	blockBytes      = 16
	laneBytes       = 8
	batchBlocks     = 4
	rowWrapBits     = 12
	counterSwapBits = 24
	wordBytes       = 4
	ghashWordBytes  = 8
	nonceBytes      = 12
	tagBytes        = 16
	counterLimit    = 1 << 32
	maxPlaintext    = 68719476704
	bitsPerByte     = 8
	tagMaskCounter  = 1
	maskLeadBlocks  = 1
)

func byteWordShift(index, width int, bigEndian bool) uint {
	position := index
	if bigEndian {
		position = width - 1 - index
	}
	return uint(position * bitsPerByte)
}

func movesInOneAccess(width int) bool {
	return width == 2 || width == 4 || width == 8
}

func wordFitsBelow(off, width, limit int) bool {
	return limit >= width && off <= limit-width
}

func readWholeWord(src []byte, off, width int, bigEndian bool) uint64 {
	window := src[off:]
	switch width {
	case 2:
		if bigEndian {
			return uint64(binary.BigEndian.Uint16(window))
		}
		return uint64(binary.LittleEndian.Uint16(window))
	case 4:
		if bigEndian {
			return uint64(binary.BigEndian.Uint32(window))
		}
		return uint64(binary.LittleEndian.Uint32(window))
	case 8:
		if bigEndian {
			return binary.BigEndian.Uint64(window)
		}
		return binary.LittleEndian.Uint64(window)
	}
	panic("readOrderedWord: a width is not moved in one access")
}

func readOrderedWord(src []byte, off, width, limit int, bigEndian bool) uint64 {
	if movesInOneAccess(width) && wordFitsBelow(off, width, limit) {
		return readWholeWord(src, off, width, bigEndian)
	}
	var word uint64
	for index := 0; index < width; index++ {
		at := off + index
		var b byte
		if at < limit {
			b = src[at]
		}
		word |= uint64(b) << byteWordShift(index, width, bigEndian)
	}
	return word
}

var rcon = []uint64{0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80, 0x1B, 0x36}

func swapPlanes(p *planes, lo, hi int, mask uint64, stride uint) {
	a, b := p[lo], p[hi]
	upper := mask << stride
	p[lo] = (a & mask) | ((b & mask) << stride)
	p[hi] = ((a & upper) >> stride) | (b & upper)
}

func ortho(p *planes) {
	swapPlanes(p, 0, 1, m1, 1)
	swapPlanes(p, 2, 3, m1, 1)
	swapPlanes(p, 4, 5, m1, 1)
	swapPlanes(p, 6, 7, m1, 1)
	swapPlanes(p, 0, 2, m2, 2)
	swapPlanes(p, 1, 3, m2, 2)
	swapPlanes(p, 4, 6, m2, 2)
	swapPlanes(p, 5, 7, m2, 2)
	swapPlanes(p, 0, 4, m4, 4)
	swapPlanes(p, 1, 5, m4, 4)
	swapPlanes(p, 2, 6, m4, 4)
	swapPlanes(p, 3, 7, m4, 4)
}

func spreadBytes(column uint64) uint64 {
	halves := (column | (column << 16)) & rowLaneMask
	return (halves | (halves << 8)) & byteLaneMask
}

func interleave(even, odd uint64) uint64 {
	return spreadBytes(even) | (spreadBytes(odd) << 8)
}

func deinterleave(spread uint64, parity uint) uint64 {
	bytes := (spread >> (parity * 8)) & byteLaneMask
	halves := (bytes | (bytes >> 8)) & rowLaneMask
	return (halves | (halves >> 16)) & wordMask
}

func packColumns(c0, c1, c2, c3 uint64) (uint64, uint64) {
	return interleave(c0, c2), interleave(c1, c3)
}

func aesReadWord(src []byte, at int) uint64 {
	return uint64(binary.LittleEndian.Uint32(src[at:]))
}

func packBlock(src []byte, off int) (uint64, uint64) {
	c0 := aesReadWord(src, off)
	c1 := aesReadWord(src, off+wordBytes)
	c2 := aesReadWord(src, off+2*wordBytes)
	c3 := aesReadWord(src, off+3*wordBytes)
	return packColumns(c0, c1, c2, c3)
}

func appendBlock(out []byte, low, high uint64) []byte {
	out = binary.LittleEndian.AppendUint32(out, uint32(deinterleave(low, 0)))
	out = binary.LittleEndian.AppendUint32(out, uint32(deinterleave(high, 0)))
	out = binary.LittleEndian.AppendUint32(out, uint32(deinterleave(low, 1)))
	out = binary.LittleEndian.AppendUint32(out, uint32(deinterleave(high, 1)))
	return out
}

func loadBatch(p *planes, src []byte, blocks int) {
	for slot := 0; slot < 4; slot++ {
		var lo, hi uint64
		if slot < blocks {
			lo, hi = packBlock(src, slot*16)
		}
		p[slot] = lo
		p[slot+4] = hi
	}
	ortho(p)
}

func storeBatch(p *planes, out []byte, blocks int) []byte {
	ortho(p)
	for slot := 0; slot < 4; slot++ {
		if slot < blocks {
			out = appendBlock(out, p[slot], p[slot+4])
		}
	}
	return out
}

func swapRow2(word uint64) uint64 {
	return ((word & row2HighPair) >> 8) | ((word & row2LowPair) << 8)
}

func shiftRowsWord(word uint64) uint64 {
	row1 := ((word & row1Last3) >> 4) | ((word & row1Nibble0) << rowWrapBits)
	row2 := swapRow2(word)
	row3 := ((word & row3Nibble3) >> rowWrapBits) | ((word & row3First3) << 4)
	return (word & rowMask) | row1 | row2 | row3
}

func shiftRows(p *planes) {
	p[0] = shiftRowsWord(p[0])
	p[1] = shiftRowsWord(p[1])
	p[2] = shiftRowsWord(p[2])
	p[3] = shiftRowsWord(p[3])
	p[4] = shiftRowsWord(p[4])
	p[5] = shiftRowsWord(p[5])
	p[6] = shiftRowsWord(p[6])
	p[7] = shiftRowsWord(p[7])
}

func rowsUp(w uint64) uint64   { return (w >> 16) | (w << 48) }
func halfSwap(w uint64) uint64 { return (w << 32) | (w >> 32) }

func mixColumns(p *planes) {
	p0, p1, p2, p3, p4, p5, p6, p7 := p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]
	r0, r1, r2, r3 := rowsUp(p0), rowsUp(p1), rowsUp(p2), rowsUp(p3)
	r4, r5, r6, r7 := rowsUp(p4), rowsUp(p5), rowsUp(p6), rowsUp(p7)
	t0, t1, t2, t3 := p0^r0, p1^r1, p2^r2, p3^r3
	t4, t5, t6, t7 := p4^r4, p5^r5, p6^r6, p7^r7
	p[0] = t7 ^ r0 ^ halfSwap(t0)
	p[1] = t0 ^ t7 ^ r1 ^ halfSwap(t1)
	p[2] = t1 ^ r2 ^ halfSwap(t2)
	p[3] = t2 ^ t7 ^ r3 ^ halfSwap(t3)
	p[4] = t3 ^ t7 ^ r4 ^ halfSwap(t4)
	p[5] = t4 ^ r5 ^ halfSwap(t5)
	p[6] = t5 ^ r6 ^ halfSwap(t6)
	p[7] = t6 ^ r7 ^ halfSwap(t7)
}

func subBytes(p *planes) {
	x7, x6, x5, x4, x3, x2, x1, x0 := p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]

	y14 := x3 ^ x5
	y13 := x0 ^ x6
	y9 := x0 ^ x3
	y8 := x0 ^ x5
	t0 := x1 ^ x2
	y1 := t0 ^ x7
	y4 := y1 ^ x3
	y12 := y13 ^ y14
	y2 := y1 ^ x0
	y5 := y1 ^ x6
	y3 := y5 ^ y8
	t1 := x4 ^ y12
	y15 := t1 ^ x5
	y20 := t1 ^ x1
	y6 := y15 ^ x7
	y10 := y15 ^ t0
	y11 := y20 ^ y9
	y7 := x7 ^ y11
	y17 := y10 ^ y11
	y19 := y10 ^ y8
	y16 := t0 ^ y11
	y21 := y13 ^ y16
	y18 := x0 ^ y16

	t2 := y12 & y15
	t3 := y3 & y6
	t4 := t3 ^ t2
	t5 := y4 & x7
	t6 := t5 ^ t2
	t7 := y13 & y16
	t8 := y5 & y1
	t9 := t8 ^ t7
	t10 := y2 & y7
	t11 := t10 ^ t7
	t12 := y9 & y11
	t13 := y14 & y17
	t14 := t13 ^ t12
	t15 := y8 & y10
	t16 := t15 ^ t12
	t17 := t4 ^ t14
	t18 := t6 ^ t16
	t19 := t9 ^ t14
	t20 := t11 ^ t16
	t21 := t17 ^ y20
	t22 := t18 ^ y19
	t23 := t19 ^ y21
	t24 := t20 ^ y18

	t25 := t21 ^ t22
	t26 := t21 & t23
	t27 := t24 ^ t26
	t28 := t25 & t27
	t29 := t28 ^ t22
	t30 := t23 ^ t24
	t31 := t22 ^ t26
	t32 := t31 & t30
	t33 := t32 ^ t24
	t34 := t23 ^ t33
	t35 := t27 ^ t33
	t36 := t24 & t35
	t37 := t36 ^ t34
	t38 := t27 ^ t36
	t39 := t29 & t38
	t40 := t25 ^ t39

	t41 := t40 ^ t37
	t42 := t29 ^ t33
	t43 := t29 ^ t40
	t44 := t33 ^ t37
	t45 := t42 ^ t41

	z0 := t44 & y15
	z1 := t37 & y6
	z2 := t33 & x7
	z3 := t43 & y16
	z4 := t40 & y1
	z5 := t29 & y7
	z6 := t42 & y11
	z7 := t45 & y17
	z8 := t41 & y10
	z9 := t44 & y12
	z10 := t37 & y3
	z11 := t33 & y4
	z12 := t43 & y13
	z13 := t40 & y5
	z14 := t29 & y2
	z15 := t42 & y9
	z16 := t45 & y14
	z17 := t41 & y8

	t46 := z15 ^ z16
	t47 := z10 ^ z11
	t48 := z5 ^ z13
	t49 := z9 ^ z10
	t50 := z2 ^ z12
	t51 := z2 ^ z5
	t52 := z7 ^ z8
	t53 := z0 ^ z3
	t54 := z6 ^ z7
	t55 := z16 ^ z17
	t56 := z12 ^ t48
	t57 := t50 ^ t53
	t58 := z4 ^ t46
	t59 := z3 ^ t54
	t60 := t46 ^ t57
	t61 := z14 ^ t57
	t62 := t52 ^ t58
	t63 := t49 ^ t58
	t64 := z4 ^ t59
	t65 := t61 ^ t62
	t66 := z1 ^ t63
	s0 := t59 ^ t63
	s6 := t56 ^ ^t62
	s7 := t48 ^ ^t60
	t67 := t64 ^ t65
	s3 := t53 ^ t66
	s4 := t51 ^ t66
	s5 := t47 ^ t65
	s1 := t64 ^ ^s3
	s2 := t55 ^ ^t67

	p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7] = s7, s6, s5, s4, s3, s2, s1, s0
}

func addRoundKey(p *planes, keys []uint64, round int) {
	base := round * 8
	for i := range p {
		p[i] ^= keys[base+i]
	}
}

func encryptPlanes(p *planes, keys []uint64, rounds int) {
	addRoundKey(p, keys, 0)
	for round := 1; round < rounds; round++ {
		subBytes(p)
		shiftRows(p)
		mixColumns(p)
		addRoundKey(p, keys, round)
	}
	subBytes(p)
	shiftRows(p)
	addRoundKey(p, keys, rounds)
}

func subWord(w uint64) uint64 {
	var p planes
	p[0] = w
	ortho(&p)
	subBytes(&p)
	ortho(&p)
	return p[0] & 0xFFFFFFFF
}

func rotateWord(w uint64) uint64 { return ((w >> 8) | (w << 24)) & 0xFFFFFFFF }

func keySchedule(key []byte, rounds int) []uint64 {
	keyWords := len(key) / 4
	total := (rounds + 1) * 4
	words := make([]uint64, 0, total)
	for i := 0; i < keyWords; i++ {
		words = append(words, aesReadWord(key, i*wordBytes))
	}
	previous := words[keyWords-1]
	slot, constant := 0, 0
	for i := keyWords; i < total; i++ {
		word := previous
		if slot == 0 {
			word = subWord(rotateWord(previous)) ^ rcon[constant]
		} else if keyWords == 8 && slot == 4 {
			word = subWord(previous)
		}
		previous = word ^ words[i-keyWords]
		words = append(words, previous)
		slot++
		if slot == keyWords {
			slot = 0
			constant++
		}
	}
	return words
}

type BitslicedAes struct {
	keys   []uint64
	rounds int
}

func NewBitslicedAes(key []byte) *BitslicedAes {
	rounds := len(key)/4 + 6
	words := keySchedule(key, rounds)
	keys := make([]uint64, 0, (rounds+1)*8)
	var p planes
	for round := 0; round <= rounds; round++ {
		b := round * 4
		lo, hi := packColumns(words[b], words[b+1], words[b+2], words[b+3])
		for slot := 0; slot < 4; slot++ {
			p[slot] = lo
			p[slot+4] = hi
		}
		ortho(&p)
		keys = append(keys, p[:]...)
	}
	return &BitslicedAes{keys, rounds}
}

func (a *BitslicedAes) EncryptBlock(block []byte) []byte {
	if len(block) != blockBytes {
		panic("bitsliced aes: a block is 16 bytes")
	}
	var p planes
	out := make([]byte, 0, 16)
	loadBatch(&p, block, 1)
	encryptPlanes(&p, a.keys, a.rounds)
	return storeBatch(&p, out, 1)
}

func (a *BitslicedAes) XorKeyStream(nonce []byte, counter uint32, data []byte) []byte {
	blocks := (len(data) + 15) >> 4
	firstBlock := int(counter)
	nonceLow, nonceMid := streamNonceWords(nonce, firstBlock, blocks)
	var p planes
	output := make([]byte, 0, len(data))
	return a.xorBatches(&p, nonceLow, nonceMid, firstBlock, blocks, 0, data, output, 0)
}

func (a *BitslicedAes) XorKeyStreamPastFirstBlock(nonce []byte, counter uint32, data, output []byte) ([]byte, uint64, uint64) {
	streamBlocks := ((len(data) + 15) >> 4) + maskLeadBlocks
	firstBlock := int(counter)
	nonceLow, nonceMid := streamNonceWords(nonce, firstBlock, streamBlocks)
	var p planes
	a.encryptCounters(&p, nonceLow, nonceMid, uint64(firstBlock))
	output = xorBatchPastMask(output, data, &p)
	firstLane, secondLane := blockLanes(&p, 0)
	output = a.xorBatches(&p, nonceLow, nonceMid, firstBlock, streamBlocks, maskLeadBlocks, data, output, batchBlocks)
	return output, firstLane, secondLane
}

func streamNonceWords(nonce []byte, firstCounter, streamBlocks int) (uint64, uint64) {
	if len(nonce) != nonceBytes {
		panic("bitsliced aes: a nonce is 12 bytes")
	}
	if firstCounter+streamBlocks > counterLimit {
		panic("bitsliced aes: the block counter overflows")
	}
	return interleave(aesReadWord(nonce, 0), aesReadWord(nonce, 2*wordBytes)), aesReadWord(nonce, wordBytes)
}

func (a *BitslicedAes) encryptCounters(p *planes, nonceLow, nonceMid, counter uint64) {
	loadCounters(p, nonceLow, nonceMid, counter)
	encryptPlanes(p, a.keys, a.rounds)
}

func (a *BitslicedAes) xorBatches(p *planes, nonceLow, nonceMid uint64, firstCounter, streamBlocks, lead int, data, output []byte, startBlock int) []byte {
	for done := startBlock; done < streamBlocks; done += batchBlocks {
		a.encryptCounters(p, nonceLow, nonceMid, uint64(firstCounter)+uint64(done))
		output = xorBatch(output, data, p, (done-lead)<<4)
	}
	return output
}

func counterWord(counter uint64) uint64 {
	low := counter & wordMask
	return ((low & byteMask) << counterSwapBits) | ((low & secondByteMask) << 8) | ((low >> 8) & secondByteMask) | (low >> counterSwapBits)
}

func loadCounters(p *planes, nonceLow, nonceMid, firstCounter uint64) {
	for slot := 0; slot < batchBlocks; slot++ {
		p[slot] = nonceLow
		p[slot+batchBlocks] = interleave(nonceMid, counterWord(firstCounter+uint64(slot)))
	}
	ortho(p)
}

func slotLane(p *planes, slot int, parity uint) uint64 {
	low, high := p[slot], p[slot+batchBlocks]
	return deinterleave(low, parity) | (deinterleave(high, parity) << 32)
}

func blockLanes(p *planes, slot int) (uint64, uint64) {
	return slotLane(p, slot, 0), slotLane(p, slot, 1)
}

func xorLane(output, data []byte, at int, lane uint64) []byte {
	total := len(data)
	if at >= total {
		return output
	}
	if at+laneBytes <= total {
		return binary.LittleEndian.AppendUint64(output, binary.LittleEndian.Uint64(data[at:])^lane)
	}
	for index := at; index < total; index++ {
		output = append(output, data[index]^byte(lane>>(uint(index-at)*bitsPerByte)))
	}
	return output
}

func xorSlot(output, data []byte, p *planes, slot, at int) []byte {
	output = xorLane(output, data, at, slotLane(p, slot, 0))
	return xorLane(output, data, at+laneBytes, slotLane(p, slot, 1))
}

func xorBatch(output, data []byte, p *planes, off int) []byte {
	ortho(p)
	for slot := 0; slot < batchBlocks; slot++ {
		output = xorSlot(output, data, p, slot, off+slot*blockBytes)
	}
	return output
}

func xorBatchPastMask(output, data []byte, p *planes) []byte {
	ortho(p)
	for slot := maskLeadBlocks; slot < batchBlocks; slot++ {
		output = xorSlot(output, data, p, slot, (slot-maskLeadBlocks)*blockBytes)
	}
	return output
}

func swapUnits(w, mask uint64, shift uint) uint64 {
	return ((w & mask) << shift) | ((w >> shift) & mask)
}

func ghashReverseBytes(w uint64) uint64 {
	w = swapUnits(w, 0x00FF00FF00FF00FF, 8)
	w = swapUnits(w, 0x0000FFFF0000FFFF, 16)
	return (w << 32) | (w >> 32)
}

func ghashReverse(w uint64) uint64 {
	w = swapUnits(w, 0x5555555555555555, 1)
	w = swapUnits(w, 0x3333333333333333, 2)
	w = swapUnits(w, 0x0F0F0F0F0F0F0F0F, 4)
	return ghashReverseBytes(w)
}

func mulLow(x, y uint64) uint64 {
	const h0, h1, h2, h3 = 0x1111111111111111, 0x2222222222222222, 0x4444444444444444, 0x8888888888888888
	x0, x1, x2, x3 := x&h0, x&h1, x&h2, x&h3
	y0, y1, y2, y3 := y&h0, y&h1, y&h2, y&h3
	z0 := ((x0 * y0) ^ (x1 * y3) ^ (x2 * y2) ^ (x3 * y1)) & h0
	z1 := ((x0 * y1) ^ (x1 * y0) ^ (x2 * y3) ^ (x3 * y2)) & h1
	z2 := ((x0 * y2) ^ (x1 * y1) ^ (x2 * y0) ^ (x3 * y3)) & h2
	z3 := ((x0 * y3) ^ (x1 * y2) ^ (x2 * y1) ^ (x3 * y0)) & h3
	return z0 | z1 | z2 | z3
}

func foldRight(w uint64) uint64 { return w ^ (w >> 1) ^ (w >> 2) ^ (w >> 7) }
func foldSpill(w uint64) uint64 { return (w << 63) ^ (w << 62) ^ (w << 57) }

type ghashKey struct{ h0, h1, h0r, h1r, h2, h2r uint64 }

func ghashReadWord(src []byte, at int) uint64 {
	return binary.BigEndian.Uint64(src[at:])
}

func newGhashKey(sub []byte) ghashKey {
	h1 := ghashReadWord(sub, 0)
	h0 := ghashReadWord(sub, ghashWordBytes)
	h0r, h1r := ghashReverse(h0), ghashReverse(h1)
	return ghashKey{h0, h1, h0r, h1r, h0 ^ h1, h0r ^ h1r}
}

func (k *ghashKey) multiply(y1, y0 uint64) (uint64, uint64) {
	y0r, y1r := ghashReverse(y0), ghashReverse(y1)
	y2, y2r := y0^y1, y0r^y1r
	z0 := mulLow(y0, k.h0)
	z1 := mulLow(y1, k.h1)
	z2 := mulLow(y2, k.h2)
	z0h := mulLow(y0r, k.h0r)
	z1h := mulLow(y1r, k.h1r)
	z2h := mulLow(y2r, k.h2r)
	middle := z2 ^ z0 ^ z1
	middleHigh := z2h ^ z0h ^ z1h
	v0 := z0
	v1 := (ghashReverse(z0h) >> 1) ^ middle
	v2 := z1 ^ (ghashReverse(middleHigh) >> 1)
	v3 := ghashReverse(z1h) >> 1
	s3 := (v3 << 1) | (v2 >> 63)
	s2 := (v2 << 1) | (v1 >> 63)
	s1 := (v1 << 1) | (v0 >> 63)
	s0 := v0 << 1
	t1 := s1 ^ foldSpill(s0)
	t2 := s2 ^ foldRight(s0)
	t3 := s3 ^ foldRight(t1)
	u2 := t2 ^ foldSpill(t1)
	return t3, u2
}

func ghashBlockWords(data []byte, blockStart int) (uint64, uint64) {
	limit := len(data)
	if blockStart+blockBytes <= limit {
		return ghashReadWord(data, blockStart), ghashReadWord(data, blockStart+ghashWordBytes)
	}
	high := readOrderedWord(data, blockStart, ghashWordBytes, limit, true)
	low := readOrderedWord(data, blockStart+ghashWordBytes, ghashWordBytes, limit, true)
	return high, low
}

func (k *ghashKey) absorb(high, low uint64, data []byte) (uint64, uint64) {
	limit := len(data)
	for start := 0; start < limit; start += blockBytes {
		blockHigh, blockLow := ghashBlockWords(data, start)
		high, low = k.multiply(high^blockHigh, low^blockLow)
	}
	return high, low
}

type BitslicedGcm struct {
	cipher *BitslicedAes
	hash   ghashKey
}

func NewBitslicedGcm(key []byte) *BitslicedGcm {
	c := NewBitslicedAes(key)
	return &BitslicedGcm{c, newGhashKey(c.EncryptBlock(make([]byte, blockBytes)))}
}

func (g *BitslicedGcm) authenticate(aad, ciphertext []byte, firstLane, secondLane uint64) (uint64, uint64) {
	high, low := g.hash.absorb(0, 0, aad)
	high, low = g.hash.absorb(high, low, ciphertext)
	high, low = g.hash.multiply(high^uint64(len(aad)*bitsPerByte), low^uint64(len(ciphertext)*bitsPerByte))
	return high ^ ghashReverseBytes(firstLane), low ^ ghashReverseBytes(secondLane)
}

func appendTag(output []byte, high, low uint64) []byte {
	output = binary.BigEndian.AppendUint64(output, high)
	return binary.BigEndian.AppendUint64(output, low)
}

func requireNonce(nonce []byte) {
	if len(nonce) != nonceBytes {
		panic("bitsliced gcm: a nonce is 12 bytes")
	}
}

func (g *BitslicedGcm) Seal(nonce, plaintext, aad []byte) []byte {
	requireNonce(nonce)
	if len(plaintext) > maxPlaintext {
		panic("bitsliced gcm: the plaintext is too long")
	}
	sealed := make([]byte, 0, len(plaintext)+tagBytes)
	sealed, firstLane, secondLane := g.cipher.XorKeyStreamPastFirstBlock(nonce, tagMaskCounter, plaintext, sealed)
	tagHigh, tagLow := g.authenticate(aad, sealed, firstLane, secondLane)
	return appendTag(sealed, tagHigh, tagLow)
}

func (g *BitslicedGcm) Open(nonce, sealed, aad []byte) []byte {
	requireNonce(nonce)
	if len(sealed) < tagBytes || len(sealed)-tagBytes > maxPlaintext {
		panic("bitsliced gcm: the sealed message has no valid length")
	}
	split := len(sealed) - tagBytes
	ciphertext := append([]byte(nil), sealed[:split]...)
	received := append([]byte(nil), sealed[split:]...)
	plaintext := make([]byte, 0, len(ciphertext))
	plaintext, firstLane, secondLane := g.cipher.XorKeyStreamPastFirstBlock(nonce, tagMaskCounter, ciphertext, plaintext)
	tagHigh, tagLow := g.authenticate(aad, ciphertext, firstLane, secondLane)
	expected := appendTag(make([]byte, 0, tagBytes), tagHigh, tagLow)
	if subtle.ConstantTimeCompare(expected, received) != 1 {
		panic("bitsliced gcm: the tag does not match")
	}
	return plaintext
}
