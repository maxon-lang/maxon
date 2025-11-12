# Maxon String Formatting Implementation Plan (Zig-style)

## Overview

Implement a compile-time string formatting system inspired by Zig's `std.fmt`, adapted for Maxon's capabilities with **labeled placeholders**. The system will provide type-safe, efficient string formatting where:
- **Every placeholder must have a label** (named argument reference)
- **Every parameter must be used at least once** (no unused arguments)
- **Labels can be reused** (same parameter can be formatted multiple times)

## Core Design Principles

1. **Compile-time validation**: All format strings must be validated at compile time
2. **Labeled placeholders**: Every placeholder must reference a named parameter: `{name}`, `{count:d}`, etc.
3. **All parameters must be used**: Error if any parameter is never referenced
4. **Labels can be reused**: Same parameter can appear multiple times: `{x}...{x}`
5. **Type-aware formatting**: Different formatting based on argument type
6. **No runtime format string parsing**: Format string is analyzed during compilation
7. **Zero-cost abstractions**: Generated code should be as efficient as hand-written formatting

## Format Syntax

### Labeled Placeholder Syntax

**All placeholders must have a label (parameter name)**:
- `{name}` - Default formatting for parameter `name`
- `{name:s}` - String formatting for parameter `name`
- `{value:d}` - Decimal integer formatting for parameter `value`
- `{addr:x}` - Hexadecimal lowercase formatting for parameter `addr`
- `{addr:X}` - Hexadecimal uppercase formatting for parameter `addr`
- `{flags:b}` - Binary formatting for parameter `flags`
- `{perms:o}` - Octal formatting for parameter `perms`
- `{letter:c}` - Character formatting for parameter `letter`
- `{{` - Escaped left brace (literal `{`)
- `}}` - Escaped right brace (literal `}`)

**Label Rules**:
1. Labels must be valid Maxon identifiers (alphanumeric + underscore)
2. Labels must match parameter names passed to the format function
3. Same label can be used multiple times: `{x} and {x} again`
4. Every parameter passed must be referenced at least once
5. Format specifier is optional: `{x}` uses default, `{x:d}` uses decimal

### Examples
```maxon
// Valid: all parameters used
fmt::format("{name} is {age} years old", name="Alice", age=30)

// Valid: parameter reused
fmt::format("{x} + {x} = {result}", x=5, result=10)

// Invalid: parameter 'y' never used
fmt::format("{x}", x=5, y=10)  // Compile error!

// Invalid: placeholder without label
fmt::format("{}", value=42)  // Compile error!

// Invalid: label 'z' not provided
fmt::format("{z}", x=5)  // Compile error!
```

### Future Extensions (Phase 2+)
- `{name:width}` - Field width specification
- `{value:0width}` - Zero-padded width
- `{num:.precision}` - Floating-point precision
- `{num:+}` - Always show sign
- `{num: }` - Space for positive numbers

## Implementation Phases

### Phase 1: Core Infrastructure (Foundation)

**Goal**: Establish the basic framework for format string parsing and validation.

#### 1.1 AST Extensions
- Add `FormatStringExprAST` node type
- Store format string literal
- Store map/array of named argument expressions: `{name: expression}`
- Store parsed format specifiers with labels: `{label, format_type, position}`
- Track which labels are used in the format string
- Track which parameters are provided

#### 1.2 Lexer Changes
- Recognize format string syntax (potential prefix like `f"..."` or use special function)
- Tokenize format string literals

#### 1.3 Parser Changes
- Parse format string expressions: `fmt::format("...", name=arg1, value=arg2, ...)`
- Parse named arguments (keyword arguments)
- Parse format specifiers with labels within strings: `{label}`, `{label:format}`
- Extract label names from placeholders
- Build AST nodes for format expressions
- Validate brace pairing at parse time

