---
feature: borrow-checker
status: experimental
keywords: [borrow, checker, mutation, safety, reference, NLL]
category: memory
---
# Borrow Checker

## Documentation

The borrow checker prevents mutation of a value while references to its internal state are alive. When you obtain a reference from a mutable source (e.g., `var s = arr.get(0)`), that source cannot be mutated until the reference is no longer used.

Borrows use non-lexical lifetimes (NLL): a borrow ends at the last use of the borrowing variable, not at the end of its scope. This means you can safely use a reference and then mutate the source after you're done with the reference.

### Error E3070: Borrow Conflict

```text
error E3070: cannot mutate 'arr' via 'push' while it is borrowed by 's'
```

## Tests

<!-- test: borrow-basic-conflict -->
### Basic borrow conflict
Getting an element then pushing must be rejected.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = ["hello"]
	let s = try arr.get(0) otherwise ""
	arr.push("world")
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-checker/borrow-basic-conflict.test:7:6: cannot mutate 'arr' via 'push' while it is borrowed by 's' (borrowed at line 6)
```

<!-- test: borrow-nll-allowed -->
### NLL: borrow ends at last use
Using the reference before mutating is allowed because the borrow expires.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(42)
	let v = try arr.get(0) otherwise 0
	print("{v}\n")
	// v is no longer used after this point — borrow has expired
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
### Different source is not a conflict
Mutating a different array is fine.
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
### Try-otherwise: source is mutable inside the otherwise handler
The borrow created by `try map.get(name)` only lives on the success path. Inside the `otherwise` block the get failed — `existing` was never bound, so there is no live borrow and mutating the same map must be allowed.
```maxon
typealias ByteArrayMap = Map with(String, ByteArray)

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
