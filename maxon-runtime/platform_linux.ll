; Linux-specific implementations using system calls directly (no libc)

target triple = "x86_64-pc-linux-gnu"

; System call numbers for x86_64 Linux
@SYS_write = internal constant i64 1
@SYS_mmap = internal constant i64 9
@SYS_munmap = internal constant i64 11
@SYS_exit = internal constant i64 60

; mmap flags
@PROT_READ = internal constant i32 1
@PROT_WRITE = internal constant i32 2
@MAP_PRIVATE = internal constant i32 2
@MAP_ANONYMOUS = internal constant i32 32

; System call wrapper
; i64 syscall(i64 number, i64 arg1, i64 arg2, i64 arg3, i64 arg4, i64 arg5, i64 arg6)
define internal i64 @syscall6(i64 %number, i64 %arg1, i64 %arg2, i64 %arg3, i64 %arg4, i64 %arg5, i64 %arg6) {
entry:
  %result = call i64 asm sideeffect "syscall",
    "={rax},{rax},{rdi},{rsi},{rdx},{r10},{r8},{r9},~{rcx},~{r11},~{memory}"
    (i64 %number, i64 %arg1, i64 %arg2, i64 %arg3, i64 %arg4, i64 %arg5, i64 %arg6)
  ret i64 %result
}

; malloc - Simple malloc implementation using mmap
; ptr malloc(i64 size)
define ptr @malloc(i64 %size) {
entry:
  ; Check for zero size
  %is_zero = icmp eq i64 %size, 0
  br i1 %is_zero, label %return_null, label %allocate

allocate:
  ; Round up to page size (4096 bytes)
  %page_size = add i64 4096, 0
  %size_plus = add i64 %size, 4095
  %rounded_size = udiv i64 %size_plus, %page_size
  %final_size = mul i64 %rounded_size, %page_size

  ; Call mmap: void *mmap(void *addr, size_t length, int prot, int flags, int fd, off_t offset)
  %sys_mmap = load i64, ptr @SYS_mmap
  %prot_read = load i32, ptr @PROT_READ
  %prot_write = load i32, ptr @PROT_WRITE
  %prot = or i32 %prot_read, %prot_write
  %prot_64 = zext i32 %prot to i64
  
  %map_private = load i32, ptr @MAP_PRIVATE
  %map_anon = load i32, ptr @MAP_ANONYMOUS
  %flags = or i32 %map_private, %map_anon
  %flags_64 = zext i32 %flags to i64
  
  %fd = add i64 -1, 0  ; fd = -1
  %offset = add i64 0, 0  ; offset = 0
  
  %result = call i64 @syscall6(i64 %sys_mmap, i64 0, i64 %final_size, i64 %prot_64, i64 %flags_64, i64 %fd, i64 %offset)
  
  ; Check for error (mmap returns -1 to -4095 on error)
  %is_negative = icmp slt i64 %result, 0
  %error_threshold = add i64 -4095, 0
  %is_error_range = icmp sge i64 %result, %error_threshold
  %is_error = and i1 %is_negative, %is_error_range
  br i1 %is_error, label %return_null, label %return_ptr

return_null:
  ret ptr null

return_ptr:
  %ptr = inttoptr i64 %result to ptr
  ret ptr %ptr
}

; free - Free implementation (no-op for now)
; In a full implementation, we'd need to track allocation sizes to call munmap
; For now, we leak memory (acceptable for test programs and small allocations)
; void free(ptr ptr)
define void @free(ptr %ptr) {
entry:
  ret void
}

; exit - Exit the process using syscall
; void exit(i32 code)
define void @exit(i32 %code) {
entry:
  %sys_exit = load i64, ptr @SYS_exit
  %code_64 = sext i32 %code to i64
  call i64 @syscall6(i64 %sys_exit, i64 %code_64, i64 0, i64 0, i64 0, i64 0, i64 0)
  unreachable
}

; write - Write to file descriptor using syscall
; i64 write(i32 fd, ptr buf, i64 count)
define i64 @write(i32 %fd, ptr %buf, i64 %count) {
entry:
  %sys_write = load i64, ptr @SYS_write
  %fd_64 = sext i32 %fd to i64
  %buf_int = ptrtoint ptr %buf to i64
  %result = call i64 @syscall6(i64 %sys_write, i64 %fd_64, i64 %buf_int, i64 %count, i64 0, i64 0, i64 0)
  ret i64 %result
}

; write_stdout - Write to stdout using write syscall
; i32 write_stdout(ptr buf, i32 count)
define i32 @write_stdout(ptr %buf, i32 %count) {
entry:
  ; Extend count to i64 for write syscall
  %count64 = sext i32 %count to i64
  ; Call write(1, buf, count) - fd 1 is stdout
  %result64 = call i64 @write(i32 1, ptr %buf, i64 %count64)
  ; Truncate result back to i32
  %result = trunc i64 %result64 to i32
  ret i32 %result
}
