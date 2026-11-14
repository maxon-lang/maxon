---
feature: static-variables
status: experimental
keywords: [static, let, global, module, type]
category: language
---
# Static Members and Top-Level Bindings

## Documentation

### Top-Level `let` Declarations

A top-level binding is a `let`. A module-level `var` and a `static var` do not exist (`specs/module-var-removed.md`): mutable state belongs in a local `var`, a record, a `SharedValue` or a service. A top-level `let` whose value is decided at compile time is image data and lives in read-only memory.

#### Syntax

```maxon
let LIMIT = 100
export let Greeting = "hello"
```

#### Features

- **Runtime storage**: A value decided at compile time is image data; any other is built before `main`
- **Initialization**: Every initializer runs before `main`, exactly once, in dependency order, whether or not anything reads the binding (`specs/lazy-static-observable.md`)
- **Type inference**: Type is inferred from the initializer
- **Export support**: Use `export let` to make bindings available to other modules

#### Initializer Requirements

A top-level `let` initializer is any expression a function body accepts, parsed by the body's
expression parser with the same meaning and the same diagnostics. A scalar binding must have a value known at
compile time (E2045 names a call of a non-generic function that returns one; any other call is E3220). A record whose value is decided at compile time is
laid down as read-only data. A struct
literal is refused at file scope (E3076); inside a `type` body a static member may be initialized by a struct
literal of that type (`specs/lazy-static.md`). An initializer calls nothing (E3220), so what a call may reach is not a question for it.

A top-level `let`'s record is immutable data through every name that reaches it — a `let` bound to it, a field
read off it, a parameter it was passed to. Calling a method that writes its receiver on it is E3207; passing it
to a parameter the callee writes is E3207; storing it into a field that is not declared `immutable` is
E3196, at the store, since the function that later writes the field cannot see where its value came from. A
function that hands the record back declares `returns immutable` (E3209), and a write through a `var` bound to what it handed back
is refused. `.clone()` makes a record of one's own.

### Static Fields in Types

Types can have static fields that are shared across all instances. A static field is written `static let`.

#### Syntax

```maxon
typealias Score = int(i64.min to i64.max)

type Counter
	static let MAX = 100           // Static field

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
- **Initializer**: The top-level initializer grammar above, plus a struct literal of the declaring type

#### Access Patterns

```maxon
function main() returns ExitCode
	var c = Counter.create(10)          // Create instance
	c.value = 20                        // Access instance field
	print("{c.value} {Counter.MAX}")    // Access static field
	return 0
end 'main'
```

## Tests

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

<!-- test: static-let-multiple-types -->
```maxon
type TypeA
	static let value = 32
end 'TypeA'

type TypeB
	static let value = 10
end 'TypeB'

function main() returns ExitCode
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
	static let base = 2
	export var id as Integer

	static function make(n Integer) returns Thing
		return Thing{id: n}
	end 'make'
end 'Thing'

function main() returns ExitCode
	let a = Thing.make(10)
	let b = Thing.make(20)
	return Thing.base + a.id + b.id
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
error E2013: <fragment>:7:2: 'Config.MAX_SIZE' is a let and cannot be reassigned
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
	export static let used = 2
end 'Config'

// --- file: app/main.maxon
function main() returns ExitCode
	return Config.used + Config.MAX_SIZE
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
error E2015: <fragment>:12:9: Unsupported: a `let` declaration in an `extension` body — an extension adds methods to types other declarations named and declares no storage of its own, so there is no type for this binding to belong to; declare it in the `type` body as a `static let`
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
member is written inside the `type` body that declares it, so `static let origin = Pair{a: 1, b: 2}` inside
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

	static let origin = Pair{a: 1, b: 2, c: 3}

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

	static let origin = Pair{a: 1}

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

	static let origin = Pair{a: "hi", b: 2}

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
error E3005: <fragment>:8:27: cannot assign a value of type 'String' to field 'a' of 'Pair', which holds 'Count'
```

<!-- test: error.static-struct-literal-field-out-of-range -->
```maxon
typealias Percent = int(0 to 100)

type Pair
	export var a as Percent

	static let origin = Pair{a: 500}

	static function get() returns immutable Pair
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

	static let one = Paint{c: 7, n: 1}

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

