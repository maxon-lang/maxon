using System.Diagnostics;
using System.Text;
using MaxonSharp.Compiler;
using MaxonSharp.Lsp;
using MaxonSharp.Testing;

namespace MaxonSharp;

class Program {
  static async Task<int> Main(string[] args) {
    Console.OutputEncoding = Encoding.UTF8;
    Console.InputEncoding = Encoding.UTF8;

    if (args.Length == 0) {
      PrintUsage();
      return 1;
    }

    var command = args[0];

    return command switch {
      "build" => RunBuild(args[1..]),
      "run" => RunRun(args[1..]),
      "fmt" => RunFmt(args[1..]),
      "monitor" => RunMonitor(args[1..]),
      "spec-test" => RunSpecTests(args[1..]),
      "lsp-server" => await RunLspAsync(),
      _ => Fail()
    };
  }

  static void PrintUsage() {
    Console.WriteLine("Usage: maxon <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  build [file|directory]   Compile a .maxon file or project directory");
    Console.WriteLine("  run <function>           Compile build.maxon and run the specified function");
    Console.WriteLine("  fmt [<file|directory>]   Format .maxon source files in-place (default: current directory)");
    Console.WriteLine("  monitor <exe> [args...]  Launch executable with shared-memory debug stream monitor");
    Console.WriteLine("  spec-test [options]      Run spec tests");
    Console.WriteLine("  lsp-server               Start language server (LSP)");
    Console.WriteLine();
    Console.WriteLine("Build options (build, run):");
    Console.WriteLine("  --target=ARCH-OS         Set compilation target (default: x64-windows)");
    Console.WriteLine("                           Examples: x64-windows, arm64-macos, x64-linux");
    Console.WriteLine("  --emit-ir                Write .ir file");
    Console.WriteLine("  --dump-stages            Write IR at each pipeline stage (.1-maxon.ir, etc.)");
    Console.WriteLine("  --mm-trace               Enable runtime memory manager trace output (stderr)");
    Console.WriteLine("  --mm-debug               Enable runtime memory debug checks (magic, canary, poison)");
    Console.WriteLine("  --async-trace            Enable async/await runtime trace output (stderr)");
    Console.WriteLine("  --debugstream            Enable shared-memory debug stream (use with 'maxon monitor')");
    Console.WriteLine();
    Console.WriteLine("Spec test options:");
    Console.WriteLine("  --filter=PATTERN         Run only tests matching pattern");
    Console.WriteLine("  --update-required        Force regeneration and update RequiredIR + MmTrace stderr");
    Console.WriteLine("  --verbose                Show detailed failure messages for failing tests");
    Console.WriteLine();
    Console.WriteLine("Logging (all commands):");
    Console.WriteLine("  --log=LEVEL              Set all log categories to LEVEL");
    Console.WriteLine("  --log=CATEGORY:LEVEL     Set specific category to LEVEL");
    Console.WriteLine();
    Console.WriteLine("Log levels: none, error, info, debug, trace");
    Console.WriteLine("Log categories: compiler, lexer, parser, semantic, hir, lir, optimizer, codegen, pe, testing");
    Console.WriteLine();
    Console.WriteLine("Testing log levels:");
    Console.WriteLine("  info   - Show failures and summary only");
    Console.WriteLine("  debug  - Also show each passing test");
  }

  static int Fail() {
    PrintUsage();
    return 1;
  }

  static (bool emitIr, bool dumpStages, bool valid) ParseOptions(string[] args, HashSet<string>? additionalOptions = null) {
    var emitIr = false;
    var dumpStages = false;

    foreach (var arg in args) {
      if (arg == "--emit-ir") {
        emitIr = true;
      } else if (arg == "--dump-stages") {
        dumpStages = true;
      } else if (arg == "--mm-trace") {
        Compiler.Compiler.MmTrace = true;
      } else if (arg == "--mm-debug") {
        Compiler.Compiler.MmDebug = true;
      } else if (arg == "--async-trace") {
        Compiler.Compiler.AsyncTrace = true;
      } else if (arg == "--debugstream") {
        Compiler.Compiler.DebugStream = true;
      } else if (arg.StartsWith("--target=")) {
        // Recognized as first-class option; parsed individually in each command
      } else if (arg.StartsWith("--log=")) {
        if (!Logger.ParseOption(arg["--log=".Length..])) {
          return (false, false, false);
        }
      } else if (arg.StartsWith('-')) {
        var recognized = false;
        if (additionalOptions != null) {
          foreach (var opt in additionalOptions) {
            if (opt.EndsWith('=') ? arg.StartsWith(opt) : arg == opt) {
              recognized = true;
              break;
            }
          }
        }
        if (!recognized) {
          return (false, false, false);
        }
      }
    }

    return (emitIr, dumpStages, true);
  }

