using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// The specialization a clone is running under, in the shape <see cref="SubstitutingOpCloner"/>
/// reads it. Two passes specialize function bodies and both have to rebuild every op they meet:
/// <see cref="FunctionCloner"/> binds generic type parameters to concrete types, and
/// MonomorphizationPass's interface-alias stage binds an interface (or interface alias) to the
/// concrete type a call site passed. What they substitute differs — that is why they are still two
/// passes — but for most ops the rebuild is the same rule applied through a different map, and that
/// rule belongs in one place.
/// </summary>
internal interface IOpSubstitution {
  /// Names the specialization mechanism for the unhandled-op message. The two passes used to raise
  /// that message themselves, and their names read as if they were swapped — FunctionCloner.cs said
  /// "Monomorphization" and MonomorphizationPass.cs said "Interface alias specialization". They were
  /// not: each names the MECHANISM, and each file's mechanism happens to be the other file's name.
  /// Carrying it on the substitution keeps that true from the one place the message now lives.
  string Mechanism { get; }

  /// The clone's counterpart of a source value, minting one if the definition has not been seen.
  MaxonValue MapValue(MaxonValue old);

  /// Wire a source op's result to the clone's, so later uses map through.
  void RegisterResult(MaxonValue oldResult, MaxonValue newResult);

  /// A type name through the substitution map; unchanged when the map does not bind it.
  string SubstituteName(string name);

  /// The kind a `TypeParameter` op result takes once its parameter is bound.
  MaxonValueKind SubstituteValueKind(MaxonValueKind kind, string? typeParamName);

  /// The concrete type bound to a type parameter, if the substitution binds one.
  bool TryGetBoundType(string typeParamName, out IrType boundType);

  /// Re-derive a managed element's representation now that its Element is bound.
  ManagedElementInfo ResolveManagedElement(MaxonManagedMemGetOp op);
}

