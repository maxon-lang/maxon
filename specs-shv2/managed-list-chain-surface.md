---
feature: managed-list-chain-surface
status: stable
keywords: [managed-list, insertAfter, insertBefore, reinsert, setValue, node, ownership]
category: collections
---

# The `__ManagedList` members `/specs/managed-list.md` declares and does not corner

## Documentation

`/specs/managed-list.md` is the canonical surface and its cases pin the ANSWERS: what `insertAfter`
splices, what order a `reinsertFirst` leaves behind, that a `setValue`'s displaced value still reads.
This file pins the things that spec's programs cannot reach — the states a node can be in when one of
these members is handed it, and the ownership each member owes for the element it moves.

Every program here uses a **heap `String`** element. That is not decoration: the `int` twin of these
programs prints `0` for a use-after-free and balances no refcount at all, and it hid four of the five
defects `W138` shipped.

### Which members write the chain, and which only read it

`count`, `isEmpty`, `head` and `tail` read the record. Everything else — both end insertions, both
interior insertions, both reinsertions, `remove`, `detach` and `clear` — writes at least one of
`head@0`, `tail@8` and `count@16`, so all nine are refused through an immutable receiver (E3019).
`setValue` is the one WRITE on the node surface, so a `let`-bound handle is refused there for the same
reason; `value`, `next` and `prev` are reads.

### A target in another chain is an ABORT, and a node in another chain is a MOVE

The two are different questions with different right answers, and the asymmetry is deliberate.

`insertAfter(target, value:)` splices a new node between `target` and its neighbour and then repairs
**this** chain's header. Handed a target that belongs to another chain — or a detached one, which has no
neighbours at all — there is nothing correct it can do: splicing into the target's chain would leave the
receiver unchanged while reporting success, and splicing into the receiver's would corrupt both. It
aborts (`77`). ⚠ v1 does neither: its `link_after` documents a caller-side membership check that no
caller of its own performs, and its `nodeNotInList` ordinal is never raised, so a cross-chain
`insertAfter` there corrupts two chains silently.

`reinsertFirst(node)` MOVES a node to this chain's head, and "wherever it is now" is part of what that
means. It unlinks from the chain `owner@32` names — repairing **that** chain's header, which is the one
the node is actually in — and links here. Every state is well defined: linked here (a reorder), linked
elsewhere (a move), detached (a relink), and the chain's element count is right on both sides after.

### No reference is ever released by a reinsertion

v1 increfs at the relink and decrefs the old chain's reference afterwards, and its own header records why
the ORDER is load-bearing: a decref before the relink can take a lone reference to zero, free the node,
and leave the relink writing through freed memory. shv2 takes neither of that pair. A linked node's chain
reference is **handed over** — the unlink drops nothing and the relink takes nothing — and only a
DETACHED node, which no chain holds a reference to, makes the new chain take one. There is no window in
which a count passes through zero, because no count moves down.

## Tests

