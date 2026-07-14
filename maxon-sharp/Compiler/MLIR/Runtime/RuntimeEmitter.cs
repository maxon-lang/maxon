using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;

namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// Platform-independent runtime code generator. Expresses algorithms using VRegs
/// and IEmitterBackend operations. Each function is written once and works on
/// both x86 (Windows) and ARM64 (macOS).
/// </summary>
public partial class RuntimeEmitter(IEmitterBackend backend) {
  private readonly IEmitterBackend _b = backend;
  private int _uniqueLabelCounter;

  private string UniqueLabel(string prefix) => $"__{prefix}_{_uniqueLabelCounter++}";

  /// <summary>
  /// Emits all runtime functions shared between x86 and ARM64 code emitters.
  /// Consolidates the identical runtime emission sequence used by both platforms.
  /// </summary>
  public void EmitAllMemoryManagerFunctions(bool mmTrace, bool mmDebug, List<string?>? tagTable,
      List<string?>? tagNames, List<string?>? logNames) {
    var tags = tagTable ?? [];
    EmitMmGlobals(mmTrace, mmDebug, tags);
    EmitMmTraceFunctions(mmTrace, tags);
    EmitMmAlloc(mmTrace, mmDebug);
    EmitMmRealloc(mmTrace, mmDebug);
    EmitMmFree(mmTrace, mmDebug);
    EmitMmIncref(mmTrace);
    EmitMmDecref(mmTrace, mmDebug);
    EmitMmManagedElementsFunctions(mmTrace);
    EmitMmLeakCheck(mmDebug, tags);
    EmitMmValidatePtr();
    // The memory-traffic counter readers. Registered HERE, at the platform-independent
    // VReg layer, so x86 and ARM64 both get them from one definition.
    EmitMmCounterAccessors();
    EmitManagedListFunctions(mmTrace);
    if (Compiler.DebugStream) {
      EmitDebugStreamFunctions(tagNames ?? [], logNames ?? []);
    }
  }

  // GreenThread (gt) and ProcContext (P) struct layouts live in GtLayout.cs.
  // Constants are imported via `using static MaxonSharp.Compiler.Ir.Runtime.GtLayout`.

  // =========================================================================
  // Memory manager constants
  // =========================================================================
  public const int MmHeaderSize = 32;
  // Header layout (relative to user pointer):
  //   [ptr - 32]: alloc_size        (total OS allocation size = user_size + MmHeaderSize, needed by munmap)
  //   [ptr - 24]: packed_id         (alloc_id << 16 | tag_index)
  //   [ptr - 16]: destructor_fn_ptr
  //   [ptr -  8]: refcount
  //   [ptr     ]: user data
  public const int MmOffAllocSize = -32;
  public const int MmOffPackedId = -24;
  public const int MmOffDestructor = -16;
  public const int MmOffRefcount = -8;

  // __ManagedMemory RECORD layout, relative to the record pointer. This is the
  // container header the array/string builtins operate on — not to be confused
  // with the allocation header above, which lives at NEGATIVE offsets from a user
  // pointer. Mirrors ManagedField* in MaxonToStandardConversion.Helpers.cs (the
  // conversion layer's view of the same record) and MM_OFFSET_* in
  // stdlib/Internals.maxon (the self-hosted runtime's).
  public const int MmemOffBuffer = 0;
  public const int MmemOffLength = 8;
  public const int MmemOffCapacity = 16;
  public const int MmemOffElementSize = 24;

  // element_size == 0 is the SUB-BYTE BIT-PACKED sentinel (`Array with bool`):
  // capacity and length stay element counts, but the buffer is (count + 7) / 8
  // bytes and an element is addressed by bit, not by byte offset.
  public const int MmemBitPackedElementSize = 0;

  // A managed element is always an 8-byte refcounted heap pointer, so the element
  // walks (incref / decref / vacate) have a fixed stride.
  public const int MmemManagedElementSize = 8;

  // Bytes per machine word — the bulk stride of the byte-exact zero loop.
  public const int MachineWordBytes = 8;

  // Debug canary value written after user data when MmDebug is enabled
  public const long MmDebugCanaryValue = unchecked((long)0xCAFEBABEDEADC0DE);

  // =========================================================================
  // DebugStream shared memory constants
  // =========================================================================
  public const long DsMagic = unchecked((long)0x4D58444253545200); // "MXDBSTR\0"
  public const long DsVersion = 1;
  public const int DsHeaderSize = 128;       // 0x80
  public const int DsDefaultBufferSize = 2 * 1024 * 1024; // 2 MB, must be power-of-two

