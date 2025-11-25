---
name: maxon-compiler
description: Build, debug, and develop the Maxon compiler. Use when working with Maxon compiler source code, fixing compiler bugs, adding language features, debugging code generation, or running tests.
---

# Maxon Compiler Development

Assist with building, debugging, and developing the Maxon programming language compiler.

## Project Overview

Maxon is a statically-typed programming language with a custom native x86-64 backend:

- **Compiler** (`maxon-bin/`) - C++ compiler generating native x86-64 code via MIR
- **Language Server** (`lsp-server/`) - C++ LSP implementation for IDE integration
- **VS Code Extension** (`vscode-extension/`) - Syntax highlighting, debugging, language features
- **Runtime Library** (`maxon-runtime/`) - Platform-specific runtime (no C runtime dependency)
- **Specs & Tests** (`specs/`, `language-tests/`) - Spec-driven development and testing

## Compilation Pipeline

```
Source (.maxon) → Lexer → Parser → AST → Semantic Analyzer → MIR → Optimizer → x86 CodeGen → Executable
```

## Instructions

### When Building the Compiler

Use `make compiler` to build the compiler. Use `make all` to build everything. See [BUILD.md](BUILD.md) for all build targets.

### When Debugging Compiler Issues

1. Create a minimal test case that reproduces the issue
2. Use `maxon compile file.maxon --emit-ir` to inspect the MIR
3. Use `maxon compile file.maxon -vvv` for maximum verbosity
4. See [DEBUGGING.md](DEBUGGING.md) for common issues and debugging workflow

### When Adding Language Features

Follow spec-driven development:
1. Create/edit spec in `specs/<feature-name>.md`
2. Run `make test` to extract, regenerate, and test
3. See [DEVELOPMENT.md](DEVELOPMENT.md) for patterns (adding keywords, LSP features, runtime functions)

### When Writing Maxon Code

Read `docs/LANGUAGE_REFERENCE.md` for complete syntax. Quick reminders:
- Block identifiers must match: `if x 'label' ... end 'label'`
- Keywords: `and`, `or`, `not` (not `&&`, `||`, `!`)
- Single `=` for equality (not `==`)
- Float literals require decimal: `5.0` not `5`

### When Running Tests

Use `make test` to run all tests. See [BUILD.md](BUILD.md) for specific test commands.

## Constraints

1. **No C Runtime** - Use `maxon-runtime/` for all system functionality
2. **Clean up temp files** - Create test files in `/temp` and delete after testing
3. **Don't use here documents** - Write files directly, not via `echo` or `cat <<EOF`
4. **Comments explain "why"** - Not "what" the code does
5. **Absolute paths** - Always use absolute paths for file operations
