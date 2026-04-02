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
The first heap allocation triggers a single `os_alloc size=67108864` (64MB arena). `@heap` forces heap allocation for allocator testing.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	@heap var p = Point.create(x: 1, y: 2)
	return p.x
end 'main'
```
```exitcode
1
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [Point.create]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [Point.create]
mm_transfer Point #1 rc=1 [Point.create]
mm_decref Point #1 rc=0 [main]
  mm_free Point #1
    sl_free Point #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: slab-class-routing-8-byte-user-data -->
<!-- MmTrace -->
A struct with 8 bytes user data: `mm_alloc` requests 40 bytes (8+32), routes to class 4 (48 bytes) since 48 >= 40. `@heap` forces heap allocation.
```maxon
typealias Integer = int(i64.min to i64.max)

type Tiny
	export var x Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Tiny'

function main() returns ExitCode
	@heap var t = Tiny.create(x: 7)
	return t.x
end 'main'
```
```exitcode
7
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Tiny #1 size=8 [Tiny.create]
  sl_alloc Tiny #1 size=40 class=4
mm_incref Tiny #1 rc=1 [Tiny.create]
mm_transfer Tiny #1 rc=1 [Tiny.create]
mm_decref Tiny #1 rc=0 [main]
  mm_free Tiny #1
    sl_free Tiny #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: slab-free-reuses-slot-no-os-alloc -->
<!-- MmTrace -->
Two allocations of the same type produce only one `os_alloc`. The slab allocator reuses freed slots without requesting more OS memory.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function make_box(v Integer) returns Box
	return Box.create(value: v)
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
mm_alloc Box #1 size=8 [Box.create]
  sl_alloc Box #1 size=40 class=4
mm_incref Box #1 rc=1 [Box.create]
mm_transfer Box #1 rc=1 [Box.create]
mm_transfer Box #1 rc=1 [allocator.make_box]
mm_alloc Box #2 size=8 [Box.create]
  sl_alloc Box #2 size=40 class=4
mm_incref Box #2 rc=1 [Box.create]
mm_transfer Box #2 rc=1 [Box.create]
mm_transfer Box #2 rc=1 [allocator.make_box]
mm_decref Box #2 rc=0 [main]
  mm_free Box #2
    sl_free Box #2 size=48 class=4
mm_decref Box #1 rc=0 [main]
  mm_free Box #1
    sl_free Box #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: slab-two-types-two-classes -->
<!-- MmTrace -->
Two types with different sizes land in different size classes. Small (8 bytes user data, 40 total) goes to class 4 (48), Large (40 bytes user data, 72 total) goes to class 6 (96). `@heap` forces heap allocation.
```maxon
typealias Integer = int(i64.min to i64.max)

type Small
	export var x Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Small'

type Large
	export var a Integer
	export var b Integer
	export var c Integer
	export var d Integer
	export var e Integer

	static function create(a Integer, b Integer, c Integer, d Integer, e Integer) returns Self
		return Self{a: a, b: b, c: c, d: d, e: e}
	end 'create'
end 'Large'

function main() returns ExitCode
	@heap var s = Small.create(x: 1)
	@heap var l = Large.create(a: 1, b: 2, c: 3, d: 4, e: 5)
	return s.x + l.a
end 'main'
```
```exitcode
2
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Small #1 size=8 [Small.create]
  sl_alloc Small #1 size=40 class=4
mm_incref Small #1 rc=1 [Small.create]
mm_transfer Small #1 rc=1 [Small.create]
mm_alloc Large #2 size=40 [Large.create]
  sl_alloc Large #2 size=72 class=6
mm_incref Large #2 rc=1 [Large.create]
mm_transfer Large #2 rc=1 [Large.create]
mm_decref Large #2 rc=0 [main]
  mm_free Large #2
    sl_free Large #2 size=96 class=6
mm_decref Small #1 rc=0 [main]
  mm_free Small #1
    sl_free Small #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: slab-arena-large-object -->
<!-- MmTrace -->
An Array with a large backing buffer (5000 i64 elements = 40000 bytes) exceeds the slab threshold (32768 bytes). The backing allocation routes to the arena-large bump path, shown as `slab_alloc size=40000 class=-1`. Arena-large frees are silent no-ops — no trace is emitted on free.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.empty()
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
mm_alloc __ManagedMemory_Integer #1 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=5
mm_alloc IntArray #2 size=16 [IntArray.empty]
  sl_alloc IntArray #2 size=48 class=4
