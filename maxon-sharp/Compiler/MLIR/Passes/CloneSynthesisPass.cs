using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Synthesizes missing clone() methods for struct types that appear as array elements
/// but whose clone() wasn't generated during parsing (e.g., compiler-generated tuple types
/// created during monomorphization). Runs after monomorphization.
/// </summary>
public static class CloneSynthesisPass {
  public static void Run(MlirModule<MaxonOp> module) {
    var funcByName = module.Functions.ToDictionary(f => f.Name);

    // Collect struct type names that need clone() (from MaxonManagedMemGetOp)
    var neededClones = new HashSet<string>();
    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonManagedMemGetOp { IsStructElement: true, StructElementTypeName: string elemType })
            neededClones.Add(elemType);
        }
      }
    }

    // Synthesize clone() for any type that needs it but doesn't have it,
    // or replace empty clone stubs (parser generates empty clones for tuple aliases
    // since it doesn't know the concrete fields before monomorphization)
    foreach (var typeName in neededClones) {
      var resolvedName = module.ResolveConcreteAlias(typeName);
      var cloneName = $"{resolvedName}.clone";

      // Check if a non-empty clone already exists
      bool hasNonEmptyClone = false;
      if (funcByName.TryGetValue(cloneName, out var existingFunc)) {
        hasNonEmptyClone = existingFunc.Body.Blocks.Count > 0;
      }
      if (!hasNonEmptyClone) {
        var suffixPattern = $".{cloneName}";
        hasNonEmptyClone = funcByName.Values.Any(f =>
          f.Name.EndsWith(suffixPattern) && f.Body.Blocks.Count > 0);
      }
      if (hasNonEmptyClone) continue;

      if (!module.TypeDefs.TryGetValue(resolvedName, out var typeDef)) continue;
      if (typeDef is not MlirStructType structType) continue;
      if (structType.Fields.Any(f => f.Type is MlirTypeParameterType)) continue;

      // Remove empty stub if it exists before adding the synthesized version
      if (existingFunc != null) {
        module.Functions.Remove(existingFunc);
      }

      var cloneFunc = SynthesizeClone(module, resolvedName, structType);
      funcByName[cloneFunc.Name] = cloneFunc;
    }
  }

  private static MlirFunction<MaxonOp> SynthesizeClone(
      MlirModule<MaxonOp> module, string typeName, MlirStructType structType) {
    var cloneName = $"{typeName}.clone";
    var cloneFunc = new MlirFunction<MaxonOp>(
      cloneName, ["self"], [(MlirType)structType], (MlirType)structType, null);
    var block = cloneFunc.Body.AddBlock("entry");

    var selfParam = new MaxonStructParamOp(0, "self", typeName);
    block.AddOp(selfParam);

    var fieldValues = new List<(string FieldName, MaxonValue Value)>();
    foreach (var field in structType.Fields) {
      var fieldKind = field.Type.ToValueKind();
      string? fieldStructTypeName = null;
      if (field.Type is MlirStructType fst) fieldStructTypeName = fst.Name;
      else if (field.Type is MlirUnionType fut) fieldStructTypeName = fut.Name;

      var accessOp = new MaxonFieldAccessOp(selfParam.Result, typeName, field.Name, fieldKind, fieldStructTypeName);
      block.AddOp(accessOp);

      MaxonValue fieldValue = accessOp.Result;
      if (field.Type is MlirStructType nestedStruct) {
        var nestedCloneName = $"{nestedStruct.Name}.clone";
        var nestedCloneCall = new MaxonCallOp(nestedCloneName, [accessOp.Result], MaxonValueKind.Struct, nestedStruct.Name);
        block.AddOp(nestedCloneCall);
        fieldValue = nestedCloneCall.Result!;
      }
      fieldValues.Add((field.Name, fieldValue));
    }

    // Scope management: ensures the return value has rc=1
    var scopeVar = $"__scope_{MlirContext.Current.NextId()}";
    block.AddOp(new MaxonScopeEnterOp(scopeVar, cloneName));

    var structLit = new MaxonStructLiteralOp(typeName, fieldValues);
    block.AddOp(structLit);

    var retvalName = $"__retval_{MlirContext.Current.NextId()}";
    block.AddOp(new MaxonAssignOp(retvalName, structLit.Result, true, false, MaxonValueKind.Struct));
    block.AddOp(new MaxonMoveOp(retvalName, scopeVar, "return_move"));
    block.AddOp(new MaxonScopeExitOp(scopeVar, "return_cleanup"));
    block.AddOp(new MaxonReturnOp(structLit.Result));

    module.AddFunction(cloneFunc);
    Logger.Debug(LogCategory.Mlir, $"Synthesized missing clone: {cloneName}");
    return cloneFunc;
  }
}
