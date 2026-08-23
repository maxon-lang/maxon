using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Core;

/// <summary>
/// How the ELEMENTS of a `__ManagedMemory` buffer travel into a COPY of that buffer.
///
/// Duplicating the buffer is a raw memcpy of 8-byte heap POINTERS, so every copy has to give the
/// new buffer its own claim on each element. There are two ways to do that and they are not
/// interchangeable:
///
///   - RETAIN (`mm_incref_managed_elements`) — the copy shares the source's element records. It is
///     refcount-correct and it is what a COPY-ON-WRITE materialisation wants, because a COW buffer
///     and its parent are two buffers holding ONE array value.
///   - DEEP CLONE (`&lt;Element&gt;.clone` per slot) — the copy owns fresh element records. It is what
///     a NEW array value wants: `specs/memory-safety.md` says `.clone()` is "a new, independent
///     copy", and an element reached through `get` is a mutable heap record, so a shared element
///     makes a write through the copy a write to the original.
///
/// The element type decides which one a value copy gets, and the rule is the language's own
/// Cloneable rule (`specs/memory-safety.md`: "all primitives, String, Array, and Cloneable structs
/// qualify"): an element whose type conforms to `Cloneable` is deep-cloned through that type's
/// `clone`, and an element whose type has no Cloneable conformance — a union, a `Vector`, a struct
/// holding one of those — is retained, because the language never promised a copy of it and there
/// is no cloner to call.
///
/// ⭐ THE PREDICATE LIVES HERE ONCE because THREE passes at three different points in the pipeline
/// have to reach the same answer: <c>CloneSynthesisPass</c> (which must MAKE the cloner exist),
/// <c>IrCallGraph</c> (which must keep dead-function elimination from deleting it) and the Maxon→Std
/// lowering (which must CALL it). A second reader that re-derives the rule and lands one step away
/// silently degrades a deep clone into an alias, or emits a call to a symbol nothing kept.
/// </summary>
public static class ManagedElementCopy {
  /// The interface whose conformance the language auto-generates for a struct whose fields are all
  /// Cloneable, and which `.clone()` requires — `specs/memory-safety.md`.
  private const string CloneableInterfaceName = "Cloneable";

  /// The method a Cloneable type is copied through.
  private const string CloneMethodName = "clone";

  /// The `__ManagedMemory` builtin that COPIES a buffer, and therefore has to carry each managed
  /// element into the copy. Both `Array.clone` and `Array.slice` reach it.
  private const string ManagedMemSliceCallee = "__managed_mem_slice";

  /// <summary>
  /// The concrete element type of a managed record type — the `Element` binding on the concrete
  /// `__ManagedMemory_&lt;X&gt;`, or on the FUSED array/vector wrapper that is its own record.
  /// Null when the record carries no element binding at all, which is a raw byte buffer (a bare
  /// `__ManagedMemory`, a fused String or Character).
  /// </summary>
  public static IrType? ElementTypeOf(IrModule<MaxonOp> module, string? managedTypeName) {
    if (managedTypeName == null) return null;
    if (!module.TypeDefs.TryGetValue(managedTypeName, out var typeDef)) return null;
    if (typeDef is not IrStructType structType) return null;
    return structType.TypeParams.TryGetValue(IrStructType.ElementTypeParamName, out var elementType)
      ? elementType
      : null;
  }

  /// <summary>
  /// The element type a `__managed_mem_slice` call copies, or null for any other call and for a
  /// buffer with no element binding. The receiver's own concrete type is the source of truth —
  /// the same one the Maxon→Std lowering reads back off the lowered value — so a fused array
  /// (which IS its own record) and a bare `__ManagedMemory_&lt;X&gt;` answer through one route.
  /// </summary>
  public static IrType? SlicedElementTypeOf(IrModule<MaxonOp> module, MaxonOp op) {
    if (op is not MaxonCallOp { Callee: ManagedMemSliceCallee } call) return null;
    if (call.Args.Count == 0 || call.Args[0] is not MaxonStruct receiver) return null;
    return ElementTypeOf(module, receiver.TypeName);
  }

  /// <summary>
  /// The `&lt;Element&gt;.clone` that a copied element of `elementType` is deep-cloned through, or null
  /// when the element is copied by RETAIN instead.
  ///
  /// Returns null — i.e. retain — for each of these, and each for its own reason:
  ///   - a non-struct element (a scalar, a simple enum): nothing refcounted to copy at all;
  ///   - a UNION or any type with no `Cloneable` conformance (`Vector`, a struct holding one):
  ///     the language auto-generates Cloneable only where every field qualifies, so there is no
  ///     independent copy defined for it and no cloner to call;
  ///   - a Cloneable type whose `clone` carries no BODY: the cloner could not be synthesized
  ///     (`CloneSynthesisPass` declines a struct still holding a type parameter), and calling a
  ///     body-less function would emit a reference to a symbol the backend never defines;
  ///   - a Cloneable type whose `clone` THROWS: the copy loop runs inside a buffer duplication
  ///     with no error path of its own, so there is nowhere for a thrown error to go.
  /// </summary>
  public static string? ClonerNameFor(IrModule<MaxonOp> module, IrType? elementType) {
    if (elementType is not IrStructType elementStruct) return null;

    var resolvedName = module.ResolveConcreteAlias(elementStruct.Name);
    if (!ConformsToCloneable(module, elementStruct, resolvedName)) return null;

    var cloneName = $"{resolvedName}.{CloneMethodName}";
    if (module.FindFunctionByExactName(cloneName) is { } exact && IsCallableCloner(exact)) return exact.Name;

    // A type declared inside a namespace carries it in the function name but not in the type name
    // the element binding holds, so the qualified spelling is found by suffix — the same lookup
    // CloneSynthesisPass uses to decide whether a clone already exists.
    var suffixPattern = $".{cloneName}";
    var qualified = module.Functions.FirstOrDefault(f =>
      f.Name.EndsWith(suffixPattern, StringComparison.Ordinal) && IsCallableCloner(f));
    return qualified?.Name;
  }

  private static bool IsCallableCloner(IrFunction<MaxonOp> cloner) =>
    cloner.Body.Blocks.Count > 0 && cloner.ThrowsType == null;

  /// <summary>
  /// Cloneable conformance, read from the element type's own record and, failing that, from the
  /// module's canonical definition of it. A type carried in a type-parameter binding can be a
  /// partially-specialised copy whose conformance set was never filled in, and judging that copy
  /// would make `Array with String` deep-clone at one call site and alias at another.
  /// </summary>
  private static bool ConformsToCloneable(IrModule<MaxonOp> module, IrStructType elementStruct, string resolvedName) {
    if (elementStruct.ConformingInterfaces.Contains(CloneableInterfaceName)) return true;
    return module.TypeDefs.TryGetValue(resolvedName, out var canonical)
      && canonical is IrStructType canonicalStruct
      && canonicalStruct.ConformingInterfaces.Contains(CloneableInterfaceName);
  }
}
