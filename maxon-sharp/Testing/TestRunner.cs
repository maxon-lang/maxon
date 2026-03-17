using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Executes tests from fragment files.
/// </summary>
public class TestRunner(string specDir, string fragmentDir, string tempDir, string? filter = null, int? workers = null, bool updateRequired = false) {
  private readonly string _specDir = specDir;
  private readonly string _fragmentDir = fragmentDir;
  private readonly string _tempDir = tempDir;
  private readonly string? _filter = filter;
  private readonly int _workerCount = workers ?? Math.Max(1, Environment.ProcessorCount / 2);
  private readonly bool _updateRequired = updateRequired;

  /// <summary>
  /// Run all tests and return summary.
  /// Uses Zig-style worker threads with atomic work-stealing for maximum parallelism.
  /// Each worker handles the full pipeline: regenerate fragment → compile → run → check.
  /// </summary>
  public TestSummary RunAllSpecTests() {
    var sw = Stopwatch.StartNew();

    // Update required blocks in spec files if requested
    if (_updateRequired) {
      UpdateRequiredInSpecFiles();
    }

    // Prepare work items from specs (sequential — just parses specs and checks mtimes)
    var prepResult = FragmentGenerator.PrepareWorkItems(_specDir, _fragmentDir, _updateRequired, _filter);

    // Abort on errors (e.g., duplicate test names)
    if (prepResult.Errors.Count > 0) {
      foreach (var error in prepResult.Errors) {
        Logger.Error(LogCategory.Testing, error);
      }
      CleanupExecutables(_fragmentDir);
      return new TestSummary {
        Results = [],
        Passed = 0,
        Failed = 0,
        Total = 0,
        TotalDuration = sw.Elapsed,
        FragmentGenerationErrors = prepResult.Errors.Count
      };
    }

    var workItems = prepResult.WorkItems;
    if (workItems.Length == 0) {
      Logger.Info(LogCategory.Testing, "No tests found");
      return new TestSummary {
        Results = [],
        Passed = 0,
        Failed = 0,
        Total = 0,
        TotalDuration = sw.Elapsed
      };
    }

    // Ensure temp directory exists
    Directory.CreateDirectory(_tempDir);

    Logger.Info(LogCategory.Testing, $"Running {workItems.Length} test(s) with {_workerCount} worker(s)...");

    // Allocate results array (one slot per work item)
    var results = new TestResult[workItems.Length];
    var nextIndex = 0;
    var generatedCount = 0;
    var generationErrors = new ConcurrentBag<string>();
    var printLock = new object();

    // Spawn worker threads (Zig-style: explicit threads + atomic work-stealing)
    var threadCount = Math.Min(_workerCount, workItems.Length);
    var threads = new Thread[threadCount];
    for (int i = 0; i < threadCount; i++) {
      threads[i] = new Thread(() => {
        while (true) {
          var index = Interlocked.Increment(ref nextIndex) - 1;
          if (index >= workItems.Length) break;

          var item = workItems[index];
          results[index] = ProcessWorkItem(item, ref generatedCount, generationErrors);

          lock (printLock) {
            var root = Compiler.CompileError.ProjectRoot ?? Environment.CurrentDirectory;
            var testIdentifier = $"specs/fragments/{item.SpecName}/{item.TestName}.test";
            if (results[index].Passed) {
              Logger.Debug(LogCategory.Testing, $"[PASS] {testIdentifier}");
            } else {
              Logger.Error(LogCategory.Testing, $"[FAIL] {testIdentifier}");
              if (results[index].ErrorMessage != null) {
                Logger.Error(LogCategory.Testing, $"  {results[index].ErrorMessage}");
              }
            }
          }
        }
      }) { IsBackground = true };
      threads[i].Start();
    }

    // Wait for all workers to complete
    foreach (var t in threads) t.Join();

    sw.Stop();

    // Report generation errors
    foreach (var error in generationErrors) {
      Logger.Error(LogCategory.Testing, error);
    }

    if (generatedCount > 0) {
      Logger.Info(LogCategory.Testing, $"Generated {generatedCount} fragment(s)");
    }

    // Write spec count flag on successful unfiltered run
    if (_filter == null && generationErrors.IsEmpty) {
      FragmentGenerator.WriteSpecCountFlagPublic(_specDir, _fragmentDir);
    }

    // Clean up generated .exe files in fragment directories
    CleanupExecutables(_fragmentDir);

    var resultList = results.Where(r => r != null).ToList();
    var passed = resultList.Count(r => r.Passed);
    var failed = resultList.Count(r => !r.Passed);

    return new TestSummary {
      Results = resultList,
      Passed = passed,
      Failed = failed,
      Total = resultList.Count,
      TotalDuration = sw.Elapsed,
      FragmentGenerationErrors = generationErrors.Count
    };
  }

