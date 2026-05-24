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

export union Op
		add(value Integer)
		sub(value Integer)
end 'Op'

export type Block
		export var id as Integer
		export var ops as IntArray
		export var terminator as Op

		static function create(id Integer, ops IntArray, terminator Op) returns Self
			return Self{id: id, ops: ops, terminator: terminator}
		end 'create'
end 'Block'

function main() returns ExitCode
		var b = Block.create(0, ops: IntArray.create(), terminator: Op.add(42))
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

export union Instruction
		load(slot Integer)
		store(slot Integer)
		nop
end 'Instruction'

export type Function
		export var name as Integer
		export var body as IntArray
		export var terminator as Instruction
		export var hasTerminator as bool

		static function create(name Integer, body IntArray, terminator Instruction, hasTerminator bool) returns Self
			return Self{name: name, body: body, terminator: terminator, hasTerminator: hasTerminator}
		end 'create'
end 'Function'

function main() returns ExitCode
		var f = Function.create(1, body: IntArray.create(), terminator: Instruction.load(99), hasTerminator: true)
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

export union Tag
		number(n Integer)
		text(len Integer)
end 'Tag'

export type Entry
		export var id as Integer
		export var tag as Tag

		static function create(id Integer, tag Tag) returns Self
			return Self{id: id, tag: tag}
		end 'create'
end 'Entry'

typealias EntryArray = Array with Entry

function main() returns ExitCode
		var entries = EntryArray.create()
		var i = 0
		while i < 10 'fill'
				let e = Entry.create(i, tag: Tag.number(i * 10))
				entries.push(e)
				i = i + 1
		end 'fill'
		let last = try entries.get(9) otherwise Entry.create(0, tag: Tag.number(0))
		match last.tag 'check'
				number(n) then return n
				text then return 0
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

export union CfOp
		br(target Integer)
		condBr(cond Integer)
end 'CfOp'

export union IrOp
		cf(op CfOp)
		arith(value Integer)
end 'IrOp'

typealias IrOpArray = Array with IrOp

export type IrBlock
		export var id as Integer
		export var ops as IrOpArray
		export var terminator as IrOp
		export var hasTerminator as bool

		static function create(id Integer, ops IrOpArray, terminator IrOp, hasTerminator bool) returns Self
			return Self{id: id, ops: ops, terminator: terminator, hasTerminator: hasTerminator}
		end 'create'
end 'IrBlock'

typealias DepArray = Array with Integer

export type Database
		export var block as IrBlock
		export var deps as DepArray

		static function create(block IrBlock, deps DepArray) returns Self
			return Self{block: block, deps: deps}
		end 'create'
end 'Database'

function main() returns ExitCode
		let block = IrBlock.create(0, ops: IrOpArray.create(), terminator: IrOp.cf(CfOp.br(0)), hasTerminator: false)
		var db = Database.create(block, deps: DepArray.create())
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

export union Op
		add(value Integer)
		nop
end 'Op'

export type Inner
		export var revision as Integer
		export var items as IntArray
		export var names as StringArray
		export var extra as IntArray

		static function create(revision Integer, items IntArray, names StringArray, extra IntArray) returns Self
			return Self{revision: revision, items: items, names: names, extra: extra}
		end 'create'
end 'Inner'

export type Outer
		export var inner as Inner
		export var tag as Op
		export var data as IntArray

		static function create(inner Inner, tag Op, data IntArray) returns Self
			return Self{inner: inner, tag: tag, data: data}
		end 'create'
end 'Outer'

function main() returns ExitCode
		let inner = Inner.create(0, items: IntArray.create(), names: StringArray.create(), extra: IntArray.create())
		var outer = Outer.create(inner, tag: Op.add(1), data: IntArray.create())
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
		export var data as IntArray
		export var value as Integer

		static function create(data IntArray, value Integer) returns Self
			return Self{data: data, value: value}
		end 'create'
end 'Inner'

export type Outer
		export var first as Inner
		export var second as Inner
		export var deps as IntArray

		static function create(first Inner, second Inner, deps IntArray) returns Self
			return Self{first: first, second: second, deps: deps}
		end 'create'
end 'Outer'

function makeOuter() returns Outer
		let shared = Inner.create(IntArray.create(), value: 42)
		return Outer.create(shared, second: shared, deps: IntArray.create())
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

export union CfOp
		br(target Integer)
end 'CfOp'

export union IrOp
		cf(op CfOp)
end 'IrOp'

typealias OpArray = Array with IrOp

