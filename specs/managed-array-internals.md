---
feature: managed-array-internals
category: internals
status: stable
---

## Developer Notes

This spec covers internal tests for the managed array system. These tests verify:
- Stack vs heap allocation thresholds
- Reference counting behavior
- Scope-based cleanup
- Memory tracking accuracy

The managed array system uses:
- Capacity semantics: 0 = stack allocated, >0 = heap allocated with ownership
- 8-byte header for heap arrays (refcount + data_size)
- Automatic cleanup at scope exit

## Documentation

### Array Memory Management

Maxon manages array memory automatically using reference counting and scope-based cleanup.

### Stack vs Heap Allocation

Small arrays with known size at compile time use stack allocation. Arrays that grow dynamically use heap allocation:

```maxon
var stack = [1, 2, 3]  // Stack allocated (capacity = 0)
var heap = array of int
heap.push(1)  // Heap allocated (capacity > 0)
```

### Reference Counting

Heap-allocated arrays use reference counting to enable efficient sharing.

### Automatic Cleanup

Arrays are automatically released when they go out of scope:

```maxon
if true 'scope'
    var temp = array of int
    temp.push(1)
    // temp is released here
end 'scope'
```

## Tests

### Heap Allocation Tests

<!-- test: heap-array-push -->
<!-- TrackAllocs: true -->
Arrays that grow via push() should allocate on heap and free properly.
```maxon
function main() returns int
    var arr = array of int
    arr.push(1)
    arr.push(2)
    arr.push(3)
    printInt(arr.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 24 bytes (array grow)
3ALLOC #2: 10 bytes (cstring conversion)
FREE #2: 10 bytes (cstring release)

FREE #1: 24 bytes (array cleanup)

=== ALLOC STATS ===
Allocated: 34 bytes
Freed:     34 bytes
Leaked:    0 bytes
```

<!-- test: heap-array-scope-cleanup -->
<!-- TrackAllocs: true -->
Heap arrays in inner scopes should be cleaned up on scope exit.
```maxon
function main() returns int
    if true 'outer'
        var outer_arr = array of int
        outer_arr.push(100)
        if true 'inner'
            var inner_arr = array of int
            inner_arr.push(200)
            printInt(inner_arr[0])
        end 'inner'
        printInt(outer_arr[0])
    end 'outer'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 24 bytes (array grow)
ALLOC #2: 24 bytes (array grow)
200ALLOC #3: 10 bytes (cstring conversion)
FREE #3: 10 bytes (cstring release)

100ALLOC #4: 10 bytes (cstring conversion)
FREE #4: 10 bytes (cstring release)

FREE #1: 24 bytes (array cleanup)
FREE #2: 24 bytes (array cleanup)

=== ALLOC STATS ===
Allocated: 68 bytes
Freed:     68 bytes
Leaked:    0 bytes
```

### Stack Array Tests

<!-- test: stack-array-no-alloc -->
<!-- TrackAllocs: true -->
Fixed-size stack arrays should not allocate on heap.
Note: printInt cstring conversion is tracked.
```maxon
function main() returns int
    var arr = [10, 20, 30]
    printInt(arr[1])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
20ALLOC #1: 10 bytes (cstring conversion)
FREE #1: 10 bytes (cstring release)


=== ALLOC STATS ===
Allocated: 10 bytes
Freed:     10 bytes
Leaked:    0 bytes
```

### Loop Growth Tests

<!-- test: loop-array-growth -->
<!-- TrackAllocs: true -->
Growing an array in a loop should release old buffers properly.
```maxon
function main() returns int
    var arr = array of int
    var i = 0
    while i < 10 'loop'
        arr.push(i)
        i = i + 1
    end 'loop'
    printInt(arr.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 24 bytes (array grow)
ALLOC #2: 40 bytes (array grow)
FREE #1: 24 bytes (array grow)
ALLOC #3: 72 bytes (array grow)
FREE #2: 40 bytes (array grow)
10ALLOC #4: 10 bytes (cstring conversion)
FREE #4: 10 bytes (cstring release)

FREE #3: 72 bytes (array cleanup)

=== ALLOC STATS ===
Allocated: 146 bytes
Freed:     146 bytes
Leaked:    0 bytes
```
