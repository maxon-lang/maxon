using System.Text;
using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Result of fragment generation.
/// </summary>
public record FragmentGenerationResult(int Generated, int Errors);

/// <summary>
/// Generates .test fragment files from spec files.
/// </summary>
public static partial class FragmentGenerator {
  private const string SpecCountFileName = ".spec_count";

  /// <summary>
  /// Get the modification time of the compiler executable.
  /// </summary>
  private static DateTime GetCompilerMtime() {
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
  /// Write a file with retry logic for transient Windows file locking errors
  /// (e.g., antivirus or search indexer briefly memory-mapping the file).
  /// </summary>
  private static void WriteFileWithRetry(string path, string content, int maxRetries = 3) {
    for (int attempt = 0; attempt <= maxRetries; attempt++) {
      try {
        File.WriteAllText(path, content);
        return;
      } catch (IOException) when (attempt < maxRetries) {
        Thread.Sleep(50 * (attempt + 1));
      }
    }
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
  /// Clean the fragments directory by deleting and recreating it.
  /// </summary>
  private static void CleanFragmentsDirectory(string fragmentDir) {
    if (Directory.Exists(fragmentDir)) {
      try { Directory.Delete(fragmentDir, recursive: true); } catch { /* ignore */ }
    }
    Directory.CreateDirectory(fragmentDir);
  }

  /// <summary>
  /// Generate fragment files from all specs in the given directory.
  /// </summary>
  /// <param name="specDir">Directory containing spec files</param>
  /// <param name="fragmentDir">Directory to output fragment files</param>
  /// <param name="force">Force regeneration even if up to date</param>
  /// <returns>Result with number of fragments generated and error count</returns>
  public static FragmentGenerationResult GenerateFragments(string specDir, string fragmentDir, bool force = false, string? filter = null) {
    if (!Directory.Exists(specDir)) {
      Logger.Error(LogCategory.Testing, $"Spec directory not found: {specDir}");
      return new FragmentGenerationResult(0, 1);
    }

    Directory.CreateDirectory(fragmentDir);

    var specs = SpecParser.ParseDirectory(specDir);
    var totalTests = specs.Sum(s => s.Tests.Count);

    // Check before deleting, then delete so interrupted runs force full regeneration next time
    var needsRegen = filter == null && NeedsRegeneration(fragmentDir, specs.Count, totalTests);
    DeleteSpecCountFlag(fragmentDir);

    if (needsRegen) {
      CleanFragmentsDirectory(fragmentDir);
      force = true;
    }

    var generated = 0;
    var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
    var workerCount = Math.Max(1, Environment.ProcessorCount / 2);
    var compilerMtime = GetCompilerMtime();

    // Check for duplicate test names within specs
    foreach (var spec in specs) {
      var dupes = spec.Tests.GroupBy(t => t.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
      if (dupes.Count > 0) {
        foreach (var dupe in dupes) {
          errors.Add($"Duplicate test name '{dupe}' in {spec.FilePath}");
        }
      }
    }
    if (!errors.IsEmpty) {
      return new FragmentGenerationResult(0, errors.Count);
    }

    Parallel.ForEach(specs, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, spec => {
      var specFile = new FileInfo(spec.FilePath);
      var specName = Path.GetFileNameWithoutExtension(spec.FilePath);
      var specFragmentDir = Path.Combine(fragmentDir, specName);
      Directory.CreateDirectory(specFragmentDir);

      foreach (var test in spec.Tests) {
        // Skip tests that don't match the filter (matched against specName/testName)
        var testPath = $"{specName}/{test.Name}";
        if (filter != null && !testPath.Contains(filter, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        var fragmentPath = Path.Combine(specFragmentDir, $"{test.Name}.test");
        var exePath = Path.Combine(specFragmentDir, $"{test.Name}.exe");
        var fragmentFile = new FileInfo(fragmentPath);

        // Skip if fragment is newer than both spec and compiler (unless force)
        if (!force && fragmentFile.Exists &&
          fragmentFile.LastWriteTimeUtc > specFile.LastWriteTimeUtc &&
          fragmentFile.LastWriteTimeUtc > compilerMtime) {
          continue;
        }

        try {
          // Pass absolute path - CompileError.Format() will make it relative to ProjectRoot
          var absolutePath = Path.GetFullPath(fragmentPath);
          var (content, error) = GenerateFragmentContent(test, exePath, absolutePath);
          if (error != null) {
            errors.Add($"Error compiling 'specs/fragments/{specName}/{test.Name}.test':\n{error}");
          }
          WriteFileWithRetry(fragmentPath, content.Replace("\r\n", "\n").Replace("\r", "\n"));
          Interlocked.Increment(ref generated);
        } catch (Exception ex) {
          errors.Add($"Exception generating specs/fragments/{specName}/{test.Name}: {ex.Message}\n{ex.StackTrace}");
        }
      }
    });

    // Report any compilation errors encountered during fragment generation
    foreach (var error in errors) {
      Logger.Error(LogCategory.Testing, error);
    }

    // Only restore the flag on a successful unfiltered run
    if (filter == null && errors.IsEmpty) {
      WriteSpecCountFlag(fragmentDir, specs.Count, totalTests);
    }

    return new FragmentGenerationResult(generated, errors.Count);
  }

  private static (string Content, string? Error) GenerateFragmentContent(TestCase test, string exePath, string fragmentPath) {
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

    if (test.TrackMemory) {
      sb.AppendLine("TrackMemory: true");
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
          var result = new Compiler.Compiler().Compile(sources, exePath, returnIr: true, trackAllocs: test.TrackMemory);
          if (result.Success) {
            if (result.X86Ir != null) {
              sb.Append(result.X86Ir.Trim());
              sb.AppendLine();
            }
          } else {
            sb.AppendLine($"// Compilation failed: {result.Error}");
            error ??= result.Error;
          }
        } finally {
          try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
      } else {
        sources = [new Compiler.SourceFile(fragmentPath, sourceWithComment)];
        var result = new Compiler.Compiler().Compile(sources, exePath, returnIr: true, trackAllocs: test.TrackMemory);
        if (result.Success) {
          if (result.X86Ir != null) {
            sb.Append(result.X86Ir.Trim());
            sb.AppendLine();
          }
        } else {
          sb.AppendLine($"// Compilation failed: {result.Error}");
          error ??= result.Error;
        }
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
    var (expectation, fragmentArgs, fragmentTrackMemory) = ParseExpectation(expectationSection);

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
      TrackMemory = fragmentTrackMemory,
      SourceFiles = sourceFiles
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

  private static (TestExpectation Expectation, string? Args, bool TrackMemory) ParseExpectation(string section) {
    var lines = section.Split('\n');
    int? exitCode = null;
    string? stdout = null;
    string? stderr = null;
    string? requiredMLIR = null;
    string? requiredRdata = null;
    string? requiredData = null;
    string? expectedError = null;
    string? args = null;
    bool trackMemory = false;

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
      } else if (line.StartsWith("TrackMemory:")) {
        trackMemory = line["TrackMemory:".Length..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
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
      }, args, trackMemory);
    }

    return (new SuccessExpectation {
      ExitCode = exitCode,
      Stdout = stdout,
      Stderr = stderr,
      RequiredMLIR = requiredMLIR,
      RequiredRdata = requiredRdata,
      RequiredData = requiredData,
      TrackMemory = trackMemory
    }, args, trackMemory);
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
