# Floating-Point Implementation Plan for Maxon

## Overview
Implement floating-point support in Maxon to enable the spectral-norm benchmark and other numerical computations. This includes adding `float` type (64-bit), math library functions, and formatted output.

## Phase 1: Core Float Type Support

### 1.1 Lexer Changes (`maxon-bin/lexer.h`, `lexer.cpp`)
- [ ] Add `FLOAT` token type to `TokenType` enum
- [ ] Add float literal recognition (e.g., `3.14`, `1.0`, `0.5`, `2e10`)
- [ ] **Require leading zero**: `.5` is invalid, must be `0.5`
- [ ] Update `readNumber()` to detect decimal points and scientific notation
- [ ] Store float literals as `double` in Token structure
- [ ] Update keyword map to recognize `float` type keyword

**Test files:**
- `language-tests/fragments/float-literal.test` - basic float literals (with leading zero)
- `language-tests/fragments/float-literal-error.test` - reject `.5` without leading zero
- `language-tests/fragments/float-scientific.test` - scientific notation

### 1.2 AST Changes (`maxon-bin/ast.h`)
- [ ] Add `FloatExprAST` class (similar to `NumberExprAST`)
  ```cpp
  class FloatExprAST : public ExprAST {
  public:
      double value;
      FloatExprAST(double val, int l = 0, int c = 0) 
          : ExprAST(l, c), value(val) {}
  };
  ```
- [ ] Update `VarDeclStmtAST` and `PrototypeAST` to handle `float` type strings

### 1.3 Parser Changes (`maxon-bin/parser.cpp`, `parser.h`)
- [ ] Add `parseFloatLiteral()` function
- [ ] Update `parsePrimary()` to handle float literals
- [ ] Update `parseType()` to recognize `float` as valid type
- [ ] Update type inference to determine float vs int from literals
- [ ] Handle mixed int/float arithmetic (promotion rules)

**Test files:**
- `language-tests/fragments/float-variable.test` - float variable declaration
- `language-tests/fragments/float-arithmetic.test` - basic float operations
- `language-tests/fragments/float-comparison.test` - float comparisons

### 1.4 Semantic Analyzer Changes (`maxon-bin/semantic_analyzer.cpp`, `semantic_analyzer.h`)
- [ ] Add `float` to type system
- [ ] Implement type coercion rules (int → float is implicit and safe)
- [ ] Validate float operations (arithmetic, comparison)
- [ ] Check function signatures with float parameters/returns
- [ ] Ensure arrays can have float element type
- [ ] Validate use of conversion functions (round, trunc, floor, ceil)

**Test files:**
- `language-tests/fragments/float-type-inference.test` - type inference with floats
- `language-tests/fragments/float-int-promotion.test` - implicit int to float conversion
- `language-tests/fragments/float-array.test` - float arrays

### 1.5 Code Generator Changes (`maxon-bin/codegen.cpp`, `codegen.h`)
- [ ] Update `getLLVMType()` to return `Type::getDoubleTy()` for `float`
- [ ] Implement `codegen()` for `FloatExprAST`
- [ ] Update binary operators to use `CreateFAdd`, `CreateFSub`, `CreateFMul`, `CreateFDiv`
- [ ] Update comparisons to use `CreateFCmpOEQ`, `CreateFCmpONE`, `CreateFCmpOLT`, etc.
- [ ] Handle mixed int/float operations with implicit promotion (int → float with `CreateSIToFP`)
- [ ] Implement conversion functions: `round()`, `trunc()`, `floor()`, `ceil()`
- [ ] Update array allocation to always use heap (malloc)
- [ ] Track allocated arrays per scope for automatic cleanup
- [ ] Generate free calls at all scope exit points
- [ ] Update array indexing for float arrays

**Test files:
- `language-tests/fragments/float-binop.test` - float binary operations
- `language-tests/fragments/float-convert.test` - conversion functions (round, trunc, floor, ceil)
- `language-tests/fragments/float-mixed-arithmetic.test` - mixed int/float operations
- `language-tests/fragments/float-function-param.test` - functions with float parameters
- `language-tests/fragments/float-function-return.test` - functions returning float

## Phase 2: Math Functions via LLVM Intrinsics

