using System.Diagnostics;
using System.Runtime.InteropServices;
using GtLayout = MaxonSharp.Compiler.Ir.Runtime.GtLayout;

namespace MaxonSharp.Debug;

/// <summary>
/// One captured stack. <see cref="StackAllocationBase"/> is the identity of the STACK the sample came
/// from (see <see cref="ProfileSampler"/>'s note on grouping); the frames themselves travel beside this
/// record as a span, so a sample costs no allocation at all.
///
/// <see cref="Truncated"/> and <see cref="StackUnreadable"/> are reported rather than swallowed, because
/// each makes the sample MEAN something different: a truncated walk has lost its OUTERMOST frames, so it
/// cannot be attributed to a root, and an unreadable stack has only its leaf.
/// </summary>
internal readonly record struct ProfileStackSample(ulong StackAllocationBase, bool Truncated, bool StackUnreadable);

/// <summary>
/// Where one sample went. A span cannot travel through <c>Action&lt;&gt;</c>, and copying every sample
/// into a list purely to hand it over would allocate once per sample at the sampling rate.
/// </summary>
internal delegate void ProfileSampleSink(ProfileStackSample sample, ReadOnlySpan<ulong> programCounters);

/// <summary>
/// A target this host cannot sample. Thrown at ATTACH, before anything is launched, so a refusal names
/// the missing mechanism rather than a profile coming back mysteriously empty.
/// </summary>
internal sealed class ProfileUnsupportedException(string message) : Exception(message);

/// <summary>
/// The sampling engine: suspend a target thread, read its registers, copy a window of its stack, resume,
/// and walk the copy. Everything OS-specific about profiling lives here.
///
/// ⭐ WHY THIS IS ALLOWED WHERE P4d's PARK WAS NOT.
///
/// P4d REFUSED <c>SuspendThread</c>, and its reason was right for what it was refusing: parking a thread
/// FOR INSPECTION can stop an M inside the allocator or holding the scheduler lock, and a held thread
/// holds that lock for as long as the user stares at the stopped program — so the rest of the program
/// deadlocks against a debugger that is working perfectly.
///
/// Sampling is a different risk, and only because it is kept different. This suspends a thread for THREE
/// SYSCALLS — <c>GetThreadContext</c>, <c>VirtualQueryEx</c>, <c>ReadProcessMemory</c> — and resumes it.
/// Nothing waits on the thread in that window, so a target caught mid-allocator is resumed before any
/// other thread can block on the lock it holds. Concretely, the three rules this engine is bound by:
///
///   1. IT ALLOCATES NOTHING IN THE TARGET, and 2. IT TAKES NO LOCK THE TARGET COULD HOLD. Both hold
///      STRUCTURALLY rather than by discipline, which is the real reason this design was chosen over an
///      in-process one: the sampler is in ANOTHER PROCESS and never calls a single instruction of the
///      debuggee. There is no path from here into <c>mm_alloc</c> or the scheduler because there is no
///      path from here into the target's code at all. (The kernel side is safe for a reason worth
///      stating: on Windows <c>SuspendThread</c> stops a thread at the USER/KERNEL BOUNDARY, never
///      inside a kernel call, so a suspended thread cannot be holding the address-space lock that this
///      process's own <c>VirtualQueryEx</c> / <c>ReadProcessMemory</c> then take.)
///   3. IT ALWAYS RESUMES. This is the one rule the structure does NOT give for free, so it is enforced
///      at the single place a thread is ever suspended (<see cref="SampleThread"/>): the suspend is
///      followed immediately by a <c>try</c> whose <c>finally</c> resumes, so every failure inside —
///      a thread that exited between the suspend and the read, a <c>GetThreadContext</c> that fails, a
///      <c>VirtualQueryEx</c> miss, an unreadable stack, or an exception nobody predicted — leaves
///      through the resume. A sampler that can leave a thread suspended is a deadlock generator.
///
/// ⭐ WHY SAMPLING OS THREADS SEES GREEN THREADS. <c>__gt_context_switch</c> saves and restores BOTH RSP
/// AND RBP (X86CodeEmitter.Runtime.cs), so an OS worker executing a green thread has its registers
/// pointing into that green thread's own stack: the register capture below IS the green thread's, and
/// the frame-pointer walk IS its backtrace. Nothing has to enumerate green threads to see them, which
/// matters because the only mechanism that CAN enumerate them — the agent's <c>DbgCmdGtList</c> — is
/// answered exclusively by a PARKED target (`MaxonDebugger.RingDoorbellAndAwaitAck`: "The mailbox is
/// only serviced by a PARKED target"), and parking per sample is precisely P4d's refused design.
///
/// ⭐ WHY THE ALLOCATION BASE IDENTIFIES A STACK. Every green thread's stack is its own reservation
/// (GtLayout's stack_base/stack_size), and so is every OS thread's, so two CONCURRENTLY LIVE stacks can
/// never share a Win32 allocation base. <c>AllocationBase</c> is used rather than <c>BaseAddress</c>
/// because one stack reservation is split into committed, guard and reserved regions with different base
/// addresses but a single allocation base — the identity has to be the thing that does not change as the
/// stack grows into its guard page. What this does NOT survive is stated honestly where it is consumed
/// (<see cref="ProfileCollector"/>): a freed stack's address can be handed to a later thread, so the unit
/// is A STACK, not a green thread's whole life.
/// </summary>
internal sealed class ProfileSampler : IDisposable {

