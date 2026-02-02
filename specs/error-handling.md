---
feature: error-handling
status: experimental
keywords: [error, throw, try, otherwise, throws, Error]
category: error-handling
---

# Error Handling

## Documentation

### Defining Error Types

Error types must be enums that conform to the `Error` interface:

```maxon
// Simple enum error
enum FileError is Error
  notFound
  permissionDenied
  alreadyExists
end 'FileError'

// Int-backed enum error (for error codes)
enum HttpError is Error
  badRequest = 400
  notFound = 404
  serverError = 500
end 'HttpError'

// String-backed enum error (for messages)
enum ValidationError is Error
  emptyField = "Field cannot be empty"
  invalidFormat = "Invalid format"
end 'ValidationError'
```

### Throwing Functions

Annotate functions that can throw with `throws ErrorType`:

```maxon
function readFile(path string) returns string throws FileError
  if not exists(path) 'check'
    throw FileError.notFound
  end 'check'
  return contents
end 'readFile'
```

### Error Handling with `otherwise`

The `otherwise` keyword provides unified error handling for throwing expressions. There are four forms:

#### Default Value Form

Provide a default value when an error occurs:

```maxon
let value = try mayFail() otherwise 42
```

If `mayFail()` throws, `value` is assigned `42`. The default expression must match the return type.

#### Ignore Form

Discard errors when you don't need the result:

```maxon
try mayFail() otherwise ignore
```

This silently ignores any thrown error. Use sparingly.

#### Block Handler Form

Execute a block of code when an error occurs:

```maxon
try readFile("config.json") otherwise 'handler'
  print("File not found, using defaults")
  useDefaults()
end 'handler'
```

The block executes only if an error is thrown.

#### Block with Error Binding

Capture the error for inspection:

```maxon
try readFile("config.json") otherwise (e) 'handler'
  print("Error: ")
  logError(e)
end 'handler'
```

The error is bound to `e` within the block.

### Error Propagation

Use `try` without `otherwise` to propagate errors to the caller (only valid in functions with `throws`):

```maxon
function loadConfig() returns Config throws FileError
  let contents = try readFile("config.json")
  return parse(contents)
end 'loadConfig'
```

## Tests

<!-- test: error.enum-simple-error -->
```maxon
// Simple enum error type
enum MyError is Error
  invalidInput
  notFound
end 'MyError'

function main() returns int
  return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.enum-int-backed-error -->
```maxon
// Int-backed enum error type (type inferred from values)
enum MyError is Error
  invalidInput = 1
  notFound = 404
end 'MyError'

function main() returns int
  return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.parse-throws-function-signature -->
```maxon
// Functions can declare they throw a specific error type
enum MyError is Error
  failed
end 'MyError'

// This function signature declares it throws MyError
function mayFail() returns int throws MyError
  return 10
end 'mayFail'

function main() returns int
  return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.throw-and-return-success -->
```maxon
// Test that throwing function can return success value
enum MyError is Error
  failed
end 'MyError'

function mayFail(shouldFail bool) returns int throws MyError
  if shouldFail 'check'
    throw MyError.failed
  end 'check'
  return 42
end 'mayFail'

function main() returns int
  return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.propagate-error-to-caller -->
```maxon
// Test error propagation: inner function throws, middle propagates, outer handles with otherwise
enum MyError is Error
  failed
end 'MyError'

function inner() returns int throws MyError
  throw MyError.failed
end 'inner'

function middle() returns int throws MyError
  let x = try inner()
  return x
end 'middle'

function main() returns int
  let x = try middle() otherwise 99
  return x
end 'main'
```
```exitcode
99
```

<!-- test: error.otherwise-default-value -->
```maxon
// Test try otherwise with default value
enum MyError is Error
  failed
end 'MyError'

function mayFail() returns int throws MyError
  throw MyError.failed
end 'mayFail'

function main() returns int
  let val = try mayFail() otherwise 42
  return val
end 'main'
```
```exitcode
42
```

<!-- test: error.otherwise-default-success -->
```maxon
// Test try otherwise when no error occurs
enum MyError is Error
  failed
end 'MyError'

function mayFail(shouldFail bool) returns int throws MyError
  if shouldFail 'check'
    throw MyError.failed
  end 'check'
  return 100
end 'mayFail'

function main() returns int
  let val = try mayFail(false) otherwise 42
  return val
end 'main'
```
```exitcode
100
```