  // Header field offsets (from base of shared memory)
  public const int DsOffMagic = 0x00;
  public const int DsOffVersion = 0x08;
  public const int DsOffBufferSize = 0x10;
  public const int DsOffWriteCursor = 0x18;
  public const int DsOffReadCursor = 0x20;
  public const int DsOffFlags = 0x28;
  public const int DsOffProcessId = 0x30;
  public const int DsOffStartTimestamp = 0x38;
  public const int DsOffTotalEvents = 0x40;
  public const int DsOffDroppedEvents = 0x48;
  public const int DsOffTagTableOffset = 0x50;
  public const int DsOffTagTableCount = 0x58;
  public const int DsOffPeakUsed = 0x60;

  // Flags bits
  public const long DsFlagProducerAlive = 1;
  public const long DsFlagConsumerAttached = 2;

  // Event type codes
  public const byte DsEvMmAlloc = 0x01;
  public const byte DsEvMmFree = 0x02;
  public const byte DsEvMmIncref = 0x03;
  public const byte DsEvMmDecref = 0x04;
  public const byte DsEvMmTransfer = 0x05;
  public const byte DsEvMmRealloc = 0x06;
  public const byte DsEvMmCow = 0x07;
  public const byte DsEvMmRawAlloc = 0x08;
  public const byte DsEvMmRawFree = 0x09;
  public const byte DsEvSchedSpawn = 0x20;
  public const byte DsEvSchedAwait = 0x21;
  public const byte DsEvSchedYield = 0x22;
  public const byte DsEvSchedResume = 0x23;
  public const byte DsEvIoYield = 0x2B;
  public const byte DsEvIoResume = 0x2C;

  // Per-slot scheduler debug events (richer payload via __ds_emit_dbg).
  // Each carries: gt(8), p_id(8), arg2(8), arg3(8), arg4(8) — 48 bytes payload.
  // Used to capture the exact path a GT pointer takes through scheduling slots,
  // so a two-workers-on-one-GT race can be reconstructed post-mortem.
  public const byte DsEvDbgEnqueue           = 0x50;  // arg2=kind(0=local,1=global,2=steal_chain), arg3=P_owner_or_victim
  public const byte DsEvDbgDequeue           = 0x51;  // arg2=kind(0=local,1=global,2=steal_first), arg3=P_owner_or_victim
  public const byte DsEvDbgRunnextSet        = 0x52;  // arg2=0
  public const byte DsEvDbgRunnextTake       = 0x53;  // arg2=0
  public const byte DsEvDbgRunnextDisplace   = 0x54;  // arg2=new_gt
  public const byte DsEvDbgStatusStore       = 0x55;  // arg2=old_status, arg3=new_status, arg4=site_id
  public const byte DsEvDbgIoComplete        = 0x56;  // arg2=phase(0=status_set,1=spin_done,2=enqueueing)
  public const byte DsEvDbgFreeListPush      = 0x57;  // arg2=new_len
  public const byte DsEvDbgFreeListPop       = 0x58;  // arg2=new_len
  public const byte DsEvDbgWloopRunGt        = 0x59;  // (worker loop about to context-switch into gt)
  public const byte DsEvDbgAwaitDeqRun       = 0x5A;  // (await loop about to context-switch into gt)
  public const byte DsEvDbgTrampolineCompleted = 0x5B; // (trampoline set status=Completed)
  public const byte DsEvDbgTimerFire         = 0x5C;  // (timer popped expired gt and set status=Ready)
  public const byte DsEvDbgCsxEntry          = 0x5D;  // arg2=to_gt, arg3=from_rsp, arg4=from_rbp
  public const byte DsEvDbgCsxExit           = 0x5E;  // arg2=to_gt, arg3=to_rsp, arg4=to_rbp

  // Log events (Workstream O): the events USER MAXON SOURCE puts into the ring, via the
  // `__DebugStream` builtin. Every other family above is emitted by the runtime; these are
  // the only ones a compiled program authors itself, and they exist because shv2 IS a Maxon
  // program — a text log to stderr stops being readable the moment N workers interleave into
  // one stream, and cannot say which worker / unit / phase a line came from.
  //
  // The wire schema is FROZEN — see maxon-shv2/ARCHITECTURE.md. Codes take the free
  // 0x60-0x6F range. Payload after the 8-byte entry header:
  //   0x60/0x61: gt(8) · p_id(8) · phase_id(2) rsvd(2) unit_id(4)                    — 32B
  //   0x62:      gt(8) · p_id(8) · cat(1) lvl(1) event_id(2) unit_id(4) · a0(8) a1(8) — 48B
  //   0x63:      gt(8) · p_id(8) · cat(1) lvl(1) len(2) unit_id(4) · UTF-8 tail        — variable
  public const byte DsEvLogPhaseBegin = 0x60;
  public const byte DsEvLogPhaseEnd   = 0x61;
  public const byte DsEvLogEvent      = 0x62;
  public const byte DsEvLogText       = 0x63;

