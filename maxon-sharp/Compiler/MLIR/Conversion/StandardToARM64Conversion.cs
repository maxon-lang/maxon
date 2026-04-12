using System.Globalization;
using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static class StandardToARM64Conversion {
  public static IrModule<ARM64Op> Run(IrModule<StandardOp> module) {
    var result = new IrModule<ARM64Op> {
      EntryFunctionName = module.EntryFunctionName
    };
    result.RdataEntries.AddRange(module.RdataEntries);
    result.SymdataEntries.AddRange(module.SymdataEntries);
    result.UcddataEntries.AddRange(module.UcddataEntries);
    result.Globals.AddRange(module.Globals);
    result.TagTable = module.TagTable;
    result.TagNames = module.TagNames;
    foreach (var (k, v) in module.TypeDefs) result.TypeDefs[k] = v;

    foreach (var func in module.Functions) {
      var newFunc = ConvertFunction(func, result);
      result.AddFunction(newFunc);
    }

    return result;
  }

  private static IrFunction<ARM64Op> ConvertFunction(IrFunction<StandardOp> func, IrModule<ARM64Op> outputModule) {
    var newFunc = new IrFunction<ARM64Op>(func.Name, func.ParamNames, func.ParamTypes, func.ReturnType, func.ThrowsType) { IsStdlib = func.IsStdlib };

    // Pre-scan: find which variables are actually loaded
    var loadedVariables = new HashSet<string>();
    var leaVariables = new HashSet<string>();
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        switch (op) {
          case ILoadOp load: loadedVariables.Add(load.VarName); break;
          case StdLeaOp lea: leaVariables.Add(lea.VarName); break;
        }
      }
    }
    foreach (var leaVar in leaVariables)
      loadedVariables.Add(leaVar);
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is IStoreOp store) {
          var dotIdx = store.VarName.IndexOf('.');
          if (dotIdx >= 0 && leaVariables.Contains(store.VarName[..dotIdx]))
            loadedVariables.Add(store.VarName);
        } else if (op is StdBulkZeroOp bulkZero && leaVariables.Contains(bulkZero.Tag)) {
          foreach (var fieldName in bulkZero.FieldNames())
            loadedVariables.Add(fieldName);
        }
      }
    }

    // Calculate stack frame — variable offsets (negative from frame pointer)
    var varOffsets = new Dictionary<string, int>();
    int varStackSize = 0;
    var deadStoreOps = new HashSet<StandardOp>();
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is StdBulkZeroOp bulkZero) {
          foreach (var fieldName in bulkZero.FieldNames()) {
            if (!loadedVariables.Contains(fieldName)) continue;
            if (!varOffsets.ContainsKey(fieldName)) {
              varStackSize += 8;
              varOffsets[fieldName] = -varStackSize;
            }
          }
          continue;
        }
        if (op is not IStoreOp storeOp) continue;
        if (!loadedVariables.Contains(storeOp.VarName)) {
          deadStoreOps.Add(op);
          continue;
        }
        if (!varOffsets.ContainsKey(storeOp.VarName)) {
          varStackSize += 8;
          varOffsets[storeOp.VarName] = -varStackSize;
        }
      }
    }

    // Pre-scan: compute last use index for each StdValue (for dead-value freeing)
    var lastUseOfValue = new Dictionary<StdValue, int>();
    var sinkOnlyValues = new HashSet<StdValue>();
    var usedByNonSink = new HashSet<StdValue>();
    int scanIdx = 0;
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        foreach (var val in op.ReadValues) {
          lastUseOfValue[val] = scanIdx;
          if (op is StdReturnOp or StdCallOp or StdCallRuntimeOp or StdCallRuntimeIfNonnullOp or StdTryCallOp or StdTryCallRuntimeOp)
            sinkOnlyValues.Add(val);
          else
            usedByNonSink.Add(val);
        }
        scanIdx++;
      }
    }
    sinkOnlyValues.ExceptWith(usedByNonSink);

    // Track float constants for rdata deduplication
    var floatConstants = new Dictionary<double, string>();
    var float32Constants = new Dictionary<float, string>();

    var regManager = new ARM64RegisterManager {
      DeferredValues = sinkOnlyValues
    };
    regManager.SetSpillBaseOffset(-varStackSize);

    var sourceBlocks = func.Body.Blocks.ToList();
    int currentOpIndex = 0;

    var needsCrossBlockSpill = BlockAnalysis.FindCrossBlockSpillBlocks(sourceBlocks);

    var divergingBlocks = BlockAnalysis.FindDivergingBlocks(sourceBlocks);

    IrBlock<ARM64Op>? prevBlock = null;
    int prevBlockIdx = -1;
    RegisterManagerBase<ARM64Register, ARM64FloatRegister, ARM64Op>.RegisterSnapshot? savedRegState = null;
    for (int blockIdx = 0; blockIdx < sourceBlocks.Count; blockIdx++) {
      var srcBlock = sourceBlocks[blockIdx];
      var armBlock = newFunc.Body.AddBlock(srcBlock.Name);

      if (blockIdx == 0 || prevBlock == null) {
        regManager.Reset();
      } else if (divergingBlocks.Contains(blockIdx)) {
        // Diverging block (noreturn) — save register state so it can be restored
        // for the next non-diverging block, avoiding unnecessary spills on the happy path.
        savedRegState ??= regManager.SaveState();
        regManager.Reset();
      } else if (savedRegState != null) {
        // First non-diverging block after diverging block(s) — restore register
        // state from before the diverging sequence.
        regManager.RestoreState(savedRegState);
        savedRegState = null;
      } else if (needsCrossBlockSpill.Contains(prevBlockIdx)) {
        regManager.ResetForBlockTransition(prevBlock!);
      } else {
        regManager.Reset();
      }

      // Entry block: register parameters
      if (blockIdx == 0) {
        // Compute type-specific param indices (GPR and FP args use separate register sequences)
        var allParamOps = srcBlock.Operations.OfType<StdParamOp>().OrderBy(p => p.Index).ToList();
        int gprIdx = 0, fpIdx = 0;
        var typeSpecificIndices = new Dictionary<int, int>();
        foreach (var p in allParamOps) {
          if (p.Result is StdF64 or StdF32)
            typeSpecificIndices[p.Index] = fpIdx++;
          else
            typeSpecificIndices[p.Index] = gprIdx++;
        }
        foreach (var op in srcBlock.Operations) {
          if (op is StdParamOp paramOp) {
            regManager.NoteParam(paramOp.Result, paramOp.Index,
              typeSpecificIndices.GetValueOrDefault(paramOp.Index, paramOp.Index), armBlock);
          }
        }
      }

      foreach (var op in srcBlock.Operations) {
        if (deadStoreOps.Contains(op)) { currentOpIndex++; continue; }
        if (op is StdParamOp && blockIdx == 0) { currentOpIndex++; continue; }
        ConvertOp(op, armBlock, varOffsets, regManager, outputModule, floatConstants, float32Constants,
          func.Name, lastUseOfValue, currentOpIndex, func.ThrowsType != null);
        FreeDeadValues(regManager, lastUseOfValue, currentOpIndex, op.ReadValues);
        regManager.AdvanceOp();
        currentOpIndex++;
      }

      prevBlock = armBlock;
      prevBlockIdx = blockIdx;
    }

    // Insert prologue/epilogue. ARM64 always needs STP/LDP for x29/x30.
    int stackSize = regManager.TotalStackSize;
    // Frame layout: [x29+0]=saved x29, [x29+8]=saved x30, locals below x29 at negative offsets.
    // totalFrameSize = 16 (for x29/x30) + localsSize (for variables + spills)
    int localsSize = AlignUp(stackSize + varStackSize, 16);
    int totalFrameSize = 16 + localsSize;
    // Any function that makes calls needs a frame for stack traces.
    // Functions with stack-passed params (>8 GPR args) also need a frame so [X29, #16] resolves correctly.
    bool hasCalls = newFunc.Body.Blocks.Any(b => b.Operations.Any(op => op is ARM64BranchLinkOp or ARM64BranchLinkRegOp));
    bool hasStackParams = func.ParamTypes.Count > ARM64RegisterManager.RegisterParamCount;
    if (!hasCalls && !hasStackParams && localsSize == 0) totalFrameSize = 0;
    if (totalFrameSize > 0) {
      var entryBlock = newFunc.Body.Blocks.First();
      entryBlock.Operations.Insert(0, new ARM64PrologueOp(totalFrameSize));
      // Update all epilogue ops with the computed stack size
      foreach (var block in newFunc.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is ARM64EpilogueOp epilogueOp) {
            epilogueOp.StackSize = totalFrameSize;
          }
        }
      }
    } else {
      foreach (var block in newFunc.Body.Blocks) {
        block.Operations.RemoveAll(op => op is ARM64EpilogueOp);
      }
    }

    return newFunc;
  }

  private static void ConvertOp(
      StandardOp op,
      IrBlock<ARM64Op> block,
      Dictionary<string, int> varOffsets,
      ARM64RegisterManager regManager,
      IrModule<ARM64Op> outputModule,
      Dictionary<double, string> floatConstants,
      Dictionary<float, string> float32Constants,
      string funcName,
      Dictionary<StdValue, int> lastUseOfValue,
      int currentOpIndex,
      bool isThrowingFunc) {
    switch (op) {
      // === Constants ===
      case StdConstI64Op constOp:
        regManager.EmitLoadImmediate(constOp.Result, constOp.Value, block);
        break;

      case StdConstI32Op constOp32:
        regManager.EmitLoadImmediate(constOp32.Result, constOp32.Value, block);
        break;

      case StdConstI1Op constBool:
        regManager.EmitLoadImmediate(constBool.Result, constBool.Value ? 1 : 0, block);
        break;

      // === Float Constants ===
      case StdConstF64Op constF64: {
        var label = GetOrCreateFloatLabel(constF64.Value, outputModule, floatConstants);
        regManager.EmitFpLoadFromRdata(constF64.Result, label, FloatPrecision.F64, block);
        break;
      }

      case StdConstF32Op constF32: {
        var label = GetOrCreateFloat32Label(constF32.Value, outputModule, float32Constants);
        regManager.EmitFpLoadFromRdata(constF32.Result, label, FloatPrecision.F32, block);
        break;
      }

      // === Store/Load (I64) ===
      case StdStoreI64Op store64:
        if (varOffsets.TryGetValue(store64.VarName, out var storeOffset64))
          regManager.EmitStoreToStack(store64.Value, storeOffset64, 8, block);
        break;

      case StdLoadI64Op load64:
        if (varOffsets.TryGetValue(load64.VarName, out var loadOffset64))
          regManager.EmitLoadFromStack(load64.Result, loadOffset64, 8, block);
        break;

      // === Store/Load (I32) ===
      case StdStoreI32Op store32:
        if (varOffsets.TryGetValue(store32.VarName, out var storeOffset32))
          regManager.EmitStoreToStack(store32.Value, storeOffset32, 4, block);
        break;

      case StdLoadI32Op load32:
        if (varOffsets.TryGetValue(load32.VarName, out var loadOffset32))
          regManager.EmitLoadFromStack(load32.Result, loadOffset32, 4, block);
        break;

      // === Store/Load (I1 - bool) ===
      case StdStoreI1Op storeBool:
        if (varOffsets.TryGetValue(storeBool.VarName, out var storeOffsetBool))
          regManager.EmitStoreToStack(storeBool.Value, storeOffsetBool, 1, block);
        break;

      case StdLoadI1Op loadBool:
        if (varOffsets.TryGetValue(loadBool.VarName, out var loadOffsetBool))
          regManager.EmitLoadFromStack(loadBool.Result, loadOffsetBool, 1, block);
        break;

      // === Store/Load (Ptr) ===
      case StdStorePtrOp storePtr:
        if (varOffsets.TryGetValue(storePtr.VarName, out var storeOffsetPtr))
          regManager.EmitStoreToStack(storePtr.Value, storeOffsetPtr, 8, block);
        break;

      case StdLoadPtrOp loadPtr:
        if (varOffsets.TryGetValue(loadPtr.VarName, out var loadOffsetPtr))
          regManager.EmitLoadFromStack(loadPtr.Result, loadOffsetPtr, 8, block);
        break;

      // === Store/Load (F64) ===
      case StdStoreF64Op storeF64:
        if (varOffsets.TryGetValue(storeF64.VarName, out var storeOffsetF64))
          regManager.EmitFpStoreToStack(storeF64.Value, storeOffsetF64, FloatPrecision.F64, block);
        break;

      case StdLoadF64Op loadF64:
        if (varOffsets.TryGetValue(loadF64.VarName, out var loadOffsetF64))
          regManager.EmitFpLoadFromStack(loadF64.Result, loadOffsetF64, FloatPrecision.F64, block);
        break;

      // === Store/Load (F32) ===
      case StdStoreF32Op storeF32:
        if (varOffsets.TryGetValue(storeF32.VarName, out var storeOffsetF32))
          regManager.EmitFpStoreToStack(storeF32.Value, storeOffsetF32, FloatPrecision.F32, block);
        break;

      case StdLoadF32Op loadF32:
        if (varOffsets.TryGetValue(loadF32.VarName, out var loadOffsetF32))
          regManager.EmitFpLoadFromStack(loadF32.Result, loadOffsetF32, FloatPrecision.F32, block);
        break;

      // === Binary I64 ops ===
      case StdBinaryI64Op binOp:
        EmitBinaryOp(binOp.Lhs, binOp.Rhs, binOp.Result, binOp.Operator, regManager, block);
        break;

      // === Binary I32 ops ===
      case StdBinaryI32Op binOp32:
        EmitBinaryOp(binOp32.Lhs, binOp32.Rhs, binOp32.Result, binOp32.Operator, regManager, block);
        break;

      // === Binary I1 (boolean) ops ===
      case StdBinaryI1Op binOpBool:
        EmitBinaryOp(binOpBool.Lhs, binOpBool.Rhs, binOpBool.Result, binOpBool.Operator, regManager, block);
        break;

      // === Comparison I64 ===
      case StdCmpI64Op cmpOp:
        regManager.EmitIntegerCompare(cmpOp.Lhs, cmpOp.Rhs, block);
        regManager.EmitSetcc(cmpOp.Result, PredicateToCondition(cmpOp.Predicate), block);
        break;

      case StdCmpU64Op cmpU64Op:
        regManager.EmitIntegerCompare(cmpU64Op.Lhs, cmpU64Op.Rhs, block);
        regManager.EmitSetcc(cmpU64Op.Result, PredicateToCondition(cmpU64Op.Predicate), block);
        break;

      // === Comparison I32 ===
      case StdCmpI32Op cmpOp32:
        regManager.EmitIntegerCompare(cmpOp32.Lhs, cmpOp32.Rhs, block);
        regManager.EmitSetcc(cmpOp32.Result, PredicateToCondition(cmpOp32.Predicate), block);
        break;

      case StdCmpU32Op cmpU32Op:
        regManager.EmitIntegerCompare(cmpU32Op.Lhs, cmpU32Op.Rhs, block);
        regManager.EmitSetcc(cmpU32Op.Result, PredicateToCondition(cmpU32Op.Predicate), block);
        break;

      // === Comparison I1 ===
      case StdCmpI1Op cmpI1Op:
        regManager.EmitIntegerCompare(cmpI1Op.Lhs, cmpI1Op.Rhs, block);
        regManager.EmitSetcc(cmpI1Op.Result, PredicateToCondition(cmpI1Op.Predicate), block);
        break;

      // === Float binary ops ===
      case StdBinaryF64Op fBinOp:
        EmitFloatBinaryOp(fBinOp.Lhs, fBinOp.Rhs, fBinOp.Result, fBinOp.Operator, FloatPrecision.F64, regManager, block);
        break;

      case StdBinaryF32Op fBinOp32:
        EmitFloatBinaryOp(fBinOp32.Lhs, fBinOp32.Rhs, fBinOp32.Result, fBinOp32.Operator, FloatPrecision.F32, regManager, block);
        break;

      // === Float comparison ===
      case StdCmpF64Op fcmpOp:
        regManager.EmitFpCompare(fcmpOp.Lhs, fcmpOp.Rhs, FloatPrecision.F64, block);
        regManager.EmitSetcc(fcmpOp.Result, FloatPredicateToCondition(fcmpOp.Predicate), block);
        break;

      case StdCmpF32Op fcmpOp32:
        regManager.EmitFpCompare(fcmpOp32.Lhs, fcmpOp32.Rhs, FloatPrecision.F32, block);
        regManager.EmitSetcc(fcmpOp32.Result, FloatPredicateToCondition(fcmpOp32.Predicate), block);
        break;

      // === Float conversions ===
      case StdFpToSiOp fpToSi:
        regManager.EmitFpToInt(fpToSi.Input, fpToSi.Result, FloatPrecision.F64, block);
        break;

      case StdSiToFpOp siToFp:
        regManager.EmitIntToFp(siToFp.Input, siToFp.Result, FloatPrecision.F64, block);
        break;

      case StdFpToSiF32Op fpToSiF32:
        regManager.EmitFpToInt(fpToSiF32.Input, fpToSiF32.Result, FloatPrecision.F32, block);
        break;

      case StdSiToFpF32Op siToFpF32:
        regManager.EmitIntToFp(siToFpF32.Input, siToFpF32.Result, FloatPrecision.F32, block);
        break;

      case StdF64ToF32Op f64ToF32:
        regManager.EmitFcvt(f64ToF32.Input, f64ToF32.Result, FloatPrecision.F32, block);
        break;

      case StdF32ToF64Op f32ToF64:
        regManager.EmitFcvt(f32ToF64.Input, f32ToF64.Result, FloatPrecision.F64, block);
        break;

      // === Float unary ===
      case StdAbsF64Op absF64:
        regManager.EmitFpUnary(absF64.Input, absF64.Result, block,
          (d, s) => new ARM64FabsOp(d, s, FloatPrecision.F64));
        break;

      case StdAbsF32Op absF32:
        regManager.EmitFpUnary(absF32.Input, absF32.Result, block,
          (d, s) => new ARM64FabsOp(d, s, FloatPrecision.F32));
        break;

      case StdSqrtF64Op sqrtF64:
        regManager.EmitFpUnary(sqrtF64.Input, sqrtF64.Result, block,
          (d, s) => new ARM64FsqrtOp(d, s, FloatPrecision.F64));
        break;

      case StdSqrtF32Op sqrtF32:
        regManager.EmitFpUnary(sqrtF32.Input, sqrtF32.Result, block,
          (d, s) => new ARM64FsqrtOp(d, s, FloatPrecision.F32));
        break;

      // === Width conversions ===
      case StdExtI32ToI64Op extOp:
        if (extOp.SignExtend)
          regManager.EmitSignExtendI32ToI64(extOp.Input, extOp.Result, block);
        else
          regManager.EmitZeroExtendI32ToI64(extOp.Input, extOp.Result, block);
        break;

      case StdTruncI64ToI32Op truncOp:
        regManager.EmitTruncI64ToI32(truncOp.Input, truncOp.Result, block);
        break;

      // === Unsigned float conversions ===
      case StdUiToFpOp uiToFp:
        regManager.EmitUnsignedIntToFp(uiToFp.Input, uiToFp.Result, FloatPrecision.F64, block);
        break;

      case StdUiToFpI32Op uiToFpI32:
        regManager.EmitIntToFp(uiToFpI32.Input, uiToFpI32.Result, FloatPrecision.F64, block);
        break;

      case StdUiToFpF32Op uiToFpF32:
        regManager.EmitUnsignedIntToFp(uiToFpF32.Input, uiToFpF32.Result, FloatPrecision.F32, block);
        break;

      case StdSiToFpI32Op siToFpI32:
        regManager.EmitIntToFp(siToFpI32.Input, siToFpI32.Result, FloatPrecision.F64, block);
        break;

      case StdFpToUiOp fpToUi:
        regManager.EmitFpToUnsignedInt(fpToUi.Input, fpToUi.Result, FloatPrecision.F64, block);
        break;

      case StdFpToUiF32Op fpToUiF32:
        regManager.EmitFpToUnsignedInt(fpToUiF32.Input, fpToUiF32.Result, FloatPrecision.F32, block);
        break;

      // === Bitcast ===
      case StdBitcastF64ToI64Op bitcast:
        regManager.EmitBitcastF64ToI64(bitcast.Input, bitcast.Result, block);
        break;

      // === Float rounding ===
      case StdFloorF64Op floorF64:
        regManager.EmitFpUnary(floorF64.Input, floorF64.Result, block,
          (d, s) => new ARM64FrintmOp(d, s, FloatPrecision.F64));
        break;

      case StdFloorF32Op floorF32:
        regManager.EmitFpUnary(floorF32.Input, floorF32.Result, block,
          (d, s) => new ARM64FrintmOp(d, s, FloatPrecision.F32));
        break;

      case StdCeilF64Op ceilF64:
        regManager.EmitFpUnary(ceilF64.Input, ceilF64.Result, block,
          (d, s) => new ARM64FrintpOp(d, s, FloatPrecision.F64));
        break;

      case StdCeilF32Op ceilF32:
        regManager.EmitFpUnary(ceilF32.Input, ceilF32.Result, block,
          (d, s) => new ARM64FrintpOp(d, s, FloatPrecision.F32));
        break;

      case StdRoundF64Op roundF64:
        regManager.EmitFpUnary(roundF64.Input, roundF64.Result, block,
          (d, s) => new ARM64FrintnOp(d, s, FloatPrecision.F64));
        break;

      case StdRoundF32Op roundF32:
        regManager.EmitFpUnary(roundF32.Input, roundF32.Result, block,
          (d, s) => new ARM64FrintnOp(d, s, FloatPrecision.F32));
        break;

      // === Select ===
      case StdSelectI64Op selectOp:
        regManager.EmitSelectI64(selectOp.Condition, selectOp.TrueValue, selectOp.FalseValue, selectOp.Result, block);
        break;

      // === Calls ===
      case StdCallOp callOp:
        regManager.EmitCall(callOp.Callee, callOp.Args, callOp.Result, block,
          ConsumedArgs(callOp.Args, lastUseOfValue, currentOpIndex));
        break;

      case StdCallRuntimeOp rtCall:
        regManager.EmitCall(rtCall.Callee, rtCall.Args, rtCall.Result, block,
          ConsumedArgs(rtCall.Args, lastUseOfValue, currentOpIndex));
        break;

      case StdCallRuntimeIfNonnullOp rtCallIfNonnull:
        // Spill before branching so values remain accessible if skipped
        regManager.SpillAllLiveRegisters(block);
        regManager.EmitBoolTest(rtCallIfNonnull.Args[0], block);
        var skipLabel = $"{funcName}.__skip_guarded_{currentOpIndex}";
        block.AddOp(new ARM64BranchCondOp(ARM64ConditionCode.Eq, skipLabel));
        regManager.EmitCall(rtCallIfNonnull.Callee, rtCallIfNonnull.Args, rtCallIfNonnull.Result, block,
          ConsumedArgs(rtCallIfNonnull.Args, lastUseOfValue, currentOpIndex));
        block.AddOp(new ARM64LabelDefOp(skipLabel));
        break;

      case StdTryCallOp tryCall:
        regManager.EmitTryCall(tryCall.Callee, tryCall.Args, tryCall.Result, tryCall.ErrorFlag, block,
          ConsumedArgs(tryCall.Args, lastUseOfValue, currentOpIndex));
        break;

      case StdTryCallRuntimeOp tryRtCall:
        regManager.EmitTryCall(tryRtCall.Callee, tryRtCall.Args, tryRtCall.Result, tryRtCall.ErrorFlag, block,
          ConsumedArgs(tryRtCall.Args, lastUseOfValue, currentOpIndex));
        break;

      case StdIndirectCallOp indirectCall:
        regManager.EmitIndirectCall(indirectCall.Callee, indirectCall.Args, indirectCall.Result, block,
          ConsumedArgs(indirectCall.Args, lastUseOfValue, currentOpIndex));
        break;

      // === Return ===
      case StdReturnOp retOp:
        if (retOp.ReturnValue != null) {
          if (retOp.ReturnValue is StdF64 or StdF32) {
            regManager.EnsureInSpecificFpRegister(retOp.ReturnValue, ARM64FloatRegister.D0, block);
          } else {
            regManager.EnsureInSpecificRegister(retOp.ReturnValue, ARM64Register.X0, block);
          }
        }
        // In throwing functions, a normal return means success: set error flag (X1) to 0
        if (isThrowingFunc) {
          block.AddOp(new ARM64MovRegImmOp(ARM64Register.X1, 0));
        }
        block.AddOp(new ARM64EpilogueOp());
        block.AddOp(new ARM64RetOp());
        break;

      case StdErrorReturnOp errorRetOp:
        // Put error flag into X1, zero into X0 (dummy return value)
        regManager.EnsureInSpecificRegister(errorRetOp.ErrorFlag, ARM64Register.X1, block);
        block.AddOp(new ARM64MovRegImmOp(ARM64Register.X0, 0));
        block.AddOp(new ARM64EpilogueOp());
        block.AddOp(new ARM64RetOp());
        break;

      // === Branch ===
      case StdBrOp brOp:
        block.AddOp(new ARM64BranchOp($"{funcName}.{brOp.Target}"));
        break;

      case StdCondBrOp condBr:
        regManager.EmitBoolTest(condBr.Condition, block);
        block.AddOp(new ARM64BranchCondOp(ARM64ConditionCode.Ne, $"{funcName}.{condBr.ThenBlock}"));
        block.AddOp(new ARM64BranchOp($"{funcName}.{condBr.ElseBlock}"));
        break;

      case StdSwitchOp switchOp: {
        var indexReg = regManager.LoadToRegister(switchOp.Scrutinee, block);
        var tableLabel = $"__jt_{funcName}_{currentOpIndex}";
        var scopedTargets = switchOp.CaseTargets
            .Select(t => $"{funcName}.{t}").ToArray();
        var scopedDefault = $"{funcName}.{switchOp.DefaultTarget}";

        block.AddOp(new ARM64JumpTableOp(indexReg, switchOp.CaseTargets.Length,
            tableLabel, scopedDefault, scopedTargets));

        outputModule.RdataEntries.Add((tableLabel, new byte[switchOp.CaseTargets.Length * 4], 4));
        break;
      }

      // === LEA (address-of) ===
      case StdLeaOp leaOp: {
        if (varOffsets.TryGetValue(leaOp.VarName, out var leaOffset)) {
          // LEA to first field's offset (for structs, the base field)
          var firstFieldOffset = leaOffset;
          // Find the field with the minimum offset (lowest stack address = start of contiguous buffer)
          var dotPrefix = leaOp.VarName + ".";
          foreach (var (name, offset) in varOffsets) {
            if (name.StartsWith(dotPrefix) && offset < firstFieldOffset) {
              firstFieldOffset = offset;
            }
          }
          regManager.EmitLeaFromStack(leaOp.Result, firstFieldOffset, block);
        } else {
          // Exact name not found — look for struct fields with dotted prefix
          var dotPrefix = leaOp.VarName + ".";
          int? firstFieldOffset = null;
          foreach (var (name, offset) in varOffsets) {
            if (name.StartsWith(dotPrefix)) {
              if (firstFieldOffset == null || offset < firstFieldOffset.Value) {
                firstFieldOffset = offset;
              }
            }
          }
          if (firstFieldOffset != null) {
            regManager.EmitLeaFromStack(leaOp.Result, firstFieldOffset.Value, block);
          }
        }
        break;
      }

      // === Bulk zero ===
      case StdBulkZeroOp bulkZero:
        foreach (var fieldName in bulkZero.FieldNames()) {
          if (varOffsets.TryGetValue(fieldName, out var zeroOffset)) {
            var scratch = new StdI64(IrContext.Current.NextId());
            regManager.EmitLoadImmediate(scratch, 0, block);
            regManager.EmitStoreToStack(scratch, zeroOffset, 8, block);
            regManager.NoteValueDead(scratch);
          }
        }
        break;

      // === Indirect load/store ===
      case StdStoreIndirectOp storeInd:
        if (storeInd.FieldType == IrType.F64)
          regManager.EmitFloatStoreIndirect(storeInd.BasePtr, storeInd.FieldOffset, storeInd.Value, FloatPrecision.F64, block);
        else if (storeInd.FieldType == IrType.F32)
          regManager.EmitFloatStoreIndirect(storeInd.BasePtr, storeInd.FieldOffset, storeInd.Value, FloatPrecision.F32, block);
        else if (storeInd.FieldType == IrType.I32 || storeInd.FieldType == IrType.U32)
          regManager.EmitStoreIndirect(storeInd.BasePtr, storeInd.FieldOffset, storeInd.Value, 4, block);
        else if (storeInd.FieldType == IrType.I1)
          regManager.EmitStoreIndirect(storeInd.BasePtr, storeInd.FieldOffset, storeInd.Value, 1, block);
        else if (storeInd.FieldType == IrType.I8 || storeInd.FieldType == IrType.U8)
          regManager.EmitStoreIndirect(storeInd.BasePtr, storeInd.FieldOffset, storeInd.Value, 1, block);
        else if (storeInd.FieldType == IrType.I16 || storeInd.FieldType == IrType.U16)
          regManager.EmitStoreIndirect(storeInd.BasePtr, storeInd.FieldOffset, storeInd.Value, 2, block);
        else
          regManager.EmitStoreIndirect(storeInd.BasePtr, storeInd.FieldOffset, storeInd.Value, 8, block);
        break;

      case StdLoadIndirectOp loadInd:
        if (loadInd.FieldType == IrType.F64)
          regManager.EmitFloatLoadIndirect(loadInd.BasePtr, loadInd.FieldOffset, loadInd.Result, FloatPrecision.F64, block);
        else if (loadInd.FieldType == IrType.F32)
          regManager.EmitFloatLoadIndirect(loadInd.BasePtr, loadInd.FieldOffset, loadInd.Result, FloatPrecision.F32, block);
        else if (loadInd.FieldType == IrType.I32 || loadInd.FieldType == IrType.U32)
          regManager.EmitLoadIndirect(loadInd.BasePtr, loadInd.FieldOffset, loadInd.Result, 4, block);
        else if (loadInd.FieldType == IrType.I1)
          regManager.EmitLoadIndirect(loadInd.BasePtr, loadInd.FieldOffset, loadInd.Result, 1, block);
        else if (loadInd.FieldType == IrType.I8 || loadInd.FieldType == IrType.U8)
          regManager.EmitLoadIndirect(loadInd.BasePtr, loadInd.FieldOffset, loadInd.Result, 1, block);
        else if (loadInd.FieldType == IrType.I16 || loadInd.FieldType == IrType.U16)
          regManager.EmitLoadIndirect(loadInd.BasePtr, loadInd.FieldOffset, loadInd.Result, 2, block);
        else
          regManager.EmitLoadIndirect(loadInd.BasePtr, loadInd.FieldOffset, loadInd.Result, 8, block);
        break;

      // === Global load/store ===
      case StdGlobalStoreI64Op globalStore:
        regManager.EmitGlobalStore(globalStore.GlobalName, globalStore.Value, 8, block);
        break;

      case StdGlobalLoadI64Op globalLoad:
        regManager.EmitGlobalLoad(globalLoad.GlobalName, globalLoad.Result, 8, block);
        break;

      case StdGlobalStoreI8Op globalStoreI8:
        regManager.EmitGlobalStore(globalStoreI8.GlobalName, globalStoreI8.Value, 1, block);
        break;

      case StdGlobalLoadI8Op globalLoadI8:
        regManager.EmitGlobalLoad(globalLoadI8.GlobalName, globalLoadI8.Result, 1, block);
        break;

      case StdGlobalStoreI16Op globalStoreI16:
        regManager.EmitGlobalStore(globalStoreI16.GlobalName, globalStoreI16.Value, 2, block);
        break;

      case StdGlobalLoadI16Op globalLoadI16:
        regManager.EmitGlobalLoad(globalLoadI16.GlobalName, globalLoadI16.Result, 2, block);
        break;

      case StdGlobalStoreI1Op globalStoreBool:
        regManager.EmitGlobalStore(globalStoreBool.GlobalName, globalStoreBool.Value, 1, block);
        break;

      case StdGlobalLoadI1Op globalLoadBool:
        regManager.EmitGlobalLoad(globalLoadBool.GlobalName, globalLoadBool.Result, 1, block);
        break;

      case StdGlobalStoreF64Op globalStoreF64:
        regManager.EmitGlobalStoreFloat(globalStoreF64.GlobalName, globalStoreF64.Value, FloatPrecision.F64, block);
        break;

      case StdGlobalLoadF64Op globalLoadF64:
        regManager.EmitGlobalLoadFloat(globalLoadF64.GlobalName, globalLoadF64.Result, FloatPrecision.F64, block);
        break;

      case StdGlobalStoreF32Op globalStoreF32:
        regManager.EmitGlobalStoreFloat(globalStoreF32.GlobalName, globalStoreF32.Value, FloatPrecision.F32, block);
        break;

      case StdGlobalLoadF32Op globalLoadF32:
        regManager.EmitGlobalLoadFloat(globalLoadF32.GlobalName, globalLoadF32.Result, FloatPrecision.F32, block);
        break;

      // === LEA rdata/symdata/ucddata/func ===
      case StdLeaRdataOp leaRdata:
        regManager.EmitLeaRdata(leaRdata.Result, leaRdata.RdataLabel, block);
        break;

      case StdLeaSymdataOp leaSymdata:
        regManager.EmitLeaSymdata(leaSymdata.Result, leaSymdata.SymdataLabel, block);
        break;

      case StdLeaUcddataOp leaUcddata:
        regManager.EmitLeaUcddata(leaUcddata.Result, leaUcddata.UcddataLabel, block);
        break;

      case StdFuncRefOp funcRef:
        regManager.EmitFuncRef(funcRef.Result, funcRef.FunctionName, block);
        break;

      // === Ptr reinterpret ===
      case StdPtrToI64Op ptrToI64:
        regManager.EmitMovValueToValue(ptrToI64.Input, ptrToI64.Result, block);
        break;

      // === Null-safe load ===
      case StdNullSafeLoadI64Op nullSafeLoad:
        regManager.EmitNullSafeLoadI64(nullSafeLoad.BasePtr, nullSafeLoad.FieldOffset, nullSafeLoad.Result, block);
        break;

      // === Memcpy / BulkZero ===
      case StdMemCopyOp memCopy:
        regManager.EmitMemCopy(memCopy.SrcPtr, memCopy.DstPtr, memCopy.ByteCount, block);
        break;

      case StdMemCopyReverseOp memCopyRev:
        regManager.EmitMemCopyReverse(memCopyRev.SrcPtr, memCopyRev.DstPtr, memCopyRev.ByteCount, block);
        break;

      // === Parameter (handled in entry block setup above, skip here) ===
      case StdParamOp:
        break;

      default:
        throw new NotImplementedException($"StandardToARM64Conversion: unhandled op type {op.GetType().Name}");
    }
  }

  private static void EmitBinaryOp(StdValue lhs, StdValue rhs, StdValue result,
      StdBinaryOperator op, ARM64RegisterManager regManager, IrBlock<ARM64Op> block) {
    switch (op) {
      case StdBinaryOperator.Add:
        regManager.EmitBinaryRegReg(lhs, rhs, result, block, (d, l, r) => new ARM64AddRegRegOp(d, l, r));
        break;
      case StdBinaryOperator.Sub:
        regManager.EmitBinaryRegReg(lhs, rhs, result, block, (d, l, r) => new ARM64SubRegRegOp(d, l, r));
        break;
      case StdBinaryOperator.Mul:
        regManager.EmitBinaryRegReg(lhs, rhs, result, block, (d, l, r) => new ARM64MulRegRegOp(d, l, r));
        break;
      case StdBinaryOperator.DivSigned:
        regManager.EmitDivision(lhs, rhs, result, block);
        break;
      case StdBinaryOperator.DivUnsigned:
        regManager.EmitUnsignedDivision(lhs, rhs, result, block);
        break;
      case StdBinaryOperator.RemSigned:
        regManager.EmitRemainder(lhs, rhs, result, block);
        break;
      case StdBinaryOperator.RemUnsigned:
        regManager.EmitUnsignedRemainder(lhs, rhs, result, block);
        break;
      case StdBinaryOperator.And:
        regManager.EmitBinaryRegReg(lhs, rhs, result, block, (d, l, r) => new ARM64AndRegRegOp(d, l, r));
        break;
      case StdBinaryOperator.Or:
        regManager.EmitBinaryRegReg(lhs, rhs, result, block, (d, l, r) => new ARM64OrrRegRegOp(d, l, r));
        break;
      case StdBinaryOperator.Xor:
        regManager.EmitBinaryRegReg(lhs, rhs, result, block, (d, l, r) => new ARM64EorRegRegOp(d, l, r));
        break;
      case StdBinaryOperator.Shl:
        regManager.EmitShift(lhs, rhs, result, block, (d, l, r) => new ARM64LslRegRegOp(d, l, r));
        break;
      case StdBinaryOperator.ShrSigned:
        regManager.EmitShift(lhs, rhs, result, block, (d, l, r) => new ARM64AsrRegRegOp(d, l, r));
        break;
      case StdBinaryOperator.ShrUnsigned:
        regManager.EmitShift(lhs, rhs, result, block, (d, l, r) => new ARM64LsrRegRegOp(d, l, r));
        break;
      case StdBinaryOperator.Min:
        EmitMinMax(lhs, rhs, result, ARM64ConditionCode.Lt, regManager, block);
        break;
      case StdBinaryOperator.Max:
        EmitMinMax(lhs, rhs, result, ARM64ConditionCode.Gt, regManager, block);
        break;
      default:
        throw new NotImplementedException($"Binary operator {op} not implemented for ARM64");
    }
  }

  private static void EmitMinMax(StdValue lhs, StdValue rhs, StdValue result,
      ARM64ConditionCode condition, ARM64RegisterManager regManager, IrBlock<ARM64Op> block) {
    // CMP lhs, rhs; CSEL result, lhs, rhs, condition
    regManager.EmitIntegerCompare(lhs, rhs, block);
    // EmitBinaryRegReg works here because it just ensures lhs/rhs are in registers
    // and allocates a result register — CSEL reads from the flags set by CMP above
    regManager.EmitBinaryRegReg(lhs, rhs, result, block,
      (d, l, r) => new ARM64CselOp(d, l, r, condition));
  }

  private static void EmitFloatBinaryOp(StdValue lhs, StdValue rhs, StdValue result,
      StdBinaryOperator op, FloatPrecision precision, ARM64RegisterManager regManager, IrBlock<ARM64Op> block) {
    switch (op) {
      case StdBinaryOperator.Add:
        regManager.EmitFpBinaryRegReg(lhs, rhs, result, block,
          (d, l, r) => new ARM64FaddOp(d, l, r, precision));
        break;
      case StdBinaryOperator.Sub:
        regManager.EmitFpBinaryRegReg(lhs, rhs, result, block,
          (d, l, r) => new ARM64FsubOp(d, l, r, precision));
        break;
      case StdBinaryOperator.Mul:
        regManager.EmitFpBinaryRegReg(lhs, rhs, result, block,
          (d, l, r) => new ARM64FmulOp(d, l, r, precision));
        break;
      case StdBinaryOperator.DivSigned:
      case StdBinaryOperator.DivUnsigned:
        regManager.EmitFpBinaryRegReg(lhs, rhs, result, block,
          (d, l, r) => new ARM64FdivOp(d, l, r, precision));
        break;
      case StdBinaryOperator.Min:
        regManager.EmitFpBinaryRegReg(lhs, rhs, result, block,
          (d, l, r) => new ARM64FminOp(d, l, r, precision));
        break;
      case StdBinaryOperator.Max:
        regManager.EmitFpBinaryRegReg(lhs, rhs, result, block,
          (d, l, r) => new ARM64FmaxOp(d, l, r, precision));
        break;
      default:
        throw new NotImplementedException($"Float binary op {op} not implemented for ARM64");
    }
  }

  private static ARM64ConditionCode PredicateToCondition(string predicate) {
    return predicate switch {
      "eq" => ARM64ConditionCode.Eq,
      "ne" => ARM64ConditionCode.Ne,
      "slt" or "lt" => ARM64ConditionCode.Lt,
      "sle" or "le" => ARM64ConditionCode.Le,
      "sgt" or "gt" => ARM64ConditionCode.Gt,
      "sge" or "ge" => ARM64ConditionCode.Ge,
      "ult" => ARM64ConditionCode.Lo,
      "ule" => ARM64ConditionCode.Ls,
      "ugt" => ARM64ConditionCode.Hi,
      "uge" => ARM64ConditionCode.Hs,
      _ => throw new NotImplementedException($"Unknown comparison predicate: {predicate}")
    };
  }

  private static ARM64ConditionCode FloatPredicateToCondition(string predicate) {
    return predicate switch {
      "oeq" or "eq" => ARM64ConditionCode.Eq,
      "one" or "ne" => ARM64ConditionCode.Ne,
      "olt" or "lt" => ARM64ConditionCode.Lt,
      "ole" or "le" => ARM64ConditionCode.Le,
      "ogt" or "gt" => ARM64ConditionCode.Gt,
      "oge" or "ge" => ARM64ConditionCode.Ge,
      _ => throw new NotImplementedException($"Unknown float comparison predicate: {predicate}")
    };
  }

  private static bool IsLastUse(Dictionary<StdValue, int> lastUseOfValue, StdValue value, int currentOpIndex) {
    return lastUseOfValue.TryGetValue(value, out var lastUse) && lastUse == currentOpIndex;
  }

  private static HashSet<StdValue>? ConsumedArgs(List<StdValue> args, Dictionary<StdValue, int> lastUseOfValue, int currentOpIndex) {
    HashSet<StdValue>? result = null;
    foreach (var arg in args) {
      if (IsLastUse(lastUseOfValue, arg, currentOpIndex))
        (result ??= []).Add(arg);
    }
    return result;
  }

  private static void FreeDeadValues(
    ARM64RegisterManager regManager,
    Dictionary<StdValue, int> lastUseOfValue,
    int currentOpIndex,
    IEnumerable<StdValue> readValues) {
    foreach (var val in readValues) {
      if (lastUseOfValue.TryGetValue(val, out var lastUse) && lastUse == currentOpIndex) {
        regManager.NoteValueDead(val);
      }
    }
  }

  private static string GetOrCreateFloatLabel(double value, IrModule<ARM64Op> module, Dictionary<double, string> floatConstants) {
    if (!floatConstants.TryGetValue(value, out var label)) {
      label = $"__float_{value.ToString(CultureInfo.InvariantCulture)}";
      floatConstants[value] = label;
      module.RdataEntries.Add((label, BitConverter.GetBytes(value), 8));
    }
    return label;
  }

  private static string GetOrCreateFloat32Label(float value, IrModule<ARM64Op> module, Dictionary<float, string> float32Constants) {
    if (!float32Constants.TryGetValue(value, out var label)) {
      label = $"__float32_{value.ToString(CultureInfo.InvariantCulture)}";
      float32Constants[value] = label;
      module.RdataEntries.Add((label, BitConverter.GetBytes(value), 4));
    }
    return label;
  }

  private static int AlignUp(int value, int alignment) {
    return (value + alignment - 1) & ~(alignment - 1);
  }
}
