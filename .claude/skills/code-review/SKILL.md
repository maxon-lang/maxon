---
name: code-review
description: Independent code review of a completed change in the compiler. DUPLICATION IS THE TOP PRIORITY, then latent bugs, then the AGENTS.md quality checklist. Never run by the agent that wrote the code. Runs after the optimization pass and before the commit.
---

# Review a change

You review a completed change **you did not write**. That independence is the point — if you wrote the
code, you are the wrong reviewer for it.

**You run AFTER the optimization pass and you are the LAST quality gate before the commit.** That
ordering is deliberate: an optimizer rewrites code, so it can introduce exactly the duplication you
exist to catch — a fast path forked from a slow one, a helper inlined at three call sites, a specialized
copy of a general routine. **Review the optimizer's diff as carefully as the implementer's**, and treat
a performance-motivated fork as duplication until proven otherwise — if it must exist, the optimizer
owes a reason why the two cannot be one, and its absence is the finding.

**Fix what you find even if it is pre-existing** — the goal is that quality improves continuously, and
AGENTS.md is explicit that you do not care whether an issue predates the change. ⚠ **Under `/land`,
WHO fixes it depends on whether it is related to the change** (`/land` §10): a related finding you fix
in this diff; an unrelated one — pre-existing, and outside what the change wrote or changed — you
REPORT, and the caller lands it in its own `/land` right after this one.

**Do not chase green.** Your job is **QUALITY and LATENT BUGS**, not re-running the suite — iterate on
`--filter` while you work.

