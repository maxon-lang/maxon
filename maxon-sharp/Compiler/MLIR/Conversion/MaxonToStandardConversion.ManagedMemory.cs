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
  /// The capacity an append grows a buffer to: EXACTLY the bytes it will hold, plus one for the NUL
  /// slot `String.cstr()` reads. No growth factor, no minimum floor.
  ///
  /// AN APPEND ALLOCATES A NEW BUFFER OF EXACTLY THE RIGHT SIZE AND COPIES INTO IT, which is Go's
  /// `string` and Go's reason: a string OWNS what it holds and not one byte more. Every string in a
  /// program pays for this policy, and nearly every string is built once and thereafter only read —
  /// so geometric growth here would leave every one of them carrying slack that will never be used,
  /// to buy an amortization only a BUILD LOOP needs.
  ///
  /// It used to be `max(requiredBytes + 1, currentCapBytes * 2, 64)` — doubling, with a 64-byte floor
  /// — which made an append loop amortized O(1) and made every string in the process up to twice the
  /// size it needed to be.
  ///
  /// THE COST IS REAL AND IT IS DELIBERATE: appending in a loop is now Θ(n²), because each append
  /// copies everything already there. That is what `StringBuilder` is for (stdlib/String.maxon) — it
  /// accumulates into a `ByteArray`, which grows geometrically, and hands the finished buffer to a
  /// `String` with no copy at all. Build there; hold a `String`.
  /// </summary>
  private static StdI64 EmitExactAppendCapacity(IrBlock<StandardOp> block, StdI64 requiredBytes) {
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var exact = new StdAddI64Op(requiredBytes, oneConst.Result);
    block.AddOp(exact);
    return exact.Result;
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
    EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: op.ResultKind == MaxonValueKind.Bool,
      isStructElement: op.IsStructElement);

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
      // Clear the bit the shift vacated at the top — a bool array's slots above
      // `length` must read false, same capacity-slot invariant as every other
      // element width. A bit-packed element is never a managed one, so this erases
      // through the same one-slot path the other widths use.
      EmitZeroElementSlot(block, managedVarName, finalNewLen, varTypes);
      EmitStructFieldStore(block, finalNewLen, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    } else {
      var addr = ComputeElementAddress(block, buffer, index, elemSize);

      if (op.IsStructElement) {
        // Load the struct pointer — ownership transfer, NO incref.
        // The buffer's reference is handed to the caller.
        //
        // The slot is NOT erased here, and used to be: an inline 8-byte store landed on
        // buffer[index] before the shift, to keep the teardown walk off the pointer just
        // handed out. Every byte of it is overwritten before anything can observe it — the
        // shift below copies [index+1, length) down over slot `index`, and when there is
        // nothing to copy (a pop, index == newLength) the single erase after the shift lands
        // on that very slot. Two writes to one slot is how the width drifted apart in the
        // first place; the one that survives is the one that derives its width.
        var loadOp = new StdLoadIndirectOp(addr, 0, IrType.I64);
        block.AddOp(loadOp);

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

      // Erase the last slot, which the shift left holding a stale duplicate of
      // the element now at newLength-1 (and, for a pop — idx == length-1 — the
      // very pointer that was just handed to the caller). It sits above the new
      // length, so a later regrow would re-adopt an element this record no longer
      // owns and the teardown walk would decref it a second time. Zeroing it also
      // gives the scalar case its documented "resize exposes zeros" behaviour.
      // See the capacity-slot invariant on EmitMmVacateManagedElements.
      //
      // ERASE, never vacate, for BOTH element classes: the departing element was
      // handed to the caller or duplicated one slot down, so releasing it here
      // would be the double-free this erase exists to prevent.
      EmitZeroElementSlot(block, managedVarName, newLength.Result, varTypes);

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
    EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: isBitPacked,
      isStructElement: op.IsStructElement);
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
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
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

    var tempName = inlineTarget
      ?? temps.CreateTemp("managed_create", op.Result.Id, "__ManagedMemory", OwnershipFlags.None);

    // Byte-fusion: when the element bytes fit MmInlineCapBytes, allocate the record AND its buffer
    // as ONE mm_alloc with the buffer INLINE (buffer = self + recordSize, parent_ptr = MmParentInline);
    // otherwise the record plus a separate raw buffer. The count is a runtime value, so the cap test
    // is a runtime branch (mirrors the self-hosted __managed_mem_create_managed). The first grow past
    // the inline capacity detaches to an external buffer; the record's own slot reclaims the bytes.
    var uid = IrContext.Current.NextId();
    var capConst = new StdConstI64Op(MmInlineCapBytes);
    block.AddOp(capConst);
    var byteSizeNonZero = new StdCmpU64Op("ne", byteSize, zero.Result);
    block.AddOp(byteSizeNonZero);
    var fitsCap = new StdCmpU64Op("ule", byteSize, capConst.Result);
    block.AddOp(fitsCap);
    var doInline = new StdAndI1Op(byteSizeNonZero.Result, fitsCap.Result);
    block.AddOp(doInline);
    var inlineLabel = $"__mmcreate_inline_{uid}";
    var externalLabel = $"__mmcreate_external_{uid}";
    var mergeLabel = $"__mmcreate_merge_{uid}";
    block.AddOp(new StdCondBrOp(doInline.Result, inlineLabel, externalLabel));

    // Inline: record + buffer in one allocation; buffer points into the record's own slot.
    var inlineBlock = func.Body.AddBlock(inlineLabel);
    var recSizeConst = new StdConstI64Op(ManagedMemoryStructSize);
    inlineBlock.AddOp(recSizeConst);
    var fusedSize = new StdAddI64Op(byteSize, recSizeConst.Result);
    inlineBlock.AddOp(fusedSize);
    var inlineSelf = EmitAlloc(inlineBlock, fusedSize.Result, "__ManagedMemory", scopeName: _currentFuncName);
    EmitStore(inlineBlock, inlineSelf, tempName, varTypes);
    var inlineBuf = EmitInlineBufferPtr(inlineBlock, tempName, ManagedMemoryStructSize, varTypes);
    var inlineParent = new StdConstI64Op(MmParentInline);
    inlineBlock.AddOp(inlineParent);
    EmitInitManagedMemory(inlineBlock, tempName, inlineBuf, count, count, elemSizeValue, inlineParent.Result, varTypes);
    inlineBlock.AddOp(new StdBrOp(mergeLabel));

    // External: record + a separate raw buffer (a larger array that will keep growing).
    var externalBlock = func.Body.AddBlock(externalLabel);
    var externalSelf = EmitAlloc(externalBlock, ManagedMemoryStructSize, "__ManagedMemory", scopeName: _currentFuncName);
    EmitStore(externalBlock, externalSelf, tempName, varTypes);
    var externalBuf = EmitRawAlloc(externalBlock, byteSize, label: "ManagedMemory.buf", scopeName: _currentFuncName);
    var externalParent = new StdConstI64Op(0);
    externalBlock.AddOp(externalParent);
    EmitInitManagedMemory(externalBlock, tempName, externalBuf, count, count, elemSizeValue, externalParent.Result, varTypes);
    externalBlock.AddOp(new StdBrOp(mergeLabel));

    block = func.Body.AddBlock(mergeLabel);
    valueMap[op.Result] = new StdHeapPtr(op.Result.Id, "__ManagedMemory", tempName);
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

    EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: op.IsBitPacked,
      isStructElement: op.IsStructElement);

    // A COW promoted a BORROWED buffer to an owned copy of the live elements and set the
    // capacity to that length — so the record may now hold MORE slots than the caller asked
    // for, and reallocating down to the ask would strand the elements the copy just rescued
    // outside the allocation (`[10, 20, 30].reserve(1)` published capacity 1 over a length of
    // 3, and the vacate that `resize(1)` runs next then zeroed 16 bytes past the buffer).
    //
    // The shrink guard above cannot catch this: it runs BEFORE the COW, when the capacity is
    // still the non-owned sentinel, and the clamp makes every request look like an increase.
    // `grow` only ever grows, so take the larger of the two — for an already-owned buffer the
    // guard has already proven the ask is the larger, and this is a no-op.
    var capAfterCow = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    var asksForFewer = new StdCmpI64Op("lt", newCap, capAfterCow);
    block.AddOp(asksForFewer);
    var grownCap = new StdSelectI64Op(asksForFewer.Result, capAfterCow, newCap);
    block.AddOp(grownCap);

    // Load buffer pointer (now guaranteed to be heap-allocated after COW check)
    var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);

    // Compute new byte size
    StdI64 newByteSize;
    if (op.IsBitPacked) {
      // Bit-packed bool: byte size = (newCap + 7) >> 3
      newByteSize = ComputeBitPackedByteSize(block, grownCap.Result);
    } else {
      var newByteSizeOp = new StdMulI64Op(grownCap.Result, elemSize);
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
    EmitStructFieldStore(block, grownCap.Result, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
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
    EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: op.IsBitPacked,
      isStructElement: op.IsStructElement);
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
    } else {
      // Byte-strided elements: one bulk copy, then erase the slot the copy vacated.
      //
      // The vacated slot is the one whose contents now live in a NEIGHBOUR: buffer[index]
      // for a right shift (its occupant moved to index+1), buffer[index+count] for a left
      // one (it was overwritten from index+count+1... upward, leaving a duplicate of the
      // last element copied). Erasing it is what stops the duplicate from being released
      // twice — Array.insert's following set() decrefs whatever it finds in the slot, and
      // a slot left above `length` is re-adopted by the next regrow and then decref'd
      // again by the teardown walk. It also gives the scalar case the documented "the
      // slots above length read zero" behaviour (the capacity-slot invariant, see
      // EmitMmVacateManagedElements).
      StdI64 vacatedSlot;
      if (op.ShiftRight) {
        vacatedSlot = index;
      } else {
        var pastEndOp = new StdAddI64Op(index, count);
        block.AddOp(pastEndOp);
        vacatedSlot = pastEndOp.Result;
      }
      // The slot INDEX is what survives the copy, not an address: rep movsb clobbers the
      // scratch registers, so it is spilled here and the address recomputed afterwards.
      var vacatedSlotVar = $"__shift_vacated_{IrContext.Current.NextId()}";
      EmitStore(block, vacatedSlot, vacatedSlotVar, varTypes);
      var bytesOp = new StdMulI64Op(count, elemSize);
      block.AddOp(bytesOp);

      if (op.ShiftRight) {
        // Copy [index, index+count) one position right. Reverse copy because dst > src.
        var srcAddr = ComputeElementAddress(block, buffer, index, elemSize);
        var dstAddr = new StdAddI64Op(srcAddr, elemSize);
        block.AddOp(dstAddr);
        block.AddOp(new StdMemCopyReverseOp(srcAddr, dstAddr.Result, bytesOp.Result));
      } else {
        // Copy [index+1, index+1+count) one position left.
        var oneConst = new StdConstI64Op(1);
        block.AddOp(oneConst);
        var srcIndex = new StdAddI64Op(index, oneConst.Result);
        block.AddOp(srcIndex);
        var srcAddr = ComputeElementAddress(block, buffer, srcIndex.Result, elemSize);
        var dstAddr = ComputeElementAddress(block, buffer, index, elemSize);
        block.AddOp(new StdMemCopyOp(srcAddr, dstAddr, bytesOp.Result));
      }

      EmitZeroElementSlot(block, managedVarName, (StdI64)EmitLoad(block, vacatedSlotVar, varTypes), varTypes);
    }
  }

  /// <summary>
  /// Erase the ONE slot at <paramref name="slotIndex"/> — THE single way this file says
  /// "a copy vacated this slot", shared by `insert`'s right shift, `shiftLeft`, and
  /// `remove`'s left shift.
  ///
  /// It goes through mm_zero_element_range so the erase is exactly `element_size` bytes
  /// wide, DERIVED from the same record field the copy's stride came from rather than
  /// assumed. Each of these sites used to hand-roll an 8-byte store instead, which is
  /// right only for a pointer-width element: at element_size 1 it erased the slot AND the
  /// seven live elements after it, so `b"hey".insert(1, value: 88)` read back
  /// `104 88 0 0` — the shifted `e` and `y` destroyed, with count() still reporting 4 and
  /// the process exiting 0.
  /// </summary>
  private static void EmitZeroElementSlot(
    IrBlock<StandardOp> block,
    string managedVarName,
    StdI64 slotIndex,
    Dictionary<string, string> varTypes) {
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var end = new StdAddI64Op(slotIndex, oneConst.Result);
    block.AddOp(end);
    EmitZeroElementRange(block, managedVarName, slotIndex, end.Result, varTypes);
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

  /// After a grow, tell `self` where its bytes now live BEFORE reading the copy source, and read
  /// that source from `other`'s record rather than from a pointer captured earlier.
  ///
  /// `other` MAY BE `self` — `s.append(s)`, `a.append(a)` — and then the source bytes are wherever
  /// the grow just put them: the pre-grow pointer is the block maxon_string_ensure_cap has just
  /// mm_raw_free'd, so copying from it copies freed memory. (Measured: `s = "abc"; s.append(s)`
  /// twice ended as `abcabc` + six bytes of the freed block, reported as success.) The two halves
  /// are one fix — re-reading `other.buffer` only sees the new buffer because `self.buffer` was
  /// published first, and publishing first is only safe because the source is re-read.
  ///
  /// When `other` is a different record the re-read returns exactly what the earlier capture held,
  /// so nothing but the load's position changes. The self-hosted runtime's twin
  /// (`__managed_mem_append` in stdlib/Internals.maxon) loads both buffers after its grow, which is
  /// why shv2 answers an aliased append correctly and the bootstrap did not.
  private static StdI64 PublishGrownBufferAndLoadSource(
    IrBlock<StandardOp> block, string selfVarName, string otherVarName, StdI64 grownBuf,
    Dictionary<string, string> varTypes) {
    EmitStructFieldStore(block, grownBuf, selfVarName, ManagedFieldBuffer, IrType.I64, varTypes);
    return LoadManagedBuffer(block, otherVarName, varTypes);
  }

  /// <summary>
  /// Emit a COW (copy-on-write) check for a managed memory struct.
  /// If capacity < 0, the buffer is read-only (rdata/slice) and must be copied to a writable heap allocation.
  /// Updates buffer and capacity fields on the managed struct (and writes through to self if needed).
  /// Element size is passed dynamically (read from the struct's element_size field).
  /// </summary>
  /// After a maxon_string_ensure_cap grow that may have DETACHED the buffer, make the record a
  /// plain ROOT owner of the fresh external buffer and release whatever parent it held. A detach is
  /// signalled by the buffer pointer having changed (a grow that fit in place keeps everything).
  /// On detach the record no longer shares its old buffer, so:
  ///   - a real heap parent (a slice's source, parent_ptr > 0) is mm_decref'd — otherwise it and
  ///     its owned buffer leak (this is what makes `slice.append(...)` balance);
  ///   - the inline sentinel (MmParentInline) is simply cleared — nothing to decref;
  ///   - a root (0) needs nothing.
  /// In every detached case parent_ptr ends at 0. Companion to the parentPtr guard in
  /// maxon_string_ensure_cap, which skips freeing an inline (or slice-owned) old buffer.
  private static void EmitReleaseParentOnDetach(
    IrBlock<StandardOp> block, string managedVarName, StdI64 oldBuf, StdI64 newBuf,
    Dictionary<string, string> varTypes) {
    var parentPtr = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldParentPtr, IrType.I64, varTypes);
    var bufChanged = new StdCmpI64Op("ne", oldBuf, newBuf);
    block.AddOp(bufChanged);
    var zeroPtr = new StdConstI64Op(0);
    block.AddOp(zeroPtr);
    // Decref only a REAL heap parent (a slice source: parent_ptr > 0); sentinels (0, -3) are not pointers.
    var isRealParent = new StdCmpI64Op("gt", parentPtr, zeroPtr.Result);
    block.AddOp(isRealParent);
    var shouldDecref = new StdAndI1Op(bufChanged.Result, isRealParent.Result);
    block.AddOp(shouldDecref);
    var parentToDecref = new StdSelectI64Op(shouldDecref.Result, parentPtr, zeroPtr.Result);
    block.AddOp(parentToDecref);
    EmitDecrefValueIfNonnull(block, parentToDecref.Result, scopeName: _currentFuncName);
    // Any detach makes the record a ROOT owner of the fresh buffer.
    var newParent = new StdSelectI64Op(bufChanged.Result, zeroPtr.Result, parentPtr);
    block.AddOp(newParent);
    EmitStructFieldStore(block, newParent.Result, managedVarName, ManagedFieldParentPtr, IrType.I64, varTypes);
  }

  private static void EmitCowCheck(
    IrBlock<StandardOp> block,
    string managedVarName,
    Dictionary<string, string> varTypes,
    StdI64 elemSize,
    bool isBitPacked = false,
    bool isStructElement = false) {
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
    // cow_check always detaches a non-owned (capacity < 0) buffer, even when
    // byteLen == 0 — an empty rdata/slice promotes to a fresh owned buffer
    // (NULL for byteLen 0), so the new buffer differs from the old and capacity
    // is reset to the length here. (Owned buffers, capacity >= 0, return the
    // same pointer and keep their capacity.)
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

    // A COW of a MANAGED-element buffer memcpy'd 8-byte element POINTERS, and a raw copy of a
    // pointer carries no claim on what it points at. Both buffers are now walked by a teardown
    // that decrefs every slot, so without this the shared elements are released twice — which is
    // exactly what `mm_decref: refcount underflow (already zero)` was, reached by cloning an
    // `Array with <struct>` and then pushing onto the clone.
    //
    // RETAIN and not a deep clone: a COW materialises a private buffer for a value that ALREADY
    // exists (a view and its parent are two buffers holding one array), so the elements are the
    // same elements. Only a copy that mints a NEW array value deep-clones — see
    // ManagedElementCopy.
    //
    // ⚠ **IT HAS TO CLAIM THE ELEMENTS BEFORE THE PARENT IS RELEASED**, which is the whole of why it
    // sits above the decref rather than at the end of this function. The release below can take the
    // parent to zero, and the parent's own teardown decrefs every element of the buffer this copy
    // was made FROM — so an incref after it runs on records that have already been freed. Measured
    // with `--mm-trace`, in this order: `mm_decref String #4 rc=0 [~ManagedElements]`,
    // `mm_free String #4`, then `mm_incref String #4 rc=1 [~ManagedElements]` resurrecting it, then
    // `refcount underflow (already zero)` on the next teardown.
    if (isStructElement) {
      var increfManagedPtr = (StdI64)EmitLoad(block, managedVarName, varTypes);
      var cowIncrefArg = new StdSelectI64Op(cowDidCopy.Result, increfManagedPtr, zeroPtr.Result);
      block.AddOp(cowIncrefArg);
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_incref_managed_elements", [cowIncrefArg.Result], null));
    }

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
    // A byte write is only ever issued against a byte buffer, so the copy has no element pointers
    // to claim — hence no isStructElement here.
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
    // COW to get an owned buffer (handles rdata and slice cases). Null-terminating is a BYTE
    // operation on a string/byte buffer, so the copy holds no element pointers to claim.
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
    // parent_ptr lets ensure_cap skip freeing an inline/slice-owned buffer (see EmitReleaseParentOnDetach).
    var fixParent = (StdI64)EmitStructFieldLoad(fixBlock, managedVarName, ManagedFieldParentPtr, IrType.I64, varTypes);
    var grownBuf = new StdI64(IrContext.Current.NextStdId());
    fixBlock.AddOp(new StdCallRuntimeOp("maxon_string_ensure_cap", [fixBuf, fixLen, fixCap, requiredCap.Result, fixParent], grownBuf));
    EmitStructFieldStore(fixBlock, grownBuf, managedVarName, ManagedFieldBuffer, IrType.I64, varTypes);
    EmitStructFieldStore(fixBlock, requiredCap.Result, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    // A record detached by this grow (buffer changed) becomes a plain external root owner
    // (the earlier EmitCowCheck already resolved any slice, so here parent is root or inline).
    EmitReleaseParentOnDetach(fixBlock, managedVarName, fixBuf, grownBuf, varTypes);
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
  /// __Builtins.readStdin(maxBytes): allocate a __ManagedMemory of byte-elements
  /// with capacity maxBytes, read up to maxBytes bytes from stdin into the MM's
  /// buffer via the runtime helper, set the MM's length to the bytes actually
  /// read, and yield the MM. Binary-safe: unlike the cstring-returning helpers,
  /// the MM length comes from the OS read return rather than a NUL scan.
  /// </summary>
  private static void LowerManagedReadStdin(
    MaxonManagedReadStdinOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps) {
    // 1. Allocate the MM with elementSize=1 (byte buffer) and capacity=maxBytes.
    var createOp = new MaxonManagedMemCreateOp(op.MaxBytes, elementSize: 1);
    LowerManagedMemCreate(createOp, func, ref block, valueMap, varTypes, temps, inlineTarget: null);

    // The created MM lives behind valueMap[createOp.Result] as a StdHeapPtr
    // whose VarName is the temp slot holding the MM pointer. Resolve once.
    var managedVarName = ResolveManagedVarName(createOp.Result, valueMap);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var maxBytes = (StdI64)valueMap[op.MaxBytes];

    // 2. Call the runtime read helper. Returns bytes-read as i64.
    var bytesRead = new StdI64(IrContext.Current.NextStdId());
    block.AddOp(new StdCallRuntimeOp("maxon_managed_read_stdin", [buffer, maxBytes], bytesRead));

    // 3. Stash the count into the MM's length field. Capacity / element_size /
    //    parent fields were initialized by LowerManagedMemCreate.
    EmitStructFieldStore(block, bytesRead, managedVarName, ManagedFieldLength, IrType.I64, varTypes);

    // 4. Hand the freshly-built MM out to the caller.
    valueMap[op.Result] = valueMap[createOp.Result];
  }

  /// <summary>
  /// Vacate the slots [start, end): release each departing managed element and
  /// erase the slots it left behind, so the range reads back as zero. The single
  /// point where the capacity-slot invariant (see the header comment on
  /// EmitMmVacateManagedElements) is re-established after slots leave the live
  /// range — shared by clear and the shrink path of setLength.
  ///
  /// Managed elements route through mm_vacate_managed_elements (decref + erase);
  /// primitive elements have nothing to release, so they only need the erase.
  /// </summary>
  private static void EmitVacateElementRange(
    IrBlock<StandardOp> block,
    string managedVarName,
    StdI64 start,
    StdI64 end,
    bool isStructElement,
    Dictionary<string, string> varTypes) {
    if (!isStructElement) {
      EmitZeroElementRange(block, managedVarName, start, end, varTypes);
      return;
    }
    var managedPtr = (StdI64)EmitLoad(block, managedVarName, varTypes);
    block.AddOp(new StdCallRuntimeOp("mm_vacate_managed_elements", [managedPtr, start, end], null));
  }

  /// <summary>
  /// Erase the slots [start, end) WITHOUT releasing what was in them — the erase half of
  /// EmitVacateElementRange on its own. That is what a MOVE wants: the element that left
  /// the range was not dropped, it was copied into a neighbouring slot or handed to the
  /// caller, so exactly one of the two references must survive and releasing the other
  /// here would be the double-free. Vacating is for an element that is genuinely GONE;
  /// this is for one that merely lives somewhere else now.
  ///
  /// The range is measured in ELEMENTS and the runtime scales it by the record's own
  /// element_size, so the erase can never be a different width from the slots it names.
  /// </summary>
  private static void EmitZeroElementRange(
    IrBlock<StandardOp> block,
    string managedVarName,
    StdI64 start,
    StdI64 end,
    Dictionary<string, string> varTypes) {
    var managedPtr = (StdI64)EmitLoad(block, managedVarName, varTypes);
    block.AddOp(new StdCallRuntimeOp("mm_zero_element_range", [managedPtr, start, end], null));
  }

  /// <summary>
  /// Set length with capacity validation: panics if newLength > capacity.
  ///
  /// A SHRINK vacates the dropped slots [newLength, oldLength) before the store:
  /// each managed element there is released (or its reference is orphaned — a
  /// leak) and the slot is erased (or a later regrow past newLength hands the
  /// caller a pointer this record no longer owns, which the teardown walk then
  /// decrefs a second time — a double-free).
  ///
  /// A GROW stores the length and nothing else, and must keep doing so: its
  /// caller has already staged the new elements into [oldLength, newLength) and
  /// is using this call to publish them (push = set-then-setLength). The exposed
  /// slots are safe because they are already zero — see the capacity-slot
  /// invariant on EmitMmVacateManagedElements.
  ///
  /// A NON-OWNED buffer (capacity &lt; 0: rdata or a read-only view) therefore
  /// admits exactly ONE length, zero, because it owns no writable slot. The
  /// clamp below is what says so. Left unclamped, `capacity + 1` for the rdata
  /// sentinel -2 is -1, which as the unsigned bound of the test below is
  /// UINT64_MAX — the largest value there is, so NOTHING compares at-or-above it
  /// and the guard admitted every length including the negative ones it exists to
  /// refuse (`b"hey".resize(-2)` published count() == -2 and exited 0).
  /// </summary>
  private static void LowerManagedMemSetLength(
    MaxonManagedMemSetLengthOp op,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue = null) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
    var rawCapacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    var capacity = EmitClampCapacityNonNeg(block, rawCapacity);
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

    // Shrink? Vacate [newLength, oldLength) first — it reads the slots off the
    // OLD live range, so it has to run before the length store.
    var oldLength = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    var isShrink = new StdCmpU64Op("ult", newLength, oldLength);
    block.AddOp(isShrink);
    var shrinkUid = IrContext.Current.NextId();
    var shrinkLabel = $"__setlen_shrink_{shrinkUid}";
    var storeLabel = $"__setlen_store_{shrinkUid}";
    block.AddOp(new StdCondBrOp(isShrink.Result, shrinkLabel, storeLabel));

    var shrinkBlock = func.Body.AddBlock(shrinkLabel);
    EmitVacateElementRange(shrinkBlock, managedVarName, newLength, oldLength, op.IsStructElement, varTypes);
    shrinkBlock.AddOp(new StdBrOp(storeLabel));

    block = func.Body.AddBlock(storeLabel);
    EmitStructFieldStore(block, newLength, managedVarName, ManagedFieldLength, IrType.I64, varTypes);

    if (slMergeLabel != null) {
      block.AddOp(new StdBrOp(slMergeLabel));
      block = func.Body.AddBlock(slMergeLabel);
    }
  }

  /// <summary>
  /// Clear all elements: vacate every live slot, then set length to 0. Clearing
  /// is just the full-range shrink, so it vacates through the same path — the
  /// managed elements are released AND their slots erased, leaving [0, capacity)
  /// zeroed so a following resize back over them re-exposes zeros rather than the
  /// pointers clear just freed.
  /// </summary>
  private static void LowerManagedMemClear(
    MaxonManagedMemClearOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);

    var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    var zeroStart = new StdConstI64Op(0);
    block.AddOp(zeroStart);
    EmitVacateElementRange(block, managedVarName, zeroStart.Result, length, op.IsStructElement, varTypes);

    // Mark array as empty after element cleanup
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    EmitStructFieldStore(block, zeroConst.Result, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
  }

  private static void LowerPanic(
    MaxonPanicOp op,
    IrBlock<StandardOp> block,
    IrModule<StandardOp> result) {
    // ⭐ THE PANIC LABEL→MESSAGE MAP IS CHECKED HERE, WHERE IT IS ALREADY BEING READ.
    //
    // `MaxonPanicOp` mints a label per distinct message (MaxonDialect.GetOrCreateLabel), so a label
    // already present is expected — and re-emitting the same bytes under it is the dedup working.
    // What must never happen is a DIFFERENT message arriving under a label already taken: only the
    // first entry's bytes are emitted, so the second panic's `lea` resolves to the first one's text
    // and the program prints a message from a function it never called.
    //
    // That is not hypothetical — it is exactly A1m: a cloned panic re-minted its label from a
    // worker thread whose label cache was empty, took a number the parse thread had already given
    // another message, and `Array.resize`'s panic printed `utf16.maxon:59`'s text. Skipping
    // silently is what let it reach a running program, and the spec that pins it can only catch it
    // when the scheduler cooperates (the thread that PARSED the stdlib re-mints to a cache HIT and
    // sees nothing). Refusing here is a check on EVERY compile, on every thread, for free — the
    // lookup was happening anyway.
    var existing = result.SymdataEntries.FirstOrDefault(e => e.label == op.SymdataLabel);
    var messageBytes = System.Text.Encoding.UTF8.GetBytes(op.Message + "\n");
    var cstrBytes = new byte[messageBytes.Length + 1]; // null-terminated
    messageBytes.CopyTo(cstrBytes, 0);

    if (existing.label == null) {
      result.SymdataEntries.Add((op.SymdataLabel, cstrBytes, 1));
    } else if (!existing.bytes.AsSpan().SequenceEqual(cstrBytes)) {
      throw new InvalidOperationException(
        $"panic label '{op.SymdataLabel}' is claimed by two different messages — the emitted "
        + $"binary can only carry one, so the second would print the first's text. Already "
        + $"emitted: {System.Text.Encoding.UTF8.GetString(existing.bytes).TrimEnd('\0', '\n')}; "
        + $"now asked for: {op.Message}. A label is minted per distinct message and must be "
        + "CARRIED by anything that reproduces an op (MaxonPanicOp.CloneKeepingLabel), never "
        + "re-minted from a cache that is not the one it came from.");
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
    // Envelope collapse: the interpolated String IS its __ManagedMemory, so the raw buffer
    // pointer (a null-terminated C string from LowerStringInterp) is read straight off the
    // value at offset 0 — no nested managed pointer to chase.
    var stringVarName = ResolveManagedVarName(op.MessageStruct, valueMap);
    var buffer = (StdI64)EmitStructFieldLoad(block, stringVarName, ManagedFieldBuffer, IrType.I64, varTypes);
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

    // Skip append if other is empty.
    //
    // ⚠ TESTED THE OTHER WAY ROUND — `Then` IS THE FALLTHROUGH EDGE, so it has to be the block added
    // next, and here that is `doAppendLabel` (`skipLabel`'s block is not created until the end of the
    // append body). Written `StdCondBrOp(isEmpty, skipLabel, doAppendLabel)` the emitted x64 read
    // `jne …__append_do_0` immediately followed by `__append_do_0:` — BOTH edges entering the append
    // body, so the skip was dead code and an empty operand ran the whole thing: `maxon_string_ensure_cap`,
    // the parent detach and the capacity rewrite. On an rdata- or slice-backed receiver that is a real
    // COW allocation where the guard promised a no-op. arm64 emits both branches and honoured the skip,
    // so the two targets disagreed. See `EmitDeepCloneManagedElements` for the same defect and
    // `StandardToX86Conversion`'s `case StdCondBrOp` for the rule.
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var hasSomethingToAppend = new StdCmpI64Op("ne", otherLen, zeroConst.Result);
    block.AddOp(hasSomethingToAppend);
    var skipLabel = $"__append_skip_{uid}";
    var doAppendLabel = $"__append_do_{uid}";
    block.AddOp(new StdCondBrOp(hasSomethingToAppend.Result, doAppendLabel, skipLabel));

    var appendBlock = func.Body.AddBlock(doAppendLabel);

    if (op.IsBitPacked) {
      // Bit-packed bool append: bit-by-bit copy loop
      var selfBuf = LoadManagedBuffer(appendBlock, selfVarName, varTypes);
      var selfLen = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldLength, IrType.I64, varTypes);
      var selfCap = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldCapacity, IrType.I64, varTypes);
      var otherLenReload = (StdI64)EmitLoad(appendBlock, otherLenVar, varTypes);

      // Compute new total length
      var totalLen = new StdAddI64Op(selfLen, otherLenReload);
      appendBlock.AddOp(totalLen);
      var totalByteSize = ComputeBitPackedByteSize(appendBlock, totalLen.Result);

      // Ensure capacity (byte-level for bit-packed: use byte sizes)
      var clampedCap = EmitClampCapacityNonNeg(appendBlock, selfCap);
      var selfByteSize = ComputeBitPackedByteSize(appendBlock, selfLen);
      var selfCapBytes = ComputeBitPackedByteSize(appendBlock, clampedCap);

      // The capacity it grows to and the "did it have to grow" test are now the SAME number: an
      // append takes exactly what it needs, so what it needs IS what it takes.
      var growCap = EmitExactAppendCapacity(appendBlock, totalByteSize);
      var requiredCap = growCap;

      // Pass original (unclamped) capacity so ensure_cap correctly skips free for rdata/slice.
      // For bit-packed, selfCap is in elements but ensure_cap only checks sign, so passing
      // the raw element capacity (which is -2 or -1 for rdata/slice) works correctly.
      // parent_ptr lets ensure_cap skip freeing an inline/slice-owned buffer (see EmitReleaseParentOnDetach).
      var selfParent = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldParentPtr, IrType.I64, varTypes);
      var newBuf = new StdI64(IrContext.Current.NextStdId());
      appendBlock.AddOp(new StdCallRuntimeOp("maxon_string_ensure_cap",
        [selfBuf, selfByteSize, selfCap, growCap, selfParent], newBuf));
      // An inline array detached by this grow (buffer changed) becomes a plain external owner.
      EmitReleaseParentOnDetach(appendBlock, selfVarName, selfBuf, newBuf, varTypes);

      // Spill values for the loop. The source buffer is read only AFTER self.buffer is published,
      // because `other` may BE `self` — see PublishGrownBufferAndLoadSource.
      var newBufVar = $"__append_buf_{uid}";
      EmitStore(appendBlock, newBuf, newBufVar, varTypes);
      var otherBufVar = $"__append_otherbuf_{uid}";
      EmitStore(appendBlock,
        PublishGrownBufferAndLoadSource(appendBlock, selfVarName, otherVarName, newBuf, varTypes),
        otherBufVar, varTypes);
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

      // buffer was published before the copy loop; only length and capacity are left.
      block = func.Body.AddBlock(loopExitLabel);
      EmitStructFieldStore(block, totalLen.Result, selfVarName, ManagedFieldLength, IrType.I64, varTypes);
      // Update capacity: use totalLen if grew (conservative)
      var grewCmp = new StdCmpU64Op("ugt", requiredCap, selfCapBytes);
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
      var otherLenReload = (StdI64)EmitLoad(appendBlock, otherLenVar, varTypes);

      // Spill values that are needed after ensure_cap call (which clobbers registers). `other`'s
      // BUFFER is deliberately not among them — see PublishGrownBufferAndLoadSource.
      var selfLenVar = $"__append_selflen_{uid}";
      var selfCapVar = $"__append_selfcap_{uid}";
      var selfBufVar = $"__append_selfbuf_{uid}";
      var elemSizeVar = $"__append_elemsize_{uid}";
      EmitStore(appendBlock, selfLen, selfLenVar, varTypes);
      EmitStore(appendBlock, selfCap, selfCapVar, varTypes);
      EmitStore(appendBlock, selfBuf, selfBufVar, varTypes);
      EmitStore(appendBlock, elemSize, elemSizeVar, varTypes);

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

      // The capacity it grows to and the "did it have to grow" test are now the SAME number: an
      // append takes exactly what it needs, so what it needs IS what it takes.
      var growByteCap = EmitExactAppendCapacity(appendBlock, totalLenBytes.Result);
      var growByteCapVar = $"__append_growcap_{uid}";
      EmitStore(appendBlock, growByteCap, growByteCapVar, varTypes);
      var reqByteCapVar = growByteCapVar;

      // Call maxon_string_ensure_cap(buffer, lengthBytes, capacity, growByteCap) -> newBuffer
      // Pass original (unclamped) capacity so ensure_cap correctly skips free for rdata/slice.
      // ensure_cap only checks the sign of capacity, so element-based values work fine.
      var callBuf = (StdI64)EmitLoad(appendBlock, selfBufVar, varTypes);
      var callLen = selfLenBytes.Result;
      var callCap = selfCap;
      var callGrow = (StdI64)EmitLoad(appendBlock, growByteCapVar, varTypes);
      // parent_ptr lets ensure_cap skip freeing an inline/slice-owned buffer (see EmitReleaseParentOnDetach).
      var callParent = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldParentPtr, IrType.I64, varTypes);
      var newBuf = new StdI64(IrContext.Current.NextStdId());
      appendBlock.AddOp(new StdCallRuntimeOp("maxon_string_ensure_cap",
        [callBuf, callLen, callCap, callGrow, callParent], newBuf));
      var newBufVar = $"__append_buf_{uid}";
      EmitStore(appendBlock, newBuf, newBufVar, varTypes);
      // An inline array/string detached by this grow (buffer changed) becomes a plain external owner.
      EmitReleaseParentOnDetach(appendBlock, selfVarName, callBuf, newBuf, varTypes);

      // Publish self.buffer, then read other.buffer — in that order, because they may be the same
      // field. See PublishGrownBufferAndLoadSource.
      var reloadNewBuf = (StdI64)EmitLoad(appendBlock, newBufVar, varTypes);
      var srcBuf = PublishGrownBufferAndLoadSource(appendBlock, selfVarName, otherVarName, reloadNewBuf, varTypes);

      // Memcpy: other.buffer -> newBuffer + selfLen * elemSize
      var reloadSelfLen = (StdI64)EmitLoad(appendBlock, selfLenVar, varTypes);
      var reloadElemSize = (StdI64)EmitLoad(appendBlock, elemSizeVar, varTypes);
      var offsetBytes = new StdMulI64Op(reloadSelfLen, reloadElemSize);
      appendBlock.AddOp(offsetBytes);
      var dstAddr = new StdAddI64Op(reloadNewBuf, offsetBytes.Result);
      appendBlock.AddOp(dstAddr);
      var reloadOtherLen = (StdI64)EmitLoad(appendBlock, otherLenVar, varTypes);
      var reloadElemSize2 = (StdI64)EmitLoad(appendBlock, elemSizeVar, varTypes);
      var copyBytes = new StdMulI64Op(reloadOtherLen, reloadElemSize2);
      appendBlock.AddOp(copyBytes);
      appendBlock.AddOp(new StdMemCopyOp(srcBuf, dstAddr.Result, copyBytes.Result));

      // Update self: length, capacity (buffer was published above, before the copy)
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
        var eightConst = new StdConstI64Op(ManagedElementPointerSize);
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
  /// the concrete managed struct type's "Element" type parameter. The representation
  /// rules themselves live in ManagedElementInfo — shared with the parser's for-in
  /// lowering and with monomorphization, which must reach the same answer.
  /// </summary>
  private static (MaxonValueKind kind, string? typeParamName, bool isBitPacked, bool isStructElem, string? structElemTypeName, IrType? elementStorageType) DeriveManagedElementInfo(
    MaxonValue managedArg,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, IrType> typeDefs) {
    var structTypeName = (valueMap[managedArg] as StdHeapPtr)?.TypeName
      ?? throw new InvalidOperationException($"Managed arg %{managedArg.Id} has no TypeName in valueMap");
    // A fused String/Character IS its own __ManagedMemory of UTF-8 BYTES. Its type carries an
    // `Element` type param from `Iterable with Character`, but that is the grapheme it yields, NOT
    // its buffer element — treating the bytes as Character pointers would incref raw text. Bytes.
    if (IsFusedStringType(structTypeName) || IsFusedCharType(structTypeName))
      return (MaxonValueKind.Integer, null, false, false, null, null);
    if (typeDefs.TryGetValue(structTypeName, out var typeInfo)
      && typeInfo is IrStructType structType
      && structType.TypeParams.TryGetValue(IrStructType.ElementTypeParamName, out var elemType)) {
      var info = ManagedElementInfo.FromElementType(elemType);
      return (info.Kind, null, info.Kind == MaxonValueKind.Bool,
              info.IsStructElement, info.StructElementTypeName, info.ElementStorageType);
    }
    // Bare __ManagedMemory with no Element type param (raw byte buffer)
    return (MaxonValueKind.Integer, null, false, false, null, null);
  }

  /// <summary>
  /// The `<Element>.clone` a copy of `managedTypeName`'s buffer deep-clones each element through,
  /// or null when the elements are carried into the copy by RETAIN instead.
  ///
  /// Reads ManagedElementCopy so the answer is the one dead-function elimination already pinned:
  /// re-deriving the rule here is how a call to a deleted symbol, or a silent downgrade from a
  /// deep clone to an alias, would get in.
  /// </summary>
  private static string? ManagedElementCloneCallee(string managedTypeName) {
    var module = _sourceModule
      ?? throw new InvalidOperationException("Managed element clone lookup ran outside MaxonToStandardConversion.Run");
    return ManagedElementCopy.ClonerNameFor(module, ManagedElementCopy.ElementTypeOf(module, managedTypeName));
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
          ElementClonerName = ManagedElementCloneCallee(sliceConcreteTypeName),
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
        var (_, _, isBitPacked, isGrowStructElem, _, _) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var growOp = new MaxonManagedMemGrowOp(args[0], args[1]) {
          IsBitPacked = isBitPacked,
          IsStructElement = isGrowStructElem
        };
        LowerManagedMemGrow(growOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        return true;
      }
      case "__managed_mem_set_length": {
        // The element class decides how a SHRINK vacates the dropped slots:
        // refcounted pointers must be released before the slot is erased.
        var (_, _, _, isSetLenStructElem, _, _) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var setLenOp = new MaxonManagedMemSetLengthOp(args[0], args[1]) {
          IsStructElement = isSetLenStructElem
        };
        LowerManagedMemSetLength(setLenOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        return true;
      }
      case "__managed_mem_shift_right": {
        var (_, _, isBitPacked, isShiftRightStructElem, _, _) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var shiftOp = new MaxonManagedMemShiftOp(args[0], args[1], args[2], shiftRight: true) {
          IsBitPacked = isBitPacked,
          IsStructElement = isShiftRightStructElem
        };
        LowerManagedMemShift(shiftOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        return true;
      }
      case "__managed_mem_shift_left": {
        var (_, _, isBitPacked, isShiftLeftStructElem, _, _) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var shiftOp = new MaxonManagedMemShiftOp(args[0], args[1], args[2], shiftRight: false) {
          IsBitPacked = isBitPacked,
          IsStructElement = isShiftLeftStructElem
        };
        LowerManagedMemShift(shiftOp, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        return true;
      }
      case "__managed_mem_swap": {
        // Slot exchange. The bootstrap's refcount model balances a get+set
        // exchange on its own (loads take refs, stores release the displaced
        // occupant's), so lower swap as exactly that sequence here. The
        // self-hosted compiler instead routes to the raw byte-exchange helper
        // (`stdlib.__managed_mem_swap`) because its move-model `set` frees the
        // displaced occupant while the swap still aliases it.
        var (elementKind, typeParamName, _, isStructElem, structElemTypeName, elementStorageType) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        var getI = new MaxonManagedMemGetOp(args[0], args[1], elementKind) {
          TypeParamName = typeParamName,
          IsStructElement = isStructElem,
          StructElementTypeName = structElemTypeName,
          ElementStorageType = elementStorageType,
          IsBoundsCheckSafe = false
        };
        LowerManagedMemGet(getI, func, ref block, valueMap, varTypes, temps, errorFlagValue: errorFlagValue);
        var getJ = new MaxonManagedMemGetOp(args[0], args[2], elementKind) {
          TypeParamName = typeParamName,
          IsStructElement = isStructElem,
          StructElementTypeName = structElemTypeName,
          ElementStorageType = elementStorageType,
          IsBoundsCheckSafe = false
        };
        LowerManagedMemGet(getJ, func, ref block, valueMap, varTypes, temps, errorFlagValue: errorFlagValue);
        var setI = new MaxonManagedMemSetOp(args[0], args[1], getJ.Result, elementKind) {
          TypeParamName = typeParamName,
          IsStructElement = isStructElem,
          ElementStorageType = elementStorageType
        };
        LowerManagedMemSet(setI, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
        var setJ = new MaxonManagedMemSetOp(args[0], args[2], getI.Result, elementKind) {
          TypeParamName = typeParamName,
          IsStructElement = isStructElem,
          ElementStorageType = elementStorageType
        };
        LowerManagedMemSet(setJ, func, ref block, valueMap, varTypes, errorFlagValue: errorFlagValue);
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
        LowerManagedMemCreate(createOp, func, ref block, valueMap, varTypes, temps,
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
    Dictionary<string, IrType> typeDefs,
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
        var (_, _, _, _, _, peekStorageType) = DeriveManagedElementInfo(args[0], valueMap, typeDefs);
        LowerCursorPeekCall(args, result, resultKind.Value, peekStructTypeName, peekStorageType, block, valueMap, varTypes, errorFlagValue, temps);
        return true;
      default:
        return false;
    }
  }

  /// <summary>
  /// Lowers the desugared checked-division builtins __checked_div / __checked_mod (see
  /// MaxonCheckedDivTryCallOp). A possibly-zero `a / b` / `a mod b` becomes: compare the divisor to
  /// 0, and
  ///   • on ZERO, set the error flag to __DivisionByZeroError.divisionByZero (ordinal 0 → flag 1)
  ///     and leave the result at a defined 0 WITHOUT executing the divide (x64 `idiv` by zero
  ///     raises #DE — the whole point of the throw is to never reach it);
  ///   • on NON-ZERO, run the real signed/unsigned Div/Rem — byte-for-byte the bare divide the
  ///     provably-non-zero path emits for the same operands.
  ///
  /// The op's ResultKind selects the operand type: an integer divide compares and divides in i64, a
  /// FLOAT divide (`__checked_div` with a Float/Float32 result) in f64/f32 — including the zero test,
  /// which is a FLOAT compare `divisor == 0.0` and NEVER an integer bit-compare, so it catches both
  /// `+0.0` and `-0.0` (IEEE makes them equal) and stops the ±inf a bare float divide would produce.
  ///
  /// The block is split entry → {zero, ok} → merge, exactly like the OOB path in LowerManagedMemGet:
  /// both arms store into a result temp the merge reloads, so the caller sees one value whichever
  /// path ran.
  /// </summary>
  private static bool TryLowerCheckedDivMod(
    string callee,
    List<MaxonValue> args,
    MaxonValue? result,
    ref IrBlock<StandardOp> block,
    IrFunction<StandardOp> func,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue,
    MaxonCallOp? sourceCallOp) {
    if (callee is not ("__checked_div" or "__checked_mod")) return false;

    // The op carries the operand kind + signedness the divide needs; lowering has already discarded
    // the ranged type it came from, so it must ride on the op. A checked divide is ALWAYS a try-call,
    // so the metadata is always present — its absence is a compiler bug, not an input error.
    if (sourceCallOp is not MaxonCheckedDivTryCallOp checkedDiv)
      throw new InvalidOperationException($"'{callee}' must be lowered from a MaxonCheckedDivTryCallOp");

    var kind = checkedDiv.ResultKind!.Value;
    var dividend = valueMap[args[0]];
    var divisor = valueMap[args[1]];

    // Error flag: divisionByZero (ordinal 0) → 1. Computed once here; both arms and the merge see it.
    var isZero = EmitDivisorZeroTest(block, kind, divisor);
    EmitBoundsCheckErrorFlag(block, isZero, 1, valueMap, varTypes, errorFlagValue);

    // Result temp, seeded to a typed 0 so the error (zero-divisor) path leaves a defined value.
    var uid = IrContext.Current.NextId();
    var resultTemp = $"__checked_div_result_{uid}";
    var seed = EmitNumericZeroConst(block, kind);
    EmitStore(block, seed, resultTemp, varTypes);

    var zeroLabel = $"__div_zero_{uid}";
    var okLabel = $"__div_ok_{uid}";
    var mergeLabel = $"__div_merge_{uid}";
    block.AddOp(new StdCondBrOp(isZero, zeroLabel, okLabel));

    // Zero path: no divide (idiv would fault); the temp is already 0. Branch to merge.
    var zeroBlock = func.Body.AddBlock(zeroLabel);
    zeroBlock.AddOp(new StdBrOp(mergeLabel));

    // Non-zero path: the real divide, then store to the temp. Reuse the exact factories the bare
    // MaxonBinOp.Div/.Rem path uses, so a checked divide and a bare one emit identical arithmetic.
    block = func.Body.AddBlock(okLabel);
    var quotient = EmitCheckedDivideOp(block, checkedDiv, dividend, divisor);
    EmitStore(block, quotient, resultTemp, varTypes);
    block.AddOp(new StdBrOp(mergeLabel));

    // Merge: reload the flag and the result so both paths converge on one value each.
    block = func.Body.AddBlock(mergeLabel);
    if (errorFlagValue != null) {
      var mergedFlag = (StdI64)EmitLoad(block, "__error_flag", varTypes);
      valueMap[errorFlagValue] = mergedFlag;
    }
    var mergedResult = EmitLoad(block, resultTemp, varTypes);
    if (result != null) valueMap[result] = mergedResult;

    return true;
  }

  /// The divisor-is-zero test for a checked divide, dispatched on the operand kind. FLOAT uses a
  /// float compare `divisor == 0.0` — never an integer bit-compare: IEEE makes `-0.0 == +0.0`, and
  /// both `x / ±0.0` are the degenerate ±inf the throw exists to replace, so both must read as zero.
  private static StdBool EmitDivisorZeroTest(IrBlock<StandardOp> block, MaxonValueKind kind, StdValue divisor) {
    switch (kind) {
      case MaxonValueKind.Float: {
        var cmp = new StdCmpF64Op("eq", (StdF64)divisor, (StdF64)EmitNumericZeroConst(block, kind));
        block.AddOp(cmp);
        return cmp.Result;
      }
      case MaxonValueKind.Float32: {
        var cmp = new StdCmpF32Op("eq", (StdF32)divisor, (StdF32)EmitNumericZeroConst(block, kind));
        block.AddOp(cmp);
        return cmp.Result;
      }
      case MaxonValueKind.Integer:
      case MaxonValueKind.Short: {
        var cmp = new StdCmpI64Op("eq", (StdI64)divisor, (StdI64)EmitNumericZeroConst(block, kind));
        block.AddOp(cmp);
        return cmp.Result;
      }
      default:
        throw new InvalidOperationException($"checked divide over non-numeric kind {kind}");
    }
  }

  /// A typed literal 0 for the checked divide's zero test and its result-temp seed. Shorts share the
  /// integer form: they occupy an i64 register at the Standard tier exactly as integers do.
  private static StdValue EmitNumericZeroConst(IrBlock<StandardOp> block, MaxonValueKind kind) {
    switch (kind) {
      case MaxonValueKind.Float: { var c = new StdConstF64Op(0.0); block.AddOp(c); return c.Result; }
      case MaxonValueKind.Float32: { var c = new StdConstF32Op(0f); block.AddOp(c); return c.Result; }
      case MaxonValueKind.Integer:
      case MaxonValueKind.Short: { var c = new StdConstI64Op(0); block.AddOp(c); return c.Result; }
      default:
        throw new InvalidOperationException($"checked divide over non-numeric kind {kind}");
    }
  }

  /// The real divide on the non-zero path, dispatched on the op's kind + signedness. Reuses the exact
  /// factories the bare MaxonBinOp.Div/.Rem path uses, so a checked divide and a provably-safe bare
  /// one emit identical arithmetic. `mod` is integer-only (no float remainder operator exists).
  private static StdValue EmitCheckedDivideOp(IrBlock<StandardOp> block,
      MaxonCheckedDivTryCallOp checkedDiv, StdValue dividend, StdValue divisor) {
    var divOp = checkedDiv.IsMod ? MaxonBinOperator.Mod : MaxonBinOperator.Div;
    (StandardOp Op, StdValue Result) emitted = checkedDiv.ResultKind switch {
      MaxonValueKind.Float => BinOpFactories[(divOp, MaxonValueKind.Float)](dividend, divisor),
      MaxonValueKind.Float32 => BinOpFactories[(divOp, MaxonValueKind.Float32)](dividend, divisor),
      MaxonValueKind.Integer or MaxonValueKind.Short => checkedDiv.IsUnsigned
        ? CreateUnsignedIntBinOp(divOp, (StdI64)dividend, (StdI64)divisor)
        : BinOpFactories[(divOp, MaxonValueKind.Integer)](dividend, divisor),
      _ => throw new InvalidOperationException(
        $"checked divide over non-numeric kind {checkedDiv.ResultKind}"),
    };
    block.AddOp(emitted.Op);
    return emitted.Result;
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

    // THROW rather than default to Windows. `Convert` sets `_currentTarget` unconditionally before
    // anything reaches here, so a null is a pass reaching this emitter from outside conversion — and
    // the old `?? "windows"` answered that by silently emitting the Win32 errno table into a macOS or
    // Linux binary, which is a WRONG ANSWER at run time and nothing anywhere would have reported it.
    // The comparison is against the roster's own spelling, not a bare literal with a culture-sensitive
    // compare: `CompileTarget.Os` only ever holds one of those constants.
    var target = _currentTarget
      ?? throw new InvalidOperationException(
        "SelectIoErrorOrdinal: no active CompileTarget — the io-error mapping table is chosen by the "
        + "target's OS, and there is no honest default for it");
    var entries = target.Os == CompileTarget.WindowsOs ? _win32IoErrorTags : _posixIoErrorTags;

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
      op.IsStructElement, op.StructElementTypeName, op.ElementStorageType, op.Result, valueMap, varTypes, temps, "ccur");
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
    IrType? elementStorageType,
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
      // Prefer the precise narrow storage type when supplied (mirrors LowerManagedMemGet's
      // behavior). Without it, a u32 element loads as i64 and reads adjacent slot bits.
      var elemType = elementStorageType ?? GetManagedMemElementType(resultKind, "EmitCursorElementLoad");
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
    IrType? elementStorageType,
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
      isStructElement, structElementTypeName, elementStorageType, result, valueMap, varTypes,
      temps, tempPrefix: "cpeek");
  }
}
