---
feature: enums-simple
status: experimental
keywords: [enum, enumeration, associated values]
category: type-system
---

# Enums

## Documentation

# Enums

Enums define a type with a fixed set of named variants called cases. Maxon enums support simple enums and enums with associated values.

### Simple Enums

The simplest form of enum defines named cases with no additional data:

```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'
```

Create enum values using dot notation:

```maxon
var dir = Direction.north
```

### Enum Methods

Enums can have methods, similar to structs:

```maxon
enum Direction
	north
	south
	east
	west

	function opposite() returns Direction
		match self 'check'
			north then return Direction.south
			south then return Direction.north
			east then return Direction.west
			west then return Direction.east
		end 'check'
	end 'opposite'

	function isVertical() returns bool
		let result = match self 'check'
			north gives true
			south gives true
			east gives false
			west gives false
		end 'check'
		return result
	end 'isVertical'
end 'Direction'
```

Call methods using instance-dot-method syntax:

```maxon
var dir = Direction.north
var opp = dir.opposite()    // Direction.south
var vert = dir.isVertical() // true
```

## Tests

<!-- test: simple-enum -->
```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'

function main() returns ExitCode
	let dir = Direction.north
	let result = match dir 'check'
		north gives 1
		south gives 0
		east gives 0
		west gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-assignment -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	var c = Color.red
	c = Color.blue
	let result = match c 'check'
		red gives 0
		green gives 0
		blue gives 1
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-not-equal -->
```maxon
enum Status
	pending
	active
	done
end 'Status'

function main() returns ExitCode
	let s = Status.pending
	let result = match s 'check'
		active gives 0
		pending gives 1
		done gives 1
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-comparison -->
```maxon
enum Status
	pending
	active
	done
end 'Status'

function main() returns ExitCode
	let s1 = Status.pending
	match s1 'check'
		pending then return 1
		active then return 0
		done then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-function-param -->
```maxon
enum Status
	on
	off
end 'Status'

function isOn(s Status) returns bool
	let result = match s 'check'
		on gives true
		off gives false
	end 'check'
	return result
end 'isOn'

function main() returns ExitCode
	let status = Status.on
	if isOn(status) 'test'
		return 1
	end 'test'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-return-type -->
```maxon
enum Result
	success
	failure
end 'Result'

function getResult(succeed bool) returns Result
	if succeed 'check'
		return Result.success
	end 'check'
	return Result.failure
end 'getResult'

function main() returns ExitCode
	let r = getResult(true)
	let result = match r 'handle'
		success gives 1
		failure gives 0
	end 'handle'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: float-backed -->
```maxon
enum FloatBacked
	North = 1.1
	South = 2.2
	East = 3.3
end 'FloatBacked'

function main() returns ExitCode
	let f = FloatBacked.North
	let result = match f 'check'
		North gives 1
		South gives 0
		East gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: float-backed-negative -->
```maxon
enum FloatSigned
	below = -2.2
	zero = 0.0
	above = 1.1
end 'FloatSigned'

function main() returns ExitCode
	let f = FloatSigned.below
	let result = match f 'check'
		below gives 2
		zero gives 0
		above gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
2
```

<!-- test: float-backed-signed-zero -->
<!-- -0.0 and +0.0 are DISTINCT raw values in shv2: an enum tag IS the IEEE-754 bit pattern (0x8000000000000000 vs 0x0), matched by an i64 compare, so ONE equality (bit-pattern) governs both the duplicate check and the dispatch — negz is reachable and dispatches to itself. The bootstrap instead REJECTS this pair as a duplicate '0': it folds -0.0 to +0.0 for the duplicate check while still dispatching by bit compare, mixing two equalities. That is a documented oracle divergence; shv2's single-equality design (the same -0.0 sign-preservation `negatedFloatBits` gives every negated float) is why this is accepted, not a wrong answer. Pinned so a future edit that normalized -0.0 would break here rather than silently. -->
```maxon
enum SignedZero
	negz = -0.0
	posz = 0.0
end 'SignedZero'

function main() returns ExitCode
	let x = SignedZero.negz
	return match x 'check'
		negz gives 4
		posz gives 7
	end 'check'
end 'main'
```
```exitcode
4
```

<!-- disabled-test: enum-method -->
<!-- instance methods on an enum receiver (later rung) -->
```maxon
enum Direction
	north
	south

	function isNorth() returns bool
		let result = match self 'check'
			north gives true
			south gives false
		end 'check'
		return result
	end 'isNorth'
end 'Direction'

function main() returns ExitCode
	let d = Direction.north
	if d.isNorth() 'test'
		return 1
	end 'test'
	return 0
end 'main'
```
```exitcode
1
```

<!-- disabled-test: enum-method-returns-enum -->
<!-- instance methods on an enum receiver (later rung) -->
```maxon
enum Toggle
	on
	off

	function flip() returns Toggle
		let result = match self 'check'
			on gives Toggle.off
			off gives Toggle.on
		end 'check'
		return result
	end 'flip'
end 'Toggle'

function main() returns ExitCode
	let t = Toggle.on
	let flipped = t.flip()
	let result = match flipped 'check'
		off gives 1
		on gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: error.duplicate-case -->
```maxon
enum Color
	red
	red
end 'Color'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3030: specs/fragments/enums-simple/error.duplicate-case.test:4:2: duplicate enum case: 'red'
```

<!-- test: error.unknown-enum-case -->
```maxon
enum Color
	red
	blue
end 'Color'

function main() returns ExitCode
	let _c = Color.green
	return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enums-simple/error.unknown-enum-case.test:8:11: unknown enum case: 'green'
```

<!-- test: error.duplicate-raw-value -->
```maxon
enum Status
	ok = 200
	success = 200
end 'Status'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3031: specs/fragments/enums-simple/error.duplicate-raw-value.test:4:2: duplicate raw value: '200'
```

<!-- test: error.raw-value-type-mismatch -->
```maxon
enum Status
	ok = 100
	fail = 5.0
end 'Status'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/enums-simple/error.raw-value-type-mismatch.test:4:2: raw value type mismatch: 'expected int, got float'
```

<!-- test: keyword-as-case-name -->
Keywords can be used as enum case names (e.g., `function`, `return`, `end`).
```maxon
enum TokenType
	function
	return
	end
	if
	else
	let
	var
	identifier
end 'TokenType'

function main() returns ExitCode
	let t = TokenType.function
	let result = match t 'check'
		function gives 1
		return gives 0
		end gives 0
		if gives 0
		else gives 0
		let gives 0
		var gives 0
		identifier gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```
