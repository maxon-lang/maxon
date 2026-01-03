---
feature: ownership
status: experimental
keywords: [ownership, move, borrow, mutation]
category: semantic-analysis
---

# Compile-Time Ownership System

## Documentation

Maxon uses compile-time ownership tracking to prevent use-after-move errors.

### Borrowing vs Moving

When you pass a variable to a function:
- If the function **only reads** the parameter: the variable is **borrowed** (caller keeps ownership)
- If the function **mutates** the parameter: ownership is **transferred** (caller loses ownership)

### Example

```maxon
function main() returns int
    var a = 42
    var b = foo(a)  // foo only reads z, so a is borrowed
    a = a + 1       // OK - a still owned by main
    bar(a)          // bar mutates z, so ownership transfers
    a = a + 1       // ERROR: a no longer owned
    return a + b
end 'main'

function foo(z int) returns int
    return z + 4    // only reads z
end 'foo'

function bar(z int)
    z = z + 1       // mutates z -> takes ownership
end 'bar'
```

```maxoncstderr
Error: Cannot use 'a' after ownership was transferred to 'bar'
```

<!-- test: basic-borrow-ok -->
```maxon
function main() returns int
    var a = 42
    var b = foo(a)
    var c = a + b
    return c
end 'main'

function foo(z int) returns int
    return z + 4
end 'foo'
```
```exitcode
88
```

<!-- test: use-after-move -->
```maxon
function main() returns int
    var a = 42
    bar(a)
    return a
end 'main'

function bar(z int)
    z = z + 1
end 'bar'
```
```maxoncstderr
Cannot use variable 'a' after ownership was transferred
