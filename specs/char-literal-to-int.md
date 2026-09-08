---
feature: char-literal-to-int
status: experimental
keywords: [character, literal, int, coercion, codepoint]
category: type-system
---

# Character Literal to Integer Coercion

## Documentation

When a character literal appears in a binary operation where the other operand is an integer type (int, byte, short), the compiler automatically converts the character literal to its Unicode codepoint value at compile time.

### Comparison

```maxon
for cp in "hello-world".codepoints() 'chars'
	if cp == '-' 'dash'
		print("found dash (cp={cp})\n")
	end 'dash'
end 'chars'
```

### Arithmetic

```maxon
var digit = 53  // codepoint for '5'
var value = digit - '0'  // '0' coerced to 48, result is 5
```

## Tests

<!-- test: char-literal-eq-codepoint -->
### Compare codepoint with character literal using ==

```maxon
function main() returns ExitCode
	let cp = 45
	if cp == '-' 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: char-literal-ne-codepoint -->
### Compare codepoint with character literal using !=

```maxon
function main() returns ExitCode
	let cp = 45
	if cp != '.' 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: char-literal-ge-le-codepoint -->
### Compare codepoint with character literal using >= and <=

```maxon
function main() returns ExitCode
	let cp = 53
	if cp >= '0' 'ge'
		if cp <= '9' 'le'
			return 0
		end 'le'
	end 'ge'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: char-literal-arithmetic -->
### Subtract character literal from codepoint

```maxon
function main() returns ExitCode
	let cp = 53
	let digit = cp - '0'
	return digit
end 'main'
```
```exitcode
5
```

<!-- test: char-literal-escape-coercion -->
### Escape sequence character literal coerced to int

```maxon
function main() returns ExitCode
	let cp = 10
	if cp == '\n' 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: char-literal-lhs-coercion -->
### Character literal on left-hand side of comparison

```maxon
function main() returns ExitCode
	let cp = 45
	if '-' == cp 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: char-literal-both-sides-still-character -->
### Two character literals compared stay as Character

```maxon
function main() returns ExitCode
	if 'A' == 'A' 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: char-literal-codepoint-iteration -->
### Character literal comparison during codepoint iteration

```maxon
function main() returns ExitCode
	var count = 0
	for cp in "a-b-c".codepoints() 'chars'
		if cp == '-' 'dash'
			count = count + 1
		end 'dash'
	end 'chars'
	return count
end 'main'
```
```exitcode
2
```

<!-- test: char-literal-coerces-across-a-closure-body -->
### A character literal parsed before a closure still converts after it

⛔⛔ **THE PER-FUNCTION COLUMN THAT WAS RESET WITHOUT BEING SAVED (BATCH23 review).** `integerizedOperand`
rewrites a character literal's already-emitted `stringLiteral` op IN PLACE, and finds that op through
`Parser.charLiteralOps` — a per-function map keyed by `ValueId`. `parseBinary` reads the LEFT operand's
token span only once the RIGHT operand is in hand, so a closure in the right operand is parsed BETWEEN the
left literal's emit and its conversion. The closure-body context swap reset that map and never restored it,
which is two wrong answers on one line, both MEASURED:

  • `'0' + apply(function(n Integer) gives n + 1, x: 1)` found no entry and **PANICKED the compiler**.
  • `'A' + apply(function(n Integer) gives n + '0', x: 1)` was worse, because a closure's value ids are a
    FRESH SSA space that COLLIDES with the enclosing function's: the lookup HIT the closure's entry, rewrote
    the CLOSURE's op, and left the outer literal a `stringLiteral`. It printed **5368717386** — the record's
    `.rdata` ADDRESS added as a number — where 114 is correct, silently.

All three answers below are the oracle's own (MEASURED: `a=50 b=114 c=51`). The third case pins the other
direction — a literal AFTER the closure — so a fix that saved the map without restoring it would still fail.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias Fn1 = function(Integer) returns Integer

function apply(f Fn1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let a = '0' + apply(function(n Integer) gives n + 1, x: 1)
	let p0 = 0
	let p1 = 1
	let b = 'A' + apply(function(n Integer) gives n + '0', x: 1)
	let c = apply(function(n Integer) gives n + 2, x: 1) + '0'
	print("a={a} b={b} c={c} pad={p0}{p1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=50 b=114 c=51 pad=01
```
