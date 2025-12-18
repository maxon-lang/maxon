---
feature: challenge-sized-arrays
status: draft
keywords: array, sized, allocation, memory
category: semantics
---

# Developer Notes

Tests for Challenge 5 from DEVELOPMENT_CHALLENGES.md: Empty and Zero-initialized Collections.

Empty collections must be handled correctly without memory leaks or invalid access.

# Documentation

## Sized Arrays

Sized arrays allocate space for a fixed number of elements.

## Tests

<!-- test: sized-array-default-values -->
```maxon
function main() returns int
    var arr = array of 3 int
    arr[1] = 42
    return arr[1]
end 'main'
```
```exitcode
42
```

<!-- test: sized-array-all-elements-writable -->
```maxon
function main() returns int
    var arr = array of 5 int
    arr[0] = 1
    arr[1] = 2
    arr[2] = 3
    arr[3] = 4
    arr[4] = 5
    return arr[0] + arr[1] + arr[2] + arr[3] + arr[4]
end 'main'
```
```exitcode
15
```
