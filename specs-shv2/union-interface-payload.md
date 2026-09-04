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

<!-- disabled-test: error.dispatch-interface-payload -->
<!-- MEASURED 2026-09-04: shv2 refuses the DECLARATION (`E2015` at the payload, with the fat-pointer reason) where
     the pin refuses the DISPATCH (`E4006` at the use). shv2's refusal is earlier and better-reasoned; which site
     the language blames is a ruling. -->
Binding an interface-typed union payload and dispatching a method on it is
rejected with E4006 — the binding has no witness to dispatch against.
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
error E4006: specs/fragments/union-interface-payload/error.dispatch-interface-payload.test:27:19: Variable 'who' is not a struct or enum type
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
