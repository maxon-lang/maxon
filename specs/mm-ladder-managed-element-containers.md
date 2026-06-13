---
feature: mm-ladder-managed-element-containers
status: selfhosted
keywords: [refcount, ownership, leak, memory, destructor, container, set, map, element]
category: memory
---
# Memory-Management Ladder — Managed-Element Containers

## Documentation

A focused continuation of [the container MM ladder](mm-ladder-containers.md), isolating the ownership of
MANAGED ELEMENTS inside a hash container (`Set`/`Map`) — the path where an element borrowed from an input
collection is moved into the container's bucket storage, which the container must then own independently of
the source. Split out of `mm-ladder-containers.md` because that file's per-fragment mm-trace + RequiredIR
regeneration had grown heavy enough to approach the spec runner's per-worker timeout; keeping this behavior
in its own (whitelisted) spec keeps both files fast.

**Oracles** (same as the core ladder): exit code, leak gate (`mmAllocCount` delta → exit 101),
`<!-- MmTrace -->` + a `stderr` block (the exact alloc/incref/decref/free tree — the strongest oracle; the
mm-trace runtime PANICS on a `(null)` / garbage tag, so a use-after-free that reads a freed header fails
loudly), and `RequiredIR:x64-windows` pinned via `--update-required`. ALWAYS hand-review a regenerated trace
before locking — the leak gate cannot see a UAF that reads freed-not-yet-reused memory; the mm-trace can.

Run via `maxon-selfhosted.exe spec-test --filter=mm-ladder-managed-element-containers`.

## Tests

<!-- test: set-from-managed-elements -->
<!-- MmTrace -->
A `Set` (or `Map`) constructed from a literal array of MANAGED elements (`Set from [Box.create(7),
Box.create(9)]`) must take its OWN reference to each element it stores. `Set.init` iterates the input array
(`for elem in arr`) — each `elem` is a BORROW of the array's element — and `insert`s it into the Set's
bucket storage. The Set keeps those elements past the input array's lifetime, so each inserted element must
be INCREF'd as it lands in the Set's storage (the container takes a ref); otherwise the input array's
teardown frees the elements out from under the Set, and the Set's own destructor walk then double-frees
dangling slots (a UAF the mm-trace catches via the garbage-tag panic, even though a non-trace `count()`-only
run may read freed-not-yet-reused memory and appear to pass). Here both `Box`es survive the input array's
drop, are reachable through the Set, and are freed EXACTLY ONCE when the Set drops at scope exit. Proves the
borrowed-element-into-container path increfs (the type-param element retain), distinct from the array-LITERAL
path where the elements' +1 transfers straight into the owning array.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias HashValue = int(0 to u32.max)

type Box implements Hashable, Equatable
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'

	export function hash() returns HashValue
		return value mod 100
	end 'hash'

	export function equals(other Box) returns bool
		return value == other.value
	end 'equals'
end 'Box'

typealias BoxSet = Set with Box

function main() returns ExitCode
	let s = BoxSet from [Box.create(7), Box.create(9)]
	return s.count()
end 'main'
```
```exitcode
2
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_incref Box #1 rc=1 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_alloc Box #2 size=8 [Box.create]
mm_incref Box #2 rc=1 [Box.create]
mm_move Box #2 -> return [Box.create]
mm_alloc __ManagedMemory #3 size=48 [main]
mm_incref __ManagedMemory #3 rc=1 [main]
mm_alloc <raw> #4 size=16 [main]
mm_incref <raw> #4 rc=1 [main]
mm_move Box #1 -> container
mm_move Box #2 -> container
mm_move __ManagedMemory #3 -> container
mm_alloc Array #5 size=8
mm_incref Array #5 rc=1
mm_alloc __ManagedMemory #6 size=48
mm_incref __ManagedMemory #6 rc=1
mm_decref __ManagedMemory #6 rc=0
  mm_free __ManagedMemory #6
mm_move Array #5 -> return
mm_alloc Array #7 size=8
mm_incref Array #7 rc=1
mm_alloc __ManagedMemory #8 size=48
mm_incref __ManagedMemory #8 rc=1
mm_alloc <raw> #9 size=128
mm_incref <raw> #9 rc=1
mm_alloc Array #10 size=8
mm_incref Array #10 rc=1
mm_alloc __ManagedMemory #11 size=48
mm_incref __ManagedMemory #11 rc=1
mm_alloc <raw> #12 size=128
mm_incref <raw> #12 rc=1
mm_alloc Set #13 size=32
mm_incref Set #13 rc=1
mm_alloc <raw> #14 size=40
mm_incref <raw> #14 rc=1
mm_incref __ManagedMemory #3 rc=2
mm_alloc ArrayIterator #15 size=8
mm_incref ArrayIterator #15 rc=1
mm_move ArrayIterator #15 -> return
mm_move ArrayIterator #15 -> return
mm_incref Box #1 rc=2
mm_incref Box #2 rc=2 [Box.hash]
mm_decref ArrayIterator #15 rc=0 [Box.hash]
  mm_decref <raw> #14 rc=0 [Box.hash]
    mm_decref __ManagedMemory #3 rc=1 [Box.hash]
    mm_free <raw> #14
  mm_free ArrayIterator #15
