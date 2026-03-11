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
alloc Point #1 rc=0 size=16 [ownership-edge-cases.main]
incref Point #1 rc=1 [ownership-edge-cases.main]
decref Point #1 rc=0 [ownership-edge-cases.main]
  free Point #1
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
alloc Box #1 rc=0 size=8 [ownership-edge-cases.main]
incref Box #1 rc=1 [ownership-edge-cases.main]
incref Box #1 rc=2 [ownership-edge-cases.main]
decref Box #1 rc=1 [ownership-edge-cases.main]
decref Box #1 rc=0 [ownership-edge-cases.main]
  free Box #1
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
alloc Tag #1 rc=0 size=8 [ownership-edge-cases.main]
incref Tag #1 rc=1 [ownership-edge-cases.main]
alloc Tag #2 rc=0 size=8 [ownership-edge-cases.main]
incref Tag #2 rc=1 [ownership-edge-cases.main]
decref Tag #1 rc=0 [ownership-edge-cases.main]
  free Tag #1
incref Tag #2 rc=2 [ownership-edge-cases.main]
alloc Tag #3 rc=0 size=8 [ownership-edge-cases.main]
incref Tag #3 rc=1 [ownership-edge-cases.main]
decref Tag #2 rc=1 [ownership-edge-cases.main]
incref Tag #3 rc=2 [ownership-edge-cases.main]
decref Tag #3 rc=1 [ownership-edge-cases.main]
decref Tag #2 rc=0 [ownership-edge-cases.main]
  free Tag #2
decref Tag #3 rc=0 [ownership-edge-cases.main]
  free Tag #3
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
alloc Widget #1 rc=0 size=8 [ownership-edge-cases.main]
incref Widget #1 rc=1 [ownership-edge-cases.main]
decref Widget #1 rc=0 [ownership-edge-cases.main]
  free Widget #1
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
alloc Token #1 rc=0 size=8 [ownership-edge-cases.makeToken]
incref Token #1 rc=1 [ownership-edge-cases.makeToken]
transfer Token #1 rc=1 [ownership-edge-cases.makeToken]
decref Token #1 rc=0 [ownership-edge-cases.main]
  free Token #1
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
alloc Num #1 rc=0 size=8 [ownership-edge-cases.main]
incref Num #1 rc=1 [ownership-edge-cases.main]
incref Num #1 rc=2 [ownership-edge-cases.main]
alloc Num #2 rc=0 size=8 [ownership-edge-cases.main]
incref Num #2 rc=1 [ownership-edge-cases.main]
decref Num #1 rc=1 [ownership-edge-cases.main]
incref Num #2 rc=2 [ownership-edge-cases.main]
decref Num #1 rc=0 [ownership-edge-cases.main]
  free Num #1
decref Num #2 rc=1 [ownership-edge-cases.main]
decref Num #2 rc=0 [ownership-edge-cases.main]
  free Num #2
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
alloc Counter #1 rc=0 size=8 [ownership-edge-cases.main]
incref Counter #1 rc=1 [ownership-edge-cases.main]
decref Counter #1 rc=0 [ownership-edge-cases.main]
  free Counter #1
alloc Counter #2 rc=0 size=8 [ownership-edge-cases.main]
incref Counter #2 rc=1 [ownership-edge-cases.main]
decref Counter #2 rc=0 [ownership-edge-cases.main]
  free Counter #2
alloc Counter #3 rc=0 size=8 [ownership-edge-cases.main]
incref Counter #3 rc=1 [ownership-edge-cases.main]
decref Counter #3 rc=0 [ownership-edge-cases.main]
  free Counter #3
alloc Counter #4 rc=0 size=8 [ownership-edge-cases.main]
incref Counter #4 rc=1 [ownership-edge-cases.main]
decref Counter #4 rc=0 [ownership-edge-cases.main]
  free Counter #4
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
alloc Step #1 rc=0 size=8 [ownership-edge-cases.main]
incref Step #1 rc=1 [ownership-edge-cases.main]
decref Step #1 rc=0 [ownership-edge-cases.main]
  free Step #1
alloc Step #2 rc=0 size=8 [ownership-edge-cases.main]
incref Step #2 rc=1 [ownership-edge-cases.main]
decref Step #2 rc=0 [ownership-edge-cases.main]
  free Step #2
alloc Step #3 rc=0 size=8 [ownership-edge-cases.main]
incref Step #3 rc=1 [ownership-edge-cases.main]
decref Step #3 rc=0 [ownership-edge-cases.main]
  free Step #3
alloc Step #4 rc=0 size=8 [ownership-edge-cases.main]
incref Step #4 rc=1 [ownership-edge-cases.main]
decref Step #4 rc=0 [ownership-edge-cases.main]
  free Step #4
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
alloc Item #1 rc=0 size=8 [ownership-edge-cases.main]
incref Item #1 rc=1 [ownership-edge-cases.main]
decref Item #1 rc=0 [ownership-edge-cases.main]
  free Item #1
alloc Item #2 rc=0 size=8 [ownership-edge-cases.main]
incref Item #2 rc=1 [ownership-edge-cases.main]
decref Item #2 rc=0 [ownership-edge-cases.main]
  free Item #2
alloc Item #3 rc=0 size=8 [ownership-edge-cases.main]
incref Item #3 rc=1 [ownership-edge-cases.main]
decref Item #3 rc=0 [ownership-edge-cases.main]
  free Item #3
alloc Item #4 rc=0 size=8 [ownership-edge-cases.main]
incref Item #4 rc=1 [ownership-edge-cases.main]
decref Item #4 rc=0 [ownership-edge-cases.main]
  free Item #4
alloc Item #5 rc=0 size=8 [ownership-edge-cases.main]
incref Item #5 rc=1 [ownership-edge-cases.main]
decref Item #5 rc=0 [ownership-edge-cases.main]
  free Item #5
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
alloc Inner #1 rc=0 size=8 [ownership-edge-cases.main]
incref Inner #1 rc=1 [ownership-edge-cases.main]
alloc Outer #2 rc=0 size=8 [ownership-edge-cases.main]
incref Inner #1 rc=2 [ownership-edge-cases.main]
incref Outer #2 rc=1 [ownership-edge-cases.main]
decref Outer #2 rc=0 [ownership-edge-cases.main]
decref Inner #1 rc=1 [~Outer]
  free Outer #2
decref Inner #1 rc=0 [ownership-edge-cases.main]
  free Inner #1
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
alloc A #1 rc=0 size=8 [ownership-edge-cases.main]
alloc B #2 rc=0 size=8 [ownership-edge-cases.main]
incref A #1 rc=1 [ownership-edge-cases.main]
alloc C #3 rc=0 size=8 [ownership-edge-cases.main]
incref B #2 rc=1 [ownership-edge-cases.main]
incref C #3 rc=1 [ownership-edge-cases.main]
decref C #3 rc=0 [ownership-edge-cases.main]
decref B #2 rc=0 [~C]
decref A #1 rc=0 [~B]
  free A #1
  free B #2
  free C #3
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
alloc Payload #1 rc=0 size=8 [ownership-edge-cases.main]
incref Payload #1 rc=1 [ownership-edge-cases.main]
alloc Container #2 rc=0 size=8 [ownership-edge-cases.main]
incref Payload #1 rc=2 [ownership-edge-cases.main]
incref Container #2 rc=1 [ownership-edge-cases.main]
alloc Payload #3 rc=0 size=8 [ownership-edge-cases.main]
incref Payload #3 rc=1 [ownership-edge-cases.main]
decref Payload #1 rc=1 [Container.setPayload]
incref Payload #3 rc=2 [Container.setPayload]
decref Container #2 rc=0 [ownership-edge-cases.main]
decref Payload #3 rc=1 [~Container]
  free Container #2
decref Payload #1 rc=0 [ownership-edge-cases.main]
  free Payload #1
decref Payload #3 rc=0 [ownership-edge-cases.main]
  free Payload #3
