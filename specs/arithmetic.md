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
function divLive(a Integer, b Integer, x Integer) returns Integer
  var preserved = x + 1
  var result = a / b
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
function modLive(a Integer, b Integer, x Integer) returns Integer
  var preserved = x + 1
  var result = a mod b
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
function divLoop(n Integer) returns Integer
  var sum = 0
  var i = 1
  while i <= n 'loop'
    sum = sum + trunc(100 / i)
    i = i + 1
  end 'loop'
  return sum
end 'divLoop'

function main() returns ExitCode
  return divLoop(5)
end 'main'
```
```exitcode
228
```

<!-- test: div-with-call -->
```maxon
function helper(x Integer) returns Integer
  return x * 2
end 'helper'

function divCall(a Integer, b Integer) returns Integer
  var temp = trunc(a / b)
  var result = helper(temp)
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
function multiDiv(a Integer, b Integer, c Integer, d Integer) returns Integer
  var r1 = a / b
  var r2 = c / d
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
function manyVars(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
  var v1 = a + 1
  var v2 = b + 2
  var v3 = c + 3
  var v4 = d + 4
  var v5 = e + 5
  var v6 = f + 6
  var v7 = v1 + v2
  var v8 = v3 + v4
  var v9 = v5 + v6
  var v10 = v7 + v8
  var v11 = v9 + v10
  var v12 = v11 + v1 + v2 + v3 + v4 + v5 + v6
  return v12
end 'manyVars'

function main() returns ExitCode
  return manyVars(1, b: 2, c: 3, d: 4, e: 5, f: 6)
end 'main'
```
```exitcode
84
```
