using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Manages and runs a pipeline of passes on MLIR modules.
/// </summary>
public sealed class PassManager(MlirContext context) {
	private readonly List<IPass> _passes = [];
	private readonly MlirContext _context = context;

	/// <summary>
	/// Whether to print the IR after each pass.
	/// </summary>
	public bool PrintAfterAll { get; set; }

	/// <summary>
	/// Whether to verify the IR after each pass.
	/// </summary>
	public bool VerifyAfterAll { get; set; } = true;

	/// <summary>
	/// Statistics collected during pass execution.
	/// </summary>
	public PassStatistics Statistics { get; } = new();

	/// <summary>
	/// Adds a pass to the pipeline.
	/// </summary>
	public PassManager AddPass<T>() where T : IPass, new() {
		_passes.Add(new T());
		return this;
	}

	/// <summary>
	/// Adds a pass instance to the pipeline.
	/// </summary>
	public PassManager AddPass(IPass pass) {
		_passes.Add(pass);
		return this;
	}

	/// <summary>
	/// Runs all passes in the pipeline on the module.
	/// </summary>
	public PassResult Run(MlirModule module) {
		var result = new PassResult();
		var startTime = DateTime.Now;

		foreach (var pass in _passes) {
			var passStart = DateTime.Now;

			try {
				var changed = pass.Run(module);
				var passTime = DateTime.Now - passStart;

				Statistics.RecordPassRun(pass.Name, passTime, changed);

				if (changed) {
					result.ModifiedPasses.Add(pass.Name);
				}

				if (PrintAfterAll) {
					PrintModule(pass.Name, module);
				}

				if (VerifyAfterAll) {
					var errors = VerifyModule(module);
					if (errors.Count > 0) {
						result.Success = false;
						result.Errors.AddRange(errors.Select(e => $"[{pass.Name}] {e}"));
						return result;
					}
				}
			} catch (Exception ex) {
				result.Success = false;
				result.Errors.Add($"[{pass.Name}] {ex.Message}");
				return result;
			}
		}

		result.TotalTime = DateTime.Now - startTime;
		result.Success = true;
		return result;
	}

	/// <summary>
	/// Creates a standard optimization pipeline.
	/// </summary>
	public static PassManager CreateOptimizationPipeline(MlirContext context) {
		return new PassManager(context)
			.AddPass<ConstantFoldingPass>()
			.AddPass<DeadCodeEliminationPass>()
			.AddPass<CommonSubexpressionEliminationPass>();
	}

	/// <summary>
	/// Creates the borrow checking pipeline.
	/// </summary>
	public static PassManager CreateBorrowCheckPipeline(MlirContext context) {
		return new PassManager(context)
			.AddPass<MaxonBorrowChecker>();
	}

	private static void PrintModule(string passName, MlirModule module) {
		Console.WriteLine($"// ===== After {passName} =====");
		var printer = new MlirPrinter();
		module.Print(printer);
		Console.WriteLine(printer.ToString());
	}

	private static List<string> VerifyModule(MlirModule module) {
		var errors = new List<string>();

		// Basic verification: check all values have types
		foreach (var func in module.Functions) {
			VerifyFunction(func, errors);
		}

		return errors;
	}

	private static void VerifyFunction(MlirFunction func, List<string> errors) {
		foreach (var block in func.Body.Blocks) {
			VerifyBlock(block, errors);
		}
	}

	private static void VerifyBlock(MlirBlock block, List<string> errors) {
		foreach (var op in block.Operations) {
			// Check results have types
			foreach (var result in op.Results) {
				if (result.Type is null) {
					errors.Add($"Result {result.Id} of {op.Mnemonic} has no type");
				}
			}

			// Check terminators are at the end
			if (op.IsTerminator && op != block.Operations.Last()) {
				errors.Add($"Terminator {op.Mnemonic} is not at end of block");
			}

			// Recursively check nested regions
			foreach (var region in op.Regions) {
				foreach (var nestedBlock in region.Blocks) {
					VerifyBlock(nestedBlock, errors);
				}
			}
		}

		// Check block has terminator
		if (block.Operations.Count > 0 && !block.Terminator?.IsTerminator == true) {
			errors.Add($"Block {block.Name} has no terminator");
		}
	}
}

/// <summary>
/// Result of running a pass pipeline.
/// </summary>
public sealed class PassResult {
	public bool Success { get; set; }
	public List<string> Errors { get; } = [];
	public List<string> ModifiedPasses { get; } = [];
	public TimeSpan TotalTime { get; set; }
}

/// <summary>
/// Statistics about pass execution.
/// </summary>
public sealed class PassStatistics {
	private readonly Dictionary<string, PassStats> _stats = [];

	public void RecordPassRun(string passName, TimeSpan time, bool changed) {
		if (!_stats.TryGetValue(passName, out var stats)) {
			stats = new PassStats { Name = passName };
			_stats[passName] = stats;
		}

		stats.RunCount++;
		stats.TotalTime += time;
		if (changed) stats.ModificationCount++;
	}

	public void PrintReport() {
		Console.WriteLine("Pass Statistics:");
		Console.WriteLine("================");
		foreach (var stats in _stats.Values.OrderByDescending(s => s.TotalTime)) {
			Console.WriteLine($"  {stats.Name}:");
			Console.WriteLine($"    Runs: {stats.RunCount}");
			Console.WriteLine($"    Modifications: {stats.ModificationCount}");
			Console.WriteLine($"    Total time: {stats.TotalTime.TotalMilliseconds:F2}ms");
		}
	}

	private sealed class PassStats {
		public required string Name { get; init; }
		public int RunCount { get; set; }
		public int ModificationCount { get; set; }
		public TimeSpan TotalTime { get; set; }
	}
}
