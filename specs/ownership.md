---
feature: ownership
status: experimental
keywords: [refcount, memory, ownership, scope, cycle, destructor]
category: memory-safety
---

# Ownership & Memory Management

## Documentation

### Refcount-Based Ownership

Every heap-allocated struct has a reference count. Assigning to a variable increments the count (incref); when a variable goes out of scope the count is decremented (decref). When the count reaches zero the object is freed immediately. No garbage collector or scope frame is involved.

**Rules summary:**

- `var p = Point{x: 1}` — allocates with rc=1.
- `let q = p` — same object, rc=2.
- `p = Point{x: 2}` — old Point decref'd (rc→1 since q holds it), new Point rc=1.
- Block exit — decref every variable declared in that block.
- Return — skips the returned variable's decref (ownership transfers to caller).
- Struct field assignment — decrefs old field value, increfs new.
- Container push — increfs the element; container holds the reference.
- Container remove / chain remove — transfers the container's reference to the caller (no extra incref).
- Container overwrite (`set`) — decrefs old element, increfs new.
- `clear()` / container freed at scope exit — decrefs all elements.

### Cycle Detection

Reference cycles are a **compile error**. A type may not reference itself directly or indirectly through struct fields, union associated values, or container element types. The compiler reports `E4014` with the cycle path.

## Tests

<!-- test: local-struct-freed -->
Struct created and used inside a function; no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function testLocal()
  var p = Point{x: 1, y: 2}
  print("{p.x}\n")
end 'testLocal'

function main() returns ExitCode
  testLocal()
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: local-alias -->
Two variables alias the same object; freed once when both go out of scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function testAlias()
  var p = Point{x: 3, y: 4}
  let q = p
  print("{q.x}\n")
  print("{p.y}\n")
end 'testAlias'

function main() returns ExitCode
  testAlias()
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3
4
```

<!-- test: reassignment-frees-old -->
Reassigning a var decrefs the old object; new object lives until block exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
  export var value Integer
end 'Box'

function testReassign()
  var x = Box{value: 1}
  x = Box{value: 2}
  print("{x.value}\n")
end 'testReassign'

function main() returns ExitCode
  testReassign()
  return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: nested-block-frees -->
Struct created inside an if-block is freed when that block exits.
```maxon
typealias Integer = int(i64.min to i64.max)

type Widget
  export var id Integer
end 'Widget'

function testNestedBlock(cond bool)
  var result = 0
  if cond 'check'
    var w = Widget{id: 42}
    result = w.id
  end 'check'
  print("{result}\n")
end 'testNestedBlock'

function main() returns ExitCode
  testNestedBlock(true)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: return-local-struct -->
Returning a struct transfers ownership to the caller without an extra incref/decref.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function makePoint(x Integer, y Integer) returns Point
  var p = Point{x: x, y: y}
  return p
end 'makePoint'

