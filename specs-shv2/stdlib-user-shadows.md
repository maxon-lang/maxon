---
feature: stdlib-user-shadows
status: stable
keywords: [stdlib, loading, shadowing, type-name, collision, ParseError, Clock, typealias]
category: system
---

# A user declaration versus a stdlib module's

## Documentation

The stdlib loader loads every module under `stdlib/` into EVERY compile
(`stdlib-loading.md`). Those modules declare ordinary English type names — `Clock`, `DurationMs`,
`ParseError`, `Promise`, `Parsable` — so the question "what happens when a user program declares one of
them too?" is not hypothetical for any of them.

**THE RULING (user, 2026-07-31): a USER declaration wins over a stdlib module's.** It is the rule
the C# bootstrap already implements, and it is load-bearing rather than cosmetic: `parsable-interface.md`'s
cases declare their own `enum ParseError` while implementing the stdlib's `interface Parsable`, so a rule
that refused the pair would refuse the specs that stdlib module exists to unblock.

### What the oracle actually does, measured

Measured against `maxon-sharp` on 2026-07-31, because the rule's SHAPE is not what "the user wins" suggests
at first reading:

| program | oracle |
|---|---|
| user `enum ParseError { mine }`, matched in `main` | compiles, exit 5 |
| the same program, ALSO calling `int.fromString("41")` | compiles, exit 42 |

The second row is the whole story. `int.fromString` is rewritten to `stdlib/Builtins.maxon`'s
`__int_fromString`, whose body throws `ParseError.invalidFormat` — a case the user's `ParseError` does not
have. Both work at once, so the oracle is not resolving one name to one declaration program-wide: **user
code sees the user's declaration and stdlib code keeps its own.** The bootstrap gets that scoping for free
from parse ORDER — it parses all of `stdlib/` first, so every reference inside a stdlib body is already
bound before the user's declaration overwrites the registry entry.

### Why shv2 cannot get it the same way — and what it does instead

shv2's front end is a WHOLE-PROGRAM DECLARATION SWEEP: `queryProgramSignatures` folds every file's
declarations into one index BEFORE any file is parsed, and the real parse of every file — stdlib and user
alike — then resolves names against that finished index. There is no "already bound" state for a stdlib
body to have been parsed into, so reordering the files changes nothing. That is the rewrite's thesis
working exactly as designed, and it is what makes this rule a SCOPING mechanism in shv2 rather than a
tie-break.

MEASURED, on an shv2 built with the obvious rule — "a user declaration displaces a stdlib one" in every
declaration registry:

- a user `type Clock`, `enum CursorError`, `interface Parsable` or ranged `typealias DurationMs` shadowing
  a stdlib module's — **all compiled and ran, the user's declaration answering**;
- a user `enum ParseError { Invalid = 1 }` — **`error E3034: stdlib/Builtins.maxon:222:11: unknown enum
  case: 'invalidFormat'`**, twice, inside a file the author never opened. `Builtins.maxon` REFERENCES the
  name it declares, so displacing its declaration retargeted its own body at the user's enum.

So two declarations of one name have to coexist, and shv2 identifies aggregates by NAME all the way down —
`structRef(name)`, `aggregateNameFor`, `__destruct_<name>`, `__layout_<name>`, both enum registries, the
interface registry. Coexisting therefore means one of them is RENAMED.

**THE STDLIB ONE MOVES, into the space `E2051` already reserves.** It is the identical trade
`ProgramSignatures.reservedIfDeclared` makes for a contested COMPILED name one namespace over, and for the
identical reason: `__` is a prefix no declaration may take, so the moved name contests nothing and no user
program can reach it. `stdlib/Clock.maxon`'s `type Clock` becomes `__Clock` — declaration, `Self`, methods
(`__Clock.nowMs`) and cross-module references alike — and a user program's own `Clock` keeps the bare name.
It happens ON CONTEST ONLY: a program that shadows nothing renames nothing and compiles to the bytes it did
before the mechanism existed.

The rename is applied to the stdlib file's IDENTIFIER TOKENS, once, at `Parser.create`. That is the whole
design rather than an implementation note: a rename that reached the declaration and missed one of the
derived spellings above would be a SILENT WRONG ANSWER rather than a compile error, so there is deliberately
no roster of doors to keep complete. It is sound because it is UNIFORM over stdlib source — a name renamed
at its declaration is renamed at every stdlib use of it — so everything stdlib-internal is unchanged and
only the cross-file reachability of the bare name moves, which is exactly the rule.

