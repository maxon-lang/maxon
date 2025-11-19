# GitHub Copilot Instructions for Maxon Language Project

## Key Workflows

### Testing
- Use `maxon <file>` to compile and run in one step (no temp files)
- Create test files in `/temp` and clean up afterwards
- `maxon regen-fragments` regenerates IR and metadata for `.test` files
- `maxon test-fragments` runs all language tests (add `--verbose` for details)

### Adding Language Features
1. Lexer/parser/codegen changes (search codebase for patterns)
2. `make compiler`
3. Create `.test` file with just source code in `language-tests/fragments/`
4. `maxon regen-fragments` to generate IR/metadata
5. Add docs to `docs/Content/*.md` if user-facing, then `make docs`

### Adding Keywords
Update: `TokenType` enum, `Lexer::readIdentifier()` map, parser logic, AST if needed, codegen, TextMate grammar

### Adding LSP Features
Add handler in `LspServer`, register in `JsonRpcHandler`, implement in `Analyzer`, update capabilities

## Build System
- Use top-level `Makefile` for all builds (see `make help`)
- `make all` - Full build (default)
- `make compiler` / `make lsp` / `make extension` - Component builds
- `maxon.exe` is in PATH (no path prefix needed)

## Runtime Library
- `maxon-runtime/runtime.obj` provides functions for LLVM intrinsics (e.g., `llvm.memset` → `memset`)
- Auto-linked with all programs
- Add functions in `runtime.ll`, then `make runtime`
- After build it is copied to 'bin' folder so maxon.exe can find it

## Guidelines
- Don't create new docs unless instructed
- Comments explain *why*, not *what*
