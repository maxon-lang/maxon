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

    // Purity analysis (before semantic checks which validate discarded results)
    PurityAnalysisPass.Run(module);

    // Semantic checks
    SemanticCheckPass.Run(module);

    // Monomorphize generic type methods for concrete aliases
    MonomorphizationPass.Run(module);

    // Remove original generic functions that were fully monomorphized
    DeadFunctionElimination.Run(module);

    // Analyze constant array literals for .rdata placement (after monomorphization)
    ConstantArrayAnalysisPass.Run(module);

    // Re-run purity analysis after monomorphization — monomorphized functions
    // (e.g. NodeArray.push from Array.push) need purity flags for scope analysis
    PurityAnalysisPass.Run(module);

    // Analyze scope lifetimes for compile-time memory management
    ScopeAnalysisPass.Run(module);

    // Capture maxon stage
    if (returnIr || dumpStagesBasePath != null) {
      if (returnIr) {
        var ir = MlirPrinter.Print(module, f => !f.IsStdlib);
        irBuilder!.AppendLine($"=== {PipelineStages.Maxon}");
        irBuilder.Append(ir.TrimEnd());
        irBuilder.AppendLine();
      }
      if (dumpStagesBasePath != null) {
        File.WriteAllText($"{dumpStagesBasePath}.1-maxon.mlir", MlirPrinter.Print(module));
      }
    }

    // Maxon dialect -> Standard dialects
    var stdModule = MaxonToStandardConversion.Run(module);
    Logger.Debug(LogCategory.Mlir, "Lowered Maxon to Standard");

    StoreForwardingPass.Run(stdModule);
    DeadStoreEliminationPass.Run(stdModule);

    // Capture standard stage
    if (returnIr || dumpStagesBasePath != null) {
      if (returnIr) {
        var ir = MlirPrinter.Print(stdModule, f => !f.IsStdlib);
        irBuilder!.AppendLine($"=== {PipelineStages.Standard}");
        irBuilder.Append(ir.TrimEnd());
        irBuilder.AppendLine();
      }
      if (dumpStagesBasePath != null) {
        File.WriteAllText($"{dumpStagesBasePath}.2-standard.mlir", MlirPrinter.Print(stdModule));
      }
    }

    // Standard dialects -> X86 dialect
    var x86Module = StandardToX86Conversion.Run(stdModule);
    Logger.Debug(LogCategory.Mlir, "Lowered Standard to X86");

    // Peephole optimization on X86 ops
    PeepholePass.Run(x86Module);

    // Capture x86 stage
    if (returnIr || dumpStagesBasePath != null) {
      if (returnIr) {
        var ir = MlirPrinter.Print(x86Module, f => !f.IsStdlib);
        irBuilder!.AppendLine($"=== {PipelineStages.X86}");
        irBuilder.Append(ir.TrimEnd());
        irBuilder.AppendLine();
      }
      if (dumpStagesBasePath != null) {
        File.WriteAllText($"{dumpStagesBasePath}.3-x86.mlir", MlirPrinter.Print(x86Module));
      }
    }

    return new MlirPipelineResult(x86Module, irBuilder?.ToString().TrimEnd());
  }

  public static void WriteMlirOutput<TOp>(MlirModule<TOp> module, string path) where TOp : IPrintableOp {
    File.WriteAllText(path, MlirPrinter.Print(module));
  }
}

public static class PipelineStages {
  public const string Maxon = "maxon";
  public const string Standard = "standard";
  public const string X86 = "x86";

  public static readonly string[] All = [Maxon, Standard, X86];
}
