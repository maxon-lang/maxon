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
| `AllocChecker` | Every function of **every** compile is symbolically verified for register correctness (a failure panics the build) |
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
- **The `AllocChecker` runs on every function of every compile** — because spec
  fragments are *outputs*, not gates: the suite would go green on a wrong-but-
  self-consistent allocator. *(→ Register allocator)*
- **Ownership-kind lattice** — `trivial · owned · borrow · shared`, with three
  first-class homes and zero sidetables. Declared, inert until the ownership stage.
  *(→ Own tier)*

---

## Driver and CLI

`Main.maxon` → `Compiler.compile`. Three commands:

| Command | Purpose |
|---|---|
| `maxon-shv2 build <file\|dir> [-o out] [--emit-ir]` | Compile to a PE. A single-file build writes next to the source (`basic.maxon` → `basic.exe`). `--emit-ir` also prints the Target module to `<output>.ir`. |
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
| `foldConstOperands` | Std → Std | constants into immediate operand forms |

`run()` enforces the **error gate**: after each pass, `projectHasErrors → throw CompileError` —
so a program that fails `semanticCheck` never reaches lowering or the backend.

`classifyPass` labels each pass `wholeModule` or `perFunction`. The classification is recorded
but not yet acted on; it is what the per-function fan-out driver will read.

**Everything grows in HERE**, appended after `lowerMaxonToStd`: the Std optimization passes,
the Own-tier passes, and `lowerToMachineForm`. There is no second pipeline over a machine tier,
because there is no machine tier.

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

### The contract: spill where it is cold, error where it would be hot

**A programmer restructuring a hot loop will beat any spiller.** A spiller can only shuffle
values between registers and stack slots; the author can change the *data structure* — hoist the
working set into an array, split the loop, reorder the computation. So when a loop genuinely
does not fit, the compiler does not quietly emit worse code. It **stops and says so precisely**.

- **Cold spilling is free and automatic.** A value with a gap in its uses gets its live range
  *split*: it lives in memory across the gap and in a register where it is used. Around a loop it
  does not touch, that is a store in the preheader and a reload after — **zero instructions added
  to the loop body**.
- **Hot spilling is `E5001`.** If a value the loop *uses* would have to be evicted and reloaded
  inside that loop, the compiler reports it instead. No backtracking, no eviction tournament, no
  spill-cost model.
- **One shot.** liveness → split → color. There is no reactive spill/color iteration.

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
2. **The compiler may therefore never waste a register, because a wasted register *is* a false
   positive.** This is what promotes 3-operand `lea`/`imul`, immediate operands,
   `foldConstOperands`, rematerialization, biased coloring, and copy-free ISel from
   "optimizations" to **contract obligations**. Any of them showing up as the cause of a blocking
   set is a defect, not a tuning opportunity.
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

> **RULE 2 (spill placement).** A store or reload may be inserted at a point `q` only if, for
> every loop containing `q`, the value has **no use or def inside that loop**. Consequently spill
> code is never added to a loop body for a value that loop uses. Straight-line (depth-0) code has
> no such loop, so Belady eviction there is unrestricted — the reload executes once.
>
> If, after splitting out every value idle across loop `L`, the pressure inside `L` still exceeds
> the pool → **`E5001`**.

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
| 1 | **Split** | `SplitLiveRanges.maxon` | Cold-spill live-range splitting. Mutates the IR and returns the final `LivenessResult`. |
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
block-arg↔branch-arg pairs and from reuse defs collapse copy-related values into one register, so
a loop's back-edge copy elides instead of landing IN the loop, and one loop-carried value is never
counted as two. Without it, the pressure model would over-count every accumulator loop — and
over-counting is what produces a FALSE pressure error, the worst bug this compiler can have (it
sends an author to "fix" correct code). The pressure decision is therefore made on the true
per-point maxlive **after** biased coloring, never on a cardinality gate.

Register preference is **caller-saved first**, so leaf and call-free code never touches a
callee-saved register and never pays a prologue push.

### Spilling: cold-spill live-range splitting

`SplitLiveRanges.maxon` runs *before* coloring and mutates the IR. Its loop is: find the peak →
choose a victim → remat or spill → recompute liveness → repeat, with a runaway bound and a
post-condition assert that no point still exceeds its pool.

