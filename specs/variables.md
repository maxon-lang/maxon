---
feature: variables
status: stable
keywords: variables, let, var
category: declaration
---
# Variables

## Documentation

Maxon supports two kinds of variable declarations:

- `let` - immutable binding
- `var` - mutable binding

## Tests

<!-- test: let-declaration -->
```maxon
function main() returns ExitCode
  let x = 42
  return x
end 'main'
```
```exitcode
42
```

<!-- test: var-declaration -->
```maxon
function main() returns ExitCode
  var x = 10
  return x
end 'main'
```
```exitcode
10
```

<!-- test: multiple-variables -->
```maxon
function main() returns ExitCode
  let a = 10
  let b = 20
  return a + b
end 'main'
```
```exitcode
30
```

<!-- test: top-level-string-constant -->
Top-level `let` with a string literal value.
```maxon
let GREETING = "hello"

function main() returns ExitCode
  if GREETING == "hello" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: var-explicit-type-error -->
Explicit type annotations are not allowed on var declarations.
```maxon
function main() returns ExitCode
  var x: int = 0
  return x
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/variables/var-explicit-type-error.test:3:8: Expected '=' but got ':'
error E2004: specs/fragments/variables/var-explicit-type-error.test:4:10: Undefined variable 'x'
```

<!-- test: let-explicit-type-error -->
Explicit type annotations are not allowed on let declarations.
```maxon
function main() returns ExitCode
  let x: int = 0
  return x
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/variables/let-explicit-type-error.test:3:8: Expected '=' but got ':'
error E2004: specs/fragments/variables/let-explicit-type-error.test:4:10: Undefined variable 'x'
```
