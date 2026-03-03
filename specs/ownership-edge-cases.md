---
feature: ownership-edge-cases
status: experimental
keywords: [refcount, memory, ownership, destructor, cleanup]
category: memory-safety
---

# Ownership & Memory Management Edge Cases

Tests for the refcount-based memory manager, ordered from simple to complex.
Uses `MmTrace: true` so the trace log verifies correct incref/decref/free behaviour.

## Tests

<!-- test: rc-single-alloc-freed -->
<!-- MmTrace -->
Single struct allocated and freed in the same function scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var p = Point{x: 1, y: 2}
  return p.x
end 'main'
```
```exitcode
1
```
```stderr
alloc Point @ rc-single-alloc-freed.test:10 #1 rc=0
incref Point @ rc-single-alloc-freed.test:10 #1 rc=1
decref Point @ rc-single-alloc-freed.test:10 #1 rc=0
free Point @ rc-single-alloc-freed.test:10
```

<!-- test: rc-alias-incref -->
<!-- MmTrace -->
Aliasing a struct increfs it; both variables share refcount and object is freed once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
  export var value Integer
end 'Box'

function main() returns ExitCode
  var a = Box{value: 42}
  let b = a
  return b.value
end 'main'
```
```exitcode
42
```
```stderr
alloc Box @ rc-alias-incref.test:9 #1 rc=0
incref Box @ rc-alias-incref.test:9 #1 rc=1
incref Box @ rc-alias-incref.test:9 #1 rc=2
decref Box @ rc-alias-incref.test:9 #1 rc=1
decref Box @ rc-alias-incref.test:9 #1 rc=0
free Box @ rc-alias-incref.test:9
```

<!-- test: rc-reassign-decrefs-old -->
<!-- MmTrace -->
Reassigning a var decrefs the old object immediately; the old object must be freed before scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Tag
  export var id Integer
end 'Tag'

function main() returns ExitCode
  var t = Tag{id: 1}
  t = Tag{id: 2}
  t = Tag{id: 3}
  return t.id
end 'main'
```
```exitcode
3
```
```stderr
alloc Tag @ rc-reassign-decrefs-old.test:9 #1 rc=0
incref Tag @ rc-reassign-decrefs-old.test:9 #1 rc=1
alloc Tag @ rc-reassign-decrefs-old.test:10 #2 rc=0
decref Tag @ rc-reassign-decrefs-old.test:9 #1 rc=0
free Tag @ rc-reassign-decrefs-old.test:9
incref Tag @ rc-reassign-decrefs-old.test:10 #2 rc=1
alloc Tag @ rc-reassign-decrefs-old.test:11 #3 rc=0
decref Tag @ rc-reassign-decrefs-old.test:10 #2 rc=0
free Tag @ rc-reassign-decrefs-old.test:10
incref Tag @ rc-reassign-decrefs-old.test:11 #3 rc=1
decref Tag @ rc-reassign-decrefs-old.test:11 #3 rc=0
free Tag @ rc-reassign-decrefs-old.test:11
```

<!-- test: rc-inner-block-freed -->
<!-- MmTrace -->
Struct created in an inner if-block is freed when that block exits, before the outer block ends.
```maxon
typealias Integer = int(i64.min to i64.max)

type Widget
  export var id Integer
end 'Widget'

function main() returns ExitCode
  var result = 0
  if true 'inner'
    var w = Widget{id: 7}
    result = w.id
  end 'inner'
  return result
end 'main'
```
```exitcode
7
```
```stderr
alloc Widget @ rc-inner-block-freed.test:11 #1 rc=0
incref Widget @ rc-inner-block-freed.test:11 #1 rc=1
decref Widget @ rc-inner-block-freed.test:11 #1 rc=0
free Widget @ rc-inner-block-freed.test:11
```

<!-- test: rc-return-transfers-ownership -->
<!-- MmTrace -->
Returning a struct skips its decref; caller receives ownership and frees it at its own scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
  export var kind Integer
end 'Token'

function makeToken(k Integer) returns Token
  var t = Token{kind: k}
  return t
end 'makeToken'

function main() returns ExitCode
  var tok = makeToken(99)
  return tok.kind
end 'main'
```
```exitcode
99
```
```stderr
alloc Token @ rc-return-transfers-ownership.test:9 #1 rc=0
incref Token @ rc-return-transfers-ownership.test:9 #1 rc=1
transfer Token @ rc-return-transfers-ownership.test:9 #1 rc=1
decref Token @ rc-return-transfers-ownership.test:9 #1 rc=0
free Token @ rc-return-transfers-ownership.test:9
```

