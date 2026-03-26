using System.Text;
using MaxonSharp.Compiler;

namespace MaxonSharp.Lsp;

public static class MaxonFormatter {
  private enum BlockKind { Other, Union, Enum, Interface, Match }

  // Labelless block openers: these keywords start a block declaration on the current line.
  private static readonly HashSet<TokenType> LabellessBlockOpeners = [
    TokenType.Function, TokenType.Type, TokenType.Union, TokenType.Enum,
    TokenType.Interface, TokenType.Extension,
  ];

  // Labeled block openers: these keywords open a block whose body is labeled with a trailing CharacterLiteral.
  private static readonly HashSet<TokenType> LabeledBlockOpeners = [
    TokenType.If, TokenType.Else, TokenType.Otherwise,
    TokenType.While, TokenType.For,
    TokenType.Match, TokenType.Then,
    TokenType.Try,
  ];

  // After these tokens, do NOT insert a space before the next token
  private static readonly HashSet<TokenType> NoSpaceAfter = [
    TokenType.LeftParen, TokenType.LeftBracket, TokenType.LeftBrace,
    TokenType.Dot, TokenType.At,
  ];

  // Before these tokens, do NOT insert a space
  private static readonly HashSet<TokenType> NoSpaceBefore = [
    TokenType.RightParen, TokenType.RightBracket, TokenType.RightBrace,
    TokenType.Comma, TokenType.Dot,
  ];

private record SourceComment(string Text, bool WholeLine);

  private static Dictionary<int, SourceComment> ExtractLineComments(string source) {
    var comments = new Dictionary<int, SourceComment>();
    var lines = source.Split('\n');
    for (int i = 0; i < lines.Length; i++) {
      var line = lines[i];
      bool inString = false;
      char stringChar = '"';
      for (int j = 0; j < line.Length - 1; j++) {
        char c = line[j];
        if (inString) {
          if (c == '\\') { j++; continue; }
          if (c == stringChar) inString = false;
        } else {
          if (c == '"' || c == '\'') { inString = true; stringChar = c; }
          else if (c == '/' && line[j + 1] == '/') {
            if (j + 2 < line.Length && line[j + 2] == '/') break; // skip ///
            var commentText = line[j..].TrimEnd();
            var wholeLine = line[..j].Trim().Length == 0;
            comments[i + 1] = new SourceComment(commentText, wholeLine);
            break;
          }
        }
      }
    }
    return comments;
  }

