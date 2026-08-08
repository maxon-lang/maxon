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
⚠⚠ **THIS CASE CAST `42` FOR MONTHS, AND `42` IS INSIDE BOTH RANGES.** It passed whichever
declaration governed the reader, so it pinned the absence of E3063 and nothing at all about
precedence. The two ranges now DISAGREE ON SIGNEDNESS and are both one byte wide, so neither value
survives the other's storage: 200 read through `int(-100 to 100)` is −56, −50 read through
`int(0 to 255)` is 206. MEASURED at this rung's merge base with exactly the program below, the
enclosing file's own bare `Tally` resolved to the NESTED declaration and the build was refused
`E3005: Value 200 is outside the range of 'Tally' (int(-100 to 100))` — which is what the `42` version
had been passing over. ⚠ Three readers on purpose, because precedence is a claim about each of them
separately: `bare` is the enclosing directory's own file resolving the name with no declaration of
its own in sight, and `enclosing`/`nested` are the two DECLARERS reading back what they stored, which
is the half a single reader cannot state. Prints `bare=200 enclosing=200 nested=-50`.
```maxon
// --- file: Compiler/types.maxon
export typealias Tally = int(0 to 255)

export function enclosingTally() returns Tally
	let v = 200 as Tally
	return v
end 'enclosingTally'

// --- file: Compiler/Coverage/types.maxon
export typealias Tally = int(-100 to 100)

export function nestedTally() returns Tally
	let v = -50 as Tally
	return v
end 'nestedTally'

// --- file: Compiler/main.maxon
function main() returns ExitCode
	let x = 200 as Tally
	print("bare={x} enclosing={enclosingTally()} nested={nestedTally()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bare=200 enclosing=200 nested=-50
```


<!-- test: enclosing-and-nested-export-keep-their-own-element -->
⭐ The generic twin of the case above, and the shape canonical's strict-enclosure rule BLESSES so
refusing it is not available: an `export typealias Cells = Array with Cell` in `outer/` and another
in `outer/inner/`, each over its own `Cell`. Neither declaration may be renamed by the rule that
settles the file-private and `module` contests — an `export` name is one any file may write — so
before this rung the two shared one family of emitted methods and the flat, name-keyed type table
handed both files whichever declaration merged last. ⚠⚠ It is ONE defect with TWO faces, and which
one a program sees is decided only by whether a range check catches the truncation: over
`int(0 to 100000)` against `int(0 to 255)` the enclosing file read its own 70000 back as 112,
silently, exit 0; over the two one-byte ranges used here — which disagree on SIGNEDNESS, so neither
value survives the other's storage — the same shape died as `Range check failed: value outside
typealias 'Cell'` in the enclosing file's own function. Prints `outer=200 inner=-50`.
```maxon
// --- file: outer/types.maxon
export typealias Cell = int(0 to 255)
export typealias Cells = Array with Cell

export type Outer
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(200)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Outer'

// --- file: outer/inner/types.maxon
export typealias Cell = int(-100 to 100)
export typealias Cells = Array with Cell

export type Inner
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(-50)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Inner'

