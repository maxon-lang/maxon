using System.Globalization;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

enum ComparisonKind { Integer, Float }

public static class StandardToX86Conversion {
  public static MlirModule<X86Op> Run(MlirModule<StandardOp> module) {
    var result = new MlirModule<X86Op>();
    result.RdataEntries.AddRange(module.RdataEntries);
    result.Globals.AddRange(module.Globals);

    foreach (var func in module.Functions) {
      var newFunc = ConvertFunction(func, result);
      PeepholeOptimize(newFunc);
      result.AddFunction(newFunc);
    }

    return result;
  }

  private static MlirFunction<X86Op> ConvertFunction(MlirFunction<StandardOp> func, MlirModule<X86Op> outputModule) {
    var newFunc = new MlirFunction<X86Op>(func.Name, func.ParamNames, func.ParamTypes, func.ReturnType, func.ThrowsType) { IsStdlib = func.IsStdlib };

    // Pre-scan: find which variables are actually loaded (read back from stack).
    // A variable is "live" if it appears in a load op, or if it's referenced
    // by a LEA op (struct base address). Struct fields (e.g. "__arr_0.buffer")
    // are live if their parent struct (e.g. "__arr_0") is referenced by LEA.
    var loadedVariables = new HashSet<string>();
    var leaVariables = new HashSet<string>();
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        switch (op) {
          case StdLoadI64Op load: loadedVariables.Add(load.VarName); break;
          case StdLoadI1Op load: loadedVariables.Add(load.VarName); break;
          case StdLoadF64Op load: loadedVariables.Add(load.VarName); break;
          case StdLoadPtrOp load: loadedVariables.Add(load.VarName); break;
          case StdLeaOp lea: leaVariables.Add(lea.VarName); break;
        }
      }
    }
    // Struct fields are live if their parent struct is referenced by LEA
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is IStoreOp store) {
          var dotIdx = store.VarName.IndexOf('.');
          if (dotIdx >= 0 && leaVariables.Contains(store.VarName[..dotIdx]))
            loadedVariables.Add(store.VarName);
        }
      }
    }

    // Pre-scan: calculate stack frame from store ops, skipping dead stores.
    // A store is "dead" if the variable is never loaded (not in loadedVariables).
    // The stored value may have other uses — those uses keep it alive in registers
    // independently. The register allocator handles spilling if needed.
    var varOffsets = new Dictionary<string, int>();
    var deadStoreOps = new HashSet<StandardOp>();
    int varStackSize = 0;
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is not IStoreOp store) continue;
        if (!loadedVariables.Contains(store.VarName)) {
          deadStoreOps.Add(op);
          continue;
        }
        if (!varOffsets.ContainsKey(store.VarName)) {
          varStackSize += store.StoredType.SizeInBytes;
          varOffsets[store.VarName] = -varStackSize;
        }
      }
    }

    // Pre-scan: detect tail calls (StdCallOp immediately followed by StdReturnOp
    // whose return value is the call's result). Excludes runtime calls, throwing
    // functions, and calls with more args than register slots.
    // Also excluded when the function contains StdLeaOp — those create stack
    // addresses that may flow (as i64 via PtrToI64) into the tail call args,
    // which would dangle after the epilogue tears down the caller's frame.
    var tailCalls = new Dictionary<StandardOp, StdCallOp>();
    bool hasStackAddresses = func.Body.Blocks
      .SelectMany(b => b.Operations).Any(op => op is StdLeaOp);
    if (func.ThrowsType == null && !hasStackAddresses) {
      foreach (var block in func.Body.Blocks) {
        var ops = block.Operations;
        for (int i = 0; i < ops.Count - 1; i++) {
          if (ops[i] is StdCallOp callOp && callOp.Result != null
              && callOp.Args.Count <= RegisterManager.RegisterParamCount
              && ops[i + 1] is StdReturnOp retOp
              && retOp.ReturnValue == callOp.Result) {
            tailCalls[ops[i + 1]] = callOp;
          }
        }
      }
    }

    // Pre-scan: compute last use index for each StdValue (for dead-value freeing)
    var lastUseOfValue = new Dictionary<StdValue, int>();
    int scanIdx = 0;
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        foreach (var val in op.ReadValues) {
          lastUseOfValue[val] = scanIdx;
        }
        scanIdx++;
      }
    }
    // For tail calls, extend arg lifetimes to the return op (where the tail jmp happens).
    // The call op is skipped, so args must stay live until the return op processes them.
    foreach (var (retOp, callOp) in tailCalls) {
      int retIdx = 0;
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op == retOp) {
            foreach (var arg in callOp.Args)
              lastUseOfValue[arg] = retIdx;
          }
          retIdx++;
        }
      }
    }

    // Track float constants for rdata deduplication
    var floatConstants = new Dictionary<double, string>();

    // Track pending comparison for flag-based branching
    StdBool? lastCmpResult = null;
    ComparisonKind? lastCmpKind = null;
    string? lastCmpPredicate = null;

    var regManager = new RegisterManager();
    regManager.SetSpillBaseOffset(-varStackSize);
    var sourceBlocks = func.Body.Blocks.ToList();
    int currentOpIndex = 0;

    // Operations pre-handled by the entry block param save pass (skip in normal loop)
    var preHandledOps = new HashSet<StandardOp>();

    for (int blockIdx = 0; blockIdx < sourceBlocks.Count; blockIdx++) {
      var srcBlock = sourceBlocks[blockIdx];
      var x86Block = newFunc.Body.AddBlock(srcBlock.Name);

      regManager.Reset();

      // In the entry block, save register-based parameters to their stack slots
      // immediately. This prevents later operations (e.g. LoadStructFieldsFromPointer)
      // from clobbering parameter registers before the params are stored.
      if (blockIdx == 0) {
        var registerParams = srcBlock.Operations.OfType<StdParamOp>()
          .Where(p => p.Index < RegisterManager.RegisterParamCount).ToList();
        foreach (var paramOp in registerParams) {
          regManager.NoteParam(paramOp.Result, paramOp.Index, x86Block);
          preHandledOps.Add(paramOp);
        }
        foreach (var paramOp in registerParams) {
          // Only match the param's own initial store (VarName == param name),
          // not arbitrary later stores that happen to use the param's SSA value.
          var storeOp = srcBlock.Operations.OfType<StdStoreI64Op>()
            .FirstOrDefault(s => s.Value.Equals(paramOp.Result) && s.VarName == paramOp.Name);
          if (storeOp != null && varOffsets.TryGetValue(storeOp.VarName, out int value)) {
            regManager.EmitStoreToStack(paramOp.Result, value, 8, x86Block);
            preHandledOps.Add(storeOp);
          }
          var storeF64 = srcBlock.Operations.OfType<StdStoreF64Op>()
            .FirstOrDefault(s => s.Value.Equals(paramOp.Result) && s.VarName == paramOp.Name);
          if (storeF64 != null && varOffsets.TryGetValue(storeF64.VarName, out int value2)) {
            regManager.EmitXmmStoreToStack(paramOp.Result, value2, x86Block);
            preHandledOps.Add(storeF64);
          }
          // Handle bool (i1) parameters - must also be stored immediately
          var storeI1 = srcBlock.Operations.OfType<StdStoreI1Op>()
            .FirstOrDefault(s => s.Value.Equals(paramOp.Result) && s.VarName == paramOp.Name);
          if (storeI1 != null && varOffsets.TryGetValue(storeI1.VarName, out int value3)) {
            regManager.EmitStoreToStack(paramOp.Result, value3, 1, x86Block);
            preHandledOps.Add(storeI1);
          }
        }
      }

      foreach (var op in srcBlock.Operations) {
        if (preHandledOps.Contains(op)) { currentOpIndex++; continue; }
        // If there's a pending comparison result and this op is NOT a condBr
        // that uses it, materialize the comparison into a register via setcc.
        if (lastCmpResult != null && !(op is StdCondBrOp cb && cb.Condition == lastCmpResult)) {
          var setccCond = lastCmpKind!.Value switch {
            ComparisonKind.Integer => IntegerPredicateToSetcc(lastCmpPredicate!),
            ComparisonKind.Float => FloatPredicateToSetcc(lastCmpPredicate!),
            _ => throw new InvalidOperationException($"Unsupported comparison kind for setcc: {lastCmpKind}")
          };
          regManager.EmitSetcc(lastCmpResult, setccCond, x86Block);
          lastCmpResult = null;
          lastCmpKind = null;
          lastCmpPredicate = null;
        }

        switch (op) {
          case StdParamOp paramOp:
            regManager.NoteParam(paramOp.Result, paramOp.Index, x86Block);
            break;

          case StdConstI64Op constOp:
            regManager.EmitLoadImmediate(constOp.Result, constOp.Value, x86Block);
            break;

          case StdConstI1Op boolOp:
            regManager.EmitLoadImmediate(boolOp.Result, boolOp.Value ? 1 : 0, x86Block);
            break;

          case StdAddI64Op addOp:
            regManager.EmitBinaryRegReg(addOp.Lhs, addOp.Rhs, addOp.Result, x86Block,
              (l, r) => new X86AddRegRegOp(l, r),
              lhsConsumed: IsLastUse(lastUseOfValue, addOp.Lhs, currentOpIndex),
              useLeaForAdd: true);
            break;

          case StdSubI64Op subOp:
            regManager.EmitBinaryRegReg(subOp.Lhs, subOp.Rhs, subOp.Result, x86Block,
              (l, r) => new X86SubRegRegOp(l, r),
              lhsConsumed: IsLastUse(lastUseOfValue, subOp.Lhs, currentOpIndex));
            break;

          case StdMulI64Op mulOp:
            regManager.EmitMultiply(mulOp.Lhs, mulOp.Rhs, mulOp.Result, x86Block,
              lhsConsumed: IsLastUse(lastUseOfValue, mulOp.Lhs, currentOpIndex));
            break;

          case StdDivI64Op divOp:
            regManager.EmitDivision(divOp.Lhs, divOp.Rhs, divOp.Result, x86Block);
            break;

          case StdRemI64Op remOp:
            regManager.EmitRemainder(remOp.Lhs, remOp.Rhs, remOp.Result, x86Block);
            break;

          case StdAddF64Op addF64Op:
            regManager.EmitXmmBinaryRegReg(addF64Op.Lhs, addF64Op.Rhs, addF64Op.Result, x86Block,
              (l, r) => new X86AddSdOp(l, r));
            break;

          case StdSubF64Op subF64Op:
            regManager.EmitXmmBinaryRegReg(subF64Op.Lhs, subF64Op.Rhs, subF64Op.Result, x86Block,
              (l, r) => new X86SubSdOp(l, r));
            break;

          case StdMulF64Op mulF64Op:
            regManager.EmitXmmBinaryRegReg(mulF64Op.Lhs, mulF64Op.Rhs, mulF64Op.Result, x86Block,
              (l, r) => new X86MulSdOp(l, r));
            break;

          case StdDivF64Op divF64Op:
            regManager.EmitXmmBinaryRegReg(divF64Op.Lhs, divF64Op.Rhs, divF64Op.Result, x86Block,
              (l, r) => new X86DivSdOp(l, r));
            break;

          case StdAndI64Op andOp:
            regManager.EmitBinaryRegReg(andOp.Lhs, andOp.Rhs, andOp.Result, x86Block,
              (l, r) => new X86AndRegRegOp(l, r),
              lhsConsumed: IsLastUse(lastUseOfValue, andOp.Lhs, currentOpIndex));
            break;

          case StdOrI64Op orOp:
            regManager.EmitBinaryRegReg(orOp.Lhs, orOp.Rhs, orOp.Result, x86Block,
              (l, r) => new X86OrRegRegOp(l, r),
              lhsConsumed: IsLastUse(lastUseOfValue, orOp.Lhs, currentOpIndex));
            break;

          case StdXorI64Op xorOp:
            regManager.EmitBinaryRegReg(xorOp.Lhs, xorOp.Rhs, xorOp.Result, x86Block,
              (l, r) => new X86XorRegRegOp(l, r),
              lhsConsumed: IsLastUse(lastUseOfValue, xorOp.Lhs, currentOpIndex));
            break;

          case StdAndI1Op andI1Op:
            regManager.EmitBinaryRegReg(andI1Op.Lhs, andI1Op.Rhs, andI1Op.Result, x86Block,
              (l, r) => new X86AndRegRegOp(l, r),
              lhsConsumed: IsLastUse(lastUseOfValue, andI1Op.Lhs, currentOpIndex));
            break;

          case StdOrI1Op orI1Op:
            regManager.EmitBinaryRegReg(orI1Op.Lhs, orI1Op.Rhs, orI1Op.Result, x86Block,
              (l, r) => new X86OrRegRegOp(l, r),
              lhsConsumed: IsLastUse(lastUseOfValue, orI1Op.Lhs, currentOpIndex));
            break;

          case StdXorI1Op xorI1Op:
            regManager.EmitBinaryRegReg(xorI1Op.Lhs, xorI1Op.Rhs, xorI1Op.Result, x86Block,
              (l, r) => new X86XorRegRegOp(l, r),
              lhsConsumed: IsLastUse(lastUseOfValue, xorI1Op.Lhs, currentOpIndex));
            break;

          case StdShlI64Op shlOp:
            regManager.EmitShift(shlOp.Lhs, shlOp.Rhs, shlOp.Result, x86Block,
              dest => new X86ShlRegClOp(dest));
            break;

          case StdShrI64Op shrOp:
            regManager.EmitShift(shrOp.Lhs, shrOp.Rhs, shrOp.Result, x86Block,
              dest => new X86SarRegClOp(dest));
            break;

          case StdFpToSiOp fpToSiOp:
            regManager.EmitCvttSd2Si(fpToSiOp.Input, fpToSiOp.Result, x86Block);
            break;

          case StdSiToFpOp siToFpOp:
            regManager.EmitCvtSi2Sd(siToFpOp.Input, siToFpOp.Result, x86Block);
            break;

          case StdAbsF64Op absOp: {
            var maskLabel = GetOrCreateAbsMask(outputModule);
            regManager.EmitAbsF64(absOp.Input, absOp.Result, maskLabel, x86Block);
            break;
          }

          case StdSqrtF64Op sqrtOp:
            regManager.EmitXmmUnaryRegReg(sqrtOp.Input, sqrtOp.Result, x86Block,
              (d, s) => new X86SqrtSdOp(d, s));
            break;

          case StdFloorF64Op floorOp:
            regManager.EmitXmmUnaryRegReg(floorOp.Input, floorOp.Result, x86Block,
              (d, s) => new X86RoundSdOp(d, s, 0x01));
            break;

          case StdCeilF64Op ceilOp:
            regManager.EmitXmmUnaryRegReg(ceilOp.Input, ceilOp.Result, x86Block,
              (d, s) => new X86RoundSdOp(d, s, 0x02));
            break;

          case StdRoundF64Op roundOp:
            regManager.EmitXmmUnaryRegReg(roundOp.Input, roundOp.Result, x86Block,
              (d, s) => new X86RoundSdOp(d, s, 0x00));
            break;

          case StdMinF64Op minOp:
            regManager.EmitXmmBinaryRegReg(minOp.Lhs, minOp.Rhs, minOp.Result, x86Block,
              (l, r) => new X86MinSdOp(l, r));
            break;

          case StdMaxF64Op maxOp:
            regManager.EmitXmmBinaryRegReg(maxOp.Lhs, maxOp.Rhs, maxOp.Result, x86Block,
              (l, r) => new X86MaxSdOp(l, r));
            break;

          case StdConstF64Op floatOp: {
            var label = GetOrCreateFloatLabel(floatOp.Value, outputModule, floatConstants);
            regManager.EmitXmmLoadFromRipRelative(floatOp.Result, label, x86Block);
            break;
          }

          case StdStoreI64Op storeOp:
            if (!deadStoreOps.Contains(op))
              regManager.EmitStoreToStack(storeOp.Value, varOffsets[storeOp.VarName], 8, x86Block);
            break;

          case StdStoreF64Op storeOp:
            if (!deadStoreOps.Contains(op))
              regManager.EmitXmmStoreToStack(storeOp.Value, varOffsets[storeOp.VarName], x86Block);
            break;

          case StdLoadI64Op loadOp:
            regManager.EmitLoadFromStack(loadOp.Result, varOffsets[loadOp.VarName], 8, x86Block);
            break;

          case StdLoadF64Op loadOp:
            regManager.EmitXmmLoadFromStack(loadOp.Result, varOffsets[loadOp.VarName], x86Block);
            break;

          case StdStoreI1Op storeBoolOp:
            if (!deadStoreOps.Contains(op))
              regManager.EmitStoreToStack(storeBoolOp.Value, varOffsets[storeBoolOp.VarName], 1, x86Block);
            break;

          case StdLoadI1Op loadBoolOp:
            regManager.EmitLoadFromStack(loadBoolOp.Result, varOffsets[loadBoolOp.VarName], 1, x86Block);
            break;

          case StdStorePtrOp storePtrOp:
            if (!deadStoreOps.Contains(op))
              regManager.EmitStoreToStack(storePtrOp.Value, varOffsets[storePtrOp.VarName], 8, x86Block);
            break;

          case StdLoadPtrOp loadPtrOp:
            regManager.EmitLoadFromStack(loadPtrOp.Result, varOffsets[loadPtrOp.VarName], 8, x86Block);
            break;

          case StdSelectI64Op selectOp:
            regManager.EmitSelectI64(selectOp.Condition, selectOp.TrueValue, selectOp.FalseValue, selectOp.Result, x86Block);
            break;

          case StdCmpI64Op cmpI64Op:
            regManager.EmitIntegerCompare(cmpI64Op.Lhs, cmpI64Op.Rhs, x86Block);
            lastCmpResult = cmpI64Op.Result;
            lastCmpKind = ComparisonKind.Integer;
            lastCmpPredicate = cmpI64Op.Predicate;
            break;

          case StdCmpI1Op cmpI1Op:
            regManager.EmitIntegerCompare(cmpI1Op.Lhs, cmpI1Op.Rhs, x86Block);
            lastCmpResult = cmpI1Op.Result;
            lastCmpKind = ComparisonKind.Integer;
            lastCmpPredicate = cmpI1Op.Predicate;
            break;

          case StdCmpF64Op cmpF64Op:
            regManager.EmitXmmCompare(cmpF64Op.Lhs, cmpF64Op.Rhs, x86Block);
            lastCmpResult = cmpF64Op.Result;
            lastCmpKind = ComparisonKind.Float;
            lastCmpPredicate = cmpF64Op.Predicate;
            break;

          case StdCondBrOp condBr: {
            var scopedElse = $"{func.Name}.{condBr.ElseBlock}";
            if (lastCmpResult != null && condBr.Condition == lastCmpResult) {
              switch (lastCmpKind!.Value) {
                case ComparisonKind.Integer:
                  x86Block.AddOp(new X86JccOp(InvertIntegerPredicate(lastCmpPredicate!), scopedElse));
                  break;
                case ComparisonKind.Float:
                  EmitFloatCondBranch(lastCmpPredicate!, scopedElse, x86Block);
                  break;
                default:
                  // this is a defensive check in case more ComparisonKind values are added later
                  throw new InvalidOperationException($"Unsupported comparison kind for conditional branch: {lastCmpKind}");
              }
            } else {
              regManager.EmitBoolTest(condBr.Condition, x86Block);
              x86Block.AddOp(new X86JccOp("e", scopedElse));
            }
            lastCmpResult = null;
            lastCmpKind = null;
            lastCmpPredicate = null;
            break;
          }

          case StdBrOp br:
            x86Block.AddOp(new X86JmpOp($"{func.Name}.{br.Target}"));
            break;

          case StdCallOp callOp:
            if (!tailCalls.ContainsValue(callOp))
              regManager.EmitCall(callOp.Callee, callOp.Args, callOp.Result, x86Block,
                ConsumedArgs(callOp.Args, lastUseOfValue, currentOpIndex));
            break;

          case StdLeaOp leaOp: {
            // Get the address of the first field slot of a struct variable.
            // The sret pointer convention uses the address of the ".first_field" slot.
            var firstFieldOffset = FindFirstFieldOffset(leaOp.VarName, varOffsets);
            regManager.EmitLeaFromStack(leaOp.Result, firstFieldOffset, x86Block);
            break;
          }

          case StdLeaRdataOp leaRdataOp: {
            regManager.EmitLeaRipRelative(leaRdataOp.Result, leaRdataOp.RdataLabel, x86Block);
            break;
          }

          case StdStoreIndirectOp storeIndOp:
            regManager.EmitStoreIndirect(storeIndOp.Value, storeIndOp.BasePtr, storeIndOp.FieldOffset, storeIndOp.FieldType, x86Block);
            break;

          case StdLoadIndirectOp loadIndOp:
            regManager.EmitLoadIndirect(loadIndOp.Result, loadIndOp.BasePtr, loadIndOp.FieldOffset, loadIndOp.FieldType, x86Block);
            break;

          case StdGlobalLoadI64Op globalLoadI64:
            regManager.EmitGlobalLoad(globalLoadI64.Result, globalLoadI64.GlobalName, x86Block);
            break;

          case StdGlobalStoreI64Op globalStoreI64:
            regManager.EmitGlobalStore(globalStoreI64.Value, globalStoreI64.GlobalName, x86Block);
            break;

          case StdGlobalLoadF64Op globalLoadF64:
            regManager.EmitXmmGlobalLoad(globalLoadF64.Result, globalLoadF64.GlobalName, x86Block);
            break;

          case StdGlobalStoreF64Op globalStoreF64:
            regManager.EmitXmmGlobalStore(globalStoreF64.Value, globalStoreF64.GlobalName, x86Block);
            break;

          case StdGlobalLoadI1Op globalLoadI1:
            regManager.EmitGlobalLoad(globalLoadI1.Result, globalLoadI1.GlobalName, x86Block);
            break;

          case StdGlobalStoreI1Op globalStoreI1:
            regManager.EmitGlobalStore(globalStoreI1.Value, globalStoreI1.GlobalName, x86Block);
            break;

          case StdReturnOp retOp: {
            if (tailCalls.TryGetValue(op, out var tailCallOp)) {
              regManager.EmitTailCall(tailCallOp.Callee, tailCallOp.Args, x86Block);
              break;
            }
            if (retOp.ReturnValue != null) {
              if (retOp.ReturnValue is StdF64) {
                regManager.EnsureInXmm0ForReturn(retOp.ReturnValue, x86Block);
              } else if (retOp.ReturnValue is StdPtr) {
                // Pointers use 64-bit RAX
                regManager.EnsureInSpecificRegister(retOp.ReturnValue, X86Register.Rax, x86Block);
              } else if (retOp.ReturnValue is StdI64 or StdI32 or StdBool) {
                // All integer types use 32-bit EAX (even i64, for compatibility with existing codegen)
                regManager.EnsureInSpecificRegister(retOp.ReturnValue, X86Register.Eax, x86Block);
              } else {
                throw new InvalidOperationException($"StandardToX86: unsupported return type {retOp.ReturnValue.GetType().Name}");
              }
            }
            // In throwing functions, a normal return means success: set error flag (RDX) to 0
            if (func.ThrowsType != null) {
              x86Block.AddOp(new X86XorRegRegOp(X86Register.Edx, X86Register.Edx));
            }

            x86Block.AddOp(new X86EpilogueOp());
            x86Block.AddOp(new X86RetOp());
            break;
          }

          case StdErrorReturnOp errRetOp: {
            // Put error flag into RDX, zero into RAX (dummy return value)
            regManager.EnsureInSpecificRegister(errRetOp.ErrorFlag, X86Register.Edx, x86Block);
            x86Block.AddOp(new X86XorRegRegOp(X86Register.Eax, X86Register.Eax));
            x86Block.AddOp(new X86EpilogueOp());
            x86Block.AddOp(new X86RetOp());
            break;
          }

          case StdTryCallOp tryCallOp: {
            regManager.EmitTryCall(tryCallOp.Callee, tryCallOp.Args, tryCallOp.Result, tryCallOp.ErrorFlag, x86Block,
              ConsumedArgs(tryCallOp.Args, lastUseOfValue, currentOpIndex));
            break;
          }

          case StdCallRuntimeOp runtimeCallOp: {
            // Runtime calls use the same calling convention as regular calls
            regManager.EmitCall(runtimeCallOp.Callee, runtimeCallOp.Args, runtimeCallOp.Result, x86Block,
              ConsumedArgs(runtimeCallOp.Args, lastUseOfValue, currentOpIndex));
            break;
          }

          case StdPtrToI64Op ptrToI64Op: {
            // Pointer is already in a GPR, just alias it as i64
            regManager.EmitMovValueToValue(ptrToI64Op.Input, ptrToI64Op.Result, x86Block);
            break;
          }

          case StdMemCopyOp memCopyOp: {
            // Copy byteCount bytes from src to dst using rep movsb
            regManager.EmitMemCopy(memCopyOp.SrcPtr, memCopyOp.DstPtr, memCopyOp.ByteCount, x86Block);
            break;
          }

          case StdFuncRefOp funcRefOp: {
            // Load the address of a function (LEA RIP-relative to function)
            regManager.EmitFuncRef(funcRefOp.FunctionName, funcRefOp.Result, x86Block);
            break;
          }

          case StdIndirectCallOp indirectCallOp: {
            // Call through a function pointer
            regManager.EmitIndirectCall(indirectCallOp.Callee, indirectCallOp.Args, indirectCallOp.Result, x86Block,
              ConsumedArgs(indirectCallOp.Args, lastUseOfValue, currentOpIndex));
            break;
          }


          default:
            throw new InvalidOperationException($"No StandardToX86 conversion for: {op.GetType().Name} ({op.Mnemonic})");
        }

        // Free registers for values whose last use was this op
        FreeDeadValues(regManager, lastUseOfValue, currentOpIndex, op.ReadValues);
        regManager.AdvanceOp();
        currentOpIndex++;
      }
    }

    // Insert prologue/epilogue only when a stack frame is needed.
    int stackSize = regManager.TotalStackSize;
    if (stackSize > 0) {
      var entryBlock = newFunc.Body.Blocks.First();
      entryBlock.Operations.Insert(0, new X86PrologueOp(stackSize));
    } else {
      foreach (var block in newFunc.Body.Blocks) {
        block.Operations.RemoveAll(op => op is X86EpilogueOp);
      }
    }

    return newFunc;
  }

  private static bool IsLastUse(Dictionary<StdValue, int> lastUseOfValue, StdValue value, int currentOpIndex) {
    return lastUseOfValue.TryGetValue(value, out var lastUse) && lastUse == currentOpIndex;
  }

  /// <summary>
  /// Compute the set of call args whose last use is this call. These args are
  /// consumed by the call and don't need pre-call spilling.
  /// </summary>
  private static HashSet<StdValue>? ConsumedArgs(List<StdValue> args, Dictionary<StdValue, int> lastUseOfValue, int currentOpIndex) {
    HashSet<StdValue>? result = null;
    foreach (var arg in args) {
      if (IsLastUse(lastUseOfValue, arg, currentOpIndex))
        (result ??= []).Add(arg);
    }
    return result;
  }

  private static void FreeDeadValues(
    RegisterManager regManager,
    Dictionary<StdValue, int> lastUseOfValue,
    int currentOpIndex,
    IEnumerable<StdValue> readValues) {
    foreach (var val in readValues) {
      if (lastUseOfValue.TryGetValue(val, out var lastUse) && lastUse == currentOpIndex) {
        regManager.NoteValueDead(val);
      }
    }
  }

  private static string InvertIntegerPredicate(string predicate) => predicate switch {
    "eq" => "ne",
    "ne" => "e",
    "lt" => "ge",
    "ge" => "l",
    "le" => "g",
    "gt" => "le",
    _ => throw new InvalidOperationException($"Unknown integer comparison predicate: {predicate}")
  };

  private static string IntegerPredicateToSetcc(string predicate) => predicate switch {
    "eq" => "e",
    "ne" => "ne",
    "lt" => "l",
    "gt" => "g",
    "le" => "le",
    "ge" => "ge",
    _ => throw new InvalidOperationException($"Unknown integer comparison predicate: {predicate}")
  };

  private static string FloatPredicateToSetcc(string predicate) => predicate switch {
    "eq" => "e",
    "ne" => "ne",
    "gt" => "a",
    "ge" => "ae",
    "lt" => "b",
    "le" => "be",
    _ => throw new InvalidOperationException($"Unknown float comparison predicate: {predicate}")
  };

  /// <summary>
  /// Emit conditional branch(es) for a float comparison.
  /// ucomisd sets CF/ZF/PF flags. We jump to elseLabel when the condition is FALSE.
  /// For ordered comparisons, unordered (NaN) falls through to else.
  /// </summary>
  private static void EmitFloatCondBranch(string predicate, string elseLabel, MlirBlock<X86Op> block) {
    // ucomisd(A, B) flag results:
    //   A > B:  ZF=0, PF=0, CF=0
    //   A < B:  ZF=0, PF=0, CF=1
    //   A == B: ZF=1, PF=0, CF=0
    //   NaN:    ZF=1, PF=1, CF=1
    switch (predicate) {
      case "eq":
        // false when ZF=0 or PF=1
        block.AddOp(new X86JccOp("ne", elseLabel));
        block.AddOp(new X86JccOp("p", elseLabel));
        break;
      case "ne":
        // true when ZF=0 or PF=1; false when ZF=1 and PF=0
        // Jump to else when ZF=1 and PF=0 — use jp to skip the je
        // Actually: ne is true except when exactly equal (ZF=1, PF=0)
        // So jump to else only when e and np. Use: jp over, je else, over:
        // Simpler: use "e" to jump to else (this treats NaN as not-equal, which is correct)
        block.AddOp(new X86JccOp("e", elseLabel));
        break;
      case "gt":
        // A > B: CF=0, ZF=0 → use "a" (above). False when CF=1 or ZF=1 → "be"
        block.AddOp(new X86JccOp("be", elseLabel));
        break;
      case "ge":
        // A >= B: CF=0 → use "ae". False when CF=1 → "b"
        block.AddOp(new X86JccOp("b", elseLabel));
        break;
      case "lt":
        // A < B: CF=1 → use "b". False when CF=0 → "ae". Also false on NaN (PF=1, CF=1 but that's "below")
        // Actually for unordered: CF=1, so NaN would look like "less than" — wrong.
        // For proper NaN handling: jump if PF=1 (unordered) OR CF=0
        block.AddOp(new X86JccOp("p", elseLabel));
        block.AddOp(new X86JccOp("ae", elseLabel));
        break;
      case "le":
        // A <= B: CF=1 or ZF=1. False when CF=0 and ZF=0 → "a"
        // But NaN gives CF=1, ZF=1, PF=1 which looks like "be" — wrong.
        block.AddOp(new X86JccOp("p", elseLabel));
        block.AddOp(new X86JccOp("a", elseLabel));
        break;
      default:
        throw new InvalidOperationException($"Unknown float comparison predicate for branch: {predicate}");
    }
  }

  private static string GetOrCreateFloatLabel(double value, MlirModule<X86Op> module, Dictionary<double, string> floatConstants) {
    if (!floatConstants.TryGetValue(value, out var label)) {
      label = $"__float_{value.ToString(CultureInfo.InvariantCulture)}";
      floatConstants[value] = label;
      module.RdataEntries.Add((label, BitConverter.GetBytes(value), 1));
    }
    return label;
  }

  /// <summary>
  /// Find the stack offset of the first field slot of a struct variable.
  /// The sret convention passes this address as the pointer to the struct's storage.
  /// Fields are stored contiguously: first field at base, second at base-8, etc.
  /// We use the most negative offset (last field) as the base, since stack grows down,
  /// and the first field ends up at the highest address in the contiguous block.
  /// </summary>
  private static int FindFirstFieldOffset(string structVarName, Dictionary<string, int> varOffsets) {
    // Find all field offsets for this struct prefix
    int? lowestOffset = null;
    foreach (var (name, offset) in varOffsets) {
      if (name.StartsWith($"{structVarName}.")) {
        if (lowestOffset == null || offset < lowestOffset)
          lowestOffset = offset;
      }
    }
    if (lowestOffset == null) {
      throw new InvalidOperationException($"No field offsets found for struct variable '{structVarName}'");
    }
    return lowestOffset.Value;
  }

  private static string GetOrCreateAbsMask(MlirModule<X86Op> module) {
    const string label = "__abs_mask";
    if (module.RdataEntries.All(e => e.label != label)) {
      // 128-bit mask: clear sign bit of each 64-bit double lane
      var mask = new byte[16];
      var laneMask = BitConverter.GetBytes(0x7FFFFFFFFFFFFFFFL);
      Array.Copy(laneMask, 0, mask, 0, 8);
      Array.Copy(laneMask, 0, mask, 8, 8);
      module.RdataEntries.Add((label, mask, 16));
    }
    return label;
  }

  /// <summary>
  /// Peephole optimization: fuse add+mov into lea.
  /// Pattern: add rX, rY; mov rZ, rX (where rX is dead after) → lea rZ, [rX + rY]
  /// </summary>
  private static void PeepholeOptimize(MlirFunction<X86Op> func) {
    foreach (var block in func.Body.Blocks) {
      var ops = block.Operations;
      for (int i = 0; i < ops.Count - 1; i++) {
        if (ops[i] is X86AddRegRegOp add && ops[i + 1] is X86MovRegRegOp mov
            && mov.Src == add.Dest && mov.Dest != add.Dest) {
          // Check that add.Dest (rX) is not read after the mov
          if (!IsRegReadAfter(ops, i + 2, add.Dest)) {
            ops[i] = new X86LeaRegRegRegOp(mov.Dest, add.Dest, add.Src);
            ops.RemoveAt(i + 1);
          }
        }
      }
    }
  }

  /// <summary>
  /// Check if a register is read by any op from startIndex to end of the op list.
  /// </summary>
  private static bool IsRegReadAfter(List<X86Op> ops, int startIndex, X86Register reg) {
    var reg64 = To64Bit(reg);
    for (int i = startIndex; i < ops.Count; i++) {
      foreach (var r in GetReadRegisters(ops[i])) {
        if (To64Bit(r) == reg64) return true;
      }
    }
    return false;
  }

  private static X86Register To64Bit(X86Register reg) => reg switch {
    X86Register.Eax => X86Register.Rax,
    X86Register.Ecx => X86Register.Rcx,
    X86Register.Edx => X86Register.Rdx,
    X86Register.Ebx => X86Register.Rbx,
    X86Register.Esp => X86Register.Rsp,
    X86Register.Ebp => X86Register.Rbp,
    X86Register.Esi => X86Register.Rsi,
    X86Register.Edi => X86Register.Rdi,
    _ => reg
  };

  /// <summary>
  /// Return the GPR registers read (used as sources) by an X86 op.
  /// Unrecognized ops conservatively return all GPRs to prevent unsafe optimizations.
  /// </summary>
  private static IEnumerable<X86Register> GetReadRegisters(X86Op op) {
    switch (op) {
      // Pure writes (no GPR reads)
      case X86PrologueOp:
      case X86EpilogueOp:
      case X86MovRegImmOp:
      case X86MovRegMemOp:
      case X86LeaRegMemOp:
      case X86LeaRipRelOp:
      case X86LeaFuncAddrOp:
      case X86SetccOp:
      case X86JccOp:
      case X86JmpOp:
      case X86RetOp:
      case X86GlobalLoadOp:
      case X86CvttSd2SiOp:
        break;
      // Single GPR read
      case X86MovRegRegOp mov: yield return mov.Src; break;
      case X86PushRegOp push: yield return push.Register; break;
      case X86PopRegOp: break;
      case X86AddRegImmOp addImm: yield return addImm.Dest; break;
      case X86SubRegImmOp subImm: yield return subImm.Dest; break;
      case X86MovzxRegOp movzx: yield return movzx.Dest; break;
      case X86MovMemRegOp store: yield return store.Src; break;
      case X86MovMemRspRegOp storeRsp: yield return storeRsp.Src; break;
      case X86GlobalStoreOp gs: yield return gs.Src; break;
      case X86CallIndirectOp callInd: yield return callInd.Target; break;
      case X86CvtSi2SdOp cvt: yield return cvt.Src; break;
      // Two GPR reads
      case X86AddRegRegOp add: yield return add.Dest; yield return add.Src; break;
      case X86SubRegRegOp sub: yield return sub.Dest; yield return sub.Src; break;
      case X86AndRegRegOp and: yield return and.Dest; yield return and.Src; break;
      case X86OrRegRegOp or: yield return or.Dest; yield return or.Src; break;
      case X86XorRegRegOp xor: yield return xor.Dest; yield return xor.Src; break;
      case X86ImulRegRegOp imul: yield return imul.Dest; yield return imul.Src; break;
      case X86XchgRegRegOp xchg: yield return xchg.A; yield return xchg.B; break;
      case X86CmpRegRegOp cmp: yield return cmp.Lhs; yield return cmp.Rhs; break;
      case X86TestRegRegOp test: yield return test.Lhs; yield return test.Rhs; break;
      case X86LeaRegRegRegOp lea: yield return lea.BaseReg; yield return lea.Index; break;
      case X86MovIndirectMemRegOp storeInd: yield return storeInd.BaseReg; yield return storeInd.Src; break;
      case X86MovRegIndirectMemOp loadInd: yield return loadInd.BaseReg; break;
      case X86MovzxRegByteIndirectOp movzxInd: yield return movzxInd.BaseReg; break;
      case X86MovByteIndirectRegOp storeByteInd: yield return storeByteInd.BaseReg; yield return storeByteInd.Src; break;
      // Shift reads dest + implicit ECX
      case X86ShlRegClOp shl: yield return shl.Dest; yield return X86Register.Ecx; break;
      case X86SarRegClOp sar: yield return sar.Dest; yield return X86Register.Ecx; break;
      // IDIV reads RAX, RDX, and divisor
      case X86CqoOp: yield return X86Register.Rax; break;
      case X86IdivRegOp idiv: yield return X86Register.Rax; yield return X86Register.Rdx; yield return idiv.Divisor; break;
      // REP MOVSB reads RSI, RDI, RCX
      case X86RepMovsbOp: yield return X86Register.Rsi; yield return X86Register.Rdi; yield return X86Register.Rcx; break;
      // XMM ops that read GPR base registers
      case X86MovSdIndirectMemXmmOp sdStoreInd: yield return sdStoreInd.BaseReg; break;
      case X86MovSdXmmIndirectMemOp sdLoadInd: yield return sdLoadInd.BaseReg; break;
      // Calls/imports: conservatively assume all caller-saved registers are read
      case X86CallDirectOp:
      case X86CallImportOp:
        yield return X86Register.Rcx; yield return X86Register.Rdx;
        yield return X86Register.R8; yield return X86Register.R9;
        break;
      // Conservative default: assume all GPRs are read
      default:
        yield return X86Register.Rax; yield return X86Register.Rcx;
        yield return X86Register.Rdx; yield return X86Register.Rbx;
        yield return X86Register.Rsi; yield return X86Register.Rdi;
        yield return X86Register.R8; yield return X86Register.R9;
        yield return X86Register.R10; yield return X86Register.R11;
        break;
    }
  }
}
