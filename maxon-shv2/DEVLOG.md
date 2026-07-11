# maxon-shv2 — Development Log (living document)

This is the onboarding document for `maxon-shv2`, the ground-up rewrite of the
Maxon self-hosted compiler. It is **not a changelog**. Each section documents
the *operation and invariants* of one part of the compiler as that part is
built, so a future agent can understand the design without re-deriving it from
the code. See [`PLAN.md`](./PLAN.md) for the full plan, milestone sequence, and
locked design decisions.

**Reading order for a new contributor:** Design Pillars → then whichever
subsystem section is relevant. The subsystem sections are filled in as the
corresponding code lands (they are stubs until then).

---

## Design pillars (why shv2 exists)

v1 (`maxon-selfhosted`) works but the two *integral* features — static
ownership/borrowing and parallel incremental compilation — were retrofitted late
(≈8 shared `Project` sidetables + a 7,755-line refcount inserter), making it
slow and memory-hungry (5–6 GB). shv2 designs these in from the first commit:

1. **Static ownership/borrowing** — compile-time move/borrow checking that drops
   values deterministically at scope exit; runtime refcounting only where escape
   analysis proves genuine sharing.
2. **Parallel incremental compilation** — green-thread fan-out over per-file
   parse and per-function passes, with the multi-core runtime prerequisites
   proven *before* the first compiler milestone.
3. **Binary event-log tracing** — DebugStream binary events to shared memory
   (near-zero overhead when off), decoded by `maxon-sharp` as the runner; powers
   `mm-trace` for ownership/memory debugging.

**Final acceptance:** `maxon-shv2.exe` compiling itself in **≤30 s**,
**≤1.7 GB RAM**, **>90% CPU** across all cores.

---

## Core invariants (fill in as subsystems land)

These are the load-bearing invariants the plan calls out. Each is documented in
full in its subsystem section once built; listed here as an index.

- **Ownership-kind lattice** — `trivial` · `owned` · `borrow` · `shared`.
  Born at the Maxon tier, fully resolved before `lowerMaxonToStd`. Three
  first-class homes, zero sidetables: (1) `OwnershipKind` attribute on every
  value/binding, (2) signature ownership modes in the function type
  (param `consume`/`borrow`/`copy`, return `owned`/`borrow`), (3) explicit
  `own.*` ops in the block stream. *(→ Own tier section)*
- **Parse-staging registry set** — the parser writes only into a per-file
  `FileParseArtifact` (MaxonModule fragment + key-and-value bundle for every
  registry it would touch); `mergeArtifacts [M]` folds them into `Project` in
  fixed source-path order, doing all duplicate detection at merge time.
  *(→ Frontend / parse-staging section)*
- **rdata deterministic-merge invariant** — the backend captures rdata constants
  chunk-locally and merges them into the shared `GlobalDataTable`
  single-threaded in function order (idempotent-by-label dedup). Content-derived
  keys for all other shared appends (FNV-1a panic labels, `__float_<bits>`).
  *(→ Backend section)*
- **DebugStream schema is frozen** — 128-byte header, ticket spinlock, MM
  `0x01–0x09`, Sched `0x20–0x2C`, Depth `0x40/41`, Dbg `0x50–0x5E`, `MXDS_TAGS`
  blob. New events get new unused type codes; existing codes are never
  reinterpreted. *(→ Event-log section)*
- **1-core-vs-N-core byte identity** — blocking gate for the entire parallel
  phase. *(→ Parallel driver section)*

---

## Subsystem sections

### Frontend (lexer, parser, parse-staging)
_stub — filled in at M1._

### Maxon dialect
_stub — filled in at M1._

### Own tier (ownership infer / check / escape / drops)
_stub — filled in at M6._

### Pass pipeline
_stub — filled in at M1, extended each milestone._

### Query spine (incremental)
_stub — skeletal from M1; warm-rebuild assertion joins the gate at M2._

### Parallel driver
_stub — per-function fan-out enabled at M5._

### Backend (Std → MIR → Target, runtime emitters)
_stub — thin mov/ret slice at M1; MM runtime + DebugStream producer at M6; GT
scheduler at Phase F._

### Event log & mm-trace harness
_stub — Track 0 Foundation 2._

---

## Milestone ledger

Checkboxes track landing against `PLAN.md`. Correctness-only gate through
Phase E; budget gate (≤30 s / ≤1.7 GB / >90% CPU) becomes hard at Phase F.

- [ ] **Step 0** — plan + DEVLOG materialized in repo
- [ ] **Track 0 / Foundation 2** — binary event log + mm-trace harness
- [ ] **Track 0 / Foundation 1** — multi-core green threads hardened
- [ ] **Track 0** — validation harness (multi-core gate)
- [ ] **M1** basics · [ ] **M2** variables · [ ] **M3** arithmetic
- [ ] **M4** control flow · [ ] **M5** functions (fan-out)
- [ ] **M6** heap+drops · [ ] **M7** moves+borrows · [ ] **M8** escape→refcount
- [ ] **M9** structs · [ ] **M10** strings · [ ] **M11** arrays
- [ ] **M12** enums · [ ] **M13** closures · [ ] **M14** interfaces/generics · [ ] **M15** error handling
- [ ] **M16** feature-complete · [ ] **M17** self-compile · [ ] **M18** budget gate
