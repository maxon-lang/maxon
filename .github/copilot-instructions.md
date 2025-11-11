# GitHub Copilot Instructions for Maxon Language Project

## Project Overview

This is the **Maxon programming language** project, which includes:
- A compiler (`maxonc`) written in C++ with LLVM backend
- A Language Server Protocol (LSP) implementation for IDE support
- A VS Code extension for syntax highlighting and language features

## Project Structure

### `/maxon-bin/` - Compiler
- **Language**: C++17 with LLVM
- **Key components**:
  - `lexer.cpp/h`: Tokenization of Maxon source code
  - `parser.cpp/h`: Recursive descent parser producing AST
  - `ast.h`: AST node definitions
  - `codegen.cpp/h`: LLVM IR generation
  - `main.cpp`: Compiler entry point
- **Build system**: CMake with Ninja

### `/lsp/` - Language Server
- **Language**: C++17
- **Key components**:
  - `lsp_server.cpp/h`: Main LSP server implementation
  - `json_rpc.cpp/h`: JSON-RPC protocol handling
  - `document_manager.cpp/h`: Text document synchronization
  - `analyzer.cpp/h`: Semantic analysis and diagnostics
  - `lsp_types.h`: LSP protocol type definitions
- **Dependencies**: nlohmann/json for JSON parsing
- **Build system**: CMake

### `/vscode-extension/` - VS Code Extension
- **Language**: TypeScript
- **Purpose**: Syntax highlighting and LSP client
- **Key files**:
  - `extension.ts`: Extension activation and LSP client setup
  - `syntaxes/maxon.tmLanguage.json`: TextMate grammar
  - `language-configuration.json`: Bracket matching, comments
- **Build system**: npm with TypeScript compiler

### `/docs/` - Documentation Generator
- **Language**: C# (.NET)
- **Purpose**: Generate HTML documentation and extract test fragments from Markdown files
- **Key components**:
  - `DocGen.cs`: Documentation generator that processes Markdown files
  - `Content/`: Source Markdown files with code examples (e.g., `control flow.md`, `variables.md`)
  - `Output/`: Generated HTML documentation files
- **Functionality**:
  - Converts Markdown to HTML using Markdig with Bootstrap styling
  - Extracts code blocks marked with `~~~` into test fragments
  - Generates test files in `language-tests/doc-fragments/` with expected exit codes
  - Code blocks can include `ExitCode: <number>` to specify expected results
- **Build system**: .NET SDK (dotnet run)

## Coding Guidelines

### C++ Code (Compiler & LSP)
1. **Use modern C++17 features**: smart pointers, auto, range-based for loops
2. **Memory management**: Prefer `std::unique_ptr` and `std::shared_ptr` over raw pointers
3. **Error handling**: Use exceptions for exceptional cases, return values for expected errors
4. **LLVM conventions**: Follow LLVM coding style when working with LLVM APIs
5. **AST nodes**: All AST nodes inherit from base classes (`ExprAST`, `StmtAST`, etc.)
6. **Lexer/Parser**: The lexer produces tokens, parser consumes tokens to build AST

### TypeScript Code (VS Code Extension)
1. **Use VS Code extension APIs properly**: Follow official VS Code extension patterns
2. **LSP client setup**: Use `vscode-languageclient` package
3. **Async/await**: Prefer async patterns for extension operations
4. **Type safety**: Always use TypeScript types, avoid `any` when possible

### LSP Implementation
1. **Follow LSP spec**: Implement handlers according to official Language Server Protocol
2. **Text synchronization**: Use `DocumentManager` for tracking document state
3. **Diagnostics**: Publish diagnostics asynchronously after document changes
4. **Capabilities**: Declare server capabilities accurately in `initialize` response

## Testing

- **Compiler tests**: Use sample `.maxon` files in `maxon-bin/`
- **LSP tests**: Unit tests in `lsp/tests/` using a test framework
- **Extension testing**: VS Code extension test framework in `vscode-extension/src/test/`
- **Language tests**: Fragment tests in `language-tests/` using C# NUnit
  - `fragments/`: Manual test fragments
  - `doc-fragments/`: Auto-generated from documentation (via `make docs`)
  - Each `.test` file contains: Maxon code, separator (`---`), LLVM IR or N/A, separator, expected output/exit code

### Creating Test Fragments

