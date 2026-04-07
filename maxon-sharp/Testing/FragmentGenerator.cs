using System.Text;
using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Result of fragment generation.
/// </summary>
public record FragmentGenerationResult(int Generated, int TotalExpected, int Errors);

/// <summary>
/// Generates .test fragment files from spec files.
/// </summary>
public static partial class FragmentGenerator {
  private const string SpecCountFileName = ".spec_count";

  /// <summary>
  /// Get the modification time of the compiler executable.
  /// </summary>
  private static DateTime GetCompilerMtime() {
    // Use the entry assembly location first — Environment.ProcessPath returns the dotnet
    // host binary path when running via `dotnet run`, which doesn't change on rebuild.
    #pragma warning disable IL3000 // Assembly.Location returns empty in single-file; handled by fallback below
    var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
    #pragma warning restore IL3000
    if (!string.IsNullOrEmpty(assemblyPath) && File.Exists(assemblyPath)) {
      return new FileInfo(assemblyPath).LastWriteTimeUtc;
    }
    // Fallback to process path (works for self-contained / published single-file)
    var exePath = Environment.ProcessPath;
    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath)) {
      return new FileInfo(exePath).LastWriteTimeUtc;
    }
    // If we can't determine compiler mtime, return max value to force regeneration
    return DateTime.MaxValue;
  }

  /// <summary>
  /// Check if fragments need regeneration based on the .spec_count flag file.
  /// Returns true if regeneration is needed.
  /// </summary>
  private static bool NeedsRegeneration(string fragmentDir, int specCount, int testCount) {
    var flagPath = Path.Combine(fragmentDir, SpecCountFileName);
    if (!File.Exists(flagPath)) {
      return true;
    }

    try {
      var content = File.ReadAllText(flagPath).Trim();
      var parts = content.Split(':');
      if (parts.Length != 2) return true;

      if (!int.TryParse(parts[0], out var savedSpecCount)) return true;
      if (!int.TryParse(parts[1], out var savedTestCount)) return true;

      return savedSpecCount != specCount || savedTestCount != testCount;
    } catch {
      return true;
    }
  }

  /// <summary>
  /// Write the .spec_count flag file to indicate successful generation.
  /// </summary>
  private static void WriteSpecCountFlag(string fragmentDir, int specCount, int testCount) {
    var flagPath = Path.Combine(fragmentDir, SpecCountFileName);
    File.WriteAllText(flagPath, $"{specCount}:{testCount}");
  }

  /// <summary>
  /// Delete the .spec_count flag file (on generation failure).
  /// </summary>
  private static void DeleteSpecCountFlag(string fragmentDir) {
    var flagPath = Path.Combine(fragmentDir, SpecCountFileName);
    if (File.Exists(flagPath)) {
      try { File.Delete(flagPath); } catch { /* ignore */ }
    }
  }

  /// <summary>
  /// Prepare work items from spec files. Parses specs, creates directories, checks
  /// staleness, and returns work items for the unified test pipeline.
  /// Does NOT compile anything — compilation happens in worker threads.
  /// </summary>
  public static PrepareResult PrepareWorkItems(string specDir, string fragmentDir, bool force = false, string? filter = null, Compiler.CompileTarget? target = null) {
    var errors = new List<string>();

    if (!Directory.Exists(specDir)) {
      errors.Add($"Spec directory not found: {specDir}");
      return new PrepareResult([], 0, errors);
    }

    Directory.CreateDirectory(fragmentDir);

    var targetKey = target != null ? $"{target.Arch}-{target.Os}" : null;
    var specs = SpecParser.ParseDirectory(specDir, targetKey);
    var totalTests = specs.Sum(s => s.Tests.Count);

    // Check before deleting, then delete so interrupted runs force full regeneration next time
    var needsRegen = filter == null && NeedsRegeneration(fragmentDir, specs.Count, totalTests);
    DeleteSpecCountFlag(fragmentDir);

    if (needsRegen) {
      force = true;
    }

    var compilerMtime = GetCompilerMtime();

    // Check for duplicate test names within specs
    foreach (var spec in specs) {
      var dupes = spec.Tests.GroupBy(t => t.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
      foreach (var dupe in dupes) {
        errors.Add($"Duplicate test name '{dupe}' in {spec.FilePath}");
      }
    }
    if (errors.Count > 0) {
      return new PrepareResult([], totalTests, errors);
    }

    // Pre-create all spec fragment directories
    foreach (var spec in specs) {
      var specName = Path.GetFileNameWithoutExtension(spec.FilePath);
      Directory.CreateDirectory(Path.Combine(fragmentDir, specName));
    }

    // Build work items with staleness info
    var workItems = new List<TestWorkItem>();
    foreach (var spec in specs) {
      var specFile = new FileInfo(spec.FilePath);
      var specName = Path.GetFileNameWithoutExtension(spec.FilePath);
      var specFragmentDir = Path.Combine(fragmentDir, specName);

      foreach (var test in spec.Tests) {
        var testPath = $"{specName}/{test.Name}";
        if (filter != null && !testPath.Contains(filter, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        var fragmentPath = Path.Combine(specFragmentDir, $"{test.Name}.test");
        // Use .ir_exe suffix to avoid collision with RunTest's pre-compiled binary path.
        // Fragment generation compiles for IR extraction; the binary should NOT be reused
        // by RunTest which compiles with different flags (AsyncTrace, MmTrace per-test).
        var exePath = Path.Combine(specFragmentDir, $"{test.Name}.ir_exe");
        var fragmentFile = new FileInfo(fragmentPath);

        var needsRebuild = force || !fragmentFile.Exists ||
          fragmentFile.LastWriteTimeUtc <= specFile.LastWriteTimeUtc ||
          fragmentFile.LastWriteTimeUtc <= compilerMtime;

        workItems.Add(new TestWorkItem(fragmentPath, exePath, specName, test.Name, test, specFile, needsRebuild));
      }
    }

    // Clean up orphaned fragments (tests removed from specs) on unfiltered runs
    if (needsRegen && filter == null) {
      var expectedFragments = new HashSet<string>(workItems.Select(w => Path.GetFullPath(w.FragmentPath)), StringComparer.OrdinalIgnoreCase);
      foreach (var specDir2 in Directory.GetDirectories(fragmentDir)) {
        foreach (var file in Directory.GetFiles(specDir2, "*.test")) {
          if (!expectedFragments.Contains(Path.GetFullPath(file))) {
            try { File.Delete(file); } catch { }
          }
        }
      }
      // Remove directories for specs that no longer exist
      var expectedSpecDirs = new HashSet<string>(specs.Select(s => Path.GetFileNameWithoutExtension(s.FilePath)), StringComparer.OrdinalIgnoreCase);
      foreach (var dir in Directory.GetDirectories(fragmentDir)) {
        if (!expectedSpecDirs.Contains(Path.GetFileName(dir))) {
          try { Directory.Delete(dir, recursive: true); } catch { }
        }
      }
    }

    return new PrepareResult([.. workItems], totalTests, errors);
  }

  /// <summary>
  /// Write the spec count flag after a successful unfiltered run.
  /// </summary>
  public static void WriteSpecCountFlagPublic(string specDir, string fragmentDir, Compiler.CompileTarget? target = null) {
    var targetKey = target != null ? $"{target.Arch}-{target.Os}" : null;
    var specs = SpecParser.ParseDirectory(specDir, targetKey);
    var totalTests = specs.Sum(s => s.Tests.Count);
    WriteSpecCountFlag(fragmentDir, specs.Count, totalTests);
  }

  public static (string Content, string? Error) GenerateFragmentContent(TestCase test, string exePath, string fragmentPath, Compiler.CompileTarget? target = null) {
    var sb = new StringBuilder();
    string? error = null;

    var commentLine = $"// Test: {test.Name}";
    var sourceWithComment = $"{commentLine}\n{test.Source}";

    sb.AppendLine(commentLine);
    sb.AppendLine(test.Source);
    sb.AppendLine("---");

    if (test.Args != null) {
      sb.AppendLine($"Args: {test.Args}");
    }

    if (test.MmTrace) {
      sb.AppendLine("MmTrace: true");
    }

    if (test.AsyncTrace) {
      sb.AppendLine("AsyncTrace: true");
    }

    if (test.Expectation is SuccessExpectation success) {
      if (success.ExitCode.HasValue) {
        sb.AppendLine($"ExitCode: {success.ExitCode.Value}");
      }
      if (success.Stdout != null) {
        sb.AppendLine("Stdout: ```");
        sb.AppendLine(success.Stdout);
        sb.AppendLine("```");
      }
      if (success.Stderr != null) {
        sb.AppendLine("Stderr: ```");
        sb.AppendLine(success.Stderr);
        sb.AppendLine("```");
      }

      // Write required MLIR if specified
      if (success.RequiredMLIR != null) {
        sb.AppendLine("RequiredMLIR: ```");
        sb.AppendLine(success.RequiredMLIR);
        sb.AppendLine("```");
      }

      if (success.RequiredRdata != null) {
        sb.AppendLine("RequiredRdata: ```");
        sb.AppendLine(success.RequiredRdata);
        sb.AppendLine("```");
      }

      if (success.RequiredData != null) {
        sb.AppendLine("RequiredData: ```");
        sb.AppendLine(success.RequiredData);
        sb.AppendLine("```");
      }
    } else if (test.Expectation is CompilerErrorExpectation compilerError) {
      sb.AppendLine("MaxoncStderr: ```");
      sb.AppendLine(compilerError.ExpectedStderr);
      sb.AppendLine("```");
    }

    sb.AppendLine("---");

    // Compile to executable and capture IR (for success expectations only, skip if RequiredMLIR covers it)
    if (test.Expectation is SuccessExpectation s2 && s2.RequiredMLIR == null) {
      Compiler.SourceFile[] sources;
      if (test.SourceFiles != null) {
        var tempDir = Path.Combine(Path.GetTempPath(), $"maxon-frag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
          sources = [.. test.SourceFiles.Select(f => {
            var path = Path.Combine(tempDir, f.FileName);
            File.WriteAllText(path, f.Source);
            return new Compiler.SourceFile(path, f.Source);
          })];
          var result = new Compiler.Compiler().Compile(sources, exePath, returnIr: true, target: target);
          if (result.Success) {
            if (result.ArchIr != null) {
              sb.Append(result.ArchIr.Trim());
              sb.AppendLine();
            }
          } else {
            var errorStr = string.Join("\n", result.Errors.Select(e => e.Format()));
            sb.AppendLine($"// Compilation failed: {errorStr}");
            error ??= errorStr;
          }
          try { if (File.Exists(exePath)) File.Delete(exePath); } catch { }
        } finally {
          try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
      } else {
        sources = [new Compiler.SourceFile(fragmentPath, sourceWithComment)];
        var result = new Compiler.Compiler().Compile(sources, exePath, returnIr: true, target: target);
        if (result.Success) {
          if (result.ArchIr != null) {
            sb.Append(result.ArchIr.Trim());
            sb.AppendLine();
          }
        } else {
          var errorStr = string.Join("\n", result.Errors.Select(e => e.Format()));
          sb.AppendLine($"// Compilation failed: {errorStr}");
          error ??= errorStr;
        }
        try { if (File.Exists(exePath)) File.Delete(exePath); } catch { }
      }
    }

    sb.AppendLine("---");

    return (sb.ToString(), error);
  }

  /// <summary>
  /// Parse a fragment file.
  /// </summary>
  public static Fragment? ParseFragment(string fragmentPath) {
    if (!File.Exists(fragmentPath)) {
      return null;
    }

    var content = File.ReadAllText(fragmentPath);
    var lines = content.Split('\n');

    // Extract test name from first line
    var testName = "unknown";
    if (lines.Length > 0 && lines[0].StartsWith("// Test: ")) {
      testName = lines[0]["// Test: ".Length..].Trim();
    }

    // Find the separator "---"
    var separatorIndex = Array.FindIndex(lines, 1, l => l.Trim() == "---");
    if (separatorIndex < 0) {
      return null;
    }

    // Extract source code (from start to first ---), including the comment line
    // so that line numbers in error messages match the actual file
    var sourceLines = lines[0..separatorIndex];
    var source = string.Join("\n", sourceLines).Trim();

    // Parse expectations (between first --- and second ---)
    var secondSeparatorIndex = Array.FindIndex(lines, separatorIndex + 1, l => l.Trim() == "---");
    if (secondSeparatorIndex < 0) {
      secondSeparatorIndex = lines.Length;
    }

    var expectationSection = string.Join("\n", lines[(separatorIndex + 1)..secondSeparatorIndex]);
    var (expectation, fragmentArgs, mmTrace, asyncTrace) = ParseExpectation(expectationSection);

    // Parse generated MLIR (between second --- and third ---)
    string? generatedMLIR = null;
    if (secondSeparatorIndex < lines.Length) {
      var thirdSeparatorIndex = Array.FindIndex(lines, secondSeparatorIndex + 1, l => l.Trim() == "---");
      if (thirdSeparatorIndex < 0) {
        thirdSeparatorIndex = lines.Length;
      }
      var irSection = lines[(secondSeparatorIndex + 1)..thirdSeparatorIndex];
      generatedMLIR = ExtractGeneratedIr(irSection);
    }

    // Detect multi-file markers in source
    var sourceFiles = SplitMultiFileSource(source);

    return new Fragment {
      FilePath = fragmentPath,
      TestName = testName,
      Source = source,
      Expectation = expectation,
      GeneratedMLIR = generatedMLIR,
      Args = fragmentArgs,
      SourceFiles = sourceFiles,
      MmTrace = mmTrace,
      AsyncTrace = asyncTrace,
    };
  }

  private static string? ExtractGeneratedIr(string[] lines) {
    foreach (var line in lines) {
      if (line.TrimStart().StartsWith("// Compilation failed:")) {
        return null;
      }
    }

    return string.Join('\n', lines).TrimEnd();
  }

  private static (TestExpectation Expectation, string? Args, bool MmTrace, bool AsyncTrace) ParseExpectation(string section) {
    var lines = section.Split('\n');
    int? exitCode = null;
    string? stdout = null;
    string? stderr = null;
    string? requiredMLIR = null;
    string? requiredRdata = null;
    string? requiredData = null;
    string? expectedError = null;
    string? args = null;
    bool mmTrace = false;
    bool asyncTrace = false;

    var i = 0;
    while (i < lines.Length) {
      var line = lines[i].Trim();

      if (line.StartsWith("ExitCode:")) {
        var value = line["ExitCode:".Length..].Trim();
        if (int.TryParse(value, out var code)) {
          exitCode = code;
        }
      } else if (line.StartsWith("Args:")) {
        args = line["Args:".Length..].Trim();
      } else if (line.StartsWith("MmTrace:")) {
        mmTrace = line["MmTrace:".Length..].Trim() == "true";
      } else if (line.StartsWith("AsyncTrace:")) {
        asyncTrace = line["AsyncTrace:".Length..].Trim() == "true";
      } else if (line.StartsWith("MaxoncStderr: ```")) {
        expectedError = ExtractMultilineValue(lines, ref i);
      } else if (line.StartsWith("Stdout: ```")) {
        stdout = ExtractMultilineValue(lines, ref i);
      } else if (line.StartsWith("Stderr: ```")) {
        stderr = ExtractMultilineValue(lines, ref i);
      } else if (line.StartsWith("RequiredMLIR: ```")) {
        requiredMLIR = ExtractMultilineValue(lines, ref i);
      } else if (line.StartsWith("RequiredRdata: ```")) {
        requiredRdata = ExtractMultilineValue(lines, ref i);
      } else if (line.StartsWith("RequiredData: ```")) {
        requiredData = ExtractMultilineValue(lines, ref i);
      }

      i++;
    }

    if (expectedError != null) {
      return (new CompilerErrorExpectation {
        ExpectedStderr = expectedError
      }, args, mmTrace, asyncTrace);
    }

    return (new SuccessExpectation {
      ExitCode = exitCode,
      Stdout = stdout,
      Stderr = stderr,
      RequiredMLIR = requiredMLIR,
      RequiredRdata = requiredRdata,
      RequiredData = requiredData,
      MmTrace = mmTrace,
      AsyncTrace = asyncTrace,
    }, args, mmTrace, asyncTrace);
  }

  private static readonly Regex FileMarkerPattern = FileMarkerRegex();

  private static List<(string FileName, string Source)>? SplitMultiFileSource(string source) {
    var matches = FileMarkerPattern.Matches(source);
    if (matches.Count == 0) return null;

    var files = new List<(string FileName, string Source)>();
    for (int i = 0; i < matches.Count; i++) {
      var fileName = matches[i].Groups[1].Value.Trim();
      var start = matches[i].Index + matches[i].Length;
      var end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
      var fileSource = source[start..end].Trim();
      files.Add((fileName, fileSource));
    }

    return files.Count > 0 ? files : null;
  }

  private static string ExtractMultilineValue(string[] lines, ref int i) {
    var sb = new StringBuilder();
    i++; // Move past the opening line with ```
    while (i < lines.Length && !lines[i].Trim().StartsWith("```")) {
      sb.AppendLine(lines[i]);
      i++;
    }
    return sb.ToString().TrimEnd();
  }

  [GeneratedRegex(@"^// --- file:\s*(.+)$", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex FileMarkerRegex();
}
