using System.Diagnostics;
using System.Text;
using MaxonSharp.Compiler;

namespace MaxonSharp.Testing;

/// <summary>
/// Unit tests for compiler internals that can't be tested via spec files.
/// These tests verify IR generation patterns and runtime behavior.
///
/// NOTE: Many tests document expected future behavior. Tests that depend on
/// unimplemented features will fail until those features are added.
/// </summary>
public static class UnitTests {
	private const int TestTimeoutMs = 5000;

	// IR patterns in final X86 IR (after lowering)
	// memref.heap_free -> HeapFree call
	// memref.heap_realloc -> HeapReAlloc call
	private const string IrHeapFree = "HeapFree";
	private const string IrHeapRealloc = "HeapReAlloc";

	/// <summary>
	/// Run all unit tests and return summary.
	/// </summary>
	public static UnitTestSummary RunAll(string? filter = null) {
		var results = new List<UnitTestResult>();
		var sw = Stopwatch.StartNew();

		// Tests are grouped by feature area.
		// Tests in each group may depend on features not yet implemented.
		var tests = new List<(string Name, Func<UnitTestResult> Run)> {
			// Stack probing: Verifies __chkstk is called for large stack frames
			("stack-probing-large-struct-recursive", StackProbingLargeStructRecursive),

			// Managed memory: Verifies heap operations in IR
			("managed-memory-heap-array-generates-free", ManagedMemoryHeapArrayGeneratesFree),
			("managed-memory-scope-cleanup-generates-free", ManagedMemoryScopeCleanupGeneratesFree),
			("managed-memory-loop-growth-generates-realloc", ManagedMemoryLoopGrowthGeneratesRealloc),
			("managed-memory-fixed-size-array-literal-cleanup", ManagedMemoryFixedSizeArrayLiteralCleanup),

			// Managed strings: Disabled - strings not yet implemented
			// ("managed-string-heap-string-generates-cleanup", ManagedStringHeapStringGeneratesCleanup),
			// ("managed-string-reassignment-handles-old-value", ManagedStringReassignmentHandlesOldValue),
			// ("managed-string-loop-concatenation-cleanup", ManagedStringLoopConcatenationCleanup),
		};

		foreach (var (name, run) in tests) {
			if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			try {
				var result = run();
				results.Add(result);

				if (result.Passed) {
					Logger.Debug(LogCategory.Testing, $"[PASS] {result.Name}");
				} else {
					Logger.Error(LogCategory.Testing, $"[FAIL] {result.Name}");
					if (result.ErrorMessage != null) {
						Logger.Error(LogCategory.Testing, $"  {result.ErrorMessage}");
					}
				}
			} catch (Exception ex) {
				results.Add(new UnitTestResult {
					Name = name,
					Passed = false,
					ErrorMessage = $"Exception: {ex.Message}"
				});
				Logger.Error(LogCategory.Testing, $"[FAIL] {name}");
				Logger.Error(LogCategory.Testing, $"  Exception: {ex.Message}");
			}
		}

		sw.Stop();

		return new UnitTestSummary {
			Results = results,
			Passed = results.Count(r => r.Passed),
			Failed = results.Count(r => !r.Passed),
			Total = results.Count,
			TotalDuration = sw.Elapsed
		};
	}

	// ============================================================================
	// Helper Methods
	// ============================================================================

	private static (bool Success, string? Error, string? Ir) CompileWithIr(string source, string tempExePath) {
		var sources = new SourceFile[] { new("test.maxon", source) };
		var result = Compiler.Compiler.Compile(sources, tempExePath, returnIr: true);
		return (result.Success, result.Error, result.X86Ir);
	}

