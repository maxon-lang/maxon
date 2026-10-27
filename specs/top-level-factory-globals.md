---
feature: top-level-factory-globals
status: stable
keywords: global, static, factory, module-init, ownership, visibility
category: language
---
# Top-Level Globals Built By A User Factory

## Documentation

A top-level binding may be initialized by a **user static factory call** —
`var db = Database.create(EntryMap.create(), sourcePaths: StringArray.create())`. The initializer is
parsed by the body's expression parser into an initializer function, and — unless the record is decided at
compile time and laid down as image data — `__module_init` calls it before `main` and stores its result in
the binding's slot, so the call, its arguments and their drops are the
ones the same expression gets in a function body.

`map-struct-bytearray.md` is the canonical spec that admits the form. This file pins the six rules
that form has to obey and that its own programs cannot reach, because all of its arguments are
consumed and all of its declarations are in one file:

**1. A generic-instance alias's `create()` is the static the program wrote.** `typealias IntBox = Box
with Count` names either a builtin container (`Array` / `Set` / `List` / `FixedArray`) or an ordinary
declared generic. A builtin's `create` is a runtime entry handed the strides and column destructors it
cannot look up; a declared generic's builds its own fields from its own declaration. A generic's
`create()` also carries a layout descriptor and one witness per `where` constraint, sourced at the CALL
SITE from the call's RESULT for a `Self`-returning static — the emitted call is the one the identical
`IntBox.create()` inside a function lowers to.

**A file-scope `IntBox.create(...)` is checked as the same call in a function body is:** a `create` the
generic does not declare is an undefined function (E3004), one the declaring file cannot name is E3008,
one returning a scalar makes the initializer a non-constant scalar (E2045), and its arguments are passed
as a body's are.

**2. An argument the callee only BORROWS is still the initializer's to free.** The initializer's call
materializes an argument as an owned temporary and drops it after the call unless the callee consumed
it, as a body's call site does. Getting this wrong is a leak on a program with no error in it — which
the exit-101 memory gate is what catches.

**3. The METHOD's visibility is checked, not only the type's.** The initializer is parsed in the
declaring file of the BINDING, so it reports the same codes a call in a function body gets: E3008 for a
file-private callee, E3088 for a module-scoped one.

**4. A factory in a LISTED STDLIB MODULE is REACHED by the initializer.** Every pre-elimination pass
skips a stdlib function the program cannot reach (`LibraryFacts.unreachable`), and that reachability
is walked from `main` over the program's own call edges. `__module_init` is not called from `main` —
the entry stub calls it — so a stdlib factory named ONLY by a top-level initializer has to be reached
through the DECLARATION that causes the call, not through the synthesized body that makes it. Getting
this wrong is not a wrong answer but a compiler panic: dead-function elimination roots
`__module_init`, reaches the factory the pre-elimination passes were told was dead, and
`requireUnreachableLibraryStayedDead` fires on a program with nothing wrong with it.

**5. The callee's declared return type is ONE fact, however the callee spelled it.** A `static`
whose `returns` clause names its own enclosing type — as `Self`, or by that type's own name — is
resolved to the type SYNTACTICALLY, because `Self` is a fact about the tokens and needs no registry.
A `returns` clause naming a FOREIGN type cannot be: the declaration sweep reads it while the registry
that would say what the name denotes is still being filled, so it records a bare name. Two spellings
of one type is a fact written down twice, and the two readers of it disagreed: the admission router
read the bare name as a record and let the program through, and the slot's width router then found a
tag it had no case for and panicked — with a sentence written for a different caller, about an `int`
the program does not contain. A factory returning a type it does not enclose is the same declaration
as one returning `Self`, and the index normalizes the two to one spelling before anything reads it.

**A TUPLE TYPEALIAS IS THE SAME RULE, and without it the answer would be a property of the disk.**
`typealias Pair = (Num, Num)` names the tuple's own struct and mints no identity of its own, so a
`returns Pair` is a `named` the sweep must resolve exactly as it resolves a declared type's name — and
the resolution goes through the one door that already knows what a name denotes, the same door the
drop router asks. Without it the same program would have TWO answers decided by which file the
directory walk reaches first: the alias resolves from the tokens of its own file, so a same-file `Pair`
would compile and a cross-file one be refused. Same program, two answers, is a wrong answer whichever
one you prefer.

**6. A recorded type can be CONCRETE and still be the wrong SPELLING, and for a tuple that is a
missing symbol rather than a wrong width.** `Parser.parseTypeReference`'s tuple arm sits outside the
`allFilesFolded` gate its neighbours are behind, so during the declaration sweep a `returns Tagged`
already resolves to the tuple's own struct — through a canonicalizer that is ITSELF gated on that
flag, and therefore hands back the raw spelling at that moment. The sweep records
`__Tuple2.String.Num` where every value of that tuple, and the destructor synthesized for it, carry
`__Tuple2.String.int`. Unnormalized, the drop the cleanup emits would name a symbol nothing defines,
and the program would die in the backend.

**The shape of that defect is worth stating exactly, because the obvious reading of it is wrong.** It
looks like "a tuple owning heap cannot be a global" — but unnormalized, `(String, String)` compiles and
runs while `(String, Num)` dies. The two differ only in whether the swept spelling and the canonical
one COINCIDE; ownership is merely what makes the difference observable, since a tuple owning nothing
asks for no destructor at all. Refusing heap-owning tuples would therefore delete a working
program to tidy a rule. ⇒ the normalization settles the SPELLING, at what is the third door this
index's types leave by; `tuples.md`'s `array-of-managed-element-tuples-drops-each` records the second,
in the same words and with the same panic.

## Tests

<!-- test: user-generic-instance-factory-at-file-scope -->
A generic-instance alias over a DECLARED generic is built before `main` by calling the `create()`
static the program wrote. Its `create` is file-private and the binding is in the same file, which is
all rule 3 asks.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'
end 'Box'

typealias IntBox = Box with Count

var b = IntBox.create()

function main() returns ExitCode
	return b.n
end 'main'
```
```exitcode
9
```

