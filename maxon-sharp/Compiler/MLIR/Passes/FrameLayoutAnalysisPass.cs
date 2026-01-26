using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Analyzes alloca operations and computes stack frame layout for each function.
/// This pass runs on Standard dialect (before X86 lowering) and stores the computed
/// offsets as function metadata for use by LowerAllocaOp.
/// </summary>
public sealed class FrameLayoutAnalysisPass : FunctionPass {
	public override string Name => "frame-layout-analysis";
	public override string Description => "Computes stack frame layout from alloca operations";

	protected override bool RunOnFunction(MlirFunction func) {
		// Skip if already analyzed
		if (func.GetMetadata<List<StackAllocaInfo>>("alloca_offsets") is not null) {
			return false;
		}

		// Collect all allocas and calculate total stack size
		var allocaInfos = CollectAllocas(func);
		if (allocaInfos.Count == 0) {
			// No allocas, but still set empty list for consistency
			func.SetMetadata("alloca_offsets", allocaInfos);
			return false;
		}

		int localVarsSize = allocaInfos.Sum(a => WindowsX64Abi.AlignTo(a.Size, 8));

		// Windows x64 requires 32 bytes shadow space + locals, aligned to 16
		int stackSize = WindowsX64Abi.AlignTo(
			WindowsX64Abi.ShadowSpaceSize + localVarsSize,
			WindowsX64Abi.StackAlignment);

		// Assign frame offsets to allocas (negative from RBP)
		// Start after shadow space
		int currentOffset = -WindowsX64Abi.ShadowSpaceSize;
		foreach (var info in allocaInfos) {
			int alignedSize = WindowsX64Abi.AlignTo(info.Size, 8);
			currentOffset -= alignedSize;
			info.FrameOffset = currentOffset;
			Logger.Trace(LogCategory.Codegen, $"frame-layout: alloca v{info.ResultId} size={info.Size} -> [rbp{info.FrameOffset}]");
		}

		// Store alloca info on the function for later use by LowerAllocaOp
		func.SetMetadata("alloca_offsets", allocaInfos);

		Logger.Debug(LogCategory.Codegen, $"frame-layout: {func.Name} has {allocaInfos.Count} allocas, locals size = {localVarsSize}");

		return true;
	}

	private static List<StackAllocaInfo> CollectAllocas(MlirFunction func) {
		var allocas = new List<StackAllocaInfo>();
		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				if (op is AllocaOp alloca) {
					// Compute total size: element size * product of all shape dimensions
					int totalSize = alloca.MemRefType.ElementType.SizeInBytes;
					if (alloca.MemRefType.Shape is { Count: > 0 } shape) {
						foreach (var dim in shape) {
							totalSize *= dim;
						}
					}
					allocas.Add(new StackAllocaInfo {
						Size = totalSize,
						ResultId = alloca.Result.Id
					});
				}
			}
		}
		return allocas;
	}
}