mm_incref __ManagedMemory_Integer #1 rc=1 [IntArray.empty]
mm_incref IntArray #2 rc=1 [IntArray.empty]
mm_transfer IntArray #2 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #1 size=40000
  mm_raw_alloc #R1 size=40000 [realloc]
    sl_alloc size=40000 class=-1
mm_decref IntArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=40960 class=-1
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=64 class=5
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: slab-span-threshold-return -->
<!-- MmTrace -->
Class 17 (slot size 32768) holds exactly 1 object per span. Each `reserve(4000)` allocates a 32000-byte backing buffer (32000 <= 32768), filling one span. After the first span is freed, the threshold return sends it back to mcentral. The second allocation reuses it — only one `os_alloc size=67108864` appears even though two class-17 spans are allocated and freed.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function alloc_large() returns IntArray
	var arr = IntArray.empty()
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
mm_alloc __ManagedMemory_Integer #1 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=5
mm_alloc IntArray #2 size=16 [IntArray.empty]
  sl_alloc IntArray #2 size=48 class=4
mm_incref __ManagedMemory_Integer #1 rc=1 [IntArray.empty]
mm_incref IntArray #2 rc=1 [IntArray.empty]
mm_transfer IntArray #2 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #1 size=32000
  mm_raw_alloc #R1 size=32000 [realloc]
    sl_alloc size=32000 class=17
mm_transfer IntArray #2 rc=1 [allocator.alloc_large]
mm_alloc __ManagedMemory_Integer #3 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #3 size=64 class=5
mm_alloc IntArray #4 size=16 [IntArray.empty]
  sl_alloc IntArray #4 size=48 class=4
mm_incref __ManagedMemory_Integer #3 rc=1 [IntArray.empty]
mm_incref IntArray #4 rc=1 [IntArray.empty]
mm_transfer IntArray #4 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #3 size=32000
  mm_raw_alloc #R2 size=32000 [realloc]
    sl_alloc size=32000 class=17
mm_transfer IntArray #4 rc=1 [allocator.alloc_large]
mm_decref IntArray #4 rc=0 [main]
  mm_decref __ManagedMemory_Integer #3 rc=0 [~IntArray]
    mm_raw_free #R2
      sl_free size=32768 class=17
    mm_free __ManagedMemory_Integer #3
      sl_free __ManagedMemory_Integer #3 size=64 class=5
  mm_free IntArray #4
    sl_free IntArray #4 size=48 class=4
mm_decref IntArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=32768 class=17
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=64 class=5
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
```

<!-- test: slab-class-boundary-exact -->
<!-- MmTrace -->
A struct with exactly 2 `Integer` fields (16 bytes user data) needs `16 + 32 = 48` bytes — exactly the class 4 slot size (48), so it routes to class 4. A struct with 3 fields (24 bytes user data) needs `24 + 32 = 56` bytes — exceeds class 4, routes to class 5 (64 bytes). Verifies the `>=` boundary in the class routing loop. `@heap` forces heap allocation.
```maxon
typealias Integer = int(i64.min to i64.max)

type TwoField
	export var a Integer
	export var b Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'TwoField'

type ThreeField
	export var a Integer
	export var b Integer
	export var c Integer

	static function create(a Integer, b Integer, c Integer) returns Self
		return Self{a: a, b: b, c: c}
	end 'create'
end 'ThreeField'

function main() returns ExitCode
	@heap var t = TwoField.create(a: 1, b: 2)
	@heap var h = ThreeField.create(a: 3, b: 4, c: 5)
	return t.a + h.a
end 'main'
```
```exitcode
4
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc TwoField #1 size=16 [TwoField.create]
  sl_alloc TwoField #1 size=48 class=4
mm_incref TwoField #1 rc=1 [TwoField.create]
mm_transfer TwoField #1 rc=1 [TwoField.create]
mm_alloc ThreeField #2 size=24 [ThreeField.create]
  sl_alloc ThreeField #2 size=56 class=5
