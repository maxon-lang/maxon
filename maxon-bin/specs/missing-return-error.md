---
feature: missing-return-error
status: stable
keywords: [return, error, semantic, validation]
category: diagnostics
---

# Missing Return Statement Error

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
error E037: specs/fragments/missing-return-error.no-return.1.test:2:10: missing return statement: 'main'
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
error E037: specs/fragments/missing-return-error.missing-else-return.1.test:2:10: missing return statement: 'test'
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

