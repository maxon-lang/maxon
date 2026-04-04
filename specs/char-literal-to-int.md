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
		// cp is int, '-' is coerced to 45
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
