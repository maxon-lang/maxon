---
feature: variables
status: stable
keywords: variables, let, var
category: declaration
---

## Developer Notes

Variable declarations using `let` (immutable) and `var` (mutable).

## Documentation

# Variables

Maxon supports two kinds of variable declarations:

- `let` - immutable binding
- `var` - mutable binding

## Tests

<!-- test: let-declaration -->
```maxon
function main() returns int
    let x = 42
    return x
end 'main'
```
```exitcode
42
```

<!-- test: var-declaration -->
```maxon
function main() returns int
    var x = 10
    return x
end 'main'
```
```exitcode
10
```

<!-- test: multiple-variables -->
```maxon
function main() returns int
    let a = 10
    let b = 20
    return a + b
end 'main'
```
```exitcode
30
```
