using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static partial class MaxonToStandardConversion {
	// ============================================================================
	// Managed memory lowering helpers
	// ============================================================================

	/// <summary>
	/// Resolve the struct variable name for a managed memory value.
	/// The managed value may be tracked as a struct variable or may need to be loaded.
	/// </summary>
	private static string ResolveManagedVarName(
	  MaxonValue managedValue,
	  Dictionary<int, string> structVarNames) {
		if (structVarNames.TryGetValue(managedValue.Id, out var varName))
			return varName;
		throw new InvalidOperationException($"Managed memory value %{managedValue.Id} not found in struct variable names");
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
	  Dictionary<int, string> structVarNames,
	  Dictionary<int, string> structValueTypes) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var index = (StdI64)valueMap[op.Index];
		var addr = ComputeElementAddress(block, buffer, index, elemSize);

		if (op.IsStructElement) {
			// Struct elements are heap pointers stored in the buffer (8 bytes each).
			// Load the pointer, store in a temp var, and register as a struct variable.
			var loadOp = new StdLoadIndirectOp(addr, 0, MlirType.I64);
			block.AddOp(loadOp);
			var tempName = $"__memget_{MlirContext.Current.NextId()}";
			EmitStore(block, loadOp.Result, tempName, varTypes);
			structVarNames[op.Result.Id] = tempName;
			if (op.StructElementTypeName != null)
				structValueTypes[op.Result.Id] = op.StructElementTypeName;
			valueMap[op.Result] = loadOp.Result;
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
	/// __managed_memory_set_at(managed, index, value): store element into heap buffer.
	/// Element size is read from the managed struct's element_size field.
	/// For struct elements, copies the full struct data inline into the buffer
	/// (not just a pointer) so elements survive stack frame reuse in loops.
	/// </summary>
	private static void LowerManagedMemSet(
	  MaxonManagedMemSetOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
		EmitCowCheck(block, managedVarName, varTypes, elemSize);
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var index = (StdI64)valueMap[op.Index];
		var addr = ComputeElementAddress(block, buffer, index, elemSize);

		if (op.IsStructElement) {
			// Struct elements are heap pointers — store the pointer value (i64) into the buffer
			var srcName = structVarNames[op.Value.Id];
			var srcHeapPtr = EmitLoad(block, srcName, varTypes);
			block.AddOp(new StdStoreIndirectOp(srcHeapPtr, addr, 0, MlirType.I64));
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
	  Dictionary<int, string> structVarNames) {
		var count = (StdI64)valueMap[op.Count];
		// Compute byte size = count * elementSize
		var sizeOp = new StdConstI64Op(op.ElementSize);
		block.AddOp(sizeOp);
		var byteSizeOp = new StdMulI64Op(count, sizeOp.Result);
		block.AddOp(byteSizeOp);
		var allocResult = EmitAlloc(block, byteSizeOp.Result);
		// Create a heap-allocated __ManagedMemory struct (4 fields * 8 bytes = 32 bytes)
		var tempName = $"__managed_create_{op.Result.Id}";
		var managedPtr = EmitAlloc(block, 32);
		EmitStore(block, managedPtr, tempName, varTypes);
		// Store fields via indirect access on the heap object
		EmitStructFieldStore(block, allocResult, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, count, tempName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, count, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
		EmitStructFieldStore(block, sizeOp.Result, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
		structVarNames[op.Result.Id] = tempName;
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
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);

		// Load element_size from the managed struct via heap pointer
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// Load capacity before COW check — needed to determine if old buffer was heap-allocated (tracked)
		StdI64? oldCapacity = null;
		if (_trackAllocs) {
			oldCapacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
			var oldCapVar = $"__grow_oldcap_{MlirContext.Current.NextId()}";
			EmitStore(block, oldCapacity, oldCapVar, varTypes);
			oldCapacity = (StdI64)EmitLoad(block, oldCapVar, varTypes);
		}

		EmitCowCheck(block, managedVarName, varTypes, elemSize);
		var newCap = (StdI64)valueMap[op.NewCapacity];

		// Load buffer pointer (now guaranteed to be heap-allocated after COW check)
		var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);

		// Compute new byte size = newCap * elementSize
		var newByteSizeOp = new StdMulI64Op(newCap, elemSize);
		block.AddOp(newByteSizeOp);

		// Track the old buffer being freed by realloc
		if (_trackAllocs && oldCapacity != null) {
			// DECREF only if old buffer was heap-allocated (capacity > 0 before COW)
			EmitTrackDecrefIfHeap(block, "array grow", 0, oldCapacity);

			var oldBufVar = $"__grow_oldbuf_{MlirContext.Current.NextId()}";
			EmitStore(block, oldBuffer, oldBufVar, varTypes);
			var oldBufReload = (StdI64)EmitLoad(block, oldBufVar, varTypes);
			EmitTrackFree(block, oldBufReload, "array grow");
			oldBuffer = (StdI64)EmitLoad(block, oldBufVar, varTypes);
		}

		// Realloc: grows buffer in-place or allocates new, copies old data, frees old
		var newBufferResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_realloc", [oldBuffer, newByteSizeOp.Result], newBufferResult));

		StdI64 newBufReload;
		if (_trackAllocs) {
			var byteSizeVar = $"__grow_bytesize_{MlirContext.Current.NextId()}";
			EmitStore(block, newByteSizeOp.Result, byteSizeVar, varTypes);
			var newBufVar = $"__grow_newbuf_{MlirContext.Current.NextId()}";
			EmitStore(block, newBufferResult, newBufVar, varTypes);

			var bufReload = (StdI64)EmitLoad(block, newBufVar, varTypes);
			var sizeReload = (StdI64)EmitLoad(block, byteSizeVar, varTypes);
			EmitTrackAlloc(block, bufReload, sizeReload, "array grow");
			EmitTrackIncref(block, "array grow", 1);

			newBufReload = (StdI64)EmitLoad(block, newBufVar, varTypes);
		} else {
			newBufReload = newBufferResult;
		}

		// Update managed struct fields through heap pointer
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
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
		EmitCowCheck(block, managedVarName, varTypes, elemSize);
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var index = (StdI64)valueMap[op.Index];
		var count = (StdI64)valueMap[op.Count];

		if (op.ShiftRight) {
			// Shift right: copy from [index+count-1] down to [index], moving each one position right
			// Effectively: for i in (count-1)..0: buffer[index+i+1] = buffer[index+i]
			// We implement this as a memcopy of count elements starting at index, shifted by +1
			var totalOffsetOp = new StdMulI64Op(index, elemSize);
			block.AddOp(totalOffsetOp);
			var srcAddr = new StdAddI64Op(buffer, totalOffsetOp.Result);
			block.AddOp(srcAddr);
			// Dest is src + elementSize (one position to the right)
			var dstAddr = new StdAddI64Op(srcAddr.Result, elemSize);
			block.AddOp(dstAddr);
			// Byte count
			var bytesOp = new StdMulI64Op(count, elemSize);
			block.AddOp(bytesOp);
			// Use memmove-style copy (handles overlapping regions)
			block.AddOp(new StdMemCopyOp(srcAddr.Result, dstAddr.Result, bytesOp.Result));
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
			block.AddOp(new StdMemCopyOp(srcAddr.Result, dstAddr.Result, bytesOp.Result));
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
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var index = (StdI64)valueMap[op.Index];
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

		var newBuffer = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_cow_check", [oldBuffer, capacity, length, elemSize], newBuffer));

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
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames) {
		var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
		var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
		EmitCowCheck(block, managedVarName, varTypes, elemSize);

		// Now perform the actual byte write using the writable buffer
		var bufReload = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldBuffer, MlirType.I64, varTypes);
		var index = (StdI64)valueMap[op.Index];
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
	  Dictionary<int, string> structVarNames) {
		var cstrPtr = (StdI64)valueMap[op.CstrPtr];

		// Get string length
		var lenResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_strlen", [cstrPtr], lenResult));

		// Allocate buffer
		var allocResult = EmitAlloc(block, lenResult);

		// Copy bytes from cstring to new buffer
		var copyResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_memcpy", [allocResult, cstrPtr, lenResult], copyResult));

		// Heap-allocate __ManagedMemory struct and store fields
		var tempName = $"__from_cstring_{op.Result.Id}";
		var managedPtr = EmitAlloc(block, 32);
		EmitStore(block, managedPtr, tempName, varTypes);
		var elemSizeOp = new StdConstI64Op(1);
		block.AddOp(elemSizeOp);
		EmitStructFieldStore(block, allocResult, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, lenResult, tempName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, lenResult, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
		EmitStructFieldStore(block, elemSizeOp.Result, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
		structVarNames[op.Result.Id] = tempName;
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
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames) {
		var managedVarName = ResolveManagedVarName(op.Managed, structVarNames);
		var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
		var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);

		var result = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_to_cstring", [buffer, length], result));
		valueMap[op.Result] = result;
	}

	/// <summary>
	/// __cstring_write_stdout(cstrPtr): write a null-terminated C string to stdout.
	/// Calls maxon_write_stdout runtime function which uses GetStdHandle + WriteFile.
	/// Returns the number of bytes written.
	/// </summary>
	private static void LowerCStringWriteStdout(
	  MaxonCStringWriteStdoutOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap) {
		var cstrPtr = (StdI64)valueMap[op.CstrPtr];
		var result = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_write_stdout", [cstrPtr], result));
		valueMap[op.Result] = result;
	}

	/// <summary>
	/// __cstring_write_stderr(cstrPtr): write a null-terminated C string to stderr.
	/// Calls maxon_write_stderr runtime function which uses GetStdHandle + WriteFile.
	/// Returns the number of bytes written.
	/// </summary>
	private static void LowerCStringWriteStderr(
	  MaxonCStringWriteStderrOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap) {
		var cstrPtr = (StdI64)valueMap[op.CstrPtr];
		var result = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_write_stderr", [cstrPtr], result));
		valueMap[op.Result] = result;
	}

	private static void LowerPanic(
	  MaxonPanicOp op,
	  MlirBlock<StandardOp> block,
	  MlirModule<StandardOp> result) {
		// Store panic message as null-terminated C string in rdata
		var bytes = System.Text.Encoding.UTF8.GetBytes(op.Message + "\n");
		var cstrBytes = new byte[bytes.Length + 1]; // null-terminated
		bytes.CopyTo(cstrBytes, 0);
		result.RdataEntries.Add((op.RdataLabel, cstrBytes, 1));
		// LEA to get pointer to the message
		var leaOp = new StdLeaRdataOp(op.RdataLabel);
		block.AddOp(leaOp);
		var ptrToI64 = new StdPtrToI64Op(leaOp.Result);
		block.AddOp(ptrToI64);
		block.AddOp(new StdCallRuntimeOp("maxon_panic", [ptrToI64.Result], null));
	}
}
