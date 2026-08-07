---
feature: generic-body-doors
status: stable
keywords: [generic, uses, type parameter, field, tuple, diagnostic]
category: type-system
---

# Doors Inside a Generic's Own Body

## Documentation

A generic type's body is compiled ONCE against its type parameters, which are opaque there: the
shared body does not know what `T` will be. Two constructs reach that opacity directly — writing a
CONCRETE value into a field the body declares as `T`, and declaring a field whose type is a TUPLE
over the parameters.

Both are refused today. **What this spec pins is not whether they are legal — it is that the refusal
is a REAL DIAGNOSTIC.** A compiler must answer a program it is given: a stated error code at a stated
position, naming a reason a reader can act on. Two failures are specifically excluded.

**An INTERNAL error is never an answer.** `docs/error-codes.txt` defines the 9xxx band as *"An
internal compiler invariant was violated. This is a compiler bug."* — so emitting one for a program a
user can write is by definition a defect, whatever the verdict on the construct should be.

**A message that compares two spellings of different things is never an answer either.** Reporting
that a field "expects `X`" and "got `Y`" is only meaningful when both sides were derived the same
way; comparing an UNSUBSTITUTED declared type against an ALREADY-LOWERED value names two things the
reader cannot reconcile, and points at the program for a disagreement inside the compiler.

⚠ **This spec deliberately does NOT decide whether either construct should eventually compile.**
Whether a tuple may be keyed by a type parameter is an open design question; if it is settled in
favour, these cases change together with the rule. What may not happen either way is a crash or a
self-contradictory sentence.

## Tests

<!-- test: error.concrete-value-written-into-a-type-parameter-field -->
Writing a concrete `Label` into a field the shared body declares as `T`. The body is compiled once
and does not know that `T` is `Label` — a different instantiation would make the same store wrong —
so this is refused. ⚠ It must be refused with a DIAGNOSTIC: this program previously reached
`KindToTypeName` with a `TypeParameter` and produced `E9001 Unknown value kind: TypeParameter`, an
internal error with a stack trace, which told the reader nothing and told the differential even less.
```maxon
typealias Num = int(i64.min to i64.max)

type Label
	export let n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Label'

type Box uses T
	export var value as T

	static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	export function clobber()
		self.value = Label.create(77)
	end 'clobber'
end 'Box'

typealias LabelBox = Box with Label

function main() returns ExitCode
	var b = LabelBox.create(Label.create(5))
	b.clobber()
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/generic-body-doors/error.concrete-value-written-into-a-type-parameter-field.test:19:16: cannot assign a value of type 'Label' to field 'value' of 'Box', which holds a type parameter
```


<!-- test: error.tuple-field-over-a-generics-own-type-parameters -->
A field whose declared type is a tuple over the generic's own parameters. A tuple's identity is its
element types, and a type parameter cannot key one in a body that does not know it — so this is
refused. ⚠ The refusal must NAME that reason. It previously read `expects '__Tuple2-A-B' but got
'__Tuple2-i64-i64'`: both spellings are the compiler's own, the declared side keeping the parameter
NAMES while the constructed value had already been lowered, so the message reported an internal
disagreement as though it were the program's mistake.
```maxon
typealias Num = int(i64.min to i64.max)

type Pair uses A, B
	export let both as (A, B)

	static function make(a A, b B) returns Self
		return Pair{both: (a, b)}
	end 'make'
end 'Pair'

typealias NumPair = Pair with (Num, Num)

function main() returns ExitCode
	let p = NumPair.make(3, b: 4)
	let t = p.both
	return (t._0 + t._1) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/generic-body-doors/error.tuple-field-over-a-generics-own-type-parameters.test:4:21: a tuple's identity is its element types, so a tuple over the type parameters 'A', 'B' cannot be keyed in a shared generic body
```
