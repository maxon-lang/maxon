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

You can also create an empty map with a type alias:

```text
typealias IntIntMap is Map with (int, int)
var m = IntIntMap{}
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
  var result = try m.get(2) otherwise 0
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
  var result = try m.get(2) otherwise 0
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
  var result = try m.get(0) otherwise 0
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
  var result = try m.get(1) otherwise 0
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

<!-- test: empty-map.from-syntax -->
```maxon
typealias IntIntMap is Map with (int, int)

function main() returns int
  var m = IntIntMap{}
  m.insert(1, 100)
  var result = try m.get(1) otherwise 0
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
  var result = try m.get(42) otherwise 0
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
  var result = try m.get(0 - 3) otherwise 0
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
  var result = try m.get(2) otherwise 0
  return result
end 'main'
```
```exitcode
200
```

<!-- test: map-type-in-field -->
```maxon
typealias IntIntMap is Map with (int, int)

type Container
  export var data IntIntMap
end 'Container'

function main() returns int
  var m = IntIntMap{}
  m.insert(1, 42)
  var c = Container{data: m}
  var result = try c.data.get(1) otherwise 0
  return result
end 'main'
```
```exitcode
42
```
<!-- test: string-keys-basic -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
  var m = ["a": 1, "b": 2]
  var result = try m.get("a") otherwise 0
  return result
end 'main'
```
```exitcode
1
```
```stdout
MOVE: managed
ALLOC #1: 80 bytes (map buffer)
ALLOC #2: 16 bytes (map buffer)
MOVE: managed
ALLOC #3: 648 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #4: 136 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #5: 136 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: ks
MOVE: vs
MOVE: sts
CLEANUP: k
CLEANUP: k
MOVE: result
FREE #1: 80 bytes (map literal keys cleanup)
FREE #2: 16 bytes (map literal values cleanup)
MOVE: managed
CLEANUP: existing
CLEANUP: m
CLEANUP: m
DECREF: m -> rc=0
FREE #3: 648 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #4: 136 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #5: 136 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 1016 bytes
Freed:     1016 bytes
Leaked:    0 bytes
Moves:     7
Increfs:   3
Decrefs:   3
Copies:    0
Cleanups:  7
```

<!-- test: string-keys-get-multiple -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
  var m = ["hello": 10, "world": 20, "foo": 30]
  var a = try m.get("hello") otherwise 0
  var b = try m.get("world") otherwise 0
  return a + b
end 'main'
```
```exitcode
30
```
```stdout
MOVE: managed
ALLOC #1: 120 bytes (map buffer)
ALLOC #2: 24 bytes (map buffer)
MOVE: managed
MOVE: managed
ALLOC #3: 648 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #4: 136 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #5: 136 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: ks
MOVE: vs
MOVE: sts
CLEANUP: k
CLEANUP: k
CLEANUP: existing
CLEANUP: k
MOVE: result
FREE #1: 120 bytes (map literal keys cleanup)
FREE #2: 24 bytes (map literal values cleanup)
MOVE: managed
CLEANUP: existing
MOVE: managed
CLEANUP: existing
CLEANUP: m
CLEANUP: m
DECREF: m -> rc=0
FREE #3: 648 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #4: 136 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #5: 136 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 1064 bytes
Freed:     1064 bytes
Leaked:    0 bytes
Moves:     9
Increfs:   3
Decrefs:   3
Copies:    0
Cleanups:  10
```

<!-- test: string-keys-contains -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
  var m = ["key1": 100, "key2": 200]
  if m.contains("key1") 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```
