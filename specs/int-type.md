---
feature: int-type
status: stable
keywords: [int, integer, i32]
category: types
---

# Int Type

## Documentation

The `int` type stores 32-bit signed integers ranging from -2,147,483,648 to 2,147,483,647.

### Syntax

```maxon
var count = 42
let total = 100
```
### Example

```maxon
function factorial(n int) returns int
    if n <= 1 'base'
        return 1
    end 'base'
    return n * factorial(n - 1)
end 'factorial'

function main() returns int
    return factorial(5)  // Returns 120
end 'main'
```
```exitcode
120
```


## Tests

<!-- test: basic-int -->
```maxon
function main() returns int
    var x = 42
    return x
end 'main'
```
```exitcode
42
```


<!-- test: int-arithmetic -->
```maxon
function main() returns int
    var a = 10
    var b = 20
    var sum = a + b
    return sum
end 'main'
```
```exitcode
30
```


<!-- test: negative-int -->
```maxon
function main() returns int
    var x = -42
    var y = -x
    if y == 42 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```


<!-- test: int-expression -->
```maxon
function main() returns int
    return 2 + 3 * 4
end 'main'
```
```exitcode
14
```

