---
feature: struct-union-array-grow
status: stable
keywords: [struct, union, array, grow, memory-management]
category: types
---
# Struct with Union Field and Array Growth

## Documentation

Structs containing both a union (enum with associated values) field and an array field should work correctly when the array grows via push operations.

## Tests

<!-- test: struct-union-field-array-push -->
Struct with union field and array that grows.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export union Op
    add(value Integer)
    sub(value Integer)
end 'Op'

export type Block
    export var id Integer
    export var ops IntArray
    export var terminator Op
end 'Block'

function main() returns ExitCode
    let b = Block{id: 0, ops: IntArray{}, terminator: Op.add(42)}
    b.ops.push(1)
    b.ops.push(2)
    b.ops.push(3)
    return b.ops.count()
end 'main'
```
```exitcode
3
```

<!-- test: struct-union-field-nested-array-push -->
Struct with nested union and array that grows past initial capacity.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export union Instruction
    load(slot Integer)
    store(slot Integer)
    nop
end 'Instruction'

export type Function
    export var name Integer
    export var body IntArray
    export var terminator Instruction
    export var hasTerminator bool
end 'Function'

function main() returns ExitCode
    let f = Function{name: 1, body: IntArray{}, terminator: Instruction.load(99), hasTerminator: true}
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

<!-- test: array-of-struct-with-union-field -->
Array of structs containing union fields, with array growth.
```maxon
typealias Integer = int(i64.min to i64.max)

export union Tag
    number(n Integer)
    text(len Integer)
end 'Tag'

export type Entry
    export var id Integer
    export var tag Tag
end 'Entry'

typealias EntryArray = Array with Entry

function main() returns ExitCode
    var entries = EntryArray{}
    var i = 0
    while i < 10 'fill'
        let e = Entry{id: i, tag: Tag.number(i * 10)}
        entries.push(e)
        i = i + 1
    end 'fill'
    let last = try entries.get(9) otherwise Entry{id: 0, tag: Tag.number(0)}
    match last.tag 'check'
        number(n) then return n
        text(len) then return 0
    end 'check'
    return 0
end 'main'
```
```exitcode
90
```

<!-- test: nested-struct-union-array-grow -->
Deeply nested struct with union field and multiple arrays.
```maxon
typealias Integer = int(i64.min to i64.max)

export union CfOp
    br(target Integer)
    condBr(cond Integer)
end 'CfOp'

export union MlirOp
    cf(op CfOp)
    arith(value Integer)
end 'MlirOp'

typealias MlirOpArray = Array with MlirOp

export type MlirBlock
    export var id Integer
    export var ops MlirOpArray
    export var terminator MlirOp
    export var hasTerminator bool
end 'MlirBlock'

typealias DepArray = Array with Integer

export type Database
    export var block MlirBlock
    export var deps DepArray
end 'Database'

function main() returns ExitCode
    let block = MlirBlock{id: 0, ops: MlirOpArray{}, terminator: MlirOp.cf(CfOp.br(0)), hasTerminator: false}
    let db = Database{block: block, deps: DepArray{}}
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
    export var revision Integer
    export var items IntArray
    export var names StringArray
    export var extra IntArray
end 'Inner'

export type Outer
    export var inner Inner
    export var tag Op
    export var data IntArray
end 'Outer'

function main() returns ExitCode
    let inner = Inner{revision: 0, items: IntArray{}, names: StringArray{}, extra: IntArray{}}
    let outer = Outer{inner: inner, tag: Op.add(1), data: IntArray{}}
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
end 'Inner'

export type Outer
    export var first Inner
    export var second Inner
    export var deps IntArray
end 'Outer'

function makeOuter() returns Outer
    let shared = Inner{data: IntArray{}, value: 42}
    return Outer{first: shared, second: shared, deps: IntArray{}}
end 'makeOuter'

function main() returns ExitCode
    let o = makeOuter()
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
Large returned struct with many array fields and nested structs with union fields.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias StringArray = Array with String

export union CfOp
    br(target Integer)
end 'CfOp'

export union MlirOp
    cf(op CfOp)
end 'MlirOp'

typealias OpArray = Array with MlirOp

export type Block
    export var id Integer
    export var label String
    export var ops OpArray
    export var terminator MlirOp
    export var hasTerminator bool
end 'Block'

export type Region
    export var blocks IntArray
    export var nextBlockId Integer
end 'Region'

export type Func
    export var name String
    export var returnType String
    export var region Region
end 'Func'

typealias FuncArray = Array with Func

export type Module
    export var functions FuncArray
end 'Module'

export type ModuleMemo
    export var value Module
    export var computedAt Integer
    export var verifiedAt Integer
end 'ModuleMemo'

export type CodeResult
    export var code IntArray
    export var offset Integer
end 'CodeResult'

