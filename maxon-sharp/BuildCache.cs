using System.Text.Json;
using MaxonSharp.Compiler;

namespace MaxonSharp;

static class BuildCache {
  /// <summary>
  /// Bumped whenever the manifest's SHAPE changes. A manifest written by an older compiler is not
  /// merely stale — it cannot be compared field for field — so it is rejected outright rather than
  /// half-read. Version 2 moved the compiler flags into <see cref="EmittedCodeFlags"/> and added
  /// <c>ExtraKey</c>; version 3 moved the compiler timestamp, the target and the source timestamps
  /// into <see cref="SourceInputs"/>, the one both this cache and the test-discovery manifest read;
  /// version 4 made that source record an ORDERED sequence (see <see cref="SourceInputs.Sources"/>).
  /// </summary>
  const int ManifestVersion = 4;

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

  /// <summary>One source file as the cache identifies it: which file, at which version.</summary>
  internal record SourceVersion {
    public required string Path { get; init; }
    public required long Modified { get; init; }
  }

  /// <summary>
  /// The INPUTS a cached artifact was produced from: which compiler, which target, and which source
  /// files at which versions.
  ///
  /// One definition because there are TWO caches over exactly this key — the build cache's manifest
  /// and <see cref="Testing.TestManifest"/>'s — and "have the inputs changed" has to be one
  /// question. It was two: each manifest carried its own copy of the three fields and its own copy
  /// of the comparison, so a rule added to one (a new key field, a content hash instead of a
  /// timestamp) would leave the other still answering the older question and serving a stale
  /// artifact without saying so. Neither would look wrong at its own site.
  ///
  /// Every property is <c>required</c>, so adding a field is a compile error at
  /// <see cref="Current"/> rather than one that silently reads as its default.
  /// </summary>
  internal record SourceInputs {
    public required long CompilerModified { get; init; }
    public required string TargetArch { get; init; }
    public required string TargetOs { get; init; }

    /// <summary>
    /// The sources IN THE ORDER THEY WERE HANDED TO THE COMPILER, which is part of the key because
    /// it is part of the input: Maxon's top-level namespace is flat, so where two files contest one
    /// name the winner is decided by which was merged last. Two orders of the same files are two
    /// different programs, and the emitted bytes say so.
    ///
    /// ⚠ IT WAS AN UNORDERED <c>Dictionary</c>, AND THAT MADE THE PROJECT'S ONLY ORDER-INDEPENDENCE
    /// SEAM SILENTLY INERT. <see cref="Compiler.SourceCollector.SourceOrderEnvVar"/> reverses the
    /// file list, but a reversal changes no path and no timestamp, so a dictionary key compared by
    /// lookup could not see it: the second build of a directory hit the cache and was handed the
    /// FIRST order's binary while reporting success. Measured on a two-scope program whose answer
    /// genuinely depends on order — <c>wide=112</c> from the cache with the variable set, against
    /// <c>wide=70000</c> once the sources were touched and it was honoured. The seam is documented
    /// as a VERIFICATION seam in <see cref="Compiler.FlatNamespaceCheck"/> and in
    /// <see cref="Compiler.SourceCollector"/>'s own header, so its failure mode was to CONFIRM
    /// order-independence that had never been tested — and both of those now carry the caveat that a
    /// SAMENESS row recorded before this change was measured through a blind instrument.
    ///
    /// The order is DERIVED from the source array rather than keyed on the environment variable that
    /// happens to set it today: any future cause of a different order — a manifest's explicit source
    /// list, a changed walk, a platform whose directory listing differs — is then already covered,
    /// and there is no second place to remember to update.
    /// </summary>
    public required List<SourceVersion> Sources { get; init; }

    /// <summary>The inputs a build of <paramref name="onDiskSources"/> would have right now.</summary>
    public static SourceInputs Current(SourceFile[] onDiskSources, CompileTarget target) => new() {
      CompilerModified = GetCompilerModifiedTicks(),
      TargetArch = target.Arch,
      TargetOs = target.Os,
      Sources = SourceVersions(onDiskSources),
    };

    /// <summary>
    /// Whether these RECORDED inputs still describe the current ones.
    ///
    /// The cheap fields are compared first and the sources last, because
    /// <see cref="SourceVersions"/> costs a stat per file (stdlib included) and a compiler or
    /// target change has already settled the answer.
    /// </summary>
    public bool StillCurrent(SourceFile[] onDiskSources, CompileTarget target) {
      if (CompilerModified != GetCompilerModifiedTicks()) return false;
      if (TargetArch != target.Arch || TargetOs != target.Os) return false;

      var expected = SourceVersions(onDiskSources);
      if (Sources.Count != expected.Count) return false;

      for (var i = 0; i < expected.Count; i++) {
        if (Sources[i].Path != expected[i].Path) return false;
        if (Sources[i].Modified != expected[i].Modified) return false;
      }

      return true;
    }
  }

