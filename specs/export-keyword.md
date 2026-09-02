---
feature: export-keyword
status: stable
keywords: [export, visibility, module, function, type]
category: infrastructure
---

# Export Keyword

## Documentation

### Export Keyword

All declarations — functions, types, enums, typealiases, and top-level variables — are file-scoped by default. The `export` keyword makes them visible to other modules. Without `export`, a declaration can only be used within the file where it is defined.

```text
export function publicApi() returns Integer
  return privateHelper()
end 'publicApi'

function privateHelper() returns Integer
  return 42
end 'privateHelper'
```

When modules are compiled together, only exported symbols from earlier modules can be called by later modules. Non-exported symbols from other files are invisible — attempting to use them produces a compile error.

### Exporting Types

Types can be exported to make them available to other modules. Without `export`, a type is only usable within its file:

```text
export type Point
  export var x as Integer
  export var y as Integer
end 'Point'
```

### Exporting Enums

Enums follow the same visibility rules as types:

```text
export enum Color
  red
  green
  blue
end 'Color'
```

Without `export`, a enum is only visible within its declaring file.

### Exporting Type Aliases

Typealiases are also file-scoped by default. Use `export` for cross-file visibility:

```text
export typealias Score = int(0 to 100)
```

The standard library exports commonly-used aliases like `Integer`, `Float`, `Byte`, `Count`, `Index`, and `ExitCode`.

### Exporting Methods

Methods within types can be individually exported:

```text
export type Calculator
  var result as Integer

  export function add(n Integer)
    result = result + n
  end 'add'

  function internalReset()
    result = 0
  end 'internalReset'
end 'Calculator'
```

### Namespace Disambiguation

A file's namespace is the directory it lives in (see `specs/namespaces.md`). When two files in different directories both export a function with the same bare name, an unqualified call from a third file is ambiguous and must be rewritten with the directory-qualified form:

```text
// math/ops.maxon and text/ops.maxon both export 'add'.
// In app/main.maxon:
var result1 = math.add(1, 2)         // calls math/ops.maxon's add
var result2 = text.add("hi", "lo")   // calls text/ops.maxon's add
```

A bare `add(...)` from `app/main.maxon` is rejected by the self-hosted compiler with E3095:

```text
error E3095: Ambiguous bare-name call to 'add': multiple visible definitions found.
  Qualify with a directory name. Candidates: math.add, text.add
```

When there is no collision, unqualified cross-file calls continue to work via the cross-file fallback. See `specs/namespaces.md` for the canonical resolution rules and the `error.cross-file-bare-name-ambiguous` test that pins this diagnostic.

The same model applies to **typealiases**: two exported typealiases with the same bare name in different directories are accepted at decl time, and a bare reference from a third file is rejected with **E3063** (`Ambiguous typealias 'Score': multiple visible definitions found. Qualify with a directory name. Candidates: api.Score, legacy.Score`). The user writes `api.Score` or `legacy.Score` to disambiguate. Same-file duplicate typealiases remain E3061 — qualification cannot resolve two declarations in the same file. See `specs/typealias-collision.md` for the canonical tests.

## Tests

<!-- test: export-function-basic -->
```maxon
// --- file: api/lib.maxon
typealias Integer = int(i64.min to i64.max)

export function helper() returns Integer
	return 21
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	return helper() + helper()
end 'main'
```
```exitcode
42
```

<!-- test: export-type-basic -->
```maxon
// --- file: api/shapes.maxon
typealias Integer = int(i64.min to i64.max)

export type Point
	var x as Integer
	var y as Integer

	export function sum() returns Integer
		return x + y
	end 'sum'

	export static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = Point.create(20, y: 22)
	return p.sum()
end 'main'
```
```exitcode
42
```

<!-- test: non-export-function-works -->
```maxon

typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: mixed-export-and-non-export -->
```maxon
// --- file: api/lib.maxon
typealias Integer = int(i64.min to i64.max)

export function publicFunc() returns Integer
	return privateFunc() + 20
end 'publicFunc'