### 2.1 Implement Math Keywords and Conversion Functions
Expose LLVM intrinsics directly as keywords in the language, plus type conversion functions.

**Math keywords to add:**
- `sqrt` - square root
- `abs` - absolute value  
- `floor` - floor function (returns int)
- `ceil` - ceiling function (returns int)
- `sin`, `cos`, `tan` - trigonometric functions
- `log`, `exp` - logarithm and exponential
- `pow` - power function

**Type conversion keywords:**
- `round(x float) int` - round to nearest integer
- `trunc(x float) int` - truncate float to integer (toward zero)
- `floor(x float) int` - floor function (returns int)
- `ceil(x float) int` - ceiling function (returns int)

### 2.2 Lexer Changes
- [ ] Add math function keywords to `TokenType` enum: `SQRT`, `ABS`, `FLOOR`, `CEIL`, etc.
- [ ] Add conversion keywords: `ROUND`, `TRUNC`
- [ ] Update keyword map in `Lexer::readIdentifier()` to recognize these keywords

### 2.3 Parser Changes
- [ ] Treat math keywords as special function calls in `parsePrimary()`
- [ ] Parse syntax: `sqrt(expression)`, `pow(base, exponent)`
- [ ] Create specialized AST nodes or use `CallExprAST` with special marker

### 2.4 Codegen Changes
- [ ] Detect math keyword calls in codegen
- [ ] Map to corresponding LLVM intrinsics:
  - `sqrt` → `Intrinsic::sqrt`
  - `abs` → `Intrinsic::fabs` (for float), `Intrinsic::abs` (for int)
  - `floor` → `Intrinsic::floor` then `CreateFPToSI` (returns int)
  - `ceil` → `Intrinsic::ceil` then `CreateFPToSI` (returns int)
  - `sin` → `Intrinsic::sin`
  - `cos` → `Intrinsic::cos`
  - `pow` → `Intrinsic::pow`
  - etc.
- [ ] Implement conversion functions:
  - `round(x)` → `Intrinsic::lround` or `Intrinsic::round` then `CreateFPToSI`
  - `trunc(x)` → `CreateFPToSI` (float to signed int)
- [ ] Use `Intrinsic::getDeclaration()` to get intrinsic function
- [ ] Generate call with appropriate arguments

**Example codegen for sqrt:**
```cpp
if (callee == "sqrt") {
    llvm::Function* sqrtFn = llvm::Intrinsic::getDeclaration(
        module.get(), 
        llvm::Intrinsic::sqrt, 
        {llvm::Type::getDoubleTy(context)}
    );
    return builder.CreateCall(sqrtFn, {arg});
}
```

**Test files:**
- `language-tests/fragments/sqrt-basic.test` - sqrt of perfect squares
- `language-tests/fragments/sqrt-precision.test` - sqrt accuracy test
- `language-tests/fragments/abs-float.test` - absolute value for floats
- `language-tests/fragments/floor-ceil.test` - floor and ceil functions (returning int)
- `language-tests/fragments/trig-functions.test` - sin, cos, tan
- `language-tests/fragments/round-basic.test` - round() conversion
- `language-tests/fragments/trunc-basic.test` - trunc() conversion

### 2.5 Usage Examples
```maxon
function example() float
    var x = 16.0
    var y = sqrt(x)              // Returns 4.0 (float)
    var z = pow(2.0, 3.0)        // Returns 8.0 (float)
    var a = abs(-5.5)            // Returns 5.5 (float)
    var b = floor(3.7)           // Returns 3 (int)
    return y + z + a
end 'example'

function conversions() int
    var f = 3.7
    var i1 = floor(f)            // Returns 3 (int)
    var i2 = ceil(f)             // Returns 4 (int)
    var i3 = trunc(-3.7)         // Returns -3 (int)
    var i4 = round(3.5)          // Returns 4 (int, rounds to nearest)
    var f2 = 42.0                // Explicit float literal (leading zero required)
    var f3 = 0.5                 // Valid: 0.5 (not .5)
    return i1 + i2 + i3 + i4
end 'conversions'
```

## Phase 3: Formatted Output (Extend Existing String Formatting)

### 3.1 Extend stdlib/fmt with Float Formatting
**Goal:** Add `format_float_array(value float, buffer []char, precision int)` similar to existing `format_int_array`

