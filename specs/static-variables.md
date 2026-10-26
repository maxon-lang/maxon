---
feature: static-variables
status: experimental
keywords: [static, var, global, mutable, module, type]
category: language
---
# Static Variables

## Documentation

### Top-Level `var` Declarations

Top-level `var` declarations define mutable module-level variables stored in the program's writable data section. A `var` is never image data; a top-level `let` whose value is decided at compile time is, and lives in read-only memory.

#### Syntax

```maxon
var counter = 0 as Count
export var globalState = false
```

#### Features

- **Runtime storage**: Variables are stored in the writable data section
- **Initialization**: Every initializer runs before `main`, exactly once, in dependency order, whether or not anything reads the variable (`specs/lazy-static-observable.md`)
- **Type inference**: Type is inferred from the initializer
- **Export support**: Use `export var` to make variables available to other modules

#### Initializer Requirements

A top-level `let` or `var` initializer is any expression a function body accepts, parsed by the body's
expression parser with the same meaning and the same diagnostics. A scalar binding must have a value known at
compile time (E2045 names the call that makes it unknown), and a `var` that starts from a bare number is
E2004. A record whose value is decided at compile time is laid down as read-only data; any other is built by
`__module_init` before `main`, in dependency order. A struct literal is refused at file scope (E3076); inside a
`type` body a static member may be initialized by a struct literal of that type (`specs/lazy-static.md`). What an initializer's calls may reach is restricted further: E3165, E3078 and E3164, below and in `specs/green-thread-globals.md`.

Code a service's message can run touches no module-level `var` and no `static var` (E3143, `specs/green-thread-globals.md`); a program that spawns a service may still declare one that only `main` and its callees use.

A top-level `let`'s record is read-only through every name that reaches it — a `let` bound to it, a field
read off it, a parameter it was passed to. Calling a method that writes its receiver on it, or passing it
to a parameter the callee writes, is E3159 naming the root binding; storing it into a `var` field is
E3078, at the store, since the function that later writes the field cannot see where its value came from. `.clone()` makes a record of one's own.

### Static Fields in Types

Types can have static fields that are shared across all instances. Static fields use the `static` keyword before `var` or `let`.

#### Syntax

```maxon
typealias Score = int(i64.min to i64.max)

type Counter
	static var count = 0 as Score  // Mutable static field
	static let MAX = 100           // Immutable static field

	export var value as Score      // Instance field

	static function create(value Score) returns Self
		return Self{value: value}
	end 'create'
end 'Counter'
```

#### Features

- **Shared storage**: One copy exists for the type, not per instance
- **Direct access**: Access via `TypeName.fieldName` syntax
- **Static let**: Immutable (E2013 on assignment); image data when its value is decided at compile time, otherwise built before `main` (same as top-level `let`)
- **Static var**: Mutable storage, never image data (same as top-level `var`)
- **Initializer**: The top-level initializer grammar above, plus a struct literal of the declaring type

#### Access Patterns

```maxon
function main() returns ExitCode
	Counter.count = Counter.count + 1   // Access static field
	var c = Counter.create(10)          // Create instance
	c.value = 20                        // Access instance field
	print("{c.value} {Counter.count} {Counter.MAX}")
	return 0
end 'main'
```

## Tests

<!-- test: top-level-var-basic -->
```maxon
var counter = 0 as FullRangeInt

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

var total = 10 as Integer

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
var a = 1 as FullRangeInt
var b = 2 as FullRangeInt
var c = 3 as FullRangeInt

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
var offset = 0 as FullRangeInt

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
	static var count = 0 as FullRangeInt
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
	static var count = 0 as FullRangeInt

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
	static var value = 10 as FullRangeInt
end 'TypeA'

type TypeB
	static var value = 20 as FullRangeInt
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
	static var created = 0 as Integer
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

the compiler makes `static let MAX = 100` inside `type Config` a top-level binding named `Config.MAX`.
Everything below follows from that one sentence rather
than from machinery of its own: per-type identity is the qualifier, the initializer obeys the top-level
initializer grammar exactly, a `static let` refuses a write through the same E2013 door a file-scope `let`
does, and a static member takes no slot in the instance layout because it never joins one.

<!-- test: error.static-let-reassign -->
Assigning to a `static let` is E2013, in the same words a top-level `let` gets
(`top-level-let-scalar-reassign-error`) and naming the qualified binding the author actually wrote.
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
and then had no binding to read; keying on the QUALIFIED name is what makes the arm decline.

Declining is not the same as falling through to the numeric-bound reading. The base is a `type` the
author declared, so the honest sentence is about that type's static roster rather than about `min` and
`max` — words for a base this program does not have. The refusal names the MEMBER token, which is the
name that is missing.

```maxon
type Config
	static let MAX_SIZE = 42
end 'Config'

function main() returns ExitCode
	return Config.MIN_SIZE
end 'main'
```
```maxoncstderr
error E2073: <fragment>:7:16: type 'Config' has no static member named 'MIN_SIZE'
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
	export static var used = 2 as FullRangeInt
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

	function tag() returns Integer
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

Nothing about a static member's initializer is its own: it is read by the body's expression parser, as
though written inside its type's body, so every form a file-scope `let`/`var` admits a static member admits,
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

⛔ **A STRUCT LITERAL IS NEVER REFUSED FOR STANDING IN AN INITIALIZER.** Every refusal it meets there is one
the same literal meets in a method body: E3076 for the wrong body, and the field refusals the sections below
pin.

### A struct-literal initializer's field refusals are the LITERAL's own, not the initializer grammar's

The construction reaches the same three deciders a literal written in a method body reaches — the field
roster, the "every slot must end up initialized" rule and the constructor restriction — so a bad field is
refused for what is wrong with it rather than for standing in an initializer.

⚠ **THE TWO CASES BELOW ARE HERE, AND NOT IN `specs/lazy-static.md`, BECAUSE THEY ARE PINNED AGAINST
the compiler's OWN WORDING OF THESE TWO DIAGNOSTICS** — which is the arrangement `error.static-let-reassign`
above already makes for E2013, and for the same reason.

<!-- test: error.static-struct-literal-unknown-field -->
```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count

	static var origin = Pair{a: 1, b: 2, c: 3}

	static function get() returns Pair
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

	static function get() returns Pair
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
error E3086: <fragment>:8:26: Field 'b' of type 'Pair' is not initialized (provide in literal, add a default value on the declaration, or assign via self.field in a static factory)
```

<!-- test: error.struct-literal-missing-field-in-a-static-method -->
The same literal in a static method's body, reported at the same token, its opening brace.
```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count

	static function get() returns Pair
		let origin = Pair{a: 1}
		return origin
	end 'get'
end 'Pair'

function main() returns ExitCode
	let p = Pair.get()
	print("{p.a} {p.b}")
	return 0
end 'main'
```
```maxoncstderr
error E3086: <fragment>:9:20: Field 'b' of type 'Pair' is not initialized (provide in literal, add a default value on the declaration, or assign via self.field in a static factory)
```

### A struct-literal initializer's field must suit the slot it fills

The tag comparison is what makes the STORE right: a pointer written into a scalar slot reads back as a
number, and a number written into a managed one is a wild pointer. The range half is the body's: a constant
field value outside the field's range is refused at compile time, as the same literal in a function body is.

<!-- test: error.static-struct-literal-field-type-mismatch -->
```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count

	static var origin = Pair{a: "hi", b: 2}

	static function get() returns Pair
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

	static function get() returns Pair
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
method BODY are refused.

The two cases below are pinned against the compiler's own wording, for the reason the pair above is.

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

	static function get() returns Paint
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

An `interface`-typed field is a widening site at module scope exactly as it is in a function body:
`__module_init` stores the conformer and, beside it, the address of its witness table, which the field's
declared interface names. Such a record is never laid down as image data, because that second word is an
address only the lowering can name.

<!-- test: static-struct-literal-interface-field -->
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Square implements Sized
	export let side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function size() returns Integer
		return self.side * self.side
	end 'size'
end 'Square'

type Holder
	export let s as Sized
	export let tag as Integer

	static var one = Holder{s: Square.create(4), tag: 77}

	static function get() returns Holder
		return Holder.one
	end 'get'
end 'Holder'

function main() returns ExitCode
	let h = Holder.get()
	print("{h.s.size()} {h.tag}")
	return 0
end 'main'
```
```stdout
16 77
```

<!-- test: static-struct-literal-interface-field-holding-a-string-literal -->
A `String` literal widened into a `Hashable` field at module scope: the record is the literal's clone, and
its witness table names the standard library's own `String.hash`, which a program reaching no other
`Hashable` position must still build.
```maxon
type Holder
	export let key as Hashable

	static var one = Holder{key: "ab1cd"}

	static function get() returns Holder
		return Holder.one
	end 'get'
end 'Holder'

function main() returns ExitCode
	let h = Holder.get()
	print("{h.key.hash()}\n")
	return 0
end 'main'
```
```stdout
252765120
```

<!-- test: error.static-struct-literal-interface-field-non-conformer -->
A value that does not conform is refused as it is in a body, by the whole-program widening check.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Holder
	export let s as Sized
	export let tag as Integer

	static var one = Holder{s: 5, tag: 77}

	static function get() returns Holder
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
error E3005: <fragment>:12:19: field type mismatch for 'Holder.s': type 'int' does not implement interface 'Sized'
```

<!-- test: static-factory-initializer -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Row
	export var n as Integer

	static function create(n Integer) returns Self
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
	static var items = FullRangeIntArray from [1, 2, 3]
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
⭐⭐ **THE WRITE-THROUGH-AN-IMMUTABLE-STATIC GUARD.** A receiver-writing method
on a `static let` array is E3159 — the SAME rule
`error.top-level-let-array-mutate` pins for a file-scope `let`, naming the qualified binding.

The static read is routed through the receiver door so that the blame name reaches the check. Falling out
to `parsePostfix` instead, whose receiver is deliberately nameless (it serves literals and call results,
which are bound to no name), this exact program would compile clean and return **5** — a mutation of a
binding declared `let`, with no diagnostic anywhere.
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
error E3159: <fragment>:7:23: cannot pass 'Cache.xs', which is read-only (a module-level `let`), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: static-struct-member-field-write -->
A field STORE through a struct-valued static goes through the ordinary field-chain door, rooted at the
qualified name. On the read side the value falls out and `parsePostfix` takes the hop; the WRITE reaches
the chain resolver, where the base `Cache` is neither a value in scope nor a bare global, so the chain
must be rooted at the qualified name rather than refused as `E2004: Undefined variable 'Cache'` — a name
defined four lines up. Both spellings root at `Cache.head`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Row
	export var n as Integer

	static function create(n Integer) returns Self
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
Bool global followed by non-zero global must not bleed adjacent data.

```maxon
var flag = false
var counter = 42 as FullRangeInt

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

<!-- test: top-level-var-enum-initializer -->
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

<!-- test: top-level-var-enum-initializer-cross-file -->
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
var offset = (BASE + 1) as FullRangeInt

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
var items = FullRangeIntArray from [10, 20, 30]

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
typealias IntegerArray = Array with Integer
var scores = IntegerArray from [10, 20, 30]

function getTotal() returns Integer
	let a = try scores.get(0) otherwise 0
	let b = try scores.get(1) otherwise 0
	let c = try scores.get(2) otherwise 0
	return a + b + c
end 'getTotal'

function setScore(index Integer, value Integer)
	try scores.set(index as ElementIndex, value: value) otherwise panic("test invariant: set OOB")
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
typealias IntegerArray = Array with Integer
var counters = IntegerArray from [0, 0, 0]

function increment(index Integer)
	let current = try counters.get(index as ElementIndex) otherwise 0
	try counters.set(index as ElementIndex, value: current + 1) otherwise panic("test invariant: set OOB")
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
(E3159).

