using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static partial class MaxonToStandardConversion {
	// ============================================================================
	// Allocation tracking helpers
	// ============================================================================

	/// <summary>
	/// Get or create an rdata label for a tracking tag string.
	/// </summary>
	private static string GetOrCreateTrackingTag(string tag) {
		if (_rdataTagCache!.TryGetValue(tag, out var label))
			return label;
		label = $"__track_tag_{_rdataTagCache.Count}";
		var bytes = System.Text.Encoding.UTF8.GetBytes(tag);
		_resultModule!.RdataEntries.Add((label, bytes, 1));
		_rdataTagCache[tag] = label;
		return label;
	}

	/// <summary>
	/// Emit ops to load a tracking tag string pointer and length.
	/// </summary>
	private static (StdI64 tagPtr, StdI64 tagLen) EmitTrackingTagLoad(
	  MlirBlock<StandardOp> block, string tag) {
		var rdataLabel = GetOrCreateTrackingTag(tag);
		var leaOp = new StdLeaRdataOp(rdataLabel);
		block.AddOp(leaOp);
		var ptrOp = new StdPtrToI64Op(leaOp.Result);
		block.AddOp(ptrOp);
		var lenOp = new StdConstI64Op(System.Text.Encoding.UTF8.GetByteCount(tag));
		block.AddOp(lenOp);
		return (ptrOp.Result, lenOp.Result);
	}

	/// <summary>
	/// Emit tracking call: ALLOC #N: X bytes (tag)
	/// </summary>
	private static void EmitTrackAlloc(
	  MlirBlock<StandardOp> block, StdI64 ptr, StdI64 size, string tag) {
		if (!_trackAllocs) return;
		var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
		block.AddOp(new StdCallRuntimeOp("maxon_track_alloc", [ptr, size, tagPtr, tagLen]));
	}

	/// <summary>
	/// Emit tracking call: INCREF: tag -> rc=N
	/// </summary>
	private static void EmitTrackIncref(
	  MlirBlock<StandardOp> block, string tag, long refcount) {
		if (!_trackAllocs) return;
		var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
		var rcOp = new StdConstI64Op(refcount);
		block.AddOp(rcOp);
		block.AddOp(new StdCallRuntimeOp("maxon_track_incref", [tagPtr, tagLen, rcOp.Result]));
	}

	/// <summary>
	/// Emit conditional DECREF: only fires if capacity > 0 (heap-allocated/tracked buffer).
	/// </summary>
	private static void EmitTrackDecrefIfHeap(
	  MlirBlock<StandardOp> block, string tag, long refcount, StdI64 capacity) {
		if (!_trackAllocs) return;
		var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
		var rcOp = new StdConstI64Op(refcount);
		block.AddOp(rcOp);
		block.AddOp(new StdCallRuntimeOp("maxon_track_decref_if_heap", [tagPtr, tagLen, rcOp.Result, capacity]));
	}

	/// <summary>
	/// Emit tracking call: CLEANUP: tag
	/// </summary>
	private static void EmitTrackCleanup(
	  MlirBlock<StandardOp> block, string tag) {
		if (!_trackAllocs) return;
		var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
		block.AddOp(new StdCallRuntimeOp("maxon_track_cleanup", [tagPtr, tagLen]));
	}

	/// <summary>
	/// Emit tracking call: MOVE: tag
	/// </summary>
	private static void EmitTrackMove(
	  MlirBlock<StandardOp> block, string tag) {
		if (!_trackAllocs) return;
		var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
		block.AddOp(new StdCallRuntimeOp("maxon_track_move", [tagPtr, tagLen]));
	}

	/// <summary>
	/// Emit tracking call: COPY: tag
	/// </summary>
	private static void EmitTrackCopy(
	  MlirBlock<StandardOp> block, string tag) {
		if (!_trackAllocs) return;
		var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
		block.AddOp(new StdCallRuntimeOp("maxon_track_copy", [tagPtr, tagLen]));
	}

	/// <summary>
	/// Emit tracking call: FREE #N: X bytes (tag)
	/// </summary>
	private static void EmitTrackFree(
	  MlirBlock<StandardOp> block, StdI64 ptr, string tag) {
		if (!_trackAllocs) return;
		var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
		block.AddOp(new StdCallRuntimeOp("maxon_track_free", [ptr, tagPtr, tagLen]));
	}

	/// <summary>
	/// Returns the offsets of managed fields (__ManagedMemory-containing fields) within a struct type.
	/// Each entry is (byteOffset, fieldTypeName) where byteOffset is the offset of the __ManagedMemory
	/// within the struct element, and fieldTypeName is the name of the field's type (e.g. "String").
	/// Returns empty list if the struct has no managed fields.
	/// </summary>
	private static List<(int offset, string typeName)> GetManagedElementFieldInfo(
	  MlirStructType elementType) {
		var result = new List<(int, string)>();
		foreach (var field in elementType.Fields) {
			if (field.Type is MlirStructType fieldStruct) {
				if (fieldStruct.Name == "__ManagedMemory") {
					result.Add((field.Offset, elementType.Name));
				} else {
					// Check nested struct for __ManagedMemory fields
					foreach (var nestedField in fieldStruct.Fields) {
						if (nestedField.Type is MlirStructType nestedType && nestedType.Name == "__ManagedMemory") {
							result.Add((field.Offset + nestedField.Offset, fieldStruct.Name));
							break;
						}
					}
				}
			}
		}
		return result;
	}

	/// <summary>
	/// Gets the element type for an array/collection struct type by looking up the "Element" type parameter.
	/// Only matches types with a field named "managed" (Array convention), not "_managed" (String/Character).
	/// Returns null if the struct is not an array-like collection or the element type is not a struct.
	/// </summary>
	private static MlirStructType? GetArrayElementStructType(
	  string structTypeName, Dictionary<string, MlirType> typeDefs) {
		if (!typeDefs.TryGetValue(structTypeName, out var typeDef) || typeDef is not MlirStructType structType)
			return null;
		// Only apply to types with a field named "managed" (Array convention for storing elements)
		if (structType.GetField("managed") is not { Type: MlirStructType { Name: "__ManagedMemory" } })
			return null;
		if (!structType.TypeParams.TryGetValue("Element", out var elementType))
			return null;
		return elementType as MlirStructType;
	}

	/// <summary>
	/// Resolves the element type for an array field within a composite struct (e.g. Map).
	/// For inner aliases like KeyArray whose Element type is a generic parameter (Key),
	/// resolves through the parent struct's TypeParams (Key→String) to get the concrete element type.
	/// </summary>
	private static MlirStructType? ResolveArrayElementType(
	  string arrayTypeName, MlirStructType parentStruct, Dictionary<string, MlirType> typeDefs) {
		// First try direct resolution (works for concrete array types)
		var direct = GetArrayElementStructType(arrayTypeName, typeDefs);
		if (direct != null) return direct;

		// For generic inner aliases, the Element type param is an unresolved MlirTypeParameterType.
		// Resolve it through the parent struct's TypeParams.
		if (!typeDefs.TryGetValue(arrayTypeName, out var arrayTypeDef) || arrayTypeDef is not MlirStructType arrayStruct)
			return null;
		if (arrayStruct.GetField("managed") is not { Type: MlirStructType { Name: "__ManagedMemory" } })
			return null;
		if (!arrayStruct.TypeParams.TryGetValue("Element", out var elementType))
			return null;

		// Element type is a generic parameter (e.g. Key) — resolve through parent's TypeParams
		if (elementType is MlirTypeParameterType typeParam) {
			if (parentStruct.TypeParams.TryGetValue(typeParam.ParameterName, out var resolved))
				return resolved as MlirStructType;
		}
		return null;
	}

	private static string? GetManagedFieldName(MlirStructType structType) {
		var managedField = structType.GetField("managed");
		if (managedField != null) return "managed";
		foreach (var field in structType.Fields)
			if (field.Type is MlirStructType nested && nested.Name == "__ManagedMemory")
				return field.Name;
		return null;
	}


	/// <summary>
	/// Finds ALL managed buffer paths within a struct type, recursing into nested structs.
	/// For MultiManaged with fields {numbers: IntArray, text: String, tag: String}, returns
	/// ["varName.numbers.managed.buffer", "varName.text._managed.buffer", "varName.tag._managed.buffer"].
	/// For single-managed structs like IntArray, returns ["varName.managed.buffer"].
	/// </summary>
	private static List<string> GetAllManagedBufferPaths(string varName, string structTypeName, Dictionary<string, MlirType> typeDefs) {
		var result = new List<string>();
		if (structTypeName == "__ManagedMemory") {
			result.Add($"{varName}.buffer");
			return result;
		}
		if (!typeDefs.TryGetValue(structTypeName, out var typeDef) || typeDef is not MlirStructType structType)
			return result;
		foreach (var field in structType.Fields) {
			if (field.Type is MlirStructType fieldStruct) {
				var nestedPaths = GetAllManagedBufferPaths($"{varName}.{field.Name}", fieldStruct.Name, typeDefs);
				result.AddRange(nestedPaths);
			}
		}
		return result;
	}

	/// <summary>
	/// For each buffer path within a struct, determines whether that path's containing array
	/// has elements with managed fields, and if so, records the element info keyed by buffer path.
	/// For a Map with String keys, "m.keys.managed.buffer" gets String's element info,
	/// while "m.values.managed.buffer" and "m.states.managed.buffer" get nothing.
	/// </summary>
	private static void PopulateManagedBufferElementInfo(
	  List<string> bufferPaths, string dstName, string structTypeName,
	  Dictionary<string, MlirType> typeDefs,
	  Dictionary<string, List<(int offset, string typeName)>> managedBufferElementInfo) {
		// For simple array types, check the top-level struct directly
		var topLevelElement = GetArrayElementStructType(structTypeName, typeDefs);
		if (topLevelElement != null) {
			var elemFieldInfo = GetManagedElementFieldInfo(topLevelElement);
			if (elemFieldInfo.Count > 0) {
				foreach (var bufferPath in bufferPaths)
					managedBufferElementInfo[bufferPath] = elemFieldInfo;
			}
			return;
		}

		// For composite types (e.g. Map), resolve each buffer path's containing array type
		if (!typeDefs.TryGetValue(structTypeName, out var typeDef) || typeDef is not MlirStructType structType)
			return;

		foreach (var bufferPath in bufferPaths) {
			// Extract the field name from the buffer path
			// e.g. "m.keys.managed.buffer" → "keys"
			var suffix = bufferPath[(dstName.Length + 1)..];
			var dotIdx = suffix.IndexOf('.');
			if (dotIdx < 0) continue;
			var fieldName = suffix[..dotIdx];

			var field = structType.GetField(fieldName);
			if (field?.Type is not MlirStructType fieldStruct) continue;

			// Resolve element type: for generic inner aliases (e.g. KeyArray with Element=Key),
			// resolve through the parent struct's TypeParams (e.g. Map's Key→String)
			var elementType = ResolveArrayElementType(fieldStruct.Name, structType, typeDefs);
			if (elementType == null) continue;

			var elemFieldInfo = GetManagedElementFieldInfo(elementType);
			if (elemFieldInfo.Count > 0)
				managedBufferElementInfo[bufferPath] = elemFieldInfo;
		}
	}

	/// <summary>
	/// Emit the cleanup sequence for a managed variable before scope exit.
	/// In tracking mode: calls maxon_cleanup_managed which prints CLEANUP/DECREF/FREE and frees.
	/// In non-tracking mode: just calls maxon_free on the buffer.
	/// If elementFieldInfo is provided, iterates all elements and cleans up their managed fields
	/// between the CLEANUP tag and the DECREF/FREE.
	/// </summary>
	private static void EmitManagedCleanup(
	  MlirBlock<StandardOp> block,
	  string varName,
	  string bufferVarPath,
	  Dictionary<string, string> varTypes,
	  Dictionary<string, MlirType> typeDefs,
	  Dictionary<string, string> varNameToStructType,
	  List<(int offset, string typeName)>? elementFieldInfo = null) {
		if (!_trackAllocs) return; // TODO: enable non-tracking cleanup once validated

		// With heap refs, bufferVarPath is the variable name holding the __ManagedMemory heap pointer
		// We need to extract the managed var name from the path. The path format is like
		// "varName.managed" or just "varName" for __ManagedMemory directly.
		// Actually, bufferVarPath is something like "arr.managed.buffer" — but with heap refs,
		// managed struct fields aren't accessed as named vars anymore. We need to navigate
		// through heap pointers. The managed var name is the struct that holds the managed field.
		// For now, we need to resolve the managed struct's heap pointer from the path.
		// bufferVarPath format: "varName.fieldPath...buffer" where the last segment before buffer
		// is the __ManagedMemory struct. With heap refs, we need to walk the pointer chain.

		// For single-managed structs like IntArray: path = "arr.managed.buffer"
		//   -> load arr's heap ptr, load managed field (another heap ptr), load buffer/capacity from it
		// For __ManagedMemory directly: path = "varName.buffer"
		//   -> load varName's heap ptr, load buffer/capacity from it
		// For multi-managed: path = "item.numbers.managed.buffer"
		//   -> load item, load numbers, load managed, load buffer/capacity

		// Extract the managed var name (everything before ".buffer")
		var managedBasePath = bufferVarPath[..bufferVarPath.LastIndexOf('.')];

		// Walk the path to get the __ManagedMemory heap pointer
		// managedBasePath could be "arr.managed", "varName", "item.numbers.managed", etc.
		// The base variable is the first segment, then each subsequent segment is a field access.
		var segments = managedBasePath.Split('.');
		var currentVar = segments[0]; // base variable, should be in varTypes as i64

		// Navigate through the struct fields to get to the __ManagedMemory heap pointer.
		// Resolve field offsets using type information.
		var currentTypeName = varNameToStructType.TryGetValue(segments[0], out var vst) ? vst : null;
		for (int si = 1; si < segments.Length; si++) {
			int fieldOffset = 0;
			if (currentTypeName != null && typeDefs.TryGetValue(currentTypeName, out var cType)
				&& cType is MlirStructType cStruct) {
				var field = cStruct.GetField(segments[si]);
				if (field != null) {
					fieldOffset = field.Offset;
					currentTypeName = field.Type is MlirStructType fst ? fst.Name : null;
				}
			}
			var nextVar = $"__cleanup_nav_{MlirContext.Current.NextId()}";
			var nextPtr = EmitStructFieldLoad(block, currentVar, fieldOffset, MlirType.I64, varTypes);
			EmitStore(block, nextPtr, nextVar, varTypes);
			currentVar = nextVar;
		}

		// currentVar now holds the __ManagedMemory heap pointer
		var capacity = (StdI64)EmitStructFieldLoad(block, currentVar, ManagedFieldCapacity, MlirType.I64, varTypes);
		var buffer = (StdI64)EmitStructFieldLoad(block, currentVar, ManagedFieldBuffer, MlirType.I64, varTypes);

		if (elementFieldInfo != null && elementFieldInfo.Count > 0) {
			EmitTrackCleanup(block, varName);

			var length = (StdI64)EmitStructFieldLoad(block, currentVar, ManagedFieldLength, MlirType.I64, varTypes);
			var elemSize = (StdI64)EmitStructFieldLoad(block, currentVar, ManagedFieldElementSize, MlirType.I64, varTypes);

			// Build the managed field offsets array in rdata
			var offsetsLabel = $"__managed_offsets_{MlirContext.Current.NextId()}";
			var offsetBytes = new byte[elementFieldInfo.Count * 8];
			for (int i = 0; i < elementFieldInfo.Count; i++) {
				BitConverter.TryWriteBytes(offsetBytes.AsSpan(i * 8), (long)elementFieldInfo[i].offset);
			}
			_resultModule!.RdataEntries.Add((offsetsLabel, offsetBytes, 8));

			var offsetsLea = new StdLeaRdataOp(offsetsLabel);
			block.AddOp(offsetsLea);
			var offsetsPtr = new StdPtrToI64Op(offsetsLea.Result);
			block.AddOp(offsetsPtr);
			var numFields = new StdConstI64Op(elementFieldInfo.Count);
			block.AddOp(numFields);

			// Save values to stack before the call (runtime calls clobber registers)
			var bufSave = $"__cleanup_buf_{MlirContext.Current.NextId()}";
			EmitStore(block, buffer, bufSave, varTypes);
			var capSave = $"__cleanup_cap_{MlirContext.Current.NextId()}";
			EmitStore(block, capacity, capSave, varTypes);

			block.AddOp(new StdCallRuntimeOp("maxon_cleanup_array_elements",
			  [buffer, length, elemSize, offsetsPtr.Result, numFields.Result]));

			// Reload buffer and capacity after the call, then just do the DECREF/FREE part
			buffer = (StdI64)EmitLoad(block, bufSave, varTypes);
			capacity = (StdI64)EmitLoad(block, capSave, varTypes);

			// Call maxon_cleanup_managed_nocleanup to do DECREF/FREE without the CLEANUP tag
			var (tagPtr, tagLen) = EmitTrackingTagLoad(block, varName);
			block.AddOp(new StdCallRuntimeOp("maxon_cleanup_managed_free", [capacity, buffer, tagPtr, tagLen]));
		} else {
			var (tagPtr, tagLen) = EmitTrackingTagLoad(block, varName);
			block.AddOp(new StdCallRuntimeOp("maxon_cleanup_managed", [capacity, buffer, tagPtr, tagLen]));
		}
	}

	/// <summary>
	/// Check if a method call name indicates mutation of the self struct.
	/// </summary>
	private static bool IsMutatingMethodCall(string callee) {
		// Method calls are like "IntArray.set", "StringArray.push", etc.
		var dot = callee.LastIndexOf('.');
		if (dot < 0) return false;
		var method = callee[(dot + 1)..];
		return method is "set" or "resize" or "push" or "shift" or "remove" or "pop" or "insert" or "concat";
	}
}
