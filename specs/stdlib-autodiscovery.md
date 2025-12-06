---
feature: stdlib-autodiscovery
status: stable
keywords: [stdlib, autodiscovery, linking]
category: stdlib
---

# Standard Library Autodiscovery

## Developer Notes

The compiler automatically discovers and links stdlib functions when they're called.

Implementation:
- When a function call is unresolved, compiler searches `stdlib/` directory
- Searches recursively for `.maxon` files
- Parses file to find matching function signature
- Compiles and links the stdlib function
- Handles transitive dependencies automatically
- Example: `pow()` depends on `log()` and `exp()`, all auto-discovered

Algorithm:
1. Parse main file and identify undefined function calls
2. Search stdlib for matching declarations
3. Parse and compile those files
4. Repeat for any new undefined calls (transitive)
5. Link everything together

This eliminates need for manual imports or linking.

## Documentation

The standard library is automatically discovered and linked when you use its functions.

### How It Works

When you call a stdlib function like `pow()`, `sqrt()`, or `print()`, the compiler:
1. Searches the `stdlib/` directory
2. Finds the function definition
3. Compiles it automatically
4. Links it into your program

No imports or includes needed!

### Example

```maxon
function main() int
    // pow() is automatically found in stdlib/math/
    var result = pow(2.0, 3.0)
    return trunc(result)
end 'main'
```
```exitcode
8
```


### Transitive Dependencies

If a stdlib function depends on other stdlib functions, they're also discovered automatically. For example, `pow()` uses `log()` and `exp()`, which are all linked automatically.

## Tests

<!-- test: basic-autodiscovery -->
```maxon
function main() int
    var buffer = array of 12 byte
    return format_int_array(42, buffer)
end 'main'
```
```exitcode
2
```


<!-- test: transitive -->
```maxon
// pow -> log, exp
function main() int
    var result = pow(2.0, 3.0)
    if result > 7.5 'check'
        return 8
    end 'check'
    return 0
end 'main'
```
```exitcode
8
```


<!-- test: unqualified-call -->
```maxon
function main() int
    var result = sqrt(16.0)
    return trunc(result)
end 'main'
```
```exitcode
4
```


<!-- test: qualified-call -->
```maxon
function main() int
    var buffer = array of 12 byte
    return format_int_array(42, buffer)
end 'main'
```
```exitcode
2
```


<!-- test: wrong-arg-count -->
```maxon
function main() int
    return format_int_array(42)
end 'main'
```
```maxoncstderr
Semantic Error: line 3, column 12
Function 'format_int_array' argument count mismatch
  Expected: 2 arguments
  Found: 1 argument

  3 |     return format_int_array(42)
    |            ^
```