**A point's pool is its own, not the global one.** `reducedPoolSizeAt(op) = popcount(pool ∖
op.implicitDefs)` — so a value live across a `callDirect` competes for the **5 callee-saved**
registers, and one live across an `idiv` for `pool ∖ {rax, rdx}`. Effective pressure at a point
also corrects for the reuse-copy transient (+1 when the reuse input outlives the op) and for dead
phis, so the peak-finder and the feasibility guard agree on one number.

**Victim choice** is remat-first, then Belady/MIN:
- **Rematerializable** values (a constant def, not a phi, not edge-passed) are re-emitted at each
  after-peak use rather than spilled — always preferred.
- Otherwise the **farthest next use** wins, gated on being **cold-spillable**: the def and every
  use must be at **loop depth 0**.

**Split shape: Belady split at the eviction point + dominating reloads.** Store after the last
before-peak use (or after the def), then **one reload per after-peak use-block**, placed so it
dominates its uses — each reload defines a **fresh `ValueId`**, so SSA is preserved and no phi or
SSA reconstruction is needed. (This is what retires v1's SplitKit failure.) An assert panics if a
store or reload would ever land at loop depth ≠ 0 — so **a loop body that does not use a spilled
value is byte-identical to the un-spilled version**.

**Hot overflow — a loop that genuinely uses more values than fit — is `E5001`** (below). An assert
also fires if a store or reload would ever land at loop depth ≠ 0: that would be a hot spill that
should have been reported instead, i.e. a splitter bug.

### `E5001` — the register-pressure diagnostic

`Targets/Shared/RegisterPressureDiagnostic.maxon`. **The decision has already been made when this
runs**: the splitter relieved every idle value and rematerialized every constant, and `chooseVictim`
still found no value crossing the peak that can be moved. What remains is the loop's true working
set against the registers available AT that point. This file does not re-decide feasibility — it
turns an already-exact decision into a source-mapped message, so **it cannot add a false positive**.

The message reports:
- **The exact deficit** — "remove 3 of these 17 values", not "too many".
- **The constrained register count that actually applies.** A value live across a **call** can only
  sit in one of the 5 callee-saved registers, so at a call the effective pool is 5, not 14 — and an
  `idiv` reserves RAX/RDX. Reporting the nominal 14 would be actively misleading to a consumer that
  acts on it literally.
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
entry — and asserts that every use reads the register holding its value.

**It runs on EVERY function of EVERY compile** (a failure panics the build → fails the test),
because `SpecTestRunner` treats fragments as **outputs, not gates**: the suite would otherwise go
green on a wrong-but-self-consistent allocator. This is the real correctness gate, and it has
caught real silent miscompiles that the full suite passed.

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

### Known limits of the design

1. **The only source of a false `E5001` is a wasted register, and copy-related values are the one
   that bites.** `maxlive` is exact for the program as lowered (chordal ⇒ χ = ω = maxlive), and
   liveness is per-program-point, so values live on disjoint paths correctly do **not** interfere.
   But two values that are *copies of each other* — a block arg and what the back edge passes it —
   hold the same value and are still counted twice. **Biased coloring is what collapses them**, and
   without it a loop would be told it needs one more register than it does. Every other
   waste-a-register path is likewise a contract bug, not a limitation: a literal in a register (→
   immediates, `foldConstOperands`), a witness table in a register (→ remat), a redundant
   two-address copy (→ `lea`, `Reuse`).
2. **Fixed-register points reduce the effective pool locally, so `maxPressure ≤ pool` is necessary
   but NOT sufficient.** Both the splitter's peak-finder and the `E5001` deficit are therefore
   computed per point against the *reduced* pool (`popcount(pool ∖ implicitDefs)`), never the
   nominal 14.
3. **There is always a rewrite, but sometimes it is a real restructuring.** Register pressure is
   always reducible by hand-spilling into memory: twenty accumulators become one array, and pressure
   collapses to `{base, index, temp}`. The floor is set by the most demanding single operation,
   which on x64 is 3–4 registers — far below 14. But the honest version is that the fix for a
   genuinely hot loop (a SHA-256 compression round wants ~24 live values) is the same restructuring
   a real implementation would already do — not a one-line tweak. Say so, rather than let an author
   conclude the compiler is being arbitrary.

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
Fragments are outputs, not gates; that is precisely why the `AllocChecker` exists.

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