<!-- test: rc-alias-survives-reassign -->
<!-- MmTrace -->
Aliased reference keeps object alive when the original var is reassigned.
```maxon
typealias Integer = int(i64.min to i64.max)

type Num
  export var v Integer
end 'Num'

function main() returns ExitCode
  var a = Num{v: 10}
  let b = a
  a = Num{v: 20}
  return b.v + a.v
end 'main'
```
```exitcode
30
```
```stderr
alloc Num @ rc-alias-survives-reassign.test:9 #1 rc=0
incref Num @ rc-alias-survives-reassign.test:9 #1 rc=1
incref Num @ rc-alias-survives-reassign.test:9 #1 rc=2
alloc Num @ rc-alias-survives-reassign.test:11 #2 rc=0
decref Num @ rc-alias-survives-reassign.test:9 #1 rc=1
incref Num @ rc-alias-survives-reassign.test:11 #2 rc=1
decref Num @ rc-alias-survives-reassign.test:11 #2 rc=0
free Num @ rc-alias-survives-reassign.test:11
decref Num @ rc-alias-survives-reassign.test:9 #1 rc=0
free Num @ rc-alias-survives-reassign.test:9
```

<!-- test: rc-loop-per-iteration-freed -->
<!-- MmTrace -->
A struct allocated each loop iteration is freed at loop-block exit before the next iteration.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
  export var n Integer
end 'Counter'

function main() returns ExitCode
  var total = 0
  var i = 0
  while i < 4 'loop'
    var c = Counter{n: i}
    total = total + c.n
    i = i + 1
  end 'loop'
  return total
end 'main'
```
```exitcode
6
```
```stderr
alloc Counter @ rc-loop-per-iteration-freed.test:12 #1 rc=0
incref Counter @ rc-loop-per-iteration-freed.test:12 #1 rc=1
decref Counter @ rc-loop-per-iteration-freed.test:12 #1 rc=0
free Counter @ rc-loop-per-iteration-freed.test:12
alloc Counter @ rc-loop-per-iteration-freed.test:12 #2 rc=0
incref Counter @ rc-loop-per-iteration-freed.test:12 #2 rc=1
decref Counter @ rc-loop-per-iteration-freed.test:12 #2 rc=0
free Counter @ rc-loop-per-iteration-freed.test:12
alloc Counter @ rc-loop-per-iteration-freed.test:12 #3 rc=0
incref Counter @ rc-loop-per-iteration-freed.test:12 #3 rc=1
decref Counter @ rc-loop-per-iteration-freed.test:12 #3 rc=0
free Counter @ rc-loop-per-iteration-freed.test:12
alloc Counter @ rc-loop-per-iteration-freed.test:12 #4 rc=0
incref Counter @ rc-loop-per-iteration-freed.test:12 #4 rc=1
decref Counter @ rc-loop-per-iteration-freed.test:12 #4 rc=0
free Counter @ rc-loop-per-iteration-freed.test:12
```

<!-- test: rc-break-frees-before-exit -->
<!-- MmTrace -->
Struct allocated before a break is decref'd before the loop block is exited.
```maxon
typealias Integer = int(i64.min to i64.max)

type Step
  export var val Integer
end 'Step'

function main() returns ExitCode
  var i = 0
  while i < 10 'loop'
    var s = Step{val: i}
    if s.val == 3 'stop'
      break
    end 'stop'
    i = i + 1
  end 'loop'
  return i
