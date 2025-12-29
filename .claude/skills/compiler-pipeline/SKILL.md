---
name: compiler-pipeline
description: Understanding the Maxon compiler pipeline stages and how to trace issues through them. Apply when debugging compiler issues, implementing features, or understanding code flow.
---

# Maxon Compiler Pipeline

## Directory Structure

```
maxon/
├── maxon-bin/           # Compiler (Zig)
│   ├── src/
│   │   ├── main.zig     # Entry point
│   │   ├── compiler/    # Compiler pipeline
│   │   │   ├── 0-compiler.zig    # Main compiler orchestration
│   │   │   ├── 1-lexer.zig       # Tokenization
│   │   │   ├── 2-parser.zig      # Recursive descent parser
│   │   │   ├── 3-mutation_analysis.zig  # Ownership/mutation analysis
│   │   │   ├── 4-ast_to_ir.zig   # AST to IR translation
│   │   │   ├── 5-optimizer.zig   # Optimization passes
│   │   │   ├── 6-codegen.zig     # IR to x86-64 machine code
│   │   │   ├── 7-pe.zig          # Windows PE executable writer
│   │   │   ├── ast.zig           # AST definitions
│   │   │   ├── ir.zig            # IR definitions
│   │   │   └── x86.zig           # x86 instruction encoding
│   │   └── testing/     # Test infrastructure
│   ├── specs/           # Language specifications (source of truth)
│   │   └── fragments/   # Generated test fragments
│   └── build.zig        # Zig build configuration
├── stdlib/              # Standard library (Maxon source)
├── vscode-extension/    # VS Code extension (TypeScript)
└── docs/                # Documentation
```

## Pipeline Stages

```
Source Code (.maxon)
        ↓
   1. Lexer (1-lexer.zig)
        ↓ tokens
   2. Parser (2-parser.zig)
        ↓ AST
   3. Mutation Analysis (3-mutation_analysis.zig)
        ↓ annotated AST
   4. AST to IR (4-ast_to_ir.zig)
        ↓ IR
   5. Optimizer (5-optimizer.zig)
        ↓ optimized IR
   6. Codegen (6-codegen.zig)
        ↓ x86-64 machine code
   7. PE Writer (7-pe.zig)
        ↓
   Executable (.exe)
```

## Key Files

| Stage | File | Purpose |
|-------|------|---------|
| Entry | `main.zig` | CLI and orchestration |
| Orchestration | `0-compiler.zig` | Pipeline coordination |
| Lexer | `1-lexer.zig` | Tokenization |
| Parser | `2-parser.zig` | Recursive descent parsing |
| Mutation | `3-mutation_analysis.zig` | Ownership/mutation analysis |
| IR Gen | `4-ast_to_ir.zig` | AST to IR translation |
| Optimizer | `5-optimizer.zig` | Optimization passes |
| Codegen | `6-codegen.zig` | x86-64 code generation |
| PE Writer | `7-pe.zig` | Windows executable format |
| AST Defs | `ast.zig` | AST node definitions |
| IR Defs | `ir.zig` | IR instruction definitions |
| x86 | `x86.zig` | x86 instruction encoding |

## Build Commands

| Task | Command |
|------|---------|
| Build compiler | `cd maxon-bin && zig build` |
| Run all tests | `cd maxon-bin && zig build test` |
| Compile and run | `./bin/maxon run file.maxon` |
| Compile only | `./bin/maxon compile file.maxon` |
| Compile with IR output | `./bin/maxon compile file.maxon --emit-ir` |

## Debugging Tips

### Viewing IR Output
```bash
./bin/maxon compile file.maxon --emit-ir
```

### Tracing Issues

1. **Syntax errors** → Check lexer/parser
2. **Type errors** → Check mutation_analysis or ast_to_ir
3. **Wrong output** → Check codegen or optimizer
4. **Crashes** → Check PE writer or codegen

### Common Debugging Flow

1. Create minimal repro case
2. Compile with `--emit-ir` to see IR
3. Identify which stage produces wrong output
4. Add debug prints in that stage
5. Trace the issue to root cause

## Adding New Features

1. **New syntax** → Lexer (tokens) → Parser (AST nodes)
2. **New semantics** → Mutation analysis → AST-to-IR
3. **New operations** → IR definitions → Codegen
4. **Optimizations** → Optimizer passes