```

<!-- test: rc-field-overwrite-managed-list -->
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
alloc Val #1 rc=0 size=8 [ownership-edge-cases.main]
alloc Holder #2 rc=0 size=8 [ownership-edge-cases.main]
incref Val #1 rc=1 [ownership-edge-cases.main]
incref Holder #2 rc=1 [ownership-edge-cases.main]
alloc Val #3 rc=0 size=8 [ownership-edge-cases.main]
incref Val #3 rc=1 [ownership-edge-cases.main]
decref Val #1 rc=0 [Holder.set]
  free Val #1
incref Val #3 rc=2 [Holder.set]
alloc Val #4 rc=0 size=8 [ownership-edge-cases.main]
incref Val #4 rc=1 [ownership-edge-cases.main]
decref Val #3 rc=1 [Holder.set]
incref Val #4 rc=2 [Holder.set]
alloc Val #5 rc=0 size=8 [ownership-edge-cases.main]
incref Val #5 rc=1 [ownership-edge-cases.main]
decref Val #4 rc=1 [Holder.set]
incref Val #5 rc=2 [Holder.set]
decref Holder #2 rc=0 [ownership-edge-cases.main]
decref Val #5 rc=1 [~Holder]
  free Holder #2
decref Val #3 rc=0 [ownership-edge-cases.main]
  free Val #3
decref Val #4 rc=0 [ownership-edge-cases.main]
  free Val #4
decref Val #5 rc=0 [ownership-edge-cases.main]
  free Val #5
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
alloc __ManagedMemory_Node #1 rc=0 size=32 [ownership-edge-cases.main]
alloc NodeArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Node #1 rc=1 [ownership-edge-cases.main]
incref NodeArray #2 rc=1 [ownership-edge-cases.main]
alloc Node #3 rc=0 size=8 [ownership-edge-cases.main]
incref Node #3 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Node #1 rc=1 size=32
incref Node #3 rc=2 [NodeArray.push]
decref Node #3 rc=1 [ownership-edge-cases.main]
incref Node #3 rc=2 [NodeArray.get]
incref Node #3 rc=3 [ownership-edge-cases.main]
decref Node #3 rc=2 [ownership-edge-cases.main]
decref Node #3 rc=1 [ownership-edge-cases.main]
decref NodeArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Node #1 rc=0 [~NodeArray]
decref Node #3 rc=0 [~ManagedElements]
  free Node #3
  free __ManagedMemory_Node #1
  free NodeArray #2
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
alloc __ManagedMemory_Node #1 rc=0 size=32 [ownership-edge-cases.main]
alloc NodeArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Node #1 rc=1 [ownership-edge-cases.main]
incref NodeArray #2 rc=1 [ownership-edge-cases.main]
alloc Node #3 rc=0 size=8 [ownership-edge-cases.main]
incref Node #3 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Node #1 rc=1 size=32
incref Node #3 rc=2 [NodeArray.push]
alloc Node #4 rc=0 size=8 [ownership-edge-cases.main]
incref Node #4 rc=1 [ownership-edge-cases.main]
incref Node #4 rc=2 [NodeArray.push]
incref Node #4 rc=3 [NodeArray.remove]
transfer Node #4 rc=3 [NodeArray.remove]
decref Node #4 rc=2 [NodeArray.remove]
incref Node #4 rc=3 [ownership-edge-cases.main]
decref Node #4 rc=2 [ownership-edge-cases.main]
decref Node #4 rc=1 [ownership-edge-cases.main]
decref NodeArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Node #1 rc=0 [~NodeArray]
decref Node #3 rc=1 [~ManagedElements]
  free __ManagedMemory_Node #1
  free NodeArray #2
decref Node #3 rc=0 [ownership-edge-cases.main]
  free Node #3
decref Node #4 rc=0 [ownership-edge-cases.main]
  free Node #4
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
alloc __ManagedMemory_Item #1 rc=0 size=32 [ownership-edge-cases.main]
alloc ItemArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Item #1 rc=1 [ownership-edge-cases.main]
incref ItemArray #2 rc=1 [ownership-edge-cases.main]
alloc Item #3 rc=0 size=8 [ownership-edge-cases.main]
incref Item #3 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Item #1 rc=1 size=32
incref Item #3 rc=2 [ItemArray.push]
alloc Item #4 rc=0 size=8 [ownership-edge-cases.main]
incref Item #4 rc=1 [ownership-edge-cases.main]
decref Item #3 rc=1 [ItemArray.set]
incref Item #4 rc=2 [ItemArray.set]
incref Item #4 rc=3 [ItemArray.get]
incref Item #4 rc=4 [ownership-edge-cases.main]
decref Item #4 rc=3 [ownership-edge-cases.main]
decref Item #4 rc=2 [ownership-edge-cases.main]
decref ItemArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
decref Item #4 rc=1 [~ManagedElements]
  free __ManagedMemory_Item #1
  free ItemArray #2
decref Item #3 rc=0 [ownership-edge-cases.main]
  free Item #3
decref Item #4 rc=0 [ownership-edge-cases.main]
  free Item #4
```

<!-- test: rc-container-clear-decrefs-all -->
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
alloc __ManagedMemory_Item #1 rc=0 size=32 [ownership-edge-cases.main]
alloc ItemArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Item #1 rc=1 [ownership-edge-cases.main]
incref ItemArray #2 rc=1 [ownership-edge-cases.main]
alloc Item #3 rc=0 size=8 [ownership-edge-cases.main]
incref Item #3 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Item #1 rc=1 size=32
incref Item #3 rc=2 [ItemArray.push]
alloc Item #4 rc=0 size=8 [ownership-edge-cases.main]
incref Item #4 rc=1 [ownership-edge-cases.main]
incref Item #4 rc=2 [ItemArray.push]
alloc Item #5 rc=0 size=8 [ownership-edge-cases.main]
incref Item #5 rc=1 [ownership-edge-cases.main]
incref Item #5 rc=2 [ItemArray.push]
decref Item #3 rc=1 [~ManagedElements]
decref Item #4 rc=1 [~ManagedElements]
decref Item #5 rc=1 [~ManagedElements]
decref ItemArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
  free __ManagedMemory_Item #1
  free ItemArray #2
decref Item #3 rc=0 [ownership-edge-cases.main]
  free Item #3
decref Item #4 rc=0 [ownership-edge-cases.main]
  free Item #4
decref Item #5 rc=0 [ownership-edge-cases.main]
  free Item #5
```

<!-- test: rc-container-scope-exit-decrefs-elements -->
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
alloc __ManagedMemory_Item #1 rc=0 size=32 [ownership-edge-cases.fill]
alloc ItemArray #2 rc=0 size=16 [ownership-edge-cases.fill]
incref __ManagedMemory_Item #1 rc=1 [ownership-edge-cases.fill]
incref ItemArray #2 rc=1 [ownership-edge-cases.fill]
alloc Item #3 rc=0 size=8 [ownership-edge-cases.fill]
incref Item #3 rc=1 [ownership-edge-cases.fill]
realloc __ManagedMemory_Item #1 rc=1 size=32
incref Item #3 rc=2 [ItemArray.push]
alloc Item #4 rc=0 size=8 [ownership-edge-cases.fill]
incref Item #4 rc=1 [ownership-edge-cases.fill]
incref Item #4 rc=2 [ItemArray.push]
alloc Item #5 rc=0 size=8 [ownership-edge-cases.fill]
incref Item #5 rc=1 [ownership-edge-cases.fill]
incref Item #5 rc=2 [ItemArray.push]
decref ItemArray #2 rc=0 [ownership-edge-cases.fill]
decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
decref Item #3 rc=1 [~ManagedElements]
decref Item #4 rc=1 [~ManagedElements]
decref Item #5 rc=1 [~ManagedElements]
  free __ManagedMemory_Item #1
  free ItemArray #2
decref Item #3 rc=0 [ownership-edge-cases.fill]
  free Item #3
decref Item #4 rc=0 [ownership-edge-cases.fill]
  free Item #4
decref Item #5 rc=0 [ownership-edge-cases.fill]
  free Item #5
```

<!-- test: rc-insert-then-remove-no-leak -->
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
alloc __ManagedMemory_Entry #1 rc=0 size=32 [ownership-edge-cases.main]
alloc EntryArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Entry #1 rc=1 [ownership-edge-cases.main]
incref EntryArray #2 rc=1 [ownership-edge-cases.main]
alloc Entry #3 rc=0 size=8 [ownership-edge-cases.main]
incref Entry #3 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Entry #1 rc=1 size=32
incref Entry #3 rc=2 [EntryArray.push]
decref Entry #3 rc=1 [ownership-edge-cases.main]
alloc Entry #4 rc=0 size=8 [ownership-edge-cases.main]
incref Entry #4 rc=1 [ownership-edge-cases.main]
incref Entry #4 rc=2 [EntryArray.push]
decref Entry #4 rc=1 [ownership-edge-cases.main]
alloc Entry #5 rc=0 size=8 [ownership-edge-cases.main]
incref Entry #5 rc=1 [ownership-edge-cases.main]
incref Entry #5 rc=2 [EntryArray.push]
decref Entry #5 rc=1 [ownership-edge-cases.main]
alloc Entry #6 rc=0 size=8 [ownership-edge-cases.main]
incref Entry #6 rc=1 [ownership-edge-cases.main]
incref Entry #6 rc=2 [EntryArray.push]
decref Entry #6 rc=1 [ownership-edge-cases.main]
alloc Entry #7 rc=0 size=8 [ownership-edge-cases.main]
incref Entry #7 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Entry #1 rc=1 size=64
incref Entry #7 rc=2 [EntryArray.push]
decref Entry #7 rc=1 [ownership-edge-cases.main]
incref Entry #3 rc=2 [EntryArray.remove]
transfer Entry #3 rc=2 [EntryArray.remove]
decref Entry #3 rc=1 [EntryArray.remove]
decref Entry #3 rc=0 [ownership-edge-cases.main]
  free Entry #3
incref Entry #4 rc=2 [EntryArray.remove]
transfer Entry #4 rc=2 [EntryArray.remove]
decref Entry #4 rc=1 [EntryArray.remove]
decref Entry #4 rc=0 [ownership-edge-cases.main]
  free Entry #4
incref Entry #5 rc=2 [EntryArray.remove]
transfer Entry #5 rc=2 [EntryArray.remove]
decref Entry #5 rc=1 [EntryArray.remove]
decref Entry #5 rc=0 [ownership-edge-cases.main]
  free Entry #5
incref Entry #6 rc=2 [EntryArray.remove]
transfer Entry #6 rc=2 [EntryArray.remove]
decref Entry #6 rc=1 [EntryArray.remove]
decref Entry #6 rc=0 [ownership-edge-cases.main]
  free Entry #6
incref Entry #7 rc=2 [EntryArray.remove]
transfer Entry #7 rc=2 [EntryArray.remove]
decref Entry #7 rc=1 [EntryArray.remove]
decref Entry #7 rc=0 [ownership-edge-cases.main]
  free Entry #7
decref EntryArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Entry #1 rc=0 [~EntryArray]
  free __ManagedMemory_Entry #1
  free EntryArray #2