end 'main'
```
```exitcode
3
```
```stderr
alloc Step @ rc-break-frees-before-exit.test:11 #1 rc=0
incref Step @ rc-break-frees-before-exit.test:11 #1 rc=1
decref Step @ rc-break-frees-before-exit.test:11 #1 rc=0
free Step @ rc-break-frees-before-exit.test:11
alloc Step @ rc-break-frees-before-exit.test:11 #2 rc=0
incref Step @ rc-break-frees-before-exit.test:11 #2 rc=1
decref Step @ rc-break-frees-before-exit.test:11 #2 rc=0
free Step @ rc-break-frees-before-exit.test:11
alloc Step @ rc-break-frees-before-exit.test:11 #3 rc=0
incref Step @ rc-break-frees-before-exit.test:11 #3 rc=1
decref Step @ rc-break-frees-before-exit.test:11 #3 rc=0
free Step @ rc-break-frees-before-exit.test:11
alloc Step @ rc-break-frees-before-exit.test:11 #4 rc=0
incref Step @ rc-break-frees-before-exit.test:11 #4 rc=1
decref Step @ rc-break-frees-before-exit.test:11 #4 rc=0
free Step @ rc-break-frees-before-exit.test:11
```

<!-- test: rc-continue-frees-before-restart -->
<!-- MmTrace -->
Struct allocated before a continue is decref'd before the loop restarts.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var v Integer
end 'Item'

function main() returns ExitCode
  var total = 0
  var i = 0
  while i < 5 'loop'
    var item = Item{v: i}
    i = i + 1
    if item.v == 2 'skip'
      continue
    end 'skip'
    total = total + item.v
  end 'loop'
  return total
end 'main'
```
```exitcode
8
```
```stderr
alloc Item @ rc-continue-frees-before-restart.test:12 #1 rc=0
incref Item @ rc-continue-frees-before-restart.test:12 #1 rc=1
decref Item @ rc-continue-frees-before-restart.test:12 #1 rc=0
free Item @ rc-continue-frees-before-restart.test:12
alloc Item @ rc-continue-frees-before-restart.test:12 #2 rc=0
incref Item @ rc-continue-frees-before-restart.test:12 #2 rc=1
decref Item @ rc-continue-frees-before-restart.test:12 #2 rc=0
free Item @ rc-continue-frees-before-restart.test:12
alloc Item @ rc-continue-frees-before-restart.test:12 #3 rc=0
incref Item @ rc-continue-frees-before-restart.test:12 #3 rc=1
decref Item @ rc-continue-frees-before-restart.test:12 #3 rc=0
free Item @ rc-continue-frees-before-restart.test:12
alloc Item @ rc-continue-frees-before-restart.test:12 #4 rc=0
incref Item @ rc-continue-frees-before-restart.test:12 #4 rc=1
decref Item @ rc-continue-frees-before-restart.test:12 #4 rc=0
free Item @ rc-continue-frees-before-restart.test:12
alloc Item @ rc-continue-frees-before-restart.test:12 #5 rc=0
incref Item @ rc-continue-frees-before-restart.test:12 #5 rc=1
decref Item @ rc-continue-frees-before-restart.test:12 #5 rc=0
free Item @ rc-continue-frees-before-restart.test:12
```

<!-- test: rc-nested-struct-field-incref -->
<!-- MmTrace -->
When a struct literal is assigned as a field, the field value is incref'd; both outer and inner are freed correctly.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
  export var val Integer
end 'Inner'

type Outer
  export var child Inner
end 'Outer'

function main() returns ExitCode
  var inner = Inner{val: 55}
  var outer = Outer{child: inner}
  return outer.child.val
end 'main'
```
```exitcode
55
```
```stderr
alloc Inner @ rc-nested-struct-field-incref.test:13 #1 rc=0
incref Inner @ rc-nested-struct-field-incref.test:13 #1 rc=1
alloc Outer @ rc-nested-struct-field-incref.test:14 #2 rc=0
incref Inner @ rc-nested-struct-field-incref.test:13 #1 rc=2
incref Outer @ rc-nested-struct-field-incref.test:14 #2 rc=1
decref Inner @ rc-nested-struct-field-incref.test:13 #1 rc=1
decref Outer @ rc-nested-struct-field-incref.test:14 #2 rc=0
free Inner @ rc-nested-struct-field-incref.test:13
free Outer @ rc-nested-struct-field-incref.test:14
```

<!-- test: rc-nested-struct-deep-freed -->
<!-- MmTrace -->
Three-level nested struct: all three levels are freed when the outermost var leaves scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type A
  export var n Integer
end 'A'

type B
  export var a A
end 'B'

type C
  export var b B
end 'C'

function main() returns ExitCode
  var c = C{b: B{a: A{n: 7}}}
  return c.b.a.n
end 'main'
```
```exitcode
7
```
```stderr
alloc A @ rc-nested-struct-deep-freed.test:17 #1 rc=0
alloc B @ rc-nested-struct-deep-freed.test:17 #2 rc=0
incref A @ rc-nested-struct-deep-freed.test:17 #1 rc=1
alloc C @ rc-nested-struct-deep-freed.test:17 #3 rc=0
incref B @ rc-nested-struct-deep-freed.test:17 #2 rc=1
incref C @ rc-nested-struct-deep-freed.test:17 #3 rc=1
decref C @ rc-nested-struct-deep-freed.test:17 #3 rc=0
free A @ rc-nested-struct-deep-freed.test:17
free B @ rc-nested-struct-deep-freed.test:17
free C @ rc-nested-struct-deep-freed.test:17
```

