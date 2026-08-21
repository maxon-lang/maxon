---
feature: countof
status: stable
keywords: [countof, vector, fixed size, generics, monomorphization]
category: intrinsic
---

# `countof(Type)` — how many elements a fixed-size container type holds

## Documentation

`countof(T)` is [`sizeof(T)`](sizeof.md)'s twin one axis over: `sizeof` answers in BYTES PER
VALUE, `countof` in ELEMENTS PER INSTANCE. It is defined for exactly the types whose element
count is part of their identity — a generic instance applied to a count, which the `with N Type`
form writes: `Vector with 3 Int` (see [vector](vector.md)), and equally any other generic
instantiated that way. A growable `Array`'s length is a runtime field of the record rather than a
coordinate of its type, so `countof` of one is refused; ask the value for its `count()`.

**Signature:** `countof(TypeName) int`

The result is a compile-time integer constant in every case — there is no runtime read.

### Two arrival points, because the count is a coordinate of the INSTANCE

- **An operand that states its own count** — `Vector with 3 Int`, and every alias for it — is
  folded to a literal by the parser, where the instance registry lives.
- **`Self`, or the enclosing declaration's own name, inside a generic body** states no count on
  its own: `Self` inside `type Vector uses Element` is the declaration, and the same body is
  compiled for `Vector with 3 Int` and for `Vector with 5 Int`. The read therefore DEFERS, and
  monomorphization — which compiles one copy of the body per instance — substitutes the instance
  the copy is for. The fold happens there, so the answer is still a literal.

### ⭐ WHERE THIS COMPILER AND `maxon-shv2` PART COMPANY — same answers, different mechanism

Both compilers answer every question below identically where both can answer it, and the reason
they differ at the edges is one fact: **this compiler MONOMORPHIZES and shv2 does not.**

- **shv2 passes the count in a hidden trailing parameter** that every call site fills in from the
  instance it holds, because one shared body serves every size there. That parameter has to be
  reserved by a token pre-scan before any body is parsed, so shv2 recognizes the operand by its
  SPELLING and refuses the shapes that pre-scan cannot see. Here there is no shared body and no
  hidden parameter: the copy of the body already knows its instance, so every such shape is simply
  answered. `a-local-bound-to-a-Self-typed-parameter-reaches-the-count` below is one shv2 refuses.
- **shv2 refuses `countof(Self)` inside an ordinary generic outright**, because it can only ask
  whether the enclosing declaration is the sized container. Here the question that decides the
  answer is which INSTANCE the body was compiled for, and that is not known until monomorphization
  — so an ordinary generic applied to a count answers it
  (`an-ordinary-generic-applied-to-a-count-answers-it`), and one applied to type arguments only is
  refused at the instance rather than at the declaration
  (`error.an-ordinary-generics-instance-states-no-count`). The refusal names the instance.
- **The refusal carries its own code here** (E2071 / E2072) where shv2 spends its `E2015`
  unsupported-construct catch-all, so `maxon error-codes` can state the one meaning of each. It
  also points at the OPERAND rather than at the `countof` token, which is what shv2 points at: what
  is wrong is the type being asked, and the sibling `Unknown type in countof:` refusal on the very
  same token already points there — two diagnostics about one token disagreeing on its column
  would be the defect.

`countof(Self)` inside a CLOSURE is refused by both, and for the same underlying reason in two
dialects: a closure body is lifted to its own top-level function that is not compiled per generic
instance, so there is no instance to read the count from.

### `countof` in a `static`, where `sizeof(Self)` is refused

`sizeof` deliberately does not resolve `Self` at all: folding it would hand a body that is
compiled per instance the enclosing TEMPLATE's layout. `countof` resolves it because it has
somewhere to send it — monomorphization — and because a fixed-size container's `create()` is a
`static function` whose whole answer is the size.

## Tests

<!-- test: a-stated-count-folds-to-a-literal -->
An operand that states its own count is a compile-time constant, exactly as a concrete `sizeof`
is. No instance, no body, no substitution — the type alone is the answer.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	return countof(Vec3)
end 'main'
```
```exitcode
3
```

<!-- test: countof-folds-in-expression-position -->
The result is an ordinary compile-time integer, so it composes like one. Both operands fold
before the addition does.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
typealias Vec5 = Vector with 5 Int

function main() returns ExitCode
	let total = countof(Vec3) + countof(Vec5)
	return total
end 'main'
```
```exitcode
8
```

<!-- test: each-instance-answers-its-own-count -->
`capacity()` is written once over `type Vector uses Element`, whose `Self` states no count.
Monomorphization compiles one copy per instance and each copy folds its own.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
typealias Vec5 = Vector with 5 Int

extension Vector
	export function capacity() returns Int
		return countof(Self)
	end 'capacity'
end 'Vector'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec5.create()
	return a.capacity() * 10 + b.capacity()
