---
feature: managed-array-internals
category: internals
status: stable
---
# Managed Array Internals

## Documentation

### Array Memory Management

Maxon manages array memory automatically using reference counting and scope-based cleanup.

### Stack vs Heap Allocation

Small arrays with known size at compile time use stack allocation. Arrays that grow dynamically use heap allocation:

```maxon
var stack = [1, 2, 3]  // Stack allocated (capacity = 0)
var heap = Array of int{}
heap.push(1)  // Heap allocated (capacity > 0)
```

### Reference Counting

Heap-allocated arrays use reference counting to enable efficient sharing.

### Automatic Cleanup

Arrays are automatically released when they go out of scope:

```maxon
if true 'scope'
    var temp = Array of int{}
    temp.push(1)
    // temp is released here
end 'scope'
```

## Tests

### Heap Allocation Tests

<!-- test: heap-array-push -->
<!-- TrackMemory: true -->
Arrays that grow via push() should allocate on heap and free properly.
```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(1)
    arr.push(2)
    arr.push(3)
    print("{arr.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 32 bytes (array grow)
ALLOC #2: 30 bytes (int.toString)
MOVE: managed
ALLOC #3: 11 bytes (string concat)
3
DECREF: <temp> -> rc=0
FREE #2: 30 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #3: 11 bytes (string cleanup)
FREE #1: 32 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 73 bytes
Freed:     73 bytes
Leaked:    0 bytes
Moves:     1
Increfs:   0
Decrefs:   2
```

<!-- test: heap-array-scope-cleanup -->
<!-- TrackMemory: true -->
Heap arrays in inner scopes should be cleaned up on scope exit.
```maxon
function main() returns int
    if true 'outer'
        var outer_arr = Array of int{}
        outer_arr.push(100)
        if true 'inner'
            var inner_arr = Array of int{}
            inner_arr.push(200)
            var inner_val = try inner_arr.get(0) otherwise 0
            print("{inner_val}\n")
        end 'inner'
        var outer_val = try outer_arr.get(0) otherwise 0
        print("{outer_val}\n")
    end 'outer'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 32 bytes (array grow)
ALLOC #2: 32 bytes (array grow)
ALLOC #3: 30 bytes (int.toString)
MOVE: managed
ALLOC #4: 13 bytes (string concat)
200
DECREF: <temp> -> rc=0
FREE #3: 30 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #4: 13 bytes (string cleanup)
ALLOC #5: 30 bytes (int.toString)
MOVE: managed
ALLOC #6: 13 bytes (string concat)
100
DECREF: <temp> -> rc=0
FREE #5: 30 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #6: 13 bytes (string cleanup)
FREE #1: 32 bytes (array cleanup)
FREE #2: 32 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 150 bytes
Freed:     150 bytes
Leaked:    0 bytes
Moves:     2
Increfs:   0
Decrefs:   4
```

### Stack Array Tests

<!-- test: stack-array-no-alloc -->
<!-- TrackMemory: true -->
Fixed-size stack arrays should not allocate on heap.
Note: print with interpolation allocates for the string conversion.
```maxon
function main() returns int
    var arr = [10, 20, 30]
    var val = try arr.get(1) otherwise 0
    print("{val}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 24 bytes (set buffer)
ALLOC #2: 30 bytes (int.toString)
MOVE: managed
ALLOC #3: 12 bytes (string concat)
20
DECREF: <temp> -> rc=0
FREE #2: 30 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #3: 12 bytes (string cleanup)
FREE #1: 24 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 66 bytes
Freed:     66 bytes
Leaked:    0 bytes
Moves:     1
Increfs:   0
Decrefs:   2
```

### Loop Growth Tests

<!-- test: loop-array-growth -->
<!-- TrackMemory: true -->
Growing an array in a loop should release old buffers properly.
```maxon
function main() returns int
    var arr = Array of int{}
    var i = 0
    while i < 10 'loop'
        arr.push(i)
        i = i + 1
    end 'loop'
    print("{arr.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 32 bytes (array grow)
REALLOC #1: 32 -> 64 bytes (array grow)
REALLOC #1: 64 -> 128 bytes (array grow)
ALLOC #2: 30 bytes (int.toString)
MOVE: managed
ALLOC #3: 12 bytes (string concat)
10
DECREF: <temp> -> rc=0
FREE #2: 30 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #3: 12 bytes (string cleanup)
FREE #1: 128 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 266 bytes
Freed:     266 bytes
Leaked:    0 bytes
Moves:     1
Increfs:   0
Decrefs:   2
```
### Struct Field Array Method Call

<!-- test: struct-field-array-count -->
Calling `.count()` on an array field of a struct should work correctly.
```maxon
type Config
    export var sources Array of String
end 'Config'

function main() returns int
    var config = Config{sources: ["a", "b", "c"]}
    let count = config.sources.count()
    print("{count}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```
