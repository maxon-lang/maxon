# Maxon Language Style Guide

A comprehensive guide to writing idiomatic and consistent Maxon code.

## Table of Contents

1. [Indentation](#indentation)
2. [Line Endings](#line-endings)
3. [Naming Conventions](#naming-conventions)
4. [Function Declaration](#function-declaration)
5. [Control Flow](#control-flow)
6. [Comments](#comments)
7. [Whitespace](#whitespace)
8. [Formatting](#formatting)

---

## Indentation

### Use Tabs, Not Spaces

Always use **tabs for indentation**. One tab per indentation level.

```maxon
function main() int
	var x = 5
	if x > 0 'check'
		print(x)
	end 'check'
	return 0
end 'main'
```

### Consistent Indentation Levels

Each block increases indentation by one level:
- `function`, `if`, `while`, `for`, `else` increase indentation
- `end` statements decrease indentation

```maxon
function calculate(value int) int
	if value > 0 'positive'
		while value > 0 'loop'
			value = value - 1
		end 'loop'
	else
		value = 0
	end 'positive'
	return value
end 'calculate'
```

---

## Line Endings

### Use LF (Unix-style)

All Maxon source files should use **LF (line feed, `\n`)** line endings, not CRLF.

**Why:** LF is the standard across most development environments and version control systems.

### Setting in VSCode

To ensure LF line endings in VSCode:

1. Click "CRLF" or "LF" in the bottom right status bar
2. Select **LF**
3. Save the file

The VSCode extension for Maxon will preserve your chosen line ending mode.

---

## Naming Conventions

### Function Names

Use **camelCase** for function names. Start with a lowercase letter.

```maxon
function calculateSum(a int, b int) int
	return a + b
end 'calculateSum'

function printGreeting(name string) int
	print(name)
	return 0
end 'printGreeting'
```

### Variable Names

Use **camelCase** for variable names.

```maxon
var myValue = 42
var userCount = 100
var isValid = true
```

### Constants

Use **UPPER_CASE** with underscores for constants (when supported).

```maxon
var MAX_ITERATIONS = 1000
var DEFAULT_TIMEOUT = 30
```

### Block Identifiers

Block identifiers (for `if`, `while`, `for`, `end`) should be descriptive and use **lowercase with hyphens or camelCase**.

```maxon
if value > 0 'check-positive'
	...
end 'check-positive'

while count < 10 'mainLoop'
	...
end 'mainLoop'
```

---

## Function Declaration

### Format

```maxon
function name(param1 type1, param2 type2) returnType
	...
end 'name'
```

### With Multiple Parameters

Parameters should be on the same line. If the line is too long, break after the opening parenthesis:

```maxon
function processData(input string, offset int, length int) int
	...
end 'processData'
```

### Single Responsibility

Each function should have a single, well-defined purpose. Keep functions focused and concise.

---

## Control Flow

### If Statements

Place the block identifier immediately after the condition:

```maxon
if condition 'check'
	...
end 'check'
```

Always include a matching block identifier for the `end` statement.

### If-Else

```maxon
if x > 0 'positive'
	print(x)
else
	print(0)
end 'positive'
```

### While Loops

```maxon
while condition 'loop'
	...
end 'loop'
```

### For Loops

```maxon
for i = 0 to 10 'count'
	print(i)
end 'count'
```

### Break Statement

Use `break` to exit loops early, with its corresponding block identifier nearby:

```maxon
while true 'processLoop'
	if shouldStop 'check'
		break
	end 'check'
end 'processLoop'
```

---

## Comments

### Line Comments

Use comments to explain **why**, not **what**. The code should be clear about what it does.

```maxon
function processItems(count int) int
	var result = 0
	
	' Sum first N items (skipping zeros for optimization)
	while count > 0 'loop'
		var item = getItem(count)
		if item != 0 'notZero'
			result = result + item
		end 'notZero'
		count = count - 1
	end 'loop'
	
	return result
end 'processItems'
```

### Comment Style

Use single quotes `'` for comments (standard Maxon style).

```maxon
' This is a comment
var x = 5  ' Inline comment
```

### Avoid Obvious Comments

```maxon
' Bad: obvious what the code does
var i = 0  ' Set i to 0

' Good: explains the purpose
var i = 0  ' Initialize counter for loop iterations
```

---

## Whitespace

### Blank Lines

Use blank lines to separate logical sections within functions:

```maxon
function initialize() int
	' Setup phase
	var config = loadConfig()
	var data = createData()
	
	' Validation phase
	if not isValid(config) 'invalid'
		return 1
	end 'invalid'
	
	' Execution phase
	return run(data)
end 'initialize'
```

### Single Blank Line Between Top-Level Items

Use a single blank line between function definitions:

```maxon
function first() int
	return 1
end 'first'

function second() int
	return 2
end 'second'
```

### No Trailing Whitespace

Remove trailing spaces and tabs at the end of lines. The VSCode formatter will do this automatically.

### No Multiple Consecutive Blank Lines

Avoid more than one blank line in a row. The formatter will consolidate multiple blank lines into one.

---

## Formatting

### Line Length

Keep lines reasonably short for readability. Aim for under 80 characters when practical, but readability is more important than a strict limit.

```maxon
' Good: clear and readable
function calculateDistance(x1 int, y1 int, x2 int, y2 int) int
	var dx = x2 - x1
	var dy = y2 - y1
	return sqrt(dx * dx + dy * dy)
end 'calculateDistance'
```

### Operator Spacing

Use spaces around binary operators:

```maxon
var result = a + b
var comparison = x > y
var product = a * b
```

### Function Calls

No space before the opening parenthesis:

```maxon
print(value)
var x = calculateSum(a, b)
```

### Block Structure

Always use block identifiers with `end` statements:

```maxon
' Good
if condition 'check'
	...
end 'check'

' Avoid: missing identifier
if condition
	...
end
```

---

## Formatting Tool

The VSCode extension includes a built-in code formatter that will automatically:

- Normalize indentation to tabs
- Consolidate multiple blank lines into one
- Remove trailing whitespace
- Ensure files end with a newline

### Using the Formatter

**Keyboard Shortcut:** `Ctrl+Shift+F` (Format Document) or `Ctrl+K Ctrl+F` (Format Selection)

**On Save:** The formatter may be configured to run automatically on save (check your VSCode settings)

---

## Example: Complete Program

Here's a complete program following all style guide conventions:

```maxon
' Calculate the sum of integers from 1 to N
function sum(n int) int
	var result = 0
	var i = 1
	
	while i <= n 'loop'
		result = result + i
		i = i + 1
	end 'loop'
	
	return result
end 'sum'

' Check if a number is prime
function isPrime(num int) int
	if num <= 1 'notPrime'
		return 0
	end 'notPrime'
	
	var i = 2
	while i * i <= num 'divisorCheck'
		if num % i = 0 'hasDivisor'
			return 0
		end 'hasDivisor'
		i = i + 1
	end 'divisorCheck'
	
	return 1
end 'isPrime'

function main() int
	var total = sum(10)
	print(total)
	
	var prime = isPrime(7)
	print(prime)
	
	return 0
end 'main'
```

---

## Summary

Key takeaways:

- **Indentation:** Tabs, one per level
- **Line Endings:** LF only
- **Names:** camelCase for variables and functions
- **Functions:** Single responsibility, clear names
- **Comments:** Explain why, not what
- **Whitespace:** Blank lines separate logical sections
- **Formatting:** Use the VSCode formatter automatically
- **Block Identifiers:** Always use descriptive identifiers with `end`

Following these conventions ensures your Maxon code is:
- **Readable:** Easy to understand and follow
- **Consistent:** Matches the style of other Maxon code
- **Maintainable:** Easier to modify and debug
- **Professional:** Suitable for team collaboration

