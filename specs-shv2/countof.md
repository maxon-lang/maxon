---
feature: countof
status: experimental
keywords: [countof, vector, fixed size, generics, dictionary passing]
category: generics
---

# `countof(Type)` — how many elements a fixed-size container type holds

## Documentation

`countof(T)` is `sizeof(T)`'s twin one axis over: `sizeof` answers in BYTES PER VALUE,
`countof` in ELEMENTS PER INSTANCE. It is defined for exactly the types whose element count
is part of their identity — today the fixed-size container, `Vector with N Element` (see
[vector](vector.md)). A growable `Array`'s length is a runtime field of the record rather than
a coordinate of its type, so `countof` of one is refused; ask the value for its `count()`.

### Two outcomes, decided by whether the operand states a count

- **An instance that states one** — `Vector with 3 Int`, and every alias for it — is a
  COMPILE-TIME CONSTANT, folded to a literal where the instance registry lives.
- **The sized container's own `Self`** states none, deliberately: `Self` inside
  `type Vector uses Element` is the declaration's own instance, interned at `NoFixedSize` so
  that ONE shared body serves every size. There the count is a fact about the RECEIVER, and
  the expression reads it out of a hidden parameter every call site fills in.

### Why the count travels in its own hidden parameter

shv2 does not monomorphize. A generic body compiles once and learns about its concrete
instance at run time from a hidden trailing LAYOUT DESCRIPTOR — a `.rdata` blob carrying the
type ARGUMENT's size, its copy thunk, its destructor and its retain thunk. A count is not a
fact about the argument, and that difference is load-bearing rather than pedantic: when a
callee's instance is written over the CALLER's own type parameters, the caller FORWARDS its
own descriptor blocks instead of minting a blob, which is sound precisely because every word
in them describes the argument, and the run makes the argument identical on both sides.

A count word in that blob would be read through the forward and answer the CALLER's count.
`Vector with 4 Element` inside `type Holder uses Element` states 4 whatever `Element` turns
out to be, while `Holder`'s own blob states no count at all — so the shared body would read
0. (Measured, when the count was a tenth descriptor word: the case below returned 0 where 4
is the answer.) Nor can the forward simply be refused in favour of minting: the forward fires
only when the instance's first type argument IS a type parameter, and minting a blob for such
an instance is impossible — the blob would have to carry that parameter's SIZE, which is
itself a descriptor read.

A parameter has none of that, because **the count is a compile-time constant at every call
site**: the receiver's instance states it, a static's RESULT instance states it, and a
`Vector` method calling a sibling on `self` forwards the slot its own caller filled. It also
costs the descriptor nothing — a body that reads only `countof(Self)` reserves no descriptor
at all, so it can be called from a frame that carries none.

### `countof` in a `static`, where `sizeof` is refused

`sizeof(T)` refuses a receiverless body: it asks whether `__self` is in scope, and no
`static function` can answer yes. `countof` asks the question that actually decides whether
the answer can be supplied — does the CALL SITE hold something that states the count? — and a
`static function … returns Self` does: the instance it builds. That is the whole reason this
expression exists, since a fixed-size container's `create()` is a static and its answer IS
the size.

## Tests

<!-- test: a-stated-count-folds-to-a-literal -->
An operand that states its own count is a compile-time constant, exactly as a concrete
`sizeof` is. No descriptor, no parameter, no instance — the type alone is the answer.
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

<!-- test: one-shared-body-answers-each-instances-own-count -->
ONE compiled body, two sizes. `capacity()` is written once over `type Vector uses Element`,
whose `Self` states no count; each call site fills in the count of the instance it holds.
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
`Vector` written inside `Vector`'s own body denotes exactly what `Self` denotes there, so it
is the same operand and reads the same slot. The token pre-scan that reserves that slot
matches BOTH spellings, because a check filed against one of them would be narrower than the
fact it is about.
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

<!-- test: a-static-reads-the-count -->
The case `sizeof` cannot serve. `counted()` is a `static function` — no receiver, no `self`,
no `__self` in scope — and it reads its own instance's count once per element. Called through
two different sizes it walks 3 trips and then 5, which is what the global counter shows: the
count reached a receiverless body, and it was the right one both times.

⚠ **`rebuild()` USED TO READ `try fresh.get(0) otherwise 0`, AND THAT SPELLING STOPPED BEING WELL
TYPED WHEN `get` RETIRED (W190).** The compiler-served accessor typed a vector element `integer`
whatever the instance said, because `requireVectorElementType` guarantees a scalar; the corpus body is
honestly typed `Element`, which inside `extension Vector` is a TYPE PARAMETER — so the `otherwise` had
no `Element` to offer (`E3059 … 'int' does not match expected type 'type parameter'`) and the `Int`
return had none either. That is the shared-body thesis and not a loss: shv2 compiles ONE body for every
size, so an element read there is opaque exactly as `sizeof(Element)` is. What the case needs of the
static's RESULT is that it be the receiver's own instance.