### The one shape the rename cannot cover, and is refused for

A MEMBER of a stdlib type spelled exactly like a contested TYPE name — a field, a method, or an argument
label — is reachable from USER code, whose tokens are never rewritten. The stdlib declaration `var
ParseError` would become `__ParseError` while a user's `x.ParseError` stayed bare, and the two sides would
disagree about one name. Both shapes are token-shaped (an identifier PRECEDED by `.`, or one FOLLOWED by
`:`), so the contest detects them and refuses the program with **E3112** rather than risking it. Nothing in
`stdlib/` does this today; the refusal is what makes that a checked fact rather than an assumption.

### ⭐⭐ What the rename must still leave WORKING: the moved declaration's own members

The soundness argument above is about NAMES — *"only the cross-file reachability of the bare name moves"* —
and a name is not the only thing that crosses that file boundary. A **VALUE** does too. A stdlib module
whose signature mentions the contested type keeps handing user code values of it: `stdlib/Directory.maxon`'s
`Directory.currentPath()` returns a `FilePath`, and under a user `type FilePath` that result is a
`__FilePath`. The value is fine; what broke was every operation on it whose callee the compiler spells out
of the value's own TYPE.

**A method call is exactly that.** `p.toString()` is dispatched by joining the receiver's resolved type name
to the member — `__FilePath.toString` — and the reserved-CALL door then read the `__` as *"the author reached
for a compiler intrinsic"* and refused with **E3004**, about a callee no author wrote and about a function
the program plainly declares. Those are two different meanings sharing one spelling: the `__` of
`__mm_alloc` is a PREFIX THE COMPILER RESERVED, and the `__` of `__FilePath` is a NAME THIS COMPILE MINTED.

⚠ **A FIELD read never had the problem, and the difference is what pins the diagnosis.** `info.isDirectory`
on a `__FileInfo` reads the value's own layout and names nothing — MEASURED working throughout, and pinned
below beside the method case. So the defect was never *"a renamed declaration is broken"*; it was precisely
*"a callee the compiler mints out of a renamed type is refused"*.

⇒ The exemption keys on the **MINT**, which is the fact the name's shape cannot carry
(`Parser.requireCalleeIsNotReservedName`, `CalleeMint.resolvedTypeQualifier`). It is deliberately NOT a
widening of the reserved space: the head must be one THIS COMPILE minted, some file must DECLARE the
callee, and the head must not be bytes a user file's author typed — which is why
`error.the-mint-is-not-reachable-from-user-code` below is unchanged. `__Clock.nowMs()` written out in a user
file is still refused; `c.nowMs()` on a value of the moved declaration is not.

### What the rule must NOT do

- **A collision between two STDLIB declarations stays a collision.** Two stdlib modules declaring one type
  name is a maintainer's bug in `stdlib/`, not a shadow, and the whole-program duplicate check exists to
  catch it. It is not pinnable from a fragment — a fragment cannot add a module to `stdlib/` —
  so it is stated here and enforced by that check.
- **A collision between two USER declarations stays a collision**, exactly as `type-name-collision.md`
  pins it. Shadowing is about PROVENANCE, not about tolerating duplicates.
- ⛔ **A FREE-FUNCTION-name collision is NOT a collision, and this bullet said the opposite.** It read
  *"A user program declaring its own `function sleep` is `E3006` naming `stdlib/Sleep.maxon` — MEASURED"*,
  and no program ever ran it: a user `function sleep` compiles clean and the USER's body is what its own
  call sites reach, which is the ruling above applied to a free function rather than an exception to it.
  `stdlib-loading.md`'s `a-user-free-function-outranks-the-stdlib-modules` and
  `a-value-returning-user-free-function-outranks-a-void-stdlib-one` are the pair that RUN it — the second
  is the negative control, and it is the one that failed. Where a genuine stdlib-path diagnostic does
  arise it is documented rather than pinned as a golden, because the path is machine-dependent — the same
  reason `stdlib-loading.md`'s collision rule gives.
- ⚠ **A METHOD is NOT a free function, and the shadow reaches it — deliberately.** A user `Clock.nowMs`
  requires a user `type Clock`, which IS a shadow, so the stdlib method has already moved to
  `__Clock.nowMs` by the time the duplicate-function check runs and there is nothing to collide with.
  MEASURED: a user `type Clock` declaring `static function nowMs()` compiles, and the call resolves to the
  USER's. That is the rule working, not an exception to it — the whole point is that the user's
  declaration answers user code — and it is why this bullet is about FREE functions: they have no type to
  shadow, so nothing moves.

