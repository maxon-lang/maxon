# Context Parameters (Dependency Injection)

## Context

The goal is dependency injection in Maxon. "DI" names three different mechanisms, so the first decision
was which one:

| | Binding resolved | Missing dep | Runtime cost | Runtime swap |
|---|---|---|---|---|
| **Context parameters** | compile time | compile error | zero | no |
| Compile-time wiring (Dagger) | compile time | compile error | zero | no |
| Runtime container | runtime | runtime panic | lookup + dynamic dispatch | yes |

**Chosen: context parameters.** Maxon is compile-time monomorphized with no reflection, so a runtime
container fights the grain (it would need a compiler-provided type key, dynamic dispatch, and shared
ownership of registered instances). Context parameters are zero-cost, turn a missing dependency into a
compile error, and are the lowest-level primitive — Dagger-style graph wiring can be built on top of them
later, but not the reverse.

A dependency is declared once as part of a contract, wired from a single composition root, and threaded by
the compiler — resolved and checked entirely at compile time.

**The feature is one concept.** `using` appears in exactly one place — a trailing clause on a function
signature — and lowers to one thing: a synthetic trailing parameter. Object-level dependencies need no
separate mechanism; they go through a static factory whose signature carries the clause (see below).

## Design rulings (user-decided)

1. **A contextual requirement's type MUST be an interface.** Resolution is therefore one rule: *which
   in-scope provided value's concrete type conforms to this interface?* Type-directed, **name-irrelevant**,
   **exactly one** match required. Enforces dependency inversion, and rejects generic type-parameter
   requirements via the same rule.
2. **`provide` is block-local only** — function bodies and nested blocks, lexically scoped. `main()` is the
   composition root. No top-level/module-scope providers in v1.
3. **Contextual bindings are resolvable inside closure bodies**, captured into `__env`.
4. **Functions declare requirements as a trailing SIGNATURE CLAUSE**, alongside `returns`/`throws` —
   matching how Maxon already expresses signature facts. Not a parameter modifier, not a body declaration.
   The requirement is part of the contract, so it belongs where callers look; and interface method
   declarations (which have no body) can carry it.
5. **Requirements are declared, never inferred.** A function that calls something with a requirement must
   declare that requirement itself, even as a pure pass-through. Chosen over call-graph inference because it
   keeps this a contained parser change, and it is **forward-compatible**: adding inference later only makes
   currently-erroring programs compile, so nothing written now breaks. *This ruling is also why ruling 4 is
   right — because callers are obligated, the requirement is contract, not implementation.*
6. **No contextual fields.** Object-level dependencies are handled by a **static factory method** whose
   signature carries the clause, storing into an ordinary field. This keeps one injection point instead of
   two, and needs **zero new ownership machinery** — see below.
7. **A dependency instance is owned by its green thread; implicit threading stops at the `async`
   boundary.** A function spawned with `async` must carry no `using` clause and must satisfy its
   requirements from `provide`s within itself — **each `async` entry is its own composition root.** This is
   what keeps the DI mechanism from silently creating cross-thread shared mutable state. See Ownership and
   Concurrency.

## Syntax

```ebnf
function_decl = 'function' NAME '(' param_list ')' [ 'returns' type ] [ throws_clause ] [ using_clause ]
using_clause  = 'using' NAME interface_type { ',' NAME interface_type }
provide_stmt  = 'provide' NAME '=' expression        (* any statement position *)
```

Clause order is `returns` → `throws` → `using`. Follow the **`module` contextual-keyword precedent**
([LANGUAGE_REFERENCE.md:166](docs/LANGUAGE_REFERENCE.md:166)): recognize `using` and `provide` only in their
unambiguous positions so neither breaks existing code using them as identifiers. Both are absent from the
keyword map and unused as identifiers repo-wide.

```maxon
function loadUser(id UserId) returns User using conn Database
    return conn.query("...")
end 'loadUser'

function renderProfile(id UserId) returns Html using conn Database   // pass-through (ruling 5)
    let u = loadUser(id)
    return Html.of(u)
end 'renderProfile'

function main() returns ExitCode
    provide pg = Postgres.create()       // Postgres implements Database — composition root
    let page = renderProfile(UserId{7})
    return 0
end 'main'
```

### Object-level dependencies: the factory idiom (ruling 6)

A type that needs a dependency takes it through a static factory carrying the clause, and stores it in an
ordinary field:

```maxon
type PostgresUserRepo implements UserRepo
    let conn as Database                 // ordinary interface-typed field

    static function create() returns Self using conn Database
        return Self{conn: conn}          // ordinary field initialization
    end 'create'

    function findUser(id UserId) returns User
        return conn.query("...")         // reads the field — no synthetic param, ABI unchanged
    end 'findUser'
end 'PostgresUserRepo'

function main() returns ExitCode
    provide pg = Postgres.create()
    let repo = PostgresUserRepo.create() // requirement resolved here
    return 0
end 'main'
```

**This needs no new compiler machinery whatsoever.** `static create(t Tagged) returns Self` /
`return Self{t: t}` — a borrowed interface-typed param stored into an interface-typed field — is already a
documented, supported pattern ([LANGUAGE_REFERENCE.md:958](docs/LANGUAGE_REFERENCE.md:958)). The only
difference here is that `conn` arrives via a clause rather than an explicit parameter, so the existing
borrow-into-field retain semantics apply unchanged. It also keeps the method ABI uniform for witness
dispatch, since the dependency lives in the object rather than in `findUser`'s signature.

## Lowering model — synthetic trailing parameters

Each requirement becomes a **synthetic trailing parameter**. This is what preserves zero IR change:
synthetics are ordinary entries in `IrFunction.ParamNames`/`ParamTypes`, so every downstream pass treats
them as normal parameters. **There is exactly one injection point**: a branch in `FillDefaultArgs`
inserted immediately before the missing-argument error.

**PreScan needs no structural change** — the decisive advantage of the clause form. `PreScanFunction`
already parses the signature and only then skips the body. The clause is parsed right after the throws
clause, before the body is skipped. Order matters:

1. Parse the `using` clause (after throws, before overload-registration-name resolution).
2. Compute `registrationName` from **declared params only** — synthetics must not participate in overload
   mangling, or two functions differing only in requirements would mangle differently yet be callable
   identically.
3. Append synthetics to the `paramNames`/`paramTypes` used to build `IrFunction`.
4. Record the synthetic index range in `_functionContextParams[registrationName]`.

Because synthetics land in `IrFunction.ParamNames` at PreScan time, they ride the existing module
seeding/merging machinery and **cross-file calls work for free**; the index set is what still needs explicit
carrying in the module state. The name binds as a body-local via `EmitParameters`, which already sets
`OwnershipFlags.IsParam` — exactly "not owned, skip decref at scope end". So
**borrow-by-default comes free**, and storing such a binding into a field retains it by the existing field
rules.

## Ownership and concurrency

Borrowing is only sound while the callee's lifetime nests inside the provider's scope — and `async`
spawning breaks exactly that assumption. Investigation split this into a half already handled and a half
needing a ruling.

### Already handled — no work required

- **Escape via spawn.** The async-call lowering builds a `managed_mask` bitmap, increfs every refcounted
  arg at the spawn site, and the trampoline decrefs after the spawned function returns — written precisely
  to stop "a managed arg could be freed by the caller's scope-end decref before the GT actually runs." A
  hard assert throws at compile time on any mask/incref asymmetry rather than corrupting refcounts.
- **Refcounting is PLAIN, not atomic, and needs none** (EC10, 2026-08-27): an `async` call is a coroutine
  of the calling green thread, so a box is never reachable from two of them. The failure atomics would
  otherwise guard against — "concurrent refcount inc/dec lose updates -> premature free / leak -> heap
  corruption" — is prevented by removing the second party rather than by serialising against it.
- **Retention analysis covers synthetics for free.** `ParameterRetentionAnalysisPass` indexes parameters
  positionally via `StdParamOp.Index`, so synthetic contextual params are indistinguishable from declared
  ones. Its conservative rules — unknown callee ⇒ retained, and **indirect calls retain every argument** —
  also cover closure-captured providers.

So a provider stays **alive** correctly regardless of how many green threads borrow it.

### The real hazard, and the rule

> ⚖ **WHERE THE HAZARD ACTUALLY LIVES (EC10, 2026-08-27).** An `async` call creates a **coroutine of the
> calling green thread**, which never migrates and never reaches a P ring, so the "stolen onto another P"
> hazard below does not arise for `async`. It arises for **`spawn`** (reserved, `SERVICES_DESIGN.md`),
> which is what W212's stealing tier was built for. ⇒ per-green-thread remains the only sound granularity
> and ruling 7 stands; re-read this before extending the feature, because the hazard has moved to a
> primitive that does not exist yet — not because it went away.

