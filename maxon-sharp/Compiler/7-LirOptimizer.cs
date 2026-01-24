namespace MaxonSharp.Compiler;

/// <summary>
/// Optimizes LIR modules before code generation.
/// Performs low-level, machine-oriented optimizations.
/// </summary>
public static class LirOptimizer {
	/// <summary>
	/// Run all LIR optimization passes on the module.
	/// </summary>
	public static void Optimize(LirModule module) {
		Logger.Debug(LogCategory.Optimizer, "Starting LIR optimization");

		foreach (var func in module.Functions) {
			OptimizeFunction(func);
		}

		Logger.Debug(LogCategory.Optimizer, "LIR optimization complete");
	}

	private static void OptimizeFunction(LirFunction func) {
		Logger.Debug(LogCategory.Optimizer, $"Optimizing LIR function: {func.Name}");

		bool changed;
		var iterations = 0;
		const int maxIterations = 10;

		do {
			changed = false;
			iterations++;

			changed |= CommonSubexpressionElimination(func);
			changed |= CopyPropagation(func);
			changed |= DeadCodeElimination(func);
			changed |= PeepholeOptimizations(func);

		} while (changed && iterations < maxIterations);

		Logger.Debug(LogCategory.Optimizer, $"LIR function {func.Name}: {iterations} optimization iterations");
	}

	/// <summary>
	/// Eliminate common subexpressions by reusing previously computed values.
	/// </summary>
	private static bool CommonSubexpressionElimination(LirFunction func) {
		var changed = false;

		foreach (var block in func.Blocks) {
			// Track available expressions: (opcode, left, right) -> result vreg
			var available = new Dictionary<string, LirVReg>();

			for (var i = 0; i < block.Instructions.Count; i++) {
				var instr = block.Instructions[i];

				if (instr is ILirBinaryOp binOp) {
					var key = GetExpressionKey(instr);
					if (key != null && available.TryGetValue(key, out var existingResult)) {
						// Replace with a move from the existing result
						block.Instructions[i] = new LirMov(binOp.Dest, existingResult);
						changed = true;
						Logger.Debug(LogCategory.Optimizer, $"CSE: {binOp.Dest} = mov {existingResult}");
					} else if (key != null) {
						available[key] = binOp.Dest;
					}
				}
			}
		}

		return changed;
	}

	/// <summary>
	/// Propagate copies to eliminate redundant moves.
	/// e.g., v1 = mov v0; use v1 -> use v0
	/// </summary>
	private static bool CopyPropagation(LirFunction func) {
		var changed = false;

		foreach (var block in func.Blocks) {
			// Build copy chains: dest -> source
			var copies = new Dictionary<int, LirValue>();

			foreach (var instr in block.Instructions) {
				if (instr is LirMov mov && mov.Src is LirVReg srcReg) {
					copies[mov.Dest.Id] = srcReg;
				}
			}

			if (copies.Count == 0) continue;

			// Replace uses of copied values
			for (var i = 0; i < block.Instructions.Count; i++) {
				var instr = block.Instructions[i];
				var replaced = PropagateInInstruction(instr, copies);
				if (replaced != null) {
					block.Instructions[i] = replaced;
					changed = true;
				}
			}
		}

		return changed;
	}

	/// <summary>
	/// Remove instructions whose results are never used.
	/// </summary>
	private static bool DeadCodeElimination(LirFunction func) {
		var changed = false;

		// Build use counts
		var useCounts = new Dictionary<int, int>();

		foreach (var block in func.Blocks) {
			foreach (var instr in block.Instructions) {
				CountUses(instr, useCounts);
			}
		}

		// Remove dead instructions
		foreach (var block in func.Blocks) {
			var toRemove = new List<int>();

			for (var i = 0; i < block.Instructions.Count; i++) {
				var instr = block.Instructions[i];
				var dest = GetInstructionDest(instr);

				if (dest.HasValue && !useCounts.ContainsKey(dest.Value) && !HasSideEffects(instr)) {
					toRemove.Add(i);
					Logger.Debug(LogCategory.Optimizer, $"LIR dead code: removing {instr}");
				}
			}

			for (var j = toRemove.Count - 1; j >= 0; j--) {
				block.Instructions.RemoveAt(toRemove[j]);
				changed = true;
			}
		}

		return changed;
	}

