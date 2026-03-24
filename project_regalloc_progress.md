---
name: Register Allocator Implementation Progress
description: Tracking progress on replicating the C# compiler's register allocator in the self-hosted compiler
type: project
---

## Goal
Replace the naive "spill everything to stack" strategy in the self-hosted compiler with a greedy linear-scan register allocator with LRU eviction, matching the C# bootstrap compiler's `RegisterManagerBase`.

## Completed

### 1. Cross-platform RegisterManager.maxon (NEW FILE)
- Location: `maxon-selfhosted/Compiler/Targets/Shared/RegisterManager.maxon`
- Uses integer register ordinals (enum raw values) — works for both x86 and ARM64
- Target-specific emission via `RegTarget` enum dispatch (x64/arm64)
- Implements: `createRegState`, `rmAllocateRegister`, `rmEnsureInRegister`, `rmSpillRegisterIfOccupied`, `rmEvict` (3-tier LRU: constants > spilled > others), `rmSpillAllCallerSaved`, `rmInvalidateCallerSaved`, `rmResetForBlockTransition`, `rmNoteValueDead`, `rmEmitLoadImmediate`, `rmTransferValue`, `rmAssign`, `rmRecordConstant`, `rmSetRegisterHint`, `rmTotalStackSize`
- Emit helpers: `rmEmitSpill`, `rmEmitReload`, `rmEmitMovImm`, `rmEmitMovReg` — each dispatches to x64 or arm64 ops
- Ordinal conversion: `ordinalToX64`/`ordinalToArm64` via `fromRawValue()`, `x64RegToOrdinal`/`arm64RegToOrdinal` via `.rawValue`
- Pool configs: `createX64GprPool` returns [0,1,2,3,6,7,8,9] (rax,rcx,rdx,rbx,rsi,rdi,r8,r9)
- State uses `RegIntArray` (signed i64) with sentinels: -1 for "not present", large negative for "no stack home"

### 2. MaxX64Dialect extensions
- Added to `MaxX64Op` union: `movRegReg`, `cmpRegReg`, `testRegReg`, `cqo`, `idivReg`, `spillToStack`, `reloadFromStack`, `xorRegRegSelf`
- Added `maxX64OpToString` cases for all new ops

### 3. MaxArm64Dialect extensions
- Added to `MaxArm64Op` union: `movRegReg`, `spillToStack`, `reloadFromStack`
- Added `maxArm64OpToString` cases

### 4. X86 Code Emitter (MaxX64CodeEmitter.maxon)
- Added REX/ModRM encoding infrastructure: `regCode`, `isExtended`, `emitRexWPrefix`, `emitRexWSingle`, `emitModRMReg`, `emitModRMExt`
- Added emitters: `emitMovRegRegOp`, `emitCmpRegRegOp`, `emitTestRegRegOp`, `emitCqoOp`, `emitIdivRegOp`, `emitXorRegRegSelfOp`, `emitSpillToStackOp`, `emitReloadFromStackOp`, `emitMovRegImmGeneric`
- Upgraded existing emitters to accept register parameters: `emitAddRegRegParam`, `emitSubRegRegParam`, `emitImulRegRegParam`, `emitNegRegParam`, `emitBitNotRegParam`, `emitAndRegRegParam`, `emitOrRegRegParam`, `emitXorRegRegParam`, `emitShlRegClParam`, `emitSarRegClParam`
- Updated `emitMaxX64Op` dispatch to use parameterized versions and handle new ops

### 5. ARM64 Code Emitter (MaxArm64CodeEmitter.maxon)
- Added: `maxArm64EmitMovRegReg` (ORR Xd, XZR, Xn), `maxArm64EmitSpillToStack` (STUR), `maxArm64EmitReloadFromStack` (LDUR)
- Updated dispatch to handle new ops

### 6. Verification
- `./bin/maxon.exe build maxon-selfhosted` compiles successfully
- All 27 self-hosted spec tests pass (no behavioral change — new ops not used yet)

## Still To Do

### 7. Rewrite MidToMaxX64Conversion.maxon
The main conversion file (~588 lines) needs to be rewritten to use `RegState` instead of `ValueSlotMap` for all lowering functions:
- **Deferred prologue**: lower body first, insert prologue with final stack size after
- **Constants**: `rmEmitLoadImmediate` instead of movRegImm+movSlot
- **Binary ops**: `rmEnsureInRegister` both operands, `rmTransferValue`, emit op with allocated registers
- **Unary ops**: similar pattern
- **Division**: ensure divisor not in RAX/RDX, spill RAX/RDX, CQO+IDIV, assign result
- **Shifts**: ensure count in RCX, spill RCX if needed
- **Comparisons**: `rmEnsureInRegister` both, emit `cmpRegReg`, flags consumed by condBr
- **Calls**: `rmSpillAllCallerSaved`, load args into CC registers, call, `rmInvalidateCallerSaved`, assign RAX
- **Return**: ensure value in register, move to RAX, epilogue
- **Variable store**: ensure in register, emit `movSlot`, `rmNoteStoreToStack`
- **Variable load**: just `rmNoteValueOnStack` (lazy load on demand)
- **Block transitions**: `rmResetForBlockTransition` at each new block
- **SysOps**: these still use slot-based addressing internally — need bridging

### 8. Rewrite MidToMaxArm64Conversion.maxon
Same pattern but simpler (no div/shift register constraints, 3-operand instructions, more GPRs).

### 9. Update spec test MLIR output sections
After the register allocator is working, all ~27 fragment test files need their MLIR output (section 3) regenerated to match the new register-allocated instruction sequences.

## Reference files
- C# allocator: `maxon-sharp/Compiler/MLIR/Conversion/RegisterManagerBase.cs`
- C# x86 specialization: `maxon-sharp/Compiler/MLIR/Conversion/RegisterManager.cs`
- C# x86 conversion: `maxon-sharp/Compiler/MLIR/Conversion/StandardToX86Conversion.cs`
