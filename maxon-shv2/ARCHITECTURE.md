# maxon-shv2 — Architecture (living document)

This is the onboarding document for `maxon-shv2`, the ground-up rewrite of the
Maxon self-hosted compiler. It describes **the compiler as it is today** — the
design of each subsystem, the invariants it holds, and the rationale where a
choice is not self-evident from the code.

It is **not a changelog** and **not a plan**:
- [`PLAN.md`](./PLAN.md) — the Minimal-Core plan: stage sequence and locked
  decisions (what we intend to build, in what order).
- [`DEVLOG.md`](./DEVLOG.md) — the dated record: milestone ledger, recon
  findings, bugs found along the way (what happened, and when).
- **This document** — how the thing works now, and why it is shaped that way.

When a subsystem changes, this document is **edited in place** to describe the new
design; the story of the change belongs in `DEVLOG.md`.

**Reading order:** Design pillars → Current state → Core invariants → whichever
subsystem section is relevant.

---

## Design pillars (why shv2 exists)

v1 (`maxon-selfhosted`) works, but its two *integral* features — static
ownership/borrowing and parallel incremental compilation — were retrofitted late
(≈8 shared `Project` sidetables + a 7,755-line refcount inserter), making it slow
and memory-hungry (5–6 GB). shv2 designs these in from the first commit:

1. **Static ownership/borrowing** — compile-time move/borrow checking that drops
   values deterministically at scope exit; runtime refcounting only where escape
   analysis proves genuine sharing.
2. **Parallel incremental compilation** — green-thread fan-out over per-file parse
   and per-function passes, with the multi-core runtime prerequisites proven
   *before* the compiler needs them.
3. **Binary event-log tracing** — DebugStream binary events to shared memory
   (near-zero overhead when off), decoded by `maxon-sharp` as the runner; powers
   `mm-trace` for ownership/memory debugging.

**Final acceptance:** `maxon-shv2.exe` compiling itself in **≤30 s**, **≤1.7 GB
RAM**, **>90% CPU** across all cores.

---

## Current state

**~16,400 lines. One target: `x64-windows`.** Anything else panics at three
gates (`allocatablePool`, `augmentWithRuntime`, `writeExecutable`).

**The language the compiler accepts today** — a scalar core with a real register
allocator under it:

- `function name(p1 T, p2 T) returns T … end` — **≤6 parameters** (the register-arg
  ABI cap), no defaults. Parameters are immutable value bindings.
- **Calls** — first argument positional, every later argument labelled
  (`f(a, b: x)`); call expressions and bare-call statements; recursion.
- `let` / `var` bindings, `var` reassignment, integer literals, identifiers.
- **Arithmetic** — `+ - * / mod` with Pratt precedence, prefix `-`.
- **Comparisons** — `== != < > <= >=`, valid *only* as the sole top-level operator
  of an `if`/`while` condition (there is no `setcc`/bool materialization yet, so a
  comparison in value position would read stale flags — the parser rejects it).
- **Control flow** — `if`/`else if`/`else`, `while` with labelled `break`/`continue`,
  `return`.
- Types: the `int`/`bool`/`float` keywords and the `ExitCode` builtin alias. No
  user types.

Everything outside that slice is rejected with a positioned diagnostic (the E-code
registry lives in `Compiler/Diagnostics.maxon`; `E3010` is the catch-all for
"unsupported construct").

**Not built yet:** heap/ownership/drops, structs, generics, interfaces,
strings/arrays/maps, floats in codegen, error handling, the runtime shv2 must
*emit* (Workstream R), the parallel compilation driver, arm64/wasm. See `PLAN.md`.

**The gate battery** (all four must be green before a commit):
| Gate | What it proves |
|---|---|
| `maxon-shv2 spec-test` | **75 passing / 0 failing** over `specs-shv2/*.md` — the functional suite |
| `AllocChecker` | Every function of every `spec-test` / `verify-warm-rebuild` compile is symbolically verified for register correctness (a failure panics the build). Opt-out per spec/test; on a plain `build` it is opt-**in** via `--check-alloc` |
| `maxon-shv2 verify-warm-rebuild <file>` | Compile determinism (byte-identical) + query-spine incrementality (content-hash cache hit) |
| `specs-shv2/fragments/x64-windows/**` | Committed Target-IR goldens — every codegen change shows up in `git diff` |

---

## Core invariants

The load-bearing rules. Each is documented in full in its subsystem section; this
is the index.

- **Three tiers, flat op unions** — `Maxon → Std → Target`. There is NO MIR tier:
  Std's `ValueId`s already *are* the virtual registers. Every dialect's op union is
  FLAT with a required `*OpMeta` struct backing, never nested — Maxon boxes each
  payload-carrying union case, so nesting costs a heap object per level on the
  compiler's most numerous object. *(→ Std dialect)*
- **Band-append** — op variants sit in category-contiguous bands so a pass can cover
  a category with one `match` range arm. **A new variant is appended at the END of
  its band, never inserted into the middle** — a range arm silently swallows anything
  inserted between its endpoints, with no missing-case error. This is the ONE place
  "no silent unhandled cases" has no compiler backstop; reviewers enforce it.
  *(→ Std dialect)*
- **No sugar reaches the backend** — `sugar`-category `StdOp`s must be eliminated
  before `buildBackend`, which gates on `assertNoSugarOps`. *(→ Std dialect)*
- **Live ops ≠ `module.ops`** — `IrModule.ops` is append-only; block-rebuilding passes
  leave the ops they replaced behind as orphans. Any pass asking "what ops are actually
  here?" goes through `IrModule.liveOpIndices(func)`, the single home of the blockRefs →
  opRefs+terminator walk. Scanning the flat array sees dead ops from every prior pass.
  *(→ Std dialect)*
- **Source spans live in a table, not on the op** — `SourceRangeTable`'s four dense
  scalar columns, appended in lockstep with ops through a single choke point.
  *(→ Maxon dialect)*
- **The parser writes only into a `FileParseArtifact`** — never into `Project`.
  `mergeArtifact` is the single writer of every shared registry. A file's parse is a
  pure function of `(tokens, filePath, namespace)`, which is what makes the per-file
  fan-out possible with no further parser change. *(→ Frontend)*
- **Virtual registers ARE Std `ValueId`s** — there is no second vreg numbering anywhere
  in shv2. *(→ Register allocator)*
- **Coloring rewrites ops in place** — at the SAME `module.ops` index. `block.opRefs`
  entries and `terminatorIndex` are indices into that array; a pass that appended or
  compacted instead would invalidate every reference in the function.
  *(→ Register allocator)*
- **The `AllocChecker` runs on every function of every `spec-test` compile** (and of
  `verify-warm-rebuild`), because spec fragments are *outputs*, not gates: the suite
  would go green on a wrong-but-self-consistent allocator. It is an **opt-out** there
  (`checkAlloc: false` per spec / per test), never an opt-in. A plain `build` skips it
  — it is a pure verification pass and cannot change an emitted byte — and re-arms it
  with `--check-alloc`. *(→ Register allocator)*
- **Ownership-kind lattice** — `trivial · owned · borrow · shared`, with three
  first-class homes and zero sidetables. Declared, inert until the ownership stage.
  *(→ Own tier)*

---

## Driver and CLI

`Main.maxon` → `Compiler.compile`. Three commands:

| Command | Purpose |
|---|---|
| `maxon-shv2 build <file\|dir> [-o out] [--emit-ir] [--check-alloc]` | Compile to a PE. A single-file build writes next to the source (`basic.maxon` → `basic.exe`). `--emit-ir` also prints the Target module to `<output>.ir`. `--check-alloc` runs the `AllocChecker` (off by default; a pure verification pass worth ~10% of compile time — it cannot change the emitted bytes). |
| `maxon-shv2 spec-test [dir]` | Run the spec suite (default `specs-shv2`) and regenerate fragments. |
| `maxon-shv2 verify-warm-rebuild <file>` | The determinism + incrementality gate. |

`--log=<category>:<level>` enables the `Logger` categories (`codegen` carries the
register-allocator stats).

`Compiler.compile` builds a `Project`, runs the pass pipeline, then goes
**backend-direct**: `buildBackend(stdModule, target, globalData)` → `writeExecutable`.
There is no second "backend pipeline" — a `CodeResult` is not an IR module.

Diagnostics are printed by `compile` on the failure path, and the error is re-signaled
as a **fresh** `compileError`. (Re-throwing the *caught* error NULL-increfs under the
C#-emitted refcount runtime; a fresh throw of the same arm is clean. A driver-shape
gotcha worth remembering.)

## Query spine (incremental)

`QueryDatabase` / `QueryEngine` / `Queries` implement
`querySourceFile → queryTokens → queryParseOps → queryAllModule`.

**Memo validity is a content-hash compare, not a revision-counter walk.**
`fileChanged` computes an FNV-1a `ContentHash` per file — there is no shared
`currentRevision` counter for parallel queries to contend on. Each per-file memo
stamps the `keyHash` it was derived from and is valid iff that still equals the file's
current hash; `queryAllModule` keys its merged-module memo on the **composite** hash
folded over every file's hash in source-path order.

`queryParseOps` produces a `FileParseArtifact` and touches no `Project` state (the
parser is pure). `queryAllModule` on a miss calls `resetMergeTargets` and re-folds the
artifacts through `mergeArtifact` — so there is **no rollback machinery**: artifacts are
the source of truth, and the derived registries are rebuilt from them.

Failure is never cached (a failed tokenize/parse, or a parse that merely reported a
diagnostic, returns an empty result uncached), so a fixed file recompiles cleanly.

The dependency graph (`recordDependency` / `clearDepsFor` / `activeQueryStack`) is
**recorded but does not yet drive invalidation** — content hashes do. It is maintained
from day one so that when a query genuinely depends on another file's *derived* state,
the wiring is already there.

`verify-warm-rebuild` (`Compiler/VerifyWarmRebuild.maxon`) is the standing gate over
this spine, asserting two properties and exiting 0 iff both hold:
1. **Determinism** — compile the file to a `CodeResult` twice, each with its OWN fresh
   `Project`/`QueryDatabase`, and byte-compare all emitted sections.
2. **Incrementality** — in one `Project`, `queryAllModule` twice (cold miss, then hit)
   plus `probeRebuildCacheHits` re-querying each file's tokens/parse, asserting via the
   DB hit/miss counters that the rebuild is a content-hash **hit**.

It sidesteps a real trap: `resolveTypes` mutates the merged module in place and
`queryAllModule` hands back that same memoized object — so the determinism check must
use independent projects, and the cache check must run only `queryAllModule` (never the
mutating pipeline) between its probes. `build` and the gate share one
`compileToCodeResult(project)`; a determinism gate over a divergent path would prove
nothing.

## Frontend (lexer, parser, parse-staging)

`Compiler/Lexer.maxon` is a table-driven DFA: `tokenize(source) -> TokenArray throws
LexerError`. It already lexes far more than the parser accepts (float/string/char
literals, `and`/`or`/`not`, `as`, all the declaration keywords) — the parser rejects
what it cannot yet handle, so the lexer needs no gating.

`Compiler/Parser.maxon` is a recursive-descent parser with a **Pratt precedence climber**
for expressions. `parseBinary(minPrec)` parses an operand, then while the next token is a
supported infix operator with `precedence >= minPrec`, consumes it and recurses the right
operand at `prec + 1` — the `+1` is what makes it left-associative. `parseUnary` is the
climber's leaf: a prefix `-` consumes and then parses a PRIMARY, not another unary, so
unary binds tighter than every binary operator but does not chain (`- -x` is `E2004`).
Precedence: comparison < additive < multiplicative.

Two structural properties define the parser's shape:

**1. Value numbering is born here.** Every op's result gets its dense function-local
`ValueId` at emit time, not at the Maxon→Std boundary. Parameters are pre-bound to the
reserved ids `0..paramCount-1`.

**2. It takes no `Project`.** The parser writes exclusively into a per-file
`FileParseArtifact` — the file's own `MaxonModule` fragment, its own `SourceRangeTable`,
its own LOCAL `TypeNameInterner`, and ordered contribution arrays for each shared
registry. A file's parse is a pure function of `(tokens, filePath, namespace)` with zero
shared state, so per-file fan-out needs no further parser change. A rejected construct
becomes a positioned `ParseError` that `queryParseOps` turns into a project diagnostic
(rendered `error E<code>: <file>:<line>:<col>: <msg>`) — the parser itself never touches
`Project` or `project.diagnostics`.

### Names, SSA, and phis — all at parse time

There are **no stack slots and no `mem2reg`**. Name resolution and SSA construction both
happen in the parser:

- `let`/`var` bind a name to the initializer's SSA `ValueId` in `Scope`
  (`declareValueBinding`); a reference is a `Scope.lookupValue` resolved AT PARSE TIME.
  So `let x = 42; return x` mints no `x` op at all and lowers identically to `return 42`.
- A `var` **reassignment** rebinds the variable's current SSA `ValueId` via
  `Scope.setValue` — no slot, no `store`/`load` (the Std memory band is still empty).
