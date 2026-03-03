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
alloc Point #1 rc=0 [ownership-edge-cases.main]
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
alloc Box #1 rc=0 [ownership-edge-cases.main]
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
alloc Tag #1 rc=0 [ownership-edge-cases.main]
incref Tag #1 rc=1 [ownership-edge-cases.main]
alloc Tag #2 rc=0 [ownership-edge-cases.main]
decref Tag #1 rc=0 [ownership-edge-cases.main]
free Tag #1
incref Tag #2 rc=1 [ownership-edge-cases.main]
alloc Tag #3 rc=0 [ownership-edge-cases.main]
decref Tag #2 rc=0 [ownership-edge-cases.main]
free Tag #2
incref Tag #3 rc=1 [ownership-edge-cases.main]
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
alloc Widget #1 rc=0 [ownership-edge-cases.main]
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
alloc Token #1 rc=0 [ownership-edge-cases.makeToken]
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
alloc Num #1 rc=0 [ownership-edge-cases.main]
incref Num #1 rc=1 [ownership-edge-cases.main]
incref Num #1 rc=2 [ownership-edge-cases.main]
alloc Num #2 rc=0 [ownership-edge-cases.main]
decref Num #1 rc=1 [ownership-edge-cases.main]
incref Num #2 rc=1 [ownership-edge-cases.main]
decref Num #2 rc=0 [ownership-edge-cases.main]
free Num #2
decref Num #1 rc=0 [ownership-edge-cases.main]
free Num #1
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
alloc Counter #1 rc=0 [ownership-edge-cases.main]
incref Counter #1 rc=1 [ownership-edge-cases.main]
decref Counter #1 rc=0 [ownership-edge-cases.main]
free Counter #1
alloc Counter #2 rc=0 [ownership-edge-cases.main]
incref Counter #2 rc=1 [ownership-edge-cases.main]
decref Counter #2 rc=0 [ownership-edge-cases.main]
free Counter #2
alloc Counter #3 rc=0 [ownership-edge-cases.main]
incref Counter #3 rc=1 [ownership-edge-cases.main]
decref Counter #3 rc=0 [ownership-edge-cases.main]
free Counter #3
alloc Counter #4 rc=0 [ownership-edge-cases.main]
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
alloc Step #1 rc=0 [ownership-edge-cases.main]
incref Step #1 rc=1 [ownership-edge-cases.main]
decref Step #1 rc=0 [ownership-edge-cases.main]
free Step #1
alloc Step #2 rc=0 [ownership-edge-cases.main]
incref Step #2 rc=1 [ownership-edge-cases.main]
decref Step #2 rc=0 [ownership-edge-cases.main]
free Step #2
alloc Step #3 rc=0 [ownership-edge-cases.main]
incref Step #3 rc=1 [ownership-edge-cases.main]
decref Step #3 rc=0 [ownership-edge-cases.main]
free Step #3
alloc Step #4 rc=0 [ownership-edge-cases.main]
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
alloc Item #1 rc=0 [ownership-edge-cases.main]
incref Item #1 rc=1 [ownership-edge-cases.main]
decref Item #1 rc=0 [ownership-edge-cases.main]
free Item #1
alloc Item #2 rc=0 [ownership-edge-cases.main]
incref Item #2 rc=1 [ownership-edge-cases.main]
decref Item #2 rc=0 [ownership-edge-cases.main]
free Item #2
alloc Item #3 rc=0 [ownership-edge-cases.main]
incref Item #3 rc=1 [ownership-edge-cases.main]
decref Item #3 rc=0 [ownership-edge-cases.main]
free Item #3
alloc Item #4 rc=0 [ownership-edge-cases.main]
incref Item #4 rc=1 [ownership-edge-cases.main]
decref Item #4 rc=0 [ownership-edge-cases.main]
free Item #4
alloc Item #5 rc=0 [ownership-edge-cases.main]
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
alloc Inner #1 rc=0 [ownership-edge-cases.main]
incref Inner #1 rc=1 [ownership-edge-cases.main]
alloc Outer #2 rc=0 [ownership-edge-cases.main]
incref Inner #1 rc=2 [ownership-edge-cases.main]
incref Outer #2 rc=1 [ownership-edge-cases.main]
decref Inner #1 rc=1 [ownership-edge-cases.main]
decref Outer #2 rc=0 [ownership-edge-cases.main]
free Inner #1
free Outer #2
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
alloc A #1 rc=0 [ownership-edge-cases.main]
alloc B #2 rc=0 [ownership-edge-cases.main]
incref A #1 rc=1 [ownership-edge-cases.main]
alloc C #3 rc=0 [ownership-edge-cases.main]
incref B #2 rc=1 [ownership-edge-cases.main]
incref C #3 rc=1 [ownership-edge-cases.main]
decref C #3 rc=0 [ownership-edge-cases.main]
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
alloc Payload #1 rc=0 [ownership-edge-cases.main]
incref Payload #1 rc=1 [ownership-edge-cases.main]
alloc Container #2 rc=0 [ownership-edge-cases.main]
incref Payload #1 rc=2 [ownership-edge-cases.main]
incref Container #2 rc=1 [ownership-edge-cases.main]
alloc Payload #3 rc=0 [ownership-edge-cases.main]
decref Payload #1 rc=1 [Container.setPayload]
incref Payload #3 rc=1 [Container.setPayload]
decref Payload #1 rc=0 [ownership-edge-cases.main]
free Payload #1
decref Container #2 rc=0 [ownership-edge-cases.main]
free Payload #3
free Container #2
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
alloc Val #1 rc=0 [ownership-edge-cases.main]
alloc Holder #2 rc=0 [ownership-edge-cases.main]
incref Val #1 rc=1 [ownership-edge-cases.main]
incref Holder #2 rc=1 [ownership-edge-cases.main]
alloc Val #3 rc=0 [ownership-edge-cases.main]
decref Val #1 rc=0 [Holder.set]
free Val #1
incref Val #3 rc=1 [Holder.set]
alloc Val #4 rc=0 [ownership-edge-cases.main]
decref Val #3 rc=0 [Holder.set]
free Val #3
incref Val #4 rc=1 [Holder.set]
alloc Val #5 rc=0 [ownership-edge-cases.main]
decref Val #4 rc=0 [Holder.set]
free Val #4
incref Val #5 rc=1 [Holder.set]
decref Holder #2 rc=0 [ownership-edge-cases.main]
free Val #5
free Holder #2
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc NodeArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref NodeArray #2 rc=1 [ownership-edge-cases.main]
alloc Node #3 rc=0 [ownership-edge-cases.main]
incref Node #3 rc=1 [ownership-edge-cases.main]
alloc_in Buffer
incref Node #3 rc=2 [NodeArray.push]
decref Node #3 rc=1 [ownership-edge-cases.main]
incref Node #3 rc=2 [NodeArray.get]
incref Node #3 rc=3 [ownership-edge-cases.main]
decref NodeArray #2 rc=0 [ownership-edge-cases.main]
free ElementMemory #1
free NodeArray #2
decref Node #3 rc=1 [ownership-edge-cases.main]
decref Node #3 rc=0 [ownership-edge-cases.main]
free Node #3
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc NodeArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref NodeArray #2 rc=1 [ownership-edge-cases.main]
alloc Node #3 rc=0 [ownership-edge-cases.main]
alloc_in Buffer
incref Node #3 rc=1 [NodeArray.push]
alloc Node #5 rc=0 [ownership-edge-cases.main]
incref Node #5 rc=1 [NodeArray.push]
transfer Node #5 rc=1 [NodeArray.remove]
incref Node #5 rc=2 [ownership-edge-cases.main]
decref NodeArray #2 rc=0 [ownership-edge-cases.main]
free Node #3
free ElementMemory #1
free NodeArray #2
decref Node #5 rc=1 [ownership-edge-cases.main]
decref Node #5 rc=0 [ownership-edge-cases.main]
free Node #5
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc ItemArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref ItemArray #2 rc=1 [ownership-edge-cases.main]
alloc Item #3 rc=0 [ownership-edge-cases.main]
alloc_in Buffer
incref Item #3 rc=1 [ItemArray.push]
alloc Item #5 rc=0 [ownership-edge-cases.main]
decref Item #3 rc=0 [ItemArray.set]
free Item #3
incref Item #5 rc=1 [ItemArray.set]
incref Item #5 rc=2 [ItemArray.get]
incref Item #5 rc=3 [ownership-edge-cases.main]
decref ItemArray #2 rc=0 [ownership-edge-cases.main]
free ElementMemory #1
free ItemArray #2
decref Item #5 rc=1 [ownership-edge-cases.main]
decref Item #5 rc=0 [ownership-edge-cases.main]
free Item #5
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc ItemArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref ItemArray #2 rc=1 [ownership-edge-cases.main]
alloc Item #3 rc=0 [ownership-edge-cases.main]
alloc_in Buffer
incref Item #3 rc=1 [ItemArray.push]
alloc Item #5 rc=0 [ownership-edge-cases.main]
incref Item #5 rc=1 [ItemArray.push]
alloc Item #6 rc=0 [ownership-edge-cases.main]
incref Item #6 rc=1 [ItemArray.push]
free Item #3
free Item #5
free Item #6
decref ItemArray #2 rc=0 [ownership-edge-cases.main]
free ElementMemory #1
free ItemArray #2
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.fill]
alloc ItemArray #2 rc=0 [ownership-edge-cases.fill]
incref ElementMemory #1 rc=1 [ownership-edge-cases.fill]
incref ItemArray #2 rc=1 [ownership-edge-cases.fill]
alloc Item #3 rc=0 [ownership-edge-cases.fill]
alloc_in Buffer
incref Item #3 rc=1 [ItemArray.push]
alloc Item #5 rc=0 [ownership-edge-cases.fill]
incref Item #5 rc=1 [ItemArray.push]
alloc Item #6 rc=0 [ownership-edge-cases.fill]
incref Item #6 rc=1 [ItemArray.push]
decref ItemArray #2 rc=0 [ownership-edge-cases.fill]
free Item #3
free Item #5
free Item #6
free ElementMemory #1
free ItemArray #2
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc EntryArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref EntryArray #2 rc=1 [ownership-edge-cases.main]
alloc Entry #3 rc=0 [ownership-edge-cases.main]
alloc_in Buffer
incref Entry #3 rc=1 [EntryArray.push]
alloc Entry #5 rc=0 [ownership-edge-cases.main]
incref Entry #5 rc=1 [EntryArray.push]
alloc Entry #6 rc=0 [ownership-edge-cases.main]
incref Entry #6 rc=1 [EntryArray.push]
alloc Entry #7 rc=0 [ownership-edge-cases.main]
incref Entry #7 rc=1 [EntryArray.push]
alloc Entry #8 rc=0 [ownership-edge-cases.main]
incref Entry #8 rc=1 [EntryArray.push]
transfer Entry #3 rc=1 [EntryArray.remove]
incref Entry #3 rc=2 [ownership-edge-cases.main]
decref Entry #3 rc=1 [ownership-edge-cases.main]
decref Entry #3 rc=0 [ownership-edge-cases.main]
free Entry #3
transfer Entry #5 rc=1 [EntryArray.remove]
incref Entry #5 rc=2 [ownership-edge-cases.main]
decref Entry #5 rc=1 [ownership-edge-cases.main]
decref Entry #5 rc=0 [ownership-edge-cases.main]
free Entry #5
transfer Entry #6 rc=1 [EntryArray.remove]
incref Entry #6 rc=2 [ownership-edge-cases.main]
decref Entry #6 rc=1 [ownership-edge-cases.main]
decref Entry #6 rc=0 [ownership-edge-cases.main]
free Entry #6
transfer Entry #7 rc=1 [EntryArray.remove]
incref Entry #7 rc=2 [ownership-edge-cases.main]
decref Entry #7 rc=1 [ownership-edge-cases.main]
decref Entry #7 rc=0 [ownership-edge-cases.main]
free Entry #7
transfer Entry #8 rc=1 [EntryArray.remove]
incref Entry #8 rc=2 [ownership-edge-cases.main]
decref Entry #8 rc=1 [ownership-edge-cases.main]
decref Entry #8 rc=0 [ownership-edge-cases.main]
free Entry #8
decref EntryArray #2 rc=0 [ownership-edge-cases.main]
free ElementMemory #1
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc ValArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref ValArray #2 rc=1 [ownership-edge-cases.main]
alloc Val #3 rc=0 [ownership-edge-cases.main]
alloc_in Buffer
incref Val #3 rc=1 [ValArray.push]
alloc Val #5 rc=0 [ownership-edge-cases.main]
incref Val #5 rc=1 [ValArray.push]
alloc Val #6 rc=0 [ownership-edge-cases.main]
incref Val #6 rc=1 [ValArray.insert]
incref Val #3 rc=2 [ValArray.get]
incref Val #3 rc=3 [ownership-edge-cases.main]
incref Val #6 rc=2 [ValArray.get]
incref Val #6 rc=3 [ownership-edge-cases.main]
incref Val #5 rc=2 [ValArray.get]
incref Val #5 rc=3 [ownership-edge-cases.main]
decref ValArray #2 rc=0 [ownership-edge-cases.main]
free ElementMemory #1
free ValArray #2
decref Val #3 rc=1 [ownership-edge-cases.main]
decref Val #6 rc=1 [ownership-edge-cases.main]
decref Val #5 rc=1 [ownership-edge-cases.main]
decref Val #3 rc=0 [ownership-edge-cases.main]
free Val #3
decref Val #6 rc=0 [ownership-edge-cases.main]
free Val #6
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc ValArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref ValArray #2 rc=1 [ownership-edge-cases.main]
alloc Val #3 rc=0 [ownership-edge-cases.main]
alloc_in Buffer
incref Val #3 rc=1 [ValArray.push]
alloc Val #5 rc=0 [ownership-edge-cases.main]
incref Val #5 rc=1 [ValArray.push]
alloc Val #6 rc=0 [ownership-edge-cases.main]
incref Val #6 rc=1 [ValArray.push]
transfer Val #5 rc=1 [ValArray.remove]
incref Val #5 rc=2 [ownership-edge-cases.main]
decref ValArray #2 rc=0 [ownership-edge-cases.main]
free Val #3
free Val #6
free ElementMemory #1
free ValArray #2
decref Val #5 rc=1 [ownership-edge-cases.main]
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc WrapperArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref WrapperArray #2 rc=1 [ownership-edge-cases.main]
alloc Inner #3 rc=0 [ownership-edge-cases.main]
alloc Wrapper #4 rc=0 [ownership-edge-cases.main]
incref Inner #3 rc=1 [ownership-edge-cases.main]
alloc_in Buffer
incref Wrapper #4 rc=1 [WrapperArray.push]
alloc Inner #6 rc=0 [ownership-edge-cases.main]
alloc Wrapper #7 rc=0 [ownership-edge-cases.main]
incref Inner #6 rc=1 [ownership-edge-cases.main]
incref Wrapper #7 rc=1 [WrapperArray.push]
decref WrapperArray #2 rc=0 [ownership-edge-cases.main]
free Inner #3
free Wrapper #4
free Inner #6
free Wrapper #7
free ElementMemory #1
free WrapperArray #2
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
alloc Step #1 rc=0 [ownership-edge-cases.compute]
incref Step #1 rc=1 [ownership-edge-cases.compute]
alloc Step #2 rc=0 [ownership-edge-cases.compute]
incref Step #2 rc=1 [ownership-edge-cases.compute]
decref Step #1 rc=0 [ownership-edge-cases.compute]
free Step #1
decref Step #2 rc=0 [ownership-edge-cases.compute]
free Step #2
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc ItemArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref ItemArray #2 rc=1 [ownership-edge-cases.main]
alloc Item #3 rc=0 [ownership-edge-cases.main]
alloc_in Buffer
incref Item #3 rc=1 [ItemArray.push]
incref Item #3 rc=2 [ItemArray.get]
incref Item #3 rc=3 [ownership-edge-cases.getFirst]
transfer Item #3 rc=3 [ownership-edge-cases.getFirst]
decref Item #3 rc=2 [ownership-edge-cases.getFirst]
decref ItemArray #2 rc=0 [ownership-edge-cases.main]
free ElementMemory #1
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
alloc Cfg #1 rc=0 [ownership-edge-cases.__module_init]
incref Cfg #1 rc=1 [ownership-edge-cases.__module_init]
alloc Cfg #2 rc=0 [ownership-edge-cases.setup]
decref Cfg #1 rc=0 [ownership-edge-cases.setup]
free Cfg #1
incref Cfg #2 rc=1 [ownership-edge-cases.setup]
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
alloc State #1 rc=0 [ownership-edge-cases.__module_init]
incref State #1 rc=1 [ownership-edge-cases.__module_init]
alloc State #2 rc=0 [ownership-edge-cases.step]
decref State #1 rc=0 [ownership-edge-cases.step]
free State #1
incref State #2 rc=1 [ownership-edge-cases.step]
alloc State #3 rc=0 [ownership-edge-cases.step]
decref State #2 rc=0 [ownership-edge-cases.step]
free State #2
incref State #3 rc=1 [ownership-edge-cases.step]
alloc State #4 rc=0 [ownership-edge-cases.step]
decref State #3 rc=0 [ownership-edge-cases.step]
free State #3
incref State #4 rc=1 [ownership-edge-cases.step]
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
alloc Body #1 rc=0 [ownership-edge-cases.main]
alloc Shape #2 rc=0 [ownership-edge-cases.main]
incref Body #1 rc=1 [ownership-edge-cases.main]
incref Shape #2 rc=1 [ownership-edge-cases.main]
incref Shape #2 rc=2 [ownership-edge-cases.massOf]
incref Body #1 rc=2 [ownership-edge-cases.massOf]
decref Shape #2 rc=1 [ownership-edge-cases.massOf]
decref Body #1 rc=1 [ownership-edge-cases.massOf]
decref Shape #2 rc=0 [ownership-edge-cases.main]
free Body #1
free Shape #2
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
alloc ClosureEnv #1 rc=0 [ownership-edge-cases.main]
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
alloc Config #1 rc=0 [ownership-edge-cases.main]
incref Config #1 rc=1 [ownership-edge-cases.main]
alloc ClosureEnv #2 rc=0 [ownership-edge-cases.main]
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc ItemArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref ItemArray #2 rc=1 [ownership-edge-cases.main]
alloc Item #3 rc=0 [ownership-edge-cases.main]
incref Item #3 rc=1 [ownership-edge-cases.main]
incref Item #3 rc=2 [ownership-edge-cases.main]
decref ItemArray #2 rc=0 [ownership-edge-cases.main]
free ElementMemory #1
free ItemArray #2
decref Item #3 rc=1 [ownership-edge-cases.main]
decref Item #3 rc=0 [ownership-edge-cases.main]
free Item #3
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc ScoreArray #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref ScoreArray #2 rc=1 [ownership-edge-cases.main]
alloc Score #3 rc=0 [ownership-edge-cases.main]
alloc_in Buffer
incref Score #3 rc=1 [ScoreArray.push]
alloc Score #5 rc=0 [ownership-edge-cases.main]
incref Score #5 rc=1 [ScoreArray.push]
alloc Score #6 rc=0 [ownership-edge-cases.main]
incref Score #6 rc=1 [ScoreArray.push]
incref ScoreArray #2 rc=2 [ownership-edge-cases.main]
incref Score #3 rc=2 [ScoreArray.next]
transfer Score #3 rc=2 [ScoreArray.next]
decref Score #3 rc=1 [ownership-edge-cases.main]
incref Score #5 rc=2 [ScoreArray.next]
transfer Score #5 rc=2 [ScoreArray.next]
decref Score #5 rc=1 [ownership-edge-cases.main]
incref Score #6 rc=2 [ScoreArray.next]
transfer Score #6 rc=2 [ScoreArray.next]
decref Score #6 rc=1 [ownership-edge-cases.main]
decref ScoreArray #2 rc=1 [ownership-edge-cases.main]
decref ScoreArray #2 rc=0 [ownership-edge-cases.main]
free Score #3
free Score #5
free Score #6
free ElementMemory #1
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
alloc Data #1 rc=0 [ownership-edge-cases.main]
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.main]
alloc Grid #2 rc=0 [ownership-edge-cases.main]
incref ElementMemory #1 rc=1 [ownership-edge-cases.main]
incref Grid #2 rc=1 [ownership-edge-cases.main]
alloc ElementMemory #3 rc=0 [ownership-edge-cases.main]
alloc CellArray #4 rc=0 [ownership-edge-cases.main]
incref ElementMemory #3 rc=1 [ownership-edge-cases.main]
incref CellArray #4 rc=1 [ownership-edge-cases.main]
alloc Cell #5 rc=0 [ownership-edge-cases.main]
alloc_in Buffer
incref Cell #5 rc=1 [CellArray.push]
alloc Cell #7 rc=0 [ownership-edge-cases.main]
incref Cell #7 rc=1 [CellArray.push]
alloc ElementMemory #8 rc=0 [ownership-edge-cases.main]
alloc CellArray #9 rc=0 [ownership-edge-cases.main]
incref ElementMemory #8 rc=1 [ownership-edge-cases.main]
incref CellArray #9 rc=1 [ownership-edge-cases.main]
alloc Cell #10 rc=0 [ownership-edge-cases.main]
alloc_in Buffer
incref Cell #10 rc=1 [CellArray.push]
alloc_in Buffer
incref CellArray #4 rc=2 [Grid.push]
incref CellArray #9 rc=2 [Grid.push]
decref Grid #2 rc=0 [ownership-edge-cases.main]
free ElementMemory #1
free Grid #2
decref CellArray #4 rc=0 [ownership-edge-cases.main]
free Cell #5
free Cell #7
free ElementMemory #3
free CellArray #4
decref CellArray #9 rc=0 [ownership-edge-cases.main]
free Cell #10
free ElementMemory #8
free CellArray #9
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
alloc ElementMemory #1 rc=0 [ownership-edge-cases.fill]
alloc EntryArray #2 rc=0 [ownership-edge-cases.fill]
incref ElementMemory #1 rc=1 [ownership-edge-cases.fill]
alloc Bucket #3 rc=0 [ownership-edge-cases.fill]
incref EntryArray #2 rc=1 [ownership-edge-cases.fill]
incref Bucket #3 rc=1 [ownership-edge-cases.fill]
alloc Entry #4 rc=0 [ownership-edge-cases.fill]
alloc_in Buffer
incref Entry #4 rc=1 [EntryArray.push]
alloc Entry #6 rc=0 [ownership-edge-cases.fill]
incref Entry #6 rc=1 [EntryArray.push]
decref Bucket #3 rc=0 [ownership-edge-cases.fill]
free Entry #4
free Entry #6
free ElementMemory #1
free EntryArray #2
free Bucket #3
```
