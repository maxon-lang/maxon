# Background Services for Maxon — design + spec tests

## Context

> ⚖ **DATED NOTE, 2026-08-27 (EC10).** `async` is settled and it is **not** a threading primitive:
> an `async` call starts a COROUTINE of the green thread that made it, published only to that green
> thread's own queue and driven only by its owner. It overlaps *waiting*, never execution, and a box
> is therefore reachable from exactly one green thread — which is what lets reference counting be
> plain rather than atomic (`MmRuntime.emitAdjustRefcount`; `SchedRuntime.maxon` states the
> invariant). ⇒ **`spawn`, reserved below, is where threads first meet**, and it inherits two things:
> the whole GMP substrate `async` no longer reaches (per-P rings, stealing, the worker loop — built,
> correct, unreached), and the obligation to say what crossing a thread does to ownership. The
> "Send is a MOVE" rule this document already reserves is that answer, and the plain refcount is what
> makes it load-bearing rather than a style choice: a `spawn` that let two green threads hold one box
> would not be slow, it would be wrong.

Maxon has a faithful Go-style GMP green-thread runtime — per-P run queues, work stealing,
`runnext`, Go's 61-tick fairness interval and `_StackMin`/`_StackGuard`, growable 2KB stacks,
IOCP/kqueue I/O. **But it exposes zero communication primitives to user code.** No channels,
mailboxes, actors, mutexes, condvars, or queues. The entire user-visible concurrency surface is
`async` / `await` / `Promise` / `sleep`.

The gap is visible in the tree. `maxon-shv2/Testing/SpecWorkerPool.maxon` (1107 lines) passes
messages over **subprocess stdin/stdout with a string protocol** because nothing in-process can do
it, and hand-rolls a `select` by polling green-thread status at line 1085:

```maxon
if __Builtins.gtIsComplete(drain.inner) != 0 'hasAnswered'
```

`stdlib/Builtins.maxon:121` exports `Promise.inner` *specifically* to enable that poll. An exported
field whose documented purpose is to let users fake `select` is a feature request written in the
source.

**This feature adds `service`: an isolated unit of state plus a serialized message loop, reachable
only through a handle.** It is a more structured version of Go's channels — where Go gives an
untyped conduit and leaves the protocol implicit, a service makes the protocol a *declaration* the
compiler checks.

## Decisions taken with the user

| Question | Decision |
|---|---|
| Interaction shape | **Reply optional.** A handler returning a value is awaitable RPC; returning nothing is fire-and-forget. |
| Lifecycle | **Explicitly spawned handles**, multiple instances. |
| State | **Actor-style isolation** — serialized handlers, no locks. |
| vs async/await | **Layer on it, do not replace it** (evidence below). |
| Target | **`maxon-sharp` bootstrap**, specs in `specs/`; shv2 follows as **P1.5c** (see below). |
| `select` | **In scope** — the motivating case. |
| Memory safety | **Send is a MOVE.** Arguments are owned by the service until returned. |
| Scope | **Design doc + spec tests only.** No compiler implementation. |

### Why services do not replace async/await

Recorded because it contradicts the initial hypothesis, and the evidence is one-sided:

1. **`async` is not function coloring.** It is a *call-site prefix* (`let p = async f()`); there is
   no `async function`. The "async infects every signature" problem that motivates replacing
   async elsewhere **does not exist here.**
2. **`Promise` is a first-class two-parameter type** (`Promise with (T, E)`), stored in struct
   fields and arrays, across 39 tests in `specs/async-await.md` plus async-filesystem, async-tcp,
   sleep, subprocess, http-client.
3. **`await svc.handler(x)` returns a promise.** Services *consume* the async machinery.
4. They solve different problems — `async f()` is a one-shot stateless future; a service is a
   long-lived stateful mailbox with identity.

The construct to compare against is not `async`. It is the *absent* channel.

---

## Recommended design

**No new declaration form and no new member keyword.** A service is an ordinary `type`. The only
new syntax in the whole feature is `spawn`.

```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Integer)                                   // message: no return → send
		self.count = self.count + by
	end 'bump'

	export function divide(n Integer, by Integer) returns Integer throws MathError
		if by == 0 'zero'
			throw MathError.divideByZero
		end 'zero'
		return self.record(n / by)
	end 'divide'

	function record(v Integer) returns Integer                         // private helper
		self.count = self.count + 1
		return v
	end 'record'
end 'Calc'
```

