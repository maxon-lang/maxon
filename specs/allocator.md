---
feature: allocator
status: experimental
keywords: [slab, allocator, memory, os_alloc, slab_alloc, slab_free]
category: memory-safety
---

# Slab Allocator

Tests for the three-tier slab allocator, verifying observable behaviors via `--mm-trace` output.
The allocator uses 18 size classes and a 64MB arena for large objects.

## Tests

<!-- test: slab-first-alloc-triggers-os-alloc -->
<!-- MmTrace -->
The first heap allocation triggers a single `os_alloc size=67108864` (64MB arena).
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var p = Point{x: 1, y: 2}
  return p.x
end 'main'
```
```exitcode
1
```
```stderr
sl_init
    os_alloc size=67108864
mm_alloc Point #1 size=16 [allocator.main]
  sl_alloc Point #1 size=48 class=5
mm_incref Point #1 rc=1 [allocator.main]
mm_decref Point #1 rc=0 [allocator.main]
  mm_free Point #1
    sl_free Point #1 size=48 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=5
mm_raw_free #R1
  sl_free size=48 class=5
```

<!-- test: slab-class-routing-8-byte-user-data -->
<!-- MmTrace -->
A struct with 8 bytes user data: `mm_alloc` requests 40 bytes (8+32), slab slot needs 56 (40+16), routes to class 5 (64 bytes).
```maxon
typealias Integer = int(i64.min to i64.max)

type Tiny
  export var x Integer
end 'Tiny'

function main() returns ExitCode
  var t = Tiny{x: 7}
  return t.x
end 'main'
```
```exitcode
7
```
```stderr
sl_init
    os_alloc size=67108864
mm_alloc Tiny #1 size=8 [allocator.main]
  sl_alloc Tiny #1 size=40 class=5
mm_incref Tiny #1 rc=1 [allocator.main]
mm_decref Tiny #1 rc=0 [allocator.main]
  mm_free Tiny #1
    sl_free Tiny #1 size=48 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=5
mm_raw_free #R1
  sl_free size=48 class=5
```

<!-- test: slab-free-reuses-slot-no-os-alloc -->
<!-- MmTrace -->
Two allocations of the same type produce only one `os_alloc`. The slab allocator reuses freed slots without requesting more OS memory.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
  export var value Integer
end 'Box'

function make_box(v Integer) returns Box
  return Box{value: v}
end 'make_box'

function main() returns ExitCode
  var a = make_box(1)
  var b = make_box(2)
  return a.value + b.value
end 'main'
```
```exitcode
3
```
```stderr
sl_init
    os_alloc size=67108864
mm_alloc Box #1 size=8 [allocator.make_box]
  sl_alloc Box #1 size=40 class=5
mm_incref Box #1 rc=1 [allocator.make_box]
mm_transfer Box #1 rc=1 [allocator.make_box]
mm_alloc Box #2 size=8 [allocator.make_box]
  sl_alloc Box #2 size=40 class=5
mm_incref Box #2 rc=1 [allocator.make_box]
mm_transfer Box #2 rc=1 [allocator.make_box]
mm_decref Box #2 rc=0 [allocator.main]
  mm_free Box #2
    sl_free Box #2 size=48 class=5
mm_decref Box #1 rc=0 [allocator.main]
  mm_free Box #1
    sl_free Box #1 size=48 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=5
mm_raw_free #R1
  sl_free size=48 class=5
```

<!-- test: slab-two-types-two-classes -->
<!-- MmTrace -->
Two types with different sizes land in different size classes. Small (8 bytes user data) goes to class 5 (64), Large (40 bytes user data) goes to class 6 (96).
```maxon
typealias Integer = int(i64.min to i64.max)

type Small
  export var x Integer
end 'Small'

type Large
  export var a Integer
  export var b Integer
  export var c Integer
  export var d Integer
  export var e Integer
end 'Large'

function main() returns ExitCode
  var s = Small{x: 1}
  var l = Large{a: 1, b: 2, c: 3, d: 4, e: 5}
  return s.x + l.a
end 'main'
```
```exitcode
2
```
```stderr
sl_init
    os_alloc size=67108864
mm_alloc Small #1 size=8 [allocator.main]
  sl_alloc Small #1 size=40 class=5
mm_incref Small #1 rc=1 [allocator.main]
mm_alloc Large #2 size=40 [allocator.main]
  sl_alloc Large #2 size=72 class=6
mm_incref Large #2 rc=1 [allocator.main]
mm_decref Large #2 rc=0 [allocator.main]
  mm_free Large #2
    sl_free Large #2 size=80 class=6
mm_decref Small #1 rc=0 [allocator.main]
  mm_free Small #1
    sl_free Small #1 size=48 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=5
mm_raw_free #R1
  sl_free size=48 class=5
```

