using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Function inlining pass - inlines small functions to enable cross-function optimizations.
///
/// Operates at the Maxon dialect level before lowering to Standard dialects.
/// </summary>
public sealed class InliningPass : AbstractPassBase {
	public override string Name => "inlining";
	public override string Description => "Inlines small functions to enable cross-function optimizations";

	/// <summary>
	/// Maximum number of operations a function can have to be considered for inlining.
	/// </summary>
	public int OperationThreshold { get; set; } = 20;

	/// <summary>
	/// Maximum number of return points allowed for inlining.
	/// Functions with more return points are considered too complex.
	/// </summary>
	public int MaxReturnPoints { get; set; } = 1;

	private int _inlinedCount;
	private int _totalCallSites;

	public override bool Run(MlirModule module) {
		_inlinedCount = 0;
		_totalCallSites = 0;

		Logger.Debug(LogCategory.Optimizer, $"inlining: analyzing {module.Functions.Count} functions");

		// Build call graph to detect recursion
		var callGraph = BuildCallGraph(module);
		var recursiveFunctions = FindRecursiveFunctions(callGraph);

		if (recursiveFunctions.Count > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  found {recursiveFunctions.Count} recursive function(s): {string.Join(", ", recursiveFunctions)}");
		}

		// Analyze which functions are inlinable
		var inlinableFunctions = new Dictionary<string, MlirFunction>();
		foreach (var func in module.Functions) {
			if (IsInlinable(func, recursiveFunctions, out var reason)) {
				inlinableFunctions[func.Name] = func;
				Logger.Trace(LogCategory.Optimizer, $"  {func.Name}: inlinable ({CountOperations(func)} ops)");
			} else {
				Logger.Trace(LogCategory.Optimizer, $"  {func.Name}: not inlinable ({reason})");
			}
		}

		Logger.Debug(LogCategory.Optimizer, $"  {inlinableFunctions.Count} function(s) eligible for inlining");

		// Process each function and inline calls
		bool anyChanged = false;
		foreach (var callerFunc in module.Functions) {
			bool changed = ProcessFunction(callerFunc, inlinableFunctions);
			anyChanged |= changed;
		}

		if (_inlinedCount > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  inlined {_inlinedCount}/{_totalCallSites} call site(s)");
		}

		return anyChanged;
	}

	/// <summary>
	/// Builds a call graph mapping function names to the set of functions they call.
	/// </summary>
	private static Dictionary<string, HashSet<string>> BuildCallGraph(MlirModule module) {
		var graph = new Dictionary<string, HashSet<string>>();

		foreach (var func in module.Functions) {
			var callees = new HashSet<string>();
			graph[func.Name] = callees;

			foreach (var op in GetAllOperations(func.Body)) {
				string? callee = op switch {
					FuncCallOp call => call.Callee,
					MaxonCallOp maxonCall => maxonCall.Callee,
					_ => null
				};
				if (callee is not null) {
					callees.Add(callee);
				}
			}
		}

		return graph;
	}

	/// <summary>
	/// Finds all functions that are part of a recursive call cycle.
	/// Uses DFS with path tracking to detect cycles.
	/// </summary>
	private static HashSet<string> FindRecursiveFunctions(Dictionary<string, HashSet<string>> callGraph) {
		var recursive = new HashSet<string>();
		var visited = new HashSet<string>();
		var currentPath = new HashSet<string>();

		foreach (var funcName in callGraph.Keys) {
			DetectCycles(funcName, callGraph, visited, currentPath, recursive);
		}

		return recursive;
	}

