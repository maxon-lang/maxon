---
feature: managed-list-opaque-element
status: stable
keywords: [managed-list, generic, type-parameter, layout-descriptor, ownership, drop]
category: ownership
---

# `__ManagedList` at a bare type parameter

## Documentation

A generic type may hold a `__ManagedList with Element` — a compiler-owned chain whose element is the
enclosing type's own parameter. The chain is created once per *instantiation*, and the element type is
not known when its body is compiled: a shared generic body compiles once for every instantiation.

### Where the element destructor comes from

`__list_create` stamps an `element_drop@24` into the chain, and the runtime walk in `__list_clear` reads
it at run time to release every element it still holds. For a concrete element that stamp is a statically
known callee. **For an opaque `Element` there is no such callee at compile time** — so the stamp comes
from the enclosing instantiation's **layout descriptor**, whose `destroyFunc@40` the caller already
threads in for exactly this purpose.

That is the same road a managed `Array with <type parameter>` already travels. Before this, the chain
door refused opacity outright rather than stamp a zero, because a zero stamp is not a refusal — it is a
leak that compiles.

### Why two instantiations in one program is the test that matters

A single instantiation cannot tell a *correct* descriptor from a *lucky* one. Two instantiations of one
generic body — one managed, one trivial — share that body and must not share the stamp: the managed one
must release its elements and the trivial one must not walk anything. A test that instantiates once
passes just as happily with the destructor hard-wired.

## Tests

<!-- test: a-chain-at-an-opaque-element-holds-managed-values -->
The base case. Every element is a heap `String` the chain owns; the run must end with nothing leaked,
which is what the exit code reports.
```maxon
typealias Count = int(0 to u64.max)

type Bag uses Element
	typealias EChain = __ManagedList with Element
	var chain as EChain

	export static function create() returns Self
		return Self{chain: EChain.create()}
	end 'create'

	export function add(v Element)
		self.chain.insertLast(v)
	end 'add'

	export function size() returns Count
		return self.chain.count()
	end 'size'
end 'Bag'

typealias StringBag = Bag with String

function main() returns ExitCode
	var b = StringBag.create()
	b.add("alpha")
	b.add("beta")
	b.add("gamma")
	return b.size() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-chain-at-an-opaque-element-holds-trivial-values -->
The trivial control. The same generic body, an element with no destructor: the stamp must be the
trivial one and the walk must release nothing.
```maxon
typealias Count = int(0 to u64.max)
typealias Small = int(0 to 1000)

type Bag uses Element
	typealias EChain = __ManagedList with Element
	var chain as EChain

	export static function create() returns Self
		return Self{chain: EChain.create()}
	end 'create'

	export function add(v Element)
		self.chain.insertLast(v)
	end 'add'

	export function size() returns Count
		return self.chain.count()
	end 'size'
end 'Bag'

typealias SmallBag = Bag with Small

function main() returns ExitCode
	var b = SmallBag.create()
	b.add(7)
	b.add(9)
	return b.size() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: two-instantiations-of-one-chain-body-carry-their-own-stamps -->
⭐⭐ **THE CONTROL THAT MATTERS.** One generic body, two instantiations, in one program. They share the
compiled body and must not share the element destructor: the `String` bag has to release its elements
and the `int` bag has to release nothing. A wrong stamp shows up here as a leak (exit 101) or a fault,
and shows up in neither test above on its own.
```maxon
typealias Count = int(0 to u64.max)
typealias Small = int(0 to 1000)

type Bag uses Element
	typealias EChain = __ManagedList with Element
	var chain as EChain

	export static function create() returns Self
		return Self{chain: EChain.create()}
	end 'create'

	export function add(v Element)
		self.chain.insertLast(v)
	end 'add'

	export function size() returns Count
		return self.chain.count()
	end 'size'
end 'Bag'

typealias StringBag = Bag with String
typealias SmallBag = Bag with Small

function main() returns ExitCode
	var s = StringBag.create()
	s.add("alpha")
	s.add("beta")

	var n = SmallBag.create()
	n.add(1)
	n.add(2)
	n.add(3)

	return (s.size() + n.size()) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: clearing-a-chain-of-opaque-managed-elements-releases-them -->
`clear()` is the runtime walk that reads `element_drop@24` and releases every element still held. With
an opaque element that stamp is the descriptor's, so this is the case where a zero stamp leaks rather
than merely being unused.
```maxon
typealias Count = int(0 to u64.max)

type Bag uses Element
	typealias EChain = __ManagedList with Element
	var chain as EChain

	export static function create() returns Self
		return Self{chain: EChain.create()}
	end 'create'

	export function add(v Element)
		self.chain.insertLast(v)
	end 'add'

	export function drop()
		self.chain.clear()
	end 'drop'

	export function size() returns Count
		return self.chain.count()
	end 'size'
end 'Bag'

typealias StringBag = Bag with String

function main() returns ExitCode
	var b = StringBag.create()
	b.add("alpha")
	b.add("beta")
	b.drop()
	return (b.size() + 4) as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: two-type-parameters-each-with-their-own-chain -->
⭐⭐ **THE BLOCK-OFFSET HAZARD, PINNED (W134 review).** A layout descriptor reserves one block **per type
parameter**, so a type with two of them holds two chains whose stamps come from two different blocks of
one descriptor. Reading the wrong block is not a refusal — it stamps the *other* parameter's destructor,
which for `Small`-then-`String` means either leaking every string or calling a string destructor on an
integer. The rung's own comments name this hazard; nothing pinned it, and a probe that finds nothing is
only a result once it is a case.
```maxon
typealias Count = int(0 to u64.max)
typealias Small = int(0 to 1000)

type Twin uses First, Second
	typealias FirstChain = __ManagedList with First
	typealias SecondChain = __ManagedList with Second
	var left as FirstChain
	var right as SecondChain

	export static function create() returns Self
		return Self{left: FirstChain.create(), right: SecondChain.create()}
	end 'create'

	export function addLeft(v First)
		self.left.insertLast(v)
	end 'addLeft'

	export function addRight(v Second)
		self.right.insertLast(v)
	end 'addRight'

	export function total() returns Count
		return self.left.count() + self.right.count()
	end 'total'
end 'Twin'

typealias SmallThenString = Twin with Small, String

function main() returns ExitCode
	var t = SmallThenString.create()
	t.addLeft(1)
	t.addLeft(2)
	t.addRight("alpha")
	return t.total() as ExitCode
end 'main'
```
```exitcode
3
```

