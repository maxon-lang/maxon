---
feature: typealias-collision
status: stable
keywords: [typealias, namespace, export, collision, disambiguation, cross-file]
category: parser-edge-cases
---

# Typealias Collision (Namespace Disambiguation)

## Documentation

When two files in different directories both export a typealias with the same bare name, both declarations are accepted at decl time. The collision becomes a **use-site error** when a third file references the bare name without a qualifying namespace prefix:

```text
// api/types.maxon and legacy/types.maxon both export `Score`.
// In app/main.maxon:
let a = 50 as api.Score
let b = 100 as legacy.Score
```

A bare `Score` reference from `app/main.maxon` triggers **E3063** asking the user to qualify it:

```text
error E3063: Ambiguous type name 'Score': more than one visible declaration matches it.
  Qualify it as one of: api.Score, legacy.Score
```

The qualifying namespace is the declaring file's directory (joined with `.` for nested directories — e.g. `lib.fmt.Score` for a file at `lib/fmt/types.maxon`). A declaration at the project root is spelled `export.Score`, and the standard library's is spelled `stdlib.Score`.

**Every visible author declaration counts, and none outranks another** — the reading file's own included: a file that declares a private `Score` and sees another file's exported `Score` is E3063 at its own bare `Score`, and the remedy there is to rename its alias. A nested directory's export competes with its enclosing directory's on equal terms, read from either. The rule covers every kind of type name — ranged, function, generic and tuple aliases, and `type`, `enum`, `union` and `interface` declarations, in any mix — and every position a type name is written in, including a static call's base, a `from` head and a top-level constant's cast target.

What is NOT a competitor:

- **A standard-library declaration.** Beside one visible author declaration of the name, the bare name means the author's; `stdlib.Name` still reaches the library's.
- **A file-private alias** (`typealias` with no modifier) is visible only in its declaring file.
- **A declaration the reader may not see** — a `module` one outside its directory subtree, or a library declaration without `public`.
- **Author declarations, read from a library file.** A library file never sees them.

Two nameable declarations of one name in ONE directory are refused where they are declared (E3061 for two aliases, E3006 otherwise), and so are two in one file, because no qualification could tell them apart. A directory whose path cannot be written as a qualifier is refused when the compile starts (E3182): a top-level piece that is `stdlib`, `export`, `runtime` or a keyword, or any piece that is not a name (`my-dir`).

The rule is the same for a FUNCTION typealias (`export typealias Step = function(…) returns …`): two exported declarations of one bare name earn E3063 at the reference, whether or not the two shapes agree.

This mirrors **E3095** for function-name ambiguity — same model, different registry.

## Tests

<!-- test: error.exported-typealias-collision -->
Two files in different directories both export `Score`. A bare reference from a third file is rejected with E3063. The diagnostic is emitted at the parse site, with exactly the candidate ordering pinned below.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/types.maxon
export typealias Score = int(0 to 200)

// --- file: app/main.maxon
function main() returns ExitCode
	let x = 50 as Score
	return x
end 'main'
```
```maxoncstderr
error E3063: app/specs/typealias-collision/error.exported-typealias-collision.maxon:10:16: Ambiguous type name 'Score': more than one visible declaration matches it. Qualify it as one of: api.Score, legacy.Score
```


<!-- test: exported-typealias-collision-qualified -->
Two files in different directories both export `Score`. A reader file disambiguates by writing `api.Score` and `legacy.Score`. Both qualified forms resolve to the alias declared in the matching directory.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/types.maxon
export typealias Score = int(0 to 80)

// --- file: app/main.maxon
function main() returns ExitCode
	let a = 50 as api.Score
	let b = 60 as legacy.Score
	return (a + (b as api.Score)) as ExitCode
end 'main'
```
```exitcode
110
```


<!-- test: exported-typealias-collision-multi-segment-namespace -->
A collision between a deeply-nested file (`lib/fmt/types.maxon`) and a top-level file (`legacy/types.maxon`) is disambiguated via the full directory chain — `lib.fmt.Score` vs `legacy.Score`. Confirms the parser's dotted-name walk consumes multi-segment qualifiers.
```maxon
// --- file: lib/fmt/types.maxon
export typealias Score = int(0 to 50)

// --- file: legacy/types.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function main() returns ExitCode
	let a = 10 as lib.fmt.Score
	let b = 65 as legacy.Score
	return ((a as legacy.Score) + b) as ExitCode
end 'main'
```
```exitcode
75
```


