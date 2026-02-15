---
feature: comparison-operators
status: stable
keywords: [operators, comparison, equals, not-equals, greater, less]
category: operators
---

# Comparison Operators

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
function main() returns ExitCode
  var x = 10
  var y = 20
  
  if x < y 'check'
    return 1
  end 'check'
  
  return 0
end 'main'
```
```exitcode
1
```


## Tests

<!-- test: equality -->
```maxon
function main() returns ExitCode
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
function main() returns ExitCode
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
function main() returns ExitCode
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
function main() returns ExitCode
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
function main() returns ExitCode
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

