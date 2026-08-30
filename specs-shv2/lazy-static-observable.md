---
feature: lazy-static-observable
status: experimental
keywords: [static, lazy, initializer, first-access, side-effect, cycle]
category: language
---

# Lazy Static Initializers — the OBSERVABLE half

## Documentation

`specs/lazy-static.md` defines the semantics: a static field's initializer is *"evaluated the first time
the static field is accessed"*, and *"after initialization, subsequent accesses return the cached value"*.

**⛔ Every one of that file's twelve cases READS the static it declares, so not one of them can tell
laziness from eager initialization.** `lazy-static.initialized-once` prints `1 1 1 1` — which an initializer
run once before `main` satisfies exactly as well as one run on first access. shv2 passed all twelve while
running every static initializer eagerly in `__module_init`, which is a spec passing 12/12 having tested
nothing of its subject.

This file holds the cases that CAN see it. They are shv2-only because they turn on *when* code runs rather
than on what a program computes, and the canonical file is not ours to extend.

### What separates the two positions

**The scope decides, not the initializer.** A `static` member is lazy; a module-level `var` is not — and
both halves are canonical:

- `specs/lazy-static.md`: a static field's initializer runs *"the first time the static field is accessed"*.
- `specs/dead-top-level-var-elim.md:31`: a module-level var's initializer *"still runs even when the slot is
  dead"*.

MEASURED on the C# bootstrap with ONE program moved between the two positions: **`static var` prints `0`,
module-level `var` prints `1`.** The first two cases below are that pair.

### The mechanism, and where it differs from the references

shv2 gives each lazy static a synthesized `__lazy_init$<label>` routine and emits one `call` to it ahead of
every access (`Parser.emitGlobalAddr` — the single door every global read and write passes through). The
guard is **the slot itself**: the `.data` image starts it at zero and a built record is a heap address, so
there is no separate flag that could disagree with the value — which is what makes a `static var`
REASSIGNMENT correct without the assignment path having to know the guard exists.

Both references instead emit the test INLINE at every load. That shape is why
`specs/lazy-static.md:353-361` exists: a second load in one function laid an init block between two guards
and produced an **endless loop**. Out of line there is one branch per STATIC rather than one per ACCESS, so
that failure mode is unreachable.

## Tests

<!-- test: never-accessed-static-does-not-initialize -->
### A static that is never accessed never runs its initializer

The RED-GATE CONTROL for the whole feature. Under eager initialization this prints `1`; under the semantics
`specs/lazy-static.md` defines it prints `0`. The C# bootstrap prints `0` (MEASURED).

```maxon
typealias Count = int(0 to u64.max)

type Counter
	static var initCount = 0
	static var cached = Counter.createInstance()
	export var id as Count

	static function createInstance() returns Counter
		Counter.initCount = Counter.initCount + 1
		return Counter{id: Counter.initCount}
	end 'createInstance'

	export static function getInitCount() returns Count
		return Counter.initCount
	end 'getInitCount'
end 'Counter'

function main() returns ExitCode
	print("{Counter.getInitCount()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: never-accessed-module-level-var-does-initialize -->
### The same binding at MODULE scope still initializes eagerly

The other half of the pair, and the reason the scope is recorded on the declaration rather than inferred.
Identical program with `cached` moved out of the `type` body: the initializer runs before `main` even though
nothing reads the slot. The C# bootstrap prints `1` (MEASURED).

```maxon
typealias Count = int(0 to u64.max)

type Counter
	static var initCount = 0
	export var id as Count

	static function createInstance() returns Counter
		Counter.initCount = Counter.initCount + 1
		return Counter{id: Counter.initCount}
	end 'createInstance'

	export static function getInitCount() returns Count
		return Counter.initCount
	end 'getInitCount'
end 'Counter'

var cached = Counter.createInstance()

