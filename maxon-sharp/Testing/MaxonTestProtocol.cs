using System.Globalization;
using System.Text;

namespace MaxonSharp.Testing;

/// <summary>
/// The wire protocol a generated test binary reports to <c>maxon test</c> through — written down
/// HERE and nowhere else.
///
/// Both ends are in this file's reach: <see cref="TestDispatcher"/> bakes these constants into the
/// Maxon source it synthesizes, and <see cref="Parse"/> reads what that source printed. A marker
/// spelled a second time in <c>stdlib/Testing.maxon</c> — or in the parser — would be the protocol
/// written down twice, and the two would drift the first time either side moved. That is why
/// <c>__TestReport.useWireFormat</c> takes the wrapper and separator as ARGUMENTS rather than
/// declaring them: the stdlib holds the mechanism, this holds the format.
///
/// Everything travels on STDERR, alongside <c>__TestReport.threw</c>, so protocol and error reports
/// stay in one stream and their ORDER is meaningful (a `threw` between a begin and an end belongs to
/// that test). A test's own <c>print</c> goes to stdout and can never be mistaken for protocol.
/// </summary>
internal static class MaxonTestProtocol {
  /// <summary>
  /// Brackets both ends of a protocol line so the runner can find it among whatever the test itself
  /// wrote to stderr. Distinctive rather than short: this is the string a chatty test must not
  /// accidentally emit at both ends of one line.
  /// </summary>
  public const string Wrapper = "@@maxon-test@@";

  /// <summary>
  /// Divides the fields inside one report. ASCII Unit Separator (0x1F) rather than a printable
  /// character because ONE field is not ours: <c>threw</c>'s <c>errorCase</c> is the thrown value
  /// rendered by the language's interpolation rule, and an enum may declare a STRING raw value
  /// containing any punctuation a printable delimiter could have used. A control character is the
  /// one class of byte such a value does not contain.
  /// </summary>
  public const char FieldSeparator = '\u001f';

  /// <summary>
  /// <see cref="FieldSeparator"/> as it must be SPELLED inside a Maxon string literal. The lexer
  /// wants exactly four hex digits (<c>\u001f</c>); it rejects <c>\u{1f}</c> by name. Kept beside
  /// character it encodes so the two cannot say different things.
  /// </summary>
  public const string FieldSeparatorInMaxonSource = "\\u001f";

  /// <summary>
  /// First field of a line the DISPATCHER wrote, marking a test about to run.
  ///
  /// <c>&gt;</c> and <c>&lt;</c> disambiguate structurally rather than by hope: the first field of a
  /// <c>threw</c> line is the callee's declared <c>throws</c> TYPE NAME, which is always a Maxon
  /// identifier, and no identifier begins with an angle bracket. So the tag alone decides which of
  /// the two shapes a line is, whatever a test printed.
  /// </summary>
  public const string BeganTag = ">";

  /// <summary>Marks a test that finished — see <see cref="BeganTag"/> for why the tag is a bracket.</summary>
  public const string EndedTag = "<";

  /// <summary>The <see cref="EndedTag"/> outcome for a test whose body returned normally.</summary>
  public const string OutcomePass = "pass";

  /// <summary>
  /// The <see cref="EndedTag"/> outcome for a test that threw. A test throws exactly
  /// <c>TestFailure</c> (a foreign error is converted by the compiler's substituted handler, which
  /// reports it first), so this covers both a failed assertion and an uncaught foreign error — the
  /// preceding <c>threw</c> line, if any, is what tells them apart.
  /// </summary>
  public const string OutcomeFail = "fail";

  /// <summary>
  /// The option the binary reads its selection from. Selection is a RUNTIME argument, never a
  /// compile-time one: every discovered test is compiled in every time, which is the whole reason a
  /// <c>--filter</c> change leaves the build cache warm.
  /// </summary>
  public const string SelectOptionName = "select";

  /// <summary>The <see cref="SelectOptionName"/> option as it appears in argv.</summary>
  public const string SelectFlag = $"--{SelectOptionName}=";

  /// <summary>
  /// The selection meaning "every test", used when the option is ABSENT — which is what a human
  /// running the binary by hand gets.
  ///
  /// It is a distinct token rather than the empty string because the runner needs to be able to
  /// select NOTHING. With absence and emptiness collapsed, a shard whose filter matched none of its
  /// tests would run all of them.
  /// </summary>
  public const string SelectAll = "*";

  /// <summary>
  /// Delimiter around each index in a selection, so a membership test is a plain substring search
  /// (<c>"|3|"</c> matches 3 and never 13 or 31).
  ///
  /// Public because the generated Maxon must bracket its search with the SAME character
  /// <see cref="FormatSelection"/> brackets its output with — a `-t` that selected test 3 and
  /// matched 13 would be exactly those two disagreeing. The dispatcher composes its needle from this
  /// constant directly rather than deriving one by rewriting a formatted example.
  /// </summary>
  public const char SelectIndexBracket = '|';

