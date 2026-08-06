---
feature: debug-info-spans
status: stable
keywords: debug-info, mxdbg, source span, lowering, sidecar, DebugSpanFlow, block
category: tooling
---
# Debug-Info Source Spans

## Documentation

`maxon build` writes a `<output>.mxdbg` debug-info sidecar **by default** (`--no-debug-info`
opts out). Producing it switches on a side channel that runs through every lowering pass:
each pass records, per source op, where that op's lowered output begins, and then stamps the
whole range with the op's `(line, col)`. The result is the sidecar's line table.

The side channel is METADATA ONLY. It never decides which ops are produced, their order, or
their operands, so a build with debug info emits a byte-identical executable to one without
(docs/DEBUGGER_DESIGN.md). That is why the sidecar can exist at all, and why a `<!-- DebugInfo -->`
test's committed fragment golden is still minted from a compile with the flag OFF: the golden
pins emitted code, and the flag is not allowed to reach it.

### Why these tests carry a `<!-- DebugInfo -->` directive

`Compiler.DebugInfo` is `[ThreadStatic]`, and until this spec existed the only writers were
`maxon build` and the MCP's debug build. The spec runner compiles on its own worker threads,
so every one of its ~3200 compiles read the CLR default `false` — **the entire debug-info
lowering path had no coverage on any target, ever**, while being the path a user gets by
default. A `<!-- DebugInfo -->` directive compiles that test's run binary the way `maxon build`
does, which is the only thing that makes such a test pin anything.

A test carrying the directive is excluded from batched compilation: a batch is one compile
shared by many tests, so a batched directive would reach no compile at all.

### The invariant these tests pin

A mark is an INDEX INTO ONE DESTINATION BLOCK. `MaxonToStandardConversion` replaces its
current destination block mid-lowering — bounds checks, divide-by-zero guards and `try` error
edges all create an error block and a fresh merge block, handed back through a `ref`
parameter — so marks recorded against a long block that lowering has since abandoned must not
be replayed against the short merge block that replaced it. When they were, the stamping walked
off the end of the merge block and the whole compile died with `E9001 ... Index was out of
range`, on programs the suite compiles successfully every single run with the flag off.

### A monomorphized generic is a body like any other

A specialization is CLONED, and a span is keyed by op REFERENCE — so a clone that does not carry
the table forward ships a function with an empty line table. That was the state until this spec's
third test: every monomorphized generic in every Maxon program had no line rows at all, so a
debugger could not stop anywhere inside one. Carrying them forward also means the mark/reset rule
above now applies to specialized bodies for the FIRST time: before, a clone recorded no marks, so
its block-splitting lowerings could not desync because there was nothing to desync.

## Tests

<!-- test: bounds-check-merge-block-does-not-desync-marks -->
### An array bounds check compiles with debug info on
`arr.get(5)` past the end lowers to a bounds check whose failure edge is a fresh error block
and whose success edge is a fresh merge block. Everything the preceding statements marked was
measured against the block that check abandoned. Verbatim the source of the committed
`array-slots/past-the-end-reports-index-out-of-bounds` fragment — which passes on every run
because the suite compiled it with debug info off, and which `maxon build` could not compile
at all.
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

<!-- test: divide-guard-merge-block-does-not-desync-marks -->
### A `try (n / d) otherwise` compiles with debug info on
The same defect through a door that has nothing to do with arrays: `/` emits a
divide-by-zero guard, so `try (n / d) otherwise 77` splits the block exactly the way a bounds
check does. The leading `print` is load-bearing — it is what makes the abandoned block longer
than the merge block that replaces it, which is the difference between a mark index that is
merely wrong and one that is out of range.
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

<!-- test: monomorphized-generic-merge-block-does-not-desync-marks -->
### A block-splitting lowering inside a monomorphized generic compiles with debug info on
`churn` is the body of a `Chain uses T` generic, so what reaches lowering is a CLONE with brand-new
op objects — and it holds both doors at once: an array `get` past the end and a checked divide by
zero, each of which abandons its block for a fresh merge block. The leading `print` is load-bearing
for the same reason it is in the divide case above: it makes the abandoned block longer than the
merge block that replaces it, which is the difference between a mark index that is merely wrong and
one that is out of range. Nothing could reach this shape before the clone carried spans.
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
