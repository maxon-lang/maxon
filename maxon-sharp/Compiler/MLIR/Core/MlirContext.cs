namespace MaxonSharp.Compiler.Ir.Core;

public class IrContext(bool isStdlibContext = false) {
  // Stdlib ids carry this bit so they can never alias user-side ids in per-function
  // valueMaps during lowering. Two paths set it:
  //   - The cached stdlib parses inside an IrContext constructed with isStdlibContext: true,
  //     so every parser-emitted MaxonValue id has the bit baked in.
  //   - During the lowering pass, the driver flips StdlibLoweringMode on while lowering
  //     stdlib functions and off while lowering user functions. This biases newly minted
  //     MaxonValue / StdValue ids accordingly even though the lowering shares one IrContext.
  // MaxonValue.ToString() / StdValue.ToString() print stdlib ids as %s<low-bits>.
  public const int StdlibIdBit = 1 << 30;
  public static bool IsStdlibId(int id) => (id & StdlibIdBit) != 0;

  private int _nextValueId;
  private int _nextStdlibValueId;
  private int _nextStdValueId;
  private int _nextStdlibStdValueId;
  private readonly bool _alwaysStdlib = isStdlibContext;
  private bool _stdlibLoweringMode;

  [ThreadStatic]
  private static IrContext? _current;

  public static IrContext Current => _current ?? throw new InvalidOperationException("No IrContext is active");

  /// <summary>
  /// While true, NextId() draws from a separate counter and OR's the stdlib bit into
  /// the result. Used by the lowering driver to keep stdlib-side ids disjoint from
  /// user-side ids without inflating user-side ids.
  /// </summary>
  public bool StdlibLoweringMode {
    get => _stdlibLoweringMode;
    set => _stdlibLoweringMode = value;
  }

  /// <summary>
  /// Mints a MaxonValue id. MaxonValues are used as keys in per-function
  /// Dictionary<MaxonValue, StdValue> valueMaps during lowering, so this counter must
  /// stay monotonic across the whole compile to avoid alias collisions when transient
  /// lowering-time MaxonValues (e.g. MaxonManagedMemSliceOp.Result) reuse a parser-time
  /// id and silently overwrite a valueMap entry. The stdlib bit isolates stdlib values
  /// from user values within the same map.
  /// </summary>
  public int NextId() {
    if (_alwaysStdlib || _stdlibLoweringMode) return _nextStdlibValueId++ | StdlibIdBit;
    return _nextValueId++;
  }

  /// <summary>
  /// Mints a StdValue id. StdValues are only ever VALUES in valueMap (never keys), so
  /// id collisions between two StdValues are harmless. They get their own counter so
  /// that user-code lowering produces small, readable %0..%n ids regardless of how
  /// much MaxonValue churn happened earlier in the pipeline.
  /// </summary>
  public int NextStdId() {
    if (_alwaysStdlib || _stdlibLoweringMode) return _nextStdlibStdValueId++ | StdlibIdBit;
    return _nextStdValueId++;
  }

  public void ResetIds() {
    _nextValueId = 0;
    _nextStdlibValueId = 0;
    _nextStdValueId = 0;
    _nextStdlibStdValueId = 0;
  }

  /// <summary>
  /// Seeds the stdlib MaxonValue / StdValue counters past the highest ids minted
  /// by the cached stdlib parse. Without this, lowering-time MaxonValues created
  /// while StdlibLoweringMode is on (e.g. MaxonManagedMemSliceOp.Result inside
  /// TryLowerManagedMemBuiltin) restart at 0 and silently overwrite parser-time
  /// stdlib MaxonValues that share the same Id in a per-function valueMap. The
  /// stdlib id-bit isolates stdlib ids from user ids, but the stdlib parse and
  /// stdlib-lowering both draw from the stdlib counter, so within that namespace
  /// the counter must remain monotonic across the whole compile.
  /// Pass the low-bits (without StdlibIdBit) of the highest id seen.
  /// </summary>
  public void SeedStdlibCounters(int maxValueIdLowBits, int maxStdValueIdLowBits) {
    if (maxValueIdLowBits + 1 > _nextStdlibValueId)
      _nextStdlibValueId = maxValueIdLowBits + 1;
    if (maxStdValueIdLowBits + 1 > _nextStdlibStdValueId)
      _nextStdlibStdValueId = maxStdValueIdLowBits + 1;
  }

  /// Read-only views used to snapshot the stdlib counter after the cached stdlib
  /// parse so the user-side IrContext can seed past it.
  public int NextStdlibValueId => _nextStdlibValueId;
  public int NextStdlibStdValueId => _nextStdlibStdValueId;

  public IDisposable PushScope() {
    var previous = _current;
    _current = this;
    return new ContextScope(previous);
  }

  private class ContextScope(IrContext? previous) : IDisposable {
    public void Dispose() {
      _current = previous;
    }
  }
}