function privateFunc() returns Integer
	return 22
end 'privateFunc'

// --- file: app/main.maxon
function main() returns ExitCode
	return publicFunc()
end 'main'
```
```exitcode
42
```

<!-- test: export-typealias-basic -->
```maxon
// --- file: api/types.maxon
typealias Integer = int(i64.min to i64.max)

export typealias IntArray = Array with Integer

// --- file: app/main.maxon
function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(42)
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: export-typealias-in-type-field -->
```maxon
// --- file: api/types.maxon
typealias Integer = int(i64.min to i64.max)

export typealias IntArray = Array with Integer

export type Container
	export var items as IntArray

	export static function create() returns Self
		return Container{items: IntArray.create()}
	end 'create'

	export function add(n Integer)
		items.push(n)
	end 'add'

	export function sum() returns Integer
		var total = 0
		for item in items 'loop'
			total = total + item
		end 'loop'
		return total
	end 'sum'
end 'Container'

// --- file: app/main.maxon
function main() returns ExitCode
	var c = Container.create()
	c.add(20)
	c.add(22)
	return c.sum()
end 'main'
```
```exitcode
42
```

<!-- test: export-typealias-as-return-type -->
```maxon
// --- file: api/types.maxon
typealias Integer = int(i64.min to i64.max)

export typealias IntArray = Array with Integer

export function makeArray() returns IntArray
	var arr = IntArray.create()
	arr.push(42)
	return arr
end 'makeArray'

// --- file: app/main.maxon
function main() returns ExitCode
	let arr = makeArray()
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: non-export-typealias-in-same-file -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(42)
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: exported-function-cross-file -->
```maxon
// --- file: api/helper.maxon
typealias Integer = int(i64.min to i64.max)

export function helper() returns Integer
	return 42
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: non-exported-function-same-file -->
```maxon

typealias Integer = int(i64.min to i64.max)

function privateHelper() returns Integer
	return 99
end 'privateHelper'

function main() returns ExitCode
	return privateHelper()
end 'main'
```
```exitcode
99
```

<!-- test: error.non-exported-function-cross-file -->
```maxon
// --- file: helper.maxon
typealias Integer = int(i64.min to i64.max)

function privateHelper() returns Integer
	return 99
end 'privateHelper'

// --- file: main.maxon
function main() returns ExitCode
	return privateHelper()
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/export-keyword/error.non-exported-function-cross-file.test:11:9: function 'privateHelper' is not exported
```

### A STATIC FIELD obeys the export rule its type does

`export` on the type publishes the type, not every slot inside it. A `static var` without its own
`export` is file-private exactly as a top-level function is, and reading it from another file is the
same refusal — `function 'x' is not exported` and `static 'T.x' is not exported` are one rule with two
nouns. Statics were the hole in that rule: a cross-file read of a private one compiled and ran.

<!-- test: error.non-exported-static-read-cross-file -->
```maxon
// --- file: holder.maxon
typealias Count = int(0 to u64.max)

export type Holder
	static var cached = Holder.build()
	export var value as Count

	static function build() returns Holder
		return Holder{value: 7}
	end 'build'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let a = Holder.cached
	return a.value
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/export-keyword/error.non-exported-static-read-cross-file.test:16:17: static 'Holder.cached' is not exported
```

<!-- test: error.non-exported-static-assign-cross-file -->
```maxon
// --- file: counter.maxon
typealias Tally = int(0 to 100)

function seed() returns Tally
	return 7
end 'seed'

export type Counter
	static var hits = seed()
end 'Counter'

// --- file: main.maxon
function main() returns ExitCode
	Counter.hits = 9
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/export-keyword/error.non-exported-static-assign-cross-file.test:15:10: static 'Counter.hits' is not exported
```

### An EXPORTED static is readable across files

The refusal above must not cost the exported case anything.

<!-- test: exported-static-cross-file -->
```maxon
// --- file: holder.maxon
typealias Count = int(0 to u64.max)

export type Holder
	export static var cached = Holder.build()
	export var value as Count

	static function build() returns Holder
		return Holder{value: 7}
	end 'build'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let a = Holder.cached
	return a.value
end 'main'
```
```exitcode
7
```

