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

**THE RULE: a user declaration and a stdlib module's of one TYPE name coexist, and the library's never
takes part in ambiguity.** The user's file means the user's declaration; a library file never sees an
author declaration and keeps its own; any OTHER author file that sees that one author declaration means
it too, and reaches the library's as `stdlib.Name`. Only two visible AUTHOR declarations are ambiguous
(E3063, listing each as `dir.Name` — `export.Name` at the project root). Coexisting is
load-bearing rather than cosmetic: `parsable-interface.md`'s cases declare their own `enum ParseError`
while implementing the stdlib's `interface Parsable`, so a rule that refused the pair would refuse the
specs that stdlib module exists to unblock. Every case below declares the name and reads it in ONE file,
whose one visible author declaration is what the bare name means.

A FREE FUNCTION follows a different rule: a user free function outranks a stdlib module's of the same name
for user code (see the bullet under *What the rule must NOT do*).

### What the rule actually requires

The rule's SHAPE is not what "the user wins" suggests at first reading:

| program | result |
|---|---|
| user `enum ParseError { mine }`, matched in `main` | compiles, exit 5 |
| the same program, ALSO calling `int.fromString("41")` | compiles, exit 42 |

The second row is the whole story. `int.fromString` is rewritten to `stdlib/Builtins.maxon`'s
`__int_fromString`, whose body throws `ParseError.invalidFormat` — a case the user's `ParseError` does not
have. Both work at once, so the rule is not resolving one name to one declaration program-wide: **the
user's declaring file sees the user's declaration and stdlib code keeps its own.** Parse ORDER would give that scoping for
free — parse all of `stdlib/` first and every reference inside a stdlib body is bound before the user's
declaration overwrites the registry entry.

### Why the compiler cannot get it the same way — and what it does instead

the compiler's front end is a WHOLE-PROGRAM DECLARATION SWEEP: `queryProgramSignatures` folds every file's
declarations into one index BEFORE any file is parsed, and the real parse of every file — stdlib and user
alike — then resolves names against that finished index. There is no "already bound" state for a stdlib
body to have been parsed into, so reordering the files changes nothing. That is the rewrite's thesis
working exactly as designed, and it is what makes this rule a SCOPING mechanism in the compiler rather than a
tie-break.

The obvious rule — "a user declaration displaces a stdlib one" in every declaration registry — gets
one half right and the other wrong:

- a user `type Clock`, `enum CursorError`, `interface Parsable` or ranged `typealias DurationMs` shadowing
  a stdlib module's — **compiles and runs, the user's declaration answering**;
- a user `enum ParseError { Invalid = 1 }` — **`error E3034: stdlib/Builtins.maxon:222:11: unknown enum
  case: 'invalidFormat'`**, twice, inside a file the author never opened. `Builtins.maxon` REFERENCES the
  name it declares, so displacing its declaration retargets its own body at the user's enum.

So two declarations of one name have to coexist, and the compiler identifies aggregates by NAME all the way down —
`structRef(name)`, `aggregateNameFor`, `__destruct_<name>`, `__layout_<name>`, both enum registries, the
interface registry. Coexisting therefore means one of them is RENAMED.

**THE STDLIB ONE MOVES, into the space `E2051` already reserves.** It is the identical trade
`ProgramSignatures.reservedIfDeclared` makes for a contested COMPILED name one namespace over, and for the
identical reason: `__` is a prefix no declaration may take, so the moved name contests nothing and no user
program can reach it. `stdlib/Clock.maxon`'s `type Clock` becomes `__Clock` — declaration, `Self`, methods
(`__Clock.nowMs`) and cross-module references alike — and a user program's own `Clock` keeps the bare name.
It happens ON CONTEST ONLY: a program that shadows nothing renames nothing.

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
`__FilePath`. The value is fine; what needs care is every operation on it whose callee the compiler spells
out of the value's own TYPE.

**A method call is exactly that.** `p.toString()` is dispatched by joining the receiver's resolved type name
to the member — `__FilePath.toString` — and a reserved-CALL door that read the `__` as *"the author reached
for a compiler intrinsic"* would refuse it with **E3004**, about a callee no author wrote and about a function
the program plainly declares. Those are two different meanings sharing one spelling: the `__` of
`__mm_alloc` is a PREFIX THE COMPILER RESERVED, and the `__` of `__FilePath` is a NAME THIS COMPILE MINTED.

