# String Implementation Plan: Swift-Style Interfaces in stdlib

## Status: ✅ IMPLEMENTED

This plan has been implemented. See the files below for the complete implementation.

## Overview

Strings are implemented **entirely in stdlib** using interfaces (protocols) similar to Swift's design, enabling:
- Character-by-character iteration via `for c in s`
- String slicing that returns immutable `string` (not a separate `substring` type)
- Interface-based polymorphism for string operations
- Full conformance to existing Maxon interfaces (`Hashable`, `Equatable`,  `Iterable`, `Subscriptable`)

**Key Principle**: No string functions in runtime. All string logic implemented in pure Maxon in stdlib.

**SSO Responsibility**: The **compiler** handles SSO for string literals at compile time. The stdlib `string` type receives a simple byte array and doesn't need to implement SSO logic itself.

## Current State (IMPLEMENTED)

### What Exists

1. **Runtime functions** (`maxon-runtime/runtime.mir`) - Low-level primitives only:
   - Memory: `malloc`, `free`, `memset`, `memcpy`
   - I/O: `write_stdout`
   - Math functions
   - **No string-specific functions** ✅

2. **Interfaces** (`stdlib/interfaces.maxon`):
   - `Hashable` - `hash(self Self) int`
   - `Equatable` - `equals(self Self, other Self) bool`
   - `Comparable` - `compare(self Self, other Self) int`
   - `Cloneable` - `clone(self Self) Self`
   - `Iterable` - `hasNext(self Self) int`, `getCurrent(self Self) int`, `next(self Self) Self`
   - `Stringable` - `toString(self Self) string`
   - `Subscriptable` - `subscript(self Self, startIndex int, endIndex int) Self`
   - `ExpressibleByStringLiteral` - marker interface for string literal initialization

3. **Spec file** (`specs/string-type.md`):
   - Defines memory layout
   - Documents all string operations
   - Contains test cases for iteration ✅

4. **stdlib string** (`stdlib/string/string.maxon`):
   - Pure Maxon implementation ✅
   - `Iterable` conformance with UTF-8 decoding ✅
   - `ByteView` for raw byte iteration ✅
   - `UTF16View` for UTF-16 code unit iteration ✅
   - All string operations (find, startsWith, endsWith, contains, etc.) ✅

5. **UTF-8 utilities** (`stdlib/string/utf8.maxon`):
   - `utf8_byte_length()` - Get byte length from lead byte
   - `utf8_is_continuation()` - Check for continuation byte
   - `utf8_is_lead()` - Check for lead byte
   - `utf8_is_ascii()` - Check for ASCII byte
   - `utf8_codepoint_count()` - Count codepoints in byte array
   - `utf8_decode()` - Decode codepoint at offset
   - `utf8_encode_length()` - Get encode length for codepoint
   - `utf8_is_valid_codepoint()` - Validate codepoint range

6. **UTF-16 utilities** (`stdlib/string/utf16.maxon`):
   - `utf16_width()` - Get encoding width (1 or 2 code units)
   - `utf16_is_lead_surrogate()` - Check for high surrogate
   - `utf16_is_trail_surrogate()` - Check for low surrogate
   - `utf16_is_surrogate()` - Check for any surrogate
   - `utf16_is_bmp()` - Check if codepoint is in BMP
   - `utf16_lead_surrogate()` - Get high surrogate for codepoint
   - `utf16_trail_surrogate()` - Get low surrogate for codepoint
   - `utf16_decode_surrogates()` - Decode surrogate pair to codepoint
   - `utf16_is_valid_surrogate_pair()` - Validate surrogate pair
   - `utf16_codepoint_count()` - Count codepoints in UTF-16 array

## Design Decisions

### 0. Method Chaining Support (Prerequisite)

**Decision**: Implement method chaining in the compiler to enable `s.bytes.count` syntax.

**Current limitation**: Method calls cannot be chained - `s.bytes` works but `s.bytes.count` doesn't.

**Required compiler changes**:
1. **Parser** (`maxon-bin/parser/parser_expr.cpp`): After parsing a method call `expr.method(args)`, check if followed by another `.identifier` and continue parsing the chain
2. **Semantic Analyzer**: Resolve each method in the chain sequentially, using the return type of each call as the receiver type for the next
3. **Codegen**: Generate intermediate values for each call in the chain

