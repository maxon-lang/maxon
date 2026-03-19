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
	/// __managed_memory_get_unchecked(managed, index): load element from heap buffer.
	/// buffer[index] = *(buffer + index * elementSize)
	/// Element size is read from the managed struct's element_size field.
	/// For struct elements, returns the address of the element in the buffer directly
	/// (the struct data is stored inline, not as a pointer).
	/// </summary>
	private static void LowerManagedMemGet(
	  MaxonManagedMemGetOp op,
	  MlirBlock<StandardOp> block,
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

		if (op.IsStructElement) {
			// Struct elements are heap pointers stored in the buffer (8 bytes each).
			// Load the pointer and incref — the caller gets their own reference.
			// The buffer retains its reference; mm_decref_managed_elements handles
			// the buffer's copy when the array is freed.
			var loadOp = new StdLoadIndirectOp(addr, 0, MlirType.I64);
			block.AddOp(loadOp);
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
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
		var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
		var index = (StdI64)valueMap[op.Index];
		EmitBoundsCheck(block, index, length, "__mm_panic_index_oob");
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// COW check before mutation
		EmitCowCheck(block, managedVarName, varTypes, elemSize);

		// Reload buffer/length after COW (COW may change the buffer pointer)
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var lengthAfterCow = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);
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
		EmitCowCheck(block, managedVarName, varTypes, elemSize);
		// Check against capacity after COW (COW updates capacity from 0 to length)
		var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var index = (StdI64)valueMap[op.Index];
		EmitBoundsCheck(block, index, capacity, "__mm_panic_index_oob");
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var addr = ComputeElementAddress(block, buffer, index, elemSize);

		if (op.IsStructElement) {
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
		if (op.ElementSize <= 0)
			throw new InvalidOperationException($"MaxonManagedMemCreateOp has invalid element_size={op.ElementSize} in func {_currentFuncName}");
		var count = (StdI64)valueMap[op.Count];
		// Compute byte size = count * elementSize
		var sizeOp = new StdConstI64Op(op.ElementSize);
		block.AddOp(sizeOp);
		var byteSizeOp = new StdMulI64Op(count, sizeOp.Result);
		block.AddOp(byteSizeOp);
		// Allocate __ManagedMemory struct, then raw buffer
		var tempName = inlineTarget
			?? temps.CreateTemp("managed_create", op.Result.Id, "__ManagedMemory", OwnershipFlags.None);
		var managedPtr = EmitAlloc(block, 32, "__ManagedMemory", scopeName: _currentFuncName);
		EmitStore(block, managedPtr, tempName, varTypes);
		var allocResult = EmitRawAlloc(block, byteSizeOp.Result, label: "ManagedMemory.buf", scopeName: _currentFuncName);
		// Store fields via indirect access on the heap object
		EmitStructFieldStore(block, allocResult, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, count, tempName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, count, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
		EmitStructFieldStore(block, sizeOp.Result, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
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

		EmitCowCheck(block, managedVarName, varTypes, elemSize);

		// Load buffer pointer (now guaranteed to be heap-allocated after COW check)
		var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);

		// Compute new byte size = newCap * elementSize
		var newByteSizeOp = new StdMulI64Op(newCap, elemSize);
		block.AddOp(newByteSizeOp);

		// Raw realloc: buffer has no refcount header (it's a raw HeapAlloc pointer)
		// Pass managedPtr as 3rd arg so mm_raw_realloc can emit trace output
		var growManagedPtr = (StdI64)EmitLoad(block, managedVarName, varTypes);
		var newBufferResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("mm_raw_realloc", [oldBuffer, newByteSizeOp.Result, growManagedPtr], newBufferResult));

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
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, valueMap);
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
		EmitCowCheck(block, managedVarName, varTypes, elemSize);
		// Check after COW (COW updates capacity from 0 to length)
		var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var index = (StdI64)valueMap[op.Index];
		var count = (StdI64)valueMap[op.Count];
		EmitBoundsCheck(block, index, capacity, "__mm_panic_shift_oob");
		var endOp = new StdAddI64Op(index, count);
		block.AddOp(endOp);
		EmitBoundsCheck(block, endOp.Result, capacity, "__mm_panic_shift_oob");
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);

		if (op.ShiftRight) {
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
			// Use memmove-style copy (handles overlapping regions)
			block.AddOp(new StdMemCopyOp(srcAddr.Result, dstAddr.Result, bytesOp.Result));
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
		var byteLimitOp = new StdMulI64Op(length, elemSize);
		block.AddOp(byteLimitOp);
		var index = (StdI64)valueMap[op.Index];
		EmitBoundsCheck(block, index, byteLimitOp.Result, "__mm_panic_byte_oob");
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
	  StdI64 elemSize) {
		var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);

		var uid = MlirContext.Current.NextId();
		var cowLenVar = $"__cow_len_{uid}";
		EmitStore(block, length, cowLenVar, varTypes);

		var managedPtr = (StdI64)EmitLoad(block, managedVarName, varTypes);
		// Compute byteLen = length * elemSize so we can pass it as a single arg
		var byteLenOp = new StdMulI64Op(length, elemSize);
		block.AddOp(byteLenOp);
		var newBuffer = new StdI64(MlirContext.Current.NextId());
		// Args: buffer, capacity, byteLen, managedPtr (4 register args, no stack args)
		block.AddOp(new StdCallRuntimeOp("maxon_cow_check", [oldBuffer, capacity, byteLenOp.Result, managedPtr], newBuffer));

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
		var byteLimitOp = new StdMulI64Op(length, elemSize);
		block.AddOp(byteLimitOp);
		var index = (StdI64)valueMap[op.Index];
		EmitBoundsCheck(block, index, byteLimitOp.Result, "__mm_panic_byte_oob");
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
		// Store panic message as null-terminated C string in symdata (.symtab section)
		var bytes = System.Text.Encoding.UTF8.GetBytes(op.Message + "\n");
		var cstrBytes = new byte[bytes.Length + 1]; // null-terminated
		bytes.CopyTo(cstrBytes, 0);
		result.SymdataEntries.Add((op.SymdataLabel, cstrBytes, 1));
		// LEA to get pointer to the message
		var leaOp = new StdLeaSymdataOp(op.SymdataLabel);
		block.AddOp(leaOp);
		var ptrToI64 = new StdPtrToI64Op(leaOp.Result);
		block.AddOp(ptrToI64);
		block.AddOp(new StdCallRuntimeOp("maxon_panic", [ptrToI64.Result], null));
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
		block.AddOp(new StdCallRuntimeOp("maxon_panic", [buffer], null));
	}
}
