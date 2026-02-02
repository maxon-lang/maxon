using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Executes tests from fragment files.
/// </summary>
public class TestRunner(string specDir, string fragmentDir, string tempDir, string? filter = null, int? workers = null, bool updateRequiredMLIR = false) {
  private readonly string _specDir = specDir;
  private readonly string _fragmentDir = fragmentDir;
  private readonly string _tempDir = tempDir;
  private readonly string? _filter = filter;
  private readonly int _workerCount = workers ?? Math.Max(1, Environment.ProcessorCount / 2);
  private readonly bool _updateRequiredMLIR = updateRequiredMLIR;

  /// <summary>
  /// Run all tests and return summary.
  /// </summary>
  public TestSummary RunAllSpecTests() {
    var sw = Stopwatch.StartNew();

    // Update RequiredMLIR in spec files if requested
    if (_updateRequiredMLIR) {
      UpdateRequiredMLIRInSpecFiles();
    }

    // Regenerate fragments if specs changed
    var genResult = FragmentGenerator.GenerateFragments(_specDir, _fragmentDir, _updateRequiredMLIR);
    if (genResult.Generated > 0) {
      Logger.Info(LogCategory.Testing, $"Generated {genResult.Generated} fragment(s)");
    }

    // Abort if fragment generation had errors
    if (genResult.Errors > 0) {
      // Clean up any executables that were generated before the error
      CleanupExecutables(_fragmentDir);
      return new TestSummary {
        Results = [],
        Passed = 0,
        Failed = 0,
        Total = 0,
        TotalDuration = sw.Elapsed,
        FragmentGenerationErrors = genResult.Errors
      };
    }

    // Load all fragments
    var fragments = LoadAllFragments();
    if (_filter != null) {
      var filterRegex = new Regex(_filter, RegexOptions.IgnoreCase);
      fragments = [.. fragments.Where(f => filterRegex.IsMatch(f.TestName))];
    }

    if (fragments.Count == 0) {
      Logger.Info(LogCategory.Testing, "No tests found");
      return new TestSummary {
        Results = [],
        Passed = 0,
        Failed = 0,
        Total = 0,
        TotalDuration = sw.Elapsed
      };
    }

    Logger.Info(LogCategory.Testing, $"Running {fragments.Count} test(s) with {_workerCount} worker(s)...");

    // Ensure temp directory exists
    Directory.CreateDirectory(_tempDir);

    // Run tests in parallel
    var results = new ConcurrentBag<TestResult>();
    Parallel.ForEach(fragments, new ParallelOptions { MaxDegreeOfParallelism = _workerCount }, fragment => {
      var result = RunTest(fragment);
      results.Add(result);

      var root = Compiler.CompileError.ProjectRoot ?? Environment.CurrentDirectory;
      var fullPath = Path.IsPathRooted(result.FilePath) ? result.FilePath : Path.GetFullPath(Path.Combine(root, result.FilePath));
      var testIdentifier = Path.GetRelativePath(root, fullPath).Replace('\\', '/');

      if (result.Passed) {
        // Passing tests only show at debug level
        Logger.Debug(LogCategory.Testing, $"[PASS] {testIdentifier}");
      } else {
        // Failing tests show at error level (always visible)
        Logger.Error(LogCategory.Testing, $"[FAIL] {testIdentifier}");
        if (result.ErrorMessage != null) {
          Logger.Error(LogCategory.Testing, $"  {result.ErrorMessage}");
        }
      }
    });

    sw.Stop();

    // Clean up generated .exe files in fragment directories
    CleanupExecutables(_fragmentDir);

    var resultList = results.ToList();
    var passed = resultList.Count(r => r.Passed);
    var failed = resultList.Count(r => !r.Passed);

    return new TestSummary {
      Results = resultList,
      Passed = passed,
      Failed = failed,
      Total = resultList.Count,
      TotalDuration = sw.Elapsed
    };
  }

