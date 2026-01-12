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
MOVE: managed
ALLOC #1: 30 bytes (int.toString)
MOVE: managed
ALLOC #2: 12 bytes (string concat)
32
DECREF: <temp> -> rc=0
FREE #1: 30 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #2: 12 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 42 bytes
Freed:     42 bytes
Leaked:    0 bytes
Moves:     2
Increfs:   0
Decrefs:   2
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
MOVE: managed
MOVE: managed
ALLOC #1: 30 bytes (int.toString)
MOVE: managed
ALLOC #2: 12 bytes (string concat)
28
DECREF: <temp> -> rc=0
FREE #1: 30 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #2: 12 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 42 bytes
Freed:     42 bytes
Leaked:    0 bytes
Moves:     3
Increfs:   0
Decrefs:   2
```

### Substring Tests

<!-- test: substring-retains-parent -->
<!-- TrackMemory: true -->
Substrings should retain their parent string.
```maxon
function main() returns int
    var s = "hello world from heap!!"
    var start = s.startIndex()
    var spaceIdx = try s.find(" ") otherwise s.endIndex()
    var sub = s.slice(start, endIndex: spaceIdx)
    print("{sub.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
MOVE: managed
MOVE: managed
ALLOC #1: 30 bytes (int.toString)
MOVE: managed
ALLOC #2: 11 bytes (string concat)
5
DECREF: <temp> -> rc=0
FREE #1: 30 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #2: 11 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 41 bytes
Freed:     41 bytes
Leaked:    0 bytes
Moves:     3
Increfs:   0
Decrefs:   2
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
MOVE: managed
heap allocated string here!!
=== MEMORY STATS ===
Allocated: 0 bytes
Freed:     0 bytes
Leaked:    0 bytes
Moves:     1
Increfs:   0
Decrefs:   0
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
MOVE: managed
ALLOC #1: 30 bytes (int.toString)
MOVE: managed
ALLOC #2: 11 bytes (string concat)
5
DECREF: <temp> -> rc=0
FREE #1: 30 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #2: 11 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 41 bytes
Freed:     41 bytes
Leaked:    0 bytes
Moves:     2
Increfs:   0
Decrefs:   2
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
MOVE: managed
ALLOC #1: 10 bytes (string concat)
ALLOC #2: 11 bytes (string concat)
DECREF: s -> rc=0
FREE #1: 10 bytes (string cleanup)
ALLOC #3: 12 bytes (string concat)
DECREF: s -> rc=0
FREE #2: 11 bytes (string cleanup)
ALLOC #4: 13 bytes (string concat)
DECREF: s -> rc=0
FREE #3: 12 bytes (string cleanup)
ALLOC #5: 14 bytes (string concat)
DECREF: s -> rc=0
FREE #4: 13 bytes (string cleanup)
ALLOC #6: 30 bytes (int.toString)
MOVE: managed
ALLOC #7: 11 bytes (string concat)
5
DECREF: <temp> -> rc=0
FREE #6: 30 bytes (string cleanup)
DECREF: <temp> -> rc=0
FREE #7: 11 bytes (string cleanup)
DECREF: s -> rc=0
FREE #5: 14 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 101 bytes
Freed:     101 bytes
Leaked:    0 bytes
Moves:     3
Increfs:   0
Decrefs:   7
```

### String Literal Deduplication Tests

<!-- test: string-literal-deduplication -->
Identical string literals share the same storage in the .rdata section.
During code generation, duplicate string constants are deduplicated so they
point to the same memory address.
```maxon
function main() returns int
    var a = "hello world"
    var b = "hello world"
    print(a)
    print(b)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello worldhello world
```
