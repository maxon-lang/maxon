---
title: Regions & Capabilities
description: "Regions, isolated and immutable data, modify blocks, freeze, inferred parameter capabilities, unique types, tied promises, and the capability errors."
sidebar:
  order: 15
---

A **region** is a group of objects reached through one entry, with nothing outside the group pointing in.
Code running in one function and its callees works in the **current region**: every ordinary record it makes
belongs to it, and records alias freely inside it (see [Reference-by-Default Assignment](/docs/language/memory-model/#reference-by-default-assignment)).
Two further kinds of data sit outside the current region:

```text
 current region                     separate region                  frozen data
 ┌────────────────────────┐         ┌──────────────────┐            ┌──────────────────┐
 │ a ─▶ Table ─▶ rows     │         │ index ─▶ counts  │            │ pinned ─▶ Table  │
 │ b ───────┘             │  entry  │   (isolated)     │            │  (immutable)     │
 │ holder ─ index ────────┼────────▶│                  │            │ read by anyone   │
 └────────────────────────┘         └──────────────────┘            └──────────────────┘
   writable here                      one entry, writable only        read-only for good
                                      inside a `modify` block
```

- **`isolated`** data is a region of its own. One name enters it, and nothing inside points out to the
  current region or to another region. It is written only through a [`modify` block](#modify-blocks) or a
  [one-call open](#one-call-and-one-statement-opens), and it moves between owners as a unit.
- **`immutable`** data is frozen: shared by any number of holders and threads, and read-only for the rest of
  its life. A `freeze` walk marks every object in the region it freezes as shared, so the result is read with
  atomic counts from any thread.

A capability is a prefix word on a type, read like an adjective. The words are contextual:
`mutable`, `isolated` and `immutable` are qualifiers exactly where the grammar expects a type, and a name or an
operand everywhere else. A local named `mutates` or `captures` keeps its meaning, and `swap(1, right: 2)`, with the
parenthesis against the name, calls a function named `swap`, where `freeze (Box.create(7))` with a space is the
operator over a parenthesized operand (`specs/regions-syntax.md`).

| Written | Reads as |
|---------|----------|
| `function pick(pair (immutable Box, Box))` | `immutable` qualifies the first tuple element |
| `function bump(immutable self)` | `immutable` is the receiver's capability |
| `type Cell uses T where T is isolated` | `isolated` is the bound |
| `typealias Visit = function(mutable Box)` | `mutable` qualifies the parameter of type `Box` |
| `swap slot with mutable(seed)` | `mutable(seed)` is a call to a function named `mutable` |
| `typealias A = immutable int` | `immutable` qualifies a type that belongs to no region: **E2010** |
| `typealias B = immutable (X, Y)` | `immutable` qualifies a tuple, which is not a record type: refused |

| Spelling | Meaning |
|----------|---------|
| `isolated T` | a region of its own; a field, parameter, result or `as` target |
| `immutable T` | frozen data; the same positions, and a type argument (`Array with immutable Box`) |
| `mutable T` | on a parameter or function-type parameter, write access to what the caller passed (see [Parameter Passing](/docs/language/functions/#parameter-passing)) |
| `function f(immutable self)`, `function f(mutable self)` | the receiver's capability, written as the first parameter |
| `let x = e as isolated T` | a local that is a region of its own; `as isolated T` is the whole initializer. From a local `r`, the value moves to `x` and reading `r` afterwards is **E3102** |
| `type T is unique` | values that move and are never copied or aliased (see [Unique Types](#unique-types)) |
| `where T is isolated`, `where T is sendable` | bounds on a generic type's parameter (see [Region Bounds](#region-bounds)) |
| `modify x 'label' … end 'label'` | opens an `isolated` value for several statements |
| `freeze x`, `swap path with value`, `merge x` | turn a region into frozen data, take a region out of a slot, bring a region into the current one |

A capability word written directly on a number, `bool`, an enum, a payload-free union or a `String` is not a
type there: those values belong to no region. A union with a payload does, and takes `isolated` and
`immutable` like a record, on the union and on each payload (see [Payload Declarations](/docs/language/enums-unions/#payload-declarations)). `mutable` on a parameter or function-type parameter of a value type is the one
place it is accepted, and it makes the parameter by reference. The same words in other positions fail as any
stray word does: the cases are in `specs/regions-syntax.md`.

A method may declare its receiver as its first parameter, `function reported(immutable self)`, where
inference cannot express what the method needs. `self` is not written at the call. A receiver capability on a
`static` or free function, or `self` after another parameter, is a parse error (`specs/regions-syntax.md`).

The capability checks run on every body of every program, whether or not it spells a region word. In a program
that spells none, what they report is the conversion rule: a function value or a conformer converts to a function type or an interface
requirement only if it declares what it does. One that writes, keeps or returns a parameter, or may wait or
yield, against a type or requirement that does not declare it is **E3210** (`specs/regions-everywhere.md`).

## Frozen Data: `freeze` and `immutable`

`freeze x` turns a region into frozen data. The result is `immutable` and is shared freely. `freeze`
consumes a local, so reading the local afterwards is **E3102**. An expression whose inputs are all
values, `immutable` data or moved `isolated` data cannot point back into the current region, so
`freeze Point.create(2, y: 3)` needs no `isolated` declaration. A local written as ordinary mutable data is
part of its region and cannot be frozen: `freeze cursor.clone()` freezes a copy, and freezing the local
itself is **E3200**, whose message names the fix (``'cursor' is used as mutable at line 15; freeze a copy: `freeze cursor.clone()` ``).
`specs/regions-freeze.md` pins these.

`immutable T` is a type, usable on a typealias, a field, a parameter, a result and a type argument:

```maxon
typealias SettledIndex = immutable IndexBuilder
```

A write through frozen data is **E3197** (`cannot write 'numeric' through 'pinned', which is immutable`), and
storing a mutable value where frozen data is held, or the reverse, is **E3196** (`cannot store a mutable
'Row' into 'pinned', which holds an immutable 'Row'; freeze it, or store a copy`). Storing immutable data
into a plain holder is **E3196** too, and the message names the declaration to change (``cannot store an
immutable 'ByteArray' into 'Tok.value', which holds a mutable 'ByteArray'; declare the field `immutable
ByteArray` ``). A method that writes its receiver, called on immutable data, is **E3207** (`'push' needs a
writable receiver; 'frozen' is immutable`). A value that joins frozen
and mutable data, through a ternary, a `match` arm, a `try … otherwise` or a `var` assigned both, is
read-only (**E3197**: `which is read-only, as it joins immutable and mutable data`). The cases are in
`specs/regions-modify.md` and `specs/regions-inferred.md`.

A generic type argument carries a capability. `List with Box` holds mutable `Box` values of the current
region and `List with immutable Box` holds frozen ones; a value that flows into either position is compared
against it as a store into a slot, so a frozen value into the mutable position and a mutable one into the
`immutable` position are both refused (**E3196** at a store, **E3207** at a call argument that the callee
stores). `Array with immutable Box` and `Array with Box` are
different types (`specs/regions-generic-flows.md`). An element read out of an `immutable` slot is read-only, so a
`for` loop variable over `Array with immutable Box`, and what an `extension` member returns out of such an
instance, is written through only as **E3197**; a `clone()` is the mutable copy.

## Constant Data

A `let` whose initializer is a compile-time constant names **image data**: a literal, a `b"…"` byte string, a
constant record, array or tuple literal, or another constant `let`. The `let` may be a module-level binding, a
`static let`, a `let` field or a local, and the data is `immutable`; every module-level `let` and `static let` is
image data, because its initializer calls nothing (**E3220**). So is every inline `b"…"` literal, bound
or not. Image data allocates nothing at run time and is read with plain counts from any thread.

Immutable data goes where it is only read, or where it is held as `immutable`:

- A parameter that only reads it, an `immutable` parameter, and a parameter the callee stores into an
  `immutable` place all take it.
- A field, array element or union payload declared `immutable T` holds it. Declared as plain `T`, the holder is
  one something may write, so storing it there is **E3196** (``cannot store an immutable 'ByteArray' into
  'Tok.value', which holds a mutable 'ByteArray'; declare the field `immutable ByteArray` ``); the message
  names the holder to declare.
- A result that returns it is declared `immutable`; an unmarked result is **E3209** (``a return does not fit the
  declared result: 'Name' is immutable, the result is not `immutable`; declare the result `immutable` ``).
- Writing through it, binding it to a `var` and writing, or passing it to a parameter the callee writes is
  **E3207** (``'push' needs a writable receiver; 'Name' is immutable``; ``'grow' writes 'name' (line 5:
  `name.push(100)`); 'Name' is immutable``). A mutable copy comes from `.clone()`, or from building the value
  (`ByteArray.create` and `push`, or `"text".toByteArray()`).
- A field default that names it (`var names = sharedNames`) stores it into a mutable field, so it is **E3196**;
  build the value in the default, or declare the field `immutable`. A closure that returns it returns `immutable`
  data (see [Closures](/docs/language/functions/#closures)).

```maxon
let Name = b"abc"

type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let t = Tok.create(Name)         // create only stores its parameter into an `immutable` field
	// Name.push(1)                  // E3207: 'push' needs a writable receiver; 'Name' is immutable
	print("{t.value.count()}\n")     // 3
	return 0
end 'main'
```

Reading an `immutable` field, payload or element slot yields `immutable` data, whatever holds the slot, and the
read leaves the holder as it was: `t.value` goes straight into another `immutable` holder, and a `for` over
an `Array with immutable ByteArray` binds `immutable` names.

Data built at run time is `immutable` once it is frozen, so `freeze` it where it enters an `immutable` holder
(`Tok.create(freeze ByteArray.create(4))`). A `Map` key and a `Set` element are `immutable` slots: a number, a
`String` or another value type needs nothing, a constant is inserted as it is, and a record or array built at
run time needs `freeze` (``'upsert' takes `immutable` 'key'; a fresh value is mutable. Freeze it first``,
**E3207**). A key read back out of a map is `immutable`, so it goes into an `immutable` holder with no copy.

A record whose fields are all `let` is immutable only structurally: every field is `let` and every field's type
is a value type or an immutable aggregate. A record with a `let items as Counts` array field is an ordinary
mutable record, and sending it as `immutable` needs `freeze`.

A record that is an immutable aggregate counts as a value, like a `String`: `immutable` over it means the type
itself, it enters an `immutable` slot or a service message with no `freeze`, and `freeze` of one is **E3200**.
This holds in a tuple element too: `(immutable P, W)` is the type `(P, W)`.

## Parameter Capabilities Are Inferred

An unmarked parameter, and an omitted receiver, takes one of three classes from how the body uses it:

| Class | The body | Accepts |
|-------|----------|---------|
| any capability | only reads it | a mutable, read-only or frozen argument |
| mutable only | writes through it, stores it, captures it, or passes it where a callee needs it mutable | a mutable argument |
| immutable only | sends it, or passes it to an `immutable` parameter or an `immutable self` call | a frozen argument |

A body that needs both is **E3206** (`parameter 't' is used as mutable (it calls 'add', which writes its
receiver) and as immutable at line 27; a parameter cannot be both`). An argument the class refuses is
**E3207**, and the message names the use that decided it:

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Table
	var rows as TallyArray

	static function create() returns Self
		return Self{rows: TallyArray.create(0)}
	end 'create'

	function count() returns Count
		return self.rows.count()
	end 'count'

	function add(n Tally)
		self.rows.push(n)
	end 'add'
end 'Table'

function describe(table Table) returns String
	return "{table.count()} rows"
end 'describe'

function stamp(table Table)
	table.add(9)
end 'stamp'

function main() returns ExitCode
	let live = Table.create()
	let frozen = freeze Table.create()
	print("{describe(live)} {describe(frozen)}\n")    // describe takes either
	stamp(live)                                        // stamp takes the mutable one
	return 0
end 'main'
```

`stamp(frozen)` is **E3207**: `'stamp' writes 'table' (it calls 'add', which writes its receiver); 'frozen' is
immutable`. `specs/regions-inferred.md` (`read-only-param-takes-mutable-and-frozen`,
`transitive-writer-refused`) pins the example and the refusal. A parameter declared `immutable T` takes a
frozen argument only (**E3207**: ``'create' takes `immutable` 'settings'; 's' is mutable. Freeze it first``),
and a method declared `immutable self` runs on a frozen receiver only.

The classes are computed together over the call graph, so they do not depend on declaration order. A union
parameter is judged like a record parameter, and a parameter passed on, even through an alias or as an
interface-typed argument, to a callee that stores or writes it is mutable only, so a read-only argument is
**E3207**.

**A result's capability comes from the arguments at the parameters some return path comes from**, and from
no other argument. Every such argument mutable gives a mutable result. Every such argument frozen gives a
frozen result when each return path returns a parameter, and a read-only one when some path returns a fresh
or local value. Any read-only source gives a read-only result. An argument at a parameter no path returns
plays no part (`specs/regions-results.md`). A call whose return paths return parameters of different
capabilities is **E3208** (`'pick' returns 'a' or 'b'; they must both be mutable or both immutable here`).
A function that declares `returns immutable T` and returns a mutable value, or declares a plain result and
returns an `isolated` one, is **E3209** (`a return does not fit the declared result: 'fresh' is mutable, the
result is immutable; freeze it`). So is a plain result that returns immutable data, a constant or an
`immutable` field read (``'Name' is immutable, the result is not `immutable`; declare the result `immutable` ``).

A value joined from a parameter's data and fresh or local mutable data is judged by the same rule against
the parameter's final class, and a write through it makes the parameter mutable only. A join with frozen data
is read-only (`specs/regions-soundness.md`).

A function whose return paths mix frozen data (its own, such as an `immutable` field, a module `let` or a
`static let`) with writable or fresh data hands back a read-only result to every caller, whatever the receiver
or the arguments are: writing through it is **E3197**, passing it to a `mutable` parameter is **E3207**, and
moving it into an `isolated` position is **E3200**, and calling a `mutates captures` function value it hands
back is **E3211**. One `return` whose `if … else`, ternary or `match` joins
both counts too, and a function that returns a mixed result unchanged is mixed. Declare the function
`returns immutable` when every path is frozen, or return a `.clone()` of the frozen path
(`specs/settled-results-through-merges.md`).

A record built from a parameter's data and returned (`return Self{field: parameter}`) holds that data and only
reads it, so the parameter stays any-capability: a frozen argument gives a read-only record, a mutable one a
writable record. A write through the record that reaches the parameter's data, or handing the record to a callee
that writes that data, stores the record or returns it, makes the parameter mutable only. A parameter a record is built from takes no argument that
is merely read-only, such as data an open `modify` block pauses. A function value, an interface requirement and
a generic method are judged as if they build a record from each unmarked argument they cannot see into
(`specs/returned-and-local-holders.md`).

## Separate Regions: `isolated`

A field, parameter, result or local is declared `isolated`. A local takes the word after its whole initializer
(written inside a call argument, `as isolated T` is a syntax error):

```maxon
let builder = IndexBuilder.create() as isolated IndexBuilder
```

An expression whose inputs are all values, `immutable` data or moved `isolated` data is a fresh region where
an `isolated` position asks for one, so `Self{index: WordIndex.create()}` fills an `isolated` field. A
variable of ordinary mutable data does not become `isolated` by being stored: it is **E3200** (``'spare' is
not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted;
declare it `as isolated Bag`, or store a copy``).

A local initialised from a fresh value (`let spare = Bag.create()`) goes into an `isolated` position the first
time the program reads it: an `isolated` parameter or slot, the initializer of an `isolated` declaration
(`let keep = Slot.holding(spare) as isolated Slot` reads it inside the union literal), `freeze` and `merge`.
That read moves the local, so a later read is **E3102**. A local the program read earlier and one with a second
name are ordinary mutable data and **E3200** (`specs/regions-recover.md`). Writing a value into one of its fields
(`spare.n = 4`) takes no reference out of it and is not such a read.

A local assigned a fresh value (`spare = Bag.create()`) is fresh in the same way, from that assignment on. Where
an `if`, a `match` or a statement `try` joins fresh values, the joined local is fresh until some path has read
it. A loop header joins the value before the loop with the value each back edge brings, the back edges of every
enclosing loop included: a local that a loop relies on as fresh needs all of them to be fresh values nothing has
read, so a lap that assigns it a fresh value on every path to its end, `continue` included, refills it, and any
other lap is **E3200**.

A `unique` argument is a clean input of a call only when its type is closed: every field and payload in its
closure is a value, `immutable`, `isolated`, a service handle, or a `unique` or linear type that is itself closed.
A call given a `unique` argument whose type holds a plain record, array, union, function value or interface value
is not a clean call, and its result goes to no position that wants an `isolated` value (**E3200**). Reading a
plain part out of a `unique` parameter the callee only lends is not a keep, so the caller's value stays usable
after the call.

`isolated` values are linear. Every use of an `isolated` local or parameter moves it except opening it, so
`let b = a` moves `a` and a later read of `a` is **E3102**. An `isolated` parameter owns the region it is
handed; `mutable isolated` is by reference, and the callee may open, swap or replace it, or lend it on to
another `mutable isolated` parameter (a method's own recursion included), but never move it out, alias it,
store it or pass it to an `isolated` parameter (**E3202**). Reading an `isolated` value without opening it, such as
`self.part` outside a block, is **E3198** (``'self.part' is isolated and cannot be read outside a block; open
it with `modify`, or take it out with `swap` ``).

### `modify` Blocks

`modify x 'label' … end 'label'` opens an `isolated` local, parameter or field for several statements. Inside,
`x` keeps its name and is writable; everything outside is read-only except block locals, objects made inside
the block and outer `var`s that hold a number, `bool`, enum, payload-free union, `String` or `Character`:

```maxon
function buildIndex(declarations DeclarationArray) returns SettledIndex
	let builder = IndexBuilder.create() as isolated IndexBuilder

	modify builder 'fill'
		for declaration in declarations 'eachDeclaration'
			builder.add(declaration.clone())
		end 'eachDeclaration'
	end 'fill'

	return freeze builder
end 'buildIndex'
```

The index is filled in a region of its own and frozen on return; `specs/regions-modify.md`
(`build-then-freeze-runs`) runs the whole program. `modify` on a name that is not `isolated` is **E3198**
(``'counter' is not isolated; 'modify' opens an isolated value. Declare it `as isolated Counter` ``). Opening,
swapping, sending or freezing a region needs a writable holder, so one reached through read-only data is
**E3199**, and so is `modify` of a temporary such as a call result: bind it to a local first.

Inside a block:

- Only the region's contents are writable. The slot holding `x` cannot be reassigned or passed by reference
  in its own block, and it cannot be moved or consumed there (**E3203**). An outer `var` that holds anything but a number, `bool`, enum, payload-free
  union, `String` or `Character` cannot be reassigned (**E3204**); the message names what it holds (``'keep' holds
  a linear value from outside 'fill' and cannot be reassigned while 'fill' is open``).
- A mutable value cannot leave the block by `return`, by `throw` or by an outer variable (**E3202**): what
  leaves is a value, `immutable` data or a fresh `isolated` region.
- A nested `modify` leaves the root of every enclosing block read-only.
- An `isolated` local or parameter declared outside the block is read-only inside it and cannot be opened: a
  call that passes it in an unmarked position (as argument or receiver), a read or write through it, and a
  nested `modify` of it are **E3199** (``'outer' cannot be opened: the value that holds it is read-only while
  'fill' is open``). It still goes where an `isolated` value goes: an `isolated` parameter or slot, a send,
  `merge`. An `isolated` local the block creates is opened like any block local. The state of an acquired
  service is held by its `acquire` and is not such a local: inside a `modify` of one state another state is read
  one statement at a time, and a write to it is **E3197** or **E3207**.
- Waiting on another party and running a coroutine are refused: `await`, `awaitAny`, `acquire`, a runtime park
  such as `Scheduler.quiesce()`, and a call that may wait or may yield are **E3205**
  ([Waiting and Yielding](#waiting-and-yielding)). A statement that needs a writable
  argument or receiver of outer data is **E3207** (`'noteFile' needs a writable receiver; 'self' is
  read-only while 'fill' is open`).
- An `isolated` value of a [linear type](#linear-types-and-finalizers) from outside the block cannot be moved
  into a parameter the callee keeps and that is not `isolated`, because it would become part of the open region
  (**E3201**): move it into an `isolated` parameter or slot, send it, or `merge` it. The receiver of a
  `consuming self` method is such a parameter when the method stores, returns or passes `self` on; a finalizer that
  only reads `self` and ends in `destruct self` is accepted. An `isolated` value older than the innermost open
  block stays outside it through every rename inside it: a local it is moved into, the `isolated` result of a call
  it is moved into, the payload a `match` of it binds, the value a failed send hands back in `mailboxFull` or
  `serviceStopped`, and a `mutable isolated` parameter, which is the caller's variable. A union or a recovered
  record built inside the block from such a value holds it with the same standing. Such a value is not opened
  inside the block (**E3199**) and still goes where an `isolated` value goes. A `mutable isolated` parameter open
  in its own block is read inside a block nested in it.
- A linear value from outside the block is read-only inside it, so consuming it there is **E3207**: a finalizer
  call, a `match` over a linear union or a move into a parameter that takes it. A linear parameter whose type
  holds a plain part is such a value too. Consume it before or after the block.
- Outside every open block, a method of an `isolated` receiver takes an un-opened `isolated` local of a linear type
  into a parameter it owns: `bx.eat(v)` moves `v`, and its region joins the receiver's. A non-linear `isolated`
  local, or a plain linear local, beside the opened receiver is **E3201**, and the call inside a `modify` block is
  **E3199**, since the receiver is then an outer value.
- `merge x` moves an `isolated` region into the region the block writes, so `built.reader = merge reader` is
  accepted inside `modify built`, and in a one-statement open as well.
- A `for` over an array the loop drains makes the array read-only in the body (**E3197**).

### One-Call and One-Statement Opens

A call that passes an `isolated` value in an unmarked position, as receiver or argument, opens it for that
call. A statement that reads or writes through an un-opened `isolated` value (`lines.count()`,
`job.number = 4`, `holder.page.lines.push("a")`) opens it for that statement. A number read straight out of an
`isolated` parameter (`self.total = self.total + point.x`) opens nothing, so the statement may write other
data; a read through a record field of one (`point.inner.n`) opens the parameter and leaves the statement's
other data read-only (**E3197**). The same rules apply as in a
block: one `isolated` root per statement, every other operand a value, `immutable` data or a moved `isolated`
value, and only values, `immutable` data and fresh `isolated` data leave. Two roots in one statement are
**E3201** (``one statement reaches through two isolated values, 'left' and 'right'; a statement opens one
isolated value, so open one with `modify` or split the statement``), as is mutable outer data beside the opened
region (`'spare' is mutable outside 'index'; a call into an isolated region takes values, immutable or
isolated`). For an `if` or `while` the open covers the condition alone; a `for` or `match` over the value
opens it for the whole statement.

### Match Arms That Lend an `isolated` Payload

An arm of a `match` that binds an `isolated` payload is a window, as a `modify` block is: data that existed
before the arm is read-only in it. A write to a field of an outer record, of a parameter, of `self`, of the root
of an enclosing `modify` or of an acquired state is **E3197** (`cannot write 'n' through 'b', which is read-only
while 'x1' is open`), and a call that needs that data writable is **E3207**. Data the arm creates, the payload
through its binding and a variable that holds a number are writable. An arm that binds no `isolated` payload
opens no window, even over a case that holds one: `have then o.n = 5` writes outer data, and an arm of
`match h.u` that binds nothing writes `h`. In every arm that binds a payload, whatever the union holds, the
variable the `match` reads stays put: reassigning it, rebinding it in a tuple assignment or passing it to a
`mutable` parameter is **E3203**; its fields stay writable unless the arm binds a `unique` or `isolated`
payload. A `match` whose subject is read through an `isolated` value that is
not open (`match job.state`) still opens that value for the whole statement, every arm included. A union with
a linear payload is consumed by the `match`, so its arms own their payloads and open no window, a frozen union
lends nothing, and a union held in an `isolated` local hands its payload out as a region of its own.
`specs/regions-modify.md` pins the forms (`a-match-arm-lending-an-isolated-payload-refuses-a-write-to-outer-data`),
and `specs/regions-match-lend.md` the arm that binds nothing.

Every other union is ordinary region data, held by a plain `let`, a `var`, a parameter, a field or an element, and
it aliases freely: a second name, a call that keeps it and a call that returns it reach the same payload. The
payload an arm binds is therefore lent to the arm and is a [part read](#part-reads-and-handed-back-results): the
arm opens it, as it opens any `isolated` value it does not own, and an owned `isolated` position (an `isolated`
parameter or slot, a send, `freeze`, a binding) refuses it with **E3198**. A payload bound from a union held in an
`isolated` local is an owned value, and passing it twice to `isolated` parameters is **E3102**.
`specs/regions-match-lend.md` pins both.

An arm that lends a `unique` payload, and a `for` body that lends `unique` elements, are no window when the payload
or element is not `isolated`: outer data stays writable (`have(t) then o.n = 5`), and the holder is protected
instead. The matched variable is not reassigned, rebound, swapped, moved, copied, kept or passed by reference in
its arm. In a lend from a field path (`match h.u`, `for t in h.xs`) the whole root `h` is the holder, read-only
for the lend: a write through it is **E3197** (`cannot write 'n' through 'h', which is read-only while 'arm' lends
a unique value out of it`), and a call that needs it writable is **E3207**. The matched variable itself goes to a
callee that writes through it only in a statement that names no binding of the arm: the receiver, a function
value or an interface method that writes through it beside a binding is **E3102**, because the callee cannot know
that two of its parameters reach one unique value. The binding is a lent `unique` value that is never kept,
stored, moved or sent, and its plain parts are ordinary data of the current region.

### Part Reads and Handed-Back Results

A read of an `isolated` field, element or payload outside a block is a **part read**: data its region still
holds. A part read becomes no other holder's data, whatever road it takes. Binding it, storing it in a field
or a variable, returning it, putting it in a tuple, a union or a constructor, capturing it in a closure,
freezing it, casting it `as isolated`, and handing it to an `async` call, a service message, an `isolated`
parameter or a parameter that keeps it are **E3198**. The read carries its mark through a join (`if … else`,
a ternary, a `match` arm, `otherwise`), through a field of it, through the result of a call given it, through
each element a `for` loop hands out over an `isolated` collection field, and through a closure that captures
it. `.clone()` and a function that `returns isolated` make a fresh value of a part read, and a value read out
of one — a number, a count, a comparison — leaves freely.

A method of an opened region that hands back mutable data of that region (`holder.blob.inner()`) gives a
**handed-back result**, which the same statement consumes: a comparison, a `for` loop over it, or a call
whose other inputs and whose result are values (`holder.blob.inner().count()`). One still unconsumed at the
end of the statement is **E3201** (``'inner' hands back mutable data of the isolated region it opens; use it
inside a `modify` block``). The message quotes the whole argument or receiver that carries the read.
`specs/regions-soundness.md` pins these.

### `swap` and `merge`

`swap path with value` takes the region out of an `isolated` field or element and leaves `value` in the slot:

```maxon
function takeIndex() returns isolated WordIndex
	return swap self.index with WordIndex.create()
end 'takeIndex'
```

`merge x` moves an `isolated` region into the current one, and the result is ordinary mutable data:
`return merge box` returns an `isolated` parameter as a plain result. The moved region is a clean input of a
call, so `freeze Settings.create(merge config)` freezes the call's result. `swap` on a path that is not an
`isolated` field is **E3198**; `merge` of a value that is not `isolated`, and `freeze` of a value type, are
**E3200**. The cases are in `specs/regions-modify.md` and `specs/regions-freeze.md`.

An `isolated` field filled in `modify`, written by a one-call open and taken out whole with `swap` is run in
`specs/regions-modify.md` (`isolated-field-modify-one-call-swap-runs`).

## Unique Types

`type Ticket is unique` declares values that are never cloned and never aliased. `let b = a` moves `a`,
whether `a` is a `let` or a `var` (**E3102** on a later read of `a`); storing a unique value moves it; an argument is lent for the call, and a
callee that keeps or returns its parameter takes the caller's variable. A parameter moves as a local does: after the
function binds it to a second name, stores it, or answers it (`Reply`), a later read of it on a path that
follows, or the same move on the next lap of a loop, is **E3102**. `clone()` is **E3214** (`'Ticket' is unique and
cannot be cloned`):

```maxon
type Ticket is unique
	export var serial as Serial

	static function create(serial Serial) returns Self
		return Self{serial: serial}
	end 'create'
end 'Ticket'
```

A type with a unique field is unique too, with no declaration and no generated `clone()` (**E3214**:
``'Wallet' is unique (its field 'ticket' holds a unique 'Ticket') and cannot be cloned``); so is a type that
holds an array of unique values, and so is a union with a unique payload (`slot.clone()` is **E3214**). A unique
value cannot be frozen, and reading one out of a field or an element
to bind, return or store it is **E3214**, and so is a `for` loop variable or a tuple destructure of one handed
to a parameter that keeps it: move it out with `pop`, `remove`, `removeFirst` or `Slot.take`, or `swap` it out
of an `isolated` field. Handing a unique value to a parameter the callee only reads leaves it
readable, and an element a container call hands back is lent for the call in the same way:
`show(items.get(0))` reads it, and `let ticket = items.get(0)` is **E3214**. A `match` over a unique union lends
it to its arms: each variable the subject comes from (the matched name, the record whose field is matched, and
the operands of a `cond`, a `match` expression or a `try … otherwise` subject, at any depth) is not moved,
copied, stored, captured or passed to a parameter that keeps it inside an arm (**E3214**), while a call that only
reads it is accepted; move or copy it before or after the `match`. The roots of a `cond` subject stay put in
every arm, the arms that bind nothing included: reassigning one, rebinding it in a tuple assignment or passing it
by reference is **E3203**, and when the union has a unique or `isolated` payload the roots are also read-only
there (**E3197**). A variable the `match` holds is not a message or `spawn` factory argument, a reply or an
argument at an `isolated` parameter inside any of its arms (**E3214**), and a call that may hand it back is
**E3200** where its result crosses; send it before or after the `match`. A message argument or `spawn` factory argument is handed to another green thread, so the sender must
own a unique value there: one read from a field or an element, lent by a loop, a tuple or a `match`, or
borrowed through a `mutable` parameter is **E3214**, and an owned one crosses only as the whole of its region
([What crosses a message](/docs/language/async/#services--spawn)).

A `cond` (`a if c else b`), a `match` expression and a `try … otherwise <value>` merge are transparent to unique
values: the merged value may be any unique variable it names. Bound, stored, returned, sent or passed to a
parameter that keeps it, the merge moves every unique variable it may be, whichever arm ran, and a later read of
any of them is **E3102**. Passed straight to a call, used as a `match` subject or used as a `for` source, it lends
them, so each stays usable afterwards and is guarded as the bare name is (a `match` arm cannot hand the matched
union to a callee that keeps it, and a `for` body cannot clear the array). A merge arm that is a unique value
read out of a field or an element, or one lent by a loop, a tuple or a `match`, is **E3214** at the merge (`cannot
join a unique value read out of a field or an element with another value`): the field or element goes on owning
it. Apply the use inside each arm instead, as `f(t) if c else f(fresh)` does.

One unique value named at two operands of one call, tuple, struct literal or case payload is **E3102**
(``'q' is passed at two arguments of this call, and a unique value has one name; pass it once``; a tuple, a
literal and a case say elements, fields and payloads): the operands would hold two names for one value. Two
places of one root where one path leads to the other (`h` and `h.t`) name one value as well, in either order,
while a plain part of the root (`h.data`) beside it does not. A merge that may be the value names it (a `gives`
arm of a `match` expression included), and so does an operand that moves it after an earlier operand lent it, such as a
nested call or tuple that keeps it or a closure that captures it. Name the value once.

The standard library's `Promise` is
unique, so is `Reply`, and so are the OS handle types (`TcpClient`,
`TcpListener`, `TlsStream`, `TlsListener`, `HttpServer`, `StreamingSubprocess`, `SharedSegment`) and every type
that holds one: binding a handle with `let`, pushing it into a container or handing it to a `spawn` moves it, a
later read of the name is **E3102**, and a handle is never cloned. One owner holds a handle and lends it to calls.
The cases are in `specs/regions-unique.md`.

## Linear Types and Finalizers

A `destruct self` statement ends the life of the value a method is called on. A method that contains one is a
**finalizer**, and it takes `consuming self`: a call to it moves the receiver, and reading the receiver
afterwards is **E3102**, and so is a read or write of `self` after `destruct self`. A `destruct self`, or any
other move of the receiver, inside a loop is **E3102** too: the next lap reads a receiver the previous lap
moved. A type with at least one finalizer is **linear**, and a linear type is `unique`: never
cloned, frozen or aliased (**E3214**).

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function use(r Resp)
	print("use {r.peek()}\n")
	r.fin()
end 'use'

function main() returns ExitCode
	let first = Resp.create(1)
	print("peek {first.peek()}\n")
	first.fin()

	let second = Resp.create(2)
	use(second)
	return 0
end 'main'
```

Output: `peek 1`, `fin 1`, `use 2` and `fin 2`. A finalizer belongs to a `type` that holds fields, and a method
that takes `consuming self` contains `destruct self`; either one alone is **E3224**.

- **A linear value is consumed exactly once on every path out of its scope**, throws, early returns and
  propagating `try`s included. A finalizer call consumes it, and so does moving it on: to a function that takes
  it (an argument at a linear position always moves, so a function that takes one consumes it), into a `return`,
  a `throw` or a message. Dropping it, overwriting it, rebinding it while live, discarding it with `let _ = r`,
  or leaving it in a temporary nobody consumes is
  [E3223](/docs/cli/error-codes/#e3223--semanticlinearvaluenotconsumed):

  ```text
  error E3223: this drops 'dropped', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
  ```

- **A linear value lives only in locals, parameters, message arguments and the arguments of a `spawn`.** A field,
  a container or other type argument, a tuple item, a closure capture, a promise payload, a message reply, a union
  stored in a field, the argument or result of an `async` call and an argument of an interface or function value
  call are [E3224](/docs/cli/error-codes/#e3224--semanticlinearrefused): each would keep the value past the code
  that must consume it. A `spawn` of a factory that cannot throw takes a linear argument, which the factory
  consumes; a factory that may throw takes none (E3224), because a factory that fails starts no service and drops
  the value. A linear type argument is reported where the instantiation is written, as E3017 is. A value lent to code that does not own it, such as a parameter of a function that does not
  take it or `self` of a method that does not take `consuming self`, is consumed only by its owner: passing,
  returning or building it into a union there is E3224.
- **No linear value is live across a wait.** An `await`, a send with a `sendTimeout:` and a call that waits
  may never end, so a linear value is consumed before one (E3223).
- **A union with a linear payload is linear.** A `match` over one consumes it: every arm for a case with a
  linear payload binds it to a name and consumes it, and a `default` arm cannot cover such a case (E3224). An
  arm may assign the variable it matched once the payload is consumed, so `some(r) then m = recycle(r)` refills
  `m`; a read of `m` after the `match` needs every arm to have refilled it, and a loop that matches `m` needs
  every path to the end of the lap, `continue` included, to have refilled it. A failed send hands a linear
  argument back in `mailboxFull` or `serviceStopped`, and the handler of the failure consumes it.
- **A `match` consumes the union on its own path.** Each branch of an `if`, and the body and the handler of an
  `acquire`, may match the same union, and a read after the join is **E3102** when any path matched it. The same
  holds for a union with a managed payload that is not linear.
- **A `match` consumes or lends its subject, decided once at its head.** A linear union, a union that holds a
  `Reply`, an `isolated` subject that is not open (a variable or a temporary) and a message parameter without a
  marker are consumed there, in every arm alike: a read of the subject in any arm, or after the `match` on a path
  no arm refilled, is **E3102**, and an arm that binds nothing consumes it too. Any other `match` lends its
  subject and consumes nothing; its holds are described under [Unique Types](#unique-types) and
  [Match Arms That Lend an `isolated` Payload](#match-arms-that-lend-an-isolated-payload).
- **A union held in an `isolated` local, an `isolated` parameter or a behaviour's message parameter is matched where
  it stands.** The `match` hands its payloads over as fresh owned values: the lone plain payload of its case goes
  where an `isolated` value goes (an `isolated` parameter or slot, `freeze`, `merge`), and inside an open `modify`
  window it belongs to the window's region, so the arm may write it, keep it in a call, or store it into the
  window's data. A union that is not linear, held in an `isolated` `let` the `match` consumes, hands its payloads
  out the same way, except a payload that holds a `unique` value. The linear argument a failed send hands back is
  `isolated` too (`specs/regions-recover.md`).
- **An owned linear value goes where an `isolated` value goes only when its type is closed.** An owned linear
  parameter or `consuming self` receiver goes to an `isolated` parameter, slot, declaration or result, a send or
  `merge` only when every field and payload in its type's closure is a value, `immutable`, `isolated`, a service
  handle, or a `unique` or linear type that is itself closed. A plain record, array or union, a function value
  and an interface value are plain parts, which other code may still name: such a value is ordinary data of its
  region, matched, finalized or moved on to another plain linear position, and an `isolated` position refuses it
  with **E3200**, naming the plain part. Declare the part `isolated`, or consume the value where it is.
  The payload a `match` of a union in an `isolated` local binds keeps the standing of that local.
- **A case that carries no linear value owes nothing.** A variable of a linear union that holds such a case
  (`Maybe.nothing`) on every path to a point is not a linear value there: assigning it, leaving its scope or
  leaving it live across a wait is not a drop. A branch, a loop lap or a `try` handler that can leave the linear
  case in it keeps the obligation, and so does a value that came from a call. Such a variable takes an `isolated`
  value without `merge`; once it may hold the linear case on some path, storing that value is **E3196**.
- **Cancellation consumes.** A coroutine that holds a linear value is cancelled
  [cooperatively](/docs/language/async/#cancellation-and-dropped-promises), so its own code consumes the value on the path the
  early return of its wait leads it down.

The standard library's [`HttpResponder`](/docs/stdlib/network/#httpresponder) is linear: `respond`,
`respondWith` and `abandon` are its finalizers.

## Region Bounds

A generic type may bound a type parameter in its own `where` clause:

```maxon
type Pool uses T where T is sendable
	export var item as T
	...
end 'Pool'
```

`T is sendable` takes an argument that stands as sendable and whose graph can be shared. A value stands as
sendable — a scalar, `String`, `Character`, a C string, or a type that is not `unique` and owns no counted box
(a union with no payload, a record packed in a word) — and so do a service handle, a `SharedValue` handle and a
type written `immutable` that is not `unique`. A function type, an interface type and a promise are refused,
written `immutable` or not. A plain record, a container instance, a tuple and a boxed union, which the caller
keeps sharing, are refused bare and taken as `immutable Job`; a `unique` type, whose values move, is refused
whatever its size. The graph must pass the share walk `freeze` and `SharedValue.publish` use, so an `immutable`
record holding a function value or a promise is refused. The bound judges the type's own graph: what an
interface-typed field holds is judged at the `freeze` that shares it, in a program that spawns a service.
`T is isolated` takes the same values and handles, or a `unique` type.

Each written instantiation is checked against the bound once, where it is written: at a `typealias`'s name, at
the base name of an instance written inside its arguments (`typealias StepPools = Array with (Pool with Step)`
reports at `Pool`), at a literal, or at a bare generic name that the enclosing generic type binds (`Pool` inside
`type Outer uses T` means `Pool with T`). An alias used as an argument is judged only at its own declaration. An
argument written inside a generic type `Outer` that is or mentions a parameter of `Outer` is judged there, from
`Outer`'s own `where` clause alone, once per declaring type at its first site, however many instantiations
`Outer` has (none included):
a bare `T` meets `sendable` only when `Outer` declares `where T is sendable`, and meets `isolated` only when it
declares `where T is isolated`; neither proves the other, so a type that fills both kinds of position declares
both (`where T is sendable and isolated`). An `immutable` argument that mentions a parameter (through an alias,
a tuple, an instance argument, a phantom one included, or a function type) meets `sendable` when every
parameter it mentions is bounded `sendable` in `Outer`; the refusal names the first unbounded one by owner and
position. A bare aggregate over a parameter is refused, and so is a function type, an interface type and a
promise, whatever the parameter is. Under `isolated`, an aggregate over a parameter is met only when it is
`unique` whatever the parameter is. `Outer with X` is judged against `Outer`'s own bounds, and an
instantiation of a bounded `Outer` leaves an inner type's bound to where `Outer` writes it. An interface's
associated type carries no bound; in an `extension` it is the conformer's binding, judged for each conformer.
A generic function called with the caller's type parameter as its argument is refused at the call (**E3181**).
A `spawn` that infers a generic service's argument is reported at the factory the `spawn` calls. A generic
service's factory parameter or reply declared as the bare `sendable`-bounded parameter is judged by the bound
alone; a type built over it (`Array with T`) crosses as any other type does (**E3217**). A refusal is **E3215** (``'Job' is not sendable, so it cannot be 'T' of 'Pool',
which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Job`, or a value
or a service handle``; ``'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares
`where T is sendable`: declare `where T is sendable` on 'Outer'``). A function's or an extension's `where` clause cannot spell either bound (**E3215**).
A generic type accepts an `isolated` type argument only when it declares `where T is isolated`
(`Wrapper with isolated Box` over an unbounded `Wrapper` is a parse error). `specs/regions-bounds.md` pins
these.

## Capabilities at Function Types and Requirements

A function type and an interface requirement have no body for the checker to read, so they declare what the
callee does. Unmarked, they are read-only and neither wait nor yield:

| Spelling | Meaning |
|----------|---------|
| `function(mutable T)`, `function f(mutable self)` | the callee writes that parameter, or the receiver |
| `function(immutable T)`, `returns immutable T` | the callee reads frozen data, or hands frozen data back |
| `returns isolated T` | the result is a region of its own |
| `function(…) returns R mutates captures` | the closure may write what it captured |

```maxon
interface Grows
	function grow(mutable self, by Tally)
end 'Grows'

typealias Step = function(mutable Counter) returns Tally
```

A closure, function value or conformer converts to one only if each unmarked parameter, its receiver and
each capture is read and not written, and it neither waits nor yields. Anything else is **E3210**:

```text
error E3210: 'Bag.grow' writes its receiver, but 'Grows.grow' does not declare `mutable self`
error E3210: function value for 'Look' writes its parameter 'counter', but 'Look' does not declare it `mutable`
error E3210: function value for 'Array.SortComparator' writes data it captured, but 'Array.SortComparator' does not declare `mutates captures`
error E3210: function value for 'Step' may yield (it awaits a coroutine at line 12), but a function type never waits or yields
error E3210: 'Relay.echo' may wait (it awaits a reply from 'Bouncer' at line 28), but a requirement never waits or yields
error E3210: function value for 'KeyOf' returns data reached from its read-only parameter 'key', which 'KeyOf' does not allow
```

A function value that returns its read-only parameter, or throws it in an error payload, converts once it
returns a `.clone()` of it (`specs/regions-everywhere.md`).

A closure that writes what it captured, or builds a record from it, mutates its captures; the compiler infers
it, so nothing is written on the closure. It converts only to a function type that declares `mutates captures`,
and a plain function type refuses it (**E3210**). A closure that writes through a captured iterator writes the
collection the iterator runs over, which therefore has to be mutable data
(`specs/closure-captured-iterators.md`). A conformer whose result holds data from its receiver needs
`mutable self` on the interface requirement (**E3210**).

A conformer or function value whose declaration marks a position the boundary leaves unmarked, or the
reverse, is refused too. A value of a `mutates captures` type called through frozen or read-only
data is **E3211** (``'Tallier' mutates captures and cannot be called through immutable data``): on frozen data
anywhere, and inside an open `modify` block when the value was returned from, read out of or stored in the data
the block holds.

A caller trusts the declaration: an argument at a `mutable` position is writable, and one at an `immutable`
position is frozen (**E3207**). A result read through an interface or function value is read-only, so a
requirement that wants to hand back data to store declares `returns isolated T` or `returns immutable T`.
`specs/capability-qualifiers.md` and `specs/regions-convert.md` pin these.

## Waiting and Yielding

Two facts are inferred for every function, with no marker. A function **may wait** when it waits on another
party: a service reply, `awaitAny`, a send with a timeout, an `acquire`, a spawn of a factory that throws, a runtime park (`Scheduler.quiesce()`, `CownTorture.violations`), or a call to a function
that may. A function **may yield** when it awaits a coroutine, or calls one that may yield or may wait. File,
socket and process I/O and `sleep` are neither: they park the green thread, and while a `modify` block is open the
runtime does not switch to a sibling coroutine of the same green thread, so the thread waits for the block
(`specs/regions-runtime.md`, `break-leaves-window-lets-sibling-run`).

A message or a `spawn` factory that may wait is
[E3221](/docs/cli/error-codes/#e3221--semanticholdermaywait): [Holders Never Wait](/docs/language/async/#holders-never-wait).

Inside a `modify` block, an `acquire` block or a one-call open (a call that passes an `isolated` value in an
unmarked position, as receiver or argument), a coroutine `await`, `awaitAny`, a call that may wait or yield, the
start of a coroutine that may wait and a `spawn` of a factory that may wait or yield are **E3205**, and the
message names why. A callee that only reads, writes or sleeps is accepted:

```text
error E3205: cannot call 'settle' inside 'fill'; it may yield (it awaits a coroutine at line 12). Call it before or after the block
error E3205: cannot call 'ask' inside 'fill'; it may wait (it awaits a reply from 'Oracle' at line 16). Call it before or after the block
```

A call through a function value that can be anything feeds the caller's may-yield bit, and a call whose body
cannot be read counts as may-wait. Await before the block and pass the result in.

## Coroutines and Regions

An `async` call that takes the caller's mutable data produces a **tied promise**, and every promise held in a
local is **must-use**. [Tied and Must-Use Promises](/docs/language/async/#tied-and-must-use-promises) states both rules. A variable
handed to a `mutable` parameter of an `async` callee is lent to the coroutine until the promise is awaited or
cancelled ([Starting a Coroutine](/docs/language/async/#starting-a-coroutine)).

## Pending Operands

A value already read for one operand survives the operands after it. A field read used as a constructor,
union-literal, send, `spawn` or `async` argument is held across every later operand that may run user code,
and a `match` subject is held across the arms that write back (`specs/regions-holds.md`). A by-reference
write is the exception a hold cannot cover, and E3186 refuses it at compile time
([Aliasing Between Arguments](/docs/language/memory-model/#aliasing-between-arguments)).

## Capability Errors

| Code | Refuses |
|------|---------|
| [E3196](/docs/cli/error-codes/#e3196--semanticcapabilitymismatchatstore) | storing a value of one capability where another is held |
| [E3197](/docs/cli/error-codes/#e3197--semanticwritethroughreadonly) | a write through frozen or read-only data |
| [E3198](/docs/cli/error-codes/#e3198--semanticisolatedreadoutsideblock) | an `isolated` value read outside a block, `modify` or `swap` of a value that is not `isolated` |
| [E3199](/docs/cli/error-codes/#e3199--semanticisolatedholdernotwritable) | opening, swapping, sending or freezing a region held by read-only data, and `modify` of a temporary |
| [E3200](/docs/cli/error-codes/#e3200--semanticisolatedrecoverneedscleaninputs) | ordinary mutable data where an `isolated` value is wanted, and `freeze` or `merge` of what they do not apply to |
| [E3201](/docs/cli/error-codes/#e3201--semanticisolatedcallopenstworegions) | one statement reaching two isolated regions or two acquired states, mutable outer data beside one, or an outer `isolated` linear value moved into an open region |
| [E3202](/docs/cli/error-codes/#e3202--semanticvalueleaveswindow) | a mutable value leaving a `modify` or `acquire` block, an acquired state leaving its block, a `mutable isolated` parameter moved out |
| [E3203](/docs/cli/error-codes/#e3203--semanticopenedslotreassigned) | the root of an open `modify` block, or of what a `match` or `for` lends, reassigned, rebound, passed by reference or moved while it is open |
| [E3204](/docs/cli/error-codes/#e3204--semanticouterdatawritteninwindow) | an outer `var` holding anything but a number, `bool`, enum, payload-free union, `String` or `Character` reassigned in a block |
| [E3205](/docs/cli/error-codes/#e3205--semanticwaitinsidewindow) | waiting or yielding inside a `modify` or `acquire` block or a one-call open |
| [E3206](/docs/cli/error-codes/#e3206--semanticparamneedsbothcapabilities) | a parameter the body needs both mutable and immutable |
| [E3207](/docs/cli/error-codes/#e3207--semanticargumentcapabilityrefused) | an argument whose capability the parameter refuses, and a linear value from outside an open block consumed in it |
| [E3208](/docs/cli/error-codes/#e3208--semanticargumentclassesdisagree) | a result whose source arguments disagree |
| [E3209](/docs/cli/error-codes/#e3209--semanticresultpathsdisagree) | a return that does not fit the declared result |
| [E3210](/docs/cli/error-codes/#e3210--semanticcapabilityconversionrefused) | a function value or conformer that does more than its function type or requirement declares |
| [E3211](/docs/cli/error-codes/#e3211--semanticmutablefunctionconversionrefused) | a `mutates captures` value called through frozen or read-only data |
| [E3212](/docs/cli/error-codes/#e3212--semantictiedpromiserefused) | a tied promise returned, stored, captured, sent, passed on, or left unconsumed on an exit |
| [E3213](/docs/cli/error-codes/#e3213--semanticpromisedropped) | a promise dropped, overwritten or discarded |
| [E3214](/docs/cli/error-codes/#e3214--semanticuniquevaluerefused) | a unique value cloned, aliased, frozen, moved out of a slot it was only read from, or sent from an arm of the `match` that lends it |
| [E3215](/docs/cli/error-codes/#e3215--semanticregionboundrefused) | a type argument that breaks a `where T is sendable` or `where T is isolated` bound |
| [E3216](/docs/cli/error-codes/#e3216--semanticservicestateholdsapromise) | a service type whose state can hold a promise |
| [E3217](/docs/cli/error-codes/#e3217--semanticmessageneedsacapability) | a service message or factory parameter, or a message reply, that holds data of its region and names neither `isolated` nor `immutable`, or whose type cannot cross at all (a promise holder, a function value, an opaque type parameter, a thread-bound handle) |
| [E3221](/docs/cli/error-codes/#e3221--semanticholdermaywait) | a message or `spawn` factory that may wait on another party while it holds its own service |
| [E3222](/docs/cli/error-codes/#e3222--semanticreplydeferralrefused) | `reply.later()` or a `Reply` used where the holder could not answer it exactly once, or a `Reply` leaving its service's code |
| [E3223](/docs/cli/error-codes/#e3223--semanticlinearvaluenotconsumed) | a linear value dropped, overwritten, discarded, or live across a wait |
| [E3224](/docs/cli/error-codes/#e3224--semanticlinearrefused) | a linear value in a field, container, tuple, capture, promise, reply or other place that cannot consume it exactly once, and a finalizer or `consuming self` written outside its rules |
| [E3226](/docs/cli/error-codes/#e3226--semantictuplecastchangescapability) | a tuple cast that gives an element a different capability |
