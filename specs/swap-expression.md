---
feature: swap-expression
status: experimental
keywords: [swap, field, assignment, memory, ownership]
category: memory-management
---

# Swap Expression

## Documentation

### Heap-Pointer Field Swap

When replacing a heap-pointer field on a struct (or an associated value in a union), you must use `swap` instead of direct assignment. The `swap` expression atomically replaces the field value and returns the old value to the current scope for proper cleanup.

```text
var old = swap fieldName with newValue
let _ = swap variable.field with newValue
```

Direct assignment to heap-pointer fields is a compile error. Use `swap` to make the ownership transfer explicit.

## Tests

<!-- test: basic-self-field-swap -->
Swap a struct field on self and verify the old value is returned and the new value is stored.
```maxon
typealias Integer = int(i64.min to i64.max)

type Container
  export var value Integer
  export var child Container

  export function replaceChild(newChild Container) returns Integer
    var old = swap child with newChild
    return old.value
  end 'replaceChild'
end 'Container'

function main() returns ExitCode
  var c = Container{value: 1, child: Container{value: 10, child: Container{}}}
  var result = c.replaceChild(Container{value: 20, child: Container{}})
  if result == 10 'check1'
    if c.child.value == 20 'check2'
      return 0
    end 'check2'
  end 'check1'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: swap-discard -->
Swap with discard using `let _ =`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder
  export var data Integer
  export var other Holder

  export function reset()
    let _ = swap other with Holder{}
  end 'reset'
end 'Holder'

function main() returns ExitCode
  var h = Holder{data: 1, other: Holder{data: 42, other: Holder{}}}
  h.reset()
  return h.other.data
end 'main'
```
```exitcode
0
```

<!-- test: swap-qualified-field -->
Swap a field on a variable (qualified field swap) from within a method.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
  export var left Integer
  export var right Pair
end 'Pair'

function main() returns ExitCode
  var p = Pair{left: 5, right: Pair{left: 10, right: Pair{}}}
  var old = swap p.right with Pair{left: 20, right: Pair{}}
  if old.left == 10 'check1'
    if p.right.left == 20 'check2'
      return 0
    end 'check2'
  end 'check1'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: error-direct-self-field-assign -->
Direct assignment to a heap-pointer self field is a compile error.
```maxon
typealias CountArray = Array with Count

type Container
  var inner CountArray

  export function badAssign()
    var newArr = CountArray{}
    inner = newArr
  end 'badAssign'
end 'Container'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/swap-expression/error-direct-self-field-assign.test:9:5: heap-pointer field 'inner' must use 'swap' instead of direct assignment
```

<!-- test: error-direct-qualified-field-assign -->
Direct assignment to a heap-pointer field via qualified access is a compile error.
```maxon
typealias CountArray = Array with Count

type Container
  export var inner CountArray
end 'Container'

function main() returns ExitCode
  var c = Container{inner: CountArray{}}
  c.inner = CountArray{}
  return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/swap-expression/error-direct-qualified-field-assign.test:10:5: heap-pointer field 'inner' must use 'swap' instead of direct assignment
```

<!-- test: scalar-field-assign-ok -->
Direct assignment to a scalar (non-heap-pointer) field is still allowed.
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

<!-- test: swap-in-conditional -->
Swap inside a conditional block correctly updates the field for code after the block.
```maxon
typealias CountArray = Array with Count

type Container
  var inner CountArray

  export function init()
    inner.resize(4)
  end 'init'

  export function maybeReset(doReset bool)
    if doReset 'reset'
      var newArr = CountArray{}
      newArr.resize(4)
      let _ = swap inner with newArr
    end 'reset'
    inner.set(0, value: 99)
  end 'maybeReset'

  export function getFirst() returns Count
    return try inner.get(0) otherwise 0
  end 'getFirst'
end 'Container'

function main() returns ExitCode
  var c = Container{inner: CountArray{}}
  c.init()
  c.maybeReset(true)
  return c.getFirst()
end 'main'
```
```exitcode
99
```
