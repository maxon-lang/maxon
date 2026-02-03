module {
  func @Array.count(__self_ptr: i64) -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.mov [rbp-8], ecx
    x86.mov eax, [ecx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add ecx, eax
    x86.mov eax, [ecx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [ecx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-32]
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
  func @Array.get(__self_ptr: i64, index: i64) -> i64 {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-8], ecx
    x86.mov eax, [ecx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add ecx, eax
    x86.mov eax, [ecx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [ecx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov [rbp-48], edx
    x86.mov eax, [rbp-32]
    x86.mov [rbp-56], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.jge Array.get.lower_0.after
  lower_0:
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  lower_0.after:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-56]
    x86.cmp eax, ecx
    x86.jl Array.get.upper_1.after
  upper_1:
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  upper_1.after:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-24]
    x86.mov edx, 8
    x86.imul eax, edx
    x86.add ecx, eax
    x86.mov ebx, [ecx+0]
    x86.mov eax, ebx
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  }
  func @Array.set(__self_ptr: i64, index: i64, value: i64) {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-8], ecx
    x86.mov eax, [ecx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add ecx, eax
    x86.mov eax, [ecx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [ecx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov [rbp-48], edx
    x86.mov [rbp-56], r8
    x86.mov eax, [rbp-32]
    x86.mov [rbp-64], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.jl Array.set.lower_0.after
  lower_0:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-64]
    x86.cmp eax, ecx
    x86.jge Array.set.upper_1.merge
  upper_1:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-56]
    x86.mov edx, [rbp-24]
    x86.mov ebx, 8
    x86.imul eax, ebx
    x86.add edx, eax
    x86.mov [edx+0], ecx
    x86.jmp Array.set.upper_1.merge
  upper_1.merge:
  lower_0.after:
    x86.epilogue
    x86.ret
  }
  func @Array.reserve(__self_ptr: i64, minCapacity: i64) {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-8], ecx
    x86.mov eax, [ecx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add ecx, eax
    x86.mov eax, [ecx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [ecx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov [rbp-48], edx
    x86.mov eax, [rbp-40]
    x86.mov [rbp-56], eax
    x86.cmp edx, eax
    x86.jle Array.reserve.grow_0.merge
  grow_0:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-24]
    x86.mov edx, 8
    x86.mov ebx, eax
    x86.imul ebx, edx
    x86.mov [rbp-64], eax
    x86.mov rdx, rbx
    x86.call maxon_realloc
    x86.mov [rbp-24], eax
    x86.mov esi, [rbp-64]
    x86.mov [rbp-40], esi
    x86.mov edi, [rbp-8]
    x86.mov [edi+8], eax
    x86.mov r8, [rbp-8]
    x86.mov [r8+24], esi
    x86.jmp Array.reserve.grow_0.merge
  grow_0.merge:
    x86.epilogue
    x86.ret
  }
  func @Array.resize(__self_ptr: i64, newLength: i64) {
  entry:
    x86.prologue stack_size=96
    x86.mov [rbp-8], ecx
    x86.mov eax, [ecx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add ecx, eax
    x86.mov eax, [ecx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [ecx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov [rbp-48], edx
    x86.mov eax, [rbp-40]
    x86.mov [rbp-56], eax
    x86.mov eax, [rbp-32]
    x86.mov [rbp-64], eax
    x86.mov eax, [rbp-24]
    x86.mov [rbp-72], eax
    x86.mov eax, [rbp-16]
    x86.mov [rbp-80], eax
    x86.lea rax, [rbp-80]
    x86.mov [rbp-88], ecx
    x86.mov rcx, rax
    x86.call Array.reserve
    x86.mov edx, [rbp-80]
    x86.mov [rbp-16], edx
    x86.mov ebx, [rbp-72]
    x86.mov [rbp-24], ebx
    x86.mov esi, [rbp-64]
    x86.mov [rbp-32], esi
    x86.mov edi, [rbp-56]
    x86.mov [rbp-40], edi
    x86.mov r8, [rbp-8]
    x86.mov r9, [rbp-16]
    x86.mov [r8+0], r9
    x86.mov eax, 8
    x86.add r8, eax
    x86.mov eax, [rbp-24]
    x86.mov [r8+0], eax
    x86.mov eax, [rbp-32]
    x86.mov [r8+8], eax
    x86.mov eax, [rbp-40]
    x86.mov [r8+16], eax
    x86.mov eax, [rbp-48]
    x86.mov [rbp-32], eax
    x86.mov ecx, [rbp-8]
    x86.mov [ecx+16], eax
    x86.epilogue
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=352
    x86.mov eax, 1
    x86.mov ecx, 2
    x86.mov edx, 3
    x86.mov [rbp-8], edx
    x86.mov [rbp-16], ecx
    x86.mov [rbp-24], eax
    x86.xor ebx, ebx
    x86.mov esi, 3
    x86.xor edi, edi
    x86.mov [rbp-32], ebx
    x86.mov [rbp-40], esi
    x86.mov [rbp-48], edi
    x86.xor r8, r8
    x86.mov [rbp-56], r8
    x86.mov r9, [rbp-32]
    x86.mov [rbp-64], r9
    x86.mov eax, [rbp-40]
    x86.mov [rbp-72], eax
    x86.mov eax, [rbp-48]
    x86.mov [rbp-80], eax
    x86.lea rax, [rbp-24]
    x86.mov rcx, rax
    x86.mov [rbp-64], ecx
    x86.xor eax, eax
    x86.mov [rbp-88], eax
    x86.xor eax, eax
    x86.mov [rbp-96], eax
    x86.xor eax, eax
    x86.mov [rbp-104], eax
    x86.xor eax, eax
    x86.mov [rbp-112], eax
    x86.xor eax, eax
    x86.mov [rbp-120], eax
    x86.xor eax, eax
    x86.mov [rbp-128], eax
    x86.xor eax, eax
    x86.mov [rbp-136], eax
    x86.xor eax, eax
    x86.mov [rbp-144], eax
    x86.xor eax, eax
    x86.mov [rbp-152], eax
    x86.xor eax, eax
    x86.mov [rbp-160], eax
    x86.xor eax, eax
    x86.mov [rbp-168], eax
    x86.lea rax, [rbp-168]
    x86.mov ecx, [rbp-56]
    x86.mov edx, [rbp-64]
    x86.mov ebx, [rbp-72]
    x86.mov esi, [rbp-80]
    x86.sub rsp, 16
    x86.mov [rsp+0], esi
    x86.mov r8, rdx
    x86.mov r9, rbx
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.call __Set_3_i64.init
    x86.add rsp, 16
    x86.mov eax, [rbp-168]
    x86.mov [rbp-176], eax
    x86.mov eax, [rbp-160]
    x86.mov [rbp-184], eax
    x86.mov eax, [rbp-152]
    x86.mov [rbp-192], eax
    x86.mov eax, [rbp-144]
    x86.mov [rbp-200], eax
    x86.mov eax, [rbp-136]
    x86.mov [rbp-208], eax
    x86.mov eax, [rbp-128]
    x86.mov [rbp-216], eax
    x86.mov eax, [rbp-120]
    x86.mov [rbp-224], eax
    x86.mov eax, [rbp-112]
    x86.mov [rbp-232], eax
    x86.mov eax, [rbp-104]
    x86.mov [rbp-240], eax
    x86.mov eax, [rbp-96]
    x86.mov [rbp-248], eax
    x86.mov eax, [rbp-88]
    x86.mov [rbp-256], eax
    x86.mov eax, [rbp-256]
    x86.mov [rbp-264], eax
    x86.mov eax, [rbp-248]
    x86.mov [rbp-272], eax
    x86.mov eax, [rbp-240]
    x86.mov [rbp-280], eax
    x86.mov eax, [rbp-232]
    x86.mov [rbp-288], eax
    x86.mov eax, [rbp-224]
    x86.mov [rbp-296], eax
    x86.mov eax, [rbp-216]
    x86.mov [rbp-304], eax
    x86.mov eax, [rbp-208]
    x86.mov [rbp-312], eax
    x86.mov eax, [rbp-200]
    x86.mov [rbp-320], eax
    x86.mov eax, [rbp-192]
    x86.mov [rbp-328], eax
    x86.mov eax, [rbp-184]
    x86.mov [rbp-336], eax
    x86.mov eax, [rbp-176]
    x86.mov [rbp-344], eax
    x86.lea rax, [rbp-344]
    x86.mov rcx, rax
    x86.call __Set_3_i64.count
    x86.mov ecx, [rbp-344]
    x86.mov [rbp-176], ecx
    x86.mov ecx, [rbp-336]
    x86.mov [rbp-184], ecx
    x86.mov ecx, [rbp-328]
    x86.mov [rbp-192], ecx
    x86.mov ecx, [rbp-320]
    x86.mov [rbp-200], ecx
    x86.mov ecx, [rbp-312]
    x86.mov [rbp-208], ecx
    x86.mov ecx, [rbp-304]
    x86.mov [rbp-216], ecx
    x86.mov ecx, [rbp-296]
    x86.mov [rbp-224], ecx
    x86.mov ecx, [rbp-288]
    x86.mov [rbp-232], ecx
    x86.mov ecx, [rbp-280]
    x86.mov [rbp-240], ecx
    x86.mov ecx, [rbp-272]
    x86.mov [rbp-248], ecx
    x86.mov ecx, [rbp-264]
    x86.mov [rbp-256], ecx
    x86.epilogue
    x86.ret
  }
  func @IntArray.get(__self_ptr: i64, index: i64) -> i64 {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-8], ecx
    x86.mov eax, [ecx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add ecx, eax
    x86.mov eax, [ecx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [ecx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov [rbp-48], edx
    x86.mov eax, [rbp-32]
    x86.mov [rbp-56], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.jge IntArray.get.lower_0.after
  lower_0:
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  lower_0.after:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-56]
    x86.cmp eax, ecx
    x86.jl IntArray.get.upper_1.after
  upper_1:
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  upper_1.after:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-24]
    x86.mov edx, 8
    x86.imul eax, edx
    x86.add ecx, eax
    x86.mov ebx, [ecx+0]
    x86.mov eax, ebx
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  }
  func @IntArray.set(__self_ptr: i64, index: i64, value: i64) {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-8], ecx
    x86.mov eax, [ecx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add ecx, eax
    x86.mov eax, [ecx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [ecx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov [rbp-48], edx
    x86.mov [rbp-56], r8
    x86.mov eax, [rbp-32]
    x86.mov [rbp-64], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.jl IntArray.set.lower_0.after
  lower_0:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-64]
    x86.cmp eax, ecx
    x86.jge IntArray.set.upper_1.merge
  upper_1:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-56]
    x86.mov edx, [rbp-24]
    x86.mov ebx, 8
    x86.imul eax, ebx
    x86.add edx, eax
    x86.mov [edx+0], ecx
    x86.jmp IntArray.set.upper_1.merge
  upper_1.merge:
  lower_0.after:
    x86.epilogue
    x86.ret
  }
  func @IntArray.reserve(__self_ptr: i64, minCapacity: i64) {
  entry:
    x86.prologue stack_size=64
    x86.mov [rbp-8], ecx
    x86.mov eax, [ecx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add ecx, eax
    x86.mov eax, [ecx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [ecx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov [rbp-48], edx
    x86.mov eax, [rbp-40]
    x86.mov [rbp-56], eax
    x86.cmp edx, eax
    x86.jle IntArray.reserve.grow_0.merge
  grow_0:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-24]
    x86.mov edx, 8
    x86.mov ebx, eax
    x86.imul ebx, edx
    x86.mov [rbp-64], eax
    x86.mov rdx, rbx
    x86.call maxon_realloc
    x86.mov [rbp-24], eax
    x86.mov esi, [rbp-64]
    x86.mov [rbp-40], esi
    x86.mov edi, [rbp-8]
    x86.mov [edi+8], eax
    x86.mov r8, [rbp-8]
    x86.mov [r8+24], esi
    x86.jmp IntArray.reserve.grow_0.merge
  grow_0.merge:
    x86.epilogue
    x86.ret
  }
  func @IntArray.resize(__self_ptr: i64, newLength: i64) {
  entry:
    x86.prologue stack_size=96
    x86.mov [rbp-8], ecx
    x86.mov eax, [ecx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add ecx, eax
    x86.mov eax, [ecx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [ecx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov [rbp-48], edx
    x86.mov eax, [rbp-40]
    x86.mov [rbp-56], eax
    x86.mov eax, [rbp-32]
    x86.mov [rbp-64], eax
    x86.mov eax, [rbp-24]
    x86.mov [rbp-72], eax
    x86.mov eax, [rbp-16]
    x86.mov [rbp-80], eax
    x86.lea rax, [rbp-80]
    x86.mov [rbp-88], ecx
    x86.mov rcx, rax
    x86.call IntArray.reserve
    x86.mov edx, [rbp-80]
    x86.mov [rbp-16], edx
    x86.mov ebx, [rbp-72]
    x86.mov [rbp-24], ebx
    x86.mov esi, [rbp-64]
    x86.mov [rbp-32], esi
    x86.mov edi, [rbp-56]
    x86.mov [rbp-40], edi
    x86.mov r8, [rbp-8]
    x86.mov r9, [rbp-16]
    x86.mov [r8+0], r9
    x86.mov eax, 8
    x86.add r8, eax
    x86.mov eax, [rbp-24]
    x86.mov [r8+0], eax
    x86.mov eax, [rbp-32]
    x86.mov [r8+8], eax
    x86.mov eax, [rbp-40]
    x86.mov [r8+16], eax
    x86.mov eax, [rbp-48]
    x86.mov [rbp-32], eax
    x86.mov ecx, [rbp-8]
    x86.mov [ecx+16], eax
    x86.epilogue
    x86.ret
  }
  func @__Set_3_i64.init(__sret: i64, arr.iterIndex: i64, arr.managed.buffer: i64, arr.managed.length: i64, arr.managed.capacity: i64) {
  entry:
    x86.prologue stack_size=624
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], edx
    x86.mov [rbp-24], r8
    x86.mov [rbp-32], r9
    x86.mov eax, [rbp+16]
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-40]
    x86.mov [rbp-48], ecx
    x86.mov edx, [rbp-32]
    x86.mov [rbp-56], edx
    x86.mov ebx, [rbp-24]
    x86.mov [rbp-64], ebx
    x86.mov esi, [rbp-16]
    x86.mov [rbp-72], esi
    x86.lea rdi, [rbp-72]
    x86.mov rcx, rdi
    x86.call Array.count
    x86.mov r8, [rbp-72]
    x86.mov [rbp-16], r8
    x86.mov r9, [rbp-64]
    x86.mov [rbp-24], r9
    x86.mov ecx, [rbp-56]
    x86.mov [rbp-32], ecx
    x86.mov ecx, [rbp-48]
    x86.mov [rbp-40], ecx
    x86.mov [rbp-80], eax
    x86.mov eax, 16
    x86.mov [rbp-88], eax
    x86.jmp __Set_3_i64.init.calc_cap_0.header
  calc_cap_0.header:
    x86.mov eax, 2
    x86.mov ecx, [rbp-80]
    x86.imul ecx, eax
    x86.mov edx, [rbp-88]
    x86.cmp edx, ecx
    x86.jge __Set_3_i64.init.calc_cap_0.exit
  calc_cap_0:
    x86.mov eax, 2
    x86.mov ecx, [rbp-88]
    x86.imul ecx, eax
    x86.mov [rbp-88], ecx
    x86.jmp __Set_3_i64.init.calc_cap_0.header
  calc_cap_0.exit:
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.mov [rbp-96], ecx
    x86.mov [rbp-104], edx
    x86.mov [rbp-112], ebx
    x86.mov [rbp-120], eax
    x86.mov esi, [rbp-96]
    x86.mov [rbp-128], esi
    x86.mov edi, [rbp-104]
    x86.mov [rbp-136], edi
    x86.mov r8, [rbp-112]
    x86.mov [rbp-144], r8
    x86.mov r9, [rbp-120]
    x86.mov [rbp-152], r9
    x86.mov eax, [rbp-128]
    x86.mov [rbp-160], eax
    x86.mov eax, [rbp-136]
    x86.mov [rbp-168], eax
    x86.mov eax, [rbp-144]
    x86.mov [rbp-176], eax
    x86.mov eax, [rbp-88]
    x86.mov ecx, [rbp-176]
    x86.mov [rbp-184], ecx
    x86.mov ecx, [rbp-168]
    x86.mov [rbp-192], ecx
    x86.mov ecx, [rbp-160]
    x86.mov [rbp-200], ecx
    x86.mov ecx, [rbp-152]
    x86.mov [rbp-208], ecx
    x86.lea rcx, [rbp-208]
    x86.mov rdx, rax
    x86.call Array.resize
    x86.mov eax, [rbp-208]
    x86.mov [rbp-152], eax
    x86.mov eax, [rbp-200]
    x86.mov [rbp-160], eax
    x86.mov eax, [rbp-192]
    x86.mov [rbp-168], eax
    x86.mov eax, [rbp-184]
    x86.mov [rbp-176], eax
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.mov [rbp-216], ecx
    x86.mov [rbp-224], edx
    x86.mov [rbp-232], ebx
    x86.mov [rbp-240], eax
    x86.mov eax, [rbp-216]
    x86.mov [rbp-248], eax
    x86.mov eax, [rbp-224]
    x86.mov [rbp-256], eax
    x86.mov eax, [rbp-232]
    x86.mov [rbp-264], eax
    x86.mov eax, [rbp-240]
    x86.mov [rbp-272], eax
    x86.mov eax, [rbp-248]
    x86.mov [rbp-280], eax
    x86.mov eax, [rbp-256]
    x86.mov [rbp-288], eax
    x86.mov eax, [rbp-264]
    x86.mov [rbp-296], eax
    x86.mov eax, [rbp-88]
    x86.mov ecx, [rbp-296]
    x86.mov [rbp-304], ecx
    x86.mov ecx, [rbp-288]
    x86.mov [rbp-312], ecx
    x86.mov ecx, [rbp-280]
    x86.mov [rbp-320], ecx
    x86.mov ecx, [rbp-272]
    x86.mov [rbp-328], ecx
    x86.lea rcx, [rbp-328]
    x86.mov rdx, rax
    x86.call IntArray.resize
    x86.mov eax, [rbp-328]
    x86.mov [rbp-272], eax
    x86.mov eax, [rbp-320]
    x86.mov [rbp-280], eax
    x86.mov eax, [rbp-312]
    x86.mov [rbp-288], eax
    x86.mov eax, [rbp-304]
    x86.mov [rbp-296], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-88]
    x86.xor edx, edx
    x86.mov ebx, [rbp-152]
    x86.mov [rbp-336], ebx
    x86.mov ebx, [rbp-160]
    x86.mov [rbp-344], ebx
    x86.mov ebx, [rbp-168]
    x86.mov [rbp-352], ebx
    x86.mov ebx, [rbp-176]
    x86.mov [rbp-360], ebx
    x86.mov ebx, [rbp-272]
    x86.mov [rbp-368], ebx
    x86.mov ebx, [rbp-280]
    x86.mov [rbp-376], ebx
    x86.mov ebx, [rbp-288]
    x86.mov [rbp-384], ebx
    x86.mov ebx, [rbp-296]
    x86.mov [rbp-392], ebx
    x86.mov [rbp-400], eax
    x86.mov [rbp-408], ecx
    x86.mov [rbp-416], edx
    x86.mov eax, [rbp-336]
    x86.mov [rbp-424], eax
    x86.mov eax, [rbp-344]
    x86.mov [rbp-432], eax
    x86.mov eax, [rbp-352]
    x86.mov [rbp-440], eax
    x86.mov eax, [rbp-360]
    x86.mov [rbp-448], eax
    x86.mov eax, [rbp-368]
    x86.mov [rbp-456], eax
    x86.mov eax, [rbp-376]
    x86.mov [rbp-464], eax
    x86.mov eax, [rbp-384]
    x86.mov [rbp-472], eax
    x86.mov eax, [rbp-392]
    x86.mov [rbp-480], eax
    x86.mov eax, [rbp-400]
    x86.mov [rbp-488], eax
    x86.mov eax, [rbp-408]
    x86.mov [rbp-496], eax
    x86.mov eax, [rbp-416]
    x86.mov [rbp-504], eax
    x86.xor eax, eax
    x86.mov [rbp-512], eax
    x86.mov eax, [rbp-32]
    x86.mov [rbp-520], eax
    x86.jmp __Set_3_i64.init.insert_loop_1.header
  insert_loop_1.header:
    x86.mov eax, [rbp-512]
    x86.mov ecx, [rbp-520]
    x86.cmp eax, ecx
    x86.jge __Set_3_i64.init.insert_loop_1.exit
  insert_loop_1:
    x86.mov eax, [rbp-512]
    x86.mov ecx, [rbp-24]
    x86.mov edx, 8
    x86.imul eax, edx
    x86.add ecx, eax
    x86.mov ebx, [ecx+0]
    x86.mov [rbp-528], ebx
    x86.mov esi, [rbp-512]
    x86.mov edi, 1
    x86.add esi, edi
    x86.mov [rbp-512], esi
    x86.mov r8, [rbp-504]
    x86.mov [rbp-536], r8
    x86.mov r9, [rbp-496]
    x86.mov [rbp-544], r9
    x86.mov eax, [rbp-488]
    x86.mov [rbp-552], eax
    x86.mov eax, [rbp-480]
    x86.mov [rbp-560], eax
    x86.mov eax, [rbp-472]
    x86.mov [rbp-568], eax
    x86.mov eax, [rbp-464]
    x86.mov [rbp-576], eax
    x86.mov eax, [rbp-456]
    x86.mov [rbp-584], eax
    x86.mov eax, [rbp-448]
    x86.mov [rbp-592], eax
    x86.mov eax, [rbp-440]
    x86.mov [rbp-600], eax
    x86.mov eax, [rbp-432]
    x86.mov [rbp-608], eax
    x86.mov eax, [rbp-424]
    x86.mov [rbp-616], eax
    x86.lea rax, [rbp-616]
    x86.mov rcx, rax
    x86.mov rdx, rbx
    x86.call __Set_3_i64.insert
    x86.mov eax, [rbp-616]
    x86.mov [rbp-424], eax
    x86.mov eax, [rbp-608]
    x86.mov [rbp-432], eax
    x86.mov eax, [rbp-600]
    x86.mov [rbp-440], eax
    x86.mov eax, [rbp-592]
    x86.mov [rbp-448], eax
    x86.mov eax, [rbp-584]
    x86.mov [rbp-456], eax
    x86.mov eax, [rbp-576]
    x86.mov [rbp-464], eax
    x86.mov eax, [rbp-568]
    x86.mov [rbp-472], eax
    x86.mov eax, [rbp-560]
    x86.mov [rbp-480], eax
    x86.mov eax, [rbp-552]
    x86.mov [rbp-488], eax
    x86.mov eax, [rbp-544]
    x86.mov [rbp-496], eax
    x86.mov eax, [rbp-536]
    x86.mov [rbp-504], eax
    x86.jmp __Set_3_i64.init.insert_loop_1.header
  insert_loop_1.exit:
    x86.mov eax, [rbp-8]
    x86.xor ecx, ecx
    x86.mov edx, eax
    x86.add edx, ecx
    x86.mov ebx, [rbp-424]
    x86.mov [edx+0], ebx
    x86.mov esi, 8
    x86.add edx, esi
    x86.mov edi, [rbp-432]
    x86.mov [edx+0], edi
    x86.mov r8, [rbp-440]
    x86.mov [edx+8], r8
    x86.mov r9, [rbp-448]
    x86.mov [edx+16], r9
    x86.mov ecx, 8
    x86.mov edx, eax
    x86.add edx, ecx
    x86.mov ecx, [rbp-456]
    x86.mov [edx+0], ecx
    x86.mov ecx, 8
    x86.add edx, ecx
    x86.mov ecx, [rbp-464]
    x86.mov [edx+0], ecx
    x86.mov ecx, [rbp-472]
    x86.mov [edx+8], ecx
    x86.mov ecx, [rbp-480]
    x86.mov [edx+16], ecx
    x86.mov ecx, [rbp-488]
    x86.mov [eax+16], ecx
    x86.mov ecx, [rbp-496]
    x86.mov [eax+24], ecx
    x86.mov ecx, [rbp-504]
    x86.mov [eax+32], ecx
    x86.epilogue
    x86.ret
  }
  func @__Set_3_i64.insert(__self_ptr: i64, element: i64) {
  entry:
    x86.prologue stack_size=816
    x86.mov [rbp-8], ecx
    x86.xor eax, eax
    x86.mov edx, ecx
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [edx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [edx+16]
    x86.mov [rbp-40], eax
    x86.mov eax, 8
    x86.mov edx, ecx
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-48], eax
    x86.mov eax, 8
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-56], eax
    x86.mov eax, [edx+8]
    x86.mov [rbp-64], eax
    x86.mov eax, [edx+16]
    x86.mov [rbp-72], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-80], eax
    x86.mov eax, [ecx+24]
    x86.mov [rbp-88], eax
    x86.mov eax, [ecx+32]
    x86.mov [rbp-96], eax
    x86.mov ecx, [rbp-80]
    x86.mov edx, [rbp-88]
    x86.mov ebx, [rbp-96]
    x86.mov [rbp-104], edx
    x86.xor esi, esi
    x86.cmp edx, esi
    x86.jne __Set_3_i64.insert.init_empty_0.merge
  init_empty_0:
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.mov [rbp-112], ecx
    x86.mov [rbp-120], edx
    x86.mov [rbp-128], ebx
    x86.mov [rbp-136], eax
    x86.mov esi, [rbp-112]
    x86.mov [rbp-144], esi
    x86.mov edi, [rbp-120]
    x86.mov [rbp-152], edi
    x86.mov r8, [rbp-128]
    x86.mov [rbp-160], r8
    x86.mov r9, [rbp-136]
    x86.mov [rbp-168], r9
    x86.mov eax, [rbp-144]
    x86.mov [rbp-176], eax
    x86.mov eax, [rbp-152]
    x86.mov [rbp-184], eax
    x86.mov eax, [rbp-160]
    x86.mov [rbp-192], eax
    x86.mov eax, 16
    x86.mov ecx, [rbp-192]
    x86.mov [rbp-200], ecx
    x86.mov ecx, [rbp-184]
    x86.mov [rbp-208], ecx
    x86.mov ecx, [rbp-176]
    x86.mov [rbp-216], ecx
    x86.mov ecx, [rbp-168]
    x86.mov [rbp-224], ecx
    x86.lea rcx, [rbp-224]
    x86.mov rdx, rax
    x86.call Array.resize
    x86.mov eax, [rbp-224]
    x86.mov [rbp-168], eax
    x86.mov eax, [rbp-216]
    x86.mov [rbp-176], eax
    x86.mov eax, [rbp-208]
    x86.mov [rbp-184], eax
    x86.mov eax, [rbp-200]
    x86.mov [rbp-192], eax
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.mov [rbp-232], ecx
    x86.mov [rbp-240], edx
    x86.mov [rbp-248], ebx
    x86.mov [rbp-256], eax
    x86.mov eax, [rbp-232]
    x86.mov [rbp-264], eax
    x86.mov eax, [rbp-240]
    x86.mov [rbp-272], eax
    x86.mov eax, [rbp-248]
    x86.mov [rbp-280], eax
    x86.mov eax, [rbp-256]
    x86.mov [rbp-288], eax
    x86.mov eax, [rbp-264]
    x86.mov [rbp-296], eax
    x86.mov eax, [rbp-272]
    x86.mov [rbp-304], eax
    x86.mov eax, [rbp-280]
    x86.mov [rbp-312], eax
    x86.mov eax, 16
    x86.mov ecx, [rbp-312]
    x86.mov [rbp-320], ecx
    x86.mov ecx, [rbp-304]
    x86.mov [rbp-328], ecx
    x86.mov ecx, [rbp-296]
    x86.mov [rbp-336], ecx
    x86.mov ecx, [rbp-288]
    x86.mov [rbp-344], ecx
    x86.lea rcx, [rbp-344]
    x86.mov rdx, rax
    x86.call IntArray.resize
    x86.mov eax, [rbp-344]
    x86.mov [rbp-288], eax
    x86.mov eax, [rbp-336]
    x86.mov [rbp-296], eax
    x86.mov eax, [rbp-328]
    x86.mov [rbp-304], eax
    x86.mov eax, [rbp-320]
    x86.mov [rbp-312], eax
    x86.mov eax, [rbp-168]
    x86.mov [rbp-352], eax
    x86.mov eax, [rbp-176]
    x86.mov [rbp-360], eax
    x86.mov eax, [rbp-184]
    x86.mov [rbp-368], eax
    x86.mov eax, [rbp-192]
    x86.mov [rbp-376], eax
    x86.mov eax, [rbp-288]
    x86.mov [rbp-384], eax
    x86.mov eax, [rbp-296]
    x86.mov [rbp-392], eax
    x86.mov eax, [rbp-304]
    x86.mov [rbp-400], eax
    x86.mov eax, [rbp-312]
    x86.mov [rbp-408], eax
    x86.mov eax, 16
    x86.mov [rbp-416], eax
    x86.mov ecx, [rbp-8]
    x86.mov [ecx+24], eax
    x86.jmp __Set_3_i64.insert.init_empty_0.merge
  init_empty_0.merge:
    x86.mov eax, 3
    x86.mov ecx, [rbp-416]
    x86.imul ecx, eax
    x86.mov edx, 4
    x86.cvtsi2sd xmm0, ecx
    x86.cvtsi2sd xmm1, edx
    x86.movsd xmm2, xmm0
    x86.divsd xmm2, xmm1
    x86.cvttsd2si ebx, xmm2
    x86.mov [rbp-424], ebx
    x86.mov esi, [rbp-80]
    x86.cmp esi, ebx
    x86.jl __Set_3_i64.insert.grow_1.merge
  grow_1:
    x86.mov eax, [rbp-96]
    x86.mov [rbp-432], eax
    x86.mov ecx, [rbp-88]
    x86.mov [rbp-440], ecx
    x86.mov edx, [rbp-80]
    x86.mov [rbp-448], edx
    x86.mov ebx, [rbp-72]
    x86.mov [rbp-456], ebx
    x86.mov esi, [rbp-64]
    x86.mov [rbp-464], esi
    x86.mov edi, [rbp-56]
    x86.mov [rbp-472], edi
    x86.mov r8, [rbp-48]
    x86.mov [rbp-480], r8
    x86.mov r9, [rbp-40]
    x86.mov [rbp-488], r9
    x86.mov eax, [rbp-32]
    x86.mov [rbp-496], eax
    x86.mov eax, [rbp-24]
    x86.mov [rbp-504], eax
    x86.mov eax, [rbp-16]
    x86.mov [rbp-512], eax
    x86.lea rax, [rbp-512]
    x86.mov rcx, rax
    x86.call __Set_3_i64.grow
    x86.mov eax, [rbp-512]
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-504]
    x86.mov [rbp-24], eax
    x86.mov eax, [rbp-496]
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-488]
    x86.mov [rbp-40], eax
    x86.mov eax, [rbp-480]
    x86.mov [rbp-48], eax
    x86.mov eax, [rbp-472]
    x86.mov [rbp-56], eax
    x86.mov eax, [rbp-464]
    x86.mov [rbp-64], eax
    x86.mov eax, [rbp-456]
    x86.mov [rbp-72], eax
    x86.mov eax, [rbp-448]
    x86.mov [rbp-80], eax
    x86.mov eax, [rbp-440]
    x86.mov [rbp-88], eax
    x86.mov eax, [rbp-432]
    x86.mov [rbp-96], eax
    x86.mov eax, [rbp-8]
    x86.xor ecx, ecx
    x86.mov edx, eax
    x86.add edx, ecx
    x86.mov ecx, [rbp-16]
    x86.mov [edx+0], ecx
    x86.mov ecx, 8
    x86.add edx, ecx
    x86.mov ecx, [rbp-24]
    x86.mov [edx+0], ecx
    x86.mov ecx, [rbp-32]
    x86.mov [edx+8], ecx
    x86.mov ecx, [rbp-40]
    x86.mov [edx+16], ecx
    x86.mov ecx, 8
    x86.mov edx, eax
    x86.add edx, ecx
    x86.mov ecx, [rbp-48]
    x86.mov [edx+0], ecx
    x86.mov ecx, 8
    x86.add edx, ecx
    x86.mov ecx, [rbp-56]
    x86.mov [edx+0], ecx
    x86.mov ecx, [rbp-64]
    x86.mov [edx+8], ecx
    x86.mov ecx, [rbp-72]
    x86.mov [edx+16], ecx
    x86.mov ecx, [rbp-80]
    x86.mov [eax+16], ecx
    x86.mov ecx, [rbp-88]
    x86.mov [eax+24], ecx
    x86.mov ecx, [rbp-96]
    x86.mov [eax+32], ecx
    x86.jmp __Set_3_i64.insert.grow_1.merge
  grow_1.merge:
    x86.mov eax, [rbp-104]
    x86.mov [rbp-520], eax
    x86.mov ecx, [rbp-416]
    x86.cqo
    x86.idiv ecx
    x86.mov [rbp-528], edx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.jge __Set_3_i64.insert.fix_negative_2.merge
  fix_negative_2:
    x86.mov eax, [rbp-528]
    x86.mov ecx, [rbp-416]
    x86.add eax, ecx
    x86.mov [rbp-528], eax
    x86.jmp __Set_3_i64.insert.fix_negative_2.merge
  fix_negative_2.merge:
    x86.mov eax, -1
    x86.mov [rbp-536], eax
    x86.xor ecx, ecx
    x86.mov [rbp-544], ecx
    x86.jmp __Set_3_i64.insert.probe_3.header
  probe_3.header:
    x86.mov eax, [rbp-544]
    x86.mov ecx, [rbp-416]
    x86.cmp eax, ecx
    x86.jge __Set_3_i64.insert.probe_3.exit
  probe_3:
    x86.mov eax, [rbp-528]
    x86.mov ecx, [rbp-408]
    x86.mov [rbp-552], ecx
    x86.mov edx, [rbp-400]
    x86.mov [rbp-560], edx
    x86.mov ebx, [rbp-392]
    x86.mov [rbp-568], ebx
    x86.mov esi, [rbp-384]
    x86.mov [rbp-576], esi
    x86.lea rdi, [rbp-576]
    x86.mov rcx, rdi
    x86.mov rdx, rax
    x86.call IntArray.get
    x86.mov [rbp-584], edx
    x86.mov r8, [rbp-576]
    x86.mov [rbp-384], r8
    x86.mov r9, [rbp-568]
    x86.mov [rbp-392], r9
    x86.mov ecx, [rbp-560]
    x86.mov [rbp-400], ecx
    x86.mov ecx, [rbp-552]
    x86.mov [rbp-408], ecx
    x86.xor ecx, ecx
    x86.mov [rbp-592], ecx
    x86.mov [rbp-600], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je __Set_3_i64.insert.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-592]
    x86.mov [rbp-600], eax
    x86.jmp __Set_3_i64.insert.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, [rbp-600]
    x86.mov [rbp-608], eax
    x86.xor ecx, ecx
    x86.cmp eax, ecx
    x86.jne __Set_3_i64.insert.empty_8.after
  empty_8:
    x86.mov eax, [rbp-528]
    x86.mov [rbp-616], eax
    x86.xor ecx, ecx
    x86.mov edx, [rbp-536]
    x86.cmp edx, ecx
    x86.jl __Set_3_i64.insert.use_deleted_9.merge
  use_deleted_9:
    x86.mov eax, [rbp-536]
    x86.mov [rbp-616], eax
    x86.jmp __Set_3_i64.insert.use_deleted_9.merge
  use_deleted_9.merge:
    x86.mov eax, [rbp-616]
    x86.mov ecx, [rbp-104]
    x86.mov edx, [rbp-376]
    x86.mov [rbp-624], edx
    x86.mov ebx, [rbp-368]
    x86.mov [rbp-632], ebx
    x86.mov esi, [rbp-360]
    x86.mov [rbp-640], esi
    x86.mov edi, [rbp-352]
    x86.mov [rbp-648], edi
    x86.lea r8, [rbp-648]
    x86.mov rdx, rax
    x86.xchg r8, rcx
    x86.xchg r8, r8
    x86.call Array.set
    x86.mov r9, [rbp-648]
    x86.mov [rbp-352], r9
    x86.mov eax, [rbp-640]
    x86.mov [rbp-360], eax
    x86.mov eax, [rbp-632]
    x86.mov [rbp-368], eax
    x86.mov eax, [rbp-624]
    x86.mov [rbp-376], eax
    x86.mov eax, [rbp-616]
    x86.mov ecx, 1
    x86.mov edx, [rbp-408]
    x86.mov [rbp-656], edx
    x86.mov edx, [rbp-400]
    x86.mov [rbp-664], edx
    x86.mov edx, [rbp-392]
    x86.mov [rbp-672], edx
    x86.mov edx, [rbp-384]
    x86.mov [rbp-680], edx
    x86.lea rdx, [rbp-680]
    x86.mov r8, rcx
    x86.mov rcx, rdx
    x86.mov rdx, rax
    x86.call IntArray.set
    x86.mov eax, [rbp-680]
    x86.mov [rbp-384], eax
    x86.mov eax, [rbp-672]
    x86.mov [rbp-392], eax
    x86.mov eax, [rbp-664]
    x86.mov [rbp-400], eax
    x86.mov eax, [rbp-656]
    x86.mov [rbp-408], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-80]
    x86.add ecx, eax
    x86.mov [rbp-688], ecx
    x86.mov eax, [rbp-8]
    x86.mov [eax+16], ecx
    x86.epilogue
    x86.ret
  empty_8.after:
    x86.mov eax, 2
    x86.mov ecx, [rbp-608]
    x86.cmp ecx, eax
    x86.sete edx
    x86.movzx edx, edxb
    x86.xor ebx, ebx
    x86.mov esi, [rbp-536]
    x86.cmp esi, ebx
    x86.setl edi
    x86.movzx edi, edib
    x86.and edx, edi
    x86.test edx, edx
    x86.je __Set_3_i64.insert.mark_deleted_10.merge
  mark_deleted_10:
    x86.mov eax, [rbp-528]
    x86.mov [rbp-536], eax
    x86.jmp __Set_3_i64.insert.mark_deleted_10.merge
  mark_deleted_10.merge:
    x86.mov eax, 1
    x86.mov ecx, [rbp-608]
    x86.cmp ecx, eax
    x86.jne __Set_3_i64.insert.check_exists_11.after
  check_exists_11:
    x86.mov eax, [rbp-528]
    x86.mov ecx, [rbp-376]
    x86.mov [rbp-696], ecx
    x86.mov edx, [rbp-368]
    x86.mov [rbp-704], edx
    x86.mov ebx, [rbp-360]
    x86.mov [rbp-712], ebx
    x86.mov esi, [rbp-352]
    x86.mov [rbp-720], esi
    x86.lea rdi, [rbp-720]
    x86.mov rcx, rdi
    x86.mov rdx, rax
    x86.call Array.get
    x86.mov [rbp-584], edx
    x86.mov r8, [rbp-720]
    x86.mov [rbp-352], r8
    x86.mov r9, [rbp-712]
    x86.mov [rbp-360], r9
    x86.mov ecx, [rbp-704]
    x86.mov [rbp-368], ecx
    x86.mov ecx, [rbp-696]
    x86.mov [rbp-376], ecx
    x86.mov [rbp-728], edx
    x86.mov [rbp-736], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.jne __Set_3_i64.insert.get_existing_14.after
  get_existing_14:
    x86.mov eax, [rbp-736]
    x86.mov [rbp-744], eax
    x86.mov ecx, [rbp-104]
    x86.cmp eax, ecx
    x86.jne __Set_3_i64.insert.exists_15.after
  exists_15:
    x86.epilogue
    x86.ret
  exists_15.after:
  get_existing_14.after:
  check_exists_11.after:
    x86.mov eax, 1
    x86.mov ecx, [rbp-528]
    x86.add ecx, eax
    x86.mov edx, [rbp-416]
    x86.mov ebx, edx
    x86.mov [rbp-816], edx
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov [rbp-528], edx
    x86.mov ebx, 1
    x86.mov esi, [rbp-544]
    x86.add esi, ebx
    x86.mov [rbp-544], esi
    x86.jmp __Set_3_i64.insert.probe_3.header
  probe_3.exit:
    x86.xor eax, eax
    x86.mov ecx, [rbp-536]
    x86.cmp ecx, eax
    x86.jl __Set_3_i64.insert.fallback_16.merge
  fallback_16:
    x86.mov eax, [rbp-536]
    x86.mov ecx, [rbp-104]
    x86.mov edx, [rbp-376]
    x86.mov [rbp-752], edx
    x86.mov ebx, [rbp-368]
    x86.mov [rbp-760], ebx
    x86.mov esi, [rbp-360]
    x86.mov [rbp-768], esi
    x86.mov edi, [rbp-352]
    x86.mov [rbp-776], edi
    x86.lea r8, [rbp-776]
    x86.mov rdx, rax
    x86.xchg r8, rcx
    x86.xchg r8, r8
    x86.call Array.set
    x86.mov r9, [rbp-776]
    x86.mov [rbp-352], r9
    x86.mov eax, [rbp-768]
    x86.mov [rbp-360], eax
    x86.mov eax, [rbp-760]
    x86.mov [rbp-368], eax
    x86.mov eax, [rbp-752]
    x86.mov [rbp-376], eax
    x86.mov eax, [rbp-536]
    x86.mov ecx, 1
    x86.mov edx, [rbp-408]
    x86.mov [rbp-784], edx
    x86.mov edx, [rbp-400]
    x86.mov [rbp-792], edx
    x86.mov edx, [rbp-392]
    x86.mov [rbp-800], edx
    x86.mov edx, [rbp-384]
    x86.mov [rbp-808], edx
    x86.lea rdx, [rbp-808]
    x86.mov r8, rcx
    x86.mov rcx, rdx
    x86.mov rdx, rax
    x86.call IntArray.set
    x86.mov eax, [rbp-808]
    x86.mov [rbp-384], eax
    x86.mov eax, [rbp-800]
    x86.mov [rbp-392], eax
    x86.mov eax, [rbp-792]
    x86.mov [rbp-400], eax
    x86.mov eax, [rbp-784]
    x86.mov [rbp-408], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-688]
    x86.add ecx, eax
    x86.mov [rbp-688], ecx
    x86.mov eax, [rbp-8]
    x86.mov [eax+16], ecx
    x86.jmp __Set_3_i64.insert.fallback_16.merge
  fallback_16.merge:
    x86.epilogue
    x86.ret
  }
  func @__Set_3_i64.count(__self_ptr: i64) -> i64 {
  entry:
    x86.prologue stack_size=96
    x86.mov [rbp-8], ecx
    x86.xor eax, eax
    x86.mov edx, ecx
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [edx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [edx+16]
    x86.mov [rbp-40], eax
    x86.mov eax, 8
    x86.mov edx, ecx
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-48], eax
    x86.mov eax, 8
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-56], eax
    x86.mov eax, [edx+8]
    x86.mov [rbp-64], eax
    x86.mov eax, [edx+16]
    x86.mov [rbp-72], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-80], eax
    x86.mov eax, [ecx+24]
    x86.mov [rbp-88], eax
    x86.mov eax, [ecx+32]
    x86.mov [rbp-96], eax
    x86.mov ecx, [rbp-80]
    x86.mov edx, [rbp-88]
    x86.mov ebx, [rbp-96]
    x86.mov eax, ecx
    x86.epilogue
    x86.ret
  }
  func @__Set_3_i64.grow(__self_ptr: i64) {
  entry:
    x86.prologue stack_size=816
    x86.mov [rbp-8], ecx
    x86.xor eax, eax
    x86.mov edx, ecx
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-16], eax
    x86.mov eax, 8
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-24], eax
    x86.mov eax, [edx+8]
    x86.mov [rbp-32], eax
    x86.mov eax, [edx+16]
    x86.mov [rbp-40], eax
    x86.mov eax, 8
    x86.mov edx, ecx
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-48], eax
    x86.mov eax, 8
    x86.add edx, eax
    x86.mov eax, [edx+0]
    x86.mov [rbp-56], eax
    x86.mov eax, [edx+8]
    x86.mov [rbp-64], eax
    x86.mov eax, [edx+16]
    x86.mov [rbp-72], eax
    x86.mov eax, [ecx+16]
    x86.mov [rbp-80], eax
    x86.mov eax, [ecx+24]
    x86.mov [rbp-88], eax
    x86.mov eax, [ecx+32]
    x86.mov [rbp-96], eax
    x86.mov ecx, [rbp-80]
    x86.mov edx, [rbp-88]
    x86.mov ebx, [rbp-96]
    x86.mov [rbp-104], edx
    x86.mov esi, 2
    x86.imul edx, esi
    x86.mov [rbp-112], edx
    x86.xor edi, edi
    x86.cmp edx, edi
    x86.jne __Set_3_i64.grow.handle_zero_0.merge
  handle_zero_0:
    x86.mov eax, 16
    x86.mov [rbp-112], eax
    x86.jmp __Set_3_i64.grow.handle_zero_0.merge
  handle_zero_0.merge:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-120], eax
    x86.mov ecx, [rbp-24]
    x86.mov [rbp-128], ecx
    x86.mov edx, [rbp-32]
    x86.mov [rbp-136], edx
    x86.mov ebx, [rbp-40]
    x86.mov [rbp-144], ebx
    x86.mov esi, [rbp-48]
    x86.mov [rbp-152], esi
    x86.mov edi, [rbp-56]
    x86.mov [rbp-160], edi
    x86.mov r8, [rbp-64]
    x86.mov [rbp-168], r8
    x86.mov r9, [rbp-72]
    x86.mov [rbp-176], r9
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.mov [rbp-184], ecx
    x86.mov [rbp-192], edx
    x86.mov [rbp-200], ebx
    x86.mov [rbp-208], eax
    x86.mov eax, [rbp-184]
    x86.mov [rbp-216], eax
    x86.mov eax, [rbp-192]
    x86.mov [rbp-224], eax
    x86.mov eax, [rbp-200]
    x86.mov [rbp-232], eax
    x86.mov eax, [rbp-208]
    x86.mov [rbp-240], eax
    x86.mov eax, [rbp-216]
    x86.mov [rbp-248], eax
    x86.mov eax, [rbp-224]
    x86.mov [rbp-256], eax
    x86.mov eax, [rbp-232]
    x86.mov [rbp-264], eax
    x86.mov eax, [rbp-112]
    x86.mov ecx, [rbp-264]
    x86.mov [rbp-272], ecx
    x86.mov ecx, [rbp-256]
    x86.mov [rbp-280], ecx
    x86.mov ecx, [rbp-248]
    x86.mov [rbp-288], ecx
    x86.mov ecx, [rbp-240]
    x86.mov [rbp-296], ecx
    x86.lea rcx, [rbp-296]
    x86.mov rdx, rax
    x86.call Array.resize
    x86.mov eax, [rbp-296]
    x86.mov [rbp-240], eax
    x86.mov eax, [rbp-288]
    x86.mov [rbp-248], eax
    x86.mov eax, [rbp-280]
    x86.mov [rbp-256], eax
    x86.mov eax, [rbp-272]
    x86.mov [rbp-264], eax
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.mov [rbp-304], ecx
    x86.mov [rbp-312], edx
    x86.mov [rbp-320], ebx
    x86.mov [rbp-328], eax
    x86.mov eax, [rbp-304]
    x86.mov [rbp-336], eax
    x86.mov eax, [rbp-312]
    x86.mov [rbp-344], eax
    x86.mov eax, [rbp-320]
    x86.mov [rbp-352], eax
    x86.mov eax, [rbp-328]
    x86.mov [rbp-360], eax
    x86.mov eax, [rbp-336]
    x86.mov [rbp-368], eax
    x86.mov eax, [rbp-344]
    x86.mov [rbp-376], eax
    x86.mov eax, [rbp-352]
    x86.mov [rbp-384], eax
    x86.mov eax, [rbp-112]
    x86.mov ecx, [rbp-384]
    x86.mov [rbp-392], ecx
    x86.mov ecx, [rbp-376]
    x86.mov [rbp-400], ecx
    x86.mov ecx, [rbp-368]
    x86.mov [rbp-408], ecx
    x86.mov ecx, [rbp-360]
    x86.mov [rbp-416], ecx
    x86.lea rcx, [rbp-416]
    x86.mov rdx, rax
    x86.call IntArray.resize
    x86.mov eax, [rbp-416]
    x86.mov [rbp-360], eax
    x86.mov eax, [rbp-408]
    x86.mov [rbp-368], eax
    x86.mov eax, [rbp-400]
    x86.mov [rbp-376], eax
    x86.mov eax, [rbp-392]
    x86.mov [rbp-384], eax
    x86.mov eax, [rbp-240]
    x86.mov [rbp-424], eax
    x86.mov eax, [rbp-248]
    x86.mov [rbp-432], eax
    x86.mov eax, [rbp-256]
    x86.mov [rbp-440], eax
    x86.mov eax, [rbp-264]
    x86.mov [rbp-448], eax
    x86.mov eax, [rbp-360]
    x86.mov [rbp-456], eax
    x86.mov eax, [rbp-368]
    x86.mov [rbp-464], eax
    x86.mov eax, [rbp-376]
    x86.mov [rbp-472], eax
    x86.mov eax, [rbp-384]
    x86.mov [rbp-480], eax
    x86.mov eax, [rbp-112]
    x86.mov [rbp-488], eax
    x86.mov ecx, [rbp-8]
    x86.mov [ecx+24], eax
    x86.xor eax, eax
    x86.mov [rbp-496], eax
    x86.mov ecx, [rbp-8]
    x86.mov [ecx+16], eax
    x86.xor eax, eax
    x86.mov [rbp-504], eax
    x86.jmp __Set_3_i64.grow.rehash_1.header
  rehash_1.header:
    x86.mov eax, [rbp-504]
    x86.mov ecx, [rbp-104]
    x86.cmp eax, ecx
    x86.jge __Set_3_i64.grow.rehash_1.exit
  rehash_1:
    x86.mov eax, [rbp-504]
    x86.mov ecx, [rbp-176]
    x86.mov [rbp-512], ecx
    x86.mov edx, [rbp-168]
    x86.mov [rbp-520], edx
    x86.mov ebx, [rbp-160]
    x86.mov [rbp-528], ebx
    x86.mov esi, [rbp-152]
    x86.mov [rbp-536], esi
    x86.lea rdi, [rbp-536]
    x86.mov rcx, rdi
    x86.mov rdx, rax
    x86.call IntArray.get
    x86.mov [rbp-544], edx
    x86.mov r8, [rbp-536]
    x86.mov [rbp-152], r8
    x86.mov r9, [rbp-528]
    x86.mov [rbp-160], r9
    x86.mov ecx, [rbp-520]
    x86.mov [rbp-168], ecx
    x86.mov ecx, [rbp-512]
    x86.mov [rbp-176], ecx
    x86.xor ecx, ecx
    x86.mov [rbp-552], ecx
    x86.mov [rbp-560], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je __Set_3_i64.grow.otherwise_default_continue_5
  otherwise_default_error_4:
    x86.mov eax, [rbp-552]
    x86.mov [rbp-560], eax
    x86.jmp __Set_3_i64.grow.otherwise_default_continue_5
  otherwise_default_continue_5:
    x86.mov eax, [rbp-560]
    x86.mov [rbp-568], eax
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne __Set_3_i64.grow.occupied_6.after
  occupied_6:
    x86.mov eax, [rbp-504]
    x86.mov ecx, [rbp-144]
    x86.mov [rbp-576], ecx
    x86.mov edx, [rbp-136]
    x86.mov [rbp-584], edx
    x86.mov ebx, [rbp-128]
    x86.mov [rbp-592], ebx
    x86.mov esi, [rbp-120]
    x86.mov [rbp-600], esi
    x86.lea rdi, [rbp-600]
    x86.mov rcx, rdi
    x86.mov rdx, rax
    x86.call Array.get
    x86.mov [rbp-544], edx
    x86.mov r8, [rbp-600]
    x86.mov [rbp-120], r8
    x86.mov r9, [rbp-592]
    x86.mov [rbp-128], r9
    x86.mov ecx, [rbp-584]
    x86.mov [rbp-136], ecx
    x86.mov ecx, [rbp-576]
    x86.mov [rbp-144], ecx
    x86.mov [rbp-608], edx
    x86.mov [rbp-616], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.jne __Set_3_i64.grow.get_elem_9.after
  get_elem_9:
    x86.mov eax, [rbp-616]
    x86.mov [rbp-624], eax
    x86.mov [rbp-632], eax
    x86.mov ecx, [rbp-112]
    x86.cqo
    x86.idiv ecx
    x86.mov [rbp-640], edx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.jge __Set_3_i64.grow.fix_negative_10.merge
  fix_negative_10:
    x86.mov eax, [rbp-640]
    x86.mov ecx, [rbp-112]
    x86.add eax, ecx
    x86.mov [rbp-640], eax
    x86.jmp __Set_3_i64.grow.fix_negative_10.merge
  fix_negative_10.merge:
    x86.mov eax, [rbp-640]
    x86.mov ecx, [rbp-480]
    x86.mov [rbp-648], ecx
    x86.mov edx, [rbp-472]
    x86.mov [rbp-656], edx
    x86.mov ebx, [rbp-464]
    x86.mov [rbp-664], ebx
    x86.mov esi, [rbp-456]
    x86.mov [rbp-672], esi
    x86.lea rdi, [rbp-672]
    x86.mov rcx, rdi
    x86.mov rdx, rax
    x86.call IntArray.get
    x86.mov [rbp-544], edx
    x86.mov r8, [rbp-672]
    x86.mov [rbp-456], r8
    x86.mov r9, [rbp-664]
    x86.mov [rbp-464], r9
    x86.mov ecx, [rbp-656]
    x86.mov [rbp-472], ecx
    x86.mov ecx, [rbp-648]
    x86.mov [rbp-480], ecx
    x86.xor ecx, ecx
    x86.mov [rbp-680], ecx
    x86.mov [rbp-688], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je __Set_3_i64.grow.otherwise_default_continue_14
  otherwise_default_error_13:
    x86.mov eax, [rbp-680]
    x86.mov [rbp-688], eax
    x86.jmp __Set_3_i64.grow.otherwise_default_continue_14
  otherwise_default_continue_14:
    x86.mov eax, [rbp-688]
    x86.mov [rbp-696], eax
    x86.jmp __Set_3_i64.grow.find_slot_15.header
  find_slot_15.header:
    x86.xor eax, eax
    x86.mov ecx, [rbp-696]
    x86.cmp ecx, eax
    x86.je __Set_3_i64.grow.find_slot_15.exit
  find_slot_15:
    x86.mov eax, 1
    x86.mov ecx, [rbp-640]
    x86.add ecx, eax
    x86.mov edx, [rbp-112]
    x86.mov ebx, edx
    x86.mov [rbp-816], edx
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov [rbp-640], edx
    x86.mov ebx, [rbp-480]
    x86.mov [rbp-704], ebx
    x86.mov esi, [rbp-472]
    x86.mov [rbp-712], esi
    x86.mov edi, [rbp-464]
    x86.mov [rbp-720], edi
    x86.mov r8, [rbp-456]
    x86.mov [rbp-728], r8
    x86.lea r9, [rbp-728]
    x86.mov rcx, r9
    x86.call IntArray.get
    x86.mov [rbp-544], edx
    x86.mov ecx, [rbp-728]
    x86.mov [rbp-456], ecx
    x86.mov ecx, [rbp-720]
    x86.mov [rbp-464], ecx
    x86.mov ecx, [rbp-712]
    x86.mov [rbp-472], ecx
    x86.mov ecx, [rbp-704]
    x86.mov [rbp-480], ecx
    x86.xor ecx, ecx
    x86.mov [rbp-736], ecx
    x86.mov [rbp-744], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je __Set_3_i64.grow.otherwise_default_continue_19
  otherwise_default_error_18:
    x86.mov eax, [rbp-736]
    x86.mov [rbp-744], eax
    x86.jmp __Set_3_i64.grow.otherwise_default_continue_19
  otherwise_default_continue_19:
    x86.mov eax, [rbp-744]
    x86.mov [rbp-696], eax
    x86.jmp __Set_3_i64.grow.find_slot_15.header
  find_slot_15.exit:
    x86.mov eax, [rbp-640]
    x86.mov ecx, [rbp-624]
    x86.mov edx, [rbp-448]
    x86.mov [rbp-752], edx
    x86.mov ebx, [rbp-440]
    x86.mov [rbp-760], ebx
    x86.mov esi, [rbp-432]
    x86.mov [rbp-768], esi
    x86.mov edi, [rbp-424]
    x86.mov [rbp-776], edi
    x86.lea r8, [rbp-776]
    x86.mov rdx, rax
    x86.xchg r8, rcx
    x86.xchg r8, r8
    x86.call Array.set
    x86.mov r9, [rbp-776]
    x86.mov [rbp-424], r9
    x86.mov eax, [rbp-768]
    x86.mov [rbp-432], eax
    x86.mov eax, [rbp-760]
    x86.mov [rbp-440], eax
    x86.mov eax, [rbp-752]
    x86.mov [rbp-448], eax
    x86.mov eax, [rbp-640]
    x86.mov ecx, 1
    x86.mov edx, [rbp-480]
    x86.mov [rbp-784], edx
    x86.mov edx, [rbp-472]
    x86.mov [rbp-792], edx
    x86.mov edx, [rbp-464]
    x86.mov [rbp-800], edx
    x86.mov edx, [rbp-456]
    x86.mov [rbp-808], edx
    x86.lea rdx, [rbp-808]
    x86.mov r8, rcx
    x86.mov rcx, rdx
    x86.mov rdx, rax
    x86.call IntArray.set
    x86.mov eax, [rbp-808]
    x86.mov [rbp-456], eax
    x86.mov eax, [rbp-800]
    x86.mov [rbp-464], eax
    x86.mov eax, [rbp-792]
    x86.mov [rbp-472], eax
    x86.mov eax, [rbp-784]
    x86.mov [rbp-480], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-496]
    x86.add ecx, eax
    x86.mov [rbp-496], ecx
    x86.mov eax, [rbp-8]
    x86.mov [eax+16], ecx
  get_elem_9.after:
  occupied_6.after:
    x86.mov eax, 1
    x86.mov ecx, [rbp-504]
    x86.add ecx, eax
    x86.mov [rbp-504], ecx
    x86.jmp __Set_3_i64.grow.rehash_1.header
  rehash_1.exit:
    x86.epilogue
    x86.ret
  }
}
