---
feature: type-cast
status: stable
keywords: [cast, as, type-conversion]
category: operators
---

# Type Cast Operator

## Documentation

The `as` operator converts a value from one type to another.

### Syntax

```maxon
value as targetType
```
### Supported Conversions

- `int` → `float` (int to float only; float to int requires `trunc()`, `round()`, `floor()`, or `ceil()`)
- `int` ↔ `character`
- `int` ↔ `bool`
- `character` ↔ `int`

### Example

```maxon
function main() returns int
    var pi = 3.14159
    var approximate = trunc(pi)
    print("{approximate}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```


## Tests

<!-- test: int-to-float -->
```maxon
function main() returns int
    var x = 5
    var y = x as float
    var z = y + 2.5
    return trunc(z)
end 'main'
```
```exitcode
7
```


