using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static partial class MaxonToStandardConversion {
	// ============================================================================
	// Managed memory lowering helpers
	// ============================================================================

	/// <summary>
	/// Emit a runtime bounds check: panics if (unsigned)index >= (unsigned)limit.
	/// Uses the maxon_bounds_check runtime function with a pre-defined panic message.
	/// </summary>
	private static void EmitBoundsCheck(
	  MlirBlock<StandardOp> block, StdI64 index, StdI64 limit, string panicSymdataLabel) {
		var leaOp = new StdLeaSymdataOp(panicSymdataLabel);
		block.AddOp(leaOp);
		var ptrToI64 = new StdPtrToI64Op(leaOp.Result);
		block.AddOp(ptrToI64);
		block.AddOp(new StdCallRuntimeOp("maxon_bounds_check", [index, limit, ptrToI64.Result], null));
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
	  MlirBlock<StandardOp> block,
	  string managedVarName,
	  Dictionary<string, string> varTypes) {
		return (StdI64)EmitStructFieldLoad(block, managedVarName, 0, MlirType.I64, varTypes);
	}

	/// <summary>
	/// Compute address: buffer + index * elementSize (runtime element size)
	/// </summary>
	private static StdI64 ComputeElementAddress(
	  MlirBlock<StandardOp> block,
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
	private static StdI64 ComputeBitPackedByteSize(MlirBlock<StandardOp> block, StdI64 count) {
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
	private static StdI64 ComputeByteLimit(MlirBlock<StandardOp> block, StdI64 length, StdI64 elemSize) {
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
	/// Extract a single bit from a bit-packed buffer. Returns 0 or 1 as i64.
	/// Computes: (buffer[index >> 3] >> (index &amp; 7)) &amp; 1
	/// </summary>
	private static StdI64 EmitBitGet(MlirBlock<StandardOp> block, StdI64 buffer, StdI64 index) {
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
		var loadOp = new StdLoadIndirectOp(addr.Result, 0, MlirType.I8);
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
	private static void EmitBitSet(MlirBlock<StandardOp> block, StdI64 buffer, StdI64 index, StdI64 value) {
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
		// Load current byte
		var loadOp = new StdLoadIndirectOp(addr.Result, 0, MlirType.I8);
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
		block.AddOp(new StdStoreIndirectOp(newByte.Result, addr.Result, 0, MlirType.I8));
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
	  MlirFunction<StandardOp> func,
	  ref MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
		var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
		var index = (StdI64)valueMap[op.Index];
		EmitBoundsCheck(block, index, length, "__mm_panic_index_oob");
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var addr = ComputeElementAddress(block, buffer, index, elemSize);

				if (op.ResultKind == MaxonValueKind.Bool) {
			// Bit-packed bool: extract bit at index from the packed buffer
			var bitResult = EmitBitGet(block, buffer, index);
			valueMap[op.Result] = bitResult;
		} else if (op.IsStructElement) {
			// Struct elements are heap pointers stored in the buffer (8 bytes each).
			// Load the pointer and incref — the caller gets their own reference.
			// The buffer retains its reference; mm_decref_managed_elements handles
			// the buffer's copy when the array is freed.
			var loadOp = new StdLoadIndirectOp(addr, 0, MlirType.I64);
			block.AddOp(loadOp);

			// Slots can be zero after resize() or remove() — increfing a null pointer
			// would corrupt the reference count. Return ArrayError.emptySlot so callers
			// using try/otherwise can handle sparse arrays without undefined behaviour.
			// Error flag = ArrayError.emptySlot ordinal (1) + 1 = 2 (0 = success convention).
			var zeroForNull = new StdConstI64Op(0);
			block.AddOp(zeroForNull);
			var isNullCmp = new StdCmpI64Op("eq", (StdI64)loadOp.Result, zeroForNull.Result);
			block.AddOp(isNullCmp);
			var nullUid = MlirContext.Current.NextId();
			var slotEmptyBlock = $"__slot_empty_{nullUid}";
			var slotNonnullBlock = $"__slot_nonnull_{nullUid}";
			block.AddOp(new StdCondBrOp(isNullCmp.Result, slotEmptyBlock, slotNonnullBlock));
			var errBlock = func.Body.AddBlock(slotEmptyBlock);
			var errFlagConst = new StdConstI64Op(2);
			errBlock.AddOp(errFlagConst);
			errBlock.AddOp(new StdErrorReturnOp(errFlagConst.Result));
			block = func.Body.AddBlock(slotNonnullBlock);

			EmitIncrefValue(block, (StdI64)loadOp.Result, scopeName: _currentFuncName);

			var tempId = MlirContext.Current.NextId();
			var tempName = temps.CreateTemp("mmget", tempId, op.StructElementTypeName ?? "unknown", OwnershipFlags.Orphan | OwnershipFlags.OwnsRef);
			EmitStore(block, (StdI64)loadOp.Result, tempName, varTypes);
			valueMap[op.Result] = new StdHeapPtr(loadOp.Result.Id, op.StructElementTypeName ?? "unknown", tempName);
		} else {
			// Determine load type based on result kind
			// For byte/bool, use I8 which triggers zero-extending byte load in x86 codegen
			var elemType = GetManagedMemElementType(op.ResultKind, "LowerManagedMemGet");
			var loadOp = new StdLoadIndirectOp(addr, 0, elemType);
			block.AddOp(loadOp);
			valueMap[op.Result] = loadOp.Result;
		}
	}

	/// <summary>
	/// __managed_memory_remove(managed, index): remove element at index with ownership transfer.
	/// Loads the element (without incref — the buffer's reference is transferred to the caller),
	/// zeroes the slot, shifts remaining elements left, and decrements length.
	/// </summary>
	private static void LowerManagedMemRemove(
	  MaxonManagedMemRemoveOp op,
	  MlirFunction<StandardOp> func,
	  ref MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
		var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
		var index = (StdI64)valueMap[op.Index];

		// Bounds check: if index >= length, return error (ArrayError.outOfBounds = ordinal 0, flag = 1)
		var cmpOp = new StdCmpI64Op("lt", index, length);
		block.AddOp(cmpOp);
		var uid = MlirContext.Current.NextId();
		var oobBlock = $"__remove_oob_{uid}";
		var inBoundsBlock = $"__remove_ok_{uid}";
		block.AddOp(new StdCondBrOp(cmpOp.Result, inBoundsBlock, oobBlock));
		// Add in-bounds block first so it's the fall-through target after the conditional jump
		block = func.Body.AddBlock(inBoundsBlock);
		var errBlock = func.Body.AddBlock(oobBlock);
		var errFlag = new StdConstI64Op(1);
		errBlock.AddOp(errFlag);
		errBlock.AddOp(new StdErrorReturnOp(errFlag.Result));
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// COW check before mutation
		EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: op.ResultKind == MaxonValueKind.Bool);

		// Reload buffer/length after COW (COW may change the buffer pointer)
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var lengthAfterCow = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);

		if (op.ResultKind == MaxonValueKind.Bool) {
			// Bit-packed bool: extract bit at index, then shift remaining bits left
			var bitResult = EmitBitGet(block, buffer, index);
			valueMap[op.Result] = bitResult;

			// Shift bits left: for i from index to length-2, copy bit[i+1] to bit[i]
			var oneConst = new StdConstI64Op(1);
			block.AddOp(oneConst);
			var newLength = new StdSubI64Op(lengthAfterCow, oneConst.Result);
			block.AddOp(newLength);

			// Loop: i = index; while (i < newLength) { bit[i] = bit[i+1]; i++ }
			var loopUid = MlirContext.Current.NextId();
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
			EmitStructFieldStore(block, finalNewLen, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
		} else {
			var addr = ComputeElementAddress(block, buffer, index, elemSize);

			if (op.IsStructElement) {
				// Load the struct pointer — ownership transfer, NO incref.
				// The buffer's reference is handed to the caller.
				var loadOp = new StdLoadIndirectOp(addr, 0, MlirType.I64);
				block.AddOp(loadOp);

				// Zero the slot to prevent mm_decref_managed_elements from touching it
				var zeroOp = new StdConstI64Op(0);
				block.AddOp(zeroOp);
				block.AddOp(new StdStoreIndirectOp(zeroOp.Result, addr, 0, MlirType.I64));

				var tempId = MlirContext.Current.NextId();
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
				block.AddOp(new StdStoreIndirectOp(zeroOp2.Result, lastAddr, 0, MlirType.I64));
			}

			// Update length
			EmitStructFieldStore(block, newLength.Result, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
		}
	}

	/// <summary>
	/// __managed_memory_set_at(managed, index, value): store element into heap buffer.
	/// For struct elements, decrefs the old occupant before storing the new pointer.
	/// </summary>
	private static void LowerManagedMemSet(
	  MaxonManagedMemSetOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
		var isBitPacked = op.ElementKind == MaxonValueKind.Bool;
				EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: isBitPacked);
		// Check against capacity after COW (COW updates capacity from 0 to length)
		var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var index = (StdI64)valueMap[op.Index];
		EmitBoundsCheck(block, index, capacity, "__mm_panic_index_oob");
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
			var oldElemLoad = new StdLoadIndirectOp(addr, 0, MlirType.I64);
			block.AddOp(oldElemLoad);
			EmitDecrefValueIfNonnull(block, (StdI64)oldElemLoad.Result, scopeName: _currentFuncName);
			var srcName = ResolveManagedVarName(op.Value, valueMap);
			var srcHeapPtr = EmitLoad(block, srcName, varTypes);
			block.AddOp(new StdStoreIndirectOp(srcHeapPtr, addr, 0, MlirType.I64));
			EmitIncrefValue(block, (StdI64)srcHeapPtr, scopeName: _currentFuncName);
		} else {
			var addr = ComputeElementAddress(block, buffer, index, elemSize);
			// Scalar elements: store directly
			var value = valueMap[op.Value];
			var elemType = GetManagedMemElementType(op.ElementKind, "LowerManagedMemSet");
			block.AddOp(new StdStoreIndirectOp(value, addr, 0, elemType));
		}
	}

	/// <summary>
	/// __managed_memory_create(count, elementSize): allocate heap buffer.
	/// Returns new __ManagedMemory struct (buffer, length, capacity, element_size).
	/// </summary>
	private static void LowerManagedMemCreate(
	  MaxonManagedMemCreateOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps,
	  string? inlineTarget = null) {
				if (!op.IsBitPacked && op.ElementSize <= 0)
			throw new InvalidOperationException($"MaxonManagedMemCreateOp has invalid element_size={op.ElementSize} in func {_currentFuncName}");
		var count = (StdI64)valueMap[op.Count];

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
		var managedPtr = EmitAlloc(block, 32, "__ManagedMemory", scopeName: _currentFuncName);
		EmitStore(block, managedPtr, tempName, varTypes);
		var allocResult = EmitRawAlloc(block, byteSize, label: "ManagedMemory.buf", scopeName: _currentFuncName);
		// Store fields via indirect access on the heap object
		EmitStructFieldStore(block, allocResult, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, count, tempName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, count, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
		EmitStructFieldStore(block, elemSizeValue, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
		valueMap[op.Result] = new StdHeapPtr(managedPtr.Id, "__ManagedMemory", tempName);
	}

	/// <summary>
	/// __managed_memory_grow(managed, newCapacity): grow heap buffer to new capacity.
	/// Uses realloc to grow (or allocate) the buffer, then updates managed struct fields.
	/// Element size is read from the managed struct's element_size field.
	/// </summary>
	private static void LowerManagedMemGrow(
	  MaxonManagedMemGrowOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);

		// Load element_size from the managed struct via heap pointer
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// Validate newCapacity >= currentCapacity (before COW check, which may change capacity)
		var oldCap = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var newCap = (StdI64)valueMap[op.NewCapacity];
		// Check: oldCap < newCap + 1, i.e. oldCap <= newCap. Use unsigned compare.
		var oneConst = new StdConstI64Op(1);
		block.AddOp(oneConst);
		var newCapPlusOne = new StdAddI64Op(newCap, oneConst.Result);
		block.AddOp(newCapPlusOne);
		EmitBoundsCheck(block, oldCap, newCapPlusOne.Result, "__mm_panic_grow_shrink");

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
		var newBufferResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("mm_raw_realloc", [oldBuffer, newByteSize, growManagedPtr], newBufferResult));

		// Update managed struct fields through heap pointer
		var newBufReload = newBufferResult;
		EmitStructFieldStore(block, newBufReload, managedVarName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, newCap, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
		// No write-through needed: with heap refs, all field stores go through
		// the heap pointer directly, so the caller sees changes automatically.
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
	  MlirFunction<StandardOp> func,
	  ref MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
		EmitCowCheck(block, managedVarName, varTypes, elemSize, isBitPacked: op.IsBitPacked);
		// Check after COW (COW updates capacity from 0 to length)
		var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var index = (StdI64)valueMap[op.Index];
		var count = (StdI64)valueMap[op.Count];
		EmitBoundsCheck(block, index, capacity, "__mm_panic_shift_oob");
		var endOp = new StdAddI64Op(index, count);
		block.AddOp(endOp);
		EmitBoundsCheck(block, endOp.Result, capacity, "__mm_panic_shift_oob");
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);

		if (op.IsBitPacked) {
			// Bit-packed bool: bit-by-bit loop
			var loopUid = MlirContext.Current.NextId();
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
			var srcAddrVarName = $"__shift_src_{MlirContext.Current.NextId()}";
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
			block.AddOp(new StdStoreIndirectOp(zeroOp.Result, reloadedSrcAddr, 0, MlirType.I64));
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
			var dstAddrVarName = $"__shift_dst_{MlirContext.Current.NextId()}";
			EmitStore(block, dstAddr.Result, dstAddrVarName, varTypes);
			var bytesVarName = $"__shift_bytes_{MlirContext.Current.NextId()}";
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
			block.AddOp(new StdStoreIndirectOp(zeroOp.Result, lastSlotAddr.Result, 0, MlirType.I64));
		}
	}

	/// <summary>
	/// __managed_memory_byte_at(managed, index): load a single byte from the managed buffer.
	/// Returns the byte zero-extended to i64.
	/// </summary>
	private static void LowerManagedMemByteGet(
	  MaxonManagedMemByteGetOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
		var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
		var byteLimit = ComputeByteLimit(block, length, elemSize);
		var index = (StdI64)valueMap[op.Index];
		EmitBoundsCheck(block, index, byteLimit, "__mm_panic_byte_oob");
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		// Compute address: buffer + index (element size is 1 byte)
		var addrOp = new StdAddI64Op(buffer, index);
		block.AddOp(addrOp);
		var loadOp = new StdLoadIndirectOp(addrOp.Result, 0, MlirType.I8);
		block.AddOp(loadOp);
		valueMap[op.Result] = loadOp.Result;
	}

	/// <summary>
	/// Emit a COW (copy-on-write) check for a managed memory struct.
	/// If capacity == 0, the buffer is read-only (rdata) and must be copied to a writable heap allocation.
	/// Updates buffer and capacity fields on the managed struct (and writes through to self if needed).
	/// Element size is passed dynamically (read from the struct's element_size field).
	/// </summary>
	private static void EmitCowCheck(
	  MlirBlock<StandardOp> block,
	  string managedVarName,
	  Dictionary<string, string> varTypes,
	  StdI64 elemSize,
	  bool isBitPacked = false) {
		var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);

		var uid = MlirContext.Current.NextId();
		var cowLenVar = $"__cow_len_{uid}";
		EmitStore(block, length, cowLenVar, varTypes);

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
		var newBuffer = new StdI64(MlirContext.Current.NextId());
		// Args: buffer, capacity, byteLen, managedPtr (4 register args, no stack args)
		block.AddOp(new StdCallRuntimeOp("maxon_cow_check", [oldBuffer, capacity, byteLen, managedPtr], newBuffer));

		EmitStructFieldStore(block, newBuffer, managedVarName, ManagedFieldBuffer, MlirType.I64, varTypes);
		// If COW triggered (capacity was 0), new capacity = length; otherwise keep original
		var zeroConst = new StdConstI64Op(0);
		block.AddOp(zeroConst);
		var cmpOp = new StdCmpI64Op("eq", capacity, zeroConst.Result);
		block.AddOp(cmpOp);
		var lenReload = (StdI64)EmitLoad(block, cowLenVar, varTypes);
		var selectOp = new StdSelectI64Op(cmpOp.Result, lenReload, capacity);
		block.AddOp(selectOp);
		EmitStructFieldStore(block, selectOp.Result, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
		// No write-through needed: heap refs ensure mutations are visible to callers.
	}

	/// <summary>
	/// __managed_memory_set_byte(managed, index, value): store a single byte to the managed buffer.
	/// Performs COW check before writing. Element size is read from the struct for COW allocation.
	/// </summary>
	private static void LowerManagedMemByteSet(
	  MaxonManagedMemByteSetOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
		var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
		var byteLimit = ComputeByteLimit(block, length, elemSize);
		var index = (StdI64)valueMap[op.Index];
		EmitBoundsCheck(block, index, byteLimit, "__mm_panic_byte_oob");
		// ByteGet/ByteSet operate on raw bytes, not logical elements, so COW uses elemSize directly.
		// For bit-packed arrays (elemSize==0), the runtime's maxon_cow_check handles capacity==0 correctly.
		EmitCowCheck(block, managedVarName, varTypes, elemSize);

		// Now perform the actual byte write using the writable buffer
		var bufReload = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldBuffer, MlirType.I64, varTypes);
		var value = valueMap[op.Value];
		var addrOp = new StdAddI64Op(bufReload, index);
		block.AddOp(addrOp);
		block.AddOp(new StdStoreIndirectOp(value, addrOp.Result, 0, MlirType.I8));
	}

	/// <summary>
	/// __cstring_to_managed(cstrPtr): convert a null-terminated C string to __ManagedMemory.
	/// Computes strlen, allocates buffer, copies bytes, returns managed struct.
	/// </summary>
	private static void LowerCStringToManaged(
	  MaxonCStringToManagedOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps,
	  string? inlineTarget = null) {
		var cstrPtr = (StdI64)valueMap[op.CstrPtr];

		// Get string length
		var lenResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_strlen", [cstrPtr], lenResult));

		// Store length so it survives alloc calls
		var lenVar = $"__cstr_len_{op.Result.Id}";
		EmitStore(block, lenResult, lenVar, varTypes);
		var cstrVar = $"__cstr_ptr_{op.Result.Id}";
		EmitStore(block, cstrPtr, cstrVar, varTypes);

		// Allocate __ManagedMemory struct, then raw buffer.
		// Allocate strlen+1 bytes so the null terminator is within the allocation,
		// preventing out-of-bounds reads in maxon_to_cstring.
		var tempName = inlineTarget
			?? temps.CreateTemp("from_cstring", op.Result.Id, "__ManagedMemory", OwnershipFlags.None);
		var managedPtr = EmitAlloc(block, 32, "__ManagedMemory", scopeName: _currentFuncName);
		EmitStore(block, managedPtr, tempName, varTypes);

		var lenReload1 = (StdI64)EmitLoad(block, lenVar, varTypes);
		var oneConst = new StdConstI64Op(1);
		block.AddOp(oneConst);
		var allocSize = new StdAddI64Op(lenReload1, oneConst.Result);
		block.AddOp(allocSize);
		var allocResult = EmitRawAlloc(block, allocSize.Result, label: "CString.buf", scopeName: _currentFuncName);

		// Store buffer pointer (alloc clobbers registers)
		var bufVar = $"__cstr_buf_{op.Result.Id}";
		EmitStore(block, allocResult, bufVar, varTypes);

		// Copy strlen+1 bytes from cstring (includes the null terminator)
		var bufReload = (StdI64)EmitLoad(block, bufVar, varTypes);
		var cstrReload = (StdI64)EmitLoad(block, cstrVar, varTypes);
		var lenReload2 = (StdI64)EmitLoad(block, lenVar, varTypes);
		var copySize = new StdAddI64Op(lenReload2, oneConst.Result);
		block.AddOp(copySize);
		var copyResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_memcpy", [bufReload, cstrReload, copySize.Result], copyResult));

		// Store fields via indirect access on the heap object
		var bufFinal = (StdI64)EmitLoad(block, bufVar, varTypes);
		var lenFinal = (StdI64)EmitLoad(block, lenVar, varTypes);
		var capOp = new StdAddI64Op(lenFinal, oneConst.Result);
		block.AddOp(capOp);
		var elemSizeOp = new StdConstI64Op(1);
		block.AddOp(elemSizeOp);
		EmitStructFieldStore(block, bufFinal, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, lenFinal, tempName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, capOp.Result, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
		EmitStructFieldStore(block, elemSizeOp.Result, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
		valueMap[op.Result] = new StdHeapPtr(managedPtr.Id, "__ManagedMemory", tempName);
	}

	/// <summary>
	/// __managed_memory_to_cstring(managed): return a null-terminated C string pointer.
	/// Calls maxon_to_cstring runtime which checks if buffer[length] is already '\0'.
	/// If so, returns the buffer directly (no allocation). Otherwise, allocates a copy
	/// with null terminator appended. This avoids unnecessary copying for non-slice strings.
	/// </summary>
	private static void LowerManagedToCString(
	  MaxonManagedToCStringOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		var managedVarName = ResolveManagedVarName(op.Managed, valueMap);
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);

		var result = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_to_cstring", [buffer, length], result));
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
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		var managedVarName = ResolveManagedVarName(managedValue, valueMap);
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
		var result = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp(runtimeName, [buffer, length], result));
		valueMap[resultValue] = result;
	}

	private static void LowerManagedWriteStdout(
	  MaxonManagedWriteStdoutOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) =>
		LowerManagedWrite("maxon_managed_write_stdout", op.Managed, op.Result, block, valueMap, varTypes);

	private static void LowerManagedWriteStderr(
	  MaxonManagedWriteStderrOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) =>
		LowerManagedWrite("maxon_managed_write_stderr", op.Managed, op.Result, block, valueMap, varTypes);

	/// <summary>
	/// Set length with capacity validation: panics if newLength > capacity.
	/// </summary>
	private static void LowerManagedMemSetLength(
	  MaxonManagedMemSetLengthOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
		var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var newLength = (StdI64)valueMap[op.NewLength];
		// Check newLength <= capacity: reframe as newLength < capacity + 1
		var oneConst = new StdConstI64Op(1);
		block.AddOp(oneConst);
		var capPlusOne = new StdAddI64Op(capacity, oneConst.Result);
		block.AddOp(capPlusOne);
		EmitBoundsCheck(block, newLength, capPlusOne.Result, "__mm_panic_setlength_oob");
		// Store the new length
		EmitStructFieldStore(block, newLength, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
	}

	/// <summary>
	/// Clear all elements: decref each struct element, then set length to 0.
	/// For primitive elements, simply sets length to 0.
	/// </summary>
	private static void LowerManagedMemClear(
	  MaxonManagedMemClearOp op,
	  MlirBlock<StandardOp> block,
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
		EmitStructFieldStore(block, zeroConst.Result, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
	}

	private static void LowerPanic(
	  MaxonPanicOp op,
	  MlirBlock<StandardOp> block,
	  MlirModule<StandardOp> result) {
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
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		// The interpolated String struct has _managed (a __ManagedMemory pointer) at field 0.
		// We need two levels of dereference: String -> __ManagedMemory -> buffer.
		// The buffer is null-terminated from LowerStringInterp.
		var stringVarName = ResolveManagedVarName(op.MessageStruct, valueMap);
		// Load field 0 of String = __ManagedMemory pointer
		var managedPtr = (StdI64)EmitStructFieldLoad(block, stringVarName, 0, MlirType.I64, varTypes);
		// Store to temp so we can load fields from it
		var managedTempVar = $"__panic_managed_{MlirContext.Current.NextId()}";
		EmitStore(block, managedPtr, managedTempVar, varTypes);
		// Load field 0 of __ManagedMemory = raw buffer pointer (C string)
		var buffer = (StdI64)EmitStructFieldLoad(block, managedTempVar, ManagedFieldBuffer, MlirType.I64, varTypes);
		block.AddOp(new StdCallRuntimeOp("mrt_panic", [buffer], null));
	}

	/// <summary>
	/// Append another __ManagedMemory buffer's data to self in-place.
	/// Grows the buffer if needed using maxon_string_ensure_cap (which handles COW for capacity=0).
	/// For struct elements, increfs the copied pointers.
	/// </summary>
	private static void LowerManagedMemAppend(
	  MaxonManagedMemAppendOp op,
	  MlirFunction<StandardOp> func,
	  ref MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {

		var selfVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
		var otherVarName = ResolveManagedVarName(op.Other, valueMap);
		var uid = MlirContext.Current.NextId();

		var otherLen = (StdI64)EmitStructFieldLoad(block, otherVarName, ManagedFieldLength, MlirType.I64, varTypes);
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
			var selfLen = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldLength, MlirType.I64, varTypes);
			var selfCap = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
			var otherBuf = LoadManagedBuffer(appendBlock, otherVarName, varTypes);
			var otherLenReload = (StdI64)EmitLoad(appendBlock, otherLenVar, varTypes);

			// Compute new total length
			var totalLen = new StdAddI64Op(selfLen, otherLenReload);
			appendBlock.AddOp(totalLen);
			var totalByteSize = ComputeBitPackedByteSize(appendBlock, totalLen.Result);

			// Ensure capacity (byte-level for bit-packed: use byte sizes)
			var selfByteSize = ComputeBitPackedByteSize(appendBlock, selfLen);
			var selfCapBytes = ComputeBitPackedByteSize(appendBlock, selfCap);

			// growCap = max(totalByteSize+1, selfCapBytes*2, 64)
			var oneConst = new StdConstI64Op(1);
			appendBlock.AddOp(oneConst);
			var requiredCap = new StdAddI64Op(totalByteSize, oneConst.Result);
			appendBlock.AddOp(requiredCap);
			var twoConst = new StdConstI64Op(2);
			appendBlock.AddOp(twoConst);
			var doubled = new StdMulI64Op(selfCapBytes, twoConst.Result);
			appendBlock.AddOp(doubled);
			var cmp1 = new StdCmpU64Op("ugt", requiredCap.Result, doubled.Result);
			appendBlock.AddOp(cmp1);
			var grow1 = new StdSelectI64Op(cmp1.Result, requiredCap.Result, doubled.Result);
			appendBlock.AddOp(grow1);
			var minCap = new StdConstI64Op(64);
			appendBlock.AddOp(minCap);
			var cmp2 = new StdCmpU64Op("ugt", grow1.Result, minCap.Result);
			appendBlock.AddOp(cmp2);
			var growCap = new StdSelectI64Op(cmp2.Result, grow1.Result, minCap.Result);
			appendBlock.AddOp(growCap);

			var newBuf = new StdI64(MlirContext.Current.NextId());
			appendBlock.AddOp(new StdCallRuntimeOp("maxon_string_ensure_cap",
				[selfBuf, selfByteSize, selfCapBytes, growCap.Result], newBuf));

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
			EmitStructFieldStore(block, finalBuf, selfVarName, ManagedFieldBuffer, MlirType.I64, varTypes);
			EmitStructFieldStore(block, totalLen.Result, selfVarName, ManagedFieldLength, MlirType.I64, varTypes);
			// Update capacity: use totalLen if grew (conservative)
			var grewCmp = new StdCmpU64Op("ugt", requiredCap.Result, selfCapBytes);
			block.AddOp(grewCmp);
			var newCap = new StdSelectI64Op(grewCmp.Result, totalLen.Result, selfCap);
			block.AddOp(newCap);
			EmitStructFieldStore(block, newCap.Result, selfVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
			block.AddOp(new StdBrOp(skipLabel));
		} else {
			// Regular element append: use element_size for byte calculations
			var selfBuf = LoadManagedBuffer(appendBlock, selfVarName, varTypes);
			var selfLen = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldLength, MlirType.I64, varTypes);
			var selfCap = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
			var elemSize = (StdI64)EmitStructFieldLoad(appendBlock, selfVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
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
			var selfCapBytes = new StdMulI64Op(selfCap, elemSize);
			appendBlock.AddOp(selfCapBytes);
			var selfCapBytesVar = $"__append_capbytes_{uid}";
			EmitStore(appendBlock, selfCapBytes.Result, selfCapBytesVar, varTypes);
			var totalLenBytes = new StdMulI64Op(totalLen.Result, elemSize);
			appendBlock.AddOp(totalLenBytes);

			// requiredByteCap = totalLenBytes + 1 (extra byte for safety)
			var oneConst = new StdConstI64Op(1);
			appendBlock.AddOp(oneConst);
			var requiredByteCap = new StdAddI64Op(totalLenBytes.Result, oneConst.Result);
			appendBlock.AddOp(requiredByteCap);

			// growByteCap = max(requiredByteCap, selfCapBytes * 2, 64)
			var twoConst = new StdConstI64Op(2);
			appendBlock.AddOp(twoConst);
			var doubled = new StdMulI64Op(selfCapBytes.Result, twoConst.Result);
			appendBlock.AddOp(doubled);
			var cmp1 = new StdCmpU64Op("ugt", requiredByteCap.Result, doubled.Result);
			appendBlock.AddOp(cmp1);
			var grow1 = new StdSelectI64Op(cmp1.Result, requiredByteCap.Result, doubled.Result);
			appendBlock.AddOp(grow1);
			var minCap = new StdConstI64Op(64);
			appendBlock.AddOp(minCap);
			var cmp2 = new StdCmpU64Op("ugt", grow1.Result, minCap.Result);
			appendBlock.AddOp(cmp2);
			var growByteCap = new StdSelectI64Op(cmp2.Result, grow1.Result, minCap.Result);
			appendBlock.AddOp(growByteCap);
			var growByteCapVar = $"__append_growcap_{uid}";
			EmitStore(appendBlock, growByteCap.Result, growByteCapVar, varTypes);
			var reqByteCapVar = $"__append_reqcap_{uid}";
			EmitStore(appendBlock, requiredByteCap.Result, reqByteCapVar, varTypes);

			// Call maxon_string_ensure_cap(buffer, lengthBytes, capacityBytes, growByteCap) -> newBuffer
			var callBuf = (StdI64)EmitLoad(appendBlock, selfBufVar, varTypes);
			var callLen = selfLenBytes.Result;
			var callCap = selfCapBytes.Result;
			var callGrow = (StdI64)EmitLoad(appendBlock, growByteCapVar, varTypes);
			var newBuf = new StdI64(MlirContext.Current.NextId());
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
			EmitStructFieldStore(appendBlock, finalBuf, selfVarName, ManagedFieldBuffer, MlirType.I64, varTypes);
			var finalLen = (StdI64)EmitLoad(appendBlock, totalLenVar, varTypes);
			EmitStructFieldStore(appendBlock, finalLen, selfVarName, ManagedFieldLength, MlirType.I64, varTypes);

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
			EmitStructFieldStore(appendBlock, finalCap.Result, selfVarName, ManagedFieldCapacity, MlirType.I64, varTypes);

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
				var elemPtr = new StdLoadIndirectOp(elemAddr.Result, 0, MlirType.I64);
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
}
