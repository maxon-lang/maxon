using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.X86;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Allocates virtual registers to physical x86-64 registers.
/// Uses a simple linear scan approach for now.
/// </summary>
public sealed class RegisterAllocationPass : FunctionPass {
	public override string Name => "register-allocation";
	public override string Description => "Allocates virtual registers to physical x86-64 registers";

	// Caller-saved (volatile) registers we can use freely
	private static readonly X86Register[] GeneralRegs = [
		X86Register.RAX,
		X86Register.RCX,
		X86Register.RDX,
		X86Register.R8,
		X86Register.R9,
		X86Register.R10,
		X86Register.R11,
		// Callee-saved registers (would need save/restore)
		// X86Register.RBX,
		// X86Register.R12,
		// X86Register.R13,
		// X86Register.R14,
		// X86Register.R15,
	];

	// SSE registers for floating point
	private static readonly X86Register[] FloatRegs = [
		X86Register.XMM0,
		X86Register.XMM1,
		X86Register.XMM2,
		X86Register.XMM3,
		X86Register.XMM4,
		X86Register.XMM5,
	];

	protected override bool RunOnFunction(MlirFunction func) {
		bool changed = false;
		int totalAllocations = 0;

		Logger.Debug(LogCategory.RegAlloc, $"register-allocation: processing {func.Name}");

		foreach (var block in func.Body.Blocks) {
			var (blockChanged, allocCount) = AllocateBlock(block);
			changed |= blockChanged;
			totalAllocations += allocCount;
		}

		if (totalAllocations > 0) {
			Logger.Debug(LogCategory.RegAlloc, $"  {func.Name}: allocated {totalAllocations} virtual registers");
		}
		return changed;
	}

	private (bool changed, int allocationCount) AllocateBlock(MlirBlock block) {
		// Map virtual register IDs to physical registers
		var allocation = new Dictionary<int, X86Register>();
		var nextGenReg = 0;
		var nextFloatReg = 0;

		// Replace operations with allocated versions
		var newOps = new List<MlirOperation>();
		bool changed = false;

		foreach (var op in block.Operations) {
			if (op is X86Op x86Op) {
				var allocated = AllocateOp(x86Op, allocation, ref nextGenReg, ref nextFloatReg);
				newOps.Add(allocated);
				changed = true;
			} else {
				newOps.Add(op);
			}
		}

		// Log allocations at trace level
		foreach (var (vregId, physReg) in allocation) {
			Logger.Trace(LogCategory.RegAlloc, $"  v{vregId} -> {physReg}");
		}

		// Replace block operations
		block.Operations.Clear();
		foreach (var op in newOps) {
			block.Operations.Add(op);
		}
		return (changed, allocation.Count);
	}

