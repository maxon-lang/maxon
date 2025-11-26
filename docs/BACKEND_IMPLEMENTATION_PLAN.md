# Backend Compiler Implementation Plan

A phased approach to implementing the Maxon compiler backend, progressing from simplest to most complex features. Each phase includes unit tests to verify the full pipeline (lexer → parser → semantic → MIR → x86 → executable) before advancing.

## Test Strategy

Unit tests use progressively complex Maxon programs. Each test:
1. Compiles a small Maxon program
2. Runs the resulting executable
3. Verifies exit code and/or stdout output

Tests are implemented in `maxon-bin/self_test.cpp` and run via `maxon self-test`.

## Workflow (Test-Driven Development)

For each test case:

1. **Write the test** - Add a new test case to `self_test.cpp`
2. **Run `maxon self-test`** - Watch it fail (unless the feature is already implemented). This also runs all existing tests to ensure no regressions.
3. **Fix the compiler** - Implement/fix code in lexer, parser, semantic analyzer, MIR codegen, or x86 backend until the test passes
4. **Move to the next test** - Repeat

This ensures we build incrementally with confidence that each feature works before adding complexity.

---

## Phase 1: Integer Constants & Return
**Status:** [ ] Not Started

The absolute minimum: a function that returns an integer constant.

### Features
- `NumberExprAST` - Integer literals
- `ReturnStmtAST` - Return statement
- Function prologue/epilogue in x86
- Entry point (_start → main → exit)

### Unit Tests
```maxon
// Test 1.1: Return zero
function main() int
    return 0
end 'main'
// Expected: ExitCode 0

// Test 1.2: Return non-zero
function main() int
    return 42
end 'main'
// Expected: ExitCode 42

// Test 1.3: Return max byte value
function main() int
    return 255
end 'main'
// Expected: ExitCode 255
```

### LLVM Reference
- `llvm-source/llvm/examples/Kaleidoscope/Chapter2/toy.cpp` - Basic AST structure

---

## Phase 2: Arithmetic Operators
**Status:** [ ] Not Started

Binary expressions for integer arithmetic.

### Features
- `BinaryExprAST` with operators: `+`, `-`, `*`, `/`, `%`
- Operator precedence
- Parenthesized expressions

### Unit Tests
```maxon
// Test 2.1: Addition
function main() int
    return 3 + 4
end 'main'
// Expected: ExitCode 7

// Test 2.2: Subtraction
function main() int
    return 10 - 3
end 'main'
// Expected: ExitCode 7

// Test 2.3: Multiplication
function main() int
    return 6 * 7
end 'main'
// Expected: ExitCode 42

// Test 2.4: Division
function main() int
    return 20 / 4
end 'main'
// Expected: ExitCode 5

// Test 2.5: Modulo
function main() int
    return 17 % 5
end 'main'
// Expected: ExitCode 2

// Test 2.6: Precedence (mul before add)
function main() int
    return 2 + 3 * 4
end 'main'
// Expected: ExitCode 14

// Test 2.7: Parentheses override precedence
function main() int
    return (2 + 3) * 4
end 'main'
// Expected: ExitCode 20

// Test 2.8: Complex expression
function main() int
    return (10 + 2) / 3 + 5 * 2
end 'main'
// Expected: ExitCode 14
```

### LLVM Reference
- `llvm-source/llvm/lib/CodeGen/SelectionDAG/` - Instruction selection patterns

---

## Phase 3: Variables
**Status:** [ ] Not Started

Stack-allocated local variables.

### Features
- `VarDeclStmtAST` - Mutable variable declaration
- `LetDeclStmtAST` - Immutable variable declaration
- `AssignStmtAST` - Variable assignment
- `VariableExprAST` - Variable reference
- Stack allocation (alloca)

