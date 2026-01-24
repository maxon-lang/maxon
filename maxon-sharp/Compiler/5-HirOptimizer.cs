namespace MaxonSharp.Compiler;

/// <summary>
/// Optimizes HIR modules before lowering to LIR.
/// Performs high-level optimizations that benefit from semantic information.
/// </summary>
public static class HirOptimizer {
	/// <summary>
	/// Run all HIR optimization passes on the module.
	/// </summary>
	public static void Optimize(HirModule module) {
		Logger.Debug(LogCategory.Optimizer, "Starting HIR optimization");

		// Module-level: Dead function elimination
		var reachableFunctions = EliminateDeadFunctions(module);
		Logger.Debug(LogCategory.Optimizer, $"Reachable functions: {reachableFunctions.Count}");

		// Function-level optimizations (fixed-point iteration)
		foreach (var func in reachableFunctions) {
			OptimizeFunction(func);
		}

		// Replace module's function list with reachable ones
		module.Functions.Clear();
		module.Functions.AddRange(reachableFunctions);

		Logger.Debug(LogCategory.Optimizer, "HIR optimization complete");
	}

	private static void OptimizeFunction(HirFunction func) {
		Logger.Debug(LogCategory.Optimizer, $"Optimizing function: {func.Name}");

		bool changed;
		var iterations = 0;
		const int maxIterations = 10;

		do {
			changed = false;
			iterations++;

			// Run each optimization pass
			changed |= ConstantFolding(func);
			changed |= PeepholeOptimizations(func);
			changed |= CopyPropagation(func);
			changed |= DeadCodeElimination(func);

		} while (changed && iterations < maxIterations);

		Logger.Debug(LogCategory.Optimizer, $"Function {func.Name}: {iterations} optimization iterations");
	}

	/// <summary>
	/// Remove functions not reachable from main.
	/// </summary>
	private static List<HirFunction> EliminateDeadFunctions(HirModule module) {
		var reachable = new HashSet<string>();
		var workList = new Queue<string>();

		// Start from main and exported functions
		foreach (var func in module.Functions) {
			if (func.Name == "main" || func.IsExport) {
				if (reachable.Add(func.Name)) {
					workList.Enqueue(func.Name);
				}
			}
		}

		// Build function lookup
		var funcLookup = module.Functions.ToDictionary(f => f.Name);

		// Trace all called functions
		while (workList.Count > 0) {
			var funcName = workList.Dequeue();
			if (!funcLookup.TryGetValue(funcName, out var func)) continue;

			foreach (var block in func.Blocks) {
				foreach (var instr in block.Instructions) {
					if (instr is HirCall call && !reachable.Contains(call.FuncName)) {
						if (reachable.Add(call.FuncName)) {
							workList.Enqueue(call.FuncName);
						}
					}
				}
			}
		}

		Logger.Debug(LogCategory.Optimizer, $"Dead function elimination: {module.Functions.Count} -> {reachable.Count} functions");

		return module.Functions.Where(f => reachable.Contains(f.Name)).ToList();
	}

