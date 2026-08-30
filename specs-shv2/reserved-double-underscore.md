---
feature: reserved-double-underscore
status: stable
keywords: [parse-error, reserved-identifier, naming, compiler-internals]
category: diagnostics
---

# Reserved Double-Underscore Identifiers

## Documentation

Identifiers starting with two underscores (`__`) are reserved for compiler-internal
symbols — runtime intrinsics (`__gt_spawn`, `__chkstk`), built-in types
(`__ManagedMemory`, `__Builtins`), synthetic destructor names (`__destruct_String`),
and parser-generated temporaries (`__discard_*`, `__try_result_*`).

User code MAY still reference these existing internal names (the stdlib does so
extensively to define `String`, `Array`, `Map`, etc.). What user code MAY NOT do is
**declare** a new identifier with that prefix. Any binding site that introduces a
fresh `__`-prefixed name — function, type, typealias, field, parameter, local
variable, enum case, match binding — is a compile-time error.

The check fires at the parser, before any later stage runs, so the error message
points at the offending name's source location.

### Error Example

```maxon
function main() returns ExitCode
	let __value = 5
	return __value
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/docs-example-1.test:3:6: identifier '__value' is reserved: declarations starting with '__' are reserved for compiler internals
```

### Why This Matters

Without the check, user code could collide with runtime symbols (`__gt_spawn`)
or shadow builtin types (`__ManagedMemory`), producing confusing link-time
failures or silently breaking memory management invariants. Reserving the prefix
makes the boundary between user identifiers and compiler internals explicit.

## Tests

<!-- test: let-declaration -->
```maxon
function main() returns ExitCode
	let __foo = 5
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/let-declaration.test:3:6: identifier '__foo' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: var-declaration -->
```maxon
function main() returns ExitCode
	var __counter = 0
	__counter = __counter + 1
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/var-declaration.test:3:6: identifier '__counter' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: function-declaration -->
```maxon
function __helper() returns ExitCode
	return 0
end '__helper'

function main() returns ExitCode
	return __helper()
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/function-declaration.test:2:10: identifier '__helper' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: function-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

function id(__x Integer) returns Integer
	return __x
end 'id'

function main() returns ExitCode
	_ = id(0)
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/function-parameter.test:4:13: identifier '__x' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: type-declaration -->
```maxon
type __Hidden
	export var x as Integer
end '__Hidden'

function main() returns ExitCode
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/type-declaration.test:2:6: identifier '__Hidden' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: type-field -->
```maxon
type Point
	export var __x as Integer
	export var y as Integer
end 'Point'

function main() returns ExitCode
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/type-field.test:3:13: identifier '__x' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: typealias-declaration -->
```maxon
typealias __Score = int(0 to 100)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/typealias-declaration.test:2:11: identifier '__Score' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: enum-case -->
```maxon
enum Color
	red
	__green
	blue
end 'Color'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/enum-case.test:4:2: identifier '__green' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: closure-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

function apply(f Integer, n Integer) returns Integer
	return f + n
end 'apply'

function main() returns ExitCode
	let f = function(__n Integer) gives __n + 1
	_ = f(0)
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/closure-parameter.test:9:19: identifier '__n' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: enum-name -->
The bootstrap spec omits the enum/union NAME (only the enum CASE is covered), but the bootstrap rejects
a `__`-prefixed enum name just as it does every other declaration — measured `E2051 @1:6`. shv2 guards it
at `parseEnumDeclaration`'s name consume, the site distinct from `readEnumCaseInto` (which covers cases).
```maxon
enum __Color
	red
	green