<!-- test: exported-typealias-no-collision-bare-works -->
When only ONE declaration of a name is visible, the bare name resolves to it without qualification — here `api.Score`, and the same holds for every library name no author file redeclares (`ExitCode`, `ElementIndex`, ...).
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function main() returns ExitCode
	let x = 42 as Score
	return x
end 'main'
```
```exitcode
42
```


<!-- test: project-export-shadows-stdlib-export -->
A project file EXPORTS a typealias whose bare name the standard library also
declares `public` (here `StringArray`, from `stdlib/Json.maxon`). A library declaration
never takes part in ambiguity, so a bare reference from a file that declares neither
means the project's `lib.StringArray` — an array of `ExitCode` here, which the library's
`Array with String` could not hold.
```maxon
// --- file: lib/types.maxon
export typealias StringArray = Array with ExitCode

// --- file: app/main.maxon
function main() returns ExitCode
	var xs = StringArray.create()
	xs.push(40)
	xs.push(2)

	return (try xs.get(0) otherwise 0) + (try xs.get(1) otherwise 0)
end 'main'
```
```exitcode
42
```


<!-- test: nested-export-shadowed-by-enclosing-dir -->
A file in `Compiler/` exports `Tally`, and a file in the nested
`Compiler/Coverage/` subdirectory also exports `Tally`. The reader is a third file
in `Compiler/`, and the enclosing directory's export does not outrank the nested
one: both are visible, so the bare reference is E3063, listing
`Compiler.Coverage.Tally` and `Compiler.Tally`.
```maxon
// --- file: Compiler/types.maxon
export typealias Tally = int(0 to 100)

// --- file: Compiler/Coverage/types.maxon
export typealias Tally = int(0 to 200)

// --- file: Compiler/main.maxon
function main() returns ExitCode
	let x = 42 as Tally
	return x
end 'main'
```
```maxoncstderr
error E3063: Compiler/<fragment>:10:16: Ambiguous type name 'Tally': more than one visible declaration matches it. Qualify it as one of: Compiler.Coverage.Tally, Compiler.Tally
```


<!-- test: module-typealias-file-private-doesnt-collide -->
A file-private `typealias` is invisible across files. When `app/types.maxon` declares a `module` `Score` and `legacy/util.maxon` a file-private `Score`, `app/main.maxon`'s bare `Score` resolves to the `module` one without ambiguity — the file-private alias isn't reachable from outside its declaring file. The `module` tier keeps `app/`'s declaration out of `legacy/util.maxon`'s sight, so neither declaring file sees the other's.
```maxon
// --- file: app/types.maxon
module typealias Score = int(0 to 100)

// --- file: legacy/util.maxon
typealias Score = int(0 to 999)

function legacyCheck(x Score) returns Score
	return x
end 'legacyCheck'

function helper() returns Score
	return legacyCheck(10)
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	let x = 42 as Score
	return x
end 'main'
```
```exitcode
42
```


<!-- test: error.three-way-ambiguous-typealias -->
E3063's candidate list with THREE competitors, declared `zulu`, `alpha`, `mid` and rendered
lexicographically — `insertCandidateName`'s sort, shared with E3095 for the reason given there.
```maxon
// --- file: zulu/t.maxon
export typealias Score = int(0 to 100)

// --- file: alpha/t.maxon
export typealias Score = int(0 to 200)

// --- file: mid/t.maxon
export typealias Score = int(0 to 150)

// --- file: app/main.maxon
function main() returns ExitCode
	let x = 50 as Score
	return x
end 'main'
```
```maxoncstderr
error E3063: app/specs/typealias-collision/error.three-way-ambiguous-typealias.maxon:13:16: Ambiguous type name 'Score': more than one visible declaration matches it. Qualify it as one of: alpha.Score, mid.Score, zulu.Score
```


<!-- test: error.exported-function-alias-collision-is-ambiguous-at-the-reference -->
A FUNCTION typealias collides on the same terms as a ranged one. Two directories export `Step` over the identical shape; the shapes agreeing does not make the bare reference unambiguous, because either declaration is one the reader may legally name.
```maxon
// --- file: api/t.maxon
typealias Integer = int(i64.min to i64.max)
export typealias Step = function(n Integer) returns Integer

