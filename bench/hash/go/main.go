package main

import (
	"crypto/sha256"
	"encoding/binary"
	"fmt"
	"hash/maphash"
	"strconv"
	"unsafe"

	benchcommon "maxon.dev/bench/common"
)

const (
	tableSize            = 1000000
	poolSize             = 8
	poolMask             = poolSize - 1
	flipSites            = poolSize - 1
	flipGaps             = flipSites - 1
	letterCount          = 26
	firstLetter          = 97
	byteFlipMask         = 1
	wordFlipMask         = 0x0101010101010101
	keyPrefix            = "key"
	scatter              = 2654435761
	accountNameBytes     = 24
	accountBaseID        = 1000
	accountBaseScore     = 77
	accountIDSite        = 0
	accountScoreSite     = 1
	accountNameFirstSite = 2
	nameFlipGaps         = flipSites - accountNameFirstSite - 1
	arrayElementStride   = 7919
	shaBufferBytes       = 1048576
	shaCarryBytes        = 32
	shaBufferSeed        = 7
	shaCarrySeed         = 200
	wordBytes            = int(unsafe.Sizeof(int64(0)))
)

func flipPosition(site, length, gaps int) int {
	return site * (length - 1) / gaps
}

func letters(count int) []byte {
	b := make([]byte, count)
	for i := range b {
		b[i] = byte(firstLetter + i%letterCount)
	}
	return b
}

func stringPool(size int) []string {
	base := letters(size)
	pool := make([]string, poolSize)
	for member := range pool {
		flipped := append([]byte(nil), base...)
		for site := 0; site < member; site++ {
			flipped[flipPosition(site, size, flipGaps)] ^= byteFlipMask
		}
		pool[member] = string(flipped)
	}
	return pool
}

func wordPool(elements int) [][]int64 {
	base := make([]int64, elements)
	for j := range base {
		base[j] = int64(j * arrayElementStride)
	}
	pool := make([][]int64, poolSize)
	for member := range pool {
		flipped := append([]int64(nil), base...)
		for site := 0; site < member; site++ {
			flipped[flipPosition(site, elements, flipGaps)] ^= wordFlipMask
		}
		pool[member] = flipped
	}
	return pool
}

type account struct {
	id    int64
	name  string
	score int64
}

func accountPool() []account {
	pool := make([]account, poolSize)
	for member := range pool {
		id, score, name := int64(accountBaseID), int64(accountBaseScore), letters(accountNameBytes)
		for site := 0; site < member; site++ {
			switch site {
			case accountIDSite:
				id ^= wordFlipMask
			case accountScoreSite:
				score ^= wordFlipMask
			default:
				name[flipPosition(site-accountNameFirstSite, accountNameBytes, nameFlipGaps)] ^= byteFlipMask
			}
		}
		pool[member] = account{id: id, name: string(name), score: score}
	}
	return pool
}

func keyStrings(count int) []string {
	keys := make([]string, count)
	for i := range keys {
		keys[i] = keyPrefix + strconv.Itoa(i)
	}
	return keys
}

func scrambledInts(count int) []int64 {
	values := make([]int64, count)
	for i := range values {
		values[i] = int64(i) * scatter
	}
	return values
}

func insertedKeys(keys []string) map[string]int64 {
	m := make(map[string]int64)
	for i, key := range keys {
		m[key] = int64(i)
	}
	return m
}

func insertedInts(values []int64) map[int64]struct{} {
	set := make(map[int64]struct{})
	for _, value := range values {
		set[value] = struct{}{}
	}
	return set
}

type fixtures struct {
	seed      maphash.Seed
	string8   []string
	string16  []string
	string64  []string
	string1k  []string
	string64k []string
	keys      []string
	keyed     map[string]int64
	ints      []int64
	intSet    map[int64]struct{}
	array16   [][]int64
	array1024 [][]int64
	accounts  []account
	shaBuffer []byte
	shaCarry  []byte
}

func newFixtures(divisor int) fixtures {
	keys := keyStrings(benchcommon.Iterations(tableSize, divisor))
	ints := scrambledInts(benchcommon.Iterations(tableSize, divisor))
	return fixtures{
		seed:      maphash.MakeSeed(),
		string8:   stringPool(8),
		string16:  stringPool(16),
		string64:  stringPool(64),
		string1k:  stringPool(1024),
		string64k: stringPool(65536),
		keys:      keys,
		keyed:     insertedKeys(keys),
		ints:      ints,
		intSet:    insertedInts(ints),
		array16:   wordPool(16),
		array1024: wordPool(1024),
		accounts:  accountPool(),
		shaBuffer: benchcommon.Filled(shaBufferBytes, shaBufferSeed),
		shaCarry:  benchcommon.Filled(shaCarryBytes, shaCarrySeed),
	}
}

type outcome struct {
	value int64
	held  any
}

func (o outcome) Answer() int64 {
	return o.value
}

