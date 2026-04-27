---
feature: lexer-parser-robustness
status: stable
keywords: [lexer, parser, robustness, fuzz, truncation]
category: diagnostics
---

# Lexer / Parser Robustness

## Documentation

Adversarial-input tests that prove the lexer and parser do not hang, crash, or silently accept malformed input. Each test asserts that the compiler terminates with a captured diagnostic. A regression that turns any of these into a hang, crash, or accidental success will fail the corresponding test.

These tests are not about diagnostic-message quality — they exist purely to detect lex/parse robustness regressions. The expected stderr text is captured verbatim from the live compiler; if the compiler is changed to emit a different (still-correct) diagnostic, the captured text will need to be regenerated.

## Tests

### Truncations: source ends mid-grammar (file-level)

<!-- test: truncated-empty-file -->
```maxon

```
```maxoncstderr
error E3001: No 'main' function found
```

<!-- test: truncated-after-function-keyword -->
```maxon
function 
```
```maxoncstderr
error E2010: specs/fragments/lexer-parser-robustness/truncated-after-function-keyword.test:2:9: Expected identifier but got ''
```

<!-- test: truncated-after-function-name -->
```maxon
function main
```
```maxoncstderr
error E2010: specs/fragments/lexer-parser-robustness/truncated-after-function-name.test:2:14: Expected '(' but got ''
```

<!-- test: truncated-mid-param-list -->
```maxon
function main(
```
```maxoncstderr
error E2010: specs/fragments/lexer-parser-robustness/truncated-mid-param-list.test:2:15: Expected identifier but got ''
```

<!-- test: truncated-after-param-name -->
```maxon
function main(x
```
```maxoncstderr
error E2003: specs/fragments/lexer-parser-robustness/truncated-after-param-name.test:2:16: Expected type name
```

<!-- test: truncated-after-param-colon -->
```maxon
function main(x:
```
```maxoncstderr
error E2003: specs/fragments/lexer-parser-robustness/truncated-after-param-colon.test:2:16: Expected type name
```

<!-- test: truncated-after-close-paren -->
```maxon
function main()
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/truncated-after-close-paren.test:2:16: unexpected token: ''
```

<!-- test: truncated-after-returns -->
```maxon
function main() returns
```
```maxoncstderr
error E2003: specs/fragments/lexer-parser-robustness/truncated-after-returns.test:2:24: Expected type name
```

<!-- test: truncated-after-return-type -->
```maxon
function main() returns ExitCode
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/truncated-after-return-type.test:2:33: unexpected token: ''
```

### Truncations: source ends mid-statement (body-level)

<!-- test: truncated-after-let -->
```maxon
function main() returns ExitCode
	let
```
```maxoncstderr
error E2010: specs/fragments/lexer-parser-robustness/truncated-after-let.test:3:5: Expected identifier but got ''
```

<!-- test: truncated-after-let-name -->
```maxon
function main() returns ExitCode
	let x
```
```maxoncstderr
error E2010: specs/fragments/lexer-parser-robustness/truncated-after-let-name.test:3:7: Expected '=' but got ''
```

<!-- test: truncated-after-let-eq -->
```maxon
function main() returns ExitCode
	let x =
```
```maxoncstderr
error E2004: specs/fragments/lexer-parser-robustness/truncated-after-let-eq.test:3:9: Expected expression but got '(empty)'
```

<!-- test: truncated-after-binary-op -->
```maxon
function main() returns ExitCode
	let x = 1 +
```
```maxoncstderr
error E2004: specs/fragments/lexer-parser-robustness/truncated-after-binary-op.test:3:13: Expected expression but got '(empty)'
```

<!-- test: truncated-after-dot -->
```maxon
function main() returns ExitCode
	let x = (5).
```
```maxoncstderr
error E2010: specs/fragments/lexer-parser-robustness/truncated-after-dot.test:3:14: Expected identifier but got ''
```

<!-- test: truncated-after-open-paren -->
```maxon
function main() returns ExitCode
	let x = (
```
```maxoncstderr
error E2004: specs/fragments/lexer-parser-robustness/truncated-after-open-paren.test:3:11: Expected expression but got '(empty)'
```

