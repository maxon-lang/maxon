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
function main() int
    var x = 42
    var addr = &x
    var addrAsInt = addr as int
    if addrAsInt > 0 'check'
        print(x)
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```


## Tests

<!-- test: address-of -->
```maxon
function main() int
    var x = 42
    var addr = &x
    var addrAsInt = addr as int
    if addrAsInt > 0 return 0
    return 1
end 'main'
```
```exitcode
0
```


<!-- test: null-pointer -->
```maxon
function main() int
    var nullPtr = 0 as ptr
    var nullAsInt = nullPtr as int
    if nullAsInt > 0 return 0
    return 42
end 'main'
```
```exitcode
42
```


<!-- test: pointer-chain-cast -->
```maxon
function main() int
    var x = 42
    var p1 = &x
    var p2 = p1
    var p3 = p2
    var result = p3 as int
    if result > 0 return 1
    return 0
end 'main'
```
```exitcode
0
```


<!-- test: with-extern -->
```maxon
extern function GetStdHandle(handle int) ptr

function main() int
    let stdout = GetStdHandle(-11)
    var result = stdout as int
    if result > 0 return 1
    return 0
end 'main'
```
```exitcode
1
```

