---
feature: cast-target-type-resolution
status: stable
keywords: cast, as, type, resolution, E3011, E3009, typealias
category: type-system
---
# An `as` Cast Target Is Resolved By The Same Authority Every Other Type Reference Is

## Documentation

`Parser.parseTypeReference` answers a handful of type spellings SYNTACTICALLY — `Self`, the
enclosing type's own name, `String`, a type parameter, a generic-instance alias, a function
alias — and every one of those reaches `applyCast` already concrete, so a cast to one of them is
checked against `castHasNoLegalConversion` and rejected when the kinds cannot convert
(`5 as String` is E3009). **Everything else stays `MaxonType.named`, a bare interned name that
is not yet a type at all** — and `maxonTypeTag` reads every one of them as a ranged int.

So the cast's legality gate was asking about a name instead of about a type, and a `named` target
fell through to the representational no-op at the bottom of `applyCast`. **Four different
programs evaporated identically:**

| `5 as X` where `X` is | was | is |
|---|---|---|
| a name NO declaration binds | compiled, cast vanished | **E3011**, at the `as` |
| a misspelling of a real alias | compiled, cast vanished | **E3011**, at the `as` |
| a declared `type` (struct) | compiled, cast vanished | **E3009**, at the `as` |
| a declared ranged typealias | resolved | resolved (unchanged) |

The cure is not a second cascade in the parser. `TypeResolution` already owns the one list of
sources a named type can come from — the `ExitCode` builtin, the compiler's synthesized int
aliases, declared structs, declared enums/unions, plain ranged typealiases, qualified
(inner / per-instance) aliases — and that list is now `denotedNamedType`, asked by BOTH the
resolution pass (whose `unknown` verdict IS its E3011) and by the two places a cast target is
written. One list; the parser and the authority cannot come to disagree about what a name means.

**The struct row is the one that was already claimed.** `applyCast`'s own header said it rejects
"a managed aggregate … cast to or from a scalar number (`"x" as Age`, `p as Age`, `f as Age`,
`n as SomeStructAlias`)" — and it did, for every spelling that arrived concrete. `n as Point`
spelled from OUTSIDE `type Point` arrived `named`, so the sentence was false for exactly the
spelling a user is most likely to write. `Self` and the type's own name (which `parseTypeReference`
mints as `structRef`) were already rejected; now the third spelling of the same type agrees.

⚠ **A cast target is written in TWO places, and both are covered.** A body cast goes through
`Parser.applyCast`; a top-level `let X = 5 as T` is folded by the const evaluator
(`Parser.applyConstCast`), which runs inside `queryProgramSignatures.evaluateInitializers` and
never reaches the parser's expression path at all. Fixing only the first would have left
`let SENTINEL = 5 as Bogus` silently evaporating — half a fact, which is worse than none because
the half that works makes the other half look tested.

## What an enum target still does, and why that is NOT this rung

