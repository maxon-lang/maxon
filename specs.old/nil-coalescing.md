---
feature: nil-coalescing
status: stable
keywords: [nil, optional, or, coalesce, guard, unwrap]
category: types
---

# Nil Coalescing and Guard-Let

## Developer Notes

This feature extends optional type handling with two new constructs:

1. **Nil Coalescing Operator**: `optionalExpr or defaultExpr` - returns unwrapped value if present, otherwise the default
2. **Guard-Let Statement**: `let x = optionalExpr or 'label' ... end 'label'` - unwraps optional or executes early exit block

### Nil Coalescing

- **Syntax**: `optionalExpr or defaultExpr`
- **Precedence**: Lowest expression precedence (below logical `or`)
- **No Chaining**: Right operand cannot be optional (prevents ambiguity)
- **Type Rules**:
  - Left operand must be `T or nil`
  - Right operand must be `T` (not optional)
  - Result type is `T` (always non-optional)

### Guard-Let

- **Syntax**: `let x = optionalExpr or 'label' statements end 'label'`
- **Scope Exit**: Guard body MUST exit scope (return, break, continue)
- **Variable Binding**: After guard block, `x` is bound to unwrapped value
- **Use Case**: Early return/exit for nil cases without deep nesting

### AST Nodes

- `OrCoalesceExprAST`: Nil coalescing expression
  - `optionalExpr`: Left operand (must be optional type)
  - `defaultExpr`: Right operand (default value)
- `GuardLetStmtAST`: Guard-let statement
  - `name`: Variable name for unwrapped value
  - `optionalExpr`: Expression to unwrap
  - `guardBody`: Statements for nil case (must exit scope)
  - `blockId`: Block identifier

### Parser Notes

The `or` keyword is context-sensitive:
- In types: `int or nil` (optional type syntax)
- In expressions: logical OR for booleans, nil coalescing for optionals
- In guard-let: followed by BLOCK_ID token

Parser distinguishes these cases:
- `parseLogicalOr`: Checks if `or` is followed by BLOCK_ID, stops if so
- `parseNilCoalesce`: Lowest precedence, handles `or defaultExpr`
- `parseGuardLet`: Handles `let x = expr or 'label'` pattern

### Semantic Analysis

For nil coalescing (`BinaryExprAST` with op='O' and optional left):
- Verify left operand is optional type
- Verify right operand is non-optional
- Verify types match after unwrapping
- Return unwrapped type

For guard-let:
- Verify expression is optional type
- Analyze guard body in separate scope
- Verify guard body exits scope (has return/break/continue)
- After guard block, variable is bound to unwrapped type

### Code Generation

Both constructs generate similar code:
1. Evaluate optional expression
2. Store to stack alloca
3. Load tag (field 0)
4. Branch: tag==1 → has-value block, tag==0 → default/guard block
5. Has-value: extract value (field 1), store to result
6. Default/guard: evaluate default expression OR execute guard body

For nil coalescing of struct types (string, etc.), must load value from pointer before storing.

## Documentation

### Nil Coalescing Operator

The nil coalescing operator `or` provides a default value when an optional is nil:

```maxon
var x = optionalValue or defaultValue
```

This is equivalent to:
```maxon
var x int = 0
if let unwrapped = optionalValue 'check'
    x = unwrapped
end 'check' else 'default'
    x = defaultValue
end 'default'
```

The result type is always the unwrapped type (non-optional).

**Important**: The nil coalescing `or` cannot be chained:
```maxon
var x = a or b      // OK: b must be non-optional
var y = a or b or c // ERROR: b or c would create optional result
```

### Guard-Let Statement

Guard-let provides early exit when an optional is nil:

```maxon
function process(value int or nil) returns int
    let x = value or 'nil_case'
        return 0  // Early exit if nil
    end 'nil_case'
    
    // x is now guaranteed to be unwrapped int
    return x * 2
end 'process'
```

The guard body MUST exit the current scope using `return`, `break`, or `continue`. This ensures that after the guard block, the variable is guaranteed to hold a valid value.

### Nil Default Parameters

Optional parameters can use `nil` as the default value:

```maxon
function greet(name String or nil = nil)
    let actualName = name or 'default'
        print("Hello, stranger!")
        return
    end 'default'
    print("Hello, " + actualName + "!")
end 'greet'

greet()              // Uses nil default → "Hello, stranger!"
greet(name = "Alice") // Uses provided value → "Hello, Alice!"
```

## Tests

<!-- test: nil-coalesce-has-value -->
```maxon
function getOptional(hasValue bool) returns int or nil
    if hasValue 'ret'
        return 42
    end 'ret'
    return nil
end 'getOptional'

function main() returns int
    var opt = getOptional(true)
    var result = opt or 0
    return result
end 'main'
```
```exitcode
42
```

<!-- test: nil-coalesce-nil-value -->
```maxon
function getOptional(hasValue bool) returns int or nil
    if hasValue 'ret'
        return 42
    end 'ret'
    return nil
end 'getOptional'

function main() returns int
    var opt = getOptional(false)
    var result = opt or 100
    return result
end 'main'
```
```exitcode
100
```

<!-- test: guard-let-has-value -->
```maxon
function getOptional(hasValue bool) returns int or nil
    if hasValue 'ret'
        return 42
    end 'ret'
    return nil
end 'getOptional'

function main() returns int
    var opt = getOptional(true)
    let value = opt or 'nil_case'
        return 99
    end 'nil_case'
    return value
end 'main'
```
```exitcode
42
```

<!-- test: guard-let-nil-value -->
```maxon
function getOptional(hasValue bool) returns int or nil
    if hasValue 'ret'
        return 42
    end 'ret'
    return nil
end 'getOptional'

function main() returns int
    var opt = getOptional(false)
    let value = opt or 'nil_case'
        return 88
    end 'nil_case'
    return value
end 'main'
```
```exitcode
88
```

<!-- test: nil-default-param -->
```maxon
function test(x int or nil = nil) returns int
    return x or 0
end 'test'

function main() returns int
    return test()
end 'main'
```
```exitcode
0
```

<!-- test: nil-default-param-with-value -->
```maxon
function test(x int or nil = nil) returns int
    return x or 0
end 'test'

function main() returns int
    return test(x = 42)
end 'main'
```
```exitcode
42
```

<!-- test.error: coalesce-left-not-optional -->
```maxon
function main() returns int
    var x = 5
    var y = x or 10
    return y
end 'main'
```
```stderr
Logical operator 'or' requires boolean or integer operands
```

<!-- test.error: guard-let-no-exit -->
```maxon
function getOptional() returns int or nil
    return 42
end 'getOptional'

function main() returns int
    var opt = getOptional()
    let value = opt or 'nil_case'
        var x = 1
    end 'nil_case'
    return value
end 'main'
```
```stderr
Guard body must exit scope
```

<!-- test.error: coalesce-chained -->
```maxon
function getOptional() returns int or nil
    return 42
end 'getOptional'

function main() returns int
    var a = getOptional()
    var b = getOptional()
    var x = a or b
    return x or 0
end 'main'
```
```stderr
Cannot chain nil coalescing operators
```
