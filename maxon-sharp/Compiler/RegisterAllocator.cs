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
		// Handle binary operations (covers LirAdd, LirSub, LirIMul, LirIDiv, LirMod,
		// LirAnd, LirOr, LirXor, LirShl, LirShr, LirFAdd, LirFSub, LirFMul, LirFDiv)
		if (instr is ILirBinaryOp binOp) {
			vregs.Add(binOp.Dest.Id);
			AddValueVReg(binOp.Left, vregs);
			AddValueVReg(binOp.Right, vregs);
			return;
		}

		// Handle unary operations (covers LirNeg, LirNot, LirFNeg, LirIntToFloat, LirFloatToInt)
		if (instr is ILirUnaryOp unaryOp) {
			vregs.Add(unaryOp.Dest.Id);
			AddValueVReg(unaryOp.Src, vregs);
			return;
		}

		// Handle remaining special cases
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
			case LirCmp cmp:
				AddValueVReg(cmp.Left, vregs);
				AddValueVReg(cmp.Right, vregs);
				break;
			case LirFCmp fcmp:
				AddValueVReg(fcmp.Left, vregs);
				AddValueVReg(fcmp.Right, vregs);
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
			case LirAddressOf addr:
				vregs.Add(addr.Dest.Id);
				break;
			case LirSignExtend signExt:
				vregs.Add(signExt.Dest.Id);
				AddValueVReg(signExt.Src, vregs);
				break;
			case LirZeroExtend zeroExt:
				vregs.Add(zeroExt.Dest.Id);
				AddValueVReg(zeroExt.Src, vregs);
				break;
		}
	}

	private static void AddValueVReg(LirValue value, HashSet<int> vregs) {
		if (value is LirVReg vreg) {
			vregs.Add(vreg.Id);
		}
	}
}

