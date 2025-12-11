---
feature: substring-type
status: experimental
keywords: [substring, string, slice, view, zero-copy]
category: types
---

# Substring Type

## Developer Notes

The `substring` type provides a zero-copy view into a string's buffer. Instead of copying data when slicing, a substring holds a reference to the parent string's storage.

### Memory Layout

**Substring Type (24 bytes):**
- `_parentManaged ptr` - pointer to parent's `__ManagedStringData`
- `_ptr ptr` - pointer into parent's buffer at slice start
- `_len i32` - byte length of substring
- `_iterPos i32` - iteration position (for `Iterable`)

### Reference Counting

When a substring is created from a heap-allocated string:
1. The parent's refcount is incremented (retained)
2. When the substring goes out of scope, the parent's refcount is decremented
3. The parent buffer is freed when refcount reaches 0

For SSO (small string optimization) strings, no refcount management is needed since the data is stored inline.

### Implementation

- Semantic analyzer: `semantic_analyzer_expr.cpp` - `__substring_*` intrinsics
- Codegen: `codegen_mir_expr.cpp` - `generateSubstringIntrinsic()`
- Scope cleanup: `codegen_mir.cpp` - `generateScopeCleanup()` handles substring cleanup
- stdlib: `stdlib/string/string.maxon` - `substring` struct and methods

## Documentation

The `substring` type is a lightweight view into a string's buffer. Substrings are created via the `slice()` method and provide zero-copy access to a portion of a string.

### Creating Substrings

```maxon
var s = "Hello, World!"
var sub = s.slice(0, 5)     // "Hello" - no copy!
var sub2 = s.slice(7, 12)   // "World" - no copy!
```

### Substring Methods

```maxon
var s = "Hello, World!"
var sub = s.slice(0, 5)
sub.count()     // 5 - byte length
sub.isEmpty()   // false
```

### Converting to String

Use `toString()` to create an independent copy:

```maxon
var s = "Hello, World!"
var sub = s.slice(0, 5)
var newStr = sub.toString()  // Creates a new string "Hello"
```

### Iteration

Substrings support iteration over Unicode codepoints:

```maxon
var s = "abc"
var sub = s.slice(0, 3)
for c in sub 'loop'
    print(c)  // Prints 97, 98, 99
end 'loop'
```

## Tests

<!-- test: basic-creation -->
```maxon
function main() returns int
    var s = "Hello, World!"
    var sub = s.slice(0, 5)
    return sub.count()
end 'main'
```
```exitcode
5
```

<!-- test: from-middle -->
```maxon
function main() returns int
    var s = "Hello, World!"
    var sub = s.slice(7, 12)
    return sub.count()
end 'main'
```
```exitcode
5
```

<!-- test: empty-substring -->
```maxon
function main() returns int
    var s = "Hello"
    var sub = s.slice(2, 2)
    return sub.count()
end 'main'
```
```exitcode
0
```

<!-- test: to-string -->
```maxon
function main() returns int
    var s = "Hello, World!"
    var sub = s.slice(0, 5)
    var str = sub.toString()
    return str.count()
end 'main'
```
```exitcode
5
```

<!-- test: iteration-count -->
```maxon
function main() returns int
    var s = "abcde"
    var sub = s.slice(1, 4)
    var sum = 0
    for c in sub 'loop'
        var cps = c.codepoints()
        if let cp = cps.next() 'get_cp'
            sum = sum + cp
        end 'get_cp'
    end 'loop'
    // 98 + 99 + 100 = 297 (b, c, d)
    return sum - 294
end 'main'
```
```exitcode
3
```

<!-- test: isEmpty-true -->
```maxon
function main() returns int
    var s = "Hello"
    var sub = s.slice(0, 0)
    if sub.isEmpty() 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: isEmpty-false -->
```maxon
function main() returns int
    var s = "Hello"
    var sub = s.slice(0, 3)
    if sub.isEmpty() 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
1
```

<!-- test: heap-string-retains-parent -->
```maxon
function main() returns int
    // Long string forces heap allocation
    var s = "This is a longer string that exceeds SSO"
    var refBefore = __string_get_refcount(s._managed)
    var sub = s.slice(0, 10)
    var refAfter = __string_get_refcount(s._managed)
    // Refcount should increase by 1, add sub.count() to use sub
    return refAfter - refBefore + sub.count() - sub.count()
end 'main'
```
```exitcode
1
```

<!-- test: sso-string-no-refcount -->
```maxon
function main() returns int
    // Short string uses SSO (no heap)
    var s = "Hello"
    var refcount = __string_get_refcount(s._managed)
    // SSO strings return -1 for refcount
    return refcount
end 'main'
```
```exitcode
-1
```

<!-- test: chained-slice -->
```maxon
function main() returns int
    var s = "Hello, World!"
    var sub1 = s.slice(0, 12)
    var sub2 = sub1.slice(0, 5)
    return sub2.count()
end 'main'
```
```exitcode
5
```