mm_incref ThreeField #2 rc=1 [ThreeField.create]
mm_transfer ThreeField #2 rc=1 [ThreeField.create]
mm_decref ThreeField #2 rc=0 [main]
  mm_free ThreeField #2
    sl_free ThreeField #2 size=64 class=5
mm_decref TwoField #1 rc=0 [main]
  mm_free TwoField #1
    sl_free TwoField #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: slab-mixed-allocation-tiers -->
<!-- MmTrace -->
A single program exercises all three allocation tiers: a small struct routes to the slab (class 4), a medium array (5000 elements = 40000 bytes) routes to the arena-large bump path (class=-1), and a huge array (10485760 elements = 80MB) routes to OS-direct (class=-1 with `os_alloc`/`os_free`). All three tiers coexist and clean up correctly.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Tag
	export var id Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Tag'

function main() returns ExitCode
	@heap var tag = Tag.create(id: 42)
	var medium = IntArray.empty()
	medium.reserve(5000)
	var huge = IntArray.empty()
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
mm_alloc Tag #1 size=8 [Tag.create]
  sl_alloc Tag #1 size=40 class=4
mm_incref Tag #1 rc=1 [Tag.create]
mm_transfer Tag #1 rc=1 [Tag.create]
mm_alloc __ManagedMemory_Integer #2 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #2 size=64 class=5
mm_alloc IntArray #3 size=16 [IntArray.empty]
  sl_alloc IntArray #3 size=48 class=4
mm_incref __ManagedMemory_Integer #2 rc=1 [IntArray.empty]
mm_incref IntArray #3 rc=1 [IntArray.empty]
mm_transfer IntArray #3 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #2 size=40000
  mm_raw_alloc #R1 size=40000 [realloc]
    sl_alloc size=40000 class=-1
mm_alloc __ManagedMemory_Integer #4 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #4 size=64 class=5
mm_alloc IntArray #5 size=16 [IntArray.empty]
  sl_alloc IntArray #5 size=48 class=4
mm_incref __ManagedMemory_Integer #4 rc=1 [IntArray.empty]
mm_incref IntArray #5 rc=1 [IntArray.empty]
mm_transfer IntArray #5 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #4 size=83886080
  mm_raw_alloc #R2 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886080
    os_alloc size=4096
mm_decref IntArray #5 rc=0 [main]
  mm_decref __ManagedMemory_Integer #4 rc=0 [~IntArray]
    mm_raw_free #R2
      sl_free size=83886080 class=-1
        os_free size=83886080
    mm_free __ManagedMemory_Integer #4
      sl_free __ManagedMemory_Integer #4 size=64 class=5
  mm_free IntArray #5
    sl_free IntArray #5 size=48 class=4
mm_decref IntArray #3 rc=0 [main]
  mm_decref __ManagedMemory_Integer #2 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=40960 class=-1
    mm_free __ManagedMemory_Integer #2
      sl_free __ManagedMemory_Integer #2 size=64 class=5
  mm_free IntArray #3
    sl_free IntArray #3 size=48 class=4
mm_decref Tag #1 rc=0 [main]
  mm_free Tag #1
    sl_free Tag #1 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
