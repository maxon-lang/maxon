---
feature: generic-instance-conformance
status: stable
keywords: [generics, conformance, where, constraint, instance, interface, dictionary-passing]
category: type-system
---

# A GENERIC's Instance Conforms Through Its BASE

## Documentation

Conformance in shv2 is DECLARED and the registry is keyed by the DECLARED struct name — so the
question *"does this type argument satisfy `where N is Named`"* is answered by reducing the argument
to its CONFORMER NAME and looking that name up (`ProgramSignatures.conformerTypeArgName`,
`ConformanceCheck.typeConformsTo`). A ranged alias reduces to its primitive (`Integer` → `int`); a
conforming array reduces to the one `Array` conformer.

**A generic's INSTANCE had no reduction at all**, so `Wrapper with Integer` reduced to its MANGLED
name (`Wrapper_int`) — a name no `implements` clause claims and no intrinsic row lists — and a legal
program was refused:

```text
error E3017: Type 'IntWrapper' does not satisfy constraint 'Named' required by type parameter 'N' of 'Holder'
```

⇒ **An instance of a DECLARED generic reduces to its base**, because under dictionary-passing the
conformance is a property of the DECLARATION and its impls are ONE shared body: `Wrapper.label` is
compiled once over an opaque layout, and every instantiation dispatches into that one symbol. So one
`__witness_Wrapper.Named` table answers for all of them, exactly as one `__witness_Array.*` pair
answers for every array.

⚠ **The reduction lives on BOTH doors, and the first cut had it on one.** A type argument arrives
either as a resolved `genericInstance` (`Holder with (Wrapper with Integer)`) or as the NAME of a
generic-instance typealias (`Holder with IntWrapper`) — the same split
`instanceReducesToArrayConformer` already exists for. Fixed on the instance door alone, the nested
spelling compiled while the ALIAS spelling — the one a user actually writes, and the only one the
bootstrap oracle can parse at all — stayed E3017.

⚠ **A base's own `where` clause is not waived by this.** `type MapIterator uses Key, Value implements
Iterator where Key is Hashable` still has that clause checked at the `MapIterator with (K, V)`
instantiation SITE, by the same `checkOneInstantiation` walk that reports every E3017. The question
answered here is a different one — *does this declaration implement that interface* — and it does not
depend on the arguments. (`Array`'s own reduction IS conditional, and only because `Array` has no
declaration to hang a `where` clause on: shv2 synthesizes it, so its conditional conformance has
nowhere else to live.)

A BUILTIN base — `Array`, `Set`, `List`, and `Map` while it is still synthesized — has no
`StructLayout` to declare anything and keeps the mangled name it always had.

## Tests

<!-- test: instance-alias-satisfies-through-its-base -->
⭐ **THE SPELLING A USER WRITES, and the one the oracle agrees on.** `IntWrapper` names
`Wrapper with Integer`; `Wrapper` declares `implements Named`; so `Holder with IntWrapper` satisfies
`where N is Named`. MEASURED at `HEAD~`: `E3017: Type 'IntWrapper' does not satisfy constraint
'Named' required by type parameter 'N' of 'Holder'`, on a program with no stdlib in it. Both
compilers now print `wrapped`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Named
	function label() returns String
end 'Named'

type Wrapper uses T implements Named
	export var v as T

	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	export function label() returns String
		return "wrapped"
	end 'label'
end 'Wrapper'

type Holder uses N where N is Named
	export var item as N

	export static function create(item N) returns Self
		return Self{item: item}
	end 'create'

	export function show() returns String
		return item.label()
	end 'show'
end 'Holder'

typealias IntWrapper = Wrapper with Integer
typealias WrapHolder = Holder with IntWrapper

function main() returns ExitCode
	let h = WrapHolder.create(IntWrapper.create(5))
	print(h.show())
	return 0
end 'main'
```
```stdout
wrapped
```

<!-- test: nested-instance-spelling-satisfies-too -->
**THE OTHER DOOR.** The same program with the instance spelled INLINE at the constrained position
rather than behind an alias. It is the shape `stdlib/Interfaces.maxon` mints when an `Iterable`
conformance is expanded onto a generic — `WithIterIterator with (MapIterator with (Key, Value), …)` —
which is why it is pinned beside the alias spelling rather than treated as a curiosity.

⚠ **NOT VALIDATED AGAINST THE ORACLE, and that is a property of the oracle rather than of this
answer**: the C# bootstrap cannot parse a nested generic instance in a type-argument position at all
(`error E2003: Expected type name`). The case above carries the cross-compiler agreement; this one
carries the door.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Named
	function label() returns String
end 'Named'

type Wrapper uses T implements Named
	export var v as T

	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	export function label() returns String
		return "wrapped"
	end 'label'
end 'Wrapper'

type Holder uses N where N is Named
	export var item as N

	export static function create(item N) returns Self
		return Self{item: item}
	end 'create'

	export function show() returns String
		return item.label()
	end 'show'
end 'Holder'

typealias IntWrapper = Wrapper with Integer
typealias WrapHolder = Holder with (Wrapper with Integer)

function main() returns ExitCode
	let h = WrapHolder.create(IntWrapper.create(5))
	print(h.show())
	return 0
end 'main'
```
```stdout
wrapped
```

<!-- test: error.a-generic-that-implements-nothing-is-still-refused -->
**THE CONTROL THAT KEEPS THE REDUCTION HONEST.** Reducing an instance to its base decides WHICH name
the conformance registry is asked about; it does not decide the answer. `Plain` is a generic that
declares no `implements`, so `Holder with PlainBox` is refused exactly as an ordinary non-conforming
struct is — and a reduction that had accepted it would have made `where` clauses unenforceable for
every generic argument in the language.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Named
	function label() returns String
end 'Named'

type Plain uses T
	export var v as T

	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'
end 'Plain'

type Holder uses N where N is Named
	export var item as N

	export static function create(item N) returns Self
		return Self{item: item}
	end 'create'
end 'Holder'

typealias PlainBox = Plain with Integer
typealias WrapHolder = Holder with PlainBox

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3017: <fragment>:25:11: Type 'PlainBox' does not satisfy constraint 'Named' required by type parameter 'N' of 'Holder'
```
