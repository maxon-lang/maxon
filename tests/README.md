# `tests/` — fixture corpora for the driver commands

A `specs` case is a Maxon **program** the harness compiles and runs, so it can
reach `stdlib/` and nothing else. **A DRIVER COMMAND is not that** (user ruling,
2026-09-02): `fmt`, `test`, `build` and friends are gated by spawning the compiler at
a fixture project and asserting what it reports. This directory is where those
fixtures live.

Twenty corpora live here, one directory each, every path into one spelled from the CHECKOUT
ROOT — the working directory every driver inherits, and the contract
`SpecTestRunner.specRunWorkingDir` states, along with why it is deliberately not `specDir.parent()`.

The last column names the constant each corpus is reached through. Two are not one constant
naming a directory, and both say so in the row: `fmt/`'s is written three times because its
generator MINTS what its two test files read, and `lsp/` has no constant for itself at all —
`maxon test` is pointed at that directory on the command line — so its constant names the
SERVER its tests spawn.

| corpus | read by | the constant it is reached through |
|---|---|---|
| `fmt/` | its own `fixtures.test.maxon` and `engine-cases.test.maxon` | `generate-expectations.py` + those two files |
| `test-fixtures/` | `test-command/fixtures.test.maxon` | `FixturesDir` |
| `test-command/` | `maxon test`, under the compiler | `ProjectDir` |
| `spec-harness/` | `maxon test`, under the compiler | `TestedCompilerStem` in `SpecHarness.maxon` — the binary it spawns: the compiler under test, which is also the `spec-test` harness whose refusals and gates are under test |
| `lsp/` | `maxon test`, under the compiler | `TestedCompilerStem` — the server, not the dir |
| `mcp/` | `maxon test`, under the compiler | `TestedCompilerStem` — the server, not the dir |
| `ladders/` | `maxon test`, under the compiler | `LaddersDirName` in `index.test.maxon` |
| `parallel-compile/` | `maxon test`, under the compiler | `TestedCompilerStem` — the compiler it spawns |
| `debug/` | `maxon test`, under the compiler | `TestedCompilerStem` in `DebugHarness.maxon` — the binary it spawns: the compiler under test, which is also what the sidecar case builds with |
| `define/` | `maxon test`, under the compiler | `TestedCompilerStem` — the compiler it spawns, which is also the one whose `--define` is under test |
| `build-manifest/` | `maxon test`, under the compiler | `TestedCompilerStem` in `BuildManifestHarness.maxon` — the binary it spawns: the compiler under test, which is also the driver that runs each fixture's `project.maxon` and builds what it describes |
| `coverage/` | `maxon test`, under the compiler | `TestedCompilerStem` in `CoverageHarness.maxon` — the binary it spawns: the compiler under test, which builds every binary it measures |
| `cli/` | `maxon test`, under the compiler | `TestedCompilerStem` in `CliHarness.maxon` — the binary it spawns: the compiler under test, which is also the DRIVER under test, or a copy of it staged under `temp/cli/` where an install would put it |
| `profile/` | `maxon test`, under the compiler | `TestedCompilerStem` in `ProfileHarness.maxon` — the binary it spawns: the compiler under test, which is also the PROFILER under test and what every fixture here is built with |
| `execute/` | `maxon test`, under the compiler | `TestedCompilerStem` in `ExecuteHarness.maxon` — the binary it spawns: the compiler under test, which is also the `execute` DRIVER under test and what every cached build is made by |
| `console-write/` | `maxon test`, under the compiler | `TestedCompilerStem` in `console-write-imports.test.maxon` — the binary it spawns: the compiler under test, which is also what EMITS the image the case reads |
| `emitted-runtime/` | `maxon test`, under the compiler | `TestedCompilerStem` in `EmittedRuntimeHarness.maxon` — the binary it spawns: the compiler under test, which is also what PRINTS the Target IR its cases read |
| `docs/` | `maxon test`, under the compiler | `StdlibReferenceDocument` in `stdlib-reference-documents-every-public-api.test.maxon` — the document it reads; it spawns nothing, and reads `stdlib/` through `StdlibDir` |
| `examples/` | `maxon test`, under the compiler | `TestedCompilerStem` in `ExamplesHarness.maxon` — the binary it spawns: the compiler under test, which builds every program in the checkout's `examples/` (reached through `ExamplesDirName`) and every complete program a document shows a reader |
| `warm-rebuild/` | `maxon test`, under the compiler | `TestedCompilerStem` in `WarmRebuildHarness.maxon` — the binary it spawns: the compiler under test, which is also the `verify-warm-rebuild` driver whose properties are under test |

⚠ **`ladders/` is cited from outside the code that reads it.** Roughly twenty
measurement-provenance comments across the compiler and
`specs/register-spill.md` name a generator by path, and `docs/optimization-log.md`
names the former path in every row minted before 2026-09-02 — a dated record, so those
rows stay as written.

⚠ **The six rules below are the `fmt/` corpus's**, and each is written against the
command `fmt` is. They are not automatically true of the other nineteen: `test-fixtures/`
deliberately stores LIVE `*.test.maxon` sources, because the command under test compiles
them, and `lsp/` stores a live `LspClient.maxon` the tests import.

