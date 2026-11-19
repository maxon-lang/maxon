# `/land` briefs

Read this before writing any `/land` brief. Every brief carries the nine things below; each stage adds
its own lines.

## The model for each stage

Each stage is its own fresh agent, on the cheapest model that can do it:

| Stage | Model |
|---|---|
| §1 scout · §4(a) scan | `haiku` |
| §1 spec author · §6 documenter | `sonnet` |
| §2 implementer, every round | per piece: `haiku` for a mechanical one (a rename, a doc line); `sonnet`, the agent's default, for a contained one whose approach is settled; `opus` for one that is cross-pass, soundness-sensitive, about performance or concurrency, or leaves design choices open |
| §4(c) optimizer · a diagnosis agent · §5 first review | `opus` |
| §5 re-review of a fix delta | `sonnet` when the delta touches at most two files and no soundness checker; `opus` otherwise |

## The nine things

1. **The change in one line**, and **the cases that must go green**: the acceptance, not a topic.
2. **"This is a `/land` change"**: main checkout, no worktree, no branch, one commit at the end, which is
   the coordinator's. ⛔ Do not commit, push, rebase or stash. No self-relaunching or polling loops; never
   copy a binary into `maxon-bin/.maxon/` (a probe build goes to `--output=temp/<task>/…`).
3. **Its EXCLUSIVE file list** when more than one agent is live: one file, one owner; agents run in
   parallel only on disjoint files, sequentially otherwise.
4. **The DIAGNOSIS you already have**: symptoms, exit codes, stderr, call sites read, hypotheses refuted.
   Making an agent re-derive it is the waste delegation exists to prevent.
   - Hand it over as a **HYPOTHESIS to test against the red set**, not a specification: if the agent's
     measurement contradicts it, it reports the measurement and stops instead of building the stated
     cure. A cure or a location is a candidate, never the place to fix.
   - Never brief an unmeasured scope claim as a prohibition ("X is not affected, do not touch it"): say
     what was checked and ask for any other route.
   - Ask for `file:line` behind every load-bearing claim and a closing list of what it asserted without
     reading. "Present but I could not reach it" bounds the agent's search, not the code: assign that
     shape to the next agent explicitly.
   - A measurement is as live as its tree: one taken under a reverted mechanism, or before a rebase, is
     re-taken before it is quoted.
   - Give a stop condition a line of its own, and prescribe the case that would FAIL if the premise is
     false: a sentence nobody is invited to doubt gets built.
