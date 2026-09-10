---
title: Maxon 0.1.1
description: Release notes for Maxon 0.1.1 — what changed in the compiler and standard library.
date: 2026-09-09
authors: maxon
tags:
  - release
excerpt: Maxon 0.1.1 is out. Here's what changed.
---

**Maxon 0.1.1 is released.** Archives for every supported target are on the
[GitHub releases page](https://github.com/maxon-lang/maxon/releases/tag/v0.1.1), alongside a Windows installer.

## What changed

### Added

- `maxon run`, which compiles a program and runs it in one step. A `.maxon` file may begin with a
  `#!` line and be run directly as a script.
- `maxon help`, and `maxon help <command>` for a single command. Running `maxon` with no arguments
  now lists the commands.
- `--define`, which sets a top-level `String` constant at build time.
- Build manifests can declare several named targets, and state the version of what they build.
- Compiled executables carry version metadata: a version resource on Windows, a source version on
  macOS, and a producer string on Linux.

### Changed

- `maxon version` replaces the `--version` and `-V` flags, and reports the commit and date the
  compiler was built from.
- `maxon help` replaces the `--help` and `-h` flags.
- `Build.buildOne` in a build manifest is renamed `Build.build`.
- `maxon fmt` separates groups of declarations with one blank line at every nesting depth, not only
  at the top level.
- The Windows installer is code-signed.

### Fixed

- Non-ASCII text displays correctly in a Windows console.
- A deadlocked program on Linux reports the deadlock instead of occasionally hanging.
- A default parameter value is filled in a global variable's initializer, not only inside a function.

### Removed

- The Windows installer no longer offers to install the VS Code extension.

## Download

```
winget install MaxonLang.Maxon          # Windows
brew install maxon-lang/tap/maxon       # macOS
```

Elsewhere, take the archive for your platform from the
[releases page](https://github.com/maxon-lang/maxon/releases/tag/v0.1.1) and follow the `INSTALL.md` inside it.

⚠ Each archive holds the `maxon` compiler and `stdlib/` **as siblings**, and that layout is the
contract: the compiler finds its standard library by walking up from its own executable, so moving the
binary out on its own leaves it without one.