> ### ⛔ NO MANUAL TESTS (user directive)
> **No sabotage** — no editing code, a test, a fixture or a file to watch a check fail — **and no
> hand-run probes, scratch programs or one-off scripts** to confirm a finding. A check worth running is
> worth keeping: write it as a case (in `specs/`, or a driver command's `tests/` corpus) and run that.
> A check not worth a case is not run; report the finding from reading, and say that it is.

## First: who is running you

> ### ⛔ IF ANOTHER PROCESS DISPATCHED YOU, DO NOT RUN THE FULL SUITE AND DO NOT COMMIT.
>
> `/land` dispatches you at its §5 and runs its landing gates at §8 **on the identical tree, minutes
> later**, then commits everything as one commit; the full suite is CI's, after the push. A full run
> here is the single largest piece of duplicated work these processes have ever paid for. **Your
> `--filter`ed runs are yours; the landing gates are the caller's, once, after you.** If a refactor you
> make spans files, **say so in your report** and name the spec files it touches, so the caller's
> filter covers them — CI is where a broken caller three files away otherwise surfaces.

**Invoked standalone**, steps 6–7 below are yours: the gates and the commit. Skip them when you were
dispatched.

Prefer the `maxon` MCP tools for build/test/format — `maxon-bin/AGENTS.md` has the task→tool table. You
work in the main checkout, so no tool needs `repoRoot`.

Create a task list to perform these steps.

## 1. Invoke the `maxon-coder` skill

Its rulebook is part of the review criteria, and every fix you make goes through its compile loop.

## 2. Format modified `.maxon` files

With `fmt`'s **file** form, as `maxon-coder` says — one call per changed file. Check `git status` after
formatting.

## 3. ⭐ ELIMINATE DUPLICATED CODE — the top priority, by user directive

It applies to pre-existing duplication in the files touched, not just new code. Under `/land`,
duplication the new code takes part in is fixed in this diff; pre-existing duplication it does not take
part in is reported, for the follow-up ledger (`/land` §10).

**Hunt the dangerous kind: logic duplicated across a boundary, where nothing MAKES the copies agree.**
They agree today; a clause added to one and not the other does not read as a bug at either site. This is
the class that has actually bitten this project:

- The spec pool's parent and worker each independently reimplemented *which tests a spec selects*. They
  agreed. Nothing made them agree — and the failure mode was the parent waiting forever for records the
  worker was never going to send.
- The spec parser and the trace re-minter each spelled where a case ends — a line that ends a case in
  one and not the other re-mints a trace block into the neighbouring case.
- Hand-rolled "find separator, slice before, slice after" written once per record type.

Ask of every helper: **could a future edit to one copy silently diverge from the other, and what would
that look like at runtime?** If the answer is "a wrong answer, not a compile error," fix it.

### ⭐ The same DECISION made in two places is duplication too, and the copies need not look alike

Duplicated *code* shows in a diff. A duplicated *decision* does not: two consumers answer one question
about the program — which declaration a name resolves to, whether a head is a value or a type, a type's
identity, a declaration's visibility, whether a file is library tier — each in its own words, over its
own inputs. Nothing textual repeats, and nothing makes the answers agree.

**Ask of every change: does it answer a question some other part of the compiler already answers?**
Look hardest at a fix that teaches ONE consumer something — "the async path now consults the
qualifier", "discovery now reads the chain" — because it says other consumers ask the same question.
Find them all, name them with `file:line`, and say whether they share one source.

- **One source, every consumer asks it:** no finding.
- **Each consumer answers for itself:** that is the finding, even when this diff only touched one of them
  and every copy agrees today. The fix is one source every consumer asks, never one more consumer taught
  the rule. ⚠ It is a DESIGN finding: report it as one, with the full consumer list, so the caller can
  take it to the user. Do not file it as one more per-site follow-up.

MEASURED, 2026-10-18: a qualified chain (`stdlib.Range.create`, `sub.Calc.create`) was resolved by the
parser's token collapse, then re-derived by the async door, `spawn`, `default`, service discovery and the
E3112 census. Five `/land`s fixed it one consumer at a time, and six reviews each merged the copies inside
their own diff without asking why the next consumer needed the same fix.

### When two places must agree on one fact, climb the ladder

1. **DERIVE** one from the other, so drift is unrepresentable.
2. Otherwise **CHECK** them against each other on a path nothing can skip — an assert in the code that
   builds both, as `assertCallClobberConsistent` and `assertRegisterFacts` do — and record why a
   derivation was refused.

⛔ **A comment saying "these must move together" is not a cure.** The checked copy fails every build when
it drifts; the commented one passes every gate. And ask **where the single place RUNS**: a dedup that
allocates on a path every compile takes is a new cost curve.

A "one source of truth" refactor keeps every existing call site's ANSWER. Unifying two choices can change
the answer at a door that never used the new feature, with the suite green: a suite checks behaviour, not
"no cost". Say which call sites' answers moved, and why each was meant to.

## 4. Latent bugs — not just style

- **Resource release on EVERY path**, including throw/panic/abort. (Two `StreamingSubprocess` handle
  leaks on error paths were found exactly this way.)
- **Ordering hazards**: a flag published before the data it guards is readable. "Safe because nothing
  yields in between" is a property of today's code, not an invariant.
- Concurrency: dropped/double-dispatched work, starvation, races between a producer and a consumer.
- **A word another thread publishes with a release is read with an acquire, always.** The edge exists only
  between a release and an acquire of the SAME word, and an acquire orders only LATER accesses, so an earlier
  plain load still floats. A new word that crosses threads gets a build-time refusal of plain access, as
  `assertPublishedWordAccessIsOrdered` and `assertRunQueueObservationIsOrdered` give theirs.
- Off-by-one, unhandled boundary, silent fallthrough.

**Sweep the class.** After a root cause, state the defining predicate of the defect class and grep every
site that satisfies it, across all targets and every consumer of the fact.

- **Sweep the FIELD or fact, not the feature.** Derive the set from the defining property, never from a
  proxy (a keyword, a parameter name, "the ones I found").
- **A check filed against a SPELLING misses values that arrive another way.** List the arrivals (name,
  field, call result, parameter, return type, alias, array element, merge) and give each its own case; a
  declared type is an arrival a value-producer sweep cannot see.
- **When a change widens what a union case carries, re-read every range or `to` arm over that union** —
  exhaustiveness catches missing arms, not over-broad ones.
- **When a predicate's premise goes false, narrow it, do not delete it.** An over-approximate analysis may
  ORDER but must never REFUSE.
- **A third site for one rule is the finding.** Fixing a value's shape door by door is an enumeration, wrong
  the moment someone adds a door. Ask what the complete set is and which one place sees all of it (a
  callee's entry sees every value entering by parameter). A list that must stay is numbered, with the wrong
  answer that found each entry.
- **Grep a count before believing or correcting it.** A comment's wrong tally of its call sites, arms or
  cases has two possible causes, and one is a site that never got the rule. Name the sites rather than
  count them.
- **When a change alters what a value MEANS, enumerate every WRITER of the state it is paired with** from
  the code, never from a header that says how many there are.
- **A change that closes the last route to a mechanism deletes that mechanism's coverage inside a green
  suite** (a construct moving from "traps" to "throws" leaves the fault handler unreached). Ask what still
  reaches it, and keep a case that does.

**Check a fix against its own predicate.** Enumerate every member the fix's condition admits and test each
against the premise it states: a registry shared by two constructs, or a rule true of "most" of a set, is a
wrong answer for the rest.

**Widening a gate.** Ask what the old gate implied without saying so (a precondition a replacement predicate
only states in its header), and check every downstream test keyed on the same set: two rosters that must
agree, in two files, with nothing tying them.

**A change that inverts the soundness direction carries the whole risk.** Where a missed fact used to cost
an optimization and now costs a wrong answer, the fact set must be a view over the classified sources,
never a further list beside them.

**Identity:**

- **A key used as an identity must be injective.** A lossy key (two spellings over one callee, a `_` join
  inside the identifier alphabet) silently merges two things, and an "idempotent by label" registry turns a
  collision into a merge. Join with a byte the parts cannot contain. A second spelling of one identity is a
  second identity.
- **A lossy key is exact only while its one producer holds.** A bare name, label or boolean standing in for
  *which value* or *which sink* goes wrong with the second producer. When a comment argues a key is safe,
  enumerate every name space it lives in: a claim true on one axis reads as true on all of them. Key on the
  value, and let the authority that owns the question (the scope, for shadowing) decide.
- A name that mints a symbol the compiler also mints REPLACES it rather than colliding: "already defined"
  checks skip the builtin, and no collision check fires.
- An order-dependent symptom with no order in the code is two id spaces meeting: an id looked up in another
  interner answers by coincidence. Renaming and watching the wrong answer follow the id proves it.
- A dense per-value column is not a lookup table: a slot no value occupies can hold a live-looking tag
  beside a stale id. Prefer a door the resolution already went through.
- **A cache key takes names, never intern-order ids**, which shift when an unrelated file declares
  something. After fixing a dependence on declaration order, grep every other consumer of the same id,
  cache keys first.

**Facts that stand in for other facts:**

- **A predicate authorizing a DESTRUCTIVE act must prove what the act requires, at every level of nesting.**
  "Owns a reference" does not license nulling a slot, and a claim about a container is not a claim about its
  contents: a sole box can hold a co-owned payload.
- **A debug label never shares a slot with a semantic fact.** A name routed through a table the front end
  reads becomes the fact, for every reader of that table. An instrument gets its own side table, keyed by
  something inert and read by one function.
- **A fact a parse newly reads must key the parse memo** (`QueryEngine.parseMemoKey`), and a build flag must
  not vary a parser artifact cached on file content: one build's op stream gets served to the other. No spec
  case can see this; check that `verify-warm-rebuild` can express the axis.
- **A branch whose last arm means "none of the above" fails open** when whatever produces the "above" can
  grow, and nothing fails to compile when it does. Find the property the cases share and test that.
- **A refusal's stated basis lives in a comment and is not re-derived.** A refusal cannot go red, so a stale
  one rejects legal programs indefinitely. When a refusal blocks a program that looks legal, grep for the
  mechanism its comment names before believing it.
- **A second instance of an existing fork ports each of the first one's guards deliberately**: read every
  guard the sibling has, and copy save-and-restore as save-and-restore, not as a reset.
- **A change that makes a new value class reachable (`inf`, `NaN`, null, zero length) owes an audit of every
  loop whose exit that class can defeat.** The failure is a hang, not a wrong answer.
- **A fix to a resource with classes (GPR and FP files, signed and unsigned) gets a case crossing the
  classes.** Same-class cases stay green over a class-blind fix.
- **A cross-arm or cross-declaration check runs whole-program**, drained after every file's signatures merge
  and before `resolveTypes` erases alias names (`checkValueMergeSignatures`). At parse time it sees one file
  and silently misses forward references and other files.

**Reading-only findings** — what to ask of the diff when nothing is run:

- A deferral ("pre-existing", "already rejected", "unreachable") is a claim. Ask what the construct this
  change now permits can reach, not what today's callers reach.
  "Pre-existing" and "this change regresses it" are different questions, and only the second decides who
  fixes it now. A same-tree variant shows a class is not specific to the change; it cannot show the change
  left the reachable programs alone. That takes the actual program compiled at the base and at the tip.
- A change that widens reach (visibility, overload set, conformance, a lookup table) admits programs:
  review what now compiles that shouldn't. Before exporting a synthesized member, check something builds
  its body.
- "Cannot happen" needs a named mechanism, with the condition that ends it written next to the check.
- A two-ended protocol (retain/release, mint/free) sharing a classifier can still disagree if each end
  asks it about a different subject (e.g. different nesting depths): check both ends ask about the same
  value.
- Two correct changes on disjoint files can compose wrongly: when one seeds a fact and another consumes
  it, or correctness rests on "X never appears in what we scan", hunt the interaction.
- A justification for a gap that rests on "no test exercises it" is circular if the tests were narrowed
  (marker, `#if`, skipped lane) because of that gap.
- "This is more work than before" is a hypothesis: hand cost claims to the optimizer to measure.
- Before blaming a mechanism, ask which pass raises the symptom and what it reads; a symptom that
  precedes the mechanism in the pipeline was not caused by it.
- A diff can be clean in every file it touches and falsify a premise in one it never opens. When a change
  widens a grammar or grants a conformance, ask what is newly declarable and who assumed it was not: grep
  the premise's sentence, not the function, including every hand-written roster over the same ground and
  every comment that quotes it. A conformance the compiler grants has no `implements` clause for the checker
  to walk: ask of a new intrinsic grant what validates the member it names.
- A refusal on one route says nothing about its sibling and hides it, because every report about the feature
  names the refusal. Enumerate a door's routes, not its refusals. Adding a refusal, read every spelling of
  each operand domain (negative and positive, `0x10` and `16`, open and closed bounds): a rule that reads a
  constant-folded value inherits every gap in the fold. Keep independent conditions separate in a fix even
  where they coincide today.
- **A lifted refusal exposes what it covered, and the guard replacing it is new surface.** "A false reject,
  never a wrong answer" describes today's diagnostics, not the mechanism underneath: ask what each shape
  behind the refusal now does. Check every key the protected lookup uses (a slot rule does not protect a
  lookup keyed by name), read a reused predicate's header for the question it answers, and make the guard an
  admit-list whose residual answers "nothing": an unreasoned case then lands as a false reject, never a null
  dereference or a leak.
- A predicate's answer spent as a different fact elsewhere is a silent coupling: widening the predicate
  changes the other fact. One bit that answers "did the substitution fire" and is read as "did the callee
  take a reference" changes ownership.
- **A fact built for consumer A is not a safety floor for consumer B.** A's deliberate narrowing is B's hole,
  and neither door can see it. Give B a column of its own. Likewise a safety argument ("a missing diagnostic
  is the safe direction") is scoped to the readers it was written for: re-derive it for a second reader.
- A verdict function whose answer implies an obligation is a trap unless it carries the obligation: every
  caller that accepts "ok, this widens" and then uses the value it already holds ships the unconverted one.
  Have the check return the converted value, and assume a survey of such callers is one short.
- A field that looks like a diagnostic channel may also drive codegen. Check its readers before writing to
  it; a name carried on the expression result reaches no later statement.
- A whole-program question cannot be asked during the sweep that answers it: an interning inside
  `queryProgramSignatures` gives one argument two keys across a file boundary. Ask it in a post-sweep pass,
  and run a merge after any split, since a fuse cannot be undone. A merge makes every recorded name
  arbitrary: before making anything non-unique, grep every reader that compares it.
- A green case does not refute a structural finding. A pass samples one point: a read across two interners
  survives exactly as long as their insertion orders coincide. Answer such a finding by reading the
  invariant, or with a case that makes the coincidence fail. Ids never cross between interners; names do.
- **Refuting a finding by measurement takes every spelling of the construct** (a receiver field write: bare,
  `self.`-qualified, through a parameter). Two agreeing samples are two samples. "The compiler accepted it
  before" says nothing about what the program answers.
- **A fold that changes a value's provenance lands where ownership is decided.** A later rewrite hands a `var`
  an immortal `.rdata` record already classified as owned, and wasm's linear memory has no read-only segment
  to catch the write. A fold minting into a content-deduped pool makes the value identical to an unrelated
  literal: it owes a case for a write leaking between them.
- **Read a new routine against its siblings first.** Ask what the neighbours all do that this one does not,
  and whether the difference is deliberate. A scanner missing the newline stop its siblings all carry deletes
  a statement on a typo no corpus program contains: a green suite is evidence about the programs that exist,
  never about malformed input nobody wrote.
- **A second execution path (a batch, a guest, a remote) must deliver every per-child facility the first
  does:** cwd, stdin, environment, wrappers, instruments. A dropped one surfaces as a plausible verdict, and an
  absent instrument can make a "costs nothing" case pass. Grep each facility builder's callers. When a gate
  names a capability, ask whether it is a property of the TARGET or of THIS RUN.
- **A blanket `otherwise` over a routine that throws for more than one reason merges those reasons.** Decide
  existence without the throwing resolver.
- **When one rule has two wordings, fork the wording and never the accept set.** Pass the position through to
  a sentence table so the code that decides cannot see it. Two exhaustive matches both break on a new arm, but
  admitting an arm in only one of them compiles.

## 5. The AGENTS.md quality checklist

**`AGENTS.md`'s "Code Quality" section is the checklist and is already in your context — apply
it to every changed file, do not restate it.** The three that get missed:

- ⛔ **COMMENTS ARE NOT YOUR CONCERN — YOU REVIEW THE CODE.** Do not judge, rewrite, delete or report
  one, and do not count a missing comment as a finding. Code is written with none and the `documenter`
  skill authors them all *after* this review, so the tree you are reading has few or none by design and
  anything you wrote about them would be overwritten anyway. **This is a decision, not an omission.**
- **Typed ranges as narrow as PROVABLY correct** — a wrong narrow bound is a runtime panic; wide is fine
  where there is no real bound.
- **Cross-target consistency:** an x64 change needs its arm64 equivalent — **the CODE, which you review
  by READING. Never ask for it to be RUN**: the arm64 lanes are remote and are not in the battery, so
  "unverified on arm64" is never a review finding and never blocks the change.

**DOCUMENTATION IS A FINDING, NOT A SUGGESTION** (`AGENTS.md`'s Documentation section). ⚠ When
`/land` dispatched you, its §6 documenter writes the docs AFTER you, so the diff you read has none yet:
name what the change owes in your report for §6 rather than writing it. Standalone, these are yours:

- **A user-visible change with no update to its `docs/` source** — per `website/MAINTAINING.md`'s
  "what changed → which source" map — is a finding, fixed before the commit.
- **A doc statement the diff made untrue** is a finding too. Grep `docs/` and `vscode-extension/README.md`
  for every command, flag, API name and diagnostic the diff changes or removes.
- **The generated pages must match:** run `node website/scripts/sync-docs.mjs` and read its diff. A
  hand edit under `website/src/content/docs/docs/{cli,language,stdlib,spec,best-practices}/` is a finding.

## 6. Rebuild and re-run the gates — STANDALONE ONLY

- **The landing gates:** every row of `/land` §8's table — build if stale (`maxon run build`, which
  builds twice by itself for an emitted-runtime change), filtered specs covering what changed, the codecs
  check, and the self-compile, seed chain and documentation gates where §8 calls for them. CI runs the
  rest. They are kept in one place.
- **Scaling:** `mcp__maxon__run_scale_test` if the change touched a pass, the IR, or a data
  structure the compiler indexes by. ⚠ **It is an INSTRUMENT with no verdict — there is no green one,
  and you never touch it to make a number look better.** Read the doubling ladder straight off the
  ALLOCATION columns: **×2 is linear, ×4 is quadratic.** A curve that bends is a reading to explain.

**Check EXIT CODES. Never grep for a success string** — a past session reported a green build by
grepping for `^error` while the real failure printed `[CMP] ERROR:`. Exit **101** = memory leak.

**The `ir-specs/` Target IR pins are gates, not a review item.** A pin the change moves is re-minted
with `--update-required --filter=<spec>/` on each lane this host runs, and the diff of the pins is the
review of the emitted code (step 7).

## 7. Commit — STANDALONE ONLY

Commit to the current branch, **including every `TargetIr` pin the change re-minted**. Give a message
that summarizes **the change**, not what happened during the review.

**Re-minted pins go in with it:** `git add -A ir-specs/ specs/`. Never revert one to tidy `git status`.

## Rules of engagement

- **A DEFECT you find by PROBING is still a blocker — a WRONG ANSWER as much as a leak —
  fix-or-cleanly-reject, NEVER "defer".** *Leaks are not ok, and neither is a wrong answer in code this
  change owns*, even latent ones the committed suite is green over: a reachable leak, or a construct
  that miscompiles, is fixed, or turned into a clean compile error, before this lands.
- **Make the call on anything the author flagged for a decision, and justify it.**
- If you find something real but genuinely OUTSIDE this change — a **compiler bug this change does
  not own**, a **distinct feature**, or a **measured-linear perf debt** — **say so and leave
  it for your caller to triage**, rather than smuggling it in OR deferring it on your own authority.
  It goes in your REPORT — naming the defect, its file:line, how it was seen and a program that
  reproduces it — and the caller FIXES it, in its own `/land` right after this one (`/land` §10);
  standalone, fix it yourself in a separate commit. ⛔ It is never filed to `todo.md`. ⚠ **"Too big to ride
  along" is NOT the same as "a wrong answer in the files I just reviewed"** — that one you fix, exactly
  like a leak. Do not let this bullet become an escape hatch for a defect the change owns.

## Report

Every issue with `file:line`; what you fixed; what you deliberately left, and why; the real output of
your re-verification. **Never claim a check passed unless you ran it.**