	/// <summary>
	/// Peephole optimizations for LIR.
	/// </summary>
	private static bool PeepholeOptimizations(LirFunction func) {
		var changed = false;

		foreach (var block in func.Blocks) {
			// Collect immediate values
			var immediates = new Dictionary<int, long>();
			foreach (var instr in block.Instructions) {
				if (instr is LirMov mov && mov.Src is LirImmediate imm) {
					immediates[mov.Dest.Id] = imm.Value;
				}
			}

			for (var i = 0; i < block.Instructions.Count; i++) {
				var instr = block.Instructions[i];

				// Strength reduction: x * 2 -> x << 1
				if (instr is LirIMul mul) {
					long? rightVal = null;
					if (mul.Right is LirImmediate rightImm) {
						rightVal = rightImm.Value;
					} else if (mul.Right is LirVReg rightReg && immediates.TryGetValue(rightReg.Id, out var v)) {
						rightVal = v;
					}

					if (rightVal.HasValue && IsPowerOfTwo(rightVal.Value)) {
						var shift = Log2(rightVal.Value);
						block.Instructions[i] = new LirShl(mul.Dest, mul.Left, new LirImmediate(shift));
						changed = true;
						Logger.Debug(LogCategory.Optimizer, $"LIR strength reduction: {mul.Dest} = shl {mul.Left}, {shift}");
					}
				}

				// Identity elimination: x + 0 -> x
				if (instr is LirAdd add) {
					if (add.Right is LirImmediate addImm && addImm.Value == 0) {
						block.Instructions[i] = new LirMov(add.Dest, add.Left);
						changed = true;
					}
				}

				// Identity elimination: x - 0 -> x
				if (instr is LirSub sub) {
					if (sub.Right is LirImmediate subImm && subImm.Value == 0) {
						block.Instructions[i] = new LirMov(sub.Dest, sub.Left);
						changed = true;
					}
				}

				// Identity elimination: x * 1 -> x
				if (instr is LirIMul mul1) {
					if (mul1.Right is LirImmediate mul1Imm && mul1Imm.Value == 1) {
						block.Instructions[i] = new LirMov(mul1.Dest, mul1.Left);
						changed = true;
					}
				}

				// Zero multiplication: x * 0 -> 0
				if (instr is LirIMul mul0) {
					if ((mul0.Right is LirImmediate mul0Imm && mul0Imm.Value == 0) ||
						(mul0.Left is LirImmediate mul0ImmL && mul0ImmL.Value == 0)) {
						block.Instructions[i] = new LirMov(mul0.Dest, new LirImmediate(0));
						changed = true;
					}
				}

				// Redundant move elimination: v1 = mov v1 (no-op)
				if (instr is LirMov selfMov && selfMov.Src is LirVReg srcReg && srcReg.Id == selfMov.Dest.Id) {
					block.Instructions.RemoveAt(i);
					i--;
					changed = true;
				}
			}
		}

		return changed;
	}

	private static string? GetExpressionKey(LirInstr instr) {
		return instr switch {
			LirAdd op => $"add:{FormatValue(op.Left)}:{FormatValue(op.Right)}",
			LirSub op => $"sub:{FormatValue(op.Left)}:{FormatValue(op.Right)}",
			LirIMul op => $"imul:{FormatValue(op.Left)}:{FormatValue(op.Right)}",
			LirAnd op => $"and:{FormatValue(op.Left)}:{FormatValue(op.Right)}",
			LirOr op => $"or:{FormatValue(op.Left)}:{FormatValue(op.Right)}",
			LirXor op => $"xor:{FormatValue(op.Left)}:{FormatValue(op.Right)}",
			_ => null
		};
	}

	private static string FormatValue(LirValue value) {
		return value switch {
			LirVReg v => $"v{v.Id}",
			LirImmediate i => $"#{i.Value}",
			_ => value.ToString() ?? "?"
		};
	}

	private static LirInstr? PropagateInInstruction(LirInstr instr, Dictionary<int, LirValue> copies) {
		LirValue Propagate(LirValue v) {
			if (v is LirVReg reg && copies.TryGetValue(reg.Id, out var src)) {
				return src;
			}
			return v;
		}

		switch (instr) {
			case LirAdd op: {
					var newLeft = Propagate(op.Left);
					var newRight = Propagate(op.Right);
					if (newLeft != op.Left || newRight != op.Right)
						return new LirAdd(op.Dest, newLeft, newRight);
					return null;
				}
			case LirSub op: {
					var newLeft = Propagate(op.Left);
					var newRight = Propagate(op.Right);
					if (newLeft != op.Left || newRight != op.Right)
						return new LirSub(op.Dest, newLeft, newRight);
					return null;
				}
			case LirIMul op: {
					var newLeft = Propagate(op.Left);
					var newRight = Propagate(op.Right);
					if (newLeft != op.Left || newRight != op.Right)
						return new LirIMul(op.Dest, newLeft, newRight);
					return null;
				}
			case LirCmp op: {
					var newLeft = Propagate(op.Left);
					var newRight = Propagate(op.Right);
					if (newLeft != op.Left || newRight != op.Right)
						return new LirCmp(newLeft, newRight);
					return null;
				}
			case LirRet op when op.Value != null: {
					var newVal = Propagate(op.Value);
					if (newVal != op.Value)
						return new LirRet(newVal);
					return null;
				}
			default:
				return null;
		}
	}