<!-- test: error.user-generic-instance-with-no-create-at-file-scope -->
A generic that declares no `create()` has none to call, at file scope as in a function body.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function make() returns Self
		return Self{n: 9}
	end 'make'
end 'Box'

typealias IntBox = Box with Count

var b = IntBox.create()

function main() returns ExitCode
	return b.n
end 'main'
```
```maxoncstderr
error E3004: <fragment>:14:16: call to undefined function 'Box.create'
```

<!-- test: error.user-generic-instance-with-no-create-in-a-function-body -->
The same call in a function body.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function make() returns Self
		return Self{n: 9}
	end 'make'
end 'Box'

typealias IntBox = Box with Count

function main() returns ExitCode
	let b = IntBox.create()
	return b.n
end 'main'
```
```maxoncstderr
error E3004: <fragment>:15:17: call to undefined function 'Box.create'
```

<!-- test: error.user-generic-instance-create-returning-a-scalar-at-file-scope-is-not-a-constant -->
A `create` that returns a `Count` makes the initializer a scalar call, which a file-scope binding
cannot hold.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create() returns Count
		return 5
	end 'create'
end 'Box'

typealias IntBox = Box with Count

var b = IntBox.create()

function main() returns ExitCode
	return 3
end 'main'
```
```maxoncstderr
error E2045: <fragment>:14:16: Function calls are not allowed in global variable initializers; 'create()' is not a constant expression
```

<!-- test: user-generic-instance-create-returning-a-scalar-in-a-function-body -->
The same call in a function body binds the `Count` it returns.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create() returns Count
		return 5
	end 'create'
end 'Box'

typealias IntBox = Box with Count

function main() returns ExitCode
	let b = IntBox.create()
	return b as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: arguments-to-a-generic-instance-create-at-file-scope -->
`IntBox.create(9)` builds a populated instance before `main`, as the same call in a function body
does.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias IntBox = Box with Count

var b = IntBox.create(9)

function main() returns ExitCode
	return b.n
end 'main'
```
```exitcode
9
```

<!-- test: arguments-to-a-generic-instance-create-in-a-function-body -->
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias IntBox = Box with Count

function main() returns ExitCode
	let b = IntBox.create(9)
	return b.n
