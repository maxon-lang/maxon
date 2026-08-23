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
error E3005: <fragment>:8:27: cannot assign a value of type 'String' to field 'a' of 'Pair', which holds 'int'
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
error E3005: <fragment>:14:25: cannot assign a value of type 'int' to field 'c' of 'Paint', which holds 'Color'
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
<!-- ⛔ RE-MEASURED 2026-08-22. The reason given here until now — "the CONSTANT EVALUATOR's missing enum-member arm" — is STALE: that arm LANDED, and a payload-free enum case is a top-level constant today (see `a-payload-free-enum-case-is-a-top-level-constant`). The blocker is now one step further on and is a different mechanism: a global whose folded value is an enum case is typed `named(<Enum>)`, and `ProgramSignatures.constantValueTypeOf` interns that name into the WHOLE-PROGRAM table while the reader of the answer is a FILE's parse. So the id is meaningless where it is read, and the slot-width door panics on it: `slotStorageType: a slot's named type has an id that is not in the interner it was asked against`. Its own header calls itself "the twin of `Parser.constantValueTypeOf` on the other interner" — `resolvedGlobalOf` hands its result to a file and calls the whole-program twin. This is the same ADOPTION trap that arm's neighbours already warn about, and it is a defect of the enum-case-constant slice rather than of anything here; it needs its own rung and its own control. (Before that panic existed the same programs died one pass later in `maxonTypeToStdType: a `named` type must be resolved to a primitive before lowering` — so this case has never passed, and nothing regressed it.) -->
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
<!-- ⛔ RE-MEASURED 2026-08-22. The reason given here until now — "the CONSTANT EVALUATOR's missing enum-member arm" — is STALE: that arm LANDED, and a payload-free enum case is a top-level constant today (see `a-payload-free-enum-case-is-a-top-level-constant`). The blocker is now one step further on and is a different mechanism: a global whose folded value is an enum case is typed `named(<Enum>)`, and `ProgramSignatures.constantValueTypeOf` interns that name into the WHOLE-PROGRAM table while the reader of the answer is a FILE's parse. So the id is meaningless where it is read, and the slot-width door panics on it: `slotStorageType: a slot's named type has an id that is not in the interner it was asked against`. Its own header calls itself "the twin of `Parser.constantValueTypeOf` on the other interner" — `resolvedGlobalOf` hands its result to a file and calls the whole-program twin. This is the same ADOPTION trap that arm's neighbours already warn about, and it is a defect of the enum-case-constant slice rather than of anything here; it needs its own rung and its own control. (Before that panic existed the same programs died one pass later in `maxonTypeToStdType: a `named` type must be resolved to a primitive before lowering` — so this case has never passed, and nothing regressed it.) -->
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

⚠ **THE REFUSAL IS ON THE WRITE, NOT ON THE BINDING** (⚖ user, 2026-08-14, W117). `var b = A` is legal —
reading a `let` global and naming the result is what the language means by reading it — and `b.push(9)`
is the error. The mark rides the VALUE and is CARRIED through the promotion rather than refused at it, so
it reaches every use in this function — the direct binding, a reassignment, a ternary/`match` merge that
joins it, an `if`/loop merge that REBINDS it, a cell it is spilled into and a closure that captures it,
each pinned below — and, through a whole-program fact, every caller of a function that returns one.