function main() returns ExitCode
	print("{Counter.getInitCount()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: initializer-runs-at-the-first-access-not-before -->
### The initializer runs AT the first access, in program order

Not merely "not before `main`" — at the access itself. `before` is printed while the static is still
uninitialized, so the initializer's own output must land between the two markers rather than ahead of both.

```maxon
typealias Count = int(0 to u64.max)

type Late
	static var value = Late.build()
	export var n as Count

	static function build() returns Late
		print("init ")
		return Late{n: 7}
	end 'build'

	export static function get() returns Count
		return Late.value.n
	end 'get'
end 'Late'

function main() returns ExitCode
	print("before ")
	let n = Late.get()
	print("after {n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
before init after 7
```

<!-- test: initializer-runs-exactly-once-across-many-accesses -->
### Many accesses, one initialization

The caching half of the semantics, asserted where `lazy-static.initialized-once` cannot: the counter is read
AFTER several accesses rather than being inferred from the cached value's own field.

```maxon
typealias Count = int(0 to u64.max)

type Once
	static var runs = 0
	static var value = Once.build()
	export var n as Count

	static function build() returns Once
		Once.runs = Once.runs + 1
		return Once{n: 5}
	end 'build'

	export static function get() returns Count
		return Once.value.n
	end 'get'

	export static function runCount() returns Count
		return Once.runs
	end 'runCount'
end 'Once'

function main() returns ExitCode
	let a = Once.get()
	let b = Once.get()
	let c = Once.get()
	print("{a} {b} {c} {Once.runCount()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 5 5 1
```

<!-- test: a-write-is-an-access-and-forces-the-initializer -->
### Assigning a `static var` forces its initializer first

⛔ **THIS CASE PINS A SEMANTIC CHOICE, NOT A CONSEQUENCE — and it is the one case in this file that does.**
Everything else here follows from `specs/lazy-static.md`'s sentence; this one INTERPRETS it. Canonical says
the initializer runs *"the first time the static field is accessed"* and draws no read/write distinction, so
shv2 reads a store as an access. The visible consequence is that a static which is only ever WRITTEN still
runs its initializer's side effects — `runs` is 1 below even though the value `build()` produced is thrown
away by the very next store.

Half of it is mechanically forced: the assignment path releases the slot's old value before storing the new
one, so without forcing, a first-access assignment would decref a slot that was never filled. The other
half — that the side effects run at all — is the choice. A compiler could instead store into the null slot
without releasing and never run the initializer.

⚠ **THE ORACLE WAS MEASURED AND CANNOT ARBITRATE.** On this shape the C# bootstrap does not compile:
`error E9001: Unresolved global: Box.slot.__initialized`, thrown out of `X86CodeEmitter.ResolveGlobals` — an
internal error, i.e. a defect on its own write-only-static path. So this expectation rests on the reading
above rather than on agreement with a reference, and a future ruling may move it.

```maxon
typealias Count = int(0 to u64.max)

type Slot
	static var runs = 0
	static var value = Slot.build()
	export var n as Count

	static function build() returns Slot
		Slot.runs = Slot.runs + 1
		return Slot{n: 1}
	end 'build'

	static function of(n Count) returns Self
		return Self{n: n}
	end 'of'

	export static function set(s Slot)
		Slot.value = s
	end 'set'

	export static function get() returns Count
		return Slot.value.n
	end 'get'

	export static function runCount() returns Count
		return Slot.runs
	end 'runCount'
end 'Slot'

function main() returns ExitCode
	Slot.set(Slot.of(99))
	print("{Slot.get()} {Slot.runCount()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99 1
```

<!-- test: reassignment-does-not-re-run-the-initializer -->
### A reassigned `static var` stays initialized

The guard is the slot's own value, so storing a fresh record leaves it satisfied: a later access must return
the assigned value and must not re-enter the initializer.

```maxon
typealias Count = int(0 to u64.max)

type Reassigned
	static var runs = 0
	static var value = Reassigned.build()
	export var n as Count

	static function build() returns Reassigned
		Reassigned.runs = Reassigned.runs + 1
		return Reassigned{n: 1}
	end 'build'

	static function of(n Count) returns Self
		return Self{n: n}
	end 'of'

	export static function set(s Reassigned)
		Reassigned.value = s
	end 'set'

	export static function get() returns Count
		return Reassigned.value.n
	end 'get'

	export static function runCount() returns Count
		return Reassigned.runs
	end 'runCount'
end 'Reassigned'

function main() returns ExitCode
	let first = Reassigned.get()
	Reassigned.set(Reassigned.of(42))
	let second = Reassigned.get()
	let third = Reassigned.get()
	print("{first} {second} {third} {Reassigned.runCount()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 42 42 1
```

<!-- test: one-static-initializer-reads-another -->
### A lazy static whose initializer reads a different lazy static

Initialization is on demand, so the inner static is forced by the outer one's initializer and both settle in
the order the reads imply. `Inner` is accessed only from `Outer`'s initializer, which is what makes this an
ordering case rather than two independent statics.

```maxon
typealias Count = int(0 to u64.max)

type Inner
	static var value = Inner.build()
	export var n as Count

	static function build() returns Inner
		print("inner ")
		return Inner{n: 3}
	end 'build'

	export static function get() returns Count
		return Inner.value.n
	end 'get'
end 'Inner'

type Outer
	static var value = Outer.build()
	export var n as Count

	static function build() returns Outer
		print("outer ")
		return Outer{n: Inner.get() + 1}
	end 'build'

	export static function get() returns Count
		return Outer.value.n
	end 'get'
end 'Outer'

function main() returns ExitCode
	print("start ")
	print("{Outer.get()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
start outer inner 4
```

<!-- test: a-managed-static-is-released-after-main -->
### An accessed managed static is released exactly once

The leak gate armed on the lazy path: the slot's record is built on first access and dropped by
`__maxon_global_cleanup` after `main`. Exit 101 would mean the cleanup missed it; a crash would mean it
dropped it twice.

```maxon
type Cached
	static var text = Cached.build()

	static function build() returns String
		return "held"
	end 'build'

	export static function get() returns String
		return Cached.text
	end 'get'
end 'Cached'

function main() returns ExitCode
	print("{Cached.get()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
held
```

<!-- test: forced-on-a-green-thread-across-a-stack-grow -->
<!-- targets: x64-windows -->
### The initializer runs on a GREEN THREAD's stack, and keeps its guard

`__lazy_init#<label>` is compiler-synthesized but its body is USER code, and it runs inside `main` on
whatever green thread first touches the static — unlike `__module_init` / `__maxon_global_cleanup`, which run
outside `main` where no green thread exists. It was therefore exempted from the green-thread stack guard by a
predicate that meant "is this the compiler's scaffolding?" (`§6` review finding 1); the exemption is now
`TargetPrinter.skipsGreenThreadStackGuard`, which that routine does not satisfy.

The recursion drives several grow-and-relocate rounds before the static is touched, so the initializer's
frame lands on a relocated stack; the second thread then finds the cache already filled. `built` appears
once.

```maxon
typealias Count = int(0 to u64.max)

type Cache
	static var value = Cache.build()
	export var n as Count

	static function build() returns Cache
		print("built ")
		return Cache{n: 21}
	end 'build'

	export static function get() returns Count
		return Cache.value.n
	end 'get'
end 'Cache'

function deepThenRead(n Integer) returns Integer
	if n == 0 'base'
		return Cache.get()
	end 'base'
	return deepThenRead(n - 1)
end 'deepThenRead'

function main() returns ExitCode
	let p = async deepThenRead(200)
	let r = await p
	let q = async deepThenRead(150)
	let s = await q
	print("{r} {s}")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
built 21 21
```

<!-- test: an-unaccessed-managed-static-is-not-dropped -->
### An unaccessed managed static is not dropped either

The other side of the cleanup: a static nothing ever accessed still holds the image's zero when
`__maxon_global_cleanup` runs, so its drop must be null-guarded. Unguarded this decrefs a null pointer and
the program dies on exit having done nothing wrong. `other` exists so the program has one managed static
that IS accessed, which is what makes the cleanup run at all.

```maxon
type Untouched
	static var never = Untouched.build()
	static var other = Untouched.build()

	static function build() returns String
		return "x"
	end 'build'

	export static function get() returns String
		return Untouched.other
	end 'get'
end 'Untouched'

function main() returns ExitCode
	print("{Untouched.get()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
x
```
