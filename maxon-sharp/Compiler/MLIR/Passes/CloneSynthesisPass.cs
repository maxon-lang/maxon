using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Synthesizes missing clone() methods for struct types that appear as array elements
/// but whose clone() wasn't generated during parsing (e.g., compiler-generated tuple types
/// created during monomorphization). Runs after monomorphization.
/// </summary>
public static class CloneSynthesisPass {
  public static void Run(IrModule<MaxonOp> module) {
    var funcByName = module.Functions.ToDictionary(f => f.Name);

    // Collect struct type names that need clone() (from MaxonManagedMemGetOp)
    var neededClones = new HashSet<string>();
    foreach (var func in module.Functions) {
      if (func.IsBuiltinSynthetic) continue;

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
      if (typeDef is not IrStructType structType) continue;
      if (structType.Fields.Any(f => f.Type is IrTypeParameterType)) continue;

      // Remove empty stub if it exists before adding the synthesized version
      if (existingFunc != null) {
        module.RemoveFunction(existingFunc);
      }

      var cloneFunc = SynthesizeClone(module, resolvedName, structType);
      funcByName[cloneFunc.Name] = cloneFunc;
    }
  }

  private static IrFunction<MaxonOp> SynthesizeClone(
      IrModule<MaxonOp> module, string typeName, IrStructType structType) {
    var cloneName = $"{typeName}.clone";
    var cloneFunc = new IrFunction<MaxonOp>(
      cloneName, ["self"], [(IrType)structType], (IrType)structType, null);
    var block = cloneFunc.Body.AddBlock("entry");

    var selfParam = new MaxonStructParamOp(0, "self", typeName);
    block.AddOp(selfParam);

    var fieldValues = new List<(string FieldName, MaxonValue Value)>();
    foreach (var field in structType.Fields) {
      var fieldKind = field.Type.ToValueKind();
      string? fieldStructTypeName = null;
      if (field.Type is IrStructType fst) fieldStructTypeName = fst.Name;
      else if (field.Type is IrEnumType fut) fieldStructTypeName = fut.Name;

      var accessOp = new MaxonFieldAccessOp(selfParam.Result, typeName, field.Name, fieldKind, fieldStructTypeName);
      block.AddOp(accessOp);

      MaxonValue fieldValue = accessOp.Result;
      if (field.Type is IrStructType nestedStruct) {
        var nestedCloneName = $"{nestedStruct.Name}.clone";
        var nestedCloneCall = new MaxonCallOp(nestedCloneName, [accessOp.Result], MaxonValueKind.Struct, nestedStruct.Name);
        block.AddOp(nestedCloneCall);
        fieldValue = nestedCloneCall.Result!;
      }
      fieldValues.Add((field.Name, fieldValue));
    }

    var structLit = new MaxonStructLiteralOp(typeName, fieldValues);
    block.AddOp(structLit);

    var retvalName = $"__retval_{IrContext.Current.NextId()}";
    block.AddOp(new MaxonAssignOp(retvalName, structLit.Result, true, false, MaxonValueKind.Struct));
    block.AddOp(new MaxonScopeEndOp([retvalName], keepVars: [retvalName]));
    block.AddOp(new MaxonReturnOp(structLit.Result));

    module.AddFunction(cloneFunc);
    Logger.Debug(LogCategory.Ir, $"Synthesized missing clone: {cloneName}");
    return cloneFunc;
  }
}
