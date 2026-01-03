---
feature: comparison-operators
status: stable
keywords: [operators, comparison, equals, not-equals, greater, less]
category: operators
---

# Comparison Operators

## Developer Notes

Comparison operators compare two values and return a `bool` result.

Implementation:
- Parsed in `Parser::parseBinaryExpression()`
- Lower precedence than arithmetic operators
- Result type is always `bool` (LLVM `i1`)
- Integer comparisons use LLVM `icmp` with predicates: `eq`, `ne`, `slt`, `sgt`, `sle`, `sge`
- Float comparisons use LLVM `fcmp` with ordered predicates: `oeq`, `one`, `olt`, `ogt`, `ole`, `oge`
- Type checker ensures both operands have same type (uses contextual literal typing for byte↔int)
- See `contextual-literal-typing` spec for type matching rules
- Common in condition expressions for `if` and `while`

Note: Maxon uses `==` for equality and `!=` for inequality.

## Documentation

Comparison operators compare two values and return `true` or `false`.

### Operators

- `==` - Equal to
- `!=` - Not equal to
- `<` - Less than
- `>` - Greater than
- `<=` - Less than or equal to
- `>=` - Greater than or equal to

### Example

```maxon
function main() returns int
    var x = 10
    var y = 20
    
    if x < y 'test1'
        if x != y 'test2'
            return 1
        end 'test2'
    end 'test1'
    
    return 0
end 'main'
```
```exitcode
1
```


## Tests

<!-- test: equality -->
```maxon
function main() returns int
    var x = 42
    if x == 42 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```


<!-- test: not-equal -->
```maxon
function main() returns int
    var x = 10
    if x != 20 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```


<!-- test: greater-than -->
```maxon
function main() returns int
    if 5 > 3 'check'
        return 42
    end 'check'
    return 0
end 'main'
```
```exitcode
42
```


<!-- test: less-than-or-equal -->
```maxon
function main() returns int
    var a = 5
    var b = 10
    if a <= b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```


<!-- test: float-comparison -->
```maxon
function main() returns int
    var x = 3.5
    var y = 2.1
    if x > y 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

