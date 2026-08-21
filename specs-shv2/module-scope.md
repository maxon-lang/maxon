---
feature: module-scope
status: stable
keywords: [module, visibility, directory, namespace, contextual-keyword]
category: diagnostics
---

# `module` visibility — the edges `module-keyword.md` does not stand on

## Documentation

`specs/module-keyword.md` states the three-tier model and pins the happy path of every declaration
kind. These are the ADVERSARIAL cases for the same mechanism — the ones an implementation can get
wrong while every case in that file stays green.

### `module` is a CONTEXTUAL modifier, not a keyword

The word `module` is an ordinary identifier everywhere except immediately before a declaration
keyword. It has to be: the compiler that reads this file uses `module` as a variable, a parameter and
a field thousands of times over, so reserving the word would make the language unable to describe its
own implementation. Recognition is exact rather than heuristic — a `module` at the end of a line is
separated from the next line's `let` by a newline token, so a trailing identifier can never be read
as the next declaration's modifier.

### The subtree test is by SEGMENT, never by character prefix

`feature2/` is not inside `feature/`. A prefix test that forgot the separator would say it was, and
that is a wrong ANSWER about what another directory may name — not a missing diagnostic.

### A DOT IS AN ORDINARY CHARACTER IN A DIRECTORY NAME

`feature.extra/` and `my.dir/` are directories like any other, and the language's `.`-joined
NAMESPACE spelling is a rendering of a directory, never a stand-in for one. An implementation that
decides the subtree question on the rendered string reintroduces the prefix bug through the joiner
itself: `feature.extra` starts with `feature.`, and `my.dir/` renders exactly like `my/dir/`. Both
are wrong ANSWERS — one admits a sibling into the module, the other merges two unrelated
directories — and neither is visible in any test whose directory names happen to have no dots.

### The root directory is every file's module

A `module` declaration in a root-level file is visible program-wide, because "the declaring
directory and every subdirectory of it" is the whole program when the directory is the root.

### `export` and `module` are mutually exclusive on EVERY declaration kind

`specs/module-keyword.md` pins the conflict for a `function`. The rule is a property of the two
modifiers, not of what they modify.

## Tests

<!-- test: module-is-an-ordinary-identifier -->
`module` names a top-level constant, a field, a static factory's parameter, a free function's
parameter and a local `var`, in one program that never uses the modifier at all.
```maxon
typealias Integer = int(i64.min to i64.max)

let module = 5

type Holder
	export var module as Integer

	export static function create(module Integer) returns Self
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
	var module = 5
	module = module + 1
	return (twice(module) + h.module + module) as ExitCode
end 'main'
```
```exitcode
34
```

<!-- test: error.a-trailing-module-identifier-does-not-modify-the-next-declaration -->
`let alsoModule = module` is followed immediately by `let derived = 37`, so the token before that
second `let` is the identifier `module`. It is NOT the modifier — a newline separates them — and
`derived` therefore stays file-private, which the second file proves by failing to see it.
```maxon
// --- file: one.maxon
typealias Integer = int(i64.min to i64.max)

let module = 5
let alsoModule = module
let derived = 37

export function reads() returns Integer
	return derived + alsoModule
end 'reads'

// --- file: two.maxon
function main() returns ExitCode
	return derived
end 'main'
```
```maxoncstderr
error E2004: <fragment>:15:9: Undefined variable 'derived'
```

<!-- test: error.a-sibling-directory-sharing-a-name-prefix-is-outside-the-module -->
`feature2/` is not a subdirectory of `feature/`. A character-prefix subtree test would admit it.
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

module function helper() returns Integer
	return 42
end 'helper'

// --- file: feature2/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3088: feature2/<fragment>:11:9: function 'helper' is module-scoped and not visible from this directory
```

<!-- test: error.a-sibling-directory-whose-name-contains-a-dot-is-outside-the-module -->
`feature.extra/` is a SIBLING of `feature/`, not a subdirectory of it. Its `.`-joined namespace is
`feature.extra`, which begins with `feature.` — so a subtree test asked on namespaces rather than on
directories admits it, and the separator guard that catches `feature2/` walks straight past.
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

module function helper() returns Integer
	return 42
end 'helper'

// --- file: feature.extra/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3088: feature.extra/<fragment>:11:9: function 'helper' is module-scoped and not visible from this directory
```

