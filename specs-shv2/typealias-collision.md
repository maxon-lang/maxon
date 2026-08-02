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

A bare `Score` reference from `app/main.maxon` triggers **E3063** asking the user to qualify with a directory namespace:

```text
error E3063: Ambiguous typealias 'Score': multiple visible definitions found.
  Qualify with a directory name. Candidates: api.Score, legacy.Score
```

The qualifying namespace is the declaring file's directory (joined with `.` for nested directories — e.g. `lib.fmt.Score` for a file at `lib/fmt/types.maxon`). Same-file duplicates remain a hard E3061 error (no qualification can disambiguate two declarations in the same file). File-private aliases (`typealias` with no modifier) are scoped to their declaring file and never participate in cross-file ambiguity.

This mirrors **E3095** for function-name ambiguity — same model, different registry.

## Tests

<!-- test: error.exported-typealias-collision -->
<!-- SelfhostedOnly -->
Two files in different directories both export `Score`. A bare reference from a third file is rejected with E3063. The self-hosted compiler emits the diagnostic at the parse site; the C# bootstrap reports an equivalent E3063 at the same point in the pipeline but with a slightly different candidate-ordering guarantee, so this test pins the self-hosted message.
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
error E3063: app/specs/fragments/typealias-collision/error.exported-typealias-collision.test:10:16: Ambiguous typealias 'Score': multiple visible definitions found. Qualify with a directory name. Candidates: api.Score, legacy.Score
```


<!-- test: exported-typealias-collision-qualified -->
<!-- SelfhostedOnly -->
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
	return a + b
end 'main'
```
```exitcode
110
```


<!-- test: exported-typealias-collision-multi-segment-namespace -->
<!-- SelfhostedOnly -->
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
	return a + b
end 'main'
```
```exitcode
75
```


<!-- test: exported-typealias-no-collision-bare-works -->
Regression guard: when only ONE definition of a name is reachable, the bare name still resolves. Covers the stdlib aliases (`Integer`, `Count`, `ExitCode`, ...) that every Maxon program uses and that must continue to work without qualification.
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
A project file exports a typealias whose bare name is *also* exported by the
stdlib (here `StringArray`, exported from `stdlib/Json.maxon`). A bare reference
resolves to the project definition without E3063 — a project export shadows a
stdlib export of the same name rather than colliding with it. Stdlib aliases are
seeded as a lower-precedence library layer, so they never participate in
cross-file ambiguity. Regression guard for self-hosting: the compiler's own
source re-exports `StringArray`, `FilePathArray`, and `ByteCount`, all of which
the stdlib also exports.
```maxon
export typealias StringArray = Array with String

function main() returns ExitCode
	var xs = StringArray.create()
	xs.push("a")
	xs.push("b")
	return xs.count() as ExitCode
end 'main'
```
```exitcode
2
```


<!-- test: nested-export-shadowed-by-enclosing-dir -->
<!-- SelfhostedOnly -->
Directory-as-module precedence: a file in `Compiler/` exports `Tally`, and a
file in the nested `Compiler/Coverage/` subdirectory also exports `Tally`. A
bare reference from a `Compiler/` file resolves to the enclosing-directory
definition without E3063 — the deeper, more-local nested export is not a
competitor from the parent scope's point of view. This mirrors the compiler's
own source, where `Compiler/` and `Compiler/Coverage/` both export
`FilePathArray`.
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
```exitcode
42
```


<!-- test: exported-typealias-file-private-doesnt-collide -->
<!-- SelfhostedOnly -->
A file-private `typealias` is invisible across files. When one file exports `Score` and another file declares a file-private `Score`, a third file using bare `Score` resolves to the exported one without ambiguity — the file-private alias isn't reachable from outside its declaring file. The C# bootstrap currently conflates file-private types across files (a single `NonExportedTypeNames` set), so this scenario only fully resolves in the self-hosted compiler.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

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
