---
feature: unary-operators
status: stable
keywords: [operators, unary, negate, plus, minus]
category: operators
---

# Unary Operators

## Developer Notes

Unary operators operate on a single operand. Currently implemented: unary `-` (negate) and unary `+` (identity).

Implementation:
- Parsed in `Parser::parseUnaryExpression()`
- Represented by `UnaryExpr` AST node
- Higher precedence than binary operators
- Unary `-` negates numeric values (int or float)
- Unary `+` is identity (no-op, returns operand unchanged)
- Code generation in `CodeGenerator::generateUnaryExpr()`
- Integer negation uses LLVM `sub i32 0, %value`
- Float negation uses LLVM `fneg double %value`

Future: Unary `!` (logical NOT) for bool values.

## Documentation

Unary operators operate on a single value.

### Operators

- `-` - Negate (flip sign of number)
- `+` - Identity (no change, rarely used)

### Example

```maxon
function main() returns int
    var x = 42
    var y = -x      // y is -42
    var z = -y      // z is 42
    return z
end 'main'
```
```exitcode
42
```


## Tests

<!-- test: negate-int -->
```maxon
function main() returns int
    var x = -42
    var y = -x
    if y == 42 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```


<!-- test: negate-float -->
```maxon
function main() returns int
    var x = -3.5
    var y = -x
    var result = trunc(y)
    return result
end 'main'
```
```exitcode
3
```


<!-- test: double-negation -->
```maxon
function main() returns int
    var x = 10
    var y = - -x
    return y
end 'main'
```
```exitcode
10
```


<!-- test: unary-plus -->
```maxon
function main() returns int
    var x = +42
    return x
end 'main'
```
```exitcode
42
```

