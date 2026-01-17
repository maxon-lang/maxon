---
status: implemented
---

# Type Checking

## Notes
Type checking ensures argument types match parameter types at compile time.

## Docs
The compiler validates that function and method arguments match the expected parameter types. Type mismatches are reported as compile-time errors (E022).

## Tests

<!-- test: method-call-wrong-self-type -->
```maxon
typealias StringArray is Array with String

function main() returns int
	var arr = StringArray{}
	arr.append("hello")
	return 0
end 'main'
```
```maxoncstderr
error E022: specs/fragments/type-checking.method-call-wrong-self-type.1.test:6:6: argument type mismatch for 'other': expected 'Array$String', got 'String'
```

<!-- test: method-call-wrong-element-type -->
```maxon
typealias IntArray is Array with int

function main() returns int
	var arr = IntArray{}
	arr.push("hello")
	return 0
end 'main'
```
```maxoncstderr
error E022: specs/fragments/type-checking.method-call-wrong-element-type.1.test:6:6: argument type mismatch for 'value': expected 'int', got 'String'
```

<!-- test: function-call-string-where-int-expected -->
```maxon
function takeInt(n int) returns int
	return n
end 'takeInt'

function main() returns int
	takeInt("hello")
	return 0
end 'main'
```
```maxoncstderr
error E022: specs/fragments/type-checking.function-call-string-where-int-expected.1.test:7:2: argument type mismatch for 'n': expected 'int', got 'String'
```

<!-- test: function-call-primitive-where-struct-expected -->
```maxon
typealias IntArray is Array with int

function takeArray(arr IntArray) returns int
	return arr.count()
end 'takeArray'

function main() returns int
	takeArray(42)
	return 0
end 'main'
```
```maxoncstderr
error E022: specs/fragments/type-checking.function-call-primitive-where-struct-expected.1.test:9:2: argument type mismatch for 'arr': expected 'Array$int', got 'int'
```

<!-- test: function-call-wrong-struct-type -->
```maxon
type Point
	export var x int
	export var y int
end 'Point'

type Size
	export var w int
	export var h int
end 'Size'

function takePoint(p Point) returns int
	return p.x
end 'takePoint'

function main() returns int
	var s = Size{}
	takePoint(s)
	return 0
end 'main'
```
```maxoncstderr
error E022: specs/fragments/type-checking.function-call-wrong-struct-type.1.test:18:2: argument type mismatch for 'p': expected 'Point', got 'Size'
```

<!-- test: stdlib-function-call-wrong-type -->
```maxon
function main() returns int
	print(42)
	return 0
end 'main'
```
```maxoncstderr
error E022: specs/fragments/type-checking.stdlib-function-call-wrong-type.1.test:3:2: argument type mismatch for 'value': expected 'String', got 'int'
```

<!-- test: implicit-method-call-wrong-type -->
```maxon
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
```maxoncstderr
error E022: specs/fragments/type-checking.implicit-method-call-wrong-type.1.test:8:9: argument type mismatch for 'value': expected 'int', got 'String'
```

<!-- test: array-of-different-element-types -->
```maxon
typealias IntArray is Array with int
typealias StringArray is Array with String

function main() returns int
	var ints = IntArray{}
	var strings = StringArray{}
	ints.append(strings)
	return 0
end 'main'
```
```maxoncstderr
error E022: specs/fragments/type-checking.array-of-different-element-types.1.test:8:7: argument type mismatch for 'other': expected 'Array$int', got 'Array$String'
```

<!-- test: typealias-forward-reference -->
```maxon
typealias FooArray is Array with Foo

type Foo
	let value int
end 'Foo'

function main() returns int
	var arr = FooArray{}
	arr.push({value: 42})
	return arr.count() - 1
end 'main'
```
```exitcode
0
```