#### 1.4 Semantic Analyzer
- Extract all labels from placeholders in format string
- Validate every placeholder has a label
- Validate every label corresponds to a provided parameter
- Validate every provided parameter is used at least once
- Validate format specifiers match argument types for each label
- Detect escaped braces (`{{` and `}}`)
- Error reporting for:
  - Placeholder without label: `{}`
  - Label not provided: `{unknown_label}`
  - Parameter never used: `provided but not in format string`
  - Invalid format specifiers
  - Unclosed braces
  - Type mismatches between label's parameter type and format specifier

### Phase 2: Code Generation (Basic Types)

**Goal**: Generate efficient LLVM IR for basic formatting operations.

#### 2.1 Integer Formatting (`{}`, `{d}`)
- Extend existing `print(int)` functionality
- Generate inline integer-to-string conversion
- Support for both signed and unsigned integers

#### 2.2 String Formatting (`{s}`)
- Handle string literal arguments
- Handle string variable arguments
- Concatenation logic

#### 2.3 Hexadecimal Formatting (`{x}`, `{X}`)
- Integer to hex conversion (lowercase)
- Integer to hex conversion (uppercase)
- Proper padding for hex values

#### 2.4 Format String Assembly
- Calculate total output buffer size at compile time (when possible)
- Generate code to:
  1. Allocate output buffer
  2. Copy literal segments
  3. Format and insert argument values
  4. Return formatted string or write to output

### Phase 3: Advanced Formatting

**Goal**: Add more format specifiers and formatting options.

#### 3.1 Binary and Octal (`{b}`, `{o}`)
- Binary integer formatting
- Octal integer formatting

#### 3.2 Character Formatting (`{c}`)
- Single character output
- ASCII character support

#### 3.3 Pointer Formatting
- Pointer address formatting (as hex)
- Null pointer handling

### Phase 4: Runtime Formatting Function

**Goal**: Provide runtime formatting capability when compile-time optimization isn't possible.

#### 4.1 Format Parser Runtime
- Runtime format string parser for dynamic scenarios
- Error handling for malformed format strings

#### 4.2 Dynamic Argument Handling
- Type-tagged argument passing
- Runtime type checking

### Phase 5: Standard Library Integration

**Goal**: Integrate formatting into Maxon's stdlib structure.

#### 5.1 stdlib/fmt Structure
```
stdlib/fmt/
  ├── format.maxon       # Main formatting function
  ├── integer.maxon      # Integer formatting (already exists)
  ├── hex.maxon          # Hexadecimal formatting
  ├── binary.maxon       # Binary formatting
  ├── octal.maxon        # Octal formatting
  ├── string.maxon       # String manipulation
  └── buffer.maxon       # Buffer management utilities
```

#### 5.2 Public API
```maxon
namespace fmt 'fmt'
    // Main formatting function with named parameters
    // Example: format("{name} is {age}", name="Alice", age=30)
    function format(format_string string, ...) string
        // Implementation
    end 'format'
    
    // Write formatted output to buffer with named parameters
    // Example: format_to_buffer(buf, 100, "{x}", x=42)
    function format_to_buffer(buffer ptr, size int, format_string string, ...) int
        // Implementation
    end 'format_to_buffer'
    
    // Print formatted string to stdout with named parameters
    // Example: print("{msg}", msg="Hello")
    function print(format_string string, ...)
        // Implementation
    end 'print'
    
    // Print formatted string with newline, using named parameters
    // Example: println("value={v}", v=42)
    function println(format_string string, ...)
        // Implementation
    end 'println'
end 'fmt'
```

## Compiler Implementation Details

### Format String Analysis

The compiler should perform these steps during semantic analysis:

1. **Tokenize format string**:
   - Split into literal segments and placeholders
   - Record position of each placeholder
   - Validate brace pairing

