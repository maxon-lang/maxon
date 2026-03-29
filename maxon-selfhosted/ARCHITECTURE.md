# Self-Hosted Maxon Compiler Architecture

The self-hosted Maxon compiler is written in Maxon itself (~15,800 lines across 54 files). It compiles Maxon source code to native executables for 6 target combinations (x86_64/aarch64 x windows/linux/macos) using an MLIR-inspired multi-dialect pipeline with incremental compilation support.

## Project Structure

```
maxon-selfhosted/
  Main.maxon                           Entry point, CLI dispatch
  build.maxon                          Project marker file

  Compiler/
    Compiler.maxon                     Top-level compile() orchestration
    Lexer.maxon                        Table-driven DFA tokenizer
    Parser.maxon                       Recursive descent parser -> MlirModule (maxhl ops)
    SemanticCheck.maxon                Semantic validation pass
    MlirPipeline.maxon                 SSA value ID management
    ErrorCode.maxon                    Error codes and CompileError union
    Logger.maxon                       Category-based logging system
    MaxonArgs.maxon                    CLI argument parsing
    Project.maxon                      Project type (central compilation context)
    ProjectManager.maxon               Multi-project management (for LSP)
    Queries.maxon                      Typed query functions (memoized pipeline stages)
    QueryDatabase.maxon                Query database types and caches
    QueryEngine.maxon                  Dependency tracking, cache validation
    StdlibLoader.maxon                 Stdlib parsing and caching
    Target.maxon                       Target (CpuArch x Os)

    MLIR/
      Core/
        MlirOp.maxon                   MlirOp wrapper union (9 dialect variants)
        MlirModule.maxon               Top-level module container
        MlirFunction.maxon             Named function with body region
        MlirBlock.maxon                Basic block within a region
        MlirRegion.maxon               Ordered list of blocks
        MlirPrinter.maxon              MLIR text format printer
      Dialects/
        MaxHLDialect.maxon             MaxHLOp union (high-level IR, 16 variants)
        ArithDialect.maxon             ArithOp union (typed arithmetic, const/unary/binary)
        CfDialect.maxon                CfOp union (block terminators: br, condBr)
        FuncDialect.maxon              FuncOp union (call, ret, param, funcRef, etc.)
        MemRefDialect.maxon            MemRefOp union (storeSlot, loadSlot)
        RuntimeDialect.maxon           RuntimeOp union (printLiteral, printInt)
        SysDialect.maxon               SysOp union (syscall, iatCall, osExit, osWrite, etc.)
      Conversion/
        MaxHLToMidConversion.maxon     MaxHLOp -> arith/cf/func/memref/runtime lowering
        Mem2RegPass.maxon              Promote memref slots to SSA values
        DeadFunctionElimination.maxon  Remove unreachable functions (unused stdlib)

    Runtime/
      runtime.mid                      Runtime functions as mid-level MLIR text
      MidLevelParser.maxon             Parses .mid text into MlirFunction objects
      RuntimeFunctions.maxon           Global data table for string literals

    Targets/
      BackendDispatch.maxon            CPU/OS dispatch, runtime augmentation, target lowering
      Shared/
        BinaryHelpers.maxon            Byte-writing utilities
        CodeResult.maxon               CodeResult, Relocation types
        OsDescriptor.maxon             OS abstraction (exit/write strategies)
        RegisterManager.maxon          Cross-platform greedy linear-scan register allocator
        StdOpHelpers.maxon             Shared helpers for mid->target conversion
      X86/
        MaxX64Dialect.maxon            MaxX64Op union (85 variants), X64Register enum
        MidToMaxX64Conversion.maxon    Mid-level -> MaxX64Op lowering + register allocation
        MaxX64CodeEmitter.maxon        MaxX64Op -> machine code bytes
      Arm64/
        MaxArm64Dialect.maxon          MaxArm64Op union (75 variants), MaxArm64Register enum
        MidToMaxArm64Conversion.maxon  Mid-level -> MaxArm64Op lowering + register allocation
        MaxArm64CodeEmitter.maxon      MaxArm64Op -> machine code bytes
      Windows/
        PeWriter.maxon                 PE64 executable writer
      Linux/
        ElfWriter.maxon                ELF executable writer
      Macos/
        MachOWriter.maxon              Mach-O executable writer

  Testing/
    SpecParser.maxon                   Spec file parser, types, and frontmatter/directive extraction
    FragmentGenerator.maxon            Fragment file generation, writing, and parsing
    SpecTestRunner.maxon               Fragment-based test runner pipeline
    IncrementalTestRunner.maxon        Incremental compilation cache tests
```

