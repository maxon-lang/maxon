---
feature: extension-publication
status: stable
keywords: [extension, method, sibling, implicit-self, corpus, conformer]
category: type-system
---

# What an `extension` PUBLISHES onto its conformer

## Documentation

An `extension` publishes its methods onto a conformer, and from that moment they are **the conformer's
methods** — indistinguishable, at every call site, from the ones the conformer's own body declares. Two
readers used to disagree with that sentence, in opposite directions.

### A bare call inside an extension body

Inside a method body, a bare `name(…)` resolves to a **sibling method of the enclosing type** before it
resolves to a free function. Inside an `extension` body the enclosing type is the CONFORMER — so the
siblings of `extension Box`'s methods are every method `Box` has, wherever it was declared.

The sibling walk is a token walk, and it begins at the **extension's** member list, because a bare call
here may name a sibling extension method. That walk therefore cannot see the conformer's own body: it is
a different declaration, possibly in a different file. The conformer's methods have to come from the
whole-program declaration index instead, which is the only reader that has them all.

```maxon
type Box
	export function tag() returns Integer
		return 5
	end 'tag'
end 'Box'

extension Box
	export function doubled() returns Integer
		return tag() * 2        -- `Box.tag`, exactly as `self.tag()` means
	end 'doubled'
end 'Box'
```

Two facts are needed per declaration, and the index publishes both under the same
`<Conformer>.<method>` key: that the declaration EXISTS, and whether it takes a **receiver**. The second
is not a refinement — a `static` handed a `self` it never declared fails its own arity check, and an
instance method denied one is called on nothing.

### A member the corpus declares, reached through the fall-through

A receiver whose surface shv2 still synthesizes (`String`, `Character`, `StringIndex`) asks its roster
first and the CORPUS second: a member the roster does not carry, and a listed module declares, becomes an
ordinary call to an ordinary declared function. That door has to ask **"is this member declared on this
type?"** — and it asked *"does the conformer declare it in its own body?"*, which is a different question
with a different answer for every method an `extension` published.

`stdlib/String.maxon` declares `type String implements … Iterable`, and `stdlib/Interfaces.maxon`'s
`extension Iterable` publishes `map`, `filter` and `withIterator` onto it. All three are `String`
methods; none is declared in `String`'s own body.

### The question the two doors must NOT share

*"Does the conformer declare this in its OWN body?"* is still asked, by exactly one caller: the extension
fold, deciding whether a conformer's own declaration **shadows** the extension's. That verdict must
exclude what an extension published, and the exclusion is what makes it independent of FOLD ORDER — two
extensions publishing one `<Conformer>.<method>` are a genuine duplicate definition and stay `E3006`
whichever of them folds first, because neither of them is the conformer's own declaration. The doors
above ask about EXISTENCE and must not inherit that exclusion.

### An extension publishes a CONFORMANCE too, and it JOINS the conformer's own

`extension X implements I` publishes a conformance onto `X` exactly as an extension method publishes a
method: from that moment `X` implements `I` **as well as** everything its own `implements` clause named.
The two declarations are a union, never a replacement, and which of them the author wrote first decides
nothing.

That is worth a pair of cases because the two are recorded as two separate declarations under one
conformer name — a `type` clause's and an extension's — and a reader that files the second over the first
answers *"type 'X' does not implement interface 'I'"* about a program that plainly declares it, for
whichever of the two happens to be written earlier.

## Tests

<!-- test: a-bare-call-inside-a-type-extension-reaches-the-conformers-own-method -->
The headline case. `self.tag()` compiled and bare `tag()` was
**`E3004: call to undefined function 'tag'`** — two spellings of one call, one of which the language says
are the same. Nothing about generics is involved; the conformer is generic here only because that is the
shape the gap was found on.
```maxon
typealias Idx = int(0 to u64.max)

type Box uses T
	export var slot as T

	export static function make(v T) returns Self
		return Self{slot: v}
	end 'make'

	export function tag() returns Idx
		return 5
	end 'tag'
end 'Box'

export extension Box
	export function bareCall() returns Idx
		return tag()
	end 'bareCall'

	export function qualifiedCall() returns Idx
		return self.tag()
	end 'qualifiedCall'
end 'Box'

function main() returns ExitCode
	let b = Box.make(1 as Idx)
	return (b.bareCall() + b.qualifiedCall() * 2) as ExitCode
end 'main'
```
```exitcode
15
```