<!-- test: error.static-struct-literal-interface-field -->
The initializer of a module-level or `static` `let` calls `Square.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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

	static let one = Holder{s: Square.create(4), tag: 77}

	static function get() returns immutable Holder
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
error E3220: <fragment>:24:19: the initializer of 'Holder.one' calls 'Square.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3209: <fragment>:27:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: static-struct-literal-interface-field-holding-a-string-literal -->
A `String` literal widened into a `Hashable` field at module scope: the record is the literal's clone, and
its witness table names the standard library's own `String.hash`, which a program reaching no other
`Hashable` position must still build.
```maxon
type Holder
	export let key as Hashable

	static let one = Holder{key: "ab1cd"}

	static function get() returns immutable Holder
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

	static let one = Holder{s: 5, tag: 77}

	static function get() returns immutable Holder
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

<!-- test: error.static-factory-initializer -->
The initializer of a module-level or `static` `let` calls `Row.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Integer = int(i64.min to i64.max)

type Row
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Cache
	static let head = Row.create(42)
end 'Cache'

function main() returns ExitCode
	return Cache.head.n as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:13:20: the initializer of 'Cache.head' calls 'Row.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: static-managed-members -->
A `String` static and an array static in one type: both materialize once before `main`, both are read
through their qualified names, and both are freed at exit.
```maxon
type Names
	static let GREETING = "hi"
	static let items = FullRangeIntArray from [1, 2, 3]
end 'Names'

