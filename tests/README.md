# `tests/` — fixture corpora for the driver commands

A `specs` case is a Maxon **program** the harness compiles and runs, so it can
reach `stdlib/` and nothing else. **A DRIVER COMMAND is not that** (user ruling,
2026-09-02): `fmt`, `test`, `build` and friends are gated by spawning the compiler at
a fixture project and asserting what it reports. This directory is where those
fixtures live.

Fifteen corpora live here, one directory each, every path into one spelled from the CHECKOUT
ROOT — the working directory every driver inherits, and the contract
`SpecTestRunner.maxon:1649` states, along with why it is deliberately not `specDir.parent()`.

The last column names the constant each corpus is reached through. Two are not one constant
naming a directory, and both say so in the row: `fmt/`'s is written twice because its
generator MINTS what its test file reads, and `lsp/` has no constant for itself at all —
`maxon test` is pointed at that directory on the command line — so its constant names the
SERVER its tests spawn.

| corpus | read by | the constant it is reached through |
|---|---|---|
| `fmt/` | its own `fixtures.test.maxon` | `generate-expectations.py` + that file |
| `test-fixtures/` | `test-command/fixtures.test.maxon` | `FixturesDir` |
| `test-command/` | `maxon test`, under the compiler | `ProjectDir` |
| `harness-fixtures/` | `spec-test` → `HarnessSelfTest` | `FixturesRelativeDir` |
| `harness-gates/` | `spec-test` → `HarnessSelfTest` | `GatesRelativeDir` |
| `lsp/` | `maxon test`, under the compiler | `TestedCompilerStem` — the server, not the dir |
| `ladders/` | `spec-test` → `requireLadderIndexComplete` | `LaddersRelativeDir` |
| `parallel-compile/` | `maxon test`, under the compiler | `TestedCompilerStem` — the compiler it spawns |
| `debug/` | `maxon test`, under the compiler | `TestedCompilerStem` in `DebugHarness.maxon` — the binary it spawns: the compiler under test, which is also what the sidecar case builds with |
| `define/` | `maxon test`, under the compiler | `TestedCompilerStem` — the compiler it spawns, which is also the one whose `--define` is under test |
| `coverage/` | `maxon test`, under the compiler | `TestedCompilerStem` in `CoverageHarness.maxon` — the binary it spawns: the compiler under test, which builds every binary it measures |
| `cli/` | `maxon test`, under the compiler | `TestedCompilerStem` in `CliHarness.maxon` — the binary it spawns: the compiler under test, which is also the DRIVER under test |
| `profile/` | `maxon test`, under the compiler | `TestedCompilerStem` in `ProfileHarness.maxon` — the binary it spawns: the compiler under test, which is also the PROFILER under test and what every fixture here is built with |
| `run/` | `maxon test`, under the compiler | `TestedCompilerStem` in `RunHarness.maxon` — the binary it spawns: the compiler under test, which is also the `run` DRIVER under test and what every cached build is made by |
| `console-write/` | `maxon test`, under the compiler | `TestedCompilerStem` in `console-write-imports.test.maxon` — the binary it spawns: the compiler under test, which is also what EMITS the image the case reads |

⚠ **`ladders/` is cited from outside the code that reads it.** Roughly twenty
measurement-provenance comments across the compiler and
`specs/register-spill.md` name a generator by path, and `docs/optimization-log.md`
names the former path in every row minted before 2026-09-02 — a dated record, so those
rows stay as written.

⚠ **The six rules below are the `fmt/` corpus's**, and each is written against the
command `fmt` is. They are not automatically true of the other fourteen: `test-fixtures/`
deliberately stores LIVE `*.test.maxon` sources, because the command under test compiles
them, and `lsp/` stores a live `LspClient.maxon` the tests import.

