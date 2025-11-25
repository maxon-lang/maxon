---
feature: print-float
status: stable
keywords: [print, float, output]
category: stdlib
---

# Print Float Function

## Developer Notes

The `print_float()` function outputs float values to stdout with specified precision.

Implementation:
- Defined in `stdlib/sys/print_float.maxon`
- Signature: `print_float(value float, precision int) int`
- Uses `format_float_array()` from `stdlib/fmt/float.maxon` to convert float to string
- Writes to stdout using `write_stdout()` from `maxon-runtime`
- `write_stdout()` is platform-specific: POSIX write() on Linux, WriteFile API on Windows
- Adds newline after output
- Auto-discovered by compiler

The precision parameter controls decimal places. Typical usage: `print_float(3.14159, 6)` outputs "3.141590".

## Documentation

The `print_float()` function outputs float values with specified decimal precision.

### Syntax

```maxon
print_float(value, precision)
```
Parameters:
- `value` - The float to print
- `precision` - Number of decimal places

### Example

```maxon
function main() int
    var pi = 3.14159
    print_float(pi, 2)     // Prints: 3.14
    print_float(pi, 5)     // Prints: 3.14159
    
    var x = -2.5
    print_float(x, 1)      // Prints: -2.5
    
    return 0
end 'main'
```
```exitcode
0
```
```stdout
3.14
3.14159
-2.5
```


## Tests

<!-- test: basic -->
```maxon
function main() int
    var x = 3.14159
    print_float(x, 6)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
3.141590
```


<!-- test: negative -->
```maxon
function main() int
    var y = -2.5
    print_float(y, 6)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
-2.500000
```


<!-- test: zero -->
```maxon
function main() int
    var z = 0.0
    print_float(z, 6)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0.000000
```


<!-- test: high-precision -->
```maxon
function main() int
    var x = 0.301029995663981
    print_float(x, 15)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0.301029995663981
```


<!-- test: high-precision-ten-digits -->
```maxon
function main() int
    var x = 0.301029995663981
    print_float(x, 10)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0.3010299957
```