```

<!-- test: rc-insert-in-middle-no-leak -->
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
alloc __ManagedMemory_Val #1 rc=0 size=32 [ownership-edge-cases.main]
alloc ValArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Val #1 rc=1 [ownership-edge-cases.main]
incref ValArray #2 rc=1 [ownership-edge-cases.main]
alloc Val #3 rc=0 size=8 [ownership-edge-cases.main]
incref Val #3 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Val #1 rc=1 size=32
incref Val #3 rc=2 [ValArray.push]
alloc Val #4 rc=0 size=8 [ownership-edge-cases.main]
incref Val #4 rc=1 [ownership-edge-cases.main]
incref Val #4 rc=2 [ValArray.push]
alloc Val #5 rc=0 size=8 [ownership-edge-cases.main]
incref Val #5 rc=1 [ownership-edge-cases.main]
incref Val #5 rc=2 [ValArray.insert]
incref Val #3 rc=3 [ValArray.get]
incref Val #3 rc=4 [ownership-edge-cases.main]
incref Val #5 rc=3 [ValArray.get]
incref Val #5 rc=4 [ownership-edge-cases.main]
incref Val #4 rc=3 [ValArray.get]
incref Val #4 rc=4 [ownership-edge-cases.main]
decref Val #4 rc=3 [ownership-edge-cases.main]
decref Val #5 rc=3 [ownership-edge-cases.main]
decref Val #3 rc=3 [ownership-edge-cases.main]
decref Val #4 rc=2 [ownership-edge-cases.main]
decref Val #5 rc=2 [ownership-edge-cases.main]
decref Val #3 rc=2 [ownership-edge-cases.main]
decref ValArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Val #1 rc=0 [~ValArray]
decref Val #3 rc=1 [~ManagedElements]
decref Val #5 rc=1 [~ManagedElements]
decref Val #4 rc=1 [~ManagedElements]
  free __ManagedMemory_Val #1
  free ValArray #2
decref Val #3 rc=0 [ownership-edge-cases.main]
  free Val #3
decref Val #4 rc=0 [ownership-edge-cases.main]
  free Val #4
decref Val #5 rc=0 [ownership-edge-cases.main]
  free Val #5
```

<!-- test: rc-remove-middle-no-double-free -->
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
alloc __ManagedMemory_Val #1 rc=0 size=32 [ownership-edge-cases.main]
alloc ValArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Val #1 rc=1 [ownership-edge-cases.main]
incref ValArray #2 rc=1 [ownership-edge-cases.main]
alloc Val #3 rc=0 size=8 [ownership-edge-cases.main]
incref Val #3 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Val #1 rc=1 size=32
incref Val #3 rc=2 [ValArray.push]
alloc Val #4 rc=0 size=8 [ownership-edge-cases.main]
incref Val #4 rc=1 [ownership-edge-cases.main]
incref Val #4 rc=2 [ValArray.push]
alloc Val #5 rc=0 size=8 [ownership-edge-cases.main]
incref Val #5 rc=1 [ownership-edge-cases.main]
incref Val #5 rc=2 [ValArray.push]
incref Val #4 rc=3 [ValArray.remove]
transfer Val #4 rc=3 [ValArray.remove]
decref Val #4 rc=2 [ValArray.remove]
incref Val #4 rc=3 [ownership-edge-cases.main]
decref Val #4 rc=2 [ownership-edge-cases.main]
decref Val #4 rc=1 [ownership-edge-cases.main]
decref ValArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Val #1 rc=0 [~ValArray]
decref Val #3 rc=1 [~ManagedElements]
decref Val #5 rc=1 [~ManagedElements]
  free __ManagedMemory_Val #1
  free ValArray #2
decref Val #3 rc=0 [ownership-edge-cases.main]
  free Val #3
decref Val #4 rc=0 [ownership-edge-cases.main]
  free Val #4
decref Val #5 rc=0 [ownership-edge-cases.main]
  free Val #5
```

<!-- test: rc-nested-container-freed -->
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
alloc __ManagedMemory_Wrapper #1 rc=0 size=32 [ownership-edge-cases.main]
alloc WrapperArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Wrapper #1 rc=1 [ownership-edge-cases.main]
incref WrapperArray #2 rc=1 [ownership-edge-cases.main]
alloc Inner #3 rc=0 size=8 [ownership-edge-cases.main]
alloc Wrapper #4 rc=0 size=8 [ownership-edge-cases.main]
incref Inner #3 rc=1 [ownership-edge-cases.main]
incref Wrapper #4 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Wrapper #1 rc=1 size=32
incref Wrapper #4 rc=2 [WrapperArray.push]
alloc Inner #5 rc=0 size=8 [ownership-edge-cases.main]
alloc Wrapper #6 rc=0 size=8 [ownership-edge-cases.main]
incref Inner #5 rc=1 [ownership-edge-cases.main]
incref Wrapper #6 rc=1 [ownership-edge-cases.main]
incref Wrapper #6 rc=2 [WrapperArray.push]
decref WrapperArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Wrapper #1 rc=0 [~WrapperArray]
decref Wrapper #4 rc=1 [~ManagedElements]
decref Wrapper #6 rc=1 [~ManagedElements]
  free __ManagedMemory_Wrapper #1
  free WrapperArray #2
decref Wrapper #4 rc=0 [ownership-edge-cases.main]
decref Inner #3 rc=0 [~Wrapper]
  free Inner #3
  free Wrapper #4
decref Wrapper #6 rc=0 [ownership-edge-cases.main]
decref Inner #5 rc=0 [~Wrapper]
  free Inner #5
  free Wrapper #6
```

<!-- test: rc-return-from-inner-block-cleanup -->
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
alloc Step #1 rc=0 size=8 [ownership-edge-cases.compute]
incref Step #1 rc=1 [ownership-edge-cases.compute]
alloc Step #2 rc=0 size=8 [ownership-edge-cases.compute]
incref Step #2 rc=1 [ownership-edge-cases.compute]
decref Step #2 rc=0 [ownership-edge-cases.compute]
  free Step #2
decref Step #1 rc=0 [ownership-edge-cases.compute]
  free Step #1
```

<!-- test: rc-return-container-element -->
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
alloc __ManagedMemory_Item #1 rc=0 size=32 [ownership-edge-cases.main]
alloc ItemArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Item #1 rc=1 [ownership-edge-cases.main]
incref ItemArray #2 rc=1 [ownership-edge-cases.main]
alloc Item #3 rc=0 size=8 [ownership-edge-cases.main]
incref Item #3 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Item #1 rc=1 size=32
incref Item #3 rc=2 [ItemArray.push]
incref Item #3 rc=3 [ItemArray.get]
incref Item #3 rc=4 [ownership-edge-cases.getFirst]
decref Item #3 rc=3 [ownership-edge-cases.getFirst]
transfer Item #3 rc=3 [ownership-edge-cases.getFirst]
decref Item #3 rc=2 [ownership-edge-cases.main]
decref ItemArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
decref Item #3 rc=1 [~ManagedElements]
  free __ManagedMemory_Item #1
  free ItemArray #2
decref Item #3 rc=0 [ownership-edge-cases.main]
  free Item #3
```

<!-- test: rc-global-struct-outlives-local -->
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
alloc Cfg #1 rc=0 size=8 [ownership-edge-cases.__module_init]
incref Cfg #1 rc=1 [ownership-edge-cases.__module_init]
incref Cfg #1 rc=2 [ownership-edge-cases.__module_init]
decref Cfg #1 rc=1 [ownership-edge-cases.__module_init]
alloc Cfg #2 rc=0 size=8 [ownership-edge-cases.setup]
incref Cfg #2 rc=1 [ownership-edge-cases.setup]
decref Cfg #1 rc=0 [ownership-edge-cases.setup]
  free Cfg #1
incref Cfg #2 rc=2 [ownership-edge-cases.setup]
decref Cfg #2 rc=1 [ownership-edge-cases.setup]
incref Cfg #2 rc=2 [ownership-edge-cases.main]
decref Cfg #2 rc=1 [ownership-edge-cases.main]
decref Cfg #2 rc=0 [__maxon_global_cleanup]
  free Cfg #2
```

<!-- test: rc-global-reassign-decrefs-old -->
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
alloc State #1 rc=0 size=8 [ownership-edge-cases.__module_init]
incref State #1 rc=1 [ownership-edge-cases.__module_init]
incref State #1 rc=2 [ownership-edge-cases.__module_init]
decref State #1 rc=1 [ownership-edge-cases.__module_init]
alloc State #2 rc=0 size=8 [ownership-edge-cases.step]
incref State #2 rc=1 [ownership-edge-cases.step]
decref State #1 rc=0 [ownership-edge-cases.step]
  free State #1
incref State #2 rc=2 [ownership-edge-cases.step]
decref State #2 rc=1 [ownership-edge-cases.step]
alloc State #3 rc=0 size=8 [ownership-edge-cases.step]
incref State #3 rc=1 [ownership-edge-cases.step]
decref State #2 rc=0 [ownership-edge-cases.step]
  free State #2
incref State #3 rc=2 [ownership-edge-cases.step]
decref State #3 rc=1 [ownership-edge-cases.step]
alloc State #4 rc=0 size=8 [ownership-edge-cases.step]
incref State #4 rc=1 [ownership-edge-cases.step]
decref State #3 rc=0 [ownership-edge-cases.step]
  free State #3
incref State #4 rc=2 [ownership-edge-cases.step]
decref State #4 rc=1 [ownership-edge-cases.step]
incref State #4 rc=2 [ownership-edge-cases.main]
decref State #4 rc=1 [ownership-edge-cases.main]
decref State #4 rc=0 [__maxon_global_cleanup]
  free State #4
```

