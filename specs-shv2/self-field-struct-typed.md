---
feature: self-field-struct-typed
status: stable
keywords: field, self, struct, receiver, E2015, ownership, unsupported, constructible
category: type-system
---
# A Struct-Typed Field Used As A Base Is MATERIALIZED — And Refused Only When Its Type Is On A Cycle

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
(`StructBase`), so it is the one reader of a base binding's box and a caller cannot hold a layout it
proved beside a box it assumed.

⭐⭐ **THE BOX IS LOADED, AND THE REFUSAL IS ONLY ABOUT WHETHER A VALUE OF THE FIELD'S TYPE COULD
EXIST (W66).** Materializing the field — `loadIndirect` the box out of the receiver, then address
that — is what `Parser.baseBoxOf` does for every door that can hold such an alias. What survives as
a refusal is the `mintPhi` trap and nothing else: for `type Node`, `Self{next: …}` needs a `Node` to
put there, and there is no `null` and no base case, so **no program could observe the load being
right**. That argument holds for a type on a cycle in the struct-field graph and for nothing else,
and `ProgramSignatures.structTypeIsConstructible` is its one home — a real walk over that graph, not
a comparison against the enclosing type's name.

⛔ **THE FOUR REFUSAL CASES BELOW ARE ALL `type Node`, WHICH IS WHY THEY ARE UNCHANGED BY THAT
NARROWING.** Every one of them declares `var next as Node` inside `type Node`, so
`structTypeIsConstructible` answers false and the message is exactly the one it always was. A change
to any of their `maxoncstderr` blocks means the narrowing has gone too wide, not that they needed
updating.

⚠ **THE TWO DOORS SPENT ONE RUNG APART, AND THAT ASYMMETRY IS WHAT W66 CLOSED.** The METHOD door
narrowed first (`Parser.structBaseOfReceiver`) while the READ/WRITE door
(`Parser.requireStructBase`) went on refusing every struct-typed self-field alias, because its box
was the alias's `boundValue` — 0, the RECEIVER. So in a perfectly ordinary
`type Outer { export var inner as Inner }` the bare `inner.get()` compiled and ran (measured **exit
41**) one function away from an `inner.x` that was E2015, and the `self.inner.x = 7` spelling of the
refused write compiled and answered **7**. Three spellings of one access, two served and one
refused. Both doors now ask one function (`Parser.requireConstructibleSelfFieldBase`), so a future
narrowing arrives at both or at neither.

⚠ **THE WRITE'S PERMISSION COMES OFF THE FIELD, NEVER OFF THE ALIAS BINDING.** `createSelfField`
binds every alias `mutable: false` (a *rebind* of the alias is never legal) and the receiver it
hangs off is a parameter, so `requireWritableInstance` asked of the alias reports E2013 against an
`export var` field — the third row of the table above, arriving by a different route. The
materialized binding carries `selfFieldIsWritable` in that column, which is the same
`layout.fieldIsMutable` the bare `n = 1` spelling asks. `constructible-field-write` is the pin for a
`var` field and `error.let-field-base-is-immutable` for a `let` one; the two must not come to
disagree.

⛔ **THE OWNERSHIP HOLE THIS FILE USED TO RECORD IS GONE, AND IT WAS RE-MEASURED RATHER THAN
ASSUMED.** The note read: *"`function link(other Node) → next = other` compiles today, to
`storeBaseDispReg.word64 [rcx + 8], rdx` — a struct pointer stored into heap storage with no
incref"*. Neither half holds any more. **That program no longer compiles at all** — `type Node`
itself is refused, **E4014 *"type 'Node' contains a reference cycle (via Node → next: Node)"***,
byte-identical on both compilers (measured W66) — so `next = other` is unreachable, and the cases
below reach their E2015 only because a parser diagnostic outranks a later-stage one. And the store
it warned about now refcounts: `emitCheckedSelfFieldStore` routes through the same `emitFieldWrite`
`p.right = …` uses, whose golden for the constructible twin (`heap-field-assignment.md`'s
`basic-self-field-assign`) carries `__mm_incref` on the incoming value and `__mm_decref` on the one
it displaces, in that order.

⚠ **W66 therefore widened nothing here, and its own write path was measured for it anyway.** A write
*through* a struct-typed base (`inner.value = 7`, `mid.leaf = fresh`) lands on
`parseFieldAssignment`'s `emitFieldWrite` — the same door — and a three-deep replace
(`Top → Mid → Leaf`) ran leak-free, exit 0. What survives of the old note is only the correction it
made to `parseMethodCall`'s claim that *"this rung has scalar and float fields only, so there is
nowhere to store it"*, which is still false as written.

