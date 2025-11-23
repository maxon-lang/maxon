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
  - `float` to `int`: `fptosi` (float to signed int, truncates)
  - `int` to `bool`: `icmp ne i32 %val, 0` (non-zero = true)
  - `int` to `char`: `trunc i32 %val to i8`
  - `char` to `int`: `sext i8 %val to i32`
  - `int` to `ptr`: `inttoptr`
  - `ptr` to `int`: `ptrtoint`
  - `ptr` to `ptr`: `bitcast`

Type checker validates that the conversion is legal. Some conversions may lose precision (float to int) or be unsafe (int to ptr).

## Documentation

The `as` operator converts a value from one type to another.

### Syntax

```maxon
value as targetType
```
### Supported Conversions

- `int` ↔ `float` (truncates when converting to int)
- `int` ↔ `char`
- `int` ↔ `bool`
- `int` ↔ `ptr`
- `ptr` ↔ `ptr` (pointer casting)
- `char` ↔ `int`

### Example

```maxon
function main() int
    var pi = 3.14159
    var approximate = pi as int
    print(approximate)
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

<!-- test: float-to-int -->
```maxon
function main() int
    var x = 3.14
    var y = 2.0
    var z = x + y
    var result = z as int
    return result
end 'main'
```
```exitcode
5
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


<!-- test: int-to-float -->
```maxon
function main() int
    var x = 5
    var y = x as float
    var z = y + 2.5
    return z as int
end 'main'
```
```exitcode
7
```


<!-- test: int-to-ptr -->
```maxon
function main() int
    var nullPtr = 0 as ptr
    var backToInt = nullPtr as int
    if backToInt > 0 return 0
    return 42
end 'main'
```
```exitcode
42
```

