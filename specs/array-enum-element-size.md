---
feature: array-enum-element-size
status: stable
keywords: [array, enum, element-size, push, grow, memory-management]
category: memory
---
# Array of Enum: Element Size and Push Correctness

## Documentation

When an array holds elements of an enum type (with associated values), the backing `__ManagedMemory` must have `element_size = 8` (heap pointer size). If `element_size` is incorrectly set to 0, every push computes `buffer + index * 0 = buffer`, always overwriting slot 0, and grow computes `newCap * 0 = 0` bytes, never actually growing the buffer. The array appears to have the right `count()` but only the last-pushed element survives; all earlier elements were decref'd and freed. Cascading cleanup then crashes on the stale pointers.

## Tests

<!-- test: enum-array-push-count -->
### Push to array of enum preserves count
Basic verification that pushing multiple enum values gives the correct count.
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
		add(value Integer)
		sub(value Integer)
		nop
end 'Op'

typealias OpArray = Array with Op

function main() returns ExitCode
		var ops = OpArray.create()
		ops.push(Op.add(1))
		ops.push(Op.sub(2))
		ops.push(Op.nop)
		ops.push(Op.add(3))
		ops.push(Op.sub(4))
		return ops.count()
end 'main'
```
```exitcode
5
```

<!-- test: enum-array-push-get -->
### Push then get retrieves correct elements
Verifies that earlier pushed elements are still accessible (not overwritten).
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
		add(value Integer)
		sub(value Integer)
		nop
end 'Op'

typealias OpArray = Array with Op

function main() returns ExitCode
		var ops = OpArray.create()
		ops.push(Op.add(10))
		ops.push(Op.sub(20))
		ops.push(Op.add(30))

		let first = try ops.get(0) otherwise Op.nop
		match first 'check'
				add(v) then return v
				sub then return 99
				nop then return 98
		end 'check'
end 'main'
```
```exitcode
10
```

<!-- test: enum-array-push-get-last -->
### Get last element from enum array
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
		add(value Integer)
		sub(value Integer)
		nop
end 'Op'

typealias OpArray = Array with Op

function main() returns ExitCode
		var ops = OpArray.create()
		ops.push(Op.add(10))
		ops.push(Op.sub(20))
		ops.push(Op.add(42))

		let last = try ops.get(2) otherwise Op.nop
		match last 'check'
				add(v) then return v
				sub then return 99
				nop then return 98
		end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: nested-enum-array-push-get -->
### Nested enum (enum wrapping enum) in array
This mirrors the IrOp pattern from the self-hosted compiler.
```maxon
typealias Integer = int(i64.min to i64.max)

union CfOp
		br(target Integer)
		condBr(cond Integer)
end 'CfOp'

union IrOp
		cf(op CfOp)
		arith(value Integer)
end 'IrOp'

typealias IrOpArray = Array with IrOp

function checkFirst(ops IrOpArray) returns Integer
		let first = try ops.get(0) otherwise IrOp.arith(0)
		match first 'checkFirst'
				arith(v) then return v
				cf then return 99
		end 'checkFirst'
end 'checkFirst'

function checkMid(ops IrOpArray) returns Integer
		let mid = try ops.get(2) otherwise IrOp.arith(0)
		match mid 'checkMid'
				cf then return 1
				arith then return 0
		end 'checkMid'
end 'checkMid'

function main() returns ExitCode
		var ops = IrOpArray.create()
		ops.push(IrOp.arith(10))
		ops.push(IrOp.arith(20))
		ops.push(IrOp.cf(CfOp.br(99)))
		ops.push(IrOp.arith(30))
		ops.push(IrOp.arith(40))

		if ops.count() != 5 'badCount'
				return 1
		end 'badCount'

		// First element must still be arith(10), not overwritten
		let v = checkFirst(ops)
		if v != 10 'wrong'
				return 2
		end 'wrong'

		// Middle element must be cf variant
		let m = checkMid(ops)
		if m != 1 'wrongMid'
				return 3
		end 'wrongMid'

		return 0
end 'main'
```
```exitcode
0
```

<!-- test: enum-array-in-struct-cascade-free -->
### Struct with enum array field: cascade free must not crash
When a struct holding an enum array is freed, the cascade must correctly
walk the array elements. If element_size is 0, the array has stale pointers
at indices > 0 and the cascade crashes.
```maxon
typealias Integer = int(i64.min to i64.max)

union CfOp
		br(target Integer)
end 'CfOp'

union IrOp
		cf(op CfOp)
		arith(value Integer)
end 'IrOp'

typealias IrOpArray = Array with IrOp

type Block
		export var id Integer
		export var ops IrOpArray
		export var terminator IrOp

		static function create(id Integer, ops IrOpArray, terminator IrOp) returns Self
			return Self{id: id, ops: ops, terminator: terminator}
		end 'create'
end 'Block'

function makeBlock() returns Block
		var b = Block.create(id: 1, ops: IrOpArray.create(), terminator: IrOp.cf(CfOp.br(0)))
		b.ops.push(IrOp.arith(10))
		b.ops.push(IrOp.arith(20))
		b.ops.push(IrOp.arith(30))
		b.ops.push(IrOp.arith(40))
		b.ops.push(IrOp.arith(50))
		return b
end 'makeBlock'

function main() returns ExitCode
		// makeBlock returns a block; the local goes out of scope and is freed.
		// The cascade must correctly free 5 ops in the array.
		let b = makeBlock()
		let first = try b.ops.get(0) otherwise IrOp.arith(0)
		match first 'check'
				arith(v) then return v
				cf then return 99
		end 'check'
end 'main'
```
```exitcode
10
```
