---
feature: stdlib-array-intrinsics
status: experimental
keywords: stdlib, array, intrinsics, __ManagedArray, internal
category: type-system
---
## Documentation

# Stdlib Array Intrinsics

The stdlib Array type uses compiler intrinsics for low-level array operations. These intrinsics are not available to user code.

## __ManagedArray Type

`__ManagedArray` is a compiler-internal type that represents the raw storage for arrays:

```
__ManagedArray (24 bytes):
+----------+----------+----------+
| _buffer  |  _len    | _capacity|
| (ptr)    |  (i64)   | (i64)    |
+----------+----------+----------+
```

- **_buffer**: Pointer to element data (stack or heap)
- **_len**: Current number of elements
- **_capacity**: 0 = constant/stack data, >0 = heap-allocated with ownership

## Tests

<!-- test: intrinsic-restriction -->
```maxon
function main() returns int
    // Attempt to call stdlib-only intrinsic from user code
    var x = __managed_array_len(0)
    return 0
end 'main'
```
```maxoncstderr
error E016
```
