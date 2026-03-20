# Self-Hosted Maxon Compiler Architecture

The self-hosted Maxon compiler is written in Maxon itself (~7,700 lines across 34 files). It compiles Maxon source code to native executables for 4 target combinations (x86_64/aarch64 × windows/linux) using an MLIR-inspired pipeline with incremental compilation support.

## Project Structure

```
maxon-selfhosted/
  Main.maxon                           Entry point, CLI dispatch
  build.maxon                          Project marker file

  Compiler/
    Compiler.maxon                     Top-level compile() orchestration
    Lexer.maxon                        Table-driven DFA tokenizer
    Parser.maxon                       Recursive descent parser → MaxonOp[]
    SemanticCheck.maxon                Semantic validation pass
    MlirPipeline.maxon                 SSA value ID management
    ErrorCode.maxon                    Error codes and CompileError union
    Logger.maxon                       Category-based logging system
    Project.maxon                      Project type (central compilation context)
    ProjectManager.maxon               Multi-project management (for LSP)
    Queries.maxon                      Typed query functions (memoized pipeline stages)
    QueryDatabase.maxon                Query database types and caches
    QueryEngine.maxon                  Dependency tracking, cache validation
    StdlibLoader.maxon                 Stdlib parsing and caching
    Target.maxon                       Target (CpuArch × Os)

    MLIR/
      Dialects/
        MaxonDialect.maxon             MaxonOp union (high-level IR, 37 variants)
        StandardDialect.maxon          StdOp union (target-independent IR, 36 variants)
      Conversion/
        MaxonToStandardConversion.maxon  MaxonOp → StdOp lowering

    Targets/
      BackendDispatch.maxon            CPU/OS dispatch for code emission
      Shared/
        BinaryHelpers.maxon            Byte-writing utilities
        CodeResult.maxon               CodeResult, Relocation types
        OsDescriptor.maxon             OS abstraction (exit/write strategies)
        StdOpHelpers.maxon             Shared helpers for Std→target conversion
      X86/
        X86Dialect.maxon               X86Op union, X86Register union
        StandardToX86Conversion.maxon  StdOp → X86Op lowering
        X86CodeEmitter.maxon           X86Op → machine code bytes
      Arm64/
        Arm64Dialect.maxon             Arm64Op union, Arm64Register union
        StandardToArm64Conversion.maxon StdOp → Arm64Op lowering
        Arm64CodeEmitter.maxon         Arm64Op → machine code bytes
      Windows/
        PeWriter.maxon                 PE64 executable writer
      Linux/
        ElfWriter.maxon                ELF executable writer

  Testing/
    SpecParser.maxon                   Spec file parser, types, and frontmatter/directive extraction
    FragmentGenerator.maxon            Fragment file generation, writing, and parsing
    SpecTestRunner.maxon               Fragment-based test runner pipeline
    IncrementalTestRunner.maxon        Incremental compilation cache tests
```

## Compilation Pipeline

The compiler transforms source code through a series of dialects, each lowering to a more concrete representation:

```
Source (.maxon files)
    │
    ▼
  Lexer          tokenize()           Source → Token[]
    │
    ▼
  Parser         parse()              Token[] → MaxonOp[]
    │
    ▼
  Semantic       runSemanticChecks()   Validates MaxonOp[]
    │
    ▼
  MaxonToStd     lowerMaxonOpsToStd()  MaxonOp[] → StdOp[]
    │
    ▼
  StdToTarget    lowerStdOpsToX86()    StdOp[] → X86Op[] (or Arm64Op[])
    │
    ▼
  Emitter        emitX86()            X86Op[] → machine code bytes
    │
    ▼
  Exe Writer     writePe() / writeElf()  bytes → executable file
```

The pipeline is driven by a query system (see "Incremental Compilation" below). The top-level call chain:

```
compile(path, target)
  → compileProject(project)
    → loadSourceFiles(project, path)
    → queryCodeResult(project)           triggers entire pipeline via queries
      → queryAllStdOps(project)
        → queryAllMaxonOps(project)
          → getStdlibOps(project)        parse stdlib once, cache globally
          → for each source file:
            → queryParseOps(project, path)
              → queryTokens(project, path)
                → tokenize(content)
              → parse(project, tokens, filePath)
          → merge all MaxonOp arrays
        → runSemanticChecks(maxonOps)
        → lowerMaxonOpsToStd(maxonOps)
      → emitBackend(stdOps, target)
        → lowerStdOpsToX86(stdOps)       (or Arm64)
        → emitX86(ops)                   (or Arm64)
    → writeExecutable(outputPath, codeResult, target)
```

### Key Design Decision: No AST

The parser emits a flat array of `MaxonOp` operations directly — there is no intermediate AST tree. Structural information like function boundaries and scoping is encoded as marker ops (`funcBegin`/`funcEnd`, `label`, `condBr`/`br`).

## Dialects

Each dialect is a Maxon `union` type. Operations carry SSA `ValueId` references for data flow.

### Maxon Dialect (MaxonDialect.maxon)

High-level operations emitted directly by the parser. 37 variants:

| Category | Operations |
|----------|-----------|
| Literals | `literal`, `floatLiteral` |
| Arithmetic | `add`, `sub`, `mul`, `div`, `mod`, `neg` |
| Bitwise | `bitNot`, `bitAnd`, `bitOr`, `bitXor`, `shl`, `shr` |
| Comparison | `cmpEq`, `cmpNe`, `cmpLt`, `cmpLe`, `cmpGt`, `cmpGe`, `floatCmpEq` |
| Variables | `varDecl`, `varLoad`, `varStore` |
| Control flow | `condBr`, `br`, `label` |
| Functions | `funcBegin`, `funcEnd`, `call`, `returnOp` |
| I/O | `printLiteral`, `printInt` |

### Standard Dialect (StandardDialect.maxon)

Target-independent lowered form. 36 variants. Nearly 1:1 with MaxonOp but with explicit type suffixes (e.g., `addI64` instead of `add`, `constI64` instead of `literal`, `storeSlot` instead of `varDecl`).

### Target Dialects (X86Dialect.maxon, Arm64Dialect.maxon)

Machine-level operations for each CPU target:

- **X86Op** (28 variants): `prologue`, `epilogue`, `movRegImm`, `movSlot`, `loadSlot`, `addRegReg`, `imulRegReg`, `idivRcx`, `cmpRaxRcx`, `condJump`, `jmp`, `label`, `callDirect`, etc.
- **Arm64Op** (27 variants): `prologue`, `epilogue`, `movImm`, `strToSlot`, `ldrFromSlot`, `addRegs`, `subRegs`, `mulRegs`, `sdivRegs`, `cmpRegs`, `condBranch`, `branch`, `branchLink`, etc.

## Key Data Structures

### Project (Project.maxon)

The central compilation context. Contains the query database, SSA counter, parser symbol table state, compile-time string constants, root path, and compilation target.

### QueryDatabase (QueryDatabase.maxon)

Incremental compilation state: current revision, source file entries, per-file caches (tokens, parse), whole-program caches (MaxonOps, StdOps, code), dependency edges, and hit/miss counters.

### Target (Target.maxon)

`cpu: CpuArch` (x86_64 | aarch64) × `os: Os` (windows | linux).

### OsDescriptor (OsDescriptor.maxon)

Pure data describing OS interaction strategies: how to call exit (syscall vs IAT call) and how to call write (Linux syscall vs Windows API). CPU emitters consume this to generate platform-specific code without knowing OS details directly.

### CodeResult (CodeResult.maxon)

Output of code generation: raw machine code bytes, the offset of the main-call fixup, and a list of relocations.

## Incremental Compilation

The compiler uses a Salsa/Rust-analyzer-inspired query system for incremental recompilation:

- Each pipeline stage is a **memoized query** (`queryTokens`, `queryParseOps`, `queryAllMaxonOps`, `queryAllStdOps`, `queryCodeResult`)
- The `QueryDatabase` stores memo entries with `computedAt` and `verifiedAt` revisions
- The `QueryEngine` tracks dependencies between queries and validates caches against upstream changes
- When a source file changes, only affected downstream queries recompute

This is validated by `IncrementalTestRunner.maxon` which checks:
1. Fresh compilation: all cache misses
2. Recompile same source: all cache hits
3. Modified source: selective recomputation

## Code Generation Strategy

The current codegen uses a "spill everything to stack" strategy — there is no register allocator:

1. Every SSA value gets a dedicated stack slot
2. Binary operations load left operand to RAX/X0 and right to RCX/X1
3. Compute the operation
4. Store the result back to a new stack slot

The `computeStackSize` function scans ops within each function to determine the total stack frame needed.

### OS Abstraction (OsDescriptor Pattern)

CPU and OS concerns are cleanly separated. The `OsDescriptor` struct provides pure data describing how to perform OS interactions:

- **ExitStrategy**: `syscall(number)` for Linux or `iatCall(slotIndex)` for Windows
- **WriteStrategy**: `syscall(writeNumber, fd)` for Linux or `winApi(getHandleSlot, handleConstant, writeFileSlot)` for Windows

CPU emitters consume this data to emit platform-specific instruction sequences, avoiding a 2×2 matrix of emitter code.

### Executable Writers

- **PeWriter.maxon**: Produces minimal PE64 executables with `.text` and `.idata` sections. Imports `ExitProcess`, `GetStdHandle`, `WriteFile` from `kernel32.dll`.
- **ElfWriter.maxon**: Produces ELF executables using syscalls directly (no dynamic linking).

---

## How to Extend the Compiler

### Adding a New Operator or Expression

Example: adding a modulo operator `%`.

**1. Lexer (Lexer.maxon)**

Add the token to `TokenKind` if it doesn't exist. For single-character tokens, add entries to the DFA tables (`charClassTable`, `transitionTable`, `actionTable`). For keyword operators (like `mod`), add an entry to `keywordMap`.

**2. Parser (Parser.maxon)**

Add parsing logic at the appropriate precedence level. The parser uses recursive descent with these precedence levels (lowest to highest):

1. `parseBitwiseOrExpr` — `or`
2. `parseBitwiseXorExpr` — `xor`
3. `parseBitwiseAndExpr` — `and`
4. `parseShiftExpr` — `shl`, `shr`
5. `parseComparisonExpr` — `==`, `!=`, `<`, `<=`, `>`, `>=`
6. `parseAddExpr` — `+`, `-`
7. `parseMulExpr` — `*`, `/`, `mod`
8. `parseUnaryExpr` — `not`, `-`
9. `parsePrimaryExpr` — literals, identifiers, calls, parens

Each level calls the next-higher precedence level, then loops checking for its operators. Emit the corresponding `MaxonOp`.

**3. Maxon Dialect (MaxonDialect.maxon)**

Add a new variant to the `MaxonOp` union:

```maxon
modOp(resultId: ValueId, left: ValueId, right: ValueId)
```

**4. Standard Dialect (StandardDialect.maxon)**

Add a corresponding `StdOp` variant with explicit types:

```maxon
modI64(resultId: ValueId, left: ValueId, right: ValueId)
```

**5. MaxonToStandard Conversion (MaxonToStandardConversion.maxon)**

Add a case in `lowerMaxonOpsToStd` mapping `MaxonOp.modOp` → `StdOp.modI64`.

**6. StandardToX86 Conversion (StandardToX86Conversion.maxon)**

Add a case in `lowerStdOpsToX86` that:
- Loads left operand to RAX
- Loads right operand to RCX
- Emits the appropriate X86 ops (e.g., `idivRcx` which puts remainder in RDX)
- Stores the result from the appropriate register

