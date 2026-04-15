# Performance Analysis: Maxon List vs C Doubly Linked List

Date: 2026-02-28 (updated after cursor-based iterator fix)

## Overview

This document compares the compiled output of a simple doubly-linked list program written in Maxon vs C. Both programs create a list, append three integers (10, 20, 30), iterate to sum them, and return the result (60) as the exit code.

## Test Programs

### Maxon (`temp/list_bench.maxon`)

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()

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
maxon build temp/list_bench.maxon

# Compile with IR dumps at each pipeline stage
maxon build temp/list_bench.maxon --dump-stages
# Produces: temp/list_bench.1-maxon.ir, temp/list_bench.2-standard.ir, temp/list_bench.3-x86.ir

# Run
maxon run temp/list_bench.maxon

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
| .text section size | 0x3035 (12,341 bytes) | 0xE110 (57,616 bytes) |
| Code bytes (from compiler) | 12,341 code + 1,314 symdata | n/a |
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

### Maxon `__ManagedListNode` Layout (32 bytes data + 8 bytes ref-count header = 40 bytes heap)

```
offset 0:  next   (8 bytes, pointer)
offset 8:  prev   (8 bytes, pointer)
offset 16: managedList  (8 bytes, back-pointer to owning managed list — enables auto-detach)
offset 24: value  (8 bytes, i64)
```

### C List Layout (20 bytes, stack-allocated)

```
offset 0:  head   (8 bytes, pointer)
offset 8:  tail   (8 bytes, pointer)
offset 16: count  (4 bytes, int)
```

### Maxon `__ManagedList` Layout (24 bytes data + 8 bytes ref-count header + cursor)

```
offset 0:  head    (8 bytes, pointer)
offset 8:  tail    (8 bytes, pointer)
offset 16: count   (8 bytes, i64)
offset 24: cursor  (8 bytes, pointer — current iteration node)
```

### Maxon `IntList` Layout (16 bytes data + 8 bytes ref-count header)

```
offset 0:  managedList  (8 bytes, pointer to __ManagedList)
offset 8:  iterIndex  (8 bytes, i64 — current iteration position)
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

**Maxon `IntList.append`** — separate function (25 instructions + 2 calls):

```
prologue stack_size=32
mov  [rbp-8], ecx          ; save self_ptr
mov  ebx, [eax+0]          ; load managed list ptr from List wrapper
mov  rcx, 32               ; sizeof(__ManagedListNode)
call mm_alloc_in            ; managed alloc in managed list's scope
; zero out next/prev/managedList fields (3 mov instructions)
mov  [eax+24], edx          ; node.value = value
call maxon_managed_list_insert_last ; link into managed list
epilogue + ret
```

`maxon_managed_list_insert_last` is a hand-written runtime function (~17 instructions on happy path):

```
; auto-detach check: if node.managedList != 0, call maxon_managed_list_unlink first
mov  rax, [node+16]        ; node.managedList
test rax, rax
jz   no_detach              ; always taken for fresh nodes
no_detach:
mov  rax, [managedList+8]   ; old_tail = managedList.tail
mov  [node+0], 0            ; node.next = 0
mov  [node+8], old_tail     ; node.prev = old_tail
mov  [node+16], managedList_ptr   ; node.managedList = managedList_ptr
test rcx, rcx               ; if old_tail != 0
mov  [old_tail+0], node     ;   old_tail.next = node
mov  [managedList+8], node  ; managedList.tail = node
cmp  [managedList+0], 0     ; if managedList.head == 0
mov  [managedList+0], node  ;   managedList.head = node (first append only)
inc  [managedList+16]       ; managedList.count++
```

Total per append: `mm_alloc_in` + `IntList.append` body + `maxon_managed_list_insert_last` = ~42 instructions + prologue/epilogue overhead.

### Iteration Loop

**C (MSVC /O2)** — 4 instructions per iteration, 0 calls:

```asm
$LL2@main:
  add  rsi, [rax+16]     ; sum += cur->value
  mov  rax, [rax+8]      ; cur = cur->next
  test rax, rax
  jne  $LL2@main
```

**Maxon** — the main loop calls `IntList.next()` which now uses a cursor (no re-walking):

Loop driver in `main` (8 instructions per iteration):
```
loop_0.header:
  mov  eax, [rbp-40]       ; load iterator ref
  mov  rcx, [rbp-40]
  call IntList.next         ; get next value
  mov  [rbp-48], eax        ; save result
  cmp  edx, ecx             ; check error flag
  jne  loop_0.exit          ; exit if exhausted
loop_0:
  mov  eax, [rbp-48]        ; item
  add  ecx, [rbp-32]        ; sum += item (2 instructions due to no add-from-mem)
  mov  [rbp-32], ecx
  jmp  loop_0.header