  /// <summary>
  /// Recursively clean up .exe files from the fragment directory and its subdirectories.
  /// </summary>
  private static void CleanupExecutables(string directory) {
    if (!Directory.Exists(directory)) return;

    try {
      // Delete .exe files in this directory
      foreach (var exeFile in Directory.GetFiles(directory, "*.exe")) {
        try {
          File.Delete(exeFile);
        } catch {
          // Ignore deletion errors (file may be locked)
        }
      }

      // Recurse into subdirectories
      foreach (var subDir in Directory.GetDirectories(directory)) {
        CleanupExecutables(subDir);
      }
    } catch {
      // Ignore directory access errors
    }
  }

  private List<Fragment> LoadAllFragments() {
    var fragments = new List<Fragment>();

    if (!Directory.Exists(_fragmentDir)) {
      return fragments;
    }

    foreach (var file in Directory.GetFiles(_fragmentDir, "*.test", SearchOption.AllDirectories)) {
      var fragment = FragmentGenerator.ParseFragment(file);
      if (fragment != null) {
        fragments.Add(fragment);
      }
    }

    return fragments;
  }

  private TestResult RunTest(Fragment fragment) {
    var sw = Stopwatch.StartNew();

    try {
      // Check for pre-compiled executable alongside the fragment
      var fragmentDir = Path.GetDirectoryName(fragment.FilePath)!;
      var precompiledExe = Path.Combine(fragmentDir, $"{fragment.TestName}.exe");
      var fragmentFile = new FileInfo(fragment.FilePath);

      // Determine which executable to use
      string exePath;
      bool needsCleanup;
      string? compileError = null;

      if (File.Exists(precompiledExe)) {
        var exeFile = new FileInfo(precompiledExe);
        if (exeFile.LastWriteTimeUtc >= fragmentFile.LastWriteTimeUtc) {
          // Use pre-compiled executable
          exePath = precompiledExe;
          needsCleanup = false;
        } else {
          // Exe is stale, compile fresh
          var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Environment.CurrentManagedThreadId}.exe");
          var (Success, Error) = CompileToExecutable(fragment, tempExe);
          compileError = Error;
          exePath = tempExe;
          needsCleanup = true;
        }
      } else {
        // No pre-compiled exe, compile fresh
        var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Environment.CurrentManagedThreadId}.exe");
        var (Success, Error) = CompileToExecutable(fragment, tempExe);
        compileError = Error;
        exePath = tempExe;
        needsCleanup = true;
      }

      try {
        if (fragment.Expectation is CompilerErrorExpectation errorExpectation) {
          // Expect compilation to fail - need to compile to check for error
          if (compileError == null && !needsCleanup) {
            // Pre-compiled exe exists, but we expect an error - compile to get the error
            var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Environment.CurrentManagedThreadId}.exe");
            var (Success, Error) = CompileToExecutable(fragment, tempExe);
            compileError = Error;
            if (File.Exists(tempExe)) {
              try { File.Delete(tempExe); } catch { /* ignore */ }
            }
          }

          var compiledSuccessfully = compileError == null;
          if (compiledSuccessfully) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = "Expected compiler error but compilation succeeded",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
          }

