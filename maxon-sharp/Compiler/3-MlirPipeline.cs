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

    // Hoist the timing-enabled check once; inside each branch the passes are
    // invoked the same way they would have been before instrumentation, so the
    // disabled path has zero per-pass overhead.
    Dictionary<string, long>? timings = null;
    if (StageTimer.Enabled) {
      timings = [];
      var sw = new System.Diagnostics.Stopwatch();

      sw.Restart(); ParameterMutationAnalysisPass.Run(module);            StageTimer.Record(timings, "paramMut",   sw.ElapsedMilliseconds);
      sw.Restart(); PurityAnalysisPass.Run(module);                       StageTimer.Record(timings, "purity",     sw.ElapsedMilliseconds);
      sw.Restart(); SemanticCheckPass.Run(module);                        StageTimer.Record(timings, "semantic",   sw.ElapsedMilliseconds);
      sw.Restart(); MonomorphizationPass.Run(module);                     StageTimer.Record(timings, "monomorph",  sw.ElapsedMilliseconds);
      sw.Restart(); CloneSynthesisPass.Run(module);                       StageTimer.Record(timings, "cloneSynth", sw.ElapsedMilliseconds);
      sw.Restart(); ForLoopIteratorElisionPass.Run(module);                StageTimer.Record(timings, "forElide",   sw.ElapsedMilliseconds);
      sw.Restart(); DeadFunctionElimination.Run(module);                  StageTimer.Record(timings, "dfe",        sw.ElapsedMilliseconds);
      sw.Restart(); ConstantArrayAnalysisPass.Run(module);                StageTimer.Record(timings, "constArr",   sw.ElapsedMilliseconds);
      sw.Restart(); ParameterMutationAnalysisPass.Run(module);            StageTimer.Record(timings, "paramMut",   sw.ElapsedMilliseconds);
      sw.Restart(); PurityAnalysisPass.Run(module);                       StageTimer.Record(timings, "purity",     sw.ElapsedMilliseconds);
      sw.Restart(); TypeCycleCheckPass.Run(module);                       StageTimer.Record(timings, "typeCycle",  sw.ElapsedMilliseconds);
      sw.Restart(); BorrowCheckPass.Run(module);                          StageTimer.Record(timings, "borrow",     sw.ElapsedMilliseconds);
      sw.Restart(); StackPromotionAnalysisPass.Run(module);               StageTimer.Record(timings, "stackProm",  sw.ElapsedMilliseconds);
    } else {
      ParameterMutationAnalysisPass.Run(module);
      PurityAnalysisPass.Run(module);
      SemanticCheckPass.Run(module);
      MonomorphizationPass.Run(module);
      CloneSynthesisPass.Run(module);
      ForLoopIteratorElisionPass.Run(module);
      DeadFunctionElimination.Run(module);
      ConstantArrayAnalysisPass.Run(module);
      ParameterMutationAnalysisPass.Run(module);
      PurityAnalysisPass.Run(module);
      TypeCycleCheckPass.Run(module);
      BorrowCheckPass.Run(module);
      StackPromotionAnalysisPass.Run(module);
    }

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

    IrModule<StandardOp> stdModule;
    if (timings != null) {
      var sw = new System.Diagnostics.Stopwatch();
      sw.Restart(); stdModule = MaxonToStandardConversion.Run(module);     StageTimer.Record(timings, "lower:mx→std", sw.ElapsedMilliseconds);
      Logger.Debug(LogCategory.Ir, "Lowered Maxon to Standard");
      sw.Restart(); StoreForwardingPass.Run(stdModule);                    StageTimer.Record(timings, "storeFwd",     sw.ElapsedMilliseconds);
      sw.Restart(); DeadStoreEliminationPass.Run(stdModule);               StageTimer.Record(timings, "dse",          sw.ElapsedMilliseconds);
      sw.Restart(); ParameterRetentionAnalysisPass.Run(stdModule);         StageTimer.Record(timings, "paramRet",     sw.ElapsedMilliseconds);
      sw.Restart(); RefcountOptimizationPass.Run(stdModule);               StageTimer.Record(timings, "refcount",     sw.ElapsedMilliseconds);
      sw.Restart(); DeadStoreEliminationPass.Run(stdModule);               StageTimer.Record(timings, "dse",          sw.ElapsedMilliseconds);
      sw.Restart(); JumpTableFormationPass.Run(stdModule);                 StageTimer.Record(timings, "jumpTab",      sw.ElapsedMilliseconds);
    } else {
      stdModule = MaxonToStandardConversion.Run(module);
      Logger.Debug(LogCategory.Ir, "Lowered Maxon to Standard");
      StoreForwardingPass.Run(stdModule);
      DeadStoreEliminationPass.Run(stdModule);
      ParameterRetentionAnalysisPass.Run(stdModule);
      RefcountOptimizationPass.Run(stdModule);
      DeadStoreEliminationPass.Run(stdModule); // cleanup after refcount opt
      JumpTableFormationPass.Run(stdModule);
    }

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
      IrModule<ARM64Op> arm64Module;
      if (timings != null) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        arm64Module = StandardToARM64Conversion.Run(stdModule);
        StageTimer.Record(timings, "lower:std→arm64", sw.ElapsedMilliseconds);
      } else {
        arm64Module = StandardToARM64Conversion.Run(stdModule);
      }
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

      if (timings != null)
        Console.Error.WriteLine("Pipeline:" + StageTimer.Format(timings));
      return new IrPipelineResult { ARM64Module = arm64Module, AllStagesIr = irBuilder?.ToString().TrimEnd() };
    } else if (target.Arch == "x64") {
      IrModule<X86Op> x86Module;
      if (timings != null) {
        var sw = new System.Diagnostics.Stopwatch();
        sw.Restart(); x86Module = StandardToX86Conversion.Run(stdModule); StageTimer.Record(timings, "lower:std→x86", sw.ElapsedMilliseconds);
        Logger.Debug(LogCategory.Ir, "Lowered Standard to X86");
        sw.Restart(); PeepholePass.Run(x86Module);                        StageTimer.Record(timings, "peephole",      sw.ElapsedMilliseconds);
      } else {
        x86Module = StandardToX86Conversion.Run(stdModule);
        Logger.Debug(LogCategory.Ir, "Lowered Standard to X86");
        PeepholePass.Run(x86Module);
      }

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

      if (timings != null)
        Console.Error.WriteLine("Pipeline:" + StageTimer.Format(timings));
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
