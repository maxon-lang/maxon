---
feature: print-function
status: stable
keywords: [print, output, stdout]
category: stdlib
---

# Print Function

## Developer Notes

The `printInt()` function is a stdlib function that outputs integer values to stdout.

Implementation:
- Defined in `stdlib/sys/print.maxon`
- Auto-discovered by compiler when referenced
- Uses `formatIntArray()` from `stdlib/fmt/integer.maxon` to convert int to string
- Writes to stdout using `write_stdout()` from `maxon-runtime`
- `write_stdout()` is platform-specific: POSIX write() on Linux, WriteFile API on Windows
- Automatically adds newline after each value
- Part of the standard library, not a language keyword

The function is linked automatically when used. No explicit import needed - the compiler's stdlib autodiscovery system finds it.

## Documentation

The `printInt()` function outputs integer values to standard output (console).

### Syntax

```maxon
printInt(value)
```
Where `value` is an `int` expression.

### Example

```maxon
function main() int
    var x = 42
    printInt(x)        // Prints: 42
    printInt(10 + 5)   // Prints: 15
    printInt(100)      // Prints: 100
    return 0
end 'main'
```
```exitcode
0
```
```stdout
42
15
100
```


Each call to `printInt()` outputs the value followed by a newline.

## Tests

<!-- test: basic -->
```maxon
function main() int
    var x = 42
    printInt(x)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```


<!-- test: expression -->
```maxon
function main() int
    printInt(10 + 5)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
15
```


<!-- test: multiple-calls -->
```maxon
function main() int
    var x = 42
    printInt(x)
    printInt(10 + 5)
    printInt(100)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
42
15
100
```