<!-- test: error.typealias-with-unknown-element-type -->
```maxon
typealias BadArray = Array with UnknownType

type Container
	var items as BadArray
end 'Container'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/export-keyword/error.typealias-with-unknown-element-type.test:2:44: Unknown type: UnknownType
```

<!-- test: exported-type-cross-file -->
```maxon
// --- file: api/point.maxon
typealias Integer = int(i64.min to i64.max)

export type Point
	export var x as Integer
	export var y as Integer

	export static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = Point.create(20, y: 22)
	return p.x + p.y
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-type-cross-file -->
```maxon
// --- file: point.maxon
typealias Integer = int(i64.min to i64.max)

type InternalPoint
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'InternalPoint'

// --- file: main.maxon
function main() returns ExitCode
	let p = InternalPoint.create(42)
	return p.x
end 'main'
```
```maxoncstderr
error E4006: specs/fragments/export-keyword/error.non-exported-type-cross-file.test:16:11: Unknown type 'InternalPoint' in field access chain
```

<!-- test: exported-enum-cross-file -->
```maxon
// --- file: api/color.maxon
export enum Color
	red
	green
	blue
end 'Color'

// --- file: app/main.maxon
function main() returns ExitCode
	let c = Color.blue
	match c 'check'
		blue then return 42
		red then return 0
		green then return 0
	end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-enum-cross-file -->
```maxon
// --- file: status.maxon
enum InternalStatus
	ok
	err
end 'InternalStatus'

// --- file: main.maxon
function main() returns ExitCode
	let s = InternalStatus.ok
	return 0
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/export-keyword/error.non-exported-enum-cross-file.test:10:10: Undefined variable 'InternalStatus'
```

### A member the COMPILER wrote is as visible as the type it belongs to

`clone`, `equals` and `hash` are synthesized for a type whose members all conform — nobody writes
them, so nobody writes a visibility modifier for them either. The only visibility the declaration
states is the TYPE's, and that is the one they take.

⚠ **THEY USED TO TAKE NONE, WHICH MEANT FILE-PRIVATE, AND THE OPERATOR DISAGREED WITH THE
METHOD.** `IsFunctionVisible` reads `IsExported`/`IsModuleVisible` off the function, and the six
places that register a synthesized member set neither — so `h.clone()` one file over was
`E3008: function 'Holder.clone' is not exported` while `a == b` over the identical pair compiled and
ran, because `==` reaches the same `equals` symbol without asking. One operation, two spellings,
disagreeing. Five arrivals of that one cause are pinned below: a struct's `clone` and `equals`, a
union's `clone`, and an enum's `equals` and `hash`.

⚠ **THE SAME ASYMMETRY SURVIVES ONE VISIBILITY DOWN, THROUGH A DIFFERENT DOOR, AND THE RULE THERE
IS NOT SETTLED.** On a value of a NON-exported type held across a file boundary — the shape the next
section blesses — `a == b` compiles and runs (`non-exported-struct-values-compare-across-a-file-boundary`,
and measured again with a SYNTHESIZED `equals`), while `a.equals(b)` and `a.clone()` are refused
`E4006: Unknown type 'Rec' in field access chain`. That refusal is the TYPE-name check at the field
access chain, not the function-visibility check above, and it is the same check that pins
`error.non-exported-type-cross-file` and `error.string-pattern-against-a-non-exported-struct-scrutinee`.
Whether the next section's *"it writes no name, so there is no visibility to demand"* extends to a
member the COMPILER named is a question for its own rung; nothing here changes it either way.

<!-- test: synthesized-clone-crosses-an-exported-type-s-file-boundary -->
```maxon
// --- file: holder.maxon
typealias Integer = int(i64.min to i64.max)

export type Holder
	export var count as Integer
	export var scale as Integer

	export static function make(c Integer, s Integer) returns Holder
		return Holder{count: c, scale: s}
	end 'make'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let h = Holder.make(3, s: 4)
	let c = h.clone()
	return c.count + c.scale
end 'main'
```
```exitcode
7
```