```
tests/
  fmt/
    fixtures.test.maxon          EVERYTHING: the harness, the guards, and all 26 tests
    generate-expectations.py     mints every expectation BY RUNNING THE COMPILER
    census-sources.txt           the real compiler files the scale case formats
    selftest-cases/<Name>.in     formatter engine cases, with .expected beside them
    fixtures/<case>/
      input/                     stored names only  — see rule 1
      expected-tree/             stored names only  — see rule 1
      expected-stdout.txt  expected-stderr.txt  expected-exit.txt  argv.txt?
  test-command/
    fixtures.test.maxon          the shared half: paths, argv, the two corpus guards
    <case>.test.maxon            ONE spawning `test` per file - see rule 5
  test-fixtures/<case>/
    <name>.test.maxon            a LIVE source: `maxon test` is what compiles it
    expected.txt  expected-exit.txt  argv.txt?
  harness-fixtures/<case>/       malformed specs the harness must REFUSE
  harness-gates/<case>/          well-formed specs it must ACCEPT, then REPORT something about
  lsp/
    LspClient.maxon              a live JSON-RPC client the tests import - see rule 1
    <area>.test.maxon            one LSP method area per file
  ladders/                       hand-built scaling generators + the README indexing them
  parallel-compile/
    parallel.test.maxon          the shared half: the spawn, the staging, the counts
    <contract>.test.maxon        ONE contract per file - see its README section
    fixtures/<program>/main.maxon.fixture   stored name only - see rule 1
  debug/
    DebugHarness.maxon                      the shared half: the spawn, the staging, the folds
    sidecar-dump.test.maxon                 the sidecar says something TRUE about the binary beside it
    fixtures/spans/main.maxon.fixture       stored name only - see rule 1
  coverage/
    CoverageHarness.maxon                   the shared half: the spawn, the staging, the report readers
    coverage-line-states.test.maxon         the four line states, each attached to its own line
    coverage-branch-arms.test.maxon         the implicit `else` and the `match` case no run reached
    coverage-byte-identical.test.maxon      instrumentation reaches the flagged build and no other
    fixtures/states/main.maxon.fixture      stored name only - see rule 1
  cli/
    CliHarness.maxon                        the shared half: the spawn, and reading a roster off a listing
    no-arguments.test.maxon                 `maxon` alone answers, SHORT, sorted, and exits 0
    help-reference.test.maxon               the reference leads with the short list, then what it hides
    help-per-command.test.maxon             every documented command answers `help <command>` for itself
    hidden-command-still-parses.test.maxon  a command left off the short LIST is still a command
    withdrawn-help-flags.test.maxon         `--help` / `-h` refused BY NAME, naming the command
    unknown-command-refused.test.maxon      a word naming no command fails at both doors
    help-takes-no-options.test.maxon        `help` refuses a flag another command implements
  profile/
    ProfileHarness.maxon                    the shared half: the spawn, the staging, the report readers
    profile-hot-ordering.test.maxon         the busier function ranks first in every section
    profile-folded.test.maxon               collapsed stacks carry every path, whatever the floor
    profile-greenthreads.test.maxon         two green threads as themselves, the scheduler absent
    fixtures/hotwarm/main.maxon.fixture     stored name only - see rule 1
    fixtures/greenthreads/main.maxon.fixture   stored name only - see rule 1
  run/
    RunHarness.maxon                        the shared half: the staging, the private cache, the spawn, the slot readers
    corpus.test.maxon                       the fixture roster, and the corpus's own file rules
    hello.test.maxon                        the program's stdout, NOTHING on stderr, exit 0
    exit-code.test.maxon                    the program's exit code is the command's
    argv.test.maxon                         the tail reaches the program verbatim, driver words and all
    stdin.test.maxon                        the program reads the caller's stdin
    compile-error.test.maxon                refused, nothing run, and no build left in the slot
    cache-hit.test.maxon                    an unchanged program is not compiled a second time
    cache-miss-edit.test.maxon              an edited one is, and the new answer runs
    directory.test.maxon                    a directory is compiled as ONE project
    wordless.test.maxon                     a `.maxon` first argument IS `run` - the shebang door
    missing-path.test.maxon                 a path naming nothing is refused at both doors
    fixtures/<program>/main.maxon.fixture   stored names only - see rule 1
  console-write/
    console-write-imports.test.maxon        which console API an emitted x64-windows image imports
    fixtures/hello/main.maxon.fixture       stored name only - see rule 1
```

## The six rules, and the hazard each one answers

Every one of these is here because the obvious alternative fails **silently**.

### 1. Nothing stored here is a live `.maxon` or a real `.git`

Stored as `<name>.fixture`; a directory that must be `.git` is stored as `dot-git/`.
The mapping is `<name>.fixture -> <name>` and `dot-<name> -> .<name>`, implemented in
exactly two places that must agree: `generate-expectations.py` and
`fmt/fixtures.test.maxon`.

