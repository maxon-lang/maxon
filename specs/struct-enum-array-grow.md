---
feature: struct-enum-array-grow
status: stable
keywords: [struct, enum, array, grow, memory-management]
category: types
---
# Struct with Enum Field and Array Growth

## Documentation

Structs containing both an enum (enum with associated values) field and an array field should work correctly when the array grows via push operations.

## Tests

<!-- test: struct-enum-field-array-push -->
Struct with enum field and array that grows.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export enum Op
		add(value Integer)
		sub(value Integer)
end 'Op'

export type Block
		export var id Integer
		export var ops IntArray
		export var terminator Op

		static function create(id Integer, ops IntArray, terminator Op) returns Self
			return Self{id: id, ops: ops, terminator: terminator}
		end 'create'
end 'Block'

function main() returns ExitCode
		var b = Block.create(id: 0, ops: IntArray.create(), terminator: Op.add(42))
		b.ops.push(1)
		b.ops.push(2)
		b.ops.push(3)
		return b.ops.count()
end 'main'
```
```exitcode
3
```

<!-- test: struct-enum-field-nested-array-push -->
Struct with nested enum and array that grows past initial capacity.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export enum Instruction
		load(slot Integer)
		store(slot Integer)
		nop
end 'Instruction'

export type Function
		export var name Integer
		export var body IntArray
		export var terminator Instruction
		export var hasTerminator bool

		static function create(name Integer, body IntArray, terminator Instruction, hasTerminator bool) returns Self
			return Self{name: name, body: body, terminator: terminator, hasTerminator: hasTerminator}
		end 'create'
end 'Function'

function main() returns ExitCode
		var f = Function.create(name: 1, body: IntArray.create(), terminator: Instruction.load(99), hasTerminator: true)
		var i = 0
		while i < 20 'fill'
				f.body.push(i)
				i = i + 1
		end 'fill'
		return f.body.count()
end 'main'
```
```exitcode
20
```

<!-- test: array-of-struct-with-enum-field -->
Array of structs containing enum fields, with array growth.
```maxon
typealias Integer = int(i64.min to i64.max)

export enum Tag
		number(n Integer)
		text(len Integer)
end 'Tag'

export type Entry
		export var id Integer
		export var tag Tag

		static function create(id Integer, tag Tag) returns Self
			return Self{id: id, tag: tag}
		end 'create'
end 'Entry'

typealias EntryArray = Array with Entry

function main() returns ExitCode
		var entries = EntryArray.create()
		var i = 0
		while i < 10 'fill'
				let e = Entry.create(id: i, tag: Tag.number(i * 10))
				entries.push(e)
				i = i + 1
		end 'fill'
		let last = try entries.get(9) otherwise Entry.create(id: 0, tag: Tag.number(0))
		match last.tag 'check'
				number(n) then return n
				text(_) then return 0
		end 'check'
end 'main'
```
```exitcode
90
```

<!-- test: nested-struct-enum-array-grow -->
Deeply nested struct with enum field and multiple arrays.
```maxon
typealias Integer = int(i64.min to i64.max)

export enum CfOp
		br(target Integer)
		condBr(cond Integer)
end 'CfOp'

export enum MlirOp
		cf(op CfOp)
		arith(value Integer)
end 'MlirOp'

typealias MlirOpArray = Array with MlirOp

export type MlirBlock
		export var id Integer
		export var ops MlirOpArray
		export var terminator MlirOp
		export var hasTerminator bool

		static function create(id Integer, ops MlirOpArray, terminator MlirOp, hasTerminator bool) returns Self
			return Self{id: id, ops: ops, terminator: terminator, hasTerminator: hasTerminator}
		end 'create'
end 'MlirBlock'

typealias DepArray = Array with Integer

export type Database
		export var block MlirBlock
		export var deps DepArray

		static function create(block MlirBlock, deps DepArray) returns Self
			return Self{block: block, deps: deps}
		end 'create'
end 'Database'

function main() returns ExitCode
		let block = MlirBlock.create(id: 0, ops: MlirOpArray.create(), terminator: MlirOp.cf(CfOp.br(0)), hasTerminator: false)
		var db = Database.create(block: block, deps: DepArray.create())
		db.deps.push(1)
		db.deps.push(2)
		db.deps.push(3)
		db.deps.push(4)
		db.deps.push(5)
		return db.deps.count()
end 'main'
```
```exitcode
5
```

<!-- test: deeply-nested-struct-many-arrays -->
Struct containing another struct with many array fields, push into arrays.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias StringArray = Array with String

export enum Op
		add(value Integer)
		nop
