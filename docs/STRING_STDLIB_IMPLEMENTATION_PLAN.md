# String Implementation Plan: Swift-Style Interfaces in stdlib

## Overview

Implement strings **entirely in stdlib** using interfaces (protocols) similar to Swift's design, enabling:
- Character-by-character iteration via `for c in s`
- String slicing that returns immutable `string` (not a separate `substring` type)
- Interface-based polymorphism for string operations
- Full conformance to existing Maxon interfaces (`Hashable`, `Equatable`, `Printable`, `Iterable`, `RangeSubscriptable`)

**Key Principle**: No string functions in runtime. All string logic implemented in pure Maxon in stdlib.

## Current State

### What Exists

1. **Runtime functions** (`maxon-runtime/runtime.mir`) - Low-level primitives only:
   - Memory: `malloc`, `free`, `memset`, `memcpy`
   - I/O: `write_stdout`
   - Math functions
   - **No string-specific functions** (to be removed if any exist)

2. **Interfaces** (`stdlib/interfaces.maxon`):
   - `Hashable` - `hash(self Self) int`
   - `Equatable` - `equals(self Self, other Self) bool`
   - `Comparable` - `compare(self Self, other Self) int`
   - `Cloneable` - `clone(self Self) Self`
   - `Iterable` - `hasNext(self Self) int`, `getCurrent(self Self) int`, `next(self Self) Self`
   - `Printable` - `print(self Self) int`
   - `Stringable` - `toString(self Self) string`
   - `RangeSubscriptable` - `subscript(self Self, startIndex int, endIndex int) Self`
   - **Missing**: `ExpressibleByStringLiteral` for string literal initialization

3. **Spec file** (`specs/string-type.md`):
   - Defines memory layout (SSO/COW)
   - Documents all string operations
   - Contains test cases

4. **Current stdlib string** (`stdlib/string/string.maxon`):
   - Simplified placeholder implementation
   - Fixed 16-byte inline storage only (no heap)
   - Pure Maxon implementation (good foundation)
   - Missing `Iterable` conformance
   - Missing heap allocation for large strings

### What's Missing

1. **Large string support** - Heap allocation for strings >15 bytes
2. **Iterable conformance** - `string` implementing `hasNext`, `getCurrent`, `next`
3. **UTF-8 decoding** in pure Maxon for codepoint iteration
4. **COW semantics** for large strings (reference counting in Maxon)
5. **Compiler integration** - codegen needs to route string ops through stdlib

## Design Decisions

### 1. No Separate `substring` Type

**Decision**: String slicing returns `string`, not `substring`.

**Rationale**:
- Simplifies the type system (one string type)
- Sliced strings are immutable copies (via COW, so cheap for shared data)
- Follows the spec's "immutable view" semantics without a new type

### 2. Multiple String Views (like Swift)

**Decision**: Provide multiple views of string data via properties.

**Views**:
- **Default (`for c in s`)**: Iterates over Unicode scalars (`char` - Unicode codepoints)
- **`.bytes`**: Iterator over raw bytes (`byte` - UTF-8 code units)
- **`.utf16`**: Iterator over UTF-16 code units (for interop)

**Rationale**:
- Different use cases need different granularity
- Default iteration over Unicode scalars is most useful for text processing
- Raw byte access needed for binary protocols, file I/O
- UTF-16 needed for Windows API interop, JavaScript interop

**Implementation**:
```maxon
// Default iteration: Unicode scalars (char)
for c in s 'chars'
    // c is char (Unicode codepoint)
end 'chars'

// Byte iteration
for b in s.bytes 'bytes'
    // b is byte (raw UTF-8 code unit)
end 'bytes'

// UTF-16 iteration
for u in s.utf16 'utf16'
    // u is int (UTF-16 code unit, 16-bit)
end 'utf16'
```

**View structs**:
```maxon
// ByteView - iterates over raw bytes
struct ByteView is Iterable
    _str string
    _pos int
end 'ByteView'

export function string.bytes(self string) ByteView
    return ByteView{_str: self, _pos: 0}
end 'bytes'

// UTF16View - iterates over UTF-16 code units
struct UTF16View is Iterable
    _str string
    _bytePos int
end 'UTF16View'

export function string.utf16(self string) UTF16View
    return UTF16View{_str: self, _bytePos: 0}
end 'utf16'
```

