---
feature: array-managed-elements
status: stable
keywords: [array, struct, managed, refcount, cleanup]
category: memory
---
# Array of Structs with Managed Fields

## Documentation

When an array contains structs that have managed fields (like String), the array must properly increment and decrement reference counts for each element's managed fields.

### managed_field_offsets

Each struct type tracks the offsets of fields that need reference counting. For example, a `Token` type with a `String` field at offset 8 would have `managed_field_offsets = [8]`.

When storing a struct in an array:
- Each managed field's reference count is incremented

When cleaning up an array:
- Each element's managed fields have their reference counts decremented

## Tests

<!-- test: array-of-structs-with-string -->
### Array of Structs Containing Strings
Structs with String fields stored in arrays must have proper refcount management.
<!-- TrackMemory: true -->
```maxon
type Item
  export var name String
  export var value int
end 'Item'

typealias ItemArray is Array with Item

function main() returns int
  var items = ItemArray{}
  items.push({name: "hello world that needs heap allocation", value: 1})
  items.push({name: "another long string for heap allocation", value: 2})
  let first = try items.get(0) otherwise Item{name: "", value: 0}
  print("{first.name}\n")
  return first.value
end 'main'
```
```exitcode
1
```
```stdout
MOVE: managed
COPY: String
ALLOC #1: 200 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: managed
COPY: String
ALLOC #2: 48 bytes (string interpolation)
INCREF: string interpolation -> rc=1
MOVE: managed
INCREF: <cstr> -> rc=2
hello world that needs heap allocation
CLEANUP: cs
DECREF: <cstr cleanup> -> rc=1
DECREF: <temp> -> rc=0
FREE #2: 48 bytes (temp cleanup)
CLEANUP: first
CLEANUP: first
CLEANUP: items
CLEANUP: <array element>
CLEANUP: <array element>
CLEANUP: <array element>
CLEANUP: <array element>
DECREF: items -> rc=0
FREE #1: 200 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 248 bytes
Freed:     248 bytes
Leaked:    0 bytes
Moves:     3
Increfs:   3
Decrefs:   3
Copies:    2
Cleanups:  8
```

<!-- test: array-of-structs-cleanup-order -->
### Array Cleanup Decrefs All Element Fields
When an array is cleaned up, each element's managed fields must be decremented.
<!-- TrackMemory: true -->
```maxon
type Pair
  export var first String
  export var second String
end 'Pair'

typealias PairArray is Array with Pair

function main() returns int
  var pairs = PairArray{}
  pairs.push({first: "alpha string that is long for heap", second: "beta string that is long for heap"})
  return 0
end 'main'
```
```exitcode
0
```
```stdout
MOVE: managed
COPY: String
MOVE: managed
COPY: String
ALLOC #1: 328 bytes (array grow)
INCREF: array grow -> rc=1
CLEANUP: pairs
CLEANUP: <array element>
CLEANUP: <array element>
CLEANUP: <array element>
DECREF: pairs -> rc=0
FREE #1: 328 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 328 bytes
Freed:     328 bytes
Leaked:    0 bytes
Moves:     2
Increfs:   1
Decrefs:   1
Copies:    2
Cleanups:  4
```

<!-- test: struct-with-multiple-managed-fields -->
### Struct With Multiple Managed Fields
Each managed field in a struct needs its own refcount tracking.
<!-- TrackMemory: true -->
```maxon
type MultiField
  export var a String
  export var b int
  export var c String
  export var d int
  export var e String
end 'MultiField'

typealias MultiArray is Array with MultiField

function main() returns int
  var items = MultiArray{}
  items.push({a: "string a that is long enough for heap", b: 1, c: "string c that is long enough for heap", d: 2, e: "string e that is long enough for heap"})
  return 0
end 'main'
```
```exitcode
0
```
```stdout
MOVE: managed
COPY: String
MOVE: managed
COPY: String
MOVE: managed
COPY: String
ALLOC #1: 552 bytes (array grow)
INCREF: array grow -> rc=1
CLEANUP: items
CLEANUP: <array element>
CLEANUP: <array element>
CLEANUP: <array element>
CLEANUP: <array element>
DECREF: items -> rc=0
FREE #1: 552 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 552 bytes
Freed:     552 bytes
Leaked:    0 bytes
Moves:     3
Increfs:   1
Decrefs:   1
Copies:    3
Cleanups:  5
```
