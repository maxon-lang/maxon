---
feature: debug-info-spans
status: experimental
keywords: [debug-info, mxdbg, source span, lowering, block split, continuation, sidecar]
category: tooling
---
# Debug-Info Source Spans

## Documentation

`maxon build` writes a `<output>.mxdbg` debug-info sidecar **by default**; `--no-debug-info` opts
out. The sidecar is METADATA ONLY — it never decides which ops are produced, their order or their
operands — so the executable is byte-identical either way. That property is what lets a case be
compiled with debug info on without changing anything the case pins about emitted code, and it is
gated where it can be observed: `tests/debug/byte-identical-debug-info.test.maxon` builds one staged
source path twice, to two outputs, differing only by the flag.

### What a span IS here, and where it stops

A Maxon op's source span is not a field on the op. It is entry `i` of `SourceRangeTable`
(`Compiler/IR/Maxon/SourceRange.maxon`), an op-parallel store of four dense scalar columns —
**a span is keyed by op INDEX**. The table is appended in lockstep with `module.ops` by the single
emit choke point (`FileParseArtifact.emitOp` / `emitTerminator`), and `record` panics when the index
it is handed is not the next unrecorded slot, so an op appended without a span fails loudly on the
FOLLOWING op instead of shifting every later span by one.

⭐ **Spans die at the Maxon→Std boundary — `StdOp` carries no range field.** A line table is a
statement about EMITTED CODE, which is the far side of that boundary, so whatever channel carries a
span across it is a second thing that must stay in step with the ops. These programs are the shapes
that make it hardest.

### Why these tests carry a `<!-- DebugInfo -->` directive

The suite compiles every one of its cases the way it always has, and a debug-info build is not that.
Without a directive the entire debug-info path has no spec coverage on any target while being the
path a user gets by default. `<!-- DebugInfo -->` compiles that case's run binary the way
`maxon build` does, which is the only thing that makes such a case pin anything.

### The invariant these programs pin

At the Std tier a lowering may CUT the block it is working in. `insertRangeChecks` turns a bounds or
narrowing site into a cascade — one `__rc_chk` block per bound, an `__rc_panic`, and an `__rc_ok`
continuation that every op past the site MOVES onto. `inlineManagedPrimitives` carves a fast path
around a runtime call and hands the continuation the call's original result ids as block args. Both
call the one splitter, `splitBlockInPlace`, and a checked divide takes the same shape: a guard block,
a panic arm, and a continuation.

⇒ **An op's position is not stable across those passes, and a span keyed by position is.** These
three programs each drive a split through a different door, so a channel that carries spans past the
Maxon tier by position and does not follow the cut is red here rather than silently misattributing
every line row after the first split.

### A monomorphized generic is a body like any other

A specialization is CLONED. A clone that does not carry the span table forward ships a function with
an empty line table — a debugger could then stop nowhere inside any monomorphized generic in any
Maxon program. The third program holds both split doors at once inside such a body, so it also asks
the invariant above of a specialized body rather than only of a written one.

## Tests

<!-- test: bounds-check-continuation-does-not-desync-spans -->
### An array bounds check compiles with debug info on
`arr.get(5)` past the end lowers to a checked access whose failure edge is its own block and whose
success edge is a continuation every op past the site moves onto. Everything the preceding statements
span was positioned in the block that split abandoned. Verbatim the source of the committed
`array-slots/past-the-end-reports-index-out-of-bounds` case, which passes on every run because the
suite compiles it with debug info off.
<!-- DebugInfo -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.reserve(2)
	try arr.managed.setLength(2) otherwise panic("setLength: capacity just reserved for 2")
	try arr.get(5) otherwise (e) 'handler'
		match e 'check'
			emptySlot then return 42
			indexOutOfBounds then return 99
		end 'check'
	end 'handler'
	return 0
end 'main'
```
```exitcode
99
```

<!-- test: divide-guard-continuation-does-not-desync-spans -->
### A `try (n / d) otherwise` compiles with debug info on
The same cut through a door that has nothing to do with arrays: `/` emits a divide-by-zero guard, so
`try (n / d) otherwise 77` splits the block exactly the way a checked access does. The leading `print`
is load-bearing — it is what makes the abandoned block longer than the continuation that replaces it,
which is the difference between a position that is merely wrong and one that is out of range.
<!-- DebugInfo -->
```maxon
typealias Num = int(0 to 1000)

function pick(x Num) returns Num
	return x
end 'pick'

function main() returns ExitCode
	print("before\n")
	let n = pick(10)
	let d = pick(0)
	let q = try (n / d) otherwise 77
	print("after\n")
	return q as ExitCode
end 'main'
```
```stdout
before
after
```
```exitcode
77
```

<!-- test: monomorphized-generic-continuation-does-not-desync-spans -->
### A block-splitting lowering inside a monomorphized generic compiles with debug info on
`churn` is the body of a `Chain uses T` generic, so what reaches lowering is a CLONE — and it holds
both doors at once: an array `get` past the end and a checked divide by zero, each of which cuts its
block for a continuation. The leading `print` is load-bearing for the same reason it is in the divide
case above. A clone whose spans are absent has an empty line table here; a clone whose spans are
carried forward is subject to the cut rule for the first time.
<!-- DebugInfo -->
```maxon
typealias Num = int(0 to 1000)

type Cell
	export var v as Num

	static function init(v Num) returns Self
		return Self{v: v}
	end 'init'
end 'Cell'

typealias CellArray = Array with Cell

type Chain uses T
	export var seed as T

	export static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	export function churn(d Num) returns Num
		print("churn\n")
		let pad = 1
		var arr = CellArray.create()
		arr.push(Cell.init(pad))
		let missed = try arr.get(7) otherwise Cell.init(3)
		return try (missed.v / d) otherwise 9
	end 'churn'
end 'Chain'

typealias NumChain = Chain with Num

function main() returns ExitCode
	let ch = NumChain.create(1)
	return ch.churn(0)
end 'main'
```
```stdout
churn
```
```exitcode
9
```
