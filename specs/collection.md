---
feature: collection
status: stable
keywords: collection, array, map, transform, functional, higher-order, get, set, count
category: stdlib
---
# Collection

## Documentation

The `Collection` interface provides indexed access and functional operations for ordered collections like arrays.

**Interface:**
```text
interface Collection uses Element extends Iterable
  function count() returns int
  function get(index int) returns Element throws ArrayError
  function set(index int, value Element) returns Self
  function map(transform (Element) Element) returns Self
end 'Collection'
```

Arrays automatically implement the Collection interface.

## count

Returns the number of elements in the collection.

```maxon
function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	print("{arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

## get

Returns the element at the specified index, or throws ArrayError if out of bounds.

```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	var val = try arr.get(1) otherwise 0
	return val
end 'main'
```
```exitcode
20
```

## set

Sets the element at the specified index. Returns self for method chaining.

```maxon
function main() returns ExitCode
	var arr = [1, 2, 3]
	arr.set(1, value: 99)
	var val = try arr.get(1) otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

## map

Transforms each element of a collection by applying a function, returning a new collection with the transformed elements.

**Signature:**
```text
collection.map(transform) collection
```

**Parameters:**
- `transform` - A function that takes an element and returns a transformed value

**Returns:**
A new array containing the transformed elements.

### Using Named Functions

Transform an array using a named function:

```maxon
typealias Score = int(i64.min to i64.max)

function double(x Score) returns Score
	return x * 2
end 'double'

function main() returns ExitCode
	var numbers = [1, 2, 3, 4, 5]
	var doubled = numbers.map(double)
	var val = try doubled.get(2) otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

### Using Closures

Transform using an inline closure with `gives`:

```maxon
typealias Score = int(i64.min to i64.max)

function main() returns ExitCode
	var numbers = [1, 2, 3]
	var squared = numbers.map((x Score) gives x * x)
	var val0 = try squared.get(0) otherwise 0
	var val1 = try squared.get(1) otherwise 0
	var val2 = try squared.get(2) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
4
9
```

## Tests

<!-- test: count-basic -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	print("{arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: count-empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray{}
	print("{arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: get-valid -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	var val0 = try arr.get(0) otherwise 0
	var val2 = try arr.get(2) otherwise 0
	return val0 + val2
end 'main'
```
```exitcode
40
```

<!-- test: get-out-of-bounds -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3]
	var val = try arr.get(10) otherwise 6
	return val
end 'main'
```
```exitcode
6
```

<!-- test: set-basic -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3]
	arr.set(0, value: 100)
	arr.set(2, value: 300)
	var val0 = try arr.get(0) otherwise 0
	var val1 = try arr.get(1) otherwise 0
	var val2 = try arr.get(2) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
100
2
300
```

<!-- test: map-basic-transform -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	var result = arr.map(double)
	var val0 = try result.get(0) otherwise 0
	var val1 = try result.get(1) otherwise 0
	var val2 = try result.get(2) otherwise 0
	var val3 = try result.get(3) otherwise 0
	var val4 = try result.get(4) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	print("{val3}\n")
	print("{val4}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
4
6
8
10
```

<!-- test: map-closure-multiply -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = [2, 3, 4]
	var result = arr.map((x Integer) gives x * 3)
	var val0 = try result.get(0) otherwise 0
	var val1 = try result.get(1) otherwise 0
	var val2 = try result.get(2) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
9
12
```

<!-- test: map-closure-square -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = [1, 2, 3, 4]
	var squared = arr.map((n Integer) gives n * n)
	var val0 = try squared.get(0) otherwise 0
	var val1 = try squared.get(1) otherwise 0
	var val2 = try squared.get(2) otherwise 0
	var val3 = try squared.get(3) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	print("{val3}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
4
9
16
```

<!-- test: map-identity-function -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function identity(x Integer) returns Integer
	return x
end 'identity'

function main() returns ExitCode
	var arr = [10, 20, 30]
	var result = arr.map(identity)
	var val0 = try result.get(0) otherwise 0
	var val1 = try result.get(1) otherwise 0
	var val2 = try result.get(2) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
```

<!-- test: map-negate -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function negate(x Integer) returns Integer
	return 0 - x
end 'negate'

function main() returns ExitCode
	var arr = [1, 2, 3]
	var result = arr.map(negate)
	var val0 = try result.get(0) otherwise 0
	var val1 = try result.get(1) otherwise 0
	var val2 = try result.get(2) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-1
-2
-3
```

<!-- test: map-single-element -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var arr = [42]
	var result = arr.map((x Integer) gives x + 8)
	var val = try result.get(0) otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
50
```
