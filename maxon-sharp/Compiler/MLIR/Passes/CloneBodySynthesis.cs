using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// ⭐ THE ONE PLACE a synthesized <c>&lt;T&gt;.clone</c> BODY is built — for a struct and for a
/// union alike.
///
/// `specs/memory-safety.md` defines `.clone()` as "a new, independent copy" and auto-generates the
/// `Cloneable` conformance for any type whose MEMBERS all conform: a struct's FIELDS, a union's
/// case PAYLOADS. Two callers have to build a body from that one rule — the PARSER, for every type
/// a source file declares, and <see cref="CloneSynthesisPass"/>, for the types minted after
/// parsing (the tuples and generic instances monomorphization creates). They each used to carry
/// their own copy of the struct walk, and the copies had already drifted: one registered the
/// nested clone results for scope cleanup and the other did not.
///
/// ⚠ A MEMBER THE COPY CANNOT REACH STAYS AN ALIAS, and it is the same rule
/// <see cref="Core.ManagedElementCopy"/> applies to a buffer element: a heap member whose type has
/// no cloner is copied by POINTER, because the language defines no independent copy of it and
/// there is nothing to call. Auto-conformance is what keeps that from being reachable for a
/// declared type — a type only conforms when every member does — so the fallback exists for the
/// post-parse types <see cref="CloneSynthesisPass"/> builds clones for without asking about
/// conformance at all.
/// </summary>
public static class CloneBodySynthesis {
  /// The receiver every synthesized cloner takes, and the variable its lowering stores the
  /// receiver into. A union's per-case block reaches the receiver back through this NAME rather
  /// than through the parameter's SSA value: the case blocks are separate blocks, and a value
  /// produced in the entry block is not live in them. Public because the two callers DECLARE the
  /// function whose body is built here, and a parameter named differently there is a body reading
  /// a variable nothing stored.
  public const string SelfParamName = "self";

  private const string EntryBlockLabel = "entry";

  /// Prefix of the variable a nested member clone's result is parked in so the scope end can
  /// release it. The member's own +1 is handed to the enclosing record by the struct literal /
  /// enum construct, which takes its OWN reference; without this the member's would leak.
  private const string MemberCloneTempPrefix = "__call_tmp_";

  /// Prefix of the variable holding the copy the cloner hands back. It is named in the scope end's
  /// KEEP set, which is what suppresses its release: the caller takes that reference.
  private const string ReturnedCopyPrefix = "__retval_";

  /// Prefix of the variable a union cloner's tag dispatch reads. `MaxonSwitchOp` reaches its
  /// scrutinee by NAME so the dispatch owns exactly one load of it, wherever its comparisons end
  /// up.
  private const string UnionTagVarPrefix = "__clonetag_";

  /// Prefix of the block labels one union cloner's dispatch mints. Unique per cloner so two
  /// cloners in one module cannot collide.
  private const string UnionDispatchPrefix = "__clonecase_";

  /// <summary>
  /// Fill `cloneFunc` with the body of `&lt;typeName&gt;.clone`. The two callers reach the two shapes
  /// through this ONE door: each of them holds a type it has already decided deserves a cloner, and
  /// asking each to dispatch on the KIND itself would be the same two-arm switch written twice.
  /// </summary>
  public static void EmitCloneBody(IrModule<MaxonOp> module, IrFunction<MaxonOp> cloneFunc,
      string typeName, IrType selfType, Func<string, string> resolveCloneName) {
    switch (selfType) {
      case IrStructType structType:
        EmitStructCloneBody(module, cloneFunc, typeName, structType, resolveCloneName);
        break;
      case IrEnumType unionType:
        EmitUnionCloneBody(module, cloneFunc, typeName, unionType, resolveCloneName);
        break;
      default:
        throw new InvalidOperationException(
          $"Cannot synthesize a cloner for '{typeName}': it is neither a struct nor a union, so it has no members to copy");
    }
  }

