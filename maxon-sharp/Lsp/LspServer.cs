using System.Collections.Concurrent;
using MaxonSharp.Compiler;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Server;

namespace MaxonSharp.Lsp;

public class LspServer {
  private readonly ConcurrentDictionary<DocumentUri, string> _documents = new();

  public async Task RunAsync() {
    var server = await LanguageServer.From(options =>
      options
        .WithInput(Console.OpenStandardInput())
        .WithOutput(Console.OpenStandardOutput())
        .ConfigureLogging(x => x.AddLanguageProtocolLogging())
        .WithConfigurationSection("maxon")
        .WithHandler<TextDocumentSyncHandler>()
        .WithHandler<CompletionHandler>()
        .WithHandler<HoverHandler>()
        .WithServices(services => {
          services.AddSingleton(this);
        })
        .OnInitialize((server, request, token) => {
          return Task.CompletedTask;
        })
        .OnInitialized((server, request, response, token) => {
          return Task.CompletedTask;
        })
    ).ConfigureAwait(false);

    await server.WaitForExit.ConfigureAwait(false);
  }

  public void UpdateDocument(DocumentUri uri, string content) {
    _documents[uri] = content;
  }

  public void RemoveDocument(DocumentUri uri) {
    _documents.TryRemove(uri, out _);
  }

  public string? GetDocument(DocumentUri uri) {
    return _documents.TryGetValue(uri, out var content) ? content : null;
  }

  public Container<Diagnostic> GetDiagnostics(DocumentUri uri) {
    if (!_documents.TryGetValue(uri, out var content)) {
      return new Container<Diagnostic>();
    }

    var diagnostics = new List<Diagnostic>();

    try {
      var lexer = new Lexer(content);
      var tokens = lexer.Tokenize();

      // Try parsing to catch more errors
      var parser = new Parser(tokens);
      parser.Parse();
    } catch (CompileError error) {
      var line = (error.Line ?? 1) - 1;
      var column = (error.Column ?? 1) - 1;

      diagnostics.Add(new Diagnostic {
        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
          new Position(line, column),
          new Position(line, column + 1)
        ),
        Severity = DiagnosticSeverity.Error,
        Source = "maxon",
        Message = error.Message,
        Code = error.Code.Format()
      });
    } catch (Exception ex) {
      // Catch any other parsing errors
      diagnostics.Add(new Diagnostic {
        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
          new Position(0, 0),
          new Position(0, 1)
        ),
        Severity = DiagnosticSeverity.Error,
        Source = "maxon",
        Message = ex.Message
      });
    }

    return new Container<Diagnostic>(diagnostics);
  }

  public static CompletionList GetCompletions(DocumentUri uri, Position position) {
    var items = new List<CompletionItem>();

    // Add keywords
    foreach (var (keyword, info) in Lexer.KeywordMap) {
      items.Add(new CompletionItem {
        Label = keyword,
        Kind = CompletionItemKind.Keyword,
        Detail = info.HelpText,
        InsertText = keyword
      });
    }

    // Add built-in types
    items.Add(new CompletionItem { Label = "int", Kind = CompletionItemKind.TypeParameter, Detail = "64-bit signed integer" });
    items.Add(new CompletionItem { Label = "float", Kind = CompletionItemKind.TypeParameter, Detail = "64-bit floating point" });
    items.Add(new CompletionItem { Label = "bool", Kind = CompletionItemKind.TypeParameter, Detail = "Boolean type" });
    items.Add(new CompletionItem { Label = "byte", Kind = CompletionItemKind.TypeParameter, Detail = "8-bit unsigned integer" });

    // Add compiler builtins
    foreach (var (name, info) in Parser.CompilerBuiltins) {
      items.Add(new CompletionItem {
        Label = name,
        Kind = CompletionItemKind.Function,
        Detail = info.HelpText,
        InsertText = name
      });
    }

    return new CompletionList(items, isIncomplete: false);
  }

  public Hover? GetHover(DocumentUri uri, Position position) {
    if (!_documents.TryGetValue(uri, out var content)) {
      return null;
    }

    // Get the word at the current position
    var lines = content.Split('\n');
    if (position.Line >= lines.Length) {
      return null;
    }

    var line = lines[position.Line];
    var word = GetWordAtPosition(line, position.Character);

    if (string.IsNullOrEmpty(word)) {
      return null;
    }

    // Check if it's a keyword
    if (Lexer.KeywordMap.TryGetValue(word, out var keywordInfo)) {
      return new Hover {
        Contents = new MarkedStringsOrMarkupContent(
          new MarkupContent {
            Kind = MarkupKind.Markdown,
            Value = $"**{word}** (keyword)\n\n{keywordInfo.HelpText}"
          }
        ),
        Range = GetWordRange(position, line, word)
      };
    }

    // Check if it's a compiler builtin
    if (Parser.CompilerBuiltins.TryGetValue(word, out var builtinInfo)) {
      return new Hover {
        Contents = new MarkedStringsOrMarkupContent(
          new MarkupContent {
            Kind = MarkupKind.Markdown,
            Value = $"**{word}** (builtin)\n\n{builtinInfo.HelpText}"
          }
        ),
        Range = GetWordRange(position, line, word)
      };
    }

    // Check if it's an operator
    if (Lexer.OperatorMap.TryGetValue(word, out var operatorInfo)) {
      return new Hover {
        Contents = new MarkedStringsOrMarkupContent(
          new MarkupContent {
            Kind = MarkupKind.Markdown,
            Value = $"**{word}** (operator)\n\n{operatorInfo.HelpText}"
          }
        ),
        Range = GetWordRange(position, line, word)
      };
    }

    return null;
  }

  private static string GetWordAtPosition(string line, int character) {
    if (character >= line.Length) {
      return "";
    }

    var start = character;
    var end = character;

    // Find start of word
    while (start > 0 && IsWordChar(line[start - 1])) {
      start--;
    }

    // Find end of word
    while (end < line.Length && IsWordChar(line[end])) {
      end++;
    }

    return line[start..end];
  }

  private static bool IsWordChar(char c) {
    return char.IsLetterOrDigit(c) || c == '_';
  }

  private static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range GetWordRange(Position position, string line, string word) {
    var start = position.Character;
    while (start > 0 && IsWordChar(line[start - 1])) {
      start--;
    }
    return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
      new Position(position.Line, start),
      new Position(position.Line, start + word.Length)
    );
  }
}
