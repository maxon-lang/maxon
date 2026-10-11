---
feature: rebindable-slot-borrow
status: stable
keywords: [ownership, borrow, field, global, rebind, retain, use-after-free, slot]
category: memory-management
---

# A Name Bound To A Value Read Out Of A REBINDABLE SLOT Takes Its Own Reference

## Documentation

A name bound to a managed value read out of storage holds its own reference to what it read. That
matters most for storage the program can overwrite — a `var` field and a top-level `var`:

```text
let old = items      // old holds its own reference to the array the field holds
items = fresh        // the store releases only the field's reference
sum(old)             // old still names the array it read
```

The storage outlives the name, but the value in it need not: a store into the slot releases the old
occupant. Because the binding is counted, the store releases only the slot's reference, and the value
the name read lives until the name goes out of scope. The count balances on every path — the store
releases the slot's reference and the binding's scope exit releases its own.

A `let` field cannot be stored to (E2013), so its occupant is never replaced; a binding of it is counted
all the same, and the program answers what it read.

## Tests

<!-- test: managed-field-read-survives-the-field-being-replaced -->
`let old = items` takes its own reference to the array the field holds; `items = fresh` releases the
field's reference; the walk below reads `old`. The sum proves the elements are still there, and the exit
code proves the count balanced (a leak is exit 101).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Holder
	var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create()}
	end 'create'

	function fill(n Integer)
		for i in 0 upto n 'each'
			items.push(i)
		end 'each'
	end 'fill'

	function swapAndRead() returns Integer
		let old = items
		var fresh = IntArray.create()
		fresh.push(99)
		items = fresh

		var sum = 0
		for i in 0 upto old.count() 'walk'
			sum = sum + (try old.get(i) otherwise 0)
		end 'walk'
		return sum
	end 'swapAndRead'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.fill(5)
	return h.swapAndRead() as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: the-var-spelling-reads-the-same -->
One keyword apart from the case above: a `var` bound to the field's array holds its own reference too,
and the walk reads the array it was bound to.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Holder
	var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create()}
	end 'create'

	function swapAndRead() returns Integer
		items.push(3)
		items.push(4)
		var old = items
		items = IntArray.create()

		var sum = 0
		for i in 0 upto old.count() 'walk'
			sum = sum + (try old.get(i) otherwise 0)
		end 'walk'
		return sum
	end 'swapAndRead'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	return h.swapAndRead() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: string-field-read-survives-the-field-being-replaced -->
⭐ **A `String` FIELD, WHERE AN UNCOUNTED READ WOULD BE A QUIET WRONG ANSWER.** A store that freed the
record `old` names would leave the read with whatever the allocator put there next, so this case builds
its strings at run time, allocates a filler after the store, and prints the text `old` read.

```maxon
type Holder
	var name as String

	static function create(n String) returns Self
		return Self{name: n}
	end 'create'

	function swapAndRead(fresh String) returns String
		let old = name
		name = fresh
		let filler = "filler {9} padded out long enough for the heap"
		return "[{old}] [{name}] [{filler}]"
	end 'swapAndRead'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create("hello world {40 + 2} padded out long enough for the heap")
	print(h.swapAndRead("replacement {7} padded out long enough for the heap"))
	return 0
end 'main'
```
```stdout
[hello world 42 padded out long enough for the heap] [replacement 7 padded out long enough for the heap] [filler 9 padded out long enough for the heap]
```

<!-- test: mutable-global-read-survives-the-global-being-replaced -->
**THE SECOND SLOT KIND.** A top-level `var` is rebindable storage exactly as a field is: the store
releases the old occupant, and `old` holds its own reference.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var G = IntArray.create()

function main() returns ExitCode
	G.push(1)
	G.push(2)
	let old = G
	var fresh = IntArray.create()
	fresh.push(99)
	G = fresh

	var sum = 0
	for i in 0 upto old.count() 'walk'
		sum = sum + (try old.get(i) otherwise 0)
	end 'walk'
	print("sum {sum}")
	return 0
end 'main'
```
```stdout
sum 3
```

<!-- test: an-immutable-field-read-is-counted-too -->
A `let` field cannot be stored to, so its occupant is never replaced; the binding is counted like any
other read, and the program answers what it read.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Frozen
	let items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(4)
		a.push(5)
		return Self{items: a}
	end 'create'

	function total() returns Integer
		let held = items

		var sum = 0
		for i in 0 upto held.count() 'walk'
			sum = sum + (try held.get(i) otherwise 0)
		end 'walk'
		return sum
	end 'total'
end 'Frozen'

function main() returns ExitCode
	var f = Frozen.create()
	return f.total() as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: the-promoted-name-keeps-its-declared-surface -->
<!-- tier: stdlib -->
⭐ **A COUNTED BINDING KEEPS ITS DECLARED SURFACE.** `__ManagedMemory` and the `Array` around it are
one record told apart by provenance, keyed by the value — so a counted binding, which is a fresh value,
must carry it, or `m.length()` becomes `E2015: Array member 'length'`. `let (m, _) = pair()` binds `m`
to a field read off the hidden tuple temp, which is a read out of a rebindable slot.
```maxon
typealias Int = int(i64.min to i64.max)

function pair() returns (__ManagedMemory, Int)
	return ("hello".toByteArray(), 7)
end 'pair'

function main() returns ExitCode
	let (m, _) = pair()
	return m.length() as ExitCode
end 'main'
```
```exitcode
5
```