func countChanges(iterations int, hash func(i int) uint64) outcome {
	var previous uint64
	changed := 0
	for i := 0; i < iterations; i++ {
		current := hash(i)
		if current != previous {
			changed++
		}
		previous = current
	}
	return outcome{value: int64(changed)}
}

func hashStrings(seed maphash.Seed, pool []string, iterations int) outcome {
	return countChanges(iterations, func(i int) uint64 { return maphash.String(seed, pool[i&poolMask]) })
}

func hashWords(seed maphash.Seed, pool [][]int64, iterations int) outcome {
	return countChanges(iterations, func(i int) uint64 {
		words := pool[i&poolMask]
		return maphash.Bytes(seed, unsafe.Slice((*byte)(unsafe.Pointer(unsafe.SliceData(words))), len(words)*wordBytes))
	})
}

func hashAccounts(seed maphash.Seed, pool []account, iterations int) outcome {
	var h maphash.Hash
	var word [wordBytes]byte
	return countChanges(iterations, func(i int) uint64 {
		a := &pool[i&poolMask]
		h.SetSeed(seed)
		binary.LittleEndian.PutUint64(word[:], uint64(a.id))
		h.Write(word[:])
		h.WriteString(a.name)
		binary.LittleEndian.PutUint64(word[:], uint64(a.score))
		h.Write(word[:])
		return h.Sum64()
	})
}

func requireRows(rows, iterations int) {
	if rows != iterations {
		panic(fmt.Sprintf("hash: a table row's fixture holds %d entries, not the %d iterations it reports", rows, iterations))
	}
}

func insertKeys(keys []string, iterations int) outcome {
	requireRows(len(keys), iterations)
	m := insertedKeys(keys)
	return outcome{value: int64(len(m)) + m[keys[len(keys)-1]], held: m}
}

func lookUpKeys(m map[string]int64, keys []string, iterations int) outcome {
	requireRows(len(keys), iterations)
	sum := int64(0)
	for _, key := range keys {
		sum += m[key]
	}
	return outcome{value: sum}
}

func insertInts(values []int64, iterations int) outcome {
	requireRows(len(values), iterations)
	set := insertedInts(values)
	return outcome{value: int64(len(set)), held: set}
}

func probeInts(set map[int64]struct{}, values []int64, iterations int) outcome {
	requireRows(len(values), iterations)
	hits := int64(0)
	for _, value := range values {
		if _, ok := set[value]; ok {
			hits++
		}
	}
	return outcome{value: hits}
}

func chainSha256(buffer, carry []byte, iterations int) outcome {
	carried := carry
	for i := 0; i < iterations; i++ {
		copy(buffer, carried)
		sum := sha256.Sum256(buffer)
		carried = sum[:]
	}
	return outcome{value: int64(benchcommon.Digest(carried))}
}

func main() {
	run := benchcommon.ParseSettings("hash")
	f := newFixtures(run.Divisor)

	run.PrintClock()

	run.Measure("string-8", 8, 2000000, func(n int) benchcommon.Result { return hashStrings(f.seed, f.string8, n) })
	run.Measure("string-16", 16, 2000000, func(n int) benchcommon.Result { return hashStrings(f.seed, f.string16, n) })
	run.Measure("string-64", 64, 2000000, func(n int) benchcommon.Result { return hashStrings(f.seed, f.string64, n) })
	run.Measure("string-1k", 1024, 200000, func(n int) benchcommon.Result { return hashStrings(f.seed, f.string1k, n) })
	run.Measure("string-64k", 65536, 2000, func(n int) benchcommon.Result { return hashStrings(f.seed, f.string64k, n) })
	run.Measure("map-string-insert", 0, tableSize, func(n int) benchcommon.Result { return insertKeys(f.keys, n) })
	run.Measure("map-string-lookup", 0, tableSize, func(n int) benchcommon.Result { return lookUpKeys(f.keyed, f.keys, n) })
	run.Measure("set-int-insert", 0, tableSize, func(n int) benchcommon.Result { return insertInts(f.ints, n) })
	run.Measure("set-int-contains", 0, tableSize, func(n int) benchcommon.Result { return probeInts(f.intSet, f.ints, n) })
	run.Measure("array-hash-16", 128, 1000000, func(n int) benchcommon.Result { return hashWords(f.seed, f.array16, n) })
	run.Measure("array-hash-1024", 8192, 50000, func(n int) benchcommon.Result { return hashWords(f.seed, f.array1024, n) })
	run.Measure("hasher-fields", 0, 2000000, func(n int) benchcommon.Result { return hashAccounts(f.seed, f.accounts, n) })
	run.Measure("hasher-object", 0, 2000000, func(n int) benchcommon.Result { return hashAccounts(f.seed, f.accounts, n) })
	run.Measure("sha256-1m", shaBufferBytes, 100, func(n int) benchcommon.Result { return chainSha256(f.shaBuffer, f.shaCarry, n) })
}
