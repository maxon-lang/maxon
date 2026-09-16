# Maxon Language Support (VS Code Extension)

Visual Studio Code extension that provides syntax highlighting and Language Server Protocol (LSP) support for the Maxon programming language.

## Features
- Syntax highlighting for `.maxon` files using a TextMate grammar
- **Test file support**: Full language support for `.test` fragment files (only the Maxon code portion)
- Language Server Protocol support (completion, diagnostics, go-to-definition, etc.) from the compiler's own `maxon lsp-server`
- Language configuration: comment support, bracket pairing, and auto-closing pairs
- **Code formatting**: Format your Maxon code with customizable indentation settings
- **Compiler Explorer**: View the Target IR the compiler lowers a program to
- **Spec Test Explorer**: Discover and run spec tests from `specs/*.md` in VS Code's Test Explorer, against the Maxon compiler

The language features come from the Maxon compiler itself, which serves the Language Server
Protocol (`maxon lsp-server`). This extension is its client.

## Requirements
- Visual Studio Code 1.75.0 or later
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

The language server runs from a copy of the compiler in the extension's storage, so a build is never
blocked by the server holding the compiler open. A `stdlib` link beside the copy points at the
original's standard library.

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
- Open a `.test` file (language test fragments) and get full LSP support for the Maxon code portion (before the `---` separator).
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

## Code Formatting

The extension provides automatic code formatting for Maxon files. You can format your code using:

- **Right-click** in the editor and select "Format Document"
- Press **Shift+Alt+F** (Windows/Linux) or **Shift+Option+F** (Mac)
- Enable format-on-save in your settings

### Formatting Configuration

Configure formatting behavior in your VSCode settings:

```json
{
  "maxon.formatting.insertSpaces": false,   // Use tabs (default)
  "[maxon]": {
    "editor.formatOnSave": true,           // Format on save (optional)
    "editor.tabSize": 2,
    "editor.insertSpaces": false
  }
}
```

### What the formatter does:
- Normalizes indentation based on block structure (function, if, while, for, struct)
- Indents type field declarations inside `struct...end` blocks
- Indents type literal fields inside `{...}` braces
- Removes trailing whitespace
- Collapses multiple consecutive blank lines into one
- Ensures proper indentation of `end` statements and closing `}`
- Converts line endings to LF (Unix-style)

## Compiler Explorer

The Compiler Explorer panel shows the Target IR the compiler lowers a program to — the same text `maxon build --emit-ir` writes, for your program's own functions. It compiles for the machine VS Code runs on, and nothing is written to disk.

### Opening the Compiler Explorer

1. Open the Command Palette (**Ctrl+Shift+P** / **Cmd+Shift+P**)
2. Run **Maxon: Open Compiler Explorer**

### Using the Panel

Type or paste a whole program into the **Source** pane. The **Target IR** pane updates shortly after you stop typing. If the program does not compile, the pane lists each error with its line and column instead.

## Spec Test Explorer

The extension contributes a `Maxon Spec Tests` test controller to VS Code's Test Explorer. Tests are discovered by parsing every markdown file under `specs/` in the workspace root: each `<!-- test: name -->` marker becomes a test item under a parent node named after the spec file (e.g. `arithmetic` → `addition`, `subtraction`, …). The controller activates as soon as the workspace contains a `specs/*.md` file, even if the language server fails to start.

### Run profile

One run profile is registered:

- **Maxon Compiler** — runs `maxon-bin/.maxon/maxon.exe spec-test`.

If the compiler binary is missing the run is aborted with an error message — build it first (see the repo root `CLAUDE.md` for build commands).

Specs marked `status: draft` in their frontmatter are skipped; selected items belonging to a skipped spec are reported as `skipped` in the run.

### Filtering

The controller passes `--filter` to the runner based on the selection:

- Run all tests → no filter, single process (the runner walks every spec in its own worker pool, which is dramatically faster than spawning per-spec).
- Run a whole spec → `--filter=<specName>/`.
- Run individual tests → one `--filter=<specName>/<testName>` invocation per test.

### Output parsing

Test results are read from the runner's stdout, which carries one verdict line per selected test and then a summary:

- `PASS <spec>/<test>`, `FAIL <spec>/<test>: <reason>`, `SKIP <spec>/<test>`, `NOTRUN <spec>/<test>`
- `<n> passed, <m> failed`

A `PASS` updates its test item live. A failure's reason — its `FAIL` line and the lines after it, up to the next verdict or the summary — is attached to the failure when the run ends. `SKIP`, `NOTRUN`, and a selected test the runner printed no verdict for are reported as skipped; if the runner stops before its summary, a test with no verdict is reported as errored instead. The runner's stderr is shown in the run output and not parsed.

### Refresh

The controller watches `specs/*.md` and re-syncs the corresponding tree node on change/create/delete. Use the Test Explorer's refresh button to force a full re-scan of the directory.

## Customizing Colors

The extension provides semantic highlighting for block identifiers (e.g., `'loop_label'`). These are colored based on their nesting depth to help visually distinguish nested blocks.

You can customize these colors in your `settings.json`. To apply changes only to Maxon files:

```json
"editor.semanticTokenColorCustomizations": {
    "[maxon]": {
        "rules": {
            "label.level0": "#FF0000", // Outermost blocks
            "label.level1": "#00FF00",
            "label.level2": "#0000FF",
            "label.level3": "#FFFF00",
            "label.level4": "#00FFFF",
            "label.level5": "#FF00FF"  // Deeply nested blocks
        }
    }
}
```

The levels cycle every 6 depths (level 0, 1, 2, 3, 4, 5, 0, 1...).

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
