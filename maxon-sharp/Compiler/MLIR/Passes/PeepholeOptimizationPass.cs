using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.X86;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Peephole optimization pass that cleans up redundant X86 instructions.
/// Runs after register allocation to eliminate:
/// - Self-moves (mov reg, reg where src == dst)
/// - Unnecessary register copies for constants (mov reg1, imm; mov reg2, reg1 -> mov reg2, imm)
/// </summary>
public sealed class PeepholeOptimizationPass : FunctionPass {
	public override string Name => "peephole-optimization";
	public override string Description => "Eliminates redundant X86 instructions";

	protected override bool RunOnFunction(MlirFunction func) {
		bool anyChanged = false;
		int removedSelfMoves = 0;
		int propagatedConstants = 0;

		Logger.Debug(LogCategory.Optimizer, $"peephole-optimization: processing {func.Name}");

		foreach (var block in func.Body.Blocks) {
			// Pattern 1: Remove self-moves (mov reg, reg)
			var (changed1, count1) = RemoveSelfMoves(block);
			anyChanged |= changed1;
			removedSelfMoves += count1;

			// Pattern 2: Propagate constants through single-use copies
			var (changed2, count2) = PropagateConstants(block);
			anyChanged |= changed2;
			propagatedConstants += count2;
		}

		if (removedSelfMoves > 0 || propagatedConstants > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: removed {removedSelfMoves} self-moves, propagated {propagatedConstants} constants");
		}

		return anyChanged;
	}

	/// <summary>
	/// Removes mov instructions where source and destination are the same register.
	/// </summary>
	private static (bool changed, int count) RemoveSelfMoves(MlirBlock block) {
		var toRemove = new List<MlirOperation>();

		foreach (var op in block.Operations) {
			if (op is MovOp mov && IsSameRegister(mov.Dst, mov.Src)) {
				toRemove.Add(op);
			}
		}

		foreach (var op in toRemove) {
			Logger.Trace(LogCategory.Optimizer, $"  removing self-move: {op}");
			block.Operations.Remove(op);
		}

		return (toRemove.Count > 0, toRemove.Count);
	}

	/// <summary>
	/// Propagates immediate values through register copies.
	/// Transforms: mov reg1, imm; mov reg2, reg1 -> mov reg2, imm (when reg1 has no other uses)
	/// </summary>
	private static (bool changed, int count) PropagateConstants(MlirBlock block) {
		bool changed = false;
		int count = 0;

		// Track: register -> immediate value mappings from mov reg, imm instructions
		var regToImm = new Dictionary<X86Register, (long value, int index)>();

		// Track which registers are used after their definition
		var regUseCounts = CountRegisterUses(block);

		// Process operations
		var ops = block.Operations.ToList();
		var toRemove = new HashSet<int>();

		for (int i = 0; i < ops.Count; i++) {
			if (ops[i] is MovOp mov) {
				// Check if this is a mov reg, imm
				if (mov.Dst is RegOperand dstReg && mov.Src is ImmOperand imm) {
					regToImm[dstReg.Register] = (imm.Value, i);
				}
				// Check if this is a mov destReg, srcReg where srcReg was loaded from imm
				else if (mov.Dst is RegOperand destReg && mov.Src is RegOperand srcReg) {
					if (regToImm.TryGetValue(srcReg.Register, out var immInfo)) {
						// Check if srcReg is only used once (this mov is its only use)
						if (regUseCounts.TryGetValue(srcReg.Register, out var useCount) && useCount == 1) {
							// Replace this mov with mov destReg, imm
							var newMov = new MovOp(destReg, new ImmOperand(immInfo.value));
							block.Operations[i] = newMov;

							// Mark the original mov reg, imm for removal
							toRemove.Add(immInfo.index);

							Logger.Trace(LogCategory.Optimizer, $"  propagating constant: mov {destReg}, {srcReg} -> mov {destReg}, {immInfo.value}");
							changed = true;
							count++;
						}
					}
				}
			}
		}

		// Remove the now-dead mov reg, imm instructions (in reverse order to preserve indices)
		foreach (var idx in toRemove.OrderByDescending(x => x)) {
			block.Operations.RemoveAt(idx);
		}

		return (changed, count);
	}

	/// <summary>
	/// Counts how many times each register is used as a source operand.
	/// </summary>
	private static Dictionary<X86Register, int> CountRegisterUses(MlirBlock block) {
		var counts = new Dictionary<X86Register, int>();

		foreach (var op in block.Operations) {
			if (op is X86Op x86Op) {
				// Check all operands except the first one (destination) for register uses
				for (int i = 1; i < x86Op.X86Operands.Count; i++) {
					CountOperandUses(x86Op.X86Operands[i], counts);
				}

				// For some ops like cmp, test, both operands are sources
				if (op is CmpOp or TestOp && x86Op.X86Operands.Count > 0) {
					CountOperandUses(x86Op.X86Operands[0], counts);
				}
			}
		}

		return counts;
	}

	private static void CountOperandUses(X86Operand operand, Dictionary<X86Register, int> counts) {
		if (operand is RegOperand reg) {
			counts[reg.Register] = counts.GetValueOrDefault(reg.Register) + 1;
		} else if (operand is MemOperand mem) {
			if (mem.Base is RegOperand baseReg) {
				counts[baseReg.Register] = counts.GetValueOrDefault(baseReg.Register) + 1;
			}
			if (mem.Index is RegOperand indexReg) {
				counts[indexReg.Register] = counts.GetValueOrDefault(indexReg.Register) + 1;
			}
		}
	}

	/// <summary>
	/// Checks if two operands refer to the same physical register.
	/// </summary>
	private static bool IsSameRegister(X86Operand a, X86Operand b) {
		return a is RegOperand regA && b is RegOperand regB && regA.Register == regB.Register;
	}
}
