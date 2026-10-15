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
this" into "you asked past the end", which sends the reader looking at the wrong thing. A `for` loop has no
error to report, so its element read of an empty slot PANICS with the empty-slot message rather than
handing the body a null.

`.managed` and `__ManagedMemory` are library-tier surface (E3185 outside `stdlib/` and `runtime/`), so
the cases that open an empty slot through `setLength` compile in the library tier
(`<!-- tier: stdlib -->`).

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
error E3106: <fragment>:17:6: 'resize' cannot grow an array of 'Slot': a grown slot holds NO element — Maxon has no default constructor — so 'count()' would not agree with 'get()'. Append with 'push(value)'
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
error E3106: <fragment>:6:6: 'resize' cannot grow an array of 'String': a grown slot holds NO element — Maxon has no default constructor — so 'count()' would not agree with 'get()'. Append with 'push(value)'
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
	var total = 0 as Integer
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
<!-- tier: stdlib -->
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
typealias IntegerArray = Array with Integer

function main() returns ExitCode
	var arr = IntegerArray.create()
	arr.push(1)
	arr.push(2)
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
`get`'s failure is decided from the live range, and "below the length" is true of `-1` on every signed
comparison, so a negative index that reached it would be reported as a slot that was never filled. It
never reaches it: `ElementIndex` stops at `i64.max`, so a negative is refused at `get`'s door and no
`ArrayError` of either name is ever minted. A literal is refused at compile time.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

function main() returns ExitCode
	var arr = IntegerArray.create()
	arr.push(1)
	arr.push(2)
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
error E3005: <fragment>:9:10: Value -1 is outside the range of 'ElementIndex' (int(0 to 18446744073709551615))
```

<!-- test: laundered-negative-index-panics-at-the-door -->
### The same refusal when the compiler cannot fold the index
A negative the compiler cannot see is refused by the runtime guard instead. The `match` arms are what prove
the refusal is not either `ArrayError`: both are still written, both would return, and neither runs.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

function launder(n Integer) returns Integer
	return n
end 'launder'

function main() returns ExitCode
	var arr = IntegerArray.create()
	arr.push(1)
	arr.push(2)
	try arr.get(launder(-1) as ElementIndex) otherwise (e) 'handler'
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
panic at laundered-negative-index-panics-at-the-door.test:13: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in main
  in mrt_start
```

<!-- test: first-empty-slot -->
<!-- tier: stdlib -->
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
<!-- tier: stdlib -->
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

<!-- test: for-over-an-empty-slot-panics -->
<!-- tier: stdlib -->
### A `for` element read of an empty slot panics
The loop reads element 0, then meets the slot `setLength` published and nothing filled. It panics with
the empty-slot message instead of dereferencing the null the slot holds. A struct element cannot be `resize`d into
existence (`error.resize-struct-element`), so the raw buffer is the only way to open an empty slot under a body that
is not generic; `for-over-an-empty-slot-in-a-generic-body-panics` opens one through `resize`.
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
	arr.push(Slot.create(5))
	arr.reserve(3)
	try arr.managed.setLength(3) otherwise panic("setLength: capacity just reserved for 3")
	for s in arr 'walk'
		print("{s.value}\n")
	end 'walk'
	return 0
end 'main'
```
```exitcode
1
```
```stdout
5
```
```stderr
panic at for-over-an-empty-slot-panics.test:19: empty slot: element 1 of the array was never filled
Stack trace:
  in main
  in mrt_start
```

<!-- test: for-over-an-empty-slot-in-a-generic-body-panics -->
### A `for` element read of an empty slot panics in a body compiled once for every element type
`Bag` is generic, so `visit` is one compiled body for every `Element`, and its loop cannot ask at compile time
whether the element owns a record. The array's own record says so at run time: `pad` grows `Bag with String` over
slots nothing wrote, and the loop panics at element 0 with the empty-slot message instead of taking a reference
to the null the slot holds.
```maxon
typealias Integer = int(i64.min to i64.max)

type Bag uses Element
	typealias ElementArray = Array with Element

	var items as ElementArray

	static function create() returns Self
		return Self{items: ElementArray.create()}
	end 'create'

	function pad(n Integer)
		self.items.resize(n as Count)
	end 'pad'

	function visit() returns Integer
		var kept = ElementArray.create()

		for e in self.items 'each'
			kept.push(e)
		end 'each'

		return kept.count() as Integer
	end 'visit'
end 'Bag'

typealias NameBag = Bag with String

