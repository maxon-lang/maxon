# `tests/` — fixture corpora for the driver commands

A `specs-shv2` case is a Maxon **program** the harness compiles and runs, so it can
reach `stdlib/` and nothing else. **A DRIVER COMMAND is not that** (user ruling,
2026-09-02): `fmt`, `test`, `build` and friends are gated by spawning the compiler at
a fixture project and asserting what it reports. This directory is where those
fixtures live.

Nine corpora live here, one directory each, every path into one spelled from the CHECKOUT
ROOT — the working directory every driver inherits, and the contract
`SpecTestRunner.maxon:1666` states, along with why it is deliberately not `specDir.parent()`.

The last column names the constant each corpus is reached through. Two are not one constant
naming a directory, and both say so in the row: `fmt/`'s is written twice because its
generator MINTS what its test file reads, and `lsp/` has no constant for itself at all —
`maxon test` is pointed at that directory on the command line — so its constant names the
SERVER its tests spawn.

| corpus | read by | the constant it is reached through |
|---|---|---|
| `fmt/` | its own `fixtures.test.maxon` | `generate-expectations.py` + that file |
| `test-fixtures/` | `test-command/fixtures.test.maxon` | `FixturesDir` |
| `test-command/` | `maxon test`, under BOTH compilers | `ProjectDir` |
| `harness-fixtures/` | `spec-test` → `HarnessSelfTest` | `FixturesRelativeDir` |
| `harness-gates/` | `spec-test` → `HarnessSelfTest` | `GatesRelativeDir` |
| `lsp/` | `maxon test`, under BOTH compilers | `TestedCompilerStem` — the server, not the dir |
| `ladders/` | `spec-test` → `requireLadderIndexComplete` | `LaddersRelativeDir` |
| `parallel-compile/` | `maxon test`, under BOTH compilers | `TestedCompilerStem` — the compiler it spawns |
| `debug/` | `maxon test`, under BOTH compilers | `TestedCompilerStem` + `BootstrapCompilerStem` — the two binaries it spawns: the compiler under test, which is also what the sidecar cases build with, and the reference, which is both the second monitor and the second reader of a sidecar |

⚠ **`ladders/` is cited from outside the code that reads it.** Roughly twenty
measurement-provenance comments across the compiler, `maxon-sharp/` and
`specs-shv2/register-spill.md` name a generator by path, and `docs/optimization-log.md`
names the former path in every row minted before 2026-09-02 — a dated record, so those
rows stay as written.

⚠ **The six rules below are the `fmt/` corpus's**, and each is written against the
command `fmt` is. They are not automatically true of the other eight: `test-fixtures/`
deliberately stores LIVE `*.test.maxon` sources, because the command under test compiles
them, and `lsp/` stores a live `LspClient.maxon` the tests import.

