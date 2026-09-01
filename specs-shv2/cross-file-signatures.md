---
feature: cross-file-signatures
status: stable
keywords: [cross-file, signatures, types, bool, int, short-circuit, undefined-function]
category: diagnostics
---

# Cross-File Callee Signatures

## Documentation

A call's result type is decided by the **parser**, and it must be exact. Two things depend on it
that no later pass can retrofit:

- `and` / `or` over `bool` operands is **short-circuit control flow**, not an operator — it lowers
  to blocks and a merge phi. Whether the right operand is evaluated *at all* follows from the left
  operand's type.
- `not` picks a **different opcode** per operand type: a bit-flip on a `bool`, a 64-bit complement
  on an `int`.

So the parser reads every function declared **anywhere in the program**, from tokens, before any
file is parsed. A callee's return type is therefore exact whether it is declared above the call,
below it, or in another file. Declaration order — and file boundaries — do not decide what a
program means.

### What this replaces, and why it was a wrong answer

A callee the parser could not see used to be typed `unresolved`, which agrees with **everything**.
That is the right move for a type you genuinely cannot know, and a catastrophic one for a type you
simply did not look for:

```text
// --- file: a.maxon
export function isReady() returns bool
	return true
end 'isReady'

// --- file: b.maxon
function main() returns ExitCode
	let x = isReady()
	return x + 41       // compiled, and returned 42 — the bool's 1 payload, added
end 'main'
```

The same program in **one** file was already rejected. The bug was not the deferral; it was that
nothing had looked.

Its twin is worse, because deferral does not merely fail to reject — it **mints a false tag**. A
word operator whose operands *agree only because one of them deferred* takes the bool reading, so
`flag and crossFileInt()` produced a merge phi tagged `bool` that carried the integer `7`, and
`if m` then branched on `7`.

### A callee no file declares

`unresolved` is still reachable, for exactly one thing: a call to a function that does not exist.
That program is rejected — `E3004: call to undefined function` — so the deferral has nothing left
to lie to.

## Tests

<!-- test: cross-file-bool-plus-int -->
A `bool` returned from another file cannot be added to an `int`. This is the headline case: it
compiled, and returned 42.
```maxon
// --- file: a.maxon

export function isReady() returns bool
	return true
end 'isReady'

// --- file: b.maxon
function main() returns ExitCode
	let x = isReady()
	return x + 41
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:11: Cannot operate on bool and int
```

<!-- test: cross-file-word-operator-mixed-operands -->
The false-tag twin. `flag and crossFileInt()` used to mint a merge phi tagged `bool` carrying the
int `7`; `if m` branched on `7` and the program returned 1.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export function seven() returns Integer
	return 7
end 'seven'

// --- file: b.maxon
function main() returns ExitCode
	let flag = true
	let m = flag and seven()
	if m 'branch'
		return 1
	end 'branch'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:15: operator 'and' requires both operands to be the same type (both bool or both int)
```

<!-- test: cross-file-comparison-mixed-operands -->
A comparison is class-strict across files too: the only thing there is to compare is the bool's 0/1
payload, which is not what the source wrote.
```maxon
// --- file: a.maxon
export function isReady() returns bool
	return true
end 'isReady'

// --- file: b.maxon
function main() returns ExitCode
	if isReady() < 4 'compare'
		return 1
	end 'compare'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:15: type mismatch: 'cannot compare bool with int'
```

<!-- test: cross-file-int-call-still-compiles -->
⚠ THE OVER-REJECTION GUARD. Refusing an operand whose type the parser could not pin would reject
this — a correct program — and over-rejection is the worse failure. The fix was to LOOK, not to
refuse.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export function crossFileInt() returns Integer
	return 41
end 'crossFileInt'

// --- file: b.maxon
function main() returns ExitCode
	return crossFileInt() + 1
end 'main'
```
```exitcode
42
```

