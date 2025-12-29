---
feature: internal-types
status: stable
keywords: [internal, opaque, __ManagedArray, stdlib, restriction]
category: type-system
---

# Internal Types

## Developer Notes

Internal types are compiler-known types that are only accessible from standard library code. They are identified by a leading underscore in the type name (e.g., `__ManagedArray`).

**Key Components:**

1. `isInternalType()` in `4-ast_to_ir.zig` checks if type name starts with `_`
2. `checkInternalTypeAccess()` verifies access is from stdlib or test fragments
3. `isStdlibFile()` determines if current source is in stdlib path
4. Error E018 is reported for unauthorized access to internal types

**Access Rules:**
- Internal types can be used in files under `/stdlib/` or `\stdlib\`
- Internal types can be used in test fragments under `specs/fragments/`
- Internal types can be used when processing monomorphized stdlib types (via `in_stdlib_method` flag)
- User code outside these paths cannot reference internal types

**Internal Types:**
- `__ManagedArray` - 24-byte struct (ptr + len + capacity) for managed array storage

**Error Reporting:**
```
error E018: file.maxon:1:1: internal type used outside stdlib: '__ManagedArray'
```

## Documentation

### Internal Types

Types starting with an underscore are internal to the standard library and cannot be used in user code. These types provide low-level implementation details that are not part of the public API.

The most common internal type is `__ManagedArray`, which provides the underlying storage for the stdlib `Array` type. While user code cannot directly use `__ManagedArray`, it is automatically used behind the scenes when working with arrays.

### Using Arrays in User Code

User code should use the public `Array` type from the standard library:

```text
var arr = Array of int{}
arr.push(42)
print(arr.count())
```

The internal `__ManagedArray` type is used internally by the `Array` implementation but is not accessible to user code.

## Tests

<!-- test: array-public-api -->
```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(10)
    arr.push(20)
    arr.push(30)
    return arr.count()
end 'main'
```
```exitcode
3
```

<!-- test: array-literal-works -->
```maxon
function main() returns int
    var arr Array of int = [1, 2, 3, 4, 5]
    return arr.count()
end 'main'
```
```exitcode
5
```
