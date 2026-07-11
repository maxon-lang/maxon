---
feature: arithmetic
status: selfhosted
keywords: [arithmetic, operators, math]
category: operators
milestone: M3
---

# Arithmetic Operators

## Documentation

Maxon supports basic arithmetic operators for integers:

- `+` addition
- `-` subtraction
- `*` multiplication
- `/` division
- `mod` modulo

At M3 the integer non-trapping set — `+`, `-`, `*` — is implemented, parsed by a
Pratt precedence climber: multiplicative (`*`) binds tighter than additive
(`+`/`-`), and both are left-associative. So `10 + 5 * 2` groups as `10 + (5 * 2)`
= 20. `/` and `mod` are DEFERRED to M5: x64 `idiv` pins its operands to the fixed
RAX/RDX pair, which requires the real register allocator (M3's placeholder gives
every value a fresh pool register and cannot honor fixed-register constraints).

## Tests

These are the M3 slice of `specs/arithmetic.md`: `+` (from M2), `-`, `*`, and the
precedence proof. The division/modulo tests — and every test that also needs
`trunc`, function parameters, or `while` — are DEFERRED and recorded under
`## Deferred` below so they are re-enabled at their milestones rather than
forgotten.

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

<!-- test: complex-expression -->
Proves `*` binds tighter than `+`: `10 + 5 * 2` is `10 + (5 * 2)` = 20, not
`(10 + 5) * 2` = 30.
```maxon
function main() returns ExitCode
	return 10 + 5 * 2
end 'main'
```
```exitcode
20
```

## Deferred

Tests recorded for re-enablement at the milestone that unblocks them. They live
in this `## Deferred` section — NOT `## Tests` — so the spec-test parser (which
scans only `## Tests`, up to the next `## ` heading) never extracts them, and
they carry NO `<!-- test: … -->` marker. To re-enable: move the test up into
`## Tests` and prefix it with its `<!-- test: NAME -->` marker.

Prerequisites:

  - division, modulo — need x64 `idiv`'s fixed RAX/RDX pair, hence the real
    register allocator (M5). `division` also needs `trunc` (float→int, later).
  - div-live-values, mod-live-values, multi-div — division + function parameters
    with named args (M5).
  - div-loop — division + `while` (M4) + mutable reassignment.
  - div-with-call — division + function calls (M5).
  - register-pressure — pure `+`, but 12 simultaneous live values exceed M3's
    6-register no-liveness placeholder allocator; re-enable at M5 with the real
    liveness-based allocator (spilling).

### division
```maxon
function main() returns ExitCode
	return trunc(100 / 4)
end 'main'
```
```exitcode
25
```

### modulo
```maxon
function main() returns ExitCode
	return 17 mod 5
end 'main'
```
```exitcode
2
```

### div-live-values
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

### mod-live-values
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

### div-loop
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

### div-with-call
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

### multi-div
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

### register-pressure
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
