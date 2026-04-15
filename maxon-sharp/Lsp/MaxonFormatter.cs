using System.Text;
using MaxonSharp.Compiler;

namespace MaxonSharp.Lsp;

public static class MaxonFormatter {
  private enum BlockKind { Other, Enum, Union, Interface, Match }

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
    // .test files have '---' section separators. The Maxon lexer treats a '---'
    // line as end-of-source and silently discards everything after it (see
    // Lexer.NextToken case '-'). If we lex the full file, the trailing sections
    // (ExitCode, CompiledIR, etc.) would be lost on format. Split here: format
    // only the part before the first '---' section separator and re-append the
    // rest unchanged.
    int separatorStart = FindSectionSeparator(source);
    if (separatorStart >= 0) {
      var maxonPart = source[..separatorStart];
      var tail = source[separatorStart..];
      var formattedHead = FormatCore(maxonPart, indentSize, useTabs);
      // FormatCore always ends with exactly one '\n'. Preserve that single newline
      // as the boundary before the separator section.
      return formattedHead + tail;
    }
    return FormatCore(source, indentSize, useTabs);
  }

  // Find the byte offset of the first '---' section separator line in `source`,
  // or -1 if none. A section separator is a line whose first non-space/tab
  // character is '-' and matches "---" followed by end-of-line (or trailing
  // whitespace then end-of-line). Matches the same rule the Maxon lexer uses
  // to end tokenization.
  private static int FindSectionSeparator(string source) {
    int i = 0;
    while (i < source.Length) {
      int lineStart = i;
      // Skip leading spaces/tabs on this line.
      int j = i;
      while (j < source.Length && (source[j] == ' ' || source[j] == '\t')) j++;
      // Check for '---' at the first non-whitespace position.
      if (j + 2 < source.Length && source[j] == '-' && source[j + 1] == '-' && source[j + 2] == '-') {
        // Everything after '---' on this line must be whitespace (or EOF).
        int k = j + 3;
        while (k < source.Length && source[k] != '\n' && source[k] != '\r') {
          if (source[k] != ' ' && source[k] != '\t') { k = -1; break; }
          k++;
        }
        if (k >= 0) return lineStart;
      }
      // Advance to the next line.
      while (i < source.Length && source[i] != '\n') i++;
      if (i < source.Length) i++; // skip the '\n'
    }
    return -1;
  }

  private static string FormatCore(string source, int indentSize, bool useTabs) {
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
      blockStack.Peek() is BlockKind.Enum or BlockKind.Union;
    bool InInterfaceBlock() => blockStack.Count > 0 && blockStack.Peek() == BlockKind.Interface;
    bool InMatchBlock() => blockStack.Count > 0 && blockStack.Peek() == BlockKind.Match;

    // Opinionated blank-line state. At indentLevel == 0 only, forces exactly one blank
    // line between adjacent top-level logical groups, where a group is a run of adjacent
    // lines sharing a group-key. Inside nested contexts, falls back to preserve-with-cap.
    string? prevGroupKey = null;           // group-key of the last fully-emitted top-level line
    string? pendingGroupKey = null;        // group-key being accumulated for the line currently being emitted
    bool currentLineOpenedBlock = false;   // set when any block-opener fires on the current line
    int uniqueGroupCounter = 0;            // monotonic counter for unique (block-opening) group-keys
    bool blankDecisionPending = false;     // true after a Newline, until next real line-start token decides the blank

    // Derive the tentative group-key for a line whose first meaningful token is `first`.
    // `lookahead` is the token immediately after `first` (or null), used to classify `export ...`.
    // Returns a key that may later be overridden to "unique:N" at line-end if the line opens a block.
    string DeriveGroupKey(TokenType first, TokenType? lookahead) {
      if (first == TokenType.DocComment) return "doc";
      // Whole-line '//' comments are handled separately (not tokenized); this helper only sees real tokens.
      TokenType kind = first;
      if (first == TokenType.Export && lookahead.HasValue) kind = lookahead.Value;
      return kind switch {
        TokenType.Let or TokenType.Var => "let",
        TokenType.TypeAlias => "typealias",
        _ => "unique:" + (uniqueGroupCounter++),
      };
    }

    // Decide how many blank lines to emit before a new top-level line with the given tentative key.
    // Returns 0 or 1. Gated to indentLevel == 0 and blockStack empty by the caller.
    // `userHadBlank` is true if the source had a blank line between the previous line and this one.
    int ForcedBlanksBefore(string thisKey, bool userHadBlank) {
      if (prevGroupKey == null) return 0;
      // Doc-attach: /// fuses with the next declaration below it, even across user blanks.
      if (prevGroupKey == "doc") return 0;
      // Comment-attach: a // comment tightly attached (no user blank) to the next line
      // is treated as a leading doc comment for that line. The user's convention is that
      // Maxon has no /// in practice — // serves as both section comment and attached doc.
      if (prevGroupKey == "comment" && !userHadBlank) return 0;
      // Within a packed group, preserve user's intentional blank-line separation.
      if (prevGroupKey == thisKey) return userHadBlank ? 1 : 0;
      return 1;
    }

    // Promote pendingGroupKey → prevGroupKey at line-end. If the current line opened a block,
    // overwrite its key with a fresh unique one so the next line always gets a forced blank.
    void CloseCurrentLine() {
      if (pendingGroupKey != null) {
        if (currentLineOpenedBlock) pendingGroupKey = "unique:" + (uniqueGroupCounter++);
        prevGroupKey = pendingGroupKey;
        pendingGroupKey = null;
      }
      currentLineOpenedBlock = false;
    }

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
          // The previous real line just ended — close it out.
          if (consecutiveNewlines == 1) {
            CloseCurrentLine();
            blankDecisionPending = true;
            // Emit the line-terminator for the previous content line.
            sb.Append('\n');
          }
          // Decide forced blank(s) before the comment (top-level only).
          bool topLevel = indentLevel == 0 && blockStack.Count == 0 && !pendingIndentIncrease;
          bool userHadBlank = lastEmittedSourceLine >= 0 && tok.Line - lastEmittedSourceLine >= 2;
          if (blankDecisionPending) {
            if (topLevel) {
              int blanks = ForcedBlanksBefore("comment", userHadBlank);
              for (int b = 0; b < blanks; b++) sb.Append('\n');
            } else if (userHadBlank) {
              // Preserve one user blank in nested contexts.
              sb.Append('\n');
            }
            blankDecisionPending = false;
          }
          for (int k = 0; k < indentLevel; k++) sb.Append(indentStr);
          sb.Append(inlineComment.Text);
          sb.Append('\n');
          if (topLevel) prevGroupKey = "comment";
          pendingGroupKey = null;
          currentLineOpenedBlock = false;
          blankDecisionPending = true;
          // Reset consecutiveNewlines to 1: the comment emission ended with '\n',
          // which is the line-terminator for the comment line itself.
          consecutiveNewlines = 1;
          atLineStart = true;
          lastEmittedSourceLine = tok.Line;
          continue;
        }

        // Close out the current line's group-key when its content ends.
        // Emit a single line-terminating '\n' on the first newline; any further
        // consecutive newlines are "user blank lines" whose effect is decided
        // later by the forced-blank logic (which may add one '\n' to represent
        // a blank line, but never more).
        if (consecutiveNewlines == 1) {
          CloseCurrentLine();
          blankDecisionPending = true;
          sb.Append('\n');
        }
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

      // lineIsBlockCloser: set when this line is the tail of a block/literal that was
      // opened on a previous line. Suppresses the forced-blank rule at top level — a
      // closing line is part of the same declaration as its opener, not a new group.
      bool lineIsBlockCloser = false;

      // ']' or '}' at line start closes a multi-line literal — decrement indent.
      if ((tok.Type == TokenType.RightBracket || tok.Type == TokenType.RightBrace) && atLineStart && indentLevel > 0) {
        indentLevel--;
        lineIsBlockCloser = true;
      }

      // '#else' and '#endif' close the current #if/#else block before emitting.
      if ((tok.Type == TokenType.HashElse || tok.Type == TokenType.HashEndif) && indentLevel > 0) {
        indentLevel--;
        // #else is both a closer of the previous branch and an opener of the next —
        // don't treat as a standalone group.
        lineIsBlockCloser = true;
      }

      // 'end label' closes a block — decrement indent and pop stack.
      // Only applies when 'end' is at line start and followed by a CharacterLiteral (not a bare enum case).
      // Mid-line 'end' (e.g. TokenKind.end) is a value reference, not a block closer.
      if (tok.Type == TokenType.End && atLineStart) {
        int next = i + 1;
        while (next < tokens.Count && tokens[next].Type == TokenType.Newline) next++;
        bool nextIsLabel = next == i + 1 && next < tokens.Count && tokens[next].Type == TokenType.CharacterLiteral;
        if (nextIsLabel) {
          if (indentLevel > 0) indentLevel--;
          if (blockStack.Count > 0) blockStack.Pop();
          lineIsBlockCloser = true;
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
        bool topLevel = indentLevel == 0 && blockStack.Count == 0 && !pendingIndentIncrease;
        // Derive this line's tentative group-key (needed for both the forced-blank decision
        // and for tracking prevGroupKey once the line closes).
        bool userHadBlankBeforeLine = lastEmittedSourceLine >= 0 && tok.Line - lastEmittedSourceLine >= 2;
        if (topLevel) {
          TokenType? lookahead = null;
          for (int la = i + 1; la < tokens.Count; la++) {
            if (tokens[la].Type == TokenType.Newline) continue;
            lookahead = tokens[la].Type;
            break;
          }
          string thisKey = DeriveGroupKey(tok.Type, lookahead);
          // Decide forced blank line(s) before the first meaningful token of this line.
          // Exception: 'end label' closing a block is the tail of the block it closes, not
          // a new group — never force a blank before it.
          if (blankDecisionPending && !lineIsBlockCloser) {
            int blanks = ForcedBlanksBefore(thisKey, userHadBlankBeforeLine);
            for (int b = 0; b < blanks; b++) sb.Append('\n');
          }
          pendingGroupKey = thisKey;
        } else if (blankDecisionPending && userHadBlankBeforeLine) {
          // Nested context — preserve at most 1 user blank.
          sb.Append('\n');
        }
        blankDecisionPending = false;
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

      // Match-block rewrite: caseName(_, _, ...) → caseName
      // When all bindings are discarded, strip the parenthesized underscores.
      if (tok.Type == TokenType.Identifier && InMatchBlock()
          && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.LeftParen) {
        int scan = i + 2;
        int bindingCount = 0;
        while (scan < tokens.Count) {
          if (tokens[scan].Type == TokenType.Identifier && tokens[scan].Value == "_") {
            bindingCount++;
            scan++;
            if (scan < tokens.Count && tokens[scan].Type == TokenType.Comma) scan++;
          } else {
            break;
          }
        }
        if (bindingCount > 0
            && scan < tokens.Count && tokens[scan].Type == TokenType.RightParen) {
          i = scan; // skip past ')'
          prevNonNewline = TokenType.Identifier;
          continue;
        }
      }

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
        if (closingOnDifferentLine) { pendingIndentIncrease = true; currentLineOpenedBlock = true; }
      }

      // '#if' and '#else' open a new block — indent the next line.
      if (tok.Type == TokenType.HashIf || tok.Type == TokenType.HashElse) {
        pendingIndentIncrease = true;
        currentLineOpenedBlock = true;
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

      // Inside a enum body, only 'end label' matters for structure.
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
            TokenType.Enum => BlockKind.Enum,
            TokenType.Union => BlockKind.Union,
            TokenType.Interface => BlockKind.Interface,
            _ => BlockKind.Other,
          };
          blockStack.Push(kind);
          pendingIndentIncrease = true;
          currentLineOpenedBlock = true;
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
          currentLineOpenedBlock = true;
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

  // Tokens after which '(' is an argument/pattern/type-list, NOT a parenthesized expression.
  // These are the only contexts where we suppress the space before '('.
  // Every other keyword (for, if, or, and, return, not, in, upto, ...) is treated as
  // a non-callable and gets a mandatory space before '('.
  private static readonly HashSet<TokenType> CallableBeforeLeftParen = [
    TokenType.Identifier, TokenType.RightParen, TokenType.RightBracket,
    // Type keywords that introduce a parameterized type or range cast, e.g. int(0 to 10).
    TokenType.Int, TokenType.Float, TokenType.Bool, TokenType.Byte,
    // Keyword callables: panic("msg") and otherwise(e) 'label' (error binding capture).
    TokenType.Panic, TokenType.Otherwise,
    // Type-param-list keyword: Map with(Key, Value), Array with(T), etc.
    TokenType.With,
    // Match-arm pattern destructuring keywords: enum(name), union(name), function(name).
    // These keywords can appear as destructure patterns inside match arms, where the tight
    // `keyword(binding)` form is user idiom. Top-level `export enum Foo` has an identifier,
    // not '(', after the keyword, so adding these here doesn't affect declaration headers.
    TokenType.Enum, TokenType.Union, TokenType.Function,
    // Match-arm result expression: `pattern gives(val1, val2)`.
    TokenType.Gives,
    // 'default' is used as a function name: `static function default()` / `ValueInfo.default()`.
    // (The match-arm `default` appears alone, never followed by '(' in current code.)
    TokenType.Default,
  ];

  private static bool NeedsSpaceBefore(TokenType cur, TokenType prev, bool prevWasDot = false, bool inDataBlock = false, bool prevWasUnary = false) {
    if (NoSpaceBefore.Contains(cur)) return false;
    if (NoSpaceAfter.Contains(prev)) return false;
    if (cur == TokenType.Colon) return false;
    // No space after unary minus/plus
    if (prevWasUnary) return false;
    // Dot-accessed member call: foo.bar() — no space.
    if (cur == TokenType.LeftParen && prevWasDot) return false;
    // Inside a data block (enum/union body), case values look like 'name(...)' — no space.
    if (cur == TokenType.LeftParen && inDataBlock) return false;
    // Suppress space before '(' only when the previous token is a genuine callable.
    // Non-callable keywords (for, if, while, match, or, and, not, return, in, ...) get a space.
    if (cur == TokenType.LeftParen && !CallableBeforeLeftParen.Contains(prev)) return true;
    if (cur == TokenType.LeftParen) return false;
    if (cur == TokenType.LeftBracket && (prev == TokenType.Identifier || prev == TokenType.RightParen || prev == TokenType.RightBracket)) return false;
    // No space before '{' after any word token (struct literal: 'Foo {...}', 'Self {...}')
    if (cur == TokenType.LeftBrace && !NonWordTokens.Contains(prev)) return false;
    return true;
  }
}