2. **Placeholder extraction**:
   ```
   Input: "Value: {num}, hex: {num:x}, name: {person:s}"
   Params: num=42, person="Alice"
   
   Output: [
     {type: "literal", value: "Value: "},
     {type: "placeholder", label: "num", format: "default"},
     {type: "literal", value: ", hex: "},
     {type: "placeholder", label: "num", format: "hex"},
     {type: "literal", value: ", name: "},
     {type: "placeholder", label: "person", format: "string"}
   ]
   ```

3. **Label and parameter validation**:
   - Extract all unique labels from placeholders: `{"num", "person"}`
   - Extract all provided parameter names: `{"num", "person"}`
   - Verify every label has a corresponding parameter
   - Verify every parameter is used at least once in format string
   - Verify each parameter's type is compatible with ALL format specifiers used for that label

4. **Type compatibility matrix**:
   ```
   Format      | Compatible Types
   ------------|------------------
   {label}     | int, char, ptr, string
   {label:d}   | int, char
   {label:x}   | int, char, ptr
   {label:X}   | int, char, ptr
   {label:b}   | int, char
   {label:o}   | int, char
   {label:c}   | int, char
   {label:s}   | string, ptr (as string)
   ```

### Code Generation Strategy

#### Option A: Inline Expansion (Preferred for simple cases)
Generate all formatting code inline, similar to how Zig does it:

```maxon
// Input:
fmt::print("x={x}, y={y}", x=10, y=20)

// Generated (conceptual):
function anonymous()
    let stdout = GetStdHandle(-11)
    WriteFile(stdout, "x=", 2, &written, 0)
    format_and_write_int(stdout, 10)  // x parameter
    WriteFile(stdout, ", y=", 4, &written, 0)
    format_and_write_int(stdout, 20)  // y parameter
end 'anonymous'
```

#### Option B: Buffer Building (For complex cases)
Calculate buffer size, allocate, build string, then output:

```maxon
// Input:
let message = fmt::format("Result: {value}", value=42)

// Generated (conceptual):
function anonymous() string
    var buffer [256]char  // Size calculated at compile time
    var pos = 0
    // Copy "Result: "
    copy_string(buffer, &pos, "Result: ")
    // Format integer (parameter 'value' = 42)
    pos = pos + format_int_to_buffer(42, &buffer[pos])
    return buffer as string
end 'anonymous'
```

### Error Messages

The compiler should provide clear error messages:

```
Error: Placeholder must have a label
  --> example.maxon:5:23
   |
 5 |     fmt::print("value: {}", num=42)
   |                        ^^
   |                        Use {label} or {label:format} syntax

Error: Unknown label 'z' in format string
  --> example.maxon:8:23
   |
 8 |     fmt::print("value: {z}", x=10, y=20)
   |                         ^
   |                         Label 'z' not found in parameters

Error: Parameter 'y' is never used
  --> example.maxon:12:37
   |
12 |     fmt::print("x={x}", x=10, y=20)
   |                                ^^^^
   |                                This parameter is not referenced in the format string

Error: Format specifier {name:x} requires integer type, got string
  --> example.maxon:15:28
   |
15 |     fmt::print("hex: {name:x}", name="Alice")
   |                       ^^^^^^^^   ^^^^^^^^^^^^
   |                       |          This is type 'string'
   |                       Expected 'int' for hex formatting

Error: Parameter 'value' used with incompatible format specifiers
  --> example.maxon:20:15
   |
20 |     fmt::print("{value:d} and {value:s}", value=42)
   |                ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
   |                'value' is type 'int' but used with both ':d' (valid) and ':s' (invalid)
```

## Test Fragments to Implement

### Category: Basic Formatting

#### Test 1: `format-basic-integer.test`
```maxon
function main() int
    let result = fmt::format("{num}", num=42)
    // Should produce "42"
    return 0
end 'main'
```

#### Test 2: `format-multiple-integers.test`
```maxon
function main() int
    fmt::println("x={x}, y={y}, z={z}", x=10, y=20, z=30)
    return 0
end 'main'
```

