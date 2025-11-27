# Swift-Style String Implementation Plan for Maxon

## Overview

This document outlines a plan to implement strings in Maxon following Swift's design philosophy. Swift's string implementation is renowned for its Unicode correctness, performance optimizations, and elegant API design.

## Swift String Key Features

### 1. **Unicode-First Design**
- All strings are valid Unicode (UTF-8 encoded internally)
- Character boundaries respect grapheme clusters (e.g., emoji with skin tone modifiers count as one character)
- Non-localized by default (machine-readable semantics)
- Localized operations require explicit locale parameter

### 2. **Small String Optimization (SSO)**
- Strings ≤15 bytes stored inline (no heap allocation)
- Encoded in the String value itself (typically 16 bytes total on 64-bit)
- Dramatically improves performance for short strings

### 3. **Copy-on-Write (COW)**
- Multiple strings can share the same heap storage
- Storage is copied only when modified
- Reference counted for automatic memory management
- Efficient for passing strings around

### 4. **string vs Substring**
- `string`: Owned, heap-allocated string type
- `Substring`: View into another string's storage (avoids copies when slicing)
- `Substring` automatically converts to `string` when needed (with copy)
- Prevents accidental memory leaks from long-lived substrings

### 5. **Multiple Views**
- Default view: Collection of grapheme clusters (user-perceived characters)
- `.chars`: Iterator over `char` (Unicode scalars - individual Unicode code points)
- `.bytes`: Iterator over `byte` (raw bytes - UTF-8 code units, 8-bit unsigned integers)
- `.utf16`: Iterator over UTF-16 code units (16-bit values)
- Each view provides different iteration semantics

## Design Goals for Maxon

1. **Unicode Correctness**: UTF-8 encoding, proper grapheme cluster handling
2. **Performance**: Small string optimization, copy-on-write
3. **Safety**: No buffer overflows, proper memory management
4. **Simplicity**: Clear distinction between owned strings and views
6. **Zero-cost Abstractions**: High-level API with low-level performance

## Proposed Type System

### Core Types

| Type | Purpose | Storage | Owned | Mutable | Size |
|------|---------|---------|-------|---------|------|
| `string` | Owned UTF-8 string | Inline or heap (COW) | Yes | Yes | 16 bytes |
| `char` | Extended Grapheme Cluster | Inline or heap (SSO) | Yes | No | 16 bytes |
| `byte` | Single byte | 8-bit unsigned | Yes | Yes | 1 byte |

Note: The separate `substring` type has been removed. String slicing operations return immutable `string` copies instead.

### Type 1: `string` (Owned UTF-8 string)

**Memory Layout** (16 bytes on 64-bit):
```
Small String (SSO - capacity ≤15 bytes):
[byte 0-14: UTF-8 data]
[byte 15: 0xxxxxxx (MSB=0 indicates small, lower 7 bits = remaining capacity)]

Large String (heap-allocated):
[bytes 0-7: pointer to heap buffer (tagged)]
[bytes 8-11: count (length in bytes)]
[bytes 12-15: capacity and flags]
```

**Characteristics**:
- UTF-8 encoded
- Small strings (≤15 bytes): stored inline, no allocation
- Large strings: heap-allocated with COW semantics
- Automatically switches between small and large representation
- Reference counting for shared storage
- Null-terminated for C interop (capacity includes null byte)

**Operations**:
```maxon
// Construction
var s = "hello"                           // String literal
var s2 = ""                               // Empty string

// Querying
var len = s.count()                       // Length in characters
var isEmpty = s.is_empty()                // Check if empty

// Modification (in-place if uniquely owned)
s.append("world")                         // Append string
s = ""                                 // Remove all content
s.reserve(100)                            // Ensure capacity

// Substrings
var sub = s[0..5]                   // Create view [0..5)
var sub2 = s[5..]                // View from character 5 to end

// Comparison
var eq = s.equals(s2)                     // Equality
var cmp = s.compare(s2)                   // -1, 0, or 1

```

### Type 2: `char` (Extended Grapheme Cluster)

**Memory Layout** (16 bytes on 64-bit):
Uses identical SSO layout as `string`:

```
Small Char (MSB of byte 15 = 0):
[bytes 0-14: UTF-8 data (inline)]
[byte 15: remaining capacity (15 - length)]

Large Char (MSB of byte 15 = 1):
[bytes 0-7: pointer to heap buffer]
[bytes 8-11: count (length in bytes)]
[bytes 12-15: capacity | 0x80000000]
```

**Characteristics**:
- Represents an Extended Grapheme Cluster (user-perceived character)
- NOT an alias for `byte` — UTF-8 characters can span multiple bytes
- Most characters fit in SSO (15 bytes covers vast majority of grapheme clusters)
- Complex emoji sequences (e.g., family emoji) may require heap allocation
- Immutable

**Examples**:
```maxon
var letter = 'A'           // 1 byte (ASCII)
var accent = 'é'           // 2 bytes (Latin Extended)
var chinese = '中'         // 3 bytes (CJK)
var emoji = '🎉'           // 4 bytes (Emoji)
var family = '👨‍👩‍👧‍👦'          // 25 bytes (Family emoji with ZWJ)
```

**Operations**:
```maxon
// String iteration yields char values
var s = "café"
for c in s 'chars'
    // c is 'c', 'a', 'f', 'é' (4 iterations, not 5 bytes)
end 'chars'

// Comparison
var eq = c1 == c2
var lt = c1 < c2

```

## Implementation Phases

### Phase 1: Core String Type with SSO

**Goals**: Implement basic `string` type with small string optimization

**Components**:
1. **String Structure** (`stdlib/string/string.maxon`)
   - 16-byte layout
   - SSO discrimination (check MSB of byte 15)
   - Inline storage for small strings (≤15 bytes)
   - Large string metadata (pointer, count, capacity)

2. **Basic Operations**:
   - Construction: empty, from literal
   - Querying: `count()`, `is_empty()`, `capacity()`
   - Internal: `is_small()`, `is_large()`

3. **Lexer/Parser Changes**:
   - String literals create `string` instances 
   - Add `string`, `substring` as new type keywords
   - Add `byte` type keyword (8-bit unsigned integer)
   - Remove old `STRING_TYPE` token (was alias for `ptr`)
   - `char` now represents a Unicode scalar/code point (variable-length UTF-8), not a single byte
   - Old `char` type (single byte) is replaced by `byte`

4. **Codegen**:
   - Emit LLVM struct type for `string` (16 bytes)
   - Handle string literal initialization (SSO or heap)
   - Implement inline SSO operations

**Test Files**:
- `language-tests/fragments/string-sso.test` - Small string operations
- `language-tests/fragments/string-literal.test` - String literal construction

### Phase 2: Heap Allocation and Growth

**Goals**: Add heap allocation for large strings

**Components**:
1. **Heap Management**:
   - `string._allocate(capacity)` - Allocate heap buffer
   - `string._deallocate()` - Free heap buffer
   - Null-termination for C interop

2. **Growth Operations**:
   - `append(str String)` - Append string
   - `append_char(c char)` - Append single byte
   - `reserve(cap int)` - Ensure capacity
   - Automatic transition from small to large

3. **Internal Helpers**:
   - `_grow(new_cap int)` - Reallocate buffer
   - `_ensure_capacity(cap int)` - Ensure space available

**Test Files**:
- `language-tests/fragments/string-append.test` - Appending strings
- `language-tests/fragments/string-growth.test` - Small to large transition

### Phase 3: Copy-on-Write (COW)

**Goals**: Implement reference counting and COW semantics

**Components**:
1. **Reference Counting**:
   - Heap buffer format: `[refcount: int][capacity: int][data: bytes...]`
   - `_retain()` - Increment refcount
   - `_release()` - Decrement refcount, free if zero
   - Automatic retain/release in assignments

2. **COW Semantics**:
   - `_make_unique()` - Copy buffer if refcount > 1 before mutation
   - Shared strings can be copied cheaply
   - Mutations trigger copy only when shared

3. **String Operations with COW**:
   - Assignment: increment refcount
   - Mutation: check uniqueness, copy if needed
   - Destruction: decrement refcount

