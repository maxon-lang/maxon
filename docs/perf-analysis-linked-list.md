# Performance Analysis: Maxon List vs C Doubly Linked List

Date: 2026-02-28

## Overview

This document compares the compiled output of a simple doubly-linked list program written in Maxon vs C. Both programs create a list, append three integers (10, 20, 30), iterate to sum them, and return the result (60) as the exit code.

## Test Programs

### Maxon (`temp/list_bench.maxon`)

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
  var list = IntList{}

  list.append(10)
  list.append(20)
  list.append(30)

  var sum = 0
  for item in list 'loop'
    sum = sum + item
  end 'loop'

  return sum
end 'main'
```

### C (`temp/list_bench.c`)

```c
#include <stdlib.h>

typedef struct Node {
    struct Node *prev;
    struct Node *next;
    long long value;
} Node;

typedef struct {
    Node *head;
    Node *tail;
    int count;
} List;

void list_init(List *list) {
    list->head = NULL;
    list->tail = NULL;
    list->count = 0;
}

void list_append(List *list, long long value) {
    Node *node = (Node *)malloc(sizeof(Node));
    node->prev = list->tail;
    node->next = NULL;
    node->value = value;
    if (list->tail) {
        list->tail->next = node;
    } else {
        list->head = node;
    }
    list->tail = node;
    list->count++;
}

void list_free(List *list) {
    Node *cur = list->head;
    while (cur) {
        Node *next = cur->next;
        free(cur);
        cur = next;
    }
}

int main(void) {
    List list;
    list_init(&list);

    list_append(&list, 10);
    list_append(&list, 20);
    list_append(&list, 30);

    long long sum = 0;
    Node *cur = list.head;
    while (cur) {
        sum += cur->value;
        cur = cur->next;
    }

    list_free(&list);
    return (int)sum;
}
```

## Build Commands

### Maxon

```bash
# Compile
./maxon-sharp/bin/Debug/net8.0/win-x64/maxon.exe compile temp/list_bench.maxon

# Compile with IR dumps at each pipeline stage
./maxon-sharp/bin/Debug/net8.0/win-x64/maxon.exe compile temp/list_bench.maxon --dump-stages
# Produces: temp/list_bench.1-maxon.mlir, temp/list_bench.2-standard.mlir, temp/list_bench.3-x86.mlir

# Run
./maxon-sharp/bin/Debug/net8.0/win-x64/maxon.exe run temp/list_bench.maxon

# Disassemble the PE executable
./llvm-project/bin/llvm-objdump.exe -d temp/list_bench.exe > temp/list_bench_disasm.txt
```

### C (MSVC)

Create `temp/compile_c.bat`:
```bat
@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
cd /d "C:\Users\Eric\Dev\maxon\temp"
cl /O2 /FA /Falist_bench_c.asm list_bench.c /link /OUT:list_bench_c.exe
```

```bash
# Compile (must use batch file because git bash mangles /O2 flags into paths)
cmd //c "C:\\Users\\Eric\\Dev\\maxon\\temp\\compile_c.bat"
# Produces: temp/list_bench_c.exe (executable) and temp/list_bench_c.asm (assembly listing)
```

## Executable Comparison

| Property | Maxon | C (MSVC /O2) |
|----------|-------|-------------|
| Total file size | 15,872 bytes | 108,032 bytes |
| .text section size | 0x3126 (12,582 bytes) | 0xE110 (57,616 bytes) |
| Code bytes (from compiler) | 12,582 code + 1,283 symdata | n/a |
| Imports | 27 | n/a |
| Runtime dependencies | None (self-contained runtime) | CRT (LIBCMT) |

Maxon's .text is smaller because its runtime (`mm_alloc`, `mm_incref`, etc.) is emitted directly into the .text section — there's no external runtime library. The C executable is larger because it links the entire CRT statically.

## Data Structures

### C Node Layout (24 bytes)

```
offset 0:  prev   (8 bytes, pointer)
offset 8:  next   (8 bytes, pointer)
offset 16: value  (8 bytes, i64)
```

### Maxon `__ChainNode` Layout (32 bytes)

```
offset 0:  next   (8 bytes, pointer)
offset 8:  prev   (8 bytes, pointer)
offset 16: chain  (8 bytes, back-pointer to owning chain — enables auto-detach)
offset 24: value  (8 bytes, i64)
```

Plus an 8-byte ref-count header prepended by `mm_alloc_in`, making the actual heap allocation 40 bytes per node.

### C List Layout (20 bytes, stack-allocated)

```
offset 0:  head   (8 bytes, pointer)
offset 8:  tail   (8 bytes, pointer)
offset 16: count  (4 bytes, int)
```

### Maxon `__Chain` Layout (24 bytes, heap-allocated + ref-counted)

```
offset 0:  head   (8 bytes, pointer)
offset 8:  tail   (8 bytes, pointer)
offset 16: count  (8 bytes, i64)
```

### Maxon `IntList` Layout (16 bytes, heap-allocated + ref-counted)

```
offset 0:  chain  (8 bytes, pointer to __Chain)
offset 8:  iterIndex (8 bytes, i64 — current iteration position)
```

## Compiled Output Analysis

### `list_append`

**C (MSVC /O2)** — fully inlined into `main`, ~10 instructions per append:

```asm
mov  ecx, 24              ; sizeof(Node)
call malloc                ; allocate
mov  [rax], rsi            ; node->prev = tail
mov  [rax+8], 0            ; node->next = NULL
mov  [rax+16], value       ; node->value
; branch: update old_tail->next or set head
inc  DWORD PTR [rdi+16]    ; count++
mov  [rdi+8], rax          ; tail = node
```

1 runtime call (malloc). All pointer linkage is inline.

**Maxon `IntList.append`** — separate function, 14 instructions + call into runtime:

```
prologue stack_size=32
mov  [rbp-8], ecx          ; save self_ptr
mov  ebx, [eax+0]          ; load chain ptr from List wrapper
mov  rcx, 32               ; sizeof(__ChainNode)
call mm_alloc_in            ; managed alloc in chain's scope
; zero out next/prev/chain fields
mov  [eax+24], edx          ; node.value = value
call maxon_chain_insert_last ; link into chain (~25 more instructions)
epilogue + ret
```

`maxon_chain_insert_last` is a hand-written runtime function (~25 instructions):

```
; auto-detach: if node.chain != 0, call maxon_chain_unlink first
mov  rax, [node+16]        ; node.chain
test rax, rax
jz   no_detach
call maxon_chain_unlink    ; potential 3rd runtime call
no_detach:
; old_tail = chain.tail
; node.next = 0
; node.prev = old_tail
; node.chain = chain_ptr
; if old_tail != 0: old_tail.next = node
; chain.tail = node
; if chain.head == 0: chain.head = node
; chain.count++
```

Total for Maxon append: 2-3 runtime calls (`mm_alloc_in` + `maxon_chain_insert_last`, potentially + `maxon_chain_unlink`) and ~40 instructions. The auto-detach check is a safety feature that has no C equivalent — it allows nodes to be safely moved between chains.

### Iteration Loop

**C (MSVC /O2)** — 4 instructions, 0 calls:

```asm
$LL2@main:
  add  rsi, [rax+16]     ; sum += cur->value
  mov  rax, [rax+8]      ; cur = cur->next
  test rax, rax
  jne  $LL2@main
