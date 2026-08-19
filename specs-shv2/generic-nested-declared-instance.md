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

### The two halves, and why the STATIC one was the hole

The forward existed for a receiver — `chain.count()` — and not for a static constructor's RESULT, so
`EChain.create()` reached the mint and **aborted the compiler**. Nothing in the argument-run reasoning is
receiver-specific: both spellings hold a value typed at the instance and both need the caller's blocks. They
are one decision now (`LowerMaxonToStd.emitInstanceDescriptorAddr`).

The supply half is only half the answer: the caller has to CARRY a descriptor to forward. The descriptor-need
fixpoint follows self-calls, so nothing reached across the `Bag`/`Inner` boundary. It draws a cross-type EDGE
now, from a call whose receiver RESOLVES to such an instance — an inner alias (`EChain.create()`), a field
declared at one (`chain.count()`), or a local bound from either. It is an EDGE and not a seed deliberately:
whether `Inner.create` needs a descriptor stays the fixpoint's own answer about `Inner.create`, so a holder
whose calls need nothing pays nothing. Seeded instead, `Holder.create(cell Cell)` — a body that is only
`Self{cell: cell}` — gained a hidden parameter and every call site gained a `lea` for a blob it never reads,
which moved 13 committed goldens.

### What is deliberately NOT covered

The containers this compiler serves itself — `Array`, `Vector` and every declared array-literal conformer —
keep the receiver-blind edge tuned to them (`Parser.corpusServesArrayMember`), which withholds statics for a
measured reason. This edge is for a DECLARED generic standing where a compiler-owned one used to.

## Tests

### A static constructor builds the enclosing type's own nested instance

The program the panic stood in front of. `Bag.create` is a `Self`-returning static, so it can carry a
descriptor its caller sources from the instance it builds, and it forwards block 0 into `Inner.create` —
whose `Array with T` reads `destroyFunc@40` out of it at run time.

<!-- test: a-static-constructor-builds-the-nested-instance -->
```maxon
typealias Int = int(i64.min to i64.max)

type Inner uses T
	typealias TArray = Array with T
	var items as TArray

	export static function create() returns Self
		return Self{items: TArray.create()}
	end 'create'

	export function count() returns Int
		return items.count()
	end 'count'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var chain as EChain

	export static function create() returns Self
		return Self{chain: EChain.create()}
	end 'create'

	export function size() returns Int
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

	export static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	export function slotSize() returns Int
		return sizeof(T)
	end 'slotSize'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var chain as EChain

	export static function create(x Element) returns Self
		return Self{chain: EChain.create(x)}
	end 'create'

	export function probe() returns Int
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

	export static function create() returns Self
		return Self{items: TArray.create()}
	end 'create'

	export function count() returns Int
		return items.count()
	end 'count'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var n as Int

	export static function make() returns Self
		var i = EChain.create()
		return Self{n: i.count() + 5}
	end 'make'

	export function size() returns Int
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

	export static function create() returns Self
		return Self{items: TArray.create()}
	end 'create'

	export function add(v T)
		items.push(v)
	end 'add'

	export function count() returns Int
		return items.count()
	end 'count'
end 'Inner'

type Bag uses Element
	typealias EChain = Inner with Element
	var chain as EChain

	export static function create() returns Self
		return Self{chain: EChain.create()}
	end 'create'

	export function add(v Element)
		chain.add(v)
	end 'add'

	export function size() returns Int
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