**File:** `stdlib/fmt/float.maxon`

### 3.2 Implementation Strategy
Float to string conversion algorithm:
1. Handle special cases: zero, negative
2. Extract integer part
3. Extract fractional part with specified precision
4. Format into buffer: `[-]integer.fraction`

**Example signature:**
```maxon
// Format a float to ASCII in a buffer
// Returns the number of bytes written
// Buffer must be at least 32 bytes
// precision = number of decimal places (0-15)
function format_float_array(value float, buffer [32]char, precision int) int
    // Implementation
end 'format_float_array'
```

### 3.3 Required Operations
To implement float formatting, we need:
- [ ] Convert float to int (truncate): `var intPart = trunc(value)`
- [ ] Subtract to get fractional part: `var fracPart = value - intPart` (int auto-promotes to float)
- [ ] Power of 10 for precision: `var scale = pow10(precision)` (helper function)
- [ ] Modulo and division for extracting digits

### 3.4 Helper Function for Power of 10
```maxon
function pow10(n int) float
    var result = 1.0
    var i = 0
    while i < n 'loop'
        result = result * 10.0
        i = i + 1
    end 'loop'
    return result
end 'pow10'
```

### 3.5 Create stdlib/io/print.maxon
Move `print()` from builtin to stdlib as a convenience function:
- Use `format_int_array()` for int values
- Use `format_float_array()` for float values (default precision 6)
- Write to stdout using existing stream functions
- Overload for different types or use type-based dispatch

**File:** `stdlib/io/print.maxon`
```maxon
// Print an integer to stdout
function print(value int) int
    var buffer [12]char = 0
    format_int_array(value, buffer)
    // Write to stdout using stdlib/fs/streams
    return 0
end 'print'

// Print a float to stdout with default precision
function print(value float) int
    var buffer [32]char = 0
    format_float_array(value, buffer, 6)
    // Write to stdout using stdlib/fs/streams
    return 0
end 'print'
```

**Test files:**
- `language-tests/fragments/format-float.test` - format_float_array basic test
- `language-tests/fragments/format-float-precision.test` - various precisions
- `language-tests/fragments/format-float-negative.test` - negative numbers
- `language-tests/fragments/format-float-zero.test` - zero handling

## Phase 4: Automatic Memory Management for Arrays

### 4.1 Array Allocation Model
**Design:** All arrays are heap-allocated and automatically freed when they go out of scope

**Current:** Fixed-size stack arrays: `var arr [100]int`
**New:** 
- Fixed-size heap arrays: `var arr [100]int` (allocated on heap, freed at scope end)
- Dynamic-size heap arrays: `var arr [n]int` where n is variable (freed at scope end)

### 4.2 Implementation Strategy

#### 4.2.1 Array Descriptor Structure
Each array is represented internally as a descriptor with:
- Pointer to heap-allocated data
- Size in bytes
- Element type information

#### 4.2.2 Scope-based Deallocation
- [ ] Track array allocations in each scope (function or block)
- [ ] Generate cleanup code at scope exit (before return, break, or end of block)
- [ ] Handle multiple exit points (return, break from loops)
- [ ] Ensure cleanup runs even on early returns

#### 4.2.3 Codegen Changes
- [ ] `VarDeclStmtAST` for arrays: emit malloc call
- [ ] Track allocated arrays in scope stack
- [ ] Insert free calls at all scope exit points
- [ ] Handle nested scopes correctly

**Example transformation:**
```maxon
function example(n int) int
    var arr [n]int = 0
    var i = 0
    while i < n 'loop'
        arr[i] = i
        i = i + 1
    end 'loop'
    return arr[5]
end 'example'
```

**Generated cleanup code (conceptual):**
```llvm
; Allocate array
%arr = call i8* @malloc(i64 %n_times_4)
; ... use array ...
; Before return:
call void @free(i8* %arr)
ret i32 %result
```

#### 4.2.4 Array Passing Semantics
Arrays passed to functions:
- Pass by reference (pointer to heap data)
- Caller retains ownership
- Callee doesn't free
- Array only freed when original scope exits

