---
feature: dead-code-elimination
status: stable
keywords: [optimization, dead-code, unused-functions, ast-pruning]
category: optimization
---

# Dead Code Elimination

## Developer Notes

Dead code elimination happens at two levels:

### AST-Level Pruning (Compile-Time)
Before generating MIR, the compiler builds a call graph starting from entry points
(`main`, `_start`, `__ffi_dispatch`, and extern functions) and prunes any functions
not reachable through the call graph.

Implementation in `call_graph.cpp` and `compiler.cpp`:
- `CallGraphBuilder` traverses function bodies to extract all call sites
- Handles implicit calls: for-loop iterator methods, Equatable comparisons, string concat
- Uses name aliasing to resolve unqualified names to qualified stdlib names
- Filters `mergedProgram->functions` to only include reachable functions
- This eliminates unused stdlib functions before MIR generation

### LLVM DCE (Link-Time)
LLVM's optimization passes provide additional dead code elimination:
- When optimization is enabled (`-O1` or higher)
- GlobalDCE removes any remaining unused functions
- Eliminates unreachable basic blocks within functions
- Reduces binary size and improves performance

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
```exitcode
42
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
```exitcode
42
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
```exitcode
10
```

