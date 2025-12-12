# Maxon Runtime Development Guide

This document details how to implement functions in the Maxon runtime library, which is written in textual MIR (Maxon Intermediate Representation).

## Overview

The runtime library is split into files:
- `runtime.mir` - Platform-independent core functions
- `runtime_windows.mir` - Windows-specific implementation (Win32 API)
- `runtime_linux.mir` - Linux-specific implementation (syscalls)

The runtime provides low-level functionality that cannot be written in pure Maxon:
- Memory allocation (`malloc`, `free`, `realloc`)
- I/O operations (`write_stdout`, `read_file`, `write_file`)
- String and array management (refcounted heap allocations)
- Math functions (`sqrt`, `sin`, `cos`, etc.)
- Process control (`exit`)

## MIR Type System

### Primitive Types

| MIR Type | Size (bytes) | Description |
|----------|--------------|-------------|
| `void`   | 0            | No value (for void returns) |
| `i1`     | 1            | Boolean (stored as 8-bit) |
| `i8`     | 1            | 8-bit signed integer / byte / character |
| `i32`    | 4            | 32-bit signed integer |
| `i64`    | 8            | 64-bit signed integer |
| `f64`    | 8            | 64-bit IEEE 754 float (double) |
| `ptr`    | 8            | Opaque pointer (64-bit on x64) |

### Composite Types

| MIR Type | Syntax | Description |
|----------|--------|-------------|
| Array    | `[N x T]` | Fixed-size array of N elements of type T |
| Struct   | `%StructName` | User-defined struct type |

### Type Mappings

| Maxon Type | MIR Type |
|------------|----------|
| `int`      | `i32`    |
| `float`    | `f64`    |
| `bool`     | `i1`     |
| `byte`     | `i8`     |
| `character`| `i8`*    |
| `string`   | `%string` (struct) |
| `T or nil` | Tagged union with i1 discriminator |

*Characters are graphemes stored as UTF-8. The `i8` type represents single bytes.

## Function Definition Syntax

### Basic Structure

```
define <return_type> @<function_name>(<params>) {
<block_name>:
    <instructions>
    <terminator>
}
```

### Parameter Declaration

Parameters are declared as `<type> %<name>`:

```
define i32 @add(i32 %a, i32 %b) {
entry:
    %result = add i32 %a, %b
    ret i32 %result
}
```

### External Function Declaration

To call external functions (OS APIs, other runtime functions):

```
declare <return_type> @<function_name>(<param_types>)
```

Example:
```
declare ptr @HeapAlloc(ptr, i32, i64)
declare i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
```

## Parameters and Return Values

### Passing Parameters

Parameters are passed by value. Use `%<name>` to reference them:

```
define i32 @multiply(i32 %x, i32 %y) {
entry:
    %result = mul i32 %x, %y
    ret i32 %result
}
```

### Parameter Types by Example

#### Integer Parameters
```
define i32 @process_int(i32 %value) {
entry:
    ; Use %value directly
    %doubled = mul i32 %value, 2
    ret i32 %doubled
}
```

#### 64-bit Integer Parameters
```
define i64 @process_size(i64 %size) {
entry:
    %padded = add i64 %size, 8
    ret i64 %padded
}
```

#### Pointer Parameters
```
define void @process_buffer(ptr %buf, i32 %len) {
entry:
    ; %buf is an opaque pointer - use load/store to access data
    %first_byte = load i8, ptr %buf
    ret void
}
```

#### Float Parameters
```
define f64 @process_float(f64 %x) {
entry:
    %squared = fmul f64 %x, %x
    ret f64 %squared
}
```

#### Boolean Parameters
```
define i32 @conditional(i1 %flag, i32 %a, i32 %b) {
entry:
    br i1 %flag, label %use_a, label %use_b

use_a:
    ret i32 %a

use_b:
    ret i32 %b
}
```

### Return Values

#### Returning Integers
```
define i32 @return_int() {
entry:
    ret i32 42
}
```

#### Returning 64-bit Values
```
define i64 @return_size() {
entry:
    ret i64 1024
}
```

