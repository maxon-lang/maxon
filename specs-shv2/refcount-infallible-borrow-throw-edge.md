---
feature: refcount-infallible-borrow-throw-edge
status: experimental
keywords: [refcount, borrow, iterator, cursor, throw, error-path, memory]
category: memory-safety
---

# Refcount: Infallible Borrow Result Dropped on a Sibling Throw Edge

## Documentation

An iterator's `current()` returns an **infallible interior borrow** of the
collection's element — it owns no reference; the collection still owns the
element. The self-hosted lowering tags every direct-call result `callReturnRc1`
(assumes a transferred `+1`), which is a lie for a borrow-returning callee. That
lie is normally harmless for a value that is *always returned* (transferred out
on its single path), and it is repaired for values consumed into a container or
used purely transiently (reclassified `borrowed`). But it is deadly for the
shape below:

```text
let cur = it.current()                 // infallible borrow, tagged callReturnRc1
try it.advance() otherwise throw E     // SEPARATE fallible op; on failure -> throw
return cur                             // normal path: forward the borrow
```

`cur` is *returned* (so not reclassified transient) and forwarded through a
borrow **intrinsic** (so not reclassified via the wrapper path) — it stays
`callReturnRc1`. On the normal path the enclosing function forwards the borrow
and the caller acquires its own `+1` (`funcReturnsBorrow`), so nothing releases
`cur`. On the **throw** path `cur` dies without being returned; the release side
treated `callReturnRc1` as owning a `+1` and decref'd it there — **stripping the
collection's own reference** to the element. The collection then holds a freed
element, and its teardown element-walk double-frees it
(`__mm_decref: over-release — refcount was already 0`).

The fix makes the release side recognize a borrow-returning call result owns no
transferable `+1` (the release-side dual of the store-side no-`+1` recognition),
so the throw-edge death emits no decref.

This differs from a *fallible* accessor (`arr.get(i)`): its success edge already
increfs the element, so its throw-edge decref is balanced — that shape does not
reproduce the bug. The infallible `current()` paired with a separate fallible
`advance()` is required. The C# oracle is immune (uniform borrow: every element
read hands the reader its own `+1`).

## Tests

<!-- test: infallible-cursor-borrow-thrown-edge -->
An infallible cursor borrow dropped on a sibling `advance()` throw edge must not
release the collection's element, or the array teardown double-frees it.
```maxon
typealias Tag = int(0 to u64.max)

type Item
	export var name as String
	export var tag as Tag

	static function create(n String, t Tag) returns Item
		return Item{name: n, tag: t}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item
typealias ItemIter = ArrayIterator with Item

union AccessError
	truncated
end 'AccessError'

type Reader
	var cursor as ItemIter

	static function create(arr ItemArray) returns Reader throws AccessError
		let c = try arr.cursor() otherwise throw AccessError.truncated
		return Reader{cursor: c}
	end 'create'

	// Mirrors Parser.consume: `cur` is an infallible borrow of the current element;
	// the SEPARATE fallible `advance()` throws on truncation, and on that throw edge
	// `cur` dies without being returned.
	function consume(depth Tag) returns Item throws AccessError
		// Self-recursive on a never-taken path so the inliner leaves this a REAL call
		// (Parser.consume is invoked via try_call, never inlined). Inlining collapses
		// the multi-path borrow-forward shape and hides the bug.
		if depth > 1000000 'rec'
			return try self.consume(depth + 1)
		end 'rec'
		let cur = cursor.current()
		try cursor.advance() otherwise throw AccessError.truncated
		return cur
	end 'consume'
end 'Reader'

function main() returns ExitCode
	var arr = ItemArray.create()
	arr.push(Item.create("alpha", t: 7))
	var reader = try Reader.create(arr) otherwise return 3
	// Cursor sits at element 0 (the last). consume(): cur = element 0 (borrow);
	// advance() fails (no element 1) -> throw; cur dies on the throw path. Pre-fix,
	// element 0 is over-released and this arr teardown double-frees it.
	let r = try reader.consume(0) otherwise Item.create("z", t: 5)
	let n = arr.count() + r.tag
	return n
end 'main'
```
```exitcode
6
```
