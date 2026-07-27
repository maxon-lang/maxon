using System.Text.Json;
using MaxonSharp.Compiler;

namespace MaxonSharp;

static class BuildCache {
  /// <summary>
  /// Bumped whenever the manifest's SHAPE changes. A manifest written by an older compiler is not
  /// merely stale — it cannot be compared field for field — so it is rejected outright rather than
  /// half-read. Version 2 moved the compiler flags into <see cref="EmittedCodeFlags"/> and added
  /// <c>ExtraKey</c>.
  /// </summary>
  const int ManifestVersion = 2;

  /// <summary>
  /// The manifest slot for a project's own binary. The compiler keeps several caches per project
  /// (the project, its build-runner, one per <c>maxon run</c> function), each named; this is the
  /// default one, spelled once instead of being repeated as a literal default on four methods.
  /// </summary>
  public const string ProjectCacheName = "build";

  /// <summary>
  /// The <c>extraKey</c> of a build whose sources are all real files on disk — which is every
  /// caller that does not synthesize source in memory. Named rather than written as a bare <c>""</c>
  /// so a call site states the fact ("this build has no in-memory sources") instead of a value.
  /// </summary>
  public const string NoInMemorySources = "";

  static long? _compilerModifiedTicks;

  /// <summary>
  /// Every compiler setting that CHANGES THE EMITTED BYTES, and therefore every setting a cached
  /// binary is only valid for if it still matches.
  ///
  /// It is a record, and the staleness check is <c>!=</c> against <see cref="Current"/>, because the
  /// list used to be written down THREE times — once as manifest fields, once as a chain of
  /// comparisons, once as an assignment — and a flag could be, and was, added to fewer than three.
  /// <c>Compiler.Testing</c> gates <c>#if testing(...)</c> in the parser, so it changes the emitted
  /// code, and it appeared in NONE of them: a build with it on was handed the binary from a build
  /// with it off. Value equality derives the comparison from the field list, and <c>required</c>
  /// makes <see cref="Current"/> fail to COMPILE if a field is added and left unread — so the two
  /// ways of forgetting a flag are now a compile error and an impossibility respectively.
  ///
  /// Two flags are deliberately absent, and both are absent because they do not change a byte:
  /// <c>DebugInfo</c> only decides whether the <c>.mxdbg</c> sidecar is written beside an otherwise
  /// identical exe, and <c>LiteralCoverage</c> only runs a measurement pass that reports to stderr.
  /// </summary>
  record EmittedCodeFlags {
    public required bool MmTrace { get; init; }
    public required bool MmTraceRawOnly { get; init; }
    public required bool MmDebug { get; init; }
    public required bool AsyncTrace { get; init; }
    public required bool DebugStream { get; init; }
    public required bool NoDebugAgent { get; init; }
    public required bool Coverage { get; init; }
    public required bool Testing { get; init; }

    /// <summary>The flags this process would compile with right now.</summary>
    public static EmittedCodeFlags Current => new() {
      MmTrace = Compiler.Compiler.MmTrace,
      MmTraceRawOnly = Compiler.Compiler.MmTraceRawOnly,
      MmDebug = Compiler.Compiler.MmDebug,
      AsyncTrace = Compiler.Compiler.AsyncTrace,
      DebugStream = Compiler.Compiler.DebugStream,
      NoDebugAgent = Compiler.Compiler.NoDebugAgent,
      Coverage = Compiler.Compiler.Coverage,
      Testing = Compiler.Compiler.Testing,
    };
  }

  /// <summary>
  /// Every property is <c>required</c> so that adding one is a compile error at the single place a
  /// manifest is built, rather than a field that silently reads as its default forever after.
  /// </summary>
  record CacheManifest {
    public required int Version { get; init; }
    public required long CompilerModified { get; init; }
    public required string TargetArch { get; init; }
    public required string TargetOs { get; init; }
    public required EmittedCodeFlags Flags { get; init; }
    public required string OutputPath { get; init; }
    public required Dictionary<string, long> Sources { get; init; }

    /// <summary>
    /// The caller's own contribution to the cache key, covering source this cache cannot see:
    /// sources that exist only in memory. See the <c>extraKey</c> parameter of
    /// <see cref="WriteCache"/>.
    /// </summary>
    public required string ExtraKey { get; init; }
  }