<!-- test: rc-field-overwrite-decrefs-old -->
<!-- MmTrace -->
Overwriting a struct field via a method decrefs the old field value and increfs the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Payload
  export var data Integer
end 'Payload'

type Container
  export var payload Payload

  export function setPayload(p Payload)
    payload = p
  end 'setPayload'
end 'Container'

function main() returns ExitCode
  var old = Payload{data: 1}
  var c = Container{payload: old}
  c.setPayload(Payload{data: 2})
  return c.payload.data
end 'main'
```
```exitcode
2
```
```stderr
alloc Payload @ rc-field-overwrite-decrefs-old.test:17 #1 rc=0
incref Payload @ rc-field-overwrite-decrefs-old.test:17 #1 rc=1
alloc Container @ rc-field-overwrite-decrefs-old.test:18 #2 rc=0
incref Payload @ rc-field-overwrite-decrefs-old.test:17 #1 rc=2
incref Container @ rc-field-overwrite-decrefs-old.test:18 #2 rc=1
alloc Payload @ rc-field-overwrite-decrefs-old.test:19 #3 rc=0
decref Payload @ rc-field-overwrite-decrefs-old.test:17 #1 rc=1
incref Payload @ rc-field-overwrite-decrefs-old.test:19 #3 rc=1
decref Payload @ rc-field-overwrite-decrefs-old.test:17 #1 rc=0
free Payload @ rc-field-overwrite-decrefs-old.test:17
decref Container @ rc-field-overwrite-decrefs-old.test:18 #2 rc=0
free Payload @ rc-field-overwrite-decrefs-old.test:19
free Container @ rc-field-overwrite-decrefs-old.test:18
```

<!-- test: rc-field-overwrite-chain -->
<!-- MmTrace -->
Overwriting a struct field three times; each old value must be freed promptly.
```maxon
typealias Integer = int(i64.min to i64.max)

type Val
  export var n Integer
end 'Val'

type Holder
  export var v Val

  export function set(newV Val)
    v = newV
  end 'set'
end 'Holder'

function main() returns ExitCode
  var h = Holder{v: Val{n: 0}}
  h.set(Val{n: 10})
  h.set(Val{n: 20})
  h.set(Val{n: 30})
  return h.v.n
