---
feature: generic-nested-declared-instance
status: stable
keywords: [generics, type-parameter, layout-descriptor, dictionary, inner-alias, static-constructor]
category: type-system
---

# A generic type holding a DECLARED generic over its own type parameter

## Documentation

`type Bag uses Element` may declare `typealias EChain = Inner with Element` and hold one:
`var chain as EChain`. The instance `Inner with Element` is not concrete — its type argument is `Bag`'s own
parameter — so **it has no layout-descriptor blob and can never have one**. The blob carries its argument's
SIZE, and a type parameter's size is itself a descriptor read; asking for it is
`primitiveTypeByteSize`'s panic rather than a number.

Every layout-needing call through such a value therefore FORWARDS the enclosing frame's descriptor blocks
instead of pointing at a blob. That works because the instance's arguments are a contiguous, in-order run of
the caller's own parameters, so one base address lines the caller's blocks up with the callee's — and because
the four words a shared body ever reads (`copyFunc@32`, `destroyFunc@40`, `elementLogicalSize@56`,
`retainFunc@64`) are facts about the type ARGUMENT, which the run makes identical on both sides.

### The two halves, and why the STATIC one needs the forward too

The forward serves a receiver — `chain.count()` — and a static constructor's RESULT alike; without it
`EChain.create()` would reach the mint and **abort the compiler**. Nothing in the argument-run reasoning is
receiver-specific: both spellings hold a value typed at the instance and both need the caller's blocks. They
are one decision (`LowerMaxonToStd.emitInstanceDescriptorAddr`).

The supply half is only half the answer: the caller has to CARRY a descriptor to forward. The descriptor-need
fixpoint follows self-calls, and across the `Bag`/`Inner` boundary it draws a cross-type EDGE, from a call
whose receiver RESOLVES to such an instance — an inner alias (`EChain.create()`), a field
declared at one (`chain.count()`), or a local bound from either. It is an EDGE and not a seed deliberately:
whether `Inner.create` needs a descriptor stays the fixpoint's own answer about `Inner.create`, so a holder
whose calls need nothing pays nothing. Seeded instead, `Holder.create(cell Cell)` — a body that is only
`Self{cell: cell}` — would gain a hidden parameter and every call site a `lea` for a blob it never reads.

### What is deliberately NOT covered

The containers this compiler serves itself — `Array`, `FixedArray` and every declared array-literal conformer —
keep the receiver-blind edge tuned to them (`Parser.corpusServesArrayMember`), which withholds statics.
This edge is for a DECLARED generic.

## Tests

### A static constructor builds the enclosing type's own nested instance

`Bag.create` is a `Self`-returning static, so it can carry a
descriptor its caller sources from the instance it builds, and it forwards block 0 into `Inner.create` —
whose `Array with T` reads `destroyFunc@40` out of it at run time.

<!-- test: a-static-constructor-builds-the-nested-instance -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	typealias TArray = Array with T
	var items as TArray

	static function create() returns Self
		return Self{items: TArray.create()}
	end 'create'

	function count() returns Int
		return items.count()
	end 'count'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var chain as EChain

	static function create() returns Self
		return Self{chain: EChain.create()}
	end 'create'

	function size() returns Int
		return chain.count()
	end 'size'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create()
	return b.size()
end 'main'
```
```exitcode
0
```

### A call on the field reads the forwarded block

`sizeof(T)` inside `Inner` is a load of `elementLogicalSize@56` from whatever descriptor the frame was
handed. `Bag.probe` hands over its own block 0, which describes `Element` — and `Element` is `Int`, so the
answer is a machine word.

<!-- test: a-call-on-the-field-reads-the-forwarded-block -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(T)
	end 'slotSize'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var chain as EChain

	static function create(x Element) returns Self
		return Self{chain: EChain.create(x)}
	end 'create'

	function probe() returns Int
		return chain.slotSize()
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create(7)
	return b.probe()
end 'main'
```
```exitcode
8
```

### A nested instance built into a LOCAL, held in no field

The holder declares no field at the instance at all, so nothing about the TYPE could have seen it — the edge
is drawn off the construction itself, and the local it is bound to carries the same base onward.

<!-- test: a-nested-instance-built-into-a-local -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	typealias TArray = Array with T
	var items as TArray

	static function create() returns Self
		return Self{items: TArray.create()}
	end 'create'

	function count() returns Int
		return items.count()
	end 'count'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var n as Int

	static function make() returns Self
		var i = EChain.create()
		return Self{n: i.count() + 5}
	end 'make'

	function size() returns Int
		return n
	end 'size'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.make()
	return b.size()