### The kind that needs no rename: a `typealias`, in ANY form

A non-exported `typealias` is FILE-LOCAL, and a file-local declaration cannot collide with anything: a
stdlib module's alias and a user file's declaration of that name are never both in scope anywhere. So
the shadow contest does not move an alias, and it does not need to — **precedence settles it instead of
a rename.** A user's own ranged `typealias DurationMs` coexists with `stdlib/Clock.maxon`'s and answers
for the user's file, including for its RANGE, which is what
`user-ranged-typealias-wins-over-a-listed-module` below observes.

⚠ **This used to hold ONLY for a ranged alias meeting another ranged alias, and the gap was a wrong
answer inside `stdlib/` itself.** The registry's carve-out asked whether the two declarations were the
same alias FORM, which is a proxy for "neither can see the other" that fails in two directions:

- **A user NOMINAL declaration against a stdlib module's alias.** `type ParsedInt` against
  `stdlib/Builtins.maxon:229`'s `typealias ParsedInt` was `E3006` — blamed on the STDLIB line, because
  stdlib merges last and is therefore the "newcomer" — plus `E3009` at `stdlib/Builtins.maxon:427`,
  where the module's own `i64.min as ParsedInt` resolved to the USER's struct. Both diagnostics named a
  file the author never opened. The four nominal keywords all reach it, and `enum`/`union` reach only
  the first: they resolve to `integer` as an alias does, so the module's own uses still type-check.
- **Two aliases in different FORMS.** A user `typealias DecimalDigit = function(…)` against the stdlib
  ranged one was `E3061`, though neither file can name the other's declaration either.

**The property that makes a pair legal is that neither declaration can SEE the other** — visibility and
provenance — and the form they are written in is not a proxy for it. Two declarations that genuinely do
meet still collide: `type-name-collision.md`'s `error.crossfile-type-and-exported-typealias` pins the
`export`ed pair, and `two-user-declarations-still-collide` below pins the same-file one.

## Tests

<!-- test: stdlib-user-shadows.user-ranged-typealias-wins-over-a-listed-module -->
`stdlib/Clock.maxon` declares `typealias DurationMs = int(0 to i64.max)` and is loaded into this compile.
A user file declaring its own `DurationMs` over a NARROWER range is legal, and the range in force in that
file is the USER's: `50` is outside `int(0 to 10)` and is refused against it, where the stdlib module's
range would have accepted it. The rejection is the observation — a program that merely compiled would not
say WHICH declaration answered.
```maxon
typealias DurationMs = int(0 to 10)

function main() returns ExitCode
	let d = 50 as DurationMs
	return d
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:13: Value 50 is outside the range of 'DurationMs' (int(0 to 10))
```

<!-- test: stdlib-user-shadows.user-ranged-typealias-still-usable -->
The same declaration in force positively: a value INSIDE the user's range compiles and runs, so the
carve-out is not merely a suppressed diagnostic.
```maxon
typealias DurationMs = int(0 to 10)

function main() returns ExitCode
	let d = 5 as DurationMs
	return d
end 'main'
```
```exitcode
5
```

<!-- test: stdlib-user-shadows.user-enum-shadows-a-listed-module -->
```maxon
enum ParseError implements Error
	Invalid = 1
end 'ParseError'

function main() returns ExitCode
	let e = ParseError.Invalid
	return match e 'which'
		Invalid gives 5
	end 'which'
end 'main'
```
```exitcode
5
```

<!-- test: stdlib-user-shadows.user-type-shadows-a-listed-module -->
```maxon
type Clock
	export var x as Integer

	static function make() returns Clock
		return Clock{x: 9}
	end 'make'
end 'Clock'

function main() returns ExitCode
	let c = Clock.make()
	return c.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
9
```

<!-- test: stdlib-user-shadows.user-union-shadows-a-listed-module -->
```maxon
union CursorError
	mine
	yours
end 'CursorError'

function main() returns ExitCode
	let e = CursorError.mine
	return match e 'which'
		mine gives 6
		yours gives 1
	end 'which'
end 'main'
```
```exitcode
6
```

