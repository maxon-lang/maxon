---
feature: lexer-edge-cases
status: stable
keywords: [lexer, cursor, eof, edge-case]
category: diagnostics
---

# Lexer Edge Cases

## Documentation

Tests for lexer behavior at source boundaries: empty source, tokens at EOF, unterminated strings and comments, and special characters.

## Tests

### Empty and minimal source

<!-- test: single-char-eof -->
```maxon
x
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/single-char-eof.test:2:1: Expected function declaration, got 'x'
```

<!-- test: whitespace-only -->
```maxon
   
```
```maxoncstderr
error E3001: No 'main' function found
```

### Unterminated strings

<!-- test: unterminated-squote-eof -->
```maxon
let x = 'hello
```
```maxoncstderr
error E1002: specs/fragments/lexer-edge-cases/unterminated-squote-eof.test:2:9: Unterminated string literal
```

<!-- test: unterminated-dquote-eof -->
```maxon
let x = "hello
```
```maxoncstderr
error E1002: specs/fragments/lexer-edge-cases/unterminated-dquote-eof.test:2:9: Unterminated string literal
```

<!-- test: string-with-newline -->
```maxon
let x = 'hel
lo'
```
```maxoncstderr
error E2004: specs/fragments/lexer-edge-cases/string-with-newline.test:2:9: Expected constant expression, got 'hel
lo'
```

<!-- test: unterminated-escape-eof -->
```maxon
let x = "hello\
```
```maxoncstderr
error E1002: specs/fragments/lexer-edge-cases/unterminated-escape-eof.test:2:9: Unterminated string literal
```

<!-- test: empty-squote-string -->
```maxon
let x = ''
```
```maxoncstderr
error E2004: specs/fragments/lexer-edge-cases/empty-squote-string.test:2:9: Expected constant expression, got ''
```

<!-- test: empty-dquote-string -->
```maxon
let x = ""
```
```maxoncstderr
error E3001: No 'main' function found
```

### Numbers at EOF

<!-- test: integer-eof -->
```maxon
42
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/integer-eof.test:2:1: Expected function declaration, got '42'
```

<!-- test: hex-eof -->
```maxon
0xFF
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/hex-eof.test:2:1: Expected function declaration, got '0xFF'
```

<!-- test: binary-eof -->
```maxon
0b1010
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/binary-eof.test:2:1: Expected function declaration, got '0b1010'
```

<!-- test: octal-eof -->
```maxon
0o77
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/octal-eof.test:2:1: Expected function declaration, got '0o77'
```

<!-- test: float-eof -->
```maxon
3.14
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/float-eof.test:2:1: Expected function declaration, got '3.14'
```

<!-- test: float-exponent-eof -->
```maxon
1e10
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/float-exponent-eof.test:2:1: Expected function declaration, got '1'
```

<!-- test: number-underscore-eof -->
```maxon
1_000
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/number-underscore-eof.test:2:1: Expected function declaration, got '1_000'
```

<!-- test: bare-hex-prefix-eof -->
```maxon
0x
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/bare-hex-prefix-eof.test:2:1: Expected function declaration, got '0x'
```

### Comments at EOF

<!-- test: line-comment-eof -->
```maxon
// comment
```
```maxoncstderr
error E3001: No 'main' function found
```

<!-- test: unterminated-block-comment -->
```maxon
/* no close
```
```maxoncstderr
error E1007: specs/fragments/lexer-edge-cases/unterminated-block-comment.test:2:1: Unterminated block comment
```

<!-- test: block-comment-multiline -->
```maxon
/* a
b */
```
```maxoncstderr
error E3001: No 'main' function found
```

<!-- test: empty-block-comment -->
```maxon
/**/
```
```maxoncstderr
error E3001: No 'main' function found
```

### Operators at EOF

<!-- test: two-char-operator-eof -->
```maxon
==
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/two-char-operator-eof.test:2:1: Expected function declaration, got '=='
```

<!-- test: single-equals-eof -->
```maxon
=
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/single-equals-eof.test:2:1: Expected function declaration, got '='
```

<!-- test: single-bang-eof -->
```maxon
!
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/single-bang-eof.test:2:1: Expected function declaration, got '!'
```

<!-- test: single-slash-eof -->
```maxon
/
```
```maxoncstderr
error E2001: specs/fragments/lexer-edge-cases/single-slash-eof.test:2:1: Expected function declaration, got '/'
```

### Line endings

<!-- test: crlf-handling -->
```maxon
let x = 1
let y = 2
```
```maxoncstderr
error E3001: No 'main' function found
```

<!-- test: bare-cr-handling -->
```maxon
let x = 1
let y = 2
```
```maxoncstderr
error E3001: No 'main' function found
```