A never-mutated `let` array is NOT promoted to an immortal shared static record: an `Array`'s own methods
rewrite its record (a `push` detaches the buffer by writing `buffer@0`/`capacity@16`/`length@8`), and a
`.rdata` record cannot be written — so immortality would need an enforced never-mutated guarantee and a
real COPY promotion for `var b = <borrowed array>`, neither of which exists. Heap for both is what can be
stated truthfully.

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
An array literal's elements are body expressions, so a reference to another top-level *scalar* constant —
and arithmetic on it — is an element.
```maxon
let BASE = 20
var xs = FullRangeIntArray from [BASE, BASE + 2]

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

⚠ **`untouched` DOES NOT REACH THE BINARY**: nothing names it, so dead-global
elimination drops its slot, the `__managed_create`/`__managed_push` run that would build it and the `__managed_decref` that
would free it — which is why the emitted `.data` holds `grow` and `fixed` alone. The declaration contributes
nothing to the answer, and what the case pins is the pair that matters here: `grow`
and `fixed` are TWO LIVE array globals sharing one `__module_init`, so the prune is per-global rather
than per-init.
```maxon

var grow = FullRangeIntArray from [1]
var untouched = FullRangeIntArray from [7, 7, 7]
let fixed = [4, 5, 6]

function main() returns ExitCode
	var i = 0 as FullRangeInt
	while i < 200 'fill'
		grow.push(i)
		i = i + 1
	end 'fill'
	grow = [3, 4]
	let a = try grow.get(0) otherwise 0
	let b = try grow.get(1) otherwise 0
	let c = try fixed.get(2) otherwise 0
	return a + b + c + grow.count() as FullRangeInt
end 'main'
```
```exitcode
15
```

<!-- test: error.top-level-let-array-mutate -->
A receiver-writing method on a `let`-bound array is E3159 — the SAME rule a `let`-bound String or Set
receiver gets, under the one deep-`let` rule. Without it a `let` array would be writable, since
it has a real slot.
```maxon
let fixed = [4, 5, 6]

function main() returns ExitCode
	fixed.push(9)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:5:8: cannot write through 'fixed': 'fixed' is read-only (a module-level `let`) and nothing reachable through it may be written; use 'var', or write a clone()
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

<!-- test: a-top-level-array-let-names-another-global -->
A `let` initializer may name another `let` global, as a body's `let b = A` does: `B` reads the same
array.
```maxon
let A = [1, 2]
let B = A

function main() returns ExitCode
	return ((try B.get(0) otherwise 0) + (try B.get(1) otherwise 0)) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-top-level-array-let-names-another-global-forward -->
The same, written the other way round: declaration order does not matter.
```maxon
let B = A
let A = [1, 2]

function main() returns ExitCode
	return ((try B.get(0) otherwise 0) + (try B.get(1) otherwise 0)) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: error.top-level-let-array-var-alias -->
A `let` array's record cannot be laundered into a mutable ALIAS. A borrowed managed aggregate bound to a
`var` is promoted to owned by an INCREF of the same box — reference semantics, deliberately, because the
alias is observable — so without this refusal `var b = A` then `b.push(9)` would grow `A`. A `var` is a
writable name, and the record it would name is one the program declared `let`, so the BINDING is refused
(E3078). Refused where the SOURCE is immutable, and only there: the same binding off a `var` global shares.

⚠ **THE REFUSAL IS ON THE BINDING.** `var b = A` is E3078; `let b = A` is legal and can only read
(`top-level-let-array-let-alias`). The mark rides the VALUE and is CARRIED through the promotion, so the
same refusal covers every door that hands a `var` the record — the direct binding, a reassignment, a
ternary/`match` merge that joins it, an `if`/loop merge that REBINDS it, a cell it is spilled into and a
closure that captures it, each pinned below — and, through a whole-program fact, a call that hands the
record back.

⚠ **A CALL BOUNDARY IS CROSSED BY THE CALLEE'S SUMMARY, NOT BY THE MARK.** A callee's parameter is a fresh
value in a fresh space, so the mark does not enter it; what crosses is what the callee does with it.
`g(A)` where `g(xs …)` binds `var b = xs` and writes `b` is E3159 at the call, because `g` writes `xs`.
`var c = idBox(G)` where `idBox(x Box)` is `return x` is E3078, because the result may share storage with
`G`. `Holder.of(G)` followed by `h.inner.n = 99` is E3159 at the write, because the record `h` holds is `G`'s.
```maxon
let A = FullRangeIntArray from [1, 2]

function main() returns ExitCode
	var b = A
	b.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E3078: <fragment>:5:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.top-level-let-array-var-reassign-alias -->
The same refusal through the other door — a REASSIGNMENT of an already-owned `var`, which promotes a
borrowed value through the identical single-sourced path and so CARRIES the mark through the identical
one. `b = A` is refused at the reassignment, as `var b = A` is at the binding.
```maxon
let A = FullRangeIntArray from [1, 2]

function main() returns ExitCode
	var b = FullRangeIntArray from [0]
	b = A
	b.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E3078: <fragment>:6:2: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.top-level-let-field-map-var-alias -->
A FIELD of a `let` whose record `__module_init` builds is that `let`'s own storage: `H.index` is the `let`'s
map, so a `var` made from it and written through is refused as one made from a field of a local `let` is.
```maxon
typealias Key = int(0 to 1000)
typealias Items = Array with Key
typealias ItemIndex = Map with (Key, Items)

type Holder
	export let index as ItemIndex

	static function create() returns Self
		var index = ItemIndex.create()
		index.upsert(1, value: [7])
		return Self{index: index}
	end 'create'
end 'Holder'

let H = Holder.create()

function main() returns ExitCode
	var m = H.index
	m.upsert(2, value: [9])
	return H.index.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:19:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'm'; use 'let' instead of 'var', or use clone()
```

<!-- test: top-level-let-field-accessor-read-only -->
An accessor may hand that field back, and a caller that only reads it compiles and runs.
```maxon
typealias Key = int(0 to 1000)
typealias Items = Array with Key
typealias ItemIndex = Map with (Key, Items)

type Holder
	export let index as ItemIndex

	static function create() returns Self
		var index = ItemIndex.create()
		index.upsert(1, value: [7])
		return Self{index: index}
	end 'create'
end 'Holder'

let H = Holder.create()

function index() returns ItemIndex
	return H.index
end 'index'

function main() returns ExitCode
	let found = index()
	return found.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.top-level-let-field-accessor-result-write -->
A `var` may not hold what the accessor handed back out of a module-level `let`, so the write through it is refused at the binding.
```maxon
typealias Key = int(0 to 1000)
typealias Items = Array with Key
typealias ItemIndex = Map with (Key, Items)

type Holder
	export let index as ItemIndex

	static function create() returns Self
		var index = ItemIndex.create()
		index.upsert(1, value: [7])
		return Self{index: index}
	end 'create'
end 'Holder'

let H = Holder.create()

function index() returns ItemIndex
	return H.index
end 'index'

function main() returns ExitCode
	var m = index()
	m.upsert(2, value: [9])
	return H.index.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:23:6: cannot assign a value, which may share storage with a `let` field, to mutable binding 'm'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.top-level-let-array-ternary-alias -->
And through a value MERGE, which is the door that costs one extra keyword. The mark that refuses the two
above rides the VALUE, and a merge mints a NEW value — so the phi has to inherit it, or `var pick = A if c
else M` launders exactly what `var pick = A` is refused for. Without that inheritance this program would
compile and return 3.
```maxon
let A = FullRangeIntArray from [1, 2]
var M = FullRangeIntArray from [5, 5]

function main() returns ExitCode
	var pick = A if M.count() == 2 else M
	pick.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E3078: <fragment>:6:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'pick'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.top-level-let-array-match-arm-alias -->
The same merge, through the `match`-expression door rather than the ternary — one `finalizeMatchMerge`
serves both, so a phi that inherits the mark for one inherits it for the other by construction.
```maxon
let A = FullRangeIntArray from [1, 2]
var M = FullRangeIntArray from [5, 5]

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
error E3078: <fragment>:12:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'pick'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.top-level-let-array-handed-back-as-a-map-miss-fallback -->
And through a call whose `otherwise` fallback is the `let` array: `pick` hands back `Empty`'s own record
whenever the lookup misses, so binding it to `var mine` is refused as the direct alias is.
```maxon
typealias Count = int(0 to 1000)
typealias Items = Array with Count
typealias ItemIndex = Map with (Count, Items)

let Empty = Items.create()

function pick(index ItemIndex, k Count) returns Items
	return try index.get(k) otherwise Empty
end 'pick'

function main() returns ExitCode
	var index = ItemIndex.create()
	var stored = Items.create()
	stored.push(7)
	index.upsert(1, value: stored)
	var mine = pick(index, k: 9)
	mine.push(3)
	return Empty.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:17:6: cannot assign the result of 'pick', which may share storage with a module-level `let`, to mutable binding 'mine'; use 'let' instead of 'var', or use clone()
```

<!-- test: top-level-array-merge-of-mutable-globals-shares -->
The merge mark is narrow to the same thing the direct one is: a merge of two MUTABLE globals still binds
and still SHARES, so the phi rule refuses laundering rather than refusing merges. `P` grows, `Q` does not.
```maxon
var P = FullRangeIntArray from [1, 2]
var Q = FullRangeIntArray from [5, 5]

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
Off a MUTABLE global the same binding is legal and SHARES the record — reference semantics; it returns 3
here. The refusal above keys on the source's immutability, not on aliasing itself.
```maxon
var A = FullRangeIntArray from [1, 2]

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
⭐ **READING AND RETURNING A `let` GLOBAL IS LEGAL; MUTATING THROUGH THE RESULT IS THE ERROR.** This is
the family's control: an accessor that hands a `let`-declared global's record back to a caller that only
READS it compiles and runs. It is the shape `stdlib/CharacterSet.maxon`'s eleven presets are declared in,
so refusing the RETURN itself would stand between the library and its callers.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static let shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	static function get() returns Box
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
The same accessor off a `var` global, which is the case that says the rule keys on IMMUTABILITY
and not on returning a global at all: the write goes through and is observable on the global. It is the
one keyword's difference that proves the refusal below is guarding the `let` claim rather than the aliasing.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static var shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	static function get() returns Box
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
And the binding is where it is refused. `Box.get()` may hand the record out; a `var` bound to what it
handed back would be a writable name for a global declared `let`, so THAT is the diagnostic, on the
binding's own line. The caller learns this from a whole-program fact — "may this function's return alias
an immutable global?" — because the accessor's body may be in another file, which is exactly why the refusal
could not stay at the return.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static let shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	static function get() returns Box
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
error E3078: <fragment>:19:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'a'; use 'let' instead of 'var', or use clone()
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

	static function get() returns Box
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
error E3078: <fragment>:23:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'a'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.let-global-passed-to-field-writing-callee -->
⛔ **A `let`-DECLARED GLOBAL HANDED STRAIGHT TO A CALLEE THAT WRITES A FIELD OF IT** — without the
record-write column it silently mutates the global (`g 99`, where the program declares `G` a `let`).
The narrow mask E3159 reads alone at a receiver slot records no field write, so it cannot see it; the
record-write column is this program.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static function make() returns Box
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
error E3159: <fragment>:19:2: cannot pass 'G', which is read-only (a module-level `let`), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: error.let-global-to-a-callee-that-calls-a-self-writing-method -->
⚖ The same refusal ONE FRAME DOWN, where the callee writes nothing of its own and only calls a method that
writes its own receiver. That write is exempt for a `let` BINDING — the exemption `parameter-mutation.md`
documents — and a global is the subject it is NOT exempt for, so the record-write closure is taken twice and
the call site reads whichever column its subject calls for. `G.bump()` written directly is refused too.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

let G = Box.make()

function tick(b Box)
	b.bump()
end 'tick'

function main() returns ExitCode
	tick(G)
	return G.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:23:2: cannot pass 'G', which is read-only (a module-level `let`), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: error.let-global-to-a-callee-that-calls-a-self-writing-method-on-a-field -->
