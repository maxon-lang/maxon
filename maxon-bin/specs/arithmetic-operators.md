---
feature: arithmetic-operators
status: stable
keywords: [operators, arithmetic, add, subtract, multiply, divide, modulo]
category: operators
---

# Arithmetic Operators

## Developer Notes

Maxon supports five binary arithmetic operators: `+`, `-`, `*`, `/`, `mod` (modulo).

Implementation details:
- Parsed in `Parser::parseBinaryExpression()` with precedence handling
- `*`, `/`, `mod` have higher precedence than `+`, `-`
- Left-associative evaluation
- Represented by `BinaryExpr` AST node with operator token
- Code generation in `CodeGenerator::generateBinaryExpr()`
- Integer operations map to LLVM: `add`, `sub`, `mul`, `sdiv`, `srem`
- Float operations map to LLVM: `fadd`, `fsub`, `fmul`, `fdiv` (no modulo)
- Type checking ensures operands are compatible
- Mixed int/float promotes int to float

Unary `-` and `+` are handled separately (see unary-operators spec).

## Documentation

Maxon supports standard arithmetic operations on numeric types.

### Operators

- `+` - Addition
- `-` - Subtraction
- `*` - Multiplication
- `/` - Division (always returns float; use `trunc(a/b)` for integer division)
- `mod` - Modulo (remainder after division, integers only)

### Precedence

Multiplication, division, and modulo have higher precedence than addition and subtraction:
```text
2 + 3 * 4  // Evaluates to 14, not 20
```
### Example

```maxon
function main() returns int
    var a = 10
    var b = 3
    var sum = a + b          // 13
    var diff = a - b         // 7
    var prod = a * b         // 30
    var div = a / b          // 3.333... (float)
    var quot = trunc(a / b)  // 3 (integer division)
    var rem = a mod b        // 1

    // Use the values
    print("{sum}")
    print("{diff}")
    print("{prod}")
    print("{div}")
    print("{quot}")
    print("{rem}")

    return 0
end 'main'
```
```exitcode
0
```
```stdout
13
7
30
3.333333
3
1
```


## Tests

<!-- test: addition -->
```maxon
function main() returns int
    return 5 + 3
end 'main'
```
```exitcode
8
```


<!-- test: multiplication -->
```maxon
function main() returns int
    return 6 * 7
end 'main'
```
```exitcode
42
```


<!-- test: precedence -->
```maxon
function main() returns int
    return 2 + 3 * 4
end 'main'
```
```exitcode
14
```


<!-- test: division-returns-float -->
```maxon
function main() returns int
    var result = 20 / 3      // 6.666...
    return trunc(result * 10.0)  // 66.666... * 10 = 66.666..., trunc = 66
end 'main'
```
```exitcode
66
```


<!-- test: trunc-division-optimizes -->
```maxon
function main() returns int
    return trunc(20 / 3)     // Optimized to sdiv, returns 6
end 'main'
```
```exitcode
6
```


<!-- test: variable-division-optimizes -->
```maxon
function main() returns int
    var a = 7
    var b = 2
    return trunc(a / b)      // Should optimize to sdiv after Mem2Reg
end 'main'
```
```exitcode
3
```


<!-- test: negative-division -->
```maxon
function main() returns int
    var neg = -7
    let a = trunc(neg / 2)    // -7/2 = -3.5, trunc = -3 (toward zero)
    if a == -3 'pass'
      return 0
    end 'pass'
    return 1
end 'main'
```
```exitcode
0
```


<!-- test: modulo -->
```maxon
function main() returns int
    return 17 mod 5
end 'main'
```
```exitcode
2
```


<!-- test: complex-expression -->
```maxon
function main() returns int
    var a = 10
    var b = 3
    var result = (a + b) * 2 - trunc(a / b)
    return result
end 'main'
```
```exitcode
23
```

