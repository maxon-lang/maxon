using MaxonSharp.Debug;

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
  /// Delete a compiled binary and the debug-info sidecar a build may have written beside it.
  /// Failures are ignored: a leftover the OS still has open is untidy, never a reason to fail a run.
  /// </summary>
  internal static void Delete(string binaryPath) {
    TryDelete(binaryPath);
    TryDelete(binaryPath + MxdbgFormat.SidecarExtension);
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
