---
name: maxon-style-guide
description: Coding conventions and style guidelines for Maxon language code and Zig compiler code. Apply when writing, reviewing, or modifying code in the Maxon project.
---

# Maxon Style Guide

## Maxon Syntax Quick Reference

```maxon
// Variables
var name = value            // mutable variable
let name = value            // immutable variable

// Type Declarations (composite types)
type Name
    var field1 int          // mutable field
    let field2 string       // immutable field
    var field3 int = 0      // field with default value
end 'Name'

var instance = Name{field1: 1, field2: "hello"}  // instantiation

// Functions
function name(p1 type, p2 type) returns returnType
    return value
end 'name'

function name(p1 type, p2 type = default) returns returnType  // default value
    return value
end 'name'

function voidFunc(p1 type)  // no return type = void
    // statements
end 'voidFunc'

// Function Calls
foo(1, 2)                   // positional arguments
foo(x = 1, y = 2)           // named arguments (optional)
foo(1, y = 2)               // mixed: positional first, then named
foo(y = 2, x = 1)           // named args in any order
foo(1)                      // omit param with default

// Control Flow
if condition 'label'
    statements
end 'label'

if condition 'label1'
    statements
end 'label1' else if othercondition 'label2'
    statements
end 'label2' else 'label3'
    statements
end 'label3'

while condition 'label'
    statements
end 'label'

for item in iterable 'label'
    statements
end 'label'

match expr 'label'
    pattern then statement
    default then statement
end 'label'

// Arrays
var arr = Array of 10 int   // sized array
let vals = [1, 2, 3]        // array literal
var elem = arr[0]           // indexing
var size = arr.count()      // length

// Types
int float bool byte character string
Array of T
T or nil                    // optional type

// Operators
+ - * / mod                 // arithmetic
== != < > <= >=             // comparison
and or not                  // logical
as                          // type cast

// Literals
42                          // int
3.14                        // float
'A'                         // character (grapheme)
"text"                      // string
"Hello {name}!"             // string interpolation
true false                  // bool
nil                         // nil (for optionals)
```

See `docs/LANGUAGE_REFERENCE.md` for complete syntax and semantics.

## Naming Conventions

### Maxon Code
- Variables and functions: `camelCase`
- Types: `PascalCase`
- Constants: `SCREAMING_SNAKE_CASE`

### Zig Compiler Code
- Functions: `camelCase`
- Types: `PascalCase`
- Constants: `SCREAMING_SNAKE_CASE`
- Files: `kebab-case.zig` or `snake_case.zig`

## Block Labels
Always use descriptive labels that match the construct:
```maxon
if condition 'check_valid'
    // ...
end 'check_valid'

while running 'main_loop'
    // ...
end 'main_loop'

function processData(input string) returns int
    // ...
end 'processData'
```

## Comments
- Explain "why" not "what"
- Use `//` for single-line comments
- Keep comments concise

## File Formatting
- Use LF line endings (Unix-style), never CRLF
- Use spaces for indentation (4 spaces)
- No trailing whitespace

## File Operations
- Always use absolute paths
- Create temp files in `/temp` directory
- Clean up temp files after use

## Zig Best Practices
- Use Zig's error unions properly
- Provide meaningful error messages
- Handle all error cases explicitly
- Use arena allocators where appropriate
- Clean up resources in defer blocks
