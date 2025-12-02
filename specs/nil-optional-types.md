---
feature: nil-optional-types
status: stable
keywords: [nil, optional, if let, else unwrap]
category: types
---

# Nil and Optional Types

## Developer Notes

Optional types allow functions to return either a value or `nil`, representing the absence of a value. Implementation details:

- **Type Syntax**: `T or nil` (e.g., `int or nil`, `string or nil`)
- **Memory Layout**: Discriminated union `[tag: i8][padding][value: T]`
  - Tag = 0: nil (value space unused)
  - Tag = 1: has value
  - Stack-allocated, no heap allocation or GC
- **Nil Literal**: `nil` keyword (lexer: `KeywordCategory::Literal`)
- **Type Safety**: Cannot use optional values without unwrapping
- **Implicit Wrapping**: Non-nil values automatically wrapped when returning to optional type

### AST Nodes

- `NilExprAST`: Represents the `nil` literal
- `IfLetStmtAST`: Safe unwrapping with pattern matching
- `ElseUnwrapStmtAST`: Unwrapping with mandatory default value

### MIR Representation

- `MIRTypeKind::Optional`: Optional type with `wrappedType` pointer
- `createNilOptional()`: Creates optional with tag=0
- `createSomeOptional()`: Creates optional with tag=1 and value

### Semantic Analysis

- `isOptionalType()`: Check if type string contains " or nil"
- `unwrapOptionalType()`: Extract base type (removes " or nil")
- `makeOptionalType()`: Wrap type as optional
- Prevents nested optionals (`int or nil or nil` is rejected)
- Requires unwrapping before arithmetic/comparison operations
- Validates return path analysis for if-let statements

### Code Generation

Files:
- `codegen_mir_optional.cpp`: Optional type generation
- `codegen_mir_stmt.cpp`: Statement dispatchers
- `semantic_analyzer_stmt.cpp`: Type checking

## Documentation

Optional types represent values that may or may not be present. Use `T or nil` to declare an optional type, where `T` is any type.

### Syntax

```maxon
function mayFail() int or nil
    return nil
end 'mayFail'

function maySucceed() int or nil
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
else 'check'
    // result was nil
    return 0
end 'check'
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
function returnsNil() int or nil
	return nil
end 'returnsNil'

function main() int
	var x = returnsNil()
	if let val = x 'check'
		return val
	else 'check'
		return 99
	end 'check'
end 'main'
```
```exitcode
99
```

<!-- test: optional-with-value -->
```maxon
function returnsValue() int or nil
	return 42
end 'returnsValue'

function main() int
	var x = returnsValue()
	if let val = x 'check'
		return val
	else 'check'
		return 0
	end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: iflet-then-branch -->
```maxon
function safeDivide(a int, b int) int or nil
	if b == 0 'check'
		return nil
	end 'check'
	return a / b
end 'safeDivide'

function main() int
	var opt = safeDivide(10, 2)
	if let result = opt 'unwrap'
		return result
	else 'unwrap'
		return 999
	end 'unwrap'
end 'main'
```
```exitcode
5
```

<!-- test: iflet-else-branch -->
```maxon
function safeDivide(a int, b int) int or nil
	if b == 0 'check'
		return nil
	end 'check'
	return a / b
end 'safeDivide'

function main() int
	var opt = safeDivide(10, 0)
	if let result = opt 'unwrap'
		return result
	else 'unwrap'
		return 88
	end 'unwrap'
end 'main'
```
```exitcode
88
```

<!-- test: else-unwrap-nil -->
```maxon
function returnsNil() int or nil
	return nil
end 'returnsNil'

function main() int
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
function returnsValue() int or nil
	return 5
end 'returnsValue'

function main() int
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
function makeOptional(x int) int or nil
	return x
end 'makeOptional'

function main() int
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
function first() int or nil
	return 10
end 'first'

function second() int or nil
	return nil
end 'second'

function main() int
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
function makeOpt(n int) int or nil
	return n
end 'makeOpt'

function main() int
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
function returnsOptional() int or nil
	return 42
end 'returnsOptional'

function main() int
	var x = returnsOptional()
	return x + 5
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 9
Cannot use optional type 'int or nil' without unwrapping. Use 'if let' to unwrap first
```

<!-- test: error.return-nil-to-non-optional -->
```maxon
function main() int
	return nil
end 'main'
```
```maxoncstderr
Semantic Error: line 2, column 2
Cannot return 'nil' from non-optional return type
  Function return type: int
  Note: To return nil, change the function return type to 'int or nil'
```

<!-- test: error.iflet-non-optional -->
```maxon
function main() int
	var x = 42
	if let val = x 'check'
		return val
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 3, column 2
'if let' requires optional type, got 'int'
  Note: Use 'if let' only with optional types (T or nil)
```

<!-- test: error.else-unwrap-non-optional -->
```maxon
function main() int
	var x = 42
	var result = x else 'default'
		result = 99
	end 'default'
	return result
end 'main'
```
```maxoncstderr
Semantic Error: line 3, column 2
'else' unwrapping requires an optional type, got 'int'
  Note: Use 'var x = expr else ...' only with optional types (T or nil)
```

<!-- test: error.else-unwrap-missing-assignment -->
```maxon
function returnsNil() int or nil
	return nil
end 'returnsNil'

function main() int
	var opt = returnsNil()
	var result = opt else 'default'
		var temp = 7
	end 'default'
	return result
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 2
Variable 'result' must be assigned a value in the else block
  The else block is only executed when the optional is nil
  Note: You must provide a default value by assigning to 'result' in the else block
```

<!-- test: error.nested-optional -->
```maxon
function main() int or nil or nil
	return nil
end 'main'
```
```maxoncstderr
Parse error: Unexpected token: 'or'
```