  /// <summary>
  /// The most stack ONE sample copies out of the target. Frames are tens of bytes, so 64 KB is
  /// thousands of them — far past <see cref="MaxWalkFrames"/> — while keeping the copy small enough that
  /// sampling at 1 kHz moves single-digit MB/s. A thread stack's RESERVATION is up to a megabyte, and
  /// copying that per sample would cost more than the program being measured.
  /// </summary>
  private const int MaxStackWindowBytes = 64 * 1024;

  /// <summary>
  /// The deepest chain one sample walks. Generous next to the agent's own 32
  /// (<see cref="Compiler.Ir.Runtime.GtLayout.MaxBacktraceFrames"/>), because a truncated PROFILE stack
  /// is worse than a truncated backtrace: the walk runs leaf-to-root, so losing the tail loses the ROOT,
  /// and a stack with no root cannot be attributed to the call tree at all. Samples that reach this are
  /// counted rather than quietly rooted at the wrong place.
  /// </summary>
  private const int MaxWalkFrames = 256;

  /// <summary>
  /// How often the target's thread list is re-enumerated, as opposed to how often it is SAMPLED.
  ///
  /// They are different rates on purpose: the scheduler creates its workers at <c>__gt_init</c> and
  /// rarely again, so the answer barely changes between ticks. A thread that appears later is picked up
  /// within this interval, and one that EXITS is noticed immediately without any enumeration, because
  /// its handle starts failing (see <see cref="SampleThread"/>).
  /// </summary>
  private static readonly TimeSpan ThreadListRefreshInterval = TimeSpan.FromMilliseconds(100);

  private readonly Process _process;
  private readonly nint _processHandle;

  /// The CONTEXT record <c>GetThreadContext</c> fills. Allocated ONCE and 16-byte aligned by hand:
  /// x64 CONTEXT contains XMM state and the API rejects a misaligned pointer, which
  /// <see cref="Marshal.AllocHGlobal"/> does not promise.
  private readonly nint _contextAllocation;
  private readonly nint _context;

  /// Signalled by <see cref="Stop"/> so the tick wait ends AT the stop rather than at its next deadline
  /// — see <see cref="WaitableTimer"/>.
  private readonly nint _stopEvent;

  /// The copied stack window, reused by every sample so the sampling loop allocates nothing per tick.
  private readonly byte[] _stackWindow = new byte[MaxStackWindowBytes];

  /// The walk's output, reused for the same reason.
  private readonly ulong[] _programCounters = new ulong[MaxWalkFrames];

  /// <summary>
  /// One target thread: the handle to act on, and the cycle count at the previous tick.
  ///
  /// <see cref="LastCycleTime"/> is what makes this a CPU profiler rather than a wall-clock one. An idle
  /// scheduler worker is parked in <c>WaitForSingleObject(P-&gt;wakeEvent, 100ms)</c> and burns no
  /// cycles; sampling it anyway would attribute a share of the profile to a thread that did not run, and
  /// on a mostly-idle program that share is most of the profile. A thread whose cycle count has not
  /// moved since the last tick is therefore skipped ENTIRELY — not even suspended, so an idle worker is
  /// not perturbed by the act of measuring it.
  /// </summary>
  private sealed class TargetThread(nint handle) {
    public readonly nint Handle = handle;
    public ulong LastCycleTime;
    public bool Seeded;
  }

  private readonly Dictionary<int, TargetThread> _threads = [];