```
tests/
  fmt/
    fixtures.test.maxon          the fixture corpus: the harness, the guards and 29 tests
    engine-cases.test.maxon      the engine corpus: 5 tests, on the staging and spawn the file above exports
    generate-expectations.py     mints every expectation BY RUNNING THE COMPILER
    census-sources.txt           the real compiler files the scale case formats
    engine-cases/<Name>.in       formatter engine cases, with .expected beside them
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
  spec-harness/
    SpecHarness.maxon            the shared half: the staging outside the checkout, the spawn, the report literals
    corpus.test.maxon            every fixture has its test file, and every test file its fixture
    refusals/<case>.test.maxon   one malformed spec `spec-test` must REFUSE, found through `__file__`
    refusals/<case>/             refusal.md  expected-refusal.txt
    gates/<case>.test.maxon      a well-formed spec it must ACCEPT, then REPORT something about
    gates/<case>/                the gate's one or two `.md`
  lsp/
    LspClient.maxon              a live JSON-RPC client the tests import - see rule 1
    <area>.test.maxon            one LSP method area per file
  ladders/                       hand-built scaling generators + the README indexing them
    index.test.maxon             the README's rows against the scripts beside it
  parallel-compile/
    parallel.test.maxon          the shared half: the spawn, the staging, the counts
    <contract>.test.maxon        ONE contract per file - see its README section
    fixtures/<program>/main.maxon.fixture   stored name only - see rule 1
  define/
    DefineHarness.maxon                     the shared half: the spawn, the tree staging, the built program's run
    override.test.maxon                     a define replaces the default, and without one the default stands
    value-with-separator.test.maxon         a value may contain `=`; the split is at the first one
    unknown-name-refused.test.maxon         a define naming nothing is an error
    non-literal-refused.test.maxon          only a written-out string literal may be replaced
    ambiguous-name-refused.test.maxon       one name reaching two declarations is refused, naming both
    reported-once.test.maxon                a refused define is reported once when a parse settles the index again
    reported-on-recheck.test.maxon          a refused define is reported by the second check of one project too
    fixtures/<program>/...                  stored names only - see rule 1
  build-manifest/
    BuildManifestHarness.maxon                          the shared half: the staging, the path-less `build` spawn, the sidecar reader, the held tree lock, the refusal check
    manifest-debug-info-false-writes-no-sidecar.test.maxon    `debugInfo: false` in the manifest: an executable and no `.mxdbg`
    manifest-debug-info-false-refuses-coverage.test.maxon     `--coverage` over that manifest is refused, and writes nothing
    manifest-no-debug-info-flag-refuses-coverage.test.maxon   `--coverage --no-debug-info` over a manifest is refused, and writes nothing
    manifest-held-tree-lock-refuses-build.test.maxon          a manifest build in a checkout whose tree lock is held exits 2, and writes nothing
    manifest-version-not-a-string-refused.test.maxon          a `version` that is not a string is refused, and writes nothing
    manifest-define-without-separator-refused.test.maxon      a define with no `=` is refused, and writes nothing
    manifest-defines-not-a-list-refused.test.maxon            `defines` that are not a list are refused, and write nothing
    manifest-define-not-a-string-refused.test.maxon           a define that is not a string is refused, and writes nothing
    manifest-source-not-a-string-refused.test.maxon           a source that is not a string is refused as malformed, and writes nothing
    manifest-program-not-written-into-project.test.maxon      the compiled manifest is kept in the run cache, so nothing lands in the project's `.maxon/`
    version-component-past-the-pe-field-refused.test.maxon    a `version` component past 65535 is refused for x64-windows, and writes nothing
    version-component-past-the-macho-field-refused.test.maxon a second `version` component past 1023 is refused for arm64-macos, and writes nothing
    version-component-not-a-number-refused.test.maxon         a `version` component that is not a number is refused, and writes nothing
    fixtures/<project>/project.maxon.fixture  main.maxon.fixture   stored names only - see rule 1
  debug/
    DebugHarness.maxon                      the shared half: the spawn, the staging, the folds, the event reader
    sidecar-dump.test.maxon                 the sidecar says something TRUE about the binary beside it
    byte-identical-debug-info.test.maxon    the sidecar never decides an instruction; `--no-debug-agent` is the one exception
    dump-info-sections.test.maxon           a word that is not a section is refused by name, and a real list prints only itself
    sidecar-local-types.test.maxon          every local of a two-file program is described under its own type
    sidecar-local-live-range.test.maxon     a local row is scoped to the code it is live over
    sidecar-array-element-type.test.maxon   an Array local names its element type
    sidecar-generic-and-clone-lines.test.maxon   a per-type generic body and a synthesized clone carry line rows
    monitor-sched-events.test.maxon         `monitor --filter=sched` shows a green thread's spawn and await
    x64-classifies-each-instruction-class.test.maxon   `debug --classify=` gives a length and a class per class
    classify-refuses-a-repeated-hex-prefix.test.maxon   a second `0x` inside a sequence is refused, never stripped
    debug-refuses-wasm.test.maxon           a wasm module is refused by the name of its target
    debug-refuses-foreign-sidecar.test.maxon     a sidecar describing another build is refused, its own build the control
    debug-refuses-a-no-debug-agent-build.test.maxon   a `--no-debug-agent` binary reports that nothing attached
    batch-run-to-exit.test.maxon            a completed session exits 0 and reports the program's code as data
    crash-exits-nonzero.test.maxon          a fault is a `crash` event and a failed session
    timeout-before-run.test.maxon           a program that never stops times out and is not left running
    timeout-during-step.test.maxon          a `finish` out of a frame that never returns times out
    heapless-program-attaches.test.maxon    a program with no heap still attaches, stops and resumes
    breakpoint-on-each-instruction-class.test.maxon   every anchored line is hit and resumed, and the program answers as it does undebugged
    break-in-inlined-function-hits-every-copy.test.maxon   a line the inliner copied twice is armed at both copies
    break-fuzzy-function.test.maxon         exact, then `Type.method`, then a word prefix
    break-ambiguous-lists-candidates.test.maxon   two functions answering one name is an ambiguity, never a silent pick
    break-no-match-suggests.test.maxon      a name nothing answers to names the nearest function
    backtrace-at-breakpoint.test.maxon      the callers outward, innermost frame first
    inline-frames-in-backtrace.test.maxon   a spliced leaf is a frame of its own, marked inlined
    stop-shows-a-source-window.test.maxon   a stop carries the source around its line, that line marked current
    batch-step-next-finish.test.maxon       `step` enters, `next` steps over, `finish` returns
    step-off-a-conditional-jump.test.maxon        a step onto a SIMULATED jump still publishes the stop that follows it
    next-stops-at-inner-breakpoint.test.maxon     a stepped-over call's breakpoint is still honoured
    finish-stops-at-inner-breakpoint.test.maxon   a walk does not swallow a breakpoint it passes
    finish-from-outermost-refused.test.maxon      a `finish` with no caller is refused, not waited on
    two-machines-hit-one-breakpoint.test.maxon     every green thread reaching one breakpoint stops on it
    clear-while-another-machine-is-mid-trap.test.maxon   a breakpoint cleared with other machines still inside it kills nothing
    gt-breakpoint-in-coroutine.test.maxon   a breakpoint inside an `async` body stops on the green thread running it
    values-render.test.maxon                every kind of local renders as itself, an enum by its raw tag
    values-register-local.test.maxon        a register local is read out of the stop's register file
    values-optimized-out.test.maxon         a local whose range does not cover the stop is unavailable, not a number
    values-unknown-local-and-field.test.maxon     a name and a field path nothing answers to are errors
    complete-command-words.test.maxon       the first word completes to the command vocabulary
    complete-function-names.test.maxon      a break target completes to the program's functions
    unknown-command-suggests.test.maxon     a word nothing answers to names the nearest command
    repl-script-over-stdin.test.maxon       the REPL reads stdin and answers in text, never in JSON
    cond-int-equals.test.maxon              an integer condition stops on the one iteration that satisfies it
    cond-bool.test.maxon                    a bool condition is one byte wide, and stops where the fixture sets it
    cond-never-true-exits.test.maxon        a condition nothing satisfies stops nothing and costs one trap a hit
    cond-replaced-by-unconditional.test.maxon   a plain `break` over a conditional one drops the condition
    cond-float-refused.test.maxon           a float local is refused by name, and the breakpoint is not armed
    cond-string-local-refused.test.maxon    a String local is refused by name, and the breakpoint is not armed
    cond-unknown-local-refused.test.maxon   a name the stop pc has no record for is refused, never guessed
    gt-threads-list.test.maxon              `threads` lists every green thread, exactly one of them running
    gt-backtrace-of-a-parked-worker.test.maxon   a parked worker's stack is walked from its own saved frame
    gt-select-reads-a-parked-workers-locals.test.maxon   `gt` selects, `locals`/`backtrace` follow it, stepping is refused
    gt-park-resume.test.maxon               a held thread runs nothing until it is released, and then runs on
    gt-park-refuses-the-running-thread.test.maxon   holding the thread on a machine is refused by reason
    gt-park-of-a-finished-thread-is-refused.test.maxon   an id whose thread has completed is `no-such-thread`
    gt-words-after-exit-answer-not-running.test.maxon   after the exit every word says the program is gone
    stop-is-a-consistent-snapshot.test.maxon   with two processors a stop names its machine and its thread
    pause-stops-a-spinning-program.test.maxon   `pause` is the only way into a program that plants no trap
    timeout-leaves-the-program-running.test.maxon   a timeout leaves it running, and the session close still reaps it
    breakpoint-set-while-running-is-hit.test.maxon   a breakpoint armed into running code is placed and hit
    clear-while-running.test.maxon          a breakpoint cleared live fires no more, and nothing faults
    trace-slice-at-stop.test.maxon          `trace` shows the DebugStream events committed before the stop
    trace-unavailable-without-debugstream.test.maxon   a build with no producer says so, and answers no list
    trace-unavailable-without-the-flag.test.maxon      the same build without `--trace` names the other reason
    fixtures/<name>/main.maxon.fixture      stored names only - see rule 1
  coverage/
    CoverageHarness.maxon                   the shared half: the spawn, the staging, the report readers
    coverage-line-states.test.maxon         the four line states, each attached to its own line
    coverage-branch-arms.test.maxon         the implicit `else` and the `match` case no run reached
    coverage-byte-identical.test.maxon      instrumentation reaches the flagged build and no other
    fixtures/states/main.maxon.fixture      stored name only - see rule 1
  cli/
    CliHarness.maxon                        the shared half: the spawn, a staged copy of the compiler, and reading a roster off a listing
    no-arguments.test.maxon                 `maxon` alone answers, SHORT, sorted, and exits 0
    help-reference.test.maxon               the reference leads with the short list, then what it hides
    help-per-command.test.maxon             every documented command answers `help <command>` for itself
    help-lists-trace-flags.test.maxon       `help build` lists `--async-trace` and `--debugstream`, which the parser accepts
    profile-usage-names-every-option.test.maxon   every option `profile`'s usage body documents is in its `Usage:` line (x64-windows only, as `profile` is)
    hidden-command-still-parses.test.maxon  a command left off the short LIST is still a command
    unknown-command-refused.test.maxon      a word naming no command fails at both doors
    help-takes-no-options.test.maxon        `help` refuses a flag another command implements
    upgrade-refuses-a-container-image.test.maxon            MAXON_IMAGE set: refused, naming `docker pull` of that image
    upgrade-refuses-a-checkout.test.maxon                   a source checkout: refused, pointing at `git pull`, `--dry-run` or not
    upgrade-refuses-homebrew.test.maxon                     a keg under `Cellar/maxon/<version>/`: refused, naming `brew upgrade`
    upgrade-refuses-an-unrecognised-layout.test.maxon       a flat `maxon` + `stdlib/`: refused, giving the install one-liner
    upgrade-dry-run-names-the-install.test.maxon            `<root>/bin/maxon`: names THAT root, never the caller's MAXON_INSTALL, runs nothing
    upgrade-takes-no-arguments.test.maxon                   a positional argument and a foreign option are both refused
    dry-run-is-upgrade-only.test.maxon                      every other command refuses `--dry-run`
    reference-documents-every-command.test.maxon            docs/CLI_REFERENCE.md has a `###` heading naming `maxon <command>` for every command `help` documents, and spells every option it lists
    reference-documents-only-real-options.test.maxon        every `--option` that document shows is listed by `help` or a subcommand's own usage (x64-windows only, as `profile` is)
    build-directory-without-output-names-the-directory.test.maxon   `build <dir>` with no `--output=` writes `<dir>/<dirname><ext>`, staged outside the checkout
    build-walk-skips-a-case-folded-manifest.test.maxon      a `BUILD.maxon` beside the program is not compiled as source
    census-by-tag-reports-a-table.test.maxon                `--census-by-tag` prints the residency census's per-tag table, and nothing prints it without the flag
    interner-presize-never-regrows.test.maxon               every source file's type-name interner reports itself under `--log=compiler:debug`, and none of them regrew
    wasm-build-without-tools-is-an-error-not-a-panic.test.maxon     an install-shaped copy outside the checkout, with no `vendor/`: exit 1 naming `wasm-tools`, no panic
    wasm-build-reports-the-module-size.test.maxon           a wasm32-wasi build's `Wrote N bytes of code` has N > 0
  profile/
    ProfileHarness.maxon                    the shared half: the spawn, the staging, the report readers
    profile-hot-ordering.test.maxon         the busier function ranks first in every section
    profile-folded.test.maxon               collapsed stacks carry every path, whatever the floor
    profile-greenthreads.test.maxon         two green threads as themselves, the scheduler absent
    fixtures/hotwarm/main.maxon.fixture     stored name only - see rule 1
    fixtures/greenthreads/main.maxon.fixture   stored name only - see rule 1
  execute/
    ExecuteHarness.maxon                        the shared half: the staging, the private cache, the spawn, the slot readers
    corpus.test.maxon                       the fixture roster, and the corpus's own file rules
    hello.test.maxon                        the program's stdout, NOTHING on stderr, exit 0
    exit-code.test.maxon                    the program's exit code is the command's
    argv.test.maxon                         the tail reaches the program verbatim, driver words and all
    stdin.test.maxon                        the program reads the caller's stdin
    compile-error.test.maxon                refused, nothing run, and no build left in the slot
    cache-hit.test.maxon                    an unchanged program is not compiled a second time
    cache-miss-edit.test.maxon              an edited one is, and the new answer runs
    cache-sweeps-older-formats.test.maxon   a published build discards an older cache format's builds and keeps a newer one's
    concurrent.test.maxon                   simultaneous cold runs of one program each behave like the only one
    directory.test.maxon                    a directory is compiled as ONE project
    wordless.test.maxon                     a `.maxon` first argument IS `run` - the shebang door
    missing-path.test.maxon                 a path naming nothing is refused at both doors
    fixtures/<program>/main.maxon.fixture   stored names only - see rule 1
  console-write/
    console-write-imports.test.maxon        which console API an emitted x64-windows image imports
    fixtures/hello/main.maxon.fixture       stored name only - see rule 1
  emitted-runtime/
    EmittedRuntimeHarness.maxon             the shared half: the staging, the spawn, the printed body, the line scan, the plain-load reading
    steal-reads-the-victim-ring-with-acquire-loads.test.maxon  the thief reads another P's runqHead, runqTail and runnext with `ldar`, on both arm64 lanes
    locked-relist-doors-recheck-the-owner-with-an-acquire-load.test.maxon  both doors that finish a slot free under `__slab_lock` re-read the span's owner word with `ldar`, on both arm64 lanes
    fixtures/spawn/main.maxon.fixture       stored name only - see rule 1
  examples/
    ExamplesHarness.maxon                   the shared half: build one example, or one document's program, into temp/examples/<name>/, run it, check its answer
    basic.test.maxon                        exits 42, the value its `main` returns
    hello.test.maxon                        prints `Hello, world!` and exits 0
    binary-trees.test.maxon                 the published n=10 checks, exit 0
    fannkuch-redux.test.maxon               the published n=7 answer and the documented n=10 one, flip count as exit code
    maxgrep.test.maxon                      the flags, the two failing exit codes, argument order, a binary file and a skipped `.git`
    msort.test.maxon                        byte order on the key, a stable sort, the flags, `--key=N`, `--jobs=N` and the failing exit code
    multifile.test.maxon                    the directory builds as one project and exits 5
    nbody.test.maxon                        the published n=1000 energies, exit 0
    spectral-norm.test.maxon                the published n=100 norm, exit 0
    homepage-hero.test.maxon                website/src/pages/index.astro's hero prints `listening on 8080`, exit 0
    readme-hero.test.maxon                  README.md's first program prints `listening on 8080`, exit 0
    introduction-first-taste.test.maxon     the docs introduction's "A first taste" prints `listening on 8080`, exit 0
    first-program-hello.test.maxon          the first-program page's "Hello, exit code" prints nothing, exit 0
    first-program-ranged-type.test.maxon    its "Adding a ranged type" prints `listening on 8080`, exit 0
    first-program-labeled-blocks.test.maxon its "Labeled blocks" prints `iteration 0` to `iteration 9`, exit 0
    first-program-try-otherwise.test.maxon  its "Fallible operations" prints `seat 1: Grace` then the fallback `seat 5: empty`, exit 0
  warm-rebuild/
    WarmRebuildHarness.maxon                the shared half: the staging and the spawn
    warm-equals-cold.test.maxon             `verify-warm-rebuild` holds on a program whose parses mint instances
    filed-type-reuse.test.maxon             a bytes-only edit re-parses one file when a parse files a type
    fixtures/<program>/<name>.maxon.fixture stored names only - see rule 1
  mcp/
    McpHarness.maxon                        the shared JSON-RPC stdio harness and JSON helpers
    standard.test.maxon                     standard user-facing MCP server tests (8 standard tools)
    dev.test.maxon                          contributor MCP server tests (11 tools + --dev)
    scale-defaults-agree-with-help.test.maxon    `run_scale_test` states the defaults `help scale-test` states
    rebuild.test.maxon                      a running server survives its image being replaced on disk
    rebuild-over-a-running-previous.test.maxon   a self-rebuild succeeds while a server runs its `.previous`
    rebuild-with-a-compile-error.test.maxon      a self-rebuild that fails to compile leaves the slot untouched
    reference-documents-every-tool.test.maxon    docs/CLI_REFERENCE.md's `## MCP Server` section names every tool and argument `--dev` advertises
    check-reports-a-type-error-and-writes-nothing.test.maxon   `check` answers a type error's code, is not the warm-rebuild gate, and writes nothing
    dump-ir-answers-the-ir-text.test.maxon       `dump_ir` answers the IR of `main` as text, and writes nothing
  docs/
    stdlib-reference-documents-every-public-api.test.maxon   docs/STDLIB_REFERENCE.md names every `public` declaration in `stdlib/*.maxon`
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
`profile/ProfileHarness.maxon`, `execute/ExecuteHarness.maxon`, `cli/CliHarness.maxon`,
`define/DefineHarness.maxon`, `build-manifest/BuildManifestHarness.maxon`, `examples/ExamplesHarness.maxon`, `warm-rebuild/WarmRebuildHarness.maxon`, `emitted-runtime/EmittedRuntimeHarness.maxon`, `spec-harness/SpecHarness.maxon` and `mcp/McpHarness.maxon` are each their corpus's shared half,
named so the runner does not take them for test files. That is fine and is not an exception being
smuggled in: the hazard above is `fmt` rewriting an ORACLE, and none of these corpora keeps one on disk —
`lsp/`'s are `b"…"` byte literals inside its test files, `examples/`'s are string constants inside its
case files, and the rest assert properties. A helper that `fmt` reformats stays a correct helper.
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

