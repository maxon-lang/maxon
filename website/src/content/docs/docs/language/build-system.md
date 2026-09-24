---
title: Build System
description: build.maxon — how a Maxon project describes its own build, as a program rather than a config file.
sidebar:
  order: 14
---

A Maxon project is a directory of `.maxon` files marked by a **`<name>.maxproj`** project file at its
root. `maxon build <directory>` compiles every source file beneath a directory, and `maxon build` with no
path builds a target the project file describes.

```text
myproject/
├── myproject.maxproj    # the project file: where the project begins, and how it is built
├── myproject.maxtasks   # the tasks `maxon run` offers, if it has any
├── main.maxon           # entry point
├── lib.maxon
├── lib.maxtest          # tests: compiled only by maxon test
└── utils/
    └── math.maxon       # subdirectories are included
```

A build compiles the `.maxon` files. Each other kind of file has its own reader:

- **`.maxproj`** — the project file; `maxon build` runs its targets.
- **`.maxtasks`** — the task file; `maxon run` runs its tasks.
- **`.maxtest`** — test files; [`maxon test`](/docs/language/testing/) compiles them with the project's `.maxon` sources.

A directory walk also skips **any directory containing a `.maxonignore` file**, with everything beneath
it. A `.maxonignore` excludes a directory the walk *discovers*; naming a path explicitly on the command
line — a file or a directory — compiles it whatever marker sits above it or on it. A file named
`project.maxon` or `tasks.maxon` is ordinary source. See
[Project Structure](/docs/cli/project-structure/) for the full layout rules.

## A Project File Is a Program

A `.maxproj` file is ordinary Maxon with the whole standard library available. The compiler compiles it
for the host, runs the chosen target, and performs the build that target describes. A build can therefore
**compute** what it compiles — list a directory, choose sources by host, derive a version from git — as
well as spell it out.

**Its targets are its exported (or `public`) functions of no parameters returning `ExitCode`.** A
non-zero return or a crash fails the build before anything is compiled.

```maxon
// hello.maxproj
export function build() returns ExitCode
	Build.build("src")
	return 0
end 'build'
```

A build that states no output is written to `.maxon/<name>`, `<name>` being the project file's own name —
`.maxon/hello` here. A stated output omits the extension: the compiler adds the target's (`.exe` on
Windows, `.wasm` for `wasm32-wasi`, none on Linux and macOS). Relative paths resolve against the project
file's directory.

`maxon build` with no path builds the project's only target. **A project with several targets** names
one on the command line, each `_` of the function's name written `-`:

```maxon
// tools.maxproj
export function app() returns ExitCode
	Build.build("app", output: "out/app", version: "1.2.3")
	return 0
end 'app'

export function gen_tool() returns ExitCode
	Build.build("tool", output: "out/tool", debugInfo: false)
	return 0
end 'gen_tool'
```

`maxon build` with no argument then lists `app` and `gen-tool` and compiles nothing; `maxon build app`
and `maxon build gen-tool` build one each. Every target is an entry point of the project file, so an
exported target is exempt from **E3092**.

**One project per tree.** A `.maxproj` file inside another project's directory tree is **E2074**, reported
from either project; a directory holds one `.maxproj` file at most.

## Describing the Build

`Build` (in the standard library) writes the build description the compiler reads back. A target
describes **one** build; describing a second in the same run is an error.

| Call | Meaning |
|------|---------|
| `Build.build(source, output: "", debugInfo: true, version: "", defines:)` | compile one file or directory to one output |
| `Build.buildWithConfig(config)` | build one `BuildConfig`, whose `sources` may list several files and directories compiled as one program, in order |
| `Build.delegate(directory, target: "")` | hand the whole description to a target of another directory's `.maxproj` file, run there |

- `debugInfo` controls the debug-information sidecar written beside the executable.
- `version` stamps a dotted version into the executable's metadata.
- `defines` is a `StringArray` of `"name=value"` entries, each replacing the written default of a top-level
  `String` constant — the same as `maxon build --define`. This is how a project file passes a value it
  computed, such as a version derived from git, into the program.
- `target` names one of the delegated project's targets the way `maxon build` does; empty means its only
  one.

**Several sources in one program** use a `BuildConfig`:

```maxon
export function build() returns ExitCode
	var sources = StringArray.create()
	sources.push("src")
	sources.push("vendor/thirdparty")

	Build.buildWithConfig(BuildConfig.create(sources, output: "out/myprogram"))
	return 0
end 'build'
```

Sources are compiled in exactly the order listed. An empty `sources` list is refused
(`myprogram.maxproj named no sources to compile`), so a build always names what it compiles.

## The Command Line Wins

Flags typed on the command line outrank the described build: `--output=` replaces the output path,
`--target` chooses the target (the project file itself always runs on the host), a `--define` is applied
after the described build's defines, and debug information is written only if both the described build
and the command line allow it.

The [CLI reference](/docs/cli/) documents `maxon build` and its flags.

## Tasks

A directory may also hold a **`<name>.maxtasks`** file, and `maxon run <task>` runs one of its exported
no-parameter `ExitCode` functions with the caller's own streams and exit code. The task is named the way
a target is, each `_` written `-`, and `maxon run` alone lists them. It is the same language and the
same standard library as a project file, and the two files divide one job in two:

```maxon
// workspace.maxtasks
export function build() returns ExitCode
	Build.delegate("compiler")
	return 0
end 'build'

export function check_format() returns ExitCode
	return 0
end 'check_format'
```

**A `.maxtasks` file marks nothing**, so it can sit in any directory: what says where a project begins is
a `.maxproj` file. A task may describe a build, as `build` does above, and the compiler performs it once
the task exits 0; with no stated output it is written to `.maxon/<name>`, `<name>` being the task file's
own. Every task is an entry point, exempt from **E3092** like a target.
