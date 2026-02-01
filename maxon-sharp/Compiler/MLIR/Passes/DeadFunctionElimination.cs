using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

public static class DeadFunctionElimination {
  public static void Run(MlirModule<MaxonOp> module) {
    var reachable = new HashSet<string>();
    var funcByName = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var func in module.Functions)
      funcByName[func.Name] = func;

    // BFS from main
    var queue = new Queue<string>();
    if (funcByName.ContainsKey("main")) {
      queue.Enqueue("main");
      reachable.Add("main");
    }

    while (queue.Count > 0) {
      var name = queue.Dequeue();
      if (!funcByName.TryGetValue(name, out var func)) continue;

      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonCallOp call && reachable.Add(call.Callee))
            queue.Enqueue(call.Callee);
        }
      }
    }

    module.Functions.RemoveAll(f => !reachable.Contains(f.Name));
  }
}