<!-- test: rc-union-no-struct-payload-freed -->
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
  let result = match c 'pick'
    Color.red   gives 1
    Color.green gives 2
    Color.blue  gives 3
  end 'pick'
  return result
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
```

<!-- test: rc-union-struct-payload-freed -->
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
    Shape.empty then return 0
    solid(b) then return b.mass
  end 'check'
end 'massOf'

function main() returns ExitCode
  var s = Shape.solid(Body{mass: 5})
  return massOf(s)
end 'main'
```
```exitcode
5
```
```stderr
alloc Body #1 rc=0 size=8 [ownership-edge-cases.main]
incref Body #1 rc=1 [ownership-edge-cases.main]
alloc Shape #2 rc=0 size=16 [ownership-edge-cases.main]
incref Body #1 rc=2 [ownership-edge-cases.main]
incref Shape #2 rc=1 [ownership-edge-cases.main]
incref Shape #2 rc=2 [ownership-edge-cases.massOf]
incref Body #1 rc=3 [ownership-edge-cases.massOf]
decref Shape #2 rc=1 [ownership-edge-cases.massOf]
decref Body #1 rc=2 [ownership-edge-cases.massOf]
decref Shape #2 rc=0 [ownership-edge-cases.main]
decref Body #1 rc=1 [~Shape]
  free Shape #2
decref Body #1 rc=0 [ownership-edge-cases.main]
  free Body #1
```

<!-- test: rc-closure-env-freed -->
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
alloc ClosureEnv #1 rc=0 size=8 [ownership-edge-cases.main]
incref ClosureEnv #1 rc=1 [ownership-edge-cases.main]
decref ClosureEnv #1 rc=0 [ownership-edge-cases.main]
  free ClosureEnv #1
```

<!-- test: rc-closure-captures-struct -->
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
alloc Config #1 rc=0 size=8 [ownership-edge-cases.main]
incref Config #1 rc=1 [ownership-edge-cases.main]
alloc ClosureEnv #2 rc=0 size=8 [ownership-edge-cases.main]
incref ClosureEnv #2 rc=1 [ownership-edge-cases.main]
decref Config #1 rc=0 [ownership-edge-cases.main]
  free Config #1
decref ClosureEnv #2 rc=0 [ownership-edge-cases.main]
  free ClosureEnv #2
```

<!-- test: rc-error-path-cleanup -->
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
alloc __ManagedMemory_Item #1 rc=0 size=32 [ownership-edge-cases.main]
alloc ItemArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Item #1 rc=1 [ownership-edge-cases.main]
incref ItemArray #2 rc=1 [ownership-edge-cases.main]
alloc Item #3 rc=0 size=8 [ownership-edge-cases.main]
incref Item #3 rc=1 [ownership-edge-cases.main]
incref Item #3 rc=2 [ownership-edge-cases.main]
decref Item #3 rc=1 [ownership-edge-cases.main]
decref Item #3 rc=0 [ownership-edge-cases.main]
  free Item #3
decref ItemArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
  free __ManagedMemory_Item #1
  free ItemArray #2
```

<!-- test: rc-managed-list-insert-incref -->
<!-- MmTrace -->
Inserting a struct into a managed list increfs the value; the node holds the reference.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
  export var id Integer
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
  var managedList = TokenManagedList.create()
  var t = Token{id: 7}
  var node = managedList.insertFirst(t)
  return node.value().id
end 'main'
```
```exitcode
7
```
```stderr
alloc TokenManagedList #1 rc=0 size=32 [ownership-edge-cases.main]
incref TokenManagedList #1 rc=1 [ownership-edge-cases.main]
alloc Token #2 rc=0 size=8 [ownership-edge-cases.main]
incref Token #2 rc=1 [ownership-edge-cases.main]
alloc __ManagedListNode #3 rc=0 size=32 [ownership-edge-cases.main]
incref Token #2 rc=2 [ownership-edge-cases.main]
incref __ManagedListNode #3 rc=1 [managed_list_insert]
incref __ManagedListNode #3 rc=2 [ownership-edge-cases.main]
decref __ManagedListNode #3 rc=1 [ownership-edge-cases.main]
decref Token #2 rc=1 [ownership-edge-cases.main]
decref TokenManagedList #1 rc=0 [ownership-edge-cases.main]
decref Token #2 rc=0 [managed_list_clear]
  free Token #2
decref __ManagedListNode #3 rc=0 [managed_list_clear]
  free __ManagedListNode #3
  free TokenManagedList #1
```

<!-- test: rc-managed-list-remove-decrefs -->
<!-- MmTrace -->
Removing a node from a managed list transfers ownership; value is freed when the result var leaves scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
  export var id Integer
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
  var managedList = TokenManagedList.create()
  var node = managedList.insertFirst(Token{id: 9})
  var removed = managedList.remove(node)
  return removed.id + managedList.count()
end 'main'
```
```exitcode
9
```
```stderr
alloc TokenManagedList #1 rc=0 size=32 [ownership-edge-cases.main]
incref TokenManagedList #1 rc=1 [ownership-edge-cases.main]
alloc Token #2 rc=0 size=8 [ownership-edge-cases.main]
incref Token #2 rc=1 [ownership-edge-cases.main]
alloc __ManagedListNode #3 rc=0 size=32 [ownership-edge-cases.main]
incref Token #2 rc=2 [ownership-edge-cases.main]
incref __ManagedListNode #3 rc=1 [managed_list_insert]
incref __ManagedListNode #3 rc=2 [ownership-edge-cases.main]
decref __ManagedListNode #3 rc=1 [ownership-edge-cases.main]
decref Token #2 rc=1 [ownership-edge-cases.main]
decref __ManagedListNode #3 rc=0 [ownership-edge-cases.main]
  free __ManagedListNode #3
incref Token #2 rc=2 [ownership-edge-cases.main]
decref Token #2 rc=1 [ownership-edge-cases.main]
decref TokenManagedList #1 rc=0 [ownership-edge-cases.main]
  free TokenManagedList #1
decref Token #2 rc=0 [ownership-edge-cases.main]
  free Token #2
```

<!-- test: rc-managed-list-clear-decrefs-all -->
<!-- MmTrace -->
Clearing a managed list decrefs every node value; all values freed when rc hits 0.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
  export var id Integer
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
  var managedList = TokenManagedList.create()
  managedList.insertLast(Token{id: 1})
  managedList.insertLast(Token{id: 2})
  managedList.insertLast(Token{id: 3})
  managedList.clear()
  return managedList.count()
end 'main'
```
```exitcode
0
```
```stderr
alloc TokenManagedList #1 rc=0 size=32 [ownership-edge-cases.main]
incref TokenManagedList #1 rc=1 [ownership-edge-cases.main]
alloc Token #2 rc=0 size=8 [ownership-edge-cases.main]
incref Token #2 rc=1 [ownership-edge-cases.main]
alloc __ManagedListNode #3 rc=0 size=32 [ownership-edge-cases.main]
incref Token #2 rc=2 [ownership-edge-cases.main]
incref __ManagedListNode #3 rc=1 [managed_list_insert]
alloc Token #4 rc=0 size=8 [ownership-edge-cases.main]
incref Token #4 rc=1 [ownership-edge-cases.main]
alloc __ManagedListNode #5 rc=0 size=32 [ownership-edge-cases.main]
incref Token #4 rc=2 [ownership-edge-cases.main]
incref __ManagedListNode #5 rc=1 [managed_list_insert]
alloc Token #6 rc=0 size=8 [ownership-edge-cases.main]
incref Token #6 rc=1 [ownership-edge-cases.main]
alloc __ManagedListNode #7 rc=0 size=32 [ownership-edge-cases.main]
incref Token #6 rc=2 [ownership-edge-cases.main]
incref __ManagedListNode #7 rc=1 [managed_list_insert]
decref Token #2 rc=1 [managed_list_clear]
decref __ManagedListNode #3 rc=0 [managed_list_clear]
  free __ManagedListNode #3
decref Token #4 rc=1 [managed_list_clear]
decref __ManagedListNode #5 rc=0 [managed_list_clear]
  free __ManagedListNode #5
decref Token #6 rc=1 [managed_list_clear]
decref __ManagedListNode #7 rc=0 [managed_list_clear]
  free __ManagedListNode #7
decref TokenManagedList #1 rc=0 [ownership-edge-cases.main]
  free TokenManagedList #1
decref Token #2 rc=0 [ownership-edge-cases.main]
  free Token #2
decref Token #4 rc=0 [ownership-edge-cases.main]
  free Token #4
decref Token #6 rc=0 [ownership-edge-cases.main]
  free Token #6
```

<!-- test: rc-managed-list-node-set-value-decrefs-old -->
<!-- MmTrace -->
Calling `setValue` on a managed list node decrefs the old value and increfs the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
  export var id Integer
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
  var managedList = TokenManagedList.create()
  var node = managedList.insertFirst(Token{id: 1})
  node.setValue(Token{id: 99})
  return node.value().id
end 'main'
```
```exitcode
99
```
```stderr
alloc TokenManagedList #1 rc=0 size=32 [ownership-edge-cases.main]
incref TokenManagedList #1 rc=1 [ownership-edge-cases.main]
alloc Token #2 rc=0 size=8 [ownership-edge-cases.main]
incref Token #2 rc=1 [ownership-edge-cases.main]
alloc __ManagedListNode #3 rc=0 size=32 [ownership-edge-cases.main]
incref Token #2 rc=2 [ownership-edge-cases.main]
incref __ManagedListNode #3 rc=1 [managed_list_insert]
incref __ManagedListNode #3 rc=2 [ownership-edge-cases.main]
alloc Token #4 rc=0 size=8 [ownership-edge-cases.main]
incref Token #4 rc=1 [ownership-edge-cases.main]
decref Token #2 rc=1 [ownership-edge-cases.main]
incref Token #4 rc=2 [ownership-edge-cases.main]
decref __ManagedListNode #3 rc=1 [ownership-edge-cases.main]
decref TokenManagedList #1 rc=0 [ownership-edge-cases.main]
decref Token #4 rc=1 [managed_list_clear]
decref __ManagedListNode #3 rc=0 [managed_list_clear]
  free __ManagedListNode #3
  free TokenManagedList #1
