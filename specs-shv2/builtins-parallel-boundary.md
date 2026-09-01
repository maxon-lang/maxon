---
feature: builtins-parallel-boundary
status: stable
keywords: [builtins, __Builtins, parallelBoundary, intrinsics, async, green-threads, E3073]
category: system
---

# The `__Builtins.parallelBoundary` intrinsic

## Documentation

`__Builtins` is the compiler's builtin TYPE, whose static methods are INTRINSICS rather than
functions any file declares (see `builtins-clock.md` for the three clock members and
`builtins-sleep.md` for the fourth). `parallelBoundary` takes NOTHING, returns VOID, and — today —
DOES NOTHING at run time.

That is not a placeholder. Its whole job is to be a MARKER a hand-written CPU-BOUND function can put
in its body to say *"spawning me is deliberate"*, without claiming I/O it does not do:

```text
function task(idx Integer, args StringArray, churn Integer) returns Integer
	__Builtins.parallelBoundary()
	...
end 'task'
```

### Why a marker is needed at all — the E3073 contract

`async f()` exists to overlap WAITING with other work, so a spawn of a function that can never give up
the green thread is refused (`E3073`, `async-await.md`). A CPU-bound task function legitimately never
waits: it computes. Under the plain rule it could only be spawned by pretending to do I/O — a
`sleep(0)`, a stat of a file it does not read — which buys a real syscall to satisfy a check.

`parallelBoundary` is the honest spelling of that intent. It compiles to a bare call to a runtime
entry point with an empty body, and the emitted program pays one call and one return for it.

⚠⚠ **AND SINCE EC10 THE MARKER BUYS NO PARALLELISM WHATEVER, WHICH IS WORTH SAYING OUT LOUD BECAUSE THE
NAME SUGGESTS OTHERWISE.** ⚖ An `async` call creates a COROUTINE of the calling green thread (user
ruling, 2026-08-27), so a CPU-bound function marked with this and spawned with `async` runs to
completion on the caller's own OS thread, at the point the driver reaches it — sequentially, exactly as
a direct call would, plus a coroutine's stack and switch. What the marker does is satisfy E3073 for a
function that neither waits nor yields, and that is ALL it does. ⇒ **its natural future is as the marker
on a `spawn` target** (`SERVICES_DESIGN.md`), where a CPU-bound body really would run on another M and
the intent it spells becomes load-bearing. It is kept for that, and because `maxon-shv2/track0`'s
torture programs need it today to make their CPU-bound tasks spawnable at all.