end 'Op'

export type Inner
		export var revision Integer
		export var items IntArray
		export var names StringArray
		export var extra IntArray

		static function create(revision Integer, items IntArray, names StringArray, extra IntArray) returns Self
			return Self{revision: revision, items: items, names: names, extra: extra}
		end 'create'
end 'Inner'

export type Outer
		export var inner Inner
		export var tag Op
		export var data IntArray

		static function create(inner Inner, tag Op, data IntArray) returns Self
			return Self{inner: inner, tag: tag, data: data}
		end 'create'
end 'Outer'

function main() returns ExitCode
		let inner = Inner.create(revision: 0, items: IntArray.create(), names: StringArray.create(), extra: IntArray.create())
		var outer = Outer.create(inner: inner, tag: Op.add(1), data: IntArray.create())
		outer.inner.items.push(10)
		outer.inner.items.push(20)
		outer.inner.items.push(30)
		outer.data.push(100)
		return outer.inner.items.count() + outer.data.count()
end 'main'
```
```exitcode
4
```

<!-- test: shared-nested-struct-in-literal -->
Same nested struct reference used for two fields of another struct.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export type Inner
		export var data IntArray
		export var value Integer

		static function create(data IntArray, value Integer) returns Self
			return Self{data: data, value: value}
		end 'create'
end 'Inner'

export type Outer
		export var first Inner
		export var second Inner
		export var deps IntArray

		static function create(first Inner, second Inner, deps IntArray) returns Self
			return Self{first: first, second: second, deps: deps}
		end 'create'
end 'Outer'

function makeOuter() returns Outer
		let shared = Inner.create(data: IntArray.create(), value: 42)
		return Outer.create(first: shared, second: shared, deps: IntArray.create())
end 'makeOuter'

function main() returns ExitCode
		var o = makeOuter()
		o.deps.push(1)
		o.deps.push(2)
		o.deps.push(3)
		return o.deps.count()
end 'main'
```
```exitcode
3
```

<!-- test: large-returned-struct-many-arrays -->
Large returned struct with many array fields and nested structs with enum fields.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias StringArray = Array with String

export enum CfOp
		br(target Integer)
end 'CfOp'

export enum MlirOp
		cf(op CfOp)
end 'MlirOp'

typealias OpArray = Array with MlirOp

export type Block
		export var id Integer
		export var label String
		export var ops OpArray
		export var terminator MlirOp
		export var hasTerminator bool

		static function create(id Integer, label String, ops OpArray, terminator MlirOp, hasTerminator bool) returns Self
			return Self{id: id, label: label, ops: ops, terminator: terminator, hasTerminator: hasTerminator}
		end 'create'
end 'Block'

export type Region
		export var blocks IntArray
		export var nextBlockId Integer

		static function create(blocks IntArray, nextBlockId Integer) returns Self
			return Self{blocks: blocks, nextBlockId: nextBlockId}
		end 'create'
end 'Region'

export type Func
		export var name String
		export var returnType String
		export var region Region

		static function create(name String, returnType String, region Region) returns Self
			return Self{name: name, returnType: returnType, region: region}
		end 'create'
end 'Func'

typealias FuncArray = Array with Func

export type Module
		export var functions FuncArray

		static function create(functions FuncArray) returns Self
			return Self{functions: functions}
		end 'create'
end 'Module'

export type ModuleMemo
		export var value Module
		export var computedAt Integer
		export var verifiedAt Integer

		static function create(value Module, computedAt Integer, verifiedAt Integer) returns Self
			return Self{value: value, computedAt: computedAt, verifiedAt: verifiedAt}
		end 'create'
end 'ModuleMemo'

export type CodeResult
		export var code IntArray
		export var offset Integer

		static function create(code IntArray, offset Integer) returns Self
			return Self{code: code, offset: offset}
		end 'create'
end 'CodeResult'

export type CodeMemo
		export var value CodeResult
		export var computedAt Integer
		export var verifiedAt Integer

		static function create(value CodeResult, computedAt Integer, verifiedAt Integer) returns Self
			return Self{value: value, computedAt: computedAt, verifiedAt: verifiedAt}
		end 'create'
end 'CodeMemo'