decref Token #2 rc=0 [ownership-edge-cases.main]
  free Token #2
decref Token #4 rc=0 [ownership-edge-cases.main]
  free Token #4
```

<!-- test: rc-for-in-elem-decrefed -->
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
alloc __ManagedMemory_Score #1 rc=0 size=32 [ownership-edge-cases.main]
alloc ScoreArray #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Score #1 rc=1 [ownership-edge-cases.main]
incref ScoreArray #2 rc=1 [ownership-edge-cases.main]
alloc Score #3 rc=0 size=8 [ownership-edge-cases.main]
incref Score #3 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Score #1 rc=1 size=32
incref Score #3 rc=2 [ScoreArray.push]
alloc Score #4 rc=0 size=8 [ownership-edge-cases.main]
incref Score #4 rc=1 [ownership-edge-cases.main]
incref Score #4 rc=2 [ScoreArray.push]
alloc Score #5 rc=0 size=8 [ownership-edge-cases.main]
incref Score #5 rc=1 [ownership-edge-cases.main]
incref Score #5 rc=2 [ScoreArray.push]
incref ScoreArray #2 rc=2 [ownership-edge-cases.main]
incref Score #3 rc=3 [ScoreArray.next]
transfer Score #3 rc=3 [ScoreArray.next]
decref Score #3 rc=2 [ownership-edge-cases.main]
decref Score #3 rc=1 [ownership-edge-cases.main]
decref Score #4 rc=1 [ownership-edge-cases.main]
decref Score #5 rc=1 [ownership-edge-cases.main]
incref Score #4 rc=2 [ScoreArray.next]
transfer Score #4 rc=2 [ScoreArray.next]
decref Score #4 rc=1 [ownership-edge-cases.main]
incref Score #5 rc=2 [ScoreArray.next]
transfer Score #5 rc=2 [ScoreArray.next]
decref Score #5 rc=1 [ownership-edge-cases.main]
decref ScoreArray #2 rc=1 [ownership-edge-cases.main]
decref ScoreArray #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_Score #1 rc=0 [~ScoreArray]
decref Score #3 rc=0 [~ManagedElements]
  free Score #3
decref Score #4 rc=0 [~ManagedElements]
  free Score #4
decref Score #5 rc=0 [~ManagedElements]
  free Score #5
  free __ManagedMemory_Score #1
  free ScoreArray #2
```

<!-- test: rc-multiple-aliases-freed-once -->
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
alloc Data #1 rc=0 size=8 [ownership-edge-cases.main]
incref Data #1 rc=1 [ownership-edge-cases.main]
incref Data #1 rc=2 [ownership-edge-cases.main]
incref Data #1 rc=3 [ownership-edge-cases.main]
decref Data #1 rc=2 [ownership-edge-cases.main]
decref Data #1 rc=1 [ownership-edge-cases.main]
decref Data #1 rc=0 [ownership-edge-cases.main]
  free Data #1
```

<!-- test: rc-deep-container-of-containers -->
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
alloc __ManagedMemory_CellArray #1 rc=0 size=32 [ownership-edge-cases.main]
alloc Grid #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_CellArray #1 rc=1 [ownership-edge-cases.main]
incref Grid #2 rc=1 [ownership-edge-cases.main]
alloc __ManagedMemory_Cell #3 rc=0 size=32 [ownership-edge-cases.main]
alloc CellArray #4 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Cell #3 rc=1 [ownership-edge-cases.main]
incref CellArray #4 rc=1 [ownership-edge-cases.main]
alloc Cell #5 rc=0 size=8 [ownership-edge-cases.main]
incref Cell #5 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Cell #3 rc=1 size=32
incref Cell #5 rc=2 [CellArray.push]
alloc Cell #6 rc=0 size=8 [ownership-edge-cases.main]
incref Cell #6 rc=1 [ownership-edge-cases.main]
incref Cell #6 rc=2 [CellArray.push]
alloc __ManagedMemory_Cell #7 rc=0 size=32 [ownership-edge-cases.main]
alloc CellArray #8 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedMemory_Cell #7 rc=1 [ownership-edge-cases.main]
incref CellArray #8 rc=1 [ownership-edge-cases.main]
alloc Cell #9 rc=0 size=8 [ownership-edge-cases.main]
incref Cell #9 rc=1 [ownership-edge-cases.main]
realloc __ManagedMemory_Cell #7 rc=1 size=32
incref Cell #9 rc=2 [CellArray.push]
realloc __ManagedMemory_CellArray #1 rc=1 size=32
incref CellArray #4 rc=2 [Grid.push]
incref CellArray #8 rc=2 [Grid.push]
decref CellArray #8 rc=1 [ownership-edge-cases.main]
decref CellArray #4 rc=1 [ownership-edge-cases.main]
decref Grid #2 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory_CellArray #1 rc=0 [~Grid]
decref CellArray #4 rc=0 [~ManagedElements]
decref __ManagedMemory_Cell #3 rc=0 [~CellArray]
decref Cell #5 rc=1 [~ManagedElements]
decref Cell #6 rc=1 [~ManagedElements]
  free __ManagedMemory_Cell #3
  free CellArray #4
decref CellArray #8 rc=0 [~ManagedElements]
decref __ManagedMemory_Cell #7 rc=0 [~CellArray]
decref Cell #9 rc=1 [~ManagedElements]
  free __ManagedMemory_Cell #7
  free CellArray #8
  free __ManagedMemory_CellArray #1
  free Grid #2
decref Cell #5 rc=0 [ownership-edge-cases.main]
  free Cell #5
decref Cell #6 rc=0 [ownership-edge-cases.main]
  free Cell #6
decref Cell #9 rc=0 [ownership-edge-cases.main]
  free Cell #9
```

<!-- test: rc-struct-with-array-field-freed -->
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
```
```stderr
alloc __ManagedMemory_Entry #1 rc=0 size=32 [ownership-edge-cases.fill]
alloc EntryArray #2 rc=0 size=16 [ownership-edge-cases.fill]
incref __ManagedMemory_Entry #1 rc=1 [ownership-edge-cases.fill]
alloc Bucket #3 rc=0 size=8 [ownership-edge-cases.fill]
incref EntryArray #2 rc=1 [ownership-edge-cases.fill]
incref Bucket #3 rc=1 [ownership-edge-cases.fill]
alloc Entry #4 rc=0 size=8 [ownership-edge-cases.fill]
incref Entry #4 rc=1 [ownership-edge-cases.fill]
realloc __ManagedMemory_Entry #1 rc=1 size=32
incref Entry #4 rc=2 [EntryArray.push]
alloc Entry #5 rc=0 size=8 [ownership-edge-cases.fill]
incref Entry #5 rc=1 [ownership-edge-cases.fill]
incref Entry #5 rc=2 [EntryArray.push]
decref Bucket #3 rc=0 [ownership-edge-cases.fill]
decref EntryArray #2 rc=0 [~Bucket]
decref __ManagedMemory_Entry #1 rc=0 [~EntryArray]
decref Entry #4 rc=1 [~ManagedElements]
decref Entry #5 rc=1 [~ManagedElements]
  free __ManagedMemory_Entry #1
  free EntryArray #2
  free Bucket #3
decref Entry #4 rc=0 [ownership-edge-cases.fill]
  free Entry #4
decref Entry #5 rc=0 [ownership-edge-cases.fill]
  free Entry #5
```

<!-- test: rc-return-struct-literal -->
<!-- MmTrace -->
Returning a struct literal directly from a function must transfer ownership at rc=1.
The callee constructs the struct (rc=0), increfs it for the assignment, and transfers
ownership to the caller via KeepVars. The caller must not incref again.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
  export var a Integer
  export var b Integer
end 'Pair'

function makePair(x Integer, y Integer) returns Pair
  return Pair{a: x, b: y}
end 'makePair'

function main() returns ExitCode
  var p = makePair(x: 3, y: 7)
  return p.a + p.b
end 'main'
```
```exitcode
10
```
```stderr
alloc Pair #1 rc=0 size=16 [ownership-edge-cases.makePair]
incref Pair #1 rc=1 [ownership-edge-cases.makePair]
transfer Pair #1 rc=1 [ownership-edge-cases.makePair]
decref Pair #1 rc=0 [ownership-edge-cases.main]
  free Pair #1
```

<!-- test: rc-return-struct-with-managed-field -->
<!-- MmTrace -->
Returning a struct whose field is a shared managed reference. The callee increfs
the shared field when storing it, and transfers the outer struct at rc=1.
The caller must decref the outer struct, which cascades to decref the managed field.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
  export var value Integer
end 'Inner'

type Wrapper
  export var inner Inner
end 'Wrapper'

function wrap(i Inner) returns Wrapper
  return Wrapper{inner: i}
end 'wrap'

function main() returns ExitCode
  var i = Inner{value: 5}
  var w = wrap(i: i)
  return w.inner.value
