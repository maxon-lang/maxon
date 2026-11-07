---
feature: regions-syntax
status: experimental
keywords: [isolated, immutable, mutable, self, capability, region, contextual words]
category: semantics
---

# Capability words and the `self` parameter

## Documentation

A capability is a prefix word on a type, read like an adjective: `isolated`, `immutable` or `mutable`. The
words are contextual: they are recognised only where the grammar expects a type, so a local named `mutable`
or a method named `merge` keeps working. A method may declare its receiver as its first parameter with a
capability, `function reported(immutable self)`, for the cases inference cannot express; `self` is never
written at the call.

A capability word written directly on a value type (a number, `bool`, a payload-free union or enum,
`String`), a service handle or a promise is refused, because such a type has no region of its own to
qualify. A union with a payload has one and takes `isolated` and `immutable`. `mutable` on a parameter
or function-type parameter of a value type is the one exception: it makes the parameter by reference.

## Tests

<!-- test: capability-word-on-a-value-type-refused -->
`immutable` qualifies a region, and a `Tally` is a number that belongs to none, so the word is refused.

```maxon
typealias Tally = int(0 to u64.max)

function echo(n immutable Tally) returns Tally
	return n
end 'echo'

function main() returns ExitCode
	print("{echo(5)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:4:17: Expected 'a type' but got 'immutable'
```

<!-- test: capability-word-on-a-payload-free-union-refused -->
A union none of whose cases carries a payload is a plain tag value that belongs to no region, so `isolated` is refused as on an enum.

```maxon
union Mode
	idle
	busy
end 'Mode'

function echo(m isolated Mode) returns Mode
	return m
end 'echo'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:7:17: Expected 'a type' but got 'isolated'
```

<!-- test: capability-word-on-a-payload-union-parameter-and-result-runs -->
A union with a payload holds a record, so it is a region and takes `isolated` on a parameter and on a result.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	filled(box Box)
	vacant
end 'Slot'

function fill(n Count) returns isolated Slot
	return Slot.filled(Box.create(n))
end 'fill'

function read(slot isolated Slot) returns Count
	return match slot 'which'
		filled(box) gives box.n
		vacant gives 0
	end 'which'
end 'read'

function main() returns ExitCode
	let slot = fill(7)
	return read(slot) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: self-parameter-not-first-refused -->
A receiver declared as a parameter is the first parameter; written after another it is a parse error.

```maxon
typealias Tally = int(0 to u64.max)

type Scale
	var reading as Tally

	static function create(reading Tally) returns Self
		return Self{reading: reading}
	end 'create'

	function plus(by Tally, immutable self) returns Tally
		return self.reading + by
	end 'plus'
end 'Scale'

function main() returns ExitCode
	let scale = freeze Scale.create(4)
	print("{scale.plus(3)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:11:36: Expected 'a type' but got 'self'
```

<!-- test: immutable-self-method-runs-on-frozen -->
A method declared `immutable self` runs on a frozen value, and reads the receiver's field.

```maxon
typealias Tally = int(0 to u64.max)

type Scale
	var reading as Tally

	static function create(reading Tally) returns Self
		return Self{reading: reading}
	end 'create'

	function reported(immutable self) returns Tally
		return self.reading
	end 'reported'
end 'Scale'

function main() returns ExitCode
	let frozen = freeze Scale.create(42)
	print("{frozen.reported()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: region-operator-word-before-a-spaced-parenthesis-is-the-operator -->
`freeze (…)` with a space is the operator over a parenthesized operand, while `swap(…)` with the parenthesis adjacent stays a call to the user's own `swap`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function swap(left Tally, right Tally) returns Tally
	return left * 10 + right
end 'swap'

function main() returns ExitCode
	let pinned = freeze (Box.create(7))
	let swapped = swap(1, right: 2)
	print("{pinned.n} {swapped}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 12
```

<!-- test: a-mutates-captures-clause-converts-a-capture-writing-closure -->
`Tallier` ends in `mutates captures`, so a closure that writes the record it captured converts to it, and the running total survives between the two calls. A local named `mutates` or `captures` keeps its meaning.

