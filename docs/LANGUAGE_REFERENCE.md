# Maxon Language Reference

**Version**: 1.0  
**Target Audience**: AI Coding Agents and Developers

This reference provides complete syntax and semantics for the Maxon programming language.

---

## Table of Contents

1. [Program Structure](#program-structure)
2. [Lexical Elements](#lexical-elements)
3. [Types](#types)
4. [Variables](#variables)
5. [Functions](#functions)
6. [Expressions](#expressions)
7. [Statements](#statements)
8. [Namespaces](#namespaces)
9. [Standard Library](#standard-library)
10. [Memory Model](#memory-model)

---

## Program Structure

### Entry Point
Every Maxon program must have a `main()` function that returns `int`:

```maxon
function main() int
    return 0
end 'main'
```

The return value becomes the program's exit code (0-255 on Windows).

### File Structure
- One or more function declarations
- Namespace derived from file path
- Export functions with `export` keyword for cross-file visibility

---

## Lexical Elements

### Comments
Single-line comments only:
```maxon
// This is a comment
```

### Identifiers
- Start with letter or underscore: `[a-zA-Z_]`
- Followed by letters, digits, or underscores: `[a-zA-Z0-9_]*`
- Case-sensitive
- Cannot be keywords

### Keywords
```
and, as, bool, break, char, continue, else, end, export, extern,
false, float, for, function, if, in, int, let, not, or, ptr,
return, true, var, while
```

### Literals

**Integer Literals**
```maxon
42
-17
0
```

**Float Literals** (must contain decimal point)
```maxon
3.14
-2.5
0.0
1.0
```

**Character Literals** (single character in single quotes)
```maxon
'A'
'z'
'\n'
'\t'
'\\'
'\''
```

**String Literals** (double-quoted, null-terminated)
```maxon
"Hello, World!"
"Line1\nLine2"
"Tab\there"
"Quote: \"text\""
```

Escape sequences: `\n` `\t` `\\` `\"`

**Boolean Literals**
```maxon
true
false
```

---

## Types

### Primitive Types

| Type | Size | Description | LLVM Type |
|------|------|-------------|-----------|
| `int` | 32-bit | Signed integer | `i32` |
| `float` | 64-bit | IEEE 754 double | `double` |
| `bool` | 1-bit | Boolean (true/false) | `i1` |
| `char` | 8-bit | Single character | `i8` |
| `ptr` | platform | Untyped pointer | `ptr` |

### Array Types

**Fixed-Size Arrays**
```maxon
var numbers = [10]int        // Array of 10 integers
var values = [1, 2, 3]       // Value-initialized, size 3
```

**Unsized Arrays** (function parameters only)
```maxon
function process(data []int, size int) int
    return data[0]
end 'process'
```

**Array Properties**
- Zero-based indexing: `array[0]`, `array[1]`, ...
- `.length` property returns array size
- Heap-allocated with automatic scope-based cleanup
- No bounds checking (undefined behavior for out-of-bounds access)

### Type Conversions

**Implicit Conversions**
- `int` → `float` (in mixed arithmetic)

**Explicit Conversions** (using `as` operator)
```maxon
var f = 3.14
var i = f as int           // 3 (truncates)
var c = 65 as char         // 'A'
var b = 1 as bool          // true
var p = 0 as ptr           // null pointer
```

Supported casts:
- `int` ↔ `float`
- `int` ↔ `char`
- `int` ↔ `bool`
- `int` ↔ `ptr`
- `ptr` ↔ `ptr`
- `char` ↔ `int`

---

## Variables

### Mutable Variables (`var`)
```maxon
var x = 42              // Type inferred
var y int = 10          // Explicit type
x = x + 5               // Reassignment allowed
```

### Immutable Variables (`let`)
```maxon
let pi = 3.14159        // Cannot be reassigned
let name = "Maxon"
// pi = 3.14            // ERROR: Cannot assign to immutable variable
```

**Rules:**
- All variables must be initialized at declaration
- Type can be inferred from initializer or explicitly specified
- Scope is block-scoped
- Variables are stack-allocated (except arrays, which are heap-allocated)

---

## Functions

### Declaration Syntax
```maxon
function name(param1 type1, param2 type2) returnType
    // statements
    return value
end 'name'
```

**Block Identifier**: The string after `end` must match the function name.

### Examples

**No Parameters**
```maxon
function getAnswer() int
    return 42
end 'getAnswer'
```

**Multiple Parameters**
```maxon
function add(a int, b int) int
    return a + b
end 'add'
```

**Array Parameters**
```maxon
function sum(numbers []int, count int) int
    var total = 0
    for i in 0..count 'loop'
        total = total + numbers[i]
    end 'loop'
    return total
end 'sum'
```

### Calling Functions
```maxon
var result = add(3, 4)
var answer = getAnswer()
```

### Extern Functions

Declare external functions (Windows API, C libraries):
```maxon
extern function GetStdHandle(nStdHandle int) ptr
extern function WriteFile(hFile ptr, lpBuffer ptr, nBytes int, written ptr, overlapped ptr) int
```

**Notes:**
- No function body or `end` statement
- No name mangling
- Assumes C calling convention
- Must exist at link time

---

## Expressions

### Operator Precedence (highest to lowest)

1. **Postfix**: `[]` (array indexing), `.` (member access), `as` (cast), function call `()`
2. **Unary**: `-` (negation), `not` (logical not), `&` (address-of), `*` (dereference)
3. **Multiplicative**: `*` `/` `%`
4. **Additive**: `+` `-`
5. **Comparison**: `=` `!=` `<` `>` `<=` `>=`
6. **Logical AND**: `and`
7. **Logical OR**: `or`

### Arithmetic Operators

| Operator | Description | Types | Example |
|----------|-------------|-------|---------|
| `+` | Addition | int, float | `a + b` |
| `-` | Subtraction | int, float | `a - b` |
| `*` | Multiplication | int, float | `a * b` |
| `/` | Division | int, float | `a / b` |
| `%` | Modulo | int only | `a % b` |

**Notes:**
- Integer division truncates
- Mixed int/float operations promote int to float

### Comparison Operators

| Operator | Description | Result Type |
|----------|-------------|-------------|
| `=` | Equal to | bool |
| `!=` | Not equal to | bool |
| `<` | Less than | bool |
| `>` | Greater than | bool |
| `<=` | Less than or equal | bool |
| `>=` | Greater than or equal | bool |

### Logical Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `and` | Logical AND | `a > 0 and b < 10` |
| `or` | Logical OR | `x = 1 or x = 2` |
| `not` | Logical NOT | `not done` |

### Unary Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `-` | Negation | `-x` |
| `not` | Logical NOT | `not condition` |
| `&` | Address-of | `&variable` |
| `*` | Dereference | `*pointer` (limited support) |

### Parentheses
Override precedence:
```maxon
(2 + 3) * 5    // 25, not 17
```

### Array Indexing
```maxon
var arr = [1, 2, 3, 4, 5]
var first = arr[0]
var last = arr[arr.length - 1]
```

---

## Statements

### Expression Statement
Any expression followed by newline:
```maxon
print(x)
add(3, 4)
x = x + 1
```

### Return Statement
```maxon
return expression
```
Must appear in every code path of non-void functions.

### Variable Declaration
```maxon
var x = 10
let y = 20
```

### Assignment
```maxon
variable = expression
array[index] = value
```

**Note:** Cannot assign to `let` variables (immutable).

### If Statement

**Single-Line**
```maxon
if condition statement
```

**Multi-Line**
```maxon
if condition 'label'
    statements
end 'label'
```

**With Else**
```maxon
if condition 'label'
    statements
else 'label'
    statements
end 'label'
```

**Notes:**
- Block identifier required for multi-line
- Condition must be `bool` type
- Can nest arbitrarily

### While Loop
```maxon
while condition 'label'
    statements
end 'label'
```

**Example:**
```maxon
var i = 0
while i < 10 'loop'
    print(i)
    i = i + 1
end 'loop'
```

### For Loop
```maxon
for variable in iterable 'label'
    statements
end 'label'
```

**Range Iteration:**
```maxon
for i in 0..10 'loop'
    print(i)
end 'loop'
```

**Notes:**
- Loop variable is immutable (like `let`)
- Currently supports ranges (`start..end`)
- Desugars to while loop with iterator protocol

### Break Statement
```maxon
break
```
Exits the innermost loop (while or for).

### Continue Statement
```maxon
continue
```
Skips to next iteration of the innermost loop.

---

## Namespaces

### Automatic Derivation
Namespaces are derived from file paths:

| File Path | Namespace |
|-----------|-----------|
| `math.maxon` | (global) |
| `utils/helpers.maxon` | `utils` |
| `stdlib/fmt/integer.maxon` | `stdlib.fmt` |

### Export Keyword
Make functions visible outside the file:

```maxon
export function public_add(a int, b int) int
    return a + b
end 'public_add'

function private_helper(x int) int
    return x * 2
end 'private_helper'
```

Only `public_add` can be called from other files.

### Qualified Names
Call functions with full namespace:
```maxon
var result = stdlib.fmt.format_int(42)
```

### Suffix Matching
If unambiguous, use short name:
```maxon
var result = format_int(42)   // Finds stdlib.fmt.format_int
```

---

## Standard Library

### Core Functions

**I/O Functions**
```maxon
print(value int)                // Print integer to stdout
print_float(value float)        // Print float to stdout
```

**Math Functions**
```maxon
abs(x int) int                  // Absolute value
abs(x float) float              // Absolute value (overloaded)
sqrt(x float) float             // Square root
pow(base float, exp float) float // Power
sin(x float) float              // Sine
cos(x float) float              // Cosine
tan(x float) float              // Tangent
exp(x float) float              // e^x
log(x float) float              // Natural logarithm
log2(x float) float             // Base-2 logarithm
log10(x float) float            // Base-10 logarithm
floor(x float) float            // Round down
ceil(x float) float             // Round up
round(x float) float            // Round to nearest
trunc(x float) float            // Truncate decimal
```

**Formatting Functions**
```maxon
format_int(value int) ptr       // Format int as string
format_float(value float) ptr   // Format float as string
```

### Standard Library Modules

Located in `stdlib/` directory:
- `stdlib/fmt/` - Formatting utilities
- `stdlib/fs/` - File system operations
- `stdlib/iter/` - Iterator protocol

---

## Memory Model

### Stack Allocation
- Local variables (`var`, `let`)
- Function parameters
- Automatic lifetime (scope-based)

### Heap Allocation
- Arrays (all array types)
- Allocated with Windows `HeapAlloc`
- Automatically freed at end of scope
- No manual `free` or garbage collector needed

### Pointer Safety
- No null pointer checks (undefined behavior)
- No bounds checking on arrays
- Address-of operator `&` creates pointer to variable
- Dereference operator `*` has limited support

### Calling Convention
- Simple types (int, float, bool, char, ptr) passed by value
- Arrays passed as pointer
- Return values passed by value (in register or stack)

---

## Code Generation

### LLVM Backend
- Maxon compiles to LLVM IR
- LLVM optimizations applied
- Native executable generated

### Optimizations
- Constant folding
- Dead code elimination
- LLVM optimization passes (inline, DCE, etc.)

### Runtime Library
- Located in `maxon-runtime/runtime.obj`
- Provides implementations for LLVM intrinsics
- Auto-linked with all programs
- No C runtime dependency

---

## Common Patterns

### Main Function Template
```maxon
function main() int
    // program logic
    return 0
end 'main'
```

### Loops with Break
```maxon
var i = 0
while true 'forever'
    if i >= 10 'done'
        break
    end 'done'
    print(i)
    i = i + 1
end 'forever'
```

### Array Iteration
```maxon
var numbers = [1, 2, 3, 4, 5]
for i in 0..numbers.length 'iter'
    print(numbers[i])
end 'iter'
```

### String Output via Extern
```maxon
extern function GetStdHandle(nStdHandle int) ptr
extern function WriteFile(hFile ptr, lpBuffer ptr, nBytes int, written ptr, overlapped ptr) int

function main() int
    let stdout = GetStdHandle(-11)
    var written = 0
    var message = "Hello, World!\n"
    WriteFile(stdout, message, 14, &written, 0 as ptr)
    return 0
end 'main'
```

### Factorial Example
```maxon
function factorial(n int) int
    if n <= 1 'base'
        return 1
    end 'base'
    return n * factorial(n - 1)
end 'factorial'

function main() int
    var result = factorial(5)
    print(result)  // 120
    return 0
end 'main'
```

---

## Compilation Commands

```bash
# Compile and run in one step
maxon program.maxon

# Compile to executable
maxon compile program.maxon -o program.exe

# Emit LLVM IR alongside executable
maxon compile program.maxon --emit-llvm

# Run language tests
maxon test-fragments

# Extract tests from specs
maxon extract-specs

# Regenerate IR for test fragments
maxon regen-fragments
```

---

## Error Handling

### Compile-Time Errors

**Type Errors**
```maxon
var x = 5 + "string"    // ERROR: Type mismatch
```

**Missing Return**
```maxon
function test() int
    var x = 5
    // ERROR: Missing return statement
end 'test'
```

**Immutable Assignment**
```maxon
let x = 5
x = 10                  // ERROR: Cannot assign to immutable variable
```

**Unknown Keyword**
```maxon
functon test()          // ERROR: Unknown keyword 'functon'
```

**Mismatched Block Identifiers**
```maxon
if x > 0 'check'
    print(x)
end 'wrong'             // ERROR: Expected 'check', got 'wrong'
```

### Runtime Behavior

**Undefined Behavior (No Error)**
- Array out-of-bounds access
- Null pointer dereference
- Integer overflow (wraps around)
- Division by zero

---

## Best Practices for AI Agents

1. **Always match block identifiers**: `if x > 0 'check'` must end with `end 'check'`

2. **Initialize all variables**: No uninitialized variables allowed

3. **Use explicit types when clarity matters**: 
   ```maxon
   var count int = 0    // Clear intent
   ```

4. **Return from all code paths**:
   ```maxon
   function test(x int) int
       if x > 0 'pos'
           return 1
       end 'pos'
       return -1        // Don't forget this
   end 'test'
   ```

5. **Remember int/float distinction**:
   ```maxon
   var x = 5           // int
   var y = 5.0         // float (note decimal point)
   ```

6. **Use `for` loops for ranges**:
   ```maxon
   for i in 0..10 'loop'
       // i is 0, 1, 2, ..., 9
   end 'loop'
   ```

7. **Prefer `let` for immutability**:
   ```maxon
   let pi = 3.14159    // Prevents accidental modification
   ```

8. **Export only necessary functions**:
   ```maxon
   export function api() int    // Public API
   function internal() int      // Private helper
   ```

9. **Check `.length` before array access**:
   ```maxon
   if index < arr.length 'safe'
       var val = arr[index]
   end 'safe'
   ```

10. **Use meaningful block identifiers**:
    ```maxon
    while not done 'process'     // 'process' describes the loop
        // ...
    end 'process'
    ```

---

## Syntax Quick Reference

```maxon
// Variables
var mutable = 42
let immutable = 100

// Functions
function name(param type) returnType
    return value
end 'name'

// Control Flow
if condition 'id' statements end 'id'
if condition 'id' statements else 'id' statements end 'id'
while condition 'id' statements end 'id'
for var in iterable 'id' statements end 'id'
break
continue

// Arrays
var arr = [10]int
var vals = [1, 2, 3]
var elem = arr[0]
var size = arr.length

// Types
int float bool char ptr
[N]type    // fixed-size array
[]type     // unsized array (parameters)

// Operators
+ - * / %                    // arithmetic
= != < > <= >=               // comparison
and or not                   // logical
as                           // type cast
& *                          // address-of, dereference

// Literals
42                           // int
3.14                         // float
'A'                          // char
"text"                       // string
true false                   // bool
```

---

**End of Reference**
