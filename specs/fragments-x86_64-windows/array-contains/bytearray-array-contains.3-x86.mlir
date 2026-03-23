module {
  func @stdlib.Print.print(value: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+0]
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-16]
    x86.mov rdx, [rax+0]
    x86.mov rax, [rbp-16]
    x86.mov rbx, [rax+8]
    x86.mov rcx, rdx
    x86.mov rdx, rbx
    x86.call maxon_managed_write_stdout
    x86.epilogue
    x86.ret
  }
  func @main() -> u32 {
  entry:
    x86.prologue stack_size=176
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.mov rsi, 8
    x86.lea_func rdi, [__destruct___ManagedMemory_ByteArray]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-8], rax
    x86.mov r9, [rbp-8]
    x86.xor rax, rax
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-8]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-8]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+24]
    x86.lea_symdata rax, [__mm_panic_element_size_zero]
    x86.mov rdx, rax
    x86.mov r8, rdx
    x86.mov rdx, rcx
    x86.xor rcx, rcx
    x86.call maxon_bounds_check
    x86.lea_func rax, [__destruct_ByteArrayArray]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 2
    x86.call mm_alloc
    x86.mov [rbp-16], rax
    x86.mov rax, [rbp-16]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+8], rax
    x86.mov rcx, [rbp-8]
    x86.call mm_incref
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.lea_rdata rax, [__bstr_0]
    x86.mov rcx, rax
    x86.mov rax, 5
    x86.lea_func rdx, [__destruct_ByteArray]
    x86.mov rbx, rdx
    x86.mov [rbp-152], rcx
    x86.mov rdx, rbx
    x86.mov rcx, 16
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-24], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-24]
    x86.mov [rcx+0], rax
    x86.lea_func rax, [__destruct___ManagedMemory]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-32], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-152]
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, 5
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-32]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-32]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-24]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-24]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rbp-24]
    x86.call ByteArrayArray.push
    x86.lea_rdata rax, [__bstr_1]
    x86.mov rcx, rax
    x86.mov rax, 5
    x86.lea_func rdx, [__destruct_ByteArray]
    x86.mov rbx, rdx
    x86.mov [rbp-160], rcx
    x86.mov rdx, rbx
    x86.mov rcx, 16
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-40], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+0], rax
    x86.lea_func rax, [__destruct___ManagedMemory]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-48], rax
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-160]
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-48]
    x86.mov rcx, 5
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-48]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-48]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-40]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rbp-40]
    x86.call ByteArrayArray.push
    x86.lea_rdata rax, [__bstr_0]
    x86.mov rcx, rax
    x86.mov rax, 5
    x86.lea_func rdx, [__destruct_ByteArray]
    x86.mov rbx, rdx
    x86.mov [rbp-168], rcx
    x86.mov rdx, rbx
    x86.mov rcx, 16
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-56], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-56]
    x86.mov [rcx+0], rax
    x86.lea_func rax, [__destruct___ManagedMemory]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-64], rax
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-168]
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-64]
    x86.mov rcx, 5
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-64]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-64]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-56]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-64]
    x86.call mm_incref
    x86.mov rax, [rbp-56]
    x86.mov rcx, [rbp-56]
    x86.call mm_incref
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-56]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rbp-56]
    x86.call ByteArrayArray.contains$sequence
    x86.test rax, rax
    x86.je main.notFound_1
  found_0:
    x86.lea_rdata rax, [__str_3]
    x86.mov rcx, rax
    x86.mov rdx, 6
    x86.lea_func rbx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-152], rcx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 5
    x86.call mm_alloc
    x86.mov [rbp-72], rax
    x86.lea_func rdi, [__destruct___ManagedMemory]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-80], rax
    x86.mov r9, [rbp-80]
    x86.mov rax, [rbp-152]
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-80]
    x86.mov rcx, 6
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-80]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-80]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-80]
    x86.mov rcx, [rbp-72]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-80]
    x86.mov rcx, [rbp-80]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, [rbp-72]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-72]
    x86.mov rcx, [rbp-72]
    x86.call mm_incref
    x86.mov rax, [rbp-72]
    x86.mov rcx, [rbp-72]
    x86.call stdlib.Print.print
    x86.mov rax, [rbp-72]
    x86.test rax, rax
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-72]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.jmp main.found_0.merge
  notFound_1:
    x86.lea_rdata rax, [__str_4]
    x86.mov rcx, rax
    x86.mov rdx, 10
    x86.lea_func rbx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-152], rcx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 5
    x86.call mm_alloc
    x86.mov [rbp-88], rax
    x86.lea_func rdi, [__destruct___ManagedMemory]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-96], rax
    x86.mov r9, [rbp-96]
    x86.mov rax, [rbp-152]
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-96]
    x86.mov rcx, 10
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-96]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-96]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-96]
    x86.mov rcx, [rbp-88]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-96]
    x86.mov rcx, [rbp-96]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, [rbp-88]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-88]
    x86.mov rcx, [rbp-88]
    x86.call mm_incref
    x86.mov rax, [rbp-88]
    x86.mov rcx, [rbp-88]
    x86.call stdlib.Print.print
    x86.mov rax, [rbp-88]
    x86.test rax, rax
    x86.jz __nonnull_skip_1
    x86.mov rcx, [rbp-88]
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.jmp main.found_0.merge
  found_0.merge:
    x86.lea_rdata rax, [__bstr_5]
    x86.mov rcx, rax
    x86.mov rdx, 7
    x86.lea_func rbx, [__destruct_ByteArray]
    x86.mov rsi, rbx
    x86.mov [rbp-152], rcx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-104], rax
    x86.xor rdi, rdi
    x86.mov r8, [rbp-104]
    x86.mov [r8+0], rdi
    x86.lea_func r9, [__destruct___ManagedMemory]
    x86.mov rax, r9
    x86.mov rdx, rax
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-112], rax
    x86.mov rax, [rbp-112]
    x86.mov rcx, [rbp-152]
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-112]
    x86.mov rcx, 7
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-112]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-112]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-112]
    x86.mov rcx, [rbp-104]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-112]
    x86.mov rcx, [rbp-112]
    x86.call mm_incref
    x86.mov rax, [rbp-104]
    x86.mov rcx, [rbp-104]
    x86.call mm_incref
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-104]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rbp-104]
    x86.call ByteArrayArray.contains$sequence
    x86.test rax, rax
    x86.je main.notFound2_3
  found2_2:
    x86.lea_rdata rax, [__str_3]
    x86.mov rcx, rax
    x86.mov rdx, 6
    x86.lea_func rbx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-152], rcx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 5
    x86.call mm_alloc
    x86.mov [rbp-120], rax
    x86.lea_func rdi, [__destruct___ManagedMemory]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-128], rax
    x86.mov r9, [rbp-128]
    x86.mov rax, [rbp-152]
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-128]
    x86.mov rcx, 6
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-128]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-128]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-128]
    x86.mov rcx, [rbp-120]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-128]
    x86.mov rcx, [rbp-128]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, [rbp-120]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-120]
    x86.mov rcx, [rbp-120]
    x86.call mm_incref
    x86.mov rax, [rbp-120]
    x86.mov rcx, [rbp-120]
    x86.call stdlib.Print.print
    x86.mov rax, [rbp-120]
    x86.test rax, rax
    x86.jz __nonnull_skip_2
    x86.mov rcx, [rbp-120]
    x86.call mm_decref
    x86.label __nonnull_skip_2
    x86.jmp main.found2_2.merge
  notFound2_3:
    x86.lea_rdata rax, [__str_4]
    x86.mov rcx, rax
    x86.mov rdx, 10
    x86.lea_func rbx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-152], rcx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 5
    x86.call mm_alloc
    x86.mov [rbp-136], rax
    x86.lea_func rdi, [__destruct___ManagedMemory]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-144], rax
    x86.mov r9, [rbp-144]
    x86.mov rax, [rbp-152]
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-144]
    x86.mov rcx, 10
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-144]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-144]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-144]
    x86.mov rcx, [rbp-136]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-144]
    x86.mov rcx, [rbp-144]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, [rbp-136]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-136]
    x86.mov rcx, [rbp-136]
    x86.call mm_incref
    x86.mov rax, [rbp-136]
    x86.mov rcx, [rbp-136]
    x86.call stdlib.Print.print
    x86.mov rax, [rbp-136]
    x86.test rax, rax
    x86.jz __nonnull_skip_3
    x86.mov rcx, [rbp-136]
    x86.call mm_decref
    x86.label __nonnull_skip_3
    x86.jmp main.found2_2.merge
  found2_2.merge:
    x86.mov rax, [rbp-104]
    x86.test rax, rax
    x86.jz __nonnull_skip_4
    x86.mov rcx, [rbp-104]
    x86.call mm_decref
    x86.label __nonnull_skip_4
    x86.mov rcx, [rbp-56]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_5
    x86.call mm_decref
    x86.label __nonnull_skip_5
    x86.mov rdx, [rbp-40]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_6
    x86.mov rcx, [rbp-40]
    x86.call mm_decref
    x86.label __nonnull_skip_6
    x86.mov rbx, [rbp-24]
    x86.test rbx, rbx
    x86.jz __nonnull_skip_7
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_7
    x86.mov rsi, [rbp-16]
    x86.test rsi, rsi
    x86.jz __nonnull_skip_8
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_8
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  }
  func @ByteArray.clone(__self_ptr: i64) -> i64 {
  entry:
    x86.prologue stack_size=80
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+8]
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-16]
    x86.mov rdx, [rax+8]
    x86.xor rax, rax
    x86.mov rbx, [rbp-16]
    x86.mov rsi, [rbx+8]
    x86.mov rbx, 1
    x86.add rsi, rbx
    x86.lea_symdata rdi, [__mm_panic_slice_oob]
    x86.mov r8, rdi
    x86.mov [rbp-48], rdx
    x86.mov rcx, [rbp-48]
    x86.mov rdx, rsi
    x86.call maxon_bounds_check
    x86.mov rcx, 1
    x86.mov rdx, [rbp-48]
    x86.lea rbx, [rdx + rcx]
    x86.lea_symdata rsi, [__mm_panic_slice_oob]
    x86.mov rdi, rsi
    x86.mov rdx, rbx
    x86.mov r8, rdi
    x86.xor rcx, rcx
    x86.call maxon_bounds_check
    x86.mov r8, [rbp-16]
    x86.mov r9, [r8+0]
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+24]
    x86.xor rax, rax
    x86.mov rdx, rax
    x86.imul rdx, rcx
    x86.add r9, rdx
    x86.mov rdx, [rbp-48]
    x86.sub rdx, rax
    x86.mov rax, rdx
    x86.imul rax, rcx
    x86.lea_func rbx, [__destruct___ManagedMemory]
    x86.mov rsi, rbx
    x86.mov [rbp-56], rax
    x86.mov [rbp-64], rcx
    x86.mov [rbp-72], rdx
    x86.mov [rbp-80], r9
    x86.mov rdx, rsi
    x86.mov rcx, 32
    x86.mov r8, 6
    x86.call mm_alloc
    x86.mov [rbp-24], rax
    x86.mov rcx, [rbp-56]
    x86.call mm_raw_alloc
    x86.mov rcx, [rbp-80]
    x86.mov rdx, [rbp-56]
    x86.mov rsi, rcx
    x86.mov rdi, rax
    x86.mov rcx, rdx
    x86.rep_movsb
    x86.mov rcx, [rbp-24]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-72]
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-24]
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-64]
    x86.mov [rax+24], rcx
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov rax, [rbp-24]
    x86.mov [rbp-32], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.xor rax, rax
    x86.lea_func rcx, [__destruct_ByteArray]
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-40], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+8], rax
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.mov rax, [rbp-40]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-24]
    x86.test rax, rax
    x86.jz __nonnull_skip_9
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_9
    x86.mov rax, [rbp-32]
    x86.test rax, rax
    x86.jz __nonnull_skip_10
    x86.mov rcx, [rbp-32]
    x86.call mm_decref
    x86.label __nonnull_skip_10
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.mov rax, [rbp-40]
    x86.epilogue
    x86.ret
  }
  func @ByteArray.equals(__self_ptr: i64, other: i64) -> i1 {
  entry:
    x86.prologue stack_size=96
    x86.mov [rbp-8], rcx
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-24], rbx
    x86.mov rax, [rbp-24]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-32], rbx
    x86.mov rax, [rbp-16]
    x86.mov rsi, [rax+8]
    x86.mov [rbp-40], rsi
    x86.mov rax, [rbp-40]
    x86.mov rsi, [rax+8]
    x86.cmp rbx, rsi
    x86.je ByteArray.equals.len_0.after
  len_0:
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  len_0.after:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-48], rcx
    x86.mov rdx, [rbp-48]
    x86.mov rbx, [rdx+24]
    x86.mov rsi, [rbp-32]
    x86.imul rsi, rbx
    x86.xor rdi, rdi
    x86.mov [rbp-56], rsi
    x86.mov [rbp-64], rdi
    x86.jmp ByteArray.equals.cmp_1.header
  cmp_1.header:
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-56]
    x86.cmp rax, rcx
    x86.jge ByteArray.equals.cmp_1.exit
  cmp_1:
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+8]
    x86.mov [rbp-72], rdx
    x86.mov rbx, [rbp-72]
    x86.mov rsi, [rbx+8]
    x86.mov rdi, [rbp-72]
    x86.mov r8, [rdi+24]
    x86.imul rsi, r8
    x86.lea_symdata r9, [__mm_panic_byte_oob]
    x86.mov rcx, r9
    x86.mov rdx, rsi
    x86.mov r8, rcx
    x86.mov rcx, [rbp-64]
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-72]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-64]
    x86.add rcx, rax
    x86.movzx rdx, byte ptr [rcx+0]
    x86.mov rcx, [rbp-16]
    x86.mov rbx, [rcx+8]
    x86.mov [rbp-80], rbx
    x86.mov rcx, [rbp-80]
    x86.mov rbx, [rcx+8]
    x86.mov rcx, [rbp-80]
    x86.mov rsi, [rcx+24]
    x86.imul rbx, rsi
    x86.lea_symdata rcx, [__mm_panic_byte_oob]
    x86.mov rsi, rcx
    x86.mov [rbp-88], rdx
    x86.mov rcx, [rbp-64]
    x86.mov rdx, rbx
    x86.mov r8, rsi
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-80]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-64]
    x86.add rcx, rax
    x86.movzx rax, byte ptr [rcx+0]
    x86.mov rcx, [rbp-88]
    x86.cmp rcx, rax
    x86.je ByteArray.equals.ne_2.after
  ne_2:
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  ne_2.after:
    x86.jmp ByteArray.equals.cmp_1.incr
  cmp_1.incr:
    x86.mov rax, 1
    x86.mov rcx, [rbp-64]
    x86.add rcx, rax
    x86.mov [rbp-64], rcx
    x86.jmp ByteArray.equals.cmp_1.header
  cmp_1.exit:
    x86.mov rax, 1
    x86.epilogue
    x86.ret
  }
  func @ByteArrayArray.count(__self_ptr: i64) -> u64 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+8]
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-16]
    x86.mov rdx, [rax+8]
    x86.xor rax, rax
    x86.cmp rdx, rax
    x86.jge ByteArrayArray.count.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_2]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov rax, rdx
    x86.epilogue
    x86.ret
  }
  func @ByteArrayArray.get(__self_ptr: i64, index: i64) -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.mov [rbp-8], rcx
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-24], rbx
    x86.mov rax, [rbp-24]
    x86.mov rbx, [rax+8]
    x86.cmp rdx, rbx
    x86.jb ByteArrayArray.get.upper_0.after
  upper_0:
    x86.xor rax, rax
    x86.mov rcx, 1
    x86.add rax, rcx
    x86.mov rdx, rax
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  upper_0.after:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-32], rcx
    x86.mov rdx, [rbp-16]
    x86.mov rbx, [rbp-32]
    x86.mov rsi, [rbx+8]
    x86.lea_symdata rdi, [__mm_panic_index_oob]
    x86.mov r8, rdi
    x86.mov rcx, [rbp-16]
    x86.mov rdx, rsi
    x86.call maxon_bounds_check
    x86.mov r9, [rbp-32]
    x86.mov rax, [r9+24]
    x86.mov rcx, [rbp-32]
    x86.mov rdx, [rcx+0]
    x86.mov rcx, [rbp-16]
    x86.imul rcx, rax
    x86.add rdx, rcx
    x86.mov rax, [rdx+0]
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.mov [rbp-48], rax
    x86.jne ByteArrayArray.get.__slot_nonnull_429
  __slot_empty_429:
    x86.mov rdx, 2
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  __slot_nonnull_429:
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
    x86.mov rax, [rbp-48]
    x86.mov [rbp-40], rax
    x86.mov rax, [rbp-40]
    x86.xor rdx, rdx
    x86.epilogue
    x86.ret
  }
  func @ByteArrayArray.ensureCapacity(__self_ptr: i64, requiredLen: i64) {
  entry:
    x86.prologue stack_size=80
    x86.mov [rbp-8], rcx
    x86.mov [rbp-24], rdx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-16], rbx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-32], rbx
    x86.mov rax, [rbp-32]
    x86.mov rbx, [rax+16]
    x86.mov [rbp-40], rbx
    x86.cmp rdx, rbx
    x86.jbe ByteArrayArray.ensureCapacity.grow_0.merge
  grow_0:
    x86.mov rax, [rbp-40]
    x86.mov [rbp-48], rax
    x86.mov rcx, 4
    x86.cmp rax, rcx
    x86.jge ByteArrayArray.ensureCapacity.min_1.merge
  min_1:
    x86.mov rax, 4
    x86.mov [rbp-48], rax
    x86.jmp ByteArrayArray.ensureCapacity.min_1.merge
  min_1.merge:
    x86.jmp ByteArrayArray.ensureCapacity.double_2.header
  double_2.header:
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-24]
    x86.cmp rax, rcx
    x86.jge ByteArrayArray.ensureCapacity.double_2.exit
  double_2:
    x86.mov rax, 2
    x86.mov rcx, [rbp-48]
    x86.imul rcx, rax
    x86.mov [rbp-48], rcx
    x86.jmp ByteArrayArray.ensureCapacity.double_2.header
  double_2.exit:
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rcx+24]
    x86.mov rbx, [rbp-16]
    x86.mov rsi, [rbx+16]
    x86.mov rdi, 1
    x86.lea r8, [rax + rdi]
    x86.lea_symdata r9, [__mm_panic_grow_shrink]
    x86.mov rcx, r9
    x86.mov [rbp-64], rdx
    x86.mov rdx, r8
    x86.mov r8, rcx
    x86.mov rcx, rsi
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-16]
    x86.mov rdx, [rax+16]
    x86.mov rax, [rbp-16]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-56], rbx
    x86.mov rax, [rbp-16]
    x86.mov rsi, [rbp-64]
    x86.imul rbx, rsi
    x86.mov [rbp-72], rdx
    x86.mov r8, rbx
    x86.mov r9, [rbp-16]
    x86.call maxon_cow_check
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-72]
    x86.cmp rcx, rax
    x86.sete rax
    x86.movzx rax, raxb
    x86.mov rdx, [rbp-56]
    x86.test rax, rax
    x86.mov rbx, rcx
    x86.cmovne rbx, rdx
    x86.mov rax, [rbp-16]
    x86.mov [rax+16], rbx
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-64]
    x86.mov rdx, [rbp-48]
    x86.mov rbx, rdx
    x86.imul rbx, rax
    x86.mov rax, [rbp-16]
    x86.mov rdx, rbx
    x86.mov r8, [rbp-16]
    x86.call mm_raw_realloc
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-48]
    x86.mov [rax+16], rcx
    x86.jmp ByteArrayArray.ensureCapacity.grow_0.merge
  grow_0.merge:
    x86.epilogue
    x86.ret
  }
  func @ByteArrayArray.push(__self_ptr: i64, value: i64) {
  entry:
    x86.prologue stack_size=96
    x86.mov [rbp-8], rcx
    x86.mov [rbp-24], rdx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-16], rbx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-32], rbx
    x86.mov rax, [rbp-32]
    x86.mov rbx, [rax+8]
    x86.mov rax, 1
    x86.lea rsi, [rbx + rax]
    x86.mov rax, [rbp-8]
    x86.mov [rbp-48], rbx
    x86.mov rcx, [rbp-8]
    x86.mov rdx, rsi
    x86.call ByteArrayArray.ensureCapacity
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rcx+24]
    x86.mov rbx, [rbp-16]
    x86.mov rsi, [rbx+0]
    x86.mov rdi, [rbp-16]
    x86.mov r8, [rdi+16]
    x86.mov r9, [rbp-16]
    x86.mov rax, [r9+8]
    x86.mov [rbp-40], rax
    x86.mov rcx, [rbp-16]
    x86.imul rax, rdx
    x86.mov [rbp-56], rdx
    x86.mov [rbp-64], r8
    x86.mov rcx, rsi
    x86.mov rdx, [rbp-64]
    x86.mov r8, rax
    x86.mov r9, [rbp-16]
    x86.call maxon_cow_check
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-64]
    x86.cmp rcx, rax
    x86.sete rax
    x86.movzx rax, raxb
    x86.mov rdx, [rbp-40]
    x86.test rax, rax
    x86.mov rbx, rcx
    x86.cmovne rbx, rdx
    x86.mov rax, [rbp-16]
    x86.mov [rax+16], rbx
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+16]
    x86.lea_symdata rax, [__mm_panic_index_oob]
    x86.mov rdx, rax
    x86.mov r8, rdx
    x86.mov rdx, rcx
    x86.mov rcx, [rbp-48]
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-56]
    x86.mov rdx, [rbp-48]
    x86.mov rbx, rdx
    x86.imul rbx, rax
    x86.add rcx, rbx
    x86.mov rax, [rcx+0]
    x86.mov [rbp-72], rax
    x86.mov [rbp-80], rcx
    x86.test rax, rax
    x86.jz __nonnull_skip_11
    x86.mov rcx, [rbp-72]
    x86.call mm_decref
    x86.label __nonnull_skip_11
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-80]
    x86.mov [rcx+0], rax
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov rax, 1
    x86.mov rcx, [rbp-48]
    x86.add rcx, rax
    x86.mov rax, [rbp-16]
    x86.mov rdx, [rax+16]
    x86.mov rax, 1
    x86.add rdx, rax
    x86.lea_symdata rax, [__mm_panic_setlength_oob]
    x86.mov rbx, rax
    x86.mov [rbp-88], rcx
    x86.mov r8, rbx
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-88]
    x86.mov [rax+8], rcx
    x86.epilogue
    x86.ret
  }
  func @ByteArrayArray.contains$sequence(__self_ptr: i64, sequence: i64) -> i1 {
  entry:
    x86.prologue stack_size=128
    x86.mov [rbp-8], rcx
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call ByteArrayArray.count
    x86.mov [rbp-24], rax
    x86.mov rcx, [rbp-16]
    x86.call ByteArrayArray.count
    x86.mov [rbp-32], rax
    x86.xor rdx, rdx
    x86.cmp rax, rdx
    x86.jne ByteArrayArray.contains$sequence.empty_0.after
  empty_0:
    x86.mov rax, 1
    x86.epilogue
    x86.ret
  empty_0.after:
    x86.xor rax, rax
    x86.mov rcx, [rbp-24]
    x86.mov rdx, [rbp-32]
    x86.sub rcx, rdx
    x86.mov [rbp-40], rcx
    x86.mov [rbp-48], rax
    x86.jmp ByteArrayArray.contains$sequence.scan_1.header
  scan_1.header:
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-40]
    x86.cmp rax, rcx
    x86.jg ByteArrayArray.contains$sequence.scan_1.exit
  scan_1:
    x86.mov rax, [rbp-48]
    x86.mov [rbp-56], rax
    x86.mov rcx, 1
    x86.mov [rbp-57], rcx
    x86.xor rdx, rdx
    x86.mov rbx, [rbp-32]
    x86.mov [rbp-65], rbx
    x86.mov [rbp-73], rdx
    x86.jmp ByteArrayArray.contains$sequence.cmp_2.header
  cmp_2.header:
    x86.mov rax, [rbp-73]
    x86.mov rcx, [rbp-65]
    x86.cmp rax, rcx
    x86.jge ByteArrayArray.contains$sequence.cmp_2.exit
  cmp_2:
    x86.mov rax, [rbp-73]
    x86.mov [rbp-81], rax
    x86.mov rcx, [rbp-56]
    x86.add rcx, rax
    x86.mov rdx, [rbp-8]
    x86.mov rdx, rcx
    x86.mov rcx, [rbp-8]
    x86.call ByteArrayArray.get
    x86.mov [rbp-89], rax
    x86.xor rbx, rbx
    x86.cmp rdx, rbx
    x86.je ByteArrayArray.contains$sequence.otherwise_continue_4
  otherwise_error_3:
    x86.mov rax, [rbp-89]
    x86.test rax, rax
    x86.jz __nonnull_skip_12
    x86.mov rcx, [rbp-89]
    x86.call mm_decref
    x86.label __nonnull_skip_12
    x86.jmp ByteArrayArray.contains$sequence.cmp_2.exit
  otherwise_continue_4:
    x86.mov rax, [rbp-89]
    x86.mov [rbp-97], rax
    x86.mov rcx, [rbp-97]
    x86.call mm_incref
    x86.mov rdx, [rbp-81]
    x86.mov rbx, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call ByteArrayArray.get
    x86.mov [rbp-105], rax
    x86.xor rsi, rsi
    x86.cmp rdx, rsi
    x86.je ByteArrayArray.contains$sequence.otherwise_continue_8
  otherwise_error_7:
    x86.mov rax, [rbp-105]
    x86.test rax, rax
    x86.jz __nonnull_skip_13
    x86.mov rcx, [rbp-105]
    x86.call mm_decref
    x86.label __nonnull_skip_13
    x86.mov rcx, [rbp-97]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_14
    x86.call mm_decref
    x86.label __nonnull_skip_14
    x86.mov rdx, [rbp-89]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_15
    x86.mov rcx, [rbp-89]
    x86.call mm_decref
    x86.label __nonnull_skip_15
    x86.jmp ByteArrayArray.contains$sequence.cmp_2.exit
  otherwise_continue_8:
    x86.mov rax, [rbp-105]
    x86.mov [rbp-113], rax
    x86.mov rcx, [rbp-113]
    x86.call mm_incref
    x86.mov rdx, [rbp-97]
    x86.mov rbx, [rbp-113]
    x86.mov rcx, [rbp-97]
    x86.mov rdx, [rbp-113]
    x86.call ByteArray.equals
    x86.mov rsi, 1
    x86.xor rax, rsi
    x86.test rax, rax
    x86.je ByteArrayArray.contains$sequence.ne_11.after
  ne_11:
    x86.xor rax, rax
    x86.mov [rbp-57], rax
    x86.mov rcx, [rbp-113]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_16
    x86.call mm_decref
    x86.label __nonnull_skip_16
    x86.mov rdx, [rbp-105]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_17
    x86.mov rcx, [rbp-105]
    x86.call mm_decref
    x86.label __nonnull_skip_17
    x86.mov rbx, [rbp-97]
    x86.test rbx, rbx
    x86.jz __nonnull_skip_18
    x86.mov rcx, [rbp-97]
    x86.call mm_decref
    x86.label __nonnull_skip_18
    x86.mov rsi, [rbp-89]
    x86.test rsi, rsi
    x86.jz __nonnull_skip_19
    x86.mov rcx, [rbp-89]
    x86.call mm_decref
    x86.label __nonnull_skip_19
    x86.jmp ByteArrayArray.contains$sequence.cmp_2.exit
  ne_11.after:
    x86.mov rax, [rbp-113]
    x86.test rax, rax
    x86.jz __nonnull_skip_20
    x86.mov rcx, [rbp-113]
    x86.call mm_decref
    x86.label __nonnull_skip_20
    x86.mov rcx, [rbp-105]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_21
    x86.call mm_decref
    x86.label __nonnull_skip_21
    x86.mov rdx, [rbp-97]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_22
    x86.mov rcx, [rbp-97]
    x86.call mm_decref
    x86.label __nonnull_skip_22
    x86.mov rbx, [rbp-89]
    x86.test rbx, rbx
    x86.jz __nonnull_skip_23
    x86.mov rcx, [rbp-89]
    x86.call mm_decref
    x86.label __nonnull_skip_23
    x86.jmp ByteArrayArray.contains$sequence.cmp_2.incr
  cmp_2.incr:
    x86.mov rax, 1
    x86.mov rcx, [rbp-73]
    x86.add rcx, rax
    x86.mov [rbp-73], rcx
    x86.jmp ByteArrayArray.contains$sequence.cmp_2.header
  cmp_2.exit:
    x86.mov rax, [rbp-57]
    x86.test rax, rax
    x86.je ByteArrayArray.contains$sequence.found_12.after
  found_12:
    x86.mov rax, 1
    x86.epilogue
    x86.ret
  found_12.after:
    x86.jmp ByteArrayArray.contains$sequence.scan_1.incr
  scan_1.incr:
    x86.mov rax, 1
    x86.mov rcx, [rbp-48]
    x86.add rcx, rax
    x86.mov [rbp-48], rcx
    x86.jmp ByteArrayArray.contains$sequence.scan_1.header
  scan_1.exit:
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  }
  func @__destruct___ManagedMemory_ByteArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory_ByteArray.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_managed_elements
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+0]
    x86.mov rcx, rdx
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory_ByteArray.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory_ByteArray.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_ByteArrayArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_24
    x86.call mm_decref
    x86.label __nonnull_skip_24
    x86.jmp __destruct_ByteArrayArray.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_ByteArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_25
    x86.call mm_decref
    x86.label __nonnull_skip_25
    x86.jmp __destruct_ByteArray.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_String(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_26
    x86.call mm_decref
    x86.label __nonnull_skip_26
    x86.jmp __destruct_String.done
  done:
    x86.epilogue
    x86.ret
  }
}