mm_move Set #13 -> return [Box.hash]
mm_decref Array #5 rc=0 [Box.hash]
  mm_decref __ManagedMemory #3 rc=0 [Box.hash]
    mm_decref Box #1 rc=1 [Box.hash]
    mm_decref Box #2 rc=1 [Box.hash]
    mm_decref <raw> #4 rc=0 [Box.hash]
      mm_free <raw> #4
    mm_free __ManagedMemory #3
  mm_free Array #5
mm_decref Set #13 rc=0 [Box.hash]
  mm_decref Array #7 rc=0 [Box.hash]
    mm_decref __ManagedMemory #8 rc=0 [Box.hash]
      mm_decref Box #1 rc=0 [Box.hash]
        mm_free Box #1
      mm_decref Box #2 rc=0 [Box.hash]
        mm_free Box #2
      mm_decref <raw> #9 rc=0 [Box.hash]
        mm_free <raw> #9
      mm_free __ManagedMemory #8
    mm_free Array #7
  mm_decref Array #10 rc=0 [Box.hash]
    mm_decref __ManagedMemory #11 rc=0 [Box.hash]
      mm_decref <raw> #12 rc=0 [Box.hash]
        mm_free <raw> #12
      mm_free __ManagedMemory #11
    mm_free Array #10
  mm_free Set #13
```

<!-- test: set-from-character-literals-membership -->
<!-- MmTrace -->
A `Set with Character` built from CHARACTER LITERALS (`Set from ['\t', '\n', ' ']`) stores each char element
into its bucket storage, then a later membership read (`contains('\n')`) probes it. Unlike the `Set with Box`
case above (managed POINTER elements), each char literal lowers to an inline value-wrapper: a small buffer
plus a 40-byte `__ManagedMemory`-shaped record plus an 8-byte `Character` envelope. The Set's element-walk
destructor treats each stored `Character` as a managed pointer and `__mm_decref`s it at teardown, cascading
into the inner `__ManagedMemory`.

Two fixes make this correct (both PRE-EXISTING gaps surfaced by this test, distinct from the borrowed-element
retain that fixed the managed-pointer case above):

1. CHAR-LITERAL ELEMENT REPRESENTATION — the records must be refcount-TRACKED to match the element-walk's
   `__mm_decref`. `lowerCharLiteral` / `materializeCharacterFromByte` now alloc the inner `__ManagedMemory`
   via `mrt_alloc_with_dtor(&__destruct___ManagedMemory)` (rc=1) and the outer `Character` via
   `stdlib.__mm_alloc(&__destruct_Character)` (rc=0, classified `freshRc0`) — mirroring `lowerStringConst`.
   The bare `mrt_alloc` (destructor=0) records were untracked, so the element-walk's decref read a garbage
   refcount header.

2. LAYOUT-DESCRIPTOR CACHE RESTORE — `__layout_Array_Character` / `__layout_Set_Character` are baked into the
   stdlib cache (because `Array<Character>`/`Set<Character>` are instantiated inside the stdlib). On a user
   build, `BuildLayoutDescriptors`'s cache-restore leg re-added the descriptor bytes but FORGOT to re-push the
   copy/destroy thunk `funcAbs64` relocs + witness-conditional roots (per-pipeline-run state the cache does
   not carry — exactly as the WITNESS cache-restore path already does). So the descriptor's +40 destroy-func
   slot stayed 0 and the destroy thunk was never linked → an element-drop through the cached descriptor called
   `[layout+40] == 0` → `rip=0`. `int`/`Box` element sets were unaffected (their descriptors are not cached,
   so they hit the fresh-emit path). `registerLayoutThunkRelocsAndRoots` now runs on BOTH legs.

Here `'\n'` is found, `'h'` is not, exit 42, and every element record is freed EXACTLY ONCE at the Set's drop.
```maxon
typealias CharSet = Set with Character

function main() returns ExitCode
	let cs = CharSet from ['\t', '\n', '\r', ' ', '\x0B']
	if cs.contains('\n') 'nlIn'
		if not cs.contains('h') 'hOut'
			return 42
		end 'hOut'
	end 'nlIn'
	return 0