end '__Color'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/enum-name.test:2:6: identifier '__Color' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: union-name -->
The same guard covers a `union` name — `parseEnumDeclaration` handles both via `isUnion`, so ONE
`requireUnreservedName` call reserves both.
```maxon
union __Shape
	circle
	square
end '__Shape'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/union-name.test:2:7: identifier '__Shape' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: user-file-named-builtins-is-not-exempt -->
`stdlib/Builtins.maxon` is the ONE file whose declarations may carry the reserved prefix — it DECLARES
the reserved space (the error enums a builtin throws, the parse helpers `int.fromString` rewrites to)
rather than merely using it, and shv2 exempts it at `requireUnreservedName` exactly as the bootstrap
exempts it at `CheckReservedDeclName` (D6).

This is the NEGATIVE CONTROL on that exemption, and it is the half that matters: the exemption is keyed
on the FILE'S IDENTITY (`<stdlibDir>/Builtins.maxon`, both sides resolved), not on its basename. Keyed on
the basename, any program could opt out of the reservation for a whole file by naming it `Builtins.maxon`
— and every guarantee that leans on "no user `__` name can be declared" would leak through it.

⚠ The POSITIVE direction used to be unpinnable, and A1s is what changed that. A test compiles a throwaway
project, so it can never CONTAIN the checkout's own `stdlib/Builtins.maxon` — but it no longer has to:
that module is now LISTED by the stdlib loader, so it is loaded into every compile, and a fragment can
simply NAME one of the `__`-prefixed types it declares. `reserved-space-module-declarations-reach-a-user-program`
below is that pin, and it deliberately names `__ManagedListError` — a type the compiler synthesizes
nowhere, so nothing but the module's own declaration can be answering.

⚠⚠ **AND THAT UNPINNABILITY HAS ALREADY COST ONE DEFECT, SO IT IS A STANDING INSTRUCTION, NOT A FOOTNOTE.**
Every reader that used to read *"`__`-prefixed"* as *"the compiler emitted this"* is wrong for a name this
module declares. A1r converted five such readers and left three; the A1r review found one of the three
reachably wrong, and only wasm could see it:

```
# append `export function __probeSub(a int, b int) returns int` + a `main` doing
# `let f = __probeSub` / `return f(50, 8) as ExitCode` to stdlib/Builtins.maxon, then:
maxon-shv2 build stdlib/Builtins.maxon -o out --target=wasm32-wasi
vendor/wasmtime/wasmtime run -S cli-exit-with-code=y out.wasm
```

`wasm trap: indirect call type mismatch`, exit **3** — against **42** for the identical program with the
`__` dropped from the name. `lowerFunctionRef` took the target's BARE address instead of its `__fnref_`
env-thunk, so an `(a, b) -> R` function was called through the uniform `(a, b, env) -> R` signature. **x64
answered 42 either way**: the extra `env` word lands in an argument register the callee never reads, so the
wrong-shaped call returned the right number. ⇒ **When you touch this exemption, sweep every reader of
`isCompilerInternalCallee` and run the sweep's result on `--target=wasm32-wasi`, which is the only local
target that type-checks an indirect call.** The three fixed readers now share ONE predicate
(`MmRuntime.isSignaturelessCompilerCallee`) with the five, so a future edit cannot move one reader's answer
without moving all of them.

⚠ **THAT TRANSCRIPT IS STILL A TRANSCRIPT, AND IT NO LONGER HAS TO BE.** The `// --- stdlib-overlay:`
fixture at the end of this file can stage exactly that program — a `__`-named function taken as a VALUE —
and `--target=wasm32-wasi` is where a wrong answer shows. No case is written for it yet; the fixture is
what a case would be written with.

**A1t IS THE THIRD SUCH FINDING, AND IT IS THE ONE THE PREFIX EXEMPTION MAKES POSSIBLE RATHER THAN MERELY
MISREADS.** A name this module declares may be a name the compiler *itself emits* — and then the program
holds two functions of one name, which no linker and no name index can resolve. Reachable only through the
real module, which is why it was first found by hand:

```
# append `export function __print_string(value String)` (empty body) + a `main` that CALLS it
# to stdlib/Builtins.maxon, then:
maxon-shv2 build stdlib/Builtins.maxon -o out
```

