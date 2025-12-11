---
feature: missing-return-error
status: stable
keywords: [return, error, semantic, validation]
category: diagnostics
---

# Missing Return Statement Error

## Developer Notes

The semantic analyzer ensures all functions that declare a return type have a return statement on all code paths.

Implementation:
- Checked in `SemanticAnalyzer::validateReturnPaths()`
- Analyzes control flow to ensure every path returns
- Checks if/else branches, while loops, etc.
- Returns error if any path doesn't end with return
- Prevents undefined behavior from missing returns

The check is conservative - some unreachable code may still trigger the error if the analyzer can't prove it's unreachable.

## Documentation

Functions that declare a return type must return a value on all code paths.

### Error Example

```maxon
function test() returns int
    // Error: No return statement
end 'test'
```
Error message:
```
Semantic Error: Function 'test' must return a value of type 'int'
Note: All execution paths through the function must end with a return statement
```

### Solution

Ensure every path returns:

```maxon
function test(x int) returns int
    if x > 0 'check'
        return 1
    end 'check'
    return 0  // Handles else case
end 'test'
```
## Tests

<!-- test: no-return -->
```maxon
function main() returns int
end 'main'
```
```maxoncstderr
Semantic Error: line 2, column 1
Function 'main' must return a value of type 'int'
  Note: All execution paths through the function must end with a return statement

  2 | function main() returns int
    | ^
```

<!-- test: missing-else-return -->
```maxon
function test(x int) returns int
    if x > 0 'check'
        return 1
    end 'check'
    // Missing return for else path
end 'test'

function main() returns int
    return test(5)
end 'main'
```
```maxoncstderr
Semantic Error: line 2, column 1
Function 'test' must return a value of type 'int'
  Note: All execution paths through the function must end with a return statement

  2 | function test(x int) returns int
    | ^
```

<!-- test: valid-all-paths -->
```maxon
function test(x int) returns int
    if x > 0 'check'
        return 1
    end 'check'
    return 0
end 'test'

function main() returns int
    return test(5)
end 'main'
```
```exitcode
1
```