<!-- test: stdlib-user-shadows.user-method-shadows-a-listed-modules-method -->
A METHOD moves with its type. `stdlib/Clock.maxon` declares `Clock.nowMs`, and a user `type Clock` that
declares its own is NOT `E3006` against it: by the time the duplicate-function check runs the stdlib method
is `__Clock.nowMs`, and the call resolves to the user's. This is the observation that separates a METHOD
from a FREE function — a user `function sleep` is still `E3006` against `stdlib/Sleep.maxon`, because a
free function has no type to shadow.
```maxon
type Clock
	export var x as Integer

	export static function nowMs() returns ExitCode
		return 11
	end 'nowMs'
end 'Clock'

function main() returns ExitCode
	return Clock.nowMs()
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
11
```

<!-- test: stdlib-user-shadows.a-method-on-a-value-of-the-moved-declaration -->
⭐ **THE VALUE CROSSES THE FILE BOUNDARY THE NAME DOES NOT.** `Directory.currentPath()` hands user code a
`FilePath` — a `__FilePath` under this shadow — and `join`/`filename` are ordinary declared methods of that
moved declaration. Before this they were `E3004 … call to undefined function '__FilePath.join': the '__'
prefix names a compiler intrinsic`, about a callee the author never wrote. `"alpha.txt"` is 9 bytes, and
the user's own `FilePath` supplies the other 4.
```maxon
type FilePath
	export var tag as Integer

	static function make() returns FilePath
		return FilePath{tag: 4}
	end 'make'
end 'FilePath'

function main() returns ExitCode
	let child = Directory.currentPath().join("alpha.txt")
	return (FilePath.make().tag + (child.filename().byteLength() as Integer)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
13
```

<!-- test: stdlib-user-shadows.both-declarations-serve-one-member-name-at-once -->
⭐⭐ **BOTH DIRECTIONS IN ONE PROGRAM, WHICH IS THE ONLY WAY TO OBSERVE THAT NEITHER DISPLACED THE OTHER.**
`filename()` is declared by the user's `FilePath` AND by the moved corpus one, and each receiver reaches its
own: the user's answers 7, the corpus's answers `"alpha.txt"`. A rule that resolved one name to one
declaration program-wide could not produce 16.
```maxon
type FilePath
	export var tag as Integer

	static function make() returns FilePath
		return FilePath{tag: 2}
	end 'make'

	export function filename() returns ExitCode
		return 7
	end 'filename'
end 'FilePath'

function main() returns ExitCode
	let mine = FilePath.make()
	let child = Directory.currentPath().join("alpha.txt")
	return mine.filename() + (child.filename().byteLength() as ExitCode)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
16
```

<!-- test: stdlib-user-shadows.a-field-of-a-value-of-the-moved-declaration -->
⚠ **THE CONTRAST THAT PINS THE DIAGNOSIS.** A FIELD read on a value of the moved declaration reads the
value's own layout and names nothing, so it worked while the method call did not — MEASURED before the fix,
and pinned here so a future cure that reaches the method by disturbing the value's layout is caught. The cwd
is a directory, so `isDirectory` adds 1 to the user's own 5.
```maxon
type FileInfo
	export var tag as Integer

	static function make() returns FileInfo
		return FileInfo{tag: 5}
	end 'make'
end 'FileInfo'

function main() returns ExitCode
	let info = try File.info(Directory.currentPath()) otherwise 'missing'
		return 90 as ExitCode
	end 'missing'
	return (FileInfo.make().tag + (1 if info.isDirectory else 0)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
6
```

