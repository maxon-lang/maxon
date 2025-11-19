# Fearless FFI Implementation Plan for Maxon

## Executive Summary

This document outlines a comprehensive plan to implement **Fearless FFI** (Foreign Function Interface) in Maxon, inspired by Vale's approach to memory-safe interoperability with unsafe languages like C. Fearless FFI protects Maxon's safe memory from corruption by C code through memory isolation, reference scrambling, separate stacks, and optional sandboxing.

## Background: Vale's Fearless FFI

Vale's Fearless FFI addresses the "leaky unsafe" problem where bugs in unsafe FFI code can corrupt safe language memory and cause difficult-to-debug issues. The key mechanisms are:

1. **Memory Separation**: Safe and unsafe memory are kept completely separate
2. **Reference Scrambling**: References passed to C are scrambled to prevent accidental dereferencing
3. **Separate Stacks**: C code runs on a separate stack to prevent buffer overruns corrupting caller memory
4. **Message Passing**: Data is copied (not shared) between safe and unsafe code
5. **Sandboxing**: Optional isolation via WebAssembly or subprocesses for untrusted code
6. **Whitelisting**: Compile-time permission system for dependencies using FFI

## Current State of Maxon FFI

### What Maxon Has Today

✅ **Basic Extern Functions**
- `extern` keyword for declaring external C functions
- Parser support in `parser.cpp` (lines 723-843)
- Semantic analysis support in `semantic_analyzer.cpp`
- Codegen creates extern linkage for C functions
- Working examples in `stdlib/sys/windows.maxon`:
  ```maxon
  extern function GetStdHandle(nStdHandle int) ptr
  extern function WriteFile(hFile ptr, lpBuffer ptr, ...) int
  ```

✅ **Basic Type Mapping**
- `int` → LLVM i32
- `ptr` → LLVM opaque pointer
- `float` → LLVM double
- Arrays with length tracking

✅ **Namespace System**
- Functions can be organized in namespaces
- Qualified names for C interop

### What Maxon Lacks

❌ **Struct Passing** - No structured data interop with C
❌ **Memory Isolation** - C and Maxon share the same heap/stack
❌ **Reference Protection** - Raw pointers can be corrupted by C
❌ **Stack Separation** - C buffer overruns can corrupt Maxon stack frames
❌ **Sandboxing** - No protection from malicious C code
❌ **FFI Whitelisting** - No permission system for dependencies

## Implementation Plan

### Phase 1: Struct Passing (Data Interop)
**Goal**: Pass structured data from Maxon to C safely

#### 1.1 Struct Definitions (Copy Semantics)
Add struct support for FFI data passing:
```maxon
struct Vec3
    x int
    y int
    z int
end 'Vec3'

extern function sum(v Vec3) int

function main() int
    var v = Vec3(10, 11, 12)
    return sum(v)  ' Maxon copies struct to C
end 'main'
```

**Implementation**:
1. Add `StructAST` node type to `ast.h`
2. Parse struct definitions in `parser.cpp`
3. Generate deep copy on FFI boundary when passing to C
4. C receives malloc'd copy and is responsible for freeing it
5. Add automatic copy generation in codegen

**Files to Modify**:
- `maxon-bin/ast.h` - Add StructAST, StructMemberAST
- `maxon-bin/parser.h/cpp` - Add parseStruct()
- `maxon-bin/codegen.h/cpp` - Add struct codegen and copy generation
- `maxon-bin/semantic_analyzer.h/cpp` - Add struct type checking

#### 1.2 Type Compatibility System
Implement C ABI type mapping:

| Maxon Type | C Type | Notes |
|-----------|--------|-------|
| `int` | `int32_t` | Already working |
| `i64` | `int64_t` | New |
| `bool` | `int8_t` | Already working |
| `float` | `double` | Already working |
| `ptr` | `void*` | Already working |
| `struct` | `struct*` | Passed by pointer |
| `[]int` | `MaxonIntArray*` | With length field |

**Files to Modify**:
- `maxon-bin/codegen.cpp` - Update type mapping functions

