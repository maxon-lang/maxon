---
feature: unused-export
status: selfhosted
keywords: [export, module, visibility, semantic, unused]
category: diagnostics
---

# Unused Export Diagnostics

## Documentation

The compiler reports diagnostics when a declaration is more widely visible
than its uses require. Three diagnostics make up the family:

- **E3092 `semanticUnusedExportedSymbol`** — an `export` decl is never
  referenced from any file outside the one that declared it. Drop the
  modifier to make the symbol file-private (or use `module` if you want to
  expose it inside the directory subtree).
- **E3093 `semanticExportableAsModule`** — an `export` decl is referenced,
  but only from files inside the declaring directory subtree. The decl
  could be downgraded to `module` visibility.
- **E3094 `semanticUnusedModuleSymbol`** — a `module` decl is never
  referenced from any file other than its declaring one. The `module`
  modifier could be dropped (the decl can be file-private).

The check covers functions (including methods), types (structs, unions,
enums), typealiases, top-level constants, and top-level variables. The
following declarations are skipped because they have no source-level caller
or are reached through indirect dispatch:

- `main` and module-init helpers
- Compiler-synthesized helpers (`__construct_*`, `__field_init_*`, lifted
  closures, etc.)
- Symbols in `project.livenessRoots` (witness-table entries,
  function-backed enum case targets, etc.)
- Methods that satisfy an `expectedMethods` interface contract on the
  declaring struct or enum
- Stdlib bootstrap and stdlib source-file declarations

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
error E3092: specs/fragments/unused-export/error.unused-exported-function.test/api/lib.maxon:0:0: exported function 'api.unusedHelper' is never referenced outside its declaring file
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
error E3094: specs/fragments/unused-export/error.unused-module-function.test/api/lib.maxon:0:0: module function 'api.localHelper' is never referenced outside its declaring file
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
error E3092: specs/fragments/unused-export/error.unused-exported-type.test/api/shapes.maxon:5:13: exported type 'LocalPoint' is never referenced outside its declaring file
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
error E3092: specs/fragments/unused-export/error.unused-exported-typealias.test/api/lib.maxon:0:0: exported typealias 'UnusedAlias' is never referenced outside its declaring file
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
error E3092: specs/fragments/unused-export/error.unused-exported-var.test/api/counter.maxon:0:0: exported variable 'unusedCounter' is never referenced outside its declaring file
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
error E3092: specs/fragments/unused-export/error.unused-exported-const.test/api/limits.maxon:0:0: exported constant 'MAX_UNUSED' is never referenced outside its declaring file
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
error E3092: specs/fragments/unused-export/error.unused-exported-enum.test/api/status.maxon:1:13: exported type 'LocalStatus' is never referenced outside its declaring file
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