  /// <summary>
  /// Render a set of test indices as the value of <see cref="SelectFlag"/>.
  ///
  /// An EMPTY set produces an empty value, which selects nothing — deliberately distinct from
  /// <see cref="SelectAll"/>. A caller that wants every test says so with <see cref="SelectAll"/>.
  /// </summary>
  public static string FormatSelection(IEnumerable<int> indices) {
    var sb = new StringBuilder();
    foreach (var index in indices) {
      if (sb.Length == 0) sb.Append(SelectIndexBracket);
      sb.Append(index).Append(SelectIndexBracket);
    }
    return sb.ToString();
  }

  /// <summary>One line the test binary reported. Ordering between events is meaningful.</summary>
  internal abstract record TestEvent;

  /// <summary>A test is about to run. Written BEFORE the body, which is what makes a crash attributable.</summary>
  internal sealed record TestBegan(int Index) : TestEvent;

  /// <summary>A test finished under its own power, passing or failing.</summary>
  internal sealed record TestEnded(int Index, bool Passed, long Nanos) : TestEvent;

  /// <summary>
  /// An error reached the end of a test body without being handled, reported by the handler the
  /// compiler substitutes (see <c>specs/test-uncaught-throw.md</c>). It arrives BEFORE the
  /// <see cref="TestEnded"/> of the test it belongs to.
  /// </summary>
  internal sealed record TestThrew(string ErrorType, string ErrorCase, string File, int Line) : TestEvent;

  /// <summary>
  /// A stderr line that was NOT protocol — the test's own diagnostics, an assertion's message, or
  /// the runtime's panic text.
  ///
  /// It is an EVENT rather than a separate blob because its POSITION is the only thing that says
  /// which test wrote it. Collected to one side, the panic that explains a crash would arrive with
  /// no way to tell it from a passing test's chatter earlier in the same process.
  /// </summary>
  internal sealed record TestOutputLine(string Text) : TestEvent;

  /// <summary>How many fields each shape carries, so a malformed line is detected rather than indexed into.</summary>
  private const int BeganFieldCount = 2;
  private const int EndedFieldCount = 4;
  private const int ThrewFieldCount = 4;

  /// <summary>
  /// Read a test binary's stderr as one ORDERED stream of events — protocol lines and the program's
  /// own output interleaved exactly as it wrote them.
  ///
  /// A line bracketed by <see cref="Wrapper"/> at both ends that does not parse is passed through as
  /// ordinary output rather than throwing. This stream is a CRASHED process's dying words as often
  /// as not: it can be truncated mid-line by a write that never completed, and refusing to report
  /// anything because the last line was cut in half would lose the panic message that explains the
  /// crash. A dispatcher that had genuinely drifted shows up as a test that never ended, which is
  /// reported as a crash — loudly, and with this text attached.
  /// </summary>
  public static IReadOnlyList<TestEvent> Parse(string stderr) {
    var events = new List<TestEvent>();

    foreach (var rawLine in stderr.Split('\n')) {
      var line = rawLine.TrimEnd('\r');
      if (TryParseLine(line, out var parsed)) {
        events.Add(parsed);
        continue;
      }

      // A trailing empty line is an artifact of splitting on the final newline, not something the
      // program wrote; every other blank line it really did write is kept.
      if (line.Length > 0) events.Add(new TestOutputLine(line));
    }

    return events;
  }

  private static bool TryParseLine(string line, out TestEvent parsed) {
    parsed = null!;

    if (line.Length < Wrapper.Length * 2) return false;
    if (!line.StartsWith(Wrapper, StringComparison.Ordinal)) return false;
    if (!line.EndsWith(Wrapper, StringComparison.Ordinal)) return false;

    var body = line[Wrapper.Length..^Wrapper.Length];
    var fields = body.Split(FieldSeparator);
    if (fields.Length == 0) return false;

    switch (fields[0]) {
      case BeganTag:
        if (fields.Length != BeganFieldCount) return false;
        if (!TryParseIndex(fields[1], out var beganIndex)) return false;
        parsed = new TestBegan(beganIndex);
        return true;

      case EndedTag:
        if (fields.Length != EndedFieldCount) return false;
        if (!TryParseIndex(fields[1], out var endedIndex)) return false;
        if (fields[2] is not (OutcomePass or OutcomeFail)) return false;
        if (!long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var nanos)) return false;
        parsed = new TestEnded(endedIndex, fields[2] == OutcomePass, nanos);
        return true;

      default:
        // Not one of our tags, so it is a `threw` report — whose first field is an error TYPE NAME.
        // See BeganTag for why that cannot collide.
        if (fields.Length != ThrewFieldCount) return false;
        if (!int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var throwLine)) return false;
        parsed = new TestThrew(fields[0], fields[1], fields[2], throwLine);
        return true;
    }
  }

  private static bool TryParseIndex(string field, out int index) =>
    int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out index);
}
