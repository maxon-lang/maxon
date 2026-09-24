---
title: Project Structure
description: Which files a build includes, the build manifest, ignored directories, the .maxon/ directory, and the tree lock.
sidebar:
  order: 2
---

A Maxon project is a directory of `.maxon` files marked by a `<name>.maxproj` project file at its root.
This page covers what the command-line tools read from and write into a project. The language side of
project and task files is described in [Build System](/docs/language/build-system/), and how files
map to namespaces in [Namespaces](/docs/language/namespaces/).

```text
myproject/
├── myproject.maxproj    # the project file: marks the root, describes the builds
├── myproject.maxtasks   # optional: the tasks `maxon run` offers
├── main.maxon           # the entry point (contains main)
├── utils.maxon
├── lib/
│   ├── math.maxon
│   └── io.maxon
├── pricing.maxtest      # tests: compiled by `maxon test` only
└── fixtures/
    ├── .maxonignore     # this directory is skipped
    └── sample.maxon
```

Each kind of file has its own extension, and each command reads its own kinds:

| File | Read by |
|------|---------|
| `*.maxon` | every build, `maxon test` and `maxon execute` |
| `*.maxtest` | `maxon test`, beside the project's `.maxon` sources |
| `<name>.maxproj` | `maxon build` with no path or with a target word |
| `<name>.maxtasks` | `maxon run` |

A directory holds one `.maxproj` file and one `.maxtasks` file at most; two of one kind in one directory
are refused, naming both. A file named `project.maxon` or `tasks.maxon` is an ordinary source file.

## Which files a build includes

`maxon build <directory>` compiles every `.maxon` file beneath the directory as one program, except
anything under a `.maxonignore` (below). `.maxproj`, `.maxtasks` and `.maxtest` files are outside every
build: test sources are a separate category that only `maxon test` compiles.

