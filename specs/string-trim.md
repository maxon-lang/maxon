---
feature: string-trim
status: experimental
keywords: [string, trim, whitespace, character-set]
category: types
---

# String Trim

## Documentation

The `trim`, `trimStart`, and `trimEnd` methods remove characters from the edges of a string. They accept a `CharacterSet` to specify which characters to remove.

**Signatures:**

```text
trim(in: CharacterSet) returns String       // both ends
trimStart(in: CharacterSet) returns String   // start only
trimEnd(in: CharacterSet) returns String     // end only
trim() returns String                        // whitespace, both ends
trimStart() returns String                   // whitespace, start only
trimEnd() returns String                     // whitespace, end only
```

**Examples:**

```text
"  hello  ".trim()                              // "hello"
"  hello  ".trimStart()                         // "hello  "
"  hello  ".trimEnd()                           // "  hello"
"123hello456".trim(CharacterSet.decimalDigits)  // "hello"
"...hello!!!".trim(CharacterSet.punctuation)    // "hello"
```

The no-argument versions are convenience methods that trim Unicode whitespace.

### CharacterSet

`CharacterSet` provides predefined character sets and supports custom sets:

```text
CharacterSet.whitespacesAndNewlines()  // All Unicode whitespace including newlines
CharacterSet.whitespaces()             // Spaces and tabs (no newlines)
CharacterSet.newlines()                // Newline characters only
CharacterSet.decimalDigits()           // 0-9
CharacterSet.letters()                 // a-z, A-Z
CharacterSet.lowercaseLetters()        // a-z
CharacterSet.uppercaseLetters()        // A-Z
CharacterSet.alphanumerics()           // a-z, A-Z, 0-9
CharacterSet.punctuation()             // Punctuation characters
CharacterSet.symbols()                 // Symbol characters
CharacterSet.controlCharacters()       // Control characters
```

## Tests

<!-- test: trim-whitespace-both -->
```maxon
function main() returns ExitCode
  var s = "  hello  "
  var ws = CharacterSet.whitespacesAndNewlines()
  var result = s.trim(ws)
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
```

<!-- test: trim-whitespace-start -->
```maxon
function main() returns ExitCode
  var s = "  hello  "
  var result = s.trimStart()
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello  ]
```

<!-- test: trim-whitespace-end -->
```maxon
function main() returns ExitCode
  var s = "  hello  "
  var result = s.trimEnd()
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[  hello]
```

<!-- test: trim-tabs-and-newlines -->
```maxon
function main() returns ExitCode
  var s = "\t\nhello\r\n"
  var result = s.trim()
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
```

<!-- test: trim-all-whitespace -->
```maxon
function main() returns ExitCode
  var s = "   "
  var result = s.trim()
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[]
```

<!-- test: trim-no-whitespace -->
```maxon
function main() returns ExitCode
  var s = "hello"
  var result = s.trim()
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
```

<!-- test: trim-empty-string -->
```maxon
function main() returns ExitCode
  var s = ""
  var result = s.trim()
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[]
```

<!-- test: trim-mixed-whitespace -->
```maxon
function main() returns ExitCode
  var s = " \t\r\n hello \t\r\n "
  var result = s.trim()
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
```

<!-- test: trim-internal-preserved -->
```maxon
function main() returns ExitCode
  var s = "  hello world  "
  var result = s.trim()
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world]
```

<!-- test: trim-charset-whitespace -->
```maxon
function main() returns ExitCode
  var s = "  hello  "
  var result = s.trim(CharacterSet.whitespacesAndNewlines())
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
```

<!-- test: trim-charset-digits -->
```maxon
function main() returns ExitCode
  var s = "123hello456"
  var result = s.trim(CharacterSet.decimalDigits())
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
```

<!-- test: trim-charset-letters -->
```maxon
function main() returns ExitCode
  var s = "abc123abc"
  var result = s.trim(CharacterSet.letters())
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[123]
```

<!-- test: trim-charset-digits-start -->
```maxon
function main() returns ExitCode
  var s = "123hello"
  var result = s.trimStart(CharacterSet.decimalDigits())
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
```

<!-- test: trim-charset-digits-end -->
```maxon
function main() returns ExitCode
  var s = "hello123"
  var result = s.trimEnd(CharacterSet.decimalDigits())
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
```

<!-- test: trim-charset-no-match -->
```maxon
function main() returns ExitCode
  var s = "hello"
  var result = s.trim(CharacterSet.decimalDigits())
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
```

<!-- test: trim-charset-all-match -->
```maxon
function main() returns ExitCode
  var s = "12345"
  var result = s.trim(CharacterSet.decimalDigits())
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[]
```

<!-- test: trim-charset-empty -->
```maxon
function main() returns ExitCode
  var s = ""
  var result = s.trim(CharacterSet.whitespacesAndNewlines())
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[]
```

<!-- test: trim-charset-punctuation -->
```maxon
function main() returns ExitCode
  var s = "...hello!!!"
  var result = s.trim(CharacterSet.punctuation())
  print("[{result}]")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
```