end 'main'
```
```exitcode
42
```
```stderr
mm_alloc __ManagedMemory #1 size=48 [main]
mm_incref __ManagedMemory #1 rc=1 [main]
mm_alloc <raw> #2 size=40 [main]
mm_incref <raw> #2 rc=1 [main]
mm_alloc <raw> #3 size=1
mm_incref <raw> #3 rc=1
mm_alloc <raw> #4 size=48
mm_incref <raw> #4 rc=1
mm_alloc Character #5 size=8
mm_incref Character #5 rc=1
mm_move Character #5 -> container
mm_alloc <raw> #6 size=1
mm_incref <raw> #6 rc=1
mm_alloc <raw> #7 size=48
mm_incref <raw> #7 rc=1
mm_alloc Character #8 size=8
mm_incref Character #8 rc=1
mm_move Character #8 -> container
mm_alloc <raw> #9 size=1
mm_incref <raw> #9 rc=1
mm_alloc <raw> #10 size=48
mm_incref <raw> #10 rc=1
mm_alloc Character #11 size=8
mm_incref Character #11 rc=1
mm_move Character #11 -> container
mm_alloc <raw> #12 size=1
mm_incref <raw> #12 rc=1
mm_alloc <raw> #13 size=48
mm_incref <raw> #13 rc=1
mm_alloc Character #14 size=8
mm_incref Character #14 rc=1
mm_move Character #14 -> container
mm_alloc <raw> #15 size=1
mm_incref <raw> #15 rc=1
mm_alloc <raw> #16 size=48
mm_incref <raw> #16 rc=1
mm_alloc Character #17 size=8
mm_incref Character #17 rc=1
mm_move Character #17 -> container
mm_move __ManagedMemory #1 -> container
mm_alloc Array #18 size=8
mm_incref Array #18 rc=1
mm_alloc __ManagedMemory #19 size=48
mm_incref __ManagedMemory #19 rc=1
mm_decref __ManagedMemory #19 rc=0
  mm_free __ManagedMemory #19
mm_move Array #18 -> return
mm_alloc Array #20 size=8
mm_incref Array #20 rc=1
mm_alloc __ManagedMemory #21 size=48
mm_incref __ManagedMemory #21 rc=1
mm_alloc <raw> #22 size=128
mm_incref <raw> #22 rc=1
mm_alloc Array #23 size=8
mm_incref Array #23 rc=1
mm_alloc __ManagedMemory #24 size=48
mm_incref __ManagedMemory #24 rc=1
mm_alloc <raw> #25 size=128
mm_incref <raw> #25 rc=1
mm_alloc Set #26 size=32
mm_incref Set #26 rc=1
mm_alloc <raw> #27 size=40
mm_incref <raw> #27 rc=1
mm_incref __ManagedMemory #1 rc=2
mm_alloc ArrayIterator #28 size=8
mm_incref ArrayIterator #28 rc=1
mm_move ArrayIterator #28 -> return
mm_move ArrayIterator #28 -> return
mm_incref Character #5 rc=2
mm_incref Character #8 rc=2
mm_incref Character #11 rc=2
mm_incref Character #14 rc=2
mm_incref Character #17 rc=2
mm_decref ArrayIterator #28 rc=0
  mm_decref <raw> #27 rc=0
    mm_decref __ManagedMemory #1 rc=1
    mm_free <raw> #27
  mm_free ArrayIterator #28
mm_move Set #26 -> return
mm_decref Array #18 rc=0
  mm_decref __ManagedMemory #1 rc=0
    mm_decref Character #5 rc=1
    mm_decref Character #8 rc=1
    mm_decref Character #11 rc=1
    mm_decref Character #14 rc=1
    mm_decref Character #17 rc=1
    mm_decref <raw> #2 rc=0
      mm_free <raw> #2
    mm_free __ManagedMemory #1
  mm_free Array #18
mm_alloc <raw> #29 size=1
mm_incref <raw> #29 rc=1
mm_alloc <raw> #30 size=48
mm_incref <raw> #30 rc=1
mm_alloc Character #31 size=8
mm_incref Character #31 rc=1
mm_move Character #31 -> container
mm_decref Character #31 rc=0
  mm_decref <raw> #30 rc=0
    mm_decref <raw> #29 rc=0
      mm_free <raw> #29
    mm_free <raw> #30
  mm_free Character #31
