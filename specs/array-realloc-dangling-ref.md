---
feature: array-realloc-dangling-ref
status: experimental
keywords: [array, realloc, dangling, reference, memory-safety, growth]
category: memory
---
# Array Realloc Dangling Reference

## Documentation

When an array grows, its backing buffer may be reallocated to a new address. Any references obtained before the growth (e.g., from `arr.get(index)`) must remain valid after the growth. Since `get` returns a reference to a managed element (with incref), the caller holds an independent reference to the element's heap object — not a pointer into the array's buffer — so growth should not invalidate it.

## Tests

<!-- test: string-ref-survives-array-growth -->
### String reference survives array growth
Get a string from a small array, then push many elements to force buffer reallocation. The original string reference must still be valid.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
  var arr = ["hello world this is a long string for heap allocation"]
  var s = try arr.get(0) otherwise ""

  // Push enough elements to force multiple growths (capacity: 4 -> 8 -> 16 -> 32)
  var i = 0
  while i < 30 'grow'
    arr.push("padding string that is long enough for heap allocation too")
    i = i + 1
  end 'grow'

  // The original reference must still work
  if s == "hello world this is a long string for heap allocation" 'check'
    print("ok\n")
    return 0
  end 'check'
  print("FAIL: got {s}\n")
  return 1
end 'main'
```
```exitcode
0
```
```stdout
ok
```

<!-- test: struct-ref-survives-array-growth -->
### Struct with string field reference survives array growth
Get a struct from an array, then grow the array. The struct's string field must still be valid.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
  export var name String
  export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
  var arr = ItemArray{}
  arr.push(Item{name: "first item with a long name for heap allocation", value: 42})

  var item = try arr.get(0) otherwise Item{name: "", value: 0}

  // Force many growths
  var i = 0
  while i < 30 'grow'
    arr.push(Item{name: "padding item with long name for heap allocation too", value: i})
    i = i + 1
  end 'grow'

  // The original reference must still work
  if item.name == "first item with a long name for heap allocation" 'check_name'
    if item.value == 42 'check_val'
      print("ok\n")
      return 0
    end 'check_val'
  end 'check_name'
  print("FAIL: name={item.name} value={item.value}\n")
  return 1
end 'main'
```
```exitcode
0
```
```stdout
ok
```

<!-- test: multiple-refs-survive-array-growth -->
### Multiple references survive array growth
Get references to multiple elements, then grow the array. All must remain valid.
```maxon
function main() returns ExitCode
  var arr = ["alpha string that is long enough for heap allocation purposes",
             "beta string that is long enough for heap allocation purposes",
             "gamma string that is long enough for heap allocation purposes"]

  var a = try arr.get(0) otherwise ""
  var b = try arr.get(1) otherwise ""
  var c = try arr.get(2) otherwise ""

  // Force many growths
  var i = 0
  while i < 30 'grow'
    arr.push("padding string that is long enough for heap allocation too x")
    i = i + 1
  end 'grow'

  // All original references must still work
  if a == "alpha string that is long enough for heap allocation purposes" 'a'
    if b == "beta string that is long enough for heap allocation purposes" 'b'
      if c == "gamma string that is long enough for heap allocation purposes" 'c'
        print("ok\n")
        return 0
      end 'c'
    end 'b'
  end 'a'
  print("FAIL\n")
  return 1
end 'main'
```
```exitcode
0
```
```stdout
ok
```
