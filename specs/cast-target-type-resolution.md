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
| a declared `enum` | **E3009**, at the `as` |
| a declared `union` | **E3009**, at the `as` |
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

⚠ **A cast target is written in TWO places, and both are covered.** A body cast and a top-level
`let X = 5 as T` both go through the body's cast parser, the top-level one while the declaration sweep
folds the constant (`ProgramSignatures.evaluateInitializers`), before any file's real parse. Covering only the first would leave
`let SENTINEL = 5 as Bogus` unpinned at the place it is folded.

## No value is forged through a cast

An integer is not an enum case and not a union value, so `5 as Color` and `4096 as Outcome` are **E3009**,
whether the integer is a literal or computed at run time. The compiler represents an enum as its integer and a
payload-carrying union as a pointer to its box, so an accepted cast would hand the program a value no case
carries — or an address it then dereferences.

- **An enum** comes from an integer through `Color.fromRawValue(raw)`, which throws `noSuchRawValue` when no
  case carries `raw` (`constants.md`).
- **A union** value is built by its case constructor, `Outcome.ok(7)`.

**An interface value converts to no other interface.** `a as B`, where `a` is held as interface `A`, is
E3009, even when the concrete type behind `a` happens to implement `B`: the cast is checked against the
interface the value is held as, which promises nothing about `B`.

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
error E3011: specs/cast-target-type-resolution/error.cast-to-undeclared-type.maxon:3:12: Unknown type 'CompletelyMadeUpNameXyz'
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
error E3011: specs/cast-target-type-resolution/error.cast-to-misspelled-alias.maxon:5:12: Unknown type 'Scor'
```

<!-- test: error.top-level-const-cast-to-undeclared-type -->
THE SECOND CAST SITE. A top-level `let`'s initializer is folded during the declaration sweep
(`ProgramSignatures.evaluateInitializers`), before any file's real parse, by the body's own cast
parser, so the two spellings of one mistake report identically.
```maxon
let SENTINEL = 5 as CompletelyMadeUpNameXyz

function main() returns ExitCode
	return SENTINEL as ExitCode
end 'main'
```
```maxoncstderr
error E3011: specs/cast-target-type-resolution/error.top-level-const-cast-to-undeclared-type.maxon:2:18: Unknown type 'CompletelyMadeUpNameXyz'
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
error E3009: specs/cast-target-type-resolution/error.cast-to-struct-type.maxon:9:12: Cannot cast from int to struct
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

An exit code cannot tell the two apart. The emitted code can: every stdlib alias this loader lists is
a checked QUANTITY, whose door refuses a value that arrived from a signed domain — so
`seed() as Milliseconds` emits a guard where a cast to a name that resolved to nothing cannot.
`stdlib/Builtins.maxon`'s roster names the aliases that are raw PATTERNS instead, and a cast to one
of those would emit nothing.

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
function seed() returns FullRangeInt
	return 5
end 'seed'

function main() returns ExitCode
	let ms = seed() as Milliseconds
	return ms as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.cast-to-misspelled-stdlib-typealias -->
The negative half of the pair above: `Millisecond` (singular) is not declared anywhere, and the
program is otherwise character-for-character the same.
```maxon
function seed() returns FullRangeInt
	return 5
end 'seed'

function main() returns ExitCode
	let ms = seed() as Millisecond
	return ms as ExitCode
end 'main'
```
```maxoncstderr
error E3011: specs/cast-target-type-resolution/error.cast-to-misspelled-stdlib-typealias.maxon:7:18: Unknown type 'Millisecond'
```

<!-- test: error.cast-int-literal-to-enum -->
An enum named from outside its body, the same spelling as the struct case above. The message names the
lookup that does the job.
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let c = 1 as Color
	return 1 if c == Color.green else 0
end 'main'
```
```maxoncstderr
error E3009: specs/cast-target-type-resolution/error.cast-int-literal-to-enum.maxon:9:12: Cannot cast from int to enum 'Color' — an integer is not a case; use 'Color.fromRawValue(raw)', which throws 'noSuchRawValue' for a value no case carries
```

<!-- test: error.cast-computed-int-to-enum -->
The same refusal for an integer only the run knows, which is the one that can hold a value no case carries.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Color
	red
	green
	blue
end 'Color'

function pick(n Integer) returns Integer
	return n + 7
end 'pick'

function main() returns ExitCode
	let raw = pick(0)
	let c = raw as Color
	return 1 if c == Color.green else 0
end 'main'
```
```maxoncstderr
error E3009: specs/cast-target-type-resolution/error.cast-computed-int-to-enum.maxon:16:14: Cannot cast from int to enum 'Color' — an integer is not a case; use 'Color.fromRawValue(raw)', which throws 'noSuchRawValue' for a value no case carries
```

<!-- test: an-enum-comes-from-an-integer-through-from-raw-value -->
The cure the refusal names: a raw value a case carries gives that case, and one no case carries throws.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Color
	red
	green
	blue
end 'Color'

function pick(n Integer) returns Integer
	return n + 1
end 'pick'

function main() returns ExitCode
	let found = try Color.fromRawValue(pick(0)) otherwise Color.red
	let missing = try Color.fromRawValue(pick(6)) otherwise Color.blue
	print("{found.name} {missing.name}\n")
	return 0
end 'main'
```
```stdout
green blue
```
```exitcode
0
```

<!-- test: error.cast-int-to-union -->
A payload-carrying union is a pointer to its box, so an accepted cast would hand `match` the address 4096
to read a case and a payload from.
```maxon
typealias Integer = int(i64.min to i64.max)

union Outcome
	ok(v Integer)
	err(msg String)
end 'Outcome'

function main() returns ExitCode
	let o = 4096 as Outcome
	match o 'm'
		ok(v) then print("{v}\n")
		err(msg) then print(msg)
	end 'm'
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/cast-target-type-resolution/error.cast-int-to-union.maxon:10:15: Cannot cast from int to union 'Outcome' — a union value is built by its case constructor
```

<!-- test: error.cast-interface-to-another-interface -->
`a` is held as `A`, and `B` is an interface `T` does not even implement. The cast is refused where it is
written rather than reaching code generation.
```maxon
typealias Integer = int(i64.min to i64.max)

interface A
	function f() returns Integer
end 'A'

interface B
	function g() returns String
end 'B'

type T implements A
	var n as Integer

	static function create() returns T
		return T{n: 7}
	end 'create'

	function f() returns Integer
		return self.n
	end 'f'
end 'T'

function viaA(a A) returns String
	let b = a as B
	return b.g()
end 'viaA'

function main() returns ExitCode
	print(viaA(T.create()))
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/cast-target-type-resolution/error.cast-interface-to-another-interface.maxon:25:12: Cannot cast from interface 'A' to interface 'B' — an interface value converts to no other interface
```

<!-- test: error.cast-interface-to-an-interface-the-conformer-also-implements -->
`T implements A, B`, so the value behind `a` could answer `g()`. The cast is refused all the same: it is
checked against `A`, the interface `a` is held as, and `A` promises nothing about `B`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface A
	function f() returns Integer
end 'A'

interface B
	function g() returns String
end 'B'

type T implements A, B
	var n as Integer

	static function create() returns T
		return T{n: 7}
	end 'create'

	function f() returns Integer
		return self.n
	end 'f'

	function g() returns String
		return "t {self.n}"
	end 'g'
end 'T'

function viaA(a A) returns String
	let b = a as B
	return b.g()
end 'viaA'

function main() returns ExitCode
	print(viaA(T.create()))
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:29:12: Cannot cast from interface 'A' to interface 'B' — an interface value converts to no other interface
```
