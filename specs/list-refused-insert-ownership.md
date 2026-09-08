---
feature: list-refused-insert-ownership
status: experimental
keywords: [list, insert, throw, ownership, move, drop, leak, out of bounds]
category: memory
---

# A Refused `List.insert` Still Owns the Element It Was Given

## Documentation

`List.insert(at, value:)` is a **throwing** mutator whose value argument is **moved in at the parse**.
`Parser.parseArraySet` — the shape parser `Vector.set`, the `__ManagedMemory` buffer's `set` and this
member all share — calls `moveElementIntoContainer` *before* the call is emitted, so from that instant the
callee owns the element and the caller's scope-exit drop is suppressed.

On the success path the chain becomes the owner and `element_drop@24` releases it. **On the out-of-range
path no node is ever linked, so the callee must destroy the element itself or nothing ever will.**
`ListRuntime.__list_insert`'s reject block calls `ManagedMemoryRuntime.emitDestroyRejectedElement` for
exactly that reason — the same helper, and the same contract, that `__managed_set` and `__managed_mem_set`
call from theirs.

⚠ **`/specs/list.md` CANNOT CATCH THIS, AND THAT IS WHY THESE CASES ARE HERE.** All four of that file's
`insert` cases (`insert.at-beginning`, `insert.at-middle`, `insert.at-end`, `insert.out-of-bounds`) use
`int` elements, where `moveElementIntoContainer` takes its trivial arm and the callee owns nothing at all.
The refused-insert leak was shipped green over 32 of 32 canonical cases and found in review; the managed
case below is the one that reddens, and the `int` case beside it is the **control** that says the
difference is the managed move-in and not the throw.

⚠ **AND THE OTHER DIRECTION IS A DOUBLE FREE.** A source binding the caller keeps live is *co-owned* into
the container (`moveManagedValueInto`'s owned-and-live arm): the callee holds one reference and the
binding holds its own. A reject that dropped more than the callee's one reference would leave the
binding naming freed memory. The third case reads the binding after the refusal, and then inserts it
successfully, so both halves of that contract are pinned by one program.

## Tests

<!-- test: a-refused-insert-does-not-leak-the-element-it-was-given -->

The reproduction, as measured. Before the reject-destroy existed this printed `rejected` and exited
**101** — a leak, not a wrong answer, which is why the exit code is pinned and not just the output.

```maxon
typealias StringList = List with String

function main() returns ExitCode
	var list = StringList.create()
	list.append("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
	try list.insert(5, value: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb") otherwise 'oob'
		print("rejected\n")
	end 'oob'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
rejected
```

<!-- test: a-trivial-element-refused-insert-is-the-control -->

The identical program over an `int` element. It passed throughout the defect's life — the element is an
inline word the callee never owns — so it is what identifies the leak above as the **managed move-in**
rather than the throw. It is also the shape all four of `/specs/list.md`'s `insert` cases take, which is
the whole reason that file could not see the defect.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	list.append(10)
	try list.insert(5, value: 20) otherwise 'oob'
		print("rejected\n")
	end 'oob'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
rejected
```

<!-- test: a-refused-insert-leaves-its-source-binding-readable -->

The other direction. `held` is an owned binding the caller keeps live, so the insert **co-owns** it: the
callee takes one reference and `held` keeps its own. The refusal must drop exactly the callee's one — a
drop of the binding's reference too would make the `print` on the very next line read freed memory, and
would then double-free at scope exit. The second insert SUCCEEDS with the same binding, so the accepted
path's transfer is pinned by the same program.

```maxon
typealias StringList = List with String

function main() returns ExitCode
	var list = StringList.create()
	list.append("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
	let held = "cccccccccccccccccccccccccccccc"
	try list.insert(9, value: held) otherwise 'oob'
		print("rejected {held}\n")
	end 'oob'
	try list.insert(1, value: held) otherwise 'oob2'
		return 2
	end 'oob2'
	let back = try list.get(1) otherwise "?"
	print("stored {back} held {held} count {list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
rejected cccccccccccccccccccccccccccccc
stored cccccccccccccccccccccccccccccc held cccccccccccccccccccccccccccccc count 2
```