### 5. Two files, one per corpus, and the per-file deadline is the only thing that would split them further

`test-command/` puts each spawning `test` in its own file because a file is what
ONE process runs, under a 5,000 ms deadline, and its fixtures each compile a project. These format a
tiny staged tree. `fixtures.test.maxon` holds the fixture corpus and the real-sources census (29
tests), and `engine-cases.test.maxon` the engine corpus (5 tests); **both files together measured
about 1.9 s**. A split would only buy back process startups, which are not where the time goes; if a
corpus here ever does approach the deadline, shorten its slowest case or pass `--timeout=`.

The engine corpus's parity, comment-multiplicity and idempotence checks share ONE test, because each
reads the same two `fmt` runs, and two spawns fit the deadline where a test per property would not.

The engine file declares no staging, spawning or run check of its own: it calls the ones
`fixtures.test.maxon` exports, and a second copy of a free function in this directory would not
compile (see the note under `debug/`).

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
  real `.maxon` files — `maxon-bin/`, `stdlib/` — in a tree copy
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

## Staging outside the checkout — `spec-harness/`, `cli/` and `lsp/`

Three corpora stage under the host temp area (`TEMP` on Windows; `TMPDIR`, else `/tmp`, elsewhere)
rather than under `temp/`, each for a premise the checkout would falsify: `spec-harness/` runs
`spec-test` where no tree lock is taken, `cli/` needs a working directory with no `vendor/` above it,
and `lsp/` a tree with no `project.maxon` above it.

