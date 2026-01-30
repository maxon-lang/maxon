namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirGlobal(string name, MlirType type, MlirAttribute? initValue = null) {
	public string Name { get; } = name;
	public MlirType Type { get; } = type;
	public MlirAttribute? InitValue { get; } = initValue;
}

public class MlirModule {
	public List<MlirFunction> Functions { get; } = [];
	public List<(string label, byte[] bytes)> RdataEntries { get; } = [];
	public List<MlirGlobal> Globals { get; } = [];

	public void AddFunction(MlirFunction func) {
		Functions.Add(func);
	}

	public void Merge(MlirModule other) {
		Functions.AddRange(other.Functions);
		RdataEntries.AddRange(other.RdataEntries);
		Globals.AddRange(other.Globals);
	}
}
