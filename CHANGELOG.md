# Changelog

What changed in each release of the Maxon compiler and standard library, newest first.

## 0.1.1 — 2026-09-10

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
- On Linux and macOS, starting a program that cannot be found raises
  `SubprocessError.executableNotFound` instead of `spawnFailed`.

### Fixed

- Non-ASCII text displays correctly in a Windows console.
- A deadlocked program on Linux reports the deadlock instead of occasionally hanging.
- A default parameter value is filled in a global variable's initializer, not only inside a function.
- On Linux, a subprocess started by program name finds the program on `PATH`, and a file of that
  name in the working directory is never run in its place.

## 0.1.0 — 2026-09-08

The first release. One compiler, written in Maxon, that builds itself.

### Added

- Binaries for `x64-windows`, `x64-linux`, `arm64-macos` and `arm64-linux`, each built and tested
  on its own architecture.
- A Homebrew formula for macOS.
- A VS Code extension, on the Marketplace and Open VSX, with a language server providing
  diagnostics, hover, go-to-definition, completion, rename, symbols and formatting.
- `maxon build`, `fmt`, `test`, `spec-test` and `lsp-server`, and a build manifest written as a
  Maxon program rather than a configuration file.
