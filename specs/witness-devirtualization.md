---
feature: witness-devirtualization
status: experimental
keywords: [witness, interface, generics, devirtualization, inliner, dispatch]
category: codegen
---

# Witness Dispatch Resolved at a Constant Table

## Documentation

A generic body reaches its type parameter's interface through a witness table it receives as a hidden
parameter. When the body is spliced into a caller that passes a table known at compile time, each witness
call in the copy is a call of one known function, and the compiler calls that function directly and splices
it too when it is small. The answer is the same as the dispatch gave: the slot a call names is the slot of
the table the caller passed, so two conformers sharing one generic body keep their own methods.

A generic body that takes or gives up ownership of a `T` reaches it through the layout descriptor of `T`, a
hidden parameter as well. In a copy that receives a known descriptor, each action resolves from the
descriptor's retain and destroy words: retain calls the retain word, drop calls the destroy word, release
calls the destroy word when the retain word is set, and reclaim calls the destroy word when it is not; an
action whose word is empty does nothing. A release whose retain word is set and whose destroy word is empty
stays a call through the descriptor. Every descriptor the compiler emits with a retain word carries a destroy
word too, so only a hand-built descriptor meets that row.

The cases below run the same generic body against two conformers, each from more than one site, so no body is
spliced for being called once, and compare against the answer each conformer's own method gives. The
descriptor cases run one generic `Slot` over a trivial element, a `String`, a struct and a promise, so each
action meets each of its outcomes.

## Tests

<!-- test: witness-devirtualization.two-conformers-keep-their-own-method -->
`Box.doubled` reads `area` through the witness table of `T`. `SquareBox` and `TriangleBox` use the one body
with different tables, and two boxes of each kind are summed: `2 * 9 + 2 * 16` for the squares and
`2 * 15 + 2 * 7` for the triangles, 94. A call resolved to the wrong conformer's slot changes the sum.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	export var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

type Triangle implements Shape
	export var base as Integer
	export var height as Integer

	static function create(base Integer, height Integer) returns Self
		return Self{base: base, height: height}
	end 'create'

	function area() returns Integer
		return (self.base * self.height) shr 1
	end 'area'
end 'Triangle'

type Box uses T where T is Shape
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function doubled() returns Integer
		return self.item.area() * 2
	end 'doubled'
end 'Box'

typealias SquareBox = Box with Square
typealias TriangleBox = Box with Triangle

function main() returns ExitCode
	let firstSquare = SquareBox.create(Square.create(3))
	let secondSquare = SquareBox.create(Square.create(4))
	let firstTriangle = TriangleBox.create(Triangle.create(6, height: 5))
	let secondTriangle = TriangleBox.create(Triangle.create(2, height: 7))
	let total = firstSquare.doubled() + secondSquare.doubled() + firstTriangle.doubled() + secondTriangle.doubled()
	return total as ExitCode
end 'main'
```
```exitcode
94
```

<!-- test: witness-devirtualization.throwing-method-through-a-constant-table -->
The throwing form: `Guard.check` propagates the error of `item.verify()` with a bare `try`. `Even` verifies
and returns its value, `Odd` always throws, and a box of each is checked twice. The even boxes give `4` and
`6`; the odd boxes take the `otherwise` arm, 100 each: 4 + 6 + 100 + 100 = 210. A direct call that dropped the
error flag would return `Odd`'s primary value instead of taking the arm.
```maxon
typealias Code = int(0 to 1000)

enum VerifyError implements Error
	refused
end 'VerifyError'

interface Verifier
	function verify() returns Code throws VerifyError
end 'Verifier'

type Even implements Verifier
	export var value as Code

	static function create(value Code) returns Self
		return Self{value: value}
	end 'create'

	function verify() returns Code throws VerifyError
		return self.value
	end 'verify'
end 'Even'

type Odd implements Verifier
	export var value as Code

	static function create(value Code) returns Self
		return Self{value: value}
	end 'create'

	function verify() returns Code throws VerifyError
		if self.value > 0 'refuse'
			throw VerifyError.refused
		end 'refuse'

		return self.value
	end 'verify'
