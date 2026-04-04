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
	let arr = ["hello"]
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
	let arr = IntArray.create()
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
	let arr2 = StringArray.create()
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
