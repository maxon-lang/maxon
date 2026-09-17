# Changelog

Versions are `YEAR.MONTH.PATCH`, the month a release shipped, and are independent of the compiler's.

## 2026.9.4

### Added

- The Test Explorer lists the `test` declarations in your project's `*.test.maxon` files and runs them
  with `maxon test`, using the compiler the language server uses. A failed assertion is shown at its own
  line, and a test that crashed, timed out, leaked or did not run says so.

### Changed

- The spec-suite explorer appears only in the Maxon compiler checkout, as its own **Maxon Spec Suite**
  controller.

### Fixed

- The status bar's project list says why it could not be read, instead of reporting that no projects are
  loaded.

### Removed

- The `label.level0` to `label.level5` semantic token colours. The language server produces no such
  tokens, so they coloured nothing; labels keep the grammar's colour.

## 2026.9.3

### Fixed

- The Compiler Explorer shows the Target IR of the program you type, or its errors with their line and
  column. Every request used to be refused by the language server, so the panel stayed empty.
- Running spec tests from the Test Explorer reports each test's result. The run passed an option the
  compiler refuses and read a report format it no longer prints, so no test was ever marked passed or
  failed.

### Removed

- The Compiler Explorer's Assembly view and its Optimized toggle, and the `Maxon: Generate Assembly
  (internal)` command. The compiler has no assembly listing and no optimization levels to choose
  between; the panel is a single Target IR view.

## 2026.9.2

### Changed

- Nothing in the extension. The version moves because 2026.9.1 was published from a release that was
  cut again, and a version already on the Marketplace cannot be published twice.

## 2026.9.1

### Changed

- The requirements section links to the installation page in Maxon's documentation. Nothing else
  in the extension changed.

## 2026.9.0

### Added

- **Install** in the compiler-not-found prompt runs Maxon's one-line installer for your account, on
  every platform, and starts the language server when it finishes. Its output is in the Maxon
  Language Server output channel.
- The extension finds a compiler installed by the install script in `~/.maxon/bin` (or
  `$MAXON_INSTALL/bin`) even when VS Code was started without your shell's `PATH`.

### Changed

- The extension installs in editors built on VS Code 1.107 or later, Antigravity included.

### Removed

- The Maxon file icon theme. Maxon files carry the language's own icon in any theme.

### Fixed

- Diagnostics, hover and completion work with an installed compiler. The language server is the
  compiler itself (`maxon lsp-server`), so it reads the standard library the compiler does, and a
  rebuilt compiler restarts it. The extension no longer keeps a `maxon-lsp` copy in its storage, and
  removes one it finds there.

## 0.1.0

The first published release, alongside Maxon 0.1.0.

### Language support

- Diagnostics, hover, go-to-definition, completion, rename, document symbols, semantic tokens and
  formatting, from the language server the compiler itself provides (`maxon lsp-server`).
- Syntax highlighting and a Spec Test explorer.

### Finding the compiler

The extension needs a `maxon` compiler; it looks in this order:

1. The `maxon.serverPath` setting, if you set one.
2. `maxon` on your `PATH` — where an installed Maxon puts itself.
3. This workspace's own build at `maxon-bin/.maxon/`, so a contributor with a built tree needs no
   configuration.

If none of those finds one, the extension offers **Locate…** to pick a compiler you already have,
rather than reporting a dead end.
