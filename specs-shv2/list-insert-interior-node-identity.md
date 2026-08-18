---
feature: list-insert-interior-node-identity
status: experimental
keywords: [list, insert, remove, clear, node, owner, refcount, drop, leak]
category: memory
---

# A Middle-Inserted `List` Node Is a Full Member of Its Chain

## Documentation

Every node in a chain carries two words beyond its links: `element_drop@24`, the per-element destructor
`__list_node_decref` calls at the node's last owner, and `owner@32`, the chain the node belongs to. Both are
written by the two entries that mint a node at an END — `__list_node_alloc` copies the stamp off the record,
and `emitListPublishEnd` calls `emitListClaimsNode`.

⭐ **`__list_insert`'s INTERIOR arm is the one place a node is linked WITHOUT `emitListPublishEnd`, so it
owes both by hand.** Its two end arms are `__list_prepend`/`__list_append`, called, and inherit everything;
the middle splice writes four link words itself and must therefore stamp the destructor and record the owner
itself as well.

⛔ **NEITHER OF THE TWO BRANCHES THAT MET HERE COULD SEE THIS, AND THAT IS THE WHOLE REASON THIS FILE
EXISTS.** `__list_insert` was written (spec-port `list`) against ONE-OWNER nodes, which had neither word;
`W138` gave nodes both words and put every *other* insertion through the helper that writes them. Each side
was green. Composed, a middle-inserted node came out with `element_drop@24 == 0` and `owner@32 == 0`:

- **the stamp** — its element is never released, because `__list_node_decref` is the one site that drops a
  node's element and it reads that word to decide whether there is one. A `clear()` over a heap `String`
  inserted in the middle exits **101**.
- **the owner** — `__list_remove_node` gates its unlink on `owner@32` naming *this* chain, so a later
  `remove(at:)` takes the already-detached arm: it hands the element out and leaves the node linked, the
  header un-decremented and every index past it naming an emptied slot.

⚠ **`/specs/list.md` CANNOT CATCH EITHER HALF**, which is why these cases are shv2-authored rather than
ported. All four of that file's `insert` cases use `int` elements (the stamp is 0 for a trivial element by
design, so the first half is invisible), and not one of them removes or re-reads through the node it
inserted (so the second half is invisible too). The `int` case below is the **control** that says the
difference is the managed element and the chain membership, not the insertion.

## Tests

<!-- test: a-middle-inserted-managed-element-is-released-by-clear -->

The stamp half. A heap `String` spliced into the middle, then a `clear()` that lets go of every node — the
element must die with its node. Without `element_drop@24` copied onto the interior node this printed the
right answer and exited **101**, which is why the exit code is pinned and not just the output.

```maxon
typealias StringList = List with String

function main() returns ExitCode
	var list = StringList.create()
	list.append("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
	list.append("cccccccccccccccccccccccccccccc")
	try list.insert(1, value: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb") otherwise 'oob'
		return 2
	end 'oob'
	let middle = try list.get(1) otherwise "?"
	print("{middle} {list.count()}\n")
	list.clear()
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 3
0
```

<!-- test: a-middle-inserted-node-can-be-removed-by-index -->

The owner half. `remove(at:)` walks to the node and hands it to `__list_remove_node`, whose `owner@32` gate
asks *"is this node linked HERE?"* — a question a node the interior splice never claimed answers with `no`.
The removal then became a silent no-op that still handed the element out: `count` stayed at 3 and `get(1)`
read the slot the move-out had just emptied.

```maxon
typealias StringList = List with String

function main() returns ExitCode
	var list = StringList.create()
	list.append("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
	list.append("cccccccccccccccccccccccccccccc")
	try list.insert(1, value: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb") otherwise 'oob'
		return 2
	end 'oob'
	let removed = try list.remove(1) otherwise "?"
	print("{removed} {list.count()}\n")
	let first = try list.first() otherwise "?"
	let last = try list.last() otherwise "?"
	print("{first} {last}\n")
	let atOne = try list.get(1) otherwise "?"
	print("{atOne}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 2
aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa cccccccccccccccccccccccccccccc
cccccccccccccccccccccccccccccc
```

<!-- test: a-trivial-middle-insert-is-the-control -->

The identical pair of shapes over an `int` element, in one program. The stamp half is 0 for a trivial
element by construction, so this case can only ever see the OWNER half — and it did see it, which is what
says the second case above is about chain membership rather than about `String`. It is also the shape all
four of `/specs/list.md`'s `insert` cases take.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	list.append(1)
	list.append(3)
	try list.insert(1, value: 2) otherwise 'oob'
		return 2
	end 'oob'
	let removed = try list.remove(1) otherwise 0
	print("{removed} {list.count()}\n")
	print("{try list.get(0) otherwise 0} {try list.get(1) otherwise 0}\n")
	list.clear()
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 2
1 3
0
```
