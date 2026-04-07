using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Generates .test fragment files from spec files.
/// </summary>
public static partial class FragmentGenerator {

  /// <summary>
  /// Get the compiler mtime as ticks for cache manifest.
  /// </summary>
  public static long GetCompilerMtimeTicks() => GetCompilerMtime().Ticks;

  /// <summary>
  /// Get the stdlib max mtime as ticks for cache manifest.
  /// </summary>
  public static long GetStdlibMtimeTicks(string projectRoot) => GetStdlibMaxMtime(projectRoot).Ticks;

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
  /// Get the maximum modification time across all files in stdlib/ (includes .maxon sources and .bin data files).
  /// </summary>
  private static DateTime GetStdlibMaxMtime(string projectRoot) {
    var stdlibDir = Path.Combine(projectRoot, "stdlib");
    if (!Directory.Exists(stdlibDir)) return DateTime.MaxValue; // force regen if missing

    var maxMtime = DateTime.MinValue;
    foreach (var file in Directory.GetFiles(stdlibDir, "*", SearchOption.AllDirectories)) {
      var mtime = new FileInfo(file).LastWriteTimeUtc;
      if (mtime > maxMtime) maxMtime = mtime;
    }
    return maxMtime == DateTime.MinValue ? DateTime.MaxValue : maxMtime;
  }

  /// <summary>
  /// Get the .spec-cache directory path for a fragment directory.
  /// </summary>
  public static string GetSpecCacheDir(string fragmentDir) => Path.Combine(fragmentDir, ".spec-cache");

  /// <summary>
  /// Load the test cache from the .spec-cache directory.
  /// Returns an empty cache if the directory or files don't exist or are corrupt.
  /// </summary>
  public static TestCache LoadTestCache(string specCacheDir) {
    var cache = new TestCache();

    var manifestPath = Path.Combine(specCacheDir, "manifest");
    if (!File.Exists(manifestPath)) return cache;

    try {
      var lines = File.ReadAllLines(manifestPath);
      foreach (var line in lines) {
        if (line.StartsWith("compiler:") && long.TryParse(line["compiler:".Length..], out var ct))
          cache.CompilerMtimeTicks = ct;
        else if (line.StartsWith("stdlib:") && long.TryParse(line["stdlib:".Length..], out var st))
          cache.StdlibMtimeTicks = st;
        else if (line.StartsWith("specs:") && int.TryParse(line["specs:".Length..], out var sc))
          cache.SpecCount = sc;
        else if (line.StartsWith("tests:") && int.TryParse(line["tests:".Length..], out var tc))
          cache.TestCount = tc;
      }
    } catch {
      return new TestCache();
    }

    var resultsPath = Path.Combine(specCacheDir, "results");
    if (!File.Exists(resultsPath)) return cache;

    try {
      foreach (var line in File.ReadAllLines(resultsPath)) {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
        var parts = line.Split('\t');
        if (parts.Length != 5) continue;
        if (!long.TryParse(parts[1], out var fragMtime)) continue;
        if (!long.TryParse(parts[2], out var exeMtime)) continue;
        if (!long.TryParse(parts[3], out var lastPass)) continue;
        cache.Entries[parts[0]] = new CacheEntry(parts[0], fragMtime, exeMtime, lastPass, parts[4]);
      }
    } catch {
      cache.Entries.Clear();
    }

    return cache;
  }

  /// <summary>
  /// Save the test cache atomically (write to temp files, then rename).
  /// </summary>
  public static void SaveTestCache(string specCacheDir, TestCache cache) {
    Directory.CreateDirectory(specCacheDir);

    // Write manifest
    var manifestPath = Path.Combine(specCacheDir, "manifest");
    var manifestTmp = manifestPath + ".tmp";
    File.WriteAllText(manifestTmp, $"CACHE_V1\ncompiler:{cache.CompilerMtimeTicks}\nstdlib:{cache.StdlibMtimeTicks}\nspecs:{cache.SpecCount}\ntests:{cache.TestCount}\n");
    File.Move(manifestTmp, manifestPath, overwrite: true);

    // Write results
    var resultsPath = Path.Combine(specCacheDir, "results");
    var resultsTmp = resultsPath + ".tmp";
    var sb = new StringBuilder();
    foreach (var entry in cache.Entries.Values.OrderBy(e => e.TestKey, StringComparer.OrdinalIgnoreCase)) {
      sb.Append(entry.TestKey).Append('\t')
        .Append(entry.FragmentMtimeTicks).Append('\t')
        .Append(entry.ExeMtimeTicks).Append('\t')
        .Append(entry.LastPassTicks).Append('\t')
        .AppendLine(entry.TestType);
    }
    File.WriteAllText(resultsTmp, sb.ToString());
    File.Move(resultsTmp, resultsPath, overwrite: true);
  }

