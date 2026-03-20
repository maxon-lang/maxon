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
sl_init
    os_alloc size=67108864
mm_alloc Inner #1 size=8 [heap-field-assignment.testAssign]
  sl_alloc Inner #1 size=40 class=4
mm_alloc Container #2 size=16 [heap-field-assignment.testAssign]
  sl_alloc Container #2 size=48 class=4
mm_incref Inner #1 rc=1 [heap-field-assignment.testAssign]
mm_incref Container #2 rc=1 [heap-field-assignment.testAssign]
mm_alloc Inner #3 size=8 [heap-field-assignment.testAssign]
  sl_alloc Inner #3 size=40 class=4
mm_incref Inner #3 rc=1 [heap-field-assignment.testAssign]
mm_decref Inner #1 rc=0 [Container.replaceChild]
  mm_free Inner #1
    sl_free Inner #1 size=48 class=4
mm_incref Inner #3 rc=2 [Container.replaceChild]
mm_raw_alloc #R1 size=21 [toStr.buf [heap-field-assignment.testAssign]]
  sl_alloc size=21 class=2
mm_alloc String #4 size=16 [heap-field-assignment.testAssign]
  sl_alloc String #4 size=48 class=4
mm_alloc __ManagedMemory #5 size=32 [heap-field-assignment.testAssign]
  sl_alloc __ManagedMemory #5 size=64 class=5
mm_raw_alloc #R2 size=4 [interp.buf [heap-field-assignment.testAssign]]
  sl_alloc size=4 class=0
mm_raw_free #R1
  sl_free size=24 class=2
mm_incref __ManagedMemory #5 rc=1 [heap-field-assignment.testAssign]
mm_incref String #4 rc=1 [heap-field-assignment.testAssign]
mm_decref String #4 rc=0 [heap-field-assignment.testAssign]
  mm_decref __ManagedMemory #5 rc=0 [~String]
    mm_raw_free #R2
      sl_free size=8 class=0
    mm_free __ManagedMemory #5
      sl_free __ManagedMemory #5 size=64 class=5
  mm_free String #4
    sl_free String #4 size=48 class=4
mm_decref Container #2 rc=0 [heap-field-assignment.testAssign]
  mm_decref Inner #3 rc=1 [~Container]
  mm_free Container #2
    sl_free Container #2 size=48 class=4
mm_decref Inner #3 rc=0 [heap-field-assignment.testAssign]
  mm_free Inner #3
    sl_free Inner #3 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
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
sl_init
    os_alloc size=67108864
mm_alloc Right #1 size=8 [heap-field-assignment.testAssign]
  sl_alloc Right #1 size=40 class=4
mm_alloc Pair #2 size=16 [heap-field-assignment.testAssign]
  sl_alloc Pair #2 size=48 class=4
mm_incref Right #1 rc=1 [heap-field-assignment.testAssign]
mm_incref Pair #2 rc=1 [heap-field-assignment.testAssign]
mm_alloc Right #3 size=8 [heap-field-assignment.testAssign]
  sl_alloc Right #3 size=40 class=4
mm_incref Right #3 rc=1 [heap-field-assignment.testAssign]
mm_decref Right #1 rc=0 [heap-field-assignment.testAssign]
  mm_free Right #1
    sl_free Right #1 size=48 class=4
mm_incref Right #3 rc=2 [heap-field-assignment.testAssign]
mm_raw_alloc #R1 size=21 [toStr.buf [heap-field-assignment.testAssign]]
  sl_alloc size=21 class=2
mm_alloc String #4 size=16 [heap-field-assignment.testAssign]
  sl_alloc String #4 size=48 class=4
mm_alloc __ManagedMemory #5 size=32 [heap-field-assignment.testAssign]
  sl_alloc __ManagedMemory #5 size=64 class=5
mm_raw_alloc #R2 size=4 [interp.buf [heap-field-assignment.testAssign]]
  sl_alloc size=4 class=0
mm_raw_free #R1
  sl_free size=24 class=2
mm_incref __ManagedMemory #5 rc=1 [heap-field-assignment.testAssign]
mm_incref String #4 rc=1 [heap-field-assignment.testAssign]
mm_decref String #4 rc=0 [heap-field-assignment.testAssign]
  mm_decref __ManagedMemory #5 rc=0 [~String]
    mm_raw_free #R2
      sl_free size=8 class=0
    mm_free __ManagedMemory #5
      sl_free __ManagedMemory #5 size=64 class=5
  mm_free String #4
    sl_free String #4 size=48 class=4
mm_decref Pair #2 rc=0 [heap-field-assignment.testAssign]
  mm_decref Right #3 rc=1 [~Pair]
  mm_free Pair #2
    sl_free Pair #2 size=48 class=4
mm_decref Right #3 rc=0 [heap-field-assignment.testAssign]
  mm_free Right #3
    sl_free Right #3 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
```
