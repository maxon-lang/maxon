---
feature: constant-string-slice
status: experimental
keywords: [string, slice, literal, rdata, constant, fold, immortal, allocation]
category: optimization
---

# A slice of a constant string is a constant

## Documentation

A String LITERAL is a wholly immortal `.rdata` record: `LowerMaxonToStd.lowerStringLiteral` emits one
`StdOp.rdataAddr` naming a record `StringRuntime.registerStringLiteralRecord` built into the image, and
`GlobalDataTable.registerRdataConstant` dedupes it by its payload bytes — so every use of `"Ada"` in a
whole program shares ONE record that is never allocated and never freed.

**A SLICE of such a literal was not one.** `s.slice(...)` reaches `stdlib/String.maxon`'s `sliceBytes`,
which calls `managed.slice` — `__managed_slice`, whose `ManagedMemoryRuntime.emitRecordView` mints a
HEAP record per evaluation whose `buffer@0` points into the literal's immortal blob, with
`capacity@16 = ViewBufferCapacity` and `parent@32 = NoOwedAllocation`. Copy-free and count-free, but an
allocation all the same, and one paid on every trip of a loop.

When the compiler can see BOTH ends of that — the receiver is a value it minted from a string literal,
and the bounds are compile-time known byte positions — it now emits the sliced bytes as a string
literal of their own instead of the call. The result is an ordinary `.rdata` record, indistinguishable
from one a source `"…"` produced, and it costs **no allocation at all**.

### What folds, and what deliberately does not

The fold fires only when every one of these holds, and declines to the ordinary runtime call otherwise:

- **The receiver's bytes are known** — its value was defined by a string-literal op in this same
  function. A parameter, a field, a value arriving through a merge, or a slice of a slice is not,
  because a String's immortality is a RUNTIME property of the record it happens to hold
  (`string-type-2.md:a-literal-reaching-a-merge-through-a-parameter` pins exactly why that choice
  cannot be made statically). Only a value the compiler MINTED as a literal is known here — the same
  criterion `LiteralArgPromotion.isImmortal` already applies for the same reason.
- **Both bounds are compile-time known byte positions** — `startIndex()` and `endIndex()` of a
  known-bytes receiver, and, for the `length:` overload, an integer constant.
- **The range is valid** — `0 <= start <= end <= byteLength`. An out-of-range constant range is left to
  the runtime call so its existing panic still fires with the trace `string-index.md` pins.

⚠ **The `length:` overload folds only for a SINGLE-BYTE-GRAPHEME literal**, where a grapheme count and
a byte count are the same number. For a literal carrying multi-byte clusters the conversion is UAX#29
grapheme segmentation, which is the emitted runtime's job and not the compiler's; those slices keep
the runtime call. `slice-by-length-counts-clusters` in `string-index.md` is that shape and stays exact.

⚠ **A byte-string (`b"…"`) or constant-array literal is OUT OF SCOPE, and not by omission.** Its blob is
immortal but its RECORD is an ordinary `__mm_alloc` box carrying `RdataBufferCapacity`, precisely so
`__managed_cow_detach` can republish it and `__managed_decref` can free it. Folding an Array slice into
an immortal record would take both of those away from a value whose whole representation assumes them.

### Why a folded slice is safe to share, and where that is load-bearing

The fold emits **the same op a source literal emits**, so it inherits the entire existing regime for
immortal records rather than needing one of its own: `LiteralArgPromotion` copies before a mutating
parameter, a `var` String is promoted at its declaration, and `__str_retain` / `__str_clone` /
`__str_decref` / `__managed_decref` all branch on `emitRecordIsImmortal`. Nothing here teaches a new
provenance value, which is what `BufferOwnership`'s header requires of anything that would.

That matters most where it is least obvious: `registerRdataConstant` dedupes by BYTES, so folding
`"hello world".slice(…, length: 5)` yields the *identical record* as an unrelated `"hello"` written
elsewhere in the same program. A write through one must never be visible through the other —
`a-folded-slice-does-not-corrupt-a-deduped-literal` below is what says so, and on `wasm32-wasi` it is
the case that matters most: linear memory has no read-only segment, so a write that FAULTS on x64
would there silently succeed and corrupt every other use of those bytes.

### How these cases measure it

