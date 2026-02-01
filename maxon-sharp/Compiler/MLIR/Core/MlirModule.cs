namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirGlobal(string name, MlirType type, MlirAttribute? initValue = null) {
  public string Name { get; } = name;
  public MlirType Type { get; } = type;
  public MlirAttribute? InitValue { get; } = initValue;
}

public class MlirModule<TOp> where TOp : IPrintableOp {
  public List<MlirFunction<TOp>> Functions { get; } = [];
  public List<(string label, byte[] bytes, int alignment)> RdataEntries { get; } = [];
  public List<MlirGlobal> Globals { get; } = [];
  public Dictionary<string, MlirType> TypeDefs { get; } = [];
  public Dictionary<string, Dictionary<int, MlirAttribute>> FunctionDefaults { get; } = [];

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
  }
}