  /// <summary>
  /// Every property is <c>required</c> so that adding one is a compile error at the single place a
  /// manifest is built, rather than a field that silently reads as its default forever after.
  /// </summary>
  record CacheManifest {
    public required int Version { get; init; }
    public required SourceInputs Inputs { get; init; }
    public required EmittedCodeFlags Flags { get; init; }
    public required string OutputPath { get; init; }

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
  /// Internal for the reason <see cref="SourceVersions"/> is: the test manifest must go stale on a
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
  /// The cache-key contribution of the real, on-disk sources: full path and last-write ticks, IN
  /// COMPILE ORDER (see <see cref="SourceInputs.Sources"/> for why the order is part of the key).
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
  internal static List<SourceVersion> SourceVersions(SourceFile[] onDiskSources) {
    var versions = new List<SourceVersion>();
    foreach (var source in WithStdlibSources(onDiskSources)) {
      if (!File.Exists(source.Path)) {
        throw new InvalidOperationException(
          $"BuildCache was given a source that is not a file on disk: '{source.Path}'. "
          + "The build cache keys on-disk sources by their last-write time, which an in-memory "
          + "source does not have. Pass only real files here and give the in-memory sources' "
          + "content hash as the cache's extraKey.");
      }
      versions.Add(new SourceVersion {
        Path = Path.GetFullPath(source.Path),
        Modified = File.GetLastWriteTimeUtc(source.Path).Ticks,
      });
    }
    return versions;
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

    return manifest.Inputs.StillCurrent(onDiskSources, target);
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
  /// <summary>
  /// The inputs to record for a build that is ABOUT TO START. Capture this BEFORE compiling and hand
  /// it to <see cref="WriteCache"/> when the build succeeds.
  /// </summary>
  /// <remarks>
  /// ⛔⛔ CAPTURE BEFORE, NOT AFTER — READING THE MTIMES AT WRITE TIME WAS A FALSE-GREEN GENERATOR.
  /// <see cref="WriteCache"/> used to call <see cref="SourceInputs.Current"/> itself, i.e. AFTER the
  /// compile had finished. A source edited WHILE a build ran therefore had its POST-edit mtime recorded
  /// as the version the binary was built from — when the binary had in fact been built from the
  /// PRE-edit content. Every later build then compared the file's current mtime against a manifest
  /// that already claimed it, found them equal, and reported success WITHOUT REBUILDING.
  ///
  /// MEASURED: 65 consecutive successful builds whose exe mtime never moved and whose behaviour was
  /// the pre-edit source's. It cost an agent two false sabotage results — a sabotage that "did not go
  /// red" because the sabotage was never compiled.
  ///
  /// ⚠ <c>spec-test</c>'s STALE guard structurally CANNOT catch this: it compares the binary's mtime
  /// against the sources', and the binary genuinely IS newer than they are. The only sound external
  /// check is to delete the manifest, rebuild, and compare the binary's HASH.
  ///
  /// Capturing first inverts the failure into the safe direction: a mid-build edit leaves the RECORDED
  /// mtime BEHIND the file's, so the next build sees stale and rebuilds. A needless rebuild costs time;
  /// a skipped one is a wrong answer that outlives the session.
  /// </remarks>
  internal static SourceInputs CaptureInputs(SourceFile[] onDiskSources, CompileTarget target)
      => SourceInputs.Current(onDiskSources, target);

  /// <summary>
  /// Record that <paramref name="outputPath"/> is current for <paramref name="capturedInputs"/>. See
  /// <see cref="IsCacheValid"/> for what the inputs and <paramref name="extraKey"/> mean — they must be
  /// the same on both sides or the entry can never be hit.
  /// </summary>
  /// <param name="capturedInputs">
  /// From <see cref="CaptureInputs"/>, taken BEFORE the compile. See its remarks for why this is a
  /// parameter rather than something this method reads for itself.
  /// </param>
  internal static void WriteCache(string projectDir, SourceInputs capturedInputs, string outputPath,
      string cacheName = ProjectCacheName, string extraKey = NoInMemorySources) {
    var manifest = new CacheManifest {
      Version = ManifestVersion,
      Inputs = capturedInputs,
      Flags = EmittedCodeFlags.Current,
      OutputPath = Path.GetFullPath(outputPath),
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
