---
feature: namespace-qualified-resolution
status: experimental
keywords: [namespace, directory, qualified, export, module, typealias]
category: organization
---

# Namespace-Qualified Resolution

## Documentation

`specs/namespaces.md` and `specs/typealias-collision.md` state the RULE — a file's namespace is its
directory, `.`-joined, and a declaration observable from outside its file may be named through it
(`utils.helper()`, `api.Score`, `lib.fmt.Score`). This file pins the parts of that rule those two do not
DISCRIMINATE:

- a qualified call is a FREE call, so it is legal in every position a bare call is — including a
  statement of its own, which is the only position a `void` function can be called from at all;
- a qualified alias resolves to the range of the declaration in THAT directory, even in a file that
  declares the same name itself;
- the project ROOT is qualified as `export` (`export.Score`, `export.pick()`), and the standard library as
  `stdlib` (`stdlib.Byte`); a directory path that cannot be written as a qualifier — a top-level piece
  `export`, `stdlib`, `runtime` or a keyword, or any piece that is not a name — is refused when the compile
  starts (E3182);
- the qualifier is a NAME LOOKUP and never a visibility bypass — `export`, `module` and file-private each
  answer at the qualified spelling exactly as they answer at the bare one;
- a bare name declared as a free function in several directories means the one declaration the caller
  may name — a declaration it cannot see never counts toward ambiguity, and two visible ones are E3095;
- a bare TYPE name means the reading file's own declaration when it has one, and otherwise the one
  declaration it may name; two visible ones — from two directories, or a directory and the standard
  library — are E3063, whose candidate list names each by its qualified spelling;
- so that each qualified spelling names exactly one declaration, a directory holds at most one
  NAMEABLE (`export`, `public` or `module`) declaration of a type name: a second one in another file of the
  same directory is refused at its declaration, E3061 for two aliases and E3006 otherwise. A file-private
  alias beside it is legal and means itself in its own file;
- a `Type.method` reading of the same tokens always wins, so a directory may be named after a type
  without moving a single call;
- the CALL position and the TYPE position read one and the same namespace, segment for segment — a
  directory name is a filesystem name and owes the grammar nothing.

## Tests

<!-- test: namespace-qualified-void-call-statement -->
A qualified call written as a STATEMENT — the only position a `void` function can be called from.
Both segment counts are exercised, because they reach the parser through different lookaheads: the
statement door's bare-call arm tests `identifier (`, which a qualifier's `.` fails, so without a
qualified arm of its own a namespaced module could declare a void function that no file outside its own
directory could ever call.
```maxon
// --- file: lib/inner/deep.maxon
export typealias Integer = int(0 to 125)

export function bump(v Integer) returns Integer
	return v + 1
end 'bump'

export function shout()
	print("shouted")
end 'shout'

// --- file: lib/top.maxon
export typealias Integer = int(0 to 125)

export function twice(v Integer) returns Integer
	return v + v
end 'twice'

// --- file: app/main.maxon
function main() returns ExitCode
	lib.inner.shout()
	let v = lib.inner.bump(20)
	return lib.twice(v)
end 'main'
```
```exitcode
42
```
```stdout
shouted
```


<!-- test: qualified-alias-carries-its-own-declaring-range -->
Two directories export a `Score` over different ranges. `api.Score` admits 50 and `legacy.Score`
admits 5, and each is checked against ITS OWN declaration — the discrimination the collision spec's
own cases cannot make, because both of their values happen to fit both ranges.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/types.maxon
export typealias Score = int(0 to 10)

// --- file: app/main.maxon
function main() returns ExitCode
	let wide = 50 as api.Score
	let narrow = 5 as legacy.Score
	return (wide + (narrow as api.Score)) as ExitCode
end 'main'
```
```exitcode
55
```


<!-- test: error.qualified-alias-out-of-its-own-range -->
The same two declarations, with a value that fits the WIDER one written against the narrower
qualified name. The refusal quotes the qualified spelling the author wrote and the bounds of the
declaration in that directory — proof that the qualifier selected a declaration rather than merely
being accepted as a name.
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/types.maxon
export typealias Score = int(0 to 10)

// --- file: app/main.maxon
function main() returns ExitCode
	let b = 50 as legacy.Score
	return b
end 'main'
```
```maxoncstderr
error E3005: app/specs/fragments/namespace-qualified-resolution/error.qualified-alias-out-of-its-own-range.test:10:13: Value 50 is outside the range of 'legacy.Score' (int(0 to 10))
```

<!-- test: error.a-qualified-alias-beside-a-same-named-type-takes-its-own-declarations-range -->
An alias whose name another directory's type also holds is still selected by its qualifier: `stdlib.Byte` is
the library's `0 to 255` even in a file whose own `Byte` is wider.
```maxon
// --- file: a/t.maxon
export type Byte
	export let v as ExitCode

	export static function make() returns Byte
		return Byte{v: 0}
	end 'make'
end 'Byte'

// --- file: app/main.maxon
typealias Byte = int(0 to 1000)

function main() returns ExitCode
	let wide = 300 as Byte
	let narrow = 300 as stdlib.Byte
	return (wide - narrow) as ExitCode + a.Byte.make().v
end 'main'
```
```maxoncstderr
error E3005: app/specs/fragments/namespace-qualified-resolution/error.a-qualified-alias-beside-a-same-named-type-takes-its-own-declarations-range.test:16:19: Value 300 is outside the range of 'stdlib.Byte' (int(0 to 255))
```


<!-- test: qualified-alias-in-signature-positions -->
A qualified alias is a TYPE, so it is legal wherever a type name is — a parameter and a return
clause, not only an `as` cast target. The call in the same program is qualified through a
multi-segment directory chain AND names a function declared with a keyword (`from`), which the
declaration rules admit and which a qualifier walk that demanded plain identifiers would have made
unreachable.
```maxon
// --- file: lib/inner/deep.maxon
export function from(v api.Score) returns api.Score
	return v + 1
end 'from'

// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function take(v api.Score) returns api.Score
	return v
end 'take'

function main() returns ExitCode
	let a = take(41)
	return lib.inner.from(a)
end 'main'
```
```exitcode
42
```


<!-- test: static-method-outranks-a-same-named-directory -->
A directory `Point/` exports a free `make`, and a `type Point` declares a static `make`. The tokens
`Point.make()` are identical for both readings and the TYPE reading wins, so naming a directory
after a type moves no existing call.
```maxon
// --- file: Point/free.maxon
public typealias Integer = int(0 to 125)

public function make() returns Integer
	return 11
end 'make'

// --- file: app/main.maxon
export typealias Integer = int(0 to 125)

public type Point
	export var x as Integer

	export static function make() returns Integer
		return 22
	end 'make'
end 'Point'

function main() returns ExitCode
	return Point.make()
end 'main'
```
```exitcode
22
```


<!-- test: keyword-named-directory-segment-resolves-in-both-positions -->
A namespace segment whose name is a KEYWORD (`lib/from/`). A directory name is a FILESYSTEM name and owes
the grammar nothing, so the two positions that walk a dotted chain — the call door and the type door —
must admit the same segments. A type door that refused `5 as lib.from.Score` with
`E3011: Unknown type 'lib'` would be a wrong rejection quoting a fragment of the name the author
wrote, for a declaration filed under exactly that key. Both spellings appear here, in one program.
```maxon
// --- file: lib/from/h.maxon
export typealias Integer = int(0 to 125)

export typealias Score = int(0 to 100)

export function helper() returns Integer
	return 7
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	let s = 5 as lib.from.Score
	return ((lib.from.helper() as lib.from.Score) + s) as ExitCode
end 'main'
```
```exitcode
12
```


<!-- test: error.qualified-call-to-non-exported-function -->
The qualifier is a name LOOKUP, not a visibility bypass: a file-private function named through its
directory is refused by the tier its declaration wrote, and the message is the one that says what is
actually wrong rather than "no such function".
```maxon
// --- file: utils/helper.maxon
export typealias Integer = int(0 to 125)

function hiddenHelper() returns Integer
	return 7
end 'hiddenHelper'

export function seen() returns Integer
	return hiddenHelper()
end 'seen'

// --- file: app/main.maxon
function main() returns ExitCode
	return utils.hiddenHelper()
end 'main'
```
```maxoncstderr
error E3008: app/specs/fragments/namespace-qualified-resolution/error.qualified-call-to-non-exported-function.test:15:15: function 'hiddenHelper' is not exported
```


<!-- test: error.multi-segment-qualified-call-to-module-scoped-function -->
The same rule through a multi-segment qualifier and the middle tier: a `module` declaration named
from outside its directory subtree is refused with the tier's own diagnostic.
```maxon
// --- file: lib/inner/deep.maxon
export typealias Integer = int(0 to 125)

module function scopedHelper() returns Integer
	return 7
end 'scopedHelper'

export function seen() returns Integer
	return scopedHelper()
end 'seen'

// --- file: app/main.maxon
function main() returns ExitCode
	return lib.inner.scopedHelper()
end 'main'
```
```maxoncstderr
error E3088: app/specs/fragments/namespace-qualified-resolution/error.multi-segment-qualified-call-to-module-scoped-function.test:15:9: function 'scopedHelper' is module-scoped and not visible from this directory
```


