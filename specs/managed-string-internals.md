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
function main() returns int
    var s = "this is a heap allocated string!"
    print("{s.count()}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 41 bytes (string literal)
ALLOC #2: 11 bytes
32ALLOC #3: 10 bytes (cstring conversion)
FREE #3: 10 bytes (cstring release)

FREE #2: 11 bytes (cstring release)
FREE #1: 41 bytes (string literal)

=== ALLOC STATS ===
Allocated: 62 bytes
Freed:     62 bytes
Leaked:    0 bytes
```

<!-- test: heap-string-reassign -->
<!-- TrackAllocs: true -->
Reassigning a heap string should release the old value.
```maxon
function main() returns int
    var s = "first heap allocated value!!"
    s = "second heap allocated here!!"
    print("{s.count()}")
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
ALLOC #3: 11 bytes
28ALLOC #4: 10 bytes (cstring conversion)
FREE #4: 10 bytes (cstring release)

FREE #3: 11 bytes (cstring release)
FREE #2: 37 bytes (string literal)

=== ALLOC STATS ===
Allocated: 95 bytes
Freed:     95 bytes
Leaked:    0 bytes
```

### Substring Tests

<!-- test: substring-retains-parent -->
<!-- TrackAllocs: true -->
Substrings should retain their parent string.
```maxon
function main() returns int
    var s = "hello world from heap!!"
    var sub = s.slice(s.startIndex(), s.indexAdvanced(s.startIndex(), 5))
    print("{sub.count()}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 32 bytes (string literal)
ALLOC #2: 10 bytes
5ALLOC #3: 10 bytes (cstring conversion)
FREE #3: 10 bytes (cstring release)

FREE #2: 10 bytes (cstring release)
FREE #1: 32 bytes (string literal)

=== ALLOC STATS ===
Allocated: 52 bytes
Freed:     52 bytes
Leaked:    0 bytes
```

### Cstring Conversion Tests

<!-- test: heap-to-cstring -->
<!-- TrackAllocs: true -->
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

<!-- test: loop-interp-accumulator -->
<!-- TrackAllocs: true -->
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
    print("{s.count()}")
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
ALLOC #6: 10 bytes
5ALLOC #7: 10 bytes (cstring conversion)
FREE #7: 10 bytes (cstring release)

FREE #6: 10 bytes (cstring release)
FREE #5: 14 bytes (string literal)

=== ALLOC STATS ===
Allocated: 80 bytes
Freed:     80 bytes
Leaked:    0 bytes
```
