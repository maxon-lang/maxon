# Maxon Language Support (VS Code Extension)

Visual Studio Code extension that provides syntax highlighting and Language Server Protocol (LSP) support for the Maxon programming language.

## Features
- Syntax highlighting for `.maxon` files using a TextMate grammar
- Language Server Protocol support (completion, diagnostics, go-to-definition, etc.) from the compiler's own `maxon lsp-server`
- **Go to definition and hover across files**: F12 on a name declared in another file of your project, or
  in the standard library, opens that file, and hovering one renders its declaration. The project is the
  nearest directory above the file that holds a `build.maxon`, searched no higher than the workspace root;
  failing that, the workspace root itself. Hover and completion also resolve a receiver typed by `self`,
  by a call, by a `try … otherwise`, by a field of the enclosing type, or by a `for … in` loop variable.
- **Diagnostics that know about your other files**: the server no longer reports a name as undeclared
  when a sibling file of the project declares it. Checking is still done per buffer, so an error only the
  whole program could raise is not reported; and while a file is unsaved, errors about its own text are
  published immediately while name-dependent ones wait until you save. It needs a workspace root — with
  none open, diagnostics are per buffer as before.
- Language configuration: comment support, bracket pairing, and auto-closing pairs
- **Code formatting**: the language server's formatter, applied on save by default
- **Compiler Explorer**: View the Target IR the compiler lowers a program to
- **Test Explorer**: Discover the `test` declarations in your `*.test.maxon` files and run them with `maxon test`

The language features come from the Maxon compiler itself, which serves the Language Server
Protocol (`maxon lsp-server`). This extension is its client.

