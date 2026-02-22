---
feature: struct-array-get-refcount
status: experimental
keywords: [array, struct, get, refcount, memory]
category: memory-safety
---

# Struct Array Get Refcount

## Documentation

When retrieving struct elements from an array via `get()`, the returned struct pointer must be reference-counted correctly. The array retains its reference to the element, and the caller receives a borrowed reference that must be incref'd to prevent premature deallocation.

## Tests

<!-- test: struct-array-get-survives-scope -->
Struct elements retrieved from an array in a loop inside a function must survive after the function returns.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
    export var value Integer
    export var next Integer
end 'Node'

typealias NodeArray = Array with Node

type List
    export var nodes NodeArray
    export var head Integer

    function pushFront(value Integer)
        var node = Node{value: value, next: self.head}
        self.nodes.push(node)
        self.head = self.nodes.count() - 1
    end 'pushFront'

    function walk()
        var current = self.head
        while current != -1 'w'
            var node = try self.nodes.get(current) otherwise Node{value: 0, next: -1}
            current = node.next
        end 'w'
    end 'walk'
end 'List'

function main() returns ExitCode
    var list = List{nodes: NodeArray{}, head: -1}
    list.pushFront(10)
    list.pushFront(20)
    list.walk()
    list.pushFront(30)
    var n1 = try list.nodes.get(1) otherwise Node{value: 0, next: -1}
    return n1.value
end 'main'
```
```exitcode
20
```

<!-- test: struct-array-get-loop-function -->
Struct elements in array survive after being read in a loop inside a standalone function.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
    export var a Integer
    export var b Integer
end 'Pair'

typealias PairArray = Array with Pair

function sumAll(pairs PairArray) returns Integer
    var total = 0
    for pair in pairs 'loop'
        total = total + pair.a + pair.b
    end 'loop'
    return total
end 'sumAll'

function main() returns ExitCode
    var pairs = PairArray{}
    pairs.push(Pair{a: 1, b: 2})
    pairs.push(Pair{a: 3, b: 4})
    pairs.push(Pair{a: 5, b: 6})
    let sum = sumAll(pairs)
    // After sumAll, elements should still be valid
    var p1 = try pairs.get(1) otherwise Pair{a: 0, b: 0}
    if sum == 21 'ok'
        return p1.a + p1.b
    end 'ok'
    return 0
end 'main'
```
```exitcode
7
```

<!-- test: struct-array-get-multiple-reads -->
Multiple reads of the same struct array element in a function don't corrupt data.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
    export var id Integer
end 'Item'

typealias ItemArray = Array with Item

function readTwice(items ItemArray) returns Integer
    var a = try items.get(0) otherwise Item{id: 0}
    var b = try items.get(0) otherwise Item{id: 0}
    return a.id + b.id
end 'readTwice'

function main() returns ExitCode
    var items = ItemArray{}
    items.push(Item{id: 21})
    let result = readTwice(items)
    var check = try items.get(0) otherwise Item{id: 0}
    return check.id + result
end 'main'
```
```exitcode
63
```