<!-- test: synthesized-equals-crosses-an-exported-type-s-file-boundary -->
The method half of the case at `non-exported-struct-values-compare-across-a-file-boundary`, which
pins the OPERATOR. `maxon-selfhosted` rewrites a struct `==` into a `methodCall` of `equals` and has
no field-wise fallback (`Compiler/IR/TypeResolution.maxon:7831`), so the two spellings are one call
and may not answer differently.
```maxon
// --- file: holder.maxon
typealias Integer = int(i64.min to i64.max)

export type Holder
	export var count as Integer
	export var scale as Integer

	export static function make(c Integer, s Integer) returns Holder
		return Holder{count: c, scale: s}
	end 'make'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let a = Holder.make(3, s: 4)
	let b = Holder.make(3, s: 4)
	let c = Holder.make(9, s: 9)
	if a.equals(b) 'same'
		if a.equals(c) 'differs'
			return 1
		end 'differs'
		return 7
	end 'same'
	return 2
end 'main'
```
```exitcode
7
```

<!-- test: synthesized-clone-crosses-an-exported-union-s-file-boundary -->
```maxon
// --- file: answer.maxon
typealias Integer = int(i64.min to i64.max)

export union Answer
	small
	big(n Integer)
end 'Answer'

export function classify(n Integer) returns Answer
	if n > 10 'big'
		return Answer.big(n)
	end 'big'
	return Answer.small
end 'classify'

// --- file: main.maxon
function main() returns ExitCode
	let a = classify(42)
	let c = a.clone()
	return match c 'm'
		small gives 1
		big(n) gives n
	end 'm'
end 'main'
```
```exitcode
42
```

<!-- test: synthesized-equals-crosses-an-exported-enum-s-file-boundary -->
```maxon
// --- file: shade.maxon
typealias Integer = int(i64.min to i64.max)

export enum Shade
	dim
	bright
end 'Shade'

export function pick(n Integer) returns Shade
	if n > 1 'b'
		return Shade.bright
	end 'b'
	return Shade.dim
end 'pick'

// --- file: main.maxon
function main() returns ExitCode
	let a = pick(0)
	let b = pick(5)
	if a.equals(b) 'same'
		return 1
	end 'same'
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: synthesized-hash-crosses-an-exported-enum-s-file-boundary -->
Two equal values hash equal — asserted as a RELATION so the case pins the call being reachable
rather than any particular hash function.
```maxon
// --- file: shade.maxon
typealias Integer = int(i64.min to i64.max)

export enum Shade
	dim
	bright
end 'Shade'

export function pick(n Integer) returns Shade
	if n > 1 'b'
		return Shade.bright
	end 'b'
	return Shade.dim
end 'pick'

// --- file: main.maxon
function main() returns ExitCode
	let a = pick(5)
	let b = pick(9)
	if a.hash() == b.hash() 'agree'
		return 7
	end 'agree'
	return 1
end 'main'
```
```exitcode
7
```

### Holding a value of a type you may not NAME

A file that RECEIVES a value of another file's non-exported `enum`, `union` or `type` — from an
exported function's declared return — may hold it, reassign it, compare it and hand it back. It writes
no name, so there is no visibility to demand: what the compiler needs is the type's REPRESENTATION,
which is the same fact for every reader. Every REACH into such a value stays refused by the cases
below.

⚠ Each of these five programs was an INTERNAL COMPILER CRASH (`E9001`, a .NET `KeyNotFoundException`
with a stack trace) until BATCH43, because the per-file type registry — which answers "may this file
NAME this type?" — was indexed for the representation too.

<!-- test: non-exported-union-value-crosses-a-file-boundary -->
```maxon
// --- file: answer.maxon
typealias Integer = int(i64.min to i64.max)

union Answer
	small
	big(n Integer)
end 'Answer'

