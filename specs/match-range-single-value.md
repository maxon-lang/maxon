---
feature: match-range-single-value
status: experimental
keywords: [match, range, to, upto, single-value]
category: control-flow
---

# Match Range Single-Value Rejection

## Documentation

A match range pattern that covers exactly one value is rejected as a compile error. Such a pattern is always a mistake — the user either meant the bare value or got the bounds wrong. The rule:

- Inclusive `to` with equal bounds — `5 to 5`, `'a' to 'a'`.
- Exclusive `upto` where lower + 1 == upper (integers and characters, whose domains have an immediate successor) — `5 upto 6`, `'a' upto 'b'`.

Open-bound forms (`min to X`, `X to max`) and multi-value ranges (`5 to 7`) are unaffected.

A range never spans enum or union CASES: that is **E3146**, and an arm names its cases with `or`, one per line.

## Tests

<!-- test: error.int-to-same -->
```maxon
function main() returns ExitCode
	let x = 5
	match x 'eval'
		5 to 5 then return 0
		default then return 1
	end 'eval'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/match-range-single-value/error.int-to-same.test:5:3: range pattern '5 to 5' covers a single value; use the bare value instead
```

<!-- test: error.int-upto-by-one -->
```maxon
function main() returns ExitCode
	let x = 5
	match x 'eval'
		5 upto 6 then return 0
		default then return 1
	end 'eval'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/match-range-single-value/error.int-upto-by-one.test:5:3: range pattern '5 upto 6' covers a single value; use the bare value instead
```

<!-- test: error.char-to-same -->
```maxon
function main() returns ExitCode
	let c = 'a'
	match c 'eval'
		'a' to 'a' then return 0
		default then return 1
	end 'eval'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/match-range-single-value/error.char-to-same.test:5:3: range pattern ''a' to 'a'' covers a single value; use the bare value instead
```

<!-- test: error.char-upto-adjacent -->
```maxon
function main() returns ExitCode
	let c = 'a'
	match c 'eval'
		'a' upto 'b' then return 0
		default then return 1
	end 'eval'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/match-range-single-value/error.char-upto-adjacent.test:5:3: range pattern ''a' upto 'b'' covers a single value; use the bare value instead
```

<!-- test: ok.int-multi-value-range -->
```maxon
function main() returns ExitCode
	let x = 6
	let result = match x 'eval'
		5 to 7 gives 0
		default gives 1
	end 'eval'
	return result
end 'main'
```
```exitcode
0
```

<!-- test: ok.int-upto-two-apart -->
```maxon
function main() returns ExitCode
	let x = 5
	let result = match x 'eval'
		5 upto 7 gives 0
		default gives 1
	end 'eval'
	return result
end 'main'
```
```exitcode
0
```