end 'main'
```
```exitcode
35
```

<!-- test: the-containers-own-name-means-the-same-as-Self -->
`Vector` written inside `Vector`'s own body denotes exactly what `Self` denotes there, so it is
the same operand and defers the same way. A check filed against one of the two spellings would be
narrower than the fact it is about.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function byName() returns Int
		return countof(Vector)
	end 'byName'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.byName()
end 'main'
```
```exitcode
3
```

<!-- test: a-sized-field-of-another-generic-answers-its-own-count -->
⭐ **THE CASE THAT DECIDES WHERE THE COUNT COMES FROM.** `Holder`'s field is a
`Vector with 4 Element` — a sized instance written over the ENCLOSING generic's own type
parameter. The count is a coordinate of THAT instance and stays 4 whatever `Element` turns out to
be, while `Holder`'s own instance states no count at all. Reading the count off the enclosing
generic would answer nothing here; reading it off the operand's instance answers 4.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntHolder = Holder with Int

extension Vector
	export function capacity() returns Int
		return countof(Self)
	end 'capacity'
end 'Vector'

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.capacity()
	end 'size'
end 'Holder'

function main() returns ExitCode
	var h = IntHolder.create()
	return h.size()
end 'main'
```
```exitcode
4
```

<!-- test: a-sized-inner-alias-is-an-operand-in-its-own-right -->
The field alias of the case above, asked directly. `Slot` names an instance that states 4, so it
folds at the parse with no deferral at all — `countof` is about the operand's instance, not about
where the operand is written.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntHolder = Holder with Int

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return countof(Slot)
	end 'size'
end 'Holder'

function main() returns ExitCode
	var h = IntHolder.create()
	return h.size()
end 'main'
```
```exitcode
4
```

<!-- test: the-count-reaches-a-Self-typed-parameter -->
`other Self` binds to an instance of the very type the receiver is — a differently-sized argument
is a different type and is refused outright — so the copy of `capacity()` the call reaches is the
receiver's own.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function capacity() returns Int
		return countof(Self)
	end 'capacity'

	export function pairWith(other Self) returns Int
		return other.capacity()
	end 'pairWith'
end 'Vector'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec3.create()
	return a.pairWith(b)
end 'main'
```
```exitcode
3
```

<!-- test: a-local-bound-to-a-Self-typed-parameter-reaches-the-count -->
⭐ **A SHAPE `maxon-shv2` REFUSES AND THIS COMPILER ANSWERS.** There, `var same = other` re-binds
the value under a name the token pre-scan never recorded, and the hidden count parameter has
nowhere to come from. Here the whole body is already compiled for one instance, so a local
binding is just a local binding.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function capacity() returns Int
		return countof(Self)
	end 'capacity'

	export function pairWith(other Self) returns Int
		var same = other
		return same.capacity()
	end 'pairWith'
end 'Vector'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec3.create()
	return a.pairWith(b)
end 'main'
```
```exitcode
3
```

<!-- test: the-count-reaches-a-chained-call -->
An UNNAMED receiver: `self.dup()` hands back a value of the enclosing type with no binding to hold
it, and `.capacity()` chains straight onto it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function dup() returns Self
		return Self{}
	end 'dup'

	export function capacity() returns Int
		return countof(Self)
	end 'capacity'

	export function chained() returns Int
		return self.dup().capacity()
	end 'chained'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.chained()
end 'main'
```
```exitcode
3
```

<!-- test: an-ordinary-generic-applied-to-a-count-answers-it -->
⭐ **THE COUNT IS A COORDINATE OF THE INSTANCE, NOT A PRIVILEGE OF ONE DECLARATION.** `Box` is an
ordinary generic with no buffer and no container interface; written `Box with 3 Int` its instance
states 3, and that is the whole of what `countof` asks. `maxon-shv2` refuses this program at the
declaration, because without monomorphization it can only ask whether the enclosing declaration is
the sized container it knows by name.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Box3 = Box with 3 Int

type Box uses T
	export var value as T

	export static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	export function slots() returns Int
		return countof(Self)
	end 'slots'
end 'Box'

function main() returns ExitCode
	let b = Box3.create(1)
	return b.slots()
end 'main'
```
```exitcode
3
```

<!-- test: a-static-reads-the-count -->
The case `sizeof(Self)` cannot serve. `counted()` is a `static function` — no receiver, no `self`
— and it reads its own instance's count as a LOOP BOUND, so the global counter ends at exactly the
count and shows a receiverless body got the right one. The answer is `b.value` (1) plus `trips`
(3): a count read as 0 would not merely be wrong, it would leave the loop unrun and the counter at
its initial value, which is a different number from every count.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Box3 = Box with 3 Int

var trips = 0

type Box uses T
	export var value as T

	export static function counted(value T) returns Self
		var i = 0
		while i < countof(Self) 'each'
			trips = trips + 1
			i = i + 1
		end 'each'
		return Self{value: value}
	end 'counted'
end 'Box'

function main() returns ExitCode
	let b = Box3.counted(1)
	return (b.value + trips) as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: error.a-type-that-states-no-element-count -->
