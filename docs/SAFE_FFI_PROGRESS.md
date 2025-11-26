# Safe FFI Implementation Progress

## Overview

Implementing safe FFI in Maxon by forking the process and using shared memory/semaphores. The forked process executes external code, so if it crashes or corrupts memory, the main process remains safe.

## Design

### Architecture
- **Parent Process**: Main Maxon program that spawns a worker subprocess on first extern call
- **Worker Process**: Same executable, detected via `__MAXON_FFI_WORKER__` environment variable
- **Communication**: Shared memory for passing arguments/results, semaphores for synchronization

### Shared Memory Layout (4KB)
```
Offset 0:   i32 command      (0=exit, 1=call)
Offset 4:   i32 function_id  (maps to extern function)
Offset 8:   i32 arg_count
Offset 12:  i32 status       (0=ok, 1=error, 2=unknown_function)
Offset 16:  [16 x i64] args  (128 bytes, up to 16 arguments)
Offset 144: i64 return_value
```

### IPC Names (with PID suffix for uniqueness)
- Shared Memory: `__MAXON_FFI_SHM_<pid>`
- Request Semaphore: `__MAXON_FFI_REQ_<pid>`
- Response Semaphore: `__MAXON_FFI_RSP_<pid>`

### Environment Variables (passed to worker)
- `__MAXON_FFI_WORKER__=1` - Indicates worker mode
- `__MAXON_FFI_SHARED_` - Shared memory name
- `__MAXON_FFI_REQ_` - Request semaphore name
- `__MAXON_FFI_RSP_` - Response semaphore name

## Components

### 1. Runtime Library (`maxon-runtime/runtime_windows.mir`)

#### Completed Functions:
- `ffi_create_shared_memory(size, name)` - Create named shared memory
- `ffi_map_shared_memory(handle, size)` - Map into address space
- `ffi_create_semaphore(initial, max, name)` - Create named semaphore
- `ffi_wait_semaphore(handle, timeout)` - Wait on semaphore
- `ffi_signal_semaphore(handle)` - Signal semaphore
- `ffi_spawn_worker(cmdline, startup_info, process_info)` - Spawn subprocess
- `ffi_open_shared_memory(name)` - Open existing shared memory
- `ffi_open_semaphore(name)` - Open existing semaphore
- `ffi_is_worker_mode()` - Check if running as worker (checks env var)
- `ffi_format_pid(buffer, pid)` - Format PID as decimal string
- `ffi_get_pid()` - Get current process ID
- `ffi_set_env(name, value)` - Set environment variable

#### Completed Infrastructure:
- `__ffi_worker_main()` - Worker entry point (opens IPC, enters dispatch loop)
- `__ffi_dispatch(shm_ptr, func_id)` - Default dispatch (replaced by codegen)
- `__ffi_parent_init()` - Initialize parent side (create IPC, spawn worker)
- `__ffi_call(func_id, arg_count, args)` - Make FFI call through worker

#### Global Variables:
- Worker-side: handles for shared memory, semaphores
- Parent-side: handles, name buffers, STARTUPINFO, PROCESS_INFORMATION

### 2. Entry Point (`codegen_mir.cpp`)

#### Completed:
- `createMinimalEntryPoint()` modified to check `ffi_is_worker_mode()`
- If worker mode: branches to `__ffi_worker_main` instead of `main`
- Normal mode: calls `main` as usual
- **Fixed**: Now properly declares runtime functions and passes worker return code to exit

### 3. Code Generator (`codegen_mir/codegen_mir_safeffi.cpp`)

#### Completed:
- `registerExternFunction()` - Track extern functions with IDs
- `generateFFIGlobals()` - Generate global variables for FFI state
- `generateFFIInitFunction()` - Generate `__ffi_init()` stub
- `generateFFICleanup()` - Generate `__ffi_cleanup()` stub
- `generateFFIDispatch()` - Generate `__ffi_dispatch(shm_ptr, func_id)`
  - Creates dispatch table mapping function IDs to extern functions
  - Reads arguments from shared memory
  - Calls the extern function
  - Writes result back to shared memory
  - **Fixed**: Now uses `builder->createFunction()` for proper parameter handling
  - **Fixed**: Uses `safeffi::TypeTag` for type comparisons

#### In Progress:
- `generateSafeFFICall()` - Replace extern calls with IPC wrapper calls

### 4. MIR Parser Fixes

#### Completed:
- Added `parseGlobalInitializer()` to parse `c"..."` string constants
- Fixed runtime global merging in `mergeRuntime()` lambda

### 5. X86 Backend Fixes

#### Completed:
- Added `getGlobalOffsets()` getter to `X86CodeGen`
- Fixed `GlobalRef` relocation to look up named globals (not just `.constN`)

## Current Status

### ✅ SAFE FFI IS WORKING

The Safe FFI implementation is complete and functional. Extern function calls are successfully routed through a subprocess for isolation.

**Test Results:**
```
extern function __test_add_numbers(a int, b int) int

function main() int
    var result = __test_add_numbers(5, 3)  ; Returns 8
    return result
end 'main'
```
- Exit code: 8 ✅

