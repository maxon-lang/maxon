using System.Diagnostics;

namespace MaxonSharp.Compiler;

/// <summary>
/// Which kinds of .maxon file a caller wants compiled. Stated rather than implied: a bare
/// <c>true</c>/<c>false</c> at the call site says nothing about WHAT is being included, and the
/// two consumers want opposite things for reasons that must stay legible.
/// </summary>
public enum SourceSelection {
  /// <summary>
  /// Ship-able program source only — <c>*.test.maxon</c> is left out. What <c>maxon build</c>
  /// wants: test code is unreferenced by the program, so it is dead weight in the emitted binary,
  /// and under shv2's <c>checkUnusedExports</c> it would refuse the build outright.
  ///
  /// ⚠ That last clause was written about a pass that existed for a few hours on 2026-08-03 and was
  /// then removed in full by user ruling, so it spent two and a half weeks being FALSE while reading
  /// as a justification. It became true again on 2026-08-21, when the family landed for good behind
  /// the <c>public</c> tier: an exported helper in a test file that the program never calls is
  /// E3092, and including one really would fail the build.
  /// </summary>
  ProductionOnly,

  /// <summary>
  /// Program source AND its co-located <c>*.test.maxon</c> files. What the LSP wants: a test file
  /// is source somebody is editing, and diagnostics, completions and hover have to work inside it.
  /// </summary>
  ProductionAndTests,
}

/// <summary>
/// Central source-file collection for both the CLI (<c>maxon build</c>) and
/// the LSP server, so that both feed <see cref="Compiler.CompileSources"/> the
/// same set of files.
///
/// Resolution of top-level CONSTANTS does not depend on this order: every file's
/// constant declarations are collected before any of them is folded (see
/// Parser.PreScanTopLevelConstantDecls). Other pre-scan passes still carry
/// ordering dependencies — a duplicate type name resolves LAST-writer-wins (the
/// flat tables are assigned into, never TryAdd'd: see Parser.CopyStateToModule
/// and IrModule.Merge), and auto-conformance synthesis only sees the files
/// pre-scanned before it — so a divergence in this order can still produce a
/// diagnostic in one client and not the other, until those are order-independent
/// too. A CONTESTED generic-instance alias is now neither, where the losing
/// declaration may be renamed (IrModule.ContestedGenericAliasNames).
///
/// <see cref="SourceOrderEnvVar"/> is the seam that proves the claim rather than
/// asserting it: a real project built forwards and backwards must produce
/// byte-identical output and diagnostics.
///
/// ⚠ IT PROVED NOTHING ON A WARM TREE UNTIL BATCH34. Reversing the list changes no
/// path and no timestamp, and the build cache keyed sources as an unordered
/// dictionary, so the second build of a directory HIT the cache and re-ran the
/// first order's binary while reporting success — the seam could return a false
/// "order-independent" and never a false positive. The cache now keys the order
/// (BuildCache.SourceInputs.Sources); a sameness result recorded before that date
/// was measured through a blind instrument.
/// </summary>
public static class SourceCollector {
  /// <summary>
  /// Project metadata, not compiler source: it declares how to build the project and is compiled
  /// separately (as the build-runner) when it exports a <c>build()</c>. Spelled once here because
  /// seven places used to carry the literal, and a rename would have moved only some of them.
  /// </summary>
  public const string BuildManifestFileName = "build.maxon";

  /// <summary>
  /// The function a <see cref="BuildManifestFileName"/> must EXPORT for its directory to be a
  /// project. Spelled beside the file name because it is the other half of one fact — "this
  /// directory is a Maxon project" — and two places now ask it: <c>maxon build</c>, which compiles
  /// the manifest and looks for the name among the module's exported functions, and
  /// <c>FlatNamespaceCheck</c>, which lexes the manifest and matches the three tokens. Those two
  /// tests are NOT the same test and cannot be (see FlatNamespaceCheck.DeclaresBuildFunction), so
  /// the least that must be shared is the NAME they both look for.
  /// </summary>
  public const string BuildFunctionName = "build";