<!-- test: a-bare-call-inside-a-type-extension-reaches-a-conformer-declared-static -->
⚠ **THE RECEIVER HALF IS A SEPARATE FACT AND GETTING IT WRONG IS A WRONG REFUSAL.** `Box.of` is
`static`, so a bare `of(7)` must be called with NO instance; handed the enclosing `self` it would fail
the arity check on a parameter its author never wrote. The index publishes staticness per declaration for
exactly this, beside the declaration's visibility and its file.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var v as Integer

	export static function of(v Integer) returns Self
		return Self{v: v}
	end 'of'
end 'Box'

extension Box
	export function twin() returns Integer
		return of(7).v + self.v
	end 'twin'
end 'Box'

function main() returns ExitCode
	let b = Box.of(4)
	return b.twin() as ExitCode
end 'main'
```
```exitcode
11
```

<!-- test: a-bare-call-inside-an-interface-extension-reaches-a-non-requirement-of-the-conformer -->
The same rule where the enclosing declaration is an INTERFACE extension. An interface's own requirements
already reach this body — they are added to the sibling map from the requirement list — so the case that
distinguishes the two readers is a conformer method the interface does **not** require: `Five.bonus`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Tagged
	function tag() returns Integer
end 'Tagged'

extension Tagged
	export function score() returns Integer
		return tag() + bonus()
	end 'score'
end 'Tagged'

type Five implements Tagged
	export static function make() returns Self
		return Self{}
	end 'make'

	export function tag() returns Integer
		return 5
	end 'tag'

	export function bonus() returns Integer
		return 30
	end 'bonus'
end 'Five'

function main() returns ExitCode
	let f = Five.make()
	return f.score() as ExitCode
end 'main'
```
```exitcode
35
```

<!-- test: a-bare-call-inside-an-extension-reaches-a-method-a-sibling-extension-published -->
A published method IS the conformer's, so a second extension's body reaches it by the same rule and
through the same index entry. Nothing here can be answered by reading either extension's own member list.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var v as Integer

	export static function of(v Integer) returns Self
		return Self{v: v}
	end 'of'
end 'Box'

extension Box
	export function tripled() returns Integer
		return self.v * 3
	end 'tripled'
end 'Box'

extension Box
	export function reported() returns Integer
		return tripled() + 1
	end 'reported'
end 'Box'

function main() returns ExitCode
	let b = Box.of(5)
	return b.reported() as ExitCode
end 'main'
```
```exitcode
16
```

<!-- test: a-bare-call-inside-the-type-body-reaches-a-method-an-extension-published -->
The same gap seen from the other side, and it is the same one sentence: the walk reads ONE declaration's
member list, so a `type` body could not see what an `extension` published onto it either. `self.tripled()`
compiled here too; bare `tripled()` was **`E3004: call to undefined function 'tripled'`**.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var v as Integer

	export static function of(v Integer) returns Self
		return Self{v: v}
	end 'of'

	export function viaBare() returns Integer
		return tripled()
	end 'viaBare'

	export function viaSelf() returns Integer
		return self.tripled()
	end 'viaSelf'
end 'Box'

extension Box
	export function tripled() returns Integer
		return self.v * 3
	end 'tripled'
end 'Box'

function main() returns ExitCode
	let b = Box.of(5)
	return (b.viaBare() + b.viaSelf()) as ExitCode
end 'main'
```
```exitcode
30
```

<!-- test: a-bare-call-reaches-the-conformers-own-body-where-it-shadows-an-extensions -->
The shadow rule, seen from the bare-call door. `Bag` declares its own `has`, so `extension Holder`'s
`has` is never published onto it and its body is never emitted — a bare `has()` inside a sibling
extension body must therefore reach `Bag.has`, the only declaration there is. `40 + 3`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Holder
	function only() returns Integer
end 'Holder'

extension Holder
	export function has() returns Integer
		return 1
	end 'has'

	export function report() returns Integer
		return has() + only()
	end 'report'
end 'Holder'

type Bag implements Holder
	export static function make() returns Self
		return Self{}
	end 'make'

	export function only() returns Integer
		return 3
	end 'only'

	export function has() returns Integer
		return 40
	end 'has'
end 'Bag'

function main() returns ExitCode
	let b = Bag.make()
	return b.report() as ExitCode