<!-- test: truncated-after-arg-comma -->
```maxon
function main() returns ExitCode
	let x = f(1,
```
```maxoncstderr
error E2004: specs/fragments/lexer-parser-robustness/truncated-after-arg-comma.test:3:10: Undefined function 'f'
```

<!-- test: truncated-after-open-bracket -->
```maxon
function main() returns ExitCode
	let x = [
```
```maxoncstderr
error E2004: specs/fragments/lexer-parser-robustness/truncated-after-open-bracket.test:3:11: Expected expression but got '(empty)'
```

<!-- test: truncated-after-if-keyword -->
```maxon
function main() returns ExitCode
	if
```
```maxoncstderr
error E2004: specs/fragments/lexer-parser-robustness/truncated-after-if-keyword.test:3:4: Expected expression but got '(empty)'
```

<!-- test: truncated-after-if-cond -->
```maxon
function main() returns ExitCode
	if true
```
```maxoncstderr
error E2010: specs/fragments/lexer-parser-robustness/truncated-after-if-cond.test:3:9: Expected characterliteral but got ''
```

<!-- test: truncated-after-if-label -->
```maxon
function main() returns ExitCode
	if true 'p'
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/truncated-after-if-label.test:3:13: Expected newline after block label, got ''
```

<!-- test: truncated-after-while -->
```maxon
function main() returns ExitCode
	while true
```
```maxoncstderr
error E2010: specs/fragments/lexer-parser-robustness/truncated-after-while.test:0:0: Expected loop label after while condition
```

<!-- test: truncated-after-match -->
```maxon
function main() returns ExitCode
	match 1
```
```maxoncstderr
error E2042: specs/fragments/lexer-parser-robustness/truncated-after-match.test:3:9: missing block identifier
```

<!-- test: truncated-after-match-label -->
```maxon
function main() returns ExitCode
	match 1 'm'
```
```maxoncstderr
error E2004: specs/fragments/lexer-parser-robustness/truncated-after-match-label.test:3:13: Expected pattern value, got ''
```

<!-- test: truncated-after-return-keyword -->
```maxon
function main() returns ExitCode
	return
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/truncated-after-return-keyword.test:3:8: unexpected token: ''
```

### Mid-keyword: keyword classifier corners

<!-- test: mid-keyword-functio -->
```maxon
functio
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/mid-keyword-functio.test:2:1: Expected function declaration, got 'functio'
```

<!-- test: mid-keyword-retur -->
```maxon
function main() returns ExitCode
	retur 0
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/mid-keyword-retur.test:3:2: unexpected token: 'retur'
```

<!-- test: mid-keyword-els -->
```maxon
function main() returns ExitCode
	if true 'p'
		return 1
	els 'p'
		return 2
	end 'p'
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/mid-keyword-els.test:5:2: unexpected token: 'els'
```

<!-- test: mid-keyword-whil -->
```maxon
function main() returns ExitCode
	whil true 'l'
		break 'l'
	end 'l'
	return 0
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/mid-keyword-whil.test:3:2: unexpected token: 'whil'
```

<!-- test: mid-keyword-matc -->
```maxon
function main() returns ExitCode
	matc 1 'm'
	default gives 0
	end 'm'
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/mid-keyword-matc.test:3:2: unexpected token: 'matc'
```

<!-- test: mid-keyword-functin-typo -->
```maxon
functin main()
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/mid-keyword-functin-typo.test:2:1: Expected function declaration, got 'functin'
```

<!-- test: keyword-prefix-ident -->
```maxon
function main() returns ExitCode
	let function2 = 5
	return function2 - 5
end 'main'
```
```exitcode
0
```

<!-- test: keyword-prefix-ident-returns -->
```maxon
function main() returns ExitCode
	let returnsX = 7
	return returnsX - 7
end 'main'
```
```exitcode
0
```

<!-- test: keyword-suffix-ident -->
```maxon
function main() returns ExitCode
	let xreturn = 3
	return xreturn - 3
end 'main'
```
```exitcode
0
```

<!-- test: near-keyword-uppercase -->
```maxon
Function main()
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/near-keyword-uppercase.test:2:1: Expected function declaration, got 'Function'
```

