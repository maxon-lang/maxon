---
feature: map-type
status: stable
keywords: map, hashmap, dictionary, collection, key-value
category: type-system
---

## Developer Notes

Hash maps provide O(1) average-case lookup, insertion, and deletion of key-value pairs.

**Declaration:**
- Syntax: `map from K to V` where K is the key type and V is the value type
- Example: `var m = map from int to int`
- Keys must implement the `Hashable` interface (int, byte, character, string)

**Implementation:**
- Open addressing with linear probing
- Initial capacity: 16 buckets
- Slot states: 0=EMPTY, 1=OCCUPIED, 2=DELETED (tombstone)
- Load factor threshold: 75% (TODO: automatic resizing)

**Memory Layout (struct):**
- `_keys []K` - Key storage array
- `_values []V` - Value storage array
- `_states []byte` - Slot state array
- `_count int` - Number of occupied entries
- `_capacity int` - Total slots

**Methods:**
- `insert(key, value)` - Insert or update a key-value pair
- `get(key)` - Get value for key (returns default if not found)
- `contains(key)` - Check if key exists
- `remove(key)` - Remove key-value pair (returns bool)
- `count()` - Return number of entries
- `capacity()` - Return total capacity

**Type System:**
- Map type notation: `map<K,V>` internally (e.g., `map<int,int>`)
- Struct type: `__map_K_V` (e.g., `__map_int_int`)
- Conforms to `Dictionary` interface

**Parser:**
- `map from K to V` parsed as `MapLiteralExprAST`
- Keywords: `map`, `from`, `to`

**Semantic Analysis:**
- Validates key type implements `Hashable`
- Registers map methods with qualified names: `map<K,V>.methodName`
- Method calls `m.insert(k, v)` resolved to `map<K,V>.insert(m, k, v)`

**Code Generation:**
- Map methods generate inline MIR code (not function calls)
- Hash function: Knuth's multiplicative hash for integers
- Linear probing for collision resolution

## Documentation

# Maps (Hash Maps)

Maps provide efficient key-value storage with O(1) average-case operations. Keys must be hashable types.

## Creating a Map

```maxon
var m = map from int to int     // Map from int keys to int values
var names = map from int to string  // Map from int keys to string values
```

## Supported Key Types

Keys must implement the `Hashable` interface:
- `int` - Integer keys
- `byte` - Byte keys
- `character` - Character keys
- `string` - String keys

## Methods

### insert(key, value)
Inserts a key-value pair, or updates the value if the key exists.

```maxon
var m = map from int to int
m.insert(1, 100)
m.insert(2, 200)
m.insert(1, 150)  // Updates existing key
```

### get(key)
Returns the value for a key. Returns the default value (0, false, "") if the key doesn't exist.

```maxon
var m = map from int to int
m.insert(1, 100)
var val = m.get(1)   // val = 100
var none = m.get(99) // none = 0 (default)
```

### contains(key)
Returns true if the key exists in the map.

```maxon
var m = map from int to int
m.insert(1, 100)
if m.contains(1) 'check'
    // Key exists
end 'check'
```

### remove(key)
Removes a key-value pair. Returns true if the key was found and removed.

```maxon
var m = map from int to int
m.insert(1, 100)
m.remove(1)          // Returns true
m.remove(99)         // Returns false (key not found)
```

### count()
Returns the number of entries in the map.

```maxon
var m = map from int to int
m.insert(1, 100)
m.insert(2, 200)
var n = m.count()    // n = 2
```

### capacity()
Returns the total capacity of the map.

```maxon
var m = map from int to int
var cap = m.capacity() // cap = 16 (initial capacity)
```

## Tests

<!-- test: basic.empty-map -->
```maxon
function main() int
    var m = map from int to int
    return m.count()
end 'main'
```
```exitcode
0
```

<!-- test: basic.insert-and-count -->
```maxon
function main() int
    var m = map from int to int
    m.insert(1, 100)
    m.insert(2, 200)
    m.insert(3, 300)
    return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: basic.insert-and-get -->
```maxon
function main() int
    var m = map from int to int
    m.insert(1, 100)
    m.insert(2, 200)
    m.insert(3, 300)
    return m.get(2)
end 'main'
```
```exitcode
200
```

<!-- test: basic.update-existing -->
```maxon
function main() int
    var m = map from int to int
    m.insert(1, 100)
    m.insert(1, 999)
    return m.get(1)
end 'main'
```
```exitcode
999
```

<!-- test: basic.contains-true -->
```maxon
function main() int
    var m = map from int to int
    m.insert(42, 100)
    if m.contains(42) 'check'
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
    var m = map from int to int
    m.insert(1, 100)
    if m.contains(99) 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: basic.get-missing-key -->
