# Debugging the Compiler

## Verbosity Levels

```bash
maxon compile file.maxon -v      # Progress, phase names
maxon compile file.maxon -vv     # Detailed info, timing
maxon compile file.maxon -vvv    # Trace, individual items
```

## Log Phases

Output is tagged with phases: `[Lexer]`, `[Parser]`, `[Semantic]`, `[MIR]`, `[Opt]`, `[RegAlloc]`, `[x86]`, `[PE]`, `[ELF]`

## Debugging Workflow

1. **Reproduce** with a minimal test case
2. **Inspect MIR** - `maxon compile file.maxon --emit-ir` (writes `.ir` file)
3. **Disassemble** if MIR looks correct - `llvm-project/bin/llvm-objdump -d file.exe`
4. **Add debug output** - Use `logger.trace(LogPhase::..., ...)` in C++ code
5. **Rebuild and test** - `make compiler && maxon compile file.maxon -vvv`

## Common Issues

| Issue | Root Cause | Solution |
|-------|------------|----------|
| GEP missing element type | Pointer without element type info | Check `MIRType` has proper `elementType` |
| Register ID collisions | Conflicting `MIRValueKind` values | Ensure unique IDs for each value |
| Optimizer breaks code | Value replacement without checking uses | Verify all uses are updated |
| Struct params fail | Not passed by pointer | Check ABI handling in codegen |
| Alloca confusion | Treating alloca like parameter | Distinguish in x86 codegen |

## Debugging Tools

- **Emit MIR**: `--emit-ir` flag to inspect intermediate representation
- **Disassemble**: `llvm-objdump -d` to check generated machine code
- **Add logging**: Use `logger.trace(LogPhase::X86, "message: {}", value);`
- **Compare optimization**: Test with and without `-O` to isolate optimizer issues

## LLVM Tools for Debugging

Pre-built LLVM tools in `llvm-project/bin/` are useful for analyzing Maxon-generated executables:

```bash
# Disassemble an executable
llvm-project/bin/llvm-objdump -d file.exe

# View object file symbols
llvm-project/bin/llvm-nm file.obj

# Read DWARF debug info
llvm-project/bin/llvm-dwarfdump file.exe
```

## LLVM Source Reference

The LLVM source code is available in `llvm-source/` as a reference for compiler implementation techniques. **Maxon has its own native x86-64 backend** and does not use LLVM for code generation.

Use LLVM source as a reference for understanding:

- **x86 instruction encoding**: `llvm-source/llvm/lib/Target/X86/`
- **Optimization techniques**: `llvm-source/llvm/lib/Transforms/`
- **ABI/calling conventions**: `llvm-source/clang/lib/CodeGen/`
- **DWARF debug info**: `llvm-source/llvm/lib/CodeGen/AsmPrinter/`
- **ELF/PE executable formats**: `llvm-source/lld/`
