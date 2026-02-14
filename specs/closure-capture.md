---
feature: closure-capture
status: experimental
keywords: [closure, capture, environment, gives]
category: functions
---
# Closure Variable Capture

## Documentation

Closures can capture variables from their enclosing scope. When a closure references a variable that is not one of its parameters, the value is captured at the time the closure is created.

```text
var offset = 10
var f = (x int) gives x + offset
```

Captured variables are read-only copies — changes to the original variable after the closure is created are not reflected inside the closure.

This is especially useful with higher-order functions like `map`:

```text
var multiplier = 3
var results = numbers.map((x) gives x * multiplier)
```

Use `_` as a parameter name to ignore the parameter:

```text
var values = items.map((_) gives defaultValue)
```

## Tests

<!-- test: closure-capture.basic -->
```maxon
function apply(f (int) returns int, x int) returns int
  return f(x)
end 'apply'

function main() returns int
  var offset = 7
  var result = apply(f: (n int) gives n + offset, x: 10)
  return result
end 'main'
```
```exitcode
17
```

<!-- test: closure-capture.ignore-param -->
```maxon
function apply(f (int) returns int, x int) returns int
  return f(x)
end 'apply'

function main() returns int
  var value = 42
  var result = apply(f: (_ int) gives value, x: 99)
  return result
end 'main'
```
```exitcode
42
```

<!-- test: closure-capture.struct-field -->
```maxon
function apply(f (int) returns int, x int) returns int
  return f(x)
end 'apply'

type Level
  export var rawValue int
end 'Level'

function main() returns int
  var level = Level{rawValue: 5}
  var result = apply(f: (_ int) gives level.rawValue, x: 0)
  return result
end 'main'
```
```exitcode
5
```

<!-- test: closure-capture.map-with-capture -->
```maxon
type Level
  export var rawValue int
end 'Level'

function main() returns int
  var level = Level{rawValue: 5}
  var arr = [1, 2, 3]
  var result = arr.map((_ int) gives level.rawValue)
  return result.count()
end 'main'
```
```exitcode
3
```

<!-- test: closure-capture.multiple-captures -->
```maxon
function apply(f (int) returns int, x int) returns int
  return f(x)
end 'apply'

function main() returns int
  var a = 10
  var b = 20
  var result = apply(f: (x int) gives x + a + b, x: 5)
  return result
end 'main'
```
```exitcode
35
```

<!-- test: closure-capture.no-capture-regression -->
```maxon
function apply(f (int) returns int, x int) returns int
  return f(x)
end 'apply'

function main() returns int
  var result = apply(f: (n int) gives n * 3, x: 10)
  return result
end 'main'
```
```exitcode
30
```

<!-- test: closure-capture.capture-string -->
```maxon
function apply(f (int) returns String, x int) returns String
  return f(x)
end 'apply'

function main() returns int
  var prefix = "hello"
  var result = apply(f: (_ int) gives prefix, x: 0)
  print(result)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```
