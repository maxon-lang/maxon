# Maxon Runtime Library

This directory contains the runtime library for the Maxon programming language. The runtime provides essential functions that are automatically linked with every Maxon program.

## Files

### Core Runtime
- **`runtime.ll`** - Core runtime library (platform-independent)
  - LLVM intrinsic implementations (memset, __chkstk)
  - Math functions (round, trunc, floor, ceil, sin, cos, tan, fmod)

### Platform-Specific Implementations
- **`platform_windows.ll`** - Windows-specific implementations
  - Target triple and floating-point support
  - Memory management (malloc/free via HeapAlloc/HeapFree)
  - Process control (exit via ExitProcess)
  - I/O (write_stdout via GetStdHandle/WriteFile)
  
- **`platform_linux.ll`** - Linux-specific implementations
  - Target triple
  - Memory management (malloc/free via mmap syscall)
  - Process control (exit via syscall)
  - I/O (write/write_stdout via syscall)

### Generated Files
- **`runtime-windows.obj`** - Compiled Windows runtime object file (in `../bin/`)
- **`runtime-linux.o`** - Compiled Linux runtime object file (in `../bin/`)

## Build Process

The runtime is built automatically when running `make runtime` or `make compiler`:

1. The platform-specific file and `runtime.ll` are concatenated:
   - Windows: `platform_windows.ll` + `runtime.ll`
   - Linux: `platform_linux.ll` + `runtime.ll`

2. The combined file is compiled to a platform-specific object file using LLVM's `llc`

3. The linker automatically uses the correct platform-specific runtime object file

## Adding New Runtime Functions

To add a new runtime function:

1. **Platform-independent functions**: Add to `runtime.ll`
2. **Platform-specific functions**: Add to both `platform_windows.ll` and `platform_linux.ll`
3. Run `make runtime` to rebuild
4. The new function will be available in all Maxon programs

## Design Philosophy

The Maxon runtime is **completely self-contained** and does not depend on the C runtime library (libc/msvcrt). All system interactions are implemented using:
- **Windows**: Direct Windows API calls
- **Linux**: Direct system calls via inline assembly

This approach provides:
- Complete control over runtime behavior
- Minimal dependencies
- Predictable performance
- Simplified deployment

## Function Reference

### Memory Management
- `malloc(size)` - Allocate memory
- `free(ptr)` - Free memory

### Process Control
- `exit(code)` - Exit the process

### I/O
- `write_stdout(buf, count)` - Write to stdout
- `write(fd, buf, count)` - Write to file descriptor (Linux only)

### Math Functions
- `memset(dest, val, count)` - Fill memory with constant byte
- `fmod(x, y)` - Floating-point modulo
- `round(x)`, `trunc(x)`, `floor(x)`, `ceil(x)` - Rounding functions
- `sin(x)`, `cos(x)`, `tan(x)` - Trigonometric functions

### Platform Support
- `__chkstk()` - Stack probe for Windows (no-op on other platforms)

## Notes

- The Linux `free()` implementation is currently a no-op (memory is not unmapped). This is acceptable for short-running programs and test cases. A full implementation would require tracking allocation sizes.
- Math functions use simplified implementations suitable for general use. For high-precision scientific computing, more sophisticated implementations may be needed.
