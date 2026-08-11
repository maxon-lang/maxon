---
feature: array-clone-element-size
status: stable
keywords: [array, clone, slice, ranged-typealias, element-size, stride, generic-instantiation, Self]
category: memory
---
# Array.clone / Array.slice preserve a RANGED element type

## Documentation

`Array.clone()` and `Array.slice()` both return `Self`. Resolving that `Self` at a
call site means resolving `Array`'s `Element` binding back to a concrete alias — and
for a RANGED element type that resolution used to throw the range away.

`ResolveStructReturnTypeThroughSelf` matches a struct element by NAME and an enum
element by NAME, but a primitive element fell through to a lookup keyed on its
`MaxonValueKind`. A ranged typealias' value kind is merely `Integer`, so
`int(0 to 16)` (one byte per element) and `int(0 to u64.max)` (eight) were
indistinguishable: the lookup could not find the user's alias, and synthesized
`__Array_i64` instead.

The result was a silent miscompile with a nasty signature. The heap data and the
`__ManagedMemory` header were both correct — element addresses are computed from the
struct's runtime `elementSize` field, so the ADDRESS arithmetic still used the true
1-byte stride. Only the LOAD WIDTH came from the static element type, and that was now
`i64`. So a cloned `Array with int(0 to 16)` holding `[1, 2, 3]` read element 0 as
`0x030201` = 131328: the right address, eight bytes wide, splicing its neighbours in.
Reading the LAST element still returned the right value (the trailing bytes are zero),
which is exactly the kind of half-right behaviour that hides a bug.

A second `Array` instantiation of a DIFFERENT element width has to exist for this to
bite. With only one integer-element array in the program, the kind-keyed lookup landed
on the right alias by luck — which is why the tests below declare two.

This was not theoretical. `maxon-shv2`'s register allocator keeps its parallel-copy
sequencer's pending moves in a `RegNumColumn` (`Array with RegNum`, and `RegNum` is
`int(0 to 16)`), alongside several `int(0 to u64.max)` columns. `sequenceParallelCopy`
opens with `var ps = srcs.clone()`, so every register number it read back out of that
clone was garbage. Comparisons against it silently failed, `rewriteSourcesThroughSwap`
never rewrote anything, and a phi-copy CYCLE broken with `xchg` emitted a stale trailing
`mov` that undid half the swap. A loop that permutes two variables across its back edge
computed the wrong answer.

The fix keys a ranged element by NAME, exactly as struct and enum elements already are.

### ⚠ A STRIDE IS NOT DIRECTLY OBSERVABLE FROM SOURCE, AND AN ASSERTION THAT CANNOT FAIL IS NOT A GATE

Every accessor reads at the same stride the write before it used — the address arithmetic and the
load width both come from the record's own `element_size@24` — so `push` / `get` / `clone` / `slice`
round-trip *whatever* stride the record was stamped with. A test that only pushes values and reads
them back therefore passes identically at 1 byte and at 8, and pins nothing about the width at all.

What DOES discriminate is a second record whose stride was fixed by a **different producer**. A
`b"…"` byte-string literal is exactly that: its record is stamped `element_size = 1` directly by
`LowerMaxonToStd.lowerByteStringLiteral`, a path that never consults the element type. `__managed_append`
compares the two records' `element_size@24` at RUN time and aborts
(`RuntimeAbort.arrayAppendElementSizeMismatch`) when they disagree — so appending a byte-string into
an `Array with Byte` succeeds at a 1-byte stride and aborts at any other.

The front-end guard cannot stand in for it: `ProgramSignatures.arrayAppendArgAdmits`'s
REPRESENTATION half compares `arrayElementSize(receiver)` against `arrayElementSize(argument)` —
still, unchanged, after A4b gave that rule a value-domain half beside it — and for these two the *instance is
the same*, so both sides re-derive the same number through the same function and the check passes
whatever that number is. A check that re-derives both of its sides through one function cannot catch
that function being wrong. Only the run can.

## Tests

<!-- test: clone-preserves-narrow-ranged-element-width -->
### Cloning an Array with a 1-byte ranged element keeps the 1-byte stride
`NarrowCol` is `Array with int(0 to 16)` — one byte per element. `WideCol` exists so a
second, 8-byte integer-element instantiation is in play and the element type cannot be
recovered from the value kind alone. Reading element 0 back out of the clone must give
`1`, not `0x030201`. `slice()` returns `Self` through the same path, so it is pinned
here too.

The final third is the part that can actually FAIL: a `Byte` array is cloned and a `b"CD"`
literal appended into the CLONE, so the clone must have carried the 1-byte stride across.
At an 8-byte stride the two records disagree and the append aborts.
```maxon
typealias Narrow = int(0 to 16)
typealias Wide = int(0 to u64.max)
typealias NarrowCol = Array with Narrow
typealias WideCol = Array with Wide
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var n = NarrowCol.create()
	n.push(1)
	n.push(2)
	n.push(3)

	var w = WideCol.create()
	w.push(9)

	var c = n.clone()
	let c0 = try c.get(0) otherwise 0
	let c1 = try c.get(1) otherwise 0
	let c2 = try c.get(2) otherwise 0

	var s = try n.slice(1, endIndex: 3) otherwise panic("slice 1..3 is in bounds")
	let s0 = try s.get(0) otherwise 0
	let s1 = try s.get(1) otherwise 0

	var b = Bytes.create()
	b.push(65)
	var bc = b.clone()
	bc.append(b"CD")
	let b0 = try bc.get(0) otherwise 0
	let b1 = try bc.get(1) otherwise 0
	let b2 = try bc.get(2) otherwise 0

	let ok = c0 == 1 and c1 == 2 and c2 == 3 and s0 == 2 and s1 == 3
	let strideOk = bc.count() == 3 and b0 == 65 and b1 == 67 and b2 == 68 and b.count() == 1
	return 0 if ok and strideOk else 1
end 'main'
```
```exitcode
0
```

<!-- test: clone-preserves-every-ranged-element-width -->
### Every narrow width clones correctly, and the clone is an independent buffer
Three element widths at once — 1, 4 and 8 bytes. Each clone must read back its own
elements at its own stride. Writing to a clone must then copy-on-write at that same
stride, leaving the original untouched: a wrong element size here would either corrupt
the neighbouring element or write into the parent's buffer.
```maxon
typealias Narrow = int(0 to 16)
typealias Mid = int(0 to 100000)
typealias Wide = int(0 to u64.max)
typealias NarrowCol = Array with Narrow
typealias MidCol = Array with Mid
typealias WideCol = Array with Wide

function main() returns ExitCode
	var n = NarrowCol.create()
	n.push(1)
	n.push(2)
	var m = MidCol.create()
	m.push(3)
	m.push(4)
	var w = WideCol.create()
	w.push(5)
	w.push(6)

	var nc = n.clone()
	var mc = m.clone()
	var wc = w.clone()

	let n0 = try nc.get(0) otherwise 0
	let m0 = try mc.get(0) otherwise 0
	let w0 = try wc.get(0) otherwise 0

	// Mutating a clone must not disturb the original (COW at the element's own stride).
	try nc.set(0, value: 15) otherwise panic("index 0 is in bounds")
	let nOrig = try n.get(0) otherwise 0
	let nClone = try nc.get(0) otherwise 0
	let nNeighbour = try nc.get(1) otherwise 0

	let ok = n0 == 1 and m0 == 3 and w0 == 5 and nOrig == 1 and nClone == 15 and nNeighbour == 2
	return 0 if ok else 1
end 'main'
```
```exitcode
0
```