// --- file: legacy/t.maxon
typealias Integer = int(i64.min to i64.max)
export typealias Step = function(n Integer) returns Integer

// --- file: app/main.maxon
typealias Integer = int(i64.min to i64.max)

function run(f Step) returns Integer
	return f(1)
end 'run'

function main() returns ExitCode
	return run(function(n Integer) gives n + 1) as ExitCode
end 'main'
```
```maxoncstderr
error E3063: app/specs/typealias-collision/error.exported-function-alias-collision-is-ambiguous-at-the-reference.maxon:13:16: Ambiguous type name 'Step': more than one visible declaration matches it. Qualify it as one of: api.Step, legacy.Step
```

<!-- test: error.exported-function-alias-collision-over-two-shapes-is-still-ambiguous -->
The twin of the case above with the two exported `Step`s declared over DIFFERENT shapes. The answer is the same E3063: disagreeing shapes are a reason the reader cannot be assumed to mean either one, not a way to pick between them.
```maxon
// --- file: api/t.maxon
typealias Integer = int(i64.min to i64.max)
export typealias Step = function(n Integer) returns Integer

// --- file: legacy/t.maxon
export typealias Step = function(s String) returns String

// --- file: app/main.maxon
typealias Integer = int(i64.min to i64.max)

function run(f Step) returns Integer
	return f(1)
end 'run'

function main() returns ExitCode
	return run(function(n Integer) gives n + 1) as ExitCode
end 'main'
```
```maxoncstderr
error E3063: app/specs/typealias-collision/error.exported-function-alias-collision-over-two-shapes-is-still-ambiguous.maxon:12:16: Ambiguous type name 'Step': more than one visible declaration matches it. Qualify it as one of: api.Step, legacy.Step
```

<!-- test: error.ambiguous-typealias-is-anchored-on-the-name-token -->
**WHERE E3063 POINTS**, pinned on its own because an anchor deserves a case that can only be
satisfied one way. The binding name is 21 characters, so the NAME token's column
(36) cannot be confused with the cast operand's (30) or with the statement's. The type token is the one
the user has to rewrite, so it is the one the diagnostic anchors on.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/types.maxon
export typealias Score = int(0 to 200)

// --- file: app/main.maxon
function main() returns ExitCode
	let averyveryverylongname = 50 as Score
	return averyveryverylongname
end 'main'
```
```maxoncstderr
error E3063: app/specs/typealias-collision/error.ambiguous-typealias-is-anchored-on-the-name-token.maxon:10:36: Ambiguous type name 'Score': more than one visible declaration matches it. Qualify it as one of: api.Score, legacy.Score
```


<!-- test: error.exported-cross-form-typealias-collision -->
Two exported aliases of one name in different directories, in DIFFERENT FORMS — one ranged, one
function. They get the same rule the same-form pair gets four cases above, not an `E3061` at
DECLARATION time: two exported declarations are accepted and the ambiguity is a property of the USE. The form they are written in is not what decides
whether a reader can tell them apart. Both are accepted; a bare reference from a third file is E3063,
naming both candidates exactly as the same-form case does.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/types.maxon
export typealias Score = function() returns ExitCode

// --- file: app/main.maxon
function main() returns ExitCode
	let x = 50 as Score
	return x
end 'main'
```
```maxoncstderr
error E3063: app/specs/typealias-collision/error.exported-cross-form-typealias-collision.maxon:10:16: Ambiguous type name 'Score': more than one visible declaration matches it. Qualify it as one of: api.Score, legacy.Score
```


<!-- test: exported-cross-form-typealias-qualified -->
The same pair disambiguated, and it is the half that proves the two declarations both SURVIVED rather
than one having been discarded to silence the diagnostic: `api.Score` is used as a ranged type and
`legacy.Score` as a function type, in one file, at once. A cure that dropped either declaration would
still pass the E3063 case above.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/types.maxon
export typealias Score = function() returns ExitCode

// --- file: app/main.maxon
function run(f legacy.Score) returns ExitCode
	return f()
end 'run'

function seven() returns ExitCode
	return 7
end 'seven'

function main() returns ExitCode
	let a = 35 as api.Score
	return (a as ExitCode) + run(seven)
end 'main'
```
```exitcode
42
```

