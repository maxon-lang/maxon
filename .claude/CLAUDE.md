# Maxon Compiler Development

> **Agent Skill**: See `.claude/skills/maxon-compiler/` for detailed build, debugging, and development guidance.

## Project Overview

Maxon is a statically-typed programming language with a custom native x86-64 backend:

- **Compiler** (`maxon-bin/`) - C++ compiler generating native x86-64 via MIR
- **Language Server** (`lsp-server/`) - C++ LSP for IDE integration
- **VS Code Extension** (`vscode-extension/`) - Syntax highlighting, debugging
- **Runtime Library** (`maxon-runtime/`) - Platform-specific runtime (no C runtime)
- **Specs & Tests** (`specs/`, `language-tests/`) - Spec-driven development

## Compilation Pipeline

```
Source (.maxon) → Lexer → Parser → AST → Semantic Analyzer → MIR → Optimizer → x86 CodeGen → Executable
```

## Quick Reference

| Task | Command |
|------|---------|
| Build everything | `make all` |
| Build compiler only | `make compiler` |
| Run all tests | `make test` |
| Compile and run | `maxon file.maxon` |
| Compile with MIR output | `maxon compile file.maxon --emit-ir` |
| Verbose compilation | `maxon compile file.maxon -vvv` |

## Language Reference

**Read `docs/LANGUAGE_REFERENCE.md`** for complete Maxon syntax.

Quick reminders:
- Block identifiers must match: `if x 'label' ... end 'label'`
- Keywords: `and`, `or`, `not` (not `&&`, `||`, `!`)
- Single `=` for equality (not `==`)
- Float literals require decimal: `5.0` not `5`

## Constraints

1. **No C Runtime** - Use `maxon-runtime/` for all system functionality
2. **Clean up temp files** - Create test files in `/temp` and delete after
3. **Don't use here documents** - Write files directly
4. **Comments explain "why"** - Not "what" the code does
5. **Absolute paths** - Always use absolute paths for file operations
