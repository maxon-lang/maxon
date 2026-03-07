---
feature: array-slice-managed-elements
status: stable
keywords: [array, slice, managed, refcount, use-after-free]
category: memory
---
# Array Slice Must Incref Managed Elements

## Documentation

When `Array.slice` copies elements via `managed.slice()`, managed elements (structs, unions)
must have their reference counts incremented. The current implementation uses a raw `memcpy`
which copies heap pointers without adjusting refcounts. When the source array is later freed,
its destructor decrements each element's refcount — potentially freeing elements that the
slice still references.

## Tests

<!-- test: slice-struct-source-freed -->
### Slice of struct array, source freed before access
```maxon
typealias Integer = int(i64.min to i64.max)

export type Item
    export var name String
    export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function makeSlice() returns ItemArray
    var src = ItemArray{}
    src.push(Item{name: "first item long enough for heap allocation", value: 10})
    src.push(Item{name: "second item long enough for heap allocation", value: 20})
    src.push(Item{name: "third item long enough for heap allocation", value: 30})
    return src.slice(0, endIndex: 2)
    // src is freed when this function returns
end 'makeSlice'

function main() returns ExitCode
    let sliced = makeSlice()

    if sliced.count() != 2 'badCount'
        return 99
    end 'badCount'

    let item = try sliced.get(0) otherwise Item{name: "", value: 0}
    return item.value
end 'main'
```
```exitcode
10
```

<!-- test: slice-union-source-freed -->
### Slice of union array, source freed before access
```maxon
typealias Integer = int(i64.min to i64.max)

export union Op
    add(value Integer)
    sub(value Integer)
    nop
end 'Op'

typealias OpArray = Array with Op

function makeSlice() returns OpArray
    var src = OpArray{}
    src.push(Op.add(10))
    src.push(Op.sub(20))
    src.push(Op.add(30))
    return src.slice(1, endIndex: 3)
end 'makeSlice'

function main() returns ExitCode
    let sliced = makeSlice()

    if sliced.count() != 2 'badCount'
        return 99
    end 'badCount'

    let op = try sliced.get(0) otherwise Op.nop
    match op 'check'
        sub(v) then return v
        add(_) then return 98
        nop then return 97
    end 'check'
    return 0
end 'main'
```
```exitcode
20
```
