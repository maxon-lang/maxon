# Maxon Standard Library Implementation Plan

## Implementation Progress (as of 2025-11-11)

### ✅ Completed Language Features
1. **Extern keyword** - Declare external Windows API functions without implementation
2. **Namespace keyword** - Organize functions into namespaces (e.g., `runtime`, `sys`, `fs`, `fmt`)
3. **Namespace function calls** - Call functions using dot notation: `runtime.exit(0)`
4. **Multiple parameter types** - Functions accept `int`, `ptr`, and `char` parameter types
5. **Typed variable declarations** - `var name type = value` syntax for int/char types
6. **Character literals** - Single character constants like `'A'`
7. **Type casting** - Convert between int, ptr, char using `as` operator
8. **Address-of operator** - Get pointer to variable with `&variable`
9. **Pointer dereferencing** - Load value through pointer with `*ptr`
10. **Modulo operator** - Integer remainder with `%`

### 🐛 Bug Fixes
- **Variable load types** (2025-11-11): Fixed codegen to load variables using their actual type (i8 for char, i32 for int) instead of always loading as i32. This enables proper typed variable support.

### 🧪 Test Coverage
- **38 fragment tests** passing in `language-tests/fragments/`
- Tests cover: namespaces, extern, typed vars, parameter types, operators, casting, pointers
- All tests validate both LLVM IR generation and execution (exit codes)

### ✅ Stdlib Structure Created
- **Directory structure**: `stdlib/`, `runtime/`, `sys/`, `fs/`, `fmt/`
- **stdlib/sys/windows.maxon** - Windows API declarations (GetStdHandle, WriteFile, ExitProcess) ✓ Compiles
- **stdlib/runtime/exit.maxon** - Exit function wrapper ✓ Compiles  
- **stdlib/runtime/entry.asm** - Assembly entry point (~25 lines) ✓ Created

### 🚧 Known Limitations
- **Pointer variables**: Storing pointers in `var p ptr` has type issues (pointers work in expressions)
- **Global variables**: Module-level var declarations not yet implemented
- **Type aliases**: `type Handle = ptr` syntax not implemented
- **Arrays**: Fixed-size array types and indexing not implemented
- **Pointer arithmetic**: Offset calculations not implemented
- **Unary minus**: Negative literals require `0 - N` workaround

### 📝 Syntax Changes
- **Namespace resolution**: Uses `.` (dot) instead of `::` - e.g., `math.add(10, 20)`
- **Optional return types**: Functions without explicit return type default to `void`
- **Type annotations**: Optional on variable declarations - `var x int = 42` or `var x = 42`

---

## Goal
Remove dependency on the C standard library (libcmt.lib, ucrt.lib) and implement a custom Maxon standard library **written in Maxon itself** with direct system calls for I/O operations.

## Philosophy
The standard library should be written in Maxon wherever possible, with only the absolute minimum runtime startup code in assembly. This provides:
- **Dogfooding**: Using Maxon to build Maxon proves the language works
- **Clarity**: Users can read and understand stdlib implementation
- **Consistency**: Same language throughout the stack
- **Debugging**: Easier to debug stdlib code in the same language

## Current State Analysis

### Dependencies Identified
1. **C Runtime Libraries**:
   - `libcmt.lib` - C runtime multi-threaded static library
   - `ucrt.lib` - Universal C Runtime
   - Links via UCRT path: `C:\Program Files (x86)\Windows Kits\10\Lib\10.0.22621.0\ucrt\x64`

2. **Current I/O Implementation**:
   - `print()` function wraps C's `printf()` 
   - Uses format string `"%d\n"` for integer output
   - Location: `maxon-bin/codegen.cpp:initStandardLibrary()`

3. **Windows System Dependencies**:
   - `kernel32.lib` - Already linked (contains WriteFile, GetStdHandle, ExitProcess)
   - `user32.lib` - Linked but may not be needed for console I/O

## Implementation Strategy

### Phase 1: Runtime Foundation (Priority: HIGH) ✅ IN PROGRESS
Create a minimal runtime in assembly that bootstraps Maxon code execution.

