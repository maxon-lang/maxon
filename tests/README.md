# `tests/` — fixture corpora for the driver commands

A `specs-shv2` case is a Maxon **program** the harness compiles and runs, so it can
reach `stdlib/` and nothing else. **A DRIVER COMMAND is not that** (user ruling,
2026-09-02): `fmt`, `test`, `build` and friends are gated by spawning the compiler at
a fixture project and asserting what it reports. This directory is where those
fixtures live.

Eight corpora live here, one directory each, every path into one spelled from the CHECKOUT
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
| `lsp-fixtures/` | `lsp-selftest` | `DeclFixtureRelativePath` |
| `ladders/` | `spec-test` → `requireLadderIndexComplete` | `LaddersRelativeDir` |

⚠ **`ladders/` is cited from outside the code that reads it.** Roughly twenty
measurement-provenance comments across the compiler, `maxon-sharp/` and
`specs-shv2/register-spill.md` name a generator by path, and `docs/optimization-log.md`
names the former path in every row minted before 2026-09-02 — a dated record, so those
rows stay as written.

⚠ **The six rules below are the `fmt/` corpus's**, and each is written against the
command `fmt` is. They are not automatically true of the other seven: `test-fixtures/`
deliberately stores LIVE `*.test.maxon` sources, because the command under test compiles
them, and `lsp-fixtures/` carries a `.maxonignore` that rule 1 forbids here.

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
  lsp-fixtures/type-declaration/ decl.maxon, whose LINE NUMBERS are the assertion
  ladders/                       hand-built scaling generators + the README indexing them
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