  /// <summary>
  /// The suffix that makes a file a unit-test file for <c>maxon test</c>. Tests are co-located
  /// with the code they cover, so the NAME is the only thing that separates them from production
  /// source — which is why this suffix, and <see cref="IsTestFile"/> that applies it, exist in
  /// exactly one place for the build, the LSP and the test runner to share.
  /// </summary>
  public const string TestFileSuffix = ".test.maxon";

  /// <summary>
  /// Whether <paramref name="path"/> names a <c>maxon test</c> unit-test file.
  ///
  /// THE definition — not a copy of one. The build excludes these, the LSP includes them and the
  /// test runner selects them; if any of the three answered this question for itself they could
  /// disagree, and a file would be a test to one and production to another.
  /// </summary>
  public static bool IsTestFile(string path) =>
    Path.GetFileName(path).EndsWith(TestFileSuffix, StringComparison.OrdinalIgnoreCase);

  /// <summary>The <c>.maxon</c> extension itself, so <see cref="SuggestTestFileName"/> can strip it.</summary>
  private const string MaxonFileSuffix = ".maxon";

  /// <summary>
  /// What <paramref name="fileName"/> would have to be called to hold a <c>test</c> declaration.
  ///
  /// It lives HERE, beside <see cref="IsTestFile"/>, because it is the inverse of that question and
  /// the two have to agree: a suggestion the predicate would then reject is worse than none. The
  /// parser used to derive it from a private copy of the suffix — and that copy also compared
  /// case-SENSITIVELY, so `Api.Test.maxon` was a test file to the build (excluded from it) and not
  /// one to the parser (E2058 on its first `test`), leaving the file neither buildable nor testable
  /// and told to rename itself to a name it already had.
  /// </summary>
  public static string SuggestTestFileName(string fileName) =>
    fileName.EndsWith(MaxonFileSuffix, StringComparison.OrdinalIgnoreCase)
      ? string.Concat(fileName.AsSpan(0, fileName.Length - MaxonFileSuffix.Length), TestFileSuffix)
      : fileName + TestFileSuffix;

  /// <summary>
  /// Resolve a <see cref="SourceSelection"/> to the one question the walk asks of each file.
  /// An unknown value throws rather than defaulting: a selection nobody taught this method is a
  /// missing decision, and guessing "include" would silently ship test code.
  /// </summary>
  private static bool IncludesTests(SourceSelection selection) => selection switch {
    SourceSelection.ProductionOnly => false,
    SourceSelection.ProductionAndTests => true,
    _ => throw new ArgumentOutOfRangeException(nameof(selection), selection,
      $"Unhandled {nameof(SourceSelection)}; every selection must state whether it includes test files."),
  };

  /// <summary>Why a discovered .maxon file is not a compiler source for this caller.</summary>
  private enum SourceExclusion {
    /// <summary>Project metadata; compiled separately as the build-runner, never as project source.</summary>
    BuildManifest,
    /// <summary>Named by a <c>.maxonignore</c>.</summary>
    Ignored,
    /// <summary>A <c>*.test.maxon</c>, and this caller asked for production source only.</summary>
    TestFile,
    /// <summary>
    /// Inside a <see cref="BuildCache.CompilerOwnedDirName"/> directory — the compiler's OWN output,
    /// at any depth. Never source, for any caller, which is why it takes no parameter.
    /// </summary>
    CompilerOutput,
  }

  /// <summary>
  /// Whether <paramref name="normalizedPath"/> lies inside a compiler-output directory.
  ///
  /// Tested per SEGMENT rather than as a prefix of one root, because a walk rooted at a directory
  /// that CONTAINS projects (the repo itself) reaches every nested project's output too, and each of
  /// those is the same kind of not-source as the walked project's own.
  /// </summary>
  internal static bool IsInCompilerOutputDir(string normalizedPath) {
    foreach (var segment in normalizedPath.Split('/')) {
      if (segment.Equals(BuildCache.CompilerOwnedDirName, StringComparison.OrdinalIgnoreCase))
        return true;
    }
    return false;
  }

