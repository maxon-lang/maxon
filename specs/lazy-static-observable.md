---
feature: lazy-static-observable
status: experimental
keywords: [static, initializer, call-free, dependency-order, cycle, library-static]
category: language
---

# Static Initializers — the OBSERVABLE half

## Documentation

A `static let` initializer calls no function (E3220), so no initializer has a side effect: nothing prints, opens or
allocates on the program's behalf before `main`, and the value a slot holds is a function of the program text
alone. A slot nothing reads is still built, and it is released with the rest of the program's globals.

**⛔ A COMPUTED VALUE CANNOT SEE WHEN ITS INITIALIZER RAN.** A case that reads a static and prints what it
holds gets the same output under any timing whatsoever, so the cases here observe the three things a value
cannot show: the refusal of an initializer that would run code, a slot nothing ever reads, and the references
between initializers, whose order is implied by what they read.

### The scope of the declaration does not change the rule

A `static` member and a module-level `let` agree: a call in either initializer is E3220.

### A LIBRARY declaration is the one exception to "every slot is kept", and it is a COST rule

`stdlib/` is loaded into every compile whether the program names it or not, so a library's statics are not
the program's. Keeping every one of them would charge `function main() returns ExitCode / return 7` for eleven
`CharacterSet` presets and the `Set`/`Array` cone behind them. A library static is therefore kept on LIVENESS
alone: read by reachable code and it is built like any other; read by nothing and neither it nor its initializer
is emitted.

### Ordering is what the reads imply

Declaration order does not order initialization, because a program may declare a type after the type whose
initializer reads it. The two ordering cases below declare the same pair in opposite orders and must print
the same thing. A cycle implies no order at all, so it is refused at compile time rather than settled to
some value at run time.

## Tests

<!-- test: never-accessed-static-is-built-and-released -->
### A static that is never accessed leaves a clean exit

The control for the whole feature. Nothing reads `Counter.cached`, so the program's clean exit, leak gate
included, is the only evidence that the slot was built and released.

```maxon
public typealias Count = int(0 to u64.max)

public type Counter
	static let cached = Counter{id: 1}
	export var id as Count
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
main
```

<!-- test: never-accessed-module-level-let-is-built-and-released -->
### The same binding at MODULE scope leaves a clean exit too

The other half of the pair: the identical program with `cached` moved out of the `type` body as a module-level `let`.

```maxon
public let cached = "held"

function main() returns ExitCode
	print("main")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
main
```

<!-- test: error.a-call-in-a-static-initializer-is-refused -->
### A call in a static initializer is refused

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
	print("{Late.get()}")
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:5:21: the initializer of 'Late.value' calls 'Late.build': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-call-in-a-module-level-let-initializer-is-refused -->
### A call in a module-level `let` initializer is refused

```maxon
public typealias Count = int(0 to u64.max)

public type Counter
	export var id as Count

	static function createInstance() returns Counter
		print("init ")
		return Counter{id: 1}
	end 'createInstance'
end 'Counter'

public let cached = Counter.createInstance()

function main() returns ExitCode
	print("main")
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:13:21: the initializer of 'cached' calls 'Counter.createInstance': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: many-accesses-answer-with-one-value -->
### Many accesses, one value

Every access answers with the same record.

```maxon
typealias Count = int(0 to u64.max)

type Once
	static let value = Once{n: 5}
	export var n as Count

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
5 5 5
```

<!-- test: one-static-initializer-reads-another -->
### A static initializer that reads another static

`Inner` is read only from `Outer`'s initializer, which is what makes this an ordering case rather than two
independent statics.

⭐ **That is DEPENDENCY order, not declaration order.** The two agree in this program; the next case is what
separates them.

```maxon
typealias Count = int(0 to u64.max)

type Inner
	static let value = 3 as Count
end 'Inner'

type Outer
	static let value = Inner.value + 1

	static function get() returns Count
		return Outer.value
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
start 4
```

<!-- test: a-static-initializer-reads-one-declared-later -->
### A static initializer that reads one declared LATER

The same pair with the declarations swapped, and therefore the same output: `Outer` is declared first and
still settles second, because it reads `Inner`. Settling them in declaration order would read `Inner`'s slot
before anything had filled it, and answer `1` or dereference a null.

```maxon
typealias Count = int(0 to u64.max)

type Outer
	static let value = Inner.value + 1

	static function get() returns Count
		return Outer.value
	end 'get'
