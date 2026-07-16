---
feature: self-field-struct-typed
status: stable
keywords: field, self, struct, receiver, E2015, ownership, unsupported
category: type-system
---
# A Struct-Typed Field Used As A Base Is REFUSED, Not Silently Addressed As The Receiver

## Documentation

A self-field alias has **no SSA value**. `VarInfo.createSelfField` leaves `boundValue` at 0 because a
read of the alias is a `loadIndirect` through the receiver, not a reference to some value that
already exists — **and ValueId 0 IS the receiver** (parameter 0 is `__self`, and parameter `i` is
bound to id `i`).

`Parser.parseVariableReference` knows this and guards it, and its comment states the hazard exactly:
*"`lookupValue` would hand that 0 back as if it were the answer, and ValueId 0 is the RECEIVER:
`return count` inside a method would silently compile to `return self`."* **The fact was written
down once and needed in four places.** The other three — `parseFieldAccess`, `parseMethodCall` and
`parseFieldAssignment` — each fetched the binding, proved its layout, and then read
`binding.boundValue` on their own.

A scalar field's alias never reaches those three: `requireStructBase`'s `structRef` gate rejects it
first (last case below). But a **self-referential** field does, because `Parser.parseTypeReference`
mints a `structRef` *directly* for the enclosing type's own name — so `var next as Node` inside
`type Node` is a `structRef`, the gate admits it, and `binding.boundValue` is 0.

**Measured on the unguarded code, all three:**

| written | emitted | meaning |
|---|---|---|
| `return next.a` | `loadRegBaseDisp.word64 r8, [rcx + 0]` | `return self.a` — **exit 0, no diagnostic** |
| `return next.readA()` | `callDirect Node.readA`, no argument setup | `self.readA()` — **exit 0, no diagnostic** |
| `next.a = 99` | — | E2013 *"cannot assign to immutable variable: 'next'"*, which is false of an `export var` |

The fix is not a fourth guard. `requireStructBase` returns the **box together with the layout**
(`StructBase`), so it is the one reader of a base binding's `boundValue` and a caller cannot hold a
layout it proved beside a box it assumed.

⚠ **Why REFUSE rather than load it correctly.** Materializing the field first — `loadIndirect` the
box out of the receiver, then address that — is one line. It is deliberately not taken: a
struct-typed field cannot be **constructed** at this rung. `Self{next: …}` needs a `Node` to put
there, and there is no `null` and no base case, so no program could observe the load being right.
That is the `mintPhi` trap exactly — a mechanism handed a consumer and no reason to be right — and
it is the same trap this rung's own self-field scaffold waited out for two rungs. It arrives with
the rung that gives a field a struct type, which is the rung that already owes this one its
receiver-ownership analysis (see `Parser.parseMethodCall`).

⚠ **That rung also inherits a live ownership hole this one does not close**, recorded here because
this is where it was found: `function link(other Node) → next = other` compiles today, to
`storeBaseDispReg.word64 [rcx + 8], rdx` — a struct pointer stored into heap storage with **no
incref**. So `parseMethodCall`'s claim that *"this rung has scalar and float fields only, so there
is nowhere to store it"* is false as written; the receiver-borrow conclusion survives only because
`parseSelfPrimary` refuses a bare `self` as a value, which is a different mechanism than the one the
comment names. The cases below refuse every path that could *observe* a struct-typed field; the
store itself needs the ownership analysis and is not this rung's to invent.

## Tests

<!-- test: error.struct-typed-field-read -->
`next.a` inside `type Node`'s own method. Refused at the BASE token — `next` is what cannot be
addressed, not `a`. Sabotage it by deleting the `isSelfField` arm of `requireStructBase` and this
case does not merely go red: it goes **green with the wrong code**, silently returning `self.a`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Node
	export var a as Integer
	export var next as Node

	export function readNextA() returns Integer
		return next.a
	end 'readNextA'
end 'Node'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:10: Unsupported: a field access through 'next', which is a struct-typed FIELD of the enclosing type (its box has to be loaded out of the receiver before it can be addressed, which arrives with the rung that gives a field a struct type — no struct-typed field can be constructed yet)
```

<!-- test: error.struct-typed-field-method-call -->
The METHOD-CALL path reaches `requireStructBase` through a different caller (`parseMethodCall`) and
must refuse identically — it passed the alias's 0 as the RECEIVER, which is why the unguarded build
emitted a `callDirect` with no argument setup whatsoever.
```maxon

typealias Integer = int(i64.min to i64.max)

type Node
	export var a as Integer
	export var next as Node

	export function callNext() returns Integer
		return next.readA()
	end 'callNext'

	export function readA() returns Integer
		return a
	end 'readA'
end 'Node'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:10: Unsupported: a field access through 'next', which is a struct-typed FIELD of the enclosing type (its box has to be loaded out of the receiver before it can be addressed, which arrives with the rung that gives a field a struct type — no struct-typed field can be constructed yet)
```

<!-- test: error.struct-typed-field-write -->
The WRITE path, anchored at the base token. Before the fix this reported E2013 *"cannot assign to
immutable variable: 'next'"* — a rejection, but by accident (a self-field alias is built
`mutable: false`, which is the BINDING's mutability and says nothing about the field's) and with a
message that is simply false of an `export var` field.
```maxon

typealias Integer = int(i64.min to i64.max)

type Node
	export var a as Integer
	export var next as Node

	export function writeNextA()
		next.a = 99
	end 'writeNextA'
end 'Node'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:3: Unsupported: a field access through 'next', which is a struct-typed FIELD of the enclosing type (its box has to be loaded out of the receiver before it can be addressed, which arrives with the rung that gives a field a struct type — no struct-typed field can be constructed yet)
```

<!-- test: error.scalar-field-base-is-not-a-struct -->
**The ORDER inside `requireStructBase`, which is the half a reader would get wrong.** A SCALAR
field's alias is not a struct base either, but it is not a struct-typed field and must not be told
it is: the `structRef` gate is asked FIRST, so `count.x` reports what is actually wrong with it.
Move the `isSelfField` arm above the `structRef` gate — the tidy-looking edit, since both reject —
and this case goes red while the three above stay green.
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	export var count as Integer

	export function bad() returns Integer
		return count.x
	end 'bad'
end 'Counter'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:10: Unsupported: a field access on 'count', which is declared 'int' and not a struct type (only a struct has fields)
```