#### Test 3: `format-with-string.test`
```maxon
function main() int
    let name = "Maxon"
    fmt::println("Hello, {name:s}!", name=name)
    return 0
end 'main'
```

#### Test 4: `format-mixed-types.test`
```maxon
function main() int
    let count = 5
    let items_name = "items"
    fmt::println("Found {count} {name:s}", count=count, name=items_name)
    return 0
end 'main'
```

### Category: Format Specifiers

#### Test 5: `format-hex-lowercase.test`
```maxon
function main() int
    fmt::println("0x{val:x}", val=255)
    // Should output: 0xff
    return 0
end 'main'
```

#### Test 6: `format-hex-uppercase.test`
```maxon
function main() int
    fmt::println("0x{val:X}", val=255)
    // Should output: 0xFF
    return 0
end 'main'
```

#### Test 7: `format-binary.test`
```maxon
function main() int
    fmt::println("binary: {num:b}", num=10)
    // Should output: binary: 1010
    return 0
end 'main'
```

#### Test 8: `format-octal.test`
```maxon
function main() int
    fmt::println("octal: {num:o}", num=64)
    // Should output: octal: 100
    return 0
end 'main'
```

#### Test 9: `format-character.test`
```maxon
function main() int
    fmt::println("char: {ch:c}", ch=65)
    // Should output: char: A
    return 0
end 'main'
```

### Category: Escaped Braces

#### Test 10: `format-escaped-braces.test`
```maxon
function main() int
    fmt::println("Use {{}} for placeholders, like {{{num}}}", num=42)
    // Should output: Use {} for placeholders, like {42}
    return 0
end 'main'
```

#### Test 11: `format-only-escaped-braces.test`
```maxon
function main() int
    fmt::println("{{no}} {{placeholders}}")
    // Should output: {no} {placeholders}
    return 0
end 'main'
```

### Category: Error Cases

#### Test 12: `format-error-no-label.test`
```maxon
function main() int
    fmt::println("value: {}", num=42)
    // Should fail at compile time: placeholder without label
    return 0
end 'main'
```

#### Test 13: `format-error-unknown-label.test`
```maxon
function main() int
    fmt::println("x={unknown}", num=10)
    // Should fail at compile time: label 'unknown' not in parameters
    return 0
end 'main'
```

#### Test 14: `format-error-unused-param.test`
```maxon
function main() int
    fmt::println("x={x}", x=10, y=20)
    // Should fail at compile time: parameter 'y' is never used
    return 0
end 'main'
```

#### Test 15: `format-error-type-mismatch.test`
```maxon
function main() int
    let name = "test"
    fmt::println("value={name:d}", name=name)
    // Should fail: {name:d} expects int, got string
    return 0
end 'main'
```

#### Test 16: `format-error-unclosed-brace.test`
```maxon
function main() int
    fmt::println("value={num", num=42)
    // Should fail: unclosed brace
    return 0
end 'main'
```

#### Test 17: `format-error-unopened-brace.test`
```maxon
function main() int
    fmt::println("value=num}", num=42)
    // Should fail: unmatched closing brace
    return 0
end 'main'
```

#### Test 18: `format-error-invalid-specifier.test`
```maxon
function main() int
    fmt::println("value={num:q}", num=42)
    // Should fail: unknown format specifier 'q'
    return 0
end 'main'
```

### Category: Edge Cases

#### Test 19: `format-empty-string.test`
```maxon
function main() int
    fmt::println("")
    // Should output empty line
    return 0
end 'main'
```

#### Test 20: `format-no-placeholders.test`
```maxon
function main() int
    fmt::println("No formatting here")
    // Should output: No formatting here
    return 0
end 'main'
```

#### Test 21: `format-label-reuse.test`
```maxon
function main() int
    fmt::println("{x} + {x} = {result}", x=5, result=10)
    // Should output: 5 + 5 = 10
    // Label 'x' is used twice
    return 0
end 'main'
```