```maxon
typealias Tally = int(0 to u64.max)
typealias Tallier = function(Tally) returns Tally mutates captures

type Total
	var sum as Tally

	static function create() returns Self
		return Self{sum: 0}
	end 'create'

	function bump(n Tally) returns Tally
		self.sum = self.sum + n
		return self.sum
	end 'bump'
end 'Total'

function feed(tallier Tallier) returns Tally
	let first = tallier(3)
	let second = tallier(4)
	return first + second
end 'feed'

function main() returns ExitCode
	var total = Total.create()
	let mutates = feed(function(n) gives total.bump(n))
	let captures = mutates + 1
	print("{mutates} {captures}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10 11
```

<!-- test: capability-word-on-a-value-type-field-refused -->
A field of a value type names no region, so a capability word before its type is not a type.

```maxon
typealias Tally = int(0 to 9)

type Gauge
	var reading as immutable Tally

	static function create() returns Self
		return Self{reading: 1}
	end 'create'
end 'Gauge'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:5:17: Expected 'a type' but got 'immutable'
```

<!-- test: capability-word-on-a-function-type-parameter-refused -->
A function type's parameter of a value type takes `mutable` and no other capability word.

```maxon
typealias Tally = int(0 to 9)
typealias Visit = function(n immutable Tally)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:30: Expected 'a type' but got 'immutable'
```

<!-- test: capability-word-on-a-value-type-result-refused -->
A result of a value type has no region either.

```maxon
typealias Tally = int(0 to 9)

function one() returns immutable Tally
	return 1
end 'one'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:4:24: Expected 'a type' but got 'immutable'
```

<!-- test: capability-word-on-a-map-key-argument-refused -->
A `Map`'s key is stored by value, so no capability word qualifies it.

```maxon
typealias Tally = int(0 to 9)

type Box
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

typealias Table = Map with immutable Tally, Box

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:12:28: Expected 'a type' but got 'immutable'
```

<!-- test: isolated-type-argument-without-the-bound-refused -->
A generic type that does not bound its parameter by `isolated` takes no `isolated` argument.

```maxon
typealias Tally = int(0 to 9)

type Box
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Wrapper uses Element
	var item as Element
end 'Wrapper'

typealias Wrapped = Wrapper with isolated Box

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:16:34: Expected 'a type' but got 'isolated'
```

<!-- test: receiver-capability-on-a-static-function-refused -->
A static function has no receiver to qualify.

```maxon
typealias Tally = int(0 to 9)

type Scale
	var reading as Tally

	static function create(immutable self) returns Self
		return Self{reading: 1}
	end 'create'
end 'Scale'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:7:25: Expected 'a type' but got 'immutable'
```

<!-- test: receiver-capability-on-a-free-function-refused -->
A free function has no receiver to qualify.

```maxon
typealias Tally = int(0 to 9)

function echo(mutable self) returns Tally
	return 1
end 'echo'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:4:15: Expected 'a type' but got 'mutable'
```

<!-- test: a-capability-word-after-as-other-than-isolated-refused -->
`as` takes `isolated` before a record type and no other capability word.

```maxon
typealias Tally = int(0 to 9)

type Box
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create()
	let b = a as mutable Box
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:14:15: Expected 'a type' but got 'mutable'
```

<!-- test: as-isolated-on-a-value-type-refused -->
`as isolated` qualifies a record, and a `Tally` is a number.

```maxon
typealias Tally = int(0 to 9)

function main() returns ExitCode
	let n = 5 as isolated Tally
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:5:15: Expected 'a type' but got 'isolated'
```

<!-- test: as-isolated-outside-an-initializer-refused -->
`as isolated` ends a `let` or `var` initializer; as a call argument it qualifies nothing.

```maxon
typealias Tally = int(0 to 9)

type Box
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

function eat(b Box)
	print("{b.n}\n")
end 'eat'

function main() returns ExitCode
	let a = Box.create()
	eat(a as isolated Box)
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:18:2: Expected newline but got 'eat'
```

<!-- test: mutable-tuple-item-refused -->
A tuple item is `immutable` or unmarked.

```maxon
typealias Tally = int(0 to 9)

type Box
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

typealias Pair = (mutable Box, Tally)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:12:19: Expected 'a type' but got 'mutable'
```

<!-- test: isolated-alias-tuple-item-refused -->
An alias of an `isolated` record cannot be a tuple item either.

```maxon
typealias Tally = int(0 to 9)

type Box
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

typealias Own = isolated Box
typealias Pair = (Own, Tally)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:13:19: Expected 'a type' but got 'Own'
```