end 'main'
```
```exitcode
43
```

<!-- test: error.a-bare-call-to-a-name-no-declaration-carries-is-still-refused -->
⛔ **THE REFUSAL MUST SURVIVE.** Widening what a bare call inside an extension body may reach is worth
nothing if a name nothing declares becomes a silent no-op or a call to something else. `Box` declares no
`missing`, no extension publishes one, and no free function of that name exists.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var v as Integer

	export static function of(v Integer) returns Self
		return Self{v: v}
	end 'of'
end 'Box'

extension Box
	export function broken() returns Integer
		return missing() + self.v
	end 'broken'
end 'Box'

function main() returns ExitCode
	let b = Box.of(4)
	return b.broken() as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:14:10: call to undefined function 'missing'
```

### The corpus fall-through

<!-- test: an-extension-published-method-is-reachable-on-a-corpus-receiver -->
⭐ **`String.filter` IS A `String` METHOD, AND THE FALL-THROUGH REFUSED IT.** It is published onto
`String` by `stdlib/Interfaces.maxon`'s `extension Iterable`, so `String`'s own body does not declare it
— and the door asked whether the conformer declares it in its own body. The refusal read
**`E2015 … 'filter' — shv2 provides hash/equals; that list IS the surface, so nothing else is served
here`**, about a method the program plainly has.
```maxon
function main() returns ExitCode
	let s = "hello"
	let ells = s.filter(function(c Character) gives c == 'l')
	return ells.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: error.a-corpus-receiver-member-no-declaration-carries-still-meets-the-roster -->
⛔ **THE OTHER HALF OF WIDENING THAT DOOR.** The fall-through now admits any declaration rather than only
the type's own, so the case that says it did not become *"admit everything"* is a member no declaration
anywhere carries: the roster refusal is still what a program gets, naming the list it was measured
against.
```maxon
function main() returns ExitCode
	let s = "hello"
	return s.reticulate() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:11: Unsupported: `String` member 'reticulate' — shv2 provides hash/equals; that list IS the surface, so nothing else is served here
```

### A conformance an extension adds

<!-- test: an-extension-conformance-joins-the-conformers-own-rather-than-replacing-it -->
⛔ **THE UNION WAS A REPLACE, AND THE LAST DECLARATION WON.** `Thing` declares `Shower` on its own body
and `Sizer` on an extension; the declared-conformance index filed a fresh set per declaration, so the
extension's entry overwrote the type's and `display(t)` was refused with
**`E3005 … type 'Thing' does not implement interface 'Shower'`** — about the clause written three lines
above it.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shower
	function show() returns Integer
end 'Shower'

interface Sizer
	function size() returns Integer
end 'Sizer'

type Thing implements Shower
	let value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'

	function show() returns Integer
		return value * 10
	end 'show'
end 'Thing'

extension Thing implements Sizer
	function size() returns Integer
		return value
	end 'size'
end 'Thing'

function display(s Shower) returns Integer
	return s.show()
end 'display'

function measure(s Sizer) returns Integer
	return s.size()
end 'measure'

function main() returns ExitCode
	let t = Thing.create(3)
	return (display(t) + measure(t)) as ExitCode
end 'main'
```
```exitcode
33
```

<!-- test: an-extension-conformance-written-before-the-type-joins-it-too -->
⚠ **THE ORDER CONTROL, AND IT IS THE HALF THAT PROVES THE CAUSE.** The same program with the extension
written FIRST refused the other interface — `E3005 … does not implement interface 'Sizer'` — so the
verdict was decided by declaration order rather than by what the program declares. A case pinning only
one order would pass on a reader that kept the FIRST declaration instead of the last, which is the same
bug facing the other way.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shower
	function show() returns Integer
end 'Shower'

interface Sizer
	function size() returns Integer
end 'Sizer'

extension Thing implements Sizer
	function size() returns Integer
		return value
	end 'size'
end 'Thing'

type Thing implements Shower
	let value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'

	function show() returns Integer
		return value * 10
	end 'show'
end 'Thing'

function display(s Shower) returns Integer
	return s.show()
end 'display'

function measure(s Sizer) returns Integer
	return s.size()
end 'measure'

function main() returns ExitCode
	let t = Thing.create(4)
	return (display(t) + measure(t)) as ExitCode
end 'main'
```
```exitcode
44
```
