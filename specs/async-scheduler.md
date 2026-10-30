---
feature: async-scheduler
status: stable
keywords: [async, await, green-threads, scheduler, concurrency, promise]
category: concurrency
---

# Async / Await — the green-thread scheduler

## Documentation

`async f(args…)` creates a **coroutine** of the calling green thread, running `f`, and yields a `Promise`
handle; `await p` parks the caller until `p`'s coroutine completes and hands back its result. A coroutine runs
on the machine that holds its green thread's strand, one member of the strand at a time, and hands over to a
sibling only at a wait — an `await`, a `sleep`, a `Scheduler.yield()` or an I/O point. The GREEN THREAD around
them is preempted: one that has held its processor for 10 ms is stopped at its next function entry, `spawn`,
`async` call, service send, `await` or `awaitAny`, or when a service takes its next message, and put
behind every other runnable one (`specs/sched-preempt.md`).

```text
function compute() returns int
	return 42
end 'compute'

function main() returns ExitCode
	let p = async compute()   // a coroutine of main's green thread; p is a Promise
	let r = await p           // park until it completes, then take its result
	return r as ExitCode
end 'main'
```

An async call's arguments and its awaited result ride the green thread's INTEGER channel, so each is an
integer, a bool, or a managed value — a `String`, a struct, an array — which crosses as its one box pointer
(`async-await.managed-args-many` and `async-await.managed-generic-result` pin that road). A `float` is
refused at compile time with **E2015** in either position, because the trampoline is hand-written assembly
that fills the integer argument registers and captures R8, and never touches XMM; so are a value held at an
interface type and one at an opaque type parameter, which are released through a companion the green-thread
struct does not carry. A float or interface ARGUMENT handed to a parameter the callee reassigns is the
exception: it rides a cell the coroutine owns, and the cell is one word that carries a witness beside the
value (`a-float-argument-the-callee-reassigns-rides-a-cell`). A float INSIDE an async'd function is ordinary — that is
`a-float-argument-survives-a-stack-grow` below, whose subject is the stack grower's XMM save list rather
than the async channel.

## Targets — the one statement of the green-thread gate

⭐⭐ **THE GREEN-THREAD FAMILY CARRIES NO `<!-- unsupported-targets: … -->` MARKER, AND THAT IS THE
POINT: THE COMPILER ALREADY REFUSES IT, LOUDLY.** The context switch is hand-written ASSEMBLY and the
scheduler reaches the host's clock, its timed park and its per-OS-thread storage directly, so a lane serves
this family only once a backend has written those out. All four native lanes have (x64-windows first —
`X64GtRuntime`'s context switch, trampoline, relocating grower and Win32 substrate; then `Arm64GtRuntime`
and the two Linux images). A WASI component has no addressable call stack for a context switch to move,
so `SemanticCheck.requireTargetSupportsCallee` answers **E3104** there, naming the entry (`__gt_resched`,
for one). `sleep` and the monotonic clock are not in that family on wasm: `__gt_sleep` is a blocking WASI
`monotonic-clock` wait there and `__gt_now_ns` reads that clock.

⛔ **AND E3104 IS REPORTED, WHICH A MARKER IS NOT.** The harness counts a target-refused case as a
**SKIP** and prints it by name; a marker takes the case out of selection with nothing said anywhere. The
two are not equivalent ways of spelling one fact — one of them is the fact, and the other is the fact
made invisible. So this family states its gate by being refused, and a reader of the wasm lane can count
exactly which cases it did not run.

⚠ **IT IS NOT A PER-TARGET OPT-IN, and the test of that is what is refused and what is not.** Anything
decided BEFORE lowering — an arity, an operand type, a shape refusal, a linearity error — is
target-neutral and PASSES on wasm: the `-refused` cases in this file assert `E2015` and the
`double-await` family asserts `E3102`/`E3141`, all of them green on wasm32-wasi. A marker on one
of those would be hiding a green lane rather than describing a red one.

## Tests

<!-- test: async-scheduler.basic -->
An `async` coroutine runs its function and `await` collects the result.
```maxon

function compute() returns Integer
	Scheduler.yield()
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
Two green threads are spawned before either is awaited; both run and their results sum.
```maxon

