# Background Services for Maxon — design + spec tests

## Context

> ⚖ **DATED NOTE, 2026-08-27 (EC10).** `async` is settled and it is **not** a threading primitive:
> an `async` call starts a COROUTINE of the green thread that made it, published only to that green
> thread's own queue and driven only by its owner. It overlaps *waiting*, never execution, and a box
> is therefore reachable from exactly one green thread — which is what lets reference counting be
> plain rather than atomic (`MmRuntime.emitAdjustRefcount`, `MmRuntime.maxon:1778`; `SchedRuntime.maxon:9-18`
> states the invariant). ⇒ **`spawn`, reserved below, is where threads first meet**, and it inherits two
> things: the whole GMP substrate `async` no longer reaches (per-P rings, stealing, the worker loop — built,
> correct, unreached), and the obligation to say what crossing a thread does to ownership. The
> "Send is a MOVE" rule this document already reserves is that answer, and the plain refcount is what
> makes it load-bearing rather than a style choice: a `spawn` that let two green threads hold one box
> would not be slow, it would be wrong.

> 📋 **RE-VERIFIED AGAINST THE TREE 2026-08-27 (`1152bbc566`).** This document was written 2026-07-22
> (`134600b695`), when shv2 stood at P1.4b with no `async`, no generics and no `Array`. Five weeks later
> **Phase 1 is closed (2026-08-22), the Phase 2 self-host gate is closed (2026-08-26), and every shv2
> prerequisite this document listed as pending has landed.** Every file:line below was re-checked on that
> commit and corrected where it had drifted; the claims that changed *meaning* rather than line number are
> marked **⚠ CHANGED** in place. The four that matter most:
>
> 1. **The target decision's premise is gone** — see §"Target". The bootstrap-first choice was made when
>    shv2 could not host this; shv2 now hosts it *better*, and EC10 made the ownership rule load-bearing
>    there. **But shv2's own source is compiled by the bootstrap at stage-1**, so the dogfood that motivated
>    the feature (`SpecWorkerPool`) cannot consume a shv2-only construct. That is a re-ruling for the user.
> 2. **Every error code this document proposed by number is TAKEN** (E3104–E3110 now belong to unrelated
>    diagnostics; E3133 is the high-water mark). Codes are named here and numbered at registration.
> 3. **The rung ids `P1.5c`/`P1.7c` name a ladder that no longer exists.** Work is now claimed as board
>    rows with lanes (`maxon-shv2/PLAN.md` §"THE SLICE BOARD"); §"Proposed rows" restates the plan in that form.
> 4. **Three FREE board rows gate this feature and did not exist when it was written**: `W217` (an
>    `Array with Promise` never drops its elements — blocks `awaitAny` over an array), `W218` (the multi-M
>    park race — unreachable for coroutines, REAL for green threads, so `spawn` arms it), and `W221`.

Maxon has a faithful Go-style GMP green-thread runtime — per-P run queues, work stealing,
`runnext`, Go's 61-tick fairness interval and `_StackMin`/`_StackGuard`, growable 2KB stacks,
IOCP/kqueue I/O. In shv2 that hierarchy landed with `W212` (closed 2026-08-27, `SchedRuntime.maxon`)
and is **built, correct and unreached** since EC10 pinned `async` to a coroutine. **But it exposes zero
communication primitives to user code.** No channels, mailboxes, actors, mutexes, condvars, or queues.
The entire user-visible concurrency surface is `async` / `await` / `Promise` / `sleep` / `Runtime.yield()`.

The gap is visible in the tree. `maxon-shv2/Testing/SpecWorkerPool.maxon` (2,384 lines) passes
messages over **subprocess stdin/stdout with a string protocol** because nothing in-process can do
it, and hand-rolls a `select` by polling green-thread status (`:1650`, served by `findReadyDrain` at
`:2351` with a `sleep(PollYieldMs)` fallback at `:2043`):

```maxon
return __Builtins.gtIsComplete(drain.inner) != 0
```

`stdlib/Builtins.maxon:121-124` exports `Promise.inner` *specifically* to enable that poll. An exported
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
| Target | ⚖ **NEEDS RE-RULING — the premise changed.** Was (2026-07-22): *`maxon-sharp` bootstrap, specs in `specs/`; shv2 follows as P1.5c.* See §"Target" for what changed and the recommendation. |
| `select` | **In scope** — the motivating case. |
| Memory safety | **Send is a MOVE.** Arguments are owned by the service until returned. |
| Scope | **Design doc + spec tests only.** No compiler implementation. |

### Why services do not replace async/await

Recorded because it contradicts the initial hypothesis, and the evidence is one-sided:

1. **`async` is not function coloring.** It is a *call-site prefix* (`let p = async f()`); there is
   no `async function`. The "async infects every signature" problem that motivates replacing
   async elsewhere **does not exist here.**
2. **`Promise` is a first-class two-parameter type** (`type Promise uses Element, ErrorType` —
   `Promise with (T, E)`, `Compiler/PromiseType.maxon`), stored in struct fields and arrays, across
   47 tests in `specs/async-await.md` (50 in `specs-shv2/`, which adds `targets:` markers) plus
   async-filesystem, async-tcp, sleep, subprocess, http-client.
3. **`await svc.handler(x)` returns a promise.** Services *consume* the async machinery.
4. They solve different problems — `async f()` is a one-shot stateless future; a service is a
   long-lived stateful mailbox with identity.
5. **⚠ CHANGED (EC10):** `async` is now a *coroutine* of its caller's green thread — it cannot cross
   an OS thread at all. So services/`spawn` are not merely "a different problem": they are the **only**
   construct in the language that moves work to another green thread, and therefore the only place
   the ownership-across-threads question is ever asked.

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
   self-send, so it needs no diagnostic — this deletes the proposed self-send code outright.

**Only `export` *instance* methods become messages.** `static function` is excluded structurally —
it has no `self`, and `spawn Calc.create()` calls it directly rather than through the handle.