<!-- test: cross-file-bool-short-circuits -->
⚠ THE SECOND OVER-REJECTION GUARD, and the one that rules out the tempting one-liner. Making the
word operator DEFER on an unpinned operand would type `ready() and enabled()` as unknown, and `not`
refuses an unknown operand — so this correct program would stop compiling. It compiles, and the
`and` genuinely short-circuits: `enabled()` divides by zero and is never reached, because `ready()`
is false. A clean exit IS the proof.

⚠ The zero divisor comes from an OPAQUE call rather than from `var zero = 0`. A folded zero is a
compile-time E3103 (A1), which would refuse the program outright and take the short-circuit — the
actual subject — with it. The `otherwise panic` is what keeps "never reached" a checked claim: if
the `and` ever stopped short-circuiting, this would name itself instead of trapping.
```maxon
// --- file: a.maxon

export function ready() returns bool
	return false
end 'ready'

function opaqueZero() returns Integer
	return 0
end 'opaqueZero'

export function enabled() returns bool
	let zero = opaqueZero()
	return (try (1 / zero) otherwise panic("`and` did not short-circuit: enabled() was evaluated")) == 0
end 'enabled'

typealias Integer = int(i64.min to i64.max)
// --- file: b.maxon
function main() returns ExitCode
	if not (ready() and enabled()) 'skipped'
		return 42
	end 'skipped'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: cross-file-declaration-order -->
A callee declared in a file that sorts AFTER the caller is typed exactly the same. Order does not
decide meaning — here the `and` short-circuits over two cross-file bools regardless.
```maxon
// --- file: a-caller.maxon
function main() returns ExitCode
	if alwaysTrue() and alwaysTrue() 'both'
		return 42
	end 'both'
	return 1
end 'main'

// --- file: z-callee.maxon
export function alwaysTrue() returns bool
	return true
end 'alwaysTrue'
```
```exitcode
42
```

<!-- test: undefined-function-rejected -->
The one thing `unresolved` still means: a callee no file declares. The program is rejected, so the
deferral can never reach codegen.
```maxon
function main() returns ExitCode
	return bogus() + 1
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:9: call to undefined function 'bogus'
```

<!-- test: cross-file-generic-alias-is-a-swept-value-type -->
⭐ **A GENERIC-INSTANCE TYPEALIAS IS A SWEPT VALUE TYPE, NOT ONLY A CALL BASE (A3e).** `parseTypeReference`'s
generic-alias arm carried a comment claiming the declaration sweep never reached it and that such an alias
is "used only as a call BASE … never as a swept value type". Both halves were false: the sweep reads
declared TYPES through that same routine, and this program spells `IntArray` at a struct FIELD, a METHOD
return and a FREE-FUNCTION return — three positions the sweep records and the whole-program index stores.
This half DECLARES the alias file first and the twin below declares it last — and since A3m the declared
order is the compiled order, so the two halves cover the two orders between them instead of both getting
whichever one the host's directory walk happened to serve.
```maxon
// --- file: alias.maxon
public typealias Int = int(i64.min to i64.max)
export typealias IntArray = Array with Int

// --- file: main.maxon
type Holder
	export var nums as IntArray

	export static function create() returns Holder
		var a = IntArray.create()
		a.push(12)
		return Self{nums: a}
	end 'create'

	export function more() returns IntArray
		var a = IntArray.create()
		a.push(14)
		return a
	end 'more'
end 'Holder'

function make() returns IntArray
	var a = IntArray.create()
	a.push(16)
	return a
end 'make'

function main() returns ExitCode
	let h = Holder.create()
	let x = try h.nums.get(0) otherwise return 1
	let y = try h.more().get(0) otherwise return 1
	let z = try make().get(0) otherwise return 1
	return (x + y + z) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: cross-file-generic-alias-is-a-swept-value-type-either-order -->
