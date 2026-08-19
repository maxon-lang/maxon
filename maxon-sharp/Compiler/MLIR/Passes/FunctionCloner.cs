using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Clones a generic function with type substitutions applied, producing
/// a monomorphized specialization for a concrete type alias.
/// One instance per specialization; holds all mutable cloning state.
/// </summary>
internal class FunctionCloner : IOpSubstitution {
  private readonly IrFunction<MaxonOp> _sourceFunc;
  private readonly string _concreteTypeName;
  private readonly TypeSubstitution _typeSubstitution;
  private readonly Dictionary<string, TypeAliasInfo> _typeAliasSources;
  private readonly Dictionary<string, IrType> _typeDefs;
  private readonly IrModule<MaxonOp>? _module;

  // Resolved return type after substitution (used for tuple name correction)
  private IrType? _resolvedReturnType;

  // When set, Clone() uses this name verbatim instead of "{ConcreteType}.{methodName}".
  // Used by closure-body specialization where source name has no "Type.method" structure.
  internal string? OverrideClonedName { get; set; }

  // Side-list of closure-body specializations scheduled while cloning this function.
  // MonomorphizationPass.Run drains this and emits the specialized closure bodies.
  internal List<(string SourceName, string SpecName, IrFunction<MaxonOp> SourceFunc)> ClosureSpecializations { get; } = [];

  // Per-cloner cache of (sourceClosureName -> specializedName) so repeated refs collapse.
  private readonly Dictionary<string, string> _closureSpecCache = [];

  // Cloning state
  private readonly Dictionary<int, MaxonValue> _valueMap = [];
  private readonly HashSet<string> _floatVars = [];
  private readonly Dictionary<string, string> _varTypeParams = [];
  private readonly HashSet<string> _structTypeParams = [];
  private readonly HashSet<string> _enumTypeParams = [];
  private readonly Dictionary<string, string> _structVars = [];
  private readonly Dictionary<string, string> _enumVars = [];
  private readonly Dictionary<string, MaxonValueKind> _resolvedVarKinds = [];

  // Derived from source function
  private readonly HashSet<int> _elementPolymorphicIndices = [];
  private readonly IrType? _concreteElementType;
  private readonly bool _substituteToFloat;
  private readonly bool _isBitPackedElement;

  public FunctionCloner(
      IrFunction<MaxonOp> sourceFunc,
      string concreteTypeName,
      TypeSubstitution typeSubstitution,
      Dictionary<string, TypeAliasInfo> typeAliasSources,
      Dictionary<string, IrType> typeDefs,
      IrModule<MaxonOp>? module = null) {
    _sourceFunc = sourceFunc;
    _concreteTypeName = concreteTypeName;
    _typeSubstitution = typeSubstitution;
    _typeAliasSources = typeAliasSources;
    _typeDefs = typeDefs;
    _module = module;
    _typeSubstitution.SetTypeAliasSources(typeAliasSources);

    // Derive element-polymorphic param indices from function signature types
    for (int i = 0; i < sourceFunc.ParamTypes.Count; i++) {
      if (sourceFunc.ParamTypes[i] is IrTypeParameterType) {
        _elementPolymorphicIndices.Add(i);
      }
    }

    // Get the concrete Element type from the substitution
    _concreteElementType = typeSubstitution.GetValueOrDefault("Element");
    _substituteToFloat = _concreteElementType == IrType.F64;
    _isBitPackedElement = _concreteElementType == IrType.I1;

    // For multi-parameter generics (Map<Key, Value>), track which type parameter
    // each variable corresponds to, so we can resolve TypeParameter kinds correctly.
    for (int i = 0; i < sourceFunc.ParamTypes.Count; i++) {
      if (sourceFunc.ParamTypes[i] is IrTypeParameterType tp
          && i < sourceFunc.ParamNames.Count) {
        _varTypeParams[sourceFunc.ParamNames[i]] = tp.ParameterName;
      }
    }

    // Track which type param names resolve to struct or associated-value enum types
    foreach (var (paramName, concreteType) in typeSubstitution.Entries) {
      if (concreteType is IrStructType && paramName != "Self"
          && !paramName.EndsWith("Array") && paramName != "Entry") {
        _structTypeParams.Add(paramName);
      }
      if (concreteType is IrEnumType { HasAssociatedValues: true } && paramName != "Self") {
        _enumTypeParams.Add(paramName);
      }
    }

    // Seed structVars from params that resolve to struct types
    foreach (var (varName, typeParamName) in _varTypeParams) {
      if (_enumTypeParams.Contains(typeParamName)
          && typeSubstitution.TryGetValue(typeParamName, out var et) && et is IrEnumType eut) {
        _enumVars[varName] = eut.Name;
      }
      if (_structTypeParams.Contains(typeParamName)
          && typeSubstitution.TryGetValue(typeParamName, out var ct) && ct is IrStructType st) {
        _structVars[varName] = st.Name;
      }
    }
  }