  public static string Format(string source, int indentSize = 2, bool useTabs = true) {
    List<Token> tokens;
    try {
      tokens = new Lexer(source).Tokenize();
    } catch {
      return source;
    }

    var lineComments = ExtractLineComments(source);

    var sb = new StringBuilder();
    var indentStr = useTabs ? "\t" : new string(' ', indentSize);
    int indentLevel = 0;
    bool atLineStart = true;
    int consecutiveNewlines = 0;
    bool pendingIndentIncrease = false;
    TokenType prevNonNewline = TokenType.Eof;
    bool prevWasDot = false;
    bool prevWasUnary = false; // true when prev '-' or '+' was unary (no space after)
    int braceDepth = 0;
    int lastEmittedSourceLine = -1;
    var consumedCommentLines = new HashSet<int>();
    bool lineStartedWithEnd = false;
    bool lineHasLabeledOpener = false; // true if a LabeledBlockOpener appeared on the current line
    bool lineHasMatch = false; // true if a Match keyword appeared on the current line

    // Stack tracking what kind of block we're inside.
    // Pushed when we open a block, popped when we see 'end label'.
    var blockStack = new Stack<BlockKind>();

    bool InDataBlock() => blockStack.Count > 0 &&
      (blockStack.Peek() == BlockKind.Union || blockStack.Peek() == BlockKind.Enum);
    bool InInterfaceBlock() => blockStack.Count > 0 && blockStack.Peek() == BlockKind.Interface;
    bool InMatchBlock() => blockStack.Count > 0 && blockStack.Peek() == BlockKind.Match;

    for (int i = 0; i < tokens.Count; i++) {
      var tok = tokens[i];

      if (tok.Type == TokenType.Eof) {
        if (lastEmittedSourceLine < 0) {
          foreach (var kv in lineComments.OrderBy(kv => kv.Key)) {
            if (kv.Value.WholeLine) { sb.Append(kv.Value.Text); sb.Append('\n'); }
          }
          break;
        }
        if (lineComments.TryGetValue(lastEmittedSourceLine, out var eofComment)
            && !eofComment.WholeLine
            && !consumedCommentLines.Contains(lastEmittedSourceLine)) {
          sb.Append("  ");
          sb.Append(eofComment.Text);
        }
        break;
      }

      if (tok.Type == TokenType.Newline) {
        if (lastEmittedSourceLine < 0) { atLineStart = true; continue; }

        consecutiveNewlines++;
        if (pendingIndentIncrease) { indentLevel++; pendingIndentIncrease = false; }

        // Trailing comment
        if (consecutiveNewlines == 1 && lastEmittedSourceLine > 0
            && lineComments.TryGetValue(lastEmittedSourceLine, out var trailingComment)
            && !trailingComment.WholeLine
            && consumedCommentLines.Add(lastEmittedSourceLine)) {
          sb.Append("  ");
          sb.Append(trailingComment.Text);
        }

        // Whole-line comment on this newline's line
        if (lastEmittedSourceLine >= 0
            && lineComments.TryGetValue(tok.Line, out var inlineComment)
            && inlineComment.WholeLine
            && !consumedCommentLines.Contains(tok.Line)) {
          for (int k = 0; k < indentLevel; k++) sb.Append(indentStr);
          sb.Append(inlineComment.Text);
          sb.Append('\n');
          consecutiveNewlines = 0;
          atLineStart = true;
          continue;
        }

        if (consecutiveNewlines <= 2) sb.Append('\n');
        atLineStart = true;
        continue;
      }

      consecutiveNewlines = 0;

      // Head comments (before first real token)
      if (lastEmittedSourceLine < 0 && tok.Line >= 1) {
        bool emittedAny = false;
        for (int srcLine = 1; srcLine < tok.Line; srcLine++) {
          if (lineComments.TryGetValue(srcLine, out var headComment) && headComment.WholeLine) {
            sb.Append(headComment.Text); sb.Append('\n');
            emittedAny = true;
          } else if (emittedAny) {
            sb.Append('\n');
          }
        }
      }

      // ']' or '}' at line start closes a multi-line literal — decrement indent.
      if ((tok.Type == TokenType.RightBracket || tok.Type == TokenType.RightBrace) && atLineStart && indentLevel > 0) {
        indentLevel--;
      }

      // '#else' and '#endif' close the current #if/#else block before emitting.
      if ((tok.Type == TokenType.HashElse || tok.Type == TokenType.HashEndif) && indentLevel > 0) {
        indentLevel--;
      }

      // 'end label' closes a block — decrement indent and pop stack.
      // Only applies when 'end' is followed by a CharacterLiteral (not a bare enum case).
      if (tok.Type == TokenType.End) {
        int next = i + 1;
        while (next < tokens.Count && tokens[next].Type == TokenType.Newline) next++;
        bool nextIsLabel = next == i + 1 && next < tokens.Count && tokens[next].Type == TokenType.CharacterLiteral;
        if (nextIsLabel) {
          if (indentLevel > 0) indentLevel--;
          if (blockStack.Count > 0) blockStack.Pop();
        }
      }

      // Track lineStartedWithEnd for label-indent suppression
      bool wasAtLineStart = atLineStart;
      if (atLineStart) {
        if (tok.Type == TokenType.End) {
          int next = i + 1;
          while (next < tokens.Count && tokens[next].Type == TokenType.Newline) next++;
          lineStartedWithEnd = next == i + 1 && next < tokens.Count && tokens[next].Type == TokenType.CharacterLiteral;
        } else {
          lineStartedWithEnd = false;
        }
        lineHasLabeledOpener = false;
        lineHasMatch = false;
      }

      // Labeled block openers that must be at line start to open a block.
      if (wasAtLineStart && LabeledBlockOpeners.Contains(tok.Type))
        lineHasLabeledOpener = true;
      // These keywords open labeled blocks even when mid-line (but not 'then' — it only
      // opens a block when at line start, to avoid treating match arm labels as block openers).
      if (tok.Type == TokenType.Try || tok.Type == TokenType.Otherwise || tok.Type == TokenType.Match)
        lineHasLabeledOpener = true;
      if (tok.Type == TokenType.Match)
        lineHasMatch = true;

      // Emit indentation
      if (atLineStart) {
        for (int k = 0; k < indentLevel; k++) sb.Append(indentStr);
        atLineStart = false;
      } else {
        if (NeedsSpaceBefore(tok.Type, prevNonNewline, prevWasDot, InDataBlock(), prevWasUnary)) sb.Append(' ');
      }

      // Emit token text
      if (tok.Type == TokenType.DocComment) {
        sb.Append("/// "); sb.Append(tok.Value);
      } else if (tok.Type == TokenType.CharacterLiteral) {
        sb.Append('\''); sb.Append(tok.Value); sb.Append('\'');
      } else if (tok.Type == TokenType.StringLiteral || tok.Type == TokenType.StringInterp) {
        sb.Append('"'); sb.Append(tok.Value); sb.Append('"');
      } else if (tok.Type == TokenType.ByteStringLiteral) {
        sb.Append("b\""); sb.Append(tok.Value); sb.Append('"');
      } else {
        sb.Append(tok.Value);
      }

      lastEmittedSourceLine = tok.Line;

      if (tok.Type == TokenType.LeftBrace) braceDepth++;
      else if (tok.Type == TokenType.RightBrace && braceDepth > 0) braceDepth--;

      // Multi-line bracket/brace literal: '[' or '{' whose matching closer is on a different line opens an indent level.
      if (tok.Type == TokenType.LeftBracket || tok.Type == TokenType.LeftBrace) {
        var open = tok.Type == TokenType.LeftBracket ? TokenType.LeftBracket : TokenType.LeftBrace;
        var close = tok.Type == TokenType.LeftBracket ? TokenType.RightBracket : TokenType.RightBrace;
        int depth = 1, scan = i + 1;
        while (scan < tokens.Count && depth > 0) {
          if (tokens[scan].Type == open) depth++;
          else if (tokens[scan].Type == close) depth--;
          scan++;
        }
        bool closingOnDifferentLine = scan - 1 < tokens.Count && tokens[scan - 1].Line != tok.Line;
        if (closingOnDifferentLine) pendingIndentIncrease = true;
      }

      // '#if' and '#else' open a new block — indent the next line.
      if (tok.Type == TokenType.HashIf || tok.Type == TokenType.HashElse) {
        pendingIndentIncrease = true;
      }

      // end 'X' else/otherwise — stay on same line, reset lineStartedWithEnd for chain label
      if (tok.Type == TokenType.CharacterLiteral && prevNonNewline == TokenType.End) {
        int j = i + 1;
        while (j < tokens.Count && tokens[j].Type == TokenType.Newline) j++;
        if (j < tokens.Count && (tokens[j].Type == TokenType.Else || tokens[j].Type == TokenType.Otherwise)) {
          i = j - 1;
          prevNonNewline = tok.Type;
          lineStartedWithEnd = false;
          lineHasLabeledOpener = true; // else/otherwise is a labeled opener on the new line
          continue;
        }
      }

      prevWasDot = prevNonNewline == TokenType.Dot;
      // Unary minus/plus: '-' or '+' is unary when preceded by an operator, open bracket, comma, or line start.
      // NOT unary after literals, identifiers, or closing brackets (those end an expression).
      static bool IsExpressionEnder(TokenType t) => t switch {
        TokenType.Identifier or TokenType.IntegerLiteral or TokenType.FloatLiteral or
        TokenType.StringLiteral or TokenType.StringInterp or TokenType.ByteStringLiteral or
        TokenType.CharacterLiteral or TokenType.RightParen or TokenType.RightBracket or TokenType.RightBrace or
        TokenType.Self or TokenType.SelfType or TokenType.True or TokenType.False => true,
        _ => false,
      };
      prevWasUnary = (tok.Type == TokenType.Minus || tok.Type == TokenType.Plus) &&
        (wasAtLineStart || (!IsExpressionEnder(prevNonNewline) && prevNonNewline != TokenType.Eof));
      var prevToken = prevNonNewline;
      prevNonNewline = tok.Type;

      // Inside a union/enum body, only 'end label' matters for structure.
      // All other keyword/label-based indent rules are suppressed.
      if (InDataBlock()) continue;

      // Labelless block openers push a block and indent the next line.
      // Only trigger when at line start (or after 'export'/'static') — not as enum case values (handled above).
      // Skip if the next non-newline token is 'end' (bodyless declaration, e.g. interface method signature).
      if (LabellessBlockOpeners.Contains(tok.Type) && (wasAtLineStart || prevToken == TokenType.Export || prevToken == TokenType.Static) && !InMatchBlock()) {
        // Inside an interface block, function/type declarations are bodyless signatures — don't open a block.
        bool bodylessDecl = InInterfaceBlock() && tok.Type == TokenType.Function;
        if (!bodylessDecl) {
          var kind = tok.Type switch {
            TokenType.Union => BlockKind.Union,
            TokenType.Enum => BlockKind.Enum,
            TokenType.Interface => BlockKind.Interface,
            _ => BlockKind.Other,
          };
          blockStack.Push(kind);
          pendingIndentIncrease = true;
        }
      }

      // Labeled block openers: a CharacterLiteral opens a new block only when a labeled-block-opener
      // keyword appeared earlier on the same line (not on break, continue, return, etc.).
      if (tok.Type == TokenType.CharacterLiteral && !lineStartedWithEnd && lineHasLabeledOpener) {
        int next = i + 1;
        while (next < tokens.Count && tokens[next].Type == TokenType.Newline) next++;
        bool nextIsChain = next < tokens.Count &&
          (tokens[next].Type == TokenType.Else || tokens[next].Type == TokenType.Otherwise);
        if (!nextIsChain) {
          blockStack.Push(lineHasMatch ? BlockKind.Match : BlockKind.Other);
          pendingIndentIncrease = true;
        }
      }
    }

    return sb.ToString().TrimEnd('\n', '\r', ' ', '\t') + '\n';
  }