<!-- test: error.a-user-directory-named-stdlib-cannot-be-a-namespace -->
A project directory named `stdlib` would be a namespace spelled exactly like the library's, so `stdlib.Score`
could name neither declaration for certain. The directory is refused when the program is loaded.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: stdlib/types.maxon
export typealias Score = int(0 to 200)

// --- file: app/main.maxon
function main() returns ExitCode
	let x = 50 as Score
	return x
end 'main'
```
```maxoncstderr
error E3182: Directory 'stdlib' cannot be a namespace: 'stdlib' already names the standard library. Rename the directory
```

<!-- test: an-author-export-beside-the-librarys-alias-over-another-primitive-is-what-the-bare-name-means -->
An author's `float` `Byte` beside the library's `int` one: the two declarations coexist whatever their
underlying primitives, and the bare read from a third file means the author's, because a library
declaration never takes part in ambiguity. `0.75` keeps its fraction only in the `float` alias.
```maxon
// --- file: legacy/types.maxon
export typealias Byte = float(0.0 to 1.0)

// --- file: app/main.maxon
function main() returns ExitCode
	let b = 0.75 as Byte
	return 3 if b > 0.5 else 4
end 'main'
```
```exitcode
3
```

<!-- test: an-author-export-beside-the-librarys-alias-over-another-primitive-is-named-by-its-directory -->
Both declarations stand and both qualified spellings work; `stdlib.Byte` is the same type as the `Byte` a
byte-string literal's elements carry (104 - 7 + 1).
```maxon
// --- file: legacy/types.maxon
export typealias Byte = float(0.0 to 1.0)

// --- file: app/main.maxon
function main() returns ExitCode
	var bytes = b"hi"
	let h = try bytes.get(0) otherwise 0
	let f = 0.5 as legacy.Byte
	let seven = 7 as stdlib.Byte
	return (h - seven + (1 if f > 0.25 else 0)) as ExitCode
end 'main'
```
```exitcode
98
```

<!-- test: a-project-export-and-the-librarys-are-each-named-by-their-directory -->
The qualified twin of `project-export-shadows-stdlib-export`, in a type position and as a static call's base.
```maxon
// --- file: lib/types.maxon
export typealias StringArray = Array with String

// --- file: app/main.maxon
function countMine(xs lib.StringArray) returns ExitCode
	return xs.count() as ExitCode
end 'countMine'

function countTheirs(xs stdlib.StringArray) returns ExitCode
	return xs.count() as ExitCode
end 'countTheirs'

function main() returns ExitCode
	var mine = lib.StringArray.create()
	mine.push("a")
	var theirs = stdlib.StringArray.create()
	theirs.push("b")
	theirs.push("c")
	return (countMine(mine) * 10 + countTheirs(theirs)) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: error.a-generic-alias-exported-by-two-directories-is-ambiguous -->
A generic alias collides on the same terms as a ranged one.
```maxon
// --- file: lib/t.maxon
export typealias Bag = Array with ExitCode

// --- file: alt/u.maxon
export typealias Bag = Array with bool

// --- file: app/main.maxon
function first(b Bag) returns ExitCode
	return try b.get(0) otherwise 0
end 'first'

function main() returns ExitCode
	var b = lib.Bag.create()
	b.push(7)
	return first(b)
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:9:18: Ambiguous type name 'Bag': more than one visible declaration matches it. Qualify it as one of: alt.Bag, lib.Bag
```

<!-- test: generic-tuple-and-nominal-names-are-named-by-their-directory -->
Every kind an E3063 list can name resolves through its qualified spelling (7 + 3 + 5 + 1).
```maxon
// --- file: lib/t.maxon
export type Point
	export var x as ExitCode

	export static function make() returns Point
		return Point{x: 5}
	end 'make'
end 'Point'

export typealias Bag = Array with ExitCode
export typealias Pair = (ExitCode, ExitCode)

// --- file: alt/u.maxon
export typealias Bag = Array with bool
export typealias Pair = (bool, bool)

// --- file: app/main.maxon
function first(b lib.Bag) returns ExitCode
	return try b.get(0) otherwise 0
end 'first'

function sum(p lib.Pair) returns ExitCode
	let (a, b) = p
	return a + b
end 'sum'

function flags(p alt.Pair, marks alt.Bag) returns ExitCode
	let (a, b) = p
	let marked = try marks.get(0) otherwise false
	return 1 if a and not b and marked else 0
end 'flags'

function main() returns ExitCode
	var b = lib.Bag.create()
	b.push(7)
	let p = lib.Point.make()
	let marks = alt.Bag from [true]
	return first(b) + sum((1, 2)) + p.x + flags((true, false), marks: marks)
end 'main'
```
```exitcode
16
```

