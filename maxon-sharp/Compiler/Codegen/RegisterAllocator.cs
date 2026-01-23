using MaxonSharp.Lir;

namespace MaxonSharp.Codegen;

public class RegisterAllocator {
	// Simple linear allocation: assign vregs to physical registers
	// For now, just use a few caller-saved registers
	private static readonly Reg[] AvailableRegs = [Reg.Rax, Reg.Rcx, Reg.Rdx, Reg.R8, Reg.R9, Reg.R10, Reg.R11];

	public static Dictionary<int, Reg> Allocate(LirFunction func) {
		var allocation = new Dictionary<int, Reg>();
		var usedRegs = 0;

		foreach (var block in func.Blocks) {
			foreach (var instr in block.Instructions) {
				switch (instr) {
					case LirMov mov:
						if (!allocation.ContainsKey(mov.Dest.Id)) {
							if (usedRegs >= AvailableRegs.Length) {
								throw new Exception("Register allocation failed: out of registers");
							}
							allocation[mov.Dest.Id] = AvailableRegs[usedRegs++];
						}
						break;
				}
			}
		}

		return allocation;
	}
}