⛔ **A CASE STAGES INTO ONE FLAT DIRECTORY NAMED `<prefix>-<pid>-<case>`** (`__Builtins.currentProcessId()`),
cleared first and removed when the case finishes, pass or fail. A name without the pid would let two runs
on one machine — two checkouts, or two sessions — delete each other's directories mid-run: in-tree
staging is covered by the checkout's tree lock, and the host temp area has nothing of the kind. A case
that panics or times out still leaves its `<pid>` directory behind.

In `cli/` the removal lives once, in `CliHarness.maxon`: a case is an `OutsideCheckoutCase` handed to
`requireHeldOutsideCheckout` (or `requireHeldInAnInstallOutsideCheckout`), which removes the directory
whether the case passed or threw. The staging function itself is private, so no case can stage outside
the checkout without that removal. An interface rather than a closure, because a closure may not throw
(E3101). In `lsp/` each case discards its directory once its session is a value, before it asserts.

## `spec-harness/` — the spec harness's own refusals and gates

`spec-test` refuses a malformed spec by PANICKING while it parses its own corpus, so a refusal is
observable only from outside the process: every case here spawns `spec-test` at a fixture and reads its
exit code and output. The shared half is `SpecHarness.maxon`.

- **`refusals/<case>/`** holds ONE malformed `refusal.md` and `expected-refusal.txt`, a substring the
  refusal must print. Trailing line terminators are trimmed from it, and it must then be non-empty and
  a single line: the part of the message that names the refusal, never the `<file>:<line>` a panic
  prints in front of it, which moves with every edit above it. The run must exit non-zero AND print it —
  a fixture that fails for some other reason proves nothing about its refusal.
- **`gates/<case>/`** holds well-formed specs the harness must accept and then report something about:
  a live-network case left out of a default run and named, an `alone` case run with nothing beside it, an
  orphaned golden named by the census, a drifted golden named and counted, and the two marker shapes the
  reference grammar reads. Each fixture's own preamble says what its test asserts.
- **`corpus.test.maxon`** holds the pairing: every fixture directory has its `<case>.test.maxon` beside
  it, every test file its fixture, and every refusal fixture exactly one spec and its expectation.

⭐ **A TEST FILE FINDS ITS FIXTURE THROUGH `__file__`.** `refusals/<case>.test.maxon` is one call to
`requireRefusalFires()`, whose defaulted argument is the calling file's path, so a test file cannot name
the wrong fixture.

⛔ **EVERY FIXTURE IS COPIED UNDER THE HOST TEMP AREA BEFORE IT RUNS.** `TreeLock` takes no lock for a
spec directory with no `stdlib/` above it, so concurrent test files and a developer's own `spec-test`
never contend, the compiler needs no exemption for these runs, and nothing a run writes — `fragments/`,
`.spec-tmp/`, `temp/` — lands in `tests/`. `SpecHarness` refuses to stage anywhere a `stdlib/` sits
above, and refuses a fixture holding a subdirectory, so every run starts from the fixture's spec files
and nothing else.

