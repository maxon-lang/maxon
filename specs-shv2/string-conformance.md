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

The witness dispatch rides the rdata function-pointer relocation, which EVERY target now fills — the
fixed-base writers bake a `.text` VA, wasm a funcref-table index, and arm64-macOS a dyld chained-fixup
rebase — so these cases run everywhere and carry no target marker (as the `primitive-conformance` and
`where-clauses` witness cases do).

Direct `s.hash()` / `s.equals(t)` on a concrete `String` value ship too, and reach the SAME two symbols: the
call names `String.hash`, the witness slot holds a relocation to `String.hash`. There is one body per method
and no thunk between the two spellings, which is what the `direct-hash-agrees-with-the-witness` case below
asserts. `Set with String` keys probe through those same two witness slots and ship in P1.7b, covered by
`set-string`; direct dispatch on a `Character` VALUE is a separate future slice and is not covered here
(a `Character` receiver is routed to its own method table, which has no conformance fall-through yet).

⚠ **THE `String.hash` / `String.equals` SYMBOLS ARE THE COMPILER'S, AND THAT IS ENFORCED RATHER THAN
ASSUMED.** A user declaration binding the name `String` — or `Character`, whose two impls are built from the
same builders — is refused (`TypeResolution.isCompilerOwnedTypeName`), because such a declaration's own
`hash()` mints the identical symbol and the installer declines to build an impl for a name the module
already defines. The two error cases at the end of this file pin both refusals and record the wrong answers
measured before they existed.

## Tests

<!-- test: string-conformance.hash-values -->
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

<!-- test: string-conformance.direct-hash-on-a-concrete-string -->
### A concrete `String` value hashes DIRECTLY, with no witness table in the program
`s.hash()` on a String-typed value is an ordinary `call String.hash` — the very symbol a
`__witness_String.Hashable` slot would have been stamped with — so the three djb2 answers the witness cases
above pin through a `Box` are the same three answers here, reached without one. The three pins are the
canonical djb2 triple: `""` -> `5381`, `"abc"` -> `193485963`, `"hi"` -> `5863446`.
```maxon
function main() returns ExitCode
	let e = ""
	if e.hash() != 5381 'p1'
		return 1
	end 'p1'
	let a = "abc"
	if a.hash() != 193485963 'p2'
		return 2
	end 'p2'
	let h = "hi"
	if h.hash() != 5863446 'p3'
		return 3
	end 'p3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.direct-hash-agrees-with-the-witness -->
### The direct call and the witness dispatch reach ONE body
Both spellings in one program, over the same bytes. They must agree not because two implementations were
written to match, but because there is only one: the direct call names `String.hash` and the witness slot
holds a relocation to `String.hash`.
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
	let s = "abc"
	let b = StringBox.create("abc")
	if s.hash() != b.itemHash() 'agree'
		return 1
	end 'agree'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.direct-equals-on-a-concrete-string -->
### A concrete `String` value compares DIRECTLY through `String.equals`
Content equality, both verdicts, plus the length-mismatch shortcut the impl opens with.
```maxon
function main() returns ExitCode
	let a = "ab"
	if not a.equals("ab") 'same'
		return 1
	end 'same'
	if a.equals("ac") 'differentByte'
		return 2
	end 'differentByte'
	if a.equals("abc") 'differentLength'
		return 3
	end 'differentLength'
	if not "".equals("") 'bothEmpty'
		return 4
	end 'bothEmpty'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.direct-call-borrows-both-operands -->
### Both operands are BORROWED, so an OWNED temporary still drops
The receiver and the `other` argument are read and never consumed — `String.hash` walks the bytes and
`String.equals` compares them, freeing nothing — so an interpolated argument stays an ordinary pending temp
and drops at statement end. Run 100 times so a leaked or double-freed temp cannot hide; the leak gate is the
assertion.
```maxon
typealias Counter = int(0 to 1000)

function main() returns ExitCode
	var i = 0 as Counter
	var acc = 0 as Counter
	while i < 100 'loop'
		if "a1b".equals("a{1}b") 'eq'
			acc = acc + 1
		end 'eq'
		if "a{1}b".hash() == 5863446 + 0 'neverTrue'
			return 1
		end 'neverTrue'
		i = i + 1
	end 'loop'
	if acc == 100 'all'
		return 0
	end 'all'
	return 2
end 'main'
```
```exitcode
0
```

<!-- test: error.direct-equals-rejects-a-non-string-argument -->
### `equals` declares `other Self`, so its argument must be a `String`
`String.equals` walks bytes off a pointer, so an `int` actual would be DEREFERENCED — the mirror of the
wrong answer `int.equals` gives a String actual, and refused by the same `Self`-formal check.
```maxon
function main() returns ExitCode
	let a = "ab"
	if a.equals(3) 'notAString'
		return 1
	end 'notAString'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:7: 'equals' requires a String, but its argument is int
```

<!-- test: error.a-declaration-may-not-bind-the-name-String -->
### A declaration may not bind `String` to a nominal identity
⚠ **This refusal is what makes the `String.hash` / `String.equals` SYMBOLS the compiler's.** A user
`type String` is otherwise inert — `parseTypeReference` settles the name syntactically, so it can never be
named at a parameter, `String{…}` is E3076 and `String.create(…)` is refused as an unknown builtin static —
but its `hash()` method registers the symbol `String.hash`, and the conformance installer builds an impl only
for a name the module does not already define. MEASURED before this refusal existed, with no diagnostic
anywhere: `Box with String`'s `itemHash()` of `""` returned **7** (the user's body) instead of **5381**, and
`Set with String` counted **3** for `insert("alice"); insert("bob"); insert("alice")` against a control's
**2** — a duplicate key stored twice, because the user's `equals` answered `false`.
```maxon
type String
	export var value as ExitCode

	export function hash() returns HashValue
		return 7
	end 'hash'
end 'String'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:6: Unsupported: a declaration of the type name 'String', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and shv2 has no namespace to tell a user declaration of the name apart from that one
```

<!-- test: error.a-declaration-may-not-bind-the-name-Character -->
### `Character` is reserved on the same footing, and for the same symbols
`Character.hash` and `Character.equals` are synthesized from the very builders `String`'s are
(`BuiltinConformanceRuntime` — a Character IS the fused byte record), under Character's own symbols. So a
user declaration of the name captures those two exactly as a `type String` captures String's, and the name
is reserved beside it rather than after the next measurement.
```maxon
enum Character
	first
	second
end 'Character'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:6: Unsupported: a declaration of the type name 'Character', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and shv2 has no namespace to tell a user declaration of the name apart from that one
```
