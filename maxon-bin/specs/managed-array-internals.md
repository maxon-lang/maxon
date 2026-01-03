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
<!-- TrackAllocs: true -->
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
ALLOC #2: 22 bytes (int.toString)
ALLOC #3: 2 bytes (string buffer)
ALLOC #4: 3 bytes (string concat)
3
FREE #2: 22 bytes (string cleanup)
FREE #3: 2 bytes (string cleanup)
FREE #4: 3 bytes (string cleanup)
FREE #1: 32 bytes (array cleanup)

=== ALLOC STATS ===
Allocated: 59 bytes
Freed:     59 bytes
Leaked:    0 bytes
```

<!-- test: heap-array-scope-cleanup -->
<!-- TrackAllocs: true -->
Heap arrays in inner scopes should be cleaned up on scope exit.
```maxon
function main() returns int
    if true 'outer'
        var outer_arr = Array of int{}
        outer_arr.push(100)
        if true 'inner'
            var inner_arr = Array of int{}
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
ALLOC #1: 32 bytes (array grow)
ALLOC #2: 32 bytes (array grow)
ALLOC #3: 22 bytes (int.toString)
ALLOC #4: 2 bytes (string buffer)
ALLOC #5: 5 bytes (string concat)
200
FREE #3: 22 bytes (string cleanup)
FREE #4: 2 bytes (string cleanup)
FREE #5: 5 bytes (string cleanup)
ALLOC #6: 22 bytes (int.toString)
ALLOC #7: 2 bytes (string buffer)
ALLOC #8: 5 bytes (string concat)
100
FREE #6: 22 bytes (string cleanup)
FREE #7: 2 bytes (string cleanup)
FREE #8: 5 bytes (string cleanup)
FREE #1: 32 bytes (array cleanup)
FREE #2: 32 bytes (array cleanup)

=== ALLOC STATS ===
Allocated: 122 bytes
Freed:     122 bytes
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
ALLOC #1: 24 bytes (set buffer)
ALLOC #2: 22 bytes (int.toString)
ALLOC #3: 2 bytes (string buffer)
ALLOC #4: 4 bytes (string concat)
20
FREE #2: 22 bytes (string cleanup)
FREE #3: 2 bytes (string cleanup)
FREE #4: 4 bytes (string cleanup)
FREE #1: 24 bytes (array cleanup)

=== ALLOC STATS ===
Allocated: 52 bytes
Freed:     52 bytes
Leaked:    0 bytes
```

### Loop Growth Tests

<!-- test: loop-array-growth -->
<!-- TrackAllocs: true -->
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
ALLOC #2: 22 bytes (int.toString)
ALLOC #3: 2 bytes (string buffer)
ALLOC #4: 4 bytes (string concat)
10
FREE #2: 22 bytes (string cleanup)
FREE #3: 2 bytes (string cleanup)
FREE #4: 4 bytes (string cleanup)
FREE #1: 128 bytes (array cleanup)

=== ALLOC STATS ===
Allocated: 252 bytes
Freed:     252 bytes
Leaked:    0 bytes
```
### Struct Field Array Method Call

<!-- test: struct-field-array-count -->
Calling `.count()` on an array field of a struct should work correctly.
```maxon
type Config
    var sources Array of String
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
