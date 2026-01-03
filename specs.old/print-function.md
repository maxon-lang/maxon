---
feature: print-function
status: stable
keywords: [print, output, stdout]
category: stdlib
---

# Print Function

## Documentation

The `print("{}\n")` function outputs integer values to standard output (console).

### Syntax

```maxon
print("{value}\n")
```
Where `value` is an `int` expression.

### Example

```maxon
function main() returns int
    var x = 42
    print("{x}\n")        // Prints: 42
    print("{10 + 5}\n")   // Prints: 15
    print("{100}\n")      // Prints: 100
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


Each call to `print("{}\n")` outputs the value followed by a newline.

## Tests

<!-- test: basic -->
```maxon
function main() returns int
    var x = 42
    print("{x}\n")
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
    print("{10 + 5}\n")
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
    print("{x}\n")
    print("{10 + 5}\n")
    print("{100}\n")
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