<!-- test: error.a-dotted-directory-is-not-the-nested-directory-it-renders-like -->
`my.dir/` and `my/dir/` are different directories that render to the same `.`-joined namespace. The
reader is in neither the declaring directory nor beneath it.
```maxon
// --- file: my.dir/helper.maxon
typealias Integer = int(i64.min to i64.max)

module function helper() returns Integer
	return 42
end 'helper'

// --- file: my/dir/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3088: my/dir/<fragment>:11:9: function 'helper' is module-scoped and not visible from this directory
```

<!-- test: a-directory-name-containing-a-dot-is-its-own-module -->
The converse of the two refusals above: a dot in a directory name costs the directory nothing. Its
own subdirectory is inside its module exactly as any other subdirectory is.
```maxon
// --- file: feature.extra/helper.maxon
typealias Integer = int(i64.min to i64.max)

module function helper() returns Integer
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

<!-- test: a-root-level-module-declaration-is-visible-from-a-subdirectory -->
The root directory contains every file, so a `module` declaration written there is visible
program-wide.
```maxon
// --- file: root.maxon
typealias Integer = int(i64.min to i64.max)

module function helper() returns Integer
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

<!-- test: error.export-and-module-on-a-let -->
```maxon
export module let LIMIT = 21

function main() returns ExitCode
	return LIMIT
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: 'export' and 'module' cannot be combined
```

<!-- test: error.export-and-module-on-a-typealias -->
```maxon
export module typealias Score = int(0 to 100)

function main() returns ExitCode
	let s = 42 as Score
	return s
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: 'export' and 'module' cannot be combined
```

<!-- test: error.export-and-module-on-a-type -->
```maxon
typealias Integer = int(i64.min to i64.max)

export module type Box
	export var v as Integer
end 'Box'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: <fragment>:4:8: 'export' and 'module' cannot be combined
```

<!-- test: error.export-and-module-on-an-enum -->
```maxon
export module enum Color
	red
	blue
end 'Color'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: 'export' and 'module' cannot be combined
```

<!-- test: error.export-and-module-on-a-field -->
The conflict is a property of the two modifiers, so a type MEMBER is refused exactly as a top-level
declaration is — by the one reader both walks share.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export module var v as Integer
end 'Box'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: <fragment>:5:9: 'export' and 'module' cannot be combined
```

<!-- test: error.a-module-field-is-not-readable-outside-the-subtree -->
The TYPE is exported and the FIELD is `module`, so the reader may name `Box` and may not read `v`.
```maxon
// --- file: feature/box.maxon
typealias Integer = int(i64.min to i64.max)

export type Box
	module var v as Integer

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
error E3014: other/<fragment>:16:11: cannot access unexported field: 'v' outside of type 'Box'
```

<!-- test: a-module-field-is-readable-from-a-subdirectory -->
```maxon
// --- file: feature/box.maxon
typealias Integer = int(i64.min to i64.max)

public type Box
	module var v as Integer

	export static function create(v Integer) returns Self
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

<!-- test: error.a-module-typealias-is-not-a-cast-target-outside-the-subtree -->
```maxon
// --- file: feature/types.maxon
module typealias Score = int(0 to 100)

// --- file: other/main.maxon
function main() returns ExitCode
	let s = 42 as Score
	return s
end 'main'
```
```maxoncstderr
error E2003: other/<fragment>:7:16: Expected type name after 'as'
```

<!-- test: error.a-module-enum-binds-nothing-outside-the-subtree -->
A declared enum the reading file may not name is not an enum reference at all, so the base falls
through to the same diagnostic a misspelled name gets.
```maxon
// --- file: feature/color.maxon
module enum Color
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
error E2004: other/<fragment>:10:10: Undefined variable 'Color'
```

<!-- test: error.a-module-type-is-unreachable-through-a-field-outside-the-subtree -->
The TYPE's visibility outranks the FIELD's: `v` is exported, and the reader still cannot reach it,
because it may not name `Box`.
```maxon
// --- file: feature/box.maxon
typealias Integer = int(i64.min to i64.max)

module type Box
	export var v as Integer

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
error E4006: other/<fragment>:16:11: Unknown type 'Box' in field access chain
```

<!-- test: a-module-type-is-reachable-from-a-subdirectory -->
```maxon
// --- file: feature/box.maxon
typealias Integer = int(i64.min to i64.max)

module type Box
	export var v as Integer

	export static function create(v Integer) returns Self
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
