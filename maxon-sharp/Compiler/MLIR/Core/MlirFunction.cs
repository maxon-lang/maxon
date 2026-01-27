namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// A function in MLIR. This is a convenience wrapper around func.func operation.
/// </summary>
public sealed class MlirFunction(string name) {
	/// <summary>
	/// Function name.
	/// </summary>
	public string Name { get; set; } = name;

	/// <summary>
	/// Whether this function is exported (visible externally).
	/// </summary>
	public bool IsExport { get; set; }

	/// <summary>
	/// Whether this function is a declaration (no body).
	/// </summary>
	public bool IsDeclaration => Body.IsEmpty;

	/// <summary>
	/// Parameter types.
	/// </summary>
	public List<MlirType> ParamTypes { get; } = [];

	/// <summary>
	/// Parameter names (for debugging).
	/// </summary>
	public List<string> ParamNames { get; } = [];

	/// <summary>
	/// Return types. Empty for void functions.
	/// </summary>
	public List<MlirType> ResultTypes { get; } = [];

	/// <summary>
	/// The function body region.
	/// </summary>
	public MlirRegion Body { get; } = new();

	/// <summary>
	/// Custom attributes on this function.
	/// </summary>
	public Dictionary<string, MlirAttribute> Attributes { get; } = [];

	/// <summary>
	/// Arbitrary metadata for passes to communicate information.
	/// </summary>
	public Dictionary<string, object> Metadata { get; } = [];

	/// <summary>
	/// Source location for error reporting.
	/// </summary>
	public SourceLocation? Location { get; set; }

	/// <summary>
	/// Sets metadata on this function.
	/// </summary>
	public void SetMetadata<T>(string key, T value) where T : notnull {
		Metadata[key] = value;
	}

	/// <summary>
	/// Gets metadata from this function.
	/// </summary>
	public T? GetMetadata<T>(string key) where T : class {
		return Metadata.TryGetValue(key, out var value) ? value as T : null;
	}

	/// <summary>
	/// Gets metadata from this function (value type version).
	/// </summary>
	public T GetMetadataValue<T>(string key) where T : struct {
		return Metadata.TryGetValue(key, out var value) && value is T typedValue ? typedValue : default;
	}

	/// <summary>
	/// Adds a parameter and returns its type.
	/// </summary>
	public void AddParam(string name, MlirType type) {
		ParamNames.Add(name);
		ParamTypes.Add(type);
	}

	/// <summary>
	/// Gets the entry block, or null if this is a declaration.
	/// </summary>
	public MlirBlock? EntryBlock => Body.EntryBlock;

	/// <summary>
	/// Creates the entry block with parameter values.
	/// </summary>
	public MlirBlock CreateEntryBlock() {
		var block = Body.CreateBlock("entry");
		for (int i = 0; i < ParamTypes.Count; i++) {
			var argValue = block.AddArgument(ParamTypes[i]);
			if (i < ParamNames.Count)
				argValue.Name = ParamNames[i];
		}
		return block;
	}

	/// <summary>
	/// Gets the parameter values from the entry block.
	/// </summary>
	public IReadOnlyList<MlirValue> GetParamValues() {
		if (Body.EntryBlock is null)
			return [];
		return [.. Body.EntryBlock.Arguments.Select(a => a.Value)];
	}

	/// <summary>
	/// Gets the function parameters as MlirValues (from entry block arguments).
	/// </summary>
	public IReadOnlyList<MlirValue> Parameters => GetParamValues();

	/// <summary>
	/// Renumbers all SSA values in this function to be sequential.
	/// Values are assigned IDs in the order they appear: block arguments first, then operation results.
	/// </summary>
	public void RenumberValues() {
		foreach (var block in Body.Blocks) {
			// Renumber block arguments
			foreach (var arg in block.Arguments) {
				arg.Value.RenumberId();
			}

			// Renumber operation results
			foreach (var op in block.Operations) {
				foreach (var result in op.Results) {
					result.RenumberId();
				}
			}
		}
	}

	/// <summary>
	/// Verify this function is well-formed.
	/// </summary>
	public string? Verify() {
		if (string.IsNullOrEmpty(Name))
			return "Function has no name";

		if (!IsDeclaration) {
			var error = Body.Verify();
			if (error is not null)
				return $"In function {Name}: {error}";

			// Verify entry block arguments match parameters
			if (Body.EntryBlock is not null) {
				if (Body.EntryBlock.Arguments.Count != ParamTypes.Count)
					return $"Function {Name}: entry block has {Body.EntryBlock.Arguments.Count} arguments but function has {ParamTypes.Count} parameters";
			}
		}

		return null;
	}

	/// <summary>
	/// Print this function in MLIR textual format.
	/// </summary>
	public void Print(MlirPrinter printer) {
		// func.func @name(args) -> results { body }
		var visibility = IsExport ? "public " : "";
		var paramStrs = new List<string>();
		for (int i = 0; i < ParamTypes.Count; i++) {
			var name = i < ParamNames.Count ? ParamNames[i] : $"arg{i}";
			paramStrs.Add($"%{name}: {ParamTypes[i]}");
		}

		var resultStr = ResultTypes.Count switch {
			0 => "",
			1 => $" -> {ResultTypes[0]}",
			_ => $" -> ({string.Join(", ", ResultTypes)})"
		};

		printer.Print($"{visibility}func.func @{Name}({string.Join(", ", paramStrs)}){resultStr}");

		if (Attributes.Count > 0) {
			var attrStrs = Attributes.Select(kv => $"{kv.Key} = {kv.Value}");
			printer.Print($" attributes {{{string.Join(", ", attrStrs)}}}");
		}

		if (IsDeclaration) {
			printer.PrintLine();
		} else {
			printer.PrintLine(" {");
			printer.Indent();
			foreach (var block in Body.Blocks) {
				block.Print(printer);
			}
			printer.Dedent();
			printer.PrintLine("}");
		}
	}

	public override string ToString() {
		var printer = new MlirPrinter();
		Print(printer);
		return printer.ToString();
	}
}