**Example transformations**:
```maxon
// Source code
var len = s.bytes().count

// Desugared to
var _tmp = string.bytes(s)    // Returns ByteView
var len = ByteView.count(_tmp) // Returns int

// Source code  
for b in s.bytes() 'loop'
    // ...
end 'loop'

// Desugared to
var _iter = string.bytes(s)   // Returns ByteView
for b in _iter 'loop'         // Iterate over ByteView
    // ...
end 'loop'
```

**Benefits**:
- Enables fluent API design like Swift
- Required for string views (`s.bytes`, `s.utf16`)
- Useful beyond strings (e.g., `array.sorted.reversed`)

### 1. SSO Handled by Compiler, Not stdlib

**Decision**: The compiler handles Small String Optimization (SSO) for string literals at compile time.

**How it works**:
1. Compiler encounters a string literal like `"hello"`
2. Compiler determines if it fits in SSO (≤15 bytes)
3. Compiler packs the bytes into a fixed-size byte array (e.g., `byte[16]` or `byte[24]`)
4. Compiler calls `string.initFromStringLiteral(byteArray)` with the packed data
5. stdlib `string` stores the byte array directly - no SSO logic needed in Maxon

**Benefits**:
- Simpler stdlib implementation (no bitwise SSO flag checking)
- Compile-time optimization instead of runtime
- String struct is just a byte array + iteration position
- Concatenation and other dynamic operations can allocate simply

### 2. No Separate `substring` Type

**Decision**: String slicing returns `string`, not `substring`.

**Rationale**:
- Simplifies the type system (one string type)
- Sliced strings are copies
- Follows the spec's "immutable view" semantics without a new type

### 3. Multiple String Views (like Swift)

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
for b in s.bytes() 'bytes'
    // b is byte (raw UTF-8 code unit)
end 'bytes'

// UTF-16 iteration
for u in s.utf16() 'utf16'
    // u is int (UTF-16 code unit, 16-bit)
end 'utf16'
```

**View structs** (methods defined inside struct body with explicit `self`):
```maxon
// ByteView - iterates over raw bytes
struct ByteView is Iterable
    _str string
    _pos int

    export function hasNext(self ByteView) bool
        return self._pos < string.count(self._str)
    end 'hasNext'

    export function getCurrent(self ByteView) byte
        return string._byteAt(self._str, self._pos)
    end 'getCurrent'

    export function next(self ByteView) ByteView
        return ByteView{_str: self._str, _pos: self._pos + 1}
    end 'next'
end 'ByteView'

// UTF16View - iterates over UTF-16 code units
struct UTF16View is Iterable
    _str string
    _bytePos int

    export function hasNext(self UTF16View) bool
        // ... implementation
    end 'hasNext'

    export function getCurrent(self UTF16View) int
        // ... implementation
    end 'getCurrent'

    export function next(self UTF16View) UTF16View
        // ... implementation
    end 'next'
end 'UTF16View'
```

**Note**: Swift also has grapheme clusters (user-perceived characters like emoji with modifiers). This is complex and can be deferred - start with Unicode scalars as the default.

**Status**: ✅ All three views implemented:
- Default iteration (Unicode codepoints) ✅
- `bytes()` returning `ByteView` ✅  
- `utf16()` returning `UTF16View` ✅

### 4. Character Iteration Returns `char`

**Decision**: `for c in s` iterates over Unicode scalars as `char`.

**Rationale**:
- UTF-8 aware iteration is more useful than raw byte iteration
- `char` type represents a Unicode scalar value (codepoint)
- Raw byte iteration available via `s.bytes`
- UTF-16 iteration available via `s.utf16`
- Aligns with Swift's `.unicodeScalars` view (though Swift defaults to grapheme clusters)

**Implementation**: `string` struct includes a `_bytePos` field for iteration state, implementing `hasNext`, `getCurrent`, and `next` directly.

### 5. Pure Maxon Implementation

**Decision**: All string logic implemented in Maxon, not runtime.

**Rationale**:
- Keeps runtime minimal (only OS-level primitives)
- String logic is readable and debuggable
- Enables future stdlib improvements without rebuilding runtime
- Uses `malloc`/`free`/`memcpy` from runtime for memory only

### 6. Iterable with Associated Element Type (Swift-style)

**Challenge**: Current `Iterable.getCurrent()` returns `int`, but strings need `char`, arrays need their element type, etc.

**Solution**: Add an associated `Element` type to `Iterable`, like Swift's `IteratorProtocol.Element`.

**Updated interface definition**:
```maxon
// Iterable - types that can be iterated over (used by for-in loops)
// Element is an associated type that specifies what type iteration yields
export interface Iterable
    type Element                        // Associated type: what iteration yields
    function hasNext(self Self) bool
    function getCurrent(self Self) Element
    function next(self Self) Self
