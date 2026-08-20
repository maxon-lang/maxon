namespace MaxonSharp.Testing;

/// <summary>
/// The self-test for <see cref="Lsp.MaxonFormatter"/>, wired as `maxon fmt-selftest` and run by every
/// `dotnet build`, in the same idiom as `golden-mint-selftest` and `stdlib-target-selftest`.
///
/// <para>⛔⛔ IT EXISTS BECAUSE `maxon fmt` SILENTLY DELETED COMMENTS FOR AN UNKNOWN LENGTH OF TIME,
/// AND NOTHING IN THE TREE COULD HAVE NOTICED. Measured 2026-08-20 on a pristine copy of
/// `maxon-shv2/Compiler/Runtime/GtRuntime.maxon`: one run took it from 5554 lines to 5251 and its
/// comment lines from 1920 to 1381 - 338 destroyed, exit 0, `changed: true`, and nothing else said.
/// Among the casualties was load-bearing prose of exactly the kind this codebase is built on. The root
/// cause was in the LEXER (`Advance` did not count a '\n' consumed inside a byte-string literal, so
/// token lines drifted below true source lines and the formatter's line-keyed comment map could no
/// longer find them), but the reason it went unnoticed for so long is here: the formatter had NO TEST
/// AT ALL. A tool that rewrites source in place and is recommended by `.claude/CLAUDE.md` gets a gate.</para>
///
/// <para>⭐ WHAT IT PINS: that formatting PRESERVES EVERY COMMENT, and that it is IDEMPOTENT. It
/// deliberately does not pin layout - blank-line grouping and indentation are the formatter's job to
/// change, and a test that froze them would fail on every legitimate improvement and teach the next
/// reader to re-bless it. Content loss is the failure mode that cannot be undone by re-running the
/// tool, so content is what is guarded.</para>
///
/// <para>⚠ THE CASES ARE CHOSEN BY MECHANISM, NOT BY COVERAGE. Every one puts a comment AFTER a
/// construct that consumes a newline as ordinary text, because that is the shape that desynchronizes
/// line numbering; `NoMultilineLiteral` is the negative control that would have passed throughout the
/// defect's whole lifetime, and is here so a future reader can see that passing it means nothing on
/// its own.</para>
/// </summary>
public static class FormatterSelfTest {
  private record Case(string Name, string Source);

  // A multi-line `b"…"` is a real construct, not a curiosity: this compiler's own source uses one
  // (`maxon-shv2/Compiler/Runtime/GtRuntime.maxon:551`, a byte-string holding a single newline).
  private static readonly Case[] Cases = [
    new("NoMultilineLiteral",
      "// leading\nlet a = b\"z\"\n// between\nlet b = 1\n// trailing\nlet c = 2\n"),

    new("ByteStringOneNewline",
      "let x = b\"\n\"\n// after one\nlet a = 1\n// after two\nlet b = 2\n// after three\nlet c = 3\n"),

    new("ByteStringTwoNewlines",
      "let x = b\"\n\n\"\n// after one\nlet a = 1\n// after two\nlet b = 2\n// after three\nlet c = 3\n"),

    // The trailing-comment half of the same defect: a comment sharing a line with code was emitted
    // against the NEXT line's token, so it migrated onto a neighbouring declaration or fell off.
    new("TrailingCommentsAfterByteString",
      "let x = b\"\n\"\nlet a = 1  // owns a\nlet b = 2  // owns b\nlet c = 3  // owns c\n"),

    // Block comments already counted their own newlines; this pins that they still do now that the
    // counting moved into `Advance`, since that is the one place a double-count would have appeared.
    new("BlockCommentSpansLines",
      "/* one\n   two\n   three */\n// after block\nlet a = 1\n// after decl\nlet b = 2\n"),

    new("ByteStringInsideFunction",
      "function main() returns ExitCode\n\tlet s = b\"\n\"\n\t// inside after literal\n\treturn 0\nend 'main'\n"),
  ];

  public static int Run() {
    var failures = 0;

    foreach (var testCase in Cases) {
      string formatted;
      try {
        formatted = Lsp.MaxonFormatter.Format(testCase.Source);
      } catch (Exception ex) {
        // ASCII only, here and below: this reports through MSBuild's `Exec`, whose console encoding
        // mangles an em dash - including in the failure text, which is the one message that has to survive.
        Console.Error.WriteLine($"fmt-selftest FAIL [{testCase.Name}]: formatter threw {ex.GetType().Name}: {ex.Message}");
        failures++;
        continue;
      }

      foreach (var comment in CommentsIn(testCase.Source)) {
        var before = Count(testCase.Source, comment);
        var after = Count(formatted, comment);
        if (after == before) continue;
        Console.Error.WriteLine(
          $"fmt-selftest FAIL [{testCase.Name}]: comment \"{comment}\" appeared {before} time(s) before "
          + $"formatting and {after} after. Formatting must never lose or duplicate a comment.");
        failures++;
      }

      // Idempotency is a separate claim from preservation and catches a different bug: a formatter can
      // keep every comment while still moving one a little further on each run.
      var twice = Lsp.MaxonFormatter.Format(formatted);
      if (twice != formatted) {
        Console.Error.WriteLine(
          $"fmt-selftest FAIL [{testCase.Name}]: formatting is not idempotent - a second pass changed the output again.");
        failures++;
      }
    }

    if (failures == 0) {
      Console.WriteLine(
        $"fmt-selftest: OK - {Cases.Length} source shapes, every comment preserved and formatting idempotent");
    }
    return failures == 0 ? 0 : 1;
  }

  /// The distinct `//` comment texts in <paramref name="source"/>. A comment that appears more than
  /// once is counted rather than listed twice, so the caller compares MULTIPLICITY and a lost duplicate
  /// is still a failure.
  private static List<string> CommentsIn(string source) {
    var found = new List<string>();
    foreach (var rawLine in source.Split('\n')) {
      var at = rawLine.IndexOf("//", StringComparison.Ordinal);
      if (at < 0) continue;
      var text = rawLine[at..].TrimEnd();
      if (text.Length > 0 && !found.Contains(text)) found.Add(text);
    }
    return found;
  }

  private static int Count(string haystack, string needle) {
    var total = 0;
    var from = 0;
    while (true) {
      var at = haystack.IndexOf(needle, from, StringComparison.Ordinal);
      if (at < 0) return total;
      total++;
      from = at + needle.Length;
    }
  }
}