#### Test 22: `format-multiple-formats-same-label.test`
```maxon
function main() int
    fmt::println("dec: {val:d}, hex: {val:x}, bin: {val:b}", val=15)
    // Should output: dec: 15, hex: f, bin: 1111
    // Same label 'val' with different format specifiers
    return 0
end 'main'
```

#### Test 23: `format-negative-numbers.test`
```maxon
function main() int
    fmt::println("negative: {num}, hex: {neg:x}", num=-42, neg=-1)
    // Should output: negative: -42, hex: ffffffff (or appropriate)
    return 0
end 'main'
```

#### Test 24: `format-zero.test`
```maxon
function main() int
    let z = 0
    fmt::println("zero: {z}, {z:x}, {z:b}, {z:o}", z=z)
    // Should output: zero: 0, 0, 0, 0
    return 0
end 'main'
```

### Category: Complex Formatting

#### Test 25: `format-nested-expressions.test`
```maxon
function main() int
    fmt::println("calc: {result}", result=10 + 5 * 2)
    // Should output: calc: 20
    return 0
end 'main'
```

#### Test 26: `format-function-call.test`
```maxon
function double(x int) int
    return x * 2
end 'double'

function main() int
    fmt::println("doubled: {result}", result=double(21))
    // Should output: doubled: 42
    return 0
end 'main'
```

#### Test 27: `format-multiple-lines.test`
```maxon
function main() int
    fmt::println("Line 1: {n}", n=1)
    fmt::println("Line 2: {n}", n=2)
    fmt::println("Line 3: {n}", n=3)
    return 0
end 'main'
```

### Category: Buffer Operations

#### Test 28: `format-to-buffer.test`
```maxon
function main() int
    var buffer [100]char
    let written = fmt::format_to_buffer(&buffer, 100, "value={val}", val=42)
    // written should be 8 (length of "value=42")
    return 0
end 'main'
```

#### Test 29: `format-return-string.test`
```maxon
function main() int
    let message = fmt::format("count={cnt}", cnt=10)
    // message should be usable as a string
    return 0
end 'main'
```

### Category: Pointer Formatting

#### Test 30: `format-pointer.test`
```maxon
function main() int
    var x = 42
    fmt::println("address: {addr:x}", addr=&x as int)
    return 0
end 'main'
```

#### Test 31: `format-null-pointer.test`
```maxon
function main() int
    let null_ptr = 0 as ptr
    fmt::println("ptr: {p:x}", p=null_ptr as int)
    // Should output: ptr: 0
    return 0
end 'main'
```

### Category: String Literal Formatting

#### Test 32: `format-string-with-special-chars.test`
```maxon
function main() int
    fmt::println("tab:\t newline:\n quote:\" backslash:\\")
    return 0
end 'main'
```

#### Test 33: `format-all-params-used-via-reuse.test`
```maxon
function main() int
    // Both params used, one multiple times
    fmt::println("{a} {b} {a}", a=1, b=2)
    // Should output: 1 2 1
    return 0
end 'main'
```

## Implementation Checklist

### Phase 1: Foundation
- [ ] Define `FormatStringExprAST` in `ast.h`
- [ ] Add format string parsing to `parser.cpp`
- [ ] Implement format string tokenizer/analyzer
- [ ] Add semantic validation in `semantic_analyzer.cpp`
- [ ] Write error messages for format validation
- [ ] Create test fragments 12-17 (error cases) to validate error detection

### Phase 2: Basic Formatting
- [ ] Implement integer formatting code generation
- [ ] Implement string formatting code generation
- [ ] Create `stdlib/fmt/format.maxon` basic structure
- [ ] Create test fragments 1-4 (basic formatting)
- [ ] Create test fragments 18-22 (edge cases)

