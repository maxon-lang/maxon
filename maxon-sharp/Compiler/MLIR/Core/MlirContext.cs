namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirContext {
  private int _nextValueId;

  [ThreadStatic]
  private static MlirContext? _current;

  public static MlirContext Current => _current ?? throw new InvalidOperationException("No MlirContext is active");

  public int NextId() {
    return _nextValueId++;
  }

  public void ResetIds() {
    _nextValueId = 0;
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