#### 1.1 Entry Point (`_start`) - Assembly Only ✅ CREATED
- Replace CRT's entry point with custom `_start` function in assembly
- Initialize stack alignment
- Call Maxon's `main` function (user code)
- Call Maxon's `runtime.exit()` with return value
- **This is the ONLY assembly code needed**

**Files created**:
- ✅ `stdlib/runtime/entry.asm` - x64 assembly entry point (25 lines)

**Implementation**:
```asm
; stdlib/runtime/entry.asm
; Minimal entry point - calls user main and exits

extern main                    ; User's main function
extern "runtime::exit"         ; Our exit function (written in Maxon)

global _start

section .text
_start:
    ; Windows x64 requires 16-byte stack alignment
    ; and 32 bytes shadow space
    sub rsp, 40                ; Align stack + shadow space
    
    ; Call user's main function
    call main                  ; main() -> int
    
    ; RAX contains return value
    ; Call runtime::exit(exit_code)
    mov rcx, rax              ; First arg in RCX
    call "runtime::exit"      ; Written in Maxon
    
    ; Should never return, but just in case
    int3                      ; Breakpoint
```

#### 1.2 Process Exit - Written in Maxon ✅ COMPLETED
Implement exit in Maxon by declaring Windows API as external:

**Files created**:
- ✅ `stdlib/runtime/exit.maxon` - Exit function written in Maxon (compiles successfully)

**Implementation**:
```maxon
// stdlib/runtime/exit.maxon
// Windows API declaration (at module level)
extern function ExitProcess(exitCode int)

namespace runtime 'runtime'
    function exit(code int) int
        ExitProcess(code)
        return 0
    end 'exit'
end 'runtime'
```

**Compiler features used**:
- ✅ `extern` keyword for external function declarations
- ✅ `namespace` keyword for organizing code
- ✅ Namespace function calls work: `runtime.exit(0)`

### Phase 2: I/O and Formatting (Priority: HIGH) 🚧 PARTIALLY COMPLETED
Implement stream access and formatting **entirely in Maxon** using two namespaces: `fs` (file system/streams) and `fmt` (formatting).

#### 2.1 Windows API Declarations - Written in Maxon ✅ COMPLETED

**File created**:
- ✅ `stdlib/sys/windows.maxon` - Windows API declarations (compiles successfully)

**Implementation**:
```maxon
// stdlib/sys/windows.maxon
namespace sys 'sys'
    // Windows API function declarations
    extern function GetStdHandle(nStdHandle int) ptr
    extern function WriteFile(hFile ptr, lpBuffer ptr, nNumberOfBytesToWrite int, 
                              lpNumberOfBytesWritten ptr, lpOverlapped ptr) int
    extern function ExitProcess(exitCode int)
    
    // Standard handle constants (using 0 - N workaround for negative literals)
    function STD_INPUT_HANDLE() int
        return 0 - 10
    end 'STD_INPUT_HANDLE'
    
    function STD_OUTPUT_HANDLE() int
        return 0 - 11
    end 'STD_OUTPUT_HANDLE'
    
    function STD_ERROR_HANDLE() int
        return 0 - 12
    end 'STD_ERROR_HANDLE'
end 'sys'
```

**Compiler features used**:
- ✅ Multiple parameter types (ptr, int)
- ✅ External function declarations with ptr parameters
- ✅ Namespace organization
- ✅ Functions generate correct LLVM IR with qualified names (e.g., `sys::GetStdHandle`)

#### 2.2 `fs` Namespace - Written in Maxon ⏳ BLOCKED
**Purpose**: Provide access to standard streams and low-level I/O operations.

**Status**: Blocked by missing language features:
- ❌ Global variables (module-level `var` declarations)
- ❌ Type aliases (`type Handle = ptr`)
- ❌ Pointer comparison with literals

**File to create**:
- `stdlib/fs/streams.maxon` - Stream management (written in Maxon)

