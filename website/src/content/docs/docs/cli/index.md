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
| `maxon init [<directory>]` | Scaffold a new project: `<directory>.maxproj` and `main.maxon` |
| `maxon cache [clear]` | Report what this compiler has cached on the host, or remove it |
| `maxon check-scriptlib [<directory>]` | Compile every [scriptlib](#the-scriptlib-directory) package with every function body built |
| `maxon coverage <run\|report> <exe>` | Run a `--coverage` binary and report line and branch coverage ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon dap-server` | Speak the Debug Adapter Protocol over stdio ([Editor Support](/docs/cli/editor/)) |
| `maxon debug <exe> [-- args...]` | Debug a program interactively: breakpoints, stepping, backtraces and locals ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon debug --dump-info <exe>` | Print the `.mxdbg` debug-info sidecar beside a binary ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon debug --symbolize <exe> <offset...>` | Resolve code offsets to `file:line:col` ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon fmt [file\|directory]` | Re-print Maxon sources in canonical layout, in place |
| `maxon help [<command>]` | Print the command and option reference, whole or for one command |
| `maxon lint [--tests] [--fix] [<file\|directory\|target>...]` | Check a program with every rule, the [lint rules](#maxon-lint) included, and stop before code generation; `--fix` rewrites the sources with each finding's one repair |
| `maxon lsp-server` | Speak the Language Server Protocol over stdio ([Editor Support](/docs/cli/editor/)) |
| `maxon mcp-server [--dev] [--http[=<port>]]` | Speak the Model Context Protocol over stdio, or over loopback HTTP ([MCP Server](/docs/cli/mcp-server/)) |
| `maxon monitor [--filter=…] <exe> [args...]` | Run a `--debugstream` binary and print its trace events ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon profile run <exe>` | Sample a running program and report where its CPU time went ([Debugging and Profiling](/docs/cli/debugging/)) |
| `maxon execute <file\|directory> [args...]` | Compile a program, or reuse a cached build of it, and run it |
| `maxon run [<task> [args...]]` | Run a task declared in the directory's `.maxtasks` file, or list the tasks |
| `maxon test [<target>] [directory\|file.maxtest]` | Run a project's own `test` declarations, or one test file's alone |
| `maxon upgrade [--dry-run]` | Update this compiler's install to the newest release |
| `maxon version` | Print the version, the commit it was built from, and the host target |

Six more commands are for working on the compiler itself: `spec-test`, `scale-test`, `check-stdlib`,
`stage-library-tier`, `verify-recheck` and `verify-warm-rebuild`. `maxon` with no arguments does not list
them; `maxon help` does. See
[Working on the Compiler](/docs/cli/compiler-development/).

Only the **first** command word on a line is the command. A later one is an ordinary positional
argument, so `maxon fmt fmt` formats the directory `fmt/`.

An option the driver does not recognize stops the command before it runs, with
`error: unknown option: <arg>` and the command list. A recognized option given a value it cannot take,
such as `--workers=0` or an empty `--filter=`, stops it with `error: invalid option value: <arg>`; an option
that takes a value takes a non-empty one, so `--output=` and `--target=` are refused this way. The first such
option on the line is the one reported, and the exit code is 1 (2 for `maxon test`). An empty positional
argument (`maxon build ""`) stops the command with `error: invalid argument: an empty word names nothing`,
with the same exit code.

An option that only other commands take stops the command the same way, before an unknown option is
looked at, with `error: <option> is taken only by <commands>; <command> does not take it` and the
command's `Usage:` line on stderr. `maxon help <command>` lists the options a command takes.

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
that look like driver options: in `maxon execute app.maxon --filter=x --output=out`, both `--filter=x`
and `--output=out` are the program's. So `execute` takes no options of its own.

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

- **The key is a content hash**, never a modification time. It covers every source file of the program,
  every standard-library and runtime source the build compiles, every
  [scriptlib](#the-scriptlib-directory) package source when the program is one file, and the Unicode
  tables the compiler embeds, each hashed by its bytes, plus the compiler's
  identity (its path, size, modification time and target). Editing any of them rebuilds; nothing else
  does.
- **The key is the file name.** A cache hit is that file existing, so two runs of one program cannot
  serve each other a stale build. A fresh build is written under a temporary name and renamed into
  place, so simultaneous cold runs never share an output path. Superseded builds, builds an older
  cache format left, and temporary builds a process that has ended left (see
  [`maxon cache`](#maxon-cache)) are removed on a best-effort basis.
- **The slot belongs to the program's physical path.** The path is anchored at the working directory and
  every symbolic link along it is followed, so `x.maxon`, `./x.maxon`, `a/../x.maxon` and a path through a
  link all share one cache slot when they name one file. The part of a path that names nothing yet is
  folded lexically, as [`FilePath.normalize()`](/docs/stdlib/io/#filepath) folds it.

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

### The library cache

Every compile reads the whole standard library and runtime. The library cache keeps what those files
cost — each file's tokens with its `#if` blocks resolved, its parse, and the declarations the library
makes — so a compile lexes, sweeps and parses the library once per compiler and target, and every later
compile, in any process, starts from what that one wrote:

```text
<cache>/library/<hash tier>/hash-seed
<cache>/library/<hash tier>/v<cache format>/<compiler path and target>/<compiler build>/
├── views-<key>.mxlc            # every library file's tokens with `#if` resolved
├── baseline-<key>.mxlc         # the library's declarations
├── core-<key>.mxlc             # the same, less the files that declare generic functions
├── parses-<key>-<key>.mxlc     # library parses
└── audit-<key>.mxlc            # the library's unused-declaration verdict
```

- **It is always on**, under the same `<cache>` as the run cache: the first usable row of the table
  above, so `MAXON_RUN_CACHE_ROOT` moves it too. On a host with no usable row, compiles run without it.
- **Its keys come from one stored seed.** The cache's keys and checksums are
  [`KeyedHasher`](/docs/stdlib/data/#keyedhasher) digests, and those differ between hash tiers, so each tier the
  host's compiles run on has a directory of its own, named by a digest that tier alone computes. That
  directory holds `hash-seed`, the 8 bytes of a [`HashSeed`](/docs/stdlib/data/#hashseed) every compile
  using it folds its keys from. The first compile to find no seed writes one, removing whatever entries the
  directory held; of compiles racing to write it, one seed wins and the others adopt it. A compile that can
  neither read nor write the seed runs without the cache.
- **It holds the installed `stdlib/` and `runtime/`.** [Scriptlib](#the-scriptlib-directory) packages
  are your own files and change between compiles, so each compile reads and parses them fresh.
- **A program folds its own declarations on top of the library's.** A program that shadows a library
  type, declares a name the library uses, or lives inside `stdlib/` has its names settled first: it
  sweeps the library's declarations itself, from the cached tokens, and its parses of library files are
  its own. A program lives inside `stdlib/` by its physical path, so one named through a symbolic link counts.
  A typealias that has the name and form (ranged, function, tuple or generic) of the library's one typealias
  of that name folds on top of the library's like any other declaration, and the program shares its parses
  of library files with the programs that declare the same library typealiases.
- **An entry is checked when it is read.** One that is damaged, was written under another entry format, or
  holds a number outside the range it is read into is deleted and written again, and the compile goes on
  as a cold one. Entries are written under a temporary name and renamed into place, so concurrent compiles
  share the cache safely.
- **Old entries are removed as new ones are written.** A new build of the compiler replaces the previous
  build's directory, a directory an older entry format left is removed (directly under `library/` as well
  as under a tier's directory), each kind keeps its 4 newest
  entries (16 for parses), and the 16 most recently written compiler directories are kept.

[`maxon cache`](#maxon-cache) counts its entries and `clear` removes it. Under `--log=compiler:debug` each
compile reports what it did with the cache; see [Logging](#logging).

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

### The scriptlib directory

A script is a single file, so code that several scripts share goes in **scriptlib**: a directory of
your own packages beside the compiler's `stdlib/`. An install's is `~/.maxon/scriptlib/`, and a
checkout's compiler reads `<checkout>/scriptlib/`, which Git ignores. Every file in it is yours, and an
install or [`maxon upgrade`](#maxon-upgrade) leaves it as it is.

```text
~/.maxon/scriptlib/
├── text/                 # a package: namespace scriptlib.text
│   └── Lines.maxon
└── notes.maxon           # outside every package: skipped
```

- **Each immediate subdirectory is a package**, and every file beneath it is part of that package. A
  `.maxon` file directly in `scriptlib/` belongs to no package and is skipped.
- **A program whose root is one file compiles the packages**: `maxon execute <file>`, a shebang script,
  `maxon build <file>`, a described build whose `sources` is one file, and a single file the language
  server or an MCP tool checks. A directory program is compiled from its own files.
- **A package's `public` and `export` declarations are what a script calls**, bare like a
  standard-library name, or qualified by the package's namespace, `scriptlib.<package>`
  ([Namespaces](/docs/language/namespaces/)). An unmarked declaration stays inside its package.
- **A package compiles as the standard library does.** A script builds only the package files whose
  functions it reaches, and a diagnostic raised inside a package names the package file. A script may
  declare a type named like one the library or a package declares, and inside the package the name
  keeps its library or package meaning; a package that also spells that name as a member is
  [E3112](/docs/cli/error-codes/#e3112--stdlibshadowednameisalsoamember). A member a script's `extension` adds to a
  library or package type is the script's: package and library files keep the meaning their own layer
  gives the name ([Bare Names and Ambiguity](/docs/language/namespaces/#bare-names-and-ambiguity)). The
  unused-declaration checks of [`maxon lint`](#maxon-lint) (E3092, E3219) cover the program's own files.
- **The compiler's reserved surface stays closed to a package**: a `__` name is
  [E2051](/docs/cli/error-codes/#e2051--reservedidentifier) or
  [E3004](/docs/cli/error-codes/#e3004--callunknownfunction), and the raw memory surface is
  [E3185](/docs/cli/error-codes/#e3185--rawsurfaceoutsidelibrarytier).
- **Editing a package rebuilds the scripts that load it**: its sources are part of the
  [run cache](#the-run-cache) key.

A script compiles only what it reaches, so a package this compiler rejects can sit unseen until a script
calls into it. [`maxon check-scriptlib`](#maxon-check-scriptlib) builds every package body.

## `maxon check-scriptlib`

Compiles every package of a [scriptlib](#the-scriptlib-directory) directory, with every function body
built, and reports every diagnostic on stderr, the [lint rules](#maxon-lint)' included.

```bash
maxon check-scriptlib              # the scriptlib/ beside this compiler's stdlib/
maxon check-scriptlib <directory>  # any other directory laid out as packages
```

The check supplies the `main` a program needs, and its only output is the diagnostics. It proves that
the packages compile on their own; a script's own calls into them are checked when that script
compiles.

| Code | Meaning |
|------|---------|
| `0` | Every package compiles, or there is no package to check: a default directory that is missing or holds no package prints a line saying so on stdout |
| `1` | A package does not compile (its diagnostics are on stderr), the named path is not a directory, or the directory could not be read |

The install scripts run it after every install and upgrade; see [`maxon upgrade`](#maxon-upgrade).

## `maxon run`

Runs a **task** declared in the `.maxtasks` file in the current directory.

```bash
maxon run                     # list the tasks
maxon run <task> [args...]    # run one
```

A task is an exported (or `public`) function of no parameters returning `ExitCode`. Every exported or
`public` function of the file is a task, and every run checks all of them when the tasks file compiles,
before anything runs: one that declares a parameter, a defaulted one included, is [E3225](/docs/cli/error-codes/#e3225--entryfunctiontakesparameters), one that
does not return `ExitCode` is [E3002](/docs/cli/error-codes/#e3002--mainwrongreturntype), and one that throws is
[E3054](/docs/cli/error-codes/#e3054--maincannotthrow). So `maxon run report` is refused beside an exported `helper` of the wrong shape; a helper
drops its `export` (or `public`). A function an inactive `#if` excludes is no task of the run. A task is
asked for by its name with each `_` written `-`: `maxon run build-extension` runs `build_extension`. The
`.maxtasks` file is an ordinary Maxon program with the whole standard library available, and it stands apart from
every build: it marks no project, and a build compiles `.maxon` files only. See
[The task file](/docs/cli/project-structure/#the-task-file).

**With no task named, the tasks are listed**, one per line on stdout in that dashed form, and the
command exits 0. A name the file leaves undeclared is refused, exit 1, with the list printed so the
reader can see what to type. A directory holding no `.maxtasks` file prints a usage line and exits 1,
and a directory holding two is refused, naming both. A name beginning `__` is reserved: it is left off
the list and refused as a task name.

**The task's streams and exit code are the command's own.** stdin, stdout and stderr are inherited, so
a long task prints as it goes, and everything after the task name reaches it as its own command line.
The task program is compiled for the host and kept in the [run cache](#the-run-cache), keyed by the
file and the task name, so each task of a file has a build of its own.

**A task may describe a build**, by calling `Build.build`, `Build.buildWithConfig` or `Build.delegate` —
the same calls a `.maxproj` target makes (see [Describing a build](/docs/cli/project-structure/#describing-a-build)). The build is
performed once the task exits 0, and the options of [`maxon build`](#maxon-build) (`--output=`,
`--target`, `--define`, `--no-debug-info` and the rest) apply to it exactly as they do there, and so does
[`rebuild_with_output`](/docs/cli/project-structure/#building-again-with-the-written-program). A task's build that states no output is written to
`.maxon/<stem>` in the working directory, `<stem>` being the `.maxtasks` file's name without its
extension.

```bash
maxon run build --output=dist/maxon --target=x64-linux
```

## `maxon init`

Scaffolds a new project.

```bash
maxon init [<directory>]
```

With no directory it initializes the working directory; with one it creates that directory. It writes
exactly two files — `<directory>.maxproj` and `main.maxon` — and the project is named after the
directory. The project file's one target, `build`, states no output, so `maxon build` writes
`.maxon/<directory>`:

```maxon
// myapp.maxproj
export function build() returns ExitCode
	Build.build(".")
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

**It writes only into a directory where both files are new.** If the directory already holds a `.maxproj`
file or a `main.maxon`, **nothing is written at all**: the existing path is named and the command exits
1. Other files in the directory are left alone. A directory inside an existing project's tree is
refused the same way, with [E2074](/docs/cli/error-codes/#e2074--nestedprojectfile): a project's
tree holds one project.

On success it prints the two paths it wrote and the command that builds them, and exits 0.

## `maxon build`

Compiles Maxon source to a standalone executable.

```bash
maxon build <file|directory>... [options]
maxon build [<target name>] [options]
```

**Arguments.** One or more paths. **Several paths are compiled as one program, in the order given.** A
directory contributes every `.maxon` file beneath it, minus the subtrees marked with a `.maxonignore`;
`.maxproj`, `.maxtasks` and `.maxtest` files are left to the commands that read them. A `.maxonignore`
excludes a directory the walk **discovers**, and a path you **named** is compiled whatever a
`.maxonignore` above it — or on it — says. **Symbolic links are followed**, including one that leaves the
directory you named. Each physical directory and file is taken once, under the first spelling the walk
reaches: a link to a sibling directory lists its files once, a link back to an ancestor ends the walk
there, and a file named through a link and then the directory holding it is loaded once.

**Diagnostics are printed in source order**, whatever phase found them: by file (in the order the compile
reads its files), then line and column. A duplicate declaration is reported at the later one.

**The lint rules are [`maxon lint`](#maxon-lint)'s.** A build compiles a program that discards a result
(E3064, E3065) or holds an unused declaration (E3092, E3219); run `maxon lint` on it to see them.

**No path** builds the target of the `.maxproj` file in the current directory. With several targets it
lists them and exits 1. **A single word naming a target** builds that target, each `_` of the target's
name written `-`: `maxon build my-app` builds the target function `my_app`. A word naming no target is
a path. See [Project Structure](/docs/cli/project-structure/). A directory with no `.maxproj` file prints a usage
line and exits 1.

**A path inside the compiler's `runtime/` directory** is refused with exit 1 and an error naming it as part
of the `runtime/` tier: every program compiles that tier, so a file of it is built through a program that
uses it.

**Options:**

| Option | Description |
|--------|-------------|
| `--output=<path>` | Output executable path. Without it the name comes from the first path given: a file is built beside itself (`foo.maxon` → `foo.exe` on Windows), and a directory into itself under its own name (`app` → `app/app.exe`). The target's executable extension is added unless the path already carries it. A value that is not a path (a non-`file` URL, or on Windows a name holding `< > " \| ? *` or a control character) is refused with exit 1. |
| `--target=<cpu>-<os>` | Compile for this target instead of the host: `x64-windows`, `x64-linux`, `arm64-macos`, `arm64-linux` or `wasm32-wasi`. See [Targets](/docs/cli/targets/). |
| `--emit-ir` | Also write the lowered Target IR beside the executable, as `<output>.ir`. It shows the functions from the program's own source. |
| `--emit-ir-function=<a>,<b>` | Also render these functions in that IR: compiler-emitted ones, standard-library ones, or the program's own. A named function of the program's own is kept out of line, so it renders as its own function even where it would otherwise be inlined. Implies `--emit-ir`. A value naming a function the program does not render — a misspelling, an unreached standard-library function, or a function of the program's own that nothing calls — is refused: the build exits 1 and writes no executable. |
| `--no-debug-info` | Do not write the `<output>.mxdbg` debug-info sidecar. It is written by default, and the executable is byte-identical either way. See [Debugging and Profiling](/docs/cli/debugging/). |
| `--rebuild` | Compile even when the output is up to date. The compile still writes its build record, so the next build without `--rebuild` can be up to date again. See [Up to date](#up-to-date). |
| `--coverage` | Instrument for code coverage: the binary counts each statement and branch arm it executes and writes the counts to `<output>.mxcov` as it exits. This changes the emitted code, so it is a separate build from the one you ship. Read the counts with `maxon coverage`. Needs the debug-info sidecar, so `--no-debug-info` beside it is refused. |
| `--debugstream` | Emit the shared-memory debug-stream producer that `maxon monitor` reads, with the memory manager's events. Also enables the `__DebugStream` builtin; without the flag its calls emit nothing. Refused on a target without shared memory and an uptime clock. |
| `--async-trace` | Write the green-thread trace to stderr as the program runs: one line per spawn, sleep, I/O wait, resume and await. See [Debugging and Profiling](/docs/cli/debugging/). |
| `--poison-freed` | Fill each freed managed box with `0x3F`, from its payload to the end of its slot, so a read after free returns 63, or a pointer of `0x3F3F3F3F3F3F3F3F` that faults when dereferenced. A box above 32 KiB goes back to the operating system whole and is not filled. |
| `--census-by-tag`, `--census-by-tag=<phase>` | Census the *live* heap by allocation tag at every compile phase boundary — which types the bytes still held at that point belong to, rather than how many bytes the phase asked for. Naming a phase censuses that phase alone and prints that phase's table, which is what a large compile can afford. It samples residency whether or not `--log=compiler:debug` is given, and writes a `tag` row per non-empty bucket to `--metrics=<path>`, named `<phase>/<tag>`, whose `allocs` and `livebytes` are the slots live at that boundary and their bytes; `--log=compiler:debug` also prints the table. A compiler built without `--debugstream` carries no tags, so the census is by *size class* instead and says so in its headline. See [Logging](#logging). |
| `--allocations-by-tag`, `--allocations-by-tag=<phase>` | Tally every allocation the compile *asks for* by allocation tag and by phase — churn rather than a level, so a phase that allocates a million short-lived boxes shows them here and in no other table. Naming a phase tallies that phase alone. It writes a `churn` row per non-empty bucket to `--metrics=<path>`, named `<phase>/<tag>`, whose `allocs` and `bytes` are the allocations the phase made and the bytes they asked for; `--log=compiler:debug` also prints the table. A compiler built without `--debugstream` carries no tags, so every allocation is counted under `(untagged)` and the report says so. See [Logging](#logging). |
| `--define=<name>=<value>` | Replace a top-level `String` constant's written-out default with `<value>`. Repeatable. See [Defines](#defines). |
| `--metrics=<path>` | Write this compile's per-phase time and memory to `<path>` as TSV. Each row's first field is its kind: `phase`, `regalloc`, `total`, `unattributed`, `upkeep`, `code`, `tag` when `--census-by-tag` is given, and `churn` when `--allocations-by-tag` is. A [`maxon lint`](#maxon-lint) table holds the `phase`, `regalloc`, `total`, `unattributed` and `upkeep` rows, and its code-generation phases read zero. `upkeep` is the time and memory spent refreshing the records of the locks the compile holds, once every 5 s; the memory columns of every `phase` row, of `total` and of `unattributed` exclude it, so two compiles of one program report the same memory however long each ran; the time columns count it in the rows that were open. `--log=compiler:debug` prints the same numbers as a table, and adds a residency table — what the heap was *holding* at each phase boundary. The TSV's five residency columns are filled only when it or `--census-by-tag` is given; on their own they are zero. |

`--debugstream`, `--async-trace`, `--poison-freed` and `--coverage` are opt-in **per build**: each adds its
machinery only to a build that names it.

A build prints the compiler's version and an early-preview warning to stdout, then
`Compiled -> <path>` when it compiled or `Up to date -> <path>` when its output was already current (see
[Up to date](#up-to-date)), and exits 0. Progress lines (`[CMP] INFO: Wrote … bytes of code to …`) go to
stderr; `--log=error` silences them. A compile error prints its diagnostics to stderr and exits 1.

**Replacing the output.** A build writes its output into `.staging-<hex>/`, a directory beside the output
named after the output's name, and publishes it by renaming: the previous output is renamed into that
directory and the new one into its place, so a program still running from the output is replaced like
any other file. A compile that fails moves the previous output out of the way, so nothing stale answers at
the output path; a compiler rebuilding its own running image keeps it in the slot (see
[Rebuilding a running compiler](/docs/cli/project-structure/#rebuilding-a-running-compiler)). A build that is killed leaves the
previous output where it was. A read-only output, a staging directory that cannot be created, and an
output that cannot be renamed are refused with
[E6002](/docs/cli/error-codes/#e6002--binaryoutputnotreplaceable), which leaves the previous output in place.
The next build of the same output removes what the staging directory still holds; an image a program
is still running from stays until that program ends, and anything else that cannot be removed is named
in a warning.

A build of a project target has one more line, between the two: whether it compiled the `.maxproj`
runner or reused the cached one. That compile is silent apart from that line — see
[The project file](/docs/cli/project-structure/#the-project-file).

```bash
maxon build hello.maxon                       # → hello.exe on Windows, hello elsewhere
maxon build src/ --output=build/app           # a whole directory, named output
maxon build a.maxon b.maxon                   # one program from two files, in that order
maxon build                                   # the .maxproj file's only target
maxon build my-app                            # the .maxproj file's target my_app
maxon build app.maxon --target=wasm32-wasi
maxon build app.maxon --define=Version=1.4.2
```

### Breaking changes since the last build

A project target's build reports the breaking changes made since the compiler release that last built
the project, on stdout after the version and early-preview lines, and then builds as usual:

```text
This project was last built with maxon 0.4.0. Breaking changes since then:
0.5.0: `Utf8ByteCount`, `CodepointCount` and `Utf16UnitCount` are removed.
  Fix: use `Count`.
```

Each line pair is one `### Breaking changes` entry of the [release notes](/docs/release-notes/) of a
release after the recorded one, up to this compiler's, with its fix. The MCP
[`breaking_changes`](/docs/cli/mcp-server/#breaking_changes) tool answers the same list, and the language server publishes it
on the project's `.maxproj` document (see [`maxon lsp-server`](/docs/cli/editor/#maxon-lsp-server)).

A project path the host refuses to resolve to its physical spelling — a directory above it the process may not
search, or a lookup the host refuses — fails the command by name, and the sentence says what to check (the
permissions of the directory it names; on Linux, that `/proc` is mounted). A build or test run that has
succeeded and then cannot resolve the project to keep its compiler-version record prints that as a `warning:` and
keeps its exit status. Every record, lock and cache slot is keyed by a resolved physical path.

The release is kept in a **compiler-version record** under the cache root, at
`<cache>/project/v1/<key>.mxpv`, where `<cache>` is the first usable row of the table under
[The run cache](#the-run-cache) and `<key>` a hash of the project directory's physical path (every symbolic link
followed), so the command line, the language server and the MCP server reach one record however the project is
spelled. It holds one line,
the release as `X.Y.Z`, and is written after a project's build succeeds and after a project's
[`maxon test`](#maxon-test) run passes. So:

- a build that fails keeps the record it found, and the report repeats until the project builds;
- a build by a compiler older than the record's release records that older release;
- a compiler built from a checkout, which carries no release version, records the newest release the
  release notes name and reports every breaking change after the recorded release, including the ones
  listed under `## Unreleased`;
- the report needs a record: a project's first build on a host, and its first after
  [`maxon cache clear`](#maxon-cache), writes one.

A record that does not hold a release version is refused, and the build exits 1:

```text
error: the compiler-version record <path> holds '<text>', which is not a release version: <why>. Delete it, or run `maxon cache clear`.
```

### Up to date

A build whose output is current skips the compile: it prints `Up to date -> <path>` and exits 0, leaving
the output and its sidecar as they are. A build that compiles first says why, in one line on stderr
(`--log=error` silences it):

```text
[CMP] INFO: compiling app.exe: src/main.maxon was modified at or after the last compile started
```

Each compile leaves a **build record** under the cache root, at `<cache>/build/v1/<key>.mxbr`, where
`<cache>` is the first usable row of the table under [The run cache](#the-run-cache) and `<key>` a hash
of the output's physical path: every symbolic link above the output is followed, and the output's own name is
kept, so a link at the output path is keyed as the file the build writes there. It records:

- every source of the program, every standard-library and runtime source, and the Unicode tables the
  compiler embeds, each with its modification time — and on `wasm32-wasi`, `vendor/wasm-tools` and the
  files under `vendor/wasi-wit`;
- the target, whether the debug-info sidecar is written, `--coverage`, `--debugstream`,
  `--async-trace`, `--poison-freed`, the product version, and every define;
- the compiler's path, size and modification time;
- the size and modification time of the output and of its `.mxdbg` sidecar, and the time the compile
  started, read from the cache root's filesystem.

The output is up to date when all of these match the build at hand and every recorded file was last
modified before that compile started. A file modified in the same second the compile started counts as
changed, and so does a file restored with an older modification time. An up-to-date build leaves a
`--coverage` binary's `.mxcov` counters where they are, since they belong to that same binary.

The reason a compiling build gives is one of these:

| Reason | When |
|--------|------|
| `no record of a previous build` | The first build of this output, or the first since its record was removed |
| `<path> was modified at or after the last compile started` | A recorded file changed |
| `<path> was replaced by an older copy` | A recorded file now has an earlier modification time than the record holds, as a restored backup does |
| `source added: <path>`, `source removed: <path>` | The program or the library gained or lost a file; `tool added:` and `tool removed:` for the `wasm32-wasi` tools |
| `option <name> changed: <old> -> <new>` | `<name>` is `target`, `debugInfo`, `coverage`, `debugStream`, `asyncTrace`, `poisonFreed`, `sourceOrder` or `version` |
| `define <name> added`, `define <name> removed`, `define <name> changed: <old> -> <new>` | A `--define` or a described build's define differs |
| `the compiler changed: <old> -> <new>` | A different compiler, or a rebuilt one, runs the build |
| `the output is missing`, `the output was replaced since it was written` | The output is gone or differs from what the compile wrote; the same for `the debug-info sidecar` |
| `--rebuild was requested` | [`--rebuild`](#maxon-build) was given |
| `<flag> observes the compile` | See below |
| `the output is the running compiler` | See [Rebuilding a running compiler](/docs/cli/project-structure/#rebuilding-a-running-compiler) |
| `no build record can be kept: <why>` | See below |
| `the build record is unreadable: <why>` | The record is damaged |
| `the build record is of format vN, not vM` | The record was written in another format |

**These builds always compile, and remove the output's build record:**

- a build **observing the compile** — `--emit-ir`, `--emit-ir-function=`, `--metrics=`, `--census-by-tag`,
  `--allocations-by-tag`, or `--log=` at `debug` or `trace` for any category — because what it asked for is
  produced by the compile itself;
- a build whose output is the compiler running it;
- a build with **no record to keep**: no usable cache root, or an input that cannot be listed or read. It
  says `no build record can be kept: <why>` and otherwise builds and exits as usual;
- a build whose debug-info sidecar could not be written, so the next build compiles again.

`--rebuild` compiles whatever the record says and writes a fresh record. Builds of one output take turns
under its [build lock](/docs/cli/project-structure/#the-build-lock).

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

A described build can supply defines too; a `--define` on the command line wins over the described
build's.

## `maxon lint`

Runs the front end and every check on a program, the lint rules included, and stops before code
generation. It writes no executable, no sidecar and no build record.

```bash
maxon lint [--fix] [<file|directory|target>...] [options]
maxon lint --tests [--fix] [<target>] [directory | file.maxtest]
maxon lint [--fix] <name>.maxproj | <name>.maxtasks
```

**The lint rules** are the checks `maxon build`, `maxon execute`, `maxon run`, `maxon test` and the
compile of a project's build runner leave to `lint`, so those commands compile and run a program that
breaks one:

| Code | Rule |
|------|------|
| [E3064](/docs/cli/error-codes/#e3064--semanticdiscardedpureresult) | A pure function's result is discarded ([Function Purity and Discarded Results](/docs/language/functions/#function-purity-and-discarded-results)) |
| [E3065](/docs/cli/error-codes/#e3065--semanticdiscardedimpureresult) | An impure function's result is dropped by a bare call statement; `_ = f()` is the discard |
| [E3092](/docs/cli/error-codes/#e3092--semanticunusedexportedsymbol) | An `export` that no file outside its declaring directory's subtree uses |
| [E3219](/docs/cli/error-codes/#e3219--semanticunusedunexporteddeclaration) | An unmarked top-level declaration that nothing live in its directory's subtree names |

Each is an error, and `lint` exits 1 on any diagnostic. `maxon spec-test`, `maxon check-stdlib`,
`maxon check-scriptlib`, `maxon verify-recheck`, the MCP [`check`](/docs/cli/mcp-server/#check) and [`dump_ir`](/docs/cli/mcp-server/#dump_ir)
tools and the [language server](/docs/cli/editor/#maxon-lsp-server) compile with the lint rules too. Every other check
runs as it does in `maxon build`. A refusal by the back end, register pressure
([E5001](/docs/cli/error-codes/#e5001--registerpressure)) among them, is `maxon build`'s to report.

**The program is named as `maxon build` names one.** Several paths are one program, in the order given. A
lone word naming a target of the working directory's `.maxproj` file is that target, each `_` written
`-`, linted over the sources and defines its description states once the description is validated as
`maxon build` validates it; delegation is followed. With no path, `lint` lints the `.maxproj` file's only
target, lists the targets and exits 1 when there are several, and lints the working directory when there
is no `.maxproj` file. `--define=` and `--target=` act as they do for `maxon build`.

**A `.maxproj` or `.maxtasks` file named alone** is linted as its runner compiles it: the first target or
task it declares is the entry, and every exported target or task counts as used. A file holding nothing
but comments and blank lines is clean, and a file that declares something but no target or task is
refused. The runner is compiled for the host with no define, so `--target=`, `--define=` and any other
path beside it are refused.

**`--tests`** lints what [`maxon test`](#maxon-test) compiles: the project's sources and its `.maxtest`
files, chosen as `maxon test` chooses them, with the test entry `maxon test` generates. It takes
`maxon test`'s arguments, an optional target and then a directory or one `.maxtest` file, and the program's
own `main` counts as used. The program is staged in `<project>/.maxon/lint/`, or for one test file in
`<its directory>/.maxon/lint/<file name>/`, beside the `.maxon/test/` tree `maxon test` compiles from.
Diagnostics name the files you wrote. A project that declares no `test` is refused, and so is `--define=`
beside `--tests`.

**`--fix`** rewrites the program's own source files in place with the repair of every finding that has
exactly one mechanical repair, then lints again, round after round, until no finding it reports has one.
Several rounds are normal: E3092 and E3219 are reported only once nothing else stands, and a repair can
expose a finding, as dropping an `export` leaves its declaration unused and discarding an unused local
leaves a pure call's result discarded. It takes `lint`'s arguments — `--tests`, a manifest named alone,
`--define=` and `--target=` — and refuses `--metrics=` as a usage error, before anything is compiled.

- **Only the bytes of each repair are written.** The rest of the file keeps its bytes, its layout and
  line endings included, and a line break a repair inserts takes the file's own line ending, so a CRLF
  file stays CRLF.
- **The files written are the program's own.** A finding in `stdlib/`, `runtime/` or a
  [scriptlib](#the-scriptlib-directory) package is reported as `lint` reports it, with no repair. With
  `--tests`, the repairs go to the `.maxtest` files and sources you wrote, which the staged copies under
  `.maxon/lint/` are made from; a manifest named alone is repaired in that `.maxproj` or `.maxtasks` file.
- **A file that holds a conditional-compilation directive (`#if`) is left as written**, and every finding
  in it is reported as `lint` reports it. `--fix` repairs the program as compiled for the lint's target,
  the host's or `--target=`'s, and the text of such a file is compiled otherwise for another target.
- **A finding with two or more reasonable repairs stays as written**, and so does one whose repair the
  compiler cannot place with certainty in the text; each is reported as `lint` reports it. A duplicate
  match pattern ([E2027](/docs/cli/error-codes/#e2027--matchduplicatepattern)), an unused parameter
  ([E3012](/docs/cli/error-codes/#e3012--semanticunusedvariable)), and a discarded pure call whose
  arguments have an effect of their own, which records a run-time check, or whose callee can panic — where
  keeping the call for its effect or its abort is the second repair — are each of the first kind. A
  repairable finding standing beside
  an error that has no repair, such as a call to an undefined function, is repaired, and the error is
  reported.
- **A file whose bytes changed after the lint read it is refused**, with `error: <path> changed after lint
  read it, so none of its repairs were written` and exit 1; a file that cannot be read or written, or a
  repair naming bytes the file does not hold, is refused the same way, naming the file. Files are written
  in path order, so the files ahead of a refused one are rewritten and listed. Repairs that keep undoing
  one another end after 100 rounds with `error: lint --fix still found a finding with a repair after 100
  rounds, so its repairs are undoing one another` and exit 1.

**What `--fix` repairs.** The repair is the compiler's own, computed with the diagnostic; the
[language server](/docs/cli/editor/#maxon-lsp-server) offers the same repairs as quick fixes.

| Code | Finding | Repair |
|------|---------|--------|
| [E3065](/docs/cli/error-codes/#e3065--semanticdiscardedimpureresult) | A bare call drops an impure function's result | `_ = ` is written before the call |
| [E3064](/docs/cli/error-codes/#e3064--semanticdiscardedpureresult) | A pure function's result is discarded: a bare call, `_ = f()`, or a destructure into `_` alone | The statement is deleted, under the deletion rule below, when its arguments have no effect of their own, it records no run-time check (a range-checked narrowing, checked arithmetic), and the callee cannot panic, directly or through what it calls |
| [E3092](/docs/cli/error-codes/#e3092--semanticunusedexportedsymbol) | An `export` that no file outside its directory's subtree uses | `export` is removed |
| [E3219](/docs/cli/error-codes/#e3219--semanticunusedunexporteddeclaration) | An unmarked top-level declaration nothing live names | The declaration is deleted with the comment lines attached to it and one blank line |
| [E3062](/docs/cli/error-codes/#e3062--semanticunusedtypealias) | An unused `typealias` | The same deletion as E3219 |
| [E3012](/docs/cli/error-codes/#e3012--semanticunusedvariable) | An unused local whose initializer makes a call | The binding becomes `_ = <initializer>` |
| [E3012](/docs/cli/error-codes/#e3012--semanticunusedvariable) | An unused local whose initializer makes no call and cannot abort | The declaration is deleted, under the deletion rule below. An initializer that can abort at run time, such as a range-checked cast `wide as Small` or checked arithmetic on a full-width unsigned type, is left and reported; a cast of a constant is deleted |
| [E3012](/docs/cli/error-codes/#e3012--semanticunusedvariable) | An unused loop variable or payload binder | The name becomes `_` |
| [E3012](/docs/cli/error-codes/#e3012--semanticunusedvariable) | An unused caught-error binder, `otherwise (e)` | `(e)` is dropped |
| [E3081](/docs/cli/error-codes/#e3081--semanticmatchdiscardedbindings) | A match arm binds payload values it never reads | The binding list is dropped: `dot(_)` becomes `dot` |
| [E3077](/docs/cli/error-codes/#e3077--semanticvarshouldbelet) | A `var` that is never reassigned | `var` becomes `let`; a `var` bound to an array, map or tuple literal that is written through becomes a `let` of the literal's `.clone()` |
| [E3010](/docs/cli/error-codes/#e3010--semanticunneededcast) | An unneeded cast | ` as <type>` is deleted |
| [E3067](/docs/cli/error-codes/#e3067--semanticselfassignment) | A self-assignment, `x = x` | The statement is deleted, under the deletion rule below |
| [E3071](/docs/cli/error-codes/#e3071--semanticunreachablecode) | Statements after the one that leaves the block | Everything from the first unreachable statement, with the blank lines above it, through the last line before the block's `end` is deleted |
| [E3055](/docs/cli/error-codes/#e3055--tryrequiresthrowingfunction) | `try` on a call that cannot throw | `try` is dropped with exactly its own `otherwise` clause: a value, a string or a handler block. A second `try` on the line keeps its `otherwise`, and inside an interpolation or the head of a `match` or `for` the rest of the line, the block's label and a range's `to` or `upto` bound included, is kept, as is the `else` of a conditional expression. A `try` whose `otherwise` clause holds a token the repair cannot place, a `try` in an `if try`, `while try` or `if let x = try` condition, and one with a comment between the call and its `otherwise`, are left and reported |
| [E3083](/docs/cli/error-codes/#e3083--tryblocknothrows) | A block-form `try` whose body cannot throw | The body moves one tab left into the enclosing block and the handler is deleted, a handler block or a one-line `otherwise panic(…)` or `otherwise throws …`; a line that begins inside a literal keeps its bytes. It is left and reported when a name the block declares is spelled elsewhere in the enclosing function, which unwrapping would put in one scope |
| [E3168](/docs/cli/error-codes/#e3168--throwsfunctionneverthrows) | A `throws` clause nothing in the body throws through | The clause is dropped |
| [E2048](/docs/cli/error-codes/#e2048--redundantcontrollabel) | A `break` or `continue` names the construct it already leaves | The label is dropped |
| [E2052](/docs/cli/error-codes/#e2052--callfirstargnamed), [E2067](/docs/cli/error-codes/#e2067--builtinargnamed) | A positional argument carries a label | The label is dropped: `f(a: 1, b: 2)` becomes `f(1, b: 2)` |
| [E3075](/docs/cli/error-codes/#e3075--matchqualifiedcasename) | A match arm names a case qualified, `Color.red` | The case is written bare, `red` |
| [E2027](/docs/cli/error-codes/#e2027--matchduplicatepattern) | A range arm covers a single value, `5 to 5` | The arm is written as the value, `5` |
| [E3147](/docs/cli/error-codes/#e3147--matchorchainneedsownline) | An `or`-chain packs two alternatives onto one line | Each alternative goes on its own line after its `or`, at the arm's indentation |
| [E2008](/docs/cli/error-codes/#e2008--parsermismatchedendlabel), [E2043](/docs/cli/error-codes/#e2043--matchmismatchedblockid) | An `end` label differs from the label its header opened | The `end` label is renamed to the header's |

**The deletion rule.** A statement E3064, E3067 or E3012 deletes goes with every line it spans, and is
deleted only when it stands alone on those lines and its labeled block keeps a statement. Comment lines
are not statements, and the count is taken over every deletion of one round: of two statements that are
all of their block, the first is deleted and the second is left and reported.

**Options:**

| Option | Description |
|--------|-------------|
| `--tests` | Lint the program `maxon test` compiles in place of the one `maxon build` compiles |
| `--fix` | Rewrite the program's own sources with the repair of every finding that has exactly one, and lint again until none has one |
| `--define=<name>=<value>` | Replace a top-level `String` constant's written-out default, as for `maxon build`. See [Defines](#defines). |
| `--target=<cpu>-<os>` | Lint the program as compiled for this target: `#if` branches, target refusals and the lint rules all read that target |
| `--metrics=<path>` | Write the compile's per-phase time and memory to `<path>` as TSV, in the rows of [`maxon build --metrics=`](#maxon-build) less `code`. The code-generation phases read zero. Refused beside `--fix`. |

**Output.** Diagnostics go to stderr as `maxon build` prints them; E3092 and E3219 are reported once
nothing else stands. A target, and `--tests` of a project, print the
[breaking changes since the last build](#breaking-changes-since-the-last-build) on stderr, and the
project's compiler-version record is read and left as it is. A clean program prints one line on stdout,
`Linted <subjects>`, where `<subjects>`, separated by `, `, are the paths typed, else the sources the
target lists, else the working directory; for `--tests`, the directory or file typed, else the target
typed alone, else the working directory.

With `--fix`, stdout starts with one line per file rewritten, `Fixed <n> finding(s) in <path>`, in path
order, where `<path>` is the file as diagnostics name it and `<n>` counts the findings repaired in it over
every round. What follows is what `lint` says of the program as `--fix` left it: the findings that stand,
on stderr, and exit 1, or `Linted <subjects>` and exit 0. A program `--fix` has repaired is left as it is
by the next `--fix`.

| Code | Meaning |
|------|---------|
| `0` | The program is clean |
| `1` | A diagnostic stands, the command line is refused, or the program could not be read |
| the runner's | A project's build runner failed, for a target or for `--tests` |

```bash
maxon lint                           # the .maxproj file's only target, or the working directory
maxon lint src/ tools/gen.maxon      # one program from two paths
maxon lint my-app --target=x64-linux # the target my_app, as compiled for x64-linux
maxon lint --tests                   # what `maxon test` compiles here
maxon lint --tests src/lexer.maxtest # one test file's test program
maxon lint maxon.maxtasks            # the task file, as its runner compiles it
maxon lint --fix src/                # repair what has one repair, then report what stands
maxon lint --tests --fix             # the same over what `maxon test` compiles
```

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
  C:\Users\you\.maxon\cache (in use): 1 build, 3.2 MB
    library cache: 4 entries
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
included**, because that is what `clear` gives back. A row holding [library cache](#the-library-cache)
entries is followed by a `library cache: <n> entries` line counting them; their bytes are in the row's
size. The `Total:` line appears when more than one row holds something.

A root that **cannot be read** says so on its own line and the command exits 1, rather than being counted
as empty: a cache reported as holding nothing while it holds hundreds of builds is an answer you cannot
act on.

`clear` removes the compiled programs [`maxon execute`](#maxon-execute) and a path-less
[`maxon build`](#maxon-build) keep, the directories holding them, the inline snippets the
[MCP server](/docs/cli/mcp-server/) stages, the programs `debug_start` and `maxon dap-server` build for debugging,
the [library cache](#the-library-cache), the [build records](#up-to-date) `maxon build` keeps, the
[compiler-version records](#breaking-changes-since-the-last-build) of every project, and the Maxon
directory above all of those. The next `run` or path-less `build` compiles from scratch and fills the
cache again, the next compile of any kind fills the library cache, the next `build` of each output
compiles and records it again, and the next successful build of each project records its release.

**A [build lock](/docs/cli/project-structure/#the-build-lock) a live build holds stays**, with its claim file and the directories
above it, and `clear` ends its report with a `Kept <n> files a live build is using` line. Each lock is
judged and removed while `clear` holds its claim file, so a build starting meanwhile waits for it. The
`build/locks/` directory stays, empty or not. A lock `clear` cannot judge, or cannot remove, is reported
as the first thing that stayed, and the command exits 1.

**A debug build a live session is using stays**, with its sidecar and the directories above it, and
`clear` ends its report with a `Kept <n> files a live debug session is using` line. A debug build
belongs to a live session while the `maxon mcp-server` or `maxon dap-server` that built it runs on this
host, in this pid namespace. A server removes the debug builds and the staged snippets it made when the
session or call that needed them ends.

**A file one process leaves for itself is swept by the name it carries.** A debug build, a staged
snippet, an MCP result document and a build still being written into the run cache each end their name
in `<tag>.<start>.<pid>.<serial>`: a tag for the host and pid namespace, the start time and id of the
process that wrote it, and a serial. Each `maxon mcp-server` and `maxon dap-server` removes, as it
starts, the debug builds and snippets whose owner has ended — the leftovers of a server that was killed —
and a run that publishes a build removes the builds an ended process was writing into that program's
slot. Only an owner with this host's tag is judged: the files of another pid namespace, such as a
container sharing this cache directory, stay for that namespace to sweep, a host that cannot identify
itself (a Linux system with no machine id) judges no owner at all, and an ended owner whose id
the system has reused reads as alive until that process ends. A file in those places whose name carries
no owner, such as one an older Maxon wrote, is removed once it is 7 days old.

It removes **only what Maxon created**. `MAXON_RUN_CACHE_ROOT` may name a directory that is already
somebody's — a shared build area, or your own scratch directory — so the clear reaches only the Maxon
directory a row of the table under [The run cache](#the-run-cache) names, and stops there. The directory
you named, and everything else in it, is left exactly as it was. A symbolic link or junction under a cache
root is one entry: `clear` removes the link itself, counted among the files it removed, and the directory
it points at stays as it was.

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
- A word other than `clear` prints a `Usage:` line on **stderr** and exits 1, and any option is refused
  as [Commands](#_top) describes.

## `maxon fmt`

Re-prints Maxon sources in canonical layout, **in place**.

```bash
maxon fmt [<file|directory>]
```

With **no path it formats the whole working directory**. A named **file** is formatted whatever it is
called. A **directory** is walked for `.maxon`, `.maxtest`, `.maxproj` and `.maxtasks` files, skipping
any subdirectory the walk finds under a `.maxonignore` and any subdirectory that holds a `.git` (a nested
clone or worktree), so a run leaves another repository's files alone. As with `build`, the directory you
**named** is formatted whatever a `.maxonignore` on or above it says — naming it is the explicit act.
Symbolic links are followed, and each physical directory and file is formatted once, under the first
spelling the walk reaches, so a link back to an ancestor ends the walk there. A directory the walk cannot
list, or an entry it cannot identify, ends the run with `fmt: <reason>` and exit 1.

**It takes no options.** Any `-`-leading argument is refused with exit 1 and nothing written, and so
is a second path.

A file that is already canonical is not rewritten, so its modification time is untouched. A file with
no content, empty or only whitespace, formats to an empty file, so an empty marker such as a `.maxproj`
stays empty and counts as unchanged. The run prints one `formatted: <path>` line per rewritten file and
a `fmt: N file(s) changed, M unchanged.` summary.

`fmt` **refuses** a file whose layout it cannot tell: a file the lexer rejects, a file with a keyword
where `let`, `var` or `for` needs a name, and a file whose `end` lines do not match its blocks. The
diagnostic goes to stderr, the file is left as it is, and the other files are still formatted. The
summary then reads `fmt: N file(s) changed, M unchanged, K refused.` and the run exits 1. Every other
error in a file's grammar is reported by `maxon build`.

The formatter decides **blank lines** as well as indentation. Exactly one blank line separates adjacent
groups in a scope, and none appears inside a group:

| Inside | These pack together | These stand alone |
|---|---|---|
| a file, a `type` or `extension` body | `let`/`var` lines; `typealias` lines | every declaration that opens a body |
| an `enum` or `union` body | the cases | a nested declaration |
| an `interface` body | the bodyless signatures | anything else |
| a function or labeled block body | the statements, `let`/`var` included | every nested block |
| a `match` body | the arms | — |
| a multi-line `(`, `[` or `{` | *(your blank lines are kept, at most one in a row)* | — |

A `(`, `[` or `{` whose closer is on a later line indents the lines inside it one level, and a closer
that begins its line returns to the opener's indentation. After a multi-line parameter list the body is
indented one level from the declaration. A statement whose brackets span
lines is followed by a blank line.

A `///` doc comment stays attached to the declaration below it, and two `///` blocks with a blank line
between them keep it. A whole-line `//` comment attaches too when there is no blank line between them;
with a blank line it reads as a section heading. A blank line you put inside a group survives, at most
one in a row, and so does a blank line between two comments.

**Comments keep their text and their place among the code.** A comment after code stays at the end of
its line, two spaces after the code, and every comment sharing a line is kept, in order. A block comment
between two pieces of code on one line, such as `f(a, /* why */ b)`, stays where it is. A block comment
that ends before code on its line is written before that code. A block comment's first line takes the
indentation of the code around it; its further lines are kept exactly as written.

A match arm whose pattern is an `or` chain gets one alternative per line, each aligned with the first:

```maxon
match level 'filter'
	trace or
	info or
	warning then break
end 'filter'
```

```bash
maxon fmt              # the working directory
maxon fmt main.maxon   # one file
maxon fmt src/         # one directory
```

## `maxon test`

Runs a project's unit tests: every `test` declaration in its `*.maxtest` files, compiled together with
the project's `.maxon` sources. How to write tests is covered in [Testing](/docs/language/testing/).

```bash
maxon test [<target>] [directory | file.maxtest] [options]
```

`[directory]` is the project to test (default: the working directory). A `.maxtest` file named in its
place is run alone: it is compiled with the library tiers and nothing else, as the editor checks a file
no `.maxproj` marks, so its siblings and any project around it stay out of the run. A path naming
nothing is refused with ``test: no such directory or `.maxtest` file: <path>``, and any other file with
``test: not a directory or a `.maxtest` file: <path>``; both exit 2 with the usage line. A third positional is refused.

**The project's sources are the ones `maxon build` compiles for it.** When the directory's `.maxproj`
file declares a target, the run compiles that target's `sources`: the project file is run as
[`maxon build`](/docs/cli/project-structure/#the-project-file) runs it, delegation included, and the description's `sources` are
read, with every `.maxtest` file beneath them. A directory with no `.maxproj` file, or one whose file
declares no target, is every source beneath it. The target is chosen as `maxon build` chooses one:

- **One target** is used.
- **`<target>`** names one, each `_` written `-`, before the directory: `maxon test gen-tool tools/`.
  A target word with a `.maxtest` file is refused, and so is a word the project declares no target for,
  which lists the targets it does declare.
- **A lone word** naming a target of the working directory's project is that target, and it outranks a
  directory of the same name ([Several targets](/docs/cli/project-structure/#several-targets)). Any other lone word is a path.
- **Several targets and no word** are listed, and the run exits 2.

A project file that does not compile, fails, or describes no build ends the run with its error and exit 2.

All discovered tests are compiled into **one binary** with a generated entry point, and which of them
run is a runtime argument. Changing `--filter` between runs therefore recompiles nothing. The project's
own `main` needs no change: the test binary does not use it, and the same directory still builds
normally with `maxon build`.

**Options:**

| Option | Description |
|--------|-------------|
| `--filter=P` | Run only tests whose name or file path contains `P` (case-insensitive). Repeatable, and one value may hold comma-separated patterns; the run takes every test any pattern selects. A pattern that selects no test ends the run with `no test matched 'P'` and exit 1, and a value naming no pattern (`--filter=,`) is refused with exit 2. |
| `--list` | Print the tests that would run. It compiles only with `--build`. A project whose sources do not all tokenize is still refused, so the list holds every file's tests. |
| `--build` | With `--list`: also build the test binary, with its debug-info sidecar, and stop before any test runs. The build happens when at least one test is selected, and `--build` without `--list` is refused. The listing names the absolute path of the binary (`binary` under `--json`) and, under `--json`, gives each test a `select` value: see [Running the test binary](#running-the-test-binary). |
| `--no-debug-info` | Build the test binary without its `.mxdbg` debug-info sidecar, as [`maxon build --no-debug-info`](#maxon-build) does. |
| `--json` | Emit the report as JSON instead of text. |
| `--isolate` | Run every test in its own process, instead of one process per test file. |
| `--bail`, `--bail=N` | Stop starting new work after `N` failures (`--bail` alone means 1). Work already running finishes, so the count may exceed `N`. Without it, every test runs. |
| `--timeout=<ms>` | Kill a test process after this many milliseconds (default 120000). One process runs one file's tests, so the deadline covers the whole file unless `--isolate` is given. |
| `--no-timing` | Omit durations, making stdout byte-for-byte reproducible. |
| `--color=auto\|always\|never` | Colour the report. `auto` colours only when stdout is a terminal, `NO_COLOR` is unset and `TERM` is not `dumb`. On `wasm32-wasi` a program cannot detect a terminal, so `auto` means `never` there; use `always` to force colour. |
| `--target=<cpu>-<os>` | Build the test binary for this target and then run it. On a host that cannot execute that target, every test is reported as not run. |

The run is serial; there is no `--workers` option.

**Breaking changes.** A project's run prints the
[breaking-change report](#breaking-changes-since-the-last-build) `maxon build` prints, before its tests
run: on stdout, or on stderr under `--json` so stdout stays one JSON document. A run in which every
test passes records this compiler's release for the project, and a compiler-version record that does
not hold a release version ends the run with its error and exit 2.

**Where it runs.** The test binary runs in the directory `maxon test` was invoked from, so a relative
path in a test means what it means in your shell. The build lives in `<project>/.maxon/test/`, or for a
test file run alone in `<its directory>/.maxon/test/<file name>/`, under a `.maxonignore` that keeps
its generated sources out of every ordinary build. [`maxon lint --tests`](#maxon-lint) stages the same
program in `.maxon/lint/`. The run empties the build's `staged/` directory before it stages into it; an
entry it cannot remove there refuses the run with
`test: could not remove <paths> from <directory>, so nothing was staged into it`, exit 2, and no test runs.

**The lint rules.** The test binary is compiled as `maxon build` compiles a program, so the
[lint rules](#maxon-lint) are left to `maxon lint --tests`, which checks this same program.

**Scratch directories.** Each test process gets two empty directories of its own, named in its
environment: `MAXON_TEST_SCRATCH`, in the system temp area (`maxon-test-<pid>`, the pid of the process
that runs the tests), for anything that must sit outside every checkout, a staged project included; and
`MAXON_TEST_PROJECT_SCRATCH`, `<project>/.maxon/test/[<file name>/]scratch-<pid>`, for what must sit inside
the tree, such as a copy of a compiler that finds `stdlib/` by walking up from itself. Each is named by its
physical path, every symbolic link along it resolved, so a path a test prints from inside it is the path the
variable holds. A test reads them through [`TestScratch`](/docs/stdlib/testing/#testscratch). Both are removed
when the process ends, and a process that passes but leaves an entry in either FAILS, each entry named as
`left MAXON_TEST_SCRATCH/<name>`. A run removes the scratch directories that runs whose process has ended
left. Each removal waits up to about 2 s for a file another process holds open against deletion, as a
process outside the run can on Windows for a moment after a program runs.

**Processes a test leaves.** On every native target a test that passes but leaves a process running when
it ends FAILS, naming each one as `<pid> <name>`, and the process is ended. A process the test waited for
has ended by then; a test that starts a detached child polls `Process.isAlive` until it answers `false`.
The check is the one [`spec-test`](/docs/cli/compiler-development/#maxon-spec-test) makes, and `MAXON_TRACK_DESCENDANTS`
arms it. The test binary names each process in a stderr frame ending with the token `maxon test` armed it
with, passed to the binary as `--descendant-token=<token>`, and its `ended` line for the test carries the
count; `maxon test` takes out the frames bearing its token, and a count that differs from them fails the
test. A frame a test prints with any other token, or none, is test output.

**The hash seed and tier.** A run draws one hash seed and hands it to every test process as
`MAXON_HASH_SEED`, with a `MAXON_HASH_TIER`: `aes` when the target has an `aes` tier, the test binary runs
natively on this CPU and the CPU has AES, else `software`. A non-empty value of either variable already in
`maxon test`'s own environment is kept. Every failure block ends with the pair, at the block's indent:

```text
  MAXON_HASH_SEED=<16 hex digits> MAXON_HASH_TIER=<tier>
```

Under `--json`, the record of every test that did not pass carries the same pair as `hashSeed` and
`hashTier`. Running with both variables set to the pair named reproduces the run's hashing.

**Isolation.** By default each test **file** runs in its own process. If a test crashes the process, the
harness re-runs the remaining tests of that file so one crash does not hide the others' results, and a
leak found at exit, an entry left in the process's scratch directories, or a process left running after
its last test is attributed by re-running tests alone. When no single test reproduces it alone, every test
of that process is reported (`LEAKED` for a leak, `FAIL` for the others) with the names of the tests in
the process, so a leak never passes unreported. `--isolate` gives every test its own process from the
start.

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
| `2` | The run could not happen: a bad option, a compile error, no source files, no such project, a project file that gave no sources, several targets and no target word, or a malformed compiler-version record |

A run that finds no tests exits 1 on purpose, so a suite that silently stopped containing tests does
not read as green. A source file that does not tokenize is a compile error like any other: the run
exits 2 with the diagnostic, rather than reporting that file's tests as absent.

### Running the test binary

`maxon test --list --build --json` builds the test binary without running it, for a tool that runs it
itself, such as a debugger. Run from the directory `maxon test` would run it in, the binary given
`--select=<select>` (a listed test's `select` value) runs that test alone, and exits:

| Code | Meaning |
|------|---------|
| `0` | Every test it ran passed |
| `3` | A test failed |
| `101` | A test leaked (see [The leak check](/docs/cli/debugging/#the-leak-check)) |
| `1` | A panic |

The VS Code extension's **Debug Test** runs it this way. A binary that finds a process a test left running
panics, naming `--descendant-token`, unless it was given `--descendant-token=<token>`, which `maxon test`
passes.

**Example.** A failing expectation, then the fix:

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
// pricing/pricing.maxtest
test 'a small order pays full price'
	Expect.equal(totalCost(250, quantity: 4), expected: 1000)
end 'a small order pays full price'

test 'ten items take the bulk discount'
	Expect.equal(totalCost(250, quantity: 10), expected: 2500)
end 'ten items take the bulk discount'
```

```text
$ maxon test pricing
pricing/pricing.maxtest:
  ✓ a small order pays full price                    0.00ms
  ✗ ten items take the bulk discount                 0.00ms

FAIL  pricing/pricing.maxtest > ten items take the bulk discount
  FAIL pricing.maxtest:6: Expect.equal
    expected: 2500
    received: 2250

 1 pass
 1 fail

 2 tests across 1 file.   compile 947ms, run 38ms
```

The `file:line` on the `FAIL` line is the assertion's own. Whatever a failing test printed appears in its
report, indented under its heading, line for line and blank lines included. Correct the expectation to `2250` and the same
command reports `2 pass`, `0 fail` and exits 0.

```bash
maxon test                        # every test under the working directory
maxon test src/parser             # one project's tests
maxon test src/lexer.maxtest      # one test file's tests, compiled alone
maxon test --filter=json          # only tests whose name or file mentions "json"
maxon test --filter=parser,lexer  # two patterns, as a union
maxon test --filter=parser --filter=lexer  # the same union, one flag per pattern
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

An install keeps `<root>/scriptlib/` as it is. When it exists, the script runs the newly installed
compiler's [`maxon check-scriptlib`](#maxon-check-scriptlib) once the new release is in place; a
package that release rejects is printed with its diagnostics and the line
`<root>/scriptlib needs updating for Maxon <version>`, and the install completes with its own exit
status.

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
with every option each command reads listed under it, `--log=` aside (see [Logging](#logging)). An
option several commands accept (such as `--target=`) is listed under each. Commands are listed alphabetically.

`maxon help <command>` prints one entry. A word that names no command is refused with exit 1 and the
command list, and a second word after the command (`maxon help build run`) is refused with exit 1 as an
unexpected extra argument, named on stderr. `help` takes no options.

## Logging

Every command but `cache`, `dap-server`, `fmt`, `help`, `mcp-server` and `upgrade` accepts a logging
option. With [`maxon execute`](#maxon-execute), a `--log=` after the path belongs to the program; written
before the command word (`maxon --log=compiler:debug execute app.maxon`) it is the driver's, and it also
brings back the build output `execute` otherwise keeps quiet.

| Option | Description |
|--------|-------------|
| `--log=LEVEL` | Set every category to `LEVEL` |
| `--log=CATEGORY:LEVEL` | Set one category to `LEVEL` |

**Levels:** `none`, `error`, `info` (the default), `debug`, `trace`

**Categories:** `compiler`, `lexer`, `parser`, `semantic`, `ir`, `codegen`, `binary`, `testing`

Log lines go to stderr, prefixed with the category and level (`[CMP] INFO: …`). An unrecognized spec
stops the command before it runs, with `error: invalid option value: <arg>` and exit 1 (2 for
`maxon test`). `maxon test` lowers the level to `error` unless you pass
`--log=`, so its stdout is only the report, and `maxon lsp-server` lowers it the same way for its whole
session.

```bash
maxon build app.maxon --log=codegen:trace
maxon build app.maxon --log=compiler:debug   # per-phase time and memory as a table
maxon build app.maxon --log=error            # only errors
```

**The type-name table.** `--log=compiler:debug` also prints one line for each source file a fold merges —
`typeNames <path>: <N> names, <B> bytes, reserved <RN> names / <RB> bytes, rehashes <H>, regrowths <G>` —
the names that file interned and the arena bytes they occupy, what its table reserved ahead of them, and
how many times the table rehashed or had to grow. A compile's first fold merges every file; a later fold
merges only the files from the first one whose parse changed, so a file can report more than once.

**The merge.** The front end folds every file's parse into one module, and folds again each time that
module reaches a standard-library body the round before left unbuilt. Each of those rounds prints
`merge: <K> fold(s), <R> of <N> file(s) re-merged` — the folds the round took, and how many of the
program's `N` files its last fold merged; it kept the other `N − R` from the fold before.

**The shared library memo.** A process that compiles many programs — a `spec-test` worker, the MCP
server, the language server — keeps each library file's tokens and parse across compiles, and
`--log=compiler:debug` prints one `shared stdlib memo` line per compile: the token, active-token and
producer-mask hits, the files admitted and held, then the parse artifacts held, the parse hits, misses and
publishes, how many held parses missed because ids they reference moved, and whether this program may
share library parses at all (`shareable`, or the reason it parses the library cold: `shadowed`,
`namesOverlap`, `rootInsideStdlib`). A view or parse loaded from the [library cache](#the-library-cache)
counts as a hit here.

**The library cache.** After its front end, every compile prints one line:

```text
library cache — baseline <loaded|built|unused>, <n> loaded, <n> written, <n> rejected, <n> library file(s) swept, <n> library file(s) parsed
```

`baseline` says where the library's declarations came from: `loaded` from the cache, `built` by this
compile (which then wrote them), or `unused` when the program sweeps the library itself. The counts are the
cache entries this compile loaded, wrote and rejected, then the library files whose declarations it swept
and the library files it parsed. `rejected` includes the entries removed, and a damaged `hash-seed`
replaced, when this compile wrote the tier's seed. The lines prefixed `library cache:` say why an entry was
rejected or a baseline went unused, or, as `no library cache: <reason>`, why the compile ran without it.

**The front-end pool.** `front end: <W> worker(s) over <P> processor(s), <n> file(s), <n> adopted` sizes
the pool that lexed and parsed the files no memo held. `adopted` counts the workers handed the signature
index before they parse: a warm compile of a one-file program adopts at most one. The next line,
`front end index overlay: <n> allocations`, is the allocation count of creating the overlay a parse
writes into over the index the workers share. The line after it, `front end index copy: <n> allocations`, is
the allocation count of one whole copy of that shared index, the figure the overlay's count is read against. A compile that keeps that work on the compiling thread prints
`front end: serial, in process, <n> file(s) — <reason>`. The work stays there when the compiler's runtime
cannot move a shared-record graph between threads, and under [`--allocations-by-tag`](#maxon-build), whose
tally attributes each stage to the thread that runs it.

**The Std fan-out.** `--log=codegen:debug` prints `stdFanOut: <N> of <M> function(s) hold an op`: the
per-function Std passes run on a worker pool over the `N` of the module's `M` functions that hold at least
one op. An unreached library body holds none, so `N` follows what the program reaches. The next line,
`stdFanOut: <W> worker(s) over <P> processor(s), <N> function(s)`, sizes that pool.

**The census by tag.** [`--census-by-tag`](#maxon-build) adds a third table under the residency one: the
live heap at each phase boundary broken down by the type each allocation was tagged with. It prints the
top five buckets for every phase sampled, then the whole table for the phase whose *live* level was
highest (or for the named phase, when one is named), and closes with three lines that bound what the table
can be trusted to say — the OS-direct mappings and allocator overhead no row counts, how many live slots
carry no box header (those are attributed by whatever their first word holds, so they land in
`(unattributable)` or in a bucket that is not theirs), and a self-check comparing the buckets' sum and the
tally's own walk against `slabLiveBytes`. A tag past the end of the table is `(overflow)`, and bucket 0,
which no type's tag occupies, is `(untagged)`.

The census samples the heap once per boundary: `lex` and `directives` at the end of the front end's lexing
drain, from one walk of the heap, and `parse` at the end of its parse drain. A phase that runs on the
Std-pass or backend worker pools (`isel` and `encode` inside `regalloc`, the Std passes inside
`stdFanOut`) has no boundary of its own, because the work of concurrent workers cannot be told apart.
Naming one yields a report that says so and names the driver phase to name instead.

Without `--log=compiler:debug` the flag still samples, but the numbers go only to `--metrics=<path>`.

```bash
maxon build app.maxon --log=compiler:debug --census-by-tag
maxon build app.maxon --log=compiler:debug --census-by-tag=regalloc  # one phase, on a large compile
```

**The allocations by tag.** [`--allocations-by-tag`](#maxon-build) answers the other question: not what
the heap is *holding* at a boundary but what the phase *asked for* between two of them, which is the
figure an allocation that is freed before the boundary appears in at all. It prints the top five buckets
for every phase, then the whole table for the phase that allocated most, and closes with a self-check:

```text
Σ over the sampled phases and buckets = <X> (and <N> more outside the phases sampled), the run's own allocation total at the last boundary = <Z> — equal
```

Each interval between two phase boundaries is credited to the innermost phase open across it, so a phase
that excludes a nested one (`load` and `loadRuntime`) owns only its own allocations. Allocations made
between phases, and those of every phase the run did not name, are the `<N>` outside the phases sampled,
and the two sides are equal exactly. The tally is armed inside `loadProject`, so `load` is credited from
there and the moments before count as outside. The front end runs serially on the compiling thread, so
`lex`, `directives` and `parse` are tallied like any other phase. A phase that runs on the Std-pass or
backend worker pools (`isel` and `encode` inside `regalloc`, the Std passes inside `stdFanOut`) cannot be
named: the report says so and names the driver phase to name instead. A compiler built without
`--debugstream` carries no tags, so every allocation is counted under `(untagged)`, per phase, and the
headline says so.

```bash
maxon build app.maxon --log=compiler:debug --allocations-by-tag
maxon build app.maxon --log=compiler:debug --allocations-by-tag=regalloc   # one phase
```

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | The command ran and failed: a compile error, a failing check, a command line it cannot act on |
| `2` | Nothing ran: the tree is not in a state to work on (another command holds its [tree lock](/docs/cli/project-structure/#the-tree-lock) or the output's [build lock](/docs/cli/project-structure/#the-build-lock), or a compiler binary is older than its sources), or a `maxon test` run could not happen |
| `101` | A program's leak check found an allocation still held at exit. The program reports it, not the driver; `maxon test` reports it as `LEAKED`. |

Some commands forward another process's exit code instead: [`maxon execute`](#maxon-execute) returns the
program's, and [`maxon upgrade`](#maxon-upgrade) the install script's. For those, only `1` is ever the
driver's own. `maxon monitor`, `maxon coverage` and `maxon profile` have their own tables on
[Debugging and Profiling](/docs/cli/debugging/).

## Environment Variables

**Read by the compiler:**

| Variable | Read by | Effect |
|----------|---------|--------|
| `MAXON_RUN_CACHE_ROOT` | `run`, `build`, `cache`, every compile (the [library cache](#the-library-cache)) | Maxon caches under `<value>/maxon`. Consulted first |
| `USERPROFILE` | `run`, `build`, `cache`, every compile (the [library cache](#the-library-cache)) on Windows | Maxon caches under `<value>\.maxon\cache` when `MAXON_RUN_CACHE_ROOT` is unset or cannot be created |
| `HOME` | `run`, `build`, `cache`, every compile (the [library cache](#the-library-cache)) elsewhere | Maxon caches under `<value>/.maxon/cache` when `MAXON_RUN_CACHE_ROOT` is unset or cannot be created |
| `LOCALAPPDATA`, then `TEMP` | `run`, `build`, `cache`, every compile (the [library cache](#the-library-cache)) on Windows | Last resort, under `<value>\maxon`, when the two above name no directory Maxon can create |
| `TMPDIR` | `run`, `build`, `cache`, every compile (the [library cache](#the-library-cache)) elsewhere | Last resort, under `<value>/maxon`, when the two above name no directory Maxon can create |
| `NO_COLOR`, `TERM` | `test --color=auto` | Set `NO_COLOR`, or `TERM=dumb`, to turn colour off |
| `MAXON_IMAGE` | `upgrade` | Marks the container image; `upgrade` refuses and names `docker pull` |
| `MAXON_INSTALL` | `upgrade` (written, not read) | `upgrade` sets it for the install script to the install the running compiler sits in, whatever your shell says |
| `MAXON_COMPILER` | `build` (project target), `run` (written, not read) | Set for the project file or task program to the path of the compiler running it, so the program can ask that compiler, for example its `maxon version` |
| `MAXON_SECOND_STAGE` | `build` (project target), `run` | Set, with any value, it makes a build asking for [`rebuild_with_output`](/docs/cli/project-structure/#building-again-with-the-written-program) end at its own output. The driver sets it to `1` for the program it runs |
| `MAXON_SOURCE_ORDER` | every compile; `spec-test` (test-only) | `reversed`, the only value, reverses the order the compiler reads the program's and the library's files in, and the order of each file's top-level declarations, so a suite run under it checks that no result depends on file or declaration order. A declaration's block must close with an `end` in the same column as the declaration's first token; a top-level line that starts no declaration, or a block closed any other way, is the error `<path>:<line>: MAXON_SOURCE_ORDER=reversed reverses top-level declarations, and it cannot place the top-level declaration at this line`; `spec-test` counts a case that error stops as SKIPPED. Any other non-empty value is the error `MAXON_SOURCE_ORDER is '<value>', and its only value is 'reversed'`. `spec-test` exits with an error naming the variable for a target whose suite runs in a copy of the compiler inside the OrbStack machine, because the variable does not reach that copy. A build records the value, so changing it rebuilds (`option sourceOrder changed`). Unset or empty reads files in the order the walk finds them |
| `MAXON_TEST_SERVICE_TAIL_STALL_MS` | every compile (test-only) | A count of milliseconds from 0 to 60000. Each compile worker sleeps that long after it sends an answer, before the answer's payloads are released, so a test can check that a memory report does not depend on how soon a worker finishes. Unset reads as 0; a value that is not a count in range is a panic. Counted allocation bytes follow the length of the environment, so two runs compared set it to values of equal length |
| `MAXON_TEST_SCRATCH`, `MAXON_TEST_PROJECT_SCRATCH` | `test` (written, not read) | Set for each test process to its two scratch directories, read through [`TestScratch`](/docs/stdlib/testing/#testscratch): see [`maxon test`](#maxon-test) |

**Read by a compiled program at run time:**

| Variable | Effect |
|----------|--------|
| `MAXON_MAX_PROCS` | The number of processors the green-thread scheduler runs on. Default: the machine's processor count. A decimal number from 1 to 2147483647, optionally led by `+`, sets it exactly, above the machine's count as well as below it; any other value is ignored. `Scheduler.processorCount()` answers the resulting count. |
| `MAXON_HASH_TIER` | Forces the tier the seeded hash ([`Hasher`](/docs/stdlib/data/#hasher), [`KeyedHasher`](/docs/stdlib/data/#keyedhasher), and every `String`, `Character` and `Array` `hash()`) runs on: `aes` or `software`. Unset or empty, the program takes the best tier the host has, `aes` before `software`; `software` is one function on every target, and the only tier `wasm32-wasi` has. A value that names no tier ends the program before `main` with exit code 1 and `MAXON_HASH_TIER=<value> is not a hash tier: expected aes or software` on stderr, and a tier the host lacks with `MAXON_HASH_TIER=<value> is not available on this host: its best hash tier is <best>`. A program reads this variable and `MAXON_HASH_SEED` only when it contains the hash runtime, and then checks both before `main`, whether or not it reaches a hash; a program with no hash runs whatever they hold. The compiler reads it too, and its library cache keeps one directory per tier. |
| `MAXON_HASH_SEED` | Pins the process's hash seed, the one `Hasher` and every `hash()` fold from, which is otherwise drawn from the operating system at startup, and the sequence of seeds [`HashSeed.random()`](/docs/stdlib/data/#hashseed) draws, which is otherwise fresh entropy at each call. Under a pin, the n-th draw of a process is the same seed on every run and on every tier, whichever thread makes it, and a pinned process asks the operating system for no entropy at startup. The value is 16 hexadecimal digits, upper or lower case, read in order as the seed's 8 bytes. Unset or empty draws a seed. Any other value ends the program before `main` with exit code 1 and `MAXON_HASH_SEED=<value> is not a hash seed: expected 16 hexadecimal digits` on stderr. A pin is readable by the program and inherited by the processes it starts, and a pinned process has no collision resistance against anyone who knows the pin, so a deployed program runs unpinned. [`scale-test`](/docs/cli/compiler-development/#maxon-scale-test) sets it for every compile it measures, and [`spec-test`](/docs/cli/compiler-development/#maxon-spec-test) and [`maxon test`](#maxon-test) for every program they run. |
| `MAXON_PREEMPT` | `off` stops the scheduler from preempting a green thread that holds a processor, for a deliberate, reproducible run. Unset, empty or `on` is normal preemption. Any other value aborts the program at start. |
| `MAXON_TRACK_DESCENDANTS` | Set by `maxon test` and `spec-test` for every program they run on a native target. A value starting `A` arms descendant tracking when the program starts, so [`Process.endDescendants()`](/docs/stdlib/io/#process) finds every process it started; the text after the `A` is the token the program ends each stderr frame with, naming a process left running (`@@maxon-spec@@!<pid> <name>`, a tab, the token); the program then rewrites the `A` to `D`, so the processes it starts inherit the variable without arming. A host that refuses the tracking ends the program at start with a fatal naming it. With any other value, or unset, a program that calls `Process.endDescendants()` tracks its descendants from startup, for that call alone; every other program runs untracked. |

`MAXON_DEBUGSTREAM` is set by `maxon monitor`, and by `maxon debug --trace`, to attach a `--debugstream`
program to the ring the driver created. You do not set it yourself.