  /// <summary>
  /// Clones the source function with all type substitutions applied.
  /// Returns the new specialized function.
  /// </summary>
  public IrFunction<MaxonOp> Clone() {
    // Newly minted MaxonValues here belong to the source function's id namespace
    // (stdlib-bit set vs unset). Cloning a stdlib function with the user-side counter
    // would let the cloned ops alias real user MaxonValues in valueMap during lowering.
    var prevMode = IrContext.Current.StdlibLoweringMode;
    IrContext.Current.StdlibLoweringMode = _sourceFunc.IsStdlib;
    try {
    // Compute new function name.
    // For closure bodies (no Type.method structure) the caller sets
    // OverrideClonedName to the pre-computed specialized closure name.
    // Use LastIndexOf to handle namespace-qualified names like "stdlib.Array.push"
    string newFuncName;
    if (OverrideClonedName != null) {
      newFuncName = OverrideClonedName;
    } else {
      var dotIdx = _sourceFunc.Name.LastIndexOf('.');
      var methodName = dotIdx >= 0 ? _sourceFunc.Name[(dotIdx + 1)..] : _sourceFunc.Name;
      newFuncName = $"{_concreteTypeName}.{methodName}";
    }

    // Clone param types with substitution
    var newParamTypes = new List<IrType>();
    for (int i = 0; i < _sourceFunc.ParamTypes.Count; i++) {
      var paramType = _sourceFunc.ParamTypes[i];
      if (paramType is IrTypeParameterType tp
          && _typeSubstitution.TryGetValue(tp.ParameterName, out var concreteType)) {
        newParamTypes.Add(concreteType);
      } else {
        newParamTypes.Add(_typeSubstitution.SubstituteType(paramType));
      }
    }

    // Clone return type with substitution
    IrType? newReturnType;
    if (_sourceFunc.ReturnType is IrTypeParameterType retTp
        && _typeSubstitution.TryGetValue(retTp.ParameterName, out var concreteRetType)) {
      newReturnType = concreteRetType;
    } else {
      newReturnType = _sourceFunc.ReturnType != null
        ? _typeSubstitution.SubstituteType(_sourceFunc.ReturnType)
        : null;
    }

    _resolvedReturnType = newReturnType;

    var newFunc = new IrFunction<MaxonOp>(
      newFuncName,
      [.. _sourceFunc.ParamNames],
      newParamTypes,
      newReturnType,
      _sourceFunc.ThrowsType) {
      IsStdlib = _sourceFunc.IsStdlib,
      SourceLine = _sourceFunc.SourceLine,
      SourceColumn = _sourceFunc.SourceColumn,
      SourceFilePath = _sourceFunc.SourceFilePath,
      ReturnsSelf = _sourceFunc.ReturnsSelf
    };

    // Clone all blocks and operations
    var extraOps = new List<MaxonOp>();
    foreach (var block in _sourceFunc.Body.Blocks) {
      var newBlock = newFunc.Body.AddBlock(block.Name);
      var ops = block.Operations;

      for (int opIdx = 0; opIdx < ops.Count; opIdx++) {
        var op = ops[opIdx];
        extraOps.Clear();

        // For struct literals that are fields of a following wrapper struct,
        // pass the wrapper so the correct concrete type can be derived from
        // the wrapper's field definitions when the substitution map is ambiguous.
        MaxonOp clonedOp;
        if (op is MaxonStructLiteralOp innerLit) {
          MaxonStructLiteralOp? nextWrapper = null;
          for (int j = opIdx + 1; j < ops.Count; j++) {
            if (ops[j] is MaxonStructLiteralOp candidate && candidate != innerLit) {
              nextWrapper = candidate;
              break;
            }
            if (ops[j] is not MaxonLiteralOp) break;
          }
          clonedOp = CloneStructLiteralOp(innerLit, extraOps, nextWrapper);
        } else {
          clonedOp = CloneOp(op, extraOps);
        }

        // A debug span is keyed by OP REFERENCE (IrFunction._debugSpans), and every op here is a
        // BRAND-NEW object — so without this a monomorphized generic ships an EMPTY line table and no
        // debugger can stop anywhere inside one. Nothing reports that as missing: the lowering's
        // DebugSpanFlow.Mark simply records nothing, and the specialization's instructions fall into
        // whatever range precedes them. `extraOps` are part of THIS source op's expansion, so they
        // carry its span too. Under --no-debug-info the source table is null, so this allocates nothing.
        bool hasSpan = _sourceFunc.TryGetDebugSpan(op, out var span);

        foreach (var extra in extraOps) {
          newBlock.AddOp(extra);
          if (hasSpan) newFunc.SetDebugSpan(extra, span);
        }
        newBlock.AddOp(clonedOp);
        if (hasSpan) newFunc.SetDebugSpan(clonedOp, span);
      }
    }

    // Post-processing: fix __ManagedMemory element_size for multi-parameter generic types
    // where a single Element substitution doesn't apply uniformly.
    if (_concreteElementType == null && _typeSubstitution.Count > 2) {
      PatchManagedMemoryElementSizes(newFunc);
    }

    return newFunc;
    } finally {
      IrContext.Current.StdlibLoweringMode = prevMode;
    }
  }

  // --- Value mapping helpers ---

  private MaxonValue MapValue(MaxonValue old) {
    if (_valueMap.TryGetValue(old.Id, out var mapped)) {
      return mapped;
    }
    // Value hasn't been mapped yet - create a new value of the same type
    // (This shouldn't happen for well-formed functions since defs precede uses)
    var newId = IrContext.Current.NextId();
    MaxonValue newVal = old switch {
      MaxonInteger => new MaxonInteger(newId),
      MaxonFloat => new MaxonFloat(newId),
      MaxonBool => new MaxonBool(newId),
      MaxonByte => new MaxonByte(newId),
      MaxonShort => new MaxonShort(newId),
      MaxonStruct s => new MaxonStruct(newId, _typeSubstitution.SubstituteName(s.TypeName)),
      MaxonEnum e => new MaxonEnum(newId, e.TypeName),
      // A clone is the same value under a new id, so it keeps the identity the original carried —
      // a struct's name, an enum's name, and a function's SIGNATURE alike. Dropping the signature
      // here would make the clone answer "I do not know what I am" to every rule that asks.
      MaxonFunctionPtr f => new MaxonFunctionPtr(newId, f.FunctionType),
      _ => throw new InvalidOperationException($"Unknown MaxonValue type: {old.GetType()}")
    };
    _valueMap[old.Id] = newVal;
    return newVal;
  }

  private void RegisterResult(MaxonValue oldResult, MaxonValue newResult) {
    _valueMap[oldResult.Id] = newResult;
  }

  // --- State query helpers ---

  private string SubName(string name) => _typeSubstitution.SubstituteName(name);

  private bool IsManagedMemoryType(string typeName) =>
    TypeAliasInfo.IsManagedMemoryType(typeName, _typeAliasSources);

  private bool IsStructTypeParam(string? typeParamName) {
    return typeParamName != null && _structTypeParams.Contains(typeParamName);
  }

  private string? GetVarTypeParam(string varName) {
    return _varTypeParams.GetValueOrDefault(varName);
  }

  private string? GetStructTypeName(string typeParamName) {
    if (_typeSubstitution.TryGetValue(typeParamName, out var t) && t is IrStructType st)
      return st.Name;
    return null;
  }

  private bool IsEnumTypeParam(string? typeParamName) {
    return typeParamName != null && _enumTypeParams.Contains(typeParamName);
  }

  private string? GetEnumTypeName(string typeParamName) {
    if (_typeSubstitution.TryGetValue(typeParamName, out var t) && t is IrEnumType ut && ut.HasAssociatedValues)
      return ut.Name;
    return null;
  }

  private bool IsElementPolymorphic(int paramIndex) =>
    _elementPolymorphicIndices.Contains(paramIndex);

  // --- Closure specialization ---

  // If `referencedName` names a closure body that is generic w.r.t. the
  // active substitution, schedule a specialized clone keyed on the concrete
  // type and return the specialized name. Otherwise return the name unchanged.
  // Without _module access (defensive fallback), returns the name unchanged.
  private string SpecializeClosureName(string referencedName) {
    if (_module == null) return referencedName;
    if (_closureSpecCache.TryGetValue(referencedName, out var cached)) return cached;
    var src = _module.FindFunctionByExactName(referencedName);
    if (src == null) {
      _closureSpecCache[referencedName] = referencedName;
      return referencedName;
    }

    if (!ClosureBodyIsGenericForSubstitution(src)) {
      _closureSpecCache[referencedName] = referencedName;
      return referencedName;
    }

    var specName = $"{referencedName}${_concreteTypeName}";
    _closureSpecCache[referencedName] = specName;
    // Dedup against existing schedule (same source closure referenced twice
    // in the same parent body should produce only one specialized body).
    if (!ClosureSpecializations.Any(e => e.SourceName == referencedName)) {
      ClosureSpecializations.Add((referencedName, specName, src));
    }
    return specName;
  }