end 'main'
```
```exitcode
9
```

<!-- test: user-generic-instance-factory-in-a-function -->
The same `IntBox.create()` written inside a function, which answers identically — the file-scope form
above emits the same call, dictionary arguments included.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

typealias IntBox = Box with Count

function main() returns ExitCode
	let b = IntBox.create()
	return b.n
end 'main'
```
```exitcode
7
```

<!-- test: borrowed-record-argument-is-released -->
`Counter.create` READS its array parameter and stores nothing, so the array `__module_init`
materialized for the call is still the initializer's own. Freeing it is what makes this exit 0
rather than 101.
```maxon
typealias Count = int(0 to u64.max)
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

type Counter
	export var n as Count

	static function create(xs Bytes) returns Self
		return Self{n: xs.count() as Count}
	end 'create'
end 'Counter'

var c = Counter.create(Bytes.create())

function main() returns ExitCode
	return c.n
end 'main'
```
```exitcode
0
```

<!-- test: error.cross-file-file-private-generic-create -->
Rule 3 reaches the CONTAINER door too, and by the same route: `__module_init` is a function no file
wrote, so the pass that checks a call's visibility exempts every call it makes. Without the check here
an exported generic's file-private `create` would be reachable from any file that writes
`var b = Alias.create()`, while the byte-identical call in a function body is E3008.
```maxon
// --- file: api/box.maxon
export typealias Count = int(0 to u64.max)

export type Box uses T
	export var n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

export typealias IntBox = Box with Count

// --- file: bin/main.maxon
var b = IntBox.create()

function main() returns ExitCode
	return b.n
end 'main'
```
```maxoncstderr
error E3008: bin/<fragment>:16:16: function 'Box.create' is not exported
```

<!-- test: error.cross-file-file-private-generic-create-in-a-function-body -->
The byte-identical call in a function body, reported at the same token.
```maxon
// --- file: api/box.maxon
export typealias Count = int(0 to u64.max)

export type Box uses T
	export var n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

export typealias IntBox = Box with Count

// --- file: bin/main.maxon
function main() returns ExitCode
	let b = IntBox.create()
	return b.n
end 'main'
```
```maxoncstderr
error E3008: bin/<fragment>:17:17: function 'Box.create' is not exported
```

<!-- test: consumed-and-borrowed-arguments-in-one-call -->
The two ownership outcomes in ONE call: `kept` and `tag` are stored (the callee's to free, through
the struct's destructor), `seen` is only counted (the initializer's). Dropping a consumed argument
would be a double free and skipping a borrowed one a leak, so this passes only when the per-argument
decision is per-argument.
```maxon
typealias Count = int(0 to u64.max)
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

type Mixed
	export var kept as Bytes
	export var n as Count
	export var tag as String

	static function create(kept Bytes, seen Bytes, tag String) returns Self
		return Self{kept: kept, n: seen.count() as Count, tag: tag}
	end 'create'
end 'Mixed'

var m = Mixed.create(Bytes.create(), seen: Bytes.create(), tag: "hi")

function main() returns ExitCode
	print("{m.tag}\n")
	return m.n + (m.kept.count() as Count)
end 'main'
```
```exitcode
0
```
```stdout
hi
```

<!-- test: same-file-file-private-factory -->
Visibility governs what ANOTHER file may name, never whether a declaration is in scope where it was
written — so a file-private `static function create` initializes a global in its own file. The
control for the two cross-file refusals below.
```maxon
type Database
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

var db = Database.create()

function main() returns ExitCode
	return db.n
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: factory-returning-a-foreign-record -->
`Outer.build` returns a type it does not enclose, so the sweep records the return type as a bare
name where `Inner.make`'s `returns Self` one screen up is already resolved. Both are the same
declaration of the same struct and both must reach the same one-word slot. There is no stdlib in
this program and no `int` in it either, which is what makes it the clean statement of rule 5.
```maxon
type Inner
	export var v as Integer

	static function make() returns Self
		return Self{v: 7}
	end 'make'
end 'Inner'

type Outer
	static function build() returns Inner
		return Inner.make()
	end 'build'
end 'Outer'

var a = Outer.build()

