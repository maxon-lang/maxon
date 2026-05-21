---
feature: short-type
status: active
keywords: [short, 16-bit, u16, i16, storage]
category: types
---

# Short (16-bit) Storage

## Documentation

Ranged integer types whose range fits in 16 bits automatically use 2-byte storage in arrays and global variables. All arithmetic is still performed at 32-bit width.

This happens automatically via the ranged typealias system:

```maxon
typealias Pixel = int(0 to 65535)     // stored as u16 (2 bytes)
typealias Offset = int(-32768 to 32767)  // stored as i16 (2 bytes)
```

Values in 16-bit storage are zero-extended (unsigned) or sign-extended (signed) to 32-bit registers when loaded.

## Tests

### U16 Array Basic

<!-- test: short-type.u16-array-basic -->
```maxon
typealias U16 = int(0 to 65535)

function main() returns ExitCode
	let arr = [100 as U16, 200 as U16, 300 as U16]
	let a = try arr.get(0) otherwise 0
	let b = try arr.get(1) otherwise 0
	let c = try arr.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
600
```

### U16 Array Max Value

<!-- test: short-type.u16-array-max -->
```maxon
typealias U16 = int(0 to 65535)
typealias Result = int(0 to u32.max)

function sumWide(a U16, b U16) returns Result
	return a + b
end 'sumWide'

function main() returns ExitCode
	let arr = [65535 as U16, 0 as U16, 1 as U16]
	let a = try arr.get(0) otherwise 0
	let b = try arr.get(2) otherwise 0
	return sumWide(a, b: b)
end 'main'
```
```exitcode
65536
```

### I16 Array Basic

<!-- test: short-type.i16-array-basic -->
```maxon
typealias I16 = int(-32768 to 32767)

function main() returns ExitCode
	let arr = [100 as I16, -50 as I16, 200 as I16]
	let a = try arr.get(0) otherwise 0
	let c = try arr.get(2) otherwise 0
	return a + c
end 'main'
```
```exitcode
300
```

### U16 Constant Array Rdata

<!-- test: short-type.u16-rdata -->
Narrow array-element storage flows through the collection-from-array
syntax: when `U16Array = Array with U16`, the element type `U16`
(`int(0 to 65535)`) propagates through `ParseFromExpression` →
`EmitArrayLiteralElements` so each integer literal is range-checked at
compile time and re-tagged with the optimal storage kind (i16/u16). The
constant-folding pass then lifts the array into `.rdata` with the narrow
element width, replacing what `U16{x}` per-element construction used to
produce.

```maxon
typealias U16 = int(0 to 65535)
typealias U16Array = Array with U16

function main() returns ExitCode
	let arr = U16Array from [10, 20, 30]
	let a = try arr.get(0) otherwise 0
	let b = try arr.get(1) otherwise 0
	let c = try arr.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
60
```
```RequiredRdata
u16 10 20 30
```

### U16 Arithmetic Uses 32-bit Ops

<!-- test: short-type.u16-arithmetic-32bit -->
```maxon
typealias U16 = int(0 to 65535)

function main() returns ExitCode
	let a = 1000 as U16
	let b = 500 as U16
	return a - b
end 'main'
```
```exitcode
500
```

### U16 Global Variable

<!-- test: short-type.u16-global -->
```maxon
typealias U16 = int(0 to 65535)

var counter = 42 as U16

function main() returns ExitCode
	return counter
end 'main'
```
```exitcode
42
```
```RequiredData
u16 42
```

### U16 Global Variable Write

<!-- test: short-type.u16-global-write -->
```maxon
typealias U16 = int(0 to 65535)

var counter = 0 as U16

function main() returns ExitCode
	counter = 99 as U16
	return counter
end 'main'
```
```exitcode
99
```

### U16 Array Set

<!-- test: short-type.u16-array-set -->
```maxon
typealias U16 = int(0 to 65535)

function main() returns ExitCode
	var arr = [0 as U16, 0 as U16, 0 as U16]
	try arr.set(1, value: 42) otherwise panic("test invariant: set OOB")
	let v = try arr.get(1) otherwise 0
	return v
end 'main'
```
```exitcode
42
```