```

`IntList.next()` — now **0 runtime calls**, pure register/memory work:

First call (iterIndex == 0) takes the `first_1` path (~42 instructions):
```
; check iterIndex >= count → exhausted
; cursorStart: managedList.cursor = managedList.head
; load cursor.value
; iterIndex++
; cursorAdvance: managedList.cursor = cursor.next
; return value
```

Subsequent calls take the `first_1.merge` fast path (~22 instructions):
```
; check iterIndex >= count → exhausted (10 instructions)
; check iterIndex != 0 → skip cursorStart (4 instructions)
; load managedList.cursor → cursor.value (5 instructions through 3 pointer chases)
; iterIndex++ (5 instructions)
; cursorAdvance: cursor = cursor.next (conditional, ~10 instructions when not last)
; return value (3 instructions)
```

Final call (exhausted) takes the `done_0` early-exit path (~8 instructions).

### Estimated Instruction Counts (Full Program Execution)

**C (Maxon code only, not counting malloc/free/CRT):**

| Phase | Instructions |
|-------|-------------|
| list_init (inlined) | 3 |
| 3x list_append (inlined) | ~30 |
| Iteration (3 items) | 4 x 3 + 2 (setup+final test) = 14 |
| list_free (3 nodes) | ~12 |
| main overhead | ~5 |
| **Total (app code)** | **~64** |

**Maxon (user-written code only, not counting mm_* runtime):**

| Phase | Instructions |
|-------|-------------|
| main setup (alloc managed list+list, zero-init, mm calls) | ~40 |
| 3x IntList.append body (excl mm_alloc_in) | 3 x 25 = 75 |
| 3x maxon_managed_list_insert_last | 3 x 17 = 51 |
| createIterator | 7 (incl prologue/epilogue) |
| IntList.next (1st call, iterIndex==0 path) | ~42 |
| IntList.next (2nd call, fast path) | ~22 |
| IntList.next (3rd call, fast path) | ~22 |
| IntList.next (4th call, exhausted) | ~8 |
| main cleanup (range check, decref, scope_exit) | ~25 |
| **Total (app code)** | **~292** |

**Maxon runtime calls made:**

| Runtime function | Call count | Purpose |
|-----------------|-----------|---------|
| mm_scope_enter | 1 | main scope |
| mm_alloc | 2 | managed list + list struct |
| mm_move | 1 | ownership transfer |
| mm_incref | 2 | list ref + iterator ref |
| mm_alloc_in | 3 | 3 managed list nodes |
| maxon_managed_list_insert_last | 3 | link nodes |
| mm_decref | 2 | list + iterator cleanup |
| mm_scope_exit | 1 | main scope |
| **Total runtime calls** | **15** | |

C makes 3 calls to `malloc` + 3 calls to `free` = 6 runtime calls.

## Summary

| Metric | C (MSVC /O2) | Maxon |
|--------|-------------|-------|
| Iteration complexity | O(n) | O(n) |
| Iteration per-step | 4 instructions, 0 calls | ~30 instructions, 1 call (non-inlined IntList.next) |
| Append per-call | ~10 inlined instructions, 1 call | ~42 instructions, 2 calls (non-inlined) |
| Total app instructions | ~64 | ~292 (~4.6x) |
| Total runtime calls | 6 (malloc+free) | 15 (mm_*) |
| Node heap size | 24 bytes | 40 bytes (32 data + 8 refcount header) |
| List struct | 20 bytes, stack-allocated | 24+16 = 40 bytes across 2 heap objects + ref-count headers |
| Inlining | Full (all functions inlined into main) | None |
| Memory management | Manual (malloc/free) | Automatic (ref-counted, scoped) |
| Safety features | None | Auto-detach, bounds checking, exhaustion detection |

## What Maxon Gets in Exchange

The ~4.6x instruction count overhead buys:
- **Automatic memory management** — no manual free, no use-after-free, no leaks
- **Safe iteration** — exhaustion detected and reported as a typed error
- **ExitCode range checking** — runtime panic if return value overflows u32
- **Auto-detach** — nodes can be safely moved between chains without manual unlink
- **Scoped cleanup** — all allocations freed automatically on scope exit

## Remaining Improvements

### High impact

1. **Inlining pass** — `IntList.append` (25 instructions) and `IntList.next` (~22 on fast path) are small enough to inline. This would eliminate call/ret overhead and enable register allocation across the inlined code.

### Medium impact

2. **Elide redundant loads** — `IntList.next` reloads `[rbp-8]` (self_ptr) repeatedly. Keeping it in a register across the function would save ~5 instructions.
3. **Elide ref-counting for non-escaping values** — the iterator ref's incref/decref pair is provably unnecessary since it doesn't escape main.

### Lower priority

4. **Stack-allocate small structs** — the List and ManagedList headers could live on the stack when they don't escape, avoiding 2 heap allocations + ref-count overhead.
5. **Combine the managed-list+list into one allocation** — the List wrapper is just a pointer to managed list + an iterIndex. Flattening would eliminate one heap object and one indirection.