5. **Its STOP RULE**: stop when your filter is green; the §8 gates are the coordinator's, the full suite
   is CI's.
   - **A defect NOT RELATED to this change (§10's test) is not fixed**: the report names it — what is
     wrong, `file:line`, how it was seen, and a program that reproduces it.
   - An "iterate until X" goal carries a build budget: at most ten builds, one at a time, then report. A
     self-compile is minutes of every core and gigabytes of memory; an open-ended loop locks up the
     workstation.
   - **An implementer or diagnosis agent hands over at 150k tokens of context or 60 model calls,
     whichever comes first**, and when the coordinator says it has passed one: it writes a handover file
     of at most 40 lines (what is done, the tree state, the hypotheses refuted with their evidence paths,
     the next step) at `<scratchpad>/reports/handover-<n>.md`, and stops. The coordinator dispatches a
     fresh agent with that path. The count is the watchdog's, never the agent's own estimate.
6. **The gate table**, in one line: which runs are the agent's, which are not. Every brief tells its
   agent to be thorough, so without this line the suite runs three to five times per change and one of
   those runs gates anything.
7. **"No manual tests"**: no sabotage, no hand-run probe or scratch program to confirm anything. A check
   worth running goes into the set as a case; anything else is not run.
8. **"Write every build and run to a file in your scratchpad directory, and report each one's path and
   exit code"**, including your LAST build and whether you edited a compiled-in source after it. **The
   report is a file** at the path the brief names, `<scratchpad>/reports/<stage>-<n>.md` (the
   coordinator's scratchpad; `<n>` counts that stage's dispatches), opening with a `## Summary` of at
   most 25 lines: the verdict, the files changed, the cases and their state, each run's log path and exit
   code, the last build and whether anything was edited after it, the unrelated defects, and the claims
   made without reading. **The final message is that summary and the path**, nothing more.
9. **"Wait only on blocking tools."** A background shell or a Monitor wakes the main session, not a
   subagent whose turn has ended. Use the blocking `maxon` MCP tools (`build`, `run_spec_test`, `test`,
   `run_scale_test`); never background a run, never Monitor, never end a turn to wait. Multi-hour sweeps
   stay with the coordinator.

⛔ **Brief for DIAGNOSIS, never for COVERAGE.** "Enumerate every construct that could false-reject",
"confirm no other spec regressed", "check every case still runs": the filtered runs, §1's count check and
CI do all three, better, one step later. ⚠ **A specific instruction in a brief OUTRANKS the agent's own
stop rule**: you cannot brief thoroughness in without briefing the stop out.

## §1 scout and spec author

- **Scout** (`Explore`, `model: "haiku"`): read-only; it runs no probes — what the compiler does today
  is what §1's red run of the chosen cases shows. Ask for FACTS, not a
  recommendation: which `specs` files own this behaviour; every existing case that touches it, with file
  + line and the **verbatim text** of any that already pins it; what the neighbouring cases in that file
  look like.
- **Spec author** (`general-purpose`, `model: "sonnet"`): the exact list — file, case name, program,
  expected `exitcode`/`stdout`/`maxoncstderr` — to write into `specs/`, and **invoke the `maxon-coder`
  skill before writing a case**.

## §2 implementer, beyond the nine

- **When a case fails for want of a distinction, grep for a comment saying that distinction does not
  matter.** Read it as a hypothesis about the shapes its author enumerated and ask which one it missed. A
  comment that justifies a discard or a gate is load-bearing, and the code does what it says.
- ⛔ **Git in a throwaway clone is one failed `cd` from the shared checkout.** The script runs
  `set -euo pipefail` and `cd <clone> || exit 1`, and asserts before every writing git command that
  `git rev-parse --show-toplevel` is not the main checkout (`;` after a `cd` runs the rest in the wrong
  tree). The main checkout takes read-only git only. A repair the permission system denied goes to the
  user.
- **Invoke the `maxon-coder` skill before writing any Maxon**, and hand back only code its compile loop
  passed.
- ⛔ **WRITE NO COMMENTS** — not a `//`, a `///`, or the doc block above a registry case. §6 authors every
  comment once, after the review. A reason worth keeping goes in the REPORT, which §6's brief is built
  from.
- **The main checkout needs no `repoRoot`** for the `maxon` MCP tools. Do not commit, `git add` or push,
  and leave every `TargetIr` pin a run re-minted exactly as it lies.
- ⛔ **`specs/` is READ-ONLY to an implementer — not one byte.** It defines the language: editing a case
  redefines Maxon. §1's author is the one writer of cases, and a fix that would falsify another spec's
  committed expectation is a STOP-and-report.
- **Root causes, no workarounds**; a defect is fixed whether or not it predates you.
- **Cross-target consistency**: an x64 change needs its arm64 equivalent. The wasm lane (CI's) is not
  scalar-only: a float or `String` case failing there is a bug on that lane.
- **A new diagnostic goes through the HAND-AUTHORED registry** (no generator): add
  `<caseName> = "EXXXX"` to `maxon-bin/Compiler/ErrorCodeRegistry.maxon`, then the code that emits it.
  ⛔ **Not its doc comment**: that block is a documentation surface (`Mcp/McpErrorCodes.maxon` re-parses
  the registry source to serve `lookup_error_code`; the Error Codes page is built from it), so §6 writes
  it, and `sync-docs.mjs` failing closed on it until then is expected. Take the next free number in the
  band by reading the enum (1xxx lexer, 2xxx parser, 3xxx semantic, 4xxx IR, 5xxx emitter, 6xxx PE writer, 9xxx internal): a
  duplicate NAME does not compile; a duplicate NUMBER is caught only by `sync-docs.mjs` in §8. Never a
  bare `"E3010"` in source: name `ErrorCode.<case>`.
- **A mechanism that does not exist yet gets BUILT** — a builtin, a runtime slice, an opcode on every
  target. Size is never a reason to stop.
- **Documentation is §6's**; the implementer reports **what the change made visible to a user**, in the
  terms of `AGENTS.md`'s Documentation section, since that is the judgement it has the context to make.

## §4(a) scan

A read-only scan agent (`Explore`, `model: "haiku"`) — or, for a located fix with no §4(c) trigger, the
reviewer — is handed the diff and this list: an O(n) lookup inside an O(n) walk; a `findFirst` over a
compiler-sized array in a loop; a fixpoint, liveness or dominator tree recomputed per element; a walk over
a dense index space where the set bits were the point; an allocation in a hot path. The compiler must stay
LINEAR in program size.

## §4(c) optimizer

A `general-purpose` agent, `model: "opus"`, told to invoke `optimize`; never one that wrote the code. The
brief says this is a `/land` change — the diff is uncommitted in the main checkout and the landing gates
are the coordinator's — so it neither runs the suite nor commits.
## §5 reviewer

A `general-purpose` agent told to invoke `code-review` on the uncommitted working diff; never one that
wrote the code, whatever the change's size: that independence is the whole point.

- **A `/land` change**: uncommitted, main checkout, no worktree, no branch, gates the coordinator's
  minutes later. The skill's steps 6–7 (gates, commit) are standalone-only: skip them.
- **A finding NOT RELATED to this change (§10's test) is REPORTED, not fixed**: the coordinator's fix after the landing
  satisfies the skill's "fix it even if pre-existing".
- ⛔ **Comments are not its concern**: the tree has none by design. Do not brief one in.
- ⛔ **Do not ask it to run the full suite, prove coverage or establish correctness.** It runs no
  sabotage and no hand probes: a finding that needs a run is a case it adds to the set, or a reading-only
  finding. Its `--filter`ed runs are its own.
- **Its scope is EVERY file in the diff** — harnesses, fixtures, the extension, stdlib helpers, CI files.
  Name the directories; a reviewer left to choose reads the compiler and skips the rest.
- For a located fix with no §4(c) trigger, add §4(a)'s scan list (above).

**A change to a soundness checker that implements an approved design** (a plan with an executable model)
gets a second, separate reviewer: a `general-purpose` agent briefed with ONLY the plan's rulings and the
diff — not the implementer's reports, not your summary — whose one job is to find a program the changed
checker accepts that a ruling refuses. Each finding becomes a case in the set; an accepted program the
ruling refuses is fixed, never argued away.

## §6 documenter

A `general-purpose` agent, `model: "sonnet"`, told to invoke `documenter`.

- **The diagnosis and the paths of the implementer's and optimizer's reports**: context for judging where
  an edit would go silently wrong, not material to transcribe.
- **What the change made user-visible**, in `AGENTS.md`'s terms, or that nothing is, which it must state.
- ⛔ **It builds nothing and runs NO tests**: its verification is a code-identity proof. It regenerates
  the site pages but does not run `sync-docs.mjs --check` (§8's).
- **It does not commit, `git add`, push, rebase or stash, and changes not one byte of code.**

## A change implementing an approved design

Every brief quotes the rulings the change implements and says that making the compiler accept a program
one of them refuses is a HALT: the agent stops and reports (`situations.md`).