<!-- test: error.otherwise-ignore -->
```maxon
// Test try otherwise ignore
enum MyError is Error
  failed
end 'MyError'

function mayFail() returns int throws MyError
  throw MyError.failed
end 'mayFail'

function main() returns int
  try mayFail() otherwise ignore
  return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.otherwise-block -->
```maxon
// Test try otherwise block handler
enum MyError is Error
  failed
end 'MyError'

function mayFail() returns int throws MyError
  throw MyError.failed
end 'mayFail'

function main() returns int
  var result = 0
  try mayFail() otherwise 'err'
    result = 42
  end 'err'
  return result
end 'main'
```
```exitcode
42
```

<!-- test: error.otherwise-block-success -->
```maxon
// Test try otherwise block when no error
enum MyError is Error
  failed
end 'MyError'

function mayFail(shouldFail bool) returns int throws MyError
  if shouldFail 'check'
    throw MyError.failed
  end 'check'
  return 100
end 'mayFail'

function main() returns int
  var result = 0
  try mayFail(false) otherwise 'err'
    result = 42
  end 'err'
  return result
end 'main'
```
```exitcode
0
```

<!-- test: error.otherwise-block-with-binding -->
```maxon
// Test try otherwise block with error binding
enum MyError is Error
  failed
end 'MyError'

function mayFail() returns int throws MyError
  throw MyError.failed
end 'mayFail'

function main() returns int
  var caught = 0
  try mayFail() otherwise 'handler'
    caught = 42
  end 'handler'
  return caught
end 'main'
```
```exitcode
42
```

<!-- test: error.main-cannot-throw -->
```maxon
// main cannot be declared with throws
enum MyError is Error
  failed
end 'MyError'

function main() returns int throws MyError
  return 42
end 'main'
```
```maxoncstderr
error E3054: specs/fragments/error-handling/error.main-cannot-throw.test:7:10: main cannot throw: 'main'
```

<!-- disabled-test: error.otherwise-type-mismatch -->
```maxon
// otherwise expression type must match the success type
enum MyError is Error
  failed
end 'MyError'

function mayFail() returns int throws MyError
  throw MyError.failed
end 'mayFail'

function main() returns int
  let val = try mayFail() otherwise "wrong type"
  return val
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/error-handling.error.otherwise-type-mismatch.1.test:12:5: type mismatch: 'otherwise type 'String' does not match expected type 'int''
```

<!-- test: error.throwing-function-requires-try -->
```maxon
// Calling a throwing function without try is an error
enum MyError is Error
  failed
end 'MyError'

function mayFail() returns int throws MyError
  return 42
end 'mayFail'

function main() returns int
  let val = mayFail()
  return val
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/error-handling/error.throwing-function-requires-try.test:12:13: throwing function requires try: 'mayFail'
```

<!-- disabled-test: error.throwing-method-requires-try -->
```maxon
typealias IntArray is Array with int

// Calling a throwing method without try is an error
function main() returns int
  let arr = IntArray{}
  let val = arr.get(0)
  return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/error-handling.error.throwing-method-requires-try.1.test:7:19: throwing function requires try: 'get'
```

<!-- test: error.try-on-non-throwing-function -->
```maxon
// Using try on a non-throwing function is an error
function noFail() returns int
  return 42
end 'noFail'

function main() returns int
  let val = try noFail() otherwise 0
  return val
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/error-handling/error.try-on-non-throwing-function.test:8:13: try requires a throwing function: ''noFail' does not throw'
```

<!-- disabled-test: error.try-on-non-throwing-method -->
```maxon
typealias IntArray is Array with int

// Using try on a non-throwing method is an error
function main() returns int
  let arr = IntArray{}
  let val = try arr.count() otherwise 0
  return val
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/error-handling.error.try-on-non-throwing-method.1.test:7:23: try requires a throwing function: ''count' does not throw'
```

<!-- disabled-test: error.otherwise-without-try -->
```maxon
typealias IntArray is Array with int

// Using otherwise without try is an error
function main() returns int
  let arr = IntArray{}
  let val = arr.get(0) otherwise 0
  return val
end 'main'
```
```maxoncstderr
error E3058: specs/fragments/error-handling.error.otherwise-without-try.1.test:7:26: otherwise requires try expression
```

<!-- test: error.otherwise-ignore-in-assignment -->
```maxon
// Using 'otherwise ignore' in an assignment is an error
enum MyError is Error
  failed
end 'MyError'

function mayFail() returns int throws MyError
  throw MyError.failed
end 'mayFail'

function main() returns int
  let val = try mayFail() otherwise ignore
  return val
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/error-handling/error.otherwise-ignore-in-assignment.test:12:13: type mismatch: ''otherwise ignore' cannot be used in assignment'
```
