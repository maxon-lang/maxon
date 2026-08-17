---
feature: static-variables
status: experimental
keywords: [static, var, global, mutable, module, type]
category: language
---
# Static Variables

## Documentation

### Top-Level `var` Declarations

Top-level `var` declarations define mutable module-level variables. Unlike `let` constants which are compile-time evaluated and stored in read-only memory, `var` declarations create mutable storage in the program's data section.

#### Syntax

```maxon
var counter = 0
export var globalState = false
```

#### Features

- **Runtime storage**: Variables are stored in the writable data section
- **Initialization**: Initializers are evaluated at program start before `main`
- **Type inference**: Type is inferred from the initializer
- **Export support**: Use `export var` to make variables available to other modules

#### Initializer Requirements

Top-level `var` initializers must be constant expressions (same rules as `let`):
- Literals: integers, floats, booleans, strings, bytes, characters
- Array literals whose elements are integer constant expressions or String literals
- Arithmetic and logical operations on constants
- References to other top-level constants
- Enum member access

Function calls and runtime expressions are not allowed in initializers.

### Static Fields in Types

Types can have static fields that are shared across all instances. Static fields use the `static` keyword before `var` or `let`.

#### Syntax

```maxon
typealias Score = int(i64.min to i64.max)

type Counter
	static var count = 0       // Mutable static field
	static let MAX = 100       // Compile-time static constant

	export var value as Score       // Instance field
end 'Counter'
```

#### Features

- **Shared storage**: One copy exists for the type, not per instance
- **Direct access**: Access via `TypeName.fieldName` syntax
- **Static let**: Compile-time constant (same as top-level `let`)
- **Static var**: Mutable storage (same as top-level `var`)

#### Access Patterns

```maxon
Counter.count = Counter.count + 1   // Access static field
var c = Counter.create(10)          // Create instance
c.value = 20                        // Access instance field
```

## Tests

<!-- test: top-level-var-basic -->
```maxon
var counter = 0

function main() returns ExitCode
	counter = 42
	return counter
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-increment -->
```maxon

typealias Integer = int(i64.min to i64.max)

var total = 10

function add(n Integer)
	total = total + n
end 'add'

function main() returns ExitCode
	add(5)
	add(27)
	return total
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-multiple -->
```maxon
var a = 1
var b = 2
var c = 3

function main() returns ExitCode
	a = a * 10
	b = b * 10
	c = c * 10
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: top-level-var-with-let -->
```maxon
let BASE = 40
var offset = 0

function main() returns ExitCode
	offset = 2
	return BASE + offset
end 'main'
```
```exitcode
42
```

<!-- test: static-var-basic -->
```maxon
type Counter
	static var count = 0
end 'Counter'

function main() returns ExitCode
	Counter.count = 42
	return Counter.count
end 'main'
```
```exitcode
42
```

<!-- test: static-var-increment -->
```maxon
type Counter
	static var count = 0

	static function increment()
		Counter.count = Counter.count + 1
	end 'increment'
end 'Counter'

function main() returns ExitCode
	Counter.increment()
	Counter.increment()
	Counter.increment()
	return Counter.count
end 'main'
```
```exitcode
3
```

<!-- test: static-let-basic -->
```maxon
type Config
	static let MAX_SIZE = 42
end 'Config'

function main() returns ExitCode
	return Config.MAX_SIZE
end 'main'
```
```exitcode
42
```

<!-- test: static-var-multiple-types -->
```maxon
type TypeA
	static var value = 10
end 'TypeA'

type TypeB
	static var value = 20
end 'TypeB'

function main() returns ExitCode
	TypeA.value = TypeA.value + 2
	TypeB.value = TypeB.value + 10
	return TypeA.value + TypeB.value
end 'main'
```
```exitcode
42
```

<!-- test: static-and-instance-fields -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Thing
	static var created = 0
	export var id as Integer

	static function make(n Integer) returns Thing
		Thing.created = Thing.created + 1
		return Thing{id: n}
	end 'make'
end 'Thing'

function main() returns ExitCode
	let a = Thing.make(10)
	let b = Thing.make(20)
	return Thing.created + a.id + b.id
end 'main'
```
```exitcode
32
```

### A static member IS a top-level binding whose name carries a dot

shv2 makes `static let MAX = 100` inside `type Config` a top-level binding named `Config.MAX` — the same
storage key both reference compilers use (`maxon-sharp/Compiler/2-Parser.cs:7812` builds
`$"{typeName}.{fieldName}"`; v1 keys a `static let` "by its enclosing type",
`maxon-selfhosted/Compiler/Project.maxon:187`). Everything below follows from that one sentence rather
than from machinery of its own: per-type identity is the qualifier, the initializer obeys the top-level
initializer grammar exactly, a `static let` refuses a write through the same E2013 door a file-scope `let`
does, and a static member takes no slot in the instance layout because it never joins one.

<!-- test: error.static-let-reassign -->
Assigning to a `static let` is E2013, in the same words a top-level `let` gets
(`top-level-let-scalar-reassign-error`) and naming the qualified binding the author actually wrote. Both
reference compilers refuse this program; only the sentence differs, and the bootstrap's is
`E3003: 'Config' is a type and cannot be used directly as a value` — a noun about the base rather than
about the `let`, which is the "sends the reader hunting for a typo" shape that spec case exists to argue
against.
```maxon
type Config
	static let MAX_SIZE = 42
end 'Config'

function main() returns ExitCode
	Config.MAX_SIZE = 7
	return Config.MAX_SIZE
end 'main'
```
```maxoncstderr
error E2013: <fragment>:7:2: cannot assign to immutable variable: 'Config.MAX_SIZE'
```

