---
feature: directory-scope
status: stable
keywords: [visibility, directory, subtree, export, field, E3008, E3014, E3092, E3167, E3219]
category: diagnostics
---

# Directory scope — an unmarked declaration belongs to its directory's subtree

## Documentation

A declaration written with no visibility modifier is visible to every file in its declaring directory and in
that directory's subdirectories, and to no other file. That holds for a top-level `function`, `let`, `type`,
`enum`, `union`, `interface` and `typealias`, for a method and a static member, for a nested typealias, and for
a field. `export` makes a declaration visible to every file of the program, and `public` to every program. No
tier narrower than a directory exists.

A file outside the subtree that names an unmarked declaration is refused where it writes the name:
**E3008** `<kind> '<name>' is not exported`, and **E3014** `field '<Type>.<field>' is not exported` for a field.
A field's directory is its type's.

The root directory contains every file, so an unmarked declaration there is visible to the whole program.

### The subtree test is by SEGMENT, never by character prefix

`feature2/` is not inside `feature/`. A prefix test that forgot the separator would say it was, and
that is a wrong ANSWER about what another directory may name — not a missing diagnostic.

### A DOT IS AN ORDINARY CHARACTER IN A DIRECTORY NAME

`feature.extra/` and `my.dir/` are directories like any other, and the language's `.`-joined
NAMESPACE spelling is a rendering of a directory, never a stand-in for one. An implementation that
decides the subtree question on the rendered string reintroduces the prefix bug through the joiner
itself: `feature.extra` starts with `feature.`, and `my.dir/` renders exactly like `my/dir/`. Both
are wrong ANSWERS — one admits a sibling into the subtree, the other merges two unrelated
directories — and neither is visible in any test whose directory names happen to have no dots.

### The audits draw the same boundary

An `export` declaration that nothing outside its declaring directory's subtree names is **E3092**: an unmarked
declaration already serves every reader it has. `public` is exempt. An unmarked declaration that nothing live in
its subtree names is **E3219**. An exported surface that names an unmarked declaration is **E3167**, which calls
the unmarked tier `unexported`.

### `module` is an identifier

`module` names nothing special. Written where a modifier would go, it is a stray word, and the parser refuses it
as it refuses any other.

## Tests

<!-- test: an-unmarked-function-is-visible-to-another-file-of-its-directory -->
```maxon
// --- file: helper.maxon
function helper() returns ExitCode
	return 42
end 'helper'

// --- file: main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: every-unmarked-declaration-kind-is-visible-to-another-file-of-its-directory -->
A type, its static factory and its method, a typealias used as a cast target, and a top-level `let`, all
unmarked in `lib.maxon` and all named from `main.maxon` (30 + 7 + 5).
```maxon
// --- file: lib.maxon
typealias Score = int(0 to 100)

let BONUS = 5 as Score

type Counter
	var total as Score

	static function create(start Score) returns Self
		return Self{total: start}
	end 'create'

	function added(n Score) returns Score
		return total + n
	end 'added'
end 'Counter'

// --- file: main.maxon
function main() returns ExitCode
	let c = Counter.create(30)
	return c.added((7 as Score) + BONUS) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: an-unmarked-function-is-visible-from-a-subdirectory-reached-through-an-export -->
`feature/sub/b.maxon` sits inside `feature/`'s subtree, so it calls `feature/a.maxon`'s unmarked `helper`; the
root reaches `feature/sub/` through its exported `bridge`.
```maxon
// --- file: feature/a.maxon
function helper() returns ExitCode
	return 40
end 'helper'

// --- file: feature/sub/b.maxon
export function bridge() returns ExitCode
	return helper() + 2
end 'bridge'

// --- file: main.maxon
function main() returns ExitCode
	return bridge()
end 'main'
```
```exitcode
42
```

<!-- test: error.an-unmarked-function-is-not-visible-from-a-sibling-directory -->
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