end 'main'
```
```exitcode
30
```
```stderr
alloc Val @ rc-field-overwrite-chain.test:17 #1 rc=0
alloc Holder @ rc-field-overwrite-chain.test:17 #2 rc=0
incref Val @ rc-field-overwrite-chain.test:17 #1 rc=1
incref Holder @ rc-field-overwrite-chain.test:17 #2 rc=1
alloc Val @ rc-field-overwrite-chain.test:18 #3 rc=0
decref Val @ rc-field-overwrite-chain.test:17 #1 rc=0
free Val @ rc-field-overwrite-chain.test:17
incref Val @ rc-field-overwrite-chain.test:18 #3 rc=1
alloc Val @ rc-field-overwrite-chain.test:19 #4 rc=0
decref Val @ rc-field-overwrite-chain.test:18 #3 rc=0
free Val @ rc-field-overwrite-chain.test:18
incref Val @ rc-field-overwrite-chain.test:19 #4 rc=1
alloc Val @ rc-field-overwrite-chain.test:20 #5 rc=0
decref Val @ rc-field-overwrite-chain.test:19 #4 rc=0
free Val @ rc-field-overwrite-chain.test:19
incref Val @ rc-field-overwrite-chain.test:20 #5 rc=1
decref Holder @ rc-field-overwrite-chain.test:17 #2 rc=0
free Val @ rc-field-overwrite-chain.test:20
free Holder @ rc-field-overwrite-chain.test:17
```

<!-- test: rc-container-push-incref -->
<!-- MmTrace -->
Pushing a struct into an array increfs it; after the local var leaves scope the element still lives.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
  export var id Integer
end 'Node'

typealias NodeArray = Array with Node

function main() returns ExitCode
  var arr = NodeArray{}
  if true 'scope'
    var n = Node{id: 10}
    arr.push(n)
  end 'scope'
  var got = try arr.get(0) otherwise Node{id: -1}
  return got.id
end 'main'
```
```exitcode
10
```
```stderr
alloc ElementMemory #1 rc=0
alloc NodeArray @ rc-container-push-incref.test:11 #2 rc=0
incref ElementMemory #1 rc=1
incref NodeArray @ rc-container-push-incref.test:11 #2 rc=1
alloc Node @ rc-container-push-incref.test:13 #3 rc=0
incref Node @ rc-container-push-incref.test:13 #3 rc=1
alloc_in Buffer
incref Node @ rc-container-push-incref.test:13 #3 rc=2
decref Node @ rc-container-push-incref.test:13 #3 rc=1
incref Node @ rc-container-push-incref.test:13 #3 rc=2
incref Node @ rc-container-push-incref.test:13 #3 rc=3
decref NodeArray @ rc-container-push-incref.test:11 #2 rc=0
free Buffer
free ElementMemory
free NodeArray @ rc-container-push-incref.test:11
decref Node @ rc-container-push-incref.test:13 #3 rc=1
decref Node @ rc-container-push-incref.test:13 #3 rc=0
free Node @ rc-container-push-incref.test:13
```

<!-- test: rc-container-pop-decrefs -->
<!-- MmTrace -->
Popping the last element and discarding the result frees the element at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
  export var id Integer
end 'Node'

typealias NodeArray = Array with Node

function main() returns ExitCode
  var arr = NodeArray{}
  arr.push(Node{id: 1})
  arr.push(Node{id: 2})
  var popped = try arr.remove(arr.count() - 1) otherwise 'err'
    return 99
  end 'err'
  return arr.count() + popped.id - popped.id
end 'main'
```
```exitcode
1
```
```stderr
alloc ElementMemory #1 rc=0
alloc NodeArray @ rc-container-pop-decrefs.test:11 #2 rc=0
incref ElementMemory #1 rc=1
incref NodeArray @ rc-container-pop-decrefs.test:11 #2 rc=1
alloc Node @ rc-container-pop-decrefs.test:12 #3 rc=0
alloc_in Buffer
incref Node @ rc-container-pop-decrefs.test:12 #3 rc=1
alloc Node @ rc-container-pop-decrefs.test:13 #5 rc=0
incref Node @ rc-container-pop-decrefs.test:13 #5 rc=1
transfer Node @ rc-container-pop-decrefs.test:13 #5 rc=1
incref Node @ rc-container-pop-decrefs.test:13 #5 rc=2
decref NodeArray @ rc-container-pop-decrefs.test:11 #2 rc=0
free Node @ rc-container-pop-decrefs.test:12
free Buffer
free ElementMemory
free NodeArray @ rc-container-pop-decrefs.test:11
decref Node @ rc-container-pop-decrefs.test:13 #5 rc=1
decref Node @ rc-container-pop-decrefs.test:13 #5 rc=0
free Node @ rc-container-pop-decrefs.test:13
```

<!-- test: rc-container-overwrite-decrefs-old -->
<!-- MmTrace -->
Setting an element at an existing index must decref the old element and incref the new one.
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
  var got = try arr.get(0) otherwise Item{value: -1}
  return got.value
end 'main'
```
```exitcode
200
```
```stderr
alloc ElementMemory #1 rc=0
alloc ItemArray @ rc-container-overwrite-decrefs-old.test:11 #2 rc=0
incref ElementMemory #1 rc=1
incref ItemArray @ rc-container-overwrite-decrefs-old.test:11 #2 rc=1
alloc Item @ rc-container-overwrite-decrefs-old.test:12 #3 rc=0
alloc_in Buffer
incref Item @ rc-container-overwrite-decrefs-old.test:12 #3 rc=1
alloc Item @ rc-container-overwrite-decrefs-old.test:13 #5 rc=0
decref Item @ rc-container-overwrite-decrefs-old.test:12 #3 rc=0
free Item @ rc-container-overwrite-decrefs-old.test:12
incref Item @ rc-container-overwrite-decrefs-old.test:13 #5 rc=1
incref Item @ rc-container-overwrite-decrefs-old.test:13 #5 rc=2
incref Item @ rc-container-overwrite-decrefs-old.test:13 #5 rc=3
decref ItemArray @ rc-container-overwrite-decrefs-old.test:11 #2 rc=0
free Buffer
free ElementMemory
free ItemArray @ rc-container-overwrite-decrefs-old.test:11
decref Item @ rc-container-overwrite-decrefs-old.test:13 #5 rc=1
decref Item @ rc-container-overwrite-decrefs-old.test:13 #5 rc=0
free Item @ rc-container-overwrite-decrefs-old.test:13
```