## Compilation Pipeline

The compiler transforms source code through a multi-dialect IR, where ops from different dialects coexist in the same module and each lowering pass handles the ops it cares about while passing others through.

### Current Flow

```
Source (.maxon files)
    |
    v
  Lexer              tokenize()                Source -> Token[]
    |
    v
  Parser             parse()                   Token[] -> MlirModule (maxhl/func/cf/memref ops)
    |
    v
  Semantic           runSemanticChecks()        Validates MlirModule
    |
    v
  DeadFuncElim       eliminateDeadFunctions()   Removes unreachable functions
    |
    v
  MaxHLToMid         lowerMaxHLToMid()          maxhl -> arith/cf/func/memref/runtime/sys
    |
    v
  Mem2Reg            promoteMem2Reg()           Promotes memref slots to SSA values
    |
    v
  Runtime            augmentWithRuntime()        Prepends runtime functions from runtime.mid
    |
    v
  MidToTarget        lowerMidToMaxX64()          Mid -> MaxX64Op (or MaxArm64Op)
                     (includes register allocation)
    |
    v
  Emitter            emitX86() / emitMaxArm64()  Target ops -> machine code bytes
    |
    v
  Exe Writer         writePe() / writeElf()      bytes -> executable file
                     / writeMachO()
```

The pipeline is driven by a query system (see "Incremental Compilation" below). The top-level call chain:

```
compile(path, target)
  -> compileProject(project)
    -> loadSourceFiles(project, path)
    -> queryCodeResult(project)              triggers entire pipeline via queries
      -> queryAllMid(project)
        -> queryAllModule(project)
          -> getStdlibModule(project)        parse stdlib once, cache globally
          -> for each source file:
            -> queryParseOps(project, path)
              -> queryTokens(project, path)
                -> tokenize(content)
              -> parse(project, tokens, filePath)
          -> merge all MlirModules
        -> runSemanticChecks(project, module)
        -> eliminateDeadFunctions(module)
        -> lowerMaxHLToMid(module)
        -> promoteMem2Reg(midModule)
      -> emitBackend(midModule, target)
        -> augmentWithRuntime(module)        prepend runtime.mid functions
        -> lowerMidToMaxX64(module)          (or MaxArm64)
        -> emitX86(module)                   (or MaxArm64)
    -> writeExecutable(outputPath, codeResult, target)
```

### Key Design Decision: No AST

The parser emits a flat array of `MlirOp` operations directly -- there is no intermediate AST tree. The parser produces an `MlirModule` containing `MlirFunction` objects, each with a body region of `MlirBlock` objects containing `MlirOp` arrays. Control flow is encoded as block terminators (`condBr`/`br`) targeting labeled blocks.

### Key Design Decision: Multi-Dialect IR

Instead of separate IR types per stage (like a `MaxonOp[]` then a `StdOp[]`), the compiler uses a single `MlirOp` wrapper union that dispatches across 9 dialect variants:

```maxon
export union MlirOp
  maxhl(op MaxHLOp)
  arith(op ArithOp)
  cf(op CfOp)
  func(op FuncOp)
  memref(op MemRefOp)
  runtime(op RuntimeOp)
  sys(op SysOp)
  maxx64(op MaxX64Op)
  maxarm64(op MaxArm64Op)
end 'MlirOp'
```

Ops from different dialects coexist in the same block. Each lowering pass handles the ops it cares about and passes others through, progressively replacing higher-level ops with lower-level ones.

## Target 7-Phase Pipeline

The architectural goal is a clean 7-phase pipeline with well-defined dialect lifespans:

```
Phase 1 (Frontend):              Parser -> maxhl/func/cf/memref
Phase 2 (Memory Safety):         convertMaxonToArith -> borrowCheck -> injectDrops
Phase 3 (SSA Construction):      mem2reg (eliminates memref)
Phase 4 (Mid-Level Optimization): canonicalize -> cse -> dce
Phase 5 (System Resolution):     convertMaxonToSysAndRuntime -> lowerABI
Phase 6 (Generic Machine Lower): augmentWithRuntime -> convertToMir
Phase 7 (Target Execution):      convertMirToTarget -> scheduleInstructions
                                  -> allocateRegisters -> insertPrologueEpilogue
```

### Dialect Lifespans

| Dialect | Introduced | Eliminated | Purpose |
|---------|-----------|------------|---------|
| maxhl   | Phase 1   | Phase 5    | Source-level semantics (variables, binary ops, comparisons) |
| memref  | Phase 1   | Phase 3    | Stack slot load/store (eliminated by mem2reg into SSA) |
| cf      | Phase 1   | Phase 6    | Block terminators (br, condBr) |
| func    | Phase 1   | Phase 6    | Function calls, returns, parameters |
| arith   | Phase 2   | Phase 6    | Typed arithmetic (addI64, cmpI64Eq, constF64, etc.) |
| runtime | Phase 2   | Phase 6    | I/O intrinsics (printLiteral, printInt) |
| sys     | Phase 5   | Phase 6    | OS primitives (syscall, iatCall, osExit, osWrite) |
| mir     | Phase 6   | Phase 7    | Generic machine IR (future; not yet implemented) |
| maxx64  | Phase 7   | emission   | x86-64 machine instructions |
| maxarm64| Phase 7   | emission   | ARM64 machine instructions |

Currently the compiler implements Phases 1, 2, 3, and 7 (with 5-6 partially merged into the target lowering passes). Phases 4 and 6 are future work.

## Dialects

Each dialect is a Maxon `union` type (or a union of sub-unions). Operations carry SSA `ValueId` references for data flow.

### MaxHL Dialect (MaxHLDialect.maxon)

High-level operations emitted directly by the parser. 16 variants organized by category:

| Category | Operations |
|----------|-----------|
| Literals | `literal` (i64 or f64 via `isFloat` flag) |
| Variables | `varDecl`, `varLoad`, `varStore` |
| Arithmetic | `binop` (add, sub, mul, div, mod, bitAnd, bitOr, bitXor, shl, shr) |
| Unary | `unop` (neg, bitNot) |
| Float conversions | `truncFloat`, `intToFloat` |
| Comparisons | `cmp` (eq, ne, lt, le, gt, ge; int or float) |
| Reference identity | `refEq` (pointer comparison, optional negate) |
| Control flow | `condBr`, `br` (block terminators) |
| Functions | `call`, `ret` |
| I/O | `printLiteral`, `printInt` |

MaxHL uses enums (`BinOpKind`, `UnaryOpKind`, `CmpPredicate`) to collapse what would be many individual variants into parameterized ops.

### Arith Dialect (ArithDialect.maxon)

Target-independent typed arithmetic. Structured as a union of three sub-unions:

- **ArithConst**: `constI64`, `constF64`
- **ArithUnaryOp**: `negI64`, `bitNotI64`, `fpToSiI64`, `siToFpF64`
- **ArithBinaryOp**: 26 opcodes covering integer arithmetic (`addI64`, `subI64`, `mulI64`, `divI64`, `remI64`), bitwise ops (`andI64`, `orI64`, `xorI64`, `shlI64`, `shrI64`), float arithmetic (`addF64`, `subF64`, `mulF64`, `divF64`), and comparisons (`cmpI64Eq`/`Ne`/`Lt`/`Le`/`Gt`/`Ge`, `cmpF64Eq`/`Ne`/`Lt`/`Le`/`Gt`/`Ge`)

### Cf Dialect (CfDialect.maxon)

Block terminators: `br(target)` and `condBr(condId, thenTarget, elseTarget)`.

### Func Dialect (FuncDialect.maxon)

Function-level operations: `ret`, `call`, `tryCall`, `indirectCall`, `funcRef`, `tailCall`, `param`.

### MemRef Dialect (MemRefDialect.maxon)

Stack slot operations: `storeSlot(slotId, valueId)` and `loadSlot(resultId, slotId)`. Eliminated by the mem2reg pass which promotes slots to SSA values.

### Runtime Dialect (RuntimeDialect.maxon)

