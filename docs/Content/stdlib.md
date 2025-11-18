# Standard Library

The Maxon standard library provides essential functionality for formatting, file I/O, and mathematical operations.

## String Formatting (stdlib/fmt)

### format_int_array

Format an integer to ASCII characters in a buffer.

**Signature:**
```maxon
function format_int_array(value int, buffer []char) int
```

**Parameters:**
- `value` - The integer to format
- `buffer` - Character array to write into (must be at least 12 bytes)

**Returns:** Number of bytes written

**Example:**
```maxon
var num = 42
var buffer = [12]char
var len = format_int_array(num, buffer)
// buffer now contains "42"
```

ExitCode: 42
```maxon
function main() int
    var num = 42
    var buffer = [12]char
    format_int_array(num, buffer)
    return num
end 'main'
```

### format_float_array

Format a floating-point number to ASCII characters in a buffer with specified precision.

**Signature:**
```maxon
function format_float_array(value float, buffer [32]char, precision int) int
```

**Parameters:**
- `value` - The float to format
- `buffer` - Character array to write into (must be at least 32 bytes)
- `precision` - Number of decimal places (0-15)

**Returns:** Number of bytes written

**Example:**
```maxon
var pi = 3.14159265
var buffer = [32]char
var len = format_float_array(pi, buffer, 6)
// buffer now contains "3.141593"
```

ExitCode: 0
```maxon
function main() int
    var x = 3.14
    var buffer = [32]char
    format_float_array(x, buffer, 2)
    // buffer contains "3.14"
    return 0
end 'main'
```

**Precision Examples:**
```maxon
var x = 123.456789
var buffer = [32]char

format_float_array(x, buffer, 0)  // "123"
format_float_array(x, buffer, 2)  // "123.46" (rounded)
format_float_array(x, buffer, 6)  // "123.456789"
```

## Automatic Array Memory Management

All arrays in Maxon are **automatically heap-allocated** and **automatically freed** when they go out of scope. You don't need to manually allocate or free array memory.

### Fixed-Size Arrays

```maxon
function example() int
    var arr = [100]int    // Allocated on heap
    arr[0] = 42
    return arr[0]
    // Array automatically freed here
end 'example'
```

### Dynamic-Size Arrays

Array size can be determined at runtime:

```maxon
function createArray(n int) int
    var arr [n]int = 0      // Size determined by parameter
    arr[0] = 100
    return arr[0]
    // Array automatically freed here
end 'createArray'
```

### Scope-Based Cleanup

Arrays are freed when their scope ends, including early returns:

```maxon
function conditionalArray(flag bool) int
    var arr = [50]int
    if flag
        return arr[0]       // Array freed before return
    end 'flag'
    arr[10] = 20
    return arr[10]          // Array freed before return
end 'conditionalArray'
```

ExitCode: 0
```maxon
function main() int
    var arr = [10]int
    arr[5] = 42
    if arr[5] > 40
        return 0            // Array freed before return
    end 'check'
    return 1
end 'main'
```

### Array Parameters

Arrays are passed by reference to functions. The **caller retains ownership** and is responsible for cleanup:

```maxon
function fillArray(arr []int, size int) int
    var i = 0
    while i < size 'loop'
        arr[i] = i * 2
        i = i + 1
    end 'loop'
    return 0
    // Array NOT freed here - caller owns it
end 'fillArray'

function main() int
    var myArray = [10]int
    fillArray(myArray, 10)
    var result = myArray[5]
    return result
    // myArray freed here
end 'main'
```

ExitCode: 10
```maxon
function setValues(arr []int, size int) int
    var i = 0
    while i < size 'loop'
        arr[i] = i * 2
        i = i + 1
    end 'loop'
    return 0
end 'setValues'

function main() int
    var arr = [10]int
    setValues(arr, 10)
    return arr[5]  // Returns 10 (5 * 2)
end 'main'
```

### Nested Scopes

Each scope properly cleans up its arrays:

```maxon
function nested() int
    var outer = [20]int
    outer[0] = 10
    
    if true
        var inner = [30]int
        inner[0] = 20
        outer[1] = inner[0]
        // inner freed here at end of if block
    end 'block'
    
    return outer[1]
    // outer freed here
end 'nested'
```

ExitCode: 20
```maxon
function main() int
    var x = 0
    if true
        var arr = [10]int
        arr[0] = 20
        x = arr[0]
        // arr freed here
    end 'block'
    return x
end 'main'
```

### Arrays in Loops

Arrays allocated in loop bodies are freed at the end of each iteration:

```maxon
function loopArrays(n int) int
    var sum = 0
    var i = 0
    while i < n 'outer'
        var temp = [10]int
        temp[0] = i
        sum = sum + temp[0]
        i = i + 1
        // temp freed here at end of each iteration
    end 'outer'
    return sum
end 'loopArrays'
```

## Array Properties

### .length

Get the number of elements in an array:

```maxon
var arr = [100]int
var size = arr.length       // Returns 100 (as int)
```

ExitCode: 42
```maxon
function main() int
    var arr = [42]int
    return arr.length       // Returns 42
end 'main'
```

The `.length` property works with both fixed-size and parameter arrays:

```maxon
function processArray(arr []int, expectedSize int) bool
    if arr.length != expectedSize
        return false
    end 'check'
    return true
end 'processArray'
```

## Command-Line Arguments

The `main` function can optionally accept command-line arguments:

```maxon
function main(argc int, argv ptr) int
    // argc = number of arguments (including program name)
    // argv = pointer to array of wide-character strings (wchar_t**)
    
    if argc > 1
        // Process command-line arguments
        // Note: String parsing requires pointer operations
        // (not yet fully implemented)
    end 'hasArgs'
    
    return 0
end 'main'
```

**Without arguments:**
```maxon
function main() int
    return 0
end 'main'
```

Both signatures are valid. Use the two-parameter version when you need to process command-line arguments.

## Future Features

The following features are planned but not yet implemented:

### String to Integer Conversion (atoi)

```maxon
// Planned - requires pointer dereferencing
function atoi(str ptr) int
    // Convert C string to integer
end 'atoi'
```

**Status:** Deferred until pointer dereferencing operations (`*ptr`, `ptr[i]`) are implemented.

### Print Functions

```maxon
// Planned - convenience functions for output
function print(value int) int
function print(value float) int
```

**Current workaround:** Use `format_int_array` or `format_float_array` with file system stream functions.

## File System (stdlib/fs)

Documentation for file system functions to be added. See source files in `stdlib/fs/` for available functionality.

## Summary

The Maxon standard library provides:

- **String formatting** for integers and floats
- **Automatic memory management** for all arrays
- **Array properties** like `.length`
- **Command-line arguments** via optional main parameters

All array memory is managed automatically - no manual allocation or deallocation required. Arrays are heap-allocated and freed when they go out of scope, including early returns and nested scopes.