**Planned implementation**:
```maxon
// stdlib/fs/streams.maxon (REQUIRES: global vars, type aliases)
namespace fs 'fs'
    // Type alias for clarity (NOT YET SUPPORTED)
    // type Handle = ptr
    
    // Cached handles (global variables - NOT YET SUPPORTED)
    // var cached_stdout ptr = 0 as ptr
    // var cached_stderr ptr = 0 as ptr
    var cached_stdout: Handle = 0 as Handle
    var cached_stderr: Handle = 0 as Handle
    
    // Get stdout handle (cached)
    func stdout() -> Handle {
        if cached_stdout == (0 as Handle) {
            cached_stdout = sys::windows::GetStdHandle(sys::windows::STD_OUTPUT_HANDLE)
        }
        return cached_stdout
    }
    
    // Get stderr handle (cached)
    func stderr() -> Handle {
        if cached_stderr == (0 as Handle) {
            cached_stderr = sys::windows::GetStdHandle(sys::windows::STD_ERROR_HANDLE)
        }
        return cached_stderr
    }
    
    // Write bytes to handle
    // Returns number of bytes written, or -1 on error
    func write(h: Handle, data: ptr, length: int) -> int {
        var bytesWritten: int = 0
        let result = sys::windows::WriteFile(
            h,
            data,
            length,
            &bytesWritten as ptr,  // Address of bytesWritten
            0 as ptr               // NULL for synchronous
        )
        
        if result == 0 {
            return -1  // Error
        }
        return bytesWritten
    }
}
```

**Note**: Requires compiler support for:
- Global variables (`var`)
- Pointer comparison with `0`
- Address-of operator (`&`)
- Type casting (`as`)

#### 2.3 `fmt` Namespace - Written in Maxon
**Purpose**: Format values into byte buffers for output.

**File to create**:
- `stdlib/fmt/integer.maxon` - Integer formatting (written in Maxon)

**Implementation**:
```maxon
// stdlib/fmt/integer.maxon
namespace fmt {
    // Format integer to buffer, returns number of bytes written
    func format_int(value: int, buffer: ptr, buffer_size: int) -> int {
        if buffer_size < 12 {
            return -1  // Not enough space
        }
        
        // Temporary buffer for digit reversal
        var temp: [12]char
        var pos: int = 11
        
        // Handle zero special case
        if value == 0 {
            temp[0] = '0'
            return 1
        }
        
        // Handle negative numbers
        var negative: int = 0
        var abs_value: int = value
        if value < 0 {
            negative = 1
            abs_value = 0 - value  // Negate
        }
        
        // Convert digits (right to left)
        while abs_value > 0 'convert {
            let digit = abs_value % 10
            temp[pos] = ('0' as int + digit) as char
            pos = pos - 1
            abs_value = abs_value / 10
        } end 'convert
        
        // Add negative sign if needed
        if negative == 1 {
            temp[pos] = '-'
            pos = pos - 1
        }
        
        // Copy from temp to output buffer
        let start = pos + 1
        let length = 12 - start
        var i: int = 0
        while i < length 'copy {
            // Write byte to buffer pointer
            let dest_ptr = (buffer as int + i) as ptr
            let src_byte = temp[start + i]
            // Store byte at pointer
            *dest_ptr = src_byte
            i = i + 1
        } end 'copy
        
        return length
    }
}
```

**Note**: Requires compiler support for:
- Array types (`[12]char`)
- Array indexing
- Character literals (`'0'`, `'-'`)
- Pointer arithmetic
- Pointer dereferencing (`*ptr = value`)
- Modulo and division operators

#### 2.4 Convenience Layer (print function) - Generated in Codegen
The Maxon `print()` built-in remains implemented in the compiler's codegen (not stdlib).

**Implementation in codegen** (unchanged from earlier):
- Stack-allocate buffer
- Call `fmt::format_int`
- Call `fs::write` for formatted output
- Call `fs::write` for newline

### Phase 3: Compiler Integration (Priority: HIGH)

#### 3.1 Compiler Features Required
To support stdlib written in Maxon, the compiler needs these features:

**Already Implemented**:
- Functions with parameters and return values
- Integer arithmetic and comparison
- While loops with break
- If/else statements

**Need to Implement**:
1. **`extern` keyword**: Declare external functions (Windows API)
   ```maxon
   extern func GetStdHandle(handle: int) -> ptr
   ```

2. **`namespace` keyword**: Organize code into namespaces
   ```maxon
   namespace fs { ... }
   ```

3. **Global variables**: Module-level state
   ```maxon
   var cached_stdout: ptr = 0 as ptr
   ```

4. **Type aliases**: Create named types
   ```maxon
   type Handle = ptr
   ```