### Unit Tests
```maxon
// Test 3.1: Declare and return
function main() int
    var x = 5
    return x
end 'main'
// Expected: ExitCode 5

// Test 3.2: Multiple variables
function main() int
    var a = 3
    var b = 4
    return a + b
end 'main'
// Expected: ExitCode 7

// Test 3.3: Let (immutable)
function main() int
    let y = 10
    return y
end 'main'
// Expected: ExitCode 10

// Test 3.4: Reassignment
function main() int
    var x = 1
    x = 2
    return x
end 'main'
// Expected: ExitCode 2

// Test 3.5: Variable in expression
function main() int
    var x = 10
    var y = x * 2 + 5
    return y
end 'main'
// Expected: ExitCode 25

// Test 3.6: Multiple reassignments
function main() int
    var x = 1
    x = x + 1
    x = x + 1
    x = x + 1
    return x
end 'main'
// Expected: ExitCode 4
```

### LLVM Reference
- `llvm-source/llvm/lib/CodeGen/` - Register allocation basics

---

## Phase 4: Comparisons & Booleans
**Status:** [ ] Not Started

Conditional values and comparison operators.

### Features
- `BooleanExprAST` - Boolean literals (true/false)
- Comparison operators: `>`, `<`, `>=`, `<=`, `==`, `!=`
- Logical operators: `and`, `or`, `not`
- `CastExprAST` - Type casts (bool to int)

### Unit Tests
```maxon
// Test 4.1: Greater than (true)
function main() int
    var result = 5 > 3
    return result as int
end 'main'
// Expected: ExitCode 1

// Test 4.2: Greater than (false)
function main() int
    var result = 3 > 5
    return result as int
end 'main'
// Expected: ExitCode 0

// Test 4.3: Less than
function main() int
    var result = 3 < 5
    return result as int
end 'main'
// Expected: ExitCode 1

// Test 4.4: Equal
function main() int
    var result = 5 == 5
    return result as int
end 'main'
// Expected: ExitCode 1

// Test 4.5: Not equal
function main() int
    var result = 5 != 3
    return result as int
end 'main'
// Expected: ExitCode 1

// Test 4.6: Boolean literal true
function main() int
    var b = true
    return b as int
end 'main'
// Expected: ExitCode 1

// Test 4.7: Boolean literal false
function main() int
    var b = false
    return b as int
end 'main'
// Expected: ExitCode 0

// Test 4.8: Logical and (both true)
function main() int
    var result = 3 > 2 and 4 > 3
    return result as int
end 'main'
// Expected: ExitCode 1

// Test 4.9: Logical and (one false)
function main() int
    var result = 3 > 2 and 4 < 3
    return result as int
end 'main'
// Expected: ExitCode 0

// Test 4.10: Logical or
function main() int
    var result = false or true
    return result as int
end 'main'
// Expected: ExitCode 1

// Test 4.11: Logical not
function main() int
    var result = not false
    return result as int
end 'main'
// Expected: ExitCode 1
```

---

## Phase 5: Control Flow
**Status:** [ ] Not Started

Branching and loops.

### Features
- `IfStmtAST` - If/else statements (single-line and multi-line)
- `WhileStmtAST` - While loops
- `BreakStmtAST` - Break from loop
- `ContinueStmtAST` - Continue to next iteration
- Block identifiers

### Unit Tests
```maxon
// Test 5.1: Simple if (true branch)
function main() int
    if 5 > 3
        return 1
    end 'if'
    return 0
end 'main'
// Expected: ExitCode 1

// Test 5.2: Simple if (false branch)
function main() int
    if 3 > 5
        return 1
    end 'if'
    return 0
end 'main'
// Expected: ExitCode 0

// Test 5.3: If-else
function main() int
    if 3 > 5
        return 1
    else
        return 2
    end 'if'
end 'main'
// Expected: ExitCode 2

// Test 5.4: Single-line if
function main() int
    var x = 0
    if 5 > 3 then x = 1
    return x
end 'main'
// Expected: ExitCode 1

// Test 5.5: While loop - count to 5
function main() int
    var i = 0
    while i < 5
        i = i + 1
    end 'loop'
    return i
end 'main'
// Expected: ExitCode 5

// Test 5.6: While loop - sum 1 to 5
function main() int
    var sum = 0
    var i = 1
    while i <= 5
        sum = sum + i
        i = i + 1
    end 'loop'
    return sum
end 'main'
// Expected: ExitCode 15

// Test 5.7: Break
function main() int
    var i = 0
    while true
        i = i + 1
        if i == 5
            break
        end 'check'
    end 'loop'
    return i
end 'main'
// Expected: ExitCode 5

// Test 5.8: Continue
function main() int
    var sum = 0
    var i = 0
    while i < 10
        i = i + 1
        if i % 2 == 1
            continue
        end 'skip'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
// Expected: ExitCode 30 (2+4+6+8+10)

// Test 5.9: Nested loops
function main() int
    var count = 0
    var i = 0
    while i < 3
        var j = 0
        while j < 3
            count = count + 1
            j = j + 1
        end 'inner'
        i = i + 1
    end 'outer'
    return count
end 'main'
// Expected: ExitCode 9
```