  static SourceFile[] WithStdlibSources(SourceFile[] userSources) {
    var stdlib = Compiler.StdlibLoader.LoadStdlibModules();
    if (stdlib.Length == 0) return userSources;
    return Compiler.StdlibLoader.PrependStdlib(stdlib, userSources);
  }

  /// <remarks>
  /// Internal for the reason <see cref="SourceTimestamps"/> is: the test manifest must go stale on a
  /// new compiler for exactly the same reason a cached binary does — the parse it recorded is the
  /// old compiler's answer.
  /// </remarks>
  internal static long GetCompilerModifiedTicks() {
    if (_compilerModifiedTicks == null) {
      var exePath = Environment.ProcessPath;
      _compilerModifiedTicks = exePath != null ? File.GetLastWriteTimeUtc(exePath).Ticks : 0;
    }
    return _compilerModifiedTicks.Value;
  }

  /// <summary>
  /// The directory the compiler CREATES INSIDE a project it is compiling, and writes its own
  /// artifacts into: this cache, the manifests, the test binary, the discovery manifest, and by
  /// convention a project's emitted executable.
  ///
  /// Spelled here — beside <see cref="EnsureCacheDir"/>, which is what brings it into existence —
  /// because the party that must also know it is <see cref="Compiler.SourceCollector"/>, whose job
  /// is to decide what counts as SOURCE. Output that a source walk collects is output that becomes
  /// the next compile's input, and the two caches keyed on that walk can then never agree with
  /// themselves: the run writes files the next run discovers, so the source SET changes underneath
  /// a key computed from it. Measured on <c>maxon-dev-mcp/test</c>, whose registry-conformance
  /// fixtures write scratch <c>ErrorCodeRegistry.maxon</c> trees here — the run after a cold one
  /// missed BOTH the discovery manifest and the build cache every time, re-parsing (413ms) and
  /// recompiling (~1.4s) a project nothing had changed, and compiled 8 scratch files into the test
  /// binary as dead weight.
  /// </summary>
  public const string CompilerOwnedDirName = ".maxon";

  public static string GetCacheDir(string projectDir) {
    return Path.Combine(projectDir, CompilerOwnedDirName, "cache");
  }

  public static void EnsureCacheDir(string projectDir) {
    var dir = GetCacheDir(projectDir);
    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
  }

  static string GetManifestPath(string projectDir, string cacheName) {
    return Path.Combine(GetCacheDir(projectDir), $"{cacheName}-manifest.json");
  }

  /// <summary>
  /// The cache-key contribution of the real, on-disk sources: full path to last-write ticks.
  ///
  /// Written once and used by BOTH sides, so the manifest cannot be built to one rule and checked
  /// against another.
  ///
  /// A source that is not a file on disk is REFUSED, because neither thing that would otherwise
  /// happen to it is acceptable. <see cref="File.GetLastWriteTimeUtc"/> does not fail on a path that
  /// does not exist — it answers 1601-01-01 — so an in-memory source would be keyed on a CONSTANT:
  /// two different synthesized sources compare equal, and the second build is handed the first one's
  /// binary. And a source whose name is not even path-shaped (<c>&lt;dispatcher&gt;</c>) throws
  /// <see cref="ArgumentException"/> from deep inside the cache instead. Callers that synthesize
  /// source pass its content hash as <c>extraKey</c>.
  /// </summary>
  /// <remarks>
  /// Internal rather than private because a SECOND cache keys on the same fact: the test manifest
  /// (<see cref="Testing.TestManifest"/>) is valid for exactly the inputs a build is, and computing
  /// "which files, at which versions" a second way is how the two would come to disagree about
  /// whether a change had happened.
  /// </remarks>
  internal static Dictionary<string, long> SourceTimestamps(SourceFile[] onDiskSources) {
    var timestamps = new Dictionary<string, long>();
    foreach (var source in WithStdlibSources(onDiskSources)) {
      if (!File.Exists(source.Path)) {
        throw new InvalidOperationException(
          $"BuildCache was given a source that is not a file on disk: '{source.Path}'. "
          + "The build cache keys on-disk sources by their last-write time, which an in-memory "
          + "source does not have. Pass only real files here and give the in-memory sources' "
          + "content hash as the cache's extraKey.");
      }
      timestamps[Path.GetFullPath(source.Path)] = File.GetLastWriteTimeUtc(source.Path).Ticks;
    }
    return timestamps;
  }