end 'main'
```
```exitcode
5
```
```stderr
alloc Inner #1 rc=0 size=8 [ownership-edge-cases.main]
incref Inner #1 rc=1 [ownership-edge-cases.main]
alloc Wrapper #2 rc=0 size=8 [ownership-edge-cases.wrap]
incref Inner #1 rc=2 [ownership-edge-cases.wrap]
incref Wrapper #2 rc=1 [ownership-edge-cases.wrap]
transfer Wrapper #2 rc=1 [ownership-edge-cases.wrap]
decref Wrapper #2 rc=0 [ownership-edge-cases.main]
decref Inner #1 rc=1 [~Wrapper]
  free Wrapper #2
decref Inner #1 rc=0 [ownership-edge-cases.main]
  free Inner #1
```

<!-- test: rc-list-scope-cleanup -->
<!-- MmTrace -->
List (struct owning a managed list field) must walk and decref managed list node values on scope exit.
```maxon
typealias StringList = List with String

function main() returns ExitCode
  var list = StringList{}
  list.append("hello")
  return 0
end 'main'
```
```exitcode
0
```
```stderr
alloc __ManagedList_String #1 rc=0 size=32 [ownership-edge-cases.main]
alloc StringList #2 rc=0 size=16 [ownership-edge-cases.main]
incref __ManagedList_String #1 rc=1 [ownership-edge-cases.main]
incref StringList #2 rc=1 [ownership-edge-cases.main]
alloc String #3 rc=0 size=16 [ownership-edge-cases.main]
alloc __ManagedMemory #4 rc=0 size=32 [ownership-edge-cases.main]
incref __ManagedMemory #4 rc=1 [ownership-edge-cases.main]
incref String #3 rc=1 [ownership-edge-cases.main]
alloc __ManagedListNode #5 rc=0 size=32 [StringList.append]
incref String #3 rc=2 [StringList.append]
incref __ManagedListNode #5 rc=1 [managed_list_insert]
decref String #3 rc=1 [ownership-edge-cases.main]
decref StringList #2 rc=0 [ownership-edge-cases.main]
decref __ManagedList_String #1 rc=0 [~StringList]
decref String #3 rc=0 [managed_list_clear]
decref __ManagedMemory #4 rc=0 [~String]
  free __ManagedMemory #4
  free String #3
decref __ManagedListNode #5 rc=0 [managed_list_clear]
  free __ManagedListNode #5
  free __ManagedList_String #1
  free StringList #2
```

<!-- test: match-string-pattern-cleanup -->
<!-- MmTrace -->
Match pattern string literals must be freed after comparison, even when a case matches.
```maxon
function main() returns ExitCode
  var name = "alice"
  match name 'greet'
    "alice" then return 1
    "bob" then return 2
    default then return 0
  end 'greet'
end 'main'
```
```exitcode
1
```
```stderr
alloc String #1 rc=0 size=16 [ownership-edge-cases.main]
alloc __ManagedMemory #2 rc=0 size=32 [ownership-edge-cases.main]
incref __ManagedMemory #2 rc=1 [ownership-edge-cases.main]
incref String #1 rc=1 [ownership-edge-cases.main]
incref String #1 rc=2 [ownership-edge-cases.main]
incref String #1 rc=3 [ownership-edge-cases.main]
alloc String #3 rc=0 size=16 [ownership-edge-cases.main]
alloc __ManagedMemory #4 rc=0 size=32 [ownership-edge-cases.main]
incref __ManagedMemory #4 rc=1 [ownership-edge-cases.main]
incref String #3 rc=1 [ownership-edge-cases.main]
decref String #3 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory #4 rc=0 [~String]
  free __ManagedMemory #4
  free String #3
decref String #1 rc=2 [ownership-edge-cases.main]
decref String #1 rc=1 [ownership-edge-cases.main]
decref String #1 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory #2 rc=0 [~String]
  free __ManagedMemory #2
  free String #1
```

<!-- test: rc-char-single-alloc-freed -->
<!-- MmTrace -->
Single character allocated and freed in the same function scope; Character + child __ManagedMemory both cleaned up.
```maxon
function main() returns ExitCode
  var c = 'A'
  return c.byteLength()
end 'main'
```
```exitcode
1
```
```stderr
alloc Character #1 rc=0 size=16 [ownership-edge-cases.main]
alloc __ManagedMemory #2 rc=0 size=32 [ownership-edge-cases.main]
incref __ManagedMemory #2 rc=1 [ownership-edge-cases.main]
incref Character #1 rc=1 [ownership-edge-cases.main]
incref Character #1 rc=2 [ownership-edge-cases.main]
decref Character #1 rc=1 [ownership-edge-cases.main]
decref Character #1 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory #2 rc=0 [~Character]
  free __ManagedMemory #2
  free Character #1
```

<!-- test: rc-char-alias-incref -->
<!-- MmTrace -->
Aliasing a character increfs it; both variables share the same Character object.
```maxon
function main() returns ExitCode
  var a = 'X'
  let b = a
  return a.byteLength() + b.byteLength()
end 'main'
```
```exitcode
2
```
```stderr
alloc Character #1 rc=0 size=16 [ownership-edge-cases.main]
alloc __ManagedMemory #2 rc=0 size=32 [ownership-edge-cases.main]
incref __ManagedMemory #2 rc=1 [ownership-edge-cases.main]
incref Character #1 rc=1 [ownership-edge-cases.main]
incref Character #1 rc=2 [ownership-edge-cases.main]
incref Character #1 rc=3 [ownership-edge-cases.main]
decref Character #1 rc=2 [ownership-edge-cases.main]
decref Character #1 rc=1 [ownership-edge-cases.main]
decref Character #1 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory #2 rc=0 [~Character]
  free __ManagedMemory #2
  free Character #1
```

<!-- test: rc-char-reassign-decrefs-old -->
<!-- MmTrace -->
Reassigning a character var decrefs and frees the old Character (with its managed child) before storing the new one.
```maxon
function main() returns ExitCode
  var c = 'A'
  c = 'B'
  return c.byteLength()
end 'main'
```
```exitcode
1
```
```stderr
alloc Character #1 rc=0 size=16 [ownership-edge-cases.main]
alloc __ManagedMemory #2 rc=0 size=32 [ownership-edge-cases.main]
incref __ManagedMemory #2 rc=1 [ownership-edge-cases.main]
incref Character #1 rc=1 [ownership-edge-cases.main]
incref Character #1 rc=2 [ownership-edge-cases.main]
alloc Character #3 rc=0 size=16 [ownership-edge-cases.main]
alloc __ManagedMemory #4 rc=0 size=32 [ownership-edge-cases.main]
incref __ManagedMemory #4 rc=1 [ownership-edge-cases.main]
incref Character #3 rc=1 [ownership-edge-cases.main]
decref Character #1 rc=1 [ownership-edge-cases.main]
incref Character #3 rc=2 [ownership-edge-cases.main]
decref Character #3 rc=1 [ownership-edge-cases.main]
decref Character #1 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory #2 rc=0 [~Character]
  free __ManagedMemory #2
  free Character #1
decref Character #3 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory #4 rc=0 [~Character]
  free __ManagedMemory #4
  free Character #3
```

<!-- test: rc-char-return-transfers-ownership -->
<!-- MmTrace -->
Returning a character from a function transfers ownership to the caller.
```maxon
function makeChar() returns Character
  return 'Z'
end 'makeChar'

function main() returns ExitCode
  var c = makeChar()
  return c.byteLength()
end 'main'
```
```exitcode
1
```
```stderr
alloc Character #1 rc=0 size=16 [ownership-edge-cases.makeChar]
alloc __ManagedMemory #2 rc=0 size=32 [ownership-edge-cases.makeChar]
incref __ManagedMemory #2 rc=1 [ownership-edge-cases.makeChar]
incref Character #1 rc=1 [ownership-edge-cases.makeChar]
transfer Character #1 rc=1 [ownership-edge-cases.makeChar]
decref Character #1 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory #2 rc=0 [~Character]
  free __ManagedMemory #2
  free Character #1
```

<!-- test: rc-char-inner-block-freed -->
<!-- MmTrace -->
A character created in an inner if-block is freed when that block exits.
```maxon
function main() returns ExitCode
  var result = 0
  if true 'inner'
    var c = 'Q'
    result = c.byteLength()
  end 'inner'
  return result
end 'main'
```
```exitcode
1
```
```stderr
alloc Character #1 rc=0 size=16 [ownership-edge-cases.main]
alloc __ManagedMemory #2 rc=0 size=32 [ownership-edge-cases.main]
incref __ManagedMemory #2 rc=1 [ownership-edge-cases.main]
incref Character #1 rc=1 [ownership-edge-cases.main]
incref Character #1 rc=2 [ownership-edge-cases.main]
decref Character #1 rc=1 [ownership-edge-cases.main]
decref Character #1 rc=0 [ownership-edge-cases.main]
decref __ManagedMemory #2 rc=0 [~Character]
  free __ManagedMemory #2
  free Character #1
```

<!-- test: rc-tuple-primitive-freed -->
<!-- MmTrace -->
A tuple of primitives is heap-allocated and freed at scope exit.
```maxon
function main() returns ExitCode
  var t = (10, 32)
  return t.0
end 'main'
```
```exitcode
10
```
```stderr
alloc __Tuple_i64_i64 #1 rc=0 size=16 [ownership-edge-cases.main]
incref __Tuple_i64_i64 #1 rc=1 [ownership-edge-cases.main]
decref __Tuple_i64_i64 #1 rc=0 [ownership-edge-cases.main]
  free __Tuple_i64_i64 #1
