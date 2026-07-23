---
feature: async-subprocess
status: stable
keywords: [subprocess, process, spawn, async, await, runProcess, green-threads, scheduler, netpoll, yield, concurrency]
category: concurrency
---

# Subprocess — spawn a child and yield while it runs (P1.5)

## Documentation

`runProcess(cmd)` spawns a Windows child process for the command line `cmd`, suspends the **current green
thread** while the child runs, and returns the child's integer exit code once it finishes. It is a **yielding**
wait: the thread parks on the child, hands control back to the scheduler, and RESUMES with its exit code once the
child has exited — so other green threads run while a child is pending.

```text
function runChild() returns int
	return runProcess("cmd /c exit 3")
end 'runChild'

function main() returns ExitCode
	let p = async runChild()
	let code = await p
	return code as ExitCode
end 'main'
```

When every green thread is parked (on a timer or a child), the scheduler **netpolls**: it blocks the single OS
thread on the parked children with a real `WaitForMultipleObjects` (bounded by the earliest timer deadline when a
timer is also pending, else indefinitely), never a busy-spin, and resumes each thread once its child exits. Because
the wait is bounded by the earliest timer, a thread that is merely sleeping still wakes on time even while another
thread's child is still running.

`runProcess` works from the main thread (`GT0`) and from a spawned `async` thread alike. Its argument is a
`String` command line — borrowed, not consumed; a `float`/`int`/`bool` is refused at compile time. Its result is
an integer (the exit code), so — unlike `sleep` — it may be used in value position.

If the command names no runnable executable (`CreateProcessA` fails outright), `runProcess` aborts the process
with exit code 1 rather than parking on a non-existent child — a deterministic fatal error, never a hang.

## Tests

<!-- test: async-subprocess.exit-code -->
<!-- targets: x64-windows -->
A spawned green thread runs a child that exits 3; the thread parks on the child (yielding), resumes once the child
exits, and returns the exit code, which becomes the program's exit code.
```maxon
function runChild() returns int
	return runProcess("cmd /c exit 3")
end 'runChild'

function main() returns ExitCode
	let p = async runChild()
	let code = await p
	return code as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: async-subprocess.sequence -->
<!-- targets: x64-windows -->
Two children run in sequence (spawn, await, spawn, await) with distinct exit codes; each thread's exit code is
read back independently and combined, proving no cross-talk between the two parked-then-resumed threads.
```maxon
function childA() returns int
	return runProcess("cmd /c exit 4")
end 'childA'

function childB() returns int
	return runProcess("cmd /c exit 5")
end 'childB'

function main() returns ExitCode
	let pa = async childA()
	let a = await pa
	let pb = async childB()
	let b = await pb
	return (a * 10 + b) as ExitCode
end 'main'
```
```exitcode
45
```

<!-- test: async-subprocess.multi-concurrent -->
<!-- targets: x64-windows -->
Three children are spawned BEFORE any await, so all three park on their processes SIMULTANEOUSLY — the netpoll
blocks on a `WaitForMultipleObjects` of THREE handles, and `__gt_proc_check`'s parallel-array swap-remove runs with
a multi-entry store (the path `sequence`, `spawn-loop` and `interleave` never reach, each parking ≤1 child at a
time). Each exit code is read back into its own digit, so `123` proves all three resumed independently with the
right handle-to-thread mapping and no cross-talk.
```maxon
function c1() returns int
	return runProcess("cmd /c exit 1")
end 'c1'

function c2() returns int
	return runProcess("cmd /c exit 2")
end 'c2'

function c3() returns int
	return runProcess("cmd /c exit 3")
end 'c3'

function main() returns ExitCode
	let p1 = async c1()
	let p2 = async c2()
	let p3 = async c3()
	let r1 = await p1
	let r2 = await p2
	let r3 = await p3
	return (r1 * 100 + r2 * 10 + r3) as ExitCode
end 'main'
```
```exitcode
123
```

<!-- test: async-subprocess.interleave-with-sleep -->
<!-- targets: x64-windows -->
A slow child (a ~1 s `ping` delay) and a short (50 ms) sleeper run concurrently. The sleeper's timer fires WHILE
the child is still running, so the sleeper resumes FIRST — proving the process wait YIELDS (it is bounded by the
earliest timer, not a blocking wait on the child). Each records its completion order into a global
(`order = order * 10 + tag`), so `21` proves the sleeper (tag 2) completed before the child thread (tag 1). A wait
that blocked the single thread on the child would instead produce `12`.
```maxon
var order = 0

function slow() returns int
	let c = runProcess("cmd /c ping -n 2 127.0.0.1 >nul")
	order = order * 10 + 1
	return 1
end 'slow'

function fast() returns int
	sleep(50)
	order = order * 10 + 2
	return 2
end 'fast'

function main() returns ExitCode
	let p1 = async slow()
	let p2 = async fast()
	let r1 = await p1
	let r2 = await p2
	return order as ExitCode
end 'main'
```
```exitcode
21
```

<!-- test: async-subprocess.spawn-loop -->
<!-- targets: x64-windows -->
Robustness: twenty-five spawned threads each run a child that exits 1, awaited in turn. Each parks on its child
(waiting, NOT completed — its stack must NOT be recycled while parked) then resumes and completes (stack recycled
onto the free-list). The sum proves all twenty-five ran to completion with no crash, no use-after-free, and no leak.
```maxon
function child() returns int
	return runProcess("cmd /c exit 1")