  /// <summary>
  /// A STRUCT's body: read every field off `self`, clone the ones that are heap records with a
  /// cloner of their own, and build a fresh record from the results. A primitive field is already
  /// an independent copy once loaded.
  /// </summary>
  private static void EmitStructCloneBody(IrModule<MaxonOp> module, IrFunction<MaxonOp> cloneFunc,
      string typeName, IrStructType structType, Func<string, string> resolveCloneName) {
    var block = cloneFunc.Body.AddBlock(EntryBlockLabel);
    var selfParam = new MaxonStructParamOp(0, SelfParamName, typeName);
    block.AddOp(selfParam);

    var fieldValues = new List<(string FieldName, MaxonValue Value)>();
    var memberCloneTemps = new List<string>();
    foreach (var field in structType.Fields) {
      var access = new MaxonFieldAccessOp(selfParam.Result, typeName, field.Name,
        field.Type.ToValueKind(), NamedIrType.NameOf(field.Type));
      block.AddOp(access);
      fieldValues.Add((field.Name,
        CopyOfMember(module, block, access.Result, field.Type, resolveCloneName, memberCloneTemps)));
    }

    var structLit = new MaxonStructLiteralOp(typeName, fieldValues);
    block.AddOp(structLit);
    EmitReturnOfTheCopy(block, structLit.Result, MaxonValueKind.Struct, memberCloneTemps);
  }

  /// <summary>
  /// A UNION's body: switch on the tag and rebuild the LIVE case from independent copies of its
  /// payloads.
  ///
  /// ⭐ THE DISPATCH IS A <see cref="MaxonSwitchOp"/>, WHICH IS THE ONE THE LANGUAGE'S OWN `match`
  /// EMITS. It hands the lowering a sorted, disjoint interval plan and lets
  /// `MaxonToStandardConversion.SwitchDispatch` pick a compare chain, a jump table or a binary
  /// search — and, decisively, that file is the only thing that then emits a `cf.cond_br`. On x64 a
  /// cond_br is a SINGLE `jcc` to its ELSE target with the THEN target reached by FALLTHROUGH, so a
  /// then-target that is not the physically next block is a silent miscompile; the dispatch emitter
  /// already owns that invariant ("emitting in pre-order and inserting the result as one run"), and
  /// routing through it means this synthesis emits no conditional branch of its own to get wrong.
  ///
  /// ⭐ THE LAST CASE IS THE DISPATCH DEFAULT rather than an interval of its own, and no unreachable
  /// block is minted for a tag outside the plan. A union's tag is written by
  /// `MaxonEnumConstructOp` and is always one of its cases, so the default is unreachable either
  /// way — and the language's own exhaustive `match` resolves the same unreachable default to a
  /// real block (its merge) rather than to a panic.
  ///
  /// ⚠ A UNION WITH NO PAYLOAD ANYWHERE IS AN i64 ORDINAL, NOT A BOX, so its copy is the ordinal
  /// itself. Rebuilding it through `MaxonEnumConstructOp` would heap-allocate a box for a type
  /// whose values are never boxed.
  /// </summary>
  private static void EmitUnionCloneBody(IrModule<MaxonOp> module, IrFunction<MaxonOp> cloneFunc,
      string typeName, IrEnumType unionType, Func<string, string> resolveCloneName) {
    var entry = cloneFunc.Body.AddBlock(EntryBlockLabel);
    var selfParam = new MaxonEnumParamOp(0, SelfParamName, typeName, MaxonValueKind.Integer);
    entry.AddOp(selfParam);

    if (!unionType.HasAssociatedValues) {
      // The copy of an ordinal IS the ordinal: nothing was allocated, so there is nothing to park,
      // nothing to keep past the scope end and nothing to release.
      entry.AddOp(new MaxonScopeEndOp([]));
      entry.AddOp(new MaxonReturnOp(selfParam.Result));
      return;
    }

    var tag = new MaxonEnumTagOp(selfParam.Result, typeName);
    entry.AddOp(tag);
    var tagVar = $"{UnionTagVarPrefix}{IrContext.Current.NextId()}";
    entry.AddOp(new MaxonAssignOp(tagVar, tag.Result, isDeclaration: true, isMutable: false,
      MaxonValueKind.Integer));

    // Ascending by tag so the plan the switch receives is SORTED, which it requires; the default
    // takes the highest, so the intervals are the rest in order and nothing has to be re-sorted.
    // Its other requirement — pairwise DISJOINT — holds because a union's tag is its case ORDINAL
    // (`IrEnumCase.TagValue`, and `E3080` refuses raw values on a union), so no two can collide.
    var dispatchPrefix = $"{UnionDispatchPrefix}{IrContext.Current.NextId()}";
    var casesByTag = unionType.Cases.OrderBy(c => c.TagValue).ToList();
    string LabelOf(int index) => $"{dispatchPrefix}.case{index}";

    var intervals = new List<MaxonSwitchInterval>();
    for (int i = 0; i < casesByTag.Count - 1; i++)
      intervals.Add(new MaxonSwitchInterval(casesByTag[i].TagValue, casesByTag[i].TagValue, LabelOf(i)));
    entry.AddOp(new MaxonSwitchOp(tagVar, intervals, LabelOf(casesByTag.Count - 1), dispatchPrefix));

    for (int i = 0; i < casesByTag.Count; i++) {
      var enumCase = casesByTag[i];
      var block = cloneFunc.Body.AddBlock(LabelOf(i));
      var selfRef = new MaxonEnumVarRefOp(SelfParamName, typeName, MaxonValueKind.Integer);
      block.AddOp(selfRef);

      var payloadCopies = new List<MaxonValue>();
      var memberCloneTemps = new List<string>();
      var payloads = enumCase.AssociatedValues ?? [];
      for (int slot = 0; slot < payloads.Count; slot++) {
        var payloadType = payloads[slot].Type;
        var heap = HeapMemberIdentityOf(payloadType);
        // A scalar payload slot is one machine word, and a clone copies it verbatim. Reading it
        // back as its DECLARED kind would round-trip a `bool` through the i1 the payload load
        // narrows it to, and hand `MaxonEnumConstructOp` an i1 to store into an i64 slot.
        var payload = new MaxonEnumPayloadOp(selfRef.Result, typeName, slot,
          heap?.Kind ?? MaxonValueKind.Integer, heap?.TypeName);
        block.AddOp(payload);
        payloadCopies.Add(
          CopyOfMember(module, block, payload.Result, payloadType, resolveCloneName, memberCloneTemps));
      }

      var rebuilt = new MaxonEnumConstructOp(typeName, enumCase.Name, enumCase.TagValue, payloadCopies);
      block.AddOp(rebuilt);
      EmitReturnOfTheCopy(block, rebuilt.Result, MaxonValueKind.Enum, memberCloneTemps);
    }
  }

