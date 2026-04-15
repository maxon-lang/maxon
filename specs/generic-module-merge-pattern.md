---
feature: generic-module-merge-pattern
status: experimental
keywords: [generics, interface, extension, merge, inner-typealias, list-iteration, monomorphization]
category: type-system
---

# Generic Module with Interface Conformance and Merge Extension

## Documentation

Exercises the full IrModule pattern from the self-hosted compiler: a generic type with an inner typealias, an interface with associated types, an extension method that iterates source data, and List iteration on struct fields.

## Tests

### Generic module merge with ops and list iteration

<!-- test: generic-module-merge -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

type Item
	export var value Integer

	export static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'

	export static function clone(src Self) returns Self
		return Self{value: src.value}
	end 'clone'
end 'Item'

typealias ItemArray = Array with Item
typealias IntList = List with Integer

type Block
	export var id Integer
	export var opRefs IntList

	export static function create(id Integer) returns Self
		return Self{id: id, opRefs: IntList.create()}
	end 'create'
end 'Block'

typealias BlockArray = Array with Block

interface Mergeable uses Func
	function opCount() returns Count
	function appendOps(source Self)
	function blockCount() returns Count
	function funcCount() returns Count
	function getFunc(index Count) returns Func
	function pushFunc(func Func)
	function cloneFunc(src Func) returns Func
end 'Mergeable'

export extension Mergeable

	export function merge(source Self)
		self.appendOps(source)

		for i in 0 upto source.funcCount() 'eachFunc'
			let srcFunc = source.getFunc(i)
			let func = self.cloneFunc(srcFunc)
			self.pushFunc(func)
		end 'eachFunc'
	end 'merge'

end 'Mergeable'

type GenModule uses Op implements Mergeable with Item
	typealias OpArray = Array with Op

	export var functions ItemArray
	export var blocks BlockArray
	export var ops OpArray

	export static function create() returns Self
		return Self{
			functions: ItemArray.create(),
			blocks: BlockArray.create(),
			ops: OpArray.create()
		}
	end 'create'

	export function opCount() returns Count
		return self.ops.count()
	end 'opCount'

	export function appendOps(source Self)
		self.ops.append(source.ops)
	end 'appendOps'

	export function blockCount() returns Count
		return self.blocks.count()
	end 'blockCount'

	export function funcCount() returns Count
		return self.functions.count()
	end 'funcCount'

	export function getFunc(index Count) returns Item
		return try self.functions.get(index) otherwise 'unreachable'
			panic("getFunc: out of bounds")
		end 'unreachable'
	end 'getFunc'

	export function pushFunc(func Item)
		self.functions.push(func)
	end 'pushFunc'

	export function cloneFunc(src Item) returns Item
		return Item.clone(src)
	end 'cloneFunc'

	export function appendOp(block Block, op Op)
		let idx = self.ops.count()
		self.ops.push(op)
		block.opRefs.append(idx)
	end 'appendOp'
end 'GenModule'

typealias StringModule = GenModule with String

function main() returns ExitCode
	// Build module A with ops and a block
	var modA = StringModule.create()
	var blockA = Block.create(0)
	modA.appendOp(blockA, op: "add")
	modA.appendOp(blockA, op: "mul")
	modA.blocks.push(blockA)
	modA.functions.push(Item.create(1))

	// Build module B with ops and a block
	var modB = StringModule.create()
	var blockB = Block.create(0)
	modB.appendOp(blockB, op: "sub")
	modB.blocks.push(blockB)
	modB.functions.push(Item.create(2))

	// Verify pre-merge counts
	let preOps = modA.opCount()
	let preFuncs = modA.funcCount()

	// Merge B into A
	modA.merge(modB)

	// Verify post-merge counts
	let postOps = modA.opCount()
	let postFuncs = modA.funcCount()

	// Iterate the block's opRefs list to verify list integrity
	var opRefSum = 0
	for opRef in blockA.opRefs 'eachRef'
		opRefSum = opRefSum + opRef
	end 'eachRef'

	print("pre: ops={preOps} funcs={preFuncs}\n")
	print("post: ops={postOps} funcs={postFuncs}\n")
	print("opRefSum={opRefSum}\n")

	if postOps == 3 and postFuncs == 2 and opRefSum == 1 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
pre: ops=2 funcs=1
post: ops=3 funcs=2
opRefSum=1
```

### Two specializations of generic type with inner array typealias

<!-- test: dual-specialize-inner-array -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

type GenContainer uses Op
	typealias OpArray = Array with Op

	export var ops OpArray

	export static function create() returns Self
		return Self{ops: OpArray.create()}
	end 'create'

	export function push(op Op)
		self.ops.push(op)
	end 'push'

	export function count() returns Count
		return self.ops.count()
	end 'count'
end 'GenContainer'

typealias IntContainer = GenContainer with Integer
typealias StringContainer = GenContainer with String

function main() returns ExitCode
	var ic = IntContainer.create()
	ic.push(10)
	ic.push(20)
	ic.push(30)

	var sc = StringContainer.create()
	sc.push("hello")
	sc.push("world")

	var intSum = 0
	for val in ic.ops 'intLoop'
		intSum = intSum + val
	end 'intLoop'

	var strCount = 0
	for val in sc.ops 'strLoop'
		strCount = strCount + 1
	end 'strLoop'

	print("ints={ic.count()} sum={intSum}\n")
	print("strings={sc.count()} count={strCount}\n")

	if ic.count() == 3 and intSum == 60 and sc.count() == 2 and strCount == 2 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
ints=3 sum=60
strings=2 count=2
```
