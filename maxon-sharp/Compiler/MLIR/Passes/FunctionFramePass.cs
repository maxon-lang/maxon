using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.X86;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Inserts function prologue ops at the start of each function's entry block.
/// Also inserts moves to copy parameters from ABI registers to virtual registers.
/// Should run after Standard→X86 lowering.
/// </summary>
public sealed class FunctionFramePass : FunctionPass {
	public override string Name => "function-frame";
	public override string Description => "Inserts function prologue and parameter moves at entry blocks";

	// Windows x64 ABI: first 4 integer/pointer args in rcx, rdx, r8, r9
	private static readonly X86Register[] ArgRegs = [
		X86Register.RCX,
		X86Register.RDX,
		X86Register.R8,
		X86Register.R9
	];

	protected override bool RunOnFunction(MlirFunction func) {
		var entryBlock = func.Body.Blocks.FirstOrDefault();
		if (entryBlock is null) return false;

		// Check if prologue already exists
		if (entryBlock.Operations.Count > 0 && entryBlock.Operations[0] is PrologueOp) {
			Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} already has prologue");
			return false;
		}

		// Calculate stack size needed
		// For now, use fixed 32 bytes for shadow space (Windows x64 ABI)
		// TODO: Calculate based on local variables and spills
		var stackSize = 32;

		// Build list of ops to insert: prologue first, then parameter copies
		var insertOps = new List<MlirOperation>();
		insertOps.Add(new PrologueOp(stackSize));

		// Copy parameters from ABI registers to their virtual registers
		var args = entryBlock.Arguments;
		for (int i = 0; i < args.Count && i < ArgRegs.Length; i++) {
			var arg = args[i];
			var paramVreg = new VRegOperand(arg.Value.Id);
			var abiReg = new RegOperand(ArgRegs[i]);
			insertOps.Add(new MovOp(paramVreg, abiReg));
			Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} param {i} from {ArgRegs[i]} to v{arg.Value.Id}");
		}

		// Insert all ops at the beginning
		for (int i = insertOps.Count - 1; i >= 0; i--) {
			entryBlock.Operations.Insert(0, insertOps[i]);
		}

		Logger.Debug(LogCategory.Codegen, $"function-frame: {func.Name} stack size = {stackSize}, params = {args.Count}");

		return true;
	}
}
