---
feature: char-type
status: stable
keywords: [char, character, i8]
category: types
---

# Char Type

## Developer Notes

The `char` type represents a single character, stored as an 8-bit integer (LLVM `i8`).

Key implementation:
- Represented as LLVM `i8` type
- Character literals enclosed in single quotes: `'A'`
- Supports ASCII characters (0-127)
- Can be cast to `int` to get ASCII value
- Can be compared using comparison operators
- Lexer handles escape sequences in `readCharLiteral()`
- Used for string manipulation (strings are `ptr` to char array)

Common escape sequences: `\n` (newline), `\t` (tab), `\\` (backslash), `\'` (single quote).

## Documentation

The `char` type stores a single character as an 8-bit value.

### Syntax

```maxon
var letter = 'A'
let newline char = '\n'
```

Character literals are enclosed in single quotes.

### Example

```maxon
function isUppercase(c char) bool
    var code = c as int
    if code >= 65 'check'
        if code <= 90 'check2'
            return true
        end 'check2'
    end 'check'
    return false
end 'isUppercase'

function main() int
    if isUppercase('Z') 'test'
        return 1
    end 'test'
    return 0
end 'main'
```

## Tests

<!-- test: basic-char -->
```maxon
function main() int
    var x = 'A'
    return x as int
end 'main'
```
```
ExitCode: 65
```

<!-- test: char-comparison -->
```maxon
function main() int
    var a = 'A'
    var b = 'B'
    if a < b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```
ExitCode: 1
```

<!-- test: char-in-variable -->
```maxon
function main() int
    var letter char = 'Z'
    var code = letter as int
    return code
end 'main'
```
```
ExitCode: 90
```