Two independent reasons, and the second is the one that bites:

- **git refuses to commit any path with a `.git` component**, in either the directory
  or the gitfile form. The fixture gating the worktree incident cannot be stored
  literally.
- **A real `.maxon` under `tests/` is walked by `maxon fmt` over the checkout** — the
  tool under test rewriting its own oracle. It would re-bless every expectation here,
  including `already-formatted`, which would then be green forever by construction.

⚠ **This rule is `fmt/`'s, and the live `.maxon` under `tests/` are no longer only the
drivers.** `lsp/LspClient.maxon` is an ordinary source — a 1,200-line JSON-RPC client the
`lsp/` tests import — and `debug/DebugHarness.maxon`, `coverage/CoverageHarness.maxon`,
`profile/ProfileHarness.maxon` and `run/RunHarness.maxon` are each their corpus's shared half, named so
the runner does not take them for test files. That is fine and is not an exception being smuggled
in: the hazard above is `fmt` rewriting an ORACLE, and none of these corpora keeps one on disk —
`lsp/`'s are `b"…"` byte literals inside its test files, and the other four assert properties. A
helper that `fmt` reformats stays a correct helper.
⇒ The rule to carry forward is **"nothing `fmt` rewrites may be a stored expectation"**,
not "no live `.maxon`". `fmt/` states it the strong way because every one of ITS fixtures
is a stored expectation.

⛔ **No `.maxonignore` in this directory.** ⚠ Its original justification was wrong and was
corrected on 2026-09-02: this said a marker "would hide the corpus from `fmt`", which
**generalised a MEASURED `maxon test` result to `fmt` without measuring it**.
`fmt` does not honour `.maxonignore` at all — `EnumerateFormattableFiles`
prunes only on `.git`. The reason that survives is
`test-command`'s, and it IS measured: a marker at a corpus root makes every case answer
`no .maxon files found`, exit 2 — the marker excludes the subtree from the very walk under
test. ⇒ Keep the rule; it was right for a reason nobody had checked.

### 2. stderr is compared, not just stdout

`test-command/fixtures.test.maxon:196` records the trap: a fixture passed byte-for-byte
*throughout a defect* because the harness compared stdout while the diagnostic went to
stderr. **Every `fmt` refusal writes to stderr and prints nothing to stdout**, so a
stdout-only corpus has zero coverage of the refusals that exist to prevent a
destructive default.

### 3. The resulting tree is compared, both ways

stdout says *which* files were written; it never says *what*. Compare `expected-tree/`
name-for-name and byte-for-byte, **and fail on a staged file with no counterpart**, so
an unexpected extra write is red even when stdout is right.

### 4. The working directory is the staging directory, never the checkout root

This inverts `test-command`'s contract deliberately. The flag-refusal cases exist
because a regression makes `fmt` fall back to the current directory and rewrite it in
place — **run from the checkout root, the fixture that gates that incident would
reproduce it.**

### 5. One file, and the per-file deadline is the only thing that would change that

`test-command/` puts each spawning `test` in its own file because a file is what
ONE process runs, under a 5,000 ms deadline, and its fixtures each compile a project. These format a
tiny staged tree. **Measured merged: 26 tests, 2,718 ms — 1.8x headroom**, of which the real-sources
census is about half. A split would only buy back process startups, which are not where the time
goes; if a corpus here ever does approach the deadline, shorten its slowest case or pass `--timeout=`.

One file also makes duplication impossible rather than merely discouraged: shared constants and
helpers can only be declared once, because a second declaration collides.

### 6. Expectations are GENERATED, never hand-written

`generate-expectations.py` runs the compiler and records its real answers. Re-run it after
changing any input; never hand-edit an `expected-*` file.

⚠ A generated expectation now pins BEHAVIOUR AGAINST REGRESSION and nothing more. It used to
prove agreement with a second implementation, and that is a real loss: a wrong answer both halves
of a port shared was catchable then and is not now.

## What this corpus cannot see

Written down because a limit nobody states gets mistaken for coverage.

