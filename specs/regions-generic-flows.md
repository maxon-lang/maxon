---
feature: regions-generic-flows
status: experimental
keywords: [immutable, generic, container, capability, store, type argument, List, Map, Array, readonly]
category: semantics
---

# Capabilities inside generic arguments

## Documentation

A type argument carries a capability: `List with Box` holds mutable `Box` values of the current region, and
`List with immutable Box` holds frozen ones. Every value flowing into a position of the generic's type
parameter is compared against that capability exactly as a store into a slot of it: a frozen or `isolated`
value into a mutable position is refused, and anything but a frozen value into an `immutable` position is
refused (`freeze` it). A call counts as a store at each parameter the callee keeps, so `List.append`,
`Map.upsert`, `Array.insert` and a declared generic's own methods are judged by one rule, with no list of names.
A parameter that is stored into an `immutable` position, or into a mutable one, takes the same class from the
instantiation, and every caller of it is held to it.

`immutable` qualifies the element type of an `Array` or `List`, the value type of a `Map` and an argument of a
declared generic `type`. It is a different type from `readonly`: `Array with immutable Box` and `Array with
readonly Box` are not interchangeable, and only the first accepts nothing but frozen elements.

## Tests

<!-- test: a-frozen-value-appended-to-a-mutable-list-refused -->
`BoxList` holds mutable boxes, so the frozen `pinned` cannot be appended to it.

```maxon
typealias Tally = int(0 to u64.max)
typealias BoxList = List with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var list = BoxList.create()
	var pinned = freeze Box.create(1)
	list.append(pinned)
	var first = try list.first() otherwise panic("empty")
	first.n = 9
	print("{first.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:14: cannot store an immutable 'Box' into 'List.append.value', which holds a mutable 'Box'; declare the element `immutable Box`
```

<!-- test: a-frozen-argument-to-a-parameter-map-upsert-stores-refused -->
`keep` stores `item` into a map of mutable boxes, so it takes mutable data and the frozen `pinned` is refused at the call.

```maxon
typealias Tally = int(0 to u64.max)
typealias BoxMap = Map with (String, Box)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function keep(into BoxMap, item Box)
	into.upsert("k", value: item)
end 'keep'

function main() returns ExitCode
	var map = BoxMap.create()
	var pinned = freeze Box.create(1)
	keep(map, item: pinned)
	print("{map.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:20:12: 'keep' writes 'item' (line 14: 'Map.upsert' keeps a value read from it in a writable slot (its parameter 'value')); 'pinned' is immutable
```

<!-- test: a-declared-generic-method-that-stores-t-refuses-a-frozen-argument -->
`Stack.push` stores its `T` into the stack's own array, and `JobStack` fixes `T` to a mutable `Job`.

```maxon
typealias Tally = int(0 to u64.max)

type Job
	export var number as Tally

	static function create(number Tally) returns Self
		return Self{number: number}
	end 'create'
end 'Job'

type Stack uses T
	typealias Items = Array with T

	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function push(item T)
		self.items.push(item)
	end 'push'

	function size() returns Tally
		return self.items.count()
	end 'size'
end 'Stack'

typealias JobStack = Stack with Job

function main() returns ExitCode
	var mine = JobStack.create()
	var pinned = freeze Job.create(2)
	mine.push(pinned)
	print("{mine.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:35:12: cannot store an immutable 'Job' into 'Stack.push.item', which holds a mutable 'Job'; declare the element `immutable Job`
```

<!-- test: array-insert-of-a-frozen-value-into-a-mutable-array-refused -->
`insert` is an ordinary library method with an opaque `T`, and it is judged like `push`.

```maxon
typealias Tally = int(0 to u64.max)
typealias BoxArray = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var arr = BoxArray.create(0)
	var pinned = freeze Box.create(1)
	arr.push(Box.create(0))
	arr.insert(0, value: pinned)
	print("{arr.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:17:23: cannot store an immutable 'Box' into 'Array.insert.value', which holds a mutable 'Box'; declare the element `immutable Box`
```

