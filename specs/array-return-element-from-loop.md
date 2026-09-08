---
feature: array-return-element-from-loop
status: stable
keywords: [array, struct, for-loop, return, managed, refcount]
category: memory
---
# Return a For-Loop Element with Managed Fields

## Documentation

When iterating an array of structs that contain managed (refcounted) fields like `String`, returning the loop variable directly from inside the loop body must transfer ownership to the caller without double-decrementing the refcount.

The for-in loop introduces two names that alias the same managed allocation:
- `__forin_result_N` — the temp that holds the result of `iter.current()`
- the user-facing loop variable (e.g. `e` in `for e in arr`)

When the body executes `return e`, the scope cleanup must skip BOTH aliases — not just `e`. Decrementing the hidden `__forin_result_N` while keeping `e` alive makes `e` a dangling pointer, and the caller ends up freeing memory that was already freed (refcount underflow / use-after-free).

## Tests

<!-- test: return-loop-var-with-string-field -->
### Return Loop Variable with String Field
Returning a for-loop iteration variable whose struct has a managed `String` field must not double-decref the allocation.
```maxon
type Entry
	export var key as String

	export static function create(key String) returns Entry
		return Self{key: key}
	end 'create'
end 'Entry'

typealias EntryArray = Array with Entry

function findEntry(entries EntryArray, key String) returns Entry
	for e in entries 'search'
		if e.key == key 'found'
			return e
		end 'found'
	end 'search'
	return Entry.create("")
end 'findEntry'

function main() returns ExitCode
	var arr = EntryArray.create()
	arr.push(Entry.create("apple"))
	arr.push(Entry.create("banana"))
	let found = findEntry(arr, key: "banana")
	print("found={found.key}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
found=banana
```