  /// <summary>
  /// Whether the cached binary for <paramref name="cacheName"/> is still current.
  /// </summary>
  /// <param name="onDiskSources">
  /// The build's sources that are real files on disk. In-memory sources do not belong here; they
  /// are keyed through <paramref name="extraKey"/>.
  /// </param>
  /// <param name="extraKey">
  /// The caller's own contribution to the cache key: anything affecting the emitted bytes that this
  /// cache cannot see for itself, which in practice means a hash of the sources the caller
  /// synthesized in memory. It must differ whenever that content differs. A caller with no
  /// in-memory sources passes <see cref="NoInMemorySources"/> and is keyed exactly as before.
  /// </param>
  public static bool IsCacheValid(string projectDir, SourceFile[] onDiskSources, string? outputPath,
      CompileTarget target, string cacheName = ProjectCacheName, string extraKey = NoInMemorySources) {
    var manifest = ReadManifest(projectDir, cacheName);
    if (manifest == null) return false;

    if (manifest.CompilerModified != GetCompilerModifiedTicks()) return false;
    if (manifest.TargetArch != target.Arch || manifest.TargetOs != target.Os) return false;
    if (manifest.Flags != EmittedCodeFlags.Current) return false;
    if (manifest.ExtraKey != extraKey) return false;

    if (outputPath != null && manifest.OutputPath != Path.GetFullPath(outputPath)) return false;
    if (!File.Exists(manifest.OutputPath)) return false;

    // A debug-info build must also have its sidecar next to the binary. Because the exe is
    // byte-identical whether or not the sidecar is written, an EXISTING sidecar from any prior
    // build of the same source is still valid for the cached binary — so the only cache miss a
    // sidecar forces is "wanted but ABSENT" (it cannot be regenerated without a recompile). This
    // is what lets the sidecar be on by default without ever bypassing the build cache.
    if (Compiler.Compiler.DebugInfo
        && !File.Exists(manifest.OutputPath + Debug.MxdbgFormat.SidecarExtension)) return false;

    var expectedSources = SourceTimestamps(onDiskSources);
    if (manifest.Sources.Count != expectedSources.Count) return false;
    foreach (var (path, ticks) in expectedSources) {
      if (!manifest.Sources.TryGetValue(path, out var cachedTicks)) return false;
      if (cachedTicks != ticks) return false;
    }

    return true;
  }

  public static string? GetCachedOutputPath(string projectDir, string cacheName = ProjectCacheName) {
    return ReadManifest(projectDir, cacheName)?.OutputPath;
  }

  static CacheManifest? ReadManifest(string projectDir, string cacheName) {
    var manifestPath = GetManifestPath(projectDir, cacheName);
    if (!File.Exists(manifestPath)) return null;
    try {
      var json = File.ReadAllText(manifestPath);
      var manifest = JsonSerializer.Deserialize<CacheManifest>(json, JsonOptions);
      if (manifest == null || manifest.Version != ManifestVersion) return null;
      return manifest;
    } catch {
      // A manifest that cannot be read is a cache MISS, never a build failure: an older shape, a
      // truncated write, a hand-edit. The cost of being wrong here is one recompile.
      return null;
    }
  }

  /// <summary>
  /// Record that <paramref name="outputPath"/> is current for these inputs. See
  /// <see cref="IsCacheValid"/> for what <paramref name="onDiskSources"/> and
  /// <paramref name="extraKey"/> mean — they must be the same on both sides or the entry can never
  /// be hit.
  /// </summary>
  public static void WriteCache(string projectDir, SourceFile[] onDiskSources, string outputPath,
      CompileTarget target, string cacheName = ProjectCacheName, string extraKey = NoInMemorySources) {
    var manifest = new CacheManifest {
      Version = ManifestVersion,
      CompilerModified = GetCompilerModifiedTicks(),
      TargetArch = target.Arch,
      TargetOs = target.Os,
      Flags = EmittedCodeFlags.Current,
      OutputPath = Path.GetFullPath(outputPath),
      Sources = SourceTimestamps(onDiskSources),
      ExtraKey = extraKey,
    };

    var manifestPath = GetManifestPath(projectDir, cacheName);
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