	private static (int ExitCode, string Stdout, string Stderr)? RunExecutable(string exePath) {
		var psi = new ProcessStartInfo {
			FileName = exePath,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		using var job = new WindowsJobObject();
		using var process = Process.Start(psi);
		if (process == null) return null;

		job.AssignProcess(process.Handle);

		var stdoutTask = process.StandardOutput.ReadToEndAsync();
		var stderrTask = process.StandardError.ReadToEndAsync();

		bool exited = process.WaitForExit(TestTimeoutMs);
		if (!exited) {
			try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
			return null;
		}

		var stdout = stdoutTask.GetAwaiter().GetResult();
		var stderr = stderrTask.GetAwaiter().GetResult();

		return (process.ExitCode, stdout, stderr);
	}

	private static int CountOccurrences(string text, string pattern) {
		int count = 0;
		int pos = 0;
		while ((pos = text.IndexOf(pattern, pos, StringComparison.Ordinal)) != -1) {
			count++;
			pos += pattern.Length;
		}
		return count;
	}

	private static string GetTempExePath(string testName) {
		var tempDir = Path.Combine(Path.GetTempPath(), "maxon-tests");
		Directory.CreateDirectory(tempDir);
		return Path.Combine(tempDir, $"{testName}_{Environment.CurrentManagedThreadId}.exe");
	}

	private static void CleanupTempFile(string path) {
		try { File.Delete(path); } catch { /* ignore */ }
	}

	// ============================================================================
	// Stack Probing Tests
	// ============================================================================

	/// <summary>
	/// Test that large struct allocation with recursion works correctly.
	/// On Windows x64, functions with >4KB stack allocation need __chkstk.
	/// Uses multiple medium-sized structs to get >4KB total without slow compilation.
	/// </summary>
	private static UnitTestResult StackProbingLargeStructRecursive() {
		const string testName = "stack-probing-large-struct-recursive";

		// Generate a struct with 64 int fields (512 bytes)
		// Then use 10 of them in a function to get 5120 bytes (> 4KB)
		var sb = new StringBuilder();
		sb.AppendLine("type BigStruct");
		for (int i = 0; i < 64; i++) {
			sb.AppendLine($"    export var f{i} int");
		}
		sb.AppendLine("end 'BigStruct'");
		sb.AppendLine();
		sb.AppendLine("""
function recurse(n int) returns int
    var s1 = BigStruct { f0: n }
    var s2 = BigStruct { f0: n }
    var s3 = BigStruct { f0: n }
    var s4 = BigStruct { f0: n }
    var s5 = BigStruct { f0: n }
    var s6 = BigStruct { f0: n }
    var s7 = BigStruct { f0: n }
    var s8 = BigStruct { f0: n }
    var s9 = BigStruct { f0: n }
    var s10 = BigStruct { f0: n }
    if n == 0 'base'
        return s10.f63
    end 'base'
    return recurse(n - 1)
end 'recurse'

function main() returns int
    return recurse(50)
end 'main'
""");

		var tempExe = GetTempExePath(testName);
		try {
			var (success, error, ir) = CompileWithIr(sb.ToString(), tempExe);
			if (!success) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Compilation failed: {error}"
				};
			}

			var runResult = RunExecutable(tempExe);
			if (runResult == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "Failed to run executable (timeout or error)"
				};
			}

