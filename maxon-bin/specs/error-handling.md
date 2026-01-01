---
feature: error-handling
status: experimental
keywords: [error, throw, try, catch, do, throws, Error]
category: error-handling
---

# Error Handling

## Developer Notes

Swift-style error handling with typed errors. Error unions (`T or E` where E conforms to Error) are distinct from optionals (`T or nil`).

**Key Concepts:**
- `Error` - Empty marker interface that only enums can conform to
- `throws E` - Function signature annotation for throwing functions
- `throw expr` - Throw an error enum value
- `try expr` - Propagate error to caller
- `do-catch` - Block-level error handling with pattern matching

**Tokens Added:**
- `throws`, `throw`, `try`, `catch`, `do`

**AST Nodes:**
- `ErrorUnionTypeExpr` - For `T or E` where E conforms to Error
- `ThrowStmt` - For `throw expr` statements
- `TryExpr` with `TryMode` (.propagate)
- `DoCatchStmt` and `CatchClause` for do-catch blocks
- `EnumDecl` now supports `conformances` field for interface conformance

**Function/Method Changes:**
- `FunctionDecl`, `MethodDecl`, `InterfaceMethod` have `throws_type: ?[]const u8`

**Type System:**
- `ErrorUnionInfo` holds success type and error enum type
- `error_union_type` variant in `ValueType`
- `EnumTypeInfo` has `is_error` flag for Error-conforming enums

**Memory Layout:**
Same discriminated union pattern as optionals:
```
+--------+--------------------------------+
| tag(8) | value OR error ordinal         |
+--------+--------------------------------+
   0=ok    success value
   1=err   enum ordinal (8 bytes)
```

**Semantic Rules:**
- Only enums conforming to Error can be thrown (E023 for struct attempts)
- `throw` only valid in functions with `throws` annotation
- `try` requires the called function to throw
- Unhandled throwing calls are compile errors

**Implementation Status:**
- [x] Lexer tokens
- [x] AST nodes
- [x] Parser support (including enum conformance)
- [x] Error interface in stdlib
- [x] IR generation for throw/try/catch
- [x] Code generation
- [x] Enum-only Error enforcement

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
enum HttpError int is Error
    badRequest = 400
    notFound = 404
    serverError = 500
end 'HttpError'

// String-backed enum error (for messages)
enum ValidationError string is Error
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
catch e FileError 'fileErr'
    print("File error occurred")
catch e 'any'
    print("Unknown error")
end 'io'
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
// Int-backed enum error type
enum MyError int is Error
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
// String-backed enum error type
enum MyError string is Error
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
enum MyError int is Error
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
    catch e MyError 'err'
        // e is the enum ordinal (0 for first member)
        return 42
    end 'io'
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
    catch e MyError 'err'
        return 0
    end 'io'
end 'main'
```
```exitcode
100
```
