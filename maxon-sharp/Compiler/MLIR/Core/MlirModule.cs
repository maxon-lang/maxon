namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// Top-level container for MLIR. Contains functions, globals, and type definitions.
/// </summary>
public sealed class MlirModule {
	/// <summary>
	/// Optional module name.
	/// </summary>
	public string? Name { get; set; }

	/// <summary>
	/// Functions defined in this module.
	/// </summary>
	public List<MlirFunction> Functions { get; } = [];

	/// <summary>
	/// Global variables.
	/// </summary>
	public List<MlirGlobal> Globals { get; } = [];

	/// <summary>
	/// Struct type definitions.
	/// </summary>
	public List<MlirStructDef> StructDefs { get; } = [];

	/// <summary>
	/// Enum type definitions.
	/// </summary>
	public List<MlirEnumDef> EnumDefs { get; } = [];

	/// <summary>
	/// String literals (for data section).
	/// </summary>
	public List<MlirStringData> Strings { get; } = [];

	/// <summary>
	/// Read-only data entries (for rdata section).
	/// Populated during StandardToX86 lowering when ConstDataOp is converted.
	/// </summary>
	public Dictionary<string, byte[]> RdataEntries { get; } = [];

	/// <summary>
	/// Adds a read-only data entry.
	/// </summary>
	public void AddRdataEntry(string label, byte[] data) {
		RdataEntries.TryAdd(label, data);
	}

	/// <summary>
	/// Module-level attributes.
	/// </summary>
	public Dictionary<string, MlirAttribute> Attributes { get; } = [];

	/// <summary>
	/// Gets a function by name.
	/// </summary>
	public MlirFunction? GetFunction(string name) =>
		Functions.Find(f => f.Name == name);

	/// <summary>
	/// Gets a global by name.
	/// </summary>
	public MlirGlobal? GetGlobal(string name) =>
		Globals.Find(g => g.Name == name);

	/// <summary>
	/// Gets a struct definition by name.
	/// </summary>
	public MlirStructDef? GetStructDef(string name) =>
		StructDefs.Find(s => s.Name == name);

	/// <summary>
	/// Gets an enum definition by name.
	/// </summary>
	public MlirEnumDef? GetEnumDef(string name) =>
		EnumDefs.Find(e => e.Name == name);

	/// <summary>
	/// Adds a string literal and returns its ID.
	/// </summary>
	public int AddString(string value) {
		var existing = Strings.FindIndex(s => s.Value == value);
		if (existing >= 0) return existing;
		var id = Strings.Count;
		Strings.Add(new MlirStringData(id, value));
		return id;
	}

	/// <summary>
	/// Renumbers all SSA values in the module to be sequential starting from 0.
	/// Call this after dead function elimination to get clean IDs.
	/// </summary>
	public void RenumberValues() {
		MlirValue.ResetIdCounter();
		MlirBlock.ResetIdCounter();

		foreach (var func in Functions) {
			func.RenumberValues();
		}
	}

	/// <summary>
	/// Verify this module is well-formed.
	/// </summary>
	public string? Verify() {
		foreach (var func in Functions) {
			var error = func.Verify();
			if (error is not null)
				return error;
		}
		return null;
	}

	/// <summary>
	/// Print this module in MLIR textual format.
	/// </summary>
	public void Print(MlirPrinter printer) {
		for (int i = 0; i < Functions.Count; i++) {
			if (i > 0) printer.PrintLine();
			Functions[i].Print(printer);
		}
	}

	public override string ToString() {
		var printer = new MlirPrinter();
		Print(printer);
		return printer.ToString();
	}
}

/// <summary>
/// A global variable or constant.
/// </summary>
public sealed class MlirGlobal(string name, MlirType type) {
	public string Name { get; set; } = name;
	public MlirType Type { get; set; } = type;
	public bool IsConstant { get; set; }
	public MlirAttribute? InitValue { get; set; }

	public void Print(MlirPrinter printer) {
		var constStr = IsConstant ? "constant" : "global";
		var initStr = InitValue is not null ? $" = {InitValue}" : "";
		printer.PrintLine($"memref.{constStr} @{Name} : {Type}{initStr}");
	}
}

/// <summary>
/// String data for the data section.
/// </summary>
public record MlirStringData(int Id, string Value) {
	public override string ToString() => $"str{Id} = \"{Value}\"";
}

/// <summary>
/// Struct type definition.
/// </summary>
public sealed class MlirStructDef(string name) {
	public string Name { get; set; } = name;
	public List<MlirFieldDef> Fields { get; } = [];
	public int SizeInBytes => Fields.Sum(f => f.Type.SizeInBytes);
}

/// <summary>
/// Field definition within a struct.
/// </summary>
/// <param name="Name">Field name</param>
/// <param name="Type">Field type</param>
/// <param name="Offset">Byte offset within struct</param>
/// <param name="IsMutable">True if declared with 'var', false if 'let'</param>
public record MlirFieldDef(string Name, MlirType Type, int Offset, bool IsMutable = false);

/// <summary>
/// Enum type definition.
/// </summary>
public sealed class MlirEnumDef(string name) {
	public string Name { get; set; } = name;
	public int TagSize { get; set; } = 1;
	public int MaxPayloadSize { get; set; }
	public List<MlirVariantDef> Variants { get; } = [];

	public int SizeInBytes => TagSize + MaxPayloadSize;

	public void Print(MlirPrinter printer) {
		printer.PrintLine($"// enum {Name} {{");
		foreach (var variant in Variants) {
			var payloadStr = variant.PayloadFields.Count > 0
				? $"({string.Join(", ", variant.PayloadFields.Select(f => f.Type))})"
				: "";
			printer.PrintLine($"//   {variant.Name}{payloadStr} = {variant.Tag}");
		}
		printer.PrintLine($"// }}");
	}
}

/// <summary>
/// Variant definition within an enum.
/// </summary>
public sealed class MlirVariantDef(string name, int tag) {
	public string Name { get; set; } = name;
	public int Tag { get; set; } = tag;
	public List<MlirFieldDef> PayloadFields { get; } = [];
}