⚠ **THE COMPILER'S REPORT TEXT IS SPELLED HERE AS LITERALS.** A test program cannot import compiler
source, so the verdict line, the result-note entry, the census entry and its headline, and the drift
summary are written out in `SpecHarness.maxon` and the gate files. A rewording in `GoldenCensus` or
`SpecTestRunner` fails this corpus, which `spec-test` does not run.

⭐ **EVERY GATE KEEPS ITS CONTROL.** The orphan gate plants a golden its lane CAN compare and requires it
unnamed, and requires the census headline PRESENT while orphans are planted — otherwise its clean-run
check for the headline's absence would pass on a reworded headline. The drift gate corrupts TWO goldens,
so "counted" is distinguishable from "noticed", and leaves a third intact that must not be named.

⚠ **THE ORPHAN GATE PLANTS ON `x64-windows` ON EVERY HOST.** The census covers every lane, not the
run's, so a fixed lane makes the same planted golden unreadable everywhere.

⚠ **RUN IT AS `maxon test tests/spec-harness --timeout=15000`.** The drift gate runs `spec-test` three
times, about 4.1 s, which leaves the 5,000 ms default no margin.

## `ladders/` — the index and the scripts it indexes

`index.test.maxon` holds `README.md` against the `*.sh` beside it in three directions — a script with
no row, a row naming no script, a script named by two rows — and fails when either side is empty,
because a comparison against nothing passes. It spawns nothing and runs at the default deadline.

## `parallel-compile/` — the compiler's worker pools

It gates a COMPILER phase rather than a driver command, and it lives here for the same reason `fmt/` does: what it asserts is what `maxon build`
REPORTS and EMITS at two processor counts, which a `specs` program cannot observe about
the compiler that compiled it. `parallel.test.maxon` is the shared half; each contract line has
its own case file — `pool-default`, `pool-pinned`, `byte-identical`, `rdata-order`, `log-order`,
`pressure-refusal`, and for the front end's pool:

- `front-end-pool-pinned` — under `MAXON_MAX_PROCS=1` the front end reports one worker over one processor
- `front-end-pool-default` — at the default it reports min(P, F) workers over P processors, exactly once, F being the most files one drain dispatched
- `front-end-byte-identical` — a two-file project whose parses mint instances in both files, and one of whose files is folded again with the other's row-set answer, emits the same image at both counts

