---
feature: borrow-checker
status: experimental
keywords: [borrow, counted, mutation, safety, reference]
category: memory
---
# Counted Borrows

## Documentation

A value read out of a collection (`arr.get(0)`, `list.first()`, the element of a `for … in`, a cursor's
`current()`) is counted: the binding holds its own reference to the element. Mutating, clearing or
rebinding the source afterwards leaves that value readable and unchanged, so a program may mutate a
collection while values read from it are still in use.

## Tests

<!-- test: get-then-push-keeps-the-element -->
### Getting an element, then pushing
The element read by `get` stays readable after the source grows and is cleared.
```maxon
function main() returns ExitCode
	var arr = ["hello {40 + 2} world, long enough for the heap"]
	let s = try arr.get(0) otherwise ""
	arr.push("world")
	arr.clear()
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello 42 world, long enough for the heap
```

<!-- test: borrow-nll-allowed -->
### Reading, then mutating
Using the value before mutating the source.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(42)
	let v = try arr.get(0) otherwise 0
	print("{v}\n")
	arr.push(99)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: borrow-different-source -->
### Mutating a different source
A value read from one array is untouched by a push to another.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	let arr1 = ["hello"]
	var arr2 = StringArray.create()
	let s = try arr1.get(0) otherwise ""
	arr2.push("world")
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: borrow-not-live-in-otherwise -->
### Try-otherwise: the source is mutable inside the otherwise handler
Inside the `otherwise` block the get failed and `existing` was never bound; the handler mutates the same map.
```maxon
typealias ByteArrayMap = Map with (String, ByteArray)

function intern(lookup ByteArrayMap, name String) returns ByteArray
	let existing = try lookup.get(name) otherwise 'fresh'
		let bytes = name.toByteArray()
		lookup.upsert(name, value: bytes)
		return bytes
	end 'fresh'
	return existing
end 'intern'

function main() returns ExitCode
	var m = ByteArrayMap.create()
	let bytes = intern(m, name: "hello")
	print("{bytes.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```
