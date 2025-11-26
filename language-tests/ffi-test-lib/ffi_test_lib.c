/**
 * FFI Test Library for Maxon Safe FFI Development
 * 
 * Implementation of test functions for validating subprocess isolation.
 */

#define FFI_TEST_LIB_EXPORTS
#include "ffi_test_lib.h"

#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
    #include <windows.h>
    #define SLEEP_MS(ms) Sleep(ms)
#else
    #include <unistd.h>
    #define SLEEP_MS(ms) usleep((ms) * 1000)
#endif

// Global counter for state testing
static int32_t g_counter = 0;

// ============================================================================
// Normal Operations
// ============================================================================

FFI_API int32_t add_numbers(int32_t a, int32_t b) {
    return a + b;
}

FFI_API double multiply_floats(double a, double b) {
    return a * b;
}

FFI_API int32_t string_length(const char* str) {
    if (str == NULL) return 0;
    return (int32_t)strlen(str);
}

FFI_API int32_t factorial(int32_t n) {
    if (n <= 1) return 1;
    return n * factorial(n - 1);
}

FFI_API int32_t fibonacci(int32_t n) {
    if (n <= 0) return 0;
    if (n == 1) return 1;
    return fibonacci(n - 1) + fibonacci(n - 2);
}

// ============================================================================
// Crash Functions
// ============================================================================

FFI_API int32_t crash_null_deref(void) {
    volatile int* ptr = NULL;
    return *ptr;  // Deliberate null dereference
}

FFI_API int32_t crash_stack_corrupt(void) {
    char buffer[8];
    // Deliberate buffer overrun - write way past the buffer
    for (int i = 0; i < 1000; i++) {
        buffer[i] = 'X';
    }
    return 0;
}

FFI_API int32_t crash_divide_by_zero(int32_t n) {
    volatile int zero = 0;
    return n / zero;  // Deliberate divide by zero
}

FFI_API int32_t crash_stack_overflow(int32_t depth) {
    char buffer[4096];  // Eat stack space
    buffer[0] = (char)depth;
    return crash_stack_overflow(depth + 1);  // Infinite recursion
}

// ============================================================================
// Timing Functions
// ============================================================================

FFI_API int32_t slow_operation(int32_t milliseconds) {
    SLEEP_MS(milliseconds);
    return milliseconds;
}

FFI_API int32_t infinite_loop(void) {
    while (1) {
        // Spin forever
    }
    return 0;  // Never reached
}

// ============================================================================
// Memory Functions
// ============================================================================

FFI_API int32_t allocate_and_leak(int32_t bytes) {
    void* ptr = malloc(bytes);
    if (ptr == NULL) return 0;
    memset(ptr, 0xAB, bytes);
    // Deliberately leak - don't free
    return bytes;
}

FFI_API int32_t write_to_address(void* addr, int32_t value) {
    if (addr == NULL) return -1;
    *((volatile int32_t*)addr) = value;
    return value;
}

// ============================================================================
// State Functions
// ============================================================================

FFI_API int32_t increment_counter(void) {
    return ++g_counter;
}

FFI_API int32_t get_counter(void) {
    return g_counter;
}

FFI_API void reset_counter(void) {
    g_counter = 0;
}
