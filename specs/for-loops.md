---
feature: for-loops
status: stable
keywords: [for, in, loop, iterator, iteration, range]
category: control-flow
---

# For Loops

## Developer Notes

For loops in Maxon use an iterator-based approach where the compiler provides syntax sugar and the standard library provides the iteration logic.

### Architecture

**Compiler Responsibility:**
- Parse `for <var> in <iterable>` syntax
- Desugar to while loop with iterator calls
- Manage loop variable scope

**Standard Library Responsibility:**
- Define `Iterator` struct contract
- Implement `hasNext()`, `getCurrent()`, `next()` functions  
- Provide concrete iterators (range, array, etc.)
- Implement map/filter/reduce using iterator interface

### Implementation Details

**Lexer** (`maxon-bin/lexer.cpp`):
- Keywords: `for`, `in` (ControlFlow category)

**AST** (`maxon-bin/ast.h`):
- `ForStmtAST`: loopVar (string), iterable (ExprAST*), body (vector<StmtAST*>)

**Parser** (`maxon-bin/parser.cpp`):
- `parseFor()`: Parses `for <var> in <expr> 'id' ... end 'id'`
- Requires block identifiers like other control flow

**Semantic Analysis** (`maxon-bin/semantic_analyzer.cpp`):
- Declares loop variable as immutable (like `let`)
- Increments `loopDepth` for break/continue validation
- Infers loop variable type from iterable (array element type or `int` for range)

**Code Generation** (`maxon-bin/codegen.cpp`):
- Desugars to:
  ```llvm
  var __iter = <iterable>
  while hasNext(&__iter) = 1
      let <loopVar> = getCurrent(&__iter)
      <body>
      __iter = next(&__iter)
  end
  ```
- Uses suffix matching to find `hasNext`, `getCurrent`, `next`
- Structs passed by pointer (Maxon calling convention)
- Loop variable scope managed via namedValues map

**Standard Library** (`stdlib/iter/`):
- `iterator.maxon`: Defines `Iterator` struct and core functions
- `range.maxon`: Provides `range(start, end)` function
- `array.maxon`: Provides array iteration (future)

### Iterator Protocol

The iterator interface is a contract defined by the standard library in `stdlib/iter/iterator.maxon`:

- `Iterator` struct with fields: `current`, `limit`, `step`
- `hasNext(Iterator) int` - Returns 1 if more elements exist, 0 otherwise
- `getCurrent(Iterator) int` - Returns current element value
- `next(Iterator) Iterator` - Returns new iterator advanced by step

Any type that implements these three functions can be used with for loops.

## Documentation

For loops provide convenient iteration over ranges and collections using an iterator protocol defined by the standard library.

### Syntax

```maxon
for <variable> in <iterable> 'identifier'
    <statements>
end 'identifier'
```
- **variable**: Loop variable name (immutable, scoped to loop body)
- **iterable**: Expression that returns an iterator (e.g., `range(0, 10)`)
- **identifier**: Block identifier (must match on `for` and `end` lines)

### Range-Based Iteration

The `range()` function creates an iterator over integers. It's automatically available from the standard library:

```maxon
for i in range(0, 5) 'loop'
    print(i)  // Prints 0, 1, 2, 3, 4
end 'loop'
```
**Range is half-open:** `range(0, 5)` iterates over `[0, 5)` (0, 1, 2, 3, 4).

### Example: Sum of Range

```maxon
function main() int
    var sum = 0
    for i in range(0, 10) 'sum_loop'
        sum = sum + i
    end 'sum_loop'
    return sum  // Returns 45 (0+1+2+...+9)
end 'main'
```
```exitcode
45
```


### Loop Control

For loops support `break` and `continue` statements:

```maxon
function main() int
    var sum = 0
    for i in range(0, 10) 'loop'
        if i == 5 'skip'
            continue  // Skip 5
        end 'skip'
        if i == 8 'stop'
            break  // Stop at 8
        end 'stop'
        sum = sum + i
    end 'loop'
    return sum  // Returns 23 (0+1+2+3+4+6+7)
end 'main'
```
```exitcode
23
```


### Loop Variable Scope

The loop variable is:
- **Immutable**: Cannot be reassigned within the loop body
- **Block-scoped**: Only visible within the loop
- **Fresh each iteration**: Gets new value from iterator each time

```maxon
function main() int
    var last = 0
    for i in range(1, 4) 'loop'
        last = i  // Can copy value to mutable variable
        // i = 5  // ERROR: Loop variable is immutable
    end 'loop'
    // print(i)  // ERROR: i not in scope here
    return last  // Returns 3
end 'main'
```
```exitcode
3
```


### How It Works

The compiler desugars for loops to iterator-based while loops. This code:

```text
for i in range(0, 3) 'loop'
    print(i)
end 'loop'
```

Becomes:

```text
var __iter = range(0, 3)
while hasNext(__iter) = 1 'loop'
    let i = getCurrent(__iter)
    print(i)
    __iter = next(__iter)
end 'loop'
```

The standard library (automatically available) provides:
- `Iterator` struct definition (exported from `stdlib/iter/iterator.maxon`)
- `hasNext(Iterator) int` - Check if more elements exist
- `getCurrent(Iterator) int` - Get current element
- `next(Iterator) Iterator` - Advance to next element
- `range(int, int) Iterator` - Create range iterator (from `stdlib/iter/range.maxon`)

This design separates concerns:
- **Compiler**: Syntax sugar only
- **Standard Library**: All iteration logic (auto-discovered, no imports needed)

### Iterator Protocol

The iterator interface is a contract defined by the standard library. Any type that implements the three required functions can be used with for loops:

- `Iterator` struct with fields: `current`, `limit`, `step`
- `hasNext(Iterator) int` - Returns 1 if more elements exist, 0 otherwise
- `getCurrent(Iterator) int` - Returns current element value
- `next(Iterator) Iterator` - Returns new iterator advanced by step

The `Iterator` struct is exported from `stdlib/iter/iterator.maxon` and can be used in any file with the qualified name `Iterator`.

## Tests

<!-- test: basic-range -->
```maxon
function main() int
    var sum = 0
    for i in range(0, 5) 'loop'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
10
```


<!-- test: break-in-for-loop -->
```maxon
function main() int
    var sum = 0
    for i in range(0, 10) 'loop'
        if i == 5 'check'
            break
        end 'check'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
10
```


<!-- test: continue-in-for-loop -->
```maxon
function main() int
    var sum = 0
    for i in range(0, 10) 'loop'
        if i == 5 'skip'
            continue
        end 'skip'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
40
```


<!-- test: nested-for-loops -->
```maxon
function main() int
    var sum = 0
    for i in range(0, 3) 'outer'
        for j in range(0, 3) 'inner'
            sum = sum + (i * 10) + j
        end 'inner'
    end 'outer'
    return sum
end 'main'
```
```exitcode
99
```


<!-- test: float-array-iteration -->
```maxon
function main() int
    let arr = [1.0, 2.0, 3.0, 4.0, 5.0]
    var sum = 0.0
    for x in arr 'loop'
        sum = sum + x
    end 'loop'
    return trunc(sum)
end 'main'
```
```exitcode
15
```

