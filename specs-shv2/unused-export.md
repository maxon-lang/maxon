---
feature: unused-export
status: stable
keywords: [export, module, public, visibility, semantic, unused]
category: diagnostics
---

# Unused Export Diagnostics

## Documentation

The compiler reports diagnostics when a declaration is more widely visible
than its uses require. Three diagnostics make up the family:

- **E3092 `semanticUnusedExportedSymbol`** — an `export` decl is never
  referenced from any file outside the one that declared it. Drop the
  modifier to make the symbol file-private, use `module` if you want to
  expose it inside the directory subtree, or say `public` if it is API
  surface.
- **E3093 `semanticExportableAsModule`** — an `export` decl is referenced,
  but only from files inside the declaring directory subtree. The decl
  could be downgraded to `module` visibility.
- **E3094 `semanticUnusedModuleSymbol`** — a `module` decl is never
  referenced from any file other than its declaring one. The `module`
  modifier could be dropped (the decl can be file-private).

### `public` is the exemption, and it is what makes the family answerable

`export` and `public` are the SAME visibility — both make a declaration
nameable from every file — and they differ in exactly one thing, which is
the claim they make about USE:

- `export` — other files may see this, **and I expect this program to use
  it**. Audited by the family above.
- `public` — this is **API surface**. Exempt from E3092 and E3093, because
  "no caller in this compilation" is not the same fact as "dead": a shared
  module may legitimately export a symbol this particular program never
  reaches.

There is deliberately no `module public`: E3094 keeps its full force, and a
`module` declaration that wants exemption is promoted to `public`, which
changes its visibility and says so.

The check covers functions (including methods), types (structs, unions,
enums), typealiases, top-level constants, and top-level variables. The
following declarations are skipped because they have no source-level caller
or are reached through indirect dispatch:

- `main` and module-init helpers
- Compiler-synthesized helpers (`__construct_*`, `__field_init_*`, lifted
  closures, etc.)
- Methods on a declared struct or enum — the declaring TYPE owns the
  diagnostic, so an internal helper method of a public type is not reported
  separately
- Every `public` declaration, which is the exemption above

The audit runs only when the program has at least two files the author
wrote: a one-file program's exports are its public surface and nothing
outside it can name them. `stdlib/` files are not counted for that test.

The pass is scheduled after `semanticCheck` and the pipeline stops at the
first pass that reports, so a program with any lexer, parser or semantic
error is never audited — a half-resolved reader cannot produce spurious
findings.

## Tests

Each test below places the declaring file and the calling `main.maxon` in
different subdirectories (`api/` vs `app/`) so the call is genuinely
"outside the declaring module subtree". The fixture's interior symbol
(`unusedHelper`, `LocalPoint`, etc.) is the one the diagnostic targets.

<!-- test: error.unused-exported-function -->
```maxon
// --- file: api/lib.maxon
typealias Integer = int(i64.min to i64.max)

export function unusedHelper() returns Integer
	return 42
end 'unusedHelper'

export function publicEntry() returns Integer
	return unusedHelper()
end 'publicEntry'

// --- file: app/main.maxon
function main() returns ExitCode
	return publicEntry()
end 'main'
```
```maxoncstderr
error E3092: api/<fragment>:5:17: exported function 'api.unusedHelper' is never referenced outside its declaring file
```

<!-- test: error.unused-module-function -->
```maxon
// --- file: api/lib.maxon
typealias Integer = int(i64.min to i64.max)

module function localHelper() returns Integer
	return 7
end 'localHelper'

export function publicEntry() returns Integer
	return localHelper()
end 'publicEntry'

// --- file: app/main.maxon
function main() returns ExitCode
	return publicEntry()
end 'main'
```
```maxoncstderr
error E3094: api/<fragment>:5:17: module function 'api.localHelper' is never referenced outside its declaring file
```

<!-- test: error.unused-exported-type -->
⭐ **ONE diagnostic, about the TYPE, though `LocalPoint` declares two exported METHODS.** A method takes
its visibility from its declaring type by convention, so auditing methods separately would report an
internal helper of a public type as dead — and an un-`export`ed method of a `module` type would report as
E3093, advising a narrowing that cannot be written. The type owns the finding.
```maxon
// --- file: api/shapes.maxon
typealias Integer = int(i64.min to i64.max)

export type LocalPoint
	export var x as Integer
	export var y as Integer

	export static function origin() returns LocalPoint
		return LocalPoint{x: 0, y: 0}
	end 'origin'

	export function sum() returns Integer
		return x + y
	end 'sum'
end 'LocalPoint'

export function entry() returns Integer
	let p = LocalPoint.origin()
	return p.sum()
end 'entry'

// --- file: app/main.maxon
function main() returns ExitCode
	return entry()
end 'main'
```
```maxoncstderr
error E3092: api/<fragment>:5:13: exported type 'LocalPoint' is never referenced outside its declaring file
```

