---
feature: array-of-bytebuffer
status: experimental
keywords: [array, bytebuffer, nested, struct]
category: collections
---

# Array of ByteBuffer

## Documentation

Storing and retrieving ByteBuffer (`Array with Byte`) values from an outer Array.

## Tests

<!-- test: array-of-bytebuffer-push-get-count -->
Push a ByteBuffer into an outer Array, retrieve it, and call .count() on it.
```maxon
typealias Byte = byte(0 to u8.max)
typealias ByteBuffer = Array with Byte
typealias ByteBufferArray = Array with ByteBuffer

function main() returns ExitCode
  var outer = ByteBufferArray{}
  var inner = ByteBuffer{}
  inner.push(10)
  inner.push(20)
  inner.push(30)
  outer.push(inner)
  let retrieved = try outer.get(0) otherwise ByteBuffer{}
  print("{retrieved.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```
