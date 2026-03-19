---
feature: heap-field-assignment
status: stable
keywords: [field, assignment, memory, ownership, struct]
category: memory-management
---

# Heap-Pointer Field Assignment

## Documentation

### Assigning to Heap-Pointer Fields

Struct fields that hold heap-pointer types (other structs, enums with associated values) can be assigned directly. When the struct is freed, its destructor decrefs each managed field, freeing them if no other references remain.

```text
container.child = newChild
```

## Tests

<!-- test: basic-self-field-assign -->
Assign to a struct field on self and verify the new value is stored.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
  export var value Integer
end 'Inner'

type Container
  export var value Integer
  export var child Inner

  export function replaceChild(newChild Inner)
    child = newChild
  end 'replaceChild'
end 'Container'

function main() returns ExitCode
  var c = Container{value: 1, child: Inner{value: 10}}
  c.replaceChild(Inner{value: 20})
  if c.child.value == 20 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: qualified-field-assign -->
Assign to a field on a variable via qualified access.
```maxon
typealias Integer = int(i64.min to i64.max)

type Right
  export var left Integer
end 'Right'

type Pair
  export var left Integer
  export var right Right
end 'Pair'

function main() returns ExitCode
  var p = Pair{left: 5, right: Right{left: 10}}
  p.right = Right{left: 20}
  if p.right.left == 20 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: scalar-field-assign-ok -->
Direct assignment to a scalar (non-heap-pointer) field is allowed.
```maxon
type Counter
  var count Count

  export function increment()
    count = count + 1
  end 'increment'

  export function value() returns Count
    return count
  end 'value'
end 'Counter'

function main() returns ExitCode
  var c = Counter{count: 0}
  c.increment()
  c.increment()
  return c.value()
end 'main'
```
```exitcode
2
```

<!-- test: memory.self-field-overwrite-frees-old -->
<!-- MmTrace -->
Overwrite a self field in a method and verify all allocations are freed properly.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
  export var value Integer
end 'Inner'

type Container
  export var value Integer
  export var child Inner

  export function replaceChild(newChild Inner)
    child = newChild
  end 'replaceChild'

  export function childValue() returns Integer
    return child.value
  end 'childValue'
end 'Container'

function testAssign()
  var c = Container{value: 1, child: Inner{value: 10}}
  c.replaceChild(Inner{value: 20})
  print("{c.childValue()}\n")
end 'testAssign'

function main() returns ExitCode
  testAssign()
  return 0
end 'main'
```
```exitcode
0
```
```stdout
20
```
```stderr
slab_alloc size=40 class=5
alloc Inner #1 rc=0 size=8 [heap-field-assignment.testAssign]
slab_alloc size=48 class=5
alloc Container #2 rc=0 size=16 [heap-field-assignment.testAssign]
incref Inner #1 rc=1 [heap-field-assignment.testAssign]
incref Container #2 rc=1 [heap-field-assignment.testAssign]
slab_alloc size=40 class=5
alloc Inner #3 rc=0 size=8 [heap-field-assignment.testAssign]
incref Inner #3 rc=1 [heap-field-assignment.testAssign]
decref Inner #1 rc=0 [Container.replaceChild]
  free Inner #1
slab_free size=48 class=5
incref Inner #3 rc=2 [Container.replaceChild]
slab_alloc size=21 class=4
raw_alloc size=21 [toStr.buf [heap-field-assignment.testAssign]]
slab_alloc size=48 class=5
alloc String #4 rc=0 size=16 [heap-field-assignment.testAssign]
slab_alloc size=64 class=6
alloc __ManagedMemory #5 rc=0 size=32 [heap-field-assignment.testAssign]
slab_alloc size=4 class=2
raw_alloc size=4 [interp.buf [heap-field-assignment.testAssign]]
raw_free
slab_free size=32 class=4
incref __ManagedMemory #5 rc=1 [heap-field-assignment.testAssign]
incref String #4 rc=1 [heap-field-assignment.testAssign]
decref String #4 rc=0 [heap-field-assignment.testAssign]
decref __ManagedMemory #5 rc=0 [~String]
raw_free
slab_free size=8 class=2
  free __ManagedMemory #5
slab_free size=80 class=6
  free String #4
slab_free size=48 class=5
decref Container #2 rc=0 [heap-field-assignment.testAssign]
decref Inner #3 rc=1 [~Container]
  free Container #2
slab_free size=48 class=5
decref Inner #3 rc=0 [heap-field-assignment.testAssign]
  free Inner #3
slab_free size=48 class=5
slab_alloc size=40 class=5
raw_alloc size=40
raw_free
slab_free size=48 class=5
```

<!-- test: memory.qualified-field-overwrite-frees-old -->
<!-- MmTrace -->
Overwrite a qualified field and verify all allocations are freed properly.
```maxon
typealias Integer = int(i64.min to i64.max)

type Right
  export var left Integer
end 'Right'

type Pair
  export var left Integer
  export var right Right
end 'Pair'

function testAssign()
  var p = Pair{left: 5, right: Right{left: 10}}
  p.right = Right{left: 20}
  print("{p.right.left}\n")
end 'testAssign'

function main() returns ExitCode
  testAssign()
  return 0
end 'main'
```
```exitcode
0
```
```stdout
20
```
```stderr
slab_alloc size=40 class=5
alloc Right #1 rc=0 size=8 [heap-field-assignment.testAssign]
slab_alloc size=48 class=5
alloc Pair #2 rc=0 size=16 [heap-field-assignment.testAssign]
incref Right #1 rc=1 [heap-field-assignment.testAssign]
incref Pair #2 rc=1 [heap-field-assignment.testAssign]
slab_alloc size=40 class=5
alloc Right #3 rc=0 size=8 [heap-field-assignment.testAssign]
incref Right #3 rc=1 [heap-field-assignment.testAssign]
decref Right #1 rc=0 [heap-field-assignment.testAssign]
  free Right #1
slab_free size=48 class=5
incref Right #3 rc=2 [heap-field-assignment.testAssign]
slab_alloc size=21 class=4
raw_alloc size=21 [toStr.buf [heap-field-assignment.testAssign]]
slab_alloc size=48 class=5
alloc String #4 rc=0 size=16 [heap-field-assignment.testAssign]
slab_alloc size=64 class=6
alloc __ManagedMemory #5 rc=0 size=32 [heap-field-assignment.testAssign]
slab_alloc size=4 class=2
raw_alloc size=4 [interp.buf [heap-field-assignment.testAssign]]
raw_free
slab_free size=32 class=4
incref __ManagedMemory #5 rc=1 [heap-field-assignment.testAssign]
incref String #4 rc=1 [heap-field-assignment.testAssign]
decref String #4 rc=0 [heap-field-assignment.testAssign]
decref __ManagedMemory #5 rc=0 [~String]
raw_free
slab_free size=8 class=2
  free __ManagedMemory #5
slab_free size=80 class=6
  free String #4
slab_free size=48 class=5
decref Pair #2 rc=0 [heap-field-assignment.testAssign]
decref Right #3 rc=1 [~Pair]
  free Pair #2
slab_free size=48 class=5
decref Right #3 rc=0 [heap-field-assignment.testAssign]
  free Right #3
slab_free size=48 class=5
slab_alloc size=40 class=5
raw_alloc size=40
raw_free
slab_free size=48 class=5
```