<!-- test: error.unused-exported-typealias -->
The alias IS named in its declaring file — `consumeAlias`'s parameter is typed by it — and is still
E3092, because the question is whether anything OUTSIDE that file names it.
```maxon
// --- file: api/lib.maxon
export typealias UnusedAlias = int(0 to 100)

typealias Integer = int(i64.min to i64.max)

function consumeAlias(value UnusedAlias) returns Integer
	return value
end 'consumeAlias'

export function entry() returns Integer
	return consumeAlias(42)
end 'entry'

// --- file: app/main.maxon
function main() returns ExitCode
	return entry()
end 'main'
```
```maxoncstderr
error E3092: api/<fragment>:3:18: exported typealias 'UnusedAlias' is never referenced outside its declaring file
```

<!-- test: error.unused-exported-var -->
```maxon
// --- file: api/counter.maxon
typealias Integer = int(i64.min to i64.max)

export var unusedCounter = 99

export function readCounter() returns Integer
	return unusedCounter
end 'readCounter'

// --- file: app/main.maxon
function main() returns ExitCode
	return readCounter()
end 'main'
```
```maxoncstderr
error E3092: api/<fragment>:5:12: exported variable 'unusedCounter' is never referenced outside its declaring file
```

<!-- test: error.unused-exported-const -->
```maxon
// --- file: api/limits.maxon
typealias Integer = int(i64.min to i64.max)

export let MAX_UNUSED = 100

export function readMax() returns Integer
	return MAX_UNUSED
end 'readMax'

// --- file: app/main.maxon
function main() returns ExitCode
	return readMax()
end 'main'
```
```maxoncstderr
error E3092: api/<fragment>:5:12: exported constant 'MAX_UNUSED' is never referenced outside its declaring file
```

<!-- test: error.unused-exported-enum -->
```maxon
// --- file: api/status.maxon
typealias Integer = int(i64.min to i64.max)

export enum LocalStatus
	idle
	running
	done
end 'LocalStatus'

function statusOrdinal(s LocalStatus) returns Integer
	return s.ordinal
end 'statusOrdinal'

export function entry() returns Integer
	return statusOrdinal(LocalStatus.idle)
end 'entry'

// --- file: app/main.maxon
function main() returns ExitCode
	return entry()
end 'main'
```
```maxoncstderr
error E3092: api/<fragment>:5:13: exported type 'LocalStatus' is never referenced outside its declaring file
```

<!-- test: exported-main-not-flagged -->

```maxon

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

## `public` exempts every declaration kind

⭐⭐ **THESE ARE THE DISCRIMINATING PAIR FOR THE WHOLE FEATURE.** Each program below is the matching
`error.unused-exported-*` case above with ONE word changed, and each compiles clean. Without them a
`public` that did nothing at all — an alias for `export` — would pass every other test in this file.

<!-- test: public-function-not-flagged -->
```maxon
// --- file: api/lib.maxon
typealias Integer = int(i64.min to i64.max)

public function unusedHelper() returns Integer
	return 42
end 'unusedHelper'

export function publicEntry() returns Integer
	return unusedHelper()
end 'publicEntry'

// --- file: app/main.maxon
function main() returns ExitCode
	return publicEntry()
end 'main'
```
```exitcode
42
```

<!-- test: public-type-not-flagged -->
```maxon
// --- file: api/shapes.maxon
typealias Integer = int(i64.min to i64.max)

public type LocalPoint
	export var x as Integer
	export var y as Integer

	export static function origin() returns LocalPoint
		return LocalPoint{x: 3, y: 4}
	end 'origin'

	export function sum() returns Integer
		return x + y
	end 'sum'
end 'LocalPoint'

export function entry() returns Integer
	let p = LocalPoint.origin()
	return p.sum()
end 'entry'

// --- file: app/main.maxon
function main() returns ExitCode
	return entry()
end 'main'
```
```exitcode
7
```

<!-- test: public-typealias-not-flagged -->
```maxon
// --- file: api/lib.maxon
public typealias UnusedAlias = int(0 to 100)

typealias Integer = int(i64.min to i64.max)

function consumeAlias(value UnusedAlias) returns Integer
	return value
end 'consumeAlias'

export function entry() returns Integer
	return consumeAlias(42)
end 'entry'

// --- file: app/main.maxon
function main() returns ExitCode
	return entry()
end 'main'
```
```exitcode
42
```

<!-- test: public-constant-not-flagged -->
```maxon
// --- file: api/limits.maxon
typealias Integer = int(i64.min to i64.max)

public let MAX_UNUSED = 100

export function readMax() returns Integer
	return MAX_UNUSED
end 'readMax'

// --- file: app/main.maxon
function main() returns ExitCode
	return readMax()
end 'main'
```
```exitcode
100
```

<!-- test: public-var-not-flagged -->
```maxon
// --- file: api/counter.maxon
typealias Integer = int(i64.min to i64.max)

public var unusedCounter = 99

export function readCounter() returns Integer
	return unusedCounter
end 'readCounter'

// --- file: app/main.maxon
function main() returns ExitCode
	return readCounter()
end 'main'
```
```exitcode
99
```

<!-- test: public-module-function-not-flagged -->
A `module` declaration nothing outside its file reaches is E3094; promoted to `public` it is exempt,
which is the only way to silence that one — `module public` does not exist.
```maxon
// --- file: api/lib.maxon
typealias Integer = int(i64.min to i64.max)

