# DWARF-Only Debug Implementation Plan

## Strategy: DWARF Everywhere + Windows Tooling

**Rationale:** DWARF is required for Linux anyway, so implement it properly once. Use modern LLDB tooling on Windows for excellent VS Code debugging support.

---

## Windows Debugging with DWARF

### The Good News

Modern Windows debugging with DWARF is **actually quite good** thanks to LLDB:

✅ **[CodeLLDB extension](https://marketplace.visualstudio.com/items?itemName=vadimcn.vscode-lldb)** - Excellent VS Code debugger powered by LLDB
✅ **Native DWARF support** - LLDB reads DWARF directly, no conversion needed
✅ **Cross-platform** - Same debugging experience on Windows and Linux
✅ **Active development** - Better than LLDB's PDB support (which is buggy)
✅ **Full features** - Breakpoints, variables, call stacks, stepping all work

### What Works

- ✅ Set breakpoints by line number
- ✅ Step through code (step in/out/over)
- ✅ Inspect variables in watch window
- ✅ View call stacks with full symbols
- ✅ Evaluate expressions
- ✅ Conditional breakpoints
- ✅ Pretty printing of complex types

### What Doesn't Work

- ❌ Visual Studio (doesn't read DWARF, requires PDB)
- ❌ WinDbg (doesn't read DWARF)
- ⚠️ Edit & Continue (not supported in LLDB)

**Verdict:** If VS Code is the primary IDE (which it is for most modern development), DWARF + LLDB is perfectly fine on Windows.

---

## Optional: PDB Conversion Workflow

For users who need Visual Studio or WinDbg, provide optional PDB conversion:

### Using cv2pdb

[cv2pdb](https://github.com/rainers/cv2pdb) converts DWARF → PDB automatically:

```bash
# Automatic conversion during build
maxon compile program.maxon --debug
cv2pdb program.exe  # Creates program.pdb
```

**Integration in compiler:**
```bash
maxon compile program.maxon --debug --pdb
# Automatically runs cv2pdb if available
```

**Build script integration:**
```makefile
%.exe: %.maxon
	maxon compile $< --debug
	@if command -v cv2pdb >/dev/null; then \
		cv2pdb $@; \
	fi
```

**Benefits:**
- ✅ Zero maintenance - cv2pdb is maintained externally
- ✅ Optional - Only needed for VS/WinDbg users
- ✅ Automated - Can be integrated into build
- ✅ No code complexity - Just shell out to external tool

---

## Revised Implementation Plan

### Phase 1: Core DWARF Integration (Week 1)

**Priority: CRITICAL - Nothing works without this**

#### 1.1 Integrate DWARF Generator into Pipeline
- [ ] Add `DwarfGenerator` instance to `MIRCodeGenerator`
- [ ] Initialize in constructor when `debugInfo` flag is true
- [ ] Call `setCompileUnit()` with source file info
- [ ] Call `addStandardTypes()` for basic types

**Files:**
- `maxon-bin/codegen_mir.h` - Add member variable
- `maxon-bin/codegen_mir.cpp` - Initialize generator

#### 1.2 Collect Debug Info During Code Generation
- [ ] In `generateFunction()`: Add function with addresses
- [ ] Track function start/end addresses after x86 codegen
- [ ] Add parameters with stack offsets
- [ ] Add local variables with stack offsets
- [ ] Add line entries for each statement

**Files:**
- `maxon-bin/codegen_mir/codegen_mir_function.cpp`
- `maxon-bin/codegen_mir/codegen_mir_stmt.cpp`

#### 1.3 Add Debug Sections to Executables

**PE Writer (Windows):**
- [ ] Add `.debug_info` section
- [ ] Add `.debug_abbrev` section
- [ ] Add `.debug_line` section
- [ ] Add `.debug_str` section
- [ ] Add `.debug_aranges` section
- [ ] Set section characteristics: `IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ | IMAGE_SCN_MEM_DISCARDABLE`

**ELF Writer (Linux):**
- [ ] Add `.debug_info` section (SHT_PROGBITS, flags=0)
- [ ] Add `.debug_abbrev` section
- [ ] Add `.debug_line` section
- [ ] Add `.debug_str` section
- [ ] Add `.debug_aranges` section

**Files:**
- `maxon-bin/backend/pe_writer.cpp`
- `maxon-bin/backend/elf_writer.cpp`

#### 1.4 Testing
- [ ] Test on Windows with VS Code + CodeLLDB extension
- [ ] Test on Linux with GDB
- [ ] Verify breakpoints work by line
- [ ] Verify simple variable inspection
- [ ] Run `dwarfdump --verify` for validation

**Expected Result:** Basic debugging works - breakpoints, stepping, simple variables

---

### Phase 2: Call Frame Information (Week 2)

**Priority: HIGH - Stack unwinding is critical**

#### 2.1 Design Frame Info Tracking
```cpp
type FunctionFrameInfo {
    uint64_t startAddress;
    uint64_t endAddress;
    int32_t frameSize;          // Total stack frame size
    int32_t prologueEnd;        // Offset where prologue ends
    int32_t epilogueBegin;      // Offset where epilogue begins
    std::vector<int> savedRegs; // Which registers are saved
};
```

#### 2.2 Collect Frame Info During x86 Codegen
- [ ] Track stack frame size for each function
- [ ] Mark prologue end (after `push rbp; mov rbp, rsp; sub rsp, X`)
- [ ] Mark epilogue start (before `mov rsp, rbp; pop rbp; ret`)
- [ ] Track which callee-saved registers are pushed

**Files:**
- `maxon-bin/backend/x86_codegen.cpp`

#### 2.3 Implement .debug_frame Generator

Create new files:
- `maxon-bin/backend/dwarf_frame.h`
- `maxon-bin/backend/dwarf_frame.cpp`

**Key structures:**
```cpp
class DwarfFrameGenerator {
public:
    void addFunction(uint64_t start, uint64_t end, const FrameInfo& info);
    void generate();
    const std::vector<uint8_t>& getDebugFrame() const;

private:
    void generateCIE();  // Common Information Entry
    void generateFDE(const FunctionFrameInfo& func);  // Frame Description Entry
};
```

**CIE (Common Information Entry):**
- Return address column: 16 (x86-64 RA)
- Code alignment: 1
- Data alignment: -8
- Initial rules: CFA = RSP + 8

**FDE (Frame Description Entry) per function:**
- Location range
- `DW_CFA_def_cfa_offset` for stack changes
- `DW_CFA_offset` for saved registers

#### 2.4 Integrate .debug_frame
- [ ] Add to PE writer as `.debug_frame` section
- [ ] Add to ELF writer as `.debug_frame` section
- [ ] For ELF: Also generate `.eh_frame` (same format, different usage)

#### 2.5 Testing
- [ ] Test `bt` (backtrace) in GDB
- [ ] Test call stack in VS Code debugger
- [ ] Test with recursive functions
- [ ] Verify `readelf --debug-dump=frames` shows correct info

**Expected Result:** Reliable stack unwinding, full call stacks

---

### Phase 3: Complex Types (Week 3)

**Priority: MEDIUM - Needed for real programs**

#### 3.1 Extend DWARF Generator

Add to `maxon-bin/backend/dwarf.h`:
```cpp
class DwarfGenerator {
public:
    // Existing methods...

    // New methods:
    uint32_t addPointerType(uint32_t pointeeType);
    uint32_t addArrayType(uint32_t elementType, uint32_t elementCount);

    // Struct support
    uint32_t beginStructType(const std::string& name, uint32_t byteSize);
    void addStructMember(const std::string& name, uint32_t typeIndex, uint32_t offset);
    void endStructType();

private:
    std::vector<DebugType> pendingTypes;
    uint32_t currentStructType = 0;
};
```

#### 3.2 Collect Type Info from AST
- [ ] Extract type definitions from `ProgramAST`
- [ ] Process `StructAST` nodes
- [ ] Handle nested structs
- [ ] Handle arrays of structs
- [ ] Map Maxon type strings to DWARF type indices

**Files:**
- `maxon-bin/codegen_mir/codegen_mir_function.cpp`

#### 3.3 Generate Type DIEs
- [ ] Implement `DW_TAG_pointer_type` generation
- [ ] Implement `DW_TAG_array_type` with `DW_TAG_subrange_type`
- [ ] Implement `DW_TAG_structure_type` with `DW_TAG_member`
- [ ] Add abbreviations for new types
- [ ] Track DIE offsets for type references

**Files:**
- `maxon-bin/backend/dwarf.cpp`

#### 3.4 Testing
- [ ] Create test with structs, arrays, pointers
- [ ] Test `print mystruct.field` in debugger
- [ ] Test `print myarray[5]`
- [ ] Test `print *myptr`
- [ ] Test nested structures

**Expected Result:** Full type inspection for all Maxon types

---

### Phase 4: Lexical Scoping (Week 4)

**Priority: LOW - Nice to have**

#### 4.1 Track Scopes During Codegen
- [ ] Push/pop scope stack during statement generation
- [ ] Track variable lifetimes
- [ ] Record code address ranges for each scope

#### 4.2 Add Lexical Blocks to DWARF
- [ ] Add `addLexicalBlock()` method
- [ ] Generate `DW_TAG_lexical_block` DIEs
- [ ] Nest variable DIEs inside block DIEs
- [ ] Set proper address ranges

#### 4.3 Testing
- [ ] Test variable visibility in nested scopes
- [ ] Test variable shadowing
- [ ] Verify out-of-scope variables hidden

**Expected Result:** Debuggers show only in-scope variables

---

## Simplified File Structure

### New Files
```
maxon-bin/backend/
├── dwarf_frame.h          # Call frame info (Phase 2)
└── dwarf_frame.cpp        # .debug_frame generation (Phase 2)
```

### Modified Files
```
maxon-bin/
├── backend/
│   ├── dwarf.h            # Add complex type methods (Phase 3)
│   ├── dwarf.cpp          # Implement new features (Phase 3)
│   ├── pe_writer.cpp      # Add debug sections (Phase 1)
│   ├── elf_writer.cpp     # Add debug sections (Phase 1)
│   └── x86_codegen.cpp    # Collect frame info (Phase 2)
├── codegen_mir.h          # Add DwarfGenerator member (Phase 1)
├── codegen_mir.cpp        # Initialize DWARF (Phase 1)
└── codegen_mir/
    ├── codegen_mir_function.cpp  # Collect debug info (Phases 1, 2, 3)
    └── codegen_mir_stmt.cpp      # Track scopes (Phase 4)
```

---

## VS Code Debugging Setup

### For Windows Users

**1. Install CodeLLDB extension:**
```
ext install vadimcn.vscode-lldb
```

**2. Create `.vscode/launch.json`:**
```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "type": "lldb",
            "request": "launch",
            "name": "Debug Maxon Program",
            "program": "${workspaceFolder}/program.exe",
            "args": [],
            "cwd": "${workspaceFolder}",
            "preLaunchTask": "build"
        }
    ]
}
```

**3. Works immediately** - No conversion, no extra steps

### For Visual Studio Users (Optional)

**Option 1: Use cv2pdb**
```bash
# Add to build script
maxon compile program.maxon --debug
cv2pdb program.exe
# Now works in Visual Studio
```

**Option 2: Use LLDB-MI adapter**
- Install LLDB
- Configure Visual Studio to use LLDB instead of native debugger
- Read DWARF directly

---

## Success Criteria

### Phase 1 Complete:
- [x] Compile with `--debug` flag
- [x] Set breakpoints by line in VS Code
- [x] Step through code
- [x] Inspect simple variables (int, float)
- [x] Function names in debugger
- [x] `dwarfdump --verify` passes

### Phase 2 Complete:
- [x] `bt` shows full call stack
- [x] Stack unwinding works in recursive calls
- [x] Call stack visible in VS Code
- [x] Debugger doesn't crash on stack operations

### Phase 3 Complete:
- [x] Inspect type members
- [x] Index into arrays
- [x] Dereference pointers
- [x] Complex nested types work

### Phase 4 Complete:
- [x] Scoped variables show/hide correctly
- [x] Variable shadowing works
- [x] Watch window shows only in-scope vars

---

## Timeline

| Phase | Duration | Deliverable |
|-------|----------|-------------|
| Phase 1 | 3-4 days | Basic debugging works |
| Phase 2 | 3-4 days | Stack unwinding works |
| Phase 3 | 2-3 days | Complex types work |
| Phase 4 | 1-2 days | Scoping works |
| **Total** | **9-13 days** | **Complete DWARF implementation** |

---

## Testing Workflow

### Manual Testing
```bash
# 1. Compile with debug info
maxon compile test.maxon --debug

# 2. Verify DWARF is present
dwarfdump --verify test.exe

# 3. Debug in VS Code (Windows)
code .
# Set breakpoint, press F5

# 4. Debug in GDB (Linux)
gdb test.exe
(gdb) break main
(gdb) run
(gdb) print myvar
```

### Automated Testing
```bash
# Create test suite
language-tests/debug/test_basic.maxon
language-tests/debug/test_structs.maxon
language-tests/debug/test_callstack.maxon

# Script to verify debug info
scripts/verify-debug.sh:
  - Compile with --debug
  - Run dwarfdump --verify
  - Check for expected sections
  - Validate DIE structure
```

---

## Advantages of DWARF-Only Approach

### ✅ Simplicity
- Single debug format to implement
- Single code path to maintain
- Easier to test and debug

### ✅ Cross-Platform
- Same debugging experience on Windows and Linux
- Same debug info format everywhere
- Same tooling (LLDB) on all platforms

### ✅ Better Tooling
- LLDB's DWARF support is mature and stable
- GDB has decades of DWARF experience
- Extensive validation tools (dwarfdump, eu-readelf)

### ✅ Modern Workflow
- VS Code is the modern development environment
- CodeLLDB is actively maintained
- LLDB is the future (Apple, LLVM project)

### ✅ No Vendor Lock-in
- DWARF is an open standard
- Not tied to Microsoft tooling
- Works with any debugger that supports DWARF

---

## Optional PDB Support

**If needed later, add as post-processing:**

```cpp
// In main.cpp compile command
if (options.debugInfo && options.generatePDB) {
    // Try to run cv2pdb
    if (findExecutable("cv2pdb")) {
        runCommand("cv2pdb " + outputExe);
        std::cout << "Generated PDB: " << baseName << ".pdb" << std::endl;
    } else {
        std::cerr << "Warning: cv2pdb not found, PDB not generated" << std::endl;
        std::cerr << "Install from: https://github.com/rainers/cv2pdb" << std::endl;
    }
}
```

**Benefits:**
- Zero maintenance burden
- Works with Visual Studio when needed
- Optional feature, not required

---

## Conclusion

**DWARF-only is the right choice:**

1. ✅ Required for Linux anyway
2. ✅ Works great on Windows with modern tooling (VS Code + LLDB)
3. ✅ Single implementation to maintain
4. ✅ Simpler, cleaner codebase
5. ✅ Optional PDB conversion available when needed

**Total effort: ~9-13 days for complete DWARF support**

This gives excellent debugging on both platforms with minimal maintenance burden.

---

## Resources

### VS Code + LLDB
- [CodeLLDB Extension](https://marketplace.visualstudio.com/items?itemName=vadimcn.vscode-lldb)
- [CodeLLDB Windows Support](https://github.com/vadimcn/codelldb/wiki/Windows)

### DWARF Specification
- [DWARF 4 Standard](http://dwarfstd.org/doc/DWARF4.pdf)
- Current implementation: `maxon-bin/backend/dwarf.cpp`

### Optional PDB Conversion
- [cv2pdb](https://github.com/rainers/cv2pdb) - DWARF to PDB converter

### Validation Tools
- `dwarfdump` - Validate DWARF structure
- `llvm-dwarfdump` - LLVM's DWARF dumper
- `readelf --debug-dump` - Linux DWARF inspection
