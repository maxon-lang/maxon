# Maxon Language Support (VS Code Extension)

Visual Studio Code extension that provides syntax highlighting and Language Server Protocol (LSP) support for the Maxon programming language.

## Features
- Syntax highlighting for `.maxon` files using a TextMate grammar
- Language Server Protocol support (completion, diagnostics, go-to-definition, etc.) when the `maxon-lsp` server is available
- Language configuration: comment support, bracket pairing, and auto-closing pairs

Note: LSP features are provided by the Maxon Language Server. This extension acts as an LSP client and will only enable advanced language features once the language server binary (`maxon-lsp` or `maxon-lsp.exe`) is built and accessible.

## Requirements
- Visual Studio Code 1.75.0 or later
- Node.js and npm for development tools (TypeScript compilation and testing)
- The Maxon language server executable (`maxon-lsp` or `maxon-lsp.exe`) built and placed in the repository `build` folder so the client can launch it. See the repository `Makefile` for build targets.

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
- If you only want syntax highlighting, no LSP server is required.

### Server location
By default the client attempts to launch the language server at a relative path from the extension runtime. The path was set to `../build/maxon-lsp.exe` in `src/extension.ts`. If you build the LSP server to a different location or platform, adjust the extension source or copy the server executable into the `build` directory adjacent to the extension before packaging.

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

## License

Licensed under either of:

- Apache License, Version 2.0 ([LICENSE-APACHE](../LICENSE-APACHE) or http://www.apache.org/licenses/LICENSE-2.0)
- MIT license ([LICENSE-MIT](../LICENSE-MIT) or http://opensource.org/licenses/MIT)

at your option.

## Notes and Troubleshooting
- If the language server fails to start, check that the binary is built and the path in `src/extension.ts` is correct. The extension currently expects the binary to live at `../build/maxon-lsp.exe` relative to the extension path — adjust or copy as needed.
- When packaging for non-Windows platforms, make sure the server binary does not have `.exe` and the extension's `src/extension.ts` points at the correct filename.
- For LSP server issues, consult the top-level `lsp-server/` folder README and the server build instructions.

---
If you need help with building or testing the extension, open an issue on the repository with your platform, VS Code version, and steps to reproduce the problem.