export function classify(n Integer) returns Answer
	if n > 10 'big'
		return Answer.big(n)
	end 'big'
	return Answer.small
end 'classify'

export function score(a Answer) returns Integer
	return match a 'm'
		small gives 1
		big(n) gives n
	end 'm'
end 'score'

// --- file: main.maxon
function main() returns ExitCode
	let a = classify(42)
	return score(a) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: non-exported-union-value-is-reassignable-across-a-file-boundary -->
A `var` holding one is written twice, so the read that crashed is reached from a block other than the
declaring one — the shape that distinguishes this from the inline `score(classify(42))`, which always
compiled because the value never landed in a local at all.
```maxon
// --- file: answer.maxon
typealias Integer = int(i64.min to i64.max)

union Answer
	small
	big(n Integer)
end 'Answer'

export function classify(n Integer) returns Answer
	if n > 10 'big'
		return Answer.big(n)
	end 'big'
	return Answer.small
end 'classify'

export function score(a Answer) returns Integer
	return match a 'm'
		small gives 1
		big(n) gives n
	end 'm'
end 'score'

// --- file: main.maxon
function main() returns ExitCode
	var a = classify(42)
	a = classify(3)
	return score(a) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: non-exported-enum-values-compare-across-a-file-boundary -->
`==` on two values of a non-exported `enum` writes no name either, and a payload-free enum HAS
synthesized equality — so this is the ordinary tag compare, not the union refusal below.
```maxon
// --- file: shade.maxon
typealias Integer = int(i64.min to i64.max)

enum Shade
	dim
	bright
end 'Shade'

export function pick(n Integer) returns Shade
	if n > 5 'b'
		return Shade.bright
	end 'b'
	return Shade.dim
end 'pick'

// --- file: main.maxon
function main() returns ExitCode
	let s = pick(7)
	let t = pick(1)
	if s == t 'same'
		return 1
	end 'same'
	return 9
end 'main'
```
```exitcode
9
```

<!-- test: non-exported-enum-case-reaches-a-file-through-an-exported-constant -->
An exported constant whose value is a case of a NON-exported enum is INLINED at the reader, which
never writes the enum's name. The case's raw value is a representation question like the rest.
```maxon
// --- file: shade.maxon
typealias Integer = int(i64.min to i64.max)

enum Shade
	dim
	bright
end 'Shade'

export let Preferred = Shade.bright

export function shadeRank(s Shade) returns Integer
	return match s 'm'
		dim gives 0
		bright gives 7
	end 'm'
end 'shadeRank'

// --- file: main.maxon
function main() returns ExitCode
	return shadeRank(Preferred) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: non-exported-struct-values-compare-across-a-file-boundary -->
The same fact one type-kind over: `==` on two values of a non-exported `type` that implements
`Equatable` dispatches to the type's own `equals` and writes no name.
```maxon
// --- file: rec.maxon
typealias Integer = int(i64.min to i64.max)

type Rec implements Equatable
	export let n as Integer

	export static function create(n Integer) returns Rec
		return Self{n: n}
	end 'create'

	export function equals(other Rec) returns bool
		return self.n == other.n
	end 'equals'
end 'Rec'

export function build(n Integer) returns Rec
	return Rec.create(n)
end 'build'

// --- file: main.maxon
function main() returns ExitCode
	let a = build(1)
	let b = build(2)
	if a == b 'same'
		return 1
	end 'same'
	return 9
end 'main'
```
```exitcode
9
```

### Reaching INTO one is refused

<!-- test: error.match-on-a-non-exported-union-cross-file -->
A `match` reads the case set out of the layout, so it is a REACH — the same event `p.x` and
`s.ordinal` are refused for above. The refusal was never written at the scrutinee, so the missing
registry entry surfaced as `E9001` plus a .NET stack trace instead of a diagnostic.

