namespace MaxonSharp.Debug;

/// <summary>
/// Finding, reading and VOUCHING FOR the `.mxdbg` that describes a given binary.
///
/// Both measuring commands need exactly this before they can say anything at all — coverage to name
/// what each counter counts, the profiler to name the code its samples landed in — and both must refuse
/// the same three ways: no sidecar, an unreadable one, and one that belongs to a different build. The
/// third is the one worth single-sourcing, because it is the only one that would otherwise produce a
/// confident WRONG answer instead of a missing one.
/// </summary>
public static class MxdbgSidecar {

  /// <summary>
  /// The validated sidecar for <paramref name="exePath"/>, or null with a refusal naming which of the
  /// three defects it hit. <paramref name="need"/> completes the sentence for the caller that asked, so
  /// a missing sidecar says what the caller could not do without it rather than only that it is absent.
  ///
  /// The build-id is recomputed from the binary's own code section and checked HERE, before any caller
  /// reads a single table — so no command can be handed a sidecar that describes other code.
  /// </summary>
  public static MxdbgReader? TryLoad(string exePath, string need, out ulong binaryBuildId, out string error) {
    if (!BinaryBuildId.TryCompute(exePath, out binaryBuildId, out error)) return null;

    var sidecarPath = exePath + MxdbgFormat.SidecarExtension;
    if (!File.Exists(sidecarPath)) {
      error = $"no debug-info sidecar at {ReportPath.Display(sidecarPath)} — {need}."
        + " The sidecar is written by default and leaves the executable byte-identical, so a binary that"
        + " has none was built --no-debug-info: rebuild it without that flag.";
      return null;
    }

    try {
      var sidecar = new MxdbgReader(File.ReadAllBytes(sidecarPath));

      // Deliberately does NOT print the two ids: they are a build-machine detail a reader can do
      // nothing with, and printing them would make every refusal transcript machine-specific.
      if (sidecar.BuildId != binaryBuildId) {
        error = "the .mxdbg sidecar describes a different build of this binary — rebuild it";
        return null;
      }

      return sidecar;
    } catch (Exception ex) when (ex is InvalidDataException or IOException) {
      error = $"cannot read {ReportPath.Display(sidecarPath)}: {ex.Message}";
      return null;
    }
  }
}