### Random bytes: small fixed pseudo-random byte blobs

<!-- test: random-printable-garbage-1 -->
```maxon
q1};+(=*foo,)bar
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-printable-garbage-1.test:2:1: Expected function declaration, got 'q1'
```

<!-- test: random-printable-garbage-2 -->
```maxon
abc 123 def!? xyz
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-printable-garbage-2.test:2:1: Expected function declaration, got 'abc'
```

<!-- test: random-printable-garbage-3 -->
```maxon
foo: bar baz; qux | zap
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-printable-garbage-3.test:2:1: Expected function declaration, got 'foo'
```

<!-- test: random-operator-soup-1 -->
```maxon
+-*/=<>(){}[]
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-operator-soup-1.test:2:1: Expected function declaration, got '+'
```

<!-- test: random-operator-soup-2 -->
```maxon
== != <= >= && || << >>
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-operator-soup-2.test:2:1: Expected function declaration, got '=='
```

<!-- test: random-operator-soup-3 -->
```maxon
.....,,,,;;;;
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-operator-soup-3.test:2:1: Expected function declaration, got '.'
```

<!-- test: random-mixed-delim-1 -->
```maxon
({[)}](})[){[(])}
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-mixed-delim-1.test:2:1: Expected function declaration, got '('
```

<!-- test: random-mixed-delim-2 -->
```maxon
(((]]]{{{)))
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-mixed-delim-2.test:2:1: Expected function declaration, got '('
```

<!-- test: random-mixed-delim-3 -->
```maxon
[}({)]}{[(
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-mixed-delim-3.test:2:1: Expected function declaration, got '['
```

<!-- test: random-backslash-in-source-1 -->
The literal chars `\x80\xFF\xC2` (not actual bytes) interleaved with text. Tests that
the lexer's identifier scanner stops at the backslash without crashing.
```maxon
foo\x80\xFF\xC2bar
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-backslash-in-source-1.test:2:1: Expected function declaration, got 'foo'
```

<!-- test: random-backslash-in-source-2 -->
```maxon
\xE9\xCA\xFE\xBA\xBE
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-backslash-in-source-2.test:2:1: Expected function declaration, got '/'
```

<!-- test: random-backslash-in-source-bell -->
```maxon
foo\x07bar
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-backslash-in-source-bell.test:2:1: Expected function declaration, got 'foo'
```

<!-- test: random-backslash-in-source-vtab -->
```maxon
foo\x0Bbaz
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-backslash-in-source-vtab.test:2:1: Expected function declaration, got 'foo'
```

<!-- test: random-backslash-in-source-formfeed -->
```maxon
foo\x0Cqux
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-backslash-in-source-formfeed.test:2:1: Expected function declaration, got 'foo'
```

<!-- test: random-backslash-in-source-null -->
```maxon
foo\x00bar
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/random-backslash-in-source-null.test:2:1: Expected function declaration, got 'foo'
```

### Pathological structure: deep / repeated input

<!-- test: deep-parens-open-only -->
```maxon
((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/deep-parens-open-only.test:2:1: Expected function declaration, got '('
```

<!-- test: deep-parens-balanced-empty -->
```maxon
((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((((
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/deep-parens-balanced-empty.test:2:1: Expected function declaration, got '('
```

<!-- test: deep-braces-open-only -->
```maxon
{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/deep-braces-open-only.test:2:1: Expected function declaration, got '{'
```

<!-- test: deep-brackets-open-only -->
```maxon
[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/deep-brackets-open-only.test:2:1: Expected function declaration, got '['
```

<!-- test: deep-mixed-delimiters -->
```maxon
({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[({[
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/deep-mixed-delimiters.test:2:1: Expected function declaration, got '('
```

<!-- test: long-binop-chain -->
```maxon
function main() returns ExitCode
	let x = 0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0
	return x
end 'main'
```
```exitcode
0
```

<!-- test: long-call-chain -->
```maxon
typealias Small = int(0 to 100)

function id(x Small) returns Small
	return x
end 'id'

function main() returns ExitCode
	let x = id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(id(0))))))))))))))))))))))))))))))))
	return x
end 'main'
```
```exitcode
0
```

