# Development Challenges Summary

Based on the documentation, here are the biggest problems encountered during Maxon development:

## 1. **Memory Management with Nested Types** (Most Critical)

**Array Deep Copy in Struct Literals** - This was blocking self-hosted compiler progress:
- When assigning array variables to struct fields, only the header (including `_buffer` pointer) was copied, not the actual buffer contents
- Stack-allocated arrays have `_buffer` pointing to stack memory that becomes invalid after function return
- **Symptom**: Segfaults when pushing structs containing arrays (e.g., `FunctionDecl` with `array<Stmt>`)
- **Fix**: Deep copy logic in multiple code paths with proper heap allocation and refcount management

**Null Pointer in Array Element Retain** - Related follow-on bug:
- After memcpy-ing array data, retain calls on managed types crashed for zero-initialized elements (null pointers)
- **Fix**: Added null checks before calling retain functions

## 2. **Type Size Computation Ordering**

**Struct and Optional sizes computed incorrectly**:
- Eager size computation in constructors ran before field types were complete
- Hash map iteration order determined which types got computed first
- Nested struct fields had size=0 when containing struct was computed
- **Symptom**: `genLoad` for large aggregates only copied 8 bytes instead of full struct size → ACCESS_VIOLATION crashes
- **Solution**: Replaced fragile multi-pass recomputation with dependency-tracking system (Winchester 1.7)

## 3. **Register Allocation Conflicts**

**Callee-saved register clobbering**:
- `genLoad` hardcoded RSI/RDI as scratch registers for struct copies
- These are callee-saved on Windows x64 and may hold live values from register allocator
- **Symptom**: Crashes in collection types (Set, Map) after Mem2Reg promoted variables to registers
- **Fix**: Use reserved scratch registers R10/R11 instead

## 4. **String Metadata Lifetime**

**Stack-allocated string metadata**:
- `__ManagedStringData` was stack-allocated in non-global contexts
- When strings were copied into returned structs, metadata pointer dangled
- **Fix**: Always heap-allocate string metadata via malloc

**String reassignment leaks**:
- Old `__ManagedStringData` struct leaked when reassigning string variables
- **Fix**: Save and free both old buffer AND old metadata struct

## 5. **Empty Array Initial Allocation Leak**

- `var arr = Array of int` allocated 8 bytes via raw malloc (no refcount header)
- `_managed_memory_release` expected a header, so memory was never freed
- **Fix**: Empty mutable arrays start with `capacity = 0`, `push()` creates proper heap buffer

---

## Pattern of Issues

Most critical bugs share a common theme: **ownership and lifetime management of heap-allocated data within composite types**, particularly when:
1. Copying structs containing pointers to other allocations
2. Computing sizes before all type information is available
3. Mixing stack and heap allocation patterns

The move from LLVM to custom backend (Winchester) introduced these challenges since the team now owns the entire memory model rather than delegating to LLVM's infrastructure.
