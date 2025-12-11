---
feature: return-statement
status: stable
keywords: [return, control-flow]
category: statements
---

# Return Statement

## Developer Notes

The `return` statement exits a function and optionally provides a value to the caller. Implementation details:

- Parsed in `Parser::parseReturnStatement()`
- Represented by `ReturnStmt` AST node
- Must match the function's declared return type
- Generates LLVM `ret` instruction
- Type checking ensures the expression type matches function return type
- All code paths in a function must return (validated by semantic analyzer)

The return value expression is evaluated first, then the function exits. There is no implicit return for functions - all functions must explicitly return.

## Documentation

The `return` statement exits the current function and optionally returns a value to the caller.

### Syntax

```maxon
return expression
```
### Example

```maxon
function isPositive(x int) returns bool
    if x > 0 'check'
        return true
    end 'check'
    return false
end 'isPositive'
```
## Tests

<!-- test: simple-return -->
```maxon
function main() returns int
    return 42
end 'main'
```
```exitcode
42
```


<!-- test: expression-return -->
```maxon
function main() returns int
    return 2 + 3 * 4
end 'main'
```
```exitcode
14
```


<!-- test: conditional-return -->
```maxon
function main() returns int
    var x = 5
    if x > 3 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