```maxon
function main() int
    var m = map from int to int
    m.insert(1, 100)
    return m.get(99)
end 'main'
```
```exitcode
0
```

<!-- test: basic.remove -->
```maxon
function main() int
    var m = map from int to int
    m.insert(1, 100)
    m.insert(2, 200)
    m.remove(1)
    return m.count()
end 'main'
```
```exitcode
1
```

<!-- test: basic.remove-then-contains -->
```maxon
function main() int
    var m = map from int to int
    m.insert(1, 100)
    m.remove(1)
    if m.contains(1) 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: basic.capacity -->
```maxon
function main() int
    var m = map from int to int
    return m.capacity()
end 'main'
```
```exitcode
16
```

<!-- test: collision.linear-probing -->
```maxon
function main() int
    var m = map from int to int
    // Insert multiple values - some may collide
    m.insert(1, 10)
    m.insert(17, 170)  // Likely same bucket as 1 with capacity 16
    m.insert(33, 330)  // Likely same bucket as 1 with capacity 16
    // All should be retrievable
    var sum = m.get(1) + m.get(17) + m.get(33)
    if sum != 510 'sum_check'
        return 1
    end 'sum_check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: collision.remove-and-find -->
```maxon
function main() int
    var m = map from int to int
    // Insert values that may collide
    m.insert(1, 10)
    m.insert(17, 170)
    m.insert(33, 330)
    // Remove middle one (creates tombstone)
    m.remove(17)
    // Should still find the last one past the tombstone
    if not m.contains(33) 'check'
        return 1
    end 'check'
    if m.get(33) != 330 'value'
        return 2
    end 'value'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: multiple.operations -->
```maxon
function main() int
    var m = map from int to int

    // Insert 10 items
    var i = 0
    while i < 10 'insert'
        m.insert(i, i * 10)
        i = i + 1
    end 'insert'

    if m.count() != 10 'count1'
        return 1
    end 'count1'

    // Verify all values
    i = 0
    while i < 10 'verify'
        if m.get(i) != i * 10 'check'
            return 2
        end 'check'
        i = i + 1
    end 'verify'

    // Remove half
    i = 0
    while i < 5 'remove'
        m.remove(i)
        i = i + 1
    end 'remove'

    if m.count() != 5 'count2'
        return 3
    end 'count2'

    // Remaining values should still work
    if m.get(7) != 70 'final'
        return 4
    end 'final'

    return 0
end 'main'
```
```exitcode
0
```

<!-- test: grow.exceeds-capacity -->
```maxon
function main() int
    var m = map from int to int

    // Insert 20 items (exceeds initial capacity of 16, triggers grow at 12)
    var i = 0
    while i < 20 'insert'
        m.insert(i, i * 10)
        i = i + 1
    end 'insert'

    // Verify count
    if m.count() != 20 'count'
        return 1
    end 'count'

    // Verify capacity grew (should be 32 after one grow)
    if m.capacity() < 32 'capacity'
        return 2
    end 'capacity'

    // Verify all values are still accessible after rehash
    i = 0
    while i < 20 'verify'
        if m.get(i) != i * 10 'check'
            return 3
        end 'check'
        i = i + 1
    end 'verify'

    return 0
end 'main'
```
```exitcode
0
```

<!-- test: grow.multiple-grows -->
```maxon
function main() int
    var m = map from int to int

    // Insert 50 items (triggers multiple grows: 16->32->64)
    var i = 0
    while i < 50 'insert'
        m.insert(i, i * 100)
        i = i + 1
    end 'insert'

    if m.count() != 50 'count'
        return 1
    end 'count'

    // Verify capacity is at least 64
    if m.capacity() < 64 'capacity'
        return 2
    end 'capacity'

    // Verify some values after multiple rehashes
    if m.get(0) != 0 'v0'
        return 3
    end 'v0'
    if m.get(25) != 2500 'v25'
        return 4
    end 'v25'
    if m.get(49) != 4900 'v49'
        return 5
    end 'v49'

    return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.non-hashable-key -->
```maxon
function main() int
    var m = map from float to int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 3, column 13
Map key type 'float' must implement Hashable interface
  Hashable types: int, string, character, byte
  Note: Only types that can be hashed can be used as map keys

  3 |     var m = map from float to int
    |             ^

Semantic Error: line 3, column 5
The variable 'm' is assigned but its value is never used

  3 |     var m = map from float to int
    |     ^
```