#### Returning Pointers
```
define ptr @return_ptr() {
entry:
    %mem = call ptr @malloc(i64 100)
    ret ptr %mem
}
```

#### Returning Floats
```
define f64 @return_float() {
entry:
    ret f64 3.14159
}
```

#### Returning Booleans
```
define i1 @return_bool(i32 %x) {
entry:
    %is_positive = icmp sgt i32 %x, 0
    ret i1 %is_positive
}
```

#### Returning Void
```
define void @no_return(i32 %x) {
entry:
    ; do something
    ret void
}
```

## Instructions Reference

### Arithmetic (Integer)

```
%result = add i32 %a, %b       ; Addition
%result = sub i32 %a, %b       ; Subtraction
%result = mul i32 %a, %b       ; Multiplication
%result = sdiv i32 %a, %b      ; Signed division
%result = srem i32 %a, %b      ; Signed remainder (modulo)
%result = udiv i32 %a, %b      ; Unsigned division
%result = urem i32 %a, %b      ; Unsigned remainder
```

### Arithmetic (Float)

```
%result = fadd f64 %a, %b      ; Addition
%result = fsub f64 %a, %b      ; Subtraction
%result = fmul f64 %a, %b      ; Multiplication
%result = fdiv f64 %a, %b      ; Division
%result = frem f64 %a, %b      ; Remainder (fmod)
%result = fneg f64 %x          ; Negation
```

### Bitwise Operations

```
%result = and i32 %a, %b       ; Bitwise AND
%result = or i32 %a, %b        ; Bitwise OR
%result = xor i32 %a, %b       ; Bitwise XOR
%result = shl i32 %a, %b       ; Shift left
%result = ashr i32 %a, %b      ; Arithmetic shift right (sign-extend)
%result = lshr i32 %a, %b      ; Logical shift right (zero-fill)
```

### Comparisons (Integer)

```
%result = icmp eq i32 %a, %b   ; Equal
%result = icmp ne i32 %a, %b   ; Not equal
%result = icmp slt i32 %a, %b  ; Signed less than
%result = icmp sle i32 %a, %b  ; Signed less than or equal
%result = icmp sgt i32 %a, %b  ; Signed greater than
%result = icmp sge i32 %a, %b  ; Signed greater than or equal
%result = icmp ult i32 %a, %b  ; Unsigned less than
%result = icmp ule i32 %a, %b  ; Unsigned less than or equal
%result = icmp ugt i32 %a, %b  ; Unsigned greater than
%result = icmp uge i32 %a, %b  ; Unsigned greater than or equal
```

### Comparisons (Float)

```
%result = fcmp oeq f64 %a, %b  ; Ordered equal
%result = fcmp one f64 %a, %b  ; Ordered not equal
%result = fcmp olt f64 %a, %b  ; Ordered less than
%result = fcmp ole f64 %a, %b  ; Ordered less or equal
%result = fcmp ogt f64 %a, %b  ; Ordered greater than
%result = fcmp oge f64 %a, %b  ; Ordered greater or equal
```

Note: "Ordered" means neither operand is NaN.

### Memory Operations

#### Stack Allocation
```
%ptr = alloca i32              ; Allocate space for one i32
%arr = alloca [10 x i32]       ; Allocate array of 10 i32s
```

#### Load from Memory
```
%value = load i32, ptr %ptr    ; Load i32 from pointer
%byte = load i8, ptr %buf      ; Load single byte
%float = load f64, ptr %fptr   ; Load float
```

#### Store to Memory
```
store i32 %value, ptr %ptr     ; Store i32 to pointer
store i8 %byte, ptr %buf       ; Store byte
store f64 %float, ptr %fptr    ; Store float
```

#### Get Element Pointer (GEP)
```
; Array indexing
%elem_ptr = getelementptr i32, ptr %arr, i64 %index

; Struct field access (by byte offset)
%field_ptr = getelementptr i8, ptr %struct_ptr, i64 8

; Multi-dimensional
%elem = getelementptr [10 x i32], ptr %arr2d, i64 %row, i64 %col
```

### Type Conversions

