---
feature: slice-memory
status: stable
keywords: [slice, memory, refcount, cow, parent]
category: memory
---
# Slice Memory Management

## Documentation

Slices are views into a parent string or array buffer. When a slice is created, it increments the parent buffer's reference count. When the slice is cleaned up, it decrements the parent's reference count.

### Slice Parent Reference Counting

When you create a slice:
1. The slice stores a pointer to the parent's buffer
2. The parent's reference count is incremented
3. The slice stores its offset within the parent (`parent_off`)

When a slice goes out of scope:
1. The slice decrements the parent buffer's reference count
2. If the parent's reference count reaches 0, the buffer is freed

### Nested Slices

A slice of a slice creates a new slice that still references the original parent buffer:

```maxon
var s = "hello world"
var first = s.slice(0, endIndex: 5)    // "hello" - parent is s's buffer
var nested = first.slice(0, endIndex: 3) // "hel" - parent is STILL s's buffer
```

The `parent_off` for a nested slice is computed as `source.parent_off + start`, ensuring proper cleanup.

## Tests

<!-- test: slice-cleanup-decrefs-parent -->
### Slice Cleanup Decrements Parent Refcount
When a slice goes out of scope, its parent's reference count must be decremented.
```maxon
function main() returns ExitCode
  var s = "hello world that is long enough to require heap allocation and more text to be sure"
  var start = s.startIndex()
  var idx = try s.findFirst(" ") otherwise s.endIndex()
  var sub = s.slice(start, endIndex: idx)
  print("{sub}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: static-string-slice -->
### Static String Slice Does Not Attempt Refcounting
Slicing a static string literal (mode=3) should create a static slice without attempting to incref/decref a non-existent refcount header.
```maxon
function main() returns ExitCode
  var s = "hello world"
  var start = s.startIndex()
  var idx = try s.findFirst(" ") otherwise s.endIndex()
  var sub = s.slice(start, endIndex: idx)
  print("{sub}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: slice-copy-increfs-parent -->
### Slice Copy Increments Parent Refcount
When a slice is copied, the parent's reference count must be incremented.
```maxon
function main() returns ExitCode
  var s = "hello world that is long enough to require heap allocation and more text to be sure"
  var start = s.startIndex()
  var idx = try s.findFirst(" ") otherwise s.endIndex()
  var sub1 = s.slice(start, endIndex: idx)
  var sub2 = sub1
  print("{sub1}\n")
  print("{sub2}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
hello
```

<!-- test: nested-slice-memory -->
### Nested Slice References Original Parent
A slice of a slice should reference the original parent buffer, not the intermediate slice.
```maxon
function main() returns ExitCode
  var s = "hello world that is long enough to require heap allocation and more text to be sure"
  var start = s.startIndex()
  var idx = try s.findFirst(" ") otherwise s.endIndex()
  var sub1 = s.slice(start, endIndex: idx)
  // Create a slice of the slice
  var sub1Start = sub1.startIndex()
  var sub1End = try sub1.findFirst("l") otherwise sub1.endIndex()
  var sub2 = sub1.slice(sub1Start, endIndex: sub1End)
  print("{sub2}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
he
```

<!-- test: slice-parent-outlives-original -->
### Slice Keeps Parent Alive After Original Goes Out of Scope
When the original string goes out of scope but a slice still exists, the buffer must remain alive.
```maxon
function getSlice() returns String
  var s = "hello world that is long enough to require heap allocation and more text to be sure"
  var start = s.startIndex()
  var idx = try s.findFirst(" ") otherwise s.endIndex()
  return s.slice(start, endIndex: idx)
end 'getSlice'

function main() returns ExitCode
  var sub = getSlice()
  print("{sub}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```
