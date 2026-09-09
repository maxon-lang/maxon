# Changelog

What changed in each release of the Maxon compiler and standard library, newest first.

Written by hand, for people installing a compiler rather than changing one. `scripts/changelog.sh
--commits-since` lists what has landed since the last release, to consult while writing the next
entry; `docs/RELEASING.md` has the order a release is cut in.

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