<!-- test: error.module-tier-alias-is-not-nameable-outside-its-subtree -->
A `module`-visible typealias named through its directory from a file outside that subtree. The
qualified spelling finds the declaration — which is what makes this the sharper diagnostic rather
than "unknown type" — and the tier then refuses it.
```maxon
// --- file: feature/types.maxon
module typealias Level = int(0 to 50)

module function seed() returns Level
	return 21
end 'seed'

// --- file: app/main.maxon
function main() returns ExitCode
	let x = 21 as feature.Level
	return x
end 'main'
```
```maxoncstderr
error E3088: app/specs/fragments/namespace-qualified-resolution/error.module-tier-alias-is-not-nameable-outside-its-subtree.test:11:16: typealias 'feature.Level' is module-scoped and not visible from this directory
```


<!-- test: error.hidden-alias-as-a-qualified-static-call-head -->
A typealias named through its directory as the HEAD of a static call, `lib.ScoreArray.create()`, is refused
like the same name in any other type position: the declaration is file-private to `lib/scores.maxon`, and
the qualified spelling reaches it without making it visible.
```maxon
// --- file: lib/scores.maxon
typealias Score = int(0 to 100)
typealias ScoreArray = Array with Score

export function probeScores() returns ExitCode
	var held = ScoreArray.create()
	held.push(3)
	return 0 if held.isEmpty() else 3
end 'probeScores'

// --- file: app/main.maxon
function main() returns ExitCode
	let made = lib.ScoreArray.create()

	if made.isEmpty() 'nothingHeld'
		return lib.probeScores()
	end 'nothingHeld'

	return 1
end 'main'
```
```maxoncstderr
error E3008: app/specs/fragments/namespace-qualified-resolution/error.hidden-alias-as-a-qualified-static-call-head.test:14:13: typealias 'lib.ScoreArray' is not exported
```


<!-- test: error.same-directory-underlying-conflict-is-reported-once -->
Two files in ONE directory export a `Score` over different underlying primitives. The pair is one
mistake and earns exactly one diagnostic, E3061 at the second declaration — a directory-qualified
declaration is filed under its qualified spelling as well, and that second entry must not report the
duplicate a second time under a name no file contains.
```maxon
// --- file: api/a.maxon
export typealias Score = int(0 to 100)

// --- file: api/b.maxon
export typealias Score = float(0.0 to 1.0)

// --- file: app/main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3061: api/specs/fragments/namespace-qualified-resolution/error.same-directory-underlying-conflict-is-reported-once.test:6:18: Duplicate typealias 'Score'
```


<!-- test: contested-free-function-pair-routes-each-qualifier-to-its-own -->
The FUNCTION half of directory-as-module, and the case a bare-name registry cannot express: two
directories each `export function pick`, and BOTH are callable through their own qualifier.

The two answers are deliberately distinct and combined positionally (`a * 10 + b`), so neither
direction of aliasing can pass by coincidence: correct is 35; both calls reaching `alpha`'s gives
33, both reaching `beta`'s gives 55. That is exactly the wrong answer a compiler produces if it
accepts the two declarations at decl time — which the language requires — but leaves them sharing
one bare registration name, so a qualifier is a route rather than a name.
```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 3
end 'pick'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 5
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (alpha.pick() * 10 + beta.pick()) as ExitCode
end 'main'
```
```exitcode
35
```


<!-- test: contested-free-function-bare-call-means-its-own-directory -->
"Local namespace wins": once a name is contested, a file that declares one of the competitors goes
on calling it UNQUALIFIED and means its own. Without this tier a directory would be forced to
qualify its own declarations the moment an unrelated directory happened to pick the same name — and
the diagnostic it would get instead is E3095, an ambiguity that is not one.

Same 3/5/35 discrimination as the case above, reached through a bare call in each declaring file.
```maxon
// --- file: alpha/a.maxon
public typealias Integer = int(0 to 125)

public function pick() returns Integer
	return 3
end 'pick'

export function localCaller() returns Integer
	return pick()
end 'localCaller'

// --- file: beta/b.maxon
public typealias Integer = int(0 to 125)

public function pick() returns Integer
	return 5
end 'pick'

export function localCaller() returns Integer
	return pick()
end 'localCaller'

// --- file: app/main.maxon
function main() returns ExitCode
	return (alpha.localCaller() * 10 + beta.localCaller()) as ExitCode
end 'main'
```
```exitcode
35
```


<!-- test: error.free-function-pair-in-one-directory-still-collides -->
The boundary of the relaxation above, from the inside: two files in ONE directory share one
namespace, so their `pick` declarations are one name declared twice and stay a hard duplicate. The
reader qualifies with `dir.` and there is still only one thing that could mean.

⚠ **THE REFUSAL'S SENTENCE NAMES A MINTED KEY.** Two files of one directory declaring one
free-function name are an OVERLOAD SET (`cross-file-overload-set.md`), so every one of these
declarations is registered under its parameter-type spelling — and these two spell the same
parameters (none), claim the same `pick#`, and collide there. The name E3006 quotes is therefore one
NEITHER declaration wrote, which is the property `ParseStaging.duplicateFunctionMessage` sorts on: a
minted name earns the sentence that explains where it came from, because told only
`Duplicate function 'pick#'` an author would search for a string that appears nowhere in their
source.
```maxon
// --- file: dir/a.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 3
end 'pick'

// --- file: dir/b.maxon
export function pick() returns Integer
	return 5
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return dir.pick()
end 'main'
```
```maxoncstderr
error E3006: dir/specs/fragments/namespace-qualified-resolution/error.free-function-pair-in-one-directory-still-collides.test:10:17: duplicate definition of function 'pick#' — 'pick' is declared as a free function in more than one FILE of its directory, so every one of those declarations is registered under its parameter-type spelling, and two of them spell the same parameters. Give the overloads distinct parameter types, or distinct names
```


<!-- test: error.flat-root-level-free-function-pair-still-collides -->
The same boundary from the other side, and the one every other cross-file collision case in
this suite stands on: ROOT is a directory — the global namespace — so two root-level files declaring
`pick` share a namespace and collide. A relaxation that keyed the contest
on "different FILE" rather than "different DIRECTORY" would un-collide the whole of
`type-name-collision.md` and `stdlib-user-shadows.md`, whose fixtures are all flat and root-level.

⚠ The sentence names a minted key for the case above's reason: two declarations that
spell one parameter list claim one registration name whichever directory they sit in.
```maxon
// --- file: a.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 3
end 'pick'

// --- file: b.maxon
export function pick() returns Integer
	return 5
end 'pick'

// --- file: main.maxon
function main() returns ExitCode
	return pick()
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/namespace-qualified-resolution/error.flat-root-level-free-function-pair-still-collides.test:10:17: duplicate definition of function 'pick#' — 'pick' is declared as a free function in more than one FILE of its directory, so every one of those declarations is registered under its parameter-type spelling, and two of them spell the same parameters. Give the overloads distinct parameter types, or distinct names
```


<!-- test: error.two-main-declarations-in-different-directories-still-collide -->
`main` is the ONE free function never qualified by its directory: the entry point is one name for the
whole program. Qualifying it would turn a
hard duplicate into two silently-accepted entry points, neither of which the entry-point check would
find under the name it looks for.
```maxon
// --- file: alpha/m.maxon
function main() returns ExitCode
	return 1
end 'main'

// --- file: beta/m.maxon
function main() returns ExitCode
	return 2
end 'main'
```
```maxoncstderr
error E3006: beta/specs/fragments/namespace-qualified-resolution/error.two-main-declarations-in-different-directories-still-collide.test:8:10: Duplicate function 'main'
```


<!-- test: same-directory-alias-pair-is-not-offered-as-its-own-disambiguation -->
Two files in ONE directory export `Score` over two RANGES of one primitive. Both would be spelled
`api.Score`, so no qualification could tell them apart: the pair is a duplicate whatever the ranges, E3061
at the second declaration, with no reader needed to provoke it.
```maxon
// --- file: api/a.maxon
export typealias Score = int(0 to 100)

// --- file: api/b.maxon
export typealias Score = int(0 to 200)

// --- file: app/main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3061: api/<fragment>:6:18: Duplicate typealias 'Score'
```


<!-- test: contested-free-function-defaults-follow-the-declaration -->
**A CONTESTED FUNCTION'S SYNTHESIZED DEFAULT HELPERS ARE RENAMED WITH IT.** A default value is
compiled as a nullary helper function whose name `Project.paramDefaultHelperName` mints from the
DECLARING function's name, and that mint's uniqueness cannot rest on
*"`funcName` is unique whole-program (a second declaration of one name is E3006)"*. Two
directories may each declare `pick`, and the sweep (which runs before any contest is known) mints
`__paramDefault#pick#0` for BOTH.