### LLVM Reference
- `llvm-source/llvm/lib/CodeGen/BranchFolding.cpp` - Branch optimization patterns

---

## Phase 6: Functions & Calls
**Status:** [ ] Not Started

User-defined functions with parameters.

### Features
- Function parameters
- `CallExprAST` - Function calls
- Recursion
- Calling convention (Win64 ABI)

### Unit Tests
```maxon
// Test 6.1: Simple function call
function double(x int) int
    return x * 2
end 'double'

function main() int
    return double(21)
end 'main'
// Expected: ExitCode 42

// Test 6.2: Two parameters
function add(a int, b int) int
    return a + b
end 'add'

function main() int
    return add(3, 4)
end 'main'
// Expected: ExitCode 7

// Test 6.3: Multiple function calls
function square(x int) int
    return x * x
end 'square'

function main() int
    var a = square(3)
    var b = square(4)
    return a + b
end 'main'
// Expected: ExitCode 25

// Test 6.4: Nested function calls
function double(x int) int
    return x * 2
end 'double'

function main() int
    return double(double(5))
end 'main'
// Expected: ExitCode 20

// Test 6.5: Recursion - factorial
function factorial(n int) int
    if n <= 1
        return 1
    end 'base'
    return n * factorial(n - 1)
end 'factorial'

function main() int
    return factorial(5)
end 'main'
// Expected: ExitCode 120

// Test 6.6: Recursion - fibonacci
function fib(n int) int
    if n <= 1
        return n
    end 'base'
    return fib(n - 1) + fib(n - 2)
end 'fib'

function main() int
    return fib(10)
end 'main'
// Expected: ExitCode 55

// Test 6.7: Many parameters (tests register spilling)
function sum5(a int, b int, c int, d int, e int) int
    return a + b + c + d + e
end 'sum5'

function main() int
    return sum5(1, 2, 3, 4, 5)
end 'main'
// Expected: ExitCode 15
```

### LLVM Reference
- `llvm-source/llvm/lib/Target/X86/X86CallingConv.td` - Win64 calling convention
- `llvm-source/llvm/lib/Target/X86/X86FrameLowering.cpp` - Stack frame setup

---

## Phase 7: Floats
**Status:** [ ] Not Started

Floating-point arithmetic using XMM registers.

### Features
- `FloatExprAST` - Float literals
- Float arithmetic (`+`, `-`, `*`, `/`)
- Float comparisons
- Float-to-int cast
- Math intrinsics (sqrt, sin, cos, etc.)

### Unit Tests
```maxon
// Test 7.1: Float addition
function main() int
    var x = 3.5 + 1.5
    return x as int
end 'main'
// Expected: ExitCode 5

// Test 7.2: Float subtraction
function main() int
    var x = 10.0 - 3.0
    return x as int
end 'main'
// Expected: ExitCode 7

// Test 7.3: Float multiplication
function main() int
    var x = 6.0 * 7.0
    return x as int
end 'main'
// Expected: ExitCode 42

// Test 7.4: Float division
function main() int
    var x = 20.0 / 4.0
    return x as int
end 'main'
// Expected: ExitCode 5

// Test 7.5: Float comparison
function main() int
    var result = 3.5 > 2.5
    return result as int
end 'main'
// Expected: ExitCode 1

// Test 7.6: sqrt intrinsic
function main() int
    var x = sqrt(16.0)
    return x as int
end 'main'
// Expected: ExitCode 4

// Test 7.7: sin/cos at zero
function main() int
    var s = sin(0.0)
    var c = cos(0.0)
    return (s + c) as int
end 'main'
// Expected: ExitCode 1 (sin(0)=0, cos(0)=1)

// Test 7.8: abs intrinsic
function main() int
    var x = abs(-42.0)
    return x as int
end 'main'
// Expected: ExitCode 42

// Test 7.9: floor/ceil
function main() int
    var f = floor(3.7)
    var c = ceil(3.2)
    return (f + c) as int
end 'main'
// Expected: ExitCode 7 (3 + 4)
```