  static Compiler.CompileTarget ParseTarget(string[] args) {
    foreach (var arg in args) {
      if (arg.StartsWith("--target=")) {
        return Compiler.CompileTarget.Parse(arg["--target=".Length..]);
      }
    }
    return Compiler.CompileTarget.Default;
  }

  static string GetOutputExtension(Compiler.CompileTarget target) {
    return target.Os.ToLowerInvariant() switch {
      "windows" => ".exe",
      "macos" => "",
      "linux" => "",
      var unknown => throw new ArgumentException($"Unknown OS '{unknown}' for output extension. Expected windows, macos, or linux.")
    };
  }

  static int RunBuild(string[] args) {
    var (emitIr, dumpStages, valid) = ParseOptions(args);
    if (!valid) return Fail();

    var target = ParseTarget(args);
    var path = GetNonOptionArg(args) ?? Directory.GetCurrentDirectory();
    var useCache = !emitIr && !dumpStages;

    if (File.Exists(path)) {
      // Single file: compile directly
      var content = ReadFileContentUntilSeparator(path);
      var ext = GetOutputExtension(target);
      var outputPath = ResolveOutputPath(path, ext);
      var fileSources = new SourceFile[] { new(path, content) };
      var projectDir = Path.GetDirectoryName(Path.GetFullPath(path))!;

      if (useCache && BuildCache.IsCacheValid(projectDir, fileSources, outputPath, target)) {
        Console.WriteLine($"Compiled -> {outputPath} (cached)");
        return 0;
      }

      var (irOutputPath, dumpStagesBasePath) = GetOutputPaths(path, emitIr, dumpStages);
      var result = CompileAndReportResult(fileSources, outputPath, irOutputPath, dumpStagesBasePath, target);
      if (result == 0 && useCache) BuildCache.WriteCache(projectDir, fileSources, outputPath, target);
      return result;
    }

    if (!Directory.Exists(path)) {
      Console.Error.WriteLine($"File or directory not found: {path}");
      return 1;
    }

    // Directory: check for build.maxon with a build() function
    var buildFile = Path.Combine(path, "build.maxon");
    if (File.Exists(buildFile)) {
      var buildContent = ReadFileContentUntilSeparator(buildFile);
      if (HasMainFunction(buildContent)) {
        Console.Error.WriteLine("build.maxon must not contain a main() function.");
        return 1;
      }
      var exportedFunctions = ListBuildFunctions(buildContent);
      if (exportedFunctions.Any(f => f.name == "build")) {
        var ext = GetOutputExtension(target);

        var projectSources = CollectFilesFromDirectory(path);
        if (projectSources.Length == 0) {
          Console.Error.WriteLine($"No .maxon files found in: {path}");
          return 1;
        }

        // Check project cache (includes build.maxon to detect config changes)
        var allSources = new SourceFile[] { new(buildFile, buildContent) }.Concat(projectSources).ToArray();
        if (useCache && BuildCache.IsCacheValid(path, allSources, null, target)) {
          var cachedOutput = BuildCache.GetCachedOutputPath(path);
          if (cachedOutput != null) {
            Console.WriteLine($"Compiled -> {cachedOutput} (cached)");
            return 0;
          }
        }

        // Cache build.maxon → .maxon-run.exe separately (only depends on build.maxon + compiler)
        var buildSources = new SourceFile[] { new(buildFile, buildContent) };
        BuildCache.EnsureCacheDir(path);
        var runPath = Path.Combine(BuildCache.GetCacheDir(path), $".maxon-run{ext}");

        if (!(useCache && BuildCache.IsCacheValid(path, buildSources, runPath, target, name: "build-runner"))) {
          var (irOutputPath, dumpStagesBasePath) = GetOutputPaths(buildFile, emitIr, dumpStages);
          var compileResult = CompileAndReportResult(buildSources, runPath, irOutputPath,
              dumpStagesBasePath, target, entryFunction: "build");
          if (compileResult != 0) return compileResult;
          if (useCache) BuildCache.WriteCache(path, buildSources, runPath, target, name: "build-runner");
        }

        var (exitCode, json) = RunExecutableCapture(runPath);
        if (exitCode != 0) return exitCode;

#pragma warning disable CA1869 // Cache and reuse 'JsonSerializerOptions' instances
        var config = System.Text.Json.JsonSerializer.Deserialize<BuildConfig>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
#pragma warning restore CA1869 // Cache and reuse 'JsonSerializerOptions' instances
        if (config == null) {
          Console.Error.WriteLine("build.maxon produced invalid build configuration.");
          return 1;
        }

        string outputPath;
        if (!string.IsNullOrEmpty(config.Output)) {
          outputPath = Path.Combine(path, config.Output);
        } else if (!string.IsNullOrEmpty(config.Name)) {
          outputPath = Path.Combine(path, config.Name + ext);
        } else {
          outputPath = Path.Combine(path, "output" + ext);
        }

        var outputDir = Path.GetDirectoryName(outputPath);
        if (outputDir != null && !Directory.Exists(outputDir))
          Directory.CreateDirectory(outputDir);

        var (irOut, dumpBase) = GetOutputPaths(buildFile, emitIr, dumpStages);
        var result = CompileAndReportResult(projectSources, outputPath, irOut,
            dumpBase, target);
        if (result == 0 && useCache) BuildCache.WriteCache(path, allSources, outputPath, target);
        return result;
      }
    }

    // No build.maxon or no build() function: compile all files in directory
    var sources = CollectFilesFromDirectory(path);
    if (sources.Length == 0) {
      Console.Error.WriteLine($"No .maxon files found in: {path}");
      return 1;
    }
    var mainFile = FindMainFile(sources, path);
    {
      var ext = GetOutputExtension(target);
      var outputPath = ResolveOutputPath(mainFile, ext);

      if (useCache && BuildCache.IsCacheValid(path, sources, outputPath, target)) {
        Console.WriteLine($"Compiled -> {outputPath} (cached)");
        return 0;
      }

      var (irOutputPath, dumpStagesBasePath) = GetOutputPaths(mainFile, emitIr, dumpStages);
      var result = CompileAndReportResult(sources, outputPath, irOutputPath, dumpStagesBasePath, target);
      if (result == 0 && useCache) BuildCache.WriteCache(path, sources, outputPath, target);
      return result;
    }
  }