```
; Integer truncation
%i32val = trunc i64 %i64val to i32
%i8val = trunc i32 %i32val to i8

; Integer extension
%i64val = zext i32 %i32val to i64   ; Zero-extend (unsigned)
%i64val = sext i32 %i32val to i64   ; Sign-extend (signed)

; Float/Int conversion
%int = fptosi f64 %float to i64     ; Float to signed int (truncate)
%float = sitofp i64 %int to f64     ; Signed int to float

; Pointer conversions
%int = ptrtoint ptr %p to i64       ; Pointer to integer
%ptr = inttoptr i64 %int to ptr     ; Integer to pointer

; Bitwise reinterpretation
%bits = bitcast f64 %float to i64   ; View float bits as int
%float = bitcast i64 %bits to f64   ; View int bits as float
```

### Control Flow

#### Unconditional Branch
```
br label %target_block
```

#### Conditional Branch
```
br i1 %condition, label %true_block, label %false_block
```

#### Return
```
ret i32 %value                 ; Return with value
ret void                       ; Return from void function
```

### Function Calls

```
; Call with return value
%result = call i32 @add(i32 %a, i32 %b)

; Call void function
call void @print(ptr %msg)

; Call with mixed types
%ptr = call ptr @malloc(i64 %size)
```

## Common Patterns

### Loop Pattern

```
define i32 @sum_to_n(i32 %n) {
entry:
    %sum = alloca i32
    %i = alloca i32
    store i32 0, ptr %sum
    store i32 0, ptr %i
    br label %loop_cond

loop_cond:
    %ival = load i32, ptr %i
    %cond = icmp slt i32 %ival, %n
    br i1 %cond, label %loop_body, label %loop_end

loop_body:
    ; sum += i
    %sumval = load i32, ptr %sum
    %newsum = add i32 %sumval, %ival
    store i32 %newsum, ptr %sum
    ; i++
    %inext = add i32 %ival, 1
    store i32 %inext, ptr %i
    br label %loop_cond

loop_end:
    %result = load i32, ptr %sum
    ret i32 %result
}
```

### Phi Node Pattern (SSA-style loop)

```
define i32 @count_loop(i32 %n) {
entry:
    br label %loop

loop:
    %i = phi i32 [0, %entry], [%next_i, %loop]
    %done = icmp sge i32 %i, %n
    br i1 %done, label %exit, label %continue

continue:
    %next_i = add i32 %i, 1
    br label %loop

exit:
    ret i32 %i
}
```

### Null Check Pattern

```
define i32 @safe_deref(ptr %p) {
entry:
    %is_null = icmp eq ptr %p, null
    br i1 %is_null, label %null_case, label %valid_case

null_case:
    ret i32 -1

valid_case:
    %value = load i32, ptr %p
    ret i32 %value
}
```

### String Processing Pattern

```
define i32 @strlen(ptr %str) {
entry:
    br label %loop

loop:
    %i = phi i32 [0, %entry], [%next_i, %continue]
    %i64 = sext i32 %i to i64
    %charptr = getelementptr i8, ptr %str, i64 %i64
    %char = load i8, ptr %charptr
    %is_null = icmp eq i8 %char, 0
    br i1 %is_null, label %done, label %continue

continue:
    %next_i = add i32 %i, 1
    br label %loop

done:
    ret i32 %i
}
```

### Calling OS APIs (Windows)

**IMPORTANT:** Every Windows API function must be declared before use. These declarations go at the top of `runtime_windows.mir`, not inside function bodies.

#### Step 1: Add Declaration at File Top

Check if the API is already declared in `runtime_windows.mir`. If not, add it:

```
; External Windows API declarations (at top of file)
declare ptr @GetStdHandle(i32)
declare i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare ptr @HeapAlloc(ptr, i32, i64)
declare i1 @HeapFree(ptr, i32, ptr)
```

#### Step 2: Use in Function

