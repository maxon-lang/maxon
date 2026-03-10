---
feature: unicode-escape
status: experimental
keywords: [unicode, escape, character, string, codepoint]
category: literals
---

# Unicode Escape Sequences

## Documentation

The `\uXXXX` escape sequence allows specifying Unicode characters by their code point (exactly 4 hex digits). It works in both character literals and string literals.

### Syntax

```maxon
var nbsp = '\u00A0'          // Non-breaking space character
var s = "Price:\u00A0$5"     // Non-breaking space in string
var sigma = '\u03A3'         // Greek capital sigma
```

### Character Literals

```maxon
var nel = '\u0085'           // U+0085 NEL (Next Line)
var nbsp = '\u00A0'          // U+00A0 Non-Breaking Space
var ideographic = '\u3000'   // U+3000 Ideographic Space
```

### String Literals

Works in both plain and interpolated strings:

```maxon
var s = "hello\u0021"        // "hello!"
var name = "world"
var s2 = "hello\u0021 {name}" // "hello! world"
```

## Tests

<!-- test: unicode-escape.char-basic -->
### Character literal with unicode escape

```maxon
function main() returns ExitCode
  var excl = '\u0021'
  if excl == '!' 'check'
    print("PASS")
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
PASS
```

<!-- test: unicode-escape.char-multibyte -->
### Multi-byte unicode character

```maxon
function main() returns ExitCode
  var sigma = '\u03A3'
  print("{sigma}")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
Σ
```

<!-- test: unicode-escape.string-basic -->
### String literal with unicode escape

```maxon
function main() returns ExitCode
  var s = "hello\u0021"
  print(s)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello!
```

<!-- test: unicode-escape.string-interp -->
### Unicode escape in interpolated string

```maxon
function main() returns ExitCode
  var name = "world"
  var s = "hello\u0021 {name}"
  print(s)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello! world
```

<!-- test: unicode-escape.whitespace-chars -->
### Unicode whitespace characters via escape

```maxon
function main() returns ExitCode
  var ws = CharacterSet.whitespacesAndNewlines()
  var nbsp = '\u00A0'
  var enSpace = '\u2002'
  var ideoSpace = '\u3000'
  if ws.contains(nbsp) 'c1'
    print("nbsp ")
  end 'c1'
  if ws.contains(enSpace) 'c2'
    print("enSpace ")
  end 'c2'
  if ws.contains(ideoSpace) 'c3'
    print("ideoSpace")
  end 'c3'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
nbsp enSpace ideoSpace
```

<!-- test: unicode-escape.invalid-too-few-digits -->
### Error: too few hex digits

```maxon
function main() returns ExitCode
  var x = '\u00'
  return 0
end 'main'
```
```maxoncstderr
error E1004: specs/fragments/unicode-escape/unicode-escape.invalid-too-few-digits.test:3:11: Invalid unicode escape '\u00': expected 4 hex digits in character literal
```
