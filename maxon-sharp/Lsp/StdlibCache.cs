using MaxonSharp.Compiler;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Lsp;

public class StdlibCache {
  private MlirModule<MaxonOp>? _cachedModule;
  private SourceFile[]? _sources;
  private readonly object _lock = new();

  public MlirModule<MaxonOp> CreateModuleWithStdlib() {
    lock (_lock) {
      if (_cachedModule == null) {
        _sources = StdlibLoader.LoadStdlibModules();
        var context = new MlirContext();
        using var scope = context.PushScope();
        _cachedModule = new MlirModule<MaxonOp>();
        Compiler.Compiler.CompileSources(_cachedModule, _sources, true);
        foreach (var func in _cachedModule.Functions)
          func.IsStdlib = true;
      }
      return _cachedModule.Clone();
    }
  }
}
