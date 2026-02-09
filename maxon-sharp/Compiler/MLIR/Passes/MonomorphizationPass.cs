using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Specializes generic type methods for concrete type aliases.
///
/// When a generic type like Vector has methods that use associated types (e.g., ElementArray),
/// calling those methods on a concrete alias (e.g., __Vector_3_i64) fails because the function
/// signature has placeholder types while the caller provides concrete types.
///
/// This pass:
/// 1. Identifies functions on generic types with associated-type params/returns
/// 2. For each concrete alias, clones the function with substituted types
/// 3. Rewrites call sites to use the specialized function
/// </summary>
public static class MonomorphizationPass {
  public static void Run(MlirModule<MaxonOp> module) {
    var specializations = CollectNeededSpecializations(module);

    if (specializations.Count == 0) return;

    // Clone functions with type substitutions
    var newFunctions = new List<MlirFunction<MaxonOp>>();
    foreach (var spec in specializations) {
      var clonedFunc = new FunctionCloner(spec.SourceFunc, spec.ConcreteTypeName, spec.TypeSubstitution).Clone();
      newFunctions.Add(clonedFunc);
      Logger.Debug(LogCategory.Mlir, $"Monomorphized {spec.SourceFunc.Name} -> {clonedFunc.Name}");
    }

    foreach (var func in newFunctions) {
      module.Functions.Add(func);
    }

    RewriteCallSites(module, specializations);
  }

  internal record Specialization(
    MlirFunction<MaxonOp> SourceFunc,
    string SourceTypeName,
    string ConcreteTypeName,
    TypeSubstitution TypeSubstitution);

  private static List<Specialization> CollectNeededSpecializations(MlirModule<MaxonOp> module) {
    var specializations = new List<Specialization>();

    foreach (var (aliasName, aliasInfo) in module.TypeAliasSources.ToList()) {
      if (aliasInfo.TypeParams == null || aliasInfo.TypeParams.Count == 0) continue;
      if (aliasInfo.TypeParams.Values.Any(t => t is MlirTypeParameterType)) continue;

      var sourceTypeName = aliasInfo.SourceTypeName;

      if (!module.TypeDefs.TryGetValue(sourceTypeName, out var sourceType)) continue;
      if (sourceType is not MlirStructType sourceStruct) continue;
      if (sourceStruct.AssociatedTypeNames.Count == 0) continue;

      var typeSubstitution = TypeSubstitution.Build(sourceStruct, aliasInfo.TypeParams, aliasName, module);

      var sourcePrefix = $"{sourceTypeName}.";
      var sourceSuffix = $".{sourceTypeName}.";
      foreach (var func in module.Functions) {
        bool isSourceMethod = func.Name.StartsWith(sourcePrefix);
        bool isSuffixMatch = !isSourceMethod && func.Name.Contains(sourceSuffix);

        if (!isSourceMethod && !isSuffixMatch) continue;
        if (!NeedsSpecialization(func, sourceStruct)) continue;

        string methodName;
        if (isSourceMethod) {
          methodName = func.Name[sourcePrefix.Length..];
        } else {
          var idx = func.Name.LastIndexOf(sourceSuffix);
          methodName = func.Name[(idx + sourceSuffix.Length)..];
        }

        var specializedName = $"{aliasName}.{methodName}";
        if (module.Functions.Any(f => f.Name == specializedName)) continue;
        if (specializations.Any(s => s.ConcreteTypeName == aliasName && s.SourceFunc == func)) continue;

        specializations.Add(new Specialization(func, sourceTypeName, aliasName, typeSubstitution));
      }
    }

    return specializations;
  }

  private static bool NeedsSpecialization(MlirFunction<MaxonOp> func, MlirStructType sourceStruct) {
    foreach (var paramType in func.ParamTypes) {
      if (paramType is MlirTypeParameterType) return true;
      if (IsAssociatedType(paramType, sourceStruct)) return true;
    }

    if (func.ReturnType is MlirTypeParameterType) return true;
    if (func.ReturnType != null && IsAssociatedType(func.ReturnType, sourceStruct)) return true;

    return false;
  }