// --- file: other/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3008: other/<fragment>:11:9: function 'helper' is not exported
```

<!-- test: an-unmarked-field-is-read-and-written-by-another-type-and-a-sibling-files-extension -->
`Filler` is another type in `Box`'s file, and `grow.maxon` holds an `extension Box` in the same directory: both
read and write the unmarked `v` (11 + 31).
```maxon
// --- file: box.maxon
type Box
	var v as ExitCode

	static function create() returns Self
		return Self{v: 1}
	end 'create'
end 'Box'

type Filler
	var step as ExitCode

	static function create() returns Self
		return Self{step: 10}
	end 'create'

	function fill() returns ExitCode
		let b = Box.create()
		b.v = b.v + step
		return b.v
	end 'fill'
end 'Filler'

// --- file: grow.maxon
extension Box
	function grown() returns ExitCode
		self.v = self.v + 20
		return v + 10
	end 'grown'
end 'Box'

// --- file: main.maxon
function main() returns ExitCode
	let filled = Filler.create().fill()
	let b = Box.create()
	return filled + b.grown()
end 'main'
```
```exitcode
42
```

<!-- test: error.an-unmarked-field-is-not-readable-outside-the-subtree -->
The TYPE is exported and the FIELD is unmarked, so the reader may name `Box` and may not read `v`.
```maxon
// --- file: feature/box.maxon
export typealias Integer = int(i64.min to i64.max)

export type Box
	var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

// --- file: other/main.maxon
function main() returns ExitCode
	let b = Box.create(42)
	return b.v
end 'main'
```
```maxoncstderr
error E3014: other/<fragment>:16:11: field 'Box.v' is not exported
```

<!-- test: an-unmarked-field-is-readable-from-a-subdirectory -->
```maxon
// --- file: feature/box.maxon
public type Box
	var v as ExitCode

	export static function create(v ExitCode) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

// --- file: feature/sub/main.maxon
function main() returns ExitCode
	let b = Box.create(42)
	return b.v
end 'main'
```
```exitcode
42
```

<!-- test: error.module-before-a-declaration-is-a-stray-word -->
```maxon
module function helper() returns ExitCode
	return 42
end 'helper'

function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:1: Expected function declaration, got 'module'
error E3004: <fragment>:7:9: call to undefined function 'helper'
```

<!-- test: error.module-before-a-member-is-a-stray-word -->
```maxon
type Box
	module var n as Count
end 'Box'

function main() returns ExitCode
	return 0
end 'main'

typealias Count = int(0 to 100)
```
```maxoncstderr
error E2010: <fragment>:3:2: Expected 'function' but got 'module'
```

<!-- test: error.module-after-export-is-a-stray-word -->
```maxon
export module let LIMIT = 21

function main() returns ExitCode
	return LIMIT
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: Expected function declaration, got 'module'
```

<!-- test: error.an-export-read-only-inside-its-directory-is-reported -->
`helper` is read by `lib/b.maxon` alone, which an unmarked `helper` would serve as well.
```maxon
// --- file: lib/a.maxon
export function helper() returns ExitCode
	return 40
end 'helper'

// --- file: lib/b.maxon
export function bridge() returns ExitCode
	return helper() + 2
end 'bridge'

// --- file: main.maxon
function main() returns ExitCode
	return bridge()
end 'main'
```
```maxoncstderr
error E3092: lib/<fragment>:3:17: exported function 'lib.helper' is never referenced outside its declaring directory
```

<!-- test: error.an-unmarked-function-nothing-in-its-directory-names-is-unused -->
```maxon
// --- file: lib/a.maxon
function helper() returns ExitCode
	return 40
end 'helper'

// --- file: lib/b.maxon
export function answer() returns ExitCode
	return 42
end 'answer'

// --- file: main.maxon
function main() returns ExitCode
	return answer()
end 'main'
```
```maxoncstderr
error E3219: lib/<fragment>:3:10: unused function: 'helper' is not exported and nothing live in its directory names it; delete it
```

<!-- test: error.an-exported-function-naming-an-unmarked-typealias-is-reported -->
```maxon
// --- file: api/lib.maxon
typealias ElementIndex = int(0 to 100)