export type Block
		export var id as Integer
		export var label as String
		export var ops as OpArray
		export var terminator as IrOp
		export var hasTerminator as bool

		static function create(id Integer, label String, ops OpArray, terminator IrOp, hasTerminator bool) returns Self
			return Self{id: id, label: label, ops: ops, terminator: terminator, hasTerminator: hasTerminator}
		end 'create'
end 'Block'

export type Region
		export var blocks as IntArray
		export var nextBlockId as Integer

		static function create(blocks IntArray, nextBlockId Integer) returns Self
			return Self{blocks: blocks, nextBlockId: nextBlockId}
		end 'create'
end 'Region'

export type Func
		export var name as String
		export var returnType as String
		export var region as Region

		static function create(name String, returnType String, region Region) returns Self
			return Self{name: name, returnType: returnType, region: region}
		end 'create'
end 'Func'

typealias FuncArray = Array with Func

export type Module
		export var functions as FuncArray

		static function create(functions FuncArray) returns Self
			return Self{functions: functions}
		end 'create'
end 'Module'

export type ModuleMemo
		export var value as Module
		export var computedAt as Integer
		export var verifiedAt as Integer

		static function create(value Module, computedAt Integer, verifiedAt Integer) returns Self
			return Self{value: value, computedAt: computedAt, verifiedAt: verifiedAt}
		end 'create'
end 'ModuleMemo'

export type CodeResult
		export var code as IntArray
		export var offset as Integer

		static function create(code IntArray, offset Integer) returns Self
			return Self{code: code, offset: offset}
		end 'create'
end 'CodeResult'

export type CodeMemo
		export var value as CodeResult
		export var computedAt as Integer
		export var verifiedAt as Integer

		static function create(value CodeResult, computedAt Integer, verifiedAt Integer) returns Self
			return Self{value: value, computedAt: computedAt, verifiedAt: verifiedAt}
		end 'create'
end 'CodeMemo'

export type QueryDatabase
		export var currentRevision as Integer
		export var sourcePaths as StringArray
		export var allModuleCache as ModuleMemo
		export var allMidCache as ModuleMemo
		export var codeCache as CodeMemo
		export var dependencies as IntArray
		export var activeQueryStack as IntArray
		export var tokenHits as Integer
		export var tokenMisses as Integer
		export var parseHits as Integer
		export var parseMisses as Integer

		static function create(currentRevision Integer, sourcePaths StringArray, allModuleCache ModuleMemo, allMidCache ModuleMemo, codeCache CodeMemo, dependencies IntArray, activeQueryStack IntArray, tokenHits Integer, tokenMisses Integer, parseHits Integer, parseMisses Integer) returns Self
			return Self{currentRevision: currentRevision, sourcePaths: sourcePaths, allModuleCache: allModuleCache, allMidCache: allMidCache, codeCache: codeCache, dependencies: dependencies, activeQueryStack: activeQueryStack, tokenHits: tokenHits, tokenMisses: tokenMisses, parseHits: parseHits, parseMisses: parseMisses}
		end 'create'
end 'QueryDatabase'

export type Project
		export var db as QueryDatabase
		export var dbInitialized as bool
		export var nextValueId as Integer
		export var parserVarNames as StringArray
		export var parserVarSlots as IntArray
		export var parserNextSlot as Integer
		export var rootPath as String
		export var isSingleFile as bool
		export var currentBlock as Block
		export var currentFunction as Func
		export var currentModule as Module

		static function create(db QueryDatabase, dbInitialized bool, nextValueId Integer, parserVarNames StringArray, parserVarSlots IntArray, parserNextSlot Integer, rootPath String, isSingleFile bool, currentBlock Block, currentFunction Func, currentModule Module) returns Self
			return Self{db: db, dbInitialized: dbInitialized, nextValueId: nextValueId, parserVarNames: parserVarNames, parserVarSlots: parserVarSlots, parserNextSlot: parserNextSlot, rootPath: rootPath, isSingleFile: isSingleFile, currentBlock: currentBlock, currentFunction: currentFunction, currentModule: currentModule}
		end 'create'
end 'Project'