⚠ **A FIELD read has no such callee, and the difference is what pins the rule.** `info.isDirectory`
on a `__FileInfo` reads the value's own layout and names nothing — pinned below beside the method case.
So the hazard is not *"a renamed declaration is broken"*; it is precisely *"a callee the compiler mints
out of a renamed type"*.

⇒ The exemption keys on the **MINT**, which is the fact the name's shape cannot carry
(`Parser.requireCalleeIsNotReservedName`, `CalleeMint.resolvedTypeQualifier`). It is deliberately NOT a
widening of the reserved space: the head must be one THIS COMPILE minted, some file must DECLARE the
callee, and the head must not be bytes a user file's author typed — which is why
`error.the-mint-is-not-reachable-from-user-code` below holds. `__Clock.nowMs()` written out in a user
file is still refused; `c.nowMs()` on a value of the moved declaration is not.

### What the rule must NOT do

- **A collision between two STDLIB declarations stays a collision.** Two stdlib modules declaring one type
  name is a maintainer's bug in `stdlib/`, not a shadow, and the whole-program duplicate check exists to
  catch it. It is not pinnable from a fragment — a fragment cannot add a module to `stdlib/` —
  so it is stated here and enforced by that check.
- **A collision between two USER declarations stays a collision**, exactly as `type-name-collision.md`
  pins it. Shadowing is about PROVENANCE, not about tolerating duplicates.
- ⛔ **A FREE-FUNCTION-name collision is NOT a collision.** A user `function sleep` compiles clean —
  not an `E3006` naming `stdlib/Sleep.maxon` — and the USER's body is what its own
  call sites reach, which is the rule above applied to a free function rather than an exception to it.
  `stdlib-loading.md`'s `a-user-free-function-outranks-the-stdlib-modules` and
  `a-value-returning-user-free-function-outranks-a-void-stdlib-one` are the pair that RUN it — the second
  is the negative control. Where a genuine stdlib-path diagnostic does
  arise it is documented rather than pinned in a `maxoncstderr` block, because the path is machine-dependent — the same
  reason `stdlib-loading.md`'s collision rule gives.
- ⚠ **A METHOD is NOT a free function, and the shadow reaches it — deliberately.** A user `Clock.nowMs`
  requires a user `type Clock`, which IS a shadow, so the stdlib method has already moved to
  `__Clock.nowMs` by the time the duplicate-function check runs and there is nothing to collide with.
  A user `type Clock` declaring `static function nowMs()` compiles, and the call resolves to the
  USER's. That is the rule working, not an exception to it — the whole point is that the user's
  declaration answers user code — and it is why this bullet is about FREE functions: they have no type to
  shadow, so nothing moves.

### The kind that needs no rename: a `typealias`, in ANY form

A typealias is resolved per reading file, so a stdlib module's alias and a user's declaration of that
name never need to share one spelling: the shadow contest does not move an alias, and it does not need
to — **the reading file settles it instead of a rename.** In the user's declaring file the user's own
declaration answers; a library file never sees it; and a third author file that sees it and no other
author declaration of the name means it too, writing `stdlib.Name` for the library's. The two are two
types even over one range, so a value crosses between them only through a cast. A
user's own ranged `typealias DurationMs` coexists with `stdlib/Clock.maxon`'s `public` one and answers
for the user's file, including for its RANGE, which is what `user-ranged-typealias-wins-over-a-listed-module`
below observes.

⚠ **This holds whatever FORM either declaration takes, and a wrong answer inside `stdlib/` itself is
what that prevents.** Asking whether the two declarations are the same alias FORM is a proxy for
"neither can see the other" that fails in two directions:

- **A user NOMINAL declaration against a stdlib module's alias.** Under that proxy `type ParsedInt`
  against `stdlib/Builtins.maxon`'s `typealias ParsedInt` is `E3006` — blamed on the STDLIB line,
  because stdlib merges last and is therefore the "newcomer" — plus `E3009` at
  `stdlib/Builtins.maxon`'s `__FloatSignBit`, where the module's own `i64.min as ParsedInt` resolves to the USER's
  struct. Both diagnostics name a file the author never opened. The four nominal keywords all reach it,
  and `enum`/`union` reach only the first: they resolve to `integer` as an alias does, so the module's
  own uses still type-check.
- **Two aliases in different FORMS.** Under that proxy a user `typealias DecimalDigit = function(…)`
  against the stdlib ranged one is `E3061`, though neither file can name the other's declaration either.

**The property that makes a pair legal is that each declaration can be NAMED apart from the other** —
by its file, its directory, or its layer (`stdlib.Name`) — and the form they are written in is not a proxy
for it. Two declarations that no spelling tells apart still collide: `type-name-collision.md`'s
`error.crossfile-type-and-exported-typealias` pins a nameable pair in one directory, and
`two-user-declarations-still-collide` below pins the same-file one.

## Tests

<!-- test: stdlib-user-shadows.user-ranged-typealias-wins-over-a-listed-module -->
`stdlib/Clock.maxon` declares `typealias DurationMs = int(0 to u64.max)` and is loaded into this compile.
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
is `__Clock.nowMs`, and the call resolves to the user's. A FREE function has no type to move; a user
`function sleep` outranks `stdlib/Sleep.maxon`'s by the free-function rule instead (`stdlib-loading.md`).
```maxon
type Clock
	export var x as Integer

	static function nowMs() returns ExitCode
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
moved declaration, so neither is refused as `E3004 … call to undefined function '__FilePath.join': the '__'
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

	function filename() returns ExitCode
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
⚠ **THE CONTRAST THAT PINS THE RULE.** A FIELD read on a value of the moved declaration reads the
value's own layout and names nothing, so it has no minted callee for the reserved door to judge — pinned
here so a cure for the method case that disturbs the value's layout is caught. The cwd
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
		return 90
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
stays refused. Unscoped, it would COMPILE AND RUN, returning the stdlib monotonic clock, while the
identical call in a program with no shadow is refused. The diagnostic below is,
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
instead of landing on it. Without that, the E2051 would be suppressed and the program refused with
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
stdlib tokens, so a `__` name reaching that door from a user file was typed by the author. With the
door unscoped and the re-probe in place, this program would COMPILE and run, exit 9 — a user declaration
in the reserved space accepted silently, the exact hole `requireUnreservedName` exists to close.
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
`stdlib/Builtins.maxon` declares `public typealias ParsedInt`, and a user program declaring its own
`type ParsedInt` compiles. Two refusals are what it must avoid, both inside a file the author never
opened: `E3006` blaming the stdlib declaration as the duplicate — stdlib merges after the user's files, so
the stdlib line is the "newcomer" — and `E3009: Cannot cast from int to struct` at `stdlib/Builtins.maxon`'s
`__FloatSignBit`, where the module's own `i64.min as ParsedInt` would resolve to the USER's struct if the
bare, whole-program struct registry were consulted first.

The two declarations sit in two layers, so neither is a duplicate of the other: the user's file means its
own `type`, and the library file never sees it. The same program with the type named `UserParsedInt`
compiles and returns 7 — the name is the ONLY difference.
```maxon
typealias Value = int(0 to 200)

type ParsedInt
	export let value as Value

	static function create(value Value) returns ParsedInt
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
The same pair with an `enum` against `stdlib/Builtins.maxon`'s file-private `typealias RepeatCount`, and it
is the half that shows the two refusals are SEPARATE. An enum resolves to `integer` exactly as a ranged
alias does, so `stdlib/Builtins.maxon`'s own uses of `RepeatCount` type-check and no `E3009` can arise —
only the duplicate check could refuse it.
Correct resolution with a registry that treats the pair as duplicates would still refuse this.
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
A `union` against `stdlib/Builtins.maxon`'s `public typealias PadWidth`. Listed for the same reason the
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
An `interface` against a stdlib module that is NOT `Builtins.maxon`: `stdlib/Testing.maxon` declares
`public typealias Tolerance = float(0.0 to f64.max)`. The rule is a property of provenance and the reading
file, not of one module, and a case anchored only in `Builtins.maxon` could not tell the two apart.
```maxon
interface Tolerance
	function score() returns ExitCode
