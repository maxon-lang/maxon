using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;
using Rt = MaxonSharp.Compiler.Ir.Runtime.RuntimeEmitter;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
	// ---- Static (immortal) literal records --------------------------------------------------
	// A string/byte/char literal SITE that LiteralCoverageAnalysisPass proved never-mutated is
	// lowered not to a per-evaluation `mm_alloc` of a managed record, but to a reference to ONE
	// shared, immortal record living in .data (a 32-byte MM header + the record fields). The
	// records are interned by (value, typeName) so identical literals share a single blob, and
	// materialized once at __module_init (only the buffer pointer, an ASLR-relocated data->data
	// pointer, cannot be baked into .data and is stored there at startup). See the design in the
	// module-scope materialization helper below.

	// Set of static-eligible literal result ids for the module being lowered (from the module's
	// StaticEligibleLiteralIds; null => treat every literal as non-eligible / heap-allocated).
	[ThreadStatic] private static HashSet<int>? _staticEligibleLiterals;
	// Interns a shared record by (value, typeName) -> its .data global label.
	[ThreadStatic] private static Dictionary<(string Value, string TypeName), string>? _staticRecordLabels;
	// The records to materialize at __module_init (order preserved for stable codegen).
	[ThreadStatic] private static List<StaticLiteralRecord>? _staticLiteralRecords;
	[ThreadStatic] private static int _nextStaticLiteralId;
	// Maps a static-eligible literal's RESULT id -> its .data global label, so a managed-element
	// array literal (`["a","b"]`) can reference its elements' static records when it too is made
	// static (3c). Populated when a string/char/array literal is lowered to a static record.
	[ThreadStatic] private static Dictionary<int, string>? _staticLiteralLabelByResultId;

	// One immortal literal record's compile-time-known contents. Written into its .data blob at
	// __module_init (see EmitStaticRecordInit). ElementSize is 1 for a string/byte/char, the element
	// width for a constant array (8/4/2/1, or 0 = bit-packed bool). The BUFFER comes from one of three
	// places, and RdataLabel/ElementLabels say which: interned .rdata bytes (RdataLabel), an INLINE
	// pointer table in this same blob filled at init with the elements' own static records
	// (ElementLabels, a managed-element array), or NOWHERE AT ALL — an empty container has no elements,
	// so both are null and `buffer` stays 0, exactly as its factory would have left it.
	private sealed record StaticLiteralRecord(
		string GlobalLabel, string? RdataLabel, int RecordSize, int AllocSize,
		int TagIndex, int Length, long Capacity, int ElementSize, bool IsString, bool SingleByteGraphemes,
		string[]? ElementLabels = null);

	/// Reset the static-literal state for a fresh module lowering, seeding the eligibility set
	/// computed by LiteralCoverageAnalysisPass (a sound lower bound; null when the pass did not run).
	private static void ResetStaticLiteralState(HashSet<int>? eligible) {
		_staticEligibleLiterals = eligible;
		_staticRecordLabels = [];
		_staticLiteralRecords = [];
		_staticLiteralLabelByResultId = [];
		_nextStaticLiteralId = 0;
	}

	private static bool IsStaticEligibleLiteral(int resultId) =>
		_staticEligibleLiterals != null && _staticEligibleLiterals.Contains(resultId);

	/// Intern a literal's encoded bytes into .rodata (NUL-terminated), returning the label to
	/// address them by and the byte length. Deduplicated by value via _rdataStringCache, exactly
	/// as the emitting path was — factored out so the static-literal record materialization can
	/// reference the SAME bytes by label without also emitting a per-site LEA.
	private static (string Label, int ByteLen) InternRdataLiteral(
	  string value, string rdataLabel, IrModule<StandardOp> result, System.Text.Encoding? encoding = null) {
		var bytes = (encoding ?? System.Text.Encoding.UTF8).GetBytes(value);
		if (_rdataStringCache!.TryGetValue(value, out var existingLabel)) {
			return (existingLabel, bytes.Length);
		}
		var nullTerminated = new byte[bytes.Length + 1];
		Array.Copy(bytes, nullTerminated, bytes.Length);
		result.RdataEntries.Add((rdataLabel, nullTerminated, 1));
		_rdataStringCache[value] = rdataLabel;
		return (rdataLabel, bytes.Length);
	}

	/// <summary>
	/// Encode a string literal into rdata and emit LEA + PtrToI64 to get a buffer pointer and length.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length) EmitRdataLiteral(
	  string value,
	  string rdataLabel,
	  IrBlock<StandardOp> block,
	  IrModule<StandardOp> result,
	  System.Text.Encoding? encoding = null) {
		var (label, byteLen) = InternRdataLiteral(value, rdataLabel, result, encoding);

		var leaOp = new StdLeaRdataOp(label);
		block.AddOp(leaOp);
		var ptrOp = new StdPtrToI64Op(leaOp.Result);
		block.AddOp(ptrOp);

		var lenOp = new StdConstI64Op(byteLen);
		block.AddOp(lenOp);

		return (ptrOp.Result, lenOp.Result);
	}

	/// Envelope collapse: build a fused managed-wrapper record (String/Character/ByteArray) directly
	/// over an already-computed rdata buffer — ONE allocation, no separate __ManagedMemory. Writes
	/// buffer/length/capacity(MmCapacityRdata: read-only, destructor frees nothing)/
	/// element_size(1, one byte per element)/parent(0) inline at offsets 0..32. The record's own
	/// size follows its type (a String is 48 bytes for the trailing singleByteGraphemesFlag, others 40).
	private static StdHeapPtr EmitFusedRdataRecord(
	  StdI64 bufferPtr, StdI64 lengthVal, string allocTag, string tempName,
	  IrBlock<StandardOp> block, Dictionary<string, string> varTypes) {
		var outerPtr = (StdHeapPtr)EmitAlloc(block, FusedManagedRecordSize(allocTag), allocTag, scopeName: _currentFuncName);
		EmitStore(block, outerPtr, tempName, varTypes);
		var capConst = new StdConstI64Op(MmCapacityRdata);
		block.AddOp(capConst);
		var elemSizeConst = new StdConstI64Op(1);
		block.AddOp(elemSizeConst);
		var parentZero = new StdConstI64Op(0);
		block.AddOp(parentZero);
		EmitInitManagedMemory(block, tempName, bufferPtr, lengthVal, capConst.Result, elemSizeConst.Result, parentZero.Result, varTypes);
		return new StdHeapPtr(outerPtr.Id, outerPtr.TypeName, tempName);
	}

	private static StdHeapPtr EmitManagedMemoryLiteral(
	  string value,
	  int resultId,
	  string rdataPrefix,
	  string tempPrefix,
	  IrBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result,
	  VarRegistry temps,
	  string? allocTag = null,
	  string? inlineTarget = null) {
		var rdataLabel = $"__{rdataPrefix}_{NextRdataId()}";
		var (bufferPtr, lengthVal) = EmitRdataLiteral(value, rdataLabel, block, result);

		var tempName = inlineTarget
			?? temps.CreateTemp(tempPrefix, resultId, allocTag ?? "unknown", OwnershipFlags.None);
		return EmitFusedRdataRecord(bufferPtr, lengthVal, allocTag ?? "unknown", tempName, block, varTypes);
	}

	/// Intern the shared immortal record for (value, typeName), creating it — rdata bytes, a
	/// zero-initialized .data blob global, and a module-init materialization request — on first
	/// sight, and returning its .data global label. The record's byte length comes from the
	/// interned rdata; its size/tag from the type. The flag is only meaningful (and only baked)
	/// for a String.
	private static string InternStaticLiteralRecord(
	  string value, string typeName, bool isString, bool singleByteGraphemes,
	  string rdataPrefix, System.Text.Encoding? encoding, IrModule<StandardOp> result) {

		var key = (value, typeName);
		if (_staticRecordLabels!.TryGetValue(key, out var existing)) return existing;

		var rdataLabel = $"__{rdataPrefix}_{NextRdataId()}";
		var (label, byteLen) = InternRdataLiteral(value, rdataLabel, result, encoding);

		var globalLabel = $"__static_lit_{_nextStaticLiteralId++}";
		int recordSize = FusedManagedRecordSize(typeName);
		int allocSize = Rt.MmHeaderSize + recordSize;
		// A raw, zero-initialized .data blob (writable — its buffer field is fixed up at init).
		result.Globals.Add(new IrGlobal(globalLabel, new IrType("__StaticLiteralRecord", allocSize)));

		_staticLiteralRecords!.Add(new StaticLiteralRecord(
			globalLabel, label, recordSize, allocSize, EnsureTagIndex(typeName), byteLen,
			MmCapacityRdata, /*elementSize*/ 1, isString, singleByteGraphemes));
		_staticRecordLabels[key] = globalLabel;
		return globalLabel;
	}

	/// Materialize a static record's user pointer (= &blob + MmHeaderSize, past the header, exactly
	/// like an mm_alloc result) into a temp and return it as a heap pointer. The shared tail of every
	/// static-literal lowering — string/char (EmitStaticManagedLiteral), constant array
	/// (EmitStaticArrayLiteral), and managed-element array (EmitStaticManagedArrayLiteral).
	private static StdHeapPtr EmitStaticRecordUserPtr(
	  string globalLabel, string typeName, int resultId, string tempPrefix,
	  IrBlock<StandardOp> block, Dictionary<string, string> varTypes,
	  VarRegistry temps, string? inlineTarget) {

		var baseLea = new StdLeaGlobalOp(globalLabel);
		block.AddOp(baseLea);
		var baseI64 = new StdPtrToI64Op(baseLea.Result);
		block.AddOp(baseI64);
		var hdrOff = new StdConstI64Op(Rt.MmHeaderSize);
		block.AddOp(hdrOff);
		var userPtr = new StdAddI64Op(baseI64.Result, hdrOff.Result);
		block.AddOp(userPtr);

		var tempName = inlineTarget ?? temps.CreateTemp(tempPrefix, resultId, typeName, OwnershipFlags.None);
		EmitStore(block, userPtr.Result, tempName, varTypes);
		return new StdHeapPtr(userPtr.Result.Id, typeName, tempName);
	}

	/// Lower a static-eligible managed literal to a reference to its SHARED immortal record:
	/// ZERO per-evaluation allocation (the record's user pointer, materialized by
	/// EmitStaticRecordUserPtr — no allocation, exactly like an mm_alloc result).
	private static StdHeapPtr EmitStaticManagedLiteral(
	  string value, int resultId, string typeName, bool isString, bool singleByteGraphemes,
	  string rdataPrefix, string tempPrefix, System.Text.Encoding? encoding,
	  IrBlock<StandardOp> block, Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result, VarRegistry temps, string? inlineTarget) {

		var globalLabel = InternStaticLiteralRecord(value, typeName, isString, singleByteGraphemes, rdataPrefix, encoding, result);
		_staticLiteralLabelByResultId![resultId] = globalLabel;
		return EmitStaticRecordUserPtr(globalLabel, typeName, resultId, tempPrefix, block, varTypes, temps, inlineTarget);
	}

	/// Pack a constant array literal's element values into a little-endian rdata blob at the element
	/// width (or one bit per element for a bit-packed bool array), returning the bytes and their
	/// alignment. Shared by the per-evaluation lowering and the static-immortal-record lowering.
	private static (byte[] Bytes, int Alignment) PackConstArrayBytes(ConstantArrayLiteralInfo info) {
		if (info.IsBitPacked) {
			var bits = new byte[(info.Values.Length + 7) / 8];
			for (int i = 0; i < info.Values.Length; i++)
				if (info.Values[i] != 0) bits[i >> 3] |= (byte)(1 << (i & 7));
			return (bits, 1);
		}
		int elemSize = info.ElementSize;
		var bytes = new byte[info.Values.Length * elemSize];
		for (int i = 0; i < info.Values.Length; i++) {
			switch (elemSize) {
				case 1: bytes[i] = (byte)info.Values[i]; break;
				case 2: BitConverter.GetBytes((ushort)info.Values[i]).CopyTo(bytes, i * elemSize); break;
				case 4: BitConverter.GetBytes((int)info.Values[i]).CopyTo(bytes, i * elemSize); break;
				case 8: BitConverter.GetBytes(info.Values[i]).CopyTo(bytes, i * elemSize); break;
				default: throw new InvalidOperationException($"Unsupported constant array element size: {elemSize}");
			}
		}
		return (bytes, elemSize);
	}

	/// Lower a static-eligible CONSTANT array literal (`[1,2,3]`, `sha256KTable = [...]`) to a
	/// reference to its SHARED immortal record — zero per-evaluation allocation, the array analogue
	/// of EmitStaticManagedLiteral. The already-constant element bytes go to rdata (interned by
	/// content); `length` is the element COUNT and `element_size` the width (0 = bit-packed bool).
	/// Only primitive-element arrays reach here (their elements are compile-time constants); a
	/// managed-element array is never a ConstantArrayLiteral and stays on the heap path.
	private static StdHeapPtr EmitStaticArrayLiteral(
	  ConstantArrayLiteralInfo info, string typeName, int resultId,
	  IrBlock<StandardOp> block, Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result, VarRegistry temps, string? inlineTarget) {

		var (packed, alignment) = PackConstArrayBytes(info);
		// element_size == 0 is the bit-packed-bool sentinel (one bit per element); otherwise the width.
		int elementSize = info.IsBitPacked ? 0 : info.ElementSize;

		// Intern by (packed-byte signature, typeName) so identical constant arrays share one record.
		// The base64 signature cannot collide with a string literal's value at the same typeName —
		// array type names (`__Array_*`) are distinct from String/ByteArray/Character.
		var key = (System.Convert.ToBase64String(packed), typeName);
		if (!_staticRecordLabels!.TryGetValue(key, out var globalLabel)) {
			var rdataLabel = $"__sarr_{NextRdataId()}";
			result.RdataEntries.Add((rdataLabel, packed, alignment));
			globalLabel = $"__static_lit_{_nextStaticLiteralId++}";
			int recordSize = FusedManagedRecordSize(typeName);
			int allocSize = Rt.MmHeaderSize + recordSize;
			result.Globals.Add(new IrGlobal(globalLabel, new IrType("__StaticLiteralRecord", allocSize)));
			_staticLiteralRecords!.Add(new StaticLiteralRecord(
				globalLabel, rdataLabel, recordSize, allocSize, EnsureTagIndex(typeName),
				info.Values.Length, MmCapacityRdata, elementSize, /*isString*/ false, /*singleByteGraphemes*/ false));
			_staticRecordLabels[key] = globalLabel;
		}

		return EmitStaticRecordUserPtr(globalLabel, typeName, resultId, "sarr", block, varTypes, temps, inlineTarget);
	}

	// A managed element is an 8-byte refcounted heap pointer (matches RuntimeEmitter's
	// MmemManagedElementSize) — the stride of a fused array's inline pointer table.
	private const int StaticManagedElementSize = 8;

	/// Lower a static-eligible MANAGED-ELEMENT array literal (`["a","b"]`, every element a static
	/// string/char literal) to a reference to its shared immortal record — zero allocations. The
	/// record is a .data blob: the 32-byte header + the 40-byte array record + an inline N*8 pointer
	/// table; at __module_init the table slots are filled with the elements' static-record user
	/// pointers. The elements are themselves immortal, so nothing here is ever allocated or freed.
	private static StdHeapPtr EmitStaticManagedArrayLiteral(
	  string[] elementLabels, string typeName, int resultId,
	  IrBlock<StandardOp> block, Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result, VarRegistry temps, string? inlineTarget) {

		// Intern by (element-label signature, typeName): arrays of the same literals share one record.
		var key = (string.Join(",", elementLabels), typeName);
		if (!_staticRecordLabels!.TryGetValue(key, out var globalLabel)) {
			globalLabel = $"__static_lit_{_nextStaticLiteralId++}";
			int recordSize = FusedManagedRecordSize(typeName);   // 40 for an Array
			int allocSize = Rt.MmHeaderSize + recordSize + elementLabels.Length * StaticManagedElementSize;
			result.Globals.Add(new IrGlobal(globalLabel, new IrType("__StaticLiteralRecord", allocSize)));
			_staticLiteralRecords!.Add(new StaticLiteralRecord(
				globalLabel, /*rdataLabel*/ null, recordSize, allocSize, EnsureTagIndex(typeName),
				elementLabels.Length, /*capacity*/ elementLabels.Length, StaticManagedElementSize,
				/*isString*/ false, /*singleByteGraphemes*/ false, elementLabels));
			_staticRecordLabels[key] = globalLabel;
		}
		_staticLiteralLabelByResultId![resultId] = globalLabel;
		return EmitStaticRecordUserPtr(globalLabel, typeName, resultId, "smarr", block, varTypes, temps, inlineTarget);
	}

	/// Lower a call to a CONSTANT EMPTY container factory (`IntArray.create()`) whose result the
	/// escape analysis proved is never written through: no call, no allocation — just a reference to
	/// the SHARED immortal record for that (type, element width). The record is byte-identical to
	/// what the factory would have built (buffer 0, length 0, capacity 0, element_size, parent 0), so
	/// every READ answers exactly as before; only a WRITE could tell the difference, and a site that
	/// writes is absent from the eligible set and still calls the factory.
	///
	/// Interned by (element width, typeName) so two empty arrays of the same element type ARE the
	/// same value — the reason `c is d` is true, exactly as `"" is ""` is. The `empty:` prefix cannot
	/// collide with the other two intern keys at the same typeName: a string literal's key is its own
	/// text (and its typeName is String/Character/a ByteArray, never an array type), and a constant
	/// array's key is base64, which has no `:`.
	private static StdHeapPtr EmitStaticEmptyContainer(
	  ConstantEmptyContainerInfo info, int resultId,
	  IrBlock<StandardOp> block, Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result, VarRegistry temps) {

		var key = ($"empty:{info.ElementSize}", info.TypeName);
		if (!_staticRecordLabels!.TryGetValue(key, out var globalLabel)) {
			globalLabel = $"__static_lit_{_nextStaticLiteralId++}";
			int recordSize = FusedManagedRecordSize(info.TypeName);
			int allocSize = Rt.MmHeaderSize + recordSize;
			result.Globals.Add(new IrGlobal(globalLabel, new IrType("__StaticLiteralRecord", allocSize)));
			// Capacity 0, not MmCapacityRdata: `capacity()` is a READ a program can make, and an empty
			// container built by the factory answers 0. The sentinel is for a record borrowing bytes it
			// must not free — this one borrows nothing, and never reaches a free either way (immortal).
			_staticLiteralRecords!.Add(new StaticLiteralRecord(
				globalLabel, /*rdataLabel*/ null, recordSize, allocSize, EnsureTagIndex(info.TypeName),
				/*length*/ 0, /*capacity*/ 0, info.ElementSize, /*isString*/ false, /*singleByteGraphemes*/ false));
			_staticRecordLabels[key] = globalLabel;
		}
		// Deliberately NOT recorded in _staticLiteralLabelByResultId: that map exists so a
		// managed-element array literal can point its inline table at its elements' static records,
		// and an element written `[X.create(), X.create()]` reaches the block as a call result rather
		// than a literal. Such an array falls back to the heap path, which is the correct answer for
		// it today; enabling the table would be a separate site with its own cases to pin.
		return EmitStaticRecordUserPtr(globalLabel, info.TypeName, resultId, "sempty", block, varTypes, temps, inlineTarget: null);
	}

	/// Emit the MATERIALISE the escape analysis planned for the op that follows: if
	/// <paramref name="point"/>'s binding still holds the SHARED immortal empty record, rebind it to a
	/// private one, so the write about to happen lands on a record that binding owns.
	///
	/// This is the compiler emitting the IR maxon-shv2 writes by hand 75 times — `x = materializedX(x)`
	/// before a write — and it is what lets `var s = create(); if rare: s.push(v)` keep the shared
	/// record on the common path. The escape analysis is flow-INSENSITIVE, so without this the one
	/// reachable `push` costs every call of that function an allocation it never uses.
	///
	/// The test is the record's own immortal sentinel, the same one mm_incref and mm_decref make
	/// (RuntimeEmitter.EmitImmortalReturnGuard) — not a comparison against the static record's address,
	/// which would need that record to have been interned already, and it may be lowered later.
	/// What gets built is the factory's own constant, so the private record is byte-identical to what
	/// the factory would have returned, at rc=1: the binding's existing scope-end decref then balances
	/// it exactly as it balances a real `create()`.
	private static void EmitMaterialise(
	  MaterialisePoint point, IrFunction<StandardOp> func, ref IrBlock<StandardOp> block,
	  Dictionary<string, string> varTypes) {

		var uid = IrContext.Current.NextId();
		var mintLabel = $"__materialise_{uid}";
		var contLabel = $"__materialised_{uid}";

		var current = (StdI64)EmitLoad(block, point.Binding, varTypes);
		var refcount = new StdLoadIndirectOp(current, Rt.MmOffRefcount, IrType.I64);
		block.AddOp(refcount);
		var immortal = new StdConstI64Op(Rt.MmImmortalRefcount);
		block.AddOp(immortal);
		var isShared = new StdCmpI64Op("eq", (StdI64)refcount.Result, immortal.Result);
		block.AddOp(isShared);
		block.AddOp(new StdCondBrOp(isShared.Result, mintLabel, contLabel));

		var mint = func.Body.AddBlock(mintLabel);
		var fresh = EmitAlloc(mint, FusedManagedRecordSize(point.Record.TypeName), point.Record.TypeName,
		  scopeName: _currentFuncName);
		void StoreField(long value, int offset) {
			var constant = new StdConstI64Op(value);
			mint.AddOp(constant);
			mint.AddOp(new StdStoreIndirectOp(constant.Result, fresh, offset, IrType.I64));
		}
		// Empty is empty: nothing to point at, nothing in it, nothing owned, no parent — only the
		// element width, which is what tells the two records apart.
		StoreField(0, ManagedFieldBuffer);
		StoreField(0, ManagedFieldLength);
		StoreField(0, ManagedFieldCapacity);
		StoreField(point.Record.ElementSize, ManagedFieldElementSize);
		StoreField(0, ManagedFieldParentPtr);
		EmitIncrefValue(mint, fresh, scopeName: _currentFuncName);
		// The record being overwritten is the immortal one — that is this block's entry condition —
		// so there is nothing to release before the store.
		EmitStore(mint, fresh, point.Binding, varTypes);
		mint.AddOp(new StdBrOp(contLabel));

		block = func.Body.AddBlock(contLabel);
	}

	/// For a managed-element array literal, return its elements' static-record labels in order if
	/// EVERY element is a static string/char literal, else null (so the caller falls back to the heap
	/// path). Elements reach the block as `maxon.assign %elem {var = <ArrayLiteralTag>.<i>}`; an
	/// element is static iff its result id was recorded by a static-literal lowering that ran earlier
	/// in this block. Any non-literal or non-eligible element leaves a hole and disqualifies the array.
	private static string[]? CollectStaticElementLabels(MaxonStructLiteralOp arr, IrBlock<MaxonOp> block) {
		var tagPrefix = arr.ArrayLiteralTag! + ".";
		var labels = new string[arr.ArrayLiteralCount];
		var filled = new bool[arr.ArrayLiteralCount];
		foreach (var op in block.Operations) {
			if (op is not MaxonAssignOp a || !a.VarName.StartsWith(tagPrefix)) continue;
			if (!int.TryParse(a.VarName.AsSpan(tagPrefix.Length), out var idx) || idx < 0 || idx >= labels.Length)
				continue;
			if (_staticLiteralLabelByResultId!.TryGetValue(a.Value.Id, out var lbl)) {
				labels[idx] = lbl;
				filled[idx] = true;
			}
		}
		for (int i = 0; i < filled.Length; i++)
			if (!filled[i]) return null;
		return labels;
	}

	/// Materialize every interned static literal record into __module_init: for each record,
	/// write its MM header (alloc_size, packed_id, destructor=0, refcount=IMMORTAL) and its
	/// record fields (buffer, length, capacity, element_size, parent=0, [flag])
	/// into its .data blob. Only the buffer pointer genuinely REQUIRES runtime materialization
	/// (a data->data pointer the loader relocates under ASLR); the constants are written the same
	/// way for uniformity and are cheap (one-time, at startup). Prepended to __module_init so the
	/// records are live before any other init code or main runs. Creates __module_init if absent.
	private static void MaterializeStaticLiteralRecords(IrModule<StandardOp> result) {
		if (_staticLiteralRecords == null || _staticLiteralRecords.Count == 0) return;

		var initFunc = result.Functions.FirstOrDefault(f => f.Name == "__module_init");
		bool created = initFunc == null;
		if (initFunc == null) {
			initFunc = new IrFunction<StandardOp>("__module_init", [], [], null, null);
			initFunc.Body.AddBlock("entry");
		}
		var entry = initFunc.Body.Blocks[0];

		var initOps = new List<StandardOp>();
		foreach (var rec in _staticLiteralRecords) {
			EmitStaticRecordInit(rec, initOps);
		}
		entry.Operations.InsertRange(0, initOps);

		if (created) {
			entry.AddOp(new StdReturnOp(null));
			result.AddFunction(initFunc);
		}
	}

	/// Emit the store sequence that fills one static record's .data blob. Offsets are relative to
	/// the blob base (= raw allocation pointer); the header sits at [base..base+MmHeaderSize) and
	/// the record fields at [base+MmHeaderSize..]. Header offsets are written as MmHeaderSize +
	/// the NEGATIVE MmOff* (which are user-pointer-relative), so they resolve to 0/8/16/24.
	private static void EmitStaticRecordInit(StaticLiteralRecord rec, List<StandardOp> ops) {
		var baseLea = new StdLeaGlobalOp(rec.GlobalLabel);
		ops.Add(baseLea);
		var baseI64 = new StdPtrToI64Op(baseLea.Result);
		ops.Add(baseI64);

		void Store(long value, int offset) {
			var c = new StdConstI64Op(value);
			ops.Add(c);
			ops.Add(new StdStoreIndirectOp(c.Result, baseI64.Result, offset, IrType.I64));
		}

		// 32-byte MM header.
		Store(rec.AllocSize, Rt.MmHeaderSize + Rt.MmOffAllocSize);       // -> 0
		Store(rec.TagIndex, Rt.MmHeaderSize + Rt.MmOffPackedId);         // -> 8  (alloc_id 0 | tag)
		Store(0, Rt.MmHeaderSize + Rt.MmOffDestructor);                  // -> 16 (never runs)
		Store(Rt.MmImmortalRefcount, Rt.MmHeaderSize + Rt.MmOffRefcount);// -> 24 (immortal sentinel)

		// Record fields (base + MmHeaderSize + ManagedField*).
		int rec0 = Rt.MmHeaderSize;

		if (rec.ElementLabels != null) {
			// Managed-element array (`["a","b"]`): the buffer is an INLINE pointer table right after
			// the record fields, in this same blob. Point `buffer` at it, and fill each slot with an
			// element's static-record user pointer (&element_blob + MmHeaderSize). The array is
			// immortal, so its destructor never runs — capacity/parent are only what reads need.
			int tableOff = rec0 + rec.RecordSize;
			var tableOffConst = new StdConstI64Op(tableOff);
			ops.Add(tableOffConst);
			var tableAddr = new StdAddI64Op(baseI64.Result, tableOffConst.Result);
			ops.Add(tableAddr);
			ops.Add(new StdStoreIndirectOp(tableAddr.Result, baseI64.Result, rec0 + ManagedFieldBuffer, IrType.I64));
			Store(rec.Length, rec0 + ManagedFieldLength);
			Store(rec.Capacity, rec0 + ManagedFieldCapacity);        // owned element count (>= length)
			Store(rec.ElementSize, rec0 + ManagedFieldElementSize);  // 8 — managed element pointers
			Store(MmParentInline, rec0 + ManagedFieldParentPtr);     // inline-buffer sentinel
			for (int i = 0; i < rec.ElementLabels.Length; i++) {
				var elemLea = new StdLeaGlobalOp(rec.ElementLabels[i]);
				ops.Add(elemLea);
				var elemI64 = new StdPtrToI64Op(elemLea.Result);
				ops.Add(elemI64);
				var elemHdr = new StdConstI64Op(Rt.MmHeaderSize);
				ops.Add(elemHdr);
				var elemUserPtr = new StdAddI64Op(elemI64.Result, elemHdr.Result);
				ops.Add(elemUserPtr);
				ops.Add(new StdStoreIndirectOp(elemUserPtr.Result, baseI64.Result, tableOff + i * 8, IrType.I64));
			}
			return;
		}

		// An EMPTY container has no elements and therefore no bytes to point at: `buffer` stays 0,
		// which is what its factory's own `Self{}` writes. Only a record WITH contents has an rdata blob.
		if (rec.RdataLabel != null) {
			var bufLea = new StdLeaRdataOp(rec.RdataLabel);
			ops.Add(bufLea);
			var bufI64 = new StdPtrToI64Op(bufLea.Result);
			ops.Add(bufI64);
			ops.Add(new StdStoreIndirectOp(bufI64.Result, baseI64.Result, rec0 + ManagedFieldBuffer, IrType.I64));
		} else {
			Store(0, rec0 + ManagedFieldBuffer);
		}
		Store(rec.Length, rec0 + ManagedFieldLength);
		Store(rec.Capacity, rec0 + ManagedFieldCapacity);
		Store(rec.ElementSize, rec0 + ManagedFieldElementSize); // 1 for a string; the element width for an array (0 = bit-packed bool)
		Store(0, rec0 + ManagedFieldParentPtr);
		if (rec.IsString) {
			Store(rec.SingleByteGraphemes ? 1 : 0, rec0 + StringFieldSingleByteGraphemes);
		}
	}

	private static void LowerStringLiteral(
	  MaxonStringLiteralOp op,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result,
	  VarRegistry temps,
	  string? inlineTarget = null) {

		// Classify at compile time (used by both the static and heap paths). CR is excluded as
		// well as the non-ASCII bytes: `String.singleByteGraphemesFlag` asserts one byte per
		// GRAPHEME, and UAX #29 GB3 joins CR to a following LF, so an all-ASCII literal holding a
		// CR is not one byte per grapheme. A literal is the one place this is free to establish.
		bool singleByteGraphemes = op.Value.All(c => c < 128 && c != '\r');

		// Static-eligible: share one immortal .data record — no allocation. The flag is baked
		// into that record at init, so nothing to store here.
		if (IsStaticEligibleLiteral(op.Result.Id)) {
			valueMap[op.Result] = EmitStaticManagedLiteral(
				op.Value, op.Result.Id, "String", isString: true, singleByteGraphemes, "str", "strtmp", null,
				block, varTypes, result, temps, inlineTarget);
			return;
		}

		var heapPtr = EmitManagedMemoryLiteral(op.Value, op.Result.Id, "str", "strtmp", block, varTypes, result, temps, "String", inlineTarget);
		valueMap[op.Result] = heapPtr;

		// Store the flag
		var flagConst = new StdConstI64Op(singleByteGraphemes ? 1 : 0);
		block.AddOp(flagConst);
		EmitStructFieldStore(block, flagConst.Result, heapPtr.VarName!, StringFieldSingleByteGraphemes, IrType.I64, varTypes);
	}

	private static void LowerByteStringLiteral(
	  MaxonByteStringLiteralOp op,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result,
	  VarRegistry temps,
	  string? inlineTarget = null) {
		// ByteArray layout: managed at offset 0 (single field). The literal's
		// static element type is `int(0 to u8.max)`; OptimalType narrows that to
		// U8, so `__managed_mem_get`/`__managed_mem_set` emit 1-byte loads/stores
		// that match the 1-byte rdata storage written below.

		// Static-eligible: share one immortal .data record — no allocation. b"..." bytes are
		// Latin1-encoded (one byte per element), matching the heap path below.
		if (IsStaticEligibleLiteral(op.Result.Id)) {
			valueMap[op.Result] = EmitStaticManagedLiteral(
				op.Value, op.Result.Id, op.ArrayTypeName, isString: false, singleByteGraphemes: false, "bstr", "bstrtmp",
				System.Text.Encoding.Latin1, block, varTypes, result, temps, inlineTarget);
			return;
		}

		var rdataLabel = $"__bstr_{NextRdataId()}";
		var (bufferPtr, lengthVal) = EmitRdataLiteral(op.Value, rdataLabel, block, result,
		  System.Text.Encoding.Latin1);

		// Envelope collapse: the ByteArray IS its own __ManagedMemory over the rdata bytes — ONE
		// allocation, element_size=1 (Byte). Its `Byte` elements are raw bytes, so the fused
		// destructor's managed-element cleanup is a no-op (correct: nothing to decref).
		var tempName = inlineTarget
			?? temps.CreateTemp("bstrtmp", op.Result.Id, op.ArrayTypeName, OwnershipFlags.None);
		valueMap[op.Result] = EmitFusedRdataRecord(bufferPtr, lengthVal, op.ArrayTypeName, tempName, block, varTypes);
	}

	private static void LowerCharLiteral(
	  MaxonCharLiteralOp op,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result,
	  VarRegistry temps,
	  string? inlineTarget = null) {

		// Static-eligible: share one immortal .data record — no allocation.
		if (IsStaticEligibleLiteral(op.Result.Id)) {
			valueMap[op.Result] = EmitStaticManagedLiteral(
				op.Value, op.Result.Id, "Character", isString: false, singleByteGraphemes: false, "chr", "chrtmp", null,
				block, varTypes, result, temps, inlineTarget);
			return;
		}

		var heapPtr = EmitManagedMemoryLiteral(op.Value, op.Result.Id, "chr", "chrtmp", block, varTypes, result, temps, "Character", inlineTarget);
		valueMap[op.Result] = heapPtr;
	}

	private static void LowerStringInterp(
	  MaxonStringInterpOp op,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result,
	  VarRegistry temps,
	  string? inlineTarget = null) {

		// Byte-fusion fast path: a lone numeric/bool part (`n.toString()` == "{n}") writes its decimal
		// text STRAIGHT into the String record's inline buffer — one allocation, no digit scratch.
		if (op.Parts.Count == 1) {
			var (singleIsLit, _, singleExpr, singleFmt, singleOpt) = op.Parts[0];
			var maxBytes = SingleNumericToStringMaxBytes(singleExpr, singleIsLit, valueMap);
			if (maxBytes != null) {
				var toStrTemp = inlineTarget
					?? temps.CreateTemp("interptmp", op.Result.Id, "String", OwnershipFlags.None);
				// ONE allocation: record (StringStructSize) + inline digit buffer (maxBytes) + NUL.
				var toStrSelf = (StdHeapPtr)EmitAlloc(block, StringStructSize + maxBytes.Value + 1, "String", scopeName: _currentFuncName);
				EmitStore(block, toStrSelf, toStrTemp, varTypes);
				var toStrBuf = EmitInlineBufferPtr(block, toStrTemp, StringStructSize, varTypes);
				var toStrLen = EmitSingleNumericToStringInto(singleExpr!, singleFmt, singleOpt, toStrBuf, block, valueMap, varTypes);
				// The runtime call clobbers registers, so recompute buffer = self + StringStructSize.
				var toStrBufR = EmitInlineBufferPtr(block, toStrTemp, StringStructSize, varTypes);
				// NUL terminator at buffer[len]
				var nulAddr = new StdAddI64Op(toStrBufR, toStrLen);
				block.AddOp(nulAddr);
				var nulZero = new StdConstI64Op(0);
				block.AddOp(nulZero);
				block.AddOp(new StdStoreIndirectOp(nulZero.Result, nulAddr.Result, 0, IrType.I8));
				// Inline managed fields: capacity == length (exact), parent_ptr = MmParentInline.
				var elemOne = new StdConstI64Op(1);
				block.AddOp(elemOne);
				var parentInline = new StdConstI64Op(MmParentInline);
				block.AddOp(parentInline);
				EmitInitManagedMemory(block, toStrTemp, toStrBufR, toStrLen, toStrLen, elemOne.Result, parentInline.Result, varTypes);
				var toStrAscii = new StdConstI64Op(0);
				block.AddOp(toStrAscii);
				EmitStructFieldStore(block, toStrAscii.Result, toStrTemp, StringFieldSingleByteGraphemes, IrType.I64, varTypes);
				valueMap[op.Result] = new StdHeapPtr(toStrSelf.Id, toStrSelf.TypeName, toStrTemp);
				return;
			}
		}

		var (partInfos, interpTempBufVars) = EmitInterpParts(op.Parts, "interp", block, valueMap, varTypes, result);

		if (partInfos.Count == 0) {
			var heapPtr = EmitManagedMemoryLiteral("", op.Result.Id, "interp", "interptmp", block, varTypes, result, temps, "String", inlineTarget);
			valueMap[op.Result] = heapPtr;
			var iaConst = new StdConstI64Op(0);
			block.AddOp(iaConst);
			EmitStructFieldStore(block, iaConst.Result, heapPtr.VarName!, StringFieldSingleByteGraphemes, IrType.I64, varTypes);
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

		// Byte-fusion: ONE String allocation holds BOTH the record AND its UTF-8 bytes. The record
		// is StringStructSize bytes; the buffer lives INLINE right after it (buffer = self +
		// StringStructSize) in the SAME allocation, so an owned interpolation result is a single
		// mm_alloc rather than a record plus a separate raw buffer. parent_ptr = MmParentInline
		// marks it; the bytes die with the record's slot (no buffer to free), and a later append
		// DETACHES to an external buffer. There is no cap on a fused string (built once, read many).
		var tempName2 = inlineTarget
			?? temps.CreateTemp("interptmp", op.Result.Id, "String", OwnershipFlags.None);

		// fusedSize = StringStructSize + totalLen + 1 (trailing NUL)
		var recPlusNulOp = new StdConstI64Op(StringStructSize + 1);
		block.AddOp(recPlusNulOp);
		var fusedSize = new StdAddI64Op(totalLen, recPlusNulOp.Result);
		block.AddOp(fusedSize);
		var interpOuterPtr = (StdHeapPtr)EmitAlloc(block, fusedSize.Result, "String", scopeName: _currentFuncName);
		EmitStore(block, interpOuterPtr, tempName2, varTypes);

		// buffer = self + StringStructSize (the inline region)
		var inlineBuf = EmitInlineBufferPtr(block, tempName2, StringStructSize, varTypes);

		// Store all values to stack variables since rep movsb clobbers RSI, RDI, RCX
		var interpOffsetVar = $"__interp_offset_{op.Result.Id}";
		var interpBufVar = $"__interp_buf_{op.Result.Id}";
		var interpTotalLenVar = $"__interp_totallen_{op.Result.Id}";
		var zeroOp = new StdConstI64Op(0);
		block.AddOp(zeroOp);
		EmitStore(block, zeroOp.Result, interpOffsetVar, varTypes);
		EmitStore(block, inlineBuf, interpBufVar, varTypes);
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
			block.AddOp(new StdStoreIndirectOp(ntZero.Result, ntAddr.Result, 0, IrType.I8));
		}

		// Free intermediate toString buffers now that contents are copied
		foreach (var bufVar in interpTempBufVars) {
			var bufPtr = (StdI64)EmitLoad(block, bufVar, varTypes);
			EmitRawFree(block, bufPtr);
		}

		// Write the managed fields inline into the String record. The buffer is INLINE and owned, so
		// capacity == length (exact) and parent_ptr = MmParentInline: the destructor skips the raw
		// free (the bytes die with the record's slot), and a later append detaches to an external buffer.
		var finalBuf = (StdI64)EmitLoad(block, interpBufVar, varTypes);
		var finalLen = (StdI64)EmitLoad(block, interpTotalLenVar, varTypes);
		var elemSizeConst2 = new StdConstI64Op(1);
		block.AddOp(elemSizeConst2);
		var interpParentInline = new StdConstI64Op(MmParentInline);
		block.AddOp(interpParentInline);
		EmitInitManagedMemory(block, tempName2, finalBuf, finalLen, finalLen, elemSizeConst2.Result, interpParentInline.Result, varTypes);

		// Store the flag = 0 (conservative default)
		var flagConst2 = new StdConstI64Op(0);
		block.AddOp(flagConst2);
		EmitStructFieldStore(block, flagConst2.Result, tempName2, StringFieldSingleByteGraphemes, IrType.I64, varTypes);

		valueMap[op.Result] = new StdHeapPtr(interpOuterPtr.Id, interpOuterPtr.TypeName, tempName2);
	}

	/// <summary>
	/// Processes interpolation parts into (buffer, length) pairs and tracks temporary buffers.
	/// </summary>
	private static (List<(StdI64 Buffer, StdI64 Length)> partInfos, List<string> tempBufVars) EmitInterpParts(
	  List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, IrType? OptimalType)> parts,
	  string rdataPrefix,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result) {
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
				// Dispatch on the VALUE KIND before the lowered representation. A union with any
				// payload-carrying case is heap-boxed, so it maps to a StdHeapPtr exactly as a String
				// does — but its record is [tag@0, payload@8...], not a __ManagedMemory. Testing the
				// representation first sent unions down the struct path, which reads offsets 0 and 8 as
				// (buffer, length): a payload-less case rendered "" (tag 0 as buffer, zeroed slot as
				// length) and a payload-carrying case copied from its tag as an address, faulting.
				if (exprValue is MaxonEnum enumValue) {
					AddEnumToStringResult(EmitEnumToString(enumValue, valueMap, block, varTypes, result));
				} else if (valueMap.TryGetValue(exprValue, out var exprStdVal) && exprStdVal is StdHeapPtr hp) {
					partInfos.Add(EmitStructInterpolation(hp.VarName!, block, varTypes));
				} else if (exprValue is MaxonInteger or MaxonByte or MaxonShort) {
					AddToStringResult(EmitIntegerToString(exprValue, FormatSpec, OptimalType, valueMap, block, varTypes));
				} else if (exprValue is MaxonBool) {
					AddToStringResult(EmitBoolToString((StdBool)valueMap[exprValue], block, varTypes));
				} else {
					// A FLOAT never reaches here: `Parser.RewriteFloatPartAsStdlibText` turned every float
					// part — including a float-backed enum's raw value — into a `__float_toString` call
					// while the Maxon IR was still being built, so it arrives as the StdHeapPtr of a
					// String. See that method for why the rewrite cannot live at this level.
					throw new InvalidOperationException(
					  $"String {rdataPrefix}: unsupported expression type {exprValue.GetType().Name} for value %{exprValue.Id}");
				}
			}
		}
		return (partInfos, tempBufVars);
	}

	/// Inline byte reservation for a lone numeric/bool interpolation part fused into a String record
	/// (`n.toString()` == "{n}"), or null when the part is not a plain numeric/bool — a String/struct
	/// (StdHeapPtr) or an enum goes through the general record-fusing path instead. Mirrors the byte
	/// budgets the corresponding EmitRuntimeToString call would have allocated for its scratch.
	private static int? SingleNumericToStringMaxBytes(
	  MaxonValue? exprValue, bool isLiteral,
	  Dictionary<MaxonValue, StdValue> valueMap) {
		if (isLiteral || exprValue == null) return null;
		// A FORMATTED integer is already a String by now (the parser rewrote it), so it is a
		// StdHeapPtr and the line above has declined it — no formatted budget is reachable here.
		if (valueMap.TryGetValue(exprValue, out var v) && v is StdHeapPtr) return null;
		return exprValue switch {
			MaxonInteger or MaxonByte or MaxonShort => DecimalToStringMaxBytes,
			MaxonBool => BoolToStringMaxBytes,
			_ => null,
		};
	}

	/// Converts a single numeric/bool interpolation expr straight into destBuffer (a String record's
	/// inline region) and returns its text length — the digit-buffer half of `n.toString()`'s single
	/// allocation. Mirrors the numeric dispatch in EmitInterpParts, but writes to a caller-owned
	/// buffer rather than a freshly-allocated scratch. Only reached for parts SingleNumericToStringMaxBytes accepts.
	private static StdI64 EmitSingleNumericToStringInto(
	  MaxonValue exprValue, string? formatSpec, IrType? optimalType, StdI64 destBuffer,
	  IrBlock<StandardOp> block, Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes) {
		if (exprValue is MaxonInteger or MaxonByte or MaxonShort) {
			return EmitIntegerToString(exprValue, formatSpec, optimalType, valueMap, block, varTypes, destBuffer).Length;
		}
		return EmitBoolToString((StdBool)valueMap[exprValue], block, varTypes, destBuffer).Length;
	}

	/// <summary>
	/// <summary>
	/// Allocates a buffer, calls a runtime conversion function, and returns (buffer, length).
	/// Used by EmitI64ToString, EmitU64ToString, and EmitBoolToString.
	/// Also returns the buffer variable name for cleanup after use.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitRuntimeToString(
	  StdValue value,
	  string runtimeFuncName,
	  int bufferSize,
	  IrBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  StdI64? destBuffer = null) {

		// Byte-fusion: when destBuffer is supplied the digits are written STRAIGHT into it (the
		// String record's own inline region), so `n.toString()` needs no scratch buffer at all.
		// The caller owns destBuffer and recomputes it after the call (registers are clobbered).
		if (destBuffer != null) {
			var lenInline = new StdI64(IrContext.Current.NextStdId());
			block.AddOp(new StdCallRuntimeOp(runtimeFuncName, [value, destBuffer], lenInline));
			return (destBuffer, lenInline, "");
		}

		var sizeOp = new StdConstI64Op(bufferSize);
		block.AddOp(sizeOp);
		var bufResult = EmitRawAlloc(block, sizeOp.Result, label: "toStr.buf", scopeName: _currentFuncName);

		// Store buffer pointer so it survives the runtime call
		var bufVarName = $"__tostr_buf_{bufResult.Id}";
		EmitStore(block, bufResult, bufVarName, varTypes);

		var lenResult = new StdI64(IrContext.Current.NextStdId());
		block.AddOp(new StdCallRuntimeOp(runtimeFuncName, [value, bufResult], lenResult));

		var finalBuf = (StdI64)EmitLoad(block, bufVarName, varTypes);
		return (finalBuf, lenResult, bufVarName);
	}

	// Max decimal-text byte budgets for each runtime conversion, reused as the inline reservation
	// when a `n.toString()` is fused into a String record (see TryEmitSingleNumericToStringInline).
	//
	// ⚠ ONE BUDGET SERVES BOTH DECIMAL ROUTINES, and it is one fact rather than two that happen to be
	// equal: `maxon_i64_to_string` and `maxon_u64_to_string` write at most 20 characters — 20 digits
	// (`18446744073709551615`) or 19 and a sign (`-9223372036854775808`) — plus a NUL, and BOTH
	// emitters fill the same buffer backwards from `buffer + 20` on both targets. Spelled twice, a
	// budget lowered on one copy would silently overrun a buffer the routine still fills to 20.
	private const int DecimalToStringMaxBytes = 21;
	private const int BoolToStringMaxBytes = 6;   // "false"

	/// The ONE lowering of an UNFORMATTED integer part: widen to i64, then the signed or the unsigned
	/// runtime conversion. The general interpolation path and the byte-fusion one used to carry a copy
	/// of the widening and the signedness test each, free to disagree.
	///
	/// A FORMATTED integer never arrives: `Parser.RewriteIntegerPartAsStdlibText` turned every part
	/// carrying a specifier into an `__int_toStringFormatted` call while the Maxon IR was still being
	/// built, so it reaches interpolation as the StdHeapPtr of a String. See that method for why the
	/// rewrite cannot live at this level, and why only the FORMATTED spelling moved.
	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitIntegerToString(
	  MaxonValue exprValue, string? formatSpec, IrType? optimalType,
	  Dictionary<MaxonValue, StdValue> valueMap, IrBlock<StandardOp> block,
	  Dictionary<string, string> varTypes, StdI64? destBuffer = null) {

		if (formatSpec != null)
			throw new InvalidOperationException(
			  $"String interpolation: integer %{exprValue.Id} still carries format spec '{formatSpec}' — the parser should have rewritten it to an __int_toStringFormatted call");

		var stdVal = valueMap[exprValue];
		// Widen narrower integer types to i64 for the runtime toString call.
		if (stdVal is StdU32 u32) stdVal = EnsureI64(new StdI32(u32.Id), block, signExtend: false);
		else if (stdVal is StdI32) stdVal = EnsureI64(stdVal, block, signExtend: true);

		return (optimalType?.IsUnsigned ?? false)
		  ? EmitU64ToString(stdVal, block, varTypes, destBuffer)
		  : EmitI64ToString(stdVal, block, varTypes, destBuffer);
	}

	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitI64ToString(
	  StdValue intValue, IrBlock<StandardOp> block, Dictionary<string, string> varTypes, StdI64? destBuffer = null) =>
	  EmitRuntimeToString(intValue, "maxon_i64_to_string", DecimalToStringMaxBytes, block, varTypes, destBuffer);

	private static (StdI64 Buffer, StdI64 Length, string BufVarName) EmitU64ToString(
	  StdValue intValue, IrBlock<StandardOp> block, Dictionary<string, string> varTypes, StdI64? destBuffer = null) =>
	  EmitRuntimeToString(intValue, "maxon_u64_to_string", DecimalToStringMaxBytes, block, varTypes, destBuffer);

	/// <summary>
	/// Handles interpolation of struct values. For String/Character types (which have buffer/length
	/// fields), reads those directly. For Stringable types, calls the toString() method and uses
	/// the returned String's buffer/length.
	/// </summary>
	private static (StdI64 Buffer, StdI64 Length) EmitStructInterpolation(
	  string managedVarName,
	  IrBlock<StandardOp> block,
	  Dictionary<string, string> varTypes) {

		// Envelope collapse: a String/Character IS its __ManagedMemory, so buffer and length sit
		// inline at offsets 0 and 8 of the value itself — read them directly, no pointer chase.
		var bufLoad = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldBuffer, IrType.I64, varTypes);
		var lenLoad = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
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
	  IrBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  IrModule<StandardOp> result) {

		if (!result.TypeDefs.TryGetValue(enumValue.TypeName, out var typeDef) || typeDef is not IrEnumType enumType) {
			throw new InvalidOperationException(
			  $"String interpolation: enum type '{enumValue.TypeName}' not found in type definitions");
		}

		var backingIrType = ResolveEnumBackingIrType(enumType);
		var stdValue = valueMap[enumValue];

		// A union with any payload-carrying case is heap-boxed, so its discriminant must be loaded
		// from the record's tag slot rather than read off the value. Such a union renders its case
		// name: scalar backing is only available to all-bare unions (so there is no raw value to
		// print here), and a struct backing is compile-time metadata, which the struct-backed branch
		// below also renders as the case name. The payload is deliberately not rendered — see the
		// "Union Types" section of specs/string-interpolation.md for why.
		if (stdValue is StdHeapPtr unionPtr) {
			var tag = EmitUnionTagLoad(unionPtr, block, varTypes);
			var rUnion = EmitEnumCaseNameToString(enumType, tag, block, result);
			return (rUnion.Buffer, rUnion.Length, null);
		}

		if (enumType.BackingType is IrStringBackingType or IrCharBackingType) {
			var r = EmitStringEnumToString(enumType, (StdI64)stdValue, block, result);
			return (r.Buffer, r.Length, null);
		}

		if (enumType.BackingType is IrStructBackingType) {
			// Struct-backed enums interpolate as their case name
			var r = EmitEnumCaseNameToString(enumType, (StdI64)stdValue, block, result);
			return (r.Buffer, r.Length, null);
		}

		// A FLOAT-backed enum never arrives: `Parser.RewriteFloatPartAsStdlibText` reads its raw value
		// and renders that through stdlib, so it reaches interpolation as a String. Saying so here keeps
		// it from silently falling into the integer arm below and casting an StdF64 to an StdI64.
		if (backingIrType == IrType.F64) {
			throw new InvalidOperationException(
			  $"String interpolation: float-backed enum '{enumType.Name}' should have been rewritten to a __float_toString call in the parser");
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
	  IrEnumType enumType,
	  StdI64 ordinalValue,
	  IrBlock<StandardOp> block,
	  IrModule<StandardOp> result) {

		var fallbackLabel = $"__enum_name_fallback_{NextRdataId()}";
		var (currentBuf, currentLen) = EmitRdataLiteral("?", fallbackLabel, block, result);

		foreach (var enumCase in enumType.Cases) {
			var caseLabel = $"__enum_name_{enumType.Name}_{enumCase.Name}_{NextRdataId()}";
			var (caseBuf, caseLen) = EmitRdataLiteral(enumCase.Name, caseLabel, block, result);

			// IrEnumCase.TagValue is the ONE source of the discriminant codegen actually stores
			// (raw value for int-backed, ordinal for everything else). Re-deriving it here would be
			// a second copy that silently mislabels every case the moment the two disagree.
			var caseConst = new StdConstI64Op(enumCase.TagValue);
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
	  IrEnumType enumType,
	  StdI64 ordinalValue,
	  IrBlock<StandardOp> block,
	  IrModule<StandardOp> result) {

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
	  IrBlock<StandardOp> block) {
		var lenCmp = new StdCmpI64Op("eq", inputLen, caseLen);
		block.AddOp(lenCmp);
		var memcmpResult = new StdI64(IrContext.Current.NextStdId());
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
	  bool isString, IrBlock<StandardOp> block,
	  Dictionary<string, string> varTypes,
	  string? allocTag = null) {
		int outerSize = isString ? StringStructSize : CharacterStructSize;
		// Envelope collapse: ONE allocation. The (buffer, length) here always name rdata
		// (enum case names / string-backed raw values), so capacity is MmCapacityRdata (never freed).
		var outerPtr = (StdHeapPtr)EmitAlloc(block, outerSize, allocTag, scopeName: _currentFuncName);
		EmitStore(block, outerPtr, tempName, varTypes);
		var capConst = new StdConstI64Op(MmCapacityRdata);
		block.AddOp(capConst);
		var elemSizeConst = new StdConstI64Op(1);
		block.AddOp(elemSizeConst);
		var parentZero = new StdConstI64Op(0);
		block.AddOp(parentZero);
		EmitInitManagedMemory(block, tempName, bufferPtr, lengthVal, capConst.Result, elemSizeConst.Result, parentZero.Result, varTypes);

		if (isString) {
			var flagConst = new StdConstI64Op(0);
			block.AddOp(flagConst);
			EmitStructFieldStore(block, flagConst.Result, tempName, StringFieldSingleByteGraphemes, IrType.I64, varTypes);
		}

		return new StdHeapPtr(outerPtr.Id, outerPtr.TypeName, tempName);
	}

	/// Envelope-collapse construction of a fused managed wrapper from a REAL source:
	/// `String{managed: X, singleByteGraphemesFlag: A}` / `Character{managed: X}` / `Array{managed: X}` (init,
	/// clone, slice — X is a parameter or method result, not a fresh literal). The result is a fresh
	/// slice-VIEW of the source, matching the pre-collapse envelope's refcount shape. Array/Vector
	/// literals and empty `create()` are handled separately (absorbed into a single record — see the
	/// struct-literal lowering); this method is never reached for those.
	private static void LowerFusedWrapperConstruction(
	  MaxonStructLiteralOp op, bool isString, bool isFusedArray,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps,
	  Dictionary<int, string> inlineTargets,
	  string scopeName) {

		MaxonValue? managedVal = null;
		MaxonValue? flagVal = null;
		foreach (var (fieldName, fieldVal) in op.FieldValues) {
			if (fieldName == "managed") managedVal = fieldVal;
			else if (fieldName == "singleByteGraphemesFlag") flagVal = fieldVal;
		}
		if (managedVal == null)
			throw new InvalidOperationException($"{op.TypeName} construction missing 'managed' field in '{scopeName}'");
		if (valueMap[managedVal] is not StdHeapPtr srcHp)
			throw new InvalidOperationException($"{op.TypeName} construction: 'managed' value %{managedVal.Id} is not a heap pointer in '{scopeName}'");
		var srcVarName = srcHp.VarName!;

		var resultVarName = inlineTargets.TryGetValue(op.Result.Id, out var it)
			? it
			: temps.CreateTemp("view", op.Result.Id, op.TypeName, OwnershipFlags.None);

		// VIEW: a fresh record that shares the source's buffer (capacity=-1) and holds a reference
		// to it (parent=source, incref source). This is the exact refcount shape of the pre-collapse
		// envelope — a distinct object referencing the underlying bytes — so the ownership discipline
		// balances unchanged, and copy-on-write protects the shared buffer against later mutation.
		var viewPtr = (StdHeapPtr)EmitAlloc(block, FusedManagedRecordSize(op.TypeName), op.TypeName, scopeName: scopeName);
		EmitStore(block, viewPtr, resultVarName, varTypes);
		var srcBuf = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldBuffer, IrType.I64, varTypes);
		var srcLen = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldLength, IrType.I64, varTypes);
		var negOne = new StdConstI64Op(-1);
		block.AddOp(negOne);
		// Element stride: String/Character are UTF-8 byte buffers (1); an Array/Vector view must
		// preserve the source's element_size so indexing/iteration keeps the right stride.
		StdI64 viewElemSize;
		if (isFusedArray) {
			viewElemSize = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldElementSize, IrType.I64, varTypes);
		} else {
			var oneElem = new StdConstI64Op(1);
			block.AddOp(oneElem);
			viewElemSize = oneElem.Result;
		}
		var srcParent = (StdI64)EmitLoad(block, srcVarName, varTypes);
		EmitInitManagedMemory(block, resultVarName, srcBuf, srcLen, negOne.Result, viewElemSize, srcParent, varTypes);
		EmitIncrefValue(block, srcParent, scopeName: scopeName);
		if (isString) {
			var (viewFlag, viewFlagType) = ResolveSingleByteGraphemesFlag(flagVal, valueMap, block);
			EmitStructFieldStore(block, viewFlag, resultVarName, StringFieldSingleByteGraphemes, viewFlagType, varTypes);
		}
		valueMap[op.Result] = new StdHeapPtr(op.Result.Id, op.TypeName, resultVarName);
	}

	/// The flag's value + its storage width for a String construction. Stored at its natural
	/// width; the low byte is read back as the `bool` field. Defaults to 0 (conservative) if absent.
	private static (StdValue Value, IrType Type) ResolveSingleByteGraphemesFlag(
	  MaxonValue? flagVal, Dictionary<MaxonValue, StdValue> valueMap, IrBlock<StandardOp> block) {
		if (flagVal != null && valueMap.TryGetValue(flagVal, out var v)) {
			return v is StdBool ? (v, IrType.I1) : (v, IrType.I64);
		}
		var zero = new StdConstI64Op(0);
		block.AddOp(zero);
		return (zero.Result, IrType.I64);
	}

	/// Converts an int-backed enum raw value to its ordinal via a select chain.
	private static StdI64 EmitIntEnumToOrdinal(
	  IrEnumType enumType, StdI64 rawValue, IrBlock<StandardOp> block) {
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
	  IrEnumType enumType, StdF64 rawValue, IrBlock<StandardOp> block) {
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
	/// Unlike EmitIntEnumToOrdinal (which returns IrEnumCase.Ordinal, used for internal name/rawValue lookup),
	/// this returns the case's index in the Cases list — the true declaration position.
	private static StdI64 EmitIntEnumToPositionIndex(
	  IrEnumType enumType, StdI64 rawValue, IrBlock<StandardOp> block) {
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
	  IrEnumType enumType, StdF64 rawValue, IrBlock<StandardOp> block) {
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
	  IrEnumType enumType, StdI64 ordinalValue,
	  IrBlock<StandardOp> block, IrModule<StandardOp> result) {
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
	  StdBool boolValue, IrBlock<StandardOp> block, Dictionary<string, string> varTypes, StdI64? destBuffer = null) =>
	  EmitRuntimeToString(boolValue, "maxon_bool_to_string", BoolToStringMaxBytes, block, varTypes, destBuffer);

	private static void LowerManagedMemSlice(
	  MaxonManagedMemSliceOp op,
	  IrFunction<StandardOp> func,
	  ref IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps,
	  string? inlineTarget = null,
	  MaxonValue? errorFlagValue = null) {

		var srcVarName = ResolveManagedVarName(op.Managed, valueMap);
		var srcLength = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldLength, IrType.I64, varTypes);

		var start = (StdI64)valueMap[op.Start];
		var end = (StdI64)valueMap[op.End];

		// Bounds checks: end <= length AND start <= end.
		// __ManagedMemoryError.sliceOutOfBounds (ordinal 2 — enum 0-based 2, plus 1 for success=0).
		// (emptySlot is ordinal 1, slot-empty fired by get() not slice().)
		const int sliceOobOrdinal = 3;
		if (errorFlagValue != null) {
			// Compose both predicates into a single "any violation" check to avoid emitting two
			// independent error-flag writes (the second would clobber the first).
			var endTooLarge = new StdCmpU64Op("ugt", end, srcLength);
			block.AddOp(endTooLarge);
			var startPastEnd = new StdCmpU64Op("ugt", start, end);
			block.AddOp(startPastEnd);
			var anyErr = new StdOrI1Op(endTooLarge.Result, startPastEnd.Result);
			block.AddOp(anyErr);
			EmitBoundsCheckErrorFlag(block, anyErr.Result, sliceOobOrdinal, valueMap, varTypes, errorFlagValue);
		} else {
			// Defensive panic-fallback for any non-try call site (e.g. cloned ops or
			// future passes that emit the dedicated MaxonManagedMemSliceOp directly).
			var sliceOneConst = new StdConstI64Op(1);
			block.AddOp(sliceOneConst);
			var lengthPlusOne = new StdAddI64Op(srcLength, sliceOneConst.Result);
			block.AddOp(lengthPlusOne);
			EmitBoundsCheck(block, end, lengthPlusOne.Result, "__mm_panic_slice_oob");
			var endPlusOne = new StdAddI64Op(end, sliceOneConst.Result);
			block.AddOp(endPlusOne);
			EmitBoundsCheck(block, start, endPlusOne.Result, "__mm_panic_slice_oob");
		}

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
			var slicePtr = (StdHeapPtr)EmitAlloc(block, FusedManagedRecordSize(managedTypeName), managedTypeName, tag: "Slice", scopeName: _currentFuncName);
			EmitStore(block, slicePtr, tempName, varTypes);

			var newBuffer = EmitRawAlloc(block, sliceByteSize, label: "slice.buf", scopeName: _currentFuncName);

			// Bit-by-bit copy loop: for i from 0 to sliceLen-1, get bit (start+i) from source, set bit i in dest
			var loopUid = IrContext.Current.NextId();
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

			var bitPackedParentZero = new StdConstI64Op(0);
			block.AddOp(bitPackedParentZero);
			EmitInitManagedMemory(block, tempName, dstBufFinal, sliceLenFinal, sliceLenFinal, zeroElemSize.Result, bitPackedParentZero.Result, varTypes);

			valueMap[op.Result] = new StdHeapPtr(slicePtr.Id, slicePtr.TypeName, tempName);
		} else {
			// Zero-copy slice: create a view into the source buffer, no data copy.
			// The slice stores a pointer into the parent's buffer and increfs the parent.
			// Data is only copied on mutation (COW) or cstring conversion.
			var srcElemSize = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldElementSize, IrType.I64, varTypes);

			// Convert element index to byte offset: start * element_size
			var startBytesOp = new StdMulI64Op(start, srcElemSize);
			block.AddOp(startBytesOp);

			// Source address for the slice data (pointer into parent's buffer)
			var srcAddrOp = new StdAddI64Op(srcBuffer, startBytesOp.Result);
			block.AddOp(srcAddrOp);

			// Slice length in elements is end - start
			var sliceLenOp = new StdSubI64Op(end, start);
			block.AddOp(sliceLenOp);

			// Heap-allocate __ManagedMemory struct (no raw buffer allocation)
			var managedTypeName = op.Result.TypeName;
			var tempName = inlineTarget
				?? temps.CreateTemp("slice", op.Result.Id, managedTypeName, OwnershipFlags.None);
			var slicePtr = (StdHeapPtr)EmitAlloc(block, FusedManagedRecordSize(managedTypeName), managedTypeName, tag: "Slice", scopeName: _currentFuncName);
			EmitStore(block, slicePtr, tempName, varTypes);

			// Store buffer (pointer into parent's data) and length
			EmitStructFieldStore(block, srcAddrOp.Result, tempName, ManagedFieldBuffer, IrType.I64, varTypes);
			EmitStructFieldStore(block, sliceLenOp.Result, tempName, ManagedFieldLength, IrType.I64, varTypes);
			EmitStructFieldStore(block, srcElemSize, tempName, ManagedFieldElementSize, IrType.I64, varTypes);

			// Determine parent and capacity based on source's mode:
			//   source capacity == MmCapacityRdata: slice takes it too, parentPtr=0 (static data, no refcounting)
			//   source capacity == -1 (nested slice): slice gets capacity=-1, parentPtr=source.parentPtr
			//   source capacity >= 0 (owned): copy data into new owned buffer
			//
			// ⚠ A DEEP-CLONING COPY HAS NO VIEW MODE, and skipping this dispatch is the whole of why.
			// A view holds no buffer of its own, so there is nowhere to put the element clones — the
			// copy would silently degrade back to sharing the source's elements. It bit exactly where
			// a view is produced: `Array.clone` hands back `Self{managed: <the copy>}`, which IS a
			// capacity==-1 view, so `a.clone().clone()` took the nested-slice arm and the second clone
			// aliased the first (measured: `a=original b=MUTATED` where shv2 answers `b=original`).
			bool deepCloneNeedsOwnBuffer = op.ElementClonerName != null;

			// Spill sliceLenOp since conditional blocks may follow
			var sliceLenVar = $"__slice_len_{op.Result.Id}";
			EmitStore(block, sliceLenOp.Result, sliceLenVar, varTypes);

			var uid = IrContext.Current.NextId();
			var rdataBlock = $"__slice_rdata_{uid}";
			var checkSliceBlock = $"__slice_check_{uid}";
			var sliceOfSliceBlock = $"__slice_nested_{uid}";
			var ownedBlock = $"__slice_owned_{uid}";
			var doneBlock = $"__slice_done_{uid}";

			if (deepCloneNeedsOwnBuffer) {
				block.AddOp(new StdBrOp(ownedBlock));
			} else {
				var srcCapacity = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldCapacity, IrType.I64, varTypes);
				var negTwoConst = new StdConstI64Op(MmCapacityRdata);
				block.AddOp(negTwoConst);
				var isRdata = new StdCmpI64Op("eq", srcCapacity, negTwoConst.Result);
				block.AddOp(isRdata);
				block.AddOp(new StdCondBrOp(isRdata.Result, rdataBlock, checkSliceBlock));

				// --- rdata path: capacity=MmCapacityRdata, parentPtr=0 ---
				var rdataBody = func.Body.AddBlock(rdataBlock);
				var rdataNegTwo = new StdConstI64Op(MmCapacityRdata);
				rdataBody.AddOp(rdataNegTwo);
				var rdataZero = new StdConstI64Op(0);
				rdataBody.AddOp(rdataZero);
				EmitStructFieldStore(rdataBody, rdataNegTwo.Result, tempName, ManagedFieldCapacity, IrType.I64, varTypes);
				EmitStructFieldStore(rdataBody, rdataZero.Result, tempName, ManagedFieldParentPtr, IrType.I64, varTypes);
				rdataBody.AddOp(new StdBrOp(doneBlock));

				// --- check if source is a slice (capacity == -1) ---
				var checkBody = func.Body.AddBlock(checkSliceBlock);
				var negOneConst = new StdConstI64Op(-1);
				checkBody.AddOp(negOneConst);
				var srcCapReload = (StdI64)EmitStructFieldLoad(checkBody, srcVarName, ManagedFieldCapacity, IrType.I64, varTypes);
				var isNestedSlice = new StdCmpI64Op("eq", srcCapReload, negOneConst.Result);
				checkBody.AddOp(isNestedSlice);
				checkBody.AddOp(new StdCondBrOp(isNestedSlice.Result, sliceOfSliceBlock, ownedBlock));

				// --- nested slice path: capacity=-1, parentPtr=source.parentPtr, incref(source.parentPtr) ---
				var nestedBody = func.Body.AddBlock(sliceOfSliceBlock);
				var nestedNegOne = new StdConstI64Op(-1);
				nestedBody.AddOp(nestedNegOne);
				EmitStructFieldStore(nestedBody, nestedNegOne.Result, tempName, ManagedFieldCapacity, IrType.I64, varTypes);
				var srcParentPtr = (StdI64)EmitStructFieldLoad(nestedBody, srcVarName, ManagedFieldParentPtr, IrType.I64, varTypes);
				EmitStructFieldStore(nestedBody, srcParentPtr, tempName, ManagedFieldParentPtr, IrType.I64, varTypes);
				EmitIncrefValue(nestedBody, srcParentPtr, scopeName: _currentFuncName);
				nestedBody.AddOp(new StdBrOp(doneBlock));
			}

			// --- owned path: copy data (heap-allocated source cannot be zero-copy because
			// struct-level COW can't distinguish slice refs from normal refs) ---
			var ownedBody = func.Body.AddBlock(ownedBlock);
			// Reload values needed for copy (registers may be clobbered by prior blocks)
			var ownedSrcBuf = LoadManagedBuffer(ownedBody, srcVarName, varTypes);
			var ownedStartBytes = new StdMulI64Op(start, (StdI64)EmitStructFieldLoad(ownedBody, srcVarName, ManagedFieldElementSize, IrType.I64, varTypes));
			ownedBody.AddOp(ownedStartBytes);
			var ownedSrcAddr = new StdAddI64Op(ownedSrcBuf, ownedStartBytes.Result);
			ownedBody.AddOp(ownedSrcAddr);
			var ownedSliceLen = (StdI64)EmitLoad(ownedBody, sliceLenVar, varTypes);
			var ownedElemSize = (StdI64)EmitStructFieldLoad(ownedBody, srcVarName, ManagedFieldElementSize, IrType.I64, varTypes);
			var ownedSliceBytes = new StdMulI64Op(ownedSliceLen, ownedElemSize);
			ownedBody.AddOp(ownedSliceBytes);
			// Allocate new buffer (sliceBytes + 1 for null terminator)
			var ownedOneExtra = new StdConstI64Op(1);
			ownedBody.AddOp(ownedOneExtra);
			var ownedAllocSize = new StdAddI64Op(ownedSliceBytes.Result, ownedOneExtra.Result);
			ownedBody.AddOp(ownedAllocSize);
			var ownedNewBuf = EmitRawAlloc(ownedBody, ownedAllocSize.Result, label: "slice.buf", scopeName: _currentFuncName);
			// Copy data
			ownedBody.AddOp(new StdMemCopyOp(ownedSrcAddr.Result, ownedNewBuf, ownedSliceBytes.Result));
			// Store fields: owned buffer with capacity = sliceLen
			var ownedParentZero = new StdConstI64Op(0);
			ownedBody.AddOp(ownedParentZero);
			EmitInitManagedMemory(ownedBody, tempName, ownedNewBuf, ownedSliceLen, ownedSliceLen, ownedElemSize, ownedParentZero.Result, varTypes);
			// The memcpy above copied raw 8-byte element POINTERS, which carry no claim on what they
			// point at. This copy is a NEW array value (`Array.clone` and `Array.slice` are the two
			// callers), so each element becomes the copy's OWN:
			//
			//  - a Cloneable element is DEEP-CLONED through its own `clone`. `specs/memory-safety.md`
			//    says `.clone()` is "a new, independent copy", and an element reached through `get` is
			//    a mutable heap record — sharing one made a write through the copy a write to the
			//    original (`Array with <struct>`, and `Array with String` just as much: `append`
			//    mutates the record the slot points at).
			//  - anything with no Cloneable conformance is RETAINED, because the language defines no
			//    independent copy of it and there is no cloner to call. ManagedElementCopy decides
			//    which, once, for the pass that must MAKE the cloner and the pass that must KEEP it.
			if (op.IsStructElement) {
				if (op.ElementClonerName != null) {
					ownedBody = EmitDeepCloneManagedElements(func, ownedBody, tempName, op.ElementClonerName, varTypes);
				} else {
					var ownedManagedPtr = (StdI64)EmitLoad(ownedBody, tempName, varTypes);
					ownedBody.AddOp(new StdCallRuntimeOp("mm_incref_managed_elements", [ownedManagedPtr], null));
				}
			}
			ownedBody.AddOp(new StdBrOp(doneBlock));

			// --- done: continue after slice creation ---
			block = func.Body.AddBlock(doneBlock);

			valueMap[op.Result] = new StdHeapPtr(slicePtr.Id, slicePtr.TypeName, tempName);
		}
	}

	/// <summary>
	/// Replace every live slot of a freshly copied managed buffer with an INDEPENDENT clone of what
	/// the memcpy left there, so the copy owns its elements outright instead of sharing the source's.
	///
	/// The walk is the deep half of the same loop `mm_incref_managed_elements` runs shallowly: one
	/// pass over `length` 8-byte pointer slots. It stays in the lowering rather than the runtime
	/// because the cloner is per ELEMENT TYPE and the runtime helpers take no function pointer — the
	/// concrete element type is known here and nowhere below.
	///
	/// A ZERO slot is left alone: `resize`/`remove` leave slots that hold nothing, and the same
	/// null guard that keeps `get` from increfing one keeps this from calling a cloner on it.
	///
	/// Returns the block execution continues in.
	/// </summary>
	private static IrBlock<StandardOp> EmitDeepCloneManagedElements(
	  IrFunction<StandardOp> func,
	  IrBlock<StandardOp> block,
	  string managedVarName,
	  string clonerName,
	  Dictionary<string, string> varTypes) {

		var uid = IrContext.Current.NextId();
		var loopVar = $"__elemclone_i_{uid}";
		var slotVar = $"__elemclone_slot_{uid}";
		var sourceVar = $"__elemclone_src_{uid}";
		var startIndex = new StdConstI64Op(0);
		block.AddOp(startIndex);
		EmitStore(block, startIndex.Result, loopVar, varTypes);

		var headerLabel = $"__elemclone_hdr_{uid}";
		var bodyLabel = $"__elemclone_body_{uid}";
		var cloneLabel = $"__elemclone_do_{uid}";
		var stepLabel = $"__elemclone_step_{uid}";
		var doneLabel = $"__elemclone_done_{uid}";
		block.AddOp(new StdBrOp(headerLabel));

		var headerBlock = func.Body.AddBlock(headerLabel);
		var indexAtHeader = (StdI64)EmitLoad(headerBlock, loopVar, varTypes);
		var length = (StdI64)EmitStructFieldLoad(headerBlock, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
		var moreToDo = new StdCmpI64Op("lt", indexAtHeader, length);
		headerBlock.AddOp(moreToDo);
		headerBlock.AddOp(new StdCondBrOp(moreToDo.Result, bodyLabel, doneLabel));

		var bodyBlock = func.Body.AddBlock(bodyLabel);
		var indexInBody = (StdI64)EmitLoad(bodyBlock, loopVar, varTypes);
		var buffer = LoadManagedBuffer(bodyBlock, managedVarName, varTypes);
		var pointerSize = new StdConstI64Op(ManagedElementPointerSize);
		bodyBlock.AddOp(pointerSize);
		var slotOffset = new StdMulI64Op(indexInBody, pointerSize.Result);
		bodyBlock.AddOp(slotOffset);
		var slotAddr = new StdAddI64Op(buffer, slotOffset.Result);
		bodyBlock.AddOp(slotAddr);
		EmitStore(bodyBlock, slotAddr.Result, slotVar, varTypes);
		var slotValue = new StdLoadIndirectOp(slotAddr.Result, 0, IrType.I64);
		bodyBlock.AddOp(slotValue);
		EmitStore(bodyBlock, (StdI64)slotValue.Result, sourceVar, varTypes);
		var emptySlot = new StdConstI64Op(0);
		bodyBlock.AddOp(emptySlot);
		// ⚠ THE `Then` TARGET IS THE ONE THAT FALLS THROUGH, so it has to be the block added NEXT.
		// StdCondBrOp lowers to a SINGLE `jcc` to `Else` and nothing at all for `Then`
		// (StandardToX86Conversion, `case StdCondBrOp`), so a `Then` that is not the physically next
		// block is not jumped to — control walks into whatever was added instead. Written the other
		// way round (`eq 0` -> step), a ZERO slot fell straight into the clone block and called
		// `<Element>.clone(null)`: measured as `panic: nil pointer` in `ByteArray.clone` for any
		// `.clone()` reaching a `Map` with a managed key, whose empty buckets are zero slots INSIDE
		// `length`.
		var slotIsFilled = new StdCmpI64Op("ne", (StdI64)slotValue.Result, emptySlot.Result);
		bodyBlock.AddOp(slotIsFilled);
		bodyBlock.AddOp(new StdCondBrOp(slotIsFilled.Result, cloneLabel, stepLabel));

		var cloneBlock = func.Body.AddBlock(cloneLabel);
		var sourceElement = (StdI64)EmitLoad(cloneBlock, sourceVar, varTypes);
		var clonedElement = EmitElementCloneCall(cloneBlock, clonerName, sourceElement, varTypes);
		var slotAddrReload = (StdI64)EmitLoad(cloneBlock, slotVar, varTypes);
		cloneBlock.AddOp(new StdStoreIndirectOp(clonedElement, slotAddrReload, 0, IrType.I64));
		cloneBlock.AddOp(new StdBrOp(stepLabel));

		var stepBlock = func.Body.AddBlock(stepLabel);
		var indexAtStep = (StdI64)EmitLoad(stepBlock, loopVar, varTypes);
		var one = new StdConstI64Op(1);
		stepBlock.AddOp(one);
		var nextIndex = new StdAddI64Op(indexAtStep, one.Result);
		stepBlock.AddOp(nextIndex);
		EmitStore(stepBlock, nextIndex.Result, loopVar, varTypes);
		stepBlock.AddOp(new StdBrOp(headerLabel));

		return func.Body.AddBlock(doneLabel);
	}

	/// <summary>
	/// Call one element cloner and hand back the pointer the buffer slot takes ownership of.
	///
	/// The +1 the cloner returns is the one the BUFFER holds — the array's teardown walk releases
	/// it — so nothing here registers a scope temp and nothing here increfs an ordinary result.
	/// A cloner that returns a TWO-REGISTER VALUE TUPLE hands its pair back in registers instead of
	/// a record, so the record the slot must hold is built here, and that one does need its single
	/// reference established (`EmitAlloc` publishes a record at zero).
	/// </summary>
	private static StdI64 EmitElementCloneCall(
	  IrBlock<StandardOp> block,
	  string clonerName,
	  StdI64 elementPtr,
	  Dictionary<string, string> varTypes) {

		var module = _sourceModule
		  ?? throw new InvalidOperationException("Element clone call emitted outside MaxonToStandardConversion.Run");
		var valueTupleType = _valueTupleReturnFunctions?.Contains(clonerName) == true
		  ? IrStructType.AsTwoRegisterValueTuple(module.FindFunctionByExactName(clonerName)?.ReturnType, module.TypeDefs)
		  : null;

		if (valueTupleType == null) {
			var result = new StdI64(IrContext.Current.NextStdId());
			block.AddOp(new StdCallOp(clonerName, [elementPtr], result));
			return result;
		}

		var low = new StdI64(IrContext.Current.NextStdId());
		var high = new StdI64(IrContext.Current.NextStdId());
		block.AddOp(new StdCallOp(clonerName, [elementPtr], low, high));
		var recordVar = $"__elemclone_tuple_{IrContext.Current.NextId()}";
		var record = EmitAlloc(block, valueTupleType.SizeInBytes, valueTupleType.Name, scopeName: _currentFuncName);
		EmitStore(block, record, recordVar, varTypes);
		EmitStructFieldStore(block, low, recordVar, valueTupleType.Fields[0].Offset,
		  IrType.Resolve(valueTupleType.Fields[0].Type), varTypes);
		EmitStructFieldStore(block, high, recordVar, valueTupleType.Fields[1].Offset,
		  IrType.Resolve(valueTupleType.Fields[1].Type), varTypes);
		EmitIncrefValue(block, record, scopeName: _currentFuncName);
		return record;
	}

	/// <summary>
	/// `makeCharFromBytes(pos, len)`: a Character owning a private COPY of `len` bytes taken from the
	/// receiver at `pos`. THE way the language turns part of a string into a Character — `charAt`,
	/// `trim`, and every step of `for c in s` land here (via the inlined `String.makeCharFromBytes`,
	/// see TryEmitBuiltinStringMethod).
	///
	/// It COPIES rather than slicing a view, and that is what makes it Stage 4b-2-ready: a small
	/// String will carry its bytes in a REGISTER, and a view cannot point into one. A copy can be
	/// made from either representation, so 4b-2 changes how the source bytes are addressed here and
	/// nothing above it moves.
	///
	/// The copy is not a cost — it is cheaper than the view it replaced. `Character.init(src.slice(
	/// pos, pos+len))` was TWO records (the slice, then the Character viewing it) plus an incref, and
	/// it PINNED the whole source string alive behind a 1-4 byte grapheme. This is ONE allocation
	/// that owns its bytes and references nothing.
	/// </summary>
	private static void LowerMakeCharFromBytes(
	  MaxonMakeCharFromBytesOp op,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  VarRegistry temps) {

		var srcVarName = ResolveManagedVarName(op.Managed, valueMap);
		var srcBuffer = LoadManagedBuffer(block, srcVarName, varTypes);
		var pos = (StdI64)valueMap[op.Pos];
		var len = (StdI64)valueMap[op.Len];

		// Bounds check: pos + len must be <= source length (byte range within buffer).
		// Panic on violation — stdlib callers validate before calling this builtin.
		// EmitBoundsCheck tests index < limit via unsigned compare; pass length+1 to allow equality.
		var srcLength = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldLength, IrType.I64, varTypes);
		var posPlusLen = new StdAddI64Op(pos, len);
		block.AddOp(posPlusLen);
		var oneForMkCharConst = new StdConstI64Op(1);
		block.AddOp(oneForMkCharConst);
		var lengthPlusOne = new StdAddI64Op(srcLength, oneForMkCharConst.Result);
		block.AddOp(lengthPlusOne);
		EmitBoundsCheck(block, posPlusLen.Result, lengthPlusOne.Result, "__mm_panic_byte_oob");

		// Compute source address: srcBuffer + pos
		var srcAddrOp = new StdAddI64Op(srcBuffer, pos);
		block.AddOp(srcAddrOp);

		// Store len and srcAddr to stack vars so they survive calls and memcopy
		var lenVar = $"__mkchar_len_{op.Result.Id}";
		EmitStore(block, len, lenVar, varTypes);
		var srcAddrVar = $"__mkchar_src_{op.Result.Id}";
		EmitStore(block, srcAddrOp.Result, srcAddrVar, varTypes);

		// Byte-fusion: ONE allocation holds BOTH the Character record AND its bytes, which live
		// INLINE right after it (buffer = self + CharacterStructSize) marked by
		// parent_ptr = MmParentInline. The bytes die with the record's slot — the destructor's
		// inline-sentinel check skips the raw free — so there is no second object to free.
		// fusedSize = CharacterStructSize + len + 1 (trailing NUL, so `toCString` never re-copies).
		var charVarName = temps.CreateTemp("char", op.Result.Id, "Character", OwnershipFlags.None);
		var lenForAlloc = (StdI64)EmitLoad(block, lenVar, varTypes);
		var recPlusNulOp = new StdConstI64Op(CharacterStructSize + 1);
		block.AddOp(recPlusNulOp);
		var fusedSize = new StdAddI64Op(lenForAlloc, recPlusNulOp.Result);
		block.AddOp(fusedSize);
		var charOuterPtr = (StdHeapPtr)EmitAlloc(block, fusedSize.Result, "Character", scopeName: _currentFuncName);
		EmitStore(block, charOuterPtr, charVarName, varTypes);

		// buffer = self + CharacterStructSize (the inline region)
		var dstBufVar = $"__mkchar_dst_{op.Result.Id}";
		var inlineBuf = EmitInlineBufferPtr(block, charVarName, CharacterStructSize, varTypes);
		EmitStore(block, inlineBuf, dstBufVar, varTypes);

		// Reload values for memcopy (alloc clobbers registers)
		var reloadLen = (StdI64)EmitLoad(block, lenVar, varTypes);
		var reloadSrc = (StdI64)EmitLoad(block, srcAddrVar, varTypes);
		var reloadDst = (StdI64)EmitLoad(block, dstBufVar, varTypes);

		// Copy bytes from source to the inline buffer
		block.AddOp(new StdMemCopyOp(reloadSrc, reloadDst, reloadLen));

		// Reload all values again after memcopy (rep movsb clobbers RSI/RDI/RCX)
		var finalLen = (StdI64)EmitLoad(block, lenVar, varTypes);
		var finalBuf = (StdI64)EmitLoad(block, dstBufVar, varTypes);

		// Write the null terminator at buffer[len]
		var ntAddr = new StdAddI64Op(finalBuf, finalLen);
		block.AddOp(ntAddr);
		var ntZero = new StdConstI64Op(0);
		block.AddOp(ntZero);
		block.AddOp(new StdStoreIndirectOp(ntZero.Result, ntAddr.Result, 0, IrType.I8));

		// Write the managed fields inline into the Character record. The buffer is INLINE and owned,
		// so capacity == length (exact) and parent_ptr = MmParentInline.
		var elemSizeConst = new StdConstI64Op(1);
		block.AddOp(elemSizeConst);
		var charParentInline = new StdConstI64Op(MmParentInline);
		block.AddOp(charParentInline);
		EmitInitManagedMemory(block, charVarName, finalBuf, finalLen, finalLen, elemSizeConst.Result, charParentInline.Result, varTypes);

		valueMap[op.Result] = new StdHeapPtr(charOuterPtr.Id, charOuterPtr.TypeName, charVarName);
	}
}
