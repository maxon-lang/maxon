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
- `/` - Division (integer division truncates)
- `mod` - Modulo (remainder after division, integers only)

### Precedence

Multiplication, division, and modulo have higher precedence than addition and subtraction:
```text
2 + 3 * 4  // Evaluates to 14, not 20
```
### Example

```maxon
function main() int
    var a = 10
    var b = 3
    var sum = a + b      // 13
    var diff = a - b     // 7
    var prod = a * b     // 30
    var quot = a / b     // 3
    var rem = a mod b      // 1
    
    // Use the values
    print(sum)
    print(diff)
    print(prod)
    print(quot)
    print(rem)
    
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
3
1
```


## Tests

<!-- test: addition -->
```maxon
function main() int
    return 5 + 3
end 'main'
```
```exitcode
8
```


<!-- test: multiplication -->
```maxon
function main() int
    return 6 * 7
end 'main'
```
```exitcode
42
```


<!-- test: precedence -->
```maxon
function main() int
    return 2 + 3 * 4
end 'main'
```
```exitcode
14
```


<!-- test: division -->
```maxon
function main() int
    return 20 / 3
end 'main'
```
```exitcode
6
```


<!-- test: modulo -->
```maxon
function main() int
    return 17 mod 5
end 'main'
```
```exitcode
2
```


<!-- test: complex-expression -->
```maxon
function main() int
    var a = 10
    var b = 3
    var result = (a + b) * 2 - a / b
    return result
end 'main'
```
```exitcode
23
```