  record BuildConfig {
    public string? Name { get; init; }
    public string? Output { get; init; }
    public string[]? Sources { get; init; }
    public bool Optimize { get; init; }
    public bool Debuag_info { get; init; }
  }

  static (int exitCode, string stdout) RunExecutableCapture(string executablePath) {
    var process = new Process {
      StartInfo = new ProcessStartInfo {
        FileName = Path.GetFullPath(executablePath),
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
      }
    };

    process.Start();

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();

    process.WaitForExit();

    if (!string.IsNullOrEmpty(stderr)) {
      Console.Error.Write(stderr);
    }

    return (process.ExitCode, stdout);
  }

  static int RunRun(string[] args) {
    var (emitIr, dumpStages, valid) = ParseOptions(args);
    if (!valid) return Fail();

    var target = ParseTarget(args);
    var cliName = GetNonOptionArg(args);
    // Translate dashes to underscores so CLI uses dashes but Maxon uses underscores
    var functionName = cliName?.Replace('-', '_');

    var directory = Directory.GetCurrentDirectory();
    var buildFile = Path.Combine(directory, "build.maxon");
    if (!File.Exists(buildFile)) {
      Console.Error.WriteLine("No build.maxon found in current directory.");
      return 1;
    }

    var content = ReadFileContentUntilSeparator(buildFile);

    if (HasMainFunction(content)) {
      Console.Error.WriteLine("build.maxon must not contain a main() function.");
      return 1;
    }

    var exportedFunctions = ListBuildFunctions(content);

    if (functionName == null) {
      if (exportedFunctions.Count == 0) {
        Console.Error.WriteLine("No exported functions found in build.maxon.");
      } else {
        PrintAvailableCommands(exportedFunctions);
      }
      return 1;
    }

    // Validate that the requested function exists before compiling
    var allFunctions = ListBuildFunctions(content, exportedOnly: false);
    var isKnown = allFunctions.Any(f => f.name == functionName);
    var isExported = exportedFunctions.Any(f => f.name == functionName);

    if (!isKnown) {
      Console.Error.WriteLine($"Unknown command '{cliName}'.");
      if (exportedFunctions.Count > 0) {
        Console.Error.WriteLine();
        PrintAvailableCommands(exportedFunctions, Console.Error);
      }
      return 1;
    }

    if (!isExported) {
      Console.Error.WriteLine($"Function '{cliName}' is not exported in build.maxon.");
      return 1;
    }

    var sources = new SourceFile[] { new(buildFile, content) };

    var ext = GetOutputExtension(target);
    var useCache = !emitIr && !dumpStages;
    BuildCache.EnsureCacheDir(directory);
    var outputPath = Path.Combine(BuildCache.GetCacheDir(directory), $".maxon-run-{functionName}{ext}");
    var cacheName = $"run-{functionName}";

    if (useCache && BuildCache.IsCacheValid(directory, sources, outputPath, target, name: cacheName)) {
      Console.WriteLine($"Using cached build runner for '{cliName}'");
    } else {
      var (irOutputPath, dumpStagesBasePath) = GetOutputPaths(buildFile, emitIr, dumpStages);
      var compileResult = CompileAndReportResult(sources, outputPath, irOutputPath,
          dumpStagesBasePath, target, entryFunction: functionName);
      if (compileResult != 0) return compileResult;
      if (useCache) BuildCache.WriteCache(directory, sources, outputPath, target, name: cacheName);
    }

    return RunExecutable(outputPath);
  }

