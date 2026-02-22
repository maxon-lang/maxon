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
```maxon

typealias Integer = int(i64.min to i64.max)

type Item
  export var name String
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
  var items = ItemArray{}
  items.push(Item{name: "hello world that needs heap allocation", value: 1})
  items.push(Item{name: "another long string for heap allocation", value: 2})
  let first = try items.get(0) otherwise Item{name: "", value: 0}
  print("{first.name}\n")
  return first.value
end 'main'
```
```exitcode
1
```
```stdout
hello world that needs heap allocation
```

<!-- test: array-of-structs-cleanup-order -->
### Array Cleanup Decrefs All Element Fields
When an array is cleaned up, each element's managed fields must be decremented.
```maxon
type Pair
  export var first String
  export var second String
end 'Pair'

typealias PairArray = Array with Pair

function main() returns ExitCode
  var pairs = PairArray{}
  pairs.push(Pair{first: "alpha string that is long for heap", second: "beta string that is long for heap"})
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: struct-with-multiple-managed-fields -->
### Struct With Multiple Managed Fields
Each managed field in a struct needs its own refcount tracking.
```maxon

typealias Integer = int(i64.min to i64.max)

type MultiField
  export var a String
  export var b Integer
  export var c String
  export var d Integer
  export var e String
end 'MultiField'

typealias MultiArray = Array with MultiField

function main() returns ExitCode
  var items = MultiArray{}
  items.push(MultiField{a: "string a that is long enough for heap", b: 1, c: "string c that is long enough for heap", d: 2, e: "string e that is long enough for heap"})
  return 0
end 'main'
```
```exitcode
0
```
