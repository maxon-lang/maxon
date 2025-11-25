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
- Used with extern functions and as data

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
    var msg = "Hello!\n"
```
## Tests