	/// <summary>
	/// DFS helper to detect cycles in the call graph.
	/// </summary>
	private static void DetectCycles(
		string current,
		Dictionary<string, HashSet<string>> callGraph,
		HashSet<string> visited,
		HashSet<string> currentPath,
		HashSet<string> recursive) {
		if (currentPath.Contains(current)) {
			// Found a cycle - mark all functions in the current path as recursive
			recursive.Add(current);
			return;
		}

		if (visited.Contains(current)) {
			return;
		}

		currentPath.Add(current);

		if (callGraph.TryGetValue(current, out var callees)) {
			foreach (var callee in callees) {
				DetectCycles(callee, callGraph, visited, currentPath, recursive);
				// If callee is recursive and we call it, we're part of the recursive set
				if (recursive.Contains(callee) && currentPath.Contains(callee)) {
					recursive.Add(current);
				}
			}
		}

		currentPath.Remove(current);
		visited.Add(current);
	}

	/// <summary>
	/// Determines if a function is eligible for inlining.
	/// </summary>
	private bool IsInlinable(MlirFunction func, HashSet<string> recursiveFunctions, out string reason) {
		// Never inline main
		if (func.Name == "main") {
			reason = "main function";
			return false;
		}

		// Never inline recursive functions
		if (recursiveFunctions.Contains(func.Name)) {
			reason = "recursive";
			return false;
		}

		// Don't inline declarations (no body)
		if (func.IsDeclaration) {
			reason = "declaration only";
			return false;
		}

		// Don't inline functions that return structs (they use sret calling convention)
		if (func.ResultTypes.Count > 0 && func.ResultTypes[0] is MaxonStructType) {
			reason = "returns struct (sret)";
			return false;
		}

		// Don't inline functions with sret parameter (hidden first parameter that is ptr to struct)
		// These use the sret calling convention where caller provides storage for struct return
		if (func.ParamTypes.Count > 0 && func.ParamTypes[0] is PtrType ptrType && ptrType.ElementType is MaxonStructType) {
			reason = "has sret parameter";
			return false;
		}

		// Don't inline functions with struct parameters (complex ownership semantics)
		foreach (var paramType in func.ParamTypes) {
			if (paramType is MaxonStructType or OwnedType { Inner: MaxonStructType }) {
				reason = "has struct parameter";
				return false;
			}
		}

		// Check operation count
		int opCount = CountOperations(func);
		if (opCount > OperationThreshold) {
			reason = $"too large ({opCount} > {OperationThreshold} ops)";
			return false;
		}

		// Check for multiple return points (too complex control flow)
		int returnCount = CountReturnPoints(func);
		if (returnCount > MaxReturnPoints) {
			reason = $"multiple returns ({returnCount} > {MaxReturnPoints})";
			return false;
		}

		// Check for multiple blocks (complex control flow)
		if (func.Body.Blocks.Count > 1) {
			reason = $"multiple blocks ({func.Body.Blocks.Count})";
			return false;
		}

		reason = "";
		return true;
	}

	/// <summary>
	/// Counts the total number of operations in a function.
	/// </summary>
	private static int CountOperations(MlirFunction func) {
		int count = 0;
		foreach (var op in GetAllOperations(func.Body)) {
			count++;
		}
		return count;
	}

	/// <summary>
	/// Counts the number of return operations in a function.
	/// </summary>
	private static int CountReturnPoints(MlirFunction func) {
		int count = 0;
		foreach (var op in GetAllOperations(func.Body)) {
			if (op is ReturnOp) {
				count++;
			}
		}
		return count;
	}