A count is a coordinate of the INSTANCE, so only a type that has one can be asked. A ranged alias
has no elements at all, and answering 0 would be a sentinel a reader cannot tell from a real
count.
```maxon
typealias Int = int(i64.min to i64.max)

function main() returns ExitCode
	return countof(Int)
end 'main'
```
```maxoncstderr
error E2071: specs/fragments/countof/error.a-type-that-states-no-element-count.test:5:17: countof of 'Int', which states no element count — only a generic instance applied to a count has one, which the `with N Type` form writes (`Vector with 3 Int`). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
```

<!-- test: error.a-growable-array-has-no-count-in-its-type -->
The same refusal for the container it is easiest to expect an answer from. An `Array`'s length
lives in the record and changes as the program runs, so it is not something the TYPE can be asked;
`arr.count()` is.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Ints = Array with Int

function main() returns ExitCode
	return countof(Ints)
end 'main'
```
```maxoncstderr
error E2071: specs/fragments/countof/error.a-growable-array-has-no-count-in-its-type.test:6:17: countof of 'Ints', which states no element count — only a generic instance applied to a count has one, which the `with N Type` form writes (`Vector with 3 Int`). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
```

<!-- test: error.a-countless-inner-alias-of-the-container-itself -->
⭐ **AN ALIAS IS ITS OWN OPERAND, EVEN INSIDE THE CONTAINER.** `Me` names `Vector with Element` —
the container over an unbound element and no count — so it is not `Self`, it does not defer, and
it is refused at the parse for the plain reason that it states no count. `maxon-shv2` refuses the
identical program because its token pre-scan cannot see through the alias; the two compilers agree
on the answer without agreeing on why.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	typealias Me = Vector with Element

	export function viaAlias() returns Int
		return countof(Me)
	end 'viaAlias'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.viaAlias()
end 'main'
```
```maxoncstderr
error E2071: specs/fragments/countof/error.a-countless-inner-alias-of-the-container-itself.test:9:18: countof of 'Me', which states no element count — only a generic instance applied to a count has one, which the `with N Type` form writes (`Vector with 3 Int`). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
```

<!-- test: error.an-ordinary-generics-instance-states-no-count -->
⭐ **THE REFUSAL LANDS AT THE INSTANCE, WHICH IS WHERE THE ANSWER WOULD HAVE COME FROM.** The
program is `an-ordinary-generic-applied-to-a-count-answers-it` with the count taken off the
instantiation, and nothing else changed — which is exactly why the declaration cannot be what is
refused. Monomorphization compiles `slots()` for `IntBox`, finds that instance states no count,
and reports it against the `countof` the copy came from, naming the instance so the two
instantiations of one declaration are told apart.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntBox = Box with Int

type Box uses T
	export var value as T

	export static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	export function slots() returns Int
		return countof(Self)
	end 'slots'
end 'Box'

function main() returns ExitCode
	let b = IntBox.create(1)
	return b.slots()
end 'main'
```
```maxoncstderr
error E2071: specs/fragments/countof/error.an-ordinary-generics-instance-states-no-count.test:13:18: countof of the enclosing generic, in a body compiled for 'IntBox' — that instance states no element count. A count is a coordinate of the INSTANCE, and this one was applied to type arguments only: instantiate it with a count (`Box with 3 Int`) or read a runtime length instead
```

<!-- test: error.countof-inside-a-closure -->
⭐ **A CLOSURE IS A DIFFERENT FUNCTION, AND THE REFUSAL SAYS SO WITH A LINE.** A closure body is
lifted to its own top-level function, and that function is emitted ONCE rather than compiled per
generic instance — so there is no instance to read the count from, however the enclosing method
was instantiated. Refused at the parse, because the alternative is discovering the unsubstituted
operand two passes later with no source position at all.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function viaClosure() returns Int
		let f = function() gives countof(Self)
		return f()
	end 'viaClosure'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.viaClosure()
end 'main'
```
```maxoncstderr
error E2072: specs/fragments/countof/error.countof-inside-a-closure.test:7:36: countof of 'Vector' inside a closure — a closure is lifted to its own top-level function, which is emitted once rather than compiled per generic instance, so the enclosing instance's element count is not in scope there. Read `countof(Self)` into a binding outside the closure and capture that
```

<!-- test: error.an-unknown-type -->
The operand is a type name, and a name no declaration binds is not one. The message names the
intrinsic that was reading it, so a `countof` typo does not report itself as a `sizeof` one.
```maxon
function main() returns ExitCode
	return countof(Nope)
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/countof/error.an-unknown-type.test:3:17: Unknown type in countof: 'Nope'
```

<!-- test: error.Self-outside-a-type-declaration -->
`Self` denotes the enclosing declaration, so outside one it denotes nothing. The same refusal
every other `Self` type position gives.
```maxon
function main() returns ExitCode
	return countof(Self)
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/countof/error.Self-outside-a-type-declaration.test:3:17: 'Self' can only be used inside a type declaration
```
