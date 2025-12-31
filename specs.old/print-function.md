---
feature: print-function
status: stable
keywords: [print, output, stdout]
category: stdlib
---

# Print Function

## Developer Notes

The `print()` function is a stdlib function that outputs string values to stdout.

Implementation:
- Defined in `stdlib/sys/print.maxon`
- Auto-discovered by compiler when referenced
- Uses `__cstring_write_stdout()` runtime intrinsic to write to stdout
- Platform-specific: POSIX write() on Linux, WriteFile API on Windows
- Automatically adds newline after each value
- Part of the standard library, not a language keyword

To print non-string values, use string interpolation which converts values using compiler intrinsics (`__int_toString`, `__float_toString`, `__bool_toString`).

The function is linked automatically when used. No explicit import needed - the compiler's stdlib autodiscovery system finds it.

## Documentation

The `print("{}")` function outputs integer values to standard output (console).

### Syntax

```maxon
print("{value}")
```
Where `value` is an `int` expression.

### Example

```maxon
function main() returns int
    var x = 42
    print("{x}")        // Prints: 42
    print("{10 + 5}")   // Prints: 15
    print("{100}")      // Prints: 100
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


Each call to `print("{}")` outputs the value followed by a newline.

## Tests

<!-- test: basic -->
```maxon
function main() returns int
    var x = 42
    print("{x}")
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
function main() returns int
    print("{10 + 5}")
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
function main() returns int
    var x = 42
    print("{x}")
    print("{10 + 5}")
    print("{100}")
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

