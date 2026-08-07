using System.Text.Json;
using MaxonSharp.Compiler;

namespace MaxonSharp.Testing;

/// <summary>
/// Caches what <see cref="Compiler.Compiler.DiscoverTests"/> found, so a repeated
/// <c>maxon test --list</c> — or a warm run whose binary is already cached — does not re-parse the
/// project to learn something that has not changed.
///
/// It is keyed on exactly what a BUILD is keyed on: the compiler's own timestamp, and every source
/// file's last-write time IN COMPILE ORDER (the order is part of the input — see
/// <see cref="BuildCache.SourceInputs.Sources"/>), all taken from <see cref="BuildCache"/> rather
/// than recomputed. Two
/// caches over the same inputs that decided staleness their own way would eventually disagree, and
/// the one that was wrong would be this one — reporting a test that no longer exists, or missing one
/// that does.
/// </summary>
internal static class TestManifest {
  /// <summary>
  /// Bumped when the stored SHAPE changes. An older manifest is rejected outright rather than
  /// half-read, matching <see cref="BuildCache"/>'s rule for the same reason. Version 2 replaced
  /// this manifest's own copy of the key fields with the shared
  /// <see cref="BuildCache.SourceInputs"/>; version 3 followed that shape's move to an ORDERED
  /// source record.
  /// </summary>
  private const int ManifestVersion = 3;

  /// <summary>
  /// The file this cache lives in.
  ///
  /// NOT `test-manifest.json`, which looks like the obvious name and is already taken:
  /// <see cref="BuildCache"/> spells its manifests <c>{cacheName}-manifest.json</c>, and the test
  /// binary's cache slot is named <c>test</c> — so that path is the BUILD manifest. Measured, not
  /// reasoned: with both writing there, each run overwrote the other's file and BOTH caches missed
  /// every time, so a warm `maxon test` recompiled the whole project on every invocation while
  /// reporting a compile time that looked plausible.
  /// </summary>
  private const string FileName = "test-discovery.json";

  /// <summary>
  /// Every property is <c>required</c> so that adding one is a compile error at the single place a
  /// manifest is built, rather than a field that silently reads as its default forever after —
  /// <see cref="BuildCache"/>'s rule, for its reason.
  /// </summary>
  private sealed record Manifest {
    public required int Version { get; init; }

    /// <summary>
    /// What the recorded discovery was performed against, in the SHARED shape — so this cache and
    /// the build cache cannot come to disagree about whether a change has happened.
    ///
    /// The TARGET is in that key and carries this cache's own reason to be there: WHICH tests exist
    /// depends on it, because <c>#if os(...)</c> / <c>arch(...)</c> are resolved during the parse
    /// this caches. Without it, `maxon test --target=arm64-macos` followed by a plain `maxon test`
    /// would reuse the first run's answer; and because the dispatcher's content hash is derived
    /// FROM that answer, the build cache would then also hit, handing back the wrong binary.
    /// </summary>
    public required BuildCache.SourceInputs Inputs { get; init; }

    public required List<DiscoveredTest> Tests { get; init; }
  }

  private static string PathFor(string projectDir) =>
    Path.Combine(BuildCache.GetCacheDir(projectDir), FileName);

  /// <summary>
  /// The tests recorded for these exact sources, or null if nothing valid is stored.
  ///
  /// A manifest that cannot be read is a MISS, never an error: an older shape, a truncated write, a
  /// hand-edit. The cost of being wrong here is one parse.
  /// </summary>
  public static List<DiscoveredTest>? Read(string projectDir, SourceFile[] onDiskSources,
      CompileTarget target) {
    var path = PathFor(projectDir);
    if (!File.Exists(path)) return null;

    try {
      var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), JsonOptions);
      if (manifest == null || manifest.Version != ManifestVersion) return null;
      if (!manifest.Inputs.StillCurrent(onDiskSources, target)) return null;

      return manifest.Tests;
    } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
      return null;
    }
  }

  /// <summary>Record <paramref name="tests"/> as current for these sources and this target.</summary>
  public static void Write(string projectDir, SourceFile[] onDiskSources, CompileTarget target,
      List<DiscoveredTest> tests) {
    var manifest = new Manifest {
      Version = ManifestVersion,
      Inputs = BuildCache.SourceInputs.Current(onDiskSources, target),
      Tests = tests,
    };

    BuildCache.EnsureCacheDir(projectDir);
    File.WriteAllText(PathFor(projectDir), JsonSerializer.Serialize(manifest, JsonOptions));
  }

  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };
}
