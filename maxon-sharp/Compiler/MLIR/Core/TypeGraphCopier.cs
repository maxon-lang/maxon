namespace MaxonSharp.Compiler.Ir.Core;

/// <summary>
/// Copies a module's TYPE GRAPH so that the copy shares no MUTABLE object with the original, while
/// preserving reference identity *within* the copy exactly as the original had it — two references to
/// one <see cref="IrStructType"/> before the copy are two references to one copied struct type after.
///
/// It exists because a compile MUTATES the types it is handed, and the module it is handed comes from
/// a cache that outlives it. <see cref="IrModule{TOp}.Clone"/> already deep-cloned Functions for that
/// reason; it copied TypeDefs BY REFERENCE, which made the clone a half-copy — and a half-copy of a
/// graph nobody else may write to is the same as no copy at all. The mutations are real and were
/// measured, not hypothesised: the Maxon→Standard lowering rewrites every struct field's type in place
/// (<c>field.Type = IrType.Resolve(field.Type)</c>), <c>RefreshTypeAliasTypeParams</c> rebinds
/// <c>TypeParams</c>, the function-backed-enum and struct-raw-value resolvers rewrite enum cases, and
/// the parser adds associated-type names and interface conformances to types the STDLIB declared.
/// Every one of those landed in the process-wide cached stdlib and was still there for the next
/// compile, which is why compiling a project alone and compiling it after another program in the same
/// process emitted different code (board row A4r).
///
/// Only the types that CAN be written to are copied. Primitives, ranged primitives, placeholders,
/// type parameters and the marker backing types are immutable once constructed, so sharing them is
/// not a leak — it is the whole reason this is cheap.
/// </summary>
public sealed class TypeGraphCopier {
  // Keyed by REFERENCE: IrErrorUnionType defines structural Equals/GetHashCode, so a value-keyed
  // memo would collapse two distinct error-union objects into one entry.
  private readonly Dictionary<IrType, IrType> _copies = new(ReferenceEqualityComparer.Instance);

  public IrType? Copy(IrType? type) {
    if (type == null) return null;
    if (_copies.TryGetValue(type, out var existing)) return existing;

    switch (type) {
      case IrStructType st: return CopyStruct(st);
      case IrEnumType et: return CopyEnum(et);

      // Containers: immutable themselves, but they can hold a struct or enum, and a reference into
      // the ORIGINAL graph reached through one of these is exactly as contaminating as a direct one.
      case IrFunctionType ft: {
        var copy = new IrFunctionType([.. ft.ParameterTypes.Select(p => Copy(p)!)], Copy(ft.ReturnType));
        return Remember(ft, copy);
      }
      case IrFunctionBackingType fb:
        return Remember(fb, new IrFunctionBackingType((IrFunctionType)Copy(fb.Signature)!));
      case IrErrorUnionType eu:
        return Remember(eu, new IrErrorUnionType([.. eu.Members.Select(m => (IrEnumType)Copy(m)!)]));

      default: return type;
    }
  }

  private IrType Remember(IrType original, IrType copy) {
    CopySourceAnchor(original, copy);
    _copies[original] = copy;
    return copy;
  }

  private static void CopySourceAnchor(IrType from, IrType to) {
    to.SourceFilePath = from.SourceFilePath;
    to.SourceLine = from.SourceLine;
    to.SourceColumn = from.SourceColumn;
  }

  /// <summary>
  /// A struct's fields are constructed from the ORIGINAL field types and only then remapped, so the
  /// constructor computes byte offsets and the struct size from exactly the types the original saw.
  /// The remap is size-preserving (a copied struct has the same layout as its original), so the
  /// offsets stay correct — and the memo is filled BEFORE the remap, which is what lets a struct hold
  /// a field of its own type without recursing forever.
  /// </summary>
  private IrStructType CopyStruct(IrStructType st) {
    var fields = st.Fields
      .Select(f => new IrStructField(f.Name, f.Type, f.IsExported, f.IsMutable, f.DefaultValue, f.IsModuleVisible))
      .ToList();
    var copy = new IrStructType(st.Name, fields, [.. st.AssociatedTypeNames], st.ConformingInterfaces,
        new Dictionary<string, long>(st.ConstParams), new Dictionary<string, IrType>(st.TypeParams),
        st.IsTuple, CopyWhereConstraints(st.WhereConstraints), st.IsInterfaceAlias) {
      DocString = st.DocString,
      OptionalTrailingTypeParamCount = st.OptionalTrailingTypeParamCount
    };
    foreach (var (name, ranged) in st.InnerRangedAliases)
      copy.InnerRangedAliases[name] = ranged;
    foreach (var (name, ranged) in st.ExtensionRangedAliases)
      copy.ExtensionRangedAliases[name] = ranged;
    Remember(st, copy);

    foreach (var field in copy.Fields)
      field.Type = Copy(field.Type)!;
    RemapTypeParams(copy.TypeParams);
    return copy;
  }

  private IrEnumType CopyEnum(IrEnumType et) {
    var cases = et.Cases.Select(CopyCase).ToList();
    var copy = new IrEnumType(et.Name, cases, et.BackingType, et.ConformingInterfaces,
        [.. et.AssociatedTypeNames], new Dictionary<string, IrType>(et.TypeParams),
        CopyWhereConstraints(et.WhereConstraints)) {
      IsUnion = et.IsUnion,
      HasExplicitBackingValues = et.HasExplicitBackingValues
    };
    Remember(et, copy);

    copy.BackingType = Copy(copy.BackingType);
    foreach (var enumCase in copy.Cases) {
      if (enumCase.AssociatedValues == null) continue;
      for (int i = 0; i < enumCase.AssociatedValues.Count; i++) {
        var (name, valueType) = enumCase.AssociatedValues[i];
        enumCase.AssociatedValues[i] = (name, Copy(valueType)!);
      }
    }
    RemapTypeParams(copy.TypeParams);
    return copy;
  }

  /// <summary>
  /// A struct-backed case's raw value keeps two lists the deferred cross-file resolver DRAINS
  /// (<c>ResolveStructRawValueEnumRefs</c> appends to Fields and clears the unresolved lists), so a
  /// shared <see cref="StructRawValue"/> would make the second compile see the first one's resolution
  /// already done. Every other RawValue shape is a long/string/bool and is immutable.
  /// </summary>
  private static IrEnumCase CopyCase(IrEnumCase c) {
    object? rawValue = c.RawValue;
    if (rawValue is StructRawValue srv) {
      var copiedRaw = new StructRawValue(srv.StructTypeName, [.. srv.Fields]);
      copiedRaw.UnresolvedEnumRefs.AddRange(srv.UnresolvedEnumRefs);
      copiedRaw.UnresolvedConstRefs.AddRange(srv.UnresolvedConstRefs);
      rawValue = copiedRaw;
    }

    return new IrEnumCase(c.Name, c.Ordinal, rawValue,
        c.AssociatedValues == null ? null : [.. c.AssociatedValues]) {
      SourceLine = c.SourceLine,
      SourceColumn = c.SourceColumn
    };
  }

  private void RemapTypeParams(Dictionary<string, IrType> typeParams) {
    foreach (var key in typeParams.Keys.ToList())
      typeParams[key] = Copy(typeParams[key])!;
  }

  private static Dictionary<string, List<string>> CopyWhereConstraints(Dictionary<string, List<string>> source) {
    var copy = new Dictionary<string, List<string>>(source.Count);
    foreach (var (key, interfaces) in source) copy[key] = [.. interfaces];
    return copy;
  }
}