```maxon
let c = Calc.create()                // a plain value — direct calls, fully testable
c.bump(3)                            // ordinary method call, synchronous

let h = spawn Calc.create()          // call-site prefix on a direct static call, like `async`
h.bump(3)                            // message send, fire-and-forget
let n = try await h.divide(10, by: 2) otherwise 0
```

### ⭐ The export boundary *is* the isolation boundary

`spawn` yields `Calc.handle`, whose method surface is **exactly `Calc`'s export surface**. Three
properties fall out, and the third is the one that matters:

1. **No method ever changes meaning.** Dispatch is decided by the *receiver's type* — a `Calc`
   value takes direct calls, a `Calc.handle` takes messages. Same spelling, and that is location
   transparency rather than ambiguity.
2. **Services are synchronously unit-testable.** Construct one directly and call its methods with
   no runtime, no mailbox, no green thread. A dedicated `service` declaration would have lost this
   for nothing.
3. **Self-send deadlock is structurally impossible.** A private helper is *not on the handle*, so
   `self.record(...)` inside a message can only ever be a direct call. There is no way to spell a
   self-send, so it needs no diagnostic — this deletes the proposed E3110 outright.

**Only `export` *instance* methods become messages.** `static function` is excluded structurally —
it has no `self`, and `spawn Calc.create()` calls it directly rather than through the handle.

> ⚠ **The one cost of overloading `export`, stated plainly.** Whether a type is a service is now a
> **whole-program** property: `spawn Calc.create()` anywhere makes `Calc` a service and subjects
> *all* its export methods to the service rules (reply-escape E3106, transferable-argument E3107).
> So a diagnostic can fire on a method in one file because of a `spawn` in another. **The
> diagnostic must name the spawn site that made the type a service**, or it will read as a
> non-sequitur. This is the price of the design and it should be paid in the error message.

### What the compiler synthesizes

Dispatch is a **union plus one exhaustive match** — not a handler table. That is forced: a message
handler must close over `self`, so it is a capturing closure, and **E3099** forbids storing a
capturing closure in a field, global, container, or union payload — every place a table could live.
The forcing turns out to be fortunate:

```maxon
union Calc.request               // SYNTHESIZED — one variant per EXPORT INSTANCE method,
	__shutdown(__reply ReplySlot)  // in declaration order. __shutdown is variant 0 so that
	bump(by Integer)               // adding a message never renumbers the synthesized one.
	divide(n Integer, by Integer, __reply ReplySlot)
end 'Calc.request'

function Calc.__loop(state Calc, mailbox __Mailbox)     // SYNTHESIZED — the whole design
	var running = true
	while running 'serve'
		match __Builtins.mailboxRecv(mailbox) 'dispatch'
			__shutdown(r) then running = Calc.__do_shutdown(state, r: r)
			bump(by) then Calc.__do_bump(state, by: by)
			divide(n, by, r) then Calc.__do_divide(state, n: n, by: by, r: r)
		end 'dispatch'
	end 'serve'
end 'Calc.__loop'
```

Four existing mechanisms fall out free: exhaustive `match` (dispatch cannot miss a handler);
**E2049's single-statement arms satisfied by construction** (each arm is one trampoline call — had
handler bodies been inlined, any handler containing an `if` would be rejected); `.unionCases` wire
tags; and struct-backed union metadata for future per-handler `priority`/`timeout`.

A fifth matters more than expected: **a closed union is the only mailbox whose contents can be
dropped.** On shutdown, un-processed messages own moved-in values; the compiler synthesizes an
exhaustive `Calc.__drop_request`. A closure-based mailbox holds erased environments, cannot know
what to free, and would fail the leak gate.

**`spawn` returns `Calc.handle`** — a compiler-synthesized companion struct, nominally distinct per
service. Precedent: `Shape.unionCases` is a synthesized companion enum in the union's namespace.

> ⚠ **Rejected: the per-instance-typealias trick.** `specs/per-instance-typealias.md` documents, as
> a *feature*, that per-instance aliases are `as`-convertible between instantiations. For an index
> that's a mild footgun; for a mailbox pointer it is type confusion that hands a `Calc.request` to a
> `Logger` loop, which matches it against the wrong variants and reads a payload that isn't there.

> ⭐ **This design needs no generics** — and that is a hard advantage, not a preference. The
> library alternative requires `ServiceHandle uses S, Request, Reply`, i.e. `async` inside a generic
> body. **Verified: that crashes the compiler today.** `MaxonAsyncCallOp`/`MaxonAwaitOp`/
> `MaxonTryAwaitOp` appear nowhere in `FunctionCloner.cs` or `MonomorphizationPass.cs`, so
> monomorphizing such a body hits `FunctionCloner.cs:564` — `throw new InvalidOperationException`,
> an unhandled crash rather than a diagnostic. Async and generics are currently disjoint universes.
> The declaration form has **fewer** prerequisites than the "lighter" library design.

