---
feature: generic-opaque-var-binding
status: stable
keywords: [generics, type-parameter, ownership, layout-descriptor, retain, var, rebind, dictionary]
category: type-system
---

# A `var` of type-parameter type owns its value from its declaration

## Documentation

A shared generic body compiles ONCE for every instantiation, so a `var` it declares over its own type
parameter cannot name the reference protocol its value owes. The concrete twin can: `var chosen = fallback`
over a `String` takes a copy at the binding and releases it when `chosen` is rebound or goes out of scope,
and over a struct it takes an incref and releases that.

**A `var` of type-parameter type OWNS its value from its declaration.** The binding takes its reference
through the enclosing instance's layout descriptor, exactly as the concrete binding door does: the
`retainFunc` word holds `__str_clone` for a byte-record argument, `__mm_retain` for a managed aggregate and
0 for an argument that owns nothing, so a scalar instantiation takes no reference at all. Every later
rebinding of the `var` then releases the value it replaces and takes the reference of the value it stores,
whatever the source was: a loop element read out of a container, a borrowed parameter, an owned call
result, or a consumed parameter that was also pushed into an array.

A `let` keeps BORROWING: it names a value another owner holds and takes no reference of its own.

The three failures this pins are the three ways a `var` that did not own could go wrong. A loop element
bound to a `var` that was never retained came back as a `String` with the right byte length and all-zero
bytes, because the container's own release freed the text under it. A `var` that owned on one path of an
`if` and borrowed on the other made the merged value's ownership disagree between the two edges, which the
compiler reported as a panic. A parameter pushed into an array and also bound to a `var` that was then
rebound released a reference nobody took, and the runtime crashed in `__mm_retain`.

## Tests

### A Map key rebound inside a loop keeps its bytes

<!-- test: map-loop-rebind-keeps-the-key -->
`var chosen = fallback` and `chosen = key` inside `for (key, _) in self` over an `extension Map`, at a `String`, a struct (a `Map` key is `Hashable` and `Equatable`) and a scalar key. The returned key is the one stored in the map, so the map still contains it.
```maxon
type Tag implements Hashable, Equatable
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Tag
		return Tag{name: name, n: n}
	end 'create'

	function hash() returns HashValue
		return (n and 0xFFFF) as HashValue
	end 'hash'

	function equals(other Tag) returns bool
		return n == other.n and name == other.name
	end 'equals'
end 'Tag'

typealias TextCountMap = Map with (String, Count)
typealias TagCountMap = Map with (Tag, Count)
typealias TallyCountMap = Map with (Count, Count)

extension Map
	function lastKeyViaVar(fallback Key) returns Key
		var chosen = fallback
		for (key, _) in self 'eachKey'
			chosen = key
		end 'eachKey'
		return chosen
	end 'lastKeyViaVar'
end 'Map'

function main() returns ExitCode
	let texts = TextCountMap.create()
	texts.upsert("key {40 + 2} long enough for a heap record", value: 1)
	let s = texts.lastKeyViaVar("fallback {40 + 2} long enough for a heap record")
	print("{s} {s.byteLength()} {texts.contains(s)}\n")

	let tags = TagCountMap.create()
	tags.upsert(Tag.create("tag {40 + 2} long enough for a heap record", n: 7), value: 1)
	let t = tags.lastKeyViaVar(Tag.create("fallback {40 + 2} long enough for a heap record", n: 9))
	print("{t.name} {t.n} {tags.contains(t)}\n")

	let tallies = TallyCountMap.create()
	tallies.upsert(5, value: 1)
	let c = tallies.lastKeyViaVar(3)
	print("{c} {tallies.contains(c)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
key 42 long enough for a heap record 36 true
tag 42 long enough for a heap record 7 true
5 true
```

### A Set element rebound inside a loop keeps its bytes

<!-- test: set-loop-rebind-keeps-the-element -->
The same rebind over `extension Set`, at a `String`, a struct and a scalar element.
```maxon
type Tag implements Hashable, Equatable
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Tag
		return Tag{name: name, n: n}
	end 'create'

	function hash() returns HashValue
		return (n and 0xFFFF) as HashValue
	end 'hash'

	function equals(other Tag) returns bool
		return n == other.n and name == other.name
	end 'equals'
end 'Tag'

typealias TextSet = Set with String
typealias TagSet = Set with Tag
typealias TallySet = Set with Count

extension Set
	function lastViaVar(fallback Element) returns Element
		var chosen = fallback
		for element in self 'eachElement'
			chosen = element
		end 'eachElement'
		return chosen
	end 'lastViaVar'
end 'Set'

function main() returns ExitCode
	let texts = TextSet.create()
	texts.insert("element {40 + 2} long enough for a heap record")
	let s = texts.lastViaVar("fallback {40 + 2} long enough for a heap record")
	print("{s} {s.byteLength()} {texts.contains(s)}\n")

	let tags = TagSet.create()
	tags.insert(Tag.create("tag {40 + 2} long enough for a heap record", n: 7))
	let t = tags.lastViaVar(Tag.create("fallback {40 + 2} long enough for a heap record", n: 9))
	print("{t.name} {t.n} {tags.contains(t)}\n")

	let tallies = TallySet.create()
	tallies.insert(5)
	let c = tallies.lastViaVar(3)
	print("{c} {tallies.contains(c)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
element 42 long enough for a heap record 40 true
tag 42 long enough for a heap record 7 true
5 true
```