public function localHelper() returns Integer
	return 7
end 'localHelper'

export function publicEntry() returns Integer
	return localHelper()
end 'publicEntry'

// --- file: app/main.maxon
function main() returns ExitCode
	return publicEntry()
end 'main'
```
```exitcode
7
```

## A genuine cross-file use is never flagged

These are the false-positive guards. Each names the declaration from the OTHER file through a different
route, and each route is a separate thing the check has to be able to see.

<!-- test: cross-file-constant-read-is-a-reference -->
⚠ **AN INLINED CONSTANT LEAVES NO OP.** A `let` with no storage is materialized at its use site, so this
read is invisible in the IR — the check is told about it by `ProgramSignatures.constantValueOf`, the door
the use site resolves through.
```maxon
// --- file: api/limits.maxon
export let MAX_USED = 7

// --- file: app/main.maxon
function main() returns ExitCode
	return MAX_USED
end 'main'
```
```exitcode
7
```

<!-- test: cross-file-constant-chain-is-a-reference -->
⚠ **AND AN INITIALIZER IS A SECOND DOOR.** `FINAL` reads `MIDDLE` from inside a constant initializer,
which resolves through the const EVALUATOR rather than through a use site. Hooking only the use site
reported every link of a cross-file chain as unreferenced.
```maxon
// --- file: app/main.maxon
let FINAL = MIDDLE + 2

function main() returns ExitCode
	return FINAL
end 'main'

// --- file: api/middle.maxon
export let MIDDLE = ROOT * 4

// --- file: api/root.maxon
export let ROOT = 10
```
```exitcode
42
```

<!-- test: cross-file-var-read-is-a-reference -->
```maxon
// --- file: api/counter.maxon
export var usedCounter = 9

// --- file: app/main.maxon
function main() returns ExitCode
	return usedCounter
end 'main'
```
```exitcode
9
```

<!-- test: cross-file-type-use-is-a-reference -->
`app/main.maxon` never writes `UsedPoint` in a TYPE position — it calls a static on it. The owner half of
a qualified callee is what makes that a reference.
```maxon
// --- file: api/shapes.maxon
typealias Integer = int(i64.min to i64.max)

export type UsedPoint
	export var x as Integer

	export static function origin() returns UsedPoint
		return UsedPoint{x: 4}
	end 'origin'
end 'UsedPoint'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = UsedPoint.origin()
	return p.x
end 'main'
```
```exitcode
4
```

<!-- test: cross-file-alias-in-a-signature-is-a-reference -->
⚠ **A RANGED ALIAS IS ERASED TO `integer` BEFORE ANY PASS SEES IT**, so a parameter typed by one leaves
no trace in the declared signature. The reference is recovered from the set of type names each file wrote
in a type position — the same set E3062 is answered from.
```maxon
// --- file: api/lib.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function scoreOf(s Score) returns Score
	return s
end 'scoreOf'

function main() returns ExitCode
	return scoreOf(5)
end 'main'
```
```exitcode
5
```

<!-- test: cross-file-enum-case-is-a-reference -->
`Color.blue` is an EXPRESSION, not a type position and not a call, and it is how an enum is used across a
file boundary almost every time.
```maxon
// --- file: api/color.maxon
export enum Color
	red
	green
	blue
end 'Color'

// --- file: app/main.maxon
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

## E3093 — referenced, but only from inside the declaring subtree

<!-- test: error.exportable-as-module -->
`pkg/sub/user.maxon` is STRICTLY NESTED under `pkg/`, so `subtreeOnly` is reachable at `module`
visibility and never needed `export`.

⚠ **A SIBLING IN THE SAME DIRECTORY DOES NOT COUNT AS INSIDE THE SUBTREE**, deliberately: without that
rule every flat two-file program would be advised to downgrade its exports, which is advice about
directory layout rather than about visibility.
```maxon
// --- file: pkg/lib.maxon
typealias Integer = int(i64.min to i64.max)

export function subtreeOnly() returns Integer
	return 3
end 'subtreeOnly'

// --- file: pkg/sub/user.maxon
typealias Num = int(i64.min to i64.max)

export function entry() returns Num
	return subtreeOnly()
end 'entry'

// --- file: main.maxon
function main() returns ExitCode
	return entry()
end 'main'
```
```maxoncstderr
error E3093: pkg/<fragment>:5:17: exported function 'pkg.subtreeOnly' is only referenced inside its declaring module subtree; consider 'module' visibility
```

## The audit needs two files the author wrote

<!-- test: single-file-exports-are-not-flagged -->
A one-file program's exports ARE its public surface, and nothing outside can name them — so every one
would be E3092 and the diagnostic would be noise. The stdlib's own files are not counted, or the gate
would never fire.
```maxon
typealias Integer = int(i64.min to i64.max)

export function nobodyOutsideCanCallThis() returns Integer
	return 5
end 'nobodyOutsideCanCallThis'

function main() returns ExitCode
	return nobodyOutsideCanCallThis()
end 'main'
```
```exitcode
5
```
