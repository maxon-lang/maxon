---
feature: nil-optional-types
status: stable
keywords: [nil, optional, if let, else unwrap]
category: types
---

# Nil and Optional Types

## Documentation

Optional types represent values that may or may not be present. Use `T or nil` to declare an optional type, where `T` is any type.

### Syntax

```maxon
function mayFail() returns int or nil
    return nil
end 'mayFail'

function maySucceed() returns int or nil
    return 42
end 'maySucceed'
```

### If-Let Unwrapping

Safely unwrap optional values with `if let`:

```maxon
var result = mayFail()
if let value = result 'check'
    // value is unwrapped int here
    return value
end 'check' else 'nil_case'
    // result was nil
    return 0
end 'nil_case'
```

### Else-Unwrap with Default

Provide a default value when the optional is nil:

```maxon
var result = mayFail() else 'default'
    result = 10  // Must assign default value
end 'default'
// result is guaranteed to be int (non-optional) here
```

### Type Safety

The compiler prevents using optional values without unwrapping:

```maxon
var x = mayFail()
return x + 5  // ERROR: Cannot use optional without unwrapping
```

## Tests

<!-- test: nil-literal -->
```maxon
function returnsNil() returns int or nil
	return nil
end 'returnsNil'

function main() returns int
	var x = returnsNil()
	if let val = x 'check'
		return val
	end 'check' else 'nil_case'
		return 99
	end 'nil_case'
end 'main'
```
```exitcode
99
```

<!-- test: optional-with-value -->
```maxon
function returnsValue() returns int or nil
	return 42
end 'returnsValue'

function main() returns int
	var x = returnsValue()
	if let val = x 'check'
		return val
	end 'check' else 'nil_case'
		return 0
	end 'nil_case'
end 'main'
```
```exitcode
42
```

<!-- test: iflet-then-branch -->
```maxon
function safeDivide(a int, b int) returns int or nil
	if b == 0 'check'
		return nil
	end 'check'
	return trunc(a / b)
end 'safeDivide'

function main() returns int
	var opt = safeDivide(10, 2)
	if let result = opt 'unwrap'
		return result
	end 'unwrap' else 'nil_case'
		return 999
	end 'nil_case'
end 'main'
```
```exitcode
5
```

<!-- test: iflet-else-branch -->
```maxon
function safeDivide(a int, b int) returns int or nil
	if b == 0 'check'
		return nil
	end 'check'
	return trunc(a / b)
end 'safeDivide'

function main() returns int
	var opt = safeDivide(10, 0)
	if let result = opt 'unwrap'
		return result
	end 'unwrap' else 'nil_case'
		return 88
	end 'nil_case'
end 'main'
```
```exitcode
88
```

<!-- test: else-unwrap-nil -->
```maxon
function returnsNil() returns int or nil
	return nil
end 'returnsNil'

function main() returns int
	var opt = returnsNil()
	var result = opt else 'default'
		result = 7
	end 'default'
	return result
end 'main'
```
```exitcode
7
```

<!-- test: else-unwrap-value -->
```maxon
function returnsValue() returns int or nil
	return 5
end 'returnsValue'

function main() returns int
	var opt = returnsValue()
	var result = opt else 'default'
		result = 99
	end 'default'
	return result
end 'main'
```
```exitcode
5
```

<!-- test: implicit-wrapping -->
```maxon
function makeOptional(x int) returns int or nil
	return x
end 'makeOptional'

function main() returns int
	var opt = makeOptional(33)
	if let val = opt 'check'
		return val
	end 'check'
	return 0
end 'main'
```
```exitcode
33
```

<!-- test: multiple-optionals -->
```maxon
function first() returns int or nil
	return 10
end 'first'

function second() returns int or nil
	return nil
end 'second'

function main() returns int
	var a = first()
	var b = second()

	var sum = 0
	if let val = a 'checkA'
		sum = val
	end 'checkA'

	if let val = b 'checkB'
		sum = sum + val
	end 'checkB'

	return sum
end 'main'
```
```exitcode
10
```

<!-- test: nested-iflet -->
```maxon
function makeOpt(n int) returns int or nil
	return n
end 'makeOpt'

function main() returns int
	var a = makeOpt(5)
	if let x = a 'checkOuter'
		var b = makeOpt(3)
		if let y = b 'checkInner'
			return x + y
		end 'checkInner'
	end 'checkOuter'
	return 0
end 'main'
```
```exitcode
8
```

