---
feature: managed-string-internals
category: internals
status: stable
---

## Developer Notes

This spec covers internal tests for the managed string system. These tests verify:
- SSO vs heap allocation thresholds
- Reference counting behavior
- Scope-based cleanup
- Memory tracking accuracy

The managed string system uses:
- 15-byte SSO threshold (strings <= 15 bytes stored inline)
- 8-byte header for heap strings (refcount + data_size)
- Automatic cleanup at scope exit

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
<!-- TrackAllocs: true -->
Heap strings should allocate and free properly.
```maxon
function main() int
    var s = "this is a heap allocated string!"
    printInt(s.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 41 bytes (string literal)
32ALLOC #2: 10 bytes (cstring conversion)
FREE #2: 10 bytes (cstring release)

FREE #1: 41 bytes (string literal)

=== ALLOC STATS ===
Allocated: 51 bytes
Freed:     51 bytes
Leaked:    0 bytes
```

<!-- test: heap-string-reassign -->
<!-- TrackAllocs: true -->
Reassigning a heap string should release the old value.
```maxon
function main() int
    var s = "first heap allocated value!!"
    s = "second heap allocated here!!"
    printInt(s.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 37 bytes (string literal)
ALLOC #2: 37 bytes (string literal)
FREE #1: 37 bytes (string reassign)
28ALLOC #3: 10 bytes (cstring conversion)
FREE #3: 10 bytes (cstring release)

FREE #2: 37 bytes (string literal)

=== ALLOC STATS ===
Allocated: 84 bytes
Freed:     84 bytes
Leaked:    0 bytes
```

### Substring Tests

<!-- test: substring-retains-parent -->
<!-- TrackAllocs: true -->
Substrings should retain their parent string.
```maxon
function main() int
    var s = "hello world from heap!!"
    var sub = s.slice(0, 5)
    printInt(sub.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 32 bytes (string literal)
5ALLOC #2: 10 bytes (cstring conversion)
FREE #2: 10 bytes (cstring release)

FREE #1: 32 bytes (substring parent)

=== ALLOC STATS ===
Allocated: 42 bytes
Freed:     42 bytes
Leaked:    0 bytes
```

### Cstring Conversion Tests

<!-- test: heap-to-cstring -->
<!-- TrackAllocs: true -->
Converting heap string to cstring retains the parent.
```maxon
function main() int
    var s = "heap allocated string here!!"
    print(s)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 37 bytes (string literal)
heap allocated string here!!ALLOC #2: 10 bytes (cstring conversion)
FREE #2: 10 bytes (cstring release)

FREE #1: 37 bytes (cstring release)

=== ALLOC STATS ===
Allocated: 47 bytes
Freed:     47 bytes
Leaked:    0 bytes
```

### Loop Concatenation Tests

<!-- test: loop-concat-accumulator -->
<!-- TrackAllocs: true -->
String accumulation in loop should release intermediate values.
```maxon
function main() int
    var s = ""
    var i = 0
    while i < 5 'loop'
        s = s + "a"
        i = i + 1
    end 'loop'
    printInt(s.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 10 bytes (string concat)
ALLOC #2: 11 bytes (string concat)
FREE #1: 10 bytes (string reassign)
ALLOC #3: 12 bytes (string concat)
FREE #2: 11 bytes (string reassign)
ALLOC #4: 13 bytes (string concat)
FREE #3: 12 bytes (string reassign)
ALLOC #5: 14 bytes (string concat)
FREE #4: 13 bytes (string reassign)
5ALLOC #6: 10 bytes (cstring conversion)
FREE #6: 10 bytes (cstring release)

FREE #5: 14 bytes (string concat)

=== ALLOC STATS ===
Allocated: 70 bytes
Freed:     70 bytes
Leaked:    0 bytes
```
