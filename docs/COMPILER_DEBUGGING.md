# Debugging the Maxon MIR Compiler

This document summarizes techniques and lessons learned for debugging the Maxon compiler's MIR (Mid-level Intermediate Representation) backend and x86-64 code generator. The compiler is brand new and still being implemented so there
could be bugs in any part.

## Architecture Overview

The compilation pipeline is:
1. **Lexer/Parser** → AST
2. **Semantic Analyzer** → Type checking, scope analysis
3. **MIR Code Generator** (`codegen_mir*.cpp`) → MIR instructions
4. **MIR Optimizer** (`optimizer.cpp`) → Optimized MIR
5. **x86 Code Generator** (`x86_codegen.cpp`) → Native x86-64 machine code

## Key Debugging Tools

### 1. Verbose Logging

The compiler has built-in verbose logging at three levels:

```bash
maxon compile file.maxon -v      # Level 1: Progress, phase names
maxon compile file.maxon -vv     # Level 2: Detailed info, timing
maxon compile file.maxon -vvv    # Level 3: Trace, individual items
```

**Level 1 (`-v`)** shows phase progress:
```
=== Maxon Compiler ===
[Lexer] Tokenized: 43 tokens from file.maxon
[Parser] Parsed: 1 function(s), 0 struct(s)
[Semantic] Semantic analysis complete
[MIR] Generating MIR...
[Opt] Dead code elimination complete
[x86] Generating executable...
Output: file.exe
```

**Level 2 (`-vv`)** adds timing and counts:
```
[Lexer]   Tokenization time: 45µs
[Lexer]   Token breakdown: 11 keywords, 10 identifiers, 6 numbers
[Parser]   Parse time: 82µs
[Semantic]   Analysis time: 47µs
[MIR]   Pass 1: Creating struct types (0 structs)
[MIR]   Pass 2: Creating function declarations (1 functions)
[Opt]   Dead code elimination time: 139µs
```

**Level 3 (`-vvv`)** shows individual items (useful for debugging):
```
[Lexer]     Token[0]: type=0 value='function' line=1 col=1
[Lexer]     Token[1]: type=1 value='main' line=1 col=10
[Parser]     Function: main -> int (0 params)
[Semantic]     Dependency resolution iteration 1
[MIR]     Declaring function: main -> int
[MIR]     Target triple: x86_64-pc-windows-msvc
```

**Log phase prefixes:**
- `[Lexer]` - Tokenization
- `[Parser]` - AST generation
- `[Semantic]` - Type checking, scope analysis
- `[MIR]` - MIR code generation
- `[Opt]` - Optimization passes
- `[RegAlloc]` - Register allocation
- `[x86]` - x86-64 code generation
- `[PE]` / `[ELF]` - Executable writing

### 2. Emit MIR Output
```bash
maxon compile file.maxon --emit-ir
```
This writes the MIR to `file.ir`. Useful for seeing the intermediate representation before native code generation.

**Without optimization:**
```bash
maxon compile file.maxon --emit-ir   # optimization is OFF by default
maxon compile file.maxon --emit-ir -O  # optimization ON
```

### 3. Disassemble Generated Executables
```bash
llvm-project/bin/llvm-objdump -d file.exe | less
```

To find a specific function:
```bash
llvm-project/bin/llvm-objdump -d file.exe | grep -A50 "<functionName>:"
```

To disassemble starting at a specific address:
```bash
llvm-project/bin/llvm-objdump -d file.exe | grep -A30 "1400010dd:"
```

### 4. Run Test Fragments
```bash
maxon test-fragments           # Run all language tests
maxon test-fragments -v        # With progress output
maxon test-fragments -vv       # With detailed output
```

### 5. Run Backend Unit Tests
```bash
make backend-test              # Build and run all backend tests (one line per test)
```

Output shows one line per test with pass/fail status. To see detailed output for a specific test:
```bash
cd maxon-bin/tests/build
./test_x86_codegen.exe                    # Detailed output with all assertions
./test_x86_codegen.exe --reporter compact # Summary only
./test_x86_codegen.exe "[load]"           # Run tests matching tag [load]
```

Unit tests are located in `maxon-bin/tests/` and cover all stages of the compilation pipeline:

| Test File | Description |
|-----------|-------------|
| `test_mir.cpp` | MIR data structures, types, instructions, control flow |
| `test_mir_parser.cpp` | MIR textual format parsing |
| `test_codegen_mir.cpp` | AST-to-MIR code generation (expressions, statements, functions) |
| `test_optimizer.cpp` | All optimization passes |
| `test_regalloc.cpp` | Liveness analysis, linear-scan allocation, spilling |
| `test_x86_encoding.cpp` | x86-64 instruction encoding, ModR/M, SIB, REX |
| `test_x86_codegen.cpp` | X86CodeGen instruction selection, calling conventions, large struct returns |
| `test_executable_writers.cpp` | ELF/PE structure generation and validation |
| `test_dwarf.cpp` | DWARF debug info generation |

**When to use unit tests vs language tests:**
- **Unit tests** - Debug specific backend components without full compilation pipeline
- **Language tests** - Verify end-to-end behavior of language features

**Using unit tests to fix issues:**

When a bug is identified (e.g., large struct returns crash at runtime), the workflow is:

1. **Write a failing unit test** that isolates the specific behavior:
   ```cpp
   TEST_CASE("X86CodeGen: large struct return - parameter shift", "[x86-codegen][large-struct]") {
       // Create MIR with large struct return and parameters
       // Generate x86 code
       // CHECK for expected machine code patterns
   }
   ```

2. **Run just that test** to confirm it fails:
   ```bash
   cd maxon-bin/tests/build && ./test_x86_codegen.exe "[large-struct]"
   ```

3. **Fix the code** until the test passes

4. **Run all backend tests** to ensure no regressions:
   ```bash
   make backend-test
   ```

This approach lets you debug complex issues (like ABI compliance) without needing the full compilation pipeline, and the test documents the expected behavior for future reference.

### 6. Debug Output in Code

The compiler uses the `Logger` class (in `logger.h`) for structured output. When you need more detail than `-vvv` provides, add logging calls at key locations:

**Adding temporary debug output:**
```cpp
// In any file that includes compiler.h or logger.h:
logger.trace(LogPhase::Parser, "varName='", varName, "' type='", type, "'");
logger.trace(LogPhase::MIR, "createLoad %", result->regId, " = load ptr %", ptr->regId);
logger.trace(LogPhase::x86, "loadValue regId=", value->regId, " kind=", static_cast<int>(value->kind));
```

**For quick debugging without Logger access:**
```cpp
std::cerr << "[DEBUG] varName='" << varName << "' type='" << type << "'\n";
```

**Key locations for debugging:**

| Component | File | What to log |
|-----------|------|-------------|
| MIR Generation | `codegen_mir/codegen_mir_expr.cpp` | Variable lookups, type conversions |
| MIR Builder | `mir/mir_builder.cpp` | Instruction creation, register allocation |
| x86 CodeGen | `backend/x86_codegen.cpp` | Instruction selection, register assignments |
| Optimizer | `mir/optimizer.cpp` | Value replacements, instruction deletions |

## Common Issues and Debugging Strategies

### Issue 1: GEP (GetElementPtr) Missing Element Type

**Symptom:** x86 codegen crashes or generates wrong offsets for struct field access.

**Debug:** Check `inst->elementType` in GEP instructions. It should contain the struct type.

**Fix location:** `mir_builder.cpp` - `createGEP()` and `createStructGEP()`

### Issue 2: Register ID Collisions

**Symptom:** Two different values have the same `%N` in MIR output.

**Root cause:** Parameters use `regId = index` (0, 1, 2...) while virtual registers also start at 0. They have different `MIRValueKind` but share the regId namespace in display.

**Debug:** Print both `regId` AND `kind` when tracing values:
```cpp
std::cerr << "%" << value->regId << " (kind=" << static_cast<int>(value->kind) << ")\n";
```

**MIRValueKind values:**
- 0 = VirtualReg
- 1 = Parameter
- 2 = ConstantInt
- 3 = ConstantFloat
- etc.

### Issue 3: Optimizer Replacing Values Incorrectly

**Symptom:** MIR shows different operands than what debug output showed during generation.

**Root cause:** `RedundantLoadStoreEliminationPass` replaces loads from allocas with the stored value.

**Debug:** Compare MIR before and after optimization, or disable optimization temporarily.

**Key optimizer code:** `optimizer.cpp` line ~1183:
```cpp
// If we have a recent store to this pointer, use the stored value
auto it = lastStore.find(ptr);
if (it != lastStore.end()) {
    loadReplacements[inst.get()] = it->second;
}
```

### Issue 4: Struct Parameters Passed by Pointer

**Symptom:** Accessing struct parameter fields returns garbage or crashes.