- **It has no shape it was not given.** This corpus tests what someone thought of; a
  tree-wide sweep tests what the tree actually contains. The `sizeof`/`countof`
  divergence was found by `grep`, not by a case.
  ⭐ **RUN IT AS A DIAGNOSTIC THE MOMENT A FORMATTING DEFECT IS SUSPECTED**: format all
  453 real `.maxon` files — `maxon-bin/`, `stdlib/`, `maxon-dev-mcp/` — in a tree copy
  outside the repo and read what moved. It is not wired in as a gate because it is slow
  and needs that copy. Against a formatter believed correct it should touch the 8 files
  it is known to touch and nothing else.
- **Scale-dependent state is under-reported.** The formatter's indent level, block
  stack and group counters are monotonic across a file; fixtures reach nesting depth
  2-3 where `Parser.maxon` reaches far more. A state imbalance moves three lines in a
  small fixture and 2,189 in a real file. `formatting real sources keeps every comment,
  every doc comment and every line of code` is the only case that
  reaches this, and it is why that case stages **live compiler sources** rather than a
  committed copy — a committed copy would drift from the original and end up measuring
  a file the compiler no longer has.
- **`unchanged` is two different facts.** A file the lexer rejects is reported exactly
  like one that was already perfect. One fixture pins that for one file; nothing tells
  you it is happening to two hundred.

## `parallel-compile/` — the register allocator's worker pool

It gates a COMPILER phase rather than a driver command, and it lives here for the same reason `fmt/` does: what it asserts is what `maxon build`
REPORTS and EMITS at two processor counts, which a `specs` program cannot observe about
the compiler that compiled it. `parallel.test.maxon` is the shared half; each contract line has
its own case file — `pool-default`, `pool-pinned`, `byte-identical`, `pressure-refusal` — and
`fixtures/<program>/main.maxon.fixture` holds the two programs. It applies rule 1's `.fixture`
half only (no `dot-` names), rule 4 (the child runs in its staging directory), and departs from
rule 5 on rule 5's own terms: the four contracts need six compiles between them, and `maxon
test` runs files concurrently, so every case stages into a directory named for ITSELF under
`temp/parallel-compile/`. Its expectations are not generated — they are properties (a pinned
report prefix, byte identity, stderr equality between two runs), each guarded by a positive
control so two runs that both failed to build cannot read as agreement.

## `debug/` — what a binary can be asked about after it is built

One case: stage a fixture, build it with the compiler under test, and ask the binary what it says
about itself.

⛔ **THE SHARED HALF IS A PLAIN `.maxon`, NOT A `.test.maxon`.** `TestedCompilerStem`, the staging, the
spawning and the stream folds live in `DebugHarness.maxon`, which the runner does not treat as a test
file — so the corpus has one home for them and no case file has to be run to declare them. `coverage/`
and `profile/` below are shaped the same way.

⚠ **THE COMPILER ENFORCES HALF OF THAT AND NO MORE.** Every free FUNCTION in a directory shares one
namespace whatever its visibility (E3006, measured here), so a second copy of a helper will not compile.
A file-private `let` does NOT collide — two case files may each declare `TypesSectionName` and both are
accepted — so duplicated CONSTANTS are the half nothing catches for you, and a name renamed to dodge a
collision is the shape that duplication takes here.

### The `.mxdbg` sidecar

`maxon build` writes `<output>.mxdbg` beside the binary BY DEFAULT and `--no-debug-info` opts out.

**`sidecar-dump`** — a sidecar that is merely PRESENT proves nothing, so this one reads it back:
`debug --dump-info` must name this host's target, a build-id that is not all zeros, the fixture's own
source file, and every function the fixture DECLARES, each with a non-empty code range. Then
`--symbolize`, handed an offset the dump itself published inside one of those functions, must answer that
file and a line inside that function's body. That last one is the JOIN: a function table and a line table
can each be internally consistent and still disagree, and only asking one about the other can see it.

⭐ **WHAT IS PINNED IS RELATIONSHIPS, NEVER NUMBERS.** Code offsets, row counts, struct sizes, a type's
position in the table and the shape of a prologue all move with codegen and with the stdlib a program
pulls in; a case that pinned them would go red for every unrelated change and teach its reader to
re-bless it. Every roster this case checks is read OUT OF THE STAGED SOURCE, so a fixture that grows a
function or a binding grows what is demanded of the sidecar.

## `coverage/` — what a `--coverage` binary can be asked about after it has RUN

Three cases over one subject: `maxon build --coverage` instruments, the program counts and writes
`<exe>.mxcov` on its way out, and `maxon coverage <run|report> <exe>` joins three artifacts produced at
three different moments — the binary's `.text` hash, the `.mxdbg` sidecar's coverage-point table, and
those counters. The shared half — `TestedCompilerStem`, the staging, the spawning, the stream folds,
the summary reader and the anchor lookup — lives in `CoverageHarness.maxon`; see the note under
`debug/`.

⭐ **A COUNTER IS AN ANONYMOUS INDEX.** That is the whole reason this corpus is shaped the way it is: a
join that is off by one stride does not fail, it produces a full report of real counts on the wrong
source lines. So the cases below ask relationships rather than numbers, and every refusal is asserted
beside every happy path.

**`coverage-line-states`** — the four states are `count`, `#####` (real code never reached), `-----` (no
code emitted) and blank (no coverage point), and conflating any two is a wrong answer a reader cannot
see. The three non-numeric ones are pinned exactly; the numeric one is pinned as an ORDER — a loop BODY
outruns its own HEADER — because a hit count moves with codegen. Every claim is joined: the row printed
for an anchored line NUMBER must carry that line's own TEXT, which is the only thing that catches a
listing walk one row out.