  /// The ids the last enumeration listed, reused so a refresh allocates nothing — see
  /// <see cref="RefreshThreadList"/>.
  private readonly HashSet<int> _liveThreadIds = [];

  private volatile bool _stop;

  /// <summary>
  /// How many times the loop actually TICKED. It is what the report divides by the run's duration to
  /// state the rate the sampler ACHIEVED, as opposed to the one a caller asked for.
  ///
  /// ⭐ A TICK, not a sample, and the distinction is the whole reason this is counted rather than
  /// derived. `--rate` asks for a TICK rate; one tick samples every thread that has RUN since the last
  /// one, which is neither one sample nor a fixed number of them. `Samples / Duration` therefore drifts
  /// from the tick rate in BOTH directions and by an amount that depends on the program: upwards when
  /// several threads run at once, and downwards when none has (measured on the two-processor
  /// green-thread sample, whose threads happen to run sequentially: 915 ticks against 870 samples).
  /// That ratio is a real quantity — samples per second — but it is not the one the flag names, and
  /// printing it beside the flag's own word would be a second wrong answer in the line the first one
  /// was found in.
  ///
  /// Written from the sampling thread and read after it is joined, which is the happens-before edge
  /// that makes a plain field safe here (the same one <see cref="ProfileCollector"/> relies on).
  /// </summary>
  public long Ticks { get; private set; }

  private ProfileSampler(Process process, nint processHandle, nint contextAllocation, nint context,
      nint stopEvent) {
    _process = process;
    _processHandle = processHandle;
    _contextAllocation = contextAllocation;
    _context = context;
    _stopEvent = stopEvent;
  }

  /// <summary>
  /// Why this host cannot sample, or null when it can.
  ///
  /// Stated as the MISSING MECHANISM rather than as "unsupported", because the two send a reader
  /// somewhere different and only one of them is actionable. macOS has the equivalent primitives —
  /// <c>thread_suspend</c> / <c>thread_get_state</c> / <c>mach_vm_read</c> — but they are reached
  /// through <c>task_for_pid</c>, which is privileged and needs either root or a signed entitlement, so
  /// a port is an ENTITLEMENT question and not a coding one. Answering with an empty profile instead
  /// would be the "instrument that lies" this workstream keeps refusing.
  /// </summary>
  public static string? UnsupportedReason =>
    !OperatingSystem.IsWindows()
      ? $"`maxon profile` samples by suspending the target's threads, which on {RuntimeInformation.OSDescription}"
        + " needs task_for_pid — a privileged Mach call requiring root or a signed entitlement. Only the"
        + " Windows sampler is implemented, so there is nothing here that could measure this program."
    : RuntimeInformation.ProcessArchitecture != Architecture.X64
      // The CONTEXT offsets below are x64's, and GetThreadContext fills whatever record the CALLING
      // process's architecture selects. On a native arm64 build they would name three unrelated words
      // as Rip/Rsp/Rbp — and nothing downstream could tell: the walk would simply symbolize garbage
      // into a confident, wrong profile. Unreachable from the win-x64 publish this ships as, which is
      // exactly why it needs to be a refusal rather than a comment.
      ? $"`maxon profile` reads the target's registers through the x64 CONTEXT record, and this build of"
        + $" maxon is {RuntimeInformation.ProcessArchitecture}. No register layout for that architecture"
        + " is implemented, and reading it with x64 offsets would produce a profile rather than an error."
    : !HasThreadWalk
      ? "`maxon profile` enumerates the target's threads through ntdll's NtGetNextThread, which this"
        + " system's ntdll.dll does not export. The documented alternative snapshots every thread on the"
        + " machine, which costs more than the sampling itself, so there is no usable sampler here."
    : null;

  /// <summary>
  /// Whether this ntdll exports the one non-Win32 entry point the sampler needs (see
  /// <see cref="RefreshThreadList"/> for why nothing documented will do). Asked here, with the other
  /// reasons a host cannot be sampled, so a missing export is a refusal BEFORE a program is launched
  /// rather than a sampler that dies on its first refresh.
  /// </summary>
  private static bool HasThreadWalk =>
    NativeLibrary.TryLoad("ntdll.dll", out nint ntdll)
    && NativeLibrary.TryGetExport(ntdll, nameof(NtGetNextThread), out _);