⭐ **THE SAME PROGRAM WITH THE TWO FILES DECLARED THE OTHER WAY ROUND, so the alias file is compiled LAST.**
Whether the arm fires during the sweep is decided by the order the files are registered in — which the
CALLER states and the loader never sorts (A3m; `StdlibLoader`'s header for the no-sort ruling). The arm is now gated on `ProgramSignatures.allFilesFolded`, so it fires in neither order and the
sweep records `named("IntArray")` for both — repaired identically at every read door. The alias file
declares nothing but aliases, so the two cases' emitted IR is the SAME text: a golden that drifts apart is
the order dependence coming back.
```maxon
// --- file: main.maxon
type Holder
	export var nums as IntArray

	export static function create() returns Holder
		var a = IntArray.create()
		a.push(12)
		return Self{nums: a}
	end 'create'

	export function more() returns IntArray
		var a = IntArray.create()
		a.push(14)
		return a
	end 'more'
end 'Holder'

function make() returns IntArray
	var a = IntArray.create()
	a.push(16)
	return a
end 'make'

function main() returns ExitCode
	let h = Holder.create()
	let x = try h.nums.get(0) otherwise return 1
	let y = try h.more().get(0) otherwise return 1
	let z = try make().get(0) otherwise return 1
	return (x + y + z) as ExitCode
end 'main'

// --- file: alias.maxon
export typealias Int = int(i64.min to i64.max)
export typealias IntArray = Array with Int
```
```exitcode
42
```

<!-- test: cross-file-function-alias-is-a-swept-value-type -->
⭐ **THE FUNCTION-ALIAS ARM IS THE SAME SHAPE (A3e).** `functionTypeAliases` is folded per FILE, so a
function alias declared in a sibling file walked earlier is registered while a later file is still being
swept — the arm's own comment claimed the registry "returns `undeclared`" throughout the sweep, which is
true only within one file. This half declares the alias file FIRST and its twin below declares it
last; since A3m that is what the compiler compiles, so the pair covers both orders (see
`cross-file-generic-alias-is-a-swept-value-type-either-order` above).
```maxon
// --- file: alias.maxon
export typealias Int = int(i64.min to i64.max)
export typealias UnaryOp = function(Int) returns Int

// --- file: main.maxon
function twice(n Int) returns Int
	return n * 2
end 'twice'

type Holder
	export var op as UnaryOp

	export static function create() returns Holder
		return Self{op: twice}
	end 'create'
end 'Holder'

function pick() returns UnaryOp
	return twice
end 'pick'

function main() returns ExitCode
	let h = Holder.create()
	let f = pick()
	return (h.op(7) + f(14)) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: cross-file-function-alias-is-a-swept-value-type-either-order -->
⭐ **THE SAME PROGRAM WITH THE TWO FILES DECLARED THE OTHER WAY ROUND.** Same gate, same reason, and the same golden-drift
tripwire: the alias file declares nothing but aliases, so this case's IR must read identically to its twin.
```maxon
// --- file: main.maxon
function twice(n Int) returns Int
	return n * 2
end 'twice'

type Holder
	export var op as UnaryOp

	export static function create() returns Holder
		return Self{op: twice}
	end 'create'
end 'Holder'

function pick() returns UnaryOp
	return twice
end 'pick'

function main() returns ExitCode
	let h = Holder.create()
	let f = pick()
	return (h.op(7) + f(14)) as ExitCode
end 'main'

// --- file: alias.maxon
export typealias Int = int(i64.min to i64.max)
export typealias UnaryOp = function(Int) returns Int
```
```exitcode
42
```

<!-- test: cross-file-generic-instance-through-a-function-alias -->
⭐⭐ **THE SWEPT FUNCTION ALIAS AGAIN, BUT OVER A GENERIC INSTANCE — AND HERE THE ORDER DECIDED THE ANSWER
RATHER THAN THE SPELLING.** The pair above pins a function alias whose types are RANGED, which round-trip
through the stored `(tag, name)` pair because they have a name. A `genericInstance` does not: its identity
is a `GenericInstanceId`, the name slot took the empty string, and the rebuild substituted `UnnamedTypeId`
— **0**, the id of whichever instantiation the sweep interned first. `thief.maxon` is walked first here
purely so that `Array with Small` takes id 0, which moves `Array with Field` to 1 and refuses this legal
program `E3005 … expected 'fn(int) returns struct', got 'fn(int) returns struct'`.

⚠ **THE FILE SPLIT IS THE REPORTED SHAPE, NOT AN EMBELLISHMENT.** This is `maxon-dev-mcp/mcp` reduced: the
generic alias lives in one file (`Schema.maxon`'s `SchemaFieldArray`), the function alias and its call site
in another (`Server.maxon`'s `SchemaFieldsBuilder`), and a dozen files that mention neither decide the
verdict by deciding who interns first. Renaming `Schema.maxon` so it sorted ahead of them made nine E3005s
disappear, which is what a bug reported as *"identity depends on file order"* looks like from outside.
```maxon
// --- file: thief.maxon
typealias Small = int(0 to 10)
typealias SmallArray = Array with Small

export function smallCount() returns Small
	var s = SmallArray.create()
	s.push(3)
	return s.count()
end 'smallCount'

// --- file: types.maxon
typealias Integer = int(i64.min to i64.max)

export type Field
	export var v as Integer

	export static function create(v Integer) returns Field
		return Self{v: v}
	end 'create'
end 'Field'

export typealias FieldArray = Array with Field

// --- file: srv.maxon
typealias Count = int(i64.min to i64.max)
typealias FieldsBuilder = function(Count) returns FieldArray

function buildFields(n Count) returns FieldArray
	var out = FieldArray.create()
	out.push(Field.create(n))
	return out
end 'buildFields'

function apply(f FieldsBuilder) returns Count
	let produced = f(7)
	let first = try produced.get(0) otherwise panic("apply: empty array")
	return first.v
end 'apply'

export function runBuilder() returns Count
	return apply(buildFields)
end 'runBuilder'

// --- file: main.maxon
function main() returns ExitCode
	return (runBuilder() + smallCount()) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: cross-file-generic-instance-through-a-function-alias-either-order -->
⭐ **THE SAME FOUR FILES WITH THE THIEF MOVED AFTER THE DECLARATION IT WAS STEALING FROM**, so
`Array with Field` interns first and holds id 0. **MEASURED: this half PASSES on the broken compiler and
its twin above does not** — same program, same files, different declared order, opposite verdicts. Since
A3m the compiler compiles the order the case declares, so the pair is a two-order test rather than one
program written twice, and neither half is decoration: the failing half proves the defect and this one
proves the fix did not simply refuse everything.
```maxon
// --- file: types.maxon
typealias Integer = int(i64.min to i64.max)

export type Field
	export var v as Integer

	export static function create(v Integer) returns Field
		return Self{v: v}
	end 'create'
end 'Field'

export typealias FieldArray = Array with Field

// --- file: srv.maxon
typealias Count = int(i64.min to i64.max)
typealias FieldsBuilder = function(Count) returns FieldArray

function buildFields(n Count) returns FieldArray
	var out = FieldArray.create()
	out.push(Field.create(n))
	return out
end 'buildFields'

function apply(f FieldsBuilder) returns Count
	let produced = f(7)
	let first = try produced.get(0) otherwise panic("apply: empty array")
	return first.v
end 'apply'

export function runBuilder() returns Count
	return apply(buildFields)
end 'runBuilder'

// --- file: thief.maxon
typealias Small = int(0 to 10)
typealias SmallArray = Array with Small

export function smallCount() returns Small
	var s = SmallArray.create()
	s.push(3)
	return s.count()
end 'smallCount'

// --- file: main.maxon
function main() returns ExitCode
	return (runBuilder() + smallCount()) as ExitCode
end 'main'
```
```exitcode
8
```