function main() returns ExitCode
	return a.v
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: factory-returning-a-foreign-record-that-owns-a-managed-field -->
The same shape with heap in it, so the exit-101 memory gate has teeth on this path: `Inner` owns a
`String`, the record is built before `main` and released after it by `__maxon_global_cleanup`, and a
slot the width router had refused to type is a slot nothing would have dropped. Rule 2's concern,
asked of the record itself rather than of an argument.
```maxon
type Inner
	export var tag as String

	static function make() returns Self
		return Self{tag: "hi"}
	end 'make'
end 'Inner'

type Outer
	static function build() returns Inner
		return Inner.make()
	end 'build'
end 'Outer'

var a = Outer.build()

function main() returns ExitCode
	print("{a.tag}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi
```

<!-- test: factory-returning-a-record-declared-in-another-file -->
The returned type declared in a DIFFERENT file from the factory that returns it. The normalization
is keyed off the recorded return type and not off any file column, which is what makes this the same
answer as the single-file case above; keyed off a file instead, a name refiled since it was recorded
would be resolved under a key it had left.
```maxon
// --- file: api/inner.maxon
export type Inner
	export var v as Integer

	export static function make() returns Self
		return Self{v: 7}
	end 'make'
end 'Inner'

export typealias Integer = int(i64.min to i64.max)
// --- file: api/outer.maxon
export type Outer
	export static function build() returns Inner
		return Inner.make()
	end 'build'
end 'Outer'

// --- file: bin/main.maxon
var a = Outer.build()

function main() returns ExitCode
	return a.v
end 'main'
```
```exitcode
7
```

<!-- test: factory-returning-a-tuple-alias -->
A tuple typealias names the tuple's own struct, so a `returns Pair` is a record whose slot is the same
one word every other record's is. It is the control for the cross-file case below and for rule 5's
tuple clause: this spelling works, and the clause must not be satisfied by refusing it.
```maxon
typealias Num = int(0 to 1000)
typealias Pair = (Num, Num)

type Maker
	static function build() returns Pair
		return (11, 22)
	end 'build'
end 'Maker'

var a = Maker.build()

function main() returns ExitCode
	print("{a.0} {a.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 22
```

<!-- test: factory-returning-a-tuple-alias-declared-in-another-file -->
The identical program with the alias declared in a DIFFERENT file from the factory, and the file
NAMES are the whole of this case. `recordTupleAlias` writes during the walk rather than at the end of
a file, so a tuple alias is resolved from the tokens when the declaring file has already been reached
and left bare when it has not — which would make the verdict a property of the DISK. Without rule 5's
tuple clause, in one directory, on byte-identical source: with the alias's file sorting FIRST the
program compiles and prints `11 22`; with the factory's file sorting first — the spelling below — the
same program is refused as a record `__module_init` cannot reach. Same program, two answers. This
case pins the order that breaks without the clause; the control above is the order that does not.
```maxon
// --- file: api/a-maker.maxon
export type Maker
	export static function build() returns Pair
		return (11, 22)
	end 'build'
end 'Maker'

// --- file: api/z-pair.maxon
public typealias Num = int(0 to 1000)
export typealias Pair = (Num, Num)

// --- file: bin/main.maxon
var a = Maker.build()

function main() returns ExitCode
	print("{a.0} {a.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 22
```

<!-- test: factory-returning-a-tuple-that-owns-heap -->
Rule 6, and the reason it is about a SPELLING rather than about ownership. This tuple's elements are
`String` and an ALIAS, so the sweep spells it `__Tuple2.String.Num` while every value of it — and the
`__destruct_` synthesized for it — carries `__Tuple2.String.int`; unnormalized, the cleanup after `main`
calls a symbol nothing defines and the backend dies with `resolveCallFixups: call to unknown function
'__destruct___Tuple2.String.Num'`. **The neighbouring `(String, String)` case, whose two spellings
coincide, compiles and runs either way** — which is what proves the cut is at the NAME and not at
owns-heap, and why this case runs rather than being refused. Exit 0 is also the memory gate: the
global's record is built before `main` and released after it, through the destructor that resolves.
```maxon
typealias Num = int(0 to 1000)
typealias Tagged = (String, Num)

type Maker
	static function build() returns Tagged
		return ("hi", 7)
	end 'build'
end 'Maker'

var a = Maker.build()

function main() returns ExitCode
	print("{a.0} {a.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi 7
```

