# Types

Maxon is a statically-typed language with a small but powerful set of built-in types.

## Basic Types

### int
A 32-bit signed integer. Range: -2,147,483,648 to 2,147,483,647.

```maxon
var count = 42
var negative = -100
var zero = 0
```

### float
A 64-bit floating-point number (IEEE 754 double precision). Float literals **must** include a decimal point and use a leading zero before the decimal point.

```maxon
var pi = 3.14159
var half = 0.5        // Valid: leading zero required
var temperature = -40.0
var scientific = 1.5e10  // Scientific notation supported
```

**Invalid float literals:**
```maxon
var bad = .5         // ERROR: Must use 0.5
var bad2 = 42        // This is an int, not a float
```

To create a float from an integer literal, use a decimal point:
```maxon
var x = 42.0         // float
var y = 42           // int
```

### char
An 8-bit character (ASCII).

```maxon
var letter = 'A'
var newline = '\n'
var tab = '\t'
```

### bool
A boolean value (`true` or `false`).

```maxon
var flag = true
var done = false

// Explicit type annotation
var enabled bool = true
let is_valid bool = false
```

### ptr
A generic pointer type. Used for untyped pointers, primarily for interfacing with external functions or system APIs.

```maxon
var p ptr = nullptr
```

## Array Types

Arrays are heap-allocated sequences of elements of the same type. Array memory is automatically managed and freed when the array goes out of scope.

### Array Initialization

Arrays can be initialized in two ways:

**Zero-initialized arrays** using `[size]type` syntax:
```maxon
var numbers = [10]int          // Array of 10 integers, all initialized to 0
var floats = [5]float          // Array of 5 floats, all initialized to 0.0
var letters = [26]char         // Array of 26 characters, all initialized to 0
```

**Value-initialized arrays** using `[val1, val2, ...]` syntax:
```maxon
var nums = [10, 20, 30, 40]    // Array of 4 integers with specified values
var coords = [1.5, 2.5, 3.5]   // Array of 3 floats with specified values
```

### Dynamic-Size Arrays

Array size can be determined at runtime:

```maxon
function createArray(n int) int
    var arr = [n]int           // Size determined by parameter n
    arr[0] = 42
    return arr[0]
    // Array automatically freed here when function returns
end 'createArray'
```

### Array Length

Arrays have a `.length` property that returns the number of elements:

```maxon
var arr = [100]int
var size = arr.length          // Returns 100 (as int)
```

ExitCode: 0
```maxon
function main() int
    var arr = [10]int
    return arr.length - 10     // Should return 0
end 'main'
```

### Array Indexing

Arrays are zero-indexed:

```maxon
var nums = [5]int
nums[0] = 10    // First element
nums[4] = 50    // Last element
var x = nums[2] // Access element
```

### Array Parameters

Arrays are passed by reference to functions. The caller retains ownership:

```maxon
function sum(arr [10]int) int
    var total = 0
    var i = 0
    while i < 10 'loop'
        total = total + arr[i]
        i = i + 1
    end 'loop'
    return total
end 'sum'

function main() int
    var numbers = [10]int
    numbers[0] = 5
    numbers[1] = 10
    var result = sum(numbers)
    return result              // Array freed here
end 'main'
```

### Unsized Array Parameters

Functions can accept arrays of any size using unsized array syntax:

```maxon
function sum(arr []int, length int) int
    var total = 0
    var i = 0
    while i < length 'loop'
        total = total + arr[i]
        i = i + 1
    end 'loop'
    return total
end 'sum'

function main() int
    var small = [5]int
    small[0] = 10
    var result1 = sum(small, 5)
    
    var large = [100]int
    large[0] = 20
    var result2 = sum(large, 100)
    
    return result1 + result2
end 'main'
```

## Type Conversions

### Implicit Conversions

**int → float:** Integers are automatically promoted to floats in mixed expressions:

```maxon
var x = 5           // int
var y = 2.5         // float
var result = x + y  // 5 promoted to 5.0, result is 7.5 (float)
```

ExitCode: 7
```maxon
function main() int
    var x = 5
    var y = 2.5
    var result = x + y          // 7.5
    return trunc(result)        // Convert to int: 7
end 'main'
```

### Explicit Conversions

Use built-in conversion functions to explicitly convert between types:

#### Float to Int Conversions

- **trunc(x float) int** - Truncate toward zero
- **round(x float) int** - Round to nearest integer
- **floor(x float) int** - Round down to integer
- **ceil(x float) int** - Round up to integer

```maxon
var f = 3.7
var i1 = trunc(f)    // 3 (truncate toward zero)
var i2 = round(f)    // 4 (round to nearest)
var i3 = floor(f)    // 3 (round down)
var i4 = ceil(f)     // 4 (round up)

var neg = -3.7
var n1 = trunc(neg)  // -3 (toward zero)
var n2 = floor(neg)  // -4 (round down)
var n3 = ceil(neg)   // -3 (round up)
```

ExitCode: 0
```maxon
function main() int
    var f = 3.7
    var result = trunc(f) + round(f) + floor(f) + ceil(f)
    // 3 + 4 + 3 + 4 = 14
    return result - 14
end 'main'
```

#### Int to Float Conversion

Use a decimal point in the literal:

```maxon
var i = 42
var f = i + 0.0      // Implicit promotion: i becomes 42.0
```

Or use an explicit float literal:

```maxon
function doubleIt(x int) float
    return 2.0 * x   // x promoted to float, returns float
end 'doubleIt'
```

## Type Checking

Maxon performs static type checking at compile time. Type mismatches are caught before execution:

```maxon
var x = 5           // int
x = 3.14            // ERROR: Cannot assign float to int variable
```

```maxon
function add(a int, b int) int
    return a + b
end 'add'

var result = add(5, 3.14)  // ERROR: Cannot pass float to int parameter
```

## Summary Table

| Type | Size | Range/Description | Example |
|------|------|-------------------|---------|
| int | 32-bit | -2,147,483,648 to 2,147,483,647 | `42` |
| float | 64-bit | IEEE 754 double precision | `3.14` or `0.5` |
| char | 8-bit | ASCII characters | `'A'` |
| bool | 1-bit | `true` or `false` | `true` |
| ptr | Platform | Generic pointer | `nullptr` |
| [N]T | N × sizeof(T) | Array of N elements of type T | `[10]int` |
| []T | - | Unsized array parameter type | `[]float` |
