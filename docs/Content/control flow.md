# Control Flow

## Loops

~~~
function main() int
	var x = 5
	var i = 3
	while true 'a loop'
		x = x + 2
		i = i - 1
		if i = 0 break
	end 'a loop'
	return x
end 'main'

ExitCode: 11
~~~

## If Statements

Maxon supports both single-line and multi-line if statements.

### Single-Line If

For simple conditions with a single statement, you can use a compact single-line syntax without block identifiers:

~~~
function main() int
	var x = 10
	var result = 0
	if x = 10 result = 42
	return result
end 'main'

ExitCode: 42
~~~

The single-line if syntax requires that the condition and statement are on the same line. This is ideal for simple guard conditions like `if x = 0 break` or quick assignments.

### Multi-Line If

For more complex conditions or multiple statements, use the traditional block identifier syntax:

~~~
function main() int
	var x = 5
	if x = 5 'check'
		return 1
	else 'check'
		return 0
	end 'check'
end 'main'

ExitCode: 1
~~~

Multi-line if statements require:
- A block identifier (string) after the condition: `if x = 5 'check'`
- The same identifier after `else` (if present): `else 'check'`
- The same identifier after `end`: `end 'check'`

This ensures proper block matching in complex nested structures.