#### 1.3 Testing
- `language-tests/fragments/ffi-struct-pass.test` - Pass struct to C
- `language-tests/fragments/ffi-struct-return.test` - Return struct from C
- `language-tests/fragments/ffi-array-pass.test` - Pass arrays to C

---

### Phase 2: Memory Isolation (Safety Foundation)
**Goal**: Separate Maxon and C memory to prevent accidental corruption

#### 3.1 Separate Heap Allocators
**Current**: Both Maxon and C use Windows HeapAlloc/GetProcessHeap

**New Architecture**:
- **Maxon Heap**: Use mimalloc with dedicated address space
- **C Heap**: Continue using Windows HeapAlloc
- Never allow reuse between heaps

**Implementation**:
1. Integrate mimalloc as a third-party dependency
2. Replace malloc/free calls in `CodeGenerator::initHeapManagement()`
3. Configure mimalloc to use reserved address range
4. Add compiler flag `--separate-heaps` (default: on)

**Files to Modify**:
- `CMakeLists.txt` - Add mimalloc dependency
- `maxon-bin/codegen.cpp` - Replace heap management implementation
- `maxon-bin/main.cpp` - Add command-line flag

**External Dependencies**:
- Add mimalloc library (https://github.com/microsoft/mimalloc)

#### 2.2 Reference Scrambling
**Goal**: Prevent C from accidentally dereferencing Maxon pointers

When passing Maxon references to C, scramble them:
1. Encode pointer with XOR and rotation
2. Use compile-time random constants
3. Only Maxon code can unscramble

**Implementation**:
```cpp
// In codegen.cpp
class ReferenceScrambler {
    uint64_t xorKey;
    int rotateAmount;
    
public:
    ReferenceScrambler() {
        // Generate random values at compile-time
        xorKey = generateRandomKey();
        rotateAmount = generateRandomRotation();
    }
    
    llvm::Value* scramble(llvm::Value* ptr);
    llvm::Value* unscramble(llvm::Value* scrambledPtr);
};
```

**Files to Create**:
- `maxon-bin/ffi_safety.h`
- `maxon-bin/ffi_safety.cpp`

**Files to Modify**:
- `maxon-bin/codegen.h` - Add ReferenceScrambler member
- `maxon-bin/codegen.cpp` - Apply scrambling at FFI boundaries

#### 2.3 Opaque Pointers
When passing Maxon pointers to C, scramble them so C cannot dereference:

```maxon
struct Ship
    fuel int
end 'Ship'

extern function processShip(s ptr) int

function main() int
    var s = Ship(42)
    ' Pointer is scrambled before passing to C
    return processShip(&s)
end 'main'
```

C receives a scrambled pointer it cannot dereference:
```c
// C sees an opaque pointer
int processShip(void* scrambled_ship_ptr) {
    // Cannot access ship->fuel directly
    // Pointer remains opaque to C
    return 0;
}
```

#### 2.4 Callback Support (Limited Export)
**Goal**: Enable Windows APIs that require callbacks (e.g., EnumWindows, CreateThread)

Many Windows APIs require function pointers as callbacks. To support this safely:

**Design Approach**:
1. Allow marking specific Maxon functions as C-callable with `@extern` annotation
2. Generate C-compatible wrappers for these functions
3. Callbacks run on the Maxon stack (not the C stack)
4. Type signature must be C-compatible (no complex Maxon types)

**Example - EnumWindows callback**:
```maxon
' Callback that C can call
@extern
function myWindowCallback(hwnd ptr, lParam ptr) int
    ' This runs on Maxon stack, safe to use Maxon code
    print(hwnd as int)
    return 1  ' Continue enumeration
end 'myWindowCallback'

extern function EnumWindows(callback ptr, lParam ptr) int

function main() int
    ' Pass function pointer to Windows API
    return EnumWindows(&myWindowCallback, 0 as ptr)
end 'main'
```

**Safety Guarantees**:
- Callbacks run on Maxon's stack (not C's secondary stack)
- Callbacks can access Maxon memory safely
- Callback signatures must use only C-compatible types
- No complex Maxon types (structs, arrays) in callback parameters