  // A closure body is generic w.r.t. this cloner's substitution iff any of:
  //   - a parameter or return type is IrTypeParameterType,
  //   - any op has a MaxonValueKind.TypeParameter result,
  //   - any op references a struct/enum type name appearing in the substitution.
  private bool ClosureBodyIsGenericForSubstitution(IrFunction<MaxonOp> closure) {
    foreach (var pt in closure.ParamTypes) {
      if (pt is IrTypeParameterType) return true;
    }
    if (closure.ReturnType is IrTypeParameterType) return true;

    var substKeys = _typeSubstitution.Entries.Select(kv => kv.Key).ToHashSet();

    foreach (var block in closure.Body.Blocks) {
      foreach (var op in block.Operations) {
        switch (op) {
          case MaxonParamOp p when p.ValueKind == MaxonValueKind.TypeParameter: return true;
          case MaxonStructParamOp sp when substKeys.Contains(sp.StructTypeName): return true;
          case MaxonStructVarRefOp sv when substKeys.Contains(sv.StructTypeName): return true;
          case MaxonFieldAccessOp fa when substKeys.Contains(fa.TypeName)
            || (fa.ResultStructTypeName != null && substKeys.Contains(fa.ResultStructTypeName)): return true;
          case MaxonFieldAssignOp fa when substKeys.Contains(fa.TypeName): return true;
          case MaxonEnumParamOp ep when substKeys.Contains(ep.EnumTypeName): return true;
          case MaxonEnumVarRefOp ev when substKeys.Contains(ev.EnumTypeName): return true;
          case MaxonEnumLiteralOp el when substKeys.Contains(el.EnumTypeName): return true;
          case MaxonEnumConstructOp ec when substKeys.Contains(ec.EnumTypeName): return true;
          case MaxonEnumTagOp et when substKeys.Contains(et.EnumTypeName): return true;
          case MaxonEnumPayloadOp epo when substKeys.Contains(epo.EnumTypeName): return true;
          case MaxonCallOp call when CalleeUsesSubstitution(call.Callee, substKeys): return true;
          case MaxonTryCallOp tryCall when CalleeUsesSubstitution(tryCall.Callee, substKeys): return true;
        }
      }
    }
    return false;
  }

  private static bool CalleeUsesSubstitution(string callee, HashSet<string> substKeys) {
    // Method calls are encoded as "Type.method" or "stdlib.Type.method"; check
    // each dot-segment so e.g. "Element.compare" is matched when Element is
    // in the substitution.
    foreach (var segment in callee.Split('.')) {
      if (substKeys.Contains(segment)) return true;
    }
    return false;
  }

  // --- Op cloning ---

  private MaxonOp CloneOp(MaxonOp op, List<MaxonOp> extraOps) {
    switch (op) {
      // Only the ops whose clone depends on THIS pass's substitution live here: the ones that read
      // per-function state accumulated while cloning (float/struct/enum var tracking), the ones that
      // may emit extra ops, and the ones whose type names the generic substitution rewrites but the
      // interface-alias substitution deliberately does not. Everything else is the shared rule -
      // see SubstitutingOpCloner, which owns the roster and the single unhandled-op message.
      case MaxonAssignOp assign: return CloneAssignOp(assign);
      case MaxonParamOp param: return CloneParamOp(param);
      case MaxonVarRefOp varRef: return CloneVarRefOp(varRef);
      case MaxonBinOp binOp: return CloneBinOp(binOp, extraOps);
      case MaxonTryCallOp tryCall: return CloneTryCallOp(tryCall);
      case MaxonCallOp call: return CloneCallOp(call);
      case MaxonIndirectCallOp indirect: return CloneIndirectCallOp(indirect);
      case MaxonStructLiteralOp structLit: return CloneStructLiteralOp(structLit, extraOps);

      // String / byte-string literals and interpolation
      case MaxonStringLiteralOp strLit: { var c = new MaxonStringLiteralOp(strLit.Value, SubName(strLit.StringTypeName)); RegisterResult(strLit.Result, c.Result); return c; }
      case MaxonByteStringLiteralOp bstrLit: { var c = new MaxonByteStringLiteralOp(bstrLit.Value, SubName(bstrLit.ArrayTypeName)); RegisterResult(bstrLit.Result, c.Result); return c; }
      case MaxonStringInterpOp interp: {
        var newParts = interp.Parts.Select(p => (p.IsLiteral, p.LiteralValue, p.ExprValue != null ? MapValue(p.ExprValue) : (MaxonValue?)null, p.FormatSpec, p.OptimalType)).ToList();
        var c = new MaxonStringInterpOp(newParts, SubName(interp.StringTypeName));
        RegisterResult(interp.Result, c.Result);
        return c;
      }

      // Enum ops
      case MaxonEnumLiteralOp el: { var c = el.BackingKind is MaxonValueKind.Float or MaxonValueKind.Float32 ? new MaxonEnumLiteralOp(SubName(el.EnumTypeName), el.CaseName, el.FloatValue) : new MaxonEnumLiteralOp(SubName(el.EnumTypeName), el.CaseName, el.IntValue); RegisterResult(el.Result, c.Result); return c; }
      case MaxonEnumParamOp ep: { var c = new MaxonEnumParamOp(ep.Index, ep.Name, SubName(ep.EnumTypeName), ep.BackingKind); RegisterResult(ep.Result, c.Result); return c; }
      case MaxonEnumVarRefOp ev: { var c = new MaxonEnumVarRefOp(ev.VarName, SubName(ev.EnumTypeName), ev.BackingKind); RegisterResult(ev.Result, c.Result); return c; }
      case MaxonEnumRawValueOp er: { var c = new MaxonEnumRawValueOp(MapValue(er.EnumValue), SubName(er.EnumTypeName), er.ResultKind); RegisterResult(er.Result, c.Result); return c; }
      case MaxonEnumOrdinalOp eo: { var c = new MaxonEnumOrdinalOp(MapValue(eo.EnumValue), SubName(eo.EnumTypeName)); RegisterResult(eo.Result, c.Result); return c; }
      case MaxonEnumNameOp en: { var c = new MaxonEnumNameOp(MapValue(en.EnumValue), SubName(en.EnumTypeName)); RegisterResult(en.Result, c.Result); return c; }
      case MaxonEnumStringRawValueOp esr: { var c = new MaxonEnumStringRawValueOp(MapValue(esr.EnumValue), SubName(esr.EnumTypeName), esr.IsChar); RegisterResult(esr.Result, c.Result); return c; }
      case MaxonEnumStructRawValueOp esrv: { var c = new MaxonEnumStructRawValueOp(MapValue(esrv.EnumValue), SubName(esrv.EnumTypeName), esrv.StructTypeName); RegisterResult(esrv.Result, c.Result); return c; }
      case MaxonEnumStructRawFieldOp esrf: { var c = new MaxonEnumStructRawFieldOp(MapValue(esrf.EnumValue), SubName(esrf.EnumTypeName), esrf.StructTypeName, esrf.FieldName, esrf.ResultKind, esrf.ResultTypeName == null ? null : SubName(esrf.ResultTypeName)); RegisterResult(esrf.Result, c.Result); return c; }
      case MaxonEnumFunctionRawValueOp efrv: { var c = new MaxonEnumFunctionRawValueOp(MapValue(efrv.EnumValue), SubName(efrv.EnumTypeName), efrv.Signature); RegisterResult(efrv.Result, c.Result); return c; }
      case MaxonErrorFlagToEnumOp ef: { var c = new MaxonErrorFlagToEnumOp(MapValue(ef.ErrorFlag), SubName(ef.EnumTypeName), ef.BackingKind, ef.HasAssociatedValues); RegisterResult(ef.Result, c.Result); return c; }
      case MaxonEnumPayloadOp payload: {
        var resultKind = _typeSubstitution.SubstituteValueKind(payload.ResultKind);
        var resultStructTypeName = payload.ResultStructTypeName != null ? SubName(payload.ResultStructTypeName) : null;
        // When substitution resolved a type parameter to a concrete struct/enum type,
        // populate the type name so downstream lowering can track it correctly
        if (resultStructTypeName == null && resultKind == MaxonValueKind.Struct && _concreteElementType is IrStructType payloadSt)
          resultStructTypeName = payloadSt.Name;
        if (resultStructTypeName == null && resultKind == MaxonValueKind.Enum && _concreteElementType is IrEnumType payloadUn)
          resultStructTypeName = payloadUn.Name;
        var c = new MaxonEnumPayloadOp(MapValue(payload.EnumValue), SubName(payload.EnumTypeName), payload.PayloadIndex, resultKind, resultStructTypeName);
        RegisterResult(payload.Result, c.Result);
        return c;
      }

      // Managed memory ops whose element metadata is RE-DERIVED from the binding, not copied
      case MaxonManagedMemClearOp memClear: {
        var paramKey = memClear.TypeParamName ?? "Element";
        var isHeapPtrElem = _typeSubstitution.TryGetValue(paramKey, out var clearElemType)
          && (clearElemType is IrStructType || clearElemType is IrEnumType { HasAssociatedValues: true });
        string? elemTypeName = null;
        if (isHeapPtrElem && clearElemType is IrType named) {
          elemTypeName = named.Name;
        }
        return new MaxonManagedMemClearOp(MapValue(memClear.ManagedStruct)) {
          IsStructElement = isHeapPtrElem,
          StructElementTypeName = elemTypeName,
          TypeParamName = memClear.TypeParamName,
          IsBitPacked = memClear.IsBitPacked || _isBitPackedElement
        };
      }
      case MaxonManagedMemAppendOp ma: {
        var isHeapPtrElem = ma.IsStructElement;
        if (ma.TypeParamName != null && _typeSubstitution.TryGetValue(ma.TypeParamName, out var appendElemType))
          isHeapPtrElem = appendElemType is IrStructType || appendElemType is IrEnumType { HasAssociatedValues: true };
        return new MaxonManagedMemAppendOp(MapValue(ma.ManagedStruct), MapValue(ma.Other)) {
          IsStructElement = isHeapPtrElem,
          TypeParamName = ma.TypeParamName,
          IsBitPacked = ma.IsBitPacked || _isBitPackedElement
        };
      }

      // Function values: only this pass rewrites the SIGNATURE, because only it binds the type
      // parameters a closure signature is written in.
      case MaxonFunctionRefOp fr: {
        var specName = SpecializeClosureName(fr.FunctionName);
        var newFnType = (IrFunctionType)_typeSubstitution.SubstituteType(fr.FunctionType);
        var c = new MaxonFunctionRefOp(specName, newFnType);
        RegisterResult(fr.Result, c.Result);
        return c;
      }
      case MaxonFunctionVarRefOp fv: { var c = new MaxonFunctionVarRefOp(fv.VarName, (IrFunctionType)_typeSubstitution.SubstituteType(fv.FunctionType)); RegisterResult(fv.Result, c.Result); return c; }
      case MaxonClosureCreateOp cc: {
        var specName = SpecializeClosureName(cc.FunctionName);
        var newFnType = (IrFunctionType)_typeSubstitution.SubstituteType(cc.FunctionType);
        var newCaptured = cc.CapturedValues.Select(MapValue).ToList();
        var newCapturedStructTypes = cc.CapturedStructTypes
          .Select(s => s == null ? null : SubName(s)).ToList();
        var c = new MaxonClosureCreateOp(
          specName,
          newFnType,
          newCaptured,
          [.. cc.CapturedNames],
          [.. cc.CapturedKinds],
          newCapturedStructTypes);
        RegisterResult(cc.Result, c.Result);
        return c;
      }

      default:
        return SubstitutingOpCloner.Clone(op, this);
    }
  }

