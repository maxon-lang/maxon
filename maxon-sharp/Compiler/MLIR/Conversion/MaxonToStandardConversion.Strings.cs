using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static partial class MaxonToStandardConversion {
	/// <summary>
	/// Encode a string literal into rdata and emit LEA + PtrToI64 to get a buffer pointer and length.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length) EmitRdataLiteral(
	  string value,
	  string rdataLabel,
	  MlirBlock<StandardOp> block,
	  MlirModule<StandardOp> result,
	  System.Text.Encoding? encoding = null) {
		var utf8Bytes = (encoding ?? System.Text.Encoding.UTF8).GetBytes(value);

		if (_rdataStringCache!.TryGetValue(value, out var existingLabel)) {
			rdataLabel = existingLabel;
		} else {
			var nullTerminated = new byte[utf8Bytes.Length + 1];
			Array.Copy(utf8Bytes, nullTerminated, utf8Bytes.Length);
			result.RdataEntries.Add((rdataLabel, nullTerminated, 1));
			_rdataStringCache[value] = rdataLabel;
		}

		var leaOp = new StdLeaRdataOp(rdataLabel);
		block.AddOp(leaOp);
		var ptrOp = new StdPtrToI64Op(leaOp.Result);
		block.AddOp(ptrOp);

		var lenOp = new StdConstI64Op(utf8Bytes.Length);
		block.AddOp(lenOp);

		return (ptrOp.Result, lenOp.Result);
	}

	/// Allocates a __ManagedMemory struct, stores the 4 managed fields
	/// (buffer, length, capacity=0, elementSize=1), and stores the pointer in the outer struct.
	private static string EmitManagedField(
	  string tempName, string managedName,
	  StdI64 bufferPtr, StdI64 lengthVal, int managedFieldOffset,
	  MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) {
		var managedPtr = EmitAlloc(block, 32, "__ManagedMemory", scopeName: _currentFuncName);
		EmitStore(block, managedPtr, managedName, varTypes);
		EmitStructFieldStore(block, bufferPtr, managedName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, lengthVal, managedName, ManagedFieldLength, MlirType.I64, varTypes);
		var capConst = new StdConstI64Op(0);
		block.AddOp(capConst);
		EmitStructFieldStore(block, capConst.Result, managedName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var elemSizeConst = new StdConstI64Op(1);
		block.AddOp(elemSizeConst);
		EmitStructFieldStore(block, elemSizeConst.Result, managedName, ManagedFieldElementSize, MlirType.I64, varTypes);
		var managedPtrReload = EmitLoad(block, managedName, varTypes);
		EmitStructFieldStore(block, managedPtrReload, tempName, managedFieldOffset, MlirType.I64, varTypes);
		EmitIncref(block, managedName, varTypes, scopeName: _currentFuncName);
		return managedName;
	}

	private static StdHeapPtr EmitManagedMemoryLiteral(
	  string value,
	  int resultId,
	  string rdataPrefix,
	  string tempPrefix,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result,
	  VarRegistry temps,
	  string? allocTag = null,
	  string? inlineTarget = null) {
		var rdataLabel = $"__{rdataPrefix}_{NextRdataId()}";
		var (bufferPtr, lengthVal) = EmitRdataLiteral(value, rdataLabel, block, result);

		var tempName = inlineTarget
			?? temps.CreateTemp(tempPrefix, resultId, allocTag ?? "unknown", OwnershipFlags.None);
		int outerSize = allocTag == "String" ? StringStructSize : CharacterStructSize;
		var outerPtr = (StdHeapPtr)EmitAlloc(block, outerSize, allocTag, scopeName: _currentFuncName);
		EmitStore(block, outerPtr, tempName, varTypes);

		var managedName = $"__{tempPrefix}_managed_{resultId}";
		EmitManagedField(tempName, managedName, bufferPtr, lengthVal, 0, block, varTypes);

		return new StdHeapPtr(outerPtr.Id, outerPtr.TypeName, tempName);
	}

	private static void LowerStringLiteral(
	  MaxonStringLiteralOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result,
	  VarRegistry temps,
	  string? inlineTarget = null) {
		var heapPtr = EmitManagedMemoryLiteral(op.Value, op.Result.Id, "str", "strtmp", block, varTypes, result, temps, "String", inlineTarget);
		valueMap[op.Result] = heapPtr;

		// Store iterPos
		var iterConst = new StdConstI64Op(0);
		block.AddOp(iterConst);
		EmitStructFieldStore(block, iterConst.Result, heapPtr.VarName!, StringFieldIterPos, MlirType.I64, varTypes);

		// Compute isAscii at compile time
		bool isAscii = op.Value.All(c => c < 128);

		// Store graphemeCount = -1 (uncached)
		// Note: even for ASCII strings, we don't pre-populate the count because
		// CRLF sequences are ASCII but count as 1 grapheme (not 2 bytes).
		var graphemeCountConst = new StdConstI64Op(-1);
		block.AddOp(graphemeCountConst);
		EmitStructFieldStore(block, graphemeCountConst.Result, heapPtr.VarName!, StringFieldGraphemeCount, MlirType.I64, varTypes);

		// Store isAscii
		var isAsciiConst = new StdConstI64Op(isAscii ? 1 : 0);
		block.AddOp(isAsciiConst);
		EmitStructFieldStore(block, isAsciiConst.Result, heapPtr.VarName!, StringFieldIsAscii, MlirType.I64, varTypes);
	}

	private static void LowerByteStringLiteral(
	  MaxonByteStringLiteralOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result,
	  VarRegistry temps,
	  string? inlineTarget = null) {
		// ByteArray layout differs from String: iterIndex at offset 0, managed at offset 8
		var rdataLabel = $"__bstr_{NextRdataId()}";
		var (bufferPtr, lengthVal) = EmitRdataLiteral(op.Value, rdataLabel, block, result,
		  System.Text.Encoding.Latin1);

		var tempName = inlineTarget
			?? temps.CreateTemp("bstrtmp", op.Result.Id, op.ArrayTypeName, OwnershipFlags.None);
		var outerPtr = (StdHeapPtr)EmitAlloc(block, 16, op.ArrayTypeName, scopeName: _currentFuncName);
		EmitStore(block, outerPtr, tempName, varTypes);

		// Store iterIndex = 0 at offset 0
		var iterConst = new StdConstI64Op(0);
		block.AddOp(iterConst);
		EmitStructFieldStore(block, iterConst.Result, tempName, 0, MlirType.I64, varTypes);

		var managedName = $"__bstrtmp_managed_{op.Result.Id}";
		EmitManagedField(tempName, managedName, bufferPtr, lengthVal, 8, block, varTypes);

		valueMap[op.Result] = new StdHeapPtr(outerPtr.Id, outerPtr.TypeName, tempName);
	}

	private static void LowerCharLiteral(
	  MaxonCharLiteralOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result,
	  VarRegistry temps,
	  string? inlineTarget = null) {
		var heapPtr = EmitManagedMemoryLiteral(op.Value, op.Result.Id, "chr", "chrtmp", block, varTypes, result, temps, "Character", inlineTarget);
		valueMap[op.Result] = heapPtr;
	}

	private static void LowerStringInterp(
	  MaxonStringInterpOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result,
	  VarRegistry temps,
	  string? inlineTarget = null) {

		var (partInfos, interpTempBufVars) = EmitInterpParts(op.Parts, "interp", block, valueMap, varTypes, result);

		if (partInfos.Count == 0) {
			var heapPtr = EmitManagedMemoryLiteral("", op.Result.Id, "interp", "interptmp", block, varTypes, result, temps, "String", inlineTarget);
			valueMap[op.Result] = heapPtr;
			var iterConst = new StdConstI64Op(0);
			block.AddOp(iterConst);
			EmitStore(block, iterConst.Result, $"{heapPtr.VarName!}._iterPos", varTypes);
			var gcConst = new StdConstI64Op(-1);
			block.AddOp(gcConst);
			EmitStructFieldStore(block, gcConst.Result, heapPtr.VarName!, StringFieldGraphemeCount, MlirType.I64, varTypes);
			var iaConst = new StdConstI64Op(0);
			block.AddOp(iaConst);
			EmitStructFieldStore(block, iaConst.Result, heapPtr.VarName!, StringFieldIsAscii, MlirType.I64, varTypes);
			return;
		}

		// Compute total length
		StdI64 totalLen;
		if (partInfos.Count == 1) {
			totalLen = partInfos[0].Length;
		} else {
			var sum = new StdAddI64Op(partInfos[0].Length, partInfos[1].Length);
			block.AddOp(sum);
			totalLen = sum.Result;
			for (int i = 2; i < partInfos.Count; i++) {
				var add = new StdAddI64Op(totalLen, partInfos[i].Length);
				block.AddOp(add);
				totalLen = add.Result;
			}
		}

		// Allocate outer String struct, then ManagedMemory, then raw buffer
		var tempName2 = inlineTarget
			?? temps.CreateTemp("interptmp", op.Result.Id, "String", OwnershipFlags.None);
		var interpOuterPtr = (StdHeapPtr)EmitAlloc(block, StringStructSize, "String", scopeName: _currentFuncName);
		EmitStore(block, interpOuterPtr, tempName2, varTypes);

		var interpManagedName = $"__interp_managed_{op.Result.Id}";
		var interpManagedPtr = EmitAlloc(block, 32, "__ManagedMemory", scopeName: _currentFuncName);
		EmitStore(block, interpManagedPtr, interpManagedName, varTypes);

		// Allocate buffer (totalLen + 1 for null terminator) as raw heap allocation
		var oneOp = new StdConstI64Op(1);
		block.AddOp(oneOp);
		var allocSize = new StdAddI64Op(totalLen, oneOp.Result);
		block.AddOp(allocSize);

		var allocResult = EmitRawAlloc(block, allocSize.Result, label: "interp.buf", scopeName: _currentFuncName);

		// Store all values to stack variables since rep movsb clobbers RSI, RDI, RCX
		var interpOffsetVar = $"__interp_offset_{op.Result.Id}";
		var interpBufVar = $"__interp_buf_{op.Result.Id}";
		var interpTotalLenVar = $"__interp_totallen_{op.Result.Id}";
		var zeroOp = new StdConstI64Op(0);
		block.AddOp(zeroOp);
		EmitStore(block, zeroOp.Result, interpOffsetVar, varTypes);
		EmitStore(block, allocResult, interpBufVar, varTypes);
		EmitStore(block, totalLen, interpTotalLenVar, varTypes);

		// Store each part's buffer and length to stack variables
		var partBufVars = new string[partInfos.Count];
		var partLenVars = new string[partInfos.Count];
		for (int i = 0; i < partInfos.Count; i++) {
			partBufVars[i] = $"__interp_partbuf_{op.Result.Id}_{i}";
			partLenVars[i] = $"__interp_partlen_{op.Result.Id}_{i}";
			EmitStore(block, partInfos[i].Buffer, partBufVars[i], varTypes);
			EmitStore(block, partInfos[i].Length, partLenVars[i], varTypes);
		}

		for (int i = 0; i < partInfos.Count; i++) {
			var curBuf = (StdI64)EmitLoad(block, interpBufVar, varTypes);
			var curOff = (StdI64)EmitLoad(block, interpOffsetVar, varTypes);
			var dstAddr = new StdAddI64Op(curBuf, curOff);
			block.AddOp(dstAddr);

			var srcBuf = (StdI64)EmitLoad(block, partBufVars[i], varTypes);
			var srcLen = (StdI64)EmitLoad(block, partLenVars[i], varTypes);
			block.AddOp(new StdMemCopyOp(srcBuf, dstAddr.Result, srcLen));

			// Reload offset and length (clobbered by memcopy) and advance
			var curOff2 = (StdI64)EmitLoad(block, interpOffsetVar, varTypes);
			var partLen = (StdI64)EmitLoad(block, partLenVars[i], varTypes);
			var newOffset = new StdAddI64Op(curOff2, partLen);
			block.AddOp(newOffset);
			EmitStore(block, newOffset.Result, interpOffsetVar, varTypes);
		}

		// Write null terminator at buffer[totalLen]
		{
			var ntBuf = (StdI64)EmitLoad(block, interpBufVar, varTypes);
			var ntOff = (StdI64)EmitLoad(block, interpTotalLenVar, varTypes);
			var ntAddr = new StdAddI64Op(ntBuf, ntOff);
			block.AddOp(ntAddr);
			var ntZero = new StdConstI64Op(0);
			block.AddOp(ntZero);
			block.AddOp(new StdStoreIndirectOp(ntZero.Result, ntAddr.Result, 0, MlirType.I8));
		}

		// Free intermediate toString buffers now that contents are copied
		foreach (var bufVar in interpTempBufVars) {
			var bufPtr = (StdI64)EmitLoad(block, bufVar, varTypes);
			if (Compiler.MmTrace) {
				var nullScope = new StdConstI64Op(0);
				block.AddOp(nullScope);
				block.AddOp(new StdCallRuntimeOp("mm_raw_free", [bufPtr, nullScope.Result], null));
			} else {
				block.AddOp(new StdCallRuntimeOp("mm_raw_free", [bufPtr], null));
			}
		}

		// Store ManagedMemory fields
		var finalBuf = (StdI64)EmitLoad(block, interpBufVar, varTypes);
		EmitStructFieldStore(block, finalBuf, interpManagedName, ManagedFieldBuffer, MlirType.I64, varTypes);

		var finalLen = (StdI64)EmitLoad(block, interpTotalLenVar, varTypes);
		EmitStructFieldStore(block, finalLen, interpManagedName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, finalLen, interpManagedName, ManagedFieldCapacity, MlirType.I64, varTypes);

		var elemSizeConst2 = new StdConstI64Op(1);
		block.AddOp(elemSizeConst2);
		EmitStructFieldStore(block, elemSizeConst2.Result, interpManagedName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// Store _managed heap pointer at offset 0 and incref it
		var interpManagedReload = EmitLoad(block, interpManagedName, varTypes);
		EmitStructFieldStore(block, interpManagedReload, tempName2, StringFieldManaged, MlirType.I64, varTypes);
		EmitIncref(block, interpManagedName, varTypes, scopeName: _currentFuncName);

		// Store iterPos
		var iterPosConst = new StdConstI64Op(0);
		block.AddOp(iterPosConst);
		EmitStructFieldStore(block, iterPosConst.Result, tempName2, StringFieldIterPos, MlirType.I64, varTypes);

		// Store graphemeCount = -1 (uncached)
		var graphemeCountConst2 = new StdConstI64Op(-1);
		block.AddOp(graphemeCountConst2);
		EmitStructFieldStore(block, graphemeCountConst2.Result, tempName2, StringFieldGraphemeCount, MlirType.I64, varTypes);

		// Store isAscii = 0 (conservative default)
		var isAsciiConst2 = new StdConstI64Op(0);
		block.AddOp(isAsciiConst2);
		EmitStructFieldStore(block, isAsciiConst2.Result, tempName2, StringFieldIsAscii, MlirType.I64, varTypes);

		valueMap[op.Result] = new StdHeapPtr(interpOuterPtr.Id, interpOuterPtr.TypeName, tempName2);
	}

	/// <summary>
	/// Processes interpolation parts into (buffer, length) pairs and tracks temporary buffers.
	/// Shared by LowerStringInterp and LowerStringAppend.
	/// </summary>
	private static (List<(StdI64 Buffer, StdI64 Length)> partInfos, List<string> tempBufVars) EmitInterpParts(
	  List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, MlirType? OptimalType)> parts,
	  string rdataPrefix,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result) {
		var partInfos = new List<(StdI64 Buffer, StdI64 Length)>();
		var tempBufVars = new List<string>();
		void AddToStringResult((StdI64 Buffer, StdI64 Length, string BufVarName) r) {
			partInfos.Add((r.Buffer, r.Length));
			tempBufVars.Add(r.BufVarName);
		}
		void AddEnumToStringResult((StdI64 Buffer, StdI64 Length, string? BufVarName) r) {
			partInfos.Add((r.Buffer, r.Length));
			if (r.BufVarName != null) tempBufVars.Add(r.BufVarName);
		}

		foreach (var (IsLiteral, LiteralValue, ExprValue, FormatSpec, OptimalType) in parts) {
			if (IsLiteral) {
				if (string.IsNullOrEmpty(LiteralValue)) continue;
				var litId = NextRdataId();
				var rdataLabel = $"__{rdataPrefix}_lit_{litId}";
				partInfos.Add(EmitRdataLiteral(LiteralValue!, rdataLabel, block, result));
			} else {
				var exprValue = ExprValue!;
				if (valueMap.TryGetValue(exprValue, out var exprStdVal) && exprStdVal is StdHeapPtr hp) {
					partInfos.Add(EmitStructInterpolation(hp.VarName!, block, varTypes));
				} else if (exprValue is MaxonInteger or MaxonByte or MaxonShort) {
					var stdVal = valueMap[exprValue];
					// Widen narrower integer types to i64 for the runtime toString call
					if (stdVal is StdU32 u32) {
						stdVal = EnsureI64(new StdI32(u32.Id), block, signExtend: false);
					} else if (stdVal is StdI32) {
						stdVal = EnsureI64(stdVal, block, signExtend: true);
					}
					bool isUnsigned = (OptimalType?.IsUnsigned ?? false) || stdVal is StdU64;
					if (FormatSpec != null) {
						if (isUnsigned) AddToStringResult(EmitU64ToStringFormatted(stdVal, FormatSpec, block, varTypes, result));
						else AddToStringResult(EmitI64ToStringFormatted(stdVal, FormatSpec, block, varTypes, result));
					} else {
						if (isUnsigned) AddToStringResult(EmitU64ToString(stdVal, block, varTypes));
						else AddToStringResult(EmitI64ToString(stdVal, block, varTypes));
					}
				} else if (exprValue is MaxonFloat && valueMap[exprValue] is StdF32 f32ForStr) {
					var promote = new StdF32ToF64Op(f32ForStr);
					block.AddOp(promote);
					if (FormatSpec != null) AddToStringResult(EmitF64ToStringFormatted(promote.Result, FormatSpec, block, varTypes, result));
					else AddToStringResult(EmitF64ToString(promote.Result, block, varTypes));
				} else if (exprValue is MaxonFloat) {
					if (FormatSpec != null) AddToStringResult(EmitF64ToStringFormatted((StdF64)valueMap[exprValue], FormatSpec, block, varTypes, result));
					else AddToStringResult(EmitF64ToString((StdF64)valueMap[exprValue], block, varTypes));
				} else if (exprValue is MaxonBool) {
					AddToStringResult(EmitBoolToString((StdBool)valueMap[exprValue], block, varTypes));
				} else if (exprValue is MaxonEnum enumValue) {
					AddEnumToStringResult(EmitEnumToString(enumValue, valueMap, block, varTypes, result));
				} else {
					throw new InvalidOperationException(
					  $"String {rdataPrefix}: unsupported expression type {exprValue.GetType().Name} for value %{exprValue.Id}");
				}
			}
		}
		return (partInfos, tempBufVars);
	}

	/// <summary>
	/// Unified append lowering for both MaxonStringAppendOp and MaxonStringAppendInterpOp.
	/// Grows the target String's buffer in place and appends new data directly into it.
	/// Handles capacity==0 (rdata/literal) by allocating a fresh writable buffer first.
	/// Uses 2x growth strategy for amortized O(1) append.
	/// </summary>
	private static void LowerStringAppend(
	  string selfVarName,
	  List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, MlirType? OptimalType)> parts,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result) {

		var (partInfos, interpTempBufVars) = EmitInterpParts(parts, "append", block, valueMap, varTypes, result);
		if (partInfos.Count == 0) return;

		// --- Step 2: Compute total append length ---
		StdI64 appendLen;
		if (partInfos.Count == 1) {
			appendLen = partInfos[0].Length;
		} else {
			var sum = new StdAddI64Op(partInfos[0].Length, partInfos[1].Length);
			block.AddOp(sum);
			appendLen = sum.Result;
			for (int i = 2; i < partInfos.Count; i++) {
				var add = new StdAddI64Op(appendLen, partInfos[i].Length);
				block.AddOp(add);
				appendLen = add.Result;
			}
		}

		// Spill append length and part buffers/lengths to stack vars (memcpy clobbers registers)
		var uid = MlirContext.Current.NextId();
		var appendLenVar = $"__append_len_{uid}";
		EmitStore(block, appendLen, appendLenVar, varTypes);
		var partBufVars = new string[partInfos.Count];
		var partLenVars = new string[partInfos.Count];
		for (int i = 0; i < partInfos.Count; i++) {
			partBufVars[i] = $"__append_partbuf_{uid}_{i}";
			partLenVars[i] = $"__append_partlen_{uid}_{i}";
			EmitStore(block, partInfos[i].Buffer, partBufVars[i], varTypes);
			EmitStore(block, partInfos[i].Length, partLenVars[i], varTypes);
		}

		// --- Step 3: Load self's managed struct fields ---
		// selfVarName is the String variable; its _managed field is at offset 0
		var selfManagedTempVar = $"__append_managed_{uid}";
		var selfManagedPtr = (StdI64)EmitStructFieldLoad(block, selfVarName, 0, MlirType.I64, varTypes);
		EmitStore(block, selfManagedPtr, selfManagedTempVar, varTypes);

		var oldBuffer = LoadManagedBuffer(block, selfManagedTempVar, varTypes);
		var oldLength = (StdI64)EmitStructFieldLoad(block, selfManagedTempVar, ManagedFieldLength, MlirType.I64, varTypes);
		var oldCapacity = (StdI64)EmitStructFieldLoad(block, selfManagedTempVar, ManagedFieldCapacity, MlirType.I64, varTypes);

		// Spill these to stack vars (they'll be needed after the ensure_cap call)
		var oldLenVar = $"__append_oldlen_{uid}";
		var oldBufVar = $"__append_oldbuf_{uid}";
		var oldCapVar = $"__append_oldcap_{uid}";
		EmitStore(block, oldLength, oldLenVar, varTypes);
		EmitStore(block, oldBuffer, oldBufVar, varTypes);
		EmitStore(block, oldCapacity, oldCapVar, varTypes);

		// --- Step 4: Compute required capacity with 2x growth ---
		var reloadAppendLen = (StdI64)EmitLoad(block, appendLenVar, varTypes);
		var requiredCapRaw = new StdAddI64Op(oldLength, reloadAppendLen);
		block.AddOp(requiredCapRaw);
		// Add 1 for null terminator
		var oneConst = new StdConstI64Op(1);
		block.AddOp(oneConst);
		var requiredCapPlus1 = new StdAddI64Op(requiredCapRaw.Result, oneConst.Result);
		block.AddOp(requiredCapPlus1);

		// growCap = max(requiredCap+1, capacity * 2, 64)
		// This is only used when ensure_cap actually needs to allocate (capacity < requiredCap+1).
		// ensure_cap returns old buffer when capacity >= requiredCap, so growCap is only
		// relevant for the allocation case.
		var twoConst = new StdConstI64Op(2);
		block.AddOp(twoConst);
		var doubledCap = new StdMulI64Op(oldCapacity, twoConst.Result);
		block.AddOp(doubledCap);
		var cmpDouble = new StdCmpU64Op("ugt", requiredCapPlus1.Result, doubledCap.Result);
		block.AddOp(cmpDouble);
		var growCap1 = new StdSelectI64Op(cmpDouble.Result, requiredCapPlus1.Result, doubledCap.Result);
		block.AddOp(growCap1);
		var minCapConst = new StdConstI64Op(64);
		block.AddOp(minCapConst);
		var cmpMin = new StdCmpU64Op("ugt", growCap1.Result, minCapConst.Result);
		block.AddOp(cmpMin);
		var growCap = new StdSelectI64Op(cmpMin.Result, growCap1.Result, minCapConst.Result);
		block.AddOp(growCap);

		// We always pass growCap to ensure_cap (which may be larger than needed).
		// After the call, we use requiredCap+1 > oldCapacity to determine if growth actually
		// occurred — pointer comparison is unreliable since the slab allocator may reuse addresses.
		var growCapVar = $"__append_growcap_{uid}";
		EmitStore(block, growCap.Result, growCapVar, varTypes);
		var reqCapVar = $"__append_reqcap_{uid}";
		EmitStore(block, requiredCapPlus1.Result, reqCapVar, varTypes);

		// --- Step 5: Call maxon_string_ensure_cap(buffer, length, capacity, growCap) ---
		var callBuf = (StdI64)EmitLoad(block, oldBufVar, varTypes);
		var callLen = (StdI64)EmitLoad(block, oldLenVar, varTypes);
		var callCap = (StdI64)EmitLoad(block, oldCapVar, varTypes);
		var callGrow = (StdI64)EmitLoad(block, growCapVar, varTypes);
		var newBuffer = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_string_ensure_cap",
		  [callBuf, callLen, callCap, callGrow], newBuffer));

		// Spill new buffer to stack
		var newBufVar = $"__append_buf_{uid}";
		EmitStore(block, newBuffer, newBufVar, varTypes);

		// Update managed struct: buffer always gets the return value (same or new ptr).
		// Capacity: use growCap if growth was needed, otherwise keep oldCapacity.
		// Re-derive the needsGrow flag from spilled values (can't rely on pointer comparison
		// since the slab allocator may reuse freed addresses).
		var reloadReqCap = (StdI64)EmitLoad(block, reqCapVar, varTypes);
		var reloadOldCap2 = (StdI64)EmitLoad(block, oldCapVar, varTypes);
		var reloadGrowCap = (StdI64)EmitLoad(block, growCapVar, varTypes);
		var needsGrow = new StdCmpU64Op("ugt", reloadReqCap, reloadOldCap2);
		block.AddOp(needsGrow);
		var actualCap = new StdSelectI64Op(needsGrow.Result, reloadGrowCap, reloadOldCap2);
		block.AddOp(actualCap);
		EmitStructFieldStore(block, newBuffer, selfManagedTempVar, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, actualCap.Result, selfManagedTempVar, ManagedFieldCapacity, MlirType.I64, varTypes);

		// --- Step 6: Append parts at buffer + oldLength ---
		var offsetVar = $"__append_offset_{uid}";
		var reloadOldLen2 = (StdI64)EmitLoad(block, oldLenVar, varTypes);
		EmitStore(block, reloadOldLen2, offsetVar, varTypes);

		for (int i = 0; i < partInfos.Count; i++) {
			var curBuf = (StdI64)EmitLoad(block, newBufVar, varTypes);
			var curOff = (StdI64)EmitLoad(block, offsetVar, varTypes);
			var dstAddr = new StdAddI64Op(curBuf, curOff);
			block.AddOp(dstAddr);

			var srcBuf = (StdI64)EmitLoad(block, partBufVars[i], varTypes);
			var srcLen = (StdI64)EmitLoad(block, partLenVars[i], varTypes);
			block.AddOp(new StdMemCopyOp(srcBuf, dstAddr.Result, srcLen));

			// Advance offset
			var curOff2 = (StdI64)EmitLoad(block, offsetVar, varTypes);
			var partLen = (StdI64)EmitLoad(block, partLenVars[i], varTypes);
			var newOffset = new StdAddI64Op(curOff2, partLen);
			block.AddOp(newOffset);
			EmitStore(block, newOffset.Result, offsetVar, varTypes);
		}

		// --- Step 7: Write null terminator at buffer[newLength] ---
		{
			var ntBuf = (StdI64)EmitLoad(block, newBufVar, varTypes);
			var ntOff = (StdI64)EmitLoad(block, offsetVar, varTypes);
			var ntAddr = new StdAddI64Op(ntBuf, ntOff);
			block.AddOp(ntAddr);
			var ntZero = new StdConstI64Op(0);
			block.AddOp(ntZero);
			block.AddOp(new StdStoreIndirectOp(ntZero.Result, ntAddr.Result, 0, MlirType.I8));
		}

		// --- Step 8: Update managed length ---
		var finalLen = (StdI64)EmitLoad(block, offsetVar, varTypes);
		EmitStructFieldStore(block, finalLen, selfManagedTempVar, ManagedFieldLength, MlirType.I64, varTypes);

		// --- Step 8b: Invalidate cached graphemeCount on the String struct ---
		// After appending, the grapheme count is no longer valid.
		var negOneConst = new StdConstI64Op(-1);
		block.AddOp(negOneConst);
		EmitStructFieldStore(block, negOneConst.Result, selfVarName, StringFieldGraphemeCount, MlirType.I64, varTypes);

		// --- Step 9: Free intermediate toString buffers ---
		foreach (var bufVar in interpTempBufVars) {
			var bufPtr = (StdI64)EmitLoad(block, bufVar, varTypes);
			if (Compiler.MmTrace) {
				var nullScope = new StdConstI64Op(0);
				block.AddOp(nullScope);
				block.AddOp(new StdCallRuntimeOp("mm_raw_free", [bufPtr, nullScope.Result], null));
			} else {
				block.AddOp(new StdCallRuntimeOp("mm_raw_free", [bufPtr], null));
			}
		}
	}

	/// <summary>
	/// Lowering entry point for MaxonStringAppendOp (append another String's bytes).
	/// Delegates to the unified LowerStringAppend with a single struct-interpolation part.
	/// </summary>
	private static void LowerStringAppendOp(
	  MaxonStringAppendOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result) {
		// Create a single-part list: the other String's buffer/length
		var parts = new List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, MlirType? OptimalType)> {
			(false, null, op.Other, null, null)
		};
		LowerStringAppend(op.SelfVarName, parts, block, valueMap, varTypes, result);
	}

	/// <summary>
	/// Lowering entry point for MaxonStringAppendInterpOp (append interpolated parts directly).
	/// </summary>
	private static void LowerStringAppendInterpOp(
	  MaxonStringAppendInterpOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result) {
		LowerStringAppend(op.SelfVarName, op.Parts, block, valueMap, varTypes, result);
	}

	/// <summary>
	/// Allocates a buffer, calls a runtime conversion function, and returns (buffer, length).
	/// Used by EmitI64ToString, EmitF64ToString, and EmitBoolToString.
	/// Also returns the buffer variable name for cleanup after use.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitRuntimeToString(
	  StdValue value,
	  string runtimeFuncName,
	  int bufferSize,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes) {

		var sizeOp = new StdConstI64Op(bufferSize);
		block.AddOp(sizeOp);
		var bufResult = EmitRawAlloc(block, sizeOp.Result, label: "toStr.buf", scopeName: _currentFuncName);

		// Store buffer pointer so it survives the runtime call
		var bufVarName = $"__tostr_buf_{bufResult.Id}";
		EmitStore(block, bufResult, bufVarName, varTypes);

		var lenResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp(runtimeFuncName, [value, bufResult], lenResult));

		var finalBuf = (StdI64)EmitLoad(block, bufVarName, varTypes);
		return (finalBuf, lenResult, bufVarName);
	}

	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitI64ToString(
	  StdValue intValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
	  EmitRuntimeToString(intValue, "maxon_i64_to_string", 21, block, varTypes);

	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitU64ToString(
	  StdValue intValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
	  EmitRuntimeToString(intValue, "maxon_u64_to_string", 21, block, varTypes);

	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitF64ToString(
	  StdF64 floatValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
	  EmitRuntimeToString(floatValue, "maxon_f64_to_string", 32, block, varTypes);

	/// <summary>
	/// Allocates a buffer, emits the format spec as rdata, calls a formatted runtime conversion function,
	/// and returns (buffer, length). Used for format-specifier string interpolation on built-in types.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitRuntimeToStringFormatted(
	  StdValue value,
	  string runtimeFuncName,
	  int bufferSize,
	  string formatSpec,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result) {

		var fmtSizeOp = new StdConstI64Op(bufferSize);
		block.AddOp(fmtSizeOp);
		var bufResult = EmitRawAlloc(block, fmtSizeOp.Result, label: "fmt.buf", scopeName: _currentFuncName);

		// Store buffer pointer so it survives the runtime call
		var bufVarName = $"__tostr_buf_{bufResult.Id}";
		EmitStore(block, bufResult, bufVarName, varTypes);

		// Emit format spec as rdata literal
		var fmtId = NextRdataId();
		var fmtLabel = $"__fmt_spec_{fmtId}";
		var fmtUtf8 = System.Text.Encoding.UTF8.GetBytes(formatSpec);
		var fmtNull = new byte[fmtUtf8.Length + 1];
		Array.Copy(fmtUtf8, fmtNull, fmtUtf8.Length);
		result.RdataEntries.Add((fmtLabel, fmtNull, 1));

		var fmtLea = new StdLeaRdataOp(fmtLabel);
		block.AddOp(fmtLea);
		var fmtPtr = new StdPtrToI64Op(fmtLea.Result);
		block.AddOp(fmtPtr);
		var fmtLen = new StdConstI64Op(fmtUtf8.Length);
		block.AddOp(fmtLen);

		var lenResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp(runtimeFuncName, [value, bufResult, fmtPtr.Result, fmtLen.Result], lenResult));

		var finalBuf = (StdI64)EmitLoad(block, bufVarName, varTypes);
		return (finalBuf, lenResult, bufVarName);
	}

	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitI64ToStringFormatted(
	  StdValue intValue, string formatSpec, MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes, MlirModule<StandardOp> result) =>
	  EmitRuntimeToStringFormatted(intValue, "maxon_i64_to_string_fmt", 72, formatSpec, block, varTypes, result);

	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitU64ToStringFormatted(
	  StdValue intValue, string formatSpec, MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes, MlirModule<StandardOp> result) =>
	  EmitRuntimeToStringFormatted(intValue, "maxon_u64_to_string_fmt", 72, formatSpec, block, varTypes, result);

	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitF64ToStringFormatted(
	  StdValue floatValue, string formatSpec, MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes, MlirModule<StandardOp> result) =>
	  EmitRuntimeToStringFormatted(floatValue, "maxon_f64_to_string_fmt", 72, formatSpec, block, varTypes, result);

	/// <summary>
	/// Handles interpolation of struct values. For String/Character types (which have buffer/length
	/// fields), reads those directly. For Stringable types, calls the toString() method and uses
	/// the returned String's buffer/length.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length) EmitStructInterpolation(
	  string managedVarName,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes) {

		// With heap refs, the String struct has _managed at offset 0.
		// Load the _managed heap pointer, then load buffer and length from it.
		// For types that are __ManagedMemory directly, managedVarName IS the managed struct.
		// Try loading _managed field first (outer struct), then fall back to direct (bare __ManagedMemory).
		var managedPtr = (StdI64)EmitStructFieldLoad(block, managedVarName, 0, MlirType.I64, varTypes);
		// Save the managed pointer so we can use it for both loads
		var managedTempVar = $"__interp_managed_ptr_{MlirContext.Current.NextId()}";
		EmitStore(block, managedPtr, managedTempVar, varTypes);
		var bufLoad = (StdI64)EmitStructFieldLoad(block, managedTempVar, ManagedFieldBuffer, MlirType.I64, varTypes);
		var lenLoad = (StdI64)EmitStructFieldLoad(block, managedTempVar, ManagedFieldLength, MlirType.I64, varTypes);
		return (bufLoad, lenLoad);
	}

	/// <summary>
	/// Converts an enum value to its string representation for interpolation.
	/// Simple and int-backed enums emit the case name (e.g., "lessThan").
	/// Float-backed enums emit the raw float value.
	/// String-backed enums emit the raw string value.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length, string? BufVarName) EmitEnumToString(
	  MaxonEnum enumValue,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result) {

		if (!result.TypeDefs.TryGetValue(enumValue.TypeName, out var typeDef) || typeDef is not MlirEnumType enumType) {
			throw new InvalidOperationException(
			  $"String interpolation: enum type '{enumValue.TypeName}' not found in type definitions");
		}

		var backingMlirType = ResolveEnumBackingMlirType(enumType);
		var stdValue = valueMap[enumValue];

		if (enumType.BackingType is MlirStringBackingType or MlirCharBackingType) {
			var r = EmitStringEnumToString(enumType, (StdI64)stdValue, block, result);
			return (r.Buffer, r.Length, null);
		}

		if (enumType.BackingType is MlirStructBackingType) {
			// Struct-backed enums interpolate as their case name
			var r = EmitEnumCaseNameToString(enumType, (StdI64)stdValue, block, result);
			return (r.Buffer, r.Length, null);
		}

		if (backingMlirType == MlirType.F64) {
			return EmitF64ToString((StdF64)stdValue, block, varTypes);
		}

		// Enums with explicit backing values interpolate as their raw value;
		// auto-incremented enums interpolate as their case name.
		if (enumType.HasExplicitBackingValues && !enumType.HasAssociatedValues) {
			return EmitI64ToString((StdI64)stdValue, block, varTypes);
		}
		var r2 = EmitEnumCaseNameToString(enumType, (StdI64)stdValue, block, result);
		return (r2.Buffer, r2.Length, null);
	}

	/// <summary>
	/// Emits code to convert an enum ordinal to its case name string.
	/// Generates a chain of select operations mapping each ordinal to its case name.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length) EmitEnumCaseNameToString(
	  MlirEnumType enumType,
	  StdI64 ordinalValue,
	  MlirBlock<StandardOp> block,
	  MlirModule<StandardOp> result) {

		var fallbackLabel = $"__enum_name_fallback_{NextRdataId()}";
		var (currentBuf, currentLen) = EmitRdataLiteral("?", fallbackLabel, block, result);

		foreach (var enumCase in enumType.Cases) {
			var caseLabel = $"__enum_name_{enumType.Name}_{enumCase.Name}_{NextRdataId()}";
			var (caseBuf, caseLen) = EmitRdataLiteral(enumCase.Name, caseLabel, block, result);

			// Int-backed enums use raw values at runtime; simple enums use ordinals
			long runtimeValue = enumCase.RawValue is long rawLong ? rawLong : enumCase.Ordinal;
			var caseConst = new StdConstI64Op(runtimeValue);
			block.AddOp(caseConst);
			var cmpOp = new StdCmpI64Op("eq", ordinalValue, caseConst.Result);
			block.AddOp(cmpOp);

			var selectBuf = new StdSelectI64Op(cmpOp.Result, caseBuf, currentBuf);
			block.AddOp(selectBuf);
			var selectLen = new StdSelectI64Op(cmpOp.Result, caseLen, currentLen);
			block.AddOp(selectLen);

			currentBuf = selectBuf.Result;
			currentLen = selectLen.Result;
		}

		return (currentBuf, currentLen);
	}

	/// <summary>
	/// Emits code to convert a string-backed enum ordinal to its string representation.
	/// Generates a chain of select operations: for each case, compares ordinal and selects
	/// the matching string. Falls back to "?" for unknown ordinals.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length) EmitStringEnumToString(
	  MlirEnumType enumType,
	  StdI64 ordinalValue,
	  MlirBlock<StandardOp> block,
	  MlirModule<StandardOp> result) {

		// Initialize with a fallback "?" value
		var fallbackLabel = $"__strenum_fallback_{NextRdataId()}";
		var (currentBuf, currentLen) = EmitRdataLiteral("?", fallbackLabel, block, result);

		// For each case, compare ordinal and conditionally select the case's string
		foreach (var enumCase in enumType.Cases) {
			if (enumCase.RawValue is not string strValue) continue;

			var caseLabel = $"__strenum_case_{enumType.Name}_{enumCase.Name}_{NextRdataId()}";
			var (caseBuf, caseLen) = EmitRdataLiteral(strValue, caseLabel, block, result);

			var ordConst = new StdConstI64Op(enumCase.Ordinal);
			block.AddOp(ordConst);
			var cmpOp = new StdCmpI64Op("eq", ordinalValue, ordConst.Result);
			block.AddOp(cmpOp);

			// Select: if ordinal matches this case, use caseBuf/caseLen; otherwise keep current
			var selectBuf = new StdSelectI64Op(cmpOp.Result, caseBuf, currentBuf);
			block.AddOp(selectBuf);
			var selectLen = new StdSelectI64Op(cmpOp.Result, caseLen, currentLen);
			block.AddOp(selectLen);

			currentBuf = selectBuf.Result;
			currentLen = selectLen.Result;
		}

		return (currentBuf, currentLen);
	}

	/// Compares two strings (inputBuf/inputLen vs caseBuf/caseLen) using length check + memcmp.
	/// Returns a boolean StdBool that is true if the strings are equal.
	private static StdBool EmitStringEquals(
	  StdI64 inputBuf, StdI64 inputLen, StdI64 caseBuf, StdI64 caseLen,
	  MlirBlock<StandardOp> block) {
		var lenCmp = new StdCmpI64Op("eq", inputLen, caseLen);
		block.AddOp(lenCmp);
		var memcmpResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp("maxon_memcmp", [inputBuf, caseBuf, caseLen], memcmpResult));
		var oneConst = new StdConstI64Op(1);
		block.AddOp(oneConst);
		var memEq = new StdCmpI64Op("eq", memcmpResult, oneConst.Result);
		block.AddOp(memEq);
		var bothMatch = new StdAndI1Op((StdBool)lenCmp.Result, (StdBool)memEq.Result);
		block.AddOp(bothMatch);
		return bothMatch.Result;
	}

	/// Builds a managed String or Character struct from a (buffer, length) pair.
	/// Heap-allocates outer struct, then __ManagedMemory, and links them via field store.
	/// Returns a StdHeapPtr with the variable name set.
	private static StdHeapPtr EmitManagedStructFromBufLen(
	  string tempName, StdI64 bufferPtr, StdI64 lengthVal,
	  bool hasIterPos, MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  string? allocTag = null) {
		int outerSize = hasIterPos ? StringStructSize : CharacterStructSize;
		var outerPtr = (StdHeapPtr)EmitAlloc(block, outerSize, allocTag, scopeName: _currentFuncName);
		EmitStore(block, outerPtr, tempName, varTypes);

		var managedName = $"{tempName}__managed";
		EmitManagedField(tempName, managedName, bufferPtr, lengthVal, 0, block, varTypes);

		if (hasIterPos) {
			var iterConst = new StdConstI64Op(0);
			block.AddOp(iterConst);
			EmitStructFieldStore(block, iterConst.Result, tempName, StringFieldIterPos, MlirType.I64, varTypes);

			var graphemeCountConst = new StdConstI64Op(-1);
			block.AddOp(graphemeCountConst);
			EmitStructFieldStore(block, graphemeCountConst.Result, tempName, StringFieldGraphemeCount, MlirType.I64, varTypes);

			var isAsciiConst = new StdConstI64Op(0);
			block.AddOp(isAsciiConst);
			EmitStructFieldStore(block, isAsciiConst.Result, tempName, StringFieldIsAscii, MlirType.I64, varTypes);
		}

		return new StdHeapPtr(outerPtr.Id, outerPtr.TypeName, tempName);
	}

	/// Converts an int-backed enum raw value to its ordinal via a select chain.
	private static StdI64 EmitIntEnumToOrdinal(
	  MlirEnumType enumType, StdI64 rawValue, MlirBlock<StandardOp> block) {
		var fallbackOrd = new StdConstI64Op(0);
		block.AddOp(fallbackOrd);
		StdI64 currentOrd = fallbackOrd.Result;

		foreach (var enumCase in enumType.Cases) {
			var caseRawConst = new StdConstI64Op((long)enumCase.RawValue!);
			block.AddOp(caseRawConst);
			var cmpOp = new StdCmpI64Op("eq", rawValue, caseRawConst.Result);
			block.AddOp(cmpOp);
			var ordConst = new StdConstI64Op(enumCase.Ordinal);
			block.AddOp(ordConst);
			var selectOp = new StdSelectI64Op(cmpOp.Result, ordConst.Result, currentOrd);
			block.AddOp(selectOp);
			currentOrd = selectOp.Result;
		}
		return currentOrd;
	}

	/// Converts a float-backed enum raw value to its ordinal via a select chain.
	private static StdI64 EmitFloatEnumToOrdinal(
	  MlirEnumType enumType, StdF64 rawValue, MlirBlock<StandardOp> block) {
		var fallbackOrd = new StdConstI64Op(0);
		block.AddOp(fallbackOrd);
		StdI64 currentOrd = fallbackOrd.Result;

		foreach (var enumCase in enumType.Cases) {
			var caseRawConst = new StdConstF64Op((double)enumCase.RawValue!);
			block.AddOp(caseRawConst);
			var cmpOp = new StdCmpF64Op("eq", rawValue, caseRawConst.Result);
			block.AddOp(cmpOp);
			var ordConst = new StdConstI64Op(enumCase.Ordinal);
			block.AddOp(ordConst);
			var selectOp = new StdSelectI64Op(cmpOp.Result, ordConst.Result, currentOrd);
			block.AddOp(selectOp);
			currentOrd = selectOp.Result;
		}
		return currentOrd;
	}

	/// Converts an int-backed enum raw value to its zero-based declaration position via a select chain.
	/// Unlike EmitIntEnumToOrdinal (which returns MlirEnumCase.Ordinal, used for internal name/rawValue lookup),
	/// this returns the case's index in the Cases list — the true declaration position.
	private static StdI64 EmitIntEnumToPositionIndex(
	  MlirEnumType enumType, StdI64 rawValue, MlirBlock<StandardOp> block) {
		var fallbackOrd = new StdConstI64Op(0);
		block.AddOp(fallbackOrd);
		StdI64 currentOrd = fallbackOrd.Result;

		for (int i = 0; i < enumType.Cases.Count; i++) {
			var enumCase = enumType.Cases[i];
			var caseRawConst = new StdConstI64Op((long)enumCase.RawValue!);
			block.AddOp(caseRawConst);
			var cmpOp = new StdCmpI64Op("eq", rawValue, caseRawConst.Result);
			block.AddOp(cmpOp);
			var posConst = new StdConstI64Op(i);
			block.AddOp(posConst);
			var selectOp = new StdSelectI64Op(cmpOp.Result, posConst.Result, currentOrd);
			block.AddOp(selectOp);
			currentOrd = selectOp.Result;
		}
		return currentOrd;
	}

	/// Converts a float-backed enum raw value to its zero-based declaration position via a select chain.
	private static StdI64 EmitFloatEnumToPositionIndex(
	  MlirEnumType enumType, StdF64 rawValue, MlirBlock<StandardOp> block) {
		var fallbackOrd = new StdConstI64Op(0);
		block.AddOp(fallbackOrd);
		StdI64 currentOrd = fallbackOrd.Result;

		for (int i = 0; i < enumType.Cases.Count; i++) {
			var enumCase = enumType.Cases[i];
			var caseRawConst = new StdConstF64Op((double)enumCase.RawValue!);
			block.AddOp(caseRawConst);
			var cmpOp = new StdCmpF64Op("eq", rawValue, caseRawConst.Result);
			block.AddOp(cmpOp);
			var posConst = new StdConstI64Op(i);
			block.AddOp(posConst);
			var selectOp = new StdSelectI64Op(cmpOp.Result, posConst.Result, currentOrd);
			block.AddOp(selectOp);
			currentOrd = selectOp.Result;
		}
		return currentOrd;
	}

	/// Looks up an enum case name by ordinal via a select chain. Returns (buffer, length).
	private static (StdI64 Buffer, StdI64 Length) EmitEnumNameLookup(
	  MlirEnumType enumType, StdI64 ordinalValue,
	  MlirBlock<StandardOp> block, MlirModule<StandardOp> result) {
		var fallbackLabel = $"__enumname_fallback_{NextRdataId()}";
		var (currentBuf, currentLen) = EmitRdataLiteral("?", fallbackLabel, block, result);

		foreach (var enumCase in enumType.Cases) {
			var caseLabel = $"__enumname_{enumType.Name}_{enumCase.Name}_{NextRdataId()}";
			var (caseBuf, caseLen) = EmitRdataLiteral(enumCase.Name, caseLabel, block, result);

			var ordConst = new StdConstI64Op(enumCase.Ordinal);
			block.AddOp(ordConst);
			var cmpOp = new StdCmpI64Op("eq", ordinalValue, ordConst.Result);
			block.AddOp(cmpOp);

			var selectBuf = new StdSelectI64Op(cmpOp.Result, caseBuf, currentBuf);
			block.AddOp(selectBuf);
			var selectLen = new StdSelectI64Op(cmpOp.Result, caseLen, currentLen);
			block.AddOp(selectLen);

			currentBuf = selectBuf.Result;
			currentLen = selectLen.Result;
		}
		return (currentBuf, currentLen);
	}

	/// <summary>
	/// Allocates a 6-byte buffer and calls maxon_bool_to_string runtime to convert
	/// a boolean value to "true" or "false". Returns (buffer, length).
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitBoolToString(
	  StdBool boolValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
	  EmitRuntimeToString(boolValue, "maxon_bool_to_string", 6, block, varTypes);

	private static void LowerManagedMemConcat(
	  MaxonManagedMemConcatOp op,
	  MlirFunction<StandardOp> func,
	  ref MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps,
	  string? inlineTarget = null) {

		var lhsVarName = ResolveManagedVarName(op.Lhs, valueMap);
		var rhsVarName = ResolveManagedVarName(op.Rhs, valueMap);

		var lhsBuf = LoadManagedBuffer(block, lhsVarName, varTypes);
		var lhsLen = (StdI64)EmitStructFieldLoad(block, lhsVarName, ManagedFieldLength, MlirType.I64, varTypes);
		var rhsBuf = LoadManagedBuffer(block, rhsVarName, varTypes);
		var rhsLen = (StdI64)EmitStructFieldLoad(block, rhsVarName, ManagedFieldLength, MlirType.I64, varTypes);

		if (op.IsBitPacked) {
			// Bit-packed bool concat
			var totalLenOp = new StdAddI64Op(lhsLen, rhsLen);
			block.AddOp(totalLenOp);
			var totalByteSize = ComputeBitPackedByteSize(block, totalLenOp.Result);
			var lhsByteSize = ComputeBitPackedByteSize(block, lhsLen);

			// Heap-allocate __ManagedMemory, then buffer
			var managedTypeName = op.Result.TypeName;
			var tempName = inlineTarget
				?? temps.CreateTemp("concat", op.Result.Id, managedTypeName, OwnershipFlags.None);
			var concatPtr = (StdHeapPtr)EmitAlloc(block, 32, managedTypeName, scopeName: _currentFuncName);
			EmitStore(block, concatPtr, tempName, varTypes);

			var allocResult = EmitRawAlloc(block, totalByteSize, label: "concat.buf", scopeName: _currentFuncName);

			// memcpy lhs bytes (includes partial last byte)
			block.AddOp(new StdMemCopyOp(lhsBuf, allocResult, lhsByteSize));

			// Spill values needed in the loop
			var loopUid = MlirContext.Current.NextId();
			var loopVar = $"__concat_i_{loopUid}";
			var zeroInit = new StdConstI64Op(0);
			block.AddOp(zeroInit);
			EmitStore(block, zeroInit.Result, loopVar, varTypes);

			var dstBufVar = $"__concat_dst_{loopUid}";
			EmitStore(block, allocResult, dstBufVar, varTypes);
			var rhsBufVar = $"__concat_rhsbuf_{loopUid}";
			EmitStore(block, rhsBuf, rhsBufVar, varTypes);
			var rhsLenVar = $"__concat_rhslen_{loopUid}";
			EmitStore(block, rhsLen, rhsLenVar, varTypes);
			var lhsLenVar = $"__concat_lhslen_{loopUid}";
			EmitStore(block, lhsLen, lhsLenVar, varTypes);

			var loopHeaderLabel = $"__concat_hdr_{loopUid}";
			var loopBodyLabel = $"__concat_body_{loopUid}";
			var loopExitLabel = $"__concat_exit_{loopUid}";
			block.AddOp(new StdBrOp(loopHeaderLabel));

			// Loop header: while i < rhsLen
			var headerBlock = func.Body.AddBlock(loopHeaderLabel);
			var iReload = (StdI64)EmitLoad(headerBlock, loopVar, varTypes);
			var rhsLenReload = (StdI64)EmitLoad(headerBlock, rhsLenVar, varTypes);
			var cmpLoop = new StdCmpI64Op("lt", iReload, rhsLenReload);
			headerBlock.AddOp(cmpLoop);
			headerBlock.AddOp(new StdCondBrOp(cmpLoop.Result, loopBodyLabel, loopExitLabel));

			// Loop body: get bit i from rhs, set bit (lhsLen + i) in dest
			var bodyBlock = func.Body.AddBlock(loopBodyLabel);
			var iBody = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
			var rhsBufBody = (StdI64)EmitLoad(bodyBlock, rhsBufVar, varTypes);
			var bitVal = EmitBitGet(bodyBlock, rhsBufBody, iBody);
			var dstBufBody = (StdI64)EmitLoad(bodyBlock, dstBufVar, varTypes);
			var lhsLenBody = (StdI64)EmitLoad(bodyBlock, lhsLenVar, varTypes);
			var iBody2 = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
			var dstIdx = new StdAddI64Op(lhsLenBody, iBody2);
			bodyBlock.AddOp(dstIdx);
			EmitBitSet(bodyBlock, dstBufBody, dstIdx.Result, bitVal);
			// Increment loop counter
			var iBody3 = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
			var oneInc = new StdConstI64Op(1);
			bodyBlock.AddOp(oneInc);
			var newI = new StdAddI64Op(iBody3, oneInc.Result);
			bodyBlock.AddOp(newI);
			EmitStore(bodyBlock, newI.Result, loopVar, varTypes);
			bodyBlock.AddOp(new StdBrOp(loopHeaderLabel));

			block = func.Body.AddBlock(loopExitLabel);
			var dstBufFinal = (StdI64)EmitLoad(block, dstBufVar, varTypes);
			var totalLenFinal = new StdAddI64Op(
				(StdI64)EmitLoad(block, lhsLenVar, varTypes),
				(StdI64)EmitLoad(block, rhsLenVar, varTypes));
			block.AddOp(totalLenFinal);
			var zeroElemSize = new StdConstI64Op(0);
			block.AddOp(zeroElemSize);

			EmitStructFieldStore(block, dstBufFinal, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
			EmitStructFieldStore(block, totalLenFinal.Result, tempName, ManagedFieldLength, MlirType.I64, varTypes);
			EmitStructFieldStore(block, totalLenFinal.Result, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
			EmitStructFieldStore(block, zeroElemSize.Result, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);

			valueMap[op.Result] = new StdHeapPtr(concatPtr.Id, concatPtr.TypeName, tempName);
		} else {
			// element_size needed to convert element counts to byte counts
			var lhsElemSize = (StdI64)EmitStructFieldLoad(block, lhsVarName, ManagedFieldElementSize, MlirType.I64, varTypes);

			// Compute byte sizes: elementCount * elementSize
			var lhsBytesOp = new StdMulI64Op(lhsLen, lhsElemSize);
			block.AddOp(lhsBytesOp);
			var rhsBytesOp = new StdMulI64Op(rhsLen, lhsElemSize);
			block.AddOp(rhsBytesOp);

			var totalBytesOp = new StdAddI64Op(lhsBytesOp.Result, rhsBytesOp.Result);
			block.AddOp(totalBytesOp);

			var oneOp = new StdConstI64Op(1);
			block.AddOp(oneOp);
			var allocSizeOp = new StdAddI64Op(totalBytesOp.Result, oneOp.Result);
			block.AddOp(allocSizeOp);

			// Heap-allocate __ManagedMemory, then buffer as raw allocation
			var managedTypeName = op.Result.TypeName;
			var tempName = inlineTarget
				?? temps.CreateTemp("concat", op.Result.Id, managedTypeName, OwnershipFlags.None);
			var concatPtr = (StdHeapPtr)EmitAlloc(block, 32, managedTypeName, scopeName: _currentFuncName);
			EmitStore(block, concatPtr, tempName, varTypes);

			var allocResult = EmitRawAlloc(block, allocSizeOp.Result, label: "concat.buf", scopeName: _currentFuncName);

			block.AddOp(new StdMemCopyOp(lhsBuf, allocResult, lhsBytesOp.Result));

			var dstAddr = new StdAddI64Op(allocResult, lhsBytesOp.Result);
			block.AddOp(dstAddr);
			block.AddOp(new StdMemCopyOp(rhsBuf, dstAddr.Result, rhsBytesOp.Result));

			// Store element counts (not byte counts) for length/capacity
			var totalLenOp = new StdAddI64Op(lhsLen, rhsLen);
			block.AddOp(totalLenOp);

			EmitStructFieldStore(block, allocResult, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
			EmitStructFieldStore(block, totalLenOp.Result, tempName, ManagedFieldLength, MlirType.I64, varTypes);
			EmitStructFieldStore(block, totalLenOp.Result, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
			EmitStructFieldStore(block, lhsElemSize, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);

			// For managed elements (structs, enums): incref each copied element.
			// The memcpy above copied raw heap pointers without adjusting refcounts.
			if (op.IsStructElement) {
				var managedPtr = (StdI64)EmitLoad(block, tempName, varTypes);
				block.AddOp(new StdCallRuntimeOp("mm_incref_managed_elements", [managedPtr], null));
			}

			valueMap[op.Result] = new StdHeapPtr(concatPtr.Id, concatPtr.TypeName, tempName);
		}
	}

	private static void LowerManagedMemSlice(
	  MaxonManagedMemSliceOp op,
	  MlirFunction<StandardOp> func,
	  ref MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps,
	  string? inlineTarget = null) {

		var srcVarName = ResolveManagedVarName(op.Managed, valueMap);
		var srcLength = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldLength, MlirType.I64, varTypes);

		var start = (StdI64)valueMap[op.Start];
		var end = (StdI64)valueMap[op.End];

		// Bounds checks: end <= length and start <= end
		var sliceOneConst = new StdConstI64Op(1);
		block.AddOp(sliceOneConst);
		var lengthPlusOne = new StdAddI64Op(srcLength, sliceOneConst.Result);
		block.AddOp(lengthPlusOne);
		EmitBoundsCheck(block, end, lengthPlusOne.Result, "__mm_panic_slice_oob");
		var endPlusOne = new StdAddI64Op(end, sliceOneConst.Result);
		block.AddOp(endPlusOne);
		EmitBoundsCheck(block, start, endPlusOne.Result, "__mm_panic_slice_oob");

		var srcBuffer = LoadManagedBuffer(block, srcVarName, varTypes);

		if (op.IsBitPacked) {
			// Bit-packed bool slice: bit-by-bit copy
			var sliceLenOp = new StdSubI64Op(end, start);
			block.AddOp(sliceLenOp);
			var sliceByteSize = ComputeBitPackedByteSize(block, sliceLenOp.Result);

			// Heap-allocate __ManagedMemory struct, then a new raw buffer
			var managedTypeName = op.Result.TypeName;
			var tempName = inlineTarget
				?? temps.CreateTemp("slice", op.Result.Id, managedTypeName, OwnershipFlags.None);
			var slicePtr = (StdHeapPtr)EmitAlloc(block, 32, managedTypeName, tag: "Slice", scopeName: _currentFuncName);
			EmitStore(block, slicePtr, tempName, varTypes);

			var newBuffer = EmitRawAlloc(block, sliceByteSize, label: "slice.buf", scopeName: _currentFuncName);

			// Bit-by-bit copy loop: for i from 0 to sliceLen-1, get bit (start+i) from source, set bit i in dest
			var loopUid = MlirContext.Current.NextId();
			var loopVar = $"__slice_i_{loopUid}";
			var zeroInit = new StdConstI64Op(0);
			block.AddOp(zeroInit);
			EmitStore(block, zeroInit.Result, loopVar, varTypes);
			var srcBufVar = $"__slice_srcbuf_{loopUid}";
			EmitStore(block, srcBuffer, srcBufVar, varTypes);
			var dstBufVar = $"__slice_dstbuf_{loopUid}";
			EmitStore(block, newBuffer, dstBufVar, varTypes);
			var sliceLenVar = $"__slice_len_{loopUid}";
			EmitStore(block, sliceLenOp.Result, sliceLenVar, varTypes);
			var startVar = $"__slice_start_{loopUid}";
			EmitStore(block, start, startVar, varTypes);

			var loopHeaderLabel = $"__slice_hdr_{loopUid}";
			var loopBodyLabel = $"__slice_body_{loopUid}";
			var loopExitLabel = $"__slice_exit_{loopUid}";
			block.AddOp(new StdBrOp(loopHeaderLabel));

			var headerBlock = func.Body.AddBlock(loopHeaderLabel);
			var iReload = (StdI64)EmitLoad(headerBlock, loopVar, varTypes);
			var sliceLenReload = (StdI64)EmitLoad(headerBlock, sliceLenVar, varTypes);
			var cmpLoop = new StdCmpI64Op("lt", iReload, sliceLenReload);
			headerBlock.AddOp(cmpLoop);
			headerBlock.AddOp(new StdCondBrOp(cmpLoop.Result, loopBodyLabel, loopExitLabel));

			var bodyBlock = func.Body.AddBlock(loopBodyLabel);
			var iBody = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
			var startBody = (StdI64)EmitLoad(bodyBlock, startVar, varTypes);
			var srcBufBody = (StdI64)EmitLoad(bodyBlock, srcBufVar, varTypes);
			var srcIdx = new StdAddI64Op(startBody, iBody);
			bodyBlock.AddOp(srcIdx);
			var bitVal = EmitBitGet(bodyBlock, srcBufBody, srcIdx.Result);
			var dstBufBody = (StdI64)EmitLoad(bodyBlock, dstBufVar, varTypes);
			var iBody2 = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
			EmitBitSet(bodyBlock, dstBufBody, iBody2, bitVal);
			// Increment loop counter
			var iBody3 = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
			var oneInc = new StdConstI64Op(1);
			bodyBlock.AddOp(oneInc);
			var newI = new StdAddI64Op(iBody3, oneInc.Result);
			bodyBlock.AddOp(newI);
			EmitStore(bodyBlock, newI.Result, loopVar, varTypes);
			bodyBlock.AddOp(new StdBrOp(loopHeaderLabel));

			block = func.Body.AddBlock(loopExitLabel);
			var dstBufFinal = (StdI64)EmitLoad(block, dstBufVar, varTypes);
			var sliceLenFinal = (StdI64)EmitLoad(block, sliceLenVar, varTypes);
			var zeroElemSize = new StdConstI64Op(0);
			block.AddOp(zeroElemSize);

			EmitStructFieldStore(block, dstBufFinal, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
			EmitStructFieldStore(block, sliceLenFinal, tempName, ManagedFieldLength, MlirType.I64, varTypes);
			EmitStructFieldStore(block, sliceLenFinal, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
			EmitStructFieldStore(block, zeroElemSize.Result, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);

			valueMap[op.Result] = new StdHeapPtr(slicePtr.Id, slicePtr.TypeName, tempName);
		} else {
			var srcElemSize = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldElementSize, MlirType.I64, varTypes);

			// Convert element index to byte offset: start * element_size
			var startBytesOp = new StdMulI64Op(start, srcElemSize);
			block.AddOp(startBytesOp);

			// Source address for the slice data
			var srcAddrOp = new StdAddI64Op(srcBuffer, startBytesOp.Result);
			block.AddOp(srcAddrOp);

			// Slice length in elements is end - start
			var sliceLenOp = new StdSubI64Op(end, start);
			block.AddOp(sliceLenOp);

			// Byte count for the copy
			var sliceBytesOp = new StdMulI64Op(sliceLenOp.Result, srcElemSize);
			block.AddOp(sliceBytesOp);

			// Heap-allocate __ManagedMemory struct, then a new raw buffer
			var managedTypeName = op.Result.TypeName;
			var tempName = inlineTarget
				?? temps.CreateTemp("slice", op.Result.Id, managedTypeName, OwnershipFlags.None);
			var slicePtr = (StdHeapPtr)EmitAlloc(block, 32, managedTypeName, tag: "Slice", scopeName: _currentFuncName);
			EmitStore(block, slicePtr, tempName, varTypes);

			var newBuffer = EmitRawAlloc(block, sliceBytesOp.Result, label: "slice.buf", scopeName: _currentFuncName);

			// Copy data from source into the new buffer
			block.AddOp(new StdMemCopyOp(srcAddrOp.Result, newBuffer, sliceBytesOp.Result));

			EmitStructFieldStore(block, newBuffer, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
			EmitStructFieldStore(block, sliceLenOp.Result, tempName, ManagedFieldLength, MlirType.I64, varTypes);
			EmitStructFieldStore(block, sliceLenOp.Result, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
			EmitStructFieldStore(block, srcElemSize, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);

			// For managed elements (structs, enums): incref each copied element.
			// The memcpy above copied raw heap pointers without adjusting refcounts.
			if (op.IsStructElement) {
				var managedPtr = (StdI64)EmitLoad(block, tempName, varTypes);
				block.AddOp(new StdCallRuntimeOp("mm_incref_managed_elements", [managedPtr], null));
			}

			valueMap[op.Result] = new StdHeapPtr(slicePtr.Id, slicePtr.TypeName, tempName);
		}
	}

	/// <summary>
	/// __make_char_from_bytes(managed, pos, len): create a Character from bytes in managed memory.
	/// Allocates a new buffer, copies len bytes from source at pos, and creates a Character struct.
	/// </summary>
	private static void LowerMakeCharFromBytes(
	  MaxonMakeCharFromBytesOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps) {

		var srcVarName = ResolveManagedVarName(op.Managed, valueMap);
		var srcBuffer = LoadManagedBuffer(block, srcVarName, varTypes);
		var pos = (StdI64)valueMap[op.Pos];
		var len = (StdI64)valueMap[op.Len];

		// Compute source address: srcBuffer + pos
		var srcAddrOp = new StdAddI64Op(srcBuffer, pos);
		block.AddOp(srcAddrOp);

		// Store len and srcAddr to stack vars so they survive calls and memcopy
		var lenVar = $"__mkchar_len_{op.Result.Id}";
		EmitStore(block, len, lenVar, varTypes);
		var srcAddrVar = $"__mkchar_src_{op.Result.Id}";
		EmitStore(block, srcAddrOp.Result, srcAddrVar, varTypes);

		// Allocate outer Character struct, then ManagedMemory, then raw buffer
		var charVarName = temps.CreateTemp("char", op.Result.Id, "Character", OwnershipFlags.None);
		var charOuterPtr = (StdHeapPtr)EmitAlloc(block, 8, "Character", scopeName: _currentFuncName);
		EmitStore(block, charOuterPtr, charVarName, varTypes);

		var charManagedName = $"__char_managed_{op.Result.Id}";
		var charManagedPtr = EmitAlloc(block, 32, "__ManagedMemory", scopeName: _currentFuncName);
		EmitStore(block, charManagedPtr, charManagedName, varTypes);

		// Reload len for buffer allocation (alloc clobbers registers)
		var lenForAlloc = (StdI64)EmitLoad(block, lenVar, varTypes);
		var newBuf = EmitRawAlloc(block, lenForAlloc, label: "mkChar.buf", scopeName: _currentFuncName);

		// Store the new buffer pointer (alloc clobbers registers)
		var dstBufVar = $"__mkchar_dst_{op.Result.Id}";
		EmitStore(block, newBuf, dstBufVar, varTypes);

		// Reload values for memcopy (alloc clobbers registers)
		var reloadLen = (StdI64)EmitLoad(block, lenVar, varTypes);
		var reloadSrc = (StdI64)EmitLoad(block, srcAddrVar, varTypes);
		var reloadDst = (StdI64)EmitLoad(block, dstBufVar, varTypes);

		// Copy bytes from source to new buffer
		block.AddOp(new StdMemCopyOp(reloadSrc, reloadDst, reloadLen));

		// Reload all values again after memcopy (rep movsb clobbers RSI/RDI/RCX)
		var finalLen = (StdI64)EmitLoad(block, lenVar, varTypes);
		var finalBuf = (StdI64)EmitLoad(block, dstBufVar, varTypes);

		// Store ManagedMemory fields
		EmitStructFieldStore(block, finalBuf, charManagedName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, finalLen, charManagedName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, finalLen, charManagedName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var elemSizeConst = new StdConstI64Op(1);
		block.AddOp(elemSizeConst);
		EmitStructFieldStore(block, elemSizeConst.Result, charManagedName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// Store _managed heap pointer at offset 0 and incref it (Character now owns a reference)
		var charManagedReload = EmitLoad(block, charManagedName, varTypes);
		EmitStructFieldStore(block, charManagedReload, charVarName, 0, MlirType.I64, varTypes);
		EmitIncrefValue(block, (StdI64)charManagedReload, scopeName: _currentFuncName);
		valueMap[op.Result] = new StdHeapPtr(charOuterPtr.Id, charOuterPtr.TypeName, charVarName);
	}
}