end 'Odd'

type Guard uses T where T is Verifier
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function check() returns Code throws VerifyError
		return try self.item.verify()
	end 'check'
end 'Guard'

typealias EvenGuard = Guard with Even
typealias OddGuard = Guard with Odd

function main() returns ExitCode
	let firstEven = EvenGuard.create(Even.create(4))
	let secondEven = EvenGuard.create(Even.create(6))
	let firstOdd = OddGuard.create(Odd.create(3))
	let secondOdd = OddGuard.create(Odd.create(5))
	let total = (try firstEven.check() otherwise 100) + (try secondEven.check() otherwise 100) + (try firstOdd.check() otherwise 100) + (try secondOdd.check() otherwise 100)
	return total as ExitCode
end 'main'
```
```exitcode
210
```

<!-- test: witness-devirtualization.a-trivial-element-takes-no-ownership-action-through-its-descriptor -->
`Slot.create` takes the reference its stored element owes, then releases the feed it was handed; `Slot.choose`
reclaims the argument it did not store; `Slot.replace` drops the element it displaces; `Slot.peek` takes a
reference to the element it returns. Each reads the layout descriptor of `T` it receives as a hidden parameter.
`Slot with Tally` has no retain word and no destroy word, so spliced into `intRound` each of the four is a
no-op. `intRound(1)` is `2 + 6` (the replaced item and the fallback), `intRound(2)` is `3 + 2`: 13.
```maxon
typealias Tally = int(0 to 1000000)

type Slot uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	static function choose(item T, fallback T, useItem bool) returns Self
		if useItem 'chosen'
			return Self{item: item}
		end 'chosen'

		return Self{item: fallback}
	end 'choose'

	function replace(next T)
		self.item = next
	end 'replace'

	function peek() returns T
		return self.item
	end 'peek'
end 'Slot'

typealias TallySlot = Slot with Tally

function intRound(seed Tally) returns Tally
	var slot = TallySlot.create(seed)
	slot.replace(seed + 1)
	let other = TallySlot.choose(seed, fallback: seed + 5, useItem: seed > 1)
	return slot.peek() + other.peek()
end 'intRound'

function main() returns ExitCode
	print("{intRound(1) + intRound(2)}\n")
	return 0
end 'main'
```
```stdout
13
```
```exitcode
0
```

<!-- test: witness-devirtualization.a-string-element-keeps-its-copy-and-releases-the-feed-through-its-descriptor -->
The `Slot` of the previous case over a `String`. The descriptor's retain word is `__str_clone` and its destroy
word the string's release, so each action resolves to a different outcome in `textRound`: the stored element is
copied by a direct `__str_clone`, the feed handed to `create` and `choose` and the element `replace` displaces
are released by a direct call, and the argument `choose` did not store is reclaimed by nothing, because the
retain word is set. A copy or a release the rewrite dropped or doubled leaks or frees a live string, and the leak
gate exits 101. `textRound(3)` is `"second 3"` and `"dd 3"`, 8 + 4; `textRound(4)` is `"second 4"` and `"c 4"`,
8 + 3: 23.
```maxon
typealias Tally = int(0 to 1000000)

type Slot uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	static function choose(item T, fallback T, useItem bool) returns Self
		if useItem 'chosen'
			return Self{item: item}
		end 'chosen'

		return Self{item: fallback}
	end 'choose'

	function replace(next T)
		self.item = next
	end 'replace'

	function peek() returns T
		return self.item
	end 'peek'
end 'Slot'

typealias TextSlot = Slot with String

function textRound(seed Tally) returns Tally
	var slot = TextSlot.create("first {seed}")
	slot.replace("second {seed}")
	let other = TextSlot.choose("c {seed}", fallback: "dd {seed}", useItem: seed > 3)
	return (slot.peek().byteLength() + other.peek().byteLength()) as Tally
end 'textRound'

function main() returns ExitCode
	print("{textRound(3) + textRound(4)}\n")
	return 0