<!-- test: slab-arena-large-object -->
<!-- MmTrace -->
An Array with a large backing buffer (5000 i64 elements = 40000 bytes) exceeds the slab threshold (32752 bytes). The backing allocation routes to the arena-large bump path, shown as `slab_alloc size=40000 class=-1`. Arena-large allocations are not individually freed — the `slab_free size=40000 class=-1` trace line confirms the no-op free path.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
  var arr = IntArray{}
  arr.reserve(5000)
  return arr.count()
end 'main'
```
```exitcode
0
```
```stderr
sl_init
    os_alloc size=67108864
mm_alloc __ManagedMemory_Integer #1 size=32 [allocator.main]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=6
mm_alloc IntArray #2 size=16 [allocator.main]
  sl_alloc IntArray #2 size=48 class=5
mm_incref __ManagedMemory_Integer #1 rc=1 [allocator.main]
mm_incref IntArray #2 rc=1 [allocator.main]
mm_realloc __ManagedMemory_Integer #1 size=40000
  mm_raw_alloc #R1 size=40000 [realloc]
    sl_alloc size=40000 class=-1
mm_decref IntArray #2 rc=0 [allocator.main]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=40000 class=-1
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=80 class=6
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=5
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=5
mm_raw_free #R2
  sl_free size=48 class=5
```

<!-- test: slab-span-threshold-return -->
<!-- MmTrace -->
Class 17 (slot size 32768) holds exactly 1 object per span. Each `reserve(4000)` allocates a 32000-byte backing buffer (32000 < 32768), filling one span. After the first span is freed, the threshold return sends it back to mcentral. The second allocation reuses it — only one `os_alloc size=67108864` appears even though two class-17 spans are allocated and freed.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function alloc_large() returns IntArray
  var arr = IntArray{}
  arr.reserve(4000)
  return arr
end 'alloc_large'

function main() returns ExitCode
  var a = alloc_large()
  var b = alloc_large()
  return a.count() + b.count()
end 'main'
```
```exitcode
0
```
```stderr
sl_init
    os_alloc size=67108864
mm_alloc __ManagedMemory_Integer #1 size=32 [allocator.alloc_large]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=6
mm_alloc IntArray #2 size=16 [allocator.alloc_large]
  sl_alloc IntArray #2 size=48 class=5
mm_incref __ManagedMemory_Integer #1 rc=1 [allocator.alloc_large]
mm_incref IntArray #2 rc=1 [allocator.alloc_large]
mm_realloc __ManagedMemory_Integer #1 size=32000
  mm_raw_alloc #R1 size=32000 [realloc]
    sl_alloc size=32000 class=17
mm_transfer IntArray #2 rc=1 [allocator.alloc_large]
mm_alloc __ManagedMemory_Integer #3 size=32 [allocator.alloc_large]
  sl_alloc __ManagedMemory_Integer #3 size=64 class=6
mm_alloc IntArray #4 size=16 [allocator.alloc_large]
  sl_alloc IntArray #4 size=48 class=5
mm_incref __ManagedMemory_Integer #3 rc=1 [allocator.alloc_large]
mm_incref IntArray #4 rc=1 [allocator.alloc_large]
mm_realloc __ManagedMemory_Integer #3 size=32000
  mm_raw_alloc #R2 size=32000 [realloc]
    sl_alloc size=32000 class=17
mm_transfer IntArray #4 rc=1 [allocator.alloc_large]
mm_decref IntArray #4 rc=0 [allocator.main]
  mm_decref __ManagedMemory_Integer #3 rc=0 [~IntArray]
    mm_raw_free #R2
      sl_free size=32752 class=17
    mm_free __ManagedMemory_Integer #3
      sl_free __ManagedMemory_Integer #3 size=80 class=6
  mm_free IntArray #4
    sl_free IntArray #4 size=48 class=5
mm_decref IntArray #2 rc=0 [allocator.main]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=32752 class=17
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=80 class=6
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=5
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=5
mm_raw_free #R3
  sl_free size=48 class=5
```

<!-- test: slab-class-boundary-exact -->
<!-- MmTrace -->
A struct with exactly 2 `Integer` fields (16 bytes user data) needs `16 + 32 + 16 = 64` bytes — exactly the class 5 slot size (64), so it routes to class 5. A struct with 3 fields (24 bytes user data) needs `24 + 32 + 16 = 72` bytes — exceeds class 5, routes to class 6 (96 bytes). Verifies the `>=` boundary in the class routing loop.
```maxon
typealias Integer = int(i64.min to i64.max)

type TwoField
  export var a Integer
  export var b Integer
end 'TwoField'

type ThreeField
  export var a Integer
  export var b Integer
  export var c Integer
end 'ThreeField'

function main() returns ExitCode
  var t = TwoField{a: 1, b: 2}
  var h = ThreeField{a: 3, b: 4, c: 5}
  return t.a + h.a
end 'main'
```
```exitcode
4
```
```stderr
sl_init
    os_alloc size=67108864
mm_alloc TwoField #1 size=16 [allocator.main]
  sl_alloc TwoField #1 size=48 class=5
mm_incref TwoField #1 rc=1 [allocator.main]
mm_alloc ThreeField #2 size=24 [allocator.main]
  sl_alloc ThreeField #2 size=56 class=6
mm_incref ThreeField #2 rc=1 [allocator.main]
mm_decref ThreeField #2 rc=0 [allocator.main]
  mm_free ThreeField #2
    sl_free ThreeField #2 size=80 class=6
mm_decref TwoField #1 rc=0 [allocator.main]
  mm_free TwoField #1
    sl_free TwoField #1 size=48 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=5
mm_raw_free #R1
  sl_free size=48 class=5
```