`5 as Color` for a declared `enum`/`union` is still accepted, because shv2 ERASES an enum to
`integer` (`resolveNamedType`'s enum arm) — so the cast is `5 as int`, a legal, pointless numeric
no-op, exactly like `5 as SomeIntAlias`. The bootstrap refuses it (`E2003`, "Expected type name
after 'as'"), but that is a consequence of ITS `as` parser accepting a fixed list of type FORMS,
not of a rule about what an int may become. Whether Maxon permits `int` ↔ `enum` casts is a
LANGUAGE question and is deliberately left where it was.

## Tests

<!-- test: error.cast-to-undeclared-type -->
The rung's own case. The name binds nothing anywhere in the program, so there is no type for the
cast to convert to and the cast is refused where it is written.
```maxon
function main() returns ExitCode
	let n = 5 as CompletelyMadeUpNameXyz
	return n
end 'main'
```
```maxoncstderr
error E3011: specs/fragments/cast-target-type-resolution/error.cast-to-undeclared-type.test:3:12: Unknown type 'CompletelyMadeUpNameXyz'
```

<!-- test: error.cast-to-misspelled-alias -->
A one-character slip on a name the same file declares. This is the case the check actually earns
its keep on: a wild name is rare, a typo is not, and before this the typo compiled to a program
whose narrowing check simply never ran.
```maxon
typealias Score = int(0 to 100)

function main() returns ExitCode
	let n = 5 as Scor
	return n as ExitCode
end 'main'
```
```maxoncstderr
error E3011: specs/fragments/cast-target-type-resolution/error.cast-to-misspelled-alias.test:5:12: Unknown type 'Scor'
```

<!-- test: error.top-level-const-cast-to-undeclared-type -->
THE SECOND CAST SITE. A top-level `let`'s initializer is folded by the const evaluator, which is
a different walk over different code — `Parser.applyConstCast`, driven from
`ProgramSignatures.evaluateInitializers` before any file is parsed. It asks the same authority
and is anchored on the same token, so the two spellings of one mistake report identically.
```maxon
let SENTINEL = 5 as CompletelyMadeUpNameXyz

function main() returns ExitCode
	return SENTINEL as ExitCode
end 'main'
```
```maxoncstderr
error E3011: specs/fragments/cast-target-type-resolution/error.top-level-const-cast-to-undeclared-type.test:2:18: Unknown type 'CompletelyMadeUpNameXyz'
```

<!-- test: error.cast-to-struct-type -->
A declared `type` named from OUTSIDE its own body. `Self` and the type's own name inside the body
already reported this (`parseTypeReference` mints them `structRef`); the third spelling of the
same type now agrees, with the same message.
```maxon
typealias Coord = int(0 to 100)

type Point
	export var x as Coord
end 'Point'

function main() returns ExitCode
	let n = 5 as Point
	return n as ExitCode
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/cast-target-type-resolution/error.cast-to-struct-type.test:9:12: Cannot cast from int to struct
```

<!-- test: cast-to-alias-declared-later -->
A cast target is resolved against the WHOLE-PROGRAM declaration index, which is swept from every
file's tokens before any file is parsed — so a forward reference is not a special case, it is the
ordinary one. A file-local check would reject this program, which is why the check is not one.
```maxon
function main() returns ExitCode
	let n = 7 as Later
	return n
end 'main'

typealias Later = int(0 to 100)
```
```exitcode
7
```

<!-- test: cast-to-loaded-stdlib-internal-typealias -->
⭐ **A TYPEALIAS DECLARED INSIDE THE STDLIB IS NAMEABLE AS A CAST TARGET FROM ANY FILE, WHATEVER
ITS VISIBILITY MODIFIER.** `stdlib/Sleep.maxon` declares `typealias Milliseconds = int(0 to
i64.max)` with no `export`, and `RangedAliasRegistry`'s bare-name fallback is what makes it
reachable here; that fallback exists FOR this property and its header names this test.

⚠ **READ THE NEXT TEST WITH THIS ONE — ON ITS OWN THIS PROGRAM PROVES NOTHING, AND THAT IS
EXACTLY HOW ITS PREDECESSOR CAME TO PASS FOR THE WRONG REASON.** The version of this case ported
from `/specs` named `ElementIndex`, which lives in `stdlib/Array.maxon` — a module the loader
did not then load. The name resolved to nothing, the cast evaporated, and the exit code
came out right anyway. It was green because the lookup FAILED. (All of `stdlib/` loads now, so that
particular lookup would succeed today; what the paragraph records is why the case was rewritten.)

An exit code cannot tell the two apart, and for the CONSTANT operand below neither can a golden:
the value folds and a folded in-range cast emits nothing either way.

⚠ **THE ARGUMENT THIS PARAGRAPH USED TO MAKE IS RETIRED, AND ONLY BECAUSE THE STDLIB CHANGED
UNDER IT.** It read: *"MEASURED — with a runtime (non-constant) operand, `x as Milliseconds` and
`x as NoSuchName` emit BYTE-IDENTICAL Target IR, because every stdlib alias this loader lists is
`int(0 to u64.max)`, a range that admits every 64-bit value and therefore emits no guard."* Every
one of those aliases has since been narrowed to its honest upper bound — `Milliseconds` is
`int(0 to i64.max)` — so a runtime operand now DOES emit a guard and a behavioural discriminator
is available where there was none. The measurement was true when it was taken; what made it stop
being true is `stdlib/Internals.maxon`'s roster, which now names the only three aliases still
full-range and why.

What discriminates HERE is still the DIAGNOSTIC, and it is the stronger half in any case: a guard
distinguishes a name that resolved to a NARROW range from one that resolved to nothing, while the
pair below distinguishes a name that resolved AT ALL — which is the property this test is about,
and the one that would survive `Milliseconds` being re-widened. It works only because of the rung
this file documents: a cast
target that denotes nothing is now a hard error. So the pair below IS the discrimination — two
programs identical but for ONE CHARACTER in the name — and if `Milliseconds` ever stopped
resolving (the module removed from `stdlib/`, the bare-name fallback narrowed, the alias
renamed) this test would produce the next test's output and FAIL. Do not delete the negative
half; it is what makes the positive half mean something.
```maxon
function main() returns ExitCode
	let n = 5 as Milliseconds
	return n as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.cast-to-misspelled-stdlib-typealias -->
The negative half of the pair above: `Millisecond` (singular) is not declared anywhere, and the
program is otherwise character-for-character the same.
```maxon
function main() returns ExitCode
	let n = 5 as Millisecond
	return n as ExitCode
end 'main'
```
```maxoncstderr
error E3011: specs/fragments/cast-target-type-resolution/error.cast-to-misspelled-stdlib-typealias.test:3:12: Unknown type 'Millisecond'
```
