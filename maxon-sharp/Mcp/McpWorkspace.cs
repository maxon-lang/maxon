using System.Diagnostics;
using System.Globalization;

namespace MaxonSharp.Mcp;

/// <summary>
/// The scratch directories a server writes SNIPPET sources and binaries into, and the one thing that
/// reclaims them when a server dies in a way it could not clean up after.
///
/// ⭐ THE DIRECTORY NAME CARRIES ITS OWNER, which is what makes reclaiming safe with any number of
/// servers running at once. Each server owns `%TEMP%/maxon-mcp/&lt;pid&gt;-&lt;start-ticks&gt;/`, and a
/// session's workspace is a directory under it. A blind "delete everything on startup" sweep would be
/// wrong — it would delete a CONCURRENT server's live workspaces — so the sweep deletes only the
/// directories whose owning process is provably gone.
///
/// ⚠ PID ALONE WOULD NOT DO: operating systems reuse process ids, so a dead server's id can belong to
/// something unrelated by the time anyone looks. The stamp therefore pairs the pid with the process's
/// START TIME, which is recorded at 100 ns resolution — a recycled pid whose process also started at the
/// same tick is not a case that occurs. A directory whose owner we cannot ASK about (a process we lack
/// the right to query) is LEFT ALONE: the cost of keeping a dead scratch directory is bytes in the
/// system temp area, and the cost of deleting a live one is another server's session failing mid-build.
/// </summary>
internal static class McpWorkspace {

  private const string Stem = "maxon-mcp";

  /// This server's own stamp — computed once, because a process's identity does not change and because
  /// the sweep must be able to recognise (and skip) the directory it is standing in.
  private static readonly string OwnerStamp = StampFor(Environment.ProcessId, OwnStartTicks());

  private static long OwnStartTicks() {
    using var self = Process.GetCurrentProcess();
    return self.StartTime.Ticks;
  }

  private static string StampFor(int processId, long startTicks) =>
    $"{processId.ToString(CultureInfo.InvariantCulture)}-{startTicks.ToString(CultureInfo.InvariantCulture)}";

  private static string Root => Path.Combine(Path.GetTempPath(), Stem);

  private static string OwnerDirectory => Path.Combine(Root, OwnerStamp);

  /// Where one session's snippets live. The directory is NOT created here — a session that never builds
  /// a snippet should leave nothing behind at all.
  public static string ForSession(string sessionId) => Path.Combine(OwnerDirectory, sessionId);

  /// <summary>
  /// How long to keep retrying a delete that a just-killed debuggee is still holding open, and how often.
  ///
  /// ⚠ THE RETRY IS NOT DEFENSIVENESS — it closes a MEASURED race. Windows releases a terminated
  /// process's image-file lock asynchronously, so a delete issued immediately after the kill can fail
  /// where the same delete a moment later succeeds. Measured on the Ctrl-C path, where the kill and the
  /// delete are back to back with no work between them: `snippet.exe` survived the shutdown that was
  /// supposed to remove it, while the DELETE path — identical except for an HTTP round trip in between —
  /// removed it every time. Half a second is far beyond the observed gap and invisible on a shutdown.
  /// </summary>
  private const int DeleteAttempts = 10;
  private const int DeleteRetryMs = 50;

  /// <summary>
  /// Delete a scratch directory, riding out the transient lock above and tolerating a genuinely stuck
  /// file. Stated once because three callers need it — a session ending, this server's own shutdown, and
  /// the sweep — and because every one of them is running for some OTHER reason: losing a scratch
  /// directory must never mask the reap, the shutdown or the sweep it was part of.
  /// </summary>
  public static void Delete(string path) {
    for (int attempt = 1; attempt <= DeleteAttempts; attempt++) {
      if (!Directory.Exists(path)) return;

      try {
        Directory.Delete(path, recursive: true);
        return;
      } catch (IOException) {
        // A file the debuggee (or something it spawned) still holds open.
      } catch (UnauthorizedAccessException) {
        // A read-only or locked artifact the build left behind.
      }

      if (attempt < DeleteAttempts) Thread.Sleep(DeleteRetryMs);
    }

    // Still held after the retries: something outlived the session. The OS owns the temp area, and the
    // next server's sweep will find this directory's owner dead and reclaim it then.
  }

  /// This server's own directory, removed on a clean shutdown so the ordinary case leaves nothing for
  /// any sweep to find.
  public static void DeleteOwn() => Delete(OwnerDirectory);

  /// <summary>
  /// Reclaim the scratch directories of servers that are GONE — the leak a `taskkill /F` leaves, which
  /// no handler in the killed process could ever have cleaned up itself. Run once at startup, because
  /// that is the moment a new server can act on a dead one's remains without racing anybody.
  /// </summary>
  public static void SweepDeadOwners() {
    if (!Directory.Exists(Root)) return;

    IEnumerable<string> owners;
    try {
      owners = Directory.EnumerateDirectories(Root);
    } catch (IOException) {
      return;                                        // the temp area is unreadable; nothing to reclaim
    } catch (UnauthorizedAccessException) {
      return;
    }

    foreach (var directory in owners) {
      var stamp = Path.GetFileName(directory);
      if (stamp == OwnerStamp) continue;             // our own, and it is very much alive

      if (OwnerIsGone(stamp)) Delete(directory);
    }
  }

  /// <summary>
  /// Is the server that owns <paramref name="stamp"/> gone? True only when we could ASK and the answer
  /// was no — an unparseable name, or an owner we cannot query, returns false so its directory survives.
  /// </summary>
  private static bool OwnerIsGone(string stamp) {
    var separator = stamp.IndexOf('-');
    if (separator <= 0) return false;

    if (!int.TryParse(stamp[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
      return false;
    if (!long.TryParse(stamp[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture,
        out var startTicks))
      return false;

    try {
      using var owner = Process.GetProcessById(pid);
      // The pid is live — but is it the SAME process? A pid outlives its process and is handed to the
      // next one, so the start time is what tells a still-running server from a stranger wearing its id.
      return owner.StartTime.Ticks != startTicks;
    } catch (ArgumentException) {
      return true;                                   // no process has that id: the owner is gone
    } catch (InvalidOperationException) {
      return true;                                   // it exited between the lookup and the question
    } catch (SystemException) {
      // We cannot query it (rights, or a protected process). Not provably dead ⇒ not deleted.
      return false;
    }
  }
}