**Note**: Swift also has grapheme clusters (user-perceived characters like emoji with modifiers). This is complex and can be deferred - start with Unicode scalars as the default.

### 3. Character Iteration Returns `char`

**Decision**: `for c in s` iterates over Unicode scalars as `char`.

**Rationale**:
- UTF-8 aware iteration is more useful than raw byte iteration
- `char` type represents a Unicode scalar value (codepoint)
- Raw byte iteration available via `s.bytes`
- UTF-16 iteration available via `s.utf16`
- Aligns with Swift's `.unicodeScalars` view (though Swift defaults to grapheme clusters)

**Implementation**: `string` struct includes a `_bytePos` field for iteration state, implementing `hasNext`, `getCurrent`, and `next` directly.

### 4. Pure Maxon Implementation

**Decision**: All string logic implemented in Maxon, not runtime.

**Rationale**:
- Keeps runtime minimal (only OS-level primitives)
- String logic is readable and debuggable
- Enables future stdlib improvements without rebuilding runtime
- Uses `malloc`/`free`/`memcpy` from runtime for memory only

### 5. String Implements Iterable Directly

**Challenge**: Current `Iterable.getCurrent()` returns `int`, but strings need `char`.

**Options**:
1. **Separate StringIterator type** - Extra struct, more complexity
2. **String implements Iterable directly** - String tracks its own iteration state
3. **Return int, cast to char** - Use existing interface, `char` fits in `int`

**Decision**: Option 2+3 - `string` implements `Iterable` directly, returning `int` (codepoint) from `getCurrent()`. The compiler casts to `char` for the loop variable. This mirrors how `Iterator` works for `range()` - no separate iterator type needed.

### 6. ExpressibleByStringLiteral Interface