**Implementation**:
1. Add `@extern` annotation parser
2. Generate extern "C" wrapper with correct calling convention
3. Ensure callback uses Maxon's stack pointer
4. Validate callback signatures are C-compatible at compile time

**Files to Create**:
- `maxon-bin/callback_wrapper.h`
- `maxon-bin/callback_wrapper.cpp`

**Files to Modify**:
- `maxon-bin/parser.cpp` - Parse @extern annotation
- `maxon-bin/ast.h` - Add isExternCallable flag to FunctionAST
- `maxon-bin/codegen.cpp` - Generate C-callable wrappers
- `maxon-bin/semantic_analyzer.cpp` - Validate callback signatures

**Limitations**:
- Only simple types (int, ptr, float) in callback parameters
- No Maxon-specific types (complex structs, arrays with metadata)
- Callback must not use scrambled references from C
- Performance: Callback entry has minimal overhead (~10-20 CPU cycles)

**Note**: This is a limited form of "export" focused specifically on callbacks. It's simpler than full bidirectional FFI because:
- C calls the function directly (no message passing needed)
- Callbacks use simple types only
- No header generation needed (just function addresses)

#### 2.5 Testing
- `language-tests/fragments/ffi-memory-isolation.test` - Verify separate heaps
- `language-tests/fragments/ffi-scrambled-ptr.test` - Pass scrambled pointer to C
- `language-tests/fragments/ffi-callback-simple.test` - C calls Maxon callback
- `language-tests/fragments/ffi-callback-enumwindows.test` - Real Windows API callback

---

### Phase 3: Stack Separation (Buffer Overrun Protection)
**Goal**: Run C code on a separate stack to prevent corruption

#### 3.1 Secondary Stack Implementation
**Mechanism**: Use inline assembly to switch stack pointer before calling C

**Implementation**:
1. Allocate secondary stack (e.g., 1MB) when initializing FFI
2. Generate wrapper functions for each extern call
3. Wrappers use `setjmp/longjmp` and inline ASM to switch stacks

**Example Generated Code**:
```cpp
// In generated C code for extern call
void extern_function_wrapper() {
    // Extract args from thread-local storage
    size_t original_stack_state = 
        thread_local_wrapper_args->original_stack_state;
    
    // Call the actual extern function
    extern_function(/* args */);
    
    // Jump back to safe stack
    longjmp(*(jmp_buf*)original_stack_state, 1);
}

// In Maxon-generated code
jmp_buf original_stack_state;
if (setjmp(original_stack_state) == 0) {
    // Store args in thread-local storage
    thread_local_wrapper_args = &args;
    
    // Switch to C stack and call wrapper
    asm volatile(
        "mov %[rs], %%rsp \n"
        "call *%[bz] \n"
        : [rs] "+r" (c_stack_top), 
          [bz] "+r" (extern_function_wrapper) ::
    );
} else {
    // Returned here via longjmp
}
```

#### 3.2 Platform Support
**Phase 3a**: Windows x64 support (using inline ASM)
**Phase 3b**: Linux x64 support
**Future**: macOS, ARM64

**Files to Create**:
- `maxon-bin/stack_switch.h` - Platform-specific stack switching
- `maxon-bin/stack_switch_win64.cpp` - Windows implementation
- `maxon-bin/stack_switch_linux.cpp` - Linux implementation (future)

**Files to Modify**:
- `maxon-bin/codegen.cpp` - Generate wrapper functions for extern calls
- `CMakeLists.txt` - Conditional compilation for platforms

#### 3.3 Testing
- `language-tests/fragments/ffi-stack-protection.test` - Verify C buffer overrun doesn't corrupt Maxon
- Manual testing with intentional buffer overruns in C

---

### Phase 4: Sandboxing (Malicious Code Protection)
**Goal**: Isolate untrusted C dependencies

#### 4.1 Sandboxing Strategies
Three tiers of isolation:

1. **No Sandboxing** (default for trusted code)
   - Direct FFI calls
   - Maximum performance
   - Use for code you control