⚠⚠ **AND IT MUST NOT ASK `count()` FOR THAT, WHICH IS WHAT IT DID UNTIL `count()` RETIRED TO
`countof(Self)` IN THE SAME RUNG.** The assertion was `fresh.count() == countof(Self)`, and once the
corpus `count()` body IS `countof(Self)` both sides are one expression: the `if` cannot be false, the
`panic` is unreachable, and the half of this case that watches the static's result stopped being able
to FAIL while still reading as coverage. It counts the record's SLOTS instead — a `for … in` walk is the
one observation that goes through the record rather than through the type — so the comparison is
record-against-type again, which is the thing being pinned.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
typealias Vec5 = Vector with 5 Int

var trips = 0

extension Vector
	export static function counted() returns Self
		var i = 0
		while i < countof(Self) 'each'
			trips = trips + 1
			i = i + 1
		end 'each'
		return Self{}
	end 'counted'

	export function rebuild() returns Int
		let fresh = Self.counted()
		var slots = 0
		for _ in fresh 'countTheRecordsSlots'
			slots = slots + 1
		end 'countTheRecordsSlots'
		if slots == countof(Self) 'theStaticsResultStatesTheReceiversCount'
			return 0
		end 'theStaticsResultStatesTheReceiversCount'
		panic("a `Self`-returning static builds the instance its call site holds")
	end 'rebuild'
end 'Vector'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec5.create()
	return a.rebuild() + b.rebuild() + trips
end 'main'
```
```exitcode
8
```

<!-- test: error.sizeof-of-a-type-parameter-in-a-static-is-still-refused -->
The door `countof` deliberately does NOT copy. In the very position the case above serves,
`sizeof` of the type parameter is refused — its answer arrives through the layout descriptor,
which B1 threads from the instance a method is called on, and a static has no such receiver.
The two are not the same question and must not share a gate: a count has a second source (the
instance the static RETURNS) and a size, today, does not.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export static function elementBytes() returns Self
		if sizeof(Element) == 0 'impossible'
			panic("a type has a size")
		end 'impossible'
		return Self{}
	end 'elementBytes'

	export function reach() returns Int
		var v = Self.elementBytes()
		if v.count() > 0 'theStaticBuiltOne'
			return 0
		end 'theStaticBuiltOne'
		panic("a `Self`-returning static builds the instance its call site holds")
	end 'reach'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.reach()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:6: Unsupported: sizeof of a type parameter with no instance receiver in scope — a `static function` and a closure are both receiverless, and B1 threads the layout descriptor from the instance a method is called on; read the size through an instance method on `self`, or a concrete instance, instead
```

<!-- test: a-sized-field-of-another-generic-answers-its-own-count -->
⭐⭐ **THE CASE THAT DECIDES WHERE THE COUNT TRAVELS.** `Holder`'s field is a
`Vector with 4 Element` — a sized instance written over the ENCLOSING generic's own type
parameter, which is the one shape whose layout descriptor is FORWARDED rather than minted. A
count carried in that descriptor is read out of `Holder`'s blob, which states none: **measured
at 0 where 4 is the answer.** Carried in its own parameter it is filled in from the instance
the call site actually holds, and answers 4.

⚠ It also compiles at all, which the descriptor route could not manage here: `Holder.size()`
carries NO layout descriptor — no edge in the descriptor-need fixpoint gives a `Vector` method
call one — so a `capacity()` that reserved a descriptor aborted the compiler in
`emitInstanceDescriptorAddr`. A count-only body reserves nothing to forward.
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

<!-- test: error.a-type-that-states-no-element-count -->
A count is a coordinate of the INSTANCE, so only a type that has one can be asked. A ranged
alias has no elements at all, and answering 0 would be a sentinel a reader cannot tell from a
real count.
```maxon
typealias Int = int(i64.min to i64.max)

function main() returns ExitCode
	return countof(Int)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:9: Unsupported: countof of a type that states no element count — only a fixed-size container type has one (`Vector with 3 Int`, or `Self` inside the sized container's own body). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
```

<!-- test: error.a-growable-array-has-no-count-in-its-type -->
The same refusal for the container it is easiest to expect an answer from. An `Array`'s length
lives in the record and changes as the program runs, so it is not something the TYPE can be
asked; `arr.count()` is.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Ints = Array with Int