⚠ **WHAT IS STILL OUTSIDE IT IS ONE BOUNDARY WITH SEVERAL SPELLINGS: A PARAMETER.** The mark is a fact
about a VALUE in ONE function's SSA space; a callee's parameter is a fresh value in a fresh space, and
whether it may alias an immutable global is a property of every CALL SITE rather than of the callee. So
the taint neither enters a callee nor comes back out of one, and all of these are accepted today:

  • the callee ALIASES the parameter before writing — `g(A)` where `g(xs …)` does `var b = xs` then
    `b.push(9)` (measured: `A` grows). The argument check reads the callee's summary of what IT writes,
    and the callee writes a LOCAL that happens to carry the same record;
  • the callee RETURNS the parameter — `var c = idBox(a)` where `idBox(x Box)` is `return x`. The sweep
    sees the `return x`, recognises `x` as this body's own binder and correctly declines to seed on it,
    so `c` comes back unmarked (measured: the caller's `c.n = 99` reaches the global);
  • the callee STORES the parameter into a record it returns — `Holder.of(a)` then `h.inner.n = 99`.

⚠ **NONE OF THE THREE IS NEW, and that is checkable rather than asserted: none of them needs an accessor
or any other W117 machinery — `g(A)` and `idBox(G)` are written against a bare `let` global and behave
identically on the merge base.** They are named together here so a later rung scopes the cure to the
BOUNDARY rather than to whichever spelling it happened to be shown. Closing it needs the taint to flow
into (and back out of) a callee's SSA space, which is a per-call-site fact, not a per-function one.
```maxon
let A = [1, 2]

function main() returns ExitCode
	var b = A
	b.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E3019: <fragment>:6:4: cannot pass 'b' to function that mutates parameter 'self' (in main)
```

<!-- test: error.top-level-let-array-var-reassign-alias -->
The same refusal through the other door — a REASSIGNMENT of an already-owned `var`, which promotes a
borrowed value through the identical single-sourced path and so CARRIES the mark through the identical
one. `b = A` is legal; the push is not.
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
error E3019: <fragment>:7:4: cannot pass 'b' to function that mutates parameter 'self' (in main)
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
error E3019: <fragment>:7:7: cannot pass 'pick' to function that mutates parameter 'self' (in main)
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
error E3019: <fragment>:16:7: cannot pass 'pick' to function that mutates parameter 'self' (in main)
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

<!-- test: top-level-let-struct-accessor-read-only -->
⭐ **READING AND RETURNING A `let` GLOBAL IS LEGAL; MUTATING THROUGH THE RESULT IS THE ERROR** (⚖ user,
2026-08-14). This is the control the whole family was missing: an accessor that hands a `let`-declared
global's record back to a caller that only READS it compiles and runs. Until W117 shv2 refused the
RETURN itself, which made the shape unwritable — and it is the shape `stdlib/CharacterSet.maxon`'s
eleven presets are declared in, so the refusal was the sole thing between the library and its callers.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static let shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	export static function get() returns Box
		return Box.shared
	end 'get'
end 'Box'

function main() returns ExitCode
	let a = Box.get()
	return a.n as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: top-level-var-struct-accessor-write-shares -->
The same accessor off a `var` global, which is the measurement that says the rule keys on IMMUTABILITY
and not on returning a global at all: the write goes through and is observable on the global. Both
compilers do this, and shv2 did it before W117 too — it is the one keyword's difference that proves the
refusal below is guarding the `let` claim rather than the aliasing.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static var shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	export static function get() returns Box
		return Box.shared
	end 'get'
end 'Box'

function main() returns ExitCode
	var a = Box.get()
	a.n = 99
	return Box.shared.n as ExitCode
end 'main'
```
```exitcode
99
```

<!-- test: error.let-global-accessor-result-field-write -->
And the write is where it is refused. `Box.get()` may hand the record out; writing a field through what
it handed back would mutate a global declared `let`, so THAT is the diagnostic, on the write's own line.
The caller learns this from a whole-program fact — "may this function's return alias an immutable
global?" — because the accessor's body may be in another file, which is exactly why the refusal could
not stay at the return.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static let shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	export static function get() returns Box
		return Box.shared
	end 'get'
end 'Box'

function main() returns ExitCode
	var a = Box.get()
	a.n = 99
	return Box.shared.n as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:20:2: Unsupported: writing through 'a', which aliases a `let`-declared top-level global — an aggregate has no owning COPY in shv2, so the write would reach the global's own record and mutate a global declared immutable; read through it instead, or declare the global `var`
```

<!-- test: error.let-global-accessor-result-two-hops-of-return -->
⭐ **THE FACT IS A CLOSURE OVER RETURN-FORWARDING EDGES, NOT A ONE-HOP RULE, AND THIS IS THE CASE THAT
SAYS SO.** `get()` returns what `inner()` returns, and only `inner()` names the global — so a rule that
looked one call deep would accept this program and silently mutate `Box.shared`. Every shape the corpus
writes today is single-hop, which is precisely why the two-hop case is pinned: *"nothing writes it yet"*
is not a soundness argument.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static let shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	static function inner() returns Box
		return Box.shared
	end 'inner'

	export static function get() returns Box
		return Box.inner()
	end 'get'
end 'Box'

function main() returns ExitCode
	var a = Box.get()
	a.n = 99
	return Box.shared.n as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:24:2: Unsupported: writing through 'a', which aliases a `let`-declared top-level global — an aggregate has no owning COPY in shv2, so the write would reach the global's own record and mutate a global declared immutable; read through it instead, or declare the global `var`
```

<!-- test: error.let-global-passed-to-field-writing-callee -->
⛔ **A `let`-DECLARED GLOBAL HANDED STRAIGHT TO A CALLEE THAT WRITES A FIELD OF IT — MEASURED SILENTLY
MUTATING THE GLOBAL BEFORE W117, ON BOTH COMPILERS** (`g 99`, where the program declares `G` a `let`).
Neither of the two masks that existed could see it: E3019's deliberately records no field write, and
E3070's is scoped to ARRAY fields. The third column is this program.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	export static function make() returns Box
		return Box{n: 1}
	end 'make'
end 'Box'

let G = Box.make()

function bump(b Box)
	b.n = 99
end 'bump'

function main() returns ExitCode
	bump(G)
	return G.n as ExitCode
end 'main'
```
```maxoncstderr
error E3019: <fragment>:19:2: cannot pass 'G' to function that mutates parameter 'b' (in main)
```

<!-- test: error.let-global-accessor-result-to-field-writing-callee -->
The same write one call away from a RETURNED alias — the shape that forces both halves at once: the
accessor's whole-program return fact, and the callee's whole-program parameter-write fact. `bump` writes
nothing of its own; it writes what it was handed.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static let shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	export static function get() returns Box
		return Box.shared
	end 'get'
end 'Box'

function bump(b Box)
	b.n = 99
end 'bump'

function main() returns ExitCode
	var a = Box.get()
	bump(a)
	return Box.shared.n as ExitCode
end 'main'
```
```maxoncstderr
error E3019: <fragment>:24:2: cannot pass 'a' to function that mutates parameter 'b' (in main)
```

<!-- test: error.let-global-to-callee-that-forwards-the-write -->
⭐ **AND THE PARAMETER HALF IS A CLOSURE TOO.** `bump` writes nothing at all — it forwards its parameter
to a `poke` that does — so the bit reaches `bump` only through the least fixpoint over the call graph.
Two hops on the argument side, as the case above is two hops on the return side.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	export static function make() returns Box
		return Box{n: 1}
	end 'make'
end 'Box'

let G = Box.make()

function poke(c Box)
	c.n = 99
end 'poke'

function bump(b Box)
	poke(b)
end 'bump'

function main() returns ExitCode
	bump(G)
	return G.n as ExitCode
end 'main'
```
```maxoncstderr
error E3019: <fragment>:23:2: cannot pass 'G' to function that mutates parameter 'b' (in main)
```

<!-- test: error.let-global-given-out-of-a-closure -->
⭐ **THE INDIRECT-CALL BOUNDARY, STATED RATHER THAN LEFT SILENT.** A closure has no static callee, so no
whole-program fact can say what calling one returns — and without that, a tainted value handed out of a
closure would escape into a call the analysis cannot follow. So a closure keeps the OLD refusal, at the
`gives`, which is the conservative answer this rung deliberately does not widen. The remedy is the same
one the message always named.

⚠ **HONEST STATUS: this case is a REGRESSION GUARD, not a red W117 turned green.** The identical refusal
fired here before the rung, because before it every such return was refused; what the case pins is that
widening the RETURN did not widen this. It goes red the day a later rung publishes a fact for a closure
without the analysis to back it. Its positive twin is `top-level-let-struct-accessor-read-only` above —
the same shape, out of a NAMED function, which now compiles.
```maxon
let A = [1, 2]

function main() returns ExitCode
	let f = function() gives A
	var b = f()
	b.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:21: Unsupported: returning a read of a `let`-declared top-level global — an aggregate has no owning COPY in shv2, so the returned value would alias the SAME record and a write through it would mutate a global declared immutable; read it through a `let` binding, or declare the global `var`
```

<!-- test: error.let-global-alias-in-a-cell-keeps-its-mark -->
⛔⛔ **A CELL IS A THIRD PLACE THE RECORD LIVES, AND IT USED TO DROP THE MARK — MEASURED SILENTLY MUTATING
THE GLOBAL (W117 review, exit 8).** `var b` becomes CELL-RESIDENT the moment anything captures it, and a
cell binding's `boundValue` is the CELL, not the value the promotion marked — so the write doors, which
ask about `boundValue`, saw an unmarked binding and let `grow(b)` through **in the very frame that owns
`b`**. The closure below does nothing but exist: delete it and the identical program is refused, which is
what makes this a defect of the CELL and not of the capture.

⇒ The mark is now carried onto the cell at the store and back off it at the load, so where a binding's
value lives cannot change what may be done through it.
```maxon
typealias Names = Array with String
typealias IntThunk = function() returns int

let A = ["a", "b"]

function expose() returns Names
	return A
end 'expose'

function size(xs Names) returns int
	return xs.count()
end 'size'

function grow(xs Names) returns int
	xs.push("zz")
	return xs.count()
end 'grow'

function callThunk(f IntThunk) returns int
	return f()
end 'callThunk'

function main() returns ExitCode
	var b = expose()
	b = expose()
	let k = callThunk(function() gives size(b))
	let n = grow(b)
	return (A.count() + k + n) as ExitCode
end 'main'
```
```maxoncstderr
error E3019: <fragment>:28:10: cannot pass 'b' to function that mutates parameter 'xs' (in main)
```

<!-- test: error.let-global-alias-captured-by-a-closure -->
⛔⛔ **AND THE CAPTURE ITSELF IS THE OTHER HALF: A CLOSURE MUST NOT LAUNDER THE TAINT** (W117 review;
MEASURED mutating the global and exiting 3 before the fix). A closure body is its own SSA space, so the
per-value marks are swapped out at its boundary — correctly, since an id means nothing there — but a
CAPTURED binding names the very same record, and dropping the marks made the capture read arrive clean.
The enclosing frame's marks therefore ride across the boundary and the capture re-mints one in the
closure's own space.

⚠ **THE BLAME IS THE NOUN, not `b`.** A captured name publishes no bare-binding blame, and the value is
what knows; the sentence is the one `bump(Box.get())` already uses. The FRAME named is the lifted closure,
which is where the call is.
```maxon
typealias Names = Array with String
typealias IntThunk = function() returns int

let A = ["a", "b"]

function expose() returns Names
	return A
end 'expose'

function grow(xs Names) returns int
	xs.push("zz")
	return xs.count()
end 'grow'

function callThunk(f IntThunk) returns int
	return f()
end 'callThunk'

function main() returns ExitCode
	var b = expose()
	b = expose()
	let n = callThunk(function() gives grow(b))
	return A.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3019: <fragment>:23:37: cannot pass 'a read of a `let`-declared global' to function that mutates parameter 'xs' (in main$closure_0)
```

<!-- test: error.let-global-alias-across-an-if-merge -->
⛔ **A MERGE THAT REBINDS A NAME MUST CARRY THE MARK, AND ONLY THE MERGES THAT PRODUCE A VALUE DID**
(W117 review). The ternary and `match … gives` merges were covered from the start
(`propagateImmutableGlobalReadToPhi`, and `error.top-level-let-array-owned-merge-alias` above pins one);
an `if` that REBINDS a carried `var` mints its phi through a different door and inherited nothing, so
`b` came out of the merge clean and `grow(b)` was accepted.
```maxon
typealias Names = Array with String

let A = ["a", "b"]

function expose() returns Names
	return A
end 'expose'

function grow(xs Names) returns int
	xs.push("zz")
	return xs.count()
end 'grow'

function main() returns ExitCode
	var b = ["q"]
	if A.count() > 1 'sometimes'
		b = expose()
	end 'sometimes'
	let n = grow(b)
	return (A.count() + n) as ExitCode
end 'main'
```
```maxoncstderr
error E3019: <fragment>:20:10: cannot pass 'b' to function that mutates parameter 'xs' (in main)
```

<!-- test: error.let-global-alias-carried-by-a-loop -->
⛔ **AND A LOOP IS THE SAME MERGE WITH THE EDGE PUSHED BEFORE THE PHI IS NAMED — MEASURED MUTATING THE
GLOBAL (W117 review, exit 6).** On a back edge the binding's own value IS the pushed value, so the header
phi — which is what every read of `b` after the loop resolves to — is a THIRD name for the record and got
the mark from neither end. It is now carried onto the loop's phi explicitly, at the one place the loop
names it.

⚠ The condition is runtime and the body may run zero times; the refusal is deliberately independent of
that, because "may alias" is the whole question.
```maxon
typealias Names = Array with String

let A = ["a", "b"]

function expose() returns Names
	return A
end 'expose'

function grow(xs Names) returns int
	xs.push("zz")
	return xs.count()
end 'grow'

function main() returns ExitCode
	var b = ["q"]
	for _ in 0 upto 2 'each'
		b = expose()
	end 'each'
	let n = grow(b)
	return (A.count() + n) as ExitCode
end 'main'
```
```maxoncstderr
error E3019: <fragment>:20:10: cannot pass 'b' to function that mutates parameter 'xs' (in main)
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
precisely the launder an unmarked incref would perform: the mark is therefore CARRIED across the
promotion (W117) rather than the promotion being refused, so the phi that joins the two arms carries it
too and `pick.push` is refused where it happens.
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
error E3019: <fragment>:15:7: cannot pass 'pick' to function that mutates parameter 'self' (in main)
```

### W117's returned-shape sweep — the INLINE CONDITIONAL

⭐⭐ **A `return` WHOSE VALUE IS AN INLINE CONDITIONAL HAS TWO ARMS, AND EITHER MAY BE A GLOBAL'S RECORD.**
`noteReturnedGlobalOrForward` recognises a deliberately narrow set of `return` shapes, and its own tail
states the family rule: an unrecognised shape leaves the function's bit clear, and a clear bit keeps the
E2015 refusal at the return — *"a missed shape costs a REFUSAL, never a silent acceptance"*.

⛔ **THE MISS WAS REACHED, BY SHV2'S OWN SOURCE.** `ConformanceCheck.pairableAssociatedTypeNames` is
`return sharedEmptyDeclaredConformances if boundTypeNames.count() < associatedTypeNames.count() else
associatedTypeNames` — a legal program the bootstrap compiles, refused because the sweep read the shape
and recorded nothing.

⚠ **BOTH ARMS ARE SEEDED, NEVER ONE.** The shape says the value is one of two records and the sweep
cannot know which; recording only the half it happened to read first would be a fact about the parser
rather than about the program. A nested conditional — in the CONDITION, where its `else` sits behind a
paren, or in the ELSE arm, where the arm is not a bare name — still records nothing for that arm, which
is the family rule again: the THEN arm is seeded either way, so a missed else arm cannot make the bit
WRONG, only incomplete.

<!-- test: a-returned-inline-conditional-may-hand-back-a-global -->
The `pairableAssociatedTypeNames` shape, and both arms are exercised — one call takes the global, the
other takes the parameter.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Names = Array with Integer

let sharedEmptyNames = Names.create()

function pairable(bound Names, wanted Names) returns Names
	return sharedEmptyNames if bound.count() < wanted.count() else wanted
end 'pairable'

function main() returns ExitCode
	var a = Names.create()
	a.push(1)
	var b = Names.create()
	b.push(2)
	b.push(3)
	let short = pairable(a, wanted: b)
	let long = pairable(b, wanted: a)
	print("short={short.count()} long={long.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
short=0 long=1
```

### W117's shadowing filter — a binder shadows only what comes AFTER it

⛔⛔ **THE FILTER WAS POSITION-BLIND, AND ITS OWN HEADER CALLED THAT ACCEPTABLE.**
`returnedNamesNotBoundHere` drops a returned name that the body binds itself, using a deliberately
GENEROUS binder set — a name is bound when it follows `let`, `var`, `for`, `(` or `,`, which also
catches plain call ARGUMENTS. The header read *"that over-inclusion drops a seed, and a dropped seed
falls back to the E2015 refusal at the return: the answer the compiler gave before this rung existed"*.
That is true, and it stops being acceptable the moment a real program is the one refused.

⭐ **IT WAS REACHED BY SHV2'S OWN SOURCE.** `SignatureIndex.surfaceOfRootBits` returns
`sharedEmptyDeclaredSurface` BARE on one path — a shape the sweep DOES recognise — and hands that same
name to a call on the next line. The argument marked the candidate bound, the seed was dropped, and the
bare return was refused on source the bootstrap compiles.

⭐⭐ **THE CURE IS POSITION, NOT SHAPE, AND THE SET IS UNCHANGED.** Narrowing the binder SET is what the
header warns against and it is right to: a `match` payload `case(a, b)` is character for character a
call, and no token test separates them. Position needs no such test and is EXACT for the question being
asked — **a name cannot resolve to a local at a `return` that precedes the local's own declaration,
because such a program does not compile at all.** So requiring the binder to come FIRST can only drop
FALSE shadowings; it cannot miss a real one.

<!-- test: a-returned-global-is-not-shadowed-by-a-later-call-argument -->
The `surfaceOfRootBits` shape: the global is returned bare on one path and passed as an argument on the
next line. Both paths are exercised.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Bits = Array with Integer

let sharedEmptyBits = Bits.create()

function withRootBits(base Bits, bits Integer) returns Bits
	var out = Bits.create()
	out.push(base.count() + bits)
	return out
end 'withRootBits'

function surfaceOfRootBits(bits Integer) returns Bits
	if bits == 0 'namesNothing'
		return sharedEmptyBits
	end 'namesNothing'

	return withRootBits(sharedEmptyBits, bits: bits)
end 'surfaceOfRootBits'

function main() returns ExitCode
	let empty = surfaceOfRootBits(0)
	let one = surfaceOfRootBits(5)
	print("empty={empty.count()} one={one.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
empty=0 one=1
```

<!-- test: a-returned-name-IS-shadowed-by-a-binder-above-it -->
⭐ **THE CONTROL, AND IT IS THE HALF THAT MUST NOT BREAK.** The filter's whole job is to drop a name the
body binds itself, and this program binds one ABOVE the return — so the local wins, the seed is dropped,
and the value handed back is the LOCAL's and not the global's.
**It WRITES through the returned value, and that is what makes it discriminating**: a spurious seed
would mark `localWins` as handing back a global's record, and the `push` would then be refused on a
program that touches no global at all. A read-only version of this case passes either way and would
have proved nothing.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Bits = Array with Integer

let bits = Bits.create()

function localWins() returns Bits
	var bits = Bits.create()
	bits.push(7)
	bits.push(9)
	return bits
end 'localWins'

function main() returns ExitCode
	var got = localWins()
	got.push(11)
	print("global={bits.count()} local={got.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
global=0 local=3
```

### W117's returned-shape sweep — A PARAMETER DEFAULT IS THE LANGUAGE'S OTHER `return`

⭐⭐ **A DEFAULT VALUE IS A SYNTHESIZED NULLARY FUNCTION WHOSE ENTIRE BODY IS `return <the expression>`**
(`Parser.parseDefaultHelperBody`), so `f(p T = A)` hands its caller exactly what `f(A)` hands it. Until
this rung the sweep read `return` STATEMENTS only, and the two spellings therefore had different answers
to the one question W117 exists to answer — *may this argument alias a `let`-declared global's record?* —
which is the one thing a default may never differ from the written-out argument in. Both halves were
measured on the tip, and they failed in OPPOSITE directions:

⛔ **THE BARE NAME WAS REFUSED, ON SHV2'S OWN SOURCE.** `ParseStaging.maxon:129` declares `opaqueParams
OpaqueParamInfo = sharedNoOpaqueParams` and `Project.maxon:682` declares `payloadStorage MaxonType =
NoPayloadStorage`; both are E2015 at the helper's return, because the sweep published no bit for a
function it never read. The bootstrap compiles and runs both.

⛔⛔ **AND THE CALL EDGE WAS SILENTLY ACCEPTED, WHICH IS THE HALF A REFUSAL DOES NOT COVER.** A default
that names the global THROUGH AN ACCESSOR (`= shared()`) reaches the helper's return as an already-OWNED
call result, so it never touches the promotion and there was no refusal to fall back on — the mark simply
stopped at the helper and the caller got an untainted value. MEASURED: a `grow(names Names = shared())`
whose body pushes printed `a=1 b=2 g=2`, mutating a `let` global, while the written-out `grow(shared())`
one line over is E3019. So this door had to be opened for SOUNDNESS and not only to lift a refusal.

<!-- test: a-parameter-default-may-name-a-let-global -->
The `sharedNoOpaqueParams` shape, and all three spellings of the same argument are exercised side by
side — omitted, written out, and a value of the caller's own — because "the default agrees with the
written-out argument" is the property, not "the default compiles".
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Names = Array with Integer

let sharedEmptyNames = Names.create()

function describe(names Names = sharedEmptyNames) returns Integer
	return names.count()
end 'describe'

function main() returns ExitCode
	var given = Names.create()
	given.push(7)
	given.push(9)
	print("omitted={describe()} written={describe(sharedEmptyNames)} given={describe(given)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
omitted=0 written=0 given=2
```

<!-- test: a-parameter-default-may-call-an-accessor-that-returns-a-global -->
The transitive half: the default is a CALL, and the mark reaches the helper through the sweep's ordinary
forwarding edge rather than through a seed of its own.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Names = Array with Integer

let sharedEmptyNames = Names.create()

function shared() returns Names
	return sharedEmptyNames
end 'shared'

function describe(names Names = shared()) returns Integer
	return names.count()
end 'describe'

function main() returns ExitCode
	print("omitted={describe()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
omitted=0
```

<!-- test: a-defaulted-global-may-not-reach-a-mutating-parameter -->
⭐ **THE CONTROL FOR THE NAME, AND IT IS THE HALF THAT MUST NOT BREAK.** Admitting the return is only
half of W117; the other half is that the refusal MOVED to the write rather than being dropped. The
callee pushes, so the omitted argument is refused at the CALL — the same E3019, from the same one
reporter, that the written-out `grow(sharedEmptyNames)` earns. **The blame is the shared noun and not a
binding name**, because a filled default names nothing the author wrote.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Names = Array with Integer

let sharedEmptyNames = Names.create()

function grow(names Names = sharedEmptyNames) returns Integer
	names.push(1)
	return names.count()
end 'grow'

function main() returns ExitCode
	print("a={grow()}")
	return 0
end 'main'
```
```maxoncstderr
error E3019: <fragment>:13:12: cannot pass 'a read of a `let`-declared global' to function that mutates parameter 'names' (in main)
```

<!-- test: a-defaulted-accessor-call-may-not-reach-a-mutating-parameter -->
⭐⭐ **THE CONTROL FOR THE EDGE — AND THIS ONE WAS A WRONG ANSWER, NOT A REFUSAL.** Identical to the case
above but for the default naming the global through `shared()`, which is the spelling that compiled clean
and mutated the global. It is refused now for the reason the written-out `grow(shared())` has always been.

⚠ **shv2 IS STRICTER THAN THE RUNNABLE ORACLE HERE, DELIBERATELY AND ALREADY.** The bootstrap catches
only a `let` handed over BY NAME, so it accepts both this program and the written-out `grow(shared())`;
shv2 refuses both, which is W117's whole stance — the subject is the RECORD, not the spelling that
reached it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Names = Array with Integer

let sharedEmptyNames = Names.create()

function shared() returns Names
	return sharedEmptyNames
end 'shared'

function grow(names Names = shared()) returns Integer
	names.push(1)
	return names.count()
end 'grow'

function main() returns ExitCode
	print("a={grow()}")
	return 0
end 'main'
```
```maxoncstderr
error E3019: <fragment>:17:12: cannot pass 'a read of a `let`-declared global' to function that mutates parameter 'names' (in main)
```

<!-- test: a-field-default-naming-a-let-global-is-still-refused -->
⛔⛔ **THE FIELD DEFAULT IS THE SAME HELPER MECHANISM AND IS DELIBERATELY *NOT* ADMITTED, WHICH IS A
DECISION AND NOT AN OVERSIGHT.** Both doors drain through one `parseDefaultHelperBody`, and what separates
them is the KEY it asks under: the sweep publishes a PARAMETER default's helper and not a FIELD's, so this
answers false and the E2015 stands.

⭐ **THE REASON IS WHERE THE RECORD LANDS.** A parameter default hands it to a call ARGUMENT, which is
where the mark is read and where a mutating callee is refused. A field default hands it to a struct SLOT,
and the mark does not survive a field store — MEASURED, `var h = Holder.make(sharedEmptyNames)` storing a
global's record and then `h.names.push(1)` compiles clean and prints `g=1`.

⚠ **THE ORACLE PRINTS `g=1` TOO, SO THIS IS A HOLE IN W117 AS A WHOLE AND NOT AN SHV2-ONLY DEFECT** — and
that is precisely why the door stays shut rather than being opened to match the bootstrap. W117 is
deliberately STRICTER than the oracle about a global's record reaching a mutating position (the case
above is one), so publishing the field helper would open a route into the one part of the rule that
cannot yet follow the record. A refusal is the direction this rule may err in; a silent acceptance is
not. It is pinned here so the gap is visible rather than assumed, and it comes off the day a field store
carries the mark.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Names = Array with Integer

let sharedEmptyNames = Names.create()

type Holder
	export var names as Names = sharedEmptyNames

	export static function make() returns Holder
		return Self{}
	end 'make'
end 'Holder'

function main() returns ExitCode
	print("n={Holder.make().names.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:30: Unsupported: returning a read of a `let`-declared top-level global — an aggregate has no owning COPY in shv2, so the returned value would alias the SAME record and a write through it would mutate a global declared immutable; read it through a `let` binding, or declare the global `var`
```

### `<Type> from "…"` AS A TOP-LEVEL INITIALIZER

⛔⛔ **THE CONST EVALUATOR HAD NO READING OF THIS SHAPE, AND ANSWERED ABOUT A CONSTANT NOBODY WROTE.**
shv2's own `Compiler/Project.maxon:407` is `export let CompilerOwnedDeclFilePath = FilePath from ""`.
Unrecognised, the scalar walk read the bare `FilePath` as a reference to another top-level `let` — its
only other reading of an identifier — and answered **E2004 `Undefined constant 'FilePath'`**. That is the
SAME misattribution the struct-literal and factory-call arms beside it were added for, a third time, and
it cost four errors: the declaration plus the three files that read the name it declares.

⭐ **IT IS RECORDED AS THE CALL IT ALREADY IS.** The body form calls the type's own `init` static, so
nothing new is evaluated: the walk states WHICH function to call and WHAT to pass it, and `__module_init`
builds the record before `main` exactly as it does for `Database.create(…)`.

⛔⛔ **AND THE CONFORMANCE HAD TO BE CHECKED HERE, WHICH THE FIRST CUT OF THIS ARM DID NOT DO.** The body
spelling's conformance is decided by `checkLiteralInitConformance`, which walks `project.literalInitSites`
— a store the REAL PARSE fills and the const evaluator cannot reach. So a top-level construction recorded
nothing and was checked by nobody. **MEASURED with the check absent: a `type Tag` with an `init` static
and NO `implements InitableFromStringLiteral` compiled and RAN**, where the bootstrap answers
`E3005 Type 'Tag' does not conform to InitableFromStringLiteral`. Accepting what the oracle refuses is the
costly direction. The arm now asks the SAME predicate through the SAME pair, reading the sweep's own
store — the substitution `sweptConformanceIndex`'s header exists for — and reports the same code with the
same sentence.

<!-- test: a-top-level-let-built-by-a-string-literal-init -->
Both a `""` and a non-empty literal, so the argument is carried and not merely accepted.
```maxon
interface InitableFromStringLiteral
end 'InitableFromStringLiteral'

type Tag implements InitableFromStringLiteral
	export let text as String

	export static function init(text String) returns Self
		return Self{text: text}
	end 'init'
end 'Tag'

let emptyTag = Tag from ""
let namedTag = Tag from "hello"

function main() returns ExitCode
	print("empty={emptyTag.text.byteLength()} named={namedTag.text}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
empty=0 named=hello
```

<!-- test: error.a-top-level-string-literal-init-still-needs-the-conformance -->
⭐ **THE CONTROL, AND IT IS THE HALF A FIRST CUT OF THIS ARM GOT WRONG.** The only difference from the
case above is the missing `implements` clause. Same code, same sentence, same anchor as the body spelling
and as the bootstrap.
```maxon
type Tag
	export let text as String

	export static function init(text String) returns Self
		return Self{text: text}
	end 'init'
end 'Tag'

let namedTag = Tag from "hello"

function main() returns ExitCode
	print("n={namedTag.text}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/static-variables/error.a-top-level-string-literal-init-still-needs-the-conformance.test:10:16: Type 'Tag' does not conform to InitableFromStringLiteral
```

### A PAYLOAD-FREE ENUM CASE IS A CONSTANT

⭐⭐ **ITS VALUE IS ITS TAG — a number fixed at declaration — so it folds like any other.** shv2 refused
it, and it cost six errors across its own backends: `let JumpTableBaseReg = X64Register.r10`
(`Targets/X64/X64Backend.maxon:1144`), `let GtSwitchFromReg = X64Register.rcx`, and the four register
tables written as array literals. The bootstrap compiles every one.

⚠ **THE NUMBER ALONE IS A WRONG ANSWER ABOUT IT, WHICH IS THE WHOLE OF WHY THIS IS NOT A ONE-LINE FOLD.**
MEASURED: a constant folded to a bare `integer` cannot be passed where the enum is declared —
`argument type mismatch for 'r': expected 'Reg', got 'int'`. So the fold is tagged `named` and carries the
enum's SPELLING, which then rides the whole chain a constant travels: `ConstValue` → `TopLevelConstant`
→ `ConstEvalOutcome` → `TopLevelConstantLookup` → the use site. Each of those arms is WIDENED rather than
joined by a parallel one: a folded enum case is not a second kind of scalar constant, it is a scalar
constant that names its type.

⚠ **BYTES, NOT AN INTERNED ID.** Ids are minted per PARSER; the evaluator is a throwaway parser built per
declaration and the use site is a third one, so an id minted during the fold means nothing where it is
read. The reader interns the bytes into its own table — `fieldTypeOf(…, into:)`'s discipline.

⚠ **AND THE USE SITE EMITS THROUGH `emitEnumTagLiteral`, THE DOOR THE BODY SPELLING TAKES**: the value is
TAGGED `named(E)` while the op it emits carries `integer`. Emitting the literal AT `MaxonType.named`
instead is a compiler panic — *"a `named` type must be resolved to a primitive before lowering"* — because
the op's valueType is a STORAGE question and `named` is not an answer to it.

⭐⭐ **A BOXED UNION'S PAYLOAD-FREE CASE IS A CONSTANT TOO — IT IS MATERIALIZED, NOT FOLDED.** `isBoxed`
means SOME case carries a payload, and then EVERY case — payload-free ones included — is a heap box rather
than a bare tag; shv2's own `SemanticCheck.maxon:729` says so about its own binding (*"a payload-free union
case is still a heap object in Maxon"*). But that settles HOW such a constant is built, never WHETHER it is
one. The box is allocated before `main` by `__module_init` — `boxSizeBytes()` bytes, tag stored at offset 0,
payload slots ZEROED, increfed into the `.data` slot — and released by `__maxon_global_cleanup`. That is the
same path an empty container and a `create()`-style factory already took, which is why the fold tier was the
wrong tier to ask the question in.

⛔⛔ **THIS PARAGRAPH USED TO CLAIM THE OPPOSITE, AND IT WAS MEASURED FALSE.** It read *"A BOXED UNION'S CASE
IS EXCLUDED, AND THAT IS THE RULE RATHER THAN A LIMIT OF THIS SLICE … A heap object is not something a
constant folds to"*, and pinned that refusal as a passing case named
`error.a-boxed-unions-case-is-still-not-a-constant`. The runnable oracle disagreed the whole time: it
compiles the byte-identical program, runs it to exit 0, and its `__module_init` heap-allocates the box
instead of folding anything. The sentence was a SLICE BOUNDARY wearing a rule's words — this tree's
recurring defect, a comment asserting a property nothing tests — and the case that pinned it was INVERTED
rather than deleted, so the claim reads as measured false rather than quietly dropped. The refusal that
survives is the honest one: a case the union does not declare.

<!-- test: a-payload-free-enum-case-is-a-top-level-constant -->
A plain enum, and a constant that REFERENCES another constant — which is the path through
`ConstEvalOutcome`, the one arm a reader might think the use site does not need.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Reg
	rcx
	rdx
	rax
end 'Reg'

let baseReg = Reg.rdx
let aliasOfBase = baseReg

function ordinalOf(r Reg) returns Integer
	return r.ordinal
end 'ordinalOf'

function main() returns ExitCode
	let local = baseReg
	print("base={ordinalOf(baseReg)} alias={ordinalOf(aliasOfBase)} name={local.name}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
base=1 alias=1 name=rdx
```

<!-- test: a-backed-enum-case-constant-keeps-both-accessors -->
⭐ **THE BACKED CASE, AND IT IS THE ONE THAT SAYS *WHICH* NUMBER IS FOLDED.** `Status.notFound` has
ordinal 1 and raw value 404; the fold stores the TAG and the accessors derive the rest from the layout, so
a fold that had stored the raw value would answer `ord=404` here.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Status
	ok = 200
	notFound = 404
end 'Status'

let backed = Status.notFound

function rawOf(s Status) returns Integer
	return s.rawValue
end 'rawOf'

function ordOf(s Status) returns Integer
	return s.ordinal
end 'ordOf'

function main() returns ExitCode
	print("raw={rawOf(backed)} ord={ordOf(backed)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
raw=404 ord=1
```

<!-- test: a-boxed-unions-payload-free-case-is-a-constant -->
⭐⭐ **THE INVERTED CONTROL — the program this spec pinned as an ERROR until the claim was measured against
the oracle.** `Boxed` has a payload-carrying case, so `plain` is a heap box; it is a constant all the same,
built by `__module_init` rather than folded. Byte-identical to the old `error.` case but for the binding's
name, which no longer describes what happens to it.
```maxon
typealias Integer = int(i64.min to i64.max)

union Boxed
	plain
	withPayload(n Integer)
end 'Boxed'

let boxedCase = Boxed.plain

function main() returns ExitCode
	return match boxedCase 'k'
		plain gives 0
		withPayload(n) gives n as ExitCode
	end 'k'
end 'main'
```
```exitcode
0
```

<!-- test: a-boxed-union-constant-stores-its-tag-and-zeroes-the-payload -->
⚠ **WHICH NUMBER GOES IN THE BOX, AND WHAT SITS BESIDE IT.** `beta` is tag 1 with a payload-free case on
either side of it, so a build that stored the wrong case's tag answers something other than 20 — and one
that folded the constant to a bare tag instead of allocating would hand `match` the number 1 to dereference.
The payload slot must be ZEROED rather than left holding whatever `__mm_alloc` returned: `withPayload`'s arm
is never taken here, but the slot is part of the record the cleanup releases.

Reading the same global three times must give ONE answer — the slot holds a record, not a value each read
rematerializes.
```maxon
typealias Integer = int(i64.min to i64.max)

union Boxed
	alpha
	beta
	withPayload(n Integer)
end 'Boxed'

let konst = Boxed.beta

function tagOf(b Boxed) returns Integer
	return match b 'k'
		alpha gives 10
		beta gives 20
		withPayload(n) gives n
	end 'k'
end 'tagOf'

function main() returns ExitCode
	let copy = konst
	print("direct={tagOf(konst)} copy={tagOf(copy)} again={tagOf(konst)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
direct=20 copy=20 again=20
```

<!-- test: a-boxed-union-constant-shared-across-many-reads-frees-cleanly -->
⭐ **THE SHAPE THE COMPILER'S OWN SOURCE USES, AND THE EXIT CODE IS THE GATE.** One shared payload-free
sentinel read and stored many times is exactly `Parser.sharedUnreadParamType` and `Project.NoPayloadStorage`
— a single box for the whole program instead of one allocation per site. Every push CO-OWNS that one box, so
`__module_init`'s incref, each push's own, and `__maxon_global_cleanup`'s decref have to balance: an
unbalanced pair is a leak (exit 101) or a double free, neither of which a stdout comparison can see.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias BoxedArray = Array with Boxed

union Boxed
	plain
	withPayload(n Integer)
end 'Boxed'

let sentinel = Boxed.plain

function tagOf(b Boxed) returns Integer
	return match b 'k'
		plain gives 7
		withPayload(n) gives n
	end 'k'
end 'tagOf'

function main() returns ExitCode
	var xs = BoxedArray.create()
	var i = 0 as Integer
	var sum = 0 as Integer
	while i < 4 'each'
		xs.push(sentinel)
		sum = sum + tagOf(sentinel)
		i = i + 1
	end 'each'
	let first = try xs.get(0) otherwise Boxed.plain
	print("count={xs.count()} sum={sum} first={tagOf(first)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
count=4 sum=28 first=7
```

<!-- test: a-boxed-union-constants-payload-slots-are-zeroed -->
⚠⚠ **THE ONE CASE HERE THAT CAN SEE THE ZERO-FILL — and finding it took ruling out the obvious candidate.**
An `or` pattern may bind a payload the matched case does not declare (`none or some(n)`), and reading `n` on
the `none` path reads the box's payload SLOT directly. `Parser.emitEnumBox` says a diagnostic rests on
exactly this: E3129 admits the pattern *because* the fill pins a deterministic 0 there, and
`zeroInhabitsPayloadType` decides whether that 0 is a value of the binding's type. So this program's answer
IS the slot's contents, and the box `__module_init` builds owes the same 0 the box a function body builds does.

⚠ **THE DESTRUCTOR CANNOT SEE IT, WHICH IS WHY THE FIRST VERSION OF THIS CASE PROVED NOTHING.** It used a
MANAGED payload (`held(s String)`) on the theory that a slot left un-zeroed would be `__str_decref`'d as a
bogus pointer at cleanup. MEASURED, that is false: `synthesizeUnionDestructor` is TAG-DISPATCHED — it loads
the tag and drops managed fields only for the case that matches, and a payload-free case's tag matches no
managed case and branches straight to the free. That case passed with the slots deliberately filled with `1`.

⚠ **AND `__mm_alloc` ALREADY RETURNS ZEROED MEMORY, so "delete the fill" is not the sabotage that tests it** —
the slab would hand back zeros anyway and every case here would stay green. The fill is what keeps the
guarantee once an allocator recycles (`emitEnumBox`'s own note carries that caveat). MEASURED with the slots
filled with `1` instead: this case answers `1`, and it is the only one in this group that moves.
```maxon
typealias Integer = int(i64.min to i64.max)

union Counter
	none
	some(n Integer)
end 'Counter'

let sentinel = Counter.none

function valueOf(c Counter) returns Integer
	return match c 'k'
		none or some(n) gives n
	end 'k'
end 'valueOf'

function main() returns ExitCode
	return valueOf(sentinel) as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: error.a-case-the-union-does-not-declare-is-still-refused -->
⛔ **THE BOUNDARY THAT SURVIVED, and it is one BOTH compilers keep.** Admitting a boxed union's
payload-free case does not admit any member name at all: `noSuchCase` is not a case of `Boxed`, so the arm
declines it and the constant evaluator reports it where the author wrote it. The oracle refuses the same
program too (E3034, *unknown enum case*) — unlike the case above, this refusal is a RULE and not a slice
boundary, which is the whole reason it is the one kept as the control.
```maxon
typealias Integer = int(i64.min to i64.max)

union Boxed
	plain
	withPayload(n Integer)
end 'Boxed'

let stillRefused = Boxed.noSuchCase

function main() returns ExitCode
	return match stillRefused 'k'
		plain gives 0
		withPayload(n) gives n as ExitCode
	end 'k'
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:20: Unsupported: `Boxed.noSuchCase` in a constant initializer — a constant is settled before `main`, either folded to a number or materialized by the module initializer, so it can name another top-level `let`, a literal, an empty container, a payload-free enum or union case, a `create()`-style factory at the TOP of an initializer, or a sized type's `.min`/`.max`, and nothing else
```

### AN ARRAY LITERAL OF ENUM CASES

⭐⭐ **AND SO IS AN ARRAY OF THEM.** The scalar half above folds `let JumpTableBaseReg = X64Register.r10`;
this is the same fact one level down — `let calleeSavedOrder = [X64Register.rbx, X64Register.r12, …]`
(`Targets/X64/X64PrologueEpilogue.maxon:123`), which is how three of shv2's own backends write their
register tables, plus the lexer's 256-entry `charClassTable`.

⚠ **THE ELEMENT CARRIES THE ENUM'S NAME AND THE KIND DOES NOT, which is not an arbitrary split.** The KIND
says which FAMILY the array belongs to; the enum's spelling lives on the ELEMENT because that is where the
walk has it. `constArrayValueType` reads it off element 0 — the first element fixes the instance — and the
homogeneity test compares the name as well as the kind.

⛔ **THAT COMPARISON HAD TO CHANGE, and the old one would have accepted a program with no type.**
`constArrayElementKindOf(elem) != element` was the whole test, and it is a KIND comparison:
`[Reg.rcx, CharClass.tab]` is two enums and ONE kind, so it would have been accepted and then typed from
whichever element the walk saw first. `constArrayElementsAgree` compares the name for the enum kind and
for no other, because no other element kind has one.

⛔⛔ **AND THE CONTENT HASH NEEDED THE NAME TOO.** An array global's readers depend on its LABEL and its
element TYPE and deliberately NOT on its values — so the elements are not hashed, and without the name
`[Reg.rcx]` and `[CharClass.other]` have the same kind and the same label, hash identically, and a file
edited from one to the other is answered from the other's parse. Its two neighbours can hash a shape alone
because their kind IS their element type; `enumCase` is a family.

<!-- test: an-array-literal-of-enum-cases-is-a-top-level-constant -->
Read three ways — iterated, counted, and indexed — because the instance is what the fix is about and each
reads it differently.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Reg
	rcx
	rdx
	rax
	r10
end 'Reg'

let calleeSaved = [Reg.rcx, Reg.rdx, Reg.r10]

function ordinalOf(r Reg) returns Integer
	return r.ordinal
end 'ordinalOf'

function main() returns ExitCode
	var total = 0
	for r in calleeSaved 'each'
		total = total + ordinalOf(r)
	end 'each'
	let first = try calleeSaved.get(0) otherwise panic("get")
	print("count={calleeSaved.count()} total={total} first={ordinalOf(first)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
count=3 total=4 first=0
```

<!-- test: error.an-array-literal-may-not-mix-two-enums -->
⭐ **THE CASE THE OLD KIND-ONLY COMPARISON WOULD HAVE LET THROUGH.** Two enums, one kind. Accepted, this
array would have been typed `Array with Reg` and hold a `CharClass` in its second slot.
```maxon
enum Reg
	rcx
	rdx
end 'Reg'

enum CharClass
	other
	tab
end 'CharClass'

let mixed = [Reg.rcx, CharClass.tab]

function main() returns ExitCode
	return mixed.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/static-variables/error.an-array-literal-may-not-mix-two-enums.test:12:23: Unsupported: an array literal with mixed element types — every element must have the same type as the first
```