2. **WebAssembly Sandbox** (medium isolation)
   - Compile C to WASM using wasm2c
   - Fast FFI calls, slight CPU overhead
   - Use for portable dependencies

3. **Subprocess Sandbox** (maximum isolation)
   - Run C code in separate process
   - Slow FFI calls, full isolation
   - Use for high-risk dependencies

#### 4.2 WASM-based Sandboxing (wasm2c)
**Implementation**:
1. Integrate wasm2c into build pipeline
2. Compile sandboxed C code to WASM, then to C
3. Link compiled WASM code with Maxon
4. Use Wasmtime/WASI for capability-based security

**Files to Create**:
- `maxon-bin/sandbox_wasm.h`
- `maxon-bin/sandbox_wasm.cpp`

**Build System**:
- `CMakeLists.txt` - Add wasm2c toolchain
- `Makefile` - Add sandbox targets

#### 4.3 Subprocess Sandboxing
**Implementation**:
1. Serialize FFI call arguments to shared memory or pipe
2. Fork/spawn C subprocess with restricted permissions
3. Deserialize results back to Maxon
4. Use OS-level security (Windows Job Objects, Linux seccomp)

**Files to Create**:
- `maxon-bin/sandbox_subprocess.h`
- `maxon-bin/sandbox_subprocess.cpp`
- `maxon-bin/ffi_serialization.h` - Serialize FFI args/results

#### 4.4 Sandbox Configuration
Add compiler directive for sandboxing strategy:
```maxon
' In source file
@sandbox("wasm")
extern function untrusted_lib_function(x int) int

@sandbox("none")  ' or omit for no sandboxing
extern function trusted_function(y int) int
```

Or via command-line:
```bash
maxon compile main.maxon --sandbox-extern=wasm --sandbox-whitelist=trusted_function
```

#### 4.5 Testing
- `language-tests/fragments/ffi-sandbox-wasm.test` - Call C via WASM
- Security testing with intentional malicious C code

---

### Phase 5: Dependency Whitelisting (Supply-Chain Protection)
**Goal**: Prevent unauthorized FFI usage in dependencies

#### 5.1 Whitelist System
**Design**:
- All modules using FFI must be explicitly whitelisted
- Transitive dependencies require whitelisting
- Compiler flag: `--allow-ffi <module>:<dependency>`

**Example**:
```bash
# MyProgram depends on FileLib, which uses FFI
maxon compile MyProgram.maxon \
    --allow-ffi FileLib:stdlib.sys \
    --allow-ffi MyProgram:FileLib
```

#### 5.2 FFI Registry
Create a registry of which functions use FFI:
```maxon
' Automatically tracked by compiler
extern function malloc(size int) ptr  ' Uses FFI
```

During compilation:
1. Scan all dependencies for `extern` declarations
2. Build FFI usage graph
3. Verify all FFI usage is whitelisted
4. Error if unauthorized FFI detected

#### 5.3 Standard Library Whitelisting
Special modules that require whitelisting:
- `stdlib.sys.*` - System calls
- `stdlib.fs.*` - File system
- `stdlib.net.*` - Network (future)
- `stdlib.subprocess.*` - Process spawning (future)

#### 5.4 Implementation
**Files to Create**:
- `maxon-bin/ffi_registry.h`
- `maxon-bin/ffi_registry.cpp`

**Files to Modify**:
- `maxon-bin/main.cpp` - Parse whitelist flags, enforce checks
- `maxon-bin/semantic_analyzer.cpp` - Track FFI usage

#### 5.5 Testing
- Test unauthorized FFI rejection
- Test transitive dependency whitelisting
- Integration tests with multi-module projects

---

### Phase 6: Documentation & Tooling

#### 6.1 User Documentation
Create comprehensive docs:
- `docs/Content/ffi-basics.md` - Extern functions and C interop
- `docs/Content/ffi-structs.md` - Struct interop
- `docs/Content/ffi-safety.md` - Fearless FFI features
- `docs/Content/ffi-sandboxing.md` - Sandboxing strategies
- `docs/Content/ffi-best-practices.md` - Guidelines