**Test files:**
- `language-tests/fragments/array-heap-fixed.test` - fixed-size heap array
- `language-tests/fragments/array-heap-dynamic.test` - dynamic-size heap array
- `language-tests/fragments/array-scope-cleanup.test` - verify cleanup on scope exit
- `language-tests/fragments/array-early-return.test` - cleanup on early return
- `language-tests/fragments/array-in-loop.test` - array allocation in loop scope
- `language-tests/fragments/array-nested-scopes.test` - nested scope cleanup

### 4.3 Advanced Features (Future)

#### 4.3.1 Array Slicing
Allow taking sub-arrays without copying:
```maxon
var arr [100]int = 0
var slice = arr[10:20]  // Reference to elements 10-19
```

#### 4.3.2 Reference Counting (If Needed)
If arrays need to be returned or stored:
```maxon
function makeArray(n int) []int
    var arr [n]int = 0
    return arr  // Return transfers ownership
end 'makeArray'
```
- Implement reference counting to track ownership
- Free when reference count reaches zero

#### 4.3.3 Bounds Checking
- Optional runtime bounds checking for safety
- Can be disabled with optimization flags

## Phase 5: Command-Line Arguments

### 5.1 Main Function with Arguments
Change main signature to accept argc/argv:
```maxon
function main(argc int, argv ptr) int
    // argc = argument count
    // argv = array of string pointers
end 'main'
```

### 5.2 String to Int Conversion
Add stdlib function:
```maxon
function atoi(str ptr) int
    // Convert C string to integer
end 'atoi'
```

**Test files:**
- `language-tests/fragments/main-with-args.test` - argc/argv
- `language-tests/fragments/atoi.test` - string to int conversion

## Phase 6: Updated Spectral-Norm Implementation

### 6.1 Create New Implementation
**File:** `examples/spectral-norm-float.maxon`

```maxon
function evalA(i int, j int) float
    var ij = i + j
    var denom = ((ij * (ij + 1)) / 2) + i + 1
    return 1.0 / denom    // denom auto-promotes to float
end 'evalA'

function times(v []float, u []float, n int) int int
    var i = 0
    while i < n 'outer'
        v[i] = 0.0
        var j = 0
        while j < n 'inner'
            v[i] = v[i] + (u[j] * evalA(i, j))
            j = j + 1
        end 'inner'
        i = i + 1
    end 'outer'
    return 0
end 'times'

function timesTransp(v []float, u []float, n int) int
    var i = 0
    while i < n 'outer'
        v[i] = 0.0
        var j = 0
        while j < n 'inner'
            v[i] = v[i] + (u[j] * evalA(j, i))
            j = j + 1
        end 'inner'
        i = i + 1
    end 'outer'
    return 0
end 'timesTransp'

function ATimesTransp(v []float, u []float, x []float, n int) int
    times(x, u, n)
    timesTransp(v, x, n)
    return 0
end 'ATimesTransp'

function main(argc int, argv ptr) int
    var n = 5500
    if argc > 1 n = atoi(argv[1])
    
    // Arrays are heap-allocated and automatically freed at scope exit
    var u [n]float = 0
    var v [n]float = 0
    var x [n]float = 0
    
    // Initialize u
    var i = 0
    while i < n 'init'
        u[i] = 1.0
        i = i + 1
    end 'init'
    
    // Power iteration
    i = 0
    while i < 10 'iter'
        ATimesTransp(v, u, x, n)
        ATimesTransp(u, v, x, n)
        i = i + 1
    end 'iter'
    
    // Calculate eigenvalue
    var vBv = 0.0
    var vv = 0.0
    i = 0
    while i < n 'calc'
        vBv = vBv + (u[i] * v[i])
        vv = vv + (v[i] * v[i])
        i = i + 1
    end 'calc'
    
    var result = sqrt(vBv / vv)
    
    // Format and print result
    var buffer [32]char = 0
    format_float_array(result, buffer, 9)
    // Use stdlib stream functions to print buffer
    // Or use stdlib/io/print: print(result)
    
    // Arrays u, v, x automatically freed here at scope exit
    return 0
end 'main'
```

**Test file:**
- `language-tests/fragments/spectral-norm-simple.test` - small N test (n=10)

## Testing Strategy

### Unit Tests (Per Feature)
Each feature should have 2-4 test fragments covering:
1. Basic functionality
2. Edge cases
3. Integration with existing features
4. Error conditions

