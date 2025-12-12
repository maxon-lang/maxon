# Microarchitecture Optimization Plan

## Overview

Add target-aware optimizations that adapt code generation to specific CPU microarchitectures. LLVM does this extensively (see `X86FixupLEAs.cpp`, `X86OptimizeLEAs.cpp`).

## Phase 1: Infrastructure (Target CPU Model)

### 1.1 Create CPU Feature Model

**File:** `maxon-bin/backend/x86_target.h/.cpp`

```cpp
type X86Target {
    enum class Microarch { Generic, Haswell, Skylake, Zen3, Zen4 };
    
    // LEA characteristics
    bool hasSlowLEA;           // Simple LEA slower than ADD (Atom)
    bool hasSlow3OpLEA;        // 3-operand LEA is 3 cycles (Sandy Bridge+)
    int simpleLeaLatency;      // 1 for most, 3 for Atom
    int complexLeaLatency;     // 1 for old, 3 for SNB+
    
    // Other characteristics for future use
    bool hasFastBMI2;          // MULX, SHRX, SHLX
    bool hasFastLZCNT;
    int moveCost;              // Register-register move cost
};
```

### 1.2 Add CLI Flag

- `--target-cpu=<name>` or `-march=<name>`
- Default to `generic` (conservative assumptions)
- Options: `generic`, `haswell`, `skylake`, `zen3`, `native` (auto-detect)

## Phase 2: LEA Optimizations

### 2.1 ADD → LEA Conversion

**File:** `maxon-bin/backend/x86_lea_opt.cpp`

Transform ADD to LEA when beneficial:

| Pattern | Before | After | Benefit |
|---------|--------|-------|---------|
| Copy-and-add | `mov rax, rbx; add rax, rcx` | `lea rax, [rbx + rcx]` | Save instruction, preserve sources |
| Add immediate | `mov rax, rbx; add rax, 4` | `lea rax, [rbx + 4]` | Save instruction |
| Preserve flags | `add rax, rbx; <uses flags>` | No change | ADD needed for flags |
| Preserve flags | `add rax, rbx; <no flag use>` | `lea rax, [rax + rbx]` | Preserve flags from earlier |

Implementation location: New post-regalloc pass or modify `genAdd()` directly.

### 2.2 LEA → ADD Conversion (for slow-LEA targets)

On microarchs where LEA is slow:
- `lea rax, [rax + rbx]` → `add rax, rbx` (when flags not needed later)
- `lea rax, [rax + 1]` → `inc rax` (smaller encoding)
- `lea rax, [rax - 1]` → `dec rax`

### 2.3 3-Operand LEA Splitting (Sandy Bridge+)

Complex LEA with base + index + disp has 3-cycle latency on modern Intel:

```asm
; Before: 3 cycles, port 1 only
lea rax, [rbx + rcx*4 + 8]

; After: 2 cycles total, more ports available  
lea rax, [rbx + rcx*4]
add rax, 8
```

Only apply when on critical path (needs simple heuristic or profile data).

## Phase 3: Implementation Steps

### Step 1: Add encoding support

- [ ] Add `leaRM64` to `X86Encoder` (LEA with memory operand)
- [ ] Support `[base + index*scale + disp]` addressing modes
- [ ] Add unit tests for LEA encoding

### Step 2: Modify genAdd for simple cases

```cpp
void X86CodeGen::genAdd(mir::MIRInstruction *inst) {
    // If result != lhs and we don't need flags, use LEA
    if (inst->result != inst->operands[0] && !needsFlagsAfter(inst)) {
        // lea result, [lhs + rhs]
        ...
    } else {
        // Original ADD path
        ...
    }
}
```

### Step 3: Create post-regalloc LEA optimization pass

- Scan for patterns: `mov + add` → `lea`
- Respect microarch settings
- Run after register allocation, before final emission

### Step 4: Add address calculation LEA usage

- For array indexing: `lea rax, [base + idx*scale]`
- For type field access with offset
- Already partially done in `genGEP`, but can be expanded

## Phase 4: Testing Strategy

### Unit tests (`backend-tests/`)

- `lea-basic.maxon` - verify LEA emitted for copy-and-add
- `lea-flags.maxon` - verify ADD used when flags needed
- `lea-complex.maxon` - verify 3-op LEA handling

### Benchmark tests

- Compare fannkuch-redux with/without LEA opts
- Compare spectral-norm
- Measure instruction count and cycles

## Phase 5: Future Microarch Optimizations

Once infrastructure is in place, add:

1. **Branch alignment** - Align loop headers to 16/32 bytes
2. **Fusion-aware scheduling** - Keep macro-fusible pairs together (cmp+jcc)
3. **Zero-idiom recognition** - `xor eax, eax` is dependency-breaking
4. **Partial register stalls** - Avoid reading full reg after writing partial
5. **MULX/SHRX/SHLX** - Use BMI2 when available (no flags, 3-operand)
6. **Store-to-load forwarding** - Ensure aligned stores for forwarding

## Files to Create/Modify

| File | Action |
|------|--------|
| `backend/x86_target.h` | New - CPU feature model |
| `backend/x86_target.cpp` | New - CPU detection & presets |
| `backend/x86_encoding.h` | Add LEA variants |
| `backend/x86_encoding.cpp` | Implement LEA encoding |
| `backend/x86_codegen.cpp` | Modify genAdd, add LEA logic |
| `backend/x86_lea_opt.cpp` | New - post-regalloc LEA pass (optional) |
| `compiler.cpp` | Add --target-cpu flag |

## Reference Materials

- LLVM: `llvm/lib/Target/X86/X86FixupLEAs.cpp`
- LLVM: `llvm/lib/Target/X86/X86OptimizeLEAs.cpp`
- Intel Optimization Manual, Section 3.5.1.1
- Agner Fog's instruction tables: https://agner.org/optimize/
