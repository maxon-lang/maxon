using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Peephole optimization pass that cleans up redundant X86 instructions.
/// Runs after register allocation to eliminate:
/// - Self-moves (mov reg, reg where src == dst)
/// - Unnecessary register copies for constants (mov reg1, imm; mov reg2, reg1 -> mov reg2, imm)
/// - Overwritten values (mov reg, X; mov reg, Y -> mov reg, Y when X is not used)
/// </summary>
public sealed class PeepholeOptimizationPass : FunctionPass {
	public override string Name => "peephole-optimization";
	public override string Description => "Eliminates redundant X86 instructions";

	protected override bool RunOnFunction(MlirFunction func) {
		bool anyChanged = false;
		int removedSelfMoves = 0;
		int propagatedConstants = 0;
		int removedDeadMoves = 0;
		Logger.Debug(LogCategory.Optimizer, $"peephole-optimization: processing {func.Name}");

		// Block-level optimization: merge blocks connected by unconditional jumps
		var (changed0, count0) = MergeBlocks(func);
		anyChanged |= changed0;
		int mergedBlocks = count0;

		foreach (var block in func.Body.Blocks) {
			// Pattern 1: Remove self-moves (mov reg, reg)
			var (changed1, count1) = RemoveSelfMoves(block);
			anyChanged |= changed1;
			removedSelfMoves += count1;

			// Pattern 2: Propagate constants through single-use copies
			var (changed2, count2) = PropagateConstants(block);
			anyChanged |= changed2;
			propagatedConstants += count2;

			// Pattern 3: Remove dead moves (mov reg, X immediately followed by mov reg, Y)
			var (changed3, count3) = RemoveDeadMoves(block);
			anyChanged |= changed3;
			removedDeadMoves += count3;
		}

		if (removedSelfMoves > 0 || propagatedConstants > 0 || removedDeadMoves > 0 || mergedBlocks > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: merged {mergedBlocks} blocks, removed {removedSelfMoves} self-moves, propagated {propagatedConstants} constants, removed {removedDeadMoves} dead moves");
		}

		return anyChanged;
	}