`fixtures/<program>/` holds the programs, each as `<name>.maxon.fixture`. It applies
rule 1's `.fixture` half only (no `dot-` names), rule 4 (the child runs in its staging directory),
and departs from rule 5 on rule 5's own terms: the contracts need two compiles each, and `maxon
test` runs files concurrently, so every case stages into a directory named for ITSELF under
`temp/parallel-compile/`. Its expectations are not generated — they are properties (a pinned
report prefix, byte identity, stderr equality between two runs), each guarded by a positive
control so two runs that both failed to build cannot read as agreement.

## `debug/` — what a binary can be asked about after it is built, and what a debugger can do to it

Every case here stages a fixture, builds it with the compiler under test, and then asks that binary
something: what its sidecar says about itself, what `maxon monitor` sees while it runs, or — for most of
the corpus — what `maxon debug` can do to it while it is RUNNING.

⛔ **EVERY CASE THAT DEBUGS A RUNNING PROGRAM NEEDS `--timeout=`.** A file's default deadline is 5,000 ms
and one of these spends a compile plus a debugged run, so the corpus is run as
`maxon test tests/debug --timeout=60000`. The deadline is per FILE, and one file is one spawning `test`.

⚠ **THE DRIVER AND THE DEBUGGEE ARE THE SAME BINARY AS THE RUNNER.** `TestedCompilerStem` names it once:
the compiler that builds each fixture, the driver that debugs it, and the program running the case. A
runner broken badly enough to report green having run nothing cannot detect itself — READ THE PASS COUNT.

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

**`sidecar-local-types`** — a two-file project, because one file cannot see this: each file interns its type
names into a table of its own and the merge renumbers every file but the first, so a local's declared type
names the right type in the sidecar only if the renumbering reached it. Every `let <name> = <Type>.create(`
binding in the staged sources is a local whose row must end `: <Type>`.

⭐ **WHAT IS PINNED IS RELATIONSHIPS, NEVER NUMBERS.** Code offsets, row counts, struct sizes, a type's
position in the table and the shape of a prologue all move with codegen and with the stdlib a program
pulls in; a case that pinned them would go red for every unrelated change and teach its reader to
re-bless it. Every roster this case checks is read OUT OF THE STAGED SOURCE, so a fixture that grows a
function or a binding grows what is demanded of the sidecar.

### `maxon debug` — the interactive debugger

⭐⭐ **WHAT IS PINNED IS PARSED EVENTS AND ANCHORED LINES, NEVER BYTE TEXT AND NEVER A NUMBER.** A
`--batch` session writes one JSON object per line on stdout, and every case reads it through
`batchEvents` and asks about FIELDS; a line a case cares about is found by the `// anchor: <name>`
comment exactly one line of the fixture carries, so no case writes a line number or a code offset down.

⛔ **A REFUSAL IS ASSERTED BESIDE ITS CONTROL.** `debug-refuses-foreign-sidecar` runs the subject with its
OWN sidecar first: a refusal equally happy to refuse a good build says nothing about the foreign one.

⛔ **THE CLASSIFIER HAS A GATE OF ITS OWN.** Arming a breakpoint displaces one instruction and resumes by
executing it out of line, so the driver must answer a LENGTH, a CLASS, a condition nibble and a
displacement position for it. A line-anchored breakpoint only ever lands on whatever instruction a
statement happens to begin with, so `x64-classifies-each-instruction-class` asks the classifier directly,
through `debug --classify=<hex>`, over a table with every class in it — including the two indirect classes
the agent refuses and bytes this build cannot decode at all.

⛔ **A CONDITION IS EVALUATED IN THE AGENT, AND THAT IS WHAT THE `cond-*` CASES MEASURE.** A driver that
filtered hits of its own would publish every one and swallow the ones it did not want — the same picture
to a reader and a different program — so each case pins the STOP COUNT against the number of times the
anchored line runs undebugged. The `condition` fixture calls `hit` from two sites on purpose: a function
the inliner splices has no local records of its own, and a condition is compiled from the record covering
the breakpoint's pc.

⛔⛔ **A GREEN-THREAD CASE NAMES ITS THREAD BY A SELECTOR, IN ONE SESSION, AND ASSERTS ON THE ID THE
EVENT REPORTS BACK.** A roster's STATUSES are not reproducible across sessions — a worker's sleep timer
fires before one stop and after the next — so a case that read an id out of a first session and asserted
its status in a second was asserting a coincidence. MEASURED: 3 of 60 runs red, all of them
`expected "waiting" received "ready"` about an id that was `waiting` when it was chosen.

⇒ The case spells `waiting:worker`, `running` or `running:brief` (ruling 9's selectors), the driver
resolves it against a roster taken at that very stop, and the event says which thread it picked. What the
case then re-asserts is that THAT id has the status and the function the selector asked for, on the
roster from the SAME stop — a statement about one moment rather than about two runs. The preconditions
are made true by the fixture rather than hoped for: `workers` sleeps far longer than a stop-and-continue
takes, so a parked worker is always there to be named, and `finisher` arms only the short-lived thread's
line before `run`, so the first stop is necessarily inside it and `running:brief` cannot miss.

⚠ **THERE IS NO CASE FOR ID STABILITY UNDER A TRUNCATED ROSTER, AND THE REASON IS THE AGENT'S ORDER.**
The page carries 80 records and the driver keeps an identity the listing did not disprove, so an id
survives a thread being omitted. Nothing here can make that omission happen and then undo it: `dbgGtList`
walks the agent's roster in CARVE order and writes the first 80 LIVE records, so a live record's rank
among live ones only ever falls as earlier ones die — once inside the window it never leaves. Present →
absent → present is therefore unreachable, and the only present → absent is a thread that COMPLETED,
which is genuine death and is what `gt-park-of-a-finished-thread-is-refused` already measures. A case
built on a 100-sleeper fixture would go green under the rule it is meant to test AND under the one it
replaced, which is a gate that cannot fail. The rule is in the driver; the channel that could measure it
is a `gtList` with a different window, and it does not exist.

⚠ **A BATCH SCRIPT IS FIXED BEFORE THE SESSION STARTS**, so no case can write `gt-park <id>` for an id
the run produces. Every word that names a thread therefore names it by selector, and
`gt-park-of-a-finished-thread-is-refused` asks with the same selector that resolved while the thread was
alive — the refusal proves the word no longer answers to anything, and the event carries no id at all.

A worker parks inside the `sleep` spliced into it, so the innermost frame of its walk is that spliced
body and the one that owns the frame comes next — `innermostPhysicalFrameFunction` is what a case asks
for.

⛔ **A HANDLE IS AN ADDRESS AND IT IS VALID ONLY AT THE STOP IT WAS READ AT.** A completed green-thread
record is zeroed and carved again for the next spawn, so an id the driver minted at one stop names a
thread that may be gone by the next. `gt-park-of-a-finished-thread-is-refused` is the case: it holds the
id of a thread that goes round its loop ONCE, continues until a second roster no longer lists it — with
the long-lived thread still on that roster, so the reason it went is that it FINISHED and not that the
program did — and then asks for it and gets `no-such-thread`.

⚠ **THE `trap` STOP REASON HAS NO CASE HERE, AND CANNOT HAVE ONE.** It reports an `int3` the agent does
not own — no breakpoint, no retired entry, no out-of-line slot, not the pause trampoline. Nothing in the
language plants one: `OpcodeInt3` appears only as inter-function PADDING and inside two x64-linux runtime
chunks, no `__Raw` row and no `__Builtins` spelling emits one, and the driver cannot write the debuggee's
memory. A fixture could therefore only pretend, so the reason is stated here instead and what measures the
agent's half is `specs/debug-agent.md`.

### `maxon monitor`

**`monitor-sched-events`** — a `--debugstream` build of a program that spawns one green thread and awaits
it, run under `monitor --filter=sched`. The program's answer is asserted before any event, because a
monitor that ran nothing prints no events either; then a `sched_spawn` and a `sched_await` line must
appear. Presence only: how many yields and resumes one await costs moves with the scheduler.

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

## `execute/` — compile a program and run it, and do not compile it again

Thirteen cases in twelve files over one subject: `maxon execute <file|directory> [args...]` compiles a program (or reuses a
cached build of it) and runs it, forwarding stdin, stdout, stderr and the exit code. The shared half —
the driver stem, the staging, the per-case cache root, the two spawners and the slot readers — lives in
`ExecuteHarness.maxon`; see the note under `debug/`.

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

⚠ **`argv`'s ARGUMENTS ARE DELIBERATELY WORDS THE DRIVER KNOWS** — `--filter=x`, `--output=x`, `build`,
`test`. A parser that did not stop at the program would select `build` as the command, or consume a flag
and hand the program a short or reordered tail; bland arguments would be green either way.

⛔ **WHAT `concurrent` CANNOT SEE: THE CREATION OF THE SLOT DIRECTORY.** `stageCase` clears a case's cache
of FILES and leaves its directories standing, so a case's slot directory survives from its previous run
and `Directory.create` short-circuits on it. Simultaneous
children racing to create one is therefore only reachable against a cache root that has never held this
program's slot: `rm -rf` the root and launch several `maxon execute` of one script by hand. MEASURED that way,
six children reddened it about one attempt in three, with `could not create <slot>` on the loser's stderr —
which is why `slotDirectory` asks whether the directory is THERE rather than whether this run made it.

It applies rule 1's `.fixture` half only (no `dot-` names) and rule 4 (every child runs in its staging
directory under `temp/execute/`), and it keeps rule 5: one spawning `test`, one file. `corpus.test.maxon` is
the exception and spawns nothing — it holds the two guards that are about the corpus rather than about the
driver: every fixture directory is one the harness's roster names and every name in that roster is a
directory, and no ordinary `.maxon` sits beside the case files nor any live one under `fixtures/`.

## `build-manifest/` — what a `project.maxon` says about the build, honoured as the command line's flags are

One subject: `maxon build` with NO path runs the staged project's `project.maxon` and builds what it
describes. A manifest is a second way to say what a flag says, so the build it describes is held to the
same rules as a path build — the sidecar the manifest turned off is not written, `--coverage` without the
sidecar is refused whichever of the two turned it off, a busy checkout refuses it, and a description field
the driver cannot use is refused rather than defaulted. The shared half lives in
`BuildManifestHarness.maxon`; see the note under `debug/`.

⛔ **THE REFUSAL CASES ASSERT THE ABSENCE OF AN EXECUTABLE BESIDE THE EXIT CODE.** An instrumented binary
with no coverage-point table is the artifact the refusal exists to prevent.

It applies rule 1's `.fixture` half only (no `dot-` names) and rule 4 (every child runs in its staging
directory under `temp/build-manifest/`, which is also what makes the build path-less), and it keeps rule 5:
one spawning `test`, one file. It runs at the default deadline.

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
its own `--output=`, because the entry stub under test is that target's. A case taking the host's default would
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

## `emitted-runtime/` — the ORDERING an emitted runtime body reads another thread's word with

Three cases, and each one's subject is a body no author wrote: one the back end synthesizes into every
program that spawns, two into every program that allocates, and fifteen scheduler, timer and poller
bodies for the `.data` words published from under `__sched_lock`. All three build the same two-line
`spawn` fixture for `arm64-macos` and for `arm64-linux` with `--emit-ir-runtime=`, cut
`func @<function>` out of the printed Target IR, and ask how that body reads a word another thread
published — with `ldar` or with a plain `ldr` — and how it writes one, with `stlr` or a plain `str`.
The shared half — the staging, the spawn, the cut, the line scan and the plain-access reading — lives
in `EmittedRuntimeHarness.maxon`; see the note under `debug/`. Every demand a case makes is one of
three scans of the cut body, and no case walks it itself: `firstLineIndex` where an ORDER is
demanded and `bodyLineCount` where a TALLY is, both selecting a line by the op it starts with, an
operand it contains and an operand it ends with; and `registerEvents`, the one scan that FOLLOWS a
register, which reads off each line both what it does through that register and whether it overwrites
it — two facts rather than one, because `ldr x1, [x1 + 48]` is both. `firstWriteOfRegister` and
`registerHoldingArgument` are readings of that one scan rather than scans of their own.

