---
feature: generic-body-doors
status: stable
keywords: [generic, uses, type parameter, field, tuple, diagnostic]
category: type-system
---

# Doors Inside a Generic's Own Body

## Documentation

A generic type's body is compiled ONCE against its type parameters, which are opaque there: the
shared body does not know what `T` will be. A value whose declared type IS a parameter is therefore
erased to its storage representation before any door sees it — a `T` is an i64 like any other — and
every door that asks "does this value's type match the declared one?" has to answer without the one
fact it would need.

**What this spec pins is not whether a construct is legal — it is that the answer is a REAL
DIAGNOSTIC or a REAL COMPILE.** A compiler must answer the program it is given: a stated error code
at a stated position, naming a reason a reader can act on. Three failures are specifically excluded.

**An INTERNAL error is never an answer.** `docs/error-codes.txt` defines the 9xxx band as *"An
internal compiler invariant was violated. This is a compiler bug."* — so emitting one for a program a
user can write is by definition a defect, whatever the verdict on the construct should be.

**A message that compares two spellings of different things is never an answer either.** Reporting
that a field "expects `X`" and "got `Y`" is only meaningful when both sides were derived the same
way; comparing an UNSUBSTITUTED declared type against an ALREADY-LOWERED value names two things the
reader cannot reconcile, and points at the program for a disagreement inside the compiler.

**And SILENCE is not an answer.** A door that skips its check because it cannot see through the
erasure lets a concrete value through into a slot that is not its type, and the wrong answer arrives
with no diagnostic at all. That is the same defect as the crash, wearing the opposite disguise: one
door refused every program including the correct ones, while its twin accepted every program
including the wrong ones.

⚠ **This spec deliberately does NOT decide whether a tuple over a generic's own parameters should
eventually be constructible inside the shared body.** That is an open design question. What may not
happen either way is a crash or a self-contradictory sentence.

## Tests

<!-- test: type-parameter-value-assigned-into-a-type-parameter-field -->
The ordinary generic setter: a value whose declared type is `T`, stored into a field whose declared
type is `T`. Nothing about it is in question — it is the shape every generic container is written in
— and it must COMPILE. ⚠ It did not. Because a `T`-typed value is erased to an i64, the assignment
door's KIND comparison could never hold (`DetermineValueKind` has no `TypeParameter` result at all),
so this program fell straight through to `E9001 Unknown value kind: TypeParameter`. The door had no
passing path: every assignment into a type-parameter field was an internal error, correct ones
included.
```maxon
typealias Num = int(i64.min to i64.max)

type Box uses T
	export var value as T

	static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	export function put(v T)
		self.value = v
	end 'put'

	export function get() returns T
		return value
	end 'get'
end 'Box'

typealias NumBox = Box with Num

function main() returns ExitCode
	var b = NumBox.create(1)
	b.put(4)
	return b.get() as ExitCode
end 'main'
```
```exitcode
4
```


<!-- test: error.concrete-value-written-into-a-type-parameter-field -->
Writing a concrete `Label` into a field the shared body declares as `T`. The body is compiled once
and does not know what `T` stands for — a different instantiation would make the same store wrong —
so this is refused. ⚠ It must be refused with a DIAGNOSTIC naming the parameter: this program
previously reached `KindToTypeName` with a `TypeParameter` and produced `E9001 Unknown value kind:
TypeParameter`, an internal error with a stack trace, which told the reader nothing and told the
differential even less.
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
error E3005: specs/fragments/generic-body-doors/error.concrete-value-written-into-a-type-parameter-field.test:19:16: a value of type 'Label' cannot meet field 'value' of 'Box', which holds the type parameter 'T': one body serves every instantiation, so the type 'T' stands for is not known here
```


<!-- test: error.concrete-value-passed-to-a-type-parameter-parameter -->
The same store one door along: a concrete `Label` handed to a parameter the shared body declares as
`T`. It is the identical question and must get the identical answer. ⚠ It got the opposite one. The
argument door could not resolve `T` against a concrete instantiation from inside the body, so it
SKIPPED the check entirely and compiled clean — passing a `Label` heap pointer into a slot the
instantiation had fixed as an integer. Measured on `Box with Num`: the program built without a
diagnostic and the pointer surfaced as the return value, where only an unrelated `ExitCode` range
guard turned it into a panic instead of a wrong number.
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

	export function put(v T) returns T
		return v
	end 'put'

	export function clobber() returns T
		return put(Label.create(77))
	end 'clobber'
end 'Box'

typealias NumBox = Box with Num

function main() returns ExitCode
	var b = NumBox.create(1)
	return b.clobber() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/generic-body-doors/error.concrete-value-passed-to-a-type-parameter-parameter.test:23:10: a value of type 'Label' cannot meet argument 'v', which holds the type parameter 'T': one body serves every instantiation, so the type 'T' stands for is not known here
```


<!-- test: error.tuple-built-over-a-generics-own-type-parameters -->
Constructing a tuple over the generic's own parameters inside the shared body. ⚠ The DECLARATION is
not what is refused, and must not be: `stdlib/Map.maxon` declares `typealias Entry = (Key, Value)`
inside `type Map uses Key, Value`, `MapIterator.current()` returns one, and every dictionary literal
in the language rests on it. What cannot be expressed is the tuple VALUE — a tuple literal's
structural name is minted from its elements' storage types, and a `T`-typed element is an i64 by the
time the mint sees it. ⚠ The refusal must NAME that. It previously read `expects '__Tuple2-A-B' but
got '__Tuple2-i64-i64'`: both spellings are the compiler's own, the declared side keeping the
parameter NAMES while the constructed value had already been lowered, so the message reported an
internal disagreement as though it were the program's mistake.
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
error E3005: specs/fragments/generic-body-doors/error.tuple-built-over-a-generics-own-type-parameters.test:8:15: a tuple built inside a shared generic body carries its elements' storage types, not the type parameters they came from, so it cannot meet field 'both' of 'Pair', declared '(A, B)'
```