function createProject(rootPath String) returns Project
		let emptyModuleMemo = ModuleMemo.create(Module.create(FuncArray.create()), computedAt: 0, verifiedAt: 0)
		let emptyCodeMemo = CodeMemo.create(CodeResult.create(IntArray.create(), offset: 0), computedAt: 0, verifiedAt: 0)
		let db = QueryDatabase.create(0, sourcePaths: StringArray.create(), allModuleCache: emptyModuleMemo, allMidCache: emptyModuleMemo, codeCache: emptyCodeMemo, dependencies: IntArray.create(), activeQueryStack: IntArray.create(), tokenHits: 0, tokenMisses: 0, parseHits: 0, parseMisses: 0)
		let emptyBlock = Block.create(0, label: "", ops: OpArray.create(), terminator: IrOp.cf(CfOp.br(0)), hasTerminator: false)
		let emptyFunc = Func.create("", returnType: "", region: Region.create(IntArray.create(), nextBlockId: 0))
		let emptyModule = Module.create(FuncArray.create())
		return Project.create(db, dbInitialized: false, nextValueId: 0, parserVarNames: StringArray.create(), parserVarSlots: IntArray.create(), parserNextSlot: 0, rootPath: rootPath, isSingleFile: false, currentBlock: emptyBlock, currentFunction: emptyFunc, currentModule: emptyModule)
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
export union QueryKey
		sourceFile(path String)
		tokens(path String)
		allModule
		codeResult
end 'QueryKey'

export type Dependency
		export var dependent as QueryKey
		export var dependency as QueryKey

		static function create(dependent QueryKey, dependency QueryKey) returns Self
			return Self{dependent: dependent, dependency: dependency}
		end 'create'
end 'Dependency'

typealias DependencyArray = Array with Dependency

function main() returns ExitCode
		var deps = DependencyArray.create()
		let d1 = Dependency.create(QueryKey.allModule, dependency: QueryKey.sourceFile("test.maxon"))
		deps.push(d1)
		let d2 = Dependency.create(QueryKey.codeResult, dependency: QueryKey.tokens("test.maxon"))
		deps.push(d2)
		let d3 = Dependency.create(QueryKey.allModule, dependency: QueryKey.sourceFile("other.maxon"))
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
		export var items as IntArray
		export var value as Integer

		static function create(items IntArray, value Integer) returns Self
			return Self{items: items, value: value}
		end 'create'
end 'Inner'

export type Outer
		export var inner as Inner
		export var initialized as bool

		static function create(inner Inner, initialized bool) returns Self
			return Self{inner: inner, initialized: initialized}
		end 'create'
end 'Outer'

function initOuter(o Outer)
		if not o.initialized 'init'
				o.inner = Inner.create(IntArray.create(), value: 42)
				o.initialized = true
		end 'init'
end 'initOuter'

function main() returns ExitCode
		var o = Outer.create(Inner.create(IntArray.create(), value: 0), initialized: false)
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
		export var items as IntArray

		static function create(items IntArray) returns Self
			return Self{items: items}
		end 'create'
end 'Module'

export type Memo
		export var value as Module
		export var rev as Integer

		static function create(value Module, rev Integer) returns Self
			return Self{value: value, rev: rev}
		end 'create'
end 'Memo'

export type Database
		export var memo as Memo
		export var deps as IntArray

		static function create(memo Memo, deps IntArray) returns Self
			return Self{memo: memo, deps: deps}
		end 'create'
end 'Database'

export type Project
		export var db as Database
		export var ready as bool

		static function create(db Database, ready bool) returns Self
			return Self{db: db, ready: ready}
		end 'create'
end 'Project'

function createModule() returns Module
		return Module.create(IntArray.create())
end 'createModule'

function initProject(p Project)
		if not p.ready 'init'
				p.db = Database.create(Memo.create(createModule(), rev: 0), deps: IntArray.create())
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
		var p = Project.create(Database.create(Memo.create(Module.create(IntArray.create()), rev: 0), deps: IntArray.create()), ready: false)
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

export union CfOp
		br(target Integer)
end 'CfOp'

export union IrOp
		cf(op CfOp)
end 'IrOp'

typealias OpArray = Array with IrOp

export type Block
		export var id as Integer
		export var ops as OpArray
		export var terminator as IrOp
		export var hasTerminator as bool

		static function create(id Integer, ops OpArray, terminator IrOp, hasTerminator bool) returns Self
			return Self{id: id, ops: ops, terminator: terminator, hasTerminator: hasTerminator}
		end 'create'
end 'Block'

typealias DepArray = Array with Integer

export type QueryDatabase
		export var revision as Integer
		export var deps as DepArray
		export var extra as DepArray

		static function create(revision Integer, deps DepArray, extra DepArray) returns Self
			return Self{revision: revision, deps: deps, extra: extra}
		end 'create'
end 'QueryDatabase'

export type Project
		export var db as QueryDatabase
		export var block as Block

		static function create(db QueryDatabase, block Block) returns Self
			return Self{db: db, block: block}
		end 'create'
end 'Project'

function createProject() returns Project
		let block = Block.create(0, ops: OpArray.create(), terminator: IrOp.cf(CfOp.br(0)), hasTerminator: false)
		let db = QueryDatabase.create(0, deps: DepArray.create(), extra: DepArray.create())
		return Project.create(db, block: block)
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
