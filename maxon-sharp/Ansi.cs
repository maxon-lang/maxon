namespace MaxonSharp;

/// <summary>
/// When a report may use colour, and how. Asked once per run; every renderer is handed the answer
/// rather than deciding for itself.
/// </summary>
internal enum ColorSetting {
  /// <summary>Colour when stdout is a terminal that wants it. See <see cref="Ansi.WantsColor"/>.</summary>
  Auto,

  /// <summary>Colour regardless — for a caller piping into something that renders escapes.</summary>
  Always,

  /// <summary>Never colour.</summary>
  Never,
}

/// <summary>
/// SGR escape sequences, and the one rule for whether to emit them.
///
/// The rule matters more than the codes: a report whose text face is a committed golden must be
/// BYTE-IDENTICAL when redirected, and <c>scripts/check-debug-goldens.sh</c> captures stdout to a
/// file. Detecting redirection is therefore not a nicety — it is what lets colour exist at all
/// without a flag every golden would have to remember to pass.
/// </summary>
internal static class Ansi {
  private const string Escape = "\u001b[";
  private const string ResetCode = "0m";

  /// <summary>
  /// The environment variable that disables colour by mere PRESENCE, whatever its value —
  /// <c>no-color.org</c>'s rule, and deliberately not "presence and non-empty": users set it to the
  /// empty string.
  /// </summary>
  private const string NoColorVariable = "NO_COLOR";

  /// <summary>The terminal type that means "assume nothing", so nothing is what we assume.</summary>
  private const string DumbTerminal = "dumb";
  private const string TermVariable = "TERM";

  private const string BoldCode = "1m";
  private const string DimCode = "2m";
  private const string RedCode = "31m";
  private const string GreenCode = "32m";
  private const string YellowCode = "33m";

  /// <summary>
  /// Whether <see cref="ColorSetting.Auto"/> resolves to colour. Three conditions, all required, and
  /// each rules out a different way of being wrong: a redirected stream is being CAPTURED (a file, a
  /// pipe, a golden), <c>NO_COLOR</c> is the user saying no across every tool at once, and
  /// <c>TERM=dumb</c> is the terminal itself saying it cannot render this.
  /// </summary>
  public static bool WantsColor() {
    if (Console.IsOutputRedirected) return false;
    if (Environment.GetEnvironmentVariable(NoColorVariable) != null) return false;
    return !string.Equals(Environment.GetEnvironmentVariable(TermVariable), DumbTerminal,
      StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>Resolve a setting to the single boolean every renderer actually needs.</summary>
  public static bool Enabled(ColorSetting setting) => setting switch {
    ColorSetting.Auto => WantsColor(),
    ColorSetting.Always => true,
    ColorSetting.Never => false,
    _ => throw new ArgumentOutOfRangeException(nameof(setting), setting,
      "Unhandled colour setting; every setting must state whether it colours."),
  };

  /// <summary>
  /// Every accepted <c>--color=</c> value and what it means. THE list: <see cref="TryParse"/>
  /// matches against it and <see cref="Choices"/> is joined from it, so the usage text cannot name a
  /// spelling the parser has stopped accepting. Written down twice — a switch here and a literal
  /// there — renaming one value leaves the help advertising a flag value that is then refused, and
  /// both texts look equally authoritative.
  /// </summary>
  private static readonly (string Name, ColorSetting Setting)[] Accepted = [
    ("auto", ColorSetting.Auto),
    ("always", ColorSetting.Always),
    ("never", ColorSetting.Never),
  ];

  /// <summary>
  /// Parse the value of <c>--color=</c>. An unknown value is REFUSED by the caller rather than
  /// falling back to auto: a misspelled <c>--color=alwyas</c> that silently means something else is
  /// the flag failing at the one job it has.
  /// </summary>
  public static bool TryParse(string value, out ColorSetting setting) {
    foreach (var (name, accepted) in Accepted) {
      if (!string.Equals(value, name, StringComparison.Ordinal)) continue;
      setting = accepted;
      return true;
    }

    setting = ColorSetting.Auto;
    return false;
  }

  /// <summary>The accepted <c>--color=</c> values, for the usage text — spelled from one list.</summary>
  public static string Choices => string.Join('|', Accepted.Select(a => a.Name));

  /// <summary>
  /// Wrap <paramref name="text"/> in an SGR code, or return it untouched when colour is off.
  ///
  /// Every helper below funnels through here, so "colour off produces the exact bytes colour on
  /// would minus the escapes" is a property of one function rather than a promise repeated at each
  /// call site.
  /// </summary>
  private static string Paint(string text, string code, bool enabled) =>
    enabled ? $"{Escape}{code}{text}{Escape}{ResetCode}" : text;

  public static string Red(string text, bool enabled) => Paint(text, RedCode, enabled);
  public static string Green(string text, bool enabled) => Paint(text, GreenCode, enabled);
  public static string Yellow(string text, bool enabled) => Paint(text, YellowCode, enabled);
  public static string Bold(string text, bool enabled) => Paint(text, BoldCode, enabled);
  public static string Dim(string text, bool enabled) => Paint(text, DimCode, enabled);
}