  /// The value a member of type `memberType` contributes to the copy: an independent clone when the
  /// member is a heap record with a cloner of its own, and the loaded value itself otherwise — a
  /// primitive, a ranged primitive and a payload-free enum are all ordinals a load already
  /// duplicates.
  private static MaxonValue CopyOfMember(IrModule<MaxonOp> module, IrBlock<MaxonOp> block,
      MaxonValue member, IrType memberType, Func<string, string> resolveCloneName,
      List<string> memberCloneTemps) {
    if (HeapMemberIdentityOf(memberType) is not { } heapMember) return member;
    var (kind, memberTypeName) = heapMember;

    // The nested type may live in a different namespace than the type being synthesized (a
    // `Testing/` struct with a `String` field clones through `stdlib.String.clone`), so the
    // registered name is resolved rather than spelled.
    var callee = resolveCloneName(memberTypeName);
    if (module.FindFunctionByExactName(callee) == null) return member;

    var cloneCall = new MaxonCallOp(callee, [member], kind, memberTypeName);
    block.AddOp(cloneCall);

    var temp = $"{MemberCloneTempPrefix}{cloneCall.Result!.Id}";
    block.AddOp(new MaxonAssignOp(temp, cloneCall.Result, isDeclaration: true, isMutable: false, kind));
    memberCloneTemps.Add(temp);
    return cloneCall.Result;
  }

  /// The kind and type name of a member the copy has to reach THROUGH, or null for one a slot copy
  /// already duplicates.
  ///
  /// A union carrying payloads is a heap box holding heap pointers, so a copied member slot is a
  /// second name for one box — the same sharing a struct field has, and it needs the same
  /// independent copy. A payload-FREE enum or union is an i64 ordinal and has no box to share.
  private static (MaxonValueKind Kind, string TypeName)? HeapMemberIdentityOf(IrType memberType) =>
    memberType switch {
      IrStructType structType => (MaxonValueKind.Struct, structType.Name),
      IrEnumType { HasAssociatedValues: true } unionType => (MaxonValueKind.Enum, unionType.Name),
      _ => null
    };

  /// Close a cloner's block: park the fresh record in a variable so the scope end can KEEP it (the
  /// caller takes that reference), release every nested member clone's own reference, and hand the
  /// record back.
  private static void EmitReturnOfTheCopy(IrBlock<MaxonOp> block, MaxonValue copy,
      MaxonValueKind copyKind, List<string> memberCloneTemps) {
    var returnedCopy = $"{ReturnedCopyPrefix}{IrContext.Current.NextId()}";
    block.AddOp(new MaxonAssignOp(returnedCopy, copy, isDeclaration: true, isMutable: false, copyKind));
    block.AddOp(new MaxonScopeEndOp([.. memberCloneTemps, returnedCopy], keepVars: [returnedCopy]));
    block.AddOp(new MaxonReturnOp(copy));
  }
}
