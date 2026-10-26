---
feature: countof
status: experimental
keywords: [countof, fixed array, fixed size, generics, dictionary passing]
category: generics
---

# `countof(Type)` — how many elements a fixed-size container type holds

## Documentation

`countof(T)` is `sizeof(T)`'s twin one axis over: `sizeof` answers in BYTES PER VALUE,
`countof` in ELEMENTS PER INSTANCE. It is defined for exactly the types whose element count
is part of their identity — the fixed-size container, `FixedArray with N Element` (see
[fixed array](fixed-array.md)). A growable `Array`'s length is a runtime field of the record rather than
a coordinate of its type, so `countof` of one is refused; ask the value for its `count()`.

### Two outcomes, decided by whether the operand states a count

- **An instance that states one** — `FixedArray with 3 Int`, and every alias for it — is a
  COMPILE-TIME CONSTANT, folded to a literal where the instance registry lives.
- **The sized container's own `Self`** states none, deliberately: `Self` inside
  `type FixedArray uses Element` is the declaration's own instance, interned at `NoFixedSize` so
  that ONE shared body serves every size. There the count is a fact about the RECEIVER, and
  the expression reads it out of a hidden parameter every call site fills in.

### Why the count travels in its own hidden parameter

the compiler does not monomorphize. A generic body compiles once and learns about its concrete
instance at run time from a hidden trailing LAYOUT DESCRIPTOR — a `.rdata` blob carrying the
type ARGUMENT's size, its copy thunk, its destructor and its retain thunk. A count is not a
fact about the argument, and that difference is load-bearing rather than pedantic: when a
callee's instance is written over the CALLER's own type parameters, the caller FORWARDS its
own descriptor blocks instead of minting a blob, which is sound precisely because every word
in them describes the argument, and the run makes the argument identical on both sides.

A count word in that blob would be read through the forward and answer the CALLER's count.
`FixedArray with 4 Element` inside `type Holder uses Element` states 4 whatever `Element` turns
out to be, while `Holder`'s own blob states no count at all — so the shared body would read
0 where 4 is the answer. Nor can the forward simply be refused in favour of minting: the forward fires
only when the instance's first type argument IS a type parameter, and minting a blob for such
an instance is impossible — the blob would have to carry that parameter's SIZE, which is
itself a descriptor read.

A parameter has none of that, because **the count is a compile-time constant at every call
site**: the receiver's instance states it, a static's RESULT instance or the instance its call
names states it, and a
`FixedArray` method calling a sibling on `self` forwards the slot its own caller filled. It also
costs the descriptor nothing — a body that reads only `countof(Self)` reserves no descriptor
at all, so it can be called from a frame that carries none.

### `countof` in a `static`

A `static function` has no receiver, so its count comes from the CALL SITE: the instance a
`static function … returns Self` builds, or the instance the call names. That is the whole
reason this expression exists, since a fixed-size container's `create()` is a static and its
answer IS the size. `sizeof(T)` in a static reads the layout descriptor from the same places.

## Tests

<!-- test: a-stated-count-folds-to-a-literal -->
An operand that states its own count is a compile-time constant, exactly as a concrete
`sizeof` is. No descriptor, no parameter, no instance — the type alone is the answer.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	return countof(Vec3)
end 'main'
```
```exitcode
3
```

<!-- test: one-shared-body-answers-each-instances-own-count -->
ONE compiled body, two sizes. `capacity()` is written once over `type FixedArray uses Element`,
whose `Self` states no count; each call site fills in the count of the instance it holds.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Vec5 = FixedArray with 5 Int

extension FixedArray
	function capacity() returns Int
		return countof(Self)
	end 'capacity'
end 'FixedArray'

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
`FixedArray` written inside `FixedArray`'s own body denotes exactly what `Self` denotes there, so it
is the same operand and reads the same slot. The token pre-scan that reserves that slot
matches BOTH spellings, because a check filed against one of them would be narrower than the
fact it is about.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function byName() returns Int
		return countof(FixedArray)
	end 'byName'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.byName()
end 'main'
```
```exitcode
3
```

