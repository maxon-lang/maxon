namespace MaxonSharp.Debug;

/// <summary>
/// What every driver-side report's TEXT face must agree about, because every one of them is committed
/// as a DebugSamples transcript and diffed against a golden. <see cref="ReportPath"/> is the same idea
/// for the paths those reports print.
/// </summary>
public static class ReportFormat {

  /// <summary>
  /// The rule a report draws between its sections.
  ///
  /// ⚠ Deliberately NOT `---`, and this is a CONSTRAINT OF THE TRANSCRIPT FORMAT rather than either
  /// report's taste: `check-debug-goldens.sh` ends an expected block at the first line starting with
  /// `--- `, so a report that ruled its sections that way is silently TRUNCATED at its first section
  /// when a transcript of it is committed — the gate goes on passing while it has stopped asserting
  /// most of what it was written to assert.
  ///
  /// It is stated HERE because it has already been discovered twice. The coverage report was bitten by
  /// it and recorded the reason; the profile report, arriving later, had to re-derive the same reason
  /// and wrote its own copy of both the constant and the warning. A third report would have made three,
  /// and the failure mode is not a compile error — it is a golden that quietly asserts less.
  /// </summary>
  public const string SectionRule = "===";
}
