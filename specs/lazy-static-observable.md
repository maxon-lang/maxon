---
feature: lazy-static-observable
status: experimental
keywords: [static, initializer, eager, before-main, dependency-order, side-effect, cycle]
category: language
---

# Static Initializers — the OBSERVABLE half

## Documentation

A `static let` initializer runs **before `main`**, exactly once, whether or not anything ever
reads the slot. Ordering among them is by DEPENDENCY: an initializer that reads another static runs after
the one it reads, so no initializer can observe a slot that has not settled.

**⛔ A COMPUTED VALUE CANNOT SEE WHEN ITS INITIALIZER RAN.** A case that reads a static and prints what it
holds gets the same output under any timing whatsoever — which is why every case of
`specs/lazy-static.md` passes without pinning one word of the above. The cases here observe the timing
itself: through a side effect in an initializer, through a slot nothing ever reads, and through an order
that two initializers cannot both be given.

### The scope of the declaration does not change the timing

A `static` member and a module-level `let` agree — both initializers run before `main`, and neither needs a
read to force one. The first two cases below are that pair, on one program moved between the two positions.

### A LIBRARY declaration is the one exception, and it is a COST rule

`stdlib/` is loaded into every compile whether the program names it or not, so a library's statics are not
the program's. Keeping every one of them for its side effects would charge `function main() returns ExitCode
/ return 7` for eleven `CharacterSet` presets and the `Set`/`Array` cone behind them. A library static is
therefore kept on LIVENESS alone: read by reachable code and it is built before `main` like any other; read
by nothing and neither it nor its initializer is emitted.

⚠ **THE EFFECT THAT SURVIVES THE DROP IS THE ONE ON ITS OWN SLOT, AND NOTHING ELSE IS PROMISED.** A library
whose static initializer registered itself into some other live structure would lose that registration; every
library initializer in the tree constructs and returns. The half a case here can observe is the live one, and
that is the case below.

### Ordering is what the reads imply

Declaration order does not order initialization, because a program may declare a type after the type whose
initializer reads it. The two ordering cases below declare the same pair in opposite orders and must print
the same thing. A cycle implies no order at all, so it is refused at compile time rather than settled to
some value at run time.

## Tests

<!-- test: never-accessed-static-does-initialize -->
### A static that is never accessed still runs its initializer

The control for the whole feature. Nothing reads `Counter.cached`, so the line its initializer prints is
the only evidence that the initializer ran at all.

```maxon
typealias Count = int(0 to u64.max)

type Counter
	static let cached = Counter.createInstance()
	export var id as Count

	static function createInstance() returns Counter
		print("init ")
		return Counter{id: 1}
	end 'createInstance'
end 'Counter'

function main() returns ExitCode
	print("main")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
init main
```

<!-- test: never-accessed-module-level-let-does-initialize -->
### The same binding at MODULE scope initializes too

The other half of the pair: the identical program with `cached` moved out of the `type` body as a module-level `let`. Where the
binding is declared decides nothing about when its initializer runs, and the two halves agreeing is what
makes that a rule rather than a coincidence of one scope.

```maxon
typealias Count = int(0 to u64.max)

type Counter
	export var id as Count

	static function createInstance() returns Counter
		print("init ")
		return Counter{id: 1}
	end 'createInstance'
end 'Counter'

let cached = Counter.createInstance()

function main() returns ExitCode
	print("main")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
init main
```

<!-- test: initializer-runs-before-main -->
### The initializer runs before `main`

Not merely "before the first read" — before `main`'s first statement. Both markers are printed by `main`, so
the initializer's own output must land ahead of both of them rather than between them.

```maxon
typealias Count = int(0 to u64.max)

type Late
	static let value = Late.build()
	export var n as Count

	static function build() returns Late
		print("init ")
		return Late{n: 7}
	end 'build'

	static function get() returns Count
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
init before after 7
```

<!-- test: initializer-runs-exactly-once-across-many-accesses -->
### Many accesses, one initialization

The initializer prints, so a second run would show here as a second `built` even though every access
answers with the same record.

```maxon
typealias Count = int(0 to u64.max)

type Once
	static let value = Once.build()
	export var n as Count

	static function build() returns Once
		print("built ")
		return Once{n: 5}
	end 'build'

	static function get() returns Count
		return Once.value.n
	end 'get'
end 'Once'

