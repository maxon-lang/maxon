---
feature: parentheses
status: stable
keywords: [parentheses, brackets, grouping, precedence]
category: expressions
---

# Parentheses in Expressions

## Documentation

Parentheses group expressions and control evaluation order.

### Syntax

```maxon
(expression)
```
### Example

```maxon
function main() returns Integer
  var a = 2 + 3 * 4      // 14 (multiply first)
  var b = (2 + 3) * 4    // 20 (add first)
  return a+b
end 'main'
```
```exitcode
34
```


## Tests

<!-- test: override-precedence -->
```maxon
function main() returns Integer
  return (2 + 3) * 4
end 'main'
```
```exitcode
20
```


<!-- test: nested-parentheses -->
```maxon
function main() returns Integer
  return ((5 + 3) * 2) - 6
end 'main'
```
```exitcode
10
```


<!-- test: complex-expression -->
```maxon
function main() returns Integer
  var result = trunc((10 + (2 * 3)) / (4 - 2))
  return result
end 'main'
```
```exitcode
8
```

