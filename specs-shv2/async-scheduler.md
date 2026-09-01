---
feature: async-scheduler
status: stable
keywords: [async, await, green-threads, scheduler, concurrency, promise]
category: concurrency
---

# Async / Await — the cooperative green-thread scheduler (P1.5-B1a)

## Documentation

`async f(args…)` spawns a **green thread** running `f` and yields a `Promise` handle; `await p` blocks the
current green thread until `p`'s thread completes and yields its result. This first slice is **single-M
cooperative**: one OS thread runs everything, a spawned thread runs only when a driver (`await`) hands it the
processor, and it runs to completion in one shot (there is no mid-body yield yet).

```text
function compute() returns int
	return 42
end 'compute'

function main() returns ExitCode
	let p = async compute()   // spawn a green thread; p is a Promise
	let r = await p           // run it, collect its result
	return r as ExitCode
end 'main'
```

The B1a slice is **scalar-only**: an async call's arguments and its awaited result must be integer/bool
values. A managed (`String`/struct) or float argument or result is refused at compile time rather than
leaked or miscompiled — the green-thread runtime moves scalars through the integer registers, and a managed
or float value needs a channel a later slice builds.

## Targets — the one statement of the green-thread gate

⭐ **This section is the HOME of the `<!-- targets: x64-windows, arm64-macos, arm64-linux -->` marker that every async, sleep,
clock and subprocess spec carries. Those files point HERE rather than restating it**, so the reason
exists once and cannot drift into twelve versions of itself.

**It is a RUNTIME-SUBSTRATE gate, and the COMPILER — not the marker — is what decides it.** The context
switch is hand-written ASSEMBLY and the driver reaches the host's clock, its timed park and its
per-OS-thread storage directly, so a lane serves this family only once a backend has written those out.
`SemanticCheck.requireTargetSupportsCallee` refuses the reachable ones on every other lane with **E3104**,
naming the entry (`__gt_sleep`, `__gt_now_ns`, `__clock_now_unix_s`). A pass there is not a thing that
could be had, and the marker only spares the runner a compile whose answer is already known.

⭐ **TWO LANES NOW SERVE IT, WHICH IS WHY THE MARKER READS `x64-windows, arm64-macos`.** x64-windows was
first (`X64GtRuntime`: the context switch, the trampoline, the relocating grower, and a Win32
`CRITICAL_SECTION`/`CreateEventA`/`TlsAlloc` substrate under them); arm64-macOS is second
(`Arm64GtRuntime` for the three AAPCS64 chunks, `Arm64DarwinRuntime` for the libSystem objects — a pthread
key for the processor slot, a recursive mutex for the run queue, and a condition variable standing in for
an auto-reset event). The remaining lanes stay refused for reasons of their own: both Linux images are raw
static binaries that link no libc, so they have no pthread primitive to build any of it on, and a WASI
component has no addressable call stack for a context switch to move.

⚠ **It is NOT a per-target opt-in, and the test of that is what carries NO marker.** Anything decided
BEFORE lowering — an arity, an operand type, a shape refusal — is target-neutral and runs everywhere.
That is why the four `-refused` cases in this file are gated by nothing at all: they assert `E2015`, and
`E2015` is the same on x64-linux and wasm32-wasi (measured 2026-07-28). **A marker on one of those would
be hiding a green lane, not describing a red one** — which is exactly what eleven of them were doing
across this file, `async-subprocess`, `builtins-clock` and `builtins-sleep` until that audit.

⚠ **Un-gate the moment a second substrate lands.** A stale gate is indistinguishable from a real one.

## Tests

<!-- test: async-scheduler.basic -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A spawned green thread runs its function and `await` collects the result.
```maxon

function compute() returns Integer
	Runtime.yield()
	return 42
end 'compute'

function main() returns ExitCode
	let p = async compute()
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-scheduler.parallel -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Two green threads are spawned before either is awaited; both run and their results sum.
```maxon

function ten() returns Integer
	Runtime.yield()
	return 10
end 'ten'

function twenty() returns Integer
	Runtime.yield()
	return 20