	/// <summary>
	/// Processes a function, inlining eligible call sites.
	/// </summary>
	private bool ProcessFunction(
		MlirFunction callerFunc,
		Dictionary<string, MlirFunction> inlinableFunctions) {
		bool anyChanged = false;
		bool changed;

		// Iterate until no more inlining is possible (may enable cascading inlines)
		do {
			changed = false;

			foreach (var block in callerFunc.Body.Blocks.ToList()) {
				var ops = block.Operations.ToList();

				for (int i = 0; i < ops.Count; i++) {
					var op = ops[i];
					string? callee = null;
					IReadOnlyList<MlirValue>? args = null;
					MlirValue? callResult = null;

					switch (op) {
						case FuncCallOp funcCall:
							callee = funcCall.Callee;
							args = funcCall.Operands;
							callResult = funcCall.Result;
							break;
						case MaxonCallOp maxonCall:
							callee = maxonCall.Callee;
							args = maxonCall.Operands;
							callResult = maxonCall.Result;
							break;
					}

					if (callee is null || args is null) {
						continue;
					}

					_totalCallSites++;

					if (!inlinableFunctions.TryGetValue(callee, out var calleeFunc)) {
						continue;
					}

					// Perform the inlining
					Logger.Trace(LogCategory.Optimizer, $"  inlining {callee} into {callerFunc.Name}");

					if (InlineCall(block, i, calleeFunc, args, callResult)) {
						_inlinedCount++;
						changed = true;
						break; // Block structure changed, restart
					}
				}

				if (changed) break;
			}

			anyChanged |= changed;
		} while (changed);

		return anyChanged;
	}

	/// <summary>
	/// Inlines a function call at the specified position.
	/// </summary>
	private static bool InlineCall(
		MlirBlock callerBlock,
		int callOpIndex,
		MlirFunction calleeFunc,
		IReadOnlyList<MlirValue> callArgs,
		MlirValue? callResult) {
		// The callee must have exactly one block (checked in IsInlinable)
		if (calleeFunc.Body.EntryBlock is null) {
			return false;
		}

		var calleeEntry = calleeFunc.Body.EntryBlock;
		var calleeParams = calleeFunc.GetParamValues();

		// Build value mapping: callee parameters -> caller arguments
		var valueMap = new Dictionary<MlirValue, MlirValue>();
		for (int i = 0; i < calleeParams.Count && i < callArgs.Count; i++) {
			valueMap[calleeParams[i]] = callArgs[i];
		}

		// Clone callee operations (except the return) and insert before the call
		var clonedOps = new List<MlirOperation>();
		MlirValue? returnValue = null;

		foreach (var op in calleeEntry.Operations) {
			if (op is ReturnOp returnOp) {
				// Capture the return value (if any)
				if (returnOp.ReturnValues.Count > 0) {
					returnValue = RemapValue(returnOp.ReturnValues[0], valueMap);
				}
				continue;
			}

			var cloned = CloneOperation(op, valueMap);
			if (cloned is not null) {
				clonedOps.Add(cloned);
			}
		}

		// If the call has a result, we need to map it to the returned value
		if (callResult is not null && returnValue is not null) {
			// Replace all uses of callResult with returnValue
			ReplaceAllUses(callerBlock.ParentRegion!, callResult, returnValue);
		}

		// Remove the call operation
		callerBlock.Operations.RemoveAt(callOpIndex);

		// Insert cloned operations at the call site
		for (int i = 0; i < clonedOps.Count; i++) {
			callerBlock.Operations.Insert(callOpIndex + i, clonedOps[i]);
			clonedOps[i].ParentBlock = callerBlock;
		}

		return true;
	}

