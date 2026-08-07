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
eventually be constructible inside the shared body.** That is an open design question, and this
compiler does not currently answer it consistently: a `return` of such a tuple is ACCEPTED, by an arm
that exists so `stdlib/Map.maxon`'s `MapIterator.current()` compiles, while a field initializer, a
field assignment and a call argument all refuse it. What may not happen either way is a crash or a
self-contradictory sentence, and those are what the cases below pin.

⚠ **A diagnostic must not state WHERE the value came from unless it knows.** A tuple field written
inline over the parameters is not substituted when the generic is instantiated, so the same refusal
is reached from `main` — with no generic body anywhere in the program. A sentence about the two
DERIVATIONS is true at every site that reaches it; a sentence about a "shared generic body" is not.

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
error E3005: specs/fragments/generic-body-doors/error.concrete-value-written-into-a-type-parameter-field.test:20:8: a value of type 'Label' cannot meet field 'value' of 'Box', which holds the type parameter 'T': one body serves every instantiation, so the type 'T' stands for is not known here
```


<!-- test: error.concrete-value-passed-to-a-type-parameter-parameter -->
The same store one door along: a concrete `Label` handed to a parameter the shared body declares as
`T`. ⚠ It compiled CLEAN, and passed a `Label` heap pointer into a slot the instantiation had fixed
as an integer — measured on `Box with Num`, the same program printing `r=2283682242640`, a pointer
rendered as a number, with no diagnostic anywhere. That is the SILENCE this spec's documentation
excludes, and it is the twin of the crash above: one door refused every program, its mirror accepted
every program.

⚠ **The door cannot decide this in general, and must not try.** `stdlib/Interfaces.maxon:230` hands
a concrete `ByteIterator` to a parameter declared `Source` and is CORRECT, because `Source` is bound
two levels up to exactly that iterator; refusing by the value's runtime aggregate identity turns the
stdlib build red. What it decides instead is the one case where the binding question answers itself:
the callee is a method of the very type whose body we are in, and the parameter is that type's OWN
parameter, still unbound. There is no binding, there cannot be one, and one body serves every
instantiation — so no concrete aggregate can be that `T`. Measured: 11 candidate sites in the
stdlib and 3495 committed spec fragments, zero refusals among them.
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
error E3005: specs/fragments/generic-body-doors/error.concrete-value-passed-to-a-type-parameter-parameter.test:24:10: a value of type 'Label' cannot meet argument 'v', which holds the type parameter 'T': one body serves every instantiation, so the type 'T' stands for is not known here
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

⚠ **The sentence names the two DERIVATIONS and deliberately does not say where the value was
built** — see the next case, which reaches the same refusal from `main`.
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
error E3005: specs/fragments/generic-body-doors/error.tuple-built-over-a-generics-own-type-parameters.test:8:15: a tuple value is named by its elements' storage types, and a tuple declared over a generic's type parameters is named by those parameters, so no tuple value can meet field 'both' of 'Pair', declared '(A, B)'
```


<!-- test: error.tuple-refused-at-an-instantiation-site-too -->
The SAME refusal, reached from `main`, where there is no shared generic body anywhere in sight. ⚠ An
inline tuple field type is NOT substituted when the generic is instantiated: `NumPair`'s `both` keeps
the type `__Tuple2-A-B`, so a tuple literal written at top level meets a declared type still spelled
in `Pair`'s parameters and is refused by the same door.

Both top-level lines are refused this way — the reported one is the CALL, because the compiler stops
at the first error; the assignment on the next line produces the same sentence with
`field 'both' of 'NumPair'` in place of `argument 't'`.

⚠ **This program is CORRECT and its refusal is a known limitation, not a verdict.** `NumPair` is
`Pair with Num, Num`, so `(1, 2)` and `(7, 8)` are exactly what `both` holds. What this case pins is the SENTENCE:
the message must state the two derivations and nothing about WHERE the value was built. It read *"a
tuple built inside a shared generic body …"* and said that of this program, which is simply false —
the same mistake, one level down, as refusing the DECLARATION would have been. Spelling the
declared side as a NAMED alias does not lift the refusal either, it only moves the spellings
(`__Tuple2-A-B_Num_Num` against `__Tuple2-i64-i64`), so lifting it is a type-identity change rather
than a diagnostic's.
```maxon
typealias Num = int(i64.min to i64.max)

type Pair uses A, B
	export var both as (A, B)

	static function make(t (A, B)) returns Self
		return Self{both: t}
	end 'make'
end 'Pair'

typealias NumPair = Pair with Num, Num

function main() returns ExitCode
	var p = NumPair.make((1, 2))
	p.both = (7, 8)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/generic-body-doors/error.tuple-refused-at-an-instantiation-site-too.test:15:18: a tuple value is named by its elements' storage types, and a tuple declared over a generic's type parameters is named by those parameters, so no tuple value can meet argument 't', declared '(A, B)'
```