function ten() returns Integer
	Scheduler.yield()
	return 10
end 'ten'

function twenty() returns Integer
	Scheduler.yield()
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
A spawn/await chain threads a value through two green threads.
```maxon

function inc(x Integer) returns Integer
	Scheduler.yield()
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
A scalar argument is spilled into the green thread's argument buffer and read back by the callee.
```maxon

function sixtimes(x Integer) returns Integer
	Scheduler.yield()
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
Several scalar arguments (positional first, then labelled) fill the argument buffer in parameter order.
```maxon

function combine(a Integer, b Integer, c Integer) returns Integer
	Scheduler.yield()
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

<!-- test: async-scheduler.a-float-argument-rides-the-float-argument-registers -->
A `float` argument handed to a parameter the callee only reads travels in the thread's float argument region,
which the trampoline loads into the float argument registers.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

function half(x Real) returns Integer
	Scheduler.yield()
	return trunc(x / 2.0)
end 'half'

function main() returns ExitCode
	let p = async half(84.0)
	return (await p) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.floats-and-integers-are-counted-by-register-file -->
The integer and float argument files are counted independently, so a float between two integers is float
slot 0 and the second integer is integer slot 1. Every argument carries a distinct weight, so one landing in
the wrong register shows in the total.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

function weigh(a Integer, x Real, b Integer, y Real, c Integer) returns Integer
	Scheduler.yield()
	let wholes = (trunc(x) as Integer) * 1000 + (trunc(y) as Integer) * 10000
	return a + b * 10 + c * 100 + wholes
end 'weigh'

function main() returns ExitCode
	let p = async weigh(1, x: 4.5, b: 2, y: 5.5, c: 3)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
54321
```

<!-- test: async-scheduler.an-integer-argument-is-widened-to-a-float-parameter -->
An integer handed to a `float` parameter is converted at the spawn, as an ordinary call converts it, so the
float argument region holds a double.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

function half(x Real) returns Integer
	Scheduler.yield()
	return trunc(x / 2.0)
end 'half'

function main() returns ExitCode
	let p = async half(84)
	return (await p) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.an-integer-argument-is-widened-into-a-float-cell -->
The same conversion at a parameter the callee reassigns, whose cell holds a double.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

function half(x Real) returns Integer
	Scheduler.yield()
	x = x / 2.0
	return trunc(x)
end 'half'

function main() returns ExitCode
	let p = async half(84)
	return (await p) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.a-float-argument-beside-a-reassigned-float-uses-both-files -->
`x` is read and travels in float slot 0; `y` is reassigned, so its cell address travels in integer slot 0.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

function blend(x Real, y Real) returns Integer
	Scheduler.yield()
	y = y + x
	return trunc(y)
end 'blend'

function main() returns ExitCode
	let p = async blend(1.5, y: 40.5)
	return (await p) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.a-float-argument-the-callee-reassigns-rides-a-cell -->
The same spawn, with a callee that reassigns its parameter: the argument rides a cell the coroutine owns, and
the cell is one integer word whatever it holds.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

function half(x Real) returns Integer
	Scheduler.yield()
	x = x / 2.0
	return trunc(x)
end 'half'

function main() returns ExitCode
	let p = async half(84.0)
	return (await p) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-scheduler.a-value-held-at-an-interface-type-rides-two-integer-slots -->
A value held at an interface type, handed to a parameter the callee only reads, travels as two words: the
value in its integer slot and its witness table in the integer slot after the declared arguments.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function measure(s Shape) returns Integer
	Scheduler.yield()
	return s.area()
end 'measure'

function shapeOf(side Integer) returns Shape
	return Square.create(side)
end 'shapeOf'

function main() returns ExitCode
	let p = async measure(shapeOf(6))
	return (await p) as ExitCode
end 'main'
```
```exitcode
36
```

<!-- test: async-scheduler.a-concrete-conformer-is-widened-to-an-interface-parameter-at-the-spawn -->
The spawn names the witness table of the conformer the argument widens through.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function measure(s Shape) returns Integer
	Scheduler.yield()
	return s.area()
end 'measure'

function main() returns ExitCode
	let p = async measure(Square.create(7))
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
49
```

