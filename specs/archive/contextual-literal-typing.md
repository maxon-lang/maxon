---
feature: contextual-literal-typing
status: stable
keywords: [literals, types, contextual, byte, int, type-inference]
category: type-system
---

# Contextual Literal Typing

## Documentation

Maxon uses contextual literal typing to allow integer and byte literals to adapt to their expected type context in comparisons and function calls.

### Byte and Int Literals

Integer literals in the range 0-255 can be compared directly with byte values:

```maxon
function main() returns int
    var b = 100 as byte
    if b == 50 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

Byte variables can be compared directly with int literals in the 0-255 range:

```maxon
function main() returns int
    var b = 200 as byte
    if b == 200 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

### No Int/Float Mixing

Comparisons between int and float types require explicit casts:

```text
var x = 5
var y = 5.0
if x == y 'check'    // Error: type mismatch
    return 1
end 'check'
```

To compare, cast explicitly:

```maxon
function main() returns int
    var x = 5
    var y = 5.0
    if (x as float) == y 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

### Math Intrinsics

Math intrinsics like `sin`, `cos`, `sqrt`, etc. accept both int and float arguments (int is promoted to float):

```maxon
function main() returns int
    var x = sqrt(16.0)
    return trunc(x)
end 'main'
```
```exitcode
4
```

## Tests

<!-- test: int-literal-vs-byte-valid -->
```maxon
function main() returns int
    var b = 42 as byte
    if b == 42 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-literal-vs-byte-out-of-range -->
```maxon
function main() returns int
    var b = 100 as byte
    if b == 300 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/contextual-literal-typing/int-literal-vs-byte-out-of-range.test:4:10: type mismatch: 'cannot compare byte with int'
```

<!-- test: int-vs-float-error -->
```maxon
function main() returns int
    var x = 5
    var y = 5.0
    if x == y 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/contextual-literal-typing/int-vs-float-error.test:5:10: type mismatch: 'cannot compare int with float'
```

<!-- test: float-vs-int-error -->
```maxon
function main() returns int
    var x = 5.0
    var y = 5
    if x == y 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/contextual-literal-typing/float-vs-int-error.test:5:10: type mismatch: 'cannot compare float with int'
```

<!-- test: int-literal-vs-float-error -->
```maxon
function main() returns int
    var x = 5.0
    if x == 5 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/contextual-literal-typing/int-literal-vs-float-error.test:4:10: type mismatch: 'cannot compare float with int'
```

<!-- test: float-literal-vs-int-error -->
```maxon
function main() returns int
    var x = 5
    if x == 5.0 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/contextual-literal-typing/float-literal-vs-int-error.test:4:10: type mismatch: 'cannot compare int with float'
```

<!-- test: explicit-cast-int-to-float -->
```maxon
function main() returns int
    var x = 5
    var y = 5.0
    if (x as float) == y 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: explicit-cast-float-to-int -->
```maxon
function main() returns int
    var x = 5
    var y = 5.0
    if x == trunc(y) 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: math-intrinsic-with-int -->
```maxon
function main() returns int
    var x = 16
    var result = sqrt(x)
    return trunc(result)
end 'main'
```
```exitcode
4
```

<!-- test: math-intrinsic-with-float-literal -->
```maxon
function main() returns int
    var x = sqrt(16.0)
    return trunc(x)
end 'main'
```
```exitcode
4
```

<!-- test: byte-vs-byte -->
```maxon
function main() returns int
    var a = 50 as byte
    var b = 50 as byte
    if a == b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-vs-int -->
```maxon
function main() returns int
    var a = 1000
    var b = 1000
    if a == b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: float-vs-float -->
```maxon
function main() returns int
    var a = 3.14
    var b = 3.14
    if a == b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

