---
feature: arithmetic
status: stable
keywords: arithmetic, operators, math
category: operators
---

## Developer Notes

Basic arithmetic operators for integers.

## Documentation

# Arithmetic Operators

Maxon supports basic arithmetic operators for integers:

- `+` addition
- `-` subtraction
- `*` multiplication
- `/` division
- `mod` modulo

## Tests

<!-- test: addition -->
```maxon
function main() returns int
    return 10 + 5
end 'main'
```
```exitcode
15
```

<!-- test: subtraction -->
```maxon
function main() returns int
    return 20 - 8
end 'main'
```
```exitcode
12
```

<!-- test: multiplication -->
```maxon
function main() returns int
    return 6 * 7
end 'main'
```
```exitcode
42
```

<!-- test: division -->
```maxon
function main() returns int
    return 100 / 4
end 'main'
```
```exitcode
25
```

<!-- test: modulo -->
```maxon
function main() returns int
    return 17 mod 5
end 'main'
```
```exitcode
2
```

<!-- test: complex-expression -->
```maxon
function main() returns int
    return 10 + 5 * 2
end 'main'
```
```exitcode
20
```
