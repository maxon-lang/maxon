using System.Text;

namespace MaxonSharp;

/// <summary>
/// The interactive line-input layer for the `maxon debug` REPL (P4c) — tab-completion, persistent
/// history (↑/↓ + a history file), and Ctrl-R reverse-search, with a graceful plain-<c>ReadLine</c>
/// fallback when stdin is not a TTY. The key-loop is deliberately thin: all the completion INTELLIGENCE
/// lives in the pure <see cref="DebugCompletion"/> engine, so the same logic drives the interactive Tab
/// key, the non-interactive <c>maxon debug --complete</c> gate, and (later) a DAP/TUI completion request.
///
/// The key-loop cannot be exercised by <c>--batch</c> (it needs a real console), so it is verified by
/// hand; the completion engine and the fuzzy matcher — the parts that carry the actual logic — are pure
/// functions gated by <c>--complete</c> and the function-break resolver.
/// </summary>
internal sealed class LineEditor {

  /// Most-recent commands kept in memory AND on disk. Bounds the history file so a long-lived
  /// `.maxon_debug_history` cannot grow without limit.
  private const int MaxHistoryEntries = 500;

  /// The history file, or null when no home directory could be resolved (history then lives only for
  /// the session, in memory). Guarded I/O — a failure to read or write it never crashes the REPL.
  private readonly string? _historyPath;
  private readonly List<string> _history = [];

  /// A history-file write is reported ONCE, not on every submit, so a read-only home directory does not
  /// bury the JSON/REPL output under a warning per command.
  private bool _historyWriteWarned;

  public LineEditor(string? historyPath) {
    _historyPath = historyPath;
    LoadHistory();
  }

  /// <summary>
  /// Read one line at <paramref name="prompt"/>. On a TTY this is the full editor (completion via
  /// <paramref name="contextProvider"/>, history, reverse-search); when stdin is redirected (piped
  /// input, no console) it degrades to a plain <see cref="TextReader.ReadLine"/> with no key handling —
  /// the graceful fallback the design requires. Returns the submitted line, or null on EOF.
  /// </summary>
  public string? ReadLine(string prompt, Func<CompletionContext> contextProvider) {
    if (Console.IsInputRedirected) return ReadLineFallback(prompt);
    try {
      return ReadLineInteractive(prompt, contextProvider);
    } catch (InvalidOperationException) {
      // No real console behind a non-redirected handle (rare): fall back rather than fault.
      return ReadLineFallback(prompt);
    }
  }

  private static string? ReadLineFallback(string prompt) {
    Console.Out.Write(prompt);
    Console.Out.Flush();
    return Console.In.ReadLine();
  }

  // ---- Interactive key-loop (TTY only; manually verified) ----

  private string? ReadLineInteractive(string prompt, Func<CompletionContext> contextProvider) {
    var buf = new StringBuilder();
    int cursor = 0;
    int historyIndex = _history.Count;   // == count means "the fresh line below the newest entry"
    string draft = "";                    // the in-progress line, preserved while browsing history
    int lastLen = 0;

    Render(prompt, buf, cursor, ref lastLen);
    while (true) {
      var key = Console.ReadKey(intercept: true);

      // Ctrl-D on an empty line is EOF, mirroring the fallback ReadLine returning null there.
      if (IsCtrl(key, ConsoleKey.D) && buf.Length == 0) {
        Console.Out.WriteLine();
        return null;
      }

      if (key.Key == ConsoleKey.Enter) {
        Console.Out.WriteLine();
        var line = buf.ToString();
        Remember(line);
        return line;
      }

      if (key.Key == ConsoleKey.Tab) {
        DoCompletion(prompt, buf, ref cursor, ref lastLen, contextProvider);
        continue;
      }

      if (IsCtrl(key, ConsoleKey.R)) {
        // Ctrl-R + Enter on a match submits that history line exactly as a typed Enter would; any other
        // exit returns null and leaves the (possibly edited) line for normal editing.
        if (DoReverseSearch(prompt, buf, ref cursor, ref lastLen) is { } chosen) {
          Remember(chosen);
          return chosen;
        }
        continue;
      }

      if (HandleEditKey(key, buf, ref cursor, ref historyIndex, ref draft)) {
        Render(prompt, buf, cursor, ref lastLen);
        continue;
      }

      if (!char.IsControl(key.KeyChar)) {
        buf.Insert(cursor, key.KeyChar);
        cursor++;
        Render(prompt, buf, cursor, ref lastLen);
      }
    }
  }