<!-- test: error.static-member-undeclared -->
⭐ **THE NEGATIVE CONTROL FOR THE STATIC-MEMBER ARM**: a member the type never declared must NOT be
claimed by it. The declaring type is right there, so a probe keyed on the BASE alone would have taken this
and then had no binding to read; keying on the QUALIFIED name is what makes the arm decline and the
statement fall through to the reading it had before static members existed — the sized numeric bound,
which is the last resort for any dotted form nothing else claims.

⚠ The sentence is therefore that arm's, and it is unchanged and pre-existing: it says `min or max` about
a base that is a declared `type` rather than a sized numeric one. The runnable oracle answers the same
program `E3003: 'Config' is a type and cannot be used directly as a value`, which is a better noun; the
two are recorded as they are rather than reconciled here, because changing the last-resort arm's wording
moves every dotted refusal in the corpus and belongs to whichever rung owns that arm.
```maxon
type Config
	static let MAX_SIZE = 42
end 'Config'

function main() returns ExitCode
	return Config.MIN_SIZE
end 'main'
```
```maxoncstderr
error E2010: <fragment>:7:16: Expected 'min or max' but got 'MIN_SIZE'
```

<!-- test: static-let-cross-file -->
Cross-file: the storage key is the qualified name and nothing else, so an `export static let` read from
another file resolves through the very probe an `export let` at file scope does. This is where a
name-keying mistake would show — a key that carried the declaring FILE would make the reader's lookup
miss, and one that dropped the type would make two types' `MAX_SIZE` one slot.
```maxon
// --- file: api/limits.maxon
export type Config
	export static let MAX_SIZE = 40
	export static var used = 2
end 'Config'

// --- file: app/main.maxon
function main() returns ExitCode
	Config.used = Config.used + Config.MAX_SIZE
	return Config.used
end 'main'
```
```exitcode
42
```

<!-- test: error.static-binding-in-an-extension -->
An `extension` body declares no storage. Its members are re-parsed once per conforming type, so a binding
written there has no declaration to belong to — and the declaration sweep, which is what builds the
conformer list, cannot know that list while it is reading the extension. Refused at the keyword rather
than admitted into a slot the sweep never recorded.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Tagged
	function tag() returns Integer
end 'Tagged'

type Widget implements Tagged
	export var v as Integer
end 'Widget'

extension Tagged
	static let LIMIT = 4

	export function tag() returns Integer
		return 1
	end 'tag'
end 'Tagged'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:9: Unsupported: a `let` declaration in an `extension` body — an extension adds methods to types other declarations named and declares no storage of its own, so there is no type for this binding to belong to; declare it in the `type` body as a `static let`
```

### A static member's initializer is the TOP-LEVEL initializer grammar, not a subset of it

Nothing about a static member's initializer is its own: it is read by the one top-level binding reader and
folded by the one constant evaluator, so every form a file-scope `let`/`var` admits a static member admits,
and every form it refuses a static member refuses in the same words. In particular the initializer is **not
restricted to literals** — a user static FACTORY CALL is built by `__module_init` before `main` exactly as
`top-level-factory-globals.md` describes it, and a managed one is released by `__maxon_global_cleanup`
(these cases are leak-gated: a missed drop is exit 101).

⭐ **THE ONE FORM WHOSE ANSWER DEPENDS ON *WHICH* BODY THE BINDING WAS DECLARED IN IS A STRUCT LITERAL**, and
that is E3076's ordinary constructor restriction rather than a rule of the initializer grammar. A static
member is written inside the `type` body that declares it, so `static var origin = Pair{a: 1, b: 2}` inside
`type Pair` is admitted for the same reason `Self{…}` is admitted in a method; the identical initializer at
FILE scope is written inside no type body and stays refused. `specs/lazy-static.md` holds the cases for both
halves.

⛔ **THIS SECTION USED TO ASSERT THE OPPOSITE** — that a struct literal is refused as a static initializer,
"a property of the top-level initializer grammar rather than of `static`", with `specs/lazy-static.md`
nominated as the rung that would close it. That rung has landed, and the sentence was already only half
true when it was written: the refusal it described was `E2004 Undefined constant 'CharacterSet'`, a message
about a `let` nobody wrote, and the file-scope spelling it pointed at is refused for a completely different
reason (E3076). The two cases below are what a claim of this shape is owed.

### A struct-literal initializer's field refusals are the LITERAL's own, not the initializer grammar's

The construction reaches the same three deciders a literal written in a method body reaches — the field
roster, the "every slot must end up initialized" rule and the constructor restriction — so a bad field is
refused for what is wrong with it rather than for standing in an initializer.

⚠ **THE TWO CASES BELOW ARE HERE, AND NOT IN `specs/lazy-static.md`, BECAUSE shv2 AND THE BOOTSTRAP WORD
THESE TWO DIAGNOSTICS DIFFERENTLY — AND HAVE DONE SINCE LONG BEFORE THIS FORM EXISTED.** MEASURED on ONE
program with the literal written in a method BODY, where both compilers have always accepted it:

| | shv2 | bootstrap |
|---|---|---|
| a field the type does not declare | `E3018 …:8:27: type 'Pair' has no field named 'c'` | `E3018 …:8:27: Type 'Pair' has no field 'c'` |
| a field the literal never fills | `E3086 …:8:10: field 'b' of 'Pair' is not initialized by this literal, and it has no default value` | `E3086 …:8:14: Field 'b' of type 'Pair' is not initialized (provide in literal, add a default value on the declaration, or assign via self.field in a static factory)` |

The CODES agree and E3018's column agrees; only the sentences (and E3086's anchor) do not. A shared
`maxoncstderr` block can carry one spelling, so the canonical file carries the cases whose two compilers
already agree byte for byte and these two live here, pinned against shv2's own — which is the arrangement
`error.static-let-reassign` above already makes for E2013 and for the same reason.

<!-- test: error.static-struct-literal-unknown-field -->
```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count

	static var origin = Pair{a: 1, b: 2, c: 3}

	export static function get() returns Pair
		return Pair.origin
	end 'get'
