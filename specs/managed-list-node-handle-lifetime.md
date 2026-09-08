---
feature: managed-list-node-handle-lifetime
status: stable
keywords: [managed-list, node-handle, ownership, refcount, escape, drop]
category: ownership
---

# A `__ManagedListNode` handle owns its node

## Documentation

A `__ManagedList with Element` hands out `__ManagedListNode with Element` handles, and **a handle is a
second OWNER of the node it names**, exactly as `/specs/managed-list.md` states in its own first
paragraph: *"__ManagedList owns its nodes via reference counting. Nodes are accessed through
`__ManagedListNode` handles with refcount-based lifetime."* That is what lets a handle leave the
statement that minted it — into a `return`, a struct **field**, a **parameter** — and outlive the chain
itself.

The obligation runs in **both directions**, and a rule that meets only one of them is met by doing
nothing at all:

* a node must **not** be freed while a handle to it lives — missing the handle's reference reads
  `__mm_free`'s `0x3F` poison back as an element, or faults;
* a node **must** be freed, with its element, once its **last** owner dies — missing the release leaks
  the node and everything it holds, which the runner reports as exit **101**.

### The chain lets go; it does not free

`remove`, `detach`, `clear` and the chain's own teardown all drop the LIST's reference to a node and
leave it at that. A node with a surviving handle survives them all, still holding its element — which is
what makes `detach` expressible at all, and what `core.detach` has always asserted.

### The element belongs to the NODE, and that is a deliberate divergence from v1

A node carries its own `element_drop`, copied at allocation from the chain's single stamp, and its
**last owner** is the only thing that ever releases the element. No unlink path touches the value, so a
double drop is not prevented by a rule to remember — it is unrepresentable.

v1 chose the opposite: its walks drop the element off a per-LIST `elem_managed` flag, so a **detached
node's element is released by nobody and leaks** (`runtime.std:5605-5623` against its teardown walk at
`:4867-4871`). It goes unnoticed there because v1's only `detach` coverage is int-valued;
`a-detached-node-releases-a-managed-element-when-its-last-handle-dies` below is that case with a
`String`.

### ⛔ Two earlier designs were measured wrong here, and the second is the instructive one

A handle was first a statement-scoped BORROW that could not escape at all (the drop router panicked on
one in a return or a field). It was then an owned box **retaining the LIST** — and that was not merely
incomplete, it was a wrong answer: `clear` frees nodes, so retaining the chain kept nothing alive, and a
handle stored in a struct field then read `4557430888798830399` = `0x3F3F3F3F3F3F3F3F` at **exit 0**,
with no diagnostic anywhere. E3070 could not reach it — `mintPendingBorrow` files a borrow against the
source NAME the mint saw, and the escape is precisely a handle leaving that name.

⭐ **A precedent is a whole mechanism, not its most quotable half.** That box was argued from
`__ManagedMemoryCursor`, which is safe under the same escapes — because a cursor pays with the retain
**and** a live re-read of its source on every use. Only the retain was ported, and a node has nothing
live to re-read. The three cases that measured it are now ordinary running cases below: under a
refcounted node they need no refusal, because there is nothing left to refuse.

### Where the demand comes from

None of `/specs/managed-list.md`'s canonical cases puts a handle in a field, a return or a parameter —
canonical assumes a refcounted node. The demand is `stdlib/List.maxon`'s, and every case below is a
minimal reproduction of one of that module's own shapes, cited in its prose.

## Tests

<!-- test: a-handle-returned-out-of-the-function-that-made-the-chain -->
`stdlib/List.maxon:60-70` — `walkTo` **returns** an `ENode` out of a chain that the returning function
is the only namer of. The chain binding dies at that `return`; only the handle leaves. Reading the
element in the caller therefore reads through the retain, and nothing else.
```maxon
typealias Small = int(0 to 255)
typealias IntChain = __ManagedList with Small
typealias IntNode = __ManagedListNode with Small

function nodeOfAFreshChain(value Small) returns IntNode
	var chain = IntChain.create()
	return chain.insertLast(value)
end 'nodeOfAFreshChain'

function main() returns ExitCode
	let node = nodeOfAFreshChain(42)
	return node.value()
end 'main'
```
```exitcode
42
```

<!-- test: the-last-handle-to-die-releases-its-chain -->
The other direction, and it needs repetition to be worth anything: four chains are built and four
handles are dropped, each at the end of its own loop trip. A retain with no matching release leaks
every one of them and the run ends **101** rather than **0** — while the printed values still read
correctly, which is exactly why a case that only checks the answer measures nothing.
```maxon
typealias Small = int(0 to 255)
typealias IntChain = __ManagedList with Small
typealias IntNode = __ManagedListNode with Small

function nodeOfAFreshChain(value Small) returns IntNode
	var chain = IntChain.create()
	return chain.insertLast(value)
end 'nodeOfAFreshChain'

function main() returns ExitCode
	for _ in 0 upto 4 'each'
		let node = nodeOfAFreshChain(7)
		print("{node.value()}\n")
	end 'each'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
7
7
7
```

