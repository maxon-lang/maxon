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

- `function name(p1 T, p2 T) returns T … end` — **any number of parameters up to
  `MaxAbiArgs` = 64**, no defaults. Parameters are immutable value bindings. The first six of
  each register FILE travel in registers (eight on arm64); the rest travel in STACK SLOTS.
  See *Stack arguments* below. The ceiling is the width of the per-argument float mask both
  ends of a call route through, and it is DIAGNOSED — it is not a register count.
- **Calls** — first argument positional, every later argument labelled
  (`f(a, b: x)`); call expressions and bare-call statements; recursion.
- `let` / `var` bindings, `var` reassignment, integer literals, identifiers.
- **Top-level `let` and `var`** — a module-scope `let` is a compile-time constant that INLINES
  at every use; a module-scope `var` is a real `.data` slot that loads and stores. Both take
  constant-only initializers (no calls), evaluated once by the declaration sweep. EITHER may be
  MANAGED — a `String` or a `b"…"` byte-string literal: a `let` borrows the literal's own record, and a
  `var` gets an 8-byte POINTER slot filled by `__module_init` before `main` and released by
  `__maxon_global_cleanup` after it. An `[…]` ARRAY literal takes that slot whichever keyword declared it —
  its record is BUILT, so no read can re-materialize one — and `let` then differs only in refusing the write.
  A binding no code NAMES is dropped entirely — slot, init and cleanup together — while one that is only
  WRITTEN survives, since a write is the same evidence a read is.
  See *Top-level managed `let`* / *Top-level managed `var`* / *Top-level ARRAY globals* /
  *Dead-global elimination* below.
- **Arithmetic** — `+ - * / mod` with Pratt precedence, prefix `-`.
- **Comparisons** — `== != < > <= >=`, valid *only* as the sole top-level operator
  of an `if`/`while` condition (there is no `setcc`/bool materialization yet, so a
  comparison in value position would read stale flags — the parser rejects it).
- **Control flow** — `if`/`else if`/`else`, `while` with labelled `break`/`continue`,
  `return`.
- Types: the `int`/`bool`/`float` keywords and the `ExitCode` builtin alias. No
  user types.

Everything outside that slice is rejected with a positioned diagnostic. The E-code
registry is `docs/error-codes.txt` at the repo root — the single source of truth for
all three compilers — and `Compiler/ErrorCodeRegistry.maxon` is GENERATED from it by
`maxon error-codes generate`. `E2015` is the catch-all for "unsupported construct".
(It used to be `E3010`, a *semantic*-band number that the other two compilers spend on
`SemanticUnneededCast`: shv2 kept its own copy of the number space, so the same number
meant two things.)

**Not built yet:** heap/ownership/drops, structs, generics, interfaces,
strings/arrays/maps, floats in codegen, error handling, the runtime shv2 must
*emit* (Workstream R), the parallel compilation driver, arm64/wasm. See `PLAN.md`.

**The gate battery** (all four must be green before a commit):
| Gate | What it proves |
|---|---|
| `maxon-shv2 spec-test` — the RUN | **314 passing / 0 failing** over `specs-shv2/*.md`. Each test compiles a program, **runs** it, and asserts its exit code — so an allocation that leaves a value in the wrong register computes the wrong answer and FAILS. This is the correctness gate on the register allocator. |
| `specs-shv2/fragments/x64-windows/**` — the GOLDENS | Committed Target-IR goldens, **compared** by the same `spec-test` run (`SpecTestRunner.checkTestFragment`) and **REPORTED, NEVER FAILED**: a difference prints as reference drift and contributes nothing to the failed count or the exit code (⚖ user ruling 2026-08-02 — *"the goldens are NOT supposed to be a gate, they are just for reference"*; the gate it used to be hid nine real x64-linux failures inside a wall of bookkeeping, PLAN row `X5`). They remain the only record of whether codegen got *worse* — an extra spill, a lost coalesce, a needlessly widened live range all still return the right answer — and they reach where the run cannot: one execution takes ONE path, the golden records EVERY block. `--update-required` regenerates them, and that diff is the review. |
| `maxon-shv2 verify-warm-rebuild <file\|dir>` | Compile determinism (byte-identical) + query-spine incrementality (content-hash cache hit) + **invalidation** (four probes: a bytes-only edit re-parses 1 file; a declaration edit re-parses all; a `let`'s VALUE edit re-parses all; a `var`'s value edit re-parses **1**) |
| ```RequiredData``` blocks — the **BINARY** | The `.data` section read back OUT of the linked PE and byte-compared (`Testing/PeSectionReader.maxon`, a port of the C# runner's `CheckRequiredData`). The only gate that looks at what was actually LINKED rather than at what the compiler meant: it catches a section header with a wrong RVA or a raw pointer off by an alignment, which no exit code and no IR dump can see. ⚠ It was **silently ignored** until P1.0d.5b — six `static-variables` cases carried one and would have passed on their exit code alone while claiming to check their layout. |

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
- **A generic instance's COMPILED NAME and the user's declared type names are DISJOINT NAMESPACES** —
  and disjoint *by construction*, not by a check. Every per-type symbol the backend emits is derived
  from one string (`__destruct_<name>`, `__layout_<name>`), and a declared `type Box_String` derives
  its own from the identical one; so when the name an instantiation would compile to is already a
  declared `type`/`enum`/`union`, `ProgramSignatures.mangleGenericInstance` mints it behind the
  reserved `__` prefix instead — a space E2051 bars every declaration from. The prefix is applied at
  the SOLE PRODUCER of the name and not at a later check, because the compiled name is *baked into
  emitted code*: the parser's `decrefCalleeFor` writes `__destruct_<mangled>` into a scope-exit drop
  call, so a name revised after the parse would leave `main` calling a symbol nothing emits. It is
  applied ON CONTEST ONLY, so no name that ever compiled moves and no golden does either. *(→
  Parse-staging)*
- **A SYMBOL BUILT BY JOINING TWO NAMES JOINS THEM WITH A CHARACTER NO NAME CAN HOLD** — the rule the
  invariant above does not cover, because that one is about a name CONTESTED by a declaration and this
  one is about two joins that spell each other. The lexer admits only `[A-Za-z0-9_]` inside an
  identifier (`Lexer.isAlphaNum`), so `_` is *in* the alphabet the components are drawn from and an
  `_`-join is **not injective**: `__witness_A_B_C` was both `(A_B, C)` and `(A, B_C)`, and since the
  witness mint MEMOIZES ON THE LABEL, the second pair did not contest the first — it silently BECAME
  it and dispatched every method slot to the other conformer's impl (measured: exit 0, no diagnostic,
  33 where the answer is 43). The witness label therefore joins with **`.`**
  (`IrInterface.witnessTableLabel`), which is injective by CHARACTER CLASS rather than by a decoding
  algorithm — the same construction `__` gives the reserved space above, one alphabet lower down.
  ⚠ **The instance mangler (`Base_Arg_Arg`) is the counter-example that must stay one**: it joins with
  `_`, is deliberately *not* injective, and is covered instead by `checkTypeSymbolNamespace`'s **E3006**
  — a front-end check it can afford because both claimants are interned declarations with source
  anchors. A witness table has neither: it is minted during LOWERING, from a pair with no `typealias`
  to blame, so it has no diagnostic available and the separator has to carry the whole property.
  *(→ Parse-staging for the E3006 half; `IrInterface.witnessTableLabel` for the argument in full)*
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
- **The allocator is gated by the SUITE, not by a pass inside the compiler** — running a
  spec test proves the allocation is CORRECT (a wrong register → a wrong exit code). That
  run **is** the gate; the committed `.test` goldens are the *reference* that shows whether
  it got WORSE (they are *compared* and *reported*, they record every block of every
  function, and they fail nothing). There is deliberately no in-compiler allocation
  verifier. *(→ Register allocator)*
- **Ownership-kind lattice** — `trivial · owned · borrow · shared`, with three
  first-class homes and zero sidetables. Declared, inert until the ownership stage.
  *(→ Own tier)*

---

## Driver and CLI

`Main.maxon` → `Compiler.compile`. Three commands:

| Command | Purpose |
|---|---|
| `maxon-shv2 build <file\|dir> [-o out] [--emit-ir]` | Compile to a PE. A single-file build writes next to the source (`basic.maxon` → `basic.exe`). `--emit-ir` also prints the Target module to `<output>.ir`. |
| `maxon-shv2 spec-test [dir] [--update-required]` | Run the spec suite (default `specs-shv2`): compile each test, run it, and check its exit code — **that** is what decides the exit code. It also compares the Target IR against the committed golden and *reports* any drift, which fails nothing. `--update-required` rewrites the goldens instead of comparing them. |
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

### ⭐ A REFUSAL RAISED INSIDE `stdlib/` IS BLAMED AT THE USER'S CONSTRUCT (`LibraryBlame`)

`stdlib/` is ordinary compiled source, so a refusal a user's program forces can be
*raised* at a line no user wrote — and every member struck from a builtin's synthesized
roster **relocates** its refusals out of a dispatch arm and into the library body that
now serves them. ⚖ **User ruling:** such a diagnostic reports the **user's construct as
its primary location** and keeps the library line as a **`note:` second line**. Losing
the library line is not the fix; it is what makes a *library* bug diagnosable.

The mechanism is one path with one applier, and the three parts are deliberately apart:

1. **Produced** by whichever gate consulted the user-supplied fact. Only
   `Parser.requireOpaqueArrayCopyable` does today: it reads the INSTANCE REGISTRY, so
   the construct it is about is an *instantiation*, and
   `ProgramSignatures.instantiationSiteOf` maps the offending instance back to where the
   program wrote it. An instance the program never wrote — one substituted into a
   generic body's inner `typealias` — is followed one hop further through
   `substitutedInstanceOrigins` to the enclosing instantiation that minted it.
2. **Carried** on `Parser.libraryBlame`, which the parse-failure arm of `queryParseOps`
   reads. The parser stays PURE: a blame is derived from `signatures`, never from
   `Project`. `Queries.blameOutsideTheLibrary` then drops a blame whose own file is also
   `stdlib/` — the parser cannot ask that question, and one library line blaming another
   is no improvement.
3. **Applied** by `reportDiagnostic`, the single writer of `project.diagnostics`, through
   `Diagnostic.blamedAt` — the ONE swap. `project.libraryBlame` is ambient rather than a
   parameter because the front-end mappers reach that sink through ~200
   `reportParseError` match arms; it is armed for exactly one reporting call.

An instance with no written origin at all keeps the raise site: precision is lost, never
the refusal.

## Query spine (incremental)

`QueryDatabase` / `QueryEngine` / `Queries` implement
`querySourceFile → queryTokens → queryProgramSignatures → queryParseOps → queryAllModule`.

**Memo validity is a content-hash compare, not a revision-counter walk.**
`fileChanged` computes an FNV-1a `ContentHash` per file — there is no shared
`currentRevision` counter for parallel queries to contend on. Each per-file memo
stamps the `keyHash` it was derived from and is valid iff that still equals the file's
current hash; `queryAllModule` keys its merged-module memo on the **composite** hash
folded over every file's hash in source-path order.

### ⭐ A WHOLE-PROGRAM query sits UNDERNEATH a per-file one, and that shapes everything below it

`queryProgramSignatures` (`SignatureIndex.maxon`) sweeps **every file's tokens** for every function
declaration, before any file is parsed. It has to: a call's result type is a decision the **parser**
cannot postpone — a bool `and`/`or` is short-circuit **control flow**, and `not` picks its opcode from
its operand's type — and the callee may be in another file. It reads tokens and never parses, so the
chain stays acyclic.

#### ⭐⭐ IT PUBLISHES PARAMETER-TYPE SPELLINGS, SO A CALL READS THE RETURN TYPE OF THE OVERLOAD IT *MEANS*

The index keeps ONE return type per registration key, LAST-WINS over the declarations wearing it — which is
exact for every name whose declarations agree, and is a **wrong answer** for one whose declarations do not.
A call's result type is fixed while its own file is parsed (it decides the machine type of the result, its
register file, and whether a scope-exit drop is enrolled for it) while the member is chosen a whole pass
later by `SemanticCheck.resolveOverloadedCalls`. **MEASURED, both directions:** a void overload beside a
value one compiled clean and died with an ACCESS VIOLATION where the drop was spent on a register the callee
never wrote; the mirror LEAKED.

So the sweep also publishes, per DECLARATION, its own return type and its **parameter-type spellings**
(`ProgramSignatures.overloadedDeclsOf`), and `Parser.overloadedCallResultType` asks which member a call means
before it types the result. Three properties make that safe to have:

- **It is asked only where the members DISAGREE** about what they return, so every call to a name whose
  declarations agree — which is every call in every program the corpus compiles — is typed by the by-name
  answer exactly as before, and emits the same bytes. ⚠ **That is a claim about AGREEING sets and not about
  every program that compiled.** A set that disagrees and compiled anyway did so because
  `reconcileOverloadResultType` RETYPED the stale scalar tag after the fact; such a call is now typed
  correctly at the parse instead, so its path — and possibly its emitted code — moves. It is a small
  population (a disagreement between two plain scalars) and it is the population this door exists to fix.
- **It is CHECKED, not trusted.** `SemanticCheck.reconcileOverloadResultType` re-asks against the member
  resolution actually chose and refuses the program if the two types diverge irreparably — so a wrong pick is
  a diagnostic, never a wrong answer.
- **It DECLINES loudly** where it cannot settle the question: it reads a parameter type only when the source
  spells it as a single type NAME (reading a compound one REGISTERS it whole-program, and every registration
  the sweep makes moves the `GenericInstanceId` of every later one — which decides mangled instance names;
  the sweep already does this for a RETURN clause, so this is a blast-radius argument and not a rule), and it
  will not choose between two members that fit equally. Both fall back to the by-name answer and to the refusal that has
  always guarded it.

It also makes a name declared by **two files of one directory** an overload set rather than a duplicate
(`Parser.overloadRegistrationNameFor`'s contest, the free-function twin of `contestedExtensionMethods`), so
two modules of one library may each carry a private helper of the same name. `specs-shv2/cross-file-overload-set.md`.

**A PARSE THEREFORE READS TWO INPUTS, AND ITS MEMO IS KEYED ON BOTH:**
`ParseMemo.keyHash = mix(fileContentHash, ProgramSignatures.hash)`. Keying on the file's own bytes
alone — which is what shipped — serves a **stale parse** when another file's return type changes: the
file did not change, but what it *means* did. The index's hash covers the **DECLARATIONS**, not the
sources they were read from, so editing a function *body* re-parses only that file, while editing a
*return type* re-parses every file. Both directions are gated by `verify-warm-rebuild`'s
**invalidation** property.

⚠ **The standing hazard of that inversion is COST.** A whole-program query under a per-file one is
asked *once per file*, so anything O(files) inside it becomes O(files²). Two such quadratics were found
this way (both pre-existing, both exposed by the extra call): `clearDepsFor` rebuilding a write-only
flat edge array, and `compositeSourceHash` re-folding every file's hash on every call. Both are now
O(1)/memoized. **Before adding work to a whole-program query, ask what it costs × the file count.**

`queryParseOps` produces a `FileParseArtifact` and touches no `Project` state (the
parser is pure). `queryAllModule` on a miss calls `resetMergeTargets` and re-folds the
artifacts through `mergeArtifact` — so there is **no rollback machinery**: artifacts are
the source of truth, and the derived registries are rebuilt from them.

Failure is never cached (a failed tokenize/parse, or a parse that merely reported a
diagnostic, returns an empty result uncached), so a fixed file recompiles cleanly.

**There is no dependency graph, by design.** A memo names its own inputs in the one function
that computes its key (`parseMemoKey`), so a dependency is *derived* from the input rather
than *recorded* as it is read — which is exactly what lets per-file queries run in parallel
with no shared mutable bookkeeping.

One was recorded anyway (`depIndex` / `recordDependency` / `clearDepsFor` / `activeQueryStack`),
kept against the day "a query genuinely depends on another file's *derived* state, so the wiring
is already there". **That day came — and the wiring was not used.** `queryParseOps` came to depend
on `queryProgramSignatures`, precisely that case, and the answer was to mix the index's hash into
the parse memo's key (`232f6c80a`); nothing consulted the graph, which by then had been extended
with a fresh edge for a consumer that did not exist. Nothing ever read a bucket, so nothing ever
validated one — a graph nobody reads is not wiring, it is unpaid bugs plus a `String` render and
two `Map` writes per edge on the hot path. It went the way its own flat-array half went one commit
earlier. If a query ever needs a dynamic edge, add the index **with its reader, in one commit**.

#### ⭐ MIX WHAT A READER COPIES — and a `var` does not copy its value

The index's hash keys every parse memo, so the rule for what rides it is exactly *"what does a
reader COPY out of this declaration?"* — and `let` and `var` answer it **oppositely**, which is the
one place they genuinely diverge:

- A **`let`'s VALUE** is copied into every reader (a use becomes a `literal` op carrying the
  number), so `let X = 7` → `8` changes what other files COMPILE TO while their own bytes never
  move. Only this hash can carry that.
