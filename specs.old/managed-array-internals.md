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
    print("{arr.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 40 bytes (array grow)
ALLOC #2: 40 bytes (array grow)
ALLOC #3: 10 bytes (int.toString)
ALLOC #4: 10 bytes (int.toString)
ALLOC #5: 32 bytes (int.toString)
3ALLOC #6: 32 bytes (string metadata)
ALLOC #7: 10 bytes (cstring conversion)
ALLOC #8: 10 bytes (cstring conversion)
FREE #8: 10 bytes (cstring release)
FREE #7: 10 bytes (cstring release)

FREE #6: 32 bytes (string metadata)
FREE #4: 10 bytes (cstring release)
FREE #3: 10 bytes (cstring release)
FREE #2: 40 bytes (array cleanup)
FREE #1: 40 bytes (array cleanup)
FREE #5: 32 bytes (string metadata)

=== ALLOC STATS ===
Allocated: 184 bytes
Freed:     184 bytes
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
            print("{inner_arr[0]}\n")
        end 'inner'
        print("{outer_arr[0]}\n")
    end 'outer'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 40 bytes (array grow)
ALLOC #2: 40 bytes (array grow)
ALLOC #3: 40 bytes (array grow)
ALLOC #4: 40 bytes (array grow)
ALLOC #5: 12 bytes (int.toString)
ALLOC #6: 12 bytes (int.toString)
ALLOC #7: 32 bytes (int.toString)
200ALLOC #8: 32 bytes (string metadata)
ALLOC #9: 10 bytes (cstring conversion)
ALLOC #10: 10 bytes (cstring conversion)
FREE #10: 10 bytes (cstring release)
FREE #9: 10 bytes (cstring release)

FREE #8: 32 bytes (string metadata)
FREE #6: 12 bytes (cstring release)
FREE #5: 12 bytes (cstring release)
ALLOC #11: 12 bytes (int.toString)
ALLOC #12: 12 bytes (int.toString)
ALLOC #13: 32 bytes (int.toString)
100ALLOC #14: 32 bytes (string metadata)
ALLOC #15: 10 bytes (cstring conversion)
ALLOC #16: 10 bytes (cstring conversion)
FREE #16: 10 bytes (cstring release)
FREE #15: 10 bytes (cstring release)

FREE #14: 32 bytes (string metadata)
FREE #12: 12 bytes (cstring release)
FREE #11: 12 bytes (cstring release)
FREE #2: 40 bytes (array cleanup)
FREE #1: 40 bytes (array cleanup)
FREE #4: 40 bytes (array cleanup)
FREE #3: 40 bytes (array cleanup)
FREE #7: 32 bytes (string metadata)
FREE #13: 32 bytes (string metadata)

=== ALLOC STATS ===
Allocated: 376 bytes
Freed:     376 bytes
Leaked:    0 bytes
```

### Stack Array Tests

<!-- test: stack-array-no-alloc -->
<!-- TrackAllocs: true -->
Fixed-size stack arrays should not allocate on heap.
Note: print with interpolation allocates for the string conversion.
```maxon
function main() returns int
    var arr = [10, 20, 30]
    print("{arr[1]}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 11 bytes (int.toString)
ALLOC #2: 11 bytes (int.toString)
ALLOC #3: 32 bytes (int.toString)
20ALLOC #4: 32 bytes (string metadata)
ALLOC #5: 10 bytes (cstring conversion)
ALLOC #6: 10 bytes (cstring conversion)
FREE #6: 10 bytes (cstring release)
FREE #5: 10 bytes (cstring release)

FREE #4: 32 bytes (string metadata)
FREE #2: 11 bytes (cstring release)
FREE #1: 11 bytes (cstring release)
FREE #3: 32 bytes (string metadata)

=== ALLOC STATS ===
Allocated: 106 bytes
Freed:     106 bytes
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
    print("{arr.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 40 bytes (array grow)
ALLOC #2: 40 bytes (array grow)
ALLOC #3: 72 bytes (array grow)
ALLOC #4: 72 bytes (array grow)
FREE #2: 40 bytes (array grow)
FREE #1: 40 bytes (array grow)
ALLOC #5: 136 bytes (array grow)
ALLOC #6: 136 bytes (array grow)
FREE #4: 72 bytes (array grow)
FREE #3: 72 bytes (array grow)
ALLOC #7: 11 bytes (int.toString)
ALLOC #8: 11 bytes (int.toString)
ALLOC #9: 32 bytes (int.toString)
10ALLOC #10: 32 bytes (string metadata)
ALLOC #11: 10 bytes (cstring conversion)
ALLOC #12: 10 bytes (cstring conversion)
FREE #12: 10 bytes (cstring release)
FREE #11: 10 bytes (cstring release)

FREE #10: 32 bytes (string metadata)
FREE #8: 11 bytes (cstring release)
FREE #7: 11 bytes (cstring release)
FREE #6: 136 bytes (array cleanup)
FREE #5: 136 bytes (array cleanup)
FREE #9: 32 bytes (string metadata)

=== ALLOC STATS ===
Allocated: 602 bytes
Freed:     602 bytes
Leaked:    0 bytes
```
### Struct Field Array Method Call

<!-- test: struct-field-array-count -->
Calling `.count()` on an array field of a struct should work correctly.
```maxon
type Config
    var sources array of string
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