function main() returns ExitCode
	let a = Once.get()
	let b = Once.get()
	let c = Once.get()
	print("{a} {b} {c}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
built 5 5 5
```

<!-- test: one-static-initializer-reads-another -->
### A static initializer that reads another static

`Inner` is read only from `Outer`'s initializer, which is what makes this an ordering case rather than two
independent statics. Both run before `main`, and `Inner` settles first because `Outer` needs the value.

⭐ **That is DEPENDENCY order, not declaration order.** The two agree in this program; the next case is what
separates them.

```maxon
typealias Count = int(0 to u64.max)

type Inner
	static let value = Inner.build()
	export var n as Count

	static function build() returns Inner
		print("inner ")
		return Inner{n: 3}
	end 'build'

	static function get() returns Count
		return Inner.value.n
	end 'get'
end 'Inner'

type Outer
	static let value = Outer.build()
	export var n as Count

	static function build() returns Outer
		print("outer ")
		return Outer{n: Inner.get() + 1}
	end 'build'

	static function get() returns Count
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
inner outer start 4
```

<!-- test: a-static-initializer-reads-one-declared-later -->
### A static initializer that reads one declared LATER

The same pair with the declarations swapped, and therefore the same output: `Outer` is declared first and
still runs second, because it reads `Inner`. Running the two in declaration order would read `Inner`'s slot
before anything had filled it, and answer `1` or dereference a null.

```maxon
typealias Count = int(0 to u64.max)

type Outer
	static let value = Outer.build()
	export var n as Count

	static function build() returns Outer
		print("outer ")
		return Outer{n: Inner.get() + 1}
	end 'build'

	static function get() returns Count
		return Outer.value.n
	end 'get'
end 'Outer'

type Inner
	static let value = Inner.build()
	export var n as Count

	static function build() returns Inner
		print("inner ")
		return Inner{n: 3}
	end 'build'

	static function get() returns Count
		return Inner.value.n
	end 'get'
end 'Inner'

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
inner outer start 4
```

<!-- test: a-cycle-between-static-initializers-is-refused -->
### Two static initializers that read each other are refused

Neither can run first, so there is no order to give them and no value either slot could honestly hold. The
program is refused rather than resolved: a zero, a null dereference or an unbounded recursion would each be
a wrong answer handed over at run time to a question that is settled at compile time.

E2012 is the code, because the fact is the declaration cycle and not the construct it was found in — the
same code a cycle among global constants gets. The diagnostic anchors on the declaration at which the cycle
closes and names the whole cycle, so the message identifies the loop rather than one member of it.

```maxon
typealias Count = int(0 to u64.max)

type Ping
	static let value = Ping.build()
	export var n as Count

	static function build() returns Ping
		return Ping{n: Pong.get() + 1}
	end 'build'

	static function get() returns Count
		return Ping.value.n
	end 'get'
end 'Ping'

type Pong
	static let value = Pong.build()
	export var n as Count

	static function build() returns Pong
		return Pong{n: Ping.get() + 1}
	end 'build'

	static function get() returns Count
		return Pong.value.n
	end 'get'
end 'Pong'

function main() returns ExitCode
	print("{Ping.get()}")
	return 0
end 'main'
```
```maxoncstderr
error E2012: <fragment>:18:13: Circular dependency detected among global initializers: Ping.value, Pong.value
```

<!-- test: a-static-initializer-that-reads-itself-is-refused -->
### A static initializer that reads its own slot is refused

The one-member arc. There is no other global to name, so the diagnostic says what the single global does
instead of printing a list of one and leaving the author to infer the shape.

```maxon
typealias Count = int(0 to u64.max)

type Loop
	static let value = Loop.build()
	export var n as Count

	static function build() returns Loop
		return Loop{n: Loop.get() + 1}
	end 'build'

	static function get() returns Count
		return Loop.value.n
	end 'get'
end 'Loop'

function main() returns ExitCode
	print("{Loop.get()}")
	return 0
end 'main'
```
```maxoncstderr
error E2012: <fragment>:5:13: Circular dependency detected among global initializers: Loop.value reads its own slot, directly or through a function its initializer calls
```

<!-- test: a-dependency-chain-initializes-from-the-bottom -->
### A dependency chain twenty deep

The pair above separates declaration order from dependency order over two globals; this separates them over
twenty-one, declared in the exact reverse of the order they must run in. `s20` prints its own `n`, which is
one more than `s19`'s and so on down to `s00`, so the answer is the depth: any global built before the one it
reads contributes a zero and the total falls short.

```maxon
typealias Count = int(0 to u64.max)

type Chain
	static let s20 = Chain.build20()
	static let s19 = Chain.build19()
	static let s18 = Chain.build18()
	static let s17 = Chain.build17()
	static let s16 = Chain.build16()
	static let s15 = Chain.build15()
	static let s14 = Chain.build14()
	static let s13 = Chain.build13()
	static let s12 = Chain.build12()
	static let s11 = Chain.build11()
	static let s10 = Chain.build10()
	static let s09 = Chain.build09()
	static let s08 = Chain.build08()
	static let s07 = Chain.build07()
	static let s06 = Chain.build06()
	static let s05 = Chain.build05()
	static let s04 = Chain.build04()
	static let s03 = Chain.build03()
	static let s02 = Chain.build02()
	static let s01 = Chain.build01()
	static let s00 = Chain.build00()
	export var n as Count

	static function build00() returns Chain
		return Chain{n: 0}
	end 'build00'

	static function build01() returns Chain
		return Chain{n: Chain.s00.n + 1}
	end 'build01'

	static function build02() returns Chain
		return Chain{n: Chain.s01.n + 1}
	end 'build02'

	static function build03() returns Chain
		return Chain{n: Chain.s02.n + 1}
	end 'build03'

	static function build04() returns Chain
		return Chain{n: Chain.s03.n + 1}
	end 'build04'

	static function build05() returns Chain
		return Chain{n: Chain.s04.n + 1}
	end 'build05'

	static function build06() returns Chain
		return Chain{n: Chain.s05.n + 1}
	end 'build06'

	static function build07() returns Chain
		return Chain{n: Chain.s06.n + 1}
	end 'build07'

	static function build08() returns Chain
		return Chain{n: Chain.s07.n + 1}
	end 'build08'

	static function build09() returns Chain
		return Chain{n: Chain.s08.n + 1}
	end 'build09'

	static function build10() returns Chain
		return Chain{n: Chain.s09.n + 1}
	end 'build10'

	static function build11() returns Chain
		return Chain{n: Chain.s10.n + 1}
	end 'build11'

	static function build12() returns Chain
		return Chain{n: Chain.s11.n + 1}
	end 'build12'

	static function build13() returns Chain
		return Chain{n: Chain.s12.n + 1}
	end 'build13'

	static function build14() returns Chain
		return Chain{n: Chain.s13.n + 1}
	end 'build14'

	static function build15() returns Chain
		return Chain{n: Chain.s14.n + 1}
	end 'build15'

	static function build16() returns Chain
		return Chain{n: Chain.s15.n + 1}
	end 'build16'

	static function build17() returns Chain
		return Chain{n: Chain.s16.n + 1}
	end 'build17'

	static function build18() returns Chain
		return Chain{n: Chain.s17.n + 1}
	end 'build18'

	static function build19() returns Chain
		return Chain{n: Chain.s18.n + 1}
	end 'build19'

	static function build20() returns Chain
		return Chain{n: Chain.s19.n + 1}
	end 'build20'

	static function top() returns Count
		return Chain.s20.n
	end 'top'
end 'Chain'

function main() returns ExitCode
	print("{Chain.top()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20
```

<!-- test: a-library-static-the-program-reads-is-built-before-main -->
### A library static the program reads is built before `main`

`CharacterSet.whitespaces` answers with a `static let` declared by `stdlib/CharacterSet.maxon`, a file
loaded into every compile whether the program names it or not. Reading one has to find a built record: the
slot the trim dereferences would otherwise hold the zero the image left there.

It is the observable half of the library rule above — the half where liveness KEEPS the slot. The other half
is visible only as emitted code, which no case here compares.

```maxon
function main() returns ExitCode
	let trimmed = "  hi  ".trim(CharacterSet.whitespaces)
	print("[{trimmed}]")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hi]
```

<!-- test: a-managed-static-is-released-after-main -->
### An accessed managed static is released exactly once

The leak gate on a slot the program reads: the record is built before `main` and dropped by
`__maxon_global_cleanup` after it. Exit 101 would mean the cleanup missed it; a crash would mean it dropped
it twice.

```maxon
type Cached
	static let text = Cached.build()

	static function build() returns String
		return "held"
	end 'build'

	static function get() returns String
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

<!-- test: a-static-is-read-from-a-green-thread-across-a-stack-grow -->
### A static is read from a green thread across a stack grow

Initializers run before `main`, where no green thread exists, so `Cache.value` already holds its record when
the first thread starts — which is what keeps the green-thread stack guard out of the question entirely.

The recursion drives several grow-and-relocate rounds before the read, so the load is issued from a frame
the runtime has moved; the record's address lives on the heap and does not move with it. The second thread
reads the same record, and `built` appears once, ahead of everything either thread prints. The `read`
marker is what makes the function YIELD: with the initializer not reached through the load, a body
that only recurses and returns never reaches a suspension point, and `async` refuses it (E3073).

```maxon
typealias Count = int(0 to u64.max)

type Cache
	static let value = Cache.build()
	export var n as Count

	static function build() returns Cache
		print("built ")
		return Cache{n: 21}
	end 'build'

	static function get() returns Count
		return Cache.value.n
	end 'get'
end 'Cache'

function deepThenRead(n Integer) returns Integer
	if n == 0 'base'
		let v = Cache.get()
		print("read ")
		return v
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
built read read 21 21
```

<!-- test: an-unaccessed-managed-static-is-initialized-and-dropped -->
### A managed static nothing reads is built AND dropped

`Untouched.never` is a `String` its initializer really allocates, and nothing in the program ever reads the
slot — so the slot is never the image's zero, and the cleanup must drop it exactly as it drops the slot that
is read. Missing it is exit 101, not a wrong string. `other` is the accessed twin, so the two slots differ
in nothing but whether a read reaches them.

```maxon
type Untouched
	static let never = Untouched.build()
	static let other = Untouched.build()

	static function build() returns String
		return "x"
	end 'build'

	static function get() returns String
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
