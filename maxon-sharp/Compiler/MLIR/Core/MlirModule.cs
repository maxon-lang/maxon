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

	public void AddFunction(MlirFunction<TOp> func) {
		Functions.Add(func);
	}

	public void Merge(MlirModule<TOp> other) {
		Functions.AddRange(other.Functions);
		RdataEntries.AddRange(other.RdataEntries);
		Globals.AddRange(other.Globals);
	}
}
