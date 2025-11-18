# String Implementation Plan for Maxon

## Overview

This document outlines a plan to implement proper string handling in Maxon, inspired by Rust's approach to strings. Rust distinguishes between two primary string types:
- `String` - owned, heap-allocated, growable UTF-8 string
- `&str` - borrowed string slice (view into UTF-8 data)

For Maxon, we'll adapt this model while keeping the language's syntax and philosophy.

## Design Goals

1. **Safety**: Prevent buffer overflows, use-after-free, and double-free errors
2. **Clarity**: Distinguish between owned strings and string views
3. **Performance**: Minimize allocations, enable efficient string operations
4. **Simplicity**: Keep syntax approachable for systems programming
5. **UTF-8**: Support UTF-8 encoding by default (like Rust)
6. **Interop**: Support C-style string literals for FFI

## Current State

Currently, Maxon has:
- `string` keyword as alias for `ptr` (basic pointer type)
- String literals: `"hello"` creates a global constant char array, returns `ptr`
- Character literals: `'A'` (single char)
- No string manipulation functions
- No distinction between owned and borrowed strings

## Proposed String Types

**Note**: Maxon follows Rust's multi-tier string approach to handle different use cases efficiently and safely.

### String Type Overview

| Type | Purpose | Encoding | Owned | Mutable | Use Case |
|------|---------|----------|-------|---------|----------|
| `str` | String view | UTF-8 | No | No | Function parameters, string literals |
| `String` | Owned string | UTF-8 | Yes | Yes | Dynamic text, string building |
| `cstr` | C string | UTF-8 (null-terminated) | No | No | FFI with C libraries |
| `OsStr` | Platform string view | Platform-dependent* | No | No | File paths, OS API parameters |
| `OsString` | Owned platform string | Platform-dependent* | Yes | Yes | File path manipulation, OS APIs |

\* Platform-dependent encoding:
- **Windows**: UTF-16 (wide characters)
- **Unix/Linux**: Byte sequences (often UTF-8, but not guaranteed)
- **macOS**: UTF-8 (treated like Unix)

