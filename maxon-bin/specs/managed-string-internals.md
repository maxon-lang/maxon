---
feature: managed-string-internals
category: internals
status: stable
---
# Managed String Internals

## Documentation

### String Memory Management

Maxon manages string memory automatically using reference counting and scope-based cleanup.

### Small String Optimization (SSO)

Strings up to 15 bytes are stored inline without heap allocation:

```maxon
var short = "hello"  // SSO (5 bytes)
var long = "this is a longer string"  // Heap allocated (24 bytes)
```

### Reference Counting

Heap-allocated strings use reference counting to enable efficient sharing:

```maxon
var a = "heap allocated string here!"
var b = a  // b shares a's buffer, refcount = 2
```

### Automatic Cleanup

Strings are automatically released when they go out of scope:

```maxon
if true 'scope'
    var temp = "temporary string data"
    // temp is released here
end 'scope'
```

## Tests

### Heap Allocation Tests

<!-- test: heap-string-alloc -->
<!-- TrackMemory: true -->
Heap strings should allocate and free properly.
```maxon
function main() returns int
    var s = "this is a heap allocated string!"
    print("{s.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 33 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #2: 22 bytes (int.toString)
ALLOC #3: 2 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #4: 4 bytes (string concat)
32
DECREF: <temp> -> rc=0
FREE #2: 22 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #3: 2 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #4: 4 bytes (string cleanup)
DECREF: s -> rc=0
FREE #1: 33 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 61 bytes
Freed:     61 bytes
Leaked:    0 bytes
Moves:     2
Increfs:   2
Decrefs:   4
```

<!-- test: heap-string-reassign -->
<!-- TrackMemory: true -->
Reassigning a heap string should release the old value.
```maxon
function main() returns int
    var s = "first heap allocated value!!"
    s = "second heap allocated here!!"
    print("{s.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 29 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #2: 29 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
DECREF: s -> rc=0
FREE #1: 29 bytes (string cleanup)
ALLOC #3: 22 bytes (int.toString)
ALLOC #4: 2 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #5: 4 bytes (string concat)
28
DECREF: <temp> -> rc=0
FREE #3: 22 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #4: 2 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #5: 4 bytes (string cleanup)
DECREF: s -> rc=0
FREE #2: 29 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 86 bytes
Freed:     86 bytes
Leaked:    0 bytes
Moves:     3
Increfs:   3
Decrefs:   5
```

### Substring Tests

<!-- test: substring-retains-parent -->
<!-- TrackMemory: true -->
Substrings should retain their parent string.
```maxon
function main() returns int
    var s = "hello world from heap!!"
    var sub = s.slice(s.startIndex(), length = 5)
    print("{sub.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 24 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #2: 22 bytes (int.toString)
ALLOC #3: 2 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #4: 3 bytes (string concat)
5
DECREF: <temp> -> rc=0
FREE #2: 22 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #3: 2 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #4: 3 bytes (string cleanup)
DECREF: s -> rc=0
FREE #1: 24 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 51 bytes
Freed:     51 bytes
Leaked:    0 bytes
Moves:     2
Increfs:   2
Decrefs:   4
```

### Cstring Conversion Tests

<!-- test: heap-to-cstring -->
<!-- TrackMemory: true -->
Converting heap string to cstring retains the parent.
```maxon
function main() returns int
    var s = "heap allocated string here!!"
    print(s)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 29 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
heap allocated string here!!DECREF: s -> rc=0
FREE #1: 29 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 29 bytes
Freed:     29 bytes
Leaked:    0 bytes
Moves:     1
Increfs:   1
Decrefs:   1
```

### Metadata Cleanup Tests

<!-- test: string-metadata-freed -->
<!-- TrackMemory: true -->
The __ManagedStringData metadata struct must be freed when string goes out of scope.
This test verifies there are no leaks from the metadata allocation.
```maxon
function main() returns int
    var s = "short"
    print("{s.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 6 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #2: 22 bytes (int.toString)
ALLOC #3: 2 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #4: 3 bytes (string concat)
5
DECREF: <temp> -> rc=0
FREE #2: 22 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #3: 2 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #4: 3 bytes (string cleanup)
DECREF: s -> rc=0
FREE #1: 6 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 33 bytes
Freed:     33 bytes
Leaked:    0 bytes
Moves:     2
Increfs:   2
Decrefs:   4
```

### Loop Concatenation Tests

<!-- test: loop-interp-accumulator -->
<!-- TrackMemory: true -->
String accumulation in loop releases intermediate values on reassignment.
The final value is properly released at scope exit.
```maxon
function main() returns int
    var s = ""
    var a = "a"
    var i = 0
    while i < 5 'loop'
        s = "{s}{a}"
        i = i + 1
    end 'loop'
    print("{s.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
MOVE: managed
ALLOC #1: 2 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #2: 2 bytes (string concat)
ALLOC #3: 3 bytes (string concat)
DECREF: s -> rc=0
FREE #2: 2 bytes (string cleanup)
ALLOC #4: 4 bytes (string concat)
DECREF: s -> rc=0
FREE #3: 3 bytes (string cleanup)
ALLOC #5: 5 bytes (string concat)
DECREF: s -> rc=0
FREE #4: 4 bytes (string cleanup)
ALLOC #6: 6 bytes (string concat)
DECREF: s -> rc=0
FREE #5: 5 bytes (string cleanup)
ALLOC #7: 22 bytes (int.toString)
ALLOC #8: 2 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #9: 3 bytes (string concat)
5
DECREF: <temp> -> rc=0
FREE #7: 22 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #8: 2 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #9: 3 bytes (string cleanup)
DECREF: s -> rc=0
FREE #6: 6 bytes (string cleanup)
DECREF: a -> rc=0
FREE #1: 2 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 49 bytes
Freed:     49 bytes
Leaked:    0 bytes
Moves:     3
Increfs:   2
Decrefs:   9
```