end 'Tolerance'

type Fixed implements Tolerance
	static function create() returns Fixed
		return Self{}
	end 'create'

	function score() returns ExitCode
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
against `stdlib/Builtins.maxon`'s ranged `DecimalDigit`. A same-form test would refuse it with `E3061`,
again blamed on the stdlib line. Both aliases are file-private, and a file-private alias is file-local
whatever its form, so the two coexist and each answers for its own file.
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
⭐ **The discriminating case: it is not enough that no diagnostic is raised.** Every case above would
also pass if `stdlib/Builtins.maxon`'s `ParsedInt` meant the user's struct and the compiler simply
did not complain about it. Here the user owns the name AND the program runs stdlib code that depends
on the module's own meaning of it — `stdlib/Builtins.maxon`'s `let __FloatSignBit = i64.min as
ParsedInt` is the sign bit float printing reads, and it is a CONST INITIALIZER, evaluated in every
compile. A wrong answer inside the module shows up as the negative sign, not as an error.
```maxon
typealias Value = int(0 to 200)

type ParsedInt
	export let value as Value

	static function create(value Value) returns ParsedInt
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
another, an element left as the bare name makes them GUESS. **Without the element mint,**
`stdlib/Builtins.maxon`'s own `typealias ParsedIntArray = Array with ParsedInt` has its element read as a
user program's struct, so printing a float drops an array of INTEGERS through `__destruct_ParsedInt` —
**exit 0xC0000005, no diagnostic and no output.**

Here BOTH containers are live at once and each must keep its own element: the user's `ValueBoxes` holds
`ParsedInt` STRUCTS that own their boxes, while `print("{x}")` runs the library's big-integer path over its
own array of `ParsedInt` LIMBS. The declaring file decides the element, so the two are two instances — and
a compiler that fused them either faults on the limbs or leaks the boxes.
```maxon
typealias Value = int(0 to 200)

type ParsedInt
	export let value as Value

	static function create(value Value) returns ParsedInt
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
arrays and still let its meaning slip in this signature passes every case above and faults here —
**exit 0xC0000005 with no diagnostic and no output at all**, the plain integer
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

	static function create(value Value) returns ParsedInt
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
`stdlib/Builtins.maxon` is contested — same six spellings, same expected text. A fault in the case above
that this one does not share is therefore a fault of the SHADOW and not of `"{n:x}"`.
```maxon
typealias Value = int(0 to 200)
typealias Bits = int(0 to u64.max)

type UserParsedInt
	export let value as Value

	static function create(value Value) returns UserParsedInt
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

<!-- test: stdlib-user-shadows.a-library-file-is-unaffected-by-an-authors-same-named-export -->
A user EXPORTS `Byte` over `float`; `stdlib/String.maxon`, whose own `Byte` is `int(0 to u8.max)`, still
compiles and serves `toByteArray`, because a library file never sees an author declaration (105 + 1).
```maxon
// --- file: wide.maxon
export typealias Byte = float(0.0 to 1.0)

export function half(b Byte) returns Byte
	return b / 2.0
end 'half'

// --- file: main.maxon
function main() returns ExitCode
	let bytes = "hi".toByteArray()
	let quarter = half(0.5)
	return ((try bytes.get(1) otherwise 0) + (1 if quarter < 0.5 else 0)) as ExitCode
end 'main'
```
```exitcode
106
```

<!-- test: stdlib-user-shadows.a-library-file-is-unaffected-by-an-authors-same-named-type -->
A user EXPORTS `type FilePath` in another file; `stdlib/Directory.maxon`'s own `FilePath` still answers its
return value, and the user's is named through its directory (4 + 9).
```maxon
// --- file: lib/path.maxon
export type FilePath
	export var tag as ExitCode

	export static function make() returns FilePath
		return FilePath{tag: 4}
	end 'make'
end 'FilePath'