mm_alloc <raw> #32 size=1
mm_incref <raw> #32 rc=1
mm_alloc <raw> #33 size=48
mm_incref <raw> #33 rc=1
mm_alloc Character #34 size=8
mm_incref Character #34 rc=1
mm_move Character #34 -> container
mm_decref Character #34 rc=0
  mm_decref <raw> #33 rc=0
    mm_decref <raw> #32 rc=0
      mm_free <raw> #32
    mm_free <raw> #33
  mm_free Character #34
mm_decref Set #26 rc=0
  mm_decref Array #20 rc=0
    mm_decref __ManagedMemory #21 rc=0
      mm_decref Character #17 rc=0
        mm_decref <raw> #16 rc=0
          mm_decref <raw> #15 rc=0
            mm_free <raw> #15
          mm_free <raw> #16
        mm_free Character #17
      mm_decref Character #11 rc=0
        mm_decref <raw> #10 rc=0
          mm_decref <raw> #9 rc=0
            mm_free <raw> #9
          mm_free <raw> #10
        mm_free Character #11
      mm_decref Character #14 rc=0
        mm_decref <raw> #13 rc=0
          mm_decref <raw> #12 rc=0
            mm_free <raw> #12
          mm_free <raw> #13
        mm_free Character #14
      mm_decref Character #5 rc=0
        mm_decref <raw> #4 rc=0
          mm_decref <raw> #3 rc=0
            mm_free <raw> #3
          mm_free <raw> #4
        mm_free Character #5
      mm_decref Character #8 rc=0
        mm_decref <raw> #7 rc=0
          mm_decref <raw> #6 rc=0
            mm_free <raw> #6
          mm_free <raw> #7
        mm_free Character #8
      mm_decref <raw> #22 rc=0
        mm_free <raw> #22
      mm_free __ManagedMemory #21
    mm_free Array #20
  mm_decref Array #23 rc=0
    mm_decref __ManagedMemory #24 rc=0
      mm_decref <raw> #25 rc=0
        mm_free <raw> #25
      mm_free __ManagedMemory #24
    mm_free Array #23
  mm_free Set #26
```

<!-- test: map-integer-key-box-value -->
<!-- MmTrace -->
A `Map with (Integer, Box)` — NON-managed key, MANAGED value — exercised by the mutating API (`upsert` +
`get`). This is the per-type-param-layout hazard in its purest form: the Map's three internal arrays (`keys`
= `Array<Integer>`, `values` = `Array<Box>`, `states` = `Array<SlotState>`) all flow through the SAME single
forwarded aggregate layout inside the generic Map body, but each must stamp its backing buffer's
`element_destroy` from its OWN element type. The aggregate `__layout_Map_Integer_Box` has HAS_HEAP_REFS set
(it contains a managed `Box`); naively forwarding that to the keys array would stamp `&__mm_decref` on the
INTEGER buffer → the destructor walk would `__mm_decref` raw integers → a garbage-header crash. The fix
projects a per-type-param element layout at each field-array call site (the keys array sees HAS_HEAP_REFS=0,
the values array sees HAS_HEAP_REFS=1). The trace must show: the keys array's `__ManagedMemory` teardown
frees ONLY its raw buffer (no integer element decrefs), the values array's teardown frees BOTH `Box`es, and
the `get` result is a BORROW that takes its own +1 (so it survives the Map's drop and is freed once at its
own last use, NOT double-freed by the value-array walk). Every allocation freed exactly once; exit 7.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxMap = Map with (Integer, Box)

function main() returns ExitCode
	var m = BoxMap.create()
	m.upsert(1, value: Box.create(7))
	m.upsert(2, value: Box.create(9))
	let b = try m.get(1) otherwise return 99
	return b.value
end 'main'
```
```exitcode
7
```
```stderr
mm_alloc Map #1 size=40 [main]
mm_alloc Array #2 size=8 [main]
mm_incref Array #2 rc=1 [main]
mm_alloc __ManagedMemory #3 size=48 [main]
mm_incref __ManagedMemory #3 rc=1 [main]
mm_move Array #2 -> return [main]
mm_move Array #2 -> return [main]
mm_alloc Array #4 size=8
mm_incref Array #4 rc=1
mm_alloc __ManagedMemory #5 size=48
mm_incref __ManagedMemory #5 rc=1
mm_move Array #4 -> return
mm_move Array #4 -> return
mm_alloc Array #6 size=8
mm_incref Array #6 rc=1
mm_alloc __ManagedMemory #7 size=48
mm_incref __ManagedMemory #7 rc=1
mm_move Array #6 -> return
mm_move Array #6 -> return
mm_move Map #1 -> return
mm_alloc Box #8 size=8 [Box.create]
mm_incref Box #8 rc=1 [Box.create]
mm_move Box #8 -> return [Box.create]
mm_move Box #8 -> container
mm_alloc Array #9 size=8
mm_incref Array #9 rc=1
mm_alloc __ManagedMemory #10 size=48
mm_incref __ManagedMemory #10 rc=1
mm_decref Array #2 rc=0
  mm_decref __ManagedMemory #3 rc=0
    mm_free __ManagedMemory #3
  mm_free Array #2
mm_alloc <raw> #11 size=128
mm_incref <raw> #11 rc=1
mm_alloc Array #12 size=8
mm_incref Array #12 rc=1
mm_alloc __ManagedMemory #13 size=48
mm_incref __ManagedMemory #13 rc=1
mm_decref Array #4 rc=0
  mm_decref __ManagedMemory #5 rc=0
    mm_free __ManagedMemory #5
  mm_free Array #4
mm_alloc <raw> #14 size=128
mm_incref <raw> #14 rc=1
mm_alloc Array #15 size=8
mm_incref Array #15 rc=1
mm_alloc __ManagedMemory #16 size=48
mm_incref __ManagedMemory #16 rc=1
mm_decref Array #6 rc=0
  mm_decref __ManagedMemory #7 rc=0
    mm_free __ManagedMemory #7
  mm_free Array #6
mm_alloc <raw> #17 size=128
mm_incref <raw> #17 rc=1
mm_alloc Box #18 size=8 [Box.create]
mm_incref Box #18 rc=1 [Box.create]
mm_move Box #18 -> return [Box.create]
mm_move Box #18 -> container
mm_incref Box #8 rc=2
mm_drop Map #1
    mm_decref Array #9 rc=0
      mm_decref __ManagedMemory #10 rc=0
        mm_decref <raw> #11 rc=0
          mm_free <raw> #11
        mm_free __ManagedMemory #10
      mm_free Array #9
    mm_decref Array #12 rc=0
      mm_decref __ManagedMemory #13 rc=0
        mm_decref Box #8 rc=1
        mm_decref Box #18 rc=0
          mm_free Box #18
        mm_decref <raw> #14 rc=0
          mm_free <raw> #14
        mm_free __ManagedMemory #13
      mm_free Array #12
    mm_decref Array #15 rc=0
      mm_decref __ManagedMemory #16 rc=0
        mm_decref <raw> #17 rc=0
          mm_free <raw> #17
        mm_free __ManagedMemory #16
      mm_free Array #15
    mm_free Map #1
mm_decref Box #8 rc=0
  mm_free Box #8
```

