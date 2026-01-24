namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// Base class for all MLIR operations. Operations are the basic unit of execution.
/// </summary>
public abstract class MlirOperation {
	/// <summary>
	/// The dialect that defines this operation.
	/// </summary>
	public abstract string Dialect { get; }

	/// <summary>
	/// The operation name within the dialect (e.g., "addi" for arith.addi).
	/// </summary>
	public abstract string Mnemonic { get; }

	/// <summary>
	/// Full operation name (dialect.mnemonic).
	/// </summary>
	public string FullName => $"{Dialect}.{Mnemonic}";

	/// <summary>
	/// The operands (inputs) to this operation.
	/// </summary>
	public List<MlirValue> Operands { get; } = [];

	/// <summary>
	/// The results (outputs) produced by this operation.
	/// </summary>
	public List<MlirValue> Results { get; } = [];

	/// <summary>
	/// Named attributes attached to this operation.
	/// </summary>
	public Dictionary<string, MlirAttribute> Attributes { get; } = [];

	/// <summary>
	/// Regions contained within this operation (for ops like func.func, cf.if).
	/// </summary>
	public List<MlirRegion> Regions { get; } = [];

	/// <summary>
	/// Block successors for terminator operations.
	/// </summary>
	public List<MlirBlock> Successors { get; } = [];

	/// <summary>
	/// The block containing this operation.
	/// </summary>
	public MlirBlock? ParentBlock { get; internal set; }

	/// <summary>
	/// Source location for error reporting.
	/// </summary>
	public SourceLocation? Location { get; set; }

	/// <summary>
	/// Returns true if this operation is a terminator (ends a block).
	/// </summary>
	public virtual bool IsTerminator => false;

	/// <summary>
	/// Returns true if this operation has side effects.
	/// </summary>
	public virtual bool HasSideEffects => true;

	/// <summary>
	/// Returns true if this operation is a constant.
	/// </summary>
	public virtual bool IsConstant => false;

	/// <summary>
	/// Creates a result value and adds it to this operation's results.
	/// </summary>
	protected MlirValue CreateResult(MlirType type) {
		var value = new MlirValue(type) { DefiningOp = this };
		Results.Add(value);
		return value;
	}

	/// <summary>
	/// Gets the single result, or throws if not exactly one result.
	/// </summary>
	public MlirValue GetSingleResult() {
		if (Results.Count != 1)
			throw new InvalidOperationException($"Operation {FullName} has {Results.Count} results, expected 1");
		return Results[0];
	}

	/// <summary>
	/// Gets a typed attribute, or throws if not found or wrong type.
	/// </summary>
	public T GetAttribute<T>(string name) where T : MlirAttribute {
		if (!Attributes.TryGetValue(name, out var attr))
			throw new KeyNotFoundException($"Attribute '{name}' not found on {FullName}");
		if (attr is not T typed)
			throw new InvalidCastException($"Attribute '{name}' is {attr.GetType().Name}, expected {typeof(T).Name}");
		return typed;
	}

	/// <summary>
	/// Gets a typed attribute, or default if not found.
	/// </summary>
	public T? GetAttributeOrDefault<T>(string name) where T : MlirAttribute {
		return Attributes.TryGetValue(name, out var attr) && attr is T typed ? typed : null;
	}

	/// <summary>
	/// Verify this operation is well-formed. Returns null if valid, error message otherwise.
	/// </summary>
	public virtual string? Verify() => null;

	/// <summary>
	/// Print this operation in MLIR textual format.
	/// </summary>
	public virtual void Print(MlirPrinter printer) {
		// Results
		if (Results.Count == 1) {
			printer.Print($"{Results[0]} = ");
		} else if (Results.Count > 1) {
			printer.Print($"({string.Join(", ", Results)}) = ");
		}

		// Operation name
		printer.Print(FullName);

		// Operands
		if (Operands.Count > 0) {
			printer.Print($" {string.Join(", ", Operands)}");
		}

		// Successors
		if (Successors.Count > 0) {
			printer.Print($" [{string.Join(", ", Successors.Select(s => $"^{s.Name}"))}]");
		}

		// Attributes
		if (Attributes.Count > 0) {
			var attrStrs = Attributes.Select(kv => $"{kv.Key} = {kv.Value}");
			printer.Print($" {{{string.Join(", ", attrStrs)}}}");
		}

		// Result types
		if (Results.Count > 0) {
			var types = Results.Select(r => r.Type.ToString());
			printer.Print($" : {string.Join(", ", types)}");
		}

		printer.PrintLine();

		// Regions (indented)
		foreach (var region in Regions) {
			region.Print(printer);
		}
	}

	public override string ToString() {
		var printer = new MlirPrinter();
		Print(printer);
		return printer.ToString().TrimEnd();
	}
}

/// <summary>
/// Source location for debugging and error reporting.
/// </summary>
public record SourceLocation(string File, int Line, int Column) {
	public override string ToString() => $"{File}:{Line}:{Column}";
}
