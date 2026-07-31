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
	export var x as int
end '__Hidden'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/type-declaration.test:2:6: identifier '__Hidden' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: type-field -->
```maxon
type Point
	export var __x as int
	export var y as int
end 'Point'

function main() returns ExitCode
	return 0
end 'main'
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

<!-- disabled-test: closure-parameter -->
<!-- P1.5 closures -->
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

⚠ The POSITIVE direction is deliberately not pinned here, because a spec cannot express it: a test
compiles a throwaway project, so it can never contain the checkout's own `stdlib/Builtins.maxon`. That
module's load IS the positive case, and it arrives when the stdlib whitelist can list it — see the notes
on `specs-shv2/parsable-interface.md`, which name the fallible-division rung that still blocks it.

⚠⚠ **AND THAT UNPINNABILITY HAS ALREADY COST ONE DEFECT, SO IT IS A STANDING INSTRUCTION, NOT A FOOTNOTE.**
Every reader that used to read *"`__`-prefixed"* as *"the compiler emitted this"* is wrong for a name this
module declares, and NO case below can go red for any of them — they are reachable only through the real
module. A1r converted five such readers and left three; the A1r review found one of the three reachably
wrong, and only wasm could see it:

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
(`MmRuntime.isSignaturelessCompilerCallee`) with the five, which is the strongest pin available here: a
future edit can no longer move one reader's answer without moving all of them.
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