**Root cause:** Struct parameters are passed by pointer (ABI requirement), but field access code may treat them as direct values.

**Debug:** Check if parameter is tracked in `structParameters` set:
```cpp
std::cerr << "isStructParam=" << isStructParameter(varName) << "\n";
```

**Fix location:** `codegen_mir_function.cpp` and `codegen_mir_expr.cpp`

### Issue 5: x86 Alloca vs Parameter Confusion

**Symptom:** Parameter values treated as alloca addresses (getting `leaq` instead of value load).

**Root cause:** `allocaRegs.count(regId)` returns true for parameters because regIds can collide.

**Debug:** In `loadValue()`, check the value's `kind` before checking `allocaRegs`:
```cpp
case mir::MIRValueKind::Parameter: {
    // Parameters are never allocas
    X86Reg reg = getAllocatedReg(value);
    ...
}
case mir::MIRValueKind::VirtualReg: {
    // Only VirtualRegs can be allocas
    if (regAlloc.allocaRegs.count(value->regId)) {
        ...
    }
}
```

## MIR Instruction Reference

### Key MIR Opcodes
- `Alloca` - Stack allocation, result is pointer to allocated memory
- `Load` - Load value from pointer
- `Store` - Store value to pointer
- `GetElementPtr` - Compute address of struct field or array element
- `Call` - Function call

### MIR Value Kinds
```cpp
enum class MIRValueKind {
    VirtualReg,    // Temporary value (result of instruction)
    Parameter,     // Function parameter
    ConstantInt,   // Integer constant
    ConstantFloat, // Float constant
    ConstantNull,  // Null pointer
    Global,        // Global variable
    BasicBlockRef  // Branch target
};
```

## x86 Code Generator Key Structures

### Register Allocation (`regAlloc`)
```cpp
struct RegAllocation {
    std::map<uint32_t, X86Reg> regMap;      // regId -> physical register
    std::map<uint32_t, int32_t> stackSlots; // regId -> stack offset from RBP
    std::set<uint32_t> allocaRegs;          // regIds that are alloca results
    uint32_t frameSize;                      // Total stack frame size
    std::vector<X86Reg> usedCalleeSaved;    // Callee-saved regs to preserve
};
```

### Windows x64 Calling Convention
- First 4 integer/pointer args: RCX, RDX, R8, R9
- Return value: RAX
- Shadow space: 32 bytes above return address
- Callee-saved: RBX, RSI, RDI, R12-R15, RBP

## Debugging Workflow

1. **Reproduce with minimal test case:**
   ```bash
   echo 'returns int ... end "main"' > temp/test.maxon
   maxon compile temp/test.maxon && ./temp/test.exe
   echo $?  # Check exit code
   ```

2. **Check MIR output:**
   ```bash
   maxon compile temp/test.maxon --emit-ir
   cat temp/test.ir
   ```

3. **Disassemble if MIR looks correct:**
   ```bash
   llvm-project/bin/llvm-objdump -d temp/test.exe | grep -A30 "<main>:"
   ```

4. **Add debug output to trace the issue:**
   - Start at high level (MIR generation)
   - Work down to x86 generation if needed

5. **Rebuild and test:**
   ```bash
   make compiler && maxon compile temp/test.maxon && ./temp/test.exe
   ```

## Files Reference

| File | Purpose |
|------|---------|
| `codegen_mir.cpp` | Main MIR code generator, type handling |
| `codegen_mir/codegen_mir_function.cpp` | Function prologue, parameter handling |
| `codegen_mir/codegen_mir_expr.cpp` | Expression code generation, field access |
| `codegen_mir/codegen_mir_stmt.cpp` | Statement code generation |
| `mir/mir.h` | MIR data structures |
| `mir/mir.cpp` | MIR implementation, toString() |
| `mir/mir_builder.cpp` | MIR instruction creation helpers |
| `mir/optimizer.cpp` | MIR optimization passes |
| `backend/x86_codegen.cpp` | x86-64 native code generation |

## Tips

1. **Always print `kind` with `regId`** - regIds are not unique across different value kinds.

2. **Check optimizer effects** - Many bugs appear after optimization. Test with and without `-O`.


3. **Use `--emit-ir` liberally** - The MIR is much easier to read than x86 assembly.

4. **Windows ABI quirks** - Struct returns, shadow space, and parameter passing all have Windows-specific rules.

5. **Alloca semantics** - An `alloca` result is a POINTER to stack memory. Loading from it gives you the stored value. Using it directly in GEP treats it as the base address.