#### 6.2 Examples
Create working examples:
- `examples/ffi-simple.maxon` - Basic C interop
- `examples/ffi-structs.maxon` - Struct passing
- `examples/ffi-callbacks.maxon` - Windows API callbacks (EnumWindows, CreateThread)
- `examples/ffi-sandboxed.maxon` - Sandboxed dependency
- `examples/ffi-sqlite.maxon` - Wrapping a real C library

#### 6.3 LSP Support
**Features**:
- Go-to-definition for extern functions
- Inline documentation for FFI safety features
- Warnings for unsafe FFI patterns

**Files to Modify**:
- `lsp-server/src/analyzer.cpp` - Add FFI-aware analysis
- `lsp-server/src/document_manager.cpp` - Track C headers

#### 6.4 VS Code Extension
**Features**:
- Snippets for FFI patterns
- Warnings for FFI safety violations
- Diagnostics for unsafe C interop patterns

**Files to Modify**:
- `vscode-extension/syntaxes/maxon.tmLanguage.json`
- `vscode-extension/src/extension.ts` - Add FFI diagnostics

---

## Implementation Timeline

### Sprint 1 (2-3 weeks): Data Interop
- ✅ Phase 1.1: Struct definitions
- ✅ Phase 1.2: Type compatibility system
- ✅ Phase 1.3: Struct passing tests

### Sprint 2 (3-4 weeks): Memory Safety
- ✅ Phase 2.1: Separate heap allocators
- ✅ Phase 2.2: Reference scrambling
- ✅ Phase 2.3: Opaque pointers
- ✅ Phase 2.4: Callback support (for Windows APIs)
- ✅ Phase 2.5: Memory isolation and callback tests

### Sprint 3 (3-4 weeks): Stack Protection
- ✅ Phase 3.1: Secondary stack implementation (Windows x64)
- ✅ Phase 3.2: Cross-platform support
- ✅ Phase 3.3: Stack protection tests

### Sprint 4 (4-5 weeks): Sandboxing
- ✅ Phase 4.1-4.2: WASM sandbox integration
- ✅ Phase 4.3: Subprocess sandboxing
- ✅ Phase 4.4: Sandbox configuration
- ✅ Phase 4.5: Security testing

### Sprint 5 (2-3 weeks): Whitelisting
- ✅ Phase 5.1-5.2: Whitelist system
- ✅ Phase 5.3: Stdlib whitelisting
- ✅ Phase 5.4: Implementation
- ✅ Phase 5.5: Integration tests

### Sprint 6 (2-3 weeks): Documentation & Polish
- ✅ Phase 6.1: User documentation
- ✅ Phase 6.2: Examples
- ✅ Phase 6.3: LSP support
- ✅ Phase 6.4: VS Code extension

**Total Estimated Time**: 16-22 weeks (4-5.5 months)

---

## Technical Challenges & Solutions

### Challenge 1: Platform-Specific Stack Switching
**Problem**: Inline assembly differs between platforms
**Solution**: 
- Abstract stack switching behind platform-specific modules
- Use `#ifdef` for Windows/Linux/macOS
- Start with Windows x64, add others incrementally

### Challenge 2: Performance Overhead
**Problem**: Scrambling, stack switching, and copying add overhead
**Solution**:
- Make features opt-in/opt-out with compiler flags
- Optimize hot paths with LLVM intrinsics
- Profile and benchmark each feature
- Allow disabling for trusted code

### Challenge 3: Debugging FFI Code
**Problem**: Scrambled references and stack switching complicate debugging
**Solution**:
- Add `--ffi-debug` flag that disables scrambling
- Generate debug symbols for wrapper functions
- Integrate with GDB/LLDB for C code
- Add extensive logging for FFI boundary crossings

---

## Success Metrics

### Safety Metrics
- ✅ Zero buffer overruns corrupting Maxon memory
- ✅ Zero use-after-free affecting Maxon objects
- ✅ Zero type confusion across FFI boundary
- ✅ Catch 100% of invalid scrambled references