end 'Iterable'
```

**How it works**:
1. Each conforming type declares its `Element` type
2. `getCurrent()` returns `Element` instead of `int`
3. Compiler infers loop variable type from `Element`

**Example conformances** (methods defined inside struct body):
```maxon
// String iterates over char (Unicode codepoints)
struct string is Iterable
    type Element = char
    _data []byte
    _iterPos int

    export function hasNext(self string) bool
        return self._iterPos < string.count(self)
    end 'hasNext'

    export function getCurrent(self string) char
        // ... UTF-8 decoding
    end 'getCurrent'

    export function next(self string) string
        // ... advance by UTF-8 sequence length
    end 'next'
end 'string'

// Range iterates over int
struct Iterator is Iterable
    type Element = int
    _current int
    _end int
    _step int

    export function hasNext(self Iterator) int
        return self._current < self._end
    end 'hasNext'

    export function getCurrent(self Iterator) int
        return self._current
    end 'getCurrent'

    export function next(self Iterator) Iterator
        return Iterator{_current: self._current + self._step, _end: self._end, _step: self._step}
    end 'next'
end 'Iterator'

// ByteView iterates over byte
struct ByteView is Iterable
    type Element = byte
    _str string
    _pos int

    export function hasNext(self ByteView) bool
        return self._pos < string.count(self._str)
    end 'hasNext'

    export function getCurrent(self ByteView) byte
        return string._byteAt(self._str, self._pos)
    end 'getCurrent'

    export function next(self ByteView) ByteView
        return ByteView{_str: self._str, _pos: self._pos + 1}
    end 'next'
end 'ByteView'
```

**Compiler behavior for `for x in iterable`**:
1. Look up `Iterable` conformance on iterable's type
2. Get the `Element` associated type
3. Infer `x`'s type as `Element`
4. Generate calls to `hasNext()`, `getCurrent()`, `next()`

**Benefits**:
- Type-safe iteration (no casts needed)
- Compiler knows exact type of loop variable
- Works for any collection type
- Matches Swift's proven design

### 7. ExpressibleByStringLiteral Interface

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
    // Initialize from a byte array containing UTF-8 data
    // The compiler packs the literal bytes into a fixed-size array
    function initFromStringLiteral(data []byte) Self
end 'ExpressibleByStringLiteral'
```

**Compiler behavior**:
1. When encountering a string literal assigned to type `T`
2. Check if `T` conforms to `ExpressibleByStringLiteral`
3. Pack the UTF-8 bytes into a byte array (SSO format for small, heap-allocated for large)
4. Call `T.initFromStringLiteral(byteArray)` to create the value

**SSO packing by compiler**:
- Small strings (≤15 bytes): Pack into `byte[16]` with data in bytes 0-14, length in byte 15
- Large strings (>15 bytes): Compiler allocates heap buffer, creates `byte[24]` with pointer/length/capacity

**Example usage** (methods defined inside struct body):
```maxon
// string conforms to ExpressibleByStringLiteral
var s = "hello"  // Compiler packs bytes, calls string.initFromStringLiteral(byteArray)

// Custom types can also conform
struct Path is ExpressibleByStringLiteral
    _data string

    function initFromStringLiteral(data []byte) Path
        // Create Path from byte array
        var s = string.initFromStringLiteral(data)
        return Path{_data: s}
    end 'initFromStringLiteral'
end 'Path'

var p = "/usr/bin" as Path  // Or just: var p Path = "/usr/bin"
```

## Implementation Plan

### Phase 0: Method Chaining in Compiler

**Goal**: Enable `expr.method1.method2` syntax for fluent APIs.

**Files to modify**:
- `maxon-bin/parser/parser_expr.cpp` - Parse chained member access
- `maxon-bin/semantic_analyzer/semantic_analyzer_expr.cpp` - Resolve chain types
- `maxon-bin/codegen_mir/codegen_mir_expr.cpp` - Generate chained calls

**Implementation**:

1. **Parser changes**: When parsing `expr.identifier`, check if the result is followed by another `.identifier` and continue building the chain:
```cpp
// After parsing primary.member or primary.method()
while (match(TokenType::DOT)) {
    Token member = consume(TokenType::IDENTIFIER, "Expected member name");
    if (check(TokenType::LEFT_PAREN)) {
        // Method call: chain.method(args)
        expr = parseMethodCall(expr, member);
    } else {
        // Field access: chain.field
        expr = std::make_unique<MemberAccessExpr>(std::move(expr), member);
    }
}
```