⇒ **THE TRANSCRIPT IS NOW A CASE** — `error.stdlib-overlay-print-string-collides-at-the-std-tier`, and its
twin `stdlib-overlay-print-string-alone-is-legal` for the half of the rule the transcript states in the
paragraph below. The `// --- stdlib-overlay:` fixture at the end of this file is what closed the gap: the
findings recorded here were reproducible but not RE-RUNNABLE, which is a check that cannot fail. (The empty
body in the transcript no longer compiles on its own — an unused parameter is E3012 — so the case gives
`value` a use; the collision is on the NAME and does not care.)

Before A1t: `panic at DeadFunctionElimination.maxon:110: indexFunctionsByName: two functions are named
'__print_string'` — no file, no line, and **both of the causes the message named were false here** (it is
not E3006 at merge, which only ever compares two *parsed* declarations, and no installer ran twice). After:
`error E4015: stdlib\Builtins.maxon:337:17: declaration of '__print_string' collides with a symbol the
compiler EMITS into this program …`.

⚠ **WHAT CREATES THE PAIR DEPENDS ON THE RUNTIME, so "just don't call it" is not the rule it looks like.**
For a USAGE-GATED symbol the CALL is what installs the compiler's own copy, so the declaration alone is
clean (`__print_string`, above — the door A1r opened). But `__module_init` / `__maxon_global_cleanup` are
installed on `globals.count() != 0` alone, so declaring one of THOSE collides with **no call anywhere** —
measured, same E4015, from a `Builtins.maxon` carrying one managed global and an uncalled
`export function __module_init()`, and now pinned by
`error.stdlib-overlay-module-init-collides-with-the-compilers-own`. ⇒ **Do NOT cure this by gating the call or steering it to the surviving definition** — that trades
a loud refusal for a silent miscompile, since `print("x")` would then bind to the author's declaration
instead of the runtime's, which is the one that knows the String record's layout. The refusal is at the
DUPLICATE, and it is total (no binary is written either way). ⇒ **And the panic did not go away, it got a
DISCRIMINATOR**: only a pair with MIXED provenance (one parsed, one synthesized) is a user error; a pair
that is both-synthesized or both-parsed is a compiler bug and still panics, naming its own cause. Keep that
split if you touch `FunctionNameIndex.refuseDuplicateFunctionName` — a fix that made every duplicate a user
diagnostic would hide an installer that ran twice, and would pass any test written only against the collision.

**A1w IS THE FOURTH, AND IT IS THE SAME RULE THROUGH A DOOR WITH NO PAIR TO COUNT.** A1t's refusal notices
TWO functions of one name, so it is blind to a declaration of a name the compiler owns but does not EMIT
into this particular program — and `DeadFunctionElimination.seedRoots` roots four such names
(`__module_init`, `__maxon_global_cleanup`, `__mm_leak_check`, `__gt_enqueue`) into *every* program's
reachability set. Found by hand for the same reason as the three above, and now pinned by
`error.stdlib-overlay-mm-leak-check-takes-a-rooted-name`:

```
# append `export function __mm_leak_check()` (empty body) to stdlib/Builtins.maxon, then compile
# ANY program that carries no heap, e.g. `function main() returns ExitCode / return 0 / end 'main'`:
maxon-shv2 build main.maxon -o out
```

Before A1w: `panic at DeadFunctionElimination.maxon:111: requireUnreachableStdlibStayedDead:
'__mm_leak_check' is in StdlibFacts.unreachable …` — no file, no line, and the message offered two causes of
which only one was the compiler's fault. MEASURED identically for `__module_init` and `__gt_enqueue`. After:
`error E4015: stdlib\Builtins.maxon:337:17: declaration of '__mm_leak_check' takes a name the compiler
owns …`, positioned at the declaration.