```

**Maxon** — the main loop body (in `list_bench.main`) is:

```
loop_0.header:
  mov  rcx, [rbp-40]
  call IntList.next         ; ~180 instructions inside
  ; check error flag
  cmp  edx, ecx
  jne  loop_0.exit
loop_0:
  mov  eax, [rbp-48]       ; item (returned from next())
  add  ecx, eax            ; sum += item
  mov  [rbp-32], ecx
  jmp  loop_0.header
```

`IntList.next()` (180 instructions, ~10 runtime calls per invocation):

1. `mm_scope_enter`
2. Check `iterIndex >= chain.count` — if exhausted, `mm_scope_exit` + return error
3. Get head node via `try_call @__chain_head` + `mm_incref` on result
4. **Walk loop** — advance `iterIndex` steps from the head each time. Per step:
   - `mm_scope_enter`
   - `try_call @__chain_node_next`
   - `mm_incref` (new node)
   - `mm_decref` (old node)
   - `mm_move`
   - `mm_scope_exit`
5. Increment `iterIndex`
6. Extract `node.value` at offset 24
7. `mm_decref` x2 + `mm_scope_exit`
8. Return value

**This makes iteration O(n²)** — it re-walks from head on every call:

| Call | C cost | Maxon cost |
|------|--------|------------|
| 1st item | 1 pointer load | head + 0 walk steps |
| 2nd item | 1 pointer load | head + 1 walk step |
| 3rd item | 1 pointer load | head + 2 walk steps |
| nth item | 1 pointer load | head + (n-1) walk steps |

## Summary

| Metric | C (MSVC /O2) | Maxon |
|--------|-------------|-------|
| Iteration complexity | O(n) | O(n²) |
| Loop body | 4 instructions, 0 calls | ~180 instructions, ~10 calls |
| Append cost | ~10 inlined instructions, 1 call | ~40 instructions, 2-3 calls |
| Node heap size | 24 bytes | 40 bytes (32 data + 8 refcount header) |
| List struct | 20 bytes, stack-allocated | 16+24 = 40 bytes across 2 heap objects |
| Inlining | Full (append inlined into main) | None |
| Memory management | Manual (malloc/free) | Automatic (ref-counted, scoped) |

## Actionable Improvements

### High impact

1. **Cache the iterator cursor** — store the current `__ChainNode*` in the List struct instead of re-walking from head using `iterIndex`. Fixes O(n²) → O(n) and eliminates the walk loop plus all its per-step ref-counting calls.

### Medium impact

2. **Inlining pass** — small leaf functions like `IntList.append` and `maxon_chain_insert_last` could be inlined at the MLIR level.
3. **Elide ref-counting for non-escaping values** — the iterator's temporary node pointers don't escape the function; their incref/decref pairs are provably unnecessary.

### Lower priority

4. **Eliminate scope_enter/scope_exit in the iteration loop body** — the for-in body in this program doesn't allocate, so the inner scope operations are no-ops.
5. **Stack-allocate small structs** — the List and Chain headers could live on the stack when they don't escape, avoiding 2 heap allocations on creation.