```

<!-- test: rc-tuple-alias-incref -->
<!-- MmTrace -->
Aliasing a tuple increfs it; both variables share the same tuple object.
```maxon
function main() returns ExitCode
  var a = (3, 7)
  let b = a
  return b.0 + b.1
end 'main'
```
```exitcode
10
```
```stderr
alloc __Tuple_i64_i64 #1 rc=0 size=16 [ownership-edge-cases.main]
incref __Tuple_i64_i64 #1 rc=1 [ownership-edge-cases.main]
incref __Tuple_i64_i64 #1 rc=2 [ownership-edge-cases.main]
decref __Tuple_i64_i64 #1 rc=1 [ownership-edge-cases.main]
decref __Tuple_i64_i64 #1 rc=0 [ownership-edge-cases.main]
  free __Tuple_i64_i64 #1
```

<!-- test: rc-tuple-reassign-decrefs-old -->
<!-- MmTrace -->
Reassigning a tuple var decrefs the old tuple before storing the new one.
```maxon
function main() returns ExitCode
  var t = (1, 2)
  t = (3, 4)
  return t.0 + t.1
end 'main'
```
```exitcode
7
```
```stderr
alloc __Tuple_i64_i64 #1 rc=0 size=16 [ownership-edge-cases.main]
incref __Tuple_i64_i64 #1 rc=1 [ownership-edge-cases.main]
alloc __Tuple_i64_i64 #2 rc=0 size=16 [ownership-edge-cases.main]
incref __Tuple_i64_i64 #2 rc=1 [ownership-edge-cases.main]
decref __Tuple_i64_i64 #1 rc=0 [ownership-edge-cases.main]
  free __Tuple_i64_i64 #1
incref __Tuple_i64_i64 #2 rc=2 [ownership-edge-cases.main]
decref __Tuple_i64_i64 #2 rc=1 [ownership-edge-cases.main]
decref __Tuple_i64_i64 #2 rc=0 [ownership-edge-cases.main]
  free __Tuple_i64_i64 #2
```

<!-- test: rc-tuple-with-string-freed -->
<!-- MmTrace -->
A tuple containing a managed type (String); the destructor must cascade to decref the String field.
```maxon
function main() returns ExitCode
  var t = (42, "hello")
  return t.0
end 'main'
```
```exitcode
42
```
```stderr
alloc String #1 rc=0 size=16 [ownership-edge-cases.main]
alloc __ManagedMemory #2 rc=0 size=32 [ownership-edge-cases.main]
incref __ManagedMemory #2 rc=1 [ownership-edge-cases.main]
incref String #1 rc=1 [ownership-edge-cases.main]
alloc __Tuple_i64_String #3 rc=0 size=16 [ownership-edge-cases.main]
incref String #1 rc=2 [ownership-edge-cases.main]
incref __Tuple_i64_String #3 rc=1 [ownership-edge-cases.main]
decref String #1 rc=1 [ownership-edge-cases.main]
decref __Tuple_i64_String #3 rc=0 [ownership-edge-cases.main]
decref String #1 rc=0 [~__Tuple_i64_String]
decref __ManagedMemory #2 rc=0 [~String]
  free __ManagedMemory #2
  free String #1
  free __Tuple_i64_String #3
```

<!-- test: rc-tuple-return-transfers-ownership -->
<!-- MmTrace -->
Returning a tuple from a function transfers ownership to the caller.
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
  return (a, b)
end 'makePair'

function main() returns ExitCode
  var t = makePair(a: 5, b: 3)
  return t.0 + t.1
end 'main'
```
```exitcode
8
```
```stderr
alloc __Tuple_i64_i64 #1 rc=0 size=16 [ownership-edge-cases.makePair]
incref __Tuple_i64_i64 #1 rc=1 [ownership-edge-cases.makePair]
transfer __Tuple_i64_i64 #1 rc=1 [ownership-edge-cases.makePair]
decref __Tuple_i64_i64 #1 rc=0 [ownership-edge-cases.main]
  free __Tuple_i64_i64 #1
```

<!-- test: rc-tuple-destructuring-cleanup -->
<!-- MmTrace -->
Destructuring a tuple frees the tuple wrapper while the bindings remain live.
```maxon
function main() returns ExitCode
  var t = (10, 20)
  var (x, y) = t
  return x + y
end 'main'
```
```exitcode
30
```
```stderr
alloc __Tuple_i64_i64 #1 rc=0 size=16 [ownership-edge-cases.main]
incref __Tuple_i64_i64 #1 rc=1 [ownership-edge-cases.main]
incref __Tuple_i64_i64 #1 rc=2 [ownership-edge-cases.main]
decref __Tuple_i64_i64 #1 rc=1 [ownership-edge-cases.main]
decref __Tuple_i64_i64 #1 rc=0 [ownership-edge-cases.main]
  free __Tuple_i64_i64 #1
```

<!-- test: rc-tuple-with-struct-freed -->
<!-- MmTrace -->
A tuple containing a user-defined struct; the destructor cascades through the tuple into the struct.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var t = (1, Point{x: 10, y: 20})
  return t.0
end 'main'
```
```exitcode
1
```
```stderr
alloc Point #1 rc=0 size=16 [ownership-edge-cases.main]
alloc __Tuple_i64_Point #2 rc=0 size=16 [ownership-edge-cases.main]
incref Point #1 rc=1 [ownership-edge-cases.main]
incref __Tuple_i64_Point #2 rc=1 [ownership-edge-cases.main]
decref __Tuple_i64_Point #2 rc=0 [ownership-edge-cases.main]
decref Point #1 rc=0 [~__Tuple_i64_Point]
  free Point #1
  free __Tuple_i64_Point #2
```

<!-- test: rc-struct-literal-as-function-arg -->
Passing a struct literal directly as a function argument must still free the struct after use. Currently leaks (exit 101).
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function acceptPoint(p Point) returns Integer
  return p.x + p.y
end 'acceptPoint'

function main() returns ExitCode
  return acceptPoint(Point{x: 3, y: 4})
end 'main'
```
```exitcode
7
```

<!-- test: rc-tuple-return-destructure-no-crash -->
Returning a tuple from a function and destructuring it must not crash. Currently the cleanup code attempts to decref the already-freed tuple, causing a segfault.
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
  return (a, b)
end 'makePair'

function main() returns ExitCode
  var (x, y) = makePair(10, b: 32)
  return x + y
end 'main'
```
```exitcode
42
```

<!-- test: rc-enum-char-rawvalue-from-function -->
Returning an enum's char rawValue through a function must not underflow the refcount. Currently the returned value is treated as a managed allocation when it's actually a raw constant, causing refcount underflow.
```maxon
enum Grade
  excellent = 'A'
  good = 'B'
  average = 'C'
end 'Grade'

function getLetter(g Grade) returns Character
  return g.rawValue
end 'getLetter'

function main() returns ExitCode
  var grade = Grade.good
  var letter = getLetter(grade)
  if letter == 'B' 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-enum-name-from-function -->
Returning an enum's .name (String) through a function must not underflow the refcount. Currently the returned raw constant string is decremented as if it were a managed allocation.
```maxon
enum Direction
  north
  south
  east
  west
end 'Direction'

function getName(d Direction) returns String
  return d.name
end 'getName'

function main() returns ExitCode
  var d = Direction.west
  var n = getName(d)
  if n == "west" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-enum-string-rawvalue-from-function -->
Returning a string-backed enum's rawValue through a function must not underflow the refcount. Same root cause as the char variant: raw constant treated as managed allocation.
```maxon
enum Planet
  earth = "Earth"
  mars = "Mars"
  venus = "Venus"
end 'Planet'

function getName(p Planet) returns String
  return p.rawValue
end 'getName'

function main() returns ExitCode
  var p = Planet.mars
  var n = getName(p)
  if n == "Mars" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-discarded-self-return -->
When a self-returning method's result is discarded, the refcount must remain balanced. Currently the cleanup code double-decrefs the struct, causing a segfault.
```maxon
typealias Count = int(i64.min to i64.max)

type Counter
  export var value Count

  function increment() returns Counter
    value = value + 1
    return self
  end 'increment'
end 'Counter'

function main() returns ExitCode
  var c = Counter{value: 0}
  c.increment()
  return c.value
end 'main'
```
```exitcode
1
```

<!-- test: rc-borrow-field-from-param -->
Extracting and returning a struct field from a borrowed parameter must not crash. Currently the cleanup code decrefs the returned borrowed field incorrectly, causing a segfault after printing the correct output.
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
  return result.value
end 'main'
```
```exitcode
42
```

<!-- test: rc-char-to-string-interpolation -->
Interpolating a character into a string must not leak. Currently the intermediate ManagedMemory allocation from the Character is not freed.
```maxon
function main() returns ExitCode
  var c = 'A'
  var s = "{c}"
  print(s)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
A
```

<!-- test: rc-match-char-range-cleanup -->
Using character range patterns in a match statement must clean up all allocated Characters. Currently the range bound Characters leak.
```maxon
function main() returns ExitCode
  var c = 'G'
  match c 'classify'
    'a' to 'z' then return 1
    'A' to 'Z' then return 2
    '0' to '9' then return 3
    default then return 0
  end 'classify'
end 'main'
```
```exitcode
2
```

<!-- test: rc-string-backed-enum-compare -->
Comparing two string-backed enum values must not leak. Currently the Character/String allocations for enum case values are not freed.
```maxon
enum ContentType
  json = "application/json"
  html = "text/html"
  plain = "text/plain"
end 'ContentType'

function main() returns ExitCode
  var ct = ContentType.json
  if ct == ContentType.json 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-char-backed-enum-compare -->