<!-- disabled-test: rc-container-clear-decrefs-all -->
<!-- MmTrace -->
Clearing an array decrefs every element; all elements freed when rc hits 0.
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
  arr.clear()
  return arr.count()
end 'main'
```
```exitcode
0
```
```stderr
fill me in
```

<!-- disabled-test: rc-container-scope-exit-decrefs-elements -->
<!-- MmTrace -->
When a container holding struct elements goes out of scope, all elements are decref'd.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function fill() returns Integer
  var arr = ItemArray{}
  arr.push(Item{value: 10})
  arr.push(Item{value: 20})
  arr.push(Item{value: 30})
  return arr.count()
end 'fill'

function main() returns ExitCode
  var n = fill()
  return n
end 'main'
```
```exitcode
3
```
```stderr
fill me in
```

<!-- disabled-test: rc-insert-then-remove-no-leak -->
<!-- MmTrace -->
Insert many structs then remove them all; zero elements remain in memory.
```maxon
typealias Integer = int(i64.min to i64.max)

type Entry
  export var key Integer
end 'Entry'

typealias EntryArray = Array with Entry

function main() returns ExitCode
  var arr = EntryArray{}
  var i = 0
  while i < 5 'push'
    arr.push(Entry{key: i})
    i = i + 1
  end 'push'
  var total = 0
  while arr.count() > 0 'pop'
    var e = try arr.remove(0) otherwise 'err'
      return 99
    end 'err'
    total = total + e.key
  end 'pop'
  return total
end 'main'
```
```exitcode
10
```
```stderr
fill me in
```

<!-- disabled-test: rc-insert-in-middle-no-leak -->
<!-- MmTrace -->
Insert at index 0 into an existing array; shiftRight zeroes the gap so no double-free occurs.
```maxon
typealias Integer = int(i64.min to i64.max)

type Val
  export var n Integer
end 'Val'

typealias ValArray = Array with Val

function main() returns ExitCode
  var arr = ValArray{}
  arr.push(Val{n: 10})
  arr.push(Val{n: 30})
  arr.insert(1, value: Val{n: 20})
  var a = try arr.get(0) otherwise Val{n: -1}
  var b = try arr.get(1) otherwise Val{n: -1}
  var c = try arr.get(2) otherwise Val{n: -1}
  return a.n + b.n + c.n
end 'main'
```
```exitcode
60
```
```stderr
fill me in
```

<!-- disabled-test: rc-remove-middle-no-double-free -->
<!-- MmTrace -->
Removing the middle element from an array; shiftLeft zeroes the trailing slot so setLength does not double-decref.
```maxon
typealias Integer = int(i64.min to i64.max)

type Val
  export var n Integer
end 'Val'

typealias ValArray = Array with Val

function main() returns ExitCode
  var arr = ValArray{}
  arr.push(Val{n: 1})
  arr.push(Val{n: 2})
  arr.push(Val{n: 3})
  var removed = try arr.remove(1) otherwise 'err'
    return 99
  end 'err'
  return removed.n + arr.count()
end 'main'
```
```exitcode
4
```
```stderr
fill me in
```

<!-- disabled-test: rc-nested-container-freed -->
<!-- MmTrace -->
An array whose element type itself contains a struct field; freeing the outer array frees all nested objects.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
  export var v Integer
end 'Inner'

type Wrapper
  export var inner Inner
end 'Wrapper'

typealias WrapperArray = Array with Wrapper

function main() returns ExitCode
  var arr = WrapperArray{}
  arr.push(Wrapper{inner: Inner{v: 1}})
  arr.push(Wrapper{inner: Inner{v: 2}})
  return arr.count()