  static int RunFmt(string[] args) {
    var path = args.FirstOrDefault(a => !a.StartsWith('-')) ?? Directory.GetCurrentDirectory();

    List<string> files;
    if (File.Exists(path)) {
      files = [path];
    } else if (Directory.Exists(path)) {
      files = [.. Directory.GetFiles(path, "*.maxon", SearchOption.AllDirectories)
        .Where(f => !Path.GetFileName(f).Equals("build.maxon", StringComparison.OrdinalIgnoreCase))
        .Where(f => !MaxonIgnore.IsIgnored(f))];
    } else {
      Console.Error.WriteLine($"fmt: path not found: {path}");
      return 1;
    }

    int changed = 0;
    foreach (var file in files) {
      var original = File.ReadAllText(file);
      var formatted = Lsp.MaxonFormatter.Format(original);
      if (formatted != original) {
        File.WriteAllText(file, formatted);
        Console.WriteLine($"formatted: {file}");
        changed++;
      }
    }

    Console.WriteLine($"fmt: {changed} file(s) changed, {files.Count - changed} unchanged.");
    return 0;
  }

  /// <summary>
  /// Extracts top-level function names from build.maxon content.
  /// Returns (name, comment) pairs where comment is the preceding // comment line, if any.
  /// </summary>
  static void PrintAvailableCommands(List<(string name, string? comment)> functions, TextWriter? writer = null) {
    writer ??= Console.Out;
    writer.WriteLine("Available commands (from build.maxon):");
    writer.WriteLine();
    foreach (var (name, comment) in functions) {
      var displayName = name.Replace('_', '-');
      if (comment != null)
        writer.WriteLine($"  {displayName,-24}{comment}");
      else
        writer.WriteLine($"  {displayName}");
    }
  }

  static bool HasMainFunction(string content) {
    foreach (var rawLine in content.Split('\n')) {
      var line = rawLine.Trim();
      if (line.StartsWith("function main(") || line.StartsWith("export function main("))
        return true;
    }
    return false;
  }