  /// <summary>
  /// Why <paramref name="path"/> is not a source for this caller, or null if it is one.
  ///
  /// ONE rule, asked by both halves of the walk. <see cref="FromDirectory"/> considers files from
  /// two places — the directory listing and the editor's unsaved buffers — and the two used to
  /// decide inclusion by DIFFERENT rules: the disk walk skipped <c>build.maxon</c> and
  /// <c>.maxonignore</c>d files, and the buffer loop skipped neither, so a file the project had
  /// excluded became a source again as soon as it was open in an editor.
  /// </summary>
  private static SourceExclusion? ExclusionFor(string path, bool includesTests) {
    // Asked FIRST, because it is the only rule whose answer does not depend on the caller: a file
    // the compiler itself wrote is not source to anybody, and the cheapest place to stop it is
    // before any question about what it might otherwise have been.
    if (IsInCompilerOutputDir(path))
      return SourceExclusion.CompilerOutput;

    if (Path.GetFileName(path).Equals(BuildManifestFileName, StringComparison.OrdinalIgnoreCase))
      return SourceExclusion.BuildManifest;

    if (!includesTests && IsTestFile(path))
      return SourceExclusion.TestFile;

    if (MaxonIgnore.IsIgnored(path))
      return SourceExclusion.Ignored;

    return null;
  }

  /// <summary>
  /// Test seam: set to <c>reverse</c> to hand the compiler every discovered file in the exact
  /// opposite order. Order-independence is a property of the RESOLVER, and the only way to keep
  /// it true is to be able to run the real corpus both ways and diff — a suite that only ever
  /// runs in one order proves nothing, which is how a filesystem-order bug survived here for
  /// months (sorted on NTFS, hash-ordered on APFS: the tree that built on Windows was refused on
  /// macOS). An environment variable rather than a CLI flag so it reaches subprocess builds too.
  /// </summary>
  public const string SourceOrderEnvVar = "MAXON_SOURCE_ORDER";
  private const string ReverseOrder = "reverse";

