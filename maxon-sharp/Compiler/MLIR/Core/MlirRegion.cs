namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// A region containing one or more blocks. Regions are used for nested control flow
/// (like function bodies, loop bodies, conditionals).
/// </summary>
public sealed class MlirRegion {
	/// <summary>
	/// Blocks in this region, in order. The first block is the entry block.
	/// </summary>
	public List<MlirBlock> Blocks { get; } = [];

	/// <summary>
	/// The operation containing this region.
	/// </summary>
	public MlirOperation? ParentOp { get; internal set; }

	/// <summary>
	/// Gets the entry block (first block) of this region.
	/// </summary>
	public MlirBlock? EntryBlock => Blocks.Count > 0 ? Blocks[0] : null;

	/// <summary>
	/// Adds a block to this region.
	/// </summary>
	public void AddBlock(MlirBlock block) {
		block.ParentRegion = this;
		Blocks.Add(block);
	}

	/// <summary>
	/// Creates and adds a new block with a unique name based on the given base name.
	/// </summary>
	public MlirBlock CreateBlock(string? baseName = null) {
		var uniqueName = GetUniqueName(baseName);
		var block = new MlirBlock(uniqueName);
		AddBlock(block);
		return block;
	}

	/// <summary>
	/// Generates a unique name by appending a suffix if needed.
	/// </summary>
	private string GetUniqueName(string? baseName) {
		if (baseName is null) return $"bb{Blocks.Count}";

		// Check if name already exists
		var existingNames = new HashSet<string>(Blocks.Select(b => b.Name));
		if (!existingNames.Contains(baseName)) return baseName;

		// Find unique suffix
		for (int i = 1; ; i++) {
			var candidate = $"{baseName}_{i}";
			if (!existingNames.Contains(candidate)) return candidate;
		}
	}

	/// <summary>
	/// Inserts a block at a specific index.
	/// </summary>
	public void InsertBlock(int index, MlirBlock block) {
		block.ParentRegion = this;
		Blocks.Insert(index, block);
	}

	/// <summary>
	/// Removes a block from this region.
	/// </summary>
	public bool RemoveBlock(MlirBlock block) {
		if (Blocks.Remove(block)) {
			block.ParentRegion = null;
			return true;
		}
		return false;
	}

	/// <summary>
	/// Returns true if this region is empty.
	/// </summary>
	public bool IsEmpty => Blocks.Count == 0;

	/// <summary>
	/// Returns true if this region has a single block.
	/// </summary>
	public bool HasSingleBlock => Blocks.Count == 1;

	/// <summary>
	/// Gets all operations in all blocks.
	/// </summary>
	public IEnumerable<MlirOperation> AllOperations =>
		Blocks.SelectMany(b => b.Operations);

	/// <summary>
	/// Verify this region is well-formed.
	/// </summary>
	public string? Verify() {
		if (Blocks.Count == 0)
			return "Region is empty";

		foreach (var block in Blocks) {
			var error = block.Verify();
			if (error is not null)
				return error;
		}

		return null;
	}

	/// <summary>
	/// Print this region in MLIR textual format.
	/// </summary>
	public void Print(MlirPrinter printer) {
		printer.PrintLine("{");
		printer.Indent();
		foreach (var block in Blocks) {
			block.Print(printer);
		}
		printer.Dedent();
		printer.PrintLine("}");
	}

	public override string ToString() {
		var printer = new MlirPrinter();
		Print(printer);
		return printer.ToString();
	}
}
