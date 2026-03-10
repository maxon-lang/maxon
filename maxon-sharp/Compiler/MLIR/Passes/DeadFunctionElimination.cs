using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

public static class DeadFunctionElimination {
  public static void Run(MlirModule<MaxonOp> module) {
    var reachable = new HashSet<string>();
    var funcByName = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var func in module.Functions)
      funcByName[func.Name] = func;

    // BFS from main and runtime entry points (check for exact match or suffix match)
    var queue = new Queue<string>();
    var mainFunc = module.Functions.FirstOrDefault(f => f.Name == "main" || f.Name.EndsWith(".main"));
    if (mainFunc != null) {
      queue.Enqueue(mainFunc.Name);
      reachable.Add(mainFunc.Name);
    }
    // __module_init and __maxon_global_cleanup are called from _start, not from Maxon code
    foreach (var func in module.Functions) {
      if (func.Name == "__module_init" || func.Name.EndsWith(".__module_init")
          || func.Name == "__maxon_global_cleanup" || func.Name.EndsWith(".__maxon_global_cleanup")) {
        if (reachable.Add(func.Name))
          queue.Enqueue(func.Name);
      }
    }

    // Resolve a callee name to its actual function name (handles namespace-qualified names)
    void EnqueueCallee(string callee) {
      if (!reachable.Add(callee)) return;
      queue.Enqueue(callee);
      // Also resolve suffix-matched names so the actual function is marked reachable
      if (!funcByName.ContainsKey(callee)) {
        var suffix = $".{callee}";
        foreach (var candidate in funcByName.Keys) {
          if (candidate.EndsWith(suffix) && reachable.Add(candidate))
            queue.Enqueue(candidate);
        }
      }
    }

    while (queue.Count > 0) {
      var name = queue.Dequeue();
      if (!funcByName.TryGetValue(name, out var func)) continue;

      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonCallOp call)
            EnqueueCallee(call.Callee);
          if (op is MaxonFunctionRefOp fnRef)
            EnqueueCallee(fnRef.FunctionName);
          if (op is MaxonClosureCreateOp closureCreate)
            EnqueueCallee(closureCreate.FunctionName);
          if (op is MaxonGlobalLoadOp { LazyInitFuncName: string lazyInit })
            EnqueueCallee(lazyInit);
          // Array.get for struct elements needs the element type's clone() method
          if (op is MaxonManagedMemGetOp { IsStructElement: true, StructElementTypeName: string elemTypeName }) {
            EnqueueCallee($"{elemTypeName}.clone");
            // Also enqueue the concrete alias clone when the element type is a
            // generic alias (e.g. Entry → ____Tuple_Key_Value_String_i64)
            var resolved = module.ResolveConcreteAlias(elemTypeName);
            if (resolved != elemTypeName)
              EnqueueCallee($"{resolved}.clone");
          }
        }
      }
    }

    module.Functions.RemoveAll(f => !reachable.Contains(f.Name));

    // Remove globals and metadata for unreachable lazy statics
    var removedLazyGlobals = new HashSet<string>();
    foreach (var (varName, meta) in module.GlobalVarInfos) {
      if (!meta.IsLazy) continue;
      var initFuncName = $"{varName}.__lazy_init";
      if (!reachable.Contains(initFuncName)) {
        removedLazyGlobals.Add(varName);
        removedLazyGlobals.Add($"{varName}.__initialized");
      }
    }
    if (removedLazyGlobals.Count > 0) {
      module.Globals.RemoveAll(g => removedLazyGlobals.Contains(g.Name));
      foreach (var name in removedLazyGlobals)
        module.GlobalVarInfos.Remove(name);
    }
  }
}