/// <summary>
/// The op-clone rule shared by every function-specializing pass.
///
/// It covers the ops whose rebuild is the SAME under any substitution: the ops that carry no type
/// name at all, and the ops whose only type-dependent state is a name (or an element kind) read
/// straight out of the map. Ops whose rebuild genuinely differs between the two passes — calls,
/// assignments, params, var refs, struct literals, closures, and everything whose clone depends on
/// per-function state the caller accumulates — stay in the caller's own switch and never reach here.
///
/// It exists because the two switches had drifted: nine op kinds had an arm in FunctionCloner and
/// none in MonomorphizationPass, so a `__Builtins.ucd*` load, a `__ManagedMemoryCursor` read or a
/// `__ManagedList` cursor read inside a function with an interface-typed parameter compiled to
/// `E9001 unhandled op type` rather than to code. A roster written twice is a roster that will
/// disagree; this is the one copy, and the one `default`.
/// </summary>
internal static class SubstitutingOpCloner {
  /// <summary>
  /// Rebuilds <paramref name="op"/> under <paramref name="sub"/>. Throws when the op is not one
  /// the shared rule covers — callers reach here only from their own switch's default arm, so an
  /// op that lands here is one no cloner knows how to specialize.
  /// </summary>
  internal static MaxonOp Clone(MaxonOp op, IOpSubstitution sub) {
    switch (op) {
      // --- Ops that carry no type-dependent state: values in, values out ---

      case MaxonLiteralOp lit: {
        var cloned = lit.ValueKind switch {
          MaxonValueKind.Integer => new MaxonLiteralOp(lit.IntValue),
          MaxonValueKind.Float => new MaxonLiteralOp(lit.FloatValue),
          MaxonValueKind.Float32 => new MaxonLiteralOp(lit.FloatValue, MaxonValueKind.Float32),
          MaxonValueKind.Bool => new MaxonLiteralOp(lit.BoolValue),
          _ => throw new InvalidOperationException($"Unsupported literal kind: {lit.ValueKind}")
        };
        sub.RegisterResult(lit.Result, cloned.Result);
        return cloned;
      }

      case MaxonCondBrOp cb: return new MaxonCondBrOp(sub.MapValue(cb.Condition), cb.ThenBlock, cb.ElseBlock);
      case MaxonBrOp br: return new MaxonBrOp(br.Target);
      // Every specialization keeps the ORIGINAL point id, so all of them add into one counter. That
      // is the right answer for a report about SOURCE: the generic body was written once, and its
      // coverage is how often that text ran, not how many types it was instantiated for.
      case MaxonCovPointOp cp: return new MaxonCovPointOp(cp.PointId);
      case MaxonSwitchOp sw: return new MaxonSwitchOp(sw.ScrutineeVarName, [.. sw.Intervals], sw.DefaultBlock, sw.DispatchLabelPrefix);
      case MaxonReturnOp ret: return new MaxonReturnOp(ret.Value != null ? sub.MapValue(ret.Value) : null, ret.IsErrorPropagation);
      case MaxonThrowOp th: return new MaxonThrowOp(sub.MapValue(th.ErrorValue), th.ErrorTypeName) { IsOwnedLocalTransfer = th.IsOwnedLocalTransfer };
      case MaxonPanicOp p: return p.CloneKeepingLabel();
      case MaxonPanicDynamicOp pd: return new MaxonPanicDynamicOp((MaxonStruct)sub.MapValue(pd.MessageStruct));
      case MaxonRefEqOp req: { var c = new MaxonRefEqOp(sub.MapValue(req.Lhs), sub.MapValue(req.Rhs), req.Negate); sub.RegisterResult(req.Result, c.Result); return c; }
      // Variable names reference, not values — copy as-is
      case MaxonScopeEndOp se: return new MaxonScopeEndOp(se.VarsToClean, se.KeepVars);

      // --- Unary and binary math on already-concrete kinds ---

      case MaxonTruncOp t: { var c = new MaxonTruncOp(sub.MapValue(t.Input)); sub.RegisterResult(t.Result, c.Result); return c; }
      case MaxonIntToFloatOp i: { var c = new MaxonIntToFloatOp(sub.MapValue(i.Input)); sub.RegisterResult(i.Result, c.Result); return c; }
      case MaxonCastOp ca: { var c = new MaxonCastOp(sub.MapValue(ca.Input), ca.TargetKind, ca.SourceOptimalType); sub.RegisterResult(ca.Result, c.Result); return c; }
      case MaxonBitcastF64ToI64Op bc: { var c = new MaxonBitcastF64ToI64Op(sub.MapValue(bc.Input)); sub.RegisterResult(bc.Result, c.Result); return c; }
      case MaxonBitcastI64ToF64Op bc: { var c = new MaxonBitcastI64ToF64Op(sub.MapValue(bc.Input)); sub.RegisterResult(bc.Result, c.Result); return c; }
      case MaxonAbsOp a: { var c = new MaxonAbsOp(sub.MapValue(a.Input)); sub.RegisterResult(a.Result, c.Result); return c; }
      case MaxonSqrtOp s: { var c = new MaxonSqrtOp(sub.MapValue(s.Input)); sub.RegisterResult(s.Result, c.Result); return c; }
      case MaxonFloorOp f: { var c = new MaxonFloorOp(sub.MapValue(f.Input)); sub.RegisterResult(f.Result, c.Result); return c; }
      case MaxonCeilOp ce: { var c = new MaxonCeilOp(sub.MapValue(ce.Input)); sub.RegisterResult(ce.Result, c.Result); return c; }
      case MaxonRoundOp r: { var c = new MaxonRoundOp(sub.MapValue(r.Input)); sub.RegisterResult(r.Result, c.Result); return c; }
      case MaxonMinOp mi: { var c = new MaxonMinOp(sub.MapValue(mi.Lhs), sub.MapValue(mi.Rhs)); sub.RegisterResult(mi.Result, c.Result); return c; }
      case MaxonMaxOp ma: { var c = new MaxonMaxOp(sub.MapValue(ma.Lhs), sub.MapValue(ma.Rhs)); sub.RegisterResult(ma.Result, c.Result); return c; }

      // --- Ops whose only type-dependent state is a name read out of the map ---

      case MaxonSizeofOp sz: { var c = new MaxonSizeofOp(sub.SubstituteName(sz.TypeName)); sub.RegisterResult(sz.Result, c.Result); return c; }
      case MaxonCountofOp co: { var c = new MaxonCountofOp(sub.SubstituteName(co.TypeName), co.Line, co.Column); sub.RegisterResult(co.Result, c.Result); return c; }
      case MaxonStructParamOp sp: { var c = new MaxonStructParamOp(sp.Index, sp.Name, sub.SubstituteName(sp.StructTypeName)); sub.RegisterResult(sp.Result, c.Result); return c; }
      case MaxonStructVarRefOp sv: { var c = new MaxonStructVarRefOp(sv.VarName, sub.SubstituteName(sv.StructTypeName)); sub.RegisterResult(sv.Result, c.Result); return c; }
      case MaxonFieldAccessOp fa: { var c = fa.CloneWith(sub.MapValue(fa.StructValue), sub.SubstituteName(fa.TypeName), fa.ResultStructTypeName != null ? sub.SubstituteName(fa.ResultStructTypeName) : null); sub.RegisterResult(fa.Result, c.Result); return c; }
      case MaxonFieldAssignOp fa: return new MaxonFieldAssignOp(sub.MapValue(fa.StructValue), sub.SubstituteName(fa.TypeName), fa.FieldName, sub.MapValue(fa.NewValue));

      // --- Enum and string type names ---
      //
      // These read their type name through the substitution exactly as the enum-construct family
      // below does. The interface-alias pass used to keep its own copies that did NOT substitute,
      // on the reasoning that an interface alias never names an enum or a string type. That
      // reasoning is sound but it did not justify a second roster: it makes the substitution the
      // IDENTITY here, not something to be skipped, and four sibling ops in that same switch
      // already applied it. MEASURED before collapsing them, over the whole bootstrap corpus:
      // 30 of these arms were reached under an interface-alias substitution and NONE of the names
      // was rewritten. The invariant behind that: the interface-alias map is keyed only by
      // interface (or interface-alias) type names, by `Self`, and by the specialized function's
      // own owner type - and `flat-namespace check` forbids one name from being both an enum and
      // an interface, so a hit can only be the owner naming itself, which is the identity.

      case MaxonEnumLiteralOp el: { var c = el.BackingKind is MaxonValueKind.Float or MaxonValueKind.Float32 ? new MaxonEnumLiteralOp(sub.SubstituteName(el.EnumTypeName), el.CaseName, el.FloatValue) : new MaxonEnumLiteralOp(sub.SubstituteName(el.EnumTypeName), el.CaseName, el.IntValue); sub.RegisterResult(el.Result, c.Result); return c; }
      case MaxonEnumParamOp ep: { var c = new MaxonEnumParamOp(ep.Index, ep.Name, sub.SubstituteName(ep.EnumTypeName), ep.BackingKind); sub.RegisterResult(ep.Result, c.Result); return c; }
      case MaxonEnumVarRefOp ev: { var c = new MaxonEnumVarRefOp(ev.VarName, sub.SubstituteName(ev.EnumTypeName), ev.BackingKind); sub.RegisterResult(ev.Result, c.Result); return c; }
      case MaxonEnumRawValueOp er: { var c = new MaxonEnumRawValueOp(sub.MapValue(er.EnumValue), sub.SubstituteName(er.EnumTypeName), er.ResultKind); sub.RegisterResult(er.Result, c.Result); return c; }
      case MaxonEnumOrdinalOp eo: { var c = new MaxonEnumOrdinalOp(sub.MapValue(eo.EnumValue), sub.SubstituteName(eo.EnumTypeName)); sub.RegisterResult(eo.Result, c.Result); return c; }
      case MaxonEnumNameOp en: { var c = new MaxonEnumNameOp(sub.MapValue(en.EnumValue), sub.SubstituteName(en.EnumTypeName)); sub.RegisterResult(en.Result, c.Result); return c; }
      case MaxonEnumStringRawValueOp esr: { var c = new MaxonEnumStringRawValueOp(sub.MapValue(esr.EnumValue), sub.SubstituteName(esr.EnumTypeName), esr.IsChar); sub.RegisterResult(esr.Result, c.Result); return c; }
      // The STRUCT type name rides through unsubstituted in both of the next two, and always did:
      // it names the case's payload record, which the enum's own definition decides.
      case MaxonEnumStructRawValueOp esrv: { var c = new MaxonEnumStructRawValueOp(sub.MapValue(esrv.EnumValue), sub.SubstituteName(esrv.EnumTypeName), esrv.StructTypeName); sub.RegisterResult(esrv.Result, c.Result); return c; }
      case MaxonEnumStructRawFieldOp esrf: { var c = new MaxonEnumStructRawFieldOp(sub.MapValue(esrf.EnumValue), sub.SubstituteName(esrf.EnumTypeName), esrf.StructTypeName, esrf.FieldName, esrf.ResultKind, esrf.ResultTypeName == null ? null : sub.SubstituteName(esrf.ResultTypeName)); sub.RegisterResult(esrf.Result, c.Result); return c; }
      case MaxonEnumFunctionRawValueOp efrv: { var c = new MaxonEnumFunctionRawValueOp(sub.MapValue(efrv.EnumValue), sub.SubstituteName(efrv.EnumTypeName), efrv.Signature); sub.RegisterResult(efrv.Result, c.Result); return c; }
      case MaxonErrorFlagToEnumOp ef: { var c = new MaxonErrorFlagToEnumOp(sub.MapValue(ef.ErrorFlag), sub.SubstituteName(ef.EnumTypeName), ef.BackingKind, ef.HasAssociatedValues); sub.RegisterResult(ef.Result, c.Result); return c; }

      case MaxonStringLiteralOp strLit: { var c = new MaxonStringLiteralOp(strLit.Value, sub.SubstituteName(strLit.StringTypeName)); sub.RegisterResult(strLit.Result, c.Result); return c; }
      case MaxonByteStringLiteralOp bstrLit: { var c = new MaxonByteStringLiteralOp(bstrLit.Value, sub.SubstituteName(bstrLit.ArrayTypeName)); sub.RegisterResult(bstrLit.Result, c.Result); return c; }
      case MaxonStringInterpOp interp: {
        var newParts = interp.Parts.Select(p => (p.IsLiteral, p.LiteralValue, p.ExprValue != null ? sub.MapValue(p.ExprValue) : (MaxonValue?)null, p.FormatSpec, p.OptimalType)).ToList();
        var c = new MaxonStringInterpOp(newParts, sub.SubstituteName(interp.StringTypeName));
        sub.RegisterResult(interp.Result, c.Result);
        return c;
      }

      case MaxonEnumConstructOp ec: { var c = new MaxonEnumConstructOp(sub.SubstituteName(ec.EnumTypeName), ec.CaseName, ec.TagValue, [.. ec.Args.Select(sub.MapValue)]); sub.RegisterResult(ec.Result, c.Result); return c; }
      case MaxonEnumTagOp et: { var c = new MaxonEnumTagOp(sub.MapValue(et.EnumValue), sub.SubstituteName(et.EnumTypeName)); sub.RegisterResult(et.Result, c.Result); return c; }
      case MaxonEnumPayloadAssignOp epa: return new MaxonEnumPayloadAssignOp(epa.EnumVarName, sub.SubstituteName(epa.EnumTypeName), epa.PayloadIndex, sub.MapValue(epa.NewValue));

      // --- Globals ---
      // The type names travel with the clone: they are what tells lowering a boxed union's slot
      // owns a refcounted record, and a clone that dropped them silently lowered the global as a
      // bare integer inside the cloned body only.
      case MaxonGlobalLoadOp gl: {
        var c = new MaxonGlobalLoadOp(gl.GlobalName, gl.ValueKind, gl.EnumTypeName, gl.StructTypeName) {
          LazyGuardName = gl.LazyGuardName, LazyInitFuncName = gl.LazyInitFuncName
        };
        sub.RegisterResult(gl.Result, c.Result);
        return c;
      }
      case MaxonGlobalStoreOp gs: return new MaxonGlobalStoreOp(gs.GlobalName, sub.MapValue(gs.Value), gs.ValueKind, gs.EnumTypeName);

      // --- Runtime calls and function params ---

      case MaxonCallRuntimeOp cr: { var na = cr.Args.Select(sub.MapValue).ToList(); var c = new MaxonCallRuntimeOp(cr.FunctionName, na, cr.Result != null); if (cr.Result != null && c.Result != null) sub.RegisterResult(cr.Result, c.Result); return c; }
      case MaxonFunctionParamOp fp: { var c = new MaxonFunctionParamOp(fp.Index, fp.Name, fp.FunctionType); sub.RegisterResult(fp.Result, c.Result); return c; }

      // --- Managed memory ---

      case MaxonUcdByteLoadOp ucdByte: { var c = new MaxonUcdByteLoadOp(ucdByte.UcddataLabel, sub.MapValue(ucdByte.ByteOffset)); sub.RegisterResult(ucdByte.Result, c.Result); return c; }
      case MaxonUcdI64LoadOp ucdI64: { var c = new MaxonUcdI64LoadOp(ucdI64.UcddataLabel, sub.MapValue(ucdI64.Index)); sub.RegisterResult(ucdI64.Result, c.Result); return c; }
      case MaxonByteRangePanicOp brp: return new MaxonByteRangePanicOp(sub.MapValue(brp.End), sub.MapValue(brp.Capacity), brp.PanicLabel);
      case MaxonManagedMemGetOp mg: {
        var mgInfo = sub.ResolveManagedElement(mg);
        var c = new MaxonManagedMemGetOp(sub.MapValue(mg.ManagedStruct), sub.MapValue(mg.Index), mgInfo.Kind) {
          IsStructElement = mgInfo.IsStructElement,
          StructElementTypeName = mgInfo.StructElementTypeName,
          TypeParamName = mg.TypeParamName,
          IsBoundsCheckSafe = mg.IsBoundsCheckSafe,
          ElementStorageType = mgInfo.ElementStorageType
        };
        sub.RegisterResult(mg.Result, mgInfo.WrapResult(c.Result));
        return c;
      }

      // --- Managed-memory cursor ---

      case MaxonCursorCurrentOp curCur: return CloneCursorCurrentOp(curCur, sub);
      case MaxonCursorIndexOp curIdx: { var c = new MaxonCursorIndexOp(sub.MapValue(curIdx.CursorStruct)); sub.RegisterResult(curIdx.Result, c.Result); return c; }

      // --- Deferred iterator resolution (for-in over a type whose iterator is not yet concrete) ---

      case MaxonIteratorAdvanceOp iterAdv: {
        var c = new MaxonIteratorAdvanceOp(sub.SubstituteName(iterAdv.IterableTypeName),
          sub.SubstituteName(iterAdv.IteratorAliasName), [.. iterAdv.Args.Select(sub.MapValue)]);
        sub.RegisterResult(iterAdv.ErrorFlag, c.ErrorFlag);
        return c;
      }
      case MaxonIteratorCurrentOp iterCur: {
        var elemStructType = iterCur.ElementStructTypeName != null ? sub.SubstituteName(iterCur.ElementStructTypeName) : null;
        var elemKind = iterCur.ElementKind.HasValue ? sub.SubstituteValueKind(iterCur.ElementKind.Value, null) : iterCur.ElementKind;
        var c = new MaxonIteratorCurrentOp(sub.SubstituteName(iterCur.IterableTypeName),
          sub.SubstituteName(iterCur.IteratorAliasName), [.. iterCur.Args.Select(sub.MapValue)], elemKind, elemStructType);
        if (iterCur.Result != null && c.Result != null)
          sub.RegisterResult(iterCur.Result, c.Result);
        return c;
      }

      // --- ManagedList (doubly-linked list) ops ---

      case MaxonManagedListCreateOp mlc: { var c = new MaxonManagedListCreateOp(sub.SubstituteName(mlc.Result.TypeName)); sub.RegisterResult(mlc.Result, c.Result); return c; }
      case MaxonManagedListInsertValueOp ci: { var c = new MaxonManagedListInsertValueOp(sub.MapValue(ci.ManagedList), sub.MapValue(ci.Value), ci.AtHead, sub.SubstituteName(ci.ValueKind)); sub.RegisterResult(ci.Result, c.Result); return c; }
      case MaxonManagedListInsertRelativeValueOp cir: { var c = new MaxonManagedListInsertRelativeValueOp(sub.MapValue(cir.ManagedList), sub.MapValue(cir.Target), sub.MapValue(cir.Value), cir.After, sub.SubstituteName(cir.ValueKind)); sub.RegisterResult(cir.Result, c.Result); return c; }
      case MaxonManagedListDetachOp cd: return new MaxonManagedListDetachOp(sub.MapValue(cd.ManagedList), sub.MapValue(cd.Node));
      case MaxonManagedListRemoveOp crm: {
        var c = new MaxonManagedListRemoveOp(sub.MapValue(crm.ManagedList), sub.MapValue(crm.Node),
          sub.SubstituteName(crm.ValueKind), BoundElementKind(sub, crm.ValueKind, crm.ResultKind));
        sub.RegisterResult(crm.Result, c.Result); return c;
      }
      case MaxonManagedListCountOp cc: { var c = new MaxonManagedListCountOp(sub.MapValue(cc.ManagedList)); sub.RegisterResult(cc.Result, c.Result); return c; }
      case MaxonManagedListNodeValueOp cnv: {
        var c = new MaxonManagedListNodeValueOp(sub.MapValue(cnv.Node),
          sub.SubstituteName(cnv.ValueKind), BoundElementKind(sub, cnv.ValueKind, cnv.ResultKind));
        sub.RegisterResult(cnv.Result, c.Result); return c;
      }
      case MaxonManagedListNodeSetValueOp cns: return new MaxonManagedListNodeSetValueOp(sub.MapValue(cns.Node), sub.MapValue(cns.Value), sub.SubstituteName(cns.ValueKind));
      case MaxonManagedListClearOp ccl: return new MaxonManagedListClearOp(sub.MapValue(ccl.ManagedList), sub.SubstituteName(ccl.ValueKind));
      case MaxonManagedListCursorResetOp ccr: return new MaxonManagedListCursorResetOp(sub.MapValue(ccr.ManagedList));
      case MaxonManagedListCursorValueOp ccv: {
        var c = new MaxonManagedListCursorValueOp(sub.MapValue(ccv.ManagedList),
          sub.SubstituteName(ccv.ValueKind), BoundElementKind(sub, ccv.ValueKind, ccv.ResultKind));
        sub.RegisterResult(ccv.Result, c.Result); return c;
      }
      case MaxonManagedListHeadPtrOp chp: { var c = new MaxonManagedListHeadPtrOp(sub.MapValue(chp.ManagedList)); sub.RegisterResult(chp.Result, c.Result); return c; }
      case MaxonManagedListNodePtrNextOp cpn: { var c = new MaxonManagedListNodePtrNextOp(sub.MapValue(cpn.CursorPtr)); sub.RegisterResult(cpn.Result, c.Result); return c; }
      case MaxonManagedListNodePtrValueOp cpv: {
        var c = new MaxonManagedListNodePtrValueOp(sub.MapValue(cpv.CursorPtr),
          sub.SubstituteName(cpv.ValueKind), BoundElementKind(sub, cpv.ValueKind, cpv.ResultKind));
        sub.RegisterResult(cpv.Result, c.Result); return c;
      }

      default:
        throw new InvalidOperationException($"{sub.Mechanism}: unhandled op type {op.GetType().Name}");
    }
  }

