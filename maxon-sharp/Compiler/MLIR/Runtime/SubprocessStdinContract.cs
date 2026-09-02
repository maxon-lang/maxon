namespace MaxonSharp.Compiler.Ir.Runtime;

/// The stdin half of the subprocess spawn contract, stated ONCE for every target that
/// emits it. The authority is `stdlib/Subprocess.maxon`'s `StdinKind`; this is the C#
/// side's single copy of those numbers.
///
/// ⛔ IT IS SHARED BECAUSE IT WAS BRIEFLY NOT, AND THE FAILURE MODE IS A WRONG ANSWER
/// RATHER THAN A BUILD BREAK. `X86CodeEmitter.Runtime.cs` and `ARM64CodeEmitter.Runtime.cs`
/// each carried their own `4`, `5`, `1000` and their own "which kinds want a pipe" array —
/// two copies, in ONE assembly, of one language-level fact, with nothing making them agree.
/// They had already drifted in spelling: kind 2 was `StdioKindCollect` in one and
/// `StdinKindBytes` in the other. Add a sixth kind to one set and not the other and the two
/// targets silently disagree about whether a spawn gets a pipe or the NUL device — no
/// compile error, no failing test on the host that built it, just a child reading EOF on
/// the lane nobody ran.
///
/// ⇒ A new kind is added HERE and nowhere else, and every emitter picks it up by
/// construction. `maxon-shv2`'s own copy (`SubpStdinHold` / `SubpStdinDelayed` in
/// `GtRuntime.maxon`) is structurally forced — a different language — and cross-references
/// this contract rather than restating its reasoning.
internal static class SubprocessStdin {
	/// A pipe with a payload queued behind it. Shares its number with the OUTPUT side's
	/// `collect`, which is a genuine dual meaning of one wire value and not a duplicate:
	/// on stdin kind 2 means "feed the child these bytes", on stdout/stderr it means
	/// "capture what the child writes".
	internal const int StdinKindBytes = 2;

	/// A pipe the parent holds OPEN and never writes to, so the child blocks on a read
	/// instead of seeing EOF. It shares the `bytes` pipe body and differs in exactly one
	/// thing — it never has a payload, so no feed thread is started and the write end
	/// stays in the handle struct until `release_handle` closes it.
	internal const int StdinKindHold = 4;

	/// `hold`'s wait FOLLOWED BY `bytes`'s delivery: same pipe, same payload copy, same
	/// feed thread, except the feed sleeps `StdinDelayedFeedMs` before its first write —
	/// so the child's read blocks in the kernel for that long and THEN completes.
	internal const int StdinKindDelayed = 5;

	/// How long `StdinKindDelayed` makes the child wait. Fixed rather than a spawn
	/// argument because the spawn contract carries a `limit` slot for stdout and stderr
	/// and none for stdin — see `InputSource.delayed`'s note in the stdlib.
	internal const int StdinDelayedFeedMs = 1000;

	/// The stdin kinds that want a parent↔child pipe. ⚠ A kind that reaches the feed path
	/// without being in this set silently gets the NUL device instead, which is how
	/// `delayed` came to read as instant EOF on one of the two runtimes.
	internal static readonly int[] StdinKindsWantingPipe = { StdinKindBytes, StdinKindHold, StdinKindDelayed };
}
