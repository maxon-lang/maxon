---
feature: pointers
status: stable
keywords: [ptr, pointer, address-of, dereference]
category: types
---

# Pointers

## Developer Notes

The `ptr` type represents memory addresses. Pointers are untyped (void*-like) in Maxon.

Implementation:
- Represented as LLVM `ptr` (opaque pointer type)
- Address-of operator `&` gets the address of a variable
- Dereference operator `*` not yet implemented
- Can be cast to/from int
- Used extensively with extern functions (Windows API)
- Pointer arithmetic not yet implemented
- Null pointer is `0 as ptr`

Common patterns:
- Getting address of local variable: `&variable`
- Passing to extern functions: `WriteFile(stdout, &written, ...)`
- String literals are `ptr` type

## Documentation

The `ptr` type stores memory addresses (pointers).

### Syntax

```maxon
var address ptr = &variable
var nullPtr = 0 as ptr
```

### Address-Of Operator

The `&` operator gets the memory address of a variable:

```maxon
var x = 42
var addr = &x
```

### Example with Extern Functions

```maxon
extern function WriteFile(hFile ptr, buffer ptr, nBytes int, written ptr, overlapped ptr) int

function main() int
    var written = 0
    var msg = "Hello"
    WriteFile(stdout, msg, 5, &written, 0 as ptr)
    return 0
end 'main'
```

## Tests

<!-- test: address-of -->
```maxon
function main() int
    var x = 42
    var addr = &x
    return 0
end 'main'
```
```
ExitCode: 0
```

<!-- test: null-pointer -->
```maxon
function main() int
    var nullPtr = 0 as ptr
    return 42
end 'main'
```
```
ExitCode: 42
```

<!-- test: pointer-chain-cast -->
```maxon
function main() int
    var x = 42
    var p1 = &x
    var p2 = p1
    var p3 = p2
    return 0
end 'main'
```
```
ExitCode: 0
```

<!-- test: with-extern -->
```maxon
extern function GetStdHandle(handle int) ptr

function main() int
    let stdout = GetStdHandle(-11)
    return 0
end 'main'
```
```
ExitCode: 0
```