5. **Array types**: Fixed-size arrays
   ```maxon
   var buffer: [12]char
   ```

6. **Array indexing**: Access array elements
   ```maxon
   buffer[5] = 'x'
   ```

7. **Pointer arithmetic**: Calculate pointer offsets
   ```maxon
   let ptr2 = (ptr1 as int + offset) as ptr
   ```

8. **Pointer dereferencing**: Read/write through pointers
   ```maxon
   *ptr = value
   ```

9. **Address-of operator**: Get address of variables
   ```maxon
   let ptr = &variable
   ```

10. **Character literals**: Single character values
    ```maxon
    let c: char = 'A'
    ```

11. **Type casting**: Convert between types
    ```maxon
    let p = 0 as ptr
    ```

12. **Modulo operator**: Integer remainder
    ```maxon
    let remainder = value % 10
    ```

#### 3.2 Build System Updates
**CMake changes** (`stdlib/CMakeLists.txt`):
```cmake
# Find maxonc compiler
find_program(MAXONC maxonc PATHS ${CMAKE_BINARY_DIR}/bin REQUIRED)

# Compile Maxon stdlib files to object files
set(MAXON_STDLIB_SOURCES
    runtime/exit.maxon
    sys/windows.maxon
    fs/streams.maxon
    fmt/integer.maxon
)

# Compile each .maxon file to .obj
set(MAXON_STDLIB_OBJECTS "")
foreach(MAXON_SRC ${MAXON_STDLIB_SOURCES})
    get_filename_component(OBJ_NAME ${MAXON_SRC} NAME_WE)
    set(OBJ_FILE ${CMAKE_CURRENT_BINARY_DIR}/${OBJ_NAME}.obj)
    
    add_custom_command(
        OUTPUT ${OBJ_FILE}
        COMMAND ${MAXONC} ${CMAKE_CURRENT_SOURCE_DIR}/${MAXON_SRC} -c -o ${OBJ_FILE}
        DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/${MAXON_SRC} maxonc
        COMMENT "Compiling ${MAXON_SRC} to object file"
    )
    
    list(APPEND MAXON_STDLIB_OBJECTS ${OBJ_FILE})
endforeach()

# Assemble entry point
enable_language(ASM_MASM)
add_library(maxon_runtime_asm OBJECT runtime/entry.asm)

# Create static library from all objects
add_library(maxon_runtime STATIC 
    ${MAXON_STDLIB_OBJECTS}
    $<TARGET_OBJECTS:maxon_runtime_asm>
)

# This is a bit special - we're linking pre-compiled objects
set_target_properties(maxon_runtime PROPERTIES 
    OUTPUT_NAME "maxon_runtime"
    LINKER_LANGUAGE CXX  # Need a linker language
)
```

#### 3.2 Linker Configuration Updates
**Changes to `codegen.cpp:compileAndLinkToExecutable()`**:

1. Remove C runtime libraries:
   ```cpp
   // REMOVE these lines:
   // lldArgs.push_back("/DEFAULTLIB:libcmt.lib");
   // lldArgs.push_back("/DEFAULTLIB:oldnames.lib");
   // lldArgs.push_back(ucrtLibPath.c_str());
   ```

2. Add Maxon runtime library:
   ```cpp
   // Add Maxon runtime
   std::string maxonRuntimeLib = buildDir + "/stdlib/libmaxon_runtime.lib";
   lldArgs.push_back(maxonRuntimeLib.c_str());
   ```

3. Set custom entry point:
   ```cpp
   lldArgs.push_back("/ENTRY:_start");  // Our custom entry point
   ```

4. Add nodefaultlib flag:
   ```cpp
   lldArgs.push_back("/NODEFAULTLIB");  // Don't link CRT
   ```

5. Keep essential Windows libraries:
   ```cpp
   lldArgs.push_back("/DEFAULTLIB:kernel32.lib");  // For WriteFile, ExitProcess
   // Remove user32.lib if not needed
   ```

#### 3.2 Linker Configuration Updates
**Changes to `codegen.cpp:initStandardLibrary()`**:

Replace `printf` wrapper with calls to `fmt::format_int` and `fs::write`:

