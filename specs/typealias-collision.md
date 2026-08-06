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
<!-- SelfhostedOnly: pins v1's E3063 ambiguous-typealias text; run here the program COMPILES CLEAN, so this compiler raises no ambiguity at all for the shape (measured 2026-08-06, BATCH29/A3a). -->
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
error E3063: specs/fragments/typealias-collision/error.exported-typealias-collision.test:10:11: Ambiguous typealias 'Score': multiple visible definitions found. Qualify with a directory name. Candidates: api.Score, legacy.Score
```


<!-- test: exported-typealias-collision-qualified -->
<!-- SelfhostedOnly: run here it does not compile: E2003 Unknown type 'api.Score' - this compiler has no directory-qualified typealias reference (measured 2026-08-06, BATCH29/A3a). -->
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
<!-- SelfhostedOnly: run here it does not compile: E2003 Unknown type 'lib.fmt' - this compiler does not walk a multi-segment directory qualifier (measured 2026-08-06, BATCH29/A3a). -->
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
A file-private `typealias` is invisible across files. When one file exports `Score` and another file declares a file-private `Score`, a third file using bare `Score` resolves to the exported one without ambiguity — the file-private alias isn't reachable from outside its declaring file.
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


<!-- test: file-private-alias-does-not-govern-another-file -->
⭐ A file-private `typealias` is scoped to its declaring file, so it may not change the meaning of a
name another file declared for itself — **including a file inside the stdlib.** `stdlib/Sha256.maxon`
declares its own file-private `Word32 = int(i64.min to i64.max)` and computes in 32-bit words; a
program that happens to declare a *different* `Word32` must not reach inside it. The range is what
makes this observable: the alias governs a WIDTH, so a narrower one silently truncates every word
rather than raising a diagnostic, and SHA-256("abc") begins `0xba` = 186 only if the stdlib kept its
own declaration. ⚠ The range here is deliberately `0 to 255` — a value a caller would plausibly
write, and wide enough that nothing in the user's own file is out of range. A wider alias such as
`int(0 to u32.max)` happens to hold the constants and answers correctly whatever the compiler does,
so it would pass while pinning nothing. Prints the first digest byte, then `7` as a liveness marker,
then `sizeof(Word32)` — which is the half that DISCRIMINATES: the user's `0 to 255` is one byte and
the `i64.min to i64.max` the stdlib declares for that same name is eight, so the SIZE says whose
declaration governs the user's own file while the digest byte says whose governs the stdlib's. ⚠ The
`7` alone pins nothing and is not that half — it is inside every range in this fixture, which is
exactly the weakness this file's older `42` cases have. ⚠⚠ `Word32` APPEARS INSIDE THE INTERPOLATED
STRING, AND THAT IS LOAD-BEARING A SECOND TIME: the runner's batch rewriter gives every top-level
declaration in a batched test a per-test prefix, which renames this `Word32` apart from the stdlib's
and dissolves the collision the case exists to catch. What keeps this test off the batched path is
`BatchRewriter.FindStringLiteralCollision` seeing a renamed name inside a `"…"` body. Move `Word32`
out of the string — spell the size as a literal, print it separately — and the case goes GREEN
against a compiler with the bug restored. Measured on a sibling case: the single-file form passed at
the parent commit while the same program compiled by hand was rejected.
```maxon
typealias Word32 = int(0 to 255)

function clampish(v Word32) returns Word32
	return v
end 'clampish'

function main() returns ExitCode
	var data = ByteArray.create()
	data.push(0x61)
	data.push(0x62)
	data.push(0x63)
	let hash = sha256(data)
	let b = try hash.get(0) otherwise 0
	print("{b} {clampish(7)} {sizeof(Word32)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
186 7 1
```


<!-- test: file-private-alias-still-governs-its-own-file -->
⭐ The MIRROR of the case above, and the reason that one may not be answered by handing the name to
the stdlib instead. A name-keyed type table holds ONE type per name, so scoping `DecimalDigit` by
letting `stdlib/Builtins.maxon`'s own file-private `int(0 to 9)` win would only move the wrong answer
— the program would then be reading a foreign 1-byte type for a name it declared, itself, four bytes
wide. Both files declare it and both must keep what they declared. The width is what makes it
observable in the program's OWN file: `70000` fits `int(0 to 100000)` and survives a round trip
through an `Array` of it only while that array's element is the program's declaration, and comes
back truncated the moment a by-name lookup swaps in the stdlib's. `sizeof` is printed beside it
precisely because the two can DISAGREE: the parser resolves a type name per file and answers 4,
while the generic instance's element type is re-resolved whole-program — a silent disagreement that
reaches the backend, which is why the value and the size are pinned together and not separately.
Prints `70000 3 70001 4`. ⚠⚠ `DecimalDigit` APPEARS INSIDE THE INTERPOLATED STRING, AND THAT IS
LOAD-BEARING A SECOND TIME — see the case above: it is what keeps this test off the batched path,
where the rewriter's per-test prefix would rename this `DecimalDigit` apart from the stdlib's and
leave nothing to collide.
```maxon
typealias DecimalDigit = int(0 to 100000)
typealias DigitArray = Array with DecimalDigit

function widen(v DecimalDigit) returns DecimalDigit
	return v
end 'widen'

function main() returns ExitCode
	var a = DigitArray.create()
	a.push(70000)
	a.push(3)
	let x = try a.get(0) otherwise 0
	let y = try a.get(1) otherwise 0
	print("{x} {y} {widen(70001)} {sizeof(DecimalDigit)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
70000 3 70001 4
```