<!-- test: async-scheduler.an-interface-argument-shares-the-files-with-floats-and-integers -->
The witness table takes the integer slot after the last integer argument, the float between them takes
float slot 0, and the callee reads all four.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function describe(a Integer, s Shape, x Real, b Integer) returns Integer
	Scheduler.yield()
	return a + s.area() * 10 + (trunc(x) as Integer) * 1000 + b * 100000
end 'describe'

function shapeOf(side Integer) returns Shape
	return Square.create(side)
end 'shapeOf'

function main() returns ExitCode
	let p = async describe(1, s: shapeOf(3), x: 2.5, b: 4)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
402091
```

<!-- test: async-scheduler.a-cancelled-spawn-releases-its-interface-argument -->
A spawn dropped before it runs gives back the reference it took to the interface value, through the witness
table stored beside it. The run is leak-free.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function measure(s Shape) returns Integer
	Scheduler.yield()
	return s.area()
end 'measure'

function shapeOf(side Integer) returns Shape
	return Square.create(side)
end 'shapeOf'

function main() returns ExitCode
	let p = async measure(shapeOf(5))
	p.cancel()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: async-scheduler.an-interface-argument-past-the-last-register-rides-the-thread-stack -->
Five integers and an interface value need seven integer slots: the value takes the sixth and its witness
table the seventh, which is past the six integer argument registers x64 passes and so rides the new thread's
stack.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function wide(a Integer, b Integer, c Integer, d Integer, e Integer, s Shape) returns Integer
	Scheduler.yield()
	return a + b + c + d + e + s.area()
end 'wide'

function shapeOf(side Integer) returns Shape
	return Square.create(side)
end 'shapeOf'

function main() returns ExitCode
	let p = async wide(1, b: 2, c: 3, d: 4, e: 5, s: shapeOf(3))
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
24
```

<!-- test: async-scheduler.eight-integer-arguments-fill-the-argument-registers -->
Eight integers are every integer argument register arm64 passes and two more than x64 has, so the last two
ride the stack there. Every argument carries a distinct weight.
```maxon
typealias Integer = int(i64.min to i64.max)

function weigh(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer) returns Integer
	Scheduler.yield()
	return a + b * 10 + c * 100 + d * 1000 + e * 10000 + f * 100000 + g * 1000000 + h * 10000000
end 'weigh'

function main() returns ExitCode
	let p = async weigh(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7, h: 8)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
87654321
```

<!-- test: async-scheduler.ten-integer-arguments-spill-onto-the-thread-stack -->
Ten integers overflow both targets' integer argument registers. The overflow words are copied onto the new
thread's stack in the order the callee reads them.
```maxon
typealias Integer = int(i64.min to i64.max)

function weigh(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer, i Integer, j Integer) returns Integer
	Scheduler.yield()
	return a + b * 10 + c * 100 + d * 1000 + e * 10000 + f * 100000 + g * 1000000 + h * 10000000 + i * 100000000 + j * 1000000000
end 'weigh'

function main() returns ExitCode
	let p = async weigh(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7, h: 8, i: 9, j: 10)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10987654321
```

<!-- test: async-scheduler.ten-float-arguments-spill-onto-the-thread-stack -->
The same with ten floats: six or eight ride the float argument registers and the rest the stack.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

function weigh(a Real, b Real, c Real, d Real, e Real, f Real, g Real, h Real, i Real, j Real) returns Integer
	Scheduler.yield()
	let low = (trunc(a) as Integer) + (trunc(b) as Integer) * 10 + (trunc(c) as Integer) * 100 + (trunc(d) as Integer) * 1000 + (trunc(e) as Integer) * 10000
	let high = (trunc(f) as Integer) * 100000 + (trunc(g) as Integer) * 1000000 + (trunc(h) as Integer) * 10000000 + (trunc(i) as Integer) * 100000000 + (trunc(j) as Integer) * 1000000000
	return low + high
end 'weigh'

function main() returns ExitCode
	let p = async weigh(1.5, b: 2.5, c: 3.5, d: 4.5, e: 5.5, f: 6.5, g: 7.5, h: 8.5, i: 9.5, j: 10.5)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10987654321
