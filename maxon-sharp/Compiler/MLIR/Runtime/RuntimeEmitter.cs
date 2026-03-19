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
}
