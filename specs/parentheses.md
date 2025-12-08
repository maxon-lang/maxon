---
feature: parentheses
status: stable
keywords: [parentheses, brackets, grouping, precedence]
category: expressions
---

# Parentheses in Expressions

## Developer Notes

Parentheses `()` are used to group expressions and override operator precedence.

Implementation:
- Parsed in `Parser::parsePrimaryExpression()`
- When `(` is encountered, recursively parse inner expression
- Expects matching `)` at the end
- No special AST node - just affects parse tree structure
- Allows explicit control of evaluation order

Example: `(2 + 3) * 4` evaluates to 20, not 14.

## Documentation

Parentheses group expressions and control evaluation order.

### Syntax

```maxon
(expression)
```
### Example

```maxon
function main() int
    var a = 2 + 3 * 4      // 14 (multiply first)
    var b = (2 + 3) * 4    // 20 (add first)
    printInt(a)
    return b
end 'main'
```
```exitcode
20
```
```stdout
14
```


## Tests

<!-- test: override-precedence -->
```maxon
function main() int
    return (2 + 3) * 4
end 'main'
```
```exitcode
20
```


<!-- test: nested-parentheses -->
```maxon
function main() int
    return ((5 + 3) * 2) - 6
end 'main'
```
```exitcode
10
```


<!-- test: complex-expression -->
```maxon
function main() int
    var result = trunc((10 + (2 * 3)) / (4 - 2))
    return result
end 'main'
```
```exitcode
8
```

