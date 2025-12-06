# Array Redesign Implementation Plan

## Overview

Replace raw array syntax `[]T` and `[N]T` with the unified `array of T` type backed by `_ManagedArray`. This aligns arrays with the `string` type's design pattern using `ExpressibleByArrayLiteral`.

## Progress

- [x] **Phase 1.1**: Add `ExpressibleByArrayLiteral` interface to `stdlib/interfaces.maxon`
- [x] **Phase 1.2**: Update `array.maxon` to use `_ManagedArray` internally
- [x] **Phase 2**: Parser changes for `array of T` syntax
  - [x] Add `array` and `of` keywords to lexer
  - [x] Add `parseTypeString()` and `parseTypeStringWithOptional()` to parser
  - [x] Update function parameter parsing to use unified type parser
  - [x] Update function return type parsing
  - [x] Update method parameter and return type parsing
  - [x] Update struct field type parsing
  - [x] Update interface method signature parsing
  - [x] Update struct `with` clause type parsing
  - [x] Update cast expression type parsing
- [x] **Phase 3**: Type system changes (already using `_ManagedArray<T>` format)
- [x] **Phase 4**: Semantic analyzer changes (working correctly)
- [x] **Phase 5**: Code generation changes (working correctly)
- [x] **Phase 6**: Spec & test updates
  - [x] Update `specs/arrays.md` - documentation and error messages
  - [x] Update `specs/command-line-args.md` - use `array of string`
  - [x] Update `specs/map-type.md` - internal storage docs
  - [x] Update `specs/format-int.md` - function signature
  - [x] Update `specs/format-float.md` - function signature
- [x] **Phase 7**: Stdlib updates
  - [x] Update `stdlib/fmt/integer.maxon` - `format_int_array`
  - [x] Update `stdlib/fmt/float.maxon` - `format_float_array`
  - [x] Update `stdlib/string/utf8.maxon` - `utf8_codepoint_count`, `utf8_decode`
  - [x] Update `stdlib/string/utf16.maxon` - `utf16_codepoint_count`
  - [x] Update `stdlib/string/_grapheme.maxon` - grapheme boundary functions
  - [x] Update `stdlib/string/string.maxon` - `string_from_characters`
  - [ ] Update `stdlib/collections/map.maxon` - internal storage (deferred, uses legacy syntax)
- [x] **Phase 8**: LSP & tooling updates
  - [x] Grammar generator already handles `array` and `of` keywords (in Type category)
  - [x] LSP hover now displays array types as `array of T` format
  - [x] LSP completion already suggests `array of` via keyword system

## New Syntax

| Old Syntax | New Syntax | Description |
|------------|------------|-------------|
| `var arr = [5]int` | `var arr = array of 5 int` | Sized mutable array |
| `var arr = []int` | `var arr = array of int` | Empty mutable array |
| `let arr = [1, 2, 3]` | `let arr = [1, 2, 3]` | Immutable array (unchanged literal syntax) |
| `var arr = [1, 2, 3]` | `var arr = [1, 2, 3]` | Mutable array (unchanged literal syntax) |
| `[]int` (type) | `array of int` | Array type annotation |
| `[3]int` (type) | `array of int` | Array type (size not in type) |
| `args []string` | `args array of string` | Function parameter |
| `[][]int` | `array of array of int` | Nested arrays |

## Semantics

### Mutability
- `let arr = [1, 2, 3]` → immutable array, capacity=0, data in read-only section
- `var arr = [1, 2, 3]` → mutable array with COW semantics, heap-allocated

### Empty/Sized Arrays
- `var arr = array of int` → empty array, length=0, capacity=0
- `var buffer = array of 50 byte` → sized array, length=50, capacity=50, zero-initialized

### Type Inference
- `var arr = [1, 2, 3]` infers `array of int`
- `var arr array of int = [1, 2, 3]` explicit type annotation

---

## Implementation Phases

### Phase 1: Interface & Stdlib Foundation

#### 1.1 Add ExpressibleByArrayLiteral Interface
**File:** `stdlib/interfaces.maxon`

