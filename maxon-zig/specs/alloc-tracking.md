---
feature: alloc-tracking
status: experimental
keywords: [memory, allocation, tracking, debug, heap]
category: debugging
---

## Developer Notes

Memory allocation tracking provides runtime visibility into heap allocations for debugging memory leaks and understanding allocation patterns. When enabled via `--track-allocs`:

1. Every `heap_alloc` prints: `ALLOC #N: X bytes`
2. Every `heap_free` prints: `FREE #N: X bytes`
3. At program exit, prints leak summary and statistics

### Implementation

- Global tracking state in .data section
- Tracking table holds up to 256 concurrent allocations
- Each entry: {ptr, size, id} = 24 bytes
- `_start` wrapper enables tracking, calls main, prints summary

### Compile Flag

`maxon compile --track-allocs file.maxon`

## Documentation

# Allocation Tracking

Debug memory issues by tracking heap allocations at runtime.

## Usage

Compile with the `--track-allocs` flag:

```text
maxon compile --track-allocs myprogram.maxon
```

## Output Format

Each allocation prints:
```text
ALLOC #1: 80 bytes
```

Each free prints:
```text
FREE #1: 80 bytes
```

At exit, a summary is printed:
```text
=== ALLOC STATS ===
Allocated: 80 bytes
Freed:     80 bytes
Leaked:    0 bytes
```

## Tests

<!-- test: dynamic-array-no-leak -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    let size = 10
    var arr = array of size int
    arr[0] = 42
    return arr[0]
end 'main'
```
```exitcode
42
```
```stdout
ALLOC #1: 80 bytes
FREE #1: 80 bytes

=== ALLOC STATS ===
Allocated: 80 bytes
Freed:     80 bytes
Leaked:    0 bytes
```

<!-- test: no-alloc-empty-program -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    return 42
end 'main'
```
```exitcode
42
```
```stdout

=== ALLOC STATS ===
Allocated: 0 bytes
Freed:     0 bytes
Leaked:    0 bytes
```
