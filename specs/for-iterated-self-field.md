---
feature: for-iterated-self-field
status: experimental
keywords: [for, iteration, self, field, rebind, clear, use-after-free]
category: control-flow
---

## Documentation

# Iterating a field of the enclosing `self`

A `for … in <array>` holds its own counted reference to the record it starts on, takes a counted
reference to each element it hands the body, and re-reads that record's live count on every step.
A field of the enclosing type can be named **two** ways as a source (`items`, `self.items`) and
**four** ways as a write (`items = …`, `self.items = …`, `items.clear()`, `self.items.clear()`), and
all eight programs compile and behave alike:

- an **assignment** rebinds the field, and the loop keeps walking the record it started on, to that
  record's end;
- a **mutating method** changes the record the loop walks: after `clear()` the next step sees a
  count of 0 and the loop ends.

Either way the element the body already holds stays valid.

```text
for it in items 'scan'
    items = Labels.create()   // the loop still walks the old record
    print("{it}\n")
end 'scan'
```

## Tests

<!-- test: iterated-self-field-bare-source-bare-assign-walks-the-old-record -->
The bare source, the bare write. Both elements of the old record are visited, and the field ends
up holding the fresh empty array: `21 + 22 + 0`.
```maxon
typealias Tally = int(0 to 1000)
typealias Labels = Array with String

type Bag
	export var items as Labels

	static function create() returns Self
		var a = Labels.create()
		a.push("first heap element {40 + 2}")
		a.push("second heap element {40 + 5}")
		return Self{items: a}
	end 'create'

	function sum() returns Tally
		var total = 0 as Tally
		for it in items 'scan'
			items = Labels.create()
			print("{it}\n")
			total = total + (it.byteLength() as Tally)
		end 'scan'
		return total + (items.count() as Tally)
	end 'sum'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```exitcode
43
```
```stdout
first heap element 42
second heap element 45
```

<!-- test: iterated-self-field-bare-source-explicit-assign-walks-the-old-record -->
The bare source, the `self.`-spelled write — the same storage, so the same answer.
```maxon
typealias Tally = int(0 to 1000)
typealias Labels = Array with String

type Bag
	export var items as Labels

	static function create() returns Self
		var a = Labels.create()
		a.push("first heap element {40 + 2}")
		a.push("second heap element {40 + 5}")
		return Self{items: a}
	end 'create'

	function sum() returns Tally
		var total = 0 as Tally
		for it in items 'scan'
			self.items = Labels.create()
			print("{it}\n")
			total = total + (it.byteLength() as Tally)
		end 'scan'
		return total + (items.count() as Tally)
	end 'sum'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```exitcode
43
```
```stdout
first heap element 42
second heap element 45
```

<!-- test: iterated-self-field-explicit-source-bare-assign-walks-the-old-record -->
The `self.`-spelled source, the bare write.
```maxon
typealias Tally = int(0 to 1000)
typealias Labels = Array with String

type Bag
	export var items as Labels

	static function create() returns Self
		var a = Labels.create()
		a.push("first heap element {40 + 2}")
		a.push("second heap element {40 + 5}")
		return Self{items: a}
	end 'create'

	function sum() returns Tally
		var total = 0 as Tally
		for it in self.items 'scan'
			items = Labels.create()
			print("{it}\n")
			total = total + (it.byteLength() as Tally)
		end 'scan'
		return total + (items.count() as Tally)
	end 'sum'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```exitcode
43
```
```stdout
first heap element 42
second heap element 45
```

<!-- test: iterated-self-field-explicit-source-explicit-assign-walks-the-old-record -->
Both halves spelled with `self.`.
```maxon
typealias Tally = int(0 to 1000)
typealias Labels = Array with String

type Bag
	export var items as Labels

	static function create() returns Self
		var a = Labels.create()
		a.push("first heap element {40 + 2}")
		a.push("second heap element {40 + 5}")
		return Self{items: a}
	end 'create'

	function sum() returns Tally
		var total = 0 as Tally
		for it in self.items 'scan'
			self.items = Labels.create()
			print("{it}\n")
			total = total + (it.byteLength() as Tally)
		end 'scan'
		return total + (items.count() as Tally)
	end 'sum'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```exitcode
43
```
```stdout
first heap element 42
second heap element 45
```

