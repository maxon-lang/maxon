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

So the cast's legality gate must ask about a TYPE rather than a name: a `named` target left unresolved
would fall through to the representational no-op at the bottom of `applyCast`, and the cast would
evaporate. **A named cast target is resolved, and each kind answers:**

| `5 as X` where `X` is | answer |
|---|---|
| a name NO declaration binds | **E3011**, at the `as` |
| a misspelling of a real alias | **E3011**, at the `as` |
| a declared `type` (struct) | **E3009**, at the `as` |
| a declared ranged typealias | resolved |

There is no second cascade in the parser. `TypeResolution` owns the one list of sources a named type can
come from — the `ExitCode` builtin, the compiler's synthesized int aliases, declared structs, declared
enums/unions, plain ranged typealiases, qualified (inner / per-instance) aliases — and that list is
`denotedNamedType`, asked by BOTH the resolution pass (whose `unknown` verdict IS its E3011) and by the two
places a cast target is written. One list; the parser and the authority cannot come to disagree about what
a name means.

**The struct row is what `applyCast`'s own header claims.** It rejects "a managed aggregate … cast to or
from a scalar number (`"x" as Age`, `p as Age`, `f as Age`, `n as SomeStructAlias`)", and that holds for
every spelling of the type: `Self`, the type's own name (which `parseTypeReference` mints as `structRef`),
and `n as Point` spelled from OUTSIDE `type Point` — the spelling a user is most likely to write, which
arrives `named` and is resolved.

⚠ **A cast target is written in TWO places, and both are covered.** A body cast goes through
`Parser.applyCast`; a top-level `let X = 5 as T` is folded by the const evaluator
(`Parser.applyConstCast`), which runs inside `queryProgramSignatures.evaluateInitializers` and
never reaches the parser's expression path at all. Covering only the first would leave
`let SENTINEL = 5 as Bogus` silently evaporating — half a fact, which is worse than none because
the half that works makes the other half look tested.

## What an enum target does

`5 as Color` for a declared `enum`/`union` is accepted, because the compiler ERASES an enum to
`integer` (`resolveNamedType`'s enum arm) — so the cast is `5 as int`, a legal, pointless numeric
no-op, exactly like `5 as SomeIntAlias`. Whether Maxon permits `int` ↔ `enum` casts is a LANGUAGE
question, and this file leaves it open.

## Tests

<!-- test: error.cast-to-undeclared-type -->
The headline case. The name binds nothing anywhere in the program, so there is no type for the
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
its keep on: a wild name is rare, a typo is not, and unchecked the typo would compile to a program
whose narrowing check simply never runs.
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
report this too (`parseTypeReference` mints them `structRef`); the third spelling of the same type
agrees, with the same message.
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

⚠ **READ THE NEXT TEST WITH THIS ONE — ON ITS OWN THIS PROGRAM PROVES NOTHING.** Were the name to
resolve to nothing, the cast would evaporate and the exit code would come out right anyway: green
because the lookup FAILED.

An exit code cannot tell the two apart, and for the CONSTANT operand below neither can a golden:
the value folds and a folded in-range cast emits nothing either way.

⚠ **A RUNTIME OPERAND DOES DISCRIMINATE, so only the CONSTANT one needs this case's argument.**
Every stdlib alias this loader lists is a checked QUANTITY, whose door refuses a value that arrived
from a signed domain — so `x as Milliseconds` emits a guard where `x as NoSuchName` cannot, and the
two are told apart by the emitted code. `stdlib/Builtins.maxon`'s roster names the aliases that
are raw PATTERNS instead, and those are the ones that would emit nothing.

What discriminates HERE is the DIAGNOSTIC, and it is the stronger half in any case: a guard
distinguishes a name that resolved to a NARROW range from one that resolved to nothing, while the
pair below distinguishes a name that resolved AT ALL — which is the property this test is about,
and the one that would survive `Milliseconds` being re-widened. It works because of the rule this
file documents: a cast target that denotes nothing is a hard error. So the pair below IS the discrimination — two
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