// --- file: app/main.maxon
function main() returns ExitCode
	let child = Directory.currentPath().join("alpha.txt")
	let name = child.filename()
	return lib.FilePath.make().tag + (name.byteLength() as ExitCode)
end 'main'
```
```exitcode
13
```

<!-- test: stdlib-user-shadows.error.an-unthrown-clause-names-the-library-type-as-written -->
A diagnostic names a moved library type as the author writes it, `stdlib.ParseError`, never by the
`__ParseError` the shadow moved it to.
```maxon

enum ParseError implements Error
	bad
end 'ParseError'

function parse() returns ExitCode throws stdlib.ParseError
	return 0
end 'parse'

function check() throws ParseError
	throw ParseError.bad
end 'check'

function main() returns ExitCode
	try check() otherwise ignore
	return try parse() otherwise 1
end 'main'
```
```maxoncstderr
error E3168: <fragment>:7:10: 'throws stdlib.ParseError' is declared but nothing in the body throws — no 'throw', no bare 'try' and no 'otherwise throw' reaches a caller. Remove the clause
```

<!-- test: stdlib-user-shadows.error.an-argument-at-the-moved-library-type-is-named-as-written -->
The same respelling in an argument mismatch: the parameter's type is quoted `stdlib.FileInfo` and the user's
own `FileInfo` bare. A message keeps an author-written `__Clock` as written — the respelling skips any mint
the reporting file's own tokens spell, which is why `error.the-mint-is-not-reachable-from-user-code` and
`error.a-user-reserved-function-beside-a-shadow` quote it.
```maxon

type FileInfo
	var size = 0

	static function make() returns Self
		return Self{}
	end 'make'
end 'FileInfo'

typealias Num = int(0 to 9)

function measure(info stdlib.FileInfo) returns Num
	return 1
end 'measure'

function main() returns ExitCode
	return measure(FileInfo.make())
end 'main'
```
```maxoncstderr
error E3005: <fragment>:18:9: argument type mismatch for 'info': expected 'stdlib.FileInfo', got 'FileInfo'
```

<!-- test: stdlib-user-shadows.a-member-typealias-after-a-method-named-like-its-type-stays-a-member -->
A method may be named like its type, so its own `end 'Holder'` is not where the type's body ends. The member
typealias after it is the type's `Holder.Range`, and the program's bare `Range` is still the library's.
```maxon
typealias Small = int(0 to 50)

type Holder
	let n as Small

	static function make() returns Holder
		return Holder{n: 3}
	end 'make'

	function Holder() returns Small
		return self.n
	end 'Holder'

	typealias Range = int(0 to 9)

	function widened() returns Range
		return self.n
	end 'widened'
end 'Holder'

function main() returns ExitCode
	let span = Range.create(1, finish: 4)
	var total = 0 as ExitCode

	for v in span 'each'
		total = total + (v as ExitCode)
	end 'each'

	return total + (Holder.make().widened() as ExitCode) + (Holder.make().Holder() as ExitCode)
end 'main'
```
```exitcode
16
```

<!-- test: stdlib-user-shadows.a-moved-library-frame-is-named-as-written-in-a-backtrace -->
A backtrace through a method of a library type the program shadows names the frame as the author would write
it, `stdlib.Range.map`, on every lane.
```maxon
type Range
	let n as ExitCode

	static function make() returns Range
		return Range{n: 1}
	end 'make'

	function value() returns ExitCode
		return self.n
	end 'value'
end 'Range'

typealias Small = int(0 to 100)

function main() returns ExitCode
	let bound = stdlib.Range.create(1, finish: 4)
	let doubled = bound.map(function(x) gives x + (((x * 1000) as Small) as RangeBound))
	return Range.make().value() + (doubled.count() as ExitCode)
end 'main'
```
```exitcode
1
```
```stderr
panic at stdlib-user-shadows.a-moved-library-frame-is-named-as-written-in-a-backtrace.test:18: Range check failed: value outside typealias 'Small'
Stack trace:
  in main$closure_0
  in stdlib.Range.map
  in main
  in mrt_start
