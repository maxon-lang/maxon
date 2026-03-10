using System.Text;

namespace MaxonSharp;

class InvalidEscapeException(string message) : Exception(message) {
  public InvalidEscapeException(char c) : this($"Invalid escape sequence '\\{c}'") { }
}

static class StringUtils {
  public static string ResolveEscapes(string text) {
    if (!text.Contains('\\')) return text;
    var sb = new StringBuilder(text.Length);
    for (int i = 0; i < text.Length; i++) {
      if (text[i] == '\\' && i + 1 < text.Length) {
        var next = text[i + 1];
        if (next == 'x') {
          if (i + 3 >= text.Length || !IsHexDigit(text[i + 2]) || !IsHexDigit(text[i + 3])) {
            var available = text.Substring(i, Math.Min(4, text.Length - i));
            throw new InvalidEscapeException($"Invalid hex escape '\\{available[1..]}': expected 2 hex digits");
          }
          var hex = text.Substring(i + 2, 2);
          sb.Append((char)Convert.ToByte(hex, 16));
          i += 3;
        } else if (next == 'u') {
          // \uXXXX — Unicode code point escape (exactly 4 hex digits)
          if (i + 5 >= text.Length || !IsHexDigit(text[i + 2]) || !IsHexDigit(text[i + 3])
              || !IsHexDigit(text[i + 4]) || !IsHexDigit(text[i + 5])) {
            var available = text.Substring(i, Math.Min(6, text.Length - i));
            throw new InvalidEscapeException($"Invalid unicode escape '\\{available[1..]}': expected 4 hex digits");
          }
          var hex = text.Substring(i + 2, 4);
          var codePoint = Convert.ToInt32(hex, 16);
          sb.Append(char.ConvertFromUtf32(codePoint));
          i += 5;
        } else {
          sb.Append(next switch {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            '0' => '\0',
            '\\' => '\\',
            '\'' => '\'',
            '"' => '"',
            _ => throw new InvalidEscapeException(next),
          });
          i++;
        }
      } else {
        sb.Append(text[i]);
      }
    }
    return sb.ToString();
  }

  private static bool IsHexDigit(char c) =>
    c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

}
