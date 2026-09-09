---
title: Changelog
description: Every released version of the Maxon compiler and standard library, and what changed in it.
---

Every released version, newest first.

Downloads for each release are on the
[GitHub releases page](https://github.com/maxon-lang/maxon/releases), and the install instructions are in
[Installation](/docs/getting-started/installation/).

## 0.1.0 — 2026-09-08

The first release. One compiler, written in Maxon, that builds itself.

### Added

- **Binaries for four targets** — `x64-windows`, `x64-linux`, `arm64-macos` and `arm64-linux`. Each
  is built and has its whole spec suite run on hardware of its own architecture.
- **A Windows installer.** `winget install MaxonLang.Maxon`, or the `.msi` directly: it installs to
  `C:\Program Files\Maxon` and adds it to the system PATH.
- **Homebrew on macOS** — `brew install maxon-lang/tap/maxon`, which puts `maxon` on the PATH and
  clears the quarantine attribute a downloaded archive carries.
- **A VS Code extension**, on both the Marketplace and Open VSX, with a language server providing
  diagnostics, hover, go-to-definition, completion, rename, symbols and formatting. It finds a
  compiler on your PATH, and offers to install one when it cannot.
- **`maxon build`, `maxon fmt`, `maxon test`, `maxon spec-test`, `maxon lsp-server`** and a build
  manifest that is a program rather than a config file — see the CLI reference.

### Known limitations

- The installers are not code-signed, so Windows SmartScreen warns on first run: **More info**, then
  **Run anyway**.
- ⚠ **`maxon` and `stdlib/` must stay together.** The compiler finds its standard library by walking
  up from its own executable, so moving the binary out of the extracted directory on its own leaves
  it without one.
