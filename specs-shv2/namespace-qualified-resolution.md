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
DISCRIMINATE, each case here having been found by probing the mechanism rather than by reading it:

- a qualified call is a FREE call, so it is legal in every position a bare call is — including a
  statement of its own, which is the only position a `void` function can be called from at all;
- a qualified alias resolves to the range of the declaration in THAT directory, not to whichever
  same-named declaration a bare lookup would have won with;
- the qualifier is a NAME LOOKUP and never a visibility bypass — `export`, `module` and file-private each
  answer at the qualified spelling exactly as they answer at the bare one;
- a `Type.method` reading of the same tokens always wins, so a directory may be named after a type
  without moving a single call;
- the CALL position and the TYPE position read one and the same namespace, segment for segment — a
  directory name is a filesystem name and owes the grammar nothing.

## Tests

<!-- test: namespace-qualified-void-call-statement -->
A qualified call written as a STATEMENT — the only position a `void` function can be called from.
Both segment counts are exercised, because they reach the parser through different lookaheads: the
statement door's bare-call arm tests `identifier (`, which a qualifier's `.` fails, so before this
was recognized a namespaced module could declare a void function that no file outside its own
directory could ever call.
```maxon
// --- file: lib/inner/deep.maxon
typealias Integer = int(0 to 125)

export function bump(v Integer) returns Integer
	return v + 1
end 'bump'

export function shout()
	print("shouted")
end 'shout'

// --- file: lib/top.maxon
typealias Integer = int(0 to 125)

export function twice(v Integer) returns Integer
	return v + v
end 'twice'

// --- file: app/main.maxon
function main() returns ExitCode
	lib.inner.shout()
	let v = lib.inner.bump(20)
	return lib.twice(v) as ExitCode
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
	return (wide + narrow) as ExitCode
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
	return b as ExitCode
end 'main'
```
```maxoncstderr
error E3005: app/specs/fragments/namespace-qualified-resolution/error.qualified-alias-out-of-its-own-range.test:10:13: Value 50 is outside the range of 'legacy.Score' (int(0 to 10))
```


<!-- test: qualified-alias-in-signature-positions -->
A qualified alias is a TYPE, so it is legal wherever a type name is — a parameter and a return
clause, not only an `as` cast target. The call in the same program is qualified through a
multi-segment directory chain AND names a function declared with a keyword (`from`), which the
declaration rules admit and which a qualifier walk that demanded plain identifiers would have made
unreachable.
```maxon
// --- file: lib/inner/deep.maxon
typealias Integer = int(0 to 125)

export function from(v Integer) returns Integer
	return v + 1
end 'from'

// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function take(v api.Score) returns api.Score
	return v
end 'take'

function main() returns ExitCode
	let a = take(41 as api.Score)
	return lib.inner.from(a) as ExitCode
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
typealias Integer = int(0 to 125)

export function make() returns Integer
	return 11
end 'make'

// --- file: app/main.maxon
typealias Integer = int(0 to 125)

export type Point
	export var x as Integer

	export static function make() returns Integer
		return 22
	end 'make'
end 'Point'

function main() returns ExitCode
	return Point.make() as ExitCode
end 'main'
```
```exitcode
22
```


<!-- test: keyword-named-directory-segment-resolves-in-both-positions -->
A namespace segment whose name is a KEYWORD (`lib/from/`). A directory name is a FILESYSTEM name and owes
the grammar nothing, so the two positions that walk a dotted chain — the call door and the type door —
must admit the same segments. They did not: they were two separate walks, one asking `tokenCanBeAName`
and one demanding a plain identifier, so `lib.from.helper()` compiled and ran while `5 as lib.from.Score`
was refused `E3011: Unknown type 'lib'` — a wrong rejection quoting a fragment of the name the author
wrote, for a declaration filed under exactly that key. Both spellings appear here, in one program.
```maxon
// --- file: lib/from/h.maxon
typealias Integer = int(0 to 125)

export typealias Score = int(0 to 100)

export function helper() returns Integer
	return 7
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	let s = 5 as lib.from.Score
	return (lib.from.helper() + (s as int)) as ExitCode
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
typealias Integer = int(0 to 125)

function hiddenHelper() returns Integer
	return 7
end 'hiddenHelper'

export function seen() returns Integer
	return hiddenHelper()
end 'seen'

// --- file: app/main.maxon
function main() returns ExitCode
	return utils.hiddenHelper() as ExitCode
end 'main'
```
```maxoncstderr
error E3008: app/specs/fragments/namespace-qualified-resolution/error.qualified-call-to-non-exported-function.test:15:9: function 'hiddenHelper' is not exported
```


