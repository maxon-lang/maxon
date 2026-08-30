---
feature: stdlib-user-shadows
status: stable
keywords: [stdlib, whitelist, shadowing, type-name, collision, ParseError, Clock, typealias]
category: system
---

# A user declaration versus a listed stdlib module's

## Documentation

The stdlib loader lists a growing subset of `stdlib/` and loads each listed module into EVERY compile
(`stdlib-whitelist.md`). Those modules declare ordinary English type names — `Clock`, `DurationMs`,
`ParseError`, `Promise`, `Parsable` — so the question "what happens when a user program declares one of
them too?" stops being hypothetical the moment a module is listed.

**THE RULING (user, 2026-07-31): a USER declaration wins over a listed stdlib module's.** It is the rule
the C# bootstrap already implements, and it is load-bearing rather than cosmetic: `parsable-interface.md`'s
cases declare their own `enum ParseError` while implementing the stdlib's `interface Parsable`, so a rule
that refused the pair would refuse the specs the whitelist entry exists to unblock.

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
  a listed module's — **all compiled and ran, the user's declaration answering**;
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
and a name is not the only thing that crosses that file boundary. A **VALUE** does too. A listed module
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

- **A collision between two STDLIB declarations stays a collision.** Two listed modules declaring one type
  name is a maintainer's bug in the whitelist, not a shadow, and `StdlibLoader.maxon`'s collision rule
  exists to catch it. It is not pinnable from a fragment — a fragment cannot add a module to `stdlib/` —
  so it is stated here and enforced by that rule.
- **A collision between two USER declarations stays a collision**, exactly as `type-name-collision.md`
  pins it. Shadowing is about PROVENANCE, not about tolerating duplicates.
- **A FREE-FUNCTION-name collision stays a collision.** A user program declaring its own `function sleep`
  is `E3006` naming `stdlib/Sleep.maxon` — MEASURED. That diagnostic names the real `stdlib/` path, which
  is machine-dependent, so — like the rest of `stdlib-whitelist.md`'s collision rule — it is documented
  rather than pinned as a golden.
- ⚠ **A METHOD is NOT a free function, and the shadow reaches it — deliberately.** A user `Clock.nowMs`
  requires a user `type Clock`, which IS a shadow, so the stdlib method has already moved to
  `__Clock.nowMs` by the time the duplicate-function check runs and there is nothing to collide with.
  MEASURED: a user `type Clock` declaring `static function nowMs()` compiles, and the call resolves to the
  USER's. That is the rule working, not an exception to it — the whole point is that the user's
  declaration answers user code — and it is why this bullet is about FREE functions: they have no type to
  shadow, so nothing moves.

### The one kind that already works: a ranged `typealias`

A non-exported `typealias` is FILE-LOCAL per alias FORM (`type-name-collision.md`), and that carve-out
predates this rule and is untouched by it. A user's own ranged `typealias DurationMs` therefore coexists
with `stdlib/Clock.maxon`'s and answers for the user's file, including for its RANGE — which is what
`user-ranged-typealias-wins-over-a-listed-module` below observes.

## Tests

<!-- test: stdlib-user-shadows.user-ranged-typealias-wins-over-a-listed-module -->
`stdlib/Clock.maxon` declares `typealias DurationMs = int(0 to i64.max)` and is loaded into this compile.
A user file declaring its own `DurationMs` over a NARROWER range is legal, and the range in force in that
file is the USER's: `50` is outside `int(0 to 10)` and is refused against it, where the listed module's
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
	export var x as int

	static function make() returns Clock
		return Clock{x: 9}
	end 'make'
end 'Clock'

function main() returns ExitCode
	let c = Clock.make()
	return c.x as ExitCode
end 'main'
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
	export var x as int

	export static function nowMs() returns ExitCode
		return 11
	end 'nowMs'
end 'Clock'

function main() returns ExitCode
	return Clock.nowMs()
end 'main'
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
	export var tag as int

	static function make() returns FilePath
		return FilePath{tag: 4}
	end 'make'
end 'FilePath'

function main() returns ExitCode
	let child = Directory.currentPath().join("alpha.txt")
	return (FilePath.make().tag + child.filename().byteLength()) as ExitCode
end 'main'
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
	export var tag as int

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
	return (mine.filename() + child.filename().byteLength()) as ExitCode
end 'main'
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
	export var tag as int

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
	export var y as int
end 'Clock'

function main() returns ExitCode
	return __Clock.nowMs() as ExitCode
end 'main'
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
	export var x as int
end '__Clock'

type Clock
	export var y as int

	static function make() returns Clock
		return Clock{y: 6}
	end 'make'
end 'Clock'

function main() returns ExitCode
	return Clock.make().y as ExitCode
end 'main'
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
	export var y as int

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
```
```maxoncstderr
error E2051: <fragment>:10:10: identifier '__Clock' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: stdlib-user-shadows.two-user-declarations-still-collide -->
```maxon
type Clock
	export var x as int
end 'Clock'

enum Clock
	red
end 'Clock'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:6:6: duplicate definition of 'Clock' — already declared as `type Clock`
```