Test fragment format:
```
<Maxon source code>
---
<Expected LLVM IR or "N/A" for error tests>
---
ExitCode: <expected exit code>
```

**Important rules for test fragments:**
1. **Always include the LLVM IR** unless the test is for a compilation error (parser/semantic error)
2. Use `N/A` for the IR section **only** when the test code intentionally doesn't compile
3. Generate IR by running: `.\build\bin\maxonc.exe <file.maxon> --emit-llvm -O`
4. Regular fragment tests use optimization (`-O` flag), so include optimized IR
5. Debug fragment tests use `--debug` flag and no optimization
6. Use module name `test.maxon` in the expected IR (the test framework standardizes this)

**Example workflow for adding a test:**
1. Create the test file with Maxon code and temporary `N/A` for IR
2. Run `.\build\bin\maxonc.exe temp.maxon --emit-llvm -O` to get optimized IR
3. Replace `N/A` with the generated IR (changing module name to `test.maxon`)
4. Add `ExitCode: <number>` line
5. Run `make language-tests` to verify

## Common Tasks

### Adding a new keyword
1. Add to `TokenType` enum in `lexer.h`
2. Update `Lexer::readIdentifier()` keyword map in `lexer.cpp`
3. Add parsing logic in `parser.cpp`
4. Update AST if needed in `ast.h`
5. Add codegen in `codegen.cpp`
6. Update TextMate grammar in `vscode-extension/syntaxes/maxon.tmLanguage.json`

### Adding LSP feature
1. Add handler method in `LspServer` class
2. Register handler in `JsonRpcHandler`
3. Implement analysis logic in `Analyzer`
4. Update capabilities in `initialize` response

### Adding a new language feature
1. Implement lexer/parser/codegen changes
2. Build compiler: `make compiler`
3. Create test fragment in `language-tests/fragments/` with proper LLVM IR
4. Run tests: `make language-tests`
5. Update documentation in `docs/Content/` if user-facing
6. Regenerate docs: `make docs` (creates additional doc-fragment tests)

## Key Principles

1. **Block identifiers**: Multi-line blocks (functions, while, multi-line if) require matching start/end identifiers
2. **Single-line if**: Single-statement if on one line doesn't require block identifiers: `if x = 11 break`
3. **Type safety**: Maxon is statically typed
4. **LLVM backend**: All code generation targets LLVM IR
5. **LSP-first IDE support**: Language features are provided via LSP, not hardcoded in extension
6. **Cross-platform**: Code should work on Windows, Linux, and macOS

## When suggesting code:

- **For Maxon language code**: 
  - Use block identifiers with `end 'identifier'` syntax for multi-line blocks
  - Single-line if is allowed: `if <condition> <statement>` (no `end` needed when on one line)
  - Multi-line if still requires block identifiers: `if <condition> 'id' ... end 'id'`
- **For C++ compiler code**: Use modern C++, LLVM APIs, and smart pointers
- **For LSP code**: Follow LSP specification and use proper JSON-RPC formatting
- **For VS Code extension**: Use TypeScript with proper VS Code extension patterns
- **Comments**: Keep them concise and explain *why*, not *what*

## Debug Tips

- **Compiler**: Use LLVM's `llvm::errs()` for debug output
- **LSP**: Log to stderr (stdout is reserved for JSON-RPC)
- **Extension**: Use VS Code's Debug Console and extension host debugging

## Make Commands

Use the top-level Makefile for all build and development tasks. Run commands from the project root using `make <target>`.

### Building
- `make all` - Configure and build both compiler and LSP server (default target)
- `make configure` - Configure CMake build system
- `make compiler` - Build only the Maxon compiler (`maxonc.exe`)
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
- `make language-tests` - Run Maxon language fragment tests (C# NUnit tests)
- `make docs` - Generate HTML documentation and extract test fragments from Markdown

### Cleanup
- `make clean` - Remove all build artifacts (compiler, LSP, extension)
- `make help` - Display all available make targets

### Common Workflows
- **First time setup**: `make all` then `make extension`
- **Compiler development**: `make compiler` after changes
- **LSP development**: `make lsp-server` then reload VS Code
- **Extension development**: `make extension-watch` in background, then reload VS Code
- **Full rebuild**: `make clean` then `make all`

## General Guidelines
- **Do not create new documents**: Unless specifically instructed
- **Use make commands**: Always use the Makefile commands listed above for building and testing
