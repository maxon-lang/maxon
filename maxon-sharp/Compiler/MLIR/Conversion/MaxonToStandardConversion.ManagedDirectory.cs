using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
	// __ManagedDirectoryError ordinals (see stdlib/Interfaces.maxon). Flag values are 1-indexed
	// (0 = success), so flag = ordinal + 1. notFound (0) / accessDenied (1) are dispatched at
	// runtime by SelectIoErrorOrdinal (errno→variant); iteratorInvalid / closed are stdlib-
	// internal invariant panics, not user-catchable.
	private const int MdErrOpenSearchFailed = 3;
	private const int MdErrNextFailed = 4;
	private const int MdErrCreateFailed = 6;
	private const int MdErrCurrentPathFailed = 7;

	/// <summary>
	/// Dispatches synthetic __managed_directory_* callees routed through LowerCallCore.
	/// Instance methods read _block before calling; static methods pass a cstring directly.
	/// Invariant panics (filename) emit null checks before calling.
	///
	/// Error mapping: each throwing method translates its sentinel return (0 / -1) into a 1-indexed
	/// __ManagedDirectoryError flag. SelectIoErrorOrdinal then routes specific OS error codes
	/// (ENOENT / EACCES on POSIX; ERROR_FILE_NOT_FOUND / ERROR_ACCESS_DENIED on Win32) to
	/// notFound / accessDenied; everything else falls through to the method-specific catch-all.
	/// </summary>
	public static bool TryLowerManagedDirectoryBuiltin(
	  string callee,
	  List<MaxonValue> args,
	  MaxonValue? result,
	  IrFunction<StandardOp> func,
	  ref IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MaxonValue? errorFlagValue,
	  VarRegistry temps) {

		switch (callee) {
			case "__managed_directory_open_search":
				LowerManagedDirectoryOpenSearch(args, result, func, ref block, valueMap, varTypes, errorFlagValue, temps);
				return true;
			case "__managed_directory_create":
				LowerManagedDirectoryCreate(args, block, valueMap, varTypes, errorFlagValue);
				return true;
			case "__managed_directory_current_path":
				LowerManagedDirectoryCurrentPath(result, block, valueMap, varTypes, errorFlagValue, temps);
				return true;
			case "__managed_directory_next":
				LowerManagedDirectoryNext(args, result, block, valueMap, varTypes, errorFlagValue);
				return true;
			case "__managed_directory_filename":
				LowerManagedDirectoryFilename(args, result, block, valueMap);
				return true;
			case "__managed_directory_close":
				LowerManagedDirectoryClose(args, block, valueMap);
				return true;
			case "__managed_directory_exists":
				LowerManagedDirectoryExists(args, result, block, valueMap);
				return true;
		}
		return false;
	}

	private static void LowerManagedDirectoryOpenSearch(
	  List<MaxonValue> args, MaxonValue? result,
	  IrFunction<StandardOp> func, ref IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MaxonValue? errorFlagValue,
	  VarRegistry temps) {
		// args[0] = cstring path
		// Runtime returns a refcounted __ManagedDirectory struct ptr on success, or 0 on failure.
		// On the error path we must still produce a VALID heap pointer (never 0) that the
		// destructor can safely clean up — allocate a fresh __ManagedDirectory with _block=0.
		var path = (StdI64)valueMap[args[0]];
		var callRt = new StdCallRuntimeOp("maxon_managed_dir_open_search", [path],
		  new StdI64(IrContext.Current.NextStdId()));
		block.AddOp(callRt);
		var dirPtr = (StdI64)callRt.Result!;

		var zero = new StdConstI64Op(0);
		block.AddOp(zero);
		var isError = new StdCmpI64Op("eq", dirPtr, zero.Result);
		block.AddOp(isError);
		EmitBoundsCheckErrorFlag(block, isError.Result, MdErrOpenSearchFailed + 1, valueMap, varTypes, errorFlagValue,
		  SelectIoErrorOrdinal);

		if (result != null) {
			var tempName = temps.CreateTemp("md_open", result.Id, "__ManagedDirectory", OwnershipFlags.None);
			var uid = IrContext.Current.NextId();
			var errAllocLabel = $"__md_open_err_alloc_{uid}";
			var okLabel = $"__md_open_ok_{uid}";
			var doneLabel = $"__md_open_done_{uid}";
			block.AddOp(new StdCondBrOp(isError.Result, errAllocLabel, okLabel));

			// Error path: allocate a fresh __ManagedDirectory with _block=0 so scope-end decref
			// reaches a real refcount header and __destruct___ManagedDirectory's null-block check
			// is a no-op. Same destructor as the runtime allocation, so the caller's scope cleanup
			// runs uniformly across both paths.
			var errBlock = func.Body.AddBlock(errAllocLabel);
			var emptyStruct = EmitAlloc(errBlock, 8, "__ManagedDirectory", tag: "md_err_slot", scopeName: _currentFuncName);
			var initZero = new StdConstI64Op(0);
			errBlock.AddOp(initZero);
			errBlock.AddOp(new StdStoreIndirectOp(initZero.Result, emptyStruct, 0, IrType.I64));
			EmitStore(errBlock, emptyStruct, tempName, varTypes);
			errBlock.AddOp(new StdBrOp(doneLabel));

			// Ok path: the runtime already allocated a __ManagedDirectory struct; store it.
			var okBlock = func.Body.AddBlock(okLabel);
			EmitStore(okBlock, dirPtr, tempName, varTypes);
			okBlock.AddOp(new StdBrOp(doneLabel));

			block = func.Body.AddBlock(doneLabel);
			valueMap[result] = new StdHeapPtr(dirPtr.Id, "__ManagedDirectory", tempName);
		}
	}

	private static void LowerManagedDirectoryCreate(
	  List<MaxonValue> args,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MaxonValue? errorFlagValue) {
		// args[0] = cstring path
		// Runtime returns non-zero on success, 0 on failure (CreateDirectoryA convention).
		var path = (StdI64)valueMap[args[0]];
		var callRt = new StdCallRuntimeOp("maxon_create_directory", [path],
		  new StdI64(IrContext.Current.NextStdId()));
		block.AddOp(callRt);
		var rc = (StdI64)callRt.Result!;
		var zero = new StdConstI64Op(0);
		block.AddOp(zero);
		var isError = new StdCmpI64Op("eq", rc, zero.Result);
		block.AddOp(isError);
		EmitBoundsCheckErrorFlag(block, isError.Result, MdErrCreateFailed + 1, valueMap, varTypes, errorFlagValue,
		  SelectIoErrorOrdinal);
	}

	private static void LowerManagedDirectoryCurrentPath(
	  MaxonValue? result,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MaxonValue? errorFlagValue,
	  VarRegistry temps) {
		// Runtime returns a heap-allocated cstring on success, or 0 on failure.
		// On success: convert cstring → __ManagedMemory, then free the raw buffer.
		// On error: the success continuation never runs, so the 0 is harmless.
		var callRt = new StdCallRuntimeOp("maxon_get_current_directory", [],
		  new StdI64(IrContext.Current.NextStdId()));
		block.AddOp(callRt);
		var cstrPtr = (StdI64)callRt.Result!;
		EmitSentinelErrorFlag(block, cstrPtr, 0, MdErrCurrentPathFailed, valueMap, varTypes, errorFlagValue,
		  SelectIoErrorOrdinal);
		// Convert cstring to __ManagedMemory.
		var resultId = result != null ? result.Id : IrContext.Current.NextStdId();
		var hp = LowerCStringToManagedCore(cstrPtr, resultId, block, varTypes, temps);
		// Free the raw cstring buffer allocated by maxon_get_current_directory.
		EmitRawFree(block, cstrPtr);
		if (result != null) valueMap[result] = hp;
	}

	private static void LowerManagedDirectoryNext(
	  List<MaxonValue> args, MaxonValue? result,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap,
	  Dictionary<string, string> varTypes,
	  MaxonValue? errorFlagValue) {
		// args[0] = _block pointer (already extracted by parser)
		// Runtime returns: non-zero=found, 0=no-more-files, -1=error.
		var blockPtr = (StdI64)valueMap[args[0]];
		var callRt = new StdCallRuntimeOp("maxon_find_next_file", [blockPtr],
		  new StdI64(IrContext.Current.NextStdId()));
		block.AddOp(callRt);
		var found = (StdI64)callRt.Result!;
		EmitSentinelErrorFlag(block, found, -1, MdErrNextFailed, valueMap, varTypes, errorFlagValue,
		  SelectIoErrorOrdinal);
		if (result != null) valueMap[result] = found;
	}

	private static void LowerManagedDirectoryFilename(
	  List<MaxonValue> args, MaxonValue? result,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap) {
		// args[0] = _block pointer (already extracted by parser)
		// Stdlib invariant: _block must not be null (caller must not call filename() after close()).
		// This is a compiler-detectable invariant, not a user-catchable error.
		var blockPtr = (StdI64)valueMap[args[0]];

		var zero = new StdConstI64Op(0);
		block.AddOp(zero);
		var isNull = new StdCmpI64Op("eq", blockPtr, zero.Result);
		block.AddOp(isNull);
		EmitPanicIf(block, isNull.Result, "__mm_panic_dir_filename_null_block");

		var callRt = new StdCallRuntimeOp("maxon_find_filename", [blockPtr],
		  new StdI64(IrContext.Current.NextStdId()));
		block.AddOp(callRt);
		if (result != null) valueMap[result] = callRt.Result!;
	}

	private static void LowerManagedDirectoryClose(
	  List<MaxonValue> args, IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap) {
		// close() is idempotent: pass the _block pointer so the runtime zeros the handle.
		var blockPtr = (StdI64)valueMap[args[0]];
		block.AddOp(new StdCallRuntimeOp("maxon_managed_dir_close", [blockPtr], null));
	}

	private static void LowerManagedDirectoryExists(
	  List<MaxonValue> args, MaxonValue? result,
	  IrBlock<StandardOp> block,
	  Dictionary<MaxonValue, StdValue> valueMap) {
		var path = (StdI64)valueMap[args[0]];
		var callRt = new StdCallRuntimeOp("maxon_directory_exists", [path],
		  new StdI64(IrContext.Current.NextStdId()));
		block.AddOp(callRt);
		if (result != null) valueMap[result] = callRt.Result!;
	}
}
