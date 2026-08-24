using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Synthesizes missing clone() methods for types that appear as array elements
/// but whose clone() wasn't generated during parsing (e.g., compiler-generated tuple types
/// created during monomorphization). Runs after monomorphization.
///
/// The BODY it builds is <see cref="CloneBodySynthesis"/>'s, shared with the parser's own
/// synthesis for declared types — the two used to hold separate copies of the field walk.
/// </summary>
public static class CloneSynthesisPass {
  public static void Run(IrModule<MaxonOp> module) {
    var funcByName = module.Functions.ToDictionary(f => f.Name);

    // Collect type names that need clone() (from MaxonManagedMemGetOp), plus the ELEMENT
    // types of every buffer copy: `ManagedElementCopy` deep-clones a Cloneable element through
    // that type's own clone, and this pass is the only thing that can make one exist for a type
    // minted after parsing.
    var neededClones = new HashSet<string>();
    foreach (var func in module.Functions) {
      if (func.IsBuiltinSynthetic) continue;

      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonManagedMemGetOp { IsStructElement: true, StructElementTypeName: string elemType })
            neededClones.Add(elemType);

          // Only a record has members to clone. A scalar element is copied by the buffer memcpy
          // itself and never reaches a cloner.
          var slicedElement = ManagedElementCopy.SlicedElementTypeOf(module, op);
          if (slicedElement is IrStructType or IrEnumType)
            neededClones.Add(slicedElement.Name);
        }
      }
    }

    // Synthesize clone() for any type that needs it but doesn't have it,
    // or replace empty clone stubs (parser generates empty clones for tuple aliases
    // since it doesn't know the concrete fields before monomorphization)
    foreach (var typeName in neededClones) {
      var resolvedName = module.ResolveConcreteAlias(typeName);
      var cloneName = $"{resolvedName}.{ManagedElementCopy.CloneMethodName}";

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
      if (typeDef is not IrStructType and not IrEnumType) continue;
      // A member still spelled as a type parameter has no concrete type to clone through —
      // monomorphization has not bound it, so there is no cloner to name.
      if (HasUnboundTypeParameter(typeDef)) continue;

      // Remove empty stub if it exists before adding the synthesized version
      if (existingFunc != null) {
        module.RemoveFunction(existingFunc);
      }

      var cloneFunc = Synthesize(module, cloneName, resolvedName, typeDef);
      funcByName[cloneFunc.Name] = cloneFunc;
    }
  }

  private static bool HasUnboundTypeParameter(IrType typeDef) => typeDef switch {
    IrStructType structType => structType.Fields.Any(f => f.Type is IrTypeParameterType),
    IrEnumType enumType => enumType.Cases.Any(c =>
      c.AssociatedValues?.Any(v => v.Type is IrTypeParameterType) == true),
    _ => throw new InvalidOperationException(
      $"CloneSynthesisPass: '{typeDef.Name}' is neither a struct nor an enum and has no members")
  };

  /// <summary>
  /// Build and register `&lt;typeName&gt;.clone`.
  ///
  /// The nested-member cloner name is spelled bare rather than resolved through a namespace,
  /// because this pass builds clones only for types minted AFTER parsing — tuples and generic
  /// instances, whose mangled names carry no namespace. A member whose cloner is not found under
  /// that name is copied by pointer, which <see cref="CloneBodySynthesis"/> documents.
  /// </summary>
  private static IrFunction<MaxonOp> Synthesize(
      IrModule<MaxonOp> module, string cloneName, string typeName, IrType typeDef) {
    // ⚠ THE ONE SYNTHESIZED MEMBER THAT DOES NOT INHERIT A TYPE'S VISIBILITY, because there is no
    // declaration to inherit one from. `Parser.AddSynthesizedMember` is where every member the
    // PARSER synthesizes gets its reach; this pass runs after parsing and builds cloners only for
    // types minted after it — tuples and generic instances — which no file declares and which
    // therefore reach wherever the instance does. Stated rather than left to
    // `IsFunctionVisible`'s null-`SourceFilePath` short-circuit, so the decision is written down
    // where it is made instead of being a side effect of a field nobody set.
    var cloneFunc = new IrFunction<MaxonOp>(cloneName, [CloneBodySynthesis.SelfParamName], [typeDef], typeDef, null) {
      IsExported = true
    };
    module.AddFunction(cloneFunc);

    CloneBodySynthesis.EmitCloneBody(module, cloneFunc, typeName, typeDef,
      memberTypeName => $"{memberTypeName}.{ManagedElementCopy.CloneMethodName}");
    return cloneFunc;
  }
}
