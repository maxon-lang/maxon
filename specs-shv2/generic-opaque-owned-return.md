---
feature: generic-opaque-owned-return
status: stable
keywords: [generics, type-parameter, ownership, layout-descriptor, retain, return, dictionary]
category: type-system
---

# Returning an OWNED value through a `returns <type parameter>` boundary

## Documentation

A shared generic body is compiled ONCE for every instantiation, so a `returns T` hand-off has to obey
ONE convention whatever the body happens to return — the caller reads the DECLARED return type and can
see nothing of the body.

The convention is the same one every other managed return in the language already uses: **the callee
hands the caller a `+1`, and the caller drops it exactly once.** A body that returns a value it already
owns — a freshly built `String`, an element moved out of an `Array with T` field by `pop`/`remove` —
simply hands that reference over. A body that returns a BORROW — `return self.value`, `return v` for a
borrowed `T` parameter — owes the reference, and takes it through the enclosing instance's layout
descriptor: `__retain_type_param` reads `retainFunc@64` and dispatches (`__str_clone` for a byte record,
`__mm_retain` for a managed aggregate, nothing at all for a trivial instantiation, whose word is 0).

**The alternative convention cannot be satisfied.** If a `returns T` body handed back a BORROW and the
caller took its own reference afterwards, a body returning a freshly created value would have nowhere to
put the reference it already holds: releasing it before the `ret` frees the record the caller is about to
retain, and keeping it leaks one record per call. Ownership of a returned value is therefore a property
of the CALLEE and is settled inside the callee, exactly as it is for a `returns String` function.

## Tests

### A freshly created owned value returned through an opaque `T`

The body returns a value it OWNS through a return type the shared body cannot classify. Handing it back
as a borrow and letting the caller take its own reference leaks the body's — one record per call, which
the leak gate reports as exit 101 while the program still prints the right answer.

<!-- test: fresh-owned-value-returned-through-an-opaque-return -->
```maxon
type Holder uses Element
	export var value as Element

	export static function create(v Element) returns Self
		return Self{value: v}
	end 'create'

	export function remade() returns Element
		return fresh()
	end 'remade'
end 'Holder'

typealias TextHolder = Holder with String

function fresh() returns String
	return "a fresh string long enough to force a heap allocation"
end 'fresh'

function main() returns ExitCode
	let t = TextHolder.create("a held string long enough to force a heap allocation")
	let got = t.remade()
	print("{got}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a fresh string long enough to force a heap allocation
```

### The caller drops the returned value immediately

The result is never bound. A statement-position call still owns the `+1` the body handed over, so the
statement's temporary drop is the one that frees it — a hundred times, which makes both a missed drop
(exit 101) and a doubled one (a poison fault) loud.

<!-- test: opaque-owned-return-dropped-by-the-caller-immediately -->
```maxon
type Holder uses Element
	export var value as Element

	export static function create(v Element) returns Self
		return Self{value: v}
	end 'create'

	export function remade() returns Element
		return fresh()
	end 'remade'
end 'Holder'

typealias TextHolder = Holder with String

function fresh() returns String
	return "a fresh string long enough to force a heap allocation"
end 'fresh'

function main() returns ExitCode
	let t = TextHolder.create("a held string long enough to force a heap allocation")
	var i = 0
	while i < 100 'spin'
		t.remade()
		i = i + 1
	end 'spin'
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
done
```

### An element MOVED OUT of an opaque `Array with T` field is returned

`pop` hands back an element the body OWNS — the array released it. Returning it transfers that one
reference to the caller, whose `let` binding drops it at scope exit. The container still holds the other
element, and drops it when the container does.

<!-- test: opaque-array-element-moved-out-and-returned -->
```maxon
type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function takeOne() returns Element
		return try self.items.pop() otherwise panic("takeOne: empty")
	end 'takeOne'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("alpha string long enough to force a heap allocation")
	sc.push("beta string long enough to force a heap allocation")
	let taken = sc.takeOne()
	print("{taken}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
beta string long enough to force a heap allocation
```

### An owned value returned through a THROWING edge

The ok edge carries the `+1`; the error edge carries no value at all. A hand-off emitted on the wrong
edge either frees a register the throw zeroed or leaves the reference untaken, so both edges are run.