<!-- test: a-static-reads-the-count -->
`counted()` is a `static function` — no receiver, no `self`,
no `__self` in scope — and it reads its own instance's count once per element. Called through
two different sizes it walks 3 trips and then 5, which is what the global counter shows: the
count reached a receiverless body, and it was the right one both times.

⚠ **`rebuild()` DOES NOT READ AN ELEMENT.** The corpus `get` is typed `Element`, which inside
`extension FixedArray` is a TYPE PARAMETER — so `try fresh.get(0) otherwise 0` has no `Element` to offer
(`E3059 … 'int' does not match expected type 'type parameter'`) and the `Int` return has none either.
That is the shared-body thesis and not a loss: the compiler compiles ONE body for every size, so an
element read there is opaque exactly as `sizeof(Element)` is. What the case needs of the static's RESULT
is that it be the receiver's own instance.

⚠⚠ **AND IT MUST NOT ASK `count()` FOR THAT.** The corpus `count()` body IS `countof(Self)`, so
`fresh.count() == countof(Self)` is one expression on both sides: the `if` cannot be false, the `panic` is
unreachable, and the half of this case that watches the static's result could not FAIL while still
reading as coverage. It counts the record's SLOTS instead — a `for … in` walk is the
one observation that goes through the record rather than through the type — so the comparison is
record-against-type again, which is the thing being pinned.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Vec5 = FixedArray with 5 Int

var trips = 0 as Int

extension FixedArray
	static function counted() returns Self
		var i = 0 as Count
		while i < countof(Self) 'each'
			trips = trips + 1
			i = i + 1
		end 'each'
		return Self{}
	end 'counted'

	function rebuild() returns Int
		let fresh = Self.counted()
		var slots = 0 as Count
		for _ in fresh 'countTheRecordsSlots'
			slots = slots + 1
		end 'countTheRecordsSlots'
		if slots == countof(Self) 'theStaticsResultStatesTheReceiversCount'
			return 0
		end 'theStaticsResultStatesTheReceiversCount'
		panic("a `Self`-returning static builds the instance its call site holds")
	end 'rebuild'
end 'FixedArray'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec5.create()
	return a.rebuild() + b.rebuild() + trips
end 'main'
```
```exitcode
8
```

<!-- test: a-static-that-returns-another-type-reads-the-count-of-the-instance-its-call-names -->
A `static function` that returns an `Int` builds no instance, so the count comes from the instance the CALL names: `Vec3.slotsOf()` states 3 and `Vec5.slotsOf()` states 5, through one compiled body. An instance method reaching the same static through `Self` forwards the count it was handed, and a static with a parameter of its own reads it the same way.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Vec5 = FixedArray with 5 Int

extension FixedArray
	static function slotsOf() returns Int
		return countof(Self)
	end 'slotsOf'

	static function scaled(factor Int) returns Int
		return (countof(Self) as Int) * factor
	end 'scaled'

	function viaSelf() returns Int
		return Self.slotsOf()
	end 'viaSelf'
end 'FixedArray'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec5.create()
	print("{Vec3.slotsOf()} {Vec5.slotsOf()} {Vec3.scaled(2)} {Vec5.scaled(3)} {a.viaSelf()} {b.viaSelf()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 5 6 15 3 5
```

