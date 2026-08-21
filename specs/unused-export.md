---
feature: unused-export
status: selfhosted
status-reason: "shv2 IMPLEMENTS THIS FAMILY as of 2026-08-21 and `specs-shv2/unused-export.md` is the LIVE copy - 22 cases, green. It stays `selfhosted` here because the C# bootstrap raises none of the three codes (`docs/error-codes.txt` claims E3092/E3093/E3094 for `selfhosted` and `shv2` only), so this runner would compile all seven error cases clean. TWO THINGS WERE CORRECTED HERE WITHOUT A RUNNER TO CATCH THEM: the positions were `0:0` for five of seven cases, which was v1s missing-data sentinel (`emitOneFunction`/`emitOneAlias`/`emitOneConst`/`emitOneVar` hardcoded it) and which v1s own runner never compared because `stripFilePathFromError` discards the whole `path:line:col:` prefix; they are now the real declaration positions, MEASURED off shv2 outside the harness. The path prefixes remain v1s spelling and are still validated by nothing here."
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

That distinction is why the family exists at all. Without it the lint reads
a VISIBILITY modifier as a claim about USE, which is a question the author
never answered — and the whole family was withdrawn once, on exactly that
objection, before `public` existed to answer it.

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
- Symbols reached only through indirect dispatch, where the implementation
  has no source-level edge to follow (v1 pins these by hand in
  `project.livenessRoots`; shv2 emits a `functionRef` and needs no list)
- Methods on a declared struct or enum — the declaring TYPE owns the
  diagnostic, so an internal helper method of a public type is not reported
  separately
- Every `public` declaration, which is the exemption above

⚠ **`stdlib/` IS NOT A SPECIAL CASE.** v1 exempts it twice — once by
skipping the pass for the stdlib's own compile and once by filtering every
stdlib declaration out of the seed by PATH — because otherwise it reports
E3092 against every stdlib symbol the user's program happens not to mention.
Under `public` there is nothing to exempt: the stdlib says `public` on its
API surface like any other library, and the compiler needs to know nothing
about where that library lives.

The audit runs only when the program has at least two files the author
wrote. A one-file program's exports ARE its public surface and nothing
outside can name them, so every one would be reported. Library files the
compiler loaded on the author's behalf are not counted for that test.

The pass is gated on project-wide structural errors: if any file has a
lexer (1xxx), parser (2xxx), or IR-stage (4xxx) error, the entire
unused-export check is skipped so a half-resolved reader doesn't cause
spurious diagnostics.

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
error E3092: specs/fragments/unused-export/error.unused-exported-function.test/api/lib.maxon:3:17: exported function 'api.unusedHelper' is never referenced outside its declaring file
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
error E3094: specs/fragments/unused-export/error.unused-module-function.test/api/lib.maxon:3:17: module function 'api.localHelper' is never referenced outside its declaring file
```

<!-- test: error.unused-exported-type -->
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
error E3092: specs/fragments/unused-export/error.unused-exported-type.test/api/shapes.maxon:3:13: exported type 'LocalPoint' is never referenced outside its declaring file
```

<!-- test: error.unused-exported-typealias -->
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
error E3092: specs/fragments/unused-export/error.unused-exported-typealias.test/api/lib.maxon:1:18: exported typealias 'UnusedAlias' is never referenced outside its declaring file
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
error E3092: specs/fragments/unused-export/error.unused-exported-var.test/api/counter.maxon:3:12: exported variable 'unusedCounter' is never referenced outside its declaring file
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
error E3092: specs/fragments/unused-export/error.unused-exported-const.test/api/limits.maxon:3:12: exported constant 'MAX_UNUSED' is never referenced outside its declaring file
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
error E3092: specs/fragments/unused-export/error.unused-exported-enum.test/api/status.maxon:3:13: exported type 'LocalStatus' is never referenced outside its declaring file
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
