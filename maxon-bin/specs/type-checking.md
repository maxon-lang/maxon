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
function main() returns int
	var arr = Array of String.new()
	arr.append("hello")
	return 0
end 'main'
```
MaxoncStderr: `error E022: type mismatch`

### Method call - wrong element type
```test error
function main() returns int
	var arr = Array of int.new()
	arr.push("hello")
	return 0
end 'main'
```
MaxoncStderr: `error E022: type mismatch`

### Regular function call - primitive where struct expected
```test error
function takeArray(arr Array of int)
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
type Container
	var items Array of int

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
function main() returns int
	var ints = Array of int.new()
	var strings = Array of String.new()
	ints.append(strings)
	return 0
end 'main'
```
MaxoncStderr: `error E022: type mismatch`
