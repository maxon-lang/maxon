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

```maxon
var ages = ["alice": 30, "bob": 25, "charlie": 35]  // map<string,int>
var scores = [1: 100, 2: 85, 3: 92]                 // map<int,int>
```

The key and value types are automatically inferred from the literal values.

You can also create an empty map with explicit types:

```maxon
var m = map from String to int
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

### contains(key) returns bool

Check if a key exists in the map. Returns `true` if found, `false` otherwise.

```maxon
var m = ["x": 1, "y": 2]
m.contains("x")    // true
m.contains("z")    // false
```

### remove(key) returns bool

Remove a key-value pair from the map. Returns `true` if the key was present and removed, `false` if it wasn't in the map.

```maxon
var m = ["a": 1, "b": 2, "c": 3]
m.remove("b")      // Returns true, map is now {"a": 1, "c": 3}
m.remove("z")      // Returns false, key wasn't present
```

### count() returns int

Get the number of key-value pairs in the map.

```maxon
var m = ["one": 1, "two": 2, "three": 3]
m.count()          // 3
```

### capacity() returns int

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
function main() returns int
    var m = ["a": 1, "b": 2, "c": 3]
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
function main() returns int
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
function main() returns int
    var m = ["one": 1, "two": 2, "three": 3]
    var result = m.get("two") else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
2
```

<!-- test: get.missing -->
```maxon
function main() returns int
    var m = ["one": 1, "two": 2]
    var result = m.get("zero") else 'default'
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
function main() returns int
    var m = ["a": 1, "b": 2]
    m.insert("a", 100)
    var result = m.get("a") else 'default'
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
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
    var m = ["a": 1, "b": 2]
    return m.capacity()
end 'main'
```
```exitcode
16
```

<!-- test: empty-map.from-syntax -->
```maxon
function main() returns int
    var m = map from int to int
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
    var m = [42: 999]
    var result = m.get(42) else 'default'
        result = 0
    end 'default'
    return result
end 'main'
```
```exitcode
999
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
    var m = ["a": 1, "b": 2, "c": 3]
    m.remove("b")
    m.insert("b", 200)
    var result = m.get("b") else 'default'
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
    var data map from int to int
end 'Container'

function main() returns int
    var m = map from int to int
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
