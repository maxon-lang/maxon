---
feature: process-background-priority
status: stable
keywords: [process, priority, background, scheduling, enterBackgroundPriority, __Builtins, intrinsics]
category: system
---

# `__Builtins.enterBackgroundPriority()` — run this process out of the way

## Documentation

`__Builtins` is the compiler's builtin TYPE: a name reserved for the compiler, whose static methods
are INTRINSICS rather than functions any file declares. This spec pins one of them:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.enterBackgroundPriority()` | put THIS process at background scheduling priority, and answer the priority the OS then reports |

It takes no arguments and has no `stdlib/` wrapper: the intrinsic IS the surface, for
`__Builtins.currentProcessId`'s reason — a declaration whose whole body is one intrinsic call is the
thin wrapper the house rules forbid.

### It is the one intrinsic in this band that CHANGES the process rather than reporting on it

`cpuCount`, `currentProcessId` and `schedMaxActiveWorkers` all ask questions. This one acts, and then
asks. Its caller is a harness that is about to saturate the machine — `maxon-shv2`'s `spec-test` pool
calls it once before spawning any worker — so that a full suite stops making the box unusable while
it runs.

### Children inherit it, which is why ONE call covers a whole pool

A process spawned afterwards inherits the priority CLASS on Windows and the NICE value under POSIX.
So a single call at a parent's startup reaches every worker subprocess it spawns *and* every binary
those workers launch, and there is deliberately no per-child variant: that would be the same fact
spelled once per spawn site, and it would still miss the threads the parent runs itself.

### ⚠ THE ANSWER'S UNIT IS PLATFORM-DEFINED, AND NOTHING CONVERTS IT

Windows answers a priority CLASS (`BELOW_NORMAL_PRIORITY_CLASS` = 16384, against
`NORMAL_PRIORITY_CLASS` = 32); POSIX answers a NICE value (10, against 0). The two scales even run in
opposite directions — larger is LOWER priority under POSIX. That is `threadCpuTicks`'s situation
exactly, and it is handled the same way: compare against the constant for the platform you are on,
never across platforms. Every case below is therefore pinned `x64-windows`.

### The answer is a SECOND READING, and that is the whole reason it returns anything

A `void` "set" would be untestable — nothing a case could name would change, which is the argument
`scavengeMemory`'s header makes for answering a byte count. So the op sets the priority and then
READS IT BACK from the OS, and the value it answers is the read, never the value it just wrote.

### ⛔⛔ NO CASE BELOW CAN SEE WHETHER THE *SET* HAPPENED, AND THAT WAS MEASURED RATHER THAN ARGUED

**The suite runs these cases inside a process tree that is ALREADY at below-normal priority**, because
`Main.runSpecTest` calls this very intrinsic before it spawns the worker pool, and a child inherits the
priority class. So a test binary is at 16384 *before its first instruction*, and `GetPriorityClass`
answers 16384 whether the lowering sets anything or not.

⚠ **MEASURED 2026-08-30, both halves, with the `SetPriorityClass` emit deleted from
`StdToX64Conversion.lowerOsEnterBackgroundPriority`:**

| where the program ran | answer |
|---|---|
| under this suite (parent already below-normal) | 16384 — **2 passed, 0 failed, the sabotage invisible** |
| from a normal-priority shell, same binary | **32** — the sabotage plainly visible |

⇒ **The cases below assert a real POSTCONDITION — after this call the process is at below-normal, and
the value is a live reading rather than 0 or garbage — but they do NOT discriminate a working SET from
a missing one, and nothing written inside this suite can.** The discriminator needs a parent at normal
priority, which the harness structurally cannot provide to its own children.

⭐ **THE DISCRIMINATING CHECK IS THEREFORE A HAND PROCEDURE, AND IT IS OWED BY ANY CHANGE TO THIS
LOWERING.** Build a program that prints `__Builtins.enterBackgroundPriority()` and run it from a
NORMAL-priority shell (not through the suite, and not through an MCP tool whose server may itself be
de-prioritised). It must print **16384**; with the SET removed it prints **32**. That is the whole of
the evidence that the write happens, and it is why this section exists instead of a case that would
look like proof and be none.

### The substrate is x64-windows only at this rung

The intrinsic lowers to `__proc_bg_priority`, which joins the pid and executable-path entries under
`SemanticCheck.calleeNeedsWin32Substrate` — by the `__proc_` PREFIX, so it is gated by construction
rather than by memory, and this is the second time that promise has been collected without an edit
there. A program reaching it on another target is refused at the call's own span with `E3104`.

⚠ On wasm the refusal is more than "not yet", as it is for the pid: a WASI component has no process
and no scheduler priority, so a lowering there could only pretend to have done something.

## Tests

<!-- test: process-background-priority.answers-the-below-normal-class -->
<!-- targets: x64-windows -->
The POSTCONDITION: after the call the process is at `BELOW_NORMAL_PRIORITY_CLASS` (16384), and the
answer is a live reading rather than 0 — which is what `GetPriorityClass` answers on failure, and what
a lowering that passed a bad handle would produce. ⚠ It does NOT prove the SET happened; see the
section above for what does, and why no case here can.
```maxon
function main() returns ExitCode
	let belowNormalPriorityClass = 16384
	let normalPriorityClass = 32
	let priority = __Builtins.enterBackgroundPriority()
	var score = 0
	if priority == belowNormalPriorityClass 'isBelowNormal'
		score = score + 1
	end 'isBelowNormal'
	if priority != normalPriorityClass 'isNotStillNormal'
		score = score + 2
	end 'isNotStillNormal'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: process-background-priority.is-stable-across-calls -->
<!-- targets: x64-windows -->
Asking twice is not an error and does not drift: the second call re-applies the same class and reads
back the same answer. A lowering that toggled, or that lowered by one step per call the way a naive
`nice`-style implementation would, fails here while still passing the case above.
```maxon
function main() returns ExitCode
	let belowNormalPriorityClass = 16384
	let first = __Builtins.enterBackgroundPriority()
	let second = __Builtins.enterBackgroundPriority()
	var score = 0
	if first == second 'agree'
		score = score + 1
	end 'agree'
	if second == belowNormalPriorityClass 'stillBelowNormal'
		score = score + 2
	end 'stillBelowNormal'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: process-background-priority.rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The introspection substrate is x64-windows only at this rung. On any other target the call is refused
at its source span with `E3104`, naming the runtime entry that has no lowering there — never a panic
from inside the wasm backend.
```maxon
function main() returns ExitCode
	let priority = __Builtins.enterBackgroundPriority()
	return priority as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:28: this construct is x64-windows only at this rung: it lowers to the runtime entry '__proc_bg_priority', which has no wasm32-wasi implementation
```