<!-- test: stdlib-user-shadows.error.the-mint-is-not-reachable-from-user-code -->
⭐ **THE MINTED NAME IS NOT A NAME USER CODE MAY *WRITE* IN A CALL, AND THAT IS THE WHOLE SAFETY ARGUMENT
FOR MOVING THE STDLIB DECLARATION INTO THE RESERVED SPACE.** `stdlib/Clock.maxon`'s `Clock.nowMs()` becomes
`__Clock.nowMs()` under this shadow, and the reserved-CALL door has to admit that callee wherever the
COMPILER put those bytes there — so the exemption is scoped to the head's PROVENANCE rather than to the
name: a stdlib file (whose identifier tokens the contest rewrote) or a `CalleeMint.resolvedTypeQualifier`
dispatch (whose head is a resolved type). This program is neither — the author typed `__Clock` — and it
stays refused. MEASURED before any such scoping: it COMPILED AND RAN, returning the stdlib monotonic clock,
while the identical call in a program with no shadow was correctly refused. The diagnostic below is,
position aside, the very one a program with no shadow gets, which is the point: whether a user may WRITE
`__Clock.nowMs` must not depend on an unrelated declaration elsewhere in the program.
⚠ The sibling cases above reach that same function through a VALUE, which is not this — see
`a-method-on-a-value-of-the-moved-declaration`.
```maxon
type Clock
	export var y as Integer
end 'Clock'

function main() returns ExitCode
	return __Clock.nowMs() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3004: <fragment>:7:17: call to undefined function '__Clock.nowMs': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: stdlib-user-shadows.error.a-user-reserved-declaration-beside-a-shadow -->
⚠ **A USER `__` DECLARATION IS E2051 WHETHER OR NOT IT COLLIDES WITH A MINT, AND THE DIAGNOSTIC POINTS AT
THE USER'S OWN LINE.** This case is the TYPE half, and what guards it is that the mint re-probes past every
name a user declaration already holds — even an ILLEGAL one — so `Clock` moves past this declaration
instead of landing on it. MEASURED without that: the E2051 was suppressed and the program was refused with
`E3006: stdlib/Clock.maxon:10:13: duplicate definition of '__Clock'` — the mint's own collision, reported
inside a file the author never opened. The case below is the other half.
```maxon
type __Clock
	export var x as Integer
end '__Clock'

type Clock
	export var y as Integer

	static function make() returns Clock
		return Clock{y: 6}
	end 'make'
end 'Clock'

function main() returns ExitCode
	return Clock.make().y as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2051: <fragment>:2:6: identifier '__Clock' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: stdlib-user-shadows.error.a-user-reserved-function-beside-a-shadow -->
⚠⚠ **AND THE OTHER HALF, WHICH THE RE-PROBE STRUCTURALLY CANNOT COVER: `requireUnreservedName` GUARDS EIGHT
DECLARATION KINDS AND THE CONTEST KNOWS ONLY TYPE NAMES.** A `function`/`let`/`var`/field/parameter/enum-case
named `__Clock` is invisible to `userDeclaredTypeNames`, so the mint for a shadowed `Clock` IS `__Clock` and
the reservation door has to be the one that says no — which it can, because a mint is only ever WRITTEN into
stdlib tokens, so a `__` name reaching that door from a user file was typed by the author. MEASURED with the
door unscoped and the re-probe in place: this program COMPILED and ran, exit 9 — a user declaration in the
reserved space accepted silently, which is the exact hole `requireUnreservedName`'s own header calls the
class of defect its rung exists to close.
```maxon
type Clock
	export var y as Integer

	static function make() returns Clock
		return Clock{y: 6}
	end 'make'
end 'Clock'

function __Clock() returns ExitCode
	return 3
end '__Clock'

function main() returns ExitCode
	return (Clock.make().y + __Clock()) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2051: <fragment>:10:10: identifier '__Clock' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: stdlib-user-shadows.two-user-declarations-still-collide -->
```maxon
type Clock
	export var x as Integer
end 'Clock'

enum Clock
	red
end 'Clock'

function main() returns ExitCode
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3006: <fragment>:6:6: duplicate definition of 'Clock' — already declared as `type Clock`
```

<!-- test: stdlib-user-shadows.user-type-coexists-with-a-listed-modules-typealias -->
`stdlib/Builtins.maxon:229` declares a NON-EXPORTED `typealias ParsedInt`, and a user program declaring
its own `type ParsedInt` used to be refused TWICE, both times inside a file the author never opened:
`E3006` blamed the stdlib declaration as the duplicate — stdlib merges after the user's files, so the
stdlib line is the "newcomer" — and `E3009: Cannot cast from int to struct` at
`stdlib/Builtins.maxon:427`, where the module's own `i64.min as ParsedInt` resolved to the USER's struct
because the struct registry is bare and whole-program and is consulted first.

Neither declaration is reachable from the other's file, so neither is a duplicate of anything: a
non-exported alias is file-local, and provenance settles the rest. Renaming the type to `UserParsedInt`
compiled and returned 7 all along — the name was the ONLY difference.
```maxon
typealias Value = int(0 to 200)

type ParsedInt
	export let value as Value

	export static function create(value Value) returns ParsedInt
		return Self{value: value}
	end 'create'
end 'ParsedInt'

