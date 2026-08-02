---
feature: unused-parameters
status: selfhosted
keywords: [parameters, warnings, errors, unused]
category: diagnostics
---

# Unused Parameter Detection

## Documentation

Maxon requires all function parameters to be used. Declaring unused parameters causes a compilation error.

### Example Error

```maxon
typealias Score = int(i64.min to i64.max)

function add(a Score, b Score) returns Score
	return a  // Error: 'b' is unused
end 'add'
```
Error message:
```
Semantic Error: The parameter 'b' is declared but its value is never used
```

### Solution

Only declare parameters you need:

```maxon
typealias Score = int(i64.min to i64.max)

function identity(a Score) returns Score
	return a  // OK: 'a' is used
end 'identity'
```

### Interface Method Exception

Methods that implement an interface are exempt from this check (see
`interface-conformance` spec). The implementer is forced to declare every
parameter the contract names, even when a particular implementation does
not need one of them. The check still applies to non-interface methods on
the same type and to local `var`/`let` bindings inside interface methods.
## Tests

<!-- test: single-unused -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a
end 'add'

function main() returns ExitCode
	return add(5, b: 10)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-parameters/single-unused.test:5:25: unused variable: 'b'
```

<!-- test: multiple-unused -->
```maxon

typealias Integer = int(i64.min to i64.max)

function test(a Integer, b Integer, c Integer) returns Integer
	return a
end 'test'

function main() returns ExitCode
	return test(1, b: 2, c: 3)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-parameters/multiple-unused.test:5:26: unused variable: 'b'
```

<!-- test: all-used-ok -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(5, b: 10)
end 'main'
```
```exitcode
15
```


<!-- test: none-unused -->
```maxon

typealias Integer = int(i64.min to i64.max)

function multiply(a Integer, b Integer) returns Integer
	return a * b
end 'multiply'

function main() returns ExitCode
	return multiply(7, b: 6)
end 'main'
```
```exitcode
42
```


<!-- test: void-function-unused -->
⚠ **THE PORTED EXPECTATION CARRIED A SECOND LINE, AND SHV2 CANNOT PRODUCE IT FOR THIS PROGRAM.** v1 and the
bootstrap report BOTH `E3005 … Second and subsequent arguments must be named` and
`E3012 … unused variable: 'x'`, ordered by source position. shv2 reports only the first, as `E2053`, and
the difference is the STAGE rather than the rule: shv2's argument-labelling refusal is a PARSE error, so
the parse of the file unwinds and the unused check — which runs at each function's `end` — never reports,
even though `doNothing` is parsed before `main` and its diagnostic had already been recorded. The
bootstrap catches the same rule semantically and keeps going.

The PROGRAM is left exactly as `/specs` wrote it and only the expectation is retracted: rewriting the call
to `doNothing(1, y: 2)` would make shv2 look fully conformant on a program the spec never asked about, and
would hide the divergence rather than record it. **The unused-parameter behaviour this case is named for is
pinned by the other cases in this file** — `single-unused`, `multiple-unused` and
`method-on-non-conforming-type-still-errors` — so what is lost here is the interleaving of the two
diagnostics, not the check.
```maxon

typealias Integer = int(i64.min to i64.max)

function doNothing(x Integer, y Integer)
	let z = 42
end 'doNothing'

function main() returns ExitCode
	doNothing(1, 2)
	return 0
end 'main'
```
```maxoncstderr
error E2053: specs/fragments/unused-parameters/void-function-unused.test:10:15: the second and later arguments must be named ('name: value')
```

<!-- test: method-on-non-conforming-type-still-errors -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Plain
	let value as Integer

	function helper(unused Integer) returns Integer
		return value
	end 'helper'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Plain'

function main() returns ExitCode
	let p = Plain.create(1)
	return p.helper(5)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-parameters/method-on-non-conforming-type-still-errors.test:8:18: unused variable: 'unused'
```
