---
title: Project Structure
description: Which files a build includes, the build manifest, ignored directories, the .maxon/ directory, and the tree lock.
sidebar:
  order: 2
---

A Maxon project is a directory of `.maxon` files. There is nothing you must write besides the sources:
the directory is the project. This page covers what the command-line tools read from and write into a
project. The language side of manifests is described in [Build System](/docs/language/build-system/),
and how files map to namespaces in [Namespaces](/docs/language/namespaces/).

```text
myproject/
├── project.maxon        # optional: the build manifest
├── tasks.maxon          # optional: the tasks `maxon run` offers
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

## Which files a build includes

`maxon build <directory>` compiles every `.maxon` file beneath the directory as one program, except:

1. **`project.maxon` and `tasks.maxon` AT THE DIRECTORY YOU NAMED.** Each is a program of its own, and
   the exclusion is exactly where the driver looks for one — the walk root — so a `Project.maxon`
   deeper in the tree is ordinary source and is compiled. The match at the root ignores case, because
   on a case-insensitive filesystem `PROJECT.maxon` is the file `maxon build` would run. Naming either
   path explicitly still compiles it.
2. **`*.test.maxon`** files. Test sources are a separate category that only `maxon test` compiles. The
   match ignores case, so `Suite.TEST.maxon` is a test file too.
3. **Anything under a `.maxonignore`** (below).

The standard library is part of every compilation; the compiler finds it by walking up from its own
executable, so nothing in the project refers to it. See the [Standard Library](/docs/stdlib/).

A file of the compiler's own `stdlib/` or `runtime/` that a build names — as its path, or in a manifest's
`sources` beside a program — is read once, not a second time as part of the library. A `runtime/` file is
compiled as runtime-tier source wherever it is named from, with the tier's rules
([The runtime tier](/docs/stdlib/#the-runtime-tier)); built on its own it has no `main`, so the
build stops at [E3001](/docs/cli/error-codes/#e3001--nomainfunction).

## The build manifest

`maxon build` with no path looks for **`project.maxon`** in the current directory, compiles it, runs it,
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
[run cache](/docs/cli/#the-run-cache), never inside the project.

That compile is **silent**, and says only which of the two things happened to it:

```text
Compiled the build runner (project.maxon)     # nothing in the cache matched, so it was compiled
Used the cached build runner (project.maxon)  # the cache had it, and nothing was compiled
```

The line goes to stdout, ahead of the build's own report. The runner is scaffolding, so a reader gets
one line about it rather than a second build's worth of output mixed into the answer they asked for —
but **silenced is not silent**: a `project.maxon` that does not compile still prints its diagnostics, and
`--log=` anywhere on the command line leaves the runner's compile as loud as any other.

### Describing a build

The manifest describes its builds by calling `stdlib/Build.maxon`, which writes them as JSON to the
file the driver names in **`MAXON_BUILD_DESCRIPTION`**. The compiler reads that file back.

**The description has a channel of its own so that stdout stays the program's.** A manifest or a task
can print whatever it likes as it works, and it streams to the caller live. With the variable unset —
a manifest run by hand with `maxon execute project.maxon` — the description goes to stdout instead,
which is the only way to inspect one directly.

| Call | Meaning |
|---|---|
| `Build.build(source, output:, debugInfo:, version:, defines:)` | Build one file or directory to one output, and write it. The common case. |
| `Build.target(name, source:, output:, debugInfo:, version:, defines:)` | Return one **named** target, for a manifest that describes several. Writes nothing. |
| `Build.buildTargets(targets)` | Write several named targets (a `BuildConfigArray`). |
| `Build.buildWithConfig(config)` | Write one `BuildConfig`, which can list several sources, compiled as one program in order. |
| `Build.delegate(name, directory:, target:)` | Hand the whole description to another directory's `project.maxon`. |
| `Build.delegateTarget(name, directory:, target:)` | Return one delegated target, for `Build.buildTargets`. Writes nothing. |

`debugInfo` defaults to `true`, `version` to `""` and `defines` to an empty list. The keys the driver
reads from the JSON are:

| Key | Type | Meaning |
|-----|------|---------|
| `name` | string | What `maxon build <name>` selects. `Build.build` sets it to the source path. |
| `output` | string, required unless `directory` | Where the executable goes, without the extension. The compiler adds `.exe` for Windows, `.wasm` for `wasm32-wasi`, and nothing for Linux and macOS. Relative to the current directory. |
| `sources` | list of strings, required unless `directory` | The files and directories to compile, in order. An empty list is refused. |
| `directory` | string | A directory whose own `project.maxon` describes this build. Stating it alongside `sources` is refused. |
| `target` | string | With `directory`, which of the delegated manifest's targets to build; empty means its sole one. |
| `debug_info` | `true` or `false` | Whether to write the `.mxdbg` sidecar (default `true`). |
| `version` | string | A dotted version stamped into the binary: a `VS_VERSIONINFO` resource on Windows and `LC_SOURCE_VERSION` on macOS. Linux and `wasm32-wasi` binaries carry no product version. Without it, the binary reports `0.0.0.0`, and a missing component is 0. A component that is not a number is refused on every target, and one the target's field cannot hold is refused too: each Windows component holds 0 to 65535 (four at most); on macOS the first holds 0 to 16777215 and the next four 0 to 1023. |
| `defines` | list of `name=value` strings | The same as [`--define=`](/docs/cli/#defines) on the command line. |

A field that is present but malformed is **refused** rather than guessed at, naming the key, for
example ``error: project.maxon's `sources` is not a list of strings``. A description that is not JSON
at all is refused with what the manifest wrote, and a manifest that exits 0 having written none is
refused as describing no build.

### Delegating to another directory

A manifest can hand the whole description to another directory's `project.maxon`:

```maxon
export function build() returns ExitCode
	Build.delegate("maxon-bin", directory: "maxon-bin")
	return 0
