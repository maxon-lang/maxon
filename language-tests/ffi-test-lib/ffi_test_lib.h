/**
 * FFI Test Library for Maxon Safe FFI Development
 * 
 * This library provides test functions to validate subprocess isolation:
 * - Normal operations (arithmetic, strings)
 * - Deliberate crashes (null deref, stack corruption)
 * - Timing functions (for timeout testing)
 */

#ifndef FFI_TEST_LIB_H
#define FFI_TEST_LIB_H

#ifdef _WIN32
    #ifdef FFI_TEST_LIB_EXPORTS
        #define FFI_API __declspec(dllexport)
    #else
        #define FFI_API __declspec(dllimport)
    #endif
#else
    #define FFI_API __attribute__((visibility("default")))
#endif

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// ============================================================================
// Normal Operations - Should work correctly
// ============================================================================

/** Add two integers */
FFI_API int32_t add_numbers(int32_t a, int32_t b);

/** Multiply two floats (doubles) */
FFI_API double multiply_floats(double a, double b);

/** Return the length of a null-terminated string */
FFI_API int32_t string_length(const char* str);

/** Compute factorial recursively */
FFI_API int32_t factorial(int32_t n);

/** Compute fibonacci number */
FFI_API int32_t fibonacci(int32_t n);

// ============================================================================
// Crash Functions - Test isolation
// ============================================================================

/** Deliberately dereference null pointer - should crash */
FFI_API int32_t crash_null_deref(void);

/** Deliberately corrupt the stack with buffer overrun */
FFI_API int32_t crash_stack_corrupt(void);

/** Divide by zero */
FFI_API int32_t crash_divide_by_zero(int32_t n);

/** Infinite recursion - stack overflow */
FFI_API int32_t crash_stack_overflow(int32_t depth);

// ============================================================================
// Timing Functions - Test timeouts
// ============================================================================

/** Sleep for specified milliseconds, return input */
FFI_API int32_t slow_operation(int32_t milliseconds);

/** Infinite loop - never returns */
FFI_API int32_t infinite_loop(void);

// ============================================================================
// Memory Functions - Test memory isolation
// ============================================================================

/** Allocate and leak memory (test subprocess memory isolation) */
FFI_API int32_t allocate_and_leak(int32_t bytes);

/** Write to arbitrary address (should crash or be contained) */
FFI_API int32_t write_to_address(void* addr, int32_t value);

// ============================================================================
// State Functions - Test subprocess state
// ============================================================================

/** Increment global counter, return new value */
FFI_API int32_t increment_counter(void);

/** Get current counter value */
FFI_API int32_t get_counter(void);

/** Reset counter to zero */
FFI_API void reset_counter(void);

#ifdef __cplusplus
}
#endif

#endif // FFI_TEST_LIB_H
