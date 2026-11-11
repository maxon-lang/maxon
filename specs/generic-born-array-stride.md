---
feature: generic-born-array-stride
status: stable
keywords: [array, generics, element-size, stride, layout-descriptor, shared-generic-body, packed, bool, byte]
category: memory
---
# An array created in a shared generic body strides by its element

## Documentation

A generic type's body is compiled once for every instantiation, and the element of an `Array with Element` that body
creates is a type parameter there. The record is nonetheless stamped with the stride of the concrete element the
instantiation binds: one byte for a `Byte`, two for a 16-bit element, four for a 32-bit one, a packed 1, 2 or 4 bits
for a `bool`, an `int(0 to 3)` and an `int(0 to 15)`, and a machine word for an element that is a pointer to a
record. The stride is read from the instantiation's layout descriptor when the array is created, so an array a shared
body creates is the same size as one a concrete body creates, and a record read through its substituted type has
exactly the stride that type states.

An enum stored in a signed narrow slot (an enum with a negative raw value) strides its slot like any other element. The
array's record also says the slot is signed, and every element the shared body reads out of the array is sign-extended
by it, so the value `-1` a shared body stores is the value `-1` it reads back. `managed.elementSize()` is the stride
alone.

Every operation of the shared body moves elements at the record's own stride. The byte-assuming built-ins
(`__Builtins.readLittleEndian`, `appendLittleEndian`, the AES calls) therefore see a `Bag with Byte`'s `items` as the
bytes it holds.

## Tests


<!-- test: a-generic-born-byte-array-strides-by-its-element -->
<!-- tier: stdlib -->
A `Bag uses Element` creates its `Array with Element` inside a body shared by every instantiation. At `Bag with Byte` the record strides 1, exactly as the same array created in a concrete body does, and every access path of the shared body reads and writes elements at that stride: `push`, `set`, `get`, `last`, `pop`, `insert`, `remove`, iteration, `clone`, `slice`, `append`, `appendMemory`, `growFilled`, `refill` and `reserve`. The caller reads the same array through its substituted type, with the stride the type states, and the two agree.
```maxon
typealias Total = int(0 to u64.max)

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(value Element)
		items.push(value)
	end 'add'

	function put(i ElementIndex, value Element)
		try items.set(i, value: value) otherwise panic("Bag.put: index out of range")
	end 'put'

	function at(i ElementIndex) returns Element
		return try items.get(i) otherwise panic("Bag.at: index out of range")
	end 'at'

	function lastOne() returns Element
		return try items.last() otherwise panic("Bag.lastOne: empty")
	end 'lastOne'

	function dropLast() returns Element
		return try items.pop() otherwise panic("Bag.dropLast: empty")
	end 'dropLast'

	function insertAt(i ElementIndex, value Element)
		items.insert(i, value: value)
	end 'insertAt'

	function removeAt(i ElementIndex) returns Element
		return try items.remove(i) otherwise panic("Bag.removeAt: index out of range")
	end 'removeAt'

	function cloned() returns Self
		return Self{items: items.clone()}
	end 'cloned'

	function window(start ElementIndex, endIndex ElementIndex) returns Self
		return Self{items: try items.slice(start, endIndex: endIndex) otherwise panic("Bag.window: range out of bounds")}
	end 'window'

	function addAll(other Self)
		items.append(other.items)
	end 'addAll'

	function addMemory(other Self)
		items.appendMemory(other.items.managed)
	end 'addMemory'

	function replayed() returns Self
		let out = Self{items: Items.create(0)}

		for value in items 'each'
			out.add(value)
		end 'each'

		return out
	end 'replayed'

	function grownTo(length ElementIndex, value Element)
		items.growFilled(length, value: value)
	end 'grownTo'

	function refilled(length ElementIndex, value Element)
		items.refill(length, value: value)
	end 'refilled'

	function reserved(capacity ElementIndex)
		items.reserve(capacity)
	end 'reserved'
end 'Bag'

typealias ByteBag = Bag with Byte

function value(i Count) returns Byte
	return ((i * 37 + 11) mod 251) as Byte
end 'value'

function sumOf(bag ByteBag) returns Total
	var total = 0 as Total

	for element in bag.items 'walk'
		total = total + (element as Total)
	end 'walk'

	return total
end 'sumOf'

function sumDirect(bag ByteBag) returns Total
	var total = 0 as Total

	for at in 0 upto bag.items.count() 'each'
		total = total + ((try bag.items.get(at) otherwise panic("sumDirect: index out of range")) as Total)
	end 'each'

	return total
end 'sumDirect'

function sumAt(bag ByteBag) returns Total
	var total = 0 as Total

	for at in 0 upto bag.items.count() 'each'
		total = total + (bag.at(at) as Total)
	end 'each'

	return total
end 'sumAt'

function main() returns ExitCode
	let bag = ByteBag.create()

	for i in 0 upto 300 'fill'
		bag.add(value(i))
	end 'fill'

	print("born {bag.items.managed.elementSize()} count {bag.items.count()} sum {sumOf(bag)} {sumDirect(bag)} {sumAt(bag)}\n")

	bag.put(5, value: 250)
	try bag.items.set(299, value: 7) otherwise return 1
	bag.put(7, value: 1)
	print("put {(bag.at(5) as Total)} {((try bag.items.get(299) otherwise return 2) as Total)} {(bag.at(7) as Total)} count {bag.items.count()} sum {sumOf(bag)}\n")

	bag.insertAt(10, value: 99)
	let removed = bag.removeAt(20)
	print("insert {(bag.at(10) as Total)} removed {(removed as Total)} count {bag.items.count()} sum {sumOf(bag)}\n")

	let last = bag.lastOne()
	let popped = bag.dropLast()
	print("last {(last as Total)} popped {(popped as Total)} count {bag.items.count()} sum {sumOf(bag)}\n")

	let snapshot = bag.cloned()
	bag.put(0, value: 1)
	print("clone {snapshot.items.managed.elementSize()} count {snapshot.items.count()} sum {sumOf(snapshot)} first {(snapshot.at(0) as Total)} source first {(bag.at(0) as Total)}\n")

	let part = bag.window(100, endIndex: 200)
	print("window {part.items.managed.elementSize()} count {part.items.count()} sum {sumOf(part)}\n")

	let joined = part.cloned()
	joined.addAll(part)
	print("append {joined.items.managed.elementSize()} count {joined.items.count()} sum {sumOf(joined)}\n")

	let memory = part.cloned()
	memory.addMemory(part)
	print("appendMemory {memory.items.managed.elementSize()} count {memory.items.count()} sum {sumOf(memory)}\n")

	let replay = bag.replayed()
	print("replay {replay.items.managed.elementSize()} count {replay.items.count()} sum {sumOf(replay)}\n")

	let grown = ByteBag.create()
	grown.add(value(0))
	grown.grownTo(70, value: 9)
	print("grow {grown.items.managed.elementSize()} count {grown.items.count()} sum {sumOf(grown)} first {(grown.at(0) as Total)} last {(grown.lastOne() as Total)}\n")

	let filled = ByteBag.create()
	filled.add(value(1))
	filled.refilled(40, value: 3)
	print("refill {filled.items.managed.elementSize()} count {filled.items.count()} sum {sumOf(filled)}\n")

	let roomy = ByteBag.create()
	roomy.add(value(1))
	roomy.add(value(2))
	roomy.add(value(3))
	roomy.reserved(1000)
	print("reserve {roomy.items.managed.elementSize()} count {roomy.items.count()} capacity-ok {roomy.items.capacity() >= 1000}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
born 1 count 300 sum 37525 37525 37525
put 250 7 1 count 300 sum 37538
insert 99 removed 212 count 300 sum 37425
last 7 popped 7 count 299 sum 37418
clone 1 count 299 sum 37418 first 11 source first 1
window 1 count 100 sum 12592
append 1 count 200 sum 25184
appendMemory 1 count 200 sum 25184
replay 1 count 299 sum 37408
grow 1 count 70 sum 632 first 11 last 9
refill 1 count 40 sum 120
reserve 1 count 3 capacity-ok true
```


