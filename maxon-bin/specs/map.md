---
feature: map
status: stable
keywords: map, dictionary, hash, key-value, contains, insert, remove, get
category: collections
---
# Map

## Documentation

A `map` is a collection that stores key-value pairs. It provides fast lookup, insertion, and removal using hash-based indexing.

## Creating a Map

Use map literal syntax with key-value pairs:

```text
var scores = [1: 100, 2: 85, 3: 92]                 // map<int,int>
var lookup = [10: 1000, 20: 2000, 30: 3000]         // map<int,int>
```

The key and value types are automatically inferred from the literal values.

> **Note:** String keys will be supported when strings are implemented in Maxon. Currently, use integer keys.

You can also create an empty map with explicit types:

```text
var m = Map from int to int{}
```

## Methods

### insert(key, value)

Add a key-value pair to the map. If the key already exists, updates the value.

```text
var m = [1: 100, 2: 200]
m.insert(3, 300)    // Map now has {1: 100, 2: 200, 3: 300}
m.insert(1, 150)    // Updates key 1 to 150
```

### get(key) Value

Get the value for a key. Returns a default value (zero-initialized) if the key is not found.

```text
var m = [10: 5, 20: 3]
m.get(10)          // 5
m.get(30)          // 0 (default for int)
```

### contains(key) returns bool

Check if a key exists in the map. Returns `true` if found, `false` otherwise.

```text
var m = [1: 10, 2: 20]
m.contains(1)      // true
m.contains(3)      // false
```

### remove(key) returns bool

Remove a key-value pair from the map. Returns `true` if the key was present and removed, `false` if it wasn't in the map.

```text
var m = [1: 10, 2: 20, 3: 30]
m.remove(2)        // Returns true, map is now {1: 10, 3: 30}
m.remove(9)        // Returns false, key wasn't present
```

### count() returns int

Get the number of key-value pairs in the map.

```text
var m = [1: 10, 2: 20, 3: 30]
m.count()          // 3
```

### capacity() returns int

Get the current capacity (number of slots) of the internal hash table.

```text
var m = [1: 100]
m.capacity()       // 16 (initial capacity)
```

## Automatic Growth

The map automatically grows when the load factor (count/capacity) exceeds 75%. When this happens, the capacity doubles and all entries are rehashed.

## Tests

<!-- test: literal.basic -->
```maxon
function main() returns int
    var m = [1: 10, 2: 20, 3: 30]
    return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: literal.int-keys -->
```maxon
function main() returns int
    var m = [1: 100, 2: 200, 3: 300]
    var result = m.get(2) else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
200
```

<!-- test: contains.true -->
```maxon
function main() returns int
    var m = [10: 100, 20: 200, 30: 300]
    if m.contains(20) 'check'
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
function main() returns int
    var m = [10: 100, 20: 200, 30: 300]
    if m.contains(40) 'check'
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
function main() returns int
    var m = [1: 10, 2: 20, 3: 30]
    var result = m.get(2) else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
20
```

<!-- test: get.missing -->
```maxon
function main() returns int
    var m = [1: 10, 2: 20]
    var result = m.get(0) else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
0
```

<!-- test: insert.new -->
```maxon
function main() returns int
    var m = [1: 10, 2: 20]
    m.insert(3, 30)
    return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: insert.update -->
```maxon
function main() returns int
    var m = [1: 10, 2: 20]
    m.insert(1, 100)
    var result = m.get(1) else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
100
```

<!-- test: insert.then-contains -->
```maxon
function main() returns int
    var m = [10: 1]
    m.insert(20, 2)
    if m.contains(20) 'check'
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
function main() returns int
    var m = [1: 10, 2: 20, 3: 30]
    var removed = m.remove(2)
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
function main() returns int
    var m = [1: 10, 2: 20]
    var removed = m.remove(99)
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
function main() returns int
    var m = [1: 10, 2: 20, 3: 30]
    m.remove(2)
    if m.contains(2) 'check'
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
function main() returns int
    var m = [1: 10, 2: 20]
    return m.capacity()
end 'main'
```
```exitcode
16
```

<!-- test: empty-map.from-syntax -->
```maxon
function main() returns int
    var m = Map from int to int{}
    m.insert(1, 100)
    var result = m.get(1) else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
100
```

<!-- test: single-entry -->
```maxon
function main() returns int
    var m = [42: 99]
    var result = m.get(42) else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
99
```

<!-- test: negative-keys -->
```maxon
function main() returns int
    var m = [0 - 5: 50, 0 - 3: 30, 0 - 1: 10]
    var result = m.get(0 - 3) else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
30
```

<!-- test: remove-reinsert -->
```maxon
function main() returns int
    var m = [1: 10, 2: 20, 3: 30]
    m.remove(2)
    m.insert(2, 200)
    var result = m.get(2) else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
200
```

<!-- test: map-type-in-field -->
```maxon
type Container
    export var data Map from int to int
end 'Container'

function main() returns int
    var m = Map from int to int{}
    m.insert(1, 42)
    var c = Container{data: m}
    var result = c.data.get(1) else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
42
```
