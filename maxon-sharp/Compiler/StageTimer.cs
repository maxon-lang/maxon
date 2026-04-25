namespace MaxonSharp.Compiler;

// Opt-in per-stage compile timer. The top-level compile path checks Enabled
// once and either runs the passes plainly (zero overhead) or runs them wrapped
// in Stopwatch calls. This is NOT gated by the Logger — allocating strings
// and touching category dictionaries on every pass call is itself measurable.
internal static class StageTimer {
  public static bool Enabled;

  // When > 0, heavy passes (monomorph, refcount, borrow) record per-function
  // wall time and print the top-N at pass end. Off by default — zero overhead
  // unless the caller opts in via --timing-functions=N. Enabling this implies
  // Enabled too.
  public static int HotFunctions;

  // Total token count summed across all lexed source files for this compile.
  // Surfaced on the Parse: line so we can compute tokens/sec throughput.
  public static long TokensLexed;

  public static void Record(Dictionary<string, long> timings, string name, long ms) {
    timings.TryGetValue(name, out var prev);
    timings[name] = prev + ms;
  }

  public static string Format(Dictionary<string, long> timings) {
    var sb = new System.Text.StringBuilder();
    foreach (var (k, v) in timings) sb.Append($" {k}={v}ms");
    return sb.ToString();
  }

  // Print the top-N functions by recorded time for a single pass. The list is
  // expected to contain one entry per function processed — duplicates are
  // summed before sorting so the headline is "function X cost the pass Yms".
  public static void PrintHotFunctions(string passName, List<(string Name, long Ms)> samples) {
    if (HotFunctions <= 0 || samples.Count == 0) return;
    var byFunc = new Dictionary<string, long>(samples.Count);
    foreach (var (name, ms) in samples) {
      byFunc.TryGetValue(name, out var prev);
      byFunc[name] = prev + ms;
    }
    var ranked = byFunc.OrderByDescending(kv => kv.Value).Take(HotFunctions).ToList();
    var sb = new System.Text.StringBuilder();
    sb.Append($"  {passName} hot:");
    foreach (var (name, ms) in ranked) sb.Append($" {name}={ms}ms");
    Console.Error.WriteLine(sb.ToString());
  }
}