<!-- test: iterated-self-field-bare-source-bare-call-ends-the-iteration -->
A mutating METHOD on the iterated field changes the record the loop walks: the element already
read stays valid, and the next step sees the cleared count and ends.
```maxon
typealias Tally = int(0 to 1000)
typealias Labels = Array with String

type Bag
	export var items as Labels

	static function create() returns Self
		var a = Labels.create()
		a.push("first heap element {40 + 2}")
		a.push("second heap element {40 + 5}")
		return Self{items: a}
	end 'create'

	function sum() returns Tally
		var total = 0 as Tally
		for it in items 'scan'
			items.clear()
			print("{it}\n")
			total = total + (it.byteLength() as Tally)
		end 'scan'
		return total + (items.count() as Tally)
	end 'sum'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```exitcode
21
```
```stdout
first heap element 42
```

<!-- test: iterated-self-field-bare-source-explicit-call-ends-the-iteration -->
The chain spelling of the same call. It reaches a different door and must reach the same answer.
```maxon
typealias Tally = int(0 to 1000)
typealias Labels = Array with String

type Bag
	export var items as Labels

	static function create() returns Self
		var a = Labels.create()
		a.push("first heap element {40 + 2}")
		a.push("second heap element {40 + 5}")
		return Self{items: a}
	end 'create'

	function sum() returns Tally
		var total = 0 as Tally
		for it in items 'scan'
			self.items.clear()
			print("{it}\n")
			total = total + (it.byteLength() as Tally)
		end 'scan'
		return total + (items.count() as Tally)
	end 'sum'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```exitcode
21
```
```stdout
first heap element 42
```

<!-- test: iterated-self-field-explicit-source-bare-call-ends-the-iteration -->
The `self.`-spelled source with the bare call.
```maxon
typealias Tally = int(0 to 1000)
typealias Labels = Array with String

type Bag
	export var items as Labels

	static function create() returns Self
		var a = Labels.create()
		a.push("first heap element {40 + 2}")
		a.push("second heap element {40 + 5}")
		return Self{items: a}
	end 'create'

	function sum() returns Tally
		var total = 0 as Tally
		for it in self.items 'scan'
			items.clear()
			print("{it}\n")
			total = total + (it.byteLength() as Tally)
		end 'scan'
		return total + (items.count() as Tally)
	end 'sum'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```exitcode
21
```
```stdout
first heap element 42
```

<!-- test: iterated-self-field-explicit-source-explicit-call-ends-the-iteration -->
Both halves of the call spelled with `self.`.
```maxon
typealias Tally = int(0 to 1000)
typealias Labels = Array with String

type Bag
	export var items as Labels

	static function create() returns Self
		var a = Labels.create()
		a.push("first heap element {40 + 2}")
		a.push("second heap element {40 + 5}")
		return Self{items: a}
	end 'create'

	function sum() returns Tally
		var total = 0 as Tally
		for it in self.items 'scan'
			self.items.clear()
			print("{it}\n")
			total = total + (it.byteLength() as Tally)
		end 'scan'
		return total + (items.count() as Tally)
	end 'sum'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```exitcode
21
```
```stdout
first heap element 42
```

<!-- test: unrelated-self-field-stays-writable -->
Every OTHER field of the same `self` is writable inside the body too, and its write takes effect.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray
	export var seen as Int

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a, seen: 0}
	end 'create'

	function sum() returns Int
		var total = 0 as Int
		for it in self.items 'scan'
			seen = seen + 1
			total = total + it
		end 'scan'
		return total + seen
	end 'sum'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: self-field-writable-after-the-loop -->
The loop's reference to its source is released at its `end`: the field is written on the very
next line, and a reference the loop kept would leak the record (exit 101).
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a}
	end 'create'

	function sumThenClear() returns Int
		var total = 0 as Int
		for it in items 'scan'
			total = total + it
		end 'scan'
		items.clear()
		return total + items.count() as Int
	end 'sumThenClear'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sumThenClear() as ExitCode
end 'main'
```
```exitcode
12
```