function main() returns ExitCode
	var bag = NameBag.create()
	bag.pad(2)
	print("visited {bag.visit()}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at for-over-an-empty-slot-in-a-generic-body-panics.test:20: empty slot: element 0 of the array was never filled
Stack trace:
  in Bag.visit
  in main
  in mrt_start
```

<!-- test: a-zero-is-an-element-in-a-generic-body-over-scalars -->
The twin: the same body instantiated at `Integer`, where a zeroed slot is the element 0 and nothing is empty, so the
loop visits both.
```maxon
typealias Integer = int(i64.min to i64.max)

type Bag uses Element
	typealias ElementArray = Array with Element

	var items as ElementArray

	static function create() returns Self
		return Self{items: ElementArray.create()}
	end 'create'

	function pad(n Integer)
		self.items.resize(n as Count)
	end 'pad'

	function visit() returns Integer
		var kept = ElementArray.create()

		for e in self.items 'each'
			kept.push(e)
		end 'each'

		return kept.count() as Integer
	end 'visit'
end 'Bag'

typealias NumberBag = Bag with Integer

function main() returns ExitCode
	var bag = NumberBag.create()
	bag.pad(2)
	print("visited {bag.visit()}\n")
	return 0
end 'main'
```
```stdout
visited 2
```
```exitcode
0
```

<!-- test: a-static-generic-body-takes-a-borrowed-element-loop-through-the-call-sites-descriptor -->
A `static function` that does not return `Self` has no instance, so the layout descriptor it takes a reference
through arrives from its call. The loop reads a borrowed element and the `clear` drops the source; the element holds
its own reference, so the `push` stores a live record and the push extends the loop by nothing.
```maxon
typealias Integer = int(i64.min to i64.max)

type Bag uses Element
	typealias ElementArray = Array with Element

	var items as ElementArray

	static function create() returns Self
		return Self{items: ElementArray.create()}
	end 'create'

	static function keepFirstAfterClear(items ElementArray) returns Integer
		var seen = 0 as Integer

		for e in items 'each'
			items.clear()
			items.push(e)
			seen = seen + 1
		end 'each'

		return seen
	end 'keepFirstAfterClear'
end 'Bag'

typealias NameBag = Bag with String
typealias NameArray = Array with String

function main() returns ExitCode
	var names = NameArray.create()
	names.push("a {names.count()}")
	let seen = NameBag.keepFirstAfterClear(names)
	print("{seen} {names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: error.declare-array-error-enum -->
### `ArrayError` is a declaration the compiler owns
The two cases above discriminate `ArrayError`, which means the compiler must SEED that enum — and it seeds
it under the BARE name, because user source spells `throws ArrayError`.
A bare seed with no reservation is half a name: The compiler has no namespace, so a user `enum ArrayError` would
land in the same registry bucket and the later write would win, while the array runtime went on returning
the builtin's ordinals — `try arr.get(5)` would route `indexOutOfBounds` (ordinal 0) into the user's first
case with no diagnostic anywhere. It is refused, exactly as `Ordering` and `CharacterSet` are — a REFERENCE
to the name in a `throws` clause stays legal, which is the whole reason the name is bare.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

enum ArrayError
	somethingElse
	another
end 'ArrayError'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(7)
	let v = try a.get(5) otherwise (e) 'oops'
		match e 'k'
			somethingElse then return 11
			another then return 12
		end 'k'
	end 'oops'
	return v
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:6: Unsupported: a declaration of the type name 'ArrayError', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and the compiler has no namespace to tell a user declaration of the name apart from that one
```

<!-- test: error.set-length-cannot-publish-slots-of-an-element-that-excludes-zero -->
<!-- tier: stdlib -->
`setLength` publishes the slots between the length and the capacity, and a slot nothing wrote is zero, which is not a `NonZero`. The buffer layer refuses it as `resize` does, so no read hands out a `NonZero` that is zero.
```maxon
typealias NonZero = int(1 to i64.max)
typealias NonZeroArray = Array with NonZero

function main() returns ExitCode
	var xs = NonZeroArray.create()
	xs.reserve(1)
	try xs.managed.setLength(1) otherwise panic("setLength: capacity just reserved for 1")
	let d = try xs.get(0) otherwise 1
	return 100 / d
end 'main'
```
```maxoncstderr
error E3106: <fragment>:8:17: 'setLength' cannot publish slots of 'NonZero': a slot nothing wrote is zero and zero is not a value of 'NonZero'
```