  /// <summary>
  /// Attach to an already-running <paramref name="process"/>. Refuses on a host with no sampler rather
  /// than returning one that collects nothing.
  /// </summary>
  public static ProfileSampler Attach(Process process) {
    if (UnsupportedReason is { } reason) throw new ProfileUnsupportedException(reason);

    // Over-allocate and align by hand: GetThreadContext rejects a CONTEXT that is not 16-byte aligned,
    // and AllocHGlobal guarantees only pointer alignment.
    nint allocation = Marshal.AllocHGlobal(ContextSize + ContextAlignment);
    nint context = (nint)(((ulong)allocation + ContextAlignment - 1) & ~(ulong)(ContextAlignment - 1));
    nint stopEvent = 0;

    try {
      // MANUAL-reset: once the run is over the event stays signalled, so a tick that reaches the wait
      // after Stop() has already returned still leaves immediately instead of sleeping out an interval
      // nobody is waiting for.
      stopEvent = CreateEventW(0, manualReset: true, initialState: false, null);
      if (stopEvent == 0)
        throw new ProfileUnsupportedException(
          $"cannot create the sampler's stop event: Win32 error {Marshal.GetLastWin32Error()}");

      return new ProfileSampler(process, process.Handle, allocation, context, stopEvent);
    } catch {
      if (stopEvent != 0) CloseHandle(stopEvent);
      Marshal.FreeHGlobal(allocation);
      throw;
    }
  }

  /// <summary>
  /// Ask the loop to finish after the tick it is on. The runner calls this once the target has exited or
  /// its deadline has passed; the loop also stops on its own when the target goes away.
  ///
  /// The event matters as much as the flag, because a flag alone cannot interrupt a wait. Without it the
  /// sampling thread sat out its whole remaining tick before noticing, so the tool hung for up to ONE
  /// SAMPLING INTERVAL after the program had ended and — worse, because it is silent — the run's
  /// measured window was inflated by that same wait. MEASURED at <c>--rate=1</c>: a program that ran for
  /// 0.947 s was reported as "over 1.0018s", the interval rounded up. At the default kilohertz that is a
  /// millisecond and invisible; at the low rates a long-running service is profiled with, it is the
  /// whole error.
  /// </summary>
  public void Stop() {
    _stop = true;
    SetEvent(_stopEvent);
  }

  public void Dispose() {
    foreach (var thread in _threads.Values) CloseHandle(thread.Handle);
    _threads.Clear();
    CloseHandle(_stopEvent);
    Marshal.FreeHGlobal(_contextAllocation);
  }

  /// <summary>
  /// Sample until <see cref="Stop"/> is called or the target exits, delivering every captured stack to
  /// <paramref name="sink"/>. Runs on its own thread, owned by the caller.
  ///
  /// The cadence comes from a high-resolution waitable timer armed to an ABSOLUTE deadline each tick, so
  /// the schedule cannot drift: a tick that arrives late does not push the next one later. Sampling from
  /// a sleep loop instead would drift by however long each round of sampling took, which is exactly the
  /// quantity that varies with what the target is doing — a sampler whose RATE depends on the program is
  /// measuring itself.
  /// </summary>
  public void Run(TimeSpan interval, ProfileSampleSink sink) {
    using var timer = WaitableTimer.Create(_stopEvent);

    long intervalTicks = Math.Max(1, interval.Ticks);
    long deadline = DateTime.UtcNow.ToFileTimeUtc();
    var lastThreadRefresh = TimeSpan.FromTicks(-ThreadListRefreshInterval.Ticks);
    var clock = Stopwatch.StartNew();

    while (!_stop) {
      if (clock.Elapsed - lastThreadRefresh >= ThreadListRefreshInterval) {
        RefreshThreadList();
        lastThreadRefresh = clock.Elapsed;
      }

      SampleOnce(sink);
      Ticks++;

      // The target exiting is checked AFTER a sample, so the last moments of a short program are
      // measured rather than lost to a liveness check that happened to win the race.
      if (_stop || _process.HasExited) return;

      deadline += intervalTicks;
      timer.WaitUntil(deadline);
    }
  }