export type QueryDatabase
		export var currentRevision Integer
		export var sourcePaths StringArray
		export var allModuleCache ModuleMemo
		export var allMidCache ModuleMemo
		export var codeCache CodeMemo
		export var dependencies IntArray
		export var activeQueryStack IntArray
		export var tokenHits Integer
		export var tokenMisses Integer
		export var parseHits Integer
		export var parseMisses Integer

		static function create(currentRevision Integer, sourcePaths StringArray, allModuleCache ModuleMemo, allMidCache ModuleMemo, codeCache CodeMemo, dependencies IntArray, activeQueryStack IntArray, tokenHits Integer, tokenMisses Integer, parseHits Integer, parseMisses Integer) returns Self
			return Self{currentRevision: currentRevision, sourcePaths: sourcePaths, allModuleCache: allModuleCache, allMidCache: allMidCache, codeCache: codeCache, dependencies: dependencies, activeQueryStack: activeQueryStack, tokenHits: tokenHits, tokenMisses: tokenMisses, parseHits: parseHits, parseMisses: parseMisses}
		end 'create'
end 'QueryDatabase'

export type Project
		export var db QueryDatabase
		export var dbInitialized bool
		export var nextValueId Integer
		export var parserVarNames StringArray
		export var parserVarSlots IntArray
		export var parserNextSlot Integer
		export var rootPath String
		export var isSingleFile bool
		export var currentBlock Block
		export var currentFunction Func
		export var currentModule Module

		static function create(db QueryDatabase, dbInitialized bool, nextValueId Integer, parserVarNames StringArray, parserVarSlots IntArray, parserNextSlot Integer, rootPath String, isSingleFile bool, currentBlock Block, currentFunction Func, currentModule Module) returns Self
			return Self{db: db, dbInitialized: dbInitialized, nextValueId: nextValueId, parserVarNames: parserVarNames, parserVarSlots: parserVarSlots, parserNextSlot: parserNextSlot, rootPath: rootPath, isSingleFile: isSingleFile, currentBlock: currentBlock, currentFunction: currentFunction, currentModule: currentModule}
		end 'create'
end 'Project'

function createProject(rootPath String) returns Project
		let emptyModuleMemo = ModuleMemo.create(value: Module.create(functions: FuncArray.create()), computedAt: 0, verifiedAt: 0)
		let emptyCodeMemo = CodeMemo.create(value: CodeResult.create(code: IntArray.create(), offset: 0), computedAt: 0, verifiedAt: 0)
		let db = QueryDatabase.create(currentRevision: 0, sourcePaths: StringArray.create(), allModuleCache: emptyModuleMemo, allMidCache: emptyModuleMemo, codeCache: emptyCodeMemo, dependencies: IntArray.create(), activeQueryStack: IntArray.create(), tokenHits: 0, tokenMisses: 0, parseHits: 0, parseMisses: 0)
		let emptyBlock = Block.create(id: 0, label: "", ops: OpArray.create(), terminator: MlirOp.cf(CfOp.br(0)), hasTerminator: false)
		let emptyFunc = Func.create(name: "", returnType: "", region: Region.create(blocks: IntArray.create(), nextBlockId: 0))
		let emptyModule = Module.create(functions: FuncArray.create())
		return Project.create(db: db, dbInitialized: false, nextValueId: 0, parserVarNames: StringArray.create(), parserVarSlots: IntArray.create(), parserNextSlot: 0, rootPath: rootPath, isSingleFile: false, currentBlock: emptyBlock, currentFunction: emptyFunc, currentModule: emptyModule)
end 'createProject'

function main() returns ExitCode
		var p = createProject("test")
		p.db.dependencies.push(1)
		p.db.dependencies.push(2)
		p.db.dependencies.push(3)
		p.db.dependencies.push(4)
		p.db.dependencies.push(5)
		p.db.activeQueryStack.push(10)
		return p.db.dependencies.count() + p.db.activeQueryStack.count()
end 'main'
```
```exitcode
6
```

<!-- test: array-of-struct-with-string-enum -->
Array of structs where struct has enum fields with String associated values.
```maxon
export enum QueryKey
		sourceFile(path String)
		tokens(path String)
		allModule
		codeResult
end 'QueryKey'

export type Dependency
		export var dependent QueryKey
		export var dependency QueryKey

		static function create(dependent QueryKey, dependency QueryKey) returns Self
			return Self{dependent: dependent, dependency: dependency}
		end 'create'
end 'Dependency'

typealias DependencyArray = Array with Dependency

function main() returns ExitCode
		var deps = DependencyArray.create()
		let d1 = Dependency.create(dependent: QueryKey.allModule, dependency: QueryKey.sourceFile("test.maxon"))
		deps.push(d1)
		let d2 = Dependency.create(dependent: QueryKey.codeResult, dependency: QueryKey.tokens("test.maxon"))
		deps.push(d2)
		let d3 = Dependency.create(dependent: QueryKey.allModule, dependency: QueryKey.sourceFile("other.maxon"))
		deps.push(d3)
		return deps.count()
