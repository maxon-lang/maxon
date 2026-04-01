namespace MaxonSharp.Compiler.Mlir.Runtime;

/// <summary>
/// Platform-independent runtime code generator. Expresses algorithms using VRegs
/// and IEmitterBackend operations. Each function is written once and works on
/// both x86 (Windows) and ARM64 (macOS).
/// </summary>
public partial class RuntimeEmitter(IEmitterBackend backend) {
  private readonly IEmitterBackend _b = backend;
  private int _uniqueLabelCounter;

  private string UniqueLabel(string prefix) => $"__{prefix}_{_uniqueLabelCounter++}";

  // =========================================================================
  // GreenThread struct layout (176 bytes = 0xB0)
  // Identical on both platforms.
  // =========================================================================
  public const int GtOffRsp = 0x00;
  public const int GtOffRbp = 0x08;
  public const int GtOffStatus = 0x10;
  public const int GtOffStackBase = 0x18;
  public const int GtOffStackSize = 0x20;
  public const int GtOffResult = 0x28;
  public const int GtOffWaiter = 0x30;
  public const int GtOffNext = 0x38;
  public const int GtOffFuncPtr = 0x40;
  public const int GtOffArgBuf = 0x48;
  public const int GtOffStackguard = 0x50;
  public const int GtOffThrew = 0x58;
  public const int GtOffIoResultVal = 0x60;
  public const int GtOffIoResultLen = 0x68;
  public const int GtOffIoErrorCode = 0x70;
  public const int GtOffCancelFlag = 0x78;
  public const int GtOffIoHandle = 0x80;
  public const int GtOffAllNext = 0x88;
  public const int GtOffTibStackBase = 0x90;
  public const int GtOffTibStackLimit = 0x98;
  public const int GtOffTraceId = 0xA0;
  public const int GtOffIoYielded = 0xA8;
  public const int GtStructSize = 0xB0;

  // GT status values
  public const int GtStatusReady = 0;
  public const int GtStatusRunning = 1;
  public const int GtStatusCompleted = 2;
  public const int GtStatusWaiting = 3;

  // =========================================================================
  // P (ProcContext) struct layout (296 bytes = 0x128)
  // Per-worker-thread scheduler context, accessed via TLS.
  // =========================================================================
  public const int POffLocalQueueHead = 0x00;
  public const int POffLocalQueueTail = 0x08;
  public const int POffLocalQueueLen = 0x10;
  public const int POffCurrentGt = 0x18;
  public const int POffId = 0x20;
  public const int POffRng = 0x28;
  public const int POffIdleFlag = 0x30;
  public const int POffWakeEvent = 0x38;
  public const int POffOsThreadHandle = 0x40;
  public const int POffStatus = 0x48;
  public const int POffPendingWaiter = 0x50;
  public const int POffRunnext = 0x58;
  public const int POffFreeListHead = 0x60;
  public const int POffFreeListLen = 0x68;
  public const int POffSystemStackSP = 0x70;
  public const int POffMainThread = 0x78;
  public const int PStructSize = POffMainThread + GtStructSize; // 0x128

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
  public const byte DsEvDepthInc = 0x40;
  public const byte DsEvDepthDec = 0x41;
  public const byte DsEvHeartbeat = 0xFE;
  public const byte DsEvPadding = 0xFF;

  // Entry header layout (8 bytes)
  //   [+0x00] u8  event_type
  //   [+0x01] u8  flags
  //   [+0x02] u16 entry_size (total bytes, 8-byte aligned)
  //   [+0x04] u32 timestamp_delta (ms since start_timestamp)
  public const int DsEntryHeaderSize = 8;
}