<!-- test: slab-mixed-allocation-tiers -->
<!-- MmTrace -->
A single program exercises all three allocation tiers: a small struct routes to the slab (class 5), a medium array (5000 elements = 40000 bytes) routes to the arena-large bump path (class=-1), and a huge array (10485760 elements = 80MB) routes to OS-direct (class=-1 with `os_alloc`/`os_free`). All three tiers coexist and clean up correctly.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Tag
  export var id Integer
end 'Tag'

function main() returns ExitCode
  var tag = Tag{id: 42}
  var medium = IntArray{}
  medium.reserve(5000)
  var huge = IntArray{}
  huge.reserve(10485760)
  return tag.id
end 'main'
```
```exitcode
42
```
```stderr
sl_init
    os_alloc size=67108864
mm_alloc Tag #1 size=8 [allocator.main]
  sl_alloc Tag #1 size=40 class=5
mm_incref Tag #1 rc=1 [allocator.main]
mm_alloc __ManagedMemory_Integer #2 size=32 [allocator.main]
  sl_alloc __ManagedMemory_Integer #2 size=64 class=6
mm_alloc IntArray #3 size=16 [allocator.main]
  sl_alloc IntArray #3 size=48 class=5
mm_incref __ManagedMemory_Integer #2 rc=1 [allocator.main]
mm_incref IntArray #3 rc=1 [allocator.main]
mm_realloc __ManagedMemory_Integer #2 size=40000
  mm_raw_alloc #R1 size=40000 [realloc]
    sl_alloc size=40000 class=-1
mm_alloc __ManagedMemory_Integer #4 size=32 [allocator.main]
  sl_alloc __ManagedMemory_Integer #4 size=64 class=6
mm_alloc IntArray #5 size=16 [allocator.main]
  sl_alloc IntArray #5 size=48 class=5
mm_incref __ManagedMemory_Integer #4 rc=1 [allocator.main]
mm_incref IntArray #5 rc=1 [allocator.main]
mm_realloc __ManagedMemory_Integer #4 size=83886080
  mm_raw_alloc #R2 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886096
mm_decref IntArray #5 rc=0 [allocator.main]
  mm_decref __ManagedMemory_Integer #4 rc=0 [~IntArray]
    mm_raw_free #R2
      sl_free size=83886080 class=-1
        os_free size=83886096
    mm_free __ManagedMemory_Integer #4
      sl_free __ManagedMemory_Integer #4 size=80 class=6
  mm_free IntArray #5
    sl_free IntArray #5 size=48 class=5
mm_decref IntArray #3 rc=0 [allocator.main]
  mm_decref __ManagedMemory_Integer #2 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=40000 class=-1
    mm_free __ManagedMemory_Integer #2
      sl_free __ManagedMemory_Integer #2 size=80 class=6
  mm_free IntArray #3
    sl_free IntArray #3 size=48 class=5
mm_decref Tag #1 rc=0 [allocator.main]
  mm_free Tag #1
    sl_free Tag #1 size=48 class=5
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=5
mm_raw_free #R3
  sl_free size=48 class=5
```

<!-- test: slab-os-direct-huge-object -->
<!-- MmTrace -->
An allocation exceeding the 64MB arena size routes to the OS-direct path. `reserve(10485760)` = 80MB of i64 elements, allocated directly via `VirtualAlloc` and freed via `VirtualFree` on scope exit (`os_free size=83886096` = 80MB + 16-byte slab header). Unlike arena-large objects, OS-direct allocations are individually freed.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
  var arr = IntArray{}
  arr.reserve(10485760)
  return arr.count()
end 'main'
```
```exitcode
0
```
```stderr
sl_init
    os_alloc size=67108864
mm_alloc __ManagedMemory_Integer #1 size=32 [allocator.main]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=6
mm_alloc IntArray #2 size=16 [allocator.main]
  sl_alloc IntArray #2 size=48 class=5
mm_incref __ManagedMemory_Integer #1 rc=1 [allocator.main]
mm_incref IntArray #2 rc=1 [allocator.main]
mm_realloc __ManagedMemory_Integer #1 size=83886080
  mm_raw_alloc #R1 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886096
mm_decref IntArray #2 rc=0 [allocator.main]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=83886080 class=-1
        os_free size=83886096
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=80 class=6
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=5
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=5
mm_raw_free #R2
  sl_free size=48 class=5
```