	/// <summary>
	/// Clones an operation, remapping all values using the provided map.
	/// </summary>
	private static MlirOperation? CloneOperation(MlirOperation original, Dictionary<MlirValue, MlirValue> valueMap) {
		// Create a new operation of the same type
		MlirOperation? cloned = original switch {
			// Arithmetic operations
			ConstantOp constOp => CloneConstantOp(constOp),
			AddIOp add => CloneBinaryOp<AddIOp>(add, valueMap),
			SubIOp sub => CloneBinaryOp<SubIOp>(sub, valueMap),
			MulIOp mul => CloneBinaryOp<MulIOp>(mul, valueMap),
			DivSIOp div => CloneBinaryOp<DivSIOp>(div, valueMap),
			DivUIOp div => CloneBinaryOp<DivUIOp>(div, valueMap),
			RemSIOp rem => CloneBinaryOp<RemSIOp>(rem, valueMap),
			RemUIOp rem => CloneBinaryOp<RemUIOp>(rem, valueMap),
			AndIOp and => CloneBinaryOp<AndIOp>(and, valueMap),
			OrIOp or => CloneBinaryOp<OrIOp>(or, valueMap),
			XOrIOp xor => CloneBinaryOp<XOrIOp>(xor, valueMap),
			ShLIOp shl => CloneBinaryOp<ShLIOp>(shl, valueMap),
			ShRSIOp shrs => CloneBinaryOp<ShRSIOp>(shrs, valueMap),
			ShRUIOp shru => CloneBinaryOp<ShRUIOp>(shru, valueMap),
			CmpIOp cmp => CloneCmpIOp(cmp, valueMap),
			ExtSIOp extsi => CloneExtOp<ExtSIOp>(extsi, valueMap),
			ExtUIOp extui => CloneExtOp<ExtUIOp>(extui, valueMap),
			TruncIOp trunc => CloneTruncOp(trunc, valueMap),

			// Floating point operations
			AddFOp addf => CloneBinaryOp<AddFOp>(addf, valueMap),
			SubFOp subf => CloneBinaryOp<SubFOp>(subf, valueMap),
			MulFOp mulf => CloneBinaryOp<MulFOp>(mulf, valueMap),
			DivFOp divf => CloneBinaryOp<DivFOp>(divf, valueMap),
			RemFOp remf => CloneBinaryOp<RemFOp>(remf, valueMap),
			CmpFOp cmpf => CloneCmpFOp(cmpf, valueMap),
			NegFOp negf => CloneUnaryOp<NegFOp>(negf, valueMap),
			SIToFPOp sitofp => CloneCastOp<SIToFPOp>(sitofp, valueMap),
			ExtFOp extf => CloneFloatCastOp<ExtFOp>(extf, valueMap),
			TruncFOp truncf => CloneFloatCastOp<TruncFOp>(truncf, valueMap),
			IndexCastOp indexCast => CloneIndexCastOp(indexCast, valueMap),
			SelectOp select => CloneSelectOp(select, valueMap),

			// Math operations
			SqrtOp sqrt => CloneMathUnaryOp<SqrtOp>(sqrt, valueMap),
			FloorOp floor => CloneMathUnaryOp<FloorOp>(floor, valueMap),
			CeilOp ceil => CloneMathUnaryOp<CeilOp>(ceil, valueMap),
			RoundOp round => CloneMathUnaryOp<RoundOp>(round, valueMap),
			AbsFOp absf => CloneMathUnaryOp<AbsFOp>(absf, valueMap),
			MinFOp minf => CloneMathBinaryOp<MinFOp>(minf, valueMap),
			MaxFOp maxf => CloneMathBinaryOp<MaxFOp>(maxf, valueMap),
			TruncOp trunc => CloneMathTruncOp(trunc, valueMap),

			// Memory operations
			AllocaOp alloca => CloneAllocaOp(alloca),
			LoadOp load => CloneLoadOp(load, valueMap),
			StoreOp store => CloneStoreOp(store, valueMap),
			GetGlobalOp getGlobal => CloneGetGlobalOp(getGlobal),

			// Maxon dialect operations
			StructInitOp structInit => CloneStructInitOp(structInit, valueMap),
			FieldGetOp fieldGet => CloneFieldGetOp(fieldGet, valueMap),
			FieldSetOp fieldSet => CloneFieldSetOp(fieldSet, valueMap),
			FieldPtrOp fieldPtr => CloneFieldPtrOp(fieldPtr, valueMap),
			MoveOp move => CloneMoveOp(move, valueMap),
			BorrowOp borrow => CloneBorrowOp(borrow, valueMap),
			DropOp drop => CloneDropOp(drop, valueMap),
			ArrayNewOp arrayNew => CloneArrayNewOp(arrayNew, valueMap),
			ArrayGetOp arrayGet => CloneArrayGetOp(arrayGet, valueMap),
			ArraySetOp arraySet => CloneArraySetOp(arraySet, valueMap),
			ArrayLenOp arrayLen => CloneArrayLenOp(arrayLen, valueMap),
			ArrayPtrOp arrayPtr => CloneArrayPtrOp(arrayPtr, valueMap),
			EnumInitOp enumInit => CloneEnumInitOp(enumInit, valueMap),
			GetTagOp getTag => CloneGetTagOp(getTag, valueMap),
			GetPayloadOp getPayload => CloneGetPayloadOp(getPayload, valueMap),

			// Call operations (nested calls)
			FuncCallOp funcCall => CloneFuncCallOp(funcCall, valueMap),
			MaxonCallOp maxonCall => CloneMaxonCallOp(maxonCall, valueMap),

			// Generic fallback - try to clone generically
			_ => CloneGeneric(original)
		};

		if (cloned is null) {
			Logger.Trace(LogCategory.Optimizer, $"    warning: could not clone {original.FullName}");
			return null;
		}

		// Map the original results to the cloned results
		for (int i = 0; i < original.Results.Count && i < cloned.Results.Count; i++) {
			valueMap[original.Results[i]] = cloned.Results[i];
		}

		// Copy location if present
		cloned.Location = original.Location;

		return cloned;
	}