  // --- The shared clone rule reads this pass's substitution through IOpSubstitution ---

  string IOpSubstitution.Mechanism => "Monomorphization";
  MaxonValue IOpSubstitution.MapValue(MaxonValue old) => MapValue(old);
  void IOpSubstitution.RegisterResult(MaxonValue oldResult, MaxonValue newResult) => RegisterResult(oldResult, newResult);
  string IOpSubstitution.SubstituteName(string name) => SubName(name);
  MaxonValueKind IOpSubstitution.SubstituteValueKind(MaxonValueKind kind, string? typeParamName) =>
    _typeSubstitution.SubstituteValueKind(kind, typeParamName);
  bool IOpSubstitution.TryGetBoundType(string typeParamName, out IrType boundType) =>
    _typeSubstitution.TryGetValue(typeParamName, out boundType);
  ManagedElementInfo IOpSubstitution.ResolveManagedElement(MaxonManagedMemGetOp op) =>
    _typeSubstitution.ResolveManagedElement(op);

  // --- Extracted handler methods for non-trivial cases ---

  private MaxonAssignOp CloneAssignOp(MaxonAssignOp assign) {
    var valueKind = assign.ValueKind;
    var mappedValue = MapValue(assign.Value);
    if (valueKind == MaxonValueKind.TypeParameter) {
      // Derive the kind from the mapped value itself — it was already resolved
      // correctly by the producing op (e.g., ResolveTypeParameterResult for calls).
      // Using SubstituteValueKind here would re-resolve through the wrong type param
      // (e.g., default "Element" → Entry tuple → Struct when the var actually holds a Key).
      valueKind = mappedValue switch {
        MaxonStruct => MaxonValueKind.Struct,
        MaxonEnum => MaxonValueKind.Enum,
        MaxonFloat => MaxonValueKind.Float,
        MaxonBool => MaxonValueKind.Bool,
        MaxonByte => MaxonValueKind.Byte,
        MaxonShort => MaxonValueKind.Short,
        MaxonInteger => MaxonValueKind.Integer,
        MaxonFunctionPtr => MaxonValueKind.Function,
        _ => throw new InvalidOperationException($"CloneAssignOp: unexpected mapped value type {mappedValue.GetType().Name}")
      };
      if (mappedValue is MaxonStruct assignedStruct) {
        _structVars.TryAdd(assign.VarName, assignedStruct.TypeName);
      }
      if (mappedValue is MaxonEnum assignedEnum) {
        _enumVars.TryAdd(assign.VarName, assignedEnum.TypeName);
      }
      // Record the resolved kind so CloneVarRefOp can use it instead of re-resolving
      _resolvedVarKinds[assign.VarName] = valueKind;
    }
    if (valueKind == MaxonValueKind.Float) {
      _floatVars.Add(assign.VarName);
    }
    if (valueKind == MaxonValueKind.Struct && mappedValue is MaxonStruct assignStruct) {
      _structVars.TryAdd(assign.VarName, assignStruct.TypeName);
    }
    if (valueKind == MaxonValueKind.Enum && mappedValue is MaxonEnum assignEnum) {
      _enumVars.TryAdd(assign.VarName, assignEnum.TypeName);
    }
    // `@heap` is the user's word, and StackPromotionAnalysisPass — which runs AFTER this clone —
    // is the only thing that reads it. Dropping it here let a specialization of a generic body put
    // on the stack what the source said must be on the heap; the interface-alias cloner has always
    // carried it, and a flag one cloner keeps and the other drops is the defect, not a detail.
    return new MaxonAssignOp(assign.VarName, mappedValue, assign.IsDeclaration, assign.IsMutable, valueKind) {
      OwnerFlags = assign.OwnerFlags,
      ForceHeap = assign.ForceHeap
    };
  }