<!-- test: a-generic-born-array-strides-by-its-element-at-every-narrow-and-wide-width -->
<!-- tier: stdlib -->
The same `Bag uses Element` at `Bag with 16-bit`, `Bag with 32-bit`, `Bag with bool`, `Bag with int(0 to 3)` and `Bag with int(0 to 15)`: each record strides 2, 4, and a packed -1, -2 and -4, exactly as the same array created in a concrete body does. An element of the wrong stride is a different wrong answer at every width, so each instantiation pushes, sets, inserts, removes, pops, clones, slices, appends, iterates and grows through the shared body and reads the same elements back through its substituted type.
```maxon
typealias Total = int(0 to u64.max)

function bit(flag bool) returns Total
	if flag 'set'
		return 1
	end 'set'

	return 0
end 'bit'

typealias Half = int(0 to 65535)

typealias Wide = int(0 to u32.max)

typealias Crumb = int(0 to 3)

typealias Nibble = int(0 to 15)

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(value Element)
		items.push(value)
	end 'add'

	function put(i ElementIndex, value Element)
		try items.set(i, value: value) otherwise panic("Bag.put: index out of range")
	end 'put'

	function at(i ElementIndex) returns Element
		return try items.get(i) otherwise panic("Bag.at: index out of range")
	end 'at'

	function lastOne() returns Element
		return try items.last() otherwise panic("Bag.lastOne: empty")
	end 'lastOne'

	function dropLast() returns Element
		return try items.pop() otherwise panic("Bag.dropLast: empty")
	end 'dropLast'

	function insertAt(i ElementIndex, value Element)
		items.insert(i, value: value)
	end 'insertAt'

	function removeAt(i ElementIndex) returns Element
		return try items.remove(i) otherwise panic("Bag.removeAt: index out of range")
	end 'removeAt'

	function cloned() returns Self
		return Self{items: items.clone()}
	end 'cloned'

	function window(start ElementIndex, endIndex ElementIndex) returns Self
		return Self{items: try items.slice(start, endIndex: endIndex) otherwise panic("Bag.window: range out of bounds")}
	end 'window'

	function addAll(other Self)
		items.append(other.items)
	end 'addAll'

	function addMemory(other Self)
		items.appendMemory(other.items.managed)
	end 'addMemory'

	function replayed() returns Self
		let out = Self{items: Items.create(0)}

		for value in items 'each'
			out.add(value)
		end 'each'

		return out
	end 'replayed'

	function grownTo(length ElementIndex, value Element)
		items.growFilled(length, value: value)
	end 'grownTo'

	function refilled(length ElementIndex, value Element)
		items.refill(length, value: value)
	end 'refilled'

	function reserved(capacity ElementIndex)
		items.reserve(capacity)
	end 'reserved'
end 'Bag'

typealias HalfBag = Bag with Half

function halfValue(i Count) returns Half
	return ((i * 1237 + 11) mod 65521) as Half
end 'halfValue'

function halfSum(bag HalfBag) returns Total
	var total = 0 as Total

	for element in bag.items 'walk'
		total = total + (element as Total)
	end 'walk'

	return total
end 'halfSum'

typealias WideBag = Bag with Wide

function wideValue(i Count) returns Wide
	return ((i * 2654435 + 11) mod 4294967291) as Wide
end 'wideValue'

function wideSum(bag WideBag) returns Total
	var total = 0 as Total

	for element in bag.items 'walk'
		total = total + (element as Total)
	end 'walk'

	return total
end 'wideSum'

typealias FlagBag = Bag with bool

function flagValue(i Count) returns bool
	return i mod 3 == 0
end 'flagValue'

function flagSum(bag FlagBag) returns Total
	var total = 0 as Total

	for element in bag.items 'walk'
		total = total + bit(element)
	end 'walk'

	return total
end 'flagSum'

typealias CrumbBag = Bag with Crumb

function crumbValue(i Count) returns Crumb
	return ((i * 5 + 1) mod 4) as Crumb
end 'crumbValue'

function crumbSum(bag CrumbBag) returns Total
	var total = 0 as Total

	for element in bag.items 'walk'
		total = total + (element as Total)
	end 'walk'

	return total
end 'crumbSum'

typealias NibbleBag = Bag with Nibble

function nibbleValue(i Count) returns Nibble
	return ((i * 7 + 3) mod 16) as Nibble
end 'nibbleValue'

function nibbleSum(bag NibbleBag) returns Total
	var total = 0 as Total

	for element in bag.items 'walk'
		total = total + (element as Total)
	end 'walk'

	return total
end 'nibbleSum'

function main() returns ExitCode
	let half = HalfBag.create()

	for i in 0 upto 300 'fillHalf'
		half.add(halfValue(i))
	end 'fillHalf'

	print("half born {half.items.managed.elementSize()} count {half.items.count()} sum {halfSum(half)}\n")

	half.put(5, value: 65000)
	try half.items.set(299, value: halfValue(7)) otherwise return 1
	half.put(7, value: 1)
	half.insertAt(10, value: 40000)
	let halfRemoved = half.removeAt(20)
	let halfLast = half.lastOne()
	let halfPopped = half.dropLast()
	print("half edit {(half.at(5) as Total)} {(half.at(10) as Total)} {(halfRemoved as Total)} {(halfLast as Total)} {(halfPopped as Total)} count {half.items.count()} sum {halfSum(half)}\n")

	let halfCopy = half.cloned()
	half.put(0, value: 1)
	let halfPart = half.window(100, endIndex: 200)
	let halfJoined = halfPart.cloned()
	halfJoined.addAll(halfPart)
	let halfReplay = half.replayed()
	print("half clone {halfCopy.items.managed.elementSize()} {halfSum(halfCopy)} window {halfPart.items.managed.elementSize()} {halfSum(halfPart)} append {halfJoined.items.managed.elementSize()} {halfSum(halfJoined)} replay {halfReplay.items.managed.elementSize()} {halfSum(halfReplay)}\n")

	let halfGrown = HalfBag.create()
	halfGrown.add(halfValue(0))
	halfGrown.grownTo(70, value: 777)
	print("half grow {halfGrown.items.managed.elementSize()} count {halfGrown.items.count()} sum {halfSum(halfGrown)}\n")

	let wide = WideBag.create()

	for i in 0 upto 300 'fillWide'
		wide.add(wideValue(i))
	end 'fillWide'

	print("wide born {wide.items.managed.elementSize()} count {wide.items.count()} sum {wideSum(wide)}\n")

	wide.put(5, value: 4000000000)
	try wide.items.set(299, value: wideValue(7)) otherwise return 1
	wide.put(7, value: 1)
	wide.insertAt(10, value: 3000000000)
	let wideRemoved = wide.removeAt(20)
	let wideLast = wide.lastOne()
	let widePopped = wide.dropLast()
	print("wide edit {(wide.at(5) as Total)} {(wide.at(10) as Total)} {(wideRemoved as Total)} {(wideLast as Total)} {(widePopped as Total)} count {wide.items.count()} sum {wideSum(wide)}\n")

	let wideCopy = wide.cloned()
	wide.put(0, value: 1)
	let widePart = wide.window(100, endIndex: 200)
	let wideJoined = widePart.cloned()
	wideJoined.addAll(widePart)
	let wideReplay = wide.replayed()
	print("wide clone {wideCopy.items.managed.elementSize()} {wideSum(wideCopy)} window {widePart.items.managed.elementSize()} {wideSum(widePart)} append {wideJoined.items.managed.elementSize()} {wideSum(wideJoined)} replay {wideReplay.items.managed.elementSize()} {wideSum(wideReplay)}\n")

	let wideGrown = WideBag.create()
	wideGrown.add(wideValue(0))
	wideGrown.grownTo(70, value: 777777)
	print("wide grow {wideGrown.items.managed.elementSize()} count {wideGrown.items.count()} sum {wideSum(wideGrown)}\n")

	let flag = FlagBag.create()

	for i in 0 upto 300 'fillFlag'
		flag.add(flagValue(i))
	end 'fillFlag'

	print("flag born {flag.items.managed.elementSize()} count {flag.items.count()} sum {flagSum(flag)}\n")

	flag.put(5, value: true)
	try flag.items.set(299, value: flagValue(7)) otherwise return 1
	flag.put(7, value: false)
	flag.insertAt(10, value: true)
	let flagRemoved = flag.removeAt(20)
	let flagLast = flag.lastOne()
	let flagPopped = flag.dropLast()
	print("flag edit {bit(flag.at(5))} {bit(flag.at(10))} {bit(flagRemoved)} {bit(flagLast)} {bit(flagPopped)} count {flag.items.count()} sum {flagSum(flag)}\n")

	let flagCopy = flag.cloned()
	flag.put(0, value: false)
	let flagPart = flag.window(100, endIndex: 200)
	let flagJoined = flagPart.cloned()
	flagJoined.addAll(flagPart)
	let flagReplay = flag.replayed()
	print("flag clone {flagCopy.items.managed.elementSize()} {flagSum(flagCopy)} window {flagPart.items.managed.elementSize()} {flagSum(flagPart)} append {flagJoined.items.managed.elementSize()} {flagSum(flagJoined)} replay {flagReplay.items.managed.elementSize()} {flagSum(flagReplay)}\n")

	let flagGrown = FlagBag.create()
	flagGrown.add(flagValue(0))
	flagGrown.grownTo(70, value: true)
	print("flag grow {flagGrown.items.managed.elementSize()} count {flagGrown.items.count()} sum {flagSum(flagGrown)}\n")

	let crumb = CrumbBag.create()

	for i in 0 upto 300 'fillCrumb'
		crumb.add(crumbValue(i))
	end 'fillCrumb'

	print("crumb born {crumb.items.managed.elementSize()} count {crumb.items.count()} sum {crumbSum(crumb)}\n")

	crumb.put(5, value: 3)
	try crumb.items.set(299, value: crumbValue(7)) otherwise return 1
	crumb.put(7, value: 0)
	crumb.insertAt(10, value: 3)
	let crumbRemoved = crumb.removeAt(20)
	let crumbLast = crumb.lastOne()
	let crumbPopped = crumb.dropLast()
	print("crumb edit {(crumb.at(5) as Total)} {(crumb.at(10) as Total)} {(crumbRemoved as Total)} {(crumbLast as Total)} {(crumbPopped as Total)} count {crumb.items.count()} sum {crumbSum(crumb)}\n")

	let crumbCopy = crumb.cloned()
	crumb.put(0, value: 0)
	let crumbPart = crumb.window(100, endIndex: 200)
	let crumbJoined = crumbPart.cloned()
	crumbJoined.addAll(crumbPart)
	let crumbReplay = crumb.replayed()
	print("crumb clone {crumbCopy.items.managed.elementSize()} {crumbSum(crumbCopy)} window {crumbPart.items.managed.elementSize()} {crumbSum(crumbPart)} append {crumbJoined.items.managed.elementSize()} {crumbSum(crumbJoined)} replay {crumbReplay.items.managed.elementSize()} {crumbSum(crumbReplay)}\n")

	let crumbGrown = CrumbBag.create()
	crumbGrown.add(crumbValue(0))
	crumbGrown.grownTo(70, value: 2)
	print("crumb grow {crumbGrown.items.managed.elementSize()} count {crumbGrown.items.count()} sum {crumbSum(crumbGrown)}\n")

	let nibble = NibbleBag.create()

	for i in 0 upto 300 'fillNibble'
		nibble.add(nibbleValue(i))
	end 'fillNibble'

	print("nibble born {nibble.items.managed.elementSize()} count {nibble.items.count()} sum {nibbleSum(nibble)}\n")

	nibble.put(5, value: 15)
	try nibble.items.set(299, value: nibbleValue(7)) otherwise return 1
	nibble.put(7, value: 2)
	nibble.insertAt(10, value: 12)
	let nibbleRemoved = nibble.removeAt(20)
	let nibbleLast = nibble.lastOne()
	let nibblePopped = nibble.dropLast()
	print("nibble edit {(nibble.at(5) as Total)} {(nibble.at(10) as Total)} {(nibbleRemoved as Total)} {(nibbleLast as Total)} {(nibblePopped as Total)} count {nibble.items.count()} sum {nibbleSum(nibble)}\n")

	let nibbleCopy = nibble.cloned()
	nibble.put(0, value: 2)
	let nibblePart = nibble.window(100, endIndex: 200)
	let nibbleJoined = nibblePart.cloned()
	nibbleJoined.addAll(nibblePart)
	let nibbleReplay = nibble.replayed()
	print("nibble clone {nibbleCopy.items.managed.elementSize()} {nibbleSum(nibbleCopy)} window {nibblePart.items.managed.elementSize()} {nibbleSum(nibblePart)} append {nibbleJoined.items.managed.elementSize()} {nibbleSum(nibbleJoined)} replay {nibbleReplay.items.managed.elementSize()} {nibbleSum(nibbleReplay)}\n")

	let nibbleGrown = NibbleBag.create()
	nibbleGrown.add(nibbleValue(0))
	nibbleGrown.grownTo(70, value: 5)
	print("nibble grow {nibbleGrown.items.managed.elementSize()} count {nibbleGrown.items.count()} sum {nibbleSum(nibbleGrown)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
half born 2 count 300 sum 9290445
half edit 65000 40000 23514 8670 8670 count 299 sum 9314797
half clone 2 9314797 window 2 3096815 append 2 6193630 replay 2 9314787
half grow 2 count 70 sum 53624
wide born 4 count 300 sum 119051413050
wide edit 4000000000 3000000000 50434276 18581056 18581056 count 299 sum 125175449457
wide clone 4 125175449457 window 4 39683804350 append 4 79367608700 replay 4 125175449447
wide grow 4 count 70 sum 53666624
flag born -1 count 300 sum 100
flag edit 1 1 0 0 0 count 299 sum 102
flag clone -1 102 window -1 33 append -1 66 replay -1 101
flag grow -1 count 70 sum 70
crumb born -2 count 300 sum 450
crumb edit 3 3 0 0 0 count 299 sum 454
crumb clone -2 454 window -2 150 append -2 300 replay -2 453
crumb grow -2 count 70 sum 139
nibble born -4 count 300 sum 2242
nibble edit 15 12 8 4 4 count 299 sum 2253
nibble clone -4 2253 window -4 758 append -4 1516 replay -4 2252
nibble grow -4 count 70 sum 348
```

