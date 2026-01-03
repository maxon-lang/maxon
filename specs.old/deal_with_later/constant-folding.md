---
feature: constant-folding
status: stable
keywords: [optimization, constant-folding, compile-time]
category: optimization
---

# Constant Folding

## Documentation

The compiler evaluates constant expressions at compile time, improving performance.

### How It Works

When all operands in an expression are constants (literals or `let` variables), the compiler calculates the result during compilation.

### Example

```maxon
function main() returns int
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
function main() returns int
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
function main() returns int
    return 10 + 20
end 'main'
```
```exitcode
30
```


<!-- test: with-let -->
```maxon
function main() returns int
    let x = 100
    let y = 25
    return x mod y
end 'main'
```
```exitcode
0
```


<!-- test: complex-expression -->
```maxon
function main() returns int
    var x = 4
    return (1+2+x)*(x+(1+2))
end 'main'
```
```exitcode
49
```


<!-- test: nested-parentheses -->
```maxon
function main() returns int
    return ((10 + 5) * 2) - 5
end 'main'
```
```exitcode
25
```