function main() returns ExitCode
  var p = makePoint(10, y: 20)
  print("{p.x}\n")
  print("{p.y}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
```

<!-- test: return-container-element-get -->
Returning a container element (`get`) keeps the element alive past container scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function getFirst(arr ItemArray) returns Item
  var elem = try arr.get(0) otherwise Item{value: -1}
  return elem
end 'getFirst'

function main() returns ExitCode
  var arr = ItemArray{}
  arr.push(Item{value: 99})
  var result = getFirst(arr)
  print("{result.value}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: return-container-element-remove -->
Removing and returning an element transfers its refcount to the caller.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function popFirst(arr ItemArray) returns Item throws ArrayError
  return try arr.remove(0)
end 'popFirst'

function main() returns ExitCode
  var arr = ItemArray{}
  arr.push(Item{value: 77})
  arr.push(Item{value: 88})
  var first = try popFirst(arr) otherwise 'err'
    return 99
  end 'err'
  print("{first.value}\n")
  print("{arr.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
77
1
```

<!-- test: return-struct-with-field-ref -->
Returning a struct whose field references another heap object keeps that object alive.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
  export var value Integer
end 'Inner'

type Outer
  export var inner Inner
end 'Outer'

function makeOuter(v Integer) returns Outer
  var inner = Inner{value: v}
  var outer = Outer{inner: inner}
  return outer
end 'makeOuter'

function main() returns ExitCode
  var o = makeOuter(55)
  print("{o.inner.value}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
55
```

<!-- test: return-param-borrow -->
Returning a field from a parameter; parameter outlives the call so the field stays valid.
```maxon
typealias Integer = int(i64.min to i64.max)

type Data
  export var value Integer
end 'Data'

type Wrapper
  export var data Data
end 'Wrapper'

function extractData(w Wrapper) returns Data
  return w.data
end 'extractData'

function main() returns ExitCode
  var d = Data{value: 42}
  var w = Wrapper{data: d}
  var result = extractData(w)
  print("{result.value}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: container-push-incref -->
After pushing a struct into a container, the element stays alive past the push site's scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
  export var id Integer
end 'Node'

typealias NodeArray = Array with Node

function main() returns ExitCode
  var arr = NodeArray{}
  var count = 0
  if true 'scope'
    var n = Node{id: 10}
    arr.push(n)
    count = arr.count()
  end 'scope'
  print("{count}\n")
  var elem = try arr.get(0) otherwise Node{id: -1}
  print("{elem.id}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1
10
```

<!-- test: container-remove-decref -->
Removing an element and assigning it to a var; var owns it and frees it at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
  var arr = ItemArray{}
  arr.push(Item{value: 1})
  arr.push(Item{value: 2})
  arr.push(Item{value: 3})
  var removed = try arr.remove(1) otherwise 'err'
    return 99
  end 'err'
  print("{removed.value}\n")
  print("{arr.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
2
2
```

<!-- test: container-push-remove-cycle -->
Many push/remove cycles with no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
  var arr = ItemArray{}
  var i = 0
  while i < 10 'push'
    arr.push(Item{value: i})
    i = i + 1
  end 'push'
  var total = 0
  while arr.count() > 0 'remove'
    var elem = try arr.remove(0) otherwise 'err'
      return 99
    end 'err'
    total = total + elem.value
  end 'remove'
  print("{total}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
45
```

<!-- test: container-overwrite-decrefs-old -->
Setting an element at an existing index decrefs the old element.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
  var arr = ItemArray{}
  arr.push(Item{value: 100})
  arr.set(0, value: Item{value: 200})
  var elem = try arr.get(0) otherwise Item{value: -1}
  print("{elem.value}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
200
```

<!-- test: container-freed-decrefs-elements -->
When a container goes out of scope all its elements are decref'd.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function fillArray() returns Integer
  var arr = ItemArray{}
  arr.push(Item{value: 1})
  arr.push(Item{value: 2})
  arr.push(Item{value: 3})
  return arr.count()
end 'fillArray'

function main() returns ExitCode
  var count = fillArray()
  print("{count}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: struct-field-assign-incref -->
Assigning a struct to a field increfs it; both outer and inner live until scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
  export var value Integer
end 'Inner'

type Outer
  export var inner Inner
end 'Outer'

function main() returns ExitCode
  var inner = Inner{value: 7}
  var outer = Outer{inner: inner}
  print("{outer.inner.value}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: struct-field-overwrite -->
Overwriting a struct field decrefs the old value and increfs the new value.
```maxon
typealias Integer = int(i64.min to i64.max)

type Data
  export var value Integer
end 'Data'

type Container
  export var data Data

  export function setData(newData Data)
    data = newData
  end 'setData'
end 'Container'

function main() returns ExitCode
  var old = Data{value: 10}
  var c = Container{data: old}
  c.setData(Data{value: 20})
  print("{c.data.value}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
20
```

<!-- test: chain-insert-incref -->
Inserting a struct into a chain increfs the value; the node holds the reference.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemChain = __Chain with Item

function main() returns ExitCode
  var chain = ItemChain{}
  var item = Item{value: 99}
  var node = chain.insertFirst(item)
  print("{node.value().value}\n")
  print("{chain.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
99
1
```

<!-- test: chain-remove-decref -->
Removing a node and discarding the result frees the value at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemChain = __Chain with Item

function main() returns ExitCode
  var chain = ItemChain{}
  var node = chain.insertFirst(Item{value: 50})
  chain.remove(node)
  print("{chain.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: chain-clear-decrefs-all -->
Clearing a chain decrefs all node values.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemChain = __Chain with Item

function main() returns ExitCode
  var chain = ItemChain{}
  chain.insertFirst(Item{value: 1})
  chain.insertLast(Item{value: 2})
  chain.insertLast(Item{value: 3})
  chain.clear()
  print("{chain.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: global-container-push-local -->
Pushing a local struct into a global container keeps it alive beyond the local scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

var globalArr = ItemArray{}

function pushLocal()
  var item = Item{value: 123}
  globalArr.push(item)
end 'pushLocal'

function main() returns ExitCode
  pushLocal()
  var elem = try globalArr.get(0) otherwise Item{value: -1}
  print("{elem.value}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
123
```

<!-- test: global-container-remove-frees -->
Removing from a global container transfers ownership; element freed at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

var globalArr = ItemArray{}

function main() returns ExitCode
  globalArr.push(Item{value: 10})
  globalArr.push(Item{value: 20})
  var removed = try globalArr.remove(0) otherwise 'err'
    return 99
  end 'err'
  print("{removed.value}\n")
  print("{globalArr.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
10
1
```

<!-- test: global-container-many-push-remove -->
Many push/remove cycles on a global container leave no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

var globalArr = ItemArray{}

function main() returns ExitCode
  var i = 0
  while i < 20 'push'
    globalArr.push(Item{value: i})
    i = i + 1
  end 'push'
  var total = 0
  while globalArr.count() > 0 'remove'
    var elem = try globalArr.remove(0) otherwise 'err'
      return 99
    end 'err'
    total = total + elem.value
  end 'remove'
  print("{total}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
190
```

<!-- test: if-then-block-cleanup -->
A struct created in an if-block is freed when the block exits.
```maxon
typealias Integer = int(i64.min to i64.max)

type Widget
  export var id Integer
end 'Widget'

function main() returns ExitCode
  var result = 0
  if true 'check'
    var w = Widget{id: 5}
    result = w.id
  end 'check'
  print("{result}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: if-else-both-assign -->
Both branches of an if/else assign to an outer var; the correct value survives.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
  export var value Integer
end 'Box'

function choose(flag bool) returns Integer
  var result = Box{value: 0}
  if flag 'branch'
    result = Box{value: 1}
  end 'branch'
  if flag == false 'branch2'
    result = Box{value: 2}
  end 'branch2'
  return result.value
end 'choose'

function main() returns ExitCode
  print("{choose(true)}\n")
  print("{choose(false)}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1
2
```

<!-- test: while-loop-iteration-cleanup -->
A struct created each loop iteration is freed before the next iteration begins.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
  export var val Integer
end 'Counter'

function main() returns ExitCode
  var total = 0
  var i = 0
  while i < 5 'loop'
    var c = Counter{val: i}
    total = total + c.val
    i = i + 1
  end 'loop'
  print("{total}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: for-loop-elem-borrow -->
For-loop element variable is decref'd each iteration; no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Score
  export var points Integer
end 'Score'

typealias ScoreArray = Array with Score

function main() returns ExitCode
  var scores = ScoreArray{}
  scores.push(Score{points: 10})
  scores.push(Score{points: 20})
  scores.push(Score{points: 30})
  var total = 0
  for s in scores 'loop'
    total = total + s.points
  end 'loop'
  print("{total}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
60
```

<!-- test: match-case-cleanup -->
A struct created in a match arm is freed when the arm's block exits.
```maxon
typealias Integer = int(i64.min to i64.max)

union Color
  red
  blue
end 'Color'

type Paint
  export var id Integer
end 'Paint'

function main() returns ExitCode
  var c = Color.red
  var result = 0
  var p = Paint{id: 0}
  match c 'pick'
    Color.red then p = Paint{id: 7}
    Color.blue then p = Paint{id: 0}
  end 'pick'
  result = p.id
  print("{result}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: break-cleanup -->
A struct created before a break is freed when the break exits the loop block.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
  export var val Integer
end 'Counter'

function main() returns ExitCode
  var total = 0
  var i = 0
  while i < 5 'loop'
    var c = Counter{val: i}
    if i == 3 'brk'
      total = total + c.val
      break
    end 'brk'
    total = total + c.val
    i = i + 1
  end 'loop'
  print("{total}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: continue-cleanup -->
A struct created before a continue is freed before the loop restarts.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

function main() returns ExitCode
  var total = 0
  var i = 0
  while i < 5 'loop'
    var item = Item{value: i}
    i = i + 1
    if item.value == 2 'skip'
      continue
    end 'skip'
    total = total + item.value
  end 'loop'
  // 0+1+3+4 = 8
  print("{total}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: closure-env-freed -->
The closure environment block is freed at block exit like any other struct.
```maxon
typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function main() returns ExitCode
  var offset = 5
  var result = apply(f: (n Integer) gives n + offset, x: 10)
  print("{result}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
15
```

<!-- test: closure-captures-borrow -->
A closure captures a struct variable by address; the original var owns the struct.
```maxon
typealias Integer = int(i64.min to i64.max)

type Config
  export var level Integer
end 'Config'

function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function main() returns ExitCode
  var cfg = Config{level: 3}
  var result = apply(f: (_ Integer) gives cfg.level, x: 0)
  print("{result}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: no-leak-nested-structs -->
A struct containing a struct field; both are freed with no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
  export var val Integer
end 'Inner'

type Outer
  export var child Inner
end 'Outer'

function main() returns ExitCode
  var o = Outer{child: Inner{val: 42}}
  print("{o.child.val}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: no-leak-nested-containers -->
A container holding another container; all freed with no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Row
  export var value Integer
end 'Row'

typealias RowArray = Array with Row

type Table
  export var rows RowArray
end 'Table'

typealias TableArray = Array with Table

function main() returns ExitCode
  var tables = TableArray{}
  var rows1 = RowArray{}
  rows1.push(Row{value: 1})
  rows1.push(Row{value: 2})
  tables.push(Table{rows: rows1})
  var rows2 = RowArray{}
  rows2.push(Row{value: 3})
  tables.push(Table{rows: rows2})
  print("{tables.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: no-leak-returned-chain -->
Build a chain in a function, return it to the caller, caller frees it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemChain = __Chain with Item

function buildChain() returns ItemChain
  var chain = ItemChain{}
  chain.insertLast(Item{value: 10})
  chain.insertLast(Item{value: 20})
  chain.insertLast(Item{value: 30})
  return chain
end 'buildChain'

function main() returns ExitCode
  var chain = buildChain()
  print("{chain.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: cycle-direct-self-ref -->
A struct with a field of its own type is a compile error.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
  export var value Integer
  export var next Node
end 'Node'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E4014: specs/fragments/ownership/cycle-direct-self-ref.test: type 'Node' contains a reference cycle (via Node → next: Node); recursive type references are not allowed
```

<!-- test: cycle-union-self-ref -->
A union with a case that references its own union type is a compile error.
```maxon
typealias Integer = int(i64.min to i64.max)

union Link
  tail
  link(value Integer, next Link)
end 'Link'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E4014: specs/fragments/ownership/cycle-union-self-ref.test: type 'Link' contains a reference cycle (via Link → link.next: Link); recursive type references are not allowed
```

<!-- test: cycle-indirect-via-container -->
A struct with a container of itself is a compile error.
```maxon
typealias Integer = int(i64.min to i64.max)

type Folder
  export var name String
  export var children Array with Folder
end 'Folder'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E4014: specs/fragments/ownership/cycle-indirect-via-container.test: type 'Folder' contains a reference cycle (via Folder → children: Array(Folder) → Folder); recursive type references are not allowed
```

<!-- test: cycle-mutual-recursion -->
Two structs that reference each other is a compile error.
```maxon
typealias Integer = int(i64.min to i64.max)

type A
  export var b B
end 'A'

type B
  export var a A
end 'B'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E4014: specs/fragments/ownership/cycle-mutual-recursion.test: type 'A' contains a reference cycle (via A → b: B → a: A); recursive type references are not allowed
```
