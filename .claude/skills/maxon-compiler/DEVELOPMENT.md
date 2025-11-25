# Development Patterns

## Spec-Driven Development

**All language features must have a spec file in `specs/`** - this is the single source of truth.

### Workflow

1. **Create/Edit Spec** - `specs/<feature-name>.md`
2. **Extract Tests** - `maxon extract-specs`
3. **Regenerate IR** - `maxon regen-fragments` (preserves expected results)
4. **Run Tests** - `maxon test-fragments`
5. **Generate Docs** - `make docs`

Or simply: `make test` (runs the full pipeline)

### Spec File Format

```markdown
---
feature: feature-name
status: stable
keywords: [keyword1, keyword2]
category: category-name
---

## Developer Notes
Implementation details for compiler developers...

## Documentation
User-facing documentation with examples...

## Tests
<!-- test: test-name -->
```maxon
function main() int
    print(42)
    return 0
end 'main'
```
```output
ExitCode: 0
Stdout: 42
```
```

### Test Fragment Format

Files in `language-tests/fragments/` have 4 sections separated by `---`:
1. Source code
2. Optimized IR (or `N/A`)
3. Debug IR (or `N/A`)
4. Metadata (ExitCode, Stdout, Stderr, MaxoncStderr)

---

## Adding a New Keyword

1. Add to `TokenType` enum in `lexer.h`
2. Add keyword mapping in `Lexer::readIdentifier()` in `lexer.cpp`
3. Add parser logic in `parser.cpp`
4. Add AST node if needed in `ast.h`
5. Add code generation in `codegen_mir.cpp`
6. Update TextMate grammar in `vscode-extension/syntaxes/maxon.tmLanguage.json`

---

## Adding an LSP Feature

1. Add handler method in `LspServer` class
2. Register handler in `JsonRpcHandler`
3. Implement analysis in `Analyzer` class
4. Update server capabilities in initialization

---

## Adding a Runtime Function

1. Add function to `maxon-runtime/runtime.mir` (platform-independent)
2. For platform-specific, add to `runtime_windows.mir` or `runtime_linux.mir`
3. The runtime is automatically merged with user code during compilation
4. Declare in compiler's known externals if needed

---

## Key Source Files

| File/Directory | Purpose |
|----------------|---------|
| `maxon-bin/lexer.cpp/h` | Tokenization |
| `maxon-bin/parser.cpp/h` | AST generation |
| `maxon-bin/semantic_analyzer.cpp/h` | Type checking, scope analysis |
| `maxon-bin/codegen_mir.cpp/h` | AST to MIR code generation |
| `maxon-bin/mir/` | MIR data structures, parser, optimizer |
| `maxon-bin/backend/x86_codegen.cpp/h` | x86-64 machine code generation |
| `maxon-bin/backend/regalloc.cpp/h` | Linear-scan register allocation |
| `maxon-bin/backend/pe_writer.cpp/h` | Windows PE executable generation |
| `maxon-bin/backend/elf_writer.cpp/h` | Linux ELF executable generation |
| `lsp-server/src/` | LSP implementation |
| `maxon-runtime/*.mir` | Runtime library (MIR format) |