export function f(i ElementIndex) returns ExitCode
	return i as ExitCode
end 'f'

// --- file: app/main.maxon
function main() returns ExitCode
	return f(7)
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:5:17: exported function 'api.f' names unexported typealias 'ElementIndex' in the type of parameter 'i'
```

<!-- test: module-is-an-ordinary-identifier -->
`module` names a top-level constant, a field, a static factory's parameter, a free function's
parameter and a local `var`.
```maxon
typealias Integer = int(i64.min to i64.max)

let module = 5

type Holder
	export var module as Integer

	static function create(module Integer) returns Self
		return Self{module: module}
	end 'create'
end 'Holder'

function twice(module Integer) returns Integer
	var doubled = module
	doubled = doubled + module
	return doubled
end 'twice'

function main() returns ExitCode
	let h = Holder.create(16)
	var module = 5 as Integer
	module = module + 1
	return (twice(module) + h.module + module) as ExitCode
end 'main'
```
```exitcode
34
```

<!-- test: a-function-in-a-subdirectory-is-visible-to-its-sibling-file -->
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

// --- file: feature/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: an-unmarked-function-is-visible-from-a-deeper-subdirectory -->
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

// --- file: feature/sub/deep.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: error.an-unmarked-function-is-not-visible-from-the-parent-directory -->
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

// --- file: main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3008: <fragment>:11:9: function 'helper' is not exported
```

<!-- test: a-type-and-its-fields-are-visible-to-a-sibling-file -->
```maxon
// --- file: feature/point.maxon
typealias Integer = int(i64.min to i64.max)

type Point
	var x as Integer
	var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

// --- file: feature/main.maxon
function main() returns ExitCode
	let p = Point.create(20, y: 22)
	return p.x + p.y
end 'main'
```
```exitcode
42
```

<!-- test: an-unmarked-enum-is-visible-to-a-sibling-file -->
```maxon
// --- file: feature/color.maxon
enum Color
	red
	green
	blue
end 'Color'

// --- file: feature/main.maxon
function main() returns ExitCode
	let c = Color.blue
	match c 'check'
		blue then return 42
		red then return 0
		green then return 0
	end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: unmarked-functions-call-each-other-across-three-files-of-a-directory -->
```maxon
// --- file: feature/inner.maxon
typealias Integer = int(i64.min to i64.max)

function inner() returns Integer
	return 22
end 'inner'

// --- file: feature/outer.maxon
typealias Int = int(i64.min to i64.max)

function outer() returns Int
	return inner() + 20
end 'outer'

// --- file: feature/main.maxon
function main() returns ExitCode
	return outer()
end 'main'
```
```exitcode
42
```

<!-- test: an-unmarked-let-is-readable-from-a-sibling-files-constant-initializer -->
```maxon
// --- file: feature/limits.maxon
let LIMIT = 21

// --- file: feature/main.maxon
let DOUBLE = LIMIT * 2

function main() returns ExitCode
	return DOUBLE
end 'main'
```
```exitcode
42
```

<!-- test: an-unmarked-let-is-readable-from-a-constant-initializer-in-a-subdirectory -->
```maxon
// --- file: feature/limits.maxon
let LIMIT = 21

// --- file: feature/sub/main.maxon
let DOUBLE = LIMIT * 2

function main() returns ExitCode
	return DOUBLE
end 'main'
```
```exitcode
42
```

<!-- test: error.an-unmarked-let-is-not-readable-from-another-directorys-constant-initializer -->
The compiler collects every file's constant declarations before folding any of them, and that does not widen
what a file may read.
```maxon
// --- file: feature/limits.maxon
let LIMIT = 21

// --- file: other/main.maxon
let DOUBLE = LIMIT * 2

function main() returns ExitCode
	return DOUBLE
