---
feature: enum-match-exhaustive
status: experimental
keywords: [enum, match, exhaustive, range, to, upto]
category: control-flow
---

# Enum Match Exhaustiveness

## Documentation

Enum match expressions require exhaustive case coverage, just like enum matches. Every enum case must be matched by either an explicit case pattern or a range pattern. Plain `default` is not allowed — use `default throws` if you want a catch-all that throws an error.

Range patterns use enum case references as bounds:

```text
match priority 'check'
    low to medium then print("not urgent")
    high to critical then print("urgent")
end 'check'
```

Ranges use the enum's ordinal values. `to` is inclusive on both ends, `upto` excludes the upper bound.

Overlapping patterns are not allowed — each enum case must be covered by exactly one arm.

## Tests

<!-- test: enum-exhaustive.all-explicit -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	var c = Color.green
	match c 'check'
		red then return 1
		green then return 2
		blue then return 3
	end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: enum-exhaustive.single-range -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	var c = Color.green
	match c 'check'
		red to blue then return 1
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.multiple-ranges -->
```maxon
enum Priority
		low
		medium
		high
		critical
end 'Priority'

function main() returns ExitCode
	var p = Priority.high
	match p 'check'
		low to medium then return 1
		high to critical then return 2
	end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: enum-exhaustive.mix-explicit-and-range -->
```maxon
enum Priority
		low
		medium
		high
		critical
end 'Priority'

function main() returns ExitCode
	var p = Priority.low
	match p 'check'
		low then return 1
		medium to critical then return 2
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.expression -->
```maxon
enum Status
		pending
		approved
		rejected
end 'Status'

function main() returns ExitCode
	var s = Status.approved
	let code = match s 'eval'
		pending gives 0
		approved to rejected gives 1
	end 'eval'
	return code
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.upto-range -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	var c = Color.red
	match c 'check'
		red upto blue then return 1
		blue then return 2
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.default-throws -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

enum AppError
		unmatched
end 'AppError'

typealias ColorCode = int(0 to 10)

function checkColor(c Color) returns ColorCode throws AppError
	match c 'check'
		red then return ColorCode{1}
		default throws AppError.unmatched
	end 'check'
end 'checkColor'

function main() returns ExitCode
	let result = try checkColor(Color.red) otherwise 99
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.float-backed-range -->
```maxon
enum Threshold
		low = 0.1
		medium = 0.5
		high = 0.9
end 'Threshold'

function main() returns ExitCode
	var t = Threshold.medium
	match t 'check'
		low to high then return 1
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: error.enum-not-exhaustive -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	var c = Color.green
	match c 'check'
		red then return 1
		green then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/enum-match-exhaustive/error.enum-not-exhaustive.test:13:2: match on enum 'Color' is not exhaustive, missing: blue
```

<!-- test: error.enum-default-without-throws -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	var c = Color.blue
	match c 'check'
		red then return 1
		default then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E2046: specs/fragments/enum-match-exhaustive/error.enum-default-without-throws.test:12:3: 'default' in a match on enum 'Color' must be followed by 'throws <error>' or 'panic("message")'
```

<!-- test: error.enum-gap-in-ranges -->
```maxon
enum Priority
		low
		medium
		high
		critical
end 'Priority'

function main() returns ExitCode
	var p = Priority.high
	match p 'check'
		low to medium then return 1
		critical then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/enum-match-exhaustive/error.enum-gap-in-ranges.test:14:2: match on enum 'Priority' is not exhaustive, missing: high
```

<!-- test: error.enum-overlapping-ranges -->
```maxon
enum Priority
		low
		medium
		high
		critical
end 'Priority'

function main() returns ExitCode
	var p = Priority.high
	match p 'check'
		low to high then return 1
		medium to critical then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/enum-match-exhaustive/error.enum-overlapping-ranges.test:13:3: overlapping pattern in match: 'medium' is already covered
```

<!-- test: error.enum-explicit-overlaps-range -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	var c = Color.green
	match c 'check'
		red to blue then return 1
		green then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/enum-match-exhaustive/error.enum-explicit-overlaps-range.test:12:3: overlapping pattern in match: 'green' is already covered
```

<!-- test: enum-exhaustive.bare-case-names -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	var c = Color.green
	match c 'check'
		red then return 1
		green then return 2
		blue then return 3
	end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: enum-exhaustive.bare-case-range -->
```maxon
enum Priority
		low
		medium
		high
		critical
end 'Priority'

function main() returns ExitCode
	var p = Priority.high
	match p 'check'
		low to medium then return 1
		high to critical then return 2
	end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: enum-exhaustive.bare-case-expression -->
```maxon
enum Status
		pending
		approved
		rejected
end 'Status'

function main() returns ExitCode
	var s = Status.approved
	let code = match s 'eval'
		pending gives 0
		approved gives 1
		rejected gives 2
	end 'eval'
	return code
end 'main'
```
```exitcode
1
```

<!-- test: error.enum-qualified-case-name -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	var c = Color.green
	match c 'check'
		Color.red then return 1
		Color.green then return 2
		Color.blue then return 3
	end 'check'
end 'main'
```
```maxoncstderr
error E3075: specs/fragments/enum-match-exhaustive/error.enum-qualified-case-name.test:11:3: use 'red' instead of 'Color.red' in match
```