### LLVM Reference
- `llvm-source/llvm/lib/Target/X86/X86InstrSSE.td` - SSE instruction definitions

---

## Phase 8: Arrays
**Status:** [ ] Not Started

Fixed-size arrays with heap allocation.

### Features
- `ArrayLiteralExprAST` - Array literals
- `ArrayIndexExprAST` - Array indexing
- `ArrayAssignStmtAST` - Array element assignment
- `MemberAccessExprAST` - `.length` property
- Heap allocation via `runtime.mir` (malloc/free)

### Unit Tests
```maxon
// Test 8.1: Array literal and index
function main() int
    var arr = [1, 2, 3]
    return arr[1]
end 'main'
// Expected: ExitCode 2

// Test 8.2: Array first element
function main() int
    var arr = [10, 20, 30]
    return arr[0]
end 'main'
// Expected: ExitCode 10

// Test 8.3: Array last element
function main() int
    var arr = [10, 20, 30]
    return arr[2]
end 'main'
// Expected: ExitCode 30

// Test 8.4: Zero-initialized array
function main() int
    var arr = [5]int
    return arr[0]
end 'main'
// Expected: ExitCode 0

// Test 8.5: Array assignment
function main() int
    var arr = [5]int
    arr[0] = 42
    return arr[0]
end 'main'
// Expected: ExitCode 42

// Test 8.6: Array length
function main() int
    var arr = [1, 2, 3, 4, 5]
    return arr.length
end 'main'
// Expected: ExitCode 5

// Test 8.7: Array sum
function main() int
    var arr = [1, 2, 3, 4, 5]
    var sum = 0
    var i = 0
    while i < arr.length
        sum = sum + arr[i]
        i = i + 1
    end 'loop'
    return sum
end 'main'
// Expected: ExitCode 15

// Test 8.8: Variable index
function main() int
    var arr = [10, 20, 30]
    var i = 1
    return arr[i]
end 'main'
// Expected: ExitCode 20

// Test 8.9: Computed index
function main() int
    var arr = [5, 10, 15, 20, 25]
    return arr[2 + 1]
end 'main'
// Expected: ExitCode 20
```

### LLVM Reference
- GEP (GetElementPtr) semantics in LLVM IR

---

## Phase 9: Structs
**Status:** [ ] Not Started

User-defined composite types.

### Features
- `StructDefAST` - Struct definition
- `StructInitExprAST` - Struct literals
- `MemberAccessExprAST` - Field access
- Struct as function parameter
- Struct as return value

### Unit Tests
```maxon
// Test 9.1: Simple struct
struct Point
    x int
    y int
end 'Point'

function main() int
    var p = Point { x: 3, y: 4 }
    return p.x + p.y
end 'main'
// Expected: ExitCode 7

// Test 9.2: Struct field access
struct Rect
    width int
    height int
end 'Rect'

function main() int
    var r = Rect { width: 5, height: 10 }
    return r.width * r.height
end 'main'
// Expected: ExitCode 50

// Test 9.3: Struct field modification
struct Counter
    value int
end 'Counter'

function main() int
    var c = Counter { value: 0 }
    c.value = 42
    return c.value
end 'main'
// Expected: ExitCode 42

// Test 9.4: Struct as parameter
struct Vec2
    x int
    y int
end 'Vec2'

function dot(a Vec2, b Vec2) int
    return a.x * b.x + a.y * b.y
end 'dot'

function main() int
    var v1 = Vec2 { x: 3, y: 4 }
    var v2 = Vec2 { x: 2, y: 1 }
    return dot(v1, v2)
end 'main'
// Expected: ExitCode 10 (3*2 + 4*1)

// Test 9.5: Struct return value
struct Pair
    first int
    second int
end 'Pair'

function makePair(a int, b int) Pair
    return Pair { first: a, second: b }
end 'makePair'

function main() int
    var p = makePair(5, 7)
    return p.first + p.second
end 'main'
// Expected: ExitCode 12

// Test 9.6: Nested struct access (future)
// Reserved for arrays of structs
```

