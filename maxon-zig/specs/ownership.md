---
feature: ownership
status: stable
keywords: ownership, borrow, move
category: semantics
---

# Developer Notes

Maxon uses inferred ownership and borrowing semantics:

- If a function **reads** a parameter without modifying it, the caller **borrows** the value (retains ownership)
- If a function **modifies** a parameter (assigns to it), the caller **transfers ownership** (moves the value)
- Using a variable after it has been moved is a compile-time error

The compiler performs a mutation analysis pass to determine which function parameters are modified, then tracks ownership state during semantic analysis.

# Documentation

## Ownership Model

Every value in Maxon has a single owner. When you pass a value to a function:

- **Borrow**: If the function only reads the parameter, you retain ownership and can continue using the variable
- **Move**: If the function modifies the parameter, ownership transfers to the function and you can no longer use the variable

This is determined automatically by analyzing what each function does with its parameters.

### Example

```maxon
function main() returns int
    var a = 42
    var b = foo(a)  // foo only reads a -> borrow, a still usable
    a = a + 1       // OK, we still own a
    bar(a)          // bar modifies its param -> ownership transfers
    // a = a + 1    // ERROR: a was moved to bar
    return b
end 'main'

function foo(z int) returns int
    return z + 4    // z is only read -> borrow
end 'foo'

function bar(z int)
    z = z + 1       // z is modified -> takes ownership
end 'bar'
```

## Tests

<!-- test: reassignment -->
Variables can be reassigned as long as they haven't been moved.

```maxon
function main() returns int
    var a = 10
    a = 20
    a = 30
    return a
end 'main'
```
```exitcode
30
```

<!-- test: multiple-assignments -->
Multiple assignments in sequence work correctly.

```maxon
function main() returns int
    var a = 1
    var b = 2
    a = a + b
    b = a + b
    return b
end 'main'
```
```exitcode
5
```

<!-- test: assign-expression -->
Assignment with complex expression on the right side.

```maxon
function main() returns int
    var a = 5
    var b = 3
    a = a * b + 2
    return a
end 'main'
```
```exitcode
17
```

<!-- test: reassign-to-self -->
Reassigning a variable to itself plus something.

```maxon
function main() returns int
    var x = 10
    x = x + 5
    x = x + 5
    return x
end 'main'
```
```exitcode
20
```

<!-- test: chain-assignments -->
Chain of assignments between variables.

```maxon
function main() returns int
    var a = 1
    var b = 2
    var c = 3
    a = b
    b = c
    c = a + b + c
    return c
end 'main'
```
```exitcode
8
```

<!-- test: var-after-let -->
Mutable variable can be reassigned after immutable declaration.

```maxon
function main() returns int
    let x = 10
    var y = x
    y = y + 5
    return y
end 'main'
```
```exitcode
15
```

<!-- test: multiple-vars-reassign -->
Multiple variables can each be reassigned.

```maxon
function main() returns int
    var a = 1
    var b = 2
    var c = 3
    a = 10
    b = 20
    c = 30
    return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: reassign-arithmetic -->
Reassignment with various arithmetic operations.

```maxon
function main() returns int
    var x = 100
    x = x - 50
    x = x * 2
    x = x / 5
    return x
end 'main'
```
```exitcode
20
```