Comparing two char-backed enum values must not leak. Currently the Character allocations for enum case values are not freed.
```maxon
enum Escape
  newline = '\n'
  tab = '\t'
end 'Escape'

function main() returns ExitCode
  var e = Escape.newline
  if e == Escape.newline 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-nested-struct-clone-no-leak -->
Cloning a struct with a nested struct field must not leak the inner clone. Currently the cloned Inner's refcount is 1 when freed via Outer cascade, leaving 1 leaked allocation.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
  export var value Integer
end 'Inner'

type Outer
  export var a Inner
  export var b Integer
end 'Outer'

function main() returns ExitCode
  var x = Outer{a: Inner{value: 42}, b: 10}
  var y = x.clone()
  y.a.value = 99
  return x.a.value
end 'main'
```
```exitcode
42
```

<!-- test: rc-string-clone-no-leak -->
Cloning a string must not leak internal Slice/ManagedMemory allocations. Currently String.clone leaks 2 allocations (the Slice and its buffer).
```maxon
function main() returns ExitCode
  var a = "hello"
  var b = a.clone()
  print(b)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: rc-string-replace-no-leak -->
String.replace must not leak internal working allocations. Currently leaks 2 allocations (ManagedMemory buffers from the replace implementation).
```maxon
function main() returns ExitCode
  var s = "hello world"
  var result = s.replace("world", with: "there")
  print(result)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello there
```

<!-- test: rc-string-replacefirst-no-leak -->
String.replaceFirst must not leak internal working allocations. The intermediate ManagedMemory and Buffer created during the replacement must be freed.
```maxon
function main() returns ExitCode
  var s = "hello world"
  var result = s.replaceFirst("o", with: "0")
  print(result)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hell0 world
```

<!-- test: rc-string-concat-loop-no-leak -->
Repeatedly concatenating strings in a loop must free intermediate ManagedMemory/Buffer pairs. Each concat creates a new string; the old one must be fully freed including its managed backing storage.
```maxon
function main() returns ExitCode
  var s = ""
  var a = "x"
  var i = 0
  while i < 5 'loop'
    s = s.concat(a)
    i = i + 1
  end 'loop'
  return s.byteLength()
end 'main'
```
```exitcode
5
```

<!-- test: rc-string-slice-no-leak -->
String.slice must not leak internal allocations. The slice operation creates managed memory that must be properly tracked and freed.
```maxon
function main() returns ExitCode
  var s = "hello world"
  var start = s.startIndex()
  var spaceIdx = try s.findFirst(" ") otherwise s.endIndex()
  var sub = s.slice(start, endIndex: spaceIdx)
  print(sub)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: rc-enum-name-no-leak -->
Accessing enum .name must not leak. The getName function allocates a String wrapper around the name data; both the wrapper and its managed memory must be freed.
```maxon
enum Color
  Red
  Green
  Blue
end 'Color'

function main() returns ExitCode
  var c = Color.Green
  if c.name == "Green" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-enum-name-reassign-no-leak -->
Accessing enum .name after reassignment must not leak. Both the old and new enum name string allocations must be properly freed.
```maxon
enum Status
  pending
  active
  done
end 'Status'

function main() returns ExitCode
  var s = Status.pending
  s = Status.done
  if s.name == "done" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-array-of-structs-get-no-leak -->
Getting a struct from an array via try/otherwise must not leak. When the array is freed, its element destructors must decref all contained structs.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
  export var first Integer
  export var second Integer
end 'Pair'

function main() returns ExitCode
  var p = Pair{first: 10, second: 20}
  var arr = [p]
  var elem = try arr.get(0) otherwise Pair{first: 0, second: 0}
  return elem.first + elem.second
end 'main'
```
```exitcode
30
```

<!-- test: rc-array-of-structs-literal-no-leak -->
Creating an array literal of structs must not leak. All struct elements must be decreffed when the array is freed.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var p1 = Point{x: 1, y: 2}
  var p2 = Point{x: 3, y: 4}
  var points = [p1, p2]
  var pt0 = try points.get(0) otherwise Point{x: 0, y: 0}
  var pt1 = try points.get(1) otherwise Point{x: 0, y: 0}
  return pt0.x + pt1.y
end 'main'
```
```exitcode
5
```

<!-- test: rc-global-array-push-local-no-leak -->
Pushing a local struct into a global array must not leak. When the global array is cleaned up, all elements (including those pushed from other function scopes) must be freed.
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
  return elem.value
end 'main'
```
```exitcode
123
```

<!-- test: rc-global-array-push-remove-loop-no-leak -->
Pushing many structs into a global array and then removing them all must not leak. Each removed element must be properly decreffed.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

var globalArr = ItemArray{}

function main() returns ExitCode
  var i = 0
  while i < 10 'push'
    globalArr.push(Item{value: i})
    i = i + 1
  end 'push'
  var total = 0
  while globalArr.count() > 0 'remove'
    var elem = try globalArr.remove(0) otherwise 'err'
      return 99
    end 'err'
    total = total + elem.value
    i = i + 1
  end 'remove'
  return total
end 'main'
```
```exitcode
45
```

<!-- test: rc-struct-field-overwrite-in-if-no-leak -->
Assigning a new struct to a struct field inside an if block must decref the old value and not leak the old struct's managed children (e.g., arrays).
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

<!-- test: rc-map-string-keys-no-leak -->
A map with string keys must free all string key allocations when the map is destroyed. The string used as a key is increffed into the map's key array; when the map is freed, these strings must be decreffed.
```maxon
function main() returns ExitCode
    var m = ["hello": 42]
    return try m.get("hello") otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: rc-map-string-keys-multiple-no-leak -->
A map with multiple string keys must free all key and value allocations. Each insert increfs the key string; the map destructor must decref all of them.
```maxon
function main() returns ExitCode
    var m = ["a": 1, "b": 2, "c": 3]
    let a = try m.get("a") otherwise 0
    let b = try m.get("b") otherwise 0
    let c = try m.get("c") otherwise 0
    return a + b + c
end 'main'
```
```exitcode
6
```

<!-- test: rc-closure-capture-string-no-crash -->
A closure that captures a string variable must properly manage the string's refcount. The closure environment holds a reference to the string; when the environment is freed, it must decref the string without crashing.
```maxon
typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns String, x Integer) returns String
  return f(x)
end 'apply'

function main() returns ExitCode
  var prefix = "hello"
  var result = apply(f: (_ Integer) gives prefix, x: 0)
  print(result)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: rc-managed-list-remove-single-no-leak -->
Removing a node from a managed list must properly decref the node's value. The managed list node itself and the stored value must both be freed.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var value Integer
end 'Item'

typealias ItemManagedList = __ManagedList with Item

function main() returns ExitCode
  var managedList = ItemManagedList.create()
  var node = managedList.insertFirst(Item{value: 50})
  managedList.remove(node)
  return managedList.count()
end 'main'
```
```exitcode
0
```

<!-- test: rc-module-level-struct-nested-field-assign -->
Assigning to a nested field of a module-level struct variable must not leak. The struct access chain must properly manage refcounts for intermediate accesses.
```maxon
typealias SmallInt = int(0 to 255)

type Inner
    export var x SmallInt
end 'Inner'

type Outer
    export var inner Inner
end 'Outer'

var state = Outer{inner: Inner{x: 0}}

function main() returns ExitCode
    state.inner.x = 99
    return state.inner.x
end 'main'
```
```exitcode
99
```

<!-- test: rc-top-level-array-literal-no-leak -->
A module-level array literal must not leak. The array and its element storage must be freed during global cleanup.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var items = [10, 20, 30]

function main() returns ExitCode
  let a = try items.get(0) otherwise 0
  let b = try items.get(1) otherwise 0
  let c = try items.get(2) otherwise 0
  return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: rc-array-append-no-leak -->
Array.append must not leak. Appending one array to another must properly manage the element storage and not leak the source array's data.
```maxon
function main() returns ExitCode
  var a = [1, 2, 3]
  var b = [4, 5, 6]
  a.append(b)
  var sum = 0
  var i = 0
  while i < a.count() 'loop'
    sum = sum + (try a.get(i) otherwise 0)
    i = i + 1
  end 'loop'
  return sum
end 'main'
```
```exitcode
21
```

<!-- test: rc-struct-with-string-union-in-array -->
Pushing structs that contain unions with string payloads into an array must not leak. The union destructors must handle string payload cleanup during array destruction.
```maxon
export union QueryKey
    sourceFile(path String)
    allModule
end 'QueryKey'

export type Dependency
    export var key QueryKey
end 'Dependency'

typealias DependencyArray = Array with Dependency

function main() returns ExitCode
    var deps = DependencyArray{}
    deps.push(Dependency{key: QueryKey.sourceFile("test.maxon")})
    deps.push(Dependency{key: QueryKey.allModule})
    deps.push(Dependency{key: QueryKey.sourceFile("other.maxon")})
    return deps.count()
end 'main'
```
```exitcode
3
```

<!-- test: rc-custom-hashable-map-key-no-leak -->
A map using a custom Hashable struct as key must not leak. The map's internal arrays (keys, values, states) and all managed elements must be freed.
```maxon
typealias Integer = int(i64.min to i64.max)

type MyKey implements Hashable, Equatable
    var value Integer

    function hash() returns HashValue
        return self.value * 31
    end 'hash'

    function equals(other MyKey) returns bool
        return self.value == other.value
    end 'equals'
end 'MyKey'

typealias MyKeyMap = Map with (MyKey, Integer)

function main() returns ExitCode
    var m = MyKeyMap{}
    m.insert(key: MyKey{value: 1}, value: 42)
    return m.count()
end 'main'
```
```exitcode
1
```