function main() returns ExitCode
	let p = ParsedInt.create(7)
	return p.value
end 'main'
```
```exitcode
7
```

<!-- test: stdlib-user-shadows.user-enum-coexists-with-a-listed-modules-typealias -->
The same pair with an `enum`, and it is the half that shows the two defects were SEPARATE. An enum
resolves to `integer` exactly as a ranged alias does, so `stdlib/Builtins.maxon`'s own uses of
`RepeatCount` (`:1699`) still type-checked and no `E3009` was ever raised — the program was refused by
the duplicate check ALONE. A fix to resolution that left the registry alone would still refuse this.
```maxon
enum RepeatCount
	first = 1
	second = 7
end 'RepeatCount'

function main() returns ExitCode
	return RepeatCount.second.rawValue
end 'main'
```
```exitcode
7
```

<!-- test: stdlib-user-shadows.user-union-coexists-with-a-listed-modules-typealias -->
A `union` against `stdlib/Builtins.maxon:1846`'s `typealias PadWidth`. Listed for the same reason the
`enum` case is: the rule is about the KEYWORD-INDEPENDENT namespace, so every declaration kind that
files a name has to be observed, not inferred from the one that was.
```maxon
union PadWidth
	narrow
	wide
end 'PadWidth'

function main() returns ExitCode
	let p = PadWidth.wide

	match p 'pick'
		narrow then return 1
		wide then return 7
	end 'pick'
end 'main'
```
```exitcode
7
```

<!-- test: stdlib-user-shadows.user-interface-coexists-with-a-listed-modules-typealias -->
An `interface` against a stdlib module that is NOT `Builtins.maxon`: `stdlib/Testing.maxon:72` declares
`typealias Tolerance = float(0.0 to f64.max)`. The rule is a property of provenance and visibility, not
of one module, and a case anchored only in `Builtins.maxon` could not tell the two apart.
```maxon
interface Tolerance
	function score() returns ExitCode
end 'Tolerance'

type Fixed implements Tolerance
	export static function create() returns Fixed
		return Self{}
	end 'create'

	export function score() returns ExitCode
		return 7
	end 'score'
end 'Fixed'

function main() returns ExitCode
	let f = Fixed.create()
	return f.score()
end 'main'
```
```exitcode
7
```

<!-- test: stdlib-user-shadows.user-function-alias-coexists-with-a-listed-modules-ranged-typealias -->
Two `typealias` declarations of one name in two files, in DIFFERENT forms — a user function alias
against `stdlib/Builtins.maxon:375`'s ranged `DecimalDigit`. The same-form carve-out did not cover it,
so it was `E3061`, again blamed on the stdlib line. A file-private alias is file-local whatever its
form, so the two coexist and each answers for its own file.
```maxon
typealias DecimalDigit = function(value ExitCode) returns ExitCode

function apply(f DecimalDigit, v ExitCode) returns ExitCode
	return f(v)
end 'apply'

function identity(value ExitCode) returns ExitCode
	return value
end 'identity'

function main() returns ExitCode
	return apply(identity, v: 7)
end 'main'
```
```exitcode
7
```

<!-- test: stdlib-user-shadows.the-listed-module-keeps-its-own-alias -->
⭐ **The discriminating case: it is not enough that the diagnostics stopped.** Every case above would
also pass if the fix had made `stdlib/Builtins.maxon`'s `ParsedInt` mean the user's struct and simply
stopped complaining about it. Here the user owns the name AND the program runs stdlib code that depends
on the module's own meaning of it — `stdlib/Builtins.maxon:427`'s `let __FloatSignBit = i64.min as
ParsedInt` is the sign bit float printing reads, and it is a CONST INITIALIZER, evaluated in every
compile. A wrong answer inside the module shows up as the negative sign, not as an error.
```maxon
typealias Value = int(0 to 200)

type ParsedInt
	export let value as Value

	export static function create(value Value) returns ParsedInt
		return Self{value: value}
	end 'create'
end 'ParsedInt'

function main() returns ExitCode
	let p = ParsedInt.create(0)
	let x = 2.5
	print("{x}")
	print("{-x}")
	return p.value
