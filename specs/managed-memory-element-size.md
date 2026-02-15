---
feature: managed-memory-element-size
status: stable
keywords: [managed-memory, element-size, intrinsics]
category: dev
---

## Documentation

The `__managed_memory_create` intrinsic takes an element_size parameter that determines
the size of each element in the buffer. This is used to calculate the total byte size
when allocating memory: `byte_size = count * element_size`.

When element_size=1 (for byte buffers), allocating N elements should allocate N bytes.
When element_size=8 (for int buffers), allocating N elements should allocate N*8 bytes.

## Tests

<!-- test: element-size-respected-in-create -->
Test that __managed_memory_create respects the element_size parameter.
When we create a buffer with element_size=1 and grow it, the allocation
should be based on 1-byte elements, not 8-byte elements.

This test creates a byte buffer, writes sequential byte values at specific
positions, then reads them back. If element_size is incorrectly hardcoded to 8,
the byte positions would be calculated wrong (multiplied by 8 instead of 1).
```maxon
function main() returns Integer
  // Create a managed memory for 10 bytes (element_size=1)
  var managed = __managed_memory_create(10, 1)

  // Write bytes at positions 0, 1, 2
  __managed_memory_set_byte(managed, 0, 65)  // 'A'
  __managed_memory_set_byte(managed, 1, 66)  // 'B'
  __managed_memory_set_byte(managed, 2, 67)  // 'C'

  // Read them back
  var b0 = __managed_memory_byte_at(managed, 0)
  var b1 = __managed_memory_byte_at(managed, 1)
  var b2 = __managed_memory_byte_at(managed, 2)

  // Verify values
  if b0 != 65 'check0'
    return 1
  end 'check0'
  if b1 != 66 'check1'
    return 2
  end 'check1'
  if b2 != 67 'check2'
    return 3
  end 'check2'

  return 0
end 'main'
```
```exitcode
0
```

<!-- test: element-size-in-grow -->
Test that __managed_memory_grow uses the correct element_size.
This tests a scenario where we need to grow a byte buffer.
```maxon
function main() returns Integer
  // Create a small byte buffer
  var managed = __managed_memory_create(4, 1)

  // Write initial bytes
  __managed_memory_set_byte(managed, 0, 10)
  __managed_memory_set_byte(managed, 1, 20)
  __managed_memory_set_byte(managed, 2, 30)

  // Grow the buffer (should allocate 8 bytes, not 64)
  __managed_memory_grow(managed, 8)

  // Write more bytes after grow
  __managed_memory_set_byte(managed, 3, 40)
  __managed_memory_set_byte(managed, 4, 50)

  // Verify all bytes
  var sum = __managed_memory_byte_at(managed, 0)
  sum = sum + __managed_memory_byte_at(managed, 1)
  sum = sum + __managed_memory_byte_at(managed, 2)
  sum = sum + __managed_memory_byte_at(managed, 3)
  sum = sum + __managed_memory_byte_at(managed, 4)

  // 10 + 20 + 30 + 40 + 50 = 150
  return sum
end 'main'
```
```exitcode
150
```