```

<!-- test: slab-os-direct-huge-object -->
<!-- MmTrace -->
An allocation exceeding the 64MB arena size routes to the OS-direct path. `reserve(10485760)` = 80MB of i64 elements, allocated directly via `VirtualAlloc` and freed via `VirtualFree` on scope exit (`os_free size=83886080`). Unlike arena-large objects, OS-direct allocations are individually freed.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.empty()
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
mm_alloc __ManagedMemory_Integer #1 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=5
mm_alloc IntArray #2 size=16 [IntArray.empty]
  sl_alloc IntArray #2 size=48 class=4
mm_incref __ManagedMemory_Integer #1 rc=1 [IntArray.empty]
mm_incref IntArray #2 rc=1 [IntArray.empty]
mm_transfer IntArray #2 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #1 size=83886080
  mm_raw_alloc #R1 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886080
    os_alloc size=4096
mm_decref IntArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=83886080 class=-1
        os_free size=83886080
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=64 class=5
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: slab-arena-large-chunk-reuse -->
<!-- MmTrace -->
Arena-large allocations are now freeable. When a 40000-byte backing buffer is freed, its bitmap chunks are returned. A second `reserve(5000)` reuses those chunks — no new `os_alloc` appears. Both allocations show `class=-1` and both frees show `sl_free ... class=-1`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function alloc_medium() returns IntArray
	var arr = IntArray.empty()
	arr.reserve(5000)
	return arr
end 'alloc_medium'

function main() returns ExitCode
	var a = alloc_medium()
	var b = alloc_medium()
	return a.count() + b.count()
end 'main'
```
```exitcode
0
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Integer #1 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=5
mm_alloc IntArray #2 size=16 [IntArray.empty]
  sl_alloc IntArray #2 size=48 class=4
mm_incref __ManagedMemory_Integer #1 rc=1 [IntArray.empty]
mm_incref IntArray #2 rc=1 [IntArray.empty]
mm_transfer IntArray #2 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #1 size=40000
  mm_raw_alloc #R1 size=40000 [realloc]
    sl_alloc size=40000 class=-1
mm_transfer IntArray #2 rc=1 [allocator.alloc_medium]
mm_alloc __ManagedMemory_Integer #3 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #3 size=64 class=5
mm_alloc IntArray #4 size=16 [IntArray.empty]
  sl_alloc IntArray #4 size=48 class=4
mm_incref __ManagedMemory_Integer #3 rc=1 [IntArray.empty]
mm_incref IntArray #4 rc=1 [IntArray.empty]
mm_transfer IntArray #4 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #3 size=40000
  mm_raw_alloc #R2 size=40000 [realloc]
    sl_alloc size=40000 class=-1
mm_transfer IntArray #4 rc=1 [allocator.alloc_medium]
mm_decref IntArray #4 rc=0 [main]
  mm_decref __ManagedMemory_Integer #3 rc=0 [~IntArray]
    mm_raw_free #R2
      sl_free size=40960 class=-1
    mm_free __ManagedMemory_Integer #3
      sl_free __ManagedMemory_Integer #3 size=64 class=5
  mm_free IntArray #4
    sl_free IntArray #4 size=48 class=4
mm_decref IntArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=40960 class=-1
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=64 class=5
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
```

<!-- test: slab-arena-large-sequential-reuse -->
<!-- MmTrace -->
Two arena-large arrays allocated and freed sequentially. The first is fully freed before the second is allocated, proving chunks are reused. Only one `os_alloc size=67108864` should appear.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function use_medium() returns Integer
	var arr = IntArray.empty()
	arr.reserve(5000)
	return arr.count()
end 'use_medium'

function main() returns ExitCode
	var x = use_medium()
	var y = use_medium()
	return x + y
end 'main'
```
```exitcode
0
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Integer #1 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=5
mm_alloc IntArray #2 size=16 [IntArray.empty]
  sl_alloc IntArray #2 size=48 class=4
mm_incref __ManagedMemory_Integer #1 rc=1 [IntArray.empty]
mm_incref IntArray #2 rc=1 [IntArray.empty]
mm_transfer IntArray #2 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #1 size=40000
  mm_raw_alloc #R1 size=40000 [realloc]
    sl_alloc size=40000 class=-1
mm_decref IntArray #2 rc=0 [allocator.use_medium]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=40960 class=-1
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=64 class=5
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=4
mm_alloc __ManagedMemory_Integer #3 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #3 size=64 class=5
mm_alloc IntArray #4 size=16 [IntArray.empty]
  sl_alloc IntArray #4 size=48 class=4
mm_incref __ManagedMemory_Integer #3 rc=1 [IntArray.empty]
mm_incref IntArray #4 rc=1 [IntArray.empty]
mm_transfer IntArray #4 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #3 size=40000
  mm_raw_alloc #R2 size=40000 [realloc]
    sl_alloc size=40000 class=-1
mm_decref IntArray #4 rc=0 [allocator.use_medium]
  mm_decref __ManagedMemory_Integer #3 rc=0 [~IntArray]
    mm_raw_free #R2
      sl_free size=40960 class=-1
    mm_free __ManagedMemory_Integer #3
      sl_free __ManagedMemory_Integer #3 size=64 class=5
  mm_free IntArray #4
    sl_free IntArray #4 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