- **Phis are `IrBlock.blockArgs` + `branchEdges`**, minted eagerly by the parser at the
  three merge points it structurally knows: **loop headers** (one per mutable var in
  scope), **loop exits** that a `break` reaches, and **`if` continuations** (merging the
  then/else values). Every predecessor records the value it carries per phi on its
  `branchEdge`, so operands are known at mint time — a single forward pass, no deferred
  backpatch.

*Rationale:* the alternative was porting v1's IDF `Mem2Reg` (2,327 lines), which needs
`alloca`/`store`/`load`, a dominance-frontier module, and a dominator-tree rename pass —
none of which shv2 has. The parser already has full structural knowledge of a *structured*
CFG and lands phis at the **same blocks** v1's IDF would. v1's mem2reg becomes the port
target if and when shv2 grows unstructured control flow.

Loop targets come from a **loop-context stack** on the parser (`self.loops`), pushed
around each `while` body: `LoopContext{label, headerId, exitId, phiVars, hasBreak}`.
`resolveControlTarget` returns the innermost loop or searches by label — `E2047` (no
enclosing/matching loop), `E2048` (label names the loop's own header).

### Scope

`Scope` today tracks exactly what the parser uses: value bindings (name → current SSA
`ValueId` + mutability), via `declareValueBinding` / `setValue` / `lookupValue`.

It also carries a **block-scoping and ownership scaffold** — `frameStack`, `ownedStack`,
`pushScope`/`popScope` (which panic on unmatched pop, since they must stay parallel), and
a `recordInCurrentFrame` that records a binding into `ownedStack` only when it is
non-trivial. **That scaffold is currently unreachable**: nothing calls `pushScope`, so
there is no lexical block scoping (a `let` inside an `if` body outlives the `if`), and
`ownedStack` is never touched. It is wired up when block scoping lands, and drained to
emit `own.drop`/`own.release` at the ownership stage.

### Parse-staging

`Compiler/ParseStaging.maxon` owns `FileParseArtifact` + `mergeArtifact(project, target,
artifact)`, **the single writer** of the shared `Project` registries. `mergeArtifact`:

1. folds the artifact's local interner into `project.typeNames` (`TypeNameInterner.foldInto`
   → a `TypeNameRemap`), and if the fold moved ids, `remapArtifact` rewrites the artifact's
   `named(id)` references;
2. offset-merges the `MaxonModule` fragment into the accumulator (`IrModule.merge`);
3. appends the artifact's `SourceRangeTable` in lockstep (asserting the op counts agree);
4. commits each registry contribution — `funcReturnTypes` (upsert) and `funcSignatures`
   (which carries the **whole-program duplicate-function check**, `E2001`).

Because the parser wrote nothing to `Project` speculatively, there is no ParseDelta
rollback dance. Each new registry family arrives the same way: an ordered contribution
array on the artifact, folded here.

**Interner note:** for a *cached* artifact, a non-identity remap must clone before
rewriting — it must not mutate the cache in place.

The `Project` registry set today is small: `funcReturnTypes`, `funcSignatures`
(param names + types), `typeNames`, `opRanges`, plus `diagnostics`, `globalData`, `db`,
`rootPath`, `target`. It grows toward v1's ~28 as the language does.

## Maxon dialect

`IR/Maxon/MaxonDialect.maxon` — the surface-level IR the parser emits.

**`MaxonOp`** (in band order; `OpCategory` bands `callFree` … `plain`, with
`callMethod`/`panicking`/`varAccess`/`awaiting`/`closureProducer`/`ownership` declared but
empty):

| Band | Variants |
|---|---|
| `callFree` | `call(result, callee, args, argLabels, argRanges)` |
| `plain` | `literal`, `ret`, `retVoid`, `binOp`, `unaryOp`, `compare`, `condBranch`, `branch` |

Every variant is tagged `= MaxonOpMeta{category}` — a new op cannot be added without
declaring its `OpCategory` — and obeys the band-append invariant.

**Value operands are `ValueId`s, minted by the parser.** Identifier TEXT that is *not* a
value — a callee name, a field name, a method name — stays a `ByteArray`. The split is "is
this operand a VALUE or a NAME?", roughly 90/10 in favour of values.

*Rationale:* v1 named Maxon values with `ByteArray`s and let `lowerMaxonToStd` assign the
"real" ids. But v1's result names were themselves synthetic `$tN` strings off a per-function
counter, and the ids were a *second* per-function counter — so the mapping was a **bijection
between two dense counters**, bought with a heap `String` + heap `ByteArray` per value and a
byte-sequence-keyed hash map (O(len) compares; one insert + one lookup per value). It
constructed no SSA. Here, `lowerMaxonToStd` passes ids through verbatim and there is no
`nameToId`/`defineName`/`resolveName` at all.

**`retVoid` is a distinct variant**, not a `ret` carrying an absent-value sentinel:
`ValueId` has no empty value, and sentinels are forbidden.

**Source spans are NOT a field on the op.** Every Maxon op has one, but it lives in
`SourceRangeTable` — an op-parallel store of **four dense scalar columns**
(`startBytes`/`endBytes`/`lines`/`columns`), keyed by op index.

*Rationale:* `SourceRange` is a `type`, so an inline `range` field would be a POINTER to a
heap-boxed SourceRange — one live heap object per op, on the compiler's most numerous
object, retained for the module's lifetime. (An `Array with SourceRange` would NOT fix
this; it is an array of pointers to the same boxes. The scalar columns are the point.) A
`SourceRange` is materialized only by `SourceRangeTable.get`, i.e. only when a diagnostic
or an LSP query asks — the cold path. Parsing and lowering never allocate one.

- **It is a MAXON-TIER table.** It lives on `FileParseArtifact` (per file) and `Project`
  (whole-program, folded by `mergeArtifact` in lockstep with `IrModule.merge`). Std and
  Target carry no spans — the same reason `StdOp` has no `range` field, and why the
  post-inline peak module will pay nothing for them.
- **Lockstep invariant.** Ops and spans are appended together by the single choke point
  `FileParseArtifact.emitOp`/`emitTerminator` — the only way a Maxon op is created.
  `SourceRangeTable.record` asserts the op index it is handed equals the table's own count:
  if anything ever appended a Maxon op behind the choke point's back, the next emit panics
  instead of silently shifting every later span by one.
- Per-ARGUMENT spans (`argRanges` on `call`) are genuine per-arg payload, not per-op, so
  they stay inline on the op.

**`MaxonType`** — `boolean | integer | float | named(TypeNameId) | exitCode | unresolved`.
`named` is the parser's interned type reference. `exitCode` is the RESOLVED form of the
`ExitCode` builtin alias — a width-FREE tag like `boolean`/`integer`; its unsigned-32-bit
width is assigned only at the Maxon→Std boundary (`maxonTypeToStdType` → `StdType.u32`), so
the invariant "MaxonType carries no width; width collapse happens only in lowering" holds.
Void is not a MaxonType — `MaxonReturnType{void, value}` carries return slots.

`MaxonType` stays a **boxed union**: packing it into an i64 would forfeit compiler-checked
payload extraction, and the static guarantee is worth more than the allocations.

**`OwnershipKind`** — `trivial | owned | borrow | shared`. Three homes, zero sidetables:
(1) the attribute (`VarInfo.ownership`), (2) signature ownership modes in the function type,
(3) explicit `own.*` ops. `isTrivialOwnership` is the shared classifier. Present but inert:
every binding is `trivial` today.

## Type resolution & semantic check (Maxon tier)

`TypeResolution.resolveTypes(project, module)` replaces every `named`/`unresolved`
`MaxonType` in the module's function signatures with a concrete one, and re-syncs each
resolved signature/return type into `project.funcSignatures`/`funcReturnTypes` (SemanticCheck's
source of truth). The **only** named type it resolves is the `ExitCode` builtin alias,
recognized by name without loading a stdlib; any other named type panics loudly. The
typealias registry and user types arrive with the struct/generics stages.

`SemanticCheck.semanticCheck(project)` runs the whole-program checks:
- **E3001** — no `main`; **E3002** — `main` does not return `ExitCode`.
- **Call validation** against `funcSignatures` — **E3030** unknown function, **E3031** arity,
  **E3032** unknown argument label, **E3033** duplicate argument — via the shared
  `slotCallArgs`, which is the ONE label→position mapping (lowering uses the same function,
  so a call cannot be validated against one slotting and lowered against another).

Both append a `Diagnostic`; the pipeline's error gate then bails.

## Std dialect

`IR/Std/StdDialect.maxon` is the mid-level tier — and, since there is no MIR tier, also the
**machine-level** tier. Two invariants define its shape, and both are load-bearing.

**1. There is NO MIR tier.** Tiers are `Maxon → Std → Target`.

*Rationale:* v1's `lowerStdToMir` was ~90% a mechanical 1:1 rename. Std's `ValueId`s
**already are** the infinite virtual registers MIR claimed to introduce, so the tier bought
no new value model — only a whole extra module copy in RSS and a wall that hid dead code
(v1's `MirOp.movReg` has zero construction sites yet still forces match arms in five
places). Everything v1 ran *on* MIR (`commuteForCoalescing`, `scheduleInstructions`) is a
Std-tier pass here, and MIR's one piece of real content — desugaring — is a **Std→Std**
pass, `lowerToMachineForm`.

**2. `StdOp` is FLAT** — one union, no `StdOp.arith(StdArithOp.const(StdArithConst))`
nesting.

*Rationale:* Maxon heap-boxes and refcounts every payload-carrying union case, so each
nesting level is another heap object: nested = **3 boxes per op**, flat = **1**, on the most
numerous object in the whole compiler.

Coarse membership — "is this any kind of call?" — is recovered without nesting from the
`StdOpMeta` struct **every variant is required to carry**: `category` (`StdOpCategory`),
`role` (`OpRole`: plain/ret/errorReturn/param), the two inliner axes (`isPure`,
`isUnsupportedInInlineBody`), and the scheduler facts (`isMemory`/`isStore`/`isCall`/
`clobbersFlags`/`isCmp`). A new variant cannot be added without declaring all of it.

**Purity is declared PER VARIANT, and that is the flat union's real payoff.** v1 could only
answer "is this op side-effect-free?" by CATEGORY, at the match site — and a category blanket
has no way to say a *trapping* op is impure, so v1's `arith gives true` called integer `div`
pure even though it faults on divide-by-zero. Here `div`/`mod` simply declare `isPure: false`
and nothing else changes. (`clobbersFlags` is a lowering artifact, not a side effect: a
flag-clobbering op is still `isPure`.)

**The current variant set**, in band order:

| Band | Variants |
|---|---|
| `arith` | `const`, `binOp(opcode)`, `unaryOp(opcode)`, `cmp(pred)`, `binOpImm(imm)`, `cmpImm(imm)`, `div`, `mod` |
| `control` | `condBranch`, `branch` |
| `call` | `ret` (role `ret`), `param` (role `param`), `call` |
| `memory` · `system` · `sugar` | *(empty)* |

Flat does **not** mean one variant per opcode: a family sharing an operand shape
(`add`/`sub`/`mul`) stays one variant with an opcode field, but a member needing *different
metadata* splits out — because the `StdOpMeta` backing attaches per variant and cannot reach
an opcode buried in a field. That is why `cmp` is its own variant (`isCmp: true`) and
`div`/`mod` are their own (`isPure: false` — `idiv` traps — plus fixed-register lowering).
`const` carries a `StdType` rather than v1's `constI64`/`constF64` opcode pair: it subsumes
MIR's `isFloat` bool and makes i32/u8/f32 representable without new opcodes.

### Bands, and the one rule with no compiler backstop

Variants are declared in **category-contiguous bands** in `StdOpCategory` order, so a pass
that treats a whole category uniformly covers it with ONE `match` range arm instead of one
arm per variant. The cost: a range arm names its endpoints, so it **silently swallows
anything inserted between them** — no missing-case error, and the new op is misclassified at
every range-arm site at once.

> **INVARIANT: append a new variant at the END of its band. Never insert into the middle of
> one.** This is the single place the project's "no silent unhandled cases" rule is a
> convention rather than a compiler-enforced property. Reviewers must enforce it by hand.

