# FIX, don't FILE — and where a legitimate deferral actually goes

*Referenced from `SKILL.md` §0 and §9.*

## Deferred work lives in PLAN.md — there is NO separate backlog file

**A finding that genuinely cannot ride this rung becomes a numbered entry on `PLAN.md` — a future rung
or an accepted-debt note — and putting it there is the COORDINATOR's call, made in the plan, never an
agent's mid-rung escape.**

*(There used to be an `OPEN.md` ledger. It was retired 2026-07-21: a standing backlog **invites**
deferral, and nearly every entry in it turned out to be a defect a rung had tripped over and filed
instead of fixing. Its live findings were folded into `PLAN.md` — each into the section it belongs to,
so a step's open work sits WITH the step.)*

Agents (implementer, optimizer, reviewer) trip over bugs constantly; that is the corpus doing its job.
**What happens next is the whole game, and the default is FIX, not FILE.**

> **⭐ A STEP WITH RESIDUALS IS NOT COMPLETE.** If a rung generates a residual it does not close, the
> rung is **not done** — however green its suite — and its `PLAN.md` status says so (**◑**, not ✅).
> *"Core landed"* is not *"complete."* The residual lives with the rung (or its workstream), and the
> rung stays on the ladder until it closes. (Accepted debt measured linear-in-practice, and a deliberate
> divergence, are DECISIONS, not residuals — they do not hold a rung open.)

- **A defect in the rung's own mechanism is FIXED, or cleanly REJECTED, before merge.** It is never
  deferred. *"I found it while doing something else"* / *"it wasn't what I was asked to do"* is **not** a
  reason to defer — it is the reason the bug is now yours.
- **Only four kinds of finding legitimately become a future rung**, and each has a real reason it cannot
  ride along. Each has a specific home in `PLAN.md`:
  1. a **bootstrap (`maxon-sharp`) bug** — it needs the full C# suite as its gate (unless this IS a
     bootstrap rung) ⇒ the **"Bootstrap oracle bugs"** list beside PLAN.md's "Traps that survive";
  2. a **distinct feature** that needs its own contract / IR ops / spec-port list ⇒ its own **numbered
     rung on the ladder** (the "Future rungs" list until it is sequenced);
  3. a **correctness-neutral perf debt the optimizer has MEASURED linear-in-practice** — a
     superlinearity you can still *trigger* on a realistic input is fixed, not filed ⇒ the **"Measured
     debt"** list in PLAN.md's Workstream O, WITH the measurement and the re-measure trigger;
  4. a **follow-on slice this rung's plan sanctioned UP FRONT** (e.g. the P1.5 async residuals held for
     B1c) — sanctioned by you, in the plan, before the wave ⇒ a **named residual on the rung / its
     workstream** (and the rung is **not** marked complete while it is open).
- **An agent that trips over anything else STOPS and reports it to you** — the same reflex as a wrong
  plan or an out-of-file-list edit. **You triage: fix-now is the default for anything the rung's own
  specs would exercise.** An agent never writes a deferral into PLAN.md on its own authority.

This is the leak rule (*"leaks are not ok"*) generalized: a leak may not be deferred because it is a
defect the rung owns — and **a wrong answer the rung owns is no different.** The only change from the
old regime is the DESTINATION of a legitimate deferral: a **numbered future rung in PLAN.md**, decided by
the coordinator up front — not a row in a backlog file that anyone could quietly append to.

## The precedents that made this rule absolute

- **P1.2**: an owned-String RETURN leaked. The option to *"defer the leak to P1.4"* was **overruled**
  and the convention pulled forward.
- **P1.3 Slice 1**: a boxed-union RETURN leaked; it was **rejected (E2015)** symmetric with the
  already-deferred param, not shipped leaking.
- **The suite was green over BOTH.** Only adversarial probing found them.

## ⭐ AN UNVERIFIED arm64 LANE IS NOT A RESIDUAL

**A residual is work this rung OWES** — a mechanism it left unbuilt, a defect it sanctioned deferring.
A skipped remote lane is **coverage this process deliberately batches**, owed by the periodic sync and
not by the rung. **Mark the rung ✅ on the local targets** and write the skip into its detail row
exactly as the gate reported it — `arm64 SKIP — remote, UNVERIFIED` — which is the spelling the existing
rows already use.

**A rung parked at ◑ waiting on another machine is the failure this rule exists to prevent: it makes the
ladder's next rung unpickable over a laptop.**
