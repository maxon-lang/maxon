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

## Tests

<!-- test: async-scheduler.basic -->
<!-- targets: x64-windows -->
A spawned green thread runs its function and `await` collects the result.
```maxon

function compute() returns int
	return 42
end 'compute'

function main() returns ExitCode
	let p = async compute()
	let r = await p
	return r as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.parallel -->
<!-- targets: x64-windows -->
Two green threads are spawned before either is awaited; both run and their results sum.
```maxon

function ten() returns int
	return 10
end 'ten'

function twenty() returns int
	return 20
end 'twenty'

function main() returns ExitCode
	let a = async ten()
	let b = async twenty()
	let ra = await a
	let rb = await b
	return (ra + rb) as ExitCode
end 'main'
```
```exitcode
30
```

<!-- test: async-scheduler.sequence -->
<!-- targets: x64-windows -->
A spawn/await chain threads a value through two green threads.
```maxon

function inc(x int) returns int
	return x + 1
end 'inc'

function main() returns ExitCode
	let p1 = async inc(40)
	let r1 = await p1
	let p2 = async inc(r1)
	let r2 = await p2
	return r2 as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.spawn-arg -->
<!-- targets: x64-windows -->
A scalar argument is spilled into the green thread's argument buffer and read back by the callee.
```maxon

function sixtimes(x int) returns int
	return x * 6
end 'sixtimes'

function main() returns ExitCode
	let p = async sixtimes(7)
	let r = await p
	return r as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.multiple-args -->
<!-- targets: x64-windows -->
Several scalar arguments (positional first, then labelled) fill the argument buffer in parameter order.
```maxon

function combine(a int, b int, c int) returns int
	return a + b * c
end 'combine'

function main() returns ExitCode
	let p = async combine(2, b: 4, c: 10)
	let r = await p
	return r as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.nested -->
<!-- targets: x64-windows -->
A green thread can itself spawn and await another green thread — the current-GT tracking nests correctly.
```maxon

function leaf() returns int
	return 20
end 'leaf'

function middle() returns int
	let inner = async leaf()
	let got = await inner
	return got + 22
end 'middle'

function main() returns ExitCode
	let p = async middle()
	let r = await p
	return r as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.spawn-immediately-awaited -->
<!-- targets: x64-windows -->
`await async f()` — spawn and await in one expression — parses and runs.
```maxon

function answer() returns int
	return 42
end 'answer'

function main() returns ExitCode
	let r = await async answer()
	return r as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.spawn-not-awaited -->
<!-- targets: x64-windows -->
A spawned green thread that is never awaited never runs and cannot leak — its struct, stack and argument
buffer are slab/OS allocations invisible to the leak gate, so the program exits with `main`'s own code
rather than 101.
```maxon

function compute() returns int
	return 42
end 'compute'

function main() returns ExitCode
	let p = async compute()
	return 7 as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-scheduler.managed-result-refused -->
<!-- targets: x64-windows -->
An async function returning a managed `String` result is refused — the runtime captures the result through
a single integer register, so a managed heap pointer has no channel to await at this rung.
```maxon

function greet() returns String
	return "hi"
end 'greet'

function main() returns ExitCode
	let p = async greet()
	let r = await p
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:16: Unsupported: `async greet(…)` must return a scalar (int/bool) — the green-thread runtime captures the result through R8, so a managed (String/struct), float or void result has no channel to await at this rung (managed async is P1.5-B1c)
```

<!-- test: async-scheduler.managed-arg-refused -->
<!-- targets: x64-windows -->
A managed `String` argument to an async call is refused for the same reason.
```maxon

function takesInt(x int) returns int
	return x
end 'takesInt'

function main() returns ExitCode
	let s = "hi"
	let p = async takesInt(s)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:16: Unsupported: an `async` call argument must be a scalar (int/bool) — a managed (String/struct) or float argument travels in a register file the green-thread trampoline cannot fill at this rung (managed async is P1.5-B1c)
```

<!-- test: async-scheduler.float-param-refused -->
<!-- targets: x64-windows -->
An async callee with a float parameter is refused — the trampoline passes arguments through the integer
registers, so a float parameter (which is read from an XMM register) would read the wrong register file.
```maxon

function takesFloat(x float) returns int
	return 5
end 'takesFloat'

function main() returns ExitCode
	let p = async takesFloat(3)
	let r = await p
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:16: `async takesFloat(…)` — an async function's parameters must be scalars (int/bool); parameter 'x' is a managed or float type the green-thread trampoline passes through the wrong register file at this rung (managed async is P1.5-B1c)
```

<!-- test: async-scheduler.await-non-promise-refused -->
<!-- targets: x64-windows -->
`await` requires a Promise from an `async` call; awaiting a plain value is refused.
```maxon

function main() returns ExitCode
	let x = 5
	let r = await x
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:10: Unsupported: `await` applies to a Promise produced by an `async` call — the operand here is not an async spawn's result
```
