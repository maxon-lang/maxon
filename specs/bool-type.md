---
feature: bool-type
status: stable
keywords: [bool, boolean, true, false]
category: types
---

# Bool Type

## Documentation

The `bool` type stores boolean values - either `true` or `false`.

### Syntax

```maxon
var flag bool = true
let condition = false
```
### Example

```maxon
typealias Score = int(i64.min to i64.max)

function isPositive(x Score) returns bool
  if x > 0 'check'
    return true
  end 'check'
  return false
end 'isPositive'

function main() returns ExitCode
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
function main() returns ExitCode
  var x = true

  if x 'check'
    return 1
  end 'check' else 'else_check'
    return 0
  end 'else_check'
end 'main'
```
```exitcode
1
```


<!-- test: bool-parameter -->
```maxon

typealias Integer = int(i64.min to i64.max)

function test_bool_param(flag bool) returns Integer
  if flag 'check'
    return 1
  end 'check' else 'else_check'
    return 0
  end 'else_check'
end 'test_bool_param'

function main() returns ExitCode
  return test_bool_param(true)
end 'main'
```
```exitcode
1
```


<!-- test: bool-from-comparison -->
```maxon
function main() returns ExitCode
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

