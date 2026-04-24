using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Core;

public enum CallEdgeKind {
  /// Synchronous direct call (MaxonCallOp / StdCallOp / StdTryCallOp).
  Direct,
  /// Async spawn — semantically distinct: callee runs concurrently. Some
  /// analyses want to skip these.
  Async,
}

/// One direct-call edge: who is called, and (when the op carries it) the
/// caller-side argument variable names. OpIndex/BlockIndex preserve iteration
/// order for passes that care about source-order.
public readonly record struct CallEdge(
    string CalleeName,
    IReadOnlyList<string?>? ArgVarNames,
    int BlockIndex,
    int OpIndex,
    CallEdgeKind Kind);

/// Dialect-specific op classification. Avoids baking MaxonOp/StandardOp
/// knowledge into IrCallGraph itself.
public sealed class CallGraphDialect<TOp> where TOp : IPrintableOp {
  public required Func<TOp, (string Name, IReadOnlyList<string?>? ArgVars, CallEdgeKind Kind)?> TryGetDirectCall;
  public required Func<TOp, bool> IsIndirectCall;
  /// All call-like name references used by DFE: direct callee, async callee,
  /// function refs, closure creations, lazy-init func names, try_call callee.
  /// Includes direct-call names too so DFE doesn't have to merge two sources.
  public required Func<TOp, IEnumerable<string>> EnumerateReferencedNames;
}

/// Lazy module-level call graph. Rebuilt on demand when the module's dirty
/// bit flips. All queries return empty / false / null for unknown keys.
///
/// Semantics:
///  - "Callees" / "call edges" reflect only direct-call ops (MaxonCallOp,
///    StdCallOp, StdTryCallOp, MaxonAsyncCallOp — dialect-defined).
///  - "Referenced names" is a broader set used by reachability analysis and
///    includes function-refs, closure creations, and lazy-init func names.
///  - Names are stored verbatim from the ops; name resolution (short name,
///    suffix match, alias expansion) is the caller's responsibility.
public sealed class IrCallGraph<TOp> where TOp : IPrintableOp {
  private readonly IrModule<TOp> _module;
  private readonly CallGraphDialect<TOp> _dialect;

  // Rebuild strategy: either a full rebuild is queued (any destructive mutation
  // to the module — remove, rename — sets this) or only newly-added functions
  // need to be indexed incrementally. Full rebuild trumps pending adds, since
  // it starts from scratch and the adds are already in _module.Functions.
  private bool _fullRebuildNeeded = true;
  private readonly List<IrFunction<TOp>> _pendingAdds = [];

  // Built on (re)build:
  private readonly Dictionary<string, List<IrFunction<TOp>>> _callers = [];
  private readonly Dictionary<string, List<IrFunction<TOp>>> _directCallers = [];
  private readonly Dictionary<IrFunction<TOp>, List<CallEdge>> _edgesByCaller = [];
  private readonly Dictionary<IrFunction<TOp>, bool> _hasIndirect = [];
  private readonly Dictionary<IrFunction<TOp>, List<string>> _referencedNamesByCaller = [];
  private readonly HashSet<string> _allCalleeNames = [];

  private static readonly IReadOnlyList<IrFunction<TOp>> EmptyFuncs = [];
  private static readonly IReadOnlyList<CallEdge> EmptyEdges = [];
  private static readonly IReadOnlyList<string> EmptyNames = [];

  public IrCallGraph(IrModule<TOp> module, CallGraphDialect<TOp> dialect) {
    _module = module;
    _dialect = dialect;
  }

  /// Destructive mutation — next access rebuilds from scratch. Use this for
  /// function removal, rename in place, or any edit that could invalidate
  /// existing edges (a body op added/changed/removed on a function already in
  /// the graph).
  public void Invalidate() {
    _fullRebuildNeeded = true;
    _pendingAdds.Clear();
  }

  /// Additive mutation — the given function was just added to the module and
  /// its edges need to be indexed. Cheaper than a full rebuild: only the new
  /// function's ops are walked on the next access.
  public void NoteAdded(IrFunction<TOp> func) {
    if (_fullRebuildNeeded) return; // already will re-index everything
    _pendingAdds.Add(func);
  }

  private void EnsureBuilt() {
    if (_fullRebuildNeeded) {
      _callers.Clear();
      _directCallers.Clear();
      _edgesByCaller.Clear();
      _hasIndirect.Clear();
      _referencedNamesByCaller.Clear();
      _allCalleeNames.Clear();

      foreach (var func in _module.Functions) IndexFunction(func);

      _fullRebuildNeeded = false;
      _pendingAdds.Clear();
      return;
    }

    if (_pendingAdds.Count > 0) {
      foreach (var func in _pendingAdds) IndexFunction(func);
      _pendingAdds.Clear();
    }
  }

