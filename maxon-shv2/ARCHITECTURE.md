# maxon-shv2 — Architecture (living document)

This is the onboarding document for `maxon-shv2`, the ground-up rewrite of the
Maxon self-hosted compiler. Each section documents the *operation and invariants*
of one part of the compiler as that part is built, so a future agent can
understand the design without re-deriving it from the code.

It is **not a changelog** and **not a plan**:
- [`PLAN.md`](./PLAN.md) — the full plan, milestone sequence, and locked design
  decisions (what we intend to build, in what order).
- [`DEVLOG.md`](./DEVLOG.md) — the dated log: milestone ledger and recon findings
  (what has actually landed, and what we learned along the way).
- **This document** — how the thing works, and why it is shaped that way.

**Reading order for a new contributor:** Design Pillars → Core invariants → then
whichever subsystem section is relevant. The subsystem sections are filled in as
the corresponding code lands (they are stubs until then).

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

- **Three tiers, flat op unions** — `Maxon → Std → Target`; NO MIR tier (v1's
  added no value model — Std's `ValueId`s already are the virtual registers).
  Every dialect's op union is FLAT with a required `*OpMeta` struct backing, never
  nested: Maxon boxes each payload-carrying union case, so nesting costs a heap
  object per level on the compiler's most numerous object. *(→ Std dialect section)*
- **Band-append invariant** — `StdOp` variants sit in category-contiguous bands so
  passes can cover a category with one `match` range arm. **A new variant is
  appended at the END of its band, never inserted into the middle** — a range arm
  silently swallows anything inserted between its endpoints, with no missing-case
  error. This is the ONE place "no silent unhandled cases" has no compiler
  backstop; reviewers enforce it. *(→ Std dialect section)*
- **No sugar reaches the backend** — `sugar`-category `StdOp`s are eliminated by
  the Std→Std `lowerToMachineForm` pass. Guarded by `assertNoSugarOps` in
  `buildBackend` (the target-neutral entry, so every backend inherits it) + an
  explicit `panic` arm per sugar variant (never a bare `default`). Replaces the
  type-level guarantee v1 got from its MIR boundary; spec tests are the real
  guarantee. *(→ Std dialect section)*
- **Live ops ≠ `module.ops`** — `IrModule.ops` is append-only; block-rebuilding
  passes leave the ops they replaced behind as orphans. Any pass asking "what ops
  are actually here?" must go through `IrModule.liveOpIndices(func)`, the single
  home of the blockRefs → opRefs+terminator walk. Scanning the flat array sees dead
  ops from every prior pass. *(→ Std dialect section)*
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
- **Coloring rewrites in place** — the register allocator rebuilds each op with colored
  operands and writes it back at the SAME `module.ops` index. `block.opRefs` entries and
  `terminatorIndex` are indices into that array; a pass that appended or compacted instead
  would invalidate every reference in the function. Virtual registers ARE Std `ValueId`s —
  there is no second vreg numbering. *(→ Register allocator section)*
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

**Type layer landed (M1 Chunk A).** `Compiler/Lexer.maxon` is copied verbatim
(`tokenize(source) -> TokenArray throws LexerError`, table-driven DFA) with its
two non-stdlib deps brought along: a trimmed `Compiler/Logger.maxon`
(LogLevel/LogCategory + emit) and the `ByteOffset` typealias in
`Targets/Shared/BinaryHelpers.maxon`. `NumberParsing.maxon` (parseInt) copied +
one root-cause fix (a byte-element-type mismatch in `b"…"` vs `.toByteArray()`
comparison, latent in v1, masked there by the compile cache). IR containers are
in: `IR/IrValueId.maxon` (zero-dep typealiases), `IR/IrBlock.maxon`,
`IR/IrModule.maxon` (generic `uses Op`; `MaxonModule = IrModule with MaxonOp`),
`IR/IrFunction.maxon` (deliberately **thinned** — dropped v1's per-ValueId
refcount side-tables + 6-way `OwnershipClass` + generics/ABI cone, which the
static Own tier and later milestones supersede), `IR/Maxon/SourceRange.maxon`,
`IR/Maxon/Scope.maxon`.

**Parser + parse-staging landed (M1 Chunk C).** `Compiler/Parser.maxon` is the
thin `return <int-literal>` slice: `parseModule → dispatchTopLevel(function) →
parseFunction → parseOptionalReturnType → parseStatements → parseReturnStatement
→ parseExpression → parsePrimary → parseIntLiteral → emitLiteral`, with a small
index cursor and a per-function `ValueIdRef` (seeded at `paramCount`). Two
structural changes from v1. **(1) Value numbering is born here** — every op's
result gets its dense function-local `ValueId` at emit time, not at the Maxon→Std
boundary (see the Maxon dialect section). **(2) The parallel-ready seam: it takes
no `Project` and writes exclusively into a per-file `FileParseArtifact`** — the
file's own
`MaxonModule` fragment, its own LOCAL `TypeNameInterner`, and an ordered
`funcReturnTypes` contribution array. So a file's parse is a pure function of
`(tokens, filePath, namespace)` with zero shared state (M5's per-file fan-out
needs no further parser change). Anything outside the current slice is rejected
with a positioned `ParseError` that `queryParseOps` turns into a project diagnostic
(rendered `error E<code>: <file>:<line>:<col>: <msg>` when the span is real, or
prefix-free for whole-program checks whose line is 0) — the parser itself never
touches `Project` or `project.diagnostics`.

**M2 grew the slice** to `let`/`var` statements, variable references, and a
left-associative binary `+`. `let`/`var` bind a name to the initializer's SSA
`ValueId` in `Scope` (`declareValueBinding`); a reference is a `Scope.lookupValue`
resolved AT PARSE TIME — so `let x = 42; return x` mints no `x` op at all and lowers
identically to `return 42`. (`var` parses like `let`; mutability + reassignment
arrive with the mem2reg/slot model when a spec needs them.) `+` emits the one new op
(`MaxonOp.binOp`).

**M3 replaced the left-fold with a Pratt precedence climber.** `parseBinary(minPrec)`
parses an operand then, while the next token is a supported infix op with
`precedence >= minPrec`, consumes it and recurses the right operand at `prec + 1`
(the `+1` is what makes it left-associative). `parseUnary` is the climber's leaf:
a prefix `-` consumes then parses a PRIMARY (not another unary), so unary binds
tighter than every binary op but does not chain — `- -x` raises **E2004** at the
second `-`. Precedence table: multiplicative (`*`) above additive (`+`/`-`).
Deferred operators (`/`/`mod` → M5, comparison → M4) are rejected in operator
position with a positioned `E3010 … arrives at Mn` note, so the precedence table
extends cleanly rather than being rewritten. Integer `sub`/`mul` join `add` as
`binOp` opcodes; unary minus is `MaxonOp.unaryOp`/`StdOp.unaryOp{neg}` (always a
runtime `neg`, no `-<literal>` const-fold — uniform with `-x`).

**`Compiler/ParseStaging.maxon`** owns `FileParseArtifact` + `mergeArtifact(project,
target, artifact)`, the **single writer** of the shared `Project` derived
registries. It (1) folds the artifact's local interner into `project.typeNames`
(via `TypeNameInterner.foldInto`, returning a `TypeNameRemap`), (2) remaps the
artifact's `named(id)` references when the fold moved ids (M2 multi-file path;
identity for M1's single file → skipped), (3) offset-merges the `MaxonModule`
fragment into the accumulator via `IrModule.merge`, and (4) commits the
`funcReturnTypes` entries. Because the parser wrote nothing to `Project`
speculatively, there is **no ParseDelta rollback dance** — v1's ~28-registry
rollback machinery is replaced by "artifacts are the source of truth; rebuild
the derived registries from them on every `queryAllModule` miss"
(`resetMergeTargets`). The registry set is one family (`funcReturnTypes`) at M1;
it grows toward v1's ~28 per milestone, each as an ordered contribution array.
Interner NOTE: for a *cached* artifact, a non-identity remap must clone before
rewriting (it must not mutate the cache in place) — a documented M2 refinement,
never triggered at M1 (single file → identity remap).

`Compiler/Project.maxon` **grew** the `Project` struct (`db`/`funcReturnTypes`/
`typeNames`/`rootPath`/`target`/`diagnostics`/`globalData`) + `createProject` +
the diagnostic sink helpers, GROWING (not redefining) the Chunk-A foundation.

**Scope ownership scaffold:** `Scope.ownedStack` runs parallel to `frameStack`,
pushed/popped in lockstep by `pushScope`/`popScope` (panic on unmatched pop —
they must stay parallel). All 4 `declare*` paths funnel through
`recordInCurrentFrame`, which records into `ownedStack` **only when the binding
is non-trivial** — inert at M1 (all bindings trivial), drained at M6 to emit
`own.drop`/`own.release`.

### Maxon dialect

**Landed (M1 Chunk A), thin + growable.** `IR/Maxon/MaxonDialect.maxon`:
- `MaxonOp` union — `literal(result, value, valueType, range)`, `ret(retVal,
  range)`, `retVoid(range)`, each tagged `= MaxonOpMeta{category}` (v1's
  invariant: a new op cannot be added without declaring its `OpCategory`).
  Variants sit in **category-contiguous bands** with the same **append-at-the-end-
  of-a-band** invariant `StdOp` carries — a range arm silently swallows anything
  inserted between its endpoints.
- **Value operands are `ValueId`s, minted by the PARSER** (a correction of v1;
  landed post-M1). v1 named Maxon values with `ByteArray`s and let
  `lowerMaxonToStd` assign the "real" ids — but v1's result names were themselves
  synthetic `$tN` strings off a per-function counter (`mintSynthName`), and the
  ids were a *second* per-function counter. The mapping was a **bijection between
  two dense counters**, bought with a heap `String` + heap `ByteArray` per value
  and a byte-sequence-keyed hash map (O(len) compares; one insert + one lookup per
  value). It constructed **no SSA**: user variables are `VarSlot`s, and real SSA
  construction is `mem2reg`'s job at the Std tier. So `lowerMaxonToStd` passes ids
  through verbatim and `nameToId`/`defineName`/`resolveName` do not exist. What
  the front end genuinely needs it keeps in better homes: **source spans** on the
  op (`range` — what the LSP actually maps cursor offsets against; it never needed
  names) and **name resolution** in `Scope`. Identifier TEXT that is not a value —
  a field name, a method name — stays a `ByteArray`; the split is "is this operand
  a VALUE or a NAME?", ~90/10 in favour of values.
- `retVoid` is a **distinct variant**, not a `ret` carrying an absent-value
  sentinel: `ValueId` has no empty value, and a sentinel is what the project
  forbids. (v1 passed an empty `ByteArray`, which only ever "worked" because a
  void `main` is rejected by E3002 before lowering runs.)
- **Source spans are NOT a field on the op.** Every Maxon op has one, but it lives
  in `SourceRangeTable` — an op-parallel store of **four dense scalar columns**
  (`startBytes`/`endBytes`/`lines`/`columns`), keyed by op index. `SourceRange` is
  a `type`, so an inline `range` field is a POINTER to a heap-boxed SourceRange:
  one live heap object per op, on the compiler's most numerous object, retained for
  the module's lifetime. (Note an `Array with SourceRange` would NOT fix this — it
  is an array of pointers to the same boxes. The scalar columns are the point.) A
  `SourceRange` is materialized only by `SourceRangeTable.get`, i.e. only when a
  diagnostic or an LSP query asks — the cold path. Parsing and lowering never
  allocate one.
  - **A MAXON-TIER table.** It lives on `FileParseArtifact` (per file) and
    `Project` (whole-program, folded by `mergeArtifact` in lockstep with
    `IrModule.merge`). Std and Target carry no spans — the same reason `StdOp` has
    no `range` field.
  - **Parallelism invariant.** Ops and spans are appended together by the single
    choke point `FileParseArtifact.emitOp`/`emitTerminator` — the only way a Maxon
    op is created. `SourceRangeTable.record` asserts the op index it is handed
    equals the table's own count, which IS the invariant, checked on every op: if
    anything ever appended a Maxon op behind the choke point's back, the next emit
    panics instead of silently shifting every later span by one.
  - Per-ARGUMENT spans (v1's `argRanges` on the call ops, M5+) are genuine per-arg
    payload, not per-op, so they stay inline on the op as a `SourceRangeArray`.
- `MaxonType` union — `boolean/integer/float/named(TypeNameId)/exitCode/
  unresolved`. `named` is the parser's interned type reference; `unresolved` its
  placeholder. **`exitCode` (added M1 Chunk C)** is the RESOLVED form of the
  `ExitCode` builtin alias — a width-FREE tag like `boolean`/`integer`. Its
  unsigned-32-bit width is assigned only at the Maxon→Std boundary
  (`maxonTypeToStdType` → `StdType.u32`), so the "MaxonType carries no width;
  width collapse happens only in lowering" invariant holds. TypeResolution
  eliminates `named`/`unresolved` before lowering. Void is not a MaxonType —
  `MaxonReturnType{void, value}` carries return slots.
- **`OwnershipKind` lattice** — `trivial | owned | borrow | shared`, the plan's
  first-class Maxon-tier ownership. Three homes, zero sidetables: (1) the
  attribute (`VarInfo.ownership`, defaulted `trivial`), (2) signature modes in
  the function type (M5/M6), (3) explicit `own.*` ops (M6). `isTrivialOwnership`
  is the shared classifier. Present-but-inert at M1; M6 activates it.

Later milestones GROW `MaxonType` (string/char/function/generic/interface arms),
`MaxonOp` (var/arith/call/control-flow), and the signature ownership modes.

### Std dialect

**Landed M1 Chunk A; restructured post-M1 (flat + 3-tier).** `IR/Std/StdDialect.maxon`
is the mid-level tier — and, since the MIR tier was dropped, also the
**machine-level** tier. Two invariants define its shape, and both are load-bearing:

**1. There is NO MIR tier.** Tiers are `Maxon → Std → Target`. v1's `lowerStdToMir`
was ~90% a mechanical 1:1 rename: Std's `ValueId`s **already are** the infinite
virtual registers MIR claimed to introduce, so the tier bought no new value model —
only a whole extra module copy in RSS and a wall that hid dead code (v1's
`MirOp.movReg` has zero construction sites yet still forces match arms in five
places). Everything v1 ran *on* MIR (`commuteForCoalescing`, `scheduleInstructions`)
is a Std-tier pass here, and MIR's one piece of real content — desugaring — becomes
a **Std→Std** pass, `lowerToMachineForm`.

**2. `StdOp` is FLAT** — one union, no `StdOp.arith(StdArithOp.const(StdArithConst))`
nesting. Maxon heap-boxes and refcounts every payload-carrying union case, so each
nesting level is another heap object: nested = **3 boxes per op**, flat = **1**, on
the most numerous object in the whole compiler. (v1's own StdDialect header admits
the nesting was a migration artifact kept "so existing pass code that matches on
these variants keeps working," not a design.) Coarse membership — "is this any kind
of call?" — is recovered without nesting from the `StdOpMeta` struct backing every
variant is REQUIRED to carry: `category` (`StdOpCategory`), `role` (`OpRole`:
plain/ret/errorReturn/param), the two inliner axes (`isPure`,
`isUnsupportedInInlineBody`), and the scheduler facts (`isMemory`/`isStore`/
`isCall`/`clobbersFlags`/`isCmp`). A new variant cannot be added without declaring
all of it.

**Purity is declared PER VARIANT, and that is the flat union's real payoff.** v1
could only answer "is this op side-effect-free?" by CATEGORY, at the match site:
`Inliner.isPureOp` reads `arith gives true; control gives true; call(c) gives
c.rawValue.isPureForInlining; memory/system give false`. A category blanket has no
way to say a *trapping* op is impure — v1's `arith gives true` calls an integer
`div` pure even though it faults on divide-by-zero. With per-variant metadata, M3's
`div` simply declares `isPure: false` and nothing else changes. (`clobbersFlags` is
a lowering artifact, not a side effect: a flag-clobbering op is still `isPure`.)

**Bands, and the one rule that has no compiler backstop.** Variants are declared in
**category-contiguous bands** in `StdOpCategory` order (arith · control · call ·
memory · system · sugar), so a pass that treats a whole category uniformly covers it
with ONE `match` range arm instead of one arm per variant. The cost: a range arm
names its endpoints, so it **silently swallows anything inserted between them** — no
missing-case error, and the new op is misclassified at every range-arm site at once.

> **INVARIANT: append a new variant at the END of its band. Never insert into the
> middle of one.** This is the single place the project's "no silent unhandled cases"
> rule is a convention rather than a compiler-enforced property. Reviewers must
> enforce it by hand.

**The sugar band.** v1 enforced "no sugar reaches the backend" with a *type*: sugar
ops lived in `StdOp` and had no `MirOp` counterpart, so the tier boundary made them
unrepresentable downstream. Collapsing the tier gives that up; the replacement is
deliberate, not accidental — (1) `assertNoSugarOps(module)`, a whole-module gate
run by **`buildBackend`**, the single target-neutral backend entry (NOT inside the
x64 lowering — sugar is a property of the Std tier, not of x64, so putting it at the
one shared entry means arm64/wasm inherit it instead of each having to remember to
re-add it), and (2) an explicit `panic("… must be desugared by lowerToMachineForm")`
match arm per sugar variant (never a bare `default`, which is exactly what would
swallow a new case). **Spec tests are the real guarantee.** Both guards are vacuous
at M1 — no sugar variants exist yet — so `lowerToMachineForm` itself is NOT written;
it arrives with the first sugar op (M6 `drop`/`free`, M14 descriptors/witness
tables).

`assertNoSugarOps` walks **live** ops via `IrModule.liveOpIndices(func)`, never the
flat `module.ops` array. That array is append-only: block-rebuilding passes clear a
block's `opRefs` and re-append fresh ops, leaving the replaced ops behind as orphans.
`lowerToMachineForm` is exactly such a pass, so the sugar ops it desugars *survive in
`module.ops`* — scanning the flat array would panic on dead entries and report a
desugaring failure that never happened. `liveOpIndices` is now the single home of the
funcs → blockRefs → blocks → opRefs+terminator descent (`liveOpCount` goes through it
too).

Flat does **not** mean one variant per opcode: a family sharing an operand shape
(`add`/`sub`/`mul`) stays one variant with an opcode field, but a member needing
*different metadata* (a compare sets `isCmp`) splits out, because the `StdOpMeta`
backing attaches per variant and cannot reach an opcode buried in a field. M2
landed `binOp(… opcode StdBinOpcode)` with the `add` opcode; M3 extends the opcode
set and splits out a separate `cmp(…)` for exactly that reason.

The union holds `const(resultId, value, valueType)`, `ret(retValId)`,
`binOp(resultId, lhs, rhs, opcode StdBinOpcode)` (M2 `add`; M3 added `sub`/`mul` —
same variant, opcode field, `StdOpMeta` `isPure: true` since integer add/sub/mul
never trap / `clobbersFlags: true`), and `unaryOp(resultId, operand, opcode
StdUnaryOpcode)` (M3, `neg`) — all appended at the END of the arith band. M5's
`div`/`mod` split out (they need `isPure: false` — `idiv` traps — and fixed-register
lowering); M4's comparison splits out as `cmp` (`isCmp: true`). `const` carries a
`StdType` rather than v1's
`constI64`/`constF64` opcode pair — it subsumes MIR's `isFloat` bool and makes
i32/u8/f32 representable without new opcodes.
`StdType`/`StdTypeInfo`/`CastCategory`/`StdReturnType` are unchanged from Chunk A.

### Own tier (ownership infer / check / escape / drops)
_Passes are a stub — filled in at M6. But one structural decision is PINNED now,
because getting it wrong at M6 is a rewrite and writing it down today is free:_

**`own.*` ops are a BAND OF `MaxonOp` — not a separate dialect, not a tier.**
"Own tier" names a *pass group* (`OwnershipInfer` → `OwnershipCheck` →
`EscapeAnalysis` → `InsertDrops`), all of which run over the Maxon module. It is
not a fourth IR tier: tiers are `Maxon → Std → Target`, full stop.

This follows from the container. `IrModule uses Op` is generic over **exactly one**
op type (`IrModule with MaxonOp`, `with StdOp`, `with TargetOp`), so an op living
in the Maxon block stream **is a `MaxonOp`**. There is no `OwnModule` and no
`lowerMaxonToOwn`. PLAN.md:50's `IR/Own/OwnDialect.maxon` is a FILE — a place to
group the `own.*` variants, their `OpCategory.ownership` band, and the lifetime
ids — not a dialect in the tier sense.

> **Do NOT introduce `union OwnOp { move, borrow, drop, retain, release }` nested
> as `MaxonOp.own(OwnOp)`.** Maxon heap-boxes every payload-carrying union case, so
> nesting costs a **second heap object per op** — the exact anti-pattern the 3-tier
> collapse (`f3b6f99ae`) removed from `StdOp`, on the ops the Own tier touches most.
> The `own.*` variants go directly into `MaxonOp`, appended at the end of the
> `ownership` band, exactly like every other variant.

`OpCategory.ownership` already exists in `MaxonDialect.maxon` for this band.

### Type resolution & semantic check (Maxon tier)

**Landed (M1 Chunk C).** `TypeResolution.resolveTypes(project, module)` replaces
every `named`/`unresolved` `MaxonType` in the module's function signatures with a
concrete one and re-syncs each resolved return type into `project.funcReturnTypes`
(SemanticCheck's source of truth). M1 SHORTCUT: the only named type resolved is
the `ExitCode` builtin alias, recognized **by name** without loading the stdlib
(→ `MaxonType.exitCode` → u32); any other named type panics loudly (user types +
the ranged-typealias registry arrive at M9). `SemanticCheck.semanticCheck(project)`
is the two `basics.md` checks reading the resolved registry: **E3001** (no `main`)
and **E3002** (`main` does not return ExitCode; the M1-exercised case is a void
`main`). Both append a `Diagnostic`; the pipeline's error gate then bails.

### Pass pipeline

**Landed (M1 Chunk C); MIR pass removed post-M1.** `PassPipeline` owns the two tier
modules it actually drives (`maxonModule` → `stdModule`) + a `project` handle + the
scheduled `PassKind` list. It owns NO target module — the pipeline's last tier is
Std, and the backend is backend-direct — so the write-once-read-never `targetModule`
field (and the `PipelinePhase` enum, which had no callers) went out with `mirModule`
as the same vestigial-tier residue. `dispatch` wires each pass to its free function:
`resolveTypes`/`semanticCheck` (read the Maxon module + project), `lowerMaxonToStd`
(produces the next tier). `run()` gained the **error gate**: after each pass,
`projectHasErrors → throw CompileError` — so a program that fails semanticCheck
(e.g. has no `main` for the backend entry stub to call) never reaches
lowering/backend. The driver (`Compiler.compile`) builds the pipeline with
`buildDefaultPipeline()`, runs it, then goes backend-direct (`buildBackend` →
`writeExecutable`; a CodeResult is not an IR module).

**The pipeline ends at `StdModule`** — there is no second "backend pipeline" over a
machine tier, because there is no machine tier (see Std dialect). Everything that
would have gone there grows in HERE, appended after `lowerMaxonToStd`: the Std opt
passes (M3+), the Own-tier passes (M6), and `lowerToMachineForm` (M6).

Diagnostics are printed by `compile` on the failure path and the error is
re-signaled as a FRESH `compileError` (re-throwing the *caught* error NULL-increfs
under the C#-emitted refcount runtime; a fresh throw of the same arm is clean — a
driver-shape gotcha worth remembering).

### Query spine (incremental)

**Landed (M1 Chunk C), content-hash-keyed.** `QueryDatabase`/`QueryEngine`/
`Queries` implement `querySourceFile → queryTokens → queryParseOps →
queryAllModule`. The load-bearing difference from v1: **memo validity is a
content-hash compare, not a revision-counter walk** (PLAN.md §78). `fileChanged`
computes an FNV-1a `ContentHash` per file (no shared `currentRevision` counter to
contend on — parallel-safe); each per-file memo stamps the `keyHash` it was
derived from and is valid iff that still equals the file's current hash;
`queryAllModule` keys its merged-module memo on the COMPOSITE hash folded over
every file's hash in source-path order. `queryParseOps` produces a
`FileParseArtifact` and touches no `Project` state (the parser is pure);
`queryAllModule` on a miss `resetMergeTargets` + re-folds artifacts via
`mergeArtifact`, so there is no rollback. The dependency graph
(`recordDependency`/`clearDepsFor`/`activeQueryStack`) is recorded from day one —
M1's single file never drives an invalidation, but recording it now is what let
the **M2 warm-rebuild byte-identity gate** join without retrofitting.

**Warm-rebuild gate landed (M2).** `maxon-shv2 verify-warm-rebuild <file>`
(`Compiler/VerifyWarmRebuild.maxon`) is the standing gate that asserts two
properties of the spine and exits 0 iff both hold: (1) **determinism** — compile
the file to a `CodeResult` twice, each with its OWN fresh `Project`/`QueryDatabase`
(two independent cold compiles share no module object), byte-compare all emitted
sections; (2) **incrementality** — in one `Project`, `queryAllModule` twice
(cold-miss then hit) plus a `probeRebuildCacheHits` that re-queries each file's
tokens/parse, asserting via the DB hit/miss counters that the rebuild is a
content-hash cache HIT. It sidesteps a real trap: `resolveTypes` mutates the merged
module in place and `queryAllModule` hands back that same memoized object, so the
determinism check must use independent projects and the cache check must run only
`queryAllModule` (never the mutating pipeline) between its probes. `build` and the
gate share one `compileToCodeResult(project)` — a determinism gate over a divergent
path would prove nothing.

### Parallel driver

**Runtime multi-core proven (Track 0 / Foundation 1, x64-windows).** The C#
emitter's green-thread scheduler + sharded allocator run correctly across many
worker Ps. Empirical: a 32-GT CPU/alloc burst compiled by `maxon.exe`, observed
under `maxon monitor`, ran on **16 distinct worker Ps** (P0–P15 = ncpu) unclamped
and **exactly 1 (P0)** under `MAXON_MAX_PROCS=1`, both producing the correct
deterministic result — so the `__gt_enqueue` worker-spawn gate fires for a
pure-CPU burst (1a.5), and the `MAXON_MAX_PROCS` clamp is a real single-thread
knob. Concurrency primitives (`maxon-sharp`-only, no shared-stdlib change):
`__Builtins.cpuCount()` (`maxon_cpu_count`, `GetSystemInfo`/`sysconf`, valid
pre-`__gt_init`), `__Builtins.parallelBoundary()` (`maxon_parallel_boundary`,
empty runtime stub + `IoStubs` entry so CPU-bound `async` passes E3073),
`MAXON_MAX_PROCS` clamp in `__gt_init` (both backends). Bisection tools:
`MAXON_SLAB_GLOBAL_LOCK` spinlock + `MAXON_SLAB_STATS` contention counters
(lock-wait, ownership-gate-miss) + `__Builtins.schedMaxActiveWorkers()`
high-water mark. **The validation harness (`maxon-shv2/track0/`) closed 1a.3**:
an allocation-torture program (main allocates managed arrays, hands them to
`async` tasks without retaining a ref → the arg is freed on the *worker* P → a
cross-P remote-free push back to P0) drove the remote-free MPSC to **7775
pushes** and, at high concurrency, surfaced a real **~2.5% NULL crash** in the
C#-emitted `__gt_spawn` (it passed `gt` to `__gt_enqueue` in R10, a Win64
caller-saved reg that `LeaveCriticalSection` clobbers on its contended
wake-a-waiter path; the main-thread spawn path — which main's own `mainThread`
GT with `stackBase==0` takes — doesn't preserve R10). Fixed by reloading `gt`
from its stack slot before the enqueue, matching ARM64. This is a latent
scheduler bug affecting **every** async/multi-core program `maxon.exe` emits
(including shv2.exe once it exists). The alloc-side ownership gate stayed at 0
misses throughout (untriggered backstop). The rdata deterministic-merge
invariant (below) is shv2's own future backend concern, not yet exercised here.

_Per-function fan-out enabled at M5._

### Backend (Std → Target, runtime emitters)
_Thin mov/ret slice landed at M1 (Chunk B2); MM runtime + DebugStream producer at
M6; GT scheduler at Phase F._ The M1 driver (`Compiler.compile`) feeds the parsed,
resolved, lowered `StdModule` straight into `buildBackend(stdModule, target,
globalData)` → `writeExecutable`.

`Std → Target` is the **only** tier boundary below Std (there is no MIR — see Std
dialect). `lowerStdToX64` (`Targets/X64/StdToX64Conversion.maxon`) asserts
`assertNoSugarOps` on entry, clones the block skeleton 1:1, then walks each function
lowering `StdOp`s to `TargetOp`s with still-virtual registers; `allocateRegisters` →
`insertPrologueEpilogue` → `augmentWithRuntime` → `emitFunctionChunk` →
`concatX64FunctionChunks` finish the job. The virtual registers the allocator colors
are Std's own `ValueId`s, unchanged since `lowerMaxonToStd` minted them.

`CodeResult.stdModule` (v1: `mirModule`) carries the mid-level module the backend
lowered FROM. v1 kept its MIR module here because the **wasm** backend consumes the
machine-level IR directly instead of going through a target dialect; with the tier
gone, Std *is* that machine-level IR, so wasm (M17) reads this field. x64/PE ignore
it.

The driver CLI is `maxon-shv2 build <file|directory> [-o <output>]`; a single-file
build writes next to the source (`basic.maxon` → `basic.exe`). **End-to-end
proven:** `maxon-shv2.exe build examples/basic.maxon` produces a PE that exits
**42**; a program with no `main` prints exactly `error E3001: No 'main' function
found` (exit 1); a void-returning `main` prints exactly `error E3002: Function
'main' must return ExitCode` (exit 1).

### Register allocator (Std `ValueId`s → physical GPRs)

**Landed M1 (Chunk B2) as a deliberate placeholder — `Targets/Shared/RegisterAllocator.maxon`.**

**What it colors is Std's own value space.** A `X64VReg` is either `physical(reg)` or
`virtual(id)` — and that `id` **is the `ValueId` `lowerMaxonToStd` minted**. There is no
separate virtual-register numbering anywhere in shv2; that is the 3-tier collapse cashing
out (the tier v1 introduced to "add virtual registers" was adding a rename — see Std
dialect). The allocator's input domain is exactly the Std value space, unchanged since
SSA construction.

**The M1 colorer (`MinimalColorer`) has no liveness at all:** assign each DISTINCT virtual
`ValueId`, in first-appearance order, its own fresh caller-saved GPR from a 6-register
pool (`rax`/`rcx`/`rdx`/`r9`/`r10`/`r11`); never reuse a register. This is **always
correct** — giving every value a distinct register cannot make two live values alias — as
long as a function's distinct virtual regs do not exceed the pool. At M1 that count is 1
(`movImm`'s result). Exhausting the pool is a **hard panic, never a miscompile**: the
placeholder fails loudly the moment a milestone outgrows it.

The pool's composition is the ABI, not an arbitrary choice. **R8 is excluded** because it
is the primary-return register under Maxon's custom convention — the return move targets it
as a *fixed physical* reg, so coloring some value into R8 could clobber an in-flight return.
RSP/RBP are the frame; the callee-saved set (RBX, R12–R15) is excluded because nothing
saves/restores it yet (the prologue pass emits no pushes until M5); RSI/RDI are caller-saved
under Maxon's convention but held back for the real allocator.

> **INVARIANT: coloring rewrites ops IN PLACE, at the same `module.ops` index.**
> `colorFunction` reads the op at index *i*, rebuilds it with colored operands, and writes
> it back at *i*. Every `block.opRefs` entry and every `terminatorIndex` is an index into
> that array — an allocator that appended colored ops or compacted the array would
> invalidate every reference in the function. (This is the append-only-`module.ops` fact
> from the other direction: passes may not renumber what other structures point at.)

Physical operands pass through `colorVReg` untouched, so the pass is safe on ops that mix
already-colored regs with virtual ones (the R8 return move; `mrt_start`'s RCX/R8).

**Where it sits in the backend chain, and why that order is forced:**
`lowerStdToX64` → **`allocateRegisters`** → `insertPrologueEpilogue` → `augmentWithRuntime`.
Regalloc runs **before** prologue/epilogue because *the frame is a function of the
allocation*: which callee-saved registers need a push/pop is known only after coloring, and
the frame size must cover the spill slots the allocator creates. It runs **before**
`augmentWithRuntime` because the `mrt_start` entry stub is hand-built in physical registers
with its own explicit `sub rsp, 0x28` frame — it must not be re-processed by either pass.
(shv2 tightens v1 here: `insertPrologueEpilogue` is the SOLE owner of both frame ops, where
v1's ISel pre-emitted an `epilogue` at each return that the pass then stripped and re-emitted.)

**The replacement (M3/M5) is the compiler's single most performance-critical pass, and
PLAN.md's premise about it was wrong on both counts.** The plan listed
`Targets/Shared/RegisterAllocator*` under COPY-near-verbatim, "operates on generic
structures." It does not: v1's 2,233-line SSA-dominance colorer is welded to v1's bespoke
`TargetModule` (an `x64Ops` array behind a `cpu` discriminator) and needs the
`TargetOpQuery` / `OpPattern` / register-mask machinery to find an op's register operands —
none of which shv2's thin `IrModule with TargetOp` tier has. And copying it would import the
wrong design anyway: **in v1, register allocation is ~74% of self-compile wall time** (~418 s
of 561 s; worst single function 58 s), against an shv2 acceptance budget of ≤30 s for the
*entire* self-compile. The allocator is where that budget is won or lost.

What v1 paid for, that shv2's allocator must be designed around from its first commit:
- **The interference graph is the cost.** v1's first-fit colorer walks `ig.neighbors`, so the
  graph is load-bearing for coloring — and v1's *reactive* spill/color loop rebuilds it from
  scratch each iteration, because `insertSpillCode` shifts positions and invalidates every
  range. The two structural wins v1 identified but never landed: kill the per-iteration
  rebuild (incremental remap, or **virtual spilling** that defers code insertion to the end),
  and replace the graph with an **availability sweep + on-demand adjacency**.
- **A one-shot (Braun–Hack MIN) spiller is not a drop-in.** v1 tried and failed: cross-block
  call-crossers cannot be relieved by splitting inside the def-block, and *spilling* a crosser
  rather than *splitting* it re-creates a re-crossing reload cluster. A correct one-shot
  spiller needs a genuine cross-block splitter (SplitKit-class).
- **Build it to be measured.** v1's "74%" figure stood for months with **no sub-phase
  attribution inside it** — the allocator's own phases were never individually profiled, and
  the wins that did land (an insertion sort in `sortByDomPreorder`; a 495 MB dedup matrix)
  fell only once someone measured. Sub-phase timers belong in the first commit, not a later
  perf push.
- **The correctness traps are all at this boundary.** Each of these was a real v1 miscompile:
  a call-clobber mask that listed a callee-saved register; a spill-demand sweep that skipped
  position-0 live-in intervals; rematerializable constants losing scarce registers to live
  pointers; a phi-copy trampoline that only one edge of a two-jump condition routed through.

`TargetOpMeta` is the extension point: it carries per-variant `isMemory`/`isStore`/`isCall`/
`setsFlags` today and GROWS with the coloring `pattern` + implicit-register read/def masks
(the epilogue reads R8; a call clobbers the caller-saved set) when the real allocator lands —
same "you cannot add an op without declaring its facts" discipline as `StdOpMeta`.

### Event log & mm-trace harness

**Producer (maxon-sharp / C# bootstrap) — verified working as of Track 0.**
Binaries compiled by `maxon.exe` with `--debugstream` emit a binary MM event
stream (128-byte header + 8-byte packed entry headers + ticket-spinlock reserve;
schema frozen in `RuntimeEmitter.cs:41-148`). Only four MM codes are produced
live: `mm_alloc`/`mm_free`/`mm_incref`/`mm_decref` (0x01–0x04) plus depth
inc/dec (0x40/41) around destructor cascades. The Sched subsystem (0x20–0x2C)
and several Dbg/raw codes are **dead** (decoder-only); real scheduler tracing
rides the Dbg events (0x50–0x5E). Type-name resolution is **automatic**: every
heap allocation flows through `EmitAlloc`→`EnsureTagIndex`, whose names land in
the PE `.symtab` `MXDS_TAGS` blob (`module.TagNames` →
`EmitAllMemoryManagerFunctions`) — so `maxon monitor` prints real names
(`String`, `__ManagedMemory`), no extra wiring needed.

**Consumer = `maxon monitor [--filter=mm] <exe>`** (`DebugStreamMonitor.cs`) —
creates the shared segment, spawns the child with `MAXON_DEBUGSTREAM` set, drains
the ring, decodes via a hand-rolled PE parser, prints
`[+SSSS.mmm] <indent>mm_<verb> <Tag> #<id> [size=|rc=]<n>` to stdout (summary to
stderr). It forwards the child's own stdout, so trace lines are identified by the
`[+…]` timestamp prefix.

**Two-tier gating (preserve in shv2's own producer at M6):** compile-time
`Compiler.DebugStream` (zero instructions when off) + runtime `MAXON_DEBUGSTREAM`
(`__ds_base==0`). Wart to NOT reproduce: MM events pay two real CALLs before the
runtime-off check, unlike Dbg events which inline the `__ds_base` guard at the
call site. shv2 should inline the guard for every event family.

**mm-trace spec harness (this repo's C# harness).** `<!-- MmTrace -->` +
`` ```mm-trace `` block ⇒ compile with `DebugStream=true`, run under
`maxon monitor --filter=mm` (subprocess), **normalize**, compare. Normalization
(deterministic goldens): keep only `[+…]`-prefixed lines, strip the timestamp
prefix + indent, dense-renumber `#<id>` in first-appearance order. Verified
byte-stable across runs for single-green-thread programs. `--max-procs 1`
(needed only for programs that spawn green threads) does **not exist yet** —
Foundation 1 dependency; the harness sets `MAXON_MAX_PROCS=1` defensively (no-op
until F1). mm-trace tests stay off the batched path (per-process ring buffer).

### Testing / spec-test harness (shv2's own)

**Landed (post-M3).** `maxon-shv2 spec-test [dir]` (default `specs-shv2`) is shv2's
self-hosting spec runner — `Compiler/Testing/{SpecParser,SpecTestRunner}.maxon`,
compiled by `maxon.exe` like the rest of shv2, so it can use the full stdlib
(File, String, `Subprocess`). It replaces the earlier hand-driving.

`SpecParser.parseSpecFile` extracts `<!-- test: NAME -->` markers + the ` ```maxon `
block + one expected block (` ```exitcode ` or ` ```maxoncstderr `) into
`SpecTest{name, source, expectation}` (`SpecExpectation` is a union, no sentinel).
It scans **only the `## Tests` section** (up to the next `## ` heading): deferred
tests live under a marker-less `## Deferred` section, because **HTML comments do
not nest** (`<!-- … <!-- test: … --> … -->` closes at the first `-->`), so a
comment-wrapped deferral would be run. `SpecTestRunner.runSpecDir` writes each
test's source verbatim (headerless, code at line 1) to a temp fragment, spawns
`<compiler> build` as a **subprocess** (isolates a compiler crash to one test;
exercises the real CLI) through the single `runProcess` choke point, and: for a
`compilerError` test, normalizes the fragment's absolute path to `<fragment>`
(line/col stay shv2-native — the headerless fragment is why they differ from v1's
`.test` files, which prepend a header line) and compares; for an `exitCode` test,
runs the produced exe and compares its exit. `Main` resolves the compiler via
`Process.executablePath()` (the runner tests itself), prints per-test PASS/FAIL +
`N passed, M failed`, and **exits non-zero iff any failed** (a real CI gate).
mm-trace fragments (runtime memory behavior) and Target-IR codegen fragments
(static generated code, via a coming `TargetPrinter` + `--emit-ir`) attach per
test at their milestones (mm-trace at M6, codegen fragments next).

### Control flow (M4a — comparison + `if`)

**Landed (M4a).** Comparison operators are a distinct `cmp` op (Maxon
`MaxonOp.compare`/`MaxonCmpOp`; Std `StdOp.cmp`/`StdCmpPred` at the arith-band end,
`isCmp: true`) — not a `binOp` opcode, because they need different metadata and
lower differently. `if`/`else`/`else-if` introduce **the first multi-block
functions**: `condBranch`/`branch` terminators (a new **`control` band** in `StdOp`
between arith and call), then/else/continuation `IrBlock`s laid out in source order,
and a continuation that is `Terminator.dead` when both arms return (emits nothing).
Because a function's blocks are contiguous in its one `FunctionCodeChunk`,
intra-function `jmp`/`jcc` are resolved **inside `emitFunctionChunk`** (not `concat`,
which only rebases cross-function `call`/IAT fixups): each block records a chunk-local
start offset; branches leave a zero rel32 + a `BlockJumpFixup`; `resolveBlockJumps`
patches them via the shared `patchChunkRel32` (forward AND backward — ready for M4b
loops). x64 is **fused `cmp`+`jcc`** (no `setcc`/bool materialization yet): `lowerCmp`
records `condId → pred` and `lowerCondBranch` emits `jcc` off it; `jcc`'s opcode is
`OpcodeJccRel32Base | X64CondCode.rawValue` (one table). **INVARIANT (enforced):** a
comparison is only valid as the *sole top-level operator of an `if` condition* — so
its `cmp` is guaranteed the last flag-setter before the branch. A comparison in value
position (`let b = x==10`) or chained (`x==10==1`) would read stale flags / not
materialize a result, so the parser rejects it (E3010) via a `permitComparison` flag
until bool materialization (`setcc`) lands. `var` reassignment, `while`/`break`/
`continue`, and boolean values are M4b.