end 'main'
```
```exitcode
3
```

<!-- test: field-assign-struct-in-if-scope -->
Struct allocated in if-block scope and assigned to parameter field must survive scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export type Inner
		export var items IntArray
		export var value Integer

		static function create(items IntArray, value Integer) returns Self
			return Self{items: items, value: value}
		end 'create'
end 'Inner'

export type Outer
		export var inner Inner
		export var initialized bool

		static function create(inner Inner, initialized bool) returns Self
			return Self{inner: inner, initialized: initialized}
		end 'create'
end 'Outer'

function initOuter(o Outer)
		if not o.initialized 'init'
				o.inner = Inner.create(items: IntArray.create(), value: 42)
				o.initialized = true
		end 'init'
end 'initOuter'

function main() returns ExitCode
		var o = Outer.create(inner: Inner.create(items: IntArray.create(), value: 0), initialized: false)
		initOuter(o)
		o.inner.items.push(1)
		o.inner.items.push(2)
		o.inner.items.push(3)
		return o.inner.items.count()
end 'main'
```
```exitcode
3
```

<!-- test: field-assign-with-func-call-in-struct -->
Struct literal with function call result as field, assigned to parameter field.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export type Module
		export var items IntArray

		static function create(items IntArray) returns Self
			return Self{items: items}
		end 'create'
end 'Module'

export type Memo
		export var value Module
		export var rev Integer

		static function create(value Module, rev Integer) returns Self
			return Self{value: value, rev: rev}
		end 'create'
end 'Memo'

export type Database
		export var memo Memo
		export var deps IntArray

		static function create(memo Memo, deps IntArray) returns Self
			return Self{memo: memo, deps: deps}
		end 'create'
end 'Database'

export type Project
		export var db Database
		export var ready bool

		static function create(db Database, ready bool) returns Self
			return Self{db: db, ready: ready}
		end 'create'
end 'Project'

function createModule() returns Module
		return Module.create(items: IntArray.create())
end 'createModule'

function initProject(p Project)
		if not p.ready 'init'
				p.db = Database.create(memo: Memo.create(value: createModule(), rev: 0), deps: IntArray.create())
				p.ready = true
		end 'init'
end 'initProject'

function useProject(p Project) returns Integer
		p.db.deps.push(1)
		p.db.deps.push(2)
		p.db.deps.push(3)
		return p.db.deps.count()
end 'useProject'

function main() returns ExitCode
		var p = Project.create(db: Database.create(memo: Memo.create(value: Module.create(items: IntArray.create()), rev: 0), deps: IntArray.create()), ready: false)
		initProject(p)
		return useProject(p)
end 'main'
```
```exitcode
3
```

<!-- test: returned-struct-with-enum-and-arrays -->
Returned struct with enum field and arrays that grow.
```maxon
typealias Integer = int(i64.min to i64.max)

export enum CfOp
		br(target Integer)
end 'CfOp'

export enum MlirOp
		cf(op CfOp)
end 'MlirOp'

typealias OpArray = Array with MlirOp

export type Block
		export var id Integer
		export var ops OpArray
		export var terminator MlirOp
		export var hasTerminator bool

		static function create(id Integer, ops OpArray, terminator MlirOp, hasTerminator bool) returns Self
			return Self{id: id, ops: ops, terminator: terminator, hasTerminator: hasTerminator}
		end 'create'
end 'Block'

typealias DepArray = Array with Integer

export type QueryDatabase
		export var revision Integer
		export var deps DepArray
		export var extra DepArray

		static function create(revision Integer, deps DepArray, extra DepArray) returns Self
			return Self{revision: revision, deps: deps, extra: extra}
		end 'create'
end 'QueryDatabase'

export type Project
		export var db QueryDatabase
		export var block Block

		static function create(db QueryDatabase, block Block) returns Self
			return Self{db: db, block: block}
		end 'create'
end 'Project'

function createProject() returns Project
		let block = Block.create(id: 0, ops: OpArray.create(), terminator: MlirOp.cf(CfOp.br(0)), hasTerminator: false)
		let db = QueryDatabase.create(revision: 0, deps: DepArray.create(), extra: DepArray.create())
		return Project.create(db: db, block: block)
end 'createProject'

function main() returns ExitCode
		var p = createProject()
		p.db.deps.push(1)
		p.db.deps.push(2)
		p.db.deps.push(3)
		p.db.deps.push(4)
		p.db.deps.push(5)
		return p.db.deps.count()
end 'main'
```
```exitcode
5
```
