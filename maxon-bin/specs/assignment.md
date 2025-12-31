---
feature: assignment
status: stable
keywords: [assignment, equals, mutation]
category: statements
---

# Assignment Statement

## Developer Notes

Assignment updates the value of a mutable variable (declared with `var`).

Implementation:
- Parsed in `Parser::parseAssignmentStatement()`
- Syntax: `identifier = expression`
- Represented by `AssignmentStmt` AST node
- Type checker ensures variable is mutable (not `let`)
- Type checker ensures expression type matches variable type
- Code generation uses LLVM `store` instruction
- Variable must already be declared

Cannot assign to:
- `let` variables (immutable)
- Function names
- Undeclared identifiers

## Documentation

The assignment operator `=` updates the value of a mutable variable.

### Syntax

```maxon
variable = expression
```
### Example

```maxon
function main() returns int
    var x = 10
    x = 20          // Update x
    x = x + 5       // x is now 25
    return x
end 'main'
```
```exitcode
25
```


### Restrictions

- Cannot assign to `let` variables
- Variable must be declared with `var`
- Expression type must match variable type

## Tests

<!-- test: basic-assignment -->
```maxon
function main() returns int
    var x = 3
    x = x + 2
    return x
end 'main'
```
```exitcode
5
```


<!-- test: multiple-assignments -->
```maxon
function main() returns int
    var x = 10
    var y = 20
    x = y
    y = 30
    return x + y
end 'main'
```
```exitcode
50
```


<!-- test: assignment-in-loop -->
```maxon
function main() returns int
    var sum = 0
    var i = 1
    while i <= 5 'loop'
        sum = sum + i
        i = i + 1
    end 'loop'
    return sum
end 'main'
```
```exitcode
15
```