function main() returns ExitCode
	return countof(Ints)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:9: Unsupported: countof of a type that states no element count — only a fixed-size container type has one (`Vector with 3 Int`, or `Self` inside the sized container's own body). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
```

<!-- test: error.an-ordinary-generics-own-Self-states-no-count -->
`Self` inside an ordinary declared generic is that declaration's own type, and nothing about
it says how many elements it holds — it holds fields, not elements. The refusal is the same
one every countless operand gets, which is what keeps `countof`'s meaning to the one thing it
means.
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
	var b = IntBox.create(1)
	return b.slots()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:10: Unsupported: countof of a type that states no element count — only a fixed-size container type has one (`Vector with 3 Int`, or `Self` inside the sized container's own body). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
```

<!-- test: error.an-alias-spelling-the-pre-scan-cannot-see-is-refused -->
⭐ **A POSITIONED REFUSAL WHERE THE MECHANISM WOULD OTHERWISE ABORT.** The hidden count slot
is reserved by a walk over RAW TOKENS that runs before any body is parsed, so it recognizes
the operand by its spelling. An inner alias naming the same countless instance reaches the
READ without having reached that walk — and rather than discovering the missing slot two
passes later with no source position, the parse refuses it here, naming the line and the cure.
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
error E2015: <fragment>:9:10: Unsupported: countof of the sized container's own `Self`, in a function that carries no fixed-element-count parameter — the count of a countless `Self` is the RECEIVER's, and it reaches a shared body through a hidden argument every call site fills in from the instance it holds. The pre-scan that reserves that slot reads raw tokens, so spell the operand `Self` (or the container's own name) directly rather than through an alias
```

<!-- test: error.an-ordinary-generics-static-is-refused-for-the-right-reason -->
⭐ **THE NARROWING THIS CASE EXISTS TO PIN.** The token pre-scan that reserves the hidden count
slot asks first whether the enclosing declaration IS the sized container. Without that
question it would reserve a slot here too — and a reservation drags in the receiverless rule
(*a `static function` needing the enclosing instance's dictionary must return `Self`*), so this
program would be refused for failing to return `Self` rather than for the thing that is
actually wrong with it. The narrowing cannot go the other way and hide a real read: `Self`
denotes a countable instance for exactly the declarations it admits.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntBox = Box with Int

type Box uses T
	export var value as T

	export static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	export static function slots() returns Int
		return countof(Self)
	end 'slots'
end 'Box'

function main() returns ExitCode
	var b = IntBox.create(1)
	return b.value + IntBox.slots()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:10: Unsupported: countof of a type that states no element count — only a fixed-size container type has one (`Vector with 3 Int`, or `Self` inside the sized container's own body). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
```

<!-- test: the-count-reaches-a-Self-typed-parameter -->
The count travels the same edge `sizeof`'s descriptor does — see
[sizeof](sizeof.md)'s `through-a-Self-typed-parameter`. `other Self` binds to the very instance
the receiver is (a countless `Self` adopts the receiver's count, and a differently-sized
argument is refused outright), so forwarding the caller's own slot is the exact answer rather
than an approximation.

⚠ Without the edge this ABORTED THE COMPILER rather than being refused: the guard that catches
the shape for a declared generic tests for a `structRef` receiver, and a container whose record
the compiler owns has a `genericInstance` `Self`, which that test walks straight past.
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

<!-- test: error.countof-inside-a-closure -->
⭐ **A CLOSURE IS A DIFFERENT FUNCTION, AND THE REFUSAL SAYS SO WITH A LINE.** A closure body is
written inside a method and emitted as its own top-level function, carrying none of that
method's hidden parameters — so the count is simply not in scope there. `sizeof` reaches the
same conclusion through its own gate (a closure is receiverless, so `__self` is not in scope);
a count has no `__self` to test, so the closure is named directly. Without this the parse
handed the lifted function a ValueId it never defines and the compiler ABORTED
(`Parser.tagOf: value v1 has no recorded type`).
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function capacity() returns Int
		return countof(Self)
	end 'capacity'

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
error E2015: <fragment>:11:28: Unsupported: countof of the sized container's own `Self` inside a closure — a closure is lifted to its own function, which carries none of the enclosing method's hidden parameters, so the count is not in scope there. Read `countof(Self)` into a binding outside the closure and capture that
```

<!-- test: the-count-reaches-a-chained-call -->
⭐ **AN UNNAMED RECEIVER.** `self.dup()` hands back a value of the enclosing type with no
binding to hold it, so neither of the fixpoint's precise self-call columns can see the
`.capacity()` that chains onto it — one keys on `self`, the other on a local bound to a
`Self{…}`. A missing edge in that fixpoint is not a wrong answer but a COMPILER ABORT: measured,
`caller 'Vector.chained' has no fixed-element-count parameter to forward to 'Vector.capacity'`.

⛔ **THE RECORDED SHAPE IS `) . <member> (`, NOT EVERY `.<member>(`, and that difference is
MEASURED rather than stylistic.** A receiver-blind arm read `VectorIter.create(managed)` inside
`createIterator` as a call to `Vector.create`, whose `Self{}` seeds a DESCRIPTOR need, and every
`for … in` over a vector grew a `lea` of its `__layout_*` blob — four drifting goldens. A
receiver spelled as a NAME is already covered by a precise column; the only receiver they cannot
see is a call's RESULT, and a call's result is what the `)` identifies.
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


<!-- test: a-local-bound-to-a-Self-returning-call-reaches-the-count -->
⭐⭐ **THIS WAS A REFUSAL FOR ONE RUNG, AND `W190` RETIRED IT BY WIDENING THE SEED RATHER THAN BY
ADDING A TOKEN SHAPE.** The pre-scan still cannot see that `dup()` returns `Self` —
`selfTypedLocalBoundAt` admits `= Self{…}` and `= Self.<static>(` because those two spellings NAME
the type, while `= self.dup()` names a METHOD — so nothing about `var other = self.dup()` changed.
What changed is `dup`'s own body: `Self{}` inside the sized container BUILDS a record whose slots are
published, so that literal now reserves the count slot itself
(`Parser.ownRecordLiteralOfTheSizedContainerAt`). `dup` is therefore count-needing, `self.dup()` is an
ordinary self-call edge, and the fixpoint gives `twice` the slot it had nothing to forward from.

⚠ **THE REFUSAL IT REPLACED WAS REAL AND IS STILL REACHABLE** — the two cases below pin it, and
before the door existed this exact program was *`panic at forwardCallerFixedElementCount: caller
'Vector.twice' has no fixed-element-count parameter to forward`*. A shape the fixpoint CAN reach is
served; one it cannot is refused with a line. That difference is the whole design, and it is why the
door was not narrowed to make this case pass: the seed reaches the caller because the callee genuinely
needs the count, not because a spelling was whitelisted.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function capacity() returns Int
		return countof(Self)
	end 'capacity'

	export function dup() returns Self
		return Self{}
	end 'dup'

	export function twice() returns Int
		var other = self.dup()
		return other.capacity()
	end 'twice'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.twice()
end 'main'
```
```exitcode
3
```

<!-- test: error.an-alias-of-a-Self-typed-parameter-is-refused -->
The same gap one alias further out. `other Self` IS a shape the pre-scan reads
(`selfTypedParamDeclaredAt`), so `other.capacity()` is served — but a plain `var same = other`
re-binds the value under a name nothing recorded, and the edge stops there. Measured as the same
abort before this door existed; a positioned refusal now, naming the reach that works.
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
```maxoncstderr
error E2015: <fragment>:12:15: Unsupported: calling 'capacity' (which reads `countof` of the sized container's own `Self`) on a receiver whose type states no element count, from a function that carries no fixed-element-count parameter of its own to forward — the count reaches a shared body through a hidden argument every call site fills in from the instance it holds. Reach it through `self`, through a local bound to a `Self{…}` or a `Self.<static>()`, by chaining the call directly onto the expression that produced the receiver, or through a concrete sized instance; a lifted closure carries no hidden parameters at all, so read the count outside it and capture the integer
```

<!-- test: error.a-count-reading-call-from-inside-a-closure-is-refused -->
⛔⛔ **THE CLOSURE REFUSAL ABOVE COVERS `countof(Self)` WRITTEN IN A CLOSURE; IT DOES NOT COVER
CALLING SOMETHING THAT READS ONE**, and that second spelling ABORTED the compiler (found at
review): *`caller 'Vector.viaClosure$closure_0' has no fixed-element-count parameter to
forward`*. No edge could have prevented it — a lifted closure is emitted as its own function and
carries NONE of the enclosing method's hidden parameters, however completely the fixpoint
reserved them. So the question the door asks is what the function being EMITTED carries, which
is `requireWitnessSourceForForwarding`'s W58 distinction under a second column.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function capacity() returns Int
		return countof(Self)
	end 'capacity'

	export function viaClosure(other Self) returns Int
		let f = function(v Self) gives v.capacity()
		return f(other)
	end 'viaClosure'
end 'Vector'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec3.create()
	return a.viaClosure(b)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:11:36: Unsupported: calling 'capacity' (which reads `countof` of the sized container's own `Self`) on a receiver whose type states no element count, from a function that carries no fixed-element-count parameter of its own to forward — the count reaches a shared body through a hidden argument every call site fills in from the instance it holds. Reach it through `self`, through a local bound to a `Self{…}` or a `Self.<static>()`, by chaining the call directly onto the expression that produced the receiver, or through a concrete sized instance; a lifted closure carries no hidden parameters at all, so read the count outside it and capture the integer
```