			// If stack probing is broken, this would crash before returning
			if (runResult.Value.ExitCode != 0) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Expected exit code 0, got {runResult.Value.ExitCode}"
				};
			}

			return new UnitTestResult { Name = testName, Passed = true };
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	// ============================================================================
	// Managed Memory Tests
	// ============================================================================

	/// <summary>
	/// Test that heap arrays generate heap_free for cleanup.
	/// </summary>
	private static UnitTestResult ManagedMemoryHeapArrayGeneratesFree() {
		const string testName = "managed-memory-heap-array-generates-free";
		const string source = """
typealias IntArray is Array with int

function main() returns int
    var arr = IntArray{}
    arr.push(1)
    arr.push(2)
    return arr.count()
end 'main'
""";

		var tempExe = GetTempExePath(testName);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Compilation failed: {error}"
				};
			}

			// Verify IR contains heap_free for array cleanup
			if (ir == null || !ir.Contains(IrHeapFree)) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"IR does not contain {IrHeapFree} instruction"
				};
			}

			var runResult = RunExecutable(tempExe);
			if (runResult == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "Failed to run executable"
				};
			}

			if (runResult.Value.ExitCode != 2) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Expected exit code 2, got {runResult.Value.ExitCode}"
				};
			}

			return new UnitTestResult { Name = testName, Passed = true };
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that nested scopes generate multiple heap_free calls.
	/// </summary>
	private static UnitTestResult ManagedMemoryScopeCleanupGeneratesFree() {
		const string testName = "managed-memory-scope-cleanup-generates-free";
		const string source = """
typealias IntArray is Array with int

function main() returns int
    if true 'outer'
        var outer_arr = IntArray{}
        outer_arr.push(100)
        if true 'inner'
            var inner_arr = IntArray{}
            inner_arr.push(200)
        end 'inner'
    end 'outer'
    return 0
end 'main'
""";

		var tempExe = GetTempExePath(testName);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Compilation failed: {error}"
				};
			}

			// Verify IR contains at least 2 heap_free calls (inner and outer scope)
			if (ir == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "No IR generated"
				};
			}

			int freeCount = CountOccurrences(ir, IrHeapFree);
			if (freeCount < 2) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Expected at least 2 {IrHeapFree} instructions, found {freeCount}"
				};
			}

			var runResult = RunExecutable(tempExe);
			if (runResult == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "Failed to run executable"
				};
			}

			if (runResult.Value.ExitCode != 0) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Expected exit code 0, got {runResult.Value.ExitCode}"
				};
			}

			return new UnitTestResult { Name = testName, Passed = true };
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that array growth in a loop generates heap_realloc.
	/// </summary>
	private static UnitTestResult ManagedMemoryLoopGrowthGeneratesRealloc() {
		const string testName = "managed-memory-loop-growth-generates-realloc";
		const string source = """
typealias IntArray is Array with int

function main() returns int
    var arr = IntArray{}
    var i = 0
    while i < 10 'loop'
        arr.push(i)
        i = i + 1
    end 'loop'
    return arr.count()
end 'main'
""";

		var tempExe = GetTempExePath(testName);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Compilation failed: {error}"
				};
			}

			if (ir == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "No IR generated"
				};
			}

			// Verify IR contains heap_realloc for array growth (or heap_realloc_or_alloc)
			if (!ir.Contains(IrHeapRealloc)) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"IR does not contain {IrHeapRealloc} instruction"
				};
			}

			// NOTE: HeapFree is only generated if cleanup code is emitted.
			// This test currently verifies realloc is called for growth.
			// Cleanup verification is separate (ManagedMemoryHeapArrayGeneratesFree)

			var runResult = RunExecutable(tempExe);
			if (runResult == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "Failed to run executable"
				};
			}

			if (runResult.Value.ExitCode != 10) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Expected exit code 10, got {runResult.Value.ExitCode}"
				};
			}

			return new UnitTestResult { Name = testName, Passed = true };
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that fixed-size array literals get cleaned up.
	/// </summary>
	private static UnitTestResult ManagedMemoryFixedSizeArrayLiteralCleanup() {
		const string testName = "managed-memory-fixed-size-array-literal-cleanup";
		const string source = """
function main() returns int
    var arr = [10, 20, 30]
    return try arr.get(1) otherwise 0
end 'main'
""";

		var tempExe = GetTempExePath(testName);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Compilation failed: {error}"
				};
			}

			if (ir == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "No IR generated"
				};
			}

			// Verify IR contains heap_free for cleanup
			if (!ir.Contains(IrHeapFree)) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"IR does not contain {IrHeapFree} instruction"
				};
			}

			var runResult = RunExecutable(tempExe);
			if (runResult == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "Failed to run executable"
				};
			}

			if (runResult.Value.ExitCode != 20) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Expected exit code 20, got {runResult.Value.ExitCode}"
				};
			}

			return new UnitTestResult { Name = testName, Passed = true };
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that struct field array method calls work correctly.
	/// Uses inline generic type since typealias may not resolve in struct field context.
	/// </summary>
	private static UnitTestResult ManagedMemoryStructFieldArrayMethodCall() {
		const string testName = "managed-memory-struct-field-array-method-call";
		const string source = """
type Config
    export var values Array with int
end 'Config'

function main() returns int
    var config = Config{values: [1, 2, 3]}
    return config.values.count()
end 'main'
""";

		var tempExe = GetTempExePath(testName);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Compilation failed: {error}"
				};
			}

			var runResult = RunExecutable(tempExe);
			if (runResult == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "Failed to run executable"
				};
			}

			if (runResult.Value.ExitCode != 3) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Expected exit code 3, got {runResult.Value.ExitCode}"
				};
			}

			return new UnitTestResult { Name = testName, Passed = true };
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	// ============================================================================
	// Managed String Tests
	// These tests require string literal support in the compiler.
	// ============================================================================

	/// <summary>
	/// Test that heap strings generate cleanup.
	/// </summary>
	private static UnitTestResult ManagedStringHeapStringGeneratesCleanup() {
		const string testName = "managed-string-heap-string-generates-cleanup";
		const string source = """
function main() returns int
    var s = "this is a heap allocated string!"
    return s.count()
end 'main'
""";

		var tempExe = GetTempExePath(testName);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Compilation failed: {error}"
				};
			}

			var runResult = RunExecutable(tempExe);
			if (runResult == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "Failed to run executable"
				};
			}

			// "this is a heap allocated string!" = 32 characters
			if (runResult.Value.ExitCode != 32) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Expected exit code 32, got {runResult.Value.ExitCode}"
				};
			}

			return new UnitTestResult { Name = testName, Passed = true };
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that string reassignment handles old value cleanup.
	/// </summary>
	private static UnitTestResult ManagedStringReassignmentHandlesOldValue() {
		const string testName = "managed-string-reassignment-handles-old-value";
		const string source = """
function main() returns int
    var s = "first heap allocated value!!"
    s = "second heap allocated here!!"
    return s.count()
end 'main'
""";

		var tempExe = GetTempExePath(testName);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Compilation failed: {error}"
				};
			}

			var runResult = RunExecutable(tempExe);
			if (runResult == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "Failed to run executable"
				};
			}

			// "second heap allocated here!!" = 28 characters
			if (runResult.Value.ExitCode != 28) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Expected exit code 28, got {runResult.Value.ExitCode}"
				};
			}

			return new UnitTestResult { Name = testName, Passed = true };
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that loop concatenation generates cleanup for intermediate strings.
	/// </summary>
	private static UnitTestResult ManagedStringLoopConcatenationCleanup() {
		const string testName = "managed-string-loop-concatenation-cleanup";
		const string source = """
function main() returns int
    var s = ""
    var a = "a"
    var i = 0
    while i < 5 'loop'
        s = "{s}{a}"
        i = i + 1
    end 'loop'
    return s.count()
end 'main'
""";

		var tempExe = GetTempExePath(testName);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Compilation failed: {error}"
				};
			}

			if (ir == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "No IR generated"
				};
			}

			// Verify IR contains heap_free for string cleanup
			if (!ir.Contains(IrHeapFree)) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"IR does not contain {IrHeapFree} instruction"
				};
			}

			var runResult = RunExecutable(tempExe);
			if (runResult == null) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = "Failed to run executable"
				};
			}

			// 5 'a' characters
			if (runResult.Value.ExitCode != 5) {
				return new UnitTestResult {
					Name = testName,
					Passed = false,
					ErrorMessage = $"Expected exit code 5, got {runResult.Value.ExitCode}"
				};
			}

			return new UnitTestResult { Name = testName, Passed = true };
		} finally {
			CleanupTempFile(tempExe);
		}
	}
}

/// <summary>
/// Result of a single unit test.
/// </summary>
public class UnitTestResult {
	public required string Name { get; init; }
	public required bool Passed { get; init; }
	public string? ErrorMessage { get; init; }
}

/// <summary>
/// Summary of all unit test results.
/// </summary>
public class UnitTestSummary {
	public required List<UnitTestResult> Results { get; init; }
	public required int Passed { get; init; }
	public required int Failed { get; init; }
	public required int Total { get; init; }
	public required TimeSpan TotalDuration { get; init; }
}
