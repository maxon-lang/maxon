---
feature: for-loops
status: stable
keywords: [for, in, loop, iterator, iteration, range]
category: control-flow
---

# For Loops

## Documentation

For loops provide convenient iteration over ranges and collections using an iterator interface defined by the standard library.

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
function main() returns int
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
function main() returns int
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
function main() returns int
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
while 1 = 1 'loop'
    var __opt = __iter.next()
    if let i = __opt 'check'
        print(i)
    end 'check' else 'break'
        break 'loop'
    end 'break'
end 'loop'
```

The standard library (automatically available) provides:
- `Iterator` type definition (exported from `stdlib/iter/iterator.maxon`)
- `next(Iterator) int or nil` - Returns next element or nil if exhausted
- `range(int, int) Iterator` - Create range iterator (from `stdlib/iter/range.maxon`)

This design separates concerns:
- **Compiler**: Syntax sugar only
- **Standard Library**: All iteration logic (auto-discovered, no imports needed)

### Iterator Interface

The iterator interface is a contract defined by the standard library. Any type that implements the `next()` function can be used with for loops:

- `Iterator` type with fields: `current`, `limit`, `step`
- `next(Iterator) int or nil` - Returns the next element value, or nil if no more elements exist

The `Iterator` type is exported from `stdlib/iter/iterator.maxon` and can be used in any file with the qualified name `Iterator`.

## Tests

<!-- test: basic-range -->
```maxon
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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

<!-- test: error.loop-var-assign -->
```maxon
function main() returns int
    var sum = 0
    for i in range(1, 5) 'loop'
        i = 10
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:5:11
Cannot assign to loop variable 'i'
  Loop variable declared at line 4, column 5
  Note: Loop iteration variables are immutable and cannot be reassigned

  5 |         i = 10
    |           ^
```


<!-- test: error.iterate-over-int -->
```maxon
function main() returns int
    var sum = 0
    let count = 5
    for i in count 'loop'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:5:14
Cannot iterate over type 'int'
  For-loops require an iterable type: array, string, range()

  5 |     for i in count 'loop'
    |              ^
```

<!-- test: error.iterate-over-bool -->
```maxon
function main() returns int
    var sum = 0
    let flag = true
    for i in flag 'loop'
        sum = sum + 1
    end 'loop'
    return sum
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:5:14
Cannot iterate over type 'bool'
  For-loops require an iterable type: array, string, range()

  5 |     for i in flag 'loop'
    |              ^

Semantic Error: temp_fragment.maxon:5:5
The variable 'i' is assigned but its value is never used

  5 |     for i in flag 'loop'
    |     ^
```

<!-- test: error.iterate-over-float -->
```maxon
function main() returns int
    var sum = 0
    let val = 3.14
    for i in val 'loop'
        sum = sum + 1
    end 'loop'
    return sum
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:5:14
Cannot iterate over type 'float'
  For-loops require an iterable type: array, string, range()

  5 |     for i in val 'loop'
    |              ^

Semantic Error: temp_fragment.maxon:5:5
The variable 'i' is assigned but its value is never used

  5 |     for i in val 'loop'
    |     ^
```
