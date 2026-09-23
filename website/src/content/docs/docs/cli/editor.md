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

**Every feature reads the project, where the document has one, and diagnostics are still checked per
buffer.** Hover, definition and completion resolve names through a separate index built by lexing every
source under the document's project root and folding its declarations — never a full compile — so a name
declared in another file resolves, and a type declared there offers its members. Diagnostics use that
same index, but only to stop the server claiming a name is undeclared when a sibling file declares it.
The *checking* is still done on the buffer together with the standard library alone, so a diagnostic that
needs the whole program merged across files is out of reach: an error only the larger program could
raise — two sibling files contesting one name, for example — is not reported, and the build remains the
authority.

**A document's uri may carry any scheme, and what the server can do with a document turns on the path
that uri spells rather than on the scheme.** No scheme is refused, so an editor that forwards whatever
uri a buffer carries is served. Document symbols, folding ranges, formatting, linked editing, rename,
code actions and semantic tokens read the buffer alone and answer for every open document. The project
half needs a path with a directory above it: a uri without one names no place in any project, so the
document contributes no `maxon/listProjects` entry. An unsaved scratch buffer (`untitled:Untitled-1`) is
checked and resolved against `stdlib/` and `runtime/` alone — the treatment a file inside those tiers
gets. A uri that spells no filesystem path at all (`git://host/repo/file.maxon`) leaves hover, definition
and completion with nothing beyond the buffer, and no diagnostics are published for it.

**While a buffer is unsaved, some diagnostics are withheld.** As long as the buffer matches the file on
disk, the server reports what a build of that file reports. Once it has been edited, diagnostics about
the buffer's own text — syntax, tokens, literals — are still published immediately, while diagnostics
that turn on what a declaration says are held back until the buffer matches disk again. That is what
stops an editor inventing errors about names it cannot see, and it applies only when the buffer does use
a name that only the project declares. This project view needs a workspace folder that contains the file:
a file the client named no root over, and a file inside `stdlib/` or `runtime/`, get the per-buffer
behaviour described below.

**The project root is a ladder, and the client's workspace folders are one of its rungs.** A file inside
the compiler's own `stdlib/` or `runtime/` gets those two tiers and nothing else, and a `runtime/` file is
checked as the build checks tier source: its reserved names and `__Raw` calls are legal, its restrictions
still apply, and a body no program reaches is not call-checked. Any other document is
rooted at the nearest ancestor directory holding a `project.maxon`, searched no higher than the nearest
root the client named that contains the document. Failing that it is rooted at that named root itself;
failing that, at its own directory. **Every** entry of `workspaceFolders` is a root, and `rootUri` is
read only when the folders name none — so a multi-root window has as many roots as it has folders, each
deciding for the documents inside it and for no others. A client that sends no root, or one whose root is
not a `file:` uri — what Remote-SSH, WSL, dev containers and Codespaces send — leaves the manifest search
and the document's own directory. The manifest is used only as a marker of where a project begins — it is
never read and never run.

**The roots are not fixed for the session.** The `initialize` response advertises
`workspace.workspaceFolders` with `supported` and `changeNotifications` both true, and the server then
handles `workspace/didChangeWorkspaceFolders`: a folder added to the window becomes a root at once, and
one removed from it stops being one, with no restart of the editor. Nothing else in the workspace is
renegotiated.

Projects are held across requests, the eight most recently used roots at a time, and a source is re-read
when its size or modification time changes on disk. Removing a workspace folder also drops every project
held under it. The list of files under a root is re-walked at most once a second, so a file created on
disk after the project was built is resolved into shortly afterwards rather than at once, and a file
deleted from disk leaves the project at that same walk — definition, hover and completion stop resolving
into it, and a name it was the only declaration of stops being one the project declares. A rename is the
two together: the answer moves to the new path, rather than one declaration being answered out of both.
Creating and deleting are noticed at the next walk, a change to a file's contents on the next request. A
walk that cannot list a directory leaves the previous list standing rather than treating the files under
it as gone.