  /// The cursor-movement / deletion / history keys. Returns true when the key was one of them (so the
  /// caller redraws); false leaves it for the printable-insert path. History browsing preserves the
  /// in-progress <paramref name="draft"/> so ↓ back past the newest entry restores what was being typed.
  private bool HandleEditKey(ConsoleKeyInfo key, StringBuilder buf, ref int cursor,
      ref int historyIndex, ref string draft) {
    switch (key.Key) {
      case ConsoleKey.Backspace:
        if (cursor > 0) { buf.Remove(cursor - 1, 1); cursor--; }
        return true;
      case ConsoleKey.Delete:
        if (cursor < buf.Length) buf.Remove(cursor, 1);
        return true;
      case ConsoleKey.LeftArrow:
        if (cursor > 0) cursor--;
        return true;
      case ConsoleKey.RightArrow:
        if (cursor < buf.Length) cursor++;
        return true;
      case ConsoleKey.Home:
        cursor = 0;
        return true;
      case ConsoleKey.End:
        cursor = buf.Length;
        return true;
      case ConsoleKey.UpArrow:
        RecallHistory(buf, ref cursor, ref historyIndex, ref draft, delta: -1);
        return true;
      case ConsoleKey.DownArrow:
        RecallHistory(buf, ref cursor, ref historyIndex, ref draft, delta: +1);
        return true;
      default:
        // Ctrl-A / Ctrl-E: the readline home/end most shells share.
        if (IsCtrl(key, ConsoleKey.A)) { cursor = 0; return true; }
        if (IsCtrl(key, ConsoleKey.E)) { cursor = buf.Length; return true; }
        if (IsCtrl(key, ConsoleKey.U)) { buf.Clear(); cursor = 0; return true; }
        return false;
    }
  }

  private void RecallHistory(StringBuilder buf, ref int cursor, ref int historyIndex,
      ref string draft, int delta) {
    if (_history.Count == 0) return;

    // Leaving the fresh line (index == count) upward stashes the draft; returning to it restores it.
    if (historyIndex == _history.Count && delta < 0) draft = buf.ToString();

    int next = historyIndex + delta;
    if (next < 0 || next > _history.Count) return;
    historyIndex = next;

    buf.Clear();
    buf.Append(historyIndex == _history.Count ? draft : _history[historyIndex]);
    cursor = buf.Length;
  }

  private static void DoCompletion(string prompt, StringBuilder buf, ref int cursor,
      ref int lastLen, Func<CompletionContext> contextProvider) {
    var candidates = DebugCompletion.Complete(buf.ToString(), cursor, contextProvider());
    if (candidates.Count == 0) return;

    int wordStart = WordStart(buf, cursor);
    string word = buf.ToString(wordStart, cursor - wordStart);

    if (candidates.Count == 1) {
      ReplaceWord(buf, ref cursor, wordStart, candidates[0]);
    } else {
      string common = DebugCompletion.LongestCommonPrefix(candidates);
      if (common.Length > word.Length) {
        ReplaceWord(buf, ref cursor, wordStart, common);
      } else {
        // No further common prefix to fill in: show the choices, then redraw the prompt beneath them.
        Console.Out.WriteLine();
        Console.Out.WriteLine(string.Join("   ", candidates));
        lastLen = 0;
      }
    }
    Render(prompt, buf, cursor, ref lastLen);
  }

  private static void ReplaceWord(StringBuilder buf, ref int cursor, int wordStart, string replacement) {
    buf.Remove(wordStart, cursor - wordStart);
    buf.Insert(wordStart, replacement);
    cursor = wordStart + replacement.Length;
  }

