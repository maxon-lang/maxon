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
A ternary's result is a phi, which no binding owns — marking it would mark neither arm. ⚠ **THIS ONE IS
CONSERVATIVE RATHER THAN LOAD-BEARING, AND SAYING SO IS THE POINT**: with the guard off the program is still
refused, by E3102, because building the phi MOVES both arms into it. It is in the roster because a refused
spelling with no measured segfault behind it is a different claim from one with, and a reader who cannot
tell them apart will delete the wrong one.
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

<!-- disabled-test: an-element-holding-a-handle-into-its-own-chain-leaks -->
<!-- W138 review, MEASURED: exit 101. The retain cycle PLAN's W138 row named as the trap to measure before building, and it IS reachable: E4014 refuses `Cell -> CellNode -> Cell` in the type graph, but it does NOT traverse an EXISTENTIAL, so a `Backref`-typed field whose conformer holds the handle closes the loop. Controls: retargeting to a handle-free conformer exits with the tag and no leak, and a handle into a DIFFERENT chain exits with the tag and no leak — so the 101 is the cycle and not a refcount imbalance. FILED AS W144. Re-measured under the second W138 ruling (refcounted nodes): unchanged at 101, with both controls still clean, and the same cycle spelled WITHOUT a node handle (an existential holding the chain itself) leaks identically on a pre-W138 compiler — so this is a pre-existing class, not something either W138 design created -->
A chain whose element can reach a handle into that same chain is a retain cycle, and refcounting cannot
collect one. The type-graph cycle check (E4014) refuses the direct spelling and does not see this one.
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
```exitcode
1
```
