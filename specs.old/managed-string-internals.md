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
ALLOC #2: 41 bytes (string literal)
ALLOC #3: 32 bytes (string metadata)
ALLOC #4: 11 bytes (int.toString)
ALLOC #5: 11 bytes (int.toString)
ALLOC #6: 32 bytes (int.toString)
32ALLOC #7: 32 bytes (string metadata)
ALLOC #8: 10 bytes (cstring conversion)
ALLOC #9: 10 bytes (cstring conversion)
FREE #9: 10 bytes (cstring release)
FREE #8: 10 bytes (cstring release)

FREE #7: 32 bytes (string metadata)
FREE #5: 11 bytes (cstring release)
FREE #4: 11 bytes (cstring release)
FREE #2: 41 bytes (string literal)
FREE #1: 41 bytes (string literal)
FREE #6: 32 bytes (string metadata)
FREE #3: 32 bytes (string metadata)

=== ALLOC STATS ===
Allocated: 220 bytes
Freed:     220 bytes
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
ALLOC #3: 32 bytes (string metadata)
ALLOC #4: 37 bytes (string literal)
ALLOC #5: 37 bytes (string literal)
ALLOC #6: 32 bytes (string metadata)
FREE #2: 37 bytes (string reassign)
FREE #1: 37 bytes (string reassign)
FREE #3: 32 bytes (string reassign meta)
ALLOC #7: 11 bytes (int.toString)
ALLOC #8: 11 bytes (int.toString)
ALLOC #9: 32 bytes (int.toString)
28ALLOC #10: 32 bytes (string metadata)
ALLOC #11: 10 bytes (cstring conversion)
ALLOC #12: 10 bytes (cstring conversion)
FREE #12: 10 bytes (cstring release)
FREE #11: 10 bytes (cstring release)

FREE #10: 32 bytes (string metadata)
FREE #8: 11 bytes (cstring release)
FREE #7: 11 bytes (cstring release)
FREE #5: 37 bytes (string literal)
FREE #4: 37 bytes (string literal)
FREE #9: 32 bytes (string metadata)
FREE #6: 32 bytes (string metadata)

=== ALLOC STATS ===
Allocated: 318 bytes
Freed:     318 bytes
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
ALLOC #2: 32 bytes (string literal)
ALLOC #3: 32 bytes (string metadata)
ALLOC #4: 10 bytes (int.toString)
ALLOC #5: 10 bytes (int.toString)
ALLOC #6: 32 bytes (int.toString)
5ALLOC #7: 32 bytes (string metadata)
ALLOC #8: 10 bytes (cstring conversion)
ALLOC #9: 10 bytes (cstring conversion)
FREE #9: 10 bytes (cstring release)
FREE #8: 10 bytes (cstring release)

FREE #7: 32 bytes (string metadata)
FREE #5: 10 bytes (cstring release)
FREE #4: 10 bytes (cstring release)
FREE #6: 32 bytes (string metadata)
FREE #2: 32 bytes (string literal)
FREE #1: 32 bytes (string literal)
FREE #3: 32 bytes (string metadata)

=== ALLOC STATS ===
Allocated: 200 bytes
Freed:     200 bytes
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
ALLOC #2: 37 bytes (string literal)
ALLOC #3: 32 bytes (string metadata)
heap allocated string here!!ALLOC #4: 32 bytes (string metadata)
ALLOC #5: 10 bytes (cstring conversion)
ALLOC #6: 10 bytes (cstring conversion)
FREE #6: 10 bytes (cstring release)
FREE #5: 10 bytes (cstring release)

FREE #4: 32 bytes (string metadata)
FREE #2: 37 bytes (cstring release)
FREE #1: 37 bytes (cstring release)
FREE #3: 32 bytes (string metadata)

=== ALLOC STATS ===
Allocated: 158 bytes
Freed:     158 bytes
Leaked:    0 bytes
```

### Metadata Cleanup Tests

<!-- test: string-metadata-freed -->
<!-- TrackAllocs: true -->
The __ManagedStringData metadata struct must be freed when string goes out of scope.
This test verifies there are no leaks from the metadata allocation.
```maxon
function main() returns int
    var s = "short"
    print("{s.count()}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 32 bytes (string metadata)
ALLOC #2: 10 bytes (int.toString)
ALLOC #3: 10 bytes (int.toString)
ALLOC #4: 32 bytes (int.toString)
5ALLOC #5: 32 bytes (string metadata)
ALLOC #6: 10 bytes (cstring conversion)
ALLOC #7: 10 bytes (cstring conversion)
FREE #7: 10 bytes (cstring release)
FREE #6: 10 bytes (cstring release)

FREE #5: 32 bytes (string metadata)
FREE #3: 10 bytes (cstring release)
FREE #2: 10 bytes (cstring release)
FREE #4: 32 bytes (string metadata)
FREE #1: 32 bytes (string metadata)

=== ALLOC STATS ===
Allocated: 136 bytes
Freed:     136 bytes
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
ALLOC #1: 32 bytes (string metadata)
ALLOC #2: 32 bytes (string metadata)
ALLOC #3: 10 bytes (string concat)
ALLOC #4: 10 bytes (string concat)
ALLOC #5: 32 bytes (string concat)
FREE #1: 32 bytes (string reassign meta)
ALLOC #6: 11 bytes (string concat)
ALLOC #7: 11 bytes (string concat)
ALLOC #8: 32 bytes (string concat)
FREE #4: 10 bytes (string reassign)
FREE #3: 10 bytes (string reassign)
FREE #5: 32 bytes (string reassign meta)
ALLOC #9: 12 bytes (string concat)
ALLOC #10: 12 bytes (string concat)
ALLOC #11: 32 bytes (string concat)
FREE #7: 11 bytes (string reassign)
FREE #6: 11 bytes (string reassign)
FREE #8: 32 bytes (string reassign meta)
ALLOC #12: 13 bytes (string concat)
ALLOC #13: 13 bytes (string concat)
ALLOC #14: 32 bytes (string concat)
FREE #10: 12 bytes (string reassign)
FREE #9: 12 bytes (string reassign)
FREE #11: 32 bytes (string reassign meta)
ALLOC #15: 14 bytes (string concat)
ALLOC #16: 14 bytes (string concat)
ALLOC #17: 32 bytes (string concat)
FREE #13: 13 bytes (string reassign)
FREE #12: 13 bytes (string reassign)
FREE #14: 32 bytes (string reassign meta)
ALLOC #18: 10 bytes (int.toString)
ALLOC #19: 10 bytes (int.toString)
ALLOC #20: 32 bytes (int.toString)
5ALLOC #21: 32 bytes (string metadata)
ALLOC #22: 10 bytes (cstring conversion)
ALLOC #23: 10 bytes (cstring conversion)
FREE #23: 10 bytes (cstring release)
FREE #22: 10 bytes (cstring release)

FREE #21: 32 bytes (string metadata)
FREE #19: 10 bytes (cstring release)
FREE #18: 10 bytes (cstring release)
FREE #20: 32 bytes (string metadata)
FREE #16: 14 bytes (string literal)
FREE #15: 14 bytes (string literal)
FREE #17: 32 bytes (string metadata)
FREE #2: 32 bytes (string metadata)

=== ALLOC STATS ===
Allocated: 448 bytes
Freed:     448 bytes
Leaked:    0 bytes
```
