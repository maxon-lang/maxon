# Maxon Compiler Development

## Project Overview

Maxon is a statically-typed programming language with a custom native x86-64 backend (no LLVM dependency for code generation):

- **Compiler** (`maxon-bin/`) - Zig compiler generating native x86-64 via IR
- **VS Code Extension** (`vscode-extension/`) - Syntax highlighting, debugging
- **Standard Library** (`stdlib/`) - Maxon standard library modules
- **Specs & Tests** (`maxon-bin/specs/`) - Spec-driven development with test fragments

## Documentation

- **`docs/LANGUAGE_REFERENCE.md`** - Complete Maxon language syntax and semantics
- **`docs/SPECS.md`** - Spec file format, workflow, and how to write specs

## Spec-Driven Development

**All language features must have a spec file in `maxon-bin/specs/`** - this is the single source of truth.

Each spec contains:
1. **Developer Notes** - Implementation details for maintainers
2. **Documentation** - User-facing docs
3. **Tests** - Test cases (extracted to `maxon-bin/specs/fragments/`)

Workflow for new features:
1. Create `maxon-bin/specs/feature-name.md` with frontmatter, notes, docs, and tests
2. Run `maxon test` to extract and run test fragments
3. Implement until tests pass

See `docs/SPECS.md` for the complete spec file format and detailed workflow.

## Constraints

### CRITICAL: No Git Commands
**NEVER use git commands.** This includes git status, git diff, git add, git commit, git log, git checkout, or any other git command. Do not suggest git commands to the user. If you need to understand changes, read the files directly. This constraint applies at all times, regardless of context or how long the session has been running.

### Other Constraints
- **Clean up temp files** - Create test files in `/temp` and delete after
- **Don't use here documents** - Write files directly with file tools
- **Comments explain "why"** - Not "what" the code does
- **Relative paths** - Always use relative paths for file operations
- **LF line endings** - All source files use Unix-style line endings
- Don't create new documentation files unless instructed
- Do not edit test fragments (in `maxon-bin/specs/fragments/`). These are generated from the spec files, edit the spec file.

## Development Notes
- Build system uses Zig build
- All tests must pass before commits to main

## Writing VSCode Extension Tests
- Do not set timeouts
- Do not use arbitrary delays, wait for what you are expecting