I/O intrinsics: `printLiteral(data)` and `printInt(valueId)`. These are lowered to `func.call` ops targeting runtime functions.

### Sys Dialect (SysDialect.maxon)

Low-level OS primitives used by the runtime. 9 variants: `syscall`, `iatCall`, `globalAddr`, `memcpy`, `stackAddr`, `storeByte`, `loadByte`, `osExit`, `osWrite`. These appear only in runtime functions (from `runtime.mid`) and are lowered directly to target machine ops.

### Target Dialects (MaxX64Dialect.maxon, MaxArm64Dialect.maxon)

Machine-level operations for each CPU target:

- **MaxX64Op** (85 variants): GPR ops (`movRegImm`, `addRegReg`, `imulRegReg`, `idivReg`, `cmpRegReg`, `setcc`, `cmov`, ...), XMM float ops (`addXmm`, `subXmm`, `mulXmm`, `divXmm`, `ucomisXmm`, `cvtSi2Float`, ...), memory ops (`storeIndirect`, `loadIndirect`, `globalLoad`, `globalStore`, ...), register allocator ops (`movRegReg`, `spillToStack`, `reloadFromStack`, `xchgRegReg`), control flow (`condJump`, `jmp`, `callDirect`, `callIndirect`), and structural ops (`prologue`, `epilogue`, `ret`).
- **MaxArm64Op** (75 variants): GPR ops (`movImm`, `addRegs`, `subRegs`, `mulRegs`, `sdivRegs`, `cmpRegs`, `cset`, `csel`, ...), FP ops (`fadd`, `fsub`, `fmul`, `fdiv`, `fcmp`, `scvtf`, `fcvtzs`, ...), memory ops (`storeIndirect`, `loadIndirect`, `globalLoad`, `globalStore`, ...), register allocator ops (`movRegReg`, `spillToStack`, `reloadFromStack`), control flow (`condBranch`, `branch`, `branchLink`, `branchLinkReg`), and structural ops (`prologue`, `epilogue`, `ret`).

## Key Data Structures

### MlirModule / MlirFunction / MlirBlock

The IR is organized in a nested structure: `MlirModule` contains `MlirFunction[]`, each function has a `MlirRegion` containing `MlirBlock[]`, and each block contains `MlirOp[]`. This mirrors MLIR's module/function/region/block hierarchy.

### Project (Project.maxon)

The central compilation context. Contains the query database, SSA counter, parser symbol table state, global data table (string literals), root path, and compilation target.

### QueryDatabase (QueryDatabase.maxon)

Incremental compilation state: current revision, source file entries, per-file caches (tokens, parse), whole-program caches (allModule, allMid, code), dependency edges, and hit/miss counters.

### Target (Target.maxon)

`cpu: CpuArch` (x86_64 | aarch64) x `os: Os` (windows | linux | macos).

### OsDescriptor (OsDescriptor.maxon)

Pure data describing OS interaction strategies: how to call exit (syscall vs IAT call) and how to call write (Linux syscall vs Windows API). CPU emitters consume this to generate platform-specific code without knowing OS details directly.

### CodeResult (CodeResult.maxon)

Output of code generation: raw machine code bytes, the offset of the main-call fixup, a list of relocations, and the final target-level MlirModule (for IR inspection).

### GlobalDataTable (RuntimeFunctions.maxon)

Maps names (like `__str_0`) to byte arrays for `.rdata`/`.rodata` sections. Built during MaxHL-to-mid lowering as string literals are encountered.

## Incremental Compilation

The compiler uses a Salsa/Rust-analyzer-inspired query system for incremental recompilation:

- Each pipeline stage is a **memoized query** (`queryTokens`, `queryParseOps`, `queryAllModule`, `queryAllMid`, `queryCodeResult`)
- The `QueryDatabase` stores memo entries with `computedAt` and `verifiedAt` revisions
- The `QueryEngine` tracks dependencies between queries and validates caches against upstream changes
- When a source file changes, only affected downstream queries recompute

This is validated by `IncrementalTestRunner.maxon` which checks:
1. Fresh compilation: all cache misses
2. Recompile same source: all cache hits
3. Modified source: selective recomputation

## Code Generation Strategy

The codegen uses a **greedy linear-scan register allocator** (`RegisterManager.maxon`) that works across both x86-64 and ARM64 targets:

1. The register allocator maintains a `RegState` tracking which SSA values are in which physical registers, with LRU eviction when registers are exhausted
2. Values are assigned to physical registers on first use and kept live as long as possible
3. When all registers are occupied, the least-recently-used value is **spilled to stack** and its register is reclaimed
4. The allocator uses **register hints** to reduce unnecessary moves (e.g., preferring the return register for values about to be returned)
5. **Deferred value materialization**: values that are only consumed by sink ops (return, call arguments) skip eager register allocation
6. **Caller-saved register management**: registers are saved/restored around call sites
7. Separate pools for GPR and FP (XMM/D-register) allocation with independent spill tracking
8. **Cross-block state**: `RegSnapshot` captures register assignments at block boundaries to handle control flow merges

The register allocator is target-independent and dispatches to target-specific ops via the `RegTarget` enum (x64, arm64). Each target's conversion pass (`MidToMaxX64Conversion`, `MidToMaxArm64Conversion`) integrates register allocation during lowering.

### OS Abstraction (OsDescriptor Pattern)

CPU and OS concerns are cleanly separated. The `OsDescriptor` struct provides pure data describing how to perform OS interactions:

- **ExitStrategy**: `syscall(number)` for Linux or `iatCall(slotIndex)` for Windows
- **WriteStrategy**: `syscall(writeNumber, fd)` for Linux or `winApi(getHandleSlot, handleConstant, writeFileSlot)` for Windows

CPU emitters consume this data to emit platform-specific instruction sequences, avoiding a 2x2 matrix of emitter code.

### Runtime Functions

Runtime support functions (`_start`, `write_stdout`, `i64_to_string`, `__rt_printInt`) are written as mid-level MLIR text in `runtime.mid` and parsed by `MidLevelParser.maxon`. They use `sys.osExit` and `sys.osWrite` ops for OS-neutral I/O, which the target backends lower to concrete syscalls or IAT calls based on the `OsDescriptor`. This avoids duplicating runtime logic across targets.

### Executable Writers

- **PeWriter.maxon**: Produces minimal PE64 executables with `.text` and `.idata` sections. Imports `ExitProcess`, `GetStdHandle`, `WriteFile` from `kernel32.dll`.
- **ElfWriter.maxon**: Produces ELF executables using syscalls directly (no dynamic linking).
- **MachOWriter.maxon**: Produces Mach-O executables for macOS.

---

## How to Extend the Compiler

### Adding a New Operator or Expression

Example: adding a new binary operator.

**1. Lexer (Lexer.maxon)**

Add the token to `TokenKind` if it doesn't exist. For single-character tokens, add entries to the DFA tables (`charClassTable`, `transitionTable`, `actionTable`). For keyword operators, add an entry to `keywordMap`.

**2. Parser (Parser.maxon)**

Add parsing logic at the appropriate precedence level. The parser uses recursive descent with these precedence levels (lowest to highest):

1. `parseBitwiseOrExpr` -- `or`
2. `parseBitwiseXorExpr` -- `xor`
3. `parseBitwiseAndExpr` -- `and`
4. `parseShiftExpr` -- `shl`, `shr`
5. `parseComparisonExpr` -- `==`, `!=`, `<`, `<=`, `>`, `>=`
6. `parseAddExpr` -- `+`, `-`
7. `parseMulExpr` -- `*`, `/`, `mod`
8. `parseUnaryExpr` -- `not`, `-`
9. `parsePrimaryExpr` -- literals, identifiers, calls, parens

Each level calls the next-higher precedence level, then loops checking for its operators. Emit the corresponding `MaxHLOp`.

**3. MaxHL Dialect (MaxHLDialect.maxon)**

If the operator fits an existing enum (`BinOpKind`, `UnaryOpKind`, `CmpPredicate`), add a new enum case. Otherwise add a new variant to the `MaxHLOp` union.

**4. MaxHLToMid Conversion (MaxHLToMidConversion.maxon)**

Add a case in the lowering pass mapping the new `MaxHLOp` variant (or `BinOpKind` case) to the appropriate `ArithOp` opcode.

**5. MidToMaxX64 Conversion (MidToMaxX64Conversion.maxon)**