```cpp
void CodeGenerator::initStandardLibrary() {
    if (module->getFunction("print")) {
        return;
    }
    
    // Declare fs::stdout() -> ptr
    llvm::FunctionType* stdoutType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {},
        false
    );
    llvm::Function::Create(
        stdoutType,
        llvm::Function::ExternalLinkage,
        "fs::stdout",
        module.get()
    );
    
    // Declare fs::write(ptr handle, ptr buffer, i32 length) -> i32
    llvm::FunctionType* writeType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {
            llvm::PointerType::get(context, 0),  // handle
            llvm::PointerType::get(context, 0),  // buffer
            llvm::Type::getInt32Ty(context)      // length
        },
        false
    );
    llvm::Function::Create(
        writeType,
        llvm::Function::ExternalLinkage,
        "fs::write",
        module.get()
    );
    
    // Declare fmt::format_int(i32 value, ptr buffer, i32 buffer_size) -> i32
    llvm::FunctionType* formatIntType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {
            llvm::Type::getInt32Ty(context),     // value
            llvm::PointerType::get(context, 0),  // buffer
            llvm::Type::getInt32Ty(context)      // buffer_size
        },
        false
    );
    llvm::Function::Create(
        formatIntType,
        llvm::Function::ExternalLinkage,
        "fmt::format_int",
        module.get()
    );
    
    // Create print() wrapper function
    llvm::FunctionType* printType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),  // Returns 0
        {llvm::Type::getInt32Ty(context)},
        false
    );
    llvm::Function* printFunc = llvm::Function::Create(
        printType,
        llvm::Function::ExternalLinkage,
        "print",
        module.get()
    );
    
    llvm::BasicBlock* entry = llvm::BasicBlock::Create(context, "entry", printFunc);
    builder.SetInsertPoint(entry);
    
    // Allocate buffer on stack: [12 x i8]
    llvm::Type* bufferType = llvm::ArrayType::get(llvm::Type::getInt8Ty(context), 12);
    llvm::Value* buffer = builder.CreateAlloca(bufferType, nullptr, "buffer");
    
    // Get pointer to first element
    llvm::Value* bufferPtr = builder.CreateBitCast(
        buffer,
        llvm::PointerType::get(context, 0)
    );
    
    // Call fmt::format_int(value, buffer, 12)
    llvm::Function* formatFunc = module->getFunction("fmt::format_int");
    llvm::Value* value = printFunc->getArg(0);
    llvm::Value* length = builder.CreateCall(
        formatFunc,
        {value, bufferPtr, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 12)}
    );
    
    // Get stdout handle
    llvm::Function* stdoutFunc = module->getFunction("fs::stdout");
    llvm::Value* handle = builder.CreateCall(stdoutFunc, {});
    
    // Write formatted integer: fs::write(handle, buffer, length)
    llvm::Function* writeFunc = module->getFunction("fs::write");
    builder.CreateCall(writeFunc, {handle, bufferPtr, length});
    
    // Write newline: fs::write(handle, "\n", 1)
    llvm::Constant* newlineStr = llvm::ConstantDataArray::getString(context, "\n", false);
    llvm::GlobalVariable* newlineVar = new llvm::GlobalVariable(
        *module,
        newlineStr->getType(),
        true,  // constant
        llvm::GlobalValue::PrivateLinkage,
        newlineStr,
        ".str.newline"
    );
    llvm::Value* newlinePtr = builder.CreateBitCast(
        newlineVar,
        llvm::PointerType::get(context, 0)
    );
    builder.CreateCall(writeFunc, {handle, newlinePtr, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 1)});
    
    // Return 0
    builder.CreateRet(llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0));
}
```

### Phase 4: Testing & Validation (Priority: HIGH)

#### 4.1 Test Cases
Create test fragments to validate:
1. **Basic output**: `print(42)` outputs "42\n"
2. **Negative numbers**: `print(-123)` outputs "-123\n"
3. **Zero**: `print(0)` outputs "0\n"
4. **Multiple prints**: Multiple print calls work correctly
5. **Exit codes**: Programs exit with correct codes

#### 4.2 Integration Testing
- Run existing language tests with new stdlib
- Verify all tests pass with identical output
- Check exit codes remain correct