```
; First declare the API
declare ptr @GetStdHandle(i32)
declare i32 @WriteFile(ptr, ptr, i32, ptr, ptr)

define i32 @write_stdout(ptr %buf, i32 %count) {
entry:
    ; Allocate space for bytes written
    %bytesWritten = alloca i32

    ; Get stdout handle (STD_OUTPUT_HANDLE = -11)
    %handle = call ptr @GetStdHandle(i32 -11)

    ; Call WriteFile
    %success = call i32 @WriteFile(ptr %handle, ptr %buf, i32 %count, ptr %bytesWritten, ptr null)

    ; Check if WriteFile succeeded
    %failed = icmp eq i32 %success, 0
    br i1 %failed, label %error, label %success_case

success_case:
    %written = load i32, ptr %bytesWritten
    ret i32 %written

error:
    ret i32 -1
}
```

## Global Variables

### Mutable Globals

```
@counter = global i32 0           ; Initialized to 0
@buffer = global [100 x i8] zeroinitializer
```

### Constants

```
@message = constant [13 x i8] c"Hello World\n\00"
@pi = constant f64 3.14159265359
```

### Using Globals

```
define void @increment_counter() {
entry:
    %old = load i32, ptr @counter
    %new = add i32 %old, 1
    store i32 %new, ptr @counter
    ret void
}
```

### Character Escape Sequences in Constants

In constant string arrays, use `\XX` hex escapes:
- `\00` - Null terminator
- `\0A` - Newline (LF)
- `\0D` - Carriage return (CR)
- `\09` - Tab

```
@newline = constant [2 x i8] c"\0A\00"
@crlf = constant [3 x i8] c"\0D\0A\00"
```

## Memory Management Functions

The runtime provides these core memory functions:

### malloc
```
; ptr malloc(i64 size)
; Allocates size bytes from the heap
%ptr = call ptr @malloc(i64 100)
```

### free
```
; void free(ptr ptr)
; Frees previously allocated memory
call void @free(ptr %ptr)
```

### realloc
```
; ptr realloc(ptr ptr, i64 old_size, i64 new_size)
; Note: old_size is required for cross-platform compatibility
%newptr = call ptr @realloc(ptr %ptr, i64 50, i64 100)
```

## Managed String/Array Functions

The runtime uses reference-counted heap allocations with an 8-byte header:

```
Layout: [refcount:i32][data_size:i32][...data...]
        ← offset -8 →← offset -4  →←  offset 0 (returned ptr)
```

**Key Point:** The pointer returned by `_managed_*_alloc` points to the DATA area, not the header. To access the header, subtract 8 from the pointer.

### Managed String Functions

These are defined in `runtime.mir` and available to all runtime code.

#### _managed_string_alloc
```
; ptr _managed_string_alloc(i64 capacity, ptr tag)
; Allocates: 8-byte header + capacity bytes
; Initializes refcount to 1
; Returns: pointer to data area (after header)
; tag: optional description for allocation tracking (use null if not needed)

%buf = call ptr @_managed_string_alloc(i64 100, ptr null)
; Now %buf points to 100 bytes of usable space
```

#### _managed_string_release
```
; void _managed_string_release(ptr data, ptr tag)
; Decrements refcount, frees memory if refcount reaches 0
; Safe to call with null (no-op)
; tag: optional description for tracking

call void @_managed_string_release(ptr %buf, ptr null)
```

#### _managed_string_retain
```
; void _managed_string_retain(ptr data)
; Increments refcount (use when copying a reference)

call void @_managed_string_retain(ptr %buf)
; Now refcount is 2 - both original and copy own it
```

#### _managed_string_get_refcount
```
; i32 _managed_string_get_refcount(ptr data)
; Returns current refcount (useful for COW decisions)

%rc = call i32 @_managed_string_get_refcount(ptr %buf)
%is_shared = icmp ugt i32 %rc, 1
```

#### _managed_string_make_unique
```
; ptr _managed_string_make_unique(ptr data, i32 len, ptr tag)
; If refcount > 1: allocates new buffer, copies data, decrements original
; If refcount == 1: returns same pointer (already unique)
; Returns: pointer to unique buffer (may be same as input)

%unique = call ptr @_managed_string_make_unique(ptr %buf, i32 %length, ptr null)
; Now safe to mutate %unique without affecting other references
```

### Managed Array Functions

Same layout and semantics as strings, but for arrays.