## Tests

<!-- test: error.struct-typed-field-read -->
`next.a` inside `type Node`'s own method. Refused at the BASE token — `next` is what cannot be
addressed, not `a`.

⛔ **ITS SABOTAGE NOTE EXPIRED WITH W66, AND WHAT REPLACES IT IS THE MEASUREMENT.** The note read
*"delete the `isSelfField` arm of `requireStructBase` and this case does not merely go red: it goes
**green with the wrong code**, silently returning `self.a`"* — true of the door that handed back
`binding.boundValue`, which is 0, which is the receiver. **That door materializes now, so there is
no 0 left to return.** Measured on this tree (W66 review) with the constructibility test disabled at
both doors: this case and the two below all answer
**`error E4014: <fragment>:5:6: type 'Node' contains a reference cycle (via Node → next: Node)`** —
a clean refusal one stage later, from `TypeCycleCheck`, whose graph is a strict SUPERSET of
`structTypeIsConstructible`'s (it walks container element types and union payloads too). The other
four cases stayed green. ⇒ **these three pin WHICH diagnostic wins, not whether the program is
refused**, and the parser's wins only because it runs first — the runnable oracle reports E4014 on
this very program, so the E2015 below is also where shv2 and the oracle part company. See
`error.scalar-field-base-is-not-a-struct` for the sabotage on this file that IS still live.
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
error E2015: <fragment>:10:10: Unsupported: a field access through 'next', which is a struct-typed FIELD of the enclosing type (its box has to be loaded out of the receiver before it can be addressed)
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
error E2015: <fragment>:10:10: Unsupported: a field access through 'next', which is a struct-typed FIELD of the enclosing type (its box has to be loaded out of the receiver before it can be addressed)
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
error E2015: <fragment>:10:3: Unsupported: a field access through 'next', which is a struct-typed FIELD of the enclosing type (its box has to be loaded out of the receiver before it can be addressed)
```

<!-- test: error.scalar-field-base-is-not-a-struct -->
**The ORDER inside `requireStructBase`, which is the half a reader would get wrong.** A SCALAR
field's alias is not a struct base either, but it is not a struct-typed field and must not be told
it is: the `structRef` gate is asked FIRST, so `count.x` reports what is actually wrong with it.
Move the `isSelfField` arm above the `structRef` gate — the tidy-looking edit, since both reject —
and this case goes red while the three above stay green. **RUN, not predicted (W66 review):** the
hoisted arm reads 6 passed / 1 failed, this one alone, with `count` told
*"a field access through 'count', which is a struct-typed FIELD of the enclosing type"* — a scalar
blamed on a struct it is not.
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

<!-- test: constructible-field-read -->
**The capability the narrowing opens, READ side (W66).** `inner.value` inside `Outer`'s own method,
where `Inner` is an ordinary type a program can build — so the box is loaded out of the receiver and
addressed, rather than refused. The sibling spelling `inner.get()` went through the METHOD door and
already worked; both are here so the two cannot come to disagree about one access.
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	export function get() returns Integer
		return value
	end 'get'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	export function readIt() returns Integer
		return inner.value
	end 'readIt'

	export function callIt() returns Integer
		return inner.get()
	end 'callIt'

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(Inner.create(20))
	return o.readIt() + o.callIt()
end 'main'
```
```exitcode
40
```

<!-- test: constructible-field-write -->
**The WRITE side, which is where the permission column matters.** A self-field alias is built
`mutable: false` and is not a parameter, so asking the BINDING refuses this program with E2013
*"cannot assign to immutable variable: 'inner'"* — false of an `export var` field. The materialized
binding answers off `layout.fieldIsMutable` instead, which is the same column the bare `value = 7`
spelling asks one indirection in. Sabotage it by threading the scope's binding into
`resolveFieldChainFrom` instead of `baseBindingOf`'s and this case goes red while
`error.let-field-base-is-immutable` below stays green — the two together are what pin the column.
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	export function writeIt(v Integer)
		inner.value = v
	end 'writeIt'

	export function readIt() returns Integer
		return inner.value
	end 'readIt'

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	var o = Outer.create(Inner.create(3))
	o.writeIt(7)
	return o.readIt()
end 'main'
```
```exitcode
7
```

<!-- test: error.let-field-base-is-immutable -->
The other half of the column: the SAME write, through a base field declared `let`. It is refused,
and the E2013 names the BASE — byte-for-byte the runnable oracle's diagnostic on this program
(measured). Without this case a permission that always answered "writable" would look correct.
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	export function writeIt(v Integer)
		inner.value = v
	end 'writeIt'

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(Inner.create(3))
	o.writeIt(7)
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:17:3: cannot assign to immutable variable: 'inner'
```