  /// <summary>
  /// Process a single work item: regenerate fragment if stale, then compile + run + check.
  /// </summary>
  private TestResult ProcessWorkItem(TestWorkItem item, ref int generatedCount, ConcurrentBag<string> generationErrors) {
    var testSw = Stopwatch.StartNew();

    try {
      // Step 1: Regenerate fragment if stale
      if (item.NeedsRegeneration) {
        // Fragment generation compiles for IR only — always disable MmTrace/AsyncTrace
        Compiler.Compiler.MmTrace = false;
        Compiler.Compiler.AsyncTrace = false;
        Compiler.Compiler.Testing = true;
        var absolutePath = Path.GetFullPath(item.FragmentPath);
        var (content, genError) = FragmentGenerator.GenerateFragmentContent(item.Test, item.ExePath, absolutePath);
        if (genError != null) {
          generationErrors.Add($"Error compiling 'specs/fragments/{item.SpecName}/{item.TestName}.test':\n{genError}");
        }
        File.WriteAllText(item.FragmentPath, content.Replace("\r\n", "\n").Replace("\r", "\n"));
        Interlocked.Increment(ref generatedCount);
      }

      // Step 2: Parse the fragment (fresh or existing)
      var fragment = FragmentGenerator.ParseFragment(item.FragmentPath);
      if (fragment == null) {
        return new TestResult {
          TestName = item.TestName,
          Passed = false,
          ErrorMessage = "Failed to parse fragment file",
          Duration = testSw.Elapsed,
          FilePath = item.FragmentPath
        };
      }

      // Step 3: Run the test (compile + execute + check expectations)
      return RunTest(fragment);
    } catch (Exception ex) {
      return new TestResult {
        TestName = item.TestName,
        Passed = false,
        ErrorMessage = $"Exception: {ex.Message}",
        Duration = testSw.Elapsed,
        FilePath = item.FragmentPath
      };
    }
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

      if (!fragment.MmTrace && !fragment.AsyncTrace && File.Exists(precompiledExe)) {
        var exeFile = new FileInfo(precompiledExe);
        if (exeFile.LastWriteTimeUtc >= fragmentFile.LastWriteTimeUtc) {
          // Use pre-compiled executable (only safe when MmTrace is off — we can't verify
          // a pre-compiled exe was built with --mm-trace, so always recompile trace tests)
          exePath = precompiledExe;
          needsCleanup = false;
        } else {
          // Exe is stale, compile fresh
          var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Guid.NewGuid():N}.exe");
          var (Success, Error) = CompileToExecutable(fragment, tempExe);
          compileError = Error;
          exePath = tempExe;
          needsCleanup = true;
        }
      } else {
        // No pre-compiled exe, compile fresh
        var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Guid.NewGuid():N}.exe");
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
            var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Guid.NewGuid():N}.exe");
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

        // Check Required MLIR by compiling fresh with all pipeline stages.
        // Use a dedicated temp exe so we never overwrite the runtime exe (exePath).
        if (successExpectation.RequiredMLIR != null) {
          Compiler.SourceFile[] irSources;
          string? irTempDir = null;
          if (fragment.SourceFiles != null) {
            irTempDir = Path.Combine(Path.GetTempPath(), $"maxon-ir-{Guid.NewGuid():N}");
            Directory.CreateDirectory(irTempDir);
            irSources = [.. fragment.SourceFiles.Select(f => {
              var path = Path.Combine(irTempDir, f.FileName);
              File.WriteAllText(path, f.Source);
              return new Compiler.SourceFile(path, f.Source);
            })];
          } else {
            irSources = [new Compiler.SourceFile(fragment.FilePath, fragment.Source)];
          }
          var irExePath = Path.Combine(_tempDir, $"{fragment.TestName}_{Guid.NewGuid():N}_ir.exe");
          Compiler.Compiler.MmTrace = false;
          Compiler.Compiler.MmDebug = false;
          Compiler.Compiler.AsyncTrace = false;
          Compiler.Compiler.Testing = true;
          var irResult = new Compiler.Compiler().Compile(irSources, irExePath, returnIr: true);
          if (irTempDir != null) {
            try { Directory.Delete(irTempDir, recursive: true); } catch { }
          }
          try { if (File.Exists(irExePath)) File.Delete(irExePath); } catch { }
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

        // Check Required Data (.data section)
        if (successExpectation.RequiredData != null) {
          var (dataPassed, dataMessage) = CheckRequiredData(successExpectation.RequiredData, exePath);
          if (!dataPassed) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = $"Required Data mismatch: {dataMessage}",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
          }
        }

        // Run the executable if we have runtime expectations
        if (successExpectation.ExitCode.HasValue || successExpectation.Stdout != null || successExpectation.Stderr != null) {
          var (ExitCode, Stdout, Stderr) = RunExecutable(exePath, _tempDir, fragment.Args);

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
            var expectedStdout = successExpectation.Stdout.Replace("\r\n", "\n").Trim();
            var actualStdout = Stdout.Replace("\r\n", "\n").Trim();
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

          if (successExpectation.Stderr != null) {
            var normalize = fragment.AsyncTrace ? NormalizeAsyncTraceStderr : (Func<string, string>)(s => s.Replace("\r\n", "\n").Trim());
            var expectedStderr = normalize(successExpectation.Stderr);
            var actualStderr = normalize(Stderr);
            if (expectedStderr != actualStderr) {
              return new TestResult {
                TestName = fragment.TestName,
                Passed = false,
                ErrorMessage = $"Stderr mismatch:\nExpected: {expectedStderr}\nActual: {actualStderr}",
                Duration = sw.Elapsed,
                FilePath = fragment.FilePath
              };
            }
          } else if (!string.IsNullOrWhiteSpace(Stderr)) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = $"Unexpected stderr output:\n{Stderr.Trim()}",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
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
      Compiler.SourceFile[] sources;
      string? tempDir = null;

      if (fragment.SourceFiles != null) {
        tempDir = Path.Combine(Path.GetTempPath(), $"maxon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        sources = [.. fragment.SourceFiles.Select(f => {
          var path = Path.Combine(tempDir, f.FileName);
          File.WriteAllText(path, f.Source);
          return new Compiler.SourceFile(path, f.Source);
        })];
      } else {
        sources = [new Compiler.SourceFile(fragment.FilePath, fragment.Source)];
      }

      try {
        Compiler.Compiler.MmTrace = fragment.MmTrace;
        Compiler.Compiler.AsyncTrace = fragment.AsyncTrace;
        Compiler.Compiler.Testing = true;
        var result = new Compiler.Compiler().Compile(sources, outputPath);
        var error = result.Error;
        // Normalize temp directory paths to just filenames for multi-file tests
        if (error != null && tempDir != null) {
          var root = Compiler.CompileError.ProjectRoot ?? Environment.CurrentDirectory;
          var relativeTempDir = Path.GetRelativePath(root, tempDir).Replace('\\', '/');
          if (!relativeTempDir.EndsWith('/')) relativeTempDir += '/';
          error = error.Replace(relativeTempDir, "");
        }
        return (result.Success, error);
      } finally {
        if (tempDir != null) {
          try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
      }
    } catch (Exception ex) {
      return (false, ex.Message);
    }
  }

  private const int TestTimeoutMs = 1000;

  private static (int ExitCode, string Stdout, string Stderr) RunExecutable(string exePath, string workingDirectory, string? args = null) {
    var psi = new ProcessStartInfo {
      FileName = exePath,
      Arguments = args ?? "",
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
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
    // Suppressing VSTHRD002: This runs in Parallel.ForEach worker threads, not on a UI thread
#pragma warning disable VSTHRD002
    var stdout = stdoutTask.GetAwaiter().GetResult();
    var stderr = stderrTask.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

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
    var normalized = stderr.Replace("\r\n", "\n");
    normalized = normalized.Replace('\\', '/');
    return normalized.Trim();
  }

  // Matches the worker-suffix appended by --async-trace-workers: " [M=N]" at end of line.
  private static readonly Regex AsyncWorkerSuffix = new(@" \[M=\d+\]$", RegexOptions.Multiline);

  // Lines emitted by the worker lifecycle tracer that don't exist in the stable trace output.
  private static readonly HashSet<string> AsyncWorkerOnlyPrefixes = new(StringComparer.Ordinal) {
    "worker_start", "worker_park", "worker_wake", "worker_exit"
  };

  /// <summary>
  /// Normalize async trace stderr for comparison: strip non-deterministic worker lifecycle
  /// lines (worker_start/park/wake/exit) and [M=N] worker suffixes so tests are stable
  /// regardless of scheduling timing.
  /// </summary>
  private static string NormalizeAsyncTraceStderr(string stderr) {
    var lines = stderr.Replace("\r\n", "\n").Split('\n');
    var kept = new List<string>();
    foreach (var raw in lines) {
      var line = AsyncWorkerSuffix.Replace(raw.TrimEnd(), "");
      var prefix = line.Contains(' ') ? line[..line.IndexOf(' ')] : line;
      if (!AsyncWorkerOnlyPrefixes.Contains(prefix))
        kept.Add(line);
    }
    return string.Join('\n', kept).Trim();
  }

  // ============================================================================
  // PE section content verification
  // ============================================================================

  private static (bool Passed, string? Message) CheckRequiredRdata(string requiredRdata, string exePath) =>
    CheckRequiredSection(requiredRdata, exePath, ".rdata");

  private static (bool Passed, string? Message) CheckRequiredData(string requiredData, string exePath) =>
    CheckRequiredSection(requiredData, exePath, ".data");

  private static (bool Passed, string? Message) CheckRequiredSection(string requiredContent, string exePath, string sectionName) {
    var expectedBytes = ParseTypedSectionValues(requiredContent);
    if (expectedBytes == null) {
      return (false, $"Failed to parse typed values for {sectionName}");
    }

    var sections = ParsePeSections(exePath);
    if (sections == null) {
      return (false, "Failed to parse PE sections");
    }

    var section = sections.FirstOrDefault(s => s.Name == sectionName);
    if (section == null) {
      return (false, $"PE does not contain {sectionName} section");
    }

    var actualBytes = ReadPeSectionData(exePath, section);
    if (actualBytes == null) {
      return (false, $"Failed to read {sectionName} section data");
    }

    // The data section may contain additional runtime globals (e.g., green thread
    // scheduler state) after the user data. Check that the user data forms a prefix.
    if (actualBytes.Length >= expectedBytes.Length &&
        actualBytes.AsSpan(0, expectedBytes.Length).SequenceEqual(expectedBytes)) {
      return (true, null);
    }

    return (false, $"{sectionName} mismatch.\nExpected ({expectedBytes.Length} bytes): {FormatHex(expectedBytes)}\nActual ({actualBytes.Length} bytes): {FormatHex(actualBytes)}");
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
  private static byte[]? ParseTypedSectionValues(string block) {
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
        case "f32": {
          if (!float.TryParse(value, CultureInfo.InvariantCulture, out var f)) return null;
          result.AddRange(BitConverter.GetBytes(f));
          break;
        }
        case "i8": {
          if (!sbyte.TryParse(value, out var sb)) return null;
          result.Add((byte)sb);
          break;
        }
        case "i16": {
          if (!short.TryParse(value, out var s)) return null;
          result.AddRange(BitConverter.GetBytes(s));
          break;
        }
        case "u16": {
          var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
          foreach (var part in parts) {
            if (!ushort.TryParse(part.Trim(), out var u)) return null;
            result.AddRange(BitConverter.GetBytes(u));
          }
          break;
        }
        case "i32": {
          if (!int.TryParse(value, out var n)) return null;
          result.AddRange(BitConverter.GetBytes(n));
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
        case "i8[]": {
          var parts = value.Split(',');
          foreach (var part in parts) {
            if (!sbyte.TryParse(part.Trim(), out var sb)) return null;
            result.Add((byte)sb);
          }
          break;
        }
        case "utf8": {
          if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return null;
          try {
            var str = StringUtils.ResolveEscapes(value[1..^1]);
            result.AddRange(Encoding.UTF8.GetBytes(str));
          } catch (InvalidEscapeException) {
            return null;
          }
          break;
        }
        case "pad": {
          if (!int.TryParse(value, out var count) || count < 0) return null;
          for (var j = 0; j < count; j++) result.Add(0);
          break;
        }
        default:
          return null;
      }
    }

    return [.. result];
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
  /// Update required blocks (RequiredMLIR, MmTrace stderr) in spec files with freshly generated output.
  /// </summary>
  private void UpdateRequiredInSpecFiles() {
    var specs = SpecParser.ParseDirectory(_specDir);
    var updatedSpecs = 0;

    Directory.CreateDirectory(_tempDir);

    foreach (var spec in specs) {
      var specContent = File.ReadAllText(spec.FilePath);
      var updated = false;
      var specName = Path.GetFileNameWithoutExtension(spec.FilePath);

      foreach (var test in spec.Tests) {
        if (test.Expectation is not SuccessExpectation success) continue;

        var fragmentPath = Path.GetFullPath(Path.Combine(_fragmentDir, specName, $"{test.Name}.test"));
        var sourceWithComment = $"// Test: {test.Name}\n{test.Source}";
        var sources = new[] { new Compiler.SourceFile(fragmentPath, sourceWithComment) };

        // Find the test marker once (shared by both RequiredMLIR and stderr updates)
        var markerPattern = $@"<!--\s*test:\s*{Regex.Escape(test.Name)}\s*-->";
        var markerMatch = Regex.Match(specContent, markerPattern);
        if (!markerMatch.Success) continue;

        // Update RequiredMLIR
        if (success.RequiredMLIR != null) {
          var exePath = Path.Combine(_tempDir, $"{specName}_{test.Name}_ir.exe");
          try {
            Compiler.Compiler.MmTrace = false;
            Compiler.Compiler.MmDebug = false;
            Compiler.Compiler.AsyncTrace = false;
            Compiler.Compiler.Testing = true;
            var irResult = new Compiler.Compiler().Compile(sources, exePath, returnIr: true);

            if (irResult.Success && irResult.AllStagesIr != null) {
              var newRequiredMLIR = irResult.AllStagesIr.Trim();
              var oldNorm = NormalizeIr(success.RequiredMLIR);
              var newNorm = NormalizeIr(newRequiredMLIR);
              if (oldNorm != newNorm) {
                var searchStart = markerMatch.Index + markerMatch.Length;
                var blockPattern = @"```RequiredMLIR\s*\n(.*?)```";
                var candidate = Regex.Match(specContent[searchStart..], blockPattern, RegexOptions.Singleline, TimeSpan.FromSeconds(5));
                if (candidate.Success) {
                  var absoluteStart = searchStart + candidate.Index;
                  var absoluteEnd = absoluteStart + candidate.Length;
                  var replacement = $"```RequiredMLIR\n{newRequiredMLIR}\n```";
                  specContent = string.Concat(specContent.AsSpan(0, absoluteStart), replacement, specContent.AsSpan(absoluteEnd));
                  updated = true;
                  Logger.Debug(LogCategory.Testing, $"Updated RequiredMLIR for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}");
                }
              }
            }
          } finally {
            try { if (File.Exists(exePath)) File.Delete(exePath); } catch { }
          }
        }

        // Update MmTrace stderr
        if (test.MmTrace && success.Stderr != null) {
          var exePath = Path.Combine(_tempDir, $"{specName}_{test.Name}_stderr.exe");
          try {
            Compiler.Compiler.MmTrace = true;
            Compiler.Compiler.MmDebug = false;
            Compiler.Compiler.Testing = true;
            var result = new Compiler.Compiler().Compile(sources, exePath);

            if (result.Success) {
              var (_, _, actualStderr) = RunExecutable(exePath, _tempDir);
              var oldStderr = success.Stderr.Replace("\r\n", "\n").Trim();
              var newStderr = actualStderr.Replace("\r\n", "\n").Trim();
              if (oldStderr != newStderr) {
                // Re-find marker since specContent may have shifted from RequiredMLIR update
                var markerMatch2 = Regex.Match(specContent, markerPattern);
                if (markerMatch2.Success) {
                  var searchStart = markerMatch2.Index + markerMatch2.Length;
                  var blockPattern = @"```stderr\s*\n(.*?)```";
                  var candidate = Regex.Match(specContent[searchStart..], blockPattern, RegexOptions.Singleline, TimeSpan.FromSeconds(5));
                  if (candidate.Success) {
                    var absoluteStart = searchStart + candidate.Index;
                    var absoluteEnd = absoluteStart + candidate.Length;
                    var replacement = $"```stderr\n{newStderr}\n```";
                    specContent = string.Concat(specContent.AsSpan(0, absoluteStart), replacement, specContent.AsSpan(absoluteEnd));
                    updated = true;
                    Logger.Debug(LogCategory.Testing, $"Updated stderr for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}");
                  }
                }
              }
            }
          } finally {
            try { if (File.Exists(exePath)) File.Delete(exePath); } catch { }
          }
        }
      }

      if (updated) {
        File.WriteAllText(spec.FilePath, specContent.Replace("\r\n", "\n"));
        updatedSpecs++;
      }
    }

    if (updatedSpecs > 0) {
      Logger.Info(LogCategory.Testing, $"Updated required blocks in {updatedSpecs} spec file(s)");
    }
  }
}
