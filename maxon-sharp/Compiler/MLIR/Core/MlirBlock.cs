namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// A basic block in MLIR. Contains a linear sequence of operations ending with a terminator.
/// Blocks can have arguments (replacing PHI nodes from traditional SSA).
/// </summary>
public sealed class MlirBlock(string? name = null) {
	[ThreadStatic]
	private static int _nextId;

	/// <summary>
	/// Unique name for this block (e.g., "entry", "bb0", "then").
	/// </summary>
	public string Name { get; set; } = name ?? $"bb{_nextId++}";

	/// <summary>
	/// Block arguments - values passed from predecessor blocks.
	/// </summary>
	public List<MlirBlockArgument> Arguments { get; } = [];

	/// <summary>
	/// Operations in this block, in order.
	/// </summary>
	public List<MlirOperation> Operations { get; } = [];

	/// <summary>
	/// The region containing this block.
	/// </summary>
	public MlirRegion? ParentRegion { get; internal set; }

	/// <summary>
	/// Resets the global ID counter. Used for testing.
	/// </summary>
	public static void ResetIdCounter() => _nextId = 0;

	/// <summary>
	/// Adds a block argument and returns its value.
	/// </summary>
	public MlirValue AddArgument(MlirType type) {
		var arg = new MlirBlockArgument(this, Arguments.Count, type);
		Arguments.Add(arg);
		return arg.Value;
	}

	/// <summary>
	/// Adds an operation to the end of this block.
	/// </summary>
	public void AddOperation(MlirOperation op) {
		op.ParentBlock = this;
		Operations.Add(op);
	}

	/// <summary>
	/// Adds an operation to the end of this block (alias for AddOperation).
	/// </summary>
	public void AddOp(MlirOperation op) => AddOperation(op);

	/// <summary>
	/// Inserts an operation at a specific index.
	/// </summary>
	public void InsertOperation(int index, MlirOperation op) {
		op.ParentBlock = this;
		Operations.Insert(index, op);
	}

	/// <summary>
	/// Removes an operation from this block.
	/// </summary>
	public bool RemoveOperation(MlirOperation op) {
		if (Operations.Remove(op)) {
			op.ParentBlock = null;
			return true;
		}
		return false;
	}

	/// <summary>
	/// Gets the terminator operation, or null if block is not properly terminated.
	/// </summary>
	public MlirOperation? Terminator =>
		Operations.Count > 0 && Operations[^1].IsTerminator ? Operations[^1] : null;

	/// <summary>
	/// Returns true if this block is properly terminated.
	/// </summary>
	public bool IsTerminated => Terminator is not null;

	/// <summary>
	/// Gets all predecessor blocks (blocks that branch to this one).
	/// </summary>
	public IEnumerable<MlirBlock> Predecessors {
		get {
			if (ParentRegion is null) yield break;
			foreach (var block in ParentRegion.Blocks) {
				if (block.Terminator?.Successors.Contains(this) == true)
					yield return block;
			}
		}
	}

	/// <summary>
	/// Gets all successor blocks (blocks this one can branch to).
	/// </summary>
	public IEnumerable<MlirBlock> Successors =>
		Terminator?.Successors ?? Enumerable.Empty<MlirBlock>();

	/// <summary>
	/// Verify this block is well-formed.
	/// </summary>
	public string? Verify() {
		if (Operations.Count == 0)
			return $"Block {Name} is empty";

		// Check only last operation is terminator
		for (int i = 0; i < Operations.Count - 1; i++) {
			if (Operations[i].IsTerminator)
				return $"Block {Name} has terminator {Operations[i].FullName} not at end";
		}

		if (!Operations[^1].IsTerminator)
			return $"Block {Name} does not end with a terminator";

		// Verify each operation
		foreach (var op in Operations) {
			var error = op.Verify();
			if (error is not null)
				return error;
		}

		return null;
	}

	/// <summary>
	/// Print this block in MLIR textual format.
	/// </summary>
	public void Print(MlirPrinter printer) {
		// Block label with arguments
		if (Arguments.Count > 0) {
			var args = Arguments.Select(a => $"{a.Value}: {a.Value.Type}");
			printer.PrintLine($"^{Name}({string.Join(", ", args)}):");
		} else {
			printer.PrintLine($"^{Name}:");
		}

		// Operations
		printer.Indent();
		foreach (var op in Operations) {
			op.Print(printer);
		}
		printer.Dedent();
	}

	public override string ToString() {
		var printer = new MlirPrinter();
		Print(printer);
		return printer.ToString();
	}
}