function main() returns ExitCode
	print("{Names.GREETING}{Names.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi3
```

<!-- test: error.static-let-array-mutate -->
⭐⭐ **THE WRITE-THROUGH-AN-IMMUTABLE-STATIC GUARD.** A receiver-writing method
on a `static let` array is E3207 — the SAME rule
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
error E3207: <fragment>:7:14: 'pop' writes its receiver (line 246: `return try managed.remove(len - 1)`); 'Cache.xs' is immutable
```

<!-- test: top-level-let-enum-initializer-cross-file -->
Cross-file: enum defined in one file, top-level `let` initialized with it in another.
```maxon
// --- file: api/defs.maxon
export enum CpuArch
	x64
	arm64
	wasm32
end 'CpuArch'

// --- file: app/main.maxon
let currentCpu = CpuArch.x64

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

### An array binding has STORAGE

A top-level array is a HEAP record with a `.data` pointer slot, built by `__module_init` before `main`
and released by `__maxon_global_cleanup` after it. A `let` refuses the write, both at an assignment (E2013)
and at a receiver-writing method (E3207).

A never-mutated `let` array is NOT promoted to an immortal shared static record: an `Array`'s own methods
rewrite its record (a `push` detaches the buffer by writing `buffer@0`/`capacity@16`/`length@8`), and a
`.rdata` record cannot be written — so immortality would need an enforced never-mutated guarantee and a
real COPY promotion for `var b = <borrowed array>`, neither of which exists. Heap is what can be
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
let xs = FullRangeIntArray from [BASE, BASE + 2]

function main() returns ExitCode
	let a = try xs.get(0) otherwise 0
	let b = try xs.get(1) otherwise 0
	return a + b
end 'main'
```
```exitcode
42
```

<!-- test: top-level-let-array-string-elements -->
A String-element array global owns its elements: each literal is CLONED into the record, and
`__managed_decref`'s walk `__str_decref`s every live slot at exit. Leak-gated — a missed clone double-frees
the immortal `.rdata` record and a missed stamp leaks all of them.
```maxon
let names = ["ab", "cde"]

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

<!-- test: error.top-level-let-array-mutate -->
A receiver-writing method on a `let`-bound array is E3207 — the SAME rule a `let`-bound String or Set
receiver gets, because the array is immutable data. Without it a `let` array would be writable, since
it has a real slot.
```maxon
let fixed = [4, 5, 6]

function main() returns ExitCode
	fixed.push(9)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:5:8: 'push' needs a writable receiver; 'fixed' is immutable
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
error E2013: <fragment>:5:2: 'xs' is a let and cannot be reassigned
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
writable name, and the record it would name is one the program declared `let`, so a write through it is
refused (E3207).

⚠ **THE REFUSAL IS ON THE WRITE.** `var b = A` followed by `b.push(9)` is E3207; `let b = A` is legal and can only read
(`top-level-let-array-let-alias`). The same refusal covers every door that hands a `var` the record — the
direct binding, a reassignment, a ternary/`match` merge that joins it, an `if`/loop merge that REBINDS it, a cell it
is spilled into and a closure that captures it, each pinned below — and, through a whole-program fact, a call that
hands the record back.

⚠ **A CALL BOUNDARY IS CROSSED BY THE CALLEE'S SUMMARY, NOT BY THE MARK.** A callee's parameter is a fresh
value in a fresh space, so the mark does not enter it; what crosses is what the callee does with it.
`g(A)` where `g(xs …)` binds `var b = xs` and writes `b` is refused at the call, because `g` writes `xs`.
`var c = idBox(G)` where `idBox(x Box)` is `return x`, followed by a write through `c`, is refused, because the result may share storage with
`G`. `Holder.of(G)` followed by `h.inner.n = 99` is refused at the write, because the record `h` holds is `G`'s.
```maxon
let A = FullRangeIntArray from [1, 2]

function main() returns ExitCode
	var b = A
	b.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E3207: <fragment>:6:4: 'push' needs a writable receiver; 'b' is immutable
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
error E3207: <fragment>:7:4: 'push' needs a writable receiver; 'b' is immutable
```

<!-- test: error.top-level-let-field-map-var-alias -->
The initializer of a module-level or `static` `let` calls `Holder.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Key = int(0 to 1000)
typealias Items = Array with Key
typealias ItemIndex = Map with (Key, Items)

type Holder
	export let index as ItemIndex

	static function create() returns Self
		var index = ItemIndex.create()
		index.upsert(1, value: [7])
		return Self{index: index.clone()}
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
error E3220: <fragment>:16:9: the initializer of 'H' calls 'Holder.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.top-level-let-field-accessor-read-only -->
The initializer of a module-level or `static` `let` calls `Holder.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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

function index() returns immutable ItemIndex
	return H.index
end 'index'

function main() returns ExitCode
	let found = index()
	return found.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:16:9: the initializer of 'H' calls 'Holder.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3209: <fragment>:19:2: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: error.top-level-let-field-accessor-result-write -->
The initializer of a module-level or `static` `let` calls `Holder.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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

function index() returns immutable ItemIndex
	return H.index
end 'index'

function main() returns ExitCode
	var m = index()
	m.upsert(2, value: [9])
	return H.index.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:16:9: the initializer of 'H' calls 'Holder.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3209: <fragment>:19:2: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
error E3207: <fragment>:24:2: 'upsert' writes its receiver (line 174: `try values.set(slotIndex as ElementIndex, value: value)`); 'm' is immutable
```

<!-- test: error.top-level-let-array-ternary-alias -->
And through a value MERGE, which is the door that costs one extra keyword. The mark that refuses the two
above rides the VALUE, and a merge mints a NEW value — so the phi has to inherit it, or `var pick = A if c
else M` launders exactly what `var pick = A` is refused for. Without that inheritance this program would
compile and return 3.
```maxon
let A = FullRangeIntArray from [1, 2]
let M = FullRangeIntArray from [5, 5]

function main() returns ExitCode
	var pick = A if M.count() == 2 else M
	pick.push(9)
	return A.count()
end 'main'
```
```maxoncstderr
error E3207: <fragment>:7:7: 'push' needs a writable receiver; 'pick' is immutable
```

<!-- test: error.top-level-let-array-match-arm-alias -->
The same merge, through the `match`-expression door rather than the ternary — one `finalizeMatchMerge`
serves both, so a phi that inherits the mark for one inherits it for the other by construction.
```maxon
let A = FullRangeIntArray from [1, 2]
let M = FullRangeIntArray from [5, 5]

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
error E3207: <fragment>:16:7: 'push' needs a writable receiver; 'pick' is immutable
```

<!-- test: error.top-level-let-array-handed-back-as-a-map-miss-fallback -->
And through a call whose `otherwise` fallback is the `let` array: `pick` hands back `Empty`'s own record
whenever the lookup misses, so its result mixes frozen and mutable data and is read-only for the caller: the write through `var mine` is E3197.
```maxon
typealias Count = int(0 to 1000)
typealias Items = Array with Count
typealias ItemIndex = Map with (Count, Items)

let Empty = Items.create(0)

function pick(index ItemIndex, k Count) returns Items
	return try index.get(k) otherwise Empty
end 'pick'

function main() returns ExitCode
	var index = ItemIndex.create()
	var stored = Items.create(0)
	stored.push(7)
	index.upsert(1, value: stored)
	var mine = pick(index, k: 9)
	mine.push(3)
	return Empty.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3197: <fragment>:18:7: cannot write 'a container' through 'a call result', which is read-only
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

<!-- test: error.top-level-let-struct-accessor-read-only -->
The initializer of a module-level or `static` `let` calls `Box.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static let shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	static function get() returns immutable Box
		return Box.shared
	end 'get'
end 'Box'

function main() returns ExitCode
	let a = Box.get()
	return a.n as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:7:22: the initializer of 'Box.shared' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3209: <fragment>:14:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: error.let-global-accessor-result-field-write -->
The initializer of a module-level or `static` `let` calls `Box.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static let shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	static function get() returns immutable Box
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
error E3220: <fragment>:7:22: the initializer of 'Box.shared' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3209: <fragment>:14:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
error E3197: <fragment>:20:2: cannot write 'n' through 'a', which is immutable
```

<!-- test: error.let-global-accessor-result-two-hops-of-return -->
The initializer of a module-level or `static` `let` calls `Box.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static let shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	static function inner() returns immutable Box
		return Box.shared
	end 'inner'

	static function get() returns immutable Box
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
error E3220: <fragment>:7:22: the initializer of 'Box.shared' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3209: <fragment>:14:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
error E3197: <fragment>:24:2: cannot write 'n' through 'a', which is immutable
```

<!-- test: error.let-global-passed-to-field-writing-callee -->
The initializer of a module-level or `static` `let` calls `Box.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:12:9: the initializer of 'G' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-to-a-callee-that-calls-a-self-writing-method -->
The initializer of a module-level or `static` `let` calls `Box.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:16:9: the initializer of 'G' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-to-a-callee-that-calls-a-self-writing-method-on-a-field -->
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:24:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-to-a-callee-that-pushes-into-a-container-a-call-hands-back-from-inside-it -->
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Holder
	export var items as CountArray

	static function make() returns Holder
		return Holder{items: CountArray.create(0)}
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
error E3220: <fragment>:13:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-to-a-callee-that-pushes-into-a-var-merged-from-inside-it -->
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Inner
	export var items as CountArray

	static function make() returns Inner
		return Inner{items: CountArray.create(0)}
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
	var list = CountArray.create(0)

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
error E3220: <fragment>:21:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-let-global-held-by-a-record-a-callee-builds-is-not-written-by-writing-that-record -->
The initializer of a module-level or `static` `let` calls `Guards.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Guards
	export var cols as CountArray

	static function make() returns Guards
		return Guards{cols: CountArray.create(0)}
	end 'make'
end 'Guards'

let Shared = Guards.make()

type Blocks
	export var subst as CountArray
	export var guards as immutable Guards

	static function create(guards immutable Guards) returns Blocks
		return Blocks{subst: CountArray.create(0), guards: guards}
	end 'create'
end 'Blocks'

function mint(guards immutable Guards) returns Blocks
	var blocks = Blocks.create(guards)
	blocks.subst.push(1)
	return blocks
end 'mint'

function main() returns ExitCode
	let b = mint(Shared)
	return (b.subst.count() * 10 + Shared.cols.count()) as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:13:14: the initializer of 'Shared' calls 'Guards.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3207: <fragment>:31:15: 'mint' takes `immutable` 'guards' (line 25: `var blocks = Blocks.create(guards)`); 'Shared' is mutable. Freeze it first
```

<!-- test: error.let-global-accessor-result-to-field-writing-callee -->
The initializer of a module-level or `static` `let` calls `Box.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)

type Box
	export var n as Count

	static let shared = Box.make()

	static function make() returns Box
		return Box{n: 1}
	end 'make'

	static function get() returns immutable Box
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
error E3220: <fragment>:7:22: the initializer of 'Box.shared' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3209: <fragment>:14:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
error E3207: <fragment>:24:7: 'bump' writes 'b' (line 19: `b.n = 99`); 'a' is immutable
```

<!-- test: error.let-global-to-callee-that-forwards-the-write -->
The initializer of a module-level or `static` `let` calls `Box.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:12:9: the initializer of 'G' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-given-out-of-a-closure -->
A closure hands out a read of a module-level `let`, and a write through the name bound to what the closure returns is
refused as a write through immutable data, as it is for a named function.
```maxon
let A = ["a", "b"]

function main() returns ExitCode
	let f = function() gives A
	let b = f()
	b.push("zz")
	return A.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3207: <fragment>:7:4: 'push' needs a writable receiver; 'b' is immutable
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

function expose() returns immutable Names
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
error E3207: <fragment>:21:42: 'grow' writes 'xs' (line 12: `xs.push("zz")`); 'expose()' is immutable
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

function expose() returns immutable Names
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
error E3207: <fragment>:20:15: 'grow' writes 'xs' (line 11: `xs.push("zz")`); 'b' is read-only
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

function expose() returns immutable Names
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
error E3207: <fragment>:20:15: 'grow' writes 'xs' (line 11: `xs.push("zz")`); 'b' is read-only
```

<!-- test: error.top-level-array-empty -->
An empty array literal has no element to infer a type from, so it is refused with the diagnostic a
function body's `[]` gets (`arrays/multi-line-literal-empty-still-rejected`).
```maxon
let items = []

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
let xs = [1, "a"]

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
	let flags = [true, false].clone()
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
let flags = [true, false, true]

function main() returns ExitCode
	return flags.count()
end 'main'
```
```exitcode
3
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

<!-- test: error.a-let-initializer-may-read-another-let -->
The initializer of a module-level or `static` `let` calls `Box.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns immutable Self
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
```maxoncstderr
error E3209: <fragment>:10:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
error E3220: <fragment>:14:9: the initializer of 'a' calls 'Box.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3220: <fragment>:15:9: the initializer of 'b' calls 'Box.current': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

### A write through a record a call hands back out of a `let`

A write, inside a function, through a record a call handed back out of a module-level `let` is E3207, and the
function that hands it back declares `returns immutable` (E3209); a write through a local `var` bound to that record is refused. The storage-provenance summary of the call answers it,
carried through calls, witness dispatches and calls through function values.

<!-- test: error.a-local-var-may-not-write-through-a-lets-record-a-call-hands-back -->
The initializer of a module-level or `static` `let` calls `Box.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns immutable Self
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
error E3209: <fragment>:10:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
error E3220: <fragment>:14:9: the initializer of 'a' calls 'Box.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3197: <fragment>:18:2: cannot write 'n' through 'w', which is immutable
```

<!-- test: error.a-lets-record-a-call-handed-back-may-not-go-to-a-writing-parameter -->
The initializer of a module-level or `static` `let` calls `Box.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns immutable Self
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
error E3209: <fragment>:10:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
error E3220: <fragment>:14:9: the initializer of 'a' calls 'Box.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3207: <fragment>:25:7: 'bump' writes 'b' (line 17: `b.n = 9`); 'Box.current()' is immutable
```

<!-- test: error.a-local-var-may-not-write-through-a-lets-record-an-await-hands-back -->
The initializer of a module-level or `static` `let` calls `Box.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns immutable Self
		return a
	end 'current'
end 'Box'

let a = Box.create()

function fetch() returns immutable Box
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
error E3209: <fragment>:10:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
error E3220: <fragment>:14:9: the initializer of 'a' calls 'Box.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3197: <fragment>:27:2: cannot write 'n' through 'b', which is immutable
```

<!-- test: error.a-local-var-may-not-write-through-a-lets-record-a-map-miss-hands-back -->
The initializer of a module-level or `static` `let` calls `Box.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:12:9: the initializer of 'a' calls 'Box.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

### A top-level `let` and a type of one name are one name declared twice

A directory declares a name once, whatever its kind: a top-level `let` beside a `type`, `union` or
`typealias` of the same name is E3006 at the later declaration, in one file or in two
(`specs/top-level-name-scope.md`).

<!-- test: error.top-level-let-and-type-of-one-name -->
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
```maxoncstderr
error E3006: <fragment>:6:6: duplicate definition of 'Widget' — already declared as `let Widget`
```

<!-- test: error.top-level-let-and-union-of-one-name -->
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
```maxoncstderr
error E3006: <fragment>:6:7: duplicate definition of 'Move' — already declared as `let Move`
```

<!-- test: error.top-level-let-and-array-alias-of-one-name -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

let IntArray = "shadow"

function main() returns ExitCode
	var a = IntArray.create(0)
	a.push(1)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3006: <fragment>:5:5: duplicate definition of 'IntArray' — already declared as `typealias IntArray`
```

<!-- test: top-level-let-scalar-reassign-error -->
Assigning to an immutable top-level `let` is E2013 — and the diagnostic names the `let` the
author actually wrote, rather than reporting a name that is defined two lines up as undefined.

That distinction is the whole reason the assignment path probes for a constant at all: a name
that is not a local would otherwise fall through to "undefined
variable", sending the reader hunting for a typo instead of at the `let` they assigned. The struct twin below pins the same arm for a struct `let`.

```maxon
let origin = 5

function main() returns ExitCode
	origin = 6
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:5:2: 'origin' is a let and cannot be reassigned
```

<!-- test: top-level-let-struct-reassign-error -->
The initializer of a module-level or `static` `let` calls `Point.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:13:14: the initializer of 'origin' calls 'Point.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E2013: <fragment>:16:3: 'origin' is a let and cannot be reassigned
```

<!-- test: top-level-let-function-call-error -->
Function calls are not allowed in module-level `let` initializers.
```maxon
typealias Integer = int(i64.min to i64.max)

function getDefault() returns Integer
	return 42
end 'getDefault'

let value = getDefault()

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
error E3006: <fragment>:3:5: duplicate definition of 'A' — already declared as `let A`
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
	var n = Names.create(0)
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
error E3197: <fragment>:15:7: cannot write through a read-only value, as it joins immutable and mutable data
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

let sharedEmptyNames = Names.create(0)

function pairable(bound Names, wanted Names) returns Names
	return sharedEmptyNames if bound.count() < wanted.count() else wanted
end 'pairable'

function main() returns ExitCode
	let a = Names.create(0)
	a.push(1)
	let b = Names.create(0)
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

let sharedEmptyBits = Bits.create(0)

function withRootBits(base Bits, bits Integer) returns Bits
	let out = Bits.create(0)
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

let bits = Bits.create(0)

function localWins() returns Bits
	let bits = Bits.create(0)
	bits.push(7)
	bits.push(9)
	return bits
end 'localWins'

function main() returns ExitCode
	let got = localWins()
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
one line over is refused. So this door is a matter of SOUNDNESS and not only of lifting a refusal.

<!-- test: a-parameter-default-may-name-a-let-global -->
The `sharedNoOpaqueParams` shape, and all three spellings of the same argument are exercised side by
side — omitted, written out, and a value of the caller's own — because "the default agrees with the
written-out argument" is the property, not "the default compiles".
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Names = Array with Integer

let sharedEmptyNames = Names.create(0)

function describe(names Names = sharedEmptyNames) returns Integer
	return names.count()
end 'describe'

function main() returns ExitCode
	let given = Names.create(0)
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

let sharedEmptyNames = Names.create(0)

function shared() returns immutable Names
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
callee pushes, so the omitted argument is refused at the CALL by the capability checker, as the written-out
`grow(sharedEmptyNames)` is. **The subject is "the argument" and not a binding name**, because a filled default
names nothing the author wrote.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Names = Array with Integer

let sharedEmptyNames = Names.create(0)

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
error E3207: <fragment>:13:12: 'grow' writes 'names' (line 8: `names.push(1)`); the argument is read-only
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

let sharedEmptyNames = Names.create(0)

function shared() returns immutable Names
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
error E3207: <fragment>:17:12: 'grow' writes 'names' (line 12: `names.push(1)`); the argument is read-only
```

<!-- test: a-field-default-naming-a-let-global-is-still-refused -->
⛔⛔ **A FIELD DEFAULT THAT NAMES A MODULE-LEVEL `let` STORES THE GLOBAL'S RECORD INTO A MUTABLE FIELD, AND IS
REFUSED.** A parameter default hands the record to a call argument, where a mutating callee is refused. A
field default hands it to a struct slot, and a write through that field would mutate a global declared
immutable, so the store is the refusal: the field is declared `immutable Names` when it is meant to hold the
global's record.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Names = Array with Integer

let sharedEmptyNames = Names.create(0)

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
error E3196: <fragment>:8:21: cannot store an immutable 'Names' into 'Holder.names', which holds a mutable 'Names'; declare the field `immutable Names`
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
`carriesPayloads` means SOME case carries a payload, and then EVERY case — payload-free ones included — is a box
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
`__module_init` builds a union box through, so the image and the built box cannot disagree about which
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
	let xs = BoxedArray.create(0)
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
	module static let used = 42 as FullRangeInt
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

let globalOps = [Op.add(7), Op.nop]

function main() returns ExitCode
	let first = try globalOps.get(0) otherwise Op.nop
	return match first 'check'
		add(v) gives v
		nop gives 99
	end 'check'
end 'main'
```
```exitcode
7
```

<!-- test: error.global-bytestring-map-with-union-array -->
The initializer of a module-level or `static` `let` calls `KeywordInfo.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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

let keywordMap = [
	b"if": KeywordInfo.create(1, helpText: "Conditional statement."),
	b"else": KeywordInfo.create(2, helpText: "Alternative branch.")
]

function main() returns ExitCode
	var ops = OpArray.create(0)
	ops.push(Op.add(1))
	ops.push(Op.nop)

	let info = try keywordMap.get(b"if") otherwise KeywordInfo.create(0, helpText: "")
	if info.kind != 1 'badLookup'
		return 1
	end 'badLookup'

	return 0 if ops.count() == 2 and keywordMap.count() == 2 else 2
end 'main'
```
```maxoncstderr
error E3220: <fragment>:21:18: the initializer of 'keywordMap' calls 'KeywordInfo.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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

let opMap = [
	b"add": Op.add(5),
	b"nop": Op.nop
]

function main() returns ExitCode
	let op = try opMap.get(b"add") otherwise Op.nop
	return match op 'check'
		add(v) gives v
		nop gives 99
	end 'check'
end 'main'
```
```exitcode
5
```

<!-- test: error.live-global-survives-dead-global-pruning -->
The initializer of a module-level or `static` `let` calls `describe`, and a module-level initializer calls nothing, so the program is refused (E3220).
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

let keywordMap = [
	b"if": KeywordInfo.create(1, helpText: describe(1)),
	b"else": KeywordInfo.create(2, helpText: describe(2))
]

public let unusedNames = StringArray.create(0)

function main() returns ExitCode
	let info = try keywordMap.get(b"else") otherwise KeywordInfo.create(0, helpText: "")
	if info.helpText != "kind 2" 'badHelp'
		return 1
	end 'badHelp'

	return 0 if keywordMap.count() == 2 and info.kind == 2 else 2
end 'main'
```
```maxoncstderr
error E3220: <fragment>:17:18: the initializer of 'keywordMap' calls 'describe': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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

let hold = Hold.owned(9)

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

⭐⭐ **A CONSTANT'S TAG IS NOT ITS IDENTITY.** `named` covers every declared `enum` AND every boxed `union`;
`genericInstance` covers every container. A slot check that compared only the tag would admit a `Status` case
into a `Colour` field and a `Map` box into an `Array` field. Both cases below are `E3005`, reported where and
as the same call in a function body reports it — at the factory's name — and each is followed by that body
twin.

<!-- test: error.const-fold-union-case-into-enum-field -->
The initializer of a module-level or `static` `let` calls `Mixed.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:23:11: the initializer of 'Bad' calls 'Mixed.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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
The initializer of a module-level or `static` `let` calls `Map.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:14:9: the initializer of 'H' calls 'Map.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Holder
	export var items as CountArray

	static function make() returns Holder
		return Holder{items: CountArray.create(0)}
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
error E3220: <fragment>:13:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-field-passed-to-a-writing-callee -->
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:20:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-alias-field-passed-to-a-writing-callee -->
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:20:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-field-receives-a-self-writing-method -->
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:24:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-alias-field-receives-a-self-writing-method -->
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:24:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-container-field-passed-to-a-pushing-callee -->
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Holder
	export var items as CountArray

	static function make() returns Holder
		return Holder{items: CountArray.create(0)}
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
error E3220: <fragment>:13:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-global-alias-container-field-passed-to-a-pushing-callee -->
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Holder
	export var items as CountArray

	static function make() returns Holder
		return Holder{items: CountArray.create(0)}
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
error E3220: <fragment>:13:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.let-binding-of-a-let-global-field-receives-a-self-writing-method -->
The initializer of a module-level or `static` `let` calls `Holder.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:24:9: the initializer of 'G' calls 'Holder.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-let-global-stored-into-a-var-field-is-refused-at-the-store -->
The initializer of a module-level or `static` `let` calls `Config.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:20:9: the initializer of 'G' calls 'Config.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-clone-of-a-let-global-stored-into-a-var-field-may-be-written -->
The initializer of a module-level or `static` `let` calls `Config.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:20:9: the initializer of 'G' calls 'Config.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: a-top-level-let-map-with-cast-values-reads-back-typed -->
A cast names its type at file scope too, so a top-level `let` map built from `as Count` values is a typed map that reads back as `Count`.
```maxon
let Tally = [b"one": 1 as Count, b"two": 2 as Count, b"three": 3 as Count]

function show(v Count)
	print("{v}\n")
end 'show'

function main() returns ExitCode
	let one = try Tally.get(b"one") otherwise 0
	show(one)
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

<!-- test: error.a-top-level-let-map-values-keep-their-alias -->
The alias a file-scope cast names is the column's type, so a read of it does not pass where another alias is declared.
```maxon
typealias Score = int(0 to 100)

let Tally = [b"one": 1 as Count]

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
error E3005: <fragment>:12:2: argument type mismatch for 'v': expected 'Score', got 'Count' — cast the argument with 'as Score'
```

<!-- test: a-top-level-character-is-a-record-slot -->
A character initializer is the expression a body would write, so the global holds a `Character` record that
is read as it was built.
```maxon
let c = 'x'
let d = 'é'

function main() returns ExitCode
	print("{c}{d}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
xé
```

### A static member's initializer ends at the line holding the closing bracket of a kept `match` block

<!-- test: error.a-match-expression-in-a-call-argument-of-a-static-let -->
The initializer of a module-level or `static` `let` calls `tag`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
enum Mode
	quiet
	loud
end 'Mode'

function tag(text String, tail String) returns String
	return "{text}{tail}"
end 'tag'

type Holder
	static let LABEL = tag(match Mode.loud 'm'
		quiet gives "quiet"
		loud gives "loud"
	end 'm', tail: "!")
	static let NEXT = "next"
end 'Holder'

function main() returns ExitCode
	print("{Holder.LABEL} {Holder.NEXT}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:12:21: the initializer of 'Holder.LABEL' calls 'tag': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-return-in-a-handler-block-of-a-static-let-names-the-initializer -->
```maxon
enum ParseError implements Error
	bad
end 'ParseError'

function parse(n Count) returns Count throws ParseError
	if n > 5 'big'
		throw ParseError.bad
	end 'big'
	return n
end 'parse'

type Config
	static let LEVEL = try parse(3) otherwise (e) 'h'
		return 3
	end 'h'
end 'Config'

function main() returns ExitCode
	print("{Config.LEVEL}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2080: <fragment>:15:3: `return` has no function to leave in the initializer of 'Config.LEVEL' — end a handler there with `panic(…)`, or give the `try` a fallback value with `otherwise <expr>`
```

<!-- test: error.a-return-in-a-handler-block-of-a-static-record-let-names-the-initializer -->
```maxon
enum ParseError implements Error
	bad
end 'ParseError'

type Config
	export let level as Count

	static function create(level Count) returns Config
		return Self{level: level}
	end 'create'
end 'Config'

function makeConfig(n Count) returns Config throws ParseError
	if n > 5 'big'
		throw ParseError.bad
	end 'big'
	return Config.create(n)
end 'makeConfig'

type Settings
	static let CONFIG = try makeConfig(3) otherwise (e) 'h'
		return Config.create(4)
	end 'h'
end 'Settings'

function main() returns ExitCode
	print("{Settings.CONFIG.level}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2080: <fragment>:23:3: `return` has no function to leave in the initializer of 'Settings.CONFIG' — end a handler there with `panic(…)`, or give the `try` a fallback value with `otherwise <expr>`
```