<!-- test: overloaded-statics-that-read-the-count-each-take-the-call-sites-count -->
Two statics of one name read `countof`, the one without a parameter declared after the one with it. Both ends of the hidden count slot decide on the overload set's one name, so each call hands the count its overload reads.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	static function slotsOf(extra Int) returns Int
		return (countof(Self) as Int) + extra
	end 'slotsOf'

	static function slotsOf() returns Int
		return countof(Self)
	end 'slotsOf'

	static function makeOf(extra Int) returns Self
		if (countof(Self) as Int) + extra == 0 'never'
			panic("never")
		end 'never'

		return Self{}
	end 'makeOf'

	static function makeOf() returns Self
		return Self{}
	end 'makeOf'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	_ = Vec3.makeOf(1)
	print("{Vec3.slotsOf(1)} {Vec3.slotsOf()} {v.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 3 3
```

<!-- test: overloaded-statics-that-read-the-count-in-the-other-declaration-order -->
The same two statics declared the other way round.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	static function slotsOf() returns Int
		return countof(Self)
	end 'slotsOf'

	static function slotsOf(extra Int) returns Int
		return (countof(Self) as Int) + extra
	end 'slotsOf'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	print("{Vec3.slotsOf(1)} {Vec3.slotsOf()} {v.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 3 3
```

<!-- test: overloaded-instance-methods-that-read-the-count-each-take-the-receivers-count -->
Two instance methods of one name read `countof` of the sized receiver, in both widths.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Vec5 = FixedArray with 5 Int

extension FixedArray
	function slotsPlus() returns Int
		return countof(Self)
	end 'slotsPlus'

	function slotsPlus(extra Int) returns Int
		return (countof(Self) as Int) + extra
	end 'slotsPlus'
end 'FixedArray'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec5.create()
	print("{a.slotsPlus(1)} {a.slotsPlus()} {b.slotsPlus(1)} {b.slotsPlus()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 3 6 5
```

<!-- test: overloaded-instance-methods-that-read-the-count-in-the-other-declaration-order -->
The same two methods declared the other way round.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Vec5 = FixedArray with 5 Int

extension FixedArray
	function slotsPlus(extra Int) returns Int
		return (countof(Self) as Int) + extra
	end 'slotsPlus'

	function slotsPlus() returns Int
		return countof(Self)
	end 'slotsPlus'
end 'FixedArray'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec5.create()
	print("{a.slotsPlus(1)} {a.slotsPlus()} {b.slotsPlus(1)} {b.slotsPlus()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 3 6 5
```

<!-- test: error.a-static-reading-the-count-through-a-bare-container-name-is-refused -->
The count of a static that returns another type comes from the instance its call names. `FixedArray.slotsOf()` names no sized instance, and `main` carries no count to forward, so the call is refused with a position rather than handed a count nobody holds.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	static function slotsOf() returns Int
		return countof(Self)
	end 'slotsOf'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	print("{FixedArray.slotsOf()} {v.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:21: Unsupported: calling 'slotsOf' (which reads `countof` of the sized container's own `Self`) through a name that states no element count, from a function that carries no fixed-element-count parameter of its own to forward — a `static function` that does not return `Self` takes the count from the instance its call names. Name a sized instance with a typealias (`FixedArray with 3 Int`) and call it through that, or call it from a method of the sized container
```

<!-- test: sizeof-of-a-type-parameter-in-a-static-reads-the-call-sites-descriptor -->
`sizeof` of the type parameter in a `static function` is read from the layout descriptor the call site hands the static, the way `countof` reads the count it is handed. `elementBytes` returns `Self`, so the instance it builds supplies both.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	static function elementBytes() returns Self
		if sizeof(Element) == 0 'impossible'
			panic("a type has a size")
		end 'impossible'
		return Self{}
	end 'elementBytes'

	function reach() returns Int
		var v = Self.elementBytes()
		if v.count() > 0 'theStaticBuiltOne'
			return 0
		end 'theStaticBuiltOne'
		panic("a `Self`-returning static builds the instance its call site holds")
	end 'reach'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.reach()
end 'main'
```
```exitcode
0
```

<!-- test: a-sized-field-of-another-generic-answers-its-own-count -->
⭐⭐ **THE CASE THAT DECIDES WHERE THE COUNT TRAVELS.** `Holder`'s field is a
`FixedArray with 4 Element` — a sized instance written over the ENCLOSING generic's own type
parameter, which is the one shape whose layout descriptor is FORWARDED rather than minted. A
count carried in that descriptor would be read out of `Holder`'s blob, which states none, and
answer **0 where 4 is the answer.** Carried in its own parameter it is filled in from the instance
the call site actually holds, and answers 4.

⚠ It also compiles at all, which a descriptor route could not manage here: `Holder.size()`
carries NO layout descriptor — no edge in the descriptor-need fixpoint gives a `FixedArray` method
call one — so a `capacity()` that reserved a descriptor would abort the compiler in
`emitInstanceDescriptorAddr`. A count-only body reserves nothing to forward.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntHolder = Holder with Int

extension FixedArray
	function capacity() returns Int
		return countof(Self)
	end 'capacity'
end 'FixedArray'

type Holder uses Element
	typealias Slot = FixedArray with 4 Element

	var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	function size() returns Int
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
error E2015: <fragment>:5:9: Unsupported: countof of a type that states no element count — only a fixed-size container type has one (`FixedArray with 3 Int`, or `Self` inside the sized container's own body). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
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
error E2015: <fragment>:6:9: Unsupported: countof of a type that states no element count — only a fixed-size container type has one (`FixedArray with 3 Int`, or `Self` inside the sized container's own body). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
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

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	function slots() returns Int
		return countof(Self)
	end 'slots'
end 'Box'

function main() returns ExitCode
	var b = IntBox.create(1)
	return b.slots()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:10: Unsupported: countof of a type that states no element count — only a fixed-size container type has one (`FixedArray with 3 Int`, or `Self` inside the sized container's own body). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
```

<!-- test: error.an-alias-spelling-the-pre-scan-cannot-see-is-refused -->
⭐ **A POSITIONED REFUSAL WHERE THE MECHANISM WOULD OTHERWISE ABORT.** The hidden count slot
is reserved by a walk over RAW TOKENS that runs before any body is parsed, so it recognizes
the operand by its spelling. An inner alias naming the same countless instance reaches the
READ without having reached that walk — and rather than discovering the missing slot two
passes later with no source position, the parse refuses it here, naming the line and the cure.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	typealias Me = FixedArray with Element

	function viaAlias() returns Int
		return countof(Me)
	end 'viaAlias'
end 'FixedArray'

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
question it would reserve a slot here too, and this program would reach that slot's machinery
rather than being refused for the thing that is actually wrong with it. The narrowing cannot go the other way and hide a real read: `Self`
denotes a countable instance for exactly the declarations it admits.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntBox = Box with Int

type Box uses T
	export var value as T

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	static function slots() returns Int
		return countof(Self)
	end 'slots'
end 'Box'

function main() returns ExitCode
	var b = IntBox.create(1)
	return b.value + IntBox.slots()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:10: Unsupported: countof of a type that states no element count — only a fixed-size container type has one (`FixedArray with 3 Int`, or `Self` inside the sized container's own body). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
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
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function capacity() returns Int
		return countof(Self)
	end 'capacity'

	function pairWith(other Self) returns Int
		return other.capacity()
	end 'pairWith'
end 'FixedArray'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec3.create()
	return a.pairWith(b)
end 'main'
```
```exitcode
3
```

<!-- test: countof-inside-a-closure-reads-the-methods-count -->
A closure carries the fixed-element count of the function it is written in, so `countof(Self)` in a
closure inside a sized container's method reads that method's count.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function capacity() returns Int
		return countof(Self)
	end 'capacity'

	function viaClosure() returns Int
		let f = function() gives countof(Self)
		return f()
	end 'viaClosure'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.viaClosure()
end 'main'
```
```exitcode
3
```

<!-- test: the-count-reaches-a-chained-call -->
⭐ **AN UNNAMED RECEIVER.** `self.dup()` hands back a value of the enclosing type with no
binding to hold it, so neither of the fixpoint's precise self-call columns can see the
`.capacity()` that chains onto it — one keys on `self`, the other on a local bound to a
`Self{…}`. A missing edge in that fixpoint is not a wrong answer but a COMPILER ABORT:
`caller 'FixedArray.chained' has no fixed-element-count parameter to forward to 'FixedArray.capacity'`.

⛔ **THE RECORDED SHAPE IS `) . <member> (`, NOT EVERY `.<member>(`, and that difference is not
stylistic.** A receiver-blind arm would read `FixedArrayIter.create(managed)` inside `createIterator` as a
call to `FixedArray.create`, whose `Self{}` seeds a DESCRIPTOR need, and every `for … in` over a fixed array would
grow a `lea` of its `__layout_*` blob. A
receiver spelled as a NAME is already covered by a precise column; the only receiver they cannot
see is a call's RESULT, and a call's result is what the `)` identifies.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function dup() returns Self
		return Self{}
	end 'dup'

	function capacity() returns Int
		return countof(Self)
	end 'capacity'

	function chained() returns Int
		return self.dup().capacity()
	end 'chained'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.chained()
end 'main'
```
```exitcode
3
```


<!-- test: a-local-bound-to-a-Self-returning-call-reaches-the-count -->
⭐⭐ **THE SEED REACHES THIS CALLER THROUGH THE CALLEE'S BODY, NOT THROUGH A TOKEN SHAPE.** The
pre-scan cannot see that `dup()` returns `Self` — `selfTypedLocalBoundAt` admits `= Self{…}` and
`= Self.<static>(` because those two spellings NAME the type, while `= self.dup()` names a METHOD.
What serves `var other = self.dup()` is `dup`'s own body: `Self{}` inside the sized container BUILDS a
record whose slots are published, so that literal reserves the count slot itself
(`Parser.ownRecordLiteralOfTheSizedContainerAt`). `dup` is therefore count-needing, `self.dup()` is an
ordinary self-call edge, and the fixpoint gives `twice` the slot it had nothing to forward from.

⚠ **THE REFUSAL NEXT TO IT IS REAL** — the two cases below pin it. A shape the fixpoint CAN reach is
served; one it cannot is refused with a line. That difference is the whole design: the seed reaches the
caller because the callee genuinely needs the count, not because a spelling was whitelisted.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function capacity() returns Int
		return countof(Self)
	end 'capacity'

	function dup() returns Self
		return Self{}
	end 'dup'

	function twice() returns Int
		var other = self.dup()
		return other.capacity()
	end 'twice'
end 'FixedArray'

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
re-binds the value under a name nothing recorded, and the edge stops there. It is refused at its
position, naming the reach that works.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function capacity() returns Int
		return countof(Self)
	end 'capacity'

	function pairWith(other Self) returns Int
		var same = other
		return same.capacity()
	end 'pairWith'
end 'FixedArray'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec3.create()
	return a.pairWith(b)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:15: Unsupported: calling 'capacity' (which reads `countof` of the sized container's own `Self`) on a receiver whose type states no element count, from a function that carries no fixed-element-count parameter of its own to forward — the count reaches a shared body through a hidden argument every call site fills in from the instance it holds. Reach it through `self`, through a local bound to a `Self{…}` or a `Self.<static>()`, by chaining the call directly onto the expression that produced the receiver, or through a concrete sized instance
```

<!-- test: a-count-reading-call-from-inside-a-closure-is-served -->
A call inside a closure to a method that reads `countof(Self)` forwards the count the closure carries
from the method it is written in.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function capacity() returns Int
		return countof(Self)
	end 'capacity'

	function viaClosure(other Self) returns Int
		let f = function(v Self) gives v.capacity()
		return f(other)
	end 'viaClosure'
end 'FixedArray'

function main() returns ExitCode
	var a = Vec3.create()
	var b = Vec3.create()
	return a.viaClosure(b)
end 'main'
```
```exitcode
3
```

<!-- test: error.countof-is-a-count -->
`countof` answers a `Count`, the alias of a number of elements, so adding it to a value of another alias is the mismatch two aliases always are.
```maxon
typealias Small = int(0 to 100)
typealias Vec3 = FixedArray with 3 Small

function main() returns ExitCode
	let base = 5 as Small
	let size = countof(Vec3)
	return (base + size) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:15: operator '+' requires both operands to be the same type: 'Small' and 'Count' are different typealiases — cast one side with 'as'
```

<!-- test: countof-is-a-count-that-indexes -->
`countof` is a `Count`, which is an `ElementIndex`, so the last slot of a fixed-size container is addressed with no cast.
```maxon
typealias Vec3 = FixedArray with 3 Count

function main() returns ExitCode
	var v = Vec3.create()
	v.set(2, value: 42)
	let last = countof(Vec3) - 1
	return v.get(last) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-top-level-let-reads-the-count-of-a-type -->
A file-scope initialiser is a constant, and `countof(T)` of a type that states its count is one, so `let n = countof(P)` is read back as the count with no function around it.
```maxon
typealias W = bits(64)
typealias P = FixedArray with 8 W

let n = countof(P)

function main() returns ExitCode
	return n as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: a-top-level-initialiser-folds-countof-in-an-expression -->
`countof(P)` is an operand of the folded top-level expression exactly as a literal is: `countof(P) - 1` is the last index and `countof(P) * 2` the doubled count.
```maxon
typealias W = bits(64)
typealias P = FixedArray with 8 W

let last = countof(P) - 1
let doubled = countof(P) * 2

function main() returns ExitCode
	return (last + doubled) as ExitCode
end 'main'
```
```exitcode
23
```

<!-- test: a-top-level-count-bounds-a-counted-loop-over-the-type -->
A top-level `let` bound to `countof(Vec4)` is a known constant inside a function, so it bounds a counted loop whose counter is proven inside the fixed array: `set` and `get` are written bare.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

let slots = countof(Vec4)

function main() returns ExitCode
	var v = Vec4.create()
	for i in 0 upto slots 'fill'
		v.set(i, value: 5)
	end 'fill'
	var total = 0 as Int
	for i in 0 upto slots 'sum'
		total = total + v.get(i)
	end 'sum'
	return total
end 'main'
```
```exitcode
20
```

<!-- test: error.a-top-level-countof-of-a-type-that-states-no-element-count -->
The refusal a function body gives a countless operand is the one a file-scope initialiser gives, at the same token.
```maxon
typealias Int = int(i64.min to i64.max)

let n = countof(Int)

function main() returns ExitCode
	return n
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:9: Unsupported: countof of a type that states no element count — only a fixed-size container type has one (`FixedArray with 3 Int`, or `Self` inside the sized container's own body). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
```

<!-- test: error.a-top-level-countof-of-a-growable-array -->
A growable `Array` has no count in its type at file scope either.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Ints = Array with Int

let n = countof(Ints)

function main() returns ExitCode
	return n
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:9: Unsupported: countof of a type that states no element count — only a fixed-size container type has one (`FixedArray with 3 Int`, or `Self` inside the sized container's own body). A growable `Array`'s length is a runtime field of the record, not part of its type, so ask it for its `count()`
```

<!-- test: error.a-file-scope-fold-adds-countof-to-another-alias-only-through-a-cast -->
A file-scope initializer that adds a `countof` to a constant of another alias meets the same mismatch, since the folded `countof` is a `Count` there too.
```maxon
typealias Small = int(0 to 100)
typealias Vec3 = FixedArray with 3 Small

let base = 5 as Small
let size = countof(Vec3)
let total = base + size

function main() returns ExitCode
	return total as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:18: operator '+' requires both operands to be the same type: 'Small' and 'Count' are different typealiases — cast one side with 'as'
```

<!-- test: error.a-top-level-countof-is-a-count -->
A file-scope `countof` is a `Count` as it is in a body, so a top-level `let` bound to it adds to another alias only through a cast, and the mismatch is the one `error.countof-is-a-count` reports.
```maxon
typealias Small = int(0 to 100)
typealias Vec3 = FixedArray with 3 Small

let size = countof(Vec3)

function main() returns ExitCode
	let base = 5 as Small
	return (base + size) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:15: operator '+' requires both operands to be the same type: 'Small' and 'Count' are different typealiases — cast one side with 'as'
```

<!-- test: error.a-countof-of-a-type-another-file-hides-is-refused-in-a-body -->
A type private to another file is not visible to `countof` in a function body.
```maxon
// --- file: lib/sizes.maxon
typealias Int = int(i64.min to i64.max)
typealias Hidden = FixedArray with 3 Int

function hiddenCount() returns Count
	return countof(Hidden)
end 'hiddenCount'

// --- file: lib/main.maxon
function main() returns ExitCode
	return (countof(Hidden) + hiddenCount()) as ExitCode
end 'main'
```
```maxoncstderr
error E3008: lib/<fragment>:12:18: typealias 'Hidden' is not exported
```

<!-- test: error.a-top-level-countof-of-a-type-another-file-hides-is-refused -->
The same type named by a file-scope `countof` is refused with the same diagnostic, not a compiler fault.
```maxon
// --- file: lib/sizes.maxon
typealias Int = int(i64.min to i64.max)
typealias Hidden = FixedArray with 3 Int

function hiddenCount() returns Count
	return countof(Hidden)
end 'hiddenCount'

// --- file: lib/main.maxon
let n = countof(Hidden)

function main() returns ExitCode
	return (n + hiddenCount()) as ExitCode
end 'main'
```
```maxoncstderr
error E3008: lib/<fragment>:11:17: typealias 'Hidden' is not exported
```
