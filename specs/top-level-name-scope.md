---
feature: top-level-name-scope
status: stable
keywords: [scope, directory, namespace, duplicate, type, function, let, typealias, shadowing]
category: diagnostics
---

# One top-level name, one declaration, per directory

## Documentation

A top-level name is scoped by its **directory**, in one namespace, as a package block is in Go. Every
top-level declaration claims its name once per directory, whatever its kind and whether it is unmarked,
`export` or `public`: a `type`, `enum`, `union`, `interface`, `typealias`, top-level `let` or `var`, and
`function` all share it. A second declaration of the name in the same directory is refused at that
declaration, in one file or across the files of the directory, and the declaration kept is the one in the
file whose path sorts first.

```text
error E3006: Point.maxon:1:10: duplicate definition of 'Point' — already declared as `type Point`
```

Directories are separate scopes, so a name two directories each declare for themselves is two names.
Locals and parameters keep block scope: one may shadow a top-level name.

A bare top-level name resolves the same way for every kind of declaration — types, enums, unions,
interfaces, typealiases, `let` and `var` bindings and functions — in this order, and the first step that has
a declaration decides:

1. the reading file's own directory's declaration;
2. else the project root's;
3. else the one declaration the file may name;
4. else it is ambiguous: E3063 for a type name and E3095 for a binding or a function, each listing the
   qualified spellings that remove the ambiguity.

A program's own declarations may shadow the standard library's and the runtime tier's: they are different
scopes.

## Tests

<!-- test: error.a-type-and-a-function-of-one-name-in-two-files-of-one-directory -->
`a.maxon` declares `type Point` and `b.maxon`, in the same directory, `function Point`. The two share one
name, and the later in path order is the one refused.
```maxon
// --- file: a.maxon
export type Point
	export var x as ExitCode
end 'Point'

// --- file: b.maxon
export function Point() returns ExitCode
	return 1
end 'Point'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:8:17: duplicate definition of 'Point' — already declared as `type Point`
```

<!-- test: error.a-function-and-a-type-of-one-name-in-two-files-of-one-directory -->
The same pair with the files the other way round: `a.maxon` declares the `function` and `b.maxon` the `type`,
so the `type` is the one refused.
```maxon
// --- file: a.maxon
export function Point() returns ExitCode
	return 1
end 'Point'

// --- file: b.maxon
export type Point
	export var x as ExitCode
end 'Point'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:8:13: duplicate definition of 'Point' — already declared as `function Point`
```

<!-- test: error.a-typealias-and-a-let-of-one-name-in-one-file -->
One file declares `typealias Count` and then `let Count`. Two kinds of declaration of one name collide
inside a single file too.
```maxon
typealias Count = int(0 to 100)

let Count = 5

function twice(n Count) returns Count
	return n
end 'twice'

function main() returns ExitCode
	return twice(3)
end 'main'
```
```maxoncstderr
error E3006: <fragment>:4:5: duplicate definition of 'Count' — already declared as `typealias Count`
```

<!-- test: error.an-enum-and-a-function-of-one-name -->
An `enum` and a `function` of one name collide, and the message names both kinds with the article each
needs.
```maxon
enum Mode
	fast
	slow
end 'Mode'

function Mode() returns ExitCode
	return 1
end 'Mode'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:7:10: duplicate definition of 'Mode' — already declared as `enum Mode`
```

<!-- test: error.a-let-and-a-function-of-one-name-in-two-files-of-one-directory -->
A `let` and a `function` of one name in two files of one directory collide.
```maxon
// --- file: a.maxon
let limit = 5

// --- file: b.maxon
function limit() returns ExitCode
	return 1
end 'limit'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:6:10: duplicate definition of 'limit' — already declared as `let limit`
```

<!-- test: error.two-unmarked-types-of-one-name-in-two-files-of-one-directory -->
Two unmarked `type Box` declarations in two files of one directory collide.
```maxon
// --- file: a.maxon
type Box
	export var n as ExitCode

	static function make() returns Self
		return Self{n: 7}
	end 'make'
end 'Box'

export function runA() returns ExitCode
	return Box.make().n
end 'runA'

// --- file: b.maxon
type Box
	export var label as String

	static function make() returns Self
		return Self{label: "b"}
	end 'make'
end 'Box'

export function runB() returns ExitCode
	return Box.make().label.count() as ExitCode
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	return runA() + runB()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:16:6: duplicate definition of 'Box' — already declared as `type Box`
```

