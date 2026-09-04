namespace MaxonSharp.Compiler.Ir.Runtime;

/// The end-state a streaming child's stdout/stderr reader will answer next, stated ONCE for every
/// target that emits it. The authority is `stdlib/Builtins.maxon`'s `__SubprocessStreamState`, whose
/// declaration order IS this ordinal column; `2-Parser.cs` builds its pre-registration from these
/// constants and checks the corpus declaration against it, so all three copies are tied together.
///
/// It is shared for the reason `SubprocessStdin` is: two emitters carrying their own copies of one
/// language-level ordinal drift silently, and the failure mode is a wrong answer on the lane nobody
/// ran rather than a build break.
///
/// `SubpStreamNoSuchChild` is never STORED — it is what a query answers from its handle guard, so a
/// state word only ever holds one of the other three.
internal static class SubprocessStreamState {
	internal const int SubpStreamOpen = 0;
	internal const int SubpStreamAtEof = 1;
	internal const int SubpStreamReadFailed = 2;
	internal const int SubpStreamNoSuchChild = 3;

	/// The case names, in ordinal order, that `stdlib/Builtins.maxon` must declare.
	internal static readonly string[] SubpStreamCaseNames = ["open", "atEof", "readFailed", "noSuchChild"];
}

/// What a stream refill answers when it did not read at all because the calling green thread has
/// been cancelled. Distinct from every byte count, from `0` (a stream that ended) and from `-1` (a
/// stream that failed), because it is none of those: nothing was transferred and the pipe is exactly
/// as the next reader will find it.
///
/// ⇒ THE LATCH MUST STORE NOTHING FOR IT. A dropped promise leaves the child's stdout open, so a
/// latched `atEof` or `readFailed` would tell the next reader the child had finished — the parent's
/// own bookkeeping inventing an end on a stream nothing happened to.
///
/// The readers still stop on any non-positive answer, so termination is unchanged on x64; on arm64,
/// where the read loop is driven by the state word rather than by the refill's return, the refill
/// hands this value back and each reader leaves through its stopped-emit tail.
internal static class SubprocessReadOutcome {
	internal const int SubpReadCancelled = -2;
}