	/// <summary>
	/// Merges blocks when a block ends with an unconditional jump to a block that has a single predecessor.
	/// This eliminates unnecessary jumps and simplifies the CFG.
	/// </summary>
	private static (bool changed, int count) MergeBlocks(MlirFunction func) {
		bool changed = false;
		int count = 0;

		// Build predecessor map - counts how many blocks jump TO each block
		// Note: we scan ALL control flow ops in each block, not just terminators,
		// because inline .args blocks contain JmpOps that are not the block terminator
		var predecessorCount = new Dictionary<string, int>();
		foreach (var block in func.Body.Blocks) {
			predecessorCount[block.Name] = 0;
		}
		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				if (op is JmpOp jmp) {
					if (predecessorCount.TryGetValue(jmp.Target, out int value)) {
						predecessorCount[jmp.Target] = ++value;
					}
				} else if (op is JccOp jcc) {
					if (predecessorCount.TryGetValue(jcc.TrueTarget, out int value)) {
						predecessorCount[jcc.TrueTarget] = ++value;
					}
					if (predecessorCount.ContainsKey(jcc.FalseTarget)) {
						// Don't double-count if both branches go to same target
						if (jcc.TrueTarget != jcc.FalseTarget) {
							predecessorCount[jcc.FalseTarget]++;
						}
					}
				}
			}
		}

		// Find and merge blocks
		bool madeChange;
		do {
			madeChange = false;
			for (int i = 0; i < func.Body.Blocks.Count; i++) {
				var block = func.Body.Blocks[i];

				// Check if this block ends with an unconditional jump
				if (block.Terminator is not JmpOp jmp) continue;

				// Find the target block
				var targetBlock = func.Body.Blocks.FirstOrDefault(b => b.Name == jmp.Target);
				if (targetBlock is null || targetBlock == block) continue;

				// Check if target has exactly one predecessor (this block)
				if (!predecessorCount.TryGetValue(targetBlock.Name, out var predCount) || predCount != 1)
					continue;

				// Check target block has no arguments (block parameters)
				if (targetBlock.Arguments.Count > 0) continue;

				// Safety: verify target block is not referenced by name from anywhere else
				// This includes LabelOps and any JmpOp/JccOp that references the block by name
				bool hasOtherReference = func.Body.Blocks.Any(b => {
					if (b == block || b == targetBlock) return false;
					foreach (var op in b.Operations) {
						if (op is LabelOp labelOp && labelOp.Name == targetBlock.Name)
							return true;
						if (op is JmpOp jmpOp && jmpOp.Target == targetBlock.Name)
							return true;
						if (op is JccOp jccOp && (jccOp.TrueTarget == targetBlock.Name || jccOp.FalseTarget == targetBlock.Name))
							return true;
					}
					return false;
				});
				if (hasOtherReference) continue;

				// Merge: remove the jmp from current block and append target block's ops
				Logger.Trace(LogCategory.Optimizer, $"  merging block {block.Name} -> {targetBlock.Name}");

				// Remove the jmp instruction
				block.Operations.Remove(jmp);

				// Add all operations from the target block
				foreach (var op in targetBlock.Operations) {
					block.Operations.Add(op);
					op.ParentBlock = block;
				}

				// Update predecessor counts for target's successors
				if (targetBlock.Terminator is JmpOp targetJmp) {
					// The jump target now has one fewer predecessor (target is gone)
					// but same count since current block now jumps there
					// Net effect: no change in predecessor count
				} else if (targetBlock.Terminator is JccOp targetJcc) {
					// Same logic - predecessor counts stay the same
				}

				// Remove the target block from predecessor count tracking
				predecessorCount.Remove(targetBlock.Name);

				// Remove the target block from the function
				func.Body.Blocks.Remove(targetBlock);

				madeChange = true;
				changed = true;
				count++;
				break; // Restart from beginning after modification
			}
		} while (madeChange);

		return (changed, count);
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
	/// Transforms: mov reg1, imm; mov reg2, reg1 -> mov reg2, imm (when reg1 has no other uses in the block)
	/// NOTE: This only removes the mov reg, imm if the register is not used elsewhere in the block.
	/// If the register is live out of the block (used in other blocks), we keep both movs.
	/// </summary>
	private static (bool changed, int count) PropagateConstants(MlirBlock block) {
		bool changed = false;
		int count = 0;

		// Track: register -> immediate value mappings from mov reg, imm instructions
		var regToImm = new Dictionary<X86Register, (long value, int index)>();

		// Track which registers are used after their definition IN THIS BLOCK
		var regUseCounts = CountRegisterUses(block);

		// Track which registers might be live-out (used in successor blocks)
		// For safety, don't remove the defining mov if the register might be used later
		var liveOutRegs = GetPotentiallyLiveOutRegisters(block);

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
						// Check if srcReg is only used once in this block (this mov is its only use)
						// AND the register is not potentially live-out (used in other blocks)
						if (regUseCounts.TryGetValue(srcReg.Register, out var useCount) && useCount == 1
							&& !liveOutRegs.Contains(srcReg.Register)) {
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
	/// Returns the set of registers that are potentially live-out from this block.
	/// A register is live-out if it might be used by a successor block.
	/// For safety, we consider any register that is defined but not consumed within
	/// the same block as potentially live-out, if the block has successors.
	/// </summary>
	private static HashSet<X86Register> GetPotentiallyLiveOutRegisters(MlirBlock block) {
		var liveOut = new HashSet<X86Register>();

		// If block ends with a return, nothing is live-out (function is ending)
		if (block.Terminator is RetOp) {
			return liveOut;
		}

		// Collect all registers that are defined in this block
		var defined = new HashSet<X86Register>();
		foreach (var op in block.Operations) {
			if (op is X86Op x86Op && x86Op.X86Operands.Count > 0) {
				// Most X86 ops have destination as first operand
				if (x86Op.X86Operands[0] is RegOperand dstReg) {
					// For cmp/test, first operand is not a destination
					if (op is not CmpOp and not TestOp) {
						defined.Add(dstReg.Register);
					}
				}
			}
		}

		// All defined registers are potentially live-out since the block has successors
		return defined;
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
	/// Removes mov instructions whose destination is overwritten before being read.
	/// Pattern: mov reg, X; mov reg, Y -> mov reg, Y (when reg is not used between the two movs)
	/// </summary>
	private static (bool changed, int count) RemoveDeadMoves(MlirBlock block) {
		var toRemove = new List<int>();
		var ops = block.Operations.ToList();

		for (int i = 0; i < ops.Count - 1; i++) {
			if (ops[i] is not MovOp mov1 || mov1.Dst is not RegOperand dstReg1)
				continue;

			// Look ahead to find if this register is overwritten before being read
			for (int j = i + 1; j < ops.Count; j++) {
				var op = ops[j];

				// Check if this operation reads the register
				if (OperationReadsRegister(op, dstReg1.Register)) {
					break; // Register is used, can't remove the first mov
				}

				// Check if this operation writes to the same register
				if (op is MovOp mov2 && mov2.Dst is RegOperand dstReg2 && dstReg2.Register == dstReg1.Register) {
					// The first mov is dead - its value is overwritten
					toRemove.Add(i);
					Logger.Trace(LogCategory.Optimizer, $"  removing dead move to {dstReg1.Register} (overwritten at index {j})");
					break;
				}

				// If this is a terminator or side-effectful op that could observe the register, stop
				if (op.IsTerminator || op is X86CallOp or RetOp or PrologueOp or EpilogueOp) {
					break;
				}
			}
		}

		// Remove in reverse order to preserve indices
		foreach (var idx in toRemove.OrderByDescending(x => x)) {
			block.Operations.RemoveAt(idx);
		}

		return (toRemove.Count > 0, toRemove.Count);
	}

	/// <summary>
	/// Checks if an operation reads from the specified register.
	/// </summary>
	private static bool OperationReadsRegister(MlirOperation op, X86Register reg) {
		if (op is not X86Op x86Op) return false;

		// cdq implicitly reads RAX (sign extends EAX to EDX:EAX)
		if (op is CdqOp && reg == X86Register.RAX) {
			return true;
		}

		// idiv implicitly reads RAX and RDX (dividend is RDX:RAX)
		// and also reads its explicit operand (the divisor)
		if (op is IdivOp idiv) {
			if (reg == X86Register.RAX || reg == X86Register.RDX) {
				return true;
			}
			// The divisor operand is read, not written
			if (OperandReadsRegister(idiv.Divisor, reg)) {
				return true;
			}
		}

		// For mov, only the source is read (index 1)
		if (op is MovOp mov) {
			return OperandReadsRegister(mov.Src, reg);
		}

		// Push reads its source operand (index 0)
		if (op is PushOp push) {
			return OperandReadsRegister(push.Src, reg);
		}

		// For most other ops, check all operands except the first (destination)
		for (int i = 1; i < x86Op.X86Operands.Count; i++) {
			if (OperandReadsRegister(x86Op.X86Operands[i], reg))
				return true;
		}

		// For cmp/test, both operands are sources
		if (op is CmpOp or TestOp && x86Op.X86Operands.Count > 0) {
			if (OperandReadsRegister(x86Op.X86Operands[0], reg))
				return true;
		}

		return false;
	}

	private static bool OperandReadsRegister(X86Operand operand, X86Register reg) {
		if (operand is RegOperand r && r.Register == reg)
			return true;
		if (operand is MemOperand mem) {
			if (mem.Base is RegOperand baseReg && baseReg.Register == reg)
				return true;
			if (mem.Index is RegOperand indexReg && indexReg.Register == reg)
				return true;
		}
		return false;
	}

	/// <summary>
	/// Checks if two operands refer to the same physical register.
	/// </summary>
	private static bool IsSameRegister(X86Operand a, X86Operand b) {
		return a is RegOperand regA && b is RegOperand regB && regA.Register == regB.Register;
	}
}
