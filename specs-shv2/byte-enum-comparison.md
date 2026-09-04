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

A constants-enum member with an integer backing value also coerces to its
backing type when passed where that type is expected — a function argument, a
collection element, a `return` value. No `.rawValue` and no explicit cast are
needed, mirroring the comparison rule above.

```maxon
enum Ascii
	space = 32
end 'Ascii'

var out = ByteArray.create()
out.push(Ascii.space)   // OK — coerces to Byte, no .rawValue needed
```

## Tests

### Byte Equals Enum

<!-- test: byte-enum-comparison.byte-eq-enum -->
```maxon

typealias Byte = int(0 to u8.max)

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

typealias Byte = int(0 to u8.max)

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

typealias Byte = int(0 to u8.max)

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

### Enum Coerces To Byte Argument

<!-- test: byte-enum-comparison.enum-coerces-to-byte-arg -->
```maxon

typealias Byte = int(0 to u8.max)

enum Ascii
	underscore = 95
	space = 32
	zero = 48
end 'Ascii'

function takesByte(b Byte) returns Byte
	return b
end 'takesByte'

function main() returns ExitCode
	let r = takesByte(Ascii.underscore)
	if r == 95 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### Enum Coerces To Byte Array Element

<!-- test: byte-enum-comparison.enum-coerces-to-byte-array-element -->
```maxon

typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

enum Ascii
	underscore = 95
	space = 32
	zero = 48
end 'Ascii'

function main() returns ExitCode
	var out = ByteArray.create()
	out.push(Ascii.space)
	out.push(Ascii.zero)
	let first = try out.get(0) otherwise 99
	let second = try out.get(1) otherwise 99
	if first == 32 and second == 48 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### Enum Coerces As Return Value

<!-- test: byte-enum-comparison.enum-coerces-as-return -->
```maxon

typealias Byte = int(0 to u8.max)

enum Punct
	colon = 58
	comma = 44
end 'Punct'

function colonByte() returns Byte
	return Punct.colon
end 'colonByte'

function main() returns ExitCode
	let r = colonByte()
	if r == 58 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```
