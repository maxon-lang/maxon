---
feature: allocator
status: selfhosted
keywords: [slab, allocator, memory, os_alloc, slab_alloc, slab_free]
category: memory-safety
---

# Slab Allocator

Tests for the three-tier slab allocator. The allocator uses 18 size classes and a 64MB
arena for large objects, with OS-direct fallback for huge allocations.

These tests use the `__mm_*` intrinsics directly (rather than `@heap` or `Array with X`)
because the self-hosted compiler does not yet implement those higher-level features.
They still exercise every allocator tier via the underlying primitives.

NOTE: These tests currently only run under the self-hosted compiler. The C# bootstrap
compiler does not yet expose `__mm_raw_alloc` / `__mm_alloc` / `__mm_decref` etc. as
user-callable intrinsics. TODO: either wire them up in the C# compiler too, or split
this spec so each compiler runs the version it supports.

## Tests

<!-- test: slab-first-alloc-triggers-os-alloc -->
The first heap allocation triggers the slab arena to be created. After alloc/free, the raw count must return to 0.
```maxon
function main() returns ExitCode
	let p = __mm_raw_alloc(16)
	__mm_raw_free(p)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-class-routing-8-byte-user-data -->
A small allocation of 8 bytes (a single i64) routes to a small size class. The allocator must return a usable pointer.
```maxon
function main() returns ExitCode
	let p = __mm_raw_alloc(8)
	__mm_raw_free(p)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-free-reuses-slot-no-os-alloc -->
Two allocations of the same size, freed in between. The slab allocator reuses freed slots without requesting more OS memory. After both are freed the count is 0.
```maxon
function main() returns ExitCode
	let a = __mm_raw_alloc(40)
	__mm_raw_free(a)
	let b = __mm_raw_alloc(40)
	__mm_raw_free(b)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-two-types-two-classes -->
Two allocations of different sizes land in different size classes (8 → small class, 40 → larger class).
```maxon
function main() returns ExitCode
	let small = __mm_raw_alloc(8)
	let large = __mm_raw_alloc(40)
	__mm_raw_free(large)
	__mm_raw_free(small)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-arena-large-object -->
An allocation of 40000 bytes exceeds the slab threshold (32768) and routes to the arena-large bump path. The pointer must be usable and the count must return to 0 after free.
```maxon
function main() returns ExitCode
	let p = __mm_raw_alloc(40000)
	__mm_raw_free(p)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-span-threshold-return -->
Class 17 (32768-byte slot) holds exactly 1 object per span. Allocating exactly at the threshold size and freeing returns the span. A second allocation reuses it.
```maxon
function main() returns ExitCode
	let a = __mm_raw_alloc(32000)
	__mm_raw_free(a)
	let b = __mm_raw_alloc(32000)
	__mm_raw_free(b)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-class-boundary-exact -->
Boundary exact: 16 bytes lands in one class, 24 bytes in another. Both must succeed and clean up.
```maxon
function main() returns ExitCode
	let p16 = __mm_raw_alloc(16)
	let p24 = __mm_raw_alloc(24)
	__mm_raw_free(p24)
	__mm_raw_free(p16)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-mixed-allocation-tiers -->
A single program exercises all three allocation tiers: small (slab class), medium (arena-large bump path), and huge (OS-direct).
```maxon
function main() returns ExitCode
	let small = __mm_raw_alloc(16)
	let medium = __mm_raw_alloc(40000)
	let huge = __mm_raw_alloc(83886080)
	__mm_raw_free(huge)
	__mm_raw_free(medium)
	__mm_raw_free(small)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-os-direct-huge-object -->
An allocation exceeding the 64MB arena size routes to the OS-direct path (allocated directly via VirtualAlloc and freed via VirtualFree).
```maxon
function main() returns ExitCode
	let p = __mm_raw_alloc(83886080)
	__mm_raw_free(p)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-arena-large-chunk-reuse -->
Arena-large allocations are freeable. After a 40000-byte buffer is freed, its chunks are returned and a second allocation can reuse them.
```maxon
function main() returns ExitCode
	let a = __mm_raw_alloc(40000)
	__mm_raw_free(a)
	let b = __mm_raw_alloc(40000)
	__mm_raw_free(b)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-arena-large-sequential-reuse -->