end 'main'
```
```exitcode
0
```
```stdout
2.5-2.5
```

<!-- test: stdlib-user-shadows.a-user-container-over-the-shadowed-name -->
⭐⭐ **The case that witnesses the MEMORY-SAFETY half, and it is the one the case above can only reach by
accident.** A container's ELEMENT is classified by a tier that has no reading file — `typeIsManaged`, and
the drop and clone routers behind it — so for a name that is a `type` in one file and a `typealias` in
another, an element left as the bare name makes them GUESS. **MEASURED before the element mint:**
`stdlib/Builtins.maxon`'s own `typealias ParsedIntArray = Array with ParsedInt` had its element read as a
user program's struct, so printing a float dropped an array of INTEGERS through `__destruct_ParsedInt` —
**exit 0xC0000005, no diagnostic and no output.**

Here BOTH containers are live at once and each must keep its own element: the user's `ValueBoxes` holds
`ParsedInt` STRUCTS that own their boxes, while `print("{x}")` runs the library's big-integer path over its
own array of `ParsedInt` LIMBS. The declaring file decides the element, so the two are two instances — and
a compiler that fused them either faults on the limbs or leaks the boxes.
```maxon
typealias Value = int(0 to 200)

type ParsedInt
	export let value as Value

	export static function create(value Value) returns ParsedInt
		return Self{value: value}
	end 'create'
end 'ParsedInt'

typealias ValueBoxes = Array with ParsedInt

function main() returns ExitCode
	var boxes = ValueBoxes.create()
	boxes.push(ParsedInt.create(3))
	boxes.push(ParsedInt.create(4))

	let x = 2.5
	print("{x}")

	var total = 0
	for b in boxes 'each'
		total = total + b.value
	end 'each'

	return total as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
2.5
```

<!-- test: stdlib-user-shadows.the-format-spec-path-keeps-the-modules-alias -->
⭐⭐ **The route the two cases above cannot reach, and it was still wrong when they went green.** Float
printing enters `stdlib/Builtins.maxon` through `__float_toString`; an INTEGER format spec — `"{n:x}"`,
`"{n:b}"`, `"{n:o}"`, `"{n:d}"` and their padded forms — enters somewhere else entirely, at
`__int_toStringFormatted(value ParsedInt, …)`, whose FIRST PARAMETER is the contested name. Nothing about
a working float path constrains it: a compiler that resolved `ParsedInt` correctly for the module's
arrays and still let its meaning slip in this signature passes every case above and faults here, which is
what was MEASURED — **exit 0xC0000005 with no diagnostic and no output at all**, the plain integer
argument arriving at a parameter typed as the user's STRUCT.

One case covers all four bases deliberately: they are not four features but one entry point, reached
through one lowering, so a fix that reached only the base the case happened to name would be a fix that
was never tested. The user's struct is live in the same program and answers for the exit code, so the
case also fails if the module's meaning wins in the other direction.
```maxon
typealias Value = int(0 to 200)
typealias Bits = int(0 to u64.max)

type ParsedInt
	export let value as Value

	export static function create(value Value) returns ParsedInt
		return Self{value: value}
	end 'create'
end 'ParsedInt'

function main() returns ExitCode
	let p = ParsedInt.create(11)
	let n = 48879 as Bits

	print("{n:x}")
	print("|{n:X}")
	print("|{n:o}")
	print("|{n:b}")
	print("|{n:06x}")
	print("|{n:d}")

	return p.value
end 'main'
```
```exitcode
11
```
```stdout
beef|BEEF|137357|1011111011101111|00beef|48879
```

<!-- test: stdlib-user-shadows.the-format-spec-path-control-under-an-uncontested-name -->
The CONTROL for the case above, and the reason its failure can be attributed to the name rather than to
the format specs. Byte for byte the same program with the type renamed to `UserParsedInt`, so nothing in
`stdlib/Builtins.maxon` is contested — same six spellings, same expected text. It passed while its twin
faulted, which is what makes the pair a measurement of the SHADOW and not of `"{n:x}"`.
```maxon
typealias Value = int(0 to 200)
typealias Bits = int(0 to u64.max)

type UserParsedInt
	export let value as Value

	export static function create(value Value) returns UserParsedInt
		return Self{value: value}
	end 'create'
end 'UserParsedInt'

function main() returns ExitCode
	let p = UserParsedInt.create(11)
	let n = 48879 as Bits

	print("{n:x}")
	print("|{n:X}")
	print("|{n:o}")
	print("|{n:b}")
	print("|{n:06x}")
	print("|{n:d}")

	return p.value
end 'main'
```
```exitcode
11
```
```stdout
beef|BEEF|137357|1011111011101111|00beef|48879
```
