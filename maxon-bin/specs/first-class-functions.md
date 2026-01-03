---
feature: first-class-functions
status: stable
keywords: function, closure, callback, higher-order, function pointer
category: functions
---
## Documentation

# First-Class Functions

Functions in Maxon are first-class citizens. They can be stored in variables, passed as arguments to other functions, and returned from functions.

## Function Types

Function types describe the signature of a function:

```maxon
// A function that takes an int and returns an int
var transform (int) returns int

// A function that takes two ints and returns a bool
var compare (int, int) returns bool

// A function with no parameters that returns void
var callback ()
```

Named parameters can be used for documentation:

```maxon
var operation (x int, y int) returns int
```

## Function References

To get a reference to a function, use the function name without parentheses:

```maxon
function double(x int) returns int
    return x * 2
end 'double'

function main() returns int
    var f = double      // f is a function reference
    return f(21)        // calls double(21), returns 42
end 'main'
```

## Passing Functions as Arguments

Functions can be passed to other functions:

```maxon
function apply(f (int) returns int, x int) returns int
    return f(x)
end 'apply'

function triple(n int) returns int
    return n * 3
end 'triple'

function main() returns int
    return apply(triple, 10)  // returns 30
end 'main'
```

## Closures

Closures are inline anonymous functions:

```maxon
function main() returns int
    var f = (x int) gives x * 2
    return f(21)  // returns 42
end 'main'
```

Closures can be passed directly to higher-order functions:

```maxon
function apply(f (int) returns int, x int) returns int
    return f(x)
end 'apply'

function main() returns int
    return apply((n int) gives n + 5, 10)  // returns 15
end 'main'
```

## Tests

<!-- test: first-class-function.basic-reference -->
```maxon
function double(x int) returns int
    return x * 2
end 'double'

function main() returns int
    var f = double
    return f(21)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.pass-as-argument -->
```maxon
function apply(f (int) returns int, x int) returns int
    return f(x)
end 'apply'

function triple(n int) returns int
    return n * 3
end 'triple'

function main() returns int
    return apply(triple, 10)
end 'main'
```
```exitcode
30
```

<!-- test: first-class-function.closure-in-variable -->
```maxon
function main() returns int
    var f = (x int) gives x * 5
    return f(8)
end 'main'
```
```exitcode
40
```

<!-- test: first-class-function.closure-as-argument -->
```maxon
function apply(f (int) returns int, x int) returns int
    return f(x)
end 'apply'

function main() returns int
    return apply((n int) gives n + 7, 10)
end 'main'
```
```exitcode
17
```

<!-- test: first-class-function.multiple-params -->
```maxon
function calculate(f (int, int) returns int, a int, b int) returns int
    return f(a, b)
end 'calculate'

function add(x int, y int) returns int
    return x + y
end 'add'

function main() returns int
    return calculate(add, 15, 27)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.reassign -->
```maxon
function double(x int) returns int
    return x * 2
end 'double'

function triple(x int) returns int
    return x * 3
end 'triple'

function main() returns int
    var f = double
    var a = f(10)
    f = triple
    var b = f(10)
    return a + b
end 'main'
```
```exitcode
50
```