end 'twenty'

function main() returns ExitCode
	let a = async ten()
	let b = async twenty()
	let ra = await a
	let rb = await b
	return (ra + rb) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
30
```

<!-- test: async-scheduler.sequence -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A spawn/await chain threads a value through two green threads.
```maxon

function inc(x Integer) returns Integer
	Runtime.yield()
	return x + 1
end 'inc'

function main() returns ExitCode
	let p1 = async inc(40)
	let r1 = await p1
	let p2 = async inc(r1)
	let r2 = await p2
	return r2 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-scheduler.spawn-arg -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A scalar argument is spilled into the green thread's argument buffer and read back by the callee.
```maxon

function sixtimes(x Integer) returns Integer
	Runtime.yield()
	return x * 6
end 'sixtimes'

function main() returns ExitCode
	let p = async sixtimes(7)
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-scheduler.multiple-args -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Several scalar arguments (positional first, then labelled) fill the argument buffer in parameter order.
```maxon

function combine(a Integer, b Integer, c Integer) returns Integer
	Runtime.yield()
	return a + b * c
end 'combine'

function main() returns ExitCode
	let p = async combine(2, b: 4, c: 10)
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-scheduler.nested -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A green thread can itself spawn and await another green thread — the current-GT tracking nests correctly.
```maxon

function leaf() returns Integer
	Runtime.yield()
	return 20
end 'leaf'

function middle() returns Integer
	let inner = async leaf()
	let got = await inner
	return got + 22
end 'middle'

function main() returns ExitCode
	let p = async middle()
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-scheduler.spawn-immediately-awaited -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
`await async f()` — spawn and await in one expression — parses and runs.
```maxon

function answer() returns Integer
	Runtime.yield()
	return 42
end 'answer'

function main() returns ExitCode
	let r = await async answer()
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-scheduler.spawn-not-awaited -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A spawned green thread that is never awaited never runs and cannot leak — its struct, stack and argument
buffer are slab/OS allocations invisible to the leak gate, so the program exits with `main`'s own code
rather than 101.
```maxon

function compute() returns Integer
	Runtime.yield()
	return 42
end 'compute'

function main() returns ExitCode
	_ = async compute()
	return 7 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: async-scheduler.await-loop-bounded -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A spawn/await loop stays bounded: each spawn commits a fresh green-thread stack and each completed thread's
stack is RELEASED (`osFreePages`/VirtualFree) as the driver reaps it, so 5000 iterations hold at most one
resident stack at a time and exit cleanly. (P1.5-B1a′ replaced B1a's fixed-size 1 MiB free-list with
alloc-fresh-on-spawn + free-on-complete, because the relocating morestack makes stacks variable-sized; the
bound is now alloc+free churn rather than a recycle. Without either, every spawn would leak its stack
commit — invisible to the `__mm` leak gate — and exhaust commit on a bounded-pagefile machine.) Since
P1.5-B1c (#87) this loop is also a LEAK GATE on the GT struct + its inline arg buffer: each completed thread's
struct is recycled onto the free-list and the completion-based `__gt_live_count` is balanced to zero, so a
clean exit (0) proves nothing leaked. Before that fix each iteration bump-leaked its ~224-byte struct and its
48-byte arg buffer (slab allocations invisible to `__mm_alloc_count`), and — once the counter existed but the
free-list did not — this same program exited 101.
```maxon

function noop() returns Integer
	Runtime.yield()
	return 0
end 'noop'

function main() returns ExitCode
	var i = 0
	var acc = 0
	while i < 5000 'loop'
		let p = async noop()
		let r = await p
		acc = acc + r
		i = i + 1
	end 'loop'
	return acc as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-scheduler.struct-reuse -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The GT-struct free-list is exercised AND its whole-struct memzero-on-recycle is correct: five spawn/awaits run
