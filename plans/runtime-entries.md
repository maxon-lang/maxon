# A runtime entry is one row of one table, read by the usage scan, the target check, the installers and the stdlib gates: no fact about it comes from its name's prefix

Class issue: #153 · Priority: P1

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. The plan lands once, after its last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | R1 | Each handle destructor is an entry-table row whose real body follows whether the program opens such a handle, never a name prefix |  |  |  |
| [ ] | R2 | Whether the scheduler is installed is decided once, before the entry-stub calls and the green-thread usage are read | R1 |  |  |
| [ ] | R3 | One entry roster per sampling shape is read by its installer and the target check, and library source naming another lane's entry is a positioned E3104 | R1 |  |  |
| [ ] | R4 | The entry table decides which hosts provide a debug-host entry, and the stdlib's #if os gates are checked against it in both directions at build | R3 |  |  |
| [ ] | R5 | Every remaining runtime family is rows of the entry table and the name-prefix predicates are gone | R1 |  |  |

## Problem

No table lists the runtime's entries. Each fact about one is a separate predicate over the
callee's bytes, most of them `bytesStartWith`: about forty `is…RuntimeCallee` functions
(`isGtRuntimeCallee`, `isProfileRuntimeCallee`, …). `recordFamilyCallUsage` maps a name to usage bits,
`calleeHostFacility` maps it to a host facility, the `isThrowing…` predicates to a `throws` clause, and
each installer builds what its own code lists. The four can disagree, and a mention is indistinguishable
from a use: `noteRelocatedFunctionUsage` passes every function name in a layout to `recordCallUsage`
after `pipeline.usesGreenThreads` has been read, guarded for the heap (`heapArrivedTooLate`) and for
nothing else. `RuntimeEntryWithStandIn` (five rows) is the one place a row owns its name, its return type
and its real-body condition.

## Design

That roster grows into the entry table: a row carries its name, its signature, the runtime
that owns it, the hosts that provide it, what a call to it turns on, whether it throws, and whether a
mention alone gets a stand-in. The usage scan, the target check, the installers and the stdlib gate check
read rows; a signatureless `__` callee with no row is a compiler panic naming it. Go's compiler does this:
`cmd/compile/internal/typecheck/builtin.go` holds `runtimeDecls`, one generated table of every runtime
function the compiler may call with its signature, `LookupRuntime` (`typecheck/syms.go`) fails on a name
outside it, and `ssagen/intrinsics.go` keys per-architecture provision in one table.

## Stages

- R1 (#153): each handle destructor is a row whose real body follows "the program opens such a handle",
  never the prefix; the four target-keyed stand-in arms (`ManagedFileRuntime`, `ManagedDirectoryRuntime`,
  `ManagedSocketRuntime`, `ProfileRuntime`) go; `noteRelocatedFunctionUsage` gets the scheduler twin of
  `heapArrivedTooLate`; a row carries its parameter shape. First step: a program that opens nothing and
  holds a file handle's type in an array. Shown by: the
  `an-array-of-process-handle-holders-is-dropped-on-every-lane` program of
  `specs/handle-holder-arrays.md` built for x64-linux and arm64-linux has no `__prof_seized_release`; a
  `tests/emittedRuntime` case pins "no scheduler" for a `.shutdown()`.
- R2 (#159), after R1: whether the scheduler is installed is decided once, before `EntryStubCalls` and
  `pipeline.usesGreenThreads` are read; a relocated `__gt_` name with no row panics. The `GtRuntime`
  comment "A clock read therefore turns on usesGt" is corrected.
- R3 (#220), after R1: one roster per sampling shape (Win32, ptrace, Mach), read by its installer and by
  the target check; an installer building outside its roster panics; library source naming another lane's
  entry is a positioned E3104. Shown by: library-tier source calling `__Builtins.profileSeizedOpen`
  outside `#if os(Linux)` with `--target=x64-windows` and `--target=arm64-macos`. The unmerged commit
  a98079196 (branch `claude/nice-bell-aepj9a`) holds an earlier form that no longer applies. If this
  removes `targetProvidesEveryFacility`, #51 closes with it.
- R4 (#294), after R3: the table decides which hosts provide a debug-host entry, and the stdlib's
  `#if os(...)` gates are checked against it in both directions at build.
- R5: the remaining families become rows and the prefix predicates go, one family per commit.

## Members

- #159 — `usesGt` can turn on after the snapshots that read it, for a relocated `__gt_` name off the scheduler roster; stage R2.
- #220 — library source naming another lane's sampling entry panics the backend instead of E3104; stage R3.
- #294 — which hosts provide a debug-host entry is decided twice, by the stdlib's `#if os` gates and by the facility table; stage R4.

#153 is this issue; its repro is stage R1's.

## Related, not folded

- #51 — `targetProvidesEveryFacility` is recomputed per function; closes with R3 only if R3 deletes it.
- #133 — a `RequiredRuntime` function is kept from the inliner only; fixed in the interprocedural passes.
- #157 — the scheduler's ordering checks do not see functions installed after it; its own mechanism.