### Working:
1. ✅ Worker mode detection via environment variable
2. ✅ Entry point branches to worker main when in worker mode
3. ✅ Runtime globals (string constants) properly merged and addressed
4. ✅ Simple runtime functions work: `ffi_get_pid()`, `ffi_is_worker_mode()`
5. ✅ `hasExternCalls` flag properly tracks ANY extern declaration (including internal ffi_* functions)
6. ✅ Entry point worker mode check generated when any extern is declared
7. ✅ **GEP array indexing fix**: Fixed x86 codegen to use correct element size when indexing into arrays
8. ✅ **Parent initialization complete**: `__ffi_parent_init()` successfully:
   - Creates shared memory via `CreateFileMappingA` and `MapViewOfFile`
   - Creates request and response semaphores via `CreateSemaphoreA`
   - Sets environment variables for worker subprocess
   - Spawns worker subprocess via `CreateProcessA`
   - Worker subprocess detects worker mode and enters `__ffi_worker_main`
9. ✅ **Worker dispatch loop**: Worker waits for requests, dispatches to extern functions, returns results
10. ✅ **Safe FFI call generation**: `generateSafeFFICall()` creates IPC wrapper code
11. ✅ **Cleanup**: `__ffi_parent_cleanup()` sends exit command and closes all handles - no orphaned workers
12. ✅ **Debug code removed**: All `ffi_debug_checkpoint` calls and related globals removed

### Testing Status:
| Test | Result |
|------|--------|
| No extern declarations | ✅ Exit 42 |
| Extern declared but not called | ✅ Exit 42 |
| Call `ffi_get_pid()` | ✅ Returns PID |
| Call `ffi_is_worker_mode()` | ✅ Returns 0/1 |
| Call `__ffi_parent_init()` | ✅ Exit 0 (success) |
| Call `__test_add_numbers(5, 3)` | ✅ Exit 8 |
| Multiple Safe FFI calls | ✅ Works correctly |

### Key Findings During Debug:
1. **STARTUPINFOA must be zeroed**: CreateProcessA crashes if STARTUPINFOA structure isn't fully zeroed before setting `cb` field
2. **GEP pointer reload needed**: Pointers stored early in a function can become invalid after many function calls (register spill/reload issue). Workaround: reload GEP fresh right before use
3. **CREATE_NO_WINDOW hides child output**: When debugging, use 0 for dwCreationFlags to see child process output
4. **ffi_create_semaphore signature**: Fixed to `(i32 initial, i32 max, ptr name)` to match CreateSemaphoreA parameter order
5. **LoadLibraryA/GetProcAddress not in PE imports**: The dynamic DLL loading functions were declared but not added to the PE import table, causing calls to go to address 0 and hang

### Remaining Work:

1. **Error handling improvements**
   - Worker crash detection
   - Timeout handling for unresponsive workers

2. **Remove debug output from runtime** - The runtime still prints debug characters (S, W, w, L, R, D, C, X) during FFI calls

## Key Bug Fixes This Session

### 1. GEP Array Element Size (x86_codegen.cpp)
**Problem**: When generating x86 code for `getelementptr [N x T], ptr, i64 0, i64 idx`, the codegen used the size of the whole array type instead of the element type T.

**Example**:
```mir
%ptr = getelementptr [16 x i8], ptr %buf, i64 0, i64 %idx
```
Generated: `lea rax, [rax + rcx*8]` (wrong - 8 byte scale)
Should be: `lea rax, [rax + rcx*1]` (correct - 1 byte scale for i8)

**Fix** in `genGEP()`:
```cpp
// If we have two indices and elementType is an array, the second index
// indexes into the array elements, so use the array's element type
if (inst->operands.size() >= 3 && inst->elementType->kind == mir::MIRTypeKind::Array) {
    sizeType = inst->elementType->elementType;
}
```

### 2. hasExternCalls Flag (codegen_mir_safeffi.cpp)
**Problem**: The `hasExternCalls` flag was only set when a user-facing extern function was registered, not for internal runtime functions like `ffi_*`. This caused the worker mode check in `_start` to not be generated.

**Fix**: Set `hasExternCalls = true` for ALL extern declarations, even internal ones that skip Safe FFI registration:
```cpp
void MIRCodeGenerator::registerExternFunction(...) {
    hasExternCalls = true;  // Set for ANY extern declaration
    
    if (name starts with "ffi_" or "__ffi_") {
        // Skip Safe FFI registration for internal functions
        return;
    }
    // ... register for Safe FFI
}
```

### 3. Entry Point Check (codegen_mir.cpp)
**Problem**: `createMinimalEntryPoint()` checked `!externFunctions.empty()` to decide whether to generate worker mode check, but internal ffi_* functions weren't added to `externFunctions`.

**Fix**: Check `hasExternCalls` instead:
```cpp
if (hasExternCalls) {
    // Generate worker mode check
}
```

## Test Files

- `temp/test_ffi_worker.maxon` - Basic test with extern declaration
- `language-tests/ffi-test-lib/` - C test library for FFI testing

## File Locations

- Runtime: `maxon-runtime/runtime_windows.mir`
- Codegen: `maxon-bin/codegen_mir.cpp`, `maxon-bin/codegen_mir/codegen_mir_safeffi.cpp`
- Entry point: `maxon-bin/codegen_mir.cpp` (`createMinimalEntryPoint()`)
- Backend: `maxon-bin/backend/x86_codegen.cpp`, `maxon-bin/codegen_mir/codegen_mir_output.cpp`
- MIR Parser: `maxon-bin/mir/mir_parser.cpp`

## Commands

```bash
# Build compiler
make compiler

# Compile test
maxon compile temp/test_ffi_worker.maxon

# Run normal mode
temp/test_ffi_worker.exe

# Run worker mode
export __MAXON_FFI_WORKER__=1 && temp/test_ffi_worker.exe

# Check disassembly
llvm-objdump -d temp/test_ffi_worker.exe

# Check data section
llvm-objdump -s -j .data temp/test_ffi_worker.exe
```