Left under that name, the pair would be refused as a duplicate definition of
`__paramDefault#pick#0` — a symbol absent from the source, against a program this file's own rule
accepts. The helper's name has to follow the DECLARATION's identity, which the fold has just decided.

3/5/35 again, so an aliased pair cannot pass by coincidence: both defaults reaching alpha's gives
33, both reaching beta's gives 55.
```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(0 to 125)

export function pick(n Integer = 3) returns Integer
	return n
end 'pick'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function pick(n Integer = 5) returns Integer
	return n
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (alpha.pick() * 10 + beta.pick()) as ExitCode
end 'main'
```
```exitcode
35
```


<!-- test: contested-free-function-nested-directory-qualifier -->
**A CONTESTED FREE FUNCTION IN A NESTED DIRECTORY, CALLED THROUGH ITS MULTI-SEGMENT QUALIFIER.**
The `declaresCallee` veto in `resolvesAsNamespaceQualifiedFunction` means "a `type lib.inner` already
wears this key, so the TYPE reading wins" — but a contested free function is registered under its own
directory-qualified spelling, so `declaresCallee("lib.inner.pick")` is true OF THE FREE FUNCTION
ITSELF, and the veto must not fire against the one name the call can reach.

The one-dot spelling cannot show this, because `parseQualifiedCall`'s static arm builds the same
string and finds the same declaration, so the two-dot shape is the one pinned.
```maxon
// --- file: lib/inner/x.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 3
end 'pick'

// --- file: beta/y.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 5
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (lib.inner.pick() * 10 + beta.pick()) as ExitCode
end 'main'
```
```exitcode
35
```


<!-- test: contested-free-function-three-directories -->
The contest's first arm handles TWO declarations; this is the third. The first pair is what
mints the contest and re-files the INCUMBENT's already-folded entries; a third directory finds the
name already contested and has nothing left to move, so it takes a different arm of
`noteFreeFunctionDeclaration` entirely. Positional digits (100/10/1) so no pair of aliased calls
produces the right total.
```maxon
// --- file: alpha/f.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 1
end 'pick'

// --- file: beta/f.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 2
end 'pick'

// --- file: gamma/f.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 4
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (alpha.pick() * 100 + beta.pick() * 10 + gamma.pick()) as ExitCode
end 'main'
```
```exitcode
124
```


<!-- test: contested-free-function-root-declaration-keeps-the-bare-name -->
A ROOT declaration contests the name but contributes NO qualified spelling, because it has none —
the root IS the unqualified namespace. Its entries keep the bare key, so a bare call from anywhere
finds it while the subdirectory's is reached through `beta.`. This is the arm
`freeFunctionRegistrationName` and `addContestedSpelling` both return early from, and the only case
in which a contested name still answers to bare bytes.
```maxon
// --- file: r.maxon
public typealias Integer = int(0 to 125)

public function pick() returns Integer
	return 3
end 'pick'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 5
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (pick() * 10 + beta.pick()) as ExitCode
end 'main'
```
```exitcode
35
```


<!-- test: contested-free-function-void-call-statement -->
A contested pair called as VOID STATEMENTS through their qualifiers — the statement-position door
(`namespaceQualifiedCallStmt`), which admits ONE dot where expression position starts at two. It
reaches `parseNamespaceQualifiedCall` directly rather than through the static-call arm, so it is the
one door a `Type.method` fallthrough does not cover.
```maxon
// --- file: alpha/a.maxon
export function shout()
	print("A")
end 'shout'

// --- file: beta/b.maxon
export function shout()
	print("B")
end 'shout'

// --- file: app/main.maxon
function main() returns ExitCode
	alpha.shout()
	beta.shout()
	return 0
end 'main'
```
```stdout
AB
```


<!-- test: contested-bare-call-with-exactly-one-visible-candidate-resolves -->
**A DECLARATION THE CALLER CANNOT SEE NEVER COUNTS TOWARD A CONTEST.** `helper` is declared in two
directories; from `app/`, alpha's is `module`-scoped and out of reach, so beta's exported one is the
only candidate and the bare call resolves to it. Ambiguity is a question about what a file could
MEAN, and a file cannot mean a name it may not write — otherwise a `module` helper added inside
`alpha/` would break a call in `app/` that can only ever reach `beta/`.

alpha's helper answers 3 and beta's 7, so the exit code says which one was reached.
```maxon
// --- file: alpha/a.maxon
module function helper() returns alpha.Integer
	return 3
end 'helper'

// --- file: alpha/use.maxon
export typealias Integer = int(0 to 125)

export function useAlpha() returns Integer
	return helper()
end 'useAlpha'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function helper() returns Integer
	return 7
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	return (useAlpha() * 10 + helper()) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: contested-bare-call-inside-a-borrow-parses-on-a-worker -->
The same one-visible resolution, reached from a body that holds a borrow: `for cell in cells` borrows the
array, and `helper(cell.s)` hands the callee a field of the borrowed element, so the parse records the call
as a possible write under the name the call binds. That name is beta's qualified spelling, which the whole-
program index files; the parse's own record must be a copy of it, because the file's parse crosses back from
a front-end worker that keeps its index. alpha's helper answers 3 and beta's the length of `hello`.
```maxon
// --- file: alpha/a.maxon
module function helper(s String) returns alpha.Integer
	return (s.byteLength() + 2) as alpha.Integer
end 'helper'

// --- file: alpha/use.maxon
export typealias Integer = int(0 to 125)

export function useAlpha() returns Integer
	return helper("a")
end 'useAlpha'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function helper(s String) returns Integer
	return s.byteLength() as Integer
end 'helper'

// --- file: app/main.maxon
typealias Count = int(0 to 125)

type Cell
	export let s as String

	static function create(s String) returns Cell
		return Self{s: s}
	end 'create'
end 'Cell'

typealias Cells = Array with Cell

function total(cells Cells) returns Count
	var sum = 0 as Count

	for cell in cells 'each'
		sum = helper(cell.s) as Count
	end 'each'

	return sum
end 'total'

function main() returns ExitCode
	var cells = Cells.create()
	cells.push(Cell.create("hello"))
	return (useAlpha() as Count + total(cells)) as ExitCode
end 'main'
```
```exitcode
8
```


<!-- test: contested-bare-call-resolves-past-a-file-private-competitor-declared-first -->
A file-private competitor in another directory, folded BEFORE the exported declaration. The two
declarations disagree about the return type (`bool` against an integer), so a call that bound the
private one would be refused at the cast rather than merely answer differently; `true 8` is only
reachable with each call reaching its own declaration.
```maxon
// --- file: alpha/a.maxon
typealias Integer = int(0 to 125)

function helper(raw Integer) returns bool
	return raw > 3
end 'helper'

export function useAlpha() returns bool
	return helper(9)
end 'useAlpha'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function helper(raw Integer) returns Integer
	return raw + 7
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	let flag = useAlpha()
	let n = helper(1)
	print("{flag} {n}\n")
	return n as ExitCode
end 'main'
```
```exitcode
8
```
```stdout
true 8
```


<!-- test: contested-bare-call-resolves-past-a-file-private-competitor-declared-second -->
The same pair with the exported declaration folded FIRST, so neither order of the two can decide the
answer.
```maxon
// --- file: alpha/b.maxon
export typealias Integer = int(0 to 125)

export function helper(raw Integer) returns Integer
	return raw + 7
end 'helper'

// --- file: zeta/a.maxon
typealias Integer = int(0 to 125)

function helper(raw Integer) returns bool
	return raw > 3
end 'helper'

export function useZeta() returns bool
	return helper(9)
end 'useZeta'

// --- file: app/main.maxon
function main() returns ExitCode
	let flag = useZeta()
	let n = helper(1)
	print("{flag} {n}\n")
	return n as ExitCode
end 'main'
```
```exitcode
8
```
```stdout
true 8
```


<!-- test: contested-bare-call-resolves-past-an-invisible-root-competitor -->
A ROOT declaration keeps the bare key, but a file-private one is still invisible from `app/`, so it is
no candidate there and beta's exported declaration is the one the bare call means.
```maxon
// --- file: r.maxon
public typealias Integer = int(0 to 125)

function helper() returns Integer
	return 3
end 'helper'

public function useRoot() returns Integer
	return helper()
end 'useRoot'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function helper() returns Integer
	return 7
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	return (useRoot() * 10 + helper()) as ExitCode
end 'main'
```
```exitcode
37
```


<!-- test: contested-bare-call-resolves-past-an-invisible-same-directory-competitor -->
The caller's OWN directory declares the name, but in another file and file-private, so the local tier
has nothing this file may name and the one visible declaration elsewhere is the answer.
```maxon
// --- file: app/other.maxon
export typealias Integer = int(0 to 125)

function helper() returns Integer
	return 3
