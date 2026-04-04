# Self-Hosted Maxon Compiler Architecture

The self-hosted Maxon compiler is written in Maxon itself (~25,400 lines across 72 files). It compiles Maxon source code to native executables for 6 target combinations (x64/arm64 x windows/linux/macos) using an MLIR-inspired multi-dialect pipeline with incremental compilation support.

## Project Structure

```
maxon-selfhosted/
  Main.maxon                           Entry point, CLI dispatch
  build.maxon                          Project marker file

  Compiler/
    Compiler.maxon                     Top-level compile() orchestration
    Lexer.maxon                        Table-driven DFA tokenizer
    Parser.maxon                       Recursive descent parser -> MlirModule (maxon ops)
    SemanticCheck.maxon                Semantic validation pass
    MlirPipeline.maxon                 SSA value ID management
    ErrorCode.maxon                    Error codes and CompileError enum
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
        MlirOp.maxon                   MlirOp wrapper enum (10 dialect variants)
        MlirModule.maxon               Top-level module container
        MlirFunction.maxon             Named function with body region
        MlirBlock.maxon                Basic block within a region
        (blocks are stored directly in MlirFunction)
        MlirPrinter.maxon              MLIR text format printer
      Dialects/
        MaxonDialect.maxon             MaxonOp enum (high-level IR, ~58 variants)
        ArithDialect.maxon             ArithOp enum (typed arithmetic, const/unary/binary)
        CfDialect.maxon                CfOp enum (block terminators: br, condBr)
        FuncDialect.maxon              FuncOp enum (call, ret, param, funcRef, etc.)
        MemRefDialect.maxon            MemRefOp enum (storeSlot, loadSlot)
        MirDialect.maxon               MirOp enum (target-independent machine IR, ~24 variants)
        RuntimeDialect.maxon           RuntimeOp enum (printLiteral, printInt)
        SysDialect.maxon               SysOp enum (syscall, iatCall, osExit, osWrite, etc., 16 variants)
      PassPipeline.maxon               Pass registry, pipeline builder, pass runner
      Passes/
        LowerMaxonToArith.maxon        MaxonOp -> arith/cf/func/memref lowering
        LowerMaxonToSysAndRuntime.maxon MaxonOp -> runtime/sys lowering
        LowerToMir.maxon               Mid-level dialects -> MirOp lowering
        LowerABI.maxon                 ABI lowering (calling conventions)
        Mem2Reg.maxon                  Promote memref slots to SSA values
        BorrowCheck.maxon              Borrow checking pass
        InjectDrops.maxon              Insert destructor calls for owned values
        InsertRangeChecks.maxon        Insert runtime range checks for typed integers
        Canonicalize.maxon             Algebraic simplification pass
        CommonSubexpressionElimination.maxon  CSE pass
        DeadCodeElimination.maxon      DCE pass
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
        InstructionScheduler.maxon     Register-pressure-aware bottom-up list scheduler
        OsDescriptor.maxon             OS abstraction (exit/write strategies)
        PrologueEpiloguePass.maxon     Shared prologue/epilogue types, frame computation, pass entry
        RegisterManager.maxon          Cross-platform greedy linear-scan register allocator
        StdOpHelpers.maxon             Shared helpers for mid->target conversion
        TargetRegAllocDispatch.maxon   Target-specific register allocation dispatch
      X64/
        X64Dialect.maxon               X64Op enum (96 variants, OpMeta-backed), X64Register enum
        X64Latency.maxon               X64 latency table creation
        X64RegisterAlloc.maxon         X64-specific register allocation
        X64PrologueEpilogue.maxon      X64 prologue/epilogue insertion, callee-saved detection
        MidToX64Conversion.maxon       Mid-level -> X64Op lowering
        X64CodeEmitter.maxon           X64Op -> machine code bytes
      Arm64/
        Arm64Dialect.maxon             Arm64Op enum (77 variants, OpMeta-backed), Arm64Register enum
        Arm64Latency.maxon             ARM64 latency table creation
        Arm64RegisterAlloc.maxon       ARM64-specific register allocation
        Arm64PrologueEpilogue.maxon    ARM64 prologue/epilogue insertion, callee-saved detection
        MidToArm64Conversion.maxon     Mid-level -> Arm64Op lowering
        Arm64CodeEmitter.maxon         Arm64Op -> machine code bytes
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

### Pipeline Phases

The pipeline is defined in `PassPipeline.maxon` as an explicit sequence of named passes:

```
Phase 1 (Frontend):              Parser -> maxon/func/cf/memref
Phase 2 (Memory Safety):         semanticCheck -> deadFuncElim -> lowerMaxonToArith
                                 -> borrowCheck -> injectDrops
