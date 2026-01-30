using System.Text;
using MaxonSharp.Compiler.Mlir.Conversion;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Passes;

namespace MaxonSharp.Compiler;

public record MlirPipelineResult(MlirModule<X86Op> Module, string? AllStagesIr);

public class MlirPipeline {
	public static MlirPipelineResult Run(MlirModule<MaxonOp> module, bool returnIr = false, string? dumpStagesBasePath = null) {
		Logger.Debug(LogCategory.Mlir, "Starting MLIR pipeline");

		StringBuilder? irBuilder = returnIr ? new() : null;

		// Semantic checks
		SemanticCheckPass.Run(module);

		// Capture maxon stage
		if (returnIr || dumpStagesBasePath != null) {
			var maxonIr = MlirPrinter.Print(module);
			if (returnIr) {
				irBuilder!.AppendLine($"=== {PipelineStages.Maxon}");
				irBuilder.Append(maxonIr.TrimEnd());
				irBuilder.AppendLine();
			}
			if (dumpStagesBasePath != null) {
				File.WriteAllText($"{dumpStagesBasePath}.1-maxon.mlir", maxonIr);
			}
		}

		// Maxon dialect -> Standard dialects
		var stdModule = MaxonToStandardConversion.Run(module);
		Logger.Debug(LogCategory.Mlir, "Lowered Maxon to Standard");

		// Capture standard stage
		if (returnIr || dumpStagesBasePath != null) {
			var standardIr = MlirPrinter.Print(stdModule);
			if (returnIr) {
				irBuilder!.AppendLine($"=== {PipelineStages.Standard}");
				irBuilder.Append(standardIr.TrimEnd());
				irBuilder.AppendLine();
			}
			if (dumpStagesBasePath != null) {
				File.WriteAllText($"{dumpStagesBasePath}.2-standard.mlir", standardIr);
			}
		}

		// Standard dialects -> X86 dialect
		var x86Module = StandardToX86Conversion.Run(stdModule);
		Logger.Debug(LogCategory.Mlir, "Lowered Standard to X86");

		// Capture x86 stage
		if (returnIr || dumpStagesBasePath != null) {
			var x86Ir = MlirPrinter.Print(x86Module);
			if (returnIr) {
				irBuilder!.AppendLine($"=== {PipelineStages.X86}");
				irBuilder.Append(x86Ir.TrimEnd());
				irBuilder.AppendLine();
			}
			if (dumpStagesBasePath != null) {
				File.WriteAllText($"{dumpStagesBasePath}.3-x86.mlir", x86Ir);
			}
		}

		return new MlirPipelineResult(x86Module, irBuilder?.ToString().TrimEnd());
	}

	public static void WriteMlirOutput<TOp>(MlirModule<TOp> module, string path) where TOp : IMlirOp {
		File.WriteAllText(path, MlirPrinter.Print(module));
	}
}

public static class PipelineStages {
	public const string Maxon = "maxon";
	public const string Standard = "standard";
	public const string X86 = "x86";

	public static readonly string[] All = [Maxon, Standard, X86];
}
