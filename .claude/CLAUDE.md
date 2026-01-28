# Maxon Compiler Development

## Project Overview

Maxon is a statically-typed programming language with a custom native x86-64 backend (no LLVM dependency for code generation):

- **Compiler** (`maxon-sharp/`) - C# compiler using MLIR-style pipeline generating native x86-64
- **VS Code Extension** (`vscode-extension/`) - Syntax highlighting, debugging
- **Standard Library** (`stdlib/`) - Maxon standard library modules
- **Specs & Tests** (`/specs/`) - Spec-driven development with test fragments

## Documentation

- **`docs/QUICK_REFERENCE.md`** - Concise overview of the Maxon language
- **`docs/LANGUAGE_REFERENCE.md`** - Complete Maxon language syntax and semantics
- **`docs/SPECS.md`** - Spec file format, workflow, and how to write specs

## Compiler Architecture (maxon-sharp)

The compiler uses an MLIR-inspired multi-stage pipeline:

1. **Lexer/Parser** - Source → AST
2. **AST to Maxon Dialect** - AST → Maxon dialect ops (ownership semantics)
3. **Maxon to Standard** - Maxon ops → Standard dialects (Arith, MemRef, Func, Cf)
4. **Standard to X86** - Standard ops → X86 dialect ops
5. **Register Allocation** - Virtual registers → Physical registers
6. **Code Emission** - X86 ops → Machine code bytes
7. **PE Generation** - Machine code → Windows PE executable

Key directories in `maxon-sharp/Compiler/MLIR/`:
- `Core/` - MlirOperation, MlirValue, MlirFunction, MlirBlock
- `Dialects/` - Maxon, Arith, MemRef, Func, Cf, X86 dialects
- `Conversion/` - Dialect lowering patterns (AstToMaxonDialect, MaxonToStandard, StandardToX86)
- `Passes/` - Optimization and transformation passes
- `Emit/` - X86CodeEmitter for machine code generation

## Spec-Driven Development

**All language features must have a spec file in `/specs/`** - this is the single source of truth.

Each spec contains:
1. **Documentation** - User-facing docs
2. **Tests** - Test cases (extracted to `/specs/fragments/`)

Workflow for new features:
1. Create `/specs/feature-name.md` with frontmatter, notes, docs, and tests
2. Run `maxonsharp spec-test` to extract and run test fragments
3. Implement until tests pass

See `docs/SPECS.md` for the complete spec file format and detailed workflow.

## Constraints

### CRITICAL: No Git Commands
**NEVER use git commands.** This includes git status, git diff, git add, git commit, git log, git checkout, or any other git command. This constraint applies at all times, regardless of context or how long the session has been running.

### CRITICAL: Finish the plan.
If you are implementing a plan then you must finish the plan.

### Other Constraints
- **Clean up temp files** - Create test files in `/temp` and delete after
- **Don't use here documents** - Write files directly with file tools
- **Comments explain "why"** - Not "what" the code does
- **Relative paths** - Always use relative paths for file operations
- **LF line endings** - All source files use Unix-style line endings
- Don't create new documentation files unless instructed
- Do not edit test fragments (in `/specs/fragments/`). These are generated from the spec files, edit the spec file.
- Do not use "cmd /c" to run commands
- There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

## Development Notes
- Build: `cd maxon-sharp && dotnet build`
- The compiler executable is at `maxon-sharp/bin/Debug/net8.0/win-x64/maxonsharp.exe`

## Running Tests
- **Spec tests**: `maxonsharp spec-test` - Extracts and runs tests from spec files
- **Unit tests**: `maxonsharp unit-test` - Runs built in unit tests
- **VSCode extension tests**: `cd vscode-extension && npm test` - Runs extension tests

## Compiler Commands
- `maxonsharp compile <file>` - Compile a single .maxon file
- `maxonsharp build [<directory>]` - Build a project
- `maxonsharp run <file|directory>` - Compile and run
- `maxonsharp spec-test [--filter=PATTERN]` - Run spec tests

## Logging
Use `--log=LEVEL` or `--log=CATEGORY:LEVEL` for debugging:
- Levels: none, error, info, debug, trace
- Categories: compiler, lexer, parser, semantic, mlir, regalloc, codegen, pe, testing

## Writing VSCode Extension Tests
- Do not set timeouts
- Do not use arbitrary delays, wait for what you are expecting

## Debugging
When debugging an issue prefer adding more debug printing so you can see what is happening instead of trying to think through it.
llvm-objdump is available in llvm-project/bin
