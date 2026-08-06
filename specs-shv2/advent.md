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

⚠ **PORT NOTE (BATCH29/A3a).** `status:` reads `stable` here and `selfhosted` in `/specs`: that frontmatter names the runner that owns the file, and the owner here is shv2. Its `/specs` twin stays `status: selfhosted`: all 4 cases fail the bootstrap on those blocks alone.

⚠ **PORT NOTE (BATCH29/A3a).** The `/specs` original carries 16 `RequiredIR:<target>` block(s) in v1's single-section dump format. None survives the port: shv2's spec parser has no `RequiredIR` arm, so every one of them would be read by nobody while reading as coverage — the shape this batch exists to remove, and `SpecParser.isUnimplementedFenceOpen` now refuses the fence rather than walking past it. What pins the emitted code here is each case's MINTED FRAGMENT GOLDEN, which records what THIS compiler emits rather than what v1 did. The `/specs` copy keeps its blocks and stays `status: selfhosted`; its `status-reason:` names this file.

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