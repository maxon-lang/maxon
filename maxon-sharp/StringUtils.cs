using System.Text;

namespace MaxonSharp;

class InvalidEscapeException(char c) : Exception($"Invalid escape sequence '\\{c}'") {
  public char Char { get; } = c;
}

static class StringUtils {
  public static string ResolveEscapes(string text) {
    if (!text.Contains('\\')) return text;
    var sb = new StringBuilder(text.Length);
    for (int i = 0; i < text.Length; i++) {
      if (text[i] == '\\' && i + 1 < text.Length) {
        var next = text[i + 1];
        sb.Append(next switch {
          'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0',
          '\\' => '\\', '\'' => '\'', '"' => '"',
          _ => throw new InvalidEscapeException(next),
        });
        i++;
      } else {
        sb.Append(text[i]);
      }
    }
    return sb.ToString();
  }
}
