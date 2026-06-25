# Small-array inline storage: one allocation instead of two

## Context

Every `Array<T>` (and `String`, which wraps `Array with Byte`) is one field: `managed as __ManagedMemory`. Creating even a 1-element byte array costs **two heap allocations**:

1. The `__ManagedMemory` record itself — `__mm_alloc(48)`.
2. A separate element buffer — `__managed_mem_alloc_buffer`.

Each allocation also carries a 32-byte MM header and is rounded up to a slab **size class** (`__slab_class_size`, [stdlib/Internals.maxon:267](stdlib/Internals.maxon#L267): 8/16/24/32/48/64/96/128/…). So a 3-byte array today burns two slab slots (e.g. class-96 for the record + class-48 for the buffer ≈ 144 bytes) and forces a pointer chase (`load buffer@0`, then load the element) on every access.

**Goal:** for small arrays, hold the element storage *inline*, right after the record fields, in the **same** allocation — halving the allocation count, removing the pointer chase's second cache line, and (crucially) reclaiming the size-class slack as free spare capacity.

This is a **self-hosted-only** change (the C# bootstrap is being deprecated, so it is left untouched). It was confirmed feasible by reading the allocation, destructor, COW, and grow paths. No existing inline/SBO precedent exists; this is new.

## Design decisions (confirmed with user)

- **Inline marker:** one new `parent_ptr` sentinel `MM_PARENT_INLINE = -3`. NOT a capacity sentinel (capacity must stay `>= 0` so bounds checks and the COW no-copy fast path keep working) and NOT a new struct field (no layout change).
- **Spare capacity = fit to the nearest size class.** The slab rounds the allocation up to a size class anyway, so after laying down the record we back out *all remaining bytes in the rounded slot* as extra element slots and set `capacity` to that. Spare pushes after creation stay single-allocation **at zero extra cost**. This requires `__mm_alloc`/`__slab_alloc` to report the rounded user-region size (see step 1).
- **Element scope:** include **managed-element** arrays (`Array<String>`, `Array<Box>`, …), not just primitives. The destructor's element-walk must run for INLINE records while skipping the buffer free.
- **Threshold:** inline only when the element bytes needed fit within **64 bytes** of inline budget (`MM_INLINE_CAP_BYTES = 64`).
- **First grow detaches to external.** Once an inline array outgrows its rounded inline capacity, `cow_check` allocates a normal external buffer, copies the live bytes out, flips `parent` to `ROOT`, and the record becomes an ordinary two-allocation array. `mmRealloc` is never called on inline storage.

## The single-allocation layout

```
__mm_alloc(MANAGED_MEM_RECORD_SIZE + inlineBytes) returns one slab slot, rounded up to a size class:

  [32-byte MM header][ record fields (48 bytes) ][ inline element bytes … ]
                      ^ record ptr (user_ptr)     ^ buffer@0 = record + MANAGED_MEM_RECORD_SIZE
```

- `buffer@0   = record + MANAGED_MEM_RECORD_SIZE` (points *into* the same allocation)
- `length@8   = 0`
- `capacity@16 = (roundedUserRegion - MANAGED_MEM_RECORD_SIZE) / elemSize` — **all** the slack, not just `count`
- `element_size@24 = elemSize`
- `parent@32  = MM_PARENT_INLINE (-3)`
- `element_destroy@40 = &__mm_decref` for managed elements, else 0

`get`/`set`/`swap`/`remove`/`append`/`slice`/`byte_at`/`set_byte` all just `load buffer@0` and operate — they are already agnostic to where the buffer lives, so they need **no change**. Only allocation, teardown, and the realloc/COW paths care.

## Implementation — self-hosted only (`stdlib/Internals.maxon`)

**1. Expose the rounded slot size.** `__slab_alloc`/`__mm_alloc` already round to a class but discard the rounded size. Add a helper that, given a requested byte size, returns the class size that will back it (mirror the scan in [__slab_alloc:921-946](stdlib/Internals.maxon#L921) using [__slab_class_size:267](stdlib/Internals.maxon#L267)). `create_managed` uses it to compute spare inline capacity. Add constants near [line 1129](stdlib/Internals.maxon#L1129):
```
let MM_PARENT_INLINE = -3
let MM_INLINE_CAP_BYTES = 64
```

**2. `__managed_mem_create_managed`** ([~1940](stdlib/Internals.maxon#L1940)) — add an inline branch *before* the existing two-alloc body:
- Eligible when `count > 0` and `count * elemSize <= MM_INLINE_CAP_BYTES` (bit-packed `elemSize==0` uses `(count+7)>>3`; can include or defer — low risk, sizing-only).
- Compute `wantBytes = MANAGED_MEM_RECORD_SIZE + count*elemSize`; `slotUser = roundedUserRegion(wantBytes)`; `inlineBytes = slotUser - MANAGED_MEM_RECORD_SIZE`; `cap = inlineBytes / elemSize`.
- `rec = __mm_alloc(MANAGED_MEM_RECORD_SIZE + inlineBytes, dtor)`; `__mm_incref(rec)` (alloc-at-0 contract, same as today).
- Zero-fill the inline region `[rec+MANAGED_MEM_RECORD_SIZE, rec+MANAGED_MEM_RECORD_SIZE+inlineBytes)` (the sparse-slot decref-fault guard, exactly as [__managed_mem_alloc_buffer:1156-1168](stdlib/Internals.maxon#L1156) does).
- Store fields per the layout above (`buffer = rec + MANAGED_MEM_RECORD_SIZE`, `parent = MM_PARENT_INLINE`, `capacity = cap`, `element_destroy = elementDestroy`).
- Else fall through to the existing exact two-alloc path unchanged.

**3. `__destruct___ManagedMemory`** ([~1184](stdlib/Internals.maxon#L1184)) — restructure the ROOT branch so INLINE shares the element-walk but skips the buffer free:
```
if parent == MM_PARENT_ROOT or parent == MM_PARENT_INLINE 'ownedWalk'
    __managed_mem_walk_elements(self)        // inert when element_destroy == 0
    if parent == MM_PARENT_ROOT 'extBuf'
        let buffer = load(self)
        if buffer != 0 'hasBuf'
            __mm_decref(buffer)              // ONLY external buffers
        end 'hasBuf'
    end 'extBuf'
    return 0
end 'ownedWalk'
```
The inline bytes are reclaimed when `__mm_free` frees the record's own slot — no separate decref.

**4. `__managed_mem_cow_check`** ([~1590](stdlib/Internals.maxon#L1590)) — the detach path. Change the no-copy fast-path gate from `capacity >= 0` to `capacity >= 0 and parent != MM_PARENT_INLINE`. An INLINE record then falls into the existing copy body, which already: allocates an external `__managed_mem_alloc_buffer(byteLen)`, memcpys the live bytes out of the (inline) old buffer, stores the new buffer, sets `capacity = length`, flips `parent` to `MM_PARENT_ROOT`, and decrefs a real slice parent. Add `parent != MM_PARENT_INLINE` to the "was a real slice parent" guard at [line 1618](stdlib/Internals.maxon#L1618) so the `-3` marker is never treated as a heap pointer to decref. After detach the record is a normal external ROOT array, so `grow`'s subsequent `mmRealloc` ([1637](stdlib/Internals.maxon#L1637)) is valid — and `mmRealloc` is never reached with an inline pointer because `grow` calls `cow_check` first ([1629](stdlib/Internals.maxon#L1629)).

Everything else (`set`/`swap`/`raw_set`/`set_byte`/`remove`/`append`/`to_cstring`) already routes mutation-that-needs-a-private-buffer through `cow_check`, so the detach is inherited for free. `slice` reads the source `buffer@0` (a valid pointer into the inline region) and builds its own external record — no change.

The self-hosted `LowerMaxonToStd.maxon` needs no change: its `create`/`grow`/`slice` ops dispatch to the stdlib helpers above, and its rdata/const-array builders build non-inline records — just keep them away from `parent = -3`.

**The C# bootstrap is intentionally left untouched** — it is being deprecated, so this is a self-hosted-only change. (Note the C# `__ManagedMemory` is a different shape anyway: 40 bytes, header-less raw buffer, capacity-keyed teardown — not worth porting.)

## Edge cases (all handled)

- **Zero-length** (`count == 0`): not eligible; stays the existing `buffer = 0`, `parent = ROOT`, `capacity = 0` path; destructor's `buffer == 0` guard already skips the decref.
- **Bit-packed bool** (`element_size == 0`): inline byte size = `(count+7)>>3`; access path via `buffer@0` unchanged. Include or defer (sizing-only).
- **Slices of an inline source:** `__managed_mem_slice` reads `srcBuf = load(self)` (a valid pointer into the inline region) and allocates its own external record+buffer. Safe, no change.
- **`toCString` / +1 terminator** ([~2001](stdlib/Internals.maxon#L2001)): the terminator write only happens after a `grow` to `length+1`, which detaches inline → external first, so it never writes past the inline region in place.
- **Managed-element inline teardown:** covered by the restructured destructor (walk runs for INLINE).

## Verification

1. Build the self-hosted compiler: `mcp__maxon-dev__build` with `target: "selfhosted"` (this rebuilds the changed stdlib). The change is stdlib-only, so the C# bootstrap that compiles the stdlib is unaffected — but a from-source stdlib rebuild is needed (`maxon clean` then build if the cache is stale).
2. Full self-hosted spec suite on both targets: `mcp__maxon-dev__run_spec_test` with `compiler: "selfhosted"`, then again with `target: "wasm32-wasi"` on the self-hosted runner.
3. **Leak/UAF focus (exit code 101):** run the array/string/managed-element suites under scrutiny — array-literal-of-Box, managed-element arrays, `String.replace`/slice, append-managed-elements, `toCString` round-trip. Use `mcp__maxon-dev__spec_test_outcome` with a `filter` for per-test detail and `mcp__maxon-dev__mm_trace_analyze` on managed-array tests.
4. **Spot-check the single allocation:** a tiny program that creates a small `Array with Byte` and pushes a few elements should detach only after exceeding the rounded inline capacity. Verify via `--mm-trace` alloc-count (one alloc, not two) using `mcp__maxon-dev__run_program` / the trace analyzer. Any `--mm-trace` spec asserting alloc/free *pairing* must have its expected alloc **count** updated (two→one) for small arrays.
5. Run the `code-review` skill on the diff before committing (per-spec quality gate).

## Files to modify

Just one file:

- [stdlib/Internals.maxon](stdlib/Internals.maxon) — new size-class-rounding helper; `MM_PARENT_INLINE`/`MM_INLINE_CAP_BYTES` consts; `__managed_mem_create_managed`; `__destruct___ManagedMemory`; `__managed_mem_cow_check`.

## Deferred (later phases)

- Apply inline to `__managed_mem_slice` results and `from_cstring` (more single-alloc wins).
- Tune `MM_INLINE_CAP_BYTES` against real workloads.
