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
        .WithHandler<DefinitionHandler>()
        .WithHandler<RenameHandler>()
        .WithHandler<PrepareRenameHandler>()
        .WithHandler<DidChangeWatchedFilesHandler>()
        .WithHandler<FormattingHandler>()
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
    // Only process real .maxon/.test files — skip virtual/temporary buffers (e.g. Copilot response files)
    if (!IsMaxonFile(uri)) return;

    _documents[uri] = content;
    var filePath = uri.GetFileSystemPath()!;
    // build.maxon is a project metadata file, not a source file — don't compile it
    if (Path.GetFileName(filePath).Equals("build.maxon", StringComparison.OrdinalIgnoreCase))
      return;
    // .test files are always single-file projects, never part of a multi-file build
    var forceSingleFile = filePath.EndsWith(".test", StringComparison.OrdinalIgnoreCase);
    var project = ProjectManager.GetOrCreateProject(filePath, forceSingleFile);
    project.NotifyFileChanged(filePath, content);
  }

  public void RemoveDocument(DocumentUri uri) {
    if (!IsMaxonFile(uri)) return;

    _documents.TryRemove(uri, out _);
    var filePath = uri.GetFileSystemPath()!;
    ProjectManager.RemoveFileFromProject(filePath);
  }

  private static bool IsMaxonFile(DocumentUri uri) {
    // Must be a real file:// URI with a .maxon or .test extension
    if (!string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase)) return false;
    var path = uri.GetFileSystemPath();
    return path != null && (
      path.EndsWith(".maxon", StringComparison.OrdinalIgnoreCase) ||
      path.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
    );
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
    info = new CompletionInfo(info.TypeDefs, info.Functions, variableTypes, info.TypeAliasSources);

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

    // Check if it's a keyword (but not when used as an enum case name)
    if (Lexer.KeywordMap.TryGetValue(word, out var keywordInfo)) {
      if (!IsInsideEnumBody(lines, (int)position.Line)) {
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

    // Try to show type information for variables, functions, and types
    var symbolHover = GetSymbolHover(uri, position, line, word);
    if (symbolHover != null) return symbolHover;

    return null;
  }

  private Hover? GetSymbolHover(DocumentUri uri, Position position, string line, string word) {
    var filePath = uri.GetFileSystemPath() ?? uri.ToString();
    var project = ProjectManager.FindProjectForFile(filePath);
    var info = project?.GetCompletionInfo();
    if (info == null) return null;

    var normalizedPath = Project.NormalizePath(filePath);

    // Check if it's a type alias — show the typealias definition
    if (info.TypeAliasSources != null && info.TypeAliasSources.TryGetValue(word, out var aliasInfo)) {
      info.TypeDefs.TryGetValue(word, out var aliasType);
      return MakeTypeAliasHover(aliasInfo, aliasType, word, position, line);
    }

    // Check if it's a type name
    if (info.TypeDefs.TryGetValue(word, out var mlirType)) {
      return MakeTypeHover(mlirType, word, position, line);
    }

    // Find the enclosing function first so we can resolve the receiver type for method calls
    var enclosingFunc = FindEnclosingFunction(info.Functions, normalizedPath, (int)position.Line + 1);

    // Detect dot-receiver (e.g. `arr.contains`) and resolve its type to narrow hover results
    var receiverName = GetReceiverName(line, (int)position.Character);
    string? receiverTypeName = null;
    if (receiverName != null) {
      receiverTypeName = ResolveReceiverType(enclosingFunc, receiverName);
      // Fallback: scan source text directly for variable declarations
      if (receiverTypeName == null) {
        var content = GetDocument(uri);
        if (content != null)
          receiverTypeName = ResolveReceiverTypeFromSource(content, receiverName);
      }
    }

    // Check if it's a function name — show signature, filtered by receiver type when known
    var funcHover = GetFunctionHover(info, word, position, line, receiverTypeName);
    if (funcHover != null) return funcHover;

    // Check function parameters
    if (enclosingFunc != null) {
      for (int i = 0; i < enclosingFunc.ParamNames.Count && i < enclosingFunc.ParamTypes.Count; i++) {
        if (enclosingFunc.ParamNames[i] == word) {
          var typeName = enclosingFunc.ParamTypes[i].Name;
          return MakeHover($"parameter {word} {typeName}", position, line, word);
        }
      }
    }

    // Check local variable declarations in the enclosing function's body
    if (enclosingFunc != null) {
      var varType = FindVariableTypeInFunction(enclosingFunc, word);
      if (varType != null) {
        return MakeHover($"{varType.Value.keyword} {word} {varType.Value.typeName}", position, line, word);
      }
    }

    // Check top-level global variables by scanning all functions for global loads
    var globalType = FindGlobalVariableType(info.Functions, word);
    if (globalType != null) {
      return MakeHover($"var {word} {globalType}", position, line, word);
    }

    return null;
  }

  /// <summary>
  /// If the word at the cursor is immediately preceded by '.identifier.', return that identifier.
  /// E.g., for 'arr.contains(', hovering on 'contains' returns 'arr'.
  /// </summary>
  private static string? GetReceiverName(string line, int cursorChar) {
    // Find the start of the word at the cursor
    var wordStart = Math.Min(cursorChar, line.Length);
    while (wordStart > 0 && IsWordChar(line[wordStart - 1])) wordStart--;

    // Must have a dot immediately before the word
    if (wordStart <= 0 || line[wordStart - 1] != '.') return null;

    // Extract the identifier before the dot
    var dotPos = wordStart - 1;
    var end = dotPos;
    var start = dotPos - 1;
    while (start >= 0 && IsWordChar(line[start])) start--;
    start++;

    return start < end ? line[start..end] : null;
  }

  /// <summary>
  /// Resolve the type name of a receiver variable from the enclosing function's params and locals.
  /// </summary>
  private static string? ResolveReceiverType(MlirFunction<MaxonOp>? enclosingFunc, string receiverName) {
    if (enclosingFunc == null) return null;

    // Check parameters
    for (int i = 0; i < enclosingFunc.ParamNames.Count && i < enclosingFunc.ParamTypes.Count; i++) {
      if (enclosingFunc.ParamNames[i] == receiverName)
        return enclosingFunc.ParamTypes[i].Name;
    }

    // Check local variables
    return FindVariableTypeInFunction(enclosingFunc, receiverName)?.typeName;
  }

  /// <summary>
  /// Source-text fallback: scan for `var name = Type{`, `let name = Type(`, `var name Type`,
  /// or parameter declarations like `name Type` in function signatures.
  /// </summary>
  private static string? ResolveReceiverTypeFromSource(string content, string receiverName) {
    var lines = content.Split('\n');
    foreach (var srcLine in lines) {
      var trimmed = srcLine.TrimStart();

      // Match: var/let name = TypeName{ or TypeName( or TypeName.
      if (trimmed.StartsWith("var ") || trimmed.StartsWith("let ")) {
        var rest = trimmed[4..].TrimStart();
        if (!rest.StartsWith(receiverName)) continue;
        var afterName = rest[receiverName.Length..];

        // `var name = TypeName{...}` or `var name = TypeName(...)`
        if (afterName.Length > 0 && (afterName[0] == ' ' || afterName[0] == '\t')) {
          var afterEq = afterName.TrimStart();
          if (afterEq.StartsWith("= ") || afterEq.StartsWith('=')) {
            afterEq = afterEq[(afterEq[1] == ' ' ? 2 : 1)..].TrimStart();
            var typeName = ExtractTypeNameToken(afterEq);
            if (typeName != null) return typeName;
          }
          // `var name TypeName` (typed declaration without =)
          else {
            var typeName = ExtractTypeNameToken(afterEq);
            if (typeName != null) return typeName;
          }
        }
      }

      // Match function parameter: `name TypeName` in a function signature line
      if (trimmed.StartsWith("function ")) {
        var parenStart = trimmed.IndexOf('(');
        var parenEnd = trimmed.LastIndexOf(')');
        if (parenStart >= 0 && parenEnd > parenStart) {
          var paramsStr = trimmed[(parenStart + 1)..parenEnd];
          var paramParts = paramsStr.Split(',');
          foreach (var param in paramParts) {
            var p = param.Trim();
            var spaceIdx = p.IndexOf(' ');
            if (spaceIdx > 0) {
              var pName = p[..spaceIdx];
              if (pName == receiverName) {
                var pType = p[(spaceIdx + 1)..].Trim();
                if (pType.Length > 0 && char.IsUpper(pType[0]))
                  return ExtractTypeNameToken(pType);
              }
            }
          }
        }
      }
    }
    return null;
  }

  /// <summary>
  /// Extract a type name identifier from the start of a string (uppercase-leading word).
  /// Stops at `{`, `(`, `.`, whitespace, or end of string.
  /// </summary>
  private static string? ExtractTypeNameToken(string text) {
    if (text.Length == 0 || !char.IsUpper(text[0])) return null;
    var end = 0;
    while (end < text.Length && IsWordChar(text[end])) end++;
    return end > 0 ? text[..end] : null;
  }

  /// <summary>
  /// Find the function whose source location encloses the given cursor position.
  /// </summary>
  private static MlirFunction<MaxonOp>? FindEnclosingFunction(
    List<MlirFunction<MaxonOp>> functions, string normalizedPath, int cursorLine1Based
  ) {
    MlirFunction<MaxonOp>? best = null;
    int bestLine = -1;
    foreach (var func in functions) {
      if (func.SourceFilePath == null || func.SourceLine == null) continue;
      var funcPath = Project.NormalizePath(func.SourceFilePath);
      if (!funcPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)) continue;
      var funcLine = func.SourceLine.Value;
      if (funcLine <= cursorLine1Based && funcLine > bestLine) {
        best = func;
        bestLine = funcLine;
      }
    }
    return best;
  }

  /// <summary>
  /// Search a function's body ops for a variable declaration matching the given name.
  /// Returns the keyword (let/var) and type name if found.
  /// </summary>
  private static (string keyword, string typeName)? FindVariableTypeInFunction(
    MlirFunction<MaxonOp> func, string varName
  ) {
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonAssignOp assign && assign.IsDeclaration && assign.VarName == varName) {
          var keyword = assign.IsMutable ? "var" : "let";
          var typeName = assign.ValueKind switch {
            MaxonValueKind.Struct when assign.Value is MaxonStruct s => s.TypeName,
            MaxonValueKind.Enum when assign.Value is MaxonEnum e => e.TypeName,
            _ => assign.ValueKind.ToString().ToLower()
          };
          return (keyword, typeName);
        }
      }
    }
    return null;
  }

  /// <summary>
  /// Search for a global variable's type by finding MaxonGlobalLoadOp references to it.
  /// </summary>
  private static string? FindGlobalVariableType(List<MlirFunction<MaxonOp>> functions, string globalName) {
    foreach (var func in functions) {
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonGlobalLoadOp load && load.GlobalName == globalName) {
            if (load.StructTypeName != null) return load.StructTypeName;
            if (load.EnumTypeName != null) return load.EnumTypeName;
            return load.ValueKind.ToString().ToLower();
          }
        }
      }
    }
    return null;
  }

  private static Hover? GetFunctionHover(CompletionInfo info, string word, Position position, string line, string? receiverTypeName = null) {
    var matchingFuncs = info.Functions.Where(f => {
      if (f.Name == word) return true;
      var dotIdx = f.Name.LastIndexOf('.');
      if (dotIdx >= 0) {
        var methodPart = f.Name[(dotIdx + 1)..];
        var dollarIdx = methodPart.IndexOf('$');
        if (dollarIdx >= 0) methodPart = methodPart[..dollarIdx];
        return methodPart == word;
      }
      return false;
    }).ToList();

    // If we know the receiver type, narrow to that type's methods.
    // Also resolve through type aliases: e.g. IntArray -> Array so that
    // generic stdlib methods (Array.contains) match an IntArray receiver.
    if (receiverTypeName != null && matchingFuncs.Count > 1) {
      var acceptableTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { receiverTypeName };
      var cursor = receiverTypeName;
      while (info.TypeAliasSources != null && info.TypeAliasSources.TryGetValue(cursor, out var aliasInfo)) {
        acceptableTypes.Add(aliasInfo.SourceTypeName);
        cursor = aliasInfo.SourceTypeName;
      }

      // Match function names containing ".TypeName." (handles namespace prefixes like "stdlib.Array.contains")
      var narrowed = matchingFuncs.Where(f => {
        foreach (var typeName in acceptableTypes) {
          var dotType = $".{typeName}.";
          var prefixType = $"{typeName}.";
          if (f.Name.Contains(dotType) || f.Name.StartsWith(prefixType)) return true;
        }
        return false;
      }).ToList();
      if (narrowed.Count > 0) matchingFuncs = narrowed;
    }

    if (matchingFuncs.Count == 0) return null;

    var signatures = new List<string>();
    foreach (var func in matchingFuncs) {
      var paramParts = new List<string>();
      for (int i = 0; i < func.ParamNames.Count; i++) {
        var paramType = i < func.ParamTypes.Count ? func.ParamTypes[i].Name : "?";
        paramParts.Add($"{func.ParamNames[i]} {paramType}");
      }
      var paramsStr = string.Join(", ", paramParts);
      var returnStr = func.ReturnType != null && func.ReturnType.Name != "void"
        ? $" returns {func.ReturnType.Name}" : "";
      var throwsStr = func.ThrowsType != null ? $" throws {func.ThrowsType.Name}" : "";
      signatures.Add($"function {word}({paramsStr}){returnStr}{throwsStr}");
    }

    var unique = signatures.Distinct().ToList();
    var value = string.Join("\n\n", unique.Select(s => $"```maxon\n{s}\n```"));

    return new Hover {
      Contents = new MarkedStringsOrMarkupContent(
        new MarkupContent { Kind = MarkupKind.Markdown, Value = value }
      ),
      Range = GetWordRange(position, line, word)
    };
  }

  private static Hover MakeTypeHover(MlirType mlirType, string word, Position position, string line) {
    string kind;
    string details = "";
    string? docString = null;
    if (mlirType is MlirStructType structType) {
      kind = "type";
      var fields = structType.Fields
        .Where(f => f.IsExported)
        .Select(f => $"  export var {f.Name} {f.Type.Name}");
      if (fields.Any())
        details = "\n\n" + string.Join("\n", fields);
      docString = structType.DocString;
    } else if (mlirType is MlirEnumType) {
      kind = "enum";
    } else if (mlirType is MlirInterfaceType) {
      kind = "interface";
    } else {
      kind = "type";
    }

    var docPart = docString != null ? $"\n\n{docString}" : "";
    return new Hover {
      Contents = new MarkedStringsOrMarkupContent(
        new MarkupContent {
          Kind = MarkupKind.Markdown,
          Value = $"```maxon\n{kind} {word}\n```{details}{docPart}"
        }
      ),
      Range = GetWordRange(position, line, word)
    };
  }

  private static Hover MakeTypeAliasHover(TypeAliasInfo aliasInfo, MlirType? resolvedType, string word, Position position, string line) {
    string rhs;
    if (resolvedType is MlirRangedPrimitiveType ranged) {
      // Show the full ranged definition: int(0 to u64.max)
      rhs = FormatRangedType(ranged);
    } else {
      rhs = aliasInfo.SourceTypeName;
      if (aliasInfo.TypeParams is { Count: > 0 }) {
        rhs += " with " + string.Join(", ", aliasInfo.TypeParams.Keys);
      }
    }
    return new Hover {
      Contents = new MarkedStringsOrMarkupContent(
        new MarkupContent {
          Kind = MarkupKind.Markdown,
          Value = $"```maxon\ntypealias {word} = {rhs}\n```"
        }
      ),
      Range = GetWordRange(position, line, word)
    };
  }

  private static string FormatRangedType(MlirRangedPrimitiveType ranged) {
    var baseName = ranged.BaseType.Name;
    var lower = FormatRangeBound(ranged.LowerBound);
    var upper = FormatRangeBound(ranged.UpperBound);
    return $"{baseName}({lower} to {upper})";
  }

  private static string FormatRangeBound(double value) {
    // Show well-known bounds as type qualifiers
    if (value == 0) return "0";
    if (value == 255) return "u8.max";
    if (value == 127) return "i8.max";
    if (value == -128) return "i8.min";
    if (value == 65535) return "u16.max";
    if (value == 32767) return "i16.max";
    if (value == -32768) return "i16.min";
    if (value == 4294967295) return "u32.max";
    if (value == 2147483647) return "i32.max";
    if (value == -2147483648) return "i32.min";
    if (value == 18446744073709551615.0) return "u64.max";
    if (value == 9223372036854775807.0) return "i64.max";
    if (value == -9223372036854775808.0) return "i64.min";
    // For float bounds
    if (value == double.MaxValue) return "f64.max";
    if (value == double.MinValue) return "f64.min";
    if (value == float.MaxValue) return "f32.max";
    if (value == float.MinValue) return "f32.min";
    // Fallback: format as integer if whole number, else as float
    if (value == Math.Floor(value) && !double.IsInfinity(value))
      return ((long)value).ToString();
    return value.ToString();
  }

  private static Hover MakeHover(string code, Position position, string line, string word) {
    return new Hover {
      Contents = new MarkedStringsOrMarkupContent(
        new MarkupContent {
          Kind = MarkupKind.Markdown,
          Value = $"```maxon\n{code}\n```"
        }
      ),
      Range = GetWordRange(position, line, word)
    };
  }

  /// <summary>
  /// Returns true if the line is inside an enum body and looks like a case name
  /// (a single word on the line, possibly with = value, not a method body line).
  /// </summary>
  private static bool IsInsideEnumBody(string[] lines, int lineIndex) {
    var line = lines[lineIndex].Trim();

    // Strip trailing comment
    var commentIdx = line.IndexOf("//");
    if (commentIdx >= 0) line = line[..commentIdx].Trim();

    // An enum case is a single word (possibly followed by = value)
    var isEnumCaseLine = false;
    if (line.Length > 0 && line.All(c => char.IsLetterOrDigit(c) || c == '_')) {
      isEnumCaseLine = true;
    } else {
      var eqIdx = line.IndexOf('=');
      if (eqIdx > 0) {
        var name = line[..eqIdx].Trim();
        isEnumCaseLine = name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_');
      }
    }
    if (!isEnumCaseLine) return false;

    // Scan backwards to find enclosing enum declaration
    for (int i = lineIndex - 1; i >= 0; i--) {
      var trimmed = lines[i].Trim();
      if (trimmed.Length == 0 || trimmed.StartsWith("//")) continue;

      // Check for enum declaration (with optional 'export' prefix)
      var enumCheck = trimmed;
      if (enumCheck.StartsWith("export ")) enumCheck = enumCheck[7..];
      var nameOffset = enumCheck.StartsWith("enum ") ? 5 : -1;
      if (nameOffset >= 0 && enumCheck.Length > nameOffset && char.IsUpper(enumCheck[nameOffset])) {
        return true;
      }
      // If we hit a top-level construct, we're not inside an enum
      if (trimmed.StartsWith("function ") || trimmed.StartsWith("type ") || trimmed.StartsWith("interface ")) {
        return false;
      }
      var rawLine = lines[i].TrimEnd();
      if (rawLine.Length > 0 && !char.IsWhiteSpace(rawLine[0]) && trimmed.StartsWith("end ")) {
        return false;
      }
    }
    return false;
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

  public Location? GetDefinition(DocumentUri uri, Position position) {
    if (!_documents.TryGetValue(uri, out var content))
      return null;

    var lines = content.Split('\n');
    if (position.Line >= lines.Length)
      return null;

    var line = lines[position.Line];
    var word = GetWordAtPosition(line, position.Character);
    if (string.IsNullOrEmpty(word))
      return null;

    // Try local definition first (let/var bindings, function parameters, for-loop variables)
    var localDef = FindLocalDefinition(lines, word, (int)position.Line, uri);
    if (localDef != null)
      return localDef;

    var filePath = uri.GetFileSystemPath() ?? uri.ToString();
    var project = ProjectManager.FindProjectForFile(filePath);

    // If the cursor word is preceded by "EnumName." on the same line, capture the qualifier
    // so enum case search can be restricted to that specific enum (avoids cross-enum collisions).
    string? dotQualifier = null;
    {
      var wordStart = (int)position.Character;
      while (wordStart > 0 && IsWordChar(line[wordStart - 1])) wordStart--;
      if (wordStart > 0 && line[wordStart - 1] == '.') {
        var qualEnd = wordStart - 1;
        var qualStart = qualEnd;
        while (qualStart > 0 && IsWordChar(line[qualStart - 1])) qualStart--;
        if (qualStart < qualEnd)
          dotQualifier = line[qualStart..qualEnd];
      }
    }

    // Text-based search for type/typealias/enum/interface/function declarations
    // across the current file, project files, and stdlib
    var textDef = FindDefinitionByTextSearch(word, project, dotQualifier);
    if (textDef != null)
      return textDef;

    return null;
  }

  /// <summary>
  /// Search project files and stdlib source for declarations of the given name.
  /// Looks for: type NAME, typealias NAME, enum NAME, interface NAME, function NAME(,
  /// and enum case declarations (bare NAME, NAME = value, NAME(type...))
  /// When <paramref name="dotQualifier"/> is non-null (e.g. "TokenKind" from "TokenKind.floatLiteral"),
  /// case hits are only accepted when the enclosing enum has that name.
  /// </summary>
  private static Location? FindDefinitionByTextSearch(string word, Project? project, string? dotQualifier = null) {
    // Collect all files to search: project files first, then stdlib
    var filesToSearch = new List<(string path, string content)>();

    if (project != null) {
      foreach (var (path, content) in project.GetFileContents())
        filesToSearch.Add((path, content));
    }

    // Add stdlib files
    var stdlibSources = StdlibLoader.LoadStdlibModules();
    foreach (var source in stdlibSources)
      filesToSearch.Add((source.Path, source.Content));

    // Declaration patterns to match (the name must be followed by appropriate context)
    foreach (var (path, content) in filesToSearch) {
      var fileLines = content.Split('\n');
      for (int i = 0; i < fileLines.Length; i++) {
        var trimmed = fileLines[i].TrimStart();
        // Strip leading "export "
        var decl = trimmed;
        if (decl.StartsWith("export "))
          decl = decl[7..];

        var indent = fileLines[i].Length - fileLines[i].TrimStart().Length;
        var isTopLevel = indent == 0;
        var col = FindDeclarationOfName(decl, word, isTopLevel);
        if (col >= 0) {
          var exportOffset = trimmed.StartsWith("export ") ? 7 : 0;
          var finalCol = indent + exportOffset + col;
          return new Location {
            Uri = DocumentUri.FromFileSystemPath(path),
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
              new Position(i, finalCol),
              new Position(i, finalCol + word.Length)
            )
          };
        }

        // Check enum case declarations:
        // A line inside an enum body that starts with the word as a bare identifier,
        // optionally followed by '= value' (raw value) or '(...)' (associated values).
        if (!trimmed.StartsWith("//") && MatchesName(trimmed, 0, word)) {
          var afterWord = trimmed[word.Length..];
          var strippedAfter = afterWord.Contains("//")
            ? afterWord[..afterWord.IndexOf("//")].TrimEnd() : afterWord.TrimEnd();
          strippedAfter = strippedAfter.TrimStart();
          var isEnumCaseContext = strippedAfter.Length == 0
            || strippedAfter.StartsWith('=')
            || strippedAfter.StartsWith('(');
          if (isEnumCaseContext) {
            var enclosingEnum = GetEnclosingEnumName(fileLines, i);
            // If we have a dot-qualifier (e.g. "TokenKind"), only match enum cases
            // inside the enum with that name; otherwise accept any enum.
            var enumMatches = enclosingEnum != null
              && (dotQualifier == null || enclosingEnum == dotQualifier);
            if (enumMatches) {
              return new Location {
                Uri = DocumentUri.FromFileSystemPath(path),
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                  new Position(i, indent),
                  new Position(i, indent + word.Length)
                )
              };
            }
          }
        }
      }
    }

    return null;
  }

  /// <summary>
  /// If the line at <paramref name="lineIndex"/> is inside an enum body, returns the enum's
  /// name; otherwise returns null.
  /// Scans upward for the first line with strictly less indentation and checks whether it
  /// is an enum declaration.
  /// </summary>
  private static string? GetEnclosingEnumName(string[] lines, int lineIndex) {
    var lineIndent = lines[lineIndex].Length - lines[lineIndex].TrimStart().Length;
    if (lineIndent == 0) return null; // top-level lines are never enum cases

    for (int i = lineIndex - 1; i >= 0; i--) {
      var rawLine = lines[i];
      var curIndent = rawLine.Length - rawLine.TrimStart().Length;
      if (curIndent >= lineIndent) continue; // same or deeper indentation — skip

      var t = rawLine.TrimStart();
      if (t.Length == 0 || t.StartsWith("//")) continue;

      // Strip optional export prefix
      var d = t.StartsWith("export ") ? t[7..] : t;
      int nameStart;
      if (d.StartsWith("enum ")) nameStart = 5;
      else return null;
      var nameEnd = nameStart;
      while (nameEnd < d.Length && IsWordChar(d[nameEnd])) nameEnd++;
      return nameEnd > nameStart ? d[nameStart..nameEnd] : null;
    }
    return null;
  }

  /// <summary>
  /// Check if a line (with "export " already stripped) declares the given name.
  /// Returns the column of the name within the line, or -1.
  /// Matches: type NAME, typealias NAME, enum NAME, interface NAME, function NAME(,
  /// and top-level let NAME / var NAME (when isTopLevel is true)
  /// </summary>
  private static int FindDeclarationOfName(string decl, string word, bool isTopLevel = false) {
    // type NAME (may have "uses" or "implements" after)
    if (decl.StartsWith("type ") && MatchesName(decl, 5, word))
      return 5;
    // typealias NAME
    if (decl.StartsWith("typealias ") && MatchesName(decl, 10, word))
      return 10;
    // enum NAME
    if (decl.StartsWith("enum ") && MatchesName(decl, 5, word))
      return 5;
    // interface NAME
    if (decl.StartsWith("interface ") && MatchesName(decl, 10, word))
      return 10;
    // function NAME(
    if (decl.StartsWith("function ") && MatchesName(decl, 9, word))
      return 9;
    // Top-level let NAME / var NAME (only at file scope, not inside functions)
    if (isTopLevel) {
      if (decl.StartsWith("let ") && MatchesName(decl, 4, word))
        return 4;
      if (decl.StartsWith("var ") && MatchesName(decl, 4, word))
        return 4;
    }
    return -1;
  }

  /// <summary>
  /// Check if 'word' appears at position 'offset' in the line, followed by
  /// a non-word character (space, paren, newline, end of string, etc.)
  /// </summary>
  private static bool MatchesName(string line, int offset, string word) {
    if (offset + word.Length > line.Length)
      return false;
    if (line.AsSpan(offset, word.Length).SequenceEqual(word.AsSpan()) == false)
      return false;
    // Must be followed by a non-word char or end of line
    var afterEnd = offset + word.Length;
    if (afterEnd >= line.Length)
      return true;
    return !IsWordChar(line[afterEnd]);
  }

  /// <summary>
  /// Find the definition of a local variable, parameter, or for-loop variable
  /// by scanning the current document. Looks for:
  ///   - let NAME = ... / var NAME = ...
  ///   - function foo(NAME Type, ...)
  ///   - for NAME in ...
  /// </summary>
  private static Location? FindLocalDefinition(string[] lines, string word, int cursorLine, DocumentUri uri) {
    // First, find the enclosing function to know where to look for parameters
    int funcLine = -1;
    for (int i = cursorLine; i >= 0; i--) {
      var trimmed = lines[i].TrimStart();
      if (trimmed.StartsWith("function ") || trimmed.StartsWith("export function ")) {
        funcLine = i;
        break;
      }
    }

    // Scan backwards from cursor for let/var/for declarations
    for (int i = cursorLine; i >= 0; i--) {
      var trimmed = lines[i].TrimStart();
      var indent = lines[i].Length - trimmed.Length;
      var col = FindLetVarDeclaration(trimmed, word);
      if (col >= 0) {
        // Skip top-level let/var (no indentation) — those are handled as globals
        if (indent == 0)
          return null;
        return MakeLocation(uri, i, indent + col, word.Length);
      }

      // Check for-loop variable: "for NAME in ..."
      col = FindForLoopVariable(trimmed, word);
      if (col >= 0) {
        return MakeLocation(uri, i, indent + col, word.Length);
      }

      // If we've reached the enclosing function declaration, check its parameters
      if (i == funcLine) {
        col = FindParameterDeclaration(lines[i], word);
        if (col >= 0)
          return MakeLocation(uri, i, col, word.Length);
        break;
      }
    }

    return null;
  }

  /// <summary>
  /// Check if a trimmed line declares 'word' via let or var.
  /// Returns the column (relative to trimmed line) of the name, or -1.
  /// Handles: let NAME = ..., var NAME = ..., let NAME Type = ...
  /// </summary>
  private static int FindLetVarDeclaration(string trimmed, string word) {
    string? rest = null;
    if (trimmed.StartsWith("let "))
      rest = trimmed[4..];
    else if (trimmed.StartsWith("var "))
      rest = trimmed[4..];

    if (rest == null) return -1;

    // The name is the first word in rest
    var nameEnd = 0;
    while (nameEnd < rest.Length && IsWordChar(rest[nameEnd]))
      nameEnd++;

    if (nameEnd == 0) return -1;
    var name = rest[..nameEnd];
    if (name != word) return -1;

    // Column offset: "let " or "var " = 4 chars
    return 4;
  }

  /// <summary>
  /// Check if a trimmed line declares 'word' as a for-loop variable.
  /// Handles: for NAME in ...
  /// Returns the column (relative to trimmed line) of the name, or -1.
  /// </summary>
  private static int FindForLoopVariable(string trimmed, string word) {
    if (!trimmed.StartsWith("for ")) return -1;
    var rest = trimmed[4..];

    var nameEnd = 0;
    while (nameEnd < rest.Length && IsWordChar(rest[nameEnd]))
      nameEnd++;

    if (nameEnd == 0) return -1;
    var name = rest[..nameEnd];
    if (name != word) return -1;

    // Verify " in " follows
    var afterName = rest[nameEnd..];
    if (!afterName.StartsWith(" in ") && !afterName.StartsWith(" in\n") && !afterName.StartsWith(" in\r"))
      return -1;

    return 4; // "for " = 4 chars
  }

  /// <summary>
  /// Check if a function declaration line contains 'word' as a parameter name.
  /// Maxon parameters: function foo(name Type, name2 Type2)
  /// Returns the column of the parameter name, or -1.
  /// </summary>
  private static int FindParameterDeclaration(string line, string word) {
    var parenIdx = line.IndexOf('(');
    if (parenIdx < 0) return -1;

    var closeIdx = line.IndexOf(')', parenIdx);
    if (closeIdx < 0) closeIdx = line.Length;

    var paramsStr = line[(parenIdx + 1)..closeIdx];
    // Split by comma, each param is "name Type"
    var paramParts = paramsStr.Split(',');
    foreach (var part in paramParts) {
      var p = part.Trim();
      // Extract the parameter name (first word)
      var nameEnd = 0;
      while (nameEnd < p.Length && IsWordChar(p[nameEnd]))
        nameEnd++;
      if (nameEnd == 0) continue;
      var paramName = p[..nameEnd];
      if (paramName != word) continue;

      // Find the actual column in the original line
      var searchStart = parenIdx + 1;
      var idx = line.IndexOf(paramName, searchStart);
      if (idx >= 0)
        return idx;
    }

    return -1;
  }

  private static Location MakeLocation(DocumentUri uri, int line, int col, int length) {
    return new Location {
      Uri = uri,
      Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
        new Position(line, col),
        new Position(line, col + length)
      )
    };
  }

  // ── Rename support ──────────────────────────────────────────────────

  public RangeOrPlaceholderRange? PrepareRename(DocumentUri uri, Position position) {
    if (!_documents.TryGetValue(uri, out var content))
      return null;

    var lines = content.Split('\n');
    if (position.Line >= lines.Length)
      return null;

    var line = lines[position.Line];
    var word = GetWordAtPosition(line, position.Character);
    if (string.IsNullOrEmpty(word))
      return null;

    // Reject keywords (unless inside enum body where they're case names)
    if (Lexer.KeywordMap.ContainsKey(word) && !IsInsideEnumBody(lines, (int)position.Line))
      return null;

    // Must be able to find a definition for this symbol
    var filePath = uri.GetFileSystemPath() ?? uri.ToString();
    var project = ProjectManager.FindProjectForFile(filePath);

    // Check local definition
    var localDef = FindLocalDefinition(lines, word, (int)position.Line, uri);
    if (localDef != null) {
      // Local definitions are always in the current file, so always renamable
      return new RangeOrPlaceholderRange(GetWordRange(position, line, word));
    }

    // Check global definition
    var globalDef = FindDefinitionByTextSearch(word, project);
    if (globalDef != null) {
      // Reject if definition is in stdlib
      var defPath = globalDef.Uri.GetFileSystemPath() ?? globalDef.Uri.ToString();
      if (IsStdlibFile(defPath))
        return null;
      return new RangeOrPlaceholderRange(GetWordRange(position, line, word));
    }

    return null;
  }

  public WorkspaceEdit? GetRename(DocumentUri uri, Position position, string newName) {
    if (!_documents.TryGetValue(uri, out var content))
      return null;

    var lines = content.Split('\n');
    if (position.Line >= lines.Length)
      return null;

    var line = lines[position.Line];
    var word = GetWordAtPosition(line, position.Character);
    if (string.IsNullOrEmpty(word) || word == newName)
      return null;

    // Validate new name is a valid identifier
    if (!IsValidIdentifier(newName))
      return null;

    // Reject renaming to a keyword
    if (Lexer.KeywordMap.ContainsKey(newName))
      return null;

    var filePath = uri.GetFileSystemPath() ?? uri.ToString();
    var project = ProjectManager.FindProjectForFile(filePath);

    // Classify: local or global?
    var localDef = FindLocalDefinition(lines, word, (int)position.Line, uri);
    if (localDef != null) {
      var references = FindLocalReferences(lines, word, (int)position.Line, filePath);
      return BuildWorkspaceEdit(references, word, newName);
    }

    var globalDef = FindDefinitionByTextSearch(word, project);
    if (globalDef != null) {
      var defPath = globalDef.Uri.GetFileSystemPath() ?? globalDef.Uri.ToString();
      if (IsStdlibFile(defPath))
        return null;

      // Top-level let/var are file-scoped, not project-wide
      if (IsFileScopedDeclaration(globalDef)) {
        var references = FindFileScopedReferences(word, defPath);
        return BuildWorkspaceEdit(references, word, newName);
      }

      var references2 = FindGlobalReferences(word, project);
      return BuildWorkspaceEdit(references2, word, newName);
    }

    return null;
  }

  private static bool IsValidIdentifier(string name) {
    if (string.IsNullOrEmpty(name))
      return false;
    if (!char.IsLetter(name[0]) && name[0] != '_')
      return false;
    for (int i = 1; i < name.Length; i++) {
      if (!IsWordChar(name[i]))
        return false;
    }
    return true;
  }

  /// <summary>
  /// Check if a definition location points to a non-exported top-level let/var
  /// declaration (file-scoped, not project-wide).
  /// </summary>
  private static bool IsFileScopedDeclaration(Location def) {
    var defPath = def.Uri.GetFileSystemPath() ?? def.Uri.ToString();
    try {
      var content = File.ReadAllText(defPath);
      var lines = content.Split('\n');
      var lineIdx = (int)def.Range.Start.Line;
      if (lineIdx >= lines.Length) return false;
      var trimmed = lines[lineIdx].TrimStart();
      // Exported let/var can be used across files
      if (trimmed.StartsWith("export ")) return false;
      return trimmed.StartsWith("let ") || trimmed.StartsWith("var ");
    } catch {
      return false;
    }
  }

  /// <summary>
  /// Find all references to a file-scoped symbol within a single file.
  /// Also finds end-label references.
  /// </summary>
  private List<(string path, int line, int col)> FindFileScopedReferences(string word, string filePath) {
    var references = new List<(string path, int line, int col)>();

    // Try open document first, fall back to disk
    string? content = null;
    foreach (var (docUri, docContent) in _documents) {
      var docPath = docUri.GetFileSystemPath() ?? docUri.ToString();
      if (Project.NormalizePath(docPath) == Project.NormalizePath(filePath)) {
        content = docContent;
        break;
      }
    }
    content ??= File.ReadAllText(filePath);

    var fileLines = content.Split('\n');
    for (int i = 0; i < fileLines.Length; i++) {
      foreach (var col in FindAllWordOccurrences(fileLines[i], word))
        references.Add((filePath, i, col));
    }

    return references;
  }

  private static bool IsStdlibFile(string path) {
    var stdlibPath = StdlibLoader.FindStdlibPath();
    if (stdlibPath == null) return false;
    var normalizedPath = Project.NormalizePath(path);
    var normalizedStdlib = Project.NormalizePath(stdlibPath);
    return normalizedPath.StartsWith(normalizedStdlib, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Find all references to a local symbol (let/var/param/for-var) within the
  /// enclosing function scope.
  /// </summary>
  private static List<(string path, int line, int col)> FindLocalReferences(
    string[] lines, string word, int cursorLine, string filePath
  ) {
    var references = new List<(string path, int line, int col)>();

    var scope = FindEnclosingFunctionScope(lines, cursorLine);
    if (scope == null) return references;

    var (startLine, endLine) = scope.Value;
    for (int i = startLine; i <= endLine && i < lines.Length; i++) {
      foreach (var col in FindAllWordOccurrences(lines[i], word)) {
        references.Add((filePath, i, col));
      }
    }

    return references;
  }

  /// <summary>
  /// Find all references to a global symbol (function/type/enum/interface/typealias)
  /// across all project files. Also finds end-label references.
  /// </summary>
  private static List<(string path, int line, int col)> FindGlobalReferences(string word, Project? project) {
    var references = new List<(string path, int line, int col)>();
    if (project == null) return references;

    foreach (var (path, content) in project.GetFileContents()) {
      var fileLines = content.Split('\n');
      for (int i = 0; i < fileLines.Length; i++) {
        // Word-boundary matches
        foreach (var col in FindAllWordOccurrences(fileLines[i], word)) {
          references.Add((path, i, col));
        }
        // End-label matches: end 'word'
        var endLabelCol = FindEndLabelOccurrence(fileLines[i], word);
        if (endLabelCol >= 0)
          references.Add((path, i, endLabelCol));
      }
    }

    return references;
  }

  /// <summary>
  /// Find the start and end lines of the enclosing function scope.
  /// </summary>
  private static (int startLine, int endLine)? FindEnclosingFunctionScope(string[] lines, int cursorLine) {
    // Scan backwards for function declaration
    int funcLine = -1;
    string? funcName = null;
    for (int i = cursorLine; i >= 0; i--) {
      var trimmed = lines[i].TrimStart();
      var stripped = trimmed;
      if (stripped.StartsWith("export ")) stripped = stripped[7..];
      if (stripped.StartsWith("static ")) stripped = stripped[7..];
      if (stripped.StartsWith("function ")) {
        funcLine = i;
        var rest = stripped[9..];
        var nameEnd = 0;
        while (nameEnd < rest.Length && IsWordChar(rest[nameEnd])) nameEnd++;
        if (nameEnd > 0) funcName = rest[..nameEnd];
        break;
      }
    }

    if (funcLine < 0 || funcName == null) return null;

    // Scan forward for end 'funcName'
    var endPattern = $"end '{funcName}'";
    for (int i = funcLine + 1; i < lines.Length; i++) {
      var trimmed = lines[i].TrimStart();
      if (trimmed == endPattern || trimmed.StartsWith(endPattern + " ") || trimmed.StartsWith(endPattern + "\r"))
        return (funcLine, i);
    }

    // Fallback: function goes to end of file
    return (funcLine, lines.Length - 1);
  }

  /// <summary>
  /// Find all positions where 'word' appears as a whole word (with non-word boundaries)
  /// in a single line of text.
  /// </summary>
  private static List<int> FindAllWordOccurrences(string lineText, string word) {
    var results = new List<int>();
    var searchStart = 0;
    while (true) {
      var idx = lineText.IndexOf(word, searchStart, StringComparison.Ordinal);
      if (idx < 0) break;

      var charBefore = idx > 0 ? lineText[idx - 1] : '\0';
      var afterIdx = idx + word.Length;
      var charAfter = afterIdx < lineText.Length ? lineText[afterIdx] : '\0';

      if (!IsWordChar(charBefore) && !IsWordChar(charAfter))
        results.Add(idx);

      searchStart = idx + 1;
    }
    return results;
  }

  /// <summary>
  /// Check if a line contains an end-label matching the word: end 'word'
  /// Returns the column of the name inside the quotes, or -1.
  /// </summary>
  private static int FindEndLabelOccurrence(string lineText, string word) {
    var trimmed = lineText.TrimStart();
    if (!trimmed.StartsWith("end '")) return -1;

    var pattern = $"'{word}'";
    var idx = lineText.IndexOf(pattern, StringComparison.Ordinal);
    if (idx < 0) return -1;

    // Return the position of the name (inside the quotes)
    return idx + 1;
  }

  private static WorkspaceEdit BuildWorkspaceEdit(
    List<(string path, int line, int col)> references, string oldName, string newName
  ) {
    var changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>();

    foreach (var group in references.GroupBy(r => r.path)) {
      var uri = DocumentUri.FromFileSystemPath(group.Key);
      var edits = group.Select(r => new TextEdit {
        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
          new Position(r.line, r.col),
          new Position(r.line, r.col + oldName.Length)
        ),
        NewText = newName
      }).ToList();
      changes[uri] = edits;
    }

    return new WorkspaceEdit { Changes = changes };
  }

}

public record CompletionInfo(
  Dictionary<string, MlirType> TypeDefs,
  List<MlirFunction<MaxonOp>> Functions,
  Dictionary<string, string> VariableTypes,
  Dictionary<string, TypeAliasInfo>? TypeAliasSources = null
);