  /// <summary>
  /// Re-enumerate the target's threads, keeping a handle for each new one. A thread that has gone away
  /// is dropped HERE only if the enumeration no longer lists it; the sampling path drops it sooner, on
  /// the first call its handle refuses.
  ///
  /// ⭐ IT WALKS THE TARGET'S THREAD LIST, NOT THE MACHINE'S, and that is the whole reason
  /// <c>NtGetNextThread</c> is used instead of a Toolhelp snapshot.
  ///
  /// <c>CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, …)</c> is documented to IGNORE the process id it is
  /// handed: it snapshots every thread on the system, and the caller filters. That makes the cost of
  /// learning six thread ids proportional to how busy the whole BOX is — which for a profiler is the
  /// worst property an operation can have, because the instrument's own cost then depends on everything
  /// it is not measuring. MEASURED on this machine: 7,198 system threads, 43–59 ms per snapshot to find
  /// the target's 6, against 19–26 us for the walk below — and at a refresh every 100 ms that snapshot
  /// was 335 ms of every second, 88% of the sampling thread's entire budget. Two visible consequences,
  /// both gone with it: the loop could not tick faster than ~1.1 kHz whatever <c>--rate</c> asked for
  /// (so a report headed "at 5000 Hz" was collected at 1,102), and each 50 ms stall was repaid by the
  /// absolute-deadline schedule as a BURST of ~50 back-to-back samples — 39% of one run's samples
  /// arrived in 7 such bursts, which for a statistical sampler is not 50 readings but one.
  ///
  /// <c>NtGetNextThread</c> is an ntdll export rather than a documented Win32 one, which is why
  /// <see cref="UnsupportedReason"/> checks for it up front rather than letting a missing export surface
  /// as a sampler that stopped. It hands back a handle already carrying the access asked for, so there
  /// is no <c>OpenThread</c> here either.
  /// </summary>
  private void RefreshThreadList() {
    _liveThreadIds.Clear();

    // The walk's cursor is a handle to the thread just returned. One of two things happens to it: it is
    // KEPT (a thread not seen before, so `_threads` takes ownership) or it is a second handle to a
    // thread already tracked and must be closed — but only AFTER the next call has used it to find its
    // successor, which is what `spare` defers.
    nint cursor = 0;
    nint spare = 0;

    while (NtGetNextThread(_processHandle, cursor, ThreadSampleAccess, 0, 0, out nint next) == StatusSuccess) {
      // Cleared as well as closed. A handle VALUE is recycled the moment it is closed, so carrying a
      // stale one forward would eventually close something else this sampler owns.
      if (spare != 0) {
        CloseHandle(spare);
        spare = 0;
      }

      cursor = next;

      uint threadId = GetThreadId(cursor);
      if (threadId != 0 && _liveThreadIds.Add((int)threadId) && !_threads.ContainsKey((int)threadId))
        _threads[(int)threadId] = new TargetThread(cursor);
      else
        spare = cursor;
    }

    if (spare != 0) CloseHandle(spare);

    // A thread the walk did not list has exited. Collected into a list first because the dictionary is
    // being read while it is edited.
    foreach (var id in _threads.Keys.Where(id => !_liveThreadIds.Contains(id)).ToList()) {
      CloseHandle(_threads[id].Handle);
      _threads.Remove(id);
    }
  }

  /// One tick: every known thread that has actually consumed CPU since the last tick.
  private void SampleOnce(ProfileSampleSink sink) {
    List<int>? dead = null;

    foreach (var (id, thread) in _threads) {
      if (!QueryThreadCycleTime(thread.Handle, out ulong cycles)) {
        (dead ??= []).Add(id);
        continue;
      }

      // A thread's FIRST sighting seeds the baseline and is not sampled: with nothing to compare
      // against, "has it run?" is unanswerable, and guessing yes would attribute a sample to a worker
      // that has been parked since the process started. One tick of latency per thread, once.
      bool ran = thread.Seeded && cycles != thread.LastCycleTime;
      thread.LastCycleTime = cycles;
      thread.Seeded = true;
      if (!ran) continue;

      if (!SampleThread(thread.Handle, sink)) {
        (dead ??= []).Add(id);
        continue;
      }

      // ⭐ RE-BASELINE AFTER SAMPLING, or the sampler measures ITSELF.
      //
      // Suspending and resuming a thread costs that thread kernel cycles, and QueryThreadCycleTime
      // counts them. Baselining only before the sample therefore closes a feedback loop: sample a
      // thread once for any reason, and the sampling itself advances its counter, so the next tick sees
      // "it ran" and samples it again — forever, whether or not the thread ever executes an
      // instruction of its own.
      //
      // MEASURED, and it is the reason this line exists: a two-processor async program whose idle
      // worker is parked in a 100 ms wait was reported as spending 52% of the run in ntdll.dll — the
      // largest row in the profile — while the process as a whole consumed 0.98 cores over 1.05 s wall,
      // i.e. exactly ONE running thread. The parked worker's first timeout wakeup latched the loop and
      // the profiler then sampled it at nearly the full rate for the rest of the run, manufacturing the
      // single biggest number in its own report.
      if (QueryThreadCycleTime(thread.Handle, out ulong settled)) thread.LastCycleTime = settled;
    }

    if (dead == null) return;

    foreach (var id in dead) {
      CloseHandle(_threads[id].Handle);
      _threads.Remove(id);
    }
  }

