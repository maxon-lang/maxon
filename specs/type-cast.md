---
feature: type-cast
status: stable
keywords: [cast, as, type-conversion]
category: operators
---

# Type Cast Operator

## Developer Notes

The `as` keyword performs explicit type conversions between compatible types.

Implementation:
- Parsed in `Parser::parseCastExpression()` as postfix operator
- Represented by `CastExpr` AST node
- Code generation depends on source/target types:
  - `int` to `float`: `sitofp` (signed int to float)
  - `int` to `bool`: `icmp ne i32 %val, 0` (non-zero = true)
  - `int` to `char`: `trunc i32 %val to i8`
  - `char` to `int`: `sext i8 %val to i32`

Type checker validates that the conversion is legal and rejects unsafe conversions.

**Important:** `float` to `int` casting is explicitly disallowed to prevent accidental precision loss. Use explicit functions instead:
- `trunc(x)` - Truncate toward zero (equivalent to old `as int` behavior)
- `round(x)` - Round to nearest integer
- `floor(x)` - Round down to nearest integer
- `ceil(x)` - Round up to nearest integer

## Documentation

The `as` operator converts a value from one type to another.

### Syntax

```maxon
value as targetType
```
### Supported Conversions

- `int` → `float` (int to float only; float to int requires `trunc()`, `round()`, `floor()`, or `ceil()`)
- `int` ↔ `char`
- `int` ↔ `bool`
- `char` ↔ `int`

### Example

```maxon
function main() int
    var pi = 3.14159
    var approximate = trunc(pi)
    print_int(approximate)
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
function main() int
    var x = 5
    var y = x as float
    var z = y + 2.5
    return trunc(z)
end 'main'
```
```exitcode
7
```


<!-- test: char-to-int -->
```maxon
function main() int
    var x = 'A'
    return x as int
end 'main'
```
```exitcode
65
```