### Integration Tests
- `language-tests/fragments/spectral-norm-n10.test` - Full benchmark with n=10
- `language-tests/fragments/spectral-norm-n100.test` - Medium size test
- Verify output matches expected eigenvalue within tolerance

### Performance Tests
- Benchmark with n=5500 (standard benchmark size)
- Compare performance with Go version
- Profile and optimize if needed

## Documentation Updates

### 6.2 Update docs/Content/variables.md
- [ ] Add section on float type
- [ ] Add float literal examples
- [ ] Document type coercion rules

### 6.3 Create docs/Content/types.md
- [ ] Document all types: int, float, char, ptr, bool
- [ ] Type conversion functions (round, trunc, floor, ceil)
- [ ] Implicit promotion rules (int→float automatic in mixed expressions)
- [ ] Explicit float literals (must use decimal point and leading zero: `42.0`, `0.5` not `.5`)
- [ ] Array types

### 6.3 Create docs/Content/stdlib.md
- [ ] Document string formatting functions (format_int_array, format_float_array)
- [ ] Document automatic array memory management
- [ ] Document string conversion functions (atoi)
- [ ] Document I/O functions (print for int and float)

### 6.4 Create docs/Content/math.md
- [ ] Document math keywords: sqrt, abs, floor, ceil, pow
- [ ] Document trigonometric functions: sin, cos, tan
- [ ] Document logarithmic functions: log, exp
- [ ] Document type conversion functions: round, trunc, floor, ceil (all return int)
- [ ] Explain implicit int→float promotion in mixed expressions
- [ ] Explain explicit float literals (42.0 vs 42, require leading zero: 0.5 not .5)
- [ ] Provide usage examples

### 6.5 Update vscode-extension/syntaxes/maxon.tmLanguage.json
- [ ] Add float to type keywords
- [ ] Add float literal patterns (require decimal point)
- [ ] Add math keywords (sqrt, abs, floor, ceil, pow, sin, cos, tan, log, exp)
- [ ] Add conversion keywords (round, trunc)

## Implementation Order (Recommended)

### Sprint 1: Core Float Support (3-5 days)
1. Lexer: float literals
2. AST: FloatExprAST
3. Parser: float parsing and type recognition
4. Codegen: basic float operations
5. Tests: 8-10 fragment tests

### Sprint 2: Math Keywords and Conversions (1-2 days)
1. Add math keywords to lexer (sqrt, abs, floor, ceil, pow, sin, cos)
2. Add conversion keywords (round, trunc); floor/ceil also return int
3. Parse math keywords as special calls
4. Codegen: map to LLVM intrinsics and conversion operations
5. Tests: math function tests (7-9 fragments including conversions)

### Sprint 3: Float Output (1-2 days)
1. Create stdlib/fmt/float.maxon with format_float_array
2. Implement float-to-string algorithm
3. Create stdlib/io/print.maxon for int and float printing
4. Tests: format tests (4-5 fragments) and print tests

### Sprint 4: Automatic Memory Management (3-4 days)
1. Implement heap allocation for all arrays
2. Scope tracking for array cleanup
3. Generate free calls at scope exits
4. Handle all exit paths (return, break)
5. Tests: heap arrays with automatic cleanup (6-8 fragments)

### Sprint 5: Command-Line Args (1 day)
1. main with argc/argv
2. atoi function
3. Tests: argument tests

### Sprint 6: Integration (1-2 days)
1. Complete spectral-norm implementation
2. Integration tests
3. Documentation updates
4. Extension syntax updates

## Success Criteria

- [ ] `maxon examples/spectral-norm-float.maxon 5500` runs successfully
- [ ] Output matches expected: `1.274224153` (±0.000000001)
- [ ] All language tests pass
- [ ] Documentation is complete
- [ ] VS Code extension provides proper syntax highlighting

## Future Enhancements

1. **More numeric types:** f32 (single precision), i64 (long), u32 (unsigned), etc.
2. **Advanced math:** sin, cos, log, exp, etc.
3. **String type:** Proper string handling beyond char arrays
4. **Array return values:** Transfer ownership when returning arrays from functions
5. **Reference counting:** For complex ownership scenarios
6. **Generics:** Type-parameterized functions
7. **SIMD support:** Vector operations for performance
8. **Array slicing:** Sub-array references without copying
