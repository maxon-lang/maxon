namespace MaxonSharp.Compiler.Ir.Core;

public class IrContext {
  private int _nextValueId;

  [ThreadStatic]
  private static IrContext? _current;

  public static IrContext Current => _current ?? throw new InvalidOperationException("No IrContext is active");

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

  private class ContextScope(IrContext? previous) : IDisposable {
    public void Dispose() {
      _current = previous;
    }
  }
}
