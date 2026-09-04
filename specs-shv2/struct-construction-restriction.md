---
feature: struct-construction-restriction
status: experimental
keywords: [struct, literal, construction, factory, encapsulation, E3076]
category: type-system
---

# Struct Construction Restriction

## Documentation

A struct literal (`Type{field: value, ...}`) may only be written **inside the
type's own methods**. Restricting construction to a single set of trusted call
sites lets a type enforce its invariants at construction time; external callers
must go through a `static function create(...)` factory instead. Constructing a
type from anywhere else — a top-level function, or an unrelated type's method —
is rejected with E3076.

The restriction has these exemptions, all of which keep legitimate construction
compiling:

- `Self{...}` inside any of the type's own methods.
- `Type{...}` written inside `Type`'s own body.
- An inner alias of the body being parsed that names a container this compiler
  owns (e.g. `Map`'s `typealias KeyArray = Array with Key`, constructed as
  `KeyArray{}` in `Map`'s methods). The exemption stops there: an inner alias
  naming a USER generic keeps E3076, because a type's construction-time
  invariants would otherwise be void against any type willing to declare an
  alias to it, and a TOP-LEVEL alias keeps E3076 for the same reason. Both take
  `<Alias>.create(...)` instead. `specs-shv2/inner-alias-construction.md` pins
  every one of these directions.

Compiler builtin managed types (`__ManagedMemory`, `__ManagedFile`, ...) are
never constructible with a struct literal and report E3072 instead.

## Tests

<!-- test: error.external-construction -->
Constructing a type with a struct literal outside its own methods (here, in
top-level `main`) is rejected with E3076.
```maxon
typealias Val = int(0 to 100)

type Box
	export var v as Val
end 'Box'

function main() returns ExitCode
	let b = Box{v: 5}
	return b.v
end 'main'
```
```maxoncstderr
error E3076: specs/fragments/struct-construction-restriction/error.external-construction.test:9:14: type 'Box' can only be constructed from within its own methods; use a static factory method instead
```

<!-- test: construct-inside-factory -->
Construction inside the type's own static factory (`Self{...}`) and instance
method use is allowed and runs normally.
```maxon
typealias Val = int(0 to 100)

type Box
	export var v as Val

	static function create(x Val) returns Box
		return Self{v: x}
	end 'create'

	function get() returns Val
		return self.v
	end 'get'
end 'Box'

function main() returns ExitCode
	let b = Box.create(5)
	return b.get()
end 'main'
```
```exitcode
5
```
