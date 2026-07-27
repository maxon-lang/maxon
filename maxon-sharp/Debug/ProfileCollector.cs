namespace MaxonSharp.Debug;

/// <summary>
/// Turns captured stacks into a <see cref="ProfileReport"/>: instruction pointers become `.text` code
/// offsets, offsets become function names through the sidecar, and names accumulate into a call tree.
///
/// ⚠ It is driven from the SAMPLER's thread and holds no lock, so <see cref="Finish"/> may only be
/// called once that thread has been joined. The runner is the one caller and does exactly that; a
/// collector that locked per sample would put a lock on a path that runs at the sampling rate for no
/// gain, since nothing reads these tables until the run is over.
/// </summary>
internal sealed class ProfileCollector(MxdbgReader sidecar, ulong textBase, uint textSize,
    ProfileModuleMap modules) {

  /// A mutable tree node. The report's own <see cref="ProfileTreeNode"/> is immutable and sorted, which
  /// is right for something that is read; this is what a million insertions need.
  private sealed class Node(string function) {
    public readonly string Function = function;
    public long Samples;
    public readonly Dictionary<string, Node> Children = new(StringComparer.Ordinal);
  }

  private readonly Dictionary<string, Node> _roots = new(StringComparer.Ordinal);
  private readonly Dictionary<string, long> _selfSamples = new(StringComparer.Ordinal);
  private readonly Dictionary<string, long> _totalSamples = new(StringComparer.Ordinal);
  private readonly Dictionary<(ulong Base, string Root), long> _stacks = [];

  /// Reused across samples so aggregating a sample allocates only when it discovers something NEW — a
  /// function or a call path never seen before. At a kilohertz over minutes, a per-sample list would be
  /// the collector's dominant cost.
  private readonly List<string> _frames = [];
  private readonly HashSet<string> _seenInStack = new(StringComparer.Ordinal);

  private long _samples;
  private long _samplesUnsymbolized;
  private long _stacksUnreadable;
  private long _stacksTruncated;

  /// The allocation base of the stack the sample being folded came from — see <see cref="Record"/>.
  private ulong _stackBase;

  /// <summary>
  /// Accept one captured stack; this is the sink handed to <see cref="ProfileSampler.Run"/>.
  /// <paramref name="programCounters"/> is leaf-first, as the walk produced it; the tree is built
  /// root-first, so it is consumed in reverse.
  /// </summary>
  public void Accept(ProfileStackSample sample, ReadOnlySpan<ulong> programCounters) {
    _samples++;
    if (sample.Truncated) _stacksTruncated++;
    if (sample.StackUnreadable) _stacksUnreadable++;
    _stackBase = sample.StackAllocationBase;

    _frames.Clear();
    ResolveFrames(programCounters);

    if (_frames.Count == 0) _frames.Add(ProfileRender.UnreadableStackFrame);

    // The LEAF is what decides whether this sample was symbolized: it is the code that was actually
    // executing, and it is the only frame whose name the profile attributes SELF time to.
    if (ProfileRender.IsSynthetic(_frames[0])) _samplesUnsymbolized++;

    Record();
  }

  /// <summary>
  /// Program counters as frame names, leaf first.
  ///
  /// The walk STOPS at the first address outside this binary's `.text`, and that single rule covers two
  /// different things honestly. At the ROOT it is simply where our code ends — the outermost Maxon
  /// frame returns into the C runtime or the thread starter — so stopping there is what makes a stack's
  /// root its entry function rather than a foreign symbol we cannot name. At the LEAF it means the
  /// sample landed in a DLL or the kernel, and the chain above it cannot be trusted at all: foreign
  /// code has no obligation to maintain a frame pointer, so following RBP out of it would produce a
  /// return address that might well resolve to a real function of ours and attribute the sample to a
  /// caller that never called it. One unnamed frame is the honest answer; a plausible wrong one is not.
  /// </summary>
  private void ResolveFrames(ReadOnlySpan<ulong> programCounters) {
    for (int i = 0; i < programCounters.Length; i++) {
      if (!TryCodeOffset(programCounters[i], out uint offset)) {
        // Only the leaf earns a placeholder: past it, leaving `.text` is the end of our stack, not a
        // frame to report. It is named by its MODULE where one can be found, because "52% of this
        // profile was somewhere else" is a fact a reader cannot act on and "52% was in ntdll.dll" is.
        if (i == 0) _frames.Add(ProfileRender.ForeignFrame(modules.NameAt(programCounters[i])));
        return;
      }

      // Frames above the leaf are RETURN addresses, so they resolve through the one call-site bias the
      // debugger's own symbolizer uses — without it, a call that is a function's last instruction names
      // the function AFTER it.
      uint lookup = i == 0 ? offset : MxdbgReader.CallSiteLookup(offset);
      var function = sidecar.FunctionAt(lookup);
      _frames.Add(function is { Name.Length: > 0 } named ? named.Name : ProfileRender.UnnamedTextFrame);
    }
  }

  /// <summary>
  /// An absolute instruction pointer as a `.text` code offset — the out-of-process twin of the agent's
  /// <c>__dbg_text_offset</c>, and rejecting exactly what that rejects. The subtraction is unsigned, so
  /// an address BELOW the code section reads as a huge offset and is refused by the same single bound.
  /// </summary>
  private bool TryCodeOffset(ulong programCounter, out uint offset) {
    ulong relative = programCounter - textBase;
    offset = (uint)relative;
    return relative < textSize;
  }

  /// Fold the resolved frames into the three aggregations. Every one of them is derived from the SAME
  /// frame list, so the hot-function table, the tree and the stack list cannot disagree about what was
  /// sampled.
  private void Record() {
    _selfSamples[_frames[0]] = _selfSamples.GetValueOrDefault(_frames[0]) + 1;

    // Counted once per STACK: a recursive function appears many times in one chain, and adding it each
    // time would report a function as having been sampled more often than the profiler sampled at all.
    _seenInStack.Clear();
    foreach (var frame in _frames)
      if (_seenInStack.Add(frame)) _totalSamples[frame] = _totalSamples.GetValueOrDefault(frame) + 1;

    var children = _roots;
    Node? node = null;
    for (int i = _frames.Count - 1; i >= 0; i--) {
      var name = _frames[i];
      if (!children.TryGetValue(name, out node)) children[name] = node = new Node(name);
      node.Samples++;
      children = node.Children;
    }

    var key = (Base: _stackBase, Root: OutermostNamedFrame());
    _stacks[key] = _stacks.GetValueOrDefault(key) + 1;
  }

  /// <summary>
  /// The name a STACK is known by: its outermost NAMED frame.
  ///
  /// Deliberately not simply its outermost frame, and the difference is the whole usefulness of the
  /// stack table. Every stack in a Maxon program bottoms out in unnamed runtime code — `mrt_start`
  /// below `main` on the process's own thread, the spawn trampoline below a green thread's entry
  /// function — and none of that is in the sidecar's function table, which records the functions a
  /// PROGRAMMER wrote. Naming stacks by their true root would therefore label every one of them
  /// `[unnamed .text]` and distinguish nothing; the outermost named frame is exactly the entry point
  /// somebody meant to start.
  ///
  /// It is also the second half of the stack's IDENTITY, and that is what makes the allocation base
  /// safe to key on. A base alone would merge two green threads that never coexisted, because a
  /// completed thread's stack goes back to the allocator and the next spawn can be handed the same
  /// address. Pairing it with the entry function splits those, while still MERGING the one case that
  /// should merge: `__gt_morestack` relocates a growing stack to a new address, and the thread it
  /// belongs to is unmistakably the same thread — same entry, and the old base is gone.
  /// </summary>
  private string OutermostNamedFrame() {
    for (int i = _frames.Count - 1; i >= 0; i--)
      if (!ProfileRender.IsSynthetic(_frames[i])) return _frames[i];

    return _frames[^1];
  }

  /// <summary>
  /// The finished report. Every list is sorted by share and then by NAME, so a tie between two equally
  /// hot rows resolves the same way on every run — a report whose row order flipped between runs could
  /// not be diffed, which is most of what a profile is for.
  /// </summary>
  public ProfileReport Finish(string exePath, int? targetExitCode, bool runCompleted, double requestedRateHz,
      long ticks, double durationSeconds, double minPercent) =>
    new(exePath, targetExitCode, runCompleted, requestedRateHz, ticks, durationSeconds,
      _samples, _samplesUnsymbolized, _stacksUnreadable, _stacksTruncated, minPercent,
      [.. _selfSamples.Keys.Union(_totalSamples.Keys, StringComparer.Ordinal)
        .Select(name => new ProfileFunction(name, _selfSamples.GetValueOrDefault(name),
          _totalSamples.GetValueOrDefault(name)))
        .OrderByDescending(f => f.SelfSamples).ThenBy(f => f.Name, StringComparer.Ordinal)],
      [.. Freeze(_roots)],
      [.. _stacks.Select(s => new ProfileStack(s.Key.Root, s.Value))
        .OrderByDescending(s => s.Samples).ThenBy(s => s.RootFunction, StringComparer.Ordinal)]);

  private static List<ProfileTreeNode> Freeze(Dictionary<string, Node> nodes) =>
    [.. nodes.Values
      .OrderByDescending(n => n.Samples).ThenBy(n => n.Function, StringComparer.Ordinal)
      .Select(n => new ProfileTreeNode(n.Function, n.Samples, Freeze(n.Children)))];
}