The same refusal where the self-writing method is called on a record INSIDE what the callee was handed: the
global's graph is written one record down, and nothing may write a global declared immutable.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

type Holder
	export var inner as Box

	static function make() returns Holder
		return Holder{inner: Box.make()}
	end 'make'
end 'Holder'

let G = Holder.make()

function nudge(t Holder)
	t.inner.bump()
end 'nudge'

function main() returns ExitCode
	nudge(G)
	return G.inner.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:31:2: cannot pass 'G', which is read-only (a module-level `let`), to a function that writes parameter 't' (in main); pass a clone()
```

<!-- test: error.let-global-to-a-callee-that-pushes-into-a-container-a-call-hands-back-from-inside-it -->
The same refusal where the write is a built-in container method: `pick` hands back the array `t` holds, and
`poke` pushes into it, so the global's graph is written one record down.
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Holder
	export var items as CountArray

	static function make() returns Holder
		return Holder{items: CountArray.create()}
	end 'make'
end 'Holder'

let G = Holder.make()

function pick(t Holder) returns CountArray
	return t.items
end 'pick'

function poke(h Holder)
	pick(h).push(1)
end 'poke'

function main() returns ExitCode
	poke(G)
	return G.items.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:24:2: cannot pass 'G', which is read-only (a module-level `let`), to a function that writes parameter 'h' (in main); pass a clone()
```

<!-- test: error.let-global-to-a-callee-that-pushes-into-a-var-merged-from-inside-it -->
The same write through a `var` that may hold the array inside the parameter on one path and a fresh one on the
other: what it may lie within is what the push writes.
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Inner
	export var items as CountArray

	static function make() returns Inner
		return Inner{items: CountArray.create()}
	end 'make'
end 'Inner'

type Holder
	export var inner as Inner

	static function make() returns Holder
		return Holder{inner: Inner.make()}
	end 'make'
end 'Holder'

let G = Holder.make()

function poke(t Holder, deep bool)
	var list = CountArray.create()

	if deep 'fromInner'
		list = t.inner.items
	end 'fromInner'

	list.push(1)
end 'poke'

function main() returns ExitCode
	poke(G, deep: true)
	return G.inner.items.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:34:2: cannot pass 'G', which is read-only (a module-level `let`), to a function that writes parameter 't' (in main); pass a clone()
```

<!-- test: a-let-global-held-by-a-record-a-callee-builds-is-not-written-by-writing-that-record -->
The control: `mint` stores `guards` in the record it builds and pushes into that record's own fresh array, which
lies within nothing `guards` holds, so handing it the global is legal and the global is untouched.
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Guards
	export var cols as CountArray

	static function make() returns Guards
		return Guards{cols: CountArray.create()}
	end 'make'
end 'Guards'

let Shared = Guards.make()

type Blocks
	export var subst as CountArray
	export var guards as Guards

	static function create(guards Guards) returns Blocks
		return Blocks{subst: CountArray.create(), guards: guards}
	end 'create'
end 'Blocks'

function mint(guards Guards) returns Blocks
	var blocks = Blocks.create(guards)
	blocks.subst.push(1)
	return blocks
end 'mint'

function main() returns ExitCode
	let b = mint(Shared)
	return (b.subst.count() * 10 + Shared.cols.count()) as ExitCode
end 'main'
```
```exitcode
10
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

	static function get() returns Box
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
error E3078: <fragment>:23:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'a'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.let-global-to-callee-that-forwards-the-write -->
⭐ **AND THE PARAMETER HALF IS A CLOSURE TOO.** `bump` writes nothing at all — it forwards its parameter
to a `poke` that does — so the bit reaches `bump` only through the least fixpoint over the call graph.
Two hops on the argument side, as the case above is two hops on the return side.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static function make() returns Box
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
error E3159: <fragment>:23:2: cannot pass 'G', which is read-only (a module-level `let`), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: error.let-global-given-out-of-a-closure -->
⭐ **THE INDIRECT-CALL BOUNDARY, STATED RATHER THAN LEFT SILENT.** A closure has no static callee, so no
whole-program fact can say what calling one returns — and without that, a tainted value handed out of a
closure would escape into a call the analysis cannot follow. So a closure keeps the refusal at the
`gives`, which is the conservative answer, deliberately not widened. The remedy is the one the message
names.

⚠ **HONEST STATUS: this case is a REGRESSION GUARD.** What it pins is that the RETURN a named function
may make does not widen to a closure. It goes red the day a fact is published for a closure
without the analysis to back it. Its positive twin is `top-level-let-struct-accessor-read-only` above —
the same shape, out of a NAMED function, which compiles.
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
error E2015: <fragment>:5:21: Unsupported: returning a read of a `let`-declared top-level global — an aggregate has no owning COPY in the compiler, so the returned value would alias the SAME record and a write through it would mutate a global declared immutable; read it through a `let` binding, or declare the global `var`
```

<!-- test: error.let-global-alias-in-a-cell-keeps-its-mark -->
⛔⛔ **A CELL IS A THIRD PLACE THE RECORD LIVES, AND THE REFUSAL REACHES IT.** A parameter its function
reassigns is CELL-RESIDENT, and a cell binding's `boundValue` is the CELL, not the value the promotion marked —
so a door asking only about `boundValue` would see an unmarked binding and let `grow(xs)` through **in the
very frame that rebound `xs`**, silently mutating the global.

⇒ The reassignment `xs = expose()` hands that cell a `let` global's record, and it is refused where it
happens (E3078), as `var xs = expose()` is: where a binding's value lives cannot change whether it may hold
the record.
```maxon
typealias Names = Array with String

let A = ["a", "b"]

function expose() returns Names
	return A
end 'expose'

function grow(xs Names) returns Integer
	xs.push("zz")
	return xs.count()
end 'grow'

function regrow(xs Names) returns Integer
	xs = expose()
	return grow(xs)
end 'regrow'

function main() returns ExitCode
	var b = ["c"]
	let n = regrow(b)
	return ((A.count() as Integer) + n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:16:2: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'xs'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.let-global-read-reassigned-into-a-cell-keeps-its-mark -->
A cell-resident parameter reassigned a bare read of the `let` global would take its own reference to that
record; the reassignment is refused just as it is for a call's result.
```maxon
typealias Names = Array with String

let A = ["a", "b"]

function grow(xs Names) returns Integer
	xs.push("zz")
	return xs.count()
end 'grow'

function regrow(xs Names) returns Integer
	xs = A
	return grow(xs)
end 'regrow'