(Appending at the *union* end also makes stale `… to iatCall` range arms fail to compile
until extended — the invariant working in the reviewer's favour.)

### The sugar band

v1 enforced "no sugar reaches the backend" with a *type*: sugar ops lived in `StdOp` and had
no `MirOp` counterpart, so the tier boundary made them unrepresentable downstream. Collapsing
the tier gives that up; the replacement is deliberate:

1. `assertNoSugarOps(module)` — a whole-module gate run by **`buildBackend`**, the single
   target-neutral backend entry. It is NOT inside the x64 lowering: sugar is a property of
   the Std tier, not of x64, so putting it at the one shared entry means arm64/wasm inherit
   it instead of each having to remember to re-add it.
2. An explicit `panic("… must be desugared by lowerToMachineForm")` match arm per sugar
   variant — never a bare `default`, which is exactly what would swallow a new case.

**Spec tests are the real guarantee.** Both guards are **vacuous today** — the sugar band is
empty, and `lowerToMachineForm` is therefore not written. It arrives with the first sugar op
(drops/frees at the ownership stage; layout descriptors and witness tables at generics).

`assertNoSugarOps` walks **live** ops via `IrModule.liveOpIndices(func)`, never the flat
`module.ops` array — that array is append-only, and a block-rebuilding pass (which
`lowerToMachineForm` will be) leaves the ops it replaced behind as orphans. Scanning the flat
array would panic on dead entries and report a desugaring failure that never happened.
`liveOpIndices` is the single home of the funcs → blockRefs → blocks → opRefs+terminator
descent.

## Pass pipeline

`PassPipeline` owns the two tier modules it drives (`maxonModule` → `stdModule`), a `project`
handle, and the scheduled `PassKind` list. It owns NO target module — the pipeline's last tier
is Std, and the backend is backend-direct.

The default pipeline, in order:

| Pass | Tier | What it does |
|---|---|---|
| `resolveTypes` | Maxon | named/unresolved types → concrete; re-sync the registries |
| `semanticCheck` | Maxon | E3001/E3002 + call validation |
| `lowerMaxonToStd` | Maxon → Std | width/ABI collapse; 1:N desugaring; `blockArgs`/`branchEdges` carried verbatim |
| `pruneDeadBlockArgs` | Std → Std | delete the loop-header phis nothing reads |
| `foldConstOperands` | Std → Std | constants into immediate operand forms |

`run()` enforces the **error gate**: after each pass, `projectHasErrors → throw CompileError` —
so a program that fails `semanticCheck` never reaches lowering or the backend.

`classifyPass` labels each pass `wholeModule` or `perFunction`. The classification is recorded
but not yet acted on; it is what the per-function fan-out driver will read.

**Everything grows in HERE**, appended after `lowerMaxonToStd`: the Std optimization passes,
the Own-tier passes, and `lowerToMachineForm`. There is no second pipeline over a machine tier,
because there is no machine tier.

### `pruneDeadBlockArgs` (a register-allocator obligation, not an optimization)

**The front end over-produces phis, and the surplus is what a FALSE `E5001` was made of.**
On-the-fly SSA must mint a loop header's phis *before* it parses the body — the set of vars the
body writes is not yet known — so `Parser.parseWhileStatement` mints one per mutable var **in
scope**. Every `var` declared before a loop gets a loop-carried phi, including vars the loop
never touches and vars that are already dead when the loop is reached.

A phi for a var the loop never reads is not merely useless, it is **self-sustaining**: the back
edge passes it to itself, so it *has* a use, and liveness holds it live around the **entire
loop**. Two sequential loops are enough — every accumulator of the first loop reappears as a
dead phi in the second loop's header. Then `maxlive` inflates by one per dead var, the splitter
**forced-spills them around the second loop's call** (a store *and* a reload every iteration,
for values nothing reads), and past the pool of 14 the compiler raises `E5001` against a program
that fits the machine comfortably — ranking the dead phis first among the values to delete,
described as "used 0 times in the loop". Which they were. They were used nowhere.

**Usefulness is a LEAST FIXPOINT, not a use count.** A block-arg is useful iff (a) an *op* reads
it, or (b) it is what some edge passes to a *useful* block-arg. A self-sustaining dead phi
satisfies neither. Asking the question `foldConstOperands`'s const DCE asks — "is this value
referenced anywhere, edges included?" — keeps every one of them.

It never rewrites a value (a dead block-arg has no reader left to rewrite); it drops
`blockArgs[k]` together with slot `k` of every incoming edge, so the positional alignment the
phi model rests on is preserved by construction. It runs **before** `foldConstOperands`, whose
const DCE then collects the `const` ops whose only reader was a dead phi's edge.

A merely **redundant** phi — a var the loop reads but never writes, so the header phi carries a
value that never changes — is deliberately left alone. It is not dead, and it costs nothing:
biased coloring coalesces it with its incoming value into one register and SSA destruction drops
the resulting self-move.

### `foldConstOperands` (the one Std optimization pass today)

Collects constant defs, then rewrites a `binOp`/`cmp` with a constant operand into
`binOpImm`/`cmpImm`, then DCEs the now-unreferenced `const` ops. It canonicalizes commutative
ops so the constant lands on the rhs, and flips the predicate for a const-lhs comparison; a
const-lhs `sub` is deliberately left alone (there is no `imm - reg` form). Range-gated to the
i32 immediate range, with `i32.min` excluded only for the `sub`→negated-`lea` path, where
negating it would overflow. `div`/`mod` are never folded (no immediate `idiv`).

The result is that literals never occupy a register.

Constant *folding* proper (`const ⊕ const` → `const`) is deliberately **not** implemented: it
would collapse the test programs to `mov r8, k` and erase the codegen the spec fragments exist
to show. It lands when there is enough real code to justify it.

## Own tier (ownership infer / check / escape / drops)

*Not built. One structural decision is PINNED now, because getting it wrong later is a rewrite
and writing it down today is free:*

**`own.*` ops are a BAND OF `MaxonOp` — not a separate dialect, not a tier.** "Own tier" names
a *pass group* (`OwnershipInfer` → `OwnershipCheck` → `EscapeAnalysis` → `InsertDrops`), all of
which run over the Maxon module. Tiers are `Maxon → Std → Target`, full stop.

This follows from the container. `IrModule uses Op` is generic over **exactly one** op type, so
an op living in the Maxon block stream **is a `MaxonOp`**. There is no `OwnModule` and no
`lowerMaxonToOwn`. `IR/Own/OwnDialect.maxon` is a FILE — a place to group the `own.*` variants,
their `OpCategory.ownership` band, and the lifetime ids — not a dialect in the tier sense.

> **Do NOT introduce `union OwnOp { move, borrow, drop, retain, release }` nested as
> `MaxonOp.own(OwnOp)`.** Maxon heap-boxes every payload-carrying union case, so nesting costs a
> **second heap object per op** — the exact anti-pattern the flat-union rule exists to prevent,
> on the ops the Own tier touches most. The `own.*` variants go directly into `MaxonOp`,
> appended at the end of the `ownership` band, exactly like every other variant.

`OpCategory.ownership` already exists in `MaxonDialect.maxon` for this band.

## Backend (Std → Target → PE)

`Std → Target` is the **only** tier boundary below Std. `buildBackend`
(`Targets/BackendDispatch.maxon`) is the target-neutral entry, and its order is:

```
assertNoSugarOps(stdModule)
lowerStdToX64(stdModule)          → TargetModule (virtual registers)
allocateRegisters(module, target)                 → physical registers
insertPrologueEpilogue(module, target)            → frames
augmentWithRuntime(module, target)                → prepend mrt_start
emitFunctionChunk(...) per function               → machine code
concatX64FunctionChunks(...)      → CodeResult
```

**That order is forced.** Register allocation runs **before** prologue/epilogue because *the
frame is a function of the allocation* (which callee-saved registers were used, how many spill
slots). It runs **before** `augmentWithRuntime` because the `mrt_start` entry stub is
hand-built in physical registers with its own frame — it must not be re-processed.

`lowerStdToX64` (`Targets/X64/StdToX64Conversion.maxon`) clones the block skeleton 1:1 and
walks each function lowering `StdOp`s to `TargetOp`s with still-virtual registers. The virtual
registers it emits are Std's own `ValueId`s, unchanged since `lowerMaxonToStd` minted them.

**Instruction selection quality** (what the ISel produces today):
- **3-operand `lea`** for `a + b` — no reuse, no copy, no flags. `sum = sum + i` in a loop is
  *one* instruction.
- **`lea` with disp32** for `a ± imm` (a subtraction becomes a negative displacement).
- **Immediate forms** — 3-operand `imul r, r/m, imm32`, `cmp r, imm32`, and a size-choosing
  `mov` imm32/imm64 — so `foldConstOperands`'s immediates never occupy a register.
- **Reuse defs** for `sub`/`imul`/`neg`, which is how the two-address seed `mov` is *not*
  emitted (see the register-allocator section).

> **x64 encoding trap, centralized in `emitBaseDispModRm`.** In a SIB byte, `mod=00` with base
> low-3-bits `= 101` means *"disp32, no base register"* — so `lea dest, [base + index]` with base in
> **RBP or R13** must be emitted as `mod=01, disp8=0`. R13 is in the allocatable pool, so this fires
> in real code. Likewise index `= 100` means *"no index"*, so RSP can never be an index — asserted,
> not trusted. This is exactly the class of silent miscompile v1 shipped.

**Intra-function branches are resolved inside `emitFunctionChunk`**, not in `concat` (which
only rebases cross-function `call`/IAT fixups): a function's blocks are contiguous in its one
`FunctionCodeChunk`, so each block records a chunk-local start offset, a branch leaves a zero
rel32 + a `BlockJumpFixup`, and `resolveBlockJumps` patches them through the shared
`patchChunkRel32` — forward and backward alike, which is why loop back-edges needed no backend
work.

**Comparisons are fused `cmp`+`jcc`** — there is no `setcc` and no bool materialization.
`lowerCmp` records `condId → pred` and `lowerCondBranch` emits the `jcc` off it; the `jcc`
opcode is `OpcodeJccRel32Base | X64CondCode.rawValue` (one table). This is the machinery the
parser's "a comparison is only valid as the sole top-level operator of an `if`/`while`
condition" restriction protects: it guarantees the `cmp` is the last flag-setter before the
branch.

`X64PrologueEpilogue` pushes and pops **exactly** the callee-saved registers the coloring
actually used, and reserves an aligned frame (32-byte shadow space + spill slots + parity
padding, so `rsp ≡ 0 mod 16` at a call).

`CodeResult.stdModule` carries the mid-level module the backend lowered FROM. x64/PE ignore
it; it exists because the **wasm** backend will consume the machine-level IR directly instead
of going through a target dialect — and with the MIR tier gone, Std *is* that IR.

`GlobalDataTable` (`.rdata`) is threaded through the backend but **empty today** — there are no
strings or floats yet. When it fills, the rule is: the backend captures rdata constants
chunk-locally and merges them into the shared table **single-threaded in function order**
(idempotent-by-label dedup), with content-derived keys for all other shared appends. That is
what keeps a parallel backend byte-deterministic.

## Register allocator (Std `ValueId`s → physical GPRs)

**SSA chordal coloring + cold-spill live-range splitting, with a hard error (`E5001`) where a
spill would be hot.** This is the most designed part of the compiler, and the section is long
because every piece of it follows from one contract.

**What it colors is Std's own value space:** an `X64VReg` is either `physical(reg)` or
`virtual(id)`, and that `id` **is the `ValueId` `lowerMaxonToStd` minted**. There is no separate
virtual-register numbering anywhere in shv2.

### The contract: refuse the SEARCH, not the SPILL