	/// <summary>
	/// Remaps a value using the mapping, returning the original if not mapped.
	/// </summary>
	private static MlirValue RemapValue(MlirValue value, Dictionary<MlirValue, MlirValue> valueMap) {
		return valueMap.TryGetValue(value, out var mapped) ? mapped : value;
	}

	/// <summary>
	/// Replaces all uses of oldValue with newValue in the region.
	/// </summary>
	private static void ReplaceAllUses(MlirRegion region, MlirValue oldValue, MlirValue newValue) {
		foreach (var block in region.Blocks) {
			foreach (var op in block.Operations) {
				for (int i = 0; i < op.Operands.Count; i++) {
					if (op.Operands[i] == oldValue) {
						op.Operands[i] = newValue;
					}
				}

				// Recurse into nested regions
				foreach (var nestedRegion in op.Regions) {
					ReplaceAllUses(nestedRegion, oldValue, newValue);
				}
			}
		}
	}

	// ============================================================================
	// Cloning helpers for specific operation types
	// ============================================================================

	private static ConstantOp CloneConstantOp(ConstantOp original) {
		return new ConstantOp(original.Value, original.Result.Type);
	}

	private static TBinaryOp CloneBinaryOp<TBinaryOp>(MlirOperation original, Dictionary<MlirValue, MlirValue> valueMap)
		where TBinaryOp : MlirOperation {
		var lhs = RemapValue(original.Operands[0], valueMap);
		var rhs = RemapValue(original.Operands[1], valueMap);

		return typeof(TBinaryOp).Name switch {
			nameof(AddIOp) => (TBinaryOp)(object)new AddIOp(lhs, rhs),
			nameof(SubIOp) => (TBinaryOp)(object)new SubIOp(lhs, rhs),
			nameof(MulIOp) => (TBinaryOp)(object)new MulIOp(lhs, rhs),
			nameof(DivSIOp) => (TBinaryOp)(object)new DivSIOp(lhs, rhs),
			nameof(DivUIOp) => (TBinaryOp)(object)new DivUIOp(lhs, rhs),
			nameof(RemSIOp) => (TBinaryOp)(object)new RemSIOp(lhs, rhs),
			nameof(RemUIOp) => (TBinaryOp)(object)new RemUIOp(lhs, rhs),
			nameof(AndIOp) => (TBinaryOp)(object)new AndIOp(lhs, rhs),
			nameof(OrIOp) => (TBinaryOp)(object)new OrIOp(lhs, rhs),
			nameof(XOrIOp) => (TBinaryOp)(object)new XOrIOp(lhs, rhs),
			nameof(ShLIOp) => (TBinaryOp)(object)new ShLIOp(lhs, rhs),
			nameof(ShRSIOp) => (TBinaryOp)(object)new ShRSIOp(lhs, rhs),
			nameof(ShRUIOp) => (TBinaryOp)(object)new ShRUIOp(lhs, rhs),
			nameof(AddFOp) => (TBinaryOp)(object)new AddFOp(lhs, rhs),
			nameof(SubFOp) => (TBinaryOp)(object)new SubFOp(lhs, rhs),
			nameof(MulFOp) => (TBinaryOp)(object)new MulFOp(lhs, rhs),
			nameof(DivFOp) => (TBinaryOp)(object)new DivFOp(lhs, rhs),
			nameof(RemFOp) => (TBinaryOp)(object)new RemFOp(lhs, rhs),
			_ => throw new NotSupportedException($"Unsupported binary op type: {typeof(TBinaryOp).Name}")
		};
	}