```

<!-- test: stdlib-user-shadows.error.a-type-the-library-keeps-private-is-hidden-bare-and-qualified -->
`stdlib/FilePath.maxon`'s `ParentComponentRule` carries no `public`, so no author file can name it — bare or
qualified, one rule for both spellings.
```maxon
function bare(r ParentComponentRule) returns ExitCode
	_ = r
	return 1
end 'bare'

function qualified(r stdlib.ParentComponentRule) returns ExitCode
	_ = r
	return 2
end 'qualified'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:2:17: type 'ParentComponentRule' is not exported
error E3008: <fragment>:7:22: type 'stdlib.ParentComponentRule' is not exported
```

<!-- test: stdlib-user-shadows.another-files-private-function-leaves-the-library-one-visible -->
A declaration a file cannot see never counts: `a/`'s file-private `round` answers its own call, and `b/`'s bare `round` is the library's.
```maxon
// --- file: a/a.maxon
typealias Real = float(f64.min to f64.max)

function round(x Real) returns Real
	return x
end 'round'

export function runA()
	print("a {round(2.5)}\n")
end 'runA'

// --- file: b/b.maxon
export function runB()
	let x = 3.7
	print("b {trunc(round(x))}\n")
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	runA()
	runB()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a 2.5
b 4
```

<!-- test: stdlib-user-shadows.two-files-private-functions-each-shadow-the-library-one-for-their-own-file -->
Each directory's file-private `round` answers its own file's calls, and the root file, which sees neither, calls the library's.
```maxon
// --- file: a/a.maxon
typealias Real = float(f64.min to f64.max)

function round(x Real) returns Real
	return x
end 'round'

export function runA()
	print("a {round(2.5)}\n")
end 'runA'

// --- file: b/b.maxon
typealias Real = float(f64.min to f64.max)

function round(x Real) returns Real
	return x + 1.0
end 'round'

export function runB()
	print("b {round(2.5)}\n")
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	runA()
	runB()
	print("main {trunc(round(3.7))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a 2.5
b 3.5
main 4
```

<!-- test: stdlib-user-shadows.a-cast-of-a-library-float-alias-to-the-programs-own-alias-of-that-name-is-not-unneeded -->
`Math.pow` answers the library's `Real`, and this file's `Real` is a different declaration of that name, so the cast between them is a conversion rather than an unneeded cast.
```maxon
typealias Real = float(f64.min to f64.max)

function half(x Real) returns Real
	return x / 2.0
end 'half'

function main() returns ExitCode
	let cube = Math.pow(2.0, exponent: 3.0) as Real
	print("{half(cube)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4.0
```

<!-- test: stdlib-user-shadows.a-masked-count-cast-to-the-programs-own-count-is-not-unneeded -->
A masked `count()` keeps the library's `Count`, and this file's `Count` is a different declaration of that name, so the cast between them is a conversion rather than an unneeded cast.
```maxon
typealias Count = int(0 to u64.max)

function main() returns ExitCode
	let xs = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]
	let low = (xs.count() and 7) as Count
	print("{low}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: stdlib-user-shadows.arithmetic-between-the-librarys-real-and-the-programs-own-real-is-refused -->
`Math.pow` answers the library's `Real` and `mine` is this file's `Real`. They share a spelling and a range but are two declarations, so `+` refuses the pair, quoting the library's as `stdlib.Real`.
```maxon
typealias Real = float(f64.min to f64.max)

function main() returns ExitCode
	let mine = 2.0 as Real
	let sum = Math.pow(2.0, exponent: 3.0) + mine
	print("{sum}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:41: operator '+' requires both operands to be the same type: 'stdlib.Real' and 'Real' are different typealiases — cast one side with 'as'
```

<!-- test: stdlib-user-shadows.a-librarys-count-and-the-programs-own-count-do-not-mix -->
`count()` answers the library's `Count` and `mine` is this file's `Count`, declared with the same range, so `+` refuses the pair.
```maxon
typealias Count = int(0 to u64.max)

function main() returns ExitCode
	let xs = [1, 2, 3]
	let mine = 3 as Count
	let total = xs.count() + mine
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:25: operator '+' requires both operands to be the same type: 'stdlib.Count' and 'Count' are different typealiases — cast one side with 'as'
```