end 'child'

function main() returns ExitCode
	var i = 0
	var sum = 0
	while i < 25 'l'
		let p = async child()
		let r = await p
		sum = sum + r
		i = i + 1
	end 'l'
	return sum as ExitCode
end 'main'
```
```exitcode
25
```

<!-- test: async-subprocess.scratch-reuse-loop -->
<!-- targets: x64-windows -->
Fifty children each exit 1, awaited in turn, summing to 50. The value is that `__gt_process_run`'s OS scratch —
STARTUPINFOA, PROCESS_INFORMATION, the mutable cmdline copy and the exit-code slot — is REUSED across all fifty
calls (P1.5-B1c #92): the three fixed buffers are one-time `__gt_init` allocations and the cmdline copy is a
grow-on-demand global that allocates once for a constant command length, so the loop stays bounded rather than
bump-leaking ~150 bytes per call. Reuse is only correct because PROCESS_INFORMATION's `hProcess` is re-zeroed
before each spawn (the failure sentinel) and the exit-code slot is re-zeroed before each `GetExitCodeProcess`
(a clean i64 read); the `__gt_live_count` gate stays clean, so a clean exit proves the reuse leaked nothing.
```maxon
function child() returns int
	return runProcess("cmd /c exit 1")
end 'child'

function main() returns ExitCode
	var i = 0
	var sum = 0
	while i < 50 'l'
		let p = async child()
		let r = await p
		sum = sum + r
		i = i + 1
	end 'l'
	return sum as ExitCode
end 'main'
```
```exitcode
50
```

<!-- test: async-subprocess.spawn-failure-aborts -->
<!-- targets: x64-windows -->
A command that names no runnable executable makes `CreateProcessA` fail outright, leaving a null child handle.
The runtime detects it and aborts with exit code 1 rather than parking on the null handle — which would busy-spin
the netpoller forever. A deterministic fatal error, never a hang.
```maxon
function main() returns ExitCode
	let code = runProcess("nonexistentprogram_xyz_12345")
	return code as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: async-subprocess.store-overflow-aborts -->
<!-- targets: x64-windows -->
Parking more than the store's capacity (64, `WaitForMultipleObjects`'s `MAXIMUM_WAIT_OBJECTS`) children
concurrently must NOT write past the 64-slot parallel arrays. Sixty-five children are spawned as LIVE promises
before any await (since P1.5-B2 #88 a DISCARDED promise is dropped-cancelled, so the children must be kept alive
by distinct bindings); `await p00` then drives them all — each `child` parks on the process store — and the 65th
park exceeds the 64 slots, so `__gt_proc_add` aborts with exit code 70 (`RuntimeAbort.processStoreOverflow`)
rather than corrupt the heap — a documented, safe hard bound. The un-awaited promises' drops are emitted but never
reached (the abort fires mid-drive). This is heavier than the other cases (~64 real child spawns before the abort)
because it is the regression test for a memory-safety guard.
```maxon
function child() returns int
	return runProcess("cmd /c exit 1")
end 'child'

function main() returns ExitCode
	let p00 = async child()
	let p01 = async child()
	let p02 = async child()
	let p03 = async child()
	let p04 = async child()
	let p05 = async child()
	let p06 = async child()
	let p07 = async child()
	let p08 = async child()
	let p09 = async child()
	let p10 = async child()
	let p11 = async child()
	let p12 = async child()
	let p13 = async child()
	let p14 = async child()
	let p15 = async child()
	let p16 = async child()
	let p17 = async child()
	let p18 = async child()
	let p19 = async child()
	let p20 = async child()
	let p21 = async child()
	let p22 = async child()
	let p23 = async child()
	let p24 = async child()
	let p25 = async child()
	let p26 = async child()
	let p27 = async child()
	let p28 = async child()
	let p29 = async child()
	let p30 = async child()
	let p31 = async child()
	let p32 = async child()
	let p33 = async child()
	let p34 = async child()
	let p35 = async child()
	let p36 = async child()
	let p37 = async child()
	let p38 = async child()
	let p39 = async child()
	let p40 = async child()
	let p41 = async child()
	let p42 = async child()
	let p43 = async child()
	let p44 = async child()
	let p45 = async child()
	let p46 = async child()
	let p47 = async child()
	let p48 = async child()
	let p49 = async child()
	let p50 = async child()
	let p51 = async child()
	let p52 = async child()
	let p53 = async child()
	let p54 = async child()
	let p55 = async child()
	let p56 = async child()
	let p57 = async child()
	let p58 = async child()
	let p59 = async child()
	let p60 = async child()
	let p61 = async child()
	let p62 = async child()
	let p63 = async child()
	let p64 = async child()
	let r = await p00
	return r as ExitCode
end 'main'
```
```exitcode
70
```

<!-- test: async-subprocess.error.non-string-arg-rejected -->
<!-- targets: x64-windows -->
`runProcess` requires a `String` command line; a non-String argument is refused at compile time.
```maxon
function main() returns ExitCode
	runProcess(42)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:2: 'runProcess' requires a String, but its argument is int
```