function main() returns ExitCode
	var b = ["c"]
	let n = regrow(b)
	return ((A.count() as Integer) + n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:12:2: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'xs'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.let-global-alias-captured-by-a-closure -->
⛔⛔ **A CLOSURE MUST NOT LAUNDER THE TAINT.** A closure body is its own SSA space, so the per-value marks are
swapped out at its boundary — correctly, since an id means nothing there — and the read of the module-level `let`
that `expose()` hands back inside the closure has to be marked again in the closure's own space, or the write
`grow` makes would reach the global — exit 3.

⚠ **THE FRAME NAMED IS THE LIFTED CLOSURE**, which is where the call is.
```maxon
typealias Names = Array with String
typealias IntThunk = function() returns Integer

let A = ["a", "b"]

function expose() returns Names
	return A
end 'expose'

function grow(xs Names) returns Integer
	xs.push("zz")
	return xs.count()
end 'grow'

function callThunk(f IntThunk) returns Integer
	return f()
end 'callThunk'

function main() returns ExitCode
	let n = callThunk(function() gives grow(expose()))
	return A.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3159: <fragment>:21:37: cannot pass the result of 'expose', which may share storage with a module-level `let`, to a function that writes parameter 'xs' (in main$closure_0); pass a clone()
```

<!-- test: error.let-global-alias-across-an-if-merge -->
⛔ **A MERGE THAT REBINDS A NAME MUST CARRY THE MARK, NOT ONLY THE MERGES THAT PRODUCE A VALUE.**
The ternary and `match … gives` merges carry it
(`propagateImmutableGlobalReadToPhi`, and `error.top-level-let-array-owned-merge-alias` above pins one);
an `if` that REBINDS a carried `var` mints its phi through a different door, and a phi that inherited
nothing would bring `b` out of the merge clean and accept `grow(b)`. The rebinding inside the `if` is the
refused door.
```maxon
typealias Names = Array with String

let A = ["a", "b"]

function expose() returns Names
	return A
end 'expose'

function grow(xs Names) returns Integer
	xs.push("zz")
	return xs.count()
end 'grow'

function main() returns ExitCode
	var b = ["q"]
	if A.count() > 1 'sometimes'
		b = expose()
	end 'sometimes'
	let n = grow(b)
	return ((A.count() as Integer) + n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:18:3: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.let-global-alias-carried-by-a-loop -->
⛔ **AND A LOOP IS THE SAME MERGE WITH THE EDGE PUSHED BEFORE THE PHI IS NAMED.** On a back edge the binding's own value IS the pushed value, so
the header phi — which is what every read of `b` after the loop resolves to — is a THIRD name for the
record and gets the mark from neither end (unmarked, the global is mutated and the program exits 6). It
is carried onto the loop's phi explicitly, at the one place the loop
names it.

⚠ The condition is runtime and the body may run zero times; the refusal is deliberately independent of
that, because "may alias" is the whole question. The rebinding inside the loop is the refused door.
```maxon
typealias Names = Array with String

let A = ["a", "b"]

function expose() returns Names
	return A
end 'expose'

function grow(xs Names) returns Integer
	xs.push("zz")
	return xs.count()
end 'grow'

function main() returns ExitCode
	var b = ["q"]
	for _ in 0 upto 2 'each'
		b = expose()
	end 'each'
	let n = grow(b)
	return ((A.count() as Integer) + n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:18:3: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.top-level-array-empty -->
An empty array literal has no element to infer a type from, so it is refused with the diagnostic a
function body's `[]` gets (`arrays/multi-line-literal-empty-still-rejected`).
```maxon
var items = []

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:13: Unsupported: an empty array literal `[]` — its element type cannot be inferred; use `Array with T` + `.create()` for an empty typed array
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
error E3005: <fragment>:2:14: argument type mismatch for 'value': expected 'int', got 'String'
```

<!-- test: a-body-array-bool-element -->
```maxon
function main() returns ExitCode
	var flags = [true, false]
	flags.push(true)
	return flags.count()
end 'main'
```
```exitcode
3
```

<!-- test: top-level-array-bool-element -->
A top-level array literal of `bool` elements is what a body's is.
```maxon
var flags = [true, false]

function main() returns ExitCode
	flags.push(true)
	return flags.count()
end 'main'
```
```exitcode
3
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

### A `let` initializer may not reach a managed `var`

⚖ **A `let` IS FIXED AT STARTUP, SO ITS INITIALIZER MAY NOT READ A MODULE-LEVEL `var` THAT HOLDS A RECORD.**
An initializer cannot name another global, but it may call, and a callee may read one. A record read out of a
`var` is still owned by the `var`, which can write it after `main` starts, so the `let` would not be fixed.
The refusal is E3165, at the read, with the `var`'s declaration as a note — even when only a scalar is read out
of the `var`: the compiler does not follow what the initializer keeps.

"Reaches" is the call graph once overload resolution has chosen each callee, closed through run-time
dispatches the way every whole-program service rule closes it. A scalar `var` owns no record and is not
refused; a `var` initializer and a `let` read by a `let` initializer are legal.

<!-- test: error.a-let-initializer-may-not-read-a-managed-var -->
The initializer's own callee hands back the `var`'s record, so `l` and `g` would own one record and `g`
could rewrite what `l` is fixed to.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return g
	end 'current'
end 'Box'

var g = Box.create()
let l = Box.current()

function main() returns ExitCode
	print("l={l.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3165: <fragment>:10:10: the initializer of the `let` `l` reaches the module-level `var` `g` here — a `let` is fixed at startup and may not hold what a `var` owns, and the compiler does not follow what the initializer keeps of `g`. Make `l` a `var`, or build its value without reading `g`
note: <fragment>:14:5: `g` is declared here
```

<!-- test: error.a-let-initializer-may-not-read-a-scalar-out-of-a-managed-var -->
**THE ACCEPTED OVER-APPROXIMATION.** `copied` builds a fresh record and keeps only the number, so nothing `g`
owns reaches `l` — and it is refused all the same, because the rule is about what the initializer reaches,
not about what it keeps.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function copied() returns Self
		return Self{n: g.n}
	end 'copied'
end 'Box'

var g = Box.create()
let l = Box.copied()

function main() returns ExitCode
	print("l={l.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3165: <fragment>:10:18: the initializer of the `let` `l` reaches the module-level `var` `g` here — a `let` is fixed at startup and may not hold what a `var` owns, and the compiler does not follow what the initializer keeps of `g`. Make `l` a `var`, or build its value without reading `g`
note: <fragment>:14:5: `g` is declared here
```

<!-- test: error.a-let-initializer-may-not-reach-a-managed-var-through-a-call -->
The read is two calls below the initializer: `current` names no global, `inner` does.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return Box.inner()
	end 'current'

	static function inner() returns Self
		return g
	end 'inner'
end 'Box'

var g = Box.create()
let l = Box.current()

function main() returns ExitCode
	print("l={l.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3165: <fragment>:14:10: the initializer of the `let` `l` reaches the module-level `var` `g` here — a `let` is fixed at startup and may not hold what a `var` owns, and the compiler does not follow what the initializer keeps of `g`. Make `l` a `var`, or build its value without reading `g`
note: <fragment>:18:5: `g` is declared here
```

<!-- test: error.a-let-initializer-may-not-reach-a-managed-var-through-the-overload-it-calls -->
The initializer calls the one-argument `make`, and that member reads `g`.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function make() returns Self
		return Self{n: 1}
	end 'make'

	static function make(seed Integer) returns Self
		return Self{n: g.n + seed}
	end 'make'
end 'Box'

var g = Box.create()
let l = Box.make(2)

function main() returns ExitCode
	print("l={l.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3165: <fragment>:14:18: the initializer of the `let` `l` reaches the module-level `var` `g` here — a `let` is fixed at startup and may not hold what a `var` owns, and the compiler does not follow what the initializer keeps of `g`. Make `l` a `var`, or build its value without reading `g`
note: <fragment>:18:5: `g` is declared here
```

<!-- test: an-overload-a-let-initializer-does-not-call-may-read-a-managed-var -->
**THE CONTROL FOR THE CASE ABOVE.** The initializer calls the zero-argument `make`; the member that reads `g`
is never called from it, so nothing refuses the program.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function make() returns Self
		return Self{n: 1}
	end 'make'

	static function make(seed Integer) returns Self
		return Self{n: g.n + seed}
	end 'make'
end 'Box'

var g = Box.create()
let l = Box.make()

function main() returns ExitCode
	g.n = 9
	let m = Box.make(1)
	print("l={l.n} g={g.n} m={m.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
l=1 g=9 m=10
```

<!-- test: a-let-initializer-may-read-a-scalar-var -->
A scalar `var` owns no record, so a `let` initializer may read it.
```maxon
type Box
	export var n as Integer

	static function sized() returns Self
		return Self{n: count}
	end 'sized'
end 'Box'

var count = 3 as Integer
let l = Box.sized()

function main() returns ExitCode
	count = 4
	print("l={l.n} count={count}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
l=3 count=4
```

<!-- test: a-var-initializer-may-read-a-managed-var -->
A `var` may share what another `var` owns: neither is fixed.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return g
	end 'current'
end 'Box'

var g = Box.create()
var v = Box.current()

function main() returns ExitCode
	v.n = 9
	print("v={v.n} g={g.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
v=9 g=9
```

<!-- test: a-let-initializer-may-read-another-let -->
A `let` may hold what another `let` holds: both are fixed.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return a
	end 'current'
end 'Box'

let a = Box.create()
let b = Box.current()

function main() returns ExitCode
	print("a={a.n} b={b.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
a=7 b=7
```

### A `var` initializer may not take a `let`'s record

⚖ **THE OTHER DIRECTION: A MODULE-LEVEL `var` MAY NOT BE HANDED A RECORD A MODULE-LEVEL `let` HOLDS.** The `var` could
write the record the `let` is fixed to. Unlike E3165 this is a question about FLOW: what the initializer's call may
hand back, at any depth — the `let`'s record itself or a record built around it. It is answered by the
storage-provenance summary of the call, carried through calls, witness dispatches and calls through function values.
A value built fresh out of numbers read from a `let` is legal. The refusal is E3078, at the `var`'s declaration.

The same summary refuses a write, inside a function, through a record a call handed back out of a `let`
(E3159).

<!-- test: error.a-var-initializer-may-not-take-a-lets-record -->
`current` hands back the `let`'s own record, so `main`'s write through `v` would change `a`.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return a
	end 'current'
end 'Box'

let a = Box.create()
var v = Box.current()

function main() returns ExitCode
	v.n = 9
	print("a={a.n} v={v.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:15:5: the initializer of the module-level `var` `v` may hand it a record a module-level `let` holds, through `Box.current` — a `let` is fixed at startup and a `var` may write what it holds, so the two may not share a record. Build the value without taking the `let`'s record, or make `v` a `let`
```

<!-- test: error.a-var-initializer-may-not-take-a-record-holding-a-lets-record -->
`around` builds a fresh record, but the record it holds is `a`'s. The `var` field `box` would let `w` write
it, so the store into `box` is the refusal.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

type Wrap
	export var box as Box

	static function around() returns Self
		return Self{box: a}
	end 'around'
end 'Wrap'

let a = Box.create()
var w = Wrap.around()

function main() returns ExitCode
	w.box.n = 9
	print("a={a.n} w={w.box.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:14:15: cannot store 'a', which is read-only (a module-level `let`), into mutable place 'Wrap.box'; use clone()
```

<!-- test: a-var-initializer-may-take-a-record-holding-a-lets-record-in-a-let-field -->
**THE TWIN.** The same wrapper with `box` a `let` field: `w` holds `a`'s record read-only and writes only its own
`count`.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

type Wrap
	export let box as Box
	export var count as Integer

	static function around() returns Self
		return Self{box: a, count: 0}
	end 'around'
end 'Wrap'

let a = Box.create()
var w = Wrap.around()

function main() returns ExitCode
	w.count = 3
	print("a={a.n} w={w.box.n} {w.count}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
a=7 w=7 3
```

<!-- test: error.a-var-initializer-may-not-take-a-lets-record-through-two-calls -->
`current` names no global; the record comes back from `inner`, one call further down.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return Box.inner()
	end 'current'

	static function inner() returns Self
		return a
	end 'inner'
end 'Box'

let a = Box.create()
var v = Box.current()

function main() returns ExitCode
	v.n = 9
	print("a={a.n} v={v.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:19:5: the initializer of the module-level `var` `v` may hand it a record a module-level `let` holds, through `Box.current` — a `let` is fixed at startup and a `var` may write what it holds, so the two may not share a record. Build the value without taking the `let`'s record, or make `v` a `let`
```

<!-- test: error.a-var-initializer-may-not-take-a-lets-record-through-a-witness-dispatch -->
`through` dispatches `box` on a `Source`, and the conformer that answers hands back `a`.
```maxon
interface Source
	function box() returns Box
end 'Source'

type FromLet implements Source
	export var tag as Integer

	static function create() returns Self
		return Self{tag: 1}
	end 'create'

	function box() returns Box
		return a
	end 'box'
end 'FromLet'

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function viaSource() returns Self
		return through(FromLet.create())
	end 'viaSource'
end 'Box'

function through(source Source) returns Box
	return source.box()
end 'through'

let a = Box.create()
var v = Box.viaSource()

function main() returns ExitCode
	v.n = 9
	print("a={a.n} v={v.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:35:5: the initializer of the module-level `var` `v` may hand it a record a module-level `let` holds, through `Box.viaSource` — a `let` is fixed at startup and a `var` may write what it holds, so the two may not share a record. Build the value without taking the `let`'s record, or make `v` a `let`
```

<!-- test: error.a-local-var-may-not-write-through-a-lets-record-a-call-hands-back -->
A local `var` bound to the record a call hands back out of a module-level `let` is refused at the binding.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return a
	end 'current'
end 'Box'

let a = Box.create()

function main() returns ExitCode
	var w = Box.current()
	w.n = 9
	print("w={w.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:17:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'w'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-var-initializer-may-build-fresh-from-a-lets-scalars -->
**THE CONTROL.** `fresh` reads a number out of `a` and builds a record of its own, and a local `var` holding `a`'s
record through a wrapper may write the wrapper's own field.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function fresh() returns Self
		return Self{n: a.n + 1}
	end 'fresh'
end 'Box'

type Wrap
	export let box as Box
	export var count as Integer

	static function around() returns Self
		return Self{box: a, count: 0}
	end 'around'
end 'Wrap'

let a = Box.create()
var b = Box.fresh()

function main() returns ExitCode
	b.n = 9
	var w = Wrap.around()
	w.count = 3
	print("a={a.n} b={b.n} w={w.count} {w.box.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
a=7 b=9 w=3 7
```

<!-- test: error.a-lets-record-a-call-handed-back-may-not-go-to-a-writing-parameter -->
`main` names no `let`, and hands `bump` the record `current` hands back out of `a`.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return a
	end 'current'
end 'Box'

let a = Box.create()

function bump(b Box)
	b.n = 9
end 'bump'

function show() returns Integer
	return a.n
end 'show'

function main() returns ExitCode
	bump(Box.current())
	print("a={show()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3159: <fragment>:25:2: cannot pass the result of 'current', which may share storage with a module-level `let`, to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: error.a-local-var-may-not-write-through-a-lets-record-an-await-hands-back -->
The record reaches `b` through a promise, and a `var` may not hold a module-level `let`'s record.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return a
	end 'current'
end 'Box'

let a = Box.create()

function fetch() returns Box
	return Box.current()
end 'fetch'

function show() returns Integer
	return a.n
end 'show'

function main() returns ExitCode
	var pending = async fetch()
	var b = await pending
	b.n = 9
	print("a={show()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:26:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-local-var-may-not-write-through-a-lets-record-a-map-miss-hands-back -->
`pick` hands back `a`'s own record whenever the lookup misses, through its `otherwise` fallback.
```maxon
type Box
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias BoxIndex = Map with (Integer, Box)

let a = Box.create(7)

function pick(index BoxIndex, k Integer) returns Box
	return try index.get(k) otherwise a
end 'pick'

function show() returns Integer
	return a.n
end 'show'

function main() returns ExitCode
	var index = BoxIndex.create()
	index.upsert(1, value: Box.create(1))
	var b = pick(index, k: 9)
	b.n = 9
	print("a={show()} b={b.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:25:6: cannot assign the result of 'pick', which may share storage with a module-level `let`, to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-var-initializer-may-take-a-number-read-through-a-nested-call -->
**THE CONTROL FOR A NESTED CALL.** `current` hands `copyOf` the `let`'s record, and `copyOf` keeps only its number.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return a
	end 'current'

	static function copyOf(source Box) returns Self
		return Self{n: source.n}
	end 'copyOf'
end 'Box'

let a = Box.create()
var v = Box.copyOf(Box.current())

function main() returns ExitCode
	v.n = 9
	print("a={a.n} v={v.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
a=7 v=9
```

<!-- test: error.a-var-initializer-may-not-take-a-lets-record-a-nested-call-passes-back -->
`pass` hands back the parameter it is given, which is `a`'s record.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return a
	end 'current'

	static function pass(source Box) returns Self
		return source
	end 'pass'
end 'Box'

let a = Box.create()
var v = Box.pass(Box.current())

function main() returns ExitCode
	v.n = 9
	print("a={a.n} v={v.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:19:5: the initializer of the module-level `var` `v` may hand it a record a module-level `let` holds, through `Box.pass` — a `let` is fixed at startup and a `var` may write what it holds, so the two may not share a record. Build the value without taking the `let`'s record, or make `v` a `let`
```

### A managed constant is the WEAKEST claim on `<base>.<member>(…)`

A top-level managed `let` is the first method RECEIVER whose name can also be a declared TYPE, so
`Widget.byteLength()` is ambiguous in a way `c.increment()` never was. The type-based reading is tried
FIRST and the constant is its fallback: 42 here, 10 in the next case.

<!-- test: top-level-let-name-shadowed-by-type -->
```maxon
typealias Integer = int(i64.min to i64.max)

let Widget = "abcdefghij"

type Widget
	export var v as Integer

	static function byteLength() returns Integer
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
reads as the CONSTANT, which is why the rule is a fallback rather than "a type name always wins".

<!-- test: top-level-let-falls-back-when-type-has-no-such-static -->
```maxon
typealias Integer = int(i64.min to i64.max)

let Widget = "abcdefghij"

type Widget
	export var v as Integer

	static function create() returns Widget
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

<!-- test: top-level-let-name-shadowed-by-a-type-with-a-static-var -->
A type and a top-level `let` of one name: `Box.m` is the type's static, read and written, exactly as a
static call on `Box` is, so the immutable `let` is neither blamed nor read.
```maxon
type Box
	static var m = 0 as ExitCode
end 'Box'

let Box = 5 as ExitCode

function main() returns ExitCode
	Box.m = 3
	return Box.m
end 'main'
```
```exitcode
3
```

<!-- test: data-section-bool-global-with-green-threads-runs -->
A program whose own globals end on a 1-byte slot starts the scheduler, whose words it lays out after
them, and runs a coroutine to completion.

```maxon
typealias Integer = int(i64.min to i64.max)

var flag = true

function compute() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 42
end 'compute'

function main() returns ExitCode
	let promise = async compute()
	let result = await promise

	if flag 'read'
		return result
	end 'read'

	return 1
end 'main'
```
```exitcode
42
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
`var`. The struct twin below pins the same arm for a struct `let`.

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
the first declaration keeps the name and the diagnostic names the redeclaration to remove. A duplicate
FUNCTION is rejected the same way, and refusing the duplicate `let` is deliberate.

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
var counter = 0 as FullRangeInt
let counter = 5

function main() returns ExitCode
	return counter
end 'main'
```
```maxoncstderr
error E3006: <fragment>:3:5: duplicate definition of 'counter'
```

<!-- test: error.top-level-let-array-owned-merge-alias -->
The FOURTH door onto the same guard. The three above all reach it as a `var`
BINDING; this one reaches it as a MERGE that must be OWNED because its other arm gives a fresh record —
so the borrowed arm is promoted, and for an aggregate a promotion is an INCREF of the same box. That is
precisely the launder an unmarked incref would perform: the mark is therefore CARRIED across the
promotion rather than the promotion being refused, so the phi that joins the two arms carries it
too and the `var pick` binding is refused where it happens.
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
error E3078: <fragment>:14:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'pick'; use 'let' instead of 'var', or use clone()
```

### The returned-shape sweep — the INLINE CONDITIONAL

⭐⭐ **A `return` WHOSE VALUE IS AN INLINE CONDITIONAL HAS TWO ARMS, AND EITHER MAY BE A GLOBAL'S RECORD.**
`noteReturnedGlobalOrForward` recognises a deliberately narrow set of `return` shapes, and its own tail
states the family rule: an unrecognised shape leaves the function's bit clear, and a clear bit keeps the
E2015 refusal at the return — *"a missed shape costs a REFUSAL, never a silent acceptance"*.

⛔ **THE MISS WAS REACHED, BY THE COMPILER'S OWN SOURCE.** `ConformanceCheck.pairableAssociatedTypeNames` is
`return sharedEmptyDeclaredConformances if boundTypeNames.count() < associatedTypeNames.count() else
associatedTypeNames` — a legal program, refused because the sweep read the shape and recorded nothing.

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
	view short = pairable(a, wanted: b)
	view long = pairable(b, wanted: a)
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

### The shadowing filter — a binder shadows only what comes AFTER it

⛔⛔ **THE FILTER IS POSITION-AWARE, BECAUSE ITS BINDER SET IS GENEROUS.**
`returnedNamesNotBoundHere` drops a returned name that the body binds itself, using a deliberately
GENEROUS binder set — a name is bound when it follows `let`, `var`, `for`, `(` or `,`, which also
catches plain call ARGUMENTS. Over-inclusion drops a seed, and a dropped seed falls back to the E2015
refusal at the return — which is not acceptable when a real program is the one refused.

⭐ **THE COMPILER'S OWN SOURCE REACHES IT.** `SignatureIndex.surfaceOfRootBits` returns
`sharedEmptyDeclaredSurface` BARE on one path — a shape the sweep DOES recognise — and hands that same
name to a call on the next line. A position-blind filter would read the argument as binding the
candidate, drop the seed, and refuse the bare return on legal source.

⭐⭐ **THE FILTER KEYS ON POSITION, NOT SHAPE, AND THE SET STAYS GENEROUS.** Narrowing the binder SET is what the
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
	out.push((base.count() as Integer) + bits)
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

### The returned-shape sweep — A PARAMETER DEFAULT IS THE LANGUAGE'S OTHER `return`

⭐⭐ **A DEFAULT VALUE IS A SYNTHESIZED NULLARY FUNCTION WHOSE ENTIRE BODY IS `return <the expression>`**
(`Parser.parseDefaultHelperBody`), so `f(p T = A)` hands its caller exactly what `f(A)` hands it. The sweep
therefore reads a default's helper as well as `return` STATEMENTS, so the two spellings give one answer
to the question the sweep exists to answer — *may this argument alias a `let`-declared global's record?* —
which is the one thing a default may never differ from the written-out argument in. A sweep that read
`return` statements only would fail in OPPOSITE directions:

⛔ **THE BARE NAME WOULD BE REFUSED, ON THE COMPILER'S OWN SOURCE.** `ParseStaging.maxon:125` declares `opaqueParams
OpaqueParamInfo = sharedNoOpaqueParams` and `Project.maxon:648` declares `payloadStorage MaxonType =
NoPayloadStorage`; both would be E2015 at the helper's return, because the sweep would publish no bit for a
function it never read.

⛔⛔ **AND THE CALL EDGE WOULD BE SILENTLY ACCEPTED, WHICH IS THE HALF A REFUSAL DOES NOT COVER.** A default
that names the global THROUGH AN ACCESSOR (`= shared()`) reaches the helper's return as an already-OWNED
call result, so it never touches the promotion and there is no refusal to fall back on — the mark would
stop at the helper and the caller would get an untainted value: a `grow(names Names = shared())`
whose body pushes would print `a=1 b=2 g=2`, mutating a `let` global, while the written-out `grow(shared())`
one line over is E3159. So this door is a matter of SOUNDNESS and not only of lifting a refusal.

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
half of the rule; the other half is that the refusal is at the write rather than dropped. The
callee pushes, so the omitted argument is refused at the CALL — the same E3159, from the same one
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
error E3159: <fragment>:13:12: cannot pass a value, which may share storage with a module-level `let`, to a function that writes parameter 'names' (in main); pass a clone()
```

<!-- test: a-defaulted-accessor-call-may-not-reach-a-mutating-parameter -->
⭐⭐ **THE CONTROL FOR THE EDGE — AND ITS FAILURE WOULD BE A WRONG ANSWER, NOT A REFUSAL.** Identical to
the case above but for the default naming the global through `shared()`, which is the spelling that would
otherwise compile clean and mutate the global. It is refused for the reason the written-out `grow(shared())` is.

⚠ **CATCHING ONLY A `let` HANDED OVER BY NAME WOULD ACCEPT BOTH THIS PROGRAM AND THE WRITTEN-OUT
`grow(shared())`.** Both are refused, which is the rule's whole stance — the subject is the RECORD, not the
spelling that reached it.
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
error E3159: <fragment>:17:12: cannot pass a value, which may share storage with a module-level `let`, to a function that writes parameter 'names' (in main); pass a clone()
```

<!-- test: a-field-default-naming-a-let-global-is-still-refused -->
⛔⛔ **THE FIELD DEFAULT IS THE SAME HELPER MECHANISM AND IS DELIBERATELY *NOT* ADMITTED, WHICH IS A
DECISION AND NOT AN OVERSIGHT.** Both doors drain through one `parseDefaultHelperBody`, and what separates
them is the KEY it asks under: the sweep publishes a PARAMETER default's helper and not a FIELD's, so this
answers false and the E2015 stands.

⭐ **THE REASON IS WHERE THE RECORD LANDS.** A parameter default hands it to a call ARGUMENT, which is
where the mark is read and where a mutating callee is refused. A field default hands it to a struct SLOT,
and the mark does not survive a field store — `var h = Holder.make(sharedEmptyNames)` storing a
global's record and then `h.names.push(1)` compiles clean and prints `g=1`.

⚠ **THIS IS A HOLE IN THE `let`-GLOBAL RULE AS A WHOLE, AND NOT A DEFECT OF ONE ARM** — and that is
precisely why the door stays shut rather than being widened. The rule is deliberately STRICT about a global's record reaching
a mutating position (the case above is one), so publishing the field helper would open a route into the one part of the rule that
cannot yet follow the record. A refusal is the direction this rule may err in; a silent acceptance is
not. It is pinned here so the gap is visible rather than assumed, and it comes off the day a field store
carries the mark.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Names = Array with Integer

let sharedEmptyNames = Names.create()

type Holder
	export var names = sharedEmptyNames as Names

	static function make() returns Holder
		return Self{}
	end 'make'
end 'Holder'

function main() returns ExitCode
	print("n={Holder.make().names.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:21: Unsupported: returning a read of a `let`-declared top-level global — an aggregate has no owning COPY in the compiler, so the returned value would alias the SAME record and a write through it would mutate a global declared immutable; read it through a `let` binding, or declare the global `var`
```

### `<Type> from "…"` AS A TOP-LEVEL INITIALIZER

The compiler's own `Compiler/Project.maxon` declares `export let CompilerOwnedDeclFilePath = FilePath from ""`.
The initializer is the body's `FilePath from ""`: a call to the type's own `init` static, which
`__module_init` makes before `main` as it does for `Database.create(…)`.

⛔⛔ **AND THE CONFORMANCE IS CHECKED.** **A `type Tag` with an `init` static and NO
`implements InitableFromStringLiteral`** is `E3005 Type 'Tag' does not conform to InitableFromStringLiteral`
at file scope, with the code and sentence the body spelling gets. A silent acceptance is the costly
direction: the record would be built by a type that never declared it can be.

<!-- test: a-top-level-let-built-by-a-string-literal-init -->
Both a `""` and a non-empty literal, so the argument is carried and not merely accepted.
```maxon
interface InitableFromStringLiteral
end 'InitableFromStringLiteral'

type Tag implements InitableFromStringLiteral
	export let text as String

	static function init(text String) returns Self
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

<!-- test: a-top-level-let-built-by-a-string-literal-init-of-a-library-type-whose-init-is-not-exported -->
`stdlib/FilePath.maxon` declares its `init` static without `export`, because the construction reaches it through the type's conformance and the author never names it. The global's initializer is the compiler's call to it, so it is not refused for a file that cannot name `init`.
```maxon
let source = FilePath from "input.txt"

function main() returns ExitCode
	print("{source.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
input.txt
```

<!-- test: error.a-top-level-string-literal-init-still-needs-the-conformance -->
⭐ **THE CONTROL, AND IT IS THE HALF A SILENT ACCEPTANCE WOULD COST.** The only difference from the
case above is the missing `implements` clause. Same code, same sentence, same anchor as the body
spelling.
```maxon
type Tag
	export let text as String

	static function init(text String) returns Self
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
error E3005: specs/static-variables/error.a-top-level-string-literal-init-still-needs-the-conformance.maxon:10:16: Type 'Tag' does not conform to InitableFromStringLiteral
```

### A PAYLOAD-FREE ENUM CASE IS A CONSTANT

⭐⭐ **ITS VALUE IS ITS TAG — a number fixed at declaration — so it folds like any other.** The compiler's
own backends declare six such constants: `let JumpTableBaseReg = X64Register.r10`
(`Targets/X64/X64Backend.maxon:1135`), `let GtSwitchFromReg = X64Register.rcx`, and the four register
tables written as array literals.

⚠ **THE NUMBER ALONE IS A WRONG ANSWER ABOUT IT, WHICH IS THE WHOLE OF WHY THIS IS NOT A ONE-LINE FOLD.**
A constant folded to a bare `integer` cannot be passed where the enum is declared —
`argument type mismatch for 'r': expected 'Reg', got 'int'`. So the fold is tagged `named` and carries the
enum's declared layout NAME — its identity, not a spelling a file wrote — which then rides the whole chain a
constant travels: `ConstValue` → `TopLevelConstant` → `ConstEvalOutcome` → `TopLevelConstantLookup` → the
use site. Each of those arms is WIDENED rather than
joined by a parallel one: a folded enum case is not a second kind of scalar constant, it is a scalar
constant that names its type.

⚠ **BYTES, NOT AN INTERNED ID.** Ids are minted per PARSER; the constant is folded by a throwaway parser
built per declaration and the use site is a third one, so an id minted during the fold means nothing where it is
read. The reader interns the bytes into its own table — `fieldTypeOf(…, into:)`'s discipline.

⚠ **AND THE USE SITE EMITS THROUGH `emitEnumTagLiteral`, THE DOOR THE BODY SPELLING TAKES**: the value is
TAGGED `named(E)` while the op it emits carries `integer`. Emitting the literal AT `MaxonType.named`
instead is a compiler panic — *"a `named` type must be resolved to a primitive before lowering"* — because
the op's valueType is a STORAGE question and `named` is not an answer to it.

⭐⭐ **A BOXED UNION'S PAYLOAD-FREE CASE IS A CONSTANT TOO — AND THE KEYWORD DECIDES WHERE ITS BOX LIVES.**
`isBoxed` means SOME case carries a payload, and then EVERY case — payload-free ones included — is a box
rather than a bare tag; the compiler's own `SemanticCheck.maxon:719` says so about its own binding (*"a payload-free
union case is still a heap object in Maxon"*). But that settles what the value IS, never where it comes
from, and the two keywords answer that differently:

  * a **`var`** is BUILT: `__module_init` allocates `boxSizeBytes()` bytes before `main`, stores the tag at
    offset 0, ZEROES the payload slots, and writes the box into the `.data` slot; `__maxon_global_cleanup`
    releases it. That is the path an empty container and a `create()`-style factory already took.
  * a **`let` is IMAGE DATA** (⚖ *a `let` is image data*): the record holds a tag and nothing else, so the
    compiler lays those bytes down in `.rdata` behind an immortal allocation header
    (`ProgramSignatures.boxedUnionCaseImageBytes`). No `.data` slot, no `__module_init` construction, no
    cleanup drop, and every incref and decref reaching it is sent home by `MmRuntime.emitBoxIsImmortal`.

⭐ **THE ADMISSION READS THE CASE, NOT THE UNION** (`boxedUnionCaseIsImageData`). A union whose OTHER case
carries an `Array` is still imageable at the cases that carry nothing, because a `let` bound to one can never
become the other. A case that DOES declare a payload is a construction and keeps the built path.

⛔ **SO EVERY `let` CASE IN THIS SECTION MEASURES THE IMAGE, AND ITS `var` TWIN MEASURES THE BUILD.** Read
each pair as one claim: the twin is not a duplicate, it is the only case on this page that still reaches
`ModuleInit.builtUnionCaseBox` at all.

⛔ The one refusal here is a case the union does not declare.

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
⭐⭐ **THE BOXED-UNION CONTROL.**
`Boxed` has a payload-carrying case, so `plain` is a box rather than a bare tag; it is a
constant all the same, and as a `let` its box is laid down in `.rdata` rather than folded to a number.
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
either side of it, so an image that stamped the wrong case's tag answers something other than 20 — and one
that folded the constant to a bare tag instead of laying a box down would hand `match` the number 1 to
dereference. The tag is read off the layout at one door (`unionCaseLayoutOf`), which is the same door
`__module_init` builds a `var`'s box through, so the two spellings of this case cannot disagree about which
number goes in.

Reading the same `let` three times must give ONE answer. It is not a slot load: each read is a `lea` at the
one `.rdata` object the image table pooled, so "one answer" here means the reads reach ONE record — the
property byte-dedup buys and a per-read rematerialization would lose.
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
⭐ **THE SHAPE THE COMPILER'S OWN SOURCE USES, AND WHAT AN IMAGED SENTINEL COSTS TO SHARE: NOTHING.** One
payload-free sentinel read and stored many times is exactly `Parser.sharedUnreadParamType` and
`Project.NoPayloadStorage` — a single record for the whole program instead of one allocation per site.

⚠ **THE EXIT CODE HERE IS NOT A REFCOUNT GATE, AND SAYING IT WAS IS WHAT MADE THIS CASE STOP MEASURING ITS
OWN SUBJECT.** A `let` of this shape is image data: there is no `.data` slot, no `__module_init` box and no
cleanup drop, and the `__mm_incref` each `push` emits into the array is routed straight home by
`emitBoxIsImmortal` — so no incref/decref pair EXISTS here to be unbalanced, and a sabotage of one could not
turn this program red. What it does hold is that the co-ownership road is TAKEN and is harmless on an
immortal box: four pushes into a durable `Array`, a read back out of it, and the array's own destructor
running over four slots that all name one read-only record.

⭐⭐ **THE BALANCE IS THE `var` TWIN'S, DIRECTLY BELOW, AND IT IS THE ONLY CASE ON THIS PAGE THAT CAN SEE
ONE.** Read the two together: this case says an imaged sentinel is free to share, the twin says a built one
is released exactly once.
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
	view first = try xs.get(0) otherwise Boxed.plain
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

<!-- test: a-boxed-union-var-shared-across-many-reads-frees-cleanly -->
⭐⭐ **THE `var` TWIN, AND THE EXIT CODE IS THE GATE.** The identical program with one keyword changed. A
`var` is never imaged — `settleRecordValue` writes `hasStorage = decl.mutable or not recordShapeHasAnImage(…)`
— so this binding keeps the whole built path the `let` above does not have: `__module_init` allocates the
box, stores the tag, zeroes the payload slots and writes the pointer into a `.data` slot, and
`__maxon_global_cleanup` drops it after `main`.

⛔ **AND THE REFCOUNTS ARE REAL HERE, WHICH IS THE WHOLE REASON THIS CASE EXISTS.** The slot owns one
reference; every `push` CO-OWNS the same box through a `__mm_incref` that actually steps a counter; the
array's destructor releases four; the cleanup releases the slot's. Those have to balance, and an unbalanced
pair is a leak (exit 101) or a double free — neither of which the stdout comparison can see. With
one extra `__mm_incref` in `ModuleInit.builtUnionCaseBox`, **this case exits 101 and the `let` case above
does not move**, which is exactly the asymmetry the pair is here to state.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias BoxedArray = Array with Boxed

union Boxed
	plain
	withPayload(n Integer)
end 'Boxed'

var sentinel = Boxed.plain

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
	view first = try xs.get(0) otherwise Boxed.plain
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

<!-- test: error.a-boxed-union-constant-cannot-be-read-through-a-payload-less-alternative -->
An `or` pattern binds a payload only on the alternative that names it. `none or some(n)` reads `n` on the
`none` path too, where no pattern bound it, so the arm is refused (E3129) — for a union constant as for any
other scrutinee. Give `none` an arm of its own.
```maxon
typealias Integer = int(i64.min to i64.max)

union Counter
	none
	some(n Integer)
end 'Counter'

let sentinel = Counter.none

function valueOf(c Counter) returns Integer
	return match c 'k'
		none or
			some(n) gives n
	end 'k'
end 'valueOf'

function main() returns ExitCode
	return valueOf(sentinel) as ExitCode
end 'main'
```
```maxoncstderr
error E3129: <fragment>:14:9: the payload binding 'n' cannot be honoured: case 'none' of `union Counter` binds nothing at payload slot 0, so on that case's path the arm body would read a value no pattern bound. Give 'none' an arm of its own
```

<!-- test: error.a-boxed-union-var-cannot-be-read-through-a-payload-less-alternative -->
The same refusal for a `var` global holding a boxed union.
```maxon
typealias Integer = int(i64.min to i64.max)

union Counter
	none
	some(n Integer)
end 'Counter'

var sentinel = Counter.none

function valueOf(c Counter) returns Integer
	return match c 'k'
		none or
			some(n) gives n
	end 'k'
end 'valueOf'

function main() returns ExitCode
	return valueOf(sentinel) as ExitCode
end 'main'
```
```maxoncstderr
error E3129: <fragment>:14:9: the payload binding 'n' cannot be honoured: case 'none' of `union Counter` binds nothing at payload slot 0, so on that case's path the arm body would read a value no pattern bound. Give 'none' an arm of its own
```

<!-- test: error.a-case-the-union-does-not-declare-is-still-refused -->
⛔ **THE BOUNDARY THAT REMAINS.** Admitting a boxed union's cases does not admit any member name at all:
`noSuchCase` is not a case of `Boxed`, so it is reported where the author wrote it with the diagnostic a body
gives (E3034, *unknown union case*). This refusal is a RULE, which is the whole reason it is the one kept as the
control.
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
error E3034: <fragment>:9:20: unknown union case: 'noSuchCase'
```

<!-- test: error.a-case-the-union-does-not-declare-is-refused-in-a-body -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Boxed
	plain
	withPayload(n Integer)
end 'Boxed'

function main() returns ExitCode
	let stillRefused = Boxed.noSuchCase
	return match stillRefused 'k'
		plain gives 0
		withPayload(n) gives n as ExitCode
	end 'k'
end 'main'
```
```maxoncstderr
error E3034: <fragment>:10:21: unknown union case: 'noSuchCase'
```

### AN ARRAY LITERAL OF ENUM CASES

⭐⭐ **AND SO IS AN ARRAY OF THEM.** The scalar half above folds `let JumpTableBaseReg = X64Register.r10`;
this is the same fact one level down — `let calleeSavedOrder = [X64Register.rbx, X64Register.r12, …]`
(`Targets/X64/X64PrologueEpilogue.maxon:117`), which is how three of the compiler's own backends write their
register tables, plus the lexer's 256-entry `charClassTable`.

⚠ **THE ELEMENT CARRIES THE ENUM'S NAME AND THE KIND DOES NOT, which is not an arbitrary split.** The KIND
says which FAMILY the array belongs to; the enum's name lives on the ELEMENT because that is where the
walk has it. `constArrayValueType` reads it off element 0 — the first element fixes the instance — and the
homogeneity test compares the name as well as the kind.

⛔ **A KIND COMPARISON ALONE WOULD ACCEPT A PROGRAM WITH NO TYPE.**
A test of `constArrayElementKindOf(elem) != element` alone is a KIND comparison:
`[Reg.rcx, CharClass.tab]` is two enums and ONE kind, so it would be accepted and then typed from
whichever element the walk saw first. `constArrayElementsAgree` compares the name for the enum kind and
for no other, because no other element kind has one.

⛔⛔ **AND THE CONTENT HASH NEEDS THE NAME TOO.** An array global's readers depend on its LABEL and its
element TYPE and deliberately NOT on its values — so the elements are not hashed, and without the name
`[Reg.rcx]` and `[CharClass.other]` have the same kind and the same label, hash identically, and a file
edited from one to the other is answered from the other's parse. Its two neighbours can hash a shape alone
because their kind IS their element type; `enumCase` is a family.

<!-- test: an-array-literal-of-enum-cases-is-a-top-level-constant -->
Read three ways — iterated, counted, and indexed — because the instance is what the element name decides and each
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
	var total = 0 as Integer
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
⭐ **THE CASE A KIND-ONLY COMPARISON WOULD LET THROUGH.** Two enums, one kind. Accepted, this array
would be typed `Array with Reg` and hold a `CharClass` in its second slot.
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
error E3005: specs/static-variables/error.an-array-literal-may-not-mix-two-enums.maxon:12:23: argument type mismatch for 'value': expected 'Reg', got 'CharClass'
```

### A STATIC MEMBER'S VISIBILITY IS THE MEMBER'S, NOT ITS TYPE'S

<!-- test: error.module-scoped-static-read-different-directory -->
`module` on a static member scopes that member to the declaring directory's subtree while the type
around it stays exported and nameable everywhere. A reader outside the subtree may therefore write
`Config` and must still be refused `used`, at the member token — the base is not the problem here and a
sentence about it sends the author to a name that is perfectly visible.

```maxon
// --- file: dir_a/config.maxon
export type Config
	module static var used = 42 as FullRangeInt
end 'Config'

// --- file: dir_b/main.maxon
function main() returns ExitCode
	return Config.used
end 'main'
```
```maxoncstderr
error E3088: dir_b/specs/static-variables/error.module-scoped-static-read-different-directory.maxon:9:16: static 'Config.used' is module-scoped and not visible from this directory
```

<!-- test: error.static-bound-name-on-a-struct-base -->
⭐ **THE ONE SHAPE WHERE THE DOOR MUST CHOOSE.** The member is spelled like a numeric bound, but the
base is a type the author declared and a declared type has a static roster to answer from. The roster
wins: the honest sentence is about what `Box` does not have, and it is anchored on the member.

⚠ Answered as a bound, the refusal lands on the BASE token and reads `Expected 'a sized numeric type
(u8/u16/u32/u64/i8/i16/i32/i64/f32/f64)' but got 'Box'` — a demand the author
never made of a name that resolved perfectly well. Nothing in either suite pins the alternative, which
is why this case exists.

⚠ A ranged ALIAS base is a different question and keeps the bound reading;
`specs/type-name-collision.md` holds that case and this arm deliberately does not claim it.
```maxon
typealias Num = int(0 to 100)

type Box
	export var n as Num
end 'Box'

function main() returns ExitCode
	return Box.min
end 'main'
```
```maxoncstderr
error E2073: <fragment>:9:13: type 'Box' has no static member named 'min'
```

<!-- test: global-union-array-literal -->
A union case's payload operand is read only by the enum-construct op, so a dead-op scan that
cannot see that read deletes the literal the case was built from.
```maxon
typealias Val = int(i64.min to i64.max)

union Op
	add(value Val)
	nop
end 'Op'

var globalOps = [Op.add(7), Op.nop]

function main() returns ExitCode
	view first = try globalOps.get(0) otherwise Op.nop
	return match first 'check'
		add(v) gives v
		nop gives 99
	end 'check'
end 'main'
```
```exitcode
7
```

<!-- test: global-bytestring-map-with-union-array -->
Neither ingredient alone exposes the hole: the map supplies a live global whose producer chain
runs through module init, and the union supplies the unscanned payload read.
```maxon
typealias Kind = int(0 to 100)
typealias Val = int(i64.min to i64.max)

type KeywordInfo
	export var kind as Kind
	export var helpText as String

	static function create(kind Kind, helpText String) returns KeywordInfo
		return Self{kind: kind, helpText: helpText}
	end 'create'
end 'KeywordInfo'

union Op
	add(value Val)
	nop
end 'Op'

typealias OpArray = Array with Op

var keywordMap = [
	b"if": KeywordInfo.create(1, helpText: "Conditional statement."),
	b"else": KeywordInfo.create(2, helpText: "Alternative branch.")
]

function main() returns ExitCode
	var ops = OpArray.create()
	ops.push(Op.add(1))
	ops.push(Op.nop)

	view info = try keywordMap.get(b"if") otherwise KeywordInfo.create(0, helpText: "")
	if info.kind != 1 'badLookup'
		return 1
	end 'badLookup'

	return 0 if ops.count() == 2 and keywordMap.count() == 2 else 2
end 'main'
```
```exitcode
0
```

<!-- test: global-map-with-union-values -->
The enum-construct sits directly inside the map literal's value buffer, so the payload read and
the map's element-assign chain are pruned by one and the same pass.
```maxon
typealias Val = int(i64.min to i64.max)

union Op
	add(value Val)
	nop
end 'Op'

var opMap = [
	b"add": Op.add(5),
	b"nop": Op.nop
]

function main() returns ExitCode
	view op = try opMap.get(b"add") otherwise Op.nop
	return match op 'check'
		add(v) gives v
		nop gives 99
	end 'check'
end 'main'
```
```exitcode
5
```

<!-- test: live-global-survives-dead-global-pruning -->
A dead global is what makes the pruning pass run at all, and the pass then walks the whole module
init block — so the live global's byte-string keys and interpolated strings, all pure producers,
are exposed to the same scan.
```maxon
typealias Kind = int(0 to 100)

type KeywordInfo
	export var kind as Kind
	export var helpText as String

	static function create(kind Kind, helpText String) returns KeywordInfo
		return Self{kind: kind, helpText: helpText}
	end 'create'
end 'KeywordInfo'

function describe(n Kind) returns String
	return "kind {n}"
end 'describe'

var keywordMap = [
	b"if": KeywordInfo.create(1, helpText: describe(1)),
	b"else": KeywordInfo.create(2, helpText: describe(2))
]

var unusedNames = StringArray.create()

function main() returns ExitCode
	view info = try keywordMap.get(b"else") otherwise KeywordInfo.create(0, helpText: "")
	if info.helpText != "kind 2" 'badHelp'
		return 1
	end 'badHelp'

	return 0 if keywordMap.count() == 2 and info.kind == 2 else 2
end 'main'
```
```exitcode
0
```

<!-- test: module-union-store-then-match -->
A store of a payload-carrying case into a module-level union var. The wrong answer is silent: the
match reads the slot's ordinal rather than the box's tag and gives 0.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.unowned

function seed()
	hold = Hold.owned(5)
end 'seed'

function main() returns ExitCode
	seed()
	return match hold 'm'
		unowned gives 0
		owned(v) gives v
	end 'm'
end 'main'
```
```exitcode
5
```

<!-- test: module-union-two-storing-functions -->
Two functions each store a different case, so a stale value id carried between them has no
definition in its own function and surfaces out of the register allocator rather than as an answer.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.unowned

function seed()
	hold = Hold.owned(5)
end 'seed'

function clear()
	hold = Hold.unowned
end 'clear'

function main() returns ExitCode
	clear()
	seed()
	return match hold 'm'
		unowned gives 0
		owned(v) gives v
	end 'm'
end 'main'
```
```exitcode
5
```

<!-- test: module-union-read-initializer -->
The declared initializer reaches `main` neither read nor written on the way, so the tag read must
find the case the declaration named.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.owned(9)

function main() returns ExitCode
	return match hold 'm'
		unowned gives 0
		owned(v) gives v
	end 'm'
end 'main'
```
```exitcode
9
```

<!-- test: module-union-reassign-releases-old -->
Each store to the global releases the previous occupant; a loop of them leaks otherwise, and the
suite fails the run on a leaked allocation.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.unowned

function main() returns ExitCode
	var i = 0 as Small
	while i < 10 'fill'
		hold = Hold.owned(i)
		i = i + 1
	end 'fill'

	return match hold 'm'
		unowned gives 0
		owned(v) gives v
	end 'm'
end 'main'
```
```exitcode
9
```

<!-- test: module-union-payload-free-union-stays-scalar -->
A union whose cases all lack payloads is a bare discriminant rather than a box, so its global
keeps the compile-time-constant ordinal — the control for the boxed cases above.
```maxon
union Mode
	off
	on
end 'Mode'

var mode = Mode.off

function seed()
	mode = Mode.on
end 'seed'

function main() returns ExitCode
	seed()
	return match mode 'm'
		off gives 0
		on gives 6
	end 'm'
end 'main'
```
```exitcode
6
```

<!-- test: module-enum-stays-scalar -->
A plain enum global folds to its ordinal, which is the second control: nothing about the union
boxing may reach a type that never had a payload to box.
```maxon
enum Color
	red
	green
end 'Color'

var c = Color.red

function seed()
	c = Color.green
end 'seed'

function main() returns ExitCode
	seed()
	return match c 'm'
		red gives 0
		green gives 7
	end 'm'
end 'main'
```
```exitcode
7
```

<!-- test: module-union-in-monomorphized-method -->
Monomorphization rebuilds every op in the body, so the slot's type name must travel with the
rebuilt load and store; without it the specialization alone lowers the global as a bare integer
and the tag read dereferences it.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.unowned

type Cell uses T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function stash(v Small)
		hold = Hold.owned(v)
	end 'stash'

	function readBack() returns Small
		return match hold 'r'
			unowned gives 0
			owned(v) gives v
		end 'r'
	end 'readBack'
end 'Cell'

typealias SmallCell = Cell with Small

function main() returns ExitCode
	let c = SmallCell.create(1)
	c.stash(5)
	return c.readBack()
end 'main'
```
```exitcode
5
```

⭐⭐ **A CONSTANT'S TAG IS NOT ITS IDENTITY.** `named` covers every declared `enum` AND every boxed `union`;
`genericInstance` covers every container. A slot check that compared only the tag would admit a `Status` case
into a `Colour` field and a `Map` box into an `Array` field. Both cases below are `E3005`, reported where and
as the same call in a function body reports it — at the factory's name — and each is followed by that body
twin.

<!-- test: error.const-fold-union-case-into-enum-field -->
```maxon
typealias Num = int(0 to 1000)

enum Colour
	red
	green
end 'Colour'

union Status
	idle
	busy(n Num)
end 'Status'

type Mixed
	export var c as Colour
	export var n as Num

	static function create(c Colour, n Num) returns Mixed
		return Self{c: c, n: n}
	end 'create'
end 'Mixed'

let Bad = Mixed.create(Status.idle, n: 4)

function main() returns ExitCode
	return Bad.n as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:23:17: argument type mismatch for 'c': expected 'Colour', got 'Status'
```

<!-- test: error.union-case-into-enum-field-in-a-function-body -->
```maxon
typealias Num = int(0 to 1000)

enum Colour
	red
	green
end 'Colour'

union Status
	idle
	busy(n Num)
end 'Status'

type Mixed
	export var c as Colour
	export var n as Num

	static function create(c Colour, n Num) returns Mixed
		return Self{c: c, n: n}
	end 'create'
end 'Mixed'

function main() returns ExitCode
	let Bad = Mixed.create(Status.idle, n: 4)
	return Bad.n as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:24:18: argument type mismatch for 'c': expected 'Colour', got 'Status'
```

The container half of the same rule. A `Map`'s box and an `Array`'s record are both `genericInstance`, and
they are not the same record at all: read back through the array surface, `H.xs.count()` loads a `Map` box's
second word as a `__ManagedMemory` length.

<!-- test: error.const-fold-map-into-array-field -->
```maxon
typealias Num = int(0 to 1000)
typealias NumArray = Array with Num
typealias Counts = Map with (String, Num)

type Holder
	export var xs as NumArray

	static function create(xs NumArray) returns Holder
		return Self{xs: xs}
	end 'create'
end 'Holder'

let H = Holder.create(Counts.create())

function main() returns ExitCode
	return H.xs.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:16: argument type mismatch for 'xs': expected 'NumArray', got 'Counts'
```

<!-- test: error.map-into-array-field-in-a-function-body -->
```maxon
typealias Num = int(0 to 1000)
typealias NumArray = Array with Num
typealias Counts = Map with (String, Num)

type Holder
	export var xs as NumArray

	static function create(xs NumArray) returns Holder
		return Self{xs: xs}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let H = Holder.create(Counts.create())
	return H.xs.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:17: argument type mismatch for 'xs': expected 'NumArray', got 'Counts'
```

<!-- test: error.let-global-alias-receives-a-self-writing-container-method-on-a-field -->
The direct `G.items.push(1)` is refused; a `let` naming `G` is the same record, so pushing through it is
refused the same way.
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Holder
	export var items as CountArray

	static function make() returns Holder
		return Holder{items: CountArray.create()}
	end 'make'
end 'Holder'

let G = Holder.make()

function main() returns ExitCode
	let v = G
	v.items.push(1)
	return G.items.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:17:10: cannot write through 'v.items': 'v' is read-only (a module-level `let`) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.let-global-field-passed-to-a-writing-callee -->
`bump(G)` is refused; a field of `G` is `G`'s own graph, so handing it to a callee that writes it is
refused too.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static function make() returns Box
		return Box{n: 1}
	end 'make'
end 'Box'

type Holder
	export var inner as Box

	static function make() returns Holder
		return Holder{inner: Box.make()}
	end 'make'
end 'Holder'

let G = Holder.make()

function bump(b Box)
	b.n = 99
end 'bump'

function main() returns ExitCode
	bump(G.inner)
	return G.inner.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:27:2: cannot pass 'G', which is read-only (a module-level `let`), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: error.let-global-alias-field-passed-to-a-writing-callee -->
The same field reached through a `let` naming `G`.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static function make() returns Box
		return Box{n: 1}
	end 'make'
end 'Box'

type Holder
	export var inner as Box

	static function make() returns Holder
		return Holder{inner: Box.make()}
	end 'make'
end 'Holder'

let G = Holder.make()

function bump(b Box)
	b.n = 99
end 'bump'

function main() returns ExitCode
	let v = G
	bump(v.inner)
	return G.inner.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:28:2: cannot pass 'v', which is read-only (a module-level `let`), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: error.let-global-field-receives-a-self-writing-method -->
A method that writes its own receiver, called on a field of `G`, writes `G`'s graph.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

type Holder
	export var inner as Box

	static function make() returns Holder
		return Holder{inner: Box.make()}
	end 'make'
end 'Holder'

let G = Holder.make()

function main() returns ExitCode
	G.inner.bump()
	return G.inner.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:27:10: cannot pass 'G', which is read-only (a module-level `let`), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: error.let-global-alias-field-receives-a-self-writing-method -->
The same method call through a `let` naming `G`.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

type Holder
	export var inner as Box

	static function make() returns Holder
		return Holder{inner: Box.make()}
	end 'make'
end 'Holder'

let G = Holder.make()

function main() returns ExitCode
	let v = G
	v.inner.bump()
	return G.inner.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:28:10: cannot pass 'v', which is read-only (a module-level `let`), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: error.let-global-container-field-passed-to-a-pushing-callee -->
A container field of `G` handed to a callee that pushes into it.
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Holder
	export var items as CountArray

	static function make() returns Holder
		return Holder{items: CountArray.create()}
	end 'make'
end 'Holder'

let G = Holder.make()

function fill(xs CountArray)
	xs.push(1)
end 'fill'

function main() returns ExitCode
	fill(G.items)
	return G.items.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:20:2: cannot pass 'G', which is read-only (a module-level `let`), to a function that writes parameter 'xs' (in main); pass a clone()
```

<!-- test: error.let-global-alias-container-field-passed-to-a-pushing-callee -->
The same container field reached through a `let` naming `G`.
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Holder
	export var items as CountArray

	static function make() returns Holder
		return Holder{items: CountArray.create()}
	end 'make'
end 'Holder'

let G = Holder.make()

function fill(xs CountArray)
	xs.push(1)
end 'fill'

function main() returns ExitCode
	let v = G
	fill(v.items)
	return G.items.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:21:2: cannot pass 'v', which is read-only (a module-level `let`), to a function that writes parameter 'xs' (in main); pass a clone()
```

<!-- test: error.let-binding-of-a-let-global-field-receives-a-self-writing-method -->
A `let` binding's own record takes a self-writing method, but a `let` bound to a field of `G` holds
`G`'s record, which nothing may write.
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

type Holder
	export var inner as Box

	static function make() returns Holder
		return Holder{inner: Box.make()}
	end 'make'
end 'Holder'

let G = Holder.make()

function main() returns ExitCode
	let i = G.inner
	i.bump()
	return G.inner.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:28:4: cannot pass 'i', which is read-only (a module-level `let`), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: error.a-let-global-stored-into-a-var-field-is-refused-at-the-store -->
A `var` field may be written by any function the record reaches, and the function that writes it cannot
see where the field's value came from. So storing `G`'s record into a `var` field is refused at the store:
`adopt` puts `G` into `b.cfg`, and `poke`, which knows only a `Bag`, would write `G.level`.
```maxon
typealias Count = int(0 to u64.max)

type Config
	export var level as Count

	static function make() returns Config
		return Config{level: 1}
	end 'make'
end 'Config'

type Bag
	export var cfg as Config

	static function make() returns Bag
		return Bag{cfg: Config.make()}
	end 'make'
end 'Bag'

let G = Config.make()

function adopt(b Bag)
	b.cfg = G
end 'adopt'

function poke(b Bag)
	b.cfg.level = 99
end 'poke'

function main() returns ExitCode
	var b = Bag.make()
	adopt(b)
	poke(b)
	return G.level as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:23:4: cannot store 'G', which is read-only (a module-level `let`), into mutable place 'Bag.cfg'; use clone()
```

<!-- test: a-clone-of-a-let-global-stored-into-a-var-field-may-be-written -->
The same program storing a clone: `poke` writes the copy, and `G` keeps its level.
```maxon
typealias Count = int(0 to u64.max)

type Config
	export var level as Count

	static function make() returns Config
		return Config{level: 1}
	end 'make'
end 'Config'

type Bag
	export var cfg as Config

	static function make() returns Bag
		return Bag{cfg: Config.make()}
	end 'make'
end 'Bag'

let G = Config.make()

function adopt(b Bag)
	b.cfg = G.clone()
end 'adopt'

function poke(b Bag)
	b.cfg.level = 99
end 'poke'

function main() returns ExitCode
	var b = Bag.make()
	adopt(b)
	poke(b)
	print("{G.level} {b.cfg.level}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 99
```

<!-- test: error.a-let-initializer-may-not-read-a-managed-var-beside-a-spawn -->
**A `let` THAT HOLDS WHAT A `var` OWNS CANNOT BE MARKED SHARED.** `l` is the record `g` owns, so marking `l`'s
graph before `main` would meet an owner no `let` holds. The program is refused at compile time rather than
stopping before `main` runs.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return g
	end 'current'
end 'Box'

var g = Box.create()
let l = Box.current()

type Reader
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function read() returns Integer
		self.asked = self.asked + 1
		return l.n
	end 'read'
end 'Reader'

function main() returns ExitCode
	let h = spawn Reader.create()
	let n = try await h.read() otherwise 0
	print("n={n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3165: <fragment>:10:10: the initializer of the `let` `l` reaches the module-level `var` `g` here — a `let` is fixed at startup and may not hold what a `var` owns, and the compiler does not follow what the initializer keeps of `g`. Make `l` a `var`, or build its value without reading `g`
note: <fragment>:14:5: `g` is declared here
```

<!-- test: error.a-var-initializer-may-not-take-a-lets-record-beside-a-spawn -->
**AND A `var` MAY NOT TAKE WHAT A `let` HOLDS.** `v` would be an owner of `a`'s record that no `let` is, so marking
`a` shared before `main` would meet it. Refused at compile time.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return a
	end 'current'
end 'Box'

let a = Box.create()
var v = Box.current()

type Reader
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function read() returns Integer
		self.asked = self.asked + 1
		return a.n
	end 'read'
end 'Reader'

function main() returns ExitCode
	let h = spawn Reader.create()
	let n = try await h.read() otherwise 0
	print("n={n} v={v.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:15:5: the initializer of the module-level `var` `v` may hand it a record a module-level `let` holds, through `Box.current` — a `let` is fixed at startup and a `var` may write what it holds, so the two may not share a record. Build the value without taking the `let`'s record, or make `v` a `let`
```

<!-- test: error.a-top-level-var-bound-to-a-bare-integer -->
A top-level `var` initialized with a bare integer literal is refused at the literal.
```maxon
var Hits = 0

function main() returns ExitCode
	Hits = Hits + 1
	return Hits
end 'main'
```
```maxoncstderr
error E2004: <fragment>:2:12: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: error.a-static-var-bound-to-a-bare-integer -->
A `static var` initialized with a bare integer literal is refused at the literal.
```maxon
type Counter
	static var hits = 7
end 'Counter'

function main() returns ExitCode
	Counter.hits = Counter.hits + 1
	return Counter.hits
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:20: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: error.a-top-level-var-bound-to-a-map-of-bare-numbers -->
A top-level `var` initialized with a map literal of bare numbers is refused at the bracket.
```maxon
var Scores = [1: 10, 2: 20]

function main() returns ExitCode
	Scores.upsert(3, value: 30)
	return Scores.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:2:14: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: a-top-level-var-map-with-cast-values-reads-back-typed -->
A cast names its type at file scope too, so a top-level `var` map built from `as Count` values is a typed map that can grow and reads back as `Count`.
```maxon
var Tally = [b"one": 1 as Count, b"two": 2 as Count]

function show(v Count)
	print("{v}\n")
end 'show'

function main() returns ExitCode
	let one = try Tally.get(b"one") otherwise 0
	show(one)
	Tally.upsert(b"three", value: 3)
	print("{Tally.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
3
```

<!-- test: error.a-top-level-var-map-values-keep-their-alias -->
The alias a file-scope cast names is the column's type, so a read of it does not pass where another alias is declared.
```maxon
typealias Score = int(0 to 100)

var Tally = [b"one": 1 as Count]

function show(v Score)
	print("{v}\n")
end 'show'

function main() returns ExitCode
	let one = try Tally.get(b"one") otherwise 0
	show(one)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:2: argument type mismatch for 'v': expected 'Score', got 'Count'
```

<!-- test: error.a-static-var-adopts-the-brand-of-its-first-branded-store -->
A `static var` initialized from a literal carries no brand until the first branded value is stored into it; a store of another brand is refused.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

type Registry
	static var items = ["a"]
end 'Registry'

function main() returns ExitCode
	Registry.items = Xs.create()
	Registry.items = Ys.create()
	print("{Registry.items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: cannot assign a value of type 'Ys' to global 'Registry.items', which holds 'Xs'
```

<!-- test: error.a-read-of-an-adopted-static-var-reaches-only-its-own-brands-parameter -->
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

type Registry
	static var items = ["a"]
end 'Registry'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

function main() returns ExitCode
	Registry.items = Xs.create()
	takeYs(Registry.items)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:2: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: a-static-var-keeps-no-brand-until-a-branded-value-is-stored -->
A read of a `static var` that was never given a branded value fits a parameter of any brand of its instance.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

type Registry
	static var items = ["a"]
end 'Registry'

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

function main() returns ExitCode
	takeXs(Registry.items)
	takeYs(Registry.items)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
1
```

<!-- test: a-top-level-character-is-a-record-slot-a-var-can-reassign -->
A character initializer is the expression a body would write, so the global holds a `Character` record: a `let`
is read as it was built, and a `var`'s slot takes a new character.
```maxon
var c = 'x'
let d = 'é'

function main() returns ExitCode
	print("{c}{d}\n")
	c = 'y'
	print("{c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
xé
y
```