```

<!-- test: async-scheduler.integers-floats-and-interface-values-past-both-register-files -->
Ten integers, ten floats and two interface values: integers and floats overflow their own files
independently, the overflow shares one run of stack words in argument order, and both witness tables are
hidden integer arguments after the declared ones.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function show(i0 Integer, f0 Real, s0 Shape, i1 Integer, f1 Real, i2 Integer, f2 Real, i3 Integer, f3 Real, i4 Integer, f4 Real, i5 Integer, f5 Real, i6 Integer, f6 Real, i7 Integer, f7 Real, i8 Integer, f8 Real, i9 Integer, f9 Real, s1 Shape)
	Scheduler.yield()
	print("{i0} {i1} {i2} {i3} {i4} {i5} {i6} {i7} {i8} {i9} | {f0:.1} {f1:.1} {f2:.1} {f3:.1} {f4:.1} {f5:.1} {f6:.1} {f7:.1} {f8:.1} {f9:.1} | {s0.area()} {s1.area()}\n")
end 'show'

function main() returns ExitCode
	let p = async show(1, f0: 0.5, s0: Square.create(3), i1: 2, f1: 1.5, i2: 3, f2: 2.5, i3: 4, f3: 3.5, i4: 5, f4: 4.5, i5: 6, f5: 5.5, i6: 7, f6: 6.5, i7: 8, f7: 7.5, i8: 9, f8: 8.5, i9: 10, f9: 9.5, s1: Square.create(4))
	await p
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 3 4 5 6 7 8 9 10 | 0.5 1.5 2.5 3.5 4.5 5.5 6.5 7.5 8.5 9.5 | 9 16
```

<!-- test: async-scheduler.managed-arguments-past-the-registers-are-released-by-the-thread -->
Ten strings: the ones past the argument registers sit on the new thread's stack, and the thread's own copy of
each is given back after the await. The run is leak-free.
```maxon
typealias Integer = int(i64.min to i64.max)

function total(a String, b String, c String, d String, e String, f String, g String, h String, i String, j String) returns Integer
	Scheduler.yield()
	return a.byteLength() + b.byteLength() + c.byteLength() + d.byteLength() + e.byteLength() + f.byteLength() + g.byteLength() + h.byteLength() + i.byteLength() + j.byteLength()
end 'total'

function main() returns ExitCode
	let p = async total("a", b: "bb", c: "ccc", d: "dddd", e: "eeeee", f: "ffffff", g: "ggggggg", h: "hhhhhhhh", i: "iiiiiiiii", j: "jjjjjjjjjj")
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
55
```

<!-- test: async-scheduler.a-cancelled-spawn-releases-the-arguments-it-holds-past-the-registers -->
A spawn dropped before it runs gives back the strings, the interface value and the argument image alike. The
run is leak-free.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function total(a String, b String, c String, d String, e String, f String, g String, x Real, y Real, h String, i String, s Shape) returns Integer
	Scheduler.yield()
	let held = a.byteLength() + b.byteLength() + c.byteLength() + d.byteLength() + e.byteLength() + f.byteLength() + g.byteLength() + h.byteLength() + i.byteLength()
	return (held as Integer) + s.area() + (trunc(x + y) as Integer)
end 'total'

function main() returns ExitCode
	let p = async total("a", b: "bb", c: "ccc", d: "dddd", e: "eeeee", f: "ffffff", g: "ggggggg", x: 1.5, y: 2.5, h: "hhhhhhhh", i: "iiiiiiiii", s: Square.create(3))
	p.cancel()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: async-scheduler.parameters-the-callee-reassigns-ride-cells-past-the-registers -->
Two parameters past the eighth argument are reassigned, so their cell addresses travel on the stack on both
targets, and the coroutine owns the cells.
```maxon
typealias Integer = int(i64.min to i64.max)

function bump(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer, i Integer, j Integer) returns Integer
	Scheduler.yield()
	i = i + 100
	j = j + 200
	return a + b + c + d + e + f + g + h + i + j
end 'bump'

function main() returns ExitCode
	let p = async bump(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7, h: 8, i: 9, j: 10)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
355
```