#### 4.3 Validation Steps
1. Verify no CRT symbols in final executable:
   ```
   dumpbin /IMPORTS output.exe
   ```
   Should only show kernel32.dll imports (WriteFile, GetStdHandle, ExitProcess)

2. Check executable size reduction (CRT adds ~50-100KB)

3. Verify startup time improvement (no CRT initialization)

### Phase 5: Extended I/O (Priority: MEDIUM)

#### 5.1 Additional Formatting (fmt namespace) - Written in Maxon
Once Maxon has more types, extend `fmt` namespace in pure Maxon:
```maxon
namespace fmt {
    func format_uint(value: uint, buffer: ptr, buffer_size: int) -> int
    func format_bool(value: bool, buffer: ptr, buffer_size: int) -> int
    func format_float(value: float, buffer: ptr, buffer_size: int, precision: int) -> int
    func format_hex(value: int, buffer: ptr, buffer_size: int) -> int
}
```

#### 5.2 File I/O (fs namespace extension) - Written in Maxon
Extend `fs` namespace with file operations in pure Maxon:
```maxon
namespace fs {
    // Declare Windows API
    extern func CreateFileA(
        lpFileName: ptr,
        dwDesiredAccess: int,
        dwShareMode: int,
        lpSecurityAttributes: ptr,
        dwCreationDisposition: int,
        dwFlagsAndAttributes: int,
        hTemplateFile: ptr
    ) -> ptr
    
    extern func ReadFile(
        hFile: ptr,
        lpBuffer: ptr,
        nNumberOfBytesToRead: int,
        lpNumberOfBytesRead: ptr,
        lpOverlapped: ptr
    ) -> int
    
    extern func CloseHandle(hObject: ptr) -> int
    
    // Maxon wrappers
    func open(path: ptr, path_len: int, mode: int) -> Handle { ... }
    func close(h: Handle) -> int { ... }
    func read(h: Handle, buffer: ptr, len: int) -> int { ... }
    func stdin() -> Handle { ... }
}
```

#### 5.3 String Output (Future) - Written in Maxon
Once Maxon has string types:
```maxon
// User can compose using fmt and fs directly
let s = "Hello, World!"
fs::write(fs::stdout(), s.data, s.length)
```

### Phase 6: Memory Management (Priority: MEDIUM)

#### 6.1 Heap Allocation
Replace malloc/free with Windows API:
- `HeapCreate` - Create private heap
- `HeapAlloc` - Allocate memory
- `HeapFree` - Free memory
- `HeapDestroy` - Cleanup on exit

#### 6.2 Stack Management
- Stack probing for large allocations
- Stack overflow detection

## Directory Structure

```
maxon2/
├── stdlib/
│   ├── CMakeLists.txt          # Build stdlib from .maxon files
│   ├── runtime/
│   │   ├── entry.asm           # _start entry point (ONLY assembly file)
│   │   └── exit.maxon          # Exit function (written in Maxon)
│   ├── sys/
│   │   └── windows.maxon       # Windows API declarations
│   ├── fs/                     # File system and streams namespace
│   │   └── streams.maxon       # stdout/stderr/stdin (written in Maxon)
│   ├── fmt/                    # Formatting namespace
│   │   └── integer.maxon       # Integer to ASCII (written in Maxon)
│   └── include/
│       ├── maxon_runtime.h     # C header for external tools (optional)
│       ├── maxon_fs.h          # C header for external tools (optional)
│       └── maxon_fmt.h         # C header for external tools (optional)
├── maxon-bin/
│   └── codegen.cpp             # Update initStandardLibrary()
└── language-tests/
    └── stdlib-tests/
        ├── basic-print.test
        ├── negative-numbers.test
        └── exit-codes.test
```

**Key Point**: Only `entry.asm` is in assembly. Everything else is written in Maxon!

## Windows API Reference

### Required Kernel32.dll Functions

#### GetStdHandle
```cpp
HANDLE GetStdHandle(DWORD nStdHandle);
// STD_OUTPUT_HANDLE = (DWORD)-11
// STD_ERROR_HANDLE = (DWORD)-12
```

#### WriteFile
```cpp
BOOL WriteFile(
    HANDLE hFile,
    LPCVOID lpBuffer,
    DWORD nNumberOfBytesToWrite,
    LPDWORD lpNumberOfBytesWritten,
    LPOVERLAPPED lpOverlapped  // NULL for synchronous
);
```

