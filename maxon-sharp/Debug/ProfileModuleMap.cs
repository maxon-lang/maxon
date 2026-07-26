using System.Diagnostics;

namespace MaxonSharp.Debug;

/// <summary>
/// Which loaded module an address belongs to, for addresses outside the profiled binary's own `.text`.
///
/// ⭐ WHY THIS EXISTS AT ALL. Without it a sample that landed in a DLL is reported as `[outside .text]`,
/// and that is a true statement a reader can do nothing with. It was measured, not anticipated: the
/// first green-thread profile taken with two processors came back **52% `[outside .text]`** — the single
/// largest row in the report — with no way to tell a scheduler defect from a thread that was simply
/// blocked in the kernel, and therefore no way to know whether the other 48% was trustworthy. A number
/// that large which cannot be attributed makes the whole instrument unusable, which is exactly the
/// failure the honesty fields exist to prevent rather than to document.
///
/// It is a SNAPSHOT taken when sampling starts. A module loaded later is not in it and its addresses
/// fall back to the unattributed name — honest, and the alternative (re-enumerating per sample) would
/// put a process-wide query on the sampling path to catch a case that does not arise in a profile of a
/// Maxon program, which loads nothing at runtime.
/// </summary>
internal sealed class ProfileModuleMap {

  /// Sorted by start address and non-overlapping, so a containment test is a binary search.
  private readonly (ulong Start, ulong End, string Name)[] _modules;

  private ProfileModuleMap((ulong Start, ulong End, string Name)[] modules) => _modules = modules;

  /// <summary>
  /// The modules <paramref name="process"/> has loaded right now. A process that has exited (or whose
  /// module list cannot be read) yields an EMPTY map rather than a failure: the profile is still
  /// perfectly valid, it simply names fewer of its foreign frames.
  /// </summary>
  public static ProfileModuleMap Snapshot(Process process) {
    try {
      var modules = new List<(ulong Start, ulong End, string Name)>();
      foreach (ProcessModule module in process.Modules) {
        ulong start = (ulong)module.BaseAddress.ToInt64();
        modules.Add((start, start + (ulong)module.ModuleMemorySize, module.ModuleName));
      }
      modules.Sort((a, b) => a.Start.CompareTo(b.Start));
      return new ProfileModuleMap([.. modules]);
    } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) {
      return new ProfileModuleMap([]);
    }
  }

  /// The module containing <paramref name="address"/>, or null when no loaded module does.
  public string? NameAt(ulong address) {
    int lo = 0;
    int hi = _modules.Length - 1;

    while (lo <= hi) {
      int mid = lo + (hi - lo) / 2;
      var module = _modules[mid];
      if (address < module.Start) hi = mid - 1;
      else if (address >= module.End) lo = mid + 1;
      else return module.Name;
    }

    return null;
  }
}