Phase 3 (SSA Construction):      mem2reg (eliminates memref)
Phase 4 (Mid-Level Optimization): canonicalize -> cse -> dce -> insertRangeChecks
Phase 5 (System Resolution):     lowerMaxonToSysAndRuntime -> lowerABI
Phase 6 (Generic Machine Lower): augmentWithRuntime -> lowerToMir
Phase 7 (Target Execution):      lowerMirToTarget -> scheduleInstructions
                                 -> allocateRegisters -> insertPrologueEpilogue
```

### Current Flow

```
Source (.maxon files)
    |
    v
  Lexer              tokenize()                Source -> Token[]
    |
    v
  Parser             parse()                   Token[] -> MlirModule (maxon/func/cf/memref ops)
    |
    v
  Phase 2-5          runPipeline(midPipeline)   Progressive lowering through dialects
    |
    v
  Phase 6-7          runPipeline(backendPipeline) Runtime augmentation, MIR, target lowering
    |
    v
  Emitter            emitTargetCode()           Target ops -> machine code bytes
    |
    v
  Exe Writer         writePe() / writeElf()     bytes -> executable file
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
        -> runPipeline(midPipeline)          phases 2-5
      -> emitBackend(midModule, target)
        -> runPipeline(backendPipeline)      phases 6-7
        -> emitTargetCode(module, target)
    -> writeExecutable(outputPath, codeResult, target)
```

### Key Design Decision: No AST

The parser emits a flat array of `MlirOp` operations directly -- there is no intermediate AST tree. The parser produces an `MlirModule` containing `MlirFunction` objects, each with a body region of `MlirBlock` objects containing `MlirOp` arrays. Control flow is encoded as block terminators (`condBr`/`br`) targeting labeled blocks.

### Key Design Decision: Multi-Dialect IR

Instead of separate IR types per stage (like a `MaxonOp[]` then a `StdOp[]`), the compiler uses a single `MlirOp` wrapper enum that dispatches across 10 dialect variants:

```maxon
export enum MlirOp
  maxon(op MaxonOp)
  arith(op ArithOp)
  cf(op CfOp)
  func(op FuncOp)
  memref(op MemRefOp)
  runtime(op RuntimeOp)
  sys(op SysOp)
  mir(op MirOp)
  x64(op X64Op)
  arm64(op Arm64Op)
end 'MlirOp'
```

Ops from different dialects coexist in the same block. Each lowering pass handles the ops it cares about and passes others through, progressively replacing higher-level ops with lower-level ones.

### Key Design Decision: Struct-Backed Target Op Enums

Each target defines its own backing type with shared fields (`latency`, `isMemory`, `isStore`, `isCall`) plus target-specific fields (e.g., `setsFlags` for x86/ARM):

```maxon
type X64OpMeta
  export let latency Latency
  export let isMemory bool
  export let isStore bool
  export let isCall bool
  export let setsFlags bool
end 'X64OpMeta'

export enum X64Op
  addRegReg(destReg X64VReg, srcReg X64VReg) = X64OpMeta{latency: 1, isMemory: false, isStore: false, isCall: false, setsFlags: true}
  loadSlot(destReg X64VReg, slotIndex VarSlot) = X64OpMeta{latency: 4, isMemory: true, isStore: false, isCall: false, setsFlags: false}
  callDirect(target ByteArray) = X64OpMeta{latency: 5, isMemory: true, isStore: false, isCall: true, setsFlags: false}
  // ...