end 'main'
```
```exitcode
2
```
```stderr
fill me in
```

<!-- disabled-test: rc-return-from-inner-block-cleanup -->
<!-- MmTrace -->
Returning from inside a nested block must decref all locals in every enclosing block before returning.
```maxon
typealias Integer = int(i64.min to i64.max)

type Step
  export var n Integer
end 'Step'

function compute(flag bool) returns Integer
  var outer = Step{n: 1}
  if flag 'inner'
    var inner = Step{n: 2}
    return outer.n + inner.n
  end 'inner'
  return outer.n
end 'compute'

function main() returns ExitCode
  return compute(true)
end 'main'
```
```exitcode
3
```
```stderr
fill me in
```

<!-- disabled-test: rc-return-container-element -->
<!-- MmTrace -->
Getting an element from a container and returning it; element rc stays above 0 while container is freed.
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
  arr.push(Item{value: 77})
  var result = getFirst(arr)
  return result.value
end 'main'
```
```exitcode
77
```
```stderr
fill me in
```

<!-- disabled-test: rc-global-struct-outlives-local -->
<!-- MmTrace -->
A global variable holds a struct that outlives the function that created it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Cfg
  export var level Integer
end 'Cfg'

var globalCfg = Cfg{level: 0}

function setup()
  globalCfg = Cfg{level: 42}
end 'setup'

function main() returns ExitCode
  setup()
  return globalCfg.level
end 'main'
```
```exitcode
42
```
```stderr
fill me in
```

<!-- disabled-test: rc-global-reassign-decrefs-old -->
<!-- MmTrace -->
Reassigning a global struct var decrefs the old object and increfs the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type State
  export var val Integer
end 'State'

var g = State{val: 0}

function step(n Integer)
  g = State{val: n}
end 'step'

function main() returns ExitCode
  step(10)
  step(20)
  step(30)
  return g.val
end 'main'
```
```exitcode
30
```
```stderr
fill me in
```

<!-- disabled-test: rc-union-no-struct-payload-freed -->
<!-- MmTrace -->
A simple enum union (no struct payload) is freed correctly at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

union Color
  red
  green
  blue
end 'Color'

function colorCode(c Color) returns Integer
  match c 'pick'
    Color.red   gives 1
    Color.green gives 2
    Color.blue  gives 3
  end 'pick'
end 'colorCode'

function main() returns ExitCode
  var c = Color.green
  return colorCode(c)
end 'main'
```
```exitcode
2
```
```stderr
fill me in
```

<!-- disabled-test: rc-union-struct-payload-freed -->
<!-- MmTrace -->
A union case with a struct-typed associated value; when the enum is freed its payload must be decref'd.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
  export var mass Integer
end 'Body'

union Shape
  empty
  solid(body Body)
end 'Shape'

function massOf(s Shape) returns Integer
  match s 'check'
    Shape.empty gives 0
    Shape.solid(body) gives body.mass
  end 'check'
end 'massOf'

function main() returns ExitCode
  var s = Shape.solid(body: Body{mass: 5})
  return massOf(s)
end 'main'
```
```exitcode
5
```
```stderr
fill me in
```

<!-- disabled-test: rc-closure-env-freed -->
<!-- MmTrace -->
Closure environment block is allocated as a struct and freed when the closure variable goes out of scope.
```maxon
typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
  return f(x)
end 'apply'

function main() returns ExitCode
  var offset = 5
  var result = apply(f: (n Integer) gives n + offset, x: 10)
  return result
end 'main'
```
```exitcode
15
```
```stderr
fill me in
```

<!-- disabled-test: rc-closure-captures-struct -->
<!-- MmTrace -->
Closure captures a struct variable by address; the closure env is freed at scope exit but the original struct lives on.
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
  return result
end 'main'
```
```exitcode
3
```
```stderr
fill me in
```

<!-- disabled-test: rc-error-path-cleanup -->
<!-- MmTrace -->
On the error path of a try expression the locally allocated struct must still be freed.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
  var arr = ItemArray{}
  var got = try arr.get(0) otherwise Item{value: 99}
  return got.value
end 'main'
```
```exitcode
99
```
```stderr
fill me in
```

<!-- disabled-test: rc-chain-insert-incref -->
<!-- MmTrace -->
Inserting a struct into a chain increfs the value; the node holds the reference.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
  export var id Integer
end 'Token'

