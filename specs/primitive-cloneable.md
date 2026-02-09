---
feature: primitive-cloneable
status: stable
keywords: clone, cloneable, copy, primitives
category: type-system
---
# Primitive Cloneable

## Documentation

All built-in types (`int`, `float`, `bool`, `byte`) implement the `Cloneable`
interface. Since primitives are value types, `clone()` returns a copy of the value.

## clone()

Returns a copy of the primitive value.

**Signatures:**
- `int.clone() -> int`
- `float.clone() -> float`
- `bool.clone() -> bool`
- `byte.clone() -> byte`

**Example:**
```maxon
var x = 42
var y = x.clone()   // y is 42
```

## Tests

<!-- test: int.clone -->
```maxon
function main() returns int
  var x = 42
  return x.clone()
end 'main'
```
```exitcode
42
```

<!-- test: float.clone -->
```maxon
function main() returns int
  var x = 3.14
  var y = x.clone()
  if y == 3.14 'ok'
    return 1
  end 'ok'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: bool.clone -->
```maxon
function main() returns int
  var x = true
  if x.clone() 'ok'
    return 1
  end 'ok'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: byte.clone -->
```maxon
function main() returns int
  var x = 65 as byte
  var y = x.clone() as int
  return y
end 'main'
```
```exitcode
65
```
