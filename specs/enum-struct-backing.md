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

	static function create(value Latency) returns Self
		return Self{value: value}
	end 'create'
end 'Meta'

enum TestOp
	add = Meta.create(value: 1)
	mul = Meta.create(value: 3)
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

	static function create(latency Latency, throughput Throughput) returns Self
		return Self{latency: latency, throughput: throughput}
	end 'create'
end 'OpInfo'

enum Instruction
	add = OpInfo.create(latency: 1, throughput: 1)
	mul = OpInfo.create(latency: 3, throughput: 2)
	div = OpInfo.create(latency: 40, throughput: 1)
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

	static function create(weight Weight) returns Self
		return Self{weight: weight}
	end 'create'
end 'Info'

enum Priority
	low = Info.create(weight: 1)
	medium = Info.create(weight: 5)
	high = Info.create(weight: 10)
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

	static function create(cost Cost) returns Self
		return Self{cost: cost}
	end 'create'
end 'Metadata'

enum Op
	read = Metadata.create(cost: 1)
	write = Metadata.create(cost: 5)
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

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'Meta'

enum TestOp
	add = Meta.create(latency: 1)
	sub = Meta.create(latency: 1)
	mul = Meta.create(latency: 3)
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

	static function create(latency Latency, throughput Throughput) returns Self
		return Self{latency: latency, throughput: throughput}
	end 'create'
end 'OpInfo'

enum Instruction
	add = OpInfo.create(latency: 1, throughput: 2)
	mul = OpInfo.create(latency: 3, throughput: 1)
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

	static function create(value Value) returns Self
		return Self{value: value}
	end 'create'
end 'Meta'

enum Mixed
	a = Meta.create(value: 1)
	b = 42
end 'Mixed'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/enum-struct-backing/error.struct-backing-mixed.test:14:2: raw value type mismatch: 'expected Meta, got int'
```

### Error: fromRawValue blocked

<!-- test: error.struct-backing-fromRawValue -->
```maxon
typealias Value = int(0 to 100)

type Meta
	export let value Value

	static function create(value Value) returns Self
		return Self{value: value}
	end 'create'
end 'Meta'

enum TestOp
	add = Meta.create(value: 1)
end 'TestOp'

function main() returns ExitCode
	let op = try TestOp.fromRawValue(1) otherwise TestOp.add
	return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enum-struct-backing/error.struct-backing-fromRawValue.test:17:15: unknown enum case: 'fromRawValue'
```