#### ExitProcess
```cpp
void ExitProcess(UINT uExitCode);
```

#### WriteConsoleA (Alternative)
```cpp
BOOL WriteConsoleA(
    HANDLE hConsoleOutput,
    const VOID *lpBuffer,
    DWORD nNumberOfCharsToWrite,
    LPDWORD lpNumberOfCharsWritten,
    LPVOID lpReserved  // NULL
);
```

## LLVM IR Declaration Examples

```llvm
; Declare Windows API functions
declare dllimport ptr @GetStdHandle(i32)
declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare dllimport void @ExitProcess(i32)

; Constants for GetStdHandle
@STD_OUTPUT_HANDLE = internal constant i32 -11
@STD_ERROR_HANDLE = internal constant i32 -12
@STD_INPUT_HANDLE = internal constant i32 -10

; Cached handles (internal)
@stdout_handle = internal global ptr null
@stderr_handle = internal global ptr null
@stdin_handle = internal global ptr null

; Maxon stdlib functions (fs namespace)
declare ptr @"fs::stdout"()
declare ptr @"fs::stderr"()
declare ptr @"fs::stdin"()
declare i32 @"fs::write"(ptr, ptr, i32)
declare i32 @"fs::read"(ptr, ptr, i32)

; Maxon stdlib functions (fmt namespace)
declare i32 @"fmt::format_int"(i32, ptr, i32)
declare i32 @"fmt::format_uint"(i32, ptr, i32)
declare i32 @"fmt::format_bool"(i1, ptr, i32)
```

## x64 Calling Convention (Windows)

### Integer/Pointer Arguments
1. RCX (first arg)
2. RDX (second arg)
3. R8 (third arg)
4. R9 (fourth arg)
5. Stack (remaining args, right-to-left)

### Return Values
- RAX (integer/pointer)

### Stack
- 16-byte aligned before CALL
- Shadow space: 32 bytes reserved by caller for first 4 args

### Caller/Callee Saved
- **Caller-saved**: RAX, RCX, RDX, R8-R11
- **Callee-saved**: RBX, RBP, RDI, RSI, RSP, R12-R15

## Implementation Timeline

### Week 1: Compiler Features for Stdlib
- [ ] Implement `extern` keyword for external function declarations
- [ ] Implement `namespace` keyword
- [ ] Implement global variables (`var` at module level)
- [ ] Implement type aliases (`type Handle = ptr`)
- [ ] Implement array types (`[12]char`)
- [ ] Implement array indexing (`buffer[i]`)
- [ ] Implement character literals (`'A'`, `'0'`)
- [ ] Implement type casting (`as` operator)
- [ ] Implement modulo operator (`%`)

### Week 2: Pointer Operations & Assembly Entry
- [ ] Implement pointer arithmetic (`(ptr as int + offset) as ptr`)
- [ ] Implement pointer dereferencing (`*ptr = value`)
- [ ] Implement address-of operator (`&variable`)
- [ ] Write `stdlib/runtime/entry.asm` (minimal assembly entry point)
- [ ] Write `stdlib/runtime/exit.maxon` (exit function in Maxon)
- [ ] Test basic program with custom entry point

### Week 3: I/O Implementation in Maxon
- [ ] Write `stdlib/sys/windows.maxon` (Windows API declarations)
- [ ] Write `stdlib/fs/streams.maxon` (fs namespace in Maxon)
- [ ] Write `stdlib/fmt/integer.maxon` (formatting in Maxon)
- [ ] Update CMake to compile .maxon stdlib files
- [ ] Update compiler codegen to call stdlib functions
- [ ] Link stdlib with user programs

### Week 4: Integration & Testing
- [ ] Update linker configuration (remove CRT)
- [ ] Create stdlib test suite
- [ ] Run all existing tests with new stdlib
- [ ] Fix any issues found
- [ ] Validate executable dependencies
- [ ] Document stdlib implementation

## Success Criteria

