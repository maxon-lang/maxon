---
feature: async-stack-growth
status: stable
keywords: [async, green-threads, morestack, stack-growth, relocation, recursion, concurrency]
category: concurrency
---

# Async stack growth — the relocating morestack (P1.5-B1a′)

## Documentation

A green thread starts on a tiny **2 KB** stack and grows it ON DEMAND: every function's prologue projects its
frame against a per-thread stack guard, and when the frame would overflow, it calls the runtime grower
`__gt_morestack`, which allocates a stack twice the size, COPIES the old stack onto it, and **walks the
saved-frame-pointer chain** fixing every interior pointer by the relocation offset — so the thread continues on
the larger stack with every live reference intact. Deep recursion inside an `async` body therefore just works:
the stack grows (and relocates) as many times as the depth needs.

```text
function deepRecurse(n int) returns int
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function main() returns ExitCode
	let p = async deepRecurse(200)   // ~200 frames — far past a 2 KB stack, so the runtime grows + relocates it
	let r = await p
	return r as ExitCode             // 200
end 'main'
```

Growth is transparent: a value live across the growth (a parameter, a partial sum) reads the same after
relocation as before, because the copy preserves the bytes and the chain walk fixes the pointers. Growth also
composes with the mid-body yield — a thread that `sleep`s, resumes, and THEN recurses deep grows on its resumed
stack — and with completion — a thread whose grown stack is released on completion leaves the next spawn a fresh
2 KB seed.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** `__gt_morestack` is hand-written x64 assembly and relocates a stack obtained from
`VirtualAlloc`, so these cases have no substrate to run on off x64-windows.

## Tests

<!-- test: async-stack-growth.deep-recursion -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
`async deepRecurse(200)` recurses ~200 frames deep — far past the 2 KB seed stack, forcing several
grow-and-relocate rounds — and the awaited sum is exact, proving the relocated stack carried every frame's
partial result correctly.
```maxon

function deepRecurse(n Integer) returns Integer
	Runtime.yield()
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function main() returns ExitCode
	let p = async deepRecurse(200)
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
200
```

<!-- test: async-stack-growth.multi-growth -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A deeper recursion (~400 frames) forces MORE grow-and-relocate rounds and a longer saved-rbp chain to walk at
the deepest growth — the stress case for the chain walk across multiple relocations. The awaited sum (400) is
exact; `main` returns it less 250 to fit the exit code.
```maxon

function deepRecurse(n Integer) returns Integer
	Runtime.yield()
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function main() returns ExitCode
	let p = async deepRecurse(400)
	let r = await p
	return (r - 250) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
150
```

<!-- test: async-stack-growth.grow-across-yield -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A thread `sleep`s (parks on the timer, context-switches back to the driver), RESUMES, and only THEN recurses
deep — so the growth happens on a stack the scheduler switched out and back in. The awaited sum is exact,
proving `gt.sp`/`gt.fp` and the saved-rbp chain are consistent across a yield followed by a relocation.
```maxon

function deepRecurse(n Integer) returns Integer
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function yieldThenRecurse() returns Integer
	sleep(1)
	return deepRecurse(200)
end 'yieldThenRecurse'

function main() returns ExitCode
	let p = async yieldThenRecurse()
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
200
```

<!-- test: async-stack-growth.grow-then-complete-then-respawn -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A thread grows, completes (its grown stack is released), then a SECOND thread spawns on a fresh 2 KB seed and
grows in turn — proving free-on-complete leaves no corruption for the next spawn. Both awaited sums are exact.
```maxon

function deepRecurse(n Integer) returns Integer
	Runtime.yield()
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function main() returns ExitCode
	let p1 = async deepRecurse(60)
	let r1 = await p1
	let p2 = async deepRecurse(90)
	let r2 = await p2
	return (r1 + r2) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
150
```