```
tests/
  fmt/
    fixtures.test.maxon          EVERYTHING: the harness, the guards, and all 26 tests
    generate-expectations.py     mints every expectation FROM THE BOOTSTRAP
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
  harness-gates/<case>/          well-formed specs it must ACCEPT, then partly skip
  lsp/
    LspClient.maxon              a live JSON-RPC client the tests import - see rule 1
    <area>.test.maxon            one LSP method area per file
  ladders/                       hand-built scaling generators + the README indexing them
  parallel-compile/
    parallel.test.maxon          the shared half: the spawn, the staging, the counts
    <contract>.test.maxon        ONE contract per file - see its README section
    fixtures/<program>/main.maxon.fixture   stored name only - see rule 1
  debug/
    monitor-agreement.test.maxon            the DebugStream consumers, compared to each other
    byte-identical-debug-info.test.maxon    the sidecar is metadata: ONE source, two builds, one image
                                            - AND the staging/spawning/reading half every case below shares
    sidecar-dump.test.maxon                 the sidecar says something TRUE about the binary beside it
    dump-info-sections.test.maxon           --dump-info prints the sections it was named, and refuses others
    dump-info-types.test.maxon              the TYPE table describes the program's own types
    dump-info-locals.test.maxon             one local record per declared binding, three honest locations
    fixtures/trace/main.maxon.fixture       stored name only - see rule 1
    fixtures/spans/main.maxon.fixture       stored name only - see rule 1
    fixtures/locals/main.maxon.fixture      stored name only - see rule 1
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
`lsp/` tests import. That is fine and is not an exception being smuggled in: the hazard
above is `fmt` rewriting an ORACLE, and `lsp/`'s oracles are `b"…"` byte literals *inside*
its test files, not files on disk. A client that `fmt` reformats stays a correct client.
⇒ The rule to carry forward is **"nothing `fmt` rewrites may be a stored expectation"**,
not "no live `.maxon`". `fmt/` states it the strong way because every one of ITS fixtures
is a stored expectation.

⛔ **No `.maxonignore` in this directory.** ⚠ Its original justification was wrong and was
corrected on 2026-09-02: this said a marker "would hide the corpus from `fmt`", which
**generalised a MEASURED `maxon test` result to `fmt` without measuring it**.
`fmt` does not honour `.maxonignore` at all — `EnumerateFormattableFiles`
(`maxon-sharp/Program.cs:1071`) prunes only on `.git`. The reason that survives is
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

### 6. Expectations are GENERATED by running the reference, never hand-written

`generate-expectations.py` runs the **bootstrap's** `fmt` and records its real answers.
A hand-written fixture proves only *"shv2 does what I wrote down"*; a generated one
proves *"shv2 does what the bootstrap does"*, which is the whole contract for a port.
Re-run it after changing any input; never hand-edit an `expected-*` file.

## What this corpus cannot see

Written down because a limit nobody states gets mistaken for coverage.

- **It has no shape it was not given.** This corpus tests what someone thought of; a
  tree-wide sweep tests what the tree actually contains. The `sizeof`/`countof`
  divergence was found by `grep`, not by a case.
  ⭐ **That sweep HAS been run, as a diagnostic rather than a gate** (2026-09-02): both
  compilers formatted 453 real `.maxon` files — `maxon-shv2/`, `stdlib/`,
  `maxon-dev-mcp/` and the 191k-line `maxon-selfhosted/` — and the resulting trees were
  **byte-identical**, the same 8 files changed in the same order. It is not wired in
  because it is slow and needs two tree copies outside the repo; it stays the right
  first move the moment a formatting defect is suspected, and it is how the claimed
  `byte(` divergence was found to be imaginary.
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

It gates a COMPILER phase rather than a driver command, and it lives here for the same reason `fmt/` does: what it asserts is what `maxon-shv2 build`
REPORTS and EMITS at two processor counts, which a `specs-shv2` program cannot observe about
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

Three cases over two subjects that share one shape: stage a fixture, build it with the compiler under
test, and ask a second tool what the binary says about itself. The staging and spawning half is
exported ONCE from `byte-identical-debug-info.test.maxon` and shared, because every free function in a
directory shares one namespace whatever its visibility (E3006, measured here).

### The `.mxdbg` sidecar — `byte-identical-debug-info` and `sidecar-dump`

`maxon build` writes `<output>.mxdbg` beside the binary BY DEFAULT and `--no-debug-info` opts out. The
whole design rests on the sidecar changing nothing about the emitted code, so the first case builds ONE
staged source path twice, to two outputs, differing only by the flag, and compares the two images byte
for byte — plus asserts the sidecar present on one side and ABSENT on the other, which is the half a
byte comparison alone is blind to.

A sidecar that is merely PRESENT proves nothing, so the second case reads it back: `debug --dump-info`
must name this host's target, a build-id that is not all zeros, the fixture's own source file, and every
function the fixture DECLARES — read out of the staged source, so a fixture that grows a function grows
the roster — each with a non-empty code range. Then `--symbolize`, handed an offset the dump itself
published inside one of those functions, must answer that file and a line inside that function's body.
That last one is the JOIN: a function table and a line table can each be internally consistent and still
disagree, and only asking one about the other can see it.

⭐ **WHAT IS PINNED IS RELATIONSHIPS, NEVER NUMBERS.** Code offsets, the row count and the shape of a
prologue all move with codegen, and a case that pinned them would go red for every unrelated change and
teach its reader to re-bless it.

⛔ **THE DUMP'S SHAPE IS THE REFERENCE DRIVER'S**, because one sidecar printed two ways by two drivers is
the drift rule 6 exists to prevent. It is checkable by hand in the other direction too:
`bin/maxon.exe debug --dump-info <an shv2-built exe>` prints the same rows shv2 does (the two
drivers differ only in their line terminators).

### `monitor-agreement` — the two DebugStream ring consumers, compared to each other

`maxon monitor --filter=mm <exe>` creates the shared section a `--debugstream` binary writes its
ring into, spawns the binary, decodes every `mm` event and exits with the CHILD's code. shv2's spec
runner depends on that consumer for every `<!-- MmTrace -->` case, so shv2 has one of its own — and
the only way to know a second decoder reads the same wire format is to run BOTH over the same bytes.
`monitor-agreement.test.maxon` stages `fixtures/trace/main.maxon.fixture`, builds it with the
compiler under test plus `--debugstream`, runs that one binary under each monitor, strips the
`[+SSSS.mmm] ` timestamp — a property of the RUN, not of the program — keeps the `mm_` lines and
asserts the two lists are EQUAL and NON-EMPTY.

⭐ **A MONITOR'S STDOUT IS TWO HALVES AND BOTH ARE COMPARED.** The stamp that marks a decoded event also
marks its complement: the child's OWN output, which a monitor forwards verbatim. The fixture prints a
BLANK line for that half's sake — it is the one line a forwarder that reads an empty string as *"the pipe
had nothing"* drops, and it drops it while every decoded event stays identical.

⚠ **THE `bin/maxon` ARM IS A TRANSITION ORACLE AND RETIRES WITH THE BOOTSTRAP.** What this corpus
gates is AGREEMENT, which needs two decoders; when the bootstrap goes there is one and the case has
no subject left. Its expectations are not generated and there is no stored transcript: what the
fixture allocates is the memory manager's business and moves whenever `StringBuilder` or `Array`
does, so a golden list of events would go red for changes that have nothing to do with either
decoder. The non-zero count on BOTH sides is the positive control — two decoders that both read
nothing compare equal.

It applies rule 1's `.fixture` half only (no `dot-` names) and rule 4 (every child runs in the
staging directory, `temp/debug/monitor-agreement/`), and it keeps rule 5: one spawning `test`,
one file.