<!-- test: error.use-optional-without-unwrap -->
```maxon
function returnsOptional() returns int or nil
	return 42
end 'returnsOptional'

function main() returns int
	var x = returnsOptional()
	return x + 5
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:8:9
Cannot use optional type 'int or nil' without unwrapping
  Note: Use 'if let' to safely unwrap optional values before using them

  8 | 	return x + 5
    |         ^
```

<!-- test: error.return-nil-to-non-optional -->
```maxon
function main() returns int
	return nil
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:3:2
Cannot return 'nil' from non-optional return type
  Function return type: int
  Note: To return nil, change the function return type to 'int or nil'

  3 | 	return nil
    |  ^
```

<!-- test: error.iflet-non-optional -->
```maxon
function main() returns int
	var x = 42
	if let val = x 'check'
		return val
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:4:2
'if let' requires optional type, got 'int'
  Note: Use 'if let' only with optional types (T or nil)

  4 | 	if let val = x 'check'
    |  ^
```

<!-- test: error.else-unwrap-non-optional -->
```maxon
function main() returns int
	var x = 42
	var result = x else 'default'
		result = 99
	end 'default'
	return result
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:4:2
'else' unwrapping requires an optional type, got 'int'
  Note: Use 'var x = expr else ...' only with optional types (T or nil)

  4 | 	var result = x else 'default'
    |  ^

Semantic Error: temp_fragment.maxon:7:9
Undefined variable: 'result'
  Note: Variable must be declared with 'var' or 'let' before use

  7 | 	return result
    |         ^
```

<!-- test: error.else-unwrap-missing-assignment -->
```maxon
function returnsNil() returns int or nil
	return nil
end 'returnsNil'

function main() returns int
	var opt = returnsNil()
	var result = opt else 'default'
		var temp = 7
	end 'default'
	return result
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:8:2
Variable 'result' must be assigned a value in the else block
  The else block is only executed when the optional is nil
  Note: You must provide a default value by assigning to 'result' in the else block

  8 | 	var result = opt else 'default'
    |  ^
```

<!-- test: error.nested-optional -->
```maxon
function broken() returns int or nil or nil
	return nil
end 'broken'
```
```maxoncstderr
In file 'temp_fragment.maxon':
Unexpected token: 'or'
  Note: Expected a statement (var, let, if, while, return, break, continue, match, or assignment)
  Location: line 2, column 38
```

<!-- test: optional-param-nil -->
```maxon
function checkValue(x int or nil) returns int
	if let val = x 'check'
		return val
	end 'check' else 'nil_case'
		return 99
	end 'nil_case'
end 'checkValue'

function main() returns int
	return checkValue(nil)
end 'main'
```
```exitcode
99
```

<!-- test: optional-param-value -->
```maxon
function checkValue(x int or nil) returns int
	if let val = x 'check'
		return val
	end 'check' else 'nil_case'
		return 99
	end 'nil_case'
end 'checkValue'

function main() returns int
	return checkValue(42)
end 'main'
```
```exitcode
42
```

<!-- test: optional-param-implicit-wrap -->
```maxon
function add(a int, b int or nil) returns int
	var result = b else 'default'
		result = 0
	end 'default'
	return a + result
end 'add'

function main() returns int
	return add(5, 10)
end 'main'
```
```exitcode
15
```

<!-- test: optional-struct-field-nil -->
```maxon
type Person
	var name String
	var age int or nil
end 'Person'

function main() returns int
	var p = Person{name: "Bob", age: nil}
	if let a = p.age 'check'
		return a
	end 'check' else 'nil_case'
		return 99
	end 'nil_case'
end 'main'
```
```exitcode
99
```

<!-- test: optional-struct-field-value -->
```maxon
type Person
	var name String
	var age int or nil
end 'Person'

function main() returns int
	var p = Person{name: "Alice", age: 30}
	if let a = p.age 'check'
		return a
	end 'check' else 'nil_case'
		return 0
	end 'nil_case'
end 'main'
```
```exitcode
30
```

<!-- test: optional-struct-field-implicit-wrap -->
```maxon
type Point
	var x int
	var y int or nil
end 'Point'

function main() returns int
	var p = Point{x: 10, y: 20}
	var result = p.y else 'default'
		result = 0
	end 'default'
	return p.x + result
end 'main'
```
```exitcode
30
```
