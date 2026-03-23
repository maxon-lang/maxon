using System.Text;
using MaxonSharp.Compiler.Mlir.Conversion;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Passes;

namespace MaxonSharp.Compiler;

public record MlirPipelineResult {
  public MlirModule<X86Op>? X86Module { get; init; }
  public MlirModule<ARM64Op>? ARM64Module { get; init; }
  public string? AllStagesIr { get; init; }
}

public class MlirPipeline {
  public static MlirPipelineResult Run(MlirModule<MaxonOp> module, bool returnIr = false, string? dumpStagesBasePath = null, CompileTarget? target = null) {
    target ??= CompileTarget.Default;
    Logger.Debug(LogCategory.Mlir, "Starting MLIR pipeline");

    StringBuilder? irBuilder = returnIr ? new() : null;

    // Purity analysis (before semantic checks which validate discarded results)
    PurityAnalysisPass.Run(module);

    // Semantic checks
    SemanticCheckPass.Run(module);

    // Separate iterator state from collection structs (before monomorphization
    // so iterator types participate in generic specialization)
    IteratorSeparationPass.Run(module);

    // Monomorphize generic type methods for concrete aliases
    MonomorphizationPass.Run(module);

    // Synthesize clone() for struct types created during monomorphization
    CloneSynthesisPass.Run(module);

    // Remove original generic functions that were fully monomorphized
    DeadFunctionElimination.Run(module);

    // Analyze constant array literals for .rdata placement (after monomorphization)
    ConstantArrayAnalysisPass.Run(module);

    // Re-run purity analysis after monomorphization
    PurityAnalysisPass.Run(module);

    // Detect reference cycles in type definitions (compile error if found)
    TypeCycleCheckPass.Run(module);

    // Borrow checking (after monomorphization so concrete method names are resolved)
    BorrowCheckPass.Run(module);

    // Stack promotion analysis: identify struct literals safe for stack allocation
    StackPromotionAnalysisPass.Run(module);

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
    RefcountOptimizationPass.Run(stdModule);
    DeadStoreEliminationPass.Run(stdModule); // cleanup after refcount opt
    JumpTableFormationPass.Run(stdModule);

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

    if (target.Arch == "aarch64") {
      // Standard dialects -> ARM64 dialect
      var arm64Module = StandardToARM64Conversion.Run(stdModule);
      Logger.Debug(LogCategory.Mlir, "Lowered Standard to ARM64");

      // Capture arm64 stage
      if (returnIr || dumpStagesBasePath != null) {
        if (returnIr) {
          var ir = MlirPrinter.Print(arm64Module, f => !f.IsStdlib);
          irBuilder!.AppendLine($"=== {PipelineStages.ARM64}");
          irBuilder.Append(ir.TrimEnd());
          irBuilder.AppendLine();
        }
        if (dumpStagesBasePath != null) {
          File.WriteAllText($"{dumpStagesBasePath}.3-arm64.mlir", MlirPrinter.Print(arm64Module));
        }
      }

      return new MlirPipelineResult { ARM64Module = arm64Module, AllStagesIr = irBuilder?.ToString().TrimEnd() };
    } else if (target.Arch == "x86_64") {
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

      return new MlirPipelineResult { X86Module = x86Module, AllStagesIr = irBuilder?.ToString().TrimEnd() };
    } else {
      throw new InvalidOperationException($"Unsupported target architecture: {target.Arch}");
    }
  }

  public static void WriteMlirOutput<TOp>(MlirModule<TOp> module, string path) where TOp : IPrintableOp {
    File.WriteAllText(path, MlirPrinter.Print(module));
  }
}

public static class PipelineStages {
  public const string Maxon = "maxon";
  public const string Standard = "standard";
  public const string X86 = "x86";
  public const string ARM64 = "arm64";

  public static readonly string[] All = [Maxon, Standard, X86, ARM64];
}