<!-- test: managed-field-write-through-the-base -->
**THE WRITE PATH WITH A MANAGED FIELD AT THE END OF IT, which is where a materialized base could
lose a refcount and nothing else in this file would notice.** `inner.name = n` reaches
`parseFieldAssignment`'s `emitFieldWrite` through the box loaded out of the receiver, and that store
MOVES the new `String` in before dropping the one it displaces. The bare-self-field twin
(`heap-field-assignment.md`'s `memory.self-field-overwrite-frees-old`) pins the same store one
indirection IN; nothing pinned it through a struct-typed base until W66 made one reachable. The
`self.`-spelled write is here beside it so the two cannot come to disagree, and the field is
overwritten twice so a missed decref is a leak (exit 101) rather than a number that still reads
right. Returns `112` — `11` after the alias write, `2` after the `self.` one.

⭐ **THE GOLDEN IS WHERE THE REAL ASSERTION LIVES: `Outer.setViaAlias` and `Outer.setViaSelf` emit
BYTE-IDENTICAL BODIES**, instruction for instruction and register for register — one
`loadRegBaseDisp [rcx + 0]` for the box (spilled to `slot0` across the allocation), the new `String`
built, `__str_decref` on the one it displaces, then the store. Move-in-then-drop, one load, no
refcount taken on the borrowed struct box. Diff those two functions when this fragment moves: the
two spellings coming apart is the failure this case is watching for, and the exit code alone cannot
see it.
```maxon

typealias Small = int(0 to 1000)

type Inner
	export var name as String

	static function create(n String) returns Self
		return Self{name: n}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	export function setViaAlias(n String)
		inner.name = n
	end 'setViaAlias'

	export function setViaSelf(n String)
		self.inner.name = n
	end 'setViaSelf'

	export function lenViaAlias() returns Small
		return inner.name.byteLength() as Small
	end 'lenViaAlias'

	static function create(i Inner) returns Self
		return Self{inner: i}
	end 'create'
end 'Outer'

function main() returns ExitCode
	var o = Outer.create(Inner.create("abc"))
	o.setViaAlias("hello world")
	let a = o.lenViaAlias()
	o.setViaSelf("hi")
	let b = o.lenViaAlias()
	return a * 10 + b
end 'main'
```
```exitcode
112
```

<!-- test: error.an-iterated-container-reached-through-the-base-is-not-writable -->
⛔ **THE ITERATION LOCK MUST SURVIVE THE MATERIALIZATION, AND IT SURVIVES ONLY BECAUSE
`selfFieldValueBinding` ASKS THE ALIAS.** `baseBindingOf` MINTS A FRESH `VarInfo`, the lock is keyed
on OBJECT IDENTITY, and that fresh object is not the one `iterationLockedBindings` holds — so the
permission has to be computed from the ALIAS still in hand (`selfFieldIsWritable`, whose header
states exactly this) and carried into the new binding's `mutable` column. Every case in
`for-iterated-self-field` uses a BARE self field, so none of them crosses a materialized base; this
is the only case in the suite whose WRITE does.

**SABOTAGE RUN, not predicted (W66 review):** make `selfFieldIsWritable` ask the lock of a FRESH
`VarInfo` instead of the alias it was handed — the exact drift `baseBindingOf` invites, since it
mints one — rebuild, and `--filter=self-field` reads **48 passed / 3 failed**: this case
*"expected a compile error but compilation succeeded"*, together with
`for-iterated-self-field`'s two bare-**CALL** cases. The four bare-**ASSIGN** cases stay RED and
catch nothing, because `parseSelfFieldAssignment` never materializes a binding at all. ⇒ the lock's
survival across a materialized base is pinned HERE and by nothing else on the write side.
```maxon

typealias Small = int(0 to 1000)
typealias StringArray = Array with String

type Inner
	export var items as StringArray

	static function create() returns Self
		var a = StringArray.create()
		a.push("x")
		return Self{items: a}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	export function probe() returns Small
		var n = 0 as Small
		for it in inner.items 'loop'
			inner.items = StringArray.create()
			n = n + it.byteLength() as Small
		end 'loop'
		return n
	end 'probe'

	static function create(i Inner) returns Self
		return Self{inner: i}
	end 'create'
end 'Outer'

function main() returns ExitCode
	var o = Outer.create(Inner.create())
	return o.probe()
end 'main'
```
```maxoncstderr
error E2013: <fragment>:22:4: cannot assign to immutable variable: 'inner'
```