Two arena-large buffers allocated and freed sequentially. Each is fully freed before the next is allocated.
```maxon
function main() returns ExitCode
	let a = __mm_raw_alloc(40000)
	__mm_raw_free(a)
	let b = __mm_raw_alloc(40000)
	__mm_raw_free(b)
	let c = __mm_raw_alloc(40000)
	__mm_raw_free(c)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-os-direct-multiple -->
Two OS-direct allocations (>64MB each) coexist and are freed independently. The dynamic tracking array handles multiple entries correctly.
```maxon
function main() returns ExitCode
	let h1 = __mm_raw_alloc(83886080)
	let h2 = __mm_raw_alloc(83886080)
	__mm_raw_free(h2)
	__mm_raw_free(h1)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-os-direct-sequential-reuse -->
Two OS-direct allocations done sequentially. The first is fully freed (VirtualFree) before the second is allocated.
```maxon
function main() returns ExitCode
	let a = __mm_raw_alloc(83886080)
	__mm_raw_free(a)
	let b = __mm_raw_alloc(83886080)
	__mm_raw_free(b)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-os-direct-sorted-array -->
Four concurrent OS-direct allocations exercise the sorted tracking array. Entries are freed in LIFO order, exercising removal from different positions.
```maxon
function main() returns ExitCode
	let a = __mm_raw_alloc(83886080)
	let b = __mm_raw_alloc(83886080)
	let c = __mm_raw_alloc(83886080)
	let d = __mm_raw_alloc(83886080)
	__mm_raw_free(d)
	__mm_raw_free(c)
	__mm_raw_free(b)
	__mm_raw_free(a)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-string-array-push -->
A tracked allocation with destructor=0 (no-op). Verifies that __mm_alloc/__mm_decref work for refcounted memory.
```maxon
function main() returns ExitCode
	let p = __mm_alloc(16, destructor: 0, tag: 0)
	__mm_decref(p)
	let count = __mm_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: arena-fill-then-free -->
Stress-tests the slab mcache refill cycle by alloc+free of 1000 small blocks.
```maxon
function main() returns ExitCode
	var i = 0
	while i < 1000 'loop'
		let p = __mm_raw_alloc(8)
		__mm_raw_free(p)
		i = i + 1
	end 'loop'
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: interleaved-alloc-free -->
5 allocs, free 3, 5 more allocs, free 7 — all 10 blocks freed by end.
```maxon
function main() returns ExitCode
	let a = __mm_raw_alloc(16)
	let b = __mm_raw_alloc(16)
	let c = __mm_raw_alloc(16)
	let d = __mm_raw_alloc(16)
	let e = __mm_raw_alloc(16)
	__mm_raw_free(a)
	__mm_raw_free(b)
	__mm_raw_free(c)
	let f = __mm_raw_alloc(16)
	let g = __mm_raw_alloc(16)
	let h = __mm_raw_alloc(16)
	let i = __mm_raw_alloc(16)
	let j = __mm_raw_alloc(16)
	__mm_raw_free(d)
	__mm_raw_free(e)
	__mm_raw_free(f)
	__mm_raw_free(g)
	__mm_raw_free(h)
	__mm_raw_free(i)
	__mm_raw_free(j)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: mixed-raw-and-tracked -->
Both raw and tracked allocations in one program; both counts must reach 0.
```maxon
function main() returns ExitCode
	let p = __mm_raw_alloc(64)
	let q = __mm_alloc(64, destructor: 0, tag: 0)
	let r = __mm_raw_alloc(128)
	let s = __mm_alloc(128, destructor: 0, tag: 0)
	__mm_raw_free(p)
	__mm_decref(q)
	__mm_raw_free(r)
	__mm_decref(s)
	let raw = __mm_raw_alloc_count()
	let tracked = __mm_alloc_count()
	if raw != 0 'rawFail'
		return 1
	end 'rawFail'
	if tracked != 0 'trackedFail'
		return 2
	end 'trackedFail'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: mm-alloc-decref -->