Add a case handling the new `ArithOp` opcode, emitting the appropriate `MaxX64Op` instructions with register allocation through `RegisterManager`.

**6. MidToMaxArm64 Conversion (MidToMaxArm64Conversion.maxon)**

Same pattern for ARM64 using the appropriate instructions.

**7. X86/Arm64 Code Emitter**

If you added new target dialect ops, add machine code emission for them in `MaxX64CodeEmitter.maxon` or `MaxArm64CodeEmitter.maxon`.

**8. Tests**

Add test cases to the relevant spec file in `/specs/`. Then add the spec name to the whitelist in `Testing/SpecTestRunner.maxon` if it's a new spec.

### Adding a New Statement

Example: adding a `while` loop.

1. **Parser**: Add a case in `parseStatement` that recognizes the keyword, parses the condition and body, and emits MaxHLOps using blocks and branches for control flow:
   - Create loop start block with `label`
   - Parse condition -> `condBr(cond, bodyBlock, exitBlock)`
   - Create body block, parse body
   - `br(loopStartBlock)`
   - Create exit block

2. **Dialect ops**: If `while` can be expressed using existing ops (`cf.br`, `cf.condBr`), no new dialect variants are needed. The parser desugars structured control flow into these primitives.

3. **Everything downstream** (MaxHLToMid, MidToTarget, emitter) already handles the underlying ops.

### Adding a New Type

1. **Parser**: Update type parsing to recognize the new type name
2. **MaxHL Dialect**: Use existing parameterized ops (e.g., `literal` with `isFloat`, `binop` with `isFloat`, `cmp` with `isFloat`) or add new variants
3. **Arith Dialect**: Add typed opcodes (e.g., `addF32` alongside `addF64`)
4. **Conversion passes**: Add cases for the new typed ops
5. **Target emitters**: Add machine code sequences for the new typed operations

### Adding a New Target

1. Create a new directory under `Targets/` (e.g., `Targets/Riscv/`)
2. Define the target dialect: `MaxRiscvDialect.maxon` with a `MaxRiscvOp` union and register enum
3. Add a new variant to `MlirOp` in `MLIR/Core/MlirOp.maxon`
4. Write the lowering pass: `MidToMaxRiscvConversion.maxon` mapping mid-level ops -> `MaxRiscvOp`
5. Write the code emitter: `MaxRiscvCodeEmitter.maxon` converting `MaxRiscvOp` -> machine code bytes
6. Add the CPU variant to `CpuArch` in `Target.maxon`
7. Add a `RegTarget` variant in `RegisterManager.maxon` and implement the target dispatch
8. Add dispatch cases in `BackendDispatch.maxon`
9. If targeting a new OS, add an executable writer under `Targets/<Os>/`
10. Add an `OsDescriptor` for the new CPU x OS combination

### Adding a New Semantic Check

1. **SemanticCheck.maxon**: Add validation logic in `runSemanticChecks`. Iterate through the `MlirModule` functions and blocks matching on relevant op variants.
2. **ErrorCode.maxon**: Add a new error code and add a variant to the `CompileError` union if needed.

### Adding a Spec Test to the Self-Hosted Runner

The self-hosted compiler runs a subset of the spec tests. To enable a new spec:

1. Open `Testing/SpecTestRunner.maxon`
2. Find the whitelist array where spec names are listed
3. Add the new spec name (e.g., `push(whitelistedSpecs, "my-new-feature")`)
4. Run `./maxon-selfhosted/maxon-selfhosted.exe spec-test --filter=my-new-feature` to verify

## Building and Testing

```bash
# Build the self-hosted compiler (using the C# compiler)
maxon build maxon-selfhosted

# Run all whitelisted spec tests
./maxon-selfhosted/maxon-selfhosted.exe spec-test

# Run filtered spec tests
./maxon-selfhosted/maxon-selfhosted.exe spec-test --filter=arithmetic

# Cross-target testing
./maxon-selfhosted/maxon-selfhosted.exe spec-test --target=all
```

## Known Constraints and Gotchas

- **Match exhaustiveness**: Maxon's `match` on unions requires exhaustive case listing -- every variant must be listed even if most are no-ops. This makes semantic check code verbose.
- **Single-line match arms**: Match arms can only contain a single expression/statement.