  /// <summary>
  /// Extracts top-level function names from build.maxon content.
  /// Returns (name, comment) pairs where comment is the preceding // comment line, if any.
  /// </summary>
  static List<(string name, string? comment)> ListBuildFunctions(string content, bool exportedOnly = true) {
    var results = new List<(string name, string? comment)>();
    var lines = content.Split('\n');
    string? lastComment = null;

    foreach (var rawLine in lines) {
      var line = rawLine.Trim();
      if (line.StartsWith("//")) {
        lastComment = line[2..].Trim();
      } else if (line.StartsWith("export function ") || (!exportedOnly && line.StartsWith("function "))) {
        var rest = line.StartsWith("export function ")
          ? line["export function ".Length..]
          : line["function ".Length..];
        var parenIndex = rest.IndexOf('(');
        if (parenIndex > 0) {
          var name = rest[..parenIndex].Trim();
          results.Add((name, lastComment));
        }
        lastComment = null;
      } else if (line.Length > 0) {
        lastComment = null;
      }
    }

    return results;
  }

  static int RunMonitor(string[] args) {
    return DebugStreamMonitor.Run(args);
  }

  /// <summary>
  /// Reads file content up to the first "---" separator line.
  /// </summary>
  static string ReadFileContentUntilSeparator(string filePath) {
    var content = File.ReadAllText(filePath);
    var lines = content.Split('\n');
    var sourceLines = new List<string>();
    foreach (var line in lines) {
      if (line.Trim() == "---") {
        break;
      }
      sourceLines.Add(line);
    }
    return string.Join('\n', sourceLines);
  }

  /// <summary>
  /// Recursively collects all .maxon files from a directory.
  /// </summary>
  static SourceFile[] CollectFilesFromDirectory(string directory) {
    var files = new List<SourceFile>();

    foreach (var file in Directory.GetFiles(directory, "*.maxon", SearchOption.AllDirectories)) {
      if (Path.GetFileName(file).Equals("build.maxon", StringComparison.OrdinalIgnoreCase))
        continue;
      if (MaxonIgnore.IsIgnored(file))
        continue;
      var content = ReadFileContentUntilSeparator(file);
      files.Add(new SourceFile(file, content));
    }

    return [.. files];
  }

  /// <summary>
  /// Finds the main file (containing main function) or uses the originally specified file.
  /// </summary>
  static string FindMainFile(SourceFile[] files, string originalPath) {
    if (File.Exists(originalPath))
      return originalPath;

    foreach (var file in files) {
      if (file.Content.Contains("function main"))
        return file.Path;
    }

    foreach (var file in files) {
      if (Path.GetFileName(file.Path).Equals("main.maxon", StringComparison.OrdinalIgnoreCase))
        return file.Path;
    }

    return files.Length > 0 ? files[0].Path : originalPath;
  }

  static string ResolveOutputPath(string mainFile, string ext) {
    return Path.ChangeExtension(mainFile, ext == "" ? null : ext);
  }

  /// <summary>
  /// Gets the first non-option argument from the args array.
  /// </summary>
  static string? GetNonOptionArg(string[] args) {
    foreach (var arg in args) {
      if (!arg.StartsWith('-')) {
        return arg;
      }
    }
    return null;
  }

  /// <summary>
  /// Gets output paths for IR and dump stages based on flags.
  /// </summary>
  static (string? irOutputPath, string? dumpStagesBasePath) GetOutputPaths(string mainFile, bool emitIr, bool dumpStages) {
    string? irOutputPath = null;
    if (emitIr) {
      irOutputPath = Path.ChangeExtension(mainFile, ".ir");
    }

    string? dumpStagesBasePath = null;
    if (dumpStages) {
      dumpStagesBasePath = Path.ChangeExtension(mainFile, null);
    }

    return (irOutputPath, dumpStagesBasePath);
  }

