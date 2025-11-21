---
feature: single-line-if
status: stable
keywords: if, single-line, compact, control flow
category: control-flow
---

## Developer Notes

Single-line if statements provide a compact syntax for simple conditionals.

**Implementation Details:**
- Parser: `parseIf()` detects single-line vs multi-line based on newline
- No block identifier required for single-line
- Statement must be on the same line as the condition
- Codegen: Same as multi-line if, creates conditional branch

**Syntax:**
```
if <condition> <statement>
```

**Restrictions:**
- Only one statement allowed
- Statement must be on the same line
- No `else` clause possible
- Common use: `if <cond> break`, `if <cond> return <value>`

**Parser Behavior:**
- If newline found after condition → expects block identifier
- If statement found on same line → single-line mode
- Error if statement is on next line without block identifier

## Documentation

# Single-Line If

Compact syntax for simple conditional statements.

**Syntax:**

```maxon
if <condition> <statement>
```

**Example:**

```maxon
var x = 11
var i = 5
while i > 0 'loop'
    if x = 11 break
    i = i - 1
end 'loop'
```

**Notes:**
- Condition and statement must be on the same line
- No block identifier needed
- Only one statement allowed
- No else clause possible
- Ideal for simple guards like `if x = 0 break` or `if done return result`

## Tests

<!-- test: single-line-if.break -->
```maxon
function main() int
	var x = 11
	var i = 5
	while i > 0 'loop'
		if x = 11 break
		i = i - 1
	end 'loop'
	return i
end 'main'
```
ExitCode: 5

<!-- test: single-line-if.return -->
```maxon
function main() int
	var x = 42
	if x = 42 return 100
	return 0
end 'main'
```
ExitCode: 100

<!-- test: single-line-if.assignment -->
```maxon
function main() int
	var x = 10
	var result = 0
	if x = 10 result = 42
	return result
end 'main'
```
ExitCode: 42
