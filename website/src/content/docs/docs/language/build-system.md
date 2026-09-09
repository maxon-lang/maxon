---
title: Build System
description: build.maxon — how a Maxon project describes its own build, as a program rather than a config file.
sidebar:
  order: 14
---

`maxon build` with no path looks for **`build.maxon`** in the current directory, compiles it, runs it,
and performs the build it describes.

```bash
maxon build
```

## A manifest is a program

`build.maxon` is ordinary Maxon with the whole standard library available. The compiler does not parse
it — it *runs* it, and reads the build off what it prints.

That is the point: a build can **compute** what it compiles. Read a directory, choose sources by host,
stamp a version out of a file — anything you can write, rather than only what a configuration format
anticipated.

## Its entry point is `build`, not `main`

A manifest holds tasks a project can be asked to perform. `build` is the one this command asks for,
and the name it asks by. It returns `ExitCode`; a non-zero return, or a crash, fails the build and
nothing is compiled.

```maxon
export function build() returns ExitCode
	Build.buildOne("src", output: "out/myprogram")
	return 0
end 'build'
```

## Project structure

A project is a directory the compiler walks. Every `.maxon` file beneath it is part of the program,
in the order given, except:

```text
myproject/
├── build.maxon          # the manifest — describes the build, never part of it
├── main.maxon           # entry point
├── lib.maxon
└── utils/
    └── math.maxon       # subdirectories are included
```

- **`build.maxon`** is skipped by the walk. It describes the build; it is not in it. Naming it
  explicitly still compiles it, like any other file.
- **`*.test.maxon`** is skipped too — a test file is a source *category*, and `maxon test` names those
  explicitly.
- **Anything under a `.maxonignore`** is skipped, along with every directory beneath it.

## Describing the build

`Build` writes the description and the compiler reads it back, so both ends share one definition of
what a build is.

| Call | What it says |
|---|---|
| `Build.buildOne(source, output:)` | Compile one file or directory to one output. The common shape. |
| `Build.buildWithConfig(config)` | Several sources, compiled as **one program**, in the order given. |

The output path omits the extension — the compiler appends `.exe` on Windows and nothing elsewhere.

For several sources:

```maxon
export function build() returns ExitCode
	var sources = SourceList.create()
	sources.push("src")
	sources.push("vendor/thirdparty")

	let config = BuildConfig.create("myprogram", output: "out/myprogram", sources: sources, optimize: false, debug_info: true)
	Build.buildWithConfig(config)
	return 0
end 'build'
```

Order is load-bearing: sources are registered in exactly the order named.

:::caution[Name what to compile]
An empty `sources` is **refused**, not read as "this directory". Accepting it would compile every file
beneath the manifest — including every test — under a command that named nothing at all.
:::

## The command line wins

`-o` and `--target` typed on the command line outrank the manifest, for the reason every flag outranks
a file: the person typing the command is answering a narrower question than the file was.

The manifest itself is always built for the host, whatever `--target` says — it is a program your
machine has to run in a moment.
