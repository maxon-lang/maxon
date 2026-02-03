namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirGlobal(string name, MlirType type, MlirAttribute? initValue = null) {
  public string Name { get; } = name;
  public MlirType Type { get; } = type;
  public MlirAttribute? InitValue { get; } = initValue;
}

// Represents a type alias with its source type and type parameter substitutions
public record TypeAliasInfo(string SourceTypeName, Dictionary<string, MlirType>? TypeParams);

// Metadata for constant array literals that can be placed in .rdata
public record ConstantArrayLiteralInfo(string RdataLabel, long[] Values, bool IsMutable);

public class MlirModule<TOp> where TOp : IPrintableOp {
  public List<MlirFunction<TOp>> Functions { get; } = [];
  public List<(string label, byte[] bytes, int alignment)> RdataEntries { get; } = [];
  public List<MlirGlobal> Globals { get; } = [];
  public Dictionary<string, MlirType> TypeDefs { get; } = [];
  public Dictionary<string, Dictionary<int, MlirAttribute>> FunctionDefaults { get; } = [];
  public Dictionary<string, HashSet<int>> ElementPolymorphicParams { get; } = [];

  // Type alias tracking: aliasName -> TypeAliasInfo (sourceTypeName + typeParams)
  public Dictionary<string, TypeAliasInfo> TypeAliasSources { get; } = [];

  // Constant array literal metadata: struct result ID -> ConstantArrayLiteralInfo
  // Populated by ConstantArrayAnalysisPass, consumed by MaxonToStandardConversion
  public Dictionary<int, ConstantArrayLiteralInfo> ConstantArrayLiterals { get; } = [];

  // Interface associated type names (interfaceName -> list of 'uses' type names)
  public Dictionary<string, List<string>> InterfaceAssociatedTypes { get; } = [];

  public void AddFunction(MlirFunction<TOp> func) {
    Functions.Add(func);
  }

  public void Merge(MlirModule<TOp> other) {
    var existingNames = new HashSet<string>(Functions.Select(f => f.Name));
    foreach (var func in other.Functions) {
      if (existingNames.Add(func.Name))
        Functions.Add(func);
    }
    RdataEntries.AddRange(other.RdataEntries);
    Globals.AddRange(other.Globals);
    foreach (var (k, v) in other.TypeDefs) TypeDefs[k] = v;
    foreach (var (k, v) in other.FunctionDefaults) FunctionDefaults.TryAdd(k, v);
    foreach (var (k, v) in other.ElementPolymorphicParams) ElementPolymorphicParams.TryAdd(k, v);
    foreach (var (k, v) in other.TypeAliasSources) TypeAliasSources.TryAdd(k, v);
    foreach (var (k, v) in other.ConstantArrayLiterals) ConstantArrayLiterals.TryAdd(k, v);
    foreach (var (k, v) in other.InterfaceAssociatedTypes) InterfaceAssociatedTypes.TryAdd(k, v);
  }
}
