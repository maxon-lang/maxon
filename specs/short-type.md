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
	let arr = [U16{100}, U16{200}, U16{300}]
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

function main() returns ExitCode
	let arr = [U16{65535}, U16{0}, U16{1}]
	let a = try arr.get(0) otherwise 0
	let b = try arr.get(2) otherwise 0
	let sum = a as Result + b as Result
	return sum
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
	let arr = [I16{100}, I16{-50}, I16{200}]
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
```maxon
typealias U16 = int(0 to 65535)

function main() returns ExitCode
	let arr = [U16{10}, U16{20}, U16{30}]
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
	let a = U16{1000}
	let b = U16{500}
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

var counter = U16{42}

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

var counter = U16{0}

function main() returns ExitCode
	counter = U16{99}
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
	var arr = [U16{0}, U16{0}, U16{0}]
	arr.set(1, value: U16{42})
	let v = try arr.get(1) otherwise 0
	return v
end 'main'
```
```exitcode
42
```
