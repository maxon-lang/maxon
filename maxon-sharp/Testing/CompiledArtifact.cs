namespace MaxonSharp.Testing;

/// <summary>
/// Removing a compiled test binary — which is more than one file.
///
/// A compile with debug info on writes a <c>&lt;binary&gt;.mxdbg</c> sidecar beside the executable,
/// so a delete that names only the executable leaves the sidecar behind. Several of these compiles
/// aim at <c>specs/fragments-*/</c>, which is a COMMITTED directory, and a leftover there is
/// untracked litter sitting next to the goldens.
///
/// One home because there were six separate copies of
/// <c>try { if (File.Exists(p)) File.Delete(p); } catch { }</c> across the runner and the fragment
/// generator. Every one of them predated debug info reaching a spec compile at all, so not one of
/// them would have grown the sidecar clause on its own — which is exactly how a rule written six
/// times loses one.
/// </summary>
internal static class CompiledArtifact {
  /// <summary>
  /// Delete a compiled binary and every sidecar a build can have written beside it.
  ///
  /// WHICH files those are is ASKED of <see cref="MaxonSharp.Compiler.Compiler.PublishedOutputPaths"/>
  /// — the compiler's own list, the one it clears before every build — rather than restated here.
  /// Restated, it had already lost the `.ir` clause that list carries, which is the failure mode this
  /// class exists to end and would have been the seventh copy of it.
  ///
  /// Failures are ignored: a leftover the OS still has open is untidy, never a reason to fail a run.
  /// That is the ONE thing that differs from the compiler's own use of the same list, which fails the
  /// build instead — a policy difference, not a different set of files.
  /// </summary>
  internal static void Delete(string binaryPath) {
    foreach (var path in MaxonSharp.Compiler.Compiler.PublishedOutputPaths(binaryPath)) {
      TryDelete(path);
    }
  }

  private static void TryDelete(string path) {
    try {
      if (File.Exists(path)) File.Delete(path);
    } catch {
      // A still-exiting child process can hold its own image open for a moment after we observed
      // its exit code; the next run's cleanup gets it.
    }
  }
}