<!-- test: two-directories-each-declare-an-unmarked-typealias-let-and-function-of-one-name -->
Directories are separate scopes: `a/` and `b/` each declare an unmarked `typealias Total`, `let step` and
`function bump`, each file means its own, and the program compiles and runs.
```maxon
// --- file: a/a.maxon
typealias Total = int(0 to 100)

let step = 3

function bump(n Total) returns Total
	return n + step
end 'bump'

export function runA() returns ExitCode
	return bump(4) as ExitCode
end 'runA'

// --- file: b/b.maxon
typealias Total = int(0 to 50)

let step = 10

function bump(n Total) returns Total
	return n + step
end 'bump'

export function runB() returns ExitCode
	return bump(5) as ExitCode
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	return runA() + runB()
end 'main'
```
```exitcode
22
```

<!-- test: a-local-and-a-parameter-shadow-a-top-level-name -->
A parameter shadows a top-level `let` and another a top-level `function`, and a local shadows the `let` too:
block scope is untouched by the directory's namespace.
```maxon
let width = 7

function double(n ExitCode) returns ExitCode
	return n * 2
end 'double'

function pair(width ExitCode, double ExitCode) returns ExitCode
	return width + double
end 'pair'

function inner() returns ExitCode
	let width = 3

	return width
end 'inner'

function main() returns ExitCode
	return pair(double(width), double: 1) + inner()
end 'main'
```
```exitcode
18
```

<!-- test: a-bare-type-name-means-the-readers-own-directorys-declaration -->
`a/` and `b/` each export a `type Box`. A function in each directory writes the bare `Box`, and each means its
own directory's: `a/`'s has the field `v` and `b/`'s the field `w`. `app/main.maxon` declares neither and
reaches each through its directory.
```maxon
// --- file: a/box.maxon
export type Box
	export let v as ExitCode

	export static function make() returns Box
		return Box{v: 3}
	end 'make'
end 'Box'

export function readA() returns ExitCode
	let box = Box.make()

	return box.v
end 'readA'

// --- file: b/box.maxon
export type Box
	export let w as ExitCode

	export static function make() returns Box
		return Box{w: 40}
	end 'make'
end 'Box'

export function readB() returns ExitCode
	let box = Box.make()

	return box.w
end 'readB'

// --- file: app/main.maxon
function main() returns ExitCode
	let fromA = a.Box.make()
	let fromB = b.Box.make()

	return readA() + readB() + fromA.v - fromB.w
end 'main'
```
```exitcode
6
```

<!-- test: a-bare-enum-and-union-name-mean-the-readers-own-directorys-declaration -->
`a/` exports an `enum Mode` and `b/` a `union Mode`. Each directory's `match` over a `Mode` it writes bare lists
that directory's cases, so the two kinds coexist and neither reader is asked to qualify.
```maxon
// --- file: a/mode.maxon
export enum Mode
	fast
	slow
end 'Mode'

export function describeA(m Mode) returns ExitCode
	return match m 'm'
		fast gives 1
		slow gives 2
	end 'm'
end 'describeA'

// --- file: b/mode.maxon
export union Mode
	idle
	busy(load ExitCode)
end 'Mode'

export function describeB(m Mode) returns ExitCode
	return match m 'm'
		idle gives 10
		busy(load) gives load
	end 'm'
end 'describeB'

// --- file: app/main.maxon
function main() returns ExitCode
	return describeA(a.Mode.slow) + describeB(b.Mode.busy(30))
end 'main'
```
```exitcode
32
```

<!-- test: a-bare-alias-name-means-the-readers-own-directorys-declaration -->
`a/` and `b/` each export a `typealias Score`, over `int(0 to 100)` and `int(0 to 200)`. Each directory's
function takes and returns its own bare `Score`, and `app/main.maxon` hands each directory a value of the alias
that directory declares: a bare `Score` read as the other directory's would reject the argument.
```maxon
// --- file: a/score.maxon
export typealias Score = int(0 to 100)

export function keepA(n Score) returns Score
	return n
end 'keepA'

// --- file: b/score.maxon
export typealias Score = int(0 to 200)

export function keepB(n Score) returns Score
	return n
end 'keepB'

// --- file: app/main.maxon
function seedA() returns a.Score
	return 50
end 'seedA'

function seedB() returns b.Score
	return 150
end 'seedB'

function main() returns ExitCode
	return (keepA(seedA()) as ExitCode) + (keepB(seedB()) as ExitCode) - 158
end 'main'
```
```exitcode
42
```

