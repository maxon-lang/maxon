# Maxon Language Support (VS Code Extension)

Visual Studio Code extension that provides syntax highlighting and Language Server Protocol (LSP) support for the Maxon programming language.

## Features
- Syntax highlighting for `.maxon` files using a TextMate grammar
- **Test file support**: Full language support for `.test` fragment files (only the Maxon code portion)
- Language Server Protocol support (completion, diagnostics, go-to-definition, etc.) when the `maxon-lsp` server is available
- Language configuration: comment support, bracket pairing, and auto-closing pairs
- **Code formatting**: Format your Maxon code with customizable indentation settings

Note: LSP features are provided by the Maxon Language Server. This extension acts as an LSP client and will only enable advanced language features once the language server binary (`maxon-lsp-server` or `maxon-lsp-server.exe`) is built and accessible.

## Requirements
- Visual Studio Code 1.75.0 or later
- Node.js and npm for development tools (TypeScript compilation and testing)
- The Maxon language server executable (`maxon-lsp-server` or `maxon-lsp-server.exe`) built and placed in the repository `build` folder so the client can launch it. See the repository `Makefile` for build targets.

## Installation

### From source (recommended for developers)
1. Build the LSP server and (optionally) the compiler:

```powershell
# From the repository root
make all
```

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

### Marketplace (future)
If/when published, the extension will be installable from the VS Code Marketplace.

## Usage
- Open a `.maxon` file in VS Code. If the LSP server binary is available and runs correctly, you should get diagnostics, code completion, and basic navigation features.
- Open a `.test` file (language test fragments) and get full LSP support for the Maxon code portion (before the `---` separator).
- If you only want syntax highlighting, no LSP server is required.

### Server location
By default the client attempts to launch the language server at a relative path from the extension runtime. The path was set to `../bin/maxon-lsp-server.exe` in `src/extension.ts`. If you build the LSP server to a different location or platform, adjust the extension source or copy the server executable into the `bin` directory adjacent to the extension before packaging.

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

Or use the repository's make target for extension tests:

```powershell
# from repo root
make extension-test
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
  "maxon.formatting.insertSpaces": false,  // Use tabs (default)
  "maxon.formatting.tabSize": 4,           // Tab size (when using spaces)
  "[maxon]": {
    "editor.formatOnSave": true,           // Format on save (optional)
    "editor.tabSize": 4,
    "editor.insertSpaces": false
  }
}
```

### What the formatter does:
- Normalizes indentation based on block structure (function, if, while, for, struct)
- Indents struct field declarations inside `struct...end` blocks
- Indents struct literal fields inside `{...}` braces
- Removes trailing whitespace
- Collapses multiple consecutive blank lines into one
- Ensures proper indentation of `end` statements and closing `}`
- Converts line endings to LF (Unix-style)

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
- If the language server fails to start, check that the binary is built and the path in `src/extension.ts` is correct. The extension currently expects the binary to live at `../bin/maxon-lsp-server.exe` relative to the extension path — adjust or copy as needed.
- When packaging for non-Windows platforms, make sure the server binary does not have `.exe` and the extension's `src/extension.ts` points at the correct filename.
- For LSP server issues, consult the top-level `lsp-server/` folder README and the server build instructions.

---
If you need help with building or testing the extension, open an issue on the repository with your platform, VS Code version, and steps to reproduce the problem.