	private X86Op AllocateOp(X86Op op, Dictionary<int, X86Register> allocation, ref int nextGenReg, ref int nextFloatReg) {
		return op switch {
			MovOp mov => new MovOp(
				AllocateOperand(mov.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(mov.Src, allocation, ref nextGenReg, ref nextFloatReg)
			),
			Dialects.X86.AddOp add => new Dialects.X86.AddOp(
				AllocateOperand(add.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(add.Src, allocation, ref nextGenReg, ref nextFloatReg)
			),
			SubOp sub => new SubOp(
				AllocateOperand(sub.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(sub.Src, allocation, ref nextGenReg, ref nextFloatReg)
			),
			ImulOp imul => imul.Src2 is not null
				? new ImulOp(
					AllocateOperand(imul.Dst, allocation, ref nextGenReg, ref nextFloatReg),
					AllocateOperand(imul.Src1, allocation, ref nextGenReg, ref nextFloatReg),
					AllocateOperand(imul.Src2, allocation, ref nextGenReg, ref nextFloatReg)
				)
				: new ImulOp(
					AllocateOperand(imul.Dst, allocation, ref nextGenReg, ref nextFloatReg),
					AllocateOperand(imul.Src1, allocation, ref nextGenReg, ref nextFloatReg)
				),
			IdivOp idiv => new IdivOp(
				AllocateOperand(idiv.Divisor, allocation, ref nextGenReg, ref nextFloatReg)
			),
			CmpOp cmp => new CmpOp(
				AllocateOperand(cmp.Left, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(cmp.Right, allocation, ref nextGenReg, ref nextFloatReg)
			),
			TestOp test => new TestOp(
				AllocateOperand(test.Left, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(test.Right, allocation, ref nextGenReg, ref nextFloatReg)
			),
			SetccOp setcc => new SetccOp(
				setcc.Condition,
				AllocateOperand(setcc.Dst, allocation, ref nextGenReg, ref nextFloatReg)
			),
			PushOp push => new PushOp(
				AllocateOperand(push.Src, allocation, ref nextGenReg, ref nextFloatReg)
			),
			PopOp pop => new PopOp(
				AllocateOperand(pop.Dst, allocation, ref nextGenReg, ref nextFloatReg)
			),
			LeaOp lea => new LeaOp(
				AllocateOperand(lea.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(lea.Src, allocation, ref nextGenReg, ref nextFloatReg)
			),
			LeaGlobalOp leaGlobal => new LeaGlobalOp(
				AllocateOperand(leaGlobal.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				leaGlobal.GlobalName
			),
			ShlOp shl => new ShlOp(
				AllocateOperand(shl.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(shl.Count, allocation, ref nextGenReg, ref nextFloatReg)
			),
			CallOp call => call, // Labels don't need allocation
			JmpOp jmp => jmp,
			JccOp jcc => jcc,
			RetOp ret => ret,
			LabelOp label => label,
			CdqOp cdq => cdq,
			PrologueOp prologue => prologue, // No allocation needed
			EpilogueOp epilogue => epilogue, // No allocation needed
											 // SSE instructions
			MovsdOp movsd => new MovsdOp(
				AllocateOperand(movsd.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(movsd.Src, allocation, ref nextGenReg, ref nextFloatReg)
			),
			AddsdOp addsd => new AddsdOp(
				AllocateOperand(addsd.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(addsd.Src, allocation, ref nextGenReg, ref nextFloatReg)
			),
			SubsdOp subsd => new SubsdOp(
				AllocateOperand(subsd.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(subsd.Src, allocation, ref nextGenReg, ref nextFloatReg)
			),
			MulsdOp mulsd => new MulsdOp(
				AllocateOperand(mulsd.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(mulsd.Src, allocation, ref nextGenReg, ref nextFloatReg)
			),
			DivsdOp divsd => new DivsdOp(
				AllocateOperand(divsd.Dst, allocation, ref nextGenReg, ref nextFloatReg),
				AllocateOperand(divsd.Src, allocation, ref nextGenReg, ref nextFloatReg)
			),
			_ => op
		};
	}

	private X86Operand AllocateOperand(X86Operand operand, Dictionary<int, X86Register> allocation, ref int nextGenReg, ref int nextFloatReg) {
		if (operand is VRegOperand vreg) {
			if (!allocation.TryGetValue(vreg.Id, out var physReg)) {
				// Allocate a new physical register
				// TODO: Use float regs for float values based on type info
				if (nextGenReg >= GeneralRegs.Length) {
					throw new InvalidOperationException($"Ran out of registers for vreg v{vreg.Id}. Spilling not yet implemented.");
				}
				physReg = GeneralRegs[nextGenReg++];
				allocation[vreg.Id] = physReg;
			}
			return new RegOperand(physReg);
		}

		if (operand is MemOperand mem) {
			return AllocateMemOperand(mem, allocation, ref nextGenReg, ref nextFloatReg);
		}

		return operand;
	}

	private MemOperand AllocateMemOperand(MemOperand mem, Dictionary<int, X86Register> allocation, ref int nextGenReg, ref int nextFloatReg) {
		return new MemOperand(
			mem.Base is not null ? AllocateOperand(mem.Base, allocation, ref nextGenReg, ref nextFloatReg) : null,
			mem.Index is not null ? AllocateOperand(mem.Index, allocation, ref nextGenReg, ref nextFloatReg) : null,
			mem.Scale,
			mem.Displacement,
			mem.Size
		);
	}
}