<!-- test: async-scheduler.an-interface-value-and-its-witness-both-ride-the-thread-stack -->
Nine integers put the interface value on the stack on both targets, and its witness table follows it there.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function describe(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer, i Integer, s Shape) returns Integer
	Scheduler.yield()
	return a + b + c + d + e + f + g + h + i + s.area() * 1000
end 'describe'

function main() returns ExitCode
	let p = async describe(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7, h: 8, i: 9, s: Square.create(5))
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
25045
```

<!-- test: async-scheduler.stack-arguments-survive-a-stack-grow-before-the-callee-reads-them -->
The callee recurses far past the seed stack before it reads its last arguments, so the stack is relocated
with the overflow words still on it.
```maxon
typealias Integer = int(i64.min to i64.max)

function deep(n Integer) returns Integer
	if n == 0 'bottom'
		return 0
	end 'bottom'

	return 1 + deep(n - 1)
end 'deep'

function late(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer, i Integer, j Integer) returns Integer
	Scheduler.yield()
	let levels = deep(4000)
	return levels + a + b + c + d + e + f + g + h + i + j * 100
end 'late'

function main() returns ExitCode
	let p = async late(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7, h: 8, i: 9, j: 10)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5045
```

<!-- test: async-scheduler.a-generic-function-is-spawned-at-its-inferred-instance -->
`async` infers a generic function's type parameters from its arguments exactly as a call does, and spawns the
instance they fix.
```maxon
typealias Integer = int(i64.min to i64.max)

function larger(a T, b T) uses T returns T where T is Comparable
	Scheduler.yield()

	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

function main() returns ExitCode
	let p = async larger(30 as Integer, b: 42)
	let q = async larger("ab", b: "cd")
	print("{await q}\n")
	return (await p) as ExitCode
end 'main'
```
```stdout
cd
```
```exitcode
42
```

<!-- test: async-scheduler.nested -->
A green thread can itself spawn and await another green thread — the current-GT tracking nests correctly.
```maxon

function leaf() returns Integer
	Scheduler.yield()
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
`await async f()` — spawn and await in one expression — parses and runs.
```maxon

function answer() returns Integer
	Scheduler.yield()
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
A spawned green thread that is never awaited never runs and cannot leak — its struct, stack and argument
buffer are slab/OS allocations invisible to the leak gate, so the program exits with `main`'s own code
rather than 101.
```maxon

function compute() returns Integer
	Scheduler.yield()
	return 42
end 'compute'

function main() returns ExitCode
	_ = async compute()
	return 7
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: async-scheduler.await-loop-bounded -->
A spawn/await loop stays bounded: each completed thread's record keeps its stack when it is still the
current seed size, so the next spawn takes record and stack together from the free list, and a stack the
relocating morestack resized is returned to the stack pool as its runner finishes with the thread. 5000 iterations
hold at most one resident stack at a time and exit cleanly. (Without it, every spawn would leak its stack
commit — invisible to the `__mm` leak gate — and exhaust commit on a bounded-pagefile machine.) This loop is also a LEAK GATE on the GT struct + its inline
arg buffer: each completed thread's struct is recycled onto the free-list and the completion-based
The green-thread live count is balanced to zero, so a clean exit (0) proves nothing leaked. The struct and its arg
buffer are slab allocations that are not boxes, so the heap leak gate's tracked live column never sees them.
```maxon

function noop() returns Integer
	Scheduler.yield()
	return 0
end 'noop'

function main() returns ExitCode
	var i = 0 as Integer
	var acc = 0 as Integer
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
The GT-struct free-list is exercised AND a recycled struct is reset where it must be: five spawn/awaits run
in sequence, each reusing the struct the previous completed thread pushed onto the free-list.
Each thread computes `2 * i` from its argument, so `2+4+6+8+10 = 30` proves every recycled struct actually RAN
its function. A recycled struct carries its previous tenant's fields (it lives outside the always-zeroing slab),
so a spawn that did not zero its status would inherit `status == completed` and its `await` would
short-circuit to the PRIOR result without ever running the new thread — a wrong sum, not 30.
```maxon