end 'Outer'

type Inner
	static let value = 3 as Count
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
start 4
```

<!-- test: a-cycle-between-static-initializers-is-refused -->
### Two static initializers that read each other are refused

Neither can settle first, so there is no order to give them and no value either slot could honestly hold. The
program is refused rather than resolved: a zero, a null dereference or an unbounded recursion would each be
a wrong answer handed over at run time to a question that is settled at compile time.

E2012 is the code, because the fact is the declaration cycle and not the construct it was found in — the
same code a cycle among global constants gets. The diagnostic anchors on the declaration at which the cycle
closes and names the whole cycle, so the message identifies the loop rather than one member of it.

```maxon
typealias Count = int(0 to u64.max)

type Ping
	static let value = Pong.value + 1
end 'Ping'

type Pong
	static let value = Ping.value + 1
end 'Pong'

function main() returns ExitCode
	print("{Ping.value}")
	return 0
end 'main'
```
```maxoncstderr
error E2012: <fragment>:9:13: Circular dependency detected among global constants: Ping.value, Pong.value
```

<!-- test: a-static-initializer-that-reads-itself-is-refused -->
### A static initializer that reads its own slot is refused

The one-member arc. There is no other global to name, so the diagnostic says what the single global does
instead of printing a list of one and leaving the author to infer the shape.

```maxon
typealias Count = int(0 to u64.max)

type Loop
	static let value = Loop.value + 1
end 'Loop'

function main() returns ExitCode
	print("{Loop.value}")
	return 0
end 'main'
```
```maxoncstderr
error E2012: <fragment>:5:13: Circular dependency detected among global constants: Loop.value
```

<!-- test: a-dependency-chain-settles-from-the-bottom -->
### A dependency chain twenty deep

The pair above separates declaration order from dependency order over two globals; this separates them over
twenty-one, declared in the exact reverse of the order they must settle in. `s20` is one more than `s19` and
so on down to `s00`, so the answer is the depth: any global settled before the one it reads contributes a zero
and the total falls short.

```maxon
typealias Count = int(0 to u64.max)

type Chain
	static let s20 = Chain.s19 + 1
	static let s19 = Chain.s18 + 1
	static let s18 = Chain.s17 + 1
	static let s17 = Chain.s16 + 1
	static let s16 = Chain.s15 + 1
	static let s15 = Chain.s14 + 1
	static let s14 = Chain.s13 + 1
	static let s13 = Chain.s12 + 1
	static let s12 = Chain.s11 + 1
	static let s11 = Chain.s10 + 1
	static let s10 = Chain.s09 + 1
	static let s09 = Chain.s08 + 1
	static let s08 = Chain.s07 + 1
	static let s07 = Chain.s06 + 1
	static let s06 = Chain.s05 + 1
	static let s05 = Chain.s04 + 1
	static let s04 = Chain.s03 + 1
	static let s03 = Chain.s02 + 1
	static let s02 = Chain.s01 + 1
	static let s01 = Chain.s00 + 1
	static let s00 = 0 as Count

	static function top() returns Count
		return Chain.s20
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
### A library static the program reads is built

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

The leak gate on a slot the program reads: the record is dropped by `__maxon_global_cleanup` after `main`. Exit
101 would mean the cleanup missed it; a crash would mean it dropped it twice.

```maxon
type Cached
	static let text = "held"

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

`Cache.value` already holds its record when the first thread starts, which is what keeps the green-thread
stack guard out of the question entirely.

The recursion drives several grow-and-relocate rounds before the read, so the load is issued from a frame
the runtime has moved; the record's address lives on the heap and does not move with it. The second thread
reads the same record. The `read` marker is what makes the function YIELD: a body that only recurses and
returns never reaches a suspension point, and `async` refuses it (E3073).

```maxon
typealias Count = int(0 to u64.max)

type Cache
	static let value = Cache{n: 21}
	export var n as Count

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
read read 21 21
```

<!-- test: an-unaccessed-managed-static-is-built-and-dropped -->
### A managed static nothing reads is built AND dropped

`Untouched.never` is a `String` the slot really holds, and nothing in the program ever reads it — so the slot
is never the image's zero, and the cleanup must drop it exactly as it drops the slot that is read. Missing it
is exit 101, not a wrong string. `other` is the accessed twin, so the two slots differ in nothing but whether
a read reaches them.

```maxon
type Untouched
	static let never = "x"
	static let other = "x"

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