end 'X64Op'
```

The instruction scheduler and other passes access metadata via `.rawValue` (e.g., `op.rawValue.latency`, `op.rawValue.isMemory`). Target-specific passes access their own fields (e.g., `op.rawValue.setsFlags`). Targets that don't need a field simply omit it from their backing type.

### Dialect Lifespans

| Dialect | Introduced | Eliminated | Purpose |
|---------|-----------|------------|---------|
| maxon   | Phase 1   | Phase 5    | Source-level semantics (variables, binary ops, comparisons) |
| memref  | Phase 1   | Phase 3    | Stack slot load/store (eliminated by mem2reg into SSA) |
| cf      | Phase 1   | Phase 6    | Block terminators (br, condBr) |
| func    | Phase 1   | Phase 6    | Function calls, returns, parameters |
| arith   | Phase 2   | Phase 6    | Typed arithmetic (addI64, cmpI64Eq, constF64, etc.) |
| runtime | Phase 2   | Phase 6    | I/O intrinsics (printLiteral, printInt) |
| sys     | Phase 5   | Phase 6    | OS primitives (syscall, iatCall, osExit, osWrite) |
| mir     | Phase 6   | Phase 7    | Target-independent machine IR (virtual registers, SSA) |
| x64     | Phase 7   | emission   | x86-64 machine instructions |
| arm64   | Phase 7   | emission   | ARM64 machine instructions |

## Dialects

Each dialect is a Maxon `enum` type (or an enum of sub-enums). Operations carry SSA `ValueId` references for data flow.

### Maxon Dialect (MaxonDialect.maxon)

High-level operations emitted directly by the parser. ~58 variants organized by category:

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

The Maxon dialect uses enums (`BinOpKind`, `UnaryOpKind`, `CmpPredicate`) to collapse what would be many individual variants into parameterized ops.

### Arith Dialect (ArithDialect.maxon)

Target-independent typed arithmetic. Structured as an enum of three sub-enums:

- **ArithConst**: `constI64`, `constF64`
- **ArithUnaryOp**: `negI64`, `bitNotI64`, `fpToSiI64`, `siToFpF64`
- **ArithBinaryOp**: 26 opcodes covering integer arithmetic (`addI64`, `subI64`, `mulI64`, `divI64`, `remI64`), bitwise ops (`andI64`, `orI64`, `xorI64`, `shlI64`, `shrI64`), float arithmetic (`addF64`, `subF64`, `mulF64`, `divF64`), and comparisons (`cmpI64Eq`/`Ne`/`Lt`/`Le`/`Gt`/`Ge`, `cmpF64Eq`/`Ne`/`Lt`/`Le`/`Gt`/`Ge`)

### Cf Dialect (CfDialect.maxon)

Block terminators: `br(target)` and `condBr(condId, thenTarget, elseTarget)`.

### Func Dialect (FuncDialect.maxon)

Function-level operations: `ret`, `call`, `tryCall`, `indirectCall`, `funcRef`, `tailCall`, `param`.

### MemRef Dialect (MemRefDialect.maxon)

Stack slot operations: `storeSlot(slotId, valueId)` and `loadSlot(resultId, slotId)`. Eliminated by the mem2reg pass which promotes slots to SSA values.

### MIR Dialect (MirDialect.maxon)

Target-independent machine IR with ~24 variants. Uses virtual registers (ValueIds) in SSA form. Sits between the mid-level dialects (arith/cf/func/memref/sys) and the target-specific dialects (x64/arm64). Categories:

| Category | Operations |
|----------|-----------|
| Constants & moves | `movImm`, `movReg` |
| ALU | `binOp` (parameterized by `MirBinOpcode`), `unaryOp` |
| Compare & branch | `cmp`, `condBranch`, `branch` |
| Functions | `call`, `indirectCall`, `ret`, `param` |
| Memory | `load`, `store`, `stackSlotAddr`, `globalAddr` |
| System | `syscall`, `iatCall`, `osExit`, `osWrite`, `memcpy`, `storeByte`, `loadByte` |

### Runtime Dialect (RuntimeDialect.maxon)

I/O intrinsics: `printLiteral(data)` and `printInt(valueId)`. These are lowered to `func.call` ops targeting runtime functions.

### Sys Dialect (SysDialect.maxon)

Low-level OS primitives used by the runtime. 16 variants including `syscall`, `iatCall`, `globalAddr`, `memcpy`, `stackAddr`, `storeByte`, `loadByte`, `osExit`, `osWrite`, `free`, `osWriteErr`, `loadFramePtr`, `loadIndirect`, `funcAddr`. These appear only in runtime functions (from `runtime.mid`) and are lowered directly to target machine ops.

### Target Dialects (X64Dialect.maxon, Arm64Dialect.maxon)

Machine-level operations for each CPU target, backed by `OpMeta` structs carrying latency and classification metadata:

- **X64Op** (96 variants): GPR ops (`movRegImm`, `addRegReg`, `imulRegReg`, `idivReg`, `cmpRegReg`, `setcc`, `cmov`, ...), XMM float ops (`addXmm`, `subXmm`, `mulXmm`, `divXmm`, `ucomisXmm`, `cvtSi2Float`, ...), memory ops (`storeIndirect`, `loadIndirect`, `globalLoad`, `globalStore`, ...), register allocator ops (`movRegReg`, `spillToStack`, `reloadFromStack`, `xchgRegReg`), control flow (`condJump`, `jmp`, `callDirect`, `callIndirect`), and structural ops (`prologue`, `epilogue`, `ret`).
- **Arm64Op** (77 variants): GPR ops (`movImm`, `addRegs`, `subRegs`, `mulRegs`, `sdivRegs`, `cmpRegs`, `cset`, `csel`, ...), FP ops (`fadd`, `fsub`, `fmul`, `fdiv`, `fcmp`, `scvtf`, `fcvtzs`, ...), memory ops (`storeIndirect`, `loadIndirect`, `globalLoad`, `globalStore`, ...), register allocator ops (`movRegReg`, `spillToStack`, `reloadFromStack`), control flow (`condBranch`, `branch`, `branchLink`, `branchLinkReg`), and structural ops (`prologue`, `epilogue`, `ret`).

## Key Data Structures

### MlirModule / MlirFunction / MlirBlock

The IR is organized in a nested structure: `MlirModule` contains `MlirFunction[]`, each function contains `MlirBlock[]`, and each block contains `MlirOp[]`.

### Project (Project.maxon)

The central compilation context. Contains the query database, SSA counter, parser symbol table state, global data table (string literals), root path, and compilation target.

### QueryDatabase (QueryDatabase.maxon)

Incremental compilation state: current revision, source file entries, per-file caches (tokens, parse), whole-program caches (allModule, allMid, code), dependency edges, and hit/miss counters.

### Target (Target.maxon)

`cpu: CpuArch` (x64 | arm64) x `os: Os` (windows | linux | macos).

### OpMeta (InstructionScheduler.maxon)

Compile-time metadata attached to each target instruction variant via struct-backed enums. Contains `latency` (cycle count), `isMemory` (accesses memory), `isStore` (writes memory), and `isCall` (call/syscall). Accessed at runtime via `.rawValue` on the enum.

### OsDescriptor (OsDescriptor.maxon)

Pure data describing OS interaction strategies: how to call exit (syscall vs IAT call) and how to call write (Linux syscall vs Windows API). CPU emitters consume this to generate platform-specific code without knowing OS details directly.

### CodeResult (CodeResult.maxon)

Output of code generation: raw machine code bytes, the offset of the main-call fixup, a list of relocations, and the final target-level MlirModule (for IR inspection).

### GlobalDataTable (RuntimeFunctions.maxon)

Maps names (like `__str_0`) to byte arrays for `.rdata`/`.rodata` sections. Built during Maxon-to-mid lowering as string literals are encountered.

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

### Instruction Scheduling (InstructionScheduler.maxon)

A register-pressure-aware bottom-up list scheduler runs after MIR lowering but before register allocation. It reorders instructions within each basic block to:
- Keep long-latency operations (division, loads) as early as possible on the critical path
- Respect data dependencies and memory ordering constraints
- Reduce register pressure by preferring pressure-reducing operations when nearing the threshold

The scheduler reads instruction metadata directly from the `OpMeta` backing struct on each target op (`latency`, `isMemory`, `isStore`, `isCall`).

### Register Allocation

The codegen uses a **greedy linear-scan register allocator** (`RegisterManager.maxon`) that works across both x86-64 and ARM64 targets:

1. The register allocator maintains a `RegState` tracking which SSA values are in which physical registers, with LRU eviction when registers are exhausted
2. Values are assigned to physical registers on first use and kept live as long as possible
3. When all registers are occupied, the least-recently-used value is **spilled to stack** and its register is reclaimed
4. The allocator uses **register hints** to reduce unnecessary moves (e.g., preferring the return register for values about to be returned)
5. **Deferred value materialization**: values that are only consumed by sink ops (return, call arguments) skip eager register allocation
6. **Caller-saved register management**: registers are saved/restored around call sites
7. Separate pools for GPR and FP (XMM/D-register) allocation with independent spill tracking
8. **Cross-block state**: `RegSnapshot` captures register assignments at block boundaries to handle control flow merges

Target-specific register allocation logic lives in `X64RegisterAlloc.maxon` and `Arm64RegisterAlloc.maxon`, dispatched via `TargetRegAllocDispatch.maxon`.

### OS Abstraction (OsDescriptor Pattern)

CPU and OS concerns are cleanly separated. The `OsDescriptor` struct provides pure data describing how to perform OS interactions:

- **ExitStrategy**: `syscall(number)` for Linux or `iatCall(slotIndex)` for Windows
- **WriteStrategy**: `syscall(writeNumber, fd)` for Linux or `winApi(getHandleSlot, handleConstant, writeFileSlot)` for Windows

CPU emitters consume this data to emit platform-specific instruction sequences, avoiding a 2x2 matrix of emitter code.

### Runtime Functions

Runtime support functions (`mrt_start`, `mrt_write_stdout`, `mrt_i64_to_string`, `mrt_printInt`) are written as mid-level MLIR text in `runtime.mid` and parsed by `MidLevelParser.maxon`. All runtime.mid functions use the `mrt_` prefix ("Maxon RunTime"). They use `sys.osExit` and `sys.osWrite` ops for OS-neutral I/O, which the target backends lower to concrete syscalls or IAT calls based on the `OsDescriptor`. This avoids duplicating runtime logic across targets.

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

Each level calls the next-higher precedence level, then loops checking for its operators. Emit the corresponding `MaxonOp`.

**3. Maxon Dialect (MaxonDialect.maxon)**

If the operator fits an existing enum (`BinOpKind`, `UnaryOpKind`, `CmpPredicate`), add a new enum case. Otherwise add a new variant to the `MaxonOp` enum.

**4. Maxon-to-Mid Lowering (LowerMaxonToArith.maxon / LowerMaxonToSysAndRuntime.maxon)**

Add a case in the appropriate lowering pass mapping the new `MaxonOp` variant (or `BinOpKind` case) to the appropriate `ArithOp` opcode (in `LowerMaxonToArith.maxon`) or `RuntimeOp`/`SysOp` (in `LowerMaxonToSysAndRuntime.maxon`).

**5. MIR Lowering (LowerToMir.maxon)**

Add a case handling the new `ArithOp` opcode, mapping it to the corresponding `MirOp`.

**6. Target Conversion (MidToX64Conversion.maxon / MidToArm64Conversion.maxon)**

Add a case handling the new `MirOp`, emitting the appropriate target ops with register allocation through `RegisterManager`.

**7. X86/Arm64 Code Emitter**

If you added new target dialect ops, add machine code emission for them in `X64CodeEmitter.maxon` or `Arm64CodeEmitter.maxon`. Add the `OpMeta` backing value to the new enum case.

**8. Tests**

Add test cases to the relevant spec file in `/specs/`. Then add the spec name to the whitelist in `Testing/SpecTestRunner.maxon` if it's a new spec.

### Adding a New Target

1. Create a new directory under `Targets/` (e.g., `Targets/Riscv/`)
2. Define the target dialect: `RiscvDialect.maxon` with a `RiscvOp` enum (backed by `OpMeta`) and register enum
3. Add a new variant to `MlirOp` in `MLIR/Core/MlirOp.maxon`
4. Write the lowering pass: `MidToRiscvConversion.maxon` mapping MIR ops -> `RiscvOp`
5. Write the code emitter: `RiscvCodeEmitter.maxon` converting `RiscvOp` -> machine code bytes
6. Add the CPU variant to `CpuArch` in `Target.maxon`
7. Create `RiscvRegisterAlloc.maxon` and add dispatch in `TargetRegAllocDispatch.maxon`
8. Create `RiscvLatency.maxon` with `createRiscvLatencyTable()`
9. Add dispatch cases in `BackendDispatch.maxon`
10. If targeting a new OS, add an executable writer under `Targets/<Os>/`
11. Add an `OsDescriptor` for the new CPU x OS combination

### Adding a New Semantic Check

1. **SemanticCheck.maxon**: Add validation logic in `runSemanticChecks`. Iterate through the `MlirModule` functions and blocks matching on relevant op variants.
2. **ErrorCode.maxon**: Add a new error code and add a variant to the `CompileError` enum if needed.

### Adding a Spec Test to the Self-Hosted Runner

The self-hosted compiler runs a subset of the spec tests. To enable a new spec:

1. Open `Testing/SpecTestRunner.maxon`
2. Find the whitelist array where spec names are listed
3. Add the new spec name (e.g., `push(whitelistedSpecs, "my-new-feature")`)
4. Run `./maxon-selfhosted/bin/maxon-selfhosted.exe spec-test --filter=my-new-feature` to verify

## Building and Testing

```bash
# Build the self-hosted compiler (using the C# compiler)
maxon build maxon-selfhosted

# Run all whitelisted spec tests
./maxon-selfhosted/bin/maxon-selfhosted.exe spec-test

# Run filtered spec tests
./maxon-selfhosted/bin/maxon-selfhosted.exe spec-test --filter=arithmetic

# Cross-target testing
./maxon-selfhosted/bin/maxon-selfhosted.exe spec-test --target=all
```

## Known Constraints and Gotchas

- **Match exhaustiveness**: Maxon's `match` on enums requires exhaustive case listing -- every variant must be listed even if most are no-ops. Range syntax (`a to b gives ...`) helps reduce verbosity.
- **Single-line match arms**: Match arms can only contain a single expression/statement.
- **Struct-backed enum field ordering**: During `PreScanEnum`, cross-file struct field resolution is deferred when the backing struct hasn't been fully defined yet (file processing order within the PreScan pass).
