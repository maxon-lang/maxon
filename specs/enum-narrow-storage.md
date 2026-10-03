---
feature: enum-narrow-storage
status: selfhosted
keywords: [enum, storage, rdata, width, narrow, sign-extension]
category: codegen
---
# Enum Narrow Storage in .rdata

## Documentation

Enum values occupy the narrowest integer width that fits all their raw values:

- `u8` when raw values are 0..255
- `u16` when raw values are 0..65535
- `u32` when raw values are 0..4294967295
- `i8` when raw values fit −128..127 (any negative case forces signed)
- `i16` when raw values fit −32768..32767 with at least one negative
- `i32` when raw values fit −2147483648..2147483647 with at least one negative
- `i64`/`u64` otherwise

This narrowing applies to memory representation everywhere — struct fields, array elements, `.rdata` constants — not just the typealias range printed at the type-system level. `Array with Color` where `Color` is a three-case enum uses `element_size = 1`, and `[Color.red, Color.green, Color.blue]` lays out three contiguous bytes in `.rdata`.

Loads from narrow signed slots (`iN`) sign-extend into the 64-bit register that holds the SSA value; loads from narrow unsigned slots (`uN`) zero-extend. Stores write only the low N bytes of the i64 source operand.

## Tests

Each test below declares an enum whose raw-value range forces a specific storage width, builds a 3-element array literal, and asserts the exact bytes in `.rdata`. The chosen raw values fall outside the next-narrower type's range so a wrong-width emission would either fail the `RequiredRdata` byte-compare or wrap the value seen at runtime through `arr.get(...)`.

<!-- test: enum-rdata-u8-array -->
### u8-backed enum array packs as 1 byte per element
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let palette = [Color.red, Color.green, Color.blue]
	let v = try palette.get(2) otherwise Color.red
	return v.ordinal
end 'main'
```
```exitcode
2
```
```RequiredRdata
u8[] 0, 1, 2
```

<!-- test: enum-rdata-u16-array -->
### u16-backed enum array packs as 2 bytes per element
Raw values exceed u8.max so the backing must be at least u16. A u8 emission would either fail the rdata byte-compare or wrap `10000` to `16`.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Code
	a = 100
	b = 1000
	c = 10000
end 'Code'

function main() returns ExitCode
	let arr = [Code.a, Code.b, Code.c]
	let v = try arr.get(1) otherwise Code.a
	return ((v.rawValue as Integer) - 1000) as ExitCode
end 'main'
```
```exitcode
0
```
```RequiredRdata
u16[] 100, 1000, 10000
```

<!-- test: enum-rdata-u32-array -->
### u32-backed enum array packs as 4 bytes per element
Raw values exceed u16.max so the backing must be at least u32.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Big
	small = 100
	medium = 100000
	large = 1000000
end 'Big'

function main() returns ExitCode
	let arr = [Big.small, Big.medium, Big.large]
	let v = try arr.get(0) otherwise Big.small
	return (v.rawValue as Integer) as ExitCode
end 'main'
```
```exitcode
100
```
```RequiredRdata
u32[] 100, 100000, 1000000
```

<!-- test: enum-rdata-i8-array -->
### i8-backed enum array packs as 1 signed byte per element
Negative raw values force signed backing. This test exercises the sign-extending load — a buggy backend that emits `movzx` (zero-extending) on i8 would read `-1` back as `255`, making `arr.get(0).rawValue + 2 == 257`, which truncates and fails the exit-code check before the `RequiredRdata` byte-compare even runs. Two layers of detection.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Signal
	minus = -1
	zero = 0
	plus = 1
end 'Signal'

function main() returns ExitCode
	let arr = [Signal.minus, Signal.zero, Signal.plus]
	let v = try arr.get(0) otherwise Signal.zero
	return ((v.rawValue as Integer) + 2) as ExitCode
end 'main'
```
```exitcode
1
```
```RequiredRdata
i8[] -1, 0, 1
```

<!-- test: enum-rdata-i16-array -->
### i16-backed enum array packs as 2 signed bytes per element
Absolute raw values exceed i8.max so the backing must be at least i16 signed.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Offset
	behind = -200
	here = 0
	ahead = 200
end 'Offset'

function main() returns ExitCode
	let arr = [Offset.behind, Offset.here, Offset.ahead]
	let v = try arr.get(2) otherwise Offset.here
	return ((v.rawValue as Integer) - 100) as ExitCode
end 'main'
```
```exitcode
100
```
```RequiredRdata
i16[] -200, 0, 200
```

<!-- test: enum-rdata-i32-array -->
### i32-backed enum array packs as 4 signed bytes per element
Absolute raw values exceed i16.max so the backing must be at least i32 signed.
```maxon
typealias Integer = int(i64.min to i64.max)

enum BigOffset
	farBehind = -100000
	here = 0
	farAhead = 100000
end 'BigOffset'

function main() returns ExitCode
	let arr = [BigOffset.farBehind, BigOffset.here, BigOffset.farAhead]
	let v = try arr.get(0) otherwise BigOffset.here
	return ((v.rawValue as Integer) + 100100) as ExitCode
end 'main'
```
```exitcode
100
```
```RequiredRdata
i32[] -100000, 0, 100000
```
