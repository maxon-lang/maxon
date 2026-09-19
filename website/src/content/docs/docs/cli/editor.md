---
title: Editor Support
description: The VS Code extension, the maxon lsp-server language server, and using it from other editors.
sidebar:
  order: 4
---

Maxon's editor support is the compiler itself: `maxon lsp-server` is a Language Server Protocol server,
and the VS Code extension runs it.

## VS Code

Install **Maxon** from the
[Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=maxon-lang.maxon-lsp-client)
or [Open VSX](https://open-vsx.org/extension/maxon-lang/maxon-lsp-client) (extension id
`maxon-lang.maxon-lsp-client`). It activates in a workspace containing `.maxon` files and provides syntax
highlighting, diagnostics, hover, completion, go-to-definition, rename, formatting, the Compiler
Explorer and a Test Explorer.

**Finding the compiler.** The extension runs `maxon lsp-server` from the first compiler it finds:

1. the `maxon.serverPath` setting, when it points at an executable;
2. `maxon` on `PATH`;
3. `$MAXON_INSTALL/bin/maxon` when `MAXON_INSTALL` is set, otherwise `~/.maxon/bin/maxon` (the install
   script's default location);
4. `maxon-bin/.maxon/maxon` in the first workspace folder, which is where a Maxon source checkout builds
   its compiler.

If none is found, the extension offers to **Install** Maxon with the install script or to **Locate…** a
compiler, which it saves to `maxon.serverPath`. When the compiler binary changes on disk (for example
after an upgrade or a rebuild), the extension restarts the language server.

**Settings:**

| Setting | Default | Description |
|---------|---------|-------------|
| `maxon.serverPath` | `""` | Absolute path to the `maxon` compiler. Empty means search as above. |
| `maxon.formatting.insertSpaces` | `false` | Indent formatted code with spaces instead of tabs |
| `maxon.formatting.tabSize` | `2` | Spaces per indent level, when `insertSpaces` is `true` |

The formatting settings replace the editor's own tab settings for Maxon files. The extension also turns
on format-on-save and semantic highlighting for Maxon files by default.

**Commands:**

| Command | What it does |
|---------|--------------|
| **Maxon: Restart Language Server** | Restart `maxon lsp-server` |
| **Maxon: Open Compiler Explorer** | Focus the Compiler Explorer view |

**Compiler Explorer.** A view in the Maxon activity-bar container with a **Source** pane and a
**Target IR** pane. Half a second after you stop typing, it compiles the source for the host and shows
the lowered Target IR, the same text `maxon build --emit-ir` writes, or the compile errors as
`Line <line>:<column>: <message>`. Nothing is written to disk. The source is treated as a whole program,
so it needs a `main`.

**Test Explorer.** The **Maxon Tests** controller lists the `test` declarations in the workspace's
`*.test.maxon` files, one node per file, and runs them with the compiler the extension found, one
`maxon test <project> --json` per project the selection touches. A test file's project is the highest
directory above it, never above the workspace folder, whose every level holds a `.maxon` source. When the
workspace folder is the Maxon source checkout, a second controller, **Maxon Spec Suite**, lists the spec
tests in `specs/*.md` and runs them with the checkout's own compiler
(`maxon-bin/.maxon/maxon spec-test --filter=…`).

## `maxon lsp-server`

```bash
maxon lsp-server
```

Speaks the Language Server Protocol over stdin and stdout: JSON-RPC messages with `Content-Length`
headers. It takes no arguments and is normally started by an editor, not by hand.

**Lifecycle.** `initialize` returns the server's capabilities; `initialized` is accepted. `shutdown`
answers `null`, and `exit` ends the process with code **0** after a `shutdown` and **1** otherwise (as
it does when stdin closes or a message cannot be framed).

**Documents.** Text synchronization is **full**: every `textDocument/didChange` carries the whole
document. The server handles `didOpen`, `didChange` and `didClose`. Each buffer is analysed from its
in-memory text, for the host target.

**Diagnostics are per-buffer; definition and completion read the project.** A buffer is *checked* on its
own together with the standard library, so a diagnostic never depends on what a sibling file declares.
`textDocument/definition` and `textDocument/completion` do read the other files of the project: they use
a separate index built by lexing every source under the document's project root and folding its
declarations — never a full compile — so a name declared in another file resolves, and a type declared
there offers its members.

**The project root is derived from the document, not from the workspace.** `rootUri` and
`workspaceFolders` are not read. A file inside the compiler's own `stdlib/` or `runtime/` gets those two
tiers and nothing else; any other file gets the nearest ancestor directory holding a `build.maxon`, or
its own directory when no ancestor has one. The manifest is used only as a marker of where a project
begins — it is never read and never run. Projects are cached for the life of the server process, and a
source is re-read when its size or modification time changes on disk.

**Diagnostics** are published with `textDocument/publishDiagnostics` after every `didOpen` and
`didChange`, and cleared on `didClose`. Each has the error code (for example `E3005`) as `code`,
`source: "maxon"` and severity Error. "No `main` function" (E3001) is not reported, because a single
buffer is not a whole program.

**Requests served:**

| Method | Result |
|--------|--------|
| `textDocument/hover` | Markdown: the declaration as written in a `maxon` code block, its `///` doc comment, and for a variable or parameter what it is and its type. Keywords and math intrinsics are described too. |
| `textDocument/definition` | The declaration of the name under the cursor. A declaration in the same document is answered from it; otherwise the project's index says which file declares the name, and the answer points into that file. A name the compiler would refuse as ambiguous, or one no visible declaration carries, answers `null`. |
| `textDocument/completion` | Members after `.` (the trigger character): fields, methods, static functions and enum cases, for a type name or a local whose type is evident. There is no completion of bare identifiers. |
| `textDocument/formatting` | One edit replacing the whole document with `maxon fmt`'s layout, or `null` when it is already formatted. `insertSpaces: false` indents with tabs; `true` indents with `tabSize` spaces. |
| `textDocument/documentSymbol` | The top-level declarations: functions, types, enums, unions, interfaces and extensions |
| `textDocument/foldingRange` | Every block spanning more than one line |
| `textDocument/linkedEditingRange` | A block's opening name and its `end '…'` label, edited together |
| `textDocument/rename` | Renames a declaration's name and its matching `end '…'` label. References elsewhere are not renamed. |
| `textDocument/codeAction` | A quick fix, "Remove unused variable", for a local `let`/`var` whose name appears nowhere else |
| `textDocument/semanticTokens/full` | Semantic tokens with the legend `keyword`, `type`, `struct`, `enum`, `interface`, `function`, `method`, `variable`, `parameter`, `modifier`, `enumMember`, `property`, `string`, `number`, `comment`, `operator` and no modifiers |

Any other request is answered with JSON-RPC error `-32601` (method not found).

**`maxon/generateIR`** is a Maxon-specific request, not advertised in the capabilities, that compiles a
source text in memory for the host and returns its Target IR. It is what the Compiler Explorer uses.

```json
{ "source": "function main() returns ExitCode\n\treturn 0\nend 'main'\n", "filename": "explorer.maxon" }
```

Both params are required (otherwise `-32602`). The result:

```json
{ "ir": "…", "errors": [ { "message": "…", "line": 1, "column": 1 } ] }
```

`ir` is the text `maxon build --emit-ir` writes, and is empty when the compile fails. `errors` lists
every diagnostic with a **1-based** `line` and `column`.

**`maxon/listProjects`** is a Maxon-specific request, not advertised in the capabilities, that lists the
projects the server holds. It is what the VS Code status bar shows. It takes no params. Each open document
is a project of its own, because each is analysed on its own:

```json
{ "projects": [ { "rootPath": "/home/me/app/main.maxon", "isSingleFile": true, "fileCount": 1 } ] }
```

`rootPath` is a filesystem path, not a URI, and the projects are listed in path order.

## Other editors

Any editor with an LSP client can use the server. Configure it to start the command `maxon` with the
argument `lsp-server` over stdio for files with the `.maxon` extension (language id `maxon`), and to
send full-document synchronization.
