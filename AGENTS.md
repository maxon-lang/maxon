You do not care if an issue is pre-existing. Just debug and fix it. One found while a change is in
progress, and not related to it, is filed as a GitHub issue — a known issue, labelled `bug` and
`accepted`, unless one already records it — and drained, one claimed issue at a time, after that change's
push (`/land` §10; in the maintainer's fleet, the orchestrator does both — "Orchestration", below).

**When another agent asks whether your task pushed a commit that turned CI red, answer it.** If the commit
is yours, claim its `ci-red` issue (in the fleet, tell the orchestrator instead), reply that you have
taken it, and fix the red before your next push, ahead of the rest of your task.

## Orchestration

**A `design` issue on the Maxon Roadmap binds every agent, as a spec does.** The decided direction,
designs and release plan live on that GitHub project, and what is proposed but undecided on the Maxon
Backlog (`CONTRIBUTING.md`, "The roadmap"): read the rulings that touch your change before you start it. A change that contradicts one is a question on its `design` issue, not
an implementation choice.

**When `git config --global --get maxon.fleetRepo` prints a repository, you are a worker in the
maintainer's fleet** — check it at the start of every task, since a session that predates the setting
is in the fleet as soon as it is set — run by the
`orchestrator` session (`.claude/agents/orchestrator.md`), which owns direction and assigns all work so
that no two sessions own the same problem. It may be on another machine or another Claude account, so
every message goes through the private fleet repository with `node .claude/fleet/fleet.mjs`, never
through `SendMessage`. Your fleet id is your session's name (`claude -n <id>`); without one, ask the user.
`fleet.mjs` reads plan usage through the Claude Agent SDK: run `npm ci --prefix .claude/fleet` once per
checkout.

- **Before any work, tell it.** A task the user gave you directly is a `REQUEST`; with no task, send
  `READY`. Start only on its `ASSIGN`. A `STOP` means another session owns the problem: tell the user.
- **Check your inbox at every step boundary** (`fleet.mjs inbox --as <id>`, then `ack` what you have
  acted on), and send a `heartbeat --as <id> --model <model id> --state working --task <n>` there too:
  the orchestrator sizes the fleet to the plans' usage from it. A cloud session cannot name its account
  itself: it heartbeats with `--account <label>`, or with `MAXON_FLEET_ACCOUNT` set in its environment,
  the label `fleet.mjs status` shows for the account it runs on.
- **`PAUSE`:** stop at the next step boundary, keeping your task and your tree — never inside §8's gates
  or the push. Heartbeat `--state paused` and wait (below) for `RESUME`.
- **Waiting** — for an `ASSIGN`, a `RULING` or a `RESUME` — is `fleet.mjs wait --as <id>` run with
  `run_in_background`: it polls GitHub, not you, so it costs no usage, and its exit wakes you. Heartbeat
  `--state idle` (or `paused`) before you start it.
- **Stay in the assigned area.** Needing a file outside it is a `PROGRESS` naming the file before you
  edit it; one that another task owns is a `BLOCKED`.
- **Split your task, and pick each piece's model.** Break the `/land` into implementer pieces, and
  dispatch each on the model it needs, in place of the implementer's default:
  - `haiku` for an extremely simple, mechanical job, such as a comment, a rename or a doc line;
  - `sonnet` for a simple, clear piece whose approach is settled and whose change is contained;
  - `opus` for a harder piece, such as one that is cross-pass, soundness-sensitive, involves
    performance or concurrency, or leaves design choices open.
- **Design rulings come from the orchestrator, not the user.** Every halt that `/land` and
  `/plan-and-land` send "to the user" for a design ruling or an approval is a `QUESTION` to it; wait for
  its `RULING`.
- **You do not edit the boards — a `design` issue, a project item, a milestone — or another design's
  wiki page (`/plan-and-land` §4: yours is yours), and you do not file,
  claim or close a known issue.** An unrelated
  defect is a `FOUND` carrying the issue's fields (`/land` §10, "Recording"); after your push, a `DONE`
  replaces `/land` §10's draining — the orchestrator hands you the next issue. Your fix's commit still
  carries `Fixes #<n>` for the issue it was assigned.
- **A message is one line of kind, then the body:** `READY` · `REQUEST <goal>` ·
  `QUESTION <task> <question, the options, your recommendation>` · `FOUND <task>` + issue fields ·
  `PROGRESS <task> <state>` · `BLOCKED <task> <why>` · `DONE <task> <sha>`. It sends `ASSIGN`,
  `RULING`, `STOP`, `PAUSE`, `RESUME` and `PING`; answer a `PING` with `PROGRESS`.
- **Nothing about the fleet goes in this repository**, which is public: no session id, machine, account or
  usage figure in a commit, a file, a public issue or a comment on one.
- **No answer from the orchestrator within 30 minutes:** tell the user, and fall back to the user and
  `/land` §10.

- **Joining with work already under way:** send a `REQUEST` describing it and carry on with it unless the
  answer is `STOP`; its `ASSIGN` gives the task its number.

Without `maxon.fleetRepo`, none of this applies: `/land` §10 and the user are your coordination.

A symptom with no mechanism (a one-off hang, a flake that will not reproduce) is the next thing to debug,
not a risk to record: never ship it described as "unexplained". A hang and a crash in the same subsystem
are one wild pointer until shown otherwise. A clean re-run bounds the probe, not the bug: ask what shape
of run would make it certain (a process's first spawn, the first lap).

A reproduction spawned through a different process chain from the failure can refute a correct hypothesis:
a shell or runtime may rewrite the environment for a child it recognises, so the parent is part of the
input. Insert the same intermediary before believing a green repro.

Do not use "cmd /c" to run commands

Do not use heredocs or here-strings (a hook refuses them): write the text with the file-writing tool and
pass the file (`git commit -F <path>`).

**Nothing an agent makes for itself goes in the workspace.** Logs, a file's backup before an edit, diffs,
commit messages and notes go in the scratchpad directory your system prompt names, which is outside the
repository; without one, in a directory of your own under the system temp directory. **The exception is
a compiler binary**, and a project staged for one: `stdlib/` is found by walking up from the executable,
so it must sit inside the tree. It goes under `temp/<task>/` — one directory per task, never a loose
file at the top of `temp/` — and that directory is deleted when the task is finished. A SessionStart
hook deletes any `temp/` entry untouched for three days.

There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

**Maxon never avoids a breaking change.** A language that refuses to break cannot improve, and Maxon is
written with agents, which fix what a release breaks. Make the better design: keep no old spelling, no
compatibility shim, no deprecation period, and never reject a design because existing code would have to
change. Because Maxon's authors are agents, a compile error costs one round trip: prefer a deterministic
error that names the deficit and the fix over silently slower or degraded code. The error must be a
property of the program, not of a heuristic search, so the author's rewrite loop converges. A change that makes Maxon source which compiled with the previous release fail to compile owes its
entry under `## Unreleased` in `RELEASE_NOTES.md` — the change and its `Fix:` — in the same commit
(`docs/RELEASING.md`, "The release notes"). A change to a command, flag, tool output or event stream is
not such an entry; it updates its `docs/` source.

**A syntax error gets the parser's generic error, never a dedicated diagnostic.** A word the grammar does
not accept in a position — a removed spelling, a capability word where none is allowed, a keyword out of
place — fails exactly as any stray word there would. Dedicated diagnostics are for rules about a program
the grammar accepts.

## Where the rules live

This file holds what binds EVERY tree in the repository. The compiler is a project inside it, with its
own build, gates and traps, and those rules load with it:

- **`maxon-bin/AGENTS.md`** — the compiler: how to build it, the seed, one self-compile (two only for an emitted-runtime change), the
  `maxon` MCP tools, targets, `spec-test`, `tests/`, `fmt`, the error-code registry, spec fragments.
  `stdlib/`, `specs/`, `tests/`, `scripts/` and `examples/` import it, because work there is compiler work.
- **the `compiler-workflow` skill** — the long procedures: the `run_scale_test` ladder, `fixpoint.sh`,
  hosting the x64-linux lane under WSL, staging `vendor/`.
- **the skills** — each named skill is `.claude/skills/<name>/SKILL.md`. An agent that cannot invoke a
  skill reads that file and follows it.

⇒ **Touching the compiler means those rules apply whether or not they are in front of you.** If you are
about to build, gate or measure it and have not read `maxon-bin/AGENTS.md` this session, read it first.

## Documentation

**A user-visible change updates its documentation in the same commit.** User-visible means a command, flag or
help text; a diagnostic; syntax or semantics; a `public` stdlib API; a runtime environment variable; target
support; LSP, VS Code or MCP behaviour.

- **The `documenter` skill is the step that does this**, on the finished diff just before the commit,
  in the same pass that writes the change's comments. What follows is what it carries out.
- **Edit the SOURCE, never the site.** `website/MAINTAINING.md`'s "what changed → which source" map names the
  `docs/*.md` file (or the error-code registry) that owns each surface.
- **Then regenerate the site:** `node website/scripts/sync-docs.mjs`. The reference pages are generated from
  those sources and `website.yml` fails on drift, so a hand edit to a generated page is overwritten.
- **The doc-coverage gates catch an undocumented surface:** `maxon test tests/cli --filter=reference-documents`,
  `tests/mcp --filter=reference-documents`, `tests/docs`. They check names, not truth — a statement the change made
  false is found by grepping the docs for what changed.

## Code Quality

Apply these standards when writing or reviewing any code:

- **Fix the class, not the instance.** Before fixing a defect, ask where else the same decision is made,
  and whether one place should make it so that no caller can get it wrong. If one place should, fix it
  there (one predicate, one constructor, one table) and route every caller through it. A per-site patch
  that leaves the sibling sites to fail the same way is not a fix.
- **Eliminate duplicated code** — refactor shared logic into helper methods. This includes
  pre-existing duplication.
- **No silent unhandled cases** — `match`/`if` chains that don't cover all cases must throw on the
  unhandled path, not return a default value. Never use a bare `default` case in `match` — use
  `default throws` or `default panic("msg")`.
- **No silent `else` fallthrough** — if an `else` branch should never be reached, throw an error.
- **`try/otherwise` that should never fail** must use `otherwise panic("reason")`.
- **No skipped work** — look for comments implying something was skipped, deferred, or not fully
  implemented, and address them.
- **typealias names describe purpose**, not type — e.g. `BytePos` not `Offset`.
- **Typed ranges should be as specific as possible** — e.g. `int(0 to 100)` instead of
  `int(0 to u64.max)`. Use the narrowest range correct for the domain. Wide ranges are fine when there
  is no clear limit.
- **Fix all IDE-reported problems and compiler warnings.**
- **Cross-target consistency** — any change to target-specific code (e.g. x64) must have an equivalent
  change in all other targets (e.g. arm64) where applicable.
- **Consolidate redundant match arms** — collapse multiple cases with the same result into one.
- **No thin wrapper functions** — remove functions that do nothing but delegate to one other call.
- **No sentinel return values** — a function that cannot return a valid value must throw, not return
  `""`, `-1`, `null`, or similar. The anonymous identity
  (`UnnamedTypeId`), real empty text and real domain values are not sentinels.
- **Blank lines for readability** — around control flow statements and between logical sections.
- **No magic values** — replace bare literal constants with named `static` constants that describe
  their meaning. Group a related set into a `static enum` rather than scattering them.

### ⛔⭐ COMMENTS — YOU WRITE NONE. THE `documenter` SKILL WRITES THEM ALL, JUST BEFORE THE COMMIT.

**While you are writing code, write NO comments.** Not a `//`, not a `///` doc comment, not the doc
block above an `ErrorCodeRegistry.maxon` case. **Code changes shape while a change is being written**,
so a comment written before the code settles is written and rewritten several times and most of it
never reaches the commit. Only the final shape is worth commenting, and it does not exist yet.

**Every comment in this repository comes from one place: the `documenter` skill, run once on the
finished diff, immediately before the commit.** It also updates the `docs/` source the change's surface
owns and regenerates the site, so the Documentation section above is the same step.

⚠ **Running last is not a licence to write more.** What it writes is **minimal and concise — the
default is still no comment**, and most declarations end with none. The rules below bind it, and bind
every comment anyone touches:

- **Minimal and concise.** The default is **no comment**. Write one only where the code cannot carry
  the point by itself, and then in as few words as it takes. A comment per line, a banner over every
  section, and a restated signature above a function are all noise — and noise is unverified prose
  that rots while the code keeps working.
- **"Why", never "how".** The code is the "how". Comment what the reader cannot recover from it: the
  constraint, the invariant, the reason this order/bound/branch is correct, the cost that motivated an
  unobvious shape.
- ⛔ **NO HISTORY. Describe the CURRENT state only.** No "used to", "previously", "changed from",
  "renamed", "now that we…", "this was a workaround for…", no dated narration of an edit, no reference
  to a former name. **Git holds the history; a comment holds the present.** The reason a guard exists
  is a *why* and belongs — but state the constraint that still binds ("callers may hand this an
  unsorted list"), never the edit that introduced it. *(Documentation follows the same rule: it states what
  the software does — not what it used to do, and not what it doesn't do. Commit messages, `docs/optimization-log.md` and a
  release's release notes entry are where a measurement, an incident and a correction get recorded.)*
- **No exact counts, file paths or line numbers.** A comment must survive a count moving from 103 to
  104 and a file being renamed. A size that matters is approximate (`~100`); a figure or a place is
  spelled exactly only when it is itself the constraint, and a place then by its shortest stable name
  (a spec's name, a type, a function), never a path or a line.
- **Edit an existing comment only when its CONTENT must change** — a fact made false, history, a fact
  that does not belong. Rewrite the sentences that carry it, or delete them. Formatting alone (bold,
  capitals, wrap width, wording) is never a reason to touch a comment whose content is right.

