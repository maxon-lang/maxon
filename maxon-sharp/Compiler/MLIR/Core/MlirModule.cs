namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirGlobal(string name, MlirType type, MlirAttribute? initValue = null) {
  public string Name { get; } = name;
  public MlirType Type { get; } = type;
  public MlirAttribute? InitValue { get; } = initValue;
}

// Represents a type alias with its source type, type parameter substitutions, and visibility metadata
public record TypeAliasInfo(string SourceTypeName, Dictionary<string, MlirType>? TypeParams,
    bool IsExported = false, bool IsStdlib = false, string? SourceFilePath = null);

// Metadata for constant array literals that can be placed in .rdata
public record ConstantArrayLiteralInfo(string RdataLabel, long[] Values, bool IsMutable, int ElementSize);

// Deferred global variable initialization: stores tokens for expressions that must be evaluated at main() entry
public record DeferredGlobalInit(string Name, List<Token> Tokens, int TokenStart, int TokenEnd, bool IsMutable, int Line, int Column, string? SourceFilePath = null);

public class MlirModule<TOp> where TOp : IPrintableOp {
  public List<MlirFunction<TOp>> Functions { get; } = [];
  public List<(string label, byte[] bytes, int alignment)> RdataEntries { get; } = [];
  public List<MlirGlobal> Globals { get; } = [];
  public Dictionary<string, MlirType> TypeDefs { get; } = [];
  public Dictionary<string, Dictionary<int, MlirAttribute>> FunctionDefaults { get; } = [];
  // Type alias tracking: aliasName -> TypeAliasInfo (sourceTypeName + typeParams)
  public Dictionary<string, TypeAliasInfo> TypeAliasSources { get; } = [];

  // Constant array literal metadata: struct result ID -> ConstantArrayLiteralInfo
  // Populated by ConstantArrayAnalysisPass, consumed by MaxonToStandardConversion
  public Dictionary<int, ConstantArrayLiteralInfo> ConstantArrayLiterals { get; } = [];

  // Interface associated type names (interfaceName -> list of 'uses' type names)
  public Dictionary<string, List<string>> InterfaceAssociatedTypes { get; } = [];

  // Primitive type conformances from extension blocks (e.g., "int" -> ["Hashable", "Equatable"])
  public Dictionary<string, List<string>> PrimitiveConformances { get; } = [];

  // Deferred global var/let initializations from all source files, emitted at start of main()
  public List<DeferredGlobalInit> DeferredGlobalInits { get; } = [];

  // Non-exported type/enum/typealias names — filtered from _typeRegistry when seeding other files
  public HashSet<string> NonExportedTypeNames { get; } = [];

  // Source file path for each type/enum/typealias (for file-scoped visibility checks)
  public Dictionary<string, string> TypeDefSourceFiles { get; } = [];

  // Ambiguous exported type names (same name from different files)
  public HashSet<string> AmbiguousTypeNames { get; } = [];

  public void AddFunction(MlirFunction<TOp> func) {
    Functions.Add(func);
  }

  public MlirModule<TOp> Clone() {
    var clone = new MlirModule<TOp>();
    clone.Functions.AddRange(Functions);
    clone.RdataEntries.AddRange(RdataEntries);
    clone.Globals.AddRange(Globals);
    foreach (var (k, v) in TypeDefs) clone.TypeDefs[k] = v;
    foreach (var (k, v) in FunctionDefaults) clone.FunctionDefaults[k] = v;
    foreach (var (k, v) in TypeAliasSources) clone.TypeAliasSources[k] = v;
    foreach (var (k, v) in ConstantArrayLiterals) clone.ConstantArrayLiterals[k] = v;
    foreach (var (k, v) in InterfaceAssociatedTypes) clone.InterfaceAssociatedTypes[k] = v;
    foreach (var (k, v) in PrimitiveConformances) clone.PrimitiveConformances[k] = [.. v];
    clone.DeferredGlobalInits.AddRange(DeferredGlobalInits);
    foreach (var n in NonExportedTypeNames) clone.NonExportedTypeNames.Add(n);
    foreach (var (k, v) in TypeDefSourceFiles) clone.TypeDefSourceFiles[k] = v;
    foreach (var n in AmbiguousTypeNames) clone.AmbiguousTypeNames.Add(n);
    return clone;
  }

  public void Merge(MlirModule<TOp> other) {
    // Add or replace functions - replace stubs (no body) with full functions (with body)
    var existingByName = Functions.ToDictionary(f => f.Name);
    foreach (var func in other.Functions) {
      if (existingByName.TryGetValue(func.Name, out var existing)) {
        if (func.Body.Blocks.Count > 0 && existing.Body.Blocks.Count == 0) {
          Functions.Remove(existing);
          Functions.Add(func);
          existingByName[func.Name] = func;
        } else if (func.Body.Blocks.Count > 0 && existing.Body.Blocks.Count > 0
                   && !ReferenceEquals(func, existing)) {
          throw new CompileError(ErrorCode.SemanticDuplicateDefinition,
            $"Duplicate function '{func.Name}'", func.SourceLine, func.SourceColumn);
        }
      } else {
        Functions.Add(func);
        existingByName[func.Name] = func;
      }
    }
    RdataEntries.AddRange(other.RdataEntries);
    foreach (var global in other.Globals) {
      if (!Globals.Any(g => g.Name == global.Name))
        Globals.Add(global);
    }
    foreach (var (k, v) in other.TypeDefs)
      TypeDefs[k] = v;
    foreach (var (k, v) in other.FunctionDefaults) FunctionDefaults.TryAdd(k, v);
    foreach (var (k, v) in other.TypeAliasSources) {
      if (TypeAliasSources.TryGetValue(k, out var existing)
          && (existing.IsExported || existing.IsStdlib)
          && (v.IsExported || v.IsStdlib)
          && existing.SourceFilePath != v.SourceFilePath)
        AmbiguousTypeNames.Add(k);
      TypeAliasSources.TryAdd(k, v);
    }
    foreach (var n in other.NonExportedTypeNames) NonExportedTypeNames.Add(n);
    foreach (var (k, v) in other.TypeDefSourceFiles) TypeDefSourceFiles.TryAdd(k, v);
    foreach (var n in other.AmbiguousTypeNames) AmbiguousTypeNames.Add(n);
    foreach (var (k, v) in other.ConstantArrayLiterals) ConstantArrayLiterals.TryAdd(k, v);
    foreach (var (k, v) in other.InterfaceAssociatedTypes) InterfaceAssociatedTypes.TryAdd(k, v);
    foreach (var init in other.DeferredGlobalInits) {
      if (!DeferredGlobalInits.Any(d => d.Name == init.Name))
        DeferredGlobalInits.Add(init);
    }
  }
}
