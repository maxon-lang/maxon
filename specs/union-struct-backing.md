---
feature: union-struct-backing
status: experimental
keywords: [union, struct, backing, rawValue, metadata, associated values]
category: type-system
---

# Union Struct Backing

## Documentation

Unions can use struct literals as backing values, combining associated values (runtime data) with compile-time metadata. Each case carries both runtime-destructured payloads and constant struct metadata accessible via `UnionType.caseName`.

```text
type OpMeta
	export let latency int(0 to 50)
end 'OpMeta'

typealias ID = int(i64.min to i64.max)

union Instruction
	add(dest ID, src ID) = OpMeta{latency: 1}
	mul(dest ID, src ID) = OpMeta{latency: 3}
	nop = OpMeta{latency: 0}
end 'Instruction'

// Compile-time metadata access:
let lat = Instruction.mul.latency  // 3

// Runtime associated value access:
let op = Instruction.add(1, src: 2)
match op 'handle'
	add(d, s) then ...
	mul(d, s) then ...
	nop then ...
end 'handle'
```

All cases must use the same struct type. Struct field values must be compile-time integer, float, or boolean constants.

## Tests

### Basic union with struct backing and associated values

<!-- test: union-backing-basic -->
```maxon
typealias Latency = int(0 to 50)
typealias ID = int(i64.min to i64.max)

type OpMeta
	export let latency Latency

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'OpMeta'

union TestOp
	add(dest ID, src ID) = OpMeta.create(1)
	mul(dest ID, src ID) = OpMeta.create(3)
	nop = OpMeta.create(0)
end 'TestOp'

function main() returns ExitCode
	let op = TestOp.add(10, src: 20)
	match op 'handle'
		add(d, s) then return d + s
		mul(d, s) then return d * s
		nop then return 0
	end 'handle'
end 'main'
```
```exitcode
30
```

### Compile-time metadata access on union cases

<!-- test: union-backing-metadata -->
```maxon
typealias Latency = int(0 to 50)
typealias ID = int(i64.min to i64.max)

type OpMeta
	export let latency Latency

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'OpMeta'

union TestOp
	add(dest ID, src ID) = OpMeta.create(1)
	mul(dest ID, src ID) = OpMeta.create(3)
	nop = OpMeta.create(0)
end 'TestOp'

function main() returns ExitCode
	return TestOp.mul.latency
end 'main'
```
```exitcode
3
```

### Match with mixed cases (some with associated values, some without)

<!-- test: union-backing-mixed-cases -->
```maxon
typealias Cost = int(0 to 100)
typealias ID = int(i64.min to i64.max)

type CostMeta
	export let cost Cost

	static function create(cost Cost) returns Self
		return Self{cost: cost}
	end 'create'
end 'CostMeta'

union Action
	load(dest ID) = CostMeta.create(4)
	store(src ID) = CostMeta.create(3)
	nop = CostMeta.create(0)
end 'Action'

function main() returns ExitCode
	let op = Action.nop
	match op 'handle'
		load(d) then return d
		store(s) then return s
		nop then return 1
	end 'handle'
end 'main'
```
```exitcode
1
```

### Union backing with multiple struct fields

<!-- test: union-backing-multi-field -->
```maxon
typealias Flag = int(0 to 1)
typealias Latency = int(0 to 50)
typealias ID = int(i64.min to i64.max)

type DetailMeta
	export let latency Latency
	export let isMemory Flag

	static function create(latency Latency, isMemory Flag) returns Self
		return Self{latency: latency, isMemory: isMemory}
	end 'create'
end 'DetailMeta'

union MemOp
	load(dest ID, offset ID) = DetailMeta.create(4, isMemory: 1)
	store(src ID, offset ID) = DetailMeta.create(3, isMemory: 1)
	add(dest ID, src ID) = DetailMeta.create(1, isMemory: 0)
end 'MemOp'

function main() returns ExitCode
	let memFlag = MemOp.load.isMemory
	let aluFlag = MemOp.add.isMemory
	return memFlag + aluFlag
end 'main'
```
```exitcode
1
```