end 'Pair'

function main() returns ExitCode
	let p = Pair.get()
	print("{p.a} {p.b}")
	return 0
end 'main'
```
```maxoncstderr
error E3018: <fragment>:8:39: type 'Pair' has no field named 'c'
```

<!-- test: error.static-struct-literal-missing-field -->
```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count

	static var origin = Pair{a: 1}

	export static function get() returns Pair
		return Pair.origin
	end 'get'
end 'Pair'

function main() returns ExitCode
	let p = Pair.get()
	print("{p.a} {p.b}")
	return 0
end 'main'
```
```maxoncstderr
error E3086: <fragment>:8:22: field 'b' of 'Pair' is not initialized by this literal, and it has no default value
```

### A struct-literal initializer's field must suit the slot it fills

The tag comparison is what makes the STORE right: a pointer written into a scalar slot reads back as a
number, and a number written into a managed one is a wild pointer. The range half is the evaluator's own,
for the reason `top-level-let.md` gives a constant cast — a top-level initializer is not a function, so it
records no site for the range pass to fold and the compile-time refusal has to be raised where the constant
is read.

<!-- test: error.static-struct-literal-field-type-mismatch -->
```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count

	static var origin = Pair{a: "hi", b: 2}

	export static function get() returns Pair
		return Pair.origin
	end 'get'
end 'Pair'

