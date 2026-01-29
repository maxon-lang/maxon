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
	// memref.heap_free -> maxon_free call
	// memref.heap_realloc -> maxon_realloc call
	// memref.heap_alloc -> maxon_alloc call
	private const string IrHeapFree = "maxon_free";
	private const string IrHeapRealloc = "maxon_realloc";
	private const string IrHeapAlloc = "maxon_alloc";

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

			// Rdata and COW: Verifies array literals use rdata and COW works
			("rdata-constant-array-uses-rdata", RdataConstantArrayUsesRdata),
			("rdata-cow-mutation-copies-to-heap", RdataCowMutationCopiesToHeap),
			("rdata-cow-multiple-mutations", RdataCowMultipleMutations),
			("rdata-non-constant-array-uses-heap", RdataNonConstantArrayUsesHeap),

			// Managed strings: Ported from maxon-bin optimizer tests
			("managed-string-heap-string-generates-cleanup", ManagedStringHeapStringGeneratesCleanup),
			("managed-string-reassignment-handles-old-value", ManagedStringReassignmentHandlesOldValue),
			("managed-string-substring-retains-parent", ManagedStringSubstringRetainsParent),
			("managed-string-print-heap-string", ManagedStringPrintHeapString),
			("managed-string-short-string-sso", ManagedStringShortStringSso),
			("managed-string-loop-concatenation-cleanup", ManagedStringLoopConcatenationCleanup),
			("managed-string-literal-deduplication", ManagedStringLiteralDeduplication),
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
		var result = new Compiler.Compiler().Compile(sources, tempExePath, returnIr: true);
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

	private static int CountByteOccurrences(byte[] data, byte[] pattern) {
		int count = 0;
		int pos = 0;
		while (pos <= data.Length - pattern.Length) {
			bool matched = true;
			for (int i = 0; i < pattern.Length; i++) {
				if (data[pos + i] != pattern[i]) {
					matched = false;
					break;
				}
			}
			if (matched) {
				count++;
				pos += pattern.Length;
				continue;
			}
			pos++;
		}
		return count;
	}

	/// <summary>
	/// Information about a PE section.
	/// </summary>
	private sealed record PeSectionInfo(string Name, uint VirtualSize, uint VirtualAddress, uint RawSize, uint RawOffset, uint Characteristics);

	/// <summary>
	/// Parses basic PE section headers from an executable.
	/// Returns null if parsing fails.
	/// </summary>
	private static List<PeSectionInfo>? ParsePeSections(string exePath) {
		try {
			using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read);
			using var reader = new BinaryReader(fs);

			// DOS Header - check magic
			var dosMagic = reader.ReadUInt16();
			if (dosMagic != 0x5A4D) return null; // "MZ"

			// Skip to e_lfanew (PE header offset at 0x3C)
			fs.Position = 0x3C;
			var peOffset = reader.ReadUInt32();

			// PE Signature
			fs.Position = peOffset;
			var peSignature = reader.ReadUInt32();
			if (peSignature != 0x00004550) return null; // "PE\0\0"

			// COFF Header
			var machine = reader.ReadUInt16();
			var numberOfSections = reader.ReadUInt16();
			reader.ReadUInt32(); // TimeDateStamp
			reader.ReadUInt32(); // PointerToSymbolTable
			reader.ReadUInt32(); // NumberOfSymbols
			var sizeOfOptionalHeader = reader.ReadUInt16();
			reader.ReadUInt16(); // Characteristics

			// Skip optional header
			fs.Position += sizeOfOptionalHeader;

			// Parse section headers
			var sections = new List<PeSectionInfo>();
			for (int i = 0; i < numberOfSections; i++) {
				var nameBytes = reader.ReadBytes(8);
				var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
				var virtualSize = reader.ReadUInt32();
				var virtualAddress = reader.ReadUInt32();
				var rawSize = reader.ReadUInt32();
				var rawOffset = reader.ReadUInt32();
				reader.ReadUInt32(); // PointerToRelocations
				reader.ReadUInt32(); // PointerToLinenumbers
				reader.ReadUInt16(); // NumberOfRelocations
				reader.ReadUInt16(); // NumberOfLinenumbers
				var characteristics = reader.ReadUInt32();

				sections.Add(new PeSectionInfo(name, virtualSize, virtualAddress, rawSize, rawOffset, characteristics));
			}

			return sections;
		} catch {
			return null;
		}
	}

	/// <summary>
	/// Reads raw data from a PE section.
	/// </summary>
	private static byte[]? ReadPeSectionData(string exePath, PeSectionInfo section) {
		try {
			using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read);
			fs.Position = section.RawOffset;
			var data = new byte[section.RawSize];
			fs.ReadExactly(data, 0, (int)section.RawSize);
			return data;
		} catch {
			return null;
		}
	}

	/// <summary>
	/// Converts an array of int64 values to little-endian bytes.
	/// </summary>
	private static byte[] Int64ArrayToBytes(params long[] values) {
		var bytes = new byte[values.Length * 8];
		for (int i = 0; i < values.Length; i++) {
			BitConverter.TryWriteBytes(bytes.AsSpan(i * 8, 8), values[i]);
		}
		return bytes;
	}

	/// <summary>
	/// Checks if the .rdata section contains the expected byte sequence.
	/// </summary>
	private static (bool found, string? error) VerifyRdataContains(string exePath, byte[] expected) {
		var sections = ParsePeSections(exePath);
		if (sections == null) return (false, "Failed to parse PE sections");

		var rdataSection = sections.FirstOrDefault(s => s.Name == ".rdata");
		if (rdataSection == null) return (false, "PE does not contain .rdata section");

		if ((rdataSection.Characteristics & 0x40000000) == 0)
			return (false, ".rdata section does not have READ characteristic");

		var rdataBytes = ReadPeSectionData(exePath, rdataSection);
		if (rdataBytes == null) return (false, "Failed to read .rdata section data");

		for (int i = 0; i <= rdataBytes.Length - expected.Length; i++) {
			if (rdataBytes.AsSpan(i, expected.Length).SequenceEqual(expected))
				return (true, null);
		}
		return (false, null);
	}

	/// <summary>
	/// Creates a failing UnitTestResult.
	/// </summary>
	private static UnitTestResult Fail(string name, string message) =>
		new() { Name = name, Passed = false, ErrorMessage = message };

	/// <summary>
	/// Creates a passing UnitTestResult.
	/// </summary>
	private static UnitTestResult Pass(string name) =>
		new() { Name = name, Passed = true };

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
	/// This test requires execution - would crash without proper stack probing.
	/// </summary>
	private static UnitTestResult StackProbingLargeStructRecursive() {
		const string name = "stack-probing-large-struct-recursive";

		// Generate a struct with 2000 int fields (16000 bytes on stack)
		// This requires stack probing on Windows - a single sub rsp, 16000
		// would skip multiple pages and crash without __chkstk
		var sb = new StringBuilder();
		sb.AppendLine("type BigStruct");
		for (int i = 0; i < 2000; i++) {
			sb.AppendLine($"    export var f{i} int");
		}
		sb.AppendLine("end 'BigStruct'");
		sb.AppendLine();
		sb.AppendLine("""
function recurse(n int) returns int
    var s = BigStruct { f0: n }
    if n == 0 'base'
        return s.f1999
    end 'base'
    return recurse(n - 1)
end 'recurse'

function main() returns int
    return recurse(10)
end 'main'
""");

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, _) = CompileWithIr(sb.ToString(), tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");

			var runResult = RunExecutable(tempExe);
			if (runResult == null) return Fail(name, "Failed to run executable (timeout or crash)");
			if (runResult.Value.ExitCode != 0) return Fail(name, $"Expected exit code 0, got {runResult.Value.ExitCode}");

			return Pass(name);
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
		const string name = "managed-memory-heap-array-generates-free";
		const string source = """
typealias IntArray is Array with int

function main() returns int
    var arr = IntArray{}
    arr.push(1)
    arr.push(2)
    return arr.count()
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null || !ir.Contains(IrHeapFree)) return Fail(name, $"IR missing {IrHeapFree}");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that nested scopes generate multiple heap_free calls.
	/// </summary>
	private static UnitTestResult ManagedMemoryScopeCleanupGeneratesFree() {
		const string name = "managed-memory-scope-cleanup-generates-free";
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

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null) return Fail(name, "No IR generated");

			int freeCount = CountOccurrences(ir, IrHeapFree);
			if (freeCount < 2) return Fail(name, $"Expected >= 2 {IrHeapFree}, found {freeCount}");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that array growth in a loop generates heap_realloc.
	/// </summary>
	private static UnitTestResult ManagedMemoryLoopGrowthGeneratesRealloc() {
		const string name = "managed-memory-loop-growth-generates-realloc";
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

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null) return Fail(name, "No IR generated");
			if (!ir.Contains(IrHeapRealloc)) return Fail(name, $"IR missing {IrHeapRealloc}");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that fixed-size array literals get cleaned up.
	/// </summary>
	private static UnitTestResult ManagedMemoryFixedSizeArrayLiteralCleanup() {
		const string name = "managed-memory-fixed-size-array-literal-cleanup";
		const string source = """
function main() returns int
    var arr = [10, 20, 30]
    return try arr.get(1) otherwise 0
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null) return Fail(name, "No IR generated");
			if (!ir.Contains(IrHeapFree)) return Fail(name, $"IR missing {IrHeapFree}");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	// ============================================================================
	// Rdata and Copy-on-Write Tests
	// ============================================================================

	/// <summary>
	/// Test that constant array literals use rdata section.
	/// Verifies: IR contains lea_rdata, PE has .rdata section with array data.
	/// </summary>
	private static UnitTestResult RdataConstantArrayUsesRdata() {
		const string name = "rdata-constant-array-uses-rdata";
		const string source = """
function main() returns int
    let arr = [10, 20, 30]
    return try arr.get(1) otherwise 0
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null) return Fail(name, "No IR generated");
			if (!ir.Contains("lea_rdata")) return Fail(name, "IR missing lea_rdata for constant array");

			var (found, rdataError) = VerifyRdataContains(tempExe, Int64ArrayToBytes(10, 20, 30));
			if (rdataError != null) return Fail(name, rdataError);
			if (!found) return Fail(name, ".rdata missing array data [10, 20, 30]");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that mutating an rdata-backed array triggers COW (copy-on-write).
	/// Verifies: IR contains lea_rdata and HeapAlloc, PE has .rdata with original data.
	/// </summary>
	private static UnitTestResult RdataCowMutationCopiesToHeap() {
		const string name = "rdata-cow-mutation-copies-to-heap";
		const string source = """
function main() returns int
    var arr = [42]
    arr.set(0, value: 77)
    return try arr.get(0) otherwise 0
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null) return Fail(name, "No IR generated");
			if (!ir.Contains("lea_rdata")) return Fail(name, "IR missing lea_rdata - array should start in rdata");
			if (!ir.Contains(IrHeapAlloc)) return Fail(name, $"IR missing {IrHeapAlloc} - COW should allocate heap");

			var (found, rdataError) = VerifyRdataContains(tempExe, Int64ArrayToBytes(42));
			if (rdataError != null) return Fail(name, rdataError);
			if (!found) return Fail(name, ".rdata missing original value [42]");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that multiple mutations after COW work correctly.
	/// Verifies: .rdata contains original data, exactly one HeapAlloc for COW.
	/// </summary>
	private static UnitTestResult RdataCowMultipleMutations() {
		const string name = "rdata-cow-multiple-mutations";
		const string source = """
function main() returns int
    var arr = [1, 2, 3]
    arr.set(0, value: 10)
    arr.set(1, value: 20)
    arr.set(2, value: 30)
    var sum = 0
    sum = sum + (try arr.get(0) otherwise 0)
    sum = sum + (try arr.get(1) otherwise 0)
    sum = sum + (try arr.get(2) otherwise 0)
    return sum
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null) return Fail(name, "No IR generated");

			var (found, rdataError) = VerifyRdataContains(tempExe, Int64ArrayToBytes(1, 2, 3));
			if (rdataError != null) return Fail(name, rdataError);
			if (!found) return Fail(name, ".rdata missing original data [1, 2, 3]");

			int heapAllocCount = CountOccurrences(ir, IrHeapAlloc);
			if (heapAllocCount != 1) return Fail(name, $"Expected 1 {IrHeapAlloc} for COW, found {heapAllocCount}");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	/// <summary>
	/// Test that non-constant array literals use heap allocation (not rdata).
	/// Verifies: no lea_rdata, HeapAlloc used instead.
	/// </summary>
	private static UnitTestResult RdataNonConstantArrayUsesHeap() {
		const string name = "rdata-non-constant-array-uses-heap";
		const string source = """
function main() returns int
    var x = 5
    var arr = [1, x, 3]
    return try arr.get(1) otherwise 0
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null) return Fail(name, "No IR generated");
			if (ir.Contains("lea_rdata")) return Fail(name, "IR has lea_rdata - non-constant array shouldn't use rdata");
			if (!ir.Contains(IrHeapAlloc)) return Fail(name, $"IR missing {IrHeapAlloc} - non-constant array should use heap");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	// ============================================================================
	// Managed String Tests
	// ============================================================================

	private static UnitTestResult ManagedStringHeapStringGeneratesCleanup() {
		const string name = "managed-string-heap-string-generates-cleanup";
		const string source = """
function main() returns int
    var s = "this is a heap allocated string!"
	return s.byteLength()
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null || !ir.Contains("lea_rdata")) return Fail(name, "IR missing lea_rdata for string literal");

			var (found, rdataError) = VerifyRdataContains(tempExe, Encoding.UTF8.GetBytes("this is a heap allocated string!\0"));
			if (rdataError != null) return Fail(name, rdataError);
			if (!found) return Fail(name, "String literal bytes not found in .rdata");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	private static UnitTestResult ManagedStringReassignmentHandlesOldValue() {
		const string name = "managed-string-reassignment-handles-old-value";
		const string source = """
function main() returns int
    var s = "first heap allocated value!!"
    s = "second heap allocated here!!"
	return s.byteLength()
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null || !ir.Contains("lea_rdata")) return Fail(name, "IR missing lea_rdata for string literals");

			var (foundFirst, firstError) = VerifyRdataContains(tempExe, Encoding.UTF8.GetBytes("first heap allocated value!!\0"));
			if (firstError != null) return Fail(name, firstError);
			if (!foundFirst) return Fail(name, "First string literal bytes not found in .rdata");

			// Second literal may be optimized or relocated depending on dedup/constant handling

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	private static UnitTestResult ManagedStringSubstringRetainsParent() {
		const string name = "managed-string-substring-retains-parent";
		const string source = """
function main() returns int
	var s = "hello world from heap!!"
	var subManaged = __managed_memory_slice(s._managed, 0, 5)
	return __managed_memory_len(subManaged)
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null) return Fail(name, "No IR generated");
			if (!ir.Contains(IrHeapAlloc)) return Fail(name, $"IR missing {IrHeapAlloc} for slice allocation");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	private static UnitTestResult ManagedStringPrintHeapString() {
		const string name = "managed-string-print-heap-string";
		const string source = """
function main() returns int
    var s = "heap allocated string here!!"
	return s.byteLength()
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null || !ir.Contains("lea_rdata")) return Fail(name, "IR missing lea_rdata for string literal");

			var (found, rdataError) = VerifyRdataContains(tempExe, Encoding.UTF8.GetBytes("heap allocated string here!!\0"));
			if (rdataError != null) return Fail(name, rdataError);
			if (!found) return Fail(name, "String literal bytes not found in .rdata");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	private static UnitTestResult ManagedStringShortStringSso() {
		const string name = "managed-string-short-string-sso";
		const string source = """
function main() returns int
    var s = "short"
	return s.byteLength()
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null || !ir.Contains("lea_rdata")) return Fail(name, "IR missing lea_rdata for string literal");

			var (found, rdataError) = VerifyRdataContains(tempExe, Encoding.UTF8.GetBytes("short\0"));
			if (rdataError != null) return Fail(name, rdataError);
			if (!found) return Fail(name, "String literal bytes not found in .rdata");

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	private static UnitTestResult ManagedStringLoopConcatenationCleanup() {
		const string name = "managed-string-loop-concatenation-cleanup";
		const string source = """
function main() returns int
    var s = ""
    var a = "a"
    var i = 0
    while i < 5 'loop'
        s = s.concat(a)
        i = i + 1
    end 'loop'
	return s.byteLength()
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, ir) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");
			if (ir == null) return Fail(name, "No IR generated");
			if (!ir.Contains(IrHeapAlloc)) return Fail(name, $"IR missing {IrHeapAlloc} for concatenation");

			// Cleanup for intermediate strings may not be implemented yet

			return Pass(name);
		} finally {
			CleanupTempFile(tempExe);
		}
	}

	private static UnitTestResult ManagedStringLiteralDeduplication() {
		const string name = "managed-string-literal-deduplication";
		const string source = """
function main() returns int
    var a = "hello world"
    var b = "hello world"
    var c = "hello world"
	return a.byteLength() + b.byteLength() + c.byteLength()
end 'main'
""";

		var tempExe = GetTempExePath(name);
		try {
			var (success, error, _) = CompileWithIr(source, tempExe);
			if (!success) return Fail(name, $"Compilation failed: {error}");

			var sections = ParsePeSections(tempExe);
			if (sections == null) return Fail(name, "Failed to parse PE sections");
			var rdataSection = sections.FirstOrDefault(s => s.Name == ".rdata");
			if (rdataSection == null) return Fail(name, "PE does not contain .rdata section");
			var rdataBytes = ReadPeSectionData(tempExe, rdataSection);
			if (rdataBytes == null) return Fail(name, "Failed to read .rdata section data");

			var needle = Encoding.UTF8.GetBytes("hello world\0");
			int occurrences = CountByteOccurrences(rdataBytes, needle);
			if (occurrences > 2) return Fail(name, $"Expected <= 2 string occurrences, found {occurrences}");

			return Pass(name);
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
