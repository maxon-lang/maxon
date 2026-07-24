---
feature: string-conformance
keywords: [hashable, equatable, string, witness, where, generics, djb2]
category: type-system
---

# String Interface Conformance

## Documentation

The managed concrete type `String` conforms to the `Hashable` and `Equatable` protocol interfaces WITHOUT a
user `implements` clause — the compiler synthesizes its `String.hash` / `String.equals` implementations
natively, exactly as it does for the primitive `int` (the type-side twin of the synthesized
`Hashable`/`Equatable` interfaces). So `Box with String where T is Hashable and Equatable` is legal and its
witness slots resolve to real `.text` symbols. This is the prerequisite for String-keyed hash collections.

- `String.hash()` is djb2 over the String's bytes: `h = 5381`, then `h = h * 33 + b` for each byte `b`,
  returning `h and 0xFFFFFFFF` (type `HashValue = int(0 to u32.max)`). So `""` -> `5381`, `"abc"` ->
  `193485963`, `"hi"` -> `5863446`.
- `String.equals(other)` is content equality: equal lengths and equal bytes — the same byte-compare
  `__str_eq` computes for the `==` operator, emitted here under the witness-visible `String.equals` symbol.

Inside a generic body the concrete type is unknown, so a call on a constrained parameter —
`self.item.hash()`, `self.item.equals(other)`, or the `==`/`!=` OPERATOR on a `T`-typed value — dispatches
through the runtime WITNESS TABLE (dictionary-passing), whose slots for a `String` argument point at the
synthesized `String.hash`/`String.equals`. The witness receiver (`self.item`) and the `other` argument are
BORROWED: the container owns the `String` and drops it exactly once at scope exit, so hashing or comparing a
boxed `String` neither double-frees nor leaks it.

The witness dispatch rides the x64 rdata function-pointer relocation, so these cases are x64-only (as the
`primitive-conformance` and `where-clauses` witness cases are).

Direct `s.hash()` on a concrete `String` value and `Character` conformance are separate future slices and are
NOT covered here. `Set with String` keys — which reuse exactly this `String` `Hashable`/`Equatable`
conformance — ship in P1.7b and are covered by `set-string`.

## Tests

<!-- test: string-conformance.hash-values -->
<!-- targets: x64-windows, x64-linux -->
A `String` argument's `Hashable` witness dispatches `element.hash()` to the synthesized `String.hash` — djb2
over the bytes. Three boxed Strings are constructed, hashed, and dropped at scope exit: the empty string
(`5381`), `"abc"` (`193485963`), and `"hi"` (`5863446`).
```maxon
type Box uses T where T is Hashable
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemHash() returns HashValue
		return self.item.hash()
	end 'itemHash'
end 'Box'

typealias StringBox = Box with String

function main() returns ExitCode
	let e = StringBox.create("")
	if e.itemHash() != 5381 'p1'
		return 1
	end 'p1'
	let a = StringBox.create("abc")
	if a.itemHash() != 193485963 'p2'
		return 2
	end 'p2'
	let h = StringBox.create("hi")
	if h.itemHash() != 5863446 'p3'
		return 3
	end 'p3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.equals-via-constraint -->
<!-- targets: x64-windows, x64-linux -->
A `String` argument's `Equatable` witness dispatches `element.equals(other)` to the synthesized
`String.equals` — content equality. The boxed `String` and the two `other` arguments are borrowed, so `b`
stays live and droppable across both comparisons.
```maxon
type Box uses T where T is Equatable
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function sameAs(other T) returns bool
		return self.item.equals(other)
	end 'sameAs'
end 'Box'

typealias StringBox = Box with String

function main() returns ExitCode
	let b = StringBox.create("abc")
	if not b.sameAs("abc") 'eq'
		return 1
	end 'eq'
	if b.sameAs("abd") 'ne'
		return 2
	end 'ne'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.hashable-and-equatable -->
<!-- targets: x64-windows, x64-linux -->
The hash-collection key shape — a parameter constrained with `where T is Hashable and Equatable` dispatches
BOTH witnesses on a `String` argument, each to its synthesized impl.
```maxon
type Key uses T where T is Hashable and Equatable
	export var value as T
	export static function create(value T) returns Self
		return Self{ value: value }
	end 'create'
	export function digest() returns HashValue
		return self.value.hash()
	end 'digest'
	export function matches(other T) returns bool
		return self.value.equals(other)
	end 'matches'
end 'Key'

typealias StringKey = Key with String

function main() returns ExitCode
	let k = StringKey.create("hi")
	if k.digest() != 5863446 'h'
		return 1
	end 'h'
	if not k.matches("hi") 'e1'
		return 2
	end 'e1'
	if k.matches("no") 'e2'
		return 3
	end 'e2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.eq-with-equatable -->
<!-- targets: x64-windows, x64-linux -->
The `==` OPERATOR on an Equatable-constrained type parameter lowers to the Equatable witness dispatch — for a
`String` argument that is the synthesized `String.equals`, so `b.eq("abc")` is true and the `if` returns 1.
```maxon
type Box uses T where T is Equatable
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function eq(other T) returns bool
		return item == other
	end 'eq'
end 'Box'

typealias StringBox = Box with String

function main() returns ExitCode
	let b = StringBox.create("abc")
	if b.eq("abc") 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-conformance.hash-in-loop-no-leak -->
<!-- targets: x64-windows, x64-linux -->
A boxed `String` constructed, hashed through the `Hashable` witness, and dropped every iteration of a
100-iteration loop — the standing leak/double-free probe. If the witness receiver were consumed rather than
borrowed the per-iteration drop would double-free the freed payload; if the box failed to drop it the run
would leak. Neither happens: `acc` reaches 100 and the leak gate stays green.
```maxon
type Box uses T where T is Hashable
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemHash() returns HashValue
		return self.item.hash()
	end 'itemHash'
end 'Box'

typealias StringBox = Box with String
typealias Counter = int(0 to 1000)

function main() returns ExitCode
	var i = 0 as Counter
	var acc = 0 as Counter
	while i < 100 'loop'
		let b = StringBox.create("abc")
		if b.itemHash() == 193485963 'ok'
			acc = acc + 1
		end 'ok'
		i = i + 1
	end 'loop'
	if acc == 100 'all'
		return 0
	end 'all'
	return 1
end 'main'
```
```exitcode
0
```
