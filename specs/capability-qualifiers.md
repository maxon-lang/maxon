---
feature: capability-qualifiers
keywords: [mutable, immutable, isolated, function type, interface, capability, E3210, E3207]
category: semantics
---

# Capability words at boundaries with no body

## Documentation

A call through a function value or an interface method has no body the checker can read, so the boundary declares
what the callee does with its values. The word is written before the type:

- `mutable` on a parameter: the callee writes through it. On the receiver it is `mutable self`.
- `immutable` on a parameter or result: the callee reads frozen data, or hands frozen data back.
- `isolated` on a result: the result is a region of its own that nothing else reaches.

Unmarked parameters of a function type or an interface requirement are read-only, and a function value or a
conformer that does more than its boundary declares (writes a parameter or its receiver, or returns a result the
boundary does not promise) is refused at the conversion (`E3210`). A caller trusts the declaration: an argument to
a `mutable` parameter must be writable, and one to an `immutable` parameter must be frozen (`E3207`).

## Tests

<!-- test: capability-qualifiers.a-function-value-that-writes-its-parameter-converts-to-a-mutable-alias -->
`Step` declares its parameter `mutable`, so `advance` converts to it, and the counter handed through `apply` is written both times.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(mutable Counter) returns Tally

type Counter
	var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump() returns Tally
		self.n = self.n + 1
		return self.n
	end 'bump'
end 'Counter'

function advance(counter mutable Counter) returns Tally
	return counter.bump()
end 'advance'

function apply(step Step, counter Counter) returns Tally
	return step(counter)
end 'apply'

function main() returns ExitCode
	var counter = Counter.create()
	let first = apply(advance, counter: counter)
	let second = apply(advance, counter: counter)
	print("{first} {second}\n")
	return 0
end 'main'
```
```stdout
1 2
```
```exitcode
0
```

<!-- test: capability-qualifiers.a-conformer-that-writes-its-receiver-is-accepted-when-the-requirement-declares-mutable-self -->
`Grows.grow` declares `mutable self`, so `Bag.grow` conforms, and `stretch` writes the bag it was handed.

```maxon
typealias Tally = int(0 to u64.max)

interface Grows
	function grow(mutable self, by Tally)
end 'Grows'

type Bag implements Grows
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function grow(by Tally)
		self.total = self.total + by
	end 'grow'

	function size() returns Tally
		return self.total
	end 'size'
end 'Bag'

function stretch(g Grows)
	g.grow(5)
end 'stretch'

function main() returns ExitCode
	var bag = Bag.create()
	stretch(bag)
	print("{bag.size()}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: capability-qualifiers.error.a-function-value-whose-result-is-not-declared-isolated-is-refused -->
`Make` promises an isolated result, and `build` does not declare one, so a caller could not rely on it.

```maxon
typealias Tally = int(0 to u64.max)
typealias Make = function() returns isolated Box

type Box
	var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function count() returns Tally
		return self.n
	end 'count'
end 'Box'

function build() returns Box
	return Box.create()
end 'build'

function run(make Make) returns Tally
	let box = make()
	return box.count()
end 'run'

function main() returns ExitCode
	let n = run(build)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3210: <fragment>:27:14: function value for 'Make' does not declare `returns isolated`, but 'Make' does
```

<!-- test: capability-qualifiers.a-function-value-declaring-an-isolated-result-converts-to-an-isolated-alias -->
`fresh` declares `returns isolated`, so it converts to `Make`, and the region it returns is read by a one-call open.

```maxon
typealias Tally = int(0 to u64.max)
typealias Make = function() returns isolated Box

type Box
	var n as Tally

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	function count() returns Tally
		return self.n
	end 'count'
end 'Box'

function fresh() returns isolated Box
	return Box.create()
end 'fresh'

function run(make Make) returns Tally
	let box = make()
	return box.count()
end 'run'

function main() returns ExitCode
	let n = run(fresh)
	print("{n}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: capability-qualifiers.error.a-function-value-whose-result-is-not-declared-immutable-is-refused -->
`Frozen` promises an immutable result, and `current` hands back a mutable field.

```maxon
typealias Tally = int(0 to u64.max)
typealias Frozen = function() returns immutable Box

type Box
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

function current() returns Box
	return Box.create()
end 'current'

function run(look Frozen) returns Tally
	let box = look()
	return box.n
end 'run'

function main() returns ExitCode
	let n = run(current)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3210: <fragment>:23:14: function value for 'Frozen' does not declare `returns immutable`, but 'Frozen' does
```

<!-- test: capability-qualifiers.error.a-capability-alias-of-a-value-type-is-refused -->
A capability alias qualifies a record, and an `int` belongs to no region, so `immutable` on it is refused as it is on a result.

```maxon
typealias A = immutable int

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:2:15: Expected 'a type' but got 'immutable'
```

<!-- test: capability-qualifiers.a-tuple-element-qualifier-in-a-parameter-a-field-and-a-result-runs -->
`(immutable Box, Box)` qualifies its first element wherever a tuple type is written: a parameter, a field and a result all read the frozen element and write the live one.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	var pair as (immutable Box, Box)

	static function create(first immutable Box, second Box) returns Self
		return Self{pair: (first, second)}
	end 'create'

	function weigh() returns Tally
		self.pair.1.n = self.pair.1.n + 1
		return self.pair.0.n + self.pair.1.n
	end 'weigh'
end 'Holder'

function pairOf(first immutable Box, second Box) returns (immutable Box, Box)
	return (first, second)
end 'pairOf'

function total(pair (immutable Box, Box)) returns Tally
	return pair.0.n + pair.1.n
end 'total'

function main() returns ExitCode
	let frozen = freeze Box.create(1)
	let live = Box.create(2)
	let holder = Holder.create(frozen, second: live)
	print("{total(pairOf(frozen, second: live))} {holder.weigh()} {live.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 4 3
```

<!-- test: capability-qualifiers.a-generic-method-with-a-receiver-capability-takes-its-arguments -->
A receiver capability is not a parameter: a generic method that writes `immutable self` binds its first argument to the first parameter after it.

```maxon
typealias Level = int(0 to 100)

type Box
	let n as Level

	static function create(n Level) returns Self
		return Self{n: n}
	end 'create'

	function pick(immutable self, item T, extra Level) uses T returns T
		if extra > self.n 'taller'
			return item
		end 'taller'

		return item
	end 'pick'
end 'Box'

function main() returns ExitCode
	let box = Box.create(1)
	let word = "five"
	print("{box.pick(7, extra: 9)} {box.pick(word, extra: 5)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 five
```
