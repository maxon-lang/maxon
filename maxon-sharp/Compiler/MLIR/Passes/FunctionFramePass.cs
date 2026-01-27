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
		// IMPORTANT: We must use a parallel copy approach to avoid clobbering unread sources.
		// We first copy all sources to scratch registers (R10, R11), then copy to destinations.
		// This handles the case where v_param1 is allocated to R8, which would clobber the
		// source for v_param2 if we copied directly.
		var args = entryBlock.Arguments;
		var scratchRegs = new[] { X86Register.R10, X86Register.R11 };

		// For more than 2 register params, we need to be careful about the order
		// Strategy: copy all int params from ABI regs to scratch, then from scratch to vregs
		// For functions with <= 2 params, no clobbering is possible, so direct copy is fine
		var intParams = new List<(int index, MlirBlockArgument arg)>();
		var floatParams = new List<(int index, MlirBlockArgument arg)>();

		for (int i = 0; i < args.Count && i < WindowsX64Abi.IntArgRegs.Length; i++) {
			var arg = args[i];
			bool isFloat = arg.Value.Type is FloatType;
			if (isFloat) {
				floatParams.Add((i, arg));
			} else {
				intParams.Add((i, arg));
			}
		}

		// Float params: XMM registers don't conflict with GPR params, so direct copy is fine
		foreach (var (i, arg) in floatParams) {
			var paramVreg = new VRegOperand(arg.Value.Id, IsFloat: true);
			var abiReg = new RegOperand(WindowsX64Abi.FloatArgRegs[i]);
			insertOps.Add(new MovsdOp(paramVreg, abiReg));
			Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} float param {i} from {WindowsX64Abi.FloatArgRegs[i]} to v{arg.Value.Id}");
		}

		// Int params: potential for clobbering if reg allocator assigns vreg to another ABI reg
		if (intParams.Count <= 2) {
			// Safe: no more than 2 params means we can't have param vreg allocated to another ABI reg
			// (at most RCX and RDX as sources, so R8/R9 are free for destinations)
			foreach (var (i, arg) in intParams) {
				var paramVreg = new VRegOperand(arg.Value.Id, IsFloat: false);
				var abiReg = new RegOperand(WindowsX64Abi.IntArgRegs[i]);
				insertOps.Add(new MovOp(paramVreg, abiReg));
				Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} int param {i} from {WindowsX64Abi.IntArgRegs[i]} to v{arg.Value.Id}");
			}
		} else {
			// 3+ int params: use scratch registers to avoid clobbering
			// First, copy all ABI regs to scratch/stack
			var tempDest = new List<(int index, X86Operand temp, MlirBlockArgument arg)>();

			for (int j = 0; j < intParams.Count; j++) {
				var (i, arg) = intParams[j];
				var abiReg = new RegOperand(WindowsX64Abi.IntArgRegs[i]);

				if (j < scratchRegs.Length) {
					// Use scratch register
					var scratch = new RegOperand(scratchRegs[j]);
					insertOps.Add(new MovOp(scratch, abiReg));
					tempDest.Add((i, scratch, arg));
					Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} int param {i} from {WindowsX64Abi.IntArgRegs[i]} to {scratchRegs[j]} (temp)");
				} else {
					// More than 2 params and we're out of scratch regs
					// For param 2 (R8) and param 3 (R9), copy directly to vreg since params 0&1 are in scratch
					var paramVreg = new VRegOperand(arg.Value.Id, IsFloat: false);
					insertOps.Add(new MovOp(paramVreg, abiReg));
					Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} int param {i} from {WindowsX64Abi.IntArgRegs[i]} to v{arg.Value.Id} (direct)");
				}
			}

			// Now copy from scratch to vregs (RCX and RDX are now free)
			foreach (var (i, temp, arg) in tempDest) {
				var paramVreg = new VRegOperand(arg.Value.Id, IsFloat: false);
				insertOps.Add(new MovOp(paramVreg, temp));
				Logger.Trace(LogCategory.Codegen, $"function-frame: {func.Name} int param {i} from {temp} to v{arg.Value.Id}");
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
