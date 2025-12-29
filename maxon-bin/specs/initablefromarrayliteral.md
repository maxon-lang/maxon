---
feature: initablefromarrayliteral
status: stable
keywords: [array, literal, interface, InitableFromArrayLiteral, generic, type annotation]
category: type-system
---

# InitableFromArrayLiteral Interface

## Developer Notes

The `InitableFromArrayLiteral` interface allows types to be initialized from array literals. When a variable declaration has a type annotation and the annotated type conforms to this interface, the compiler automatically transforms the array literal into a call to the type's `init` method.

**Transformation:**
```text
var arr Array of int = [1, 2, 3]
```
becomes:
```text
// 1. Create __ManagedArray with elements
// 2. Call Array$init(managed)
// 3. Store result in arr
```

**Implementation Details:**
1. `convertVarDecl` checks for type annotation with generic type
2. `typeConformsTo()` checks if base type conforms to `InitableFromArrayLiteral`
3. If value is array literal, call `convertInitableFromArrayLiteral()`
4. Creates `__ManagedArray` struct (24 bytes: ptr + len + capacity)
5. Stores elements in heap-allocated buffer
6. Calls `Type$init(managed_ptr)` with sret for struct return

**__ManagedArray Layout:**
- offset 0: `_buffer` (ptr) - pointer to element storage
- offset 8: `_len` (i64) - number of elements
- offset 16: `_capacity` (i64) - allocated capacity

## Documentation

### Array Literals with Type Annotations

The stdlib `Array` type implements `InitableFromArrayLiteral`, allowing initialization from array literals:

```text
var arr Array of int = [1, 2, 3]
```

This creates an Array containing the elements 1, 2, and 3.

## Tests

<!-- test: array-from-literal -->
```maxon
function main() returns int
    var arr Array of int = [10, 20, 30]
    return arr.count()
end 'main'
```
```exitcode
3
```

<!-- test: array-from-literal-access -->
```maxon
function main() returns int
    var arr Array of int = [10, 20, 30]
    var val = arr.get(1)
    if let v = val 'check'
        return v
    end 'check'
    return 0
end 'main'
```
```exitcode
20
```

<!-- test: array-from-literal-first -->
```maxon
function main() returns int
    var arr Array of int = [42, 2, 3]
    var val = arr.first()
    if let v = val 'check'
        return v
    end 'check'
    return 0
end 'main'
```
```exitcode
42
```

<!-- test: array-from-literal-last -->
```maxon
function main() returns int
    var arr Array of int = [1, 2, 99]
    var val = arr.last()
    if let v = val 'check'
        return v
    end 'check'
    return 0
end 'main'
```
```exitcode
99
```
