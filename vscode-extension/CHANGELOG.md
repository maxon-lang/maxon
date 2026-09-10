# Changelog

## 0.2.0

### Added

- **Install** in the compiler-not-found prompt runs Maxon's one-line installer for your account, on
  every platform, and starts the language server when it finishes. Its output is in the Maxon
  Language Server output channel.
- The extension finds a compiler installed by the install script in `~/.maxon/bin` (or
  `$MAXON_INSTALL/bin`) even when VS Code was started without your shell's `PATH`.

### Fixed

- Diagnostics, hover and completion work with an installed compiler. The language server runs from a
  copy of the compiler in the extension's storage, and that copy now finds the standard library.

## 0.1.0

The first published release, alongside Maxon 0.1.0.

### Language support

- Diagnostics, hover, go-to-definition, completion, rename, document symbols, semantic tokens and
  formatting, from the language server the compiler itself provides (`maxon lsp-server`).
- Syntax highlighting, an icon theme, and a Spec Test explorer.

### Finding the compiler

The extension needs a `maxon` compiler; it looks in this order:

1. The `maxon.serverPath` setting, if you set one.
2. `maxon` on your `PATH` — where an installed Maxon puts itself.
3. This workspace's own build at `maxon-bin/.maxon/`, so a contributor with a built tree needs no
   configuration.

If none of those finds one, the extension offers **Locate…** to pick a compiler you already have,
rather than reporting a dead end.
