---
feature: literals
status: stable
keywords: [literal, constant, int, float, char, string, bool]
category: expressions
---

# Literals

## Developer Notes

Literals are constant values written directly in source code.

Implementation:
- Integer literals: Parsed in `Lexer::readNumber()`, represented as `IntLiteral` AST node
- Float literals: Must contain `.`, parsed as `FloatLiteral`
- Character literals: Single quotes `'A'`, parsed as `CharLiteral`
- String literals: Double quotes `"text"`, stored as global constants
- Boolean literals: `true` and `false` keywords, represented as `BoolLiteral`

Each literal type creates the appropriate LLVM constant value during code generation.

## Documentation

Literals are constant values used directly in code.

### Integer Literals
```maxon
42
-17
0
```
### Float Literals
Must include decimal point:
```maxon
3.14
-2.5
0.0
```
### Character Literals
Single character in single quotes:
```maxon
'A'
'z'
'\n'
```
### String Literals
Text in double quotes:
```maxon
"Hello, World!"
"Line1\nLine2"
```
### Boolean Literals
```maxon
true
false
```
## Tests

<!-- test: integer -->
```maxon
function main() int
    return 5
end 'main'
```
```exitcode
5
```


<!-- test: float -->
```maxon
function main() int
    var x = 3.14
    return trunc(x)
end 'main'
```
```exitcode
3
```


<!-- test: boolean -->
```maxon
function main() int
    var flag = true
    if flag 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```