**`coverage-branch-arms`** — the thing no line table can do. A line row is `(codeOffset, file, line, col,
flags)`, so an `if` with no `else` has NO ROW for the arm it does not take and cannot have one.
Instrumentation is the only place that count can come from, and the fixture drives that edge. `match` is
the other half and the one that reads green when it is wrong: before its arms were instrumented a program
with an untested case reported `branches: 0/0 arms taken` with nothing beside any arm. So the summary is
held to a non-zero denominator AND a numerator below it, and the untaken case arm to exactly 0.

**`coverage-byte-identical`** — instrumentation is the OPPOSITE of the debug sidecar: it must change the
image. One staged source and ONE output path built three times — the basename reaches a Mach-O image, so
each image is copied aside under a name no build writes. Two ordinary builds are asserted byte-identical
FIRST (the control: a comparison that can only say "different" proves nothing by saying it), and only
then is the instrumented build asserted DIFFERENT. It also asserts an ordinary build leaves no `.mxcov`,
which byte identity alone is blind to.

⛔ **EVERY LINE A CASE NAMES IS FOUND BY AN `// anchor: <name>` COMMENT IN THE FIXTURE**, and exactly one
line must carry it. A line number written into a case goes stale the moment the fixture grows a line, and
it goes stale silently — it still names a line, just not the one the case is about.

⚠ **THESE CASES EXCEED THE 5,000 ms PER-FILE DEADLINE**: each compiles between one and three programs
and spawns the driver repeatedly. Run the corpus with `--timeout=30000`.

It applies rule 1's `.fixture` half only (no `dot-` names) and rule 4 (every child runs in a staging
directory), and it keeps rule 5: one spawning `test`, one file. Every case stages into a directory named
for ITSELF under `temp/coverage/`, and a case needing two programs takes two staging labels — `maxon
test` runs files concurrently, and a tree is what an output path lives in.

## `profile/` — where a program's CPU time went, measured from outside it

Three cases over one subject: `maxon profile run` samples a RUNNING program by suspending its threads
from another process, and reports where the samples landed. The shared half — the driver stem, the
staging, the spawning, the folds every reader applies and the corpus's own reporting floor — lives in
`ProfileHarness.maxon`; see the note under `debug/`.

⭐⭐ **THIS CORPUS PINS NO NUMBER, AND THAT IS NOT TIDINESS — IT IS WHAT A SAMPLING PROFILER IS.** Two
runs of one program do not agree on a count, a percentage or a sample total, so every assertion here is
a RELATIONSHIP: an ordering, a presence or an absence. A case that pinned a number would fail about
half the time and teach its reader to re-bless it.

**`profile-hot-ordering`** — the busier function ranks first in every section that ranks. ⚠ The
`stacks` section is NOT one of them: a stack is named by its OUTERMOST frame, so a single-threaded
fixture has exactly ONE stack row and neither leaf appears in it. The case asserts that instead — one
row, and neither leaf naming it — which is the stronger claim, and it catches a stacks section that
wrongly mirrors the self-time table.