  /// <summary>
  /// Capture ONE stack. False means the thread is gone and should be dropped.
  ///
  /// ⭐ THIS IS THE ONLY PLACE A THREAD IS EVER SUSPENDED, and the <c>try</c> starts on the very next
  /// statement after the suspend so that rule 3 cannot be broken by anything added later: every exit
  /// from the captured region — a failed context read, a region query that misses, an unreadable stack,
  /// or an exception — leaves through the <c>finally</c>. The failure of a <c>SuspendThread</c> itself
  /// returns BEFORE the try, which is correct precisely because nothing was suspended.
  /// </summary>
  private bool SampleThread(nint handle, ProfileSampleSink sink) {
    // -1 is the documented failure of SuspendThread, and the only way it happens here is a thread that
    // exited since the last tick. Nothing was suspended, so there is nothing to resume — which is why
    // this return is ABOVE the try rather than inside it.
    if (SuspendThread(handle) == unchecked((uint)-1)) return false;

    bool registersRead = false;
    ulong pc = 0;
    ulong framePointer = 0;
    ulong stackPointer = 0;
    ulong allocationBase = 0;
    int windowBytes = 0;

    try {
      Marshal.WriteInt32(_context, ContextFlagsOffset, ContextControl | ContextInteger);
      registersRead = GetThreadContext(handle, _context);

      if (registersRead) {
        pc = (ulong)Marshal.ReadInt64(_context, ContextRipOffset);
        framePointer = (ulong)Marshal.ReadInt64(_context, ContextRbpOffset);
        stackPointer = (ulong)Marshal.ReadInt64(_context, ContextRspOffset);

        // The stack's own extent, which bounds the copy AND identifies the stack. A miss means the
        // stack pointer does not address mapped memory — a thread caught with a torn RSP mid-switch —
        // and the sample is reported unreadable rather than walked from a pointer nothing vouches for.
        if (VirtualQueryEx(_processHandle, (nint)stackPointer, out var region,
              MemoryBasicInformationSize) != 0) {
          allocationBase = (ulong)region.AllocationBase;
          ulong regionEnd = (ulong)region.BaseAddress + (ulong)region.RegionSize;
          windowBytes = (int)Math.Min(regionEnd > stackPointer ? regionEnd - stackPointer : 0,
            (ulong)MaxStackWindowBytes);

          // A partial copy is still a walkable PREFIX, and the walk bounds itself to whatever arrived,
          // so a stack that runs into a guard page yields its readable frames instead of nothing.
          if (windowBytes > 0
              && !ReadProcessMemory(_processHandle, (nint)stackPointer, _stackWindow, windowBytes, out var read))
            windowBytes = (int)read;
        }
      }
    } finally {
      // ⭐ RULE 3. Unconditional, and reached from every path through the block above — a failed context
      // read, a region query that missed, an unreadable stack, or an exception nobody predicted. The
      // target is running again before this method's caller sees anything at all.
      ResumeThread(handle);
    }

    // Everything below runs with the thread ALREADY RESUMED, over the copy. Nothing about symbolizing or
    // walking needs the target held, and holding it for the walk would multiply the suspension by the
    // depth of the stack.
    if (!registersRead) {
      sink(new ProfileStackSample(0, Truncated: false, StackUnreadable: true), ReadOnlySpan<ulong>.Empty);
      return true;
    }

    _programCounters[0] = pc;
    if (windowBytes <= 0) {
      sink(new ProfileStackSample(allocationBase, Truncated: false, StackUnreadable: true),
        _programCounters.AsSpan(0, 1));
      return true;
    }

    int count = WalkCopiedStack(framePointer, stackPointer, windowBytes);
    sink(new ProfileStackSample(allocationBase, Truncated: count == MaxWalkFrames, StackUnreadable: false),
      _programCounters.AsSpan(0, count));
    return true;
  }