⚠ **REFUSED WHETHER OR NOT THE COMPILER EMITS ITS OWN COPY HERE, AND THAT IS THE RULING RATHER THAN AN
IMPLEMENTATION ACCIDENT.** Simply not rooting a parsed hit would COMPILE — the declaration is dead code and
would be pruned — but then `__module_init` would be legal in a program with no managed global and E4015 in
one with a managed global: the legality of a NAME decided by which runtime floor some other program happens
to carry. ⇒ **The rule is about the name.** Both detections raise the one code (E4015,
`IrDeclarationTakesCompilerOwnedName`), because a second number for one rule is the same disease one level
up. ⇒ **And `main` carries the OPPOSITE premise at the same root set** — it must be the author's — so the two
kinds of root deliberately do not share a door; kind 3 (`.rdata` slots) checks neither, because a witness
slot legitimately names a PARSED `Type.method`.
```maxon
// --- file: Builtins.maxon
export enum __ParseError implements Error
	invalidFormat
end '__ParseError'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: <fragment>:3:13: identifier '__ParseError' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: user-file-cannot-call-a-reserved-name -->
**THE CALL SIDE OF THE SAME EXEMPTION (A1r).** D6 opened the DECLARATION door for `stdlib/Builtins.maxon`
and left the CALL door shut for every file including that one, so the module could declare
`__int_fromString` and then not call it. A1r opens the call door for exactly the file that declares the
name — and this is the pin that it opened no wider.

`__int_fromString` is a name the real `stdlib/Builtins.maxon` genuinely DOES declare, which is what makes
this case distinct from `builtins-clock.unknown-internal-callee`'s `__whatever`: the refusal here is not
"no such name anywhere" but "not a name YOUR file may write". The reserved-prefix wording is deliberate
and stays — it tells the author the prefix is the problem, where the bootstrap gives a source-written `__`
call the same generic *"Undefined function"* any typo gets (`2-Parser.cs:20373`).

⚠ It also narrows the paragraph above: in shv2 a `__` name may be REFERENCED from exactly one file, not
from user code generally. `String`/`Array`/`Map` are synthesized builtins here rather than stdlib sources,
so no other file has a reason to reach for one.
```maxon
function main() returns ExitCode
	return __int_fromString("42") as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:9: call to undefined function '__int_fromString': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: user-file-named-builtins-cannot-call-a-reserved-name -->
**THE NEGATIVE CONTROL ON THE CALL-SIDE EXEMPTION, and it is the half that matters** — the exact mirror of
`user-file-named-builtins-is-not-exempt` one door over. The exemption is keyed on the FILE'S IDENTITY
(`<stdlibDir>/Builtins.maxon`, both sides resolved), and the BASENAME test in front of that compare is a
prefilter, never the answer. Keyed on the basename, any program could reach every compiler internal —
`__mm_free`, `__gt_spawn` — by naming one of its own files `Builtins.maxon`.

Both sides of one rule are therefore pinned the same way, because a rule pinned on one side is a rule that
can lapse on the other: that asymmetry is what D6 shipped.
```maxon
// --- file: Builtins.maxon
function main() returns ExitCode
	return __int_fromString("42") as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:4:9: call to undefined function '__int_fromString': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: user-file-named-builtins-cannot-call-a-runtime-entry-point -->
**THE SHAPE WITH NO DIAGNOSTIC WAITING BEHIND IT.** The two cases above name a stdlib helper, so a widened
door lands them on `resolveCallFixups`' *"call to unknown function"* — a panic, but a loud one. `__mm_free`
is worse in the one way that matters: it is a symbol the emitted runtime really HAS, and naming it is
itself what pulls the heap runtime into the image (`MmRuntime.isRuntimeCallee`). So a widened door does not
fail to link here — it links, and frees address 0.

Nothing declares a runtime entry point (they are built at the Std tier, after the merge that fills
`funcSignatures`), which is why the exemption is "a name some file DECLARES" and not merely "this file may
write `__`": the second reading would hand this module every entry point in the runtime, unvalidated and
un-arity-checked. See `Parser.requireCalleeIsNotReservedName`.
```maxon
// --- file: Builtins.maxon
function main() returns ExitCode
	__mm_free(0)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:4:2: call to undefined function '__mm_free': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: user-file-named-builtins-cannot-async-call-a-reserved-name -->