<!-- test: a-map-over-a-narrow-key-stores-its-keys-at-the-keys-width -->
<!-- tier: stdlib -->
`Map` builds its key column inside a shared generic body, so the column's stride is the key's: one byte for a `Byte` key, two for a 16-bit key. Insertion, replacement, removal and growth of the table all move keys at that width, and every key and value reads back.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Total = int(0 to u64.max)
typealias Half = int(0 to 65535)
typealias ByteMap = Map with (Byte, Integer)
typealias HalfMap = Map with (Half, Integer)
typealias Crumb = int(0 to 3)
typealias CrumbMap = Map with (Crumb, Integer)

function main() returns ExitCode
	let bytes = ByteMap.create()

	for i in 0 upto 200 'fillBytes'
		bytes.upsert(((i * 7) mod 256) as Byte, value: i)
	end 'fillBytes'

	for i in 0 upto 200 'removeBytes'
		if i mod 5 == 0 'every'
			_ = bytes.remove(((i * 7) mod 256) as Byte)
		end 'every'
	end 'removeBytes'

	for i in 0 upto 200 'replaceBytes'
		if i mod 3 == 0 and bytes.contains(((i * 7) mod 256) as Byte) 'present'
			bytes.upsert(((i * 7) mod 256) as Byte, value: i + 1000)
		end 'present'
	end 'replaceBytes'

	var byteKeys = 0 as Total
	var byteValues = 0 as Integer

	for (key, value) in bytes 'walkBytes'
		byteKeys = byteKeys + (key as Total)
		byteValues = byteValues + value
	end 'walkBytes'

	print("byte count {bytes.count()} keys {byteKeys} values {byteValues}\n")

	let halves = HalfMap.create()

	for i in 0 upto 300 'fillHalves'
		halves.upsert(((i * 1237 + 5) mod 65521) as Half, value: i)
	end 'fillHalves'

	for i in 0 upto 300 'removeHalves'
		if i mod 5 == 0 'every'
			_ = halves.remove(((i * 1237 + 5) mod 65521) as Half)
		end 'every'
	end 'removeHalves'

	for i in 0 upto 300 'replaceHalves'
		if i mod 3 == 0 and halves.contains(((i * 1237 + 5) mod 65521) as Half) 'present'
			halves.upsert(((i * 1237 + 5) mod 65521) as Half, value: i + 1000)
		end 'present'
	end 'replaceHalves'

	var halfKeys = 0 as Total
	var halfValues = 0 as Integer

	for (key, value) in halves 'walkHalves'
		halfKeys = halfKeys + (key as Total)
		halfValues = halfValues + value
	end 'walkHalves'

	print("half count {halves.count()} keys {halfKeys} values {halfValues}\n")

	let crumbs = CrumbMap.create()
	crumbs.upsert(0, value: 10)
	crumbs.upsert(3, value: 11)
	crumbs.upsert(1, value: 12)
	crumbs.upsert(3, value: 13)
	let onThree = try crumbs.get(3) otherwise return 1
	let onZero = try crumbs.get(0) otherwise return 2
	print("crumb count {crumbs.count()} three {onThree} zero {onZero}\n")
	_ = crumbs.remove(3)
	print("crumb after remove count {crumbs.count()} has three {crumbs.contains(3)} has one {crumbs.contains(1)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
byte count 160 keys 19584 values 69000
half count 240 keys 7448314 values 116000
crumb count 3 three 13 zero 10
crumb after remove count 2 has three false has one true
```

<!-- test: a-generic-born-array-of-a-narrow-signed-enum-strides-its-slot-and-keeps-its-sign -->
<!-- tier: stdlib -->
An enum with negative raw values is stored in a signed one-, two- or four-byte slot, and an array a shared generic body creates over it strides that slot like every other array. The array's record says the slot is signed, so every element the shared body reads comes out sign-extended: the value `-1` that goes in comes out as `-1` through `get`, `first`, `last`, `pop`, `remove`, `peek`, iteration, `clone` and the substituted type, and the signed arms of `insert`, `growFilled`, `refill` and `append` (of a constant literal and of a concrete array) store and read the same value at every width. The extension has to happen inside the shared body: a `Map` and a `Set` keyed by the enum find a key they inserted and iterate the keys they hold only because `Map.findSlot`'s `existing == key` compares the key it reads out of the column, sign-extended, with the key the caller passes; zero-extended, the stored `-1` would read `255` and no inserted key would be found.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Signal
	minus = -1
	zero = 0
	plus = 1
end 'Signal'

enum Mid
	low = -300
	zero = 0
	high = 300
end 'Mid'

enum Big
	low = -70000
	zero = 0
	high = 70000
end 'Big'

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	static function wrapping(source Items) returns Self
		let own = Items.create(0)
		own.append(source)

		return Self{items: own}
	end 'wrapping'

	function add(value Element)
		items.push(value)
	end 'add'

	function at(i ElementIndex) returns Element
		return try items.get(i) otherwise panic("Bag.at: index out of range")
	end 'at'

	function cloned() returns Self
		return Self{items: items.clone()}
	end 'cloned'

	function firstOne() returns Element
		return try items.first() otherwise panic("Bag.firstOne: empty")
	end 'firstOne'

	function lastOne() returns Element
		return try items.last() otherwise panic("Bag.lastOne: empty")
	end 'lastOne'

	function peekedAt(ahead ElementIndex) returns Element
		let cursor = try items.cursor() otherwise panic("Bag.peekedAt: empty")

		return try cursor.peek(ahead) otherwise panic("Bag.peekedAt: past the end")
	end 'peekedAt'

	function dropLast() returns Element
		return try items.pop() otherwise panic("Bag.dropLast: empty")
	end 'dropLast'

	function removeAt(i ElementIndex) returns Element
		return try items.remove(i) otherwise panic("Bag.removeAt: index out of range")
	end 'removeAt'

	function replayed() returns Self
		let out = Self{items: Items.create(0)}

		for value in items 'each'
			out.add(value)
		end 'each'

		return out
	end 'replayed'

	function insertAt(i ElementIndex, value Element)
		items.insert(i, value: value)
	end 'insertAt'

	function grownTo(length ElementIndex, value Element)
		items.growFilled(length, value: value)
	end 'grownTo'

	function refilled(length ElementIndex, value Element)
		items.refill(length, value: value)
	end 'refilled'

	function addAll(other Self)
		items.append(other.items)
	end 'addAll'
end 'Bag'

typealias SignalBag = Bag with Signal
typealias MidBag = Bag with Mid
typealias BigBag = Bag with Big
typealias MidArray = Array with Mid
typealias BigArray = Array with Big
typealias SignalArray = Array with Signal
typealias SignalMap = Map with (Signal, Integer)
typealias SignalSet = Set with Signal

function sumOf(bag SignalBag) returns Integer
	var total = 0 as Integer

	for signal in bag.items 'walk'
		total = total + (signal.rawValue as Integer)
	end 'walk'

	return total
end 'sumOf'

function sumOfArray(items SignalArray) returns Integer
	var total = 0 as Integer

	for at in 0 upto items.count() 'each'
		let signal = try items.get(at) otherwise panic("sumOfArray: index out of range")
		total = total + (signal.rawValue as Integer)
	end 'each'

	return total
end 'sumOfArray'

function sumSignal(bag SignalBag) returns Integer
	var total = 0 as Integer

	for element in bag.items 'walk'
		total = total + (element.rawValue as Integer)
	end 'walk'

	return total
end 'sumSignal'

function probeSignal()
	let bag = SignalBag.create()
	bag.add(Signal.plus)
	bag.insertAt(0, value: Signal.minus)
	bag.grownTo(4, value: Signal.minus)
	print("signal grown first {bag.firstOne().rawValue as Integer} second {bag.at(1).rawValue as Integer} last {bag.lastOne().rawValue as Integer} sum {sumSignal(bag)}\n")
	bag.refilled(2, value: Signal.minus)
	print("signal refilled count {bag.items.count()} sum {sumSignal(bag)}\n")
	bag.addAll(SignalBag.wrapping([Signal.minus, Signal.plus]))
	print("signal appended literal count {bag.items.count()} third {bag.at(2).rawValue as Integer} sum {sumSignal(bag)}\n")
	let concrete = SignalArray.create(0)
	concrete.push(Signal.plus)
	concrete.append([Signal.minus, Signal.plus])
	var total = 0 as Integer

	for element in concrete 'walkConcrete'
		total = total + (element.rawValue as Integer)
	end 'walkConcrete'

	print("signal appended onto concrete count {concrete.count()} sum {total}\n")
end 'probeSignal'

function sumMid(bag MidBag) returns Integer
	var total = 0 as Integer

	for element in bag.items 'walk'
		total = total + (element.rawValue as Integer)
	end 'walk'

	return total
end 'sumMid'

function probeMid()
	let bag = MidBag.create()
	bag.add(Mid.high)
	bag.insertAt(0, value: Mid.low)
	bag.grownTo(4, value: Mid.low)
	print("mid grown first {bag.firstOne().rawValue as Integer} second {bag.at(1).rawValue as Integer} last {bag.lastOne().rawValue as Integer} sum {sumMid(bag)}\n")
	bag.refilled(2, value: Mid.low)
	print("mid refilled count {bag.items.count()} sum {sumMid(bag)}\n")
	bag.addAll(MidBag.wrapping([Mid.low, Mid.high]))
	print("mid appended literal count {bag.items.count()} third {bag.at(2).rawValue as Integer} sum {sumMid(bag)}\n")
	let concrete = MidArray.create(0)
	concrete.push(Mid.high)
	concrete.append([Mid.low, Mid.high])
	var total = 0 as Integer

	for element in concrete 'walkConcrete'
		total = total + (element.rawValue as Integer)
	end 'walkConcrete'

	print("mid appended onto concrete count {concrete.count()} sum {total}\n")
end 'probeMid'

function sumBig(bag BigBag) returns Integer
	var total = 0 as Integer

	for element in bag.items 'walk'
		total = total + (element.rawValue as Integer)
	end 'walk'

	return total
end 'sumBig'

function probeBig()
	let bag = BigBag.create()
	bag.add(Big.high)
	bag.insertAt(0, value: Big.low)
	bag.grownTo(4, value: Big.low)
	print("big grown first {bag.firstOne().rawValue as Integer} second {bag.at(1).rawValue as Integer} last {bag.lastOne().rawValue as Integer} sum {sumBig(bag)}\n")
	bag.refilled(2, value: Big.low)
	print("big refilled count {bag.items.count()} sum {sumBig(bag)}\n")
	bag.addAll(BigBag.wrapping([Big.low, Big.high]))
	print("big appended literal count {bag.items.count()} third {bag.at(2).rawValue as Integer} sum {sumBig(bag)}\n")
	let concrete = BigArray.create(0)
	concrete.push(Big.high)
	concrete.append([Big.low, Big.high])
	var total = 0 as Integer

	for element in concrete 'walkConcrete'
		total = total + (element.rawValue as Integer)
	end 'walkConcrete'

	print("big appended onto concrete count {concrete.count()} sum {total}\n")
end 'probeBig'

function main() returns ExitCode
	let bag = SignalBag.create()
	bag.add(Signal.minus)
	bag.add(Signal.zero)
	bag.add(Signal.plus)
	bag.add(Signal.minus)

	let first = bag.at(0)
	let copy = bag.cloned()
	let replay = bag.replayed()
	print("signal {bag.items.managed.elementSize()} first {first.rawValue as Integer} sum {sumOf(bag)} clone {sumOf(copy)} replay {sumOf(replay)}\n")

	print("first {bag.firstOne().rawValue as Integer} peek {bag.peekedAt(3).rawValue as Integer} {bag.peekedAt(2).rawValue as Integer}\n")

	let concrete = SignalArray.create(0)
	concrete.push(Signal.minus)
	concrete.push(Signal.plus)
	try bag.items.set(2, value: Signal.minus) otherwise return 3
	print("direct {sumOfArray(bag.items)} {sumOfArray(concrete)} {concrete.managed.elementSize()}\n")

	let literal = [Signal.minus, Signal.zero, Signal.minus]
	let wrapped = SignalBag.wrapping(literal)
	print("literal {wrapped.at(0).rawValue as Integer} {wrapped.lastOne().rawValue as Integer} {sumOf(wrapped)}\n")

	let last = bag.lastOne()
	let popped = bag.dropLast()
	let removed = bag.removeAt(1)
	print("ends {last.rawValue as Integer} {popped.rawValue as Integer} {removed.rawValue as Integer} count {bag.items.count()}\n")

	let map = SignalMap.create()
	map.upsert(Signal.minus, value: 10)
	map.upsert(Signal.plus, value: 20)
	map.upsert(Signal.zero, value: 30)
	map.upsert(Signal.minus, value: 11)
	let onMinus = try map.get(Signal.minus) otherwise return 1
	let dropped = map.remove(Signal.plus)
	print("map count {map.count()} minus {onMinus} removed {dropped} has plus {map.contains(Signal.plus)} has zero {map.contains(Signal.zero)}\n")

	var keySum = 0 as Integer
	var valueSum = 0 as Integer

	for (key, value) in map 'entries'
		keySum = keySum + (key.rawValue as Integer)
		valueSum = valueSum + value
	end 'entries'

	print("entries keys {keySum} values {valueSum}\n")

	let set = SignalSet.create()
	set.insert(Signal.minus)
	set.insert(Signal.minus)
	set.insert(Signal.plus)
	print("set count {set.count()} has minus {set.contains(Signal.minus)} has zero {set.contains(Signal.zero)}\n")

	probeSignal()
	probeMid()
	probeBig()

	return 0
end 'main'
```
```exitcode
0
```
```stdout
signal 1 first -1 sum -1 clone -1 replay -1
first -1 peek -1 1
direct -3 0 1
literal -1 -1 -2
ends -1 -1 0 count 2
map count 2 minus 11 removed true has plus false has zero true
entries keys -1 values 41
set count 2 has minus true has zero false
signal grown first -1 second 1 last -1 sum -2
signal refilled count 2 sum -2
signal appended literal count 4 third -1 sum -2
signal appended onto concrete count 3 sum 1
mid grown first -300 second 300 last -300 sum -600
mid refilled count 2 sum -600
mid appended literal count 4 third -300 sum -600
mid appended onto concrete count 3 sum 300
big grown first -70000 second 70000 last -70000 sum -140000
big refilled count 2 sum -140000
big appended literal count 4 third -70000 sum -140000
big appended onto concrete count 3 sum 70000
```

<!-- test: a-generic-born-array-of-records-stays-pointer-strided -->
<!-- tier: stdlib -->
An element that is a record is stored in an array as a pointer, so a `Bag` of `String` or of a struct creates its array at the machine word, as the same array created in a concrete body does. Elements move through `push`, `get`, `clone`, `slice` and iteration, and the records are released once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Stamp
	export let tick as Integer
	export let label as String

	static function create(tick Integer, label String) returns Self
		return Self{tick: tick, label: label}
	end 'create'
end 'Stamp'

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(value Element)
		items.push(value)
	end 'add'

	function at(i ElementIndex) returns Element
		return try items.get(i) otherwise panic("Bag.at: index out of range")
	end 'at'

	function cloned() returns Self
		return Self{items: items.clone()}
	end 'cloned'

	function window(start ElementIndex, endIndex ElementIndex) returns Self
		return Self{items: try items.slice(start, endIndex: endIndex) otherwise panic("Bag.window: range out of bounds")}
	end 'window'
end 'Bag'

typealias TextBag = Bag with String
typealias StampBag = Bag with Stamp

function main() returns ExitCode
	let texts = TextBag.create()
	let stamps = StampBag.create()

	for i in 0 upto 40 'fill'
		texts.add("text {i}")
		stamps.add(Stamp.create(i, label: "stamp {i}"))
	end 'fill'

	let copy = texts.cloned()
	let part = stamps.window(10, endIndex: 20)
	var tickSum = 0 as Integer

	for stamp in part.items 'walk'
		tickSum = tickSum + stamp.tick
	end 'walk'

	print("strides {texts.items.managed.elementSize()} {stamps.items.managed.elementSize()} {copy.items.managed.elementSize()} {part.items.managed.elementSize()}\n")
	print("text {texts.at(7)} {copy.at(39)} stamp {stamps.at(12).label} {part.at(0).tick} ticks {tickSum} count {part.items.count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
strides 8 8 8 8
text text 7 text 39 stamp stamp 12 10 ticks 145 count 10
```