⚠ **THE TWO COMPILERS DISAGREE ABOUT THIS PROGRAM AND THE RULE IS NOT SETTLED.** shv2 COMPILES it: it
enforces no visibility at a match scrutinee at all, which is why its own E3092 hygiene check could
advise dropping an `export` that made the bootstrap crash. What is settled is that a `KeyNotFoundException`
is not an answer to a user program under any reading. A port of this case to `specs-shv2/` must stay
disabled until the visibility question is ruled on.
```maxon
// --- file: answer.maxon
typealias Integer = int(i64.min to i64.max)

union Answer
	small
	big(n Integer)
end 'Answer'

export function classify(n Integer) returns Answer
	if n > 10 'big'
		return Answer.big(n)
	end 'big'
	return Answer.small
end 'classify'

// --- file: main.maxon
function main() returns ExitCode
	return match classify(42) 'a'
		small gives 1
		big(n) gives n
	end 'a'
end 'main'
```
```maxoncstderr
error E4006: specs/fragments/export-keyword/error.match-on-a-non-exported-union-cross-file.test:19:9: Unknown type 'Answer' in match scrutinee
```

<!-- test: error.compare-a-non-exported-union-cross-file -->
⭐ **A UNION IS NOT COMPARABLE WHETHER OR NOT THE READER MAY NAME IT, AND THE GUARD USED TO ASK THE
WRONG QUESTION.** It tested the per-file registry — "may this file name the type?" — so for a
non-exported union it did not fire at all, and the backing walk one line below would then have
compared TAGS, making `big(1) == big(2)` true. It never shipped only because that walk's own raw
lookup crashed first. Comparability is a property of the TYPE, so it is asked of the type wherever it
is declared.
```maxon
// --- file: answer.maxon
typealias Integer = int(i64.min to i64.max)

union Answer
	small
	big(n Integer)
end 'Answer'

export function classify(n Integer) returns Answer
	if n > 10 'big'
		return Answer.big(n)
	end 'big'
	return Answer.small
end 'classify'

// --- file: main.maxon
function main() returns ExitCode
	let a = classify(11)
	let b = classify(42)
	if a == b 'same'
		return 1
	end 'same'
	return 0
end 'main'
```
```maxoncstderr
error E3066: specs/fragments/export-keyword/error.compare-a-non-exported-union-cross-file.test:21:7: cannot compare union values using '==', use 'match' instead
```

<!-- test: error.string-pattern-against-a-non-exported-struct-scrutinee -->
⚠ **A DIAGNOSTIC THAT HAD TO LOOK THE TYPE UP TO WORD ITSELF, AND CRASHED DOING SO.** The pattern is
ill-formed whatever `Rec`'s visibility is, but deciding that meant asking whether `Rec` conforms to
`BuiltinStringLiteral` — off the per-file registry, which has no entry for a type this file may not
name. shv2 refuses the same program with the same code.
```maxon
// --- file: rec.maxon
typealias Integer = int(i64.min to i64.max)

type Rec
	export let n as Integer

	export static function create(n Integer) returns Rec
		return Self{n: n}
	end 'create'
end 'Rec'

export function build(n Integer) returns Rec
	return Rec.create(n)
end 'build'

// --- file: main.maxon
function main() returns ExitCode
	let r = build(1)
	match r 'x'
		"hi" then return 1
		default panic("no")
	end 'x'
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/export-keyword/error.string-pattern-against-a-non-exported-struct-scrutinee.test:21:3: pattern type 'String' does not match scrutinee type 'Rec'
```

<!-- test: exported-typealias-cross-file -->
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function main() returns ExitCode
	let s = 42 as Score
	return s
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-typealias-cross-file -->
```maxon
// --- file: types.maxon
typealias InternalScore = int(0 to 100)

// --- file: main.maxon
function main() returns ExitCode
	let s = 42 as InternalScore
	return s
end 'main'
```
```maxoncstderr
error E3062: specs/fragments/export-keyword/error.non-exported-typealias-cross-file.test:3:11: unused typealias: 'InternalScore'
error E2003: specs/fragments/export-keyword/error.non-exported-typealias-cross-file.test:7:16: Expected type name after 'as'
```

