---
feature: unused-variables
status: stable
keywords: [variables, unused, diagnostics, errors]
category: diagnostics
---

# Unused Variable Detection

## Documentation

Maxon requires all local variables to be used. Declaring a variable with `var` or `let` and never referencing it causes a compilation error.

### Example Error

```maxon
function main() returns ExitCode
  var x = 42  // Error: 'x' is unused
  return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/docs-example-1.test:3:7: unused variable: 'x'
```

### Discarding Return Values

Use `let _ =` to discard a function's return value:

```maxon
typealias Integer = int(i64.min to i64.max)

function sideEffect() returns Integer
  print("hello\n")
  return 42
end 'sideEffect'

function main() returns ExitCode
  let _ = sideEffect()  // OK: underscore discards return value
  return 0
end 'main'
```

## Tests

<!-- test: unused-var -->
```maxon

function main() returns ExitCode
  var x = 42
  return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/unused-var.test:4:7: unused variable: 'x'
```

<!-- test: unused-let -->
```maxon

function main() returns ExitCode
  let x = 42
  return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/unused-let.test:4:7: unused variable: 'x'
```

<!-- test: used-var -->
```maxon

function main() returns ExitCode
  var x = 42
  return x
end 'main'
```
```exitcode
42
```

<!-- test: used-let -->
```maxon

function main() returns ExitCode
  let x = 10
  return x
end 'main'
```
```exitcode
10
```

<!-- test: underscore-discard -->
```maxon

function main() returns ExitCode
  let _ = 42
  return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/unused-variables/underscore-discard.test:4:7: expected a function call
```

<!-- test: used-in-nested-scope -->
```maxon

function main() returns ExitCode
  var x = 42
  if x > 0 'check'
    return x
  end 'check'
  return 0
end 'main'
```
```exitcode
42
```

<!-- test: tuple-destructuring-unused -->
```maxon

typealias Small = int(0 to 100)

function makePair() returns (Small, Small)
  return (10, 20)
end 'makePair'

function main() returns ExitCode
  var (a, b) = makePair()
  return a
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/tuple-destructuring-unused.test:10:11: unused variable: 'b'
```

<!-- test: multiple-unused-first-reported -->
```maxon

function main() returns ExitCode
  var x = 1
  var y = 2
  return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/multiple-unused-first-reported.test:4:7: unused variable: 'x'
```