  /// <summary>
  /// Follow the frame-pointer chain through the copied window, appending each frame's return address.
  /// Returns the total frame count including the leaf PC already in slot 0.
  ///
  /// The chain is exactly the one the agent's own <c>__dbg_walk_frames</c> follows — the saved frame
  /// pointer at <c>[fp]</c> and the return address at <c>[fp + 8]</c> — with the same two-word
  /// requirement (<see cref="Compiler.Ir.Runtime.GtLayout.FrameLinkBytes"/>) before either is read.
  ///
  /// Two things bound it, and they close different failures. The WINDOW bounds reads, so a frame pointer
  /// into the target's heap or into another stack simply ends the walk. STRICT INCREASE bounds cycles: a
  /// stack grows downwards, so a caller's frame is always at a higher address, and a corrupt chain that
  /// points backwards or at itself would otherwise spin forever.
  ///
  /// ⚠ What it cannot see, stated because it bounds the accuracy of every fp-based sampler: a sample
  /// landing inside a prologue (after <c>push rbp</c>, before <c>mov rbp, rsp</c>) or an epilogue (after
  /// <c>pop rbp</c>) reads the CALLER's frame pointer, so that one sample attributes the leaf correctly
  /// and misses its immediate caller. It is a per-sample error of one frame over a window of one or two
  /// instructions per call, and it does not accumulate.
  /// </summary>
  private int WalkCopiedStack(ulong framePointer, ulong stackPointer, int windowBytes) {
    int count = 1;
    ulong fp = framePointer;

    if (windowBytes < GtLayout.FrameLinkBytes) return count;

    // The largest offset into the copy at which BOTH words of a frame link still fit. Computed once, by
    // SUBTRACTING the link size from the bound rather than adding it to the pointer — see the guard.
    ulong lastFrameLinkOffset = (ulong)(windowBytes - GtLayout.FrameLinkBytes);

    while (count < MaxWalkFrames) {
      // The frame link is TWO words — the saved frame pointer and the return address — so the bound has
      // to leave room for both, read from the ONE constant the in-process walk states it with.
      //
      // ⚠ Compared in the WINDOW's own coordinates, and never as `fp + FrameLinkBytes > windowEnd`.
      // That form WRAPS, and the value that wraps it is not exotic: RBP is only a frame pointer by
      // CONVENTION, so a sample landing in foreign code that uses it as an ordinary callee-saved
      // register — holding −1, or an all-ones mask — arrives here as 0xFFFF_FFFF_FFFF_FFFF, whose sum
      // with 16 is 15. That passes an upper bound written the other way and then indexes the window at
      // a truncated, possibly negative, offset. MEASURED by forcing exactly that frame pointer: the
      // walk threw, and because the throw leaves the sampling thread the runner discards the ENTIRE
      // run as "the sampler stopped early" — one stray register value costing a whole profile.
      if (fp < stackPointer || fp - stackPointer > lastFrameLinkOffset) break;

      int offset = (int)(fp - stackPointer);
      ulong savedFp = BitConverter.ToUInt64(_stackWindow, offset);
      ulong returnAddress = BitConverter.ToUInt64(_stackWindow, offset + 8);
      if (returnAddress == 0) break;

      _programCounters[count++] = returnAddress;

      if (savedFp <= fp) break;
      fp = savedFp;
    }

    return count;
  }

  // ---- The high-resolution tick ----

  /// <summary>
  /// The waitable timer the sampling cadence comes from.
  ///
  /// <c>Thread.Sleep</c> is not usable for this: its resolution is the system timer tick — ~15.6 ms
  /// unless something on the machine has raised it — so a 1 ms sleep is a 15 ms sleep, and a profiler
  /// asking for 1 kHz would silently collect 64 Hz. A high-resolution waitable timer is what makes the
  /// default kilohertz real (measured: 996 Hz asked 1000) and, when the host predates it, the ordinary
  /// one still beats sleeping.
  ///
  /// ⚠ It is NOT exact, and this is where the sampler's real rate ceiling lives — see
  /// <see cref="ProfileRunner.MaxRateHz"/> for the measurement. Its granularity is ~0.5 ms, so a wait
  /// whose deadline has ALREADY PASSED still returns after ~520 us: past ~1.75 kHz the loop cannot tick
  /// faster however short the interval it is handed.
  /// </summary>
  private sealed class WaitableTimer : IDisposable {
    private readonly nint _timer;

    /// The timer AND the sampler's stop event, in one array built once: a per-tick wait must not
    /// allocate, and marshalling a fresh handle array at the sampling rate would.
    private readonly nint[] _waitOn;

    private WaitableTimer(nint timer, nint stopEvent) {
      _timer = timer;
      _waitOn = [timer, stopEvent];
    }

