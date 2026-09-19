---
title: Build System
description: build.maxon — how a Maxon project describes its own build, as a program rather than a config file.
sidebar:
  order: 14
---

A Maxon project is a directory of `.maxon` files — there is nothing to declare. `maxon build <directory>`
compiles every source file beneath it. A directory that wants to say *how* it is built puts a
**`build.maxon`** beside its sources, and `maxon build` with no path runs it.

```text
myproject/
├── build.maxon          # the build manifest, if the project needs one
├── main.maxon           # entry point
├── lib.maxon
├── lib.test.maxon       # tests: compiled only by maxon test
└── utils/
    └── math.maxon       # subdirectories are included
```

A directory walk skips three things:

- **`build.maxon`** — it describes the build and is never part of the program being built.
- **`*.test.maxon`** — test files; [`maxon test`](/docs/language/testing/) compiles them.
- **any directory containing a `.maxonignore` file**, with everything beneath it.

A `.maxonignore` excludes a directory the walk *discovers*. Naming a path explicitly on the command
line — a file or a directory — compiles it regardless of a marker above it or on it. See
[Project Structure](/docs/cli/project-structure/) for the full layout rules.

## A Manifest Is a Program

`build.maxon` is ordinary Maxon with the whole standard library available. The compiler does not parse it
as configuration: it compiles it for the host, runs it, and performs the build it describes. A build can
therefore **compute** what it compiles — list a directory, choose sources by host, derive a version from
git — instead of only spelling it out.

Its entry point is a function named **`build`**, not `main`. It returns `ExitCode`; a non-zero return or a
crash fails the build before anything is compiled.

```maxon
function build() returns ExitCode
	Build.build("src", output: "out/hello")
	return 0
end 'build'
```

The output path omits the extension: the compiler adds the target's (`.exe` on Windows, `.wasm` for
`wasm32-wasi`, none on Linux and macOS). Relative paths resolve against the manifest's directory.

## Describing the Build

`Build` (in the standard library) writes the build description the compiler reads back:

| Call | Meaning |
|------|---------|
| `Build.build(source, output:, debugInfo: true, version: "", defines:)` | compile one file or directory to one output |
| `Build.target(name, source:, output:, debugInfo: true, version: "", defines:)` | describe one named target, returning a `BuildConfig` |
| `Build.buildTargets(targets)` | declare several named targets (a `BuildConfigArray`) |
| `Build.buildWithConfig(config)` | build one `BuildConfig`, whose `sources` may list several files and directories compiled as one program, in order |

- `debugInfo` controls the debug-information sidecar written beside the executable.
- `version` stamps a dotted version into the executable's metadata.
- `defines` is a `StringArray` of `"name=value"` entries, each replacing the written default of a top-level
  `String` constant — the same as `maxon build --define`. This is how a manifest passes a value it computed,
  such as a version derived from git, into the program.

**Several targets** are listed rather than guessed at:

```maxon
function build() returns ExitCode
	var targets = BuildConfigArray.create()
	targets.push(Build.target("app", source: "app", output: "out/app", version: "1.2.3"))
	targets.push(Build.target("tool", source: "tool", output: "out/tool", debugInfo: false))
	Build.buildTargets(targets)
	return 0
end 'build'
```

`maxon build` with no argument then prints the target names and compiles nothing; `maxon build app`
builds one.

**Several sources in one program** use a `BuildConfig`:

```maxon
function build() returns ExitCode
	var sources = StringArray.create()
	sources.push("src")
	sources.push("vendor/thirdparty")

	let config = BuildConfig.create("myprogram", output: "out/myprogram", sources: sources, debug_info: true)
	Build.buildWithConfig(config)
	return 0
end 'build'
```

Sources are compiled in exactly the order listed. An empty `sources` list is refused (`build.maxon named
no sources to compile`) rather than read as "everything here".

## The Command Line Wins

Flags typed on the command line outrank the manifest: `-o` replaces the output path, `--target` chooses
the target (the manifest itself always runs on the host), a `--define` is applied after the manifest's
defines, and debug information is written only if both the manifest and the command line allow it.

The [CLI reference](/docs/cli/) documents `maxon build` and its flags.