end 'main'
```
```maxoncstderr
error E2004: other/<fragment>:6:14: Undefined variable 'LIMIT'
```

<!-- test: error.an-unmarked-let-is-not-readable-from-another-directorys-body -->
```maxon
// --- file: feature/limits.maxon
let LIMIT = 21

// --- file: other/main.maxon
function main() returns ExitCode
	let double = LIMIT * 2
	return double
end 'main'
```
```maxoncstderr
error E2004: other/<fragment>:7:15: Undefined variable 'LIMIT'
```

<!-- test: error.a-sibling-directory-sharing-a-name-prefix-is-outside-the-subtree -->
`feature2/` is not a subdirectory of `feature/`. A character-prefix subtree test would admit it.
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

// --- file: feature2/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3008: feature2/<fragment>:11:9: function 'helper' is not exported
```

<!-- test: error.a-sibling-directory-whose-name-contains-a-dot-is-outside-the-subtree -->
`feature.extra/` is a SIBLING of `feature/`, not a subdirectory of it. Its `.`-joined namespace is
`feature.extra`, which begins with `feature.` — so a subtree test asked on namespaces rather than on
directories admits it, and the separator guard that catches `feature2/` walks straight past.
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

// --- file: feature.extra/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3008: feature.extra/<fragment>:11:9: function 'helper' is not exported
```

<!-- test: error.a-dotted-directory-is-not-the-nested-directory-it-renders-like -->
`my.dir/` and `my/dir/` are different directories that render to the same `.`-joined namespace. The
reader is in neither the declaring directory nor beneath it.
```maxon
// --- file: my.dir/helper.maxon
typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

// --- file: my/dir/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3008: my/dir/<fragment>:11:9: function 'helper' is not exported
```

<!-- test: a-directory-name-containing-a-dot-is-its-own-subtree -->
The converse of the two refusals above: a dot in a directory name costs the directory nothing. Its
own subdirectory is inside its subtree exactly as any other subdirectory is.
```maxon
// --- file: feature.extra/helper.maxon
typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

// --- file: feature.extra/sub/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: a-root-level-unmarked-declaration-is-visible-from-a-subdirectory -->
The root directory contains every file, so an unmarked declaration written there is visible
program-wide.
```maxon
// --- file: root.maxon
typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

// --- file: sub/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: error.a-root-typealias-only-a-library-file-writes-is-unused -->
A library file is in no author's directory, so the root alias it happens to spell credits nothing:
`stdlib/Builtins.maxon` writes `DecimalPointScale` in its own signatures, and the program's alias of that name
is still unused.
```maxon
typealias DecimalPointScale = int(0 to 100)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3062: <fragment>:2:11: unused typealias: 'DecimalPointScale'
```

<!-- test: error.an-unmarked-typealias-is-not-a-cast-target-outside-the-subtree -->
The refused cast credits nothing, so the alias nothing in `feature/` names is unused too.
```maxon
// --- file: feature/types.maxon
typealias Score = int(0 to 100)

// --- file: other/main.maxon
function main() returns ExitCode
	let s = 42 as Score
	return s
end 'main'
```
```maxoncstderr
error E3062: feature/<fragment>:3:11: unused typealias: 'Score'
error E3008: other/<fragment>:7:16: typealias 'Score' is not exported
```

<!-- test: error.an-unmarked-enum-binds-nothing-outside-the-subtree -->
A declared enum the reading file may not name is not an enum reference at all, so the base falls
through to the same diagnostic a misspelled name gets.
```maxon
// --- file: feature/color.maxon
enum Color
	red
	blue
end 'Color'

// --- file: other/main.maxon
function main() returns ExitCode
	let c = Color.blue
	return 0
end 'main'
```
```maxoncstderr
error E3008: other/<fragment>:10:10: type 'Color' is not exported
```

<!-- test: error.an-unmarked-types-method-is-hidden-outside-the-subtree -->
A value of an unmarked type that reached a file outside the subtree exposes none of the type's methods there.
```maxon
// --- file: feature/box.maxon
export typealias Integer = int(i64.min to i64.max)