#### _managed_array_alloc
```
; ptr _managed_array_alloc(i64 byteSize, ptr tag)
; Allocates: 8-byte header + byteSize bytes
; Initializes refcount to 1

; For an array of 10 i32s:
%arr = call ptr @_managed_array_alloc(i64 40, ptr null)  ; 10 * 4 bytes
```

#### _managed_array_release
```
; void _managed_array_release(ptr data, ptr tag)
; Decrements refcount, frees if zero
; Safe to call with null

call void @_managed_array_release(ptr %arr, ptr null)
```

#### _managed_array_retain
```
; void _managed_array_retain(ptr data)
; Increments refcount
; Safe to call with null (no-op)

call void @_managed_array_retain(ptr %arr)
```

#### _managed_array_get_refcount
```
; i32 _managed_array_get_refcount(ptr data)
; Returns current refcount

%rc = call i32 @_managed_array_get_refcount(ptr %arr)
```

### Usage Example: Creating a Managed Buffer

```
define ptr @create_buffer(i64 %size) {
entry:
    ; Allocate managed buffer
    %buf = call ptr @_managed_string_alloc(i64 %size, ptr null)
    
    ; Check allocation succeeded
    %is_null = icmp eq ptr %buf, null
    br i1 %is_null, label %failed, label %success

success:
    ; Zero-fill the buffer
    call ptr @memset(ptr %buf, i32 0, i64 %size)
    ret ptr %buf

failed:
    ret ptr null
}
```

### Usage Example: Copy-on-Write Pattern

```
define ptr @modify_buffer(ptr %buf, i32 %len) {
entry:
    ; Ensure we have exclusive ownership before modifying
    %unique = call ptr @_managed_string_make_unique(ptr %buf, i32 %len, ptr null)
    
    ; Now safe to modify %unique
    store i8 65, ptr %unique  ; Write 'A' to first byte
    
    ret ptr %unique
}
```

### Allocation Tracking

For debugging memory leaks, pass a tag string to identify allocations:

```
@__tag_my_buffer = constant [10 x i8] c"my buffer\00"

define ptr @tracked_alloc() {
entry:
    %buf = call ptr @_managed_string_alloc(i64 100, ptr @__tag_my_buffer)
    ret ptr %buf
}
```

When `--track-allocs` is enabled, output will show:
```
ALLOC #1: 108 bytes (my buffer)
FREE #1: 108 bytes (my buffer)
```

## Complete Example: Implementing a Runtime Function

Here's a complete example implementing `memset`:

```
;==============================================================================
; memset - fill memory with a constant byte value
; ptr memset(ptr dest, i32 val, i64 count)
;==============================================================================
define ptr @memset(ptr %dest, i32 %val, i64 %count) {
entry:
    ; Convert value to i8
    %byteVal = trunc i32 %val to i8
    ; Allocate loop counter
    %i = alloca i64
    store i64 0, ptr %i
    br label %loop.cond

loop.cond:
    %iVal = load i64, ptr %i
    %cond = icmp ult i64 %iVal, %count
    br i1 %cond, label %loop.body, label %loop.end

loop.body:
    ; Calculate pointer: dest + i
    %ptr = getelementptr i8, ptr %dest, i64 %iVal
    ; Store byte
    store i8 %byteVal, ptr %ptr
    ; Increment counter
    %iNext = add i64 %iVal, 1
    store i64 %iNext, ptr %i
    br label %loop.cond

loop.end:
    ret ptr %dest
}
```

### Debugging Tips

### Common Mistakes

1. **Forgetting to declare Windows APIs**: Every Windows API call needs a `declare` statement at the top of `runtime_windows.mir`. This is the #1 cause of runtime issues.

2. **Wrong type sizes**: `i32` is 4 bytes, `i64` is 8 bytes. Use appropriate sizes.

2. **Forgetting to sign-extend**: When using i32 as array index, extend to i64:
   ```
   %i64 = sext i32 %i to i64
   %ptr = getelementptr i8, ptr %base, i64 %i64
   ```

3. **Missing null terminators**: C strings need `\00`:
   ```
   @msg = constant [6 x i8] c"hello\00"
   ```

4. **Incorrect comparison types**: Use `icmp` for integers, `fcmp` for floats.

