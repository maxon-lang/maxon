using System.Text.Json;
using MaxonSharp.Compiler;

namespace MaxonSharp;

static class BuildCache {
  const int ManifestVersion = 1;

  static long? _compilerModifiedTicks;

  record CacheManifest {
    public int Version { get; init; }
    public long CompilerModified { get; init; }
    public string TargetArch { get; init; } = "";
    public string TargetOs { get; init; } = "";
    public bool MmTrace { get; init; }
    public bool MmDebug { get; init; }
    public bool AsyncTrace { get; init; }
    public bool DebugStream { get; init; }
    public string OutputPath { get; init; } = "";
    public Dictionary<string, long> Sources { get; init; } = [];
  }

  static long GetCompilerModifiedTicks() {
    if (_compilerModifiedTicks == null) {
      var exePath = Environment.ProcessPath;
      _compilerModifiedTicks = exePath != null ? File.GetLastWriteTimeUtc(exePath).Ticks : 0;
    }
    return _compilerModifiedTicks.Value;
  }

  public static string GetCacheDir(string projectDir) {
    return Path.Combine(projectDir, ".maxon", "cache");
  }

  public static void EnsureCacheDir(string projectDir) {
    var dir = GetCacheDir(projectDir);
    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
  }

  static string GetManifestPath(string projectDir, string name = "build") {
    return Path.Combine(GetCacheDir(projectDir), $"{name}-manifest.json");
  }

  public static bool IsCacheValid(string projectDir, SourceFile[] sources, string? outputPath, CompileTarget target, string name = "build") {
    var manifest = ReadManifest(projectDir, name);
    if (manifest == null) return false;

    if (manifest.CompilerModified != GetCompilerModifiedTicks()) return false;
    if (manifest.TargetArch != target.Arch || manifest.TargetOs != target.Os) return false;
    if (manifest.MmTrace != Compiler.Compiler.MmTrace) return false;
    if (manifest.MmDebug != Compiler.Compiler.MmDebug) return false;
    if (manifest.AsyncTrace != Compiler.Compiler.AsyncTrace) return false;
    if (manifest.DebugStream != Compiler.Compiler.DebugStream) return false;

    if (outputPath != null && manifest.OutputPath != Path.GetFullPath(outputPath)) return false;
    if (!File.Exists(manifest.OutputPath)) return false;

    if (manifest.Sources.Count != sources.Length) return false;
    foreach (var source in sources) {
      var fullPath = Path.GetFullPath(source.Path);
      if (!manifest.Sources.TryGetValue(fullPath, out var cachedTicks)) return false;
      if (File.GetLastWriteTimeUtc(source.Path).Ticks != cachedTicks) return false;
    }

    return true;
  }

  public static string? GetCachedOutputPath(string projectDir, string name = "build") {
    return ReadManifest(projectDir, name)?.OutputPath;
  }

  static CacheManifest? ReadManifest(string projectDir, string name = "build") {
    var manifestPath = GetManifestPath(projectDir, name);
    if (!File.Exists(manifestPath)) return null;
    try {
      var json = File.ReadAllText(manifestPath);
      var manifest = JsonSerializer.Deserialize<CacheManifest>(json, JsonOptions);
      if (manifest == null || manifest.Version != ManifestVersion) return null;
      return manifest;
    } catch {
      return null;
    }
  }

  public static void WriteCache(string projectDir, SourceFile[] sources, string outputPath, CompileTarget target, string name = "build") {
    var sourcesMap = new Dictionary<string, long>();
    foreach (var source in sources) {
      sourcesMap[Path.GetFullPath(source.Path)] = File.GetLastWriteTimeUtc(source.Path).Ticks;
    }

    var manifest = new CacheManifest {
      Version = ManifestVersion,
      CompilerModified = GetCompilerModifiedTicks(),
      TargetArch = target.Arch,
      TargetOs = target.Os,
      MmTrace = Compiler.Compiler.MmTrace,
      MmDebug = Compiler.Compiler.MmDebug,
      AsyncTrace = Compiler.Compiler.AsyncTrace,
      DebugStream = Compiler.Compiler.DebugStream,
      OutputPath = Path.GetFullPath(outputPath),
      Sources = sourcesMap
    };

    var manifestPath = GetManifestPath(projectDir, name);
    var dir = Path.GetDirectoryName(manifestPath)!;
    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

    var json = JsonSerializer.Serialize(manifest, JsonOptions);
    File.WriteAllText(manifestPath, json);
  }

  static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };
}