- A **`var`'s value is copied into NOTHING.** A reader emits `globalAddr __data_X` +
  `loadIndirect`, byte-identical whichever number the initializer held; the value reaches only the
  `.data` image, which is rebuilt from the arena whenever the signature query misses. What a var's
  reader DOES depend on is its **TAG** (an `i1` moves one byte, an `i64` eight) and its **LABEL**
  (the slot the emitted `globalAddr` names) — so those ride, and the payload does not.

Mixing a var's payload too would be **SOUND and WRONG**: every keystroke on any global's initial
value would re-parse the whole program — the "sound, and useless" composite-source-hash key wearing
a different hat. **MEASURED:** with the payload mixed, `verify-warm-rebuild`'s var probe reports
`expected 1, got 3` while its other three properties stay green, because a pessimal key re-parses
everything for everything and none of them can tell that from a correct one.

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

`Scope` tracks value bindings (name → current SSA `ValueId` + mutability) via
`declareValueBinding` / `setValue` / `lookupValue`, and it enforces **lexical block
scoping**: `parseBlockBody` opens a FRAME around every `if` / `else` / `while` body, so a
binding declared inside a block is gone at its `end` (`specs/block-scoping.md`).

A frame is a **MARK into flat declaration stacks**, not a stack of per-frame arrays:
`pushScope` pushes the current stack height — O(1), and with no heap object per frame,
which matters because a block is the most numerous scope in any real program — and
`popScope` drains back to it, still O(declarations-in-this-frame). An empty mark stack
means function scope.

Two consequences are load-bearing, and both exist because **shadowing is legal**:

- **`popScope` RESTORES, it does not just remove.** `vars` is keyed by name, so an inner
  `let x` overwrites the outer `x`'s entry; deleting the key at scope exit would delete
  the outer binding too. A declaration that DISPLACES one pushes the displaced `VarInfo`
  onto `shadowedInfos`, and the frame is drained in **reverse** so a name declared twice
  in one frame unwinds through its own chain.
- **Bindings are mutated IN PLACE, not replaced.** `VarInfo` is a heap `type`, so `vars`
  stores a reference and `setValue` writes `boundValue` through it. That is what lets the
  parser hold a **direct reference** to a mutable binding — its loop-phi list
  (`LoopPhiVar.binding`) and its merge snapshots do — and still address the right binding
  when a nested block shadows the NAME. A name-keyed rebind cannot: it always resolves to
  the innermost binding, so a `break` out of a block that shadows a loop-carried `var`
  would carry the SHADOW's value into the loop's exit phi. A silent miscompile.

**The ownership hook lives in `popScope`** (M6): draining a frame already resolves each
declaration back to the `VarInfo` leaving scope, and that record carries both the
`ownership` kind to test and the type + slot a drop must name — so `own.drop` /
`own.release` are emitted right there, with no parallel "owned names" list to keep in
lockstep. At M1 every binding is `trivial`, so the hook never fires.

### Parse-staging

`Compiler/ParseStaging.maxon` owns `FileParseArtifact` + `mergeArtifact(project, target,
artifact)`, **the single writer** of the shared `Project` registries. `mergeArtifact`:

1. folds the artifact's local interner into `project.typeNames` (`TypeNameInterner.foldInto`
   → a `TypeNameRemap`), and if the fold moved ids, `remapArtifact` rewrites the artifact's
   `named(id)` references;
2. offset-merges the `MaxonModule` fragment into the accumulator (`IrModule.merge`);
3. appends the artifact's `SourceRangeTable` in lockstep (asserting the op counts agree);
4. commits each registry contribution — `funcReturnTypes` (upsert) and `funcSignatures`
   (which carries the **whole-program duplicate-function check**, `E3006`).

Because the parser wrote nothing to `Project` speculatively, there is no ParseDelta
rollback dance. Each new registry family arrives the same way: an ordered contribution
array on the artifact, folded here.

**Interner note:** for a *cached* artifact, a non-identity remap must not mutate the cache
in place. The remedy is not "clone the artifact first" — it is to build the rewritten data
fresh and assign it, which is what `remapArtifact` already does for `maxonParamTypes`.

**The memo is shared, so nothing may be mutated in place.** `queryAllModule` returns the
SAME `MaxonModule` object on every cache hit, and the merge aliases each artifact's phi
arrays into it (`relocateIrBlock`) rather than copying them. The tiers then share that one
phi model rather than deep-copying it at each boundary — a Std block's `blockArgs`/
`branchEdges` ARE the Maxon block's, and a Target block shares the `ValueIdArray` inside
each edge. So an in-place write to any of them corrupts the memo for every later compile,
silently: nothing downstream re-reads the Maxon or Std phi model to notice.

The rule, stated once here and enforced by convention in `IrBlock`'s header: **to change
the phi model, build a fresh array and assign the field — never mutate in place.** Every
pass obeys it (`pruneDeadBlockArgs`, `extractPredEdge`, `clearPhiModel` all rebuild-and-
assign). The single in-place writer in the compiler, the splitter's `rewriteEdgeArgsIn`,
de-aliases first: it copies an edge's `argIds` before its first write to it. That
copy-on-write is the compiler's ONE remaining `.clone()` — independent mutation being the
only thing Maxon's reference semantics make a clone necessary for.

The `Project` registry set today is small: `funcReturnTypes`, `funcSignatures`
(param names + types), `typeNames`, `opRanges`, plus `diagnostics`, `globalData`, `db`,
`rootPath`, `target`. It grows toward v1's ~28 as the language does.

#### The compiled type-name namespace (`checkTypeSymbolNamespace`)

`ParseStaging` also owns the whole-program check over the names generic instantiations compile
to. It has **one** job left, because the other half is now impossible:

- **A declaration contesting an instance cannot happen** — `mangleGenericInstance` mints a
  contested name behind `__` (the Core invariant above). Reporting it was a defect, not a rule:
  an `[Foo…]` array literal interns `Array with Foo` without naming it, so a legal `type Array_Foo`
  was rejected with a diagnostic naming an instantiation absent from the program's source. The
  bootstrap has never had the contest to diagnose (its instances are `__Array_Foo`); v1 has it and
  tolerates the duplicate label silently, first-write-wins, which is the wild free the check
  existed to stop.
- **Two INSTANTIATIONS compiling to one name is still `E3006`**, and a prefix cannot cure it —
  there is no declaration to move aside. `_` is a legal name character and the join has no
  escaping, so `Pair with (Box_Int, Str)` and `Pair with (Box, Int_Str)` both give
  `Pair_Box_Int_Str` (measured before the check existed: exit 0, no diagnostic, **SIGSEGV**).
  **Neither reference compiler diagnoses this**; it is shv2's own.

The walk is in gid order — intern order, hence source-path order — so which of a colliding pair is
the newcomer is a property of the program, not of a map's iteration; and a rejected newcomer never
displaces the incumbent, so a third instantiation is reported against the claimant that *settled*
the name rather than one already refused.

## Maxon dialect

`IR/Maxon/MaxonDialect.maxon` — the surface-level IR the parser emits.

**`MaxonOp`** (in band order; `OpCategory` bands `callFree` … `plain`, with
`callMethod`/`panicking`/`varAccess`/`awaiting`/`closureProducer`/`ownership` declared but
empty):

| Band | Variants |
|---|---|
| `callFree` | `call(result, callee, args, argLabels, argRanges)` |
| `plain` | `literal`, `ret`, `retVoid`, `binOp`, `unaryOp`, `compare`, `condBranch`, `branch` |
| `varAccess` | `globalAddr`, `loadIndirect`, `storeIndirect` |

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
- **Call validation** against `funcSignatures` — **E3004** unknown function, **E3036** arity,
  **E3037** unknown argument label, **E3038** duplicate argument — via the shared
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
| `call` | `ret` (role `ret`), `param` (role `param`), `call`, `retVoid` (role `ret`) |
| `memory` | `globalAddr`, `loadIndirect`, `storeIndirect` |
| `system` · `sugar` | *(empty)* |

Flat does **not** mean one variant per opcode: a family sharing an operand shape
(`add`/`sub`/`mul`) stays one variant with an opcode field, but a member needing *different
metadata* splits out — because the `StdOpMeta` backing attaches per variant and cannot reach
an opcode buried in a field. That is why `cmp` is its own variant (`isCmp: true`) and
`div`/`mod` are their own (`isPure: false` — `idiv` traps — plus fixed-register lowering).
`const` carries a `StdType` rather than v1's `constI64`/`constF64` opcode pair: it subsumes
MIR's `isFloat` bool and makes i32/u8/f32 representable without new opcodes. And `retVoid`
is its own variant rather than a `ret` carrying an absent-value `ValueId` — the Maxon tier
splits it for the same reason (below): a sentinel operand would be READ as a real use by
`collectStdOpUses`, either holding a dead value live or naming one that was never defined.

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

### The memory band, and the one metadata bit that is a correctness constraint

`globalAddr` · `loadIndirect` · `storeIndirect` — v1's shape verbatim
(`maxon-selfhosted/Compiler/Runtime/runtime.std:30`). **THREE ops, not a fused
`globalLoad`/`globalStore`:** a global's read is an address then a load, which is the same general
pair a struct field (P1.1) and a heap value (P1.2) need, reached one milestone early rather than as
a special case that has to be undone. shv2 SKIPS v1's `unresolvedRead`/`unresolvedAssign`
placeholder indirection entirely — v1 needs it because its parser cannot know what a bare name is
until a later pass, and shv2's `SignatureIndex` has that answer before any file is parsed.

Both `loadIndirect` and `storeIndirect` carry an `StdType` where v1 carried
`(width, signed, isFloat)`: shv2's `StdType` already encodes all three, and its
`StdTypeInfo.storageBytes` backing is the single source of a type's memory footprint (an `i1` is
ONE byte, which is what `data-section-bool-1byte` pins).

> ⚠ **`loadIndirect` declares `isPure: FALSE`, and that is the CORRECT declaration — but it is a
> declaration AWAITING ITS READER, not a constraint anything enforces today.** A load writes
> nothing, so calling it "side-effect-free" is true and useless: `isPure` licenses a pass to
> DUPLICATE, REORDER or DROP an op, and a load may do none of those — what it reads is a mutable
> location some other op writes. Declared pure, a global's read *would* hoist out of a loop that
> writes the global and the loop *would* read a stale value for ever: a **silent wrong answer**, not
> a crash. That is why the value must stay `false`.
>
> ⚠ **BUT `StdOpMeta.isPure` HAS ZERO READERS** (measured 2026-07-15: a per-field sweep of all nine
> `StdOpMeta` fields, plus a SABOTAGE — with `loadIndirect` flipped to `isPure: true`, `specs-shv2`
> still passes **371/0, exit 0**). Its readers — DCE/CSE/LICM and the inliner — are **scheduled, not
> present**: the pipeline is `resolveTypes → semanticCheck → lowerMaxonToStd → pruneDeadBlockArgs →
> elimTrivialBlockArgs → foldConstOperands`, and **nothing in it hoists**. So the no-hoist property
> holds today **because there is no hoister**, not because of this flag.
> `specs-shv2/global-load-not-hoisted.md` and `struct-field-load-not-hoisted.md` are **STANDING
> GUARDS that will earn their keep when the first hoisting pass lands** — they pin the ANSWER, which
> is worth pinning, but they are not gates on the flag and structurally cannot be. Keep the
> declaration correct; do not mistake it for something enforced. **See OPEN.md #27.**
>
> `globalAddr` is `isPure: true` and `isMemory: false` — it computes an address (a RIP-relative
> `lea` reading no register at all) rather than touching memory.

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

**The front end used to over-produce phis, and the surplus is what a FALSE `E5001` was made of.**
On-the-fly SSA must mint a loop header's phis *before* it parses the body, and it once minted one
per mutable var **in scope** — so every `var` declared before a loop got a loop-carried phi,
including vars the loop never touches and vars already dead when the loop is reached.

⚠ **It does not any more.** `Parser.parseWhileStatement` reads the loop's **assigned names** off
the TOKENS before emitting anything and carries a phi only for those. The old rule was *quadratic
in the program* — a function's `var`s accumulate, so the Nth loop minted ~N phis and burned a
ValueId on each, and `blockArgIdBound` (with every dense column sized by it) is O(ValueIds), so
phis that were deleted three passes later were still paid for in bytes by every pass in between.
Measured on the scale corpus: `pruneDeadBlockArgs` bytes grew **x2.17** per doubling and now grow
**x1.99**; eight sequential pressured loops minted **332** header phis of which **260** were
surplus, and now mint exactly **72**.

**Both Std passes below stay, on a strictly smaller input**, because "which names does the loop
assign" is a question about tokens and "is this phi read / does it carry anything" is a question
about the IR the parser has not finished building. The front end no longer over-produces; these
delete what is genuinely dead or genuinely trivial.

A phi for a var the loop never reads is not merely useless, it is **self-sustaining**: the back
edge passes it to itself, so it *has* a use, and liveness holds it live around the **entire
loop**. Two sequential loops are enough — every accumulator of the first loop reappears as a
dead phi in the second loop's header. Then `maxlive` inflates by one per dead var, the splitter
**forced-spills them around the second loop's call** (a store *and* a reload every iteration,
for values nothing reads), and past the pool of 14 the compiler raises `E5001` against a program
that fits the machine comfortably — ranking the dead phis first among the values to delete,
described as "read 0 times in the loop". Which they were. They were read nowhere, and written
nowhere either.

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
eliminateDeadFunctions(stdModule) → prune what no root reaches
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

`X64PrologueEpilogue` gives **every function a real frame pointer** — `push rbp` / `mov rbp,
rsp` opens the prologue and `pop rbp` closes the epilogue — then pushes and pops **exactly**
the callee-saved registers the coloring actually used, and reserves an aligned frame (32-byte
shadow space + spill slots + parity padding, so `rsp ≡ 0 mod 16` at a call). The frame
pointer's push counts toward that parity, so it *inverts* the residue the reservation must
correct for; get that wrong and the misalignment is silent until something spills an XMM
register.

### The frame pointer exists for the SAVED-RBP CHAIN, not for addressing

Locals and spill slots are addressed off **`rsp`** (`spillSlotBaseDisp`), and that does not
change — rbp addresses nothing. What it provides is the **chain**: `[rbp]` is the caller's
rbp and `[rbp+8]` is the return address, so a live thread's frames form a linked list that
can be **walked**. Two mechanisms need exactly that list:

1. **The panic backtrace** — `mrt_panic_walk_frames` follows it to symbolize a fault's or a
   panic's callers. Frame 0 comes from the faulting RIP; every frame above it comes from
   the chain.
2. **Growable green-thread stacks** (R3 @ P1.5) — a Go-style `morestack` allocates a bigger
   stack, **copies** the old one onto it, and must then find and fix every interior frame
   pointer, all of which have just moved. The chain is *how it finds them*: v1's
   `__gt_morestack` walks it and adds the relocation offset to each saved rbp
   (`maxon-selfhosted/Compiler/Targets/X64/X64Backend.maxon:7094`, step 6b at `:7172`). A
   relocating stack with no chain cannot enumerate the pointers it just invalidated.

**Leaves are framed too.** A frameless leaf cannot be unwound out of, so it truncates any
walk that reaches it — and a chain with a hole is not a chain. The frame *pointer* is
therefore unconditional; only the frame *reservation* (`sub rsp, N`) is still elided when
there is nothing to reserve, which is most leaves.

**It costs no register.** `allocatablePool` has always excluded rsp/rbp as "the frame", so
the 14-GPR pool is unchanged — shv2 was already paying a frame pointer's full price while
leaving rbp holding whatever the loader put there.

> ⚠ **This REPLACES the frame-pointer-omitted (rsp-only) design shv2 shipped with**, which was
> chosen before the `morestack` requirement above was on the table and left rbp reserved but
> dead — a strictly dominated state. Adopting it moved all 221 IR-bearing goldens **once**
> (P1.0d.3); retrofitting it at P1.5 would move the same goldens *then*, on top of a runtime
> that had already shipped against the chain's absence. That is the retrofit this rewrite
> exists to avoid.

`CodeResult.stdModule` carries the mid-level module the backend lowered FROM. x64/PE ignore
it; it exists because the **wasm** backend will consume the machine-level IR directly instead
of going through a target dialect — and with the MIR tier gone, Std *is* that IR.

`GlobalDataTable` carries two sections. Its **`.rdata`** half is still **empty** — there are no
strings or floats yet. When it fills, the rule is: the backend captures rdata constants
chunk-locally and merges them into the shared table **single-threaded in function order**
(idempotent-by-label dedup), with content-derived keys for all other shared appends. That is
what keeps a parallel backend byte-deterministic.

### Stack arguments — the arguments past the register files

A calling convention passes the first few arguments in registers and the rest on the stack. shv2's
custom x64 ABI has **six** integer argument registers and **six** float ones, counted independently;
AAPCS64 has **eight** of each; wasm has no such thing at all, because its parameters ARE locals.

**⭐ ONE PLACEMENT RULE, CONSULTED BY BOTH ENDS OF EVERY CALL** — `abiFileSlotIndex` /
`abiArgIsOnStack` / `abiStackSlotsBefore` in `Targets/Shared/StdLoweringShared.maxon`. It reads a
per-argument `ArgFloatMask` and the target's per-file register capacity, and nothing else. Both
sides already hold a mask: a callee folds its declared parameter types once per function
(`floatParamMaskOf`), a caller looks the callee's up by name (`calleeArgFloatMask`) or reads an
indirect call's off the op.

