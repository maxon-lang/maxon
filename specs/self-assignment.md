---
feature: self-assignment
status: stable
keywords: [assignment, diagnostics, errors]
category: diagnostics
---

# Self-Assignment Detection

## Documentation

Maxon detects self-assignment — assigning a variable to itself — and reports it as a compile error, since it has no effect and is likely a bug. Similarly, discarding a non-call expression with `let _ =` is an error.

### Example Errors

```maxon
function main() returns ExitCode
  var x = 42
  x = x  // Error: self-assignment has no effect
  return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/self-assignment/docs-example-1.test:4:3: self-assignment has no effect: 'x = x'
```

```maxon
function main() returns ExitCode
  let _ = 0  // Error: discarding a non-call expression has no effect
  return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/self-assignment/docs-example-2.test:3:7: discarding a non-call expression has no effect
```

## Tests

<!-- test: self-assignment.basic -->
```maxon

function main() returns ExitCode
  var x = 10
  x = x
  return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/self-assignment/self-assignment.basic.test:5:3: self-assignment has no effect: 'x = x'
```

<!-- test: self-assignment.different-var -->
```maxon

function main() returns ExitCode
  var x = 10
  var y = 20
  x = y
  return x
end 'main'
```
```exitcode
20
```

<!-- test: self-assignment.expr-not-flagged -->
```maxon

function main() returns ExitCode
  var x = 10
  x = x + 1
  return x
end 'main'
```
```exitcode
11
```

<!-- test: self-assignment.field-self-assign -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var p = Point{x: 1, y: 2}
  p.x = p.x
  return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/self-assignment/self-assignment.field-self-assign.test:12:3: self-assignment has no effect: 'p.x = p.x'
```

<!-- test: self-assignment.discard-literal -->
```maxon

function main() returns ExitCode
  let _ = 42
  return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/self-assignment/self-assignment.discard-literal.test:4:7: discarding a non-call expression has no effect
```