> ⚠ **The one cost of overloading `export`, stated plainly.** Whether a type is a service is now a
> **whole-program** property: `spawn Calc.create()` anywhere makes `Calc` a service and subjects
> *all* its export methods to the service rules (reply-escape, transferable-argument). So a
> diagnostic can fire on a method in one file because of a `spawn` in another. **The diagnostic must
> name the spawn site that made the type a service**, or it will read as a non-sequitur. This is the
> price of the design and it should be paid in the error message. *(shv2 already records every
> `async` site's quotable text and position as `Project.asyncSpawnSites` for exactly this purpose —
> E3073's message names the spawn — so the mechanism to name a `spawn` site exists.)*
>
> ⚠ **`export` carries a second meaning in shv2 that this design must not collide with.** An `export`
> is also the cross-module visibility keyword, gated by `UnusedExportCheck` (E3092/3/4) and — per
> `feedback_the_self_compile_is_the_only_e3092_gate` — gated ONLY by shv2 compiling itself. An
> export method that is reachable only as a *message* must count as USED by its `spawn`, or the
> unused-export check will refuse every service whose handle is the only caller.

### What the compiler synthesizes

Dispatch is a **union plus one exhaustive match** — not a handler table. That is forced: a message
handler must close over `self`, so it is a capturing closure, and **E3099** (`capturingClosureEscapes`,
claimed by both compilers) forbids storing a capturing closure in a field, global, container, or union
payload — every place a table could live. The forcing turns out to be fortunate:

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
exhaustive `Calc.__drop_request` — in shv2 that is the tag-conditional `__destruct_<U>` cascade P1.3
already emits for every union with owned payloads. A closure-based mailbox holds erased environments,
cannot know what to free, and would fail the leak gate.

**`spawn` returns `Calc.handle`** — a compiler-synthesized companion struct, nominally distinct per
service. Precedent: `Shape.unionCases` is a synthesized companion enum in the union's namespace.

> ⚠ **Rejected: the per-instance-typealias trick.** `specs/per-instance-typealias.md` documents, as
> a *feature*, that per-instance aliases are `as`-convertible between instantiations. For an index
> that's a mild footgun; for a mailbox pointer it is type confusion that hands a `Calc.request` to a
> `Logger` loop, which matches it against the wrong variants and reads a payload that isn't there.

> ⭐ **This design needs no generics** — the companion types (`Calc.request`, `Calc.handle`) are
> monomorphic by construction. **⚠ CHANGED — the argument that made this a *hard* advantage is
> bootstrap-specific.** The library alternative (`ServiceHandle uses S, Request, Reply`) needs
> `async` inside a generic body, and in the BOOTSTRAP that still crashes: `MaxonAsyncCallOp` /
> `MaxonAwaitOp` / `MaxonTryAwaitOp` have no arm in `FunctionCloner.cs` (its default at `:457`
> delegates to `SubstitutingOpCloner.Clone`, which throws `InvalidOperationException` "unhandled op
> type" at `SubstitutingOpCloner.cs:252`) — a crash, not a diagnostic. **shv2 does not monomorphize**
> (locked decision: dictionary-passing + layout descriptors), so no clone is ever made and that failure
> mode cannot transfer. Whether shv2 *accepts* `async` inside a generic body is **UNMEASURED** — no
> `specs-shv2` case combines the two (grep 2026-08-27). The monomorphic design remains the simpler one;
> it is no longer the only one that can exist.

### Ownership — the spine

> Every handler argument is **moved** into the service, owned exclusively for the handler's
> duration. A return is **moved back**. A moved-from binding is poisoned; assignment revives it.

```maxon
var buf = makeBuffer()
buf = try await store.process(buf) otherwise panic("store is live")   // out and back
```

Re-arming by assignment is not a new idiom — `specs/async-await.md` already documents exactly this
rule for promises, and in shv2 it is the language's move model: `let u = t` / `s = t` MOVE, the source
is poisoned, a read is **E3102** (`useAfterMove`, shv2-only — the bootstrap aliases), reassignment revives.

**⭐ This deletes `Sendable` rather than adding it.** `Send`/`Sync` answer *"can two threads
reference this at once?"* — a question about **sharing**. Under move-only transfer there is never a
second reference, so the question does not arise. Two further reasons it would be wrong here:
Maxon's marker interfaces are singleton compiler hooks (the compiler scans for *the one* conformer),
not a conformance-set mechanism; and `Array with T` would need conditional conformance, not emitted
until P2.2 (still ⬜). What remains is a smaller structural transfer-shape rule, checked rather than
declared — which is how Maxon does structural properties everywhere else.

> ⚠ **A THIRD REASON USED TO STAND HERE AND EC10 REVERSED IT, WHICH STRENGTHENS THE CONCLUSION RATHER
> THAN WEAKENING IT.** It read *"refcounts are already unconditionally atomic
> (`RuntimeEmitter.MemoryManager.cs:411,517`), so there is no non-atomic fast path to opt out of"* — true
> of the BOOTSTRAP, and **false of the self-hosted compiler since 2026-08-27**, where the refcount step is
> a plain load/add/store (`MmRuntime.emitAdjustRefcount`). So there IS a non-atomic fast path now, and it
> is the only path. ⇒ move-only transfer stops being a simplification and becomes **the thing that keeps
> the plain refcount correct**: a `spawn` that let two green threads hold one box would not be slow, it
> would corrupt the heap. The `Sendable` deletion still stands — under move-only transfer there is never
> a second reference, so the question it answers still does not arise — but this rule is now load-bearing
> and must be CHECKED, not assumed.

> ⛔ **CONSEQUENCE THE 07-22 TEXT DID NOT DRAW: a CO-OWNED value is not transferable, full stop.** shv2
> has two shapes whose refcount is legitimately above one on a single green thread — a value promoted to
> `shared` by P1.5's escape analysis (a closure or coroutine captured it), and a struct type-argument
> retained into a longer-lived generic field (`retain-escaping`, co-owned by ruling). Sending either hands
> a box to a second green thread while the first still holds it: exactly the case the plain refcount
> forbids, with the second holder on another M. So the transfer-shape rule is not only "no Promise, no
> interface value, no closure" — it is **"exactly one owner at the send, provably"**, derived from the
> ownership bits the parser already carries (`valueOwnsHeap`, the escape/retain marks), and its ARRIVALS
> must be enumerated rather than spelled (`feedback_enumerate_arrivals_not_spellings`): every route by which
> a value can acquire a second referent is a route the send-site check must see. `.clone()` is the
> user's answer, and here it is the right one.

**Move is also simpler than the existing async-argument protocol.** In the bootstrap, `LowerAsyncCall`
(`MLIR/Conversion/MaxonToStandardConversion.Async.cs:15`) increfs each managed arg at the spawn site and
the trampoline decrefs through a `managed_mask` *because `async f(x)` does not move* — the caller keeps
`x`, so two owners coexist. (shv2's equivalent is `AsyncReleaser` in `LowerMaxonToStd.maxon` /
`TypeRules.maxon`, which routes a Promise slot to `__gt_promise_drop`.) Services move, so there is one owner:

| | `async f(x)` today | `svc.handler(x)` |
|---|---|---|
| send site | incref, set mask bit | **nothing** — hand over the existing +1 |
| caller scope end | decref | **suppressed** — binding is moved-from |
| handler end | trampoline walks mask, decrefs | ordinary scope-end decref, unless returned |

The mask does not vanish entirely: it degenerates into the **drop map for abandonment paths** —
"this message will die without a handler running; which of its N untyped words are heap pointers?"
Five paths need that answer and it is not derivable at runtime. In shv2 the synthesized request union
*is* that map — the `__destruct_<U>` cascade knows every payload's type.

#### ⚠ The one real tension — and it is the BOOTSTRAP's

`docs/MEMORY_MANAGEMENT.md:293` states the bootstrap's model outright: *"Function parameters are not
owned by the callee. The caller retains ownership."* And the bootstrap aliases by default — `var b = a`
makes both point at one object. **A move contradicts the model the bootstrap is built on**, and the
guarantee is only real if the send site can prove uniqueness.

**⚠ CHANGED — in shv2 there is no tension, because the rule is already the language's.** P1.4a's
ruling (user, 2026-07-18): *params BORROW by default, CONSUME by use; returns ADOPT.* A handler
parameter is a consuming parameter; a send is a consuming call; the source is poisoned and E3102 fires
on reuse; a return adopts back into the caller. The send-uniqueness question becomes *"is this argument
an OWNED value with no co-owner?"* — answered from the ownership bit and the escape/retain marks, not
from a new alias analysis. What shv2 does NOT have is the *transitive* half of param-consume — Wave 2
landed the direct-sink analysis and E2015-deferred the fixpoint (`PLAN.md:2590`) — see Known weakness 1.

**If the bootstrap target is confirmed, the resolution is to extend `BorrowCheckPass.cs`** — 336
lines, and the right home by *data*, not analogy. It already builds, in one linear walk with a global
op index: `assignsByValueId` (`:41`, every assign, binary-searchable), `lastUse` per variable (`:45`, its
NLL machinery), an `activeBorrows` map (`:104`) with activation-at-assignment and expiry-after-last-use,
and a rule that reassigning a borrowed-from source kills the borrow. An alias is a `MaxonAssignOp` whose
RHS is a bare reference — a shape it already indexes. `MaxonCallOp.ArgVarNames` (`MaxonDialect.cs:657`)
already tells it which caller variable each argument came from. The rule must be **conservative-reject**:
an argument is sendable iff *provably unique* — a fresh rvalue, or a local whose initializer was a fresh
rvalue with no aliasing assignment, field store, or container push since. Everything else is refused,
naming the aliasing binding and its line, in E3070's existing diagnostic shape. *(shv2's
`BorrowCheck.maxon` is a different thing — E3070 alone, container-element borrow liveness — and is not
the home for this.)*

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

**A reply slot is a real GT that never runs**: `stackBase = 0`, created blocked, never enqueued —
a wait token plus a result slot. Both the syntax designer and the runtime designer converged on this
independently, which is worth weight.

`Promise.inner` is unchanged (it holds a GT pointer today), `await` and `try await` are unchanged,
`gtIsComplete` works on it unmodified, and **E3100 linear-await composes for free** because the check
keys on the promise's identity, not on what produced it (in shv2 that identity IS the `asyncCall`
ValueId — `let q = p` shares it, a re-arm mints a fresh one; `PLAN.md:1273`). Cost is a GT struct, not a
2KB stack. Critically, **the `stackBase == 0` GT already exists in both runtimes**: the bootstrap's
`__gt_await` skips the stack free when `stack_base == 0` (`MLIR/X86CodeEmitter.Runtime.cs:4382`, the
`mainThread` branch), and in shv2 GT0 — each P's inline scheduler context — is exactly that GT
(`GtRuntime.maxon:87,150,1809`: *"`stackBase == 0` ⇒ run the kernel call directly, no switch"*).

