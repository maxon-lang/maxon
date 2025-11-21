---
feature: string-literals
status: stable
keywords: [string, literal, text]
category: literals
---

# String Literals

## Developer Notes

String literals are sequences of characters enclosed in double quotes. They are stored as compile-time constants in the data section.

Implementation:
- Parsed in `Lexer::readStringLiteral()`
- Represented as `StringLiteral` AST node
- Type is `ptr` (pointer to char array)
- Stored as LLVM global constant array
- Supports escape sequences: `\n`, `\t`, `\\`, `\"`
- Null-terminated automatically
- Used primarily with extern functions (WriteFile, etc.)

The string literal is converted to LLVM `[N x i8]` constant and referenced via `getelementptr`.

## Documentation

String literals are text enclosed in double quotes.

### Syntax

```maxon
var message = "Hello, World!"
```

### Escape Sequences

- `\n` - Newline
- `\t` - Tab
- `\\` - Backslash
- `\"` - Double quote

### Example

```maxon
function main() int
    // String literal with escape sequence
    var msg = "Hello!\n"
    // In real code, you'd use print or write to output this
    return 0
end 'main'
```
```output
ExitCode: 0
```

## Tests

<!-- test: simple -->
```maxon
extern function GetStdHandle(nStdHandle int) ptr
extern function WriteFile(hFile ptr, lpBuffer ptr, nNumberOfBytesToWrite int, lpNumberOfBytesWritten ptr, lpOverlapped ptr) int

function main() int
    let stdoutHandle = 0 - 11
    let stdout = GetStdHandle(stdoutHandle)
    var message = "Hello!"
    var written = 0
    
    WriteFile(stdout, message, 6, &written, 0 as ptr)
    return 0
end 'main'
```
```
ExitCode: 0
Stdout: Hello!
```

<!-- test: with-escapes -->
```maxon
extern function GetStdHandle(nStdHandle int) ptr
extern function WriteFile(hFile ptr, lpBuffer ptr, nBytes int, written ptr, overlapped ptr) int

function main() int
    let stdout = GetStdHandle(-11)
    var written = 0
    var msg = "Line1\nLine2\tTabbed"
    WriteFile(stdout, msg, 18, &written, 0 as ptr)
    return 0
end 'main'
```
```
ExitCode: 0
Stdout: Line1
Line2	Tabbed
```

<!-- test: multiple -->
```maxon
extern function GetStdHandle(nStdHandle int) ptr
extern function WriteFile(hFile ptr, lpBuffer ptr, nBytes int, written ptr, overlapped ptr) int

function main() int
    let stdout = GetStdHandle(-11)
    var written = 0
    var msg1 = "Hello "
    var msg2 = "World!"
    WriteFile(stdout, msg1, 6, &written, 0 as ptr)
    WriteFile(stdout, msg2, 6, &written, 0 as ptr)
    return 0
end 'main'
```
```
ExitCode: 0
Stdout: Hello World!
```