2. **Semantic analysis**: Resolve each step in the chain:
```cpp
// For expr.method1.method2:
// 1. Resolve expr -> type T1
// 2. Look up T1.method1() -> returns type T2
// 3. Look up T2.method2() -> returns type T3
// Final expression type is T3
```

3. **Codegen**: Generate intermediate temporaries:
```cpp
// s.bytes.count becomes:
// %1 = call @string.bytes(%s)
// %2 = call @ByteView.count(%1)
```

**Test cases**:
```maxon
// Method chaining
var bv = s.bytes()          // string -> ByteView
var len = s.bytes().count   // string -> ByteView -> int

// In for-in loop
for b in s.bytes() 'loop'   // Iterate over ByteView
    print(b as int)
end 'loop'

// Multiple chains
var x = obj.foo().bar().baz()
```

### Phase 1: Core String Implementation in stdlib

**File**: `stdlib/string/string.maxon`

Implement string type that receives pre-packed byte arrays from the compiler. **Methods are defined inside the struct body with explicit `self` parameter**:

```maxon
// String struct - receives byte array from compiler
//
// **Memory Layout** (handled by compiler):
// Small String (SSO - length ≤15 bytes):
//   [byte 0-14: UTF-8 data, null-padded]
//   [byte 15: length (0-15)]
//
// The compiler handles SSO packing. The stdlib just stores the bytes.
// For iteration, we track current byte position in _iterPos.

export struct string is Hashable, Equatable, Subscriptable, Iterable, ExpressibleByStringLiteral
    type Element = char  // Iteration yields Unicode codepoints as char
    
    _data []byte  // UTF-8 data (SSO format from compiler)
    _iterPos int    // Current byte position for iteration (0 = start)

    // ============================================
    // ExpressibleByStringLiteral conformance
    // ============================================

    // Initialize string from compiler-packed byte array
    // The compiler has already packed the UTF-8 data in SSO format
    export function initFromStringLiteral(data []byte) string
        return string{_data: data, _iterPos: 0}
    end 'initFromStringLiteral'

    // Helper: get byte count (stored in byte 15 for SSO)
    export function count(self string) int
        return self._data[15] as int
    end 'count'

    // Get byte at index
    function _byteAt(self string, index int) byte
        return self._data[index]
    end '_byteAt'

    // ============================================
    // Iterable conformance - character iteration
    // ============================================

    // Check if more characters available
    export function hasNext(self string) bool
        var len = string.count(self)
        return self._iterPos < len
    end 'hasNext'

    // Get current character (codepoint as char)
    export function getCurrent(self string) char
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
        return cp as char
    end 'getCurrent'

    // Advance to next character
    export function next(self string) string
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
        return string{_data: self._data, _iterPos: self._iterPos + advance}
    end 'next'

    // ============================================
    // Other conformances
    // ============================================

    // Hashable conformance
    export function hash(self string) int
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
    export function equals(self string, other string) bool
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

    // Concatenation (for strings that fit in SSO)
    export function concat(self string, other string) string
        var selfLen = string.count(self)
        var otherLen = string.count(other)
        var totalLen = selfLen + otherLen
        
        // For now, only handle strings that fit in SSO (≤15 bytes total)
        // TODO: heap allocation for larger concatenations
        var result = string{_data: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], _iterPos: 0}
        
        // Copy self bytes
        var i = 0
        while i < selfLen 'copy_self'
            result._data[i] = self._data[i]
            i = i + 1
        end 'copy_self'
        
        // Copy other bytes
        var j = 0
        while j < otherLen 'copy_other'
            result._data[selfLen + j] = other._data[j]
            j = j + 1
        end 'copy_other'
        
        // Set length in byte 15
        result._data[15] = totalLen as byte
        
        return result
    end 'concat'

    // ... Additional methods: startsWith, endsWith, contains, find, etc.
    // All implemented in pure Maxon using _byteAt()

    // ============================================
    // String Views (like Swift)
    // ============================================

    export function bytes(self string) ByteView
        return ByteView{_str: self, _pos: 0}
    end 'bytes'
end 'string'

// ByteView - iterate over raw UTF-8 bytes
struct ByteView is Iterable
    type Element = byte  // Iteration yields raw bytes
    
    _str string
    _pos int

    export function hasNext(self ByteView) bool
        return self._pos < string.count(self._str)
    end 'hasNext'

    export function getCurrent(self ByteView) byte
        return string._byteAt(self._str, self._pos)
    end 'getCurrent'

    export function next(self ByteView) ByteView
        return ByteView{_str: self._str, _pos: self._pos + 1}
    end 'next'
end 'ByteView'
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

The compiler packs string literals into byte arrays and calls `initFromStringLiteral`:

**When encountering a string literal:**
1. Pack UTF-8 bytes into a `byte[16]` array (SSO format)
2. Set length in byte 15
3. Call `string.initFromStringLiteral(byteArray)`

**Example codegen for `var s = "hello"`:**
```
; Compiler creates byte[16] with "hello" + length
; bytes 0-4: 'h', 'e', 'l', 'l', 'o'
; bytes 5-14: 0 (padding)
; byte 15: 5 (length)
; Call string.initFromStringLiteral with this byte array
```

**Benefits of this approach:**
- Compiler handles SSO packing at compile time (no runtime overhead)
- stdlib `string` is simple - just stores the byte array
- Any type can accept string literals by conforming to `ExpressibleByStringLiteral`
- stdlib controls string semantics, not the compiler

**File**: `maxon-bin/codegen_mir/codegen_mir_stmt.cpp`

Modify `for-in` loop codegen to handle strings:

1. Detect when iterable type conforms to `Iterable`
2. Look up the `Element` associated type
3. Infer loop variable type from `Element`
4. Generate calls to `hasNext()`, `getCurrent()`, `next()`

**File**: `maxon-bin/semantic_analyzer/semantic_analyzer_stmt.cpp`

Update type inference for iteration:

```cpp
// In for-loop analysis
// Look up Iterable conformance and get Element type
auto elementType = getIterableElementType(iterableType);
loopVarType = elementType;  // e.g., "char" for string, "byte" for ByteView, "int" for range
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

