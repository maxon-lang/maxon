---
feature: iterator-element-provenance
status: experimental
keywords: [let, read-only, iterator, cursor, withIterator, createIterator, current, element, for]
category: type-system
---

# An iterator or cursor over a read-only source hands out read-only elements

## Documentation

A `let` is deeply read-only, and so is everything read out of it. An element an iterator or a cursor yields is read
out of the collection it was made from, so it carries the collection's mutability:

- a `for` over `withIterator()`, or over any cursor made from a `let`, a `let` parameter or a `let` field, yields
  read-only elements, exactly as `for element in collection` does;
- `current()` and `peek()` of a cursor made by `createIterator()` or `cursor()` on a read-only collection yield
  read-only elements: writing through one is E3159, passing one to a function that writes it is E3159, and binding
  one to a `var` is E3078;
- the cursor itself is not read-only: `advance()`, `retreat()` and `seek()` move it, and a `var` cursor over a
  `let` collection is legal;
- over a `var` collection every element stays writable, through a `for` and through a cursor alike.

A `let` that was made from a `var` (`let frozen = items`, which moves `items`) is read-only like any other `let`.

## Tests

<!-- test: error.a-for-with-iterator-over-a-let-array-refuses-a-write -->
`frozen` is the `let` that took `items` over. The element `c` is read out of `frozen`, so the write is refused.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Plain = Array with Counter

function main() returns ExitCode
	var items = Plain.create()
	items.push(Counter.create(4))
	let frozen = items

	for (_, c) in frozen.withIterator() 'each'
		c.n = 99
	end 'each'

	let first = try frozen.get(0) otherwise return 1
	return first.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:20:3: cannot write through 'c': 'frozen' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.a-for-with-iterator-over-a-let-built-by-a-call-refuses-a-write -->
The same with a `let` bound to a call result instead of one moved out of a `var`.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Plain = Array with Counter

function build() returns Plain
	var items = Plain.create()
	items.push(Counter.create(4))
	return items
end 'build'

function main() returns ExitCode
	let frozen = build()

	for (_, c) in frozen.withIterator() 'each'
		c.n = 99
	end 'each'

	let first = try frozen.get(0) otherwise return 1
	return first.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:24:3: cannot write through 'c': 'frozen' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.a-for-with-iterator-over-a-let-parameters-field-refuses-the-call -->
`bumpAll` writes the elements of the array its parameter holds. Handing it a `let` is refused at the call.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Plain = Array with Counter

type Bag
	export var items as Plain

	static function create(items Plain) returns Self
		return Self{items: items}
	end 'create'
end 'Bag'

function bumpAll(bag Bag)
	for (_, c) in bag.items.withIterator() 'each'
		c.n = 99
	end 'each'
end 'bumpAll'

function main() returns ExitCode
	var items = Plain.create()
	items.push(Counter.create(4))
	let bag = Bag.create(items)
	bumpAll(bag)
	let first = try bag.items.get(0) otherwise return 1
	return first.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:32:2: cannot pass 'bag', which is read-only (a `let` binding), to a function that writes parameter 'bag' (in main); pass a clone()
```

<!-- test: error.the-current-element-of-a-cursor-over-a-let-array-cannot-be-bound-to-a-var -->
A cursor made from the `let` `frozen` hands out `frozen`'s elements, so `var c` would write one.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Plain = Array with Counter

function main() returns ExitCode
	var items = Plain.create()
	items.push(Counter.create(4))
	let frozen = items

	var it = try frozen.createIterator() otherwise return 2
	var c = it.current()
	c.n = 99

	let first = try frozen.get(0) otherwise return 1
	return first.n as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:20:6: cannot assign the result of 'current', which may share storage with a field of an immutable variable, to mutable binding 'c'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.the-current-element-of-a-cursor-over-a-let-array-refuses-a-writing-callee -->
The element is passed straight to `bump`, which writes it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Plain = Array with Counter

function bump(c Counter)
	c.n = 99
end 'bump'

function main() returns ExitCode
	var items = Plain.create()
	items.push(Counter.create(4))
	let frozen = items

	var it = try frozen.createIterator() otherwise return 2
	bump(it.current())

	let first = try frozen.get(0) otherwise return 1
	return first.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:24:2: cannot pass 'frozen', which is read-only (a `let` binding), to a function that writes parameter 'c' (in main); pass a clone()
```

<!-- test: error.an-element-read-out-of-a-let-moved-from-a-var-cannot-be-bound-to-a-var -->
`get` on the `let` that took `items` over hands out an element of `frozen`. No cursor is involved.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Plain = Array with Counter

function main() returns ExitCode
	var items = Plain.create()
	items.push(Counter.create(4))
	let frozen = items

	var c = try frozen.get(0) otherwise return 2
	c.n = 99

	let first = try frozen.get(0) otherwise return 1
	return first.n as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:19:6: cannot assign the result of 'get', which may share storage with immutable variable 'frozen', to mutable binding 'c'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-var-array-hands-out-writable-elements-through-a-for-and-a-cursor -->
Over a `var` array the same two routes write: the `for` variable and the cursor's element both are the array's own
records.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Plain = Array with Counter

function main() returns ExitCode
	var items = Plain.create()
	items.push(Counter.create(4))
	items.push(Counter.create(5))

	for (_, c) in items.withIterator() 'each'
		c.n = c.n + 10
	end 'each'

	var it = try items.createIterator() otherwise return 2
	var c = it.current()
	c.n = c.n + 100
	try it.advance() otherwise return 3
	var d = it.current()
	d.n = d.n + 1000

	let first = try items.get(0) otherwise return 1
	let second = try items.get(1) otherwise return 1
	return (first.n + second.n - 1000 - 100) as ExitCode
end 'main'
```
```exitcode
29
```

<!-- test: a-let-array-is-read-and-a-var-cursor-over-it-moves -->
A `var` cursor over a `let` array advances, and every element it yields is read: bound to a `let`, summed, passed to
a function that only reads. Nothing writes an element, so the program is accepted.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Plain = Array with Counter

function read(c Counter) returns Integer
	return c.n
end 'read'

function main() returns ExitCode
	var items = Plain.create()
	items.push(Counter.create(4))
	items.push(Counter.create(5))
	let frozen = items

	var total = 0 as Integer

	for (_, c) in frozen.withIterator() 'each'
		total = total + read(c)
	end 'each'

	var it = try frozen.createIterator() otherwise return 2
	let head = it.current()
	total = total + head.n
	try it.advance() otherwise return 3
	total = total + read(it.current())

	return total as ExitCode
end 'main'
```
```exitcode
18
```
