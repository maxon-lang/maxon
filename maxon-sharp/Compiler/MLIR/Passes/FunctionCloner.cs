using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Clones a generic function with type substitutions applied, producing
/// a monomorphized specialization for a concrete type alias.
/// One instance per specialization; holds all mutable cloning state.
/// </summary>
internal class FunctionCloner {
  private readonly IrFunction<MaxonOp> _sourceFunc;
  private readonly string _concreteTypeName;
  private readonly TypeSubstitution _typeSubstitution;
  private readonly Dictionary<string, TypeAliasInfo> _typeAliasSources;
  private readonly Dictionary<string, IrType> _typeDefs;

  // Resolved return type after substitution (used for tuple name correction)
  private IrType? _resolvedReturnType;

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
      Dictionary<string, IrType> typeDefs) {
    _sourceFunc = sourceFunc;
    _concreteTypeName = concreteTypeName;
    _typeSubstitution = typeSubstitution;
    _typeAliasSources = typeAliasSources;
    _typeDefs = typeDefs;
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
    // Compute new function name
    // Use LastIndexOf to handle namespace-qualified names like "stdlib.Array.push"
    var dotIdx = _sourceFunc.Name.LastIndexOf('.');
    var methodName = dotIdx >= 0 ? _sourceFunc.Name[(dotIdx + 1)..] : _sourceFunc.Name;
    var newFuncName = $"{_concreteTypeName}.{methodName}";

    if (_concreteElementType != null) {
      Logger.Debug(LogCategory.Ir, $"  Element type substitution: Element -> {_concreteElementType.Name}");
    } else {
      Logger.Debug(LogCategory.Ir, $"  No Element type in substitution. TypeSubstitution keys: [{string.Join(", ", _typeSubstitution.Keys)}]");
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
      ReturnsSelf = _sourceFunc.ReturnsSelf
    };

    Logger.Debug(LogCategory.Ir, $"  Monomorphized func: {newFuncName}({string.Join(", ", newParamTypes)}) -> {newReturnType}");

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

        foreach (var extra in extraOps) {
          newBlock.AddOp(extra);
        }
        newBlock.AddOp(clonedOp);
      }
    }

    // Post-processing: fix __ManagedMemory element_size for multi-parameter generic types
    // where a single Element substitution doesn't apply uniformly.
    if (_concreteElementType == null && _typeSubstitution.Count > 2) {
      PatchManagedMemoryElementSizes(newFunc);
    }

    return newFunc;
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
      MaxonFunctionPtr => new MaxonFunctionPtr(newId),
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

  // --- Op cloning ---

  private MaxonOp CloneOp(MaxonOp op, List<MaxonOp> extraOps) {
    switch (op) {
      // Non-trivial cases: extracted to handler methods
      case MaxonAssignOp assign: return CloneAssignOp(assign);
      case MaxonParamOp param: return CloneParamOp(param);
      case MaxonVarRefOp varRef: return CloneVarRefOp(varRef);
      case MaxonBinOp binOp: return CloneBinOp(binOp, extraOps);
      case MaxonIteratorNextOp iterNext: return CloneIteratorNextOp(iterNext);
      case MaxonTryCallOp tryCall: return CloneTryCallOp(tryCall);
      case MaxonCallOp call: return CloneCallOp(call);
      case MaxonIndirectCallOp indirect: return CloneIndirectCallOp(indirect);
      case MaxonStructLiteralOp structLit: return CloneStructLiteralOp(structLit, extraOps);
      case MaxonManagedMemGetOp memGet: return CloneManagedMemGetOp(memGet);
      case MaxonManagedMemRemoveOp memRemove: return CloneManagedMemRemoveOp(memRemove);
      case MaxonManagedMemSetOp memSet: return CloneManagedMemSetOp(memSet);

      // Literal
      case MaxonLiteralOp lit: {
        var cloned = lit.ValueKind switch {
          MaxonValueKind.Integer => new MaxonLiteralOp(lit.IntValue),
          MaxonValueKind.Float => new MaxonLiteralOp(lit.FloatValue),
          MaxonValueKind.Float32 => new MaxonLiteralOp(lit.FloatValue, MaxonValueKind.Float32),
          MaxonValueKind.Bool => new MaxonLiteralOp(lit.BoolValue),
          _ => throw new InvalidOperationException($"Unsupported literal kind: {lit.ValueKind}")
        };
        RegisterResult(lit.Result, cloned.Result);
        return cloned;
      }

      // Type name substitution
      case MaxonStructParamOp sp: { var c = new MaxonStructParamOp(sp.Index, sp.Name, SubName(sp.StructTypeName)); RegisterResult(sp.Result, c.Result); return c; }
      case MaxonStructVarRefOp sv: { var c = new MaxonStructVarRefOp(sv.VarName, SubName(sv.StructTypeName)); RegisterResult(sv.Result, c.Result); return c; }
      case MaxonFieldAccessOp fa: { var c = new MaxonFieldAccessOp(MapValue(fa.StructValue), SubName(fa.TypeName), fa.FieldName, fa.ResultKind, fa.ResultStructTypeName != null ? SubName(fa.ResultStructTypeName) : null); RegisterResult(fa.Result, c.Result); return c; }
      case MaxonFieldAssignOp fa: return new MaxonFieldAssignOp(MapValue(fa.StructValue), SubName(fa.TypeName), fa.FieldName, MapValue(fa.NewValue));

      // Control flow
      case MaxonCondBrOp cb: return new MaxonCondBrOp(MapValue(cb.Condition), cb.ThenBlock, cb.ElseBlock);
      case MaxonBrOp br: return new MaxonBrOp(br.Target);
      case MaxonReturnOp ret: return new MaxonReturnOp(ret.Value != null ? MapValue(ret.Value) : null, ret.IsErrorPropagation);
      case MaxonThrowOp th: return new MaxonThrowOp(MapValue(th.ErrorValue), th.ErrorTypeName);
      case MaxonPanicOp p: return new MaxonPanicOp(p.Message);
      case MaxonPanicDynamicOp pd: return new MaxonPanicDynamicOp((MaxonStruct)MapValue(pd.MessageStruct));
      case MaxonRefEqOp req: { var c = new MaxonRefEqOp(MapValue(req.Lhs), MapValue(req.Rhs), req.Negate); RegisterResult(req.Result, c.Result); return c; }
      // Variable names reference, not values — copy as-is
      case MaxonScopeEndOp se: return new MaxonScopeEndOp(se.VarsToClean, se.KeepVars);

      // Unary math
      case MaxonTruncOp t: { var c = new MaxonTruncOp(MapValue(t.Input)); RegisterResult(t.Result, c.Result); return c; }
      case MaxonIntToFloatOp i: { var c = new MaxonIntToFloatOp(MapValue(i.Input)); RegisterResult(i.Result, c.Result); return c; }
      case MaxonCastOp ca: { var c = new MaxonCastOp(MapValue(ca.Input), ca.TargetKind, ca.SourceOptimalType); RegisterResult(ca.Result, c.Result); return c; }
      case MaxonBitcastF64ToI64Op bc: { var c = new MaxonBitcastF64ToI64Op(MapValue(bc.Input)); RegisterResult(bc.Result, c.Result); return c; }
      case MaxonSizeofOp sz: { var c = new MaxonSizeofOp(SubName(sz.TypeName)); RegisterResult(sz.Result, c.Result); return c; }
      case MaxonAbsOp a: { var c = new MaxonAbsOp(MapValue(a.Input)); RegisterResult(a.Result, c.Result); return c; }
      case MaxonSqrtOp s: { var c = new MaxonSqrtOp(MapValue(s.Input)); RegisterResult(s.Result, c.Result); return c; }
      case MaxonFloorOp f: { var c = new MaxonFloorOp(MapValue(f.Input)); RegisterResult(f.Result, c.Result); return c; }
      case MaxonCeilOp ce: { var c = new MaxonCeilOp(MapValue(ce.Input)); RegisterResult(ce.Result, c.Result); return c; }
      case MaxonRoundOp r: { var c = new MaxonRoundOp(MapValue(r.Input)); RegisterResult(r.Result, c.Result); return c; }
      case MaxonMinOp mi: { var c = new MaxonMinOp(MapValue(mi.Lhs), MapValue(mi.Rhs)); RegisterResult(mi.Result, c.Result); return c; }
      case MaxonMaxOp ma: { var c = new MaxonMaxOp(MapValue(ma.Lhs), MapValue(ma.Rhs)); RegisterResult(ma.Result, c.Result); return c; }

      // Enum ops
      case MaxonEnumLiteralOp el: { var c = el.BackingKind is MaxonValueKind.Float or MaxonValueKind.Float32 ? new MaxonEnumLiteralOp(SubName(el.EnumTypeName), el.CaseName, el.FloatValue) : new MaxonEnumLiteralOp(SubName(el.EnumTypeName), el.CaseName, el.IntValue); RegisterResult(el.Result, c.Result); return c; }
      case MaxonEnumParamOp ep: { var c = new MaxonEnumParamOp(ep.Index, ep.Name, SubName(ep.EnumTypeName), ep.BackingKind); RegisterResult(ep.Result, c.Result); return c; }
      case MaxonEnumVarRefOp ev: { var c = new MaxonEnumVarRefOp(ev.VarName, SubName(ev.EnumTypeName), ev.BackingKind); RegisterResult(ev.Result, c.Result); return c; }
      case MaxonEnumRawValueOp er: { var c = new MaxonEnumRawValueOp(MapValue(er.EnumValue), SubName(er.EnumTypeName), er.ResultKind); RegisterResult(er.Result, c.Result); return c; }
      case MaxonEnumOrdinalOp eo: { var c = new MaxonEnumOrdinalOp(MapValue(eo.EnumValue), SubName(eo.EnumTypeName)); RegisterResult(eo.Result, c.Result); return c; }
      case MaxonEnumNameOp en: { var c = new MaxonEnumNameOp(MapValue(en.EnumValue), SubName(en.EnumTypeName)); RegisterResult(en.Result, c.Result); return c; }
      case MaxonEnumStringRawValueOp esr: { var c = new MaxonEnumStringRawValueOp(MapValue(esr.EnumValue), SubName(esr.EnumTypeName), esr.IsChar); RegisterResult(esr.Result, c.Result); return c; }
      case MaxonEnumStructRawValueOp esrv: { var c = new MaxonEnumStructRawValueOp(MapValue(esrv.EnumValue), SubName(esrv.EnumTypeName), esrv.StructTypeName); RegisterResult(esrv.Result, c.Result); return c; }
      case MaxonErrorFlagToEnumOp ef: { var c = new MaxonErrorFlagToEnumOp(MapValue(ef.ErrorFlag), SubName(ef.EnumTypeName), ef.BackingKind, ef.HasAssociatedValues); RegisterResult(ef.Result, c.Result); return c; }
      case MaxonEnumConstructOp ec: { var c = new MaxonEnumConstructOp(SubName(ec.EnumTypeName), ec.CaseName, ec.TagValue, [.. ec.Args.Select(MapValue)]); RegisterResult(ec.Result, c.Result); return c; }
      case MaxonEnumTagOp et: { var c = new MaxonEnumTagOp(MapValue(et.EnumValue), SubName(et.EnumTypeName)); RegisterResult(et.Result, c.Result); return c; }
      case MaxonEnumPayloadAssignOp epa: return new MaxonEnumPayloadAssignOp(epa.EnumVarName, SubName(epa.EnumTypeName), epa.PayloadIndex, MapValue(epa.NewValue));
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

      // Global ops
      case MaxonGlobalLoadOp gl: { var c = new MaxonGlobalLoadOp(gl.GlobalName, gl.ValueKind); RegisterResult(gl.Result, c.Result); return c; }
      case MaxonGlobalStoreOp gs: return new MaxonGlobalStoreOp(gs.GlobalName, MapValue(gs.Value), gs.ValueKind);

      // Managed memory ops (trivial)
      case MaxonManagedMemByteGetOp bg: { var c = new MaxonManagedMemByteGetOp(MapValue(bg.ManagedStruct), MapValue(bg.Index)); RegisterResult(bg.Result, c.Result); return c; }
      case MaxonUcdByteLoadOp ucdByte: { var c = new MaxonUcdByteLoadOp(ucdByte.UcddataLabel, MapValue(ucdByte.ByteOffset)); RegisterResult(ucdByte.Result, c.Result); return c; }
      case MaxonUcdI64LoadOp ucdI64: { var c = new MaxonUcdI64LoadOp(ucdI64.UcddataLabel, MapValue(ucdI64.Index)); RegisterResult(ucdI64.Result, c.Result); return c; }
      case MaxonManagedMemByteSetOp bs: return new MaxonManagedMemByteSetOp(MapValue(bs.ManagedStruct), MapValue(bs.Index), MapValue(bs.Value));
      case MaxonManagedMemCreateOp mc: { var c = new MaxonManagedMemCreateOp(MapValue(mc.Count), mc.ElementSize) { IsBitPacked = mc.IsBitPacked || _isBitPackedElement }; RegisterResult(mc.Result, c.Result); return c; }
      case MaxonManagedMemGrowOp mg: return new MaxonManagedMemGrowOp(MapValue(mg.ManagedStruct), MapValue(mg.NewCapacity)) { IsBitPacked = mg.IsBitPacked || _isBitPackedElement };
      case MaxonManagedMemSetLengthOp sl: return new MaxonManagedMemSetLengthOp(MapValue(sl.ManagedStruct), MapValue(sl.NewLength));
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
      case MaxonManagedMemShiftOp ms: return new MaxonManagedMemShiftOp(MapValue(ms.ManagedStruct), MapValue(ms.Index), MapValue(ms.Count), ms.ShiftRight) { IsBitPacked = ms.IsBitPacked || _isBitPackedElement };
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
      case MaxonManagedMemSliceOp sl: {
        var isHeapPtrElem = sl.IsStructElement;
        if (sl.TypeParamName != null && _typeSubstitution.TryGetValue(sl.TypeParamName, out var sliceElemType))
          isHeapPtrElem = sliceElemType is IrStructType || sliceElemType is IrEnumType { HasAssociatedValues: true };
        var c = new MaxonManagedMemSliceOp(MapValue(sl.Managed), MapValue(sl.Start), MapValue(sl.End)) {
          IsStructElement = isHeapPtrElem,
          TypeParamName = sl.TypeParamName,
          IsBitPacked = sl.IsBitPacked || _isBitPackedElement
        };
        if (isHeapPtrElem && IsManagedMemoryType(sl.Result.TypeName) && _concreteElementType != null)
          c.Result.TypeName = $"__ManagedMemory_{_concreteElementType.Name}";
        RegisterResult(sl.Result, c.Result);
        return c;
      }

      // Runtime and function ops
      case MaxonCallRuntimeOp cr: { var na = cr.Args.Select(MapValue).ToList(); var c = new MaxonCallRuntimeOp(cr.FunctionName, na, cr.Result != null); if (cr.Result != null && c.Result != null) RegisterResult(cr.Result, c.Result); return c; }
      case MaxonFunctionParamOp fp: { var c = new MaxonFunctionParamOp(fp.Index, fp.Name, fp.FunctionType); RegisterResult(fp.Result, c.Result); return c; }
      case MaxonFunctionRefOp fr: { var c = new MaxonFunctionRefOp(fr.FunctionName, fr.FunctionType); RegisterResult(fr.Result, c.Result); return c; }
      case MaxonFunctionVarRefOp fv: { var c = new MaxonFunctionVarRefOp(fv.VarName, (IrFunctionType)_typeSubstitution.SubstituteType(fv.FunctionType)); RegisterResult(fv.Result, c.Result); return c; }

      // ManagedList (doubly-linked list) ops
      case MaxonManagedListCreateOp: { var c = new MaxonManagedListCreateOp(); RegisterResult(((MaxonManagedListCreateOp)op).Result, c.Result); return c; }
      case MaxonManagedListInsertValueOp ci: { var c = new MaxonManagedListInsertValueOp(MapValue(ci.ManagedList), MapValue(ci.Value), ci.AtHead, SubName(ci.ValueKind)); RegisterResult(ci.Result, c.Result); return c; }
      case MaxonManagedListInsertRelativeValueOp cir: { var c = new MaxonManagedListInsertRelativeValueOp(MapValue(cir.ManagedList), MapValue(cir.Target), MapValue(cir.Value), cir.After, SubName(cir.ValueKind)); RegisterResult(cir.Result, c.Result); return c; }
      case MaxonManagedListReinsertOp cr: return new MaxonManagedListReinsertOp(MapValue(cr.ManagedList), MapValue(cr.Node), cr.AtHead);
      case MaxonManagedListReinsertRelativeOp crr: return new MaxonManagedListReinsertRelativeOp(MapValue(crr.ManagedList), MapValue(crr.Target), MapValue(crr.Node), crr.After);
      case MaxonManagedListDetachOp cd: return new MaxonManagedListDetachOp(MapValue(cd.ManagedList), MapValue(cd.Node));
      case MaxonManagedListRemoveOp crm: {
        var newRK = _typeSubstitution.TryGetValue(crm.ValueKind, out var rvt) ? rvt.ToValueKind() : crm.ResultKind;
        var c = new MaxonManagedListRemoveOp(MapValue(crm.ManagedList), MapValue(crm.Node), SubName(crm.ValueKind), newRK);
        RegisterResult(crm.Result, c.Result); return c;
      }
      case MaxonManagedListCountOp cc: { var c = new MaxonManagedListCountOp(MapValue(cc.ManagedList)); RegisterResult(cc.Result, c.Result); return c; }
      case MaxonManagedListNodeValueOp cnv: {
        var newRK = _typeSubstitution.TryGetValue(cnv.ValueKind, out var nvt) ? nvt.ToValueKind() : cnv.ResultKind;
        var c = new MaxonManagedListNodeValueOp(MapValue(cnv.Node), SubName(cnv.ValueKind), newRK);
        RegisterResult(cnv.Result, c.Result); return c;
      }
      case MaxonManagedListNodeSetValueOp cns: return new MaxonManagedListNodeSetValueOp(MapValue(cns.Node), MapValue(cns.Value), SubName(cns.ValueKind));
      case MaxonManagedListClearOp ccl: return new MaxonManagedListClearOp(MapValue(ccl.ManagedList), SubName(ccl.ValueKind));
      case MaxonManagedListCursorResetOp ccr: return new MaxonManagedListCursorResetOp(MapValue(ccr.ManagedList));
      case MaxonManagedListCursorValueOp ccv: {
        var newRK = _typeSubstitution.TryGetValue(ccv.ValueKind, out var cvt) ? cvt.ToValueKind() : ccv.ResultKind;
        var c = new MaxonManagedListCursorValueOp(MapValue(ccv.ManagedList), SubName(ccv.ValueKind), newRK);
        RegisterResult(ccv.Result, c.Result); return c;
      }
      case MaxonManagedListHeadPtrOp chp: { var c = new MaxonManagedListHeadPtrOp(MapValue(chp.ManagedList)); RegisterResult(chp.Result, c.Result); return c; }
      case MaxonManagedListNodePtrNextOp cpn: { var c = new MaxonManagedListNodePtrNextOp(MapValue(cpn.CursorPtr)); RegisterResult(cpn.Result, c.Result); return c; }
      case MaxonManagedListNodePtrValueOp cpv: {
        var newRK = _typeSubstitution.TryGetValue(cpv.ValueKind, out var pvt) ? pvt.ToValueKind() : cpv.ResultKind;
        var c = new MaxonManagedListNodePtrValueOp(MapValue(cpv.CursorPtr), SubName(cpv.ValueKind), newRK);
        RegisterResult(cpv.Result, c.Result); return c;
      }

      default:
        throw new InvalidOperationException($"Monomorphization: unhandled op type {op.GetType().Name}");
    }
  }

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
    var cloned = new MaxonAssignOp(assign.VarName, mappedValue, assign.IsDeclaration, assign.IsMutable, valueKind) {
      OwnerFlags = assign.OwnerFlags
    };
    return cloned;
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
    var newArgs = call.Args.Select(MapValue).ToList();
    var (resultKind, resultStructTypeName) = ResolveCallResultType(call.ResultKind, call.ResultStructTypeName, newArgs);
    var cloned = new MaxonCallOp(newCallee, newArgs, resultKind, resultStructTypeName) { ArgMutabilities = call.ArgMutabilities, ArgVarNames = call.ArgVarNames, CallLine = call.CallLine, CallColumn = call.CallColumn };
    if (call.Result != null && cloned.Result != null)
      RegisterResult(call.Result, cloned.Result);
    return cloned;
  }

  private MaxonIteratorNextOp CloneIteratorNextOp(MaxonIteratorNextOp iterNext) {
    var newIterableType = SubName(iterNext.IterableTypeName);
    var newIteratorAlias = SubName(iterNext.IteratorAliasName);
    var newArgs = iterNext.Args.Select(MapValue).ToList();
    var elemStructType = iterNext.ElementStructTypeName != null ? SubName(iterNext.ElementStructTypeName) : null;
    var elemKind = iterNext.ElementKind.HasValue ? _typeSubstitution.SubstituteValueKind(iterNext.ElementKind.Value) : iterNext.ElementKind;
    var cloned = new MaxonIteratorNextOp(newIterableType, newIteratorAlias, newArgs, elemKind, elemStructType);
    if (iterNext.Result != null && cloned.Result != null)
      RegisterResult(iterNext.Result, cloned.Result);
    RegisterResult(iterNext.ErrorFlag, cloned.ErrorFlag);
    return cloned;
  }

  private MaxonTryCallOp CloneTryCallOp(MaxonTryCallOp tryCall) {
    var newCallee = _typeSubstitution.SubstituteCallee(tryCall.Callee);
    var newArgs = tryCall.Args.Select(MapValue).ToList();
    var (resultKind, resultStructTypeName) = ResolveCallResultType(tryCall.ResultKind, tryCall.ResultStructTypeName, newArgs);
    var cloned = new MaxonTryCallOp(newCallee, newArgs, resultKind, resultStructTypeName) { ArgMutabilities = tryCall.ArgMutabilities, ArgVarNames = tryCall.ArgVarNames, CallLine = tryCall.CallLine, CallColumn = tryCall.CallColumn };
    if (tryCall.Result != null && cloned.Result != null)
      RegisterResult(tryCall.Result, cloned.Result);
    RegisterResult(tryCall.ErrorFlag, cloned.ErrorFlag);
    return cloned;
  }

  private (MaxonValueKind?, string?) ResolveCallResultType(
      MaxonValueKind? originalResultKind, string? originalStructTypeName, List<MaxonValue> newArgs) {
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

    // Tuple type names encode field types (e.g., __Tuple_i64_i64) which aren't map keys.
    // When a generic function returns a tuple with type-parameter-derived fields,
    // the parser creates the tuple using runtime representations (all i64), but
    // the function's return type is correctly substituted. Use it for correction.
    if (resolvedTypeName == structLit.TypeName
        && structLit.TypeName.StartsWith("__Tuple_")
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
        && resolvedStruct.ConstParams.TryGetValue("__capacity", out var capacity)
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

      var managedFields = new List<(string, MaxonValue)> {
        ("buffer", bufLit.Result),
        ("length", lenLit.Result),
        ("capacity", capLit.Result),
        ("element_size", elemSizeLit.Result)
      };
      var managedTypeName = resolvedStruct.GetField("managed")!.Type.Name;
      var managedStruct = new MaxonStructLiteralOp(managedTypeName, managedFields);
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

  private MaxonManagedMemGetOp CloneManagedMemGetOp(MaxonManagedMemGetOp memGet) {
    var resultKind = _typeSubstitution.SubstituteValueKind(memGet.ResultKind, memGet.TypeParamName);
    var paramKey = memGet.TypeParamName ?? "Element";
    var isHeapPtrElem = _typeSubstitution.TryGetValue(paramKey, out var getElemType)
      && (getElemType is IrStructType || getElemType is IrEnumType { HasAssociatedValues: true });
    string? elemTypeName = null;
    if (isHeapPtrElem && getElemType is IrType named) {
      // Use Name directly — the type was already resolved to its concrete form
      // by TypeSubstitution.Build. Calling SubstituteName again would incorrectly
      // resolve user types whose names collide with internal aliases (e.g., user's
      // "Entry" type vs Map's internal "typealias Entry = (Key, Value)").
      elemTypeName = named.Name;
    }
    var cloned = new MaxonManagedMemGetOp(MapValue(memGet.ManagedStruct), MapValue(memGet.Index), resultKind) {
      IsStructElement = isHeapPtrElem,
      StructElementTypeName = elemTypeName,
      TypeParamName = memGet.TypeParamName
    };
    RegisterHeapElementResult(memGet.Result, cloned.Result, isHeapPtrElem, elemTypeName, getElemType);
    return cloned;
  }

  private MaxonManagedMemRemoveOp CloneManagedMemRemoveOp(MaxonManagedMemRemoveOp memRemove) {
    var resultKind = _typeSubstitution.SubstituteValueKind(memRemove.ResultKind, memRemove.TypeParamName);
    var paramKey = memRemove.TypeParamName ?? "Element";
    var isHeapPtrElem = _typeSubstitution.TryGetValue(paramKey, out var removeElemType)
      && (removeElemType is IrStructType || removeElemType is IrEnumType { HasAssociatedValues: true });
    string? elemTypeName = null;
    if (isHeapPtrElem && removeElemType is IrType named) {
      elemTypeName = named.Name;
    }
    var cloned = new MaxonManagedMemRemoveOp(MapValue(memRemove.ManagedStruct), MapValue(memRemove.Index), resultKind) {
      IsStructElement = isHeapPtrElem,
      StructElementTypeName = elemTypeName,
      TypeParamName = memRemove.TypeParamName
    };
    RegisterHeapElementResult(memRemove.Result, cloned.Result, isHeapPtrElem, elemTypeName, removeElemType);
    return cloned;
  }

  private MaxonManagedMemSetOp CloneManagedMemSetOp(MaxonManagedMemSetOp memSet) {
    var elementKind = _typeSubstitution.SubstituteValueKind(memSet.ElementKind, memSet.TypeParamName);
    var paramKey = memSet.TypeParamName ?? "Element";
    var isHeapPtrElem = _typeSubstitution.TryGetValue(paramKey, out var setElemType)
      && (setElemType is IrStructType || setElemType is IrEnumType { HasAssociatedValues: true });
    var mappedValue = MapValue(memSet.Value);
    return new MaxonManagedMemSetOp(MapValue(memSet.ManagedStruct), MapValue(memSet.Index), mappedValue, elementKind) {
      IsStructElement = isHeapPtrElem,
      TypeParamName = memSet.TypeParamName
    };
  }

  /// When the element is a heap-allocated struct/enum, register the result with
  /// the concrete type so downstream CloneAssignOp resolves the value kind correctly
  /// for refcount management.
  private void RegisterHeapElementResult(MaxonValue sourceResult, MaxonValue clonedResult,
      bool isHeapPtrElem, string? elemTypeName, IrType? elemType) {
    if (isHeapPtrElem && elemTypeName != null) {
      RegisterResult(sourceResult, new MaxonStruct(clonedResult.Id, elemTypeName));
    } else if (isHeapPtrElem && elemType is IrEnumType) {
      RegisterResult(sourceResult, new MaxonEnum(clonedResult.Id, elemType.Name));
    } else {
      RegisterResult(sourceResult, clonedResult);
    }
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
