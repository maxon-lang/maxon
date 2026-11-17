# A stored element has one door per operation, reached alike from concrete, generic and library code: Map, String and hash cost what Go's do

Class issue: #82 · Priority: P1

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. The plan lands once, after its last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | A type parameter's retain is the retain concrete code uses: retainCalleeForProtocol answers __str_retain for a byte record |  |  | Open question 1: a generic `var` of a `String` (a) the retain word co-owns and a var binding goes through the copy door (recommended), or (b) the retain word stays a copy and containers get a second word |
| [ ] | 2 | One String comparison graph, a word at a time, reached alike through == and the Equatable witness; the byte-range compare exits on one buffer |  | [structural-equality](structural-equality.md) 2 |  |
| [ ] | 3 | The Map rows are re-measured on both hosts and #106's gate (shared body at least 1.2 times a specialised body) is read | 1, 2 |  |  |
| [ ] | 4 | The software hash tier multiplies with the target's own high multiply, and the software pair mix is emitted once |  |  |  |
| [ ] | 5 | A store of a managed element whose kind the compiler knows is inline: release the occupant, store the value |  |  |  |
| [ ] | 6 | An element read that is only read borrows: the retain/release pair cancels when every use between only reads the value | 1 |  |  |
| [ ] | 7 | A short key's hash is one runtime call that takes the seed as an argument, with no separate seed load and no span guard | [structural-equality](structural-equality.md) 4 |  |  |
| [ ] | 8 | Map and Set are one table core with control words, capped tables, and a rehash that moves keys with no retain or release; two commits | 3, [structural-equality](structural-equality.md) 4, [structural-equality](structural-equality.md) 5 |  | Open questions 2 and 3: the table's storage shape (three columns per capped table, recommended, or a compiler-owned group storage kind) and whether the cached hash column is dropped (recommended on equal readings) or kept |
| [ ] | 9 | A generic type is compiled per instantiation, so a method costs the same at one call site or at ten; starts only if stage 3 confirms #106's gate | 3 |  | Stage 3's reading of #106's gate: starts only if the gate is confirmed; otherwise withdrawing #106 is the maintainer's decision |
| [ ] | 10 | The closing measurement: every Map and hash row re-measured on both hosts, each at 1.0 or below against Go or each remaining term naming an open issue | 1, 2, 3, 4, 5, 6, 7, 8, 9 |  |  |

## Problem

`Map with String, Count` is 7 to 12 times slower than Go's `map[string]int` on lookup and 3 to 4 times on insert, and the seeded hash is 1.3 to 2.5 times slower than Go's on short keys. The hash function is not the Map gap: the `software` tier reads within 1% of `aes` on both Map rows.

The shared mechanism is that **each thing a container does to a stored element has a cheap door that only some callers reach.** The same value is retained, compared, stored, read, moved and released differently depending on whether the code is concrete, a generic body, a library body or the compiler's own accessor:

| operation | cheap door | who reaches it | what the others do |
|---|---|---|---|
| retain | `__str_retain` (an increment; a copy only for an immortal literal) | concrete code | a type parameter's descriptor and witness words come from `retainCalleeForProtocol`, which answers `__str_clone`: an allocation and a byte copy per retain (#199) |
| equality | `__str_eq` | the `==` operator (`emitStrEqCall`) | the `Equatable` witness reaches `String.equals` in `stdlib/String.maxon`, which runs `bytesEqual`, one throwing `byteAt` per byte per side; `BuiltinConformanceRuntime` builds a second graph under the same impl name (#200). `__str_eq` itself compares a byte per step with no exit for one record (`emitByteRangeCompareLoop`) |
| store | the inline slot store | a trivial element | `expandElementStore` sends every managed element to the out-of-line `__managed_set` through `guardManagedElement`, even when the element type is known (#202) |
| read | the borrowing read (`emitContainerElementAccessor`) | the compiler's own element reads | `Array.get` returns an owned value (`emitOwnedValueReturn`) and no pass removes the retain and release around a read that is only compared: `doorFusesInPlace` answers false for the `string` family, and `CancelRetainRelease` stops at `__str_eq` (#208) |
| move | none | nobody | `Map.rehash` reads each key with a retain, writes four parallel columns through `insertAtSlot`, and releases the old column key by key (#207) |
| release of one slot | none | nobody | `Array.clearSlot` is two swaps and two `setLength` calls per column, whatever the element owns (#3) |
| hash | one call with the seed passed in | nobody | `hashString` calls `__hash_seed`, then `__hash_bytes`, which bounds-checks and calls `__hash_run` (#84); the `software` tier builds its 64×64→128 multiply from 32-bit pieces on every target (`emitHashMulHigh`, #85) |

`Map` and `Set` are also two copies of one table (`stdlib/Map.maxon`, `stdlib/Set.maxon`; only the slot scan is shared, in `stdlib/helpers/hashtable`), so each of these is paid, and would be fixed, twice.

Witness dispatch is not the cause. A hand-specialised `Map` removes 69 to 80% of the lookup gap, but 93 to 98% of what it removes is the retain and the equality rows above; the shared body against a concrete body doing the same work is 1.3 to 5.1% of the gap.

## Measurements

The instrument is `bench/hash/hash.maxon` against Go, run by `scripts/bench-hash.py`; its rows are in `docs/hash-benchmark-log.md` and the attribution in `docs/optimization-log.md` ("where `Map with String, Count` loses to Go's `map[string]int`"). Only rows from one host compare. `vs Go` is Maxon's time over Go's; the work drives it to 1.0 or below.

**x64-linux (Xeon 2.30 GHz, go1.24.7), `--runs 5`.**

| workload | `aes` Maxon / Go | vs Go | `software` Maxon / Go | vs Go |
|---|---|---:|---|---:|
| map-string-lookup | 384.58 / 32.77 ns | 11.74 | 391.59 / 36.76 ns | 10.65 |
| map-string-insert | 660.98 / 166.10 ns | 3.98 | 747.08 / 192.41 ns | 3.88 |
| set-int-insert | 98.27 / 90.49 ns | 1.09 | 97.17 / 86.58 ns | 1.12 |
| set-int-contains | 30.31 / 26.76 ns | 1.13 | 29.41 / 25.45 ns | 1.16 |
| string-8 | 1225.1 / 1909.6 MB/s | 1.56 | 950.9 / 1775.5 MB/s | 1.87 |
| string-16 | 2482.2 / 4077.3 MB/s | 1.64 | 1856.0 / 3643.0 MB/s | 1.96 |
| string-64 | 9672.7 / 12248.1 MB/s | 1.27 | 4556.6 / 8839.9 MB/s | 1.94 |
| string-1k | 39562.4 / 13740.4 MB/s | 0.35 | 7846.4 / 14826.4 MB/s | 1.89 |
| string-64k | 57752.9 / 12692.6 MB/s | 0.22 | 8582.3 / 13922.2 MB/s | 1.62 |
| array-hash-16 | 14702.3 / 19541.2 MB/s | 1.33 | 5631.1 / 13069.5 MB/s | 2.32 |
| array-hash-1024 | 53914.0 / 13087.5 MB/s | 0.24 | 8495.6 / 14156.5 MB/s | 1.67 |
| hasher-fields | 18.98 / 15.95 ns | 1.19 | 22.73 / 15.14 ns | 1.50 |
| hasher-object | 22.61 / 16.10 ns | 1.40 | 29.10 / 15.35 ns | 1.90 |

**arm64-macos (Apple M3, go1.27.3), `--runs 5`.**

| workload | `aes` Maxon / Go | vs Go | `software` Maxon / Go | vs Go |
|---|---|---:|---|---:|
| map-string-lookup | 237.60 / 33.40 ns | 7.11 | 237.72 / 32.73 ns | 7.26 |
| map-string-insert | 204.96 / 63.19 ns | 3.24 | 206.57 / 64.05 ns | 3.23 |
| set-int-insert | 49.43 / 41.25 ns | 1.20 | 49.28 / 39.59 ns | 1.24 |
| set-int-contains | 22.62 / 21.45 ns | 1.05 | 22.79 / 18.97 ns | 1.20 |
| string-8 | 1380.8 / 1731.1 MB/s | 1.25 | 1242.4 / 1819.9 MB/s | 1.46 |
| string-16 | 2756.3 / 3818.0 MB/s | 1.39 | 2486.5 / 3630.1 MB/s | 1.46 |
| string-64 | 8164.4 / 14487.8 MB/s | 1.77 | 6114.9 / 10712.5 MB/s | 1.75 |
| string-1k | 36786.0 / 16088.9 MB/s | 0.44 | 11567.2 / 18230.8 MB/s | 1.58 |
| string-64k | 44236.2 / 14726.4 MB/s | 0.33 | 12039.8 / 15955.7 MB/s | 1.33 |
| array-hash-16 | 9823.2 / 23570.8 MB/s | 2.40 | 8039.5 / 18329.4 MB/s | 2.28 |
| array-hash-1024 | 43501.2 / 15041.3 MB/s | 0.35 | 12039.5 / 16187.7 MB/s | 1.34 |
| hasher-fields | 27.81 / 10.98 ns | 2.53 | 17.94 / 12.79 ns | 1.40 |
| hasher-object | 30.71 / 11.03 ns | 2.78 | 26.19 / 12.82 ns | 2.04 |

**Attribution of the Map rows (arm64-macos, `aes` tier, best of 7 interleaved rounds of 5, ns per operation; sizes 1,000,000 / 62,500 / 1,000 entries).** G is `Map with String, Count`; S is a textual `Key = String`, `Value = Count` copy; K is S inlined into the loop with borrowed keys; L is K with one `{tag, value}` element per slot beside a key column.

Lookup:

| component | 1,000,000 | 62,500 | 1,000 | stage |
|---|---:|---:|---:|---|
| G / Go / gap | 234.14 / 32.01 / 202.14 | 75.65 / 8.16 / 67.49 | 66.92 / 6.37 / 60.55 | |
| equality through `String.equals`, a byte at a time | 72.75 (36.0%) | 35.46 (52.5%) | 27.79 (45.9%) | 2 |
| a heap copy of the stored key at every hit | 16.28 (8.1%) | 6.19 (9.2%) | 11.48 (19.0%) | 1 |
| stored keys are scattered copies, one more cache miss per hit | 74.29 (36.8%) | 0.88 (1.3%) | 0.62 (1.0%) | 1 |
| `get`, `findSlot` and the hash out of line, a retain and release per hit (S − K) | 21.09 (10.4%) | 7.67 (11.4%) | 8.18 (13.5%) | 6, 9; #120 |
| `__str_eq` against Go's `memequal` | 5.56 (2.7%) | 5.09 (7.5%) | 3.95 (6.5%) | 2 |
| the shared body against a concrete one doing the same work | 2.63 (1.3%) | 2.84 (4.2%) | 3.08 (5.1%) | 9 |
| four columns against one element per slot (K − L) | 3.30 (1.6%) | 1.12 (1.7%) | 1.00 (1.7%) | 8 |
| unattributed residual | 2.31 (1.1%) | 3.01 (4.5%) | 2.16 (3.6%) | |

Insert, with the map growing:

| component | 1,000,000 | 62,500 | 1,000 | stage |
|---|---:|---:|---:|---|
| G / Go / gap | 196.31 / 68.10 / 128.21 | 139.24 / 43.83 / 95.41 | 126.58 / 38.21 / 88.37 | |
| growth beyond Go's (grown − presized, each language) | 54.25 (42.3%) | 46.69 (48.9%) | 38.90 (44.0%) | 8 |
| the shared body's store, presized (key copies and dispatch) | 25.70 (20.0%) | 9.31 (9.8%) | 15.86 (17.9%) | 1, 9 |
| the insert path out of line (S − K, presized) | 13.34 (10.4%) | 5.96 (6.3%) | 5.65 (6.4%) | 9; #120 |
| the key store: a retain and an out-of-line `__managed_set` | 8.12 (6.3%) | 7.94 (8.3%) | 7.95 (9.0%) | 5 |
| `withIterator()` in the benchmark's own loop | −1.60 | 8.79 (9.2%) | 13.27 (15.0%) | #201 |
| cache-miss term of L against Go, presized | 16.37 (12.8%) | | | 8 |
| unattributed residual | 7.50 (5.8%) | 7.71 (8.1%) | 4.51 (5.1%) | |

Further readings the stages use:

- Allocations per 1,000 operations: 1,000 for G's lookups and none for S's; 3,591 for G's inserts and 1,067 for S's.
- Growth per entry (insert into a growing map minus into a presized one), ns: G 90.3 / 78.8 / 67.5; S 82.4 / 45.4 / 46.6; S inlined into the fill loop 87.1 / 47.6 / 47.0; inlined with one `{tag, value}` element per slot beside a key column 67.2 / 45.1 / 41.0; Go 36.0 / 32.1 / 28.6. Both languages end at the same load (0.477 to 0.488); Maxon makes 1.57 moves per final entry. In cache the specialised body is 13 to 18 ns per entry over Go; at 1,000,000 it pays 36 ns per entry more than in cache, where Go pays 4 to 7.
- `Map with Count, Count` is 2.4 to 3.5 times Go on lookup (50.76 / 17.61 / 16.44 against 21.27 / 6.20 / 4.65 ns) and its hand-specialised copy 1.8 to 2.3 times. Pure dispatch for a scalar key costs 5 to 13 ns per lookup.
- `Set.contains` is near Go only because its one call site in the benchmark is spliced by the called-once rule; `Map.get` has two sites and `findSlot` three, so they stay shared.
- With the retain and equality rows out of the path, the arms put lookup at S's reading: about 75 ns at 1,000,000 (from 234) and 28 to 31 ns in cache (from 67 to 76), which is 2.3 to 4.0 times Go.
- A String store into an `Array with String` takes about 10 ns against 0.2 ns for a word (8.6 ns against Go's 0.6 for a 16-byte header).
- `a == b` on 9-byte Strings takes 7 ns and `a.equals(b)` 48 ns; one `text == text` of 100,000 bytes takes about 52,000 ns.
- Borrowing the stored key for the compare takes a hand-specialised lookup from 74.1 / 28.2 / 25.5 ns to 69.4 / 25.8 / 22.6: the retain and release cost 2.4 to 4.7 ns per hit.
- A static-form fold (`empty` / `combined` / `combinedBytes`) makes no allocation where the object form makes one per fold.

## Design

**One door per element operation, reached alike from every kind of code.** For each operation in the table above there is one place that says how it is done for a given element kind, and concrete code, a generic body's descriptor or witness word, a library body and the compiler's own accessor all read that place.

- **Retain.** `retainCalleeForProtocol` is the one spelling, and it answers what concrete code calls: for a byte record, `__str_retain`, which has `__str_clone`'s signature and already clones only an immortal literal. The concrete sites that name `__str_retain` themselves read the same function. The stage checks every arm of `retainCalleeForProtocol` against its concrete counterpart, the buffer-record arm first: a generic `extension Map` method pushing 1,000 `ByteArray` keys into an array makes 1,006 allocations where the same concrete loop makes 6.
- **Equality.** One String comparison graph serves `==` and the `Equatable` witness; `stdlib/String.maxon` declares no second body. The one byte-range compare (`emitByteRangeCompareLoop`, shared with `__managed_eq`) returns at once when both operands are the same buffer and compares a word per step. The companion issue's leaf table (the one verdict of #78 ruling 11a) then holds this as `String`'s equality.
- **Store.** A store of a managed element whose kind the compiler knows is inline: release the occupant, store the value. The out-of-line `__managed_set` serves only an element kind the site cannot name.
- **Read.** Whether an element read borrows is decided by what the reader does with the value, in the one pass that cancels retain and release pairs, not by the return convention of the call that read it. A `string`-family pair cancels when every use between the two only reads the value; a read-only runtime callee (`__str_eq`, a hash computation) does not end the search.
- **Move and release of a slot.** `__ManagedMemory` gains two members: move one slot's element to a slot of another buffer with no retain or release, and release one slot in place. Both are expanded inline from the element plan, so a trivial element costs a word copy or nothing. A new member reaches the library through a bootstrap chain.
- **One table.** `Map` and `Set` are one table core in `stdlib/helpers/hashtable`; a `Set` is the table without a value column.
- **Hash.** A key's hash is one runtime call that takes the seed as an argument. The `software` tier takes the high half of its multiply from `StdBinOpcode.mulHighUnsigned` where the target lowers it and from the 32-bit decomposition elsewhere (`wasm32-wasi`), chosen by the predicate `StrengthReduceDivision` already asks; the digest is the same on every target (#78 ruling 4). The software pair mix, written once in the seed generator (`buildHashGenerate`) and once in `buildHashWord`'s software arm, becomes one emitter.

**What was read in Go, and what is taken.**
- `internal/bytealg/equal_arm64.s` (`memequal`): a zero length and equal pointers return before any load, then 16 and 32 bytes per step. Adopted for the one byte-range compare. Equality is still by content (#78 ruling 11a.1): the identity exit is taken only where the comparison is the byte loop, for which one buffer is always equal to itself.
- `internal/runtime/maps/map.go`, `table.go`, `group.go` (the Swiss table of Go 1.24): groups of 8 slots under one control word holding 7 hash bits per slot, so a probe examines 8 slots in one word compare and calls equality only on a control-byte match; a table is capped at 1,024 slots (`maxTableCapacity`) and a map is a directory of tables selected by the hash's upper bits, so a grow moves at most one small table and stays in cache; growth re-hashes each key with the map's seed and moves it by memory copy. Adopted for the table stage: control bytes replace the state column and the cached-hash column, tables are capped and split, and a move is a move.
- `internal/runtime/maps/memhash_aes.go`, `runtime_hash64.go`: `StrHash` is one call taking the seed; the portable fold takes its product from `bits.Mul64`, which is one instruction on x64 and arm64. Adopted for the two hash stages.
- Departures, in Maxon's terms. Go's `map` is runtime code with per-key-kind fast paths, and Go's generics share a body per GC shape and pay dictionary dispatch. Maxon's `Map` is library code, and #106 rules that a generic type is compiled per instantiation, because a program's cost must be a property of the program and not of how many call sites a method has. Go's values are traced, so a move is free of counts; Maxon's are counted, so the move needs a buffer member that says so.

**The measurement rule.** A stage's effect on a row is an A/B of two arms in one session, interleaved. Until #204 lands, every `aes`-tier reading checks that the round-key table of the measured binary does not cross a page, or is read beside the `software` tier.

### Open design questions

1. **A generic `var` of a `String`.** A `var` of type-parameter type owns its value through the descriptor's retain word, which for `String` is a copy today, matching the concrete binding door. Once the retain word is an increment, such a `var` would share where concrete code copies. Options: (a) the retain word co-owns and a `var` binding goes through the copy door (`typeParamCopy`), as the concrete binding does; (b) the retain word stays a copy and containers get a second, co-owning word. Recommendation: (a): one word means one thing, and the binding door is where "this storage is mutable" is known.
2. **The table's storage shape.** Options: (a) per table of at most 1,024 slots, three columns (control words, keys, values); (b) a new compiler-owned storage kind holding a group's control word, keys and values together. Recommendation: (a) first. Two arrays against four already take growth at 1,000,000 from 87 to 67 ns per entry, and a capped table keeps all three columns in cache; (b) only if the closing measurement leaves a layout term of 5% or more.
3. **The cached hash.** The table keeps a 32-bit hash per slot today to skip equality and to avoid re-hashing on growth. Options: (a) drop the column, guard equality with the 7 control bits and re-hash on growth, as Go does; (b) keep a full-hash column beside the control words. Recommendation: measure both arms in the table stage with `String` keys at 1,000,000 entries, where re-hashing touches each key's bytes; default to (a) on equal readings.

## Stages

Each stage is one `/land` commit, green alone, unless it says `/plan-and-land` or names two commits. "The hashing issue" is the companion enhancement that implements #78 ruling 11a (the one verdict, `hash(sink HashSink)`, a seed per table).

1. **A type parameter's retain is the retain concrete code uses.**
   - Changes: the retain row of the design, and open question 1's answer.
   - Area: `SignatureIndex.maxon` (`retainCalleeForProtocol`), its readers in `SignatureIndexGenericTypes.maxon` and `IR/LayoutDescriptor.maxon`, the concrete retain sites (`ParserOwnership.maxon`, `ParserTypeDeclarations.maxon`'s `holdRetainPlan`, `SignatureIndexSettle.maxon`), the generic `var` binding door.
   - Depends on: nothing.
   - Discharges: #199.
   - Shown by: an allocation census spec, red first: 1,000 `get` hits on a `Map with String, Count` allocate nothing (today 1,000), and 1,000 inserts allocate what a concrete copy does (today 3,591 against 1,067); the `ByteArray` extension loop allocates what the concrete loop does; a spec that a write through a generic `var` of a `String` does not reach its source.

2. **One String comparison, a word at a time, reached alike through `==` and the witness.**
   - Changes: the equality row of the design.
   - Area: `Runtime/StringRuntime.maxon` (`buildStrEq`, `emitByteRangeCompareLoop`), `Runtime/BuiltinConformanceRuntime.maxon`, `stdlib/String.maxon` (`equals`, `bytesEqual`'s equality caller).
   - Depends on: nothing; it can run beside stage 1. It lands before the hashing issue's structural-equality stage, which edits the same sites.
   - Discharges: #200.
   - Shown by: an ir-spec, red first, that the `Equatable` witness slot of `String` and `==` call one symbol and that its loop loads words; the repro's three readings as acceptance on arm64-macos (`a == b` 7 ns, `a.equals(b)` 48 ns, `text == text` of 100,000 bytes about 52,000 ns): the first two equal, the third independent of length.

3. **The Map rows are re-measured, and #106's gate is read.**
   - Changes: rows appended by `scripts/bench-hash.py --note` on both hosts, and the attribution arms re-run on arm64-macos.
   - Area: `docs/hash-benchmark-log.md`, `docs/optimization-log.md`.
   - Depends on: stages 1 and 2.
   - Discharges: the measurement #82 asks for.
   - Shown by: the gate #106 states. Arms: the shared body and a hand-specialised copy calling the same equality and the same retain. Keys: `String` and `Count`. Sizes: 62,500 and 1,000,000. Median of three sessions. #106 is confirmed if, for either key type, shared lookup time is at least 1.2 times the specialised time at both sizes; otherwise withdrawing it is the maintainer's decision on those numbers. Expected lookup: about 75 ns at 1,000,000 and 28 to 31 ns in cache.

4. **The `software` tier multiplies with the target's own high multiply, and the software pair mix is emitted once.**
   - Changes: the hash row of the design, for the `software` tier.
   - Area: `Runtime/HashRuntime.maxon` (`emitHashMulHigh`, `emitHashFinish`, `buildHashGenerate`, `buildHashWord`).
   - Depends on: nothing.
   - Discharges: #85, #197.
   - Shown by: an ir-spec, red first, that `__hash_software` holds one high multiply on x64 and arm64; the pinned-seed `MAXON_HASH_TIER=software` digests unchanged on every lane, `wasm32-wasi` included; the `software` rows of the tables above as acceptance (strings 8 B to 1 KiB 1.87 to 1.96, 64 KiB 1.62, array-hash-16 2.32, array-hash-1024 1.67, hasher-fields 1.50, hasher-object 1.90 on x64-linux).

5. **A store of a managed element of known kind is inline.**
   - Changes: the store row of the design.
   - Area: `IR/Std/InlineManagedPrimitives.maxon` (`expandElementStore`, `emitDetachingStoreGuards`, `guardManagedElement`).
   - Depends on: nothing.
   - Discharges: #202.
   - Shown by: an ir-spec, red first, that `column.set(i, value: key)` on an `Array with String` holds no call to `__managed_set`; the store repro as acceptance (about 10 ns against 0.2 ns for a word on arm64-macos).

6. **An element read that is only read borrows.** `/plan-and-land`.
   - Changes: the read row of the design.
   - Area: `IR/Std/CancelRetainRelease.maxon`, `Runtime/OwnershipDoors.maxon` (`doorFusesInPlace` and the purity roster it reads), `HoldResolution.maxon`.
   - Depends on: stage 1 (the pair is a retain and a release only once the retain is not a copy).
   - Discharges: #208.
   - Shown by: an ir-spec, red first, that `let existing = try keys.get(i) …` followed only by `existing == key` holds neither `__str_retain` nor `__str_decref`; a spec that a read kept past a store of the same slot still retains; 2.4 to 4.7 ns per lookup hit as acceptance. The worker's survey reads how Swift's ownership SSA turns a copy with only borrowed uses into a borrow, and Perceus's borrow inference.

7. **A short key's hash is one runtime call.**
   - Changes: the hash row of the design, for the call shape: the `String` leaf's fold passes the record's bytes and the table's seed to one entry, with no separate seed load and no span guard a `String`'s own bytes cannot fail. The worker measures whether the `aes` tier should fold keys below a length with the multiply, as Go's `MemHash` does.
   - Area: `Runtime/HashRuntime.maxon` (`buildHashBytes`, `buildHashRun`, `emitHashTierDispatch`), the `String`, `Character` and `Array` leaf folds.
   - Depends on: the hashing issue's `hash(sink HashSink)` stage, which removes the process fold this path starts from.
   - Discharges: #84.
   - Shown by: an ir-spec, red first, that a `String` key's hash is one call; rows string-8, string-16 and string-64 on the `aes` tier as acceptance (1.56, 1.64, 1.27 on x64-linux; 1.25, 1.39, 1.77 on arm64-macos).

8. **One table for `Map` and `Set`: control words, capped tables, and a rehash that moves.** `/plan-and-land`, two commits.
   - Changes: (a) the compiler's two `__ManagedMemory` members, move a slot and release a slot, on every target; (b) the table core, with open questions 2 and 3 answered, `Map` and `Set` over it, `remove` releasing a slot in place, growth moving each key with no retain or release. Iteration order stays unspecified. The survey states what an iterator sees when its table grows or splits under it, and keeps today's answer.
   - Area: (a) `IR/Std/InlineManagedPrimitives.maxon`, `Runtime/ManagedMemoryRuntime.maxon`, the builtin member table; (b) `stdlib/helpers/hashtable`, `stdlib/Map.maxon`, `stdlib/Set.maxon`, `stdlib/Array.maxon` (`clearSlot` goes when it has no caller).
   - Depends on: stage 3; the hashing issue's `hash(sink HashSink)` stage (the 64-bit internal hash the control bits come from) and its seed-per-table stage (both edit `Map` and `Set`). (b) depends on (a) and on the seed that carries it.
   - Discharges: #207, #3.
   - Shown by: ir-specs, red first, that a rehash of a `Map with String, Count` holds no retain and no release per moved key, and that `remove` on a table of trivial elements holds no swap; the growth repro as acceptance (on arm64-macos, insert into a growing map 105 ns per key and upsert into a grown map 78 ns, against Go's 38 ns for the same 1,000-key fill; growth per entry against Go's 36.0 / 32.1 / 28.6 ns); the set-int rows do not regress; the scale ladder stays linear.

9. **A generic type is compiled per instantiation.** `/plan-and-land`. Starts only if stage 3 confirms #106's gate.
   - Changes: what #106 rules. The plan covers the five points #106 lists under "What the plan must cover": what changes for `Array`; a method of a generic type costs the same at one call site or at ten; whether an instance is checked once as its template or again as concrete code (with E3106, E2010 and E2015 resolved first in the second case); what specialisation leaves to concrete code; whether a static call through a bare generic base with an unbound `T` stays legal.
   - Area: set by its plan.
   - Depends on: stage 3.
   - Discharges: the shared-body and out-of-line terms of the attribution tables.
   - Shown by: `Map with Count, Count` lookup (2.4 to 3.5 times Go, specialised copy 1.8 to 2.3 times); the scale-test ladder linear in the number of instantiations.

10. **The closing measurement.**
    - Changes: every row re-measured on both hosts, and the attribution arms re-run.
    - Area: `docs/hash-benchmark-log.md`, `docs/optimization-log.md`.
    - Depends on: every stage above that started.
    - Shown by: each Map and hash row at 1.0 or below against Go, or each remaining term of 5% or more of a row's gap naming an open issue. That commit closes this issue.

## Members

- #199 — a generic container heap-copies a `String` at every retain; stage 1. Carried: the allocation repro's readings (1,000 per 1,000 lookups; 3,591 per 1,000 inserts against 1,067), the `ByteArray` sighting (1,006 allocations against 6), and the generic-`var` question (open question 1).
- #200 — `String.equals` through the witness is a byte loop, and `==` compares a string with itself byte by byte; stage 2. Carried: 7 ns, 48 ns and about 52,000 ns.
- #202 — a `String` stored into an `Array with String` takes the out-of-line `__managed_set`; stage 5. Carried: about 10 ns against 0.2 ns.
- #208 — an element read that is only compared retains and releases; stage 6. Carried: 74.1 / 28.2 / 25.5 ns to 69.4 / 25.8 / 22.6.
- #207 — growing a `Map` costs 45 to 90 ns per entry against Go's 29 to 36; stage 8. Carried: the growth-per-entry arms and the 105 / 78 / 38 ns repro.
- #3 — `Map.remove` and `Set.remove` release a slot through two swaps and two `setLength` calls per column; stage 8. The release member arrives through a bootstrap chain, so it waits for no release and does not depend on #106.
- #84 — short-string hashing costs about 1.6 times Go on the `aes` tier, two runtime calls per hash; stage 7.
- #85 — the `software` tier's multiply-high is built from 32-bit pieces on x64 and arm64; stage 4.
- #197 — the software pair mix is emitted twice; stage 4.

## Related, not folded

- #78, #106 — the rulings. Stage 3 reads #106's gate and stage 9 implements it.
- The hashing issue — stages 7 and 8 here wait for its `hash(sink HashSink)` and seed-per-table stages; its structural-equality stage waits for stage 2 here.
- #203 — an allocate-then-free pair evicts and relists a span whenever the class's cached span has one free slot (15 ns per pair; 20 against 35 ns per clone-and-drop). Its mechanism is in the slab allocator, its repro uses no table, and once stage 1 lands a lookup allocates nothing, so no row here can show it.
- #204 — the `aes` tier's round-key table has 8-byte alignment (a lookup at 29 ns or 44 to 53 ns in cache by layout). Its mechanism is the alignment of constant data a vector load reads, which the SHA-256 round constants share on both targets; no row here shows it deterministically. It binds the measurement rule above until it lands.
- #201 — `for (iter, element) in array.withIterator()` allocates once per element (0 to 15% of the insert row, in the benchmark's own loop). Its mechanism is a tuple result boxed on the heap; no element door is involved.
- #86 — `Hasher.create()` heap-allocates when the `Hasher` never leaves its function (hasher-object 1.40 against hasher-fields 1.19 on x64-linux `aes`). The same family as #201: a short-lived aggregate that does not escape reaches the heap.
- #87 — a one-line helper returning an array's `String` element makes a hash loop 2.4 times slower (string-8 13.8 ms inline against 33 ms). It is not localised: the candidates are the inliner and the owned return. It is re-measured after stage 6, which removes one candidate.
- #120 — one inline cost budget. The out-of-line `get`, `findSlot` and hash term is its ground where stage 9 does not remove it.
- #277 — retain and drop decisions spelled in more than one place. Stage 1 routes its sites through `retainCalleeForProtocol` and adds no spelling; #277's three questions are different ones.
- #161 — float and quoted rendering by per-unit `String` concatenation. A text-assembly defect in `stdlib/Builtins.maxon`, with no table, equality or hash on its path.