Refcounting protects the *allocation*, not the *contents*. The scheduler is genuinely multi-threaded
(`CreateThread` / `pthread_create`) **with work stealing** — `__gt_steal_work`, dequeue chain
"runnext → local → global → steal". **Green threads migrate between processors**, which rules out per-P
ownership: a GT holding a reference to "its P's instance" can be stolen onto another P, putting two Ps on
the same instance. **Per-green-thread is the only sound
granularity** — the GT carries its own instance wherever it runs.

Hence ruling 7: **a function spawned with `async` must have no `using` clause.** It establishes its own
providers internally. Rejected at the spawn site with a diagnostic naming the async boundary and the
unsatisfied requirement.

Re-evaluating the provider per spawned GT was considered and rejected: `provide db = Postgres.create(cfg)`
re-run in the child must capture `cfg`, so **the recipe's inputs are shared anyway** — isolation is
incomplete — and construction cost would scale with task count.

**The guarantee's exact boundary, stated honestly.** This ensures *the DI mechanism never implicitly
creates cross-thread sharing*. It does not stop explicit sharing: passing a provider as an ordinary
argument to an async function, or spawning with a closure that captured one, still shares it — exactly as
for any other value today. Narrowing that further would require tracking provider-ness through the type
system, which is out of scope for v1.

## Resolution algorithm

Runs in `FillDefaultArgs` per null arg slot whose index is in the callee's synthetic set.

State: a new parser field `_contextBindings : List<(string Name, string TypeName, int Depth)>`. It must be
**separate from `VarRegistry`**, which is a flat dictionary with snapshot-based scoping and cannot express
innermost-first ordering. Both `provide` bindings and a function's own clause bindings are pushed onto it
— the latter is what makes chains work under ruling 5.

1. Walk `_contextBindings` **innermost-first**, restricted to bindings live in the current lexical scope.
2. **Shadowing pre-filter:** among bindings sharing a name, only the innermost is a candidate.
3. **Conformance match:** the requirement type is always an `IrInterfaceType` (ruling 1), so match iff
   `TypeConformsToInterface(candidateConcreteType, iface.Name)` — which already handles transitive `extends`,
   typealiases, and primitive-extension conformance.
4. **Exactly-one rule:** 0 matches → `SemanticNoContextProvider`, hinting with the in-scope contextual
   bindings and their types so a near-miss non-conforming provider is visible, and naming the fix (add a
   `using` clause or a `provide`). ≥2 → `SemanticAmbiguousContextProvider`, naming every candidate and its
   declaration line.
5. **Materialize via `ResolveExprValue`** — build an `ExprResult.VarRef` and route through it. **Do not
   reuse `info.Value` directly.** `FillDefaultArgs` runs after `ParseArgList`'s cross-block argument
   pinning, so a binding from another `IrBlock` needs the `MaxonVarRefOp` reload. That same routine
   provides the closure-capture path ruling 3 depends on. Reusing `info.Value` naively yields a
   cross-block SSA violation surfacing far from the cause.
6. **Mark used:** add the name to `_referencedVars` when it satisfies a downstream call, so
   `CheckUnusedVariables` does not fire E3012 on a pass-through clause. Under ruling 5 pass-throughs are common and expected. A requirement *neither* read
   nor propagated should still report E3012 — that is a genuinely dead declaration.
7. Fall through to the existing per-arg type-check loop unchanged.

## v1 restrictions (each with its reason)

- **No `using` clause on `main`** — nothing provides to the entry point. `main` must use `provide`.
- **No `using` clause on a function spawned with `async`** (ruling 7) — same shape as the `main` rule, and
  not a coincidence: both are *composition roots*, entry points with no provider scope above them. `main` is
  the synchronous one; every `async` entry is a concurrent one.
- **No `using` clause on a closure literal** — a closure would need its own synthetic param. Closures may
  still *reference* an enclosing contextual binding via capture (ruling 3); only declaring one is barred.
- **An implementing method's `using` clause must match its interface declaration's clause exactly** — same
  names, types, and order. A conformance check like return-type matching; it is what keeps the witness ABI
  uniform. A dependency the interface does not declare belongs in a field via the factory idiom.
- **Callers cannot pass a synthetic param explicitly** — it is not part of the declared signature. Override
  is done by shadowing the `provide` in the caller's scope.

## Implementation slices

**Slice 1 — Lexer.** Add `Using`/`Provide` to the token type and the keyword map. LSP
hover/completion is driven off the keyword map, so that surface comes free. Also update
[vscode-extension/syntaxes/maxon.tmLanguage.json](vscode-extension/syntaxes/maxon.tmLanguage.json)
(not generated).