**`steal-reads-the-victim-ring-with-acquire-loads`** — `__sched_steal`, the green-thread scheduler's
thief, and the victim's `runqHead`, `runqTail` and `runnext`.

**`locked-relist-doors-recheck-the-owner-with-an-acquire-load`** — `__slab_free_to_full` and
`__slab_free_to_raw`, the two doors that finish a slot free under `__slab_lock` (an exhausted span
relisted, and a span of the shared raw row), and the span's `owning_p` word at mspan+48. The evicting processor publishes that word with a release
store while it pops the span dry; these routines read it from another OS thread, under `__slab_lock`,
which does not serialise that pop. So the read must be an acquire, and the case demands two things of
EACH door on BOTH lanes: no plain load addresses `[<span> + 48]`, and an `ldar` stands ahead of the
body's FIRST `bl __slab_…` line — the acquire must precede every slab call, not merely appear
somewhere in the body. ⛔ **ITS SELF-PROOF IS THAT IT ADDRESSED SOMETHING**: the body must hold
`bl __slab_free_to_span`, and the register the case attributes the owner word to must be the one that
call takes its span from. Two emitted shapes satisfy that and nothing else does: the body moves the
span argument into a callee-saved register (`movRegReg <reg>, x1`) and moves it back
(`movRegReg x1, <reg>`) ahead of the `bl`, which is `__slab_free_to_full`; or it never moves the span
at all and the call inherits `x1`, which is `__slab_free_to_raw` — and then the proof is that NO line
ahead of the `bl` writes `x1`, read off each line's destination operand rather than off a roster of
the ops that write. Failing both panics rather than passing, because a body that does none of this
work satisfies an absence demand by holding nothing, and a register the span is not addressed through
satisfies it the same way.

**`published-scheduler-words-are-stored-with-release-and-loaded-with-acquire`** — the five `.data` words
classed `MultiMSharing.publishedFromTheLock` (`__sched_phase`, `__sched_lastpoll`, `__sched_poll_until`,
`__gt_timer_when`, `__np_waiters`), over the fifteen emitted bodies that write or read one. Each word is
written under `__sched_lock` and read on an OS thread that holds no lock in common with the writer, so
the lock orders nothing for that reader and the edge has to travel on the accesses themselves: `stlr` at
every store, `ldar` at every load, the locked sites included. The case walks each body for the
`leaGlobal … , <label>` lines that materialise a word's address, follows the register the address lands
in to the first thing done through it, and tallies the four shapes it can read. A table of body against
word says which of publishing and observing each body owes. ⛔ **ITS SELF-PROOFS PANIC RATHER THAN
PASS**: a body that never names the word, a materialisation whose destination register cannot be read,
one whose register is overwritten before it reaches anything, and an access matching none of the four
shapes each leave the absence demands true for the reason that they address nothing.

⭐⭐ **THE TARGET IR IS THE ONLY PLACE THE ANSWER IS.** The victim publishes its tail with a
read-modify-write and claims a head with a compare-and-swap, so the WRITER's half of the pair is
ordered; the reader's half is one instruction selection, and a program cannot see which instruction
carried its own load. Nor can an exit code: a thief that reads a stale tail copies a slot the victim
has not written yet and runs whatever word was there, which on a wrapped ring is a green thread that
is already running — a corruption whose symptom is a crash somewhere else entirely, and only
sometimes.

⭐ **BOTH ARM64 LANES ARE BUILT, AND x64 IS NOT.** TSO never reorders load with load, so the same
Std op lowers to the plain `mov` there and an x64 build would assert nothing. The two arm64 targets
share one lowering (`StdToArm64Conversion`), and building both is what keeps a cure that reaches only
one of them from reading as a cure.

⛔ **THE VICTIM'S REGISTER IS READ OUT OF THE BODY, NEVER ASSUMED.** The case takes the register the
prologue moves the second argument into, then REQUIRES the body to claim a head through it
(`arm64AtomicCas … [<victim>]`) before any load is attributed to a field. A register that is not the
victim's would make every absence demand below it true by holding nothing, so failing to attribute it
panics rather than passes.

⛔ **THE COUNT IS ASSERTED BESIDE THE THREE ABSENCES.** A body with no plain load of a ring word and
no acquire load either is a body whose shape has moved; requiring three `ldar.word64` alongside is
what makes the absences a reading rather than a search that found nothing.

⚠ **THE FIELD OFFSETS ARE EACH CASE'S OWN CONSTANTS** (`P+0`, `P+8`, `P+88` in the steal case,
`mspan+48` in the relist one), because a test program cannot import the compiler's `SchedRuntime` and
`SlabRuntime` constants. They are the one thing here that can rot silently in the absence half — which
is the other reason each case demands a presence beside its absences: the acquire COUNT in the steal
case, and an acquire ahead of the first slab call in the other.

⚠ **NOTHING IN `ci.yml` RUNS THIS CORPUS**: that workflow runs `spec-test`, `tests/lsp`, `tests/fmt`,
`tests/spec-harness` and `tests/ladders` only, so this gate is one a `/land` battery or a contributor
runs by name —
`maxon test tests/emitted-runtime`.

It applies rule 1's `.fixture` half only (no `dot-` names) and rule 4 (every child runs in a staging
directory under `temp/emitted-runtime/`, named for the CASE, because `maxon test` runs files
concurrently and one fixture staged twice would be two runs over one tree), and it keeps rule 5 at the
budget `parallel-compile/` prices: one spawning `test` per file, then one compile per BODY per lane for
the two cases that ask for one body at a time — two for the steal case, four for the relist doors —
against one compile per LANE for the published words, whose fifteen bodies are named in a single
comma-separated `--emit-ir-runtime=`. That is the thinnest margin in this corpus: the widest case
MEASURED at 2.7 s of the 5,000 ms deadline on a Windows host, so a body added to a per-body case is a
`--timeout=` away from needing one.

⚠ **A COMMA-SEPARATED `--emit-ir-runtime=` COSTS ONE COMPILE AND TAKES ITS WHOLE LIST DOWN WITH ANY ONE
NAME.** A name this program contains nothing of PANICS the build
(`TargetPrinter.requireEveryNameRendered`), so a list reports nothing about any of the bodies on it —
which is exactly the state a door being ADDED is in while it is being added. A case naming a settled set
of bodies takes the single compile; one being extended asks for its bodies one at a time.

## `examples/` — every program in `examples/`, and every complete program the docs show, still builds and still computes its known answer

One case per program: build it with the compiler under test and, where its answer is known, run it and
compare. The shared half — the compiler stem, the build, the run and the answer check — lives in
`ExamplesHarness.maxon`; see the note under `debug/`.

A complete program a document shows a reader — one with a `main`: the README and homepage heroes, the
docs' introduction and first-program walk-through — is gated the same way, because nothing else compiles
it and a visitor copies it as written. Fragments with no `main` are not. `runDocumentProgram` reads the
document, takes the ONE program block containing the case's marker (a ```` ```maxon ```` fence in
Markdown; a `` const <name> = `…`; `` template literal in an Astro page, its `\\` escapes undone), and builds
and runs it. ⛔ A marker matching no block, or more than one, PANICS naming the document and the marker,
so an edit to a document cannot quietly drop its program out of the gate.