<!-- test: a-user-type-beside-the-librarys-alias-is-what-the-bare-name-means -->
The cross-KIND pair: a user `type` against `stdlib/Builtins.maxon`'s public `typealias ParsedInt`, read from a
third file as a static call's base. The library's declaration takes no part in ambiguity, so the bare name
is the user's `type`, the only one of the two with a `create`.
```maxon
// --- file: lib/p.maxon
export type ParsedInt
	export let value as ExitCode

	export static function create(value ExitCode) returns ParsedInt
		return Self{value: value}
	end 'create'
end 'ParsedInt'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = ParsedInt.create(7)
	return p.value
end 'main'
```
```exitcode
7
```

<!-- test: a-user-type-and-the-librarys-alias-are-each-named-by-their-directory -->
The qualified twin of the case above.
```maxon
// --- file: lib/p.maxon
export type ParsedInt
	export let value as ExitCode

	export static function create(value ExitCode) returns ParsedInt
		return Self{value: value}
	end 'create'
end 'ParsedInt'

// --- file: app/main.maxon
function valueOf(p lib.ParsedInt) returns ExitCode
	return p.value
end 'valueOf'

function main() returns ExitCode
	let p = lib.ParsedInt.create(7)
	let n = 5 as stdlib.ParsedInt
	return valueOf(p) + (n as ExitCode)
end 'main'
```
```exitcode
12
```

<!-- test: a-user-type-beside-the-librarys-type-is-what-the-bare-name-means -->
A user `type Clock` exported beside `stdlib/Clock.maxon`'s public `Clock`, read bare from a third file. The
library's declaration moves out of the user's way (`__Clock`) and takes no part in ambiguity, so the bare
name is the user's type, the one with a `make`.
```maxon
// --- file: lib/clock.maxon
export type Clock
	export var ticks as ExitCode

	export static function make() returns Clock
		return Clock{ticks: 7}
	end 'make'
end 'Clock'

// --- file: app/main.maxon
function main() returns ExitCode
	let c = Clock.make()
	return c.ticks
end 'main'
```
```exitcode
7
```

<!-- test: a-user-type-and-the-librarys-type-are-each-named-by-their-directory -->
`stdlib.X` reaches the library's type and `lib.X` the user's — in a struct FIELD (recorded by the declaration
sweep), a throws clause, a static call and an enum case (7 + 10 + 2 + 20).
```maxon
// --- file: lib/clock.maxon
export type Clock
	export var ticks as ExitCode

	export static function make() returns Clock
		return Clock{ticks: 7}
	end 'make'
end 'Clock'

// --- file: lib/errors.maxon
export enum ParseError implements Error
	mine
	yours
end 'ParseError'

// --- file: app/main.maxon
type Holder
	export var mine as lib.Clock
	export var theirs as stdlib.ParseError
	export var ours as lib.ParseError

	static function make() returns Holder
		return Holder{mine: lib.Clock.make(), theirs: stdlib.ParseError.invalidFormat, ours: lib.ParseError.yours}
	end 'make'
end 'Holder'

function parsed(text String) returns ExitCode throws stdlib.ParseError
	return (try int.fromString(text)) as ExitCode
end 'parsed'

function main() returns ExitCode
	let h = Holder.make()
	let started = stdlib.Clock.nowMs()
	let theirs = match h.theirs 'which'
		invalidFormat gives 10
	end 'which'
	let ours = match h.ours 'which'
		mine gives 1
		yours gives 2
	end 'which'
	let n = try parsed("x") otherwise 20
	return h.mine.ticks + theirs + ours + n + (0 if started >= 0 else 1)
end 'main'
```
```exitcode
39
```

<!-- test: error.an-ambiguous-array-from-head-is-refused -->
Two directories export an `Array` instance alias of one name, so a bare `from` head in a third file could build
either and must be qualified.
```maxon
// --- file: api/types.maxon
export typealias Small = int(0 to 255)
export typealias Smalls = Array with Small

// --- file: legacy/types.maxon
export typealias Smalls = Array with bool

// --- file: app/main.maxon
function main() returns ExitCode
	let xs = Smalls from [1, 2]
	print("{xs.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:11:11: Ambiguous type name 'Smalls': more than one visible declaration matches it. Qualify it as one of: api.Smalls, legacy.Smalls
```