	private static TUnaryOp CloneUnaryOp<TUnaryOp>(MlirOperation original, Dictionary<MlirValue, MlirValue> valueMap)
		where TUnaryOp : MlirOperation {
		var operand = RemapValue(original.Operands[0], valueMap);

		return typeof(TUnaryOp).Name switch {
			nameof(NegFOp) => (TUnaryOp)(object)new NegFOp(operand),
			_ => throw new NotSupportedException($"Unsupported unary op type: {typeof(TUnaryOp).Name}")
		};
	}

	private static CmpIOp CloneCmpIOp(CmpIOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var lhs = RemapValue(original.Lhs, valueMap);
		var rhs = RemapValue(original.Rhs, valueMap);
		return new CmpIOp(original.Predicate, lhs, rhs);
	}

	private static CmpFOp CloneCmpFOp(CmpFOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var lhs = RemapValue(original.Lhs, valueMap);
		var rhs = RemapValue(original.Rhs, valueMap);
		return new CmpFOp(original.Predicate, lhs, rhs);
	}

	private static TExtOp CloneExtOp<TExtOp>(MlirOperation original, Dictionary<MlirValue, MlirValue> valueMap)
		where TExtOp : MlirOperation {
		var operand = RemapValue(original.Operands[0], valueMap);
		var resultType = (IntegerType)original.Results[0].Type;

		return typeof(TExtOp).Name switch {
			nameof(ExtSIOp) => (TExtOp)(object)new ExtSIOp(operand, resultType),
			nameof(ExtUIOp) => (TExtOp)(object)new ExtUIOp(operand, resultType),
			_ => throw new NotSupportedException($"Unsupported ext op type: {typeof(TExtOp).Name}")
		};
	}

	private static TruncIOp CloneTruncOp(TruncIOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var operand = RemapValue(original.Operands[0], valueMap);
		return new TruncIOp(operand, (IntegerType)original.Result.Type);
	}

	private static TCastOp CloneCastOp<TCastOp>(MlirOperation original, Dictionary<MlirValue, MlirValue> valueMap)
		where TCastOp : MlirOperation {
		var operand = RemapValue(original.Operands[0], valueMap);
		var resultType = original.Results[0].Type;

		return typeof(TCastOp).Name switch {
			nameof(SIToFPOp) => (TCastOp)(object)new SIToFPOp(operand, (FloatType)resultType),
			_ => throw new NotSupportedException($"Unsupported cast op type: {typeof(TCastOp).Name}")
		};
	}

	private static TMathOp CloneMathUnaryOp<TMathOp>(FloatUnaryOp original, Dictionary<MlirValue, MlirValue> valueMap)
		where TMathOp : FloatUnaryOp {
		var operand = RemapValue(original.Operand, valueMap);

		return typeof(TMathOp).Name switch {
			nameof(SqrtOp) => (TMathOp)(object)new SqrtOp(operand),
			nameof(FloorOp) => (TMathOp)(object)new FloorOp(operand),
			nameof(CeilOp) => (TMathOp)(object)new CeilOp(operand),
			nameof(RoundOp) => (TMathOp)(object)new RoundOp(operand),
			nameof(AbsFOp) => (TMathOp)(object)new AbsFOp(operand),
			_ => throw new NotSupportedException($"Unsupported math unary op type: {typeof(TMathOp).Name}")
		};
	}