export type CodeMemo
    export var value CodeResult
    export var computedAt Integer
    export var verifiedAt Integer
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
end 'Project'

function createProject(rootPath String) returns Project
    let emptyModuleMemo = ModuleMemo{value: Module{functions: FuncArray{}}, computedAt: 0, verifiedAt: 0}
    let emptyCodeMemo = CodeMemo{value: CodeResult{code: IntArray{}, offset: 0}, computedAt: 0, verifiedAt: 0}
    let db = QueryDatabase{
        currentRevision: 0,
        sourcePaths: StringArray{},
        allModuleCache: emptyModuleMemo,
        allMidCache: emptyModuleMemo,
        codeCache: emptyCodeMemo,
        dependencies: IntArray{},
        activeQueryStack: IntArray{},
        tokenHits: 0, tokenMisses: 0,
        parseHits: 0, parseMisses: 0
    }
    let emptyBlock = Block{id: 0, label: "", ops: OpArray{}, terminator: MlirOp.cf(CfOp.br(0)), hasTerminator: false}
    let emptyFunc = Func{name: "", returnType: "", region: Region{blocks: IntArray{}, nextBlockId: 0}}
    let emptyModule = Module{functions: FuncArray{}}
    return Project{
        db: db,
        dbInitialized: false,
        nextValueId: 0,
        parserVarNames: StringArray{},
        parserVarSlots: IntArray{},
        parserNextSlot: 0,
        rootPath: rootPath,
        isSingleFile: false,
        currentBlock: emptyBlock,
        currentFunction: emptyFunc,
        currentModule: emptyModule
    }
end 'createProject'

function main() returns ExitCode
    let p = createProject("test")
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

<!-- test: array-of-struct-with-string-union -->
Array of structs where struct has union fields with String associated values.
```maxon
export union QueryKey
    sourceFile(path String)
    tokens(path String)
    allModule
    codeResult
end 'QueryKey'

export type Dependency
    export var dependent QueryKey
    export var dependency QueryKey
end 'Dependency'

typealias DependencyArray = Array with Dependency

function main() returns ExitCode
    var deps = DependencyArray{}
    let d1 = Dependency{dependent: QueryKey.allModule, dependency: QueryKey.sourceFile("test.maxon")}
    deps.push(d1)
    let d2 = Dependency{dependent: QueryKey.codeResult, dependency: QueryKey.tokens("test.maxon")}
    deps.push(d2)
    let d3 = Dependency{dependent: QueryKey.allModule, dependency: QueryKey.sourceFile("other.maxon")}
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
end 'Inner'

export type Outer
    export var inner Inner
    export var initialized bool
end 'Outer'

function initOuter(o Outer)
    if not o.initialized 'init'
        o.inner = Inner{items: IntArray{}, value: 42}
        o.initialized = true
    end 'init'
end 'initOuter'

function main() returns ExitCode
    let o = Outer{inner: Inner{items: IntArray{}, value: 0}, initialized: false}
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
end 'Module'

export type Memo
    export var value Module
    export var rev Integer
end 'Memo'

export type Database
    export var memo Memo
    export var deps IntArray
end 'Database'

export type Project
    export var db Database
    export var ready bool
end 'Project'

function createModule() returns Module
    return Module{items: IntArray{}}
end 'createModule'

function initProject(p Project)
    if not p.ready 'init'
        p.db = Database{
            memo: Memo{value: createModule(), rev: 0},
            deps: IntArray{}
        }
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
    let p = Project{db: Database{memo: Memo{value: Module{items: IntArray{}}, rev: 0}, deps: IntArray{}}, ready: false}
    initProject(p)
    return useProject(p)
end 'main'
```
```exitcode
3
```

<!-- test: returned-struct-with-union-and-arrays -->
Returned struct with union field and arrays that grow.
```maxon
typealias Integer = int(i64.min to i64.max)

export union CfOp
    br(target Integer)
end 'CfOp'

export union MlirOp
    cf(op CfOp)
end 'MlirOp'

typealias OpArray = Array with MlirOp

export type Block
    export var id Integer
    export var ops OpArray
    export var terminator MlirOp
    export var hasTerminator bool
end 'Block'

typealias DepArray = Array with Integer

export type QueryDatabase
    export var revision Integer
    export var deps DepArray
    export var extra DepArray
end 'QueryDatabase'

export type Project
    export var db QueryDatabase
    export var block Block
end 'Project'

function createProject() returns Project
    let block = Block{id: 0, ops: OpArray{}, terminator: MlirOp.cf(CfOp.br(0)), hasTerminator: false}
    let db = QueryDatabase{revision: 0, deps: DepArray{}, extra: DepArray{}}
    return Project{db: db, block: block}
end 'createProject'

function main() returns ExitCode
    let p = createProject()
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
