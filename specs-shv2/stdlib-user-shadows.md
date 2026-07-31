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

### Why shv2 cannot get it the same way

shv2's front end is a WHOLE-PROGRAM DECLARATION SWEEP: `queryProgramSignatures` folds every file's
declarations into one index BEFORE any file is parsed, and the real parse of every file — stdlib and user
alike — then resolves names against that finished index. There is no "already bound" state for a stdlib
body to have been parsed into, so reordering the files changes nothing. That is the rewrite's thesis
working exactly as designed, and it is also what makes this rule a SCOPING mechanism in shv2 rather than a
tie-break.

MEASURED, on an shv2 built with a plain "a user declaration displaces a stdlib one" rule in every
declaration registry:

- a user `type Clock`, `enum CursorError`, `interface Parsable` or ranged `typealias DurationMs` shadowing
  a listed module's — **all compile and run, the user's declaration answering**;
- a user `enum ParseError { Invalid = 1 }` — **`error E3034: stdlib/Builtins.maxon:222:11: unknown enum
  case: 'invalidFormat'`**, twice, inside a file the author never opened. `Builtins.maxon` references the
  name it declares, so displacing its declaration retargets its own body at the user's enum.

So the boundary is exact: a stdlib declaration NO stdlib source references can simply lose, and one that IS
referenced needs two declarations of one name to coexist. shv2 identifies aggregates by NAME all the way
down — `structRef(name)`, `aggregateNameFor`, `__destruct_<name>`, `__layout_<name>` — so two live
declarations under one name are not representable in the merged IR without renaming one of them. The
compiler already has the mechanism for exactly that (`ProgramSignatures.mangleGenericInstance`'s
`reservedIfDeclared` mints a contested compiled name behind the reserved `__` prefix), which is where a
future rung should start.

### What the rule must NOT do

- **A collision between two STDLIB declarations stays a collision.** Two listed modules declaring one type
  name is a maintainer's bug in the whitelist, not a shadow, and `StdlibLoader.maxon`'s collision rule
  exists to catch it. It is not pinnable from a fragment — a fragment cannot add a module to `stdlib/` —
  so it is stated here and enforced by that rule.
- **A collision between two USER declarations stays a collision**, exactly as `type-name-collision.md`
  pins it. Shadowing is about PROVENANCE, not about tolerating duplicates.
- **A FUNCTION-name collision stays a collision.** A user program declaring its own `function sleep` is
  `E3006` naming `stdlib/Sleep.maxon`, and a user `Clock.nowMs` likewise. That diagnostic names the real
  `stdlib/` path, which is machine-dependent, so — like the rest of `stdlib-whitelist.md`'s collision
  rule — it is documented rather than pinned as a golden.

### The one kind that already works: a ranged `typealias`

A non-exported `typealias` is FILE-LOCAL per alias FORM (`type-name-collision.md`), and that carve-out
predates this rule and is untouched by it. A user's own ranged `typealias DurationMs` therefore coexists
with `stdlib/Clock.maxon`'s and answers for the user's file, including for its RANGE — which is what
`user-ranged-typealias-wins-over-a-listed-module` below observes.

## Tests

<!-- test: stdlib-user-shadows.user-ranged-typealias-wins-over-a-listed-module -->
`stdlib/Clock.maxon` declares `typealias DurationMs = int(0 to u64.max)` and is loaded into this compile.
A user file declaring its own `DurationMs` over a NARROWER range is legal, and the range in force in that
file is the USER's: `50` is outside `int(0 to 10)` and is refused against it, where the listed module's
range would have accepted it. The rejection is the observation — a program that merely compiled would not
say WHICH declaration answered.
```maxon
typealias DurationMs = int(0 to 10)

function main() returns ExitCode
	let d = 50 as DurationMs
	return d as ExitCode
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
	return d as ExitCode
end 'main'
```
```exitcode
5
```

<!-- disabled-test: stdlib-user-shadows.user-enum-shadows-a-listed-module -->
<!-- BLOCKED ON PROVENANCE-SCOPED NAME RESOLUTION. `stdlib/Builtins.maxon:133` declares `enum ParseError`
     and REFERENCES it from twelve sites in its own bodies, so the user's declaration cannot simply
     displace it: measured, doing so reports `E3034 unknown enum case: 'invalidFormat'` inside
     `Builtins.maxon` itself. Two declarations of one name must coexist, which shv2's name-keyed aggregate
     identity cannot express until one of them is renamed (see the Documentation above). -->
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

<!-- disabled-test: stdlib-user-shadows.user-type-shadows-a-listed-module -->
<!-- BLOCKED ON THE SAME RUNG. This one is not blocked by a stdlib self-reference — measured, it compiles
     and returns 9 the moment a user declaration is allowed to displace a stdlib one — but it is held with
     its siblings so the rule lands as ONE rule for every declaration kind rather than a per-kind list,
     which is exactly the asymmetry this project's collision rules keep having to undo. -->
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

<!-- disabled-test: stdlib-user-shadows.user-union-shadows-a-listed-module -->
<!-- BLOCKED ON THE SAME RUNG, and held with its siblings for the same reason. `stdlib/Builtins.maxon:138`
     declares `enum CursorError` and references it nowhere, so this compiles and returns 6 as soon as the
     rule lands. -->
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

<!-- disabled-test: stdlib-user-shadows.two-user-declarations-still-collide -->
<!-- BLOCKED ON THE SAME RUNG — not because the collision is missing today (it fires exactly as written),
     but because it is the CONTROL on that rung and must be enabled with it. A rule that admitted a
     shadowed stdlib declaration by widening `TypeNameRegistry.record` would take this case with it, and a
     suite that never ran it could not tell. -->
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
error E3006: <fragment>:7:6: duplicate definition of 'Clock' — already declared as `type Clock`
```
