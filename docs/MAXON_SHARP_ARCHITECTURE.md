# Maxon-Sharp Compiler Architecture

A C# compiler for the Maxon language, using an MLIR-inspired multi-stage pipeline to generate native x86-64 Windows PE executables without any LLVM dependency.

## Table of Contents

- [Overview](#overview)
- [Directory Structure](#directory-structure)
- [Compilation Pipeline](#compilation-pipeline)
- [Stage 1: Lexer](#stage-1-lexer)
- [Stage 2: Parser](#stage-2-parser)
- [Stage 3: MLIR Pipeline](#stage-3-mlir-pipeline)
- [MLIR Core Infrastructure](#mlir-core-infrastructure)
- [MLIR Dialects](#mlir-dialects)
- [Dialect Conversions](#dialect-conversions)
- [Semantic Passes](#semantic-passes)
- [Stage 4: Code Emission](#stage-4-code-emission)
- [Stage 5: PE Writer](#stage-5-pe-writer)
- [Error System](#error-system)
- [Logging System](#logging-system)
- [Testing Infrastructure](#testing-infrastructure)
- [Key Design Decisions](#key-design-decisions)


## Overview

Maxon-sharp is a ground-up C# rewrite of the Maxon compiler, replacing an earlier C++ implementation. It compiles Maxon source files into native Windows x86-64 PE executables through a progressive lowering pipeline inspired by MLIR (Multi-Level Intermediate Representation).

The compiler is a single .NET 8.0 console application (`maxonsharp.exe`) that handles compilation, building, running, and testing. It targets `win-x64` and publishes as a single-file executable.

Entry point: `maxon-sharp/Program.cs`

Commands:
- `maxonsharp compile <file>` -- compile a single `.maxon` file
- `maxonsharp build [<directory>]` -- build a project directory
- `maxonsharp run <file|directory>` -- compile and run
- `maxonsharp spec-test [--filter=PATTERN]` -- run spec tests


## Directory Structure

```
maxon-sharp/
  Program.cs                          # CLI entry point and command dispatch
  Logger.cs                           # Per-category logging system
  GlobalSuppressions.cs               # Code analysis suppressions
  MaxonSharp.csproj                   # .NET 8.0 project (win-x64, single-file publish)

  Compiler/
    0-Compiler.cs                     # Top-level Compiler class and StdlibLoader
    1-Lexer.cs                        # Tokenizer (Token, TokenType, Lexer)
    2-Parser.cs                       # Parser producing MlirModule directly
    3-MlirPipeline.cs                 # MLIR pipeline orchestration
    4-CodeEmitter.cs                  # Stage orchestrator for code emission
    5-PeWriter.cs                     # Windows PE32+ executable writer
    CompileError.cs                   # Structured compile error with formatting
    ErrorCode.cs                      # Error code enum (E1xxx-E6xxx by stage)

    MLIR/
      Core/
        MlirContext.cs                # Thread-local context, value factory
        MlirValue.cs                  # SSA value with ID and type
        MlirType.cs                   # Type system (i32, i64, f64, i1, void)
        MlirOperation.cs             # Abstract base for all operations
        MlirBlock.cs                  # Named block containing operations
        MlirRegion.cs                 # Region containing ordered blocks
        MlirFunction.cs              # Function with params, return type, body region
        MlirModule.cs                # Top-level module (functions, rdata, globals)
        MlirAttribute.cs             # Operation attributes (IntegerAttr)
        MlirPrinter.cs               # MLIR textual IR printer

      Dialects/
        MaxonDialect.cs               # High-level Maxon operations
        ArithDialect.cs               # Arithmetic constant operations
        FuncDialect.cs                # Function call and return operations
        X86Dialect.cs                 # x86-64 machine operations

      Conversion/
        MaxonToStandardConversion.cs  # Maxon dialect -> Arith + Func dialects
        StandardToX86Conversion.cs    # Arith + Func -> X86 dialect

      Passes/
        SemanticCheckPass.cs          # Semantic validation (main exists, returns int)

      Emit/
        X86CodeEmitter.cs             # X86 operation -> machine code bytes

  Testing/
    TestTypes.cs                      # SpecFile, TestCase, Fragment, TestResult, etc.
    SpecParser.cs                     # Parses markdown spec files into test cases
    FragmentGenerator.cs              # Generates .test fragments from specs
    TestRunner.cs                     # Parallel test execution engine
    UnitTests.cs                      # Built-in compiler unit tests
    WindowsJobObject.cs              # Win32 job objects for process management
```


## Compilation Pipeline

The compiler transforms Maxon source code into a Windows executable through six stages. The `Compiler` class in `maxon-sharp/Compiler/0-Compiler.cs` orchestrates the full pipeline:

```
Source Code
    |
    v
[Stage 1] Lexer          -- source text -> token stream
    |
    v
[Stage 2] Parser          -- tokens -> MlirModule (Maxon dialect)
    |
    v
[Stage 3] MLIR Pipeline   -- semantic checks, then progressive lowering:
    |                         Maxon dialect -> Standard dialects -> X86 dialect
    v
[Stage 4] Code Emission   -- X86 dialect ops -> machine code bytes
    |
    v
[Stage 5] PE Writer       -- machine code + data -> .exe file
    |
    v
Native Windows PE Executable
```

Multi-file compilation works by lexing and parsing each source file into its own `MlirModule`, then merging all modules into a single module before the MLIR pipeline runs. The standard library is optionally prepended (loaded by `StdlibLoader`).

The `--dump-stages` flag writes the IR at each pipeline stage to files:
- `.1-maxon.mlir` -- after parsing (Maxon dialect)
- `.2-standard.mlir` -- after MaxonToStandard conversion
- `.3-x86.mlir` -- after StandardToX86 conversion


## Stage 1: Lexer

**File:** `maxon-sharp/Compiler/1-Lexer.cs`

The `Lexer` class tokenizes Maxon source into a `List<Token>`. Each `Token` carries its `TokenType`, string `Value`, and source location (`Line`, `Column`).

### Token Categories

**Keywords** (44 total) -- defined in `Lexer.KeywordMap` with metadata:
- Control flow: `function`, `return`, `end`, `if`, `else`, `while`, `for`, `in`, `break`, `continue`, `match`, `then`, `fallthrough`, `default`
- Declarations: `let`, `var`, `type`, `enum`, `interface`, `extension`, `export`, `static`, `typealias`
- Type system: `returns`, `uses`, `is`, `with`, `extends`, `self`, `Self`, `array`, `of`, `as`
- Logical: `and`, `or`, `not`, `mod`
- Error handling: `throws`, `throw`, `try`, `otherwise`, `ignore`
- Literals: `true`, `false`
- Built-in types: `int`, `float`, `bool`, `byte`
- Other: `from`, `to`, `gives`

Each keyword entry includes a `KeywordCategory`, help text (used by the VS Code extension), and a flag indicating whether it can have a block label.

**Operators** -- defined in `Lexer.OperatorMap`:
- Arithmetic: `+`, `-`, `*`, `/`
- Comparison: `==`, `!=`, `<`, `<=`, `>`, `>=`
- Bitwise: `&`, `|`, `^`, `<<`, `>>`
- Assignment: `=`
- Range: `..`, `..=`, `..<`

**Literals:**
- Integer: decimal, hex (`0x`), binary (`0b`), octal (`0o`), with underscore separators
- Float: decimal with optional exponent notation
- String: double-quoted, with `{...}` interpolation detection
- Character: single-quoted (also used for `end` block labels like `'main'`)

**Formatting tokens:** `Newline`, `DocComment` (lines starting with `///`), `Eof`

The lexer operates as a single-pass scanner. It skips `//` line comments inline and returns `DocComment` tokens for `///` lines. Whitespace (spaces, tabs, carriage returns) is consumed silently, but newlines produce explicit `Newline` tokens since Maxon uses newline-delimited syntax.


## Stage 2: Parser

**File:** `maxon-sharp/Compiler/2-Parser.cs`

The `Parser` class takes a token list and produces an `MlirModule` populated with `MlirFunction` objects containing Maxon dialect operations. There is no separate AST data structure -- the parser emits MLIR operations directly into blocks.

### Key Design: Direct-to-MLIR Parsing

Unlike traditional compilers that parse into an AST and then lower to IR, maxon-sharp's parser constructs the MLIR representation directly. As it parses each source construct, it creates the corresponding Maxon dialect operations and adds them to the current block:

```csharp
// The parser maintains current function and block pointers
private MlirFunction? _currentFunction;
private MlirBlock? _currentBlock;
```

### Parsing Flow

1. **Top level:** iterates tokens, expecting `function` declarations
2. **Function parsing:** creates an `MlirFunction`, sets up an `entry` block, parses the body, and expects an `end 'name'` label
3. **Statement parsing:** currently handles `return` statements
4. **Expression parsing:** handles integer literals (creating `MaxonConstantOp`) and function calls (creating `MaxonCallOp`)

### Type Resolution

The parser maps Maxon type names to `MlirType` values:
- `int` -> `MlirType.I64` (64-bit signed integer)
- `float` -> `MlirType.F64` (64-bit double)
- `bool` -> `MlirType.I1` (1-bit boolean)


## Stage 3: MLIR Pipeline

**File:** `maxon-sharp/Compiler/3-MlirPipeline.cs`

The `MlirPipeline` class runs the sequence of passes and dialect conversions on the module. The pipeline executes three phases in order:

```
1. SemanticCheckPass.Run(module)           -- validate program semantics
2. MaxonToStandardConversion.Run(module)   -- lower Maxon ops to Standard ops
3. StandardToX86Conversion.Run(module)     -- lower Standard ops to X86 ops
```

All transformations operate on the module in-place, replacing operations within blocks. The pipeline returns an `MlirPipelineResult` containing the transformed module and optionally the textual X86 IR.


## MLIR Core Infrastructure

The compiler implements a subset of MLIR concepts as C# classes in `maxon-sharp/Compiler/MLIR/Core/`. This is not a binding to the real MLIR library -- it is a custom reimplementation of the core abstractions.

### MlirContext

**File:** `maxon-sharp/Compiler/MLIR/Core/MlirContext.cs`

The context is the factory for SSA values and provides thread-local scoping. It uses a `[ThreadStatic]` field to make the current context implicitly available to all operation constructors:

```csharp
public class MlirContext {
    [ThreadStatic]
    private static MlirContext? _current;
    public static MlirContext Current => _current ?? throw ...;
}
```

Operations create their result values via `MlirContext.Current.CreateValue(type, this)`, which assigns a sequential integer ID. The context is pushed onto the thread-local stack using `PushScope()`, which returns an `IDisposable` for RAII-style scoping.

### MlirValue

**File:** `maxon-sharp/Compiler/MLIR/Core/MlirValue.cs`

Represents an SSA value -- the result of an operation. Each value has:
- `Id` (int) -- unique sequential identifier, printed as `%0`, `%1`, etc.
- `Type` (MlirType) -- the value's type
- `DefiningOp` (MlirOperation?) -- the operation that produced this value

### MlirType

**File:** `maxon-sharp/Compiler/MLIR/Core/MlirType.cs`

Represents types in the IR. Each type has a `Name` (string) and `SizeInBytes` (int). The following built-in types are defined as static singletons:

| Type | Name | Size |
|------|------|------|
| `MlirType.I32` | `i32` | 4 bytes |
| `MlirType.I64` | `i64` | 8 bytes |
| `MlirType.F64` | `f64` | 8 bytes |
| `MlirType.I1` | `i1` | 1 byte |
| `MlirType.Void` | `void` | 0 bytes |

### MlirOperation

**File:** `maxon-sharp/Compiler/MLIR/Core/MlirOperation.cs`

Abstract base class for all operations across all dialects. Every operation has:
- `Mnemonic` (abstract string) -- the operation's textual name (e.g., `"maxon.constant"`, `"x86.ret"`)
- `Operands` (List\<MlirValue\>) -- input values consumed by this operation
- `Results` (List\<MlirValue\>) -- output values produced by this operation
- `Attributes` (Dictionary\<string, MlirAttribute\>) -- named compile-time attributes

### MlirAttribute

**File:** `maxon-sharp/Compiler/MLIR/Core/MlirAttribute.cs`

Base class for compile-time metadata attached to operations. Currently one concrete subclass:
- `IntegerAttr` -- holds a `long Value` and `MlirType Type`

### MlirBlock

**File:** `maxon-sharp/Compiler/MLIR/Core/MlirBlock.cs`

A named sequence of operations. Blocks are the basic unit of control flow -- execution proceeds linearly through a block's operations. Each block has:
- `Name` (string) -- e.g., `"entry"`
- `Operations` (List\<MlirOperation\>) -- the ordered list of operations

### MlirRegion

**File:** `maxon-sharp/Compiler/MLIR/Core/MlirRegion.cs`

A region contains an ordered list of blocks. The first block is the `EntryBlock`. Regions model scoped control flow -- a function body is a region.

### MlirFunction

**File:** `maxon-sharp/Compiler/MLIR/Core/MlirFunction.cs`

Represents a function definition:
- `Name` (string) -- the function's identifier
- `ParamTypes` (List\<MlirType\>) -- parameter types
- `ReturnType` (MlirType?) -- return type (null for void functions)
- `Body` (MlirRegion) -- the function's body region

### MlirModule

**File:** `maxon-sharp/Compiler/MLIR/Core/MlirModule.cs`

The top-level container for a compilation unit:
- `Functions` (List\<MlirFunction\>) -- all function definitions
- `RdataEntries` (List\<(string label, byte[] bytes)\>) -- read-only data constants (e.g., string literals, constant array data)
- `Globals` (List\<MlirGlobal\>) -- global variables with optional init values

The `Merge` method combines two modules by appending all functions, rdata entries, and globals. This supports multi-file compilation where each source file is parsed into a separate module and then merged.

`MlirGlobal` is defined alongside the module:
```csharp
public class MlirGlobal(string name, MlirType type, MlirAttribute? initValue = null)
```

### MlirPrinter

**File:** `maxon-sharp/Compiler/MLIR/Core/MlirPrinter.cs`

Produces a textual representation of the IR, similar to MLIR's assembly format. The output format is:

```
module {
  func @name(types...) -> returnType {
  entry:
    %0 = op.name %1, %2 {attr = value}
  }
}
```

The printer is used for `--emit-ir`, `--dump-stages`, and capturing IR for test verification.


## MLIR Dialects

Each dialect defines a set of operations at a particular abstraction level. Operations at higher levels are progressively lowered (converted) into operations at lower levels.

### Maxon Dialect

**File:** `maxon-sharp/Compiler/MLIR/Dialects/MaxonDialect.cs`

The highest-level dialect, representing Maxon language constructs directly. The parser emits operations in this dialect.

**Operations:**

| Operation | Mnemonic | Description |
|-----------|----------|-------------|
| `MaxonConstantOp` | `maxon.constant` | Creates a constant value. Produces one result value and stores an `IntegerAttr` attribute. |
| `MaxonCallOp` | `maxon.call @<callee>` | Calls a named function with a list of argument values. Does not produce a result directly (result handled through `MaxonExpr`). |
| `MaxonReturnOp` | `maxon.return` | Returns from a function, optionally with a return expression. |

**Expression System:**

The Maxon dialect uses a `MaxonExpr` discriminated union to represent expressions that may involve deferred evaluation:

```csharp
public abstract record MaxonExpr {
    public sealed record Value(MlirValue MlirValue) : MaxonExpr;
    public sealed record Call(MaxonCallOp CallOp) : MaxonExpr;
}
```

`MaxonReturnOp` holds an optional `MaxonExpr` rather than a raw `MlirValue`, allowing the return to contain a call expression whose result type needs to be resolved during lowering (when the callee's return type is known from the module's function list).

### Arith Dialect

**File:** `maxon-sharp/Compiler/MLIR/Dialects/ArithDialect.cs`

Standard arithmetic operations, modeled after MLIR's `arith` dialect.

| Operation | Mnemonic | Description |
|-----------|----------|-------------|
| `ArithConstantOp` | `arith.constant` | Materializes a compile-time constant as an SSA value. Stores the value as an `IntegerAttr`. |

### Func Dialect

**File:** `maxon-sharp/Compiler/MLIR/Dialects/FuncDialect.cs`

Standard function operations, modeled after MLIR's `func` dialect.

| Operation | Mnemonic | Description |
|-----------|----------|-------------|
| `FuncCallOp` | `func.call @<callee>` | Calls a function. Produces a result value if the callee has a return type. |
| `FuncReturnOp` | `func.return` | Returns from a function, optionally with a return value. |

### X86 Dialect

**File:** `maxon-sharp/Compiler/MLIR/Dialects/X86Dialect.cs`

Low-level x86-64 instructions. All operations inherit from `X86Op`, which extends `MlirOperation`.

**Register Enum:**

`X86Register` defines the available registers:
- 64-bit general purpose: `Rax`, `Rcx`, `Rdx`, `Rbx`, `Rsp`, `Rbp`, `Rsi`, `Rdi`, `R8`--`R15`
- 32-bit general purpose: `Eax`, `Ecx`, `Edx`, `Ebx`, `Esp`, `Ebp`, `Esi`, `Edi`

**Operations:**

| Operation | Mnemonic | Description |
|-----------|----------|-------------|
| `X86PushReg` | `x86.push <reg>` | Push register onto stack |
| `X86PopReg` | `x86.pop <reg>` | Pop stack into register |
| `X86MovRegReg` | `x86.mov <dst>, <src>` | Move register to register |
| `X86MovRegImm` | `x86.mov <dst>, <imm>` | Move immediate to register |
| `X86SubRegImm` | `x86.sub <dst>, <imm>` | Subtract immediate from register |
| `X86AddRegImm` | `x86.add <dst>, <imm>` | Add immediate to register |
| `X86CallDirect` | `x86.call <target>` | Direct function call (relative) |
| `X86Ret` | `x86.ret` | Return from function |

### Dialect Hierarchy

```
Maxon Dialect (language-level semantics)
    |
    | MaxonToStandardConversion
    v
Standard Dialects: Arith + Func (generic operations)
    |
    | StandardToX86Conversion
    v
X86 Dialect (machine instructions)
```


## Dialect Conversions

Conversions transform operations from higher-level dialects into lower-level dialects. Each conversion operates on the module in-place, walking all functions and blocks and replacing operations.

### MaxonToStandard Conversion

**File:** `maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs`

Converts Maxon dialect operations to Arith and Func dialect operations:

| Source | Target |
|--------|--------|
| `MaxonConstantOp` | `ArithConstantOp` |
| `MaxonReturnOp` (with `MaxonExpr.Value`) | `FuncReturnOp` |
| `MaxonReturnOp` (with `MaxonExpr.Call`) | `FuncCallOp` + `FuncReturnOp` |
| `MaxonCallOp` | Handled through `MaxonReturnOp`'s `MaxonExpr.Call` |

The conversion maintains a `valueMap` (Dictionary\<MlirValue, MlirValue\>) to remap values from old operations to their replacements. When a `MaxonReturnOp` contains a `MaxonExpr.Call`, the conversion looks up the callee function in the module to determine the return type for the `FuncCallOp` result.

### StandardToX86 Conversion

**File:** `maxon-sharp/Compiler/MLIR/Conversion/StandardToX86Conversion.cs`

Converts Arith and Func operations to X86 instructions. For each function, it creates a new `entry` block with:

1. **Function prologue:** `push rbp` + `mov rbp, rsp`
2. **Operation translation:**
   - `ArithConstantOp` -> `mov eax, <value>` (return value register)
   - `FuncCallOp` -> `call <target>` (direct call)
   - `FuncReturnOp` -> `pop rbp` + `ret` (function epilogue)
3. Replaces the function's body with the new block


## Semantic Passes

### SemanticCheckPass

**File:** `maxon-sharp/Compiler/MLIR/Passes/SemanticCheckPass.cs`

Runs before dialect conversions. Currently validates:
- **E3001:** A `main` function must exist in the module
- **E3002:** The `main` function must return `ExitCode`


## Stage 4: Code Emission

Code emission converts X86 dialect operations into raw machine code bytes.

### CodeEmitter (Orchestrator)

**File:** `maxon-sharp/Compiler/4-CodeEmitter.cs`

The `CodeEmitter` class orchestrates the emission process. It produces a `CodeEmitResult` containing:
- `Code` (byte[]) -- the `.text` section machine code
- `Rdata` (byte[]) -- the `.rdata` section (read-only data)
- `Data` (byte[]) -- the `.data` section (global variables)
- `Imports` (IReadOnlyList\<ImportEntry\>) -- DLL imports

**Emission order:**

1. Define rdata constants from `module.RdataEntries`
2. Define globals in the data section from `module.Globals`
3. Emit the `_start` wrapper (entry point at code offset 0)
4. Emit the `main` function first, then all other functions
5. Emit the `__chkstk` stub (stack probing for large allocations)
6. Patch `__chkstk` call sites
7. Resolve internal label references
8. Calculate section layout and resolve cross-section references (rdata, globals, imports)

### X86CodeEmitter (Machine Code Generator)

**File:** `maxon-sharp/Compiler/MLIR/Emit/X86CodeEmitter.cs`

The `X86CodeEmitter` is the low-level machine code generator. It maintains:

**Code buffer and sections:**
- `_code` (List\<byte\>) -- machine code bytes
- `_rdata` (List\<byte\>) -- read-only data bytes
- `_data` (List\<byte\>) -- global variable data bytes
- `_imports` (List\<ImportEntry\>) -- DLL import entries

**Label and fixup tracking:**
- `_labels` -- maps label names to code offsets
- `_relCallFixups` -- patches for relative call instructions
- `_importCallFixups` -- patches for indirect calls through the IAT
- `_rdataFixups` -- patches for rdata references (RIP-relative)
- `_globalFixups` -- patches for global variable references
- `_chkstkCallSites` -- call sites needing chkstk patching

**X86 encoding:**

The emitter encodes x86-64 instructions directly, handling:
- REX prefixes for 64-bit operations and extended registers (R8-R15)
- ModR/M bytes for register-register operations
- Immediate encoding (8-bit, 32-bit, 64-bit)
- RIP-relative addressing for cross-section references

Key encoding methods:
- `EmitPushReg` / `EmitPopReg` -- stack operations with REX.B for R8-R15
- `EmitMovRegReg` -- REX.W + 0x89 encoding with proper REX.R/REX.B bits
- `EmitMovRegImm` -- selects between imm32 (sign-extended) and imm64 forms
- `EmitSubRegImm` / `EmitAddRegImm` -- selects between imm8 and imm32 forms

**The `_start` entry point:**

`EmitStartWrapper()` generates a small entry stub that:
1. Allocates shadow space (`sub rsp, 0x28`) per the Windows x64 ABI
2. Calls `main` (relative call, patched later)
3. Moves the return value (`eax`) to the first argument register (`ecx`)
4. Calls `ExitProcess` via an indirect call through the IAT
5. Emits `int3` as an unreachable guard

**Resolution phases:**

After all code is emitted, four resolution phases patch placeholder values:
- `ResolveLabels()` -- patches relative call offsets between internal functions
- `ResolveRdata(offset)` -- patches RIP-relative references to `.rdata` section data
- `ResolveGlobals(offset)` -- patches RIP-relative references to `.data` section globals
- `ResolveImports(offset)` -- patches RIP-relative references to IAT entries in `.idata`

Each uses the formula: `target_offset - (fixup_offset + 4)` for x86 RIP-relative addressing.


## Stage 5: PE Writer

**File:** `maxon-sharp/Compiler/5-PeWriter.cs`

Generates a Windows PE32+ (64-bit) executable. The PE writer constructs the entire binary from scratch, writing headers and sections in a single pass.

### PE Layout

```
+-------------------+
| DOS Header (MZ)   |  64 bytes, e_lfanew points to PE signature
+-------------------+
| PE Signature      |  "PE\0\0"
+-------------------+
| COFF Header       |  Machine: AMD64, section count, optional header size
+-------------------+
| Optional Header   |  PE32+ (240 bytes), entry point, image base, data directories
+-------------------+
| Section Headers   |  40 bytes each (.text, .rdata, .data, .idata)
+-------------------+
| .text             |  Executable code (entry point at start)
+-------------------+
| .rdata            |  Read-only data (string literals, constant arrays)
+-------------------+
| .data             |  Read-write global variables
+-------------------+
| .idata            |  Import tables (IAT, ILT, Import Directory, Hint/Name, DLL names)
+-------------------+
```

### Section Details

| Section | Present | Characteristics | Contents |
|---------|---------|-----------------|----------|
| `.text` | Always | `0x60000020` (CODE, EXECUTE, READ) | Machine code starting with `_start` |
| `.rdata` | When rdata exists | `0x40000040` (INIT_DATA, READ) | String literals, constant array data |
| `.data` | When globals exist | `0xC0000040` (INIT_DATA, READ, WRITE) | Global variables with initial values |
| `.idata` | When imports exist | `0xC0000040` (INIT_DATA, READ, WRITE) | Import tables for DLL functions |

### PE Constants

- **File alignment:** 512 bytes (`0x200`)
- **Section alignment:** 4096 bytes (`0x1000`)
- **Image base:** `0x140000000` (standard for 64-bit)
- **Subsystem:** Console (`3`)
- **DLL characteristics:** `0x8160` (DYNAMIC_BASE, NX_COMPAT, TERMINAL_SERVER_AWARE, HIGH_ENTROPY_VA)

### Import Section Structure

The `.idata` section is built by `BuildImportSection()` and contains:

1. **IAT (Import Address Table)** -- 8-byte entries per import, null-terminated per DLL. The Windows loader overwrites these with actual function addresses at load time.
2. **ILT (Import Lookup Table)** -- same structure as the IAT (pre-load copy)
3. **Import Directory Table** -- 20-byte entries per DLL: ILT RVA, timestamp, forwarder chain, name RVA, IAT RVA
4. **Hint/Name Table** -- hint (2 bytes) + null-terminated function name for each import
5. **DLL name strings** -- null-terminated DLL names

The import section uses relative RVAs internally, then `FixupImportSection()` adds the actual `.idata` section RVA to convert them to image-relative addresses.

### Current Imports

The only import used by the default entry stub is `ExitProcess` from `kernel32.dll`. The runtime stubs may also import heap management functions.


## Error System

### CompileError

**File:** `maxon-sharp/Compiler/CompileError.cs`

All compiler errors are thrown as `CompileError` exceptions, which carry:
- `Code` (ErrorCode) -- structured error code
- `Message` (string) -- human-readable description
- `Line` / `Column` (int?) -- source location
- `FilePath` (string?) -- source file path

Error formatting produces messages like:
```
error E3001: path/file.maxon:5:10: No 'main' function found
```

Paths are made relative to `CompileError.ProjectRoot` for clean display.

### ErrorCode

**File:** `maxon-sharp/Compiler/ErrorCode.cs`

Error codes follow the format `E` + 4 digits, grouped by compilation stage:

| Range | Stage | Examples |
|-------|-------|----------|
| `E1xxx` | Lexer | `E1001` unexpected character, `E1002` unterminated string |
| `E2xxx` | Parser | `E2001` unexpected token, `E2008` mismatched end label |
| `E3xxx` | Semantic | `E3001` no main, `E3002` main wrong return type |
| `E4xxx` | MLIR pipeline | `E4001` unsupported expression, `E4009` immutable variable, `E4010`--`E4013` ownership errors |
| `E5xxx` | Code emitter | `E5001` no main function, `E5002` unsupported instruction |
| `E6xxx` | PE writer | `E6001` write error |

The error code enum defines more codes than are currently exercised -- several (like ownership errors E4010-E4013) represent planned future features.


## Logging System

**File:** `maxon-sharp/Logger.cs`

The `Logger` provides structured logging with per-category level control.

**Log levels** (least to most verbose): `None`, `Error`, `Info`, `Debug`, `Trace`

**Log categories** with their 3-letter codes:

| Category | Code | Area |
|----------|------|------|
| `Compiler` | `CMP` | Top-level compiler |
| `Lexer` | `LEX` | Tokenization |
| `Parser` | `PAR` | Parsing |
| `Semantic` | `SEM` | Semantic analysis |
| `Hir` | `HIR` | High-level IR (reserved) |
| `Lir` | `LIR` | Low-level IR (reserved) |
| `Optimizer` | `OPT` | Optimization (reserved) |
| `Codegen` | `GEN` | Code emission |
| `Pe` | `PE ` | PE file writing |
| `Testing` | `TST` | Test execution |
| `Mlir` | `MLR` | MLIR pipeline |
| `RegAlloc` | `REG` | Register allocation (reserved) |

Usage: `--log=debug` sets all categories, `--log=lexer:trace` sets a specific category.

All log output goes to `stderr`, keeping `stdout` clean for program output.


## Testing Infrastructure

The compiler has two testing systems: spec tests (integration tests from specification files) and unit tests (compiler-internal verification).

### Spec Tests

Spec-driven development is the primary testing approach. Each language feature is specified in a markdown file under `/specs/`, and the compiler extracts and runs tests from those specs.

**Pipeline:**

```
Spec file (.md)              -- human-readable specification with embedded tests
    |
    | SpecParser
    v
SpecFile (in-memory)         -- parsed feature metadata + test cases
    |
    | FragmentGenerator
    v
Fragment files (.test)       -- one file per test, with source + expectations + IR
    |
    | TestRunner
    v
Test results                 -- parallel execution, comparison against expectations
```

**SpecParser** (`maxon-sharp/Testing/SpecParser.cs`):
- Parses YAML frontmatter for `feature`, `status`, and `category`
- Extracts test cases from `<!-- test: name -->` markers
- Extracts code blocks by language tag: `maxon` (source), `exitcode`, `stdout`, `maxoncstderr`, `expectedmlir`, `requiredmlir`
- Also extracts executable examples from the `## Documentation` section (code blocks containing `function main()`)
- Skips specs with `status: draft`

**FragmentGenerator** (`maxon-sharp/Testing/FragmentGenerator.cs`):
- Generates `.test` fragment files in `/specs/fragments/<spec-name>/`
- Each fragment contains: source code, expectations (separated by `---`), and the generated X86 IR
- Uses incremental regeneration: tracks spec count and test count in a `.spec_count` flag file, only regenerates when specs change or the compiler binary is newer
- Pre-compiles executables alongside fragments for caching
- Runs fragment generation in parallel using `Parallel.ForEach`

**Fragment file format:**
```
// Test: test-name
<maxon source code>
---
ExitCode: 42
Stdout: ```
expected output
```
RequiredMlir: ```
expected IR
```
---
<generated X86 IR from compilation>
---
```

**TestRunner** (`maxon-sharp/Testing/TestRunner.cs`):
- Loads all `.test` fragments from `/specs/fragments/`
- Supports `--filter=PATTERN` with regex matching
- Runs tests in parallel using configurable worker count (default: `ProcessorCount / 2`)
- For each test:
  - Compiles the fragment source to a temp executable (or uses a cached pre-compiled one)
  - For `CompilerErrorExpectation`: verifies compilation fails with the expected stderr message
  - For `SuccessExpectation`: verifies exit code, stdout, and optionally required MLIR patterns
- Uses Windows Job Objects (`WindowsJobObject`) to ensure child processes are killed on timeout (1 second)
- Cleans up generated `.exe` files after the run

### Test Expectations

Tests support two expectation types:

**SuccessExpectation:**
- `ExitCode` -- expected process exit code
- `Stdout` -- expected standard output (exact match after trimming)
- `ExpectedMlir` -- reference IR (auto-generated, not checked)
- `RequiredMlir` -- IR that must match exactly (verified during test runs)

**CompilerErrorExpectation:**
- `ExpectedStderr` -- expected compiler error output, normalized (CRLF->LF, backslash->forward slash, trimmed)

### Unit Tests

**File:** `maxon-sharp/Testing/UnitTests.cs`

Unit tests verify compiler internals that cannot be tested through spec files, particularly IR generation patterns and PE structure. They compile Maxon source programmatically and inspect the generated IR and binary output.

**Test categories:**

| Category | Tests | What They Verify |
|----------|-------|------------------|
| Stack probing | 1 | Large struct allocations (>4KB) call `__chkstk` and survive recursion |
| Managed memory | 4 | Heap arrays generate `heap_free`, nested scopes clean up, loops generate `heap_realloc`, array literals get freed |
| Rdata and COW | 4 | Constant arrays use `.rdata`, mutation triggers copy-on-write, non-constant arrays use heap |
| Managed strings | 6 | String literals in `.rdata`, reassignment cleanup, substring retention, SSO, loop concatenation, literal deduplication |

Unit tests use helper methods to:
- Compile source and capture IR (`CompileWithIr`)
- Run executables with timeout and job object management (`RunExecutable`)
- Parse PE section headers and read section data (`ParsePeSections`, `ReadPeSectionData`)
- Verify `.rdata` contains expected byte patterns (`VerifyRdataContains`)

Many unit tests document expected future behavior -- they exercise features (like managed memory, string management, ownership) that are not yet implemented in the current pipeline, serving as a forward-looking test suite.

### WindowsJobObject

**File:** `maxon-sharp/Testing/WindowsJobObject.cs`

A Win32 interop wrapper that creates a Windows Job Object configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. When the job object is disposed, all processes assigned to it are terminated. This prevents zombie test processes from lingering after timeouts.


## Key Design Decisions

### 1. No LLVM Dependency

The compiler generates native x86-64 machine code directly, with no dependency on LLVM, Cranelift, or any external code generation library. The `X86CodeEmitter` encodes instructions byte-by-byte, and the `PeWriter` constructs PE executables from scratch. This gives full control over the output and simplifies the build/deployment story.

### 2. MLIR-Inspired Architecture Without MLIR

The compiler borrows MLIR's conceptual model -- typed SSA values, operations with operands/results/attributes, blocks, regions, modules, and progressive dialect lowering -- but implements everything as plain C# classes. This avoids the complexity of binding to the real MLIR C++ library while preserving the architectural benefits of progressive lowering.

### 3. Direct-to-MLIR Parsing (No AST)

The parser emits Maxon dialect operations directly instead of constructing a separate AST. This eliminates an entire intermediate data structure and the corresponding AST-to-IR lowering pass. The Maxon dialect itself serves as the "AST equivalent," carrying all high-level semantic information.

### 4. In-Place Module Transformation

All pipeline passes and dialect conversions operate on the `MlirModule` in place. Conversions walk blocks and replace operations within them. This avoids allocating new modules at each stage and keeps the pipeline simple.

### 5. Numbered Source Files

Compiler source files are prefixed with stage numbers (`0-Compiler.cs`, `1-Lexer.cs`, ..., `5-PeWriter.cs`). This makes the compilation pipeline order immediately visible in file listings and reinforces the sequential nature of the pipeline.

### 6. Thread-Local MlirContext

The `MlirContext` uses `[ThreadStatic]` to provide an implicit current context. This allows operation constructors to create result values without explicitly passing a context parameter, keeping the operation construction code clean. The `PushScope()` pattern ensures proper cleanup.

### 7. Spec-Driven Development

Language features are specified in markdown files that serve triple duty: as human-readable documentation, as the source of truth for test cases, and as a reference for expected compiler behavior. The spec-test pipeline automatically extracts tests and verifies the compiler matches the specification.

### 8. Structured Error Codes

All compiler errors use a numeric code scheme (E1001-E6001) grouped by compilation stage. This makes errors programmatically identifiable, filterable by stage, and stable across compiler versions.

### 9. Flat Namespace Structure

Despite having files in subdirectories (`MLIR/Core/`, `MLIR/Dialects/`, etc.), the project uses a flat namespace structure: `MaxonSharp.Compiler.Mlir.Core`, `MaxonSharp.Compiler.Mlir.Dialects`, etc. A global suppression (`GlobalSuppressions.cs`) silences the IDE warning about namespace-folder mismatches.
