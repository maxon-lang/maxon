# Maxon CLI Reference

Everything the `maxon` compiler driver accepts: the commands, their options, the project layout they
read, and the tools around them.

`maxon help` prints the same command and option list from the driver itself, and `maxon help <command>`
prints one command's part of it. Where this document and that listing disagree, the listing is the
compiler.

## Commands

`maxon` is one binary. It compiles, runs, formats and tests Maxon programs, serves editors and AI
agents, and reads back what a program did.

| Command | Description |
|---------|-------------|
| `maxon <file>.maxon [args...]` | Run a Maxon file as a script: [`maxon run`](#maxon-run) without the command word |
| `maxon build <file\|directory>...` | Compile a Maxon program to an executable |
| `maxon coverage <run\|report> <exe>` | Run a `--coverage` binary and report line and branch coverage ([Debugging and Profiling](#debugging-and-profiling)) |
| `maxon debug --dump-info <exe>` | Print the `.mxdbg` debug-info sidecar beside a binary ([Debugging and Profiling](#debugging-and-profiling)) |
| `maxon debug --symbolize <exe> <offset...>` | Resolve code offsets to `file:line:col` ([Debugging and Profiling](#debugging-and-profiling)) |
| `maxon fmt [file\|directory]` | Re-print `.maxon` sources in canonical layout, in place |
| `maxon help [<command>]` | Print the command and option reference, whole or for one command |
| `maxon lsp-server` | Speak the Language Server Protocol over stdio ([Editor Support](#editor-support)) |
| `maxon mcp-server [--dev]` | Speak the Model Context Protocol over stdio ([MCP Server](#mcp-server)) |
| `maxon monitor [--filter=…] <exe> [args...]` | Run a `--debugstream` binary and print its trace events ([Debugging and Profiling](#debugging-and-profiling)) |
| `maxon profile run <exe>` | Sample a running program and report where its CPU time went ([Debugging and Profiling](#debugging-and-profiling)) |
| `maxon run <file\|directory> [args...]` | Compile a program, or reuse a cached build of it, and run it |
| `maxon test [directory]` | Run a project's own `test` declarations |
| `maxon upgrade [--dry-run]` | Update this compiler's install to the newest release |
| `maxon version` | Print the version, the commit it was built from, and the host target |

Four more commands are for working on the compiler itself: `spec-test`, `scale-test`, `verify-recheck`
and `verify-warm-rebuild`. `maxon` with no arguments does not list them; `maxon help` does. See
[Working on the Compiler](#working-on-the-compiler).

Only the **first** command word on a line is the command. A later one is an ordinary positional
argument, so `maxon fmt fmt` formats the directory `fmt/`.

An option the driver does not implement is refused before any command runs (`error: unknown option:
<option>`, exit 1, or 2 for `maxon test`), rather than ignored.

### `maxon run`

Compiles a program, or reuses a cached build of it, and runs it.

```bash
maxon run <file|directory> [args...]
maxon <file>.maxon [args...]
```

The second form is the same command: a **first** argument ending in `.maxon` selects `run` with no
command word. That is what a kernel hands the interpreter for a script whose first line is
`#!/usr/bin/env maxon`.

**Everything after the path is the program's command line** and reaches it untouched, including tokens
that look like driver options: in `maxon run app.maxon --filter=x -o out`, both `--filter=x` and
`-o out` are the program's. So `run` takes no options of its own.

The program inherits stdin, stdout, stderr and the working directory, and **its exit code becomes this
command's**. The build itself prints nothing, so the program's output is not mixed with compiler
output. The program's `argv[0]` is the cached executable, not the path you typed.

`run` always compiles for the host. To build for another target, use
[`maxon build --target=`](#maxon-build).

#### The run cache

Each program gets a directory of its own in a cache, and its build is reused until something it was
built from changes:

```text
<root>/maxon/run/v<cache format>/<hash of the program's path and entry point>/
├── hello-<key>.exe         # the build, named after the key it was built under
└── hello-<key>.exe.mxdbg   # its debug-info sidecar
```

- **The key is a content hash**, never a modification time. It covers every source file of the program
  and every standard-library source the build compiles, each hashed by its bytes, plus the compiler's
  identity (its path, size, modification time and target). Editing any of them rebuilds; nothing else
  does.
- **The key is the file name.** A cache hit is that file existing, so two runs of one program cannot
  serve each other a stale build. A fresh build is written under a temporary name and renamed into
  place, so simultaneous cold runs never share an output path. Superseded builds, and builds an older
  cache format left, are removed on a best-effort basis.
- **The path is resolved against the working directory but not canonicalized.** `x.maxon`, `./x.maxon`
  and `a/../x.maxon` get three cache slots. That costs an extra compile, never a wrong binary.

`<root>` is `MAXON_RUN_CACHE_ROOT` when it is set, then `LOCALAPPDATA` and then `TEMP` on Windows, and
`TMPDIR` and then `/tmp` elsewhere. On Windows, a host that sets none of them is refused by name.

#### Scripts and the shebang line

A source file whose **first two bytes** are `#!` has that first line ignored, which lets a Maxon
program be an executable script:

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

The `#!` must be at byte 0. Anywhere else, `#` opens a compiler directive (`#if` / `#else` / `#endif`),
so a later `#!` is **E1009: Unknown compiler directive**. The line's newline is kept, so line numbers in
diagnostics still match the file, and `maxon fmt` keeps the line as written.

Windows has no shebang mechanism. Git Bash and WSL honour the line; `maxon hello.maxon` works everywhere.

**Exit codes:**

| Code | Meaning |
|------|---------|
| the program's | Forwarded as-is, including the raw status of a child that terminated abnormally |
| `1` | `run` could not start the program: no program named, no such file, a compile error (the diagnostics are printed and nothing runs), no writable cache root, or a build that could not be stored in the cache |

A missing file is reported as `error: file not found: <path as typed>`, the same sentence `maxon build`
prints.

```bash
maxon run hello.maxon                 # compile if needed, then run
maxon run myproject --verbose         # a directory as one program; --verbose is the program's
maxon hello.maxon a b c               # no command word, three arguments
MAXON_RUN_CACHE_ROOT=/build/cache maxon run hello.maxon
```

### `maxon build`

Compiles Maxon source to a standalone executable.

```bash
maxon build <file|directory>... [options]
maxon build [<target name>] [options]
```

**Arguments.** One or more paths. **Several paths are compiled as one program, in the order given.** A
directory contributes every `.maxon` file beneath it, except `build.maxon` (in any letter case),
`*.test.maxon` files and subtrees marked with a `.maxonignore`. A file you name explicitly is compiled whatever a
`.maxonignore` above it says.

**No path** runs the `build.maxon` manifest in the current directory, and a bare word that names one of
its targets builds that target. See [Project Structure](#project-structure). A directory
with no `build.maxon` prints a usage line and exits 1.

**Options:**

| Option | Description |
|--------|-------------|
| `-o <path>`, `--output=<path>` | Output executable path. Without it the name comes from the first path given: a file is built beside itself (`foo.maxon` → `foo.exe` on Windows), and a directory into itself under its own name (`app` → `app/app.exe`). The target's executable extension is added unless the path already carries it. A value that is not a path (a non-`file` URL, or on Windows a name holding `< > " \| ? *` or a control character) is refused with exit 1. |
| `--target=<cpu>-<os>` | Compile for this target instead of the host: `x64-windows`, `x64-linux`, `arm64-macos`, `arm64-linux` or `wasm32-wasi`. See [Targets](#targets). |
| `--emit-ir` | Also write the lowered Target IR beside the executable, as `<output>.ir`. It shows the functions from the program's own source. |
| `--emit-ir-runtime=<a>,<b>` | Also render these compiler-emitted or standard-library functions in that IR. Implies `--emit-ir`. A value naming no function is refused. |
| `--no-debug-info` | Do not write the `<output>.mxdbg` debug-info sidecar. It is written by default, and the executable is byte-identical either way. See [Debugging and Profiling](#debugging-and-profiling). |
| `--coverage` | Instrument for code coverage: the binary counts each statement and branch arm it executes and writes the counts to `<output>.mxcov` as it exits. This changes the emitted code, so it is a separate build from the one you ship. Read the counts with `maxon coverage`. Needs the debug-info sidecar, so `--no-debug-info` beside it is refused. |
| `--debugstream` | Emit the shared-memory debug-stream producer that `maxon monitor` reads, with the memory manager's events. Also enables the `__DebugStream` builtin; without the flag its calls emit nothing. Refused on a target without shared memory and an uptime clock. |
| `--async-trace` | Write the green-thread trace to stderr as the program runs: one line per spawn, sleep, I/O wait, resume and await. See [Debugging and Profiling](#debugging-and-profiling). |
| `--define=<name>=<value>` | Replace a top-level `String` constant's written-out default with `<value>`. Repeatable. See [Defines](#defines). |
| `--metrics=<path>` | Write this compile's per-phase time and memory to `<path>` as TSV. `--log=compiler:debug` prints the same numbers as a table. |

`--debugstream`, `--async-trace` and `--coverage` are opt-in **per build**. Without the flag, none of
that machinery is emitted.

A build prints the compiler's version and an early-preview warning to stdout, then
`Compiled -> <path>` on success, and exits 0. Progress lines (`[CMP] INFO: Wrote … bytes of code to …`)
go to stderr; `--log=error` silences them. A compile error prints its diagnostics to stderr and exits 1.

```bash
maxon build hello.maxon                       # → hello.exe on Windows, hello elsewhere
maxon build src/ -o build/app                 # a whole directory, named output
maxon build a.maxon b.maxon                   # one program from two files, in that order
maxon build                                   # run build.maxon
maxon build app                               # build.maxon's target named "app"
maxon build app.maxon --target=wasm32-wasi
maxon build app.maxon --define=Version=1.4.2
```

#### Defines

`--define=<name>=<value>` replaces the value of a top-level `String` constant whose initializer is a
string literal. Use it for facts known to whoever runs the build rather than to the source: a version,
a commit, a release channel.

```maxon
let Version = "dev"        // what a plain `maxon build` reports
```

```bash
maxon build myapp --define=Version=1.4.2
maxon build myapp --define=Build.Channel=nightly
```

The declaration keeps a real default, so a fresh checkout builds with no flags. The name may be bare or
namespace-qualified (`Build.Channel` is `Channel` in the `Build/` directory). The split is at the
first `=`, so the value may contain `=` and the name may not: `--define=Url=https://x/?a=b` sets the
whole URL.

A define that would do nothing is refused, and every problem on the command line is reported before
the build stops:

| Code | Refused because |
|------|-----------------|
| **E3149** | the name matches no declaration |
| **E3150** | the name matches more than one declaration (both are named, with their files) |
| **E3151** | the initializer is not a plain string literal |

A `build.maxon` manifest can supply defines too; a `--define` on the command line wins over the
manifest's.

### `maxon fmt`

Re-prints `.maxon` sources in canonical layout, **in place**.

```bash
maxon fmt [<file|directory>]
```

With **no path it formats the whole working directory**. A named **file** is formatted whatever it is
called. A **directory** is walked for `.maxon` files, skipping a project's `build.maxon` (in any letter case;
the compiler's own `stdlib/` and `runtime/` hold no manifest, so their `Build.maxon` is formatted), anything under a
`.maxonignore`, and any subdirectory that holds a `.git` (a nested clone or worktree), so a run never
rewrites another repository's files.

**It takes no options.** Any `-`-leading argument is refused with exit 1 and nothing written, and so
is a second path.

A file that is already canonical is not rewritten, so its modification time is untouched. A file that
cannot be lexed is left as it is and counted as unchanged. The run prints one `formatted: <path>` line
per rewritten file and a `fmt: N file(s) changed, M unchanged.` summary.

The formatter decides **blank lines** as well as indentation. Exactly one blank line separates adjacent
groups in a scope, and none appears inside a group:

| Inside | These pack together | These stand alone |
|---|---|---|
| a file, a `type` or `extension` body | `let`/`var` lines; `typealias` lines | every declaration that opens a body |
| an `enum` or `union` body | the cases | a nested declaration |
| an `interface` body | the bodyless signatures | anything else |
| a function or labeled block body | the statements, `let`/`var` included | every nested block |
| a `match` body | the arms | — |
| a multi-line `[`/`{` literal | *(never grouped: your blank lines are kept, at most one in a row)* | — |

A `///` doc comment stays attached to the declaration below it. A whole-line `//` comment does too when
there is no blank line between them; with a blank line it reads as a section heading. A blank line you
put inside a group survives, at most one in a row.

```bash
maxon fmt              # the working directory
maxon fmt main.maxon   # one file
maxon fmt src/         # one directory
```

### `maxon test`

Runs a project's unit tests: every `test` declaration in its `*.test.maxon` files. How to write tests
is covered in [Testing](LANGUAGE_REFERENCE.md#testing).

```bash
maxon test [directory] [options]
```

`[directory]` is the project to test (default: the working directory). A second positional is refused.

All discovered tests are compiled into **one binary** with a generated entry point, and which of them
run is a runtime argument. Changing `--filter` between runs therefore recompiles nothing. The project's
own `main` needs no change: the test binary does not use it, and the same directory still builds
normally with `maxon build`.

**Options:**

| Option | Description |
|--------|-------------|
| `-t P`, `-t=P`, `--filter=P` | Run only tests whose name or file path contains `P` (case-insensitive). Comma-separated patterns are a union. A bare `-t` with nothing after it is refused. |
| `--list` | Print the tests that would run, and compile nothing. |
| `--json` | Emit the report as JSON instead of text. |
| `--isolate` | Run every test in its own process, instead of one process per test file. |
| `--bail`, `--bail=N` | Stop starting new work after `N` failures (`--bail` alone means 1). Work already running finishes, so the count may exceed `N`. Without it, every test runs. |
| `--timeout=<ms>` | Kill a test process after this many milliseconds (default 5000). One process runs one file's tests, so the deadline covers the whole file unless `--isolate` is given. |
| `--no-timing` | Omit durations, making stdout byte-for-byte reproducible. |
| `--color=auto\|always\|never` | Colour the report. `auto` colours only when stdout is a terminal, `NO_COLOR` is unset and `TERM` is not `dumb`. On `wasm32-wasi` a program cannot detect a terminal, so `auto` means `never` there; use `always` to force colour. |
| `--target=<cpu>-<os>` | Build the test binary for this target and then run it. On a host that cannot execute that target, every test is reported as not run. |

The run is serial; there is no `--workers` option.

**Where it runs.** The test binary runs in the directory `maxon test` was invoked from, so a relative
path in a test means what it means in your shell. The build lives in `<project>/.maxon/test/`, which
carries its own `.maxonignore` so generated sources never leak into an ordinary build.

**Isolation.** By default each test **file** runs in its own process. If a test crashes the process, the
harness re-runs the remaining tests of that file so one crash does not hide the others' results, and a
leak found at exit is attributed by re-running tests alone. `--isolate` gives every test its own
process from the start.

**Outcomes.** A test that does not pass is reported as one of five states:

| State | `--json` `state` | Meaning |
|-------|------------------|---------|
| `✓` | `passed` | The test passed |
| `FAIL` | `failed` | The body threw: a failed assertion, or an error nothing caught |
| `CRASHED` | `crashed` | The test started and never finished: it took the process down (a `panic` cannot be caught) |
| `TIMED OUT` | `timedOut` | Still running when its process hit `--timeout`, and was killed |
| `DID NOT RUN` | `didNotRun` | Selected for a process that died before reaching it, and re-running made no progress. Never counted as a pass. |
| `LEAKED` | `leaked` | Held an allocation at exit (exit code 101), attributed by re-running the test alone |

**Exit codes:**

| Code | Meaning |
|------|---------|
| `0` | Every test passed |
| `1` | A test failed, crashed, timed out, leaked or did not run, **or no tests were found** |
| `2` | The run could not happen: a bad option, a compile error, no source files, no such project |

A run that finds no tests exits 1 on purpose, so a suite that silently stopped containing tests does
not read as green.

**Example.** A failing expectation, then the fix:

```maxon
// pricing/pricing.maxon
typealias Cents = int(0 to i64.max)

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
	try Expect.equal(totalCost(250, quantity: 4) as AssertedInt, expected: 1000)
end 'a small order pays full price'

test 'ten items take the bulk discount'
	try Expect.equal(totalCost(250, quantity: 10) as AssertedInt, expected: 2500)
end 'ten items take the bulk discount'
```

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

The `file:line` on the `FAIL` line is the assertion's own. Correct the expectation to `2250` and the same
command reports `2 pass`, `0 fail` and exits 0.

```bash
maxon test                        # every test under the working directory
maxon test src/parser             # one project's tests
maxon test -t json                # only tests whose name or file mentions "json"
maxon test --filter=parser,lexer  # two patterns, as a union
maxon test --list                 # what would run, without compiling
maxon test --json --no-timing     # machine-readable and reproducible
maxon test --bail=3 --timeout=20000
```

### `maxon upgrade`

Updates the install this compiler runs from to the newest release.

```bash
maxon upgrade              # install the newest release over this compiler's install
maxon upgrade --dry-run    # print the install root and the command that would run, and run nothing
```

It runs the published install script again, against the install the running compiler sits in
(`<root>/bin/maxon` beside `<root>/stdlib`, the layout the script creates). The root comes from where
the binary is, never from your `MAXON_INSTALL`: the script is handed this root in that variable, so an
upgrade cannot update a different install. Its exit status is the script's.

- **macOS and Linux:** `curl` downloads `https://maxon.dev/install.sh`, then `/bin/sh` runs it with
  `--no-modify-path`.
- **Windows:** Windows PowerShell (found under `%SystemRoot%`) runs `https://maxon.dev/install.ps1` with
  `-NoPathUpdate`.

A compiler the script did not install is refused with exit 1, naming what does update it:

| Where the compiler is | What `upgrade` tells you to run |
|-----------------------|---------------------------------|
| The container image (`MAXON_IMAGE` is set) | `docker pull <image>` |
| A Homebrew keg | `brew upgrade maxon-lang/tap/maxon` |
| A source checkout | `git -C <root> pull`, then `maxon-bin/.maxon/maxon build maxon-bin` |
| Anywhere else | The install one-liner |

`--dry-run` never bypasses a refusal, and a refusal writes nothing to stdout. The command takes no other
argument. To install a specific release, use the install script's own version option; `maxon upgrade`
refuses `--version`, naming that option.

### `maxon version`

Prints one line to stdout and exits 0:

```bash
maxon version    # maxon 0.1.1 (a1b2c3d 2026-09-09) (x64-windows)
```

The line holds the release number, the commit and date the compiler was built from, and the host
target. It takes no arguments. The old `--version` and `-V` spellings are refused, naming this command.

### `maxon help`

```bash
maxon                  # the version, the early-preview warning, and one line per command
maxon help             # every command, with the options each one reads
maxon help build       # one command's entry and its options
```

`maxon` with no arguments prints the version, the warning and the commands for using the compiler, and
exits 0. `maxon help` prints the whole reference, including the commands for working on the compiler,
with every option each command reads listed under it. An option several commands accept (such as
`--target=`) is listed under each. Commands are listed alphabetically.

`maxon help <command>` prints one entry. A word that names no command is refused with exit 1 and the
command list. `help` takes no options, and the old `--help` and `-h` spellings are refused, naming this
command.

### Logging

Every command except [`maxon run`](#maxon-run) accepts a logging option. With `run`, a `--log=` after the
path belongs to the program; written before the command word (`maxon --log=compiler:debug run app.maxon`)
it is the driver's, and it also brings back the build output `run` otherwise keeps quiet.

| Option | Description |
|--------|-------------|
| `--log=LEVEL` | Set every category to `LEVEL` |
| `--log=CATEGORY:LEVEL` | Set one category to `LEVEL` |

**Levels:** `none`, `error`, `info` (the default), `debug`, `trace`

**Categories:** `compiler`, `lexer`, `parser`, `semantic`, `ir`, `codegen`, `binary`, `testing`

Log lines go to stderr, prefixed with the category and level (`[CMP] INFO: …`). An unrecognized spec is
reported on stderr and otherwise ignored. `maxon test` lowers the level to `error` unless you pass
`--log=`, so its stdout is only the report.

```bash
maxon build app.maxon --log=codegen:trace
maxon build app.maxon --log=compiler:debug   # per-phase time and memory as a table
maxon build app.maxon --log=error            # only errors
```

### Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | The command ran and failed: a compile error, a failing check, a command line it cannot act on |
| `2` | Nothing ran: the tree is not in a state to work on (another command holds its [tree lock](#project-structure), or a compiler binary is older than its sources), or a `maxon test` run could not happen |
| `101` | A program's leak check found an allocation still held at exit. The program reports it, not the driver; `maxon test` reports it as `LEAKED`. |

Some commands forward another process's exit code instead: [`maxon run`](#maxon-run) returns the
program's, and [`maxon upgrade`](#maxon-upgrade) the install script's. For those, only `1` is ever the
driver's own. `maxon monitor`, `maxon coverage` and `maxon profile` have their own tables on
[Debugging and Profiling](#debugging-and-profiling).

### Environment Variables

**Read by the compiler:**

| Variable | Read by | Effect |
|----------|---------|--------|
| `MAXON_RUN_CACHE_ROOT` | `run`, `build` (manifest) | Root directory of the run cache |
| `LOCALAPPDATA`, then `TEMP` | `run`, `build` (manifest) on Windows | Run cache root when `MAXON_RUN_CACHE_ROOT` is unset |
| `TMPDIR` (then `/tmp`) | `run`, `build` (manifest) elsewhere | Run cache root when `MAXON_RUN_CACHE_ROOT` is unset |
| `NO_COLOR`, `TERM` | `test --color=auto` | Set `NO_COLOR`, or `TERM=dumb`, to turn colour off |
| `MAXON_IMAGE` | `upgrade` | Marks the container image; `upgrade` refuses and names `docker pull` |
| `MAXON_INSTALL` | `upgrade` (written, not read) | `upgrade` sets it for the install script to the install the running compiler sits in, whatever your shell says |

**Read by a compiled program at run time:**

| Variable | Effect |
|----------|--------|
| `MAXON_MAX_PROCS` | The number of processors the green-thread scheduler runs on. Default: the machine's processor count. A number from 1 up sets it exactly; a larger number is capped at the machine's count; a value that is not a positive number is ignored. `Runtime.processorCount()` answers the resulting count. |
| `MAXON_PREEMPT` | `off` stops the scheduler from preempting a green thread that holds a processor, for a deliberate, reproducible run. Unset, empty or `on` is normal preemption. Any other value aborts the program at start. |

`MAXON_DEBUGSTREAM` is set by `maxon monitor` to attach a `--debugstream` program to its ring. You do not
set it yourself.

## Project Structure

A Maxon project is a directory of `.maxon` files. There is nothing you must write besides the sources:
the directory is the project. This page covers what the command-line tools read from and write into a
project. The language side of manifests is described in [Build System](LANGUAGE_REFERENCE.md#build-system),
and how files map to namespaces in [Namespaces](LANGUAGE_REFERENCE.md#namespaces).

```text
myproject/
├── build.maxon          # optional: the build manifest
├── main.maxon           # the entry point (contains main)
├── utils.maxon
├── lib/
│   ├── math.maxon
│   └── io.maxon
├── pricing.test.maxon   # tests: not part of an ordinary build
└── fixtures/
    ├── .maxonignore     # this directory is skipped
    └── sample.maxon
```

### Which files a build includes

`maxon build <directory>` compiles every `.maxon` file beneath the directory as one program, except:

1. **`build.maxon`**, the build manifest, which is a program of its own. Naming it explicitly still
   compiles it.
2. **`*.test.maxon`** files. Test sources are a separate category that only `maxon test` compiles. The
   match ignores case, so `Suite.TEST.maxon` is a test file too.
3. **Anything under a `.maxonignore`** (below).

The standard library is part of every compilation; the compiler finds it by walking up from its own
executable, so nothing in the project refers to it. See the [Standard Library](STDLIB_REFERENCE.md).

### The build manifest

`maxon build` with no path looks for **`build.maxon`** in the current directory, compiles it, runs it,
and performs the build it describes.

**A manifest is a program, not a configuration file.** It is ordinary Maxon with the whole standard
library available, so a build can compute what it compiles (read a directory, choose by host, derive a
version from git) instead of only listing it. Its entry point is **`build`**, not `main`. It returns
`ExitCode`; a non-zero return or a crash fails the build and nothing is compiled.

```maxon
export function build() returns ExitCode
	Build.build("src", output: ".maxon/myapp", version: "1.4.2")
	return 0
end 'build'
```

The manifest program is compiled **for the host**, whatever `--target` says, because this machine runs
it. It is compiled on its own (the project's other files are not part of it) and kept in the
[run cache](#the-run-cache), never inside the project.

#### Describing a build

The manifest describes its builds by calling `stdlib/Build.maxon`, which prints them as JSON on
stdout. The compiler reads that JSON back.

| Call | Meaning |
|---|---|
| `Build.build(source, output:, debugInfo:, version:, defines:)` | Build one file or directory to one output, and print it. The common case. |
| `Build.target(name, source:, output:, debugInfo:, version:, defines:)` | Return one **named** target, for a manifest that describes several. Prints nothing. |
| `Build.buildTargets(targets)` | Print several named targets (a `BuildConfigArray`). |
| `Build.buildWithConfig(config)` | Print one `BuildConfig`, which can list several sources, compiled as one program in order. |

`debugInfo` defaults to `true`, `version` to `""` and `defines` to an empty list. The keys the driver
reads from the JSON are:

| Key | Type | Meaning |
|-----|------|---------|
| `name` | string | What `maxon build <name>` selects. `Build.build` sets it to the source path. |
| `output` | string, required | Where the executable goes, without the extension. The compiler adds `.exe` for Windows, `.wasm` for `wasm32-wasi`, and nothing for Linux and macOS. Relative to the current directory. |
| `sources` | list of strings, required | The files and directories to compile, in order. An empty list is refused. |
| `debug_info` | `true` or `false` | Whether to write the `.mxdbg` sidecar (default `true`). |
| `version` | string | A dotted version stamped into the binary: a `VS_VERSIONINFO` resource on Windows and `LC_SOURCE_VERSION` on macOS. Linux and `wasm32-wasi` binaries carry no product version. Without it, the binary reports `0.0.0.0`, and a missing component is 0. A component that is not a number is refused on every target, and one the target's field cannot hold is refused too: each Windows component holds 0 to 65535 (four at most); on macOS the first holds 0 to 16777215 and the next four 0 to 1023. |
| `defines` | list of `name=value` strings | The same as [`--define=`](#defines) on the command line. |

A field that is present but malformed is **refused** rather than guessed at, naming the key, for
example ``error: build.maxon's `sources` is not a list of strings``. Output that is not JSON at all is
refused with what the manifest printed.

#### Named targets

```maxon
export function build() returns ExitCode
	var targets = BuildConfigArray.create()
	targets.push(Build.target("app", source: "src", output: ".maxon/app"))
	targets.push(Build.target("tool", source: "tools/gen.maxon", output: ".maxon/gen"))
	Build.buildTargets(targets)
	return 0
end 'build'
```

```bash
maxon build          # with one target, builds it; with several, lists their names and exits 1
maxon build app      # builds the target named "app"
```

A target name **outranks a path of the same spelling**: `maxon build app` builds the target even if a
directory `app/` exists. A word no target declares falls back to being a path, so a manifest never
breaks `maxon build some/file.maxon`. Two or more positionals are always paths.

#### The command line wins

| Command line | Manifest | Result |
|--------------|----------|--------|
| `-o <path>` | `output` | The command line's path |
| `--define=<name>=<value>` | `defines` | Both apply; for the same name, the command line's value wins |
| `--no-debug-info` | `debug_info` | Either one can turn the sidecar off; neither can force it on |
| `--target=<cpu>-<os>` | *(no key)* | The built program uses the command line's target; the manifest program itself is always built for the host |

#### Rebuilding a running compiler

A manifest whose output is the compiler running the command (such as the Maxon repository's own) can
still build. Once the compile succeeds, the compiler renames its running image to `maxon.previous`
(`maxon.previous.exe` on Windows) and writes the new binary into the empty slot. If an older
`maxon.previous` is itself still running, for example an editor's language server, it is renamed aside to
`maxon.retired-<stamp>` and deleted by a later rebuild. A **failed** build leaves the running compiler in
place.

### Ignoring directories

Place a `.maxonignore` file in a directory to exclude it, and everything beneath it, from builds,
`maxon test` discovery and `maxon fmt`. The file is a flag; its contents are never read. A marker in any
directory above a path excludes that path too.

The marker means "do not sweep me into somebody else's program", not "this file may not be compiled":
**naming a file outright overrides it**. `maxon build fixtures/sample.maxon` compiles that file, and
`maxon fmt fixtures/sample.maxon` formats it. Naming a marked *directory* compiles nothing.

### The `.maxon/` directory

`.maxon/` holds a project's build products and is safe to delete or ignore in version control:

- `maxon test` stages its build in `<project>/.maxon/test/`, with its own `.maxonignore`.
- Manifests conventionally write their outputs there (`output: ".maxon/myapp"`), and the compiler
  creates the output directory if it is missing.

A plain `maxon build <directory>` without `-o` does not use `.maxon/`: it writes the executable into the
directory, named for it (`maxon build app` writes `app/app.exe` on Windows). Pass `-o` or use a manifest to
choose the location.

### The tree lock

Two commands writing the same output directories at once would corrupt each other's work, so some
commands take a lock on the **checkout** they write into: the nearest directory above the path that
holds a `stdlib/` directory (a Maxon source checkout or install). A project with no `stdlib/` above it
takes no lock.

The lock is the file `.maxon-tree.lock` at that root. It is taken by `spec-test`, `scale-test`, and by a
`build` of a directory without `-o`. `run`, `test`, `fmt` and builds with `-o` take none.

A command that finds the lock held prints what holds it and exits **2** without doing anything:

```text
error: this checkout is BUSY — another maxon command holds its tree lock, and two of them in one tree corrupt each other's output directories. Nothing was run.
```

A live holder refreshes the lock every 5 seconds. A lock untouched for 60 seconds is treated as
abandoned: the next command breaks it with a warning and proceeds.

## Debugging and Profiling

Maxon has **no interactive debugger**: there are no breakpoints, no stepping and no attaching to a
running process. The tools on this page are what exists: debug information beside every binary, panic
backtraces, a leak check, a trace monitor, a sampling profiler and code coverage.

The examples below use output from real runs; addresses, counts and timings vary from build to build.

### The `.mxdbg` debug-info sidecar

Every `maxon build` writes a sidecar named after the full output file, beside it, unless you pass
`--no-debug-info` or the manifest sets `debugInfo: false`:

```text
[CMP] INFO: Wrote 35598 bytes of debug info to app.exe.mxdbg
[CMP] INFO: Wrote 103992 bytes of code to app.exe (compiled in 0.3 s)
Compiled -> app.exe
```

On Linux and macOS the executable is `app` and the sidecar `app.mxdbg`. No sidecar is written for
`wasm32-wasi`. The executable is **byte-identical** with or without the sidecar, so the binary you debug is
the binary you ship.

The sidecar maps machine code back to the source. It records the target and a **build id** (a hash of
the executable's code section), then the source files, functions with their frame size and local
variables, types, the line table and inlining records. `maxon debug` prints it; `maxon profile` and
`maxon coverage` read it, and a `--coverage` build requires it.

### Panics and backtraces

A runtime panic (a failed range check, an explicit `panic`, an out-of-bounds access) prints the message
with its source position, then the call chain, to stderr, and the program exits with code **1**:

```text
panic at app.maxon:21: Range check failed: value outside typealias 'Byte'
Stack trace:
  in narrow
  in main
  in mrt_start
```

Frames are listed innermost first, by function name only, down to the program's start (`mrt_start`).
At most 100 frames are printed; a deeper chain
ends with an `...additional frames elided...` line. A processor fault has no source position, for example
`panic: nil pointer or invalid memory access` or `panic: stack overflow`, followed by the same stack trace.

### The leak check

A program that allocates checks, after `main` returns, that every heap allocation was released. If one
was not, the program prints nothing and exits with code **101** instead of the code `main` returned. Maxon
releases memory itself, so there is no call you forgot to make: exit 101 points at the memory management
the compiler emitted. `maxon test` reports such a test as `LEAKED`, and
[`maxon monitor --filter=mm`](#maxon-monitor) shows every allocation and free.

### `maxon debug`

Reads the `.mxdbg` sidecar back. Both forms are read-only and accept either the executable or the sidecar
itself.

```bash
maxon debug --dump-info <exe|.mxdbg> [header|files|functions|types|lines|statements|inline]
maxon debug --symbolize <exe|.mxdbg> <codeOffset...>
```

| Option | Description |
|--------|-------------|
| `--dump-info` | Print the sidecar, or only the sections named after the path |
| `--symbolize` | Resolve offsets into the executable's code section to `file:line:col` |

`maxon debug` with no arguments prints the usage above and exits 1.

**Sections** of `--dump-info` (with none named, all are printed):

| Section | Contents |
|---------|----------|
| `header` | The file, target and build id |
| `files` | The source files, including the standard-library and runtime files the program uses |
| `functions` | Each function's code range, frame size, parameter, line and local counts, and each local's location (a frame slot, a register or `<optimized out>`) and type |
| `types` | Each type's kind, size, alignment and fields |
| `lines` | The line table: code offset and source position |
| `statements` | The same table, source positions only |
| `inline` | Inlined call sites and the code ranges they occupy |

```text
$ maxon debug --dump-info app.exe header
Debug info: app.exe
  target:   x64-windows
  build-id: 0xc9c1ee8ee7dd71e0

$ maxon debug --dump-info app.exe functions
  functions (136):
    mrt_start                        [0x0000, 0x003b)  frame=0x20  params=0  lines=0  locals=0
    worker                           [0x0060, 0x00de)  frame=0x28  params=1  lines=4  locals=1
        base                 reg3  : Integer
    main                             [0x00e0, 0x053d)  frame=0x88  params=0  lines=50  locals=3
        points               [rbp-0x60]  : <generic instance>

$ maxon debug --dump-info app.exe lines
  line table (390):
    0x00c9  app.maxon:16:8  [statement]
    0x0151  app.maxon:25:15  [statement]
```

A word that is not a section is refused before the file is read:

```text
maxon debug --dump-info: 'bogus' is not a section (header|files|functions|types|lines|statements|inline).
```

**`--symbolize`** takes offsets in decimal or `0x`-prefixed hex and prints one line each. An offset
before the function's first statement prints `<no line>`. An unreadable offset is reported and the
command exits 1 after printing the others.

```text
$ maxon debug --symbolize app.exe 0x00e0 0x0151 zz
0x00e0  <no line>  (in main)
0x0151  app.maxon:25:15  (in main)
Not a code offset: 'zz' (use decimal or 0x-prefixed hex).
```

**The build id.** Given an executable, `maxon debug` checks that the sidecar beside it was written by the
same build, and refuses a stale one with exit 1:

```text
maxon debug: the .mxdbg sidecar describes a different build of this binary — rebuild it
```

Given the `.mxdbg` path directly, it prints the sidecar without that check. A missing sidecar is refused
with exit 1, naming the path it looked for and `--no-debug-info` as the likely cause. `maxon debug` reads
PE, ELF and Mach-O executables whatever the host, so it works on a cross-compiled binary.

### `maxon monitor`

Launches an executable built with `--debugstream` and prints the trace events it writes into a
shared-memory ring. Each event line is prefixed with the time since start as `[+SSSS.mmm]`. The monitor
creates the ring and passes its name to the program in the `MAXON_DEBUGSTREAM` environment variable.
The program's own stdout and stderr pass through unchanged.

```bash
maxon monitor [--filter=mm|sched|log] <exe> [args...]
```

Everything after the executable is the program's command line and reaches it untouched.

| Option | Description |
|--------|-------------|
| `--filter=mm` | Memory-manager events only: `mm_alloc`, `mm_free`, `mm_incref`, `mm_decref` and related |
| `--filter=sched` | Green-thread events only: `sched_spawn #N`, `sched_await #N` (the thread awaited), `sched_yield #N` and `sched_resume #N` (around `sleep` and `Runtime.yield()`), `io_yield #N` and `io_resume #N` (around a blocking I/O operation). `N` is the thread's number, the same one `--async-trace` prints. |
| `--filter=log` | Only the events the program emitted through the `__DebugStream` builtin |

With no `--filter`, every family is printed. An unrecognized value is refused with the usage line.

```text
$ maxon build app.maxon --debugstream
$ maxon monitor --filter=mm app.exe
[+0000.015] mm_alloc ArrayRecord #1 size=48
[+0000.015] mm_alloc Point #2 size=16
[+0000.015] mm_alloc ElementBuffer #3 size=32
...
points=1000 worker=42
[debugstream] 3063 events, 0 dropped, peak buffer: 0.0 MB / 2.0 MB (2%)
```

Events go to stdout; the closing `[debugstream]` summary goes to stderr.

A binary built without `--debugstream` is refused before it starts:

```text
maxon monitor: could not read the interned name tables out of the target (app.exe has no .symtab section)
```

**Exit codes:** the program's own exit code (a panicking program's `1` included), except `1` for a
command line `monitor` cannot act on (no executable, no such file, an unknown filter, a binary built
without `--debugstream`) and `3` for a trace this compiler cannot decode, which asks you to rebuild the
program.

**Emitting your own events.** A `--debugstream` build enables the `__DebugStream` builtin, which puts
events from your code into the same ring. Without `--debugstream`, every call compiles to nothing; with no
monitor attached, `__DebugStream.enabled()` is `false`.

| Call | Event | Notes |
|------|-------|-------|
| `__DebugStream.enabled()` | — | `true` when a monitor is attached, so a caller can skip building a message nobody reads |
| `__DebugStream.nameId("phase")` | — | Interns a name at compile time and returns its id. The argument must be a string literal. |
| `__DebugStream.phaseBegin(nameId, unitId)` | `log_phase_begin` | Opens a nested span |
| `__DebugStream.phaseEnd(nameId, unitId)` | `log_phase_end` | Closes it |
| `__DebugStream.event(nameId, cat, lvl, unitId, arg0, arg1)` | `log_event` | A name and two numbers; allocates nothing, so it is safe on a hot path |
| `__DebugStream.text(cat, lvl, unitId, message)` | `log_text` | A UTF-8 message, truncated at 64 KiB |

Each event line names the green thread and processor that emitted it (`gt=0x0 P0`).

### `maxon profile`

Launches a program and samples where its CPU time goes. The program needs no instrumentation and no
rebuild: only its `.mxdbg` sidecar is read, so you measure the binary you ship. The program's own output
goes to stderr, so stdout is only the report.

**x64-windows only.** Sampling suspends the program's threads and reads their x64 register state. A
compiler for any other host refuses the command.

```bash
maxon profile run <exe> [--json|--folded] [--rate=<hz>] [--min-percent=<share>] [--timeout=<seconds>] [--target-env=<NAME>=<VALUE>]... [args...]
```

| Option | Description |
|--------|-------------|
| `--json` | Emit the full report as JSON instead of a summary |
| `--folded` | Emit collapsed stacks (`root;child;leaf <count>`), the format flamegraph.pl, inferno and speedscope read. `--json` and `--folded` together are refused. |
| `--rate=N` | Samples per CPU-second a thread consumes (default 1000, from 1 to 10000). A thread is charged by the CPU time it used, so an idle thread costs almost nothing. The report shows the rate achieved beside the one requested. |
| `--min-percent=N` | Hide rows below this share of the run from the printed tables (default 1). The number hidden is always reported. |
| `--timeout=S` | Stop the program after `S` seconds (default 600) and report what was sampled, marked `PARTIAL` |
| `--target-env=N=V` | Set a variable in the profiled program's environment (repeatable). `--target-env=MAXON_MAX_PROCS=1` pins the scheduler's processor count, which makes a green-thread profile reproducible. |

```text
$ maxon profile run busy.exe
profile: busy.exe
target exit code: 0
samples: 2204 from 2060 captures at 1000 Hz over 2.3066124s (requested 1000 Hz)
unsymbolized: 2 samples (0.1%)
stacks: 0 truncated, 1 unreadable

=== hot functions (self time) ===
 99.5%     2193  mix
  (4 functions below 1% not shown)

=== call tree ===
 99.9%     2202  mrt_start
 99.9%     2202    main
 99.9%     2202      spin
 99.5%     2193        mix
  (1 nodes below 1% not shown)

=== stacks ===
  one row per stack that ran — an OS thread's or a green thread's — named by its outermost frame
 99.9%     2202  mrt_start
  (1 stacks below 1% not shown)
```

```text
$ maxon profile run busy.exe --folded
[ntdll.dll] 1
mrt_start;main;spin 10
mrt_start;main;spin;mix 2194
```

`--json` prints one object with `exe`, `targetExitCode`, `runCompleted`, `achievedRateHz`,
`requestedRateHz`, `samples`, `captures` and related counts, and the arrays `functions`
(`name`, `selfSamples`, `totalSamples`), `tree` and `stacks`.

**Exit codes:** `0` when the measurement completed. `1` for a refusal (no such executable, no sidecar, a
sidecar from a different build, a program that ended before sampling began) and for a run stopped by
`--timeout`, whose partial report is still printed. The program's own status is the `target exit code`
line.

### `maxon coverage`

Reads back what a `--coverage` build counted, as line and branch coverage. The instrumented program
writes its counters to `<exe>.mxcov` as it exits.

```bash
maxon coverage <run|report> <exe> [--json] [--data=<file>] [--timeout=<seconds>] [args...]
```

- **`run`** deletes the previous counter file, launches the program, then reports on the counters it
  wrote. The program's output goes to stderr, so stdout is only the report. Arguments after the
  executable are the program's.
- **`report`** reports from counters an earlier run wrote, and launches nothing.

| Option | Description |
|--------|-------------|
| `--json` | Emit the report as JSON instead of an annotated source listing |
| `--data=F` | Read counters from `F` instead of `<exe>.mxcov`. For `report` only; `run` refuses it. |
| `--timeout=S` | Kill the program after `S` seconds (default 600) and report nothing. For `run` only. |

```text
$ maxon build cov.maxon --coverage
$ maxon coverage run cov.exe
coverage: cov.exe
target exit code: 0
lines: 5/6 covered  branches: 1/2 arms taken  eliminated: 0 lines
legend: a count = executed, ##### = never executed, ----- = no code emitted (optimized away), blank = no coverage point

=== cov.maxon ===
        |   3| function classify(n Count) returns String
      3 |   4| 	if n > 5 'big'
  ##### |   5| 		return "big"
        |   6| 	end 'big'
        |   7| 
      3 |   8| 	return "small"
...
=== branches ===
  cov.maxon:4:2  then@4=0  else(implicit)@4=3
```

`maxon coverage report cov.exe` prints the same report without the `target exit code` line. With
`--json` the report is one object with `exe`, `runCompleted`, `linesCovered`, `linesInstrumented`,
`linesEliminated`, `armsTaken`, `armsInstrumented`, and `files` (each line's `state` and `count`) and
`branches` arrays.

**Exit codes:** `0` when a report is produced, `1` for a refusal, such as a binary built without
`--coverage`:

```text
coverage: app.exe was not built with coverage instrumentation — it has no coverage points to report on
```

The program's own status is the `target exit code` line.

### Tracing green threads

A build with `--async-trace` writes one line to stderr each time a green thread is spawned, sleeps,
waits on I/O, resumes, or is awaited. Without the flag, no trace code is emitted.

```text
$ maxon build app.maxon --async-trace
$ ./app.exe
spawn #1
sleep_yield #1
sleep_resume #1
await #1 [yield]
points=1000 worker=42
```

| Line | Meaning |
|------|---------|
| `spawn #N` | Green thread `N` was started |
| `sleep_yield #N`, `sleep_resume #N` | It gave up its processor to sleep, and resumed |
| `io_yield #N [kind]`, `io_resume #N [kind]` | It waited on I/O (for example `[file_exists]`, `[net_connect]`, `[net_recv]`), and resumed |
| `await #N [yield]` | It was awaited and the caller had to wait |
| `await #N [immediate]` | It was awaited after it had already finished |
| `try_await #N` | The same, for a `try await` |

### Investigating a running program

1. **A panic:** read the backtrace, then map positions with `maxon debug --dump-info <exe> lines` or
   `maxon debug --symbolize`.
2. **Green threads that stall or run out of order:** build with `--async-trace`.
3. **Memory:** build with `--debugstream` and run `maxon monitor --filter=mm`. Exit code 101 is the leak
   check.
4. **Slow:** `maxon profile run` (x64-windows), or `--folded` into a flame graph.
5. **Untested code:** build with `--coverage` and run `maxon coverage run`.
6. **What the compiler emitted:** `maxon build --emit-ir`.

## Editor Support

Maxon's editor support is the compiler itself: `maxon lsp-server` is a Language Server Protocol server,
and the VS Code extension runs it.

### VS Code

Install **Maxon** from the
[Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=maxon-lang.maxon-lsp-client)
or [Open VSX](https://open-vsx.org/extension/maxon-lang/maxon-lsp-client) (extension id
`maxon-lang.maxon-lsp-client`). It activates in a workspace containing `.maxon` files and provides syntax
highlighting, diagnostics, hover, completion, go-to-definition, rename, formatting, the Compiler
Explorer and a Test Explorer.

**Finding the compiler.** The extension runs `maxon lsp-server` from the first compiler it finds:

1. the `maxon.serverPath` setting, when it points at an executable;
2. `maxon` on `PATH`;
3. `$MAXON_INSTALL/bin/maxon` when `MAXON_INSTALL` is set, otherwise `~/.maxon/bin/maxon` (the install
   script's default location);
4. `maxon-bin/.maxon/maxon` in the first workspace folder, which is where a Maxon source checkout builds
   its compiler.

If none is found, the extension offers to **Install** Maxon with the install script or to **Locate…** a
compiler, which it saves to `maxon.serverPath`. When the compiler binary changes on disk (for example
after an upgrade or a rebuild), the extension restarts the language server.

**Settings:**

| Setting | Default | Description |
|---------|---------|-------------|
| `maxon.serverPath` | `""` | Absolute path to the `maxon` compiler. Empty means search as above. |
| `maxon.formatting.insertSpaces` | `false` | Indent formatted code with spaces instead of tabs |
| `maxon.formatting.tabSize` | `2` | Spaces per indent level, when `insertSpaces` is `true` |

The formatting settings replace the editor's own tab settings for Maxon files. The extension also turns
on format-on-save and semantic highlighting for Maxon files by default.

**Commands:**

| Command | What it does |
|---------|--------------|
| **Maxon: Restart Language Server** | Restart `maxon lsp-server` |
| **Maxon: Open Compiler Explorer** | Focus the Compiler Explorer view |

**Compiler Explorer.** A view in the Maxon activity-bar container with a **Source** pane and a
**Target IR** pane. Half a second after you stop typing, it compiles the source for the host and shows
the lowered Target IR, the same text `maxon build --emit-ir` writes, or the compile errors as
`Line <line>:<column>: <message>`. Nothing is written to disk. The source is treated as a whole program,
so it needs a `main`.

**Test Explorer.** The **Maxon Tests** controller lists the `test` declarations in the workspace's
`*.test.maxon` files, one node per file, and runs them with the compiler the extension found, one
`maxon test <project> --json` per project the selection touches. A test file's project is the highest
directory above it, never above the workspace folder, whose every level holds a `.maxon` source. When the
workspace folder is the Maxon source checkout, a second controller, **Maxon Spec Suite**, lists the spec
tests in `specs/*.md` and runs them with the checkout's own compiler
(`maxon-bin/.maxon/maxon spec-test --filter=…`).

### `maxon lsp-server`

```bash
maxon lsp-server
```

Speaks the Language Server Protocol over stdin and stdout: JSON-RPC messages with `Content-Length`
headers. It takes no arguments and is normally started by an editor, not by hand.

**Lifecycle.** `initialize` returns the server's capabilities; `initialized` is accepted. `shutdown`
answers `null`, and `exit` ends the process with code **0** after a `shutdown` and **1** otherwise (as
it does when stdin closes or a message cannot be framed).

**Documents.** Text synchronization is **full**: every `textDocument/didChange` carries the whole
document. The server handles `didOpen`, `didChange` and `didClose`. Each buffer is analysed **on its own**
together with the standard library, from its in-memory text, for the host target; other files of the
project are not read, so a call into another file of the project is not resolved in the editor.

**Diagnostics** are published with `textDocument/publishDiagnostics` after every `didOpen` and
`didChange`, and cleared on `didClose`. Each has the error code (for example `E3005`) as `code`,
`source: "maxon"` and severity Error. "No `main` function" (E3001) is not reported, because a single
buffer is not a whole program.

**Requests served:**

| Method | Result |
|--------|--------|
| `textDocument/hover` | Markdown: the declaration as written in a `maxon` code block, its `///` doc comment, and for a variable or parameter what it is and its type. Keywords and math intrinsics are described too. |
| `textDocument/definition` | The declaration of the name under the cursor, **in the same document** |
| `textDocument/completion` | Members after `.` (the trigger character): fields, methods, static functions and enum cases, for a type name or a local whose type is evident. There is no completion of bare identifiers. |
| `textDocument/formatting` | One edit replacing the whole document with `maxon fmt`'s layout, or `null` when it is already formatted. `insertSpaces: false` indents with tabs; `true` indents with `tabSize` spaces. |
| `textDocument/documentSymbol` | The top-level declarations: functions, types, enums, unions, interfaces and extensions |
| `textDocument/foldingRange` | Every block spanning more than one line |
| `textDocument/linkedEditingRange` | A block's opening name and its `end '…'` label, edited together |
| `textDocument/rename` | Renames a declaration's name and its matching `end '…'` label. References elsewhere are not renamed. |
| `textDocument/codeAction` | A quick fix, "Remove unused variable", for a local `let`/`var` whose name appears nowhere else |
| `textDocument/semanticTokens/full` | Semantic tokens with the legend `keyword`, `type`, `struct`, `enum`, `interface`, `function`, `method`, `variable`, `parameter`, `modifier`, `enumMember`, `property`, `string`, `number`, `comment`, `operator` and no modifiers |

Any other request is answered with JSON-RPC error `-32601` (method not found).

**`maxon/generateIR`** is a Maxon-specific request, not advertised in the capabilities, that compiles a
source text in memory for the host and returns its Target IR. It is what the Compiler Explorer uses.

```json
{ "source": "function main() returns ExitCode\n\treturn 0\nend 'main'\n", "filename": "explorer.maxon" }
```

Both params are required (otherwise `-32602`). The result:

```json
{ "ir": "…", "errors": [ { "message": "…", "line": 1, "column": 1 } ] }
```

`ir` is the text `maxon build --emit-ir` writes, and is empty when the compile fails. `errors` lists
every diagnostic with a **1-based** `line` and `column`.

**`maxon/listProjects`** is a Maxon-specific request, not advertised in the capabilities, that lists the
projects the server holds. It is what the VS Code status bar shows. It takes no params. Each open document
is a project of its own, because each is analysed on its own:

```json
{ "projects": [ { "rootPath": "/home/me/app/main.maxon", "isSingleFile": true, "fileCount": 1 } ] }
```

`rootPath` is a filesystem path, not a URI, and the projects are listed in path order.

### Other editors

Any editor with an LSP client can use the server. Configure it to start the command `maxon` with the
argument `lsp-server` over stdio for files with the `.maxon` extension (language id `maxon`), and to
send full-document synchronization.

## Targets

`maxon` compiles for five targets. Without `--target`, it compiles for the **host** it runs on.

| Target | Output | Extension |
|--------|--------|-----------|
| `x64-windows` | PE executable | `.exe` |
| `x64-linux` | Static ELF executable, no libc | none |
| `arm64-linux` | Static ELF executable, no libc | none |
| `arm64-macos` | Mach-O executable, ad-hoc signed | none |
| `wasm32-wasi` | WASI Preview 2 component | `.wasm` |

`maxon version` prints the host target. A spelling that names none of them is refused and the command
exits 1:

```text
error: unknown --target 'x86-linux' (expected one of: x64-windows, x64-linux, arm64-macos, arm64-linux, wasm32-wasi)
```

### Cross-compiling

`--target=` makes the compiler **cross-compile** the program: the compiler itself still runs on the
host. Any host can build for any target, with no extra toolchain for the four native targets: the
compiler writes the executable, including the macOS code signature, itself.

Commands that take `--target=`:

| Command | What the target changes |
|---------|-------------------------|
| [`maxon build`](#maxon-build) | The executable it writes |
| [`maxon test`](#maxon-test) | The test binary, which it then runs; on a host that cannot execute that target, every test is reported as not run |
| [`maxon spec-test`](#maxon-spec-test) | Each spec test, run under the checkout's vendored runtime where needed |

`maxon run` always builds for the host, and so does a `build.maxon` manifest program (the builds it
describes follow `--target`). An executable copied from Windows to a Linux or macOS machine may need
`chmod +x` before it runs.

### Running a `wasm32-wasi` program

The output is a WASI Preview 2 component. Run it under a runtime that supports components, such as
[wasmtime](https://wasmtime.dev/), enabling the `cli-exit-with-code` interface the component imports so
its exit code is delivered:

```bash
maxon build app.maxon --target=wasm32-wasi     # writes app.wasm
wasmtime run -S cli-exit-with-code=y app.wasm
```

Without `-S cli-exit-with-code=y`, wasmtime refuses to start the component.

Building a component needs two tools the compiler calls: `wasm-tools`, and the WASI WIT package. The
compiler looks for them as `vendor/wasm-tools/` and `vendor/wasi-wit/` in the working directory and the
nine directories above it. A Maxon source checkout stages them with `scripts/fetch-vendor.sh`; an
installed compiler does not include them. A build that cannot find one is refused with error E6005, naming
what is missing and where it looked, before anything is compiled; a `wasm-tools` step that fails is refused
with the same code and what the step printed.

### What each target supports

`x64-windows`, `x64-linux`, `arm64-linux` and `arm64-macos` support the whole standard library. The
differences a program can meet are:

- **`maxon profile`** runs only on x64-windows. Elsewhere it is refused.
- On the Linux targets, host-name resolution for sockets is built in and simple: `A` records only, the
  first nameserver in `/etc/resolv.conf`, no search domains and no CNAME following.

**`wasm32-wasi`** runs heap-allocated values, strings, `print`, structs, arrays, closures, interfaces and
floating point. It does not provide these, and a program that reaches one is **refused at compile
time**, at the call:

| Not available on `wasm32-wasi` | Refused with |
|--------------------------------|--------------|
| `async`/`await`, green threads, services, `sleep`, `Runtime.yield`, `Runtime.processorCount` | E3104 |
| Clocks (`Clock`, current time, CPU ticks) | E3104 |
| Command-line arguments | E3104 |
| File and directory I/O | E3104 |
| Sockets | E3104 |
| Reading console stdin | E3104 |
| Process information, priority, CPU count | E3104 |
| Subprocesses | E3074 |
| Environment variables (`Process.environmentVariable`) | E3074 |

```text
error E3104: app.maxon:3:2: 'sleep' lowers to the runtime entry '__gt_sleep', which has no wasm32-wasi implementation
error E3074: app.maxon:5:17: Subprocess is not supported on wasm32-wasi (no process-spawn primitive); guard the call with #if not os(Wasi).
```

To keep one source for several targets, guard the unsupported part with `#if not os(Wasi)`.

Build options that need a host facility are refused on `wasm32-wasi` before anything is compiled:
`--debugstream` (needs shared memory and an uptime clock) and `--coverage` (needs file output).
`maxon test --color=auto` cannot detect a terminal there and prints no colour.

On Windows, a program's output to a console is converted from UTF-8 as it is written, so non-ASCII text
displays correctly; output to a pipe or file is the program's UTF-8 bytes unchanged. The console's code
page is never modified.

## MCP Server

Maxon includes a **Model Context Protocol (MCP)** server built into the compiler binary:

```bash
maxon mcp-server
```

Any MCP-compatible client (Claude Desktop, Claude Code, Cursor, Antigravity, VS Code and others) can use
it to build, run, test, format and inspect Maxon code through structured tools, instead of shell
commands. There is nothing else to install: the server is a command of every `maxon` binary.

### Quick setup

Add the server to your client's configuration.

**Claude Desktop:** `claude_desktop_config.json` (in `%APPDATA%\Claude\` on Windows,
`~/Library/Application Support/Claude/` on macOS):

```json
{
  "mcpServers": {
    "maxon": {
      "command": "maxon",
      "args": ["mcp-server"]
    }
  }
}
```

**Cursor, Claude Code and Antigravity:** `.cursor/mcp.json` or `.mcp.json` in your project root, with
the same content.

**Other clients:** configure a stdio server with command `maxon` and arguments `["mcp-server"]`.

### `maxon mcp-server`

```bash
maxon mcp-server         # standard mode
maxon mcp-server --dev   # also exposes the compiler-development tools
```

| Option | Description |
|--------|-------------|
| `--dev` | Enable the tools for working on the Maxon compiler: `run_spec_test`, `run_scale_test`, `spec_test_outcome`, and the `repoRoot` and `from` arguments of `build` |

The server reads newline-delimited JSON-RPC 2.0 messages on stdin and writes responses to stdout. It
implements `initialize` (protocol version `2024-11-05`, server name `maxon`), `tools/list` and
`tools/call`.

| Mode | Invocation | Tools |
|------|------------|-------|
| Standard | `maxon mcp-server` | 8: `build`, `run`, `test`, `fmt`, `check`, `dump_ir`, `lookup_error_code`, `info` |
| Developer | `maxon mcp-server --dev` | 11: the standard tools plus `run_spec_test`, `run_scale_test`, `spec_test_outcome` |

**An argument a tool does not declare is refused** with `invalidParams`, never ignored, so the arguments
listed below are exactly the ones that exist. A developer-mode argument sent to a standard-mode server is
refused the same way, as is an argument of the wrong JSON type.

Tools that run a compiler command answer with a JSON object holding `success`, the `command` that ran,
`exitCode`, `stdout` and `stderr`; `check` and `dump_ir` compile inside the server and answer as described
under each. Every tool acts in the server's working directory, which is normally your project.

### Standard tools

#### `build`

Compiles a source file, a directory, a manifest target or an inline snippet, as `maxon build` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | Source file or project directory. Omitted, the working directory's `build.maxon` runs. |
| `source` | string | Inline Maxon source to build instead of a path. Give `path` or `source`, not both. |
| `output` | string | Output executable path (`-o`) |
| `target` | string | A target such as `wasm32-wasi` (a value containing `-` is passed as `--target=`), or the name of a target declared in `build.maxon` (a bare word) |
| `emitIr` | boolean | Also write the Target IR (`--emit-ir`) |

#### `run`

Compiles, or reuses a cached build of, a program and runs it, as `maxon run` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | The `.maxon` file or directory to run |
| `source` | string | Inline Maxon source to compile and run. Give `path` or `source`. |
| `arguments` | array of strings | Command-line arguments for the program |

The answer carries the program's exit code, stdout and stderr.

#### `test`

Runs a project's `test` declarations, as `maxon test` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | Project directory (default: the working directory) |
| `filter` | string | Selects tests by name or file: case-insensitive, comma-separated patterns are a union |

#### `fmt`

Formats Maxon source, as `maxon fmt` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | File or directory, rewritten **in place**. Omitted, the whole working directory is formatted. |
| `source` | string | Inline source to format. Nothing is written; the result's `formatted` field holds the text. Give `path` or `source`, not both. |

#### `check`

Compiles a program for the host, as `maxon build` would, and writes nothing: no executable and no
sidecar. The answer's `success` says whether it compiled, and `diagnostics` holds what `maxon build` would
have printed on stderr, one `error E…` line per problem. The compile runs inside the server process, not
in a child `maxon`, so a compiler panic ends the server.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string, required | The `.maxon` file or directory to check |

#### `dump_ir`

Compiles a program for the host, as `maxon build` would, and answers its Target IR in `ir`: the text
`maxon build --emit-ir` writes. Nothing is written. `success` and `diagnostics` are as for `check`, and
`ir` is empty when the compile fails. Like `check`, it compiles inside the server process, so a compiler
panic ends the server.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string, required | The `.maxon` file or directory to compile |

#### `lookup_error_code`

Looks up a compiler error code.

| Argument | Type | Description |
|----------|------|-------------|
| `code` | string or integer, required | `3014`, `"3014"`, `"E3014"`, or the registry case name `"semanticUnneededCast"` |

The answer gives the code, its registry name, the compilation stage its leading digit names, and its
documentation. The name and stage come from the registry compiled into the binary, so every install
answers them. The documentation is read from the compiler's source when the compiler sits in a source
checkout; otherwise `documentationAvailable` is `false`. A number no error code claims is refused.

#### `info`

Takes no arguments. Returns `name`, `version`, `commit`, `commitDate`, `executable` (the running
compiler's path) and `hostTarget`.

### Developer tools (`--dev`)

These tools are for working on the Maxon compiler itself, in a checkout of its repository.

#### Which tree, and which compiler

One server can serve several checkouts or worktrees, so every developer tool, and `build` in developer
mode, takes a `repoRoot` argument:

| Argument | Type | Description |
|----------|------|-------------|
| `repoRoot` | string | Absolute path of the Maxon checkout to act in. Defaults to the checkout the server's own compiler sits in. |

- `repoRoot` must be **absolute**. A relative path, or a directory that is not a Maxon checkout, is
  refused, never replaced by another tree.
- A tool acting on a tree runs **that tree's own compiler**, `<repoRoot>/maxon-bin/.maxon/maxon`, because
  the compiler finds its standard library by walking up from its own executable. A tree whose compiler
  has not been built is refused, naming the `build` tool.
- **Every answer echoes the `repoRoot` it used**, on success and on refusal.

In developer mode `build` also accepts:

| Argument | Type | Description |
|----------|------|-------------|
| `repoRoot` | string | The checkout to build in |
| `from` | string | A compiler binary to build **with**, instead of the tree's own (for a tree whose compiler slot is empty or broken) |

#### `run_spec_test`

Runs `maxon spec-test` and returns `passed`, `failed`, `total`, `summaryParsed`, `durationMs`,
`exitCode`, `memoryLeak` (exit code 101) and `rawTail` (the last 4 KiB of output).

| Argument | Type | Description |
|----------|------|-------------|
| `filter` | string | `--filter=`: one case-sensitive substring of the `<spec>/<test>` label, not a list |
| `directory` | string | Spec directory (default `specs`) |
| `updateRequired` | boolean | `--update-required`: rewrite the committed IR goldens. Always pair it with `filter`; unfiltered, it rewrites every golden. |
| `log` | string | `--log=` value, such as `ir:debug` |
| `network` | boolean | `--network`: also run the cases that reach a real external host |
| `target` | string | `--target=` value, such as `wasm32-wasi` |
| `workers` | integer | `--workers=`: the worker process count. A debugging aid; the default is what the suite normally runs at. |
| `repoRoot` | string | The checkout to run in |

#### `run_scale_test`

Runs `maxon scale-test`, the scaling instrument, and returns its whole result document as `result`. It
has no verdict: the ratio between rungs is the reading (×2 linear, ×4 quadratic).

| Argument | Type | Description |
|----------|------|-------------|
| `rungs` | integer | Rungs to climb (1–8, default 6). Each rung doubles the program. |
| `repeat` | integer | Compiles per rung (1–25, default 1). CPU takes the minimum; memory is cross-checked. |
| `note` | string | Record the run in `docs/optimization-log.md` with this text as the reason |
| `emitCorpus` | string | Write the generated programs to this directory and compile nothing |
| `log` | string | `--log=` value |
| `repoRoot` | string | The checkout to run in |

#### `spec_test_outcome`

Runs spec tests for a filter and returns a `tests` array of `{spec, test, status}` entries (`PASS` or
`FAIL`), a `failures` array of `{spec, message}`, and the counts.

| Argument | Type | Description |
|----------|------|-------------|
| `filter` | string, required | One case-sensitive substring, typically a label like `arithmetic/addition` |
| `target` | string | `--target=` value |
| `network` | boolean | Also run the cases that reach a real external host |
| `repoRoot` | string | The checkout to run in |

### Rebuilding the compiler under a running server

The server is the compiler, so building the compiler (for example with the `build` tool and
`path: "maxon-bin"`) replaces the executable the server runs from. That works: once the compile
succeeds, the running image is renamed to `maxon.previous` and the new compiler is written in its
place, and the server keeps answering from the renamed image. A server started before an earlier
rebuild is running an older `maxon.previous`; that file is renamed aside to `maxon.retired-<stamp>` and
deleted by a later rebuild once the server has exited. A failed build leaves the running compiler in the
slot.

**The running server is still the compiler it was started as.** Restart it when you want the newly
built compiler to answer.

## Working on the Compiler

These commands are for people changing the Maxon compiler, in a checkout of its repository. `maxon`
with no arguments does not list them; `maxon help` does. The contributor guide,
[Contributing](/docs/contributing/), covers building the compiler and the rules for a change.

In a checkout, run the tree's own compiler, `./maxon-bin/.maxon/maxon`, never an installed `maxon` on
`PATH`: the compiler finds `stdlib/` by walking up from its own executable, so an installed compiler would
compile the tree against the release's standard library.

### `maxon spec-test`

Runs the compiler's own spec suite, the test cases embedded in `specs/*.md`. For a project's unit tests,
see [`maxon test`](#maxon-test).

```bash
maxon spec-test [directory] [options]
```

`[directory]` is the spec directory (default `specs`). A second positional is refused. Every selected
case is compiled inside this process, so the compiler under test is the executable you ran.

| Option | Description |
|--------|-------------|
| `--filter=<pattern>` | Run only tests whose `<spec>/<test>` label contains `<pattern>`. One case-sensitive substring, not a list: a spec name selects that spec, a test name selects that test, and `spec/test` selects exactly one. |
| `--workers=<n>` | Run on `<n>` persistent worker processes (default: this machine's count, shown by `maxon help spec-test`). `1` is the same pool with one worker, not a serial mode, and output is identical for every count. |
| `--target=<cpu>-<os>` | Cross-compile each selected test for that target and run it under the vendored runtime. |
| `--network` | Also run the cases that open a socket to a real external host. A default run names every case it left out. |
| `--update-required` | Rewrite the committed IR goldens instead of checking against them. Review the diff, and pair it with `--filter`: unfiltered, it rewrites every golden. |

The run prints one line per test, then `N passed, M failed` and lines for skipped, not-run and drifted
cases. A golden whose text differs from this run's output is reported as drift and does not fail the
case; only `--update-required` rewrites one.

It refuses to start, with exit **2** and nothing run, when the compiler binary is older than the sources
it was built from, or when another command holds the checkout's [tree lock](#the-tree-lock).

```bash
maxon spec-test
maxon spec-test --filter=arrays
maxon spec-test --filter=arrays/a-pushed-element-survives-the-push
maxon spec-test --target=wasm32-wasi
maxon spec-test --filter=strings --update-required
```

### `maxon scale-test`

Measures how the compiler scales: it compiles a ladder of generated programs, each rung double the last,
and reports each phase's **memory** (allocations, frees and bytes, which are exact) and the **CPU ticks**
the compiling thread spent. It does not report wall time, which depends on everything else the machine
is doing. The ratio between rungs is the growth: ×2.00 is linear, ×4.00 quadratic.

It is an instrument, not a gate: it exits 0 whatever the numbers say, and non-zero only when the run
itself broke.

```bash
maxon scale-test [options]
```

| Option | Description |
|--------|-------------|
| `--rungs=<n>` | Climb `<n>` rungs (default 6, range 1–8). Each rung doubles the program. |
| `--repeat=<n>` | Compile every rung `<n>` times (default 1, range 1–25) and report the least CPU per phase. Memory comes from the first compile and must be identical in the rest; a difference is a broken run. |
| `--note=<text>` | Record the run as a dated row in `docs/optimization-log.md` with `<text>` as the reason. Without it, nothing is recorded. |
| `--emit-corpus=<dir>` | Write the generated programs to `<dir>` and compile nothing. |
| `--result-json=<path>` | Also write the whole run, including the change since the last recorded row, to `<path>` as JSON. |

A value out of range is refused, never clamped.

### `maxon verify-warm-rebuild` and `maxon verify-recheck`

Two checks on the compiler's incremental machinery. Each takes one path and exits 0 only when its
property holds, printing a `PASS` or failure line per property.

```bash
maxon verify-warm-rebuild <file>
maxon verify-recheck <file|dir>
```

- **`verify-warm-rebuild`** checks that the query layer is deterministic (two cold compiles agree byte
  for byte) and incremental (a rebuild reuses cached work, and each kind of edit invalidates exactly what
  it should), and that a compile reusing the memos of files an edit left alone emits exactly what a cold
  compile of the edited program emits. A file with compile errors reports them and exits 1.
- **`verify-recheck`** checks that one project can be re-checked the way an editor does: two checks of
  unchanged input agree, and a diagnostic introduced by an edit clears when the edit is undone. It takes
  `--define=<name>=<value>` as `build` does, so a define is checked on every re-check.

### A typical loop

```bash
./maxon-bin/.maxon/maxon build maxon-bin              # rebuild the compiler with itself
./maxon-bin/.maxon/maxon spec-test --filter=arrays    # the specs you touched
./maxon-bin/.maxon/maxon spec-test > temp/spec.log 2>&1   # the whole suite, read from the file
./maxon-bin/.maxon/maxon scale-test                   # after a change to a compiler pass
```

To look at what the compiler emits for a program:

```bash
maxon build problem.maxon --emit-ir                          # writes problem.ir
maxon build problem.maxon --emit-ir-runtime=__managed_get    # plus a compiler-emitted function
maxon build problem.maxon --metrics=temp/phases.tsv          # where the compile time went
```

A change to the runtime the compiler emits into every program takes effect in the compiler's own
behaviour only after it has rebuilt itself twice: the first rebuild emits the new runtime into programs,
the second into the compiler.
