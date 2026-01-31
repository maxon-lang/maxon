---
feature: expressions
status: stable
keywords: [expressions, evaluation, operators]
category: expressions
---

# Expressions

## Documentation

Expressions combine values, operators, and function calls to compute results.

### Simple Expressions

```maxon
2 + 3
x * y
a > b
```
### Compound Expressions

Combine multiple operations:
```maxon
(2 + 3) * 5
a + b * c - d
x > 0 and y < 10  // When logical operators added
```
### Expressions with Function Calls

```maxon
5 + add(3, b: 4)
sqrt(x * x + y * y)
```
### Type Compatibility

Operands must have compatible types:
- `int` + `int` → `int`
- `float` + `float` → `float`
- `int` + `float` → `float` (int promoted)
- `int` > `int` → `bool`

## Tests

<!-- test: compound -->
```maxon
function main() returns int
  return (2 + 3) * 5
end 'main'
```
```exitcode
25
```


<!-- test: with-function-call -->
```maxon
function main() returns int
  var x = 3
  return 5 + add(x, b: 4)
end 'main'

function add(a int, b int) returns int
  return a + b
end 'add'
```
```exitcode
12
```


<!-- test: multiple-variables -->
```maxon
function main() returns int
  var x = 42
  var y = 10
  var result = x + y
  return result
end 'main'
```
```exitcode
52
```


<!-- test: mixed-operators -->
```maxon
function main() returns int
  var a = 10
  var b = 3
  return a * 2 + b - 1
end 'main'
```
```exitcode
22
```


<!-- test: comparison-in-expression -->
```maxon
function main() returns int
  var isGreater = 10 > 5
  if isGreater 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