## Requirements
- Visual Studio Code 1.107.0 or later, or an editor built on it
- The Maxon compiler. If the extension cannot find one it offers to install it, using the same
  one-line installer as the [installation page](https://maxon.dev/docs/getting-started/installation/).

## Installation

Install **Maxon** from the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=maxon-lang.maxon-lsp-client)
or [Open VSX](https://open-vsx.org/extension/maxon-lang/maxon-lsp-client).

### Finding the compiler

The extension looks in this order:

1. The `maxon.serverPath` setting, if you set one.
2. `maxon` on your `PATH`.
3. `~/.maxon/bin` (`%USERPROFILE%\.maxon\bin` on Windows), or `$MAXON_INSTALL/bin` — where the install
   script puts it. Searched directly, because a VS Code started from the dock or the Start menu may not
   see your shell's `PATH`.
4. This workspace's own build at `maxon-bin/.maxon/`, so a contributor with a built tree needs no
   configuration.

If none of those finds one, it offers **Install**, which runs the installer for your account, and
**Locate…**, which lets you pick a compiler you already have.

The language server is the compiler it finds, run as `maxon lsp-server`. When that file is rebuilt
the extension restarts the server, so it answers from the new compiler.

### From source (for working on the extension)
1. Build the compiler in the repository root (see the repository README); the extension finds
   `maxon-bin/.maxon/maxon` in the workspace.

2. Install extension dependencies and compile the extension:

```powershell
cd vscode-extension
npm install
npm run compile
```

3. Install the packaged extension (optional):

```powershell
npm run package       # creates a .vsix file
npm run install-extension
```

## Usage
- Open a `.maxon` file in VS Code. If the LSP server binary is available and runs correctly, you should get diagnostics, code completion, and basic navigation features.
- A `.test` file (a spec fragment in the Maxon checkout) is highlighted as Maxon and sent to the language server whole, exactly like a `.maxon` file; nothing in it is split off or skipped.
- If you only want syntax highlighting, no LSP server is required.

## Development
- Use the `watch` script during development to compile TypeScript and auto-emit changes:

```powershell
cd vscode-extension
npm run watch
```

- In VS Code, open the `vscode-extension` folder and launch the extension host via the Run/Debug panel to test and iterate quickly.

### Extension build and packaging
- `npm run compile` — compile TypeScript to JavaScript (output is `out/`)
- `npm run package` — build a `.vsix` package using `vsce`
- `npm run install-extension` — installs the generated `.vsix` locally

## Testing
- The extension uses `@vscode/test-electron` for integration tests and `mocha` for unit testing.
- To run tests:

```powershell
cd vscode-extension
npm test
```

- `npm run test:unit` compiles the extension and runs the unit tests that need no VS Code instance
  (`src/test/unit/`), such as how a `maxon test --json` report maps onto Test Explorer results.
- `npm run test:grammar` checks the TextMate grammar against the snapshots in `test-fixtures/`.

## Code Formatting

The extension has no formatter of its own. **Format Document** (**Shift+Alt+F**, or **Shift+Option+F** on
Mac) sends `textDocument/formatting` to the language server, which re-prints the whole document with the
compiler's formatter and answers with one edit replacing all of it — or with nothing when the document is
already formatted.

The extension sets `editor.formatOnSave` to `true` for Maxon files, so saving formats. Turn it off in your
settings to format only on request:

```json
{
  "[maxon]": {
    "editor.formatOnSave": false
  }
}
```

### Indentation

Maxon indents with tabs. Two settings choose what the server is asked for:

- `maxon.formatting.insertSpaces` (default `false`): indent with spaces instead.
- `maxon.formatting.tabSize` (default `2`): how many spaces make one indent. The server reads it only
  when `insertSpaces` is `true`; with tabs it is ignored.

These settings replace the editor's own `editor.insertSpaces` and `editor.tabSize` for the formatting
request.

## Compiler Explorer

The Compiler Explorer panel shows the Target IR the compiler lowers a program to — the same text `maxon build --emit-ir` writes, for your program's own functions. It compiles for the machine VS Code runs on, and nothing is written to disk.

### Opening the Compiler Explorer

1. Open the Command Palette (**Ctrl+Shift+P** / **Cmd+Shift+P**)
2. Run **Maxon: Open Compiler Explorer**

### Using the Panel

Type or paste a whole program into the **Source** pane. The **Target IR** pane updates shortly after you stop typing. If the program does not compile, the pane lists each error with its line and column instead.

## Test Explorer

The extension contributes a **Maxon Tests** controller to VS Code's Test Explorer. It lists every
`test '<name>'` declaration in the workspace's `*.test.maxon` files, one node per file with its tests
beneath it, and runs them with [`maxon test`](https://maxon.dev/docs/cli/#maxon-test) — the same compiler
the language server uses (see [Finding the compiler](#finding-the-compiler)). The tests are listed even
when no compiler is found; running them then reports that none was.

### Discovery

Test files are read from disk, and the tree follows them as they are created, saved and deleted. A file
beneath a directory holding a `.maxonignore` is not listed, because no `maxon test` run compiles it. The
refresh button in the Test Explorer re-reads every test file.

### Which project a test runs in

`maxon test <directory>` compiles every `.maxon` file beneath the directory, together with its tests, as
one program. A test file's project is the highest directory reachable from the file's own directory
through parent directories that each hold a `.maxon` source (a `build.maxon` manifest does not count),
never above the workspace folder. So a `lib/math.test.maxon` in a project whose `main.maxon` is at the
root runs from the root, and `tests/cli/help.test.maxon` runs from `tests/cli/` when `tests/` itself
holds no source.

The compiler runs in the workspace folder, so a relative path inside a test means what it means in a
terminal opened there.

### Running

Each project the selection touches is one invocation:

```text
maxon test <project> --json [--filter=<patterns>]
```

Running every test of a project passes no filter. Otherwise the filter names each file whose tests are
all selected by its path, and each other selected test by its name. The compiler matches a pattern as a
case-insensitive substring, so the run may include more tests than you selected; only the selected
ones are reported.

### Results

`--json` prints one report, which the extension matches to the tree by file and test name:

| `state` | Test Explorer |
|---------|---------------|
| `passed` | passed, with its duration |
| `failed` | failed, with the assertion's output, located at the assertion's line |
| `crashed`, `timedOut`, `leaked` | failed, saying which |
| `didNotRun` | errored |

A test that threw an error nothing caught names the error, and points at where it was thrown when that
file exists. Anything a test printed goes to the run's output.

When `maxon test` exits with code 2 it could not run at all — a compile error, most often — and every
test in that project is errored with what it printed. A selected test missing from the report is errored
too, with the report's reason when it ran nothing: the compiler reads saved files, so a test that is not
saved yet is not in it.

### The spec suite

When the workspace folder is the Maxon compiler checkout — it holds both `specs/` and `maxon-bin/` — a
second controller, **Maxon Spec Suite**, lists the `<!-- test: name -->` markers in `specs/*.md` under
one node per spec, and runs them with the tree's own build, `maxon-bin/.maxon/maxon spec-test`. A run of
every spec passes no filter; a whole spec runs as `--filter=<spec>/`, and each single test as
`--filter=<spec>/<test>`. Specs marked `status: draft` are reported as skipped. Verdicts are read from the
`PASS`, `FAIL`, `SKIP` and `NOTRUN` lines on stdout; a selected test with no verdict is skipped when the
run reached its `<n> passed, <m> failed` summary, and errored when it did not.

## License

Licensed under either of:

- Apache License, Version 2.0 ([LICENSE-APACHE](../LICENSE-APACHE) or http://www.apache.org/licenses/LICENSE-2.0)
- MIT license ([LICENSE-MIT](../LICENSE-MIT) or http://opensource.org/licenses/MIT)

at your option.

## Notes and Troubleshooting
- If the language server fails to start, the **Maxon Language Server** output channel says which compiler
  it found, or that it found none. See [Finding the compiler](#finding-the-compiler).
- For LSP server issues, the embedded server code is in `maxon-bin/Compiler/Lsp/`.

---
If you need help with building or testing the extension, open an issue on the repository with your platform, VS Code version, and steps to reproduce the problem.