  public const byte DsEvDepthInc = 0x40;
  public const byte DsEvDepthDec = 0x41;
  public const byte DsEvHeartbeat = 0xFE;
  public const byte DsEvPadding = 0xFF;

  // ---- Log event payload geometry ----
  // Offsets are from the START of the entry (i.e. they include the 8-byte entry header),
  // so an emitter can write straight through the pointer __ds_reserve handed back.
  public const int DsLogOffGt = 8;
  public const int DsLogOffPid = 16;
  /// The packed field word. Its layout differs per event (see the table above), but every
  /// Log event has exactly one and it always sits here.
  public const int DsLogOffFields = 24;
  public const int DsLogOffArg0 = 32;   // LOG_EVENT only
  public const int DsLogOffArg1 = 40;   // LOG_EVENT only
  public const int DsLogOffText = 32;   // LOG_TEXT only: first byte of the UTF-8 tail

  // Total entry sizes, header included. LOG_EVENT deliberately reuses the 48-byte Dbg shape.
  public const int DsLogPhaseEntrySize = 32;
  public const int DsLogEventEntrySize = 48;
  /// The fixed part of a LOG_TEXT entry; the 8-byte-aligned UTF-8 tail follows it.
  public const int DsLogTextFixedSize = 32;

  // Field packing for the DsLogOffFields word.
  //
  // LOG_EVENT and LOG_TEXT pack this word IDENTICALLY — cat(1) lvl(1) u16(2) unit_id(4). Only
  // the meaning of the 16-bit slot differs (an interned `event_id` for one, a byte `len` for
  // the other), which is why there is ONE shift and ONE mask for it rather than a pair each.
  // LOG_PHASE_BEGIN/END use only the u16 slot (as `phase_id`) and the unit.
  //
  // `unit_id` occupies the high 32 bits, so the left shift of 32 already discards anything that
  // would not fit — it needs no mask.
  public const long DsLogCatMask = 0xFF;
  public const int DsLogLvlShift = 8;
  public const long DsLogLvlMask = 0xFF;
  public const int DsLogU16FieldShift = 16;
  public const long DsLogU16FieldMask = 0xFFFF;
  public const int DsLogUnitIdShift = 32;

  /// The ring keeps every entry 8-byte aligned, so every entry size is rounded UP to this.
  public const int DsEntryAlign = 8;
  public const int DsEntryAlignBias = DsEntryAlign - 1;
  /// Masks a biased length back DOWN to a multiple of DsEntryAlign — together, round-up.
  public const long DsEntryAlignMask = ~(long)DsEntryAlignBias;

  /// The entry header's `entry_size` is a u16, so no single entry can exceed this.
  public const int DsEntrySizeMax = 0xFFFF;

  /// The largest UTF-8 tail a LOG_TEXT entry can carry: what is left of a maximal entry after
  /// the fixed part, rounded DOWN to the ring's alignment. A longer message is TRUNCATED to it
  /// — never torn across two entries, which would need a continuation code the frozen schema
  /// does not have.
  public const int DsLogTextMaxBytes =
    (DsEntrySizeMax - DsLogTextFixedSize) / DsEntryAlign * DsEntryAlign;

  // Site IDs for DsEvDbgStatusStore. Distinct constants per call site so the trace
  // attributes a torn status transition to its source.
  public const int DsStatusSiteSpawnReady           = 1;
  public const int DsStatusSiteWloopRun             = 2;
  public const int DsStatusSiteAwaitSwitchMain      = 3;
  public const int DsStatusSiteAwaitHasNext         = 4;
  public const int DsStatusSiteYieldCompletedMain   = 5;
  public const int DsStatusSiteTrampolineCompleted  = 6;
  public const int DsStatusSiteAwaitWaiting         = 7;
  public const int DsStatusSiteIoCompleteReady      = 8;
  public const int DsStatusSiteTimerFireReady       = 9;
  public const int DsStatusSiteIoMainLoopRunning    = 10;
  public const int DsStatusSiteIoYieldTargetRunning = 11;

  // Entry header layout (8 bytes)
  //   [+0x00] u8  event_type
  //   [+0x01] u8  flags
  //   [+0x02] u16 entry_size (total bytes, 8-byte aligned)
  //   [+0x04] u32 timestamp_delta (ms since start_timestamp)
  public const int DsEntryHeaderSize = 8;
}
