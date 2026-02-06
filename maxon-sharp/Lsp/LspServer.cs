using System.Collections.Concurrent;
using MaxonSharp.Compiler;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;

namespace MaxonSharp.Lsp;

public class LspServer {
  private readonly ConcurrentDictionary<DocumentUri, string> _documents = new();
  private ProjectManager? _projectManager;

  public ProjectManager ProjectManager =>
    _projectManager ?? throw new InvalidOperationException("LSP not initialized");

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
          _projectManager = new ProjectManager(
            (uri, diags) => server.TextDocument.PublishDiagnostics(
              new PublishDiagnosticsParams { Uri = uri, Diagnostics = diags }
            )
          );
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
    var filePath = uri.GetFileSystemPath() ?? uri.ToString();
    var project = ProjectManager.GetOrCreateProject(filePath);
    project.NotifyFileChanged(filePath, content);
  }

  public void RemoveDocument(DocumentUri uri) {
    _documents.TryRemove(uri, out _);
    var filePath = uri.GetFileSystemPath() ?? uri.ToString();
    ProjectManager.RemoveFileFromProject(filePath);
  }

  public string? GetDocument(DocumentUri uri) {
    return _documents.TryGetValue(uri, out var content) ? content : null;
  }

  public CompletionList GetCompletions(DocumentUri uri, Position position) {
    var content = GetDocument(uri);
    if (content != null) {
      var dotTarget = GetDotCompletionTarget(content, position);
      if (dotTarget != null) {
        return GetMemberCompletions(uri, dotTarget);
      }
    }

    return GetDefaultCompletions();
  }

  private static string? GetDotCompletionTarget(string content, Position position) {
    var lines = content.Split('\n');
    if (position.Line >= lines.Length) return null;

    var line = lines[position.Line];
    var col = (int)position.Character;

    // The cursor is right after the dot, so find the dot
    // col points to after the dot. We need to find the identifier before it.
    if (col <= 0 || col > line.Length) return null;

    // Walk back to find the dot
    var dotPos = col - 1;
    if (dotPos < 0 || dotPos >= line.Length || line[dotPos] != '.') return null;

    // Walk back from dot to find the identifier
    var end = dotPos;
    var start = end - 1;
    while (start >= 0 && IsWordChar(line[start])) {
      start--;
    }
    start++;

    if (start >= end) return null;
    return line[start..end];
  }

  private CompletionList GetMemberCompletions(DocumentUri uri, string targetName) {
    var filePath = uri.GetFileSystemPath() ?? uri.ToString();
    var project = ProjectManager.FindProjectForFile(filePath);
    var info = project?.GetCompletionInfo();

    if (info == null)
      return GetDefaultCompletions();

    // Extract parameter types from parsed functions (no re-lexing needed)
    var variableTypes = ExtractParamTypes(info.Functions);
    info = new CompletionInfo(info.TypeDefs, info.Functions, variableTypes);

    return BuildMemberCompletionList(info, targetName);
  }

  private static Dictionary<string, string> ExtractParamTypes(
    List<Compiler.Mlir.Core.MlirFunction<Compiler.Mlir.Dialects.MaxonOp>> functions
  ) {
    var result = new Dictionary<string, string>();
    foreach (var func in functions) {
      for (int i = 0; i < func.ParamNames.Count && i < func.ParamTypes.Count; i++) {
        var paramName = func.ParamNames[i];
        var paramType = func.ParamTypes[i];
        if (paramName != "self" && paramType is Compiler.Mlir.Core.MlirStructType structType) {
          result.TryAdd(paramName, structType.Name);
        }
      }
    }
    return result;
  }

  private static CompletionList BuildMemberCompletionList(CompletionInfo info, string targetName) {
    // Resolve the type name: check if target is a variable or a type name directly
    string? typeName = null;
    if (info.VariableTypes.TryGetValue(targetName, out var varType)) {
      typeName = varType;
    } else if (info.TypeDefs.ContainsKey(targetName)) {
      // Direct type access like String.init()
      typeName = targetName;
    }

    if (typeName == null) return GetDefaultCompletions();

    var items = new List<CompletionItem>();
    var addedNames = new HashSet<string>();

    // Add fields from the struct type
    if (info.TypeDefs.TryGetValue(typeName, out var mlirType)
        && mlirType is MaxonSharp.Compiler.Mlir.Core.MlirStructType structType) {
      foreach (var field in structType.Fields) {
        if (!field.IsExported) continue;
        if (addedNames.Add(field.Name)) {
          items.Add(new CompletionItem {
            Label = field.Name,
            Kind = CompletionItemKind.Field,
            Detail = $"{typeName}.{field.Name}: {field.Type.Name}"
          });
        }
      }
    }

    // Add methods: functions named "*.TypeName.methodName"
    // e.g., "stdlib.String.slice" -> method "slice" on type "String"
    var dotType = $".{typeName}.";
    var prefixType = $"{typeName}.";
    foreach (var func in info.Functions) {
      var funcName = func.Name;

      // Extract method name after "TypeName."
      string? methodPart = null;
      var idx = funcName.IndexOf(dotType);
      if (idx >= 0) {
        methodPart = funcName[(idx + dotType.Length)..];
      } else if (funcName.StartsWith(prefixType)) {
        methodPart = funcName[prefixType.Length..];
      }
      if (methodPart == null) continue;

      // Strip overload mangling (e.g., "slice$endIndex" -> "slice")
      var dollarIdx = methodPart.IndexOf('$');
      if (dollarIdx >= 0) methodPart = methodPart[..dollarIdx];

      // Skip nested names (e.g., "String.ByteView.next" when looking at String)
      if (methodPart.Contains('.')) continue;

      if (string.IsNullOrEmpty(methodPart) || !addedNames.Add(methodPart)) continue;

      // Build parameter signature
      var paramParts = new List<string>();
      for (int i = 0; i < func.ParamNames.Count; i++) {
        if (func.ParamNames[i] == "self") continue;
        var paramType = i < func.ParamTypes.Count ? func.ParamTypes[i].Name : "?";
        paramParts.Add($"{func.ParamNames[i]} {paramType}");
      }
      var paramsStr = string.Join(", ", paramParts);
      var returnStr = func.ReturnType != null && func.ReturnType.Name != "void"
        ? $" returns {func.ReturnType.Name}" : "";

      items.Add(new CompletionItem {
        Label = methodPart,
        Kind = CompletionItemKind.Method,
        Detail = $"{methodPart}({paramsStr}){returnStr}",
        InsertText = methodPart
      });
    }

    if (items.Count == 0) return GetDefaultCompletions();

    return new CompletionList(items, isIncomplete: false);
  }

  private static CompletionList GetDefaultCompletions() {
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
        Detail = "Stdlib only. " + info.HelpText,
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
            Value = $"**{word}** (builtin, stdlib only)\n\n{builtinInfo.HelpText}"
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

public record CompletionInfo(
  Dictionary<string, MlirType> TypeDefs,
  List<MlirFunction<MaxonOp>> Functions,
  Dictionary<string, string> VariableTypes
);