**Slice 2 — Clause parsing + synthetic params.** A shared `ParseUsingClause()` returning
`(names, interfaceTypes)`, enforcing that each type resolves to an interface. Called from every signature
site so both passes agree: `PreScanFunction` per the four-step order above, `ParseFunction`,
`PreScanInstanceMethod`, the instance-method parse, and **interface method declarations**. Add
`_functionContextParams`, mirroring `_functionDefaults`. Add `FunctionContextParams` to the module state
beside `FunctionDefaults`,
including its `Clone` and `Merge`, and copy it when seeding from and copying state to a module.
Add the interface-conformance clause check where method signature conformance is validated.

**Slice 3 — `provide` + binding stack.** `ParseStatement` gains a `Provide` branch delegating to a
`ParseVarOrLetDecl`-shaped path so it reuses `CheckReservedDeclName`, self-field-shadow checks, and
`MaxonAssignOp` emission — then pushes onto `_contextBindings`. `EmitParameters` pushes synthetic params
as contextual bindings too. Hook `PushScope`/`PopScope` to truncate, and clear in `SetupFunctionParsing`.

**Slice 4 — Call-site injection.** One branch in `FillDefaultArgs`, between the defaults case and the
missing-argument throw; all four call paths funnel through it. Three companion fixes, all consequences of
synthetics being real entries in `ParamNames`:
- `ParseArgList` "too many arguments" must count **declared** params only.
- `ParseNamedArg` does `ParamNames.IndexOf(name)` and **must exclude synthetics**, or a caller could pass
  one by name and leak the lowering.
- **`SelectOverloadByNamedArgs`** computes
  `requiredParams = c.ParamNames.Count(n => n != "self")` and **must also exclude synthetics**, or any
  requirement-bearing overload is silently filtered out of every call site. *Verified against source — the
  subtlest breakage in the feature.*

Model the shape on the division desugar: one private emit method documented as THE single place the
policy lives. Copy its *shape*, not its new-IR-op strategy.

**Slice 4c — the async boundary (ruling 7).** At the site that emits `MaxonAsyncCallOp`, reject a callee
carrying synthetic context params — do **not** inject them. The diagnostic must name the async boundary,
the unsatisfied requirement, and the fix ("`provide` it inside the spawned function"). Because `async`
spawns a *named* callee (`StdFuncRefOp(asyncOp.Callee)`) the callee's synthetic set is always statically
known, so this is a simple lookup — no analysis required.

**Slice 5 — Diagnostics.** `maxon-bin/Compiler/ErrorCodeRegistry.maxon` IS the registry and it is
AUTHORED: take the next free number in the right band, add a case, write the code that emits it. Do not
pick a number that is already claimed — `Testing/ErrorCodeSelfTest.maxon` refuses a duplicate on every
`spec-test`, naming both claimants, because two agents once took E3099 the same day. **Never reference a
code by its number outside the registry**; use the generated member. Semantic band: `SemanticNoContextProvider`, `SemanticAmbiguousContextProvider`, an interface-clause-mismatch
code, and `SemanticContextAcrossAsyncBoundary` (ruling 7). Parser band: non-interface requirement type and
the restriction set above.

**Slice 6 — Spec + docs.** `specs/context-parameters.md`, plus [docs/BNF_SYNTAX.md](docs/BNF_SYNTAX.md)
(keyword list :47, `function_decl` production), `LANGUAGE_REFERENCE.md`, `QUICK_REFERENCE.md`. Document the
factory idiom as the sanctioned route for object-level dependencies.

**Order:** 1 → 2 → 3 → write spec with all tests `disabled-test` → 4 → 4c → 5 → enable tests one at a time.
Slices 1–3 change no existing program's behavior; slice 4 turns the feature on. **Land 4c with 4**, not
after — shipping injection without the async guard would mean silently threading providers across thread
boundaries, which is the one outcome ruling 7 exists to prevent.

## Feasibility: zero IR/codegen change

**Verdict: holds.** The parser is the only thing that constructs `MaxonCallOp` argument lists; nothing
downstream re-derives arity from source. Synthetics are ordinary `IrFunction` entries, so
`MonomorphizationPass`, `MaxonToStandardConversion`, `BorrowCheckPass`, `RefcountOptimizationPass`, and the
emitters cannot distinguish one from a hand-written param — after `FillDefaultArgs` returns there *is* no
difference. Interface-typed params are already a solved path: `FillDefaultArgs` accepts a conforming
concrete struct for an `IrInterfaceType` param, and `MonomorphizationPass` specializes on the concrete
arg type.

