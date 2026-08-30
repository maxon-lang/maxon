---
feature: array-slots
status: experimental
keywords: [array, slot, empty, managed, error]
category: memory-safety
---
# Array Slots - Empty Slot Detection

## Documentation

An `Array` is DENSE: every index in `[0, count())` holds an element. `resize` grows the live
range by exposing zero-initialized slots, and a zero is a real element only when the element is
stored INLINE — an `int`, a `float`, a `bool`, a `byte`, a ranged typealias over one of those, a
payload-free enum. A MANAGED element (a struct, a `String`, a nested container, a boxed union) is
stored as a POINTER, so a zeroed slot is a NULL: an absence, not a value. Maxon has no default
constructor, so there is nothing correct for `resize` to put there.

`resize` on an array whose element type is managed is therefore **refused at compile time**
(**E3106**), naming the element type. Append with `push(value)`, or grow to a length in one call
with `growFilled(newLength, value:)` — both supply the element the type cannot invent.

The refusal covers the whole call, not only the growing half: which half a `resize(n)` is cannot
be known until it runs. Shrinking needs no element and stays available for every element type,
under a name that says the direction — `truncate(newLength)`.

Empty slots still exist one layer down. `__ManagedMemory` is the raw buffer, and its `setLength`
publishes a length over slots nothing has written; that is how a slot table whose occupancy is
tracked separately (`Map`, `Set`) is built. Reading such a slot back through `Array.get` reports
**`ArrayError.emptySlot`** — a DIFFERENT error from `ArrayError.indexOutOfBounds`, because the
index is in range and it is the slot that is empty. Conflating the two turns "you never filled
this" into "you asked past the end", which sends the reader looking at the wrong thing.

## Tests

<!-- test: error.resize-struct-element -->
### resize on a struct-element array is refused
A grown slot would hold NULL rather than a `Slot`, so `count()` would not agree with `get()`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.push(Slot.create(10))
	arr.resize(3)
	return arr.count()
end 'main'
```
```maxoncstderr
error E3106: specs/fragments/array-slots/error.resize-struct-element.test:17:6: 'resize' cannot grow an array of 'Slot': a grown slot holds NO element — Maxon has no default constructor — so 'count()' would not agree with 'get()'. Append with 'push(value)', grow with 'growFilled(newLength, value:)', shrink with 'truncate(newLength)'
```

<!-- test: error.resize-string-element -->
### resize on a String-element array is refused
A `String` element is a pointer too — the refusal is about the element being MANAGED, not about
it being a struct.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	arr.resize(3)
	return arr.count()
end 'main'
```
```maxoncstderr
error E3106: specs/fragments/array-slots/error.resize-string-element.test:6:6: 'resize' cannot grow an array of 'String': a grown slot holds NO element — Maxon has no default constructor — so 'count()' would not agree with 'get()'. Append with 'push(value)', grow with 'growFilled(newLength, value:)', shrink with 'truncate(newLength)'
```

<!-- test: grow-filled-managed-element -->
### growFilled is the grow that supplies the element
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.growFilled(3, value: Slot.create(14))
	var total = 0
	for s in arr 'sum'
		total = total + s.value
	end 'sum'
	return total
end 'main'
```
```exitcode
42
```

<!-- test: get-valid-slot-not-empty -->
### Get on a populated slot works fine
Getting an element from a slot that was populated via push should work without error.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.push(Slot.create(42))
	let result = try arr.get(0) otherwise Slot.create(0)
	return result.value
end 'main'
```
```exitcode
42
```

<!-- test: int-array-zero-not-empty -->
### Int array containing zero does NOT throw emptySlot
Primitive arrays store values inline. A zero value is a valid element, not an empty slot, so
`resize` stays available for them.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(0)
	arr.push(5)
	arr.resize(4)
	let val = try arr.get(0) otherwise -1
	let val2 = try arr.get(2) otherwise -1
	return val + val2
end 'main'
```
```exitcode
0
```

<!-- test: raw-open-slot-reports-empty-slot -->
### An empty slot opened through the raw buffer reports emptySlot
`__ManagedMemory.setLength` is the layer where an unwritten slot is a defined state. Read back
through `Array.get` it is `emptySlot` — NOT `indexOutOfBounds`, which would blame an index that
is perfectly in range.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.reserve(2)
	try arr.managed.setLength(2) otherwise panic("setLength: capacity just reserved for 2")
	try arr.get(0) otherwise (e) 'handler'
		match e 'check'
			emptySlot then return 42
			indexOutOfBounds then return 99
		end 'check'
	end 'handler'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: past-the-end-reports-index-out-of-bounds -->
### An index past the end still reports indexOutOfBounds
The other half of the same distinction: this one really IS out of bounds.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.reserve(2)
	try arr.managed.setLength(2) otherwise panic("setLength: capacity just reserved for 2")
	try arr.get(5) otherwise (e) 'handler'
		match e 'check'
			emptySlot then return 42
			indexOutOfBounds then return 99
		end 'check'
	end 'handler'
	return 0
end 'main'
```
```exitcode
99
```

<!-- test: error.negative-index-is-refused-at-the-door -->
### A NEGATIVE index never reaches the distinction at all
The third answer the same distinction once owed, and the one nothing asked for. `get`'s failure is decided
from the live range, and "below the length" is true of `-1` on every signed comparison, so a negative index
was once reported as a slot that was never filled — sending the reader to look for a slot that was never
addressable. Naming it `indexOutOfBounds` was the repair then. It is not the answer now, because the
question no longer gets asked: `ElementIndex` stops at `i64.max`, so a negative is refused at `get`'s door
and no `ArrayError` of either name is ever minted. A literal is refused at compile time.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.reserve(2)
	try arr.managed.setLength(2) otherwise panic("setLength: capacity just reserved for 2")
	try arr.get(-1) otherwise (e) 'handler'
		match e 'check'
			emptySlot then return 42
			indexOutOfBounds then return 99
		end 'check'
	end 'handler'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/array-slots/error.negative-index-is-refused-at-the-door.test:14:10: Value -1 is outside the range of 'ElementIndex' (int(0 to 9223372036854775807))
```

<!-- test: laundered-negative-index-panics-at-the-door -->
### The same refusal when the compiler cannot fold the index
A negative the compiler cannot see is refused by the callee-entry guard rather than at the call site. The
`match` arms are what prove the refusal is not either `ArrayError`: both are still written, both would
return, and neither runs.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer
end 'Slot'

typealias SlotArray = Array with Slot

function launder(n Integer) returns Integer
	return n
end 'launder'

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.reserve(2)
	try arr.managed.setLength(2) otherwise panic("setLength: capacity just reserved for 2")
	try arr.get(launder(-1)) otherwise (e) 'handler'
		match e 'check'
			emptySlot then return 42
			indexOutOfBounds then return 99
		end 'check'
	end 'handler'
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at Array.maxon:249: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in SlotArray.get
  in main
  in mrt_start
```

<!-- test: first-empty-slot -->
### first() on an array whose slot 0 is empty
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.reserve(3)
	try arr.managed.setLength(3) otherwise panic("setLength: capacity just reserved for 3")
	let result = try arr.first() otherwise Slot.create(77)
	return result.value
end 'main'
```
```exitcode
77
```

<!-- test: last-empty-slot -->
### last() on an array whose last slot is empty
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.push(Slot.create(1))
	arr.reserve(3)
	try arr.managed.setLength(3) otherwise panic("setLength: capacity just reserved for 3")
	let result = try arr.last() otherwise Slot.create(55)
	return result.value
end 'main'
```
```exitcode
55
```