**`profile-folded`** — `--folded` is deliberately UNFILTERED, so the case applies its own floor. ⚠ Two
traps, both found by running it rather than by reading it: a stray sample in a foreign module is a
collapsed stack of ONE segment (`[KERNELBASE.dll] 1`), so "every folded line contains `;`" is false and
the tool is right; and a folded line splits as *path + LAST word*, not two words, because a frame name
may carry a space (`[unnamed .text]`).

**`profile-greenthreads`** — green threads appear as themselves with NOTHING enumerating them, because
`__gt_context_switch` restores RSP *and* RBP, so sampling the OS worker IS sampling the green thread.
`__sched_worker_loop` must appear NOWHERE at any reporting floor. ⚠ The two tasks are deliberately
2:1 and not 1:1 — with equal work their order is a coin flip. ⚠ Their stacks are named
`__gt_trampoline`, not `spinA`/`spinB`: the trampoline is a NAMED frame, so the case asserts two
stacks and both tasks ranked, not their names in the stacks table.

It applies rule 1's `.fixture` half only (no `dot-` names) and rule 4 (every child runs in a staging
directory under `temp/profile/`), and it keeps rule 5: one spawning `test`, one file.

## `run/` — compile a program and run it, and do not compile it again

Thirteen cases in twelve files over one subject: `maxon run <file|directory> [args...]` compiles a program (or reuses a
cached build of it) and runs it, forwarding stdin, stdout, stderr and the exit code. The shared half —
the driver stem, the staging, the per-case cache root, the two spawners and the slot readers — lives in
`RunHarness.maxon`; see the note under `debug/`.

⭐⭐ **EVERY CASE POINTS THE DRIVER AT A CACHE OF ITS OWN**, through `MAXON_RUN_CACHE_ROOT` in the child's
environment, and that is two facts at once: `maxon test` runs files CONCURRENTLY, so two cases sharing a
cache would race over one slot; and the cache the command reaches for by default is the developer's own
temp area, which a test must neither write into nor read a previous run out of. A case finds its slot by
listing `<cache>/maxon/run/` and requiring EXACTLY ONE entry — which both locates it and checks that the
driver put its build where its own layout says — rather than by respelling the key rule.

⭐⭐ **THE BUILD'S NAME IS THE KEY IT WAS BUILT UNDER**, so a case reads freshness off the slot listing and
there is nothing recorded beside a build to read instead. `slotExecutableName` is that reader; a pending
`.tmp` name is excluded from it exactly as it is excluded from the driver's hit test, because counting one
would make a run that CRASHED mid-build read like a cached one.

⭐⭐ **"THE BINARY DID NOT CHANGE" IS NOT REUSE, AND THAT IS `cache-hit`'s WHOLE DIFFICULTY.** A compiler is
deterministic: recompiling one unchanged program produces the same bytes, the same size and the same
name, so every comparison of the slot's CONTENTS is equally satisfied by a cache that never hits at all.
The discriminator is the DEBUG SIDECAR, removed between the two runs — nothing reads it back and it is not
part of the key, so its absence changes no answer the driver gives, except that a rebuild writes it again
and a reused build does not. Sabotage-proved: a hit test forced to answer false reddens this case alone.

⭐ **FRESHNESS IS A CONTENT HASH, AND `cache-miss-edit` IS WHERE THAT BECOMES OBSERVABLE.** It edits the
staged program within the same second as the build before it. A file's modification time is whole seconds
on every target, so a timestamp rule TIES there and serves the stale build; the case asserts both that the
new text runs and that the build's name moved. Its `slotExecutableName` also gates the sweep that follows a
publish: a superseded build is one no key can name again, so the slot holds ONE build afterwards.

⭐⭐ **`concurrent` IS THE ONLY CASE HERE THAT SPAWNS ITS CHILDREN BEFORE WAITING ON ANY OF THEM**, and
that is the whole of what it tests. `Configuration.run()` spawns and waits in one call, so a loop over it
is a queue; `spawnConcurrentRuns` uses `StreamingSubprocess` to get four cold runs of one program actually
overlapping, then drains each child's streams and waits. What it asserts is EMPTY STDERR alongside the
exit code and the stdout, because the exit code and the stdout are not enough to see the defect: two cold
runs sharing one output path both answer correctly, and the loser writes the backend's
`Failed to write PE file` onto the PROGRAM's stderr. Red-gate control: compile straight to the published
name instead of a `.tmp` of the run's own, and this case reddens 3/3 on exactly that assertion.