1. ✅ Maxon programs compile without linking to libcmt.lib or ucrt.lib
2. ✅ `print()` function works identically to current implementation
3. ✅ `fs` namespace provides access to stdout/stderr streams **written in Maxon**
4. ✅ `fmt` namespace provides integer formatting **written in Maxon**
5. ✅ Only ~20 lines of assembly code (entry point), rest is Maxon
6. ✅ All existing language tests pass
7. ✅ Executables only import from kernel32.dll (no msvcrt.dll)
8. ✅ Smaller executable size (50-100KB reduction)
9. ✅ Faster startup time (no CRT initialization)
10. ✅ Clean, maintainable code with clear namespace separation
11. ✅ Well-documented API
12. ✅ Users can read and understand stdlib source code (it's in Maxon!)

## Risks & Mitigations

### Risk 1: Debugging Support
**Issue**: CRT provides debugging infrastructure
**Mitigation**: Maintain debug symbol generation, implement minimal debug hooks if needed

### Risk 2: Stack Probing
**Issue**: Large stack allocations need probing on Windows
**Mitigation**: Implement __chkstk or use LLVM's built-in stack probe

### Risk 3: Exception Handling
**Issue**: If we add exceptions later, CRT provides unwinding
**Mitigation**: Use Windows SEH (Structured Exception Handling) directly

### Risk 4: Floating Point
**Issue**: CRT initializes FPU state
**Mitigation**: Initialize FPU control word in _start if needed

### Risk 5: TLS (Thread Local Storage)
**Issue**: Multi-threading requires TLS initialization
**Mitigation**: Implement TLS callbacks if needed, or defer to Phase 2

## Future Enhancements

### Beyond Console I/O
All written in Maxon:
1. **File I/O**: CreateFile, ReadFile, CloseHandle (extend `fs` namespace)
2. **Memory Management**: HeapAlloc/HeapFree wrappers in Maxon
3. **Additional Formatting**: Float, hex, unsigned int formatting (extend `fmt` namespace)
4. **Math Library**: Implement in Maxon using LLVM intrinsics
5. **Time/Date**: Windows API wrappers in Maxon
6. **Process Control**: CreateProcess wrappers in Maxon
7. **Environment**: GetEnvironmentVariable wrappers in Maxon
8. **Error Handling**: GetLastError, FormatMessage wrappers in Maxon

### User-Facing API Examples
Users can read the stdlib source (it's Maxon!) and compose I/O operations explicitly:
```maxon
// Print a number
let buffer: [12]char
let len = fmt::format_int(42, &buffer as ptr, 12)
fs::write(fs::stdout(), &buffer as ptr, len)
fs::write(fs::stdout(), "\n" as ptr, 1)

// Print to stderr
let err_len = fmt::format_int(-1, &buffer as ptr, 12)
fs::write(fs::stderr(), &buffer as ptr, err_len)

// Or use the built-in print() convenience function
print(42)  // Implemented as wrapper in codegen
```

### Stdlib Source Code Visibility
One of the best features: **users can read the stdlib implementation**!

```maxon
// From stdlib/fmt/integer.maxon - users can read this!
namespace fmt {
    func format_int(value: int, buffer: ptr, buffer_size: int) -> int {
        // ... clear Maxon code that users can understand and learn from
    }
}
```

### Cross-Platform Support
- Linux: Use syscalls (write, exit_group) instead of kernel32
- macOS: Similar to Linux with BSD syscalls
- Abstract platform-specific code behind common interface

## References

### Windows API Documentation
- https://learn.microsoft.com/en-us/windows/win32/api/
- https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-getstdhandle
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-writefile
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-exitprocess

### x64 Calling Convention
- https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention

### LLVM Documentation
- https://llvm.org/docs/LangRef.html
- https://llvm.org/docs/WritingAnLLVMBackend.html

### Similar Projects
- Rust's libcore (no_std)
- Zig's standard library
- musl libc (minimal C library)
- TinyCC runtime

## Notes

- This plan assumes Windows x64 as the primary target
- LLVM already handles most low-level code generation
- **Only ~20 lines of assembly code needed** (entry point)
- **Everything else written in Maxon** - dogfooding the language!
- The compiler itself still uses C++ standard library (for tooling)
- Only generated Maxon programs will use the custom stdlib
- **Users can read stdlib source code** since it's written in Maxon
- This proves Maxon is capable of systems programming
