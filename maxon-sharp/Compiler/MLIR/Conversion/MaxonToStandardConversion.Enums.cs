using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
  /// <summary>
  /// Loads the discriminant out of a union record, given a pointer to the record itself.
  /// </summary>
  /// <remarks>
  /// Every reader of a union's tag must agree on where it lives. Four do — <c>MaxonEnumTagOp</c>,
  /// <c>MaxonEnumRawValueOp</c>, string interpolation, and the generated union destructor — so they
  /// share this one load rather than each spelling out the load-at-offset-0 by hand.
  /// </remarks>
  private static StdI64 EmitUnionTagLoadFrom(StdI64 recordPtr, IrBlock<StandardOp> block) {
    var tagLoad = new StdLoadIndirectOp(recordPtr, UnionFieldTag, IrType.I64);
    block.AddOp(tagLoad);
    return (StdI64)tagLoad.Result;
  }

  /// <summary>
  /// Loads the discriminant of a heap-boxed (associated-value) union held in a named variable.
  /// </summary>
  /// <remarks>
  /// The record pointer is reloaded from the variable on every call because intervening runtime
  /// calls clobber registers.
  /// </remarks>
  private static StdI64 EmitUnionTagLoad(
    StdHeapPtr unionPtr,
    IrBlock<StandardOp> block,
    Dictionary<string, string> varTypes) =>
    EmitUnionTagLoadFrom((StdI64)EmitLoad(block, unionPtr.VarName!, varTypes), block);

  /// <summary>
  /// Lowers EnumType.fromRawValue(arg) inline as a comparison chain.
  /// For simple/int-backed enums: compares arg against each case's ordinal/raw value.
  /// For float-backed enums: compares arg against each case's float raw value.
  /// For string/char-backed enums: compares arg string against each case's string via memcmp.
  /// Sets error flag to 0 on match, 1 on no match. Result is the matched ordinal.
  /// </summary>
  private static void LowerEnumFromRawValue(
    MaxonTryCallOp tryCallOp,
    IrEnumType enumType,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {

    var inputArg = tryCallOp.Args[0];

    if (enumType.BackingType is IrStringBackingType or IrCharBackingType) {
      // String/char-backed: input is a managed struct, compare against each case's string
      LowerEnumFromRawValueString(tryCallOp, enumType, block, valueMap, varTypes);
    } else if (enumType.BackingType == IrType.F64) {
      // Float-backed: compare float values, result is the input value itself
      var inputVal = (StdF64)valueMap[inputArg];

      var noMatchFlag = new StdConstI64Op(1);
      block.AddOp(noMatchFlag);
      StdI64 currentErrorFlag = noMatchFlag.Result;

      foreach (var enumCase in enumType.Cases) {
        var caseRawConst = new StdConstF64Op((double)enumCase.RawValue!);
        block.AddOp(caseRawConst);
        var cmpOp = new StdCmpF64Op("eq", inputVal, caseRawConst.Result);
        block.AddOp(cmpOp);

        var zeroFlag = new StdConstI64Op(0);
        block.AddOp(zeroFlag);
        var selectFlag = new StdSelectI64Op(cmpOp.Result, zeroFlag.Result, currentErrorFlag);
        block.AddOp(selectFlag);
        currentErrorFlag = selectFlag.Result;
      }

      valueMap[tryCallOp.ErrorFlag] = currentErrorFlag;
      // The result is the input float value (which IS the enum's runtime representation)
      valueMap[tryCallOp.Result!] = inputVal;
    } else if (enumType.BackingType == IrType.I64 || enumType.BackingType == null) {
      // Simple (null backing) or int-backed: compare integer values
      var inputVal = (StdI64)valueMap[inputArg];

      var noMatchFlag = new StdConstI64Op(1);
      block.AddOp(noMatchFlag);
      var defaultOrd = new StdConstI64Op(0);
      block.AddOp(defaultOrd);
      StdI64 currentErrorFlag = noMatchFlag.Result;
      StdI64 currentResult = defaultOrd.Result;

      foreach (var enumCase in enumType.Cases) {
        long rawValue = enumType.BackingType == IrType.I64
          ? (long)enumCase.RawValue!
          : enumCase.Ordinal;

        var caseRawConst = new StdConstI64Op(rawValue);
        block.AddOp(caseRawConst);
        var cmpOp = new StdCmpI64Op("eq", inputVal, caseRawConst.Result);
        block.AddOp(cmpOp);

        // On match: error flag = 0, result = ordinal (or raw value for int-backed)
        var zeroFlag = new StdConstI64Op(0);
        block.AddOp(zeroFlag);
        var selectFlag = new StdSelectI64Op(cmpOp.Result, zeroFlag.Result, currentErrorFlag);
        block.AddOp(selectFlag);
        currentErrorFlag = selectFlag.Result;

        // Result is the runtime value of the enum (ordinal for simple, raw value for int-backed)
        var resultConst = new StdConstI64Op(enumType.BackingType == IrType.I64 ? rawValue : enumCase.Ordinal);
        block.AddOp(resultConst);
        var selectResult = new StdSelectI64Op(cmpOp.Result, resultConst.Result, currentResult);
        block.AddOp(selectResult);
        currentResult = selectResult.Result;
      }

      valueMap[tryCallOp.ErrorFlag] = currentErrorFlag;
      valueMap[tryCallOp.Result!] = currentResult;
    } else {
      throw new InvalidOperationException($"Unsupported enum backing type for fromRawValue: {enumType.BackingType}");
    }
  }

  /// <summary>
  /// Handles fromRawValue for string/char-backed enums.
  /// Compares input string against each case's string value using length check + memcmp.
  /// </summary>
  private static void LowerEnumFromRawValueString(
    MaxonTryCallOp tryCallOp,
    IrEnumType enumType,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {

    var inputArg = tryCallOp.Args[0];
    // Envelope collapse: the String/Character IS its __ManagedMemory, so buffer and length are
    // read straight off the value at offsets 0 and 8 — no nested managed pointer to chase.
    var inputStructName = ((StdHeapPtr)valueMap[inputArg]).VarName!;
    var inputBuf = (StdI64)EmitStructFieldLoad(block, inputStructName, ManagedFieldBuffer, IrType.I64, varTypes);
    var inputLen = (StdI64)EmitStructFieldLoad(block, inputStructName, ManagedFieldLength, IrType.I64, varTypes);

    var noMatchFlag = new StdConstI64Op(1);
    block.AddOp(noMatchFlag);
    var defaultOrd = new StdConstI64Op(0);
    block.AddOp(defaultOrd);
    StdI64 currentErrorFlag = noMatchFlag.Result;
    StdI64 currentResult = defaultOrd.Result;

    foreach (var enumCase in enumType.Cases) {
      var caseString = (string)enumCase.RawValue!;
      var rdataLabel = $"__enum_frv_{enumType.Name}_{enumCase.Name}_{NextRdataId()}";
      var (caseBuf, caseLen) = EmitRdataLiteral(caseString, rdataLabel, block, _resultModule!);
      var bothMatch = EmitStringEquals(inputBuf, inputLen, caseBuf, caseLen, block);

      var zeroFlag = new StdConstI64Op(0);
      block.AddOp(zeroFlag);
      var selectFlag = new StdSelectI64Op(bothMatch, zeroFlag.Result, currentErrorFlag);
      block.AddOp(selectFlag);
      currentErrorFlag = selectFlag.Result;

      var ordConst = new StdConstI64Op(enumCase.Ordinal);
      block.AddOp(ordConst);
      var selectResult = new StdSelectI64Op(bothMatch, ordConst.Result, currentResult);
      block.AddOp(selectResult);
      currentResult = selectResult.Result;
    }

    valueMap[tryCallOp.ErrorFlag] = currentErrorFlag;
    // String/char-backed enums store ordinals at runtime
    valueMap[tryCallOp.Result!] = currentResult;
  }

  /// <summary>
  /// Lowers EnumType.fromName(nameArg, ...associatedArgs) inline as a comparison chain.
  /// Compares input string against each case name using length check + memcmp.
  /// For associated-value enums with compile-time literal name: constructs the full enum.
  /// For associated-value enums with dynamic name: only matches cases without associated values.
  /// </summary>
  private static void LowerEnumFromName(
    MaxonTryCallOp tryCallOp,
    IrEnumType enumType,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps) {

    var nameArg = tryCallOp.Args[0];
    // Envelope collapse: the String IS its __ManagedMemory, so buffer and length are read
    // straight off the value at offsets 0 and 8 — no nested managed pointer to chase.
    var nameStructName = ((StdHeapPtr)valueMap[nameArg]).VarName!;
    var nameBuf = (StdI64)EmitStructFieldLoad(block, nameStructName, ManagedFieldBuffer, IrType.I64, varTypes);
    var nameLen = (StdI64)EmitStructFieldLoad(block, nameStructName, ManagedFieldLength, IrType.I64, varTypes);

    bool hasAssociatedValues = enumType.HasAssociatedValues;
    bool hasExtraArgs = tryCallOp.Args.Count > 1;

    if (hasAssociatedValues) {
      // For associated-value enums, construct as flat struct (tag + payload)
      LowerEnumFromNameAssociated(tryCallOp, enumType, block, valueMap, varTypes,
        nameBuf, nameLen, hasExtraArgs, temps: temps);
    } else {
      // Simple/raw-value enum: result is an ordinal/raw value
      LowerEnumFromNameSimple(tryCallOp, enumType, block, valueMap, varTypes, nameBuf, nameLen);
    }
  }

  private static void LowerEnumFromNameSimple(
    MaxonTryCallOp tryCallOp,
    IrEnumType enumType,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    StdI64 nameBuf, StdI64 nameLen) {

    var noMatchFlag = new StdConstI64Op(1);
    block.AddOp(noMatchFlag);
    var defaultResult = new StdConstI64Op(0);
    block.AddOp(defaultResult);
    StdI64 currentErrorFlag = noMatchFlag.Result;
    StdI64 currentResult = defaultResult.Result;

    foreach (var enumCase in enumType.Cases) {
      var rdataLabel = $"__enum_fn_{enumType.Name}_{enumCase.Name}_{NextRdataId()}";
      var (caseBuf, caseLen) = EmitRdataLiteral(enumCase.Name, rdataLabel, block, _resultModule!);
      var isMatch = EmitStringEquals(nameBuf, nameLen, caseBuf, caseLen, block);

      var zeroFlag = new StdConstI64Op(0);
      block.AddOp(zeroFlag);
      var selectFlag = new StdSelectI64Op(isMatch, zeroFlag.Result, currentErrorFlag);
      block.AddOp(selectFlag);
      currentErrorFlag = selectFlag.Result;

      long runtimeValue = enumType.BackingType == IrType.I64
        ? (long)enumCase.RawValue!
        : enumCase.Ordinal;
      var resultConst = new StdConstI64Op(runtimeValue);
      block.AddOp(resultConst);
      var selectResult = new StdSelectI64Op(isMatch, resultConst.Result, currentResult);
      block.AddOp(selectResult);
      currentResult = selectResult.Result;
    }

    valueMap[tryCallOp.ErrorFlag] = currentErrorFlag;

    if (enumType.BackingType == IrType.F64) {
      // Float-backed fromName: convert ordinal to float via i64 bit pattern select chain,
      // then reinterpret the bits as f64 through a stack variable
      var bitsVarName = $"__enum_fn_bits_{IrContext.Current.NextId()}";
      var defaultBits = new StdConstI64Op(0);
      block.AddOp(defaultBits);
      StdI64 currentBits = defaultBits.Result;
      foreach (var enumCase in enumType.Cases) {
        long floatBits = BitConverter.DoubleToInt64Bits((double)enumCase.RawValue!);
        var caseBitsConst = new StdConstI64Op(floatBits);
        block.AddOp(caseBitsConst);
        var ordCheckConst = new StdConstI64Op(enumCase.Ordinal);
        block.AddOp(ordCheckConst);
        var cmpOrdConst = new StdCmpI64Op("eq", currentResult, ordCheckConst.Result);
        block.AddOp(cmpOrdConst);
        var selectBits = new StdSelectI64Op(cmpOrdConst.Result, caseBitsConst.Result, currentBits);
        block.AddOp(selectBits);
        currentBits = selectBits.Result;
      }
      // Store as i64, then load as f64 (reinterpret via same stack slot)
      EmitStore(block, currentBits, bitsVarName, varTypes);
      varTypes[bitsVarName] = "f64";
      var floatResult = (StdF64)EmitLoad(block, bitsVarName, varTypes);
      valueMap[tryCallOp.Result!] = floatResult;
    } else {
      valueMap[tryCallOp.Result!] = currentResult;
    }
  }

  private static void LowerEnumFromNameAssociated(
    MaxonTryCallOp tryCallOp,
    IrEnumType enumType,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    StdI64 nameBuf, StdI64 nameLen,
    bool hasExtraArgs,
    VarRegistry temps) {

    var tempName = temps.CreateTemp("enum", tryCallOp.Result!.Id, enumType.Name, OwnershipFlags.None);
    int maxPayload = GetMaxFlatPayloadSlots(enumType);
    int heapSize = UnionPayloadOffset(maxPayload);
    var enumPtr = EmitAlloc(block, heapSize, enumType.Name, scopeName: _currentFuncName);
    EmitStore(block, enumPtr, tempName, varTypes);

    // Initialize tag=0 and zero payload slots on the heap
    var defaultTag = new StdConstI64Op(0);
    block.AddOp(defaultTag);
    block.AddOp(new StdStoreIndirectOp(defaultTag.Result, enumPtr, UnionFieldTag, IrType.I64));
    for (int i = 0; i < maxPayload; i++) {
      var zeroPayload = new StdConstI64Op(0);
      block.AddOp(zeroPayload);
      block.AddOp(new StdStoreIndirectOp(zeroPayload.Result, enumPtr, UnionPayloadOffset(i), IrType.I64));
    }

    var noMatchFlag = new StdConstI64Op(1);
    block.AddOp(noMatchFlag);
    StdI64 currentErrorFlag = noMatchFlag.Result;

    // How many payloads the CALL supplies. `Args[0]` is the name; the rest are the payloads, and
    // `Args[1 + ai]` below reads them positionally.
    int suppliedPayloadCount = tryCallOp.Args.Count - 1;

    foreach (var enumCase in enumType.Cases) {
      bool caseHasAssocValues = enumCase.AssociatedValues is { Count: > 0 };

      // ⛔ A CASE THIS CALL CANNOT POSSIBLY SELECT IS SKIPPED, AND SKIPPING IT ON ARITY IS WHAT
      // KEEPS `Args[1 + ai]` IN BOUNDS. The parser holds the NAMED case to its own arity, so the
      // one case that can match always has exactly `suppliedPayloadCount` payloads; every case with
      // a different count is unreachable at run time whatever the name compares equal to. Asking
      // only "were there extra args at all" walked those cases anyway and indexed past the end of
      // the argument list: measured, a union carrying both `titled(t String)` and
      // `pair(a String, b String)` turned `fromName("titled", s)` into
      // `error E9001: Lowering function 'main' failed: Index was out of range` — a raw .NET
      // exception with a three-frame trace printed at the user, for a program that is legal.
      if (caseHasAssocValues && enumCase.AssociatedValues!.Count != suppliedPayloadCount) continue;

      var rdataLabel = $"__enum_fna_{enumType.Name}_{enumCase.Name}_{NextRdataId()}";
      var (caseBuf, caseLen) = EmitRdataLiteral(enumCase.Name, rdataLabel, block, _resultModule!);
      var isMatch = EmitStringEquals(nameBuf, nameLen, caseBuf, caseLen, block);

      var zeroFlag = new StdConstI64Op(0);
      block.AddOp(zeroFlag);
      var selectFlag = new StdSelectI64Op(isMatch, zeroFlag.Result, currentErrorFlag);
      block.AddOp(selectFlag);
      currentErrorFlag = selectFlag.Result;

      // On match, set the tag via indirect load/select/store on the heap
      var tagConst = new StdConstI64Op(enumCase.TagValue);
      block.AddOp(tagConst);
      var currentTag = new StdLoadIndirectOp(enumPtr, UnionFieldTag, IrType.I64);
      block.AddOp(currentTag);
      var selectTag = new StdSelectI64Op(isMatch, tagConst.Result, (StdI64)currentTag.Result);
      block.AddOp(selectTag);
      block.AddOp(new StdStoreIndirectOp(selectTag.Result, enumPtr, UnionFieldTag, IrType.I64));

      if (hasExtraArgs && caseHasAssocValues) {
        for (int ai = 0; ai < enumCase.AssociatedValues!.Count; ai++) {
          var avArg = tryCallOp.Args[1 + ai];
          var avSlotValue = valueMap[avArg];

          // ⛔ A MANAGED PAYLOAD IS A SYMBOLIC HANDLE, NOT AN SSA VALUE, AND USING IT AS ONE WROTE
          // AN UNRELATED NUMBER INTO THE SLOT. An `StdHeapPtr` carries the NAME of the variable the
          // argument lowering stored the pointer in; its id belongs to the Maxon value space, so as
          // a `select` operand it aliases whatever Std value happens to share that id. MEASURED for
          // `Named.fromName("titled", <String>)`: the operand printed as `%21` — the no-match flag —
          // and the slot was written with the CONSTANT 1, which the first `mm_incref` through the
          // payload then dereferenced. The direct construct (`MaxonEnumConstructOp`) has always
          // LOADED the pointer first; this site has to do the same.
          var avManagedPtr = avSlotValue as StdHeapPtr;

          StdI64 avStdVal;
          if (avManagedPtr != null) {
            var payloadVarName = avManagedPtr.VarName
              ?? throw new InvalidOperationException(
                $"union payload slot: the managed payload of '{enumType.Name}.{enumCase.Name}' "
                + "arrived as an StdHeapPtr with no variable name, so there is no pointer to load. "
                + "Every managed argument is stored to a variable before it reaches a construct.");
            avStdVal = (StdI64)EmitLoad(block, payloadVarName, varTypes);
          } else {
            // Branchless case selection makes this a SELECT rather than a store, and a select is an
            // i64 operation — so a scalar payload is widened into the slot's representation here
            // instead of being stored at its own type the way the direct construct stores it.
            avStdVal = EmitPayloadAsSlotBits(block, avSlotValue);
          }

          int byteOffset = UnionPayloadOffset(ai);
          var currentPayload = new StdLoadIndirectOp(enumPtr, byteOffset, IrType.I64);
          block.AddOp(currentPayload);
          var selectPayload = new StdSelectI64Op(isMatch, avStdVal, (StdI64)currentPayload.Result);
          block.AddOp(selectPayload);
          block.AddOp(new StdStoreIndirectOp(selectPayload.Result, enumPtr, byteOffset, IrType.I64));

          // ⭐ AND THE UNION HAS TO TAKE A REFERENCE TO WHAT IT NOW POINTS AT — the obligation the
          // direct construct discharges with a plain `EmitIncrefValue`, because it knows its case at
          // COMPILE time. This site picks the case at RUNTIME, so the reference is conditional on
          // the same `isMatch` the slot's own select is: select NULL on the other arm and go through
          // the null-guarded call, which keeps the site branchless. An unconditional incref would
          // leak the argument once per non-matching case — and on a `fromName` that finds no case at
          // all, that is every case there is.
          if (avManagedPtr != null) {
            var noRetain = new StdConstI64Op(0);
            block.AddOp(noRetain);
            var retained = new StdSelectI64Op(isMatch, avStdVal, noRetain.Result);
            block.AddOp(retained);
            EmitIncrefValueIfNonnull(block, retained.Result, scopeName: _currentFuncName);
          }
        }
      }
    }

    valueMap[tryCallOp.ErrorFlag] = currentErrorFlag;
    valueMap[tryCallOp.Result!] = new StdHeapPtr(enumPtr.Id, enumType.Name, tempName);
  }

  /// <summary>
  /// Lower call/try_call arguments for the standard calling convention.
  /// Struct args are passed as heap pointers (i64) directly.
  /// Associated-value enum args are packed into heap blocks and passed as pointers.
  /// </summary>
  // Materialize a stack-resident struct/interface argument as an i64 pointer:
  // LEA the variable's stack region, then convert the pointer to i64 so the
  // StdCallOp receives a real producer. `varName` may be null (no recorded
  // tag), in which case the conventional `__stk_<name>` tag is synthesized.
  private static StdI64 MaterializeStackPtrArg(IrBlock<StandardOp> block, string? varName) {
    var stackTag = _stackVarTags != null && varName != null && _stackVarTags.TryGetValue(varName, out var tag)
      ? tag : $"__stk_{varName}";
    var leaOp = new StdLeaOp(stackTag);
    block.AddOp(leaOp);
    var ptrOp = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrOp);
    return ptrOp.Result;
  }

  private static void FlattenCallArgs(
    List<MaxonValue> args,
    IrFunction<MaxonOp> calleeFunc,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    List<StdValue> newArgs,
    string calleeName,
    Dictionary<int, string>? fnEnvVarNames = null,
    Dictionary<int, StdI64>? fnEnvDirectValues = null,
    List<string?>? argVarNames = null) {
    bool calleeIsEnumInstance = IsEnumInstanceMethod(calleeFunc);

    for (int i = 0; i < args.Count; i++) {
      var arg = args[i];

      // Pass-by-reference: if this param is reassigned by the callee, pass address instead of value
      if (calleeFunc.ReassignedParams != null && i < calleeFunc.ParamNames.Count
          && calleeFunc.ReassignedParams.Contains(calleeFunc.ParamNames[i])
          && calleeFunc.ParamNames[i] != "self") {
        string? argVarName = null;
        if (valueMap.TryGetValue(arg, out var svnSv) && svnSv is StdHeapPtr svnHp) argVarName = svnHp.VarName!;
        else if (argVarNames != null && i < argVarNames.Count) argVarName = argVarNames[i];

        if (argVarName != null && varTypes.ContainsKey(argVarName)) {
          // If this variable is itself a ref param, forward the original pointer
          // so writes propagate all the way back to the original caller
          if (_refParamPtrVars != null && _refParamPtrVars.TryGetValue(argVarName, out var refPtrVar)) {
            var refPtr = EmitLoad(block, refPtrVar, varTypes);
            newArgs.Add(refPtr);
          } else {
            var leaOp = new StdLeaOp(argVarName);
            block.AddOp(leaOp);
            var ptrToI64 = new StdPtrToI64Op(leaOp.Result);
            block.AddOp(ptrToI64);
            newArgs.Add(ptrToI64.Result);
          }
        } else {
          // Literal/expression: create a temporary so the callee has a valid address to read from
          var tempName = $"__ref_temp_{IrContext.Current.NextId()}";
          if (valueMap.TryGetValue(arg, out var argVal)) {
            EmitStore(block, argVal, tempName, varTypes);
          } else if (valueMap.TryGetValue(arg, out var snSv) && snSv is StdHeapPtr snHp) {
            var hp = EmitLoad(block, snHp.VarName!, varTypes);
            EmitStore(block, hp, tempName, varTypes);
          } else {
            throw new InvalidOperationException($"Cannot resolve arg for pass-by-ref temp in call to '{calleeName}', arg {i}");
          }
          var leaOp = new StdLeaOp(tempName);
          block.AddOp(leaOp);
          var ptrToI64 = new StdPtrToI64Op(leaOp.Result);
          block.AddOp(ptrToI64);
          newArgs.Add(ptrToI64.Result);
        }
        continue;
      }

      if (calleeIsEnumInstance && i == 0) {
        // self for an enum/union method. Simple enums forward the scalar
        // value as-is; associated-value unions match the rule used for
        // assoc-value enum args below — load the heap pointer so the
        // callee receives an i64 pointer to the receiver block.
        //
        // The decision hinges on whether the receiver flowed through a heap
        // pointer (union with associated values) or a scalar (simple enum).
        // We prefer the runtime shape (`epSelfSv is StdHeapPtr`) over
        // `selfEnumType.HasAssociatedValues` because the stdlib cache can
        // restore the param's IrEnumType as a bare stub (no Cases), making
        // `HasAssociatedValues` falsely report `false` even when the caller
        // really did stage a heap pointer for the receiver. Trusting the
        // runtime shape keeps the load path active and avoids handing a raw
        // MaxonValue-ID-shaped StdHeapPtr to the StdCallOp.
        if (valueMap.TryGetValue(arg, out var epSelfSv) && epSelfSv is StdHeapPtr epSelfHp) {
          var selfHeapPtr = EmitLoad(block, epSelfHp.VarName!, varTypes);
          newArgs.Add(selfHeapPtr);
        } else {
          newArgs.Add(valueMap[arg]);
        }
      } else if (calleeFunc.ParamTypes[i] is IrEnumType enumArgType && enumArgType.HasAssociatedValues
                 && valueMap.TryGetValue(arg, out var epSv) && epSv is StdHeapPtr epHp) {
        // Associated-value enum: already a heap pointer, just load it
        var heapPtr = EmitLoad(block, epHp.VarName!, varTypes);
        newArgs.Add(heapPtr);
      } else if (calleeFunc.ParamTypes[i] is IrEnumType) {
        if (valueMap.TryGetValue(arg, out var enumVal)) {
          newArgs.Add(enumVal);
        } else if (valueMap.TryGetValue(arg, out var etSv) && etSv is StdHeapPtr etHp) {
          // Simple enum constructed via enum_construct — load its tag
          var tagVal = EmitLoad(block, $"{etHp.VarName!}.__tag", varTypes);
          newArgs.Add(tagVal);
        } else {
          throw new InvalidOperationException($"Enum arg %{arg.Id} not found in valueMap as StdHeapPtr for call to '{calleeName}'");
        }
      } else if (valueMap.TryGetValue(arg, out var deferredSv) && deferredSv is StdStackPtr deferredSp && deferredSp.VarName != null) {
        // Deferred stack-struct var-ref. ParamTypes[i] may be a stale IrTypeParameterType
        // stub after monomorphization + stdlib-cache round-trip (e.g. Array<SortRun>.push(value Element)),
        // so trust the runtime StdValue shape over the cached static type — the same reasoning used
        // for enum receivers above. Materialize via LEA so the StdCallOp gets a real producer instead
        // of an orphan deferred-ref id. (StdStackPtr precedes StdHeapPtr: it is a subclass.)
        newArgs.Add(MaterializeStackPtrArg(block, deferredSp.VarName));
      } else if (valueMap.TryGetValue(arg, out var deferredHpSv) && deferredHpSv is StdHeapPtr deferredHp && deferredHp.VarName != null) {
        // Deferred heap-struct var-ref (the glidesort stack.push(newRun) crash case). Same rationale
        // as the stack branch above; materialize via EmitLoad like the struct heap branch below.
        newArgs.Add(EmitLoad(block, deferredHp.VarName, varTypes));
      } else if (calleeFunc.ParamTypes[i] is IrStructType or IrInterfaceType && valueMap.TryGetValue(arg, out var asSv) && asSv is StdStackPtr asSp) {
        // Stack struct/interface arg: emit LEA to get pointer to the stack region
        newArgs.Add(MaterializeStackPtrArg(block, asSp.VarName));
      } else if (calleeFunc.ParamTypes[i] is IrStructType or IrInterfaceType && valueMap.TryGetValue(arg, out var asHpSv) && asHpSv is StdHeapPtr asHp) {
        // Struct/interface arg: pass the heap pointer directly
        if (asHp.VarName == null)
          throw new InvalidOperationException($"FlattenCallArgs: StdHeapPtr for arg %{arg.Id} (param '{calleeFunc.ParamNames[i]}') has null VarName in call to '{calleeName}'. TypeName={asHp.TypeName}, StdId={asHp.Id}");
        var heapPtr = EmitLoad(block, asHp.VarName, varTypes);
        newArgs.Add(heapPtr);
      } else if (calleeFunc.ParamTypes[i] is IrStructType or IrInterfaceType && valueMap.TryGetValue(arg, out var rawPtrValue)) {
        // Struct/interface arg from managed memory get — the value is already a pointer
        newArgs.Add(rawPtrValue);
      } else if (calleeFunc.ParamTypes[i] is IrFunctionType) {
        // Function-typed arg: pass fn_ptr + env_ptr, the two halves of one value.
        var fnStdVal = valueMap[arg];
        newArgs.Add(fnStdVal);
        newArgs.Add(ResolveClosureEnvArg(fnStdVal.Id, block, varTypes, fnEnvVarNames, fnEnvDirectValues));
      } else if (calleeFunc.ParamTypes[i] is not IrStructType and not IrInterfaceType and not IrEnumType) {
        newArgs.Add(valueMap[arg]);
      } else {
        throw new InvalidOperationException($"Unhandled call argument type: {calleeFunc.ParamTypes[i].GetType().Name} for arg {i} in call to '{calleeName}'");
      }
    }
  }
}
