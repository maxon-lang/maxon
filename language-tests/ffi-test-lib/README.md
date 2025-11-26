# FFI Test Library

A simple C library for testing Maxon's Safe FFI subprocess isolation.

## Building

### Windows (MSVC, MinGW, or Clang)
```batch
build.bat [debug|release]
```

### Linux/macOS
```bash
chmod +x build.sh
./build.sh [debug|release]
```

## Functions

### Normal Operations
- `add_numbers(a, b)` - Add two integers
- `multiply_floats(a, b)` - Multiply two doubles  
- `string_length(str)` - Get string length
- `factorial(n)` - Compute factorial
- `fibonacci(n)` - Compute fibonacci number

### Crash Functions (for testing isolation)
- `crash_null_deref()` - Dereference null pointer
- `crash_stack_corrupt()` - Buffer overrun
- `crash_divide_by_zero(n)` - Divide by zero
- `crash_stack_overflow(n)` - Infinite recursion

### Timing Functions
- `slow_operation(ms)` - Sleep for milliseconds
- `infinite_loop()` - Never returns

### Memory Functions
- `allocate_and_leak(bytes)` - Allocate without freeing
- `write_to_address(addr, value)` - Write to arbitrary address

### State Functions
- `increment_counter()` - Increment and return global counter
- `get_counter()` - Get current counter value
- `reset_counter()` - Reset counter to zero

## Testing Isolation

The crash functions verify that Maxon's Safe FFI correctly isolates external code:

1. **Normal calls** should work and return correct values
2. **Crash functions** should crash the FFI worker subprocess, not the main Maxon process
3. **Infinite loops** should be terminated by timeout
4. **Memory leaks** should be contained to the subprocess
5. **State functions** test whether subprocess state persists between calls
