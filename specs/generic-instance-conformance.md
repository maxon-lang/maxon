---
feature: generic-instance-conformance
status: stable
keywords: [generics, conformance, where, constraint, instance, interface, dictionary-passing]
category: type-system
---

# A GENERIC's Instance Conforms Through Its BASE

## Documentation

Conformance in the compiler is DECLARED and the registry is keyed by the DECLARED struct name — so the
question *"does this type argument satisfy `where N is Named`"* is answered by reducing the argument
to its CONFORMER NAME and looking that name up (`ProgramSignatures.conformerTypeArgName`,
`ConformanceCheck.typeConformsTo`). A ranged alias reduces to its primitive (`Integer` → `int`); a
conforming array reduces to the one `Array` conformer.

**Without a reduction for a generic's INSTANCE**, `Wrapper with Integer` would reduce to its MANGLED
name (`Wrapper_int`) — a name no `implements` clause claims and no intrinsic row lists — and a legal
program would be refused:

```text
error E3017: Type 'IntWrapper' does not satisfy constraint 'Named' required by type parameter 'N' of 'Holder'
```

⇒ **An instance of a DECLARED generic reduces to its base**, because under dictionary-passing the
conformance is a property of the DECLARATION and its impls are ONE shared body: `Wrapper.label` is
compiled once over an opaque layout, and every instantiation dispatches into that one symbol. So one
`__witness_Wrapper.Named` table answers for all of them, exactly as one `__witness_Array.*` pair
answers for every array.

⚠ **The reduction lives on BOTH doors.** A type argument arrives
either as a resolved `genericInstance` (`Holder with (Wrapper with Integer)`) or as the NAME of a
generic-instance typealias (`Holder with IntWrapper`) — `ProgramSignatures.conformerNameOfType` and
`conformerNameOfDeclaredName`, both answering through `instanceConformerReduction`. On the instance door
alone, the nested spelling would compile while the ALIAS spelling — the one a user actually writes — would
stay E3017.

⚠ **A base's own `where` clause is not waived by this.** `type MapIterator uses Key, Value implements
Iterator where Key is Hashable` still has that clause checked at the `MapIterator with (K, V)`
instantiation SITE, by the same `checkOneInstantiation` walk that reports every E3017. The question
answered here is a different one — *does this declaration implement that interface* — and it does not
depend on the arguments. (`Array`'s own reduction IS conditional: `stdlib/Array.maxon` grants
`Hashable`/`Equatable` through `extension Array … where Element is Hashable and Equatable`, a conformance
the name-keyed registry cannot condition, so `instanceConformerReduction` reduces an `Array with E` to
`Array` only when `E` conforms and otherwise leaves it unreduced.)

`Set`, `List` and `Map` are declared generics (`stdlib/Set.maxon`, `stdlib/List.maxon`,
`stdlib/Map.maxon`), so their instances reduce to their base like any other. A base with no declaration
anywhere has no `StructLayout` to declare anything, so its instance keeps its mangled name.

## Tests

<!-- test: instance-alias-satisfies-through-its-base -->
⭐ **THE SPELLING A USER WRITES.** `IntWrapper` names
`Wrapper with Integer`; `Wrapper` declares `implements Named`; so `Holder with IntWrapper` satisfies
`where N is Named`. The program has no stdlib in it, and it prints `wrapped`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Named
	function label() returns String
end 'Named'

type Wrapper uses T implements Named
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function label() returns String
		return "wrapped"
	end 'label'
end 'Wrapper'

type Holder uses N where N is Named
	export var item as N

	static function create(item N) returns Self
		return Self{item: item}
	end 'create'

	function show() returns String
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

```maxon
typealias Integer = int(i64.min to i64.max)

interface Named
	function label() returns String
end 'Named'

type Wrapper uses T implements Named
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function label() returns String
		return "wrapped"
	end 'label'
end 'Wrapper'

type Holder uses N where N is Named
	export var item as N

	static function create(item N) returns Self
		return Self{item: item}
	end 'create'

	function show() returns String
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

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
end 'Plain'

type Holder uses N where N is Named
	export var item as N

	static function create(item N) returns Self
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