  private MaxonOp CloneParamOp(MaxonParamOp param) {
    var paramTypeParam = GetVarTypeParam(param.Name);
    if (param.ValueKind == MaxonValueKind.TypeParameter && IsStructTypeParam(paramTypeParam)) {
      var structTypeName = GetStructTypeName(paramTypeParam!);
      if (structTypeName != null) {
        _structVars.TryAdd(param.Name, structTypeName);
        var cloned = new MaxonStructParamOp(param.Index, param.Name, structTypeName);
        RegisterResult(param.Result, cloned.Result);
        return cloned;
      }
    }
    if (param.ValueKind == MaxonValueKind.TypeParameter && IsEnumTypeParam(paramTypeParam)) {
      var enumTypeName = GetEnumTypeName(paramTypeParam!);
      if (enumTypeName != null) {
        _enumVars.TryAdd(param.Name, enumTypeName);
        var cloned = new MaxonEnumParamOp(param.Index, param.Name, enumTypeName, MaxonValueKind.Enum);
        RegisterResult(param.Result, cloned.Result);
        return cloned;
      }
    }
    var valueKind = _typeSubstitution.SubstituteValueKind(param.ValueKind, paramTypeParam);
    if (_substituteToFloat && IsElementPolymorphic(param.Index)) {
      valueKind = MaxonValueKind.Float;
    }
    if (valueKind == MaxonValueKind.Float) {
      _floatVars.Add(param.Name);
    }
    if (valueKind == MaxonValueKind.Struct) {
      // SubstituteValueKind resolved to a struct — promote to typed param
      var structTypeName = _typeSubstitution.TryGetValue(paramTypeParam ?? "Element", out var ct) ? ct.Name : null;
      if (structTypeName != null) {
        _structVars.TryAdd(param.Name, structTypeName);
        var structParam = new MaxonStructParamOp(param.Index, param.Name, structTypeName);
        RegisterResult(param.Result, structParam.Result);
        return structParam;
      }
    }
    if (valueKind == MaxonValueKind.Enum) {
      // SubstituteValueKind resolved to an associated-value enum — promote to typed param
      var enumTypeName = _typeSubstitution.TryGetValue(paramTypeParam ?? "Element", out var et) && et is IrEnumType ? et.Name : null;
      if (enumTypeName != null) {
        _enumVars.TryAdd(param.Name, enumTypeName);
        var enumParam = new MaxonEnumParamOp(param.Index, param.Name, enumTypeName, MaxonValueKind.Enum);
        RegisterResult(param.Result, enumParam.Result);
        return enumParam;
      }
    }
    var scalarParam = new MaxonParamOp(param.Index, param.Name, valueKind);
    RegisterResult(param.Result, scalarParam.Result);
    return scalarParam;
  }

  private MaxonOp CloneVarRefOp(MaxonVarRefOp varRef) {
    if (varRef.ValueKind == MaxonValueKind.TypeParameter
        && _structVars.TryGetValue(varRef.VarName, out var svTypeName)) {
      var cloned = new MaxonStructVarRefOp(varRef.VarName, svTypeName);
      RegisterResult(varRef.Result, cloned.Result);
      return cloned;
    }
    if (varRef.ValueKind == MaxonValueKind.TypeParameter
        && _enumVars.TryGetValue(varRef.VarName, out var evTypeName)) {
      var cloned = new MaxonEnumVarRefOp(varRef.VarName, evTypeName, MaxonValueKind.Enum);
      RegisterResult(varRef.Result, cloned.Result);
      return cloned;
    }
    var varTp = GetVarTypeParam(varRef.VarName);
    if (varRef.ValueKind == MaxonValueKind.TypeParameter && IsStructTypeParam(varTp)) {
      var structTypeName = GetStructTypeName(varTp!);
      if (structTypeName != null) {
        _structVars.TryAdd(varRef.VarName, structTypeName);
        var cloned = new MaxonStructVarRefOp(varRef.VarName, structTypeName);
        RegisterResult(varRef.Result, cloned.Result);
        return cloned;
      }
    }
    if (varRef.ValueKind == MaxonValueKind.TypeParameter && IsEnumTypeParam(varTp)) {
      var enumTypeName = GetEnumTypeName(varTp!);
      if (enumTypeName != null) {
        _enumVars.TryAdd(varRef.VarName, enumTypeName);
        var cloned = new MaxonEnumVarRefOp(varRef.VarName, enumTypeName, MaxonValueKind.Enum);
        RegisterResult(varRef.Result, cloned.Result);
        return cloned;
      }
    }
    // Use previously resolved kind from assignment when available — this avoids
    // re-resolving through SubstituteValueKind which may use wrong type param
    // (e.g., default "Element" maps to Entry tuple when the var holds a Key value)
    MaxonValueKind valueKind;
    if (varRef.ValueKind == MaxonValueKind.TypeParameter
        && _resolvedVarKinds.TryGetValue(varRef.VarName, out var resolvedKind)) {
      valueKind = resolvedKind;
    } else {
      valueKind = _typeSubstitution.SubstituteValueKind(varRef.ValueKind, varTp);
    }
    if (_substituteToFloat && _floatVars.Contains(varRef.VarName)) {
      valueKind = MaxonValueKind.Float;
    }
    if (valueKind == MaxonValueKind.Struct) {
      // SubstituteValueKind resolved to a struct type — must use typed variant
      var typeName = _typeSubstitution.TryGetValue(varTp ?? "Element", out var ct) ? ct.Name : null;
      if (typeName != null) {
        _structVars.TryAdd(varRef.VarName, typeName);
        var cloned = new MaxonStructVarRefOp(varRef.VarName, typeName);
        RegisterResult(varRef.Result, cloned.Result);
        return cloned;
      }
    }
    if (valueKind == MaxonValueKind.Enum) {
      // SubstituteValueKind resolved to an associated-value enum — must use typed variant
      var typeName = _typeSubstitution.TryGetValue(varTp ?? "Element", out var et) && et is IrEnumType ? et.Name : null;
      if (typeName != null) {
        _enumVars.TryAdd(varRef.VarName, typeName);
        var cloned = new MaxonEnumVarRefOp(varRef.VarName, typeName, MaxonValueKind.Enum);
        RegisterResult(varRef.Result, cloned.Result);
        return cloned;
      }
    }
    var scalarRef = new MaxonVarRefOp(varRef.VarName, valueKind);
    RegisterResult(varRef.Result, scalarRef.Result);
    return scalarRef;
  }

