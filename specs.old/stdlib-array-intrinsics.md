---
feature: stdlib-array-intrinsics
status: experimental
keywords: stdlib, array, intrinsics, __ManagedArray, internal
category: type-system
---

## Developer Notes

This spec covers the compiler intrinsics used by the stdlib Array type. These intrinsics are stdlib-only and cannot be called from user code.

**__ManagedArray Type:**
- Compiler-internal struct type (24 bytes)
- Layout: `{ ptr _buffer, i64 _len, i64 _capacity }`
- `_buffer`: pointer to element data
- `_len`: current number of elements
- `_capacity`: 0 for stack/constant data, >0 for heap-allocated

**Intrinsics (stdlib-only):**
- `__managed_array_len(managed)` - returns length
- `__managed_array_capacity(managed)` - returns capacity
- `__managed_array_set_at(managed, index, value)` - sets element (no bounds check)
- `__managed_array_set_length(managed, new_len)` - updates length field
- `__managed_array_grow(managed, new_capacity)` - reallocates buffer
- `__managed_array_shift_right(managed, start_index, count)` - shifts elements right
- `__managed_array_shift_left(managed, start_index, count)` - shifts elements left

**stdlib-only Enforcement:**
- Intrinsics starting with `__managed_array_` can only be called from files in `/stdlib/`
- User code calling these intrinsics will get error E016

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