⭐⭐ **THESE EXPECTATIONS ARE HAND-WRITTEN, AND THAT INVERTS RULE 6 ON PURPOSE.** An answer generated by
running this compiler would agree with whatever the compiler does; the point here is an answer that
does not come from it. Every expected value is EXTERNAL — the Benchmarks Game's published reference
output, or the behaviour the example's own source documents — and each case cites its source in a
comment beside the value.

⚠ **THE BENCHMARKS RUN AT THE PUBLISHED SIZE, NEVER THEIR DEFAULT.** Each benchmark example takes its
size as argv[1] and without one runs the benchmark's full size (fannkuch-redux n=11; nbody 50,000,000
steps; spectral-norm n=5500), which does not fit a file's 5,000 ms deadline. Each case passes the size
the Benchmarks Game's reference output was produced at, and expects that output byte for byte.

⛔ **THE EXAMPLES ARE BUILT WHERE THEY SIT, NEVER COPIED** — the gate is about the files a reader runs.
Only the output is staged, into `temp/examples/<example>/`; `maxon test` runs files concurrently, and
`--output=` keeps each build out of the tree lock. A document's program has no file of its own, so it is cut
out of the document as it stands and written into `temp/examples/<name>/`, and built there. It keeps rule 5: one `test` per file, and no case compiles
more than once. It runs at the default deadline.

⭐ **AN EXAMPLE THAT READS THE FILESYSTEM IS GIVEN ITS INPUT BY `stageExampleFile(example, relativePath:,
content:)`**, which writes one file under that example's staging directory, creating the directories the
relative path names, and answers with the path it wrote. That directory is the working directory the run
gets, so the case hands the program a RELATIVE path and reads a relative one back. `requireExampleBuild`
clears what a previous run staged, so staging follows the build.
⛔ **IT IS ALSO HOW A FIXTURE A CHECKOUT CANNOT CARRY IS BUILT** — rule 1 forbids a stored `.git`, and
`maxgrep.test.maxon` needs a real one, plus a file holding a NUL byte.
⚠ **A PATH THE PROGRAM PRINTS WEARS THE HOST'S SEPARATOR**; the harness folds `\r` and nothing else, so an
expectation containing one is built from `FilePath.separator()`.

## `warm-rebuild/` — a compile that reuses memos emits what a cold one emits, and reuses what it should

Two cases, each over `maxon verify-warm-rebuild <dir>`: the driver compiles one project cold, edits the last file
the author wrote by bytes alone, compiles the same project warm, and compares what that emits with a cold
compile of the edited sources. A bytes-only edit rebuilds the signature index and leaves every other file's
parse reused, so whatever a reused artifact says about the index has to mean the same thing in the rebuilt one.

⭐ **THE FIXTURE IS THE CASE.** `nested/` has two files whose parses each mint an instance no declaration names —
a holder's cell read through a concrete holder — and the edited file is LAST in fold order, so its re-parse runs
after the reused artifact, which is where an instance id could be claimed twice.

`filed/` (`filed-type-reuse.test.maxon`) has a generic type whose only instance is an argument-inferred call, so
the front end files it and settles the index again. The filed type is part of the key every parse memo reads, so
the case asserts the driver's invalidation property: the edit re-parses the edited file and no other.

⚠ **IT EXCEEDS THE 5,000 ms PER-FILE DEADLINE**: the driver compiles the program several times. Run the corpus
with `--timeout=60000`. It applies rule 1's `.fixture` half only and rule 4 (the child runs in a staging
directory under `temp/warm-rebuild/`).

## `mcp/` — JSON-RPC MCP server for end users and compiler contributors

The Model Context Protocol (MCP) server runs over standard I/O using newline-delimited JSON-RPC 2.0.
`McpHarness.maxon` provides the shared harness for spawning the compiler as an MCP server, exchanging
JSON-RPC messages, and inspecting structured responses.

- `standard.test.maxon` gates the default end-user mode (`maxon mcp-server`): the handshake, the 8 tools
  (`build`, `run`, `test`, `fmt`, `check`, `dump_ir`, `lookup_error_code`, `info`), their schemas, and the
  refusals — an argument no tool declares, an argument of the wrong JSON type, a contributor argument in
  user mode, and an error code no registry case claims.
- `dev.test.maxon` gates contributor mode (`maxon mcp-server --dev`): the 11 tools, the `repoRoot` and
  `from` arguments, checkout validation, and the `repoRoot` ECHO on both an answer and a refusal.
- `scale-defaults-agree-with-help.test.maxon` reads the default each `run_scale_test` property states and the
  default `maxon help scale-test` states for the same option, and holds them equal. One server and one `help` run.
- `rebuild.test.maxon` gates the half of a self-rebuild that a live server depends on: its image is
  renamed out from under it and another is written in its place, and it keeps answering.
- `rebuild-over-a-running-previous.test.maxon` gates the other half: a compiler building over its own
  image while a server runs the `.previous` that image would replace. On Windows a running image cannot
  be deleted, so the build must move the held one aside and still succeed, and the next self-rebuild,
  with nothing held, must leave no moved-aside image behind. A program source it builds is written into
  `temp/` from a string, so no live `.maxon` sits under `tests/`.
- `rebuild-with-a-compile-error.test.maxon` gates the failure half: a self-rebuild whose program does
  not compile leaves the running image in the slot, byte for byte, and moves nothing to `.previous` —
  and that image then builds a good program over itself.
- `check-reports-a-type-error-and-writes-nothing.test.maxon` stages a type error and a well-formed program
  under `temp/`: the first is answered as an error carrying `E3005`, the second as a success, neither
  answer is the warm-rebuild gate's run (which also compiles without writing, so files alone cannot tell
  them apart), and nothing but the source is left in either directory.
- `dump-ir-answers-the-ir-text.test.maxon` stages a well-formed program: the answer carries `func @main`,
  and nothing but the source is left beside it.
- `reference-documents-every-tool.test.maxon` reads the `--dev` `tools/list` roster and holds
  `docs/CLI_REFERENCE.md`'s `## MCP Server` section (its heading line up to the next `## ` line) to naming
  every tool and every argument as a whole word. A document with no such section fails naming the whole
  roster, tool by tool.

⛔ **EVERY REBUILD CASE STAGES ITS OWN COPY OF THE COMPILER AND REPLACES THAT.** Driving a real
`build maxon-bin` would rebuild the tree's compiler as a side effect of running the corpus. Testing
on a copy costs a file write rather than 25 s, and the whole corpus therefore runs at the default deadline: `maxon test tests/mcp`.


## `docs/` — the stdlib reference names every public declaration

One case, and it spawns nothing: it reads every top-level `stdlib/*.maxon` and `docs/STDLIB_REFERENCE.md`, and
fails listing, file by file, each `public` declaration name the document does not contain as a whole word.
`stdlib/Builtins.maxon` is excluded with its reason in the case, and `stdlib/helpers/` is not descended
into; a name in the `__` band, and everything declared inside one, is compiler machinery and is left out.

⛔ **A `public` LINE WHOSE NAME THE SCANNER CANNOT READ PANICS**, naming its file and line. A shape it
skipped would drop out of the roster and read exactly like a documented name.

It is the one corpus whose case reads the checkout rather than a fixture or a spawned driver, so it has no
shared half and no staging. Run it as `maxon test tests/docs`.