    public static WaitableTimer Create(nint stopEvent) {
      nint handle = CreateWaitableTimerExW(0, null, CreateWaitableTimerHighResolution, TimerAllAccess);
      if (handle == 0) handle = CreateWaitableTimerExW(0, null, 0, TimerAllAccess);
      if (handle == 0)
        throw new ProfileUnsupportedException(
          $"cannot create the sampling timer: Win32 error {Marshal.GetLastWin32Error()}");

      return new WaitableTimer(handle, stopEvent);
    }

    /// Wait until <paramref name="fileTimeUtc"/> — or until the run is stopped, whichever comes first. A
    /// POSITIVE due time is absolute, which is what keeps the schedule from drifting by however long the
    /// previous tick's work took; waiting on the stop event beside it is what keeps the SHUTDOWN from
    /// costing an interval (see <see cref="Stop"/>).
    public void WaitUntil(long fileTimeUtc) {
      if (!SetWaitableTimer(_timer, in fileTimeUtc, 0, 0, 0, false)) return;
      WaitForMultipleObjects((uint)_waitOn.Length, _waitOn, waitAll: false, InfiniteWait);
    }

    /// The stop event belongs to the sampler, which outlives every timer and closes it itself.
    public void Dispose() => CloseHandle(_timer);
  }

  // ---- Win32 ----

  private const uint InfiniteWait = 0xFFFFFFFF;

  /// STATUS_SUCCESS. <c>NtGetNextThread</c> ends a walk with STATUS_NO_MORE_ENTRIES, so anything but
  /// success — including a genuine failure — stops it, exactly as running out of threads does.
  private const int StatusSuccess = 0;

  /// Suspend/resume, read the context, and read the cycle counter — exactly the four things this engine
  /// does to a thread and nothing more. Also the access the thread walk asks for each handle it hands
  /// back, so an enumerated thread arrives ready to sample.
  private const uint ThreadSampleAccess = 0x0002 | 0x0008 | 0x0040 | 0x0800;

  private const uint CreateWaitableTimerHighResolution = 0x00000002;
  private const uint TimerAllAccess = 0x1F0003;

  /// x64 CONTEXT: 1232 bytes, 16-byte aligned. The offsets below are the three registers a stack walk
  /// needs; CONTEXT_CONTROL supplies Rip/Rsp and CONTEXT_INTEGER supplies Rbp.
  private const int ContextSize = 1232;
  private const int ContextAlignment = 16;
  private const int ContextFlagsOffset = 0x30;
  private const int ContextRspOffset = 0x98;
  private const int ContextRbpOffset = 0xA0;
  private const int ContextRipOffset = 0xF8;
  private const int ContextControl = 0x00100001;
  private const int ContextInteger = 0x00100002;

  private static readonly nint MemoryBasicInformationSize = Marshal.SizeOf<MemoryBasicInformation>();

  [StructLayout(LayoutKind.Sequential)]
  private struct MemoryBasicInformation {
    public nint BaseAddress;
    public nint AllocationBase;
    public uint AllocationProtect;
    public uint PartitionId;
    public nint RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;
  }

#pragma warning disable SYSLIB1054 // DllImport keeps this block uniform with Testing/WindowsJobObject.cs
  /// The target's OWN thread list, one thread per call. <paramref name="thread"/> is the previous
  /// result (0 to start) and is only READ — the walk does not take ownership of it.
  [DllImport("ntdll.dll")]
  private static extern int NtGetNextThread(nint process, nint thread, uint desiredAccess,
    uint handleAttributes, uint flags, out nint newThread);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern uint GetThreadId(nint thread);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern uint SuspendThread(nint thread);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern uint ResumeThread(nint thread);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool GetThreadContext(nint thread, nint context);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool QueryThreadCycleTime(nint thread, out ulong cycleTime);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool ReadProcessMemory(nint process, nint address, byte[] buffer, int size,
    out nint bytesRead);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern nint VirtualQueryEx(nint process, nint address,
    out MemoryBasicInformation buffer, nint length);

  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  private static extern nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool SetWaitableTimer(nint timer, in long dueTime, int period,
    nint completionRoutine, nint argument, bool resume);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern uint WaitForMultipleObjects(uint count, nint[] handles, bool waitAll,
    uint milliseconds);

  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  private static extern nint CreateEventW(nint attributes, bool manualReset, bool initialState,
    string? name);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool SetEvent(nint handle);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool CloseHandle(nint handle);
#pragma warning restore SYSLIB1054
}
