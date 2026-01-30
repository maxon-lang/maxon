using MaxonSharp.Compiler.Mlir.Conversion;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Passes;

namespace MaxonSharp.Compiler;

public record MlirPipelineResult(MlirModule Module, string? X86Ir);

public class MlirPipeline {
	public static MlirPipelineResult Run(MlirModule module, bool returnIr = false, string? dumpStagesBasePath = null) {
		Logger.Debug(LogCategory.Mlir, "Starting MLIR pipeline");

		// Semantic checks
		SemanticCheckPass.Run(module);

		if (dumpStagesBasePath != null) {
			File.WriteAllText($"{dumpStagesBasePath}.1-maxon.mlir", MlirPrinter.Print(module));
		}

		// Maxon dialect -> Standard dialects
		MaxonToStandardConversion.Run(module);
		Logger.Debug(LogCategory.Mlir, "Lowered Maxon to Standard");

		if (dumpStagesBasePath != null) {
			File.WriteAllText($"{dumpStagesBasePath}.2-standard.mlir", MlirPrinter.Print(module));
		}

		// Standard dialects -> X86 dialect
		StandardToX86Conversion.Run(module);
		Logger.Debug(LogCategory.Mlir, "Lowered Standard to X86");

		string? x86Ir = null;
		if (returnIr || dumpStagesBasePath != null) {
			x86Ir = MlirPrinter.Print(module);
			if (dumpStagesBasePath != null) {
				File.WriteAllText($"{dumpStagesBasePath}.3-x86.mlir", x86Ir);
			}
		}

		return new MlirPipelineResult(module, x86Ir);
	}

	public static void WriteMlirOutput(MlirModule module, string path) {
		File.WriteAllText(path, MlirPrinter.Print(module));
	}
}