	private static TMathOp CloneMathBinaryOp<TMathOp>(FloatBinaryMathOp original, Dictionary<MlirValue, MlirValue> valueMap)
		where TMathOp : FloatBinaryMathOp {
		var lhs = RemapValue(original.Lhs, valueMap);
		var rhs = RemapValue(original.Rhs, valueMap);

		return typeof(TMathOp).Name switch {
			nameof(MinFOp) => (TMathOp)(object)new MinFOp(lhs, rhs),
			nameof(MaxFOp) => (TMathOp)(object)new MaxFOp(lhs, rhs),
			_ => throw new NotSupportedException($"Unsupported math binary op type: {typeof(TMathOp).Name}")
		};
	}

	private static TruncOp CloneMathTruncOp(TruncOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var operand = RemapValue(original.Operand, valueMap);
		return new TruncOp(operand, (IntegerType)original.Result.Type);
	}

	private static TFloatCastOp CloneFloatCastOp<TFloatCastOp>(MlirOperation original, Dictionary<MlirValue, MlirValue> valueMap)
		where TFloatCastOp : MlirOperation {
		var operand = RemapValue(original.Operands[0], valueMap);
		var resultType = (FloatType)original.Results[0].Type;

		return typeof(TFloatCastOp).Name switch {
			nameof(ExtFOp) => (TFloatCastOp)(object)new ExtFOp(operand, resultType),
			nameof(TruncFOp) => (TFloatCastOp)(object)new TruncFOp(operand, resultType),
			_ => throw new NotSupportedException($"Unsupported float cast op type: {typeof(TFloatCastOp).Name}")
		};
	}

	private static IndexCastOp CloneIndexCastOp(IndexCastOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var operand = RemapValue(original.Operand, valueMap);
		return new IndexCastOp(operand, original.Result.Type);
	}

	private static SelectOp CloneSelectOp(SelectOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var condition = RemapValue(original.Condition, valueMap);
		var trueValue = RemapValue(original.TrueValue, valueMap);
		var falseValue = RemapValue(original.FalseValue, valueMap);
		return new SelectOp(condition, trueValue, falseValue);
	}

	private static AllocaOp CloneAllocaOp(AllocaOp original) {
		return new AllocaOp(original.MemRefType);
	}

	private static LoadOp CloneLoadOp(LoadOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var memref = RemapValue(original.MemRef, valueMap);
		var indices = original.Indices.Select(idx => RemapValue(idx, valueMap)).ToArray();
		return new LoadOp(memref, indices);
	}

	private static StoreOp CloneStoreOp(StoreOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var value = RemapValue(original.Value, valueMap);
		var memref = RemapValue(original.MemRef, valueMap);
		return new StoreOp(value, memref);
	}

	private static GetGlobalOp CloneGetGlobalOp(GetGlobalOp original) {
		return new GetGlobalOp(original.Name, (MemRefType)original.Result.Type);
	}

	private static StructInitOp CloneStructInitOp(StructInitOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var fieldValues = new Dictionary<string, MlirValue>();
		foreach (var kv in original.FieldValues) {
			fieldValues[kv.Key] = RemapValue(kv.Value, valueMap);
		}
		return new StructInitOp(original.StructType, fieldValues);
	}

	private static FieldGetOp CloneFieldGetOp(FieldGetOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var structVal = RemapValue(original.Struct, valueMap);
		return new FieldGetOp(structVal, original.FieldName, original.Result.Type);
	}

	private static FieldSetOp CloneFieldSetOp(FieldSetOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var structVal = RemapValue(original.Struct, valueMap);
		var value = RemapValue(original.Value, valueMap);
		return new FieldSetOp(structVal, original.FieldName, value);
	}

