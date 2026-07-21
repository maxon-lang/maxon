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
concurrently must NOT write past the 64-slot parallel arrays. Seventy children are spawned before any await (the
discarded promises keep their GTs parked); driving them all would overflow the store, so `__gt_proc_add` aborts
with exit code 70 (`ProcStoreOverflowExitCode`) rather than corrupt the heap — a documented, safe hard bound. This
is heavier than the other cases (~64 real child spawns before the abort) because it is the regression test for a
memory-safety guard.
```maxon
function child() returns int
	return runProcess("cmd /c exit 1")
end 'child'

function main() returns ExitCode
	var i = 0
	while i < 70 'l'
		let p = async child()
		i = i + 1
	end 'l'
	let q = async child()
	let r = await q
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