  /// The start of the whitespace-delimited word ending at <paramref name="cursor"/> — the span Tab
  /// completes, matching how <see cref="DebugCompletion"/> isolates the word it filters against.
  private static int WordStart(StringBuilder buf, int cursor) {
    int i = cursor;
    while (i > 0 && !char.IsWhiteSpace(buf[i - 1])) i--;
    return i;
  }

  // ---- Ctrl-R reverse incremental search ----

  /// Run the Ctrl-R incremental reverse search. Returns the chosen history line when Enter accepts a
  /// match (the caller submits it); returns null on cancel or on accept-into-buffer-for-editing, having
  /// redrawn the ordinary prompt so the key-loop resumes cleanly.
  private string? DoReverseSearch(string prompt, StringBuilder buf, ref int cursor, ref int lastLen) {
    var query = new StringBuilder();
    int matchIndex = -1;
    int searchLen = 0;

    RenderSearch(query.ToString(), "", ref searchLen);
    while (true) {
      var key = Console.ReadKey(intercept: true);

      if (key.Key == ConsoleKey.Enter) {
        // Accept the current match and submit it (bash semantics); no match falls back to editing.
        if (matchIndex >= 0) {
          Console.Out.WriteLine();
          return _history[matchIndex];
        }
        break;
      }

      if (key.Key == ConsoleKey.Escape || IsCtrl(key, ConsoleKey.G)) break;   // cancel: keep the original line

      if (IsCtrl(key, ConsoleKey.R)) {
        // Ctrl-R again: step to the next older match.
        int from = matchIndex < 0 ? _history.Count - 1 : matchIndex - 1;
        int found = FindMatch(query.ToString(), from);
        if (found >= 0) matchIndex = found;
        RenderSearch(query.ToString(), matchIndex >= 0 ? _history[matchIndex] : "", ref searchLen);
        continue;
      }

      if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Home or ConsoleKey.End) {
        // Editing keys accept the match onto the line and leave search for normal editing.
        if (matchIndex >= 0) { buf.Clear(); buf.Append(_history[matchIndex]); cursor = buf.Length; }
        break;
      }

      if (key.Key == ConsoleKey.Backspace) {
        if (query.Length > 0) query.Remove(query.Length - 1, 1);
        matchIndex = FindMatch(query.ToString(), _history.Count - 1);
        RenderSearch(query.ToString(), matchIndex >= 0 ? _history[matchIndex] : "", ref searchLen);
        continue;
      }

      if (!char.IsControl(key.KeyChar)) {
        query.Append(key.KeyChar);
        matchIndex = FindMatch(query.ToString(), _history.Count - 1);
        RenderSearch(query.ToString(), matchIndex >= 0 ? _history[matchIndex] : "", ref searchLen);
      }
    }

