# Maxon CLI Reference

Everything the `maxon` compiler driver accepts: the commands, their options, and the project layout
they read.

`maxon help` prints the same command and option list from the driver itself, and `maxon help <command>`
one command's part of it; `maxon` with no arguments prints the version and the command list alone. Where
this document and that listing disagree, the listing is the compiler and this is a copy.

---

## Quick Reference

Alphabetical, which is the order the driver itself prints — see [`maxon help`](#maxon-help) for why that
one and not another. The first row is the exception: it is no command word at all, so it sits ahead of
the roster rather than inside it.

| Command | Description |
|---------|-------------|
| `maxon <file>.maxon [args...]` | Run a Maxon file as a script — [`maxon run`](#maxon-run) with the word left out |
| `maxon build <file\|directory>...` | Compile a Maxon program to an executable |
| `maxon coverage <run\|report> <exe>` | Run a `--coverage` binary and report line + branch coverage |
| `maxon debug --dump-info <exe>` | Print the `.mxdbg` debug-info sidecar beside a binary |
| `maxon debug --symbolize <exe> <off...>` | Resolve `.text` code offsets to `file:line:col` |
| `maxon fmt [file\|directory]` | Re-print `.maxon` sources in canonical layout, in place |
| `maxon help [<command>]` | Print the command and option reference, whole or for one command |
| `maxon lsp-server` | Speak the Language Server Protocol over stdio |
| `maxon monitor [--filter=…] <exe> [args...]` | Run a `--debugstream` binary and print its trace events |
| `maxon profile run <exe>` | Sample a running program and report where its CPU time went |
| `maxon run <file\|directory> [args...]` | Compile a program, or reuse a cached build of it, and run it |
| `maxon test [directory]` | Run a project's own `test` declarations |
| `maxon upgrade [--dry-run]` | Update this compiler's install to the newest release |
| `maxon version` | Print the version, the commit it was built from, and the host target |

### For working on the compiler itself

These four are about THIS COMPILER rather than about any program it builds, so `maxon` with no arguments
does not list them. They are typed, parsed and run exactly like any other command, and `maxon help` and
`maxon help <command>` document them.

| Command | Description |
|---------|-------------|
| `maxon scale-test` | Compile a doubling ladder and report per-phase memory and CPU |
| `maxon spec-test [directory]` | Run the COMPILER's own spec suite |
| `maxon verify-recheck <file\|dir>` | Assert one project survives being re-checked |
| `maxon verify-warm-rebuild <file>` | Assert the query spine is deterministic and incremental |

Only the **first** command word on a line is the command. A later one is an ordinary positional
argument, so `maxon fmt fmt` formats the directory `fmt/` rather than selecting `fmt` twice.

---

## Commands

### `maxon run`

Compiles a program — or reuses a cached build of it — and runs it.

**Usage:**
```bash
maxon run <file|directory> [args...]
maxon <file>.maxon [args...]
```

**Two doors, one command.** The word, and a **first** argument ending in `.maxon` with no word at all
— which is what a kernel hands the interpreter for a file whose first line is `#!/usr/bin/env maxon`.
No command word ends in that extension, so the wordless door cannot swallow one, and both doors reach
the same implementation.

**Everything after the path is the PROGRAM's command line** and reaches it verbatim, including every
token this driver implements for some other command: `--filter=x`, `-o`, `build` and `test` are the
program's arguments here. **`run` therefore takes no options of its own** — a flag it consumed would
be a flag no program run this way could ever be given.

The program inherits **stdin, stdout, stderr and the working directory**, and **its exit code becomes
this command's**, so a program launched this way is indistinguishable from one launched by name. The
build says nothing at all: a cached run writes zero bytes of its own to either stream, because a
compiler announcing what it just wrote onto a program's own stdout is output nothing downstream can
filter back out. ⚠ The program's `argv[0]` is the cached executable, not the path that was typed.

**Host target only.** There is no `--target`: a program built for another target is one this machine
cannot run, and building one is what [`maxon build --target=`](#maxon-build) is for.

**The cache.** Each program's build gets a directory of its own, and is reused until something it was
built from changes:

```
<root>/maxon/run/<sha256 of the program's absolute path>/
├── hello-<key>.exe         # the build, named after the key it was built under
└── hello-<key>.exe.mxdbg   # its debug-info sidecar, always written — a script is code being developed
```

⭐ **The key IS the filename, and nothing is recorded beside the build or compared against it.** A hit
is that file existing. A build named after the inputs it was made from cannot vouch for any others, so
two runs that compute different keys write different files and cannot contend, and two that compute the
same key are producing byte-identical output — whichever of them lands is correct. A fresh build is
written under a `.tmp` name of its own and renamed into place, so simultaneous cold runs of one program
never share an output path; a rename that fails onto a name already there has been beaten to it by a run
that built the very same bytes, and is a hit. Superseded builds are removed as each new one is published,
best-effort and silently — one another process is executing cannot be deleted on Windows, and the run
doing the removing has already built what it was asked for.

`<root>` is **`MAXON_RUN_CACHE_ROOT`** when that is set, then `LOCALAPPDATA` and `TEMP` on Windows, and
`TMPDIR` then `/tmp` elsewhere. Windows has no directory every process may write to unasked, so a host
setting neither of its two is **refused by name** rather than sent to an invented path.

⚠ **The path is resolved against the working directory but not canonicalized**, so one spelling run
from two directories reaches one slot, while `x.maxon`, `./x.maxon` and `a/../x.maxon` are three. That
is the safe direction — an extra compile, never a cached binary served for a different program — and a
canonicalizer written here would be a second answer to a question the filesystem owns.

⭐ **The key is a CONTENT hash, never a modification time.** It covers every source file of the program
and every `stdlib/` source the build compiles, each hashed by its bytes, plus this compiler's own
identity — its path, size, modification time and target. (The compiler is identified rather than
hashed: reading tens of megabytes of binary ahead of every cached run is the cost this cache exists to
avoid, and it is the same identity test `spec-test` decides a compiler is current by.) A modification
time is whole **seconds** on every target this compiler emits, so an edit landing in the same second as
the build it invalidates would TIE — and a tie resolved either way is wrong: reuse runs stale code,
rebuild recompiles on every run inside that second. A hash has no tie. A rebuild happens when anything
in that set changes, and only then.

Measured on one Windows machine for a hello-world script: ~450 ms cold, ~170 ms warm, against ~90 ms
for launching the built executable directly. The warm gap over that floor is the key — hashing the
program and the standard library.

**The shebang line.** A source file whose **first two bytes** are `#!` has that first line ignored by
the lexer, which is what lets a Maxon program be an executable script:

```maxon
#!/usr/bin/env maxon

function main() returns ExitCode
	print("Hello, world!\n")
	return 0
end 'main'
```

```bash
chmod +x hello.maxon
./hello.maxon
```

⛔ **At byte 0 and nowhere else.** `#` opens a compiler directive (`#if` / `#else` / `#endif`)
everywhere else in a file, so a `#!` anywhere but the very start of one is still
**E1009: Unknown compiler directive**. The line's newline is **not** consumed, so every line beneath it
keeps the number it has in the file and a diagnostic on line 4 says line 4. `maxon fmt` preserves the
line verbatim.

⚠ **Windows has no shebang mechanism** — its kernel does not read the first line of a file it is asked
to execute. Git Bash and WSL honour it; `maxon hello.maxon` is the spelling that works everywhere.

**Exit codes:**

| Code | Meaning |
|------|---------|
| the program's | Forwarded verbatim, whatever it is — including the raw status of a child that terminated abnormally (`3221225725` on Windows for a stack overflow) |
| `1` | This command could not act: no program named, no such file, a compile error (the diagnostics are printed and the program is **not** run), no writable cache root, or a finished build that could not be published into one |

A missing file is reported as `error: file not found: <path as typed>`, the same sentence
[`maxon build`](#maxon-build) prints for the same path.

**Examples:**
```bash
maxon run hello.maxon                 # compile if needed, then run
maxon run myproject --verbose         # a directory as one project; `--verbose` is the program's
maxon hello.maxon a b c               # the wordless door, with three arguments
MAXON_RUN_CACHE_ROOT=/build/cache maxon run hello.maxon
```

---

### `maxon build`

Compiles Maxon source to a standalone executable.

**Usage:**
```bash
maxon build <file|directory>... [options]
```

**Arguments:**
- `<file|directory>...` — one or more paths. **Several paths are compiled as ONE program, in the order
  given**, and that order is load-bearing: sources are registered in exactly the order named. A
  directory contributes every `.maxon` file beneath it, except `build.maxon` and except the subtrees a
  `.maxonignore` excludes. A path named explicitly is compiled whatever a `.maxonignore` above it says
  — the marker governs the *walk*, never an explicit name.

Naming no path runs the **build manifest** in the current directory — see
[The build manifest](#the-build-manifest) below. A directory with no `build.maxon` prints the usage
line and exits 1.

**Options:**

| Option | Description |
|--------|-------------|
| `-o <path>`, `--output=<path>` | Output executable path. Without it the name is derived: a single-file build writes beside the source (`foo.maxon` → `foo.exe`); every other shape takes the first registered source file's base name. An override that already carries the target's executable extension is used as it is. |
| `--target=<cpu>-<os>` | Compile for this target instead of the host. See [Targets](#targets). |
| `--emit-ir` | Also write the lowered Target IR beside the executable, as `<output>.ir`. It renders the functions parsed from the program's own source. |
| `--emit-ir-runtime=<a>,<b>` | Also render these compiler-emitted or `stdlib/` functions in that IR (implies `--emit-ir`). A value naming no function is refused. |
| `--no-debug-info` | Do NOT write the `<output>.mxdbg` debug-info sidecar. It is written **by default**, and the executable is byte-identical either way — the sidecar never decides which instructions are emitted, so the binary you debug is the binary you shipped. |
| `--coverage` | Instrument for code coverage: the binary counts each statement and branch arm it executes and writes them to `<output>.mxcov` as it exits. It **changes the emitted code**, so it is a separate build from the one you ship; read it back with `maxon coverage`. It requires the debug-info sidecar (which carries what each counter counts), so `--no-debug-info` beside it is refused. |
| `--debugstream` | Emit the shared-memory debug-stream producer, plus the memory-manager events written into it. Use with `maxon monitor`. It also enables the `__DebugStream` builtin: without it, every `__DebugStream` call emits zero instructions. Refused on a target with no shared-memory or uptime-clock facility. |
| `--async-trace` | Emit the green-thread trace (`spawn #`, `io_yield #`, `io_resume #`, `await #`) to stderr. |
| `--define=<name>=<value>` | Replace a top-level `String` constant's written-out default with `<value>`. Repeatable. See [Defines](#defines). |
| `--metrics=<path>` | Write this compile's per-phase time and memory attribution to `<path>` as TSV. `--log=compiler:debug` prints the same numbers as a human-readable table. |

`--debugstream`, `--async-trace` and `--coverage` are opt-in **per build**: with the flag off, not one
instruction of the machinery is emitted — not a branch that is never taken, nothing at all.

#### Defines

`--define=<name>=<value>` replaces the value of a top-level `String` constant whose initializer is a
written-out string literal. It is Go's `-ldflags -X` for Maxon, and it exists for the same thing: a
version, a commit, a build channel — facts known to whoever runs the build and not to the source.

```bash
maxon build myapp --define=Version=1.4.2
maxon build myapp --define=Compiler.CompilerVersion=0.1.1 --define=Build.Channel=nightly
```

⭐ **The declaration keeps a real default, and that is the point.** A constant is ordinary source with an
ordinary value, so a fresh clone builds with no flags at all and reports something honest. `--define`
replaces the default; it does not supply a missing one.

```maxon
let Version = "dev"        // what a plain `maxon build` reports
```

⚠ **The name may be bare or namespace-qualified, and neither is assumed unique.** A namespace is the
module DIRECTORY, so `Compiler.CompilerVersion` is a constant in `<root>/Compiler/`. Two file-private
constants of one name can share a directory, so the compiler counts the matches rather than trusting the
spelling.

⛔ **Three things are refused rather than ignored**, because a define that quietly does nothing produces
a build indistinguishable from the one it was meant to replace:

| | |
|---|---|
| **E3149** | the name matches no declaration |
| **E3150** | the name matches more than one — both are named, with their files |
| **E3151** | the initializer is not a plain string literal (an expression, or a constant that is not a `String`) |

Every problem on one command line is reported before any is fatal.

⚠ **The value may contain `=`; the name may not.** The split is at the first one, so
`--define=Url=https://x/?a=b` sets the whole URL.

A build manifest can supply defines too, which is how a manifest that *computes* something gets it into
the binary — see [The build manifest](#the-build-manifest). A `--define` on the command line wins over
one the manifest wrote, exactly as `-o` wins over the manifest's `output`.

**Tree lock.** A build that does *not* pass `-o` and whose project root is a **directory** takes a lock
on the checkout it writes into, so two such builds cannot race on `<project>/.maxon/`. Everything else
takes nothing: a `-o` build writes where the caller said, a single-file build writes beside the file it
was handed, and a path with no `stdlib/` anywhere above it is not in a checkout to lock.

**Examples:**
```bash
maxon build hello.maxon                       # → hello.exe
maxon build src/ -o build/app                 # a whole directory, named output
maxon build maxon-bin                         # a target named by build.maxon
maxon build a.maxon b.maxon                   # one program out of two files, in that order
maxon build app.maxon --emit-ir
maxon build app.maxon --target=wasm32-wasi
```

On success it prints `Compiled -> <path>` and exits 0. A compile error prints its diagnostics and
exits 1.

---

### `maxon fmt`

Re-prints `.maxon` sources in canonical layout, **in place**.

**Usage:**
```bash
maxon fmt [<file|directory>]
```

**Arguments:**
- `[file|directory]` — default: the working directory. A named **file** is formatted whatever it is
  called and wherever it sits. A **directory** is walked for `.maxon` files, skipping `build.maxon`,
  skipping anything under a `.maxonignore`, and **pruning any subdirectory that holds a `.git`** (a
  nested clone or a worktree), so a run cannot rewrite files outside the tree you named. The traversal
  root itself is exempt, or `fmt` inside a checkout would format nothing.

⚠ **With no path it formats the whole current directory.** That is the documented default.

**It takes NO options.** Any `-`-leading argument is refused on stderr with exit 1 and nothing written
— including a flag the driver implements for other commands, and wherever on the line it sits. A
swallowed flag would leave `fmt` with no path and send it at the current directory. More than one path
is refused the same way.

**A file it cannot lex is left byte-identical** and counted as `unchanged`, which is the same word it
uses for a file that was already canonical. A file that is already canonical is not rewritten at all,
so a run over a clean tree leaves every mtime alone.

**It is opinionated about BLANK LINES, at every nesting depth.** Exactly one blank line separates
adjacent logical groups within a scope, and none appears within a group. What counts as one group is
the enclosing scope's business:

| Inside | These pack together | These stand alone |
|---|---|---|
| a file, a `type` or `extension` body | `let`/`var` lines; `typealias` lines | every declaration that opens a body |
| an `enum` or `union` body | the cases | a nested declaration |
| an `interface` body | the bodyless signatures | anything else |
| a function or labeled block body | the statements, `let`/`var` included | every nested block |
| a `match` body | the arms | — |
| a multi-line `[`/`{` literal | *(never grouped — your own blanks are kept, capped at one)* | — |

A `///` block fuses to the declaration below it; a whole-line `//` fuses the same way, but only when
you left no blank between the two — a blank there is what makes it a section heading instead. A blank
you put inside one group survives, capped at one, so a deliberate grouping is never flattened. An
enum whose cases carry no comments is therefore left exactly as written.

The run prints one `formatted: <path>` line per rewritten file and a
`fmt: N file(s) changed, M unchanged.` summary. `maxon fmt` is gated byte-for-byte by `tests/fmt/`.

**Examples:**
```bash
maxon fmt              # the current directory
maxon fmt main.maxon   # one file
maxon fmt src/         # one directory
```

---

### `maxon test`

Runs a project's unit tests — every `test` declaration in its `*.test.maxon` files.

All discovered tests are compiled into ONE binary with a generated entry point, and which of them run
is a runtime argument. That is why changing `--filter` between runs does not recompile anything, and
why every re-run the harness needs (isolating a crash, attributing a leak) costs a process rather than
a build.

The project's own `main` needs no change: the test binary is compiled with a generated entry instead,
so `main` becomes unreachable and is dropped. The same directory still builds and runs normally with
`maxon build`.

**Usage:**
```bash
maxon test [directory] [options]
```

**Arguments:**
- `[directory]` — the project to test (default: the working directory). A second positional is refused.

**Options:**

| Option | Description |
|--------|-------------|
| `-t PATTERN`, `-t=PATTERN`, `--filter=PATTERN` | Run only tests whose NAME or FILE path contains PATTERN (case-insensitive). Comma-separated patterns run a union. A bare `-t` with nothing after it is refused rather than treated as "everything". |
| `--list` | Print the tests that would run, and compile nothing |
| `--json` | Emit the report as JSON instead of text |
| `--isolate` | Run every test in its own process, rather than one process per test FILE |
| `--bail[=N]` | Stop claiming new work after N failures (default 1). The shard already running finishes, so the total may exceed N. |
| `--timeout=MS` | Kill a test process after MS milliseconds (default 5000). The deadline is per PROCESS, and one process runs one FILE's tests — so it bounds a file's whole run, not each test, unless `--isolate` is given. |
| `--no-timing` | Omit durations, making stdout byte-reproducible |
| `--color=auto\|always\|never` | Colour; `auto` colours only when stdout is a terminal — not redirected, `NO_COLOR` unset, and `TERM` not `dumb` |
| `--target=<cpu>-<os>` | Build the test binary for that target and then SPAWN it, so a target this host cannot execute reports every test as not run |
| `--log=CATEGORY:LEVEL` | Enable compiler logging (e.g. `codegen:trace`). Without it, `maxon test` silences the compiler's INFO chatter so its stdout is a report rather than a build log. |

**The run is serial**, by design, so there is no `--workers` — and one is not accepted as a no-op
either.

**Working directory.** A test binary runs in the directory `maxon test` itself was invoked from — not
the project directory, and not the staging tree the tests were compiled out of. So a relative path in a
test body means the same thing it means in the shell you typed the command in.

**Where the build lives.** `<project>/.maxon/test/`, with a `.maxonignore` written at its root before
anything is staged, so a generated test source can never be swept into the project's next ordinary
build or into the next run's own discovery.

**On Windows, an emitted program converts per write and changes nothing about the console.** A Maxon
program holds UTF-8 bytes; a Windows console consumes UTF-16 and decodes anything else with its own output
code page — the machine's OEM page unless somebody changed it, which reads an em-dash as three Latin-1
letters. So each standard stream is asked ONCE, on its first write, whether its handle is a console
(`GetConsoleMode`); a console gets the bytes converted and handed over as UTF-16, and a pipe or a
redirected file gets them exactly as they are. `print`, `printError` and the panic path all take the same
road, so a backtrace reads the way a `print` does.

The console's own state is never written, so nothing about it outlives the program: a shell's code page is
whatever it was before, and a sibling process attached to the same console is unaffected. Redirected
output is byte-for-byte the UTF-8 the program produced — a pipe, a file and a captured golden all see the
same bytes as each other and as every other target.

**`--color=auto` degrades to `never` on wasm32-wasi.** Asking the OS what kind of object a handle is
needs a host call: **x64-windows** makes it with `GetFileType`, **arm64-macos** with `isatty`, and both
Linux lanes with the `ioctl(1, TCGETS, ...)` `isatty` is made of — a libc-less static image has no
`isatty` to call, and reads `NO_COLOR` and `TERM` out of the environment vector its entry stub
captured. On **wasm32-wasi** the question is answered "not a terminal", so `auto` prints no colour
there however the report is being viewed. `--color=always` is unaffected and is the way to get colour
on that lane.

The build is never refused for it: the answer degrades rather than the compile failing, because "not a
terminal" is a sound conservative answer where a missing file surface or a missing stdin would leave a
program with no answer at all. Which lanes can ask is recorded in
`TargetFacilities.targetProvidesFacility`'s `terminalDetection` row. wasm32-wasi is not merely
unwritten: a WASI component cannot ask what is on the other end of its `output-stream` at all.

**Outcomes.** A test that does not pass is reported as one of five distinct states, because they are
found by different evidence and call for different action:

| State | Meaning |
|-------|---------|
| `FAIL` | The body threw — a failed assertion, or a foreign error the compiler reported |
| `CRASHED` | The test began and never ended: it took the process down (`panic` is uncatchable) |
| `TIMED OUT` | Still running when its process hit `--timeout`, and was killed |
| `DID NOT RUN` | Selected for a process that died before reaching it, and re-running made no progress. Never reported as a pass. |
| `LEAKED` | Held an allocation at exit (exit code 101), attributed by re-running the test alone |

`--json` reports the same outcomes under its own `state` vocabulary, which a machine reader branches
on. The two spellings are one map in the renderer, so they cannot describe different states:

| `state` | Text label |
|---------|------------|
| `passed` | (a `✓` line) |
| `failed` | `FAIL` |
| `crashed` | `CRASHED` |
| `timedOut` | `TIMED OUT` |
| `didNotRun` | `DID NOT RUN` |
| `leaked` | `LEAKED` |

**Exit codes:**

| Code | Meaning |
|------|---------|
| `0` | Every test passed |
| `1` | A test failed, crashed, timed out, leaked, or did not run — **or no tests were found** |
| `2` | The run could not happen: a bad flag, a compile error, no such project |

A zero-test run exits 1 on purpose: a silently-green suite that stopped containing tests is the failure
this command exists to prevent.

**A worked example — write a test, run it, read the failure.**

Two files in a directory. `pricing.maxon` is ordinary source; the tests go in a sibling whose name ends
`.test.maxon`, which is the only place a `test` declaration is allowed:

```maxon
// pricing/pricing.maxon
export typealias Cents = int(0 to i64.max)

/// What `quantity` items cost at `unitPrice`, with a tenth off from 10 items up.
export function totalCost(unitPrice Cents, quantity Cents) returns Cents
	let gross = unitPrice * quantity
	if quantity < 10 'noDiscount'
		return gross
	end 'noDiscount'

	return gross - gross / 10
end 'totalCost'
```

```maxon
// pricing/pricing.test.maxon
test 'a small order pays full price'
	try Expect.equal(totalCost(250, quantity: 4), expected: 1000)
end 'a small order pays full price'

test 'ten items take the bulk discount'
	try Expect.equal(totalCost(250, quantity: 10), expected: 2500)
end 'ten items take the bulk discount'
```

The `try` is not optional: a `test` implicitly declares `throws TestFailure`, so an assertion without
it is a compile error rather than an assertion whose failure nothing observes. There is no `main` here
and none is needed — `maxon test` compiles a generated entry point instead.

The second expectation is wrong: it forgot the discount. Run it:

```text
$ maxon test pricing
pricing/pricing.test.maxon:
  ✓ a small order pays full price                    0.00ms
  ✗ ten items take the bulk discount                 0.00ms

FAIL  pricing/pricing.test.maxon > ten items take the bulk discount
  FAIL pricing.test.maxon:6: Expect.equal
    expected: 2500
    received: 2250

 1 pass
 1 fail

 2 tests across 1 file.   compile 947ms, run 38ms
```

The `file:line` on the `FAIL` line is the **assertion's own**, not a line inside `Testing.maxon` —
`Expect`'s `file` and `line` parameters default to `__file__` / `__line__`, which expand at the call
site. Correct the expectation to `2250` and the same command exits 0:

```text
$ maxon test pricing
pricing/pricing.test.maxon:
  ✓ a small order pays full price                    0.00ms
  ✓ ten items take the bulk discount                 0.00ms

 2 pass
 0 fail

 2 tests across 1 file.   compile 966ms, run 138ms
```

The second run still says `compile` rather than `cached` because the source changed. Editing only the
`--filter` between runs recompiles nothing: every discovered test is built into the binary and which
ones run is an argument.

**Examples:**
```bash
maxon test                        # every test in the current directory
maxon test src/parser             # one project's tests
maxon test -t json                # only tests whose name or file mentions "json"
maxon test --filter=parser,lexer  # two patterns, as a union
maxon test --list                 # what would run, without compiling
maxon test --json --no-timing     # machine-readable, and reproducible byte for byte
```

---

### `maxon spec-test`

Runs the spec tests — the COMPILER's own suite, under `specs/`. For a project's unit tests see
[`maxon test`](#maxon-test).

**Usage:**
```bash
maxon spec-test [directory] [options]
```

**Arguments:**
- `[directory]` — the spec directory (default: `specs`, resolved against the working directory). A
  second positional is refused.

**Options:**

| Option | Description |
|--------|-------------|
| `--filter=PATTERN` | Run only tests whose `<spec>/<test>` label **contains** PATTERN. One substring, matched case-sensitively — not a list. A bare spec name selects that whole spec, a bare test name selects that test wherever it lives, and `spec/test` selects exactly one. |
| `--workers=N` | Run the suite on N persistent worker subprocesses (default: this machine's CPU count). `1` is the same pool with one worker in it, NOT a serial path — every worker count must print byte-identical output, and that invariance is the pool's sharpest gate. |
| `--target=<cpu>-<os>` | Cross-compile each selected test for that target and run it under the vendored runtime |
| `--network` | Also run the cases that open a socket to a REAL EXTERNAL HOST. They are out of a default run because their verdict measures somebody else's uptime rather than this compiler; a default run NAMES every one it left out. |
| `--update-required` | Rewrite the committed `.test` goldens instead of checking the emitted IR against them. **Review the resulting diff**, and pair it with `--filter`: an unfiltered run rewrites EVERY golden in the suite. |

Running a test proves its allocation is *correct*; only the pinned IR proves the code did not get
*worse*, which is why regenerating a golden is a deliberate act and the diff is the review.

The run refuses to start — exit 2, nothing measured — if the compiler binary is older than the sources
it was built from, or if another command already holds this checkout's tree lock. A stale binary
reports a verdict about code that is not here.

**Examples:**
```bash
maxon spec-test
maxon spec-test --filter=arrays
maxon spec-test --filter=arrays/a-pushed-element-survives-the-push
maxon spec-test --workers=1
maxon spec-test --target=wasm32-wasi
maxon spec-test --filter=strings --update-required
```

---

### `maxon scale-test`

Compiles a ladder of generated programs, each rung double the last, and reports the **per-phase
memory** — allocations, frees and bytes — plus the CPU **ticks** the compiling thread spent.

It does NOT collect wall time: wall time counts every other process on the box, so a dated table of it
would compare a loaded machine in July against an idle one in August. The ladder doubles, so the ratio
between rungs IS the growth (×2.00 linear, ×4.00 quadratic) and nothing is fitted.

⚠ **An INSTRUMENT, not a gate.** It renders no verdict and exits 0 whatever the numbers say; a non-zero
exit means the RUN broke. The trend it feeds is `docs/optimization-log.md`.

**Usage:**
```bash
maxon scale-test [options]
```

**Options:**

| Option | Description |
|--------|-------------|
| `--rungs=N` | Climb N rungs (default 6, range 1–8). Each rung DOUBLES the program, so this is exponential in cost. |
| `--repeat=N` | Compile every rung N times (default 1, range 1–25) and report the LEAST CPU ticks any of them spent in each phase — the least-disturbed sample, since noise can only ADD to a CPU reading and a mean would be dragged by one stall. The MEMORY columns take nothing from a repeat: they are exact, so they come from the first compile and are cross-checked for equality against the rest. A memory number that differs between two compiles of one rung is a broken run. |
| `--note=TEXT` | Record this run in `docs/optimization-log.md` — one dated row of the per-rung allocations and bytes, with TEXT as the reason. Without it a run reports and records nothing; **with** it the row is written, moved or not, because it is an instruction. The instrument can see exactly WHAT moved and can never see why. |
| `--emit-corpus=DIR` | Dump the generated corpus to DIR for inspection (it is otherwise built into a temp directory and thrown away) |
| `--result-json=PATH` | Write the whole run — per-rung and per-phase memory, and the deltas from the last recorded run — to PATH as JSON, so a tool never has to scrape the human report |

A bad value for `--rungs`, `--repeat` or `--workers` is refused as a bad option, never clamped to the
default: quietly running a different ladder would answer a question the caller never asked.

---

### `maxon lsp-server`

Speaks the Language Server Protocol over stdio: JSON-RPC bodies under `Content-Length` framing, the
`initialize` / `shutdown` / `exit` lifecycle, and full text-document synchronization. Normally launched
by the VS Code extension rather than by hand.

---

### `maxon monitor`

Launches an executable with the shared-memory debug-stream monitor attached. Reads the binary trace
events a `--debugstream` build writes and prints them to the terminal, each prefixed with
`[+SSSS.mmm]`. The child's own stdout and stderr are forwarded unchanged, so trace lines are told apart
by that prefix.

**Usage:**
```bash
maxon monitor [--filter=mm|sched|log] <exe> [args...]
```

Everything after the executable is the **child's** command line and reaches it verbatim — the driver
stops parsing at the word `monitor`, so a target argument spelled like one of these flags is not
consumed on the way past.

**Options:**

| Option | Description |
|--------|-------------|
| `--filter=mm` | Memory-manager events only (`mm_alloc` / `mm_free` / `mm_incref` / …) |
| `--filter=sched` | Scheduler and green-thread events only |
| `--filter=log` | Only the events the program itself emitted via the `__DebugStream` builtin |

Given no `--filter`, every family is printed. An unrecognized value is refused by name.

**Exit codes:** the child's own status, except 1 for a command line this could not act on (no
executable, no such file, an unknown filter, no ring) and 3 for a trace schema this build cannot decode.

**Examples:**
```bash
maxon build app.maxon --debugstream
maxon monitor app.exe
maxon monitor --filter=log app.exe
```

**The `__DebugStream` builtin.** Every event family above except `log` is emitted by the runtime.
`__DebugStream` is what lets *user Maxon source* put an event into the same ring — which is how a
compiler written in Maxon stays debuggable once its work is spread over several workers and one stderr
stops being readable.

| Call | Event | Notes |
|------|-------|-------|
| `__DebugStream.enabled()` | — | `true` when the ring is attached. Lets a caller skip building a message nothing would read. |
| `__DebugStream.nameId("phase")` | — | Interns a name **at compile time** into the executable's `MXDS_STRS` blob and yields its `u16`. The name never exists at runtime; the monitor prints it anyway. **The argument must be a string literal.** |
| `__DebugStream.phaseBegin(nameId, unitId)` | `LOG_PHASE_BEGIN` | Opens a nested, per-worker, per-unit span. |
| `__DebugStream.phaseEnd(nameId, unitId)` | `LOG_PHASE_END` | Closes it. |
| `__DebugStream.event(nameId, cat, lvl, unitId, arg0, arg1)` | `LOG_EVENT` | **Structured, zero-alloc.** An interned name plus two numbers — safe on a hot path, where a formatted message would allocate into the very `mm` stream a trace is being read to investigate. |
| `__DebugStream.text(cat, lvl, unitId, message)` | `LOG_TEXT` | A UTF-8 message, for the rare human line. Allocating (the caller built the string); truncated, never torn, at 64 KiB. |

Every Log event also carries the emitting green thread and its owning processor, so a run with several
workers in flight can be demuxed back into one timeline per worker.

Two gates keep this free enough to leave in place:

- **Compile time** — without `--debugstream`, every call above emits **zero instructions**. Not a
  branch that is never taken: nothing at all.
- **Runtime** — with the ring detached, each call is a load of `__ds_base`, a test, and a not-taken
  branch. The bail is inline, before any `call`.

---

### `maxon debug`

Reads the `.mxdbg` debug-info sidecar back. Both faces are read-only, and both accept either the
executable or the sidecar itself.

**Usage:**
```bash
maxon debug --dump-info <exe|.mxdbg> [header|files|functions|types|lines|statements|inline]
maxon debug --symbolize <exe|.mxdbg> <codeOffset...>
```

`--dump-info` with no section names prints the whole sidecar. A name that is not a section is refused
by name, before the file is even read, so a misspelling is answered as a misspelling rather than by
whichever failure the path happens to produce first.

`--symbolize` resolves `.text` code offsets to `file:line:col`.

Like `monitor`, the parse stops at the word `debug` — its own subcommands are `-`-leading and every
other arm of the driver would otherwise read them as unknown options and abort.

---

### `maxon coverage`

Reads back what a `--coverage` binary counted, as line and branch coverage.

**Usage:**
```bash
maxon coverage <run|report> <exe> [--json] [--data=<file>] [--timeout=<seconds>] [args...]
```

- **`run`** launches the instrumented program (deleting the previous data file first, so a run whose
  dump never happened cannot be reported from stale numbers) and then reports on the counters it wrote.
  The program's own output goes to **stderr**, so stdout is only the report. Any trailing `args...` are
  the program's.
- **`report`** reads counters already on disk and launches nothing.

| Option | Description |
|--------|-------------|
| `--json` | Emit the report as JSON |
| `--data=<file>` | Where to READ counters from. Belongs to `report`; refused on `run`, which reports on the file the program itself just wrote. |
| `--timeout=<seconds>` | Bound the run (default 600). Belongs to `run`; refused on `report`, which launches nothing to wait for. |

Target arguments are refused on `report` for the same reason: it launches no program to give them to.
Everything after the verb is filed verbatim, so a target's own flag is neither consumed on the way past
nor able to abort the run before the child is spawned.

---

### `maxon profile`

Launches a program and samples where its CPU time goes. It needs no instrumentation and no rebuild —
only the `.mxdbg` sidecar is read, so the program measured is the one that ships. The program's own
output goes to stderr, so stdout is only the report.

⚠ **x64-windows only.** Sampling suspends the target's threads and reads an x64 `CONTEXT` record.
Elsewhere the command is refused by name rather than being absent.

**Usage:**
```bash
maxon profile run <exe> [--json|--folded] [--rate=<hz>] [--min-percent=<share>] [--timeout=<seconds>] [--target-env=<NAME>=<VALUE>] [args...]
```

| Option | Description |
|--------|-------------|
| `--json` | Emit the full report as JSON instead of a summary |
| `--folded` | Emit collapsed stacks (`root;child;leaf <count>`) — the format flamegraph.pl, inferno and speedscope all read. `--json` and `--folded` are two different machine formats; asking for both is refused. |
| `--rate=N` | Samples per CPU-second a thread consumes (default 1000, min 1, max 10000): a thread is charged one sample per `1/N` s of CPU it actually used, so one that wakes for microseconds is charged almost nothing. A rate is **refused rather than clamped**; the report also states the rate it ACHIEVED beside the one requested, since the real ceiling is the OS timer's granularity, and how many stacks were captured beside how many samples they carry. |
| `--min-percent=N` | Hide rows below this share of the run from the printed tables (default 1). How many were hidden is always reported. |
| `--timeout=S` | Stop the program and report a PARTIAL profile after S seconds (default 600). Unlike coverage's deadline, reaching it is not fatal to the measurement — the samples taken so far are still a profile. |
| `--target-env=N=V` | Set a variable in the PROFILED program's environment (repeatable). `MAXON_MAX_PROCS=N` pins the scheduler's worker count, which is what makes a green-thread profile reproducible rather than machine-dependent. |

This tool's own exit code says whether the MEASUREMENT completed; the program's status is reported as
`target exit code`.

---

### `maxon verify-warm-rebuild` and `maxon verify-recheck`

Two gates on the incremental machinery. Each takes exactly one path and exits 0 only if its property
holds; the failures print their own reasons.

- **`maxon verify-warm-rebuild <file>`** asserts the query spine is deterministic **and** incremental,
  without ever running the pipeline.
- **`maxon verify-recheck <file|dir>`** asserts one project can be re-checked: two checks over
  unchanged input agree, and a diagnostic introduced by an edit CLEARS when the edit is undone. That is
  what an editor holding one project open for hours actually does.

---

### `maxon upgrade`

```bash
maxon upgrade              # install the newest release over the install this compiler runs from
maxon upgrade --dry-run    # print the install root and the command that would run, and run nothing
```

It runs the published install script again, against the install the running compiler sits in —
`<root>/bin/maxon` beside `<root>/stdlib`, which is the layout the script makes. The root comes from
where the binary is, never from the caller's `MAXON_INSTALL`: the child is handed this root in that
variable, so an upgrade cannot update some other install and leave this one as it was. Its exit status
is the script's.

- **macOS and Linux:** `curl` downloads `https://maxon.dev/install.sh`, then `/bin/sh` runs it with
  `--no-modify-path`. Two steps rather than `curl | sh`, because a pipeline reports success when the
  download fails.
- **Windows:** Windows PowerShell, by its absolute path under `%SystemRoot%`, runs
  `https://maxon.dev/install.ps1` as a script block with `-NoPathUpdate`.

A compiler the script did not install is refused (exit 1), naming what does update it:

| Where the compiler is | What `upgrade` says to run |
|-----------------------|----------------------------|
| The container image (`MAXON_IMAGE` is set and not empty) | `docker pull <MAXON_IMAGE>`, the newest image of that variant |
| A Homebrew keg (`Cellar/maxon/<version>/`) | `brew upgrade maxon-lang/tap/maxon` |
| A source checkout (`maxon-bin/` beside `stdlib/`) | `git -C <root> pull`, then `maxon-bin/.maxon/maxon build maxon-bin` |
| Anywhere else | The install one-liner |

`--dry-run` never bypasses a refusal, and every refusal leaves stdout empty. The command takes no
argument and no other option. `maxon upgrade --version X` is refused too, naming the install script's
own version option, which is what installs a chosen release.

---

### `maxon version`

Prints one line to stdout and exits 0:

```bash
maxon version    # maxon 0.1.1 (a1b2c3d 2026-09-09) (x64-windows)
```

The shape is rustc's: the release number, then the commit and the day it was built from, then the host
target. The last three are what make a report about "0.1.1" answerable when there have been forty
builds of 0.1.1, and what tell four targets shipping under one name apart.

It takes no arguments — a positional is refused rather than ignored, because a `maxon version` that
printed an answer while dropping half of what was typed is the one outcome a caller cannot see.

**`--version` and `-V` were withdrawn.** They are refused by name, naming this command, so a script
still carrying one is told what to write rather than that the driver never heard of it.

---

### `maxon help`

```bash
maxon                  # the version, and one line per command
maxon help             # every command, with the options each one reads
maxon help build       # one command's entry and its options
```

`maxon` with **no arguments** answers the only question a caller with nothing to go on can ask: which
binary this is, and what it does. One line per command, and exit **0** — nothing was typed wrong.

It lists the commands for **using** the compiler and leaves out the four for **working on** it
(`spec-test`, `scale-test`, `verify-recheck`, `verify-warm-rebuild`) — and its footer says so, because a
short list that quietly omitted them would leave a reader who needs one with no reason to look further.
Hiding is a listing concern only: every one of them is still typed, parsed and run exactly as before.

`maxon help` prints the whole reference — nothing is left out here — as two runs, the second under
`Commands for working on the compiler itself:`. Each command's entry carries every option it reads,
indented underneath. An option that several commands accept — `--target=` is read by `build`, `spec-test`
and `test` — is listed under **each** of them, so one command's entry is that command's whole surface and
a reader never filters a global list.

`maxon help <command>` prints just that entry. A word that names no command is refused (exit 1) with
the command list under it, which is the way back.

It takes no options: `maxon --emit-ir help` is refused rather than answered as though the flag had
meant something.

**`--help` and `-h` were withdrawn.** Help is a command, so there is one door onto it, and a caller does
not have to know the word to find it — `maxon` alone lists it. Both spellings are refused by name,
naming the command, rather than reported as options the driver never heard of.

Commands are listed **alphabetically**, and that is a decision rather than an accident: it is the one
ordering a gate can check, so `tests/cli` asserts it off the binary's own output. An order by how often a
command is used could be checked by nothing, and is not one fact anyway — a compiler developer runs
`spec-test` fifty times a day and `build` twice, and someone using Maxon does the reverse.

The listing is **derived**: the roster the parser reads to decide that a word is a command is the same
roster the reference walks to print one, and each entry declares its own audience. A command cannot be
documented under a word nothing accepts, nor accepted under a word the reference never prints, nor added
without saying which listing it belongs in.

---

## Targets

`--target=<cpu>-<os>` is accepted by `build`, `spec-test` and `test`. Without it, the host target is
used.

| Spelling | Output |
|----------|--------|
| `x64-windows` | PE executable |
| `x64-linux` | ELF executable |
| `arm64-linux` | ELF executable |
| `arm64-macos` | Mach-O executable |
| `wasm32-wasi` | WASI Preview 2 component |

A spelling that names none of them is refused (`error: unknown --target '<spec>'`) and the command
exits 1. `maxon help build` prints the authoritative list; this table is a copy of it.

Not every target provides every host facility. `--debugstream` and `--coverage` are refused by name on
a target that cannot serve them, before anything is compiled, rather than compiling to an instrument
that silently reports nothing.

---

## Logging

Every command accepts logging options **except [`maxon run`](#maxon-run)**, whose tail belongs to the
program it launches: a `--log=` written after the path is one of the program's own arguments. Written
before the word — `maxon --log=compiler:debug run app.maxon` — it is this driver's, and it also
restores the build chatter `run` otherwise silences.

| Option | Description |
|--------|-------------|
| `--log=LEVEL` | Set all log categories to the given level |
| `--log=CATEGORY:LEVEL` | Set one category to the given level |

**Levels:** `none`, `error`, `info`, `debug`, `trace`

**Categories:** `compiler`, `lexer`, `parser`, `semantic`, `ir`, `codegen`, `binary`, `testing`

An unrecognized spec is reported on stderr and otherwise ignored.

```bash
maxon build app.maxon --log=codegen:trace
maxon build app.maxon --log=compiler:debug   # per-phase timing + memory as a table
maxon spec-test --log=ir:debug
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | The command ran and failed — a compile error, a failing gate, an unusable command line |
| `2` | **Nothing ran**: the tree is not in a state to be measured (a compiler binary older than its sources, a checkout another command is already writing), or a `maxon test` run that could not happen |
| `101` | A leak-gated program still held an allocation at exit. Reported by the program, not by the driver; `maxon test` turns it into a `LEAKED` outcome. |

`2` is deliberately distinct from `1`: "nothing ran" and "something failed" are different facts, and a
caller reading only the exit code has to be able to tell them apart.

⚠ **[`maxon run`](#maxon-run) forwards the PROGRAM's exit code**, so the table above does not describe
it. Any number in it is the program's own; only `1` is ever this driver's, and it means the program was
never reached. [`maxon upgrade`](#maxon-upgrade) forwards the install script's the same way.

---

## Project Structure

A Maxon project is a directory of `.maxon` files. There is no manifest to write — the directory *is*
the project.

```
myproject/
├── main.maxon          # entry point (contains main)
├── utils.maxon
├── lib/
│   ├── math.maxon
│   └── io.maxon
└── pricing.test.maxon  # tests: not part of an ordinary build
```

All `.maxon` files in subdirectories are included when compiling a directory, with three exclusions:

1. **`build.maxon`** — the build manifest is a program in its own right, not part of the one being
   built, so it is never swept in. Naming it explicitly still compiles it, and `maxon build` with no
   path runs it — see [The build manifest](#the-build-manifest).
2. **`*.test.maxon`** — a `test` declaration's file is a source *category*, not a flag inside the file,
   so test sources are not part of an ordinary build. `maxon test` names them explicitly. The match is
   case-insensitive, so `Suite.TEST.maxon` is a test file too.
3. **Anything under a `.maxonignore`** — see below.

### The build manifest

`maxon build` with no path looks for **`build.maxon`** in the current directory, compiles it, runs it,
and performs the build it describes.

⭐ **A manifest is a PROGRAM, not a configuration file.** It is ordinary Maxon with the whole standard
library available, so a build can *compute* what it compiles — read a directory, choose by host,
stamp a version — rather than only spell it out. The compiler does not parse the manifest; it runs it
and reads what it prints.

**Its entry point is `build`, not `main`.** A manifest holds tasks, and `build` is the one this
command asks for by name. It returns `ExitCode`; a non-zero return, or a crash, fails the build and
nothing is compiled.

```maxon
export function build() returns ExitCode
	var targets = BuildConfigArray.create()
	targets.push(Build.target("maxon-bin", source: "maxon-bin", output: "maxon-bin/.maxon/maxon"))
	targets.push(Build.target("dev-mcp", source: "maxon-dev-mcp/mcp", output: "maxon-dev-mcp/mcp/.maxon/maxon-dev-mcp"))
	Build.buildTargets(targets)
	return 0
end 'build'
```

**One target needs no name; several are listed rather than guessed at.** `maxon build` with no
argument builds a manifest's only target, and with several it prints their names and compiles
nothing — picking the first would build something the caller did not ask for and report success.
`maxon build <name>` selects one.

⛔ **A target name outranks a path of the same spelling.** A target names an OUTPUT as well as a
source, so `maxon build maxon-bin` resolved as a path would compile the same directory to a different
file and leave the real one stale. A spelling no target declares falls through to the path meaning, so
a manifest never breaks an ordinary `maxon build some/file.maxon`.

**What it prints is the contract.** `stdlib/Build.maxon` writes the build description as JSON on
stdout, and the compiler reads it back — so both ends share one description of what a build is:

| Call | Meaning |
|---|---|
| `Build.build(source, output:, version:)` | Compile one file or directory to one output. The common shape. |
| `Build.target(name, source:, output:, version:)` | One NAMED target, for a manifest describing more than one. |
| `Build.buildTargets(targets)` | Emit several named targets. |
| `Build.buildWithConfig(config)` | Full control via a `BuildConfig`: several sources, in order. |

The output path omits the extension: the compiler appends `.exe` on Windows and nothing elsewhere.

⭐ **`version` PUTS A VERSION INSIDE THE BINARY** — a Windows `VS_VERSIONINFO` resource, and a Mach-O
`LC_SOURCE_VERSION`. Omit it and the binary reports 0.0.0.0, which is what an unversioned program
honestly is. The product NAME in that metadata is the output's own file name, not the manifest's
`name`: that field is what selected the build, and is `.` for a project built from its own directory.

⛔ **An empty `sources` is refused rather than read as "this directory".** Accepting it would compile
every file beneath the manifest — every spec, every test — under a command that named nothing at all.

⚠ **`-o` and `--target` on the command line outrank the manifest**, for the reason every flag outranks
a file: the person typing the command is answering a narrower question than the file was. The manifest
itself is always built for the host, whatever `--target` says, because it is a program this machine has
to run in a moment.

#### Rebuilding the compiler that is running

The manifest at the root of the Maxon repository builds the compiler into the slot the running
compiler occupies, so `maxon build` there replaces the binary executing the command.

That works. No operating system permits *deleting* a running executable, but they permit *renaming*
one, so the compiler moves its running image to `maxon.previous.exe` first and the slot is then empty
for the ordinary write path.

⛔ A **failed** build therefore leaves the slot **empty** and the last good compiler at
`maxon.previous.exe`. That is deliberate: the alternative — compiling to a temporary name and swapping
it in afterwards — leaves the old binary in place when the build fails, and a stale compiler answering
as though it were current is the failure every staleness check here exists to prevent. If a build
fails, move `maxon.previous.exe` back deliberately.

### Ignoring Directories

Place a `.maxonignore` file in any directory to exclude it, and every directory beneath it, from
compilation, formatting and LSP processing. The file is a flag — its contents are ignored, and a marker
anywhere *above* a directory excludes it too.

```
myproject/
├── main.maxon
├── fixtures/
│   ├── .maxonignore     # this directory is skipped by the walk
│   └── sample.maxon
└── src/
    └── app.maxon
```

The marker says *"do not sweep me into somebody else's program"*, never *"this file may not be
compiled"*. **Naming a path outright overrides it**: `maxon build fixtures/sample.maxon` compiles that
file, and `maxon fmt` formats a file you name inside a marked directory.

---

## Namespace Resolution

Namespaces are derived from file paths, relative to the project root (the directory you named, or the
file's own directory for a single-file build):

| File Path | Namespace |
|-----------|-----------|
| `main.maxon` | (global) |
| `utils/helpers.maxon` | `utils` |
| `lib/math/vectors.maxon` | `lib.math` |

### Calling Functions Across Files

**Full qualification:**
```maxon
var result = utils.format(value)
```

**Suffix matching (if unambiguous):**
```maxon
var result = format(value)  // finds utils.format if unique
```

### Export Visibility

Functions must be exported to be visible from other files:

```maxon
// utils.maxon
export function helper(x int) returns int
	return x * 2
end 'helper'

function internal(x int) returns int  // not visible from other files
	return x + 1
end 'internal'
```

---

## Standard Library

The standard library is loaded for every compilation. The compiler finds it by walking **up from its
own executable's directory** looking for a `stdlib/` — not up from the working directory, so a program
compiled in a throwaway temp directory still gets the right library. Failing to find one is a hard
error, not a silent skip.

⚠ That walk is why a bootstrap compiler is kept **alone** in `.bootstrap/`: a release archive unpacked
whole would leave a released `stdlib/` one directory above the compiler, and the tree's own would never
be reached.

See [STDLIB_REFERENCE.md](STDLIB_REFERENCE.md) for what the library contains.

---

## Common Workflows

### A single file

```bash
maxon run program.maxon        # compile if needed, then run — the build is cached
```

To produce a binary you can ship or hand to another tool, build it:

```bash
maxon build program.maxon
./program.exe
```

### A project

```bash
maxon build myproject
maxon test myproject
maxon fmt myproject
```

### Working on the compiler

```bash
./maxon-bin/.maxon/maxon build                  # rebuild it with itself
maxon-bin/.maxon/maxon spec-test                # the whole suite
maxon-bin/.maxon/maxon spec-test --filter=arrays
scripts/fixpoint.sh                             # does it reproduce itself byte for byte?
```

### Investigating emitted code

```bash
maxon build problem.maxon --emit-ir                                # writes problem.ir
maxon build problem.maxon --emit-ir-runtime=__managed_get          # plus a runtime body
maxon build problem.maxon --metrics=temp/phases.tsv                # where the compile went
```

### Investigating a running program

```bash
maxon build app.maxon --debugstream && maxon monitor --filter=mm app.exe
maxon build app.maxon --coverage    && maxon coverage run app.exe
maxon profile run app.exe --folded > app.folded
maxon debug --dump-info app.exe functions lines
```
