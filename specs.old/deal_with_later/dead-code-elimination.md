---
feature: dead-code-elimination
status: stable
keywords: [optimization, dead-code, unused-functions, ast-pruning]
category: optimization
---

# Dead Code Elimination

## Documentation

The compiler automatically removes functions that are never called (dead code).

### How It Works

When optimization is enabled, functions that aren't called (directly or indirectly) from `main()` are eliminated from the final binary.

### Example

```maxon
function used() returns int
    return 42
end 'used'

function unused() returns int
    return 999  // This function is eliminated
end 'unused'

function main() returns int
    return used()  // Only this is kept
end 'main'
```
```exitcode
42
```


The `unused()` function won't appear in the optimized output.

## Tests

<!-- test: eliminates-unused -->
```maxon
function used_function() returns int
    return 42
end 'used_function'

function unused_function() returns int
    return 999
end 'unused_function'

function another_unused_function() returns int
    return 123
end 'another_unused_function'

function main() returns int
    var result = used_function()
    return result
end 'main'
```
```exitcode
42
```


<!-- test: keeps-transitive-calls -->
```maxon
function helper() returns int
    return 10
end 'helper'

function used() returns int
    return helper()
end 'used'

function unused() returns int
    return 999
end 'unused'

function main() returns int
    return used()
end 'main'
```
```exitcode
10
```