  /// <summary>
  /// Prepare work items from spec files. Parses specs, creates directories, checks
  /// staleness against the .spec-cache, and returns work items for the unified test pipeline.
  /// Does NOT compile anything — compilation happens in worker threads.
  /// </summary>
  public static PrepareResult PrepareWorkItems(string specDir, string fragmentDir, string projectRoot, bool force = false, string? filter = null, Compiler.CompileTarget? target = null) {
    var errors = new List<string>();

    if (!Directory.Exists(specDir)) {
      errors.Add($"Spec directory not found: {specDir}");
      return new PrepareResult([], 0, errors);
    }

    Directory.CreateDirectory(fragmentDir);

    var targetKey = target != null ? $"{target.Arch}-{target.Os}" : null;
    var specs = SpecParser.ParseDirectory(specDir, targetKey);
    var totalTests = specs.Sum(s => s.Tests.Count);

    // Load cache and check for global invalidation
    var specCacheDir = GetSpecCacheDir(fragmentDir);
    var cache = LoadTestCache(specCacheDir);
    var compilerMtime = GetCompilerMtime();
    var stdlibMtime = GetStdlibMaxMtime(projectRoot);
    var compilerTicks = compilerMtime.Ticks;
    var stdlibTicks = stdlibMtime.Ticks;

    var globalInvalidation = force
      || cache.CompilerMtimeTicks != compilerTicks
      || cache.StdlibMtimeTicks != stdlibTicks
      || (filter == null && (cache.SpecCount != specs.Count || cache.TestCount != totalTests));

    if (globalInvalidation) {
      cache.Entries.Clear();
    }

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

    // Pre-create all spec fragment directories (and cache subdirectories)
    foreach (var spec in specs) {
      var specName = Path.GetFileNameWithoutExtension(spec.FilePath);
      Directory.CreateDirectory(Path.Combine(fragmentDir, specName));
      Directory.CreateDirectory(Path.Combine(specCacheDir, specName));
    }

    // Determine exe extension for cached executables
    var exeExt = target?.Os == "windows" || (target == null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ? ".exe" : "";

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
        // .ir_exe is used for fragment generation IR extraction only
        var irExePath = Path.Combine(specFragmentDir, $"{test.Name}.ir_exe");
        var fragmentFile = new FileInfo(fragmentPath);

        // Fragment regeneration check (same as before)
        var needsRegen = force || !fragmentFile.Exists ||
          fragmentFile.LastWriteTimeUtc <= specFile.LastWriteTimeUtc ||
          fragmentFile.LastWriteTimeUtc <= compilerMtime;

        // Determine compilation and execution needs from cache
        var testKey = testPath;
        var cachedExePath = Path.Combine(specCacheDir, specName, $"{test.Name}{exeExt}");
        var needsCompilation = true;
        var needsExecution = true;

        if (!needsRegen && cache.Entries.TryGetValue(testKey, out var entry)) {
          var fragMtimeTicks = fragmentFile.Exists ? fragmentFile.LastWriteTimeUtc.Ticks : 0;
          if (entry.FragmentMtimeTicks == fragMtimeTicks) {
            if (entry.TestType == "compiler_error") {
              // CompilerError tests have no exe — cached if fragment unchanged and previously passed
              needsCompilation = false;
              needsExecution = entry.LastPassTicks <= 0;
            } else if (File.Exists(cachedExePath)) {
              var exeFile = new FileInfo(cachedExePath);
              if (exeFile.LastWriteTimeUtc.Ticks == entry.ExeMtimeTicks) {
                needsCompilation = false;
                needsExecution = entry.LastPassTicks <= 0;
              }
            }
          }
        }

        workItems.Add(new TestWorkItem(fragmentPath, irExePath, specName, test.Name, test, specFile, needsRegen, needsCompilation, needsExecution));
      }
    }

    // Clean up orphaned fragments and cache entries on unfiltered runs with global invalidation
    if (globalInvalidation && filter == null) {
      var expectedFragments = new HashSet<string>(workItems.Select(w => Path.GetFullPath(w.FragmentPath)), StringComparer.OrdinalIgnoreCase);
      foreach (var specDir2 in Directory.GetDirectories(fragmentDir)) {
        if (Path.GetFileName(specDir2) == ".spec-cache") continue;
        foreach (var file in Directory.GetFiles(specDir2, "*.test")) {
          if (!expectedFragments.Contains(Path.GetFullPath(file))) {
            try { File.Delete(file); } catch { }
          }
        }
      }
      // Remove directories for specs that no longer exist
      var expectedSpecDirs = new HashSet<string>(specs.Select(s => Path.GetFileNameWithoutExtension(s.FilePath)), StringComparer.OrdinalIgnoreCase);
      foreach (var dir in Directory.GetDirectories(fragmentDir)) {
        if (Path.GetFileName(dir) == ".spec-cache") continue;
        if (!expectedSpecDirs.Contains(Path.GetFileName(dir))) {
          try { Directory.Delete(dir, recursive: true); } catch { }
        }
      }
      // Clean orphaned cache subdirectories
      if (Directory.Exists(specCacheDir)) {
        foreach (var dir in Directory.GetDirectories(specCacheDir)) {
          if (!expectedSpecDirs.Contains(Path.GetFileName(dir))) {
            try { Directory.Delete(dir, recursive: true); } catch { }
          }
        }
      }
    }

    return new PrepareResult([.. workItems], totalTests, errors);
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