<!-- test: a-bare-literal-init-head-beside-the-librarys-builds-the-users-type -->
A user `FilePath` exported beside the library's: a bare `FilePath from "…"` in a third file builds the user's
type, because the library's declaration takes no part in ambiguity. Only the user's has a `text` field.
```maxon
// --- file: lib/path.maxon
export type FilePath implements InitableFromStringLiteral
	export let text as String

	export static function init(value String) returns FilePath
		return Self{text: value}
	end 'init'
end 'FilePath'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = FilePath from "x"
	print("{p.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
x
```

<!-- test: a-qualified-literal-init-head-names-the-type-it-qualifies -->
The qualified spelling of that `FilePath` builds the user's type.
```maxon
// --- file: lib/path.maxon
export type FilePath implements InitableFromStringLiteral
	export let text as String

	export static function init(value String) returns FilePath
		return Self{text: value}
	end 'init'
end 'FilePath'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = lib.FilePath from "abc"
	return p.text.byteLength() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-qualified-array-from-head-names-the-alias-it-qualifies -->
The spelling E3063 prescribes for that `Smalls` builds the `Array` instance its directory declares.
```maxon
// --- file: api/types.maxon
export typealias Smalls = Array with ExitCode

// --- file: legacy/types.maxon
export typealias Smalls = Array with bool

// --- file: app/main.maxon
function main() returns ExitCode
	let xs = api.Smalls from [1, 2, 3]
	let flags = legacy.Smalls from [true]
	return (xs.count() * flags.count()) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-qualified-set-from-head-names-the-alias-it-qualifies -->
A user `CharSet` exported beside the library's: each qualified head builds the set its own declaration states.
```maxon
// --- file: api/chars.maxon
export typealias CharSet = Set with Character

// --- file: app/main.maxon
function main() returns ExitCode
	let mine = api.CharSet from ['a', 'b']
	let theirs = stdlib.CharSet from ['x', 'y', 'z']
	return (mine.count() * 10 + theirs.count()) as ExitCode
end 'main'
```
```exitcode
23
```

<!-- test: a-qualified-array-from-head-names-the-alias-it-qualifies-at-top-level -->
A top-level constant's qualified `from` head builds the `Array` instance its directory declares, as a body's does.
```maxon
// --- file: api/types.maxon
export typealias Smalls = Array with ExitCode

// --- file: legacy/types.maxon
export typealias Smalls = Array with bool

// --- file: app/main.maxon
let Xs = api.Smalls from [1, 2, 3]
let Flags = legacy.Smalls from [true]

function main() returns ExitCode
	return (Xs.count() * Flags.count()) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-qualified-set-from-head-names-the-alias-it-qualifies-at-top-level -->
Each qualified `CharSet` head of a top-level constant builds the set its own declaration states.
```maxon
// --- file: api/chars.maxon
export typealias CharSet = Set with Character

// --- file: app/main.maxon
let Mine = api.CharSet from ['a', 'b']
let Theirs = stdlib.CharSet from ['x', 'y', 'z']

function main() returns ExitCode
	return (Mine.count() * 10 + Theirs.count()) as ExitCode
end 'main'
```
```exitcode
23
```

<!-- test: error.an-ambiguous-top-level-cast-target-is-refused -->
A top-level constant's cast target is a type position like any other, so an ambiguous one is refused there too.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/types.maxon
export typealias Score = int(0 to 200)

// --- file: app/main.maxon
let K = 50 as Score

function main() returns ExitCode
	return K
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:9:15: Ambiguous type name 'Score': more than one visible declaration matches it. Qualify it as one of: api.Score, legacy.Score
```

<!-- test: two-library-module-aliases-no-reader-can-both-name-coexist -->
Two `module` aliases of one name in two library directories neither of which contains the other: no file can
name both, so neither is a duplicate of the other.
```maxon
// --- stdlib-overlay: helpers/string/hash.maxon
module typealias OverlayLocalWidth = int(0 to 7)
// --- stdlib-overlay: helpers/hashtable/slotScan.maxon
module typealias OverlayLocalWidth = int(0 to 9)
// --- file: main.maxon
function main() returns ExitCode
	return 3
end 'main'
```
```exitcode
3
```
