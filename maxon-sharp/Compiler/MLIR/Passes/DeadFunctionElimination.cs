using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

public static class DeadFunctionElimination {
  public static void Run(MlirModule<MaxonOp> module) {
    var reachable = new HashSet<string>();
    var funcByName = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var func in module.Functions)
      funcByName[func.Name] = func;

    // BFS from main (check for exact match or suffix match)
    var queue = new Queue<string>();
    var mainFunc = module.Functions.FirstOrDefault(f => f.Name == "main" || f.Name.EndsWith(".main"));
    if (mainFunc != null) {
      queue.Enqueue(mainFunc.Name);
      reachable.Add(mainFunc.Name);
    }

    while (queue.Count > 0) {
      var name = queue.Dequeue();
      if (!funcByName.TryGetValue(name, out var func)) continue;

      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonCallOp call && reachable.Add(call.Callee))
            queue.Enqueue(call.Callee);
          if (op is MaxonTryCallOp tryCall && reachable.Add(tryCall.Callee))
            queue.Enqueue(tryCall.Callee);
          if (op is MaxonFunctionRefOp fnRef && reachable.Add(fnRef.FunctionName))
            queue.Enqueue(fnRef.FunctionName);
          // String interpolation with struct values may call toString dynamically
          if (op is MaxonStringInterpOp interp) {
            foreach (var (isLiteral, _, exprValue, _) in interp.Parts) {
              if (!isLiteral && exprValue is MaxonStruct structVal) {
                // Try both exact and suffix match for toString method
                var exactName = $"{structVal.TypeName}.toString";
                if (funcByName.ContainsKey(exactName) && reachable.Add(exactName))
                  queue.Enqueue(exactName);
                var suffix = $".{structVal.TypeName}.toString";
                foreach (var candidate in funcByName.Keys) {
                  if (candidate.EndsWith(suffix) && reachable.Add(candidate))
                    queue.Enqueue(candidate);
                }
              }
            }
          }
        }
      }
    }

    module.Functions.RemoveAll(f => !reachable.Contains(f.Name));
  }
}