⚠ **`hello` ASSERTS AN EMPTY STDERR, AND THAT IS THE SHARPEST LINE IN THE CORPUS.** A compile at the
default log level announces every file it writes, and those lines land on the streams the PROGRAM is
speaking through. Sabotage-proved: dropping the driver's `quietTheBuildChatter` call reddens this case
alone, with three `INFO` lines in `received:`.

⚠ **`argv`'s ARGUMENTS ARE DELIBERATELY WORDS THE DRIVER KNOWS** — `--filter=x`, `-o`, `build`, `test`. A
parser that did not stop at the program would swallow `-o`'s neighbour, select `build` as the command, or
abort over a flag it does not implement; bland arguments would be green through all three.

⛔ **WHAT `concurrent` CANNOT SEE: THE CREATION OF THE SLOT DIRECTORY.** `stageCase` clears a case's cache
of FILES and cannot remove the directories — the standard library has no directory removal — so a case's
slot directory survives from its previous run and `Directory.create` short-circuits on it. Simultaneous
children racing to create one is therefore only reachable against a cache root that has never held this
program's slot: `rm -rf` the root and launch several `maxon run` of one script by hand. MEASURED that way,
six children reddened it about one attempt in three, with `could not create <slot>` on the loser's stderr —
which is why `slotDirectory` asks whether the directory is THERE rather than whether this run made it.

It applies rule 1's `.fixture` half only (no `dot-` names) and rule 4 (every child runs in its staging
directory under `temp/run/`), and it keeps rule 5: one spawning `test`, one file. `corpus.test.maxon` is
the exception and spawns nothing — it holds the two guards that are about the corpus rather than about the
driver: every fixture directory is one the harness's roster names and every name in that roster is a
directory, and no ordinary `.maxon` sits beside the case files nor any live one under `fixtures/`.

## `console-write/` — which console API an emitted x64-windows image imports

One case, and its subject is a program the compiler WROTE rather than one it ran. A Windows console
decodes what a program writes, and the two ways to make it decode UTF-8 differ in what they touch:
switching the console's OUTPUT CODE PAGE mutates a shared object the program does not own, outlives the
process on a console the process did not create, and answers nothing when the stream is a pipe or a
file; probing each standard stream with `GetConsoleMode` and writing UTF-16 through `WriteConsoleW`
touches only the streams this program holds. The case gates the second and pins the first as absent.

⭐⭐ **THE ANSWER IS IN THE BYTES, WHICH IS THE ONLY PLACE IT IS.** A PE's idata name table holds every
imported name as plain ASCII, exactly once, so `File.readBinary` plus `Array.contains(sequence)` answers
"does this program call `WriteConsoleW`" without running it and without a console to run it on. Nothing
observable from OUTSIDE a running program distinguishes the two routes — the text arrives either way
until an encoding or a redirection makes it not — and a `specs` case is a program that cannot see how it
was built.

⭐ **THE TARGET IS PINNED, NOT INHERITED.** The build names `--target=x64-windows` and spells `.exe` into
its own `-o`, because the entry stub under test is that target's. A case taking the host's default would
assert a Windows-only fact about a Mach-O or an ELF on every other host.

⛔ **THE ABSENCE IS ASSERTED BESIDE THE TWO PRESENCES**, and it is the half that would otherwise rot: a
write path that reaches `WriteConsoleW` while the stub ALSO still switches the code page satisfies every
positive demand and keeps the process-wide side effect the change exists to remove.

⛔ **THE IMPLEMENTATION MEANS ARE NOT PINNED.** `MultiByteToWideChar` is one way to obtain UTF-16 and is
not the behaviour; naming it would forbid an encoder written in Maxon that is just as correct.

⚠ **ALL THREE NAMES ARE REPORTED BY ONE RUN.** An assertion per name stops at the first disagreement and
hides the rest behind it, and the rest are what say whether the change landed halfway — so the case
collects its findings and fails once, listing every one. Its own red output is what proves the search
works in both directions: a name that IS imported is found, and one that is not is not.

It applies rule 1's `.fixture` half only (no `dot-` names) and rule 4 (the child runs in a staging
directory under `temp/console-write/`), and it keeps rule 5: one spawning `test`, one file, one compile.