  /// Indexes a single function's edges into all six structures. Shared by
  /// full-rebuild and incremental-add paths.
  private void IndexFunction(IrFunction<TOp> func) {
    List<CallEdge>? edges = null;
    List<string>? refNames = null;
    bool hasIndirect = false;

    var blocks = func.Body.Blocks;
    for (int bi = 0; bi < blocks.Count; bi++) {
      var ops = blocks[bi].Operations;
      for (int oi = 0; oi < ops.Count; oi++) {
        var op = ops[oi];

        var direct = _dialect.TryGetDirectCall(op);
        if (direct is { } dc) {
          edges ??= [];
          edges.Add(new CallEdge(dc.Name, dc.ArgVars, bi, oi, dc.Kind));
          AddCaller(_callers, dc.Name, func);
          if (dc.Kind == CallEdgeKind.Direct)
            AddCaller(_directCallers, dc.Name, func);
          _allCalleeNames.Add(dc.Name);
        }

        if (!hasIndirect && _dialect.IsIndirectCall(op))
          hasIndirect = true;

        foreach (var name in _dialect.EnumerateReferencedNames(op)) {
          refNames ??= [];
          refNames.Add(name);
        }
      }
    }

    if (edges != null) _edgesByCaller[func] = edges;
    if (refNames != null) _referencedNamesByCaller[func] = refNames;
    if (hasIndirect) _hasIndirect[func] = true;
  }

  private static void AddCaller(Dictionary<string, List<IrFunction<TOp>>> map, string callee, IrFunction<TOp> caller) {
    if (!map.TryGetValue(callee, out var list)) {
      list = [];
      map[callee] = list;
    }
    // A single caller function may call the same callee from multiple ops; only
    // record it once. Last-element check catches same-function runs (the common
    // case); fall back to a contains check for interleaved calls.
    if (list.Count > 0 && list[^1] == caller) return;
    if (list.Count > 0 && list.Contains(caller)) return;
    list.Add(caller);
  }

  /// All callers (direct + async).
  public IReadOnlyList<IrFunction<TOp>> GetCallers(string calleeName) {
    EnsureBuilt();
    return _callers.TryGetValue(calleeName, out var list) ? list : EmptyFuncs;
  }

  /// Synchronous callers only — excludes async spawns. Used by analyses that
  /// only want to propagate through the synchronous dataflow.
  public IReadOnlyList<IrFunction<TOp>> GetDirectCallers(string calleeName) {
    EnsureBuilt();
    return _directCallers.TryGetValue(calleeName, out var list) ? list : EmptyFuncs;
  }

  public IReadOnlyList<CallEdge> GetCallEdges(IrFunction<TOp> caller) {
    EnsureBuilt();
    return _edgesByCaller.TryGetValue(caller, out var list) ? list : EmptyEdges;
  }

  public IReadOnlyCollection<string> GetAllCalleeNames() {
    EnsureBuilt();
    return _allCalleeNames;
  }

  public bool HasIndirectCall(IrFunction<TOp> func) {
    EnsureBuilt();
    return _hasIndirect.TryGetValue(func, out var v) && v;
  }

  public IReadOnlyList<string> GetReferencedNames(IrFunction<TOp> caller) {
    EnsureBuilt();
    return _referencedNamesByCaller.TryGetValue(caller, out var list) ? list : EmptyNames;
  }
}

public static class CallGraphDialects {
  public static readonly CallGraphDialect<MaxonOp> Maxon = new() {
    TryGetDirectCall = op => op switch {
      MaxonCallOp c => (c.Callee, (IReadOnlyList<string?>?)c.ArgVarNames, CallEdgeKind.Direct),
      MaxonAsyncCallOp ac => (ac.Callee, (IReadOnlyList<string?>?)ac.ArgVarNames, CallEdgeKind.Async),
      _ => null
    },
    IsIndirectCall = op => op is MaxonIndirectCallOp,
    EnumerateReferencedNames = EnumerateMaxonReferencedNames,
  };

  private static IEnumerable<string> EnumerateMaxonReferencedNames(MaxonOp op) {
    switch (op) {
      case MaxonCallOp c:
        yield return c.Callee;
        break;
      case MaxonAsyncCallOp ac:
        yield return ac.Callee;
        break;
      case MaxonFunctionRefOp fr:
        yield return fr.FunctionName;
        break;
      case MaxonClosureCreateOp cc:
        yield return cc.FunctionName;
        break;
      case MaxonGlobalLoadOp gl when gl.LazyInitFuncName is string lazy:
        yield return lazy;
        break;
    }
  }

  public static readonly CallGraphDialect<StandardOp> Standard = new() {
    TryGetDirectCall = op => op switch {
      StdCallOp c => (c.Callee, (IReadOnlyList<string?>?)null, CallEdgeKind.Direct),
      StdTryCallOp tc => (tc.Callee, (IReadOnlyList<string?>?)null, CallEdgeKind.Direct),
      _ => null
    },
    IsIndirectCall = op => op is StdIndirectCallOp,
    EnumerateReferencedNames = EnumerateStandardReferencedNames,
  };

  private static IEnumerable<string> EnumerateStandardReferencedNames(StandardOp op) {
    switch (op) {
      case StdCallOp c:
        yield return c.Callee;
        break;
      case StdTryCallOp tc:
        yield return tc.Callee;
        break;
      case StdFuncRefOp fr:
        yield return fr.FunctionName;
        break;
    }
  }
}
