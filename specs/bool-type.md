---
feature: bool-type
status: stable
keywords: [bool, boolean, true, false]
category: types
---

# Bool Type

## Developer Notes

The `bool` type represents boolean values (true/false). Implementation details:

- Represented as LLVM `i1` type
- Two literal values: `true` and `false` (keywords)
- Used in condition expressions for `if` and `while`
- Can be function parameters and return values
- Implicitly converted to int when needed (true=1, false=0)
- Comparison operators return bool
- Logical operators (when implemented) will operate on bool

The lexer recognizes `true` and `false` as `TokenType::True` and `TokenType::False`. The parser creates `BoolLiteral` AST nodes.

## Documentation

The `bool` type stores boolean values - either `true` or `false`.

### Syntax

```maxon
var flag bool = true
let condition = false
```
### Example

```maxon
function isPositive(x int) bool
    if x > 0 'check'
        return true
    end 'check'
    return false
end 'isPositive'

function main() int
    if isPositive(5) 'test'
        return 1
    end 'test'
    return 0
end 'main'
```
```exitcode
1
```


## Tests

<!-- test: basic-bool -->
```maxon
function main() int
    var x = true

    if x 'check'
        return 1
    else 'check'
        return 0
    end 'check'
end 'main'
```
```exitcode
1
```


<!-- test: bool-parameter -->
```maxon
function test_bool_param(flag bool) int
    if flag 'check'
        return 1
    else 'check'
        return 0
    end 'check'
end 'test_bool_param'

function main() int
    return test_bool_param(true)
end 'main'
```
```exitcode
1
```


<!-- test: bool-from-comparison -->
```maxon
function main() int
    var result = 5 > 3
    if result 'check'
        return 42
    end 'check'
    return 0
end 'main'
```
```exitcode
42
```

