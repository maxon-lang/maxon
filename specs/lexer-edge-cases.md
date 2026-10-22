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
error E2001: specs/lexer-edge-cases/single-char-eof.maxon:2:1: Expected function declaration, got 'x'
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
error E1002: specs/lexer-edge-cases/unterminated-squote-eof.maxon:2:9: Unterminated string literal
```

<!-- test: unterminated-dquote-eof -->
```maxon
let x = "hello
```
```maxoncstderr
error E1002: specs/lexer-edge-cases/unterminated-dquote-eof.maxon:2:9: Unterminated string literal
```

<!-- test: string-with-newline -->
```maxon
let x = 'hel
lo'
```
```maxoncstderr
error E2004: specs/lexer-edge-cases/string-with-newline.maxon:2:9: Expected constant expression, got 'hel
lo'
```

<!-- test: unterminated-escape-eof -->
```maxon
let x = "hello\
```
```maxoncstderr
error E1002: specs/lexer-edge-cases/unterminated-escape-eof.maxon:2:9: Unterminated string literal
```

<!-- test: empty-squote-string -->
```maxon
let x = ''
```
```maxoncstderr
error E2016: specs/lexer-edge-cases/empty-squote-string.maxon:2:9: invalid character literal: a character literal must contain exactly one character
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
error E2001: specs/lexer-edge-cases/integer-eof.maxon:2:1: Expected function declaration, got '42'
```

<!-- test: hex-eof -->
```maxon
0xFF
```
```maxoncstderr
error E2001: specs/lexer-edge-cases/hex-eof.maxon:2:1: Expected function declaration, got '0xFF'
```

<!-- test: binary-eof -->
```maxon
0b1010
```
```maxoncstderr
error E2001: specs/lexer-edge-cases/binary-eof.maxon:2:1: Expected function declaration, got '0b1010'
```

<!-- test: octal-eof -->
```maxon
0o77
```
```maxoncstderr
error E2001: specs/lexer-edge-cases/octal-eof.maxon:2:1: Expected function declaration, got '0o77'
```

<!-- test: float-eof -->
```maxon
3.14
```
```maxoncstderr
error E2001: specs/lexer-edge-cases/float-eof.maxon:2:1: Expected function declaration, got '3.14'
```

<!-- test: float-exponent-eof -->
```maxon
1e10
```
```maxoncstderr
error E2001: specs/lexer-edge-cases/float-exponent-eof.maxon:2:1: Expected function declaration, got '1'
```

<!-- test: number-underscore-eof -->
```maxon
1_000
```
```maxoncstderr
error E2001: specs/lexer-edge-cases/number-underscore-eof.maxon:2:1: Expected function declaration, got '1_000'
```

<!-- test: bare-hex-prefix-eof -->
```maxon
0x
```
```maxoncstderr
error E2001: specs/lexer-edge-cases/bare-hex-prefix-eof.maxon:2:1: Expected function declaration, got '0x'
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
error E1007: specs/lexer-edge-cases/unterminated-block-comment.maxon:2:1: Unterminated block comment
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
error E2001: specs/lexer-edge-cases/two-char-operator-eof.maxon:2:1: Expected function declaration, got '=='
```

<!-- test: single-equals-eof -->
```maxon
=
```
```maxoncstderr
error E2001: specs/lexer-edge-cases/single-equals-eof.maxon:2:1: Expected function declaration, got '='
```

<!-- test: single-bang-eof -->
```maxon
!
```
```maxoncstderr
error E2001: specs/lexer-edge-cases/single-bang-eof.maxon:2:1: Expected function declaration, got '!'
```

<!-- test: single-slash-eof -->
```maxon
/
```
```maxoncstderr
error E2001: specs/lexer-edge-cases/single-slash-eof.maxon:2:1: Expected function declaration, got '/'
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

### Stray characters

<!-- test: stray-question-mark-after-type -->
```maxon
function f(a Count?, b Count)
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-question-mark-after-type.maxon:2:19: Unexpected character '?'
```

<!-- test: stray-question-mark-after-expression -->
```maxon
let x = 5 ?
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-question-mark-after-expression.maxon:2:11: Unexpected character '?'
```

<!-- test: stray-question-mark-before-more-source -->
```maxon
let x = 5 ?
let y = 6
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-question-mark-before-more-source.maxon:2:11: Unexpected character '?'
```

<!-- test: stray-question-mark-in-interpolation -->
```maxon
let s = "n{5 ?}"
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-question-mark-in-interpolation.maxon:2:14: Unexpected character '?'
```

<!-- test: stray-question-mark-in-function-body -->
```maxon
function main() returns ExitCode
	let x = 1?
	return x
end 'main'
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-question-mark-in-function-body.maxon:3:11: Unexpected character '?'
```

<!-- test: stray-semicolon -->
```maxon
let x = 1;
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-semicolon.maxon:2:10: Unexpected character ';'
```

<!-- test: stray-pipe -->
```maxon
let x = a | b
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-pipe.maxon:2:11: Unexpected character '|'
```

<!-- test: stray-ampersand -->
```maxon
let x = a && b
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-ampersand.maxon:2:11: Unexpected character '&'
```

<!-- test: stray-dollar -->
```maxon
let x = $y
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-dollar.maxon:2:9: Unexpected character '$'
```

<!-- test: stray-percent -->
```maxon
let x = 7 % 2
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-percent.maxon:2:11: Unexpected character '%'
```

<!-- test: stray-tilde -->
```maxon
let x = ~1
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-tilde.maxon:2:9: Unexpected character '~'
```

<!-- test: stray-caret -->
```maxon
let x = 1 ^ 2
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-caret.maxon:2:11: Unexpected character '^'
```

<!-- test: stray-backtick -->
```maxon
let x = `y`
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-backtick.maxon:2:9: Unexpected character '`'
```

<!-- test: stray-non-ascii-letter -->
```maxon
let x = é
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-non-ascii-letter.maxon:2:9: Unexpected character 'é'
```

<!-- test: stray-curly-quote -->
```maxon
let x = “hello”
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-curly-quote.maxon:2:9: Unexpected character '“'
```

<!-- test: question-mark-is-text-in-strings-and-comments -->
```maxon
// is this a comment?
/* or a block comment? */
function main() returns ExitCode
	let q = "why?"
	let c = '?'
	print("{q}{c}\n")
	return 0
end 'main'
```
```stdout
why??
```
```exitcode
0
```

<!-- test: byte-order-mark-at-start-is-accepted -->
```maxon
// --- file: main.maxon
﻿function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: stray-byte-order-mark-mid-file -->
```maxon
function main() returns ExitCode
	﻿return 0
end 'main'
```
```maxoncstderr
error E1010: specs/lexer-edge-cases/stray-byte-order-mark-mid-file.maxon:3:2: Unexpected character '﻿'
```
