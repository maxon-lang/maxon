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
  /// They are different rates on purpose. A Toolhelp snapshot walks every thread ON THE MACHINE, so
  /// taking one per tick would cost more than the sampling does, and the answer barely changes: the
  /// scheduler creates its workers at <c>__gt_init</c> and rarely again. A thread that appears later is
  /// picked up within this interval, and one that EXITS is noticed immediately without any enumeration,
  /// because its handle starts failing (see <see cref="SampleThread"/>).
  /// </summary>
  private static readonly TimeSpan ThreadListRefreshInterval = TimeSpan.FromMilliseconds(100);

  private readonly Process _process;
  private readonly nint _processHandle;

  /// The CONTEXT record <c>GetThreadContext</c> fills. Allocated ONCE and 16-byte aligned by hand:
  /// x64 CONTEXT contains XMM state and the API rejects a misaligned pointer, which
  /// <see cref="Marshal.AllocHGlobal"/> does not promise.
  private readonly nint _contextAllocation;
  private readonly nint _context;

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

  private volatile bool _stop;

  private ProfileSampler(Process process, nint processHandle, nint contextAllocation, nint context) {
    _process = process;
    _processHandle = processHandle;
    _contextAllocation = contextAllocation;
    _context = context;
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
  public static string? UnsupportedReason => OperatingSystem.IsWindows()
    ? null
    : $"`maxon profile` samples by suspending the target's threads, which on {RuntimeInformation.OSDescription}"
      + " needs task_for_pid — a privileged Mach call requiring root or a signed entitlement. Only the"
      + " Windows sampler is implemented, so there is nothing here that could measure this program.";

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

    try {
      return new ProfileSampler(process, process.Handle, allocation, context);
    } catch {
      Marshal.FreeHGlobal(allocation);
      throw;
    }
  }

  /// Ask the loop to finish after the tick it is on. The runner calls this once the target has exited or
  /// its deadline has passed; the loop also stops on its own when the target goes away.
  public void Stop() => _stop = true;

  public void Dispose() {
    foreach (var thread in _threads.Values) CloseHandle(thread.Handle);
    _threads.Clear();
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
    using var timer = WaitableTimer.Create();

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

      // The target exiting is checked AFTER a sample, so the last moments of a short program are
      // measured rather than lost to a liveness check that happened to win the race.
      if (_stop || _process.HasExited) return;

      deadline += intervalTicks;
      timer.WaitUntil(deadline);
    }
  }

  /// <summary>
  /// Re-enumerate the target's threads, opening a handle for each new one. A thread that has gone away
  /// is dropped HERE only if the enumeration no longer lists it; the sampling path drops it sooner, on
  /// the first call its handle refuses.
  /// </summary>
  private void RefreshThreadList() {
    nint snapshot = CreateToolhelp32Snapshot(Th32CsSnapThread, 0);
    if (snapshot == InvalidHandle) return;

    try {
      var entry = new ThreadEntry32 { Size = (uint)Marshal.SizeOf<ThreadEntry32>() };
      if (!Thread32First(snapshot, ref entry)) return;

      var seen = new HashSet<int>();
      uint targetPid = (uint)_process.Id;
      do {
        if (entry.OwnerProcessId != targetPid) continue;

        int id = (int)entry.ThreadId;
        seen.Add(id);
        if (_threads.ContainsKey(id)) continue;

        nint handle = OpenThread(ThreadSampleAccess, false, entry.ThreadId);
        if (handle != 0) _threads[id] = new TargetThread(handle);
      } while (Thread32Next(snapshot, ref entry));

      foreach (var id in _threads.Keys.Where(id => !seen.Contains(id)).ToList()) {
        CloseHandle(_threads[id].Handle);
        _threads.Remove(id);
      }
    } finally {
      CloseHandle(snapshot);
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
    ulong windowEnd = stackPointer + (ulong)windowBytes;
    int count = 1;
    ulong fp = framePointer;

    while (count < MaxWalkFrames) {
      // The frame link is TWO words — the saved frame pointer and the return address — so the bound has
      // to leave room for both, read from the ONE constant the in-process walk states it with.
      if (fp < stackPointer || fp + GtLayout.FrameLinkBytes > windowEnd) break;

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
  /// asking for 1 kHz would silently collect 64 Hz. A high-resolution waitable timer is exact and, when
  /// the host predates it, the ordinary one still beats sleeping.
  /// </summary>
  private sealed class WaitableTimer(nint handle) : IDisposable {
    public static WaitableTimer Create() {
      nint handle = CreateWaitableTimerExW(0, null, CreateWaitableTimerHighResolution, TimerAllAccess);
      if (handle == 0) handle = CreateWaitableTimerExW(0, null, 0, TimerAllAccess);
      if (handle == 0)
        throw new ProfileUnsupportedException(
          $"cannot create the sampling timer: Win32 error {Marshal.GetLastWin32Error()}");

      return new WaitableTimer(handle);
    }

    /// Wait until <paramref name="fileTimeUtc"/>. A POSITIVE due time is absolute, which is what keeps
    /// the schedule from drifting by however long the previous tick's work took.
    public void WaitUntil(long fileTimeUtc) {
      if (!SetWaitableTimer(handle, in fileTimeUtc, 0, 0, 0, false)) return;
      WaitForSingleObject(handle, InfiniteWait);
    }

    public void Dispose() => CloseHandle(handle);
  }

  // ---- Win32 ----

  private const nint InvalidHandle = -1;
  private const uint InfiniteWait = 0xFFFFFFFF;

  /// Suspend/resume, read the context, and read the cycle counter — exactly the four things this engine
  /// does to a thread and nothing more.
  private const uint ThreadSampleAccess = 0x0002 | 0x0008 | 0x0040 | 0x0800;

  private const uint Th32CsSnapThread = 0x00000004;

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

  [StructLayout(LayoutKind.Sequential)]
  private struct ThreadEntry32 {
    public uint Size;
    public uint Usage;
    public uint ThreadId;
    public uint OwnerProcessId;
    public int BasePriority;
    public int DeltaPriority;
    public uint Flags;
  }

#pragma warning disable SYSLIB1054 // DllImport keeps this block uniform with Testing/WindowsJobObject.cs
  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern nint OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

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

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool Thread32First(nint snapshot, ref ThreadEntry32 entry);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool Thread32Next(nint snapshot, ref ThreadEntry32 entry);

  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  private static extern nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool SetWaitableTimer(nint timer, in long dueTime, int period,
    nint completionRoutine, nint argument, bool resume);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool CloseHandle(nint handle);
#pragma warning restore SYSLIB1054
}