<!-- test: factory-returning-a-tuple-whose-spelling-is-already-canonical -->
The control the case above cannot do without: a heap-owning tuple whose elements name no alias, so the
swept spelling IS the canonical one. It compiles and runs — the evidence that the defect above is not
"a tuple owns heap", and the reason no refusal exists for that shape.
```maxon
typealias Both = (String, String)

type Maker
	static function build() returns Both
		return ("hi", "there")
	end 'build'
end 'Maker'

var a = Maker.build()

function main() returns ExitCode
	print("{a.0} {a.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi there
```

<!-- test: factory-returning-a-generic-alias -->
A factory returning a generic-instance alias builds the global before `main`.
```maxon
typealias Num = int(0 to 1000)

type Box uses T
	export var n as Num

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

typealias NumBox = Box with Num

type Maker
	static function build() returns NumBox
		return NumBox.create()
	end 'build'
end 'Maker'

var a = Maker.build()

function main() returns ExitCode
	return a.n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: factory-returning-a-generic-alias-in-a-function-body -->
```maxon
typealias Num = int(0 to 1000)

type Box uses T
	export var n as Num

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

typealias NumBox = Box with Num

type Maker
	static function build() returns NumBox
		return NumBox.create()
	end 'build'
end 'Maker'

function main() returns ExitCode
	let a = Maker.build()
	return a.n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: factory-returning-a-boxed-union -->
A factory returning a boxed union builds the global before `main`, and `match` reads its case.
```maxon
union Shape
	circle(r Integer)
	square(s Integer)
end 'Shape'

type Maker
	static function build() returns Shape
		return Shape.circle(7)
	end 'build'
end 'Maker'

var a = Maker.build()

function main() returns ExitCode
	match a 'kind'
		circle(r) then return r
		square(s) then return s
	end 'kind'
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: factory-returning-a-boxed-union-in-a-function-body -->
```maxon
union Shape
	circle(r Integer)
	square(s Integer)
end 'Shape'

type Maker
	static function build() returns Shape
		return Shape.circle(7)
	end 'build'
end 'Maker'

function main() returns ExitCode
	let a = Maker.build()
	match a 'kind'
		circle(r) then return r
		square(s) then return s
	end 'kind'
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: error.cross-file-file-private-factory -->
An `export type` whose `static` factory is NOT exported: naming it from another file's top-level
initializer is the same mistake as naming it from that file's function body, and gets the same
E3008.
```maxon
// --- file: api/db.maxon
export type Database
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

export typealias Integer = int(i64.min to i64.max)
// --- file: bin/main.maxon
var db = Database.create()

function main() returns ExitCode
	return db.n
end 'main'
```
```maxoncstderr
error E3008: bin/<fragment>:13:19: function 'Database.create' is not exported
```

<!-- test: error.cross-file-file-private-factory-in-a-function-body -->
```maxon
// --- file: api/db.maxon
export type Database
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

export typealias Integer = int(i64.min to i64.max)
// --- file: bin/main.maxon
function main() returns ExitCode
	let db = Database.create()
	return db.n
end 'main'
```
```maxoncstderr
error E3008: bin/<fragment>:14:20: function 'Database.create' is not exported
```

<!-- test: error.cross-file-module-scoped-factory -->
The `module` tier of the same rule, from a directory outside the declaration's subtree — E3088, the
code and the sentence a module-scoped callee gets at a call in a function body.
```maxon
// --- file: api/db.maxon
export type Database
	export var n as Integer

	module static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

export typealias Integer = int(i64.min to i64.max)
// --- file: bin/main.maxon
var db = Database.create()

function main() returns ExitCode
	return db.n
end 'main'
```
```maxoncstderr
error E3088: bin/<fragment>:13:19: function 'Database.create' is module-scoped and not visible from this directory
```

<!-- test: error.cross-file-module-scoped-factory-in-a-function-body -->
```maxon
// --- file: api/db.maxon
export type Database
	export var n as Integer

	module static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

export typealias Integer = int(i64.min to i64.max)
// --- file: bin/main.maxon
function main() returns ExitCode
	let db = Database.create()
	return db.n
end 'main'
```
```maxoncstderr
error E3088: bin/<fragment>:14:20: function 'Database.create' is module-scoped and not visible from this directory
```

