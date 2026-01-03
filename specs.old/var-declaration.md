---
feature: var-declaration
status: stable
keywords: var, variable, declaration, mutable
category: declaration
---
# Variable Declaration (var)

## Documentation

Declare a mutable variable that can be reassigned.

**Syntax:**

```maxon
var <name> = <initializer>
var <name> <type> = <initializer>
```
**Example:**

```maxon
var x = 42              // Type inferred as int
var y int = 10          // Explicit type
var result = x + y      // result is 52
```
**Example with reassignment:**

```maxon
var x = 3
x = x + 2               // OK - var is mutable
// x is now 5
```
**Notes:**
- Variables declared with `var` are mutable
- Type can be inferred from initializer
- All variables must be initialized at declaration
- Scope is function-local

## Tests

<!-- test: var-declaration.basic -->
```maxon
function main() returns int
    var x = 42
    var y = 10
    var result = x + y
    return result
end 'main'
```
```exitcode
52
```

<!-- test: var-declaration.reassignment -->
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