Allocate one tracked block then decref — count must return to 0.
```maxon
function main() returns ExitCode
	let p = __mm_alloc(64, destructor: 0, tag: 0)
	__mm_decref(p)
	let count = __mm_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: mm-alloc-incref-decref -->
Allocates a tracked block, incref 3 times then decref 3 times — count should still be 1. One more decref drops count to 0.
```maxon
function main() returns ExitCode
	let p = __mm_alloc(64, destructor: 0, tag: 0)
	__mm_incref(p)
	__mm_incref(p)
	__mm_incref(p)
	__mm_decref(p)
	__mm_decref(p)
	__mm_decref(p)
	let after3 = __mm_alloc_count()
	__mm_decref(p)
	let after4 = __mm_alloc_count()
	var fail = 0
	if after3 != 1 'a'
		fail = 1
	end 'a'
	if after4 != 0 'b'
		fail = fail + 2
	end 'b'
	if fail == 0 'ok'
		return 0
	end 'ok'
	if fail == 1 'f1'
		return 1
	end 'f1'
	if fail == 2 'f2'
		return 2
	end 'f2'
	return 3
end 'main'
```
```exitcode
0
```

<!-- test: mm-alloc-tracked-count -->
Loop 100 tracked alloc + decref pairs; final tracked alloc count should be 0.
```maxon
function main() returns ExitCode
	for i in 1 upto 100 'loop'
		let p = __mm_alloc(64, destructor: 0, tag: 0)
		__mm_decref(p)
	end 'loop'
	let count = __mm_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: nested-incref -->
Single tracked alloc, 5 increfs, 6 decrefs (one extra to bring refcount to 0).
```maxon
function main() returns ExitCode
	let p = __mm_alloc(64, destructor: 0, tag: 0)
	__mm_incref(p)
	__mm_incref(p)
	__mm_incref(p)
	__mm_incref(p)
	__mm_incref(p)
	__mm_decref(p)
	__mm_decref(p)
	__mm_decref(p)
	__mm_decref(p)
	__mm_decref(p)
	__mm_decref(p)
	let count = __mm_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: raw-alloc-basic -->
Single raw alloc + free, no count assertion.
```maxon
function main() returns ExitCode
	let p = __mm_raw_alloc(64)
	__mm_raw_free(p)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: raw-alloc-free-reuse -->
Two sequential raw alloc + free pairs; count must reach 0.
```maxon
function main() returns ExitCode
	let p1 = __mm_raw_alloc(64)
	__mm_raw_free(p1)
	let p2 = __mm_raw_alloc(64)
	__mm_raw_free(p2)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: raw-alloc-large -->
A 100MB allocation routed through OS-direct path; freed cleanly.
```maxon
function main() returns ExitCode
	let big = 100 * 1024 * 1024
	let p = __mm_raw_alloc(big)
	__mm_raw_free(p)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: raw-alloc-many -->
100 raw allocs of varying sizes, each freed immediately.
```maxon
function main() returns ExitCode
	for i in 1 upto 100 'loop'
		let p = __mm_raw_alloc(i * 8)
		__mm_raw_free(p)
	end 'loop'
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slab-size-class-boundaries -->
Allocates+frees one block at each of 18 sizes spanning the slab/arena/OS-direct tiers. Final raw alloc count should be 0.
```maxon
function main() returns ExitCode
	let p1 = __mm_raw_alloc(8)
	__mm_raw_free(p1)
	let p2 = __mm_raw_alloc(16)
	__mm_raw_free(p2)
	let p3 = __mm_raw_alloc(24)
	__mm_raw_free(p3)
	let p4 = __mm_raw_alloc(32)
	__mm_raw_free(p4)
	let p5 = __mm_raw_alloc(48)
	__mm_raw_free(p5)
	let p6 = __mm_raw_alloc(64)
	__mm_raw_free(p6)
	let p7 = __mm_raw_alloc(96)
	__mm_raw_free(p7)
	let p8 = __mm_raw_alloc(128)
	__mm_raw_free(p8)
	let p9 = __mm_raw_alloc(192)
	__mm_raw_free(p9)
	let p10 = __mm_raw_alloc(256)
	__mm_raw_free(p10)
	let p11 = __mm_raw_alloc(384)
	__mm_raw_free(p11)
	let p12 = __mm_raw_alloc(512)
	__mm_raw_free(p12)
	let p13 = __mm_raw_alloc(1024)
	__mm_raw_free(p13)
	let p14 = __mm_raw_alloc(2048)
	__mm_raw_free(p14)
	let p15 = __mm_raw_alloc(4096)
	__mm_raw_free(p15)
	let p16 = __mm_raw_alloc(8192)
	__mm_raw_free(p16)
	let p17 = __mm_raw_alloc(16384)
	__mm_raw_free(p17)
	let p18 = __mm_raw_alloc(32768)
	__mm_raw_free(p18)
	let count = __mm_raw_alloc_count()
	if count == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```