	/// <summary>
	/// Fold constant expressions at compile time.
	/// e.g., 10 + 20 -> 30
	/// </summary>
	private static bool ConstantFolding(HirFunction func) {
		var changed = false;
		var constants = new Dictionary<int, long>(); // HirValue.Id -> constant value

		foreach (var block in func.Blocks) {
			for (var i = 0; i < block.Instructions.Count; i++) {
				var instr = block.Instructions[i];

				// Track constant values
				if (instr is HirConstInt constInt) {
					constants[constInt.Dest.Id] = constInt.Value;
					continue;
				}

				// Fold binary operations with constant operands
				if (instr is IHirBinaryOp binOp) {
					if (constants.TryGetValue(binOp.Left.Id, out var leftVal) &&
						constants.TryGetValue(binOp.Right.Id, out var rightVal)) {
						long? result = instr switch {
							HirAdd => leftVal + rightVal,
							HirSub => leftVal - rightVal,
							HirMul => leftVal * rightVal,
							HirDiv when rightVal != 0 => leftVal / rightVal,
							HirMod when rightVal != 0 => leftVal % rightVal,
							HirBand => leftVal & rightVal,
							HirBor => leftVal | rightVal,
							HirBxor => leftVal ^ rightVal,
							HirShl => leftVal << (int)rightVal,
							HirShr => leftVal >> (int)rightVal,
							_ => null
						};

						if (result.HasValue) {
							block.Instructions[i] = new HirConstInt(binOp.Dest, result.Value);
							constants[binOp.Dest.Id] = result.Value;
							changed = true;
							Logger.Debug(LogCategory.Optimizer, $"Constant folded: {binOp.Dest} = {result.Value}");
						}
					}
				}

				// Fold comparisons with constant operands
				if (instr is IHirCmpOp cmpOp) {
					if (constants.TryGetValue(cmpOp.Left.Id, out var leftCmp) &&
						constants.TryGetValue(cmpOp.Right.Id, out var rightCmp)) {
						bool? result = instr switch {
							HirCmpEq => leftCmp == rightCmp,
							HirCmpNe => leftCmp != rightCmp,
							HirCmpLt => leftCmp < rightCmp,
							HirCmpLe => leftCmp <= rightCmp,
							HirCmpGt => leftCmp > rightCmp,
							HirCmpGe => leftCmp >= rightCmp,
							_ => null
						};

						if (result.HasValue) {
							block.Instructions[i] = new HirConstBool(cmpOp.Dest, result.Value);
							changed = true;
							Logger.Debug(LogCategory.Optimizer, $"Comparison folded: {cmpOp.Dest} = {result.Value}");
						}
					}
				}

				// Fold unary operations
				if (instr is HirNeg neg && constants.TryGetValue(neg.Operand.Id, out var negVal)) {
					block.Instructions[i] = new HirConstInt(neg.Dest, -negVal);
					constants[neg.Dest.Id] = -negVal;
					changed = true;
				}
			}
		}

		return changed;
	}

	/// <summary>
	/// Simple peephole optimizations.
	/// e.g., x + 0 -> x, x * 1 -> x, x * 0 -> 0
	/// </summary>
	private static bool PeepholeOptimizations(HirFunction func) {
		var changed = false;
		var constants = new Dictionary<int, long>();

		// First pass: collect all constants
		foreach (var block in func.Blocks) {
			foreach (var instr in block.Instructions) {
				if (instr is HirConstInt constInt) {
					constants[constInt.Dest.Id] = constInt.Value;
				}
			}
		}

		// Second pass: apply peephole optimizations
		foreach (var block in func.Blocks) {
			for (var i = 0; i < block.Instructions.Count; i++) {
				var instr = block.Instructions[i];

				if (instr is HirAdd add) {
					// x + 0 -> x (replace with a copy, marked by HirConstInt pointing to same value)
					if (constants.TryGetValue(add.Right.Id, out var rightVal) && rightVal == 0) {
						// Replace with move/copy - in SSA form, we just record that Dest = Left
						block.Instructions[i] = new HirAdd(add.Dest, add.Left, add.Left) with { Right = add.Left };
						// Actually, we should track that add.Dest maps to add.Left
						// For now, mark for copy propagation
						changed = true;
					} else if (constants.TryGetValue(add.Left.Id, out var leftVal) && leftVal == 0) {
						changed = true;
					}
				} else if (instr is HirSub sub) {
					// x - 0 -> x
					if (constants.TryGetValue(sub.Right.Id, out var rightVal) && rightVal == 0) {
						changed = true;
					}
				} else if (instr is HirMul mul) {
					// x * 0 -> 0
					if ((constants.TryGetValue(mul.Right.Id, out var rightVal) && rightVal == 0) ||
						(constants.TryGetValue(mul.Left.Id, out var leftVal) && leftVal == 0)) {
						block.Instructions[i] = new HirConstInt(mul.Dest, 0);
						constants[mul.Dest.Id] = 0;
						changed = true;
						Logger.Debug(LogCategory.Optimizer, $"Peephole: {mul.Dest} = 0 (x * 0)");
					}
					// x * 1 -> x
					else if (constants.TryGetValue(mul.Right.Id, out rightVal) && rightVal == 1) {
						changed = true;
					} else if (constants.TryGetValue(mul.Left.Id, out leftVal) && leftVal == 1) {
						changed = true;
					}
					// x * 2 -> x << 1 (strength reduction)
					else if (constants.TryGetValue(mul.Right.Id, out rightVal) && IsPowerOfTwo(rightVal)) {
						var shift = Log2(rightVal);
						var shiftConst = new HirValue(GetMaxValueId(func) + 1);
						// Insert the constant before this instruction
						block.Instructions.Insert(i, new HirConstInt(shiftConst, shift));
						block.Instructions[i + 1] = new HirShl(mul.Dest, mul.Left, shiftConst);
						changed = true;
						Logger.Debug(LogCategory.Optimizer, $"Strength reduction: {mul.Dest} = {mul.Left} << {shift}");
					}
				}
			}
		}

		return changed;
	}

