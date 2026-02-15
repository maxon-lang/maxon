---
feature: arithmetic-operators
status: stable
keywords: [operators, arithmetic, add, subtract, multiply, divide, modulo]
category: operators
---

# Arithmetic Operators

## Documentation

Maxon supports standard arithmetic operations on numeric types.

### Operators

- `+` - Addition
- `-` - Subtraction
- `*` - Multiplication
- `/` - Division (int/int produces truncating int; float/float produces float)
- `mod` - Modulo (remainder after division, integers only)

### Precedence

Multiplication, division, and modulo have higher precedence than addition and subtraction:
```text
2 + 3 * 4  // Evaluates to 14, not 20
```
### Example

```maxon
function main() returns ExitCode
  var a = 10
  var b = 3
  var sum = a + b          // 13
  var diff = a - b         // 7
  var prod = a * b         // 30
  var div = a / b          // 3 (truncating integer division)
  var rem = a mod b        // 1

  // Use the values
  print("{sum}\n")
  print("{diff}\n")
  print("{prod}\n")
  print("{div}\n")
  print("{rem}\n")

  return 0
end 'main'
```
```exitcode
0
```
```stdout
13
7
30
3
1
```


## Tests

<!-- test: addition -->
```maxon
function main() returns ExitCode
  return 5 + 3
end 'main'
```
```exitcode
8
```


<!-- test: multiplication -->
```maxon
function main() returns ExitCode
  return 6 * 7
end 'main'
```
```exitcode
42
```


<!-- test: precedence -->
```maxon
function main() returns ExitCode
  return 2 + 3 * 4
end 'main'
```
```exitcode
14
```


<!-- test: division-truncating-int -->
```maxon
function main() returns ExitCode
  return 20 / 3
end 'main'
```
```exitcode
6
```


<!-- test: trunc-division-optimizes -->
```maxon
function main() returns ExitCode
  return 20 / 3             // int/int = truncating int, returns 6
end 'main'
```
```exitcode
6
```


<!-- test: variable-division-optimizes -->
```maxon
function main() returns ExitCode
  var a = 7
  var b = 2
  return a / b              // int/int = truncating int, returns 3
end 'main'
```
```exitcode
3
```


<!-- test: negative-division -->
```maxon
function main() returns ExitCode
  var neg = -7
  let a = neg / 2           // -7/2 = -3 (truncating toward zero)
  if a == -3 'pass'
      return 0
  end 'pass'
  return 1
end 'main'
```
```exitcode
0
```


<!-- test: modulo -->
```maxon
function main() returns ExitCode
  return 17 mod 5
end 'main'
```
```exitcode
2
```


<!-- test: complex-expression -->
```maxon
function main() returns ExitCode
  var a = 10
  var b = 3
  var result = (a + b) * 2 - a / b
  return result
end 'main'
```
```exitcode
23
```