**Test Files**:
- `language-tests/fragments/string-cow.test` - Copy-on-write behavior
- `language-tests/fragments/string-copy.test` - String copies and sharing

### Phase 4: String Slicing

**Goals**: Implement string slicing that returns immutable `string` references (views)

**Components**:
1. **String Slicing Operations**:
   - `string.slice(start, end)` → `string` (immutable reference to original)
   - `string.slice_from(start)` → `string` (immutable reference to original)
   - `string.slice_to(end)` → `string` (immutable reference to original)

2. **Semantics**:
   - Slicing returns an immutable string that references the original storage
   - No data copying — the slice is a view into the original string
   - Result is immutable to prevent modification of shared storage
   - Keeps original string alive (reference counting)

3. **Index Types**:
   - Indices operate on grapheme cluster boundaries
   - `s[0..5]` returns first 5 characters (not bytes)

**Test Files**:
- `language-tests/fragments/string-slice.test` - String slicing operations

### Phase 6: UTF-8 Validation and Iteration

**Goals**: Ensure Unicode correctness

**Components**:
1. **UTF-8 Validation**:
   - `_validate_utf8(bytes)` - Check if valid UTF-8
   - `string.from_bytes(ptr, len)` - Validate on construction

2. **Byte Iteration**:
   - `bytes()` - Iterator over bytes

3. **Error Handling**:
   - Invalid UTF-8 sequences replaced with U+FFFD (replacement character)
   - Or return error code from validation functions

**Test Files**:
- `language-tests/fragments/string-utf8.test` - UTF-8 validation
- `language-tests/fragments/string-iteration.test` - Byte iteration

### Phase 7: Advanced String Operations

**Goals**: Add common string manipulation functions

**Components**:
1. **Searching**:
   - `contains(needle)` - Check if contains substring
   - `starts_with(prefix)` - Check prefix
   - `ends_with(suffix)` - Check suffix
   - `find(needle)` → `int` - Find first occurrence (-1 if not found)

2. **Transformation**:
   - `to_upper()` - Convert to uppercase (ASCII only initially)
   - `to_lower()` - Convert to lowercase (ASCII only initially)
   - `trim()` - Remove leading/trailing whitespace

3. **Splitting/Joining**:
   - `split(sep)` → `[]Substring` - Split by separator
   - `join(parts, sep)` - Join strings with separator

**Test Files**:
- `language-tests/fragments/string-search.test` - String searching
- `language-tests/fragments/string-transform.test` - String transformations
- `language-tests/fragments/string-split.test` - Splitting and joining

## String Literal Implementation

### Literal Syntax
String literals are declared using double quotes:
```maxon
"hello world"           // Basic string literal
"hello\nworld"          // With escape sequences: \n \t \r \\ \" \0
"multi \"quoted\" text" // Escaped quotes
""                      // Empty string
```

### Type Inference Rules
1. **Default**: String literals have type `string` when context is ambiguous
4. **Assignment**: Type determined by variable declaration

### Lexer/Parser Behavior
- Lexer produces `STRING` token with the literal value (escape sequences processed)
- Parser creates `StringLiteralExprAST` node
- Semantic analyzer determines target type from context:
  - Variable declaration type
  - Function parameter type
  - Default to `string` if ambiguous
- Codegen emits appropriate code based on target type

### Codegen Strategy

**For `string` type:**
```cpp
if (length <= 15) {
    // Small String Optimization: embed in 16-byte value
    // [data bytes 0-14][tag byte 15]
    return inline_string_value;
} else {
    // Heap allocated: create global constant + runtime string initialization
    // Call string constructor that references the global data
    return heap_string_value;
}
```

## Memory Management Details

### Small String (SSO)
```
Bytes 0-14: UTF-8 data (inline)
Byte 15: [0][capacity] where capacity = 15 - length
         MSB = 0 indicates small string
         
Example: "hello" (5 bytes)
[h][e][l][l][o][0][0][0][0][0][0][0][0][0][0][0b00001010]
 0  1  2  3  4  5  6  7  8  9 10 11 12 13 14    15
                                              ^^
                                              MSB=0 (small)
                                              Remaining cap = 10
```