```maxon
// ExpressibleByArrayLiteral - types that can be initialized from array literals
// The compiler constructs a _ManagedArray and passes it to init.
// _ManagedArray is a compiler-internal opaque type with layout: { ptr buffer, i32 len, i32 capacity }
// - capacity = 0 for constant data (no heap cleanup needed)
// - capacity > 0 for heap-allocated data with refcount header
export interface ExpressibleByArrayLiteral uses Element
    function init(managed _ManagedArray uses Element) Self
end 'ExpressibleByArrayLiteral'
```

#### 1.2 Update array.maxon
**File:** `stdlib/collections/array.maxon`

Change internal storage from `var _data []Element` to `var _managed _ManagedArray uses Element`:

```maxon
export struct array uses Element is Iterable with Element, ExpressibleByArrayLiteral with Element
    var _managed _ManagedArray uses Element  // Compiler-managed opaque array storage
    var _iterIndex int

    // ExpressibleByArrayLiteral conformance
    function ExpressibleByArrayLiteral.init(managed _ManagedArray uses Element) array uses Element
        return array{_managed: managed, _iterIndex: 0}
    end 'init'

    // Update all methods to use _managed instead of _data
    // e.g., __managed_array_len(_managed) instead of __managed_array_len(_data)
```

**Methods to update:**
- `count()` - use `__managed_array_len(_managed)`
- `capacity()` - use `__managed_array_capacity(_managed)`
- `isEmpty()` - use `__managed_array_len(_managed) == 0`
- `first()`, `last()` - use `_managed[index]`
- `get()`, `set()` - use `_managed[index]` and `__managed_array_set_at(_managed, ...)`
- `push()`, `pop()` - use `__managed_array_*(_managed, ...)`
- `insert()`, `remove()` - use shift intrinsics on `_managed`
- `clear()`, `reserve()` - use `__managed_array_*(_managed, ...)`
- `Iterable.next()` - use `_managed[_iterIndex]`

---

### Phase 2: Parser Changes

#### 2.1 Type Parsing
**File:** `maxon-bin/parser/parser_type.cpp` (or relevant parser file)

Add parsing for `array of T` type syntax:

```cpp
// parseType() additions:
// When seeing "array" keyword followed by "of":
//   array of T           -> ArrayType(elementType=T, size=0)
//   array of N T         -> ArrayType(elementType=T, size=N)
//   array of array of T  -> ArrayType(elementType=ArrayType(T))
```

**Grammar:**
```
array_type := "array" "of" ( INTEGER type | type )
type := ... | array_type | ...
```

#### 2.2 Expression Parsing
**File:** `maxon-bin/parser/parser_expr.cpp`

Update array literal and constructor parsing:

```cpp
// parseArrayLiteral() - keep [1, 2, 3] syntax for literals
// Add: parseArrayConstructor() for "array of T" as expression
//   array of int         -> empty array constructor
//   array of 50 byte     -> sized array constructor
```

#### 2.3 Remove Legacy Syntax
Remove parsing of:
- `[N]T` sized array type syntax
- `[]T` unsized array type syntax
- `[N]type` as expression (replace with `array of N type`)

---

### Phase 3: Type System Changes

#### 3.1 Internal Type Representation
**File:** `maxon-bin/types/type_conversion.cpp`

Change internal type string format:
- Old: `[3]int`, `[]int`, `_ManagedArray<int>`
- New: `array of int` (unified format)

Update functions:
```cpp
bool TypeConversion::isArrayType(const std::string &type) {
    // Check for "array of " prefix
    return type.rfind("array of ", 0) == 0;
}

std::string TypeConversion::getArrayElementType(const std::string &arrayType) {
    // "array of int" -> "int"
    // "array of array of int" -> "array of int"
    if (arrayType.rfind("array of ", 0) == 0) {
        return arrayType.substr(9); // length of "array of "
    }
    return arrayType;
}

// Remove: isManagedArrayType(), isStaticArrayType() - unified to single array type
// Remove: Legacy [N]T and []T format handling
```

