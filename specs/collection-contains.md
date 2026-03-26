---
feature: collection-contains
status: stable
keywords: [contains, collection, search, element, predicate, subsequence]
category: stdlib
---

## Documentation

# Contains

Three variants of `contains()` are available depending on the protocol and argument type.

### Single Element (Collection)

Checks if a collection contains a specific element. Requires the element type to be `Equatable`.

```text
var numbers = [1, 2, 3, 4, 5]
numbers.contains(3)    // true
numbers.contains(99)   // false

var s = "hello"
s.contains('l')   // true (Character search)
```

### Subsequence (Collection)

Checks if a collection contains another collection as a contiguous subsequence.

```text
var arr = [1, 2, 3, 4, 5]
arr.contains(sequence: [2, 3, 4])   // true
arr.contains(sequence: [1, 3])      // false (not contiguous)

var s = "hello world"
s.contains("lo wo")                 // true (substring search)
```

### Predicate (Iterable)

Checks if any element satisfies a predicate closure. Works on all iterable types including Map.

```text
var numbers = [1, 2, 3, 4, 5]
numbers.contains(predicate: (n int) gives n > 3)   // true

var dict = ["a": 1, "b": 2]
dict.contains(predicate: (e (String, int)) gives e.0 == "a")   // true
```

## Tests

### Single Element

<!-- test: array-int-found -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30, 40, 50]
	if arr.contains(30) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: array-int-not-found -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	if arr.contains(99) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: array-string-found -->
```maxon
function main() returns ExitCode
	var arr = ["apple", "banana", "cherry"]
	if arr.contains("banana") 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: array-string-not-found -->
```maxon
function main() returns ExitCode
	var arr = ["apple", "banana", "cherry"]
	if arr.contains("grape") 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-char-found -->
```maxon
function main() returns ExitCode
	var s = "hello world"
	if s.contains('o') 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-char-not-found -->
```maxon
function main() returns ExitCode
	var s = "hello world"
	if s.contains('z') 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: array-empty -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray{}
	if arr.contains(1) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: array-first-element -->
```maxon
function main() returns ExitCode
	var arr = [5, 10, 15]
	if arr.contains(5) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: array-last-element -->
```maxon
function main() returns ExitCode
	var arr = [5, 10, 15]
	if arr.contains(15) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: set-contains -->
```maxon
function main() returns ExitCode
	var s = Set from [1, 2, 3, 4, 5]
	if s.contains(3) 'check'
		print("found\n")
	end 'check'
	if s.contains(99) 'check2'
		print("not found\n")
	end 'check2'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
found
```

### Subsequence

<!-- test: array-subsequence-found -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	if arr.contains(sequence: [2, 3, 4]) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: array-subsequence-not-found -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	if arr.contains(sequence: [1, 3]) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: array-subsequence-at-start -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	if arr.contains(sequence: [1, 2]) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: array-subsequence-at-end -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	if arr.contains(sequence: [4, 5]) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: array-subsequence-empty -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = [1, 2, 3]
	if arr.contains(sequence: IntArray{}) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-substring-found -->
```maxon
function main() returns ExitCode
	var s = "hello world"
	if s.contains("lo wo") 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-substring-not-found -->
```maxon
function main() returns ExitCode
	var s = "hello world"
	if s.contains("xyz") 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

### Predicate

<!-- test: array-predicate-found -->
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	if arr.contains(predicate: (n Integer) gives n > 3) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: array-predicate-not-found -->
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	if arr.contains(predicate: (n Integer) gives n > 10) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: map-predicate-key -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias MapEntry = (String, Integer)

function main() returns ExitCode
	var dict = ["a": 1, "b": 2, "c": 3]
	if dict.contains(predicate: (e MapEntry) gives e.0 == "b") 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: map-predicate-value -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias MapEntry = (String, Integer)

function main() returns ExitCode
	var dict = ["a": 1, "b": 2, "c": 3]
	if dict.contains(predicate: (e MapEntry) gives e.1 == 2) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: map-predicate-not-found -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias MapEntry = (String, Integer)

function main() returns ExitCode
	var dict = ["a": 1, "b": 2, "c": 3]
	if dict.contains(predicate: (e MapEntry) gives e.1 > 10) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