**One project per tree.** A `.maxproj` file inside the tree of another project is refused with
[E2074](/docs/cli/error-codes/#e2074--nestedprojectfile), whichever of the two projects is built, and
`maxon init` refuses to create one there.

The standard library is part of every compilation; the compiler finds it by walking up from its own
executable, so nothing in the project refers to it. See the [Standard Library](/docs/stdlib/).

A file of the compiler's own `stdlib/` or `runtime/` that a build names — as its path, or in a
described build's `sources` beside a program — is read once, as part of the library. A `runtime/` file
is compiled as runtime-tier source wherever it is named from, with the tier's rules
([The runtime tier](/docs/stdlib/#the-runtime-tier)); built on its own it has no `main`, so the
build stops at [E3001](/docs/cli/error-codes/#e3001--nomainfunction).

## The project file

`maxon build` with no path looks for the **`.maxproj`** file in the current directory, compiles it, runs
the chosen target, and performs the build that target describes.

**A project file is a program.** It is ordinary Maxon with the whole standard library available, so a
build can compute what it compiles (read a directory, choose by host, derive a version from git) as well
as list it. **Its targets are its exported (or `public`) functions of no parameters returning
`ExitCode`**, declared at the left margin. A non-zero return or a crash fails the build and nothing is
compiled.

```maxon
// myapp.maxproj
export function build() returns ExitCode
	Build.build("src", version: "1.4.2")
	return 0
end 'build'
```

The project file is compiled **for the host**, whatever `--target` says, because this machine runs it.
It is compiled on its own (the project's other files are outside it) and kept in the
[run cache](/docs/cli/#the-run-cache), outside the project.

That compile is **silent**, and says only which of the two things happened to it:

```text
Compiled the build runner (myapp.maxproj)     # nothing in the cache matched, so it was compiled
Used the cached build runner (myapp.maxproj)  # the cache had it, and nothing was compiled
```

The line goes to stdout, ahead of the build's own report. The runner is scaffolding, so a reader gets
one line about it in place of a second build's worth of output mixed into the answer they asked for —
and **silenced is still heard**: a project file that fails to compile prints its diagnostics, and
`--log=` anywhere on the command line leaves the runner's compile as loud as any other.

### Describing a build

A target describes **one** build by calling `stdlib/Build.maxon`, which writes it as JSON to the file the
driver names in **`MAXON_BUILD_DESCRIPTION`**. The compiler reads that file back. A target or task that
describes a second build in one run is refused.

**The description has a channel of its own so that stdout stays the program's.** A target or a task can
print whatever it likes as it works, and it streams to the caller live. With the variable unset, the
description goes to stdout, which is the one way to inspect it directly.

| Call | Meaning |
|---|---|
| `Build.build(source, output:, debugInfo:, version:, defines:)` | Build one file or directory to one output. The common case. |
| `Build.buildWithConfig(config)` | Build one `BuildConfig`, which can list several sources, compiled as one program in order. |
| `Build.delegate(directory, target:)` | Hand the whole description to a target of another directory's `.maxproj` file. |

`output` defaults to `""`, `debugInfo` to `true`, `version` to `""` and `defines` to an empty list. **A
build that states no output is written to `.maxon/<name>`**, `<name>` being the project file's name
without its extension (for a task, the `.maxtasks` file's). The keys the driver reads from the JSON are:

| Key | Type | Meaning |
|-----|------|---------|
| `output` | string, required unless `directory` | Where the executable goes, without the extension. The compiler adds `.exe` for Windows, `.wasm` for `wasm32-wasi`, and nothing for Linux and macOS. Relative to the current directory. Empty means `.maxon/<name>`. |
| `sources` | list of strings, required unless `directory` | The files and directories to compile, in order. An empty list is refused. |
| `directory` | string | A directory whose own `.maxproj` file describes this build. Stating it alongside `sources` is refused. |
| `target` | string | With `directory`, which of the delegated project's targets to build, as `maxon build` spells it; empty means its sole one. |
| `debug_info` | `true` or `false` | Whether to write the `.mxdbg` sidecar (default `true`). |
| `version` | string | A dotted version stamped into the binary: a `VS_VERSIONINFO` resource on Windows and `LC_SOURCE_VERSION` on macOS. Linux and `wasm32-wasi` binaries carry no product version. Without it, the binary reports `0.0.0.0`, and a missing component is 0. A component that is not a number is refused on every target, and one the target's field cannot hold is refused too: each Windows component holds 0 to 65535 (four at most); on macOS the first holds 0 to 16777215 and the next four 0 to 1023. |
| `defines` | list of `name=value` strings | The same as [`--define=`](/docs/cli/#defines) on the command line. |

A field that is present but malformed is **refused**, naming the key, for example
``error: myapp.maxproj's `sources` is not a list of strings``. A description that fails to parse as JSON
is refused with what the target wrote, and a target that exits 0 having written none is refused as
describing no build.

### Delegating to another directory

A target can hand the whole description to a target of another directory's `.maxproj` file:

```maxon
export function build() returns ExitCode
	Build.delegate("maxon-bin")
	return 0
end 'build'
```

The compiler runs that directory's project file **with the directory as its working directory**, and
resolves the relative `sources` and `output` it states against it — so the delegated project builds the
same thing whether it is reached from above or built from inside. `target:` picks one of its targets the
way `maxon build <word>` does; left empty, a project with one target builds it and one with several
lists them and refuses. The command line's `--output=`, `--target`, `--define` and `--no-debug-info` are
applied afterwards, exactly as they are to a build described in place. Delegation more than eight deep
is refused as a cycle.

### The task file

A `<name>.maxtasks` file holds the tasks [`maxon run`](/docs/cli/#maxon-run) offers in its directory. It sits
wherever tasks are wanted — beside a project file, or in a directory that is no project at all. The two
kinds of file have separate jobs:

- **The `.maxproj` file says what the project IS.** Its presence marks where a project begins — the
  editor's project root ([Editor Support](/docs/cli/editor/)) — and its targets describe the builds.
- **The `.maxtasks` file says what a person DOES here.** It marks nothing, so a directory of scripts
  holding one stays a directory of scripts.

Both stand apart from the program (see [Which files a build includes](#which-files-a-build-includes)).

Every target a `.maxproj` file declares and every task a `.maxtasks` file declares counts as an entry
point, whichever one was asked for: the runner reaches the others by name, so an exported target or task
is an entry point and stays clear of
[E3092](/docs/cli/error-codes/#e3092--semanticunusedexportedsymbol).

### Several targets

```maxon
// myapp.maxproj
export function app() returns ExitCode
	Build.build("src")
	return 0
end 'app'

export function gen_tool() returns ExitCode
	Build.build("tools/gen.maxon", output: ".maxon/gen")
	return 0
end 'gen_tool'
```

```bash
maxon build            # with one target, builds it; with several, lists them and exits 1
maxon build app        # builds the target `app`, into .maxon/myapp
maxon build gen-tool   # builds the target `gen_tool`
```

The listing spells each target as `maxon build` takes it, with each `_` written `-`. A target name
**outranks a path of the same spelling**: `maxon build app` builds the target even if a directory `app/`
exists. A word no target declares is a path, so `maxon build some/file.maxon` builds that file in any
project. Two or more positionals are always paths.

### The command line wins

| Command line | Described build | Result |
|--------------|-----------------|--------|
| `--output=<path>` | `output` | The command line's path |
| `--define=<name>=<value>` | `defines` | Both apply; for the same name, the command line's value wins |
| `--no-debug-info` | `debug_info` | Either one can turn the sidecar off; it is written only when both allow it |
| `--target=<cpu>-<os>` | *(no key)* | The built program uses the command line's target; the project file itself is always built for the host |

### Rebuilding a running compiler

A build whose output is the compiler running the command (such as the Maxon repository's own) can still
build. Once the compile succeeds, the compiler renames its running image to `maxon.previous`
(`maxon.previous.exe` on Windows) and writes the new binary into the empty slot. If an older
`maxon.previous` is itself still running, for example an editor's language server, it is renamed aside to
`maxon.retired-<stamp>` and deleted by a later rebuild. A **failed** build leaves the running compiler in
place.

## Ignoring directories

Place a `.maxonignore` file in a directory to exclude it, and everything beneath it, from builds,
`maxon test` discovery and `maxon fmt`. The file is a flag; its contents are never read.

The marker means "do not sweep me into somebody else's program", not "this may not be compiled":
**it excludes a directory the walk discovers, and never a path you named**. `maxon build
fixtures/sample.maxon` compiles that file and `maxon fmt fixtures/sample.maxon` formats it; so do
`maxon build fixtures` and `maxon fmt fixtures`, and so does naming a directory with a marker in an
ancestor above it. Only a marker *below* the path you named can exclude anything.

## The `.maxon/` directory

`.maxon/` holds a project's build products and is safe to delete or ignore in version control:

- `maxon test` stages its build in `<project>/.maxon/test/`, with its own `.maxonignore`.
- A project target's build that states no output is written there, as `.maxon/<name>` for the project
  file `<name>.maxproj`, and the compiler creates the output directory if it is missing.

A plain `maxon build <directory>` without `--output=` writes the executable into the directory, named
for it (`maxon build app` writes `app/app.exe` on Windows). Pass `--output=` or build a project target
to choose the location.

## The tree lock

Two commands writing the same output directories at once would corrupt each other's work, so some
commands take a lock on the **checkout** they write into: the nearest directory above the path that
holds a `stdlib/` directory (a Maxon source checkout or install). A project with no `stdlib/` above it
takes no lock.

The lock is the file `.maxon-tree.lock` at that root. It is taken by `spec-test`, `scale-test`, and by a
`build` of a directory without `--output=`. `run`, `test`, `fmt` and builds with `--output=` take none.

A command that finds the lock held prints what holds it and exits **2** without doing anything:

```text
error: this checkout is BUSY — another maxon command holds its tree lock, and two of them in one tree corrupt each other's output directories. Nothing was run.
```

A live holder refreshes the lock every 5 seconds. A lock untouched for 60 seconds is treated as
abandoned: the next command breaks it with a warning and proceeds.
