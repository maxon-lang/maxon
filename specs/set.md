---
feature: set
status: stable
keywords: set, collection, hash, unique, contains, insert, remove
category: collections
---

## Developer Notes

The `set` type is a generic hash set collection that stores unique elements. It uses open addressing with linear probing for collision resolution.

**Syntax:**
- Creation: `set from [element1, element2, ...]`
- The element type is inferred from the array literal

**Implementation:**
- Hash set with open addressing and linear probing
- Automatic resize at 75% load factor
- Initial capacity of 16 buckets
- Elements must implement `Hashable` interface (have `hash()` method)

**Slot States:**
- 0 = EMPTY (never used)
- 1 = OCCUPIED (has valid element)
- 2 = DELETED (tombstone for probing continuity)

**AST:**
- `SetFromExprAST` - represents `set from [array]` expression
  - `setType`: the generic struct name (e.g., "set")
  - `arrayExpr`: the array literal expression
  - `inferredElementType`: element type inferred from array

**Parser:**
- `parseSetFromExpr()` in parser_expr.cpp
- Parses `set from [expr]` syntax

**Semantic Analysis:**
- Infers element type from array literal
- Auto-imports `stdlib/collections/set.maxon` if not present
- Instantiates generic struct `set<T>` for the element type
- Instantiates all methods with type bindings

**Code Generation:**
- Inline initialization (does not call `ExpressibleByArrayLiteral.init`)
- Allocates heap arrays for `_elements` and `_states`
- Zero-initializes state array (all EMPTY)
- Iterates through array literal elements calling `insert()` for each

**Memory Layout:**
```text
set<T> struct:
  _elements: ptr to T[]     // Element storage (heap)
  _states: ptr to byte[]    // Slot states (heap)
  _count: int               // Number of occupied entries
  _capacity: int            // Total slots
```

**Methods:**
- `insert(element T)` - Add element to set (no-op if exists)
- `contains(element T) bool` - Check if element exists
- `remove(element T) bool` - Remove element, returns true if was present
- `count() int` - Get number of elements
- `capacity() int` - Get current capacity

**Struct Definition (stdlib/collections/set.maxon):**
```text
export struct set uses Element is ExpressibleByArrayLiteral with Element
    var _elements array of Element
    var _states array of byte
    var _count int
    var _capacity int
```

## Documentation

# Set

A `set` is a collection of unique elements. It provides fast membership testing, insertion, and removal using hash-based lookup.

## Creating a Set

Use the `set from` syntax with an array literal:

```maxon
var s = set from [1, 2, 3]           // set<int> with elements 1, 2, 3
var names = set from ["alice", "bob"] // set<string>
```

The element type is automatically inferred from the array values.

## Methods

### insert(element)

Add an element to the set. If the element already exists, this is a no-op.

```maxon
var s = set from [1, 2]
s.insert(3)    // Set now contains {1, 2, 3}
s.insert(2)    // No change - 2 already exists
```

### contains(element) bool

Check if an element exists in the set. Returns `true` if found, `false` otherwise.

```maxon
var s = set from [1, 2, 3]
s.contains(2)  // true
s.contains(5)  // false
```

### remove(element) bool

Remove an element from the set. Returns `true` if the element was present and removed, `false` if it wasn't in the set.

```maxon
var s = set from [1, 2, 3]
s.remove(2)    // Returns true, set is now {1, 3}
s.remove(5)    // Returns false, element wasn't present
```

### count() int

Get the number of elements in the set.

```maxon
var s = set from [1, 2, 3]
s.count()      // 3
```

### capacity() int

Get the current capacity (number of slots) of the internal hash table.

```maxon
var s = set from [1, 2, 3]
s.capacity()   // 16 (initial capacity)
```

## Automatic Growth

The set automatically grows when the load factor (count/capacity) exceeds 75%. When this happens, the capacity doubles and all elements are rehashed.

## Tests

<!-- test: basic.creation -->
```maxon
function main() int
    var s = set from [1, 2, 3]
    return s.count()
end 'main'
```
```exitcode
3
```

<!-- test: basic.contains-true -->
```maxon
function main() int
    var s = set from [10, 20, 30]
    if s.contains(20) 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: basic.contains-false -->
```maxon
function main() int
    var s = set from [10, 20, 30]
    if s.contains(99) 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: insert.new-element -->
```maxon
function main() int
    var s = set from [1, 2, 3]
    s.insert(4)
    return s.count()
end 'main'
```
```exitcode
4
```

<!-- test: insert.duplicate -->
```maxon
function main() int
    var s = set from [1, 2, 3]
    s.insert(2)
    return s.count()
end 'main'
```
```exitcode
3
```

<!-- test: insert.then-contains -->
```maxon
function main() int
    var s = set from [1, 2, 3]
    s.insert(5)
    if s.contains(5) 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: remove.existing -->
```maxon
function main() int
    var s = set from [1, 2, 3]
    var removed = s.remove(2)
    if removed 'check'
        return s.count()
    end 'check'
    return 0 - 1
end 'main'
```
```exitcode
2
```

<!-- test: remove.nonexistent -->
```maxon
function main() int
    var s = set from [1, 2, 3]
    var removed = s.remove(99)
    if removed 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove.then-contains -->
```maxon
function main() int
    var s = set from [1, 2, 3]
    s.remove(2)
    if s.contains(2) 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: capacity.initial -->
```maxon
function main() int
    var s = set from [1, 2, 3]
    return s.capacity()
end 'main'
```
```exitcode
16
```

<!-- test: grow.trigger -->
```maxon
function main() int
    var s = set from [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]
    return s.capacity()
end 'main'
```
```exitcode
32
```

<!-- test: grow.preserves-elements -->
```maxon
function main() int
    var s = set from [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]
    var allPresent = 1
    var i = 1
    while i <= 15 'check'
        if not s.contains(i) 'missing'
            allPresent = 0
        end 'missing'
        i = i + 1
    end 'check'
    return allPresent
end 'main'
```
```exitcode
1
```

<!-- test: empty.single-element -->
```maxon
function main() int
    var s = set from [42]
    return s.count()
end 'main'
```
```exitcode
1
```

<!-- test: remove-reinsert -->
```maxon
function main() int
    var s = set from [1, 2, 3]
    s.remove(2)
    s.insert(2)
    if s.contains(2) 'check'
        return s.count()
    end 'check'
    return 0 - 1
end 'main'
```
```exitcode
3
```

<!-- test: negative-values -->
```maxon
function main() int
    var s = set from [0 - 5, 0 - 3, 0 - 1, 0, 1, 3, 5]
    if s.contains(0 - 3) 'check'
        return s.count()
    end 'check'
    return 0 - 1
end 'main'
```
```exitcode
7
```
