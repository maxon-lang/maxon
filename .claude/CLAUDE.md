# Maxon Compiler Development

## Project Overview

Maxon is a statically-typed programming language with a custom native x86-64 backend (no LLVM dependency for code generation):

- **Compiler** (`maxon-bin/`) - C++ compiler generating native x86-64 via MIR
- **Language Server** (`lsp-server/`) - C++ LSP for IDE integration
- **VS Code Extension** (`vscode-extension/`) - Syntax highlighting, debugging
- **Runtime Library** (`maxon-runtime/`) - Platform-specific runtime (no C runtime)
- **Standard Library** (`stdlib/`) - Maxon standard library modules
- **Tests** (`language-tests/`, `backend-tests/`, `specs/`) - Spec-driven development

## Quick Reference

| Task | Command |
|------|---------|
| Build everything | `make all` |
| Build compiler only | `make compiler` |
| Run all tests | `make test` |
| Run backend tests | `make backend-test` |
| Compile and run | `./bin/maxon file.maxon` |
| Compile with MIR output | `./bin/maxon compile file.maxon --emit-ir` |
| Compile and run lsp tests | `make lsp-test` |

## Maxon Language Overview

Maxon is a statically-typed language with labeled blocks and explicit `end` statements:

- **Types**: `int`, `float`, `bool`, `byte`, `char`, `string`, arrays (`[10]int`), structs, maps
- **Variables**: `var` (mutable), `let` (immutable), ie `let x = 5`
- **Functions**: `function fname(param type) returnType ... end 'fname'`
- **Control flow**: `if`/`else`, `while`, `for`/`in` with range() - all require block labels
- **Operators**: Arithmetic, comparison, logical (`and`, `or`, `not`), `mod`, `as` (cast)
- **Structs**: `struct SName ... end 'SName'` with methods using explicit `self` parameter

See `docs/LANGUAGE_REFERENCE.md` for complete syntax and semantics.

## Documentation

- **`docs/LANGUAGE_REFERENCE.md`** - Complete Maxon language syntax and semantics
- **`docs/SPECS.md`** - Spec file format, workflow, and how to write specs
- **`maxon-runtime/README.md`** - Runtime library details

## Directory Structure

```
maxon/
├── maxon-bin/           # Compiler source (C++)
│   ├── backend/         # x86-64 native code generator
│   ├── codegen_mir/     # MIR code generation from AST
│   ├── mir/             # MIR infrastructure & optimizer passes
│   ├── parser/          # Parser implementation
│   └── semantic_analyzer/
├── maxon-runtime/       # Runtime library (handwritten MIR)
├── stdlib/              # Standard library (Maxon source)
├── lsp-server/          # Language server (C++)
├── vscode-extension/    # VS Code extension (TypeScript)
├── language-tests/      # Language test suite
├── backend-tests/       # Backend-specific tests
├── specs/               # Language specifications (source of truth)
└── docs/                # Documentation
```

## Key Compiler Files

- `lexer.cpp/h` - Tokenization with SIMD optimizations
- `parser.cpp` + `parser/` - Recursive descent parser
- `semantic_analyzer.cpp` - Type checking, name resolution
- `codegen_mir.cpp` + `codegen_mir/` - AST to MIR translation
- `mir/optimizer.cpp` + `mir/opt_*.cpp` - Optimization passes
- `backend/x86_codegen.cpp` - MIR to x86-64 machine code
- `backend/pe_writer.cpp` - Windows PE executable writer
- `backend/elf_writer.cpp` - Linux ELF executable writer

## Spec-Driven Development

**All language features must have a spec file in `specs/`** - this is the single source of truth.

Each spec contains three sections:
1. **Developer Notes** - Implementation details for maintainers
2. **Documentation** - User-facing docs (extracted to HTML)
3. **Tests** - Test cases (extracted to `language-tests/fragments/`)

Workflow for new features:
1. Create `specs/feature-name.md` with frontmatter, notes, docs, and tests
2. `maxon extract-specs` - Extract test fragments from spec
3. `maxon regen-fragments` - Generate IR for test fragments
4. Implement until tests pass
5. `make docs` - Generate HTML documentation

See `docs/SPECS.md` for the complete spec file format and detailed workflow.

## Debugging

The compiler generates DWARF debug info for source-level debugging. Use `-g` flag to include debug symbols.

Debugging tools:
- **Windows**: Use VS Code with the Maxon extension (integrates Windows Debug API)
- **Linux**: Use LLDB or GDB with the compiled executable

Debugger tests are in `debugger-tests/` - run with `make debugger-test`.

See `docs/COMPILER_DEBUGGING.md` for detailed workflow.

## Constraints
- **No C Runtime** - Use `maxon-runtime/` for all system functionality
- **Clean up temp files** - Create test files in `/temp` and delete after
- **Don't use here documents** - Write files directly with file tools
- **Comments explain "why"** - Not "what" the code does
- **Absolute paths** - Always use absolute paths for file operations
- **LF line endings** - All source files use Unix-style line endings
- Don't create new documentation files unless instructed
- Do not edit test fragments (in /language-tests/fragments). These are generated from the spec files, edit the spec file.
- **Do not use git** - Ignore any git history just fix the current tree

## Development Notes
- Build system uses CMake with Ninja generator
- Windows requires Git Bash for Make commands
- Linux development uses dev container (recommended)
- All tests must pass before commits to main

## Writing VSCode Extension Tests
- Do not set timeouts
- Do not use arbitrary delays, wait for what you are expecting
