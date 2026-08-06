---
feature: top-level-factory-globals
status: stable
keywords: global, static, factory, module-init, ownership, visibility
category: language
---
# Top-Level Globals Built By A User Factory

## Documentation

A top-level binding may be initialized by a **user static factory call** —
`var db = Database.create(EntryMap.create(), sourcePaths: StringArray.create())`. It is the one
initializer form that is not a fold: nothing is evaluated at compile time, and the record is built
before `main` by `__module_init`, which materializes each argument and then makes an ordinary call.

`map-struct-bytearray.md` is the canonical spec that admits the form. This file pins the six rules
that form has to obey and that its own programs cannot reach, because all of its arguments are
consumed and all of its declarations are in one file:

**1. The callee must be a function, not a builtin container's `create`.** A generic-instance alias
(`typealias IntBox = Box with Count`) is claimed by the CONTAINER door, which routes off the builtin
container registry — `Array` / `Set` / `Map` / `List` / `Vector`. A **user** generic has no entry
there, so it is refused with a sentence naming that, and the same call inside a function is legal.

**2. An argument the callee only BORROWS is still the initializer's to free.** A body's call site
materializes an argument as an owned temporary and drops it at the end of the statement unless the
callee consumed it. `__module_init` has no statement to end, so the drop is emitted right after the
call, for exactly the arguments the callee's consume bits say it did not take. Getting this wrong is
a leak on a program with no error in it — which the exit-101 memory gate is what catches.

**3. The METHOD's visibility is checked, not only the type's.** `__module_init` is a function no file
wrote, so the pass that checks a call's visibility exempts every call it makes; the check therefore
belongs to the initializer's own walk, where the declaring file of the BINDING is known exactly. It
reports the same codes a call in a function body gets: E3008 for a file-private callee, E3088 for a
module-scoped one.

**4. A factory in a LISTED STDLIB MODULE is REACHED by the initializer.** Every pre-elimination pass
skips a stdlib function the program cannot reach (`StdlibFacts.unreachable`), and that reachability
is walked from `main` over the program's own call edges. `__module_init` is not called from `main` —
the entry stub calls it — so a stdlib factory named ONLY by a top-level initializer has to be reached
through the DECLARATION that causes the call, not through the synthesized body that makes it. Getting
this wrong is not a wrong answer but a compiler panic: dead-function elimination roots
`__module_init`, reaches the factory the pre-elimination passes were told was dead, and
`requireUnreachableStdlibStayedDead` fires on a program with nothing wrong with it.

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

**A TUPLE TYPEALIAS IS THE SAME RULE, and leaving it out made the answer a property of the disk.**
`typealias Pair = (Num, Num)` names the tuple's own struct and mints no identity of its own, so a
`returns Pair` is a `named` the sweep must resolve exactly as it resolves a declared type's name — and
the resolution goes through the one door that already knows what a name denotes, the same door the
drop router asks. Without it the same program had TWO answers decided by which file the directory walk
reached first: the alias resolved from the tokens of its own file, so a same-file `Pair` compiled and a
cross-file one was refused. Same program, two answers, is a wrong answer whichever one you prefer.

**6. A recorded type can be CONCRETE and still be the wrong SPELLING, and for a tuple that is a
missing symbol rather than a wrong width.** `Parser.parseTypeReference`'s tuple arm sits outside the
`allFilesFolded` gate its neighbours are behind, so during the declaration sweep a `returns Tagged`
already resolves to the tuple's own struct — through a canonicalizer that is ITSELF gated on that
flag, and therefore hands back the raw spelling at that moment. The sweep records
`__Tuple2.String.Num` where every value of that tuple, and the destructor synthesized for it, carry
`__Tuple2.String.int`. So the drop the cleanup emits names a symbol nothing defines, and the program
dies in the backend.

**The shape of that defect is worth stating exactly, because the obvious reading of it is wrong.** It
looks like "a tuple owning heap cannot be a global" — and measured, `(String, String)` compiled and
ran while `(String, Num)` died. The two differ only in whether the swept spelling and the canonical
one COINCIDE; ownership is merely what makes the difference observable, since a tuple owning nothing
asks for no destructor at all. Refusing heap-owning tuples would therefore have deleted a working
program to tidy a rule. ⇒ the normalization settles the SPELLING, at what is the third door this
index's types leave by; `tuples.md`'s `array-of-managed-element-tuples-drops-each` records the second,
in the same words and with the same panic.

## Tests

<!-- test: error.user-generic-instance-factory-at-file-scope -->
A generic-instance alias over a USER generic is not a builtin container, so it cannot be built before
`main`. The refusal names the base generic and the position, because the identical call is legal
inside a function (see the next test).
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Box'

typealias IntBox = Box with Count

var b = IntBox.create()

function main() returns ExitCode
	return b.n