### Ownership — the spine

> Every handler argument is **moved** into the service, owned exclusively for the handler's
> duration. A return is **moved back**. A moved-from binding is poisoned; assignment revives it.

```maxon
var buf = makeBuffer()
buf = try await store.process(buf) otherwise panic("store is live")   // out and back
```

Re-arming by assignment is not a new idiom — `specs/async-await.md` already documents exactly this
rule for promises.

**⭐ This deletes `Sendable` rather than adding it.** `Send`/`Sync` answer *"can two threads
reference this at once?"* — a question about **sharing**. Under move-only transfer there is never a
second reference, so the question does not arise. Three further reasons it would be wrong here:
Maxon's marker interfaces are singleton compiler hooks (the compiler scans for *the one* conformer),
not a conformance-set mechanism; `Array with T` would need conditional conformance, not emitted
until P2.2; and **refcounts are already unconditionally atomic** (`RuntimeEmitter.MemoryManager.cs:409,515`),
so there is no non-atomic fast path to opt out of. What remains is a smaller structural
transfer-shape rule, checked rather than declared — which is how Maxon does structural properties
everywhere else.

**Move is also simpler than the existing `managed_mask` protocol.** `LowerAsyncCall` increfs each
managed arg at the spawn site and decrefs in the trampoline *because `async f(x)` does not move* —
the caller keeps `x`, so two owners coexist. Services move, so there is one owner:

| | `async f(x)` today | `svc.handler(x)` |
|---|---|---|
| send site | incref, set mask bit | **nothing** — hand over the existing +1 |
| caller scope end | decref | **suppressed** — binding is moved-from |
| handler end | trampoline walks mask, decrefs | ordinary scope-end decref, unless returned |

The mask does not vanish entirely: it degenerates into the **drop map for abandonment paths** —
"this message will die without a handler running; which of its N untyped words are heap pointers?"
Five paths need that answer and it is not derivable at runtime.

#### ⚠ The one real tension

`docs/MEMORY_MANAGEMENT.md:293` states the bootstrap's model outright: *"Function parameters are not
owned by the callee. The caller retains ownership."* And the bootstrap aliases by default — `var b = a`
makes both point at one object. **A move contradicts the model the bootstrap is built on**, and the
guarantee is only real if the send site can prove uniqueness. (The rule is much closer to shv2's
static single-owner thesis; targeting the bootstrap forces this reconciliation.)

**Resolution: extend `BorrowCheckPass.cs`** — 336 lines, and the right home by *data*, not analogy.
It already builds, in one linear walk with a global op index: `assignsByValueId` (every assign,
binary-searchable), `lastUse` per variable (its NLL machinery), an `activeBorrows` map with
activation-at-assignment and expiry-after-last-use, and a rule that reassigning a borrowed-from
source kills the borrow. An alias is a `MaxonAssignOp` whose RHS is a bare reference — a shape it
already indexes, and exactly what shv2's E3102 keys on. `MaxonCallOp.ArgVarNames` already tells it
which caller variable each argument came from.

The rule must be **conservative-reject** (the guarantee is memory safety): an argument is sendable
iff *provably unique* — a fresh rvalue, or a local whose initializer was a fresh rvalue with no
aliasing assignment, field store, or container push since. Everything else is refused, naming the
aliasing binding and its line, in E3070's existing diagnostic shape.

> **Rejected: deep-clone on send.** A clone is not "returned to the caller," so
> `buf = await svc.process(buf)` would silently hand back a *different object* and identity-dependent
> code would silently break. It also hides an O(n) cost behind what looks like a call, in a language
> that makes costs explicit everywhere (`try` at every call site, no implicit conversions).
> **`.clone()` stays explicit** and is exactly how a user resolves a uniqueness rejection.

**One non-obvious extra check:** a handler must not return a value reachable from `self`, or the
caller ends up aliasing service state and every guarantee evaporates. Call it the **reply-escape
rule** — the mirror of send-uniqueness, checkable by the same local analysis. `self.buf.clone()` is
the fix, and here the clone is *right*, because the caller genuinely asked for a copy.

### Reply channel — a stackless green thread

**A reply slot is a real GT that never runs**: `stack_base = 0`, created blocked, never enqueued —
a wait token plus a result slot. Both the syntax designer and the runtime designer converged on this
independently, which is worth weight.