<!-- test: a-handle-stored-in-a-struct-field -->
`stdlib/List.maxon:161-165` — `ListIterator` declares `var node as ENode` and `create` returns
`Self{node: head}`. Reproduced here in all three escape positions at once: the handle arrives as a
**parameter**, is stored in a **field**, and the box is **returned** to a caller that outlives the
chain binding entirely.
```maxon
typealias Small = int(0 to 255)
typealias IntChain = __ManagedList with Small
typealias IntNode = __ManagedListNode with Small

type Cursor
	var node as IntNode

	export static function create(n IntNode) returns Self
		return Self{node: n}
	end 'create'

	export function read() returns Small
		return self.node.value()
	end 'read'
end 'Cursor'

function cursorOverAFreshChain(value Small) returns Cursor
	var chain = IntChain.create()
	return Cursor.create(chain.insertLast(value))
end 'cursorOverAFreshChain'

function main() returns ExitCode
	let cursor = cursorOverAFreshChain(31)
	return cursor.read()
end 'main'
```
```exitcode
31
```

<!-- test: a-handle-field-reassigned-releases-the-one-it-replaced -->
`stdlib/List.maxon:173` — `ListIterator.advance` **reassigns** its own handle field. A reassignment
owes both halves: the replacement's list must be retained and the replaced box's list must be
released. Keeping only the retain leaks (**101**); keeping only the release frees a chain the field
still points into, and the read below then reports poison.

⚠ The canonical shape derives the new handle from the **old** one (`node.next()`), which `W137`
supplies; until then the same reassignment is driven by a second insertion into the same chain. What
this case pins is the reassignment's balance, not the derivation.
```maxon
typealias Small = int(0 to 255)
typealias IntChain = __ManagedList with Small
typealias IntNode = __ManagedListNode with Small

type Cursor
	var node as IntNode

	export static function create(n IntNode) returns Self
		return Self{node: n}
	end 'create'

	export function retarget(n IntNode)
		self.node = n
	end 'retarget'

	export function read() returns Small
		return self.node.value()
	end 'read'
end 'Cursor'

function cursorOnTheSecondOfTwo(first Small, second Small) returns Cursor
	var chain = IntChain.create()
	var cursor = Cursor.create(chain.insertLast(first))
	cursor.retarget(chain.insertLast(second))
	return cursor
end 'cursorOnTheSecondOfTwo'

function main() returns ExitCode
	let cursor = cursorOnTheSecondOfTwo(11, second: 23)
	return cursor.read()
end 'main'
```
```exitcode
23
```

<!-- test: a-handle-into-a-chain-of-managed-elements -->
The same escape over a **managed** element, so the chain's teardown walk actually runs when the last
handle dies. The element is a heap `String` the chain owns; the handle's release has to reach
`__list_clear`'s walk and not merely free the record, or the strings leak. `managed-list-opaque-element`
pairs `String` with `int` for this reason; the trivial twin is the next case.
```maxon
typealias StringChain = __ManagedList with String
typealias StringNode = __ManagedListNode with String

function nodeOfAFreshStringChain() returns StringNode
	var chain = StringChain.create()
	chain.insertLast("alpha")
	return chain.insertLast("a heap string long enough not to be inline")
end 'nodeOfAFreshStringChain'

function main() returns ExitCode
	let node = nodeOfAFreshStringChain()
	print("{node.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a heap string long enough not to be inline
```

<!-- test: a-handle-into-a-chain-of-trivial-elements -->
The trivial control. The identical escape over an element with no destructor: the release must free
the record and the nodes and walk nothing. A run that reports the right answer here and leaks in the
case above has a broken walk, not a broken retain.
```maxon
typealias Small = int(0 to 255)
typealias IntChain = __ManagedList with Small
typealias IntNode = __ManagedListNode with Small

function nodeOfAFreshChain() returns IntNode
	var chain = IntChain.create()
	chain.insertLast(1)
	return chain.insertLast(64)
end 'nodeOfAFreshChain'

function main() returns ExitCode
	let node = nodeOfAFreshChain()
	print("{node.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
64
```

<!-- test: a-chain-passed-as-a-parameter-hands-a-handle-back -->
`stdlib/List.maxon:163` — `ListIterator.create(chain EManagedList)` takes the chain **as a parameter**
and mints a handle from it. The handle outlives the call, so the retain is taken against a list the
callee never owned. Its own binding in `main` is still what finally releases it.
```maxon
typealias Small = int(0 to 255)
typealias IntChain = __ManagedList with Small
typealias IntNode = __ManagedListNode with Small

function appendTo(chain IntChain, value Small) returns IntNode
	return chain.insertLast(value)
end 'appendTo'

function main() returns ExitCode
	var chain = IntChain.create()
	let node = appendTo(chain, value: 55)
	return node.value()
end 'main'
```
```exitcode
55
```

<!-- test: a-returned-handle-survives-a-clear -->
⭐⭐ **THE CASE THE SECOND RULING WAS TAKEN FOR.** `a-chain-passed-as-a-parameter-hands-a-handle-back`
with one line added: the chain is CLEARED while a handle into it is still live, in a different function
from the one that minted it. Under a refcounted node this needs no refusal at all — `clear` drops the
LIST's reference and the handle's own reference keeps the node alive, so the read below is a legitimate
read of a legitimate value. It is `core.detach`'s *"detached node still holds its value"*, reached
through `clear` instead.

