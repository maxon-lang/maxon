---
feature: print-function
status: stable
keywords: [print, output, stdout]
category: stdlib
---

# Print Function

## Developer Notes

The `print()` function is a built-in stdlib function that outputs integer values to stdout.

Implementation:
- Defined in `stdlib/sys/print.maxon`
- Auto-discovered by compiler when referenced
- Converts int to string using `format_int_array()`
- Writes to stdout using Windows API `WriteFile()`
- Automatically adds newline after each value
- Part of the standard library, not a language keyword

The function is linked automatically when used. No explicit import needed - the compiler's stdlib autodiscovery system finds it.

## Documentation

The `print()` function outputs integer values to standard output (console).

### Syntax

```maxon
print(value)
```

Where `value` is an `int` expression.

### Example

```maxon
function main() int
    var x = 42
    print(x)        // Prints: 42
    print(10 + 5)   // Prints: 15
    print(100)      // Prints: 100
    return 0
end 'main'
```

Each call to `print()` outputs the value followed by a newline.

## Tests

<!-- test: basic -->
```maxon
function main() int
    var x = 42
    print(x)
    return 0
end 'main'
```
```
ExitCode: 0
Stdout: 42
```

<!-- test: expression -->
```maxon
function main() int
    print(10 + 5)
    return 0
end 'main'
```
```
ExitCode: 0
Stdout: 15
```

<!-- test: multiple-calls -->
```maxon
function main() int
    var x = 42
    print(x)
    print(10 + 5)
    print(100)
    return 0
end 'main'
```
```
ExitCode: 0
Stdout: 42
15
100
```