	private static FieldPtrOp CloneFieldPtrOp(FieldPtrOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var structVal = RemapValue(original.Struct, valueMap);
		// Extract the element type from the PtrType result
		var ptrType = (PtrType)original.Result.Type;
		return new FieldPtrOp(structVal, original.FieldName, original.Offset, ptrType.ElementType ?? throw new InvalidOperationException("FieldPtrOp result must have typed pointer"));
	}

	private static MoveOp CloneMoveOp(MoveOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var source = RemapValue(original.Source, valueMap);
		return new MoveOp(source);
	}

	private static BorrowOp CloneBorrowOp(BorrowOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var source = RemapValue(original.Source, valueMap);
		return new BorrowOp(source, original.IsMutable);
	}

	private static DropOp CloneDropOp(DropOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var value = RemapValue(original.Value, valueMap);
		return new DropOp(value);
	}

	private static ArrayNewOp CloneArrayNewOp(ArrayNewOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var size = RemapValue(original.Size, valueMap);
		return new ArrayNewOp(size, original.ArrayType.ElementType);
	}

	private static ArrayGetOp CloneArrayGetOp(ArrayGetOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var array = RemapValue(original.Array, valueMap);
		var index = RemapValue(original.Index, valueMap);
		return new ArrayGetOp(array, index, original.Result.Type);
	}

	private static ArraySetOp CloneArraySetOp(ArraySetOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var array = RemapValue(original.Array, valueMap);
		var index = RemapValue(original.Index, valueMap);
		var value = RemapValue(original.Value, valueMap);
		return new ArraySetOp(array, index, value);
	}

	private static ArrayLenOp CloneArrayLenOp(ArrayLenOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var array = RemapValue(original.Array, valueMap);
		return new ArrayLenOp(array);
	}

	private static ArrayPtrOp CloneArrayPtrOp(ArrayPtrOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var array = RemapValue(original.Array, valueMap);
		var index = RemapValue(original.Index, valueMap);
		// Extract element type from PtrType result
		var ptrType = (PtrType)original.Result.Type;
		return new ArrayPtrOp(array, index, ptrType.ElementType ?? throw new InvalidOperationException("ArrayPtrOp result must have typed pointer"));
	}

	private static EnumInitOp CloneEnumInitOp(EnumInitOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var payloadValues = original.PayloadValues.Select(v => RemapValue(v, valueMap)).ToList();
		return new EnumInitOp(original.EnumType, original.VariantName, payloadValues);
	}

	private static GetTagOp CloneGetTagOp(GetTagOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var enumVal = RemapValue(original.EnumValue, valueMap);
		var bitWidth = ((IntegerType)original.Result.Type).BitWidth;
		return new GetTagOp(enumVal, bitWidth);
	}

	private static GetPayloadOp CloneGetPayloadOp(GetPayloadOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var enumVal = RemapValue(original.EnumValue, valueMap);
		return new GetPayloadOp(enumVal, original.VariantName, original.PayloadIndex, original.Result.Type);
	}

	private static FuncCallOp CloneFuncCallOp(FuncCallOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var args = original.Operands.Select(v => RemapValue(v, valueMap));
		return new FuncCallOp(original.Callee, args, original.Result?.Type);
	}

	private static MaxonCallOp CloneMaxonCallOp(MaxonCallOp original, Dictionary<MlirValue, MlirValue> valueMap) {
		var args = original.Operands.Select(v => RemapValue(v, valueMap)).ToList();
		return new MaxonCallOp(original.Callee, args, [.. original.ArgOwnership], original.Result?.Type);
	}

	/// <summary>
	/// Generic fallback for cloning operations not explicitly handled.
	/// </summary>
	private static MlirOperation? CloneGeneric(MlirOperation original) {
		// For unknown operations, we cannot safely clone them
		// Return null to indicate cloning failure
		Logger.Trace(LogCategory.Optimizer, $"    cannot clone unknown operation type: {original.GetType().Name}");
		return null;
	}
}