### Large String (Heap)
```
Bytes 0-7: Pointer to heap buffer (with tag bit cleared)
Bytes 8-11: Count (number of bytes of UTF-8 data, not including null)
Bytes 12-15: Capacity and flags
             [capacity: 31 bits][is_large_flag: 1 bit (MSB=1)]

Heap buffer format:
[refcount: int32][capacity: int32][...UTF-8 data...][0 (null terminator)]
```

### Reference Counting Rules
1. **Creation**: refcount = 1
2. **Copy/Assignment**: increment refcount
3. **Mutation**: if refcount > 1, copy buffer first (COW), else mutate in-place
4. **Destruction**: decrement refcount, free if zero

## Compiler Implementation Details

### AST Changes (`maxon-bin/ast.h`)
```cpp
// New string types
enum class StringType {
    String,      // Owned string type
    Substring,   // String view type
};

// String literal now needs context about expected type
class StringLiteralExprAST : public ExprAST {
public:
    std::string value;
    StringType targetType;  // Determined during semantic analysis
    
    StringLiteralExprAST(const std::string& val, int l = 0, int c = 0)
        : ExprAST(l, c), value(val), targetType(StringType::String) {}
};
```

### Lexer Changes (`maxon-bin/lexer.h`, `lexer.cpp`)
```cpp
// Add new keywords to TokenType enum
enum class TokenType {
    // ... existing tokens ...
    STRING,      // string type keyword (new owned string type)
    SUBSTRING,   // substring type keyword
    CHAR,        // char type keyword (Unicode scalar/code point, variable-length)
    BYTE,        // byte type keyword (8-bit unsigned)
};

// Update keyword map
static const std::unordered_map<std::string, TokenType> keywords = {
    // ... existing keywords ...
    {"string", TokenType::STRING},
    {"substring", TokenType::SUBSTRING},
    {"byte", TokenType::BYTE},
};
```

### Semantic Analyzer Changes (`maxon-bin/semantic_analyzer.cpp`)
```cpp
// Update type checking to understand new string types
bool SemanticAnalyzer::typesMatch(const std::string& type1, const std::string& type2) {
    // String types
    if (type1 == "string" && type2 == "string") return true;
    if (type1 == "substring" && type2 == "substring") return true;
    
    // substring implicitly converts to string
    if (type1 == "string" && type2 == "substring") return true;
    
    return type1 == type2;
}
```

### Codegen Changes (`maxon-bin/codegen.cpp`)
```cpp
// Add string type to type mapping
static llvm::Type* getTypeFromString(llvm::LLVMContext& context, 
                                     const std::string& typeStr) {
    if (typeStr == "string") {
        // 16-byte struct
        llvm::Type* i64 = llvm::Type::getInt64Ty(context);
        return llvm::StructType::get(context, {i64, i64});
    } else if (typeStr == "substring") {
        // 24-byte struct  
        llvm::Type* ptr = llvm::PointerType::get(context, 0);
        llvm::Type* i64 = llvm::Type::getInt64Ty(context);
        return llvm::StructType::get(context, {ptr, i64, i64});
    } else if (typeStr == "char") {
        // Unicode scalar/code point (variable-length UTF-8)
        // Represented as substring into parent string
        llvm::Type* ptr = llvm::PointerType::get(context, 0);
        llvm::Type* i32 = llvm::Type::getInt32Ty(context);
        return llvm::StructType::get(context, {ptr, i32});
    } else if (typeStr == "byte") {
        // Single byte (8-bit unsigned integer)
        return llvm::Type::getInt8Ty(context);
    }
    // ... existing types ...
}

// Generate String literal initialization
llvm::Value* CodeGenerator::generateStringLiteral(StringLiteralExprAST* expr) {
    if (expr->targetType == StringType::String) {
        // New: create string value with SSO or heap allocation
        size_t len = expr->value.length();
        
        if (len <= 15) {
            // Small string optimization - store inline
            return generateSmallStringLiteral(expr->value);
        } else {
            // Large string - allocate on heap
            return generateLargeStringLiteral(expr->value);
        }
    }
}

llvm::Value* CodeGenerator::generateSmallStringLiteral(const std::string& str) {
    // Create 16-byte array with string data and SSO tag
    llvm::Type* stringType = getTypeFromString(context, "string");
    llvm::Value* stringValue = llvm::UndefValue::get(stringType);
    
    // Pack bytes 0-14 with string data, byte 15 with SSO tag
    // Return the constructed string value
    // ... implementation details ...
}
```