*Rationale:* the rule used to be stated TWICE — a recomputing `computeParamSlotIndex` on the callee
side and a running-counter twin inside each backend's argument loop, held together by a comment
saying so. Two monotone counters over one list cannot disagree, so that was survivable while every
argument fit a register. It stops being survivable the moment an argument can OVERFLOW, because
"which slot" then means two different things — an index within one register FILE, and an index into
the single merged outgoing STACK area — and a caller and callee that disagree about one argument do
not fail to compile, they compute a different answer.

**The registers are two files; the stack is ONE area.** A float that overflows the last XMM slot
takes the next stack slot after an integer that overflowed the last GPR slot: there is one outgoing
region and one 8-byte stride, so the MERGED count indexes it, not either file's own.

**The frame.** `IrFunction.outgoingArgSize` is the per-function MAX over its call sites (the region
is reused, so the largest call's need, not the sum), folded during the isel walk. On x64 it sits
above the shadow space at `[rsp + win64OutgoingArgDisp(slot)]` — the SAME formula and the SAME region
the >4-argument import calls already used, so a frame has one outgoing area rather than an internal
layout beside the Win64 one. `spillSlotBaseDisp` already places every spill above it. arm64 has no
shadow space: its region is at `[SP + slot*8]`, at the very bottom of the frame, which pushes the
x29/x30 frame record up by `outgoingArgSize` (`arm64Prologue`'s `recordOffset`). x29 still points AT
the record, so `[x29 + 16 + slot*8]` spill addressing is untouched, and a frame with no outgoing
arguments emits the byte-identical prologue it always did.

**⭐ THE CALLEE READS OFF THE FRAME POINTER, WHICH IS WHY IT CAN BE COMPUTED IN THE ISEL.** On x64 an
incoming stack argument is at `[rbp + 16 + shadow + slot*8]` — frame-size- and push-count-INDEPENDENT,
because `insertPrologueAtEntry` establishes the frame pointer BEFORE any callee-saved push. (It does
that for the saved-rbp chain; the stack-argument displacement is a second thing that ordering buys.)
arm64 cannot: its incoming displacement is `frameSize - outgoingArgSize + slot*8`, neither term known
until the allocator has finished, so the isel emits `arm64LoadIncomingArg(dest, slot)` and
`Arm64PrologueEpilogue` — the one pass that knows both — rewrites each in place.

**Two consequences worth stating, because each is a guard the reference compilers needed and shv2
does not:**
- **No frameless-leaf hazard.** v1 (`FrameScanState.needsFrame`) and the bootstrap (`hasStackParams`)
  each had to force a frame onto a leaf with stack parameters but no locals, spills or calls, or
  `[rbp + 16]` reads through the CALLER's frame pointer. shv2 frames EVERY function unconditionally.
- **No FP-overflow gap.** Both references PANIC on a float argument past the float file. Here the
  store/load is `word64` and the encoder picks `movsd`/`STR Dt` from the register's CLASS, so the
  float path needed no code of its own.

**⛔ THE STACK STORES ARE EMITTED BEFORE THE REGISTER MOVES, and that is a correctness requirement.**
Materializing a stack argument's source takes a register the allocator picks; an argument register an
earlier move already loaded is not a *value*, so nothing marks it live from that move to the call, and
a store emitted after one can silently overwrite it. `specs-shv2/x64-stack-arg-disp32.md` returned
**400 instead of 200** in exactly that shape (`movRegImm32 rax, 200` three instructions before the
`call`, where rax was argument slot 2). Ordering the stores first closes it rather than narrowing it:
while they run, no argument register has been established. Both reference compilers order it this way
— v1's `sequentializeCallArgSetup` "Phase 0", the bootstrap's "compute the stack args FIRST".

**The ceilings that remain are STATED, and there are two.** `MaxAbiArgs` = **64** is the width of the
per-argument float mask (`StdDialect.ArgFloatMask`): bit 64 does not exist, and an argument past it
would be classed GPR whatever its type. `Parser.requireAbiParamCount` refuses a signature past it —
ONE test, of the ABI count (declared parameters plus the companion environment per function-typed
parameter, the layout descriptor, and one witness per `where` constraint), replacing two checks that
tested two different counts against one constant. `MaxAsyncArgs` = **6** is the green thread's inline
argument region, which the hand-assembled trampoline reads back into the argument registers one slot
at a time; a spawn therefore has no stack-argument path even though an ordinary call does, and
`Parser.emitAsyncCall` says so where a compiler panic used to fire.

### `.data` — the top-level `var` slots (P1.0d.5b)

Its **`.data`** half is live. `GlobalDataTable.layOut` is the SINGLE writer of the slot order, of
every slot's offset, and of the label→slot index — so the image, the relocation resolver and the
`RequiredData` gate cannot hold three opinions about where a global lives.

**Slots are sorted by storage size, LARGEST FIRST, stably.** Every size is a power of two and ≤ 8,
so laying the widest out first makes each slot naturally aligned as it is reached and the padding is
**zero** — declaration order would put an 8-byte slot at offset 1 and need seven bytes to fix it.
The sort is STABLE because equal-sized slots are pinned in declaration order
(`specs/static-variables.md`'s `data-section-multiple-bools`). An entry carries its `(value, type)`
and NOT its bytes: the image is rendered from that pair by `dataSectionImage`, once, so a value and
a serialization of it cannot disagree. A MANAGED global's entry is that pair too — an 8-byte slot whose
value is the NULL pointer `__module_init` overwrites before `main` (see *Top-level managed `var`*), so the
image has no case for it and `globalStdType` is still the single home of "which globals have storage".

⭐ **A global's IDENTITY and its LABEL are two facts, and that is what makes file-private globals
work.** The storage KEY is file-scoped (`SignatureIndex.topLevelStorageKey`), so two files may each
declare `var counter` and each file's reads resolve to its OWN slot. The LABEL is DERIVED from it —
the bare `__data_counter`, suffixed `$1`, `$2`… only on collision, in arena order — so it is
path-free and a golden fragment never contains the temp directory it was compiled in.

> ⚠ **Both reference compilers get this wrong, and it is measured, not inferred.** Their resolver is
> file-scoped while their `.data` label is global-by-name, so two file-private `counter`s ALIAS onto
> one slot: `specs-shv2/global-file-private-same-name.md` returns **212** under the C# bootstrap
> where **118** is correct. v1 concedes it in its own comment — "in practice no two file-private
> vars share a bare name" (`Parser.maxon:3206-3214`) — which is correctness resting on a property of
> the corpus. None of v1's four bare-key sites is ported.

The label minter COUNTS rather than SEARCHES (`DataLabelCountMap`): probing `$1`, `$2`, … until one
is free is O(k) for the k-th global of a name, hence Θ(n²) for n same-named globals — exactly the
shape the spec above is about. `$` is legal in no Maxon identifier, so the disambiguator is the only
minter of suffixed labels and its count IS the next free ordinal. Measured off-instrument (the scale
corpus contains no top-level `var` at all): 800 file-private globals all named `counter` compile at
**x2.02 per doubling** across a 16x span, and produce 800 distinct slots.

### Top-level managed `let` — a constant whose value is BYTES, not a number

A module-scope `let` may hold a `String` or a `b"…"` byte-string literal. It is the SAME mechanism a
scalar constant uses, with one thing changed: what the constant's value IS.

- **The arena is the same one.** `ProgramSignatures`'s `TopLevelDecl` arena, its memoized DFS, its
  cycle detection and its file-scoped resolution are untouched; `TopLevelConstant` simply gains a
  `managedValue(bytes, kind)` answer beside `value(payload, tag)`.
- **The split is made ONCE, at the evaluator's entry, on the token range.** An initializer that is
  exactly one `stringLiteral` / `byteStringLiteral` token folds to bytes; everything else runs the
  scalar walk unchanged, so no arithmetic fold ever has to reject a managed operand. An interpolated
  `"a{x}b"` is several tokens and is therefore *not* a constant, which is correct.
- **The use site materializes the literal**, exactly as the scalar use inlines the number.

> ⚠ **IT IS ALSO THE FIRST RECEIVER WHOSE NAME CAN BELONG TO A DECLARED TYPE, AND THAT BREAKS AN
> INVARIANT THE DISPATCHERS RESTED ON.** `methodCallsAt`'s premise was that the token shape alone
> cannot tell `c.increment()` from `Point.create()` and that *"only the scope knows which"* — true
> while every receiver was a local or a capture, because a name was a value **or** a type, never both.
> A managed constant can be both, and `methodCallsAt` is the FIRST arm of all three dispatchers, so a
> constant claiming the shape outranks every type-based reading of it. Left unstated, `let Widget =
> "abcdefghij"` beside a `type Widget` made `Widget.byteLength()` answer **10** — the constant's byte
> length — where the oracle answers **42**, the type's static. Silently, with a green suite.
>
> ⇒ **A managed constant is the WEAKEST claim on `<base> . <member> (`** (`staticCallClaimsBase`,
> the mirror of `parseDottedPrimary`'s arm order). The type-based readings are asked first, each
> through the authority its own arm uses — `containsEnum` for a union CONSTRUCT (`Move.walk(5)`, same
> token shape), `isGenericAlias` for the `Array`/`Set` statics `parseQualifiedCall` intercepts ahead of
> its mangle (no file declares an `Array.create` for a callee probe to find), and `declaresCallee` on
> the mangled name built by the SAME expression `parseQualifiedCall` builds it with. It is a FALLBACK,
> not a precedence: when the type declares no such static the constant reads, and the oracle answers
> 10 there too. Every one of those five answers is measured against the runnable oracle, one program at
> a time, and pinned by `static-variables`'s `top-level-let-name-shadowed-by-*` cases.

> ⚠ **A MANAGED INITIALIZER MAY NOT REFERENCE ANOTHER GLOBAL, and that is a hard boundary rather than
> an ordering quirk.** `evaluateDecl` answers `notFound` for a managed decl, so `let A = "x"` /
> `let B = A` is **E2004 `Undefined constant 'A'`** — in BOTH declaration orders, which is MEASURED to
> be exactly what the runnable oracle does (its bare literal goes to the managed bucket while its bare
> identifier goes to the scalar folder, and the scalar folder never sees managed declarations). ⇒ there
> is no managed-to-managed dependency to order and no managed cycle to detect. Nothing about a managed
> constant depends on file order.

**Storage: shv2's OWN immortality, not the oracle's refcount sentinel.** The bootstrap marks such
records with `MmImmortalRefcount = 0x4000_0000_0000_0000` and gates `mm_incref`/`mm_decref` on it.
shv2 imports none of that — a second immortality mechanism is this project's signature bug — and
instead reaches the same place through what it already has: `.rdata` plus static ownership.

| kind | what a read materializes | shared? | dropped? |
|---|---|---|---|
| `String` | `lea` of the ONE immortal `.rdata` 56-byte record `lowerStringLiteral` registers (deduped by bytes) | **yes, one record for the whole program** | never — the value is borrowed |
| `b"…"` | `lowerByteStringLiteral`'s owned 48-byte record over the ONE immortal `.rdata` blob (deduped by bytes) | the **payload** is; the record is per read | the record is, by the ordinary owned-temp drop |

> ⚠ **THE BYTE-STRING RECORD IS NOT IMMORTAL, AND THE REASON IS A MISSING PREREQUISITE, NOT A CHOICE
> OF MECHANISM.** An `Array` is a MUTABLE container whose own methods rewrite its record — a `push`
> detaches the buffer by writing `buffer@0`, `capacity@16` and `length@8` — and a `.rdata` record
> cannot be written. Two things must exist before the record can move there, and shv2 has neither:
> (1) the guarantee that such a value is never mutated, and (2) a real COPY promotion for
> `var b = <borrowed array>`, which today `__mm_incref`s the box — and an rdata box has no refcount
> word to incref. **(1) IS NOW WHOLE**: the receiver half landed with the rung below, and the CALL-SITE
> half — a `let` handed to a parameter the callee writes, transitively — landed with the
> parameter-mutation rung (see *Parameter mutation* below). (2) remains, and is a rung of its own.

**A `let`-bound container is not writable, and that is enforced — ONCE, for all three of them.**
`noteReceiverWrite` refuses a receiver-writing method on an immutable receiver with **E3019**, the
diagnostic the oracle raises. Without it a top-level managed `let` would silently mutate a per-read
copy — a wrong answer with no diagnostic. A **PARAMETER is exempt**: it is not `mutable` (it cannot be
rebound) yet the record it denotes very much can be written, because it is a borrowed reference to the
caller's. That is what `VarInfo.isParameter` exists to say — `mutable` answers "may this NAME be
rebound?", which is a different question, and one a `let` ALIASING a parameter answers differently
again (`let a = p` is still a `let`; E3019, measured).

| receiver | receiver-writing methods | where that list lives |
|---|---|---|
| `String` | `append` | the arm that dispatches it (`parseStringAppend`) |
| `Array` | `push`/`set`/`insert`/`append`/`reserve`/`resize`/`clear`/`pop`/`remove` | `arrayMethodMutatesReceiver` |
| `Set` | `insert`/`remove` | `setMethodMutatesReceiver` |

> ⚠ **THE THREE HAD ALREADY DRIFTED, AND TWO OF THEM DISAGREED WITH THE ORACLE.** Each container
> answered "may this receiver be written?" with its own premise: `String` asked `not binding.mutable`,
> `Array` asked `mutable or isParameter`, and `Set` asked *nothing at all*. So `function grow(s String)
> … s.append("XY")` was a FALSE REJECTION (a parameter is never `mutable`) where the oracle returns 4,
> and `let s = IntSet.create() … s.insert(1)` was ACCEPTED where the oracle raises E3019. One rule, one
> home: what stays per-container is only WHICH NAMES write the receiver — a fact about that container's
> method table — while the binding's writability and the diagnostic are shared, so a fourth container
> cannot invent a fourth answer.

⚠ **A SELF-FIELD RECEIVER IS LOADED, AND ITS WRITABILITY IS THE FIELD'S OWN `var`/`let`.**
`methodReceiverBinding` materializes a bare `items.push(v)` inside a method through the same
`parseSelfFieldRead` a bare read uses. Without it the dispatch took `VarInfo.boundValue`, which
`createSelfField` leaves **0** — and 0 is `self`'s own id — so `items.count()` answered **0** for an array
holding one element (a silent wrong answer; the oracle answers 1), and `items.push(v)` was **E3019**
against an ordinary struct-with-a-container method, because the alias is neither `mutable` nor
`isParameter`. The writability question is `layout.fieldIsMutable`, the SAME column
`emitCheckedSelfFieldStore` asks of `n = 1`, and it is asked through `selfFieldIsWritable` — the receiver
(`items.push(v)`) and the bare-name ARGUMENT (`grow(items)`, whose blame name `parseVariableReference`
publishes) are one question about one field, and they were derived twice, once INVERTED.
`builtinConformanceReceiverValue` was this rule's third copy and is gone: one materialization, four
dispatch arms.

⚠ **A SELF-FIELD ALIAS IS NOT A CAPTURABLE BINDING, FOR THE SAME REASON — `boundValue` 0 IS THE
RECEIVER.** `emitCaptureRead` read it as an ordinary enclosing local, so a closure inside a method naming a
field by its bare name stored the enclosing receiver's BOX in its env slot and handed it back as the field:
`apply(function(k int) gives k + n, x: 1)` on a `type Counter{ var n as int }` holding 3 returned **25**,
with no diagnostic anywhere — while `self.n`, the same expression's other spelling, was refused, and the
ranged-alias spelling panicked in lowering (the env slot's type is filled at parse time, so a `named`
reaches `maxonTypeToStdType` unresolved). All three now reach `requireNotInSelfCapture`, which
`requireReceiverValue` also owns so no future receiver user has to remember to call it.

### Parameter mutation — the CALL-SITE half of E3019 (a whole-program fixpoint)

The receiver half above refuses a write through an immutable binding where the receiver and the method are
one expression. The other half is `grow(a)` — legal or not depending on `grow`'s **body**, which may be in
another file. So it is a whole-program summary, built once in `SemanticCheck` and read at every call site:

| | |
|---|---|
| **the seed** | `IrFunction.writtenParamMask`, an i64 bit per parameter, set by the PARSER at `noteReceiverWrite` — the one site that already decides whether a receiver-writing method may be called at all. The summary never re-derives "which methods write their receiver" from the LOWERED callees (`__managed_push`, `__managed_append`, `__set_insert`); that would be a second list, three tiers from the first. |
| **the closure** | a worklist least-fixpoint over the reverse call graph (`buildParamMutationSummary`), the same shape `Parser.solveDescriptorNeeds` uses. A mask only ever GAINS bits and a function has at most `MaxAbiArgs` of them, so a caller is re-enqueued only on a real change — which is what makes a recursive and a mutually recursive graph terminate. |
| **the adjacency** | a `CsrGraph` keyed by function INDEX, per `CsrGraph.maxon`'s own instruction. Written first as a `Map with (ByteArray, OpIndexArray)`, it cost one array per callee: on a maximal N-function chain, `phase:semanticCheck` allocations grew **+984 → +13,104** from N=400 to N=6,400. The CSR costs **+142 → +238**, and the drain loop hashes nothing. |
| **the call site** | `checkImmutableArgToMutatingParam`, in `checkSlottedCall`. It reads the SOURCE-order `argImmutableNames` column and bridges to the callee's declaration-ordered mask through `argSlotPosition` — the same mapping `slotCallArgs` applied to produce the `argIds` its sibling checks read. |

**The blamed name rides the CALL OP (`MaxonOp.call.argImmutableNames`), because immutability is a fact
about the SYNTAX and not about the value.** `let a = p` binds `a` to the parameter `p`'s own ValueId, so a
per-value mark would refuse `f(p)` in any function that also wrote `let a = p` — while the oracle refuses
`f(a)` and accepts `f(p)` (measured). The column is parallel to `args` when present and EMPTY when no
argument names an immutable binding, which is every compiler-emitted call and most user calls; a shared
empty array serves them all.

⚠ **AN INDIRECT CALL AND A WITNESS DISPATCH ARE NOT CHECKED.** Neither names a callee a summary could be
keyed on, and BOTH reference compilers accept `let f = grow` then `f(a)` (measured — the oracle compiles it
and the push takes effect), so a conservative refusal would break agreement on a legal program rather than
close a hole. `parameter-mutation.md` pins the acceptance so the gap is visible rather than assumed.

⚠ **A METHOD WRITING ITS OWN RECEIVER'S DATA IS NOT A PARAMETER MUTATION, and that is a RULING taken
because the oracle disagrees with itself.** On one program with a `let` receiver,
`self.total = self.total + value` is ACCEPTED (42) while `total = total + value` — the same write, the
other spelling — is **E3019**: its analysis matches op TYPES and only the bare spelling emits the
operation its self-field check inspects. shv2 has exactly ONE self-field store for both spellings (v1's
split is what made its field-visibility check structurally blind to bare names), so it must answer once;
it answers the way the corpus pins (`self-keyword.md`'s `self-with-params`, which both compilers run at
42, and which `parseSelfFieldAssignment` already stated as the rule: a `let` on a struct binding refuses a
rebind and a direct field write through it, and *does not reach inside the type's own methods*). The same
ruling covers a container held in a field — there is no principled line between writing `self.total` and
writing the array `self.items` points at, and drawing one at the spelling IS the inconsistency above.
`receiverOwnerMask` is where it lives.

### Top-level managed `var` — a POINTER slot, filled before `main` and released after it

A module-scope `var` may hold a `String` or a `b"…"` byte string. Its `.data` slot is **eight bytes of
zero**, and two synthesized functions bracket `main`:

```
mrt_start:  … call __module_init ; call main ; [__maxon_global_cleanup] ; [__mm_leak_check] ; exit
```

Everything about it follows from one sentence: **a `var` is MUTATED, so its record may be neither baked
nor shared.**

- **Not baked.** A `.data` slot holding another section's address needs a compile-time VA, and there is
  none under ASLR (`specs/static-variables.md:729-730`). So the image holds a NULL pointer and
  `__module_init` writes the address — which is exactly what the `let` half does *not* need, and the
  whole reason a managed `var` waited for its own rung.
- **Not shared.** A `let`'s String is the ONE immortal `.rdata` record every read of that literal borrows;
  a `var` gets a **real owned heap record** (`__str_clone` of the literal), because `msg.append("!")`
  rewrites `buffer@0`/`length@8` in place. Sharing it would make a `let` of the same bytes observe the
  change and dropping it would free read-only data. **MEASURED**: with `let SHARED = "hi"` beside
  `var one = "hi"` and `var two = "hi"`, mutating `one` prints `hi hi! hi`. The oracle draws the same
  boundary from the other side — its static-literal path EXCLUDES a mutable global
  (`MaxonToStandardConversion.cs:1045-1049`, *"a mutable global array stays heap and COWs on first
  write"*) — and `static-variables.md`'s `top-level-var-string-mutate-cross-function` leak-gates it.
  A `b"…"` needs no clone: `lowerByteStringLiteral` already produces an owned record over an immortal
  blob, which is precisely what the slot must hold.

**The two functions are MAXON-tier, not Std-tier, and that is the rung's one design choice.** Every other
compiler-synthesized function (`installMmRuntime`, the destructor cascades) is built as already-lowered
`StdOp`s after the pipeline. These are built as `MaxonOp`s and appended to the merged module *before* it —
because what they must build is a LITERAL'S RECORD, and `lowerMaxonToStd` already turns one
`stringLiteral`/`byteStringLiteral` op into exactly that. Six op constructions instead of a second copy of
`lowerByteStringLiteral`. The price is the dense per-value type columns a Maxon function carries, paid
once by `ModuleInit.SynthesizedFunction`. Appending before the pipeline is also what makes
`scanRuntimeUsage` see their `__str_clone`/`__str_decref`/`__managed_decref` calls and install the floor.

| | `__module_init()` | `__maxon_global_cleanup(code) → code` |
|---|---|---|
| per global | build the owned record, `storeIndirect` it into the slot | `loadIndirect` the slot, drop it through `managedFieldDropCallee` |
| old value | **not loaded, not decref'd** — the slot is zero | — |
| null guard | — | **none** |

> ⭐ **BOTH REFERENCE COMPILERS CARRY A GUARD HERE AND shv2 CARRIES NEITHER, because a fact replaces
> each.** Their initializing store is on the SAME path as an ordinary assignment, so they emit the
> load-and-decref and then suppress it by matching the enclosing FUNCTION'S NAME
> (`isModuleInit = func.Name == "__module_init"`, `MaxonToStandardConversion.cs:2327`); shv2's
> initializing store is emitted by different CODE, so which store it is needs no runtime name. And their
> cleanup decref is null-guarded (`EmitDecrefValueIfNonnull`, v1's `__mm_decref_maybenull_helper`)
> because a lazy or never-run initializer can leave a slot zero; shv2's `__module_init` runs
> unconditionally and fills every entry of the same list, so a slot reaching the cleanup is non-null by
> construction. **The user-visible re-assignment therefore also decrefs unconditionally** — one branch
> per write, deleted, on the strength of one invariant.

**A re-assignment is: settle the new owner, release the old, store** (`Parser.emitCheckedGlobalStore`).
The new value goes through `moveManagedValueInto`, the SAME door a struct field and a union payload use —
a borrowed String is COPIED, an owned one is MOVED (its source poisoned or its statement-end drop
cancelled), a borrowed non-String aggregate is refused. So `g = g`, `g = other` and `g = <owned temp>` all
work, and none of them aliases.

**A `var` is also a method RECEIVER, and it is the first MUTABLE one that is not a binding.** The base is
loaded and dispatched through the same `dispatchMethodOnBinding` a local goes through, wearing a synthetic
**mutable** `VarInfo` — which is the whole of what makes `Buffer.push(1)` legal where `Live.push(1)` is
E3019. The loaded pointer is BORROWED (the slot owns it) and there is no store-back: every
receiver-writing method rewrites the record in place and never moves it. `managedReceiverBindingOf` is the
ONE resolution both the token-shape predicate and the dispatch read, so they cannot claim the shape for
one door and resolve it through the other.

> ⚠ **A RECEIVER RESOLVES IN THE SAME ORDER A BARE READ DOES — local, then CAPTURE, then top-level — and
> that order is the shadowing rule, not a cost preference.** It is encoded in three places that cannot
> route through one another (`parseVariableReference` returns a value, `parseMethodCall` needs a receiver
> binding, `methodCallsAt` must be a side-effect-free bool), so each of them states it. `parseMethodCall`
> had the last two INVERTED until the slice-2a review, and it was a live wrong answer rather than a latent
> one: with `var msg = "GLOBAL-TWELVE"` and a `let msg = "ab"` in `main`, one closure body read the
> capture for `{msg}` and the **global** for `msg.byteLength()` — two resolutions of one name in one
> expression, neither of them a compile error. A `var` receiver is the worse half, because it is WRITABLE:
> a mutating method would have written the global while the program named a local. Correctly ordered, a
> captured String receiver is refused exactly as it is without the name collision (`capturedStructBase` —
> only a struct carries methods through a capture at this rung).

**Both functions are unconditional DFE roots**, beside `__mm_leak_check`, for its reason: the entry stub
that calls them is built after the prune, so a pruned root leaves a call with no callee — loud on every
backend, never silent. And the name index the prune walks now **PANICS on a duplicate** rather than
silently keeping the last one: `indexFunctionsByName` documented "installed at exactly one site" without
checking it, and `ModuleInit` is the first installer to append into the MEMOIZED `queryAllModule` module,
so that premise acquired a second dependency — that `compileToCodeResult` runs at most once per `Project`,
which is the driver's property and not the pass's. The check is one integer compare per function (the loop
inserts index `i` on iteration `i`, so the map's height must be `i + 1`), and it costs zero allocations.

**Nothing about a managed `var` depends on file order.** A managed initializer may not reference another
global (`evaluateDecl` answers `notFound` for one, so `let A = "x"` / `let B = A` is E2004 in both
declaration orders — measured against the oracle), so there is no initialization order to get wrong. The
arena order the init emits in is fixed only so the emitted code is a function of the program.

### Top-level ARRAY globals — a heap record for `let` AND `var` alike (P1.7 slice 2b)

A module-scope binding may hold an `[…]` array literal whose elements are **integer constant expressions or
String literals**. It is the managed-`var` mechanism above with one thing changed, and one premise broken.

**The thing changed: the value is BUILT, not materialized.** A String literal is an immortal `.rdata` record
and a `b"…"` is an owned record over an immortal blob, so both slots are filled from a literal the lowering
already knows how to emit. An array has no literal record at all, so `__module_init` emits the ops
`Parser.parseArrayLiteral` emits in a function body — `__managed_create(elementSize, elementDestroy)` then one
`__managed_push` per element, with a String element **cloned** because the array becomes its sole owner and
`__managed_decref`'s walk drops every live slot. The element values are folded by the SAME scalar constant
evaluator every other initializer uses (`evalConstArrayLiteral` → `evalConstArrayElement`), so
`[BASE, BASE + 2]` is as much a literal as `[1, 2]`; a String element is the one-token managed test applied
between two separators, so an interpolation and every trailing form fall to the scalar walk and are rejected.

> ⚠ **v1's `ConstantArrayLiteralRdata` is deliberately NOT ported.** It moves an all-constant-integer element
> BUFFER into `.rdata` while still heap-allocating the header. That is an OPTIMIZATION over this, it is the
> optimizer's call, and building it now would fork a second element-emission path before the first is proven.

**The premise broken: `mutable` no longer decides which door a use goes through.** An array's record cannot
be re-materialized by a READ — a `let` String's read borrows the ONE immortal record, but an array's record
is mutable by nature, so a per-read copy would be a different array every time — so an array binding gets a
`.data` slot **whichever keyword declared it**, and `let` differs in exactly one thing: it refuses the WRITE.
`TopLevelDecl.hasStorage` is that criterion, written where the outcome is written and read by all four
askers (the two use-doors, the layout, the label minter); `DeclKind` is `inlined | stored`, not `let | var`.

⚠ **This is a DELIBERATE DIVERGENCE from the oracle**, which makes a never-mutated `let` array an immortal
shared static record (`MaxonToStandardConversion.cs:1039-1077`). shv2 cannot yet: immortality needs
enforcement that such a value is never mutated — **now complete**, the E3019 receiver rule plus the
call-site rule (*Parameter mutation*) — and a real COPY promotion for `var b = <borrowed array>`, which it
still lacks. Heap for both is what it can state truthfully until then.

| the refusal | code | where |
|---|---|---|
| `Live.push(9)` — a receiver-writing method on an immutable receiver | `E3019` | `noteReceiverWrite`, via the `mutable` bit on the synthetic receiver binding |
| `Live = […]` — assigning a binding that has a slot and still refuses writes | `E2013` | `storeToGlobal`, off `TopLevelGlobalLookup.found`'s `mutable` |
| `var b = Live` / `b = Live` — a mutable ALIAS of an immutable record | `E2015` | `promoteBorrowedToOwned`'s aggregate arm |

> ⭐ **THE THIRD ROW IS THE ONE THIS SLICE HAD TO FIND, and it is what "an immutable binding with a SHARED
> mutable record" costs.** A borrowed managed aggregate bound to a `var` is promoted to owned by an INCREF of
> the same box — reference semantics, deliberately, because the alias is observable — so `var b = A` then
> `b.push(9)` grew `A` with E2013 and E3019 both intact and nothing to report it. MEASURED at 3 where 2 is
> correct. It is reachable only through this slice: a `let` String's promotion COPIES (`var b = <String let>`
> stays legal and correct, and the oracle *leaks* on that same program), and a `let` byte-string
> re-materializes a fresh record per read. Widening the incref into a copy is not the fix — a borrowed array
> bound to a `var` SHARES in both reference compilers when the source is mutable (measured: the oracle
> returns 3 for a `var` global and for a parameter, exactly as shv2 does), so copying would break an
> agreement rather than fix a disagreement. **Immutability of the SOURCE is the whole discriminator**, and
> the mark rides the VALUE (`Parser.immutableGlobalReads`) rather than being re-derived from the initializer's
> tokens — which would be a FOURTH encoding of the local→capture→top-level order this file already carries
> three copies of, and whose drift is silent.

**Nothing depends on file order, still.** A managed initializer may not name another managed global, so
`let A = [1,2]` / `let B = A` is **E2004 in both declaration orders** — reached through the settled
`arrayValue` arm one way and through `hasStorage`'s "not yet evaluated ⇒ not stored" answer the other, which
is what also keeps the forward reference `let TOTAL = FIRST + SECOND` working. An **empty** `[]` is refused
(E2015): it has no element to infer from, and unlike a function body it cannot be told to use
`Array with T` + `.create()`, because a call is not a constant.

### Top-level container globals — `<Alias>.create()`, builtin or DECLARED (W41)

`var g = ItemArray.create()` and `var g = StrMap.create()` are the array literal's **empty case**, and they
take the same slot, the same `__module_init` build and the same `__maxon_global_cleanup` drop. One outcome
(`ConstFoldedValue.containerCreate(giid)`) carries an INSTANCE and nothing else, which is what lets it serve
a factory ARGUMENT as well as a global's own slot — `var db = Database.create(EntryMap.create(), …)`.

**What `__module_init` needs is a CALLEE and a STAMP LIST** (`ProgramSignatures.containerCreateCall`), and
the two kinds of container differ only in the second:

- a **builtin** container's `create` is a runtime entry (`__managed_create`, `__set_create`, …) that cannot look
  up a stride or a column destructor and must be handed both;
- a **declared** generic's is a real `create()` static the program wrote (`Map.create`, once
  `stdlib/Map.maxon` is listed and `Map` is an ordinary generic), which builds its own fields from its own
  declaration and is handed nothing.

> ⭐ **The hidden DICTIONARY arguments are not in that description, and deliberately so.** A generic's
> `create()` carries a layout descriptor plus one witness per `where` constraint, and both are sourced at the
> CALL SITE from the call's RESULT for a `Self`-returning static (`LowerMaxonToStd.witnessSourceValue`). The
> record minted here is typed `genericInstance(giid)`, so the lowering threads `__layout_Map_String_Integer`
> and `__witness_String.Hashable`/`.Equatable` unasked — the emitted call is byte-identical to the one the
> same `StrMap.create()` inside a function lowers to. Building that block here would have been a second,
> driftable copy of an ABI.

Three premises of that call are therefore checked at the declaration
(`Parser.requireDeclaredGenericGlobalCreate`): the `create()` must EXIST, be NAMEABLE from the declaring file
(`__module_init` is exempt from `SemanticCheck.calleeVisibleFrom`, so the visibility question has to be asked
where the reader file is known — the same measured hole the user-factory form closes), and return `Self`.

> ⚠ **A DECLARED `create` MUST BE ROOTED, and missing one is a PANIC rather than a wrong answer.**
> `__module_init` does not exist when `StdlibSource.deriveStdlibFacts` walks reachability, and it is called by
> the entry stub rather than from `main` — so a stdlib `create` named only by an initializer is filed
> unreachable, its body is never lowered, and `DeadFunctionElimination` then reaches the empty function
> (`requireUnreachableStdlibStayedDead`). `ProgramSignatures.globalInitCallees` supplies those roots off the
> DECLARATION, and it must name **every** call the initializer makes: measured, a `Map.create` sitting in a
> factory's ARGUMENT list was left out while the factory itself was rooted, and that is precisely the panic.

**What stays refused** is a `create` this position cannot call — absent, invisible, or not `Self`-returning —
plus ARGUMENTS, which no `containerCreate` outcome carries. The initializer form that does carry them
(`factoryValue`) types its slot from the callee's DECLARED return, which for a generic is the BASE and not
the instance, so it cannot serve `IntBox.create(9)` either. The oracle accepts that program; shv2 refuses it
at the argument, and closing the gap means retyping a factory global's slot to the instance its alias names.

### Dead-global elimination — a global no code NAMES is never built (P1.7 slice 3)

A stored top-level binding costs three things: an 8-byte `.data` slot, the `__module_init` ops that build
its record and store it, and the `__maxon_global_cleanup` drop that releases it. A binding nothing reads or
writes pays all three for nothing — **MEASURED at +2,164 emitted code bytes** for a lone `var UNUSED =
[1,2,3]` (3,676 against a 1,512-byte floor), because the dead array also drags in the array runtime, both
synthesized functions and the entry stub's two calls.

**`DeadGlobalElimination.liveGlobalLabels` decides what to BUILD; it removes nothing.** Both reference
compilers do the equivalent job at the Std tier, *after* their `.data` layout is fixed, so they must splice
ops back out. shv2 knows first: the slots (`declaredGlobals`) and the two functions (`installManagedGlobalInit`)
are produced from the SAME arena, in `compileToCodeResult`, before the pipeline — so **one filter on that
arena (`ProgramSignatures.contributesStorage`) drops all three atomically**, with no init run to splice, no
table to re-lay-out, and no ordering between three edits to get right.

| the piece | who would have built it | what the filter does |
|---|---|---|
| the `.data` slot | `declaredGlobals` | no `DataSectionEntry` |
| the record build + store | `managedGlobals` → `__module_init` | no ops, and with no managed global left, no function at all |
| the cleanup drop | `managedGlobals` → `__maxon_global_cleanup` | likewise |

> ⭐ **THE EVIDENCE IS THE EMITTED `globalAddr` LABEL, NOT A PREDICATE OVER NAMES.** A use resolves
> local → capture → top-level, and *every* slice that re-derived that order produced a silent wrong answer.
> So liveness re-derives nothing: it is read off the ops resolution ALREADY PRODUCED. `Parser.emitGlobalAddr`
> is the only emitter in user code and is reached only from `emitGlobalLoad`/`emitGlobalStore`, so the live
> set is built from the very ops that would reference the slot — making "an op names a slot the layout
> dropped" **unrepresentable** rather than merely unlikely.
>
> ⭐ **AND IF IT IS EVER WRONG ANYWAY, IT FAILS LOUD ON EVERY TARGET.** All three backends resolve a
> `globalAddr` label through the one `GlobalDataTable.dataSectionOffsetOf`, which PANICS by name on a label
> the layout does not hold. So an under-walk is a compile-time abort naming the global, never a miscompile —
> verified by forcing the walk to answer "nothing is live": x64, arm64 and wasm all died with
> `no .data slot is labelled '__data_G'`. That backstop is what makes the pass safe to extend.
>
> ⭐ **BIDIRECTIONALITY IS FREE, and the three-op split is why.** The spec states liveness as "a `globalLoad`
> OR a `globalStore`"; shv2 has neither fused op — a read is `globalAddr`+`loadIndirect`, a write is
> `globalAddr`+`storeIndirect` — so both open with the SAME op and there is no second question to ask. A
> write-only global survives because a write IS the evidence. A rule naming two forms could be half-written;
> this one cannot.
>
> ⚠ **IT RUNS BEFORE `installManagedGlobalInit`, and that is correctness, not tidiness.** `__module_init`
> opens every eager managed global with a `globalAddr` of that global's own slot (and each `__lazy_init#…`
> opens its own), so a scan run afterwards would
> find every global keeping itself alive — always, silently. Both references meet the same shape and answer
> it with a runtime `func.Name == "__module_init"` compare; here the fact is expressed by WHICH CODE HAS RUN.
>
> ⚠ **LABELS ARE MINTED BEFORE LIVENESS, so a dead global keeps its claim on the bare name.** With two
> file-private `counter`s, dropping the one holding `__data_counter` leaves the survivor as
> `__data_counter$1` — correct, because its `globalAddr` ops already spell that label. Renumbering the
> disambiguator afterwards would rename a *surviving* global out from under its own ops. Verified both ways.

**Two deliberate narrowings, both strictly conservative:**

- **No reachability term.** The spec says "never read or written by any *reachable* function"; this says "by
  any function", which only ever keeps MORE. Reachability is `DeadFunctionElimination`'s fact, settled at the
  Std tier after `.data` is laid out — too late to decide what to build — and the only early answer,
  `StdlibSource.reachableMaxonFunctionNames`, is scoped to stdlib source precisely because it misses witness
  and `.rdata` edges. Widening it would be a second decider of reachability, disagreeing in the direction
  that drops a LIVE global.
- **No side-effect heuristic.** The bootstrap decides removability by `callee.EndsWith(".create") || ".from"`
  (`DeadFunctionElimination.cs:181-183`), so `Counter.build()` keeps its `print` while the identical body
  renamed `Counter.create()` silently loses it. **Renaming a method must not change whether its side effects
  run.** shv2 answers it structurally instead: **an EAGER initializer that CALLS keeps its slot, whatever
  names it** — the whole filter short-circuits on `ProgramSignatures.declScopeInitializerCall`, so a dead
  8-byte word is paid rather than a `print` lost, and the decision reads the OUTCOME rather than a spelling.

  ⛔ **THE KEEP-ALIVE IS EAGER-ONLY, AND FOR A LAZY `static` MEMBER THE OPPOSITE IS CANONICAL** (spec-port
  `lazy-static`). `specs/lazy-static.md` says a static field's initializer runs *"the first time the static
  field is accessed"*, so a static nothing accesses must run NOTHING — keeping it alive to preserve a side
  effect would preserve the very effect the language forbids. `declScopeInitializerCall` silences the rule
  for exactly those, which loses nothing: a lazy initializer runs only from an access, so no access means
  there was never a run to preserve. MEASURED on the oracle, one never-read binding moved between the two
  positions: `static var` prints 0, module-level `var` prints 1. TWO initializer
  forms call: a user static factory (`var db = Database.create(…)`, spec-port `map-struct-bytearray`) and a
  DECLARED generic's empty record (`var g = StrMap.create()`, W41), the second in the ARGUMENT position as
  well as its own. That one projection also feeds `globalInitCallees`, which ROOTS every one of those callees
  — see *Top-level container globals* below for why a missed root is a compiler panic rather than a wrong
  answer.

**Cost: ONE O(ops) walk that allocates nothing per op, SKIPPED entirely when the program declares no stored
global** (`ProgramSignatures.storedGlobalCount`, counted by `assignDataLabels`, which already visits exactly
those decls). The whole spec corpus and every scale rung's per-phase allocation counts are bit-identical to
before; the only cost is the label set itself, linear in globals declared. An unconditional second
full-module walk would have been a per-compile TIME cost allocating nothing — the shape `scale-test` is
structurally blind to.

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

> When the value is read on **both sides** of the call inside the loop, the bracket becomes a load
> **at each use** — the value is stored once at its def, outside the loop, and lives in a register
> only where it is read. Still forced, still not searched: the sixth of six values simply has nowhere
> else to be. (`SplitScope.everyUse`.)

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
> is a perfect elimination order. After splitting, `maxlive ≤ pool` everywhere by construction.
>
> ⛔⛔ **THAT IS NOT "THE COLORER CANNOT FAIL", AND THIS RULE SAID IT WAS** (corrected A5g,
> 2026-08-04, along with the same claim in three compiler sources). Chordal exactness is a theorem
> for the **unconstrained** problem; forbidden sets make this list colouring, and `maxlive ≤ pool` is
> then necessary and **not sufficient** — see "Known limits" #0 and
> `HallCondition.hallVerdictAt`'s header, which is the one place the NP-hardness argument is made.
> A value the greedy cannot place at a point that Hall's exact verdict calls FEASIBLE is a
> **confinement**, not a bug: `RegisterAllocator.reportExhaustion` runs that verdict to tell the two
> apart, and `SplitDriver.repairAtExhaustion` / `repairByEvictingOccupant` relieve it before the
> function is coloured again. What survives of the old claim is the half that is true and is still
> asserted: reaching exhaustion with an **OVERFLOW** verdict is a splitter bug and panics as one.
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
> full pool but not the subset they are confined to) is *always* relievable by a forced bracket, so
> reaching `E5001` from one is a **splitter bug**, and `noVictimAtPeak` panics rather than let it
> surface as user register pressure. The four legs of that "always" are stated on the panic, because
> asserting only the first of them once let the panic fire on ordinary Maxon: every value has a store
> anchor (an op def, or a phi's block entry); its store fits the pool (full-pool peaks are relieved
> first, so no def point is over-pool by then); **every** confined value can be made dead across the
> peak (below); and the tight set outnumbers the witness by more than the two virtual registers an op
> can name, so the values the peak op itself reads can never be all of them.
>
> **The two pools count two different demands, and lumping them over-counts.** Full-pool demand is
> the colorer's total register hold at a point — live values *plus* dead-phi reservations *plus*
> the reuse-copy transient. Reduced-pool demand is only the values the op *constrains*: those live
> across it, plus its own operands. A dead phi is neither, and `pickPreferredRegister` lands it in
> a caller-saved register anyway, so it never competes for the callee-saved subset.
>
> **A split only counts if it actually kills the value at the peak — and when the split at the
> eviction point cannot, the split WIDENS.** Uses *after* the peak become reloads; uses *before* it
> keep the original. So the value is dead across the peak only if no before-peak use is still
> reachable *from* the peak without passing the value's (single, SSA) def (`killsValueAtPeak`). Under
> a back edge it often is: `n` in `while i < n` is used in the header, *before* the loop's call in
> layout order, yet stays live across it. A loop-header **phi** is the opposite: every back-edge path
> re-enters its def, so the eviction-point split kills it.
>
> A value the eviction-point split cannot kill is **not un-relievable** — it just cannot be relieved
> *that cheaply*. At a **confined** peak the split widens to `SplitScope.everyUse`: **every** use is
> rewritten to a reload, so the original's only remaining reader is its own store and it is dead from
> the def onward. Six loop-invariants each read *before and after* a call inside the loop are exactly
> this shape — the eviction-point split cuts none of their ranges, yet six values against fourteen
> registers plainly fit the machine, so the answer is neither `E5001` nor a panic but a load before
> each use, which is the code the author would hand-write. (A constant there is re-emitted at every
> use instead, and costs nothing at all.) At a **full-pool** peak the widening is *refused*: reloading
> a value the loop uses at every one of its uses, every iteration, is precisely the cost `E5001` will
> not pay silently. That asymmetry is the forced-vs-searched line, drawn once.
> (`register-spill.loop-invariant-read-across-a-call-in-a-loop`,
> `register-spill.loop-invariant-constant-read-across-a-call-in-a-loop`.)

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
| 4 | **Commit** | `SsaDestruction.maxon` | `applyAllocation` rewrites ops in place, splices copies, clears the phi model. |

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

**Victim choice** is four tiers, cheapest first; within a tier, Belady/MIN (farthest next use), ties
to the lowest id. The candidates are the values live before the peak that the witness **confines**
and the peak op does not itself read.

1. **Remat** at the eviction point — a constant def (not a phi, not edge-passed) that
   `killsValueAtPeak` accepts is re-emitted before each *after-peak* use. Free: no store, no slot, no
   load.
2. **The eviction-point split** (`SplitScope.afterPeakUses`) of a value not yet in a slot — one store
   at the def, one reload per after-peak use-block, before-peak uses keeping the original in its
   register for free. Gated on being spillable for the peak's **placement** (**cold** requires def and
   every use at loop depth 0; **forced** requires only a store anchor), on the store fitting the pool,
   and on `killsValueAtPeak`.
3. **Full remat** (`SplitScope.everyUse`) — *confined peaks only*. A constant the eviction-point split
   cannot kill, re-emitted before **every** use. Still free, so still preferred over any spill.
4. **The full split / re-relief** (`SplitScope.everyUse`, or reloads-only for a value already in a
   slot) — *confined peaks only*. The forced bracket, paid at every use. This tier is what makes
   `noVictimAtPeak`'s confined panic unreachable.

Tiers 3 and 4 are consulted **only** where tiers 1 and 2 are empty — which before they existed was a
panic (confined peak) or `E5001` (full-pool peak). So no program that already compiled changed by a
single instruction when they were added; the whole `specs-shv2` golden set is byte-identical across
that change. And they are confined-only, so the `E5001` cliff is exactly where it was.

**A split must actually kill the value at the peak.** After-peak uses become reloads; before-peak
uses keep the original. So the value dies across the peak only if no before-peak use is still
reachable *from* the peak without passing its (single, SSA) def. `n` in `while i < n` is the
cautionary case: its only use is the header compare, which precedes the loop's call in **layout**
order but follows it around the **back edge** — storing it relieves nothing. A loop-header **phi**
is the opposite: every back-edge path re-enters its def, so it is relievable. (Without this test the
splitter over-spills, and its termination potential Φ does not strictly decrease.) `killsValueAtPeak`
gates **both** remat and spill at tiers 1–2, and for the same reason — remat partitions a value's uses
around the peak exactly as a spill does. A constant that failed it — `let c = 7` used only in a
`while i mod c < 3` header, live across the loop's peak solely around the back edge — re-emitted
nothing, relieved nothing, and was re-picked until the runaway bound panicked.
(`register-spill.remat-constant-live-only-around-the-back-edge`.) At a confined peak, failing it now
selects tier 3 or 4 rather than nothing at all.

**One slot per value, ever.** The store anchors at the def, which dominates every reload *wherever a
later peak puts one* — so a value relieved again at a **second** confined peak emits only more loads,
out of the slot its first store already wrote (`UseIndex.spillSlotOf`). This is what lets tier 4 admit
an already-stored value without ever writing it twice, and it is why `hasSpillStore` is a tier
discriminator rather than a refusal.

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
- **Each blocking value's source def site**, ranked cheapest-to-move first (fewest **reads** inside
  the loop = fewest reloads after the array rewrite). It counts op operands and **not** branch-edge
  args, which is why the word is `read`: a loop-carried `var` the loop only *assigns* is a block arg
  handed on at every join, so every one of its in-loop uses is an edge and its count is 0 — and it
  is still, correctly, in the blocking set (see "Known limits" #1).
- **The transformation**, named: hold the working set in an array. Array elements are never promoted
  into registers, so the hand-spill *stays* spilled.

It is **deterministic byte-for-byte** — no map iteration anywhere, values swept in id order,
candidates sorted by a total order (reads-in-loop, then value id). `specs-shv2/register-pressure.md`
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

### What gates the allocator

**There is no in-compiler allocation verifier, and none is needed.** Two things gate the
allocator, and both live in the suite:

| Instrument | Question it answers | Why the other one cannot |
|---|---|---|
| **The RUN — the GATE** (`spec-test` compiles each test, executes it, asserts its exit code) | *Is the allocation CORRECT?* A value left in the wrong register — an aliased colour, a mis-ordered edge copy, a reload from the wrong slot, a clobbered parameter — computes the wrong answer, and the exit-code assertion catches it end-to-end. | A golden cannot say whether the answer is *right*; it only says the code is what it was. |
| **The GOLDENS — the REFERENCE** (`specs-shv2/fragments/**.test`, **compared and reported** by `SpecTestRunner.checkTestFragment`) | *Did the code get WORSE?* An extra spill, a lost coalesce, a needlessly widened live range: each still returns the right answer, so a run alone sees nothing while codegen quietly rots. The goldens also reach where the run cannot — one execution takes ONE path, but the golden records **every block** of the function, including blocks that path never enters. | The run cannot see quality, and cannot see unexecuted blocks. |

⚖ **ONLY THE FIRST ROW IS A GATE** (user, 2026-08-02: *"the gate is the spec tests passing"* /
*"the goldens are NOT supposed to be a gate, they are just for reference"*). A golden difference is
printed as **reference drift** on stderr and contributes nothing to the failed count or the exit
code; `--update-required` regenerates the files, and **that diff is the review**. The gate role was
removed because it was actively harmful: a cross-target red read as *"10 stale golden mismatches +
9 others"* and the 9 were nine float programs exiting 1 on x64-linux (PLAN row `X5`), unlooked-at
for a day because ten pieces of bookkeeping in the same list were exactly as red as they were.

**The pairing is teeth-tested.** Disable the copy hint in `chooseRegister`
(`if false and hints.hasCopy(v)`) — a change that produces *correct but worse* code — and the suite
reported **72 passed, 12 failed**, all twelve `codegen changed` and **zero** behavioural. That split
is what says the reference is armed, and it is unchanged; only its rendering is. The same experiment
now reads **84 passed, 0 failed** with **12 golden fragment(s) drifted** reported beneath it —
*(derived from the measured split, not re-measured)* — which is the point: correct-but-worse code is
not a broken program, and the two now read differently at a glance.

> **HISTORY.** M5 carried a third gate, an `AllocChecker`: a symbolic verifier that abstractly
> interpreted the allocated function and asserted every use read the register holding its value.
> It existed because the fragments were then **regenerated** on every run — *outputs*, not gates —
> so the suite would go green on a wrong-but-self-consistent allocator, and the checker was the
> only thing in that path that could say *no*. It earned its keep (it caught the parameter-capture
> read-after-clobber, a real shipped miscompile). Commit `41b498a1d` turned the fragments into
> **compared goldens**, which — together with the run, which was always there — decides everything
> the checker decided. It was then spending 7–10% of every spec-test compile (its own `checking`
> sub-phase timer: 13.7 ms of 192 ms on a 300-function benchmark) to re-derive a verdict the suite
> already reached, so it was removed. Its one check that was *not* about allocation — the
> CFG `condBranch`-index completeness sweep — was kept and moved to `TargetLiveness`
> (see *The `condBranch` index* below), where it now runs on **every** build rather than only under
> `spec-test`.

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
can only take its own incoming register or a non-argument register. (The entry move elides
whenever the value lands in its own argument register.) This class **shipped once as a silent
miscompile**; what holds it now is that a clobbered incoming register hands the callee a wrong
argument, so the multi-parameter tests in `functions.md` return the wrong exit code and FAIL.

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
| `TargetLiveness.condBranchTargetOf` | O(1), always | the op **at** the recorded index **is** a `jcc` — so a stale index, or one set by something that is not a branch, cannot survive a CFG build |
| `TargetLiveness.checkCondBranchIndex` | O(ops in the block), always | the **converse**, against the OPS: the block's `jcc` set is exactly what `condBranch` names, and a two-way block's terminator is a `jmp` |

**Why the O(ops) sweep is not redundant, given the three O(1) guards.** `IrModule` is generic over
`Op` and therefore **cannot ask whether an op is a conditional branch** — `appendCondBranch`'s own
comment says so. `appendOp` can only refuse to append *past* a branch it already knows about; it
cannot refuse a `jcc` appended to a block that has not recorded one **yet**, and such a block's
then-edge would then be missing from every CFG built from it. (`SplitLiveRanges` widens the hole: it
mints its ops through its *own* append — push to `module.ops`, then `opRefs.insert` — bypassing
`IrModule.appendOp` entirely.) `condBranchTargetOf` checks only the ops the index *names*; nothing
but this sweep looks at the ops it does **not** name. It is what makes the class unspellable rather
than merely unlikely.

**Where it runs.** `buildFuncCfg` — once per function, on **every** build (it used to be an
`AllocChecker` check, and so ran only under `spec-test` / `verify-warm-rebuild`). That is the sole
constructor of the CFG the allocator consumes, so the invariant is checked exactly where it is
relied on; and it is asymptotically **free** there, because `scanFunctionValueCount` beside it
already walks every op of the function. It adds one op-kind match per op and no new traversal.

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

> **These numbers are now MEASURED ON EVERY RUN, not remembered.** Every figure in this section — and
> every linearity claim below — used to rest on throwaway generated programs that were never
> committed, which made them unfalsifiable and free to regress silently. `maxon-shv2 scale-test` is
> the committed instrument: a six-rung ladder, **each rung double the last**, reporting per-rung and
> per-phase MEMORY. See `Testing/ScaleTestRunner.maxon`, or run it through
> `mcp__maxon-dev__run_scale_test`.
>
> ⚠ **IT IS AN INSTRUMENT, NOT A GATE.** It renders no verdict, there is nothing to pass, and there is
> no green light to chase — a number that looks surprising is a **reading to explain**. (It once had
> budgets, memory goldens and a PASS/FAIL/VOID/NOISY verdict; when `regalloc:liveness` reported NOISY,
> the response was to change the *instrument* so it would stop complaining. That is optimizing the
> gauge instead of the engine, and it is only ever tempting when there is a gauge that can be wrong.)
> **The deliverable is the trend: [`docs/optimization-log.md`](../docs/optimization-log.md)**, a dated
> table you read downwards. `--note="<why>"` appends a row; the last row is what every run reports its
> delta from.
>
> ⚠ **IT MEASURES MEMORY, AND NOT TIME — and it fits nothing.** Allocations and bytes are bit-for-bit
> reproducible, so a number that moved, moved for a *reason*; wall time is machine-dependent and a
> dated column of it would compare a loaded box in July against an idle one in August. And because the
> **ladder DOUBLES, the ratio between two rungs IS the growth** — **x2.00 is linear, x4.00 is
> quadratic**, read straight off the counts. A fitted exponent said nothing those numbers had not
> already said, and it dragged a residual behind it, and the residual dragged the NOISY verdict. For
> the milliseconds of a *single* compile, the compiler still times itself: `--log=compiler:debug`.
>
> **As measured, on the committed corpus (6 rungs, 27 KB → 922 KB of source) — allocation growth from
> rung 4 to rung 5, where the ladder doubles:**
>
> | phase | growth per doubling |
> |---|---|
> | every frontend + mid phase (`lex`, `parse`, `merge`, `lowerMaxonToStd`, `isel`, `encode`, …) | **x1.99** — linear, to two decimals, across the board |
> | `regalloc:splitting` | **x3.51** |
> | `regalloc:liveness` | **x3.69** |
> | `regalloc` (phase, = the sum of the above) | **x3.28** |
> | whole compile | **x2.73** |
>
> So the linearity claim HOLDS for the whole compiler **except** the register allocator's splitter,
> exactly as this section says — and it holds *visibly*: nineteen phases sitting on x1.99 while two sit
> near x3.5 is a statement that needs no interpretation and no threshold. That exception is now a
> number watched down the pages of a log rather than a caveat in a document.
>
> The suite found one thing this section did not know about: `elimTrivialBlockArgs` was **quadratic**
> and had become **54% of a large compile**. It applied its substitutions one at a time, re-walking
> every op and rebuilding every branch edge per substitution, and it rescanned every edge in the
> function for every block-arg. Both are now single passes; the emitted IR is byte-identical, rung 3
> compiles **2.2× faster** with **2.8× fewer allocations**, and the phase now sits on **x1.96** with
> everything else.

**That last exception is GONE: the driver is now incremental, and a split costs what it changed.**
It used to rebuild liveness AND re-sweep the whole function for pressure after every split — O(function
× splits), with `liveness` at ~80% of the allocation there.

**The ladder doubles, so the ratio between rungs IS the growth: 2× is linear, 4× is quadratic.**
`regalloc:liveness` grew **×3.69 per doubling** — visibly on its way to quadratic — against ×1.99 for
everything outside the allocator. It is now **×2.0**, and so is `splitting`. The allocator grows at the
rate the program does.

> ### ⚠ THAT ×2.0 IS TRUE OF THE SHAPES IT WAS MEASURED ON, AND THEY ALL HAVE MANY BLOCKS
>
> **The incrementality above is per BLOCK.** `dirty` is a `BlockSet`, `refreshAfterSplit` and
> `refreshPeak` loop over dirty BLOCKS, and the `PeakTree` has one leaf per block. Both shapes in the
> table — *"400 functions, one pressured loop each"* and *"ONE function, 100 pressured loops"* — are
> made of many small blocks, so a split's dirty region really is a small slice of the function and the
> tournament really does have something to be a tournament over.
>
> **On ONE straight-line basic block the dirty region IS the function and the tree has ONE leaf.**
> Every per-split "re-derive only what changed" then re-derives the whole block, and if that block also
> takes Θ(block) splits — N call results all live to one N-term sum — the two Θ(N) terms multiply.
> Measured on `Testing/ladders/genwidelive.sh <N> sum` (N = 100 / 200 / 400, 96 / 196 / 396 splits):
> `regalloc:splitting` was **0.65 s / 2.73 s / 11.49 s** — **×4.19, ×4.22 per doubling, a clean
> quadratic** — and the committed spec that is its N=800 rung
> (`specs-shv2/x64-large-frame-arg7.md`) took **53.9 s to compile**, 99.8% of it in `splitting` and 93%
> of the whole suite's wall time.
>
> **The dominant term was not the sweep. It was work the sweep computed and then THREW AWAY.** A
> FULL-POOL overflow outranks every confined one unconditionally (`peakOutranks`), so a confined rank
> can only ever be the peak when NO op in the whole function overflows its full pool — yet
> `analyzeBlockPressure` ran the exact Hall confirmation eagerly at every op that fit its full pool, on
> every split. Measured: the screen fired at **~44% of the ops**, each exact confirmation cost
> **~80,000 CPU ticks** against **~2,900 for the entire rest of an op's step**, and the total was **86%
> of `analyzeBlockPressure` and 75% of the whole per-split cost**. `refreshPeak` now sweeps for the
> full-pool half alone and re-derives the confined half only when the full-pool half comes back empty
> (`SplitScratch.confinedMode`); a split never RAISES an op's pressure, so a function leaves full-pool
> mode at most once and the one whole-function re-sweep that costs is paid at most once.
>
> Two smaller wastes went with it. `applyForbidden` re-ORed each clobber mask into **every live value**,
> when a split clears only the victim's and the fresh ids' masks and `forbidden` only ever ORs — so the
> other N-2 were re-derived onto a column that already held the answer, O(clobber-ops × live) per split,
> which on this shape is **past quadratic in its own right** (measured ×5.07 then ×5.70 per doubling).
> And `opAtBlockPos` copied the block's whole op list into scratch to answer one indexed lookup — it
> is gone entirely now: `fillLiveBeforeOp` re-walks that same block in the same analysis and its LAST
> step IS the peak op, so the index comes back from the walk instead of from a second reading of a
> block's execution order.
>
> **New reading, same decisions** (`valuesSplit` 96 / 196 / 396 unchanged, all 243 committed IR goldens
> byte-identical, suite 1789/0 with `VerifyIncrementalSplit` both off AND on): **0.14 s / 0.46 s /
> 1.58 s**, ×3.2 and ×3.5 per doubling; the spec compiles in **6.09 s** (8.8×) and the suite fell
> **56.3 s → 16.4 s**. On `scale-test`'s own corpus the CPU column does not move outside its noise band
> (`regalloc:splitting` −2.2%..+0.9% across five rungs, exponent ×2.04 against ×2.08) — and it should
> not: that corpus is pressured LOOPS, whose functions are in confined mode from their first analysis,
> so the deferral never engages. Allocations there fall **−2,124 .. −32,858** whole-compile, from
> hoisting the `ScannedValues` union out of two per-block loops.
>
> **It is still superlinear on this shape, and the reason is now four terms rather than one.** Per split
> at N=400: `analyzeBlockPressure` 45%, `sweepBlockPressure` 32%, `reindexSplitValues` 14%,
> `fillLiveBeforeOp` 5%, `SplitEdits.commit` 2.5%, `chooseVictim` 1.4% — and every one of the last five
> still grows **×1.9–2.0 per split per doubling**, i.e. each is its own O(block)-per-split walk. Making
> this shape LINEAR needs a per-OP incremental structure (a lazy range-add / range-max over op
> positions, which must survive op INSERTION shifting those positions) feeding all four consumers, plus
> a maintained victim priority for `chooseVictim`'s Θ(candidates) scan. That is a redesign of the pass,
> not a patch to it, and it is not done.
>
> > **Two of those four walks were also ALLOCATING, and that part was not a redesign.** A `PeakRank` is
> > a 72-byte heap object — which `PeakTree` already knew, being flat columns for exactly that reason —
> > and the argmax was minting one at every step that won. `peakOutranks` REPLACES on an exact tie, so
> > on a block where nearly every op overflows, `betterPeak` won at a large share of the ops:
> > **Θ(block) objects per block sweep**, Θ(block × splits) per function. `betterBlock` did the same one
> > level up, materializing **two** ranks per combine to read four fields, and `promote` runs
> > O(log blocks) combines for every dirty block of every split. Both now mutate a reused rank —
> > `analyzeBlockPressure` copies its scratch out exactly once (`PeakRank.snapshot`, and the copy is
> > load-bearing: return the scratch itself and the whole-function fold compares an object with itself,
> > reads a tie, and returns the LAST block — 15 `register-*` failures when tried).
> >
> > The two are complementary, and the shapes say which is which. **Ladder (ONE block, Θ(N) splits):**
> > `regalloc:splitting` allocations 137,687 / 544,949 / 2,169,459 → **130,053 / 510,015 / 2,019,925**,
> > bytes **−19.5% / −22.5% / −24.2%** — nearly all of it the per-op mint. **`scale-test` corpus (many
> > small blocks):** allocations 43,060 / 75,141 / 140,137 / 271,549 → **41,668 / 71,512 / 131,178 /
> > 250,430** (**−3.2% / −4.8% / −6.4% / −7.8%**, a share that GROWS with the rung because the combine
> > term is O(log blocks)), bytes −96,072 / −255,096 / −634,776 / −1,502,136. Every removed allocation
> > divides out at 72 bytes: the attribution is exact, not inferred. **The CPU column did not move
> > outside its noise band on either** (ladder ×3.22/×3.44 before, ×3.23/×3.41 after) and it should not
> > be claimed to have: this is an allocation term, and the allocation columns are the ones that are
> > exact. Decisions untouched — `valuesSplit` 96/196/396, every committed IR golden byte-identical
> > (`git status --short specs-shv2/` empty after a suite run), 1789/0 with `VerifyIncrementalSplit`
> > OFF and ON, wasm32-wasi 1597/0.

Three enabling changes made it possible, and each landed on its own with the byte-exact IR goldens
unmoved: an op's sequence number became **block-relative** (so inserting one op renumbers only its own
block), the live sets became **per-block editable lists** instead of CSR (so removing one value from one
block is O(1), not O(function)), and the `UseIndex` became an **editable record arena** with intrusive
per-value and per-block threads (so a split rebuilds one value's use list instead of the function's).

What the driver does now is compute a **dirty region** per split — the victim's whole pre-split live
range, its def and use blocks, and every block an op was spliced into — and re-derive only that. Each of
the four facts it rests on is exact rather than hopeful:

* **liveness** is a per-value backward walk from a value's uses to its def, so a value the split did not
  touch walks the same blocks and marks the same sets. Only the victim (whose range shrinks) and the
  fresh reload ids (short new ranges) move.
* **`forbidden`** is a monotone OR-accumulation, and the ops a split inserts (`storeSlotReg`,
  `loadRegSlot`, a constant materialization) all carry `implicitDefs == 0` and no physical def operand —
  so re-sweeping a dirty block re-ORs bits the values there already carry. The one mask that can SHRINK
  is the victim's, and that one is zeroed and rebuilt.
* **sequence numbers** are block-relative, so an insert moves only its own block's, and the `UseIndex`'s
  per-block thread finds exactly those records.
* the per-block **dead-phi count** and **register demand** are functions of that block's own ops and live
  sets.

The global peak is found by a **tournament** over the per-block peaks (a segment tree whose combine is
`peakOutranks` with the right child as candidate, so it reproduces the full sweep's tie-breaking exactly).
A per-split fold over every block would merely have moved the quadratic: the block count grows with the
function.

The decisions are UNTOUCHED — same peak, same victim, same order, same code — and the byte-exact
specs-shv2 IR goldens do not move, which is how that is checked. The failure mode of a wrong invalidation
set is a value that silently loses a clobber bit, hence a caller-saved register across a call: a
miscompile no test is obliged to catch. So the splitter carries a **verify mode**
(`SplitLiveRanges.VerifyIncrementalSplit`) which, after every incremental update, also computes liveness
and pressure from scratch and PANICS on any difference — live sets, forbidden masks, dead-phi counts,
block demands, def-point pressures, the whole use index, and the chosen peak. It is off by default (it
makes every split O(function) again); flip it to `true` and run the suite plus the scale corpus.

### The per-split re-analysis: what it is allowed to ALLOCATE

The driver re-analyses after every split, so anything the sweep does per CALL is paid K times for a
function needing K splits. Three such costs were superlinear, and **not one of them was a traversal** —
they were allocations and a row copy, hiding inside an algorithm whose *asymptotics* everyone had
already agreed were fine. `SplitScratch` (one set of buffers per function) and `HallScratch` (the
exact confirmation's five columns) hold them now.

| site | was | why it mattered |
|---|---|---|
| `hallVerdictAt` | heap-allocated 5 columns per call, **2 of them per live value** inside `augmentValue` | Confinement is a property of a value's **whole live range**, not of the clobber op. N accumulators live across a call in a loop are confined at **every op of that loop**, so for N=8 the screen's O(1) early-out (`constrained ≤ smallestPool` = `8 ≤ 5`) does **not** fire and the exact matching runs at every op, on every iteration. "Bounded by 16×16" bounds ONE call and says nothing about the call COUNT. ⚠ **THE CALL COUNT WAS THE REAL BILL, AND THIS ROW ONLY TOOK THE ALLOCATIONS OUT OF IT.** Scratch made each call allocation-free; each still costs ~80,000 CPU ticks, and the count stayed at ops × splits. `refreshPeak` now defers the confined half until it can win, which is what finally bounds it — see the shape warning above. |
| `betterPeak` | a fresh row + a `wordsPerRow` copy on every **tie** | `peakOutranks` **replaces on an exact tie** — that IS the tie-break. In a pressured loop every op carries the same confined pressure, so **every op is a tie**. The peak now carries scalars only (`PeakRank`); the row is re-derived once for the winner (`fillLiveBeforeOp`) from the same backward transfer the sweep steps with. |
| `EffectivePools` / `defLiveAfter` | rebuilt over the whole **value space** per split — and the value space GROWS with every split | Θ(K × values) of allocation for columns whose contents are recomputed anyway. `popcountWord` was also Kernighan's clear-lowest-set loop, i.e. one iteration **per set bit** — its worst case on its commonest input (an unconstrained value's mask is the whole 14-register pool). It is SWAR now. |

`ReachCache` additionally **outlives the split**: its rows are a pure function of `(peak block, def
block)` over a CFG splitting cannot change, and the peak block does not move across the splits of one
pressured loop, so K BFS walks collapse to one. `retargetTo` is the sole invalidator and
`reachableFromPeak` **asserts** it was called — a row walked from a different peak would flip
`killsValueAtPeak` and silently pick a different victim.

**None of this changes a decision, and the gate proves it rather than the argument doing so:** the
`specs-shv2` goldens are byte-identical, and the `E5001` cliff (accept/reject boundary AND exact
diagnostic text, swept over N accumulators across a call in a loop) is unmoved against a
parent-commit build. The exponent does **not** move, and no design in this space moves it: Belady
ranks by farthest-next-use, so the victim is the value whose range is *widest* at the peak — on a
loop-carried accumulator that range **is** the function, and any "dirty region" is the whole of it.

> **⚠ REUSED SCRATCH IS NOT ZEROED SCRATCH — BUT NOT FOR THE REASON THIS DOC USED TO GIVE, AND THE OLD
> REASON IS FIXED.** This block used to warn that `Array.resize` hands back a reused buffer's old
> bytes, citing a measurement: push 77 eight times, `clear()`, `resize(8)`, and **all eight entries
> read back 77**. That was TRUE when it was written (`44dba203e`) and was FIXED THE SAME DAY by
> `3504ded93` ("the slots above `length` must be ZERO — resize() was handing back the dead"), which
> cites this exact repro as the bug it closed — for scalars it was garbage, and for MANAGED elements it
> was a use-after-free. Re-run today the eight entries read back **0**.
>
> THE CAPACITY-SLOT INVARIANT (`stdlib/Internals.maxon`) now holds: **every slot in `[length,
> capacity)` reads ZERO**, upheld by erasing a slot on the way OUT of the live range (`clear`,
> `remove`/`pop`, and a shrinking `resize`). The C# bootstrap's emitted runtime implements the same
> invariant independently (`MaxonToStandardConversion.EmitVacateElementRange`), and the bootstrap is
> what compiles this compiler.
>
> So a lengthening `resize` IS safe for zeroes. The buffers in `HallScratch` / `SplitScratch` /
> `ReachCache` are still explicitly re-initialized over the extent they are read (`Array.refill` writes
> `[0, count)`; `Array.growFilled` writes what an extension added; `bitsetCollectRow` and
> `targetOpOperands` `clear()` their outputs), and that IS still load-bearing — because most of them
> need a **non-zero** value in every entry (a `NoStoreAnchorPressure`, a `numBlocks` "unset", an
> out-of-file register sentinel), which no invariant will hand you. Reuse is safe because every reader's
> extent is WRITTEN, not because `resize` refuses to zero.
>
> **⚠ TWO ARRAY ALIASES WITH DIFFERENT RANGED ELEMENTS USED TO UNIFY SILENTLY. THEY NO LONGER DO.** An
> `Array`'s element must be a typealias, so the two sides are always named:
>
> ```
> typealias Narrow = int(0 to 16)      typealias NarrowCol = Array with Narrow
> typealias Wide   = int(0 to u64.max) typealias WideCol   = Array with Wide
> ```
>
> Passing a `NarrowCol` to a parameter typed `WideCol` used to **compile clean**, because
> `HaveMatchingTypeParams` NORMALIZED a ranged element to its BASE TYPE before comparing — throwing the
> range away, so `Narrow` and `Wide` compared equal. Memory stayed safe (element width is
> dictionary-passed WITH the value, so the stride was right); what you got was **silent TRUNCATION** —
> 300 written through the wide parameter read back 44. Same root cause as
> `specs/array-clone-element-size.md`.
>
> `e4146cf8e` ("a generic's RANGED element type is part of its type") CLOSED the argument-passing path.
> It is now a compile error:
>
> ```
> error E3005: argument type mismatch for 'col': expected 'DenseColumn', got 'RegNumColumn'
> ```
>
> Verified against the current bootstrap. This is why `HallScratch.regOfValue` is finally typed
> `RegNumColumn` — it holds register numbers, and it spent a while widened to `DenseColumn` with a
> comment telling you not to fix the name.

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
   this **list colouring**, which is **NP-hard** — `Compiler/Targets/Shared/HallCondition.maxon`'s
   `hallVerdictAt` header carries that argument and is the one place it is made. It is not a pressure
   problem and the splitter cannot see it: the values *fit* (five confined values, five callee-saved
   registers) and yet a greedy order can still fail.

   Concretely: biased coloring would honour a copy hint that handed a **callee-saved** register to a
   value that did not need one, and a value that could live nowhere else then found none — the
   colorer died with every register blocked. Two sequential loops containing calls were enough. The
   mitigation is `preferredVolatilityMask`: **a hint may never take a register outside a value's
   preferred volatility half while that half still has one free.** Copy elision is worth a `mov`; it
   is never worth a register the value cannot otherwise obtain.
   (`while-loops.sequential-loops-across-a-call`.)

   ⛔ **IT IS A MITIGATION, NOT AN EXACTNESS RULE, AND THIS ENTRY USED TO SAY OTHERWISE** — *"greedy
   is exact only if the scarce class is protected"*, which reads as "protect it and greedy is exact".
   A5g measured four programs where the half IS protected and the greedy still loses; their failing
   points come back FEASIBLE from `hallVerdictAt`. See RULE 1 in "The three rules" above for what
   replaced the invariant that sentence propped up.

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

   > **The bracket had to be widened before that "never `E5001`" was actually true.** Surfacing
   > confined peaks at ops that clobber nothing surfaced them for values the *eviction-point* split
   > cannot kill — a loop-invariant read on **both** sides of a call inside the loop is live across
   > that call around the back edge no matter how its after-peak uses are rewritten. Six of them
   > against the five callee-saved registers left `chooseVictim` with nothing, and `noVictimAtPeak`
   > panicked on a program that fits fourteen registers twice over. The relief is `SplitScope.everyUse`
   > — rewrite **every** use, so the original's only reader is its own store — which is the same forced
   > bracket paid per use instead of once. See Rule 2 and `register-spill.
   > loop-invariant-read-across-a-call-in-a-loop`.

1. **`maxlive` is exact for the program AS LOWERED — so a false `E5001` means the IR itself
   carries a value the author did not write.** Chordal ⇒ χ = ω = maxlive, and liveness is
   per-program-point, so values live on disjoint paths correctly do **not** interfere. The
   diagnostic can only be wrong if something *upstream* put a surplus value into the IR, and the
   tell is a blocking value the ranking reports as **"read 0 times in the loop"**.

   > ⚠⚠ **THAT TELL IS NECESSARY, NOT SUFFICIENT, AND THIS ENTRY SAID "always".** A **0** means
   > only that no *op* of the loop names the value; branch-edge args are not counted (they are not
   > reloads after the author's array rewrite, so the rank key is right to exclude them). A
   > **loop-carried `var` the loop only ASSIGNS** — written in one arm, read after the loop —
   > reads 0 for exactly that reason and is **not** a surplus: it is a loop-header phi the author
   > wrote, defined inside the loop, and no live-range split shortens its range (its store would
   > anchor at its own header block, its reloads at its in-loop edge uses), so `isColdSpillable`
   > refuses it on the DEPTH of that def. MEASURED on the spec harness's own `MaxonArgs.parse`
   > (BATCH43): **33** values at the peak, **22** of them loop-header phis of that shape (21 `var`
   > flags plus the counter), **11** printing 0. Nothing upstream over-produced; the loop's working
   > set really is 33 **as lowered**, and the remedy is the one the message names — hold the flags
   > in one record or array so the loop carries one value instead of twenty-one. ⇒ **Read a 0 as
   > "look at the IR", not as "the IR is wrong".**

   > **CORRECTED (this cost real debugging time).** This entry used to claim the surplus was a
   > *copy-related pair* — a block arg and what the back edge passes it — "counted twice" unless
   > biased coloring collapsed them. **That is not how it works.** Those two are never
   > simultaneously live (the arg dies at the edge; the phi is defined at the successor's entry),
   > so liveness never counts them twice, and coloring runs *after* the splitter has already
   > decided `E5001` — it cannot move the number either way. Biased coloring buys registers-used
   > and copies-emitted, which is worth having, but it is not what makes the pressure model
   > honest.

   The real surplus was **dead loop-header phis**: on-the-fly SSA minted one phi per mutable var in
   scope, a phi the loop never reads is *self-sustaining* through its own back edge, and liveness
   correctly holds it live around the whole loop. Two sequential loops with six accumulators each
   demanded **17** registers where the true working set is **9**. The parser now carries a phi only
   for the names the loop ASSIGNS, so it does not mint them at all, and `pruneDeadBlockArgs` deletes
   the ones that survive that (see the Std passes).

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

## The emitted allocator — three layers, one zeroing contract (Workstream R / S1–S6)

shv2 emits its runtime natively, so the allocator is shv2's to *write*, not to inherit. It is
**built**, in three files that stack, and the split is by the question each answers:

| Layer | File | Answers |
|---|---|---|
| **Page arena** | `Compiler/Runtime/SlabArena.maxon` | *"where does an 8 KiB chunk come from, and when does it go back to the OS?"* — a bitmap chunk allocator over `osReservePages` reservations committed a granule at a time, the radix `page → mspan` reverse map, and (S6) the granule scavenger. |
| **Object layer** | `Compiler/Runtime/SlabRuntime.maxon` (+ `SlabClasses.maxon` / `SlabClassTable.maxon`) | *"which slot of which span serves a 37-byte request, and where does it go when it dies?"* — Go's 68-class ladder, mspans, mcentral, an mcache at full shard width, and an OS-direct tier above 32 KiB. |
| **Box layer** | `Compiler/Runtime/MmRuntime.maxon` | *"what does a MANAGED value's memory look like?"* — the 24-byte header (destructor · size · refcount), `__mm_incref`/`__mm_decref`, the destructor and deep-clone cascades, the leak gate. |

⭐⭐ **THE BOX LAYER HAS NO ALLOCATOR OF ITS OWN, AND THAT IS THE WHOLE OF S4.** `__mm_alloc(size,
destructor)` asks `__slab_alloc` for `header + size` bytes in ONE request and `__mm_free` gives that same
pointer back to `__slab_free`; the span the slot came from knows its class, so the box layer computes no
size class, keeps no bucket and holds no `.data` state but the live count the leak gate reads. Between S2
and S4 it did carry a second, 16-byte-granular free-list allocator — a stopgap for the days when the layer
beneath it reclaimed nothing — and that is gone: **two size-class ladders over one heap is not a cache, it
is a fork.**

Consequently the **header's `size` field holds the size the CALLER asked for**, not a rounded one. Its one
reader is `__mm_free`'s poison range, which wants exactly the bytes the program could write.

### The zeroing contract

> **The allocator ALWAYS returns zeroed memory.** Zeroing is a property of the
> allocator, not a thing each caller is trusted to remember.

`__mm_alloc` leans on this so completely that it **does not write the refcount** — 0 is the born state
(`owners - 1`), and re-establishing a guarantee the allocator already makes is the habit that hides an
allocator bug inside every call site.

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

| Go | shv2 |
|---|---|
| `mallocgc(size, typ, needzero bool)` | `__slab_alloc` (zeroes) + `__slab_alloc_raw` (does not) |
| `if needzero && span.needzero != 0 { memclrNoHeapPointers(x, size) }` | same rule, same place — but per SLOT, see below |
| `mspan.freeindex` (bump cursor) | `mspan.bump_next` |
| `memclrNoHeapPointers` | `StdOp.memFill`, a **size ladder** the backend emits (below) |

⚠ **`needzero` is a BUILD-time argument here, not a runtime parameter.** v1 threads a flag through
`__slab_alloc_needzero → __slab_alloc_class`, which puts a branch on the hot path of every allocation in the
language and an extra argument in every call. shv2 builds two bodies from one builder
(`buildSlabAllocClass(name, zeroed:)`), and `DeadFunctionElimination` means no program that uses one carries
the other.

A slot reaches a caller from one of two places, and they differ in whether the memory is
dirty:

- **the free list** — a recycled slot, still holding the previous occupant's bytes plus
  the free-list link written into `slot[0]` when it was freed. **Must be memFilled with zero**, and is.
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
> all. **Do not build an eager intrusive free list.** (v1 deleted its own `__slab_build_freelist`
> for this reason; shv2 never had one.)

This is finer-grained than Go, whose `needzero` is per-span: Go re-zeroes even never-used
slots in a span that has seen one free; a per-slot cursor never pays for a slot that was
never dirtied.

**Invariants** (every mutation site re-establishes them; `SlabRuntime.maxon`'s header restates them beside
the code):
```
INV-1  free_count == |free_list| + (bump_end - bump_next) / slot_size
INV-2  bump_end   == base_addr + slot_size * total_slots      (derived, never stored)
INV-3  every byte in [bump_next, bump_end) is ZERO
```
INV-1 **traps** (`RuntimeAbort.slabSpanExhaustedPastItsEnd`): a `free_count > 0` with an empty free list and
an exhausted bump region would hand out a slot past the span's end.

The case a reviewer will not believe, so state it: a span can return to mcentral carrying
**both** an unconsumed bump region **and** a populated free list (allocate 3 slots from a
fresh 1024-slot span, free all 3 → `free_count == total_slots` → returned, holding 3
free-list entries and 1021 virgin slots). Both survive; INV-1 holds.

### Poison-on-free, and the two passes it costs

`__mm_free` overwrites the dead payload with `PoisonByte` (`0x3F`) before returning the slot. It is
**unconditional and not a flag**: a use-after-free that reads bytes nothing overwrote returns exactly the
value it wrote before the free and is invisible to every test, whereas `0x3F` reads as a conspicuous 63 and a
word of it (`0x3F3F3F3F3F3F3F3F`) is a non-canonical x64 address that FAULTS when dereferenced.

⚠⚠ **A RECYCLE THEREFORE COSTS TWO PASSES OVER THE SAME BYTES — poison at free, zero at alloc — and that
is a STATED OPEN COST.** The halves live one layer apart because they are different obligations: the poison
is a MANAGED-BOX discipline (it wants the caller's size and is meaningless for a green-thread stack), the
zero is an ALLOCATOR obligation. Collapsing them means giving up either a debugging guarantee or a
correctness one. A **virgin** slot pays neither, which is exactly what the bump region is for.

### `__slab_alloc_raw` — the escape hatch, and its audit rule

Go's `needzero=false`. Keep the caller set as small as Go does (`rawbyteslice`,
`rawstring`, `growslice`). It has **no caller in shv2 today** and `DFE` drops it from every program; it is
built so that admitting a caller is an argument against the rule below rather than an invented body.

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

### The OS-direct tier — above `SlabMaxSmallSize`

A request past 32 KiB is its own mapping, with its byte count and a magic word in a **16-byte prefix** — 16
rather than 8 so the pointer handed back keeps the 16-byte alignment every record layout above assumes.

⚠ **The prefix is not an optimization, it is the only place the length can live.** A free has to know how
many bytes to hand back, and the `page → mspan` reverse map cannot answer: a map MISS *is* the OS-direct
sentinel (`ArenaMapNoSpan`), so registering these pages would destroy the signal that identifies them.

⛔ **v1's answer — a 512-entry array with a linear scan — is deliberately NOT ported: it has a CEILING.**
Its `__slab_os_direct_alloc` exits with sentinel 134 once 512 mappings are live. shv2 routes every box above
32 KiB here, so a program holding 513 large buffers would abort in the allocator for no reason of its own. A
prefix has no ceiling and frees in O(1); the magic word recovers the fault v1 gets from failing to find a
pointer in that array (a double free, or a pointer that was never ours — without it a bogus free reads user
data as a byte count and unmaps an arbitrary region).

### The scavenger — giving memory back (S6)

**Since S6 there is one**, `__Builtins.scavengeMemory()`, and it answers the bytes it handed back. Three
steps in one call, and the middle one is the whole design:

| Step | What moves |
|---|---|
| **grace** | every span parked on an mcentral list is marked `SEEN_FREE`. Nothing is released. |
| **release** | a span that was ALREADY `SEEN_FREE` is destroyed — reverse-map slots cleared, chunk run back to the arena's bitmap, mspan header onto the metadata free list. |
| **decommit** | every 64 KiB commit granule whose chunks are now ALL free loses its physical backing (`osDecommitPages`) and its commit bit. |

⭐⭐ **THE TWO-EPOCH GRACE (v1's, and it is what v1 got right).** A span that empties and refills between
two calls is reset to `ACTIVE` by `__slab_refill`'s install, so it never reaches the release arm — the
active working set is never released, never memzeroed and never decommitted. **The first call after a
population is dropped therefore returns exactly 0**, and the second returns the bytes.

⛔⛔ **THE DECOMMIT IS AT THE ARENA'S GRANULE, NOT AT THE SPAN — AND THIS IS WHERE v1 IS WRONG.** v1
decommits a span's own footprint, `ceil(slotSize · totalSlots / 8192) · 8192` at the span's base. That range
is 8 KiB-granular, and `madvise` gives it one of two answers on a 16 KiB-page lane (**arm64-macOS**):
`EINVAL` for an unaligned base — a silent no-op, since nothing checks the result — or, for a base that
happens to be aligned, a **length rounded up that throws away the first 8 KiB of the next span.** Green on
x64 either way.

⚠ **That pair is DERIVED from `madvise`'s documented contract and from the page sizes this tree already
records, not MEASURED on a 16 KiB host** — v1 cannot be run at all (it does not build), and shv2 has no
arm64-macOS runner here. What IS measured is that the granule form works, on x64-windows and on
wasm32-wasi. The derivation is what decides the design; a lane that can run it should confirm the v1 shape
fails there.

shv2 releases the span's CHUNKS instead and decommits at the arena's 64 KiB commit granule,
which is a whole multiple of every page size the lanes have. **Go answers the same question the same way**:
its scavenger works on the page allocator's free runs, rounded to `physPageSize` with a
`minPages = physPageSize / pageSize` floor (`mgcscavenge.go`), never on spans.

⛔ **THE COMMIT WATERMARK HAD TO GO, AND ITS DELETION IS THE SCAVENGER'S PRECONDITION.** The arena used to
record its committed region as one word — `[0, committedChunks)` — which is only expressible while commits
go one way. A scavenger drops a granule wherever the chunks happen to have gone free, in the MIDDLE of that
range, so the committed set stops being a prefix on the first call. It is a per-granule **commit bitmap**
now: 16 words on the reserving lane, 1 on the other.

⛔ **NO BACKGROUND SCAVENGER, and that is a property of this runtime rather than a preference.** Go's is a
goroutine paced against the GC's heap goal at 1% of mutator time. shv2 has no GC and so no heap goal to
pace against; its scheduler is cooperative with no timer to hang a background G on; and green threads exist
on **one** of the lanes, so a scavenger built on them would not run in a wasm or POSIX program at all. What
Go contributes and this does take is the shape *underneath* the pacing.

⚠ This paragraph used to open its middle clause with *"its scheduler is single-M"*, which G1 made only
half-true: shv2 has a worker M and runs one **by default**, not by construction. The clause that carries the
argument is the other one — a cooperative scheduler has no preemption point to hang a background goroutine
off, whatever its M count — so the stale half is simply gone rather than qualified.

⚠ **ON wasm32-wasi `osDecommitPages` EMITS NOTHING and the rest still runs.** Linear memory only grows, so
there is no backing to drop — but the grace advances, the chunks go back to the arena, and any class can
then reuse them instead of growing linear memory again. A documented no-op with an ISA reason, never a
panic.

### INV-4 — every chunk the arena hands out reads ZERO

**Chunk recycling is the second way to break INV-3, and S6 is what made it reachable.** Before it, a chunk
had only ever come from a fresh `osReservePages`/`osCommitPages`, which every lane zeroes. A recycled chunk
holds the previous span's slots: poisoned payloads, and in each box header a **refcount**. `__slab_refill`
cuts a span whose whole bump region is presumed zero, and `__mm_alloc` does not write the refcount because 0
is the born state — so an unzeroed recycled chunk is a heap that frees boxes at the wrong time, with no
diagnostic.

> **INV-4 `__slab_arena_free_chunks` memzeroes the run it releases**, so every chunk
> `__slab_arena_alloc_chunks` hands out reads zero — virgin or recycled, on every lane.

⭐⭐ **THE ZERO IS WRITTEN ON THE RELEASE PATH, NOT RECOVERED FROM THE OS ON THE CLAIM PATH, AND THAT IS THE
WHOLE DIFFERENCE.** What a recommitted page contains is four different answers — Windows
`VirtualAlloc(MEM_COMMIT)` zeroes, Linux `MADV_DONTNEED` refaults zero, macOS `MADV_FREE` **may hand the
same dirty page back**, and a wasm no-op decommit leaves the bytes verbatim. Go guards that with a
per-platform `needZeroAfterSysUnused()`; v1 takes the always-true branch on reback. **shv2 needs neither**,
because the zero is ours and is written into committed memory before any decommit can touch it: Darwin's
dirty page is dirty with our zeroes, and wasm's verbatim bytes are the same zeroes.

⛔ **THE FAILURE SHAPE THIS AVOIDS IS NAMED AT `StdOp.osDecommitPages`** — a future per-OS
`pagesAreZeroAfterReback` flag, set false for macOS and forgotten for wasm, so **wasm and macOS break
together while Windows and Linux stay green**, which is the least detectable outcome available. There is no
such flag to write here; one unconditional `memFill`, no lane test, nothing for anyone to make conditional
later. **Do not move the zero to the claim path to "save" it on virgin chunks** — that is the same
conditional wearing a different name, and the virgin case is exactly the one the OS already paid for.

### The `memzero` the backend emits (`StdOp.memFill`)

**Not one instruction — a SIZE LADDER**, and that is why it is an op rather than a Std-IR loop. The dominant
caller zeroes a size-class slot (8/16/24/32/48/64…), and a naive `rep stosq` there is a large regression: it
costs ~20–40 cycles of startup before writing a byte, dwarfing the 1–4 plain stores an 8–32 byte slot needs.
(ERMSB/FSRM improve throughput and short `rep stosb`; they do not remove `rep stosq`'s setup.)

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

⚠ **The size dispatch branches INSIDE the encoder, never between IR blocks.** `memFill` is a body op, and
lowering panics if one ever appears as a block terminator — the ladder is one macro-op to every pass above
the encoder.

**`memFill` is declared FLAG-CLOBBERING** — the ladder `cmp`s the length to pick an arm, so it writes EFLAGS,
and `StdOpMeta.clobbersFlags: true` is what stops `recordFusableCompare` hoisting a `cmp` feeding a
`condBranch` across it. (v1's `memcpy` op declares its flags fact `false`, correct for a bare `rep movsb`;
copying that metadata onto memzero would be a silent miscompile.)

⚠ **Do not go looking for a `setsFlags` on `TargetOpMeta` to declare this on — there isn't
one, deliberately** (deleted 2026-07-14: 40 writes, 0 reads, and it *contradicted* the Std
tier). The flags fact lives **once**, at the Std tier, because its only consumer —
`recordFusableCompare`'s compare/branch fusion — runs on StdOps **before** lowering, since its answer is what
decides what the lowering emits. See the comment on `TargetOpMeta` for the full story; it is a worked example
of this project's most expensive recurring bug, *one fact written down twice*.

### The two traffic layers, and why they must stay disjoint

`PhaseProbe` **sums** the TRACKED columns (`__mm_alloc_*`, one per box) and the RAW ones
(`__mm_raw_alloc_*`, what `__slab_alloc` handed out). `__mm_alloc` is itself a `__slab_alloc` caller, so
counted naively every box would be reported twice and `totalAllocs()` would read exactly double. The fix is
an **uncounted twin**, `__slab_alloc_box`, built from the same builder with `countRaw: false` and called only
by `__mm_alloc`, and only in a build that reads the counters at all.

There is deliberately **no `__slab_free_box`**: `__slab_free` credits no column on either route, so a twin
would be a byte-for-byte copy under a second name. It grows one on the day a COUNTED `__slab_alloc` caller
also frees (a green-thread stack returned at exit, say) — which is the day
`builtins-mm-counters.md`'s `raw-live-equals-raw-total` goes red, which is why that case is pinned.

The v1 implementation of all of the above is `stdlib/Internals.maxon` (the slab) and
`maxon-selfhosted/Compiler/Targets/*/` (`emitX64MemzeroOp`, `arm64EmitMemzeroOp`,
`emitMemzero`). What was taken and what was left is recorded item by item in `SlabRuntime.maxon`'s and
`SlabArena.maxon`'s headers — the multi-OS-thread apparatus (the global lock, the per-P remote-free
MPSC queues, the `owning_p` gate) is absent because `__slab_shard_row` answers one row today, and that is a
decision to revisit when green threads can run the allocator on more than one OS thread, not an omission.

⚠⚠ **THAT DAY IS NOW REACHABLE ON DEMAND, AND THE ALLOCATOR IS WHAT STOPS IT.** G1 gave shv2 a real
processor structure and a real worker M; `MAXON_MAX_PROCS=N` runs green threads on N OS threads, and a
green thread that only COMPUTES is correct there. One that ALLOCATES is not: `track0/alloc-torture.maxon`
dies at `MAXON_MAX_PROCS=2` with **exit 86** (`slabSpanExhaustedPastItsEnd` — this allocator's own INV-1
trap), which is the sharpest statement of the sentence above that exists. `SchedRuntime.maxon`'s header
enumerates every other item in the same position, so the rung that shards this allocator has a list rather
than a search.
The **two-epoch scavenger** was on that list until S6 and is now taken — its GUARD verbatim, the mechanism
underneath it deliberately not; see "The scavenger" above for the `madvise` alignment fact that decides it.

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

### Compiler self-tracing — the `Log` events (Workstream O)

Every event family above is emitted by the **runtime**. **Nothing lets user Maxon source put an
event into the ring** — and shv2 *is* user Maxon source. That gap is what Workstream O closes, with
a `__DebugStream` builtin in `maxon-sharp` and four new codes. It is what makes shv2 debuggable
**once the harness goes parallel**: a stderr line from one of N interleaved workers cannot say which
worker, which compilation unit, or which phase it came from, so `Logger`'s text sink stops being
readable at P1.0a. See **PLAN.md → "Workstream O"**.

`Log` events take the **free `0x60–0x6F` range** (the schema is frozen; `0x5F–0xFD` was unused):

| code | event | payload after the 8-byte entry header |
|---|---|---|
| `0x60` | `LOG_PHASE_BEGIN` | `gt`(8) · `p_id`(8) · `phase_id`(2) `rsvd`(2) `unit_id`(4) — 32B |
| `0x61` | `LOG_PHASE_END` | *(same)* |
| `0x62` | `LOG_EVENT` | `gt`(8) · `p_id`(8) · `cat`(1) `lvl`(1) `event_id`(2) `unit_id`(4) · `arg0`(8) · `arg1`(8) — 48B, the Dbg shape |
| `0x63` | `LOG_TEXT` | `gt`(8) · `p_id`(8) · `cat`(1) `lvl`(1) `len`(2) `unit_id`(4) · UTF-8 tail zero-padded to 8 |

Two invariants, both load-bearing:

- **The hot tier does not allocate.** `LOG_EVENT` carries numeric args and a `u16` `event_id`;
  names are interned at compile time into a **`MXDS_STRS`** PE blob (the `MXDS_TAGS` mechanism,
  reused), so the monitor prints real names for free. A formatted-`String` log inside the register
  allocator would allocate into the very `mm` stream being read, and `Logger`'s `LazyMessage` thunk
  does not prevent it — the closure env is built at the call site regardless of the level check.
  `LOG_TEXT` is the allocating escape hatch, for the rare human message.
- **Every event carries `gt` + `p_id`** (via `LoadCurrentP`, NULL-guarded, as `EmitDbgCallCore`
  does) **plus a `unit_id`**. Without them the ring is a shuffled pile; with them the monitor
  reconstructs one timeline per worker and per compilation unit.

⇒ **Obligation on Workstream R1:** shv2's backend must emit this builtin too, or the trace dies at
the self-host boundary.

## Spec-test harness

`maxon-shv2 spec-test [dir]` (default `specs-shv2`) is shv2's own spec runner —
`Testing/{SpecParser,SpecTestRunner,SpecWorkerPool}.maxon`, compiled by `maxon.exe` like the rest of
shv2, so it can use the full stdlib (File, String, `Subprocess`, `async`).

`SpecParser.parseSpecFile` extracts `<!-- test: NAME -->` markers + the ` ```maxon ` block + one
expected block (` ```exitcode ` or ` ```maxoncstderr `) into a `SpecTest{name, source, expectation}`
(`SpecExpectation` is a union — no sentinel). It scans **only the `## Tests` section** (up to the
next STOP heading — `SpecParser.RegionEndHeadings`, today just `## Deferred`): deferred tests live
under a marker-less `## Deferred` section, because **HTML comments do not nest** —
`<!-- … <!-- test: … --> … -->` closes at the first `-->`, so a comment-wrapped deferral would
still be run. An ORDINARY `## ` heading does **not** end the region — a spec may organize its cases
into sections (`/specs/array-sort.md` uses six `## Stage N` headings), and a rule that stopped at
any heading silently ran 12 of that file's 40 cases and reported green.

`SpecTestRunner.runOneSpec` runs **one** spec: it writes each test's source verbatim (headerless,
code at line 1) to a temp fragment and spawns `<compiler> build` as a **subprocess** through the
single `runProcess` choke point — which isolates a compiler crash to one test and exercises the real
CLI. For a `compilerError` test it normalizes the fragment's absolute path to `<fragment>` (line/col
stay shv2-native) and compares; for an `exitCode` test it runs the produced exe and compares the
exit. `Main` resolves the compiler via `Process.executablePath()` (the runner tests itself), prints
per-test PASS/FAIL and `N passed, M failed`, and **exits non-zero iff any failed**.

**The pool** (`SpecWorkerPool.maxon`). One spec is one *job*, and the suite runs on N **persistent
worker subprocesses** — each of them this same executable, re-launched with `--worker-persistent`,
reading `JOB:singles:<spec>` lines on stdin and writing one result record per test on stdout. The
parent dispatches from a **slowest-first** queue, spawns an `async drainResultsThunk(child)` green
thread per dispatch so all N overlapped stdout reads are in flight at once, and serves whichever
drain completes first (`__Builtins.gtIsComplete` — a non-blocking peek, without which the parent
would park on the heaviest spec while every other worker sat idle). *This is not about the seconds*
(3.2 s → 0.6 s): `async` is a mechanism shv2 must eventually **emit**, and the pool is its dogfood.

**Worker-count invariance is the gate.** `--workers=1` and `--workers=N` must print **byte-identical
stdout**, so nothing is reported as it arrives: the parent knows each spec's ordered test list
*before* dispatching (it parses the specs to weight the queue), checks every drained record against
that list name-for-name — a lost, extra, reordered or unexpected record is a **panic** — and
reassembles the buckets into spec-listing order for the single reporter, `Main.reportResults`. Each
spec gets its own scratch dir (`.spec-tmp/<spec>/`) because test names are unique *within* a spec but
not *across* specs. A worker that dies **aborts the run** with its stderr; there is no retry, because
a dead worker is the harness crashing, not a flaky test.

**Fragments.** `specs-shv2/fragments/x64-windows/<spec>/<test>.test` = the test source + its
generated **Target IR** (via `IR/Target/TargetPrinter.maxon` — an exhaustive `match` over every
`TargetOp`, so a new op is a compile error, never a silent `??`), captured through `build --emit-ir`;
or the normalized diagnostic for an error test. They are byte-deterministic and committed.

**Fragments are COMPARED REFERENCE, not gates and not outputs.** `checkTestFragment` **compares** the
emitted IR against the committed golden and, on a difference, hands the parent a `GoldenDrift.drifted`
that `Main` prints beneath the summary on stderr — it never touches the failed count or the exit code
(⚖ user ruling 2026-08-02; see `Testing/GoldenTracking.maxon` for what the gate role cost).
`--update-required` rewrites them instead of comparing, and that diff is the review. The comparison
runs only *after* the behaviour check has passed — a failing test's IR is noise — which is also what
makes drift reachable only alongside a PASS, and what lets the worker wire carry both facts in one
record per test (`WORKER_SINGLE_DRIFT`).

Together with the run, this is what watches the register allocator (see *What gates the allocator*):
the run **gates** correctness, the golden **records** whether it got worse — including in blocks the
test's single execution path never enters.

## Coverage scaffold

`Compiler/Coverage/CovSiteTable.maxon` carries `BlockSourceInfo` — a per-block source footprint
(file, opening line, deduped statement lines) that `IrModule` records at every block-creation site
and preserves through Maxon→Std lowering. The coverage-instrumentation pass and its site table are a
later deliverable that grows this file; today the metadata is recorded and unused.

---

## Known gaps in the built subsystems

Things that are *implemented but incomplete*, distinct from what simply hasn't been built (for the
latter, see `PLAN.md`):

- **A named type that resolves to nothing panics the compiler.** `TypeResolution` throws a `panic`
  for a type reference that is neither the `ExitCode` builtin nor a declared `typealias` — so a TYPO
  in a type position (`function f(x Scor)`) takes the compiler down instead of reporting E2003
  (`Unknown type: Scor`). Giving it a diagnostic needs a source span for the type REFERENCE, and the
  parser records spans only for ops and parameters today.
- **`Early`/`Late` operand position is packed but never read** — sound today only because no op has
  both explicit operands and a late implicit clobber.
- **No spill-slot coalescing** — every spilled value gets its own slot.
- **The splitter recomputes liveness once per split**, and several of its helpers are linear scans
  inside that loop. Correct, but superlinear in a way the rest of the codebase's "no `Map`, no
  hashing in the hot path" discipline avoids. A performance follow-on, not a correctness one.
- **No empty-block diagnostic (E3082).** An empty `if`/`else`/`while` body is a compile error in the
  language; shv2 accepts it and emits an empty block.
- **A comparison cannot be a VALUE.** Every compare is FUSED into the branch that consumes it and is
  never materialized, so `let flag = a > b`, `(a == b)` in an expression, and a chained `a == b == c`
  are all rejected in operator position. Lifting this is one mechanism — boolean materialization
  (`setcc`) — and it lifts all three at once. A boolean-VALUED condition (`if flag`, `while true`)
  already works: the parser emits the truth test `flag != false`, which gives the branch the `cmp` it
  fuses with.
