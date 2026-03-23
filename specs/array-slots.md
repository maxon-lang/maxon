---
feature: array-slots
status: experimental
keywords: [array, slot, empty, managed, error]
category: memory-safety
---
# Array Slots - Empty Slot Detection

## Documentation

When an array of structs is resized beyond its populated elements, the new slots contain null (zero) pointers. Accessing these empty slots via `managed.get()` now throws `ArrayError.emptySlot` instead of dereferencing a null pointer.

This only applies to struct-element arrays. Primitive arrays (int, float, byte, bool) store values inline, so a zero value is a valid element, not an empty slot.

## Tests

<!-- test: get-empty-slot-basic -->
### Get on empty slot uses otherwise path
Create an array, push one item, resize to 3, then `try arr.get(1) otherwise` should use the otherwise path since slot 1 is empty.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
  export var value Integer
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
  var arr = SlotArray{}
  arr.push(Slot{value: 10})
  arr.resize(3)
  let result = try arr.get(1) otherwise Slot{value: -1}
  return result.value + 1
end 'main'
```
```exitcode
0
```

<!-- test: get-valid-slot-not-empty -->
### Get on a populated slot works fine
Getting an element from a slot that was populated via push should work without error.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
  export var value Integer
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
  var arr = SlotArray{}
  arr.push(Slot{value: 42})
  let result = try arr.get(0) otherwise Slot{value: 0}
  return result.value
end 'main'
```
```exitcode
42
```

<!-- test: int-array-zero-not-empty -->
### Int array containing zero does NOT throw emptySlot
Primitive arrays store values inline. A zero value is a valid element, not an empty slot.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(0)
  arr.push(5)
  arr.resize(4)
  let val = try arr.get(0) otherwise -1
  let val2 = try arr.get(2) otherwise -1
  return val + val2
end 'main'
```
```exitcode
0
```

<!-- test: get-empty-slot-try-otherwise-value -->
### Try/otherwise returns default struct value when slot is empty
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
  export var value Integer
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
  var arr = SlotArray{}
  arr.resize(3)
  let result = try arr.get(0) otherwise Slot{value: 99}
  return result.value
end 'main'
```
```exitcode
99
```

<!-- test: first-empty-slot -->
### first() on array where slot 0 is empty
Resize to 3 without pushing any elements. `first()` should use the otherwise path.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
  export var value Integer
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
  var arr = SlotArray{}
  arr.push(Slot{value: 1})
  arr.resize(3)
  try arr.remove(0) otherwise ignore
  let result = try arr.first() otherwise Slot{value: 77}
  return result.value
end 'main'
```
```exitcode
77
```

<!-- test: last-empty-slot -->
### last() on array where last slot is empty
Resize to 3, only push 1 item. `last()` should use the otherwise path since slot 2 is empty.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
  export var value Integer
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
  var arr = SlotArray{}
  arr.push(Slot{value: 1})
  arr.resize(3)
  let result = try arr.last() otherwise Slot{value: 55}
  return result.value
end 'main'
```
```exitcode
55
```

<!-- test: try-otherwise-error-binding -->
### Try/otherwise with error binding and match on ArrayError.emptySlot
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
  export var value Integer
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
  var arr = SlotArray{}
  arr.resize(2)
  try arr.get(0) otherwise (e) 'handler'
    match e 'check'
      emptySlot then return 42
      indexOutOfBounds then return 99
    end 'check'
  end 'handler'
  return 0
end 'main'
```
```exitcode
42
```