  /// A ManagedList op's element type name doubles as the type-parameter key: once the parameter is
  /// bound the read is typed by the bound type, otherwise the op keeps the kind the parser gave it.
  private static MaxonValueKind BoundElementKind(IOpSubstitution sub, string elementTypeName, MaxonValueKind declaredKind) =>
    sub.TryGetBoundType(elementTypeName, out var bound) ? bound.ToValueKind() : declaredKind;

  private static MaxonCursorCurrentOp CloneCursorCurrentOp(MaxonCursorCurrentOp op, IOpSubstitution sub) {
    var resultKind = sub.SubstituteValueKind(op.ResultKind, op.TypeParamName);
    var paramKey = op.TypeParamName ?? "Element";
    var hasSubstitution = sub.TryGetBoundType(paramKey, out var elemType);
    var isHeapPtrElem = hasSubstitution
      && (elemType is IrStructType || elemType is IrEnumType { HasAssociatedValues: true });
    string? elemTypeName = null;
    if (isHeapPtrElem && elemType is IrType named)
      elemTypeName = named.Name;
    // After monomorphization the cursor's Element is concrete: derive the precise
    // narrow storage type so the load width matches the buffer layout (mirrors
    // DeriveManagedElementInfo). Without this, a u32 element gets loaded as i64
    // and reads adjacent slot bits — corrupting iteration.
    IrType? elementStorageType = op.ElementStorageType;
    if (hasSubstitution && !isHeapPtrElem && elemType is not null) {
      var loadType = elemType switch {
        IrRangedPrimitiveType rpt => rpt.OptimalType,
        _ when elemType == IrType.I8 => IrType.U8,
        _ when elemType == IrType.I16 => IrType.U16,
        _ => elemType
      };
      elementStorageType = loadType is IrEnumType ? null : loadType;
    }
    var cloned = new MaxonCursorCurrentOp(sub.MapValue(op.CursorStruct), resultKind) {
      IsStructElement = isHeapPtrElem,
      StructElementTypeName = elemTypeName,
      TypeParamName = op.TypeParamName,
      ElementStorageType = elementStorageType
    };
    // When the element is a heap-allocated struct/enum, register the result with the concrete type
    // so a downstream assign clone resolves the value kind correctly for refcount management.
    if (isHeapPtrElem && elemTypeName != null) {
      sub.RegisterResult(op.Result, new MaxonStruct(cloned.Result.Id, elemTypeName));
    } else if (isHeapPtrElem && elemType is IrEnumType) {
      sub.RegisterResult(op.Result, new MaxonEnum(cloned.Result.Id, elemType.Name));
    } else {
      sub.RegisterResult(op.Result, cloned.Result);
    }
    return cloned;
  }
}
