---
title: CLI Reference
description: The maxon commands and their options, logging, exit codes, and environment variables.
sidebar:
  order: 1
---

Everything the `maxon` compiler driver accepts: the commands, their options, the project layout they
read, and the tools around them.

`maxon help` prints the same command and option list from the driver itself, and `maxon help <command>`
prints one command's part of it. Where this document and that listing disagree, the listing is the
compiler.

`maxon` is one binary. It compiles, runs, formats and tests Maxon programs, serves editors and AI
agents, and reads back what a program did.

| Command | Description |
|---------|-------------|
| `maxon <file>.maxon [args...]` | Run a Maxon file as a script: [`maxon execute`](#maxon-execute) without the command word |
| `maxon build <file\|directory>...` | Compile a Maxon program to an executable |
| `maxon init [<directory>]` | Scaffold a new project: `project.maxon` and `main.maxon` |
| `maxon cache [clear]` | Report what this compiler has cached on the host, or remove it |
| `maxon coverage <run\|report> <exe>` | Run a `--coverage` binary and report line and branch coverage ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon debug --dump-info <exe>` | Print the `.mxdbg` debug-info sidecar beside a binary ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon debug --symbolize <exe> <offset...>` | Resolve code offsets to `file:line:col` ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon fmt [file\|directory]` | Re-print `.maxon` sources in canonical layout, in place |
| `maxon help [<command>]` | Print the command and option reference, whole or for one command |
| `maxon lsp-server` | Speak the Language Server Protocol over stdio ([Editor Support](/docs/cli/editor/)) |
| `maxon mcp-server [--dev]` | Speak the Model Context Protocol over stdio ([MCP Server](/docs/cli/mcp-server/)) |
| `maxon monitor [--filter=…] <exe> [args...]` | Run a `--debugstream` binary and print its trace events ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon profile run <exe>` | Sample a running program and report where its CPU time went ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon execute <file\|directory> [args...]` | Compile a program, or reuse a cached build of it, and run it |
| `maxon run [<task> [args...]]` | Run a task declared in `tasks.maxon`, or list the tasks |
| `maxon test [directory]` | Run a project's own `test` declarations |
| `maxon upgrade [--dry-run]` | Update this compiler's install to the newest release |
| `maxon version` | Print the version, the commit it was built from, and the host target |

Four more commands are for working on the compiler itself: `spec-test`, `scale-test`, `verify-recheck`
and `verify-warm-rebuild`. `maxon` with no arguments does not list them; `maxon help` does. See
[Working on the Compiler](/docs/cli/compiler-development/).

Only the **first** command word on a line is the command. A later one is an ordinary positional
argument, so `maxon fmt fmt` formats the directory `fmt/`.

An option the driver does not implement is refused before any command runs (`error: unknown option:
<option>`, exit 1, or 2 for `maxon test`), rather than ignored.

## `maxon execute`

Compiles a program, or reuses a cached build of it, and runs it.

```bash
maxon execute <file|directory> [args...]
maxon <file>.maxon [args...]
```

The second form is the same command: a **first** argument ending in `.maxon` selects `execute` with no
command word. That is what a kernel hands the interpreter for a script whose first line is
`#!/usr/bin/env maxon`.

**Everything after the path is the program's command line** and reaches it untouched, including tokens
that look like driver options: in `maxon execute app.maxon --filter=x -o out`, both `--filter=x` and
`-o out` are the program's. So `execute` takes no options of its own.

The program inherits stdin, stdout, stderr and the working directory, and **its exit code becomes this
command's**. The build itself prints nothing, so the program's output is not mixed with compiler
output. The program's `argv[0]` is the cached executable, not the path you typed.

`execute` always compiles for the host. To build for another target, use
[`maxon build --target=`](#maxon-build).

### The run cache

Each program gets a directory of its own in a cache, and its build is reused until something it was
built from changes:

```text
<cache>/run/v<cache format>/<hash of the program's path and entry point>/
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

`<cache>` is the directory Maxon owns on this host. It is the first of these whose variable names a
directory this compiler can create:

| Windows | macOS and Linux |
|---------|-----------------|
| `<MAXON_RUN_CACHE_ROOT>\maxon` | `<MAXON_RUN_CACHE_ROOT>/maxon` |
| `<USERPROFILE>\.maxon\cache` | `<HOME>/.maxon/cache` |
| `<LOCALAPPDATA>\maxon` | `<TMPDIR>/maxon` |
| `<TEMP>\maxon` | — |

Your home directory comes before the host's temp area because `<home>/.maxon` is where the install
script puts `bin/` and `stdlib/`: the cache sits beside the install it was built by, and survives a host
that sweeps its temp area. **Every row is a directory a variable names, and a host where no row can be
used — none of the variables set, or every directory they name refused — is refused by name** rather than
sent to an invented path: a cache under a world-writable directory such as `/tmp` would be shared with
every other user of the machine.

**A row that cannot be created does not take the cache from a row that would have worked.** Maxon tries to
create each row in turn and keeps the build under the first one it gets. A `HOME` that exists but cannot be
written under — which is what a container run with a numeric `--user` that has no passwd entry gets, since
Docker hands it `HOME=/` — therefore costs a run nothing as long as some lower row works.

Everything under `<cache>` is Maxon's, and everything beside it is not — which is what
[`maxon cache clear`](#maxon-cache) removes and what it leaves alone. A run uses one row;
[`maxon cache`](#maxon-cache) reports every row and `clear` sweeps every row this host resolves, because a
build published while another row ranked first — or while a row that is now unusable still worked — is
otherwise stranded where nothing looks again.

### Scripts and the shebang line

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
| `1` | `execute` could not start the program: no program named, no such file, a compile error (the diagnostics are printed and nothing runs), no directory to keep the cache in, or a build that could not be stored in it |

A missing file is reported as `error: file not found: <path as typed>`, the same sentence `maxon build`
prints.

```bash
maxon execute hello.maxon                 # compile if needed, then run
maxon execute myproject --verbose     # a directory as one program; --verbose is the program's
maxon hello.maxon a b c               # no command word, three arguments
MAXON_RUN_CACHE_ROOT=/build/cache maxon execute hello.maxon
```

## `maxon run`

Runs a **task** declared in the `tasks.maxon` in the current directory.

```bash
maxon run                     # list the tasks
maxon run <task> [args...]    # run one
```

A task is an exported function of no parameters returning `ExitCode`. `tasks.maxon` is an ordinary
Maxon program with the whole standard library available, and it is **not** part of any build: it marks
no project, and no walk compiles it into one. See [The task file](/docs/cli/project-structure/#the-task-file).

**With no task named, the tasks are listed**, one per line on stdout, and the command exits 0. A name
the file does not declare is refused, exit 1, with the list printed so the reader can see what to type
instead. A directory holding no `tasks.maxon` is refused the same way. A name beginning `__` is
reserved and is neither listed nor runnable.

**The task's streams and exit code are the command's own.** stdin, stdout and stderr are inherited, so
a long task prints as it goes, and everything after the task name reaches it as its own command line.
The task program is compiled for the host and kept in the [run cache](#the-run-cache), keyed by the
file and the task name, so two tasks of one file never share a build.

**A task may describe a build**, by calling `Build.build`, `Build.target`, `Build.buildTargets`,
`Build.buildWithConfig` or `Build.delegate` — the same calls a `project.maxon` makes (see
[Describing a build](/docs/cli/project-structure/#describing-a-build)). The build is performed once the task exits 0, and `-o`,
`--target`, `--define` and `--no-debug-info` apply to it exactly as they do to `maxon build`.

```bash
maxon run build -o dist/maxon --target=x64-linux
```

## `maxon init`

Scaffolds a new project.

```bash
maxon init [<directory>]
```

With no directory it initializes the working directory; with one it creates that directory. It writes
exactly two files — `project.maxon` and `main.maxon` — and the project is named after the directory,
which is the output name the manifest builds to:

```maxon
// project.maxon
export function build() returns ExitCode
	Build.build(".", output: ".maxon/myapp")
	return 0
end 'build'
```

```maxon
// main.maxon
function main() returns ExitCode
	print("Hello, world!\n")
	return 0
end 'main'
```

**It never overwrites.** If either file is already there, **nothing is written at all**: the existing
path is named and the command exits 1. Other files in the directory are left alone. A directory whose
name holds a character the manifest cannot state (`"`, `{`, `}`, `\`, a control character) is refused
before anything is written.

On success it prints the two paths it wrote and the command that builds them, and exits 0.

## `maxon build`

Compiles Maxon source to a standalone executable.

```bash
maxon build <file|directory>... [options]
maxon build [<target name>] [options]
```

**Arguments.** One or more paths. **Several paths are compiled as one program, in the order given.** A
directory contributes every `.maxon` file beneath it, except `project.maxon` (in any letter case),
`*.test.maxon` files and subtrees marked with a `.maxonignore`. A `.maxonignore` excludes a directory the
walk **discovers**; it does not override a path you **named**. So a file or a directory you name
explicitly is compiled whatever a `.maxonignore` above it — or on it — says.

**No path** runs the `project.maxon` manifest in the current directory, and a bare word that names one of
its targets builds that target. See [Project Structure](/docs/cli/project-structure/). A directory
with no `project.maxon` prints a usage line and exits 1.

**Options:**

| Option | Description |
|--------|-------------|
| `-o <path>`, `--output=<path>` | Output executable path. Without it the name comes from the first path given: a file is built beside itself (`foo.maxon` → `foo.exe` on Windows), and a directory into itself under its own name (`app` → `app/app.exe`). The target's executable extension is added unless the path already carries it. A value that is not a path (a non-`file` URL, or on Windows a name holding `< > " \| ? *` or a control character) is refused with exit 1. |
| `--target=<cpu>-<os>` | Compile for this target instead of the host: `x64-windows`, `x64-linux`, `arm64-macos`, `arm64-linux` or `wasm32-wasi`. See [Targets](/docs/cli/targets/). |
| `--emit-ir` | Also write the lowered Target IR beside the executable, as `<output>.ir`. It shows the functions from the program's own source. |
| `--emit-ir-runtime=<a>,<b>` | Also render these compiler-emitted or standard-library functions in that IR. Implies `--emit-ir`. A value naming no function is refused. |
| `--no-debug-info` | Do not write the `<output>.mxdbg` debug-info sidecar. It is written by default, and the executable is byte-identical either way. See [Debugging and Profiling](/docs/cli/debugging/). |
| `--coverage` | Instrument for code coverage: the binary counts each statement and branch arm it executes and writes the counts to `<output>.mxcov` as it exits. This changes the emitted code, so it is a separate build from the one you ship. Read the counts with `maxon coverage`. Needs the debug-info sidecar, so `--no-debug-info` beside it is refused. |
| `--debugstream` | Emit the shared-memory debug-stream producer that `maxon monitor` reads, with the memory manager's events. Also enables the `__DebugStream` builtin; without the flag its calls emit nothing. Refused on a target without shared memory and an uptime clock. |
| `--async-trace` | Write the green-thread trace to stderr as the program runs: one line per spawn, sleep, I/O wait, resume and await. See [Debugging and Profiling](/docs/cli/debugging/). |
| `--census-by-tag`, `--census-by-tag=<phase>` | Census the *live* heap by allocation tag at every compile phase boundary — which types the bytes still held at that point belong to, rather than how many bytes the phase asked for. Naming a phase censuses that phase alone, which is what a large compile can afford. It samples residency whether or not `--log=compiler:debug` is given, and writes a `tag` row per non-empty bucket to `--metrics=<path>`, named `<phase>/<tag>`, whose `allocs` and `livebytes` are the slots live at that boundary and their bytes; `--log=compiler:debug` also prints the table. A compiler built without `--debugstream` carries no tags, so the census is by *size class* instead and says so in its headline. See [Logging](#logging). |
| `--allocations-by-tag`, `--allocations-by-tag=<phase>` | Tally every allocation the compile *asks for* by allocation tag and by phase — churn rather than a level, so a phase that allocates a million short-lived boxes shows them here and in no other table. Naming a phase tallies that phase alone. It writes a `churn` row per non-empty bucket to `--metrics=<path>`, named `<phase>/<tag>`, whose `allocs` and `bytes` are the allocations the phase made and the bytes they asked for; `--log=compiler:debug` also prints the table. A compiler built without `--debugstream` carries no tags, so nothing is tallied and the report says so. See [Logging](#logging). |
| `--define=<name>=<value>` | Replace a top-level `String` constant's written-out default with `<value>`. Repeatable. See [Defines](#defines). |
| `--metrics=<path>` | Write this compile's per-phase time and memory to `<path>` as TSV. Each row's first field is its kind: `phase`, `regalloc`, `total`, `unattributed`, `code`, `tag` when `--census-by-tag` is given, and `churn` when `--allocations-by-tag` is. `--log=compiler:debug` prints the same numbers as a table, and adds a residency table — what the heap was *holding* at each phase boundary, rather than what the phase asked for. The TSV's five residency columns are filled only when it or `--census-by-tag` is given; on their own they are zero. |

`--debugstream`, `--async-trace` and `--coverage` are opt-in **per build**. Without the flag, none of
that machinery is emitted.

A build prints the compiler's version and an early-preview warning to stdout, then
`Compiled -> <path>` on success, and exits 0. Progress lines (`[CMP] INFO: Wrote … bytes of code to …`)
go to stderr; `--log=error` silences them. A compile error prints its diagnostics to stderr and exits 1.

A path-less build has one more line, between the two: whether it compiled the `project.maxon` runner or
reused the cached one. That compile makes no progress lines of its own — see
[The build manifest](/docs/cli/project-structure/#the-build-manifest).

```bash
maxon build hello.maxon                       # → hello.exe on Windows, hello elsewhere
maxon build src/ -o build/app                 # a whole directory, named output
maxon build a.maxon b.maxon                   # one program from two files, in that order
maxon build                                   # run project.maxon
maxon build app                               # project.maxon's target named "app"
maxon build app.maxon --target=wasm32-wasi
maxon build app.maxon --define=Version=1.4.2
```

### Defines

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

A `project.maxon` manifest can supply defines too; a `--define` on the command line wins over the
manifest's.

## `maxon cache`

Reports what this compiler has cached on the host, and removes it.

```bash
maxon cache
maxon cache clear
```

With **no verb it reports**, on stdout, and exits 0. One line per row of the table under
[The run cache](#the-run-cache), in that order, whether or not the row holds anything — a reader asking
where the cache is gets the whole roster:

```
Cache roots on this host:
  C:\Users\you\.maxon\cache (in use): 1 build, 451.8 KB
  C:\Users\you\AppData\Local\maxon: 3 builds, 929.7 KB
  C:\Users\you\AppData\Local\Temp\maxon: empty
Total: 4 builds, 1.3 MB
```

`(in use)` marks the row the next `run` or path-less `build` will fill; the others may still hold builds
nothing looks for again. Reporting it creates that directory if it is not there yet, because finding the
row a run uses is the same act as using it. **Where no row can be created nothing is marked**, and the
report ends with the sentence [`maxon execute`](#maxon-execute) refuses with, naming every variable this host
consults — still on stdout, still exit 0, because "none, and here is why" answers the question the report
was asked. **A build is the executable** — the `.mxdbg` debug sidecar beside it is not one,
and neither is a build still being written. The size is everything under the directory, **sidecars
included**, because that is what `clear` gives back. The `Total:` line appears when more than one row
holds something.

A root that **cannot be read** says so on its own line and the command exits 1, rather than being counted
as empty: a cache reported as holding nothing while it holds hundreds of builds is an answer you cannot
act on.

`clear` removes the compiled programs [`maxon execute`](#maxon-execute) and a path-less
[`maxon build`](#maxon-build) keep, the directories holding them, the inline snippets the
[MCP server](/docs/cli/mcp-server/) stages, and the Maxon directory above all of those. The next `run` or
path-less `build` compiles from scratch and fills the cache again.

It removes **only what Maxon created**. `MAXON_RUN_CACHE_ROOT` may name a directory that is already
somebody's — a shared build area, or your own scratch directory — so the clear reaches only the Maxon
directory a row of the table under [The run cache](#the-run-cache) names, and stops there. The directory
you named, and everything else in it, is left exactly as it was.

**It sweeps every row of that table, not just the one in use.** A run caches under the first row whose
variable names a directory it can create, so a build made before `MAXON_RUN_CACHE_ROOT` was set — or under
a `TMPDIR` that has since changed, or under a row that has since stopped being creatable — sits where
nothing looks again, and only a clear that visits every row can still reach it. Two variables naming one directory are one cache, cleared once and counted once.

- **A cache holding nothing is not an error.** A fresh machine and a second `clear` both find nothing;
  both say so on stdout and exit 0.
- **The report names more than one directory only when more than one held something.** One cache is the
  ordinary case and gets a single line.
- **A removal that fails is reported and exits 1**, naming the first file or directory that stayed. A
  cache reported as cleared while it still holds builds is an answer you cannot act on. On Windows, a
  build another process is executing cannot be removed.
- `cache` takes no options. A word that is not `clear`, or an option, prints a `Usage:` line on
  **stderr** and exits 1.

## `maxon fmt`

Re-prints `.maxon` sources in canonical layout, **in place**.

```bash
maxon fmt [<file|directory>]
```

With **no path it formats the whole working directory**. A named **file** is formatted whatever it is
called. A **directory** is walked for `.maxon` files, skipping a project's `project.maxon` (in any letter case;
the compiler's own `stdlib/` and `runtime/` hold no manifest, so their `Build.maxon` is formatted), any
subdirectory the walk finds under a `.maxonignore`, and any subdirectory that holds a `.git` (a nested
clone or worktree), so a run never rewrites another repository's files. As with `build`, a `.maxonignore`
on or above the directory you **named** does not exclude it — naming it is the explicit act.

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

## `maxon test`

Runs a project's unit tests: every `test` declaration in its `*.test.maxon` files. How to write tests
is covered in [Testing](/docs/language/testing/).

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
| `--list` | Print the tests that would run, and compile nothing. A project whose sources do not all tokenize is still refused, so the list is never quietly short a file. |
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
not read as green. A source file that does not tokenize is a compile error like any other: the run
exits 2 with the diagnostic, rather than reporting that file's tests as absent.

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

## `maxon upgrade`

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
| A source checkout | `git -C <root> pull`, then `maxon-bin/.maxon/maxon run build` |
| Anywhere else | The install one-liner |

`--dry-run` never bypasses a refusal, and a refusal writes nothing to stdout. The command takes no other
argument and no option but `--dry-run`. To install a specific release, run the install script yourself
with its own version option.

## `maxon version`

Prints one line to stdout and exits 0:

```bash
maxon version    # maxon 0.1.1 (a1b2c3d 2026-09-09) (x64-windows)
```

The line holds the release number, the commit and date the compiler was built from, and the host
target. It takes no arguments.

## `maxon help`

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
command list. `help` takes no options.

## Logging

Every command except [`maxon execute`](#maxon-execute) accepts a logging option. With `run`, a `--log=` after the
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

**The type-name table.** `--log=compiler:debug` also prints one line per source file per fold round —
`typeNames <path>: <N> names, <B> bytes, reserved <RN> names / <RB> bytes, rehashes <H>, regrowths <G>` —
the names that file interned and the arena bytes they occupy, what its table reserved ahead of them, and
how many times the table rehashed or had to grow.

**The census by tag.** [`--census-by-tag`](#maxon-build) adds a third table under the residency one: the
live heap at each phase boundary broken down by the type each allocation was tagged with. It prints the
top five buckets for every phase sampled, then the whole table for the phase whose *live* level was
highest, and closes with three lines that bound what the table can be trusted to say — the OS-direct
mappings and allocator overhead no row counts, how many live slots carry no box header (those are
attributed by whatever their first word holds, so they land in `(unattributable)` or in a bucket that is
not theirs), and a self-check comparing the buckets' sum and the tally's own walk against
`slabLiveBytes`. A tag past the end of the table is `(overflow)`, and bucket 0, which no type's tag
occupies, is `(untagged)`.

Without `--log=compiler:debug` the flag still samples, but the numbers go only to `--metrics=<path>`.

```bash
maxon build app.maxon --log=compiler:debug --census-by-tag
maxon build app.maxon --log=compiler:debug --census-by-tag=codegen   # one phase, on a large compile
```

**The allocations by tag.** [`--allocations-by-tag`](#maxon-build) answers the other question: not what
the heap is *holding* at a boundary but what the phase *asked for* between two of them, which is the
figure an allocation that is freed before the boundary appears in at all. It prints the top five buckets
for every phase, then the whole table for the phase that allocated most, and closes with a self-check —
the buckets summed over every phase against the run's own allocation total at the last boundary. The two
are equal, exactly: the first phase sampled absorbs everything allocated before it, so the sum
telescopes, and it does so when one phase is named too. A compiler built without `--debugstream` carries
no tags, so nothing is tallied and the headline says so.

```bash
maxon build app.maxon --log=compiler:debug --allocations-by-tag
maxon build app.maxon --log=compiler:debug --allocations-by-tag=regalloc   # one phase
```

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | The command ran and failed: a compile error, a failing check, a command line it cannot act on |
| `2` | Nothing ran: the tree is not in a state to work on (another command holds its [tree lock](/docs/cli/project-structure/), or a compiler binary is older than its sources), or a `maxon test` run could not happen |
| `101` | A program's leak check found an allocation still held at exit. The program reports it, not the driver; `maxon test` reports it as `LEAKED`. |

Some commands forward another process's exit code instead: [`maxon execute`](#maxon-execute) returns the
program's, and [`maxon upgrade`](#maxon-upgrade) the install script's. For those, only `1` is ever the
driver's own. `maxon monitor`, `maxon coverage` and `maxon profile` have their own tables on
[Debugging and Profiling](/docs/cli/debugging/).

## Environment Variables

**Read by the compiler:**

| Variable | Read by | Effect |
|----------|---------|--------|
| `MAXON_RUN_CACHE_ROOT` | `run`, `build` (manifest), `cache` | Maxon caches under `<value>/maxon`. Consulted first |
| `USERPROFILE` | `run`, `build` (manifest), `cache` on Windows | Maxon caches under `<value>\.maxon\cache` when `MAXON_RUN_CACHE_ROOT` is unset or cannot be created |
| `HOME` | `run`, `build` (manifest), `cache` elsewhere | Maxon caches under `<value>/.maxon/cache` when `MAXON_RUN_CACHE_ROOT` is unset or cannot be created |
| `LOCALAPPDATA`, then `TEMP` | `run`, `build` (manifest), `cache` on Windows | Last resort, under `<value>\maxon`, when neither of the two above names a directory Maxon can create |
| `TMPDIR` | `run`, `build` (manifest), `cache` elsewhere | Last resort, under `<value>/maxon`, when neither of the two above names a directory Maxon can create |
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
