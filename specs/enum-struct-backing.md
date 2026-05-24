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
	export let latency as int(0 to 50)
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
	export let value as Latency

	static function create(value Latency) returns Self
		return Self{value: value}
	end 'create'
end 'Meta'

enum TestOp
	add = Meta.create(1)
	mul = Meta.create(3)
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
	export let latency as Latency
	export let throughput as Throughput

	static function create(latency Latency, throughput Throughput) returns Self
		return Self{latency: latency, throughput: throughput}
	end 'create'
end 'OpInfo'

enum Instruction
	add = OpInfo.create(1, throughput: 1)
	mul = OpInfo.create(3, throughput: 2)
	div = OpInfo.create(40, throughput: 1)
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
	export let weight as Weight

	static function create(weight Weight) returns Self
		return Self{weight: weight}
	end 'create'
end 'Info'

enum Priority
	low = Info.create(1)
	medium = Info.create(5)
	high = Info.create(10)
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
	export let cost as Cost

	static function create(cost Cost) returns Self
		return Self{cost: cost}
	end 'create'
end 'Metadata'

enum Op
	read = Metadata.create(1)
	write = Metadata.create(5)
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
	export let latency as Latency

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'Meta'

enum TestOp
	add = Meta.create(1)
	sub = Meta.create(1)
	mul = Meta.create(3)
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
	export let latency as Latency
	export let throughput as Throughput

	static function create(latency Latency, throughput Throughput) returns Self
		return Self{latency: latency, throughput: throughput}
	end 'create'
end 'OpInfo'

enum Instruction
	add = OpInfo.create(1, throughput: 2)
	mul = OpInfo.create(3, throughput: 1)
end 'Instruction'

function main() returns ExitCode
	let op = Instruction.add
	return op.rawValue.throughput
end 'main'
```
```exitcode
2
```

### Enum member reference as struct field value

<!-- test: struct-backing-enum-field -->
```maxon
typealias Cost = int(0 to 100)

enum Priority
	low
	medium
	high
end 'Priority'

type TaskInfo
	export let priority as Priority
	export let cost as Cost
end 'TaskInfo'

enum Task
	quick = TaskInfo{priority: Priority.low, cost: 1}
	normal = TaskInfo{priority: Priority.medium, cost: 5}
	heavy = TaskInfo{priority: Priority.high, cost: 10}
end 'Task'

function main() returns ExitCode
	let t = Task.heavy
	return t.rawValue.priority.ordinal
end 'main'
```
```exitcode
2
```

### Enum member reference in factory call

<!-- test: struct-backing-enum-factory -->
```maxon
typealias Level = int(0 to 100)

enum Mode
	fast
	slow
end 'Mode'

type Config
	export let mode as Mode
	export let level as Level

	static function create(mode Mode, level Level) returns Self
		return Self{mode: mode, level: level}
	end 'create'
end 'Config'

enum Setting
	turbo = Config.create(Mode.fast, level: 10)
	eco = Config.create(Mode.slow, level: 3)
end 'Setting'

function main() returns ExitCode
	let s = Setting.turbo
	return s.rawValue.mode.ordinal
end 'main'
```
```exitcode
0
```

### Error: mixed backing types

<!-- test: error.struct-backing-mixed -->
```maxon
typealias Value = int(0 to 100)

type Meta
	export let value as Value

	static function create(value Value) returns Self
		return Self{value: value}
	end 'create'
end 'Meta'

enum Mixed
	a = Meta.create(1)
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
	export let value as Value

	static function create(value Value) returns Self
		return Self{value: value}
	end 'create'
end 'Meta'

enum TestOp
	add = Meta.create(1)
end 'TestOp'

function main() returns ExitCode
	let op = try TestOp.fromRawValue(1) otherwise TestOp.add
	return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enum-struct-backing/error.struct-backing-fromRawValue.test:17:15: unknown enum case: 'fromRawValue'
```
