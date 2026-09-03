---
feature: terminal-color-detection
status: stable
keywords: [terminal, tty, color, colour, ansi, NO_COLOR, TERM, stdoutWantsAnsiColor, __Builtins, intrinsics]
category: system
---

# `__Builtins.stdoutWantsAnsiColor()` — may this program's output carry ANSI escapes?

## Documentation

`__Builtins` is the compiler's builtin TYPE: a name reserved for the compiler, whose static methods
are INTRINSICS rather than functions any file declares. This spec pins one of them:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.stdoutWantsAnsiColor()` | `true` when this process's standard output is a terminal that wants colour |

It takes no arguments and has no `stdlib/` wrapper: the intrinsic IS the surface, for
`__Builtins.enterBackgroundPriority`'s reason — a declaration whose whole body is one intrinsic call
is the thin wrapper the house rules forbid.

### THREE conditions, ALL required, and each rules out a different way of being wrong

The rule is the reference's (`maxon-sharp/Ansi.cs`, `WantsColor`), unchanged:

1. **stdout is a character device.** A redirected stream is being CAPTURED — a file, a pipe, a
   golden — and escape sequences in a capture are corruption of the thing captured.
2. **`NO_COLOR` is unset.** The user saying no across every tool at once (no-color.org). PRESENCE
   alone disables, whatever the value.
3. **`TERM` is not `dumb`**, compared case-insensitively. The terminal itself saying it cannot render
   this.

Dropping any one of them is a wrong answer of a different kind, which is why none is optional.

### ⛔⛔ IT CLOSES A FLAG THAT WAS A DOCUMENTED LIE

`docs/CLI_REFERENCE.md` and `maxon-shv2`'s own usage text both said *"auto means only when stdout is
a terminal"* while `TestRender.reportColorEnabled` answered `false` for `auto` unconditionally, with
a comment saying so: *"there is no terminal-detection primitive in this compiler or its stdlib"*.
That was true, and it made the flag say one thing and do another. `--color=auto` now resolves through
this intrinsic.

### ⚠ THE ANSWER IS A PREDICATE, WHICH IS WHY IT IS A `bool` AND NOT AN `int`

Its neighbours in the argumentless band — `currentProcessId`, `cpuCount`,
`enterBackgroundPriority` — answer NUMBERS a caller reads. This one answers a QUESTION, and tagged
`integer` it would need `!= 0` at every call site: the sentinel-shaped spelling the house rules
refuse. The runtime still answers 0/1, because the Std tier has no `setcc`; the PARSER is what tags
the result `boolean`, which is `__ManagedDirectory.exists`'s arrangement exactly.

### ⚠ ON WINDOWS, `NO_COLOR` SET TO THE EMPTY STRING READS AS UNSET

`GetEnvironmentVariableA` answers 0 both for a name that is not set and for one whose value is
empty, and Win32 offers no third answer to tell them apart — `set NO_COLOR=` DELETES the variable
there, so the two states coincide on the platform as well as in the API. A lane whose `getenv` can
report a non-NULL empty string owes the sharper test when it lands.

### ⭐⭐ IT IS ANSWERED ON EVERY TARGET — THE MISSING FACILITY DEGRADES THE ANSWER, IT DOES NOT REFUSE THE BUILD

The intrinsic lowers to `__tty_stdout_wants_color`, and **that entry point exists on every target.**
`TargetFacilities.targetProvidesFacility`'s `terminalDetection` row says which lanes can really ask the
OS — today every lane but **wasm32-wasi** — and `TerminalRuntime.installTerminalRuntime` reads that ONE
row to choose the BODY: the three conditions where the host can answer, and a body whose whole content is
`return 0` where it cannot. So on every other lane the predicate is `false`, `--color=auto` resolves to
`never`, and nothing is refused.

⛔⛔ **IT WAS AN E3104 REFUSAL FOR ONE DAY, AND THAT MADE THE COMPILER UNBUILDABLE FOR arm64-macOS.**
`maxon-shv2/Testing/TestRender.reportColorEnabled` asks the question unconditionally, `Testing/` is
ordinary user code, and the E3104 gate is reachability-blind for user code by design — so shv2 stopped
cross-compiling for a lane that had built the day before. ⇒ **E3104 is right for a facility whose
absence leaves a program with NO ANSWER** (there is no honest `read stdin` on a lane with no stdin);
it is wrong for one whose absence IS an answer. "This is not a terminal" is exactly that, it is the
conservative half, and it is what `--color=auto` resolved to on every lane before this intrinsic
existed. `HostFacility.terminalDetection` is the one row in that table carrying this shape, and it says
so at the declaration.

⛔ **THE FALLBACK MAY NEVER BECOME `true`.** The whole point of the question is that a REDIRECTED
stream is being captured; a lane that guessed "yes" would put escape sequences into every golden,
every log file and every pipe.

⭐ **HOW A POSIX LANE ANSWERS CONDITION 1.** `isatty(1)` on the descriptor `osStdHandle` already
produces, mapped into the vocabulary `StdOp.osHandleFileType` speaks — `FILE_TYPE_CHAR` when it is a
terminal and `FILE_TYPE_DISK` when it is not, the same shape as that lane's existing errno→Win32
mapping. On arm64-macOS conditions 2 and 3 are `getenv`, the idiom already used for `MAXON_MAX_PROCS`.

⚠ **THE TWO LINUX LANES LINK NO LIBC, AND WHAT THAT COST WAS CONDITIONS 2 AND 3 RATHER THAN CONDITION
1.** Condition 1 there is the `ioctl(1, TCGETS, …)` that `isatty` is made of. The ENVIRONMENT is the part
that needed building: `NO_COLOR` and `TERM` are not optional, and a libc-less image reaches its
environment only through a vector the entry stub captured — so the capture, the `.data` word it lands in
and the walk that reads it all ride the union of `osEnvRead`'s two producers rather than the scheduler's
bit, because a terminal-detecting program need not have a scheduler.

⚠ On **wasm32-wasi** the `false` is more than "not yet": a WASI component's stdout is an
`output-stream` RESOURCE, and the component model exposes no way to ask what is on the other end of
it. A lowering there could only guess — which is precisely why the fallback is the conservative
answer rather than a refusal.

## Tests

<!-- test: terminal-color-detection.answers-false-when-stdout-is-captured -->
⭐⭐ **THE ONE HALF OF THIS PREDICATE A HARNESS CAN PIN, AND IT IS PINNABLE PRECISELY BECAUSE OF WHAT
THE HARNESS DOES.** A spec case's stdout is CAPTURED so the runner can compare it, which makes
condition 1 false by construction for every case in this suite — so "a captured stream is not a
terminal" is exactly the property a case here can assert, and it is the half that matters: it is the
half that keeps escape sequences out of goldens, transcripts and pipes.

⛔ **AND THE TRUE CASE CANNOT BE PINNED FROM HERE AT ALL.** It needs stdout attached to a character
device, which is the one thing a runner that reads stdout cannot provide — the same structural limit
`process-background-priority` records for its own set, one facility over. It was MEASURED by hand on
every lane instead. On Windows and on both compilers: a program returning the predicate as its exit
code, with stdout redirected to the NUL device (a character device that discards), answered **true**
with `NO_COLOR` and `TERM` unset, **false** with `NO_COLOR` set to any value including a non-empty
one, **false** for `TERM` in `dumb`/`DUMB`/`DuMb`, and **true** for `xterm`, `dumber` and `dum` —
which is the length test and the case-fold test each shown to discriminate. On arm64-macos, arm64-linux and x64-linux the
same program was run with stdout on a REAL terminal (a pty): **true** there with `NO_COLOR` and `TERM`
unset, **false** through a pipe, **false** on that terminal with `NO_COLOR` set to `1` or to `hello`,
**false** for `TERM` in `dumb`/`DuMb`, and **true** for `xterm`, `dumber` and `dum`. ⚠ On the Linux
lanes `/dev/null` answers **false** where the NUL device answers **true** on Windows, and that is not a
disagreement: `ioctl(TCGETS)` asks whether a TERMINAL is there, where `GetFileType` answers the coarser
"character device".
```maxon
function main() returns ExitCode
	if __Builtins.stdoutWantsAnsiColor() 'wantsColor'
		return 1
	end 'wantsColor'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: terminal-color-detection.is-stable-across-calls -->