  // Punctuation/operator/literal tokens — everything that is NOT a keyword or identifier.
  private static readonly HashSet<TokenType> NonWordTokens = [
    TokenType.LeftParen, TokenType.RightParen,
    TokenType.LeftBracket, TokenType.RightBracket,
    TokenType.LeftBrace, TokenType.RightBrace,
    TokenType.Comma, TokenType.Dot, TokenType.At, TokenType.Colon,
    TokenType.Plus, TokenType.Minus, TokenType.Star, TokenType.Slash,
    TokenType.Equals, TokenType.EqualsEquals, TokenType.NotEquals,
    TokenType.LessThan, TokenType.LessEquals, TokenType.GreaterThan, TokenType.GreaterEquals,
    TokenType.Newline, TokenType.Eof,
    TokenType.IntegerLiteral, TokenType.FloatLiteral, TokenType.StringLiteral,
    TokenType.StringInterp, TokenType.CharacterLiteral, TokenType.ByteStringLiteral,
    TokenType.DocComment,
  ];

  private static bool NeedsSpaceBefore(TokenType cur, TokenType prev, bool prevWasDot = false, bool inDataBlock = false, bool prevWasUnary = false) {
    if (NoSpaceBefore.Contains(cur)) return false;
    if (NoSpaceAfter.Contains(prev)) return false;
    if (cur == TokenType.Colon) return false;
    // No space after unary minus/plus
    if (prevWasUnary) return false;
    // No space before '(' after any word token (identifier or keyword), dot-accessed member, or inside data block
    if (cur == TokenType.LeftParen && (!NonWordTokens.Contains(prev) || prevWasDot || inDataBlock)) return false;
    if (cur == TokenType.LeftBracket && (prev == TokenType.Identifier || prev == TokenType.RightParen || prev == TokenType.RightBracket)) return false;
    // No space before '{' after any word token (struct literal: 'Foo {...}', 'Self {...}')
    if (cur == TokenType.LeftBrace && !NonWordTokens.Contains(prev)) return false;
    return true;
  }
}
