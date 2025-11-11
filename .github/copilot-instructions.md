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

## Key Principles

1. **Block identifiers are mandatory**: Every Maxon block must have matching start/end identifiers
2. **Type safety**: Maxon is statically typed
3. **LLVM backend**: All code generation targets LLVM IR
4. **LSP-first IDE support**: Language features are provided via LSP, not hardcoded in extension
5. **Cross-platform**: Code should work on Windows, Linux, and macOS

## When suggesting code:

- **For Maxon language code**: Always use block identifiers with `end 'identifier'` syntax
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
