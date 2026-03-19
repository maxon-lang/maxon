module {
  func @allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.mov rax, 1
    x86.mov rcx, 2
    x86.lea_symdata rdx, [__tag_allocator_main]
    x86.mov rbx, rdx
    x86.mov r9, rbx
    x86.mov rcx, 16
    x86.xor rdx, rdx
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-8], rax
    x86.mov rsi, [rbp-8]
    x86.mov rdi, 1
    x86.mov [rsi+0], rdi
    x86.mov r8, [rbp-8]
    x86.mov r9, 2
    x86.mov [r8+8], r9
    x86.mov rax, [rbp-8]
    x86.lea_symdata rcx, [__tag_allocator_main]
    x86.mov rdx, rcx
    x86.mov rcx, [rbp-8]
    x86.call mm_incref
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-16], rcx
    x86.xor rax, rax
    x86.cmp rcx, rax
    x86.setl rax
    x86.movzx rax, raxb
    x86.mov rdx, 4294967295
    x86.cmp rcx, rdx
    x86.setg rcx
    x86.movzx rcx, rcxb
    x86.or rax, rcx
    x86.test rax, rax
    x86.je allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_24]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-8]
    x86.lea_symdata rdx, [__tag_allocator_main]
    x86.mov rbx, rdx
    x86.mov [rbp-24], rbx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_0
    x86.mov rdx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov rax, [rbp-16]
    x86.epilogue
    x86.ret
  }
  func @__destruct_Point(ptr: i64) {
  entry:
    x86.jmp __destruct_Point.done
  done:
    x86.ret
  }
}