```stdout
MOVE: managed
ALLOC #1: 80 bytes (map buffer)
ALLOC #2: 16 bytes (map buffer)
MOVE: managed
ALLOC #3: 648 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #4: 136 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #5: 136 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: ks
MOVE: vs
MOVE: sts
CLEANUP: k
CLEANUP: k
MOVE: result
FREE #1: 80 bytes (map literal keys cleanup)
FREE #2: 16 bytes (map literal values cleanup)
MOVE: managed
CLEANUP: existing
CLEANUP: m
CLEANUP: m
DECREF: m -> rc=0
FREE #3: 648 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #4: 136 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #5: 136 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 1016 bytes
Freed:     1016 bytes
Leaked:    0 bytes
Moves:     7
Increfs:   3
Decrefs:   3
Copies:    0
Cleanups:  7
```

<!-- test: string-keys-insert-update -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
  var m = ["x": 10]
  m.insert("x", 99)
  var result = try m.get("x") otherwise 0
  return result
end 'main'
```
```exitcode
99
```
```stdout
MOVE: managed
ALLOC #1: 40 bytes (map buffer)
ALLOC #2: 8 bytes (map buffer)
ALLOC #3: 648 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #4: 136 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #5: 136 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: ks
MOVE: vs
MOVE: sts
CLEANUP: k
MOVE: result
FREE #1: 40 bytes (map literal keys cleanup)
FREE #2: 8 bytes (map literal values cleanup)
MOVE: managed
CLEANUP: existing
MOVE: managed
CLEANUP: existing
CLEANUP: m
CLEANUP: m
DECREF: m -> rc=0
FREE #3: 648 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #4: 136 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #5: 136 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 968 bytes
Freed:     968 bytes
Leaked:    0 bytes
Moves:     7
Increfs:   3
Decrefs:   3
Copies:    0
Cleanups:  7
```

<!-- test: string-keys-remove -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
  var m = ["alpha": 1, "beta": 2, "gamma": 3]
  m.remove("beta")
  if m.contains("beta") 'check'
    return 1
  end 'check'
  return m.count()
end 'main'
```
```exitcode
2
```
```stdout
MOVE: managed
ALLOC #1: 120 bytes (map buffer)
ALLOC #2: 24 bytes (map buffer)
MOVE: managed
MOVE: managed
ALLOC #3: 648 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #4: 136 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #5: 136 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: ks
MOVE: vs
MOVE: sts
CLEANUP: k
CLEANUP: k
CLEANUP: k
MOVE: result
FREE #1: 120 bytes (map literal keys cleanup)
FREE #2: 24 bytes (map literal values cleanup)
MOVE: managed
CLEANUP: existing
MOVE: managed
CLEANUP: m
CLEANUP: m
DECREF: m -> rc=0
FREE #3: 648 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #4: 136 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #5: 136 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 1064 bytes
Freed:     1064 bytes
Leaked:    0 bytes
Moves:     9
Increfs:   3
Decrefs:   3
Copies:    0
Cleanups:  8
```

<!-- test: string-keys-early-return -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
  var m = ["test": 42]
  var v = try m.get("test") otherwise 0
  return v
end 'main'
```
```exitcode
42
```

<!-- test: multiline-map-literal -->
```maxon
function main() returns int
  var m = [
    1: 100,
    2: 200,
    3: 300
  ]
  return m.count()
end 'main'
```
```exitcode
3
```
```stdout
MOVE: managed
ALLOC #1: 40 bytes (map buffer)
ALLOC #2: 8 bytes (map buffer)
ALLOC #3: 648 bytes (array grow)
ALLOC #4: 136 bytes (array grow)
ALLOC #5: 136 bytes (array grow)
MOVE: ks
MOVE: vs
MOVE: sts
MOVE: result
FREE #1: 40 bytes (map literal keys cleanup)
FREE #2: 8 bytes (map literal values cleanup)
MOVE: managed
CLEANUP: m
DECREF: m -> rc=0
FREE #3: 648 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #4: 136 bytes (array cleanup)
CLEANUP: m
DECREF: m -> rc=0
FREE #5: 136 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 968 bytes
Freed:     968 bytes
Leaked:    0 bytes
Moves:     6