### Performance Metrics
- 📊 FFI call overhead < 50ns for unsandboxed code
- 📊 FFI call overhead < 1μs for WASM sandbox
- 📊 FFI call overhead < 100μs for subprocess sandbox
- 📊 No measurable heap fragmentation from dual allocators

### Usability Metrics
- 📚 Complete documentation for all FFI features
- 📚 At least 8 working examples
- 📚 LSP support for FFI (go-to-def, diagnostics)
- 📚 Clear compiler error messages for FFI violations

### Ecosystem Metrics
- 🔧 Able to wrap existing C libraries (e.g., SQLite, zlib)
- 🔧 Stdlib modules use fearless FFI
- 🔧 Third-party packages adopt whitelisting

---

## Risk Mitigation

### Risk: Inline Assembly Fragility
**Impact**: High - breaks on new platforms/compilers
**Mitigation**:
- Comprehensive testing on target platforms
- Fallback to non-optimized C implementations
- Consider using established libraries (e.g., libucontext)

### Risk: Complex Debugging Experience
**Impact**: Medium - developer frustration
**Mitigation**:
- Excellent error messages
- Debug mode that disables protections
- Extensive documentation with troubleshooting guide

### Risk: Performance Regression
**Impact**: Medium - reduced adoption
**Mitigation**:
- Benchmark suite for FFI operations
- Make protections opt-in where possible
- Profile and optimize hot paths

---

## Future Enhancements (Post-Initial Implementation)

### Generational References
Replace pointer scrambling with Vale's generational references:
- More robust than simple scrambling
- Detects use-after-free at runtime
- Better error messages for FFI bugs

### Automatic Binding Generation
Generate Maxon extern declarations from C header files:
```bash
maxon bindgen mylib.h --output stdlib/mylib/
```

This would parse C headers and generate Maxon `extern function` declarations automatically.

### FFI Performance Profiler
Built-in tool to measure FFI overhead:
```bash
maxon profile --ffi myapp.exe
```

### Region-Based Memory Management
Integrate Vale's region borrowing for zero-cost FFI:
- Eliminates scrambling overhead for proven-safe code
- Static analysis proves no corruption possible

### Network Sandboxing
Extend whitelisting to network operations:
- Per-module network permissions
- Restrict which hosts dependencies can access

---

## Conclusion

Implementing Fearless FFI will position Maxon as a leader in memory-safe systems programming. By systematically isolating unsafe C code from Maxon's safe memory, we can:

1. **Prevent accidental corruption** from buffer overruns and type confusion
2. **Mitigate supply-chain attacks** through whitelisting and sandboxing
3. **Enable confident C interop** without sacrificing safety
4. **Build a robust ecosystem** of safe native libraries

The phased approach allows incremental delivery of value while managing complexity. Starting with struct passing and memory isolation provides immediate safety improvements, while later phases (sandboxing, whitelisting) add advanced protection for production systems.

This implementation will require approximately 4-5.5 months of focused development, with ongoing maintenance and platform support thereafter. The result will be a unique and compelling feature that differentiates Maxon in the systems programming language landscape.

**Note**: This design focuses primarily on Maxon-to-C interop (calling C from Maxon), with limited callback support for C APIs that require function pointers. Full bidirectional FFI with arbitrary C-to-Maxon calls is not supported, which keeps the implementation focused while still enabling common Windows API patterns like EnumWindows, CreateThread, and qsort.

---

## References

- [Vale Fearless FFI Blog Post](https://verdagon.dev/blog/fearless-ffi)
- [Vale FFI Guide](https://vale.dev/guide/externs)
- [Vale Generational References](https://verdagon.dev/blog/generational-references)
- [WebAssembly System Interface (WASI)](https://wasi.dev/)
- [wasm2c Tool](https://github.com/WebAssembly/wabt)
- [mimalloc Memory Allocator](https://github.com/microsoft/mimalloc)
- [Firefox Fine-Grained Sandboxing](https://hacks.mozilla.org/2021/12/webassembly-and-back-again-fine-grained-sandboxing-in-firefox-95/)
