---
feature: array-append-managed-elements
status: stable
keywords: [array, append, concat, managed, refcount, use-after-free]
category: memory
---
# Array Append Must Incref Managed Elements

## Documentation

When `Array.append` copies elements from one array to another via `managed.concat()`,
managed elements (structs, unions, strings) must have their reference counts incremented.
The current implementation uses a raw `memcpy` which copies heap pointers without adjusting
refcounts. When the source array is later freed, its destructor decrements each element's
refcount — potentially freeing elements that the destination array still references.

## Tests

<!-- test: append-struct-source-freed -->
### Append structs with managed fields, source freed before access
The helper function creates a source array and appends it into dest. When the
helper returns, the source array is freed. If concat didn't incref, the
elements in dest are now dangling pointers.
```maxon
typealias Integer = int(i64.min to i64.max)

export type Item
    export var name String
    export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function appendFromHelper(dest ItemArray)
    var src = ItemArray{}
    src.push(Item{name: "hello from source that is long enough", value: 10})
    src.push(Item{name: "second item from source long enough", value: 20})
    src.push(Item{name: "third item from source long enough", value: 30})
    dest.append(src)
    // src is freed when this function returns
end 'appendFromHelper'

function main() returns ExitCode
    var dest = ItemArray{}
    dest.push(Item{name: "dest item that is long enough for heap", value: 1})
    appendFromHelper(dest)

    // Source array is freed. dest should still have valid elements.
    if dest.count() != 4 'badCount'
        return 99
    end 'badCount'

    let item = try dest.get(1) otherwise Item{name: "", value: 0}
    return item.value
end 'main'
```
```exitcode
10
```

<!-- test: append-union-source-freed -->
### Append unions, source freed before access
```maxon
typealias Integer = int(i64.min to i64.max)

export union Op
    add(value Integer)
    sub(value Integer)
    nop
end 'Op'

typealias OpArray = Array with Op

function appendOps(dest OpArray)
    var src = OpArray{}
    src.push(Op.add(10))
    src.push(Op.sub(20))
    src.push(Op.add(30))
    dest.append(src)
end 'appendOps'

function main() returns ExitCode
    var dest = OpArray{}
    dest.push(Op.nop)
    appendOps(dest)

    if dest.count() != 4 'badCount'
        return 99
    end 'badCount'

    let op = try dest.get(1) otherwise Op.nop
    match op 'check'
        add(v) then return v
        sub(_) then return 98
        nop then return 97
    end 'check'
end 'main'
```
```exitcode
10
```

<!-- test: merge-modules-source-freed -->
### Merge pattern: source module freed, access merged elements
Mirrors the self-hosted compiler pattern. A helper merges a parsed module
into an accumulator. The parsed module is freed when the helper returns.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export type Func
    export var name String
    export var body IntArray
end 'Func'

typealias FuncArray = Array with Func

export type Module
    export var functions FuncArray
end 'Module'

function createModule() returns Module
    return Module{functions: FuncArray{}}
end 'createModule'

function parseAndMerge(dest Module, name String)
    let source = createModule()
    source.functions.push(Func{name: name, body: IntArray{}})
    dest.functions.append(source.functions)
    // source is freed when this function returns
end 'parseAndMerge'

function main() returns ExitCode
    var allModule = createModule()
    parseAndMerge(allModule, name: "func_a_with_long_name_for_heap")
    parseAndMerge(allModule, name: "func_b_with_long_name_for_heap")

    if allModule.functions.count() != 2 'badCount'
        return 99
    end 'badCount'

    let first = try allModule.functions.get(0) otherwise Func{name: "", body: IntArray{}}
    if first.name == "func_a_with_long_name_for_heap" 'correct'
        return 0
    end 'correct'
    return 1
end 'main'
```
```exitcode
0
```