<!-- test: list-with-immutable-takes-frozen-values-and-runs -->
A list of frozen boxes takes frozen values, and the element read back is frozen too.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxList = List with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var list = FrozenBoxList.create()
	var pinned = freeze Box.create(7)
	list.append(pinned)
	let first = try list.first() otherwise panic("empty")
	print("{list.count()} {first.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 7
```

<!-- test: a-declared-generic-with-an-immutable-argument-runs-beside-a-mutable-one -->
`Stack with Job` takes mutable jobs and `Stack with immutable Job` takes frozen ones, from one declaration.

```maxon
typealias Tally = int(0 to u64.max)

type Job
	export var number as Tally

	static function create(number Tally) returns Self
		return Self{number: number}
	end 'create'
end 'Job'

type Stack uses T
	typealias Items = Array with T

	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function push(item T)
		self.items.push(item)
	end 'push'

	function size() returns Tally
		return self.items.count()
	end 'size'
end 'Stack'

typealias JobStack = Stack with Job
typealias FrozenJobStack = Stack with immutable Job

function main() returns ExitCode
	var mine = JobStack.create()
	mine.push(Job.create(1))
	var shared = FrozenJobStack.create()
	var pinned = freeze Job.create(2)
	shared.push(pinned)
	print("{mine.size()} {shared.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: a-mutable-value-pushed-into-a-declared-generic-with-an-immutable-argument-refused -->
`FrozenJobStack` fixes `T` to a frozen `Job`, so the mutable `loose` is refused by the same method that took `pinned`.

```maxon
typealias Tally = int(0 to u64.max)

type Job
	export var number as Tally

	static function create(number Tally) returns Self
		return Self{number: number}
	end 'create'
end 'Job'

type Stack uses T
	typealias Items = Array with T

	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function push(item T)
		self.items.push(item)
	end 'push'

	function size() returns Tally
		return self.items.count()
	end 'size'
end 'Stack'

typealias FrozenJobStack = Stack with immutable Job

function main() returns ExitCode
	var shared = FrozenJobStack.create()
	var loose = Job.create(2)
	shared.push(loose)
	print("{shared.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:35:14: cannot store a mutable 'Job' into 'Stack.push.item', which holds an immutable 'Job'; freeze it
```

<!-- test: a-mutable-local-pushed-into-an-immutable-array-refused -->
`Array with immutable Box` holds frozen boxes: a mutable local cannot be stored, even though it is read again.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var items = FrozenBoxes.create(0)
	var loose = Box.create(1)
	items.push(loose)
	loose.n = 5
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:13: cannot store a mutable 'Box' into 'an element of the container push writes', which holds an immutable 'Box'; freeze it
```

<!-- test: a-fresh-value-pushed-into-an-immutable-array-refused -->
A fresh value is mutable data too: the element must be frozen first.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var items = FrozenBoxes.create(0)
	items.push(Box.create(1))
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:15:13: cannot store a mutable 'Box' into 'an element of the container push writes', which holds an immutable 'Box'; freeze it
```

<!-- test: a-moved-local-pushed-into-an-immutable-array-refused -->
`moved` is not read again, but moving mutable data into a frozen element does not freeze it.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var items = FrozenBoxes.create(0)
	var moved = Box.create(1)
	items.push(moved)
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:13: cannot store a mutable 'Box' into 'an element of the container push writes', which holds an immutable 'Box'; freeze it
```

<!-- test: a-mutable-value-reaching-an-immutable-element-through-a-parameter-refused -->
`stash` stores `item` into frozen elements, so it needs frozen data and the call with the mutable `loose` is refused.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function stash(items FrozenBoxes, item Box)
	items.push(item)
end 'stash'

function main() returns ExitCode
	var items = FrozenBoxes.create(0)
	var loose = Box.create(1)
	loose.n = 5
	stash(items, item: loose)
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:21:15: 'stash' needs 'item' frozen (line 14: `items.push(item)`); 'loose' is mutable. Freeze it first
```

<!-- test: a-mutable-value-upserted-into-an-immutable-map-value-refused -->
The value type of `Map with (String, immutable Box)` is frozen, and `upsert` is judged by the same rule as `push`.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxMap = Map with (String, immutable Box)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var map = FrozenBoxMap.create()
	var loose = Box.create(1)
	map.upsert("k", value: loose)
	print("{map.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:25: cannot store a mutable 'Box' into 'Map.upsert.value', which holds an immutable 'Box'; freeze it
```

<!-- test: a-mutable-value-set-into-an-immutable-array-refused -->
`set` is the indexed store door, and it holds an `immutable` element to the same rule.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var items = FrozenBoxes.create(0)
	var pinned = freeze Box.create(1)
	items.push(pinned)
	var loose = Box.create(2)
	try items.set(0, value: loose) otherwise ignore
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:18:26: cannot store a mutable 'Box' into 'an element of the container set writes', which holds an immutable 'Box'; freeze it
```

<!-- test: an-array-literal-of-fresh-values-into-an-immutable-array-refused -->
The literal's elements are stores into the instance's slots like any other.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var items = FrozenBoxes from [Box.create(1), Box.create(2)]
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:14:32: cannot store a mutable 'Box' into 'an element of the array literal', which holds an immutable 'Box'; freeze it
```

<!-- test: readonly-and-immutable-element-arrays-are-different-types -->
The two qualifiers are two types: a frozen array is not a `readonly` view.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box
typealias ReadOnlyBoxes = Array with readonly Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function count(items ReadOnlyBoxes) returns Tally
	return items.count()
end 'count'

function main() returns ExitCode
	var items = FrozenBoxes.create(0)
	var pinned = freeze Box.create(1)
	items.push(pinned)
	print("{count(items)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:10: argument type mismatch for 'items': expected 'ReadOnlyBoxes', got 'FrozenBoxes'
```

<!-- test: a-static-factory-of-an-immutable-instance-refuses-a-mutable-argument -->
`FrozenHolder.wrap` has no receiver, and the alias fixes `T` to a frozen `Job`.

```maxon
typealias Tally = int(0 to u64.max)

type Job
	export var number as Tally

	static function create(number Tally) returns Self
		return Self{number: number}
	end 'create'
end 'Job'

type Holder uses T
	var held as T

	static function wrap(item T) returns Self
		return Self{held: item}
	end 'wrap'

	function number() returns Tally
		return 1
	end 'number'
end 'Holder'

typealias FrozenHolder = Holder with immutable Job

function main() returns ExitCode
	var loose = Job.create(2)
	var holder = FrozenHolder.wrap(loose)
	print("{holder.number()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:28:33: cannot store a mutable 'Job' into 'Holder.wrap.item', which holds an immutable 'Job'; freeze it
```

<!-- test: a-static-factory-of-a-mutable-instance-refuses-a-frozen-argument -->
`PlainHolder.wrap` holds mutable jobs, so the frozen `pinned` is refused there too.

```maxon
typealias Tally = int(0 to u64.max)

type Job
	export var number as Tally

	static function create(number Tally) returns Self
		return Self{number: number}
	end 'create'
end 'Job'

type Holder uses T
	var held as T

	static function wrap(item T) returns Self
		return Self{held: item}
	end 'wrap'

	function number() returns Tally
		return 1
	end 'number'
end 'Holder'

typealias PlainHolder = Holder with Job

function main() returns ExitCode
	var pinned = freeze Job.create(2)
	var holder = PlainHolder.wrap(pinned)
	print("{holder.number()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:28:32: cannot store an immutable 'Job' into 'Holder.wrap.item', which holds a mutable 'Job'; declare the element `immutable Job`
```

<!-- test: a-fresh-value-passed-to-an-immutable-parameter-refused -->
A fresh call result is mutable data: an `immutable` parameter takes only a frozen value, so it is written `freeze Box.create(1)`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function take(item immutable Box) returns Tally
	return item.n
end 'take'

function main() returns ExitCode
	let a = take(Box.create(1))
	print("{a}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:17:15: 'take' takes `immutable` 'item'; a fresh value is mutable. Freeze it first: write `freeze` before the fresh expression (`freeze Box.create(1)`), or around a receiver (`(freeze Table.create()).submit(a)`)
```

<!-- test: a-fresh-receiver-of-an-immutable-self-method-refused -->
`peek` is declared `immutable self`, so a fresh receiver is frozen first: `(freeze Box.create(2)).peek()`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function peek(immutable self) returns Tally
		return self.n
	end 'peek'
end 'Box'

function main() returns ExitCode
	let b = Box.create(2).peek()
	print("{b}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:17:23: 'peek' is declared `immutable self`; a fresh value is mutable. Freeze it first: write `freeze` before the fresh expression (`freeze Box.create(1)`), or around a receiver (`(freeze Table.create()).submit(a)`)
```

<!-- test: a-frozen-expression-is-an-immutable-receiver-and-argument-and-runs -->
`freeze` written before a fresh expression, in parentheses where it is a receiver, satisfies an `immutable` position.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function peek(immutable self) returns Tally
		return self.n
	end 'peek'
end 'Box'

function take(item immutable Box) returns Tally
	return item.n
end 'take'

function main() returns ExitCode
	let c = (freeze Box.create(3)).peek()
	let d = take(freeze Box.create(4))
	print("{c} {d}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 4
```

<!-- test: a-fresh-value-reaching-an-immutable-element-through-a-parameter-refused -->
`stash` stores `item` into frozen elements, so it needs frozen data and a fresh value is refused at the call like a mutable local.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function stash(items FrozenBoxes, item Box)
	items.push(item)
end 'stash'

function main() returns ExitCode
	var items = FrozenBoxes.create(0)
	stash(items, item: Box.create(1))
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:19:15: 'stash' needs 'item' frozen (line 14: `items.push(item)`); a fresh value is mutable. Freeze it first: write `freeze` before the fresh expression (`freeze Box.create(1)`), or around a receiver (`(freeze Table.create()).submit(a)`)
```

<!-- test: a-mutable-local-read-again-reaching-an-immutable-element-through-a-parameter-is-reported-once -->
The refusal names the call and `immutable`; the read-again local gets no second, `readonly` sentence.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function stash(items FrozenBoxes, item Box)
	items.push(item)
end 'stash'

function main() returns ExitCode
	var items = FrozenBoxes.create(0)
	var loose = Box.create(1)
	stash(items, item: loose)
	loose.n = 5
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:20:15: 'stash' needs 'item' frozen (line 14: `items.push(item)`); 'loose' is mutable. Freeze it first
```

<!-- test: a-frozen-field-of-a-parameter-held-by-a-let-into-a-mutable-list-refused -->
`first` is the parameter's data held by a `let`; the frozen crate's item cannot go into a list of mutable boxes, so `load` needs a mutable `crate`.

```maxon
typealias Tally = int(0 to u64.max)
typealias BoxList = List with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Crate
	export var item as Box

	static function create(item Box) returns Self
		return Self{item: item}
	end 'create'
end 'Crate'

function load(crate Crate, into BoxList)
	let first = crate.item
	into.append(first)
end 'load'

function main() returns ExitCode
	var list = BoxList.create()
	var live = freeze Crate.create(Box.create(1))
	load(live, into: list)
	print("{list.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:29:7: 'load' writes 'crate' (line 23: 'List.append' keeps a value read from it in a writable slot (its parameter 'value')); 'live' is immutable
```

<!-- test: a-frozen-field-of-a-parameter-held-by-a-let-into-an-immutable-list-runs -->
The same `load` into a list of frozen boxes needs a frozen `crate`, and gets one.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxList = List with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Crate
	export var item as Box

	static function create(item Box) returns Self
		return Self{item: item}
	end 'create'
end 'Crate'

function load(crate Crate, into FrozenBoxList)
	let first = crate.item
	into.append(first)
end 'load'

function main() returns ExitCode
	var list = FrozenBoxList.create()
	var live = freeze Crate.create(Box.create(1))
	load(live, into: list)
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-mutable-argument-for-a-parameter-held-by-a-let-into-an-immutable-list-refused-at-the-call -->
`load` is correct for a frozen `crate`, so the refusal is at the call that passes a mutable one.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxList = List with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Crate
	export var item as Box

	static function create(item Box) returns Self
		return Self{item: item}
	end 'create'
end 'Crate'

function load(crate Crate, into FrozenBoxList)
	let first = crate.item
	into.append(first)
end 'load'

function main() returns ExitCode
	var list = FrozenBoxList.create()
	var live = Crate.create(Box.create(1))
	live.item.n = 4
	load(live, into: list)
	print("{list.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:30:7: 'load' needs 'crate' frozen (line 23: 'List.append' keeps a value read from it in an `immutable` slot (its parameter 'value')); 'live' is mutable. Freeze it first
```

<!-- test: a-receiverless-generic-call-refuses-a-frozen-argument-for-the-inferred-instance -->
`Holder.wrap(pinned)` names no alias, so `T` is inferred as the plain `Job`; a frozen argument needs the alias `Holder with immutable Job`.

```maxon
typealias Tally = int(0 to u64.max)

type Job
	export var number as Tally

	static function create(number Tally) returns Self
		return Self{number: number}
	end 'create'
end 'Job'

type Holder uses T
	var held as T

	static function wrap(item T) returns Self
		return Self{held: item}
	end 'wrap'

	function number() returns Tally
		return 1
	end 'number'
end 'Holder'

function main() returns ExitCode
	var pinned = freeze Job.create(2)
	var holder = Holder.wrap(pinned)
	print("{holder.number()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:26:27: cannot store an immutable 'Job' into 'Holder.wrap.item', which holds a mutable 'Job'; declare the element `immutable Job`
```

<!-- test: a-receiverless-generic-call-with-a-mutable-argument-runs -->
The inferred instance is plain, and a mutable argument fits it.

```maxon
typealias Tally = int(0 to u64.max)

type Job
	export var number as Tally

	static function create(number Tally) returns Self
		return Self{number: number}
	end 'create'
end 'Job'

type Holder uses T
	var held as T

	static function wrap(item T) returns Self
		return Self{held: item}
	end 'wrap'

	function number() returns Tally
		return 1
	end 'number'
end 'Holder'

function main() returns ExitCode
	var loose = Job.create(2)
	var holder = Holder.wrap(loose)
	print("{holder.number()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-frozen-item-of-a-tuple-is-not-writable -->
The literal `(a, b)` is typed `(immutable Box, Box)`, so `pair.1` is writable and `pair.0` is not.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var pair = (a, b)
	pair.1.n = 6
	pair.0.n = 5
	print("{a.n} {b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:17:2: cannot write 'n' through 'pair.0', which is immutable
```

<!-- test: a-tuple-parameter-with-an-immutable-item-takes-a-frozen-item-and-runs -->
`(immutable Box, Box)` spells the item capabilities; the first item reads, the second item is written.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function weigh(pair (immutable Box, Box)) returns Tally
	pair.1.n = pair.1.n + 1
	return pair.0.n + pair.1.n
end 'weigh'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	print("{weigh((a, b))} {b.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 3
```

<!-- test: a-write-through-an-immutable-tuple-item-parameter-refused -->
A parameter spelled `(immutable Box, Box)` reads its first item and cannot write through it.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function bump(pair (immutable Box, Box))
	pair.0.n = 9
end 'bump'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	bump((a, b))
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:13:2: cannot write 'n' through 'pair.0', which is immutable
```

<!-- test: a-destructured-frozen-item-is-not-writable -->
Destructuring binds each name at its item's capability, so `x` is frozen and `y` is writable.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var (x, y) = (a, b)
	y.n = 9
	x.n = 8
	print("{a.n} {b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:17:2: cannot write 'n' through 'x', which is immutable
```

<!-- test: a-destructured-tuple-binds-each-item-at-its-capability-and-runs -->
The writable item of a destructured tuple is written and the frozen one is read.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	let (x, y) = (a, b)
	y.n = x.n + 8
	print("{x.n} {y.n} {b.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 9 9
```

<!-- test: a-list-of-tuples-with-an-immutable-item-takes-a-matching-tuple-and-runs -->
`List with (immutable Box, Box)` stores a tuple whose first item is frozen and reads it back at the same capabilities.

```maxon
typealias Tally = int(0 to u64.max)
typealias Mixed = (immutable Box, Box)
typealias MixedList = List with Mixed

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var mixed = MixedList.create()
	mixed.append((a, b))
	var found = try mixed.get(0) otherwise panic("empty")
	found.1.n = 5
	print("{found.0.n} {b.n} {mixed.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 5 1
```

<!-- test: a-frozen-item-into-a-list-of-plain-tuples-refused -->
`List with (Box, Box)` holds mutable items only, so the tuple with a frozen first item is refused where it is stored.

```maxon
typealias Tally = int(0 to u64.max)
typealias PlainPair = (Box, Box)
typealias PlainList = List with PlainPair

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var plain = PlainList.create()
	plain.append((a, b))
	print("{plain.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:18:16: cannot store an immutable 'Box' into 'an item of the tuple', which holds a mutable 'Box'; declare the item `immutable Box`
```

<!-- test: a-tuple-bound-to-a-local-into-a-list-of-plain-tuples-refused -->
The local `pair` is typed `(immutable Box, Box)`; a list of `(Box, Box)` is a different type.

```maxon
typealias Tally = int(0 to u64.max)
typealias PlainPair = (Box, Box)
typealias PlainList = List with PlainPair

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var pair = (a, b)
	var plain = PlainList.create()
	plain.append(pair)
	print("{plain.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:8: argument type mismatch for 'value': expected '(Box, Box)', got '(immutable Box, Box)'
```

<!-- test: a-tuple-with-another-item-capability-is-another-type -->
`(immutable Box, Box)` and `(Box, Box)` are two types, so the parameter refuses the first.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function weigh(pair (Box, Box)) returns Tally
	return pair.0.n + pair.1.n
end 'weigh'

function pairOf(first immutable Box, second Box) returns (immutable Box, Box)
	return (first, second)
end 'pairOf'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	let both = pairOf(a, second: b)
	print("{weigh(both)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:24:10: argument type mismatch for 'pair': expected '(Box, Box)', got '(immutable Box, Box)'
```

<!-- test: a-tuple-result-type-carries-its-item-capabilities-and-runs -->
The result `(immutable Box, Box)` flows into a parameter of the same tuple type.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function weigh(pair (immutable Box, Box)) returns Tally
	return pair.0.n + pair.1.n
end 'weigh'

function pairOf(first immutable Box, second Box) returns (immutable Box, Box)
	return (first, second)
end 'pairOf'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	let both = pairOf(a, second: b)
	print("{weigh(both)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: a-nested-tuple-item-capability-is-compared-and-not-writable -->
Item capabilities nest: the first item of the inner tuple is frozen, so writing through `pair.0.0` is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var c = Box.create(3)
	var pair = ((a, b), c)
	pair.0.1.n = 7
	pair.0.0.n = 5
	print("{a.n} {b.n} {c.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:18:2: cannot write 'n' through 'pair.0.0', which is immutable
```

<!-- test: a-nested-tuple-with-another-item-capability-is-another-type -->
The inner tuple's item capabilities are part of the outer tuple's type.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function weigh(pair ((Box, Box), Box)) returns Tally
	return pair.0.0.n + pair.0.1.n + pair.1.n
end 'weigh'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var c = Box.create(3)
	let nested = ((a, b), c)
	print("{weigh(nested)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:21:10: argument type mismatch for 'pair': expected '((Box, Box), Box)', got '((immutable Box, Box), Box)'
```

<!-- test: an-isolated-tuple-item-type-refused -->
An item is `immutable` or unmarked; an `isolated` value cannot be a tuple element.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function weigh(pair (isolated Box, Box)) returns Tally
	return pair.1.n
end 'weigh'

function main() returns ExitCode
	var b = Box.create(2)
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:12:22: Expected 'a type' but got 'isolated'
```

<!-- test: a-frozen-tuple-handed-to-a-generic-method-that-stores-its-items-refused -->
`absorb` stores both items of its `(T, T)` argument, and `BoxBag` fixes `T` to a mutable `Box`; the tuple cannot be built from frozen boxes.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Bag uses T
	typealias Items = Array with T

	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function absorb(pair (T, T))
		self.items.push(pair.0)
		self.items.push(pair.1)
	end 'absorb'

	function size() returns Tally
		return self.items.count()
	end 'size'
end 'Bag'

typealias BoxBag = Bag with Box

function main() returns ExitCode
	var bag = BoxBag.create()
	var a = freeze Box.create(1)
	var b = freeze Box.create(2)
	bag.absorb((a, b))
	print("{bag.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:37:14: cannot store an immutable 'Box' into 'an item of the tuple', which holds a mutable 'Box'; declare the item `immutable Box`
```

<!-- test: a-tuple-of-frozen-items-handed-to-an-immutable-generic-method-runs -->
`absorb` takes `(T, T)` with `T` fixed to `immutable Box`, so a tuple of two frozen boxes is stored.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Bag uses T
	typealias Items = Array with T

	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function absorb(pair (T, T))
		self.items.push(pair.0)
		self.items.push(pair.1)
	end 'absorb'

	function size() returns Tally
		return self.items.count()
	end 'size'
end 'Bag'

typealias FrozenBag = Bag with immutable Box

function main() returns ExitCode
	var bag = FrozenBag.create()
	var a = freeze Box.create(1)
	var b = freeze Box.create(2)
	bag.absorb((a, b))
	print("{bag.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: an-isolated-value-in-a-tuple-literal-refused -->
An `isolated` value is merged before it joins a tuple.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create(1) as isolated Box
	var b = Box.create(2)
	var pair = (a, b)
	pair.0.n = 5
	print("{pair.0.n} {b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:15:14: cannot store an isolated 'Box' into 'an item of the tuple', which holds a mutable 'Box'; `merge` it first
```

<!-- test: an-isolated-value-in-a-map-literal-refused -->
An `isolated` value is merged before it becomes a map value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create(1) as isolated Box
	var m = ["k": a]
	var found = try m.get("k") otherwise panic("missing")
	found.n = 5
	print("{found.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:14:16: cannot store an isolated 'Box' into 'a value of the map literal', which holds a mutable 'Box'; `merge` it first
```

<!-- test: a-read-only-element-in-a-tuple-literal-refused -->
`held` is read out of a `readonly` element, and the tuple keeps it where the write through `pair.0` is refused.

```maxon
typealias Tally = int(0 to u64.max)
typealias ReadOnlyBoxes = Array with readonly Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var items = ReadOnlyBoxes.create(0)
	items.push(Box.create(1))
	let held = try items.get(0) otherwise panic("empty")
	var b = Box.create(2)
	var pair = (held, b)
	pair.0.n = 5
	print("{pair.0.n} {b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:19:2: cannot write through 'pair.0', which may be a record read out of a `readonly` element that a call or a merge handed back; nothing reachable through a `readonly` element may be written; use clone()
```

<!-- test: a-read-only-element-in-a-map-literal-refused -->
`held` is read out of a `readonly` element, and the map would keep it where something may write it.

```maxon
typealias Tally = int(0 to u64.max)
typealias ReadOnlyBoxes = Array with readonly Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var items = ReadOnlyBoxes.create(0)
	items.push(Box.create(1))
	let held = try items.get(0) otherwise panic("empty")
	var m = ["k": held]
	var found = try m.get("k") otherwise panic("missing")
	found.n = 5
	print("{found.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:17:11: cannot pass a value read out of a `readonly` element, which is read-only, to parameter 'value' of 'Map.upsert', which keeps it where something may write it; pass a clone()
```

<!-- test: a-set-literal-of-frozen-elements-runs -->
A set stores its elements read-only, so a frozen element needs no capability of its own.

```maxon
typealias Tally = int(0 to u64.max)
typealias Keys = Set with Key

type Key implements Hashable, Equatable
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function hash() returns HashValue
		return self.n as HashValue
	end 'hash'

	function equals(other Key) returns bool
		return self.n == other.n
	end 'equals'
end 'Key'

function main() returns ExitCode
	let a = freeze Key.create(1)
	var keys = Keys from [a]
	print("{keys.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-nested-list-of-another-element-capability-is-another-type -->
`List with List with Box` and `List with List with immutable Box` differ in the inner element capability.

```maxon
typealias Tally = int(0 to u64.max)
typealias NestedBoxes = List with BoxList
typealias BoxList = List with Box
typealias FrozenBoxList = List with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var inner = FrozenBoxList.create()
	inner.append(a)
	var nested = NestedBoxes.create()
	nested.append(inner)
	print("{nested.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:9: argument type mismatch for 'value': expected 'BoxList', got 'FrozenBoxList'
```

<!-- test: a-frozen-value-in-a-map-literal-refused -->
A map literal stores its values into the map's value slots, and an inferred map holds mutable values.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var m = ["k": a]
	var found = try m.get("k") otherwise panic("missing")
	found.n = 7
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:14:16: cannot store an immutable 'Box' into 'a value of the map literal', which holds a mutable 'Box'; declare the value `immutable Box`
```

<!-- test: a-loop-variable-over-an-immutable-array-is-blamed-as-immutable -->
The elements are frozen by the array's type, and the refusal says `immutable`, not `readonly`.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var items = FrozenBoxes.create(0)
	var pinned = freeze Box.create(1)
	items.push(pinned)

	for item in items 'each'
		item.n = 3
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:19:3: cannot write through 'item': 'item' is read-only (a `for` element of a read-only source) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: a-value-read-out-of-an-immutable-list-bound-to-a-var-is-blamed-as-immutable -->
`first` is read out of a list whose elements are frozen, so it cannot be a `var`.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxList = List with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var list = FrozenBoxList.create()
	var pinned = freeze Box.create(7)
	list.append(pinned)
	var first = try list.first() otherwise panic("empty")
	print("{first.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:17:6: cannot assign a value, which may share storage with an `immutable` element, to mutable binding 'first'; use 'let' instead of 'var', or use clone()
```

<!-- test: an-element-handed-back-by-a-call-over-an-immutable-array-is-blamed-as-immutable -->
`pick` returns an `immutable` element of a frozen array, so writing through the binding `held` is blamed on the `immutable` element.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(items FrozenBoxes) returns immutable Box
	return try items.get(0) otherwise panic("empty")
end 'pick'

function main() returns ExitCode
	var items = FrozenBoxes.create(0)
	var pinned = freeze Box.create(1)
	items.push(pinned)
	var held = pick(items)
	held.n = 5
	print("{pinned.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:22:2: cannot write 'n' through 'held', which is immutable
```

<!-- test: an-element-handed-back-by-a-call-over-a-readonly-array-is-blamed-as-readonly -->
The same shape over `Array with readonly Box` is blamed as a `readonly` element.

```maxon
typealias Tally = int(0 to u64.max)
typealias ReadOnlyBoxes = Array with readonly Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(items ReadOnlyBoxes) returns Box
	return try items.get(0) otherwise panic("empty")
end 'pick'

function main() returns ExitCode
	var items = ReadOnlyBoxes.create(0)
	items.push(Box.create(1))
	var held = pick(items)
	held.n = 5
	print("{held.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:20:6: cannot assign a value, which may share storage with a `readonly` element, to mutable binding 'held'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-declared-generic-over-a-tuple-of-its-parameter-takes-frozen-and-mutable-arguments-and-runs -->
`Pair` is a tuple of the declared generic's `T`, so its items are frozen for `Pairs with immutable Box` and mutable for `Pairs with Box`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pairs uses T
	typealias Pair = (T, T)
	typealias Items = List with Pair

	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(first T, second T)
		self.items.append((first, second))
	end 'add'

	function size() returns Tally
		return self.items.count()
	end 'size'
end 'Pairs'

typealias FrozenPairs = Pairs with immutable Box
typealias BoxPairs = Pairs with Box

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = freeze Box.create(2)
	var frozen = FrozenPairs.create()
	frozen.add(a, second: b)
	var c = Box.create(3)
	var d = Box.create(4)
	var plain = BoxPairs.create()
	plain.add(c, second: d)
	print("{frozen.size()} {plain.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: a-declared-generic-over-a-tuple-of-its-parameter-refuses-a-frozen-argument-for-a-mutable-instance -->
`BoxPairs.add` stores its arguments into the mutable items of a `(Box, Box)` tuple, so a frozen first argument is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pairs uses T
	typealias Pair = (T, T)
	typealias Items = List with Pair

	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(first T, second T)
		self.items.append((first, second))
	end 'add'

	function size() returns Tally
		return self.items.count()
	end 'size'
end 'Pairs'

typealias BoxPairs = Pairs with Box

function main() returns ExitCode
	var a = freeze Box.create(1)
	var d = Box.create(4)
	var plain = BoxPairs.create()
	plain.add(a, second: d)
	print("{plain.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:37:12: cannot store an immutable 'Box' into 'Pairs.add.first', which holds a mutable 'Box'; declare the element `immutable Box`
```

<!-- test: an-item-handed-back-by-a-call-over-a-tuple-with-a-frozen-item-is-immutable -->
`leak` returns `pair.0` as an ordinary `Box`; the tuple argument holds a frozen item, so the result is `immutable` and `got` cannot be written through.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function leak(pair (immutable Box, Box)) returns Box
	return pair.0
end 'leak'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var got = leak((a, b))
	got.n = 9
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:20:2: cannot write 'n' through 'got', which is immutable
```

<!-- test: an-item-handed-back-by-a-call-over-a-list-of-tuples-with-a-frozen-item-is-immutable -->
The frozen item reaches the result through the list, and the list's element type names it.

```maxon
typealias Tally = int(0 to u64.max)
typealias Mixed = (immutable Box, Box)
typealias MixedList = List with Mixed

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function firstOf(list MixedList) returns Box
	let first = try list.get(0) otherwise panic("empty")
	return first.0
end 'firstOf'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var mixed = MixedList.create()
	mixed.append((a, b))
	var got = firstOf(mixed)
	got.n = 9
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:25:2: cannot write 'n' through 'got', which is immutable
```

<!-- test: a-tuple-with-a-frozen-item-returned-as-a-plain-tuple-refused -->
The result `(Box, Box)` is a different type from the parameter's `(immutable Box, Box)`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function widen(pair (immutable Box, Box)) returns (Box, Box)
	return pair
end 'widen'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var wide = widen((a, b))
	print("{wide.0.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:13:2: Cannot return '(immutable Box, Box)' from function declared to return '(Box, Box)'
```

<!-- test: a-frozen-item-copied-into-a-plain-result-tuple-refused -->
Building `(pair.0, pair.1)` for a `(Box, Box)` result stores the frozen first item into a mutable item.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function widen(pair (immutable Box, Box)) returns (Box, Box)
	return (pair.0, pair.1)
end 'widen'

function main() returns ExitCode
	var a = freeze Box.create(1)
	var b = Box.create(2)
	var wide = widen((a, b))
	print("{wide.0.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:13:10: cannot store an immutable 'Box' into 'an item of the tuple', which holds a mutable 'Box'; declare the item `immutable Box`
```

<!-- test: a-nested-array-of-another-capability-is-another-type -->
`Bag.Items` is `Array with Box` for `BoxBag`; an `Array with immutable Box` is a different type, so the argument is refused as a type mismatch.

```maxon
typealias Tally = int(0 to u64.max)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Bag uses T
	typealias Items = Array with T

	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function absorb(more Items)
		self.items = more
	end 'absorb'

	function size() returns Tally
		return self.items.count()
	end 'size'
end 'Bag'

typealias BoxBag = Bag with Box

function main() returns ExitCode
	var bag = BoxBag.create()
	var frozen = FrozenBoxes.create(0)
	var pinned = freeze Box.create(1)
	frozen.push(pinned)
	bag.absorb(frozen)
	print("{bag.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:38:6: argument type mismatch for 'more': expected 'Array with Box', got 'FrozenBoxes'
```

<!-- test: a-closure-that-writes-its-parameter-is-refused-for-an-immutable-instance -->
`Step` takes `T`, which `FrozenVisitor` fixes to `immutable Box`; `bump` writes its parameter, so the closure cannot stand for a `Step`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Visitor uses T
	typealias Step = function(T) returns Tally

	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function visit(item T, step Step)
		self.total = self.total + step(item)
	end 'visit'

	function sum() returns Tally
		return self.total
	end 'sum'
end 'Visitor'

typealias FrozenVisitor = Visitor with immutable Box

function bump(b Box) returns Tally
	b.n = b.n + 1
	return b.n
end 'bump'

function main() returns ExitCode
	var visitor = FrozenVisitor.create()
	var pinned = freeze Box.create(1)
	visitor.visit(pinned, step: function(b Box) gives bump(b))
	print("{visitor.sum()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3210: <fragment>:40:30: function value for 'Visitor.Step' writes its parameter 'b', but 'Visitor.Step' does not declare it `mutable`
```

<!-- test: a-closure-that-reads-its-parameter-runs-for-an-immutable-instance -->
A closure that only reads its parameter takes the frozen box.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Visitor uses T
	typealias Step = function(T) returns Tally

	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function visit(item T, step Step)
		self.total = self.total + step(item)
	end 'visit'

	function sum() returns Tally
		return self.total
	end 'sum'
end 'Visitor'

typealias FrozenVisitor = Visitor with immutable Box

function main() returns ExitCode
	var visitor = FrozenVisitor.create()
	var pinned = freeze Box.create(1)
	visitor.visit(pinned, step: function(b Box) gives b.n)
	print("{visitor.sum()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-fresh-value-returned-through-a-local-from-an-immutable-result-refused -->
A fresh value is mutable, so a `returns immutable` function freezes it before returning it, through a local too.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function viaLocal() returns immutable Box
	let fresh = Box.create(1)
	return fresh
end 'viaLocal'

function main() returns ExitCode
	let a = viaLocal()
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:14:9: a return does not fit the declared result: 'fresh' is mutable, the result is immutable; freeze it
```

<!-- test: a-fresh-value-in-an-immutable-field-initializer-refused -->
An `immutable` field holds frozen data, so its initializer is frozen first.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Shelf
	export var held as immutable Box

	static function create() returns Self
		return Self{held: Box.create(1)}
	end 'create'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create()
	print("{shelf.held.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:21: cannot store a mutable 'Box' into 'Shelf.held', which holds an immutable 'Box'; freeze it
```

<!-- test: a-fresh-value-assigned-to-an-immutable-field-refused -->
So is a later assignment to it.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Shelf
	export var held as immutable Box

	static function create() returns Self
		return Self{held: freeze Box.create(1)}
	end 'create'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create()
	shelf.held = Box.create(2)
	print("{shelf.held.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:22:15: cannot store a mutable 'Box' into 'held', which holds an immutable 'Box'; freeze it
```

<!-- test: a-fresh-value-sent-to-an-immutable-message-parameter-refused -->
A message takes a frozen argument in an `immutable` parameter, so the fresh `Leaf` is frozen first.

```maxon
typealias Tally = int(0 to u64.max)

type Leaf
	export var weight as Tally

	static function create(weight Tally) returns Self
		return Self{weight: weight}
	end 'create'
end 'Leaf'

type Reader
	static function create() returns Self
		return Self{}
	end 'create'

	export function weigh(leaf immutable Leaf) returns Tally
		return leaf.weight
	end 'weigh'
end 'Reader'

function main() returns ExitCode
	let reader = spawn Reader.create()
	let first = try reader.weigh(Leaf.create(3)) otherwise panic("main: a fresh reader accepts its only request")
	let a = await first
	reader.shutdown()
	print("{a}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:24:31: 'weigh' takes `immutable` 'leaf'; a fresh value is mutable. Freeze it first: write `freeze` before the fresh expression (`freeze Box.create(1)`), or around a receiver (`(freeze Table.create()).submit(a)`)
```

<!-- test: a-fresh-value-replied-from-an-immutable-message-result-refused -->
A reply declared `returns immutable` is frozen where it is returned.

```maxon
typealias Tally = int(0 to u64.max)

type Leaf
	export var weight as Tally

	static function create(weight Tally) returns Self
		return Self{weight: weight}
	end 'create'
end 'Leaf'

type Reader
	static function create() returns Self
		return Self{}
	end 'create'

	export function make() returns immutable Leaf
		return Leaf.create(9)
	end 'make'
end 'Reader'

function main() returns ExitCode
	let reader = spawn Reader.create()
	reader.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:18:10: a return does not fit the declared result: 'Leaf.create(9)' is mutable, the result is immutable; freeze it
```

<!-- test: a-field-assignment-into-an-immutable-instance-refuses-a-mutable-value -->
`FrozenHolder` fixes `T` to a frozen `Job`, so the public field `held` is an immutable slot and `loose` cannot be assigned to it.

```maxon
typealias Tally = int(0 to u64.max)

type Job
	export var number as Tally

	static function create(number Tally) returns Self
		return Self{number: number}
	end 'create'
end 'Job'

type Holder uses T
	export var held as T

	static function wrap(item T) returns Self
		return Self{held: item}
	end 'wrap'
end 'Holder'

typealias FrozenHolder = Holder with immutable Job

function main() returns ExitCode
	var pinned = freeze Job.create(1)
	var loose = Job.create(2)
	var holder = FrozenHolder.wrap(pinned)
	holder.held = loose
	print("{holder.held.number}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:26:16: cannot store a mutable 'Job' into 'held', which holds an immutable 'Job'; freeze it
```

<!-- test: a-field-assignment-into-an-immutable-instance-takes-a-frozen-value-and-runs -->
The same field takes a frozen value, and a mutable instance of the same type takes a mutable one.

```maxon
typealias Tally = int(0 to u64.max)

type Job
	export var number as Tally

	static function create(number Tally) returns Self
		return Self{number: number}
	end 'create'
end 'Job'

type Holder uses T
	export var held as T

	static function wrap(item T) returns Self
		return Self{held: item}
	end 'wrap'
end 'Holder'

typealias FrozenHolder = Holder with immutable Job
typealias PlainHolder = Holder with Job

function main() returns ExitCode
	var pinned = freeze Job.create(1)
	var other = freeze Job.create(3)
	var holder = FrozenHolder.wrap(pinned)
	holder.held = other
	var loose = Job.create(2)
	var plain = PlainHolder.wrap(loose)
	var next = Job.create(4)
	plain.held = next
	print("{holder.held.number} {plain.held.number}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 4
```

<!-- test: a-method-of-the-library-that-stores-its-record-parameter-refuses-frozen-data -->
`HttpServer.setLimits` keeps `caps` in a mutable field, so the frozen `limits` is refused at the call whatever the program spells.

```maxon
function main() returns ExitCode
	var limits = freeze HttpServerLimits.create()
	var server = try HttpServer.bind("127.0.0.1", port: 0) otherwise panic("bind")
	server.setLimits(limits)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:5:19: 'setLimits' writes 'caps' (line 273: `self.limits = caps`); 'limits' is immutable
```

<!-- test: a-factory-of-the-library-that-stores-its-record-parameter-refuses-frozen-data -->
`TlsCertificateEntry.create` builds a record around the certificate it is given, so the frozen `der` is refused at the call whatever the program spells.

```maxon
function main() returns ExitCode
	let der = freeze ByteArray.create(0)
	let extensions = TlsExtensionArray.create(0)
	let entry = TlsCertificateEntry.create(der, extensions: extensions)
	print("{entry.certificate().count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3208: <fragment>:5:34: 'create' returns 'certificate' or 'extensions'; they must both be mutable or both immutable here
```

<!-- test: a-generic-function-declared-to-return-immutable-hands-back-frozen-data -->
`frozenRow` is generic and declares `returns immutable`, so a call to it gives frozen data that `bump`, which writes its parameter, refuses.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function frozenRow(_ T) uses T returns immutable Box
	let fresh = freeze Box.create(1)
	return fresh
end 'frozenRow'

function bump(box Box) returns Tally
	box.n = box.n + 1
	return box.n
end 'bump'

function main() returns ExitCode
	print("{bump(frozenRow(1))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:23:15: 'bump' writes 'box' (line 18: `box.n = box.n + 1`); 'frozenRow(1)' is immutable
```

<!-- test: a-generic-function-declared-to-return-isolated-hands-back-an-isolated-value -->
`make` is generic and declares `returns isolated`, so the local that holds its result is an isolated value that `Vault.wrap` takes into its `isolated` parameter.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Vault
	export var held as isolated Box

	static function wrap(box isolated Box) returns Self
		return Self{held: box}
	end 'wrap'
end 'Vault'

function make(_ T) uses T returns isolated Box
	let fresh = Box.create(1) as isolated Box
	return fresh
end 'make'

function main() returns ExitCode
	let region = make(1)
	let vault = Vault.wrap(region)
	print("{vault.held.n}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```