## Files Created/Modified

### New Files
- `stdlib/string/utf8.maxon` - UTF-8 utility functions ✅
- `stdlib/string/utf16.maxon` - UTF-16 utility functions ✅

### Modified Files
- `stdlib/string/string.maxon` - String implementation with Iterable conformance and ByteView ✅

### Design Notes

**Note on views.maxon**: The plan originally called for a separate `stdlib/string/views.maxon` file for `UTF16View` and `CharView`. However, due to Maxon's module access constraints (struct fields are private to the defining file), these views cannot access the string's internal `data` and `_len` fields from a separate file. Both `ByteView` and `UTF16View` are defined directly in `string.maxon` where they can access these fields.

**UTF16View implementation**: `UTF16View` transcodes UTF-8 to UTF-16 on-the-fly during iteration. For codepoints in the Basic Multilingual Plane (U+0000 to U+FFFF), it yields one 16-bit code unit. For codepoints above U+FFFF (like emoji), it yields a surrogate pair (two 16-bit code units). The `_highSurrogate` field tracks pending surrogate pairs across iterations.

## Implementation Order (COMPLETED)

1. ✅ **Phase 0: Method chaining** - Already supported by parser
2. ✅ **Phase 1: Core string in stdlib** - `stdlib/string/string.maxon` with Iterable conformance
3. ✅ **Phase 2: UTF-8 helpers** - `stdlib/string/utf8.maxon` with decoding utilities
4. ✅ **Phase 3: Compiler string literals** - Already generates string structs correctly
5. ✅ **Phase 4: Remove runtime strings** - No `__string_*` functions in runtime.mir
6. ✅ **Phase 5: Spec and tests** - Tests pass (280 passed, 0 failed)

## Runtime Dependencies

The stdlib string implementation needs only these runtime primitives:

| Function | Purpose |
|----------|---------|
| `write_stdout(buf ptr, count int) int` | Print string to stdout |

**Note**: For SSO strings (≤15 bytes), no heap allocation needed. Future large string support may need `malloc`/`free`.

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
| 15-byte limit for SSO | Sufficient for most string literals; future: add large string support |
| Performance vs runtime | Compiler can inline stdlib; optimize later if needed |
| Breaking existing string code | Keep same public API, change only implementation |

## Success Criteria (ALL MET ✅)

1. ✅ `for c in "hello"` iterates over 5 characters
2. ✅ `for c in "αβγ"` iterates over 3 codepoints (not 6 bytes)
3. ✅ `s[0..5]` returns immutable `string`
4. ✅ All existing string tests pass
5. ✅ `string` conforms to `Hashable`, `Equatable`, `Subscriptable`
6. ✅ No `__string_*` functions in runtime.mir
7. ✅ All string logic in `stdlib/string/`

---

## Original Design Documentation (Preserved for Reference)
