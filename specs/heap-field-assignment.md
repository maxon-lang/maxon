---
feature: heap-field-assignment
status: stable
keywords: [field, assignment, memory, ownership, struct]
category: memory-management
---

# Heap-Pointer Field Assignment

## Documentation

### Assigning to Heap-Pointer Fields

Struct fields that hold heap-pointer types (other structs, enums with associated values) can be assigned directly. The memory manager tracks the old value as a child of the parent struct and frees it when the parent is freed.

```text
container.child = newChild
```

## Tests

<!-- test: basic-self-field-assign -->
Assign to a struct field on self and verify the new value is stored.
```maxon
typealias Integer = int(i64.min to i64.max)

type Container
  export var value Integer
  export var child Container

  export function replaceChild(newChild Container)
    child = newChild
  end 'replaceChild'
end 'Container'

function main() returns ExitCode
  var c = Container{value: 1, child: Container{value: 10, child: Container{}}}
  c.replaceChild(Container{value: 20, child: Container{}})
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

type Pair
  export var left Integer
  export var right Pair
end 'Pair'

function main() returns ExitCode
  var p = Pair{left: 5, right: Pair{left: 10, right: Pair{}}}
  p.right = Pair{left: 20, right: Pair{}}
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

type Container
  export var value Integer
  export var child Container

  export function replaceChild(newChild Container)
    child = newChild
  end 'replaceChild'

  export function childValue() returns Integer
    return child.value
  end 'childValue'
end 'Container'

function testAssign()
  var c = Container{value: 1, child: Container{value: 10, child: Container{}}}
  c.replaceChild(Container{value: 20, child: Container{}})
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
  scope_enter heap-field-assignment.testAssign (depth=1)
  alloc Container rc=0
  alloc Container rc=0
  move Container
  alloc Container rc=0
  move Container
  alloc Container rc=0
  move Container
  alloc Container rc=0
  move Container
  incref Container rc=1
  alloc Container rc=0
  alloc Container rc=0
  move Container
  alloc Container rc=0
  move Container
  alloc Container rc=0
  move Container
  decref Container rc=0
  incref Container rc=1
  move Container
  alloc ToStringBuf rc=0
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in Buffer
  free ToStringBuf
  decref Container rc=0
  scope_exit heap-field-assignment.testAssign (2 owned)
  free Container
  free Container
  free Container
  free Container
  free Container
  free Container
  free Container
  free Container
  free Container
  free Buffer
  free __ManagedMemory
  free String
```

<!-- test: memory.qualified-field-overwrite-frees-old -->
<!-- MmTrace -->
Overwrite a qualified field and verify all allocations are freed properly.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
  export var left Integer
  export var right Pair
end 'Pair'

function testAssign()
  var p = Pair{left: 5, right: Pair{left: 10, right: Pair{}}}
  p.right = Pair{left: 20, right: Pair{}}
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
  scope_enter heap-field-assignment.testAssign (depth=1)
  alloc Pair rc=0
  alloc Pair rc=0
  move Pair
  alloc Pair rc=0
  move Pair
  alloc Pair rc=0
  move Pair
  alloc Pair rc=0
  move Pair
  incref Pair rc=1
  alloc Pair rc=0
  alloc Pair rc=0
  move Pair
  alloc Pair rc=0
  move Pair
  alloc Pair rc=0
  move Pair
  move Pair
  alloc ToStringBuf rc=0
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in Buffer
  free ToStringBuf
  decref Pair rc=0
  scope_exit heap-field-assignment.testAssign (2 owned)
  free Pair
  free Pair
  free Pair
  free Pair
  free Pair
  free Pair
  free Pair
  free Pair
  free Pair
  free Buffer
  free __ManagedMemory
  free String
```
