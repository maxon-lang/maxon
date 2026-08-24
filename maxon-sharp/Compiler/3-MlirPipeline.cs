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
  /// <summary>
  /// Extension of the `--emit-ir` sidecar this class writes beside the binary. Named here, next to
  /// the only thing that produces one, because TWO places have to agree about it and they are not
  /// adjacent: the flag decides where to WRITE it, and <see cref="Compiler.DiscardPreviousOutput"/>
  /// decides where to REMOVE it — and the removal is unconditional, so it cannot borrow the flag's
  /// answer. Two spellings of ".ir" would leave a build silently preserving the previous build's
  /// sidecar under a name nothing swept.
  /// </summary>
  public const string SidecarExtension = ".ir";

  public static IrPipelineResult Run(IrModule<MaxonOp> module, bool returnIr = false, string? dumpStagesBasePath = null, CompileTarget? target = null) {
    target ??= CompileTarget.Default;
    Logger.Debug(LogCategory.Ir, "Starting IR pipeline");

    // ⭐ A CLOSED PIPELINE STAGE IS THE PROOF OF PROGRESS THE TREE LOCK'S HEARTBEAT NEEDS, and these
    // three boundaries are where a compile produces one on EVERY path — outside the `StageTimer`
    // branches (which only run under `--timing`) and outside the per-target arms (which would be two
    // copies that could drift). ONE site serves both holders: a long `maxon build <project>`, and a
    // `spec-test` whose thousands of compiles all run in-process through here. The write is throttled
    // to one every few seconds, so a per-compile call costs a tick read (see `TreeLock.Touch`).
    TreeLock.Touch();

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
      sw.Restart(); DeadFunctionElimination.Run(module);                  StageTimer.Record(timings, "dfe",        sw.ElapsedMilliseconds);
      sw.Restart(); ConstantArrayAnalysisPass.Run(module);                StageTimer.Record(timings, "constArr",   sw.ElapsedMilliseconds);
      sw.Restart(); ParameterMutationAnalysisPass.Run(module);            StageTimer.Record(timings, "paramMut",   sw.ElapsedMilliseconds);
      sw.Restart(); PurityAnalysisPass.Run(module);                       StageTimer.Record(timings, "purity",     sw.ElapsedMilliseconds);
      sw.Restart(); TypeCycleCheckPass.Run(module);                       StageTimer.Record(timings, "typeCycle",  sw.ElapsedMilliseconds);
      sw.Restart(); BorrowCheckPass.Run(module);                          StageTimer.Record(timings, "borrow",     sw.ElapsedMilliseconds);
      sw.Restart(); ValueTupleAbiPass.Run(module);                        StageTimer.Record(timings, "valueTuple", sw.ElapsedMilliseconds);
      sw.Restart(); StackPromotionAnalysisPass.Run(module);               StageTimer.Record(timings, "stackProm",  sw.ElapsedMilliseconds);
      sw.Restart(); LiteralCoverageAnalysisPass.Run(module, report: Compiler.LiteralCoverage); StageTimer.Record(timings, "litStatic", sw.ElapsedMilliseconds);
    } else {
      ParameterMutationAnalysisPass.Run(module);
      PurityAnalysisPass.Run(module);
      SemanticCheckPass.Run(module);
      MonomorphizationPass.Run(module);
      CloneSynthesisPass.Run(module);
      DeadFunctionElimination.Run(module);
      ConstantArrayAnalysisPass.Run(module);
      ParameterMutationAnalysisPass.Run(module);
      PurityAnalysisPass.Run(module);
      TypeCycleCheckPass.Run(module);
      BorrowCheckPass.Run(module);
      ValueTupleAbiPass.Run(module);
      StackPromotionAnalysisPass.Run(module);
      LiteralCoverageAnalysisPass.Run(module, report: Compiler.LiteralCoverage);
    }

    // Why LiteralCoverageAnalysisPass sits HERE (what it decides is stated in its own doc, once):
    // after monomorphization + DFE so it sees the concrete, reachable site set, after
    // ConstantArrayAnalysisPass so the empty-container factories it counts calls to are known, and
    // before lowering so literals are still MaxonStringLiteralOp etc. It is a whole-program union
    // find + call-graph fixpoint — linear in program size and cheap (see the litStatic timing).

    // Capture maxon stage
    if (returnIr || dumpStagesBasePath != null) {
      if (returnIr) {
        var ir = IrPrinter.Print(module, f => !f.IsStdlib);
        irBuilder!.AppendLine(PipelineStages.Header(PipelineStages.Maxon));
        irBuilder.Append(ir.TrimEnd());
        irBuilder.AppendLine();
      }
      if (dumpStagesBasePath != null) {
        File.WriteAllText($"{dumpStagesBasePath}.1-maxon.ir", IrPrinter.Print(module));
      }
    }

    TreeLock.Touch();

    IrModule<StandardOp> stdModule;
    if (timings != null) {
      var sw = new System.Diagnostics.Stopwatch();
      sw.Restart(); stdModule = MaxonToStandardConversion.Run(module, target); StageTimer.Record(timings, "lower:mx→std", sw.ElapsedMilliseconds);
      Logger.Debug(LogCategory.Ir, "Lowered Maxon to Standard");
      sw.Restart(); StoreForwardingPass.Run(stdModule);                    StageTimer.Record(timings, "storeFwd",     sw.ElapsedMilliseconds);
      sw.Restart(); DeadStoreEliminationPass.Run(stdModule);               StageTimer.Record(timings, "dse",          sw.ElapsedMilliseconds);
      sw.Restart(); ParameterRetentionAnalysisPass.Run(stdModule);         StageTimer.Record(timings, "paramRet",     sw.ElapsedMilliseconds);
      sw.Restart(); RefcountOptimizationPass.Run(stdModule);               StageTimer.Record(timings, "refcount",     sw.ElapsedMilliseconds);
      sw.Restart(); DeadStoreEliminationPass.Run(stdModule);               StageTimer.Record(timings, "dse",          sw.ElapsedMilliseconds);
    } else {
      stdModule = MaxonToStandardConversion.Run(module, target);
      Logger.Debug(LogCategory.Ir, "Lowered Maxon to Standard");
      StoreForwardingPass.Run(stdModule);
      DeadStoreEliminationPass.Run(stdModule);
      ParameterRetentionAnalysisPass.Run(stdModule);
      RefcountOptimizationPass.Run(stdModule);
      DeadStoreEliminationPass.Run(stdModule); // cleanup after refcount opt
    }

    // Capture standard stage
    if (returnIr || dumpStagesBasePath != null) {
      if (returnIr) {
        var ir = IrPrinter.Print(stdModule, f => !f.IsStdlib);
        irBuilder!.AppendLine(PipelineStages.Header(PipelineStages.Standard));
        irBuilder.Append(ir.TrimEnd());
        irBuilder.AppendLine();
      }
      if (dumpStagesBasePath != null) {
        File.WriteAllText($"{dumpStagesBasePath}.2-standard.ir", IrPrinter.Print(stdModule));
      }
    }

    TreeLock.Touch();

    if (target.Arch == CompileTarget.Arm64Arch) {
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
          irBuilder!.AppendLine(PipelineStages.Header(PipelineStages.ARM64));
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
    } else if (target.Arch == CompileTarget.X64Arch) {
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
          irBuilder!.AppendLine(PipelineStages.Header(PipelineStages.X86));
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

/// <summary>
/// The stage names in the multi-stage IR dump (<see cref="CompileResult.AllStagesIr"/>), the marker
/// that separates them, and every reader of that marker.
///
/// <para>The READERS live here, beside the four sites that WRITE the marker, because they had drifted
/// apart while nothing made them agree. There were three, each with its own spelling of the same
/// rule: <see cref="CompileResult.ArchIr"/> matched <c>"\n=== "</c> — so a marker on the very first
/// line was invisible to it — the spec runner's section parser matched a TRIMMED line, and the
/// stdlib-target self-test added a third with its own pair of constants. They agreed only because the
/// emitter happens to put a newline first and no leading whitespace. Change the marker at the four
/// writers and two of the three readers stop matching anything: one returns null, one reports every
/// section as missing, and neither says why.</para>
///
/// <para>Note what these readers can and cannot see: the dump is printed with
/// <c>f => !f.IsStdlib</c>, so stdlib and monomorphized bodies are NOT in it. A question about those
/// — a stdlib panic label, say — has to be asked of the emitted binary, not of this text.</para>
/// </summary>
public static class PipelineStages {
  public const string Maxon = "maxon";
  public const string Standard = "standard";
  public const string X86 = "x86";
  public const string ARM64 = "arm64";

  public static readonly string[] All = [Maxon, Standard, X86, ARM64];

  /// What separates one stage's dump from the next. Written by <see cref="IrPipeline.Run"/> via
  /// <see cref="Header"/>, read by everything below.
  private const string SectionMarker = "=== ";

  /// The header line introducing <paramref name="stage"/>'s dump.
  public static string Header(string stage) => SectionMarker + stage;

  /// <summary>
  /// The dump split into (stage name, body) in emission order.
  ///
  /// Bodies are re-joined with <c>\n</c> — the form the spec runner's IR comparison has always used,
  /// which is why <see cref="LastSectionBody"/> exists separately rather than being expressed on top
  /// of this: the fragment generator writes the last section into a committed golden and must not
  /// re-line-ending it.
  /// </summary>
  public static List<(string Name, string Body)> Split(string allStagesIr) {
    var sections = new List<(string Name, string Body)>();
    string? current = null;
    var body = new List<string>();

    foreach (var line in allStagesIr.Split(['\r', '\n'])) {
      var trimmed = line.Trim();

      if (trimmed.StartsWith(SectionMarker)) {
        if (current != null) sections.Add((current, string.Join("\n", body)));
        current = trimmed[SectionMarker.Length..].Trim();
        body.Clear();
        continue;
      }

      body.Add(line);
    }

    if (current != null) sections.Add((current, string.Join("\n", body)));

    return sections;
  }

  /// <summary>
  /// The last stage's dump — the architecture-specific one — with the ORIGINAL line endings intact,
  /// or null if the text carries no marker at all.
  ///
  /// It slices the input rather than rebuilding it from <see cref="Split"/> because its result is
  /// written verbatim into committed spec goldens: re-joining with <c>\n</c> would rewrite every
  /// fragment on a Windows host and call it a diff.
  /// </summary>
  public static string? LastSectionBody(string allStagesIr) {
    var lastMarker = allStagesIr.LastIndexOf('\n' + SectionMarker, StringComparison.Ordinal);
    if (lastMarker < 0) return null;

    var lineEnd = allStagesIr.IndexOf('\n', lastMarker + 1);
    if (lineEnd < 0) return null;

    return allStagesIr[(lineEnd + 1)..].TrimEnd();
  }
}