<!-- test: error.duplicate-typealias-same-file -->
```maxon
typealias Score = int(0 to 100)
typealias Score = int(0 to 200)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3061: specs/fragments/export-keyword/error.duplicate-typealias-same-file.test:3:11: Duplicate typealias 'Score'
```

<!-- test: non-exported-type-same-file -->
```maxon

typealias Integer = int(i64.min to i64.max)

type InternalPoint
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'InternalPoint'

function main() returns ExitCode
	let p = InternalPoint.create(20, y: 22)
	return p.x + p.y
end 'main'
```
```exitcode
42
```

<!-- test: exported-var-cross-file -->
Cross-file access to an exported module-level var with a simple constant value.
```maxon
// --- file: api/counter.maxon
export var counter = 10

// --- file: app/main.maxon
function main() returns ExitCode
		return counter
end 'main'
```
```exitcode
10
```

<!-- test: exported-struct-var-cross-file -->
Cross-file access to an exported module-level struct var.
```maxon
// --- file: api/state.maxon
typealias SmallInt = int(0 to u8.max)

export type Counter
		export var value as SmallInt

		export static function create(value SmallInt) returns Self
			return Self{value: value}
		end 'create'
end 'Counter'

export var shared = Counter.create(0)

// --- file: app/main.maxon
function main() returns ExitCode
		let c = Counter.create(1)
		shared.value = 42 - c.value + c.value
		return shared.value
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-var-cross-file -->
Non-exported module-level var should not be accessible from another file.
```maxon
// --- file: state.maxon
var secret = 99

// --- file: main.maxon
function main() returns ExitCode
		return secret
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/export-keyword/error.non-exported-var-cross-file.test:7:10: Undefined variable 'secret'
```

<!-- test: non-exported-enum-same-file -->
```maxon
enum Direction
	up
	down
end 'Direction'

function main() returns ExitCode
	let d = Direction.up
	match d 'check'
		up then return 42
		down then return 0
	end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: error.stdlib-non-exported-method-is-not-callable -->
**A stdlib declaration's visibility means what it says.** It did not used to: `StdlibLoader`
force-exported every stdlib function after the stdlib's own compile (`func.IsExported = true;
func.IsModuleVisible = false`, "Stdlib symbols are globally visible"), so **no stdlib function could
be internal whatever it declared** — `String.mapAsciiCase` here, and `String.addressableBytes`, the
stdlib's own raw door to a string's bytes, which Stage 4c had just introduced *precisely* to stop
user code reaching those bytes. The door was reachable the day it was built.

Removing the force-export cost exactly ten `export` keywords across the stdlib — `Iterable.map` /
`filter` / `contains`, `Stdin.readLine`, and six `JsonDoc` accessors — all genuine public API that had
been riding it rather than declaring itself. Nothing else in the stdlib wanted to be public.
```maxon
function main() returns ExitCode
	let s = "HELLO"
	let x = s.mapAsciiCase(65, hi: 90, delta: 32)
	print("{x}")
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/export-keyword/error.stdlib-non-exported-method-is-not-callable.test:4:12: function 'stdlib.String.mapAsciiCase' is not exported
```

<!-- test: error.stdlib-non-exported-static-method-is-not-callable -->
A non-exported **static** method is not callable either. It used to be: a type-qualified name skips
`ResolveFunctionOverloads`' visibility filter **on purpose**, so that a hidden TYPE reports E4006
rather than a confusing not-exported about its method — but only the INSTANCE path re-applied the
function's own visibility afterwards, and the static path never did.

So `String.fromOwnedBytes` was callable from anywhere while documenting itself *"Not exported: 'take
these bytes and trust me about them' is not a promise the stdlib can let arbitrary code make."*
```maxon
function main() returns ExitCode
	var bytes = ByteArray.create()
	bytes.push(74)
	let s = String.fromOwnedBytes(bytes, isAscii: true)
	print("{s}")
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/export-keyword/error.stdlib-non-exported-static-method-is-not-callable.test:5:17: function 'stdlib.String.fromOwnedBytes' is not exported
```
