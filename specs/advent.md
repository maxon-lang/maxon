---
feature: advent of compiler optimization
status: stable
keywords: abs, absolute value, math
category: math-intrinsic
---
# advent of compiler optimization

## Documentation

Matt Godbolt's Advent of Compiler Optimizations 2025
https://www.youtube.com/playlist?list=PL2HVqYf7If8cY4wLk7JUQ2f0JXY_xMQm2

## Tests

<!-- test: day1 -->
```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
<!-- test: day2 -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(x Integer, y Integer) returns Integer
		return x + y
end 'add'

function main() returns ExitCode
	return add(3, y: 4)
end 'main'
```
```exitcode
7
```
<!-- test: day4a -->
<!-- Args: 1 -->
```maxon

typealias Integer = int(i64.min to i64.max)

function multiply(x Integer) returns Integer
		return x * 1
end 'multiply'

function main() returns ExitCode
	let args = CommandLine.args()
	let parsed = try int.fromString(try args.get(1) otherwise "") otherwise 0
	if parsed > 1000 'guard'
		return 99
	end 'guard'
	return multiply(3)
end 'main'
```
```exitcode
3
```
<!-- test: day4b -->
<!-- Args: 3 -->
```maxon

typealias Integer = int(i64.min to i64.max)

function multiply(x Integer) returns Integer
		return x * 2
end 'multiply'

function main() returns ExitCode
	let args = CommandLine.args()
	let parsed = try int.fromString(try args.get(1) otherwise "") otherwise 0
	if parsed > 1000 'guard'
		return 99
	end 'guard'
	return multiply(3)
end 'main'
```
```exitcode
6
```