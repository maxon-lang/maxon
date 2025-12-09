---
feature: map
status: stable
keywords: map, dictionary, hash, key-value, contains, insert, remove, get
category: collections
---

## Developer Notes

The `map` type is a generic hash map collection that stores key-value pairs. It uses open addressing with linear probing for collision resolution.

**Syntax:**
- Map literal: `["key1": value1, "key2": value2, ...]`
- Empty map: `map from K to V` (existing syntax)
- The key and value types are inferred from the literal

**Implementation:**
- Hash map with open addressing and linear probing
- Automatic resize at 75% load factor
- Initial capacity of 16 buckets
- Keys must implement `Hashable` and `Equatable` interfaces

**Slot States:**
- 0 = EMPTY (never used)
- 1 = OCCUPIED (has valid key-value pair)
- 2 = DELETED (tombstone for probing continuity)

**AST:**
- `MapLiteralExprAST` - represents `["key": value, ...]` expression
  - `entries`: vector of key-value expression pairs
  - `keyType`: inferred key type
  - `valueType`: inferred value type

**Parser:**
- Map literals are parsed when `[` is followed by an expression and `:`
- Distinguishes from array literals by checking for `:` after first expression
- Empty map literal `[:]` creates an empty map (requires type annotation)

**Semantic Analysis:**
- Infers key and value types from literal entries
- All keys must have consistent types
- All values must have consistent types
- Auto-imports `stdlib/collections/map.maxon` if not present
- Instantiates generic struct `map<K,V>` for the types
- Instantiates all methods with type bindings

**Code Generation:**
- Inline initialization (does not call `ExpressibleByMapLiteral.init`)
- Allocates heap arrays for `keys`, `values`, and `states`
- Zero-initializes state array (all EMPTY)
- Iterates through map literal entries calling `insert()` for each

**Memory Layout:**
```text
map<K,V> struct:
  keys: array<K>        // Key storage
  values: array<V>      // Value storage
  states: array<byte>   // Slot states
  count: int            // Number of occupied entries
  capacity: int         // Total slots
```

**Methods:**
- `insert(key K, value V)` - Add or update key-value pair
- `get(key K) V` - Get value for key (returns default if not found)
- `contains(key K) bool` - Check if key exists
- `remove(key K) bool` - Remove key, returns true if was present
- `count() int` - Get number of key-value pairs
- `capacity() int` - Get current capacity

**Interfaces:**
- `Dictionary` - Provides insert, get, contains, remove, count

**Struct Definition (stdlib/collections/map.maxon):**
```text
export struct map uses Key, Value is Dictionary with Key, Value
    var keys array of Key
    var values array of Value
    var states array of byte
    var count int
    var capacity int
```

## Documentation

# Map

A `map` is a collection that stores key-value pairs. It provides fast lookup, insertion, and removal using hash-based indexing.

## Creating a Map

Use map literal syntax with key-value pairs:

```maxon
var ages = ["alice": 30, "bob": 25, "charlie": 35]  // map<string,int>
var scores = [1: 100, 2: 85, 3: 92]                 // map<int,int>
```

The key and value types are automatically inferred from the literal values.

You can also create an empty map with explicit types:

```maxon
var m = map from string to int
```

## Methods

### insert(key, value)

Add a key-value pair to the map. If the key already exists, updates the value.

```maxon
var m = ["a": 1, "b": 2]
m.insert("c", 3)    // Map now has {"a": 1, "b": 2, "c": 3}
m.insert("a", 10)   // Updates "a" to 10
```

### get(key) Value

Get the value for a key. Returns a default value (zero-initialized) if the key is not found.

```maxon
var m = ["apple": 5, "banana": 3]
m.get("apple")     // 5
m.get("orange")    // 0 (default for int)
```

### contains(key) bool

Check if a key exists in the map. Returns `true` if found, `false` otherwise.

```maxon
var m = ["x": 1, "y": 2]
m.contains("x")    // true
m.contains("z")    // false
```

### remove(key) bool

Remove a key-value pair from the map. Returns `true` if the key was present and removed, `false` if it wasn't in the map.

```maxon
var m = ["a": 1, "b": 2, "c": 3]
m.remove("b")      // Returns true, map is now {"a": 1, "c": 3}
m.remove("z")      // Returns false, key wasn't present
```

### count() int

Get the number of key-value pairs in the map.

```maxon
var m = ["one": 1, "two": 2, "three": 3]
m.count()          // 3
```

### capacity() int

Get the current capacity (number of slots) of the internal hash table.

```maxon
var m = ["a": 1]
m.capacity()       // 16 (initial capacity)
```

## Automatic Growth

The map automatically grows when the load factor (count/capacity) exceeds 75%. When this happens, the capacity doubles and all entries are rehashed.

## Tests

<!-- test: literal.basic -->
```maxon
function main() int
    var m = ["a": 1, "b": 2, "c": 3]
    return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: literal.int-keys -->
```maxon
function main() int
    var m = [1: 100, 2: 200, 3: 300]
    return m.get(2)
end 'main'
```
```exitcode
200
```

<!-- test: contains.true -->
```maxon
function main() int
    var m = ["x": 10, "y": 20, "z": 30]
    if m.contains("y") 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: contains.false -->
```maxon
function main() int
    var m = ["x": 10, "y": 20, "z": 30]
    if m.contains("w") 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: get.existing -->
```maxon
function main() int
    var m = ["one": 1, "two": 2, "three": 3]
    return m.get("two")
end 'main'
```
```exitcode
2
```

<!-- test: get.missing -->
```maxon
function main() int
    var m = ["one": 1, "two": 2]
    return m.get("zero")
end 'main'
```
```exitcode
0
```

<!-- test: insert.new -->
```maxon
function main() int
    var m = ["a": 1, "b": 2]
    m.insert("c", 3)
    return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: insert.update -->
```maxon
function main() int
    var m = ["a": 1, "b": 2]
    m.insert("a", 100)
    return m.get("a")
end 'main'
```
```exitcode
100
```

<!-- test: insert.then-contains -->
```maxon
function main() int
    var m = ["x": 1]
    m.insert("y", 2)
    if m.contains("y") 'check'
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
    var m = ["a": 1, "b": 2, "c": 3]
    var removed = m.remove("b")
    if removed 'check'
        return m.count()
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
    var m = ["a": 1, "b": 2]
    var removed = m.remove("z")
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
    var m = ["a": 1, "b": 2, "c": 3]
    m.remove("b")
    if m.contains("b") 'check'
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
    var m = ["a": 1, "b": 2]
    return m.capacity()
end 'main'
```
```exitcode
16
```

<!-- test: empty-map.from-syntax -->
```maxon
function main() int
    var m = map from int to int
    m.insert(1, 100)
    return m.get(1)
end 'main'
```
```exitcode
100
```

<!-- test: single-entry -->
```maxon
function main() int
    var m = [42: 999]
    return m.get(42)
end 'main'
```
```exitcode
999
```

<!-- test: negative-keys -->
```maxon
function main() int
    var m = [0 - 5: 50, 0 - 3: 30, 0 - 1: 10]
    return m.get(0 - 3)
end 'main'
```
```exitcode
30
```

<!-- test: remove-reinsert -->
```maxon
function main() int
    var m = ["a": 1, "b": 2, "c": 3]
    m.remove("b")
    m.insert("b", 200)
    return m.get("b")
end 'main'
```
```exitcode
200
```
