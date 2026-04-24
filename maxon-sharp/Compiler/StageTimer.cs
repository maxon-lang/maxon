namespace MaxonSharp.Compiler;

// Opt-in per-stage compile timer. The top-level compile path checks Enabled
// once and either runs the passes plainly (zero overhead) or runs them wrapped
// in Stopwatch calls. This is NOT gated by the Logger — allocating strings
// and touching category dictionaries on every pass call is itself measurable.
internal static class StageTimer {
  public static bool Enabled;

  // Shared accumulator for per-Parse() sub-stage timings. Set by Compile()
  // before invoking CompileSources, consumed + printed + cleared after. Null
  // when --timing is off, so Parse() can short-circuit without allocating.
  public static Dictionary<string, long>? ParseDetail;

  public static void Record(Dictionary<string, long> timings, string name, long ms) {
    timings.TryGetValue(name, out var prev);
    timings[name] = prev + ms;
  }

  public static string Format(Dictionary<string, long> timings) {
    var sb = new System.Text.StringBuilder();
    foreach (var (k, v) in timings) sb.Append($" {k}={v}ms");
    return sb.ToString();
  }
}