	private static void CountUses(LirInstr instr, Dictionary<int, int> useCounts) {
		void AddUse(LirValue v) {
			if (v is LirVReg reg) {
				useCounts.TryGetValue(reg.Id, out var count);
				useCounts[reg.Id] = count + 1;
			}
		}

		switch (instr) {
			case LirMov op: AddUse(op.Src); break;
			case LirLoad op: AddUse(op.Ptr); break;
			case LirStore op: AddUse(op.Ptr); AddUse(op.Value); break;
			case LirMemcpy op: AddUse(op.Dest); AddUse(op.Src); break;
			case LirLea op: AddUse(op.Addr); break;
			case LirAdd op: AddUse(op.Left); AddUse(op.Right); break;
			case LirSub op: AddUse(op.Left); AddUse(op.Right); break;
			case LirIMul op: AddUse(op.Left); AddUse(op.Right); break;
			case LirIDiv op: AddUse(op.Left); AddUse(op.Right); break;
			case LirMod op: AddUse(op.Left); AddUse(op.Right); break;
			case LirNeg op: AddUse(op.Src); break;
			case LirAnd op: AddUse(op.Left); AddUse(op.Right); break;
			case LirOr op: AddUse(op.Left); AddUse(op.Right); break;
			case LirXor op: AddUse(op.Left); AddUse(op.Right); break;
			case LirNot op: AddUse(op.Src); break;
			case LirShl op: AddUse(op.Left); AddUse(op.Right); break;
			case LirShr op: AddUse(op.Left); AddUse(op.Right); break;
			case LirFAdd op: AddUse(op.Left); AddUse(op.Right); break;
			case LirFSub op: AddUse(op.Left); AddUse(op.Right); break;
			case LirFMul op: AddUse(op.Left); AddUse(op.Right); break;
			case LirFDiv op: AddUse(op.Left); AddUse(op.Right); break;
			case LirFNeg op: AddUse(op.Src); break;
			case LirCmp op: AddUse(op.Left); AddUse(op.Right); break;
			case LirFCmp op: AddUse(op.Left); AddUse(op.Right); break;
			case LirRet op: if (op.Value != null) AddUse(op.Value); break;
			case LirCall op: foreach (var arg in op.Args) AddUse(arg); break;
			case LirPush op: AddUse(op.Value); break;
			case LirIntToFloat op: AddUse(op.Src); break;
			case LirFloatToInt op: AddUse(op.Src); break;
			case LirSignExtend op: AddUse(op.Src); break;
			case LirZeroExtend op: AddUse(op.Src); break;
		}
	}

	private static int? GetInstructionDest(LirInstr instr) {
		return instr switch {
			LirMov op => op.Dest.Id,
			LirLoad op => op.Dest.Id,
			LirLea op => op.Dest.Id,
			LirAdd op => op.Dest.Id,
			LirSub op => op.Dest.Id,
			LirIMul op => op.Dest.Id,
			LirIDiv op => op.Dest.Id,
			LirMod op => op.Dest.Id,
			LirNeg op => op.Dest.Id,
			LirAnd op => op.Dest.Id,
			LirOr op => op.Dest.Id,
			LirXor op => op.Dest.Id,
			LirNot op => op.Dest.Id,
			LirShl op => op.Dest.Id,
			LirShr op => op.Dest.Id,
			LirFAdd op => op.Dest.Id,
			LirFSub op => op.Dest.Id,
			LirFMul op => op.Dest.Id,
			LirFDiv op => op.Dest.Id,
			LirFNeg op => op.Dest.Id,
			LirSetCC op => op.Dest.Id,
			LirCall op => op.Dest?.Id,
			LirPop op => op.Dest.Id,
			LirIntToFloat op => op.Dest.Id,
			LirFloatToInt op => op.Dest.Id,
			LirSignExtend op => op.Dest.Id,
			LirZeroExtend op => op.Dest.Id,
			LirAddressOf op => op.Dest.Id,
			_ => null
		};
	}

	private static bool HasSideEffects(LirInstr instr) {
		return instr switch {
			LirStore => true,
			LirMemcpy => true,
			LirCall => true,
			LirRet => true,
			LirJmp => true,
			LirJmpCC => true,
			LirPush => true,
			LirCmp => true,  // Sets flags
			LirFCmp => true, // Sets flags
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
}
