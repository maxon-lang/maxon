// Stub implementations for library functions that LLVM's intrinsic lowering may require
// These are linked with user programs along with runtime.obj

#ifdef _WIN32
#include <windows.h>
#endif

extern "C"
{

    // Math function stubs for LLVM intrinsic lowering
    // These provide basic implementations when LLVM lowers intrinsics to library calls
    double trunc(double x)
    {
        return (double)(long long)x;
    }

    double fmod(double x, double y)
    {
        long long quotient = (long long)(x / y);
        return x - quotient * y;
    }

#ifdef _WIN32
    // Windows implementations using Windows API

    void *malloc(unsigned long long size)
    {
        HANDLE heap = GetProcessHeap();
        return HeapAlloc(heap, 0, size);
    }

    void free(void *ptr)
    {
        HANDLE heap = GetProcessHeap();
        HeapFree(heap, 0, ptr);
    }

    void exit(int code)
    {
        ExitProcess((unsigned int)code);
        while (1)
            ; // Never returns
    }

#else
// Linux implementations using system calls directly (no libc)

// System call numbers for x86_64 Linux
#define SYS_mmap 9
#define SYS_munmap 11
#define SYS_exit 60

// mmap flags
#define PROT_READ 0x1
#define PROT_WRITE 0x2
#define MAP_PRIVATE 0x02
#define MAP_ANONYMOUS 0x20

    // System call wrapper using inline assembly
    static inline long syscall1(long n, long a1)
    {
        long ret;
        asm volatile("syscall" : "=a"(ret) : "a"(n), "D"(a1) : "rcx", "r11", "memory");
        return ret;
    }

    static inline long syscall6(long n, long a1, long a2, long a3, long a4, long a5, long a6)
    {
        long ret;
        register long r10 asm("r10") = a4;
        register long r8 asm("r8") = a5;
        register long r9 asm("r9") = a6;
        asm volatile("syscall"
                     : "=a"(ret)
                     : "a"(n), "D"(a1), "S"(a2), "d"(a3), "r"(r10), "r"(r8), "r"(r9)
                     : "rcx", "r11", "memory");
        return ret;
    }

    // Simple malloc implementation using mmap
    void *malloc(unsigned long long size)
    {
        if (size == 0)
            return nullptr;

        // Round up to page size (4096 bytes)
        unsigned long long page_size = 4096;
        unsigned long long rounded_size = ((size + page_size - 1) / page_size) * page_size;

        // Call mmap: void *mmap(void *addr, size_t length, int prot, int flags, int fd, off_t offset)
        long result = syscall6(SYS_mmap,
                               0,                           // addr (let kernel choose)
                               rounded_size,                // length
                               PROT_READ | PROT_WRITE,      // prot
                               MAP_PRIVATE | MAP_ANONYMOUS, // flags
                               -1,                          // fd (not used with MAP_ANONYMOUS)
                               0);                          // offset

        // mmap returns -1 on error
        if (result < 0 && result >= -4095)
        {
            return nullptr;
        }

        return (void *)result;
    }

    // Free implementation (no-op for now since we don't track sizes)
    // In a real implementation, we'd need to track allocation sizes to call munmap
    void free(void *ptr)
    {
        // For now, we leak memory. This is acceptable for the compiler's use cases
        // (test programs, small allocations). A full implementation would require
        // maintaining a size map.
        (void)ptr;
    }

    // Exit implementation using syscall
    void exit(int code)
    {
        syscall1(SYS_exit, code);
        while (1)
            ; // Never returns, but prevents compiler warning
    }

#endif
}
