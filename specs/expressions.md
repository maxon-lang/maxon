---
feature: expressions
status: stable
keywords: [expressions, evaluation, operators]
category: expressions
---

# Expressions

## Developer Notes

Expressions are combinations of values, operators, and function calls that evaluate to a result.

Implementation:
- Expressions parsed recursively with precedence handling
- Binary expressions: `Parser::parseBinaryExpression()`
- Unary expressions: `Parser::parseUnaryExpression()`
- Primary expressions (literals, identifiers, function calls, parentheses): `Parser::parsePrimaryExpression()`
- Type checking ensures operands are compatible
- Result type determined by operands and operator

Operator precedence (highest to lowest):
1. Unary: `-`, `+`
2. Multiplicative: `*`, `/`, `%`
3. Additive: `+`, `-`
4. Comparison: `=`, `!=`, `<`, `>`, `<=`, `>=`

## Documentation

Expressions combine values, operators, and function calls to compute results.

### Simple Expressions

```maxon
2 + 3
x * y
a > b
```

### Compound Expressions

Combine multiple operations:
```maxon
(2 + 3) * 5
a + b * c - d
x > 0 and y < 10  // When logical operators added
```

### Expressions with Function Calls

```maxon
5 + add(3, 4)
sqrt(x * x + y * y)
```

### Type Compatibility

Operands must have compatible types:
- `int` + `int` → `int`
- `float` + `float` → `float`
- `int` + `float` → `float` (int promoted)
- `int` > `int` → `bool`

## Tests

<!-- test: compound -->
```maxon
function main() int
    return (2 + 3) * 5
end 'main'
```
```
ExitCode: 25
```

<!-- test: with-function-call -->
```maxon
function main() int
    var x = 3
    return 5 + add(x, 4)
end 'main'

function add(a int, b int) int
    return a + b
end 'add'
```
```
ExitCode: 12
```

<!-- test: multiple-variables -->
```maxon
function main() int
    var x = 42
    var y = 10
    var result = x + y
    return result
end 'main'
```
```
ExitCode: 52
```

<!-- test: mixed-operators -->
```maxon
function main() int
    var a = 10
    var b = 3
    return a * 2 + b - 1
end 'main'
```
```
ExitCode: 22
```

<!-- test: comparison-in-expression -->
```maxon
function main() int
    var isGreater = 10 > 5
    if isGreater 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```
ExitCode: 1
```
