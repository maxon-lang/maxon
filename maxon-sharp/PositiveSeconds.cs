namespace MaxonSharp;

/// <summary>
/// A duration a CALLER SPELLED IN SECONDS, validated the ONE way.
///
/// Three surfaces take one — `maxon debug --stop-timeout=`, `maxon mcp --idle-timeout=`, and the MCP
/// `debug_start` tool's `stopTimeoutSeconds` — and all three mean exactly the same thing by it: a
/// deadline the code will actually wait on. They had three copies of the rule and the copies had
/// already diverged, in the direction that matters:
///
///   * the CLI checked the CONVERTED <see cref="TimeSpan"/>, so `--stop-timeout=1e-9` was refused —
///     it rounds to zero ticks, and a zero deadline times out every wait before the target could
///     possibly stop;
///   * the two MCP copies checked the DOUBLE, so `--idle-timeout=1e-9` was accepted and the server
///     announced "idle sessions reaped after 0s" — every session reaped on the first sweep — while
///     `stopTimeoutSeconds: 1e-9` gave a debugger that could never reach any breakpoint. (Measured,
///     not reasoned: the MCP copy's own comment claimed it refused "exactly as the CLI flag is".)
///
/// So the rule is stated here once, in the strict form: the value the caller gets is the value that
/// was checked. <see cref="TimeSpan.FromSeconds(double)"/> THROWS above <see cref="TimeSpan.MaxValue"/>,
/// which a validator whose whole job is to say no must not do, so the conversion is guarded rather
/// than pre-bounded by a comparison that rounding can slip past.
///
/// The refusal WORDING stays with each caller, because their destinations differ (stderr under a
/// command's own name, or a JSON-RPC `invalidParams`); only the shared phrase is here, so three
/// messages cannot come to describe one rule three ways.
/// </summary>
internal static class PositiveSeconds {

  /// The half-sentence every refusal is built around, so the three surfaces describe one rule alike.
  public const string RequirementText = "needs a positive number of seconds";

  /// Parse a caller-spelled decimal number of seconds. Fractions are accepted so a gate can drive a
  /// sub-second deadline deterministically.
  public static bool TryParse(string text, out TimeSpan value) {
    value = default;
    return double.TryParse(text, System.Globalization.NumberStyles.Float,
      System.Globalization.CultureInfo.InvariantCulture, out var seconds) && TryFrom(seconds, out value);
  }

  /// <summary>
  /// The rule itself: a finite, positive number of seconds that survives conversion as a positive
  /// duration. A value that rounds away to zero is REFUSED rather than clamped — a zero deadline is a
  /// timer that has already expired, which every caller here would report as a configured setting
  /// rather than as the broken one it is.
  /// </summary>
  public static bool TryFrom(double seconds, out TimeSpan value) {
    value = default;
    if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return false;

    try {
      value = TimeSpan.FromSeconds(seconds);
    } catch (OverflowException) {
      return false;
    }

    return value > TimeSpan.Zero;
  }
}