end 'main'
```
```exitcode
5
```

### The whole chain over a MANAGED element

The forwarded word is a real destructor here, not a zero: `Bag with String` puts `__str_decref` in block 0's
`destroyFunc@40`, `Inner.create` stamps it into the record its `Array with T` builds, and the two `String`s
the bag takes are released by that record's own walk. One retain per store against one release per
destruction is the whole of the contract — a surplus release faults and a missing one is exit 101.

<!-- test: the-whole-chain-over-a-managed-element -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	typealias TArray = Array with T
	var items as TArray

	static function create() returns Self
		return Self{items: TArray.create()}
	end 'create'

	function add(v T)
		items.push(v)
	end 'add'

	function count() returns Int
		return items.count()
	end 'count'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var chain as EChain

	static function create() returns Self
		return Self{chain: EChain.create()}
	end 'create'

	function add(v Element)
		chain.add(v)
	end 'add'

	function size() returns Int
		return chain.count()
	end 'size'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	b.add("a string long enough to be heap allocated")
	b.add("another string long enough to be heap allocated")
	return b.size()
end 'main'
```
```exitcode
2
```

### A PARAMETER declared at the inner alias is a receiver too

The edge resolves a receiver through name doors — an inner alias, a field of the enclosing type, a local
bound by `var <n> = <name>` — and a parameter is none of them: the alias appears as the declaration's TYPE
rather than as a value. Without a door of its own, `c.slotSize()` would draw no edge, `probeVia` would
reserve nothing, and the call would reach the mint for an instance that can never have a blob — a
`LayoutDescriptor` panic. `var x as EChain` is the same shape with `as` between the two tokens.

<!-- test: a-parameter-declared-at-the-inner-alias -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(T)
	end 'slotSize'
end 'Inner'

type Bag uses Element
	export typealias EChain = Inner with Element
	var chain as EChain

	static function create(x Element) returns Self
		return Self{chain: EChain.create(x)}
	end 'create'

	function probeVia(c EChain) returns Int
		return c.slotSize()
	end 'probeVia'

	function probe() returns Int
		return self.probeVia(chain)
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create(7)
	return b.probe()
end 'main'
```
```exitcode
8
```

### A `for … in self` whose cursor is built through the inner alias

The loop's `createIterator()` / `current()` /
`advance()` are emitted by the parser and appear in NO token — the per-trip drop's SEED reads that fact,
and it hides the CALL EDGES just as completely. `BIter.create()` inside `createIterator` reserves a
descriptor, so without edges from the emitted calls `forwardCallerLayout` would find that caller
`Bag.walk` has no layout descriptor to forward to `Bag.createIterator`.

The element is a concrete `Int`, deliberately: that keeps the per-trip drop's own seed OFF (its gate asks whether the
cursor yields a bare type parameter), so the reservation under test can only have arrived through the
emitted-call edges.

<!-- test: a-cursor-built-through-the-inner-alias-is-walked -->
```maxon
typealias Int = int(i64.min to i64.max)

type BagIter uses E implements Iterator with Int
	typealias EArray = Array with E
	var items as EArray
	var i as Int

	static function create() returns Self
		return Self{items: EArray.create(), i: 3}
	end 'create'

	function current() returns Int
		return i
	end 'current'

	function advance() throws IterationError
		i = i - 1
		if i <= 0 'exhausted'
			throw IterationError.exhausted
		end 'exhausted'
	end 'advance'
end 'BagIter'

type Bag uses Element implements Iterable with (Int, BIter)
	export typealias BIter = BagIter with Element
	var n as Int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function createIterator() returns BIter throws IterationError
		return BIter.create()
	end 'createIterator'

	function walk() returns Int
		var total = 0 as Int
		for x in self 'loop'
			total = total + x
		end 'loop'
		return total
	end 'walk'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create()
	return b.walk()
end 'main'
```
```exitcode
6
```

### An instance over the SECOND of two parameters reads that parameter's size

`Wrapper with (Small, String)` hands `Slot with U` the block of `U`, so `sizeof(V)` inside `Slot` is `String`'s.

<!-- test: an-instance-over-the-second-parameter-of-two-reads-that-parameters-size -->
```maxon
typealias Small = int(0 to 255)

type Slot uses V
	export var v as V

	static function create(first V) returns Self
		return Self{v: first}
	end 'create'

	function slotSize() returns Count
		return sizeof(V)
	end 'slotSize'
