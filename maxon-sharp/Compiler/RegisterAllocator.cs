namespace MaxonSharp.Compiler;

public enum Reg {
	Rax = 0,
	Rcx = 1,
	Rdx = 2,
	Rbx = 3,
	Rsp = 4,
	Rbp = 5,
	Rsi = 6,
	Rdi = 7,
	R8 = 8,
	R9 = 9,
	R10 = 10,
	R11 = 11,
	R12 = 12,
	R13 = 13,
	R14 = 14,
	R15 = 15
}

/// <summary>
/// Spill-everything register allocator.
/// Every virtual register is assigned a stack slot.
/// Operations load from stack, compute in temp registers, store back to stack.
/// </summary>
public class RegisterAllocator {
	// Scratch registers for computation (caller-saved, not used for parameters)
	public static readonly Reg[] ScratchRegs = [Reg.Rax, Reg.R10, Reg.R11];

	// Windows x64 ABI: first 4 integer args in RCX, RDX, R8, R9
	public static readonly Reg[] ParamRegs = [Reg.Rcx, Reg.Rdx, Reg.R8, Reg.R9];

	// Windows x64 ABI: first 4 float args in XMM0-XMM3
	public static readonly int[] FloatParamRegs = [0, 1, 2, 3];

	public class Allocation {
		public Dictionary<int, int> VRegToStackOffset { get; } = [];
		public int StackSize { get; set; }
		/// <summary>
		/// Dedicated temporary slot for float immediate loading (avoids corrupting user variables)
		/// </summary>
		public int FloatTempOffset { get; set; }
	}

	public static Allocation Allocate(LirFunction func) {
		var alloc = new Allocation();

		// Start vreg allocation AFTER the alloca stack space
		// func.StackSize is the space needed for HirAlloca'd variables
		var nextOffset = func.StackSize;

		// Reserve a dedicated slot for float temporaries
		nextOffset += 8;
		alloc.FloatTempOffset = -nextOffset;

		// Collect all vreg definitions
		var vregs = new HashSet<int>();

		foreach (var block in func.Blocks) {
			foreach (var instr in block.Instructions) {
				CollectVRegs(instr, vregs);
			}
		}

		// Assign each vreg a stack slot (8 bytes each)
		foreach (var vreg in vregs.Order()) {
			nextOffset += 8;
			alloc.VRegToStackOffset[vreg] = -nextOffset;
		}

		// Align to 16 bytes (Windows x64 ABI)
		alloc.StackSize = ((nextOffset + 15) / 16) * 16;

		return alloc;
	}

	private static void CollectVRegs(LirInstr instr, HashSet<int> vregs) {
		switch (instr) {
			case LirMov mov:
				vregs.Add(mov.Dest.Id);
				AddValueVReg(mov.Src, vregs);
				break;
			case LirLoad load:
				vregs.Add(load.Dest.Id);
				AddValueVReg(load.Ptr, vregs);
				break;
			case LirStore store:
				AddValueVReg(store.Ptr, vregs);
				AddValueVReg(store.Value, vregs);
				break;
			case LirMemcpy memcpy:
				AddValueVReg(memcpy.Dest, vregs);
				AddValueVReg(memcpy.Src, vregs);
				break;
			case LirLea lea:
				vregs.Add(lea.Dest.Id);
				break;
			case LirAdd add:
				vregs.Add(add.Dest.Id);
				AddValueVReg(add.Left, vregs);
				AddValueVReg(add.Right, vregs);
				break;
			case LirSub sub:
				vregs.Add(sub.Dest.Id);
				AddValueVReg(sub.Left, vregs);
				AddValueVReg(sub.Right, vregs);
				break;
			case LirIMul mul:
				vregs.Add(mul.Dest.Id);
				AddValueVReg(mul.Left, vregs);
				AddValueVReg(mul.Right, vregs);
				break;
			case LirIDiv div:
				vregs.Add(div.Dest.Id);
				AddValueVReg(div.Left, vregs);
				AddValueVReg(div.Right, vregs);
				break;
			case LirMod mod:
				vregs.Add(mod.Dest.Id);
				AddValueVReg(mod.Left, vregs);
				AddValueVReg(mod.Right, vregs);
				break;
			case LirNeg neg:
				vregs.Add(neg.Dest.Id);
				AddValueVReg(neg.Src, vregs);
				break;
			case LirAnd and:
				vregs.Add(and.Dest.Id);
				AddValueVReg(and.Left, vregs);
				AddValueVReg(and.Right, vregs);
				break;
			case LirOr or:
				vregs.Add(or.Dest.Id);
				AddValueVReg(or.Left, vregs);
				AddValueVReg(or.Right, vregs);
				break;
			case LirXor xor:
				vregs.Add(xor.Dest.Id);
				AddValueVReg(xor.Left, vregs);
				AddValueVReg(xor.Right, vregs);
				break;
			case LirNot not:
				vregs.Add(not.Dest.Id);
				AddValueVReg(not.Src, vregs);
				break;
			case LirShl shl:
				vregs.Add(shl.Dest.Id);
				AddValueVReg(shl.Left, vregs);
				AddValueVReg(shl.Right, vregs);
				break;
			case LirShr shr:
				vregs.Add(shr.Dest.Id);
				AddValueVReg(shr.Left, vregs);
				AddValueVReg(shr.Right, vregs);
				break;
			case LirCmp cmp:
				AddValueVReg(cmp.Left, vregs);
				AddValueVReg(cmp.Right, vregs);
				break;
			case LirSetCC setcc:
				vregs.Add(setcc.Dest.Id);
				break;
			case LirRet ret:
				if (ret.Value != null) AddValueVReg(ret.Value, vregs);
				break;
			case LirCall call:
				if (call.Dest != null) vregs.Add(call.Dest.Id);
				foreach (var arg in call.Args) AddValueVReg(arg, vregs);
				break;
			case LirPush push:
				AddValueVReg(push.Value, vregs);
				break;
			case LirPop pop:
				vregs.Add(pop.Dest.Id);
				break;
			case LirIntToFloat itof:
				vregs.Add(itof.Dest.Id);
				AddValueVReg(itof.Src, vregs);
				break;
			case LirFloatToInt ftoi:
				vregs.Add(ftoi.Dest.Id);
				AddValueVReg(ftoi.Src, vregs);
				break;
			case LirFAdd fadd:
				vregs.Add(fadd.Dest.Id);
				AddValueVReg(fadd.Left, vregs);
				AddValueVReg(fadd.Right, vregs);
				break;
			case LirFSub fsub:
				vregs.Add(fsub.Dest.Id);
				AddValueVReg(fsub.Left, vregs);
				AddValueVReg(fsub.Right, vregs);
				break;
			case LirFMul fmul:
				vregs.Add(fmul.Dest.Id);
				AddValueVReg(fmul.Left, vregs);
				AddValueVReg(fmul.Right, vregs);
				break;
			case LirFDiv fdiv:
				vregs.Add(fdiv.Dest.Id);
				AddValueVReg(fdiv.Left, vregs);
				AddValueVReg(fdiv.Right, vregs);
				break;
			case LirAddressOf addr:
				vregs.Add(addr.Dest.Id);
				break;
		}
	}

	private static void AddValueVReg(LirValue value, HashSet<int> vregs) {
		if (value is LirVReg vreg) {
			vregs.Add(vreg.Id);
		}
	}
}