function dbl(x Integer) returns Integer
	Scheduler.yield()
	return x * 2
end 'dbl'

function main() returns ExitCode
	var i = 1 as Integer
	var acc = 0 as Integer
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
A promise's struct must survive until ITS OWN `await`, even when the thread completes early while a DIFFERENT
await is parked. While `main` is parked on `await p2`, its strand runs `p1` first, FIFO; `p1` is
then a completed-but-un-awaited handle. Two intervening `async` spawns must NOT recycle `p1`'s struct — only `p1`'s
own `await` may. `p4` therefore gets a distinct struct, and `await p1` reads `p1`'s real result (10), not `p4`'s
(40). `10+20+30+40 = 100`. Reclaiming at completion instead of at await would return 130 here (a silent wrong
answer).
```maxon

function w(x Integer) returns Integer
	Scheduler.yield()
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
Two green threads each hold TEN f64 locals across a `sleep`, interleaved so each is suspended while
the other is actively occupying xmm6–15.

**The context switch is the one call whose callee-saved promise is kept by hand.** Since Wave 2
xmm6–15 are callee-saved, so the register allocator believes a float in xmm6 survives a
`call __gt_context_switch` — and that call does not return to its caller: it switches to another
green thread's stack, which runs arbitrary user code, and comes back only later. The switch has no
prologue and no coloring, so it must save that half explicitly (`X64GtRuntime`), and this is the
test that says so: with the switch's `movsd` saves removed, `threadA` returns **5524** — the sum of
`threadB`'s floats — instead of 79. Only a RUN can see this.

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
A recursive float-taking function runs on a green thread's 2 KB stack, so `__gt_morestack` fires
part-way down the recursion and relocates the stack underneath it.

**The stack guard is emitted at offset 0 of the entry block — before `push rbp` and before the
`param` captures — so an incoming FLOAT argument is still sitting in xmm0–5 when the guard `call`s
`__gt_morestack`**, which calls `VirtualAlloc`/`VirtualFree`. Those are Win64-volatile in xmm0–5, so
the grower must save and restore that half by hand exactly as it saves the thirteen GPRs
(`X64GtRuntime.morestackSavedXmmOrder`). Without it `x` reads 0 from the level the guard fires at
downwards, and since `0 * 2.0` is 0 for ever the sweep saturates: **20 / 60 / 60 / 60** instead of
20 / 60 / 140 / 300. The same recursion run synchronously is correct, because nothing grows a
non-GT stack — only a RUN through `await async` can see this.

`scale(20.0, depth: n)` sums 20 + 40 + … + 20·2ⁿ, so the four depths are 20, 60, 140, 300; each
mismatch returns its own exit code so a partial corruption names the level it started at.
```maxon
function scale(x Real, depth Integer) returns Integer
	if depth == 0 'base'
		return trunc(x) as Integer
	end 'base'
	return (trunc(x) as Integer) + scale(x * 2.0, depth: depth - 1)
end 'scale'

function sweep() returns Integer
	Scheduler.yield()
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
The sibling of the test above, widened from one FP argument register to **all six**: `six` spends
every one of the parser's six parameter slots on a float, so xmm0–5 are ALL live at the stack guard
when `__gt_morestack` fires. A save list that covered only some of them would still pass the
one-argument test.

