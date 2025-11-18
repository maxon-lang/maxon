# GitHub Copilot Instructions for Maxon Language Project

## Project Overview

## Project Structure

- **`/maxon-bin/`** - Compiler (C++17, CMake/Ninja)
  - `lexer.cpp/h`, `parser.cpp/h`, `ast.h`, `codegen.cpp/h`, `main.cpp`
  - Lexer → Parser (AST) → Semantic Analyzer → LLVM IR Codegen

- **`/lsp-server/`** - Language Server (C++17, CMake)
  - `lsp_server.cpp/h`, `json_rpc.cpp/h`, `document_manager.cpp/h`, `analyzer.cpp/h`
  - Uses nlohmann/json

- **`/vscode-extension/`** - VS Code Extension (TypeScript, npm)
  - `extension.ts`, `syntaxes/maxon.tmLanguage.json`, `language-configuration.json`

- **`/maxon-runtime/`** - Runtime Library
  - `runtime.ll` - LLVM IR implementation of runtime functions (memset, etc.)
  - `runtime.obj` - Compiled runtime library (auto-linked with all programs)
  - Provides functions that LLVM intrinsics lower to (e.g., `llvm.memset` → `memset`)

- **`/docs/`** - Documentation Generator
  - `DocGen.cs` converts Markdown to HTML and extracts test fragments
  - Code blocks with `ExitCode:` → `language-tests/doc-fragments/*.test`

## Coding Standards

**Comments:** Explain *why*, not *what*

## Testing

- **Language tests**: `language-tests/` (C# NUnit)
  - `fragments/`: Manual test fragments
  - `doc-fragments/`: Auto-generated from docs
  - Format: Maxon code, `---`, LLVM IR (or N/A), `---`, ExitCode
- **LSP tests**: `lsp-server/tests/`
- **Extension tests**: `vscode-extension/src/test/`

### Creating Test Fragments

```powershell
# Create new test fragment from .maxon source
.\create-test-fragment.ps1 -TestName "my-test" -SourceFile "examples/test.maxon"

# Update existing test fragment (regenerates IR)
.\create-test-fragment.ps1 -TestName "my-test" -SourceFile "language-tests/fragments/my-test.test"

# Debug mode (no optimization)
.\create-test-fragment.ps1 -TestName "my-test" -SourceFile "test.maxon" -UseDebug
```

The script:
- Compiles with `-O` (or `--debug` for debug mode)
- Extracts normalized LLVM IR
- Captures exit code and stdout
- Creates/updates the `.test` file
- Can update existing `.test` files by parsing them and regenerating IR

**Test Fragment Format:**
```
Maxon source code
---
Expected LLVM IR (or N/A for compilation errors)
---
ExitCode: N
```

**Important:** Tests will fail if a fragment has `N/A` for IR but the code compiles successfully. Use the script to regenerate proper IR.

## Common Tasks

### Adding a keyword
1. Add to `TokenType` enum in `lexer.h`
2. Update `Lexer::readIdentifier()` keyword map in `lexer.cpp`
3. Add parsing logic in `parser.cpp`
4. Update AST in `ast.h` if needed
5. Add codegen in `codegen.cpp`
6. Update TextMate grammar in `vscode-extension/syntaxes/maxon.tmLanguage.json`

### Adding an LSP feature
1. Add handler method in `LspServer` class
2. Register in `JsonRpcHandler`
3. Implement in `Analyzer`
4. Update capabilities in `initialize` response

### Adding a language feature
1. Implement lexer/parser/codegen changes
2. `make compiler`
3. Create test: `.\create-test-fragment.ps1`
4. `make language-tests`
5. Update `docs/Content/*.md` if user-facing
6. `make docs` (creates doc-fragment tests)


## Make Commands

Use the top-level Makefile for all build and development tasks. Run from project root: `make <target>`

### Building
- `make all` - Configure and build compiler, runtime library, and LSP server (default target)
- `make configure` - Configure CMake build system
- `make runtime` - Build Maxon runtime library (runtime.obj)
- `make compiler` - Build only the Maxon compiler (`maxon.exe`)
- `make lsp-server` - Build only the C++ LSP server
- `make lsp` - Build both LSP server and VS Code extension

### VS Code Extension
- `make extension` - Install npm dependencies and build the extension
- `make extension-build` - Compile extension (assumes dependencies installed)
- `make extension-watch` - Start watch mode for extension development
- `make extension-test` - Run extension test suite
- `make extension-package` - Package extension as .vsix file
- `make extension-install` - Install extension locally in VS Code

### Testing
- `make lsp-test` - Build and run LSP C++ unit tests
- `make language-tests` - Run Maxon language fragment tests (C# NUnit)
- `make docs` - Generate HTML documentation and extract test fragments

### Cleanup
- `make clean` - Remove all build artifacts (compiler, LSP, extension, runtime.obj)
- `make help` - Display all available make targets

## Runtime Library

The Maxon runtime library (`maxon-runtime/runtime.obj`) provides essential functions that the compiler-generated code needs:

- **`memset(ptr, int, int) -> ptr`** - Fill memory with a constant byte value
  - Called when LLVM lowers `llvm.memset` intrinsics during optimization
  - Avoids dependency on C runtime library
  - Automatically linked with all Maxon programs

**Adding runtime functions:**
1. Add implementation to `maxon-runtime/runtime.ll` (LLVM IR)
2. `make runtime` to rebuild runtime.obj
3. Functions are automatically available to all Maxon programs

The runtime library is found relative to the compiler executable and automatically linked during the final linking stage in `codegen.cpp`.

## General Guidelines
- **Do not create new documents** unless specifically instructed
- **Use make commands** for building and testing

The 'bin' directory is in the path so you can just use "maxon.exe" without a path from anywhere.

When testing sample code always use "maxon <file>" to compile and run in one step without leaving temporary files behind.

If you need to create files to test compilation then put them in the /temp directory and clean them up afterwards.
