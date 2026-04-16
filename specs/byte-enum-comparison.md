---
feature: byte-enum-comparison
status: stable
keywords: [byte, enum, comparison, constants]
category: types
---

# Byte Enum Comparison

## Documentation

Byte values can be compared directly with constants-enum members that have integer backing values in the 0-255 range. No explicit `as int` cast is needed.

```maxon
enum Ascii
	underscore = 95
	space = 32
end 'Ascii'

let b = 95 as Byte
if b == Ascii.underscore   // OK — no cast needed
```

Both orderings work: `byte == enum` and `enum == byte`.

## Tests

### Byte Equals Enum

<!-- test: byte-enum-comparison.byte-eq-enum -->
```maxon

typealias Byte = byte(0 to u8.max)

enum Ascii
	underscore = 95
	space = 32
	zero = 48
end 'Ascii'

function main() returns ExitCode
	let b = 95 as Byte
	if b == Ascii.underscore 'match'
		return 0
	end 'match'
	return 1
end 'main'
```
```exitcode
0
```

### Enum Equals Byte

<!-- test: byte-enum-comparison.enum-eq-byte -->
```maxon

typealias Byte = byte(0 to u8.max)

enum Ascii
	underscore = 95
	space = 32
	zero = 48
end 'Ascii'

function main() returns ExitCode
	let s = 32 as Byte
	if Ascii.space == s 'match'
		return 0
	end 'match'
	return 1
end 'main'
```
```exitcode
0
```

### Byte Not Equals Enum

<!-- test: byte-enum-comparison.byte-ne-enum -->
```maxon

typealias Byte = byte(0 to u8.max)

enum Ascii
	underscore = 95
	space = 32
	zero = 48
end 'Ascii'

function main() returns ExitCode
	let b = 95 as Byte
	if b == Ascii.space 'noMatch'
		return 1
	end 'noMatch'
	return 0
end 'main'
```
```exitcode
0
```