**Diagnostics** are published with `textDocument/publishDiagnostics` after every `didOpen` and
`didChange`, and cleared on `didClose`. Each has the error code (for example `E3005`) as `code`,
`source: "maxon"` and severity Error. "No `main` function" (E3001) and the unused-export diagnostics
(E3092, E3093, E3094) are not reported, because a single buffer is not a whole program: an export the
buffer never uses may be read by a sibling file the check cannot see.

**Requests served:**

| Method | Result |
|--------|--------|
| `textDocument/hover` | Markdown: the declaration as written in a `maxon` code block, its `///` doc comment, and for a variable or parameter what it is and its type. A binding is named for where it lives — a module-level constant, a field of the type that declares it, a local, or a loop variable — and its type is read from whatever gives it one: a written type, a literal, a call's declared return type, a `try … otherwise`, a field of another type, the element type of the collection a `for … in` walks, or the element of a generic container reached through a type alias. A name the buffer does not declare is looked up in the project, and so is a member of one: a field or method of a type declared in a sibling file or in the standard library renders too, and only where that file's visibility rules let this file name it. Where the type cannot be named — an element still standing for a type parameter — the hover says it could not infer the type rather than naming the parameter. Keywords and math intrinsics are described too. |
| `textDocument/definition` | The declaration of the name under the cursor. A declaration in the same document is answered from it; otherwise the project's index says which file declares the name, and the answer points into that file. A member of a chained receiver (`a.b.c`, `a.m().n()`) resolves through the same walk the hover uses, so it lands on its declaration too. A name the compiler would refuse as ambiguous, or one no visible declaration carries, answers `null`. |
| `textDocument/completion` | Members after `.` (the trigger character), and while a partial member name is being typed: fields, methods, static functions and enum cases. The receiver may be a type name, `self` or `Self`, a field of the enclosing type, a `for … in` loop variable, a local whose type is evident — including one bound by a call or by a `try … otherwise` — a chain of any of these (`a.b.c`, `a.m().n()`), a receiver whose declared type is a generic-instance type alias, resolved to the base it names, an element taken out of a generic container (`try xs.get(0)`), a field or method of a type declared in another file of the project, or a service handle, whose surface is its service's messages plus `shutdown` and `clone` — what the compiler accepts on a handle — and not the synthesized companion's own field. A type the buffer declares is answered from the buffer, including the members an `extension` over it adds. There is no completion of bare identifiers. |
| `textDocument/formatting` | One edit replacing the whole document with `maxon fmt`'s layout, or `null` when it is already formatted. `insertSpaces: false` indents with tabs; `true` indents with `tabSize` spaces. |
| `textDocument/documentSymbol` | The top-level declarations: functions, types, enums, unions, interfaces and extensions. A block whose `end '…'` the author has not typed yet is left out |
| `textDocument/foldingRange` | Every closed block spanning more than one line |
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
projects the server holds. It is what the VS Code status bar shows. It takes no params. The open documents
are grouped by the project root the ladder above resolves each of them to, one entry per distinct root —
two files of one project are one entry, and two sibling projects are two:

```json
{ "projects": [ { "rootPath": "/home/me/app", "isSingleFile": false, "fileCount": 12 } ] }
```

`rootPath` is that root directory. `isSingleFile` is true only where the ladder gives a document no
project — a file inside `stdlib/` or `runtime/`, or one sitting at a volume root — and then `rootPath` is
the document's own file. A document whose uri spells no filesystem path, or whose path has no directory
above it, resolves to no root at all and contributes no entry, so the list can name fewer projects than
there are open documents. `fileCount` is the number of `.maxon` sources under that root — `*.test.maxon`
and the manifest aside — and is **0** for a root whose corpus the server has not built yet; the request
never sweeps for one, so any hover,
definition or completion in that project is what builds it and the next answer carries the real count.
`rootPath` is a filesystem path, not a URI, and the projects are listed in path order.

## Other editors

Any editor with an LSP client can use the server. Configure it to start the command `maxon` with the
argument `lsp-server` over stdio for files with the `.maxon` extension (language id `maxon`), and to
send full-document synchronization.