<!-- test: insert-after-and-before-splice-around-a-named-node -->
Both interior insertions, on one chain, checked by walking the whole thing forwards — and then by reading
every handle again, which is what catches a splice that linked the node into the walk but left a
neighbour pointing at the wrong side.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let a = chain.insertFirst("alpha, long enough to be a real heap allocation")
	let c = chain.insertLast("gamma, long enough to be a real heap allocation")
	let b = chain.insertAfter(a, value: "beta, long enough to be a real heap allocation")
	let z = chain.insertBefore(a, value: "zero, long enough to be a real heap allocation")
	var cur = try chain.head() otherwise 'empty'
		return 1
	end 'empty'
	print("{cur.value()}\n")
	while true 'walk'
		cur = try cur.next() otherwise 'atEnd'
			break
		end 'atEnd'
		print("{cur.value()}\n")
	end 'walk'
	print("{a.value()} | {b.value()} | {c.value()} | {z.value()}\n")
	print("{chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
zero, long enough to be a real heap allocation
alpha, long enough to be a real heap allocation
beta, long enough to be a real heap allocation
gamma, long enough to be a real heap allocation
alpha, long enough to be a real heap allocation | beta, long enough to be a real heap allocation | gamma, long enough to be a real heap allocation | zero, long enough to be a real heap allocation
4
```

<!-- test: inserting-beside-a-node-of-another-chain-aborts -->
Two chains of one element type share a `__ManagedListNode` type, so a handle carried between two NAMED
chains type-checks at the call and no compile-time rule can see it — the same hole `remove` meets, one
member over. `owner@32` names the chain a node belongs to and the splice compares against it.

⚠ The value has already been moved in when the abort fires, and that is why the target is read BEFORE the
node is allocated: an abort ends the process, so there is no continuation in which the element could be
observed leaked. A `throw` here would have to answer for it.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var a = StrChain.create()
	var b = StrChain.create()
	let na = a.insertLast("the only element of chain a, on the heap")
	let nb = b.insertLast("the only element of chain b, on the heap")
	print("before\n")
	let spliced = b.insertAfter(na, value: "a value chain b will never hold")
	print("after [{spliced.value()}] [{nb.value()}]\n")
	return 0
end 'main'
```
```exitcode
77
```
```stdout
before
```

<!-- test: inserting-beside-a-detached-node-aborts -->
The other state `owner@32` catches, and it is not the same program: a detached node belongs to NO chain,
so a splice around it would link the new node to a pair of zeroed neighbours and leave the header
untouched — one element the chain counts and cannot reach. The test is `owner == list`, which answers
both this and the case above; testing merely for non-zero would admit this one.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let n = chain.insertLast("the element of the node about to be detached")
	chain.detach(n)
	print("before\n")
	let spliced = chain.insertBefore(n, value: "a value this chain will never hold")
	print("after [{spliced.value()}]\n")
	return 0
end 'main'
```
```exitcode
77
```
```stdout
before
```

<!-- test: reinserting-a-node-into-its-own-chain-is-a-reorder -->
The self-move, in both directions, including the case a hand-written unlink gets wrong: a chain of ONE,
where the node is both `head` and `tail` and the unlink must leave the chain genuinely empty for the
relink's empty-list arm to adopt both ends again.
```maxon
typealias StrChain = __ManagedList with String

function dump(c StrChain, tag String)
	var cur = try c.head() otherwise 'empty'
		print("{tag}: <empty>\n")
		return
	end 'empty'
	print("{tag}: {cur.value()}")
	while true 'walk'
		cur = try cur.next() otherwise 'atEnd'
			break
		end 'atEnd'
		print(" | {cur.value()}")
	end 'walk'
	print(" ({c.count()})\n")
end 'dump'

function main() returns ExitCode
	var chain = StrChain.create()
	let n1 = chain.insertLast("one, long enough to be a real heap allocation")
	let n2 = chain.insertLast("two, long enough to be a real heap allocation")
	let n3 = chain.insertLast("three, long enough to be a real heap allocation")
	chain.reinsertFirst(n3)
	dump(chain, tag: "tail to front")
	chain.reinsertLast(n3)
	dump(chain, tag: "and back again")
	chain.reinsertLast(n2)
	dump(chain, tag: "middle to back")
	var solo = StrChain.create()
	let only = solo.insertLast("the only element there is, on the heap")
	solo.reinsertFirst(only)
	dump(solo, tag: "a chain of one")
	solo.reinsertLast(only)
	dump(solo, tag: "again")
	print("{n1.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
tail to front: three, long enough to be a real heap allocation | one, long enough to be a real heap allocation | two, long enough to be a real heap allocation (3)
and back again: one, long enough to be a real heap allocation | two, long enough to be a real heap allocation | three, long enough to be a real heap allocation (3)
middle to back: one, long enough to be a real heap allocation | three, long enough to be a real heap allocation | two, long enough to be a real heap allocation (3)
a chain of one: the only element there is, on the heap (1)
again: the only element there is, on the heap (1)
one, long enough to be a real heap allocation
```

<!-- test: reinserting-a-detached-node-links-it-back -->
The member `detach` earns, and the one arm where the new chain takes a reference of its own — a detached
node is held only by handles, so the relink has nothing handed to it. Miss that incref and the node dies
with its last handle while the chain still lists it; take one on the LINKED arm as well and the node
leaks, which the run reports as **101**. Both directions run here.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let n1 = chain.insertLast("one, long enough to be a real heap allocation")
	let n2 = chain.insertLast("two, long enough to be a real heap allocation")
	for _ in 0 upto 8 'churn'
		chain.detach(n2)
		chain.reinsertFirst(n2)
		chain.detach(n2)
		chain.reinsertLast(n2)
	end 'churn'
	print("{chain.count()} {n1.value()} {n2.value()}\n")
	let h = try chain.head() otherwise 'empty'
		return 1
	end 'empty'
	print("{h.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 one, long enough to be a real heap allocation two, long enough to be a real heap allocation
one, long enough to be a real heap allocation
```

<!-- test: reinserting-a-node-of-another-chain-moves-it -->
Where `insertAfter` aborts, this is well defined and runs: the unlink repairs the chain `owner@32`
names — not the receiver's — so both counts are right afterwards, and the one chain reference the node
carried simply belongs to the new chain.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var a = StrChain.create()
	var b = StrChain.create()
	let na = a.insertLast("the element that stays in chain a, on the heap")
	let nb = b.insertLast("the element chain a is about to take, on the heap")
	a.reinsertLast(nb)
	print("a={a.count()} b={b.count()}\n")
	var cur = try a.head() otherwise 'empty'
		return 1
	end 'empty'
	print("{cur.value()}\n")
	cur = try cur.next() otherwise 'atEnd'
		return 2
	end 'atEnd'
	print("{cur.value()}\n")
	print("{na.value()} {nb.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=2 b=0
the element that stays in chain a, on the heap
the element chain a is about to take, on the heap
the element that stays in chain a, on the heap the element chain a is about to take, on the heap
```

<!-- test: setting-a-value-releases-the-one-it-replaced -->
The leak direction of `setValue`, and the reason it needs a loop: a single overwrite that forgets to
release the displaced element still prints the right answer. Sixteen do too — and end at **101**.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	var n = chain.insertFirst("the first element, long enough to be a real allocation")
	for _ in 0 upto 16 'churn'
		n.setValue("a replacement element, long enough to be a real allocation")
	end 'churn'
	print("{n.value()}\n")
	let h = try chain.head() otherwise 'empty'
		return 1
	end 'empty'
	print("{h.value()}\n")
	print("{chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a replacement element, long enough to be a real allocation
a replacement element, long enough to be a real allocation
1
```

<!-- test: setting-a-value-on-a-node-the-chain-let-go -->
A detached node still owns its element, so it still owes the release when one replaces it — and the node
is the only thing that can do it, because no chain lists it any more. The read afterwards is through the
handle alone.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	var n = chain.insertFirst("the element the node is detached holding, on the heap")
	chain.detach(n)
	n.setValue("the element that replaced it after the detach, on the heap")
	print("{n.value()} {chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the element that replaced it after the detach, on the heap 0
```

<!-- test: setting-a-value-through-a-let-handle-is-refused -->
`setValue` is the one member that WRITES a node, so a `let`-bound handle is refused exactly as a
`let`-bound chain's `insertLast` is. `/specs/managed-list.md` writes `var node` for both of its
`setValue` cases and `let` for every read-only one, so the canonical spec already spells the split.

⚠ The runnable oracle accepts this program — it has no immutable-receiver rule on either chain surface at
all, and shv2's is a deliberate departure that predates this member. What would be wrong is to have every
other write on these two types refused through a `let` and this one admitted.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	let n = chain.insertFirst("the first element, long enough to be a real allocation")
	n.setValue("a replacement element, long enough to be a real allocation")
	print("{n.value()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3019: <fragment>:7:4: cannot pass 'n' to function that mutates parameter 'self' (in main)
```

<!-- test: detaching-through-a-let-chain-is-refused -->
`detach` writes `head@0`, `tail@8` and `count@16` and was missing from the receiver-write roster until
`BATCH37` — so `ml.detach(n)` was accepted through a `let` where the identical `ml.remove(n)` was
refused, which is one operation with two answers about the same binding. The chain is built in a helper
so the `let` can hold a populated one.
```maxon
typealias StrChain = __ManagedList with String

function build() returns StrChain
	var c = StrChain.create()
	c.insertLast("one, long enough to be a real heap allocation")
	c.insertLast("two, long enough to be a real heap allocation")
	return c
end 'build'

function main() returns ExitCode
	let chain = build()
	let n = try chain.head() otherwise 'empty'
		return 1
	end 'empty'
	chain.detach(n)
	print("{chain.count()} {n.value()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3019: <fragment>:16:8: cannot pass 'chain' to function that mutates parameter 'self' (in main)
```

<!-- test: a-discarded-insertion-mints-no-handle -->
⭐ **`W148`.** An insertion whose node nobody reads pays no refcount round trip: `insertLast(x)` in
statement position emits the link and nothing else, where it used to emit an `__mm_incref` and a
`__list_node_decref` that the statement's own drain cancelled one instruction later.

⚠ **The predicate is not `resultUsed`**, and this case is the second half of why. A statement whose
postfix chain CONTINUES — `chain.insertLast(x).value()` — forwards the last hop's `resultUsed` to the
insertion, so a mint gated on that flag alone would hand `.value()` a node nobody had increfed. The
chain-continues arrival is admitted by the `.` the next hop is about to consume.
```maxon
typealias StrChain = __ManagedList with String

function main() returns ExitCode
	var chain = StrChain.create()
	for _ in 0 upto 32 'fill'
		chain.insertLast("an element nobody keeps a handle to, on the heap")
	end 'fill'
	chain.insertFirst("another element nobody keeps a handle to, on the heap").value()
	print("{chain.count()}\n")
	let h = try chain.head() otherwise 'empty'
		return 1
	end 'empty'
	print("{h.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
33
another element nobody keeps a handle to, on the heap
```