function main() returns ExitCode
	let p = Pair.get()
	print("{p.a} {p.b}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:27: cannot assign 'String' to variable 'Pair.a' of type 'int'
```

<!-- test: error.static-struct-literal-field-out-of-range -->
```maxon
typealias Percent = int(0 to 100)

type Pair
	export var a as Percent

	static var origin = Pair{a: 500}

	export static function get() returns Pair
		return Pair.origin
	end 'get'
end 'Pair'

function main() returns ExitCode
	let p = Pair.get()
	print("{p.a}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:27: Value 500 is outside the range of 'Percent' (int(0 to 100))
```

### The slot a field's constant must suit is the one the field DECLARES, never the width it is STORED at

⛔ **A STORE WIDTH IS A LOSSY PROJECTION OF A TYPE, AND TWO SLOT KINDS PROJECT ONTO A BARE MACHINE WORD.** A
declared `enum`/`union` field stores its tag in one word and an `interface`-typed field accesses each half of
its fat pointer as one word, so a check written against the WIDTH sees `int` at both and admits any integer
constant. Neither admission is a refusal the language makes anywhere else: the same two literals written in a
method BODY are refused, and the runnable oracle refuses the enum one too.

The two cases below are shv2-worded for the reason the pair above is — the bootstrap says
`cannot assign a value of type 'int' to field 'c' of 'Paint', which holds 'enum'` for the first and cannot
express the second at all (it fails the same program with an internal `E9001`, its own defect). The enum
refusal's CODE agrees; only the sentence does not.

<!-- test: error.static-struct-literal-enum-field -->
```maxon
typealias Count = int(0 to u64.max)

enum Color
	red
	green
	blue
end 'Color'

type Paint
	export let c as Color
	export let n as Count

	static var one = Paint{c: 7, n: 1}

	export static function get() returns Paint
		return Paint.one
	end 'get'
end 'Paint'

function main() returns ExitCode
	let p = Paint.get()
	print("{p.n}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:25: cannot assign 'int' to variable 'Paint.c' of type 'Color'
```

An `interface`-typed field is refused OUTRIGHT rather than by a comparison, and the refusal is the body
path's own sentence (`Parser.widenValueIntoExistentialField`): the second word of a fat pointer is the
address of the conformer's witness table, which only a widening SITE can name, and a top-level initializer
mints no values at all — so no constant this evaluator can produce carries one. Left admitted, the box got
ONE store into a TWO-slot field and the witness half stayed whatever `__mm_alloc` left there; the first
dispatch through it was an access violation, MEASURED.

⚠ **IT IS ANCHORED AT THE DECLARED NAME, not at the field value, and that is forced rather than chosen.**
The constant evaluator cannot see such a field at all: a swept interface annotation is a bare `named` until
`normalizeSweptInterfaceTypes` re-tags the column, and that runs AFTER the evaluator. The first door that
sees the re-tagged column and has a positioned diagnostic is the real parse's own binding-failure door, and
what it holds is the declaration's name. The same program with the value written as a conforming
`Square.create(4)` is refused identically, in the same words and at the same position — the slot is what
makes it impossible, not the value.

<!-- test: error.static-struct-literal-interface-field -->
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Square implements Sized
	export let side as Integer

	export static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	export function size() returns Integer
		return self.side * self.side
	end 'size'
end 'Square'

type Holder
	export let s as Sized
	export let tag as Integer

	static var one = Holder{s: 5, tag: 77}

	export static function get() returns Holder
		return Holder.one
	end 'get'
end 'Holder'

function main() returns ExitCode
	let h = Holder.get()
	print("{h.s.size()} {h.tag}")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:24:13: Unsupported: storing a value of a CONCRETE type into the interface-typed field 'Holder.s' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and the second word is the address of the conformer's witness table, which only a widening SITE can name. A field store has no callee whose declared parameter types could name it, unlike a call argument. Pass the value to a named function taking the interface as a PARAMETER and store THAT parameter (which arrives already widened), or declare the field at a concrete type
```

<!-- test: static-factory-initializer -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Row
	export var n as Integer

	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Cache
	static var head = Row.create(42)
end 'Cache'

function main() returns ExitCode
	return Cache.head.n as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: static-managed-members -->
A `String` static and an array static in one type: both materialize once before `main`, both are read
through their qualified names, the array's record is MUTATED in place through a method call written as a
statement, and both are freed at exit.
```maxon
type Names
	static let GREETING = "hi"
	static var items = [1, 2, 3]
end 'Names'

function main() returns ExitCode
	Names.items.push(9)
	print("{Names.GREETING}{Names.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi4
```

<!-- test: error.static-let-array-mutate -->
⭐⭐ **THE WRITE-THROUGH-AN-IMMUTABLE-STATIC GUARD, AND IT WAS MEASURED FAILING.** A receiver-writing method
on a `static let` array is E3019 — the SAME `requireMutableReceiver` rule
`error.top-level-let-array-mutate` pins for a file-scope `let`, now naming the qualified binding.

Before the static read was routed through the receiver door it fell out to `parsePostfix`, whose receiver
is deliberately nameless (it serves literals and call results, which are bound to no name), so the blame
name never reached the check: this exact program COMPILED CLEAN and returned **5** — a mutation of a
binding declared `let`, with no diagnostic anywhere — while its file-scope twin was refused. The runnable
oracle refuses the whole shape from further back (`E3003: 'Cache' is a type and cannot be used directly as
a value`), so it could not have caught this one for us.
```maxon
type Cache
	static let xs = [1, 2, 3]
end 'Cache'

function main() returns ExitCode
	let v = try Cache.xs.pop() otherwise 0
	return (v + Cache.xs.count()) as ExitCode
end 'main'
```
```maxoncstderr
error E3019: <fragment>:7:23: cannot pass 'Cache.xs' to function that mutates parameter 'self' (in main)
```

<!-- test: static-struct-member-field-write -->
A field STORE through a struct-valued static goes through the ordinary field-chain door, rooted at the
qualified name. The read side worked from the moment the static arm existed (the value falls out and
`parsePostfix` takes the hop); the WRITE reaches the chain resolver, where the base `Cache` is neither a
value in scope nor a bare global and fell to the tail as `E2004: Undefined variable 'Cache'` — a name
defined four lines up. Both spellings now root at `Cache.head`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Row
	export var n as Integer

	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Cache
	static var head = Row.create(1)
end 'Cache'

function main() returns ExitCode
	Cache.head.n = 42
	return Cache.head.n as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: static-var-bool -->
```maxon
var initialized = false

function init()
	initialized = true
end 'init'

function main() returns ExitCode
	if initialized 'check1'
		return 1
	end 'check1'
	init()
	if initialized 'check2'
		return 42
	end 'check2'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: static-var-bool-adjacent-globals -->
<!-- P1.2 String — the `print` builtin -->
Bool global followed by non-zero global must not bleed adjacent data.

```maxon
var flag = false
var counter = 42

function main() returns ExitCode
	if flag 'checkFalse'
		print("flag should be false\n")
		return 1
	end 'checkFalse'
	if counter == 42 'checkCounter'
		return 0
	end 'checkCounter'
	print("counter wrong\n")
	return 1
end 'main'
```
```exitcode
0
```

<!-- disabled-test: top-level-var-enum-initializer -->
<!-- The reason is NOT "P1.1 enums + `match`", which both landed long ago; RE-MEASURED 2026-08-12 (BATCH36 W65). The blocker is the CONSTANT EVALUATOR's missing enum-member arm, and it says so itself, positioned at the initializer: `E2015: Unsupported: `Color.Green` in a constant initializer - a constant is folded before any code runs, so it can name another top-level `let`, a literal, an empty container, a `create()`-style factory at the TOP of an initializer, or a sized type's `.min`/`.max`, and nothing else`. Correctly disabled, wrongly explained: the marker sent a reader to the enum/match rungs, which are done, instead of to the one arm that is missing. -->
```maxon
enum Color
		Red
		Green
		Blue
end 'Color'

var current = Color.Green

function main() returns ExitCode
	let isGreen = match current 'check'
		Green gives true
		Red gives false
		Blue gives false
	end 'check'
	if isGreen 'check'
		current = Color.Blue
		let isBlue = match current 'check2'
			Blue gives true
			Red gives false
			Green gives false
		end 'check2'
		if isBlue 'check2'
			return 42
		end 'check2'
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```

<!-- disabled-test: top-level-var-enum-initializer-cross-file -->
<!-- The reason is NOT "P1.1 enums + `match`", which both landed long ago; RE-MEASURED 2026-08-12 (BATCH36 W65). The blocker is the CONSTANT EVALUATOR's missing enum-member arm, and it says so itself, positioned at the initializer: `E2015: Unsupported: `Color.Green` in a constant initializer - a constant is folded before any code runs, so it can name another top-level `let`, a literal, an empty container, a `create()`-style factory at the TOP of an initializer, or a sized type's `.min`/`.max`, and nothing else`. Correctly disabled, wrongly explained: the marker sent a reader to the enum/match rungs, which are done, instead of to the one arm that is missing. -->
Cross-file: enum defined in one file, top-level var initialized with it in another.
```maxon
// --- file: api/defs.maxon
export enum CpuArch
	x64
	arm64
	wasm32
end 'CpuArch'

// --- file: app/main.maxon
var currentCpu = CpuArch.x64

function main() returns ExitCode
	let result = match currentCpu 'check'
		x64 gives 42
		arm64 gives 1
		wasm32 gives 2
	end 'check'
	return result
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-const-expr -->
```maxon
let BASE = 20
var offset = BASE + 1

function main() returns ExitCode
	offset = offset * 2
	return offset
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-array-literal -->
```maxon
var items = [10, 20, 30]

function main() returns ExitCode
	try items.set(1, value: 12) otherwise panic("test invariant: set OOB")
	let a = try items.get(0) otherwise 0
	let b = try items.get(1) otherwise 0
	let c = try items.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
52
```

<!-- test: top-level-var-array-cross-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

var scores = [10, 20, 30]

function getTotal() returns Integer
	let a = try scores.get(0) otherwise 0
	let b = try scores.get(1) otherwise 0
	let c = try scores.get(2) otherwise 0
	return a + b + c
end 'getTotal'

function setScore(index Integer, value Integer)
	try scores.set(index, value: value) otherwise panic("test invariant: set OOB")
end 'setScore'

function main() returns ExitCode
	setScore(1, value: 12)
	return getTotal()
end 'main'
```
```exitcode
52
```

<!-- test: top-level-var-array-mutate-cross-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

var counters = [0, 0, 0]

function increment(index Integer)
	let current = try counters.get(index) otherwise 0
	try counters.set(index, value: current + 1) otherwise panic("test invariant: set OOB")
end 'increment'

function total() returns Integer
	let a = try counters.get(0) otherwise 0
	let b = try counters.get(1) otherwise 0
	let c = try counters.get(2) otherwise 0
	return a + b + c
end 'total'

function main() returns ExitCode
	increment(0)
	increment(0)
	increment(1)
	increment(2)
	increment(2)
	increment(2)
	return total()
end 'main'
```
```exitcode
6
```

### An array binding has STORAGE whichever keyword declared it

A top-level array is a HEAP record with a `.data` pointer slot, built by `__module_init` before `main`
and released by `__maxon_global_cleanup` after it — for a `let` exactly as for a `var`. A `let` differs
in one thing only: it refuses the write, both at an assignment (E2013) and at a receiver-writing method
(E3019).

That is a deliberate divergence from the reference compiler, which makes a never-mutated `let` array an
immortal shared static record. shv2 cannot yet: an `Array`'s own methods rewrite its record (a `push`
detaches the buffer by writing `buffer@0`/`capacity@16`/`length@8`), and a `.rdata` record cannot be
written — so immortality needs an enforced never-mutated guarantee and a real COPY promotion for
`var b = <borrowed array>`, neither of which exists. Heap for both is what shv2 can state truthfully.

<!-- test: top-level-let-array-literal -->
```maxon
let NUMS = [10, 20, 30]

function main() returns ExitCode
	let a = try NUMS.get(0) otherwise 0
	let b = try NUMS.get(2) otherwise 0
	return a + b
end 'main'
```
```exitcode
40
```

<!-- test: top-level-array-const-element -->
An array literal's elements go through the same scalar constant evaluator every other initializer does,
so a reference to another top-level *scalar* constant — and arithmetic on it — is an element.
```maxon
let BASE = 20
var xs = [BASE, BASE + 2]

function main() returns ExitCode
	let a = try xs.get(0) otherwise 0
	let b = try xs.get(1) otherwise 0
	return a + b
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-array-string-elements -->
A String-element array global owns its elements: each literal is CLONED into the record, and
`__managed_decref`'s walk `__str_decref`s every live slot at exit. Leak-gated — a missed clone double-frees
the immortal `.rdata` record and a missed stamp leaks all of them.
```maxon
var names = ["ab", "cde"]

function main() returns ExitCode
	let a = try names.get(0) otherwise ""
	let b = try names.get(1) otherwise ""
	print("{a}{b}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
abcde
```

<!-- test: top-level-array-grow-reassign-untouched -->
Three array globals in one program: one grown past its initial capacity in a loop and then REASSIGNED to
a fresh literal (the old record must be released, not leaked), one never touched at all, and an
immutable one only read. `3 + 4 + 6 + 2`.

⚠ **`untouched` NO LONGER REACHES THE BINARY** (P1.7 slice 3): nothing names it, so dead-global
elimination drops its slot, the `__managed_create`/`__managed_push` run that built it and the `__managed_decref` that
freed it — which is why the golden's `.data` holds `grow` and `fixed` alone. The answer is unmoved (the
declaration never contributed to it), and what the case still pins is the pair that matters here: `grow`
and `fixed` are TWO LIVE array globals sharing one `__module_init`, so the prune is per-global rather
than per-init.
```maxon

var grow = [1]
var untouched = [7, 7, 7]
let fixed = [4, 5, 6]

function main() returns ExitCode
	var i = 0
	while i < 200 'fill'
		grow.push(i)
		i = i + 1
	end 'fill'
	grow = [3, 4]
	let a = try grow.get(0) otherwise 0
	let b = try grow.get(1) otherwise 0
	let c = try fixed.get(2) otherwise 0
	return a + b + c + grow.count()
end 'main'
```
```exitcode
15
```

<!-- test: error.top-level-let-array-mutate -->
A receiver-writing method on a `let`-bound array is E3019 — the SAME rule a `let`-bound String or Set
receiver gets, through the one `requireMutableReceiver`. Without it a `let` array would be writable, since
it has a real slot.
```maxon
let fixed = [4, 5, 6]

function main() returns ExitCode
	fixed.push(9)
	return 0
end 'main'
```
```maxoncstderr
error E3019: <fragment>:5:8: cannot pass 'fixed' to function that mutates parameter 'self' (in main)
```

<!-- test: error.top-level-let-array-reassign -->
Reassigning a `let` array is E2013, not "undefined variable": having a `.data` slot is not permission to
write one, and the name is very much defined.
```maxon
let xs = [1, 2]

function main() returns ExitCode
	xs = [3, 4]
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:5:2: cannot assign to immutable variable: 'xs'
```

<!-- test: error.top-level-array-names-another-global -->
A managed initializer may not name another managed global, in EITHER declaration order — which is what
keeps initialization order a non-problem: there is no managed-to-managed dependency to order and no
managed cycle to detect. Measured against the reference compiler, which reports the same code at the same
position.
```maxon
let A = [1, 2]
let B = A

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:9: Undefined constant 'A'
```

<!-- test: error.top-level-array-names-another-global-forward -->
The same, written the other way round.
```maxon
let B = A
let A = [1, 2]

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:2:9: Undefined constant 'A'
```

<!-- test: error.top-level-let-array-var-alias -->
A `let` array's record cannot be laundered into a mutable ALIAS **within the function that reads it**. A
borrowed managed aggregate bound to a `var` is promoted to owned by an INCREF of the same box — reference
semantics, deliberately, because the alias is observable — so without this refusal `var b = A` then
`b.push(9)` would grow `A` with E2013 and E3019 both intact and nothing to report it. Refused where the
SOURCE is immutable, and only there: the same binding off a `var` global shares, exactly as it does in the
reference compiler.

⚠ **THE GUARD IS INTRA-FUNCTION, AND THAT BOUND IS EXACT.** It is a mark on the VALUE the read produced,
so it reaches every use of that value in that function — the direct binding, a reassignment, and a
ternary/`match` merge that joins it (all four are pinned below). It does NOT cross a CALL: `g(A)` where
`g(xs …)` does `var b = xs` still grows `A` (measured: 3), because the callee's parameter is a fresh value
in a fresh SSA space and whether it may alias an immutable global is a property of every call site, not of
the callee. Closing that needs the same whole-program call-graph fixpoint the transitive-consume case is
waiting on, so it is part of the **mutation enforcement** prerequisite the immortality residual already
names, not a hole this refusal was meant to cover. The reference compiler does not refuse the call form
either — it shares and then leaks (exit 101) — so shv2 is no looser here, only not yet stricter.
```maxon
let A = [1, 2]

function main() returns ExitCode
	var b = A
	b.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:6: Unsupported: binding a `let`-declared top-level global to a `var` — an aggregate has no owning COPY in shv2, so the binding would alias the SAME record and a write through it would mutate a global declared immutable; read it through a `let` binding, or declare the global `var`
```

<!-- test: error.top-level-let-array-var-reassign-alias -->
The same refusal through the other door — a REASSIGNMENT of an already-owned `var`, which promotes a
borrowed value through the identical single-sourced path.
```maxon
let A = [1, 2]

function main() returns ExitCode
	var b = [0]
	b = A
	b.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:2: Unsupported: binding a `let`-declared top-level global to a `var` — an aggregate has no owning COPY in shv2, so the binding would alias the SAME record and a write through it would mutate a global declared immutable; read it through a `let` binding, or declare the global `var`
```

<!-- test: error.top-level-let-array-ternary-alias -->
And through a value MERGE, which is the door that costs one extra keyword. The mark that refuses the two
above rides the VALUE, and a merge mints a NEW value — so the phi has to inherit it, or `var pick = A if c
else M` launders exactly what `var pick = A` is refused for. Measured before the phi inherited it: this
program compiled and returned 3.
```maxon
let A = [1, 2]
var M = [5, 5]

function main() returns ExitCode
	var pick = A if M.count() == 2 else M
	pick.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:6: Unsupported: binding a `let`-declared top-level global to a `var` — an aggregate has no owning COPY in shv2, so the binding would alias the SAME record and a write through it would mutate a global declared immutable; read it through a `let` binding, or declare the global `var`
```

<!-- test: error.top-level-let-array-match-arm-alias -->
The same merge, through the `match`-expression door rather than the ternary — one `finalizeMatchMerge`
serves both, so a phi that inherits the mark for one inherits it for the other by construction.
```maxon
let A = [1, 2]
var M = [5, 5]

enum Which
	fixed
	live
end 'Which'

function main() returns ExitCode
	let w = Which.fixed
	var pick = match w 'w'
		fixed gives A
		live gives M
	end 'w'
	pick.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:6: Unsupported: binding a `let`-declared top-level global to a `var` — an aggregate has no owning COPY in shv2, so the binding would alias the SAME record and a write through it would mutate a global declared immutable; read it through a `let` binding, or declare the global `var`
```

<!-- test: top-level-array-merge-of-mutable-globals-shares -->
The merge mark is narrow to the same thing the direct one is: a merge of two MUTABLE globals still binds
and still SHARES, so the phi rule refuses laundering rather than refusing merges. `P` grows, `Q` does not.
```maxon
var P = [1, 2]
var Q = [5, 5]

function main() returns ExitCode
	var pick = P if Q.count() == 2 else Q
	pick.push(9)
	return P.count() + Q.count()
end 'main'
```
```exitcode
5
```

<!-- test: top-level-let-array-let-alias -->
A `let` binding of a `let` array is fine and stays fine: it can only read, so there is nothing to
launder. This is the remedy the refusal above names, so it has to work. A `let` binding of a MERGE that
includes one is legal for the same reason — it can only read.
```maxon
let A = [1, 2]

function main() returns ExitCode
	let b = A
	return b.count()
end 'main'
```
```exitcode
2
```

<!-- test: top-level-var-array-var-alias-shares -->
Off a MUTABLE global the same binding is legal and SHARES the record — reference semantics, and measured
to be what the reference compiler does (it returns 3 here too). The refusal above keys on the source's
immutability, not on aliasing itself.
```maxon
var A = [1, 2]

function main() returns ExitCode
	var b = A
	b.push(9)
	return A.count()
end 'main'
```
```exitcode
3
```

<!-- test: error.top-level-array-empty -->
An empty array literal has no element to infer a type from, so it is refused — and the advice is the
same one a function body's `[]` gets, because a top-level `<Alias>.create()` now names the element type
the brackets could not. (It did not when this case was written: the sentence used to end *"a `.create()`
call is not a constant"*, which the container-factory initializer made false.)
```maxon
var items = []

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:13: Unsupported: an empty array literal `[]` as a top-level initializer — its element type cannot be inferred; name it with a generic alias and its factory instead (`typealias Ints = Array with Integer` + `var xs = Ints.create()`)
```

<!-- test: error.top-level-array-mixed-elements -->
Every element must have the first element's type — the same rule, in the same words, a literal in a
function body gets.
```maxon
var xs = [1, "a"]

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:14: Unsupported: an array literal with mixed element types — every element must have the same type as the first
```

<!-- test: error.top-level-array-bool-element -->
A bool element folds perfectly well and is still refused: stored, it would build an `Array with Integer`
of 1s and 0s, which is not the type the program wrote. A literal in a function body refuses a bool
element too, so the top level accepts no more than a body does.
```maxon
var flags = [true, false]

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:14: Unsupported: a top-level array literal element of type 'bool' — a constant array's elements are integers or String literals
```

<!-- test: top-level-var-string-literal -->
A top-level `var` string is valid and reassignable — it materializes once at startup, like an
array-literal global.
```maxon
var greeting = "hello"

function main() returns ExitCode
	print("{greeting} ")
	greeting = "world"
	print(greeting)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world
```

<!-- test: top-level-var-string-mutate-cross-function -->
A `var` string global mutated in place across a function boundary must NOT be shared as an
immortal static record: it mutates correctly and frees cleanly (no leaked copy-on-write buffer).
```maxon
var msg = "hi"

function bump()
	msg.append("!")
end 'bump'

function main() returns ExitCode
	bump()
	bump()
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi!!
```

<!-- test: top-level-let-string-literal -->
```maxon
let name = "Ada"

function main() returns ExitCode
	print(name)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Ada
```

### A managed constant is the WEAKEST claim on `<base>.<member>(…)`

A top-level managed `let` is the first method RECEIVER whose name can also be a declared TYPE, so
`Widget.byteLength()` is ambiguous in a way `c.increment()` never was. The type-based reading is tried
FIRST and the constant is its fallback — measured against the reference compiler, which answers 42 here
and 10 in the next case.

<!-- test: top-level-let-name-shadowed-by-type -->
```maxon
typealias Integer = int(i64.min to i64.max)

let Widget = "abcdefghij"

type Widget
	export var v as Integer

	export static function byteLength() returns Integer
		return 42
	end 'byteLength'
end 'Widget'

function main() returns ExitCode
	return Widget.byteLength() as ExitCode
end 'main'
```
```exitcode
42
```

When the type declares no static of that name there is no static call to make, and the same spelling
reads as the CONSTANT — the reference compiler's answer, and the reason the rule is a fallback rather
than "a type name always wins".

<!-- test: top-level-let-falls-back-when-type-has-no-such-static -->
```maxon
typealias Integer = int(i64.min to i64.max)

let Widget = "abcdefghij"

type Widget
	export var v as Integer

	export static function create() returns Widget
		return Self{v: 7}
	end 'create'
end 'Widget'

function main() returns ExitCode
	return Widget.byteLength() as ExitCode
end 'main'
```
```exitcode
10
```

A union CONSTRUCT (`Move.walk(5)`) has the same token shape, and an `Array`-instance alias's static
(`IntArray.create()`) is claimed by the array runtime rather than by any declared callee — both keep
their reading when a managed constant happens to share the name.

<!-- test: top-level-let-name-shadowed-by-union -->
```maxon
typealias Integer = int(i64.min to i64.max)

let Move = "shadow"

union Move
	walk(steps Integer)
	stop
end 'Move'

function main() returns ExitCode
	let m = Move.walk(5)
	let n = match m 'k'
		walk(s) gives s
		stop gives 0
	end 'k'
	return n as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: top-level-let-name-shadowed-by-array-alias -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

let IntArray = "shadow"

function main() returns ExitCode
	var a = IntArray.create()
	a.push(1)
	return a.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: data-section-bool-1byte -->
A single bool global occupies 1 byte in the .data section.

```maxon
var flag = true

function main() returns ExitCode
	if flag 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i8 1
```

<!-- test: data-section-i64-8byte -->
A single i64 global occupies 8 bytes in the .data section.

```maxon
var counter = 42

function main() returns ExitCode
	return counter - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
```

<!-- test: data-section-f64-8byte -->
A single f64 global occupies 8 bytes in the .data section.

```maxon
var pi = 3.14

function main() returns ExitCode
	if pi > 3.0 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
f64 3.14
```

<!-- test: data-section-f64-8byte-folded -->
A FOLDED float initializer lays down the same 8 bytes as the literal: `3.0 + 0.14` produces `3.14`, the strongest proof the constant evaluator produced a NUMBER (folded with the host's f64) and not a summed bit pattern (oracle-verified byte-identical to the literal `3.14`).

```maxon
var pi = 3.0 + 0.14

function main() returns ExitCode
	if pi > 3.0 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
f64 3.14
```

<!-- test: data-section-bool-then-i64-sorted -->
A bool and i64 global: sorted largest-first, no padding needed.

```maxon
var flag = false
var counter = 42

function main() returns ExitCode
	if flag 'read'
		return 1
	end 'read'
	return counter - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i8 0
```

<!-- test: data-section-bool-true-then-i64 -->
A true bool and i64: sorted largest-first, no padding needed.

```maxon
var flag = true
var counter = 99

function main() returns ExitCode
	if flag 'read'
		return counter - 99
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 99
i8 1
```

<!-- test: data-section-i64-then-bool -->
An i64 followed by a bool: no padding needed since bool has 1-byte alignment.

```maxon
var counter = 7
var flag = true

function main() returns ExitCode
	if flag 'read'
		return counter - 7
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 7
i8 1
```

<!-- test: data-section-multiple-bools -->
Multiple consecutive bools occupy 1 byte each with no padding.

```maxon
var a = true
var b = false
var c = true

function main() returns ExitCode
	if a and c and (b == false) 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i8 1
i8 0
i8 1
```

<!-- test: data-section-mixed-types -->
Mixed bool, i64, f64 globals sorted largest-first, no padding.

```maxon
var flag = true
var count = 10
var ratio = 2.5

function main() returns ExitCode
	if flag and (count == 10) and (ratio > 2.0) 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 10
f64 2.5
i8 1
```

<!-- test: top-level-var-byte-ranged-type -->
Module-level var with a byte-sized ranged type.
```maxon
typealias SmallInt = int(0 to u8.max)

var counter = 42 as SmallInt

function main() returns ExitCode
		return counter
end 'main'
```
```exitcode
42
```

<!-- test: top-level-let-scalar-reassign-error -->
Assigning to an immutable top-level `let` is E2013 — and the diagnostic names the `let` the
author actually wrote, rather than reporting a name that is defined two lines up as undefined.

That distinction is the whole reason the assignment path probes for a constant at all: a name
that is neither a local nor a top-level `var` would otherwise fall through to "undefined
variable", sending the reader hunting for a typo instead of at the `let` they meant to make a
`var`. The struct twin below pins the same arm but is blocked on P1.1 — a scalar `let` reaches
it today, so the property is pinned now rather than on structs' schedule.

```maxon
let origin = 5

function main() returns ExitCode
	origin = 6
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:5:2: cannot assign to immutable variable: 'origin'
```

<!-- test: top-level-let-struct-reassign-error -->
<!-- P1.1 structs — the `let` holds a `Point.create(...)`, a runtime initializer -->
Reassigning an immutable top-level `let` struct variable should error.
```maxon
typealias SmallInt = int(0 to u8.max)

type Point
		export var x as SmallInt
		export var y as SmallInt

		static function create(x SmallInt, y SmallInt) returns Self
			return Self{x: x, y: y}
		end 'create'
end 'Point'

let origin = Point.create(0, y: 0)

function main() returns ExitCode
		origin = Point.create(1, y: 1)
		return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:16:3: cannot assign to immutable variable: 'origin'
```

<!-- test: top-level-var-function-call-error -->
Function calls are not allowed in module-level `var` initializers.
```maxon
typealias Integer = int(i64.min to i64.max)

function getDefault() returns Integer
	return 42
end 'getDefault'

var value = getDefault()

function main() returns ExitCode
	return value
end 'main'
```
```maxoncstderr
error E2045: <fragment>:8:13: Function calls are not allowed in global variable initializers; 'getDefault()' is not a constant expression
```

<!-- test: top-level-let-duplicate-declaration-error -->
Declaring the same top-level `let` name twice is a duplicate definition (E3006), positioned at the
LATER declaration — the top-level twin of the duplicate-function check. `recordDecl` is first-wins, so
the first declaration keeps the name and the diagnostic names the redeclaration to remove. Both
compilers reject a duplicate FUNCTION this way; the bootstrap silently first-wins a duplicate top-level
`let`, so shv2 is deliberately stricter here (OPEN.md #4b).

```maxon
let A = 1
let A = 2

function main() returns ExitCode
	return A
end 'main'
```
```maxoncstderr
error E3006: <fragment>:3:5: duplicate definition of 'A'
```

<!-- test: top-level-var-let-duplicate-declaration-error -->
The storage key is kind-independent, so a `var` and a `let` sharing one name in one file collide the
same way — the second declaration is the duplicate.

```maxon
var counter = 0
let counter = 5

function main() returns ExitCode
	return counter
end 'main'
```
```maxoncstderr
error E3006: <fragment>:3:5: duplicate definition of 'counter'
```

<!-- test: error.top-level-let-array-owned-merge-alias -->
The FOURTH door onto the same guard, and the one S5 opened. The three above all reach it as a `var`
BINDING; this one reaches it as a MERGE that must be OWNED because its other arm gives a fresh record —
so the borrowed arm is promoted, and for an aggregate a promotion is an INCREF of the same box. That is
precisely the launder: an incref mints an unmarked SSA name for the marked record, so `pick.push` would
grow `A` with every other guard intact. Refused inside the promotion, at the door that names itself, so
the guard covers a door the day the door opens rather than the day someone notices.
```maxon
typealias Names = Array with String

let A = ["ab", "cde"]

function fresh() returns Names
	var n = Names.create()
	n.push("zz")
	return n
end 'fresh'

function main() returns ExitCode
	let c = true
	var pick = A if c else fresh()
	pick.push("qq")
	return A.count()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:14:15: Unsupported: merging a read of a `let`-declared top-level global into an OWNED result — an aggregate has no owning COPY in shv2, so the merged value would alias the SAME record and a write through it would mutate a global declared immutable; read it through a `let` binding, or declare the global `var`
```