<!-- test: a-root-declaration-is-what-the-bare-name-means-beside-a-subdirectorys -->
The root declares an unmarked `type Box` and `sub/` exports another. `other/o.maxon` sees both and declares neither, so its bare
`Box` is the root's; `sub/box.maxon` means its own directory's, which is nearer than the root's. `main.maxon`
reaches `sub/`'s through its directory.
```maxon
// --- file: main.maxon
type Box
	export let v as ExitCode

	static function make() returns Box
		return Box{v: 38}
	end 'make'
end 'Box'

function main() returns ExitCode
	let inSub = sub.Box.make()

	return fromOther() + fromSub() + inSub.w
end 'main'

// --- file: sub/box.maxon
export type Box
	export let w as ExitCode

	export static function make() returns Box
		return Box{w: 2}
	end 'make'
end 'Box'

export function fromSub() returns ExitCode
	return Box.make().w
end 'fromSub'

// --- file: other/o.maxon
export function fromOther() returns ExitCode
	return Box.make().v
end 'fromOther'
```
```exitcode
42
```

<!-- test: error.a-bare-type-name-is-ambiguous-where-neither-the-readers-directory-nor-the-root-declares-it -->
`a/` and `b/` each export a `type Box`, and `c/` declares none. A bare `Box` in `c/` has no nearer declaration to
mean, so it is E3063 and lists both directories' spellings.
```maxon
// --- file: a/box.maxon
export type Box
	export let v as ExitCode

	export static function make() returns Box
		return Box{v: 3}
	end 'make'
end 'Box'

// --- file: b/box.maxon
export type Box
	export let w as ExitCode

	export static function make() returns Box
		return Box{w: 40}
	end 'make'
end 'Box'

// --- file: c/use.maxon
export function readC() returns ExitCode
	let box = Box.make()

	return 1
end 'readC'

// --- file: app/main.maxon
function main() returns ExitCode
	return readC()
end 'main'
```
```maxoncstderr
error E3063: c/<fragment>:22:12: Ambiguous type name 'Box': more than one visible declaration matches it. Qualify it as one of: a.Box, b.Box
```

<!-- test: error.a-diagnostic-names-a-nested-directorys-type-whose-leaf-directory-also-declares-one -->
`deep/` and `sub/deep/` each export a `type Box`, and the file in `sub/deep/` reads a field `Box` does not have. The
diagnostic names the type as `sub.deep.Box`: the spelling of `deep/`'s type does not stand inside the longer one.
```maxon
// --- file: deep/box.maxon
export type Box
	export let v as ExitCode

	export static function make() returns Box
		return Box{v: 3}
	end 'make'
end 'Box'

// --- file: deep/use.maxon
export function readDeep() returns ExitCode
	return Box.make().v
end 'readDeep'

// --- file: sub/deep/box.maxon
export type Box
	export let v as ExitCode

	export static function make() returns Box
		return Box{v: 5}
	end 'make'
end 'Box'

// --- file: sub/deep/use.maxon
export function readNested() returns ExitCode
	return Box.make().zz
end 'readNested'

// --- file: app/main.maxon
function main() returns ExitCode
	return readDeep() + readNested()
end 'main'
```
```maxoncstderr
error E3018: sub/deep/<fragment>:27:20: type 'sub.deep.Box' has no field named 'zz'
```

<!-- test: error.a-diagnostic-names-a-nested-directorys-type-declared-before-the-leaf-directorys -->
The same pair with the nested directory sorting first (`a/deep/` before `deep/`), so the two orders of one program say
the same thing.
```maxon
// --- file: a/deep/box.maxon
export type Box
	export let v as ExitCode

	export static function make() returns Box
		return Box{v: 3}
	end 'make'
end 'Box'

// --- file: a/deep/use.maxon
export function readNested() returns ExitCode
	return Box.make().zz
end 'readNested'

// --- file: deep/box.maxon
export type Box
	export let v as ExitCode

	export static function make() returns Box
		return Box{v: 5}
	end 'make'
end 'Box'

// --- file: deep/use.maxon
export function readDeep() returns ExitCode
	return Box.make().v
end 'readDeep'

// --- file: app/main.maxon
function main() returns ExitCode
	return readDeep() + readNested()
end 'main'
```
```maxoncstderr
error E3018: a/deep/<fragment>:13:20: type 'a.deep.Box' has no field named 'zz'
```