  /// <summary>
  /// Apply <see cref="SourceOrderEnvVar"/>. An unrecognized value throws rather than falling
  /// back to natural order: a typo'd seam that silently measures nothing is worse than no seam.
  /// </summary>
  private static List<SourceFile> ApplyRequestedOrder(List<SourceFile> files) {
    var requested = Environment.GetEnvironmentVariable(SourceOrderEnvVar);
    if (string.IsNullOrEmpty(requested)) return files;
    if (requested == ReverseOrder) {
      files.Reverse();
      return files;
    }
    throw new ArgumentException(
      $"{SourceOrderEnvVar}='{requested}' is not a known source order; the only supported value is '{ReverseOrder}'");
  }
  /// <summary>
  /// Walk <paramref name="directory"/> and collect every .maxon file suitable
  /// as a compiler source. Skips <c>build.maxon</c> (project metadata, not
  /// source), anything listed in a <c>.maxonignore</c>, and — unless
  /// <paramref name="selection"/> asks for them — <c>*.test.maxon</c>. Each file's content
  /// is truncated at a <c>---</c> separator line (used by spec tests and the
  /// like to pin test fixtures after the source).
  ///
  /// <paramref name="editorOverrides"/> supplies in-memory buffers keyed by
  /// the file's <em>normalized</em> path (see <see cref="NormalizePath"/>).
  /// When an override matches a discovered file, its content replaces the
  /// disk content — the LSP uses this so an edited-but-unsaved buffer is
  /// what gets compiled.
  ///
  /// <paramref name="selection"/> defaults to <see cref="SourceSelection.ProductionOnly"/> so
  /// that a caller which says nothing gets a shippable program: including test code has to be
  /// asked for, because the cost of accidentally including it (dead weight in every binary, and a
  /// refused build under shv2's unused-export check, live again since 2026-08-21; see
  /// ProductionOnly) is paid silently, whereas the cost of accidentally excluding it is a missing
  /// test the runner reports by name.
  /// </summary>
  public static SourceFile[] FromDirectory(
    string directory,
    IReadOnlyDictionary<string, string>? editorOverrides = null,
    SourceSelection selection = SourceSelection.ProductionOnly
  ) {
    // The walked directory IS the project compile root: every file's namespace
    // is derived as rel(file, directory).parent under the directory-as-module
    // design (Phase 1 plumbing).
    var rootPath = directory;
    var files = new List<SourceFile>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var includesTests = IncludesTests(selection);
    // Only test-file exclusions are announced; the other two are long-standing project rules that
    // a user is not surprised by, whereas "my tests are not in the binary" is new behaviour.
    var excludedTestFileCount = 0;
    foreach (var file in Directory.GetFiles(directory, "*.maxon", SearchOption.AllDirectories)) {
      var normalized = NormalizePath(file);
      var exclusion = ExclusionFor(normalized, includesTests);
      if (exclusion != null) {
        if (exclusion == SourceExclusion.TestFile) excludedTestFileCount++;
        // Recorded as decided, so an editor buffer for the same file cannot re-admit it below —
        // and so it cannot be counted a second time.
        seen.Add(normalized);
        continue;
      }

      var content = editorOverrides != null && editorOverrides.TryGetValue(normalized, out var buf)
        ? ReadUpToSeparator(buf)
        : ReadUpToSeparator(File.ReadAllText(file));
      files.Add(new SourceFile(normalized, content, rootPath));
    }

    // Editor-only files (newly created, not yet on disk) still need to be
    // compiled so the LSP can show diagnostics for them — but they are subject to the same
    // exclusions as the disk walk. A test file the user has created and not yet saved is still a
    // test file, and a production-only caller must not receive it just because it lives in a buffer.
    if (editorOverrides != null) {
      foreach (var (path, content) in editorOverrides) {
        if (!seen.Add(path)) continue;

        var exclusion = ExclusionFor(path, includesTests);
        if (exclusion != null) {
          if (exclusion == SourceExclusion.TestFile) excludedTestFileCount++;
          continue;
        }

        files.Add(new SourceFile(path, ReadUpToSeparator(content), rootPath));
      }
    }

    // Announced HERE rather than by `maxon build`, because this is the only place that knows what
    // was left out. Reported on stderr via Logger so it cannot corrupt the LSP's stdout JSON-RPC
    // stream — and in practice the LSP never reaches it, since it asks for tests to be included.
    if (excludedTestFileCount > 0) {
      Logger.Info(LogCategory.Compiler,
        $"Skipped {excludedTestFileCount} test file(s) matching *{TestFileSuffix} — "
        + "test code is not part of the built program; run `maxon test` to compile and run them.");
    }

    return [.. ApplyRequestedOrder(files)];
  }

  /// <summary>
  /// Truncate a file's text at the first <c>---</c> separator line. Lines
  /// after the separator are treated as trailing fixture data, not source.
  /// </summary>
  public static string ReadUpToSeparator(string content) {
    var lines = content.Split('\n');
    var sourceLines = new List<string>();
    foreach (var line in lines) {
      if (line.Trim() == "---") break;
      sourceLines.Add(line);
    }
    return string.Join('\n', sourceLines);
  }

  /// <summary>
  /// Normalize a filesystem path to the form used as a source-file key: an
  /// absolute path with forward-slash separators. Matching editor-override
  /// keys against discovered files relies on both sides using this form.
  /// </summary>
  public static string NormalizePath(string path) =>
    Path.GetFullPath(path).Replace('\\', '/');

  /// <summary>The VCS this repository's ignore rules live in, asked directly rather than imitated.</summary>
  private const string GitExecutable = "git";

  /// <summary>Git's own "would this path be excluded?" query, which is the whole of the question.</summary>
  private const string GitCheckIgnoreVerb = "check-ignore";