**Decision**: Add `ExpressibleByStringLiteral` interface (like Swift's protocol).

**Rationale**:
- Compiler uses this interface to convert string literals to types
- Any type can conform to receive string literal initialization
- Keeps literal handling in stdlib, not hardcoded in compiler
- Enables custom string types (e.g., `Path`, `URL`, `Regex`) to accept literals

**Interface definition**:
```maxon
// ExpressibleByStringLiteral - types that can be initialized from string literals
// The compiler calls this when a string literal is assigned to a conforming type
export interface ExpressibleByStringLiteral
    // Initialize from raw UTF-8 bytes
    // data: pointer to UTF-8 byte data (null-terminated)
    // length: number of bytes (not including null terminator)
    function initFromStringLiteral(data ptr, length int) Self
end 'ExpressibleByStringLiteral'
```

**Compiler behavior**:
1. When encountering a string literal assigned to type `T`
2. Check if `T` conforms to `ExpressibleByStringLiteral`
3. Emit global constant for UTF-8 data
4. Call `T.initFromStringLiteral(dataPtr, length)` to create the value

**Example usage**:
```maxon
// string conforms to ExpressibleByStringLiteral
var s = "hello"  // Compiler calls string.initFromStringLiteral(ptr, 5)

// Custom types can also conform
struct Path is ExpressibleByStringLiteral
    _data string
end 'Path'

function Path.initFromStringLiteral(data ptr, length int) Path
    // Create Path from raw bytes
end 'initFromStringLiteral'

var p = "/usr/bin" as Path  // Or just: var p Path = "/usr/bin"
```

## Implementation Plan

### Phase 1: Core String Implementation in stdlib

**File**: `stdlib/string/string.maxon`

Implement full string type with SSO, heap allocation, and Iterable conformance:

```maxon
// String struct - 16 bytes on 64-bit + iteration state
//
// **Memory Layout**:
// Small String (SSO - capacity ≤15 bytes):
//   [byte 0-14: UTF-8 data]
//   [byte 15: 0xxxxxxx (MSB=0 indicates small, lower 7 bits = remaining capacity)]
//
// Large String (heap-allocated):
//   [bytes 0-7: pointer to heap buffer (tagged)]
//   [bytes 8-11: count (length in bytes)]
//   [bytes 12-15: capacity and flags]
//
// **Characteristics**:
// - UTF-8 encoded
// - Small strings (≤15 bytes): stored inline, no allocation
// - Large strings: heap-allocated with COW semantics
// - Automatically switches between small and large representation
// - Reference counting for shared storage
// - Null-terminated for C interop (capacity includes null byte)
//
// For iteration, we track current byte position in _iterPos.

export struct string is Hashable, Equatable, Printable, RangeSubscriptable, Iterable, ExpressibleByStringLiteral
    _word0 int      // First 8 bytes (or pointer for large)
    _word1 int      // Second 8 bytes (or count/capacity for large)
    _iterPos int    // Current byte position for iteration (0 = start)
end 'string'

// ============================================
// ExpressibleByStringLiteral conformance
// ============================================

// Initialize string from raw UTF-8 literal data
export function string.initFromStringLiteral(data ptr, length int) string
    if length <= 15 'small'
        // Create small string: copy bytes inline
        var result = string{_word0: 0, _word1: 0, _iterPos: 0}
        // Copy bytes from data to result._word0 (bytes 0-7) and result._word1 (bytes 8-15)
        var i = 0
        while i < length 'copy'
            // Copy byte i from data to appropriate position in result
            // (Implementation uses pointer arithmetic on &result)
            i = i + 1
        end 'copy'
        // Set byte 15: format is 0xxxxxxx where lower 7 bits = remaining capacity (15 - length)
        // Null-terminate at position length
        var remainingCapacity = 15 - length
        // Set byte 15 in word1 (shift left 56 bits)
        result._word1 = result._word1 + (remainingCapacity * 72057594037927936)
        return result
    else 'small'
        // Create large string: allocate heap buffer
        // Buffer layout: [4 bytes refcount][data...][null terminator]
        var bufSize = 4 + length + 1  // refcount + data + null
        var buf = malloc(bufSize)
        // Set refcount = 1 at buf[0..3]
        var refcountPtr = buf as ptr
        // *refcountPtr = 1 (store 32-bit refcount)
        // Copy data to buf+4
        memcpy((buf as int + 4) as ptr, data, length)
        // Null terminate
        // Set byte at buf+4+length to 0
        
        // Build large string struct:
        // word0 = pointer to data (buf + 4)
        // word1 low 32 bits = count (length)
        // word1 bytes 12-15 = capacity and flags (MSB=1 to indicate large)
        var dataPtr = (buf as int + 4) as int
        var capacity = length + 1  // includes null terminator
        var flags = 128 * 72057594037927936  // MSB=1 in byte 15 (indicates large string)
        var word1 = length + (capacity * 4294967296) + flags
        
        return string{_word0: dataPtr, _word1: word1, _iterPos: 0}
    end 'small'
end 'initFromStringLiteral'

// Helper: check if string is small (inline storage)
// Small string has MSB of byte 15 = 0
// Large string has MSB of byte 15 = 1
function string._isSmall(self string) bool
    // Byte 15 is the high byte of word1 (bytes 8-15)
    // Extract byte 15 (most significant byte of word1)
    var byte15 = (self._word1 / 72057594037927936) mod 256  // >> 56
    // MSB=0 means small, MSB=1 means large
    return byte15 < 128
end '_isSmall'

// Helper: get byte count
export function string.count(self string) int
    if string._isSmall(self) 'check'
        // Small: byte 15 has format 0xxxxxxx where lower 7 bits = remaining capacity
        // length = 15 - remaining_capacity
        var byte15 = (self._word1 / 72057594037927936) mod 256  // >> 56
        var remainingCapacity = byte15 mod 128  // mask off MSB, get lower 7 bits
        return 15 - remainingCapacity
    else 'check'
        // Large: count is in bytes 8-11 (lower 32 bits of word1)
        return self._word1 mod 4294967296
    end 'check'
end 'count'

// Helper: get data pointer
function string._data(self string) ptr
    if string._isSmall(self) 'check'
        // Small: data is inline, return address of self (bytes 0-14)
        return &self as ptr
    else 'check'
        // Large: word0 is the tagged pointer to heap buffer
        // The pointer points to the start of string data (after refcount header)
        return self._word0 as ptr
    end 'check'
end '_data'

// Get byte at index
function string._byteAt(self string, index int) byte
    var data = string._data(self)
    // Pointer arithmetic to get byte at offset
    var bytePtr = (data as int + index) as ptr
    return *bytePtr as byte
end '_byteAt'

// ============================================
// Iterable conformance - character iteration
// ============================================

// Check if more characters available
export function string.hasNext(self string) int
    var len = string.count(self)
    if self._iterPos < len 'check'
        return 1
    end 'check'
    return 0
end 'hasNext'

// Get current character (codepoint as int)
export function string.getCurrent(self string) int
    var b0 = string._byteAt(self, self._iterPos) as int
    
    // ASCII (1 byte)
    if b0 < 128 'ascii'
        return b0
    end 'ascii'
    
    // 2-byte sequence (110xxxxx 10xxxxxx)
    if b0 < 224 'two_byte'
        var b1 = string._byteAt(self, self._iterPos + 1) as int
        var cp = ((b0 mod 32) * 64) + (b1 mod 64)
        return cp
    end 'two_byte'
    
    // 3-byte sequence (1110xxxx 10xxxxxx 10xxxxxx)
    if b0 < 240 'three_byte'
        var b1 = string._byteAt(self, self._iterPos + 1) as int
        var b2 = string._byteAt(self, self._iterPos + 2) as int
        var cp = ((b0 mod 16) * 4096) + ((b1 mod 64) * 64) + (b2 mod 64)
        return cp
    end 'three_byte'
    
    // 4-byte sequence (11110xxx 10xxxxxx 10xxxxxx 10xxxxxx)
    var b1 = string._byteAt(self, self._iterPos + 1) as int
    var b2 = string._byteAt(self, self._iterPos + 2) as int
    var b3 = string._byteAt(self, self._iterPos + 3) as int
    var cp = ((b0 mod 8) * 262144) + ((b1 mod 64) * 4096) + ((b2 mod 64) * 64) + (b3 mod 64)
    return cp
end 'getCurrent'

// Advance to next character
export function string.next(self string) string
    var b0 = string._byteAt(self, self._iterPos) as int
    var advance = 1
    
    if b0 >= 240 'four_byte'
        advance = 4
    else 'four_byte'
        if b0 >= 224 'three_byte'
            advance = 3
        else 'three_byte'
            if b0 >= 192 'two_byte'
                advance = 2
            end 'two_byte'
        end 'three_byte'
    end 'four_byte'
    
    // Return copy of string with updated position
    return string{_word0: self._word0, _word1: self._word1, _iterPos: self._iterPos + advance}
end 'next'

// ============================================
// Other conformances
// ============================================

// Printable conformance - print string to stdout
export function string.print(self string) int
    var data = string._data(self)
    var len = string.count(self)
    // Call runtime write_stdout
    return write_stdout(data, len)
end 'print'

// Hashable conformance
export function string.hash(self string) int
    var h = 5381
    var len = string.count(self)
    var i = 0
    while i < len 'hash_loop'
        var b = string._byteAt(self, i)
        h = h * 33 + (b as int)
        i = i + 1
    end 'hash_loop'
    return h
end 'hash'

// Equatable conformance
export function string.equals(self string, other string) bool
    var len = string.count(self)
    var otherLen = string.count(other)
    if len != otherLen 'length_check'
        return false
    end 'length_check'
    
    var i = 0
    while i < len 'compare_loop'
        if string._byteAt(self, i) != string._byteAt(other, i) 'byte_check'
            return false
        end 'byte_check'
        i = i + 1
    end 'compare_loop'
    return true
end 'equals'

// Concatenation
export function string.concat(self string, other string) string
    var selfLen = string.count(self)
    var otherLen = string.count(other)
    var totalLen = selfLen + otherLen
    
    if totalLen <= 15 'fits_sso'
        // Create small string
        var result = string{_word0: 0, _word1: 0, _iterPos: 0}
        // Copy bytes inline...
        // (Implementation details)
        return result
    else 'fits_sso'
        // Allocate heap buffer
        var bufSize = 8 + totalLen + 1  // header + data + null
        var buf = malloc(bufSize)
        // Set refcount = 1 at buf[0..3]
        // Set capacity at buf[4..7]
        // Copy data to buf[8..]
        // Return large string struct
        // (Implementation details)
    end 'fits_sso'
end 'concat'

// ... Additional methods: startsWith, endsWith, contains, find, etc.
// All implemented in pure Maxon using _data() and _byteAt()

// ============================================
// String Views (like Swift)
// ============================================

// ByteView - iterate over raw UTF-8 bytes
struct ByteView is Iterable
    _str string
    _pos int
end 'ByteView'

export function string.bytes(self string) ByteView
    return ByteView{_str: self, _pos: 0}
end 'bytes'

export function ByteView.hasNext(self ByteView) int
    if self._pos < string.count(self._str) then return 1
    return 0
end 'hasNext'

export function ByteView.getCurrent(self ByteView) int
    return string._byteAt(self._str, self._pos) as int
end 'getCurrent'

export function ByteView.next(self ByteView) ByteView
    return ByteView{_str: self._str, _pos: self._pos + 1}
end 'next'

// UTF16View - iterate over UTF-16 code units
struct UTF16View is Iterable
    _str string
    _bytePos int
    _pendingSurrogate int  // For codepoints > 0xFFFF (surrogate pairs)
end 'UTF16View'

export function string.utf16(self string) UTF16View
    return UTF16View{_str: self, _bytePos: 0, _pendingSurrogate: 0}
end 'utf16'

export function UTF16View.hasNext(self UTF16View) int
    if self._pendingSurrogate != 0 then return 1
    if self._bytePos < string.count(self._str) then return 1
    return 0
end 'hasNext'

export function UTF16View.getCurrent(self UTF16View) int
    // If we have a pending low surrogate, return it
    if self._pendingSurrogate != 0 'pending'
        return self._pendingSurrogate
    end 'pending'
    
    // Decode UTF-8 codepoint
    var cp = // ... decode codepoint at _bytePos
    
    if cp <= 65535 'bmp'
        // Basic Multilingual Plane - single UTF-16 unit
        return cp
    else 'bmp'
        // Supplementary plane - return high surrogate
        // (low surrogate stored in _pendingSurrogate for next call)
        var adjusted = cp - 65536
        var highSurrogate = 55296 + (adjusted / 1024)  // 0xD800 + high 10 bits
        return highSurrogate
    end 'bmp'
end 'getCurrent'

export function UTF16View.next(self UTF16View) UTF16View
    // ... advance logic, handle surrogate pairs
end 'next'
```

### Phase 2: UTF-8 Helpers in stdlib

**File**: `stdlib/string/utf8.maxon`

```maxon
// UTF-8 utility functions

// Get byte length of UTF-8 sequence from lead byte
export function utf8_byte_length(leadByte byte) int
    var b = leadByte as int
    if b < 128 then return 1
    if b < 224 then return 2
    if b < 240 then return 3
    return 4
end 'utf8_byte_length'

// Check if byte is a continuation byte (10xxxxxx)
export function utf8_is_continuation(b byte) bool
    var val = b as int
    return val >= 128 and val < 192
end 'utf8_is_continuation'

// Decode codepoint from byte array at offset
export function utf8_decode(data ptr, offset int) int
    // Same logic as string.getCurrent()
    // ...
end 'utf8_decode'
```

### Phase 3: Compiler Changes for String Literals and Iteration

**File**: `maxon-bin/codegen_mir/codegen_mir_expr.cpp`

The compiler uses `ExpressibleByStringLiteral` interface to create strings from literals:

**When encountering a string literal:**
1. Emit UTF-8 data as global constant: `@.str.N = private constant [len x i8] c"..."`
2. Look up the target type (defaults to `string` if unspecified)
3. Check if target type conforms to `ExpressibleByStringLiteral`
4. Call `TargetType.initFromStringLiteral(dataPtr, length)`

**Example codegen for `var s = "hello"`:**
```llvm
; Global constant for literal data
@.str.0 = private constant [6 x i8] c"hello\00"

; In function:
; Call string.initFromStringLiteral(@.str.0, 5)
%s = call %string @"string.initFromStringLiteral"(ptr @.str.0, i32 5)
```

**Benefits of interface approach:**
- Compiler doesn't need to know `string` struct layout
- Any type can accept string literals by conforming to the interface
- stdlib controls how strings are created, not the compiler

**File**: `maxon-bin/codegen_mir/codegen_mir_stmt.cpp`

Modify `for-in` loop codegen to handle strings:

1. Detect when iterable type is `string`
2. Use `string.hasNext/getCurrent/next` for iteration (same as other Iterables)
3. Cast `getCurrent()` result to `char` for loop variable

**File**: `maxon-bin/semantic_analyzer/semantic_analyzer_stmt.cpp`

Update type inference for string iteration:

```cpp
// In for-loop analysis
if (iterableType == "string") {
    loopVarType = "char";  // Iteration yields char (codepoints)
}
```

### Phase 4: Remove String Functions from Runtime

**File**: `maxon-runtime/runtime.mir`

Remove all `__string_*` functions:
- `__string_is_small`
- `__string_count`
- `__string_is_empty`
- `__string_data`
- `__string_byte_at`
- `__string_equals`
- `__string_print`
- `__string_concat`
- `__string_retain`
- `__string_release`
- `__string_codepoint_count`
- `__string_codepoint_to_byte_index`
- `__utf8_byte_length`
- `__utf8_decode_codepoint`
- `__string_slice`
- `__string_slice_from`
- `__string_slice_to`
- `__string_starts_with`
- `__string_ends_with`
- `__string_find`
- `__string_contains`
- `__string_to_upper`
- `__string_to_lower`
- `__string_trim`
- etc.

Keep only:
- `malloc`, `free`, `memset`, `memcpy` (memory primitives)
- `write_stdout` (I/O primitive)
- Math functions

### Phase 5: Update Spec and Tests

**File**: `specs/string-type.md`

Add tests for character iteration:

```markdown
<!-- test: for-in-string -->
```maxon
function main() int
    var s = "abc"
    for c in s 'loop'
        print(c as int)
    end 'loop'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
97
98
99
```

<!-- test: for-in-unicode -->
```maxon
function main() int
    var s = "αβγ"
    var count = 0
    for c in s 'loop'
        count = count + 1
    end 'loop'
    print(count)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```
```

## Implementation Order

1. **Phase 1: Core string in stdlib** - Full `string` struct with SSO/heap and Iterable conformance
2. **Phase 2: UTF-8 helpers** - Decoding utilities in stdlib
3. **Phase 3: Compiler string literals** - Generate `string` structs from literals, handle iteration
4. **Phase 4: Remove runtime strings** - Clean up runtime.mir
5. **Phase 5: Spec and tests** - Update with iteration tests

## Files to Create/Modify

### New Files
- `stdlib/string/utf8.maxon` - UTF-8 utility functions
- `stdlib/string/views.maxon` - ByteView, UTF16View structs for string views

### Modified Files
- `stdlib/string/string.maxon` - Full string implementation with SSO/heap, Iterable, and ExpressibleByStringLiteral
- `stdlib/interfaces.maxon` - Add `ExpressibleByStringLiteral` interface (and optionally StringLike, Transformable)
- `specs/string-type.md` - Add iteration tests
- `maxon-bin/codegen_mir/codegen_mir_expr.cpp` - Use `ExpressibleByStringLiteral` for string literals
- `maxon-bin/codegen_mir/codegen_mir_stmt.cpp` - Handle string in for-in loops
- `maxon-bin/semantic_analyzer/semantic_analyzer_stmt.cpp` - Infer `char` type for string iteration
- `maxon-runtime/runtime.mir` - **Remove** all `__string_*` functions

## Runtime Dependencies

The stdlib string implementation needs only these runtime primitives:

| Function | Purpose |
|----------|---------|
| `malloc(size int) ptr` | Allocate heap buffer for large strings |
| `free(ptr ptr)` | Free heap buffer |
| `memcpy(dest ptr, src ptr, count int)` | Copy string data |
| `write_stdout(buf ptr, count int) int` | Print string to stdout |

**No string-specific runtime functions.**

## Validation

After implementation, run:

```bash
make test
```

This will:
1. Extract specs to fragments
2. Regenerate IR
3. Run all language tests including new string iteration tests

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Pointer arithmetic in Maxon | Use `ptr` type with `as` casts |
| Bitwise operations for SSO flag | Use arithmetic (div/mod by powers of 2) |
| Performance vs runtime | Compiler can inline stdlib; optimize later if needed |
| Breaking existing string code | Keep same public API, change only implementation |

## Success Criteria

1. ✅ `for c in "hello"` iterates over 5 characters
2. ✅ `for c in "αβγ"` iterates over 3 codepoints (not 6 bytes)
3. ✅ `s[0..5]` returns immutable `string`
4. ✅ All existing string tests pass
5. ✅ `string` conforms to `Hashable`, `Equatable`, `Printable`, `RangeSubscriptable`
6. ✅ No `__string_*` functions in runtime.mir
7. ✅ All string logic in `stdlib/string/`
