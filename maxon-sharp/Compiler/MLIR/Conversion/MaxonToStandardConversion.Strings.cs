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
	  MlirModule<StandardOp> result) {
		var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(value);

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

	private static string EmitManagedMemoryLiteral(
	  string value,
	  int resultId,
	  string rdataPrefix,
	  string tempPrefix,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames,
	  MlirModule<StandardOp> result) {
		var rdataLabel = $"__{rdataPrefix}_{resultId}";
		var (bufferPtr, lengthVal) = EmitRdataLiteral(value, rdataLabel, block, result);

		// Heap-allocate __ManagedMemory struct (32 bytes)
		var managedName = $"__{tempPrefix}_managed_{resultId}";
		var managedPtr = EmitAlloc(block, 32);
		EmitStore(block, managedPtr, managedName, varTypes);

		// Store __ManagedMemory fields via heap pointer
		EmitStructFieldStore(block, bufferPtr, managedName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, lengthVal, managedName, ManagedFieldLength, MlirType.I64, varTypes);
		var capConst = new StdConstI64Op(0);
		block.AddOp(capConst);
		EmitStructFieldStore(block, capConst.Result, managedName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var elemSizeConst = new StdConstI64Op(1);
		block.AddOp(elemSizeConst);
		EmitStructFieldStore(block, elemSizeConst.Result, managedName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// Heap-allocate outer struct (String/Character: _managed + _iterPos = 16 bytes)
		var tempName = $"__{tempPrefix}_{resultId}";
		var outerPtr = EmitAlloc(block, 16);
		EmitStore(block, outerPtr, tempName, varTypes);

		// Store _managed heap pointer in outer struct at offset 0
		var managedPtrReload = EmitLoad(block, managedName, varTypes);
		EmitStructFieldStore(block, managedPtrReload, tempName, 0, MlirType.I64, varTypes);

		structVarNames[resultId] = tempName;
		return tempName;
	}

	private static void LowerStringLiteral(
	  MaxonStringLiteralOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames,
	  MlirModule<StandardOp> result) {
		var tempName = EmitManagedMemoryLiteral(op.Value, op.Result.Id, "str", "strtmp", block, varTypes, structVarNames, result);

		// Store _iterPos at offset 8 (second field in String struct)
		var iterConst = new StdConstI64Op(0);
		block.AddOp(iterConst);
		EmitStructFieldStore(block, iterConst.Result, tempName, 8, MlirType.I64, varTypes);
	}

	private static void LowerByteStringLiteral(
	  MaxonByteStringLiteralOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames,
	  MlirModule<StandardOp> result) {
		// Array layout: iterIndex (offset 0), managed (offset 8)
		// EmitManagedMemoryLiteral stores managed at offset 0 (for String layout),
		// so we build the ByteArray struct manually.
		var rdataLabel = $"__bstr_{op.Result.Id}";
		var (bufferPtr, lengthVal) = EmitRdataLiteral(op.Value, rdataLabel, block, result);

		// Heap-allocate __ManagedMemory struct (32 bytes)
		var managedName = $"__bstrtmp_managed_{op.Result.Id}";
		var managedPtr = EmitAlloc(block, 32);
		EmitStore(block, managedPtr, managedName, varTypes);

		EmitStructFieldStore(block, bufferPtr, managedName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, lengthVal, managedName, ManagedFieldLength, MlirType.I64, varTypes);
		var capConst = new StdConstI64Op(0);
		block.AddOp(capConst);
		EmitStructFieldStore(block, capConst.Result, managedName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var elemSizeConst = new StdConstI64Op(1);
		block.AddOp(elemSizeConst);
		EmitStructFieldStore(block, elemSizeConst.Result, managedName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// Heap-allocate outer Array struct (16 bytes: iterIndex at 0, managed at 8)
		var tempName = $"__bstrtmp_{op.Result.Id}";
		var outerPtr = EmitAlloc(block, 16);
		EmitStore(block, outerPtr, tempName, varTypes);

		// Store iterIndex = 0 at offset 0
		var iterConst = new StdConstI64Op(0);
		block.AddOp(iterConst);
		EmitStructFieldStore(block, iterConst.Result, tempName, 0, MlirType.I64, varTypes);

		// Store managed pointer at offset 8
		var managedPtrReload = EmitLoad(block, managedName, varTypes);
		EmitStructFieldStore(block, managedPtrReload, tempName, 8, MlirType.I64, varTypes);

		structVarNames[op.Result.Id] = tempName;
	}

	private static void LowerCharLiteral(
	  MaxonCharLiteralOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames,
	  MlirModule<StandardOp> result) {
		EmitManagedMemoryLiteral(op.Value, op.Result.Id, "chr", "chrtmp", block, varTypes, structVarNames, result);
	}

	private static void LowerStringInterp(
	  MaxonStringInterpOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames,
	  MlirModule<StandardOp> result) {

		var partInfos = new List<(StdI64 Buffer, StdI64 Length)>();

		foreach (var (IsLiteral, LiteralValue, ExprValue, FormatSpec, OptimalType) in op.Parts) {
			if (IsLiteral) {
				if (string.IsNullOrEmpty(LiteralValue)) continue;

				var litId = MlirContext.Current.NextId();
				var rdataLabel = $"__interp_lit_{litId}";
				partInfos.Add(EmitRdataLiteral(LiteralValue!, rdataLabel, block, result));
			} else {
				var exprValue = ExprValue!;

				if (structVarNames.TryGetValue(exprValue.Id, out var managedVarName)) {
					partInfos.Add(EmitStructInterpolation(managedVarName, block, varTypes));
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
						if (isUnsigned) {
							partInfos.Add(EmitU64ToStringFormatted(stdVal, FormatSpec, block, varTypes, result));
						} else {
							partInfos.Add(EmitI64ToStringFormatted(stdVal, FormatSpec, block, varTypes, result));
						}
					} else {
						if (isUnsigned) {
							partInfos.Add(EmitU64ToString(stdVal, block, varTypes));
						} else {
							partInfos.Add(EmitI64ToString(stdVal, block, varTypes));
						}
					}
				} else if (exprValue is MaxonFloat && valueMap[exprValue] is StdF32 f32ForStr) {
					// Promote f32 to f64 for string conversion (reuses existing f64 toString runtime)
					var promote = new StdF32ToF64Op(f32ForStr);
					block.AddOp(promote);
					if (FormatSpec != null) {
						partInfos.Add(EmitF64ToStringFormatted(promote.Result, FormatSpec, block, varTypes, result));
					} else {
						partInfos.Add(EmitF64ToString(promote.Result, block, varTypes));
					}
				} else if (exprValue is MaxonFloat) {
					if (FormatSpec != null) {
						partInfos.Add(EmitF64ToStringFormatted((StdF64)valueMap[exprValue], FormatSpec, block, varTypes, result));
					} else {
						partInfos.Add(EmitF64ToString((StdF64)valueMap[exprValue], block, varTypes));
					}
				} else if (exprValue is MaxonBool) {
					partInfos.Add(EmitBoolToString((StdBool)valueMap[exprValue], block, varTypes));
				} else if (exprValue is MaxonEnum enumValue) {
					partInfos.Add(EmitEnumToString(enumValue, valueMap, block, varTypes, result));
				} else {
					throw new InvalidOperationException(
					  $"String interpolation: unsupported expression type {exprValue.GetType().Name} for value %{exprValue.Id}");
				}
			}
		}

		if (partInfos.Count == 0) {
			var tempName = EmitManagedMemoryLiteral("", op.Result.Id, "interp", "interptmp", block, varTypes, structVarNames, result);
			var iterConst = new StdConstI64Op(0);
			block.AddOp(iterConst);
			EmitStore(block, iterConst.Result, $"{tempName}._iterPos", varTypes);
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

		// Allocate buffer (totalLen + 1 for null terminator)
		var oneOp = new StdConstI64Op(1);
		block.AddOp(oneOp);
		var allocSize = new StdAddI64Op(totalLen, oneOp.Result);
		block.AddOp(allocSize);

		var allocResult = EmitAlloc(block, allocSize.Result);

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

		// Create String struct with heap-allocated __ManagedMemory
		var tempName2 = $"__interptmp_{op.Result.Id}";

		// Heap-allocate __ManagedMemory (32 bytes)
		var interpManagedName = $"__interp_managed_{op.Result.Id}";
		var interpManagedPtr = EmitAlloc(block, 32);
		EmitStore(block, interpManagedPtr, interpManagedName, varTypes);

		var finalBuf = (StdI64)EmitLoad(block, interpBufVar, varTypes);
		EmitStructFieldStore(block, finalBuf, interpManagedName, ManagedFieldBuffer, MlirType.I64, varTypes);

		var finalLen = (StdI64)EmitLoad(block, interpTotalLenVar, varTypes);
		EmitStructFieldStore(block, finalLen, interpManagedName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, finalLen, interpManagedName, ManagedFieldCapacity, MlirType.I64, varTypes);

		var elemSizeConst2 = new StdConstI64Op(1);
		block.AddOp(elemSizeConst2);
		EmitStructFieldStore(block, elemSizeConst2.Result, interpManagedName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// Heap-allocate String struct (16 bytes: _managed + _iterPos)
		var interpOuterPtr = EmitAlloc(block, 16);
		EmitStore(block, interpOuterPtr, tempName2, varTypes);

		// Store _managed heap pointer at offset 0
		var interpManagedReload = EmitLoad(block, interpManagedName, varTypes);
		EmitStructFieldStore(block, interpManagedReload, tempName2, 0, MlirType.I64, varTypes);

		// Store _iterPos at offset 8
		var iterPosConst = new StdConstI64Op(0);
		block.AddOp(iterPosConst);
		EmitStructFieldStore(block, iterPosConst.Result, tempName2, 8, MlirType.I64, varTypes);

		structVarNames[op.Result.Id] = tempName2;
	}

	/// <summary>
	/// Allocates a buffer, calls a runtime conversion function, and returns (buffer, length).
	/// Used by EmitI64ToString, EmitF64ToString, and EmitBoolToString.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length) EmitRuntimeToString(
	  StdValue value,
	  string runtimeFuncName,
	  int bufferSize,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes) {

		var bufResult = EmitAlloc(block, bufferSize);

		// Store buffer pointer so it survives the runtime call
		var bufVarName = $"__tostr_buf_{bufResult.Id}";
		EmitStore(block, bufResult, bufVarName, varTypes);

		var lenResult = new StdI64(MlirContext.Current.NextId());
		block.AddOp(new StdCallRuntimeOp(runtimeFuncName, [value, bufResult], lenResult));

		var finalBuf = (StdI64)EmitLoad(block, bufVarName, varTypes);
		return (finalBuf, lenResult);
	}

	private static (StdI64 Buffer, StdI64 Length) EmitI64ToString(
	  StdValue intValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
	  EmitRuntimeToString(intValue, "maxon_i64_to_string", 21, block, varTypes);

	private static (StdI64 Buffer, StdI64 Length) EmitU64ToString(
	  StdValue intValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
	  EmitRuntimeToString(intValue, "maxon_u64_to_string", 21, block, varTypes);

	private static (StdI64 Buffer, StdI64 Length) EmitF64ToString(
	  StdF64 floatValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
	  EmitRuntimeToString(floatValue, "maxon_f64_to_string", 32, block, varTypes);

	/// <summary>
	/// Allocates a buffer, emits the format spec as rdata, calls a formatted runtime conversion function,
	/// and returns (buffer, length). Used for format-specifier string interpolation on built-in types.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length) EmitRuntimeToStringFormatted(
	  StdValue value,
	  string runtimeFuncName,
	  int bufferSize,
	  string formatSpec,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  MlirModule<StandardOp> result) {

		var bufResult = EmitAlloc(block, bufferSize);

		// Store buffer pointer so it survives the runtime call
		var bufVarName = $"__tostr_buf_{bufResult.Id}";
		EmitStore(block, bufResult, bufVarName, varTypes);

		// Emit format spec as rdata literal
		var fmtId = MlirContext.Current.NextId();
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
		return (finalBuf, lenResult);
	}

	private static (StdI64 Buffer, StdI64 Length) EmitI64ToStringFormatted(
	  StdValue intValue, string formatSpec, MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes, MlirModule<StandardOp> result) =>
	  EmitRuntimeToStringFormatted(intValue, "maxon_i64_to_string_fmt", 72, formatSpec, block, varTypes, result);

	private static (StdI64 Buffer, StdI64 Length) EmitU64ToStringFormatted(
	  StdValue intValue, string formatSpec, MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes, MlirModule<StandardOp> result) =>
	  EmitRuntimeToStringFormatted(intValue, "maxon_u64_to_string_fmt", 72, formatSpec, block, varTypes, result);

	private static (StdI64 Buffer, StdI64 Length) EmitF64ToStringFormatted(
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
	private static (StdI64 Buffer, StdI64 Length) EmitEnumToString(
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
			return EmitStringEnumToString(enumType, (StdI64)stdValue, block, result);
		}

		if (backingMlirType == MlirType.F64) {
			return EmitF64ToString((StdF64)stdValue, block, varTypes);
		}

		// Constants interpolate as their backing value; enums interpolate as their case name
		if (enumType is MlirConstantsType) {
			return EmitI64ToString((StdI64)stdValue, block, varTypes);
		}
		return EmitEnumCaseNameToString(enumType, (StdI64)stdValue, block, result);
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

		var fallbackLabel = $"__enum_name_fallback_{MlirContext.Current.NextId()}";
		var (currentBuf, currentLen) = EmitRdataLiteral("?", fallbackLabel, block, result);

		foreach (var enumCase in enumType.Cases) {
			var caseLabel = $"__enum_name_{enumType.Name}_{enumCase.Name}_{MlirContext.Current.NextId()}";
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
		var fallbackLabel = $"__strenum_fallback_{MlirContext.Current.NextId()}";
		var (currentBuf, currentLen) = EmitRdataLiteral("?", fallbackLabel, block, result);

		// For each case, compare ordinal and conditionally select the case's string
		foreach (var enumCase in enumType.Cases) {
			if (enumCase.RawValue is not string strValue) continue;

			var caseLabel = $"__strenum_case_{enumType.Name}_{enumCase.Name}_{MlirContext.Current.NextId()}";
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
	/// Heap-allocates both the outer struct and the __ManagedMemory inner struct.
	private static void EmitManagedStructFromBufLen(
	  string tempName, StdI64 bufferPtr, StdI64 lengthVal,
	  bool hasIterPos, MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes, Dictionary<int, string> structVarNames, int resultId) {
		// Heap-allocate __ManagedMemory (32 bytes)
		var managedName = $"{tempName}__managed";
		var managedPtr = EmitAlloc(block, 32);
		EmitStore(block, managedPtr, managedName, varTypes);
		EmitStructFieldStore(block, bufferPtr, managedName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, lengthVal, managedName, ManagedFieldLength, MlirType.I64, varTypes);
		var capConst = new StdConstI64Op(0);
		block.AddOp(capConst);
		EmitStructFieldStore(block, capConst.Result, managedName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var elemSizeConst = new StdConstI64Op(1);
		block.AddOp(elemSizeConst);
		EmitStructFieldStore(block, elemSizeConst.Result, managedName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// Heap-allocate outer struct (String=16 bytes, Character=8 bytes)
		int outerSize = hasIterPos ? 16 : 8;
		var outerPtr = EmitAlloc(block, outerSize);
		EmitStore(block, outerPtr, tempName, varTypes);

		// Store _managed heap pointer at offset 0
		var managedPtrReload = EmitLoad(block, managedName, varTypes);
		EmitStructFieldStore(block, managedPtrReload, tempName, 0, MlirType.I64, varTypes);

		if (hasIterPos) {
			var iterConst = new StdConstI64Op(0);
			block.AddOp(iterConst);
			EmitStructFieldStore(block, iterConst.Result, tempName, 8, MlirType.I64, varTypes);
		}

		structVarNames[resultId] = tempName;
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

	/// Looks up an enum case name by ordinal via a select chain. Returns (buffer, length).
	private static (StdI64 Buffer, StdI64 Length) EmitEnumNameLookup(
	  MlirEnumType enumType, StdI64 ordinalValue,
	  MlirBlock<StandardOp> block, MlirModule<StandardOp> result) {
		var fallbackLabel = $"__enumname_fallback_{MlirContext.Current.NextId()}";
		var (currentBuf, currentLen) = EmitRdataLiteral("?", fallbackLabel, block, result);

		foreach (var enumCase in enumType.Cases) {
			var caseLabel = $"__enumname_{enumType.Name}_{enumCase.Name}_{MlirContext.Current.NextId()}";
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
	private static (StdI64 Buffer, StdI64 Length) EmitBoolToString(
	  StdBool boolValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
	  EmitRuntimeToString(boolValue, "maxon_bool_to_string", 6, block, varTypes);

	private static void LowerManagedMemConcat(
	  MaxonManagedMemConcatOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames) {

		var lhsVarName = ResolveManagedVarName(op.Lhs, structVarNames);
		var rhsVarName = ResolveManagedVarName(op.Rhs, structVarNames);

		var lhsBuf = LoadManagedBuffer(block, lhsVarName, varTypes);
		var lhsLen = (StdI64)EmitStructFieldLoad(block, lhsVarName, ManagedFieldLength, MlirType.I64, varTypes);
		var rhsBuf = LoadManagedBuffer(block, rhsVarName, varTypes);
		var rhsLen = (StdI64)EmitStructFieldLoad(block, rhsVarName, ManagedFieldLength, MlirType.I64, varTypes);

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

		var allocResult = EmitAlloc(block, allocSizeOp.Result);

		block.AddOp(new StdMemCopyOp(lhsBuf, allocResult, lhsBytesOp.Result));

		var dstAddr = new StdAddI64Op(allocResult, lhsBytesOp.Result);
		block.AddOp(dstAddr);
		block.AddOp(new StdMemCopyOp(rhsBuf, dstAddr.Result, rhsBytesOp.Result));

		// Store element counts (not byte counts) for length/capacity
		var totalLenOp = new StdAddI64Op(lhsLen, rhsLen);
		block.AddOp(totalLenOp);

		// Heap-allocate __ManagedMemory result (32 bytes)
		var tempName = $"__concat_{op.Result.Id}";
		var concatPtr = EmitAlloc(block, 32);
		EmitStore(block, concatPtr, tempName, varTypes);
		EmitStructFieldStore(block, allocResult, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, totalLenOp.Result, tempName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, totalLenOp.Result, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
		EmitStructFieldStore(block, lhsElemSize, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
		structVarNames[op.Result.Id] = tempName;
	}

	private static void LowerManagedMemSlice(
	  MaxonManagedMemSliceOp op,
	  MlirBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  Dictionary<int, string> structVarNames) {

		var srcVarName = ResolveManagedVarName(op.Managed, structVarNames);
		var srcBuffer = LoadManagedBuffer(block, srcVarName, varTypes);
		var srcElemSize = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldElementSize, MlirType.I64, varTypes);

		var start = (StdI64)valueMap[op.Start];
		var end = (StdI64)valueMap[op.End];

		// Convert element index to byte offset: start * element_size
		var startBytesOp = new StdMulI64Op(start, srcElemSize);
		block.AddOp(startBytesOp);

		// Slice buffer points into the existing allocation at byte offset
		var sliceBufferOp = new StdAddI64Op(srcBuffer, startBytesOp.Result);
		block.AddOp(sliceBufferOp);

		// Slice length in elements is end - start
		var sliceLenOp = new StdSubI64Op(end, start);
		block.AddOp(sliceLenOp);

		// capacity=0 marks the slice as read-only so COW triggers on mutation
		var zeroOp = new StdConstI64Op(0);
		block.AddOp(zeroOp);

		// Heap-allocate __ManagedMemory result (32 bytes)
		var tempName = $"__slice_{op.Result.Id}";
		var slicePtr = EmitAlloc(block, 32);
		EmitStore(block, slicePtr, tempName, varTypes);
		EmitStructFieldStore(block, sliceBufferOp.Result, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, sliceLenOp.Result, tempName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, zeroOp.Result, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
		EmitStructFieldStore(block, srcElemSize, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
		structVarNames[op.Result.Id] = tempName;
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
	  Dictionary<int, string> structVarNames) {

		var srcVarName = ResolveManagedVarName(op.Managed, structVarNames);
		var srcBuffer = LoadManagedBuffer(block, srcVarName, varTypes);
		var pos = (StdI64)valueMap[op.Pos];
		var len = (StdI64)valueMap[op.Len];

		// Compute source address: srcBuffer + pos
		var srcAddrOp = new StdAddI64Op(srcBuffer, pos);
		block.AddOp(srcAddrOp);

		// Store len, srcAddr, and dstBuf to stack vars so they survive calls and memcopy
		var lenVar = $"__mkchar_len_{op.Result.Id}";
		EmitStore(block, len, lenVar, varTypes);
		var srcAddrVar = $"__mkchar_src_{op.Result.Id}";
		EmitStore(block, srcAddrOp.Result, srcAddrVar, varTypes);

		// Allocate new buffer
		var newBuf = EmitAlloc(block, len);

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

		// Create Character struct with heap-allocated __ManagedMemory
		var charVarName = $"__char_{op.Result.Id}";

		// Heap-allocate __ManagedMemory (32 bytes)
		var charManagedName = $"__char_managed_{op.Result.Id}";
		var charManagedPtr = EmitAlloc(block, 32);
		EmitStore(block, charManagedPtr, charManagedName, varTypes);
		EmitStructFieldStore(block, finalBuf, charManagedName, ManagedFieldBuffer, MlirType.I64, varTypes);
		EmitStructFieldStore(block, finalLen, charManagedName, ManagedFieldLength, MlirType.I64, varTypes);
		EmitStructFieldStore(block, finalLen, charManagedName, ManagedFieldCapacity, MlirType.I64, varTypes);
		var elemSizeConst = new StdConstI64Op(1);
		block.AddOp(elemSizeConst);
		EmitStructFieldStore(block, elemSizeConst.Result, charManagedName, ManagedFieldElementSize, MlirType.I64, varTypes);

		// Heap-allocate Character struct (8 bytes: _managed only)
		var charOuterPtr = EmitAlloc(block, 8);
		EmitStore(block, charOuterPtr, charVarName, varTypes);
		// Store _managed heap pointer at offset 0
		var charManagedReload = EmitLoad(block, charManagedName, varTypes);
		EmitStructFieldStore(block, charManagedReload, charVarName, 0, MlirType.I64, varTypes);
		structVarNames[op.Result.Id] = charVarName;
	}
}
