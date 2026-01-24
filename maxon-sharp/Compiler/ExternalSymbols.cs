namespace MaxonSharp.Compiler;

/// <summary>
/// Represents an exported type from another source file.
/// </summary>
public record ExternalTypeInfo(
	string Name,
	string SourcePath,
	List<ExternalFieldInfo> Fields
);

/// <summary>
/// Represents a field within an external type.
/// </summary>
public record ExternalFieldInfo(
	string Name,
	string TypeName
);

/// <summary>
/// Represents an exported function signature from another source file.
/// </summary>
public record ExternalFuncSignature(
	string Name,
	string SourcePath,
	List<ExternalParamInfo> Params,
	string? ReturnTypeName
);

/// <summary>
/// Represents a parameter in an external function signature.
/// </summary>
public record ExternalParamInfo(
	string Name,
	string TypeName
);

/// <summary>
/// Represents an exported enum from another source file.
/// </summary>
public record ExternalEnumInfo(
	string Name,
	string SourcePath,
	List<ExternalEnumVariant> Variants
);

/// <summary>
/// Represents a variant of an external enum.
/// </summary>
public record ExternalEnumVariant(
	string Name,
	List<ExternalFieldInfo> PayloadFields
);

/// <summary>
/// Collection of external symbols from other source files.
/// </summary>
public class ExternalSymbolsCollection {
	public List<ExternalTypeInfo> Types { get; } = [];
	public List<ExternalFuncSignature> Functions { get; } = [];
	public List<ExternalEnumInfo> Enums { get; } = [];

	/// <summary>
	/// Returns a new collection with symbols from the specified source path excluded.
	/// This is used to get external symbols for a file (excluding its own symbols).
	/// </summary>
	public ExternalSymbolsCollection ExcludingSource(string sourcePath) {
		var result = new ExternalSymbolsCollection();
		result.Types.AddRange(Types.Where(t => t.SourcePath != sourcePath));
		result.Functions.AddRange(Functions.Where(f => f.SourcePath != sourcePath));
		result.Enums.AddRange(Enums.Where(e => e.SourcePath != sourcePath));
		return result;
	}

	/// <summary>
	/// Merges another collection into this one.
	/// </summary>
	public void Merge(ExternalSymbolsCollection other) {
		Types.AddRange(other.Types);
		Functions.AddRange(other.Functions);
		Enums.AddRange(other.Enums);
	}

	/// <summary>
	/// Finds a type by name.
	/// </summary>
	public ExternalTypeInfo? FindType(string name) {
		return Types.FirstOrDefault(t => t.Name == name);
	}

	/// <summary>
	/// Finds a function by name.
	/// </summary>
	public ExternalFuncSignature? FindFunction(string name) {
		return Functions.FirstOrDefault(f => f.Name == name);
	}

	/// <summary>
	/// Finds an enum by name.
	/// </summary>
	public ExternalEnumInfo? FindEnum(string name) {
		return Enums.FirstOrDefault(e => e.Name == name);
	}
}
