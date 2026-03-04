namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirContext {
  private int _nextValueId;
  private int _highWaterMark;

  [ThreadStatic]
  private static MlirContext? _current;

  public static MlirContext Current => _current ?? throw new InvalidOperationException("No MlirContext is active");

  public int NextId() {
    var id = _nextValueId++;
    if (_highWaterMark > 0 && id >= _highWaterMark)
      throw new InvalidOperationException(
        $"MlirContext: value ID {id} collides with pre-reset range (high water mark: {_highWaterMark}). " +
        "ResetIds() was called but new IDs have reached the range of previously issued IDs.");
    return id;
  }

  /// <summary>
  /// Reset the ID counter to 0, recording the current counter as a high water mark.
  /// Subsequent NextId() calls will throw if IDs reach the pre-reset range,
  /// detecting mid-pipeline resets that could cause value ID collisions.
  /// Use ClearHighWaterMark() first when resetting between independent compilation stages.
  /// </summary>
  public void ResetIds() {
    if (_nextValueId > _highWaterMark)
      _highWaterMark = _nextValueId;
    _nextValueId = 0;
  }

  /// <summary>
  /// Clear the high water mark, allowing IDs to be reused from 0 without collision detection.
  /// Call this between independent compilation stages where old values are fully discarded.
  /// </summary>
  public void ClearHighWaterMark() {
    _highWaterMark = 0;
  }

  public IDisposable PushScope() {
    var previous = _current;
    _current = this;
    return new ContextScope(previous);
  }

  private class ContextScope(MlirContext? previous) : IDisposable {
    public void Dispose() {
      _current = previous;
    }
  }
}