end 'helper'

export function useOther() returns Integer
	return helper()
end 'useOther'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function helper() returns Integer
	return 7
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	return (useOther() * 10 + helper()) as ExitCode
end 'main'
```
```exitcode
37
```


<!-- test: contested-bare-function-value-resolves-past-an-invisible-competitor -->
A bare name READ as a function value answers the same question a bare call does: the one declaration
the file may name, here beta's, whose return type the value's calls carry.
```maxon
// --- file: alpha/a.maxon
typealias Integer = int(0 to 125)

function helper(raw Integer) returns bool
	return raw > 3
end 'helper'

export function useAlpha() returns bool
	return helper(9)
end 'useAlpha'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function helper(raw Integer) returns Integer
	return raw + 7
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	let flag = useAlpha()
	let f = helper
	let n = f(1)
	print("{flag} {n}\n")
	return n as ExitCode
end 'main'
```
```exitcode
8
```
```stdout
true 8
```


<!-- test: error.contested-bare-function-value-with-two-visible-candidates -->
A function value is refused for the ambiguity a call is refused for, rather than bound to either
declaration.
```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(0 to 125)

export function helper(raw Integer) returns Integer
	return raw + 3
end 'helper'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function helper(raw Integer) returns Integer
	return raw + 7
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	let f = helper
	return f(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3095: app/<fragment>:18:10: Ambiguous bare function name 'helper' used as a value: more than one visible declaration matches it. Qualify it as one of: alpha.helper, beta.helper
```


<!-- test: contested-function-value-named-by-its-directory -->
The remedy E3095 names is writable at the value door: a directory-qualified name read as a value binds the
declaration that directory holds, exactly as a qualified call does.
```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(0 to 125)

export function helper(raw Integer) returns Integer
	return raw + 3
end 'helper'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export function helper(raw Integer) returns Integer
	return raw + 7
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	let f = beta.helper
	let g = alpha.helper
	return (f(1) * 10 + g(1)) as ExitCode
end 'main'
```
```exitcode
84
```


<!-- test: function-value-named-by-its-directory -->
An uncontested name qualified by its directory is a route to the one declaration, as a value as well as a call.
```maxon
// --- file: lib/util.maxon
export typealias Integer = int(0 to 125)

export function triple(raw Integer) returns Integer
	return raw * 3
end 'triple'

// --- file: app/main.maxon
function main() returns ExitCode
	let f = lib.triple
	return f(4) as ExitCode
end 'main'
```
```exitcode
12
```


<!-- test: error.a-directory-qualified-function-value-is-refused-by-the-name-it-was-written-as -->
A refusal at the value door quotes the name the author wrote, not the declaration it resolved to.
```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(0 to 125)

export function risky(raw Integer) returns Integer
	return raw
end 'risky'

// --- file: beta/b.maxon
export typealias Integer = int(0 to 125)

export enum Oops implements Error
	bad
end 'Oops'

export function risky(raw Integer) returns Integer throws Oops
	if raw > 100 'tooBig'
		throw Oops.bad
	end 'tooBig'

	return raw
end 'risky'

// --- file: app/main.maxon
function main() returns ExitCode
	let f = beta.risky
	return f(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3101: app/<fragment>:26:10: Cannot use throwing function 'beta.risky' as a value: it throws 'Oops', and a function type cannot express 'throws'. Wrap the call in a non-throwing function that handles the error with 'try'.
```


<!-- test: error.two-visible-candidates-are-ambiguous-past-an-invisible-root-declaration -->
A file-private ROOT declaration keeps the bare key, but from `app/` it is no candidate: the call is
ambiguous between the two visible subdirectory declarations, and the root one is neither reported as
"not exported" nor listed.
```maxon
// --- file: r.maxon
public typealias Integer = int(0 to 125)

function pick() returns Integer
	return 1
end 'pick'

public function useRoot() returns Integer
	return pick()
end 'useRoot'

// --- file: alpha/f.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 2
end 'pick'

// --- file: zulu/f.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 4
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (pick() + useRoot()) as ExitCode
end 'main'
```
```maxoncstderr
error E3095: app/<fragment>:29:10: Ambiguous bare-name call to 'pick': more than one visible declaration matches it. Qualify it as one of: alpha.pick, zulu.pick
```


<!-- test: error.two-visible-candidates-are-ambiguous-under-try -->
A `try` over the contested bare call gets the same ambiguity. Both candidates throw, so no claim about
whether "the" function throws can be made before the call is resolved.
```maxon
// --- file: r.maxon
public enum Oops implements Error
	bad
end 'Oops'

public typealias Integer = int(0 to 125)

// --- file: alpha/f.maxon
export function pick() returns Integer throws Oops
	throw Oops.bad
end 'pick'

// --- file: zulu/f.maxon
export function pick() returns Integer throws Oops
	return try int.fromString("4") otherwise throw Oops.bad
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	let v = try pick() otherwise 9
	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3095: app/<fragment>:21:14: Ambiguous bare-name call to 'pick': more than one visible declaration matches it. Qualify it as one of: alpha.pick, zulu.pick
```


<!-- test: error.two-visible-candidates-with-different-results-are-ambiguous -->
The candidates disagree about the result, so the call's result is not typed from either of them ahead of
the ambiguity.
```maxon
// --- file: r.maxon
public enum Oops implements Error
	bad
end 'Oops'

public typealias Integer = int(0 to 125)

// --- file: alpha/f.maxon
export function pick() returns Integer
	return 3
end 'pick'

// --- file: zulu/f.maxon
export function pick() returns String
	return "abcd"
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	let v = pick()
	return v.byteLength() as ExitCode
end 'main'
```
```maxoncstderr
error E3095: app/<fragment>:21:10: Ambiguous bare-name call to 'pick': more than one visible declaration matches it. Qualify it as one of: alpha.pick, zulu.pick
```


<!-- test: error.two-visible-candidates-are-ambiguous-under-if-try -->
An `if let … = try` over the contested bare call gets the ambiguity.
```maxon
// --- file: r.maxon
public enum Oops implements Error
	bad
end 'Oops'

public typealias Integer = int(0 to 125)

// --- file: alpha/f.maxon
export function pick() returns Integer throws Oops
	throw Oops.bad
end 'pick'

// --- file: zulu/f.maxon
export function pick() returns Integer throws Oops
	return try int.fromString("4") otherwise throw Oops.bad
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	if let v = try pick() 'ok'
		return v as ExitCode
	end 'ok'

	return 9
end 'main'
```
```maxoncstderr
error E3095: app/<fragment>:21:17: Ambiguous bare-name call to 'pick': more than one visible declaration matches it. Qualify it as one of: alpha.pick, zulu.pick
```


<!-- test: error.two-visible-candidates-are-ambiguous-when-spawned-and-awaited-under-try -->
A spawn of the contested bare call gets the ambiguity, not a claim about whether its promise throws.
```maxon
// --- file: r.maxon
public enum Oops implements Error
	bad
end 'Oops'

public typealias Integer = int(0 to 125)

// --- file: alpha/f.maxon
export function pick() returns Integer throws Oops
	throw Oops.bad
end 'pick'

// --- file: zulu/f.maxon
export function pick() returns Integer throws Oops
	return try int.fromString("4") otherwise throw Oops.bad
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = async pick()
	let v = try await p otherwise 9
	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3095: app/<fragment>:21:16: Ambiguous bare-name call to 'pick': more than one visible declaration matches it. Qualify it as one of: alpha.pick, zulu.pick
```


<!-- test: error.two-visible-candidates-are-ambiguous-when-spawned-inside-try-await -->
The same spawn written inside the `try await` gets the ambiguity.
```maxon
// --- file: r.maxon
public enum Oops implements Error
	bad
end 'Oops'

public typealias Integer = int(0 to 125)

// --- file: alpha/f.maxon
export function pick() returns Integer throws Oops
	throw Oops.bad
end 'pick'

// --- file: zulu/f.maxon
export function pick() returns Integer throws Oops
	return try int.fromString("4") otherwise throw Oops.bad
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	let v = try await async pick() otherwise 9
	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3095: app/<fragment>:21:26: Ambiguous bare-name call to 'pick': more than one visible declaration matches it. Qualify it as one of: alpha.pick, zulu.pick
```


<!-- test: error.two-visible-candidates-are-ambiguous-when-a-spawn-is-stored -->
Storing the spawn's promise gets the ambiguity, not a mismatch with the storage's throws clause.
```maxon
// --- file: r.maxon
public enum Oops implements Error
	bad
end 'Oops'

public typealias Integer = int(0 to 125)

// --- file: alpha/f.maxon
export function pick() returns Integer throws Oops
	throw Oops.bad
end 'pick'

// --- file: zulu/f.maxon
export function pick() returns Integer throws Oops
	return try int.fromString("4") otherwise throw Oops.bad
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	var ps = IntPromises.create()
	ps.push(async pick())
	let p = try ps.pop() otherwise return 8
	let v = try await p otherwise 9
	return v as ExitCode
end 'main'

typealias IntPromise = Promise with (Integer, Oops)
typealias IntPromises = Array with IntPromise
```
```maxoncstderr
error E3095: app/<fragment>:22:16: Ambiguous bare-name call to 'pick': more than one visible declaration matches it. Qualify it as one of: alpha.pick, zulu.pick
```


<!-- test: contested-free-function-root-declaration-does-not-inherit-a-subdirectory-candidates-facts -->
The root declaration keeps the bare name, and what is true of another directory's `pick` — here that it
never returns — is not true of it: the call returns and the statement after it runs.
```maxon
// --- file: alpha/f.maxon
function pick()
	panic("alpha never returns")
end 'pick'

// --- file: beta/f.maxon
function pick()
	print("beta pick\n")
end 'pick'

// --- file: main.maxon
function pick()
	print("root pick\n")
end 'pick'

function main() returns ExitCode
	pick()
	print("after\n")
	return 0
end 'main'
```
```stdout
root pick
after
```
```exitcode
0
```


<!-- test: contested-free-function-root-declaration-does-not-inherit-always-throwing -->
The same holds for "always throws": the root `pick` returns normally, so a propagating `try pick()` is
followed by the next statement.
```maxon
// --- file: alpha/f.maxon
function pick() throws Oops
	throw Oops.bad
end 'pick'

// --- file: beta/f.maxon
function pick()
	print("beta pick\n")
end 'pick'

// --- file: main.maxon
module enum Oops implements Error
	bad
end 'Oops'

function pick() throws Oops
	if File.exists(FilePath from "pick-fails.txt") 'present'
		throw Oops.bad
	end 'present'

	print("root pick\n")
end 'pick'

function run() throws Oops
	try pick()
	print("after\n")
end 'run'

function main() returns ExitCode
	try run() otherwise return 3
	return 0
end 'main'
```
```stdout
root pick
after
```
```exitcode
0
```


<!-- test: error.two-visible-candidates-are-ambiguous-past-an-invisible-third -->
Two VISIBLE declarations are still an ambiguity, and the invisible third is neither what makes it one
nor offered as a way out of it.
```maxon
// --- file: alpha/f.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 1
end 'pick'

// --- file: mid/f.maxon
export typealias Integer = int(0 to 125)

function pick() returns Integer
	return 2
end 'pick'

export function useMid() returns Integer
	return pick()
end 'useMid'

// --- file: zulu/f.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 4
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (pick() + mid.useMid()) as ExitCode
end 'main'
```
```maxoncstderr
error E3095: app/<fragment>:29:10: Ambiguous bare-name call to 'pick': more than one visible declaration matches it. Qualify it as one of: alpha.pick, zulu.pick
```


<!-- test: error.contested-free-function-in-a-directory-named-after-a-type -->
**A CONTESTED SPELLING AND A `Type.method` KEY ARE THE SAME BYTES, AND THE PROGRAM IS REFUSED FOR IT.**
A contested free function is registered as `<directory>.<name>` — deliberately the same construction,
in the same flat key space, a method is filed under. That is also a collision waiting to happen: a
directory called `Point/` holding a contested `create` claims the exact key `type Point`'s static
factory is declared under. Neither declaration is wrong on its own, and no rename of the *bare* name
fixes it, so the refusal has to say which two things met and what to do — the quoted name is one the
author wrote no part of.

⛔ **THE REFUSAL'S OWN CAUSE MUST NOT PRE-EMPT IT.** The parse reads the method's signature entry long
before merge reports the duplicate, so a refile that OVERWROTE the method's return type with the free
function's would type `p` in `let p = Point.create()` as `int` and blame a correct line with an E2015
field-access refusal. The refile declines a key another declaration owns
(`ProgramSignatures.refileContestedFreeFunction`), so the call types correctly and this is the only
diagnostic any variant produces.

⚠ **`app/main.maxon` IS DECLARED FIRST, AND THAT IS THE ASSERTION, NOT A LAYOUT PREFERENCE.**
`commitFuncSignatures` reports the SECOND declaration to claim a key, so which of the two colliders the
refusal points at is decided by the order the files are compiled in. Blaming the METHOD would tell its
author to rename a directory the method knows nothing about. Declaring the type's file first states the order that makes the refusal name the declaration its advice
is about.
```maxon
// --- file: app/main.maxon
typealias Integer = int(0 to 125)

export type Point
	export var x as Integer

	export static function create() returns Self
		return Self{x: 9}
	end 'create'
end 'Point'

function main() returns ExitCode
	let p = Point.create()
	return p.x
end 'main'

// --- file: Point/p.maxon
export typealias Integer = int(0 to 125)

export function create() returns Integer
	return 3
end 'create'

// --- file: other/o.maxon
export typealias Integer = int(0 to 125)

export function create() returns Integer
	return 5
end 'create'
```
```maxoncstderr
error E3006: Point/specs/fragments/namespace-qualified-resolution/error.contested-free-function-in-a-directory-named-after-a-type.test:21:17: duplicate definition of function 'Point.create' — a free function of that bare name is declared in more than one DIRECTORY, so each is registered under its directory-qualified spelling, and that spelling is already the mangled name of a method. Rename the directory, or rename the function
```


<!-- test: error.contested-free-function-collides-with-a-fieldless-method -->
**THE VARIANT WITH NO WITNESS.** The case above produces a visible symptom only because the
caller touches a FIELD. Here the method returns a plain `Integer`, so nothing downstream would ever
notice which of the two `Point.create`s it reached — this is the shape that would have to be a
silent wrong answer if the duplicate check were the thing at fault. It is not: `commitFuncSignatures`
sees both declarations claim one key whatever they return, and refuses. Pinned so that the claim
"nothing downstream notices" is tested rather than assumed.

⚠ **`app/main.maxon` IS DECLARED FIRST, AND THAT IS THE ASSERTION, NOT A LAYOUT PREFERENCE.**
`commitFuncSignatures` reports the SECOND declaration to claim a key, so which of the two colliders the
refusal points at is decided by the order the files are compiled in. Blaming the METHOD would tell its
author to rename a directory the method knows nothing about. Declaring the type's file first states the order that makes the refusal name the declaration its advice
is about.
```maxon
// --- file: app/main.maxon
export typealias Integer = int(0 to 125)

export type Point
	export var x as Integer

	export static function create() returns Integer
		return 9
	end 'create'
end 'Point'

function main() returns ExitCode
	return Point.create()
end 'main'

// --- file: Point/p.maxon
export typealias Integer = int(0 to 125)

export function create() returns Integer
	return 3
end 'create'

// --- file: other/o.maxon
export typealias Integer = int(0 to 125)

export function create() returns Integer
	return 5
end 'create'
```
```maxoncstderr
error E3006: Point/specs/fragments/namespace-qualified-resolution/error.contested-free-function-collides-with-a-fieldless-method.test:20:17: duplicate definition of function 'Point.create' — a free function of that bare name is declared in more than one DIRECTORY, so each is registered under its directory-qualified spelling, and that spelling is already the mangled name of a method. Rename the directory, or rename the function
```


<!-- test: contested-free-function-in-a-type-named-directory-that-collides-with-nothing -->
**THE REFUSAL ABOVE MUST NOT BE A RULE ABOUT DIRECTORY NAMES.** `Point/` is still named after a type
and its `helper` is still contested with `other/`'s — but `type Point` declares no `helper`, so
`Point.helper` collides with nothing and both spellings resolve: the type's `create` through the
static-call door, the directory's `helper` through the namespace door, in one expression. 9*10+3.
```maxon
// --- file: Point/p.maxon
export typealias Integer = int(0 to 125)

export function helper() returns Integer
	return 3
end 'helper'

// --- file: other/o.maxon
public typealias Integer = int(0 to 125)

public function helper() returns Integer
	return 5
end 'helper'

// --- file: app/main.maxon
export typealias Integer = int(0 to 125)

public type Point
	export var x as Integer

	export static function create() returns Integer
		return 9
	end 'create'
end 'Point'

function main() returns ExitCode
	return (Point.create() * 10 + Point.helper()) as ExitCode
end 'main'
```
```exitcode
93
```


<!-- test: uncontested-free-function-in-a-type-named-directory -->
The same shape with NO contest at all — one declaration of `helper`, in a directory named after a
type. It keeps its bare registration name and never goes near the `Type.method` space, so this is the
case the guard must not be able to reach however the contest logic changes.
```maxon
// --- file: Point/p.maxon
export typealias Integer = int(0 to 125)

export function helper() returns Integer
	return 3
end 'helper'

// --- file: app/main.maxon
export typealias Integer = int(0 to 125)

public type Point
	export var x as Integer

	export static function create() returns Integer
		return 9
	end 'create'
end 'Point'

function main() returns ExitCode
	return (Point.create() * 10 + Point.helper()) as ExitCode
end 'main'
```
```exitcode
93
```



<!-- test: error.three-way-ambiguous-bare-call -->
E3095's candidate list with THREE competitors, declared in an order the sorted output does not
preserve (`zulu`, `alpha`, `mid`). The list is rendered lexicographically because the compiler's `Map` is
open-addressed and its iteration is SLOT order — a function of two file paths' hashes — so an
unsorted list would reorder itself on a rename, on table growth, or on a host whose paths hash
differently, against a message pinned by this golden. Sorting is what makes the message reproducible.
```maxon
// --- file: zulu/f.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 1
end 'pick'

// --- file: alpha/f.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 2
end 'pick'

// --- file: mid/f.maxon
export typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 4
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return pick()
end 'main'
```
```maxoncstderr
error E3095: app/specs/fragments/namespace-qualified-resolution/error.three-way-ambiguous-bare-call.test:25:9: Ambiguous bare-name call to 'pick': more than one visible declaration matches it. Qualify it as one of: alpha.pick, mid.pick, zulu.pick
```


<!-- test: error.contested-free-function-default-is-not-inherited -->
⛔⛔ **A CONTESTANT'S PARAMETER DEFAULT BELONGS TO ITS OWN DECLARATION, NEVER TO WHICHEVER FILE FOLDED
FIRST.** The end-of-fold SELF refile moves this file's declaration
off the bare key — but the bare key ACCUMULATES, so a positive fact sitting there may be an earlier
directory's. `beta.pick` declares no default and takes one required argument; `alpha.pick`, folded
first, declares one.

⛔ Borrowing it would compile this program and answer **5** from `beta.pick()` — alpha's default
value, supplied to beta's parameter, in a file that declares no default at all. The control is the
byte-identical program with alpha's `= 5` removed, which reports exactly the refusal below. **The only variable is a DIFFERENT directory's declaration.**
```maxon
// --- file: alpha/a.maxon
export typealias Ms = int(0 to 125)

export function pick(ms Ms = 5) returns Ms
	return ms
end 'pick'

// --- file: beta/b.maxon
export typealias Slot = int(0 to 125)

export function pick(slot Slot) returns Slot
	return slot
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (alpha.pick() * 10) as ExitCode + (beta.pick() as ExitCode)
end 'main'
```
```maxoncstderr
error E3036: app/specs/fragments/namespace-qualified-resolution/error.contested-free-function-default-is-not-inherited.test:18:49: 'beta.pick' expects 1 argument(s) but 0 were provided
```


<!-- test: error.a-root-contestant-does-not-inherit-a-subdirectorys-default -->
⛔⛔ **THE CASE ABOVE WITH THE SECOND CONTESTANT AT THE *ROOT*, AND THAT IS A DIFFERENT KEY.**
Above, `beta/` has a qualified key of its own and never looks at
the bare name, so leaving a stale fact there is harmless. A ROOT declaration has no qualified spelling:
the bare `pick` IS its registration key. So the incumbent's default, filed under that same bare name by a
fold that could not yet know, sits exactly where the root declaration is about to read from — and the root
states no default, so nothing of its own overwrites it.

⛔ **What keeps them apart is `ProgramSignatures.clearByNameSweepEntries`, and this case is its gate.**
Without its one call this program would **compile and answer 13** — `pick(2)` silently filling `b` from
`alpha/`'s default, on a declaration that declares none. The order matters and only one of the two shows it: with the root file folded FIRST the
incumbent is the root's own declaration and there is nothing stale to inherit.
```maxon
// --- file: alpha/a.maxon
export typealias Num = int(-1000 to 1000)

export function pick(a Num, b Num = 5) returns Num
	return a + b
end 'pick'

// --- file: rootpick.maxon
export typealias Small = int(-1000 to 1000)

export function pick(a Small, b Small) returns Small
	return a + b
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (pick(2) as ExitCode) + (alpha.pick(1) as ExitCode)
end 'main'
```
```maxoncstderr
error E3036: app/<fragment>:18:10: 'pick' expects 2 argument(s) but 1 were provided
```


<!-- test: error.a-root-contestant-does-not-inherit-a-subdirectorys-throws -->
⛔ **THE SAME HOLE ON THE `throws` REGISTRY.** `error.contested-free-function-throws-is-not-inherited`
below is this program with the second contestant in `beta/` instead of at the root — and that one passes
whether or not the by-name clear runs, for the reason above. Without the clear this program would
**compile and answer 5**, a `try` accepted over a root `pick` that declares no `throws`, where the
correct answer is the E3055 below. Its twin fold order (root file first) refuses either way.
```maxon
// --- file: alpha/a.maxon
export enum Boom
	bad
end 'Boom'

export function pick() returns Integer throws Boom
	throw Boom.bad
end 'pick'

export typealias Integer = int(i64.min to i64.max)
// --- file: rootpick.maxon
export function pick() returns Integer
	return 5
end 'pick'

export typealias Integer = int(i64.min to i64.max)
// --- file: app/main.maxon
function main() returns ExitCode
	let v = try pick() otherwise 0
	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3055: app/<fragment>:20:10: try requires a throwing function: 'pick' does not throw'
```


<!-- test: contested-free-function-own-default-still-applies -->
The positive half of the case above, and the reason the cure is a SOURCE test rather than a clear of
the bare key: alpha's own default must still reach alpha's own qualified key. Only one of the two
contestants declares a default here, which is precisely the asymmetry the refusal above rests on —
so a cure that stopped copying defaults altogether would turn this green case red.
```maxon
// --- file: alpha/a.maxon
export typealias Ms = int(0 to 125)

export function pick(ms Ms = 5) returns Ms
	return ms
end 'pick'

// --- file: beta/b.maxon
export typealias Slot = int(0 to 125)

export function pick(slot Slot) returns Slot
	return slot
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (alpha.pick() * 10) as ExitCode + (beta.pick(3) as ExitCode)
end 'main'
```
```exitcode
53
```


<!-- test: error.contested-free-function-throws-is-not-inherited -->
**THE SAME BORROWED FACT ON THE `throws` REGISTRY.** The six
facts the SELF refile moves travel together, so the `throws` clause could be inherited exactly as the
parameter default could — and it IS read: `Parser.requireThrowingNamedTryTarget` asks
`ProgramSignatures.throwsOf(callee)` to refuse a `try` on a callee that cannot throw.

⛔ Inherited, it would let `try beta.pick() otherwise 0` compile clean, against a `beta.pick` whose
declaration has no `throws` clause. The control — the same program with alpha's `throws Boom`
removed — reports exactly the refusal below. *(E3057, the other direction, is NOT affected:
`SemanticCheck.buildThrowsMap` rebuilds its map from the checked IR functions rather than from the
sweep, so a call that omits a needed `try` is never decided by this registry.)*
```maxon
// --- file: alpha/a.maxon
export enum Boom
	case bad
end 'Boom'

export function pick() returns Integer throws Boom
	throw Boom.bad
end 'pick'

export typealias Integer = int(i64.min to i64.max)
// --- file: beta/b.maxon
export function pick() returns Integer
	return 5
end 'pick'

export typealias Integer = int(i64.min to i64.max)
// --- file: app/main.maxon
function main() returns ExitCode
	let v = try beta.pick() otherwise 0
	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3055: app/specs/fragments/namespace-qualified-resolution/error.contested-free-function-throws-is-not-inherited.test:20:10: try requires a throwing function: 'beta.pick' does not throw'
```


<!-- test: contested-free-function-caller-location-slots-are-not-renamed -->
**A CONTESTED FUNCTION'S CALLER-LOCATION SLOTS ARE SKIPPED BY THE RENAME, AND ITS HELPER SLOT IS NOT.**
The sibling case above pins that a contested declaration's synthesized default helpers follow it onto
its directory-qualified registration name. There is a SECOND kind of default —
`__file__` / `__line__`, which synthesize nothing and are materialized at the caller — so
`renameParamDefaultHelpers` walks a column with four answers and must move exactly one of them.

A caller-location slot has no synthesized function anywhere to rename. Renamed anyway, the four
column moves each find nothing and `ParamDefaultInfo.renameHelper` INVENTS a helper at that slot that
the drain never declared: the call site then emits a `call` to `__paramDefault#alpha.note#2`, a symbol
no file produced. Skipped along with the undefaulted slots, the caller supplies the constant itself.

This is the combination nothing else runs. `source-location-defaults.md` is flat — every file of it
sits at the compile root, so `registrationName` equals `bareName` and the rename never fires — and the
sibling case here carries only an ordinary helper default, so neither reaches a caller-location arm.

3/5 again, so an aliased pair cannot pass by coincidence: the LEVELS prove each helper followed its own
declaration, while `__file__`/`__line__` prove the two skipped slots still answer for the CALLER —
`app/main.maxon` when main calls, each directory's own file when the sibling inside it does.
```maxon
// --- file: alpha/a.maxon
export typealias Severity = int(0 to 9)

export function note(tag String, level Severity = 3, file String = __file__, at SourceLineNumber = __line__) returns SourceLineNumber
	print("{tag} lvl={level} {file}:{at}\n")
	return at
end 'note'

export function fromAlpha() returns SourceLineNumber
	return note("inAlpha")
end 'fromAlpha'

// --- file: beta/b.maxon
export typealias Severity = int(0 to 9)

export function note(tag String, level Severity = 5, file String = __file__, at SourceLineNumber = __line__) returns SourceLineNumber
	print("{tag} lvl={level} {file}:{at}\n")
	return at
end 'note'

export function fromBeta() returns SourceLineNumber
	return note("inBeta")
end 'fromBeta'

// --- file: app/main.maxon
function main() returns ExitCode
	let a = alpha.note("a")
	let b = beta.note("b")
	let c = alpha.note("c", level: 8)
	let ia = alpha.fromAlpha()
	let ib = beta.fromBeta()
	print("sum={a}:{b}:{c}:{ia}:{ib}\n")
	return (a * 10 + b) as ExitCode
end 'main'
```
```exitcode
23
```
```stdout
a lvl=3 app/main.maxon:2
b lvl=5 app/main.maxon:3
c lvl=8 app/main.maxon:4
inAlpha lvl=3 alpha/a.maxon:9
inBeta lvl=5 beta/b.maxon:9
sum=2:3:4:9:9
```

<!-- test: error.two-directories-function-aliases-of-one-shape-do-not-interchange -->
**A DIRECTORY-QUALIFIED FUNCTION ALIAS IS A BRAND.** `api.Score` and `legacy.Score` are file-scope
declarations wearing their directory, and an author can write either in a type position — so the
nominal rule of `nominal-function-alias.md` applies to them exactly as it does to a bare `Handler` and
`Callback`. The qualifier is not what makes a name brandless; being unwritable outside its type is, and
these are writable.
```maxon
// --- file: api/types.maxon
typealias Integer = int(i64.min to i64.max)
export typealias Score = function(n Integer) returns Integer

// --- file: legacy/types.maxon
typealias Integer = int(i64.min to i64.max)
export typealias Score = function(n Integer) returns Integer

// --- file: app/main.maxon
typealias Integer = int(i64.min to i64.max)

function addOne(n Integer) returns Integer
	return n + 1
end 'addOne'

function pickLegacy() returns legacy.Score
	return addOne
end 'pickLegacy'

function runApi(f api.Score) returns Integer
	return f(20)
end 'runApi'

function main() returns ExitCode
	let h = pickLegacy()
	print("{runApi(h)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: app/<fragment>:27:10: argument type mismatch for 'f': expected 'api.Score', got 'legacy.Score'
```

<!-- test: two-directories-export-one-alias-over-two-primitives-and-both-stand -->
Author against author across directories: the pair coexists and both qualified spellings work (50 + 1).
```maxon
// --- file: api/t.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/t.maxon
export typealias Score = float(0.0 to 1.0)

// --- file: app/main.maxon
function main() returns ExitCode
	let a = 50 as api.Score
	let f = 0.5 as legacy.Score
	return (a + (1 if f > 0.25 else 0)) as ExitCode
end 'main'
```
```exitcode
51
```

<!-- test: the-librarys-supplied-aliases-are-named-through-stdlib -->
`Codepoint`, `ElementIndex` and `BytePos` are the library's public aliases. Each `stdlib.` spelling is the
same type a library value carries, and the bare spelling in the same file names the same declaration
(70 + 108 - 100 + 2).
```maxon
function main() returns ExitCode
	let c = 70 as stdlib.Codepoint
	let i = 2 as stdlib.ElementIndex
	let p = 3 as stdlib.BytePos
	let bytes = "hello".toByteArray()
	let b = try bytes.get(p) otherwise 0
	return (c + (b as Codepoint) - 100 + (i as Codepoint)) as ExitCode
end 'main'
```
```exitcode
80
```

<!-- test: error.a-module-and-an-exported-alias-in-one-directory-are-a-duplicate -->
Two NAMEABLE declarations of one name in one directory share one qualified spelling, so no reader could name
either: the pair is a duplicate at the second declaration, whatever tiers it was written at.
```maxon
// --- file: api/a.maxon
module typealias Score = int(0 to 100)

// --- file: api/b.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3061: api/<fragment>:6:18: Duplicate typealias 'Score'
```

<!-- test: a-file-private-and-an-exported-alias-in-one-directory-coexist -->
A file-private alias is reachable from its own file alone, so it is no second meaning for anyone else and the
pair in one directory is legal.
```maxon
// --- file: api/a.maxon
typealias Score = int(0 to 10)

export function small() returns ExitCode
	return 4 as Score
end 'small'

// --- file: api/b.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function main() returns ExitCode
	let s = 38 as Score
	return (s + (small() as Score)) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: error.a-root-export-beside-the-librarys-alias-names-the-root-as-export -->
A ROOT declaration's qualified spelling is `export.X`: the root namespace has no directory name, and `export`
is a reserved word, so no directory can ever be spelled as a qualifier head with it.
```maxon
// --- file: wide.maxon
export typealias Byte = int(0 to 1000)
// --- file: app/main.maxon
function main() returns ExitCode
	let b = 5 as Byte
	return b
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:6:15: Ambiguous type name 'Byte': more than one visible declaration matches it. Qualify it as one of: export.Byte, stdlib.Byte
```

<!-- test: the-root-namespace-is-named-by-export -->
`export.X` reaches a root declaration of every kind a directory qualifier reaches — an alias as a cast target
and an argument to a root function declared over the bare name, a type as a parameter and a static call's base,
and a free function called, called as a statement, and read as a value (5 + 3 + 3 + 1).
```maxon
// --- file: root.maxon
module type Point
	module var x as Integer

	module static function make() returns Point
		return Point{x: 5}
	end 'make'
end 'Point'

module function pick() returns Integer
	return 3
end 'pick'

module function shout()
	print("root")
end 'shout'

module typealias Integer = int(i64.min to i64.max)

module typealias Byte = int(0 to 1000)

module function widen(b Byte) returns Integer
	return b - 499
end 'widen'
// --- file: app/main.maxon
function take(p export.Point) returns ExitCode
	return p.x as ExitCode
end 'take'

function main() returns ExitCode
	let f = export.pick
	export.shout()
	return take(export.Point.make()) + (export.pick() as ExitCode) + (f() as ExitCode) + (widen(500 as export.Byte) as ExitCode)
end 'main'
```
```exitcode
12
```
```stdout
root
```

<!-- test: error.a-directory-named-export-cannot-be-a-namespace -->
A directory is a namespace, so its name must be one a qualified reference can spell. `export` already qualifies
the project root, so a directory named `export` is refused when the program is loaded, before any reference to
it could be offered a spelling that names the root instead.
```maxon
// --- file: r.maxon
module typealias Score = int(0 to 100)
// --- file: export/t.maxon
export typealias Score = int(0 to 10)
// --- file: app/main.maxon
function main() returns ExitCode
	let s = 50 as export.Score
	return s
end 'main'
```
```maxoncstderr
error E3182: Directory 'export' cannot be a namespace: 'export' already qualifies the project root. Rename the directory
```

<!-- test: error.a-directory-named-runtime-cannot-be-a-namespace -->
`runtime` names the runtime tier's directory, so an author's directory of that name is refused when the program
is loaded.
```maxon
// --- file: runtime/h.maxon
export typealias Score = int(0 to 100)
// --- file: app/main.maxon
function main() returns ExitCode
	let s = 5 as Score
	return s
end 'main'
```
```maxoncstderr
error E3182: Directory 'runtime' cannot be a namespace: 'runtime' already names the runtime tier. Rename the directory
```


<!-- test: a-qualified-type-reaches-its-declaration-past-the-readers-own-alias -->
A file's own declaration wins its bare name, and the qualified spelling still reaches the other directory's
declaration: `a.Foo` is the struct in every position — a parameter type, a static call's base and the field
read through the parameter — while `Foo` written bare in the same file is the file's own ranged alias.
```maxon
// --- file: a/foo.maxon
export type Foo
	export let v as ExitCode

	export static function make() returns Foo
		return Foo{v: 42}
	end 'make'
end 'Foo'

// --- file: app/main.maxon
typealias Foo = int(0 to 10)

function take(f a.Foo) returns ExitCode
	return f.v
end 'take'

function main() returns ExitCode
	let local = 3 as Foo
	return take(a.Foo.make()) + (local as ExitCode)
end 'main'
```
```exitcode
45
```

<!-- test: export-qualified-type-reaches-the-roots-declaration-past-the-readers-own-alias -->
The same rule through the root qualifier: `export.Foo` is the root module's struct, not the reading file's own
alias of that name.
```maxon
// --- file: r.maxon
module type Foo
	export let v as ExitCode

	module static function make() returns Foo
		return Foo{v: 42}
	end 'make'
end 'Foo'

// --- file: app/main.maxon
typealias Foo = int(0 to 10)

function take(f export.Foo) returns ExitCode
	return f.v
end 'take'

function main() returns ExitCode
	let local = 3 as Foo
	return take(export.Foo.make()) + (local as ExitCode)
end 'main'
```
```exitcode
45
```

<!-- test: a-qualified-enum-keeps-its-identity-where-the-files-own-alias-takes-its-name -->
A file's own typealias takes the bare name, and the qualified spelling still reaches the enum: the value
`a.Color.red` is the enum's, so `rawValue` reads it, while `Color` written bare in the same file is the alias.
```maxon
// --- file: a/c.maxon
export enum Color
	red = 40
	blue = 41
end 'Color'

// --- file: app/main.maxon
typealias Color = int(0 to 10)

function main() returns ExitCode
	let local = 3 as Color
	let d = a.Color.red
	return (d.rawValue - 38 + local) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-local-binding-named-like-a-directory-keeps-its-member-access -->
A local binding shadows a directory of the same name inside an expression: `api.Score` on a parameter named
`api` reads that value's `Score` field, while `api.Score.make()` written where no binding of that name exists
names the directory's type.
```maxon
// --- file: api/score.maxon
export type Score
	export let v as ExitCode

	export static function make() returns Score
		return Score{v: 30}
	end 'make'
end 'Score'

// --- file: app/main.maxon
type Panel
	let Score as ExitCode

	static function make() returns Panel
		return Panel{Score: 12}
	end 'make'

	function total(api Panel) returns ExitCode
		return api.Score + self.Score
	end 'total'
end 'Panel'

function main() returns ExitCode
	let p = Panel.make()
	return p.total(Panel.make()) + api.Score.make().v
end 'main'
```
```exitcode
54
```

<!-- test: an-argument-label-named-like-a-directory-does-not-shadow-it -->
A name that binds no value in the function, such as the argument label `api:`, leaves `api.Score` naming the
directory's type.
```maxon
// --- file: api/score.maxon
export type Score
	export let v as ExitCode

	export static function make() returns Score
		return Score{v: 30}
	end 'make'
end 'Score'

// --- file: app/main.maxon
typealias Score = int(0 to 10)

function scaled(by ExitCode, api ExitCode) returns ExitCode
	return by + api
end 'scaled'

function main() returns ExitCode
	let local = 4 as Score
	let base = scaled(1, api: 2)
	return base + api.Score.make().v + (local as ExitCode)
end 'main'
```
```exitcode
37
```

<!-- test: a-top-level-binding-named-like-a-directory-keeps-its-member-access -->
A top-level binding shadows a directory of the same name exactly as a local one does: in the file that declares
`api`, `api.Upper` reads the binding's field, while a file the binding is hidden from names the directory's type.
```maxon
// --- file: api/upper.maxon
export type Upper
	export let v as ExitCode

	export static function make() returns Upper
		return Upper{v: 30}
	end 'make'
end 'Upper'

// --- file: app/main.maxon
type Holder
	export let Upper as ExitCode

	static function make() returns Holder
		return Holder{Upper: 7}
	end 'make'
end 'Holder'

let api = Holder.make()

function main() returns ExitCode
	return api.Upper + seven()
end 'main'

// --- file: app/other.maxon
module function seven() returns ExitCode
	return api.Upper.make().v
end 'seven'
```
```exitcode
37
```

<!-- test: error.a-keyword-named-directory-cannot-be-a-namespace -->
A directory named with a keyword cannot begin a qualified name, so nothing in it could ever be named by
qualification; it is refused when the program is loaded. A keyword is still a legal LATER segment
(`lib/from/`).
```maxon
// --- file: from/h.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function main() returns ExitCode
	let s = 5 as Score
	return s
end 'main'
```
```maxoncstderr
error E3182: Directory 'from' cannot be a namespace: 'from' is a keyword, which cannot begin a qualified name. Rename the directory
```

<!-- test: error.a-directory-whose-name-is-not-a-name-cannot-be-a-namespace -->
A directory name that does not lex as one name cannot be written as a qualifier segment at all.
```maxon
// --- file: my-dir/h.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function main() returns ExitCode
	let s = 5 as Score
	return s
end 'main'
```
```maxoncstderr
error E3182: Directory 'my-dir' cannot be a namespace: 'my-dir' is not a name. Rename the directory
```

<!-- test: an-enum-returned-into-a-file-whose-own-alias-takes-its-name-stays-the-enum -->
A value keeps the type it was declared with when it crosses into a file whose own typealias shares the enum's
name: `a.pick()` hands back the enum, so its `rawValue` reads and it passes back to `a.rank` as the enum.
```maxon
// --- file: a/c.maxon
export enum Color
	red = 40
	blue = 41
end 'Color'

export function pick() returns Color
	return Color.blue
end 'pick'

export function rank(c Color) returns ExitCode
	return c.rawValue
end 'rank'

// --- file: app/main.maxon
typealias Color = int(0 to 10)

function main() returns ExitCode
	let local = 3 as Color
	let c = a.pick()
	return (c.rawValue - 38 + local) as ExitCode + a.rank(c) - 41
end 'main'
```
```exitcode
6
```

<!-- test: a-union-keeps-its-identity-where-the-files-own-alias-takes-its-name -->
The union twin: a parameter typed `a.Shape`, a value returned by `a.longest()` and the case `a.Shape.dot` are
all the union, matched on its cases, in a file whose own `Shape` is a ranged alias.
```maxon
// --- file: a/s.maxon
export union Shape
	dot
	line(length ExitCode)
end 'Shape'

export function longest() returns Shape
	return Shape.line(9)
end 'longest'

// --- file: app/main.maxon
typealias Shape = int(0 to 10)

function measure(s a.Shape) returns ExitCode
	match s 'kind'
		dot then return 1
		line(length) then return length
	end 'kind'
end 'measure'

function main() returns ExitCode
	let local = 2 as Shape
	return measure(a.longest()) + measure(a.Shape.dot) + (local as ExitCode)
end 'main'
```
```exitcode
12
```

<!-- test: one-alias-reached-by-its-bare-and-qualified-spellings-is-one-type -->
A typealias only one file declares is one type however it is spelled: `aaa.bump(3)` returns the alias as its
own file spells it and `5 as aaa.Small` names it through its directory, and the two add.
```maxon
// --- file: aaa/s.maxon
export typealias Small = int(0 to 1000)

export function bump(v Small) returns Small
	return v + 1
end 'bump'

// --- file: app/main.maxon
function main() returns ExitCode
	let x = 5 as aaa.Small
	return (aaa.bump(3) + x) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: an-authors-alias-reached-bare-and-qualified-beside-another-directorys-is-one-type -->
Two directories declare `Score`, and only one of them is visible to `a/t.maxon`, so that file's bare `Score` and
its `a.Score` name one declaration and are one type.
```maxon
// --- file: a/s.maxon
export typealias Score = int(0 to 100)

// --- file: a/t.maxon
export function mix() returns ExitCode
	let x = 5 as Score
	let y = 6 as a.Score
	return (x + y) as ExitCode
end 'mix'

// --- file: b/s.maxon
module typealias Score = int(0 to 10)

// --- file: app/main.maxon
function main() returns ExitCode
	return a.mix()
end 'main'
```
```exitcode
11
```

<!-- test: an-authors-enum-beside-the-librarys-element-index-leaves-the-alias-one-type -->
`ElementIndex` is the library's typealias, and an author's directory declares an enum of that name. The
alias keeps one identity: `xs.count()` returns it and `3 as stdlib.ElementIndex` names it, so the two add,
while `a.ElementIndex` reaches the enum.
```maxon
// --- file: a/e.maxon
export enum ElementIndex
	below = -5
	above = 7
end 'ElementIndex'

export function pick(up bool) returns ElementIndex
	return ElementIndex.above if up else ElementIndex.below
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	let xs = [1, 2]
	let b = a.pick(false)
	let c = a.ElementIndex.above
	let q = 3 as stdlib.ElementIndex
	return (b.rawValue + c.rawValue + 1 + xs.count() + q) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: an-authors-type-beside-the-librarys-byte-pos-leaves-the-alias-one-type -->
`BytePos` is a public typealias of `stdlib/String.maxon`. An author's
`type BytePos` beside it makes the same pair any other library alias makes with an author's type: the
author's file means its own type by the bare name, and the library's positions stay one alias.
```maxon
// --- file: app/main.maxon
type BytePos
	export let v as ExitCode

	static function make(v ExitCode) returns BytePos
		return Self{v: v}
	end 'make'
end 'BytePos'

function main() returns ExitCode
	let s = "abc"
	let start = 1 as stdlib.BytePos
	let stop = findGraphemeEnd(s, startPos: start)
	let back = findGraphemeStart(s, beforePos: stop)
	return (stop + back) as ExitCode + BytePos.make(4).v
end 'main'
```
```exitcode
7
```
