using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
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
    // Input is a String or Character managed struct - load _managed heap pointer, then buffer and length
    var inputStructName = ((StdHeapPtr)valueMap[inputArg]).VarName!;
    var inputManagedPtr = (StdI64)EmitStructFieldLoad(block, inputStructName, 0, IrType.I64, varTypes);
    var inputManagedVar = $"__frv_managed_{IrContext.Current.NextId()}";
    EmitStore(block, inputManagedPtr, inputManagedVar, varTypes);
    var inputBuf = (StdI64)EmitStructFieldLoad(block, inputManagedVar, ManagedFieldBuffer, IrType.I64, varTypes);
    var inputLen = (StdI64)EmitStructFieldLoad(block, inputManagedVar, ManagedFieldLength, IrType.I64, varTypes);

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
    // Name is always a String managed struct - load _managed heap pointer, then buffer and length
    var nameStructName = ((StdHeapPtr)valueMap[nameArg]).VarName!;
    var nameManagedPtr = (StdI64)EmitStructFieldLoad(block, nameStructName, 0, IrType.I64, varTypes);
    var nameManagedVar = $"__fn_managed_{IrContext.Current.NextId()}";
    EmitStore(block, nameManagedPtr, nameManagedVar, varTypes);
    var nameBuf = (StdI64)EmitStructFieldLoad(block, nameManagedVar, ManagedFieldBuffer, IrType.I64, varTypes);
    var nameLen = (StdI64)EmitStructFieldLoad(block, nameManagedVar, ManagedFieldLength, IrType.I64, varTypes);

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

    // Heap-allocate the enum: [tag:i64 @ 0, payload_0:i64 @ 8, ...]
    var tempName = temps.CreateTemp("enum", tryCallOp.Result!.Id, enumType.Name, OwnershipFlags.None);
    int maxPayload = GetMaxFlatPayloadSlots(enumType);
    int heapSize = 8 + maxPayload * 8;
    var enumPtr = EmitAlloc(block, heapSize, enumType.Name, scopeName: _currentFuncName);
    EmitStore(block, enumPtr, tempName, varTypes);

    // Initialize tag=0 and zero payload slots on the heap
    var defaultTag = new StdConstI64Op(0);
    block.AddOp(defaultTag);
    block.AddOp(new StdStoreIndirectOp(defaultTag.Result, enumPtr, 0, IrType.I64));
    for (int i = 0; i < maxPayload; i++) {
      var zeroPayload = new StdConstI64Op(0);
      block.AddOp(zeroPayload);
      block.AddOp(new StdStoreIndirectOp(zeroPayload.Result, enumPtr, 8 + i * 8, IrType.I64));
    }

    var noMatchFlag = new StdConstI64Op(1);
    block.AddOp(noMatchFlag);
    StdI64 currentErrorFlag = noMatchFlag.Result;

    foreach (var enumCase in enumType.Cases) {
      bool caseHasAssocValues = enumCase.AssociatedValues is { Count: > 0 };

      // For dynamic name (no extra args), skip cases that need associated values
      if (!hasExtraArgs && caseHasAssocValues) continue;

      var rdataLabel = $"__enum_fna_{enumType.Name}_{enumCase.Name}_{NextRdataId()}";
      var (caseBuf, caseLen) = EmitRdataLiteral(enumCase.Name, rdataLabel, block, _resultModule!);
      var isMatch = EmitStringEquals(nameBuf, nameLen, caseBuf, caseLen, block);

      var zeroFlag = new StdConstI64Op(0);
      block.AddOp(zeroFlag);
      var selectFlag = new StdSelectI64Op(isMatch, zeroFlag.Result, currentErrorFlag);
      block.AddOp(selectFlag);
      currentErrorFlag = selectFlag.Result;

      // On match, set the tag via indirect load/select/store on the heap
      var tagConst = new StdConstI64Op(enumCase.RawValue is long rv ? rv : enumCase.Ordinal);
      block.AddOp(tagConst);
      var currentTag = new StdLoadIndirectOp(enumPtr, 0, IrType.I64);
      block.AddOp(currentTag);
      var selectTag = new StdSelectI64Op(isMatch, tagConst.Result, (StdI64)currentTag.Result);
      block.AddOp(selectTag);
      block.AddOp(new StdStoreIndirectOp(selectTag.Result, enumPtr, 0, IrType.I64));

      if (hasExtraArgs && caseHasAssocValues) {
        for (int ai = 0; ai < enumCase.AssociatedValues!.Count; ai++) {
          var avArg = tryCallOp.Args[1 + ai];
          var avStdVal = valueMap[avArg];
          int byteOffset = 8 + ai * 8;
          var currentPayload = new StdLoadIndirectOp(enumPtr, byteOffset, IrType.I64);
          block.AddOp(currentPayload);
          var selectPayload = new StdSelectI64Op(isMatch, (StdI64)avStdVal, (StdI64)currentPayload.Result);
          block.AddOp(selectPayload);
          block.AddOp(new StdStoreIndirectOp(selectPayload.Result, enumPtr, byteOffset, IrType.I64));
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
  private static void FlattenCallArgs(
    List<MaxonValue> args,
    IrFunction<MaxonOp> calleeFunc,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    List<StdValue> newArgs,
    string calleeName,
    Dictionary<int, string>? fnEnvVarNames = null,
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
        newArgs.Add(valueMap[arg]);
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
      } else if (calleeFunc.ParamTypes[i] is IrStructType or IrInterfaceType && valueMap.TryGetValue(arg, out var asSv) && asSv is StdStackPtr asSp) {
        // Stack struct/interface arg: emit LEA to get pointer to the stack region
        var stackTag = _stackVarTags != null && asSp.VarName != null && _stackVarTags.TryGetValue(asSp.VarName, out var tag)
          ? tag : $"__stk_{asSp.VarName}";
        var leaOp = new StdLeaOp(stackTag);
        block.AddOp(leaOp);
        var ptrOp = new StdPtrToI64Op(leaOp.Result);
        block.AddOp(ptrOp);
        newArgs.Add(ptrOp.Result);
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
        // Function-typed arg: pass fn_ptr + env_ptr
        newArgs.Add(valueMap[arg]);
        // Look up and pass the associated env_ptr
        var fnStdVal = valueMap[arg];
        if (fnEnvVarNames != null && fnEnvVarNames.TryGetValue(fnStdVal.Id, out var envVarName)) {
          var envPtr = EmitLoad(block, envVarName, varTypes);
          newArgs.Add(envPtr);
        } else {
          var zeroConst = new StdConstI64Op(0);
          block.AddOp(zeroConst);
          newArgs.Add(zeroConst.Result);
        }
      } else if (calleeFunc.ParamTypes[i] is not IrStructType and not IrInterfaceType and not IrEnumType) {
        newArgs.Add(valueMap[arg]);
      } else {
        throw new InvalidOperationException($"Unhandled call argument type: {calleeFunc.ParamTypes[i].GetType().Name} for arg {i} in call to '{calleeName}'");
      }
    }
  }
}