⚠ Under the superseded ruling (a handle retained its LIST, nodes stayed unrefcounted) this compiled
clean and printed `4557430888798830399` = `0x3F3F3F3F3F3F3F3F`, `__mm_free`'s poison, at exit 0 — a
wrong answer in every channel. E3070 could not reach it: `mintPendingBorrow` files the borrow against
the source NAME the mint saw, and a returned handle carries no such record into its caller.
```maxon
typealias Small = int(0 to 255)
typealias IntChain = __ManagedList with Small
typealias IntNode = __ManagedListNode with Small

function appendTo(chain IntChain, value Small) returns IntNode
	return chain.insertLast(value)
end 'appendTo'

function main() returns ExitCode
	var chain = IntChain.create()
	let n = appendTo(chain, value: 7)
	chain.clear()
	print("{n.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-handle-in-a-field-survives-a-clear -->
The harder half of the pair, and the one no function boundary explains: `a-handle-stored-in-a-struct-field`
with one line added, with the mint, the `clear` and the read all in `main`. What lost the borrow under the
superseded ruling was the store into a struct FIELD, which no binding then claims — same poison, same
exit 0. Under a refcounted node the field is simply a second owner and the read is correct.
```maxon
typealias Small = int(0 to 255)
typealias IntChain = __ManagedList with Small
typealias IntNode = __ManagedListNode with Small

type Cursor
	var node as IntNode

	export static function create(n IntNode) returns Self
		return Self{node: n}
	end 'create'

	export function read() returns Small
		return self.node.value()
	end 'read'
end 'Cursor'

