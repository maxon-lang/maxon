---
feature: mutable-unions
status: experimental
keywords: [union, mutable, associated values, match, write-back]
category: type-system
---

# Mutable Union Associated Values

## Documentation

### Mutable Match Bindings

When a union variable is mutable (`var`), match bindings on associated values are also mutable. Assigning to a match binding writes the new value back to the union's heap block in-place.

```text
var myNode = Node.node(10, Node.empty)
match myNode 'update'
  node(value, next) then next = Node.empty
  empty then return
end 'update'
```

When the union variable is immutable (`let`), match bindings are read-only copies, preserving the existing behavior.

## Tests

<!-- test: basic-mutable -->
Mutate an associated value through a match binding on a `var` union.
```maxon
typealias Integer = int(i64.min to i64.max)

union Box
  empty
  full(value Integer)
end 'Box'

function main() returns ExitCode
  var b = Box.full(10)
  match b 'update'
    full(value) then value = 42
    empty then return 0
  end 'update'
  match b 'read'
    full(value) then return value
    empty then return 0
  end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: write-back-verify -->
Verify that mutation through match binding persists after the match.
```maxon
typealias Integer = int(i64.min to i64.max)

union Wrapper
  none
  some(n Integer)
end 'Wrapper'

function readValue(w Wrapper) returns Integer
  match w 'r'
    some(n) then return n
    none then return 0
  end 'r'
end 'readValue'

function main() returns ExitCode
  var w = Wrapper.some(5)
  match w 'mutate'
    some(n) then n = 99
    none then return 0
  end 'mutate'
  return readValue(w)
end 'main'
```
```exitcode
99
```

<!-- test: multiple-mutable-fields -->
Multiple mutable fields in one case, mutate one at a time.
```maxon
typealias Integer = int(i64.min to i64.max)

union Pair
  empty
  values(a Integer, b Integer)
end 'Pair'

function main() returns ExitCode
  var p = Pair.values(1, 2)
  match p 'update-a'
    values(a, _b) then a = 10
    empty then return 0
  end 'update-a'
  match p 'update-b'
    values(_a, b) then b = 32
    empty then return 0
  end 'update-b'
  match p 'read'
    values(a, b) then return a + b
    empty then return 0
  end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: mixed-readonly-and-mutable -->
Only assign to some bindings, leave others unchanged.
```maxon
typealias Integer = int(i64.min to i64.max)

union Record
  blank
  data(id Integer, count Integer)
end 'Record'

function main() returns ExitCode
  var e = Record.data(1, 0)
  match e 'inc'
    data(id, count) then count = count + id
    blank then return 0
  end 'inc'
  match e 'read'
    data(id, count) then return id + count
    blank then return 0
  end 'read'
end 'main'
```
```exitcode
2
```

<!-- test: linked-list-mutation -->
Build a linked list and mutate next pointers in-place.
```maxon
typealias Integer = int(i64.min to i64.max)

union Node
  empty
  item(value Integer, next Node)
end 'Node'

function main() returns ExitCode
  var n1 = Node.item(10, Node.empty)
  var n2 = Node.item(32, Node.empty)
  match n1 'link'
    item(_value, next) then next = n2
    empty then return 0
  end 'link'
  match n1 'read'
    item(v1, tail) then return v1 + nodeValue(tail)
    empty then return 0
  end 'read'
end 'main'

function nodeValue(n Node) returns Integer
  match n 'get'
    item(v, _next) then return v
    empty then return 0
  end 'get'
end 'nodeValue'
```
```exitcode
42
```

<!-- test: mutate-twice -->
Mutate the same associated value twice in sequence.
```maxon
typealias Integer = int(i64.min to i64.max)

union Cell
  empty
  holding(value Integer)
end 'Cell'

function main() returns ExitCode
  var c = Cell.holding(10)
  match c 'first'
    holding(value) then value = 20
    empty then return 0
  end 'first'
  match c 'second'
    holding(value) then value = value + 22
    empty then return 0
  end 'second'
  match c 'read'
    holding(value) then return value
    empty then return 0
  end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: self-referential-mutation -->
Self-referential union — link two nodes and traverse.
```maxon
typealias Integer = int(i64.min to i64.max)

union Chain
  tail
  link(value Integer, next Chain)
end 'Chain'

function sumChain(c Chain) returns Integer
  match c 'walk'
    link(v, next) then return v + sumChain(next)
    tail then return 0
  end 'walk'
end 'sumChain'

function main() returns ExitCode
  var c2 = Chain.link(32, Chain.tail)
  var c1 = Chain.link(10, Chain.tail)
  match c1 'link1'
    link(_v, next) then next = c2
    tail then return 0
  end 'link1'
  return sumChain(c1)
end 'main'
```
```exitcode
42
```

<!-- test: error.assign-to-let-union-binding -->
Error: assigning to a match binding when the union variable is `let`.
```maxon
typealias Integer = int(i64.min to i64.max)

union Box
  empty
  full(value Integer)
end 'Box'

function main() returns ExitCode
  let b = Box.full(10)
  match b 'bad'
    full(value) then value = 42
    empty then return 0
  end 'bad'
  return 0
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/mutable-unions/error.assign-to-let-union-binding.test:12:22: cannot assign to immutable variable: 'value'
```

<!-- test: mutate-string-payload -->
Mutate a String-typed associated value.
```maxon
union Named
  anonymous
  named(name String)
end 'Named'

function main() returns ExitCode
  var n = Named.named("hello")
  match n 'update'
    named(name) then name = "world"
    anonymous then return 0
  end 'update'
  match n 'read'
    named(name) then return name.byteLength()
    anonymous then return 0
  end 'read'
end 'main'
```
```exitcode
5
```

<!-- test: mutate-struct-payload -->
Mutate a struct-typed associated value.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

union Shape
  nothing
  located(pos Point)
end 'Shape'

function main() returns ExitCode
  var s = Shape.located(Point{x: 1, y: 2})
  match s 'update'
    located(pos) then pos = Point{x: 10, y: 32}
    nothing then return 0
  end 'update'
  match s 'read'
    located(pos) then return pos.x + pos.y
    nothing then return 0
  end 'read'
end 'main'
```
```exitcode
42
```
