using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Inserts function prologue ops at the start of each function's entry block.
/// Also inserts moves to copy parameters from ABI registers to virtual registers.
/// Calculates stack frame size from alloca operations.
/// Should run after Standard→X86 lowering.
/// </summary>
public sealed class FunctionFramePass : FunctionPass {
	public override string Name => "function-frame";
	public override string Description => "Inserts function prologue, parameter moves, and calculates stack frame";

	protected override bool RunOnFunction(MlirFunction func) {
		var entryBlock = func.Body.Blocks.FirstOrDefault();
		if (entryBlock is null) return false;

		// Check if prologue already exists
		if (entryBlock.Operations.Count > 0 && entryBlock.Operations[0] is PrologueOp) {
			Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} already has prologue");
			return false;
		}

		// Get pre-computed alloca info from FrameLayoutAnalysisPass
		// This should have been computed before StandardToX86 lowering
		var allocaInfos = func.GetMetadata<List<StackAllocaInfo>>("alloca_offsets");
		int localVarsSize = allocaInfos?.Sum(a => WindowsX64Abi.AlignTo(a.Size, 8)) ?? 0;

		// Windows x64 requires 32 bytes shadow space + locals, aligned to 16
		int stackSize = WindowsX64Abi.AlignTo(
			WindowsX64Abi.ShadowSpaceSize + localVarsSize,
			WindowsX64Abi.StackAlignment);

		// Build list of ops to insert: prologue first, then parameter copies
		var insertOps = new List<MlirOperation> {
			new PrologueOp(stackSize)
		};

		// Copy parameters from ABI registers to their virtual registers
		// First 4 parameters come from registers (RCX, RDX, R8, R9 for int; XMM0-XMM3 for float)
		var args = entryBlock.Arguments;
		for (int i = 0; i < args.Count && i < WindowsX64Abi.IntArgRegs.Length; i++) {
			var arg = args[i];
			bool isFloat = arg.Value.Type is FloatType;
			var paramVreg = new VRegOperand(arg.Value.Id, IsFloat: isFloat);

			if (isFloat) {
				// Float: copy from XMMn using movsd
				var abiReg = new RegOperand(WindowsX64Abi.FloatArgRegs[i]);
				insertOps.Add(new MovsdOp(paramVreg, abiReg));
				Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} float param {i} from {WindowsX64Abi.FloatArgRegs[i]} to v{arg.Value.Id}");
			} else {
				// Integer: copy from GPR
				var abiReg = new RegOperand(WindowsX64Abi.IntArgRegs[i]);
				insertOps.Add(new MovOp(paramVreg, abiReg));
				Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} int param {i} from {WindowsX64Abi.IntArgRegs[i]} to v{arg.Value.Id}");
			}
		}

		// Parameters 5+ come from the stack (Windows x64 calling convention)
		// Stack layout after prologue (push rbp; mov rbp, rsp):
		//   [RBP]     = saved RBP
		//   [RBP+8]   = return address
		//   [RBP+16]  = 5th argument
		//   [RBP+24]  = 6th argument
		//   etc.
		for (int i = WindowsX64Abi.IntArgRegs.Length; i < args.Count; i++) {
			var arg = args[i];
			bool isFloat = arg.Value.Type is FloatType;
			var paramVreg = new VRegOperand(arg.Value.Id, IsFloat: isFloat);

			// Stack offset: +16 for saved RBP and return address, then +8 per argument
			int stackOffset = 16 + (i - WindowsX64Abi.IntArgRegs.Length) * 8;
			var stackMem = new MemOperand(
				Base: new RegOperand(X86Register.RBP),
				Displacement: stackOffset,
				Size: 8
			);

			if (isFloat) {
				insertOps.Add(new MovsdOp(paramVreg, stackMem));
				Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} float param {i} from [RBP+{stackOffset}] to v{arg.Value.Id}");
			} else {
				insertOps.Add(new MovOp(paramVreg, stackMem));
				Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} int param {i} from [RBP+{stackOffset}] to v{arg.Value.Id}");
			}
		}

		// Insert all ops at the beginning
		for (int i = insertOps.Count - 1; i >= 0; i--) {
			entryBlock.Operations.Insert(0, insertOps[i]);
		}

		Logger.Debug(LogCategory.Codegen, $"function-frame: {func.Name} stack size = {stackSize} ({localVarsSize} locals + {WindowsX64Abi.ShadowSpaceSize} shadow), params = {args.Count}");

		return true;
	}

	private static List<StackAllocaInfo> CollectAllocas(MlirFunction func) {
		var allocas = new List<StackAllocaInfo>();
		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				if (op is AllocaOp alloca) {
					allocas.Add(new StackAllocaInfo {
						Size = alloca.MemRefType.ElementType.SizeInBytes,
						ResultId = alloca.Result.Id
					});
				}
			}
		}
		return allocas;
	}
}

/// <summary>
/// Information about a stack-allocated variable for frame layout.
/// </summary>
public class StackAllocaInfo {
	public int Size { get; set; }
	public int ResultId { get; set; }
	public int FrameOffset { get; set; }
}
