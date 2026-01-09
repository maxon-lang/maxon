---
feature: error-handling
status: experimental
keywords: [error, throw, try, catch, do, throws, Error]
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

### Handling Blocks (do-catch)

Use `do-catch` blocks for handling throwing calls:

```maxon
do 'io'
    let config = try readFile("config.json")
    let data = try readFile("data.json")
    process(config, data)
end 'io' catch (e FileError) 'fileErr'
    print("File error occurred")
end 'fileErr' catch (e) 'any'
    print("Unknown error")
end 'any'
```

### Error Propagation

Use `try` to propagate errors to the caller:

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

<!-- test: error.enum-string-backed-error -->
```maxon
// String-backed enum error type (type inferred from values)
enum MyError is Error
    invalidInput = "Invalid input"
    notFound = "Not found"
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

<!-- test: error.do-catch-handles-thrown-error -->
```maxon
// Test do-catch catches thrown errors
enum MyError is Error
    failed = 42
end 'MyError'

function mayFail(shouldFail bool) returns int throws MyError
    if shouldFail 'check'
        throw MyError.failed
    end 'check'
    return 100
end 'mayFail'

function main() returns int
    do 'io'
        let x = try mayFail(true)
        return x
    end 'io' catch (e MyError) 'err'
        // e is the enum ordinal (0 for first member)
        return 42
    end 'err'
end 'main'
```
```exitcode
42
```

<!-- test: error.do-catch-success-case -->
```maxon
// Test do-catch with no error thrown
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
    do 'io'
        let x = try mayFail(false)
        return x
    end 'io' catch (e MyError) 'err'
        return 0
    end 'err'
end 'main'
```
```exitcode
100
```

<!-- test: error.propagate-error-to-caller -->
```maxon
// Test error propagation: inner function throws, middle propagates, outer catches
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
    do 'io'
        let x = try middle()
        return x
    end 'io' catch (e MyError) 'err'
        return 99
    end 'err'
end 'main'
```
```exitcode
99
```