Each level contributes `trunc(1.5)+trunc(2.5)+trunc(3.5)+trunc(4.5)+trunc(5.5)` = 15, and the
recursion runs while `a >= 1.0` from 6.0 down to 0.0 — seven levels, 105. A destroyed xmm0 ends the
recursion at the first level (15); a destroyed xmm1–5 drops that register's term from every level.
```maxon
function six(a Real, b Real, c Real, d Real, e Real, f Real) returns Integer
	let sum = (trunc(b) + trunc(c) + trunc(d) + trunc(e) + trunc(f)) as Integer
	if a < 1.0 'base'
		return sum
	end 'base'
	return sum + six(a - 1.0, b: b, c: c, d: d, e: e, f: f)
end 'six'

function fpArgs() returns Integer
	Scheduler.yield()
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

<!-- test: async-scheduler.gt-recycle-a-reclaimed-struct-serves-the-next-spawn -->
<!-- procs: 1 -->
**A GREEN-THREAD STRUCT IS NEVER HANDED BACK TO THE ALLOCATOR WHILE THE SCHEDULER RUNS.** Go keeps its `g`
records type-stable — freed onto a per-P list and reused, never returned to the heap — because the scheduler
reads and writes them without holding their owner's lock: sysmon poisons a running thread's stack guard,
a signal handler reads the interrupted thread's record. Here the reader that outlives its thread is
`awaitAny` over a promise already awaited (`await-any.md`), and a record handed back to the slab would let
that read land in whatever the slab gave the memory to next.

Two hundred spawn-and-await rounds, one after another: the first spawn has nothing to reuse, and each of the
other 199 takes the record the round before it gave back. `Scheduler.counters().recycledThreads` counts the
spawns a free list served. The case runs at one processor, because a reuse count is exact only while every
reclaim and every spawn share one processor's list (`gt-recycle-a-hundred-returned-records-serve-the-next-hundred-spawns`
says why).
```maxon
typealias Integer = int(i64.min to i64.max)

function square(n Integer) returns Integer
	Scheduler.yield()
	return n * n
end 'square'

function main() returns ExitCode
	var sum = 0 as Integer
	var i = 0 as Integer
	while i < 200 'rounds'
		let p = async square(i)
		sum = sum + await p
		i = i + 1
	end 'rounds'
	print("sum={sum} recycled={Scheduler.counters().recycledThreads}\n")
	return 0
end 'main'
```
```stdout
sum=2646700 recycled=199
```
```exitcode
0
```

<!-- test: async-scheduler.gt-recycle-a-hundred-returned-records-serve-the-next-hundred-spawns -->
<!-- procs: 1 -->
**EVERY RECORD GIVEN BACK IS THERE FOR THE NEXT SPAWN.** A hundred threads are all outstanding before any is
awaited, so no spawn in the first wave can reuse anything, and all hundred records are given back together.
The free lists are Go's: one per processor, which sheds to one global list when it reaches 64 records and
refills from it 32 at a time, so the second wave finds the records that `main`'s processor kept and the ones
it shed.
⛔ **THE CASE RUNS AT ONE PROCESSOR, WHICH IS WHAT MAKES 100 EXACT.** A record is reclaimed onto the list of the
processor `main` holds at that moment, and `main` does not keep one processor for a run: the system monitor
preempts a strand that has held its processor for 10 ms of wall time, which a host that holds the process
off a core reaches in a short program, and a preempted `main` is taken from the global queue by any machine.
A record on another processor's list is out of a spawn's reach, so at several processors a reuse count can come
up short with nothing lost. At one processor there is one local list and the global ones, which it sheds to
and refills from, so every record is reachable.
Whether a spawn reuses a record or carves a fresh one does not reach the compiler's own memory, which
`scale-test` measures bit-for-bit: records come from the scheduler's own arena, outside the counted allocator.
Every one of the second wave's hundred spawns is served from a list, so a record lost or freed on the way back
reads below 100.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function twice(n Integer) returns Integer
	Scheduler.yield()
	return n + n
end 'twice'

function wave(base Integer) returns Integer
	var ps = IntPromiseArray.create()
	var i = 0 as Integer
	while i < 100 'spawnEach'
		ps.push(async twice(base + i))
		i = i + 1
	end 'spawnEach'
	var sum = 0 as Integer
	var k = 0 as ElementIndex
	while k < 100 'awaitEach'
		let p = try ps.get(k) otherwise panic("ps.get OOB at {k} — bounded by the pushes above")
		sum = sum + await p
		k = k + 1
	end 'awaitEach'
	return sum
end 'wave'

function main() returns ExitCode
	let first = wave(0)
	let afterFirst = Scheduler.counters().recycledThreads
	let second = wave(1000)
	let afterSecond = Scheduler.counters().recycledThreads
	print("first={first} second={second} wave1={afterFirst} wave2={afterSecond - afterFirst}\n")
	return 0
end 'main'
```
```stdout
first=9900 second=209900 wave1=0 wave2=100
```
```exitcode
0
```