  /// For Eq/Ne where operands resolved to structs, convert to equals() method call.
  /// Check mappedLhs type because it carries the concrete struct type name needed for the call.
  private MaxonOp CloneBinOp(MaxonBinOp binOp, List<MaxonOp> extraOps) {
    var mappedLhs = MapValue(binOp.Lhs);
    var mappedRhs = MapValue(binOp.Rhs);
    if (binOp.Operator is MaxonBinOperator.Eq or MaxonBinOperator.Ne
        && mappedLhs is MaxonStruct lhsStruct
        && !_typeSubstitution.IsPrimitiveAlias(lhsStruct.TypeName)) {
      var equalsCallee = $"{lhsStruct.TypeName}.equals";
      var callOp = new MaxonCallOp(equalsCallee, [mappedLhs, mappedRhs], MaxonValueKind.Bool, null);
      if (binOp.Operator == MaxonBinOperator.Ne) {
        extraOps.Add(callOp);
        var trueOp = new MaxonLiteralOp(true);
        extraOps.Add(trueOp);
        var xorOp = new MaxonBinOp(MaxonBinOperator.BitXor, callOp.Result!, trueOp.Result, MaxonValueKind.Bool);
        RegisterResult(binOp.Result, xorOp.Result);
        return xorOp;
      }
      RegisterResult(binOp.Result, callOp.Result!);
      return callOp;
    }
    var operandKind = _typeSubstitution.SubstituteValueKind(binOp.OperandKind);
    if (_substituteToFloat && (mappedLhs is MaxonFloat || mappedRhs is MaxonFloat)) {
      operandKind = MaxonValueKind.Float;
    }
    var cloned = new MaxonBinOp(binOp.Operator, mappedLhs, mappedRhs, operandKind, binOp.OptimalType);
    RegisterResult(binOp.Result, cloned.Result);
    return cloned;
  }

  private MaxonCallOp CloneCallOp(MaxonCallOp call) {
    var newCallee = _typeSubstitution.SubstituteCallee(call.Callee);
    // SubstituteCallee de-aliases concrete specializations (e.g., Array_SmallInt → Array)
    // to help RewriteCallSites find the right method. But for inner typealiases of the type
    // being monomorphized (e.g., ElementArray -> Array_SmallInt), the concrete specialization
    // will have its own monomorphized methods. Use the concrete name directly when the callee's
    // type prefix was an inner alias that resolved to a concrete specialization.
    var calleeDotIdx = call.Callee.LastIndexOf('.');
    if (calleeDotIdx > 0) {
      var calleeTypePart = call.Callee[..calleeDotIdx];
      var calleeMethod = call.Callee[(calleeDotIdx + 1)..];
      if (_typeSubstitution.TryGetValue(calleeTypePart, out var concreteType)
          && concreteType is IrStructType concreteStruct
          && _typeAliasSources.TryGetValue(concreteStruct.Name, out var concreteAliasInfo)
          && concreteAliasInfo.TypeParams != null
          && concreteAliasInfo.TypeParams.Count > 0
          && concreteAliasInfo.TypeParams.Values.All(t => t is not IrTypeParameterType)
          && _typeSubstitution.IsSourceTypeMethod(calleeMethod)) {
        newCallee = $"{concreteStruct.Name}.{calleeMethod}";
      }
    }
    var newArgs = call.Args.Select(MapValue).ToList();
    var (resultKind, resultStructTypeName) = ResolveCallResultType(call.ResultKind, call.ResultStructTypeName, newArgs, call.Result);
    // Synthetic __ManagedMemory builtins keep the same concrete managed type as the source arg.
    // Mirror the cloning behavior from the old dedicated-op paths.
    if ((call.Callee is "__managed_mem_slice" or "__managed_mem_get" or "__managed_mem_remove")
        && resultStructTypeName == "__ManagedMemory"
        && _concreteElementType != null
        && IsHeapPtrForManagedMemory(_concreteElementType)) {
      resultStructTypeName = $"__ManagedMemory_{_concreteElementType.Name}";
    }

    // For get/remove returning struct elements the result type is the element type, not __ManagedMemory.
    // Re-concretize by substituting the element type parameter name.
    if (call.Callee is "__managed_mem_get" or "__managed_mem_remove"
        && resultStructTypeName != null
        && resultStructTypeName != "__ManagedMemory") {
      resultStructTypeName = SubName(resultStructTypeName);
    }

    var cloned = new MaxonCallOp(newCallee, newArgs, resultKind, resultStructTypeName) {
      ArgMutabilities = call.ArgMutabilities,
      ArgVarNames = call.ArgVarNames,
      CallLine = call.CallLine,
      CallColumn = call.CallColumn
    };
    if (call.Result != null && cloned.Result != null)
      RegisterResult(call.Result, cloned.Result);
    return cloned;
  }

  private static bool IsHeapPtrForManagedMemory(IrType t) =>
    t is IrStructType || t is IrEnumType { HasAssociatedValues: true };

  private MaxonTryCallOp CloneTryCallOp(MaxonTryCallOp tryCall) {
    var newCallee = _typeSubstitution.SubstituteCallee(tryCall.Callee);
    var newArgs = tryCall.Args.Select(MapValue).ToList();
    var (resultKind, resultStructTypeName) = ResolveCallResultType(tryCall.ResultKind, tryCall.ResultStructTypeName, newArgs);
    MaxonTryCallOp cloned;
    if (tryCall is MaxonManagedMemCreateTryCallOp createTryCall) {
      // Preserve compile-time element metadata across monomorphization.
      cloned = new MaxonManagedMemCreateTryCallOp(newArgs[0], createTryCall.ElementSize, createTryCall.IsBitPacked) {
        ResultStructTypeName = resultStructTypeName
      };
    } else if (tryCall is MaxonCheckedDivTryCallOp checkedDiv) {
      // Preserve the divide's mod/signedness metadata the lowering needs (the callee is fixed;
      // dropping the subtype would strand the divide as a plain try-call the lowering can't emit).
      cloned = new MaxonCheckedDivTryCallOp(newArgs[0], newArgs[1], checkedDiv.IsMod,
        checkedDiv.IsUnsigned, resultKind ?? tryCall.ResultKind!.Value, checkedDiv.ThrowsType!);
    } else {
      cloned = new MaxonTryCallOp(newCallee, newArgs, resultKind, resultStructTypeName);
    }
    cloned.ArgMutabilities = tryCall.ArgMutabilities;
    cloned.ArgVarNames = tryCall.ArgVarNames;
    cloned.CallLine = tryCall.CallLine;
    cloned.CallColumn = tryCall.CallColumn;
    if (tryCall.Result != null && cloned.Result != null)
      RegisterResult(tryCall.Result, cloned.Result);
    RegisterResult(tryCall.ErrorFlag, cloned.ErrorFlag);
    return cloned;
  }