<!-- test: error.multi-segment-qualified-call-to-module-scoped-function -->
The same rule through a multi-segment qualifier and the middle tier: a `module` declaration named
from outside its directory subtree is refused with the tier's own diagnostic.
```maxon
// --- file: lib/inner/deep.maxon
typealias Integer = int(0 to 125)

module function scopedHelper() returns Integer
	return 7
end 'scopedHelper'

export function seen() returns Integer
	return scopedHelper()
end 'seen'

// --- file: app/main.maxon
function main() returns ExitCode
	return lib.inner.scopedHelper() as ExitCode
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

export function seed() returns Level
	return 21
end 'seed'

// --- file: app/main.maxon
function main() returns ExitCode
	let x = 21 as feature.Level
	return x as ExitCode
end 'main'
```
```maxoncstderr
error E2003: app/specs/fragments/namespace-qualified-resolution/error.module-tier-alias-is-not-nameable-outside-its-subtree.test:11:16: Expected type name after 'as'
```


<!-- test: error.same-directory-underlying-conflict-is-reported-once -->
Two files in ONE directory export a `Score` over different underlying primitives. The pair is one
mistake and earns exactly one diagnostic, named against the declaration the author wrote — a
directory-qualified declaration is filed under its qualified spelling as well, and that second entry
must not report the conflict a second time under a name no file contains.
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
error E3105: api/specs/fragments/namespace-qualified-resolution/error.same-directory-underlying-conflict-is-reported-once.test:6:18: Typealias 'Score' is declared over 'float' here and over 'int' in another file — two files may declare one alias name over different RANGES, but not over different underlying types
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
typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 3
end 'pick'

// --- file: beta/b.maxon
typealias Integer = int(0 to 125)

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
typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 3
end 'pick'

export function localCaller() returns Integer
	return pick()
end 'localCaller'

// --- file: beta/b.maxon
typealias Integer = int(0 to 125)

export function pick() returns Integer
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
```maxon
// --- file: dir/a.maxon
typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 3
end 'pick'

// --- file: dir/b.maxon
typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 5
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return dir.pick() as ExitCode
end 'main'
```
```maxoncstderr
error E3006: dir/specs/fragments/namespace-qualified-resolution/error.free-function-pair-in-one-directory-still-collides.test:12:17: duplicate definition of function 'pick'
```


<!-- test: error.flat-root-level-free-function-pair-still-collides -->
The same boundary from the other side, and the one every pre-existing cross-file collision case in
this suite stands on: ROOT is a directory — the global namespace — so two root-level files declaring
`pick` share a namespace and collide exactly as they always did. A relaxation that keyed the contest
on "different FILE" rather than "different DIRECTORY" would un-collide the whole of
`type-name-collision.md` and `stdlib-user-shadows.md`, whose fixtures are all flat and root-level.
```maxon
// --- file: a.maxon
typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 3
end 'pick'

// --- file: b.maxon
typealias Integer = int(0 to 125)

export function pick() returns Integer
	return 5
end 'pick'

// --- file: main.maxon
function main() returns ExitCode
	return pick() as ExitCode
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/namespace-qualified-resolution/error.flat-root-level-free-function-pair-still-collides.test:12:17: duplicate definition of function 'pick'
```


<!-- test: error.two-main-declarations-in-different-directories-still-collide -->
`main` is the ONE free function never qualified by its directory, in both this compiler and the
self-hosted reference: the entry point is one name for the whole program. Qualifying it would turn a
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
error E3006: beta/specs/fragments/namespace-qualified-resolution/error.two-main-declarations-in-different-directories-still-collide.test:8:10: duplicate definition of function 'main'
```


<!-- test: same-directory-alias-pair-is-not-offered-as-its-own-disambiguation -->
The edge E3063's candidate list must not lie about. Two files in ONE directory legally export
`Score` over two RANGES (a cross-file ranged pair is legal; only a cross-file PRIMITIVE pair is
refused, above). A third file's bare `Score` therefore has two reachable declarations that render
the SAME candidate — and `api.Score` is contested between exactly those two files, so offering it
as the fix would be offering a fix that changes nothing.

The candidate list is a SET, so the two collapse to one, no ambiguity is claimed, and resolution
proceeds as it did before directory-as-module named the collision. `120` is in range for the wider
declaration and out of range for the narrower, so this case also RECORDS which one a stranger
resolves to rather than leaving it unstated.
```maxon
// --- file: api/a.maxon
export typealias Score = int(0 to 100)

// --- file: api/b.maxon
export typealias Score = int(0 to 200)

// --- file: app/main.maxon
function main() returns ExitCode
	let x = 120 as Score
	return x as ExitCode
end 'main'
```
```exitcode
120
```