<!-- test: map-string-key-box-value -->
<!-- MmTrace -->
A `Map with (String, Box)` — MANAGED key AND MANAGED value. The sibling of `map-integer-key-box-value`, but
now BOTH the keys array (`Array<String>`) and the values array (`Array<Box>`) hold heap-managed elements, so
the per-type-param layout projection must independently stamp `element_destroy` on BOTH backing buffers (the
"4-layer String-keyed-map" shape). At teardown the Map's destructor must walk the keys array — freeing each
`String` (and cascading into each String's own backing record + buffer) — AND walk the values array freeing
each `Box`; the single-layout bug would over- or under-free one of the two. The states array
(`Array<SlotState>`) stays non-managed (no element walk). The `get("alpha")` result is a BORROW of a `Box`
that takes its own +1, surviving the Map drop and freed once at its last use. Every String, every Box, and
all backing memory freed exactly once; exit 7.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias StrBoxMap = Map with (String, Box)

function main() returns ExitCode
	var m = StrBoxMap.create()
	m.upsert("alpha", value: Box.create(7))
	m.upsert("beta", value: Box.create(9))
	let b = try m.get("alpha") otherwise return 99
	return b.value
end 'main'
```
```exitcode
7
```
```stderr
mm_alloc Map #1 size=40 [main]
mm_alloc Array #2 size=8 [main]
mm_incref Array #2 rc=1 [main]
mm_alloc __ManagedMemory #3 size=48 [main]
mm_incref __ManagedMemory #3 rc=1 [main]
mm_move Array #2 -> return [main]
mm_move Array #2 -> return [main]
mm_alloc Array #4 size=8
mm_incref Array #4 rc=1
mm_alloc __ManagedMemory #5 size=48
mm_incref __ManagedMemory #5 rc=1
mm_move Array #4 -> return
mm_move Array #4 -> return
mm_alloc Array #6 size=8
mm_incref Array #6 rc=1
mm_alloc __ManagedMemory #7 size=48
mm_incref __ManagedMemory #7 rc=1
mm_move Array #6 -> return
mm_move Array #6 -> return
mm_move Map #1 -> return
mm_alloc <raw> #8 size=48
mm_incref <raw> #8 rc=1
mm_alloc String #9 size=16
mm_incref String #9 rc=1
mm_alloc Box #10 size=8 [Box.create]
mm_incref Box #10 rc=1 [Box.create]
mm_move Box #10 -> return [Box.create]
mm_move String #9 -> container
mm_move Box #10 -> container
mm_alloc Array #11 size=8
mm_incref Array #11 rc=1
mm_alloc __ManagedMemory #12 size=48
mm_incref __ManagedMemory #12 rc=1
mm_decref Array #2 rc=0
  mm_decref __ManagedMemory #3 rc=0
    mm_free __ManagedMemory #3
  mm_free Array #2
mm_alloc <raw> #13 size=128
mm_incref <raw> #13 rc=1
mm_alloc Array #14 size=8
mm_incref Array #14 rc=1
mm_alloc __ManagedMemory #15 size=48
mm_incref __ManagedMemory #15 rc=1
mm_decref Array #4 rc=0
  mm_decref __ManagedMemory #5 rc=0
    mm_free __ManagedMemory #5
  mm_free Array #4
mm_alloc <raw> #16 size=128
mm_incref <raw> #16 rc=1
mm_alloc Array #17 size=8
mm_incref Array #17 rc=1
mm_alloc __ManagedMemory #18 size=48
mm_incref __ManagedMemory #18 rc=1
mm_decref Array #6 rc=0
  mm_decref __ManagedMemory #7 rc=0
    mm_free __ManagedMemory #7
  mm_free Array #6
mm_alloc <raw> #19 size=128
mm_incref <raw> #19 rc=1
mm_incref String #9 rc=2
mm_decref String #9 rc=1
mm_alloc <raw> #20 size=48
mm_incref <raw> #20 rc=1
mm_alloc String #21 size=16
mm_incref String #21 rc=1
mm_alloc Box #22 size=8 [Box.create]
mm_incref Box #22 rc=1 [Box.create]
mm_move Box #22 -> return [Box.create]
mm_move Box #22 -> container
mm_move String #21 -> container
mm_incref String #21 rc=2
mm_decref String #21 rc=1
mm_alloc <raw> #23 size=48
mm_incref <raw> #23 rc=1
mm_alloc String #24 size=16
mm_incref String #24 rc=1
mm_move String #24 -> container
mm_decref String #24 rc=0
  mm_decref <raw> #23 rc=0
    mm_free <raw> #23
  mm_free String #24
mm_incref Box #10 rc=2
mm_drop Map #1
    mm_decref Array #11 rc=0
      mm_decref __ManagedMemory #12 rc=0
        mm_decref String #21 rc=0
          mm_decref <raw> #20 rc=0
            mm_free <raw> #20
          mm_free String #21
        mm_decref String #9 rc=0
          mm_decref <raw> #8 rc=0
            mm_free <raw> #8
          mm_free String #9
        mm_decref <raw> #13 rc=0
          mm_free <raw> #13
        mm_free __ManagedMemory #12
      mm_free Array #11
    mm_decref Array #14 rc=0
      mm_decref __ManagedMemory #15 rc=0
        mm_decref Box #22 rc=0
          mm_free Box #22
        mm_decref Box #10 rc=1
        mm_decref <raw> #16 rc=0
          mm_free <raw> #16
        mm_free __ManagedMemory #15
      mm_free Array #14
    mm_decref Array #17 rc=0
      mm_decref __ManagedMemory #18 rc=0
        mm_decref <raw> #19 rc=0
          mm_free <raw> #19
        mm_free __ManagedMemory #18
      mm_free Array #17
    mm_free Map #1
mm_decref Box #10 rc=0
  mm_free Box #10
```

<!-- test: set-box-mutating-api -->
<!-- MmTrace -->
A `Set with Box` driven by the MUTATING API — `insert` / `remove` / `contains` — rather than the `Set from
[…]` literal the first rung covers. Each `insert(Box.create(N))` MOVES a freshly-owned element's +1 into the
bucket storage (the OWNED-element path: no extra retain, distinct from the borrowed-from-input-array retain
the `from`-literal rung exercises). `remove(Box.create(9))` builds a transient probe `Box` (hash/equals match
the stored one) and drops the probe at the call; the matched slot is TOMBSTONED (marked `Deleted`, count
decremented) — the stored element itself stays in the bucket buffer and is freed by the Set's element-walk at
teardown (an in-bounds slot the walk still visits), not eagerly. `contains(Box.create(7))` builds another
transient probe, reads membership, and drops it. Five `Box`es total: three inserted (all three freed by the
walk at scope exit, including the tombstoned one), plus the two transient probes freed at their respective
calls. Also locks the inserter's FIELD-REASSIGN decref-old fix: `Set.insert`'s lazy-init reassigns the
`elements`/`states` fields (`elements = ElementArray{}`) over the empty arrays `Set.create()`'s `Self{}`
already allocated. The field-store decref-old used to fire only on a PROVEN overwrite (a dominating prior
field load), which the init-shaped reassignment lacks — so the old empty arrays leaked. The fix recognizes a
managed field store through a PARAM base (`self`) as an overwrite of a constructed (hence initialized) field
→ null-guarded decref-old, no leak. The stdlib is UNCHANGED — this is a self-hosted-compiler fix, the
move-model-consistent single decref-old (NOT C#'s incref-per-reference). Every allocation freed exactly once;
no leak, no double-free; exit 2.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias HashValue = int(0 to u32.max)

type Box implements Hashable, Equatable
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'

	export function hash() returns HashValue
		return value mod 100
	end 'hash'

	export function equals(other Box) returns bool
		return value == other.value
	end 'equals'
end 'Box'

typealias BoxSet = Set with Box

function main() returns ExitCode
	var s = BoxSet.create()
	s.insert(Box.create(7))
	s.insert(Box.create(9))
	s.insert(Box.create(11))
	_ = s.remove(Box.create(9))
	if s.contains(Box.create(7)) 'has7'
		return s.count()
	end 'has7'
	return 0
end 'main'
```
```exitcode
2
```
```stderr
mm_alloc Set #1 size=32 [main]
mm_alloc Array #2 size=8 [main]
mm_incref Array #2 rc=1 [main]
mm_alloc __ManagedMemory #3 size=48 [main]
mm_incref __ManagedMemory #3 rc=1 [main]
mm_move Array #2 -> return [main]
mm_move Array #2 -> return [main]
mm_alloc Array #4 size=8
mm_incref Array #4 rc=1
mm_alloc __ManagedMemory #5 size=48
mm_incref __ManagedMemory #5 rc=1
mm_move Array #4 -> return
mm_move Array #4 -> return
mm_move Set #1 -> return
mm_alloc Box #6 size=8 [Box.create]
mm_incref Box #6 rc=1 [Box.create]
mm_move Box #6 -> return [Box.create]
mm_move Box #6 -> container
mm_alloc Array #7 size=8
mm_incref Array #7 rc=1
mm_alloc __ManagedMemory #8 size=48
mm_incref __ManagedMemory #8 rc=1
mm_alloc <raw> #9 size=128
mm_incref <raw> #9 rc=1
mm_alloc Array #10 size=8
mm_incref Array #10 rc=1
mm_alloc __ManagedMemory #11 size=48
mm_incref __ManagedMemory #11 rc=1
mm_alloc <raw> #12 size=128
mm_incref <raw> #12 rc=1
mm_decref Array #2 rc=0
  mm_decref __ManagedMemory #3 rc=0
    mm_free __ManagedMemory #3
  mm_free Array #2
mm_decref Array #4 rc=0
  mm_decref __ManagedMemory #5 rc=0
    mm_free __ManagedMemory #5
  mm_free Array #4
mm_alloc Box #13 size=8 [Box.create]
mm_incref Box #13 rc=1 [Box.create]
mm_move Box #13 -> return [Box.create]
mm_move Box #13 -> container [Box.hash]
mm_alloc Box #14 size=8 [Box.create]
mm_incref Box #14 rc=1 [Box.create]
mm_move Box #14 -> return [Box.create]
mm_move Box #14 -> container [Box.hash]
mm_alloc Box #15 size=8 [Box.create]
mm_incref Box #15 rc=1 [Box.create]
mm_move Box #15 -> return [Box.create]
mm_move Box #15 -> container [Box.hash]
mm_decref Box #15 rc=0 [Box.hash]
  mm_free Box #15
mm_alloc Box #16 size=8 [Box.create]
mm_incref Box #16 rc=1 [Box.create]
mm_move Box #16 -> return [Box.create]
mm_move Box #16 -> container [Box.hash]
mm_decref Box #16 rc=0 [Box.hash]
  mm_free Box #16
mm_drop Set #1 [Box.hash]
    mm_decref Array #7 rc=0 [Box.hash]
      mm_decref __ManagedMemory #8 rc=0 [Box.hash]
        mm_decref Box #6 rc=0 [Box.hash]
          mm_free Box #6
        mm_decref Box #13 rc=0 [Box.hash]
          mm_free Box #13
        mm_decref Box #14 rc=0 [Box.hash]
          mm_free Box #14
        mm_decref <raw> #9 rc=0 [Box.hash]
          mm_free <raw> #9
        mm_free __ManagedMemory #8
      mm_free Array #7
    mm_decref Array #10 rc=0 [Box.hash]
      mm_decref __ManagedMemory #11 rc=0 [Box.hash]
        mm_decref <raw> #12 rc=0 [Box.hash]
          mm_free <raw> #12
        mm_free __ManagedMemory #11
      mm_free Array #10
    mm_free Set #1
```

<!-- test: vector-from-managed-elements -->
<!-- MmTrace -->
A `Vector with Box` built from an array literal of MANAGED elements (`Vector from [Box.create(7),
Box.create(9)]`) must walk and free its stored elements at teardown, exactly as `Array with Box` does
(rung-10). Unlike `Array`, a `Vector` is a FIXED-size collection whose only field is its backing
`__ManagedMemory with Element` — there is no separate `length`/`capacity` growth machinery and no `push`.
The `BuiltinArrayLiteral` `from`-path lowers each element's `+1` straight INTO the vector's backing buffer
(the OWNED-element transfer — `mm_move … -> container`, no extra retain, distinct from the borrowed-element
retain the `Set from […]` rung exercises). At scope exit the `Vector` envelope drops, which decrefs its
backing `__ManagedMemory`, whose `element_destroy`-stamped walk then decrefs each stored `Box` to rc=0 and
frees it. This proves the array element-walk model carries onto the Vector lowering (same backing
`__ManagedMemory`, same `__layout_*` element-destroy stamping) with no Vector-specific gap. Both `Box`es are
freed EXACTLY ONCE at the vector's drop; exit 2 (the fixed element count).
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	let xs = Vector from [Box.create(7), Box.create(9)]
	return xs.count()
end 'main'
```
```exitcode
2
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_incref Box #1 rc=1 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_alloc Box #2 size=8 [Box.create]
mm_incref Box #2 rc=1 [Box.create]
mm_move Box #2 -> return [Box.create]
mm_alloc __ManagedMemory #3 size=48 [main]
mm_incref __ManagedMemory #3 rc=1 [main]
mm_alloc <raw> #4 size=16 [main]
mm_incref <raw> #4 rc=1 [main]
mm_move Box #1 -> container
mm_move Box #2 -> container
mm_move __ManagedMemory #3 -> container
mm_alloc Vector #5 size=8
mm_incref Vector #5 rc=1
mm_alloc __ManagedMemory #6 size=48
mm_incref __ManagedMemory #6 rc=1
mm_decref __ManagedMemory #6 rc=0
  mm_free __ManagedMemory #6
mm_move Vector #5 -> return
mm_decref Vector #5 rc=0
  mm_decref __ManagedMemory #3 rc=0
    mm_decref Box #1 rc=0
      mm_free Box #1
    mm_decref Box #2 rc=0
      mm_free Box #2
    mm_decref <raw> #4 rc=0
      mm_free <raw> #4
    mm_free __ManagedMemory #3
  mm_free Vector #5
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__mtstr_scope_Box_create]
    x64.mov r14d, 60
    x64.xor r15d, r15d
    x64.mov r8d, 8
    x64.mov r8d, 7
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rdx, r15
    x64.mov rax, r14
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.mov r8, 7
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.lea r13, [rip+__mtstr_scope_Box_create]
    x64.mov r14d, 60
    x64.xor r15d, r15d
    x64.mov r8d, 8
    x64.mov r8d, 9
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rdx, r15
    x64.mov rax, r14
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 9
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.xor eax, eax
    x64.mov edx, 8
    x64.mov ecx, 2
    x64.call stdlib.__managed_mem_create_managed
    x64.mov r14, r8
    x64.call mm_scope_pop
    x64.mov rcx, r12
    x64.call mm_move_container
    x64.xor edx, edx
    x64.mov rcx, r14
    x64.mov rax, r12
    x64.call stdlib.__managed_mem_set
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.mov edx, 1
    x64.mov rcx, r14
    x64.mov rax, r13
    x64.call stdlib.__managed_mem_set
    x64.mov edx, 2
    x64.mov rcx, r14
    x64.call stdlib.__managed_mem_set_length
    x64.lea r8, [rip+stdlib.__mm_decref]
    x64.mov [r14+40], r8 (8b)
    x64.mov rcx, r14
    x64.call mm_move_container
    x64.lea rdx, [rip+__layout_Vector_Box]
    x64.mov rcx, r14
    x64.call Vector.init
    x64.mov r12, r8
    x64.lea rdx, [rip+__layout_Vector_Box]
    x64.mov rcx, r12
    x64.call Vector.count
    x64.mov r13, r8
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_15c8287a7b7eb9de]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```