end 'build'
```

The compiler runs that directory's manifest **with the directory as its working directory**, and
resolves the relative `sources` and `output` it states against it — so the delegated project builds
the same thing whether it is reached from above or built from inside. The command line's `--output=`,
`--target`, `--define` and `--no-debug-info` are applied afterwards, exactly as they are to a build
described in place. Delegation more than eight deep is refused as a cycle.

### The task file

`tasks.maxon` beside `project.maxon` holds the tasks [`maxon run`](/docs/cli/#maxon-run) offers. The two files
are separate on purpose:

- **`project.maxon` says what the project IS.** Its presence marks where a project begins — the
  editor's project root ([Editor Support](/docs/cli/editor/)) — and its `build` task describes the
  build.
- **`tasks.maxon` says what a person DOES here.** It marks nothing, so a directory of scripts is not
  turned into a project by holding one.

Neither is compiled into the program (see [Which files a build includes](#which-files-a-build-includes)).

Every task `tasks.maxon` declares counts as an entry point, whichever one `maxon run` was asked for: the
runner reaches the others by name, so an `export function` task is never reported as an unused export
([E3092](/docs/cli/error-codes/#e3092--semanticunusedexportedsymbol)).

### Named targets

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

### The command line wins

| Command line | Manifest | Result |
|--------------|----------|--------|
| `--output=<path>` | `output` | The command line's path |
| `--define=<name>=<value>` | `defines` | Both apply; for the same name, the command line's value wins |
| `--no-debug-info` | `debug_info` | Either one can turn the sidecar off; neither can force it on |
| `--target=<cpu>-<os>` | *(no key)* | The built program uses the command line's target; the manifest program itself is always built for the host |

### Rebuilding a running compiler

A manifest whose output is the compiler running the command (such as the Maxon repository's own) can
still build. Once the compile succeeds, the compiler renames its running image to `maxon.previous`
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
- Manifests conventionally write their outputs there (`output: ".maxon/myapp"`), and the compiler
  creates the output directory if it is missing.

A plain `maxon build <directory>` without `--output=` does not use `.maxon/`: it writes the executable
into the directory, named for it (`maxon build app` writes `app/app.exe` on Windows). Pass `--output=`
or use a manifest to choose the location.

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