**7. StandardToArm64 Conversion (StandardToArm64Conversion.maxon)**

Same pattern for ARM64 using the appropriate instructions.

**8. X86/Arm64 Code Emitter**

If you added new target dialect ops, add machine code emission for them in `X86CodeEmitter.maxon` or `Arm64CodeEmitter.maxon`.

**9. Tests**

Add test cases to the relevant spec file in `/specs/`. Then add the spec name to the whitelist in `Testing/SpecTestRunner.maxon` if it's a new spec.

### Adding a New Statement

Example: adding a `while` loop.

1. **Parser**: Add a case in `parseStatement` that recognizes the keyword, parses the condition and body, and emits MaxonOps using labels and branches for control flow:
   - `label(loopStart)`
   - Parse condition → `condBr(cond, bodyLabel, exitLabel)`
   - `label(bodyLabel)`, parse body
   - `br(loopStart)`
   - `label(exitLabel)`

2. **Dialect ops**: If `while` can be expressed using existing ops (`label`, `condBr`, `br`), no new dialect variants are needed. The parser desugars structured control flow into these primitives.

3. **Everything downstream** (MaxonToStd, StdToTarget, emitter) already handles the underlying ops.

### Adding a New Type

1. **Parser**: Update type parsing to recognize the new type name
2. **Maxon Dialect**: Add type-specific op variants if the type needs different operations (e.g., `floatLiteral` vs `literal`)
3. **Standard Dialect**: Add typed variants (e.g., `addF64` alongside `addI64`)
4. **Conversion passes**: Add cases for the new typed ops
5. **Target emitters**: Add machine code sequences for the new typed operations

### Adding a New Target

1. Create a new directory under `Targets/` (e.g., `Targets/Riscv/`)
2. Define the target dialect: `RiscvDialect.maxon` with a `RiscvOp` union and `RiscvRegister` union
3. Write the lowering pass: `StandardToRiscvConversion.maxon` mapping `StdOp` → `RiscvOp`
4. Write the code emitter: `RiscvCodeEmitter.maxon` converting `RiscvOp` → machine code bytes
5. Add the CPU variant to `CpuArch` in `Target.maxon`
6. Add dispatch cases in `BackendDispatch.maxon`
7. If targeting a new OS, add an executable writer under `Targets/<Os>/`
8. Add an `OsDescriptor` for the new CPU×OS combination

### Adding a New Semantic Check

1. **SemanticCheck.maxon**: Add validation logic in `runSemanticChecks`. Iterate through the `MaxonOp` array matching on relevant op variants.
2. **ErrorCode.maxon**: Add a new error code and add a variant to the `CompileError` union if needed.

### Adding a Spec Test to the Self-Hosted Runner

The self-hosted compiler runs a subset of the spec tests. To enable a new spec:

1. Open `Testing/SpecTestRunner.maxon`
2. Find the whitelist array where spec names are listed
3. Add the new spec name (e.g., `push(whitelistedSpecs, "my-new-feature")`)
4. Run `./maxon-selfhosted/Main.exe spec-test --filter=my-new-feature` to verify

## Building and Testing

```bash
# Build the self-hosted compiler (using the C# compiler)
maxon build maxon-selfhosted

# Run all whitelisted spec tests
./maxon-selfhosted/Main.exe spec-test

# Run filtered spec tests
./maxon-selfhosted/Main.exe spec-test --filter=arithmetic

# Cross-target testing
./maxon-selfhosted/Main.exe spec-test --target=all
```

## Known Constraints and Gotchas

- **ByteArray comparison**: Identifiers and keywords are compared as `ByteArray` using element-wise `bytesEqual()` because string comparison is pointer-based.
- **Match exhaustiveness**: Maxon's `match` on unions requires exhaustive case listing — every variant must be listed even if most are no-ops. This makes semantic check code verbose.
- **Single-line match arms**: Match arms can only contain a single expression/statement.