  private static bool IsAssociatedType(MlirType type, MlirStructType sourceStruct) {
    if (type is not MlirStructType st) return false;
    if (st.Name == sourceStruct.Name || st.Name == "Self") return true;

    foreach (var assocName in sourceStruct.AssociatedTypeNames) {
      if (st.Name.Contains(assocName)) return true;
    }

    return false;
  }

  private static string? ResolveCalleeRewrite(string callee, string? resultStructTypeName, List<MaxonValue> args, Dictionary<(string, string), string> calleeMap) {
    if (resultStructTypeName != null) {
      var key = (callee, resultStructTypeName);
      if (calleeMap.TryGetValue(key, out var newCallee)) return newCallee;
    }
    if (args.Count > 0 && args[0] is MaxonStruct selfStruct) {
      var key = (callee, selfStruct.TypeName);
      if (calleeMap.TryGetValue(key, out var newCallee)) return newCallee;
    }
    return null;
  }

  private static void RewriteCallSites(MlirModule<MaxonOp> module, List<Specialization> specializations) {
    var calleeMap = new Dictionary<(string, string), string>();
    foreach (var spec in specializations) {
      var dotIdx = spec.SourceFunc.Name.LastIndexOf('.');
      var methodName = dotIdx >= 0 ? spec.SourceFunc.Name[(dotIdx + 1)..] : spec.SourceFunc.Name;
      var newCallee = $"{spec.ConcreteTypeName}.{methodName}";
      calleeMap[(spec.SourceFunc.Name, spec.ConcreteTypeName)] = newCallee;
    }

    var funcLookup = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var f in module.Functions) {
      funcLookup[f.Name] = f;
    }

    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        for (int i = 0; i < block.Operations.Count; i++) {
          var op = block.Operations[i];

          if (op is MaxonCallOp call) {
            var newCallee = ResolveCalleeRewrite(call.Callee, call.ResultStructTypeName, call.Args, calleeMap);
            if (newCallee != null) {
              Logger.Debug(LogCategory.Mlir, $"  Rewrote call {call.Callee} -> {newCallee} in {func.Name}");
              var (newResultKind, newResultStructTypeName) = ResolveMonomorphizedResultType(
                call.ResultKind, call.ResultStructTypeName, newCallee, funcLookup);
              block.Operations[i] = call is MaxonTryCallOp tryCall
                ? new MaxonTryCallOp(newCallee, tryCall.Args, tryCall.Result, tryCall.ErrorFlag, newResultKind, newResultStructTypeName)
                : new MaxonCallOp(newCallee, call.Args, call.Result, newResultKind, newResultStructTypeName);
              if (newResultKind != call.ResultKind && call.Result != null) {
                UpdateSubsequentAssignOps(block, i + 1, call.Result, newResultKind);
              }
            }
          }
        }
      }
    }
  }

  private static (MaxonValueKind?, string?) ResolveMonomorphizedResultType(
      MaxonValueKind? originalKind, string? originalStructTypeName,
      string newCallee, Dictionary<string, MlirFunction<MaxonOp>> funcLookup) {
    if (!funcLookup.TryGetValue(newCallee, out var newFunc) || newFunc.ReturnType == null)
      return (originalKind, originalStructTypeName);

    var kind = newFunc.ReturnType.ToValueKind();
    var typeName = newFunc.ReturnType switch {
      MlirStructType s => s.Name,
      MlirEnumType e => e.Name,
      _ => (string?)null
    };
    return (kind, typeName);
  }

  private static void UpdateSubsequentAssignOps(
      MlirBlock<MaxonOp> block, int startIndex, MaxonValue result, MaxonValueKind? newKind) {
    if (newKind == null) return;
    for (int j = startIndex; j < block.Operations.Count; j++) {
      if (block.Operations[j] is MaxonAssignOp assign && assign.Value == result) {
        block.Operations[j] = new MaxonAssignOp(assign.VarName, assign.Value, assign.IsDeclaration, assign.IsMutable, newKind.Value);
      }
    }
  }
}
