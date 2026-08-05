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

`map-struct-bytearray.md` is the canonical spec that admits the form. This file pins the three rules
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
