# GitHub Copilot Instructions for Maxon Language Project

## Key Workflows

### Spec-Driven Development (PRIMARY WORKFLOW)
- **All language features must have a spec file in `specs/`** - single source of truth
- Each spec contains: Developer Notes, Documentation (user-facing), and Tests
- Spec format defined in `specs/README.md`

**Adding a New Feature:**
1. Create `specs/feature-name.md` with YAML frontmatter, notes, docs, and tests
2. `maxon extract-specs` - Extract test fragments from spec
3. `maxon regen-fragments` - Generate IR and metadata for tests  
4. Implement feature in lexer/parser/codegen until tests pass
5. `make docs` - Generate HTML documentation from spec

**Modifying Existing Feature:**
1. Edit the spec file in `specs/`
2. `make test` - Automatically extracts specs, regenerates, and runs tests
3. Update implementation if needed

**Validation:**
- `make validate-specs` - Check for orphaned fragments not in any spec
- All fragments in `language-tests/fragments/` should be generated from specs

### Testing
- Use `maxon <file>` to compile and run in one step (no temp files)
- Create test files in `/temp` and clean up afterwards
- `maxon test-fragments` runs all language tests (add `--verbose` for details)

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
- NEVER resort to calling the c runtime library, we do not use it!

## Guidelines
- Don't create new docs unless instructed
- Comments explain *why*, not *what*

## Writing VSCode Extension Tests
- Do not set timeouts
- Do not use arbitrary delays, wait for what you are expecting