### LLVM Reference
- `llvm-source/llvm/lib/Target/X86/X86ISelLowering.cpp` - Struct ABI handling

---

## Phase 10: Pointers & Advanced Features
**Status:** [ ] Not Started

Memory addresses and remaining features.

### Features
- `AddressOfExprAST` - Address-of operator (`&`)
- `DerefExprAST` - Dereference operator (`*`)
- `DerefAssignStmtAST` - Dereference assignment
- `ForStmtAST` - For loops with range
- `UnaryExprAST` - Unary minus/plus
- Namespaces

### Unit Tests
```maxon
// Test 10.1: Address-of and dereference
function main() int
    var x = 42
    var p = &x
    return *p
end 'main'
// Expected: ExitCode 42

// Test 10.2: Modify via pointer
function main() int
    var x = 10
    var p = &x
    *p = 20
    return x
end 'main'
// Expected: ExitCode 20

// Test 10.3: For loop with range
function main() int
    var sum = 0
    for i in range(1, 6)
        sum = sum + i
    end 'loop'
    return sum
end 'main'
// Expected: ExitCode 15 (1+2+3+4+5)

// Test 10.4: For loop over array
function main() int
    var arr = [2, 4, 6, 8]
    var sum = 0
    for x in arr
        sum = sum + x
    end 'loop'
    return sum
end 'main'
// Expected: ExitCode 20

// Test 10.5: Unary minus
function main() int
    var x = 10
    var y = -x
    return 0 - y
end 'main'
// Expected: ExitCode 10

// Test 10.6: Character type
function main() int
    var c = 'A'
    return c as int
end 'main'
// Expected: ExitCode 65

// Test 10.7: Nested control flow
function main() int
    var result = 0
    var i = 0
    while i < 3
        var j = 0
        while j < 3
            if (i + j) % 2 == 0
                result = result + 1
            end 'even'
            j = j + 1
        end 'inner'
        i = i + 1
    end 'outer'
    return result
end 'main'
// Expected: ExitCode 5
```

---

## Progress Tracking

| Phase | Description | Tests | Status |
|-------|-------------|-------|--------|
| 1 | Integer Constants & Return | 3 | [ ] |
| 2 | Arithmetic Operators | 8 | [ ] |
| 3 | Variables | 6 | [ ] |
| 4 | Comparisons & Booleans | 11 | [ ] |
| 5 | Control Flow | 9 | [ ] |
| 6 | Functions & Calls | 7 | [ ] |
| 7 | Floats | 9 | [ ] |
| 8 | Arrays | 9 | [ ] |
| 9 | Structs | 6 | [ ] |
| 10 | Pointers & Advanced | 7 | [ ] |
| **Total** | | **75** | |

---

## Implementation Notes

### Running Tests
```bash
# Run all unit tests
maxon self-test

# Run with verbose output
maxon self-test -v
```

### Adding New Tests
Tests are defined in `maxon-bin/self_test.cpp`. Each test case contains:
- Maxon source code
- Expected exit code
- Optional expected stdout

### LLVM Source References
Key files for reference when implementing backend features:

- **Basic AST**: `llvm-source/llvm/examples/Kaleidoscope/Chapter2/toy.cpp`
- **Code Generation**: `llvm-source/llvm/examples/Kaleidoscope/Chapter3/toy.cpp`
- **Control Flow**: `llvm-source/llvm/examples/Kaleidoscope/Chapter5/toy.cpp`
- **x86 Calling Convention**: `llvm-source/llvm/lib/Target/X86/X86CallingConv.td`
- **x86 Instruction Selection**: `llvm-source/llvm/lib/Target/X86/X86ISelLowering.cpp`
- **Register Allocation**: `llvm-source/llvm/lib/CodeGen/RegAllocGreedy.cpp`
- **Stack Frame**: `llvm-source/llvm/lib/Target/X86/X86FrameLowering.cpp`
