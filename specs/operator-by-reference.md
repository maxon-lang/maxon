---
feature: operator-by-reference
keywords: [operator, comparison, equals, compare, witness, byref, param, reassign, conformer, equatable, comparable]
category: type-system
---

# The Methods Behind the Comparison Operators Do Not Write Their Operand

## Documentation

`a == b` and `a != b` dispatch to `equals`, and `a < b`, `a <= b`, `a > b` and `a >= b` to `compare`. A comparison
never changes what it compares, so a method named `equals` or `compare` that takes one operand besides its
receiver may not write that operand or its receiver, or keep it where something may write it, directly or by
handing it to a function that does: that is refused at the method's declaration (`E3187`), whichever type declares it, whichever interface it satisfies and
whether or not it satisfies one. Operator operands are always read, so a `let`, a `var`, a field or an expression is an equally
good operand.

Any other requirement keeps the direct-call rule, and a position some conformer writes is passed by reference
(`witness-by-reference.md`).

## Tests

<!-- test: operator-by-reference.error.equals-may-not-assign-its-parameter -->
```maxon
typealias Count = int(i64.min to i64.max)

type Tagged implements Equatable
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function equals(other Self) returns bool
		let same = self.tag == other.tag
		other = Tagged.create(0)
		return same
	end 'equals'
end 'Tagged'

function main() returns ExitCode
	let a = Tagged.create(2)
	let b = Tagged.create(2)
	print("{a == b}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:11:11: 'Tagged.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.compare-may-not-assign-its-parameter -->
```maxon
typealias Count = int(i64.min to i64.max)

type Ranked implements Comparable
	export var rank as Count

	static function create(rank Count) returns Self
		return Self{rank: rank}
	end 'create'

	function compare(other Self) returns Ordering
		let verdict = Ordering.lessThan if self.rank < other.rank else Ordering.equalTo
		other = Ranked.create(0)
		return verdict
	end 'compare'
end 'Ranked'