in sequence, each reusing the struct the previous completed thread pushed onto the free-list (P1.5-B1c #87).
Each thread computes `2 * i` from its argument, so `2+4+6+8+10 = 30` proves every recycled struct actually RAN
its function. A recycled struct carries its previous tenant's fields (it lives outside the always-zeroing slab),
so without the whole-struct memzero the second spawn would inherit `status == completed` and its `await` would
short-circuit to the PRIOR result without ever running the new thread — a wrong sum, not 30.
```maxon

function dbl(x Integer) returns Integer
	Runtime.yield()
	return x * 2
end 'dbl'

function main() returns ExitCode
	var i = 1
	var acc = 0
	while i <= 5 'loop'
		let p = async dbl(i)
		let r = await p
		acc = acc + r
		i = i + 1
	end 'loop'
	return acc as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
30
```

<!-- test: async-scheduler.out-of-order-await -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A promise's struct must survive until ITS OWN `await`, even when the thread completes early as a side effect of
driving a DIFFERENT await (P1.5-B1c #87). `await p2` drives the FIFO run queue and completes `p1` first; `p1` is
now a completed-but-un-awaited handle. Two intervening `async` spawns must NOT recycle `p1`'s struct — only `p1`'s
own `await` may. `p4` therefore gets a distinct struct, and `await p1` reads `p1`'s real result (10), not `p4`'s
(40). `10+20+30+40 = 100`. Reclaiming at completion instead of at await returned 130 here (a silent wrong answer).
```maxon

function w(x Integer) returns Integer
	Runtime.yield()
	return x * 10
end 'w'

function main() returns ExitCode
	let p1 = async w(1)
	let p2 = async w(2)
	let r2 = await p2
	let p3 = async w(3)
	let p4 = async w(4)
	let r3 = await p3
	let r4 = await p4
	let r1 = await p1
	return (r1 + r2 + r3 + r4) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
100
```

<!-- test: async-scheduler.callee-saved-xmms-survive-a-context-switch -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Two green threads each hold TEN f64 locals across a `sleep`, interleaved so each is suspended while
the other is actively occupying xmm6–15.

**The context switch is the one call whose callee-saved promise is kept by hand.** Since Wave 2
xmm6–15 are callee-saved, so the register allocator believes a float in xmm6 survives a
`call __gt_context_switch` — and that call does not return to its caller: it switches to another
green thread's stack, which runs arbitrary user code, and comes back only later. The switch has no
prologue and no coloring, so it must save that half explicitly (`X64GtRuntime`), and this is the
test that says so: with the switch's `movsd` saves removed, `threadA` returns **5524** — the sum of
`threadB`'s floats — instead of 79. Nothing in the golden fragments covers the emitted GT runtime,
so only a RUN can see this.

`threadA` sums 1..10 = 55 plus `trunc(3.0) + trunc(21.0)` = 24, so 79. `threadB` sums
100+…+1000 = 5500 plus 201 + 2001 = 2202, so 7702. `main`'s own two floats must also survive both
awaits: 11 + 22 = 33. The exit code is 0 only if all three hold.
```maxon
function threadA() returns Integer
	let a0 = 1.5
	let a1 = 2.5
	let a2 = 3.5
	let a3 = 4.5
	let a4 = 5.5
	let a5 = 6.5
	let a6 = 7.5
	let a7 = 8.5
	let a8 = 9.5
	let a9 = 10.5
	sleep(4)
	let s1 = trunc(a0) + trunc(a1) + trunc(a2) + trunc(a3) + trunc(a4)
	let s2 = trunc(a5) + trunc(a6) + trunc(a7) + trunc(a8) + trunc(a9)
	sleep(4)
	let s3 = trunc(a0 * 2.0) + trunc(a9 * 2.0)
	return s1 + s2 + s3
end 'threadA'

function threadB() returns Integer
	let b0 = 100.5
	let b1 = 200.5
	let b2 = 300.5
	let b3 = 400.5
	let b4 = 500.5
	let b5 = 600.5
	let b6 = 700.5
	let b7 = 800.5
	let b8 = 900.5
	let b9 = 1000.5
	sleep(2)
	let t1 = trunc(b0) + trunc(b1) + trunc(b2) + trunc(b3) + trunc(b4)
	let t2 = trunc(b5) + trunc(b6) + trunc(b7) + trunc(b8) + trunc(b9)
	sleep(2)
	let t3 = trunc(b0 * 2.0) + trunc(b9 * 2.0)
	return t1 + t2 + t3
end 'threadB'

function main() returns ExitCode
	let m0 = 11.5
	let m1 = 22.5
	let pa = async threadA()
	let pb = async threadB()
	let ra = await pa
	let rb = await pb
	let mine = trunc(m0) + trunc(m1)
	if ra == 79 and rb == 7702 and mine == 33 'ok'
		return 0
	end 'ok'
	return 9
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-scheduler.a-float-argument-survives-a-stack-grow -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A recursive float-taking function runs on a green thread's 2 KB stack, so `__gt_morestack` fires
part-way down the recursion and relocates the stack underneath it.

**The stack guard is emitted at offset 0 of the entry block — before `push rbp` and before the
`param` captures — so an incoming FLOAT argument is still sitting in xmm0–5 when the guard `call`s
`__gt_morestack`**, which calls `VirtualAlloc`/`VirtualFree`. Those are Win64-volatile in xmm0–5, so
the grower must save and restore that half by hand exactly as it saves the thirteen GPRs
(`X64GtRuntime.morestackSavedXmmOrder`). Without it `x` reads 0 from the level the guard fires at
downwards, and since `0 * 2.0` is 0 for ever the sweep saturates: **20 / 60 / 60 / 60** instead of
20 / 60 / 140 / 300. The same recursion run synchronously is correct, because nothing grows a
non-GT stack — only a RUN through `await async` can see this, and no golden fragment covers the
hand-assembled GT runtime.

`scale(20.0, depth: n)` sums 20 + 40 + … + 20·2ⁿ, so the four depths are 20, 60, 140, 300; each
mismatch returns its own exit code so a partial corruption names the level it started at.
```maxon
function scale(x Real, depth Integer) returns Integer
	if depth == 0 'base'
		return trunc(x)
	end 'base'
	return trunc(x) + scale(x * 2.0, depth: depth - 1)
end 'scale'

function sweep() returns Integer
	Runtime.yield()
	let d0 = scale(20.0, depth: 0)
	if d0 != 20 'bad0'
		return 1
	end 'bad0'

	let d1 = scale(20.0, depth: 1)
	if d1 != 60 'bad1'
		return 2
	end 'bad1'

	let d2 = scale(20.0, depth: 2)
	if d2 != 140 'bad2'
		return 3
	end 'bad2'

	let d3 = scale(20.0, depth: 3)
	if d3 != 300 'bad3'
		return 4
	end 'bad3'

	return 0
end 'sweep'

function main() returns ExitCode
	let p = async sweep()
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)
```
```exitcode
0
```

<!-- test: async-scheduler.every-fp-argument-register-survives-a-stack-grow -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The sibling of the test above, widened from one FP argument register to **all six**: `six` spends
every one of the parser's six parameter slots on a float, so xmm0–5 are ALL live at the stack guard
when `__gt_morestack` fires. A save list that covered only some of them would still pass the
one-argument test.

Each level contributes `trunc(1.5)+trunc(2.5)+trunc(3.5)+trunc(4.5)+trunc(5.5)` = 15, and the
recursion runs while `a >= 1.0` from 6.0 down to 0.0 — seven levels, 105. A destroyed xmm0 ends the
recursion at the first level (15); a destroyed xmm1–5 drops that register's term from every level.
```maxon
function six(a Real, b Real, c Real, d Real, e Real, f Real) returns Integer
	let sum = trunc(b) + trunc(c) + trunc(d) + trunc(e) + trunc(f)
	if a < 1.0 'base'
		return sum
	end 'base'
	return sum + six(a - 1.0, b: b, c: c, d: d, e: e, f: f)
end 'six'

function fpArgs() returns Integer
	Runtime.yield()
	return six(6.0, b: 1.5, c: 2.5, d: 3.5, e: 4.5, f: 5.5)
end 'fpArgs'

function main() returns ExitCode
	let p = async fpArgs()
	let r = await p
	if r == 105 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)
```
```exitcode
0
```