```

<!-- test: slab-os-direct-multiple -->
<!-- MmTrace -->
Two OS-direct allocations (>64MB each) coexist and are freed independently. Both show `os_alloc` and `os_free` in the trace. The dynamic tracking array handles multiple entries correctly.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var huge1 = IntArray.empty()
	huge1.reserve(10485760)
	var huge2 = IntArray.empty()
	huge2.reserve(10485760)
	return huge1.count() + huge2.count()
end 'main'
```
```exitcode
0
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Integer #1 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=5
mm_alloc IntArray #2 size=16 [IntArray.empty]
  sl_alloc IntArray #2 size=48 class=4
mm_incref __ManagedMemory_Integer #1 rc=1 [IntArray.empty]
mm_incref IntArray #2 rc=1 [IntArray.empty]
mm_transfer IntArray #2 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #1 size=83886080
  mm_raw_alloc #R1 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886080
    os_alloc size=4096
mm_alloc __ManagedMemory_Integer #3 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #3 size=64 class=5
mm_alloc IntArray #4 size=16 [IntArray.empty]
  sl_alloc IntArray #4 size=48 class=4
mm_incref __ManagedMemory_Integer #3 rc=1 [IntArray.empty]
mm_incref IntArray #4 rc=1 [IntArray.empty]
mm_transfer IntArray #4 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #3 size=83886080
  mm_raw_alloc #R2 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886080
mm_decref IntArray #4 rc=0 [main]
  mm_decref __ManagedMemory_Integer #3 rc=0 [~IntArray]
    mm_raw_free #R2
      sl_free size=83886080 class=-1
        os_free size=83886080
    mm_free __ManagedMemory_Integer #3
      sl_free __ManagedMemory_Integer #3 size=64 class=5
  mm_free IntArray #4
    sl_free IntArray #4 size=48 class=4
mm_decref IntArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=83886080 class=-1
        os_free size=83886080
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=64 class=5
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
```

<!-- test: slab-os-direct-sequential-reuse -->
<!-- MmTrace -->
Two OS-direct allocations done sequentially. The first is fully freed (VirtualFree) before the second is allocated. Both appear as separate `os_alloc`/`os_free` pairs since OS-direct memory is returned to the OS.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function use_huge() returns Integer
	var arr = IntArray.empty()
	arr.reserve(10485760)
	return arr.count()
end 'use_huge'

function main() returns ExitCode
	var x = use_huge()
	var y = use_huge()
	return x + y
end 'main'
```
```exitcode
0
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Integer #1 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=5
mm_alloc IntArray #2 size=16 [IntArray.empty]
  sl_alloc IntArray #2 size=48 class=4
mm_incref __ManagedMemory_Integer #1 rc=1 [IntArray.empty]
mm_incref IntArray #2 rc=1 [IntArray.empty]
mm_transfer IntArray #2 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #1 size=83886080
  mm_raw_alloc #R1 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886080
    os_alloc size=4096
mm_decref IntArray #2 rc=0 [allocator.use_huge]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=83886080 class=-1
        os_free size=83886080
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=64 class=5
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=4
mm_alloc __ManagedMemory_Integer #3 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #3 size=64 class=5
mm_alloc IntArray #4 size=16 [IntArray.empty]
  sl_alloc IntArray #4 size=48 class=4
mm_incref __ManagedMemory_Integer #3 rc=1 [IntArray.empty]
mm_incref IntArray #4 rc=1 [IntArray.empty]
mm_transfer IntArray #4 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #3 size=83886080
  mm_raw_alloc #R2 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886080
mm_decref IntArray #4 rc=0 [allocator.use_huge]
  mm_decref __ManagedMemory_Integer #3 rc=0 [~IntArray]
    mm_raw_free #R2
      sl_free size=83886080 class=-1
        os_free size=83886080
    mm_free __ManagedMemory_Integer #3
      sl_free __ManagedMemory_Integer #3 size=64 class=5
  mm_free IntArray #4
    sl_free IntArray #4 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