<!-- test: opaque-owned-return-through-a-throwing-edge -->
```maxon
enum MakeError
	refused
end 'MakeError'

type Holder uses Element
	export var value as Element

	export static function create(v Element) returns Self
		return Self{value: v}
	end 'create'

	export function remadeOrThrow(ok bool) returns Element throws MakeError
		if not ok 'refuse'
			throw MakeError.refused
		end 'refuse'
		return fresh()
	end 'remadeOrThrow'
end 'Holder'

typealias TextHolder = Holder with String

function fresh() returns String
	return "a fresh string long enough to force a heap allocation"
end 'fresh'

function main() returns ExitCode
	let t = TextHolder.create("a held string long enough to force a heap allocation")
	let good = try t.remadeOrThrow(true) otherwise panic("remadeOrThrow refused a true request")
	print("{good}\n")
	let bad = try t.remadeOrThrow(false) otherwise "fallback"
	print("{bad}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a fresh string long enough to force a heap allocation
fallback
```

### An owned value returned out of an INTERFACE EXTENSION body

An extension over an interface is the other shared body with an opaque return: it is compiled once for
every conformer and its `Item` is the conformer's associated type. The value it builds is owned there for
the same reason it is owned in a generic type's own method.

<!-- test: opaque-owned-return-from-an-interface-extension -->
```maxon
interface Holder uses Item
	function get() returns Item
end 'Holder'

extension Holder
	function remade() returns Item
		return fresh()
	end 'remade'
end 'Holder'

type TextHolder implements Holder with String
	let held as String

	static function create(held String) returns Self
		return Self{held: held}
	end 'create'

	function get() returns String
		return self.held
	end 'get'
end 'TextHolder'

function fresh() returns String
	return "a fresh string long enough to force a heap allocation"
end 'fresh'

function main() returns ExitCode
	let h = TextHolder.create("a held string long enough to force a heap allocation")
	let got = h.remade()
	print("{got}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a fresh string long enough to force a heap allocation
```

### CONTROL — a BORROWED opaque return still hands back exactly one reference

The other half of the convention, and the one that already worked: the body returns a value the instance
still owns, so the reference the caller drops has to be taken somewhere. A hundred round trips make a
missed reference a poison fault and a doubled one a leak.

<!-- test: borrowed-opaque-return-hands-back-one-reference -->
```maxon
type Holder uses Element
	export var value as Element

	export static function create(v Element) returns Self
		return Self{value: v}
	end 'create'

	export function get() returns Element
		return self.value
	end 'get'
end 'Holder'

typealias TextHolder = Holder with String

function main() returns ExitCode
	let t = TextHolder.create("a held string long enough to force a heap allocation")
	var i = 0
	while i < 100 'spin'
		let got = t.get()
		print("{got}")
		i = i + 1
	end 'spin'
	print("{t.get()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocationa held string long enough to force a heap allocation

```

### CONTROL — a TRIVIAL instantiation returns a raw scalar and allocates nothing

`retainFunc@64` is 0 for an instantiation whose argument owns no record, so the hand-off is inert and the
emitted code for `Holder with Integer` is a bare move. Both provenances are exercised on it.

<!-- test: trivial-instantiation-opaque-return-is-inert -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder uses Element
	export var value as Element

	export static function create(v Element) returns Self
		return Self{value: v}
	end 'create'

	export function get() returns Element
		return self.value
	end 'get'

	export function remade() returns Element
		return 7
	end 'remade'
end 'Holder'

typealias IntHolder = Holder with Integer

function main() returns ExitCode
	let t = IntHolder.create(5)
	print("{t.get()} {t.remade()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 7
```

### COVERAGE — `Iterable.map` over an `Array with String`

The downstream shape this slice was a prerequisite for: `stdlib/Interfaces.maxon`'s `extension Iterable`
builds an `Array with Element` and pushes a transform's result into it, so every element crosses a
`returns Element` boundary out of a body compiled once for every conformer.

⚠ This case is COVERAGE and not a bug capture — it was green before the slice as well. `Array` is a
BUILTIN container whose element accessors shv2 synthesizes rather than compiles, so the elements never
travel through a user `returns T` body; what it pins is that the convention change did not disturb the
route the retirement of the synthesized `Array` will eventually send them down.

<!-- test: iterable-map-over-an-array-of-strings -->
```maxon
typealias Strings = Array with String

function main() returns ExitCode
	var xs = Strings.create()
	xs.push("alpha string long enough to force a heap allocation")
	xs.push("beta string long enough to force a heap allocation")
	let ys = xs.map(function(s) gives s)
	print("{ys.count()}\n")
	for y in ys 'loop'
		print("{y}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
alpha string long enough to force a heap allocation
beta string long enough to force a heap allocation
```
