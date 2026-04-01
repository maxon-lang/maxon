---
feature: enum-struct-backing
status: experimental
keywords: [enum, struct, backing, rawValue, metadata]
category: type-system
---

# Enum Struct Backing

## Documentation

Enums can use struct literals as backing values. Each case carries compile-time constant struct metadata accessible via `.rawValue`. At runtime, the enum is stored as an ordinal (i64) — the struct is reconstructed from constant data on `.rawValue` access.

```text
type Meta
	export let latency int(0 to 50)
end 'Meta'

enum Instruction
	add = Meta{latency: 1}
	mul = Meta{latency: 3}
end 'Instruction'

let op = Instruction.mul
let lat = op.rawValue.latency  // 3
```

All cases must use the same struct type. Struct field values must be compile-time integer, float, or boolean constants, or nested struct literals.

## Tests

### Basic rawValue field access

<!-- test: struct-backing-basic -->
```maxon
typealias Latency = int(0 to 50)

type Meta
	export let value Latency
end 'Meta'

enum TestOp
	add = Meta{value: 1}
	mul = Meta{value: 3}
end 'TestOp'

function main() returns ExitCode
	let op = TestOp.mul
	return op.rawValue.value
end 'main'
```
```exitcode
3
```

### Multiple struct fields

<!-- test: struct-backing-multi-field -->
```maxon
typealias Latency = int(0 to 100)
typealias Throughput = int(0 to 10)

type OpInfo
	export let latency Latency
	export let throughput Throughput
end 'OpInfo'

enum Instruction
	add = OpInfo{latency: 1, throughput: 1}
	mul = OpInfo{latency: 3, throughput: 2}
	div = OpInfo{latency: 40, throughput: 1}
end 'Instruction'

function main() returns ExitCode
	let op = Instruction.div
	return op.rawValue.latency
end 'main'
```
```exitcode
40
```

### Ordinal access on struct-backed enum

<!-- test: struct-backing-ordinal -->
```maxon
typealias Weight = int(0 to 100)

type Info
	export let weight Weight
end 'Info'

enum Priority
	low = Info{weight: 1}
	medium = Info{weight: 5}
	high = Info{weight: 10}
end 'Priority'

function main() returns ExitCode
	let p = Priority.high
	return p.ordinal
end 'main'
```
```exitcode
2
```

### Name access on struct-backed enum

<!-- test: struct-backing-name -->
```maxon
typealias Cost = int(0 to 100)

type Metadata
	export let cost Cost
end 'Metadata'

enum Op
	read = Metadata{cost: 1}
	write = Metadata{cost: 5}
end 'Op'

function main() returns ExitCode
	let op = Op.write
	if op.name == "write" 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Match on struct-backed enum

<!-- test: struct-backing-match -->
```maxon
typealias Latency = int(0 to 50)

type Meta
	export let latency Latency
end 'Meta'

enum TestOp
	add = Meta{latency: 1}
	sub = Meta{latency: 1}
	mul = Meta{latency: 3}
end 'TestOp'

function main() returns ExitCode
	let op = TestOp.sub
	return match op 'dispatch'
		add gives 10
		sub gives 20
		mul gives 30
	end 'dispatch'
end 'main'
```
```exitcode
20
```

### Throughput from second field

<!-- test: struct-backing-second-field -->
```maxon
typealias Latency = int(0 to 100)
typealias Throughput = int(0 to 10)

type OpInfo
	export let latency Latency
	export let throughput Throughput
end 'OpInfo'

enum Instruction
	add = OpInfo{latency: 1, throughput: 2}
	mul = OpInfo{latency: 3, throughput: 1}
end 'Instruction'

function main() returns ExitCode
	let op = Instruction.add
	return op.rawValue.throughput
end 'main'
```
```exitcode
2
```

### Error: mixed backing types

<!-- test: error.struct-backing-mixed -->
```maxon
typealias Value = int(0 to 100)

type Meta
	export let value Value
end 'Meta'

enum Mixed
	a = Meta{value: 1}
	b = 42
end 'Mixed'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/enum-struct-backing/error.struct-backing-mixed.test:10:2: raw value type mismatch: 'expected Meta, got int'
```

### Error: fromRawValue blocked

<!-- test: error.struct-backing-fromRawValue -->
```maxon
typealias Value = int(0 to 100)

type Meta
	export let value Value
end 'Meta'

enum TestOp
	add = Meta{value: 1}
end 'TestOp'

function main() returns ExitCode
	let op = try TestOp.fromRawValue(1) otherwise TestOp.add
	return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enum-struct-backing/error.struct-backing-fromRawValue.test:13:15: unknown enum case: 'fromRawValue'
```