### A Map entry destructured by `let` and rebound into a `var`

<!-- test: map-loop-entry-destructured-by-let -->
The same loop spelled `for entry in self`, then `let (key, _) = entry`, then `chosen = key`. The `let` borrows out of the entry and the `var` owns what it is rebound to.
```maxon
type Tag implements Hashable, Equatable
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Tag
		return Tag{name: name, n: n}
	end 'create'

	function hash() returns HashValue
		return (n and 0xFFFF) as HashValue
	end 'hash'

	function equals(other Tag) returns bool
		return n == other.n and name == other.name
	end 'equals'
end 'Tag'

typealias TextCountMap = Map with (String, Count)
typealias TagCountMap = Map with (Tag, Count)
typealias TallyCountMap = Map with (Count, Count)

extension Map
	function lastKeyViaEntry(fallback Key) returns Key
		var chosen = fallback
		for entry in self 'eachEntry'
			let (key, _) = entry
			chosen = key
		end 'eachEntry'
		return chosen
	end 'lastKeyViaEntry'
end 'Map'

function main() returns ExitCode
	let texts = TextCountMap.create()
	texts.upsert("key {40 + 2} long enough for a heap record", value: 1)
	let s = texts.lastKeyViaEntry("fallback {40 + 2} long enough for a heap record")
	print("{s} {s.byteLength()} {texts.contains(s)}\n")

	let tags = TagCountMap.create()
	tags.upsert(Tag.create("tag {40 + 2} long enough for a heap record", n: 7), value: 1)
	let t = tags.lastKeyViaEntry(Tag.create("fallback {40 + 2} long enough for a heap record", n: 9))
	print("{t.name} {t.n} {tags.contains(t)}\n")

	let tallies = TallyCountMap.create()
	tallies.upsert(5, value: 1)
	let c = tallies.lastKeyViaEntry(3)
	print("{c} {tallies.contains(c)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
key 42 long enough for a heap record 36 true
tag 42 long enough for a heap record 7 true
5 true
```

### A `var` bound from a borrow and rebound from an owned call result

<!-- test: if-rebind-borrowed-alias-from-owned-result -->
`var chosen = fallback` binds a borrowed parameter; one branch of an `if` rebinds it to a call that hands back an owned value. The merged value must be owned on both edges. Each instantiation runs with the flag true and false.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Box uses Item
	var stored as Item

	static function create(stored Item) returns Self
		return Self{stored: stored}
	end 'create'

	function fresh() returns Item
		return self.stored
	end 'fresh'

	function ifAssignOwned(fallback Item, flag bool) returns Item
		var chosen = fallback
		if flag 'taken'
			chosen = self.fresh()
		end 'taken'
		return chosen
	end 'ifAssignOwned'
end 'Box'

typealias TextBox = Box with String
typealias PebbleBox = Box with Pebble
typealias TallyBox = Box with Count