  private (MaxonValueKind?, string?) ResolveCallResultType(
      MaxonValueKind? originalResultKind, string? originalStructTypeName, List<MaxonValue> newArgs,
      MaxonValue? originalResult = null) {
    // The call op's ResultStructTypeName can be the bare generic source type (e.g. "Array")
    // even when the call actually returns a distinct concrete alias of that source. A static
    // factory like `RunArrayStack.create()` (RunArrayStack = Array with SortRun) called inside a
    // method being specialized for a *different* alias (e.g. IntArray = Array with Integer) records
    // ResultStructTypeName="Array", which SubName would map to the wrong Self instantiation
    // ("IntArray") — silently retyping the result and routing later method calls on it to the wrong
    // monomorphization. The result MaxonStruct's own TypeName carries the correct concrete alias
    // ("RunArrayStack"), so prefer it whenever it is a distinct concrete alias that is NOT a type
    // parameter being substituted (those still route through originalStructTypeName/SubName).
    if (originalResult is MaxonStruct resultStruct
        && !string.IsNullOrEmpty(resultStruct.TypeName)
        && resultStruct.TypeName != originalStructTypeName
        && !_typeSubstitution.TryGetValue(resultStruct.TypeName, out _)
        && _typeAliasSources.TryGetValue(resultStruct.TypeName, out var resultAliasInfo)
        && resultAliasInfo.TypeParams != null
        && resultAliasInfo.TypeParams.Count > 0
        && resultAliasInfo.TypeParams.Values.All(t => t is not IrTypeParameterType)) {
      originalStructTypeName = resultStruct.TypeName;
    }

    var resultStructTypeName = originalStructTypeName != null ? SubName(originalStructTypeName) : null;
    var resultKind = originalResultKind.HasValue ? _typeSubstitution.SubstituteValueKind(originalResultKind.Value) : originalResultKind;
    if (resultKind == MaxonValueKind.Struct && resultStructTypeName == null && _concreteElementType is IrStructType st)
      resultStructTypeName = st.Name;
    if (resultKind == MaxonValueKind.Enum && resultStructTypeName == null && _concreteElementType is IrEnumType en)
      resultStructTypeName = en.Name;
    ResolveTypeParameterResult(originalResultKind, newArgs, ref resultKind, ref resultStructTypeName);
    return (resultKind, resultStructTypeName);
  }

  private MaxonIndirectCallOp CloneIndirectCallOp(MaxonIndirectCallOp indirectCall) {
    var newCallee = MapValue(indirectCall.Callee);
    var newArgs = indirectCall.Args.Select(MapValue).ToList();
    var resultKind = indirectCall.ResultKind.HasValue
      ? _typeSubstitution.SubstituteValueKind(indirectCall.ResultKind.Value)
      : (MaxonValueKind?)null;
    var newCalleeType = (IrFunctionType)_typeSubstitution.SubstituteType(indirectCall.CalleeType);
    var newResultStructTypeName = indirectCall.ResultStructTypeName != null ? SubName(indirectCall.ResultStructTypeName) : null;
    var cloned = new MaxonIndirectCallOp(newCallee, newCalleeType, newArgs, resultKind, newResultStructTypeName);
    if (indirectCall.Result != null && cloned.Result != null)
      RegisterResult(indirectCall.Result, cloned.Result);
    return cloned;
  }

  private MaxonStructLiteralOp CloneStructLiteralOp(MaxonStructLiteralOp structLit, List<MaxonOp> extraOps, MaxonStructLiteralOp? nextWrapperStructLit = null) {
    var newFieldValues = structLit.FieldValues.Select(fv => (fv.FieldName, MapValue(fv.Value))).ToList();

    // For __ManagedMemory structs, substitute element_size based on the Element type substitution.
    // Bit-packed bool arrays use element_size=0 as a sentinel.
    if (IsManagedMemoryType(structLit.TypeName) && _concreteElementType != null) {
      for (int i = 0; i < newFieldValues.Count; i++) {
        if (newFieldValues[i].FieldName == "element_size") {
          int elementSize = _concreteElementType.ManagedMemoryElementSize;
          var elementSizeLitOp = new MaxonLiteralOp((long)elementSize);
          extraOps.Add(elementSizeLitOp);
          newFieldValues[i] = ("element_size", elementSizeLitOp.Result);
          break;
        }
      }
    }

    // Resolve the type name. When the substitution map doesn't resolve it (ambiguous
    // or missing mapping), derive the correct concrete type from the following wrapper
    // struct's field definitions. This handles cases like Map where KeyArray and ValueArray
    // both have an ElementMemory field that resolves to different concrete types.
    var resolvedTypeName = SubName(structLit.TypeName);

    // Tuple type names encode field types (e.g., __Tuple2-i64-i64) which aren't map keys.
    // When a generic function returns a tuple with type-parameter-derived fields,
    // the parser creates the tuple using runtime representations (all i64), but
    // the function's return type is correctly substituted. Use it for correction.
    if (resolvedTypeName == structLit.TypeName
        && IrType.IsJoinedTypeName(structLit.TypeName, IrStructType.TupleTypeNamePrefix)
        && _resolvedReturnType is IrStructType retTuple && retTuple.IsTuple
        && retTuple.Fields.Count == structLit.FieldValues.Count
        && retTuple.Name != structLit.TypeName) {
      resolvedTypeName = retTuple.Name;
      // Register the tuple type definition if not already present
      if (!_typeDefs.ContainsKey(resolvedTypeName)) {
        _typeDefs[resolvedTypeName] = retTuple;
      }
    }

    if (resolvedTypeName == structLit.TypeName && nextWrapperStructLit != null) {
      var wrapperTypeName = SubName(nextWrapperStructLit.TypeName);
      if (_typeDefs.TryGetValue(wrapperTypeName, out var wrapperDef)
          && wrapperDef is IrStructType wrapperStruct) {
        // Find the field in the source type that corresponds to this struct literal
        var wrapperSourceName = _typeAliasSources.TryGetValue(wrapperTypeName, out var wai)
          ? wai.SourceTypeName : wrapperTypeName;
        if (_typeDefs.TryGetValue(wrapperSourceName, out var wrapperSourceDef)
            && wrapperSourceDef is IrStructType wrapperSourceStruct) {
          for (int fi = 0; fi < wrapperSourceStruct.Fields.Count && fi < wrapperStruct.Fields.Count; fi++) {
            if (wrapperSourceStruct.Fields[fi].Type.Name == structLit.TypeName) {
              resolvedTypeName = wrapperStruct.Fields[fi].Type.Name;
              break;
            }
          }
        }
      }
    }

    // For fixed-capacity types (Vector): when the source had no ArrayLiteralTag
    // (generic Self{} at parse time), but the resolved type has __capacity,
    // set up the capacity info so the conversion pass allocates the buffer.
    var arrayTag = structLit.ArrayLiteralTag;
    var arrayCount = structLit.ArrayLiteralCount;
    // Check if the managed field was zero-initialized (from Self{}) vs user-provided (from Self{managed: param}).
    // Only inject capacity handling for zero-init: the managed value is a MaxonStruct from a struct literal,
    // not from a function parameter.
    bool managedIsZeroInit = false;
    var (FieldName, Value) = structLit.FieldValues.FirstOrDefault(fv => fv.FieldName == "managed");
    if (FieldName == "managed" && Value is MaxonStruct) {
      // Check if this value was produced by a struct literal op in the same block
      // (zero-init), not from a function parameter
      managedIsZeroInit = !_sourceFunc.ParamNames.Contains("managed");
    }
    if (arrayTag == null && managedIsZeroInit
        && _typeDefs.TryGetValue(resolvedTypeName, out var resolvedDef)
        && resolvedDef is IrStructType resolvedStruct
        && resolvedStruct.ConstParams.TryGetValue(IrStructType.CapacityConstParamName, out var capacity)
        && resolvedStruct.GetField("managed") != null) {
      // Determine element size from the concrete type
      int elemSize = 8;
      if (resolvedStruct.TypeParams.TryGetValue("Element", out var elemType))
        elemSize = elemType.ManagedMemoryElementSize;
      var elemKind = elemType?.ToValueKind() ?? MaxonValueKind.Integer;

      arrayTag = $"__arr_{IrContext.Current.NextId()}";
      arrayCount = (int)capacity;

      // Create zero-valued element variables
      for (int i = arrayCount - 1; i >= 0; i--) {
        var zeroVal = new MaxonLiteralOp(0L);
        extraOps.Add(zeroVal);
        var elemVarName = $"{arrayTag}.{i}";
        extraOps.Add(new MaxonAssignOp(elemVarName, zeroVal.Result, isDeclaration: true, isMutable: false, elemKind));
      }

      // Create __ManagedMemory struct with placeholder buffer
      var bufLit = new MaxonLiteralOp(0L);
      extraOps.Add(bufLit);
      var lenLit = new MaxonLiteralOp(capacity);
      extraOps.Add(lenLit);
      var capLit = new MaxonLiteralOp(0L);
      extraOps.Add(capLit);
      var elemSizeLit = new MaxonLiteralOp((long)elemSize);
      extraOps.Add(elemSizeLit);

      var parentPtrLit = new MaxonLiteralOp(0L);
      extraOps.Add(parentPtrLit);
      var managedFields = new List<(string, MaxonValue)> {
        ("buffer", bufLit.Result),
        ("length", lenLit.Result),
        ("capacity", capLit.Result),
        ("element_size", elemSizeLit.Result),
        ("parent_ptr", parentPtrLit.Result)
      };
      var managedTypeName = resolvedStruct.GetField("managed")!.Type.Name;
      // A bool element bit-packs with element_size == 0 as the sentinel (see
      // ManagedMemoryElementSize). Flag the synthesized managed struct bit-packed so
      // the conversion skips the element_size==0 runtime guard — mirrors the parser's
      // Self{}+__capacity path. Without it a sized bool Vector.create() panics at
      // runtime ("element_size must be > 0").
      var managedStruct = new MaxonStructLiteralOp(managedTypeName, managedFields) { IsBitPacked = elemSize == 0 };
      extraOps.Add(managedStruct);
      // Replace the existing zero-initialized managed field with the capacity-aware one
      var existingIdx = newFieldValues.FindIndex(fv => fv.FieldName == "managed");
      if (existingIdx >= 0)
        newFieldValues[existingIdx] = ("managed", managedStruct.Result);
      else
        newFieldValues.Add(("managed", managedStruct.Result));
    }

    var cloned = new MaxonStructLiteralOp(resolvedTypeName, newFieldValues) {
      ArrayLiteralTag = arrayTag,
      ArrayLiteralCount = arrayCount,
      IsBitPacked = structLit.IsBitPacked || _isBitPackedElement
    };
    RegisterResult(structLit.Result, cloned.Result);
    return cloned;
  }