#### 3.2 Type Compatibility
**File:** `maxon-bin/types/type_conversion.cpp`

```cpp
bool TypeConversion::typesCompatible(const std::string &t1, const std::string &t2) {
    // array of int == array of int
    // array of array of int == array of array of int
    if (isArrayType(t1) && isArrayType(t2)) {
        return getArrayElementType(t1) == getArrayElementType(t2);
    }
    // ...
}
```

---

### Phase 4: Semantic Analyzer Changes

#### 4.1 Variable Declaration Analysis
**File:** `maxon-bin/semantic_analyzer/semantic_analyzer_stmt.cpp`

```cpp
// For let declarations with array literal:
//   let arr = [1, 2, 3]
// Mark as immutable, infer type as "array of int"

// For var declarations with array literal:
//   var arr = [1, 2, 3]
// Mark as mutable, infer type as "array of int"

// For var with array constructor:
//   var arr = array of int
//   var buffer = array of 50 byte
// Validate and set appropriate type
```

#### 4.2 Type Inference
**File:** `maxon-bin/semantic_analyzer/semantic_analyzer_expr.cpp`

```cpp
// analyzeExpression() for ArrayLiteralExprAST:
// Infer element type from first element, return "array of T"

// analyzeExpression() for ArrayConstructorExprAST (new):
// Return "array of T" with optional size
```

#### 4.3 ExpressibleByArrayLiteral Detection
**File:** `maxon-bin/semantic_analyzer/semantic_analyzer_expr.cpp`

When target type conforms to `ExpressibleByArrayLiteral`, allow array literal assignment:
```cpp
// var arr array of int = [1, 2, 3]
// Check if "array of int" conforms to ExpressibleByArrayLiteral
// Transform to: array.init(managed) call
```

---

### Phase 5: Code Generation Changes

#### 5.1 MIR Type Generation
**File:** `maxon-bin/codegen_mir.cpp`

Create `__ManagedArrayData` struct type:
```cpp
// Layout: { _buffer ptr, _len i32, _capacity i32 }
mir::MIRType *managedArrayDataType = module->getOrCreateStructType(
    "__ManagedArrayData",
    {mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getInt32()});
```

#### 5.2 Array Literal Code Generation
**File:** `maxon-bin/codegen_mir/codegen_mir_expr.cpp`

For `let arr = [1, 2, 3]` (immutable):
```cpp
// 1. Create global constant data for array elements
// 2. Create __ManagedArrayData with capacity=0 (constant flag)
// 3. Call array.init(managed) to construct array struct
```

For `var arr = [1, 2, 3]` (mutable):
```cpp
// 1. Allocate heap buffer with refcount header
// 2. Copy literal values to heap
// 3. Create __ManagedArrayData with capacity=N
// 4. Call array.init(managed) to construct array struct
```

#### 5.3 Array Constructor Code Generation
**File:** `maxon-bin/codegen_mir/codegen_mir_stmt_decl.cpp`

For `var arr = array of int`:
```cpp
// Create empty __ManagedArrayData: { null, 0, 0 }
// Call array.init(managed)
```

For `var buffer = array of 50 byte`:
```cpp
// Allocate 50-byte heap buffer
// Zero-initialize
// Create __ManagedArrayData: { ptr, 50, 50 }
// Call array.init(managed)
```

#### 5.4 ExpressibleByArrayLiteral Pattern
**File:** `maxon-bin/codegen_mir/codegen_mir_expr.cpp`

Follow `ExpressibleByStringLiteral` pattern (lines 68-140):
```cpp
// When assigning array literal to ExpressibleByArrayLiteral type:
// 1. Check structConformsTo for "ExpressibleByArrayLiteral"
// 2. Create _ManagedArray struct
// 3. Call Type.init(self=null, managed)
```

---

### Phase 6: Spec & Test Updates

#### 6.1 Update specs/arrays.md
- Document new `array of T` syntax
- Document `array of N T` sized syntax
- Document `let` vs `var` semantics
- Document nested `array of array of T`
- Update all code examples
- Update all test cases

