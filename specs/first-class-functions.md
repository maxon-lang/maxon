---
feature: first-class-functions
status: stable
keywords: function, closure, callback, higher-order, function pointer
category: functions
---
# First-Class Functions

## Documentation

Functions in Maxon are first-class citizens. They can be stored in variables, passed as arguments to other functions, and returned from functions.

## Function Types

Function types describe the signature of a function:

```maxon
// A function that takes an int and returns an int
var transform (Integer) returns Integer

// A function that takes two ints and returns a bool
var compare (Integer, Integer) returns bool

// A function with no parameters that returns void
var callback ()
```

Named parameters can be used for documentation:

```maxon
var operation (x Integer, y Integer) returns Integer
```

## Function References

To get a reference to a function, use the function name without parentheses:

```maxon
function double(x Integer) returns Integer
  return x * 2
end 'double'

function main() returns Integer
  var f = double      // f is a function reference
  return f(21)        // calls double(21), returns 42
end 'main'
```
```exitcode
42
```

## Passing Functions as Arguments

Functions can be passed to other functions:

```maxon
function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function triple(n Integer) returns Integer
  return n * 3
end 'triple'

function main() returns Integer
  return apply(triple, x: 10)  // returns 30
end 'main'
```
```exitcode
30
```

## Closures

Closures are inline anonymous functions:

```maxon
function main() returns Integer
  var f = (x Integer) gives x * 2
  return f(21)  // returns 42
end 'main'
```
```exitcode
42
```

Closures can be passed directly to higher-order functions:

```maxon
function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function main() returns Integer
  return apply((n Integer) gives n + 5, x: 10)  // returns 15
end 'main'
```
```exitcode
15
```

## Tests

<!-- test: first-class-function.basic-reference -->
```maxon
function double(x Integer) returns Integer
  return x * 2
end 'double'

function main() returns Integer
  var f = double
  return f(21)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.pass-as-argument -->
```maxon
function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function triple(n Integer) returns Integer
  return n * 3
end 'triple'

function main() returns Integer
  return apply(triple, x: 10)
end 'main'
```
```exitcode
30
```

<!-- test: first-class-function.closure-in-variable -->
```maxon
function main() returns Integer
  var f = (x Integer) gives x * 5
  return f(8)
end 'main'
```
```exitcode
40
```

<!-- test: first-class-function.closure-as-argument -->
```maxon
function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function main() returns Integer
  return apply((n Integer) gives n + 7, x: 10)
end 'main'
```
```exitcode
17
```

<!-- test: first-class-function.multiple-params -->
```maxon
function calculate(f (Integer, Integer) returns Integer, a Integer, b Integer) returns Integer
  return f(a, b)
end 'calculate'

function add(x Integer, y Integer) returns Integer
  return x + y
end 'add'

function main() returns Integer
  return calculate(add, a: 15, b: 27)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.reassign -->
```maxon
function double(x Integer) returns Integer
  return x * 2
end 'double'

function triple(x Integer) returns Integer
  return x * 3
end 'triple'

function main() returns Integer
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