// --- file: main.maxon
function main() returns ExitCode
	print("outer={Outer.stash()} inner={Inner.stash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
outer=200 inner=-50
```


<!-- test: module-alias-governs-a-sibling-in-its-own-subtree -->
⭐ The half `module-alias-does-not-govern-another-directory` leaves out: a file that merely READS a
`module` alias two DISJOINT subtrees declare. The pair is legal precisely because no file can see
both, and the reader here is in `scopeA/`, so `scopeA/`'s declaration is the only one it may name at
all. ⚠⚠ It was served `scopeB/`'s: the DECLARERS are each cured by the contest rename, and the reader
is not — it resolves the bare name through the module's alias tables, which hold ONE declaration per
name, so it got whichever merged last and read 65000 back as 232 (= 65000 mod 256). ⚠ The reader is
deliberately a SEPARATE FILE from the declarer: written into the declaring file itself it is served by
that parser's own registry and answers correctly whatever the module tables hold, which is the
weakness that left this half untested. `narrow=200` is a liveness marker only — it sits inside both
ranges and can never tell them apart.
⚠⚠ **THE READER SORTS BEFORE ITS OWN DECLARER ON PURPOSE, AND THAT NAMING IS LOAD-BEARING.** It
catches a SECOND defect the other order hides: `PreScanTypeAliasesOnly` had the `module` modifier in
hand and passed only `export` on to `PreScanTypeAlias`, so for the whole pre-scan window a `module`
declaration's recorded reach was FILE — invisible to every file but its own. A reader pre-scanned
after its declarer never saw that window, because `PreScan` re-recorded the right reach first; a
reader pre-scanned BEFORE it was refused `E2003: Unknown type: Cell` for a name declared beside it.
Written `aa.maxon`/`reader.maxon` this case answers correctly and pins only half of what it is for.
```maxon
// --- file: scopeA/aa-reader.maxon
export type Sibling
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(65000)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Sibling'

// --- file: scopeA/zz-decl.maxon
module typealias Cell = int(0 to 100000)
module typealias Cells = Array with Cell

// --- file: scopeB/zz.maxon
module typealias Cell = int(0 to 255)
module typealias Cells = Array with Cell

export type Narrow
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(200)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Narrow'

// --- file: main.maxon
function main() returns ExitCode
	print("sib={Sibling.stash()} narrow={Narrow.stash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sib=65000 narrow=200
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
exactly the weakness this file's older `42` cases have. ⚠⚠ **TWO FILES ON PURPOSE, AND THE SPLIT IS
WHAT KEEPS THIS CASE ABLE TO FAIL AT ALL.** The runner's batch rewriter gives every top-level
declaration in a batched test a per-test prefix, which renames this `Word32` apart from the stdlib's
and dissolves the very collision the case exists to catch — leaving a green run that tests nothing.
`FragmentGenerator.IsBatchable` refuses a multi-file test outright, so the split puts this case
STRUCTURALLY beyond the rewriter rather than incidentally beyond it. *(Incidentally is not
hypothetical: the single-file form was kept off the batched path only by `Word32` appearing inside an
interpolated string, and a sibling case in this rung PASSED at the parent commit in that form while
the identical program compiled by hand was rejected there.)* The split also states the defect more
honestly — the colliding declaration belongs to a LIBRARY file, and `main` never names it.
```maxon
// --- file: digits.maxon
typealias Word32 = int(0 to 255)
export typealias Reading = int(0 to 1000)

export function clampish(v Word32) returns Reading
	return v
end 'clampish'

export function ownWidth() returns Reading
	return sizeof(Word32)
end 'ownWidth'

// --- file: main.maxon
function main() returns ExitCode
	var data = ByteArray.create()
	data.push(0x61)
	data.push(0x62)
	data.push(0x63)
	let hash = sha256(data)
	let b = try hash.get(0) otherwise 0
	print("{b} {clampish(7)} {ownWidth()}\n")
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
Prints `70000 70001 4`. ⚠⚠ TWO FILES ON PURPOSE, for the reason the case above states: the batch
rewriter's per-test prefix would rename this `DecimalDigit` apart from the stdlib's and leave nothing
to collide, and `FragmentGenerator.IsBatchable` refuses a multi-file test outright. ⚠ This case is a
GUARD-RAIL rather than a regression pin, so it is green both before and after this rung — what it
exists to fail is the *other* cure for its sibling, a visibility-rank guard on the `TypeDefs` write,
which was built during this rung and measured to make SHA-256 correct while turning this program's
`70000` into `880`. Without this case that road passes the suite.
```maxon
// --- file: digits.maxon
typealias DecimalDigit = int(0 to 100000)
typealias DigitArray = Array with DecimalDigit
export typealias Reading = int(0 to 1000000)

export function roundTrip() returns Reading
	var a = DigitArray.create()
	a.push(70000)
	let x = try a.get(0) otherwise 0
	return x
end 'roundTrip'

export function widen() returns Reading
	return 70001
end 'widen'

export function ownWidth() returns Reading
	return sizeof(DecimalDigit)
end 'ownWidth'

// --- file: main.maxon
function main() returns ExitCode
	print("{roundTrip()} {widen()} {ownWidth()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
70000 70001 4
```


<!-- test: file-private-alias-does-not-govern-another-project-file -->
⭐ The project-to-project twin of `file-private-alias-does-not-govern-another-file`, which pins the
same rule against the *stdlib*. A file-private `typealias` is scoped to its declaring file, so two
project files may each declare the same name over a different RANGE and each must keep its own.
⚠⚠ **THE FILE ORDER IS LOAD-BEARING AND IS WHY THIS CASE LOOKS ODD.** The declaration that merges
LAST wins the shared table, so the defect is invisible unless the NARROW declaration is the later one:
written the other way round this program answers correctly and pins nothing. `aa.maxon` declares
`Cell` wide and `zz.maxon` declares it narrow, and the names are chosen for that order, not for
readability. ⚠ The wide file's own round trip is the discriminator: 70000 fits `int(0 to 100000)`
and is truncated to 112 (= 70000 mod 256) the moment a range it never declared decides its storage.
`Narrow.stash()`'s 200 is a liveness marker only — it sits inside BOTH ranges and can never tell them
apart. Prints `wide=70000 narrow=200`.
```maxon
// --- file: aa.maxon
typealias Cell = int(0 to 100000)
typealias Cells = Array with Cell

export type Wide
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(70000)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Wide'

// --- file: zz.maxon
typealias Cell = int(0 to 255)
typealias Cells = Array with Cell

export type Narrow
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(200)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Narrow'

// --- file: main.maxon
function main() returns ExitCode
	print("wide={Wide.stash()} narrow={Narrow.stash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
wide=70000 narrow=200
```


<!-- test: file-private-generic-alias-over-different-element-types -->
⭐ The twin of the case above, and the shape where one instance CANNOT stand in for the other. Both
files declare `Bag`, but over element types that share nothing: one is an `Array with String` and the
other an `Array with Num`, so no single specialization serves them and there is no widest declaration
to fall back on. The two declarations are legal for the reason its sibling's are — a plain
`typealias` is file-local (`specs/duplicate-typealias.md`) — and each file's own `Bag.create()` must
reach its own family of methods. ⚠⚠ **THIS ONE DID NOT PRINT A WRONG NUMBER; IT FAULTED.** Measured
before the fix: the program compiled clean and died in `mm_incref` inside `Wide.stash`, because the
surviving `Bag` was the integer array, so pushing a `String` stored a pointer through a one-byte
element and reading it back retained garbage. That is the same defect as the range case — one flat
name-keyed table, one winner, chosen by which file merged last — in the direction where the loser's
values are POINTERS. Prints `wide=5 narrow=200`; `5` is the byte count of the string the wide file
stored and can only be right if that file's `Bag` held a `String`.
```maxon
// --- file: aa.maxon
typealias Len = int(0 to 1000)
typealias Bag = Array with String

export type Wide
	export static function stash() returns Len
		var xs = Bag.create()
		xs.push("hello")
		let v = try xs.get(0) otherwise ""
		return v.count()
	end 'stash'
end 'Wide'

// --- file: zz.maxon
typealias Num = int(0 to 255)
typealias Bag = Array with Num

export type Narrow
	export static function stash() returns Num
		var xs = Bag.create()
		xs.push(200)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Narrow'

// --- file: main.maxon
function main() returns ExitCode
	print("wide={Wide.stash()} narrow={Narrow.stash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
wide=5 narrow=200
```


<!-- test: file-private-generic-alias-named-in-a-signature -->
⭐ The same two declarations as the case above, with the alias named where a TYPE NAME OUTLIVES THE
PASS THAT RESOLVED IT: a function parameter. ⚠⚠ **THIS CRASHED THE COMPILER** — `E9001 An item with
the same key has already been added. Key: wideFill$xs_Cells`, out of `ParameterMutationAnalysisPass`,
on a program that is perfectly legal. A contested alias's type is registered under a structural name,
so `xs Cells` resolved to a type answering `Cells` for its name in the pass that had not yet seen the
contest and `Array_Cell_i64_0to100000` in the pass that had. Overload registration mangles a colliding
signature by that name, so ONE function registered in two passes became TWO, and the second was then
renamed onto the first's name. The cure is that the contest is settled by the whole-project
DECLARATION pass — the one pass that has read every file and minted nothing — so no pass that mints
ever sees the answer change. Prints `wide=70000 narrow=200`, exactly as its sibling: the point is that
it compiles at all, and that the two files still keep their own storage.
```maxon
// --- file: aa.maxon
typealias Cell = int(0 to 100000)
typealias Cells = Array with Cell

function wideFill(xs Cells) returns Cell
	xs.push(70000)
	let v = try xs.get(0) otherwise 0
	return v
end 'wideFill'

export type Wide
	export static function stash() returns Cell
		var xs = Cells.create()
		return wideFill(xs)
	end 'stash'
end 'Wide'

// --- file: zz.maxon
typealias Cell = int(0 to 255)
typealias Cells = Array with Cell

function narrowFill(xs Cells) returns Cell
	xs.push(200)
	let v = try xs.get(0) otherwise 0
	return v
end 'narrowFill'

export type Narrow
	export static function stash() returns Cell
		var xs = Cells.create()
		return narrowFill(xs)
	end 'stash'
end 'Narrow'

// --- file: main.maxon
function main() returns ExitCode
	print("wide={Wide.stash()} narrow={Narrow.stash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
wide=70000 narrow=200
```


<!-- test: file-private-generic-alias-over-float-and-int -->
⭐ Two files' `Cell` agree on a NAME and on both BOUNDS and differ in the only remaining thing — the
base type. ⚠⚠ **A CONTEST TEST THAT SPELLS AN INSTANCE WITHOUT ITS BASE TYPE READS THIS PAIR AS ONE
INSTANCE**, reports agreement, renames nothing, and hands both files the same `Array`. Measured that
way: `wide` printed a double's bit pattern read as an integer in one file order and `narrow` printed
`0` in the other — a wrong answer BOTH ways, and a different wrong answer each way. It is the sibling
cases' defect reached through the identity spelling rather than through the alias table, which is why
it belongs beside them: a projection that drops a distinguishing field does not report a difference,
it reports agreement. Prints `wide=2.5 narrow=70000`.
```maxon
// --- file: aa.maxon
typealias Cell = float(0 to 100000)
typealias Cells = Array with Cell

export type Wide
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(2.5)
		let v = try xs.get(0) otherwise 0.0
		return v
	end 'stash'
end 'Wide'

// --- file: zz.maxon
typealias Cell = int(0 to 100000)
typealias Cells = Array with Cell

export type Narrow
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(70000)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Narrow'

// --- file: main.maxon
function main() returns ExitCode
	print("wide={Wide.stash()} narrow={Narrow.stash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
wide=2.5 narrow=70000
```


<!-- test: module-alias-does-not-govern-another-directory -->
⭐ The `module` twin of `file-private-alias-does-not-govern-another-project-file`, and the SAME rule one
scope wider. A `module` declaration is scoped to its declarer's directory subtree, so two subtrees are
either nested or disjoint — and where two `module typealias` declarations of one name sit in DISJOINT
subtrees no file can see both, so neither may decide the other's storage. ⚠ It did: `scopeB/`'s
`Cell = int(0 to 255)` truncated `scopeA/`'s 70000 to 112 (= 70000 mod 256), and building the same
directory under `MAXON_SOURCE_ORDER=reverse` printed 70000 — the wrong half being whichever file merged
last into the one flat entry the name has. The contest was DETECTED and only the RENAME was gated out,
because the gate asked "is this declaration file-private?" where the question is "may anything outside
this contest name it?". `narrow=200` is a liveness marker only: it sits inside both ranges and can never
tell them apart. ⚠⚠ TWO DIRECTORIES ON PURPOSE — a `module` declaration's scope IS its directory, so the
case cannot be written in one.
```maxon
// --- file: scopeA/aa.maxon
module typealias Cell = int(0 to 100000)
module typealias Cells = Array with Cell

export type Wide
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(70000)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Wide'

// --- file: scopeB/zz.maxon
module typealias Cell = int(0 to 255)
module typealias Cells = Array with Cell

export type Narrow
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(200)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Narrow'

// --- file: main.maxon
function main() returns ExitCode
	print("wide={Wide.stash()} narrow={Narrow.stash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
wide=70000 narrow=200
```


<!-- test: exported-generic-alias-keeps-its-own-element -->
⭐ An `export typealias` over a file-private element type keeps THAT element, even where another file
declares its own file-private alias of the same name over a narrower one. ⚠⚠ **EXACTLY ONE OF THE FOUR
export/file-private PAIRINGS WAS WRONG, WHICH IS WHAT SAYS THE REASON IS ORDER RATHER THAN VISIBILITY.**
Measured before the fix: `export`-wide + file-private-narrow printed `wide=112`; the same program with
the exported side narrow, or with the two modifiers swapped, printed 70000. The narrow file's `Cells` IS
renamed under the contest — it is file-private, so it may be — but it went on publishing its BARE name
into the whole-program type table alongside the structural one, and merging later it won: `Cells.create`
was emitted returning `Array_Cell_i64_0to255` and the exported alias read its own 70000 back one byte
wide. ⚠ The wide side is the EXPORTED one deliberately; written the other way round the program answers
correctly and pins nothing. ⚠ Only `Cells` differs in visibility here — both files' `Cell` is
file-private — so the case cannot pass by accident through the ranged alias's own rules; `wsize` prints
`sizeof(Cell)` for the exported file, which stays 4 in every arrangement and is therefore the half that
does NOT discriminate, kept because a silent disagreement between the two is exactly what reaches the
backend. Prints `wide=70000 narrow=200 wsize=4`.
```maxon
// --- file: aa.maxon
typealias Cell = int(0 to 100000)
export typealias Cells = Array with Cell

export type Wide
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(70000)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'

	export static function width() returns Cell
		return sizeof(Cell)
	end 'width'
end 'Wide'

// --- file: zz.maxon
typealias Cell = int(0 to 255)
typealias Cells = Array with Cell

export type Narrow
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(200)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Narrow'

// --- file: main.maxon
function main() returns ExitCode
	print("wide={Wide.stash()} narrow={Narrow.stash()} wsize={Wide.width()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
wide=70000 narrow=200 wsize=4
```


<!-- test: exported-typealias-collision-unnamed-still-compiles -->
⭐ Two files export `Score` over different ranges and the program never writes the bare name. It
compiles and runs. The collision is a USE-SITE error — `Documentation` above says so, and this is the
half that says so from the other side: the two declarations are individually legal, and refusing them
where they stand would refuse a program that has no ambiguity in it. Returns `7 + 3`.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

export function fromApi() returns ExitCode
	return 7
end 'fromApi'

// --- file: legacy/types.maxon
export typealias Score = int(0 to 200)

export function fromLegacy() returns ExitCode
	return 3
end 'fromLegacy'

// --- file: main.maxon
function main() returns ExitCode
	return fromApi() + fromLegacy()
end 'main'
```
```exitcode
10
```


<!-- test: error.exported-typealias-collision-bare-cast -->
⭐ The bootstrap's own E3063 for the shape `error.exported-typealias-collision` pins for the
self-hosted compiler, written so this compiler runs it: the ambiguous name is reached through an `as`
CAST, and the use sits at the project root so the reported path carries no directory prefix.
⚠⚠ **THE ONE CASE IN THE CORPUS THAT EXISTS TO PIN THIS DIAGNOSTIC WENT THROUGH THE ONE DOOR THAT
COULD NOT RAISE IT.** `ParseTypeRef` consulted the ambiguity set; `ParseTypeKeyword` — the `as` target
— resolved straight out of the type registry and never asked, so the program compiled and returned 50.
⚠ And the set it consults had never held a name: it was computed in `IrModule.Merge`, which a project
build does not reach, because the alias table is written directly. Both halves are fixed, so the
diagnostic that was written, wired and reachable now actually fires.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/types.maxon
export typealias Score = int(0 to 200)

// --- file: main.maxon
function main() returns ExitCode
	let x = 50 as Score
	return x
end 'main'
```
```maxoncstderr
error E3063: specs/fragments/typealias-collision/error.exported-typealias-collision-bare-cast.test:10:16: Ambiguous typealias 'Score': multiple visible definitions found. Qualify with a directory name. Candidates: api.Score, legacy.Score
```


<!-- test: error.exported-and-module-alias-of-one-name -->
⭐ An `export typealias` and a `module typealias` of one name in directories neither of which contains
the other. ⚠⚠ **THIS WAS A SILENT WRONG ANSWER, AND ONLY IN ONE SOURCE ORDER**, which is what says the
reason was order rather than visibility: the two `Cell`s are both ONE BYTE and disagree only on
SIGNEDNESS, so neither value survives the other's storage — 200 read through `int(-100 to 100)` is
−56, −50 read through `int(0 to 255)` is 206 — and the pair printed `b=206` forwards and answered
correctly reversed. The `module` half is renamed under the contest and keeps its own storage, but it
still publishes its bare name for its own subtree to read, and the `export` half cannot be renamed at
all, so the flat type table hands one of them the other's element. Neither declaration reaches past a
directory the other can see, and no file can say which `Cell` it means: E3063. ⚠ It is refused in BOTH
orders. Refusing only the order that answered wrongly would make legality a property of the checkout,
which is the defect, wearing the fix's clothes.
```maxon
// --- file: dirA/a.maxon
export typealias Cell = int(0 to 255)
export typealias Cells = Array with Cell

export type Alpha
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(200)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Alpha'

// --- file: dirB/z.maxon
module typealias Cell = int(-100 to 100)
module typealias Cells = Array with Cell

export type Beta
	export static function stash() returns Cell
		var xs = Cells.create()
		xs.push(-50)
		let v = try xs.get(0) otherwise 0
		return v
	end 'stash'
end 'Beta'

// --- file: main.maxon
function main() returns ExitCode
	print("a={Alpha.stash()} b={Beta.stash()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3063: dirA/specs/fragments/typealias-collision/error.exported-and-module-alias-of-one-name.test:4:37: Ambiguous typealias 'Cell': multiple visible definitions found. Qualify with a directory name. Candidates: dirA.Cell, dirB.Cell
error E3063: dirB/specs/fragments/typealias-collision/error.exported-and-module-alias-of-one-name.test:17:37: Ambiguous typealias 'Cell': multiple visible definitions found. Qualify with a directory name. Candidates: dirA.Cell, dirB.Cell
```


<!-- test: error.ambiguity-does-not-depend-on-a-third-declaration -->
⭐ Two `module typealias Cell`s in ONE directory are a pair no file in that directory can choose
between, and adding a THIRD declaration elsewhere does not resolve them. ⚠⚠ **IT USED TO.** The rule
was asked of a new declaration against whichever declaration held the module's single alias record,
and that holder is picked by a different rule — the widest reach wins — while ambiguity is not a
transitive relation. A project-root `export typealias Cell` is exempt against each `module` one by
the enclosure rule AND is wide enough to keep the record, so with it present the two files were never
compared with each other and the program compiled; without it the same two files were refused.
Legality of a pair must not depend on what else the program declares. ⚠ The candidate list names the
declaring FILE where two declarations share a directory: no directory qualifier can separate them,
and collapsing them by that qualifier printed a list of one.
```maxon
// --- file: dirA/one.maxon
module typealias Cell = int(0 to 255)

export type One
	export static function stash() returns Cell
		let v = 200 as Cell
		return v
	end 'stash'
end 'One'

// --- file: dirA/two.maxon
module typealias Cell = int(0 to 100)

export type Two
	export static function stash() returns Cell
		let v = 50 as Cell
		return v
	end 'stash'
end 'Two'

// --- file: main.maxon
export typealias Cell = int(0 to 300)

function main() returns ExitCode
	return One.stash() + Two.stash()
end 'main'
```
```maxoncstderr
error E3063: dirA/specs/fragments/typealias-collision/error.ambiguity-does-not-depend-on-a-third-declaration.test:6:41: Ambiguous typealias 'Cell': multiple visible definitions found. Qualify with a directory name. Candidates: dirA.Cell (one.maxon), dirA.Cell (two.maxon)
error E3063: dirA/specs/fragments/typealias-collision/error.ambiguity-does-not-depend-on-a-third-declaration.test:16:41: Ambiguous typealias 'Cell': multiple visible definitions found. Qualify with a directory name. Candidates: dirA.Cell (one.maxon), dirA.Cell (two.maxon)
```