function main() returns ExitCode
	let a = Ranked.create(1)
	let b = Ranked.create(2)
	print("{a < b}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:11:11: 'Ranked.compare' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-write-through-a-helper-is-refused -->
```maxon
typealias Count = int(i64.min to i64.max)

type Tagged implements Equatable
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function equals(other Self) returns bool
		let same = self.tag == other.tag
		clear(other)
		return same
	end 'equals'
end 'Tagged'

function clear(target Tagged)
	target = Tagged.create(0)
end 'clear'

function main() returns ExitCode
	let a = Tagged.create(2)
	let b = Tagged.create(2)
	print("{a == b}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:11:11: 'Tagged.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.an-enum-conformer-may-not-write-its-operand -->
```maxon
enum Shade implements Equatable
	light
	dark

	function equals(other Self) returns bool
		let same = self == other
		other = Shade.light
		return same
	end 'equals'
end 'Shade'

function main() returns ExitCode
	print("{Shade.light == Shade.dark}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:6:11: 'Shade.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-conformer-through-an-extending-interface-may-not-write-its-operand -->
```maxon
typealias Count = int(i64.min to i64.max)

interface Keyed extends Equatable
	function key() returns Count
end 'Keyed'

type Tagged implements Keyed
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function key() returns Count
		return self.tag
	end 'key'

	function equals(other Self) returns bool
		let same = self.tag == other.tag
		other = Tagged.create(0)
		return same
	end 'equals'
end 'Tagged'

function main() returns ExitCode
	let a = Tagged.create(2)
	print("{a.key()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:19:11: 'Tagged.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-method-named-equals-is-judged-with-or-without-a-conformance -->
```maxon
typealias Count = int(i64.min to i64.max)

type Plain
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function equals(other Self) returns bool
		other = Plain.create(0)
		return true
	end 'equals'
end 'Plain'

function main() returns ExitCode
	let a = Plain.create(2)
	let b = Plain.create(2)
	print("{a == b}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:11:11: 'Plain.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.a-method-named-equals-without-a-conformance-is-what-the-operator-calls -->
`==` on two values of a type that declares `equals` calls that method whether or not the type implements `Equatable`, which is why the method is the operator's to judge.
```maxon
typealias Count = int(i64.min to i64.max)

type Plain
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function equals(other Self) returns bool
		print("equals called\n")
		return self.tag == other.tag
	end 'equals'
end 'Plain'

function main() returns ExitCode
	let a = Plain.create(2)
	let b = Plain.create(2)
	print("{a == b}\n")
	return 0
end 'main'
```
```stdout
equals called
true
```
```exitcode
0
```

<!-- test: operator-by-reference.a-reading-equals-takes-every-kind-of-operand -->
```maxon
typealias Count = int(i64.min to i64.max)

type Tagged implements Equatable
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function equals(other Self) returns bool
		return self.tag == other.tag
	end 'equals'
end 'Tagged'

type Slot
	export var held as Tagged

	static function create(held Tagged) returns Self
		return Self{held: held}
	end 'create'
end 'Slot'

function main() returns ExitCode
	let a = Tagged.create(2)
	let fixed = Tagged.create(2)
	var moving = Tagged.create(2)
	let slot = Slot.create(Tagged.create(3))
	print("{a == fixed} {a == moving} {a == Tagged.create(2)} {a != slot.held}\n")
	moving = Tagged.create(5)
	print("{a == moving} {moving.tag}\n")
	return 0
end 'main'
```
```stdout
true true true true
false 5
```
```exitcode
0
```

<!-- test: operator-by-reference.a-reading-compare-takes-every-kind-of-operand -->
```maxon
typealias Count = int(i64.min to i64.max)

type Ranked implements Comparable
	export var rank as Count

	static function create(rank Count) returns Self
		return Self{rank: rank}
	end 'create'

	function compare(other Self) returns Ordering
		if self.rank < other.rank 'lower'
			return Ordering.lessThan
		end 'lower'

		if self.rank > other.rank 'higher'
			return Ordering.greaterThan
		end 'higher'

		return Ordering.equalTo
	end 'compare'
end 'Ranked'

function main() returns ExitCode
	let a = Ranked.create(1)
	var b = Ranked.create(2)
	print("{a < b} {a >= b} {a <= Ranked.create(1)} {b > a}\n")
	b = Ranked.create(0)
	print("{a < b} {a >= b}\n")
	return 0
end 'main'
```
```stdout
true false true true
false true
```
```exitcode
0
```

<!-- test: operator-by-reference.a-reading-equals-is-dispatched-through-a-witness-with-any-operand -->
```maxon
typealias Count = int(i64.min to i64.max)

type Tagged implements Equatable
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function equals(other Self) returns bool
		return self.tag == other.tag
	end 'equals'
end 'Tagged'

type Probe uses T where T is Equatable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function pick() returns T
		return self.item
	end 'pick'

	function matches(candidate T) returns bool
		let copy = self.item
		return self.item == candidate and self.item == copy and self.item == self.pick()
	end 'matches'
end 'Probe'

typealias TaggedProbe = Probe with Tagged
typealias CountProbe = Probe with Count

function main() returns ExitCode
	let tagged = TaggedProbe.create(Tagged.create(2))
	let counted = CountProbe.create(5)
	let fixed = Tagged.create(2)
	var moving = Tagged.create(2)
	print("{tagged.matches(fixed)} {tagged.matches(moving)} {counted.matches(5)}\n")
	return 0
end 'main'
```
```stdout
true true true
```
```exitcode
0
```

<!-- test: operator-by-reference.a-method-with-more-parameters-is-not-behind-an-operator -->
```maxon
typealias Count = int(i64.min to i64.max)

type Tagged
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function equals(other Self, tolerance Count) returns bool
		tolerance = tolerance + 1
		return self.tag == other.tag
	end 'equals'
end 'Tagged'

function main() returns ExitCode
	let a = Tagged.create(2)
	let b = Tagged.create(2)
	var slack = 1 as Count
	print("{a.equals(b, tolerance: slack)} {slack}\n")
	return 0
end 'main'
```
```stdout
true 2
```
```exitcode
0
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-write-a-field-of-its-operand -->
Writing a field of the operand modifies it as surely as reassigning it.
```maxon
typealias Count = int(i64.min to i64.max)

type Tagged
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function equals(other Self) returns bool
		other.tag = 0
		return true
	end 'equals'
end 'Tagged'

function main() returns ExitCode
	let a = Tagged.create(2)
	print("{a.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:11:11: 'Tagged.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-grow-a-container-its-operand-holds -->
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

type Bag
	export var items as Counts

	static function create() returns Self
		return Self{items: Counts.create()}
	end 'create'

	function equals(other Self) returns bool
		other.items.push(1)
		return true
	end 'equals'
end 'Bag'

function main() returns ExitCode
	let a = Bag.create()
	print("{a.items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:12:11: 'Bag.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-hand-its-operand-to-a-writer -->
The write is found through the call graph: `equals` hands its operand to a method that writes a field of it.
```maxon
typealias Count = int(0 to 1000)

type Tagged
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function clear(other Self)
		other.tag = 0
	end 'clear'

	function equals(other Self) returns bool
		self.clear(other)
		return true
	end 'equals'
end 'Tagged'

function main() returns ExitCode
	let a = Tagged.create(2)
	print("{a.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:15:11: 'Tagged.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-write-a-field-of-its-receiver -->
```maxon
typealias Count = int(0 to 1000)

type Tagged
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function equals(other Self) returns bool
		self.tag = 0
		return true
	end 'equals'
end 'Tagged'

function main() returns ExitCode
	let a = Tagged.create(2)
	print("{a.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:11:11: 'Tagged.equals' writes its receiver, but a method behind the comparison operators may not modify its receiver — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-grow-a-container-its-receiver-holds -->
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

type Bag
	export var items as Counts

	static function create() returns Self
		return Self{items: Counts.create()}
	end 'create'

	function equals(other Self) returns bool
		self.items.push(1)
		return true
	end 'equals'
end 'Bag'

function main() returns ExitCode
	let a = Bag.create()
	print("{a.items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:12:11: 'Bag.equals' writes its receiver, but a method behind the comparison operators may not modify its receiver — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-call-a-helper-that-writes-its-receiver -->
```maxon
typealias Count = int(0 to 1000)

type Tagged
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function clear()
		tag = 0
	end 'clear'

	function equals(other Self) returns bool
		self.clear()
		return true
	end 'equals'
end 'Tagged'

function main() returns ExitCode
	let a = Tagged.create(2)
	print("{a.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:15:11: 'Tagged.equals' writes its receiver, but a method behind the comparison operators may not modify its receiver — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-reach-a-writing-requirement-through-its-receiver -->
`self.item.reset()` is a witness call whose conformer writes its own receiver, and that receiver lies within `self`.
```maxon
typealias Count = int(0 to 1000)

interface Resetter
	function reset()
end 'Resetter'

type Cell implements Resetter
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function reset()
		n = 0
	end 'reset'
end 'Cell'

type Holder uses T where T is Resetter
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function equals(other Self) returns bool
		self.item.reset()
		return true
	end 'equals'
end 'Holder'

typealias CellHolder = Holder with Cell

function main() returns ExitCode
	let a = CellHolder.create(Cell.create(2))
	print("{a.item.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:27:11: 'Holder.equals' writes its receiver, but a method behind the comparison operators may not modify its receiver — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-reach-a-writing-requirement-through-its-operand -->
`other.item.reset()` is a witness call whose conformer writes its own receiver, and that receiver lies within `other`.
```maxon
typealias Count = int(0 to 1000)

interface Resetter
	function reset()
end 'Resetter'

type Cell implements Resetter
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function reset()
		n = 0
	end 'reset'
end 'Cell'

type Holder uses T where T is Resetter
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function equals(other Self) returns bool
		other.item.reset()
		return true
	end 'equals'
end 'Holder'

typealias CellHolder = Holder with Cell

function main() returns ExitCode
	let a = CellHolder.create(Cell.create(2))
	print("{a.item.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:27:11: 'Holder.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-write-its-operand-through-a-closure -->
The closure captures `other` and the call inside it writes `other`'s record.
```maxon
typealias Count = int(0 to 1000)

type Tagged
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function clear() returns Count
		tag = 0
		return 0
	end 'clear'

	function equals(other Self) returns bool
		let f = function() gives other.clear()
		let r = f()
		return r == 0
	end 'equals'
end 'Tagged'

function main() returns ExitCode
	let a = Tagged.create(2)
	print("{a.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:16:11: 'Tagged.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-write-its-receiver-through-a-closure -->
```maxon
typealias Count = int(0 to 1000)

type Tagged
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function clear() returns Count
		tag = 0
		return 0
	end 'clear'

	function equals(other Self) returns bool
		let f = function() gives self.clear()
		let r = f()
		return r == other.tag
	end 'equals'
end 'Tagged'

function main() returns ExitCode
	let a = Tagged.create(2)
	print("{a.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:16:11: 'Tagged.equals' writes its receiver, but a method behind the comparison operators may not modify its receiver — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-write-its-operand-through-a-holder -->
The operand is stored into a record whose method writes it.

```maxon
typealias Count = int(0 to 1000)

type Tagged
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function clear() returns Count
		tag = 0
		return 0
	end 'clear'

	function equals(other Self) returns bool
		var holder = Holder.create(other)
		let r = holder.run()
		return r == 0
	end 'equals'
end 'Tagged'

type Holder
	export var held as Tagged

	static function create(held Tagged) returns Self
		return Self{held: held}
	end 'create'

	function run() returns Count
		return self.held.clear()
	end 'run'
end 'Holder'

function main() returns ExitCode
	let a = Tagged.create(2)
	print("{a.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:16:11: 'Tagged.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-write-its-receiver-through-a-holder -->
The receiver is stored into a record whose method writes it.

```maxon
typealias Count = int(0 to 1000)

type Tagged
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function clear() returns Count
		tag = 0
		return 0
	end 'clear'

	function equals(other Self) returns bool
		var holder = Holder.create(self)
		let r = holder.run()
		return r == other.tag
	end 'equals'
end 'Tagged'

type Holder
	export var held as Tagged

	static function create(held Tagged) returns Self
		return Self{held: held}
	end 'create'

	function run() returns Count
		return self.held.clear()
	end 'run'
end 'Holder'

function main() returns ExitCode
	let a = Tagged.create(2)
	print("{a.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:16:11: 'Tagged.equals' writes its receiver, but a method behind the comparison operators may not modify its receiver — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: operator-by-reference.error.a-comparison-method-may-not-write-its-operand-through-a-global -->
`equals` keeps its operand in a module container, where a helper then writes it, so the method writes its operand.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Reg
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'
end 'Reg'

type Outer
	export var inner as Reg

	static function create() returns Self
		return Self{inner: Reg.create()}
	end 'create'
end 'Outer'

type Top
	export var outer as Outer

	static function create() returns Self
		return Self{outer: Outer.create()}
	end 'create'
end 'Top'

function zeroBox(w Box)
	w.n = 0
end 'zeroBox'

typealias Tags = Array with Tagged

var seen = Tags.create()

type Tagged
	export var tag as Count

	static function create(tag Count) returns Self
		return Self{tag: tag}
	end 'create'

	function equals(other Self) returns bool
		seen.push(other)
		wipeSeen()
		return true
	end 'equals'
end 'Tagged'

function wipeSeen()
	var w = try seen.get(0) otherwise return
	w.tag = 0
end 'wipeSeen'

function main() returns ExitCode
	let a = Tagged.create(2)
	let b = Tagged.create(3)
	if a == b 'same'
		print("{a.tag}\n")
	end 'same'
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:52:11: 'Tagged.equals' writes its parameter 'other', but a method behind the comparison operators may not modify its operand — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```
