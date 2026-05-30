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

    ParallelForCapture(funcs.Count, i => body(funcs[i]));
  }

  /// Parallel order-preserving map: runs <paramref name="body"/> over every
  /// item and returns the results indexed by input position. Used by passes
  /// whose per-item work is independent and produces a value (e.g. lowering a
  /// function into a new dialect) — the caller then assembles the results
  /// sequentially in input order, keeping output deterministic regardless of
  /// thread scheduling. Shares the sequential-threshold and deterministic
  /// error-rethrow semantics with <see cref="Run"/>.
  public static TOut[] Map<TIn, TOut>(IReadOnlyList<TIn> items, Func<TIn, TOut> body) {
    var results = new TOut[items.Count];
    if (items.Count < SequentialThreshold) {
      for (int i = 0; i < items.Count; i++) results[i] = body(items[i]);
      return results;
    }

    ParallelForCapture(items.Count, i => results[i] = body(items[i]));
    return results;
  }

  /// Runs <paramref name="body"/> over indices [0, count) in parallel, capturing
  /// any thrown exception per index rather than tearing down the whole partition,
  /// then re-throwing the lexicographically-first one (see
  /// <see cref="ThrowFirstDeterministic"/>). Shared by Run and Map; callers own
  /// the sequential small-input fast path.
  private static void ParallelForCapture(int count, Action<int> body) {
    var errors = new ConcurrentBag<Exception>();
    Parallel.For(0, count, i => {
      try {
        body(i);
      } catch (Exception ex) {
        // Capture, don't crash the whole partition; we re-throw deterministically below.
        errors.Add(ex);
      }
    });

    ThrowFirstDeterministic(errors);
  }

  /// Re-throws the lexicographically-first captured exception so parallel runs
  /// surface a stable error. CompileErrors sort by their string form; non-
  /// CompileError exceptions are surfaced first to make crashes loud.
  private static void ThrowFirstDeterministic(ConcurrentBag<Exception> errors) {
    if (errors.IsEmpty) return;
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
