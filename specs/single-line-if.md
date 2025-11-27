---
feature: single-line-if
status: stable
keywords: if, single-line, compact, control flow
category: control-flow
---

## Developer Notes

Single-line if statements provide a compact syntax for simple conditionals.

**Implementation Details:**
- Parser: `parseIf()` detects single-line vs multi-line based on `then` keyword
- No block identifier required for single-line
- Uses `then` keyword to introduce the statement
- Codegen: Same as multi-line if, creates conditional branch

**Syntax:**
```
if <condition> then <statement>
```

**Restrictions:**
- Only one statement allowed
- `then` keyword required before statement
- Single-line else is possible: `if <cond> then <stmt> else <stmt>`
- Common use: `if <cond> then break`, `if <cond> then return <value>`

**Parser Behavior:**
- If `then` keyword found after condition → single-line mode
- If block identifier found after condition → multi-line mode
- Error if neither `then` nor block identifier found

## Documentation

# Single-Line If

Compact syntax for simple conditional statements.

**Syntax:**

```maxon
if <condition> then <statement>
```
**Example:**

```maxon
var x = 11
var i = 5
while i > 0 'loop'
    if x == 11 then break
    i = i - 1
end 'loop'
```
**Notes:**
- Use `then` keyword before the statement
- No block identifier needed
- Only one statement allowed
- Single-line else is possible: `if <cond> then <stmt> else <stmt>`
- Ideal for simple guards like `if x == 0 then break` or `if done then return result`

## Tests

<!-- test: single-line-if.break -->
```maxon
function main() int
	var x = 11
	var i = 5
	while i > 0 'loop'
		if x == 11 then break
		i = i - 1
	end 'loop'
	return i
end 'main'
```
```exitcode
5
```

<!-- test: single-line-if.return -->
```maxon
function main() int
	var x = 42
	if x == 42 then return 100
	return 0
end 'main'
```
```exitcode
100
```

<!-- test: single-line-if.assignment -->
```maxon
function main() int
	var x = 10
	var result = 0
	if x == 10 then result = 42
	return result
end 'main'
```
```exitcode
42
```