⚠ **IT IS A CHECKPOINT, NOT A YIELD.** It does not reschedule, it does not park, and it does not hand
the processor to anybody — `Runtime.yield()` is the intrinsic that does (`__Builtins.yield`, see
`builtins-sleep.md`'s neighbours). A future scheduler could hang a cooperative-yield check here; today
the body is a prologue and an epilogue.

### shv2 satisfies E3073 through the WALK, not through a roster

`SemanticCheck.calleeYields` answers `true` for any callee the program does not DECLARE, and a runtime
entry point is declared in no source at all — so `__parallel_boundary` reaches E3073's yield closure by
the same route `__mm_alloc` and `__gt_sleep` do, and needs no entry in
`ioYieldingRuntimeCallee`'s roster. That is why the two cases below are stated as a PAIR: the marker
case alone would pass against a compiler that had stopped checking, and the control is what says the
check is still live.

⚠ The bootstrap arrives at the same verdict by the opposite construction — an explicit
`YieldingRuntimeEntries` roster naming `maxon_parallel_boundary` (`SemanticCheckPass.cs`), because its
walk does NOT fall open on an unknown callee. Same answer for every program; only the derivation
differs.

## Tests

<!-- test: builtins-parallel-boundary.marks-a-cpu-bound-spawn -->
<!-- targets: x64-windows, arm64-linux -->
A CPU-bound function whose only concession to the scheduler is `__Builtins.parallelBoundary()` is a
legal `async` target: the spawn compiles, runs, and hands back the value it computed.

⛔⛔ **THE MARKER IS ABOUT `async`, NOT ABOUT THIS INTRINSIC, AND THE CASE BELOW IS THE PROOF.**
`statement-position` emits the very same `call __parallel_boundary` and carries NO restriction — it
passes on `wasm32-wasi`, because an empty function lowers on every target shv2 emits. What has no wasm
lowering is the SPAWN: `StdToWasm` has no `__gt_trampoline`, and there is no target gate on the `async`
CONSTRUCT — the refusal is reached only INDIRECTLY, when the spawned callee happens to touch an
x64-only runtime entry (`async-await.basic` earns `E3104` on `File.exists` and is skipped for it).

⚠ **A CALLEE THAT YIELDS ONLY BY `calleeYields`' FALL-OPEN REACHES THE BACKEND AND PANICS, AND THAT IS
PRE-EXISTING RATHER THAN THIS RUNG'S.** MEASURED on a program naming no intrinsic of this file —
`async` over a function whose whole body is `print("value {n}")` — `panic at StdToWasm.maxon:2182:
emitFuncAddr: no wasm function index for function value '__gt_trampoline'`, no file and no line. The
honest cure is to refuse the SPAWN on a target with no green-thread substrate rather than to refuse
whatever the callee happened to call, and that moves the skip reason of every running `async` case;
it is its own rung. Until then this case names the lane it can run on.
```maxon
function work(n ExitCode) returns ExitCode
	__Builtins.parallelBoundary()
	return n + 1
end 'work'

function main() returns ExitCode
	let p = async work(6)
	let got = await p
	print("{got}")
	return got
end 'main'
```
```stdout
7
```
```exitcode
7
```

<!-- test: builtins-parallel-boundary.error.the-control-still-refuses -->
**THE CONTROL, AND IT IS HALF THE PROOF.** The identical program WITHOUT the marker is refused. A
compiler that had stopped checking, or one that treated every function as yielding, would pass the
case above and this one too — so the marker case means something only while this one is red.
```maxon
function work(n ExitCode) returns ExitCode
	return n + 1
end 'work'

function main() returns ExitCode
	let p = async work(6)
	let got = await p
	return got
end 'main'
```
```maxoncstderr
error E3073: <fragment>:7:10: 'async work(6)' — function never yields; 'async' is for I/O-concurrent work only
```

<!-- test: builtins-parallel-boundary.statement-position -->
It returns nothing, so it is written on a line of its own — the statement door `builtins-sleep.md`
opened for `__Builtins.<member>(…)`, which is a SHAPE and not a name list. A program whose whole body
is the marker compiles and exits normally.
```maxon
function checkpoint()
	__Builtins.parallelBoundary()
end 'checkpoint'

function main() returns ExitCode
	checkpoint()
	checkpoint()
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: builtins-parallel-boundary.error.value-position-rejected -->
It returns nothing, so reading its result is reading a value that is not there — the same rejection
`__Builtins.sleep`'s result gets, quoting the QUALIFIED name the user wrote.
```maxon
function main() returns ExitCode
	let ignored = __Builtins.parallelBoundary()
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:27: Function '__Builtins.parallelBoundary' does not return a value
```

<!-- test: builtins-parallel-boundary.error.arity-rejected -->
An intrinsic has no signature registry entry for the ordinary arity check to consult, so the arity is
enforced at the emit — and the diagnostic quotes what the user wrote, not the bare spelling. It takes
NOTHING: there is no knob on a checkpoint.

⚠ *"exactly 0 argument"* is `builtinArity`'s one sentence, unpluralized, and every zero-arity intrinsic
already earns it (`builtins-clock`, `process-executable-path`, `process-id` pin the same words). Conformed
to rather than corrected here: the wording is one renderer's and moving it moves four specs at once.
```maxon
function main() returns ExitCode
	__Builtins.parallelBoundary(1)
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:13: '__Builtins.parallelBoundary' takes exactly 0 argument, but 1 were given
```
