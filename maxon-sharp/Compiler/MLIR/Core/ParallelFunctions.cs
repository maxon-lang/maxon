using System.Collections.Concurrent;

namespace MaxonSharp.Compiler.Ir.Core;

/// <summary>
/// Helper for running per-function pass bodies in parallel. Used by passes
/// whose per-function work is independent and only mutates state local to the
/// function (or thread-safe shared state via the supplied funcLookup).
///
/// Why a helper rather than a raw Parallel.ForEach at each call site:
///   - one place to tune partitioning / disable on small modules
///   - error capture: passes throw CompileError on user errors; we collect
///     them deterministically and re-throw the lexicographically-first one so
///     parallel runs surface a stable error
///   - timing integration: when StageTimer.HotFunctions > 0 the per-function
///     wall-time samples are accumulated thread-safely
/// </summary>
internal static class ParallelFunctions {
  // Below this threshold, sequential execution is faster than the partitioning
  // and synchronization overhead. Calibrated empirically — most pipeline
  // passes process 2000+ functions on the self-hosted module so this almost
  // never trips, but small test modules benefit from skipping the parallel
  // path entirely.
  private const int SequentialThreshold = 32;

  public static void Run<TOp>(IrModule<TOp> module, Action<IrFunction<TOp>> body)
      where TOp : IPrintableOp {
    var funcs = module.Functions;
    if (funcs.Count < SequentialThreshold) {
      foreach (var f in funcs) body(f);
      return;
    }

    ConcurrentBag<Exception>? errors = null;
    Parallel.ForEach(funcs, f => {
      try {
        body(f);
      } catch (Exception ex) {
        // Capture, don't crash the whole partition; we re-throw deterministically below.
        (errors ??= []).Add(ex);
      }
    });

    if (errors == null || errors.IsEmpty) return;

    // Pick the error with the smallest sort-key (file path then line then
    // column then message) so parallel runs are reproducible. CompileErrors
    // sort by their string form; non-CompileError exceptions are surfaced
    // first to make crashes loud.
    var list = errors.ToArray();
    Array.Sort(list, (a, b) => {
      bool ac = a is CompileError, bc = b is CompileError;
      if (ac != bc) return ac ? 1 : -1; // non-CompileError first
      return string.CompareOrdinal(a.ToString(), b.ToString());
    });
    throw list[0];
  }

  /// Variant that also feeds per-function wall time into a hot-functions list
  /// when --timing-functions=N is on. Sequential when small or when timing is
  /// disabled (no point synchronizing if no consumer).
  public static void Run<TOp>(IrModule<TOp> module, Action<IrFunction<TOp>> body,
      List<(string Name, long Ms)>? hotSamples)
      where TOp : IPrintableOp {
    if (hotSamples == null) {
      Run(module, body);
      return;
    }

    var bag = new ConcurrentBag<(string, long)>();
    Run(module, f => {
      var sw = System.Diagnostics.Stopwatch.StartNew();
      body(f);
      bag.Add((f.Name, sw.ElapsedMilliseconds));
    });
    foreach (var entry in bag) hotSamples.Add(entry);
  }
}