**`E5001` exists to keep the allocator out of the search business.** What is expensive — in
compile time, in determinism, in explainability — is not emitting a spill. It is *deciding* one:
the eviction tournament, the spill-cost model, the iterated re-splitting, the backtracking. That
machinery is what made v1's allocator **74% of self-compile**, and it is what makes a failure
unactionable ("my search gave up") instead of a theorem ("your loop needs 17 registers and has
14").

So the line is **not** hot-vs-cold. It is **forced-vs-searched**:

- **If the placement is forced, emit it.** Deciding it costs nothing, so there is nothing to
  refuse.
- **If relieving the pressure would require a search, raise `E5001`.** That is the machinery we
  decline to build.

That yields exactly three cases:

**1. A value idle across a pressured region → split it (free).** It lives in memory across the gap
and in a register where it is used. Around a loop it does not touch: a store before, a reload
after, **nothing added to the loop body**. One placement, no choice.

**2. A value live across a fixed-register point → bracket it (cheap).** A call clobbers all 9
caller-saved registers, so a value that must survive it has exactly two homes: one of the **5
callee-saved** registers, or the stack. Past the fifth, the ABI has already made the decision —
memory is the *only* home. The allocator stores before the call and reloads after, **including
inside a loop**, because that placement is *forced by the ABI, not chosen by a search*. The pair
is cheap against the op it brackets: one store and one load against a call that costs far more.
(An `idiv` is the same case in miniature, reserving `RAX`/`RDX`.)

> **This must never be `E5001`.** Such a program *fits the machine* — the values are excluded only
> from the registers one op happens to clobber. Refusing it would be a false positive, and the
> restructuring it would demand (hoist the loop's values into an array) puts **every** value in
> memory and reads *and writes* each one at **every use** — strictly worse code than the single
> bracket the compiler declined to emit. The remedy would be worse than the disease, and the
> "restructuring beats the spiller" premise simply does not hold here: there is no data-structure
> win to capture, only the same store and load, hand-written.
>
> This is also what keeps [Rule 3](#rule-3) true as the language grows. A dictionary-passing layout
> descriptor inside a generic body (M14) is a hidden *parameter* — not rematerializable, invisible
> in the author's source. Under a hot-spill ban it could land in a blocking set, which Rule 3 calls
> unrecoverable. Under a forced bracket it simply spills around the call and blocks nothing.

**3. A value the loop genuinely USES, when the working set exceeds the whole pool → `E5001`.**
Here there is nothing to bracket. Spilling any of them puts a reload at *every use*, every
iteration, and *choosing which to sacrifice is precisely the search this allocator refuses to
run*. The loop's working set is simply larger than the machine. No spiller can fix that — only a
restructuring the author can do, and that one **is** a real data-structure change: twenty
accumulators become one array, and the pressure collapses to `{base, index, temp}`.

- **One shot.** liveness → split → color. There is no reactive spill/color iteration.

**The deficit is measured against the FULL pool of 14, never against a reduced one.** A reduced
pool (the 5 callee-saved at a call) is a *clobber constraint*, not a capacity limit, and case 2
dispatches it without an error. Reporting "6 live, only 5 survive a call" as a deficit was the
false-positive class: it made an ordinary loop with five accumulators and a call — the shape of
almost all real code, including this compiler's own inner loops — a compile error.

**And the author is expected to be an AI agent**, which is what makes erroring-instead-of-degrading
the right trade rather than a hostile one: the cost of an `E5001` is one compile round-trip. It
also imposes two requirements a human-facing compiler could ignore:

- **The error must be deterministic — a property of the program, not of a search.** An agent loop
  needs `same program → same error` or it cannot converge. "Your loop needs 17 registers and has
  14" is a *theorem*, stable across compiler versions; "my evict/split search gave up" can flip
  when a heuristic is tuned, and a rewrite loop chasing it may never terminate.
- **The error must be actionable in ONE step, not by bisection** — the exact deficit, the ranked
  candidates, and the named transformation.

Three consequences, and they are the reason several things elsewhere in the compiler exist:

1. **A false `E5001` is the worst bug this compiler can have.** It sends an author to restructure
   code that was fine, and can break an agent's convergence loop. Trust in the diagnostic is the
   whole product.
2. **The compiler may therefore never leave a surplus value in the IR, because a surplus value
   *is* a false positive.** This is what promotes 3-operand `lea`/`imul`, immediate operands,
   `foldConstOperands`, `pruneDeadBlockArgs`, rematerialization, and copy-free ISel from
   "optimizations" to **contract obligations**. Any of them showing up as the cause of a blocking
   set is a defect, not a tuning opportunity. (Biased coloring belongs to the same discipline but
   is *not* on this list, and the distinction matters: it runs after the pressure decision, so it
   can waste a **register** but never a **value**. See "Known limits" #1.)
3. **We can afford to be exact.** Because SSA interference is chordal, per-point `maxlive` *is*
   the minimum register count for the program as lowered — not an estimate. So `E5001` fires
   **iff** the loop truly does not fit, and the only way to be wrong is to have wasted a register
   upstream.

### The three rules

> **RULE 1 (SSA / single live-range start).** Every `ValueId` has exactly one def, and it
> dominates every point at which the value is live.
>
> This is what makes live ranges dominance-closed subtrees, hence the interference graph
> **chordal**, hence dominance-order greedy coloring **exact**: two values interfere iff one is
> live at the other's def, so each edge is enforced once at its later endpoint, and layout order
> is a perfect elimination order. After splitting, `maxlive ≤ pool` everywhere by construction, so
> **the colorer cannot fail** — a coloring failure is a compiler bug and asserts as one.
>
> The `Reuse` operand model is what makes Rule 1 true at the Target tier: without it,
> `mov dest,lhs; add dest,rhs` writes `dest` twice and the tier is not SSA.

> **RULE 2 (spill placement — emit the forced spill, refuse the searched one).** A spill is
> emitted when its placement is *determined*; `E5001` is raised when choosing it would require a
> *search*. Concretely, two placements are legal, and they are the two the compiler never has to
> think about:
>
> - **COLD** (`SpillPlacement.cold`) — the value is **idle** across the pressured region (its def
>   and every use sit at loop depth 0). Store before, reload after; **every inserted op lies
>   outside every loop**, asserted on insertion. This relieves a **full-pool** overflow.
>
> - **FORCED** (`SpillPlacement.forced`) — the value is **confined** by the clobbers it is live
>   across (a call's caller-saved set, an `idiv`'s RAX/RDX), and more values are confined to that
>   register subset than it has registers (Hall's condition — `HallCondition.maxon`). It *cannot* stay
>   in a register those ops clobber, so the store/reload bracket is the only placement in existence.
>   **Permitted at any loop depth** — this is the amendment, and it is what makes ordinary
>   call-in-a-loop code compile. Note the confinement is a property of the **value's whole live
>   range**, not of the clobber op: the violation can therefore appear at a point that clobbers
>   nothing at all, which is exactly the bug "Known limits" #0 records.
>
> `E5001` fires **only** on a **full-pool** overflow with no cold-spillable value: the loop's
> genuine working set exceeds the machine. A **confined** (clobber-only) overflow (the values fit the
> full pool but not the subset they are confined to) is *always* relievable by a forced bracket —
> every confined value has a store anchor, an op def or a phi's block entry — so reaching `E5001`
> from one is a **splitter bug**, and `noVictimAtPeak` panics rather than let it surface as user
> register pressure.
>
> **The two pools count two different demands, and lumping them over-counts.** Full-pool demand is
> the colorer's total register hold at a point — live values *plus* dead-phi reservations *plus*
> the reuse-copy transient. Reduced-pool demand is only the values the op *constrains*: those live
> across it, plus its own operands. A dead phi is neither, and `pickPreferredRegister` lands it in
> a caller-saved register anyway, so it never competes for the callee-saved subset.
>
> **A split only counts if it actually kills the value at the peak.** Uses *after* the peak become
> reloads; uses *before* it keep the original. So the value is dead across the peak only if no
> before-peak use is still reachable *from* the peak without passing the value's (single, SSA) def.
> Under a back edge it often is: `n` in `while i < n` is used in the header, *before* the loop's
> call in layout order, yet stays live across it — storing it relieves nothing and leaves a wasted
> store in the header. A loop-header **phi** is the opposite: every back-edge path re-enters its
> def, so it *is* relievable. `killsValueAtPeak` is that test.

> **RULE 3 (the author always has a move).** There is **no escape hatch** — no attribute, no flag.
> `E5001` is final. That is only defensible if every value in a blocking set is one the author can
> actually *see and remove*, which makes the following an invariant rather than a nicety:
>
> **No compiler-introduced value may ever appear in an `E5001` blocking set.** Witness tables,
> layout descriptors (dictionary-passing generics), and refcount temporaries are pressure the
> author cannot see, did not write, and cannot delete. They are all either loop-invariant constant
> addresses (→ **rematerialize**) or tiny-lived temporaries. **If one ever blocks an allocation,
> that is a compiler bug, not author error** — told to delete a value that is not in its source, an
> agent cannot converge, so it hangs. This is why rematerialization is load-bearing rather than an
> optimization, and why the diagnostic *panics* rather than emit a misleading location when a
> blocking value has no source origin.

**Ops emitted *after* coloring — `pushReg`, `popReg`, `xchgRegReg`, SSA-destruction copies — carry
physical registers only and are invisible to Rule 1.** (`xchgRegReg` has two defs and would
otherwise violate it; it is legal precisely because the allocator never sees it.)

### Lineage: what is taken from Cranelift's regalloc2, and what is not

**Taken — the operand model** (`Def`/`Use`, `Early`/`Late`, `Any`/`FixedReg`/`Reuse`), because
`Reuse(i)` deletes the two-address problem at the root and gives fixed-register ops a declarative
home. **Taken — the checker**, a symbolic verifier that runs on every compile. **Taken — the data
layout** (below); regalloc2 is fast substantially *because of* its representation.

**Not taken — the algorithm.** Live bundles, spill weights, eviction, the priority queue, iterative
re-splitting. regalloc2 is engineered so allocation *always succeeds by spilling*; this contract is
to fail loudly instead — and a heuristic-dependent failure is both unactionable for someone told to
rewrite their code and unstable across compiler versions.

### Data representation — the part that decides whether this is fast

Not a detail and not deferrable: register allocation was **74% of v1's self-compile**, and the
budget is ≤30 s for the whole thing.

**In Maxon this matters more than in Rust, and the codebase already knows why.** A `type` has
*reference* semantics — so `Array with LiveRange` is an array of **pointers to heap-boxed,
refcounted** LiveRanges: one live heap object per range. That is precisely the trap
`SourceRange.maxon` documents and dodges with dense scalar columns, and the allocator inherits the
discipline wholesale. `ValueId`s are dense, function-local integers, which makes them perfect array
indices:

| Concern | Representation |
|---|---|
| Value → color / next-use / `forbiddenPhys` / def point | one flat `Array with int` column each, indexed by `ValueId` — no hashing |
| Live-in / live-out per block | a **dense bitset** over `ValueId` (`int` words), all blocks in ONE flat `blocks × words` matrix, so the fixpoint's union/diff are word-parallel |
| Per-op operands | `Operand` **packed into one `MachineWord`**, filled into a **reusable scratch buffer** — no allocation and no heap box per operand |
| `BlockId` → block | a flat array (`BlockId`s are dense too). `IrModule.getBlockByIdIn` is O(blocks); calling it inside a fixpoint would be O(B² × iters) — v1's trap in miniature |
| Register sets | `u16` bitmask; picking a register is `lowestClearBit(...)` |
| Scratch buffers | allocated once, reused across functions |

> **No `Map` and no hashing anywhere in the allocator's hot path.** The `fixpointIterations` and
> `maxPressure` counters exist to tell us when that stops being true.

### The pipeline

`allocateRegisters` first asserts call-clobber consistency, then runs `splitCriticalEdges`
across the module (outside the per-function loop, because inserting a block mutates
`func.blockRefs`), then per function:

| # | Phase | File | What it does |
|---|---|---|---|
| 1 | **Split** | `SplitLiveRanges.maxon` + `HallCondition.maxon` | Cold-spill live-range splitting. Finds every point that cannot be colored — including one whose values are *confined* to a register subset too small for them (Hall) — and relieves it. Mutates the IR and returns the final `LivenessResult`. |
| 2 | **Color** | `RegisterAllocator.maxon` | Biased forward-sweep coloring. Records the reuse copies it needed. |
| 3 | **Plan SSA destruction** | `SsaDestruction.maxon` | Builds the per-edge parallel-copy plan; does not commit. |
| 4 | **Check** | `AllocChecker.maxon` | Symbolically verifies the still-virtual program + coloring + plans. |
| 5 | **Commit** | `SsaDestruction.maxon` | `applyAllocation` rewrites ops in place, splices copies, clears the phi model. |

> **INVARIANT: coloring rewrites ops IN PLACE, at the same `module.ops` index.**
> `applyAllocation` reads the op at index *i*, rebuilds it with colored operands, and writes it
> back at *i* (dropping a `mov r,r` self-move that biased coloring produced). Every
> `block.opRefs` entry and `terminatorIndex` is an index into that array — appending colored ops
> or compacting would invalidate every reference in the function.

### The operand model

`IR/Target/TargetOperands.maxon`. `targetOpOperands` is ONE exhaustive `match` over every
`TargetOp` — no bare `default`, so a new op is a **compile error** until its operands are
declared. Every consumer — liveness, splitter, colorer, checker — reads register facts ONLY
from here. Operands are PACKED into one `MachineWord` in a reusable dense buffer (no heap box
per operand — the same discipline as `SourceRangeTable`).

Each operand declares a kind (`Def`/`Use`), a position (`Early`/`Late`), and a constraint:

- **`any`** — a plain use or def.
- **`reuse(i)`** — the def must land in operand *i*'s register. **This is what deletes the
  two-address `mov` at the root.** ISel emits ONE op (`sub dest, lhs, rhs`), never a seed copy;
  `collectReuseHints` biases dest and input to the same register; `allocateReuseDef`
  materializes `mov dest, input` **only** when the input outlives the op and the coloring could
  not coalesce them. The common dies-at-the-op case costs zero copies, so loop bodies are
  copy-free.
- **`fixedReg`** — declared, but **not currently used**. Fixed-register requirements are
  expressed instead through (a) `physical(...)` operands in the IR (the `mov rax, dividend` and
  `mov argReg[k], arg` pre-moves) and (b) the `implicitUses`/`implicitDefs` register masks on
  `TargetOpMeta`.

The implicit masks are how the allocator learns about registers an instruction touches without
naming: `ret` implicitly uses `{R8}`; `callDirect` implicitly defs the 9 caller-saved (`0xFC7`);
`cqo` uses RAX and defs RDX; `idivReg` uses RAX and defs RAX|RDX.

**Known limit:** the `Early`/`Late` position bit is packed but **never read back**. Def/use
timing is instead answered operationally (per-op death sets). The consequence is documented in
`TargetLiveness.maxon`: `forbidOperandsFromImplicit` is *not* correct for a late-clobber op such
as a call — which is sound today only because `callDirect` carries no explicit operands. An op
with **both** explicit operands and a late implicit clobber would need this made position-aware.

### Liveness

`TargetLiveness.maxon` + `RegBits.maxon`. A `FuncCfg` is built ONCE (CSR succ/pred, `BlockId` →
local flat arrays — never a `Map` in a fixpoint), with back-edge loop detection giving per-block
depth. SSA live-in/out is iterated to a **fixpoint** (a single reverse pass is wrong once there
are back edges). A backward sweep then yields, per program point, the exact live set — from
which come `maxPressure` (the exact per-point χ = ω) and `forbiddenPhys` (the registers a value
may not take, accumulated from every clobber mask it is live across). All state is `Array`-indexed
columns + a `blocks × words` bitset matrix + `u16` register masks.

### Coloring

A forward walk over a `u16` in-use bitmask, seeded per block from `liveIn`. At each op it frees
dying operands, then picks the def's register with `lowestClearBit(inUse | forbiddenPhys(v) |
~pool)`. Dominance order is ASSERTED (every use is already colored when reached).

**Biased coloring is a correctness obligation, not an optimization.** Hints from
block-arg↔branch-arg pairs and from reuse defs collapse copy-related values into ONE register, so
a loop's back-edge copy elides instead of landing IN the loop, and one loop-carried value costs
one register instead of two. A value the compiler holds in two registers is a **wasted register**,
and Rule-of-the-contract #2 says a wasted register is a false positive waiting to happen.

> **What it does NOT do — and this misdirection has cost real debugging time — is change
> `maxlive`.** A phi and the value an edge passes it are *never simultaneously live*: the arg's
> range ends at the edge, the phi's begins at the successor's entry. Liveness does not count them
> twice, and the splitter (which is what raises `E5001`, and runs **before** coloring) therefore
> cannot see a coalescing failure at all. Biased coloring buys **registers used and copies
> emitted**, not pressure. When an `E5001` really is false, the surplus is in the *IR* — the
> shape `pruneDeadBlockArgs` deletes — not in the coloring.

Register preference is **caller-saved first**, so leaf and call-free code never touches a
callee-saved register and never pays a prologue push.

**The class constraint propagates BACKWARDS along a copy hint**, because a forward sweep in
dominance order colors the DEF first and the hint can only be read off an *already-colored*
partner. A loop-carried accumulator is defined in the entry block and its header phi later, so at
the moment that matters there is no register to copy — only a constraint:

```
var sum = 0                 // survives no call -> prefers CALLER-saved -> rax
while i < n 'L'
    sum = sum + f(i)        // the PHI is live across the call -> forbidden all 9 caller-saved
end 'L'                     //   -> its only home is a CALLEE-saved register -> r12
```

The hint then misses (rax is forbidden for the phi), SSA destruction materializes `mov r12, rax`
on the loop-entry edge, and the one value `sum` holds **two** registers. `copyGroupBlocked`
widens the constant's blocked mask by the *phi's* forbidden set, so the constant is materialized
straight into `r12`, the edge copy self-elides, and the pair costs one register.
`pinnedPartnerRegister` is the same move for a partner pinned by a *physical* operand (`return
sum` pins the phi to R8 through the return move). Neither can over-constrain: both are
PREFERENCES — `chooseRegister` falls back to the value's own mask when the group's class is full
— and neither fires when the partner is already colored, which is what leaves M5.12's
scarce-class protection (below) exactly as it was.

### Spilling: cold-spill live-range splitting

`SplitLiveRanges.maxon` runs *before* coloring and mutates the IR. Its loop is: find the peak →
choose a victim → remat or spill → recompute liveness → repeat, with a runaway bound and a
post-condition assert that no point still exceeds its pool.

**Two overflows, two responses — and the second is HALL'S CONDITION, not a second number**
(`HallCondition.maxon`). A clobber shrinks a *value's* pool for its **whole live range**: a value
live across a `callDirect` can only ever sit in one of the **5 callee-saved** registers, one live
across an `idiv` in `pool ∖ {rax, rdx}`, one live across an argument pre-move outside that argument
register. Values at one point are therefore shopping in **differently sized register sets**, and no
single number decides whether they fit. Each op is tested two ways:

| overflow | demand counted | against | response |
|---|---|---|---|
| **full-pool** | `effective` — live values **+** dead-phi reservations **+** the reuse-copy transient | the whole pool (14) | COLD split; `E5001` if nothing is idle |
| **confined** | Hall: for every register subset `U`, the live values with `A(v) = pool ∖ forbidden(v) ⊆ U` | `U` — the **witness** (the callee-saved subset at a call; `pool ∖ {rax,rdx}` at an `idiv`) | **FORCED bracket**, at any loop depth. Never `E5001` |

The two demands are genuinely different, and lumping them over-counts: a dead phi is not live across
the call and is not forbidden its clobbered registers, and `pickPreferredRegister` puts it in a
caller-saved register anyway — so it is confined to nothing and never competes for the callee-saved
subset. Full-pool peaks always outrank confined ones, so they are relieved first; that ordering is
what makes the store-anchor gate sound by the time a confined peak is reached (and it also bounds the
live set at 14 wherever the Hall search runs, which is what makes an exact search affordable).

The confined test **subsumes** the old one — at a call, every live-across value is confined to the
callee-saved subset, so the witness is that subset and the count is the same live count the old
`raw > popcount(pool ∖ implicitDefs)` compared. What it adds is the case that check could not
express: values confined by **different** calls, colliding at a point that is not itself a call.
That was a colorer panic. See "Known limits" #0 for the two-stage screen-then-confirm shape and why
the cheap cardinality form alone is *not* exact.

**Victim choice** is remat-first, then Belady/MIN:
- **Rematerializable** values (a constant def, not a phi, not edge-passed) are re-emitted at each
  after-peak use rather than spilled — always preferred.
- Otherwise the **farthest next use** wins, gated on (a) being spillable for the peak's placement —
  **cold** requires def and every use at loop depth 0, **forced** requires only a store anchor (an
  op def, or a phi's block entry).
- **Both** are additionally gated on `killsValueAtPeak`. It is not a spill-specific test: remat
  partitions a value's uses around the peak exactly as a spill does, so the original dies across the
  peak under precisely the same condition. A constant that failed it — `let c = 7` used only in a
  `while i mod c < 3` header, live across the loop's peak solely around the back edge — re-emitted
  nothing, relieved nothing, and was re-picked until the runaway bound panicked.
  (`register-spill.remat-constant-live-only-around-the-back-edge`.)

**A split must actually kill the value at the peak.** After-peak uses become reloads; before-peak
uses keep the original. So the value dies across the peak only if no before-peak use is still
reachable *from* the peak without passing its (single, SSA) def. `n` in `while i < n` is the
cautionary case: its only use is the header compare, which precedes the loop's call in **layout**
order but follows it around the **back edge** — storing it relieves nothing. A loop-header **phi**
is the opposite: every back-edge path re-enters its def, so it is relievable. (Without this test the
splitter over-spills, and its termination potential Φ does not strictly decrease.)

**Split shape: Belady split at the eviction point + dominating reloads.** Store **at the def** (after
the defining op — or, for a **phi**, at its block's entry, where the edge copies have already placed
it), then **one reload per after-peak use-block**, placed so it dominates its uses — each reload
defines a **fresh `ValueId`**, so SSA is preserved and no phi or SSA reconstruction is needed. (This
is what retires v1's SplitKit failure.) **Branch-edge args are rewritten alongside op uses**: an edge
arg is a real use, read at the block's end, and one left naming the original keeps it live to that
point — so the spill would not kill it across the peak and the driver would re-pick it forever.
Loop-carried accumulators are exactly this shape.

> **The store anchors at the DEF, and that is a correctness requirement, not a preference.** It used
> to anchor after the value's last *before-peak* use, on the reasoning that "it precedes the peak, the
> reloads follow it". **Layout order is not dominance**, and that reasoning fails twice over: a
> before-peak use can sit in a block that does not dominate the reload sites (the `then` arm of an
> `if`, whose store never runs on the `else` path, leaving the slot unwritten under the reload after
> the merge), and a before-peak use can be an **edge arg**, whose anchor op is the block's
> *terminator* — which a store cannot follow at all, and which crashed the splitter outright. The def
> dominates every use of the value by Rule 1, so it is the only anchor that always dominates every
> reload. It also emits strictly better code: a value defined outside a loop but used inside it before
> the loop's peak now stores **once** instead of every iteration.
> (`register-spill.forced-spill-with-edge-arg-before-the-peak`.)

For a **cold** split, an assert panics if any inserted op lands at loop depth ≠ 0 — so **a loop
body that does not use a spilled value is byte-identical to the un-spilled version**. A **forced**
bracket is exempt by construction: it is the placement that belongs in the loop.

### `E5001` — the register-pressure diagnostic

`Targets/Shared/RegisterPressureDiagnostic.maxon`. **The decision has already been made when this
runs**: the splitter relieved every idle value and rematerialized every constant, and `chooseVictim`
still found no value crossing the peak that can be moved. What remains is the loop's true working
set against the registers available AT that point. This file does not re-decide feasibility — it
turns an already-exact decision into a source-mapped message, so **it cannot add a false positive**.

The message reports:
- **The exact deficit** — "remove 3 of these 17 values", not "too many" — measured against the
  peak's own **witness** (`PressurePeak.witness`), which for an `E5001` is always the full pool of
  14. That equality is enforced, not assumed: `E5001` is raised **only** for a full-pool overflow (a
  *confined* point is relieved by a forced bracket, and `noVictimAtPeak` panics rather than let one
  reach the diagnostic), and `buildRegisterPressureMessage` panics if it ever sees a witness that is
  not the whole pool. Reading the pool off the peak **op** instead — `pool ∖ op.implicitDefs`, which
  is what this used to do — reports 5 whenever a full-pool overflow happens to land on a call, and
  the deficit computed from it asks the author to remove **nine more values than the program needs**.
  Measuring an `E5001` against a reduced pool is the false-positive class of "Known limits" #2; the
  witness makes it structurally unspellable.
- **Each blocking value's source def site**, ranked cheapest-to-move first (fewest uses inside the
  loop = fewest reloads after the array rewrite).
- **The transformation**, named: hold the working set in an array. Array elements are never promoted
  into registers, so the hand-spill *stays* spilled.

It is **deterministic byte-for-byte** — no map iteration anywhere, values swept in id order,
candidates sorted by a total order (uses-in-loop, then value id). `specs-shv2/register-pressure.md`
gates the exact text through a ` ```maxoncstderr ` block.

**`ValueOrigin` is what lets a Target-tier diagnostic point at source.** Source spans die at the
Maxon→Std boundary by design (`SourceRangeTable` is keyed by Maxon op index and does not survive
`lowerMaxonToStd`'s fresh module). But two things survive verbatim through all three tiers: a
value's `ValueId` and a function's INDEX. `IR/Maxon/ValueOrigin.maxon` is the `(funcIndex, ValueId)
→ Maxon OpIndex` map that closes the loop — **three dense scalar columns**, same discipline and
same reason as `SourceRangeTable`, recorded inside the same `emitOp`/`emitTerminator` choke points
(so a value cannot be minted without an origin) and folded whole-program by `mergeArtifact`. A
loop-carried value is an SSA phi with no defining op, so it is chased to the incoming value it
copies — the author's declaration. A value that resolves to NO origin is by Rule 3 a compiler
defect, and `defSiteOf` panics naming it rather than emitting a misleading location.

`allocateRegisters` and `buildBackend` therefore `throw CompileError`, and the diagnostic lands on
`project.diagnostics` like any other. The ownership checker will want this same table to say "moved
here, used there" — it is not regalloc-only infrastructure.

### SSA destruction

AFTER coloring. The allocator consumes `blockArgs`/`branchEdges` directly — there is no
phi-elimination pass. Per phi-carrying edge it sequences a parallel copy of physical `mov`s,
breaking cycles with a physical-only `xchgRegReg` (`REX.W 87 /r`, invisible to SSA). Placement is
at the predecessor's end (single-successor pred) or the successor's start (single-predecessor
succ) — which is why **every phi-carrying critical edge is split by a pre-pass before coloring**,
so a move never runs on a sibling edge.

### The `AllocChecker`

A symbolic verifier that abstractly interprets the allocated function — per-op `preg → vreg`
state, per-edge parallel-copy simulation, spill store→slot→reload identity chains, the
reuse-invariant (dest holds the reuse input at the op), and the incoming ABI registers seeded at
entry — and asserts that every use reads the register holding its value. A failure **panics** the
build, which under `spec-test` fails the test that triggered it.

**Where it runs** — it is a *verification* pass costing ~10% of total compile time, so it is
always on where it is the actual safety net and opt-in where it is merely a tax:

| Context | AllocChecker | Why |
|---|---|---|
| `spec-test` | **ON** (default, per test) | the only real gate — see below |
| `verify-warm-rebuild` | **ON** (forced) | dev gate; byte-identity across two cold compiles is satisfied just as well by two *identically wrong* compiles, so determinism alone proves nothing about correctness |
| `build` | **OFF** unless `--check-alloc` | a production build should not pay ~10% for a verification pass |

**Why `spec-test` must never turn it off.** `SpecTestRunner` **regenerates** the committed
fragments on every run: they are **outputs, not gates**. A wrong-but-self-consistent allocator
therefore produces a self-consistent fragment and a **green suite**. The checker is the one thing
in that path that can say *no*. This is not hypothetical — it has caught real silent miscompiles
the full suite passed (the parameter-capture read-after-clobber in `functions.md`), and it is
directly reproducible: sabotage `chooseRegister` to honour a copy hint without checking the
register is free, and `functions/call-in-loop` still **passes** with the checker off, on a
program the checker proves is miscompiled.

**Why the split is safe.** The checker is *pure*: it reads the IR plus the allocation plan and
either panics or does nothing. It cannot change an emitted byte, so `build` and
`build --check-alloc` emit **identical** output — the flag decides only whether a wrong byte is
*caught*, never which byte is produced. (It runs before `applyAllocation` commits the plan.)

**Opt-out, never opt-in.** Under `spec-test` the checker defaults to ON for every test, and a
spec must deliberately *say* it does not want it:

* per spec — `checkAlloc: false` in the YAML frontmatter, beside `status:`
* per test — a `<!-- checkAlloc: false -->` marker on a line *after* that test's
  `<!-- test: NAME -->` marker (overrides the spec-level value for that one test)

Any value other than `true`/`false` is a hard `CompileError.specError` that aborts the run — an
unreadable gate directive is never guessed at. The polarity is the whole point: the checker's
value is that it runs over code nobody thought to check. Every allocator bug it has caught
surfaced where no author would have ticked a box — the parameter-capture clobber in `functions`,
the two-sequential-loops colorer panic in `while-loops`, the false-E5001 class in ordinary loop
code; **none** in an allocator-focused spec. An opt-in would arm it exactly where someone already
suspected a problem, i.e. where it is least needed.

The setting rides on `Project.checkAlloc` (already threaded into the backend, so no global and no
new parameter). `spec-test` passes `--check-alloc` on the **subprocess** `build` it spawns per
test — the compile happens in another process, so the flag is the only channel available.

### The register pool and ABI

| | Registers |
|---|---|
| **Allocatable pool** | 14 — every GPR except `rsp`/`rbp` |
| **Caller-saved** | `rax, rcx, rdx, rsi, rdi, r8, r9, r10, r11` (mask `0xFC7`) |
| **Callee-saved** | `rbx, r12, r13, r14, r15` |
| **Argument registers** | `[rcx, rdx, rax, r9, rsi, rdi]` — 6, all caller-saved (the array length IS the parser's param cap) |
| **Return** | `R8` |

This is Maxon's existing custom ABI, not the Win64 one. `r10`/`r11` are **in** the pool: shv2
needs no reserved scratch, because SSA destruction breaks copy cycles with `xchg` and the IAT
call is RIP-relative. (This supersedes the "reserved scratch" note in the design doc's corrections
header.) The pool is a property of the target (`allocatablePool`); arm64 will declare its own.

**Calls** constrain coloring through three mechanisms, and no fourth:
1. `callDirect.implicitDefs = 0xFC7` → the backward sweep folds it into the clobber mask → every
   value **live across** the call is forbidden all 9 caller-saved registers, so its only home is
   the 5 callee-saved.
2. Caller-saved-first preference keeps call-free code off the callee-saved registers entirely.
3. `usedCalleeSavedRegs` scans the **colored** body and pushes/pops exactly what was used.

Call arguments are plain physical pre-moves (`mov argReg[k], arg_k`) followed by `callDirect`,
with **no parallel-copy sequencer** — each pre-move has a physical *def* that the backward sweep
already forbids for any value live across it, so forward-order emission can never read a clobbered
register. `callDirect` carries no explicit operands, which is what sidesteps the late-clobber
unsoundness in `forbidOperandsFromImplicit`.

Parameter **capture** at entry (`mov v_i, argReg[i]`, a physical *source*) needed its own
protection, because the arg-setup forbidding is a physical-def mechanism and does not mirror to
sources: each parameter is forbidden every *other* parameter's incoming register, so a parameter
can only take its own incoming register or a non-argument register. The `AllocChecker` seeds each
parameter in its incoming register at entry so it models this and catches violations. (The entry
move elides whenever the value lands in its own argument register.)

**`idiv`/`mod`** is lowered as `mov rax, dividend; cqo; idivReg divisor; mov result, rax|rdx`, with
the divisor left VIRTUAL. Two mechanisms keep it out of RAX/RDX: the clobber→forbidden path
handles values live *across* the `idiv`, and `forbidOperandsFromImplicit` handles the divisor
itself — which **dies** at the `idiv` and is therefore absent from the live-across set.
Divide-by-zero and `INT_MIN / -1` raise a raw `#DE`; the fault handler is a runtime deliverable, so
there is no compiler guard.

### Stats

`RegAllocStats` records per-phase `Clock` milliseconds plus the counters that matter more at
sub-millisecond scale: `fixpointIterations`, `maxPressure`, `copiesInserted`,
`hintsHonoured`/`hintsMissed`. Logged under `LogCategory.codegen`.

*Rationale for the whole shape:* v1's register allocation was **~74% of self-compile wall time** —
not because backtracking is inherently slow, but because it rebuilt the interference graph from
scratch on every spill iteration. shv2 builds NO graph (an availability sweep) and runs no
reactive spill loop over coloring. And v1's "74%" stood for months with **zero sub-phase
attribution**, which is why the timers shipped in the allocator's first commit.

### Performance: what the data structures cost

Register allocation was **97% of compile wall time** on a large function (one 3,200-`if` function:
5.6 s of 5.8 s), growing **quadratically** — the shape that made v1's allocator 74% of self-compile.
None of it was the colorer. All of it was **iterating the value space where the live set was meant
to be iterated**:

| site | was | now |
|---|---|---|
| `seedInUse` (colorer) | O(blocks × values), once per block | O(live) — `bitsetCollectRow` |
| `seedRegState`, `checkEdge` (AllocChecker) | O(blocks × values), O(edges × values) | O(live) |
| `applyForbidden` (liveness) | O(clobber-ops × values × operands) — every call | O(live) |
| `deadPhiCountForBlock` | re-swept each block + 2 heap allocs per call | recorded once by the sweep that already computes it |
| liveness fixpoint inner step | 9 row passes per block per iteration | 2 (`bitsetOrAndNotRow`, `bitsetTransferRow`) |

A live set is **sparse** — a handful of live values out of thousands of dense `ValueId`s — so
walking the set bits (word-parallel, skipping 64 empty ids at a time) is the contract, not a
preference. **`bitsetCollectRow` is the only way to walk a live set**; a `for v in 0 upto
valueCount` over one is a quadratic waiting to happen. Scratch buffers are reused across blocks so
the hot paths allocate nothing.

Result: **8.8× on the large function** (5,609 ms → 640 ms of allocator time) and **8× on a
call-heavy one** (750 ms → 94 ms), with the call-heavy case now linear. The phase timers are
**disjoint** — `splitting` no longer silently includes `liveness`, which was hiding the single
biggest cost in the wrong bucket.

### The CFG skeleton: ADDRESS the branch, do not SEARCH for it

The same mistake once more, and this time in the CFG rather than the value space. **A block's
successors were found by walking every op in the block** — `collectBlockSuccessorIds` scanned
`opRefs` looking for the `jcc`, because on x64 a two-way branch is a `jcc` BODY op (the then-edge)
paired with a `jmp` TERMINATOR (the else-edge), so a block's second successor is named by a body
op. Building the skeleton was therefore a **full pass over every op of the function**, and the
allocator builds two per function (the critical-edge split, then the RPO reorder). That is what put
`blockOrder` and `criticalEdges` on the profile at all: two phases that allocate nothing and decide
nothing, costing together as much as coloring.

`IrBlock` now records **where** its conditional branch is (`condBranch: none | opIndex(i)`), so
`collectBlockSuccessorIds` is O(1) per block and a skeleton build is **O(blocks + edges)**.

**The ops remain the ground truth, and this is the whole design.** The block caches only the
*position* of its two control ops; every target is dereferenced from the op itself at every read.
So `retargetJcc` — which rewrites the op in place at the same index — has **no cache to
invalidate**, and `splitCriticalEdges` (which ADDS blocks and rewires edges) needs no maintenance
beyond the fresh blocks, which are born with `condBranch: none` and a `jmp` terminator. Caching the
successor *ids* would have created a second source of truth for the CFG; caching the *index* does
not. And `branchEdges` is still not the successor list — it holds only phi-CARRYING edges, so
deriving the CFG from it would silently drop every non-phi edge.

What must hold is that the index is **complete** — a `jcc` that entered a block another way would
leave its then-edge out of every CFG the allocator builds, and liveness, loop depth, pressure and
spill placement would all silently follow a CFG the program does not have, surfacing far away as an
allocator bug. It is enforced in three places, none of them a hope:

| where | cost | what it enforces |
|---|---|---|
| `IrModule.appendCondBranch` — the ONLY way a conditional branch enters a block (`lowerCondBranch` is its one caller) | O(1), always | the index is *set*, and a block gets at most one |
| `IrModule.appendOp` | O(1), always | **no op may be appended past a conditional branch** — it would run only on the not-taken path (a miscompile independent of the CFG), and would hide an edge behind an op the builder no longer scans |
| `AllocChecker.checkCondBranchIndex` (check D) | O(ops), under `checkAlloc` | against the OPS: the block's `jcc` set is exactly what `condBranch` names, and a two-way block's terminator is a `jmp` |

The O(ops) completeness sweep is the very scan the index removed, which is why it lives behind
`Project.checkAlloc` — `spec-test` and `verify-warm-rebuild` pay it on every function of every run,
a production `build` does not. Same policy as the rest of the AllocChecker: the flag decides whether
a wrong byte is *caught*, never which byte is *produced*.

Two scans died with it. `SsaDestruction.retargetControlOp` was walking a predecessor's whole op list
to find the `jcc` it had to redirect. And `insertReloadAtBlockEnd` — the splitter's "put the reload
after every op, ahead of the branch that reads it" — was inserting at the end of `opRefs`, which on
a two-way block is *between* the `jcc` and the `jmp`: the reload would have run only on the
not-taken path, leaving the taken edge reading an unloaded register. It now goes before the
conditional branch, which dominates both edges (a `loadRegSlot` sets no flags, so it is safe between
the compare and the `jcc`). No program in the suite reaches that placement today — the one test that
reaches `insertReloadAtBlockEnd` at all does so on a single-successor latch — so this is a latent
hazard closed, not a miscompile observed.

### The splitter: one INDEX, not a scan per candidate

The same mistake, one layer up. `chooseVictim` asks a fixed set of questions — *where is this value
defined? where is it used? is it a phi? is it edge-passed? is it already stored? does it touch a
loop?* — of **every value crossing the peak**, and each one was answered by its own walk of the
function. That is **O(candidates × blocks × ops) per split**, and it was **61% of register
allocation** whenever the splitter ran.

There is now **one `UseIndex` per function** (`TargetLiveness.maxon`), built in a SINGLE pass and
hung on `LivenessResult`. It carries, keyed by `ValueId`: the def site (op + block, or phi block),
the use sites (op index, owning block, layout sequence, and whether the read is an edge arg), and the
flag columns the eligibility gates test. Liveness needed the use lists anyway; the splitter's
questions are the same traversal, so they are the same index. Every splitter lookup is now O(1) or
O(uses of that one value), and `chooseVictim`'s candidates come from `bitsetCollectRow` over the
peak's live set — never `for v in 0 upto valueCount`.

The use sites are four parallel RECORD columns in layout order, grouped by value with the shared
`buildCsr` (M5.14) — the CSR's `list` holds **record indices**, not values, which is what lets one
counting sort group four columns at once. `buildCsr` preserves push order within a key, so a value's
uses come out in ascending sequence: the order the split's before/after-the-peak partition depends
on. No fifth hand-rolled counting sort.

It is a per-iteration **snapshot**, not a maintained structure: it is rebuilt with liveness after
every split, and the only consumer that runs *while* the IR is being mutated reads exclusively the
fields that are stable under those mutations — an op's `module.ops` index (the table is
**append-only**, so an index never moves) and a use's owning block (an insertion never moves an op
between blocks). Everything positional is re-derived at the point of use. So a split cannot read a
stale index, because nothing it reads *can* go stale.

Three sparse side-tables were the same shape at smaller scale, all quadratic in the number of
splits or edges, and all now indexed: the reload origins (by fresh value id **and** by slot —
`ReloadOriginIndex`), the SSA-destruction move plans (by CSR edge slot), and the reuse copies (by
layout sequence — **not** by `module.ops` index, which is module-global and non-contiguous per
function, so a column keyed by it would be the size of the whole module, per function).

**The CFG is invariant under splitting**, so it is built once and reused by every liveness
recomputation: a split inserts only body ops, rewrites operands, and repoints edge args — it never
adds a block, never changes a branch target. Only `valueCount` grows, and the splitter knows it
exactly (every id it mints is defined by an op it inserted).

Measured on a spill-heavy benchmark (loops holding 8 accumulators across a call, so ~3 forced
spills each). The refactor is **byte-identical** — the emitted `.exe` compares equal to the
pre-index compiler's at every size, up to 1,600 splits — so these are the same allocation decisions,
reached faster:

| shape | splitting | allocation total |
|---|---|---|
| 400 functions, one pressured loop each (1,600 splits) | 358 ms → **109 ms** (3.3×) | 735 ms → **437 ms**, linear |
| ONE function, 100 pressured loops (400 splits) | 5,530 ms → **623 ms** (8.9×) | 7,906 ms → **2,688 ms** (2.9×) |

The splitter's own growth exponent on the intra-function shape falls from **2.07 to 1.26**.

**What is left, and it is now the whole of it: the driver still recomputes liveness from scratch
after every split.** That is O(function × splits) — the intra-function shape above is still
exponent ~1.9, and `liveness` is ~80% of allocation there. Splitting itself is no longer the cost.
Making it incremental is tractable *in principle* — a split changes the liveness of exactly one
value plus the fresh reload ids it mints, and nothing else — but the CSR live sets are deliberately
not editable in place, the per-op layout sequence numbers all shift when an op is inserted, and the
peak would have to be maintained rather than re-swept. It is a redesign of the allocator's core with
a silent-miscompile failure mode, not a refactor, and it is not worth doing until a real program
demands it (the many-small-functions shape — which is what a real codebase, and this compiler's own
source, looks like — is already linear).

### Liveness: SSA path exploration, no fixpoint, sparse sets

The classic iterative dataflow fixpoint (`liveIn = use ∪ (liveOut ∖ def)`, swept to convergence over
`blocks × values` bitsets) is the **wrong algorithm for an SSA program**, and it was the last
superlinear term in allocation.

**In strict SSA the def dominates every use, so liveness needs no iteration at all.** A value is
live exactly on the CFG paths running backward from each of its uses up to its single def. So walk
them and stop: `solveLivenessSsa` starts at each use, marks blocks live going up through
predecessors, and halts at the def. Nothing converges; every block is visited at most once per
value, and only blocks where the value is genuinely live are visited at all. Cost is O(the liveness
information itself). Two things make it exact:

- **A back edge needs no special case.** Walking up from a use inside a loop reaches the header's
  predecessors — including the latch — and marks the value live around the back edge in the same
  walk. The live-in memo terminates it.
- **A phi is where a walk STOPS; a phi ARG is where one STARTS.** A block-arg is defined at its
  block's entry, so the walk halts there and does not enter the predecessors — and `liveIn`
  *excludes* a block's own phi defs (the colorer seeds from `liveIn` and *then* colors the
  block-args). A value passed on an edge is live at the END of the **predecessor**, so its walk
  seeds there. Getting these backwards is the classic phi-liveness bug.

**And the sets are SPARSE** (`LiveSets`, CSR). A dense `blocks × ceil(values/64)` matrix is
proportional to the value space rather than to the information: a 19,200-block function allocated
and zeroed **61 MB of live sets even when only one value was ever live**, and every consumer then
scanned 200 words per block to find it. The CSR lists are proportional to the liveness that actually
exists — which is exactly what the walk computes, so nothing is thrown away to build them. A dense
row survives only where it is right: the ONE working row a block's backward sweep mutates, seeded
and cleared in O(live).

The last O(values/64)-per-op cost went with it: the sweep's live **population is maintained
incrementally** (a step flips only the op's own operands) rather than recomputed with a popcount
over the whole row at every op. Likewise the colorer's death record, which was an `ops ×
values/64` matrix *per block* — but every query is about one of the op's own operands, so it is now
a per-op list of dying values.

**Result: allocation is LINEAR in program size.**

| shape | before | after |
|---|---|---|
| one large function (3,200 `if`s) | 5,609 ms, exponent 1.96, **97% of compile** | **140 ms, exponent ~1.0** (40×) |
| call-heavy (6,400 chained calls) | 750 ms, exponent ~2.0 | **62 ms, linear** (12×) |
| 19,200-block function | **SIGSEGV** | 281 ms |

That segfault was its own bug: `dfsBackEdges` and `rpoDfs` recursed once per block ("the CFGs it
runs on are small"), and overflowed the native stack. **No CFG traversal in the allocator may
recurse** — block counts are bounded by the program, not by us. All of them are worklist-driven.

### Known limits of the design

0. **Chordal exactness does NOT survive precoloring, and this is the one place the theory runs
   out.** `χ = ω` is a theorem for the *unconstrained* problem. Forbidden sets (a value live across
   a call is confined to the 5 callee-saved registers; an `idiv` operand cannot be RAX/RDX) make
   this **list colouring**, where greedy is exact only if the **scarce class is protected**. It is
   not a pressure problem and the splitter cannot see it: the values *fit* (five confined values,
   five callee-saved registers) and yet a greedy order can still fail.

   Concretely: biased coloring would honour a copy hint that handed a **callee-saved** register to a
   value that did not need one, and a value that could live nowhere else then found none — the
   colorer died with every register blocked. Two sequential loops containing calls were enough. The
   guard is `preferredClassMask`: **a hint may never take a register outside a value's preferred
   class while that class still has one free.** Copy elision is worth a `mov`; it is never worth a
   register the value cannot otherwise obtain. (`while-loops.sequential-loops-across-a-call`.)

   **The second half of this class is now CLOSED (`HallCondition.maxon`), and it took a stronger
   test than this entry originally proposed.** The exposure was the same shape at a *different*
   scale: values confined by **different** calls, simultaneously live at a point that is **not
   itself a call**. The peak-finder only checked the reduced pool AT clobber ops, so it saw nothing
   (at that point the pool is nominally the full 14), no single call had more than five values live
   across it, and the **colorer** then died with every register blocked. Six values, each used after
   a call in its own arm of an `if`/`else if` chain, are enough — liveness is path-sensitive, so
   each is live across only *its own* call, yet all six are live together at the chain's first
   `cmp`. (`register-spill.values-confined-by-different-calls`.)

   The model is **Hall's condition** on the per-value effective pools: no register subset `U` may
   have more values *confined* to it (`A(v) = pool ∖ forbidden(v) ⊆ U`) than it has registers. It
   **subsumes** both old tests — `U = pool` is the full-pool pigeonhole, and `U = callee-saved` at a
   call is the reduced-pool check — and the witness `U` is what tells `chooseVictim` which values can
   actually relieve the point (spilling one that could have taken a register outside `U` frees
   nothing) and what the E5001 deficit is measured against.

   > **The laminarity this entry claimed is FALSE, and the cardinality test it implied is therefore
   > not exact.** `forbidEntryParamCrossRegisters` forbids each parameter its **siblings'** incoming
   > registers, so with three parameters the masks are `{rdx,rax}`, `{rcx,rax}`, `{rcx,rdx}` — the
   > same size, pairwise **incomparable**, and jointly colorable. Bucketing by `|A(v)|` and
   > prefix-summing (`C(p) > p`) can call such a group an overflow *when it fits* — and the relief it
   > would then emit is a spill the program did not need, which is a false positive in a smaller key.
   > So the test runs in **two stages**: the O(16) cardinality prefix-sum is a **screen** (it never
   > *misses* — every value of a violating tight set has `|A(v)| ≤ |U|` — and is maintained
   > incrementally alongside the live count, gated on any value being confined at all, so a call-free
   > function pays nothing), and where it fires, an **exact** maximum bipartite matching decides.
   > Feasible iff it saturates the live values; the deficit is `|live| − |matching|` and the witness
   > falls out of the same alternating-path search. The confirmation is cheap because it only runs
   > where the point already fits the full pool, which bounds the live set at 14.

   Relief is a **forced bracket**, never `E5001` — the program fits the machine. One tie-break makes
   that true *and* leaves existing code untouched: a value confined by a call is confined
   **everywhere it is live**, not just at the call, so Hall sees the same violation at every op the
   confined values span (a loop header's `cmp` included). At equal pressure the peak therefore
   prefers a **fixed-register op**: bracketing at the call is the minimal repair, and without the
   preference the peak would drift to the earliest op the values happen to be live at, widening the
   bracket for no gain. A violation visible only at normal ops — the case above — has no such op and
   is relieved where it is seen.

1. **`maxlive` is exact for the program AS LOWERED — so a false `E5001` means the IR itself
   carries a value the author did not write.** Chordal ⇒ χ = ω = maxlive, and liveness is
   per-program-point, so values live on disjoint paths correctly do **not** interfere. The
   diagnostic can only be wrong if something *upstream* put a surplus value into the IR, and the
   tell is always the same: a blocking value the ranking reports as **"used 0 times in the loop"**.

   > **CORRECTED (this cost real debugging time).** This entry used to claim the surplus was a
   > *copy-related pair* — a block arg and what the back edge passes it — "counted twice" unless
   > biased coloring collapsed them. **That is not how it works.** Those two are never
   > simultaneously live (the arg dies at the edge; the phi is defined at the successor's entry),
   > so liveness never counts them twice, and coloring runs *after* the splitter has already
   > decided `E5001` — it cannot move the number either way. Biased coloring buys registers-used
   > and copies-emitted, which is worth having, but it is not what makes the pressure model
   > honest.

   The real surplus was **dead loop-header phis**: on-the-fly SSA mints one phi per mutable var in
   scope, a phi the loop never reads is *self-sustaining* through its own back edge, and liveness
   correctly holds it live around the whole loop. `pruneDeadBlockArgs` deletes them (see the Std
   passes). Two sequential loops with six accumulators each demanded **17** registers where the
   true working set is **9**.

   Every other put-a-surplus-value-in-the-IR path is likewise a contract bug, not a limitation: a
   literal in a register (→ immediates, `foldConstOperands`), a witness table in a register (→
   remat), a redundant two-address copy (→ `lea`, `Reuse`). **The lesson generalizes: when
   `E5001` looks wrong, read the IR before you read the colorer.**
2. **Clobbers reduce the effective pool PER VALUE, so `maxPressure ≤ pool` is necessary but NOT
   sufficient** — the splitter's peak-finder tests Hall's condition on the per-value pools as well as
   the full one (#0). But a confined overflow is a **clobber constraint, not a capacity limit**: it is
   dispatched by a FORCED bracket, never by an error. The **`E5001` deficit is always against the
   full pool of 14** — enforced by measuring it against the peak's own witness, which for an `E5001`
   is the whole pool by construction. Reporting it against a reduced pool ("6 live, only 5 survive a
   call") was a false-positive generator — it made an ordinary loop with five accumulators and a call
   a compile error, which is the shape of most real code and of this compiler's own inner loops.
3. **There is always a rewrite, but sometimes it is a real restructuring.** Register pressure is
   always reducible by hand-spilling into memory: twenty accumulators become one array, and pressure
   collapses to `{base, index, temp}`. The floor is set by the most demanding single operation,
   which on x64 is 3–4 registers — far below 14. But the honest version is that the fix for a
   genuinely hot loop (a SHA-256 compression round wants ~24 live values) is the same restructuring
   a real implementation would already do — not a one-line tweak. Say so, rather than let an author
   conclude the compiler is being arbitrary.

## Allocator: the zeroing contract (Workstream R / slice R1) ⚠ NOT BUILT YET

shv2 emits its runtime natively (Workstream R), so the slab allocator is shv2's to
*write*, not to inherit. This section is R1's spec. It is written down now because the
guarantee below is cheap to build in and expensive to retrofit.

> **The allocator ALWAYS returns zeroed memory.** Zeroing is a property of the
> allocator, not a thing each caller is trusted to remember.

**Why this is non-negotiable.** v1 shipped a non-zeroing slab and paid for it three
separate times, each root-caused independently and each "fixed" by bolting a zeroing
loop onto the *caller*: `__gt_spawn`'s GreenThread struct (an uninitialized
`cancel_flag` aborted every overlapped read, so the coordinator declared live workers
dead and the runner hung); socket/`ConnectEx` `OVERLAPPED` contexts (a garbage `hEvent`
suppressed the IOCP completion packet, so a parked green thread never woke); and
`mrt_alloc`'s buffers (a sparsely-filled Map/Set hash table left unwritten slots holding
the previous occupant's bytes, which the element-walk decref'd as live pointers). The
v1 ownership audit named the pattern: *"C# fails SAFE (zero-filled alloc), self-hosted
fails DEADLY."* Every new raw-buffer call site was another chance to reopen the class.

### The model is Go's

| Go | shv2 R1 |
|---|---|
| `mallocgc(size, typ, needzero bool)` | `__slab_alloc` (zeroes) + `__slab_alloc_raw` (does not) |
| `if needzero && span.needzero != 0 { memclrNoHeapPointers(x, size) }` | same rule, same place |
| `mspan.freeindex` (bump cursor) | `mspan.bump_next` |
| `memclrNoHeapPointers` | a `memzero` **size ladder** the backend emits (below) |

A slot reaches a caller from one of two places, and they differ in whether the memory is
dirty:

- **the free list** — a recycled slot, still holding the previous occupant's bytes plus
  the free-list link written into `slot[0]` when it was freed. **Must be memzeroed.**
- **the bump region** `[bump_next, bump_end)` — slots never handed out. Their pages came
  straight from fresh `VirtualAlloc`/`mmap`/`memory.grow`, which every target guarantees
  arrives **zeroed** (Go leans on exactly this: *"sysAlloc obtains a large chunk of
  ZEROED memory from the operating system"*). **Already zero — costs nothing.**

**The load-bearing consequence — do not skip this:**

> Threading an intrusive free list through a fresh span writes a next-pointer into
> `slot[0]` of **every** slot, which **dirties every one of them**. A dirty slot must be
> memzeroed before it can be handed out. So building the free list up front is not merely
> wasted work — **it is the thing that would force a memzero on every first-ever
> allocation.** Leaving the region pristine is the *precondition* for any zero-elision at
> all. **Do not build an eager intrusive free list.**

This is finer-grained than Go, whose `needzero` is per-span: Go re-zeroes even never-used
slots in a span that has seen one free; a per-slot cursor never pays for a slot that was
never dirtied.

**Invariants** (every mutation site must re-establish them):
```
INV-1  free_count == |free_list| + (bump_end - bump_next) / slot_size
INV-2  bump_end   == base_addr + slot_size * total_slots      (derived, never stored)
INV-3  every byte in [bump_next, bump_end) is ZERO
```
The case a reviewer will not believe, so state it: a span can return to mcentral carrying
**both** an unconsumed bump region **and** a populated free list (allocate 3 slots from a
fresh 1024-slot span, free all 3 → `free_count == total_slots` → returned, holding 3
free-list entries and 1021 virgin slots). Both survive; INV-1 holds.

**Decommit/recommit is NOT zero.** If R1 gains a scavenger, recommitted pages must be
re-zeroed: Windows `VirtualAlloc(MEM_COMMIT)` and Linux `MADV_DONTNEED` do zero, but
macOS `MADV_FREE` does **not**, and a wasm no-op decommit leaves contents verbatim. Go
guards this with a per-platform `needZeroAfterSysUnused()`; v1 takes the always-true
branch (eager memzero on reback) because its reback is cold. Either is fine — silently
assuming zero is not.

**If chunks are ever recycled, the chunk-free path must memzero them.** v1's self-hosted
arena never recycles chunks, but the C# bootstrap's arena-large tier does — and there,
`__slab_arena_free_chunks` MUST zero the run it releases, or the bump region's "already
zero" assumption is false. This is the single easiest way to break the design.

### `__slab_alloc_raw` — the escape hatch, and its audit rule

Go's `needzero=false`. Keep the caller set as small as Go does (`rawbyteslice`,
`rawstring`, `growslice`).

> `__slab_alloc_raw` may ONLY be used where the caller provably writes **every byte** of
> the returned region before anything else can read it, **AND** the region is never walked
> as managed pointers. A region is *walked as managed pointers* if a destructor's element
> walk, a Map/Set slot-table teardown, or an `array.set` old-occupant decref will ever run
> over it. Such a buffer **must** come from `__slab_alloc` — a non-zeroed slot read as a
> pointer and decref'd is precisely the bug this design exists to prevent. When in doubt,
> use the zeroing path: it is now cheap.

The canonical legitimate caller is `realloc`: allocate raw, `memcpy` the prefix, zero the
grown tail — together covering every byte. That is Go's `growslice`, and the tail-zero
*is what makes the raw allocation safe*.

### The `memzero` the backend must emit

**Not one instruction — a SIZE LADDER.** The dominant caller zeroes a size-class slot
(8/16/24/32/48/64…), and a naive `rep stosq` there is a large regression: it costs ~20–40
cycles of startup before writing a byte, dwarfing the 1–4 plain stores an 8–32 byte slot
needs. (ERMSB/FSRM improve throughput and short `rep stosb`; they do not remove
`rep stosq`'s setup.)

```
x64    <8 byte loop | 8..63 straight-line overlapping stores
       | 64..255 8-qword store loop | >=256 rep stosq
arm64  same bands with `stp xzr, xzr` (16 B, single uop). No `rep` analogue, so the
       bulk loop IS the large path. No `dc zva` (needs DCZID_EL0, wants 64-byte
       alignment, is disable-able, and buys nothing at the dominant sizes).
wasm   one `memory.fill`. Do NOT unroll small constant sizes: each i64.store is
       independently bounds-checked, whereas one memory.fill is a single check plus an
       engine-tuned memset.
```

Every arm pins a register to the **end** of the region and writes forward from the start
*and* backward from the end, letting the middle be written twice. **Overlapping stores**
cover any length in the band with straight-line code — no loop, and no ragged-tail
handling for lengths that are not multiples of 8. Zeroing a byte twice is free; branching
to decide whether to is not.

If the memzero is a Target-dialect op rather than a raw encoder, it must declare
**`setsFlags: true`** — the ladder `cmp`s the length to pick an arm. (v1's `memcpy` op
declares `false`, correct for a bare `rep movsb`; copying that metadata onto memzero lets
the scheduler hoist a `cmp` feeding a `condBranch` across it — a silent miscompile.)

The v1 implementation of all of the above is `stdlib/Internals.maxon` (the slab) and
`maxon-selfhosted/Compiler/Targets/*/` (`emitX64MemzeroOp`, `arm64EmitMemzeroOp`,
`emitMemzero`) — port the *design*, not the code, since shv2 hand-assembles where v1
compiles Maxon source.

## Parallel driver

**Not built.** Passes are classified `wholeModule`/`perFunction` and the parser is already a pure
function of its file, so both fan-out seams exist; nothing drives them yet.

**The runtime underneath it is proven** (x64-windows). The C# emitter's green-thread scheduler and
sharded allocator run correctly across many worker Ps: a 32-green-thread CPU/alloc burst runs on 16
distinct worker Ps (P0–P15 = ncpu) unclamped and exactly 1 (P0) under `MAXON_MAX_PROCS=1`, both
producing the correct deterministic result. The concurrency primitives that made that testable live
in `maxon-sharp` only (no shared-stdlib change): `__Builtins.cpuCount()`,
`__Builtins.parallelBoundary()` (an empty runtime stub, so a CPU-bound `async` passes E3073), the
`MAXON_MAX_PROCS` clamp in `__gt_init`, and the bisection tools `MAXON_SLAB_GLOBAL_LOCK` /
`MAXON_SLAB_STATS` / `schedMaxActiveWorkers()`. `maxon-shv2/track0/` holds the allocation-torture
harness that exercises the cross-P remote-free MPSC.

**1-core-vs-N-core byte identity is the blocking gate** for the parallel phase when it starts.

## Event log & mm-trace harness

**The producer is `maxon-sharp`'s.** Binaries compiled by `maxon.exe` with `--debugstream` emit a
binary MM event stream: a 128-byte header, 8-byte packed entry headers, a ticket-spinlock reserve.
Four MM codes are produced live — `mm_alloc`/`mm_free`/`mm_incref`/`mm_decref` (`0x01–0x04`) — plus
depth inc/dec (`0x40/41`) around destructor cascades. The Sched subsystem (`0x20–0x2C`) and several
Dbg/raw codes are decoder-only; real scheduler tracing rides the Dbg events (`0x50–0x5E`).
Type-name resolution is automatic: every heap allocation flows through `EmitAlloc`→`EnsureTagIndex`,
whose names land in the PE `.symtab` `MXDS_TAGS` blob, so the monitor prints real type names with no
extra wiring.

> **The DebugStream schema is FROZEN.** New events get new unused type codes; existing codes are
> never reinterpreted.

**The consumer is `maxon monitor [--filter=mm] <exe>`** — it creates the shared segment, spawns the
child with `MAXON_DEBUGSTREAM` set, drains the ring, decodes via a hand-rolled PE parser, and prints
`[+SSSS.mmm] <indent>mm_<verb> <Tag> #<id> [size=|rc=]<n>`. It forwards the child's own stdout, so
trace lines are identified by the `[+…]` prefix.

**The mm-trace spec harness** (in the C# repo): an `<!-- MmTrace -->` marker + an ` ```mm-trace `
block compiles with `DebugStream=true`, runs under `maxon monitor --filter=mm`, **normalizes**, and
compares. Normalization is what makes goldens deterministic: keep only `[+…]` lines, strip the
timestamp and indent, dense-renumber `#<id>` in first-appearance order.

**shv2's own producer is a runtime deliverable (Workstream R)** — it does not exist yet. Two design
notes for it: keep the two-tier gating (compile-time `DebugStream` = zero instructions when off,
runtime `MAXON_DEBUGSTREAM` = `__ds_base == 0`), and **inline the `__ds_base` guard for every event
family** — the C# producer's MM events pay two real CALLs before the runtime-off check, unlike its
Dbg events, and that wart should not be reproduced.

## Spec-test harness

`maxon-shv2 spec-test [dir]` (default `specs-shv2`) is shv2's own spec runner —
`Testing/{SpecParser,SpecTestRunner}.maxon`, compiled by `maxon.exe` like the rest of shv2, so it
can use the full stdlib (File, String, `Subprocess`).

`SpecParser.parseSpecFile` extracts `<!-- test: NAME -->` markers + the ` ```maxon ` block + one
expected block (` ```exitcode ` or ` ```maxoncstderr `) into a `SpecTest{name, source, expectation}`
(`SpecExpectation` is a union — no sentinel). It scans **only the `## Tests` section** (up to the
next `## ` heading): deferred tests live under a marker-less `## Deferred` section, because **HTML
comments do not nest** — `<!-- … <!-- test: … --> … -->` closes at the first `-->`, so a
comment-wrapped deferral would still be run.

`SpecTestRunner.runSpecDir` writes each test's source verbatim (headerless, code at line 1) to a
temp fragment and spawns `<compiler> build` as a **subprocess** through the single `runProcess`
choke point — which isolates a compiler crash to one test and exercises the real CLI. For a
`compilerError` test it normalizes the fragment's absolute path to `<fragment>` (line/col stay
shv2-native) and compares; for an `exitCode` test it runs the produced exe and compares the exit.
`Main` resolves the compiler via `Process.executablePath()` (the runner tests itself), prints
per-test PASS/FAIL and `N passed, M failed`, and **exits non-zero iff any failed**.

**Fragments.** Every run regenerates `specs-shv2/fragments/x64-windows/<spec>/<test>.test` = the test
source + its generated **Target IR** (via `IR/Target/TargetPrinter.maxon` — an exhaustive `match`
over every `TargetOp`, so a new op is a compile error, never a silent `??`), written through
`build --emit-ir`; or the normalized diagnostic for an error test. The fragments are
byte-deterministic and committed, so `git diff` surfaces every codegen change in review.

**Fragment writing is a pure side effect** — it never changes `spec-test`'s pass/fail or exit code.
Fragments are outputs, not gates; that is precisely why the `AllocChecker` exists, and why the
runner spawns each test's `build` with **`--check-alloc`** (see *The `AllocChecker`*). The compile
runs in a subprocess, so that flag is the only channel through which the checker can be armed.
It is on for every test unless the spec or the test explicitly opts out.

## Coverage scaffold

`Compiler/Coverage/CovSiteTable.maxon` carries `BlockSourceInfo` — a per-block source footprint
(file, opening line, deduped statement lines) that `IrModule` records at every block-creation site
and preserves through Maxon→Std lowering. The coverage-instrumentation pass and its site table are a
later deliverable that grows this file; today the metadata is recorded and unused.

---

## Known gaps in the built subsystems

Things that are *implemented but incomplete*, distinct from what simply hasn't been built (for the
latter, see `PLAN.md`):

- **No lexical block scoping.** `Scope`'s `pushScope`/`popScope`/`ownedStack` are unreachable, so a
  `let` inside an `if` body outlives the `if`, and a `var` declared on only one branch is not merged
  at the continuation.
- **`Early`/`Late` operand position is packed but never read** — sound today only because no op has
  both explicit operands and a late implicit clobber.
- **The query dependency graph is recorded but does not drive invalidation** (content hashes do).
- **No spill-slot coalescing** — every spilled value gets its own slot.
- **The splitter recomputes liveness once per split**, and several of its helpers are linear scans
  inside that loop. Correct, but superlinear in a way the rest of the codebase's "no `Map`, no
  hashing in the hot path" discipline avoids. A performance follow-on, not a correctness one.
- **A void `return` in a non-`main` function panics the compiler** — `StdOp` has no void-return
  variant. Currently unreachable, because E3002 rejects a void `main` first.
</content>
</invoke>
