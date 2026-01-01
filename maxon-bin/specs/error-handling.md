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
- `Error` - Empty marker interface (like Swift's Error protocol)
- `throws E` - Function signature annotation for throwing functions
- `throw expr` - Throw an error value
- `try expr` - Propagate error to caller
- `do-catch` - Block-level error handling with pattern matching

**Tokens Added:**
- `throws`, `throw`, `try`, `catch`, `do`

**AST Nodes:**
- `ErrorUnionTypeExpr` - For `T or E` where E conforms to Error
- `ThrowStmt` - For `throw expr` statements
- `TryExpr` with `TryMode` (.propagate)
- `DoCatchStmt` and `CatchClause` for do-catch blocks

**Function/Method Changes:**
- `FunctionDecl`, `MethodDecl`, `InterfaceMethod` have `throws_type: ?[]const u8`

**Type System:**
- `ErrorUnionInfo` holds success type and error type
- `error_union_type` variant in `ValueType`

**Memory Layout:**
Same discriminated union pattern as optionals:
```
+--------+--------------------------------+
| tag(8) | value OR error                 |
+--------+--------------------------------+
   0=ok    success value
   1=err   error value
```

**Semantic Rules:**
- Only types conforming to Error can be thrown
- `throw` only valid in functions with `throws` annotation
- `try` requires the called function to throw
- Unhandled throwing calls are compile errors

**Implementation Status:**
- [x] Lexer tokens
- [x] AST nodes
- [x] Parser support
- [x] Error interface in stdlib
- [x] IR generation for throw/try/catch
- [x] Code generation

## Documentation

### Defining Error Types

Create error types by conforming to the `Error` interface:

```maxon
type FileError is Error
    var code int
    var path string
end 'FileError'
```

### Throwing Functions

Annotate functions that can throw with `throws ErrorType`:

```maxon
function readFile(path string) returns string throws FileError
    if not exists(path) 'check'
        throw FileError{code: 404, path: path}
    end 'check'
    return contents
end 'readFile'
```

### Handling Single Calls (if-let)

Use `if let` with an `else` clause to handle errors from a single call:

```maxon
if let contents = readFile("data.txt") 'ok'
    print(contents)
else e 'err'
    print("Error: {e.path}")
end 'ok'
```

### Handling Blocks (do-catch)

Use `do-catch` blocks for handling multiple throwing calls:

```maxon
do 'io'
    let config = try readFile("config.json")
    let data = try readFile("data.json")
    process(config, data)
catch e FileError 'fileErr'
    print("File error: {e.path}")
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

<!-- test: error.parse-error-type -->
```maxon
// Error types conform to the Error interface
type MyError is Error
    var code int
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
type MyError is Error
    var code int
end 'MyError'

// This function signature declares it throws MyError
// (body doesn't actually throw - just testing signature parsing)
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

<!-- test: error.parse-error-with-multiple-fields -->
```maxon
// Error types can have multiple fields
type DetailedError is Error
    var code int
    var line int
    var column int
end 'DetailedError'

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
type MyError is Error
    var code int
end 'MyError'

function mayFail(shouldFail bool) returns int throws MyError
    if shouldFail 'check'
        throw MyError{code: 1}
    end 'check'
    return 42
end 'mayFail'

function main() returns int
    // For now, just verify the function can be called
    // (without try, we can't actually call it - testing just the parse/compile)
    return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.do-catch-handles-thrown-error -->
```maxon
// Test do-catch catches thrown errors
type MyError is Error
    var code int
end 'MyError'

function mayFail(shouldFail bool) returns int throws MyError
    if shouldFail 'check'
        throw MyError{code: 42}
    end 'check'
    return 100
end 'mayFail'

function main() returns int
    do 'io'
        let x = try mayFail(true)
        return x
    catch e MyError 'err'
        return e.code
    end 'io'
end 'main'
```
```exitcode
42
```

<!-- test: error.do-catch-success-case -->
```maxon
// Test do-catch with no error thrown
type MyError is Error
    var code int
end 'MyError'

function mayFail(shouldFail bool) returns int throws MyError
    if shouldFail 'check'
        throw MyError{code: 42}
    end 'check'
    return 100
end 'mayFail'

function main() returns int
    do 'io'
        let x = try mayFail(false)
        return x
    catch e MyError 'err'
        return e.code
    end 'io'
end 'main'
```
```exitcode
100
```
