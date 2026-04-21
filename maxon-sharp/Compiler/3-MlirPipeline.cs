using System.Text;
using MaxonSharp.Compiler.Ir.Conversion;
using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;
using MaxonSharp.Compiler.Ir.Passes;

namespace MaxonSharp.Compiler;

public record IrPipelineResult {
  public IrModule<X86Op>? X86Module { get; init; }
  public IrModule<ARM64Op>? ARM64Module { get; init; }
  public string? AllStagesIr { get; init; }
}

public class IrPipeline {
  public static IrPipelineResult Run(IrModule<MaxonOp> module, bool returnIr = false, string? dumpStagesBasePath = null, CompileTarget? target = null) {
    target ??= CompileTarget.Default;
    Logger.Debug(LogCategory.Ir, "Starting IR pipeline");

    StringBuilder? irBuilder = returnIr ? new() : null;

    // Parameter mutation analysis — populates MutatedParams, which purity
    // analysis consults to detect self-field writes (e.g. `pos = x` inside
    // a method lowers to MaxonAssignOp("pos", ...) rather than a
    // FieldAssignOp; only this pass resolves it back to a self mutation).
    ParameterMutationAnalysisPass.Run(module);

    // Purity analysis (before semantic checks which validate discarded results)
    PurityAnalysisPass.Run(module);

    // Semantic checks
    SemanticCheckPass.Run(module);

    // Monomorphize generic type methods for concrete aliases
    MonomorphizationPass.Run(module);

    // Synthesize clone() for struct types created during monomorphization
    CloneSynthesisPass.Run(module);

    // Elide the ArrayIterator wrapper on simple `for x in arr` loops, rewriting
    // them to direct indexed access over the backing __ManagedMemory. Runs after
    // monomorphization so concrete iterator callees like __ArrayIterator_Int.advance
    // are visible for pattern matching.
    ForLoopIteratorElisionPass.Run(module);

    // Remove original generic functions that were fully monomorphized, and the
    // <T>Array.createIterator / __ArrayIterator_T.advance / .current (plus their
    // transitive ArrayIterator.create / __ManagedMemoryCursor.create callees) that
    // ForLoopIteratorElisionPass made unreachable. Running DFE after the elision
    // also prevents the lowering pass from emitting destructors for iterator /
    // cursor types that are no longer allocated on any reachable path.
    DeadFunctionElimination.Run(module);

    // Analyze constant array literals for .rdata placement (after monomorphization)
    ConstantArrayAnalysisPass.Run(module);

    // Re-run mutation + purity analysis after monomorphization (clones may
    // have reset the per-function flags, and cross-function propagation
    // needs the post-monomorphization call graph).
    ParameterMutationAnalysisPass.Run(module);
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
        var ir = IrPrinter.Print(module, f => !f.IsStdlib);
        irBuilder!.AppendLine($"=== {PipelineStages.Maxon}");
        irBuilder.Append(ir.TrimEnd());
        irBuilder.AppendLine();
      }
      if (dumpStagesBasePath != null) {
        File.WriteAllText($"{dumpStagesBasePath}.1-maxon.ir", IrPrinter.Print(module));
      }
    }

    // Maxon dialect -> Standard dialects
    var stdModule = MaxonToStandardConversion.Run(module);
    Logger.Debug(LogCategory.Ir, "Lowered Maxon to Standard");

    StoreForwardingPass.Run(stdModule);
    DeadStoreEliminationPass.Run(stdModule);
    ParameterRetentionAnalysisPass.Run(stdModule);
    RefcountOptimizationPass.Run(stdModule);
    DeadStoreEliminationPass.Run(stdModule); // cleanup after refcount opt
    JumpTableFormationPass.Run(stdModule);

    // Capture standard stage
    if (returnIr || dumpStagesBasePath != null) {
      if (returnIr) {
        var ir = IrPrinter.Print(stdModule, f => !f.IsStdlib);
        irBuilder!.AppendLine($"=== {PipelineStages.Standard}");
        irBuilder.Append(ir.TrimEnd());
        irBuilder.AppendLine();
      }
      if (dumpStagesBasePath != null) {
        File.WriteAllText($"{dumpStagesBasePath}.2-standard.ir", IrPrinter.Print(stdModule));
      }
    }

    if (target.Arch == "arm64") {
      // Standard dialects -> ARM64 dialect
      var arm64Module = StandardToARM64Conversion.Run(stdModule);
      Logger.Debug(LogCategory.Ir, "Lowered Standard to ARM64");

      // Capture arm64 stage
      if (returnIr || dumpStagesBasePath != null) {
        if (returnIr) {
          var ir = IrPrinter.Print(arm64Module, f => !f.IsStdlib);
          irBuilder!.AppendLine($"=== {PipelineStages.ARM64}");
          irBuilder.Append(ir.TrimEnd());
          irBuilder.AppendLine();
        }
        if (dumpStagesBasePath != null) {
          File.WriteAllText($"{dumpStagesBasePath}.3-arm64.ir", IrPrinter.Print(arm64Module));
        }
      }

      return new IrPipelineResult { ARM64Module = arm64Module, AllStagesIr = irBuilder?.ToString().TrimEnd() };
    } else if (target.Arch == "x64") {
      // Standard dialects -> X86 dialect
      var x86Module = StandardToX86Conversion.Run(stdModule);
      Logger.Debug(LogCategory.Ir, "Lowered Standard to X86");

      // Peephole optimization on X86 ops
      PeepholePass.Run(x86Module);

      // Capture x86 stage
      if (returnIr || dumpStagesBasePath != null) {
        if (returnIr) {
          var ir = IrPrinter.Print(x86Module, f => !f.IsStdlib);
          irBuilder!.AppendLine($"=== {PipelineStages.X86}");
          irBuilder.Append(ir.TrimEnd());
          irBuilder.AppendLine();
        }
        if (dumpStagesBasePath != null) {
          File.WriteAllText($"{dumpStagesBasePath}.3-x64.ir", IrPrinter.Print(x86Module));
        }
      }

      return new IrPipelineResult { X86Module = x86Module, AllStagesIr = irBuilder?.ToString().TrimEnd() };
    } else {
      throw new InvalidOperationException($"Unsupported target architecture: {target.Arch}");
    }
  }

  public static void WriteIrOutput<TOp>(IrModule<TOp> module, string path) where TOp : IPrintableOp {
    File.WriteAllText(path, IrPrinter.Print(module));
  }
}

public static class PipelineStages {
  public const string Maxon = "maxon";
  public const string Standard = "standard";
  public const string X86 = "x86";
  public const string ARM64 = "arm64";

  public static readonly string[] All = [Maxon, Standard, X86, ARM64];
}