## Standard Library Structure

```
stdlib/
  string/
    string.maxon         # String type and core operations
    substring.maxon      # Substring type and operations  
    char.maxon           # Unicode scalar/code point type and operations
    byte.maxon           # Byte type
    utf8.maxon           # UTF-8 validation and iteration
    operations.maxon     # Advanced string operations (search, split, etc.)
```

## Migration Path

### Phase 1: Implement New Types
- Introduce `string`, `substring`, `cstr` types
- Remove `STRING_TYPE` token (old alias for `ptr`)
- String literals default to `string` type instead of `ptr`

### Phase 3: Full Adoption
- Migrate all examples to use new string types
- Update standard library to use proper string types
- All string operations use new type system

## Testing Strategy

### Unit Tests (Language Tests)
- Test each phase incrementally
- Verify SSO behavior (small vs large threshold)
- Test COW semantics (shared vs unique modifications)
- Test Substring behavior and conversions
- Test C interop (cstr conversions)
- Test UTF-8 validation
- Test edge cases (empty strings, single char, boundary conditions)

### Integration Tests
- Real-world string processing examples
- Performance benchmarks (compare with C strings)
- Memory leak detection (refcount verification)

### Benchmarks
- String creation (small vs large)
- String copying (with COW)
- String appending (growth patterns)
- Substring slicing (no-copy verification)
- C string conversions

## Documentation Updates

### User-Facing Documentation
1. **New String Types Guide** (`docs/Content/Strings.md`)
   - Overview of string, substring
   - When to use each type
   - String literal behavior
   - Common operations
   - C interop patterns
   - char type (Unicode scalars) vs byte type (raw bytes)
   - String views: .chars, .bytes, .utf16

2. **String Operations Reference** (`docs/Content/String-Operations.md`)
   - Construction methods
   - Querying methods
   - Mutation methods
   - Conversion methods
   - Comparison methods

### Internal Documentation
- Memory layout diagrams
- Reference counting algorithm
- COW implementation details
- SSO threshold rationale
- UTF-8 validation algorithm

## Open Questions and Future Work

### Phase 8+: Advanced Features

1. **Character/Grapheme Support**
   - Iterator over grapheme clusters (not just Unicode scalars)
   - Proper Unicode segmentation (extended grapheme clusters)
   - `char` type represents a Unicode scalar (single code point, variable-length UTF-8)
   - `byte` type represents a single byte (8-bit unsigned, for raw UTF-8 access)
   - Grapheme clusters may be composed of multiple `char` values (e.g., base + combining marks)
   - Examples: "é" can be 1 char (U+00E9) or 2 chars (U+0065+U+0301), "👨‍👩‍👧‍👦" (family emoji) is multiple chars forming one grapheme

2. **Formatting**
   - String interpolation: `"Hello {name}!"` 
   - Escape curly brackets: `"This is \{not} interpolated"`
   - Format strings: `string.format("Value: {value}")`
   - replaces stdlib `fmt` module

3. **Localization**
   - Locale-aware comparison
   - Case conversion with locale
   - Collation support

5. **String Builder**
   - Efficient way to build strings with many concatenations
   - Pre-allocate capacity to avoid repeated reallocations

6. **Performance Optimizations**
   - SIMD operations for searching/validation
   - Rope data structure for very large strings
   - String interning for common strings

### Questions to Resolve

1. **Should string.count() return byte count or character/grapheme count?**
	- character count

2. **How to handle invalid UTF-8?**
   - Swift: Replace with U+FFFD replacement character
   - Rust: Panic or return Result
   - **Proposal**: Replace with U+FFFD for from_cstr, validate in debug mode

3. **Should string indexing be allowed?**
   - Swift: No direct indexing (must use indices)
   - Rust: No direct indexing (iterate or use byte slices)
   - **Proposal**: No direct indexing initially, use `byte_at()` explicitly