          // Normalize and compare stderr exactly
          var expectedNorm = NormalizeStderr(errorExpectation.ExpectedStderr);
          var actualNorm = NormalizeStderr(compileError!);
          if (expectedNorm != actualNorm) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = $"Stderr mismatch.\nExpected:\n  {expectedNorm}\nActual:\n  {actualNorm}",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
          }

          return new TestResult {
            TestName = fragment.TestName,
            Passed = true,
            Duration = sw.Elapsed,
            FilePath = fragment.FilePath
          };
        }

        // Expect compilation to succeed
        if (compileError != null) {
          return new TestResult {
            TestName = fragment.TestName,
            Passed = false,
            ErrorMessage = $"Compilation failed: {compileError}",
            Duration = sw.Elapsed,
            FilePath = fragment.FilePath
          };
        }

        var successExpectation = (SuccessExpectation)fragment.Expectation;

        // Check Required MLIR by compiling fresh with all pipeline stages
        if (successExpectation.RequiredMLIR != null) {
          var irResult = new Compiler.Compiler().Compile(
            [new Compiler.SourceFile(fragment.FilePath, fragment.Source)],
            exePath, returnIr: true);
          if (!irResult.Success || irResult.AllStagesIr == null) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = "RequiredMLIR specified but compilation failed or produced no IR",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
          }

          var (Passed, Message) = CheckRequiredIr(successExpectation.RequiredMLIR, irResult.AllStagesIr);
          if (!Passed) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = $"Required MLIR mismatch: {Message}",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
          }
        }

        // Check Required Rdata
        if (successExpectation.RequiredRdata != null) {
          var (rdataPassed, rdataMessage) = CheckRequiredRdata(successExpectation.RequiredRdata, exePath);
          if (!rdataPassed) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = $"Required Rdata mismatch: {rdataMessage}",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
          }
        }

        // Run the executable if we have runtime expectations
        if (successExpectation.ExitCode.HasValue || successExpectation.Stdout != null) {
          var (ExitCode, Stdout, Stderr) = RunExecutable(exePath);

          if (successExpectation.ExitCode.HasValue && ExitCode != successExpectation.ExitCode.Value) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = $"Expected exit code {successExpectation.ExitCode.Value}, got {ExitCode}",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
          }

          if (successExpectation.Stdout != null) {
            var expectedStdout = successExpectation.Stdout.Trim();
            var actualStdout = Stdout.Trim();
            if (expectedStdout != actualStdout) {
              return new TestResult {
                TestName = fragment.TestName,
                Passed = false,
                ErrorMessage = $"Stdout mismatch:\nExpected: {expectedStdout}\nActual: {actualStdout}",
                Duration = sw.Elapsed,
                FilePath = fragment.FilePath
              };
            }
          }
        }

        return new TestResult {
          TestName = fragment.TestName,
          Passed = true,
          Duration = sw.Elapsed,
          FilePath = fragment.FilePath
        };
      } finally {
        // Cleanup temp file if we created one
        if (needsCleanup && File.Exists(exePath)) {
          try { File.Delete(exePath); } catch { /* ignore */ }
        }
      }
    } catch (Exception ex) {
      return new TestResult {
        TestName = fragment.TestName,
        Passed = false,
        ErrorMessage = $"Exception: {ex.Message}",
        Duration = sw.Elapsed,
        FilePath = fragment.FilePath
      };
    }
  }

  private static (bool Success, string? Error) CompileToExecutable(Fragment fragment, string outputPath) {
    try {
      var result = new Compiler.Compiler().Compile(
        [new Compiler.SourceFile(fragment.FilePath, fragment.Source)],
        outputPath);
      return (result.Success, result.Error);
    } catch (Exception ex) {
      return (false, ex.Message);
    }
  }

  private const int TestTimeoutMs = 1000;

  private static (int ExitCode, string Stdout, string Stderr) RunExecutable(string exePath) {
    var psi = new ProcessStartInfo {
      FileName = exePath,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    // Use a job object to ensure child processes are killed on timeout
    using var job = new WindowsJobObject();
    using var process = Process.Start(psi)!;

    // Assign process to job object for guaranteed cleanup
    job.AssignProcess(process.Handle);

    // Read stdout/stderr asynchronously to avoid deadlocks
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();

    bool exited = process.WaitForExit(TestTimeoutMs);
    if (!exited) {
      // Process timed out - kill it via job object (happens automatically on dispose)
      // but also explicitly kill to ensure we don't hang on ReadToEndAsync
      try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
      return (-1, "", "Process timed out");
    }

    // Process exited normally - wait for async reads to complete
    var stdout = stdoutTask.GetAwaiter().GetResult();
    var stderr = stderrTask.GetAwaiter().GetResult();

    return (process.ExitCode, stdout, stderr);
  }

  private static string NormalizeIr(string ir) {
    // Trim each line, remove empty lines, normalize line endings
    var lines = ir.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
      .Select(l => l.Trim())
      .Where(l => l.Length > 0);
    return string.Join("\n", lines);
  }

  private static (bool Passed, string? Message) CheckRequiredIr(string required, string actual) {
    // Normalize both for exact match
    var requiredNorm = NormalizeIr(required);
    var actualNorm = NormalizeIr(actual);

    if (actualNorm == requiredNorm) {
      return (true, null);
    }

    // Show what we expected vs what we got
    return (false, $"IR mismatch.\nExpected:\n{requiredNorm}\n\nActual:\n{actualNorm}");
  }

  /// <summary>
  /// Normalize stderr for comparison: CRLF -> LF, trim, backslash -> forward slash in paths.
  /// </summary>
  private static string NormalizeStderr(string stderr) {
    // Normalize line endings (CRLF -> LF)
    var normalized = stderr.Replace("\r\n", "\n");
    // Normalize path separators (backslash -> forward slash)
    normalized = normalized.Replace('\\', '/');
    // Trim whitespace
    return normalized.Trim();
  }

  // ============================================================================
  // RequiredRdata verification
  // ============================================================================

  private static (bool Passed, string? Message) CheckRequiredRdata(string requiredRdata, string exePath) {
    var expectedBytes = ParseTypedRdataValues(requiredRdata);
    if (expectedBytes == null) {
      return (false, "Failed to parse RequiredRdata typed values");
    }

    var sections = ParsePeSections(exePath);
    if (sections == null) {
      return (false, "Failed to parse PE sections");
    }

    var rdataSection = sections.FirstOrDefault(s => s.Name == ".rdata");
    if (rdataSection == null) {
      return (false, "PE does not contain .rdata section");
    }

    var actualBytes = ReadPeSectionData(exePath, rdataSection);
    if (actualBytes == null) {
      return (false, "Failed to read .rdata section data");
    }

    if (actualBytes.AsSpan().SequenceEqual(expectedBytes)) {
      return (true, null);
    }

    return (false, $"Rdata mismatch.\nExpected ({expectedBytes.Length} bytes): {FormatHex(expectedBytes)}\nActual ({actualBytes.Length} bytes): {FormatHex(actualBytes)}");
  }

  private static string FormatHex(byte[] data) {
    var sb = new StringBuilder(data.Length * 3);
    for (int i = 0; i < data.Length; i++) {
      if (i > 0) sb.Append(' ');
      sb.Append(data[i].ToString("x2"));
    }
    return sb.ToString();
  }

  /// <summary>
  /// Parses typed value lines into a concatenated byte array.
  /// </summary>
  private static byte[]? ParseTypedRdataValues(string block) {
    var result = new List<byte>();
    var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    foreach (var rawLine in lines) {
      var line = rawLine.Trim();
      if (line.Length == 0) continue;

      var spaceIdx = line.IndexOf(' ');
      if (spaceIdx < 0) return null;

      var type = line[..spaceIdx];
      var value = line[(spaceIdx + 1)..].Trim();

      switch (type) {
        case "f64": {
          if (!double.TryParse(value, CultureInfo.InvariantCulture, out var d)) return null;
          result.AddRange(BitConverter.GetBytes(d));
          break;
        }
        case "i64": {
          if (!long.TryParse(value, out var l)) return null;
          result.AddRange(BitConverter.GetBytes(l));
          break;
        }
        case "i64[]": {
          var parts = value.Split(',');
          foreach (var part in parts) {
            if (!long.TryParse(part.Trim(), out var l)) return null;
            result.AddRange(BitConverter.GetBytes(l));
          }
          break;
        }
        case "utf8": {
          if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return null;
          var str = ProcessEscapes(value[1..^1]);
          result.AddRange(Encoding.UTF8.GetBytes(str));
          break;
        }
        default:
          return null;
      }
    }

    return [.. result];
  }

  private static string ProcessEscapes(string input) {
    var sb = new StringBuilder(input.Length);
    for (int i = 0; i < input.Length; i++) {
      if (input[i] == '\\' && i + 1 < input.Length) {
        switch (input[i + 1]) {
          case '0': sb.Append('\0'); i++; break;
          case 'n': sb.Append('\n'); i++; break;
          case 't': sb.Append('\t'); i++; break;
          case '\\': sb.Append('\\'); i++; break;
          case '"': sb.Append('"'); i++; break;
          default: sb.Append(input[i]); break;
        }
      } else {
        sb.Append(input[i]);
      }
    }
    return sb.ToString();
  }

  // ============================================================================
  // PE parsing helpers
  // ============================================================================

  private sealed record PeSectionInfo(string Name, uint VirtualSize, uint VirtualAddress, uint RawSize, uint RawOffset, uint Characteristics);

  private static List<PeSectionInfo>? ParsePeSections(string exePath) {
    try {
      using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read);
      using var reader = new BinaryReader(fs);

      var dosMagic = reader.ReadUInt16();
      if (dosMagic != 0x5A4D) return null;

      fs.Position = 0x3C;
      var peOffset = reader.ReadUInt32();

      fs.Position = peOffset;
      var peSignature = reader.ReadUInt32();
      if (peSignature != 0x00004550) return null;

      reader.ReadUInt16(); // Machine
      var numberOfSections = reader.ReadUInt16();
      reader.ReadUInt32(); // TimeDateStamp
      reader.ReadUInt32(); // PointerToSymbolTable
      reader.ReadUInt32(); // NumberOfSymbols
      var sizeOfOptionalHeader = reader.ReadUInt16();
      reader.ReadUInt16(); // Characteristics

      fs.Position += sizeOfOptionalHeader;

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

  private static byte[]? ReadPeSectionData(string exePath, PeSectionInfo section) {
    try {
      using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read);
      fs.Position = section.RawOffset;
      // Read VirtualSize bytes (actual data) rather than RawSize (file-aligned, padded with zeros)
      var readSize = Math.Min(section.VirtualSize, section.RawSize);
      var data = new byte[readSize];
      fs.ReadExactly(data, 0, (int)readSize);
      return data;
    } catch {
      return null;
    }
  }

  /// <summary>
  /// Update RequiredMLIR sections in spec files with newly generated MLIR from fragments.
  /// </summary>
  private void UpdateRequiredMLIRInSpecFiles() {
    var specs = SpecParser.ParseDirectory(_specDir);
    var updatedSpecs = 0;

    foreach (var spec in specs) {
      var specContent = File.ReadAllText(spec.FilePath);
      var updated = false;

      foreach (var test in spec.Tests) {
        if (test.Expectation is SuccessExpectation success && success.RequiredMLIR != null) {
          // Compile the test to get the current IR
          var sources = new[] { new Compiler.SourceFile(spec.FilePath, test.Source) };
          var exePath = Path.Combine(_tempDir, $"{Path.GetFileNameWithoutExtension(spec.FilePath)}_{test.Name}_temp.exe");
          var irResult = new Compiler.Compiler().Compile(sources, exePath, returnIr: true);

          if (irResult.Success && irResult.AllStagesIr != null) {
            var newRequiredMLIR = irResult.AllStagesIr.Trim();
            var oldNorm = NormalizeIr(success.RequiredMLIR);
            var newNorm = NormalizeIr(newRequiredMLIR);
            if (oldNorm == newNorm) continue;

            // Find the test section by its marker, then replace the RequiredMLIR block within it
            var markerPattern = $@"<!--\s*test:\s*{Regex.Escape(test.Name)}\s*-->";
            var markerMatch = Regex.Match(specContent, markerPattern);
            if (!markerMatch.Success) continue;

            // Find the first RequiredMLIR block after this test marker
            var searchStart = markerMatch.Index + markerMatch.Length;
            var blockPattern = @"```RequiredMLIR\s*\n(.*?)```";
            var candidate = Regex.Match(specContent[searchStart..], blockPattern, RegexOptions.Singleline, TimeSpan.FromSeconds(5));
            if (!candidate.Success) continue;

            var absoluteStart = searchStart + candidate.Index;
            var absoluteEnd = absoluteStart + candidate.Length;
            var replacement = $"```RequiredMLIR\n{newRequiredMLIR}\n```";
            specContent = string.Concat(specContent.AsSpan(0, absoluteStart), replacement, specContent.AsSpan(absoluteEnd));
            updated = true;
            Logger.Debug(LogCategory.Testing, $"Updated RequiredMLIR for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}");
          }
        }
      }

      if (updated) {
        File.WriteAllText(spec.FilePath, specContent);
        updatedSpecs++;
      }
    }

    if (updatedSpecs > 0) {
      Logger.Info(LogCategory.Testing, $"Updated RequiredMLIR in {updatedSpecs} spec file(s)");
    }
  }
}