```

<!-- test: slab-os-direct-sorted-array -->
<!-- MmTrace -->
Four concurrent OS-direct allocations exercise the sorted tracking array. The array must maintain sort order by pointer for binary search lookups. Entries are freed in LIFO order (inner scopes first), exercising removal from different positions in the sorted array.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.empty()
	a.reserve(10485760)
	var b = IntArray.empty()
	b.reserve(10485760)
	var c = IntArray.empty()
	c.reserve(10485760)
	var d = IntArray.empty()
	d.reserve(10485760)
	return a.count() + b.count() + c.count() + d.count()
end 'main'
```
```exitcode
0
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Integer #1 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #1 size=64 class=5
mm_alloc IntArray #2 size=16 [IntArray.empty]
  sl_alloc IntArray #2 size=48 class=4
mm_incref __ManagedMemory_Integer #1 rc=1 [IntArray.empty]
mm_incref IntArray #2 rc=1 [IntArray.empty]
mm_transfer IntArray #2 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #1 size=83886080
  mm_raw_alloc #R1 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886080
    os_alloc size=4096
mm_alloc __ManagedMemory_Integer #3 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #3 size=64 class=5
mm_alloc IntArray #4 size=16 [IntArray.empty]
  sl_alloc IntArray #4 size=48 class=4
mm_incref __ManagedMemory_Integer #3 rc=1 [IntArray.empty]
mm_incref IntArray #4 rc=1 [IntArray.empty]
mm_transfer IntArray #4 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #3 size=83886080
  mm_raw_alloc #R2 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886080
mm_alloc __ManagedMemory_Integer #5 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #5 size=64 class=5
mm_alloc IntArray #6 size=16 [IntArray.empty]
  sl_alloc IntArray #6 size=48 class=4
mm_incref __ManagedMemory_Integer #5 rc=1 [IntArray.empty]
mm_incref IntArray #6 rc=1 [IntArray.empty]
mm_transfer IntArray #6 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #5 size=83886080
  mm_raw_alloc #R3 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886080
mm_alloc __ManagedMemory_Integer #7 size=32 [IntArray.empty]
  sl_alloc __ManagedMemory_Integer #7 size=64 class=5
mm_alloc IntArray #8 size=16 [IntArray.empty]
  sl_alloc IntArray #8 size=48 class=4
mm_incref __ManagedMemory_Integer #7 rc=1 [IntArray.empty]
mm_incref IntArray #8 rc=1 [IntArray.empty]
mm_transfer IntArray #8 rc=1 [IntArray.empty]
mm_realloc __ManagedMemory_Integer #7 size=83886080
  mm_raw_alloc #R4 size=83886080 [realloc]
    sl_alloc size=83886080 class=-1
      os_alloc size=83886080
mm_decref IntArray #8 rc=0 [main]
  mm_decref __ManagedMemory_Integer #7 rc=0 [~IntArray]
    mm_raw_free #R4
      sl_free size=83886080 class=-1
        os_free size=83886080
    mm_free __ManagedMemory_Integer #7
      sl_free __ManagedMemory_Integer #7 size=64 class=5
  mm_free IntArray #8
    sl_free IntArray #8 size=48 class=4
mm_decref IntArray #6 rc=0 [main]
  mm_decref __ManagedMemory_Integer #5 rc=0 [~IntArray]
    mm_raw_free #R3
      sl_free size=83886080 class=-1
        os_free size=83886080
    mm_free __ManagedMemory_Integer #5
      sl_free __ManagedMemory_Integer #5 size=64 class=5
  mm_free IntArray #6
    sl_free IntArray #6 size=48 class=4
mm_decref IntArray #4 rc=0 [main]
  mm_decref __ManagedMemory_Integer #3 rc=0 [~IntArray]
    mm_raw_free #R2
      sl_free size=83886080 class=-1
        os_free size=83886080
    mm_free __ManagedMemory_Integer #3
      sl_free __ManagedMemory_Integer #3 size=64 class=5
  mm_free IntArray #4
    sl_free IntArray #4 size=48 class=4
mm_decref IntArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Integer #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=83886080 class=-1
        os_free size=83886080
    mm_free __ManagedMemory_Integer #1
      sl_free __ManagedMemory_Integer #1 size=64 class=5
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=4
mm_raw_alloc #R5 size=40
  sl_alloc size=40 class=4
mm_raw_free #R5
  sl_free size=48 class=4
```

<!-- test: slab-string-array-push -->
A StringArray push triggers a realloc of the backing buffer. Managed String pointers in the buffer must survive the realloc without corruption.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.empty()
	arr.push("hello")
	return 0
end 'main'
```
```exitcode
0
```