    // Non-submit exit: erase the search line and repaint the ordinary prompt for continued editing.
    Console.Out.Write('\r');
    Console.Out.Write(new string(' ', searchLen));
    lastLen = 0;
    Render(prompt, buf, cursor, ref lastLen);
    return null;
  }

  /// The most recent history entry at or before <paramref name="from"/> containing <paramref name="query"/>
  /// as a substring, or -1. An empty query matches nothing (the search line shows no candidate yet).
  private int FindMatch(string query, int from) {
    if (query.Length == 0) return -1;
    for (int i = Math.Min(from, _history.Count - 1); i >= 0; i--)
      if (_history[i].Contains(query, StringComparison.Ordinal)) return i;
    return -1;
  }

  private static void RenderSearch(string query, string match, ref int lastLen) {
    Console.Out.Write('\r');
    string line = $"(reverse-i-search)'{query}': {match}";
    Console.Out.Write(line);
    if (line.Length < lastLen) Console.Out.Write(new string(' ', lastLen - line.Length));
    lastLen = line.Length;
    Console.Out.Flush();
  }

  // ---- Rendering ----

  /// Repaint the single prompt line: return to column 0, write the prompt and buffer, erase any tail left
  /// by a now-shorter line, then place the cursor. Single-line by design — a buffer that wraps the console
  /// width is a known limitation of this MVP editor (noted for P4c), not a correctness concern for the
  /// short commands a debugger prompt takes.
  private static void Render(string prompt, StringBuilder buf, int cursor, ref int lastLen) {
    Console.Out.Write('\r');
    Console.Out.Write(prompt);
    Console.Out.Write(buf.ToString());
    int shown = prompt.Length + buf.Length;
    if (shown < lastLen) Console.Out.Write(new string(' ', lastLen - shown));
    lastLen = shown;
    Console.Out.Flush();

    int target = prompt.Length + cursor;
    try {
      if (target < Console.BufferWidth) Console.CursorLeft = target;
    } catch (Exception) {
      // A redirected/height-less console rejects cursor positioning; leaving it at the end is harmless.
    }
  }

  // ---- History ----

  /// Record a submitted line: skip blanks, dedup a line identical to the previous one, cap the buffer, and
  /// persist. Called from both Enter and Ctrl-R-Enter so the two submit paths remember identically.
  private void Remember(string line) {
    if (line.Trim().Length == 0) return;
    if (_history.Count > 0 && _history[^1] == line) return;

    _history.Add(line);
    if (_history.Count > MaxHistoryEntries) _history.RemoveRange(0, _history.Count - MaxHistoryEntries);
    SaveHistory();
  }

  private void LoadHistory() {
    if (_historyPath == null || !File.Exists(_historyPath)) return;
    try {
      var lines = File.ReadAllLines(_historyPath);
      int start = Math.Max(0, lines.Length - MaxHistoryEntries);
      for (int i = start; i < lines.Length; i++)
        if (lines[i].Length > 0) _history.Add(lines[i]);
    } catch (Exception ex) {
      Console.Error.WriteLine($"maxon debug: could not read command history ({ex.Message}); continuing without it.");
    }
  }

  private void SaveHistory() {
    if (_historyPath == null) return;
    try {
      File.WriteAllLines(_historyPath, _history);
    } catch (Exception ex) {
      if (!_historyWriteWarned) {
        Console.Error.WriteLine($"maxon debug: could not save command history ({ex.Message}); continuing without it.");
        _historyWriteWarned = true;
      }
    }
  }

  private static bool IsCtrl(ConsoleKeyInfo key, ConsoleKey which) =>
    key.Key == which && (key.Modifiers & ConsoleModifiers.Control) != 0;
}

/// <summary>
/// What the completion engine may offer at the current position, resolved by the REPL's ONE command
/// classifier so the alias set (`b`↔`break`, `p`↔`print`) is never restated here.
/// </summary>
internal enum CompletionArgTarget {
  /// The command takes no completable argument (run, continue, backtrace, …).
  None,
  /// A `break` target — function names and file names.
  FunctionsAndFiles,
  /// A `print` / `locals` target — the stopped frame's local names.
  Locals,
}

/// <summary>
/// The name pools the pure completion engine draws from, plus the REPL's classifier that maps a command
/// word to the pool its argument completes against. Carrying the classifier (rather than re-deriving the
/// command vocabulary here) keeps the alias set single-sourced in <c>MaxonDebugRepl.ParseCommand</c>.
/// </summary>
internal readonly record struct CompletionContext(
  IReadOnlyList<string> Commands,
  IReadOnlyList<string> Functions,
  IReadOnlyList<string> Files,
  IReadOnlyList<string> Locals,
  Func<string, CompletionArgTarget> ClassifyArg);

/// <summary>
/// The context-aware completion ENGINE — a pure function, with no console I/O, so the interactive Tab key
/// (<see cref="LineEditor"/>) and the non-interactive <c>maxon debug --complete</c> gate share exactly one
/// implementation and it is batch-testable. Given a line, a cursor, and the available name pools it returns
/// the sorted, de-duplicated candidates whose text extends the word under the cursor:
///
///   * at the command word (the first token) — command names;
///   * after `break` — function and file names;
///   * after `print` / `locals` — the stopped frame's local names.
/// </summary>
internal static class DebugCompletion {

