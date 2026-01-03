---
feature: stdlib-autodiscovery
status: stable
keywords: [stdlib, autodiscovery, linking]
category: stdlib
---

# Standard Library Autodiscovery

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
function main() returns int
    // pow() is automatically found in stdlib/math/
    var result = Math.pow(2.0, 3.0)
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
function main() returns int
    return trunc(sqrt(16.0))
end 'main'
```
```exitcode
4
```


<!-- test: transitive -->
```maxon
// pow -> log, exp
function main() returns int
    var result = Math.pow(2.0, 3.0)
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
function main() returns int
    var result = sqrt(16.0)
    return trunc(result)
end 'main'
```
```exitcode
4
```


<!-- test: qualified-call -->
```maxon
function main() returns int
    return trunc(Math.pow(2.0, 4.0))
end 'main'
```
```exitcode
16
```


<!-- test: wrong-arg-count -->
```maxon
function main() returns int
    return trunc(Math.pow(2.0))
end 'main'
```
```maxoncstderr
error E011: specs/fragments/stdlib-autodiscovery.wrong-arg-count.1.test:3:5: Wrong number of arguments: expected 2, got 1
```