*Note:* monomorphization specializes the whole call chain per concrete provider type, so code size grows with
(chain depth × provider types). Expected for zero-cost static dispatch; worth watching, not a blocker.

## Spec tests — `specs/context-parameters.md`

Frontmatter: `feature: context-parameters`, `status: experimental`,
`keywords: [using, provide, context, contextual, implicit, ambient, dependency]`, `category: functions`.
All start as `disabled-test`, enabled one at a time.

| Test | Description |
|---|---|
| `basic-resolution` | One `provide`, one requirement; call site mentions nothing. |
| `conformance-match` | `provide pg = Postgres.create()` satisfies `using conn Database`. |
| `threading-through-layers` | Three-deep chain, every frame declaring the requirement; only innermost reads it. **The headline capability under ruling 5.** |
| `passthrough-clause-not-unused` | Regression guard for the E3012 hazard (step 6): a pass-through requirement consumed only by propagation must not report unused. |
| `dead-requirement-reports-unused` | A requirement neither read nor propagated *does* report E3012. |
| `shadowing-inner-provide` | Inner `provide` shadows outer; correct value each side of the block. |
| `closure-captures-provider` | Closure body references an enclosing contextual binding (ruling 3) — captured into `__env`. |
| `missing-declaration-error` | Caller omits the pass-through clause → `SemanticNoContextProvider`, message names the fix. |
| `no-provider-nonconforming` | Non-conforming provider in scope → error lists the near-miss candidate. |
| `ambiguous-provider-error` | Two conforming bindings → `SemanticAmbiguousContextProvider`, names both. |
| `provide-out-of-scope-error` | `provide` inside `if`; call after the block → no-provider (proves `PopScope` truncation). |
| `using-not-interface-error` | `using c Postgres` (concrete type) → rejected (**ruling 1**). |
| `using-on-main-error` / `using-on-closure-error` | The two declaration-site restrictions. |
| `interface-method-clause-inherited` | Interface declares a requirement; an implementation is called through **witness dispatch** — proves uniform ABI. |
| `interface-method-clause-mismatch-error` | Implementation's clause differs from the interface's → conformance error. |
| `synthetic-param-not-passable` | Caller attempting to pass a requirement by name → `unknown parameter name`. |
| `overload-arity-with-using` | Overload set where one member declares a requirement — guards the `SelectOverloadByNamedArgs` fix. |
| `ownership-borrow-not-consumed` | Provider used by two calls then read again — proves borrow-by-default (no E3102, no double-free). |
| `async-callee-with-requirement-error` | `async worker()` where `worker` carries a `using` clause → `SemanticContextAcrossAsyncBoundary` (**ruling 7**). |
| `async-callee-self-provided` | A spawned function that `provide`s its own dependency compiles and runs — the sanctioned pattern. |
| `async-sibling-isolation` | Two spawned GTs each construct their own instance; assert the instances are distinct (e.g. per-instance counters diverge). Proves per-GT ownership rather than accidental sharing. |
| `async-provider-outlives-spawn` | A provider explicitly passed into an async call survives the parent scope ending before the GT runs — exercises the spawn-site incref / trampoline-decref pairing. Leak-gate sensitive. |
| `factory-injection` | **The ruling-6 idiom:** `static create() returns Self using conn Database` storing into a field; the object outlives the factory scope and a method reads the field. Leak-gate sensitive (borrow → field retain). |
| `cross-file-resolution` | Callee declaring a requirement in another file — verifies module seeding of the synthetic index set. |
| `zero-ir-delta` | `RequiredIR` test: the implicit form produces **byte-identical IR** to a hand-written explicit-parameter equivalent. The executable form of the feasibility claim. |

## Verification

1. `scripts/build.sh` — exit 0, **zero warnings**.
2. The error-code self-test passes (it runs as part of every `spec-test`).
3. `mcp__maxon-dev__run_spec_test` with `filter: "context-parameters"` — walk the disabled tests green one
   at a time.
4. **Full suite neutral** — no regression against a pre-change baseline established first-hand; never
   trust a claimed-green tree.
5. **Leak gate** — no run exits 101. The `async-*` and `factory-injection` cases are the ones most likely
   to trip it, since they exercise the borrow→retain boundaries.
6. **Async cases under load** — run the suite at `--workers=1` and a high worker count and confirm
   identical results. Ruling 7's whole purpose is preventing cross-thread sharing, and a scheduler race
   would surface as worker-count-dependent flakiness rather than a clean failure.
7. `zero-ir-delta` green — the machine-checkable form of "no IR/codegen change."
8. `maxon fmt` over the whole tree (project code-review convention).