typealias TokenChain = __Chain with Token

function main() returns ExitCode
  var chain = TokenChain{}
  var t = Token{id: 7}
  var node = chain.insertFirst(t)
  return node.value().id
end 'main'
```
```exitcode
7
```
```stderr
fill me in
```

<!-- disabled-test: rc-chain-remove-decrefs -->
<!-- MmTrace -->
Removing a node from a chain transfers ownership; value is freed when the result var leaves scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
  export var id Integer
end 'Token'

typealias TokenChain = __Chain with Token

function main() returns ExitCode
  var chain = TokenChain{}
  var node = chain.insertFirst(Token{id: 9})
  var removed = chain.remove(node)
  return removed.id + chain.count()
end 'main'
```
```exitcode
9
```
```stderr
fill me in
```

<!-- disabled-test: rc-chain-clear-decrefs-all -->
<!-- MmTrace -->
Clearing a chain decrefs every node value; all values freed when rc hits 0.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
  export var id Integer
end 'Token'

typealias TokenChain = __Chain with Token

function main() returns ExitCode
  var chain = TokenChain{}
  chain.insertLast(Token{id: 1})
  chain.insertLast(Token{id: 2})
  chain.insertLast(Token{id: 3})
  chain.clear()
  return chain.count()
end 'main'
```
```exitcode
0
```
```stderr
fill me in
```

<!-- disabled-test: rc-chain-node-set-value-decrefs-old -->
<!-- MmTrace -->
Calling `setValue` on a chain node decrefs the old value and increfs the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
  export var id Integer
end 'Token'

typealias TokenChain = __Chain with Token

function main() returns ExitCode
  var chain = TokenChain{}
  var node = chain.insertFirst(Token{id: 1})
  node.setValue(Token{id: 99})
  return node.value().id
end 'main'
```
```exitcode
99
```
```stderr
fill me in
```

<!-- disabled-test: rc-for-in-elem-decrefed -->
<!-- MmTrace -->
In a for-in loop over a struct array each element reference is decref'd at the end of the loop body.
```maxon
typealias Integer = int(i64.min to i64.max)

type Score
  export var pts Integer
end 'Score'

typealias ScoreArray = Array with Score

function main() returns ExitCode
  var scores = ScoreArray{}
  scores.push(Score{pts: 10})
  scores.push(Score{pts: 20})
  scores.push(Score{pts: 30})
  var total = 0
  for s in scores 'loop'
    total = total + s.pts
  end 'loop'
  return total
end 'main'
```
```exitcode
60
```
```stderr
fill me in
```

<!-- disabled-test: rc-multiple-aliases-freed-once -->
<!-- MmTrace -->
Three aliases to the same object; the object is freed exactly once when the last alias leaves scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Data
  export var n Integer
end 'Data'

function main() returns ExitCode
  var a = Data{n: 7}
  var b = a
  var c = a
  return a.n + b.n + c.n
end 'main'
```
```exitcode
21
```
```stderr
fill me in
```

<!-- disabled-test: rc-deep-container-of-containers -->
<!-- MmTrace -->
An array of arrays of structs; freeing the outer array cascades through all levels.
```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
  export var val Integer
end 'Cell'

typealias CellArray = Array with Cell
typealias Grid = Array with CellArray

function main() returns ExitCode
  var grid = Grid{}
  var row1 = CellArray{}
  row1.push(Cell{val: 1})
  row1.push(Cell{val: 2})
  var row2 = CellArray{}
  row2.push(Cell{val: 3})
  grid.push(row1)
  grid.push(row2)
  return grid.count()
end 'main'
```
```exitcode
2
```
```stderr
fill me in
```

<!-- disabled-test: rc-struct-with-array-field-freed -->
<!-- MmTrace -->
A struct that owns an array field; when the struct is freed the array (and its elements) are freed too.
```maxon
typealias Integer = int(i64.min to i64.max)

type Entry
  export var val Integer
end 'Entry'

typealias EntryArray = Array with Entry

type Bucket
  export var items EntryArray
end 'Bucket'

function fill() returns Integer
  var b = Bucket{items: EntryArray{}}
  b.items.push(Entry{val: 10})
  b.items.push(Entry{val: 20})
  return b.items.count()
end 'fill'

function main() returns ExitCode
  return fill()
end 'main'
```
```exitcode
2
``````stderr
fill me in
```