<!-- test: long-method-chain-truncated -->
```maxon
function main() returns ExitCode
	let x = (5).a.b.c.d.e.f.g.h.i.j.k.l.m.n.o.p.q.r.s.t.u.v.w.x.y.z.
end 'main'
```
```maxoncstderr
error E4006: specs/fragments/lexer-parser-robustness/long-method-chain-truncated.test:3:14: Cannot access field on non-struct value
```

<!-- test: interpolation-nesting-deep -->
Locks in current behavior: deeply nested `"{ ... }"` interpolation with adjacent
quoted fragments is parsed without crashing or hanging. The exact runtime value
is not asserted — the point is only that compilation terminates.
```maxon
function main() returns ExitCode
	let s = "{ "{ "{ "{ "inner" }" }" }" }"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: unmatched-close-paren-many -->
```maxon
))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/unmatched-close-paren-many.test:2:1: Expected function declaration, got ')'
```

<!-- test: unmatched-close-brace-many -->
```maxon
}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}
```
```maxoncstderr
error E2001: specs/fragments/lexer-parser-robustness/unmatched-close-brace-many.test:2:1: Expected function declaration, got '}'
```

<!-- test: comment-with-many-asterisks -->
```maxon
/* ************************************************************ */
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: repeated-keyword -->
```maxon
function function function function
```
```maxoncstderr
error E2010: specs/fragments/lexer-parser-robustness/repeated-keyword.test:2:19: Expected '(' but got 'function'
```

### Truncated literals: partial tokens at unusual boundaries

<!-- test: unterminated-string-followed-by-code -->
Locks in current behavior: when a double-quoted string is unterminated, the lexer
ends the string at the newline, so a following statement parses as normal. This
file therefore compiles and runs successfully — the "unterminated" string is just
the value of `s`. If the lexer is changed to make this an error, this test will
need to be updated.
```maxon
function main() returns ExitCode
	let s = "hello
	print(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: unterminated-interp -->
Locks in current behavior: an interpolation expression that hits a newline before
its closing `}` is somehow accepted (the lexer terminates the string, and what
follows is parsed as expression continuation). If this is later tightened into a
diagnostic, this test will need to be updated.
```maxon
function main() returns ExitCode
	let s = "{1+2
	print(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: unterminated-interp-mid-expr -->
```maxon
function main() returns ExitCode
	let x = "{1+}"
	return 0
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/lexer-parser-robustness/unterminated-interp-mid-expr.test:3:14: Expected expression but got '(empty)'
```

<!-- test: unterminated-block-comment-with-newlines -->
```maxon
/* line1
line2
line3
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3001: No 'main' function found
```

<!-- test: bad-hex-escape-string -->
```maxon
function main() returns ExitCode
	let x = "\xZZ"
	return 0
end 'main'
```
```maxoncstderr
error E1004: specs/fragments/lexer-parser-robustness/bad-hex-escape-string.test:3:10: Invalid hex escape '/xZZ': expected 2 hex digits in string interpolation
```

<!-- test: bad-hex-escape-char -->
```maxon
function main() returns ExitCode
	let x = '\xZZ'
	return 0
end 'main'
```
```maxoncstderr
error E1004: specs/fragments/lexer-parser-robustness/bad-hex-escape-char.test:3:10: Invalid hex escape '/xZZ': expected 2 hex digits in character literal
```

<!-- test: short-unicode-escape -->
```maxon
function main() returns ExitCode
	let x = "\u12"
	return 0
end 'main'
```
```maxoncstderr
error E1004: specs/fragments/lexer-parser-robustness/short-unicode-escape.test:3:10: Invalid unicode escape '/u12': expected 4 hex digits in string interpolation
```

<!-- test: non-hex-unicode-escape -->
```maxon
function main() returns ExitCode
	let x = "\uZZZZ"
	return 0
end 'main'
```
```maxoncstderr
error E1004: specs/fragments/lexer-parser-robustness/non-hex-unicode-escape.test:3:10: Invalid unicode escape '/uZZZZ': expected 4 hex digits in string interpolation
```
