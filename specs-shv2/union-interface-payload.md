---
feature: union-interface-payload
status: experimental
keywords: [union, interface, payload, dispatch, witness, E4006]
category: type-system
---

# Union Interface-Typed Payload

## Documentation

A union case may carry a payload of any concrete type, but **not** an interface
type. An interface value is a 16-byte fat pointer — a value half plus a witness
half that carries the method table used for dynamic dispatch. A union stores
only the value half of its payload, so an interface-typed payload loses its
witness the moment it is placed in the union. When such a payload is later bound
in a match arm (`live(who) gives ...`), the binding `who` has no witness, and
any method or field access on it (`who.volume()`) cannot be dispatched.

The compiler rejects binding an interface-typed union payload with E4006. To
carry a dynamically-dispatched value, hold the concrete conformer type in the
payload, or store the interface value in a struct field (struct fields keep both
halves of the fat pointer).

A payload of a **concrete** type — including a struct — is unaffected: the
binding retains its type and can be used normally.

## Tests

<!-- test: error.dispatch-interface-payload -->
⚠ **shv2 REFUSES THE DECLARATION, NOT THE DISPATCH.** The bootstrap admits the payload and fails at the
`who.volume()` that has no witness to dispatch against (`E4006`); shv2 refuses `live(who Speaker)` itself,
and the reason is what makes the earlier site the right one: a value held at an interface type is a two-word
`(value, witness)` fat pointer, a payload slot is ONE machine word, and unlike a struct field it cannot
widen — every case of a union shares one slot region. That is a fact about the DECLARATION, so no use of it
could ever have worked and the diagnostic that names it is the one an author can act on.
```maxon
typealias Vol = int(0 to 1000)

interface Speaker
	function volume() returns Vol
end 'Speaker'

type Quiet implements Speaker
	export var tag as Vol

	static function create() returns Quiet
		return Self{tag: 0}
	end 'create'

	function volume() returns Vol
		return 3
	end 'volume'
end 'Quiet'

union Sound
	live(who Speaker)
	silent
end 'Sound'

function speak(s Sound) returns Vol
	return match s 'm'
		live(who) gives who.volume()
		silent gives 0
	end 'm'
end 'speak'

function main() returns ExitCode
	return speak(Sound.live(Quiet.create()))
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:11: Unsupported: a union case payload declared at the interface type 'Speaker' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a payload slot is one machine word, and unlike a struct field it cannot widen — every case of a union shares one slot region. Declare it at a concrete type, or take the interface as a PARAMETER of a plain function, which carries its witness as an adjacent argument
```

<!-- test: struct-payload-dispatch -->
A union payload of a concrete struct type keeps its type when bound; a field
access on the binding resolves normally. Only interface-typed payloads are
rejected.
```maxon
typealias Val = int(0 to 1000)

type Wrapped
	export var n as Val

	static function create(x Val) returns Wrapped
		return Self{n: x}
	end 'create'
end 'Wrapped'

union Holder
	full(w Wrapped)
	empty
end 'Holder'

function get(h Holder) returns Val
	return match h 'm'
		full(w) gives w.n
		empty gives 0
	end 'm'
end 'get'

function main() returns ExitCode
	return get(Holder.full(Wrapped.create(7)))
end 'main'
```
```exitcode
7
```
