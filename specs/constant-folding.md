---
feature: constant-folding
status: stable
keywords: [optimization, constant-folding, compile-time]
category: optimization
---

# Constant Folding

## Developer Notes

LLVM's optimizer performs constant folding - evaluating expressions with constant operands at compile time.

Implementation:
- Not implemented by Maxon - handled by LLVM optimization passes
- Arithmetic with literals: `1 + 2` → `3`
- Complex expressions: `(1+2+4)*(4+(1+2))` → `49`
- Reduces runtime computation
- Works with int, float, and bool operations
- Enabled at `-O1` or higher optimization levels

The compiler generates the full expression tree, but LLVM's ConstantFolding and InstCombine passes simplify it.

## Documentation

The compiler evaluates constant expressions at compile time, improving performance.

### How It Works

When all operands in an expression are constants (literals or `let` variables), the compiler calculates the result during compilation.

### Example

```maxon
function main() int
    var result = 2 + 3 * 4  // Computed at compile time: 14
    return result
end 'main'
```
```exitcode
14
```


### Complex Expressions

Even complex nested expressions are optimized:
```maxon
function main() int
    var x = 4
    return (1+2+x)*(x+(1+2))  // Optimized to: 7 * 7 = 49
end 'main'
```
```exitcode
49
```


## Tests

<!-- test: simple-arithmetic -->
```maxon
function main() int
    return 10 + 20
end 'main'
```
```exitcode
30
```


<!-- test: with-let -->
```maxon
function main() int
    let x = 100
    let y = 25
    return x % y
end 'main'
```
```exitcode
0
```


<!-- test: complex-expression -->
```maxon
function main() int
    var x = 4
    return (1+2+x)*(x+(1+2))
end 'main'
```
```exitcode
49
```


<!-- test: nested-parentheses -->
```maxon
function main() int
    return ((10 + 5) * 2) - 5
end 'main'
```
```exitcode
25
```