**THE SECOND DOOR.** There are exactly two source call doors — `Parser.emitCall` and
`Parser.emitAsyncCall` — and `async __name(…)` reaches the second one from ordinary user source
(`builtins-clock.unknown-internal-callee-async` is the plain-user-file half of it). The exemption is
plumbed into the one decision function both doors call, so the file test must hold at both; pinned at only
one, a spawn would be the way around it.
```maxon
// --- file: Builtins.maxon
function main() returns ExitCode
	let p = async __int_fromString("42")
	return await p as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:4:16: call to undefined function '__int_fromString': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: reserved-space-module-declarations-reach-a-user-program -->
**THE POSITIVE CONTROL ON THE DECLARATION-SIDE EXEMPTION (A1s), and it is the half every negative case
above assumes.** The four cases before this one prove the exemption is not WIDER than one file. None of
them proves it is not EMPTY: a compiler that refused `stdlib/Builtins.maxon`'s own declarations too would
pass every one of them.

It became pinnable when the stdlib loader listed that module. The fragment cannot contain the checkout's
`Builtins.maxon` — it never could — but it no longer needs to, because that file is loaded into this
compile like any other stdlib source, and its `__`-prefixed declarations are therefore in scope here.

⚠ **IT NAMES `__ManagedListError` ON PURPOSE.** Its `__Managed{Memory,File,Directory}Error` siblings would
prove less: those three USED to be built by the compiler as well as declared in that file, and A1s deleted
the compiler's copies precisely because two spellings of one wire format is the shape that drifts silently.
`__ManagedListError` was only ever declared, by `stdlib/Builtins.maxon:144`, and no line of the compiler
mentions it — so a `match` that names its four cases exhaustively can be answered by nothing else. The
`nodeNotInList` arm is third, which is also a reading of the declaration's ORDER and not merely its
membership.
```maxon
function rank(e __ManagedListError) returns ExitCode
	return match e 'which'
		empty gives 1
		endOfList gives 2
		nodeNotInList gives 3
		nodeAlreadyInList gives 4
	end 'which'
end 'rank'

function main() returns ExitCode
	return rank(__ManagedListError.nodeNotInList)
end 'main'
```
```exitcode
3
```

<!-- test: reserved-space-functions-reach-a-user-program-through-their-primitive-static-spelling -->
**THE POSITIVE CONTROL ON THE *CALL*-SIDE RULE, AND IT IS THE HALF THE FOUR REFUSALS ABOVE LEAVE
UNSTATED.** `user-file-cannot-call-a-reserved-name` pins that `__int_fromString("42")` is refused from
ordinary source. On its own that reads as *"the reserved free functions are unreachable"*, and they are
not: `stdlib/Builtins.maxon` marks three of them `public` (`__float_bitsFromText`,
`__float_textFromBits`, `__int_fromString`) precisely so that a program CAN reach them — through the
PRIMITIVE-STATIC spelling, which is the surface, where `__` is the mangling.

⇒ **The rule is about the NAME the author wrote, not about the function.** `float.textFromBits(bits)`
mints exactly the callee `__float_textFromBits` that the reserved spelling would have named
(`Parser.primitiveStaticCallee`), meets `requireCalleeIsNotReservedName`'s `primitiveStatic` mint
exemption, and links to the same body. Two spellings, one function, opposite verdicts — because one of
them is bytes the author typed into the reserved space and the other is not.

⚠ **THE COMPILER ITSELF IS A CALLER, WHICH IS WHY THIS IS PINNED RATHER THAN MERELY TRUE.**
`Compiler/IR/Maxon/TypeRules.formatBound` renders the f64 bounds of a ranged float alias into an E3005
sentence, and `Compiler/Parser.floatLiteralBits` decodes every float literal — both through this
surface. shv2 compiling its own source is an ordinary program calling ordinary stdlib functions, and it
gets no exemption from this file's rule; it spells the surface instead.

⚠ The rendered digits are the BOOTSTRAP's notation and not `float.toString`'s — `100` and not `100.0`,
and `-0` keeping its sign. `stdlib/Builtins.maxon`'s FLOAT TEXT band states why the two spellings are
deliberately different, and `ranged-typealias.md` pins the same digits as they reach an E3005 message.
```maxon
typealias FBits = int(i64.min to i64.max)

