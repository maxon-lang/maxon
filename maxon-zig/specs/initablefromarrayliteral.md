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

Types that implement `InitableFromArrayLiteral` can be initialized from array literals using a type annotation:

```text
interface InitableFromArrayLiteral uses Element
    static function init(managed __ManagedArray) returns Self
end 'InitableFromArrayLiteral'

type MyArray uses Element is InitableFromArrayLiteral with Element
    var managed __ManagedArray
    
    static function init(managed __ManagedArray) returns Self
        return Self{managed: managed}
    end 'init'
end 'MyArray'

// Use with type annotation
var arr MyArray of int = [1, 2, 3]
```

## Tests

<!-- test: type-annotation-basic -->
```maxon
interface InitableFromArrayLiteral uses Element
    static function init(managed __ManagedArray) returns Self
end 'InitableFromArrayLiteral'

type IntArray is InitableFromArrayLiteral with int
    var managed __ManagedArray
    var extra int
    
    static function init(managed __ManagedArray) returns Self
        return Self{managed: managed, extra: 42}
    end 'init'
    
    function getExtra() returns int
        return extra
    end 'getExtra'
end 'IntArray'

function main() returns int
    var arr IntArray = [10, 20, 30]
    return arr.getExtra()
end 'main'
```
```exitcode
42
```

<!-- test: type-annotation-generic -->
```maxon
interface InitableFromArrayLiteral uses Element
    static function init(managed __ManagedArray) returns Self
end 'InitableFromArrayLiteral'

type GenArray uses Element is InitableFromArrayLiteral with Element
    var managed __ManagedArray
    var marker int
    
    static function init(managed __ManagedArray) returns Self
        return Self{managed: managed, marker: 99}
    end 'init'
    
    function getMarker() returns int
        return marker
    end 'getMarker'
end 'GenArray'

function main() returns int
    var arr GenArray of int = [1, 2, 3]
    return arr.getMarker()
end 'main'
```
```exitcode
99
```

<!-- test: managed-array-indexing -->
```maxon
interface InitableFromArrayLiteral uses Element
    static function init(managed __ManagedArray) returns Self
end 'InitableFromArrayLiteral'

type IntArray is InitableFromArrayLiteral with int
    var managed __ManagedArray
    var marker int
    
    static function init(managed __ManagedArray) returns Self
        return Self{managed: managed, marker: 99}
    end 'init'
    
    function getMarker() returns int
        return marker
    end 'getMarker'
    
    function get(index int) returns int or nil
        return managed[index]
    end 'get'
end 'IntArray'

function main() returns int
    var arr IntArray = [10, 20, 30]
    return arr.getMarker()
end 'main'
```
```exitcode
99
```

<!-- test: managed-array-get-element -->
```maxon
interface InitableFromArrayLiteral uses Element
    static function init(managed __ManagedArray) returns Self
end 'InitableFromArrayLiteral'

type IntArr is InitableFromArrayLiteral with int
    var managed __ManagedArray
    
    static function init(managed __ManagedArray) returns Self
        return Self{managed: managed}
    end 'init'
    
    function get(index int) returns int or nil
        return managed[index]
    end 'get'
end 'IntArr'

function main() returns int
    var arr IntArr = [10, 20, 30]
    var val = arr.get(1)
    if let v = val 'check'
        return v
    end 'check'
    return 88
end 'main'
```
```exitcode
20
```