function main() returns ExitCode
	let texts = TextBox.create("stored {40 + 2} long enough for a heap record")
	print("{texts.ifAssignOwned("fallback {40 + 2} long enough for a heap record", flag: true)}\n")
	print("{texts.ifAssignOwned("fallback {40 + 2} long enough for a heap record", flag: false)}\n")

	let pebbles = PebbleBox.create(Pebble.create("stored {40 + 2} long enough for a heap record", n: 1))
	let taken = pebbles.ifAssignOwned(Pebble.create("fallback {40 + 2} long enough for a heap record", n: 2), flag: true)
	print("{taken.name} {taken.n}\n")
	let kept = pebbles.ifAssignOwned(Pebble.create("fallback {40 + 2} long enough for a heap record", n: 2), flag: false)
	print("{kept.name} {kept.n}\n")

	let tallies = TallyBox.create(10)
	print("{tallies.ifAssignOwned(20, flag: true)}\n")
	print("{tallies.ifAssignOwned(20, flag: false)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
stored 42 long enough for a heap record
fallback 42 long enough for a heap record
stored 42 long enough for a heap record 1
fallback 42 long enough for a heap record 2
10
20
```

### A `var` born owned and rebound from a borrowed parameter

<!-- test: if-rebind-owned-from-borrowed-parameter -->
The reverse order: `var chosen = self.fresh()` is born owned and one branch of an `if` rebinds it to a borrowed parameter.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Box uses Item
	var stored as Item

	static function create(stored Item) returns Self
		return Self{stored: stored}
	end 'create'

	function fresh() returns Item
		return self.stored
	end 'fresh'

	function bornOwnedThenIf(fallback Item, flag bool) returns Item
		var chosen = self.fresh()
		if flag 'taken'
			chosen = fallback
		end 'taken'
		return chosen
	end 'bornOwnedThenIf'
end 'Box'

typealias TextBox = Box with String
typealias PebbleBox = Box with Pebble
typealias TallyBox = Box with Count

function main() returns ExitCode
	let texts = TextBox.create("stored {40 + 2} long enough for a heap record")
	print("{texts.bornOwnedThenIf("fallback {40 + 2} long enough for a heap record", flag: true)}\n")
	print("{texts.bornOwnedThenIf("fallback {40 + 2} long enough for a heap record", flag: false)}\n")

	let pebbles = PebbleBox.create(Pebble.create("stored {40 + 2} long enough for a heap record", n: 1))
	let taken = pebbles.bornOwnedThenIf(Pebble.create("fallback {40 + 2} long enough for a heap record", n: 2), flag: true)
	print("{taken.name} {taken.n}\n")
	let kept = pebbles.bornOwnedThenIf(Pebble.create("fallback {40 + 2} long enough for a heap record", n: 2), flag: false)
	print("{kept.name} {kept.n}\n")

	let tallies = TallyBox.create(10)
	print("{tallies.bornOwnedThenIf(20, flag: true)}\n")
	print("{tallies.bornOwnedThenIf(20, flag: false)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fallback 42 long enough for a heap record
stored 42 long enough for a heap record
fallback 42 long enough for a heap record 2
stored 42 long enough for a heap record 1
20
10
```

### A consumed parameter bound to a `var`, pushed, and the `var` rebound

<!-- test: consumed-parameter-bound-then-rebound -->
`var kept = item`, `items.push(item)`, then `kept = other`: the array owns the pushed `item`, the `var` released its own reference when it was rebound, and `other` is what comes back. The scalar instantiation is the one that crashed in `__mm_retain`.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Bag uses Item
	typealias ItemArray = Array with Item
	var items as ItemArray

	static function create() returns Self
		return Self{items: ItemArray.create(0)}
	end 'create'

	function addThenSwap(item Item, other Item) returns Item
		var kept = item
		items.push(item)
		kept = other
		return kept
	end 'addThenSwap'

	function size() returns Count
		return items.count()
	end 'size'

	function first() returns Item
		return try items.get(0) otherwise panic("empty")
	end 'first'
end 'Bag'

typealias TextBag = Bag with String
typealias PebbleBag = Bag with Pebble
typealias TallyBag = Bag with Count

function main() returns ExitCode
	let texts = TextBag.create()
	let s = texts.addThenSwap("item {40 + 2} long enough for a heap record", other: "other {40 + 2} long enough for a heap record")
	print("{s} {texts.size()} {texts.first()}\n")

	let pebbles = PebbleBag.create()
	let p = pebbles.addThenSwap(Pebble.create("item {40 + 2} long enough for a heap record", n: 1), other: Pebble.create("other {40 + 2} long enough for a heap record", n: 2))
	let q = pebbles.first()
	print("{p.name} {p.n} {pebbles.size()} {q.name} {q.n}\n")

	let tallies = TallyBag.create()
	let c = tallies.addThenSwap(11, other: 22)
	print("{c} {tallies.size()} {tallies.first()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
other 42 long enough for a heap record 1 item 42 long enough for a heap record
other 42 long enough for a heap record 2 1 item 42 long enough for a heap record 1
22 1 11
```

### A kept constructor feed bound to a `var` and read

<!-- test: kept-feed-bound-to-a-var-and-read -->
`item` is stored into a field, so the constructor keeps it as a feed, and is also bound to `var copy`, which is conditionally rebound and then read into a second field. Built at both settings of the flag at three instantiations; exit 0 is the leak gate. No spec instantiates a user generic type at a promise, so that instantiation is not covered here.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Holder uses Item
	var held as Item
	var echoed as Item

	static function create(item Item, other Item, swap bool) returns Self
		var copy = item
		if swap 'maybeSwap'
			copy = other
		end 'maybeSwap'
		return Self{held: item, echoed: copy}
	end 'create'
end 'Holder'

typealias TextHolder = Holder with String
typealias PebbleHolder = Holder with Pebble
typealias TallyHolder = Holder with Count

function main() returns ExitCode
	let keptText = TextHolder.create("item {40 + 2} long enough for a heap record", other: "other {40 + 2} long enough for a heap record", swap: false)
	print("{keptText.held} {keptText.echoed}\n")
	let swappedText = TextHolder.create("item {40 + 2} long enough for a heap record", other: "other {40 + 2} long enough for a heap record", swap: true)
	print("{swappedText.held} {swappedText.echoed}\n")

	let keptPebble = PebbleHolder.create(Pebble.create("item {40 + 2} long enough for a heap record", n: 1), other: Pebble.create("other {40 + 2} long enough for a heap record", n: 2), swap: false)
	print("{keptPebble.held.name} {keptPebble.held.n} {keptPebble.echoed.name} {keptPebble.echoed.n}\n")
	let swappedPebble = PebbleHolder.create(Pebble.create("item {40 + 2} long enough for a heap record", n: 1), other: Pebble.create("other {40 + 2} long enough for a heap record", n: 2), swap: true)
	print("{swappedPebble.held.name} {swappedPebble.held.n} {swappedPebble.echoed.name} {swappedPebble.echoed.n}\n")

	let keptTally = TallyHolder.create(11, other: 22, swap: false)
	print("{keptTally.held} {keptTally.echoed}\n")
	let swappedTally = TallyHolder.create(11, other: 22, swap: true)
	print("{swappedTally.held} {swappedTally.echoed}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
item 42 long enough for a heap record item 42 long enough for a heap record
item 42 long enough for a heap record other 42 long enough for a heap record
item 42 long enough for a heap record 1 item 42 long enough for a heap record 1
item 42 long enough for a heap record 1 other 42 long enough for a heap record 2
11 11
11 22
```

### A kept promise feed stored twice is moved

<!-- test: error.kept-promise-feed-stored-twice-is-moved -->
Binding `item` to `var copy` moves the promise, so storing `item` into a field as well reads it after the move, and the green thread's single owner rule refuses it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

type Holder uses Item
	var held as Item
	var echoed as Item

	static function create(item Item, other Item, swap bool) returns Self
		var copy = item
		if swap 'maybeSwap'
			copy = other
		end 'maybeSwap'
		return Self{held: item, echoed: copy}
	end 'create'
end 'Holder'

typealias PromiseHolder = Holder with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	let h = PromiseHolder.create(async plain(7), other: async plain(9), swap: false)
	print("{h.held.isComplete() or true}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:21: use of moved value 'item': its ownership moved to another binding at an earlier bind or assignment
```

### A match arm gives a field of a record the arm drops

<!-- test: match-arm-gives-a-field-of-a-dying-record -->
One arm of a `match` expression gives the opaque field of a record built inside the arm, so the record is dropped at the arm's end and the merge has to take its own reference to what the field held, as it does for a `String` or struct field. Called through both arms at three instantiations; exit 0 is the leak gate.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Box uses Item
	var stored as Item

	static function create(stored Item) returns Self
		return Self{stored: stored}
	end 'create'

	function pick(v Item, which Count) returns Item
		let chosen = match which 'arm'
			0 gives Self.create(v).stored
			default gives self.stored
		end 'arm'
		return chosen
	end 'pick'
end 'Box'

typealias TextBox = Box with String
typealias PebbleBox = Box with Pebble
typealias TallyBox = Box with Count

function main() returns ExitCode
	let texts = TextBox.create("held {40 + 2} long enough for a heap record")
	print("{texts.pick("picked {40 + 2} long enough for a heap record", which: 0)}\n")
	print("{texts.pick("unused {40 + 2} long enough for a heap record", which: 1)}\n")

	let pebbles = PebbleBox.create(Pebble.create("held {40 + 2} long enough for a heap record", n: 1))
	let first = pebbles.pick(Pebble.create("picked {40 + 2} long enough for a heap record", n: 2), which: 0)
	let second = pebbles.pick(Pebble.create("unused {40 + 2} long enough for a heap record", n: 3), which: 1)
	print("{first.name} {first.n}\n")
	print("{second.name} {second.n}\n")

	let tallies = TallyBox.create(5)
	print("{tallies.pick(7, which: 0)}\n")
	print("{tallies.pick(8, which: 1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
picked 42 long enough for a heap record
held 42 long enough for a heap record
picked 42 long enough for a heap record 2
held 42 long enough for a heap record 1
7
5
```

### A match arm gives a field of a dying instance of another generic type

<!-- test: match-arm-gives-a-field-of-a-dying-generic-instance -->
One arm of a `match` expression gives the field of a temporary `Cell with Item`, another generic type instantiated at the enclosing type's own parameter, so the temporary is dropped at the arm's end and the merge has to take its own reference to what the field held. Both arms are taken at three instantiations; exit 0 is the leak gate.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Cell uses T
	var value as T

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'
end 'Cell'

type Box uses Item
	typealias ItemCell = Cell with Item
	var stored as Item

	static function create(stored Item) returns Self
		return Self{stored: stored}
	end 'create'

	function pick(v Item, which Count) returns Item
		let chosen = match which 'arm'
			0 gives ItemCell.create(v).value
			default gives self.stored
		end 'arm'
		return chosen
	end 'pick'
end 'Box'

typealias TextBox = Box with String
typealias PebbleBox = Box with Pebble
typealias TallyBox = Box with Count

function main() returns ExitCode
	let texts = TextBox.create("held {40 + 2} long enough for a heap record")
	print("{texts.pick("picked {40 + 2} long enough for a heap record", which: 0)}\n")
	print("{texts.pick("unused {40 + 2} long enough for a heap record", which: 1)}\n")

	let pebbles = PebbleBox.create(Pebble.create("held {40 + 2} long enough for a heap record", n: 1))
	let first = pebbles.pick(Pebble.create("picked {40 + 2} long enough for a heap record", n: 2), which: 0)
	let second = pebbles.pick(Pebble.create("unused {40 + 2} long enough for a heap record", n: 3), which: 1)
	print("{first.name} {first.n}\n")
	print("{second.name} {second.n}\n")

	let tallies = TallyBox.create(5)
	print("{tallies.pick(7, which: 0)}\n")
	print("{tallies.pick(8, which: 1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
picked 42 long enough for a heap record
held 42 long enough for a heap record
picked 42 long enough for a heap record 2
held 42 long enough for a heap record 1
7
5
```

### A ternary arm gives a field of a record the arm drops

<!-- test: ternary-arm-gives-a-field-of-a-dying-record -->
The same program with the `match` spelled as a ternary: one branch gives the opaque field of a record built inside the branch, so the record is dropped at the branch's end and the merge has to take its own reference to what the field held. Both branches run at three instantiations; exit 0 is the leak gate.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Box uses Item
	var stored as Item

	static function create(stored Item) returns Self
		return Self{stored: stored}
	end 'create'

	function pick(v Item, which Count) returns Item
		let chosen = Self.create(v).stored if which == 0 else self.stored
		return chosen
	end 'pick'
end 'Box'

typealias TextBox = Box with String
typealias PebbleBox = Box with Pebble
typealias TallyBox = Box with Count

function main() returns ExitCode
	let texts = TextBox.create("held {40 + 2} long enough for a heap record")
	print("{texts.pick("picked {40 + 2} long enough for a heap record", which: 0)}\n")
	print("{texts.pick("unused {40 + 2} long enough for a heap record", which: 1)}\n")

	let pebbles = PebbleBox.create(Pebble.create("held {40 + 2} long enough for a heap record", n: 1))
	let first = pebbles.pick(Pebble.create("picked {40 + 2} long enough for a heap record", n: 2), which: 0)
	let second = pebbles.pick(Pebble.create("unused {40 + 2} long enough for a heap record", n: 3), which: 1)
	print("{first.name} {first.n}\n")
	print("{second.name} {second.n}\n")

	let tallies = TallyBox.create(5)
	print("{tallies.pick(7, which: 0)}\n")
	print("{tallies.pick(8, which: 1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
picked 42 long enough for a heap record
held 42 long enough for a heap record
picked 42 long enough for a heap record 2
held 42 long enough for a heap record 1
7
5
```

### A match arm gives a borrowed value beside an owned sibling arm

<!-- test: match-arm-gives-beside-an-owned-sibling-arm -->
One arm of a `match` expression gives a borrowed parameter and its sibling gives an owned call result, so the merge must leave the value owned on both edges. Each arm is taken at three instantiations; exit 0 is the leak gate.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Box uses Item
	var stored as Item

	static function create(stored Item) returns Self
		return Self{stored: stored}
	end 'create'

	function fresh() returns Item
		return self.stored
	end 'fresh'

	function pick(v Item, which Count) returns Item
		let chosen = match which 'arm'
			0 gives v
			default gives self.fresh()
		end 'arm'
		return chosen
	end 'pick'
end 'Box'

typealias TextBox = Box with String
typealias PebbleBox = Box with Pebble
typealias TallyBox = Box with Count

function main() returns ExitCode
	let texts = TextBox.create("held {40 + 2} long enough for a heap record")
	print("{texts.pick("picked {40 + 2} long enough for a heap record", which: 0)}\n")
	print("{texts.pick("unused {40 + 2} long enough for a heap record", which: 1)}\n")

	let pebbles = PebbleBox.create(Pebble.create("held {40 + 2} long enough for a heap record", n: 1))
	let first = pebbles.pick(Pebble.create("picked {40 + 2} long enough for a heap record", n: 2), which: 0)
	let second = pebbles.pick(Pebble.create("unused {40 + 2} long enough for a heap record", n: 3), which: 1)
	print("{first.name} {first.n}\n")
	print("{second.name} {second.n}\n")

	let tallies = TallyBox.create(5)
	print("{tallies.pick(7, which: 0)}\n")
	print("{tallies.pick(8, which: 1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
picked 42 long enough for a heap record
held 42 long enough for a heap record
picked 42 long enough for a heap record 2
held 42 long enough for a heap record 1
7
5
```

### A match whose arms all give borrowed values

<!-- test: match-arm-gives-beside-only-borrowed-sibling-arms -->
Every arm of a `match` expression gives a borrowed value, a parameter or a field of `self`, so the merge owns nothing it must release. Each arm is taken at three instantiations; exit 0 is the leak gate.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Box uses Item
	var stored as Item

	static function create(stored Item) returns Self
		return Self{stored: stored}
	end 'create'

	function pick(v Item, w Item, which Count) returns Item
		let chosen = match which 'arm'
			0 gives v
			1 gives self.stored
			default gives w
		end 'arm'
		return chosen
	end 'pick'
end 'Box'

typealias TextBox = Box with String
typealias PebbleBox = Box with Pebble
typealias TallyBox = Box with Count

function main() returns ExitCode
	let texts = TextBox.create("held {40 + 2} long enough for a heap record")
	print("{texts.pick("first {40 + 2} long enough for a heap record", w: "second {40 + 2} long enough for a heap record", which: 0)}\n")
	print("{texts.pick("first {40 + 2} long enough for a heap record", w: "second {40 + 2} long enough for a heap record", which: 1)}\n")
	print("{texts.pick("first {40 + 2} long enough for a heap record", w: "second {40 + 2} long enough for a heap record", which: 2)}\n")

	let pebbles = PebbleBox.create(Pebble.create("held {40 + 2} long enough for a heap record", n: 1))
	let a = pebbles.pick(Pebble.create("first {40 + 2} long enough for a heap record", n: 2), w: Pebble.create("second {40 + 2} long enough for a heap record", n: 3), which: 0)
	let b = pebbles.pick(Pebble.create("first {40 + 2} long enough for a heap record", n: 2), w: Pebble.create("second {40 + 2} long enough for a heap record", n: 3), which: 1)
	let c = pebbles.pick(Pebble.create("first {40 + 2} long enough for a heap record", n: 2), w: Pebble.create("second {40 + 2} long enough for a heap record", n: 3), which: 2)
	print("{a.name} {a.n}\n")
	print("{b.name} {b.n}\n")
	print("{c.name} {c.n}\n")

	let tallies = TallyBox.create(5)
	print("{tallies.pick(7, w: 8, which: 0)}\n")
	print("{tallies.pick(7, w: 8, which: 1)}\n")
	print("{tallies.pick(7, w: 8, which: 2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first 42 long enough for a heap record
held 42 long enough for a heap record
second 42 long enough for a heap record
first 42 long enough for a heap record 2
held 42 long enough for a heap record 1
second 42 long enough for a heap record 3
7
5
8
```

### A scalar instantiation does no allocation work in a match arm

<!-- test: scalar-match-arm-allocates-nothing -->
The arms of the `match` give only a parameter and a field of `self`, so no record is built, and a scalar `Item` has a zero `retainFunc`: `Box with Count` runs the merge with the allocation count unmoved. The first call is a warm-up outside the bracket.
```maxon
type Box uses Item
	var stored as Item

	static function create(stored Item) returns Self
		return Self{stored: stored}
	end 'create'

	function pick(v Item, which Count) returns Item
		let chosen = match which 'arm'
			0 gives v
			default gives self.stored
		end 'arm'
		return chosen
	end 'pick'
end 'Box'

typealias TallyBox = Box with Count

function main() returns ExitCode
	let tallies = TallyBox.create(5)
	var total = tallies.pick(0, which: 0)
	let before = HeapStats.allocationsMade()
	for round in 0 upto 100 'eachRound'
		total = total + tallies.pick(round, which: round mod 2)
	end 'eachRound'
	let after = HeapStats.allocationsMade()
	let delta = after - before
	print("{total} {delta}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2700 0
```

### A scalar merge beside an owned arm allocates nothing

<!-- test: scalar-merge-beside-an-owned-arm-allocates-nothing -->
One arm gives a borrowed parameter and its sibling an owned `self.fresh()` result, so the merge promotes the borrowed arm; at `Box with Count` the `retainFunc` is zero and the merge runs with the allocation count unmoved. The first call is a warm-up outside the bracket.
```maxon
type Box uses Item
	var stored as Item

	static function create(stored Item) returns Self
		return Self{stored: stored}
	end 'create'

	function fresh() returns Item
		return self.stored
	end 'fresh'

	function pick(v Item, which Count) returns Item
		let chosen = match which 'arm'
			0 gives v
			default gives self.fresh()
		end 'arm'
		return chosen
	end 'pick'
end 'Box'

typealias TallyBox = Box with Count

function main() returns ExitCode
	let tallies = TallyBox.create(5)
	var total = tallies.pick(0, which: 0)
	let before = HeapStats.allocationsMade()
	for round in 0 upto 100 'eachRound'
		total = total + tallies.pick(round, which: round mod 2)
	end 'eachRound'
	let after = HeapStats.allocationsMade()
	let delta = after - before
	print("{total} {delta}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2700 0
```

### A method that rebinds a `var` of `Item` but returns a `Count`

<!-- test: descriptor-carrier-method-returns-count -->
The return type names no `Item`, so nothing in the call's result carries the instance's layout descriptor; the body still binds and rebinds a `var` of `Item` and has to reach the descriptor another way. Called in a loop at three instantiations; exit 0 is the leak gate.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Slot uses Item
	var swaps = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	function rebind(first Item, second Item) returns Count
		var held = first
		held = second
		_ = held
		self.swaps = self.swaps + 1
		return self.swaps
	end 'rebind'
end 'Slot'

typealias TextSlot = Slot with String
typealias PebbleSlot = Slot with Pebble
typealias TallySlot = Slot with Count

function main() returns ExitCode
	let texts = TextSlot.create()
	var textSwaps = 0 as Count
	for round in 0 upto 4 'eachTextRound'
		textSwaps = texts.rebind("first {round} long enough for a heap record", second: "second {round} long enough for a heap record")
	end 'eachTextRound'
	print("{textSwaps}\n")

	let pebbles = PebbleSlot.create()
	var pebbleSwaps = 0 as Count
	for round in 0 upto 4 'eachPebbleRound'
		pebbleSwaps = pebbles.rebind(Pebble.create("first {round} long enough for a heap record", n: 1), second: Pebble.create("second {round} long enough for a heap record", n: 2))
	end 'eachPebbleRound'
	print("{pebbleSwaps}\n")

	let tallies = TallySlot.create()
	var tallySwaps = 0 as Count
	for round in 0 upto 4 'eachTallyRound'
		tallySwaps = tallies.rebind(round, second: round + 10)
	end 'eachTallyRound'
	print("{tallySwaps}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
4
4
```

### A scalar instantiation does no allocation work in a bind-and-rebind body

<!-- test: scalar-instantiation-retains-nothing -->
`HeapStats.allocationsMade()` is the allocator's cumulative count of tracked allocations. A scalar `Item` has a zero `retainFunc`, so the `var` binding and rebinding of `Slot with Count` run with the allocation count unmoved. The first call is a warm-up outside the bracket.
```maxon
type Slot uses Item
	var latest as Item
	var swaps = 0 as Count

	static function create(start Item) returns Self
		return Self{latest: start}
	end 'create'

	function rebind(first Item, second Item) returns Count
		var held = first
		held = second
		self.latest = held
		self.swaps = self.swaps + 1
		return self.swaps
	end 'rebind'
end 'Slot'

typealias TallySlot = Slot with Count

function main() returns ExitCode
	let tallies = TallySlot.create(0)
	_ = tallies.rebind(1, second: 2)
	let before = HeapStats.allocationsMade()
	var swaps = 0 as Count
	for round in 0 upto 100 'eachRound'
		swaps = tallies.rebind(round, second: round + 1)
	end 'eachRound'
	let after = HeapStats.allocationsMade()
	let delta = after - before
	print("{swaps} {delta}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
101 0
```

### A `while` loop carries an owned `var` through its header

<!-- test: while-loop-carries-an-owned-var -->
The `var` starts from a borrowed parameter and is rebound on alternate trips from an owned call result and from a borrowed parameter, then returned after the loop; exit 0 is the leak gate at three instantiations.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type Chooser uses Item
	var spare as Item

	static function create(spare Item) returns Self
		return Self{spare: spare}
	end 'create'

	function fresh() returns Item
		return self.spare
	end 'fresh'

	function choose(first Item, other Item, rounds Count) returns Item
		var chosen = first
		var trip = 0 as Count
		while trip < rounds 'trips'
			if trip mod 2 == 0 'even'
				chosen = self.fresh()
			end 'even' else 'odd'
				chosen = other
			end 'odd'
			trip = trip + 1
		end 'trips'
		return chosen
	end 'choose'
end 'Chooser'

typealias TextChooser = Chooser with String
typealias PebbleChooser = Chooser with Pebble
typealias TallyChooser = Chooser with Count

function main() returns ExitCode
	let texts = TextChooser.create("spare text long enough for a heap record")
	print("{texts.choose("first text long enough for a heap record", other: "other text long enough for a heap record", rounds: 3)}\n")
	print("{texts.choose("first text long enough for a heap record", other: "other text long enough for a heap record", rounds: 4)}\n")

	let pebbles = PebbleChooser.create(Pebble.create("spare pebble long enough for a heap record", n: 7))
	let threeTrips = pebbles.choose(Pebble.create("first pebble long enough for a heap record", n: 1), other: Pebble.create("other pebble long enough for a heap record", n: 2), rounds: 3)
	print("{threeTrips.name} {threeTrips.n}\n")
	let fourTrips = pebbles.choose(Pebble.create("first pebble long enough for a heap record", n: 1), other: Pebble.create("other pebble long enough for a heap record", n: 2), rounds: 4)
	print("{fourTrips.name} {fourTrips.n}\n")

	let tallies = TallyChooser.create(7)
	print("{tallies.choose(1, other: 2, rounds: 3)}\n")
	print("{tallies.choose(1, other: 2, rounds: 4)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
spare text long enough for a heap record
other text long enough for a heap record
spare pebble long enough for a heap record 7
other pebble long enough for a heap record 2
7
2
```

### A `for` loop over an iterable that is empty at run time carries an owned `var`

<!-- test: empty-iterable-loop-carries-an-owned-var -->
The `var` starts from a borrowed parameter and is rebound from an owned call result inside a `for` loop over a declared `Iterable` whose iterator is refused on one call (no trips) and yields twice on another, then returned after the loop; exit 0 is the leak gate at three instantiations.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

type TickCursor
	var left as Count

	static function create(n Count) returns Self throws IterationError
		if n == 0 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{left: n}
	end 'create'

	function current() returns Count
		return self.left
	end 'current'

	function advance() throws IterationError
		if self.left <= 1 'atEnd'
			throw IterationError.exhausted
		end 'atEnd'
		self.left = self.left - 1
	end 'advance'
end 'TickCursor'

type Ticker
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function createIterator() returns TickCursor throws IterationError
		return try TickCursor.create(self.n)
	end 'createIterator'
end 'Ticker'

type Box uses Item
	var stored as Item

	static function create(stored Item) returns Self
		return Self{stored: stored}
	end 'create'

	function fresh() returns Item
		return self.stored
	end 'fresh'

	function pick(fallback Item, n Count) returns Item
		var chosen = fallback
		for _ in Ticker.create(n) 'trip'
			chosen = self.fresh()
		end 'trip'
		return chosen
	end 'pick'
end 'Box'

typealias TextBox = Box with String
typealias PebbleBox = Box with Pebble
typealias TallyBox = Box with Count

function main() returns ExitCode
	let texts = TextBox.create("stored text long enough for a heap record")
	print("{texts.pick("fallback {40 + 2} long enough for a heap record", n: 0)}\n")
	print("{texts.pick("fallback {40 + 2} long enough for a heap record", n: 2)}\n")

	let pebbles = PebbleBox.create(Pebble.create("stored pebble long enough for a heap record", n: 7))
	let none = pebbles.pick(Pebble.create("fallback pebble long enough for a heap record", n: 1), n: 0)
	print("{none.name} {none.n}\n")
	let some = pebbles.pick(Pebble.create("fallback pebble long enough for a heap record", n: 1), n: 2)
	print("{some.name} {some.n}\n")

	let tallies = TallyBox.create(7)
	print("{tallies.pick(1, n: 0)}\n")
	print("{tallies.pick(1, n: 2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fallback 42 long enough for a heap record
stored text long enough for a heap record
fallback pebble long enough for a heap record 1
stored pebble long enough for a heap record 7
1
7
```

### A block-form `try` carries an owned `var` along its error edge

<!-- test: try-error-edge-carries-an-owned-var -->
The `var` is rebound inside the `try` block and the second call refuses on one path, so that path leaves through the error edge with the first rebinding while the other reaches the second; the handler and the fall-through both return the `var`, at three instantiations.
```maxon
type Pebble
	export let name as String
	export let n as Count

	static function create(name String, n Count) returns Pebble
		return Pebble{name: name, n: n}
	end 'create'
end 'Pebble'

enum StepError implements Error
	refused
end 'StepError'

function check(step Count) throws StepError
	if step == 2 'refuse'
		throw StepError.refused
	end 'refuse'
end 'check'

type Chooser uses Item
	var spare as Item

	static function create(spare Item) returns Self
		return Self{spare: spare}
	end 'create'

	function fresh() returns Item
		return self.spare
	end 'fresh'

	function attempt(first Item, other Item, step Count) returns Item
		var chosen = first
		try 'work'
			chosen = self.fresh()
			check(step)
			chosen = other
		end 'work'
		otherwise (e) 'handler'
			match e 'kind'
				refused then print("refused\n")
			end 'kind'
		end 'handler'
		return chosen
	end 'attempt'
end 'Chooser'

typealias TextChooser = Chooser with String
typealias PebbleChooser = Chooser with Pebble
typealias TallyChooser = Chooser with Count

function main() returns ExitCode
	let texts = TextChooser.create("spare text long enough for a heap record")
	print("{texts.attempt("first text long enough for a heap record", other: "other text long enough for a heap record", step: 1)}\n")
	print("{texts.attempt("first text long enough for a heap record", other: "other text long enough for a heap record", step: 2)}\n")

	let pebbles = PebbleChooser.create(Pebble.create("spare pebble long enough for a heap record", n: 7))
	let passed = pebbles.attempt(Pebble.create("first pebble long enough for a heap record", n: 1), other: Pebble.create("other pebble long enough for a heap record", n: 2), step: 1)
	print("{passed.name} {passed.n}\n")
	let refused = pebbles.attempt(Pebble.create("first pebble long enough for a heap record", n: 1), other: Pebble.create("other pebble long enough for a heap record", n: 2), step: 2)
	print("{refused.name} {refused.n}\n")

	let tallies = TallyChooser.create(7)
	print("{tallies.attempt(1, other: 2, step: 1)}\n")
	print("{tallies.attempt(1, other: 2, step: 2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
other text long enough for a heap record
refused
spare text long enough for a heap record
other pebble long enough for a heap record 2
refused
spare pebble long enough for a heap record 7
2
refused
7
```

### A `var` bound from an owned `var` is moved whatever the instantiation

The body is checked once for an unbounded `Item` that may own, so reading `a` after `var b = a` is E3102 even when the only instantiation is a scalar.

<!-- test: error.a-var-bound-from-an-owned-var-is-moved -->
```maxon
type Holder uses Item
	var latest as Item

	static function create(start Item) returns Self
		return Self{latest: start}
	end 'create'

	function rebind(first Item) returns Item
		var a = first
		var b = a
		self.latest = b
		return a
	end 'rebind'
end 'Holder'

typealias CountHolder = Holder with Count

function main() returns ExitCode
	let holder = CountHolder.create(1)
	_ = holder.rebind(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:10: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

### A `let` bound from an owned `var` is moved whatever the instantiation

Reading `a` after `let b = a` is E3102 in the body even when the only instantiation is a scalar.

<!-- test: error.a-let-bound-from-an-owned-var-is-moved -->
```maxon
type Holder uses Item
	var latest as Item

	static function create(start Item) returns Self
		return Self{latest: start}
	end 'create'

	function rebind(first Item) returns Item
		var a = first
		let b = a
		self.latest = b
		return a
	end 'rebind'
end 'Holder'

typealias CountHolder = Holder with Count

function main() returns ExitCode
	let holder = CountHolder.create(1)
	_ = holder.rebind(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:10: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

### An owned `var` assigned to another `var` is moved whatever the instantiation

Reading `a` after `b = a` is E3102 in the body even when the only instantiation is a scalar.

<!-- test: error.an-owned-var-assigned-to-another-var-is-moved -->
```maxon
type Holder uses Item
	var latest as Item

	static function create(start Item) returns Self
		return Self{latest: start}
	end 'create'

	function rebind(first Item, second Item) returns Item
		var a = first
		var b = second
		b = a
		self.latest = b
		return a
	end 'rebind'
end 'Holder'

typealias CountHolder = Holder with Count

function main() returns ExitCode
	let holder = CountHolder.create(1)
	_ = holder.rebind(2, second: 3)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:10: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```