function main() returns ExitCode
	print("{float.textFromBits(4636737291354636288 as FBits)} {float.textFromBits(4614253070214989087 as FBits)} {float.textFromBits(-9223372036854775808 as FBits)}")
	match float.bitsFromText("3.14") 'decoded'
		bits(value) then return 7 if value == 4614253070214989087 else 1
		overflow then return 2
		malformed then return 3
	end 'decoded'
end 'main'
```
```stdout
100 3.14 -0
```
```exitcode
7
```

<!-- test: error.the-reserved-spelling-of-a-primitive-static-stays-shut -->
**THE NEGATIVE HALF OF THE CASE ABOVE, on the SAME function.** Reaching `__float_textFromBits` by the
name `stdlib/Builtins.maxon` wrote is the author reaching into the reserved space, and it is refused —
so the positive control cannot be read as "the door opened". It is `user-file-cannot-call-a-reserved-name`
one function over, stated here because this is the pair that makes the mint exemption's shape legible.
```maxon
typealias FBits = int(i64.min to i64.max)

function main() returns ExitCode
	print("{__float_textFromBits(4636737291354636288 as FBits)}")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:5:10: call to undefined function '__float_textFromBits': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

### The `// --- stdlib-overlay:` fixture — the four E4015 doors that used to be reachable only by hand

⭐⭐ **THREE OF THIS FILE'S OWN FINDINGS WERE RECORDED AS SHELL TRANSCRIPTS BECAUSE NOTHING IN THE SUITE
COULD REACH THEM, AND A CHECK THAT CANNOT FAIL IS NOT A CHECK.** Every route to E4015 through
`FunctionNameIndex` or through `DeadFunctionElimination.seedRoots` needs a DECLARATION carrying the
reserved prefix, and exactly one file may write one — `<stdlibDir>/Builtins.maxon`, by IDENTITY. A spec
fragment is a throwaway file in a scratch directory, so it can never BE that file, and the negative
controls above are what prove it cannot fake it.

⇒ the harness now stages a private stdlib for the case that asks for one. A
`// --- stdlib-overlay: <path under stdlib/>` section names a stdlib file; the runner copies the compiler
under test and the whole of ITS `stdlib/` into a directory of the case's own, writes the section's text at
the TOP of the named copy, and compiles with the copied binary — which locates `stdlib/` by walking up
from its own path (`StdlibSource.locateStdlibDir`), so the copy is the one it loads. The checkout's
`stdlib/` is never written to, and no two cases can see each other's overlay.

⚠ **THE SECTION IS PLACED AT THE TOP OF THE FILE, AND THAT IS A POSITION CONTRACT, NOT AN IMPLEMENTATION
DETAIL.** A diagnostic in the overlay must name a line the READER can find, and the only line number that
does not move when `stdlib/Builtins.maxon` gains or loses a line is one counted from the file's start. So
the overlay's own line 1 is the copy's line 1, and it normalizes to the merged fragment exactly as a
`// --- file:` section does. shv2 sweeps every file's declarations before parsing any of them, so nothing
about a stdlib module depends on where in the file a declaration sits.

<!-- test: stdlib-overlay-declaration-reaches-the-user-program -->

