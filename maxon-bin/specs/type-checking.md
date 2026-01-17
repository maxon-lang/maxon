---
status: implemented
---

# Type Checking

## Notes
Type checking ensures argument types match parameter types at compile time.

## Docs
The compiler validates that function and method arguments match the expected parameter types. Type mismatches are reported as compile-time errors (E022).

## Tests

### Method call - wrong type for Self parameter (the original bug)
```test error
typealias StringArray is Array with String

function main() returns int
	var arr = StringArray.new()
	arr.append("hello")
	return 0
end 'main'
```
MaxoncStderr: `error E022: type mismatch`

### Method call - wrong element type
```test error
typealias IntArray is Array with int

function main() returns int
	var arr = IntArray.new()
	arr.push("hello")
	return 0
end 'main'
```
MaxoncStderr: `error E022: type mismatch`

### Regular function call - primitive where struct expected
```test error
typealias IntArray is Array with int

function takeArray(arr IntArray)
end 'takeArray'

function main() returns int
	takeArray(42)
	return 0
end 'main'
```
MaxoncStderr: `error E022: type mismatch`

### Regular function call - wrong struct type
```test error
type Point
	var x int
	var y int
end 'Point'

type Size
	var w int
	var h int
end 'Size'

function takePoint(p Point)
end 'takePoint'

function main() returns int
	var s = Size.new()
	takePoint(s)
	return 0
end 'main'
```
MaxoncStderr: `error E022: type mismatch`

### Stdlib function call - wrong type
```test error
function main() returns int
	print(42)
	return 0
end 'main'
```
MaxoncStderr: `error E022: type mismatch`

### Implicit method call (calling method from within type)
```test error
typealias IntArray is Array with int

type Container
	var items IntArray

	function addWrong(s String)
		items.push(s)
	end 'addWrong'
end 'Container'

function main() returns int
	return 0
end 'main'
```
MaxoncStderr: `error E022: type mismatch`

### Array of different element types
```test error
typealias IntArray is Array with int
typealias StringArray is Array with String

function main() returns int
	var ints = IntArray.new()
	var strings = StringArray.new()
	ints.append(strings)
	return 0
end 'main'
```
MaxoncStderr: `error E022: type mismatch`
