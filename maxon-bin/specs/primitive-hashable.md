---
feature: primitive-hashable
status: stable
keywords: hash, equals, hashable, equatable, primitives
category: type-system
---

## Developer Notes

Built-in types (`int`, `float`, `bool`, `byte`) implement `Hashable` and `Equatable` interfaces
as compiler intrinsics in `4-ast_to_ir.zig`.

**Implementation Details:**
- Method calls on primitives are handled in `convertMethodCallExpr`
- `emitPrimitiveMethodCall` dispatches to type-specific implementations
- `emitPrimitiveHash` and `emitPrimitiveEquals` generate inline IR

**Hash Implementations:**
- `int.hash()`: Returns value directly (identity hash)
- `float.hash()`: Normalizes -0.0 to +0.0, then bitcasts to i64 (Swift approach)
- `bool.hash()`: Returns 1 for true, 0 for false
- `byte.hash()`: Returns value directly

**Equals Implementations:**
- All types use direct comparison (`icmp_eq` for int/bool/byte, `fcmp_eq` for float)
- Float follows IEEE semantics: `NaN.equals(NaN)` returns false

**Key Design Decisions:**
- Float hash uses Swift's approach to ensure `0.0.hash() == (-0.0).hash()`
- Uses `bitcast_f64_to_i64` IR operation for float hash

## Documentation

# Primitive Hashable and Equatable

All built-in types (`int`, `float`, `bool`, `byte`) implement the `Hashable` and `Equatable`
interfaces, allowing them to be used in hash-based collections like `Set` and `Map`.

## hash()

Returns an integer hash value for the primitive.

**Signatures:**
- `int.hash() -> int`
- `float.hash() -> int`
- `bool.hash() -> int`
- `byte.hash() -> int`

**Example:**
```maxon
var x = 42
var h = x.hash()    // returns 42

var f = 3.14
var fh = f.hash()   // returns bit pattern as int

var t = true
var th = t.hash()   // returns 1
```

**Notes:**
- `0.0.hash()` and `(-0.0).hash()` return the same value
- Integer hash is identity function

## equals(other)

Compares two values for equality.

**Signatures:**
- `int.equals(other int) -> bool`
- `float.equals(other float) -> bool`
- `bool.equals(other bool) -> bool`
- `byte.equals(other byte) -> bool`

**Example:**
```maxon
var a = 42
var b = 42
if a.equals(b) 'check'
    // true
end 'check'
```

**Notes:**
- Float comparison follows IEEE semantics: `NaN.equals(NaN)` returns false

## Tests

<!-- test: int.hash -->
```maxon
function main() returns int
    var i = 42
    var h = i.hash()
    return h
end 'main'
```
```exitcode
42
```

<!-- test: bool.hash.true -->
```maxon
function main() returns int
    var t = true
    return t.hash()
end 'main'
```
```exitcode
1
```

<!-- test: bool.hash.false -->
```maxon
function main() returns int
    var f = false
    return f.hash()
end 'main'
```
```exitcode
0
```

<!-- test: float.hash.nonzero -->
```maxon
function main() returns int
    var f = 3.14
    var h = f.hash()
    if h != 0 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: float.hash.zero-normalization -->
```maxon
function main() returns int
    var pos = 0.0
    var neg = 0.0 - 0.0
    var h1 = pos.hash()
    var h2 = neg.hash()
    if h1 == h2 'eq'
        return 1
    end 'eq'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: int.equals.same -->
```maxon
function main() returns int
    var a = 42
    var b = 42
    if a.equals(b) 'eq'
        return 1
    end 'eq'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: int.equals.different -->
```maxon
function main() returns int
    var a = 42
    var b = 17
    if a.equals(b) 'eq'
        return 1
    end 'eq'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: bool.equals -->
```maxon
function main() returns int
    var a = true
    var b = true
    if a.equals(b) 'eq'
        return 1
    end 'eq'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: float.equals -->
```maxon
function main() returns int
    var a = 3.14
    var b = 3.14
    if a.equals(b) 'eq'
        return 1
    end 'eq'
    return 0
end 'main'
```
```exitcode
1
```