Asking twice answers the same thing. It reads the OS and the environment rather than consuming
anything, so there is no state to advance — a lowering that leaked its scratch buffer or left the
environment probe half-read would be free to differ on the second call, and this is what says it does
not.
```maxon
function main() returns ExitCode
	let first = __Builtins.stdoutWantsAnsiColor()
	let second = __Builtins.stdoutWantsAnsiColor()
	if first == second 'agree'
		return 0
	end 'agree'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: terminal-color-detection.degrades-to-false-on-a-lane-with-no-terminal-notion -->
<!-- targets: wasm32-wasi -->
⭐⭐ **THE DEGRADATION, PINNED ON THE ONE LANE THAT CAN NEVER HAVE THE FACILITY.** A WASI component
cannot ask what is on the other end of its `output-stream`, so this program must still BUILD and must
answer `false` — the conservative half — rather than being refused at its own span. ⛔ This case
replaced one asserting `E3104` here, and the replacement is the point: that refusal took the compiler's
own `Testing/` code with it and made shv2 unbuildable for arm64-macOS. An `exitcode` of 1 would mean the
fallback had started GUESSING `true`, which is the one answer this must never give.
```maxon
function main() returns ExitCode
	if __Builtins.stdoutWantsAnsiColor() 'wantsColor'
		return 1
	end 'wantsColor'

	return 0
end 'main'
```
```exitcode
0
```
