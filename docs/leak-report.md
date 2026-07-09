# `--leak-report`: per-destructor leak census

`maxon build --leak-report <program>` wires a **per-destructor leak census** into
the emitted binary. At process exit — in addition to the `--leak-check` exit-101
gate, which `--leak-report` implies — the runtime dumps a table to **stderr**
tallying, for every allocation still live at exit, the destructor it was allocated
with. This turns the single "N allocations leaked" number that `--leak-check`
reports into a per-type breakdown, which is what makes a leak actionable: you can
see *which* destructor's objects are leaking and how many.

It is a diagnostic tool for the compiler's own memory management. A correct build
of a correct program leaks nothing, so a non-empty table means a compiler bug
(the same premise as `--leak-check`).

## Usage

```
maxon build --leak-report <program-dir-or-file>
<program>            # the table prints to stderr at exit; exit code 101 if leaks
```

`--leak-report` implies `--leak-check`, so the process still exits `101` when any
allocation is live at exit and `0` otherwise — the census table is purely
additional stderr output and never changes the exit code on its own.

**Self-hosted only.** The census keys on native code addresses, so it is a no-op
on `wasm32-wasi` (a warning is printed; the implied leak gate still runs). The C#
bootstrap is garbage-collected and never emits the census; its
`leakReport(...)` conditional-compilation predicate is constant-false.

## Output format

```
LEAK-REPORT-BEGIN total=<N> distinct=<D> raw=<R>
LEAK-DTOR count=<c> off=0x<offset> alloc=0x<alloc-offset> alloc2=0x<alloc2-offset>
LEAK-DTOR count=<c> off=0x<offset> alloc=0x<alloc-offset> alloc2=0x<alloc2-offset>
...
LEAK-RAW count=<R>
LEAK-REPORT-END
```

- `total` — `__mm_alloc_count` at exit (the true live-allocation count; the
  `--leak-check` gate uses the same value).
- `distinct` — number of distinct destructors with a non-zero live count.
- `raw` — live allocations with **no** destructor (`destructor == 0`: raw element
  buffers, string byte buffers, etc.). Also echoed as a `LEAK-RAW` line when non-zero.
- Each `LEAK-DTOR` line is one distinct destructor, **sorted by count descending**.
  `off` is the destructor's address **minus `mrt_start`'s address**, so it is
  ASLR-invariant and stable across runs of the same binary.
  - `alloc` is the **one-deep** allocation-site sample: the return address (again as
    an `mrt_start`-relative offset) of the code that called `__mm_alloc` for one
    leaking object of this type — typically the per-type constructor (e.g.
    `Array.init` for the Array family).
  - `alloc2` is the **two-deep** sample: that constructor's *own* caller, walked one
    validated frame further — for an Array literal, the function that wrote `[...]`.
    It is `0x0` when the frame link was unwalkable (e.g. `wasm32-wasi`, which has no
    frame-pointer chain, or a corrupt/absent saved-rbp link). The `alloc`/`alloc2`
    pair is a coherent snapshot of **one** concrete allocation. It is **not** the
    first allocation: the sample is resampled on power-of-two count milestones
    (1, 2, 4, …), so the retained pair is drawn from deep in the bulk and names the
    **dominant** allocation site for that destructor — a single, lazily-initialized
    process-lifetime global that merely happens to allocate first will not mask the
    systematic pipeline leaker that shares its destructor bucket.

Invariant: `total == raw + Σ(LEAK-DTOR count)`. A clean program prints
`total=0 distinct=0 raw=0` and just the `BEGIN`/`END` frame.

## Symbolizing offline

The `off=` values are offsets from `mrt_start`, not symbol names (resolving names
in-process would pull the whole symbol table into every census build). Resolve
them offline with `llvm-nm`:

1. Dump the binary's symbols, numerically sorted:
   ```
   llvm-nm --numeric-sort <program>.exe > syms.txt
   ```
2. Find `mrt_start`'s virtual address `V0` in that list.
3. For each `LEAK-DTOR ... off=0xOFF`, the destructor's VA is `V0 + OFF`. Look that
   VA up in `syms.txt` (exact match, or the nearest preceding symbol + offset).
   Destructor symbols are named `__destruct_<Type>` / `__layout_destroy_<Type_Args>`.
   The same `V0 + OFF` lookup symbolizes the `alloc=` (one-deep constructor) and
   `alloc2=` (two-deep caller) offsets — those resolve to ordinary function symbols
   (the *nearest preceding* symbol + byte offset names the function containing the
   call).

`scratchpad/census_lr.py` automates this: it runs `llvm-nm`, computes each offset
from `mrt_start`, and aggregates the `LEAK-DTOR` lines into a symbolized table,
resolving the destructor plus both `alloc`/`alloc2` samples per line. Run the
instrumented compiler with stderr captured, then:

```
<instrumented-compiler> build <program> 2> census.err
python3 scratchpad/census_lr.py census.err <instrumented-compiler>.exe
```

## How it works (and why it costs nothing when off)

All census code lives inside `#if leakReport(true)` regions of
`stdlib/Internals.maxon`, gated by the parser's `leakReport(...)` predicate
(mirroring `--rc-sanitize`). A default build never parses those regions, so the
alloc/free hot path — and the entire emitted binary — is byte-identical to a
build where the census does not exist. The stdlib cache filename carries a
`-lkrpt` token so a census stdlib and a normal one coexist on disk.

The census itself is a tiny open-addressed hash table keyed by destructor address
(`__mm_leak_ctl` anchors it in `.data`; the bucket array is a single runtime-slab
allocation, never counted against itself):

- `__mm_alloc` increments the destructor's bucket (or the ctl's `raw_live` word for
  `destructor == 0`), adjacent to the `__mm_alloc_count` bump so the two stay in
  lockstep. We record the destructor at **alloc** time, when the object is
  unquestionably live — we never read `[user_ptr - 24]` of a possibly-freed object
  at walk time.
- `__mm_free` decrements it, using the destructor it already read from the still-live
  header.
- `mm_realloc` frees the old buffer directly (bypassing `__mm_free`), so it calls
  `__leak_count_dec` on the preserved destructor to avoid a permanent `+1` skew per
  realloc. That call is unconditional in `runtime.std` (which has no conditional
  compilation); `__leak_count_dec` compiles to an empty no-op when the census is off.
- At exit, `__leak_report_dump` (injected into `mrt_start` right before the
  `mrt_leak_check` gate) freezes the histogram, compacts + sorts the non-empty
  buckets in place, and prints the table.

## Testability note

Because Maxon's type system forbids recursive types (`E4014`), safe user code
**cannot** construct a reference cycle, and reference counting frees everything
else deterministically — so a leaking program is not expressible in ordinary
Maxon. A clean program's empty table (`total=0`, exit 0) is directly verifiable; a
**populated** table is exercised by running an instrumented compiler over a real
input (e.g. a hello-world self-compile), where genuine compiler-bug leaks appear.
