module {
  func @HashBucket.lookup(__self_ptr: i64, target: i64) -> i1 {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-16], edx
    x86.xor eax, eax
    x86.mov [rbp-56], ecx
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-56]
    x86.mov [rbp-24], ecx
    x86.jmp HashBucket.lookup.loop_0.header
  loop_0.header:
    x86.mov eax, [rbp-24]
    x86.mov rcx, rax
    x86.call HashBucket.next
    x86.mov [rbp-32], eax
    x86.xor ecx, ecx
    x86.cmp edx, ecx
    x86.jne HashBucket.lookup.loop_0.exit
  loop_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-32]
    x86.mov [rbp-48], ecx
    x86.mov edx, [rbp-48]
    x86.mov ebx, [rbp-16]
    x86.mov rcx, rdx
    x86.mov rdx, rbx
    x86.call HashItem.equals
    x86.test eax, eax
    x86.je HashBucket.lookup.found_3.after
  found_3:
    x86.mov eax, 1
    x86.mov ecx, [rbp-40]
    x86.call mm_scope_exit
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, 1
    x86.epilogue
    x86.ret
  found_3.after:
    x86.mov rcx, [rbp-40]
    x86.call mm_scope_exit
    x86.jmp HashBucket.lookup.loop_0.header
  loop_0.exit:
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
  func @HashItem.equals(__self_ptr: i64, other: i64) -> i1 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], edx
    x86.mov eax, [rbp-8]
    x86.mov ebx, [eax+0]
    x86.mov eax, [rbp-16]
    x86.mov esi, [eax+0]
    x86.cmp ebx, esi
    x86.sete eax
    x86.movzx eax, eaxb
    x86.epilogue
    x86.ret
  }
  func @HashBucket.next(__self_ptr: i64) -> i64 {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-16], ecx
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.mov eax, [rbp-16]
    x86.mov edx, [eax+8]
    x86.mov eax, [rbp-16]
    x86.mov ebx, [eax+0]
    x86.mov [rbp-24], ebx
    x86.mov eax, [rbp-24]
    x86.mov [rbp-56], edx
    x86.mov rcx, rax
    x86.call HashItemArray.count
    x86.mov ecx, [rbp-56]
    x86.cmp ecx, eax
    x86.jl HashBucket.next.done_0.after
  done_0:
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  done_0.after:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-32], ecx
    x86.mov edx, [rbp-16]
    x86.mov ebx, [edx+8]
    x86.mov rcx, [rbp-32]
    x86.mov rdx, rbx
    x86.call HashItemArray.get
    x86.mov [rbp-40], eax
    x86.xor edi, edi
    x86.cmp edx, edi
    x86.je HashBucket.next.otherwise_continue_2
  otherwise_error_1:
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  otherwise_continue_2:
    x86.mov eax, [rbp-40]
    x86.mov [rbp-48], eax
    x86.mov ecx, 1
    x86.mov edx, [rbp-16]
    x86.mov ebx, [edx+8]
    x86.add ebx, ecx
    x86.mov esi, [rbp-16]
    x86.mov [esi+8], ebx
    x86.mov edi, [rbp-48]
    x86.mov r8, [rbp-8]
    x86.mov r9, [r8+8]
    x86.xor eax, eax
    x86.mov rcx, rdi
    x86.mov rdx, r9
    x86.mov r8, rax
    x86.call mm_move
    x86.mov eax, [rbp-48]
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  }
  func @conditional-extensions.multiple-constraints.main() -> u32 {
  entry:
    x86.prologue stack_size=80
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov edx, 8
    x86.xor ebx, ebx
    x86.mov rcx, rdx
    x86.mov rdx, rbx
    x86.call mm_alloc
    x86.mov [rbp-16], eax
    x86.mov esi, [rbp-16]
    x86.mov edi, 1
    x86.mov [esi+0], edi
    x86.mov r8, 2
    x86.mov r9, 8
    x86.xor ecx, ecx
    x86.mov rdx, rcx
    x86.mov rcx, r9
    x86.call mm_alloc
    x86.mov [rbp-24], eax
    x86.mov ecx, [rbp-24]
    x86.mov edx, 2
    x86.mov [ecx+0], edx
    x86.mov ecx, 3
    x86.mov edx, 8
    x86.xor ebx, ebx
    x86.mov rcx, rdx
    x86.mov rdx, rbx
    x86.call mm_alloc
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-32]
    x86.mov ecx, 3
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-24]
    x86.mov [rbp-40], eax
    x86.mov eax, [rbp-16]
    x86.mov [rbp-48], eax
    x86.xor eax, eax
    x86.mov ecx, 3
    x86.xor edx, edx
    x86.mov ebx, 8
    x86.mov esi, 32
    x86.xor edi, edi
    x86.mov rcx, rsi
    x86.mov rdx, rdi
    x86.call mm_alloc
    x86.mov [rbp-56], eax
    x86.mov eax, [rbp-56]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-56]
    x86.mov ecx, 3
    x86.mov [eax+8], ecx
    x86.mov eax, [rbp-56]
    x86.xor ecx, ecx
    x86.mov [eax+16], ecx
    x86.mov eax, [rbp-56]
    x86.mov ecx, 8
    x86.mov [eax+24], ecx
    x86.xor eax, eax
    x86.mov ecx, 16
    x86.xor edx, edx
    x86.call mm_alloc
    x86.mov [rbp-64], eax
    x86.mov eax, [rbp-64]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-64]
    x86.mov [ecx+8], eax
    x86.mov ecx, [rbp-64]
    x86.mov edx, 1
    x86.mov r8, rdx
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.call mm_move
    x86.lea rax, [rbp-48]
    x86.mov rcx, rax
    x86.mov eax, [rbp-64]
    x86.mov edx, [eax+8]
    x86.mov [edx+0], ecx
    x86.xor eax, eax
    x86.mov ecx, 16
    x86.xor edx, edx
    x86.call mm_alloc
    x86.mov [rbp-72], eax
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-72]
    x86.mov [ecx+0], eax
    x86.mov ecx, [rbp-72]
    x86.mov edx, 1
    x86.mov r8, rdx
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.call mm_move
    x86.mov eax, [rbp-72]
    x86.xor ecx, ecx
    x86.mov [eax+8], ecx
    x86.mov eax, 2
    x86.mov ecx, 8
    x86.xor edx, edx
    x86.call mm_alloc
    x86.mov [rbp-80], eax
    x86.mov eax, [rbp-80]
    x86.mov ecx, 2
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-72]
    x86.mov ecx, [rbp-80]
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.call HashBucket.lookup
    x86.test eax, eax
    x86.je conditional-extensions.multiple-constraints.main.found_1.after
  found_1:
    x86.mov eax, 1
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, 1
    x86.epilogue
    x86.ret
  found_1.after:
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
  func @HashItemArray.count(__self_ptr: i64) -> u64 {
  entry:
    x86.prologue stack_size=48
    x86.mov [rbp-16], ecx
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [ecx+8]
    x86.mov [rbp-24], edx
    x86.mov ebx, [rbp-24]
    x86.mov esi, [ebx+8]
    x86.mov [rbp-32], esi
    x86.xor edi, edi
    x86.cmp esi, edi
    x86.jge HashItemArray.count.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_1509]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-40], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-40]
    x86.epilogue
    x86.ret
  }
  func @HashItemArray.get(__self_ptr: i64, index: i64) -> i64 {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-16], ecx
    x86.mov [rbp-24], edx
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [ecx+8]
    x86.mov [rbp-32], edx
    x86.mov ebx, [rbp-32]
    x86.mov esi, [ebx+8]
    x86.mov edi, [rbp-24]
    x86.cmp edi, esi
    x86.jb HashItemArray.get.upper_0.after
  upper_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.xor ecx, ecx
    x86.mov edx, [rbp-40]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov esi, 1
    x86.xor edi, edi
    x86.lea edx, [edi + esi]
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  upper_0.after:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [eax+8]
    x86.mov [rbp-48], ecx
    x86.mov edx, [rbp-24]
    x86.mov ebx, [rbp-48]
    x86.mov esi, [ebx+24]
    x86.mov edi, [rbp-48]
    x86.mov r8, [edi+0]
    x86.imul edx, esi
    x86.add r8, edx
    x86.mov r9, [r8+0]
    x86.mov [rbp-56], r9
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, [rbp-56]
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  }
}
