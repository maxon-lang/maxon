# Changelog

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

If none of those finds one, the extension offers to install it rather than reporting a dead end: on
Windows it runs `winget install --id MaxonLang.Maxon -e` in a visible terminal, and on every platform
**Locate…** lets you pick a compiler you already have.