  /// <summary>
  /// Compiles source files and reports the result.
  /// </summary>
  static int CompileAndReportResult(SourceFile[] sources, string outputPath, string? irOutputPath, string? dumpStagesBasePath, Compiler.CompileTarget? target = null, string entryFunction = "main") {
    var result = new Compiler.Compiler().Compile(sources, outputPath, irOutputPath, dumpStagesBasePath: dumpStagesBasePath, target: target, entryFunction: entryFunction);
    if (!result.Success) {
      foreach (var error in result.Errors)
        Logger.Error(LogCategory.Compiler, error.Format());
    }
    return result.Success ? 0 : 1;
  }

  /// <summary>
  /// Runs a compiled executable and returns its exit code.
  /// </summary>
  static int RunExecutable(string executablePath) {
    var process = new Process {
      StartInfo = new ProcessStartInfo {
        FileName = Path.GetFullPath(executablePath),
        UseShellExecute = false,
      }
    };

    process.Start();
    process.WaitForExit();

    return process.ExitCode;
  }

  static int RunSpecTests(string[] args) {
    SetupTestLogging();

    var specTestOptions = new HashSet<string> { "--filter=", "--workers=", "--update-required", "--target=", "--verbose" };
    var (_, _, valid) = ParseOptions(args, specTestOptions);
    if (!valid) return Fail();

    string? filter = null;
    int? workers = null;
    bool updateRequired = false;
    bool verbose = false;
    Compiler.CompileTarget? target = null;

    foreach (var arg in args) {
      if (arg.StartsWith("--filter=")) {
        filter = arg["--filter=".Length..];
      } else if (arg.StartsWith("--workers=")) {
        if (int.TryParse(arg["--workers=".Length..], out var w)) {
          workers = w;
        } else {
          return Fail();
        }
      } else if (arg == "--update-required") {
        updateRequired = true;
      } else if (arg == "--verbose") {
        verbose = true;
      } else if (arg.StartsWith("--target=")) {
        target = Compiler.CompileTarget.Parse(arg["--target=".Length..]);
      }
    }

    target ??= Compiler.CompileTarget.Default;

    var projectDir = FindProjectRoot();
    if (projectDir == null) {
      Console.WriteLine("Could not find project root (looking for specs/ directory)");
      return 1;
    }

    var specDir = Path.Combine(projectDir, "specs");
    var fragmentDir = Path.Combine(specDir, $"fragments-{target.Arch}-{target.Os}");
    var tempDir = Path.Combine(projectDir, "temp");

    Compiler.CompileError.ProjectRoot = projectDir;

    var runner = new TestRunner(specDir, fragmentDir, tempDir, projectDir, filter, workers, updateRequired, target, verbose);
    var summary = runner.RunAllSpecTests();

    Logger.Info(LogCategory.Testing, "");
    if (summary.FragmentGenerationErrors > 0) {
      Logger.Error(LogCategory.Testing, $"Fragment generation failed: {summary.FragmentGenerationErrors} error(s) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
      return 1;
    }

    return ReportTestResults(summary);
  }

  static string? FindProjectRoot() {
    // Look for specs/ directory to find project root
    var dir = Directory.GetCurrentDirectory();
    while (dir != null) {
      if (Directory.Exists(Path.Combine(dir, "specs"))) {
        return dir;
      }
      dir = Path.GetDirectoryName(dir);
    }
    return null;
  }

  /// <summary>
  /// Sets up logging for test commands (suppresses compiler Info messages).
  /// </summary>
  static void SetupTestLogging() {
    Logger.SetLevel(LogCategory.Compiler, LogLevel.Error);
  }

  /// <summary>
  /// Reports test results in a consistent format.
  /// </summary>
  static int ReportTestResults(TestSummary summary) {
    var cachedInfo = summary.CachedPassed > 0 ? $" ({summary.CachedPassed} cached)" : "";
    if (summary.Failed == 0) {
      Logger.Info(LogCategory.Testing, $"Tests: {summary.Passed} passed{cachedInfo} (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
      return 0;
    } else {
      Logger.Error(LogCategory.Testing, $"Tests: {summary.Passed} passed{cachedInfo}, {summary.Failed} failed (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
      return 1;
    }
  }

  static async Task<int> RunLspAsync() {
    var server = new LspServer();
    await server.RunAsync();
    return 0;
  }

}