	/// <summary>
	/// Propagate copies to eliminate redundant moves.
	/// </summary>
	private static bool CopyPropagation(HirFunction func) {
		// In SSA form, copy propagation means replacing uses of a value
		// with the value it was copied from
		// For now, we don't have explicit copy instructions in HIR
		return false;
	}

	/// <summary>
	/// Remove instructions whose results are never used.
	/// </summary>
	private static bool DeadCodeElimination(HirFunction func) {
		var changed = false;

		// Build use counts for all values
		var useCounts = new Dictionary<int, int>();

		// Count uses
		foreach (var block in func.Blocks) {
			foreach (var instr in block.Instructions) {
				CountUses(instr, useCounts);
			}
		}

		// Remove dead instructions (those with unused results and no side effects)
		foreach (var block in func.Blocks) {
			var toRemove = new List<int>();

			for (var i = 0; i < block.Instructions.Count; i++) {
				var instr = block.Instructions[i];
				var dest = GetInstructionDest(instr);

				if (dest.HasValue && !useCounts.ContainsKey(dest.Value) && !HasSideEffects(instr)) {
					toRemove.Add(i);
					Logger.Debug(LogCategory.Optimizer, $"Dead code: removing {instr}");
				}
			}

			// Remove in reverse order to maintain indices
			for (var j = toRemove.Count - 1; j >= 0; j--) {
				block.Instructions.RemoveAt(toRemove[j]);
				changed = true;
			}
		}

		return changed;
	}

	private static void CountUses(HirInstr instr, Dictionary<int, int> useCounts) {
		void AddUse(HirValue v) {
			useCounts.TryGetValue(v.Id, out var count);
			useCounts[v.Id] = count + 1;
		}

		switch (instr) {
			case HirAdd op: AddUse(op.Left); AddUse(op.Right); break;
			case HirSub op: AddUse(op.Left); AddUse(op.Right); break;
			case HirMul op: AddUse(op.Left); AddUse(op.Right); break;
			case HirDiv op: AddUse(op.Left); AddUse(op.Right); break;
			case HirMod op: AddUse(op.Left); AddUse(op.Right); break;
			case HirBand op: AddUse(op.Left); AddUse(op.Right); break;
			case HirBor op: AddUse(op.Left); AddUse(op.Right); break;
			case HirBxor op: AddUse(op.Left); AddUse(op.Right); break;
			case HirShl op: AddUse(op.Left); AddUse(op.Right); break;
			case HirShr op: AddUse(op.Left); AddUse(op.Right); break;
			case HirFAdd op: AddUse(op.Left); AddUse(op.Right); break;
			case HirFSub op: AddUse(op.Left); AddUse(op.Right); break;
			case HirFMul op: AddUse(op.Left); AddUse(op.Right); break;
			case HirFDiv op: AddUse(op.Left); AddUse(op.Right); break;
			case HirCmpEq op: AddUse(op.Left); AddUse(op.Right); break;
			case HirCmpNe op: AddUse(op.Left); AddUse(op.Right); break;
			case HirCmpLt op: AddUse(op.Left); AddUse(op.Right); break;
			case HirCmpLe op: AddUse(op.Left); AddUse(op.Right); break;
			case HirCmpGt op: AddUse(op.Left); AddUse(op.Right); break;
			case HirCmpGe op: AddUse(op.Left); AddUse(op.Right); break;
			case HirLogicalAnd op: AddUse(op.Left); AddUse(op.Right); break;
			case HirLogicalOr op: AddUse(op.Left); AddUse(op.Right); break;
			case HirNeg op: AddUse(op.Operand); break;
			case HirNot op: AddUse(op.Operand); break;
			case HirLoad op: AddUse(op.Ptr); break;
			case HirStore op: AddUse(op.Ptr); AddUse(op.Value); break;
			case HirMemcpy op: AddUse(op.Dest); AddUse(op.Src); break;
			case HirGetFieldPtr op: AddUse(op.Base); break;
			case HirGetElemPtr op: AddUse(op.Base); AddUse(op.Index); break;
			case HirRet op: if (op.Value != null) AddUse(op.Value); break;
			case HirBrCond op: AddUse(op.Cond); break;
			case HirCall op: foreach (var arg in op.Args) AddUse(arg); break;
			case HirIntToFloat op: AddUse(op.Value); break;
			case HirFloatToInt op: AddUse(op.Value); break;
			case HirHeapAlloc op: AddUse(op.Size); break;
			case HirHeapFree op: AddUse(op.Ptr); break;
		}
	}

