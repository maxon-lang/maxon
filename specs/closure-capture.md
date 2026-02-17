---
feature: closure-capture
status: experimental
keywords: [closure, capture, environment, gives]
category: functions
---
# Closure Variable Capture

## Documentation

Closures can capture variables from their enclosing scope. When a closure references a variable that is not one of its parameters, the variable is captured by reference.

```text
var offset = 10
var f = (x int) gives x + offset
```

Because captures are by reference, the closure always sees the current value of the captured variable, even if it changes after the closure is created.

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

typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function main() returns ExitCode
  var offset = 7
  var result = apply(f: (n Integer) gives n + offset, x: 10)
  return result
end 'main'
```
```exitcode
17
```

<!-- test: closure-capture.ignore-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function main() returns ExitCode
  var value = 42
  var result = apply(f: (_ Integer) gives value, x: 99)
  return result
end 'main'
```
```exitcode
42
```

<!-- test: closure-capture.struct-field -->
```maxon

typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

type Level
  export var rawValue Integer
end 'Level'

function main() returns ExitCode
  var level = Level{rawValue: 5}
  var result = apply(f: (_ Integer) gives level.rawValue, x: 0)
  return result
end 'main'
```
```exitcode
5
```

<!-- test: closure-capture.map-with-capture -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Level
  export var rawValue Integer
end 'Level'

function main() returns ExitCode
  var level = Level{rawValue: 5}
  var arr = [1, 2, 3]
  var result = arr.map((_ Integer) gives level.rawValue)
  return result.count()
end 'main'
```
```exitcode
3
```

<!-- test: closure-capture.multiple-captures -->
```maxon

typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function main() returns ExitCode
  var a = 10
  var b = 20
  var result = apply(f: (x Integer) gives x + a + b, x: 5)
  return result
end 'main'
```
```exitcode
35
```

<!-- test: closure-capture.no-capture-regression -->
```maxon

typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function main() returns ExitCode
  var result = apply(f: (n Integer) gives n * 3, x: 10)
  return result
end 'main'
```
```exitcode
30
```

<!-- test: closure-capture.capture-string -->
```maxon

typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns String, x Integer) returns String
  return f(x)
end 'apply'

function main() returns ExitCode
  var prefix = "hello"
  var result = apply(f: (_ Integer) gives prefix, x: 0)
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
