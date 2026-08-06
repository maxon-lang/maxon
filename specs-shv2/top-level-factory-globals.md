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

`map-struct-bytearray.md` is the canonical spec that admits the form. This file pins the four rules
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

	static function make() returns Self
		return Self{v: 7}
	end 'make'
end 'Inner'

// --- file: api/outer.maxon
export type Outer
	export function build() returns Inner
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

<!-- test: factory-returning-a-boxed-union -->
A boxed union is a record for every purpose this form has: its value is a heap pointer, its slot is
the same one word a struct's is, and the drop router already routes it. It is the ONE record a
declared name can denote that the normalization above cannot reach — a union value carries the bare
name deliberately, since that is what a `match` reads it by — so admitting it is a decision the slot
had to make rather than one the re-tag makes for it.
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