5. **Missing basic block terminators**: Every block needs a terminator (`br`, `ret`).

6. **SSA violations**: Each `%name` can only be assigned once. Use `alloca`/`load`/`store` for mutable state, or phi nodes.

### Testing Runtime Functions

1. Add a test in `language-tests/` that calls your function
2. Run `make test` to verify
3. Use `--track-allocs` flag to check for memory leaks

## Platform-Specific Notes

### Windows

#### Declaring Windows APIs

**Every Windows API function you call MUST be declared at the top of `runtime_windows.mir`.**

This is the most common mistake when adding new runtime functions. If you forget the declaration, you'll get linker errors or undefined behavior.

**Common Windows API declarations (check if already present before adding):**

```
; Process/Memory
declare ptr @GetProcessHeap()
declare ptr @HeapAlloc(ptr, i32, i64)
declare ptr @HeapReAlloc(ptr, i32, ptr, i64)
declare i1 @HeapFree(ptr, i32, ptr)
declare void @ExitProcess(i32)

; Console I/O
declare ptr @GetStdHandle(i32)
declare i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare i32 @ReadFile(ptr, ptr, i32, ptr, ptr)

; File I/O
declare ptr @CreateFileA(ptr, i32, i32, ptr, i32, i32, ptr)
declare i32 @GetFileSize(ptr, ptr)
declare i1 @CloseHandle(ptr)

; Environment/Process Info
declare ptr @GetCommandLineA()
declare i32 @GetEnvironmentVariableA(ptr, ptr, i32)
declare i1 @SetEnvironmentVariableA(ptr, ptr)
declare i32 @GetCurrentProcessId()
declare i32 @GetLastError()
```

**Finding Windows API signatures:**
1. Look up the function on Microsoft Docs
2. Map C types to MIR types:
   - `HANDLE`, `LPVOID`, `LPCSTR` → `ptr`
   - `DWORD`, `UINT` → `i32`
   - `SIZE_T` → `i64`
   - `BOOL` → `i1` (but some return `i32`)
   - `VOID` → `void`

#### Common Windows Constants

- `STD_OUTPUT_HANDLE = -11`
- `STD_INPUT_HANDLE = -10`
- `STD_ERROR_HANDLE = -12`
- `INVALID_HANDLE_VALUE = -1` (as pointer: `inttoptr i64 -1 to ptr`)
- `GENERIC_READ = 0x80000000` (-2147483648 as i32)
- `GENERIC_WRITE = 0x40000000` (1073741824)
- `FILE_SHARE_READ = 1`
- `OPEN_EXISTING = 3`
- `CREATE_ALWAYS = 2`
- `FILE_ATTRIBUTE_NORMAL = 128`

#### Heap Functions

#### Heap Functions

Use the Windows heap API for memory allocation:
```
%heap = call ptr @GetProcessHeap()
%ptr = call ptr @HeapAlloc(ptr %heap, i32 0, i64 %size)
```

### Linux

- Use syscalls directly via inline assembly or syscall wrapper
- Common syscalls: `write = 1`, `exit = 60`, `mmap = 9`, `munmap = 11`
- File descriptors: `stdin = 0`, `stdout = 1`, `stderr = 2`

## Summary Table: Type/Instruction Quick Reference

| Operation | Integer | Float |
|-----------|---------|-------|
| Add       | `add`   | `fadd` |
| Subtract  | `sub`   | `fsub` |
| Multiply  | `mul`   | `fmul` |
| Divide    | `sdiv`/`udiv` | `fdiv` |
| Compare   | `icmp`  | `fcmp` |
| Negate    | `sub 0, %x` or build with `mul -1` | `fneg` |

| Type Conversion | Instruction |
|-----------------|-------------|
| i32 → i64 (signed) | `sext i32 %x to i64` |
| i32 → i64 (unsigned) | `zext i32 %x to i64` |
| i64 → i32 | `trunc i64 %x to i32` |
| float → int | `fptosi f64 %x to i64` |
| int → float | `sitofp i64 %x to f64` |
| ptr → int | `ptrtoint ptr %p to i64` |
| int → ptr | `inttoptr i64 %x to ptr` |
