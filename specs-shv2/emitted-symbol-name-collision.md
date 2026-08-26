---
feature: emitted-symbol-name-collision
status: stable
keywords: [diagnostics, naming, compiler-internals, linking, backend]
category: diagnostics
---

# A declaration may not take a name the compiler LAYS OUT

## Documentation

`specs-shv2/reserved-double-underscore.md` reserves the `__` prefix at the PARSER, and A1t/A1w refuse a
declaration that collides with a compiler-emitted **module function** (E4015, at `FunctionNameIndex`). Both
of those doors see only names a *module* carries.

**The compiler-owned name space is larger than either door.** Three kinds of body reach `.text` without
ever being a module function, and all three are minted at the TARGET tier, after the last name index any
Std pass can build:

| minted by | examples | on |
|---|---|---|
| the entry stub | `mrt_start` | EVERY native target (x64 `augmentWithRuntime`, arm64 `appendArm64RuntimeChunks`) |
| hand-assembled runtime chunks | `mrt_runtime_init`, `mrt_panic`, `mrt_fault_handler`, `__gt_context_switch` | **x64 ONLY** — the arm64 scalar core allocates nothing and has no fault handler, so it hand-assembles NO chunk but the stub (`Arm64Runtime`'s header) |
| the symbol table that closes `.text` | `__symtable` | x64 only |

None of those wears a `__` prefix except the last two, so the parser's reservation does not cover them, and
none of them is in `stdModule.functions`, so `FunctionNameIndex` cannot either. The concat pass keys every
one of them into a **name → `.text` offset** map that `resolveCallFixups` then resolves every direct call
through.

⚠ **A bare last-wins `upsert` there does not degrade an answer — it BINDS EVERY CALL TO THE OTHER BODY.**
MEASURED, before A2l: the program under `runtime-chunk-name` below compiled clean, linked, ran, and
**exited 127** where its source says 7. The user's `mrt_runtime_init` was laid out, the hand-assembled
panic-runtime chunk of the same name was laid *after* it, last-wins overwrote the label, and the program's
call to its own function silently reached the runtime chunk. No diagnostic anywhere. `mrt_start` was worse
only in being loud: it tripped the entry-offset guard and PANICKED the compiler on a plain user program,
which is not a diagnostic either.

**The rule: a `.text` label is claimed exactly once, and a second claim is refused at the point of layout.**
It is the SAME code as A1t's — **E4015**, positioned at the declaration, with the same cure (rename it) —
because it is the same rule: a declaration took a symbol the compiler emits, and the compiler's copy is the
one its own emitted code is bound to.

### Why it is DETECTED at layout rather than PREDICTED upstream

A1t's finding was that the collision never needed predicting because it was already detectable, and its
proposed cure — an up-front roster of every name the compiler might emit — was both impossible and
unnecessary. The same holds one tier down, and more sharply: **which chunks exist depends on the target and
on `RuntimeUsage`.** arm64 emits no `__symtable`; the `__gt_*` chunks appear only under `usesGt`; wasm
synthesizes its `run` entry with no name key at all and has no hand-assembled chunks whatsoever. A roster
would therefore be one list per (target × usage) combination, maintained by hand, with nothing between it
and the truth.

⚠ **So the diagnostic is TARGET-SCOPED, and that is a reading of where the collision exists rather than a
gap — but the scope is PER NAME, not "native vs wasm".** Each case's `<!-- targets: -->` marker names the
targets that actually lay a chunk down under *its* name, and the two below differ:

| name | x64-windows | x64-linux | arm64-macos | arm64-linux | wasm32-wasi |
|---|---|---|---|---|---|
| `mrt_start` | chunk | chunk | chunk | chunk | no chunk (the `run` entry has no name key) |
| `mrt_runtime_init` | chunk | chunk | **no chunk** | **no chunk** | no chunk |

⛔⛔ **`runtime-chunk-name`'s marker USED TO NAME ALL FOUR NATIVE TARGETS, AND ITS ARM64 HALF WAS FALSE.**
MEASURED 2026-07-31 by cross-compiling the case below: `maxon-shv2 build … --target=arm64-macos` and
`--target=arm64-linux` both print `Compiled ->` and exit 0 — there is no `mrt_runtime_init` on arm64 to
collide with, exactly as `Arm64Runtime`'s own header says ("no fault handler and no `mrt_runtime_init` to
call before user code. The whole runtime is therefore this one stub"). The two arm64 lanes are REMOTE and
so were never run, which is precisely why the marker could be widened past where the code stands and stay
green. `entry-stub-name` is the case that legitimately spans all four, and it is what keeps arm64's
`recordChunkLabel` covered.

## Tests

<!-- test: runtime-chunk-name -->
<!-- targets: x64-windows, x64-linux -->
The measured silent miscompile: this exact program returned **127** instead of **7** before A2l, with the
build exiting 0 — re-measured with the refusal removed from `recordChunkLabel`, 2026-07-31, still **127**.

⚠ **x64 ONLY, and the omission of arm64 is the reading above, not a lane that was skipped**: arm64 lays no
`mrt_runtime_init` chunk, so on those two targets this program holds no collision and compiles.
⚠ **THE BODY IS REACHED THROUGH A FUNCTION VALUE, AND THAT IS LOAD-BEARING (EC5, 2026-08-26).** The rule is about a body the
compiler LAYS DOWN, and a direct call to a one-line function no longer guarantees one: `inlineLeaves` splices the
leaf into `main`, `dfe` drops the now-uncalled declaration, no second body exists under the label, and the program
compiles and returns 7 — which is the rule's own correct answer for a body that is never emitted. Taking the
function as a VALUE roots it (`funcAddr` is a liveness root) and a call through a value is never inlined, so the
collision this case pins is the one that actually reaches layout.
```maxon
typealias Integer = int(i64.min to i64.max)

function mrt_runtime_init() returns Integer
	return 7
end 'mrt_runtime_init'

function main() returns ExitCode
	let emitted = mrt_runtime_init
	return emitted() as ExitCode
end 'main'
```
```maxoncstderr
error E4015: <fragment>:4:10: declaration of 'mrt_runtime_init' collides with a symbol the compiler EMITS into this program, and a program cannot hold two bodies under one `.text` label. The compiler's own definition is the one its emitted code is bound to — the entry stub's calls and the runtime's own call sites — so the declaration is the side that must move: rename it
```

<!-- test: entry-stub-name -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The ENTRY STUB's own name — **the case that spans all four native targets**, because every one of them
lays a `mrt_start` chunk down (x64 as a module function via `prependEntryStub`, arm64 as a hand-assembled
chunk via `appendArm64RuntimeChunks`), and therefore the one that keeps arm64's half of the shared
`recordChunkLabel` covered. Verified by cross-compiling: E4015 on x64-windows, x64-linux, arm64-macos and
arm64-linux alike. Before A2l this reached the entry-offset guard — `panic at X64Backend.maxon:
concatX64FunctionChunks: 'mrt_start' must be laid out FIRST in .text but is at offset 33` — a compiler
panic, with no file and no line, on a program whose only fault is a name. The refusal now fires at the
label, which is one chunk EARLIER than the guard, so the guard keeps its own meaning (the stub was
appended in the wrong ORDER) rather than doubling as a collision report.
⚠ **THE BODY IS REACHED THROUGH A FUNCTION VALUE, AND THAT IS LOAD-BEARING (EC5, 2026-08-26).** The rule is about a body the
compiler LAYS DOWN, and a direct call to a one-line function no longer guarantees one: `inlineLeaves` splices the
leaf into `main`, `dfe` drops the now-uncalled declaration, no second body exists under the label, and the program
compiles and returns 7 — which is the rule's own correct answer for a body that is never emitted. Taking the
function as a VALUE roots it (`funcAddr` is a liveness root) and a call through a value is never inlined, so the
collision this case pins is the one that actually reaches layout.
```maxon
typealias Integer = int(i64.min to i64.max)

function mrt_start() returns Integer
	return 7
end 'mrt_start'

function main() returns ExitCode
	let emitted = mrt_start
	return emitted() as ExitCode
end 'main'
```
```maxoncstderr
error E4015: <fragment>:4:10: declaration of 'mrt_start' collides with a symbol the compiler EMITS into this program, and a program cannot hold two bodies under one `.text` label. The compiler's own definition is the one its emitted code is bound to — the entry stub's calls and the runtime's own call sites — so the declaration is the side that must move: rename it
```

<!-- test: an-unemitted-lookalike-name-is-legal -->
**THE NEGATIVE CONTROL, and it is the half that decides what the rule is ABOUT.** The two refusals above
would both pass under a rule that simply reserved the `mrt_` prefix — and that rule would be wrong: it
would refuse a name no chunk claims, on every target, for a collision that does not exist.

The rule is about a name the compiler ACTUALLY LAYS DOWN in *this* image, which is why it is asked at the
point of layout and answered from the chunk list rather than from a prefix. `mrt_not_a_chunk` is spelled
exactly like its two neighbours and is refused by nothing, so it compiles and runs on every target — this
one carries no `targets:` marker for that reason.
```maxon
typealias Integer = int(i64.min to i64.max)

function mrt_not_a_chunk() returns Integer
	return 7
end 'mrt_not_a_chunk'

function main() returns ExitCode
	return mrt_not_a_chunk() as ExitCode
end 'main'
```
```exitcode
7
```