end 'Slot'

type Wrapper uses T, U
	export typealias Inner = Slot with U
	var tag as T
	var inner as Inner

	static function create(t T, u U) returns Self
		return Self{tag: t, inner: Inner.create(u)}
	end 'create'

	function ownSize() returns Count
		return self.inner.slotSize()
	end 'ownSize'
end 'Wrapper'

typealias StrSlot = Slot with String
typealias SmallStrWrapper = Wrapper with (Small, String)

function main() returns ExitCode
	let w = SmallStrWrapper.create(3, u: "alpha {40 + 2} value long enough for a heap record")
	let concrete = StrSlot.create("x")
	print("{w.ownSize()} {concrete.slotSize()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
56 56
```

### A call two nested instances deep

<!-- test: a-call-on-a-field-of-a-field-both-nested-instances -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(T)
	end 'slotSize'
end 'Inner'

type Middle uses U
	typealias EChain = Inner with U
	export var chain as EChain

	static function create(x U) returns Self
		return Self{chain: EChain.create(x)}
	end 'create'
end 'Middle'

type Bag uses Element
	typealias EMiddle = Middle with Element
	var middle as EMiddle

	static function create(x Element) returns Self
		return Self{middle: EMiddle.create(x)}
	end 'create'

	function probe() returns Int
		return self.middle.chain.slotSize()
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create(7)
	return b.probe()
end 'main'
```
```exitcode
8
```

### A call on the nested field of a `Self`-typed parameter

`other.chain.slotSize()` reaches the same `Inner` the receiver's own `chain` does, so `probe` forwards its
block 0 to it exactly as it does for `chain.slotSize()`.

<!-- test: a-call-on-the-nested-field-of-a-self-typed-parameter -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(T)
	end 'slotSize'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	export var chain as EChain

	static function create(x Element) returns Self
		return Self{chain: EChain.create(x)}
	end 'create'

	function probe(other Self) returns Int
		return other.chain.slotSize()
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var a = IntBag.create(7)
	var b = IntBag.create(9)
	return a.probe(b)
end 'main'
```
```exitcode
8
```

### A closure parameter named like a nested field denotes the parameter's instance only inside the closure

`inner` is a field over a `Pair`, whose `slotSize()` reads the block; the closure's own `inner` is an
`Inner with Element`, whose `slotSize()` reads none. The call after the closure is the field's.

<!-- test: a-closure-parameter-named-like-a-nested-field-calls-through-its-own-instance-and-the-field-keeps-its-own-after -->
```maxon
typealias Int = int(i64.min to i64.max)

type Pair uses A
	var w as A

	static function create(x A) returns Self
		return Self{w: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(A)
	end 'slotSize'
end 'Pair'

type Inner uses T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function slotSize() returns Int
		return 1
	end 'slotSize'
end 'Inner'

type Bag uses Element
	typealias EPair = Pair with Element
	typealias EChain = Inner with Element
	var inner as EPair

	static function create(x Element) returns Self
		return Self{inner: EPair.create(x)}
	end 'create'

	function probe(chain EChain) returns Int
		let g = function(inner EChain) gives inner.slotSize()
		return g(chain) + inner.slotSize()
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int
typealias IntChain = Inner with Int

function main() returns ExitCode
	var b = IntBag.create(7)
	return b.probe(IntChain.create(9))
end 'main'
```
```exitcode
9
```

### A closure parameter named like a type-parameter field is not the field

<!-- test: a-closure-parameter-named-like-a-type-parameter-field-calls-a-corpus-member-of-its-own-array -->
```maxon
type Bag uses Element
	export typealias Items = Array with Element
	var current as Element

	static function create(first Element) returns Self
		return Self{current: first}
	end 'create'

	function probe(xs Items) returns bool
		let g = function(current Items) gives current.isEmpty()
		return g(xs)
	end 'probe'

	function value() returns Element
		return self.current
	end 'value'
end 'Bag'

typealias StrBag = Bag with String
typealias StrItems = Array with String

function main() returns ExitCode
	var b = StrBag.create("alpha {40 + 2} value long enough for a heap record")
	var xs = StrItems.create()
	xs.push("beta {40 + 2} value long enough for a heap record")
	print("{b.probe(xs)} {b.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
false alpha 42 value long enough for a heap record
```

### A closure parameter that is not an instance ends with its closure

`chain` is a field over an `Inner`; the closure's `chain` is an `Int`. The call after the closure is still the field's.

<!-- test: a-closure-parameter-of-another-type-leaves-the-field-in-scope-after-it -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(T)
	end 'slotSize'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var chain as EChain

	static function create(x Element) returns Self
		return Self{chain: EChain.create(x)}
	end 'create'

	function probe() returns Int
		let a = chain.slotSize()
		let g = function(chain Int) gives chain + 1
		return a + g(1) + chain.slotSize()
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create(7)
	return b.probe()
end 'main'
```
```exitcode
18
```

### A closure parameter's extent runs through a multi-line body

The closure's body is a `match` whose arms stand on the lines after the `gives`; the parameter `e` stays an `Inner` instance through the final `end`, so the arm's `e.slotSize()` draws its edge.

<!-- test: a-closure-parameters-extent-runs-through-a-multi-line-match-body -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(T)
	end 'slotSize'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	function probe(chain EChain) returns Int
		let g = function(e EChain, pick Int) gives match pick 'arm'
			1 gives e.slotSize()
			default gives 0
		end 'arm'

		return g(chain, 1)
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int
typealias IntChain = Inner with Int

function main() returns ExitCode
	var b = IntBag.create()
	return b.probe(IntChain.create(9))
end 'main'
```
```exitcode
8
```

### A forwarded size read through a cursor of a two-parameter instance

`Pairs with (bool, String)` reaches `Slot with W` through its iterator, which `map` builds from a body that carries no descriptor of its own. The read of `sizeof(V)` is `String`'s, never a blank block's.

<!-- test: a-forwarded-size-read-through-a-cursor-of-a-two-parameter-instance -->
```maxon
public type Slot uses V
	var v as V

	public static function create(x V) returns Self
		return Self{v: x}
	end 'create'

	public static function measure() returns Count
		return sizeof(V)
	end 'measure'
end 'Slot'

public type PairIter uses K, W implements Iterator with Count
	public typealias Held = Slot with W
	var done as bool

	public static function create() returns Self
		return Self{done: false}
	end 'create'

	public function current() returns Count
		return Held.measure()
	end 'current'

	public function advance() throws IterationError
		if done 'finished'
			throw IterationError.exhausted
		end 'finished'

		done = true
	end 'advance'
end 'PairIter'

public type Pairs uses Key, Value implements Iterable with (Count, PIter)
	public typealias PIter = PairIter with (Key, Value)
	var tag as Key
	var value as Value

	public static function create(tag Key, value Value) returns Self
		return Self{tag: tag, value: value}
	end 'create'

	public function createIterator() returns PIter throws IterationError
		return PIter.create()
	end 'createIterator'
end 'Pairs'

typealias StrPairs = Pairs with (bool, String)

function main() returns ExitCode
	let p = StrPairs.create(true, value: "alpha {40 + 2} value long enough for a heap record")
	let xs = p.map(function(n Count) gives n)
	print("{xs.count()} {try xs.get(0) otherwise 99}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 56
```

### A local bound to a longer chain is the instance the whole chain answers

`chain.peer().plain()` ends in `Plain`, not in the `Pair` that `peer` answers, so `c.slotSize()` is `Plain`'s.

<!-- test: a-local-bound-to-a-longer-chain-is-the-instance-the-whole-chain-answers -->
```maxon
typealias Int = int(i64.min to i64.max)

type Plain
	var n as Int

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	function slotSize() returns Int
		return n
	end 'slotSize'
end 'Plain'

type Pair uses A
	var w as A

	static function create(x A) returns Self
		return Self{w: x}
	end 'create'

	function plain() returns Plain
		return Plain.create()
	end 'plain'

	function slotSize() returns Int
		return sizeof(A)
	end 'slotSize'
end 'Pair'

type Inner uses T
	typealias P = Pair with T
	var p as P

	static function create(x T) returns Self
		return Self{p: P.create(x)}
	end 'create'

	function peer() returns P
		return p
	end 'peer'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var chain as EChain

	static function create(x Element) returns Self
		return Self{chain: EChain.create(x)}
	end 'create'

	function probe() returns Int
		let c = chain.peer().plain()
		return c.slotSize()
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create(7)
	return b.probe()
end 'main'
```
```exitcode
3
```

### A local bound to a method's result is the instance that method returns

`chain.peer()` answers a `Pair`, not the `Inner` that `chain` is, so `c.slotSize()` is `Pair`'s.

<!-- test: a-local-bound-to-a-methods-result-is-the-instance-the-method-returns -->
```maxon
typealias Int = int(i64.min to i64.max)

type Pair uses A
	var w as A

	static function create(x A) returns Self
		return Self{w: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(A)
	end 'slotSize'
end 'Pair'

type Inner uses T
	typealias P = Pair with T
	var p as P

	static function create(x T) returns Self
		return Self{p: P.create(x)}
	end 'create'

	function peer() returns P
		return p
	end 'peer'

	function slotSize() returns Int
		return 1
	end 'slotSize'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var chain as EChain

	static function create(x Element) returns Self
		return Self{chain: EChain.create(x)}
	end 'create'

	function probe() returns Int
		let c = chain.peer()
		return c.slotSize()
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create(7)
	return b.probe()
end 'main'
```
```exitcode
8
```

### The same through a chain of two nested fields

<!-- test: a-local-bound-to-a-methods-result-through-two-nested-fields -->
```maxon
typealias Int = int(i64.min to i64.max)

type Pair uses A
	var w as A

	static function create(x A) returns Self
		return Self{w: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(A)
	end 'slotSize'
end 'Pair'

type Inner uses T
	typealias P = Pair with T
	var p as P

	static function create(x T) returns Self
		return Self{p: P.create(x)}
	end 'create'

	function peer() returns P
		return p
	end 'peer'

	function slotSize() returns Int
		return 1
	end 'slotSize'
end 'Inner'

type Middle uses U
	typealias EChain = Inner with U
	export var b as EChain

	static function create(x U) returns Self
		return Self{b: EChain.create(x)}
	end 'create'
end 'Middle'

type Bag uses Element
	typealias EMiddle = Middle with Element
	var a as EMiddle

	static function create(x Element) returns Self
		return Self{a: EMiddle.create(x)}
	end 'create'

	function probe() returns Int
		let c = self.a.b.peer()
		return c.slotSize()
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create(7)
	return b.probe()
end 'main'
```
```exitcode
8
```

### A local bound to a call on an overloaded method's result

`dup` has two overloads that both answer `Self`, so the result is the receiver's own instance whichever is bound, and `c.size()` reads `bool`'s size.

<!-- test: a-local-bound-to-an-overloaded-methods-result-is-the-receiver-instance -->
```maxon
type Slot uses V
	var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function dup() returns Self
		return Self{n: n}
	end 'dup'

	function dup(times Count) returns Self
		return Self{n: times}
	end 'dup'

	function size() returns Count
		return sizeof(V)
	end 'size'
end 'Slot'

type Bag uses Element
	typealias Inner = Slot with Element
	var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'

	function probe() returns Count
		let c = self.inner.dup()
		return c.size()
	end 'probe'
end 'Bag'

typealias BoolBag = Bag with bool

function main() returns ExitCode
	let b = BoolBag.create()
	print("{b.probe()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

### A nested instance bound to a local from a `Self`-typed parameter's field

<!-- test: a-local-bound-to-the-nested-field-of-a-self-typed-parameter -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(T)
	end 'slotSize'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	export var chain as EChain

	static function create(x Element) returns Self
		return Self{chain: EChain.create(x)}
	end 'create'

	function probe(other Self) returns Int
		view peer = other.chain
		return peer.slotSize()
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var a = IntBag.create(7)
	var b = IntBag.create(9)
	return a.probe(b)
end 'main'
```
```exitcode
8
```

### Two sibling closures, each parameter its own instance

<!-- test: two-sibling-closures-name-a-parameter-alike-over-different-instances -->
```maxon
typealias Int = int(i64.min to i64.max)

type Pair uses A
	var w as A

	static function create(x A) returns Self
		return Self{w: x}
	end 'create'

	function slotSize() returns Int
		return sizeof(A)
	end 'slotSize'
end 'Pair'

type Inner uses T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function slotSize() returns Int
		return 1
	end 'slotSize'
end 'Inner'

type Bag uses Element
	typealias EPair = Pair with Element
	typealias EChain = Inner with Element
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	function probe(chain EChain, pair EPair) returns Int
		let g = function(c EChain) gives c.slotSize()
		let h = function(c EPair) gives c.slotSize()
		return g(chain) + h(pair)
	end 'probe'
end 'Bag'

typealias IntBag = Bag with Int
typealias IntChain = Inner with Int
typealias IntPair = Pair with Int

function main() returns ExitCode
	var b = IntBag.create()
	return b.probe(IntChain.create(9), pair: IntPair.create(7))
end 'main'
```
```exitcode
9
```