`Promise.inner` is unchanged (it holds a GT pointer today), `await` and `try await` are unchanged,
`__gt_is_complete` works on it unmodified, and **E3100 linear-await composes for free** because the
check keys on the binding, not on what produced it. Cost is a GT struct, not a 2KB stack.
Critically, **`__gt_await`'s free path already skips the stack free when `stack_base == 0`**
(`X86CodeEmitter.Runtime.cs:5138`) — that branch exists today for `mainThread`.

Two corrections this forces, both important:
- **The cell allocator must not be `__gt_spawn`** — spawn adds to `__gt_all_head` and increments
  `__gt_live_count`, which only the trampoline undoes. A cell runs no trampoline, so it would never
  be removed and `__gt_cleanup` would spin forever.
- **`__gt_await` must clear `ioYielded` before publishing `promise.waiter`.** For a real promise the
  trampoline's `pendingWaiter` path makes the spin unnecessary; for a reply cell the completer is an
  unrelated GT on another thread. This is a two-store change on the path **every existing `await`
  takes** — the highest-risk edit in the design.

### `select` — ship `awaitAny`, defer the statement

```maxon
let ready = try awaitAny(drains) otherwise (e) 'allDone'
	break
end 'allDone'
```

**Recommendation: a stdlib function returning the completed index, not a `select` statement.**
A statement form was designed (`on n = p then ...` / `timeout 0` / `on (slot,v) = any arr`) and it
reads better, but it loses on two counts that matter more:

1. **It would ship a new *unchecked* linearity hole.** An `any` arm consumes an array *slot*, and
   slot-level linearity is precisely the documented gap ("awaiting the same array slot twice is not
   statically caught — that needs ownership through the container, shv2's ownership milestone").
   v1 would delete a hand-rolled hack and replace it with a construct whose misuse is a runtime
   double-free. `awaitAny` **does not consume** — it returns an index and the caller awaits exactly
   one promise, so it is neutral on the *existing* hole rather than widening it.
2. **Zero grammar.** No arm syntax, no exhaustiveness rules, no interaction with `try`'s primary
   precedence — the `try (a/b)` wrinkle that catches every new expression-position construct.

It also composes uniformly: because a handler reply is an ordinary `Promise`, one waiting primitive
covers service replies, file I/O, and subprocess drains. There is no separate "channel select."

**Implementation is a park, not a poll.** The selecting GT registers on all K reply cells at once.
The single `waiter` slot forces a heap `MboxWaiter` record per mailbox (Go's `sudog`, same forcing
argument), with an atomic claim CAS so exactly one sender wins. **Do not take K locks** — Go orders
channel locks by address, which is wrong here because both platform locks are **recursive on the
wrong identity** (Windows `EnterCriticalSection` is per-OS-thread; arm64 spinlock's owner word is the
*P pointer*), and green threads multiplex over both. A GT parking while holding a mailbox lock lets
another GT on the same P take the *recursive* path straight into the critical section — not a
deadlock, but silent FIFO corruption. **Hard rule: no mailbox lock is ever held across
`__gt_context_switch`.** Register one lock at a time; no cycle can form.

The success criterion is checkable: `findReadyDrain` and the `sleep(PollYieldMs)` fallback both
delete, and `Promise.inner`'s `export` can be removed.

### ⭐ Deadlock freedom by construction — the acyclic blocking graph

Mutual reentrancy (A awaits B, B awaits A) is not diagnosed, it is **made unrepresentable**. The
compiler builds a directed graph over service *types* and rejects any cycle.

**Only blocking edges count.** An edge `A → B` exists iff a message of `A` **awaits a reply** from a
`B.handle`. A fire-and-forget send is *not* an edge, because a non-blocking send cannot participate
in a wait cycle. This is the whole design decision, and it is what keeps the rule from being
crippling: **peer-to-peer messaging stays legal** as long as it is fire-and-forget, which is how
actor systems are written anyway. Constraining all edges instead would ban large classes of
provably deadlock-free programs for no safety gain.

**Why acyclicity is sufficient, not merely suggestive.** An acyclic graph has a topological order.
The service lowest in that order awaits no one, so it always makes progress; when it replies, its
callers unblock; by induction on the order, every blocked caller eventually resumes. There is no
configuration in which everyone is waiting. That is a proof, and it is the static analogue of
lock-ordering discipline — with the ordering checked rather than documented.

**Edges are transitive through ordinary functions.** A message of `A` that calls a free function
which awaits a `B.handle` still produces `A → B`. This is a fixed-point over the call graph —
exactly `CheckAsyncYielding`'s existing shape (`SemanticCheckPass.cs:179`), and `IrCallGraph.cs`
already ships. Detection is Tarjan SCC over the blocking edges: any SCC with more than one member,
or any self-loop, is the error.

Edges are **by type, not by instance**, which is what makes them statically knowable — a handle in a
field, an array, or a `Map` still has a type. `E3107` already forbids sending a `Promise` into a
service, so the only way a message can block on another service is to call and await it in its own
body or a callee's. There is no back door.

```
error E3110: service call cycle — these messages can deadlock waiting on each other:
    Calc.divide       (calc.maxon:14) awaits →
    Logger.write      (logger.maxon:9) awaits →
    Calc.total        (calc.maxon:22) awaits → Calc.divide
  A message may not await a reply from a service that can await back.
  Break the cycle by making one of these calls fire-and-forget (drop the `returns`
  clause, or ignore the reply) — a non-blocking send is not part of the graph.
```

> ⚠ **The cost, recorded plainly: self-edges are banned, so two instances of the *same* service
> cannot await each other.** A `Worker` whose message awaits a reply from another `Worker` is
> rejected even though distinct instances would not actually deadlock. Type-level granularity cannot
> tell the instances apart, and the analysis must be conservative because the guarantee is
> deadlock freedom. Workarounds: make the peer call fire-and-forget and have the peer reply with a
> separate message, or split the role into two types. **This is the main thing users will hit**, and
> the diagnostic should name the workaround rather than just the cycle.

### Errors

The N-handlers-N-error-types problem **dissolves at the call site**: dispatch is a runtime
mechanism but the call is statically resolved. You wrote `c.divide(...)`, so the compiler knows
which handler's `throws` applies. The merge is always two-way — transport plus one handler:

```maxon
let v = try await c.divide(10, by: 0) otherwise (e) 'oops'
	match e 'why'
		ServiceError.stopped then return 70
		MathError.divideByZero then return 71
	end 'why'
end 'oops'
```

That is character-for-character the existing try-block synthesized-error-union syntax. For bare
`try` propagation (which needs exact type match), name the union: `typealias E = Calc.divide.errors`.
The diagnostic for getting this wrong should hand the user that exact line.

**E3073:** register `maxon_mailbox_recv` and `maxon_select_wait` in `SemanticCheckPass.cs` `IoStubs`
*and* the selfhosted `ioYieldBuiltinSet()`, or `spawn` fails its own yield analysis for every
service. Do **not** register `send` or `try_recv` — they complete inline and never suspend the
caller; listing them would let `async` wrap a genuinely non-yielding function.

### Shutdown

`shutdown()` enqueues a poison pill **behind** everything queued — a graceful drain, not a kill.
Dropping the last handle does the same, so ordinary programs need no shutdown boilerplate. Pending
replies of un-processed messages resolve with `ServiceError.stopped` rather than hanging their
awaiters — a *liveness* obligation needing its own spec test.

⚠ **`__gt_cleanup` gap:** its drain only frees GTs reaching `Completed`, but a service parked in
`recv` is `Waiting` and in no run queue — reachable only from the mailbox. Without a global mailbox
registry (`__mbox_all_head`, mirroring `__gt_all_head`) walked at cleanup, **process exit hangs
whenever a service is idle**, which is the steady state.

---

## Deliverables

1. **`specs/services.md`** — the spec, ~24 tests. Frontmatter `feature: services`,
   `status: experimental`, `category: concurrency`. Live tests: spawn/send/await, FIFO +
   serialization, independent instances, **the same type used directly *and* spawned** (the
   location-transparency property, and the test that would have been impossible under a `service`
   declaration), **private methods are absent from the handle**, message throws, named error union,
   call-after-shutdown, shutdown drains, shutdown resolves pending replies, `.unionCases` tags,
   handles in an array, move-in-and-back, `awaitAny`, and diagnostics (use-after-send, non-unique
   send, reply-aliases-state, sending a `Promise`, handle mismatch, double-await of a reply).
   **Deadlock-freedom tests carry their own group**, since the rule is the subtlest part of the
   design and each case must be pinned separately:
   - `services.cycle-two-services-refused` — A awaits B awaits A → E3110, asserting the full
     cycle path appears in the diagnostic
   - `services.cycle-through-a-free-function-refused` — the edge is transitive through an ordinary
     function, proving the fixed-point runs
   - `services.cycle-same-type-self-edge-refused` — a `Worker` awaiting a `Worker` handle
   - `services.fire-and-forget-cycle-is-legal` ⭐ — A sends to B, B sends back to A, both
     non-blocking, program runs to completion. **The test that pins "only blocking edges count"**;
     without it a later tightening would silently ban correct programs
   - `services.deep-acyclic-chain-runs` — A→B→C→D awaited end to end, no diagnostic

   `disabled-test` with the gating reason: per-message metadata, generic services (P1.6 witness
   tables), send-through-a-parameter (interprocedural fixpoint).
2. **`docs/LANGUAGE_REFERENCE.md` §14** — currently "Async/Await (Concurrency)" at line 4051.
   Add a Services subsection and TOC sub-bullets under item 14 (which has none today).
3. **`docs/BNF_SYNTAX.md`** — just `spawn_expr`, beside `async_expr` (§6.6). No new declaration
   production: `type_decl` is unchanged, and "which members are messages" is a *semantic* rule over
   `visibility_prefix`, not a grammar change.
4. **Proposed error codes, NOT registered yet** — `error-codes check` **fails the build** on a claim
   with no live emitting site, so the registry edit lands with the first emitting commit.
   **E3104 is the next free number** (verified: E3103 is the highest).

| code | name | fires when |
|---|---|---|
| E3104 | `SemanticServiceArgumentMoved` | reading a binding after it was sent |
| E3105 | `SemanticServiceArgumentNotUnique` | sending a value with a live alias |
| E3106 | `SemanticServiceReplyAliasesState` | an export method returns something reachable from `self` |
| E3107 | `SemanticServiceValueNotTransferable` | sending a `Promise`, interface value, or closure |
| E3108 | `SemanticServiceHandleMismatch` | `Logger.handle` where `Calc.handle` is expected |
| E3109 | `SemanticServiceBareTryUnnamedError` | bare `try` on a handle call without the named union |
| E3110 | `SemanticServiceCallCycle` | a cycle in the blocking call graph — the deadlock check |

E3100, E3102, E3070, E2049, E2051 and discarded-results are **reused as-is**.
**E3106, E3107 and E3110 must name the `spawn` site** that made the type a service (see the
whole-program caveat above) — the method they fire on may be in a different file from the cause.
**E3110 must print the full cycle path** with a file:line per hop; a cycle reported as a single
site is unactionable.

> A *self-send* code is deliberately absent: private helpers are not on the handle, so a direct
> self-send cannot be spelled. E3110 covers the case that survives — a cycle through other
> services, or a same-type self-edge between two instances.

### Files the eventual implementation touches (not this phase)

- `1-Lexer.cs` — **one** `TokenType` + `KeywordMap` row: `spawn`
- `2-Parser.cs` — `ParsePrimary` (:16199) for `spawn`, beside the existing `Async` arm.
  **No `ParseTopLevel` change and no pre-scan work** — a service is an ordinary `type`.
- `MLIR/Dialects/MaxonDialect.cs` — `MaxonOpKind` + op classes
- `MLIR/Conversion/MaxonToStandardConversion.Services.cs` (new), beside `.Async.cs`
- **Both** `StandardToX86Conversion.cs` and `StandardToARM64Conversion.cs` — CLAUDE.md requires parity
- `MLIR/Passes/BorrowCheckPass.cs` — send-uniqueness + reply-escape
- `MLIR/Passes/SemanticCheckPass.cs` — `IoStubs` (:106), `Run()` (:41-54); the E3110 cycle check is
  one method modelled on `CheckAsyncYielding` (:179), over `IrCallGraph.cs`
- `MLIR/Runtime/GtLayout.cs`, `RuntimeEmitter.Scheduler.cs` — mailbox layout + park/wake
- `stdlib/Builtins.maxon` — `ServiceError`, `ServiceExit`; later, un-export `Promise.inner`

> ⚠ **Do not follow the `implement-feature` skill's Step 11 literally** — 5 of the 8 directories it
> names do not exist, and its `AstToMaxonDialect` stage is not real: **there is no AST.**
> `2-Parser.cs` builds Maxon-dialect IR directly as it parses.

## Where this fits in the shv2 ladder

### ⭐ shv2 is a better host for this design than the bootstrap

The design's single largest weakness — move-on-send enforced over a **refcounted, aliasing-by-default**
language, needing a new uniqueness analysis bolted onto `BorrowCheckPass` — **does not exist in shv2**,
because shv2's ownership thesis already *is* this design's ownership rule. Verified against the ladder:

| Services need | shv2 status |
|---|---|
| Static single-owner moves, use-after-move | ✅ **P1.2 Wave C CLOSED** — `let u = t` / `s = t` MOVE, source poisoned, read is **E3102**, reassign REVIVES, conditional poison conservative. **This is move-on-send, already shipped.** |
| Params that consume | ✅ **P1.4a RULING (user, 2026-07-18)** — params BORROW by default, **CONSUME by use**, returns **ADOPT**. Wave 2 shipped a direct-sink param-consume analysis. **The `moves` parameter mode the bootstrap lacks is native here.** |
| Managed payloads in unions | ✅ **P1.3 Slice 2 CLOSED** — String/struct payloads with move-in (E3102 on source reuse), move-out (slot nulled, scrutinee `partiallyMoved`), and a **tag-conditional static destructor cascade** (`__destruct_<U>`). **That cascade is exactly the synthesized mailbox drop** the design needs at shutdown. |
| Path-sensitive moves | ✅ **P1.4a Wave 2** — drops reconciled at every join, drop on the LIVE edges, no runtime flags. |
| Structs + instance methods | ✅ **P1.1a Wave 3 CLOSED.** |
| Errors (`throws`/`try`/`otherwise`) | ⏳ **P1.4b — the current rung.** Needed for `ServiceError`. |
| `async` / `await` / Promise / GT scheduler | ⏳ **P1.5** (Runtime slice **R3**). Not started. **The one hard dependency.** |
| Generics, `Array`, interfaces/witness tables | ❌ **Not needed** — the companion-type design (`Calc.request`, `Calc.handle`) is monomorphic by construction. **P1.6/P1.7/P1.7a are not on the critical path.** |

### The escape-channel question — and why services do *not* have to co-land with P1.5

P1.5's entire thesis is that closures, `async`, and escape are **one mechanism** and must co-land:
*"a closure captures into an env block; a green thread captures into a task frame… land escape
single-threaded and add `async` later and you bolt a **second capture channel** onto it: v1's
`sys.dropTypeParam` split-brain mistake, exactly."*

That argument appears to apply here with equal force — a mailbox looks like a **third** capture
channel, which would say services must co-land too, making an already-enormous rung bigger.

**It does not apply, and the reason is the ownership rule.** Escape → `shared` exists because a
captured value has **two** referents, so it needs a refcount. **A moved value has exactly one owner
at every instant** (§ Ownership) — it is not captured, it is *transferred*. So a mailbox is not a
capture channel at all; it is a transfer channel, built from P1.2/P1.3/P1.4a machinery **that is
already shipped**, not from P1.5's `EscapeAnalysis`.

Two consequences worth stating:
- **Services are a clean successor rung, not a co-lander.** They consume P1.5's `async`/Promise/GT
  scheduler and add nothing to its escape analysis.
- **Services should *lower* P1.5's tracked metric.** P1.5 tracks *"% values promoted to `shared`* —
  if it's 40%, static ownership bought nothing." Sending a value instead of capturing it moves work
  out of the refcounted channel into the owned one. If that number is uncomfortable at P1.5,
  services are part of the answer.

### Proposed rungs

**`P1.5c` — services core.** ⭐ Immediately after P1.5, gated on P1.4b + P1.5.
`spawn`, the synthesized request union + `.handle` companion + dispatch loop, the mailbox, move-on-send,
reply cells, `ServiceError`, graceful shutdown, and the **E3110 acyclic-blocking-graph check** (pure
frontend analysis over `IrCallGraph` — no runtime, could land independently). Needs no generics,
no `Array`, no interfaces.

**`P1.7c` — `awaitAny` / select.** Gated on **P1.7 (`Array`)**, because the primitive takes an array
of promises (`awaitAny(promises PromiseArray)`). Services are useful without it; the worker-pool
dogfood is not.

> ⚠ **Sequencing note worth flagging to whoever schedules P1.5.** P1.5's stated acceptance test is
> the parallel worker pool, and its scope is *"minimal `async` = `async`/`await` + Promise + **the
> worker pool's needs**."* The pool's needs include a `select` — which is why the bootstrap's pool
> polls `Promise.inner` and why `Builtins.maxon` exports that field at all. **If P1.5 ships without
> a plan for `awaitAny`, shv2 will re-import the bootstrap's `inner`-polling wart** and inherit an
> export whose only purpose is to fake a missing feature. The fix is not to enlarge P1.5 — it is to
> keep `Promise.inner` unexported in shv2 and let the pool stay serial until P1.7c.

**Not on the ladder: P2.5 and P2.6 interactions.** P2.5 (closure dogfood) is unaffected — services
use no closures. P2.6 (per-function fan-out) already notes *"the runtime under it now exists, because
P1.5 brought R3 forward"*; services are a second consumer of that same runtime, not a new requirement.

### Specs in shv2

Per Workstream S, spec files are copied from `/specs` **on demand, by the rung that needs them** —
never as a bulk dump. So `specs-shv2/services.md` is created by P1.5c, carrying only the cases that
rung enables, with everything else `<!-- disabled-test: -->` plus the gating rung on the next line
(`<!-- P1.7c awaitAny -->`, `<!-- P1.6 generics -->`). **The ratchet applies: an enabled case may
never be re-disabled.**

## Verification

Design-phase only — no compiler changes, so the gate is review plus spec well-formedness:

1. **Specs parse and are discovered.** `specs/*.md` is auto-globbed (`SpecParser.cs:45`), so
   `./bin/maxon spec-test --filter=services` must list every test. Live tests will FAIL (no
   implementation) — the check is that they are *discovered and run*, not skipped or unparsed.
2. **`disabled-test` markers are shelved, not silently dropped** — each must carry its gating
   reason on the following comment line, per the shv2 convention where `grep -A1 disabled-test:`
   *is* the roadmap.
3. **No error codes registered.** `./bin/maxon error-codes check` must still pass — proof the design
   phase did not claim a number without a live emitting site.
4. **Maxon in the specs is syntax-checked**, not just eyeballed: `./bin/maxon fmt` over extracted
   snippets. ⚠ None of the Maxon in the design outputs has been compiled — treat it as
   shape-accurate, not build-verified.
5. **Baseline unchanged**: full `./bin/maxon spec-test` still green apart from the new failing
   `services` tests.

## Known weaknesses — recorded deliberately

1. **Send-uniqueness does not survive a function boundary, and that is the most common shape.**
   `function forward(svc, buf) ... svc.keep(buf)` is refused: `buf` arrived as a parameter and the
   bootstrap cannot prove the caller doesn't still hold it. The fix is a call-graph param-consume
   fixpoint — shv2 landed the one-level direct-sink version at P1.4a Wave 2 and **explicitly
   rejected the transitive half (E2015)**. So the guarantee is airtight *within* a function and
   gets conservative outward. **Biggest risk to the feature being pleasant; build this first.**
   ⭐ **This weakness is bootstrap-specific.** In shv2 the move rule *is* the language's ownership
   model (P1.2/P1.4a, already shipped), so most of this evaporates — see the shv2 section.
2. **`ServiceError` on every value-returning call is a real tax.** Shutdown is observable and
   unprovable-absent, so every RPC needs `try ... otherwise`. Most sites become
   `otherwise panic(...)`, which trains people to stop reading error handling. The alternative —
   panic on a stopped service — trades a compile-time obligation for a runtime abort, which is worse.
3. **One GT allocation per RPC, unmeasured.** Reusing the GT struct as a reply cell keeps `Promise`,
   `await`, and `gtIsComplete` unchanged — worth a lot, since the ABI is the expensive thing to
   change. But a chatty service pays a GT-sized allocation per message. Revisit only on a benchmark.
4. **The `.unionCases` wire-tag story is aspirational.** The tags are free; the *transport* is not.
   `SpecWorkerPool`'s `JOB:` string protocol does **not** get deleted by this design — only its
   `select` does.
5. **Deadlock freedom costs same-type peer RPC.** Mutual reentrancy is *solved* rather than
   documented (acyclic blocking graph, E3110) — but the analysis is type-level, so two instances of
   one service may not await each other even though distinct instances would not deadlock.
   Fire-and-forget peer messaging is unaffected. Expect this to be the most-hit diagnostic; the
   message must teach the workaround, not just report the cycle.
6. **Whether a type is a service is a whole-program property.** A `spawn` in one file subjects a
   type's export methods to service rules everywhere. Mitigated by requiring the diagnostic to name
   the spawn site, but it remains real action-at-a-distance and is the cost of overloading `export`.

### Adjacent bug found — worth fixing regardless

**`specs/async-await.md` is materially wrong about the threading model.** Line 12: *"All green
threads run on a single OS thread"*; line 32: *"No atomics needed — reference counting stays
non-atomic."* Both false — `__sched_max_procs` is seeded from CPU count, `EmitGtStealWork` is
emitted, and `RuntimeEmitter.MemoryManager.cs:409,515` emit `AtomicInc`/`AtomicDec` unconditionally.
Anyone reasoning about concurrency from that spec reasons from a runtime that no longer exists.