### Phase 3: Format Specifiers
- [ ] Implement hex formatting (lowercase and uppercase)
- [ ] Implement binary formatting
- [ ] Implement octal formatting
- [ ] Implement character formatting
- [ ] Create test fragments 5-9 (format specifiers)

### Phase 4: Escaped Braces
- [ ] Implement `{{` and `}}` escape sequences
- [ ] Create test fragments 10-11 (escaped braces)

### Phase 5: Advanced Features
- [ ] Implement expression arguments (nested expressions, function calls)
- [ ] Implement buffer formatting functions
- [ ] Create test fragments 23-27 (complex formatting, buffers)

### Phase 6: Additional Types
- [ ] Implement pointer formatting
- [ ] Implement string literal with special characters
- [ ] Create test fragments 28-30 (pointers, special chars)

### Phase 7: Documentation
- [ ] Write `docs/Content/string-formatting.md`
- [ ] Add examples to documentation
- [ ] Generate documentation with `make docs`
- [ ] Update README with formatting examples

### Phase 8: Performance Optimization
- [ ] Profile generated code
- [ ] Optimize buffer allocation
- [ ] Optimize literal string concatenation
- [ ] Benchmark against manual formatting

## Technical Considerations

### Memory Management
- **Stack allocation**: For format strings with known size at compile time
- **Heap allocation**: For dynamic or large format strings (future consideration)
- **Buffer reuse**: Consider allowing user-provided buffers for zero-allocation formatting

### Type System Integration
- Format specifiers should respect Maxon's type system
- Potential for custom formatters for user-defined types (future)
- Integration with existing `print()` function infrastructure

### Performance Goals
- **Zero overhead**: Formatting should be as fast as manual string building
- **Compile-time optimization**: Eliminate all format string parsing at runtime
- **Minimal code size**: Don't generate bloated LLVM IR

### Compatibility
- Must work with existing Maxon string handling
- Must integrate with existing `stdlib/fmt/integer.maxon` functions
- Should not break existing `print(int)` functionality

## Success Criteria

1. **All 30 test fragments pass** their respective tests
2. **Error detection**: All 6 error test cases produce appropriate compile-time errors
3. **Performance**: Generated code is comparable to hand-written formatting
4. **Documentation**: Complete documentation with examples
5. **Type safety**: No runtime type errors possible
6. **Usability**: Easy to use syntax that feels natural in Maxon

## Future Enhancements (Post-MVP)

1. **Float formatting**: `{val:f}`, `{val:e}`, `{val:g}` specifiers
2. **Width and precision**: `{val:10}`, `{num:5.2f}` style formatting
3. **Alignment**: Left, right, center alignment
4. **Custom formatters**: User-defined type formatting
5. **Compile-time format validation macros**
6. **Locale-aware formatting**
7. **Unicode support**
8. **Format string variables** (with compile-time constant requirement)

## Related Work

- **Zig std.fmt**: Inspiration for compile-time validation
- **Rust formatting**: Inspiration for named placeholders and excellent error messages (`println!("{name}", name=value)`)
- **Python f-strings**: User-friendly syntax (though Python allows unlabeled positional args)
- **C++ std::format**: Modern C++ approach with positional arguments
- **Maxon's approach**: Combines Zig's compile-time safety with Rust's named parameter style, with the added requirement that ALL parameters must be used

## Notes

- This design prioritizes **compile-time safety** over flexibility
- All errors should be caught at compile time when possible
- The goal is zero-cost abstractions, not runtime flexibility
- Format strings must be compile-time constants (like Zig)
- **Every placeholder must have a label** - no positional arguments
- **Every parameter must be used at least once** - prevents accidental unused arguments
- **Labels can be reused** - same value can be formatted multiple times in different ways
- This is stricter than Zig (which allows positional `{}`) but provides better error detection

---

**Document Version**: 1.0  
**Created**: 2025-11-11  
**Last Updated**: 2025-11-11