### Type 1: `str` (String View/Slice)
**Concept**: Borrowed, immutable view into UTF-8 string data (similar to Rust's `&str`)

**Properties**:
- Does NOT own the data
- Points to existing string data (literal, buffer, or part of an owned string)
- Immutable by default
- Fixed-size struct containing:
  - `ptr` - pointer to UTF-8 bytes
  - `len` - length in bytes (not characters, due to UTF-8)
- Zero-cost abstraction for string views

**Syntax**:
```maxon
function greet(name str)
    // 'name' is a view into string data
    print_str(name)
end 'greet'

let message str = "Hello, world!"  // View into string literal
```

### Type 2: `String` (Owned String)
**Concept**: Owned, heap-allocated, growable UTF-8 string (similar to Rust's `String`)

**Properties**:
- Owns the data on the heap
- Mutable and growable
- Automatically freed when it goes out of scope
- Struct containing:
  - `ptr` - pointer to heap-allocated UTF-8 bytes
  - `len` - current length in bytes
  - `capacity` - allocated capacity in bytes
- Reference-counted or move-semantics for ownership

**Syntax**:
```maxon
var message String = String.new()
message.push_str("Hello")
message.push_str(", world!")
// message automatically freed at end of scope
```

### Type 3: `cstr` (C String)
**Concept**: Null-terminated C-style string pointer for FFI

**Properties**:
- Raw pointer to null-terminated UTF-8 bytes
- For C interop (calling external functions)
- No length tracking
- Minimal safety guarantees

**Syntax**:
```maxon
extern function strlen(s cstr) int end

function call_c_function()
    let s cstr = "hello\0" as cstr  // Explicit null terminator
    let len int = strlen(s)
end 'call_c_function'
```

### Type 4: `OsString` and `OsStr` (Platform Strings)
**Concept**: Platform-native string encoding for OS APIs (similar to Rust's `OsString`/`OsStr`)

**Why needed**:
- **Windows**: Uses UTF-16 (wide characters) for most APIs
- **Unix/Linux**: Uses byte sequences (often UTF-8, but not guaranteed)
- **macOS**: Uses UTF-8
- File paths may contain invalid UTF-8 on Unix
- Windows file paths require UTF-16 conversion

**Properties**:
- `OsStr` - Borrowed view of platform string (like `str` but platform-encoded)
- `OsString` - Owned platform string (like `String` but platform-encoded)
- Automatically converts between UTF-8 and platform encoding
- Essential for file system operations
- Platform-specific internal representation:
  - Windows: UTF-16 (wchar_t*)
  - Unix/Linux: Byte array (may not be valid UTF-8)

**Syntax**:
```maxon
# File path handling
function open_file(path OsStr) int
    # path is in platform-native encoding
    let fd int = fs.open(path)
    return fd
end 'open_file'

function main()
    # Convert UTF-8 string to platform string
    let filename str = "test.txt"
    let os_path OsString = OsString.from_str(filename)
    
    let fd int = open_file(OsString.as_os_str(os_path))
end 'main'
```

**Windows-specific example**:
```maxon
# On Windows, file paths use UTF-16
extern function CreateFileW(
    filename ptr,  # LPCWSTR (UTF-16)
    access int,
    share int,
    security ptr,
    creation int,
    flags int,
    template ptr
) ptr end

function open_windows_file(path OsStr) ptr
    # OsStr automatically provides UTF-16 on Windows
    return CreateFileW(path.as_wide_ptr(), ...)
end 'open_windows_file'
```

## Implementation Phases

---

## Phase 1: String View Type (`str`)

### 1.1 Core Type Definition

**In `maxon-bin/ast.h`**:
```cpp
// String view struct (fat pointer)
struct StrView {
    const char* data;
    size_t len;
};
```

**In `maxon-bin/lexer.h`**:
- Keep `STRING_TYPE` token (currently maps to "string")
- Add `STR` token for new `str` keyword

**In `maxon-bin/lexer.cpp`**:
- Add `{"str", TokenType::STR}` to keyword map
- Keep current string literal handling

**In `maxon-bin/parser.cpp`**:
- Recognize `str` as a type in variable declarations
- Recognize `str` as a type in function parameters

**In `maxon-bin/codegen.cpp`**:
- Add `getTypeFromString()` case for `"str"`:
  - Returns struct type: `{ ptr i8, i64 }` (pointer + length)
- String literals now create `str` values:
  - Global constant for string data
  - Return struct with `{pointer, length}`

**In `maxon-bin/semantic_analyzer.cpp`**:
- Add type checking for `str`
- String literals have type `str` by default
- Allow implicit conversion from string literal to `str`

### 1.2 String Literal Handling

**Current behavior**:
```maxon
var s ptr = "hello"  // Returns ptr to global string constant
```

**New behavior**:
```maxon
let s str = "hello"  // Returns {ptr, len} struct
```

**Implementation**:
- String literals create immutable global constants
- Return `str` struct: `{pointer to data, length}`
- Length does NOT include null terminator
- Data IS null-terminated for C interop (but length doesn't count it)

### 1.3 Basic String Operations (stdlib)

**In `stdlib/str/`** (new directory):

Create `stdlib/str/core.maxon`:
```maxon
namespace str

# Get length of string in bytes
function len(s str) int
    return s.len as int
end 'len'

# Check if string is empty
function is_empty(s str) int
    return s.len = 0
end 'is_empty'

# Compare two strings for equality
function equals(a str, b str) int
    if a.len != b.len
        return 0  # false
    end
    
    # Compare byte by byte
    var i int = 0
    while i < a.len
        let byte_a char = *(a.data + i)
        let byte_b char = *(b.data + i)
        if byte_a != byte_b
            return 0  # false
        end
        i = i + 1
    end
    
    return 1  # true
end 'equals'

# Print string to stdout
extern function write(fd int, buf ptr, count int) int end

function print(s str)
    write(1, s.data, s.len as int)
end 'print'

end 'namespace str'
```

### 1.4 Testing

**Test files to create**:
- `language-tests/fragments/str-literal.test` - Basic string literal to `str`
- `language-tests/fragments/str-function-param.test` - Pass `str` to function
- `language-tests/fragments/str-len.test` - Get string length
- `language-tests/fragments/str-equals.test` - Compare strings
- `language-tests/fragments/str-print.test` - Print string

---

## Phase 2: Struct Support (Required for `str`)

To properly implement `str` as a struct with `{ptr, len}`, we need struct support.

### 2.1 Struct Definition Syntax

**In `maxon-bin/lexer.h`**:
- Add `STRUCT` token type

**In `maxon-bin/ast.h`**:
```cpp
// Struct field
struct StructField {
    std::string name;
    std::string type;
};

// Struct definition
class StructDefAST : public ASTNode {
public:
    std::string name;
    std::vector<StructField> fields;
};

// Struct member access: obj.field
class MemberAccessExprAST : public ExprAST {
public:
    std::unique_ptr<ExprAST> object;
    std::string fieldName;
};
```

**Syntax**:
```maxon
struct str
    data ptr
    len int
end 'str'

var s str
s.data = "hello"
s.len = 5
```

### 2.2 Built-in Structs

Register built-in structs in semantic analyzer:
- `str` struct (defined by compiler)
- User-defined structs (parsed from code)

---

## Phase 3: Owned String Type (`String`)

### 3.1 Memory Management Strategy

**Option A: Manual Management** (simplest, Maxon Phase 1)
```maxon
var s String = String.new()
s.push_str("hello")
s.free()  # Manual cleanup
```

**Option B: RAII with Scope-based Cleanup** (preferred, similar to Rust)
```maxon
function test()
    var s String = String.new()
    s.push_str("hello")
    # Automatic cleanup at end of scope
end 'test'
```

**Option C: Reference Counting** (complex, future)
- Track references to `String`
- Free when reference count reaches zero
- Similar to `Rc<String>` in Rust

**Recommendation**: Start with **Option A** (manual), evolve to **Option B** (RAII) as compiler matures.

### 3.2 String Struct Definition

**In `stdlib/str/string.maxon`**:
```maxon
namespace str

struct String
    data ptr      # Heap-allocated buffer
    len int       # Current length in bytes
    capacity int  # Allocated capacity
end 'String'

# Allocate new empty string with capacity
extern function malloc(size int) ptr end
extern function free(ptr ptr) end

function new() String
    let cap int = 16  # Initial capacity
    let buffer ptr = malloc(cap)
    
    var s String
    s.data = buffer
    s.len = 0
    s.capacity = cap
    return s
end 'new'

# Free string memory
function free_string(s String)
    if s.data != 0 as ptr
        free(s.data)
    end
end 'free_string'

# Get string as view
function as_str(s String) str
    var view str
    view.data = s.data
    view.len = s.len
    return view
end 'as_str'

end 'namespace str'
```

### 3.3 String Growth and Mutation

**In `stdlib/str/string.maxon`**:
```maxon
namespace str

extern function realloc(ptr ptr, size int) ptr end
extern function memcpy(dest ptr, src ptr, n int) ptr end

# Ensure capacity for at least 'min_cap' bytes
function reserve(s String, min_cap int) String
    if s.capacity >= min_cap
        return s
    end
    
    var new_cap int = s.capacity * 2
    if new_cap < min_cap
        new_cap = min_cap
    end
    
    let new_buffer ptr = realloc(s.data, new_cap)
    s.data = new_buffer
    s.capacity = new_cap
    return s
end 'reserve'

# Append string view to owned string
function push_str(s String, addition str) String
    let new_len int = s.len + addition.len
    s = reserve(s, new_len)
    
    # Copy bytes from addition to end of s.data
    memcpy(s.data + s.len, addition.data, addition.len)
    s.len = new_len
    
    return s
end 'push_str'

# Append single char
function push_char(s String, c char) String
    let new_len int = s.len + 1
    s = reserve(s, new_len)
    
    *(s.data + s.len) = c
    s.len = new_len
    
    return s
end 'push_char'

end 'namespace str'
```

### 3.4 String Conversion Functions

**In `stdlib/str/convert.maxon`**:
```maxon
namespace str

# Convert integer to string
function from_int(value int) String
    var s String = new()
    
    if value = 0
        s = push_char(s, '0')
        return s
    end
    
    var is_negative int = 0
    if value < 0
        is_negative = 1
        value = 0 - value
    end
    
    # Build digits in reverse
    var [20]char buffer
    var pos int = 0
    
    while value > 0
        let digit int = value % 10
        buffer[pos] = ('0' as int + digit) as char
        pos = pos + 1
        value = value / 10
    end
    
    if is_negative
        s = push_char(s, '-')
    end
    
    # Reverse append
    var i int = pos - 1
    while i >= 0
        s = push_char(s, buffer[i])
        i = i - 1
    end
    
    return s
end 'from_int'

# Parse integer from string view
function parse_int(s str) int
    var result int = 0
    var is_negative int = 0
    var i int = 0
    
    # Check for negative sign
    if s.len > 0
        let first char = *(s.data + 0)
        if first = '-'
            is_negative = 1
            i = 1
        end
    end
    
    while i < s.len
        let c char = *(s.data + i)
        if c >= '0'
            if c <= '9'
                let digit int = (c as int) - ('0' as int)
                result = result * 10 + digit
            end
        end
        i = i + 1
    end
    
    if is_negative
        result = 0 - result
    end
    
    return result
end 'parse_int'

end 'namespace str'
```

---

## Phase 4: String Slicing

### 4.1 Slice Syntax

**Concept**: Create a view (`str`) into part of a string

**Syntax**:
```maxon
let full str = "Hello, world!"
let slice str = full[0:5]  # "Hello"
let rest str = full[7:]    # "world!"
```

### 4.2 Implementation

**In `maxon-bin/ast.h`**:
```cpp
// String slice expression
class StringSliceExprAST : public ExprAST {
public:
    std::unique_ptr<ExprAST> string;  // Base string
    std::unique_ptr<ExprAST> start;   // Start index (optional)
    std::unique_ptr<ExprAST> end;     // End index (optional)
};
```

**In `maxon-bin/parser.cpp`**:
- Parse `expr[start:end]` syntax
- Handle `[start:]`, `[:end]`, `[:]` cases

**In `maxon-bin/codegen.cpp`**:
- Load string struct `{data, len}`
- Calculate slice bounds with bounds checking
- Return new `str` struct with adjusted pointer and length

### 4.3 Bounds Checking

Add runtime bounds checking:
```maxon
function slice_bounds_check(s str, start int, end int)
    if start < 0
        # panic("slice start < 0")
        exit(1)
    end
    if end > s.len
        # panic("slice end > length")
        exit(1)
    end
    if start > end
        # panic("slice start > end")
        exit(1)
    end
end 'slice_bounds_check'
```

---

## Phase 5: UTF-8 Support

### 5.1 Character vs Byte Distinction

**Important**: In UTF-8, string length in bytes ≠ length in characters

**In `stdlib/str/utf8.maxon`**:
```maxon
namespace str

# Count UTF-8 characters (code points) in string
function char_count(s str) int
    var count int = 0
    var i int = 0
    
    while i < s.len
        let byte char = *(s.data + i)
        let byte_val int = byte as int
        
        if byte_val < 128
            # ASCII (1 byte)
            i = i + 1
        else
            if byte_val < 224
                # 2-byte sequence
                i = i + 2
            else
                if byte_val < 240
                    # 3-byte sequence
                    i = i + 3
                else
                    # 4-byte sequence
                    i = i + 4
                end
            end
        end
        count = count + 1
    end
    
    return count
end 'char_count'

# Validate UTF-8 encoding
function is_valid_utf8(s str) int
    # TODO: Implement full UTF-8 validation
    return 1
end 'is_valid_utf8'

end 'namespace str'
```

### 5.2 Character Iteration

**In `stdlib/str/utf8.maxon`**:
```maxon
namespace str

struct CharIter
    data ptr
    len int
    pos int
end 'CharIter'

function chars(s str) CharIter
    var iter CharIter
    iter.data = s.data
    iter.len = s.len
    iter.pos = 0
    return iter
end 'chars'

# Get next UTF-8 character (returns -1 if done)
function next_char(iter CharIter) int
    if iter.pos >= iter.len
        return -1
    end
    
    let byte char = *(iter.data + iter.pos)
    let byte_val int = byte as int
    
    var char_val int = 0
    
    if byte_val < 128
        # ASCII (1 byte)
        char_val = byte_val
        iter.pos = iter.pos + 1
    end
    # TODO: Handle multi-byte sequences
    
    return char_val
end 'next_char'

end 'namespace str'
```

---

## Phase 6: Advanced String Operations

### 6.1 String Formatting

**In `stdlib/str/format.maxon`**:
```maxon
namespace str

# Format string with integer placeholder
function format(template str, value int) String
    var result String = new()
    var i int = 0
    
    while i < template.len
        let c char = *(template.data + i)
        
        if c = '{'
            # Check for {i} placeholder
            if i + 2 < template.len
                let next char = *(template.data + i + 1)
                if next = 'i'
                    let close char = *(template.data + i + 2)
                    if close = '}'
                        # Replace with integer value
                        let int_str String = from_int(value)
                        result = push_str(result, as_str(int_str))
                        free_string(int_str)
                        i = i + 3
                        continue
                    end
                end
            end
        end
        
        result = push_char(result, c)
        i = i + 1
    end
    
    return result
end 'format'

end 'namespace str'
```

### 6.2 String Search

**In `stdlib/str/search.maxon`**:
```maxon
namespace str

# Find first occurrence of substring
function find(haystack str, needle str) int
    if needle.len = 0
        return 0
    end
    
    if needle.len > haystack.len
        return -1
    end
    
    var i int = 0
    let max int = haystack.len - needle.len
    
    while i <= max
        var found int = 1
        var j int = 0
        
        while j < needle.len
            let h_byte char = *(haystack.data + i + j)
            let n_byte char = *(needle.data + j)
            if h_byte != n_byte
                found = 0
                break
            end
            j = j + 1
        end
        
        if found
            return i
        end
        
        i = i + 1
    end
    
    return -1
end 'find'

# Check if string starts with prefix
function starts_with(s str, prefix str) int
    if prefix.len > s.len
        return 0
    end
    
    var i int = 0
    while i < prefix.len
        let s_byte char = *(s.data + i)
        let p_byte char = *(prefix.data + i)
        if s_byte != p_byte
            return 0
        end
        i = i + 1
    end
    
    return 1
end 'starts_with'

end 'namespace str'
```

### 6.3 String Splitting

**In `stdlib/str/split.maxon`**:
```maxon
namespace str

# Split string by delimiter (returns array of views)
# Note: Requires dynamic array support
function split(s str, delimiter char) []str
    # Count occurrences
    var count int = 1
    var i int = 0
    while i < s.len
        let c char = *(s.data + i)
        if c = delimiter
            count = count + 1
        end
        i = i + 1
    end
    
    # Allocate result array
    var result []str = alloc_array_str(count)
    
    # Fill array with substrings
    var part_idx int = 0
    var start int = 0
    i = 0
    
    while i < s.len
        let c char = *(s.data + i)
        if c = delimiter
            # Create slice from start to i
            var part str
            part.data = s.data + start
            part.len = i - start
            result[part_idx] = part
            
            part_idx = part_idx + 1
            start = i + 1
        end
        i = i + 1
    end
    
    # Add final part
    var part str
    part.data = s.data + start
    part.len = s.len - start
    result[part_idx] = part
    
    return result
end 'split'

end 'namespace str'
```

---

## Phase 7: Platform Strings (`OsString` / `OsStr`)

### 7.1 Platform String Types

**Purpose**: Handle platform-specific string encodings for OS APIs and file paths

**In `maxon-bin/lexer.h`**:
- Add `OSSTRING` token type
- Add `OSSTR` token type

**In `maxon-bin/codegen.cpp`**:
```cpp
if (typeStr == "OsString" || typeStr == "OsStr") {
    // Platform-specific representation
    #ifdef _WIN32
        // Windows: struct { ptr wchar_t, i64 len }
        return StructType::create({
            PointerType::get(context, 0),  // UTF-16 data
            Type::getInt64Ty(context)      // length in wchar_t units
        });
    #else
        // Unix: Same as str (byte array)
        return StructType::create({
            PointerType::get(context, 0),  // byte data
            Type::getInt64Ty(context)      // length in bytes
        });
    #endif
}
```

### 7.2 Platform String Implementation

**In `stdlib/os/osstring.maxon`**:
```maxon
namespace os

# Platform string types
struct OsString
    data ptr      # Platform-encoded data (UTF-16 on Windows, bytes on Unix)
    len int       # Length (wchar_t count on Windows, bytes on Unix)
    capacity int  # Allocated capacity
end 'OsString'

struct OsStr
    data ptr      # Platform-encoded data
    len int       # Length
end 'OsStr'

# Platform detection (compiler defines)
# Windows: _WIN32
# Unix/Linux: __unix__ or __linux__
# macOS: __APPLE__

end 'namespace os'
```

### 7.3 Conversion Functions (Windows)

**In `stdlib/os/osstring_windows.maxon`** (Windows-specific):
```maxon
namespace os

# Windows uses UTF-16 (wide characters)
extern function MultiByteToWideChar(
    codepage int,
    flags int,
    str ptr,
    strlen int,
    wstr ptr,
    wstrlen int
) int end

extern function WideCharToMultiByte(
    codepage int,
    flags int,
    wstr ptr,
    wstrlen int,
    str ptr,
    strlen int,
    default ptr,
    used ptr
) int end

# UTF-8 code page
let CP_UTF8 int = 65001

# Convert UTF-8 str to UTF-16 OsString
function from_str(s str) OsString
    # Calculate required buffer size
    let wlen int = MultiByteToWideChar(CP_UTF8, 0, s.data, s.len, 0 as ptr, 0)
    
    if wlen = 0
        # Error handling
        var empty OsString
        empty.data = 0 as ptr
        empty.len = 0
        empty.capacity = 0
        return empty
    end
    
    # Allocate buffer for UTF-16
    let capacity int = wlen * 2  # 2 bytes per wchar_t
    let buffer ptr = malloc(capacity)
    
    # Perform conversion
    let result int = MultiByteToWideChar(CP_UTF8, 0, s.data, s.len, buffer, wlen)
    
    var os_string OsString
    os_string.data = buffer
    os_string.len = wlen
    os_string.capacity = wlen
    return os_string
end 'from_str'

# Convert UTF-16 OsString to UTF-8 str
function to_str(os OsString) String
    # Calculate required buffer size
    let len int = WideCharToMultiByte(CP_UTF8, 0, os.data, os.len, 0 as ptr, 0, 0 as ptr, 0 as ptr)
    
    if len = 0
        return String.new()
    end
    
    # Allocate buffer for UTF-8
    let buffer ptr = malloc(len)
    
    # Perform conversion
    let result int = WideCharToMultiByte(CP_UTF8, 0, os.data, os.len, buffer, len, 0 as ptr, 0 as ptr)
    
    var s String
    s.data = buffer
    s.len = len
    s.capacity = len
    return s
end 'to_str'

# Get OsStr view from OsString
function as_os_str(os OsString) OsStr
    var view OsStr
    view.data = os.data
    view.len = os.len
    return view
end 'as_os_str'

# Get wide pointer for Windows APIs
function as_wide_ptr(os OsStr) ptr
    return os.data
end 'as_wide_ptr'

end 'namespace os'
```

### 7.4 Conversion Functions (Unix/Linux)

**In `stdlib/os/osstring_unix.maxon`** (Unix-specific):
```maxon
namespace os

# Unix uses byte sequences (no guaranteed encoding)
# OsString is essentially the same as String

# Convert str to OsString (no conversion needed on Unix)
function from_str(s str) OsString
    var os OsString
    
    # Copy the string data
    let buffer ptr = malloc(s.len)
    memcpy(buffer, s.data, s.len)
    
    os.data = buffer
    os.len = s.len
    os.capacity = s.len
    return os
end 'from_str'

# Convert OsString to String (no conversion needed)
function to_str(os OsString) String
    var s String
    
    # Copy the data
    let buffer ptr = malloc(os.len)
    memcpy(buffer, os.data, os.len)
    
    s.data = buffer
    s.len = os.len
    s.capacity = os.len
    return s
end 'to_str'

# Get OsStr view
function as_os_str(os OsString) OsStr
    var view OsStr
    view.data = os.data
    view.len = os.len
    return view
end 'as_os_str'

# Get byte pointer (for Unix APIs)
function as_ptr(os OsStr) ptr
    return os.data
end 'as_ptr'

end 'namespace os'
```

### 7.5 File System Integration

**In `stdlib/fs/path.maxon`**:
```maxon
namespace fs

# Path operations using OsString
struct Path
    inner OsString
end 'Path'

# Create path from string
function path_from_str(s str) Path
    var p Path
    p.inner = os.from_str(s)
    return p
end 'path_from_str'

# Open file using platform-native API
# Windows: CreateFileW (UTF-16)
# Unix: open (byte array)

# Windows version
extern function CreateFileW(
    filename ptr,      # LPCWSTR
    access int,
    share int,
    security ptr,
    creation int,
    flags int,
    template ptr
) ptr end

let GENERIC_READ int = 0x80000000
let OPEN_EXISTING int = 3

function open_windows(path Path) ptr
    let os_view OsStr = os.as_os_str(path.inner)
    let wide_ptr ptr = os.as_wide_ptr(os_view)
    
    return CreateFileW(
        wide_ptr,
        GENERIC_READ,
        0,
        0 as ptr,
        OPEN_EXISTING,
        0,
        0 as ptr
    )
end 'open_windows'

# Unix version
extern function open(pathname ptr, flags int) int end

let O_RDONLY int = 0

function open_unix(path Path) int
    let os_view OsStr = os.as_os_str(path.inner)
    let ptr_val ptr = os.as_ptr(os_view)
    
    return open(ptr_val, O_RDONLY)
end 'open_unix'

# Cross-platform open
function open_file(path Path) int
    # Compiler selects based on platform
    # ifdef _WIN32
    #   return open_windows(path) as int
    # else
    #   return open_unix(path)
    # endif
end 'open_file'

end 'namespace fs'
```

### 7.6 Testing

**Test files to create**:
- `language-tests/fragments/osstring-from-str.test` - Convert str to OsString
- `language-tests/fragments/osstring-to-str.test` - Convert OsString to str
- `language-tests/fragments/osstring-file-path.test` - Use OsString for file operations
- `language-tests/fragments/osstring-windows-api.test` - Windows UTF-16 API calls
- `language-tests/fragments/osstring-unix-api.test` - Unix byte array APIs

---

## Phase 8: C String Interop (`cstr`)

### 8.1 C String Type

**Purpose**: Null-terminated strings for FFI

**In `maxon-bin/lexer.h`**:
- Add `CSTR` token type

**In `maxon-bin/codegen.cpp`**:
```cpp
if (typeStr == "cstr") {
    return llvm::PointerType::get(context, 0);  // i8*
}
```

**Syntax**:
```maxon
extern function strlen(s cstr) int end
extern function printf(format cstr, ...) int end

function test()
    let msg cstr = "Hello\0" as cstr
    let len int = strlen(msg)
end 'test'
```

### 7.2 Conversions

**In `stdlib/str/cstr.maxon`**:
```maxon
namespace str

# Convert str to cstr (allocates null-terminated copy)
function to_cstr(s str) cstr
    let buffer ptr = malloc(s.len + 1)
    memcpy(buffer, s.data, s.len)
    *(buffer + s.len) = '\0'
    return buffer as cstr
end 'to_cstr'

# Convert cstr to str (view, no allocation)
function from_cstr(cs cstr) str
    # Find length
    var len int = 0
    while *(cs + len) != '\0'
        len = len + 1
    end
    
    var s str
    s.data = cs as ptr
    s.len = len
    return s
end 'from_cstr'

end 'namespace str'
```

---

## Phase 9: Ownership and Move Semantics

### 8.1 Move Semantics (Similar to Rust)

**Concept**: Transfer ownership instead of copying

**Syntax**:
```maxon
function take_ownership(s String)
    # s is moved here, caller loses access
    print_str(as_str(s))
    free_string(s)
end 'take_ownership'

function main()
    var message String = String.new()
    message.push_str("Hello")
    
    take_ownership(message)
    # message is no longer valid here
end 'main'
```

**Implementation**:
- Track ownership in semantic analyzer
- Mark variables as "moved" after passing to functions
- Error on use-after-move

### 8.2 Borrowing (Future Enhancement)

**Concept**: Pass references without transferring ownership

**Syntax**:
```maxon
function borrow_string(s &String)
    # Can read s, but doesn't own it
    print_str(as_str(s))
    # s is NOT freed here
end 'borrow_string'

function main()
    var message String = String.new()
    message.push_str("Hello")
    
    borrow_string(&message)
    # message still valid here
    
    free_string(message)
end 'main'
```

---

## Phase 10: Testing Strategy

### 10.1 Unit Tests

**Test files to create**:
```
language-tests/fragments/
  str-literal.test              # String literal creates str
  str-struct.test               # str as struct {ptr, len}
  str-function-param.test       # Pass str to function
  str-equals.test               # String comparison
  str-concat.test               # String concatenation
  String-new.test               # Create owned String
  String-push-str.test          # Append to String
  String-free.test              # Manual cleanup
  String-from-int.test          # Integer to string
  str-parse-int.test            # String to integer
  str-slice.test                # String slicing
  str-find.test                 # String search
  str-split.test                # String splitting
  cstr-conversion.test          # str <-> cstr conversion
  cstr-ffi.test                 # Call C function with cstr
```

### 10.2 Integration Tests

**Example programs**:
```maxon
# examples/string-demo.maxon
function main()
    var message String = String.new()
    message = push_str(message, "Hello, ")
    message = push_str(message, "world!")
    
    print_str(as_str(message))
    
    free_string(message)
end 'main'
```

### 10.3 Documentation Tests

**In `docs/Content/strings.md`**:
```markdown
# Strings in Maxon

Maxon has three string types:

## String Views (`str`)

A `str` is an immutable view into UTF-8 string data:

```maxon
let greeting str = "Hello, world!"
print_str(greeting)
```
ExitCode: 0

## Owned Strings (`String`)

A `String` is a heap-allocated, growable string:

```maxon
var message String = String.new()
message = push_str(message, "Hello")
free_string(message)
```
ExitCode: 0
```

---

## Phase 11: Documentation

### 11.1 Create `docs/Content/strings.md`

Comprehensive documentation covering:
- String types overview
- String literals
- String operations
- Memory management
- UTF-8 handling
- C interop
- Best practices

### 11.2 Update `docs/Content/types.md`

Add string types to type system documentation:
- `str` (string view)
- `String` (owned string)
- `cstr` (C string)
- `OsString` / `OsStr` (platform strings)
- Conversions between types

### 11.3 Update `stdlib/README.md`

Document string standard library modules:
- `str/core.maxon` - Basic operations
- `str/string.maxon` - Owned String type
- `str/convert.maxon` - Conversions
- `str/utf8.maxon` - UTF-8 utilities
- `str/format.maxon` - String formatting
- `str/search.maxon` - Search operations
- `str/cstr.maxon` - C string interop
- `os/osstring.maxon` - Platform strings
- `os/osstring_windows.maxon` - Windows UTF-16 support
- `os/osstring_unix.maxon` - Unix byte array support

---

## Implementation Order Summary

1. **Phase 1**: String view type (`str`) with basic operations
2. **Phase 2**: Struct support (required for `str` internals)
3. **Phase 3**: Owned string type (`String`) with manual memory management
4. **Phase 4**: String slicing
5. **Phase 5**: UTF-8 support
6. **Phase 6**: Advanced operations (format, search, split)
7. **Phase 7**: Platform strings (`OsString` / `OsStr`) for file paths and OS APIs
8. **Phase 8**: C string interop (`cstr`)
9. **Phase 9**: Ownership and move semantics
10. **Phase 10**: Comprehensive testing
11. **Phase 11**: Documentation

---

## Migration Strategy

### Backward Compatibility

**Current code**:
```maxon
var s ptr = "hello"
```

**Migration path**:
```maxon
# Option 1: Use new str type
let s str = "hello"

# Option 2: Keep old behavior with explicit cast
var s ptr = "hello" as ptr
```

**Deprecation timeline**:
1. Phase 1: Introduce `str` type, keep `string` as alias for `ptr`
2. Phase 2: Deprecate `string` keyword, recommend `str` or `ptr`
3. Phase 3: Remove `string` keyword (breaking change)

### Standard Library Evolution

Provide compatibility shims:
```maxon
# Old: print takes ptr
function print_old(s ptr)
    # ...
end

# New: print takes str
function print_str(s str)
    # ...
end

# Compatibility wrapper
function print(s ptr)
    let view str = from_ptr(s)
    print_str(view)
end
```

---

## Open Questions

1. **Reference Counting vs RAII**: Which memory management strategy for `String`?
2. **String Encoding**: Support UTF-16, UTF-32, or just UTF-8 for regular strings?
3. **String Interpolation**: Syntax like `"Hello, {name}!"`?
4. **Regex Support**: Include regex in stdlib?
5. **Small String Optimization**: Store small strings inline (like Rust)?
6. **OsString on macOS**: Treat as UTF-8 (like Unix) or have special handling?
7. **Path normalization**: Should `Path` type handle `.` and `..` automatically?

---

## Comparison with Rust

### Similarities
- Distinguish between owned (`String`) and borrowed (`str`) strings
- UTF-8 by default for regular strings
- Platform-specific `OsString` / `OsStr` for OS APIs
- Explicit memory management
- Zero-cost string views
- C string interop

### Differences
- Rust: Automatic memory management (ownership + borrowing)
- Maxon: Manual or RAII-based cleanup (simpler for learning)
- Rust: String slicing built into language
- Maxon: String slicing as library function (for now)
- Rust: Rich ecosystem of string crates
- Maxon: Minimal stdlib (growing)
- Rust: `OsString` is completely opaque
- Maxon: `OsString` exposes platform representation more directly

---

## Success Criteria

String implementation is complete when:
- ✅ Can create string views (`str`) from literals
- ✅ Can create owned strings (`String`) on heap
- ✅ Can append, concatenate, and manipulate strings
- ✅ Can slice strings safely with bounds checking
- ✅ Can convert to/from integers
- ✅ Can search and split strings
- ✅ Can interop with C strings (`cstr`)
- ✅ Can handle platform strings (`OsString` / `OsStr`)
- ✅ Can open files with Unicode paths on Windows (UTF-16)
- ✅ Can handle non-UTF-8 file paths on Unix
- ✅ UTF-8 aware character counting
- ✅ Memory is properly managed (no leaks)
- ✅ Comprehensive test coverage
- ✅ Full documentation

---

## References

- [Rust String Documentation](https://doc.rust-lang.org/std/string/struct.String.html)
- [Rust str Documentation](https://doc.rust-lang.org/std/primitive.str.html)
- [Rust OsString Documentation](https://doc.rust-lang.org/std/ffi/struct.OsString.html)
- [Rust OsStr Documentation](https://doc.rust-lang.org/std/ffi/struct.OsStr.html)
- [UTF-8 Specification](https://en.wikipedia.org/wiki/UTF-8)
- [Windows Unicode APIs](https://docs.microsoft.com/en-us/windows/win32/intl/unicode-in-the-windows-api)
- Maxon's `float-implementation-plan.md` (similar approach)
