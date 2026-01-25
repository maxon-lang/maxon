# Maxon-Sharp Compiler Architecture

An MLIR-inspired compiler for the Maxon language, generating native x86-64 Windows executables.

## Compilation Pipeline

```
Source → Lexer → Parser → Semantic Analysis → MLIR Pipeline → Code Emission → PE Writer → Executable
```

### Stage 1: Lexer
Tokenizes Maxon source code into a token stream (keywords, literals, operators, punctuation).

### Stage 2: Parser  
Builds an AST from tokens. Parses:
- Functions, types, enums, interfaces, extensions
- Global constants and variables
- Type aliases

### Stage 3: Semantic Analyzer
- Validates `main` function exists and returns `int`
- Runs mutation analysis for ownership tracking

### Stage 4: MLIR Pipeline
The core of the compiler. Progressively lowers the AST through dialect conversions:

```
AST → Maxon Dialect → Standard Dialects → X86 Dialect
```

#### Dialects
| Dialect | Purpose |
|---------|---------|
| **Maxon** | High-level Maxon-specific operations |
| **Arith** | Arithmetic operations |
| **MemRef** | Memory references and operations |
| **Func** | Function definitions and calls |
| **Cf** | Control flow (branches, jumps) |
| **X86** | Low-level x86-64 instructions |

#### Optimization Passes

**Maxon Dialect Passes:**
- Borrow Checker
- Dead Function Elimination

**Standard Dialect Passes:**
- Mem2Reg (promote memory to registers)
- Constant Folding
- Dead Code Elimination
- Dead Store Elimination

**X86 Dialect Passes:**
- Function Frame Insertion (prologue/epilogue)
- Register Allocation
- Peephole Optimization

### Stage 5: Code Emitter
Converts X86 dialect operations to machine code bytes. Handles:
- Global variable definitions in data section
- Function emission (entry point first)
- Label resolution
- Global reference patching

### Stage 6: PE Writer
Generates Windows PE32+ executables with:
- `.text` section for code
- `.data` section for globals (when needed)
- Console subsystem targeting

## Key Design Principles

1. **No LLVM dependency** - Custom native code generation
2. **MLIR-inspired architecture** - Progressive lowering through dialects
3. **Pattern-based conversion** - Clean separation between dialect levels
4. **Pass-based optimization** - Modular, composable optimization passes