⭐ **THE POSITIVE CONTROL ON THE FIXTURE ITSELF, and every case below is worthless without it.** A runner
that staged the overlay and then compiled with the ORIGINAL binary — or that dropped the section on the
floor, which is precisely what it did before the marker existed — would leave the three refusals below
passing for a reason that has nothing to do with an overlay: an unknown type refuses a program too. This
case can only be answered by the overlay actually being the `Builtins.maxon` this compile loaded, and it
answers 42 rather than merely compiling.
```maxon
// --- stdlib-overlay: Builtins.maxon
export enum __OverlayProbe
	first
	second
end '__OverlayProbe'
// --- file: main.maxon
function rank(e __OverlayProbe) returns ExitCode
	return match e 'which'
		first gives 1
		second gives 42
	end 'which'
end 'rank'

function main() returns ExitCode
	return rank(__OverlayProbe.second)
end 'main'
```
```exitcode
42
```

<!-- test: error.stdlib-overlay-module-init-collides-with-the-compilers-own -->

⭐⭐ **THE MAXON-TIER `FunctionNameIndex` DOOR (A1t), AND IT NEEDS NO CALL ANYWHERE.** `__module_init` is
installed on `globals.count() != 0` alone (`ModuleInit`), so a program with ONE managed global holds the
compiler's copy and this declaration both — a pair with MIXED provenance, which is the one shape
`refuseDuplicateFunctionName` reports rather than panics. The index that meets it first is a Maxon-tier
one (`StdlibSource.reachableMaxonFunctionNames` / `IR/PassPipeline.run`), upstream of every runtime
installer. Drop the `var` and the program has no managed global, no `__module_init` is installed, and the
next case's door — not this one — is what answers.
```maxon
// --- stdlib-overlay: Builtins.maxon
export function __module_init()
end '__module_init'
// --- file: main.maxon
var banner = "hello"

function main() returns ExitCode
	return banner.byteLength() as ExitCode
end 'main'
```
```maxoncstderr
error E4015: <fragment>:3:17: declaration of '__module_init' collides with a symbol the compiler EMITS into this program, and a program cannot hold two functions of one name. The compiler's own definition is the one its emitted code is bound to — the entry stub's calls, the runtime's own call sites, and every call it lowers to that name — so the declaration is the side that must move: rename it
```

<!-- test: error.stdlib-overlay-write-stdout-collides-at-the-std-tier -->