The counters are `builtins-mm-counters.md`'s, read as the SUM of the tracked and raw layers — the
number `PhaseProbe` reads, and the one that means the same thing under both compilers. Each case
brackets ONLY the slice, because `print` and interpolation allocate on their own account.

## Tests

<!-- test: full-slice-of-a-literal-allocates-nothing -->
Slicing a literal end to end costs nothing: the bytes are already in the image, and the record naming
them is too. Before this fold the two `StringIndex` boxes and the view record made this a non-zero
delta.
```maxon
function main() returns ExitCode
	let s = "hello world"
	let before = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	let sub = s.slice(s.startIndex(), endIndex: s.endIndex())
	let after = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	print("{after - before} {sub}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 hello world
```

<!-- test: length-slice-of-a-literal-allocates-nothing -->
The shape that makes the fold worth having — a PROPER substring, whose bytes are a window the compiler
can cut for itself. `"hello world"` is single-byte-grapheme, so `length: 5` is five bytes.
```maxon
function main() returns ExitCode
	let s = "hello world"
	let before = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	let sub = s.slice(s.startIndex(), length: 5)
	let after = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	print("{after - before} {sub}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 hello
```

<!-- test: empty-slice-of-a-literal-allocates-nothing -->
An empty window is a constant like any other. It is worth its own case because it is the one range
whose start and end coincide, and because the runtime path it replaces builds a length-0 view that
holds its parent alive.
```maxon
function main() returns ExitCode
	let s = "hello"
	let before = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	let sub = s.slice(s.startIndex(), length: 0)
	let after = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	print("{after - before} [{sub}] {sub.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 [] 0
```

<!-- test: a-folded-slice-in-a-loop-allocates-nothing -->
⭐ **THE MOTIVATION, and the shape no single-evaluation case can show.** The cost the fold removes is
paid PER EVALUATION, so a loop is where it was actually being spent: 500 trips used to mean 500 view
records plus their index boxes. A fold that fired once and then fell back would pass the three cases
above and fail this one.
```maxon
function main() returns ExitCode
	let s = "hello world"
	let before = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	var total = 0
	var trips = 0
	while trips < 500 'loop'
		let sub = s.slice(s.startIndex(), length: 5)
		total = total + sub.byteLength()
		trips = trips + 1
	end 'loop'
	let after = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	print("{after - before} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 2500
```

<!-- test: a-folded-slice-is-still-copy-on-write -->
⛔ **THE FAULT THIS FOLD COULD HAVE INTRODUCED.** The folded slice is an immortal `.rdata` record, and
a write published back into one is a store into a read-only page: `0xC0000005` on x64 and arm64, and —
worse — a SUCCEEDING store on `wasm32-wasi`, whose linear memory has no read-only segment. The write
must copy first, exactly as it does for a value written as a literal in source. The receiver must be
left untouched.
```maxon
function main() returns ExitCode
	let s = "hello world"
	var sub = s.slice(s.startIndex(), length: 5)
	sub.append("!")
	print("{sub} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello! hello world
```

<!-- test: a-folded-slice-does-not-corrupt-a-deduped-literal -->
⛔ **THE HAZARD THE FOLD CREATES THAT DID NOT EXIST BEFORE.** `registerRdataConstant` dedupes by
payload bytes, so the folded slice of `"hello world"` and the independently written literal `"hello"`
are the SAME record. Writing through one must not be visible through the other. Before the fold these
two values had nothing to do with each other, so no existing case can catch this.
```maxon
function main() returns ExitCode
	let s = "hello world"
	let other = "hello"
	var sub = s.slice(s.startIndex(), length: 5)
	sub.append("!")
	print("{sub} {other}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello! hello
```

<!-- test: a-runtime-bound-still-takes-the-runtime-path -->
**THE BOUNDARY, held from the other side.** A literal receiver whose bound is NOT compile-time known
keeps the view record it always had — the delta is positive — and still answers the right bytes. Without
this, a fold that quietly guessed at a `findFirst` result would look exactly like a working one.
```maxon
function main() returns ExitCode
	let s = "hello world"
	let before = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	let idx = try s.findFirst(" ") otherwise s.endIndex()
	let sub = s.slice(s.startIndex(), endIndex: idx)
	let after = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	print("{after > before} {sub}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true hello
```