function main() returns ExitCode
	var chain = IntChain.create()
	let c = Cursor.create(chain.insertLast(7))
	chain.clear()
	print("{c.read()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: reading-a-handle-after-its-element-is-removed-is-refused -->
⭐⭐ **THE CASE THIS RUNG SHIPPED A SEGFAULT OVER, AND THE REASON `remove` KEPT A REFUSAL WHEN `clear` LOST
ONE.** Nothing on this surface FREES a node any more, so `managedListMethodFreesANode` was deleted — but
`remove` still MOVES the element out and empties the node's `value@16`, so the argument's own handle is
left naming a node whose value is gone. MEASURED without this refusal: **exit 139**, a segfault, after
printing the moved string. ⚠ The `int` twin merely prints `0`, which is exactly how it survived a pass —
so this case carries a `String` on purpose.

It is refused per-BINDING (`partiallyMoved`, the bit a binding-`match` sets when it empties a union payload
slot), never per-storage: `core.remove` reads two SIBLING handles after removing a third and stays green.
That is the whole difference from the E3070 this replaces.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let n = chain.insertLast("a heap string long enough to be a real allocation")
	let moved = chain.remove(n)
	print("[{moved}]\n")
	print("[{n.value()}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:9:11: use of moved value 'n': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: removing-through-a-handle-twice-is-refused -->
The same rule reaching the second `remove` rather than a `value()`. Under the superseded model this
FAULTED with `0xC0000005` (the second call unlinked and `__mm_free`d an already-freed node); it then
compiled and answered `7 0 0` once nodes were refcounted; and it is a compile error now, because the first
`remove` took the element and the handle may not be read again to ask for it twice.
```maxon
typealias Small = int(0 to 255)
typealias IntChain = __ManagedList with Small
typealias IntNode = __ManagedListNode with Small

function appendTo(chain IntChain, value Small) returns IntNode
	return chain.insertLast(value)
end 'appendTo'

function main() returns ExitCode
	var chain = IntChain.create()
	let n = appendTo(chain, value: 7)
	let a = chain.remove(n)
	let b = chain.remove(n)
	print("{a} {b}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:23: use of moved value 'n': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: removing-a-handle-no-binding-owns-is-refused -->
⭐⭐ **THE ESCAPE THE PER-BINDING MARK COULD NOT REACH, FOUND AT REVIEW AND MEASURED AT EXIT 139.** The
refusal above is filed against the BINDING that owns the handle, so it is silently INERT on a handle read
back out of a struct FIELD — and a field is exactly where `stdlib/List.maxon`'s iterator keeps one. With
the mark missing, `chain.remove(k.node)` then `k.read()` printed the moved string and then **segfaulted**,
where the pre-rung compiler refused the same program at the drop router. It is the same shape the E3070
half had: a mark filed against a name the value has since left.

⇒ `remove` refuses a handle this statement cannot mark. An unnamed temporary is admitted, because nothing
can name it a second time — `removing-a-handle-minted-in-the-same-statement` below is that control, and
without it this refusal would be indistinguishable from banning the argument position outright.
```maxon
typealias StrChain = __ManagedList with String
typealias StrNode = __ManagedListNode with String

type Keeper
	export var node as StrNode

	export static function create(n StrNode) returns Self
		return Self{node: n}
	end 'create'

	export function read() returns String
		return self.node.value()
	end 'read'
end 'Keeper'

function main() returns ExitCode
	var chain = StrChain.create()
	let n = chain.insertLast("a heap string long enough to be a real allocation")
	var k = Keeper.create(n)
	let got = chain.remove(k.node)
	print("[{got}]\n")
	print("[{k.read()}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:18: Unsupported: `__ManagedList.remove` takes the element OUT of the node, so its handle must be one this statement can mark as moved — a name (`remove(n)`) or an insertion on this same chain, handed in inline. A handle arriving any other way — out of a field, an element, a call result, a merge — would go on answering `value()` from the emptied slot with nothing left to refuse the read; bind it to a name first, or use `detach`, which leaves the element where it is
```

<!-- test: removing-a-handle-minted-in-the-same-statement -->
The control for the refusal above, and the reason it tests for a MARKABLE handle rather than for a NAMED
one. `insertLast`'s result is owned by no binding either, so a refusal keyed on *"is there a binding?"*
alone would refuse this too — and there is nothing here to protect, because no second expression can name
the handle. It runs, hands the element straight back out, and leaves the chain empty.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let got = chain.remove(chain.insertLast("a heap string long enough to be a real allocation"))
	print("[{got}]\n")
	print("{chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[a heap string long enough to be a real allocation]
0
```

<!-- test: removing-a-handle-a-call-hands-back-is-refused -->
⭐⭐ **THE ESCAPE THAT GOT THROUGH THE FIRST CUT OF THE REFUSAL ABOVE, AND THE REASON THE EXEMPTION IS NOW
KEYED BY IDENTITY.** That guard admitted any unnamed owned temporary (`pendingTempsContain`) — which a
METHOD RETU\nING A FIELD'S HANDLE is, exactly as much as an insertion is. MEASURED with the guard as first
written: `chain.remove(k.held())` then `k.read()` printed the moved string and exited **139**. The predicate
was wider than the sentence it was defending, which is this rung's own recurring defect one level up. The
exemption now asks `nodeHandleMintedFrom` — the stamp the ONE mint door writes — so a call result is
refused however it is spelled.
```maxon
typealias StrChain = __ManagedList with String
typealias StrNode = __ManagedListNode with String

type Keeper
	var node as StrNode

	export static function create(n StrNode) returns Self
		return Self{node: n}
	end 'create'

	export function held() returns StrNode
		return self.node
	end 'held'

	export function read() returns String
		return self.node.value()
	end 'read'
end 'Keeper'

function main() returns ExitCode
	var chain = StrChain.create()
	let n = chain.insertLast("a heap string long enough to be a real allocation")
	var k = Keeper.create(n)
	let got = chain.remove(k.held())
	print("[{got}]\n")
	print("[{k.read()}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:25:18: Unsupported: `__ManagedList.remove` takes the element OUT of the node, so its handle must be one this statement can mark as moved — a name (`remove(n)`) or an insertion on this same chain, handed in inline. A handle arriving any other way — out of a field, an element, a call result, a merge — would go on answering `value()` from the emptied slot with nothing left to refuse the read; bind it to a name first, or use `detach`, which leaves the element where it is
```

<!-- test: removing-a-handle-held-only-by-a-parameter-is-refused -->
⭐⭐ **A BORROWED PARAMETER IS REFUSED, AND THIS IS THE ONE WHERE THE REFUSAL IS NOT MERELY CONSERVATIVE —
THE MARK COULD NOT POSSIBLY REACH THE BINDING THAT MATTERS.** The handle belongs to the CALLER; the callee
holds a borrow of it, so marking anything inside the callee leaves `main`'s own `n` readable over a node
whose `value@16` the call emptied. MEASURED with the guard off: exit **139** on the caller's read.
⚠ Rebinding it in the callee (`let held = node`) does not help and is refused too — a borrow rebinds to a
borrow — which is why the diagnostic offers `detach` rather than only *"bind it to a name"*.
```maxon
typealias StrChain = __ManagedList with String
typealias StrNode = __ManagedListNode with String

function takeIt(chain StrChain, node StrNode) returns String
	return chain.remove(node)
end 'takeIt'

function main() returns ExitCode
	var chain = StrChain.create()
	let n = chain.insertLast("a heap string long enough to be a real allocation")
	print("[{takeIt(chain, node: n)}]\n")
	print("[{n.value()}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:15: Unsupported: `__ManagedList.remove` takes the element OUT of the node, so its handle must be one this statement can mark as moved — a name (`remove(n)`) or an insertion on this same chain, handed in inline. A handle arriving any other way — out of a field, an element, a call result, a merge — would go on answering `value()` from the emptied slot with nothing left to refuse the read; bind it to a name first, or use `detach`, which leaves the element where it is
```

<!-- test: removing-a-handle-out-of-an-array-element-is-refused -->
A container ELEMENT is the field case with a different storage: `bag.get(0)` yields a borrow no binding
owns, and the same index reads it again afterwards. MEASURED with the guard off: exit **139**.
```maxon
typealias StrChain = __ManagedList with String
typealias StrNode = __ManagedListNode with String
typealias NodeBag = Array with StrNode

function main() returns ExitCode
	var chain = StrChain.create()
	let n = chain.insertLast("a heap string long enough to be a real allocation")
	var bag = NodeBag.create()
	bag.push(n)
	let got = chain.remove(try bag.get(0) otherwise panic("bag.get: index 0 of a one-element bag"))
	print("[{got}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:11:18: Unsupported: `__ManagedList.remove` takes the element OUT of the node, so its handle must be one this statement can mark as moved — a name (`remove(n)`) or an insertion on this same chain, handed in inline. A handle arriving any other way — out of a field, an element, a call result, a merge — would go on answering `value()` from the emptied slot with nothing left to refuse the read; bind it to a name first, or use `detach`, which leaves the element where it is
```

<!-- test: removing-another-chains-inline-insertion-is-refused -->
⭐ **THE EXEMPTION CARRIES *WHICH* CHAIN, NOT JUST *THAT IT IS A MINT*.** `b.insertLast(x)` is an
insertion's own result and is unnamable, so the *"nothing can name it twice"* half holds — but the node it
mints belongs to `b`, and `a.remove(…)` would unlink it out of `b`'s links while repairing `a`'s header. The
stamp records the chain, so this is a compile error; it is also the one refused spelling caught twice, by
`RuntimeAbort.managedListNodeNotInThisChain` below (MEASURED at exit **77** with the guard off).
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var a = StrChain.create()
	var b = StrChain.create()
	let got = a.remove(b.insertLast("a heap string long enough to be a real allocation"))
	print("[{got}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:14: Unsupported: `__ManagedList.remove` takes the element OUT of the node, so its handle must be one this statement can mark as moved — a name (`remove(n)`) or an insertion on this same chain, handed in inline. A handle arriving any other way — out of a field, an element, a call result, a merge — would go on answering `value()` from the emptied slot with nothing left to refuse the read; bind it to a name first, or use `detach`, which leaves the element where it is
```

<!-- test: removing-a-handle-out-of-a-merge-is-refused -->
A ternary's result is a phi, which no binding owns — marking it would mark neither arm.

⚠ **THIS GUARD USED TO BE CONSERVATIVE AND IS NOT ANY MORE — the second refusal behind it is GONE.** It
read *"with the guard off the program is still refused, by E3102, because building the phi MOVES both arms
into it"*, and that sentence stopped being true when a merge arm reading an IMMUTABLE binding began to
CO-OWN rather than move (⚖ 2026-08-04, `Parser.settleArmGive`). `a` and `b` are `let` bindings holding
owned handles, so MEASURED on this tree both arms now incref and neither is poisoned: with the guard off
this program would COMPILE. What happens after that is not measured here — the guard is what stops it —
but the E3102 backstop this note rested on no longer exists, so the guard is the only thing standing.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let a = chain.insertLast("first heap string long enough to be a real allocation")
	let b = chain.insertLast("second heap string long enough to be a real allocation")
	let pick = chain.count() > 1
	let got = chain.remove(a if pick else b)
	print("[{got}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:18: Unsupported: `__ManagedList.remove` takes the element OUT of the node, so its handle must be one this statement can mark as moved — a name (`remove(n)`) or an insertion on this same chain, handed in inline. A handle arriving any other way — out of a field, an element, a call result, a merge — would go on answering `value()` from the emptied slot with nothing left to refuse the read; bind it to a name first, or use `detach`, which leaves the element where it is
```

<!-- test: removing-a-handle-a-union-arm-bound-runs -->
⭐ **AN ADMITTED SPELLING THAT IS NOT A PLAIN NAME.** A `match` arm binding a managed payload out of a union
IS a live owned binding, so the mark lands on it and a later read in the arm is refused per-binding like any
other name. The route AROUND it — re-matching the box for a fresh binding over the same node — is closed by
the union machinery rather than by this rung: moving the payload out consumes the scrutinee, so a second
`match s` is E3102. Two producers of `partiallyMoved`, and the union's own consume rule is what keeps the
composition honest.
```maxon
typealias StrChain = __ManagedList with String
typealias StrNode = __ManagedListNode with String

union Slot
	empty
	held(node StrNode)
end 'Slot'

function main() returns ExitCode
	var chain = StrChain.create()
	let n = chain.insertLast("a heap string long enough to be a real allocation")
	let s = Slot.held(n)
	match s 'take'
		held(node) then print("[{chain.remove(node)}]\n")
		empty then return 1
	end 'take'
	print("{chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[a heap string long enough to be a real allocation]
0
```

<!-- test: removing-a-handle-a-navigator-hands-back-is-refused -->
⭐⭐ **THE ROSTER RE-RUN AGAINST A SURFACE FOUR TIMES ITS SIZE (`BATCH37`).** The refusal above was
written when TWO doors minted a handle; there are eight now, and *"an insertion on this same chain, handed
in inline"* is a sentence about the two that existed. `n.next()` mints from a NODE — no chain value is in
scope at that door to stamp — so the identity test cannot admit it and it is refused. That is the SAFE
direction and it is also an over-refusal: this particular handle is as unnameable as an inline insertion's.
It is cased rather than quietly relaxed, because relaxing it means teaching the mint door to guess a chain,
which is exactly how an exemption gets widened by a caller.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let a = chain.insertLast("first element, long enough to be a real heap allocation")
	let b = chain.insertLast("second element, long enough to be a real heap allocation")
	let got = chain.remove(try a.next() otherwise 'atEnd'
		return 1
	end 'atEnd')
	print("[{got}] [{b.value()}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:18: Unsupported: `__ManagedList.remove` takes the element OUT of the node, so its handle must be one this statement can mark as moved — a name (`remove(n)`) or an insertion on this same chain, handed in inline. A handle arriving any other way — out of a field, an element, a call result, a merge — would go on answering `value()` from the emptied slot with nothing left to refuse the read; bind it to a name first, or use `detach`, which leaves the element where it is
```

<!-- test: removing-a-handle-this-chains-own-navigator-hands-back-inline -->
The other half of the row above, and the reason the test is IDENTITY rather than "which door minted it".
`chain.tail()` and `chain.insertAfter(…)` both mint from THIS chain, so both carry its stamp and both are
admitted — the handle exists only as this argument and no second expression can name it. Two new mint
doors joining the one exemption, measured rather than assumed.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let a = chain.insertLast("the element that stays behind, on the heap")
	let spliced = chain.remove(chain.insertAfter(a, value: "an element spliced in and taken straight out"))
	print("[{spliced}]\n")
	let last = chain.remove(try chain.tail() otherwise 'empty'
		return 1
	end 'empty')
	print("[{last}] {chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[an element spliced in and taken straight out]
[the element that stays behind, on the heap] 0
```

<!-- test: the-four-node-argument-doors-admit-every-arrival -->
⭐⭐ **THE ROSTER'S ANSWER FOR THE FOUR NEW NODE-ARGUMENT DOORS, AND IT IS "ALL OF THEM" — DERIVED, NOT
PROBED.** `remove` needs a roster because it EMPTIES `value@16`, so a handle that survives the call would
read an emptied slot. `insertAfter`, `insertBefore`, `reinsertFirst` and `reinsertLast` change LINKS and
leave `value@16` exactly where it is, so every handle they are handed goes on answering `value()`,
`next()` and `prev()` afterwards and there is nothing for a later read to be refused about — which is
`detach`'s position, stated once for all five.

This runs every arrival the refusal above enumerates, through the reinsertion: a FIELD, a CALL RESULT, a
borrowed PARAMETER, an inline NAVIGATOR result and an inline INSERTION result. A guard copied here from
`remove` would refuse the first four.
```maxon
typealias StrChain = __ManagedList with String
typealias StrNode = __ManagedListNode with String

type Keeper
	export var node as StrNode

	export static function create(n StrNode) returns Self
		return Self{node: n}
	end 'create'

	export function held() returns StrNode
		return self.node
	end 'held'

	export function read() returns String
		return self.node.value()
	end 'read'
end 'Keeper'

function moveToBack(c StrChain, n StrNode)
	c.reinsertLast(n)
end 'moveToBack'

function headOf(c StrChain) returns String
	let h = try c.head() otherwise 'empty'
		return "<empty>"
	end 'empty'
	return h.value()
end 'headOf'

function main() returns ExitCode
	var chain = StrChain.create()
	let n1 = chain.insertLast("one, long enough to be a real heap allocation")
	let n2 = chain.insertLast("two, long enough to be a real heap allocation")
	let k = Keeper.create(n2)
	chain.reinsertFirst(k.node)
	print("field: {headOf(chain)} / {k.read()}\n")
	chain.reinsertLast(k.held())
	print("call result: {headOf(chain)} / {k.read()}\n")
	moveToBack(chain, n: n1)
	print("parameter: {headOf(chain)} / {n1.value()}\n")
	chain.reinsertFirst(try chain.tail() otherwise 'empty'
		return 1
	end 'empty')
	print("navigator: {headOf(chain)}\n")
	chain.reinsertLast(chain.insertFirst("three, long enough to be a real heap allocation"))
	print("insertion: {headOf(chain)} {chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
field: two, long enough to be a real heap allocation / two, long enough to be a real heap allocation
call result: one, long enough to be a real heap allocation / two, long enough to be a real heap allocation
parameter: two, long enough to be a real heap allocation / one, long enough to be a real heap allocation
navigator: one, long enough to be a real heap allocation
insertion: one, long enough to be a real heap allocation 3
```

<!-- test: a-navigators-handle-outlives-the-chain-it-came-from -->
`head()` mints a handle exactly as an insertion does, and it hands back a node that ALREADY has owners —
so its `+1` is taken inside the runtime entry, on the ok path, where the parser structurally cannot emit
one: an incref at the mint door would sit before the `try` fork and run on the ERROR edge too. This is
`a-handle-returned-out-of-the-function-that-made-the-chain` through the new door: the chain dies at the
`return` and only the handle leaves.
```maxon
typealias StrChain = __ManagedList with String
typealias StrNode = __ManagedListNode with String

function headOfAFreshChain() returns StrNode throws ArrayError
	var chain = StrChain.create()
	chain.insertLast("the element the handle keeps alive, on the heap")
	chain.insertLast("an element nothing keeps alive, on the heap")
	return try chain.head()
end 'headOfAFreshChain'

function main() returns ExitCode
	for _ in 0 upto 8 'each'
		let node = try headOfAFreshChain() otherwise 'empty'
			return 1
		end 'empty'
		print("{node.value()}\n")
	end 'each'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the element the handle keeps alive, on the heap
the element the handle keeps alive, on the heap
the element the handle keeps alive, on the heap
the element the handle keeps alive, on the heap
the element the handle keeps alive, on the heap
the element the handle keeps alive, on the heap
the element the handle keeps alive, on the heap
the element the handle keeps alive, on the heap
```

<!-- test: a-navigators-handle-in-a-field-walks-the-chain -->
⭐ **`stdlib/List.maxon:158-175`'s `ListIterator`, whole.** A handle from `head()` stored in a FIELD, a
`next()` handle REASSIGNED over it on every step, and a read through the field after the chain has been
cleared. Each of the three is a separate obligation — the mint's reference, the reassignment releasing the
one it replaced, and the surviving node outliving the chain — and a rung can satisfy any two of them and
report the right answer.
```maxon
typealias StrChain = __ManagedList with String
typealias StrNode = __ManagedListNode with String

type Cursor
	export var at as StrNode

	export static function create(n StrNode) returns Self
		return Self{at: n}
	end 'create'

	export function step() throws ArrayError
		self.at = try self.at.next()
	end 'step'
end 'Cursor'

function main() returns ExitCode
	var chain = StrChain.create()
	chain.insertLast("one, long enough to be a real heap allocation")
	chain.insertLast("two, long enough to be a real heap allocation")
	chain.insertLast("three, long enough to be a real heap allocation")
	var cursor = Cursor.create(try chain.head() otherwise 'empty'
		return 1
	end 'empty')
	print("{cursor.at.value()}\n")
	try cursor.step() otherwise 'atEnd'
		return 2
	end 'atEnd'
	print("{cursor.at.value()}\n")
	try cursor.step() otherwise 'atEnd2'
		return 3
	end 'atEnd2'
	print("{cursor.at.value()}\n")
	chain.clear()
	print("{cursor.at.value()} {chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
one, long enough to be a real heap allocation
two, long enough to be a real heap allocation
three, long enough to be a real heap allocation
three, long enough to be a real heap allocation 0
```

<!-- test: a-navigator-that-throws-mints-nothing -->
The error edge of all four navigators, two hundred times over. A mint whose `+1` landed BEFORE the `try`
fork would incref a result that is not a node on every one of these trips — and would also displace the
call as the op the `try` rewrite targets, which is a compile-time E3055 rather than a wrong answer. Once
per trip is invisible; two hundred is not.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var empty = StrChain.create()
	var caught = 0
	for _ in 0 upto 50 'headSpin'
		let n = try empty.head() otherwise 'noHead'
			caught = caught + 1
			continue
		end 'noHead'
		print("{n.value()}\n")
	end 'headSpin'
	for _ in 0 upto 50 'tailSpin'
		let n = try empty.tail() otherwise 'noTail'
			caught = caught + 1
			continue
		end 'noTail'
		print("{n.value()}\n")
	end 'tailSpin'
	var one = StrChain.create()
	let solo = one.insertFirst("the only node there is, on the heap")
	for _ in 0 upto 50 'nextSpin'
		let n = try solo.next() otherwise 'atEnd'
			caught = caught + 1
			continue
		end 'atEnd'
		print("{n.value()}\n")
	end 'nextSpin'
	for _ in 0 upto 50 'prevSpin'
		let n = try solo.prev() otherwise 'atStart'
			caught = caught + 1
			continue
		end 'atStart'
		print("{n.value()}\n")
	end 'prevSpin'
	print("{caught} {solo.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
200 the only node there is, on the heap
```

<!-- test: removing-a-node-of-another-chain-aborts -->
⭐⭐ **THE LAST NET, AND THE ONE THE FRONT END CANNOT STAND IN FRONT OF.** Two chains of one element type
share a `__ManagedListNode` type, so a handle carried between two NAMED chains type-checks at the call and
no compile-time rule this rung has can see it. `owner@32` already names the chain a node belongs to, and the
unlink used to gate on it merely being NON-ZERO: `b.remove(nodeOfA)` therefore repaired `b`'s `head`/`tail`
from `a`'s links. MEASURED before the gate read the name: a live `b` reading `count = -1` and then exit
**139** on a `String` element — and, on the `int` twin, exit **0** with two silently wrong counts, which is
how it had been surviving.

⚖ **THE ABORT DECIDES NOTHING ABOUT WHAT THE PROGRAM MEANT.** Whether `list.remove(nodeOfAnother)` should
refuse earlier or deliberately retarget is a semantic question `/specs/managed-list.md` does not answer and
is filed separately. This case pins only that the corruption is over.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var a = StrChain.create()
	var b = StrChain.create()
	let n1 = a.insertLast("first element of chain a, long enough for the heap")
	let n2 = a.insertLast("second element of chain a, long enough for the heap")
	let got = b.remove(n1)
	print("[{got}] a={a.count()} b={b.count()} n2={n2.value()}\n")
	return 0
end 'main'
```
```exitcode
77
```

<!-- test: removing-a-node-the-chain-has-already-let-go -->
⭐ **THE `owner@32` GATE ON THE ROUTE THE REFUSAL DOES NOT COVER, AND A SECOND MEASURED SEGFAULT.** The
refusal above stops a handle being read after ITS OWN `remove`; it says nothing about a node the chain let
go some other way. `clear()` unlinks every node without touching any handle, so `remove(n)` afterwards is
an ordinary compiling program that reaches a DETACHED node — which is what `owner@32` is for: no links to
repair, no `count` to decrement, no reference for the chain to drop.

⚠ It must still hand the element out, and getting that half wrong is not a wrong answer but a DOUBLE FREE:
the move-out originally sat on the linked arm only, so this path returned an element the node still
claimed and the run ended **139**. The move-out is in the block both arms dominate now.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let n = chain.insertLast("a heap string long enough to be a real allocation")
	chain.clear()
	let got = chain.remove(n)
	print("[{got}]\n")
	print("{chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[a heap string long enough to be a real allocation]
0
```

<!-- test: a-detached-node-releases-a-managed-element-when-its-last-handle-dies -->
⭐⭐ **THE CASE v1 GETS WRONG, AND THE ONE THE ELEMENT-DESTRUCTOR DECISION EXISTS FOR.** `detach` unlinks
a node and leaves its element in place; the chain will never walk that node again. If the element's drop
lived on the LIST — v1's design, gated by a per-list `elem_managed` flag — nobody would ever release this
`String` and the run would end **101**. The node carrying its own `element_drop` is what makes its last
owner the one site that can, and the exit code is the whole assertion. The `int` twin is
`a-handle-into-a-chain-of-trivial-elements` above, where the same path must release nothing.

⭐ **IT IS ALSO THE OTHER HALF OF THE `remove` PAIR**, and the reason it reads `n.value()` at all: `detach`
does NOT move the element out, so the handle goes on answering and must NOT be caught by the refusal
`reading-a-handle-after-its-element-is-removed-is-refused` pins. One case each way, on the same element
type, so the narrowing cannot quietly widen.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let n = chain.insertLast("a heap string long enough not to be inline")
	chain.detach(n)
	print("{n.value()}\n")
	print("{chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a heap string long enough not to be inline
0
```

<!-- test: error.an-element-holding-a-handle-into-its-own-chain-is-a-cycle -->
A chain whose element can reach a handle back into that same chain is a retain cycle, and refcounting
cannot collect one — which is why `ownership.md` makes a cycle a COMPILE error rather than a runtime
concern. The cycle here closes through an interface (`Cell` holds a `Backref`; `Holder` is one and holds
the node), so it is `ownership/cycle-through-an-interface` with a `__ManagedListNode` on the return leg
rather than a plain field. ⚠ The container is incidental: the same cycle over two plain structs is
refused by the same rule.
```maxon
typealias Small = int(0 to 255)
typealias CellChain = __ManagedList with Cell
typealias CellNode = __ManagedListNode with Cell

interface Backref
	function tag() returns Small
end 'Backref'

type Nothing implements Backref
	let mark as Small

	export static function create(m Small) returns Self
		return Self{mark: m}
	end 'create'

	export function tag() returns Small
		return self.mark
	end 'tag'
end 'Nothing'

type Holder implements Backref
	var back as CellNode

	export static function create(n CellNode) returns Self
		return Self{back: n}
	end 'create'

	export function tag() returns Small
		return 1
	end 'tag'
end 'Holder'

type Cell
	var back as Backref

	export static function create(b Backref) returns Self
		return Self{back: b}
	end 'create'

	export function retarget(b Backref)
		self.back = b
	end 'retarget'

	export function tag() returns Small
		return self.back.tag()
	end 'tag'
end 'Cell'

function main() returns ExitCode
	var chain = CellChain.create()
	let n = chain.insertLast(Cell.create(Nothing.create(3)))
	var cell = n.value()
	cell.retarget(Holder.create(n))
	return cell.tag()
end 'main'
```
```maxoncstderr
error E4014: <fragment>:22:6: type 'Holder' contains a reference cycle (via Holder → back: CellNode → Cell → back: Backref → Holder); recursive type references are not allowed
```

<!-- test: reading-a-node-a-remove-emptied-aborts -->
⭐⭐ **THE ROSTER ROW THE SURFACE'S GROWTH ADDED, AND IT IS ON THE READ SIDE RATHER THAN THE ARGUMENT
SIDE.** Every row above asks *"which handles may ARRIVE at `remove`"*; this one asks *"which handles
SURVIVE it"*, and the answer is *"all of them but the one it marked"*. `remove` empties `value@16` so the
element it hands out is not dropped twice, and it defends that hole with a per-BINDING move mark on its
argument. `head`/`tail`/`next`/`prev` mint a SECOND, independently named handle on the same node, so the
mark defends one name out of however many the program cares to make — MEASURED as **exit 139** before the
guard, in eight lines with no struct in them.

Which handles alias one node is a run-time fact, so the refusal is a run-time one:
`__list_node_value` aborts (**81**) on an empty slot whose element is MANAGED. ⚠ It is the CORRUPTION
that is stopped, not the semantics that are settled — what `n.value()` on an emptied node should mean is
`/specs/managed-list.md`'s to say.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let a = chain.insertLast("the only element, long enough to be a real heap allocation")
	let h = try chain.head() otherwise 'empty'
		return 1
	end 'empty'
	let got = chain.remove(a)
	print("removed [{got}] count={chain.count()}\n")
	print("the second handle reads [{h.value()}]\n")
	return 0
end 'main'
```
```exitcode
81
```
```stdout
removed [the only element, long enough to be a real heap allocation] count=0
```

<!-- test: reinserting-an-emptied-node-puts-it-back-where-the-chain-itself-reads-it -->
⭐⭐ **AND THE REINSERTIONS CARRY IT INTO THE CHAIN, WHICH IS WHY A STALE HANDLE IS NOT THE WHOLE OF IT.**
`the-four-node-argument-doors-admit-every-arrival` derives its *"admit everything"* from *"they change
LINKS and leave `value@16` alone"* — true of what the member DOES, and silent about the STATE the node
can already be in. A node `remove` emptied is detached, and `reinsertFirst` links a detached node back:
`count` then says 2, and the chain's OWN walk — not a stale handle — reaches the hole. Same abort, one
door further in.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let a = chain.insertLast("the only element, long enough to be a real heap allocation")
	let h = try chain.head() otherwise 'empty'
		return 1
	end 'empty'
	let got = chain.remove(a)
	print("removed [{got}] count={chain.count()}\n")
	chain.reinsertFirst(h)
	print("relinked count={chain.count()}\n")
	let back = try chain.head() otherwise 'empty'
		return 1
	end 'empty'
	print("the chain's own head reads [{back.value()}]\n")
	return 0
end 'main'
```
```exitcode
81
```
```stdout
removed [the only element, long enough to be a real heap allocation] count=0
relinked count=1
```

<!-- test: an-empty-managed-element-is-not-an-emptied-slot -->
⭐ **THE POSITIVE CONTROL THE GUARD IS EXACTLY AS TRUSTWORTHY AS.** The test is *"the slot is 0"*, and it
would be worth nothing — a false abort on ordinary programs — if a managed element could legitimately BE
0. An empty `String` is a real record, so it is not; measured here rather than assumed, because a guard
whose predicate is never shown to be quiet is a guard nobody can tell from a bug.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let a = chain.insertLast("")
	var s = ""
	let b = chain.insertLast(s)
	print("[{a.value()}][{b.value()}] {chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[][] 2
```

<!-- test: a-trivial-element-of-zero-is-the-element-and-not-a-hole -->
⭐ **THE OTHER HALF OF THE SAME CONTROL, AND IT IS WHY THE GUARD LOADS `element_drop@24` AT ALL.** A
trivial element legitimately holds `0`, so an empty slot means nothing on its own — the same two-test
pair `__list_node_decref` already makes to decide whether there is anything to drop. Without the second
test this program aborts.
```maxon
typealias Small = int(0 to 100)
typealias SmallChain = __ManagedList with Small

function main() returns ExitCode
	var chain = SmallChain.create()
	let a = chain.insertLast(0)
	let b = chain.insertLast(7)
	print("{a.value()} {b.value()} {chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 7 2
```