#### 6.2 Update specs/command-line-args.md
- Change `args []string` to `args array of string`
- Update all test cases

#### 6.3 Regenerate Test Fragments
```bash
maxon extract-specs
maxon regen-fragments
```

---

### Phase 7: Stdlib Updates

#### 7.1 collections/map.maxon
```maxon
// Before:
var _keys []KeyType
var _values []ValueType
var _states []byte

// After:
var _keys array of  KeyType
var _values array of  ValueType
var _states array of  byte
```
Note: Map uses internal `_ManagedArray` directly, not the public `array of T` type.

#### 7.2 string/_grapheme.maxon
```maxon
// Before:
export function next_grapheme_boundary(data []byte, len int, offset int) int

// After:
export function next_grapheme_boundary(data array of byte, len int, offset int) int
```

#### 7.3 string/utf8.maxon
```maxon
// Before:
export function utf8_codepoint_count(data []byte, len int) int
export function utf8_decode(data []byte, offset int) int

// After:
export function utf8_codepoint_count(data array of byte, len int) int
export function utf8_decode(data array of byte, offset int) int
```

#### 7.4 string/utf16.maxon
```maxon
// Before:
export function utf16_codepoint_count(data []int, len int) int

// After:
export function utf16_codepoint_count(data array of int, len int) int
```

#### 7.5 fmt/integer.maxon
```maxon
// Before:
export function format_int_array(value int, buffer []byte) int

// After:
export function format_int_array(value int, buffer array of byte) int
```

#### 7.6 fmt/float.maxon
```maxon
// Before:
export function format_float_array(value float, buffer []byte, precision int) int

// After:
export function format_float_array(value float, buffer array of byte, precision int) int
```

#### 7.7 string/string.maxon
```maxon
// Before:
export function string_from_characters(buffer []byte, length int) string

// After:
export function string_from_characters(buffer array of byte, length int) string
```

---

### Phase 8: LSP & Tooling Updates

#### 8.1 Grammar Generator
**File:** `maxon-bin/grammar_generator.cpp`

Update TextMate grammar for:
- `array of T` type highlighting
- `array of N T` sized array highlighting

#### 8.2 LSP Completion
**File:** `maxon-bin/lsp/features/completion.cpp`

- Suggest `array of` after typing `array`
- Complete element types after `array of`

#### 8.3 LSP Hover
**File:** `maxon-bin/lsp/features/hover.cpp`

Display `array of T` type information on hover.

---

## Migration Notes

### Breaking Changes
None - the legacy `[]T` and `[N]T` syntax is still supported for backward compatibility.
New code should use the `array of T` syntax.

### Backward Compatibility
- Legacy syntax `[]T` and `[N]T` still works (parsed and converted to internal format)
- Array literals `[1, 2, 3]` unchanged
- Indexing `arr[i]` unchanged
- Methods `.push()`, `.pop()`, etc. unchanged
- LSP hover displays types in the new `array of T` format

---

## Testing Checklist

**Type Syntax (working):**
- [x] Function parameters: `function foo(arr array of int)` ✓
- [x] Type annotations: `var arr array of int`
- [x] Struct field types: using `array of T`
- [x] Interface method signatures: using `array of T`
- [x] Nested array types: `array of array of int` in type positions

**Array Creation (expression syntax unchanged):**
- [x] Sized array creation: `var buffer = [50]byte` (legacy syntax still used for expressions)
- [x] Immutable array literal: `let arr = [1, 2, 3]` ✓
- [x] Mutable array literal: `var arr = [1, 2, 3]` (note: push requires `[N]T` form)
- [x] Type inference from literal ✓

**Array Operations (all working via existing tests):**
- [x] Array methods: push, pop on dynamic arrays
- [x] Iteration with for-in
- [x] Command line args: `function main(args array of string)` ✓

**Note:** The `array of N T` syntax is only valid in type positions (parameters, annotations).
For creating sized arrays as expressions, use the legacy `[N]T` syntax: `var arr = [5]int`.