type Box
	var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'

	export function get() returns Integer
		return self.v
	end 'get'
end 'Box'

export type Shelf
	export var box as Box

	export static function make() returns Self
		return Self{box: Box.create(42)}
	end 'make'
end 'Shelf'

// --- file: other/main.maxon
function main() returns ExitCode
	let s = Shelf.make()
	print("{s.box.get()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3167: feature/<fragment>:18:20: exported field 'feature.Shelf.box' names unexported type 'Box' in its type
error E3008: other/<fragment>:28:16: type 'Box' is not exported
```

<!-- test: error.an-unmarked-type-is-unreachable-through-a-field-outside-the-subtree -->
The TYPE's visibility outranks the FIELD's: `v` is exported, and the reader still cannot reach it,
because it may not name `Box`.
```maxon
// --- file: feature/box.maxon
export typealias Integer = int(i64.min to i64.max)

type Box
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

export type Shelf
	export var box as Box

	export static function make() returns Self
		return Self{box: Box.create(42)}
	end 'make'
end 'Shelf'

// --- file: other/main.maxon
function main() returns ExitCode
	let s = Shelf.make()
	return s.box.v
end 'main'
```
```maxoncstderr
error E3167: feature/<fragment>:14:20: exported field 'feature.Shelf.box' names unexported type 'Box' in its type
error E3008: other/<fragment>:24:15: type 'Box' is not exported
```

<!-- test: an-unmarked-type-is-reachable-from-a-subdirectory -->
```maxon
// --- file: feature/box.maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

// --- file: feature/sub/main.maxon
function main() returns ExitCode
	let b = Box.create(42)
	return b.v
end 'main'
```
```exitcode
42
```

<!-- test: a-file-reads-its-own-directorys-alias-not-an-unrelated-directorys -->
`lib/b.maxon` sees `lib/a.maxon`'s `Score`, so `150` fits; `z/`'s `Score`, which `lib/` cannot see, admits only
0 to 5.
```maxon
// --- file: lib/a.maxon
typealias Score = int(0 to 200)

// --- file: lib/b.maxon
export function pick() returns ExitCode
	return 150 as Score
end 'pick'

// --- file: z/z.maxon
typealias Score = int(0 to 5)

export function tiny() returns ExitCode
	let s = 1 as Score
	return s
end 'tiny'

// --- file: main.maxon
function main() returns ExitCode
	return pick() + tiny()
end 'main'
```
```exitcode
151
```

<!-- test: error.a-parent-alias-a-nested-directorys-same-named-alias-hides-is-unused -->
`lib/inner/`'s `Size` resolves to its own, nearer alias, so it credits that one and not `lib/`'s, which nothing
names.
```maxon
// --- file: lib/sizes.maxon
typealias Size = int(0 to 10)

// --- file: lib/inner/inner.maxon
typealias Size = int(0 to 99)

export function wide() returns ExitCode
	return 50 as Size
end 'wide'

// --- file: main.maxon
function main() returns ExitCode
	return wide()
end 'main'
```
```maxoncstderr
error E3062: lib/<fragment>:3:11: unused typealias: 'Size'
```

<!-- test: error.a-qualified-static-call-head-the-reader-cannot-see-is-refused-and-the-file-reads-on -->
A qualified static-call head the reader may not name is refused at the name, and the refusal does not stop the
file: a second hidden name later in it is reported too, exactly as two bare ones are.
```maxon
// --- file: feature/types.maxon
typealias Bag = Array with ExitCode

typealias Score = int(0 to 100)

// --- file: other/main.maxon
function main() returns ExitCode
	var b = feature.Bag.create(0)
	b.push(4)
	let s = 7 as feature.Score
	print("{s} {b.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3062: feature/<fragment>:3:11: unused typealias: 'Bag'
error E3062: feature/<fragment>:5:11: unused typealias: 'Score'
error E3008: other/<fragment>:9:10: typealias 'feature.Bag' is not exported
error E3008: other/<fragment>:11:15: typealias 'feature.Score' is not exported
```