Three corrections this forces, all important:
- **The cell allocator must not be `__gt_spawn`.** In the bootstrap, spawn adds to `__gt_all_head`
  and increments `__gt_live_count` (`X86CodeEmitter.Runtime.cs:3867-3874`), which only the trampoline
  undoes; a cell runs no trampoline, so it would never be removed and `__gt_cleanup` would spin
  forever. In shv2, `__gt_spawn` stamps `owner`, publishes to the owner's coroutine queue and takes
  `__sched_lock` to do it (`W221`) — a cell must be minted from the slab **unpublished**, and it must be
  reclaimable through `__gt_promise_drop` / the teardown ticket like any other promise (`W212`).
- **⚠ CHANGED — the reply completion is the FIRST cross-green-thread wake in the language.** The
  07-22 text framed this as *"`__gt_await` must clear `ioYielded` before publishing `promise.waiter`"*
  — a two-store change on the path every bootstrap `await` takes. In shv2 the shape is different and
  the hazard is already filed: the completer is the SERVICE's green thread, possibly on another M,
  publishing to the awaiter's **owner** queue. The door for a cross-M publish exists — the IOCP
  completion thread calls `__gt_coro_enqueue` under `__sched_lock` (`SchedRuntime.maxon:9-18` names it
  as one of the two remaining cross-thread writers). The hazard is **`W218`**: *"the sleep and proc parks
  register a green thread BEFORE `__gt_context_switch` has saved its registers — at N>1 another M can
  fire it and run it mid-save: two Ms on one stack."* Unreachable for coroutines, REAL for green
  threads. **The reply park must use the deferred-registration shape W218 prescribes** (register after
  the switch, from the driver side — `__gt_resched`'s `pendingYielder` road), or `spawn` ships the race.
  This remains the highest-risk edit in the design; it has moved files, not gone away.
- **Every new `.data` word must state its `MultiMSharing` class** — `pushRuntimeGlobal` panics
  without one (`SchedRuntime.maxon:176`). A mailbox head, a waiter slot and a claim word are exactly
  the words that class system exists for.

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

> ⛔ **HARD PREREQUISITE, filed after this document: `W217`.** *"`Array with Promise` NEVER DROPS ITS
> ELEMENTS — an array of un-awaited promises going out of scope emits no `__gt_promise_drop` per
> element, `__gt_live_count` stays up, and the process ABORTS 75."* Pre-existing, three reproducer
> shapes written into the row. `awaitAny` returns an index and leaves the OTHER promises un-awaited in
> the array — which is the exact shape that aborts today. So `awaitAny`, "handles in an array", and the
> worker-pool dogfood are all gated on `W217`, and its row says whoever fixes it must first read
> `emitGtFifoSweepDropped`'s header, because fixing the leak ARMS a latent lock-recursion hang.

**Implementation is a park, not a poll.** The selecting GT registers on all K reply cells at once.
The single `waiter` slot forces a heap `MboxWaiter` record per mailbox (Go's `sudog`, same forcing
argument), with an atomic claim CAS so exactly one sender wins. **Do not take K locks** — Go orders
channel locks by address, which is wrong here because the platform locks are **recursive on the
wrong identity**: a Win32 `CRITICAL_SECTION` (the bootstrap's `__sched_global_lock`, and shv2's
`__sched_lock`) is recursive per OS thread, and the bootstrap's arm64 spinlock is recursive on its
`owner` word (`ARM64CodeEmitter.Runtime.cs:2663`), while green threads multiplex over both. A GT
parking while holding a mailbox lock lets another GT on the same M take the *recursive* path straight
into the critical section — not a deadlock, but silent FIFO corruption. **This is not hypothetical in
shv2: `W217`'s review found a latent hang of exactly this class** — `emitGtFifoSweepDropped` holds
`__sched_lock` across a path that can spin-wait *"with the lock RELEASED"*, and a recursive release of
a `CRITICAL_SECTION` releases nothing. **Hard rule: no mailbox lock is ever held across
`__gt_context_switch`.** Register one lock at a time; no cycle can form.

The success criterion is checkable: `findReadyDrain` and the `sleep(PollYieldMs)` fallback both
delete, and `Promise.inner`'s `export` can be removed — **subject to the stage-1 constraint in
§"Target"**, since `SpecWorkerPool.maxon` is shv2's own source and the bootstrap compiles it.

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
exactly the shape of the existing yield analysis: the bootstrap's `CheckAsyncYielding`
(`SemanticCheckPass.cs:263`) over `MLIR/Core/IrCallGraph.cs`, and shv2's `checkAsyncYielding` /
`buildYieldingSummary` (`SemanticCheck.maxon:3385,3534`) over `CallGraphEdges`. Detection is Tarjan
SCC over the blocking edges: any SCC with more than one member, or any self-loop, is the error.

Edges are **by type, not by instance**, which is what makes them statically knowable — a handle in a
field, an array, or a `Map` still has a type. The transferable-argument rule already forbids sending
a `Promise` into a service, so the only way a message can block on another service is to call and
await it in its own body or a callee's. There is no back door.

```
error E<n> (SemanticServiceCallCycle): service call cycle — these messages can deadlock waiting on each other:
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

**E3073 (`AsyncNonYielding`, both compilers):** the mailbox receive and the select wait are PARK
POINTS and must be registered as such, or `spawn` fails its own yield analysis for every service.
In shv2 the roster is the one documented at `SemanticCheck.maxon:3624` — *"THE PARK POINTS — THE
ONLY SEEDS OF THE YIELD CLOSURE, AND EACH ONE REALLY SUSPENDS"* (`__gt_sleep`, `__gt_resched`,
`__gt_process_run`, the `__gt_io_park` filesystem bands); in the bootstrap it is `SemanticCheckPass.cs`'s
`IoStubs` (`:174` — whose own header now warns *"IT IS NOT AN I/O ROSTER"*, it is the same set of
park points). ⚠ CHANGED: the 07-22 text also named v1's `ioYieldBuiltinSet()`; v1 is deprecated and
no longer builds, so there is no third roster to keep in step. Do **not** register `send` or
`try_recv` — they complete inline and never suspend the caller; listing them would let `async` wrap a
genuinely non-yielding function.

### Shutdown

`shutdown()` enqueues a poison pill **behind** everything queued — a graceful drain, not a kill.
Dropping the last handle does the same, so ordinary programs need no shutdown boilerplate. Pending
replies of un-processed messages resolve with `ServiceError.stopped` rather than hanging their
awaiters — a *liveness* obligation needing its own spec test.

⚠ **Process-exit gap, bootstrap form:** `__gt_cleanup` (`X86CodeEmitter.Runtime.cs:4965`) first walks
`__gt_all_head` calling `__gt_cancel` on every live GT — but cancel only sets a flag and `CancelIoEx`s
a pending IO handle. A service parked in `recv` has no IO handle and never runs to observe the flag, so
it is `Waiting`, in no run queue, reachable only from the mailbox, and step 3's drain (which frees GTs
reaching `Completed`) never sees it. Without a global mailbox registry (`__mbox_all_head`, mirroring
`__gt_all_head`) walked at cleanup, **process exit hangs whenever a service is idle**, which is the
steady state. **shv2 form:** reclamation already goes through `__gt_promise_drop` and the teardown
ticket *"whether the thread is queued, running, parked or completed"* (`W212`), and dropping the last
handle enqueues `__shutdown` — so the exit path is handle-drop → poison → the service completes →
reclaimed. The residual is a handle whose owner is a global; the spec that pins it is "an idle service
whose handle lives in a global still lets the process exit 0".

---

## Deliverables

1. **`specs-shv2/services.md`** — the spec, ~24 tests. ⚠ CHANGED from `specs/services.md`: the
   bootstrap suite auto-globs `specs/*.md` (`SpecParser.cs:74`), so a `specs/` file would run — and fail
   — under the bootstrap. shv2-only feature specs already live only in `specs-shv2/` (`sched-runqueue.md`,
   `sched-processor.md`, `builtins-cpu-parallel.md` have no `specs/` twin); this follows that precedent.
   Every case carries the async family's `<!-- targets: -->` restriction (the wasm lane runs no green
   threads). Frontmatter `feature: services`, `status: experimental`, `category: concurrency`. Live
   tests: spawn/send/await, FIFO + serialization, independent instances, **the same type used directly
   *and* spawned** (the location-transparency property, and the test that would have been impossible
   under a `service` declaration), **private methods are absent from the handle**, message throws,
   named error union, call-after-shutdown, shutdown drains, shutdown resolves pending replies,
   `.unionCases` tags, handles in an array (gated `W217`), move-in-and-back, `awaitAny` (gated `W217`),
   an idle service with a global handle exits 0, and diagnostics (use-after-send, non-unique send,
   **co-owned/`shared` send**, reply-aliases-state, sending a `Promise`, handle mismatch, double-await
   of a reply, an export reachable only by message is not "unused"). **Deadlock-freedom tests carry
   their own group**, since the rule is the subtlest part of the design and each case must be pinned
   separately:
   - `services.cycle-two-services-refused` — A awaits B awaits A → `SemanticServiceCallCycle`,
     asserting the full cycle path appears in the diagnostic
   - `services.cycle-through-a-free-function-refused` — the edge is transitive through an ordinary
     function, proving the fixed-point runs
   - `services.cycle-same-type-self-edge-refused` — a `Worker` awaiting a `Worker` handle
   - `services.fire-and-forget-cycle-is-legal` ⭐ — A sends to B, B sends back to A, both
     non-blocking, program runs to completion. **The test that pins "only blocking edges count"**;
     without it a later tightening would silently ban correct programs
   - `services.deep-acyclic-chain-runs` — A→B→C→D awaited end to end, no diagnostic

   `disabled-test` with the gating reason on the next line: per-message metadata, generic services,
   send-through-a-parameter (interprocedural fixpoint — the E2015-deferred half of param-consume).

   **Plus the four `sched-runqueue` cases EC10 deleted**, which must be re-authored against `spawn`
   because their premise was that a spawned frame enters the P ring: `ring-overflow-runs-every-spawned-thread`,
   `the-ring-index-wraps-past-its-capacity`, `the-global-queue-is-consulted-within-sixty-one-schedules`,
   `nothing-is-stolen-on-one-processor` (EC10's row, "D3"). And **`track0/pin-matrix.sh` goes RED the day
   `spawn` lands** — it pins `workers == 1`, `steals == 0` at every `MAXON_MAX_PROCS` as the current truth
   and says so; the `spawn` rung rewrites it, it does not delete it.
2. **`docs/LANGUAGE_REFERENCE.md` §14** — "Async/Await (Concurrency)" at line 4251 (TOC item 14 at
   `:37`, which has no sub-bullets today). It already carries the one-sentence reservation at `:4257`
   (*"Creating a green thread that is scheduled independently is `spawn`, which is reserved and not
   built"*); add a Services subsection and the TOC sub-bullets.
3. **`docs/BNF_SYNTAX.md`** — just `spawn_expr`, beside `async_expr` (§6.6, `:859-862`). No new
   declaration production: `type_decl` is unchanged, and "which members are messages" is a *semantic*
   rule over `visibility_prefix`, not a grammar change. *(Adjacent nit: `async_expr`'s comment still
   reads "spawn green thread" — stale since EC10; so do the `async` keyword help texts in both lexers,
   `1-Lexer.cs:251` and `Lexer.maxon:1687`. Fix them in the same commit that adds `spawn`, since that is
   when the two words come to mean different things.)*
4. **Proposed error codes, NAMED here and NUMBERED at registration.** ⚠ CHANGED: the 07-22 table
   assigned E3104–E3110, and every one of those numbers has since been taken by an unrelated
   diagnostic (E3104 `SemanticTargetUnsupportedConstruct` … E3110 `SemanticBufferByteAccessManagedElement`);
   E3133 is the high-water mark and **E3134 was next free on 2026-08-27** — but do not take it from this
   sentence. `error-codes check` **fails the build** on a claim with no live emitting site, so the
   registry edit lands with the first emitting commit, taking the next free number *from the registry
   at that moment* (grepping a copy is how E3099 was claimed twice).

| canonical name | fires when |
|---|---|
| `SemanticServiceArgumentMoved` | reading a binding after it was sent *(shv2: this is E3102 `useAfterMove` already — may need no new code, only the send site marking the move)* |
| `SemanticServiceArgumentNotUnique` | sending a value with a live alias (bootstrap) / a co-owner — `shared`, retained — (shv2) |
| `SemanticServiceReplyAliasesState` | an export method returns something reachable from `self` |
| `SemanticServiceValueNotTransferable` | sending a `Promise`, interface value, closure, or a co-owned box |
| `SemanticServiceHandleMismatch` | `Logger.handle` where `Calc.handle` is expected |
| `SemanticServiceBareTryUnnamedError` | bare `try` on a handle call without the named union |
| `SemanticServiceCallCycle` | a cycle in the blocking call graph — the deadlock check |

E3100, E3102, E3070, E2049, E2051 and discarded-results are **reused as-is** (all claimed by shv2; E3102
by shv2 only). **`ReplyAliasesState`, `ValueNotTransferable` and `CallCycle` must name the `spawn` site**
that made the type a service (see the whole-program caveat above) — the method they fire on may be in a
different file from the cause. **`CallCycle` must print the full cycle path** with a file:line per hop;
a cycle reported as a single site is unactionable.

> A *self-send* code is deliberately absent: private helpers are not on the handle, so a direct
> self-send cannot be spelled. `CallCycle` covers the case that survives — a cycle through other
> services, or a same-type self-edge between two instances.

### Files the eventual implementation touches (not this phase)

**shv2** (the recommended target):

- `Compiler/Lexer.maxon` — **one** keyword-table row beside `async` (`:1687`): `spawn`
- `Compiler/Parser.maxon` — the `async` prefix arm at `:45169` gains a `spawn` sibling; the synthesized
  `<T>.request` union / `<T>.handle` companion / `<T>.__loop`; the send site as a consuming call.
  **No pre-scan change** — a service is an ordinary `type`, and the parser stays a pure per-file
  function of its own tokens (locked decision). ⚠ Whether a type is spawned anywhere IS a whole-program
  fact; it must arrive through the one `queryProgramSignatures` sweep as a new arm, never a second sweep.
- `Compiler/IR/Maxon/MaxonDialect.maxon` — new ops **appended at the END of the union** per the
  band-append invariant (`:1288`), never mid-union
- `Compiler/IR/Maxon/LowerMaxonToStd.maxon`, `TypeRules.maxon` — the send/reply lowering beside `AsyncReleaser`
- `Compiler/SemanticCheck.maxon` — park-point roster (`:3624`); the cycle check as one function on
  `checkAsyncYielding`'s shape over `CallGraphEdges`; reply-escape + transferability
- `Compiler/UnusedExportCheck.maxon` — an export reached only by message counts as used
- `Compiler/Runtime/GtRuntime.maxon`, `SchedRuntime.maxon` — reply cell (unpublished, `stackBase == 0`),
  mailbox park/wake through `__gt_coro_enqueue`'s door with `W218`'s deferred registration, every new
  `.data` word with its `MultiMSharing` class; `MmRuntime.maxon` is **untouched** — refcounts stay plain
- `stdlib/Builtins.maxon` — `ServiceError`; later, un-export `Promise.inner`
- `track0/pin-matrix.sh`, `specs-shv2/sched-runqueue.md` — rewritten, see Deliverable 1

**bootstrap** (only if the re-ruling keeps it as a target): `1-Lexer.cs` (`KeywordMap` row at `:251`),
`2-Parser.cs` (`ParsePrimary` `:19093`, beside the `Async` arm at `:19103`), `MLIR/Dialects/MaxonDialect.cs`,
a new `MLIR/Conversion/MaxonToStandardConversion.Services.cs` beside `.Async.cs`, **both**
`StandardToX86Conversion.cs` and `StandardToARM64Conversion.cs` (CLAUDE.md requires parity),
`MLIR/Passes/BorrowCheckPass.cs`, `MLIR/Passes/SemanticCheckPass.cs` (`Run()` `:7`, `IoStubs` `:174`,
`CheckAsyncYielding` `:263`), `MLIR/Runtime/GtLayout.cs` + `RuntimeEmitter.Scheduler.cs`.

> ⚠ **Do not follow the `implement-feature` skill's Step 11 literally** — it is bootstrap-only, and
> the six directories it names (`Compiler/Lexer/`, `Parser/`, `AST/`, `IR/Conversion/`, `IR/Emit/`,
> `Semantic/`) **do not exist — none of them** (07-22 said "5 of 8"; re-checked 2026-08-27, it is all
> six distinct paths). Its `AstToMaxonDialect` stage is not real: **there is no AST.** `2-Parser.cs`
> builds Maxon-dialect IR directly as it parses; the real homes are `MLIR/Dialects`, `MLIR/Conversion`,
> `MLIR/Passes`.

## ⚖ Target — the decision whose premise changed

The 07-22 ruling was *bootstrap first, specs in `specs/`, shv2 follows at P1.5c*. It was the only
possible ruling that day: shv2 had no `async`, no errors, no generics, no `Array`. Every one of those
has since landed (table below), Phase 1 closed on 2026-08-22, the self-host fixpoint closed on
2026-08-26, and EC10 made this design's ownership rule the thing that keeps shv2's refcount correct.
Meanwhile the bootstrap's runtime is **unchanged** — it still multiplexes `async` onto worker Ms with
atomic refcounts, so in the bootstrap move-on-send is a simplification, not a correctness requirement.

**Recommendation: shv2 is the target, `specs-shv2/services.md` is the spec, and the bootstrap gets
nothing unless the user rules otherwise.** Three reasons, and one hard constraint against:

1. **The design's ownership rule IS shv2's language** (P1.2 moves, P1.4a consuming params, P1.3 payload
   cascades). In the bootstrap it needs a new uniqueness analysis bolted onto `BorrowCheckPass` over an
   aliasing-by-default model — the doc's own "one real tension" — and that tension does not exist in shv2.
2. **The runtime tier is already built and waiting for exactly this producer.** `W212`'s ring, stealing
   and worker loop are "correct and unreached"; `GtRuntime.maxon:8`, `SchedRuntime.maxon:9-18`,
   `ARCHITECTURE.md:2937`, `LANGUAGE_REFERENCE.md:4257`, `CONTEXT-PARAMETERS-PLAN.md:184` and
   `track0/` all name `spawn` as the reserved producer, by reference to this document.
3. **P2.6 now depends on it.** `ARCHITECTURE.md:2934-2940`: *"Fanning a `perFunction` pass across cores
   is `spawn`'s job … A fan-out written with `async` would compile, run every pass in sequence on one
   M, and read as a completed parallel driver."* The 07-22 text had P2.6 as a co-consumer; it is a
   dependant.

⛔ **THE HARD CONSTRAINT: shv2's own source is compiled by the bootstrap at stage-1** (`scripts/self-host-ab.sh:11`:
*"stage-1 = the tree's `maxon-shv2` (bootstrap-built)"*), there is no bootstrap-retirement plan in
`PLAN.md` or `ARCHITECTURE.md`, and `ConditionalCompilation.maxon` gates on targets, not compilers.
⇒ **Nothing under `maxon-shv2/` may use `spawn` or a service until the bootstrap parses them too.**
That includes the motivating dogfood (`SpecWorkerPool.maxon`'s `findReadyDrain`) *and* P2.6's fan-out.
Spec tests in `specs-shv2/` are unaffected — shv2 alone compiles them. So the choice is:

| option | what it buys | what it costs |
|---|---|---|
| **A. shv2 only** *(recommended)* | the feature, spec-gated, where the ownership rule already holds | the harness dogfood and P2.6's fan-out wait for a bootstrap-parity or bootstrap-retirement decision that does not exist yet |
| B. both, shv2 first | A, then the dogfood | a second implementation over an aliasing model with atomic refcounts — the design's weakest form, built twice |
| C. bootstrap only *(the 07-22 ruling)* | the dogfood, eventually | builds the design where its spine is a bolt-on, in the compiler whose job is stage-1 |

Two half-measures are **rejected** on the project's own rule: two arms of the pool under `#if` would be
one fact (the select) written twice; and deleting `findReadyDrain` to dodge the constraint is a scope
cut (`PLAN.md` §"PRINCIPLE — when may we rewrite shv2's own source?").

### ⭐ shv2 is a better host for this design than the bootstrap

The design's single largest weakness — move-on-send enforced over a **refcounted, aliasing-by-default**
language, needing a new uniqueness analysis — **does not exist in shv2**, because shv2's ownership
thesis already *is* this design's ownership rule. Re-verified against the ladder, 2026-08-27:

| Services need | shv2 status |
|---|---|
| Static single-owner moves, use-after-move | ✅ **P1.2 CLOSED** — `let u = t` / `s = t` MOVE, source poisoned, read is **E3102**, reassign REVIVES, conditional poison path-sensitive. **This is move-on-send, already shipped.** |
| Params that consume | ✅ **P1.4a CLOSED** (ruling 2026-07-18: params BORROW by default, CONSUME by use, returns ADOPT). Direct-sink param-consume shipped; the transitive fixpoint E2015-deferred. |
| Managed payloads in unions | ✅ **P1.3 CLOSED** — move-in (E3102 on source reuse), move-out, tag-conditional `__destruct_<U>` cascade. **That cascade IS the synthesized mailbox drop.** |
| Path-sensitive moves | ✅ **P1.4a Wave 2** — drops reconciled at every join, drop on the LIVE edges, no runtime flags. |
| Structs + instance methods | ✅ **P1.1a CLOSED.** |
| Errors (`throws`/`try`/`otherwise`) | ✅ **P1.4b CLOSED 2026-07-20** (was "the current rung"). |
| `async` / `await` / Promise / GT scheduler | ✅ **P1.5 CLOSED 2026-07-26; R3 substrate complete; W212 GMP hierarchy CLOSED 2026-08-27; EC10 pin CLOSED 2026-08-27** (was "Not started. The one hard dependency"). |
| Generics, `Array`, interfaces/witness tables | ✅ **P1.6, P1.7, P1.7a all CLOSED** — not on the critical path anyway (companion types are monomorphic), but `awaitAny(promises)` takes an `Array with Promise`, which now exists — and never drops its elements (`W217`). |
| 🚩 Phase 1 gate / Phase 2 self-host gate | ✅ **CLOSED 2026-08-22 / 2026-08-26.** |

### The escape-channel question — and why services do *not* have to co-land with P1.5

P1.5's thesis was that closures, `async`, and escape are **one mechanism** and must co-land:
*"a closure captures into an env block; a green thread captures into a task frame… land escape
single-threaded and add `async` later and you bolt a **second capture channel** onto it."*

That argument appears to apply here with equal force — a mailbox looks like a **third** capture
channel. **It does not apply, and the reason is the ownership rule.** Escape → `shared` exists because
a captured value has **two** referents, so it needs a refcount. **A moved value has exactly one owner
at every instant** (§ Ownership) — it is not captured, it is *transferred*. So a mailbox is not a
capture channel at all; it is a transfer channel, built from P1.2/P1.3/P1.4a machinery, not from P1.5's
`EscapeAnalysis`. P1.5 has since closed with `async` as a coroutine that never crosses a thread, which
makes the point sharper: **the mailbox is the first and only channel through which a value crosses a
green thread**, and it does so by transfer, which is why a `shared` value may not enter it.

Two consequences worth stating:
- **Services are a clean successor, not a co-lander.** They consume `async`/Promise/the GT scheduler
  and add nothing to escape analysis.
- **Services should *lower* P1.5's tracked metric** (*"% values promoted to `shared`"*). Sending a
  value instead of capturing it moves work out of the refcounted channel into the owned one.

### Proposed rows — in the board's form, not the ladder's

⚠ CHANGED: `P1.5c` / `P1.7c` named positions on a ladder that closed. Work is claimed on `PLAN.md`'s
🧭 SLICE BOARD as rows with lanes; these are the rows, in dependency order. **`DefaultMaxProcs` is 1 and
`MAXON_MAX_PROCS>1` is opt-in** (G1 made it so "precisely so this class could be found and fixed one
arm at a time"), so the first landing of `spawn` is **single-M by default and multi-M under the
`track0` matrix** — the ring, the steal and the worker loop get their first producer without the
default build changing.

| row | scope | lane(s) | gated on |
|---|---|---|---|
| **`SV1` — `spawn`** | the keyword; a GT that IS a green thread (`owner = self`, `GtRuntime.maxon:6-8`), published to a P ring; `track0/pin-matrix.sh` rewritten (its `workers==1`/`steals==0` pins go red by design); the four deleted `sched-runqueue` cases re-authored; **`W218` closed in the same rung** — the sleep/proc park race is unreachable for coroutines and real for green threads, so the rung that creates green threads inherits it; a `spawn` copies the caller's context parameters (`CONTEXT-PARAMETERS-PLAN.md` ruling 7) | L-gt-runtime (`GtRuntime.maxon`, `SchedRuntime.maxon`) · L-parser-decl | nothing — the substrate is built |
| **`SV2` — services core** | the synthesized request union + `.handle` companion + dispatch loop, the mailbox, move-on-send with the co-owner refusal, reply cells with `W218`'s deferred registration, `ServiceError`, graceful shutdown, `UnusedExportCheck` arm, and the **acyclic-blocking-graph check** (pure front-end analysis over `CallGraphEdges` — could land as its own row ahead of the runtime) | L-parser-decl · L-ownership · L-gt-runtime · L-types | `SV1` |
| **`SV3` — `awaitAny`** | the stdlib primitive over `Array with Promise`; `MboxWaiter` registration on K cells; the un-export of `Promise.inner` | L-stdlib · L-gt-runtime | `SV2`, **`W217`** |
| *(dogfood)* | `SpecWorkerPool.findReadyDrain` + `sleep(PollYieldMs)` deleted; P2.6's fan-out | — | **the bootstrap-parity / retirement re-ruling** — see the hard constraint |

`W221` (`__sched_lock` on every spawn and drive iteration) is adjacent, not a gate: its row says
measure first, and `spawn` makes the spawn-heavy A/B it asks for more representative.

> ⚠ **Sequencing note that outlived its rung.** The 07-22 text warned that if P1.5 shipped without a
> plan for `awaitAny`, shv2's pool would re-import the bootstrap's `inner`-polling wart. **It did** —
> `SpecWorkerPool.maxon:1650` polls `drain.inner` today, because P1.5's acceptance test was the parallel
> pool and the pool needs a select. That is now the stage-1 constraint's problem rather than P1.5's,
> and the fix is unchanged: keep `Promise.inner` exported only until `SV3` and the re-ruling let the
> pool stop polling.

### Specs in shv2

Per Workstream S, spec files are ported from `/specs` on demand, by the rung that needs them — never
as a bulk dump — and a NEW shv2-only feature's spec is authored directly in `specs-shv2/` (precedent
above). So `specs-shv2/services.md` is created by `SV2`, carrying only the cases that row enables, with
everything else `<!-- disabled-test: -->` plus the gating row on the next line (`<!-- SV3 awaitAny -->`,
`<!-- W217 -->`). **The ratchet applies: an enabled case may never be re-disabled.**

## Verification

Design-phase only — no compiler changes, so the gate is review plus spec well-formedness:

1. **Specs parse and are discovered.** `./maxon-shv2/.maxon/maxon-shv2.exe spec-test --filter=services`
   (run FROM the checkout — the runner reads `specs-shv2/` relative to cwd) must list every test. Live
   tests will FAIL (no implementation) — the check is that they are *discovered and run*, not skipped
   or unparsed. Redirect the run to a file; never pipe it.
2. **`disabled-test` markers are shelved, not silently dropped** — each must carry its gating
   reason on the following comment line, per the shv2 convention where `grep -A1 disabled-test:`
   *is* the roadmap.
3. **No error codes registered.** `./bin/maxon.exe error-codes check` must still pass — proof the design
   phase did not claim a number without a live emitting site.
4. **Maxon in the specs is syntax-checked**, not just eyeballed: `./bin/maxon.exe fmt <file>` over
   extracted snippets (always with a path — bare `fmt` formats the whole directory). ⚠ None of the Maxon
   in this document has been compiled — treat it as shape-accurate, not build-verified.
5. **Baseline unchanged**: the full shv2 suite still green apart from the new failing `services` tests,
   and the bootstrap suite untouched (no `specs/` file was added).

## Known weaknesses — recorded deliberately

1. **Send-uniqueness does not survive a function boundary, and that is the most common shape.**
   `function forward(svc, buf) ... svc.keep(buf)` is refused: `buf` arrived as a parameter and the
   compiler cannot prove the caller doesn't still hold it. shv2 landed the one-level direct-sink
   param-consume analysis at P1.4a Wave 2 and **explicitly deferred the transitive fixpoint (E2015)**
   (`PLAN.md:2590`), so the guarantee is airtight *within* a function and gets conservative outward.
   **Biggest risk to the feature being pleasant; build this first.** In the bootstrap the weakness is
   larger still (no move model at all); in shv2 it is exactly the deferred half of an analysis that exists.
2. **`ServiceError` on every value-returning call is a real tax.** Shutdown is observable and
   unprovable-absent, so every RPC needs `try ... otherwise`. Most sites become
   `otherwise panic(...)`, which trains people to stop reading error handling. The alternative —
   panic on a stopped service — trades a compile-time obligation for a runtime abort, which is worse.
3. **One GT allocation per RPC, unmeasured.** Reusing the GT struct as a reply cell keeps `Promise`,
   `await`, and `gtIsComplete` unchanged — worth a lot, since the ABI is the expensive thing to
   change. But a chatty service pays a GT-sized allocation per message (`GtStructSize` is 0x118 since
   EC10). Revisit only on a benchmark — `scale-test` cannot see it until the corpus expresses a service.
4. **The `.unionCases` wire-tag story is aspirational.** The tags are free; the *transport* is not.
   `SpecWorkerPool`'s `JOB:` string protocol does **not** get deleted by this design — only its
   `select` does.
5. **Deadlock freedom costs same-type peer RPC.** Mutual reentrancy is *solved* rather than
   documented (acyclic blocking graph) — but the analysis is type-level, so two instances of
   one service may not await each other even though distinct instances would not deadlock.
   Fire-and-forget peer messaging is unaffected. Expect this to be the most-hit diagnostic; the
   message must teach the workaround, not just report the cycle.
6. **Whether a type is a service is a whole-program property.** A `spawn` in one file subjects a
   type's export methods to service rules everywhere. Mitigated by requiring the diagnostic to name
   the spawn site, but it remains real action-at-a-distance and is the cost of overloading `export`.
7. **⚠ NEW — the motivating dogfood cannot consume the feature.** `SpecWorkerPool.maxon` is shv2's
   own source and the bootstrap compiles it at stage-1; until the bootstrap parses `spawn` or is
   retired, the hand-rolled select this document opened with stays. The spec suite is the acceptance
   test; the harness is not.
8. **⚠ NEW — `spawn` arms two filed races on the day it lands.** `W218` (park registered before the
   context switch saved registers — REAL for green threads) is part of `SV1`; `W217` (un-awaited
   promises in an array never drop, exit 75) gates `SV3`. Both rows say the fix must be seen RED first.
9. **⚠ NEW — the co-owner refusal is a rule with no roster yet.** Every route by which a box gains a
   second referent (`shared` promotion, `retain-escaping`, a closure capture, a container push) is a
   route the send site must refuse, and the list must be *derived* from the ownership marks, not
   grepped. Until it is enumerated, the plain refcount's safety under `spawn` is an argument, not a gate.

### Adjacent finding — resolved, kept for the record

The 07-22 text reported `specs/async-await.md` as *"materially wrong about the threading model"*
(*"All green threads run on a single OS thread"*, *"No atomics needed"*). ⚖ **RESOLVED 2026-08-27
(EC10), in the opposite direction to the one this entry expected:** the USER RULED that an `async` call
is a coroutine of the calling green thread, so both quoted sentences are now substantially RIGHT for the
self-hosted tier and the spec was rewritten to say so (`specs/async-await.md:12-13`, byte-matched into
`specs-shv2/` bar `targets:` markers). They remain wrong only for the BOOTSTRAP, whose runtime is
unchanged and still multiplexes `async` onto worker Ms with atomic refcounts (`MemoryManager.cs:411,517`,
`__sched_max_procs` from CPU count, `EmitGtStealWork`). `specs-shv2/builtins-cpu-parallel.md` carries the
measured differential. ⇒ the residual defect is the **bootstrap's divergence from the ruling**, not the
spec's text — and it is one more reason the bootstrap is the wrong first home for a design whose spine
is "one green thread holds a box".
