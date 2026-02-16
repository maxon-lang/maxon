---
feature: parameter-labels
status: stable
keywords: [parameters, named-arguments, arguments, call-site, default-values]
category: core
---

# Named Arguments

## Documentation

All function and method calls require named arguments using colon syntax. This improves code clarity at call sites by explicitly naming parameter values.

### Named Arguments (Required)

All arguments must be named using `name: value` syntax:

```maxon
typealias Score = i64

function add(a Score, b Score) returns Score
  return a + b
end 'add'

function main() returns ExitCode
  return add(a: 3, b: 4)
end 'main'
```
```exitcode
7
```


### Named Arguments in Any Order

Named arguments can appear in any order:

```maxon
typealias Score = i64

function subtract(a Score, b Score) returns Score
  return a - b
end 'subtract'

function main() returns ExitCode
  return subtract(b: 3, a: 10)
end 'main'
```
```exitcode
7
```


### Default Parameter Values

Parameters with default values can be omitted:

```maxon
typealias Score = i64

function repeat(value Score, times Score = 1) returns Score
  return value * times
end 'repeat'

function main() returns ExitCode
  return repeat(value: 7, times: 6)
end 'main'
```
```exitcode
42
```


## Tests

<!-- test: named-args -->
```maxon

typealias Integer = i64

function add(a Integer, b Integer) returns Integer
  return a + b
end 'add'

function main() returns ExitCode
  return add(a: 3, b: 4)
end 'main'
```
```exitcode
7
```

<!-- test: named-args-multiply -->
```maxon

typealias Integer = i64

function multiply(x Integer, y Integer) returns Integer
  return x * y
end 'multiply'

function main() returns ExitCode
  return multiply(x: 6, y: 7)
end 'main'
```
```exitcode
42
```

<!-- test: named-args-any-order -->
```maxon

typealias Integer = i64

function subtract(a Integer, b Integer) returns Integer
  return a - b
end 'subtract'

function main() returns ExitCode
  return subtract(b: 3, a: 10)
end 'main'
```
```exitcode
7
```

<!-- test: default-param-named -->
```maxon

typealias Integer = i64

function repeat(value Integer, times Integer = 1) returns Integer
  return value * times
end 'repeat'

function main() returns ExitCode
  return repeat(value: 7, times: 6)
end 'main'
```
```exitcode
42
```

<!-- test: default-param-omitted -->
```maxon

typealias Integer = i64

function repeat(value Integer, times Integer = 2) returns Integer
  return value * times
end 'repeat'

function main() returns ExitCode
  return repeat(value: 21)
end 'main'
```
```exitcode
42
```

<!-- test: error-missing-param-name -->
```maxon

typealias Integer = i64

function add(a Integer, b Integer) returns Integer
  return a + b
end 'add'

function main() returns ExitCode
  return add(3, 4)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/parameter-labels/error-missing-param-name.test:10:10: Second and subsequent arguments must be named. Use 'name: value' syntax
```

<!-- test: error-unknown-param-name -->
```maxon

typealias Integer = i64

function greet(name Integer) returns Integer
  return name
end 'greet'

function main() returns ExitCode
  return greet(person: 42)
end 'main'
```
```maxoncstderr
error E3003: specs/fragments/parameter-labels/error-unknown-param-name.test:10:16: unknown parameter name: 'person'
```