end 'main'
```
```maxoncstderr
error E2015: <fragment>:14:9: Unsupported: a top-level `IntBox.create()` — `IntBox` instantiates the user generic `Box`, and a global's record is built before `main` by `__module_init` from the BUILTIN container registry's description, which a user generic has none of. Declare it inside a function
```

<!-- test: user-generic-instance-factory-in-a-function -->
The control for the refusal above: the same `IntBox.create()` written inside a function compiles and
runs. What is refused is the POSITION, not the call.
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
		return Self{n: xs.count()}
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
		return Self{kept: kept, n: seen.count(), tag: tag}
	end 'create'
end 'Mixed'

var m = Mixed.create(Bytes.create(), seen: Bytes.create(), tag: "hi")

function main() returns ExitCode
	print("{m.tag}\n")
	return m.n + m.kept.count()
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
	export var n as int

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

var db = Database.create()

function main() returns ExitCode
	return db.n
end 'main'
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
	export var v as int

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
	export var v as int

	export static function make() returns Self
		return Self{v: 7}
	end 'make'
end 'Inner'

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
tuple clause: this spelling already worked, and the fix must not be bought by refusing it.
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
and left bare when it has not — which makes the verdict a property of the DISK. Measured before rule
5's tuple clause, in one directory, on byte-identical source: with the alias's file sorting FIRST the
program compiled and printed `11 22`; with the factory's file sorting first — the spelling below — the
same program was refused as a record `__module_init` cannot reach. Same program, two answers. This
case pins the order that was wrong; the control above is the order that was accidentally right.
```maxon
// --- file: api/a-maker.maxon
export type Maker
	export static function build() returns Pair
		return (11, 22)
	end 'build'
end 'Maker'

// --- file: api/z-pair.maxon
export typealias Num = int(0 to 1000)
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
`__destruct_` synthesized for it — carries `__Tuple2.String.int`; the cleanup after `main` then called
a symbol nothing defines and the backend died with `resolveCallFixups: call to unknown function
'__destruct___Tuple2.String.Num'`. **The neighbouring `(String, String)` case, whose two spellings
coincide, compiled and ran throughout** — which is what proves the cut is at the NAME and not at
owns-heap, and why this case runs rather than being refused. Exit 0 is also the memory gate: the
global's record is built before `main` and released after it, through the destructor that now resolves.
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
swept spelling IS the canonical one. It compiled and ran on the merge base and must keep doing so —
it is the measurement that says the defect above was never "a tuple owns heap", and the reason no
refusal was added for that shape.
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

<!-- test: error.factory-returning-a-generic-alias -->
The other half of the refusal below, pinned rather than asserted: a generic-instance alias keeps the
bare type name as its type for the union's reason, so it gets the union's sentence. Its own base
generic is refused one screen up for a DIFFERENT reason (rule 1, the container registry), which is why
both refusals exist.
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
```maxoncstderr
error E2015: <fragment>:20:15: Unsupported: `Maker.build` as a top-level initializer — it returns 'NumBox', which IS a record but is not one `__module_init` can access a `.data` slot through. A boxed union and a generic alias keep the bare type NAME as their type — a union's is what a `match` reads it by — and the synthesized initializer and cleanup carry that name straight onto the slot's load and store, where there is no width to give it. A struct, a tuple, a String, a Character and a builtin container all resolve to a concrete type and are legal here. Build it inside a function instead
```

<!-- test: error.factory-returning-a-boxed-union -->
A boxed union is a record — a heap pointer, a one-word slot, and a drop the router already handles —
but it is the one record rule 5's normalization cannot reach, because a union value carries the bare
type NAME as its type deliberately: that is what a `match` reads it by. What is missing is not the
slot's WIDTH but a concrete type for the two SYNTHESIZED functions to access the slot through: user
code bridges a declared name to a storage type at every field and every global read, and the
initializer and cleanup this form generates have no such bridge. So it is refused at the declaration,
where the author can see it, rather than panicking three passes later — which is what it did until
this rule was written down. A generic alias is refused by the same sentence; a TUPLE alias is not, and
the sentence used to say it was — see the two tuple cases above, which run.
```maxon
union Shape
	circle(r int)
	square(s int)
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
```
```maxoncstderr
error E2015: <fragment>:13:15: Unsupported: `Maker.build` as a top-level initializer — it returns 'Shape', which IS a record but is not one `__module_init` can access a `.data` slot through. A boxed union and a generic alias keep the bare type NAME as their type — a union's is what a `match` reads it by — and the synthesized initializer and cleanup carry that name straight onto the slot's load and store, where there is no width to give it. A struct, a tuple, a String, a Character and a builtin container all resolve to a concrete type and are legal here. Build it inside a function instead
```

<!-- test: error.cross-file-file-private-factory -->
An `export type` whose `static` factory is NOT exported: naming it from another file's top-level
initializer is the same mistake as naming it from that file's function body, and gets the same
E3008.
```maxon
// --- file: api/db.maxon
export type Database
	export var n as int

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

// --- file: bin/main.maxon
var db = Database.create()

function main() returns ExitCode
	return db.n
end 'main'
```
```maxoncstderr
error E3008: bin/<fragment>:12:19: function 'Database.create' is not exported
```

<!-- test: error.cross-file-module-scoped-factory -->
The `module` tier of the same rule, from a directory outside the declaration's subtree — E3088, the
code and the sentence a module-scoped callee gets at a call in a function body.
```maxon
// --- file: api/db.maxon
export type Database
	export var n as int

	module static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

// --- file: bin/main.maxon
var db = Database.create()

function main() returns ExitCode
	return db.n
end 'main'
```
```maxoncstderr
error E3088: bin/<fragment>:12:19: function 'Database.create' is module-scoped and not visible from this directory
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
this initializer, and the `Array with String` built for `sources` is materialized, borrowed and freed
inside `__module_init` — so the exit-101 gate is asserting the argument cleanup on a callee whose body
the compiler had to be told to lower.
```maxon
typealias Sources = Array with String

var cfg = BuildConfig.create(name: "app", output: "app.exe", sources: Sources.create(), optimize: false, debug_info: false)

function main() returns ExitCode
	return cfg.sources.count() as ExitCode
end 'main'
```
```exitcode
0
```