<!-- test: stdlib-factory-reached-from-the-initializer -->
`FilePath.separator()` is a listed stdlib module's static, and this program names it NOWHERE else —
so the only edge that reaches it is the top-level initializer's. The separator is one byte on every
host (`\` on Windows, `/` elsewhere), which is what makes the exit code a portable assertion that the
factory actually RAN rather than merely compiled.
```maxon
var sep = FilePath.separator()

function main() returns ExitCode
	return sep.byteLength() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: stdlib-factory-with-arguments-reached-from-the-initializer -->
The same edge with a record ARGUMENT and a struct result: `BuildConfig.create` is reached only from
this initializer, and the `StringArray` built for `sources` is materialized, borrowed and freed
inside `__module_init` — so the exit-101 gate is asserting the argument cleanup on a callee whose body
the compiler had to be told to lower.
```maxon
var cfg = BuildConfig.create(StringArray.create(), output: "app.exe", debug_info: false)

function main() returns ExitCode
	return cfg.sources.count() as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: stdlib-generic-create-reached-as-a-factory-argument -->
Rule 4 through the ARGUMENT rather than through the binding: `Map` is a listed stdlib generic, so
`StrMap.create()` is a call on a stdlib body — and here it is nowhere in the program but INSIDE another
initializer's argument list. The declaration that causes the call therefore has to contribute BOTH
callees to the root set, not just the one it names first. With only the outer one rooted, a program
with nothing wrong with it panics: `panic … requireUnreachableLibraryStayedDead: 'Map.create' is in
LibraryFacts.unreachable`.
```maxon
typealias Count = int(0 to u64.max)
typealias StrMap = Map with (String, Count)

type Holder
	export var table as StrMap

	static function create(table StrMap) returns Self
		return Self{table: table}
	end 'create'
end 'Holder'

var h = Holder.create(StrMap.create())

function main() returns ExitCode
	h.table.upsert("a", value: 6)
	return h.table.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-factory-returning-an-interface-cannot-fill-a-global -->
An interface-typed value is the fat pointer `(value, witness)`, and a global's slot is one machine word, so a
file-scope binding built by a factory that returns an interface is refused at the binding rather than
panicking where the global is later read.
```maxon
typealias Side = int(0 to 100)

interface Shape
	function area() returns Side
end 'Shape'

type Square implements Shape
	let side as Side

	function area() returns Side
		return side
	end 'area'

	static function create(side Side) returns Square
		return Square{side: side}
	end 'create'

	static function shape() returns Shape
		return Square.create(7)
	end 'shape'
end 'Square'

let g = Square.shape()

function main() returns ExitCode
	return g.area() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:24:9: Unsupported: a top-level binding declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a global's slot is one machine word, with nowhere to hold the witness half. Return a concrete type from the initializer, or hold the interface in a LOCAL of a function, which carries its witness as a second value
```

<!-- test: a-factory-returning-an-interface-is-held-by-a-local -->
The same factory in a function body: a local is a second SSA value, so it carries the witness.
```maxon
typealias Side = int(0 to 100)

interface Shape
	function area() returns Side
end 'Shape'

type Square implements Shape
	let side as Side

	function area() returns Side
		return side
	end 'area'

	static function create(side Side) returns Square
		return Square{side: side}
	end 'create'

	static function shape() returns Shape
		return Square.create(7)
	end 'shape'
end 'Square'

function main() returns ExitCode
	let g = Square.shape()
	return g.area() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: error.a-factory-returning-an-enum-at-file-scope-is-not-a-constant -->
An enum is a `named` scalar, so a factory returning one makes the initializer a non-constant scalar, as a
factory returning a ranged alias does.
```maxon
enum Color
	red
	green
end 'Color'

type Maker
	static function color() returns Color
		return Color.green
	end 'color'
end 'Maker'

var g = Maker.color()

function main() returns ExitCode
	if g == Color.green 'isGreen'
		return 4
	end 'isGreen'

	return 0
end 'main'
```
```maxoncstderr
error E2045: <fragment>:13:15: Function calls are not allowed in global variable initializers; 'color()' is not a constant expression
```