  public static IReadOnlyList<string> Complete(string line, int cursor, CompletionContext ctx) {
    if (cursor < 0) cursor = 0;
    if (cursor > line.Length) cursor = line.Length;

    int wordStart = cursor;
    while (wordStart > 0 && !char.IsWhiteSpace(line[wordStart - 1])) wordStart--;
    string prefix = line[wordStart..cursor];

    var pool = PoolFor(line, wordStart, ctx);
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var result = new List<string>();
    foreach (var name in pool) {
      if (name.Length == 0 || !name.StartsWith(prefix, StringComparison.Ordinal)) continue;
      if (seen.Add(name)) result.Add(name);
    }
    result.Sort(StringComparer.Ordinal);
    return result;
  }

  /// The pool the word under the cursor draws from: commands when it IS the command word (everything before
  /// it is whitespace), otherwise the pool the first word's <see cref="CompletionArgTarget"/> selects.
  private static IReadOnlyList<string> PoolFor(string line, int wordStart, CompletionContext ctx) {
    bool commandPosition = line[..wordStart].Trim().Length == 0;
    if (commandPosition) return ctx.Commands;

    string firstWord = FirstWord(line);
    return ctx.ClassifyArg(firstWord) switch {
      CompletionArgTarget.FunctionsAndFiles => Concat(ctx.Functions, ctx.Files),
      CompletionArgTarget.Locals => ctx.Locals,
      CompletionArgTarget.None => [],
      _ => throw new InvalidOperationException("unhandled completion target"),
    };
  }

  private static string FirstWord(string line) {
    int i = 0;
    while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
    int j = i;
    while (j < line.Length && !char.IsWhiteSpace(line[j])) j++;
    return line[i..j];
  }

  private static IReadOnlyList<string> Concat(IReadOnlyList<string> a, IReadOnlyList<string> b) {
    var list = new List<string>(a.Count + b.Count);
    list.AddRange(a);
    list.AddRange(b);
    return list;
  }

  /// The longest shared leading run of the candidates — how far Tab fills in on a multi-match completion
  /// before it has to list the choices.
  public static string LongestCommonPrefix(IReadOnlyList<string> items) {
    if (items.Count == 0) return "";
    string prefix = items[0];
    for (int i = 1; i < items.Count && prefix.Length > 0; i++) {
      var s = items[i];
      int k = 0;
      while (k < prefix.Length && k < s.Length && prefix[k] == s[k]) k++;
      prefix = prefix[..k];
    }
    return prefix;
  }
}

/// <summary>
/// The ONE fuzzy matcher — a Levenshtein edit distance with a small threshold — shared by every "did you
/// mean" the REPL offers: an unknown command word and an unresolved `break &lt;function&gt;` both suggest
/// their closest known name through here, so the two cannot use different notions of "closest".
/// </summary>
internal static class DebugFuzzy {

  /// The largest edit distance a suggestion may be from the input. Small on purpose: it catches ordinary
  /// typos (a dropped/added/swapped character) without volunteering an unrelated word for a genuine miss.
  public const int MaxEditDistance = 2;

  /// The candidate closest to <paramref name="target"/> within <paramref name="maxDistance"/>, or null when
  /// none is that close. Ties break to the ordinally-smallest candidate, so the suggestion is deterministic.
  public static string? ClosestMatch(string target, IEnumerable<string> candidates, int maxDistance) {
    string? best = null;
    int bestDistance = int.MaxValue;
    foreach (var candidate in candidates) {
      if (candidate.Length == 0) continue;
      int d = Distance(target, candidate);
      if (d < bestDistance || (d == bestDistance && best != null && string.CompareOrdinal(candidate, best) < 0)) {
        bestDistance = d;
        best = candidate;
      }
    }
    return bestDistance <= maxDistance ? best : null;
  }

  /// Classic two-row Levenshtein distance (insert / delete / substitute all cost 1).
  private static int Distance(string a, string b) {
    if (a.Length == 0) return b.Length;
    if (b.Length == 0) return a.Length;

    var prev = new int[b.Length + 1];
    var cur = new int[b.Length + 1];
    for (int j = 0; j <= b.Length; j++) prev[j] = j;

    for (int i = 1; i <= a.Length; i++) {
      cur[0] = i;
      for (int j = 1; j <= b.Length; j++) {
        int substitution = prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
        cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), substitution);
      }
      (prev, cur) = (cur, prev);
    }
    return prev[b.Length];
  }
}
