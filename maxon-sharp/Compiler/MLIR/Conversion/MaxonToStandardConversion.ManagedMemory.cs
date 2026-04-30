using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
  // ============================================================================
  // Managed memory lowering helpers
  // ============================================================================

  /// <summary>
  /// Clamp a capacity value to 0 if negative (rdata/slice sentinels are -2/-1).
  /// Returns the clamped value as an StdI64 suitable for arithmetic.
  /// </summary>
  private static StdI64 EmitClampCapacityNonNeg(IrBlock<StandardOp> block, StdI64 capacity) {
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var isNeg = new StdCmpI64Op("lt", capacity, zeroConst.Result);
    block.AddOp(isNeg);
    var clamped = new StdSelectI64Op(isNeg.Result, zeroConst.Result, capacity);
    block.AddOp(clamped);
    return clamped.Result;
  }

  /// <summary>
  /// Compute grow capacity: max(requiredBytes + 1, currentCapBytes * 2, 64).
  /// Standard geometric growth strategy with a minimum floor.
  /// </summary>
  private static StdI64 EmitGrowCapacity(
    IrBlock<StandardOp> block, StdI64 requiredBytes, StdI64 currentCapBytes) {
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var requiredPlusOne = new StdAddI64Op(requiredBytes, oneConst.Result);
    block.AddOp(requiredPlusOne);
    var twoConst = new StdConstI64Op(2);
    block.AddOp(twoConst);
    var doubled = new StdMulI64Op(currentCapBytes, twoConst.Result);
    block.AddOp(doubled);
    var cmp1 = new StdCmpU64Op("ugt", requiredPlusOne.Result, doubled.Result);
    block.AddOp(cmp1);
    var grow1 = new StdSelectI64Op(cmp1.Result, requiredPlusOne.Result, doubled.Result);
    block.AddOp(grow1);
    var minCap = new StdConstI64Op(64);
    block.AddOp(minCap);
    var cmp2 = new StdCmpU64Op("ugt", grow1.Result, minCap.Result);
    block.AddOp(cmp2);
    var growCap = new StdSelectI64Op(cmp2.Result, grow1.Result, minCap.Result);
    block.AddOp(growCap);
    return growCap.Result;
  }

  /// <summary>
  /// Emit a runtime bounds check: panics if (unsigned)index >= (unsigned)limit.
  /// Uses the maxon_bounds_check runtime function with a pre-defined panic message.
  /// </summary>
  private static void EmitBoundsCheck(
    IrBlock<StandardOp> block, StdI64 index, StdI64 limit, string panicSymdataLabel) {
    var leaOp = new StdLeaSymdataOp(panicSymdataLabel);
    block.AddOp(leaOp);
    var ptrToI64 = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrToI64);
    block.AddOp(new StdCallRuntimeOp("maxon_bounds_check", [index, limit, ptrToI64.Result], null));
  }

  /// <summary>
  /// Emit "panic if cond is true". Builds index = (cond ? 1 : 0), limit = 1 and feeds
  /// them to maxon_bounds_check, which panics when index >= limit. Lets callers express
  /// arbitrary boolean panic conditions (null check, equality mismatch) without writing
  /// the same select-and-bounds-check sequence by hand.
  /// </summary>
  private static void EmitPanicIf(
    IrBlock<StandardOp> block, StdBool cond, string panicSymdataLabel) {
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var asIdx = new StdSelectI64Op(cond, oneConst.Result, zeroConst.Result);
    block.AddOp(asIdx);
    EmitBoundsCheck(block, asIdx.Result, oneConst.Result, panicSymdataLabel);
  }


  /// <summary>
  /// Resolve the struct variable name for a managed memory value.
  /// Uses the valueMap to find a StdHeapPtr which carries the variable name.
  /// </summary>
  private static string ResolveManagedVarName(
    MaxonValue managedValue,
    Dictionary<MaxonValue, StdValue> valueMap) {
    if (valueMap.TryGetValue(managedValue, out var stdVal) && stdVal is StdHeapPtr hp && hp.VarName != null)
      return hp.VarName;
    throw new InvalidOperationException($"Managed memory value %{managedValue.Id} not found in valueMap as StdHeapPtr with VarName");
  }

  /// <summary>
  /// Load the buffer pointer from a heap-allocated __ManagedMemory struct.
  /// The managedVarName variable holds the heap pointer to the __ManagedMemory struct.
  /// buffer is at offset 0 (first field).
  /// </summary>
  private static StdI64 LoadManagedBuffer(
    IrBlock<StandardOp> block,
    string managedVarName,
    Dictionary<string, string> varTypes) {
    return (StdI64)EmitStructFieldLoad(block, managedVarName, 0, IrType.I64, varTypes);
  }

  /// <summary>
  /// Compute address: buffer + index * elementSize (runtime element size)
  /// </summary>
  private static StdI64 ComputeElementAddress(
    IrBlock<StandardOp> block,
    StdI64 buffer,
    StdI64 index,
    StdI64 elementSize) {
    var offsetOp = new StdMulI64Op(index, elementSize);
    block.AddOp(offsetOp);
    var addrOp = new StdAddI64Op(buffer, offsetOp.Result);
    block.AddOp(addrOp);
    return addrOp.Result;
  }

  /// <summary>
  /// Compute byte size for a bit-packed buffer: (count + 7) >> 3.
  /// Used when element_size is 0 (bit-packed bool arrays).
  /// </summary>
  private static StdI64 ComputeBitPackedByteSize(IrBlock<StandardOp> block, StdI64 count) {
    var sevenConst = new StdConstI64Op(7);
    block.AddOp(sevenConst);
    var countPlus7 = new StdAddI64Op(count, sevenConst.Result);
    block.AddOp(countPlus7);
    var threeConst = new StdConstI64Op(3);
    block.AddOp(threeConst);
    var byteSize = new StdShrU64Op(countPlus7.Result, threeConst.Result);
    block.AddOp(byteSize);
    return byteSize.Result;
  }

  /// <summary>
  /// Compute the byte limit for bounds-checking byte-level access to a managed buffer.
  /// Handles both bit-packed (elemSize==0) and normal layouts via a runtime select.
  /// </summary>
  private static StdI64 ComputeByteLimit(IrBlock<StandardOp> block, StdI64 length, StdI64 elemSize) {
    var zeroForCheck = new StdConstI64Op(0);
    block.AddOp(zeroForCheck);
    var zeroCheck = new StdCmpI64Op("eq", elemSize, zeroForCheck.Result);
    block.AddOp(zeroCheck);
    var bitPackedLimit = ComputeBitPackedByteSize(block, length);
    var normalLimit = new StdMulI64Op(length, elemSize);
    block.AddOp(normalLimit);
    var byteLimit = new StdSelectI64Op(zeroCheck.Result, bitPackedLimit, normalLimit.Result);
    block.AddOp(byteLimit);
    return byteLimit.Result;
  }

  /// <summary>
  /// Extracts bit at index from a bit-packed buffer and widens to an i1 StdBool.
  /// Use this when the bit value flows out as a bool (e.g. Array&lt;bool&gt;.get(i)
  /// returning an Element, for-in iteration yielding a bool). For internal bit-copy
  /// operations that immediately re-pack via EmitBitSet, use EmitBitGet directly
  /// to keep the value as an i64 with {0,1} payload.
  /// </summary>
  private static StdBool EmitBitGetAsBool(IrBlock<StandardOp> block, StdI64 buffer, StdI64 index) {
    var bit = EmitBitGet(block, buffer, index);
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    var cmp = new StdCmpI64Op("ne", bit, zero.Result);
    block.AddOp(cmp);
    return cmp.Result;
  }

  /// <summary>
  /// Extract a single bit from a bit-packed buffer. Returns 0 or 1 as i64.
  /// Computes: (buffer[index >> 3] >> (index &amp; 7)) &amp; 1
  /// </summary>
  private static StdI64 EmitBitGet(IrBlock<StandardOp> block, StdI64 buffer, StdI64 index) {
    var threeConst = new StdConstI64Op(3);
    block.AddOp(threeConst);
    var byteIndex = new StdShrU64Op(index, threeConst.Result);
    block.AddOp(byteIndex);
    var sevenConst = new StdConstI64Op(7);
    block.AddOp(sevenConst);
    var bitOffset = new StdAndI64Op(index, sevenConst.Result);
    block.AddOp(bitOffset);
    var addr = new StdAddI64Op(buffer, byteIndex.Result);
    block.AddOp(addr);
    // Bit-packed: load the byte unsigned so the shift+mask extracts the right bit
    // (sign-extending would propagate bit 7 across the high bits and break the ZX shift).
    var loadOp = new StdLoadIndirectOp(addr.Result, 0, IrType.U8);
    block.AddOp(loadOp);
    var shifted = new StdShrU64Op((StdI64)loadOp.Result, bitOffset.Result);
    block.AddOp(shifted);
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var result = new StdAndI64Op(shifted.Result, oneConst.Result);
    block.AddOp(result);
    return result.Result;
  }

  /// <summary>
  /// Write a single bit to a bit-packed buffer. value should be 0 or 1.
  /// Computes: buffer[index >> 3] = (buffer[index >> 3] &amp; ~(1 &lt;&lt; (index &amp; 7))) | ((value &amp; 1) &lt;&lt; (index &amp; 7))
  /// </summary>
  private static void EmitBitSet(IrBlock<StandardOp> block, StdI64 buffer, StdI64 index, StdI64 value) {
    var threeConst = new StdConstI64Op(3);
    block.AddOp(threeConst);
    var byteIndex = new StdShrU64Op(index, threeConst.Result);
    block.AddOp(byteIndex);
    var sevenConst = new StdConstI64Op(7);
    block.AddOp(sevenConst);
    var bitOffset = new StdAndI64Op(index, sevenConst.Result);
    block.AddOp(bitOffset);
    var addr = new StdAddI64Op(buffer, byteIndex.Result);
    block.AddOp(addr);
    // Load current byte unsigned (this is a raw byte buffer used for bit packing).
    var loadOp = new StdLoadIndirectOp(addr.Result, 0, IrType.U8);
    block.AddOp(loadOp);
    // Clear the target bit: byte & ~(1 << bitOffset)
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var mask = new StdShlI64Op(oneConst.Result, bitOffset.Result);
    block.AddOp(mask);
    var ffConst = new StdConstI64Op(0xFF);
    block.AddOp(ffConst);
    var invMask = new StdXorI64Op(mask.Result, ffConst.Result);
    block.AddOp(invMask);
    var cleared = new StdAndI64Op((StdI64)loadOp.Result, invMask.Result);
    block.AddOp(cleared);
    // Set the target bit: cleared | ((value & 1) << bitOffset)
    var valueBit = new StdAndI64Op(value, oneConst.Result);
    block.AddOp(valueBit);
    var shiftedValue = new StdShlI64Op(valueBit.Result, bitOffset.Result);
    block.AddOp(shiftedValue);
    var newByte = new StdOrI64Op(cleared.Result, shiftedValue.Result);
    block.AddOp(newByte);
    // Store back
    block.AddOp(new StdStoreIndirectOp(newByte.Result, addr.Result, 0, IrType.I8));
  }

  /// <summary>
  /// __managed_memory_get(managed, index): load element from heap buffer.
  /// For primitive elements: loads the value directly from buffer[index].
  /// For struct elements: loads the heap pointer stored at buffer[index], guards
  /// against null (empty slot → ArrayError.emptySlot), then increfs the pointer
  /// so the caller receives its own reference.
  /// </summary>
  private static void LowerManagedMemGet(
    MaxonManagedMemGetOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps,
    MaxonValue? errorFlagValue = null) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
    var index = (StdI64)valueMap[op.Index];
    // mergeLabel is non-null when we emitted a conditional branch to skip invalid
    // memory access on the OOB path; both the error path and ok path branch here.
    string? mergeLabel = null;

    // For struct elements we'll write the loaded heap pointer into a stable temp
    // that the merge block reads. Pre-allocate and seed to 0 BEFORE the OOB cond_br
    // so the OOB-error path observes a defined null value (the merge load otherwise
    // reads stack garbage and the caller's destructor decrefs that garbage).
    string? structResultTemp = null;
    if (op.IsStructElement && errorFlagValue != null) {
      var preTempId = IrContext.Current.NextId();
      structResultTemp = temps.CreateTemp("mmget", preTempId, op.StructElementTypeName ?? "unknown", OwnershipFlags.Orphan | OwnershipFlags.OwnsRef);
      var preSeedConst = new StdConstI64Op(0);
      block.AddOp(preSeedConst);
      EmitStore(block, preSeedConst.Result, structResultTemp, varTypes);
    }

    if (!op.IsBoundsCheckSafe) {
      var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
      if (errorFlagValue != null) {
        // __ManagedMemoryError.indexOutOfBounds (ordinal 0) → flag 1
        var isError = new StdCmpU64Op("uge", index, length);
        block.AddOp(isError);
        EmitBoundsCheckErrorFlag(block, isError.Result, 1, valueMap, varTypes, errorFlagValue);
        // Branch to skip buffer dereference on OOB (buffer may be null for empty arrays).
        // The error path stores a dummy result and branches to a merge block; the ok path
        // does the actual load and also falls through to the merge block.
        var oobUid = IrContext.Current.NextId();
        var oobLabel = $"__get_oob_{oobUid}";
        var okLabel = $"__get_ok_{oobUid}";
        mergeLabel = $"__get_merge_{oobUid}";
        block.AddOp(new StdCondBrOp(isError.Result, oobLabel, okLabel));
        // Error path: store dummy 0 to the result temp, then branch to merge.
        var errBlock = func.Body.AddBlock(oobLabel);
        var dummyConst = new StdConstI64Op(0);
        errBlock.AddOp(dummyConst);
        var dummyTemp = $"__get_dummy_{oobUid}";
        varTypes[dummyTemp] = "i64";
        EmitStore(errBlock, dummyConst.Result, dummyTemp, varTypes);
        errBlock.AddOp(new StdBrOp(mergeLabel));
        block = func.Body.AddBlock(okLabel);
      } else {
        EmitBoundsCheck(block, index, length, "__mm_panic_index_oob");
      }
    }
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, IrType.I64, varTypes);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var addr = ComputeElementAddress(block, buffer, index, elemSize);

    if (op.ResultKind == MaxonValueKind.Bool) {
      // Bit-packed bool: extract bit at index and normalize to a StdBool so
      // downstream consumers (cond_br, bool-typed assigns) see the right shape.
      valueMap[op.Result] = EmitBitGetAsBool(block, buffer, index);
    } else if (op.IsStructElement) {
      // Struct elements are heap pointers stored in the buffer (8 bytes each).
      // Load the pointer and incref — the caller gets their own reference.
      // The buffer retains its reference; mm_decref_managed_elements handles
      // the buffer's copy when the array is freed.
      var loadOp = new StdLoadIndirectOp(addr, 0, IrType.I64);
      block.AddOp(loadOp);

      // Slots can be zero after resize() or remove() — increfing a null pointer
      // would corrupt the reference count. Return ArrayError.emptySlot so callers
      // using try/otherwise can handle sparse arrays without undefined behaviour.
      // Error flag = ArrayError.emptySlot ordinal (1) + 1 = 2 (0 = success convention).
      var zeroForNull = new StdConstI64Op(0);
      block.AddOp(zeroForNull);
      var isNullCmp = new StdCmpI64Op("eq", (StdI64)loadOp.Result, zeroForNull.Result);
      block.AddOp(isNullCmp);
      var nullUid = IrContext.Current.NextId();
      var slotEmptyLabel = $"__slot_empty_{nullUid}";
      var slotNonnullLabel = $"__slot_nonnull_{nullUid}";
      var slotMergeLabel = $"__slot_merge_{nullUid}";

      // Reuse the temp pre-allocated and seeded above (or allocate now if none was —
      // happens in the panic-only / no-errorFlag path).
      string tempName;
      if (structResultTemp != null) {
        tempName = structResultTemp;
      } else {
        var tempId = IrContext.Current.NextId();
        tempName = temps.CreateTemp("mmget", tempId, op.StructElementTypeName ?? "unknown", OwnershipFlags.Orphan | OwnershipFlags.OwnsRef);
        var seedConst = new StdConstI64Op(0);
        block.AddOp(seedConst);
        EmitStore(block, seedConst.Result, tempName, varTypes);
      }

      block.AddOp(new StdCondBrOp(isNullCmp.Result, slotEmptyLabel, slotNonnullLabel));

      // Empty slot path: record error flag = 2 (ArrayError.emptySlot), leave temp at 0,
      // then branch to slot merge (no actual memory access on this path).
      var slotErrBlock = func.Body.AddBlock(slotEmptyLabel);
      if (errorFlagValue != null) {
        var errFlagConst = new StdConstI64Op(2);
        slotErrBlock.AddOp(errFlagConst);
        EmitStore(slotErrBlock, errFlagConst.Result, "__error_flag", varTypes);
      }
      slotErrBlock.AddOp(new StdBrOp(slotMergeLabel));

      // Nonnull slot path: incref and store to result temp.
      block = func.Body.AddBlock(slotNonnullLabel);
      EmitIncrefValue(block, (StdI64)loadOp.Result, scopeName: _currentFuncName);
      EmitStore(block, (StdI64)loadOp.Result, tempName, varTypes);
      block.AddOp(new StdBrOp(slotMergeLabel));

      // Merge: load from the result temp (both paths stored here).
      // Re-load __error_flag from memory so the caller sees the merged flag
      // (could be 0 from OOB-success path, 2 from slot-empty path, or 0 from
      // nonnull path). Replacing valueMap with a per-block constant clobbers
      // the OOB check's success select and breaks the success path.
      block = func.Body.AddBlock(slotMergeLabel);
      if (errorFlagValue != null) {
        var mergedFlag = (StdI64)EmitLoad(block, "__error_flag", varTypes);
        valueMap[errorFlagValue] = mergedFlag;
      }
      var mergedLoad = EmitLoad(block, tempName, varTypes);
      valueMap[op.Result] = new StdHeapPtr(mergedLoad.Id, op.StructElementTypeName ?? "unknown", tempName);
    } else {
      // Prefer the precise narrow storage type when available (e.g. U8 for int(0..100),
      // I8 for int(-50..50)) so the codegen picks movzx vs movsx correctly. Fall back to
      // the kind-based mapping for callers that don't supply the type hint.
      var elemType = op.ElementStorageType ?? GetManagedMemElementType(op.ResultKind, "LowerManagedMemGet");
      var loadOp = new StdLoadIndirectOp(addr, 0, elemType);
      block.AddOp(loadOp);
      valueMap[op.Result] = loadOp.Result;
    }

    if (mergeLabel != null) {
      // The ok path completes here; branch to merge so OOB and ok paths converge.
      block.AddOp(new StdBrOp(mergeLabel));
      block = func.Body.AddBlock(mergeLabel);
      // Re-load __error_flag in the merge block — the OOB-error path stored 1,
      // the OOB-success path stored 0, and (for struct elements) the slot-empty
      // path stored 2. Using the per-branch SSA value via valueMap clobbers
      // across blocks; the load is the merge.
      if (errorFlagValue != null) {
        var mergedOobFlag = (StdI64)EmitLoad(block, "__error_flag", varTypes);
        valueMap[errorFlagValue] = mergedOobFlag;
      }
    }
  }

  /// <summary>
  /// __managed_memory_remove(managed, index): remove element at index with ownership transfer.
  /// Loads the element (without incref — the buffer's reference is transferred to the caller),
  /// zeroes the slot, shifts remaining elements left, and decrements length.
  /// </summary>
  private static void LowerManagedMemRemove(
    MaxonManagedMemRemoveOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps,
    MaxonValue? errorFlagValue = null) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
    var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    var index = (StdI64)valueMap[op.Index];

    string? removeMergeLabel = null;
    if (errorFlagValue != null) {
      // Emit error flag: __ManagedMemoryError.indexOutOfBounds (ordinal 0) → flag 1
      var isError = new StdCmpU64Op("uge", index, length);
      block.AddOp(isError);
      EmitBoundsCheckErrorFlag(block, isError.Result, 1, valueMap, varTypes, errorFlagValue);
      // Branch to skip buffer dereference on OOB; both paths merge after the remove body.
      var oobUid = IrContext.Current.NextId();
      var removeOobLabel = $"__remove_oob_{oobUid}";
      var removeOkLabel = $"__remove_ok_{oobUid}";
      removeMergeLabel = $"__remove_merge_{oobUid}";
      block.AddOp(new StdCondBrOp(isError.Result, removeOobLabel, removeOkLabel));
      // Error path: store dummy 0 to result var, branch to merge.
      var removeErrBlock = func.Body.AddBlock(removeOobLabel);
      var dummyConst = new StdConstI64Op(0);
      removeErrBlock.AddOp(dummyConst);
      var removeDummyTemp = $"__remove_dummy_{oobUid}";
      varTypes[removeDummyTemp] = "i64";
      EmitStore(removeErrBlock, dummyConst.Result, removeDummyTemp, varTypes);
      removeErrBlock.AddOp(new StdBrOp(removeMergeLabel));
      block = func.Body.AddBlock(removeOkLabel);
    } else {
      // Bounds check: if index >= length, panic
      var cmpOp = new StdCmpI64Op("lt", index, length);
      block.AddOp(cmpOp);
      var uid = IrContext.Current.NextId();
      var oobBlock = $"__remove_oob_{uid}";
      var inBoundsBlock = $"__remove_ok_{uid}";
      block.AddOp(new StdCondBrOp(cmpOp.Result, inBoundsBlock, oobBlock));
      block = func.Body.AddBlock(inBoundsBlock);
      var errBlock = func.Body.AddBlock(oobBlock);
      var errFlag = new StdConstI64Op(1);
      errBlock.AddOp(errFlag);
      errBlock.AddOp(new StdErrorReturnOp(errFlag.Result));
    }

    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, IrType.I64, varTypes);

    // COW check before mutation
    EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: op.ResultKind == MaxonValueKind.Bool);

    // Reload buffer/length after COW (COW may change the buffer pointer)
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var lengthAfterCow = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);

    if (op.ResultKind == MaxonValueKind.Bool) {
      // Bit-packed bool: extract bit at index and widen to a StdBool for the caller.
      // The shift loop below reads bits via EmitBitGet on each iteration.
      valueMap[op.Result] = EmitBitGetAsBool(block, buffer, index);

      // Shift bits left: for i from index to length-2, copy bit[i+1] to bit[i]
      var oneConst = new StdConstI64Op(1);
      block.AddOp(oneConst);
      var newLength = new StdSubI64Op(lengthAfterCow, oneConst.Result);
      block.AddOp(newLength);

      // Loop: i = index; while (i < newLength) { bit[i] = bit[i+1]; i++ }
      var loopUid = IrContext.Current.NextId();
      var loopVar = $"__remove_i_{loopUid}";
      EmitStore(block, index, loopVar, varTypes);
      // Spill buffer and newLength for use inside loop
      var bufVar = $"__remove_buf_{loopUid}";
      EmitStore(block, buffer, bufVar, varTypes);
      var newLenVar = $"__remove_newlen_{loopUid}";
      EmitStore(block, newLength.Result, newLenVar, varTypes);

      var loopHeaderLabel = $"__remove_hdr_{loopUid}";
      var loopBodyLabel = $"__remove_body_{loopUid}";
      var loopExitLabel = $"__remove_exit_{loopUid}";
      block.AddOp(new StdBrOp(loopHeaderLabel));

      var headerBlock = func.Body.AddBlock(loopHeaderLabel);
      var iReload = (StdI64)EmitLoad(headerBlock, loopVar, varTypes);
      var newLenReload = (StdI64)EmitLoad(headerBlock, newLenVar, varTypes);
      var cmpLoop = new StdCmpI64Op("lt", iReload, newLenReload);
      headerBlock.AddOp(cmpLoop);
      headerBlock.AddOp(new StdCondBrOp(cmpLoop.Result, loopBodyLabel, loopExitLabel));

      var bodyBlock = func.Body.AddBlock(loopBodyLabel);
      var iBody = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
      var bufBody = (StdI64)EmitLoad(bodyBlock, bufVar, varTypes);
      var oneBody = new StdConstI64Op(1);
      bodyBlock.AddOp(oneBody);
      var nextIdx = new StdAddI64Op(iBody, oneBody.Result);
      bodyBlock.AddOp(nextIdx);
      var bitVal = EmitBitGet(bodyBlock, bufBody, nextIdx.Result);
      // Reload buffer after EmitBitGet (it doesn't clobber, but be consistent)
      var bufBody2 = (StdI64)EmitLoad(bodyBlock, bufVar, varTypes);
      var iBody2 = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
      EmitBitSet(bodyBlock, bufBody2, iBody2, bitVal);
      // Increment loop counter
      var iBody3 = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
      var oneInc = new StdConstI64Op(1);
      bodyBlock.AddOp(oneInc);
      var newI = new StdAddI64Op(iBody3, oneInc.Result);
      bodyBlock.AddOp(newI);
      EmitStore(bodyBlock, newI.Result, loopVar, varTypes);
      bodyBlock.AddOp(new StdBrOp(loopHeaderLabel));

      block = func.Body.AddBlock(loopExitLabel);
      var finalNewLen = (StdI64)EmitLoad(block, newLenVar, varTypes);
      EmitStructFieldStore(block, finalNewLen, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    } else {
      var addr = ComputeElementAddress(block, buffer, index, elemSize);

      if (op.IsStructElement) {
        // Load the struct pointer — ownership transfer, NO incref.
        // The buffer's reference is handed to the caller.
        var loadOp = new StdLoadIndirectOp(addr, 0, IrType.I64);
        block.AddOp(loadOp);

        // Zero the slot to prevent mm_decref_managed_elements from touching it
        var zeroOp = new StdConstI64Op(0);
        block.AddOp(zeroOp);
        block.AddOp(new StdStoreIndirectOp(zeroOp.Result, addr, 0, IrType.I64));

        var tempId = IrContext.Current.NextId();
        var tempName = temps.CreateTemp("callret", tempId, op.StructElementTypeName ?? "unknown", OwnershipFlags.Orphan);
        EmitStore(block, (StdI64)loadOp.Result, tempName, varTypes);
        valueMap[op.Result] = new StdHeapPtr(loadOp.Result.Id, op.StructElementTypeName ?? "unknown", tempName);
      } else {
        var elemType = GetManagedMemElementType(op.ResultKind, "LowerManagedMemRemove");
        var loadOp = new StdLoadIndirectOp(addr, 0, elemType);
        block.AddOp(loadOp);
        valueMap[op.Result] = loadOp.Result;
      }

      // Shift elements left: move [index+1..length-1] to [index..length-2]
      var oneConst = new StdConstI64Op(1);
      block.AddOp(oneConst);
      var newLength = new StdSubI64Op(lengthAfterCow, oneConst.Result);
      block.AddOp(newLength);

      // Only shift if there are elements after the removed one (index < newLength)
      var shiftCount = new StdSubI64Op(newLength.Result, index);
      block.AddOp(shiftCount);

      // Compute src/dst addresses for memmove
      var srcIndex = new StdAddI64Op(index, oneConst.Result);
      block.AddOp(srcIndex);
      var srcOffset = new StdMulI64Op(srcIndex.Result, elemSize);
      block.AddOp(srcOffset);
      var srcAddr = new StdAddI64Op(buffer, srcOffset.Result);
      block.AddOp(srcAddr);
      var dstOffset = new StdMulI64Op(index, elemSize);
      block.AddOp(dstOffset);
      var dstAddr = new StdAddI64Op(buffer, dstOffset.Result);
      block.AddOp(dstAddr);
      var bytesToMove = new StdMulI64Op(shiftCount.Result, elemSize);
      block.AddOp(bytesToMove);
      block.AddOp(new StdMemCopyOp(srcAddr.Result, dstAddr.Result, bytesToMove.Result));

      // Zero the last slot (now a stale duplicate from the shift)
      if (op.IsStructElement) {
        var lastAddr = ComputeElementAddress(block, buffer, newLength.Result, elemSize);
        var zeroOp2 = new StdConstI64Op(0);
        block.AddOp(zeroOp2);
        block.AddOp(new StdStoreIndirectOp(zeroOp2.Result, lastAddr, 0, IrType.I64));
      }

      // Update length
      EmitStructFieldStore(block, newLength.Result, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    }

    if (removeMergeLabel != null) {
      // Ok path completes here; branch to merge so OOB and ok paths converge.
      block.AddOp(new StdBrOp(removeMergeLabel));
      block = func.Body.AddBlock(removeMergeLabel);
    }
  }

  /// <summary>
  /// __managed_memory_set_at(managed, index, value): store element into heap buffer.
  /// For struct elements, decrefs the old occupant before storing the new pointer.
  /// </summary>
  private static void LowerManagedMemSet(
    MaxonManagedMemSetOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue = null) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, IrType.I64, varTypes);
    var isBitPacked = op.ElementKind == MaxonValueKind.Bool;
    EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: isBitPacked);
    // Check against capacity after COW (COW updates capacity from 0 to length)
    var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    var index = (StdI64)valueMap[op.Index];
    string? setMergeLabel = null;
    if (errorFlagValue != null) {
      var isError = new StdCmpU64Op("uge", index, capacity);
      block.AddOp(isError);
      EmitBoundsCheckErrorFlag(block, isError.Result, 1, valueMap, varTypes, errorFlagValue);
      // Branch around the store so an OOB index doesn't dereference past the buffer
      // (or worse, hit a null buffer when the array was created but never sized).
      var setUid = IrContext.Current.NextId();
      var setOobLabel = $"__set_oob_{setUid}";
      var setOkLabel = $"__set_ok_{setUid}";
      setMergeLabel = $"__set_merge_{setUid}";
      block.AddOp(new StdCondBrOp(isError.Result, setOobLabel, setOkLabel));
      var setErrBlock = func.Body.AddBlock(setOobLabel);
      setErrBlock.AddOp(new StdBrOp(setMergeLabel));
      block = func.Body.AddBlock(setOkLabel);
    } else {
      EmitBoundsCheck(block, index, capacity, "__mm_panic_index_oob");
    }
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);

    if (isBitPacked) {
      // Bit-packed bool: read-modify-write a single bit
      // The value may be StdBool (i1) — convert to StdI64 (0 or 1) for EmitBitSet
      var rawValue = valueMap[op.Value];
      StdI64 value;
      if (rawValue is StdBool boolVal) {
        var oneConst = new StdConstI64Op(1);
        block.AddOp(oneConst);
        var zeroConst = new StdConstI64Op(0);
        block.AddOp(zeroConst);
        var selectOp = new StdSelectI64Op(boolVal, oneConst.Result, zeroConst.Result);
        block.AddOp(selectOp);
        value = selectOp.Result;
      } else {
        value = (StdI64)rawValue;
      }
      EmitBitSet(block, buffer, index, value);
    } else if (op.IsStructElement) {
      var addr = ComputeElementAddress(block, buffer, index, elemSize);
      // Struct elements are heap pointers — release the old reference with field cleanup before overwriting.
      // Old slot may be null (zeroed after remove), so use null-guarded decref.
      var oldElemLoad = new StdLoadIndirectOp(addr, 0, IrType.I64);
      block.AddOp(oldElemLoad);
      EmitDecrefValueIfNonnull(block, (StdI64)oldElemLoad.Result, scopeName: _currentFuncName);
      var srcName = ResolveManagedVarName(op.Value, valueMap);
      var srcHeapPtr = EmitLoad(block, srcName, varTypes);
      block.AddOp(new StdStoreIndirectOp(srcHeapPtr, addr, 0, IrType.I64));
      EmitIncrefValue(block, (StdI64)srcHeapPtr, scopeName: _currentFuncName);
    } else {
      var addr = ComputeElementAddress(block, buffer, index, elemSize);
      // Scalar elements: store directly. Prefer the precise narrow storage type when
      // available so the codegen picks the right-width store (e.g. mov byte ptr for
      // int(0..100), not mov qword ptr — otherwise an 8-byte store overwrites the next
      // 7 elements when element_size is 1).
      var value = valueMap[op.Value];
      var elemType = op.ElementStorageType ?? GetManagedMemElementType(op.ElementKind, "LowerManagedMemSet");
      block.AddOp(new StdStoreIndirectOp(value, addr, 0, elemType));
    }

    if (setMergeLabel != null) {
      block.AddOp(new StdBrOp(setMergeLabel));
      block = func.Body.AddBlock(setMergeLabel);
    }
  }

  /// <summary>
  /// __managed_memory_create(count, elementSize): allocate heap buffer.
  /// Returns new __ManagedMemory struct (buffer, length, capacity, element_size).
  /// </summary>
  private static void LowerManagedMemCreate(
    MaxonManagedMemCreateOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps,
    string? inlineTarget = null,
    MaxonValue? errorFlagValue = null) {
    if (!op.IsBitPacked && op.ElementSize <= 0)
      throw new InvalidOperationException($"MaxonManagedMemCreateOp has invalid element_size={op.ElementSize} in func {_currentFuncName}");
    var count = (StdI64)valueMap[op.Count];

    // Validate count >= 0 — negative counts would wrap to huge unsigned sizes.
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    var isNeg = new StdCmpI64Op("lt", count, zero.Result);
    block.AddOp(isNeg);
    if (errorFlagValue != null) {
      // __ManagedMemoryError.invalidAllocation (ordinal 6) → flag 7
      EmitBoundsCheckErrorFlag(block, isNeg.Result, 7, valueMap, varTypes, errorFlagValue);
    } else {
      var oneForNegCheck = new StdConstI64Op(1);
      block.AddOp(oneForNegCheck);
      var asI64 = new StdSelectI64Op(isNeg.Result, oneForNegCheck.Result, zero.Result);
      block.AddOp(asI64);
      EmitBoundsCheck(block, asI64.Result, oneForNegCheck.Result, "__mm_panic_create_negative_count");
    }

    StdI64 byteSize;
    StdI64 elemSizeValue;
    if (op.IsBitPacked) {
      // Bit-packed bool: byte size = (count + 7) >> 3, element_size sentinel = 0
      byteSize = ComputeBitPackedByteSize(block, count);
      var zeroElemSize = new StdConstI64Op(0);
      block.AddOp(zeroElemSize);
      elemSizeValue = zeroElemSize.Result;
    } else {
      // Compute byte size = count * elementSize
      var sizeOp = new StdConstI64Op(op.ElementSize);
      block.AddOp(sizeOp);
      var byteSizeOp = new StdMulI64Op(count, sizeOp.Result);
      block.AddOp(byteSizeOp);
      byteSize = byteSizeOp.Result;
      elemSizeValue = sizeOp.Result;
    }

    // Allocate __ManagedMemory struct, then raw buffer
    var tempName = inlineTarget
      ?? temps.CreateTemp("managed_create", op.Result.Id, "__ManagedMemory", OwnershipFlags.None);
    var managedPtr = EmitAlloc(block, ManagedMemoryStructSize, "__ManagedMemory", scopeName: _currentFuncName);
    EmitStore(block, managedPtr, tempName, varTypes);
    var allocResult = EmitRawAlloc(block, byteSize, label: "ManagedMemory.buf", scopeName: _currentFuncName);
    var createParentZero = new StdConstI64Op(0);
    block.AddOp(createParentZero);
    EmitInitManagedMemory(block, tempName, allocResult, count, count, elemSizeValue, createParentZero.Result, varTypes);
    valueMap[op.Result] = new StdHeapPtr(managedPtr.Id, "__ManagedMemory", tempName);
  }

  /// <summary>
  /// __managed_memory_grow(managed, newCapacity): grow heap buffer to new capacity.
  /// Uses realloc to grow (or allocate) the buffer, then updates managed struct fields.
  /// Element size is read from the managed struct's element_size field.
  /// </summary>
  private static void LowerManagedMemGrow(
    MaxonManagedMemGrowOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue = null) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);

    // Load element_size from the managed struct via heap pointer
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, IrType.I64, varTypes);

    // Validate newCapacity >= currentCapacity (before COW check, which may change capacity)
    // Skip check when capacity < 0 (rdata/slice — not a real capacity value)
    var oldCap = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    var newCap = (StdI64)valueMap[op.NewCapacity];
    var clampedOldCap = EmitClampCapacityNonNeg(block, oldCap);
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var newCapPlusOne = new StdAddI64Op(newCap, oneConst.Result);
    block.AddOp(newCapPlusOne);
    string? growMergeLabel = null;
    if (errorFlagValue != null) {
      // __ManagedMemoryError.invalidCapacity (ordinal 3) → flag 4 when shrinking
      var isError = new StdCmpU64Op("uge", clampedOldCap, newCapPlusOne.Result);
      block.AddOp(isError);
      EmitBoundsCheckErrorFlag(block, isError.Result, 4, valueMap, varTypes, errorFlagValue);
      // Skip the realloc on error — shrinking would corrupt outstanding pointers.
      var growUid = IrContext.Current.NextId();
      var growErrLabel = $"__grow_err_{growUid}";
      var growOkLabel = $"__grow_ok_{growUid}";
      growMergeLabel = $"__grow_merge_{growUid}";
      block.AddOp(new StdCondBrOp(isError.Result, growErrLabel, growOkLabel));
      var growErrBlock = func.Body.AddBlock(growErrLabel);
      growErrBlock.AddOp(new StdBrOp(growMergeLabel));
      block = func.Body.AddBlock(growOkLabel);
    } else {
      EmitBoundsCheck(block, clampedOldCap, newCapPlusOne.Result, "__mm_panic_grow_shrink");
    }

    EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: op.IsBitPacked);

    // Load buffer pointer (now guaranteed to be heap-allocated after COW check)
    var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);

    // Compute new byte size
    StdI64 newByteSize;
    if (op.IsBitPacked) {
      // Bit-packed bool: byte size = (newCap + 7) >> 3
      newByteSize = ComputeBitPackedByteSize(block, newCap);
    } else {
      var newByteSizeOp = new StdMulI64Op(newCap, elemSize);
      block.AddOp(newByteSizeOp);
      newByteSize = newByteSizeOp.Result;
    }

    // Raw realloc: buffer has no refcount header (it's a raw HeapAlloc pointer)
    // Pass managedPtr as 3rd arg so mm_raw_realloc can emit trace output
    var growManagedPtr = (StdI64)EmitLoad(block, managedVarName, varTypes);
    var newBufferResult = new StdI64(IrContext.Current.NextStdId());
    block.AddOp(new StdCallRuntimeOp("mm_raw_realloc", [oldBuffer, newByteSize, growManagedPtr], newBufferResult));

    // Update managed struct fields through heap pointer
    var newBufReload = newBufferResult;
    EmitStructFieldStore(block, newBufReload, managedVarName, ManagedFieldBuffer, IrType.I64, varTypes);
    EmitStructFieldStore(block, newCap, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    // No write-through needed: with heap refs, all field stores go through
    // the heap pointer directly, so the caller sees changes automatically.

    if (growMergeLabel != null) {
      block.AddOp(new StdBrOp(growMergeLabel));
      block = func.Body.AddBlock(growMergeLabel);
    }
  }

  /// <summary>
  /// __managed_memory_shift_right/left(managed, index, count): shift elements in buffer.
  /// For shift_right: move elements [index..index+count-1] to [index+1..index+count] (backwards copy)
  /// For shift_left: move elements [index+1..index+count] to [index..index+count-1] (forward copy)
  /// Implemented as element-by-element copy using indirect load/store.
  /// Element size is read from the managed struct's element_size field.
  /// </summary>
  private static void LowerManagedMemShift(
    MaxonManagedMemShiftOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue = null) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, IrType.I64, varTypes);
    EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: op.IsBitPacked);
    // Check after COW (COW updates capacity from 0 to length)
    var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    var index = (StdI64)valueMap[op.Index];
    var count = (StdI64)valueMap[op.Count];
    var endOp = new StdAddI64Op(index, count);
    block.AddOp(endOp);
    if (errorFlagValue != null) {
      // __ManagedMemoryError.shiftOutOfBounds (ordinal 4) → flag 5; check end <= capacity
      var isError = new StdCmpU64Op("ugt", endOp.Result, capacity);
      block.AddOp(isError);
      EmitBoundsCheckErrorFlag(block, isError.Result, 5, valueMap, varTypes, errorFlagValue);
    } else {
      EmitBoundsCheck(block, index, capacity, "__mm_panic_shift_oob");
      EmitBoundsCheck(block, endOp.Result, capacity, "__mm_panic_shift_oob");
    }
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);

    if (op.IsBitPacked) {
      // Bit-packed bool: bit-by-bit loop
      var loopUid = IrContext.Current.NextId();
      var loopVar = $"__shift_i_{loopUid}";
      var bufVar = $"__shift_buf_{loopUid}";
      EmitStore(block, buffer, bufVar, varTypes);
      var countVar = $"__shift_count_{loopUid}";
      EmitStore(block, count, countVar, varTypes);
      var indexVar = $"__shift_idx_{loopUid}";
      EmitStore(block, index, indexVar, varTypes);

      if (op.ShiftRight) {
        // Shift right: for i from count-1 downto 0: bit[index+i+1] = bit[index+i]
        // Start i at count-1 and iterate while i >= 0
        var oneConst = new StdConstI64Op(1);
        block.AddOp(oneConst);
        var startI = new StdSubI64Op(count, oneConst.Result);
        block.AddOp(startI);
        EmitStore(block, startI.Result, loopVar, varTypes);

        var loopHeaderLabel = $"__shift_hdr_{loopUid}";
        var loopBodyLabel = $"__shift_body_{loopUid}";
        var loopExitLabel = $"__shift_exit_{loopUid}";
        block.AddOp(new StdBrOp(loopHeaderLabel));

        var headerBlock = func.Body.AddBlock(loopHeaderLabel);
        var iReload = (StdI64)EmitLoad(headerBlock, loopVar, varTypes);
        var zeroConst = new StdConstI64Op(0);
        headerBlock.AddOp(zeroConst);
        // i >= 0 => not (i < 0) => use signed >=
        var cmpLoop = new StdCmpI64Op("ge", iReload, zeroConst.Result);
        headerBlock.AddOp(cmpLoop);
        headerBlock.AddOp(new StdCondBrOp(cmpLoop.Result, loopBodyLabel, loopExitLabel));

        var bodyBlock = func.Body.AddBlock(loopBodyLabel);
        var iBody = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
        var idxBody = (StdI64)EmitLoad(bodyBlock, indexVar, varTypes);
        var bufBody = (StdI64)EmitLoad(bodyBlock, bufVar, varTypes);
        // srcBitIdx = index + i
        var srcBitIdx = new StdAddI64Op(idxBody, iBody);
        bodyBlock.AddOp(srcBitIdx);
        var bitVal = EmitBitGet(bodyBlock, bufBody, srcBitIdx.Result);
        // dstBitIdx = index + i + 1
        var bufBody2 = (StdI64)EmitLoad(bodyBlock, bufVar, varTypes);
        var idxBody2 = (StdI64)EmitLoad(bodyBlock, indexVar, varTypes);
        var iBody2 = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
        var oneBody = new StdConstI64Op(1);
        bodyBlock.AddOp(oneBody);
        var dstBitIdx = new StdAddI64Op(idxBody2, iBody2);
        bodyBlock.AddOp(dstBitIdx);
        var dstBitIdx2 = new StdAddI64Op(dstBitIdx.Result, oneBody.Result);
        bodyBlock.AddOp(dstBitIdx2);
        EmitBitSet(bodyBlock, bufBody2, dstBitIdx2.Result, bitVal);
        // Decrement i
        var iBody3 = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
        var oneDec = new StdConstI64Op(1);
        bodyBlock.AddOp(oneDec);
        var newI = new StdSubI64Op(iBody3, oneDec.Result);
        bodyBlock.AddOp(newI);
        EmitStore(bodyBlock, newI.Result, loopVar, varTypes);
        bodyBlock.AddOp(new StdBrOp(loopHeaderLabel));

        block = func.Body.AddBlock(loopExitLabel);
      } else {
        // Shift left: for i from 0 to count-1: bit[index+i] = bit[index+i+1]
        var zeroInit = new StdConstI64Op(0);
        block.AddOp(zeroInit);
        EmitStore(block, zeroInit.Result, loopVar, varTypes);

        var loopHeaderLabel = $"__shift_hdr_{loopUid}";
        var loopBodyLabel = $"__shift_body_{loopUid}";
        var loopExitLabel = $"__shift_exit_{loopUid}";
        block.AddOp(new StdBrOp(loopHeaderLabel));

        var headerBlock = func.Body.AddBlock(loopHeaderLabel);
        var iReload = (StdI64)EmitLoad(headerBlock, loopVar, varTypes);
        var countReload = (StdI64)EmitLoad(headerBlock, countVar, varTypes);
        var cmpLoop = new StdCmpI64Op("lt", iReload, countReload);
        headerBlock.AddOp(cmpLoop);
        headerBlock.AddOp(new StdCondBrOp(cmpLoop.Result, loopBodyLabel, loopExitLabel));

        var bodyBlock = func.Body.AddBlock(loopBodyLabel);
        var iBody = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
        var idxBody = (StdI64)EmitLoad(bodyBlock, indexVar, varTypes);
        var bufBody = (StdI64)EmitLoad(bodyBlock, bufVar, varTypes);
        // srcBitIdx = index + i + 1
        var oneBody = new StdConstI64Op(1);
        bodyBlock.AddOp(oneBody);
        var srcBitIdx = new StdAddI64Op(idxBody, iBody);
        bodyBlock.AddOp(srcBitIdx);
        var srcBitIdx2 = new StdAddI64Op(srcBitIdx.Result, oneBody.Result);
        bodyBlock.AddOp(srcBitIdx2);
        var bitVal = EmitBitGet(bodyBlock, bufBody, srcBitIdx2.Result);
        // dstBitIdx = index + i
        var bufBody2 = (StdI64)EmitLoad(bodyBlock, bufVar, varTypes);
        var idxBody2 = (StdI64)EmitLoad(bodyBlock, indexVar, varTypes);
        var iBody2 = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
        var dstBitIdx = new StdAddI64Op(idxBody2, iBody2);
        bodyBlock.AddOp(dstBitIdx);
        EmitBitSet(bodyBlock, bufBody2, dstBitIdx.Result, bitVal);
        // Increment i
        var iBody3 = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
        var oneInc = new StdConstI64Op(1);
        bodyBlock.AddOp(oneInc);
        var newI = new StdAddI64Op(iBody3, oneInc.Result);
        bodyBlock.AddOp(newI);
        EmitStore(bodyBlock, newI.Result, loopVar, varTypes);
        bodyBlock.AddOp(new StdBrOp(loopHeaderLabel));

        block = func.Body.AddBlock(loopExitLabel);
      }
    } else if (op.ShiftRight) {
      // Shift right: copy from [index+count-1] down to [index], moving each one position right
      // Effectively: for i in (count-1)..0: buffer[index+i+1] = buffer[index+i]
      // We implement this as a memcopy of count elements starting at index, shifted by +1
      var totalOffsetOp = new StdMulI64Op(index, elemSize);
      block.AddOp(totalOffsetOp);
      var srcAddr = new StdAddI64Op(buffer, totalOffsetOp.Result);
      block.AddOp(srcAddr);
      // Save srcAddr to a local before memcopy (rep movsb clobbers RSI/RDI/RCX)
      var srcAddrVarName = $"__shift_src_{IrContext.Current.NextId()}";
      EmitStore(block, srcAddr.Result, srcAddrVarName, varTypes);
      // Dest is src + elementSize (one position to the right)
      var dstAddr = new StdAddI64Op(srcAddr.Result, elemSize);
      block.AddOp(dstAddr);
      // Byte count
      var bytesOp = new StdMulI64Op(count, elemSize);
      block.AddOp(bytesOp);
      // Use reverse copy for overlapping shift-right (dst > src)
      block.AddOp(new StdMemCopyReverseOp(srcAddr.Result, dstAddr.Result, bytesOp.Result));
      // Zero the source slot at buffer[index] to prevent the subsequent set() from
      // decrefing a stale duplicate pointer (the original was copied to buffer[index+1])
      var reloadedSrcAddr = EmitLoad(block, srcAddrVarName, varTypes);
      var zeroOp = new StdConstI64Op(0);
      block.AddOp(zeroOp);
      block.AddOp(new StdStoreIndirectOp(zeroOp.Result, reloadedSrcAddr, 0, IrType.I64));
    } else {
      // Shift left: copy from [index+1] forward, moving each one position left
      var oneConst = new StdConstI64Op(1);
      block.AddOp(oneConst);
      var srcIndex = new StdAddI64Op(index, oneConst.Result);
      block.AddOp(srcIndex);
      var srcOffset = new StdMulI64Op(srcIndex.Result, elemSize);
      block.AddOp(srcOffset);
      var srcAddr = new StdAddI64Op(buffer, srcOffset.Result);
      block.AddOp(srcAddr);
      var dstOffset = new StdMulI64Op(index, elemSize);
      block.AddOp(dstOffset);
      var dstAddr = new StdAddI64Op(buffer, dstOffset.Result);
      block.AddOp(dstAddr);
      var bytesOp = new StdMulI64Op(count, elemSize);
      block.AddOp(bytesOp);
      // Spill dstAddr and byteCount to stack before memcopy — the copy operation
      // may consume these SSA values, and we need them again to zero the trailing slot.
      var dstAddrVarName = $"__shift_dst_{IrContext.Current.NextId()}";
      EmitStore(block, dstAddr.Result, dstAddrVarName, varTypes);
      var bytesVarName = $"__shift_bytes_{IrContext.Current.NextId()}";
      EmitStore(block, bytesOp.Result, bytesVarName, varTypes);
      block.AddOp(new StdMemCopyOp(srcAddr.Result, dstAddr.Result, bytesOp.Result));
      // Zero the trailing slot at buffer[index+count] to prevent stale duplicate
      // pointer from causing double-decref when the buffer is freed or reused
      var reloadedDstAddr = EmitLoad(block, dstAddrVarName, varTypes);
      var reloadedBytes = EmitLoad(block, bytesVarName, varTypes);
      var lastSlotAddr = new StdAddI64Op((StdI64)reloadedDstAddr, (StdI64)reloadedBytes);
      block.AddOp(lastSlotAddr);
      var zeroOp = new StdConstI64Op(0);
      block.AddOp(zeroOp);
      block.AddOp(new StdStoreIndirectOp(zeroOp.Result, lastSlotAddr.Result, 0, IrType.I64));
    }
  }

  /// <summary>
  /// __managed_memory_byte_at(managed, index): load a single byte from the managed buffer.
  /// Returns the byte zero-extended to i64.
  /// </summary>
  private static void LowerManagedMemByteGet(
    MaxonManagedMemByteGetOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue = null) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
    var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, IrType.I64, varTypes);
    var byteLimit = ComputeByteLimit(block, length, elemSize);
    var index = (StdI64)valueMap[op.Index];

    // Pre-allocate result temp seeded to 0 so the OOB path can supply a defined
    // value to the merge load without dereferencing the buffer.
    string? bgResultTemp = null;
    string? bgMergeLabel = null;
    if (errorFlagValue != null) {
      var bgUid = IrContext.Current.NextId();
      bgResultTemp = $"__byteat_result_{bgUid}";
      varTypes[bgResultTemp] = "i64";
      var bgSeedConst = new StdConstI64Op(0);
      block.AddOp(bgSeedConst);
      EmitStore(block, bgSeedConst.Result, bgResultTemp, varTypes);

      // __ManagedMemoryError.indexOutOfBounds (ordinal 0) → flag 1
      var isError = new StdCmpU64Op("uge", index, byteLimit);
      block.AddOp(isError);
      EmitBoundsCheckErrorFlag(block, isError.Result, 1, valueMap, varTypes, errorFlagValue);
      var bgErrLabel = $"__byteat_err_{bgUid}";
      var bgOkLabel = $"__byteat_ok_{bgUid}";
      bgMergeLabel = $"__byteat_merge_{bgUid}";
      block.AddOp(new StdCondBrOp(isError.Result, bgErrLabel, bgOkLabel));
      var bgErrBlock = func.Body.AddBlock(bgErrLabel);
      bgErrBlock.AddOp(new StdBrOp(bgMergeLabel));
      block = func.Body.AddBlock(bgOkLabel);
    } else {
      EmitBoundsCheck(block, index, byteLimit, "__mm_panic_byte_oob");
    }
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    // Compute address: buffer + index (element size is 1 byte)
    var addrOp = new StdAddI64Op(buffer, index);
    block.AddOp(addrOp);
    // byteAt returns an unsigned byte (0..255). Use U8 so codegen picks zero-extending
    // load — passing I8 would sign-extend bytes >= 128 to negative i64 values, breaking
    // UTF-8 decoders that compare bytes against 128/224/240.
    var loadOp = new StdLoadIndirectOp(addrOp.Result, 0, IrType.U8);
    block.AddOp(loadOp);
    if (bgResultTemp != null) {
      EmitStore(block, loadOp.Result, bgResultTemp, varTypes);
    } else {
      valueMap[op.Result] = loadOp.Result;
    }

    if (bgMergeLabel != null && bgResultTemp != null) {
      block.AddOp(new StdBrOp(bgMergeLabel));
      block = func.Body.AddBlock(bgMergeLabel);
      var bgMergedLoad = (StdI64)EmitLoad(block, bgResultTemp, varTypes);
      valueMap[op.Result] = bgMergedLoad;
    }
  }

  /// <summary>
  /// Emit a COW (copy-on-write) check for a managed memory struct.
  /// If capacity < 0, the buffer is read-only (rdata/slice) and must be copied to a writable heap allocation.
  /// Updates buffer and capacity fields on the managed struct (and writes through to self if needed).
  /// Element size is passed dynamically (read from the struct's element_size field).
  /// </summary>
  private static void EmitCowCheck(
    IrBlock<StandardOp> block,
    string managedVarName,
    Dictionary<string, string> varTypes,
    StdI64 elemSize,
    bool isBitPacked = false) {
    var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);

    var uid = IrContext.Current.NextId();
    var cowLenVar = $"__cow_len_{uid}";
    EmitStore(block, length, cowLenVar, varTypes);
    var cowCapVar = $"__cow_cap_{uid}";
    EmitStore(block, capacity, cowCapVar, varTypes);
    var cowBufVar = $"__cow_buf_{uid}";
    var cowOldBufSave = LoadManagedBuffer(block, managedVarName, varTypes);
    EmitStore(block, cowOldBufSave, cowBufVar, varTypes);

    var managedPtr = (StdI64)EmitLoad(block, managedVarName, varTypes);
    // Compute byteLen so we can pass it as a single arg to the runtime
    StdI64 byteLen;
    if (isBitPacked) {
      // Bit-packed bool: byteLen = (length + 7) >> 3
      byteLen = ComputeBitPackedByteSize(block, length);
    } else {
      var byteLenOp = new StdMulI64Op(length, elemSize);
      block.AddOp(byteLenOp);
      byteLen = byteLenOp.Result;
    }

    // Buffer-level COW for rdata (capacity==-2) and slice (capacity==-1) structs.
    // maxon_cow_check allocates a new buffer and copies data if capacity < 0.
    // For owned buffers (capacity >= 0), returns the existing buffer unchanged.
    var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var newBuffer = new StdI64(IrContext.Current.NextStdId());
    // Args: buffer, capacity, byteLen, managedPtr (4 register args, no stack args)
    block.AddOp(new StdCallRuntimeOp("maxon_cow_check", [oldBuffer, capacity, byteLen, managedPtr], newBuffer));

    EmitStructFieldStore(block, newBuffer, managedVarName, ManagedFieldBuffer, IrType.I64, varTypes);

    // If COW actually happened (buffer changed), update capacity and parentPtr.
    // cow_check skips COW when byteLen=0, returning the old buffer unchanged.
    // In that case, capacity must stay as-is (rdata/slice sentinel).
    var origCap = (StdI64)EmitLoad(block, cowCapVar, varTypes);
    var cowOldBuf = (StdI64)EmitLoad(block, cowBufVar, varTypes);
    var cowDidCopy = new StdCmpI64Op("ne", cowOldBuf, newBuffer);
    block.AddOp(cowDidCopy);
    var lenReload = (StdI64)EmitLoad(block, cowLenVar, varTypes);
    var capAfterCow = new StdSelectI64Op(cowDidCopy.Result, lenReload, origCap);
    block.AddOp(capAfterCow);
    EmitStructFieldStore(block, capAfterCow.Result, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);

    // If COW happened and original was a slice, decref parentPtr and zero it
    var negOneConst = new StdConstI64Op(-1);
    block.AddOp(negOneConst);
    var wasSlice = new StdCmpI64Op("eq", origCap, negOneConst.Result);
    block.AddOp(wasSlice);
    // Only act on slice cleanup if COW actually copied (AND both conditions)
    var wasSliceAndCopied = new StdAndI1Op(cowDidCopy.Result, wasSlice.Result);
    block.AddOp(wasSliceAndCopied);
    var parentPtr = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldParentPtr, IrType.I64, varTypes);
    var zeroPtr = new StdConstI64Op(0);
    block.AddOp(zeroPtr);
    var parentOrNull = new StdSelectI64Op(wasSliceAndCopied.Result, parentPtr, zeroPtr.Result);
    block.AddOp(parentOrNull);
    EmitDecrefValueIfNonnull(block, parentOrNull.Result, scopeName: _currentFuncName);
    var parentAfter = new StdSelectI64Op(wasSliceAndCopied.Result, zeroPtr.Result, parentPtr);
    block.AddOp(parentAfter);
    EmitStructFieldStore(block, parentAfter.Result, managedVarName, ManagedFieldParentPtr, IrType.I64, varTypes);
  }

  /// <summary>
  /// Lowers MaxonByteRangePanicOp: panics via the named panic symdata if end > capacity.
  /// Used by socket/file/directory builtins that pass a pointer+length range into a
  /// raw buffer and must not read OOB. Reuses maxon_bounds_check: we frame the check
  /// as "violation = (end > capacity) ? 1 : 0; panic if violation >= 1".
  /// </summary>
  private static void LowerByteRangePanic(
    MaxonByteRangePanicOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {
    var end = (StdI64)valueMap[op.End];
    var capacity = (StdI64)valueMap[op.Capacity];
    // Violation predicate: end > capacity (unsigned).
    var isError = new StdCmpU64Op("ugt", end, capacity);
    block.AddOp(isError);
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    var one = new StdConstI64Op(1);
    block.AddOp(one);
    var asI64 = new StdSelectI64Op(isError.Result, one.Result, zero.Result);
    block.AddOp(asI64);
    EmitBoundsCheck(block, asI64.Result, one.Result, op.PanicLabel);
  }

  /// <summary>
  /// __managed_memory_set_byte(managed, index, value): store a single byte to the managed buffer.
  /// Performs COW check before writing. Element size is read from the struct for COW allocation.
  /// </summary>
  private static void LowerManagedMemByteSet(
    MaxonManagedMemByteSetOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue = null) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
    var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, IrType.I64, varTypes);
    var byteLimit = ComputeByteLimit(block, length, elemSize);
    var index = (StdI64)valueMap[op.Index];
    string? bsMergeLabel = null;
    if (errorFlagValue != null) {
      // __ManagedMemoryError.indexOutOfBounds (ordinal 0) → flag 1
      var isError = new StdCmpU64Op("uge", index, byteLimit);
      block.AddOp(isError);
      EmitBoundsCheckErrorFlag(block, isError.Result, 1, valueMap, varTypes, errorFlagValue);
      var bsUid = IrContext.Current.NextId();
      var bsErrLabel = $"__bs_err_{bsUid}";
      var bsOkLabel = $"__bs_ok_{bsUid}";
      bsMergeLabel = $"__bs_merge_{bsUid}";
      block.AddOp(new StdCondBrOp(isError.Result, bsErrLabel, bsOkLabel));
      var bsErrBlock = func.Body.AddBlock(bsErrLabel);
      bsErrBlock.AddOp(new StdBrOp(bsMergeLabel));
      block = func.Body.AddBlock(bsOkLabel);
    } else {
      EmitBoundsCheck(block, index, byteLimit, "__mm_panic_byte_oob");
    }
    // ByteGet/ByteSet operate on raw bytes, not logical elements, so COW uses elemSize directly.
    // For bit-packed arrays (elemSize==0), the runtime's maxon_cow_check handles capacity==-2 correctly.
    EmitCowCheck(block, managedVarName, varTypes, elemSize);

    // Now perform the actual byte write using the writable buffer
    var bufReload = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldBuffer, IrType.I64, varTypes);
    var value = valueMap[op.Value];
    var addrOp = new StdAddI64Op(bufReload, index);
    block.AddOp(addrOp);
    block.AddOp(new StdStoreIndirectOp(value, addrOp.Result, 0, IrType.I8));

    if (bsMergeLabel != null) {
      block.AddOp(new StdBrOp(bsMergeLabel));
      block = func.Body.AddBlock(bsMergeLabel);
    }
  }

  /// <summary>
  /// __cstring_to_managed(cstrPtr): convert a null-terminated C string to __ManagedMemory.
  /// Computes strlen, allocates buffer, copies bytes, returns managed struct.
  /// </summary>
  /// Converts a raw cstring pointer to a __ManagedMemory struct. Used both by
  /// MaxonCStringToManagedOp lowering and directly by directory builtins.
  internal static StdHeapPtr LowerCStringToManagedCore(
    StdI64 cstrPtr,
    int resultId,
    IrBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    VarRegistry temps,
    string? inlineTarget = null) {
    // Get string length
    var lenResult = new StdI64(IrContext.Current.NextStdId());
    block.AddOp(new StdCallRuntimeOp("maxon_strlen", [cstrPtr], lenResult));

    // Store length so it survives alloc calls
    var lenVar = $"__cstr_len_{resultId}";
    EmitStore(block, lenResult, lenVar, varTypes);
    var cstrVar = $"__cstr_ptr_{resultId}";
    EmitStore(block, cstrPtr, cstrVar, varTypes);

    // Allocate __ManagedMemory struct, then raw buffer.
    var tempName = inlineTarget
      ?? temps.CreateTemp("from_cstring", resultId, "__ManagedMemory", OwnershipFlags.None);
    var managedPtr = EmitAlloc(block, ManagedMemoryStructSize, "__ManagedMemory", scopeName: _currentFuncName);
    EmitStore(block, managedPtr, tempName, varTypes);

    var lenReload1 = (StdI64)EmitLoad(block, lenVar, varTypes);
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var allocSize = new StdAddI64Op(lenReload1, oneConst.Result);
    block.AddOp(allocSize);
    var allocResult = EmitRawAlloc(block, allocSize.Result, label: "CString.buf", scopeName: _currentFuncName);

    var bufVar = $"__cstr_buf_{resultId}";
    EmitStore(block, allocResult, bufVar, varTypes);

    var bufReload = (StdI64)EmitLoad(block, bufVar, varTypes);
    var cstrReload = (StdI64)EmitLoad(block, cstrVar, varTypes);
    var lenReload2 = (StdI64)EmitLoad(block, lenVar, varTypes);
    var copySize = new StdAddI64Op(lenReload2, oneConst.Result);
    block.AddOp(copySize);
    var copyResult = new StdI64(IrContext.Current.NextStdId());
    block.AddOp(new StdCallRuntimeOp("maxon_memcpy", [bufReload, cstrReload, copySize.Result], copyResult));

    var bufFinal = (StdI64)EmitLoad(block, bufVar, varTypes);
    var lenFinal = (StdI64)EmitLoad(block, lenVar, varTypes);
    var capOp = new StdAddI64Op(lenFinal, oneConst.Result);
    block.AddOp(capOp);
    var elemSizeOp = new StdConstI64Op(1);
    block.AddOp(elemSizeOp);
    var cstrParentZero = new StdConstI64Op(0);
    block.AddOp(cstrParentZero);
    EmitInitManagedMemory(block, tempName, bufFinal, lenFinal, capOp.Result, elemSizeOp.Result, cstrParentZero.Result, varTypes);
    return new StdHeapPtr(managedPtr.Id, "__ManagedMemory", tempName);
  }

  private static void LowerCStringToManaged(
    MaxonCStringToManagedOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps,
    string? inlineTarget = null) {
    var cstrPtr = (StdI64)valueMap[op.CstrPtr];
    var hp = LowerCStringToManagedCore(cstrPtr, op.Result.Id, block, varTypes, temps, inlineTarget);
    valueMap[op.Result] = hp;
  }

  /// <summary>
  /// __managed_memory_to_cstring(managed): return a null-terminated C string pointer.
  /// Calls maxon_to_cstring runtime which checks if buffer[length] is already '\0'.
  /// If so, returns the buffer directly (no allocation). Otherwise, allocates a copy
  /// with null terminator appended. This avoids unnecessary copying for non-slice strings.
  /// </summary>
  private static void LowerManagedToCString(
    MaxonManagedToCStringOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {
    var managedVarName = ResolveManagedVarName(op.Managed, valueMap);

    // Ensure the buffer is null-terminated without leaking a temporary copy.
    // For zero-copy slices, buffer[length] is often not '\0'.
    // Strategy: check if already terminated. If not, COW the managed struct to get
    // an owned buffer, grow it by 1 byte for the null terminator, and write '\0'.
    // The managed struct then owns the null-terminated buffer — no separate allocation.
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);

    // Check: buffer[length] == '\0'? Use unsigned byte for consistency with byteAt
    // semantics (raw byte buffers are conceptually u8).
    var uid = IrContext.Current.NextId();
    var termAddr2 = new StdAddI64Op(buffer, length);
    block.AddOp(termAddr2);
    var termByte = new StdLoadIndirectOp(termAddr2.Result, 0, IrType.U8);
    block.AddOp(termByte);
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var isTerminated = new StdCmpI64Op("eq", (StdI64)termByte.Result, zeroConst.Result);
    block.AddOp(isTerminated);

    var alreadyTermLabel = $"__cstr_ok_{uid}";
    var needTermLabel = $"__cstr_fix_{uid}";
    var doneLabel = $"__cstr_done_{uid}";
    block.AddOp(new StdCondBrOp(isTerminated.Result, alreadyTermLabel, needTermLabel));

    // --- already terminated: result = buffer ---
    var okBlock = func.Body.AddBlock(alreadyTermLabel);
    var okBuf = LoadManagedBuffer(okBlock, managedVarName, varTypes);
    var okBufVar = $"__cstr_buf_{uid}";
    EmitStore(okBlock, okBuf, okBufVar, varTypes);
    okBlock.AddOp(new StdBrOp(doneLabel));

    // --- not terminated: COW + ensure capacity + write null ---
    var fixBlock = func.Body.AddBlock(needTermLabel);
    // COW to get an owned buffer (handles rdata and slice cases)
    var elemSize = (StdI64)EmitStructFieldLoad(fixBlock, managedVarName, ManagedFieldElementSize, IrType.I64, varTypes);
    EmitCowCheck(fixBlock, managedVarName, varTypes, elemSize);
    // Ensure capacity >= length + 1
    var fixBuf = LoadManagedBuffer(fixBlock, managedVarName, varTypes);
    var fixLen = (StdI64)EmitStructFieldLoad(fixBlock, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    var fixCap = (StdI64)EmitStructFieldLoad(fixBlock, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    var oneConst = new StdConstI64Op(1);
    fixBlock.AddOp(oneConst);
    var requiredCap = new StdAddI64Op(fixLen, oneConst.Result);
    fixBlock.AddOp(requiredCap);
    var grownBuf = new StdI64(IrContext.Current.NextStdId());
    fixBlock.AddOp(new StdCallRuntimeOp("maxon_string_ensure_cap", [fixBuf, fixLen, fixCap, requiredCap.Result], grownBuf));
    EmitStructFieldStore(fixBlock, grownBuf, managedVarName, ManagedFieldBuffer, IrType.I64, varTypes);
    EmitStructFieldStore(fixBlock, requiredCap.Result, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    // Write null terminator: buffer[length] = 0
    var fixLenReload = (StdI64)EmitStructFieldLoad(fixBlock, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    var termAddr = new StdAddI64Op(grownBuf, fixLenReload);
    fixBlock.AddOp(termAddr);
    var zeroByte = new StdConstI64Op(0);
    fixBlock.AddOp(zeroByte);
    fixBlock.AddOp(new StdStoreIndirectOp(zeroByte.Result, termAddr.Result, 0, IrType.I8));
    EmitStore(fixBlock, grownBuf, okBufVar, varTypes);
    fixBlock.AddOp(new StdBrOp(doneLabel));

    // --- done: load result ---
    block = func.Body.AddBlock(doneLabel);
    var result = (StdI64)EmitLoad(block, okBufVar, varTypes);
    valueMap[op.Result] = result;
  }

  /// <summary>
  /// Write managed memory buffer to a stream via runtime call with (buffer, length) args.
  /// Extracts buffer pointer and length from the managed struct, avoiding cstring conversion.
  /// </summary>
  private static void LowerManagedWrite(
    string runtimeName,
    MaxonValue managedValue,
    MaxonValue resultValue,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {
    var managedVarName = ResolveManagedVarName(managedValue, valueMap);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    var result = new StdI64(IrContext.Current.NextStdId());
    block.AddOp(new StdCallRuntimeOp(runtimeName, [buffer, length], result));
    valueMap[resultValue] = result;
  }

  private static void LowerManagedWriteStdout(
    MaxonManagedWriteStdoutOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) =>
    LowerManagedWrite("maxon_managed_write_stdout", op.Managed, op.Result, block, valueMap, varTypes);

  private static void LowerManagedWriteStderr(
    MaxonManagedWriteStderrOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) =>
    LowerManagedWrite("maxon_managed_write_stderr", op.Managed, op.Result, block, valueMap, varTypes);

  /// <summary>
  /// Set length with capacity validation: panics if newLength > capacity.
  /// </summary>
  private static void LowerManagedMemSetLength(
    MaxonManagedMemSetLengthOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue = null) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
    var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    var newLength = (StdI64)valueMap[op.NewLength];
    // Check newLength <= capacity: reframe as newLength < capacity + 1
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var capPlusOne = new StdAddI64Op(capacity, oneConst.Result);
    block.AddOp(capPlusOne);
    string? slMergeLabel = null;
    if (errorFlagValue != null) {
      // __ManagedMemoryError.invalidLength (ordinal 2) → flag 3
      var isError = new StdCmpU64Op("uge", newLength, capPlusOne.Result);
      block.AddOp(isError);
      EmitBoundsCheckErrorFlag(block, isError.Result, 3, valueMap, varTypes, errorFlagValue);
      // Skip the store on error so a bad setLength doesn't leave the array
      // with length > capacity (which would make subsequent get() read past
      // the allocated buffer).
      var slUid = IrContext.Current.NextId();
      var slErrLabel = $"__setlen_err_{slUid}";
      var slOkLabel = $"__setlen_ok_{slUid}";
      slMergeLabel = $"__setlen_merge_{slUid}";
      block.AddOp(new StdCondBrOp(isError.Result, slErrLabel, slOkLabel));
      var slErrBlock = func.Body.AddBlock(slErrLabel);
      slErrBlock.AddOp(new StdBrOp(slMergeLabel));
      block = func.Body.AddBlock(slOkLabel);
    } else {
      EmitBoundsCheck(block, newLength, capPlusOne.Result, "__mm_panic_setlength_oob");
    }
    // Store the new length
    EmitStructFieldStore(block, newLength, managedVarName, ManagedFieldLength, IrType.I64, varTypes);

    if (slMergeLabel != null) {
      block.AddOp(new StdBrOp(slMergeLabel));
      block = func.Body.AddBlock(slMergeLabel);
    }
  }

  /// <summary>
  /// Clear all elements: decref each struct element, then set length to 0.
  /// For primitive elements, simply sets length to 0.
  /// </summary>
  private static void LowerManagedMemClear(
    MaxonManagedMemClearOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);

    if (op.IsStructElement) {
      // Decref each struct element and zero the buffer slots — uses the runtime
      // loop that walks the buffer, decrefs each non-null 8-byte heap pointer,
      // and zeroes the slot to prevent stale-pointer double-decref on reuse.
      var managedPtr = (StdI64)EmitLoad(block, managedVarName, varTypes);
      block.AddOp(new StdCallRuntimeOp("mm_clear_managed_elements", [managedPtr], null));
    }

    // Mark array as empty after element cleanup
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    EmitStructFieldStore(block, zeroConst.Result, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
  }

  private static void LowerPanic(
    MaxonPanicOp op,
    IrBlock<StandardOp> block,
    IrModule<StandardOp> result) {
    // MaxonPanicOp deduplicates labels by message content, so skip if already emitted
    if (!result.SymdataEntries.Any(e => e.label == op.SymdataLabel)) {
      var bytes = System.Text.Encoding.UTF8.GetBytes(op.Message + "\n");
      var cstrBytes = new byte[bytes.Length + 1]; // null-terminated
      bytes.CopyTo(cstrBytes, 0);
      result.SymdataEntries.Add((op.SymdataLabel, cstrBytes, 1));
    }
    // LEA to get pointer to the message
    var leaOp = new StdLeaSymdataOp(op.SymdataLabel);
    block.AddOp(leaOp);
    var ptrToI64 = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrToI64);
    block.AddOp(new StdCallRuntimeOp("mrt_panic", [ptrToI64.Result], null));
  }

  private static void LowerPanicDynamic(
    MaxonPanicDynamicOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {
    // The interpolated String struct has _managed (a __ManagedMemory pointer) at field 0.
    // We need two levels of dereference: String -> __ManagedMemory -> buffer.
    // The buffer is null-terminated from LowerStringInterp.
    var stringVarName = ResolveManagedVarName(op.MessageStruct, valueMap);
    // Load field 0 of String = __ManagedMemory pointer
    var managedPtr = (StdI64)EmitStructFieldLoad(block, stringVarName, 0, IrType.I64, varTypes);
    // Store to temp so we can load fields from it
    var managedTempVar = $"__panic_managed_{IrContext.Current.NextId()}";
    EmitStore(block, managedPtr, managedTempVar, varTypes);
    // Load field 0 of __ManagedMemory = raw buffer pointer (C string)
    var buffer = (StdI64)EmitStructFieldLoad(block, managedTempVar, ManagedFieldBuffer, IrType.I64, varTypes);
    block.AddOp(new StdCallRuntimeOp("mrt_panic", [buffer], null));
  }

  /// <summary>
  /// Append another __ManagedMemory buffer's data to self in-place.
  /// Grows the buffer if needed using maxon_string_ensure_cap (which handles COW for capacity=0).
  /// For struct elements, increfs the copied pointers.
  /// </summary>
  private static void LowerManagedMemAppend(
    MaxonManagedMemAppendOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {

    var selfVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
    var otherVarName = ResolveManagedVarName(op.Other, valueMap);
    var uid = IrContext.Current.NextId();

    var otherLen = (StdI64)EmitStructFieldLoad(block, otherVarName, ManagedFieldLength, IrType.I64, varTypes);
    var otherLenVar = $"__append_otherlen_{uid}";
    EmitStore(block, otherLen, otherLenVar, varTypes);

    // Skip append if other is empty
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var isEmpty = new StdCmpI64Op("eq", otherLen, zeroConst.Result);
    block.AddOp(isEmpty);
    var skipLabel = $"__append_skip_{uid}";
    var doAppendLabel = $"__append_do_{uid}";
    block.AddOp(new StdCondBrOp(isEmpty.Result, skipLabel, doAppendLabel));

    var appendBlock = func.Body.AddBlock(doAppendLabel);

    if (op.IsBitPacked) {
      // Bit-packed bool append: bit-by-bit copy loop
      var selfBuf = LoadManagedBuffer(appendBlock, selfVarName, varTypes);
      var selfLen = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldLength, IrType.I64, varTypes);
      var selfCap = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldCapacity, IrType.I64, varTypes);
      var otherBuf = LoadManagedBuffer(appendBlock, otherVarName, varTypes);
      var otherLenReload = (StdI64)EmitLoad(appendBlock, otherLenVar, varTypes);

      // Compute new total length
      var totalLen = new StdAddI64Op(selfLen, otherLenReload);
      appendBlock.AddOp(totalLen);
      var totalByteSize = ComputeBitPackedByteSize(appendBlock, totalLen.Result);

      // Ensure capacity (byte-level for bit-packed: use byte sizes)
      var clampedCap = EmitClampCapacityNonNeg(appendBlock, selfCap);
      var selfByteSize = ComputeBitPackedByteSize(appendBlock, selfLen);
      var selfCapBytes = ComputeBitPackedByteSize(appendBlock, clampedCap);

      var growCap = EmitGrowCapacity(appendBlock, totalByteSize, selfCapBytes);
      // requiredCap = totalByteSize + 1 (used later to check if growth occurred)
      var oneConst = new StdConstI64Op(1);
      appendBlock.AddOp(oneConst);
      var requiredCap = new StdAddI64Op(totalByteSize, oneConst.Result);
      appendBlock.AddOp(requiredCap);

      // Pass original (unclamped) capacity so ensure_cap correctly skips free for rdata/slice.
      // For bit-packed, selfCap is in elements but ensure_cap only checks sign, so passing
      // the raw element capacity (which is -2 or -1 for rdata/slice) works correctly.
      var newBuf = new StdI64(IrContext.Current.NextStdId());
      appendBlock.AddOp(new StdCallRuntimeOp("maxon_string_ensure_cap",
        [selfBuf, selfByteSize, selfCap, growCap], newBuf));

      // Spill values for the loop
      var newBufVar = $"__append_buf_{uid}";
      EmitStore(appendBlock, newBuf, newBufVar, varTypes);
      var otherBufVar = $"__append_otherbuf_{uid}";
      EmitStore(appendBlock, otherBuf, otherBufVar, varTypes);
      var selfLenVar = $"__append_selflen_{uid}";
      EmitStore(appendBlock, selfLen, selfLenVar, varTypes);
      var loopVar = $"__append_i_{uid}";
      var zeroInit = new StdConstI64Op(0);
      appendBlock.AddOp(zeroInit);
      EmitStore(appendBlock, zeroInit.Result, loopVar, varTypes);

      var loopHeaderLabel = $"__append_hdr_{uid}";
      var loopBodyLabel = $"__append_body_{uid}";
      var loopExitLabel = $"__append_exit_{uid}";
      appendBlock.AddOp(new StdBrOp(loopHeaderLabel));

      // Loop header: while i < otherLen
      var headerBlock = func.Body.AddBlock(loopHeaderLabel);
      var iReload = (StdI64)EmitLoad(headerBlock, loopVar, varTypes);
      var otherLenLoop = (StdI64)EmitLoad(headerBlock, otherLenVar, varTypes);
      var cmpLoop = new StdCmpI64Op("lt", iReload, otherLenLoop);
      headerBlock.AddOp(cmpLoop);
      headerBlock.AddOp(new StdCondBrOp(cmpLoop.Result, loopBodyLabel, loopExitLabel));

      // Loop body: get bit i from other, set bit (selfLen + i) in dest
      var bodyBlock = func.Body.AddBlock(loopBodyLabel);
      var iBody = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
      var otherBufBody = (StdI64)EmitLoad(bodyBlock, otherBufVar, varTypes);
      var bitVal = EmitBitGet(bodyBlock, otherBufBody, iBody);
      var dstBufBody = (StdI64)EmitLoad(bodyBlock, newBufVar, varTypes);
      var selfLenBody = (StdI64)EmitLoad(bodyBlock, selfLenVar, varTypes);
      var dstIdx = new StdAddI64Op(selfLenBody, iBody);
      bodyBlock.AddOp(dstIdx);
      EmitBitSet(bodyBlock, dstBufBody, dstIdx.Result, bitVal);
      // Increment loop counter
      var iInc = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
      var oneInc = new StdConstI64Op(1);
      bodyBlock.AddOp(oneInc);
      var newI = new StdAddI64Op(iInc, oneInc.Result);
      bodyBlock.AddOp(newI);
      EmitStore(bodyBlock, newI.Result, loopVar, varTypes);
      bodyBlock.AddOp(new StdBrOp(loopHeaderLabel));

      block = func.Body.AddBlock(loopExitLabel);
      var finalBuf = (StdI64)EmitLoad(block, newBufVar, varTypes);
      EmitStructFieldStore(block, finalBuf, selfVarName, ManagedFieldBuffer, IrType.I64, varTypes);
      EmitStructFieldStore(block, totalLen.Result, selfVarName, ManagedFieldLength, IrType.I64, varTypes);
      // Update capacity: use totalLen if grew (conservative)
      var grewCmp = new StdCmpU64Op("ugt", requiredCap.Result, selfCapBytes);
      block.AddOp(grewCmp);
      var newCap = new StdSelectI64Op(grewCmp.Result, totalLen.Result, selfCap);
      block.AddOp(newCap);
      EmitStructFieldStore(block, newCap.Result, selfVarName, ManagedFieldCapacity, IrType.I64, varTypes);
      block.AddOp(new StdBrOp(skipLabel));
    } else {
      // Regular element append: use element_size for byte calculations
      var selfBuf = LoadManagedBuffer(appendBlock, selfVarName, varTypes);
      var selfLen = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldLength, IrType.I64, varTypes);
      var selfCap = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldCapacity, IrType.I64, varTypes);
      var elemSize = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldElementSize, IrType.I64, varTypes);
      var otherBuf = LoadManagedBuffer(appendBlock, otherVarName, varTypes);
      var otherLenReload = (StdI64)EmitLoad(appendBlock, otherLenVar, varTypes);

      // Spill values that are needed after ensure_cap call (which clobbers registers)
      var selfLenVar = $"__append_selflen_{uid}";
      var selfCapVar = $"__append_selfcap_{uid}";
      var selfBufVar = $"__append_selfbuf_{uid}";
      var elemSizeVar = $"__append_elemsize_{uid}";
      var otherBufVar = $"__append_otherbuf_{uid}";
      EmitStore(appendBlock, selfLen, selfLenVar, varTypes);
      EmitStore(appendBlock, selfCap, selfCapVar, varTypes);
      EmitStore(appendBlock, selfBuf, selfBufVar, varTypes);
      EmitStore(appendBlock, elemSize, elemSizeVar, varTypes);
      EmitStore(appendBlock, otherBuf, otherBufVar, varTypes);

      // Compute new total length (in elements)
      var totalLen = new StdAddI64Op(selfLen, otherLenReload);
      appendBlock.AddOp(totalLen);
      var totalLenVar = $"__append_totallen_{uid}";
      EmitStore(appendBlock, totalLen.Result, totalLenVar, varTypes);

      // Convert to bytes for ensure_cap
      var selfLenBytes = new StdMulI64Op(selfLen, elemSize);
      appendBlock.AddOp(selfLenBytes);
      var clampedCap = EmitClampCapacityNonNeg(appendBlock, selfCap);
      var selfCapBytes = new StdMulI64Op(clampedCap, elemSize);
      appendBlock.AddOp(selfCapBytes);
      var selfCapBytesVar = $"__append_capbytes_{uid}";
      EmitStore(appendBlock, selfCapBytes.Result, selfCapBytesVar, varTypes);
      var totalLenBytes = new StdMulI64Op(totalLen.Result, elemSize);
      appendBlock.AddOp(totalLenBytes);

      var growByteCap = EmitGrowCapacity(appendBlock, totalLenBytes.Result, selfCapBytes.Result);
      var growByteCapVar = $"__append_growcap_{uid}";
      EmitStore(appendBlock, growByteCap, growByteCapVar, varTypes);
      // requiredByteCap = totalLenBytes + 1 (used later to check if growth occurred)
      var oneConst = new StdConstI64Op(1);
      appendBlock.AddOp(oneConst);
      var requiredByteCap = new StdAddI64Op(totalLenBytes.Result, oneConst.Result);
      appendBlock.AddOp(requiredByteCap);
      var reqByteCapVar = $"__append_reqcap_{uid}";
      EmitStore(appendBlock, requiredByteCap.Result, reqByteCapVar, varTypes);

      // Call maxon_string_ensure_cap(buffer, lengthBytes, capacity, growByteCap) -> newBuffer
      // Pass original (unclamped) capacity so ensure_cap correctly skips free for rdata/slice.
      // ensure_cap only checks the sign of capacity, so element-based values work fine.
      var callBuf = (StdI64)EmitLoad(appendBlock, selfBufVar, varTypes);
      var callLen = selfLenBytes.Result;
      var callCap = selfCap;
      var callGrow = (StdI64)EmitLoad(appendBlock, growByteCapVar, varTypes);
      var newBuf = new StdI64(IrContext.Current.NextStdId());
      appendBlock.AddOp(new StdCallRuntimeOp("maxon_string_ensure_cap",
        [callBuf, callLen, callCap, callGrow], newBuf));
      var newBufVar = $"__append_buf_{uid}";
      EmitStore(appendBlock, newBuf, newBufVar, varTypes);

      // Memcpy: other.buffer -> newBuffer + selfLen * elemSize
      var reloadSelfLen = (StdI64)EmitLoad(appendBlock, selfLenVar, varTypes);
      var reloadElemSize = (StdI64)EmitLoad(appendBlock, elemSizeVar, varTypes);
      var offsetBytes = new StdMulI64Op(reloadSelfLen, reloadElemSize);
      appendBlock.AddOp(offsetBytes);
      var reloadNewBuf = (StdI64)EmitLoad(appendBlock, newBufVar, varTypes);
      var dstAddr = new StdAddI64Op(reloadNewBuf, offsetBytes.Result);
      appendBlock.AddOp(dstAddr);
      var reloadOtherBuf = (StdI64)EmitLoad(appendBlock, otherBufVar, varTypes);
      var reloadOtherLen = (StdI64)EmitLoad(appendBlock, otherLenVar, varTypes);
      var reloadElemSize2 = (StdI64)EmitLoad(appendBlock, elemSizeVar, varTypes);
      var copyBytes = new StdMulI64Op(reloadOtherLen, reloadElemSize2);
      appendBlock.AddOp(copyBytes);
      appendBlock.AddOp(new StdMemCopyOp(reloadOtherBuf, dstAddr.Result, copyBytes.Result));

      // Update self: buffer, length, capacity
      var finalBuf = (StdI64)EmitLoad(appendBlock, newBufVar, varTypes);
      EmitStructFieldStore(appendBlock, finalBuf, selfVarName, ManagedFieldBuffer, IrType.I64, varTypes);
      var finalLen = (StdI64)EmitLoad(appendBlock, totalLenVar, varTypes);
      EmitStructFieldStore(appendBlock, finalLen, selfVarName, ManagedFieldLength, IrType.I64, varTypes);

      // Capacity: if growth occurred (requiredByteCap > oldCapBytes), compute new element capacity
      // from growByteCap / elemSize. Otherwise keep old capacity.
      var reloadReqCap = (StdI64)EmitLoad(appendBlock, reqByteCapVar, varTypes);
      var reloadOldCapBytes = (StdI64)EmitLoad(appendBlock, selfCapBytesVar, varTypes);
      var needsGrow = new StdCmpU64Op("ugt", reloadReqCap, reloadOldCapBytes);
      appendBlock.AddOp(needsGrow);
      var reloadGrowCap = (StdI64)EmitLoad(appendBlock, growByteCapVar, varTypes);
      var reloadElemSize3 = (StdI64)EmitLoad(appendBlock, elemSizeVar, varTypes);
      var newCapElems = new StdDivU64Op(reloadGrowCap, reloadElemSize3);
      appendBlock.AddOp(newCapElems);
      var reloadOldCap = (StdI64)EmitLoad(appendBlock, selfCapVar, varTypes);
      var finalCap = new StdSelectI64Op(needsGrow.Result, newCapElems.Result, reloadOldCap);
      appendBlock.AddOp(finalCap);
      EmitStructFieldStore(appendBlock, finalCap.Result, selfVarName, ManagedFieldCapacity, IrType.I64, varTypes);

      // For struct elements: incref each newly copied element.
      // The copied region starts at newBuffer + selfLen * elemSize, otherLen elements.
      // Each element is an 8-byte heap pointer that needs incref.
      if (op.IsStructElement) {
        var increfLoopVar = $"__append_incref_i_{uid}";
        var increfZero = new StdConstI64Op(0);
        appendBlock.AddOp(increfZero);
        EmitStore(appendBlock, increfZero.Result, increfLoopVar, varTypes);
        var increfStartVar = $"__append_incref_start_{uid}";
        var increfBuf2 = (StdI64)EmitLoad(appendBlock, newBufVar, varTypes);
        var increfSelfLen = (StdI64)EmitLoad(appendBlock, selfLenVar, varTypes);
        var increfElemSize = (StdI64)EmitLoad(appendBlock, elemSizeVar, varTypes);
        var increfOff = new StdMulI64Op(increfSelfLen, increfElemSize);
        appendBlock.AddOp(increfOff);
        var increfStart = new StdAddI64Op(increfBuf2, increfOff.Result);
        appendBlock.AddOp(increfStart);
        EmitStore(appendBlock, increfStart.Result, increfStartVar, varTypes);

        var increfHdrLabel = $"__append_incref_hdr_{uid}";
        var increfBodyLabel = $"__append_incref_body_{uid}";
        var increfDoneLabel = $"__append_incref_done_{uid}";
        appendBlock.AddOp(new StdBrOp(increfHdrLabel));

        var increfHdr = func.Body.AddBlock(increfHdrLabel);
        var increfI = (StdI64)EmitLoad(increfHdr, increfLoopVar, varTypes);
        var increfOtherLen = (StdI64)EmitLoad(increfHdr, otherLenVar, varTypes);
        var increfCmp = new StdCmpI64Op("lt", increfI, increfOtherLen);
        increfHdr.AddOp(increfCmp);
        increfHdr.AddOp(new StdCondBrOp(increfCmp.Result, increfBodyLabel, increfDoneLabel));

        var increfBody = func.Body.AddBlock(increfBodyLabel);
        var iBody = (StdI64)EmitLoad(increfBody, increfLoopVar, varTypes);
        var ptrBase = (StdI64)EmitLoad(increfBody, increfStartVar, varTypes);
        var eightConst = new StdConstI64Op(8);
        increfBody.AddOp(eightConst);
        var ptrOff = new StdMulI64Op(iBody, eightConst.Result);
        increfBody.AddOp(ptrOff);
        var elemAddr = new StdAddI64Op(ptrBase, ptrOff.Result);
        increfBody.AddOp(elemAddr);
        var elemPtr = new StdLoadIndirectOp(elemAddr.Result, 0, IrType.I64);
        increfBody.AddOp(elemPtr);
        EmitIncrefValueIfNonnull(increfBody, (StdI64)elemPtr.Result, scopeName: _currentFuncName);
        // Increment loop counter
        var incI2 = (StdI64)EmitLoad(increfBody, increfLoopVar, varTypes);
        var incOne = new StdConstI64Op(1);
        increfBody.AddOp(incOne);
        var incNext = new StdAddI64Op(incI2, incOne.Result);
        increfBody.AddOp(incNext);
        EmitStore(increfBody, incNext.Result, increfLoopVar, varTypes);
        increfBody.AddOp(new StdBrOp(increfHdrLabel));

        appendBlock = func.Body.AddBlock(increfDoneLabel);
      }

      appendBlock.AddOp(new StdBrOp(skipLabel));
    }

    block = func.Body.AddBlock(skipLabel);
  }

  // ============================================================================
  // __ManagedMemoryCursor lowering
  // ============================================================================

  /// <summary>
  /// Re-derive the element metadata for a __ManagedMemory arg at lowering time from
  /// the concrete managed struct type's "Element" type parameter. Mirrors the parser's
  /// GetManagedMemElementKind but reads typeDefs instead of _typeRegistry.
  /// </summary>
  private static (MaxonValueKind kind, string? typeParamName, bool isBitPacked, bool isStructElem, string? structElemTypeName, IrType? elementStorageType) DeriveManagedElementInfo(
    MaxonValue managedArg,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, IrType> typeDefs) {
    var structTypeName = (valueMap[managedArg] as StdHeapPtr)?.TypeName
      ?? throw new InvalidOperationException($"Managed arg %{managedArg.Id} has no TypeName in valueMap");
    if (typeDefs.TryGetValue(structTypeName, out var typeInfo)
      && typeInfo is IrStructType structType
      && structType.TypeParams.TryGetValue("Element", out var elemType)) {
      // For ranged primitives (e.g. Score = int(0..100)), use the OPTIMAL storage type
      // (the narrow type the buffer is laid out for, used to compute element_size at
      // allocation), not the source-level BaseType. ToValueKind on a ranged primitive
      // otherwise returns the base kind (e.g. Integer for int) and the lowering would
      // emit i64 loads/stores against a u8-spaced buffer — corrupting adjacent slots.
      //
      // For non-ranged narrow types used directly as elements (e.g. byte-string literals
      // emit Element = bare IrType.I8 to mean "unsigned byte buffer"), promote to the
      // unsigned variant so codegen picks zero-extend on load. Without this, byte-string
      // reads sign-extend and turn 0xFF into -1.
      var loadType = elemType switch {
        IrRangedPrimitiveType rpt => rpt.OptimalType,
        _ when elemType == IrType.I8 => IrType.U8,
        _ when elemType == IrType.I16 => IrType.U16,
        _ => elemType
      };
      var kind = loadType.ToValueKind();
      // Unions (enums with associated values) are heap-allocated structs and need refcount
      // treatment. Simple enums (no associated values) are stored as raw i64 scalars.
      bool isUnion = elemType is IrEnumType et && et.Cases.Any(c => c.AssociatedValues?.Count > 0);
      bool isStruct = kind == MaxonValueKind.Struct || isUnion;
      string? elemName = isStruct ? elemType.Name : null;
      // Pass the precise narrow type through so codegen can pick movsx vs movzx for
      // signed/unsigned bytes/words. ToValueKind collapses I8/U8 to MaxonValueKind.Byte,
      // losing the signedness — without this hint, signed narrow ranges (e.g.
      // int(-50..50)) would zero-extend on load and turn -7 into 249.
      IrType? elemStorageType = !isStruct && loadType is not IrEnumType ? loadType : null;
      return (kind, null, kind == MaxonValueKind.Bool, isStruct, elemName, elemStorageType);
    }
    // Bare __ManagedMemory with no Element type param (raw byte buffer)
    return (MaxonValueKind.Integer, null, false, false, null, null);
  }

  /// <summary>
  /// Intercepts synthetic __ManagedMemory builtin calls. Emitted by the parser as
  /// MaxonTryCallOp (throwing builtins are always called from a try context).
  /// Returns true if the callee was handled.
  /// </summary>
  private static bool TryLowerManagedMemBuiltin(
    string callee,
    List<MaxonValue> args,
    MaxonValue? result,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    MaxonValue? errorFlagValue,
    VarRegistry temps,
    MaxonCallOp? sourceCallOp = null) {

    switch (callee) {
      case "__managed_mem_slice": {
        if (result is not MaxonStruct sliceResult)
          throw new InvalidOperationException("__managed_mem_slice requires a MaxonStruct result");
        var (_, typeParamName, isBitPacked, isStructElem, _, _) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        // The slice's concrete managed type equals the source's concrete managed type
        // (slice preserves the element type). Read it from args[0]'s StdHeapPtr TypeName.
        string sliceConcreteTypeName = (valueMap[args[0]] as StdHeapPtr)?.TypeName
          ?? throw new InvalidOperationException($"Slice source arg has no concrete managed type in valueMap");
        var sliceOp = new MaxonManagedMemSliceOp(args[0], args[1], args[2]) {
          IsStructElement = isStructElem,
          TypeParamName = typeParamName,
          IsBitPacked = isBitPacked
        };
        sliceOp.Result.TypeName = sliceConcreteTypeName;
        LowerManagedMemSlice(sliceOp, func, ref block, valueMap, varTypes, temps,
          inlineTarget: null, errorFlagValue: errorFlagValue);
        sliceResult.TypeName = sliceConcreteTypeName;
        if (valueMap.TryGetValue(sliceOp.Result, out var mapped)) {
          valueMap[sliceResult] = mapped;
        }
        return true;
      }
      case "__managed_mem_get": {
        var (elementKind, typeParamName, _, isStructElem, structElemTypeName, elementStorageType) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var getOp = new MaxonManagedMemGetOp(args[0], args[1], elementKind) {
          TypeParamName = typeParamName,
          IsStructElement = isStructElem,
          StructElementTypeName = structElemTypeName,
          ElementStorageType = elementStorageType,
          IsBoundsCheckSafe = false
        };
        LowerManagedMemGet(getOp, func, ref block, valueMap, varTypes, temps, errorFlagValue: errorFlagValue);
        if (result != null && getOp.Result != null && valueMap.TryGetValue(getOp.Result, out var getMapped))
          valueMap[result] = getMapped;
        return true;
      }
      case "__managed_mem_remove": {
        var (elementKind, typeParamName, _, isStructElem, structElemTypeName, _) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var removeOp = new MaxonManagedMemRemoveOp(args[0], args[1], elementKind) {
          TypeParamName = typeParamName,
          IsStructElement = isStructElem,
          StructElementTypeName = structElemTypeName
        };
        LowerManagedMemRemove(removeOp, func, ref block, valueMap, varTypes, temps, errorFlagValue: errorFlagValue);
        if (result != null && removeOp.Result != null && valueMap.TryGetValue(removeOp.Result, out var removeMapped))
          valueMap[result] = removeMapped;
        return true;
      }
      case "__managed_mem_set": {
        var (elementKind, typeParamName, _, isStructElem, _, elementStorageType) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var setOp = new MaxonManagedMemSetOp(args[0], args[1], args[2], elementKind) {
          TypeParamName = typeParamName,
          IsStructElement = isStructElem,
          ElementStorageType = elementStorageType
        };
        LowerManagedMemSet(setOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        return true;
      }
      case "__managed_mem_byte_at": {
        var byteAtOp = new MaxonManagedMemByteGetOp(args[0], args[1]);
        LowerManagedMemByteGet(byteAtOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        if (result != null && valueMap.TryGetValue(byteAtOp.Result, out var byteAtMapped))
          valueMap[result] = byteAtMapped;
        return true;
      }
      case "__managed_mem_set_byte": {
        var setByteOp = new MaxonManagedMemByteSetOp(args[0], args[1], args[2]);
        LowerManagedMemByteSet(setByteOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        return true;
      }
      case "__managed_mem_grow": {
        var (_, _, isBitPacked, _, _, _) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var growOp = new MaxonManagedMemGrowOp(args[0], args[1]) {
          IsBitPacked = isBitPacked
        };
        LowerManagedMemGrow(growOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        return true;
      }
      case "__managed_mem_set_length": {
        var setLenOp = new MaxonManagedMemSetLengthOp(args[0], args[1]);
        LowerManagedMemSetLength(setLenOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        return true;
      }
      case "__managed_mem_shift_right": {
        var (_, _, isBitPacked, _, _, _) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var shiftOp = new MaxonManagedMemShiftOp(args[0], args[1], args[2], shiftRight: true) {
          IsBitPacked = isBitPacked
        };
        LowerManagedMemShift(shiftOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        return true;
      }
      case "__managed_mem_shift_left": {
        var (_, _, isBitPacked, _, _, _) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var shiftOp = new MaxonManagedMemShiftOp(args[0], args[1], args[2], shiftRight: false) {
          IsBitPacked = isBitPacked
        };
        LowerManagedMemShift(shiftOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        return true;
      }
      case "__managed_mem_create": {
        if (result is not MaxonStruct createResult)
          throw new InvalidOperationException("__managed_mem_create requires a MaxonStruct result");
        var createMeta = sourceCallOp as MaxonManagedMemCreateTryCallOp
          ?? throw new InvalidOperationException("__managed_mem_create must be lowered from MaxonManagedMemCreateTryCallOp (carrying ElementSize/IsBitPacked)");
        var createOp = new MaxonManagedMemCreateOp(args[0], createMeta.ElementSize) {
          IsBitPacked = createMeta.IsBitPacked
        };
        LowerManagedMemCreate(createOp, block, valueMap, varTypes, temps,
          inlineTarget: null, errorFlagValue: errorFlagValue);
        createResult.TypeName = "__ManagedMemory";
        if (valueMap.TryGetValue(createOp.Result, out var createMapped))
          valueMap[createResult] = createMapped;
        return true;
      }
      default:
        return false;
    }
  }

  /// <summary>
  /// Intercepts synthetic cursor calls (__managed_mem_create_cursor, __cursor_advance, etc.)
  /// during lowering. These are emitted as MaxonCallOp by the parser so that try/otherwise works.
  /// Returns true if the callee was handled.
  /// </summary>
  private static bool TryLowerCursorCall(
    string callee,
    List<MaxonValue> args,
    MaxonValue? result,
    MaxonValueKind? resultKind,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue,
    VarRegistry temps) {

    switch (callee) {
      case "__managed_mem_create_cursor":
        LowerCreateCursor(args, result, block, valueMap, varTypes, errorFlagValue, temps);
        return true;
      case "__cursor_advance":
        LowerCursorAdvanceByCall(args, block, valueMap, varTypes, errorFlagValue);
        return true;
      case "__cursor_retreat":
        LowerCursorRetreatByCall(args, block, valueMap, varTypes, errorFlagValue);
        return true;
      case "__cursor_seek":
        LowerCursorSeekCall(args, block, valueMap, varTypes, errorFlagValue);
        return true;
      case "__cursor_peek":
        if (resultKind == null)
          throw new InvalidOperationException("__cursor_peek call is missing ResultKind — parser must set MaxonCallOp.ResultKind so lowering can pick the right element-load path (byte load vs. bit extract)");
        // For struct/enum elements, extract the heap type name from the result
        // value (preferred — set by the parser at emit time) or fall back to
        // the cursor argument's concrete element type registered in typeDefs
        // (after monomorphization substitutes the generic Element parameter).
        var peekStructTypeName = (result as MaxonStruct)?.TypeName ?? (result as MaxonEnum)?.TypeName;
        LowerCursorPeekCall(args, result, resultKind.Value, peekStructTypeName, block, valueMap, varTypes, errorFlagValue, temps);
        return true;
      default:
        return false;
    }
  }

  /// <summary>
  /// Helper: emit a bounds-check error flag using select (like EmitNullCheckErrorFlag).
  /// Sets __error_flag to errorOrdinal if isError is true, else 0.
  ///
  /// When ioErrnoOrdinalSelector is non-null, the truthy arm is replaced by a
  /// runtime-computed ordinal+1 (errno → variant). The selector callback emits
  /// the IR that produces the StdI64 ordinal SSA value (typically by calling
  /// SelectIoErrorOrdinal with the catch-all = errorOrdinal). This lets ManagedFile
  /// / ManagedDirectory throwing builtins route ENOENT/EACCES to notFound/accessDenied
  /// while keeping the empty-struct-on-error pattern downstream untouched.
  /// </summary>
  private static void EmitBoundsCheckErrorFlag(
    IrBlock<StandardOp> block,
    StdBool isError,
    int errorOrdinal,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue,
    Func<IrBlock<StandardOp>, int, StdI64>? ioErrnoOrdinalSelector = null) {
    StdI64 truthyOrdinal;
    if (ioErrnoOrdinalSelector != null) {
      truthyOrdinal = ioErrnoOrdinalSelector(block, errorOrdinal);
    } else {
      var errorConst = new StdConstI64Op(errorOrdinal);
      block.AddOp(errorConst);
      truthyOrdinal = errorConst.Result;
    }
    var successConst = new StdConstI64Op(0);
    block.AddOp(successConst);
    var selectFlag = new StdSelectI64Op(isError, truthyOrdinal, successConst.Result);
    block.AddOp(selectFlag);
    EmitStore(block, selectFlag.Result, "__error_flag", varTypes);
    if (errorFlagValue != null) {
      valueMap[errorFlagValue] = selectFlag.Result;
    }
  }

  // notFound and accessDenied share the same ordinals between __ManagedFileError and
  // __ManagedDirectoryError (notFound=0, accessDenied=1), so the same os→ordinal table
  // drives the SelectIoErrorOrdinal lowering for both error enums.
  private const int IoErrNotFoundFlag = 0 + 1;       // ordinal 0 + 1 (1-indexed)
  private const int IoErrAccessDeniedFlag = 1 + 1;   // ordinal 1 + 1

  /// <summary>
  /// Win32 GetLastError → 1-indexed flag value mapping for the throwing managed-* builtins.
  /// Codes outside the table fall through to the method-specific catch-all ordinal.
  /// </summary>
  private static readonly (long Code, int FlagValue)[] _win32IoErrorTags = [
    (2,  IoErrNotFoundFlag),       // ERROR_FILE_NOT_FOUND
    (3,  IoErrNotFoundFlag),       // ERROR_PATH_NOT_FOUND
    (5,  IoErrAccessDeniedFlag),   // ERROR_ACCESS_DENIED
    (32, IoErrAccessDeniedFlag),   // ERROR_SHARING_VIOLATION
  ];

  /// <summary>
  /// POSIX errno → 1-indexed flag value mapping for the throwing managed-* builtins.
  /// </summary>
  private static readonly (long Code, int FlagValue)[] _posixIoErrorTags = [
    (2,  IoErrNotFoundFlag),       // ENOENT
    (13, IoErrAccessDeniedFlag),   // EACCES
  ];

  /// <summary>
  /// Emit IR that fetches gt->io_error_code via __io_get_last_error and converts
  /// it to a 1-indexed error-flag value (ordinal + 1) using a chain of cmp+select
  /// ops. The mapping table is selected at emit time based on the active CompileTarget
  /// (Win32 codes on Windows, POSIX errno elsewhere).
  ///
  /// catchAllOrdinal is the 1-indexed flag value already shifted (errorOrdinal + 1),
  /// matching what the constant-ordinal path uses.
  /// </summary>
  internal static StdI64 SelectIoErrorOrdinal(IrBlock<StandardOp> block, int catchAllOrdinal) {
    var errCodeCall = new StdCallRuntimeOp("__io_get_last_error", [], new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(errCodeCall);
    var errCode = (StdI64)errCodeCall.Result!;

    var defaultConst = new StdConstI64Op(catchAllOrdinal);
    block.AddOp(defaultConst);
    StdI64 result = defaultConst.Result;

    bool isWindows = (_currentTarget?.Os ?? "windows").Equals("windows", StringComparison.InvariantCultureIgnoreCase);
    var entries = isWindows ? _win32IoErrorTags : _posixIoErrorTags;

    foreach (var (code, flagValue) in entries) {
      var codeConst = new StdConstI64Op(code);
      block.AddOp(codeConst);
      var match = new StdCmpI64Op("eq", errCode, codeConst.Result);
      block.AddOp(match);
      var ordinalConst = new StdConstI64Op(flagValue);
      block.AddOp(ordinalConst);
      var sel = new StdSelectI64Op(match.Result, ordinalConst.Result, result);
      block.AddOp(sel);
      result = sel.Result;
    }

    return result;
  }

  /// <summary>
  /// __managed_memory.createCursor(): allocate a cursor struct, copy buffer/length/element_size
  /// from the source, set position=0, incref source, store source_ptr.
  /// Sets error flag CursorError.exhausted (1) if source is empty.
  /// </summary>
  private static void LowerCreateCursor(
    List<MaxonValue> args,
    MaxonValue? result,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue,
    VarRegistry temps) {
    var srcVarName = ResolveManagedVarName(args[0], valueMap);
    var srcLength = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldLength, IrType.I64, varTypes);

    // Check empty: length == 0 → error
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var isEmpty = new StdCmpI64Op("eq", srcLength, zeroConst.Result);
    block.AddOp(isEmpty);
    EmitBoundsCheckErrorFlag(block, isEmpty.Result, 1, valueMap, varTypes, errorFlagValue);

    // Allocate cursor struct (even on error path — try/otherwise handles the branch)
    var cursorTypeName = result is MaxonStruct ms ? ms.TypeName : "__ManagedMemoryCursor";
    var tempName = temps.CreateTemp("cursor", result?.Id ?? IrContext.Current.NextId(), cursorTypeName, OwnershipFlags.None);
    var cursorSizeConst = new StdConstI64Op(CursorStructSize);
    block.AddOp(cursorSizeConst);
    var cursorPtr = (StdHeapPtr)EmitAlloc(block, cursorSizeConst.Result, cursorTypeName, tag: "Cursor", scopeName: _currentFuncName);
    EmitStore(block, cursorPtr, tempName, varTypes);

    // Copy fields from source __ManagedMemory
    var srcBuffer = LoadManagedBuffer(block, srcVarName, varTypes);
    var srcElemSize = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldElementSize, IrType.I64, varTypes);
    var posZero = new StdConstI64Op(0);
    block.AddOp(posZero);

    EmitStructFieldStore(block, srcBuffer, tempName, CursorFieldBuffer, IrType.I64, varTypes);
    EmitStructFieldStore(block, posZero.Result, tempName, CursorFieldPosition, IrType.I64, varTypes);
    EmitStructFieldStore(block, srcLength, tempName, CursorFieldLength, IrType.I64, varTypes);
    EmitStructFieldStore(block, srcElemSize, tempName, CursorFieldElementSize, IrType.I64, varTypes);

    // Incref source and store source_ptr
    var srcPtr = (StdI64)EmitLoad(block, srcVarName, varTypes);
    EmitIncrefValue(block, srcPtr, scopeName: _currentFuncName);
    EmitStructFieldStore(block, srcPtr, tempName, CursorFieldSourcePtr, IrType.I64, varTypes);

    if (result != null)
      valueMap[result] = new StdHeapPtr(cursorPtr.Id, cursorTypeName, tempName);
  }

  /// <summary>
  /// cursor.current(): load element at current position. Bounds-checks position
  /// against cursor length and panics (via __mm_panic_cursor_oob) if out of range;
  /// stdlib wrappers validate via `hasValue` before calling this, so the panic
  /// branch is unreachable in practice but catches misuse.
  /// </summary>
  private static void LowerCursorCurrent(
    MaxonCursorCurrentOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps) {
    var cursorVarName = ResolveManagedVarName(op.CursorStruct, valueMap);
    var buffer = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldBuffer, IrType.I64, varTypes);
    var position = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldPosition, IrType.I64, varTypes);
    var length = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldLength, IrType.I64, varTypes);
    // Bounds check: position must be < length.
    EmitBoundsCheck(block, position, length, "__mm_panic_cursor_oob");

    EmitCursorElementLoad(block, cursorVarName, buffer, position, op.ResultKind,
      op.IsStructElement, op.StructElementTypeName, op.Result, valueMap, varTypes, temps, "ccur");
  }

  /// <summary>
  /// Loads a cursor element into <paramref name="result"/>. Dispatches on element kind:
  /// bool → bit-extract from the packed buffer; struct/enum → heap pointer load + incref;
  /// primitive → typed load at <c>buffer + index * element_size</c>.
  ///
  /// Every cursor read op must go through this helper so that adding a new kind (or a new
  /// layout like bit-packing) is a single-site change. Callers pass the op's declared
  /// <c>ResultKind</c> — never infer it from the runtime <see cref="MaxonValue"/> subtype.
  /// </summary>
  private static void EmitCursorElementLoad(
    IrBlock<StandardOp> block,
    string cursorVarName,
    StdI64 buffer,
    StdI64 index,
    MaxonValueKind resultKind,
    bool isStructElement,
    string? structElementTypeName,
    MaxonValue result,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps,
    string tempPrefix) {
    if (resultKind == MaxonValueKind.Bool) {
      // Bit-packed bool: extract bit at index and widen to a StdBool so callers
      // (cond_br, bool-typed assigns, bool-returning wrappers) see the right shape.
      valueMap[result] = EmitBitGetAsBool(block, buffer, index);
      return;
    }

    var elemSize = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldElementSize, IrType.I64, varTypes);
    var addr = ComputeElementAddress(block, buffer, index, elemSize);

    if (isStructElement) {
      var loadOp = new StdLoadIndirectOp(addr, 0, IrType.I64);
      block.AddOp(loadOp);
      EmitIncrefValue(block, (StdI64)loadOp.Result, scopeName: _currentFuncName);
      var tempId = IrContext.Current.NextId();
      var typeName = structElementTypeName ?? "unknown";
      var tempName = temps.CreateTemp(tempPrefix, tempId, typeName, OwnershipFlags.Orphan | OwnershipFlags.OwnsRef);
      EmitStore(block, (StdI64)loadOp.Result, tempName, varTypes);
      valueMap[result] = new StdHeapPtr(loadOp.Result.Id, typeName, tempName);
    } else {
      var elemType = GetManagedMemElementType(resultKind, "EmitCursorElementLoad");
      var loadOp = new StdLoadIndirectOp(addr, 0, elemType);
      block.AddOp(loadOp);
      valueMap[result] = loadOp.Result;
    }
  }

  /// <summary>
  /// cursor.index(): read the position field.
  /// </summary>
  private static void LowerCursorIndex(
    MaxonCursorIndexOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {
    var cursorVarName = ResolveManagedVarName(op.CursorStruct, valueMap);
    var position = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldPosition, IrType.I64, varTypes);
    valueMap[op.Result] = position;
  }

  /// <summary>
  /// Emit: cursor.position = isValid ? newPos : oldPosition; errorFlag = !isValid ? errorCode : 0.
  /// Shared tail for advance() / retreat() / seek(index) — each computes its own
  /// newPos and validity condition, then hands off here.
  /// </summary>
  private static void EmitCursorPositionUpdate(
    string cursorVarName,
    StdI64 newPos,
    StdBool isValid,
    StdI64 oldPosition,
    int errorCode,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    var selectedPos = new StdSelectI64Op(isValid, newPos, oldPosition);
    block.AddOp(selectedPos);
    EmitStructFieldStore(block, selectedPos.Result, cursorVarName, CursorFieldPosition, IrType.I64, varTypes);

    var trueConst = new StdConstI1Op(true);
    block.AddOp(trueConst);
    var isError = new StdXorI1Op(isValid, trueConst.Result);
    block.AddOp(isError);
    EmitBoundsCheckErrorFlag(block, isError.Result, errorCode, valueMap, varTypes, errorFlagValue);
  }

  /// <summary>
  /// cursor.advance(): position += 1. Sets error flag CursorError.exhausted (1)
  /// if position + 1 >= length.
  /// </summary>
  private static void LowerCursorAdvanceByCall(
    List<MaxonValue> args,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    var cursorVarName = ResolveManagedVarName(args[0], valueMap);
    var position = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldPosition, IrType.I64, varTypes);
    var length = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldLength, IrType.I64, varTypes);
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);

    var newPos = new StdAddI64Op(position, oneConst.Result);
    block.AddOp(newPos);
    var isValid = new StdCmpI64Op("lt", newPos.Result, length);
    block.AddOp(isValid);

    EmitCursorPositionUpdate(cursorVarName, newPos.Result, isValid.Result, position, 1, block, valueMap, varTypes, errorFlagValue);
  }

  /// <summary>
  /// cursor.retreat(): position -= 1. Sets error flag CursorError.atStart (2) if position - 1 < 0.
  /// </summary>
  private static void LowerCursorRetreatByCall(
    List<MaxonValue> args,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    var cursorVarName = ResolveManagedVarName(args[0], valueMap);
    var position = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldPosition, IrType.I64, varTypes);
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);

    var newPos = new StdSubI64Op(position, oneConst.Result);
    block.AddOp(newPos);
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var isValid = new StdCmpI64Op("ge", newPos.Result, zeroConst.Result);
    block.AddOp(isValid);

    EmitCursorPositionUpdate(cursorVarName, newPos.Result, isValid.Result, position, 2, block, valueMap, varTypes, errorFlagValue);
  }

  /// <summary>
  /// cursor.seek(index): jump to arbitrary position. Sets error flag CursorError.exhausted (1)
  /// if index is out of bounds (index &lt; 0 or index &gt;= length).
  /// </summary>
  private static void LowerCursorSeekCall(
    List<MaxonValue> args,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    var cursorVarName = ResolveManagedVarName(args[0], valueMap);
    var oldPosition = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldPosition, IrType.I64, varTypes);
    var length = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldLength, IrType.I64, varTypes);
    var newIdx = (StdI64)valueMap[args[1]];

    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var isInBoundsLower = new StdCmpI64Op("ge", newIdx, zeroConst.Result);
    block.AddOp(isInBoundsLower);
    var isInBoundsUpper = new StdCmpI64Op("lt", newIdx, length);
    block.AddOp(isInBoundsUpper);
    var isValid = new StdAndI1Op(isInBoundsLower.Result, isInBoundsUpper.Result);
    block.AddOp(isValid);

    // seek jumps directly to newIdx (not a computed position), so pass it
    // as "newPos" — the helper emits the select + error-flag tail.
    EmitCursorPositionUpdate(cursorVarName, newIdx, isValid.Result, oldPosition, 1, block, valueMap, varTypes, errorFlagValue);
  }

  /// <summary>
  /// cursor.peek(ahead): load element at position + ahead. Sets error flag if out of bounds.
  /// Returns default value (0) on error path.
  /// </summary>
  private static void LowerCursorPeekCall(
    List<MaxonValue> args,
    MaxonValue? result,
    MaxonValueKind resultKind,
    string? structElementTypeName,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue,
    VarRegistry temps) {
    if (result == null)
      throw new InvalidOperationException("__cursor_peek requires a result value");

    var cursorVarName = ResolveManagedVarName(args[0], valueMap);
    var position = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldPosition, IrType.I64, varTypes);
    var length = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldLength, IrType.I64, varTypes);
    var ahead = (StdI64)valueMap[args[1]];

    var target = new StdAddI64Op(position, ahead);
    block.AddOp(target);

    var isError = new StdCmpI64Op("ge", target.Result, length);
    block.AddOp(isError);
    EmitBoundsCheckErrorFlag(block, isError.Result, 1, valueMap, varTypes, errorFlagValue);

    // Clamp target to valid range to avoid accessing invalid memory on the error path —
    // the value loaded will be discarded by try/otherwise but the load itself still runs.
    var buffer = (StdI64)EmitStructFieldLoad(block, cursorVarName, CursorFieldBuffer, IrType.I64, varTypes);
    var isValid = new StdCmpI64Op("lt", target.Result, length);
    block.AddOp(isValid);
    var safeTarget = new StdSelectI64Op(isValid.Result, target.Result, position);
    block.AddOp(safeTarget);

    var isStructElement = resultKind is MaxonValueKind.Struct or MaxonValueKind.Enum;
    EmitCursorElementLoad(block, cursorVarName, buffer, safeTarget.Result, resultKind,
      isStructElement, structElementTypeName, result, valueMap, varTypes,
      temps, tempPrefix: "cpeek");
  }
}