  /// When a call returns TypeParameter and the self arg is a concrete inner alias,
  /// resolve through the inner alias's Element type param to get the correct result kind.
  /// This handles cases like Array<Color>.get() where the outer type's Element is Entry
  /// but the inner array's Element is Color.
  private void ResolveTypeParameterResult(
      MaxonValueKind? originalKind, List<MaxonValue> newArgs,
      ref MaxonValueKind? resultKind, ref string? resultStructTypeName) {
    if (originalKind != MaxonValueKind.TypeParameter) return;
    if (newArgs.Count == 0) return;
    if (newArgs[0] is not MaxonStruct selfStruct) return;

    foreach (var (key, concreteType) in _typeSubstitution.Entries) {
      if (concreteType is IrStructType st && st.Name == selfStruct.TypeName) {
        if (st.TypeParams != null && st.TypeParams.TryGetValue("Element", out var elemType)) {
          if (elemType is IrStructType elemStruct) {
            resultKind = MaxonValueKind.Struct;
            resultStructTypeName = elemStruct.Name;
          } else if (elemType is IrEnumType elemEnum && elemEnum.HasAssociatedValues) {
            resultKind = MaxonValueKind.Enum;
            resultStructTypeName = elemEnum.Name;
          } else if (elemType is IrEnumType) {
            // Simple enum without associated values — treated as integer
            resultKind = MaxonValueKind.Integer;
            resultStructTypeName = null;
          } else {
            // Primitive type — resolve to its value kind
            resultKind = elemType.ToValueKind();
            resultStructTypeName = null;
          }
        }
        break;
      }
    }
  }

  // --- Post-processing ---

  /// Fix __ManagedMemory element_size for multi-parameter generic types.
  private void PatchManagedMemoryElementSizes(IrFunction<MaxonOp> func) {
    foreach (var block in func.Body.Blocks) {
      var managedMemOps = new Dictionary<int, (MaxonStructLiteralOp Op, int BlockIndex)>();
      for (int i = 0; i < block.Operations.Count; i++) {
        if (block.Operations[i] is MaxonStructLiteralOp mmOp && IsManagedMemoryType(mmOp.TypeName)) {
          managedMemOps[mmOp.Result.Id] = (mmOp, i);
        }
      }

      if (managedMemOps.Count == 0) continue;

      for (int i = 0; i < block.Operations.Count; i++) {
        if (block.Operations[i] is not MaxonStructLiteralOp wrapperOp) continue;
        if (IsManagedMemoryType(wrapperOp.TypeName)) continue;

        foreach (var (fieldName, fieldVal) in wrapperOp.FieldValues) {
          if (fieldName != "managed") continue;
          if (!managedMemOps.TryGetValue(fieldVal.Id, out var mmInfo)) continue;

          int? elemSize = GetElementSizeFromResolvedAlias(wrapperOp.TypeName);
          if (elemSize == null || elemSize == 0) continue;

          var mmOp = mmInfo.Op;
          for (int fi = 0; fi < mmOp.FieldValues.Count; fi++) {
            if (mmOp.FieldValues[fi].FieldName != "element_size") continue;
            var newLit = new MaxonLiteralOp((long)elemSize.Value);
            block.Operations.Insert(mmInfo.BlockIndex, newLit);
            mmOp.FieldValues[fi] = ("element_size", newLit.Result);
            foreach (var key in managedMemOps.Keys.ToList()) {
              var (Op, BlockIndex) = managedMemOps[key];
              if (BlockIndex >= mmInfo.BlockIndex)
                managedMemOps[key] = (Op, BlockIndex + 1);
            }
            i++;
            break;
          }
        }
      }
    }
  }

  private int? GetElementSizeFromResolvedAlias(string typeName) {
    foreach (var (_, concreteType) in _typeSubstitution.Entries) {
      if (concreteType is IrStructType st && st.Name == typeName) {
        if (st.TypeParams != null && st.TypeParams.TryGetValue("Element", out var elemType) && elemType is not IrTypeParameterType) {
          return elemType.ManagedMemoryElementSize;
        }
      }
    }
    return null;
  }
}
