module {
  func @stdlib.helpers.string._hash.hashManagedStringRange(managed: i64, offset: i64, len: i64) -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-8], rcx
    x86.mov [rbp-16], rdx
    x86.mov rax, 5381
    x86.mov [rbp-24], rax
    x86.xor rax, rax
    x86.mov [rbp-32], r8
    x86.mov [rbp-40], rax
    x86.jmp stdlib.helpers.string._hash.hashManagedStringRange.hash_loop_0.header
  hash_loop_0.header:
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-32]
    x86.cmp rax, rcx
    x86.jge stdlib.helpers.string._hash.hashManagedStringRange.hash_loop_0.exit
  hash_loop_0:
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-16]
    x86.add rcx, rax
    x86.mov rdx, [rbp-8]
    x86.mov rbx, [rdx+8]
    x86.mov rsi, [rbp-8]
    x86.mov rdi, [rsi+24]
    x86.imul rbx, rdi
    x86.lea_symdata r8, [__mm_panic_byte_oob]
    x86.mov r9, r8
    x86.mov [rbp-56], rcx
    x86.mov rdx, rbx
    x86.mov r8, r9
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-56]
    x86.add rcx, rax
    x86.movzx rax, byte ptr [rcx+0]
    x86.mov rcx, 33
    x86.mov rdx, [rbp-24]
    x86.imul rdx, rcx
    x86.add rdx, rax
    x86.mov [rbp-24], rdx
    x86.jmp stdlib.helpers.string._hash.hashManagedStringRange.hash_loop_0.incr
  hash_loop_0.incr:
    x86.mov rax, 1
    x86.mov rcx, [rbp-40]
    x86.add rcx, rax
    x86.mov [rbp-40], rcx
    x86.jmp stdlib.helpers.string._hash.hashManagedStringRange.hash_loop_0.header
  hash_loop_0.exit:
    x86.mov rax, 4294967295
    x86.mov rcx, [rbp-24]
    x86.and rcx, rax
    x86.mov [rbp-48], rcx
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.setl rbx
    x86.movzx rbx, rbxb
    x86.mov rsi, 4294967295
    x86.cmp rcx, rsi
    x86.setg rdi
    x86.movzx rdi, rdib
    x86.or rbx, rdi
    x86.test rbx, rbx
    x86.je stdlib.helpers.string._hash.hashManagedStringRange.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_1068]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov rax, [rbp-48]
    x86.epilogue
    x86.ret
  }
  func @stdlib.helpers.string._hash.hashManagedString(managed: i64) -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+8]
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.mov r8, rdx
    x86.xor rdx, rdx
    x86.call stdlib.helpers.string._hash.hashManagedStringRange
    x86.mov ecx, rax
    x86.mov [rbp-16], rcx
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.setl rbx
    x86.movzx rbx, rbxb
    x86.mov rsi, 4294967295
    x86.cmp rcx, rsi
    x86.setg rdi
    x86.movzx rdi, rdib
    x86.or rbx, rdi
    x86.test rbx, rbx
    x86.je stdlib.helpers.string._hash.hashManagedString.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_1081]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov rax, [rbp-16]
    x86.epilogue
    x86.ret
  }
  func @stdlib.String.clone(__self_ptr: i64) -> i64 {
  entry:
    x86.prologue stack_size=80
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+0]
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
    x86.mov r8, 1
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
    x86.lea_func rcx, [__destruct_String]
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 2
    x86.call mm_alloc
    x86.mov [rbp-40], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+0], rax
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.mov rax, [rbp-40]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-24]
    x86.test rax, rax
    x86.jz __stdlib_nn_skip_0
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __stdlib_nn_skip_0
    x86.mov rax, [rbp-32]
    x86.test rax, rax
    x86.jz __stdlib_nn_skip_1
    x86.mov rcx, [rbp-32]
    x86.call mm_decref
    x86.label __stdlib_nn_skip_1
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.mov rax, [rbp-40]
    x86.epilogue
    x86.ret
  }
  func @stdlib.String.hash(__self_ptr: i64) -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+0]
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call stdlib.helpers.string._hash.hashManagedString
    x86.mov ecx, rax
    x86.mov [rbp-24], rcx
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.setl rbx
    x86.movzx rbx, rbxb
    x86.mov rsi, 4294967295
    x86.cmp rcx, rsi
    x86.setg rdi
    x86.movzx rdi, rdib
    x86.or rbx, rdi
    x86.test rbx, rbx
    x86.je stdlib.String.hash.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_5246]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov rax, [rbp-24]
    x86.epilogue
    x86.ret
  }
  func @stdlib.String.equals(__self_ptr: i64, other: i64) -> i1 {
  entry:
    x86.prologue stack_size=80
    x86.mov [rbp-8], rcx
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+0]
    x86.mov [rbp-24], rbx
    x86.mov rax, [rbp-24]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-32], rbx
    x86.mov rax, [rbp-16]
    x86.mov rsi, [rax+0]
    x86.mov [rbp-40], rsi
    x86.mov rax, [rbp-40]
    x86.mov rsi, [rax+8]
    x86.cmp rbx, rsi
    x86.je stdlib.String.equals.lenMismatch_0.after
  lenMismatch_0:
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  lenMismatch_0.after:
    x86.xor rax, rax
    x86.mov rcx, [rbp-32]
    x86.mov [rbp-48], rcx
    x86.mov [rbp-56], rax
    x86.jmp stdlib.String.equals.byteCheck_1.header
  byteCheck_1.header:
    x86.mov rax, [rbp-56]
    x86.mov rcx, [rbp-48]
    x86.cmp rax, rcx
    x86.jge stdlib.String.equals.byteCheck_1.exit
  byteCheck_1:
    x86.mov rax, [rbp-56]
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+0]
    x86.mov [rbp-64], rdx
    x86.mov rbx, [rbp-64]
    x86.mov rsi, [rbx+8]
    x86.mov rdi, [rbp-64]
    x86.mov r8, [rdi+24]
    x86.imul rsi, r8
    x86.lea_symdata r9, [__mm_panic_byte_oob]
    x86.mov rcx, r9
    x86.mov rdx, rsi
    x86.mov r8, rcx
    x86.mov rcx, [rbp-56]
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-56]
    x86.add rcx, rax
    x86.movzx rdx, byte ptr [rcx+0]
    x86.mov rcx, [rbp-16]
    x86.mov rbx, [rcx+0]
    x86.mov [rbp-72], rbx
    x86.mov rcx, [rbp-72]
    x86.mov rbx, [rcx+8]
    x86.mov rcx, [rbp-72]
    x86.mov rsi, [rcx+24]
    x86.imul rbx, rsi
    x86.lea_symdata rcx, [__mm_panic_byte_oob]
    x86.mov rsi, rcx
    x86.mov [rbp-80], rdx
    x86.mov rcx, [rbp-56]
    x86.mov rdx, rbx
    x86.mov r8, rsi
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-72]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-56]
    x86.add rcx, rax
    x86.movzx rax, byte ptr [rcx+0]
    x86.mov rcx, [rbp-80]
    x86.cmp rcx, rax
    x86.je stdlib.String.equals.mismatch_2.after
  mismatch_2:
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  mismatch_2.after:
    x86.jmp stdlib.String.equals.byteCheck_1.incr
  byteCheck_1.incr:
    x86.mov rax, 1
    x86.mov rcx, [rbp-56]
    x86.add rcx, rax
    x86.mov [rbp-56], rcx
    x86.jmp stdlib.String.equals.byteCheck_1.header
  byteCheck_1.exit:
    x86.mov rax, 1
    x86.epilogue
    x86.ret
  }
  func @stdlib-map-on-map-with-function.main() -> u32 {
  entry:
    x86.prologue stack_size=192
    x86.lea_rdata rax, [__str_0]
    x86.mov rcx, rax
    x86.mov rdx, 1
    x86.lea_func rbx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-176], rcx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 2
    x86.call mm_alloc
    x86.mov [rbp-8], rax
    x86.lea_func rdi, [__destruct___ManagedMemory]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-16], rax
    x86.mov r9, [rbp-16]
    x86.mov rax, [rbp-176]
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, 1
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_incref
    x86.mov rax, 1
    x86.lea_rdata rcx, [__str_1]
    x86.mov rdx, rcx
    x86.mov rcx, 1
    x86.lea_func rbx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-184], rdx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 2
    x86.call mm_alloc
    x86.mov [rbp-24], rax
    x86.lea_func rcx, [__destruct___ManagedMemory]
    x86.mov rdx, rcx
    x86.mov rcx, 32
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-32], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-184]
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, 1
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-32]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-32]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-24]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, [rbp-24]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov rax, 2
    x86.lea_rdata rcx, [__str_2]
    x86.mov rdx, rcx
    x86.mov rcx, 1
    x86.lea_func rbx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-192], rdx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 2
    x86.call mm_alloc
    x86.mov [rbp-40], rax
    x86.lea_func rcx, [__destruct___ManagedMemory]
    x86.mov rdx, rcx
    x86.mov rcx, 32
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-48], rax
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-192]
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-48]
    x86.mov rcx, 1
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-48]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-48]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.mov rax, 3
    x86.mov rcx, [rbp-40]
    x86.mov [rbp-56], rcx
    x86.mov rcx, [rbp-56]
    x86.call mm_incref
    x86.mov rax, [rbp-24]
    x86.mov [rbp-64], rax
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-64]
    x86.call mm_incref
    x86.mov rax, [rbp-8]
    x86.mov [rbp-72], rax
    x86.mov rax, [rbp-72]
    x86.mov rcx, [rbp-72]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, 3
    x86.xor rdx, rdx
    x86.mov rbx, 8
    x86.lea_func rsi, [__destruct___ManagedMemory_String]
    x86.mov rdi, rsi
    x86.mov rdx, rdi
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-80], rax
    x86.mov rcx, [rbp-80]
    x86.xor rdx, rdx
    x86.mov [rcx+0], rdx
    x86.mov rcx, [rbp-80]
    x86.mov rdx, 3
    x86.mov [rcx+8], rdx
    x86.mov rcx, [rbp-80]
    x86.xor rdx, rdx
    x86.mov [rcx+16], rdx
    x86.mov rcx, [rbp-80]
    x86.mov rdx, 8
    x86.mov [rcx+24], rdx
    x86.lea rcx, [rbp-72]
    x86.mov rdx, rcx
    x86.mov rcx, [rbp-80]
    x86.mov [rcx+0], rdx
    x86.mov rcx, [rbp-80]
    x86.call mm_incref
    x86.mov rax, 3
    x86.mov [rbp-88], rax
    x86.mov rax, 2
    x86.mov [rbp-96], rax
    x86.mov rax, 1
    x86.mov [rbp-104], rax
    x86.xor rax, rax
    x86.mov rcx, 3
    x86.xor rdx, rdx
    x86.mov rbx, 8
    x86.lea_func rsi, [__destruct___ManagedMemory]
    x86.mov rdi, rsi
    x86.mov rdx, rdi
    x86.mov rcx, 32
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-112], rax
    x86.mov rcx, [rbp-112]
    x86.xor rdx, rdx
    x86.mov [rcx+0], rdx
    x86.mov rcx, [rbp-112]
    x86.mov rdx, 3
    x86.mov [rcx+8], rdx
    x86.mov rcx, [rbp-112]
    x86.xor rdx, rdx
    x86.mov [rcx+16], rdx
    x86.mov rcx, [rbp-112]
    x86.mov rdx, 8
    x86.mov [rcx+24], rdx
    x86.lea rcx, [rbp-104]
    x86.mov rdx, rcx
    x86.mov rcx, [rbp-112]
    x86.mov [rcx+0], rdx
    x86.mov rcx, [rbp-112]
    x86.call mm_incref
    x86.mov rax, [rbp-80]
    x86.mov rcx, [rbp-112]
    x86.mov rcx, [rbp-80]
    x86.mov rdx, [rbp-112]
    x86.call __Map_String_i64.init
    x86.mov [rbp-120], rax
    x86.lea_func rax, [__closure_0]
    x86.mov rcx, [rbp-120]
    x86.mov rdx, rax
    x86.xor r8, r8
    x86.call __Map_String_i64.map
    x86.mov [rbp-128], rax
    x86.xor rcx, rcx
    x86.mov [rbp-136], rcx
    x86.mov [rbp-144], rax
    x86.mov rax, [rbp-144]
    x86.mov rcx, [rbp-144]
    x86.call mm_incref
    x86.mov rax, [rbp-144]
    x86.mov rcx, [rbp-144]
    x86.call __Array_Entry.createIterator
    x86.jmp stdlib-map-on-map-with-function.main.loop_2.header
  loop_2.header:
    x86.mov rax, [rbp-144]
    x86.mov rcx, [rbp-144]
    x86.call __Array_Entry.next
    x86.mov [rbp-152], rax
    x86.xor rcx, rcx
    x86.cmp rdx, rcx
    x86.jne stdlib-map-on-map-with-function.main.loop_2.exit
  loop_2:
    x86.mov rax, [rbp-152]
    x86.mov [rbp-160], rax
    x86.mov rcx, [rbp-160]
    x86.call mm_incref
    x86.mov rdx, [rbp-160]
    x86.mov rbx, [rdx+8]
    x86.mov rsi, [rbp-136]
    x86.add rsi, rbx
    x86.mov [rbp-136], rsi
    x86.mov rdi, [rbp-160]
    x86.test rdi, rdi
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-160]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov r8, [rbp-152]
    x86.test r8, r8
    x86.jz __nonnull_skip_1
    x86.mov rcx, [rbp-152]
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.mov r9, [rbp-80]
    x86.test r9, r9
    x86.jz __nonnull_skip_2
    x86.mov rcx, [rbp-80]
    x86.call mm_decref
    x86.label __nonnull_skip_2
    x86.xor rax, rax
    x86.mov [rbp-80], rax
    x86.mov rax, [rbp-112]
    x86.test rax, rax
    x86.jz __nonnull_skip_3
    x86.mov rcx, [rbp-112]
    x86.call mm_decref
    x86.label __nonnull_skip_3
    x86.xor rax, rax
    x86.mov [rbp-112], rax
    x86.jmp stdlib-map-on-map-with-function.main.loop_2.header
  loop_2.exit:
    x86.mov rax, [rbp-136]
    x86.mov [rbp-168], rax
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rdx
    x86.movzx rdx, rdxb
    x86.mov rbx, 4294967295
    x86.cmp rax, rbx
    x86.setg rsi
    x86.movzx rsi, rsib
    x86.or rdx, rsi
    x86.test rdx, rdx
    x86.je stdlib-map-on-map-with-function.main.__range_ok_5
  __range_panic_5:
    x86.lea_symdata rax, [__panic_msg_40]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_5:
    x86.mov rax, [rbp-168]
    x86.mov rcx, [rbp-152]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_4
    x86.call mm_decref
    x86.label __nonnull_skip_4
    x86.mov rax, [rbp-144]
    x86.test rax, rax
    x86.jz __nonnull_skip_5
    x86.mov rcx, [rbp-144]
    x86.call mm_decref
    x86.label __nonnull_skip_5
    x86.mov rcx, [rbp-72]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_6
    x86.call mm_decref
    x86.label __nonnull_skip_6
    x86.xor rdx, rdx
    x86.mov [rbp-72], rdx
    x86.mov rbx, [rbp-64]
    x86.test rbx, rbx
    x86.jz __nonnull_skip_7
    x86.mov rcx, [rbp-64]
    x86.call mm_decref
    x86.label __nonnull_skip_7
    x86.xor rsi, rsi
    x86.mov [rbp-64], rsi
    x86.mov rdi, [rbp-56]
    x86.test rdi, rdi
    x86.jz __nonnull_skip_8
    x86.mov rcx, [rbp-56]
    x86.call mm_decref
    x86.label __nonnull_skip_8
    x86.xor r8, r8
    x86.mov [rbp-56], r8
    x86.mov r9, [rbp-40]
    x86.test r9, r9
    x86.jz __nonnull_skip_9
    x86.mov rcx, [rbp-40]
    x86.call mm_decref
    x86.label __nonnull_skip_9
    x86.mov rax, [rbp-24]
    x86.test rax, rax
    x86.jz __nonnull_skip_10
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_10
    x86.mov rax, [rbp-8]
    x86.test rax, rax
    x86.jz __nonnull_skip_11
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_11
    x86.mov rax, [rbp-128]
    x86.test rax, rax
    x86.jz __nonnull_skip_12
    x86.mov rcx, [rbp-128]
    x86.call mm_decref
    x86.label __nonnull_skip_12
    x86.mov rax, [rbp-120]
    x86.test rax, rax
    x86.jz __nonnull_skip_13
    x86.mov rcx, [rbp-120]
    x86.call mm_decref
    x86.label __nonnull_skip_13
    x86.mov rax, [rbp-80]
    x86.test rax, rax
    x86.jz __nonnull_skip_14
    x86.mov rcx, [rbp-80]
    x86.call mm_decref
    x86.label __nonnull_skip_14
    x86.mov rax, [rbp-112]
    x86.test rax, rax
    x86.jz __nonnull_skip_15
    x86.mov rcx, [rbp-112]
    x86.call mm_decref
    x86.label __nonnull_skip_15
    x86.mov rax, [rbp-168]
    x86.epilogue
    x86.ret
  }
  func @__closure_0(p: i64) -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.epilogue
    x86.ret
  }
  func @StringArray.get(__self_ptr: i64, index: i64) -> i64 {
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
    x86.jb StringArray.get.upper_0.after
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
    x86.mov [rbp-48], rax
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
    x86.mov rax, [rbp-48]
    x86.mov [rbp-40], rax
    x86.mov rax, [rbp-40]
    x86.xor rdx, rdx
    x86.epilogue
    x86.ret
  }
  func @StringArray.set(__self_ptr: i64, index: i64, value: i64) {
  entry:
    x86.prologue stack_size=80
    x86.mov [rbp-8], rcx
    x86.mov [rbp-24], rdx
    x86.mov [rbp-32], r8
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-16], rbx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-40], rbx
    x86.mov rax, [rbp-40]
    x86.mov rbx, [rax+8]
    x86.cmp rdx, rbx
    x86.jae StringArray.set.inbounds_0.merge
  inbounds_0:
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rcx+24]
    x86.mov rbx, [rbp-16]
    x86.mov rsi, [rbx+0]
    x86.mov rdi, [rbp-16]
    x86.mov r8, [rdi+16]
    x86.mov r9, [rbp-16]
    x86.mov rcx, [r9+8]
    x86.mov [rbp-48], rcx
    x86.mov rbx, [rbp-16]
    x86.mov [rbp-56], rdx
    x86.mov [rbp-64], r8
    x86.mov rcx, rsi
    x86.mov rdx, [rbp-64]
    x86.mov r8, [rbp-48]
    x86.mov r9, [rbp-56]
    x86.mov rsi, [rbp-16]
    x86.call maxon_cow_check
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-64]
    x86.cmp rcx, rax
    x86.sete rax
    x86.movzx rax, raxb
    x86.mov rdx, [rbp-48]
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
    x86.mov rcx, [rbp-24]
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-56]
    x86.mov rdx, [rbp-24]
    x86.imul rdx, rax
    x86.add rcx, rdx
    x86.mov rax, [rcx+0]
    x86.mov [rbp-72], rax
    x86.mov [rbp-80], rcx
    x86.test rax, rax
    x86.jz __nonnull_skip_16
    x86.mov rcx, [rbp-72]
    x86.call mm_decref
    x86.label __nonnull_skip_16
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-80]
    x86.mov [rcx+0], rax
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.jmp StringArray.set.inbounds_0.merge
  inbounds_0.merge:
    x86.epilogue
    x86.ret
  }
  func @StringArray.reserve(__self_ptr: i64, minCapacity: i64) {
  entry:
    x86.prologue stack_size=64
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
    x86.cmp rdx, rbx
    x86.jbe StringArray.reserve.grow_0.merge
  grow_0:
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rcx+24]
    x86.mov rbx, [rbp-16]
    x86.mov rsi, [rbx+16]
    x86.mov rdi, 1
    x86.lea r8, [rax + rdi]
    x86.lea_symdata r9, [__mm_panic_grow_shrink]
    x86.mov rcx, r9
    x86.mov [rbp-48], rdx
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
    x86.mov [rbp-40], rbx
    x86.mov rax, [rbp-16]
    x86.mov [rbp-56], rdx
    x86.mov r8, [rbp-40]
    x86.mov r9, [rbp-48]
    x86.mov rsi, [rbp-16]
    x86.call maxon_cow_check
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-56]
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
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-48]
    x86.mov rdx, [rbp-24]
    x86.mov rbx, rdx
    x86.imul rbx, rax
    x86.mov rdx, rbx
    x86.call mm_raw_realloc
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-24]
    x86.mov [rax+16], rcx
    x86.jmp StringArray.reserve.grow_0.merge
  grow_0.merge:
    x86.epilogue
    x86.ret
  }
  func @StringArray.resize(__self_ptr: i64, newLength: i64) {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-16], rbx
    x86.mov rax, [rbp-8]
    x86.mov [rbp-24], rdx
    x86.mov rcx, [rbp-8]
    x86.call StringArray.reserve
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rcx+16]
    x86.mov rbx, 1
    x86.add rdx, rbx
    x86.lea_symdata rsi, [__mm_panic_setlength_oob]
    x86.mov rdi, rsi
    x86.mov rcx, [rbp-24]
    x86.mov r8, rdi
    x86.call maxon_bounds_check
    x86.mov r8, [rbp-16]
    x86.mov r9, [rbp-24]
    x86.mov [r8+8], r9
    x86.epilogue
    x86.ret
  }
  func @StateArray.get(__self_ptr: i64, index: i64) -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-8], rcx
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-24], rbx
    x86.mov rax, [rbp-24]
    x86.mov rbx, [rax+8]
    x86.cmp rdx, rbx
    x86.jb StateArray.get.upper_0.after
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
    x86.xor rdx, rdx
    x86.epilogue
    x86.ret
  }
  func @StateArray.set(__self_ptr: i64, index: i64, value: i64) {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-8], rcx
    x86.mov [rbp-24], rdx
    x86.mov [rbp-32], r8
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-16], rbx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-40], rbx
    x86.mov rax, [rbp-40]
    x86.mov rbx, [rax+8]
    x86.cmp rdx, rbx
    x86.jae StateArray.set.inbounds_0.merge
  inbounds_0:
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-32]
    x86.mov rdx, [rbp-16]
    x86.mov rbx, [rdx+24]
    x86.mov rsi, [rbp-16]
    x86.mov rdi, [rsi+0]
    x86.mov r8, [rbp-16]
    x86.mov r9, [r8+16]
    x86.mov rdx, [rbp-16]
    x86.mov rsi, [rdx+8]
    x86.mov [rbp-48], rsi
    x86.mov rdx, [rbp-16]
    x86.mov [rbp-56], rbx
    x86.mov [rbp-64], r9
    x86.mov rcx, rdi
    x86.mov rdx, [rbp-64]
    x86.mov r8, [rbp-48]
    x86.mov r9, [rbp-56]
    x86.mov rsi, [rbp-16]
    x86.call maxon_cow_check
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-64]
    x86.cmp rcx, rax
    x86.sete rax
    x86.movzx rax, raxb
    x86.mov rdx, [rbp-48]
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
    x86.mov rcx, [rbp-24]
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-56]
    x86.mov rdx, [rbp-24]
    x86.imul rdx, rax
    x86.add rcx, rdx
    x86.mov rax, [rbp-32]
    x86.mov [rcx+0], rax
    x86.jmp StateArray.set.inbounds_0.merge
  inbounds_0.merge:
    x86.epilogue
    x86.ret
  }
  func @StateArray.reserve(__self_ptr: i64, minCapacity: i64) {
  entry:
    x86.prologue stack_size=64
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
    x86.cmp rdx, rbx
    x86.jbe StateArray.reserve.grow_0.merge
  grow_0:
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rcx+24]
    x86.mov rbx, [rbp-16]
    x86.mov rsi, [rbx+16]
    x86.mov rdi, 1
    x86.lea r8, [rax + rdi]
    x86.lea_symdata r9, [__mm_panic_grow_shrink]
    x86.mov rcx, r9
    x86.mov [rbp-48], rdx
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
    x86.mov [rbp-40], rbx
    x86.mov rax, [rbp-16]
    x86.mov [rbp-56], rdx
    x86.mov r8, [rbp-40]
    x86.mov r9, [rbp-48]
    x86.mov rsi, [rbp-16]
    x86.call maxon_cow_check
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-56]
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
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-48]
    x86.mov rdx, [rbp-24]
    x86.mov rbx, rdx
    x86.imul rbx, rax
    x86.mov rdx, rbx
    x86.call mm_raw_realloc
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-24]
    x86.mov [rax+16], rcx
    x86.jmp StateArray.reserve.grow_0.merge
  grow_0.merge:
    x86.epilogue
    x86.ret
  }
  func @StateArray.resize(__self_ptr: i64, newLength: i64) {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-16], rbx
    x86.mov rax, [rbp-8]
    x86.mov [rbp-24], rdx
    x86.mov rcx, [rbp-8]
    x86.call StateArray.reserve
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rcx+16]
    x86.mov rbx, 1
    x86.add rdx, rbx
    x86.lea_symdata rsi, [__mm_panic_setlength_oob]
    x86.mov rdi, rsi
    x86.mov rcx, [rbp-24]
    x86.mov r8, rdi
    x86.call maxon_bounds_check
    x86.mov r8, [rbp-16]
    x86.mov r9, [rbp-24]
    x86.mov [r8+8], r9
    x86.epilogue
    x86.ret
  }
  func @Array_i64.get(__self_ptr: i64, index: i64) -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-8], rcx
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-24], rbx
    x86.mov rax, [rbp-24]
    x86.mov rbx, [rax+8]
    x86.cmp rdx, rbx
    x86.jb Array_i64.get.upper_0.after
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
    x86.xor rdx, rdx
    x86.epilogue
    x86.ret
  }
  func @Array_i64.set(__self_ptr: i64, index: i64, value: i64) {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-8], rcx
    x86.mov [rbp-24], rdx
    x86.mov [rbp-32], r8
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-16], rbx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-40], rbx
    x86.mov rax, [rbp-40]
    x86.mov rbx, [rax+8]
    x86.cmp rdx, rbx
    x86.jae Array_i64.set.inbounds_0.merge
  inbounds_0:
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-32]
    x86.mov rdx, [rbp-16]
    x86.mov rbx, [rdx+24]
    x86.mov rsi, [rbp-16]
    x86.mov rdi, [rsi+0]
    x86.mov r8, [rbp-16]
    x86.mov r9, [r8+16]
    x86.mov rdx, [rbp-16]
    x86.mov rsi, [rdx+8]
    x86.mov [rbp-48], rsi
    x86.mov rdx, [rbp-16]
    x86.mov [rbp-56], rbx
    x86.mov [rbp-64], r9
    x86.mov rcx, rdi
    x86.mov rdx, [rbp-64]
    x86.mov r8, [rbp-48]
    x86.mov r9, [rbp-56]
    x86.mov rsi, [rbp-16]
    x86.call maxon_cow_check
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-64]
    x86.cmp rcx, rax
    x86.sete rax
    x86.movzx rax, raxb
    x86.mov rdx, [rbp-48]
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
    x86.mov rcx, [rbp-24]
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-56]
    x86.mov rdx, [rbp-24]
    x86.imul rdx, rax
    x86.add rcx, rdx
    x86.mov rax, [rbp-32]
    x86.mov [rcx+0], rax
    x86.jmp Array_i64.set.inbounds_0.merge
  inbounds_0.merge:
    x86.epilogue
    x86.ret
  }
  func @Array_i64.reserve(__self_ptr: i64, minCapacity: i64) {
  entry:
    x86.prologue stack_size=64
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
    x86.cmp rdx, rbx
    x86.jbe Array_i64.reserve.grow_0.merge
  grow_0:
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rcx+24]
    x86.mov rbx, [rbp-16]
    x86.mov rsi, [rbx+16]
    x86.mov rdi, 1
    x86.lea r8, [rax + rdi]
    x86.lea_symdata r9, [__mm_panic_grow_shrink]
    x86.mov rcx, r9
    x86.mov [rbp-48], rdx
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
    x86.mov [rbp-40], rbx
    x86.mov rax, [rbp-16]
    x86.mov [rbp-56], rdx
    x86.mov r8, [rbp-40]
    x86.mov r9, [rbp-48]
    x86.mov rsi, [rbp-16]
    x86.call maxon_cow_check
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-56]
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
    x86.mov rcx, [rax+0]
    x86.mov rax, [rbp-48]
    x86.mov rdx, [rbp-24]
    x86.mov rbx, rdx
    x86.imul rbx, rax
    x86.mov rdx, rbx
    x86.call mm_raw_realloc
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-24]
    x86.mov [rax+16], rcx
    x86.jmp Array_i64.reserve.grow_0.merge
  grow_0.merge:
    x86.epilogue
    x86.ret
  }
  func @Array_i64.resize(__self_ptr: i64, newLength: i64) {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-16], rbx
    x86.mov rax, [rbp-8]
    x86.mov [rbp-24], rdx
    x86.mov rcx, [rbp-8]
    x86.call Array_i64.reserve
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rcx+16]
    x86.mov rbx, 1
    x86.add rdx, rbx
    x86.lea_symdata rsi, [__mm_panic_setlength_oob]
    x86.mov rdi, rsi
    x86.mov rcx, [rbp-24]
    x86.mov r8, rdi
    x86.call maxon_bounds_check
    x86.mov r8, [rbp-16]
    x86.mov r9, [rbp-24]
    x86.mov [r8+8], r9
    x86.epilogue
    x86.ret
  }
  func @__Map_String_i64.map(__self_ptr: i64, transform: i64, __env_transform: i64) -> i64 {
  entry:
    x86.prologue stack_size=80
    x86.mov [rbp-24], r8
    x86.xor rax, rax
    x86.mov [rbp-8], rax
    x86.mov [rbp-16], rdx
    x86.xor rax, rax
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.xor rsi, rsi
    x86.mov rdi, 8
    x86.lea_func r9, [__destruct___ManagedMemory_Entry]
    x86.mov rax, r9
    x86.mov [rbp-72], rcx
    x86.mov rdx, rax
    x86.mov rcx, 32
    x86.mov r8, 5
    x86.call mm_alloc
    x86.mov [rbp-32], rax
    x86.mov rcx, [rbp-32]
    x86.xor rdx, rdx
    x86.mov [rcx+0], rdx
    x86.mov rbx, [rbp-32]
    x86.xor rsi, rsi
    x86.mov [rbx+8], rsi
    x86.mov rdi, [rbp-32]
    x86.xor r8, r8
    x86.mov [rdi+16], r8
    x86.mov r9, [rbp-32]
    x86.mov rax, 8
    x86.mov [r9+24], rax
    x86.lea_func rax, [__destruct___Array_____Tuple_Key_Value_String_i64]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 6
    x86.call mm_alloc
    x86.mov [rbp-40], rax
    x86.mov rax, [rbp-40]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+8], rax
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.mov rax, [rbp-72]
    x86.mov [rbp-48], rax
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call __Map_String_i64.createIterator
    x86.jmp __Map_String_i64.map.loop_0.header
  loop_0.header:
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call __Map_String_i64.next
    x86.mov [rbp-56], rax
    x86.xor rcx, rcx
    x86.cmp rdx, rcx
    x86.jne __Map_String_i64.map.loop_0.exit
  loop_0:
    x86.mov rax, [rbp-56]
    x86.mov [rbp-64], rax
    x86.mov rcx, [rbp-64]
    x86.call mm_incref
    x86.mov rdx, [rbp-16]
    x86.mov rbx, [rbp-64]
    x86.mov rsi, [rbp-24]
    x86.mov r10, rdx
    x86.mov rcx, [rbp-64]
    x86.mov rdx, [rbp-24]
    x86.call r10
    x86.mov [rbp-8], rax
    x86.mov rdi, [rbp-40]
    x86.mov r8, [rbp-8]
    x86.mov rcx, [rbp-40]
    x86.mov rdx, [rbp-8]
    x86.call __Array_____Tuple_Key_Value_String_i64.push
    x86.mov r9, [rbp-64]
    x86.test r9, r9
    x86.jz __nonnull_skip_17
    x86.mov rcx, [rbp-64]
    x86.call mm_decref
    x86.label __nonnull_skip_17
    x86.mov rax, [rbp-56]
    x86.test rax, rax
    x86.jz __nonnull_skip_18
    x86.mov rcx, [rbp-56]
    x86.call mm_decref
    x86.label __nonnull_skip_18
    x86.mov rax, [rbp-8]
    x86.test rax, rax
    x86.jz __nonnull_skip_19
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_19
    x86.xor rax, rax
    x86.mov [rbp-8], rax
    x86.jmp __Map_String_i64.map.loop_0.header
  loop_0.exit:
    x86.mov rax, [rbp-56]
    x86.test rax, rax
    x86.jz __nonnull_skip_20
    x86.mov rcx, [rbp-56]
    x86.call mm_decref
    x86.label __nonnull_skip_20
    x86.mov rcx, [rbp-48]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_21
    x86.call mm_decref
    x86.label __nonnull_skip_21
    x86.mov rdx, [rbp-8]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_22
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_22
    x86.mov rax, [rbp-40]
    x86.epilogue
    x86.ret
  }
  func @__Map_String_i64.init(initKeys: i64, initValues: i64) -> i64 {
  entry:
    x86.prologue stack_size=128
    x86.mov [rbp-8], rcx
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-24], rbx
    x86.mov rax, 16
    x86.mov [rbp-32], rax
    x86.jmp __Map_String_i64.init.calc_cap_0.header
  calc_cap_0.header:
    x86.mov rax, 2
    x86.mov rcx, [rbp-24]
    x86.imul rcx, rax
    x86.mov rdx, [rbp-32]
    x86.cmp rdx, rcx
    x86.jge __Map_String_i64.init.calc_cap_0.exit
  calc_cap_0:
    x86.mov rax, 2
    x86.mov rcx, [rbp-32]
    x86.imul rcx, rax
    x86.mov [rbp-32], rcx
    x86.jmp __Map_String_i64.init.calc_cap_0.header
  calc_cap_0.exit:
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.mov rsi, 8
    x86.lea_func rdi, [__destruct___ManagedMemory_String]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-40], rax
    x86.mov r9, [rbp-40]
    x86.xor rax, rax
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-40]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-40]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-40]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.lea_func rax, [__destruct_StringArray]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 7
    x86.call mm_alloc
    x86.mov [rbp-48], rax
    x86.mov rax, [rbp-48]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-48]
    x86.mov [rcx+8], rax
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-48]
    x86.mov rdx, [rbp-32]
    x86.call StringArray.resize
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.mov rsi, 8
    x86.lea_func rdi, [__destruct___ManagedMemory_i64]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 8
    x86.call mm_alloc
    x86.mov [rbp-56], rax
    x86.mov rax, [rbp-56]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-56]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-56]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-56]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.lea_func rax, [__destruct_Array_i64]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 9
    x86.call mm_alloc
    x86.mov [rbp-64], rax
    x86.mov rax, [rbp-64]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-56]
    x86.mov rcx, [rbp-64]
    x86.mov [rcx+8], rax
    x86.mov rcx, [rbp-56]
    x86.call mm_incref
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-64]
    x86.call mm_incref
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-64]
    x86.mov rdx, [rbp-32]
    x86.call Array_i64.resize
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.mov rsi, 8
    x86.lea_func rdi, [__destruct___ManagedMemory_SlotState]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 10
    x86.call mm_alloc
    x86.mov [rbp-72], rax
    x86.mov rax, [rbp-72]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-72]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-72]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-72]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.lea_func rax, [__destruct_StateArray]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 11
    x86.call mm_alloc
    x86.mov [rbp-80], rax
    x86.mov rax, [rbp-80]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-72]
    x86.mov rcx, [rbp-80]
    x86.mov [rcx+8], rax
    x86.mov rcx, [rbp-72]
    x86.call mm_incref
    x86.mov rax, [rbp-80]
    x86.mov rcx, [rbp-80]
    x86.call mm_incref
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-80]
    x86.mov rdx, [rbp-32]
    x86.call StateArray.resize
    x86.xor rax, rax
    x86.mov rcx, [rbp-32]
    x86.xor rdx, rdx
    x86.lea_func rbx, [__destruct___Map_String_i64]
    x86.mov rsi, rbx
    x86.mov rdx, rsi
    x86.mov rcx, 48
    x86.mov r8, 12
    x86.call mm_alloc
    x86.mov [rbp-88], rax
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-88]
    x86.mov [rcx+0], rax
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-88]
    x86.mov [rcx+8], rax
    x86.mov rcx, [rbp-64]
    x86.call mm_incref
    x86.mov rax, [rbp-80]
    x86.mov rcx, [rbp-88]
    x86.mov [rcx+16], rax
    x86.mov rcx, [rbp-80]
    x86.call mm_incref
    x86.mov rax, [rbp-88]
    x86.xor rcx, rcx
    x86.mov [rax+24], rcx
    x86.mov rax, [rbp-88]
    x86.mov rcx, [rbp-32]
    x86.mov [rax+32], rcx
    x86.mov rax, [rbp-88]
    x86.xor rcx, rcx
    x86.mov [rax+40], rcx
    x86.mov rax, [rbp-88]
    x86.mov rcx, [rbp-88]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, [rbp-24]
    x86.mov [rbp-96], rcx
    x86.mov [rbp-104], rax
    x86.jmp __Map_String_i64.init.insert_loop_1.header
  insert_loop_1.header:
    x86.mov rax, [rbp-104]
    x86.mov rcx, [rbp-96]
    x86.cmp rax, rcx
    x86.jge __Map_String_i64.init.insert_loop_1.exit
  insert_loop_1:
    x86.mov rax, [rbp-104]
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+8]
    x86.lea_symdata rbx, [__mm_panic_index_oob]
    x86.mov rsi, rbx
    x86.mov rcx, [rbp-104]
    x86.mov r8, rsi
    x86.call maxon_bounds_check
    x86.mov rdi, [rbp-8]
    x86.mov r8, [rdi+24]
    x86.mov r9, [rbp-8]
    x86.mov rax, [r9+0]
    x86.mov rcx, [rbp-104]
    x86.mov rdx, rcx
    x86.imul rdx, r8
    x86.add rax, rdx
    x86.mov rdx, [rax+0]
    x86.mov [rbp-120], rdx
    x86.mov rcx, [rbp-120]
    x86.call mm_incref
    x86.mov rax, [rbp-120]
    x86.mov [rbp-112], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+8]
    x86.lea_symdata rax, [__mm_panic_index_oob]
    x86.mov rdx, rax
    x86.mov r8, rdx
    x86.mov rdx, rcx
    x86.mov rcx, [rbp-104]
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+24]
    x86.mov rax, [rbp-16]
    x86.mov rdx, [rax+0]
    x86.mov rax, [rbp-104]
    x86.imul rax, rcx
    x86.add rdx, rax
    x86.mov rax, [rdx+0]
    x86.mov rcx, [rbp-88]
    x86.mov rdx, [rbp-112]
    x86.mov r8, rax
    x86.call __Map_String_i64.insert
    x86.mov rax, [rbp-112]
    x86.test rax, rax
    x86.jz __nonnull_skip_23
    x86.mov rcx, [rbp-112]
    x86.call mm_decref
    x86.label __nonnull_skip_23
    x86.jmp __Map_String_i64.init.insert_loop_1.incr
  insert_loop_1.incr:
    x86.mov rax, 1
    x86.mov rcx, [rbp-104]
    x86.add rcx, rax
    x86.mov [rbp-104], rcx
    x86.jmp __Map_String_i64.init.insert_loop_1.header
  insert_loop_1.exit:
    x86.mov rax, [rbp-80]
    x86.test rax, rax
    x86.jz __nonnull_skip_24
    x86.mov rcx, [rbp-80]
    x86.call mm_decref
    x86.label __nonnull_skip_24
    x86.mov rcx, [rbp-64]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_25
    x86.call mm_decref
    x86.label __nonnull_skip_25
    x86.mov rdx, [rbp-48]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_26
    x86.mov rcx, [rbp-48]
    x86.call mm_decref
    x86.label __nonnull_skip_26
    x86.mov rax, [rbp-88]
    x86.epilogue
    x86.ret
  }
  func @__Map_String_i64.insert(__self_ptr: i64, key: i64, value: i64) {
  entry:
    x86.prologue stack_size=240
    x86.mov [rbp-8], rcx
    x86.mov [rbp-40], rdx
    x86.mov [rbp-48], r8
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+0]
    x86.mov [rbp-16], rbx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-24], rbx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+16]
    x86.mov [rbp-32], rbx
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+32]
    x86.xor rax, rax
    x86.cmp rbx, rax
    x86.jne __Map_String_i64.insert.init_empty_0.merge
  init_empty_0:
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.mov rsi, 8
    x86.lea_func rdi, [__destruct___ManagedMemory_String]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-56], rax
    x86.mov r9, [rbp-56]
    x86.xor rax, rax
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-56]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-56]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-56]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.lea_func rax, [__destruct_StringArray]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 7
    x86.call mm_alloc
    x86.mov [rbp-64], rax
    x86.mov rcx, [rbp-64]
    x86.xor rdx, rdx
    x86.mov [rcx+0], rdx
    x86.mov rcx, [rbp-56]
    x86.mov rdx, [rbp-64]
    x86.mov [rdx+8], rcx
    x86.call mm_incref
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-64]
    x86.call mm_incref
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-64]
    x86.mov rdx, 16
    x86.call StringArray.resize
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.mov rsi, 8
    x86.lea_func rdi, [__destruct___ManagedMemory_i64]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 8
    x86.call mm_alloc
    x86.mov [rbp-72], rax
    x86.mov rax, [rbp-72]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-72]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-72]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-72]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.lea_func rax, [__destruct_Array_i64]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 9
    x86.call mm_alloc
    x86.mov [rbp-80], rax
    x86.mov rcx, [rbp-80]
    x86.xor rdx, rdx
    x86.mov [rcx+0], rdx
    x86.mov rcx, [rbp-72]
    x86.mov rdx, [rbp-80]
    x86.mov [rdx+8], rcx
    x86.call mm_incref
    x86.mov rax, [rbp-80]
    x86.mov rcx, [rbp-80]
    x86.call mm_incref
    x86.mov rax, [rbp-80]
    x86.mov rcx, [rbp-80]
    x86.mov rdx, 16
    x86.call Array_i64.resize
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.mov rsi, 8
    x86.lea_func rdi, [__destruct___ManagedMemory_SlotState]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 10
    x86.call mm_alloc
    x86.mov [rbp-88], rax
    x86.mov rax, [rbp-88]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-88]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-88]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-88]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.lea_func rax, [__destruct_StateArray]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 11
    x86.call mm_alloc
    x86.mov [rbp-96], rax
    x86.mov rcx, [rbp-96]
    x86.xor rdx, rdx
    x86.mov [rcx+0], rdx
    x86.mov rcx, [rbp-88]
    x86.mov rdx, [rbp-96]
    x86.mov [rdx+8], rcx
    x86.call mm_incref
    x86.mov rax, [rbp-96]
    x86.mov rcx, [rbp-96]
    x86.call mm_incref
    x86.mov rax, [rbp-96]
    x86.mov rcx, [rbp-96]
    x86.mov rdx, 16
    x86.call StateArray.resize
    x86.mov rax, [rbp-16]
    x86.test rax, rax
    x86.jz __nonnull_skip_27
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_27
    x86.mov rax, [rbp-64]
    x86.mov [rbp-16], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-24]
    x86.test rax, rax
    x86.jz __nonnull_skip_28
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_28
    x86.mov rax, [rbp-80]
    x86.mov [rbp-24], rax
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-32]
    x86.test rax, rax
    x86.jz __nonnull_skip_29
    x86.mov rcx, [rbp-32]
    x86.call mm_decref
    x86.label __nonnull_skip_29
    x86.mov rax, [rbp-96]
    x86.mov [rbp-32], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+16], rax
    x86.mov rax, 16
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+32], rax
    x86.mov rax, [rbp-96]
    x86.test rax, rax
    x86.jz __nonnull_skip_30
    x86.mov rcx, [rbp-96]
    x86.call mm_decref
    x86.label __nonnull_skip_30
    x86.mov rax, [rbp-80]
    x86.test rax, rax
    x86.jz __nonnull_skip_31
    x86.mov rcx, [rbp-80]
    x86.call mm_decref
    x86.label __nonnull_skip_31
    x86.mov rax, [rbp-64]
    x86.test rax, rax
    x86.jz __nonnull_skip_32
    x86.mov rcx, [rbp-64]
    x86.call mm_decref
    x86.label __nonnull_skip_32
    x86.jmp __Map_String_i64.insert.init_empty_0.merge
  init_empty_0.merge:
    x86.mov rax, 3
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+32]
    x86.imul rdx, rax
    x86.mov rbx, 4
    x86.mov [rbp-240], rdx
    x86.mov rax, rdx
    x86.cqo
    x86.idiv rbx
    x86.mov rsi, [rbp-8]
    x86.mov rdi, [rsi+24]
    x86.cmp rdi, rax
    x86.jl __Map_String_i64.insert.do_grow_1.merge
  do_grow_1:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call __Map_String_i64.grow
    x86.jmp __Map_String_i64.insert.do_grow_1.merge
  do_grow_1.merge:
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call stdlib.String.hash
    x86.mov ecx, rax
    x86.mov rdx, [rbp-8]
    x86.mov rbx, [rdx+32]
    x86.mov rax, rcx
    x86.cqo
    x86.idiv rbx
    x86.mov [rbp-104], rdx
    x86.xor rsi, rsi
    x86.cmp rdx, rsi
    x86.jge __Map_String_i64.insert.fix_negative_2.merge
  fix_negative_2:
    x86.mov rax, [rbp-104]
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+32]
    x86.add rax, rdx
    x86.mov [rbp-104], rax
    x86.jmp __Map_String_i64.insert.fix_negative_2.merge
  fix_negative_2.merge:
    x86.mov rax, -1
    x86.mov [rbp-112], rax
    x86.xor rcx, rcx
    x86.mov [rbp-120], rcx
    x86.jmp __Map_String_i64.insert.probe_3.header
  probe_3.header:
    x86.mov rax, [rbp-120]
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+32]
    x86.cmp rax, rdx
    x86.jge __Map_String_i64.insert.probe_3.exit
  probe_3:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.mov [rbp-128], rcx
    x86.mov rdx, [rbp-104]
    x86.mov rbx, [rbp-128]
    x86.mov rcx, [rbp-128]
    x86.call StateArray.get
    x86.xor rsi, rsi
    x86.mov [rbp-136], rsi
    x86.mov [rbp-144], rax
    x86.xor rdi, rdi
    x86.cmp rdx, rdi
    x86.je __Map_String_i64.insert.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov rax, [rbp-136]
    x86.mov [rbp-144], rax
    x86.jmp __Map_String_i64.insert.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov rax, [rbp-144]
    x86.mov [rbp-152], rax
    x86.mov rcx, [rbp-152]
    x86.xor rdx, rdx
    x86.mov [rbp-160], rdx
    x86.mov [rbp-168], rcx
    x86.jmp __Map_String_i64.insert.se1_8.cmp0
  se1_8.cmp0:
    x86.mov rax, [rbp-168]
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.insert.se1_8.cmp1
  se1_8.case0:
    x86.mov rax, 1
    x86.mov [rbp-160], rax
    x86.jmp __Map_String_i64.insert.se1_8.merge
  se1_8.cmp1:
    x86.mov rax, [rbp-168]
    x86.mov rcx, 1
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.insert.se1_8.cmp2
  se1_8.case1:
    x86.xor rax, rax
    x86.mov [rbp-160], rax
    x86.jmp __Map_String_i64.insert.se1_8.merge
  se1_8.cmp2:
    x86.mov rax, [rbp-168]
    x86.mov rcx, 2
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.insert.se1_8.merge
  se1_8.case2:
    x86.xor rax, rax
    x86.mov [rbp-160], rax
    x86.jmp __Map_String_i64.insert.se1_8.merge
  se1_8.merge:
    x86.mov rax, [rbp-160]
    x86.test rax, rax
    x86.je __Map_String_i64.insert.empty_9.after
  empty_9:
    x86.mov rax, [rbp-104]
    x86.mov [rbp-176], rax
    x86.xor rcx, rcx
    x86.mov rdx, [rbp-112]
    x86.cmp rdx, rcx
    x86.jl __Map_String_i64.insert.use_deleted_10.merge
  use_deleted_10:
    x86.mov rax, [rbp-112]
    x86.mov [rbp-176], rax
    x86.jmp __Map_String_i64.insert.use_deleted_10.merge
  use_deleted_10.merge:
    x86.mov rax, [rbp-176]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rbp-40]
    x86.mov rdx, [rbp-176]
    x86.mov r8, [rbp-40]
    x86.call StringArray.set
    x86.mov rbx, [rbp-176]
    x86.mov rsi, [rbp-48]
    x86.mov rdi, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.mov rdx, [rbp-176]
    x86.mov r8, [rbp-48]
    x86.call Array_i64.set
    x86.mov r8, [rbp-176]
    x86.mov r9, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.mov rdx, [rbp-176]
    x86.mov r8, 1
    x86.call StateArray.set
    x86.mov rax, 1
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+24]
    x86.add rdx, rax
    x86.mov rax, [rbp-8]
    x86.mov [rax+24], rdx
    x86.epilogue
    x86.ret
  empty_9.after:
    x86.mov rax, [rbp-152]
    x86.xor rcx, rcx
    x86.mov [rbp-184], rcx
    x86.mov [rbp-192], rax
    x86.jmp __Map_String_i64.insert.sd1_11.cmp0
  sd1_11.cmp0:
    x86.mov rax, [rbp-192]
    x86.mov rcx, 2
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.insert.sd1_11.cmp1
  sd1_11.case0:
    x86.mov rax, 1
    x86.mov [rbp-184], rax
    x86.jmp __Map_String_i64.insert.sd1_11.merge
  sd1_11.cmp1:
    x86.mov rax, [rbp-192]
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.insert.sd1_11.cmp2
  sd1_11.case1:
    x86.xor rax, rax
    x86.mov [rbp-184], rax
    x86.jmp __Map_String_i64.insert.sd1_11.merge
  sd1_11.cmp2:
    x86.mov rax, [rbp-192]
    x86.mov rcx, 1
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.insert.sd1_11.merge
  sd1_11.case2:
    x86.xor rax, rax
    x86.mov [rbp-184], rax
    x86.jmp __Map_String_i64.insert.sd1_11.merge
  sd1_11.merge:
    x86.mov rax, [rbp-184]
    x86.xor rcx, rcx
    x86.mov rdx, [rbp-112]
    x86.cmp rdx, rcx
    x86.setl rbx
    x86.movzx rbx, rbxb
    x86.and rax, rbx
    x86.test rax, rax
    x86.je __Map_String_i64.insert.mark_deleted_12.merge
  mark_deleted_12:
    x86.mov rax, [rbp-104]
    x86.mov [rbp-112], rax
    x86.jmp __Map_String_i64.insert.mark_deleted_12.merge
  mark_deleted_12.merge:
    x86.mov rax, [rbp-152]
    x86.xor rcx, rcx
    x86.mov [rbp-200], rcx
    x86.mov [rbp-208], rax
    x86.jmp __Map_String_i64.insert.so1_13.cmp0
  so1_13.cmp0:
    x86.mov rax, [rbp-208]
    x86.mov rcx, 1
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.insert.so1_13.cmp1
  so1_13.case0:
    x86.mov rax, 1
    x86.mov [rbp-200], rax
    x86.jmp __Map_String_i64.insert.so1_13.merge
  so1_13.cmp1:
    x86.mov rax, [rbp-208]
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.insert.so1_13.cmp2
  so1_13.case1:
    x86.xor rax, rax
    x86.mov [rbp-200], rax
    x86.jmp __Map_String_i64.insert.so1_13.merge
  so1_13.cmp2:
    x86.mov rax, [rbp-208]
    x86.mov rcx, 2
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.insert.so1_13.merge
  so1_13.case2:
    x86.xor rax, rax
    x86.mov [rbp-200], rax
    x86.jmp __Map_String_i64.insert.so1_13.merge
  so1_13.merge:
    x86.mov rax, [rbp-200]
    x86.test rax, rax
    x86.je __Map_String_i64.insert.check_key_14.merge
  check_key_14:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-216], rcx
    x86.mov rdx, [rbp-104]
    x86.mov rbx, [rbp-216]
    x86.mov rcx, [rbp-216]
    x86.call StringArray.get
    x86.mov [rbp-224], rax
    x86.xor rsi, rsi
    x86.cmp rdx, rsi
    x86.jne __Map_String_i64.insert.get_existing_17.merge
  get_existing_17:
    x86.mov rax, [rbp-224]
    x86.mov [rbp-232], rax
    x86.mov rcx, [rbp-232]
    x86.call mm_incref
    x86.mov rdx, [rbp-232]
    x86.mov rbx, [rbp-40]
    x86.mov rcx, [rbp-232]
    x86.mov rdx, [rbp-40]
    x86.call stdlib.String.equals
    x86.test rax, rax
    x86.je __Map_String_i64.insert.exists_18.after
  exists_18:
    x86.mov rax, [rbp-104]
    x86.mov rcx, [rbp-48]
    x86.mov rdx, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.mov rdx, [rbp-104]
    x86.mov r8, [rbp-48]
    x86.call Array_i64.set
    x86.mov rbx, [rbp-224]
    x86.test rbx, rbx
    x86.jz __nonnull_skip_33
    x86.mov rcx, [rbp-224]
    x86.call mm_decref
    x86.label __nonnull_skip_33
    x86.mov rsi, [rbp-232]
    x86.test rsi, rsi
    x86.jz __nonnull_skip_34
    x86.mov rcx, [rbp-232]
    x86.call mm_decref
    x86.label __nonnull_skip_34
    x86.epilogue
    x86.ret
  exists_18.after:
    x86.mov rax, [rbp-232]
    x86.test rax, rax
    x86.jz __nonnull_skip_35
    x86.mov rcx, [rbp-232]
    x86.call mm_decref
    x86.label __nonnull_skip_35
    x86.jmp __Map_String_i64.insert.get_existing_17.merge
  get_existing_17.merge:
    x86.mov rax, [rbp-224]
    x86.test rax, rax
    x86.jz __nonnull_skip_36
    x86.mov rcx, [rbp-224]
    x86.call mm_decref
    x86.label __nonnull_skip_36
    x86.jmp __Map_String_i64.insert.check_key_14.merge
  check_key_14.merge:
    x86.mov rax, 1
    x86.mov rcx, [rbp-104]
    x86.add rcx, rax
    x86.mov rdx, [rbp-8]
    x86.mov rbx, [rdx+32]
    x86.mov rax, rcx
    x86.cqo
    x86.idiv rbx
    x86.mov [rbp-104], rdx
    x86.mov rsi, 1
    x86.mov rdi, [rbp-120]
    x86.add rdi, rsi
    x86.mov [rbp-120], rdi
    x86.jmp __Map_String_i64.insert.probe_3.header
  probe_3.exit:
    x86.xor rax, rax
    x86.mov rcx, [rbp-112]
    x86.cmp rcx, rax
    x86.jl __Map_String_i64.insert.fallback_19.merge
  fallback_19:
    x86.mov rax, [rbp-112]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rbp-40]
    x86.mov rdx, [rbp-112]
    x86.mov r8, [rbp-40]
    x86.call StringArray.set
    x86.mov rbx, [rbp-112]
    x86.mov rsi, [rbp-48]
    x86.mov rdi, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.mov rdx, [rbp-112]
    x86.mov r8, [rbp-48]
    x86.call Array_i64.set
    x86.mov r8, [rbp-112]
    x86.mov r9, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.mov rdx, [rbp-112]
    x86.mov r8, 1
    x86.call StateArray.set
    x86.mov rax, 1
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+24]
    x86.add rdx, rax
    x86.mov rax, [rbp-8]
    x86.mov [rax+24], rdx
    x86.jmp __Map_String_i64.insert.fallback_19.merge
  fallback_19.merge:
    x86.epilogue
    x86.ret
  }
  func @__Map_String_i64.createIterator(__self_ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.xor rax, rax
    x86.mov rdx, [rbp-8]
    x86.mov [rdx+40], rax
    x86.epilogue
    x86.ret
  }
  func @__Map_String_i64.next(__self_ptr: i64) -> i64 {
  entry:
    x86.prologue stack_size=112
    x86.mov [rbp-8], rcx
    x86.jmp __Map_String_i64.next.scan_0.header
  scan_0.header:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+40]
    x86.mov rdx, [rbp-8]
    x86.mov rbx, [rdx+32]
    x86.cmp rcx, rbx
    x86.jge __Map_String_i64.next.scan_0.exit
  scan_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.mov [rbp-16], rcx
    x86.mov rdx, [rbp-8]
    x86.mov rbx, [rdx+40]
    x86.mov rsi, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, rbx
    x86.call StateArray.get
    x86.xor rdi, rdi
    x86.mov [rbp-24], rdi
    x86.mov [rbp-32], rax
    x86.xor r8, r8
    x86.cmp rdx, r8
    x86.je __Map_String_i64.next.otherwise_default_continue_4
  otherwise_default_error_3:
    x86.mov rax, [rbp-24]
    x86.mov [rbp-32], rax
    x86.jmp __Map_String_i64.next.otherwise_default_continue_4
  otherwise_default_continue_4:
    x86.mov rax, [rbp-32]
    x86.mov [rbp-40], rax
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+40]
    x86.mov [rbp-48], rdx
    x86.mov rbx, 1
    x86.mov rsi, [rbp-8]
    x86.mov rdi, [rsi+40]
    x86.add rdi, rbx
    x86.mov r8, [rbp-8]
    x86.mov [r8+40], rdi
    x86.mov r9, [rbp-40]
    x86.xor rax, rax
    x86.mov [rbp-56], rax
    x86.mov [rbp-64], r9
    x86.jmp __Map_String_i64.next.so1_5.cmp0
  so1_5.cmp0:
    x86.mov rax, [rbp-64]
    x86.mov rcx, 1
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.next.so1_5.cmp1
  so1_5.case0:
    x86.mov rax, 1
    x86.mov [rbp-56], rax
    x86.jmp __Map_String_i64.next.so1_5.merge
  so1_5.cmp1:
    x86.mov rax, [rbp-64]
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.next.so1_5.cmp2
  so1_5.case1:
    x86.xor rax, rax
    x86.mov [rbp-56], rax
    x86.jmp __Map_String_i64.next.so1_5.merge
  so1_5.cmp2:
    x86.mov rax, [rbp-64]
    x86.mov rcx, 2
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.next.so1_5.merge
  so1_5.case2:
    x86.xor rax, rax
    x86.mov [rbp-56], rax
    x86.jmp __Map_String_i64.next.so1_5.merge
  so1_5.merge:
    x86.mov rax, [rbp-56]
    x86.test rax, rax
    x86.je __Map_String_i64.next.occupied_6.after
  occupied_6:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-72], rcx
    x86.mov rdx, [rbp-48]
    x86.mov rbx, [rbp-72]
    x86.mov rcx, [rbp-72]
    x86.call StringArray.get
    x86.mov [rbp-80], rax
    x86.xor rsi, rsi
    x86.cmp rdx, rsi
    x86.je __Map_String_i64.next.otherwise_continue_8
  otherwise_error_7:
    x86.xor rax, rax
    x86.mov rcx, [rbp-80]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_37
    x86.call mm_decref
    x86.label __nonnull_skip_37
    x86.mov rdx, 1
    x86.xor rbx, rbx
    x86.lea rdx, [rbx + rdx]
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  otherwise_continue_8:
    x86.mov rax, [rbp-80]
    x86.mov [rbp-88], rax
    x86.mov rcx, [rbp-88]
    x86.call mm_incref
    x86.mov rdx, [rbp-8]
    x86.mov rbx, [rdx+8]
    x86.mov [rbp-96], rbx
    x86.mov rsi, [rbp-48]
    x86.mov rdi, [rbp-96]
    x86.mov rcx, [rbp-96]
    x86.mov rdx, [rbp-48]
    x86.call Array_i64.get
    x86.mov [rbp-104], rax
    x86.xor r8, r8
    x86.cmp rdx, r8
    x86.je __Map_String_i64.next.otherwise_continue_12
  otherwise_error_11:
    x86.xor rax, rax
    x86.mov rcx, [rbp-80]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_38
    x86.call mm_decref
    x86.label __nonnull_skip_38
    x86.mov rdx, [rbp-88]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_39
    x86.mov rcx, [rbp-88]
    x86.call mm_decref
    x86.label __nonnull_skip_39
    x86.mov rbx, 1
    x86.xor rsi, rsi
    x86.lea rdx, [rsi + rbx]
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  otherwise_continue_12:
    x86.mov rax, [rbp-104]
    x86.lea_func rcx, [__destruct_____Tuple_Key_Value_String_i64]
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 13
    x86.call mm_alloc
    x86.mov [rbp-112], rax
    x86.mov rbx, [rbp-88]
    x86.mov rsi, [rbp-112]
    x86.mov [rsi+0], rbx
    x86.mov rcx, [rbp-88]
    x86.call mm_incref
    x86.mov rdi, [rbp-112]
    x86.mov r8, [rbp-104]
    x86.mov [rdi+8], r8
    x86.mov r9, [rbp-80]
    x86.test r9, r9
    x86.jz __nonnull_skip_40
    x86.mov rcx, [rbp-80]
    x86.call mm_decref
    x86.label __nonnull_skip_40
    x86.mov rax, [rbp-88]
    x86.test rax, rax
    x86.jz __nonnull_skip_41
    x86.mov rcx, [rbp-88]
    x86.call mm_decref
    x86.label __nonnull_skip_41
    x86.mov rax, [rbp-112]
    x86.mov rcx, [rbp-112]
    x86.call mm_incref
    x86.mov rax, [rbp-112]
    x86.xor rdx, rdx
    x86.epilogue
    x86.ret
  occupied_6.after:
    x86.jmp __Map_String_i64.next.scan_0.header
  scan_0.exit:
    x86.xor rax, rax
    x86.mov rcx, 1
    x86.add rax, rcx
    x86.mov rdx, rax
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  }
  func @__Map_String_i64.grow(__self_ptr: i64) {
  entry:
    x86.prologue stack_size=208
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+0]
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+8]
    x86.mov [rbp-24], rdx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+16]
    x86.mov [rbp-32], rdx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+32]
    x86.mov [rbp-40], rdx
    x86.mov rax, 2
    x86.imul rdx, rax
    x86.mov [rbp-48], rdx
    x86.xor rax, rax
    x86.cmp rdx, rax
    x86.jne __Map_String_i64.grow.handle_zero_0.merge
  handle_zero_0:
    x86.mov rax, 16
    x86.mov [rbp-48], rax
    x86.jmp __Map_String_i64.grow.handle_zero_0.merge
  handle_zero_0.merge:
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.mov rsi, 8
    x86.lea_func rdi, [__destruct___ManagedMemory_String]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-56], rax
    x86.mov r9, [rbp-56]
    x86.xor rax, rax
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-56]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-56]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-56]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.lea_func rax, [__destruct_StringArray]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 7
    x86.call mm_alloc
    x86.mov [rbp-64], rax
    x86.mov rcx, [rbp-64]
    x86.xor rdx, rdx
    x86.mov [rcx+0], rdx
    x86.mov rcx, [rbp-56]
    x86.mov rdx, [rbp-64]
    x86.mov [rdx+8], rcx
    x86.call mm_incref
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-64]
    x86.call mm_incref
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-64]
    x86.mov rdx, [rbp-48]
    x86.call StringArray.resize
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.mov rsi, 8
    x86.lea_func rdi, [__destruct___ManagedMemory_i64]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 8
    x86.call mm_alloc
    x86.mov [rbp-72], rax
    x86.mov rax, [rbp-72]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-72]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-72]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-72]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.lea_func rax, [__destruct_Array_i64]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 9
    x86.call mm_alloc
    x86.mov [rbp-80], rax
    x86.mov rcx, [rbp-80]
    x86.xor rdx, rdx
    x86.mov [rcx+0], rdx
    x86.mov rcx, [rbp-72]
    x86.mov rdx, [rbp-80]
    x86.mov [rdx+8], rcx
    x86.call mm_incref
    x86.mov rax, [rbp-80]
    x86.mov rcx, [rbp-80]
    x86.call mm_incref
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-80]
    x86.mov rdx, [rbp-48]
    x86.call Array_i64.resize
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.mov rsi, 8
    x86.lea_func rdi, [__destruct___ManagedMemory_SlotState]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 10
    x86.call mm_alloc
    x86.mov [rbp-88], rax
    x86.mov rax, [rbp-88]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-88]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-88]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-88]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.lea_func rax, [__destruct_StateArray]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 11
    x86.call mm_alloc
    x86.mov [rbp-96], rax
    x86.mov rcx, [rbp-96]
    x86.xor rdx, rdx
    x86.mov [rcx+0], rdx
    x86.mov rcx, [rbp-88]
    x86.mov rdx, [rbp-96]
    x86.mov [rdx+8], rcx
    x86.call mm_incref
    x86.mov rax, [rbp-96]
    x86.mov rcx, [rbp-96]
    x86.call mm_incref
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-96]
    x86.mov rdx, [rbp-48]
    x86.call StateArray.resize
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-104], rcx
    x86.mov rax, [rbp-104]
    x86.mov rcx, [rbp-104]
    x86.call mm_incref
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-112], rcx
    x86.mov rax, [rbp-112]
    x86.mov rcx, [rbp-112]
    x86.call mm_incref
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.mov [rbp-120], rcx
    x86.mov rax, [rbp-120]
    x86.mov rcx, [rbp-120]
    x86.call mm_incref
    x86.mov rax, [rbp-16]
    x86.test rax, rax
    x86.jz __nonnull_skip_42
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_42
    x86.mov rax, [rbp-64]
    x86.mov [rbp-16], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-24]
    x86.test rax, rax
    x86.jz __nonnull_skip_43
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_43
    x86.mov rax, [rbp-80]
    x86.mov [rbp-24], rax
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-32]
    x86.test rax, rax
    x86.jz __nonnull_skip_44
    x86.mov rcx, [rbp-32]
    x86.call mm_decref
    x86.label __nonnull_skip_44
    x86.mov rax, [rbp-96]
    x86.mov [rbp-32], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+16], rax
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+32], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+24], rax
    x86.xor rax, rax
    x86.mov rcx, [rbp-40]
    x86.mov [rbp-128], rcx
    x86.mov [rbp-136], rax
    x86.jmp __Map_String_i64.grow.rehash_1.header
  rehash_1.header:
    x86.mov rax, [rbp-136]
    x86.mov rcx, [rbp-128]
    x86.cmp rax, rcx
    x86.jge __Map_String_i64.grow.rehash_1.exit
  rehash_1:
    x86.mov rax, [rbp-136]
    x86.mov [rbp-144], rax
    x86.mov rcx, [rbp-120]
    x86.mov rdx, [rbp-144]
    x86.call StateArray.get
    x86.xor rcx, rcx
    x86.mov [rbp-152], rcx
    x86.mov [rbp-160], rax
    x86.xor rax, rax
    x86.cmp rdx, rax
    x86.je __Map_String_i64.grow.otherwise_default_continue_5
  otherwise_default_error_4:
    x86.mov rax, [rbp-152]
    x86.mov [rbp-160], rax
    x86.jmp __Map_String_i64.grow.otherwise_default_continue_5
  otherwise_default_continue_5:
    x86.mov rax, [rbp-160]
    x86.mov [rbp-168], rax
    x86.mov rcx, [rbp-168]
    x86.xor rdx, rdx
    x86.mov [rbp-176], rdx
    x86.mov [rbp-184], rcx
    x86.jmp __Map_String_i64.grow.so1_6.cmp0
  so1_6.cmp0:
    x86.mov rax, [rbp-184]
    x86.mov rcx, 1
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.grow.so1_6.cmp1
  so1_6.case0:
    x86.mov rax, 1
    x86.mov [rbp-176], rax
    x86.jmp __Map_String_i64.grow.so1_6.merge
  so1_6.cmp1:
    x86.mov rax, [rbp-184]
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.grow.so1_6.cmp2
  so1_6.case1:
    x86.xor rax, rax
    x86.mov [rbp-176], rax
    x86.jmp __Map_String_i64.grow.so1_6.merge
  so1_6.cmp2:
    x86.mov rax, [rbp-184]
    x86.mov rcx, 2
    x86.cmp rax, rcx
    x86.jne __Map_String_i64.grow.so1_6.merge
  so1_6.case2:
    x86.xor rax, rax
    x86.mov [rbp-176], rax
    x86.jmp __Map_String_i64.grow.so1_6.merge
  so1_6.merge:
    x86.mov rax, [rbp-176]
    x86.test rax, rax
    x86.je __Map_String_i64.grow.occupied_7.merge
  occupied_7:
    x86.mov rax, [rbp-144]
    x86.mov rcx, [rbp-104]
    x86.mov rdx, [rbp-144]
    x86.call StringArray.get
    x86.mov [rbp-192], rax
    x86.xor rax, rax
    x86.cmp rdx, rax
    x86.je __Map_String_i64.grow.otherwise_continue_9
  otherwise_error_8:
    x86.mov rax, [rbp-192]
    x86.test rax, rax
    x86.jz __nonnull_skip_45
    x86.mov rcx, [rbp-192]
    x86.call mm_decref
    x86.label __nonnull_skip_45
    x86.jmp __Map_String_i64.grow.rehash_1.incr
  otherwise_continue_9:
    x86.mov rax, [rbp-192]
    x86.mov [rbp-200], rax
    x86.mov rcx, [rbp-200]
    x86.call mm_incref
    x86.mov rdx, [rbp-144]
    x86.mov rbx, [rbp-112]
    x86.mov rcx, [rbp-112]
    x86.call Array_i64.get
    x86.mov [rbp-208], rax
    x86.xor rsi, rsi
    x86.cmp rdx, rsi
    x86.je __Map_String_i64.grow.otherwise_continue_13
  otherwise_error_12:
    x86.mov rax, [rbp-200]
    x86.test rax, rax
    x86.jz __nonnull_skip_46
    x86.mov rcx, [rbp-200]
    x86.call mm_decref
    x86.label __nonnull_skip_46
    x86.mov rcx, [rbp-192]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_47
    x86.call mm_decref
    x86.label __nonnull_skip_47
    x86.jmp __Map_String_i64.grow.rehash_1.incr
  otherwise_continue_13:
    x86.mov rax, [rbp-208]
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rbp-200]
    x86.mov r8, [rbp-208]
    x86.call __Map_String_i64.insert
    x86.mov rbx, [rbp-200]
    x86.test rbx, rbx
    x86.jz __nonnull_skip_48
    x86.mov rcx, [rbp-200]
    x86.call mm_decref
    x86.label __nonnull_skip_48
    x86.mov rsi, [rbp-192]
    x86.test rsi, rsi
    x86.jz __nonnull_skip_49
    x86.mov rcx, [rbp-192]
    x86.call mm_decref
    x86.label __nonnull_skip_49
    x86.jmp __Map_String_i64.grow.occupied_7.merge
  occupied_7.merge:
    x86.jmp __Map_String_i64.grow.rehash_1.incr
  rehash_1.incr:
    x86.mov rax, 1
    x86.mov rcx, [rbp-136]
    x86.add rcx, rax
    x86.mov [rbp-136], rcx
    x86.jmp __Map_String_i64.grow.rehash_1.header
  rehash_1.exit:
    x86.mov rax, [rbp-120]
    x86.test rax, rax
    x86.jz __nonnull_skip_50
    x86.mov rcx, [rbp-120]
    x86.call mm_decref
    x86.label __nonnull_skip_50
    x86.mov rcx, [rbp-112]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_51
    x86.call mm_decref
    x86.label __nonnull_skip_51
    x86.mov rdx, [rbp-104]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_52
    x86.mov rcx, [rbp-104]
    x86.call mm_decref
    x86.label __nonnull_skip_52
    x86.mov rbx, [rbp-96]
    x86.test rbx, rbx
    x86.jz __nonnull_skip_53
    x86.mov rcx, [rbp-96]
    x86.call mm_decref
    x86.label __nonnull_skip_53
    x86.mov rsi, [rbp-80]
    x86.test rsi, rsi
    x86.jz __nonnull_skip_54
    x86.mov rcx, [rbp-80]
    x86.call mm_decref
    x86.label __nonnull_skip_54
    x86.mov rdi, [rbp-64]
    x86.test rdi, rdi
    x86.jz __nonnull_skip_55
    x86.mov rcx, [rbp-64]
    x86.call mm_decref
    x86.label __nonnull_skip_55
    x86.epilogue
    x86.ret
  }
  func @__Array_Entry.createIterator(__self_ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.xor rax, rax
    x86.mov rdx, [rbp-8]
    x86.mov [rdx+0], rax
    x86.epilogue
    x86.ret
  }
  func @__Array_Entry.next(__self_ptr: i64) -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+0]
    x86.mov rax, [rbp-8]
    x86.mov rbx, [rax+8]
    x86.mov [rbp-16], rbx
    x86.mov rax, [rbp-16]
    x86.mov rbx, [rax+8]
    x86.cmp rdx, rbx
    x86.jl __Array_Entry.next.done_0.after
  done_0:
    x86.xor rax, rax
    x86.mov rcx, 1
    x86.add rax, rcx
    x86.mov rdx, rax
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  done_0.after:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-24], rcx
    x86.mov rdx, [rbp-8]
    x86.mov rbx, [rdx+0]
    x86.mov rsi, [rbp-24]
    x86.mov rdi, [rsi+8]
    x86.lea_symdata r8, [__mm_panic_index_oob]
    x86.mov r9, r8
    x86.mov [rbp-40], rbx
    x86.mov rcx, [rbp-40]
    x86.mov rdx, rdi
    x86.mov r8, r9
    x86.call maxon_bounds_check
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rax+24]
    x86.mov rax, [rbp-24]
    x86.mov rdx, [rax+0]
    x86.mov rax, [rbp-40]
    x86.imul rax, rcx
    x86.add rdx, rax
    x86.mov rax, [rdx+0]
    x86.mov [rbp-48], rax
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
    x86.mov rax, [rbp-48]
    x86.mov [rbp-32], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+0]
    x86.add rdx, rax
    x86.mov rax, [rbp-8]
    x86.mov [rax+0], rdx
    x86.mov rax, [rbp-32]
    x86.xor rdx, rdx
    x86.epilogue
    x86.ret
  }
  func @__Array_____Tuple_Key_Value_String_i64.ensureCapacity(__self_ptr: i64, requiredLen: i64) {
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
    x86.jbe __Array_____Tuple_Key_Value_String_i64.ensureCapacity.grow_0.merge
  grow_0:
    x86.mov rax, [rbp-40]
    x86.mov [rbp-48], rax
    x86.mov rcx, 4
    x86.cmp rax, rcx
    x86.jge __Array_____Tuple_Key_Value_String_i64.ensureCapacity.min_1.merge
  min_1:
    x86.mov rax, 4
    x86.mov [rbp-48], rax
    x86.jmp __Array_____Tuple_Key_Value_String_i64.ensureCapacity.min_1.merge
  min_1.merge:
    x86.jmp __Array_____Tuple_Key_Value_String_i64.ensureCapacity.double_2.header
  double_2.header:
    x86.mov rax, [rbp-48]
    x86.mov rcx, [rbp-24]
    x86.cmp rax, rcx
    x86.jge __Array_____Tuple_Key_Value_String_i64.ensureCapacity.double_2.exit
  double_2:
    x86.mov rax, 2
    x86.mov rcx, [rbp-48]
    x86.imul rcx, rax
    x86.mov [rbp-48], rcx
    x86.jmp __Array_____Tuple_Key_Value_String_i64.ensureCapacity.double_2.header
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
    x86.mov [rbp-72], rdx
    x86.mov r8, [rbp-56]
    x86.mov r9, [rbp-64]
    x86.mov rsi, [rbp-16]
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
    x86.mov rdx, rbx
    x86.call mm_raw_realloc
    x86.mov rcx, [rbp-16]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-48]
    x86.mov [rax+16], rcx
    x86.jmp __Array_____Tuple_Key_Value_String_i64.ensureCapacity.grow_0.merge
  grow_0.merge:
    x86.epilogue
    x86.ret
  }
  func @__Array_____Tuple_Key_Value_String_i64.push(__self_ptr: i64, value: i64) {
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
    x86.call __Array_____Tuple_Key_Value_String_i64.ensureCapacity
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
    x86.mov [rbp-56], rdx
    x86.mov [rbp-64], r8
    x86.mov rcx, rsi
    x86.mov rdx, [rbp-64]
    x86.mov r8, [rbp-40]
    x86.mov r9, [rbp-56]
    x86.mov rsi, [rbp-16]
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
    x86.jz __nonnull_skip_56
    x86.mov rcx, [rbp-72]
    x86.call mm_decref
    x86.label __nonnull_skip_56
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
  func @____Tuple_Key_Value_String_i64.clone(__self_ptr: i64) -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+0]
    x86.mov [rbp-16], rdx
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call stdlib.String.clone
    x86.mov [rbp-24], rax
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+8]
    x86.lea_func rbx, [__destruct_____Tuple_Key_Value_String_i64]
    x86.mov rsi, rbx
    x86.mov [rbp-40], rdx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 13
    x86.call mm_alloc
    x86.mov [rbp-32], rax
    x86.mov rdi, [rbp-24]
    x86.mov r8, [rbp-32]
    x86.mov [r8+0], rdi
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov r9, [rbp-32]
    x86.mov rax, [rbp-40]
    x86.mov [r9+8], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.mov rax, [rbp-24]
    x86.test rax, rax
    x86.jz __nonnull_skip_57
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_57
    x86.mov rax, [rbp-32]
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
    x86.jz __nonnull_skip_58
    x86.call mm_decref
    x86.label __nonnull_skip_58
    x86.jmp __destruct_String.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory_String.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_managed_elements
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+0]
    x86.mov rcx, rdx
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory_String.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory_String.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct___ManagedMemory_Entry(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory_Entry.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_managed_elements
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+0]
    x86.mov rcx, rdx
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory_Entry.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory_Entry.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct___Array_____Tuple_Key_Value_String_i64(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_59
    x86.call mm_decref
    x86.label __nonnull_skip_59
    x86.jmp __destruct___Array_____Tuple_Key_Value_String_i64.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_60
    x86.call mm_decref
    x86.label __nonnull_skip_60
    x86.jmp __destruct_StringArray.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct___ManagedMemory_i64(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory_i64.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory_i64.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory_i64.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_Array_i64(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_61
    x86.call mm_decref
    x86.label __nonnull_skip_61
    x86.jmp __destruct_Array_i64.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct___ManagedMemory_SlotState(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory_SlotState.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory_SlotState.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory_SlotState.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_StateArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_62
    x86.call mm_decref
    x86.label __nonnull_skip_62
    x86.jmp __destruct_StateArray.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct___Map_String_i64(ptr: i64) {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_63
    x86.call mm_decref
    x86.label __nonnull_skip_63
    x86.mov rdx, [rbp-8]
    x86.mov rbx, [rdx+8]
    x86.mov [rbp-24], rbx
    x86.test rbx, rbx
    x86.jz __nonnull_skip_64
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_64
    x86.mov rsi, [rbp-8]
    x86.mov rdi, [rsi+16]
    x86.mov [rbp-32], rdi
    x86.test rdi, rdi
    x86.jz __nonnull_skip_65
    x86.mov rcx, [rbp-32]
    x86.call mm_decref
    x86.label __nonnull_skip_65
    x86.jmp __destruct___Map_String_i64.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_____Tuple_Key_Value_String_i64(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_66
    x86.call mm_decref
    x86.label __nonnull_skip_66
    x86.jmp __destruct_____Tuple_Key_Value_String_i64.done
  done:
    x86.epilogue
    x86.ret
  }
}