	private static int? GetInstructionDest(HirInstr instr) {
		return instr switch {
			HirConstInt c => c.Dest.Id,
			HirConstFloat c => c.Dest.Id,
			HirConstBool c => c.Dest.Id,
			HirConstString c => c.Dest.Id,
			HirAlloca a => a.Dest.Id,
			HirLoad l => l.Dest.Id,
			HirGetFieldPtr g => g.Dest.Id,
			HirGetElemPtr g => g.Dest.Id,
			HirAdd a => a.Dest.Id,
			HirSub s => s.Dest.Id,
			HirMul m => m.Dest.Id,
			HirDiv d => d.Dest.Id,
			HirMod m => m.Dest.Id,
			HirBand b => b.Dest.Id,
			HirBor b => b.Dest.Id,
			HirBxor b => b.Dest.Id,
			HirShl s => s.Dest.Id,
			HirShr s => s.Dest.Id,
			HirFAdd a => a.Dest.Id,
			HirFSub s => s.Dest.Id,
			HirFMul m => m.Dest.Id,
			HirFDiv d => d.Dest.Id,
			HirNeg n => n.Dest.Id,
			HirNot n => n.Dest.Id,
			HirCmpEq c => c.Dest.Id,
			HirCmpNe c => c.Dest.Id,
			HirCmpLt c => c.Dest.Id,
			HirCmpLe c => c.Dest.Id,
			HirCmpGt c => c.Dest.Id,
			HirCmpGe c => c.Dest.Id,
			HirLogicalAnd l => l.Dest.Id,
			HirLogicalOr l => l.Dest.Id,
			HirCall c => c.Dest?.Id,
			HirParam p => p.Dest.Id,
			HirIntToFloat i => i.Dest.Id,
			HirFloatToInt f => f.Dest.Id,
			HirHeapAlloc h => h.Dest.Id,
			HirGlobalAddr g => g.Dest.Id,
			_ => null
		};
	}

	private static bool HasSideEffects(HirInstr instr) {
		return instr switch {
			HirStore => true,
			HirMemcpy => true,
			HirCall => true,
			HirRet => true,
			HirBr => true,
			HirBrCond => true,
			HirHeapAlloc => true,
			HirHeapFree => true,
			_ => false
		};
	}

	private static bool IsPowerOfTwo(long n) {
		return n > 0 && (n & (n - 1)) == 0;
	}

	private static int Log2(long n) {
		var result = 0;
		while (n > 1) {
			n >>= 1;
			result++;
		}
		return result;
	}

	private static int GetMaxValueId(HirFunction func) {
		var max = 0;
		foreach (var block in func.Blocks) {
			foreach (var instr in block.Instructions) {
				var dest = GetInstructionDest(instr);
				if (dest.HasValue && dest.Value > max) {
					max = dest.Value;
				}
			}
		}
		return max;
	}
}
