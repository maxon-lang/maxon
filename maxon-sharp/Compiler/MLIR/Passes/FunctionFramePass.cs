using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.X86;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Inserts function prologue ops at the start of each function's entry block.
/// Should run after Standard→X86 lowering.
/// </summary>
public sealed class FunctionFramePass : FunctionPass {
	public override string Name => "function-frame";
	public override string Description => "Inserts function prologue at entry blocks";

	protected override bool RunOnFunction(MlirFunction func) {
		var entryBlock = func.Body.Blocks.FirstOrDefault();
		if (entryBlock is null) return false;

		// Check if prologue already exists
		if (entryBlock.Operations.Count > 0 && entryBlock.Operations[0] is PrologueOp) {
			return false;
		}

		// Calculate stack size needed
		// For now, use fixed 32 bytes for shadow space (Windows x64 ABI)
		// TODO: Calculate based on local variables and spills
		var stackSize = 32;

		// Insert prologue at the beginning
		entryBlock.Operations.Insert(0, new PrologueOp(stackSize));

		return true;
	}
}
