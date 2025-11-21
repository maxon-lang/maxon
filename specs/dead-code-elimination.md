---
feature: dead-code-elimination
status: stable
keywords: [optimization, dead-code, unused-functions]
category: optimization
---

# Dead Code Elimination

## Developer Notes

LLVM's optimization passes automatically remove unused functions.

Implementation:
- Not implemented by Maxon directly - handled by LLVM
- When optimization is enabled (`-O1` or higher)
- Functions not reachable from `main()` are eliminated
- Reduces binary size and improves performance
- Also eliminates unreachable basic blocks within functions

The compiler generates all functions, but LLVM's GlobalDCE (Dead Code Elimination) pass removes unused ones during linking.

## Documentation

The compiler automatically removes functions that are never called (dead code).

### How It Works

When optimization is enabled, functions that aren't called (directly or indirectly) from `main()` are eliminated from the final binary.

### Example

```maxon
function used() int
    return 42
end 'used'

function unused() int
    return 999  // This function is eliminated
end 'unused'

function main() int
    return used()  // Only this is kept
end 'main'
```

The `unused()` function won't appear in the optimized output.

## Tests

<!-- test: eliminates-unused -->
```maxon
function used_function() int
    return 42
end 'used_function'

function unused_function() int
    return 999
end 'unused_function'

function another_unused_function() int
    return 123
end 'another_unused_function'

function main() int
    var result = used_function()
    return result
end 'main'
```
```
ExitCode: 42
```

<!-- test: keeps-transitive-calls -->
```maxon
function helper() int
    return 10
end 'helper'

function used() int
    return helper()
end 'used'

function unused() int
    return 999
end 'unused'

function main() int
    return used()
end 'main'
```
```
ExitCode: 10
```