end 'main'
```
```stdout
23
```
```exitcode
0
```

<!-- test: witness-devirtualization.a-struct-element-is-shared-and-destroyed-through-its-descriptor -->
The `Slot` of the previous cases over a `Label`, a struct holding a `String`. The retain word is `__mm_retain`
and the destroy word `__destruct_Label`: the element `create` and `peek` store or return is shared by a direct
`__mm_retain`, the feed and the displaced element are destroyed by a direct `__destruct_Label`, and the
argument `choose` did not store is reclaimed by nothing. `labelRound(5)` is the replaced weight 6 and the
fallback weight 9, `labelRound(6)` the replaced 7 and the chosen 7: 29.
```maxon
typealias Tally = int(0 to 1000000)

type Label
	export var text as String
	export var weight as Tally

	static function create(text String, weight Tally) returns Self
		return Self{text: text, weight: weight}
	end 'create'
end 'Label'

type Slot uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	static function choose(item T, fallback T, useItem bool) returns Self
		if useItem 'chosen'
			return Self{item: item}
		end 'chosen'

		return Self{item: fallback}
	end 'choose'

	function replace(next T)
		self.item = next
	end 'replace'

	function peek() returns T
		return self.item
	end 'peek'
end 'Slot'

typealias LabelSlot = Slot with Label

function labelRound(seed Tally) returns Tally
	var slot = LabelSlot.create(Label.create("a {seed}", weight: seed))
	slot.replace(Label.create("bb {seed}", weight: seed + 1))
	let other = LabelSlot.choose(Label.create("c {seed}", weight: 7), fallback: Label.create("dd {seed}", weight: 9), useItem: seed > 5)
	return slot.peek().weight + other.peek().weight
end 'labelRound'

function main() returns ExitCode
	print("{labelRound(5) + labelRound(6)}\n")
	return 0
end 'main'
```
```stdout
29
```
```exitcode
0
```

<!-- test: witness-devirtualization.a-promise-element-is-dropped-and-reclaimed-by-its-destroy-word-alone -->
A promise is never co-owned, so `Slot with Promise` has a destroy word and no retain word. In `promiseRound`
the retain of the element `create` stores is the identity, the release of the feed is nothing, and the drop of
the displaced promise and the reclaim of the one `choose` did not store are each a direct call of the destroy
word, which cancels the thread. A reclaim left to a retain word that is not there, or a release resolved
through the destroy word, would cancel the thread of a promise the slot holds or leave the other running past
the leak gate. `promiseRound(1)` is the replaced origin 11 and the fallback's 2, `promiseRound(2)` the replaced
12 and the chosen 1: 26.
```maxon
typealias Tally = int(0 to 1000000)
typealias TallyPromise = Promise with Tally

function work(n Tally) returns Tally
	Scheduler.yield()
	return n
end 'work'

type Slot uses T
	var item as T
	var origin as Tally

	static function create(item T, origin Tally) returns Self
		return Self{item: item, origin: origin}
	end 'create'

	static function choose(item T, spare T, useItem bool) returns Self
		if useItem 'chosen'
			return Self{item: item, origin: 1}
		end 'chosen'

		return Self{item: spare, origin: 2}
	end 'choose'

	function replace(next T, stamp Tally)
		self.item = next
		self.origin = stamp
	end 'replace'

	function source() returns Tally
		return self.origin
	end 'source'
end 'Slot'

typealias PromiseSlot = Slot with TallyPromise

function promiseRound(seed Tally) returns Tally
	var slot = PromiseSlot.create(async work(seed), origin: seed)
	slot.replace(async work(seed + 1), stamp: seed + 10)
	let other = PromiseSlot.choose(async work(seed), spare: async work(seed + 2), useItem: seed > 1)
	return slot.source() + other.source()
end 'promiseRound'

function main() returns ExitCode
	print("{promiseRound(1) + promiseRound(2)}\n")
	return 0
end 'main'
```
```stdout
26
```
```exitcode
0
```
