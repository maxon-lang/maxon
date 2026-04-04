---
feature: arithmetic
status: stable
keywords: arithmetic, operators, math
category: operators
---
# Arithmetic Operators

## Documentation

Maxon supports basic arithmetic operators for integers:

- `+` addition
- `-` subtraction
- `*` multiplication
- `/` division
- `mod` modulo

## Tests

<!-- test: addition -->
```maxon
function main() returns ExitCode
	return 10 + 5
end 'main'
```
```exitcode
15
```

<!-- test: subtraction -->
```maxon
function main() returns ExitCode
	return 20-8
end 'main'
```
```exitcode
12
```

<!-- test: multiplication -->
```maxon
function main() returns ExitCode
	return 6 * 7
end 'main'
```
```exitcode
42
```

<!-- test: division -->
```maxon
function main() returns ExitCode
	return trunc(100 / 4)
end 'main'
```
```exitcode
25
```

<!-- test: modulo -->
```maxon
function main() returns ExitCode
	return 17 mod 5
end 'main'
```
```exitcode
2
```

<!-- test: complex-expression -->
```maxon
function main() returns ExitCode
	return 10 + 5 * 2
end 'main'
```
```exitcode
20
```

<!-- test: div-live-values -->
```maxon

typealias Integer = int(i64.min to i64.max)

function divLive(a Integer, b Integer, x Integer) returns Integer
	let preserved = x + 1
	let result = a / b
	return trunc(result + preserved)
end 'divLive'

function main() returns ExitCode
	return divLive(10, b: 2, x: 5)
end 'main'
```
```exitcode
11
```

<!-- test: mod-live-values -->
```maxon

typealias Integer = int(i64.min to i64.max)

function modLive(a Integer, b Integer, x Integer) returns Integer
	let preserved = x + 1
	let result = a mod b
	return result + preserved
end 'modLive'

function main() returns ExitCode
	return modLive(10, b: 3, x: 5)
end 'main'
```
```exitcode
7
```

<!-- test: div-loop -->
```maxon

typealias Integer = int(i64.min to i64.max)

function divLoop(n Integer) returns Integer
	var sum = 0
	var i = 1
	while i <= n 'loop'
		sum = sum + trunc(50 / i)
		i = i + 1
	end 'loop'
	return sum
end 'divLoop'

function main() returns ExitCode
	return divLoop(5)
end 'main'
```
```exitcode
113
```

<!-- test: div-with-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function helper(x Integer) returns Integer
	return x * 2
end 'helper'

function divCall(a Integer, b Integer) returns Integer
	let temp = trunc(a / b)
	let result = helper(temp)
	return result + temp
end 'divCall'

function main() returns ExitCode
	return divCall(10, b: 2)
end 'main'
```
```exitcode
15
```

<!-- test: multi-div -->
```maxon

typealias Integer = int(i64.min to i64.max)

function multiDiv(a Integer, b Integer, c Integer, d Integer) returns Integer
	let r1 = a / b
	let r2 = c / d
	return trunc(r1 + r2)
end 'multiDiv'

function main() returns ExitCode
	return multiDiv(10, b: 2, c: 20, d: 4)
end 'main'
```
```exitcode
10
```

<!-- test: register-pressure -->
```maxon

typealias Integer = int(i64.min to i64.max)

function manyVars(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	let v1 = a + 1
	let v2 = b + 2
	let v3 = c + 3
	let v4 = d + 4
	let v5 = e + 5
	let v6 = f + 6
	let v7 = v1 + v2
	let v8 = v3 + v4
	let v9 = v5 + v6
	let v10 = v7 + v8
	let v11 = v9 + v10
	let v12 = v11 + v1 + v2 + v3 + v4 + v5 + v6
	return v12
end 'manyVars'

function main() returns ExitCode
	return manyVars(1, b: 2, c: 3, d: 4, e: 5, f: 6)
end 'main'
```
```exitcode
84
```