⭐ **THE STD-TIER `FunctionNameIndex` DOOR — the one A1t was written for, and the only one no earlier
index can reach.** `__write_stdout` is USAGE-GATED: the compiler installs its own copy because the program
calls `print` (whose corpus body's only floor is `__Builtins.writeStdout`), and that installer runs after
the whole Maxon tier, so the pair first exists at `DeadFunctionElimination`'s index.

⚠ **THE EXEMPLAR WAS `__print_string` UNTIL W35, AND IT HAD TO MOVE BECAUSE THE SYMBOL DID.** That entry
point existed only to serve a bare-name `print` builtin; retiring the builtin retired it, and a case naming
a symbol nothing emits would pin nothing while still passing — which is the failure mode this whole file is
about. `__write_stdout` is its successor at the same tier through the same gate, so the case is the same
statement about the same door. (The empty body in the A1t transcript no longer compiles on its own — an
unused parameter is E3012 — so the case gives `value` a use.)

⚠⚠ **THE DECLARATION MUST CARRY THE SIGNATURE THE COMPILER'S OWN EMITTED CALL PASSES, and getting that
wrong moves the answer to a DIFFERENT DOOR** — the sentence the retired version of this case made about
arity, MEASURED again here about types. A declaration exists, so `SemanticCheck` stops skipping the
parser-emitted `__write_stdout` call inside `stdlib/Print.maxon` and type-checks it; a signature that does
not match is `E3005` at `stdlib/Print.maxon:10` and never reaches the tier this case is about.

⇒ **SO THIS CASE ALSO PINS THE BYTE-VIEW FOLD, from the one angle a spec can see it.** The emitted call
passes the `String` ITSELF, because `foldByteViewIntoStreamWrite` rewrites `__str_bytes_view`'s call into
the write rather than letting the view be built — so `(value String)` is what type-checks here. Before the
fold the argument was the view and this declaration had to be `(value __ManagedMemory)`; MEASURED, with the
buffer signature it is now `E3005 … expected 'ByteArray', got 'String'`. Undo the fold and this case fails.
```maxon
// --- stdlib-overlay: Builtins.maxon
export typealias WrittenByteCount = int(0 to u64.max)

export function __write_stdout(value String) returns WrittenByteCount
	return value.byteLength()
end '__write_stdout'
// --- file: main.maxon
function main() returns ExitCode
	print("hi")
	return 0
end 'main'
```
```maxoncstderr
error E4015: <fragment>:5:17: declaration of '__write_stdout' collides with a symbol the compiler EMITS into this program, and a program cannot hold two functions of one name. The compiler's own definition is the one its emitted code is bound to — the entry stub's calls, the runtime's own call sites, and every call it lowers to that name — so the declaration is the side that must move: rename it
```

<!-- test: stdlib-overlay-write-stdout-alone-is-legal -->

⭐ **THE OTHER HALF OF THE USAGE GATE, and it is what makes the case above a statement about the PAIR
rather than about the prefix.** The same `__write_stdout` declaration, in a program that never prints: no
installer runs, there is no second `__write_stdout`, the declaration is unreachable and is pruned. A rule
that refused the NAME would refuse this too — and that is exactly the rule A1w applies to the four ROOTED names, which is
why the next case's message is a different sentence about a different thing.

⚠ It is also the pin that `stdlib/Print.maxon` being LISTED (W35) did not quietly widen the runtime floor:
the corpus module is loaded into this program like every other, and `__write_stdout` is STILL not installed,
because `scanRuntimeUsage` skips a stdlib body no path from `main` reaches (`StdlibFacts.unreachable`).

⚠ The exit code is routed through a SECOND declaration in the same overlay, and that is deliberate: a
program returning a bare `42` would compile with the overlay dropped on the floor, so the case would go on
passing for a compiler that had never staged one. What a case asserts has to be unavailable to a run that
skipped the mechanism.
```maxon
// --- stdlib-overlay: Builtins.maxon
export typealias WrittenByteCount = int(0 to u64.max)

export function __write_stdout(value String) returns WrittenByteCount
	return value.byteLength()
end '__write_stdout'

export enum __UnprintedProbe
	answer
end '__UnprintedProbe'
// --- file: main.maxon
function main() returns ExitCode
	return match __UnprintedProbe.answer 'which'
		answer gives 42
	end 'which'
end 'main'
```
```exitcode
42
```

<!-- test: error.stdlib-overlay-mm-leak-check-takes-a-rooted-name -->

⭐ **THE A1w DOOR — the same rule where there is NO PAIR TO COUNT.** `__mm_leak_check` is one of the four
names `DeadFunctionElimination.seedRoots` roots into EVERY program's reachability set, so a heap-free
program that installs no memory runtime still roots it — at the declaration, whose body the earlier passes
were told was unreachable and never lowered. Refused for the NAME, and the message says so: the legality
of a name must not turn on which runtime floor some other program happens to carry.
```maxon
// --- stdlib-overlay: Builtins.maxon
export function __mm_leak_check()
end '__mm_leak_check'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E4015: <fragment>:3:17: declaration of '__mm_leak_check' takes a name the compiler owns: it roots that name into every program's reachability set, for a function it emits itself. This program installs no runtime under it, so the declaration is what the root would keep — with an EMPTY body, since the passes that lower bodies were told the name was unreachable. Rename the declaration
```