  /// <summary>
  /// Which of <paramref name="normalizedPaths"/> this repository's own ignore rules exclude: the
  /// tool-owned scratch, caches and generated corpora that live inside the tree without being part of
  /// it. Paths are matched, and returned, in the exact spelling they were passed — use
  /// <see cref="NormalizePath"/> on both sides.
  ///
  /// IT ASKS GIT, AND THAT IS THE POINT. "These paths are not repository content" is already written
  /// down exactly once, in <c>.gitignore</c>. Any hand-maintained copy of it in C# would be that fact
  /// written twice — this repository's signature defect, and there are already FOUR tree walkers here
  /// with four different exclusion sets, so a fifth would not have been an answer. Reimplementing the
  /// matcher is not an option either: gitignore has anchoring, negation, <c>**</c>, per-directory
  /// files and repository-local excludes, and a partial mirror of another tool's rule loses one clause
  /// at a time, silently.
  ///
  /// WHAT IT COST BEFORE: <c>scale-test --perType</c> writes a generated Maxon project into
  /// <c>.scale-tmp/mmtrace-src/</c>, which <c>.gitignore</c> excludes and
  /// <c>FlatNamespaceCheck.UntabledProjectDirs</c> did not, so a tree that had ever run that tool
  /// failed <c>dotnet build</c> — loudest for an unattended agent, which then had a red baseline it
  /// did not cause.
  ///
  /// FAILURE IS IN THE SAFE DIRECTION, deliberately. No git on PATH, no repository, a git that errors
  /// — each yields the EMPTY set, every candidate stays in, and the caller OVER-reports. Over-reporting
  /// fails a build asking for an explicit decision, which is loud and recoverable; under-reporting
  /// would let a real project escape a completeness check, which is the silence being guarded against.
  /// (Git says "none are ignored" with exit 1 and an error with exit 128; both produce no output, so
  /// the empty result covers all three without the caller having to tell them apart.)
  /// </summary>
  public static HashSet<string> RepositoryIgnoredPaths(string root, IReadOnlyCollection<string> normalizedPaths) {
    var ignored = new HashSet<string>(StringComparer.Ordinal);
    if (normalizedPaths.Count == 0) return ignored;

    var startInfo = new ProcessStartInfo(GitExecutable) {
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    // `-C` rather than WorkingDirectory: it is git's own way of naming the tree, and it keeps the
    // answer tied to the root the caller asked about rather than to this process's cwd.
    startInfo.ArgumentList.Add("-C");
    startInfo.ArgumentList.Add(root);
    startInfo.ArgumentList.Add(GitCheckIgnoreVerb);
    // `--stdin` so a long candidate list cannot overflow a command line, and `-z` on both ends
    // because NUL is the one byte a filesystem path cannot contain.
    startInfo.ArgumentList.Add("--stdin");
    startInfo.ArgumentList.Add("-z");

    string output;
    try {
      using var git = Process.Start(startInfo);
      if (git == null) return ignored;

      // Drain both pipes WHILE writing. A candidate list large enough to fill the OS pipe buffer
      // would otherwise deadlock, each side blocked waiting for the other to read.
      var stdout = git.StandardOutput.ReadToEndAsync();
      var stderr = git.StandardError.ReadToEndAsync();
      foreach (var path in normalizedPaths) {
        git.StandardInput.Write(path);
        git.StandardInput.Write('\0');
      }
      git.StandardInput.Close();
      // Blocking on the readers is what the async start bought: the pipes drain while stdin is being
      // written, and only then does this thread wait. Same suppression and same reason as
      // ProcessLauncher.Run and TargetStdio — a console command has no SynchronizationContext for
      // GetResult() to deadlock against, which is the hazard the analyzer is about.
#pragma warning disable VSTHRD002
      output = stdout.GetAwaiter().GetResult();
      stderr.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
      git.WaitForExit();
    } catch (Exception) {
      // Every way of failing to ASK means the same thing — we do not know which paths git considers
      // ignored — and not knowing must degrade to the over-reporting direction documented above,
      // never to "nothing is a project".
      return ignored;
    }

    foreach (var path in output.Split('\0')) {
      if (path.Length > 0) ignored.Add(path);
    }

    return ignored;
  }
}
