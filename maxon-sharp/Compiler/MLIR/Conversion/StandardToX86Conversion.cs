using System.Globalization;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

enum ComparisonKind { Integer, UnsignedInteger, Float }

public static class StandardToX86Conversion {
  [ThreadStatic] private static int _floatBranchCounter;
  [ThreadStatic] private static int _labelCounter;
  [ThreadStatic] private static int _nonnullSkipCounter;
  [ThreadStatic] private static bool _inStdlib;
  public static int NextLabelId() => _labelCounter++;
  public static MlirModule<X86Op> Run(MlirModule<StandardOp> module) {
    var result = new MlirModule<X86Op> {
      EntryFunctionName = module.EntryFunctionName
    };
    result.RdataEntries.AddRange(module.RdataEntries);
    result.SymdataEntries.AddRange(module.SymdataEntries);
    result.UcddataEntries.AddRange(module.UcddataEntries);
    result.Globals.AddRange(module.Globals);
    result.TagTable = module.TagTable;
    result.TagNames = module.TagNames;
    foreach (var (k, v) in module.TypeDefs) result.TypeDefs[k] = v;

    _labelCounter = 0;
    _floatBranchCounter = 0;
    _nonnullSkipCounter = 0;
    _inStdlib = true;
    foreach (var func in module.Functions) {
      if (_inStdlib && !func.IsStdlib) {
        _inStdlib = false;
        _nonnullSkipCounter = 0;
      }
      try {
        var newFunc = ConvertFunction(func, result);
        result.AddFunction(newFunc);
      } catch (Exception ex) {
        throw new InvalidOperationException($"Error converting function '{func.Name}': {ex.Message}", ex);
      }
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
          case ILoadOp load: loadedVariables.Add(load.VarName); break;
          case StdLeaOp lea: leaVariables.Add(lea.VarName); break;
        }
      }
    }
    // LEA variables need stack slots for their address to be taken
    foreach (var leaVar in leaVariables)
      loadedVariables.Add(leaVar);
    // Struct fields are live if their parent struct is referenced by LEA
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

    // Pre-scan: calculate stack frame from store ops, skipping dead stores.
    // A store is "dead" if the variable is never loaded (not in loadedVariables).
    // The stored value may have other uses — those uses keep it alive in registers
    // independently. The register allocator handles spilling if needed.
    var varOffsets = new Dictionary<string, int>();
    var deadStoreOps = new HashSet<StandardOp>();
    int varStackSize = 0;
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

    if (func.Name.Contains("runAllSpecTests")) {
      Logger.Debug(LogCategory.Codegen, $"Stack layout for {func.Name} (varStackSize={varStackSize}):");
      foreach (var (name, offset) in varOffsets.OrderBy(kv => kv.Value))
        Logger.Debug(LogCategory.Codegen, $"  [{offset}] {name}");
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
    // Track which value IDs have any read — pure ops defining unused values can be skipped.
    var usedValueIds = new HashSet<int>();
    // Values only consumed by return/call ops can be deferred (not eagerly materialized).
    // Call arg placement and return handling will materialize them on demand from
    // their stack home or constant record, avoiding redundant register loads.
    var sinkOnlyValues = new HashSet<StdValue>();
    var usedByNonSink = new HashSet<StdValue>();
    int scanIdx = 0;
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        foreach (var val in op.ReadValues) {
          lastUseOfValue[val] = scanIdx;
          usedValueIds.Add(val.Id);
          if (op is StdReturnOp or StdCallOp or StdCallRuntimeOp or StdCallRuntimeIfNonnullOp or StdTryCallOp or StdTryCallRuntimeOp)
            sinkOnlyValues.Add(val);
          else
            usedByNonSink.Add(val);
        }
        scanIdx++;
      }
    }
    sinkOnlyValues.ExceptWith(usedByNonSink);
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
    var float32Constants = new Dictionary<float, string>();

    // F32 abs mask (created on demand when StdAbsF32Op is present)
    var absF32MaskLabel = "__abs_mask_f32";
    if (func.Body.Blocks.SelectMany(b => b.Operations).Any(op => op is StdAbsF32Op)) {
      outputModule.RdataEntries.Add((absF32MaskLabel, [0xFF, 0xFF, 0xFF, 0x7F], 1));
    }

    // Track pending comparison for flag-based branching
    StdBool? lastCmpResult = null;
    ComparisonKind? lastCmpKind = null;
    string? lastCmpPredicate = null;

    var regManager = new RegisterManager {
      DeferredValues = sinkOnlyValues
    };
    regManager.SetSpillBaseOffset(-varStackSize);
    var sourceBlocks = func.Body.Blocks.ToList();
    int currentOpIndex = 0;

    // Operations pre-handled by the entry block param save pass (skip in normal loop)
    var preHandledOps = new HashSet<StandardOp>();

    // Pre-scan: detect values that are defined in one block but used in another.
    // Blocks that define such values need spilling at block transitions to
    // preserve register values across block boundaries.
    var valueDefBlock = new Dictionary<int, int>(); // value ID -> block index
    var needsCrossBlockSpill = new HashSet<int>();
    for (int bi = 0; bi < sourceBlocks.Count; bi++) {
      foreach (var op in sourceBlocks[bi].Operations) {
        int resultId = op.AnyResultId;
        if (resultId >= 0)
          valueDefBlock[resultId] = bi;
      }
    }
    for (int bi = 0; bi < sourceBlocks.Count; bi++) {
      foreach (var op in sourceBlocks[bi].Operations) {
        foreach (var use in op.ReadValues) {
          if (valueDefBlock.TryGetValue(use.Id, out int defBlock) && defBlock != bi) {
            // Mark all blocks between def and use as needing spill preservation
            for (int k = defBlock; k < bi; k++)
              needsCrossBlockSpill.Add(k);
          }
        }
      }
    }

    var divergingBlocks = BlockAnalysis.FindDivergingBlocks(sourceBlocks);

    MlirBlock<X86Op>? prevX86Block = null;
    int prevBlockIdx = -1;
    RegisterManagerBase<X86Register, X86XmmRegister, X86Op>.RegisterSnapshot? savedRegState = null;
    for (int blockIdx = 0; blockIdx < sourceBlocks.Count; blockIdx++) {
      var srcBlock = sourceBlocks[blockIdx];
      var x86Block = newFunc.Body.AddBlock(srcBlock.Name);

      if (blockIdx == 0 || prevX86Block == null) {
        regManager.Reset();
      } else if (divergingBlocks.Contains(blockIdx)) {
        // Diverging block (noreturn) — save register state so it can be restored
        // for the next non-diverging block, avoiding unnecessary spills on the happy path.
        // Don't spill via ResetForBlockTransition — the diverging block accesses
        // cross-block values through variable stack slots (memref.load), not SSA refs.
        savedRegState ??= regManager.SaveState();
        regManager.Reset();
      } else if (savedRegState != null) {
        // First non-diverging block after diverging block(s) — restore register
        // state from before the diverging sequence.
        regManager.RestoreState(savedRegState);
        savedRegState = null;
      } else if (needsCrossBlockSpill.Contains(prevBlockIdx)) {
        // Identify values whose last use has already passed — no need to spill them.
        HashSet<StdValue>? deadAtTransition = null;
        foreach (var (val, lastUse) in lastUseOfValue) {
          if (lastUse < currentOpIndex)
            (deadAtTransition ??= []).Add(val);
        }
        regManager.ResetForBlockTransition(prevX86Block!, deadAtTransition);
      } else {
        regManager.Reset();
      }

      // In the entry block, save register-based parameters to their stack slots
      // immediately. This prevents later operations from clobbering parameter
      // registers before the params are stored.
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
          var storeF32 = srcBlock.Operations.OfType<StdStoreF32Op>()
            .FirstOrDefault(s => s.Value.Equals(paramOp.Result) && s.VarName == paramOp.Name);
          if (storeF32 != null && varOffsets.TryGetValue(storeF32.VarName, out int value2b)) {
            regManager.EmitXmmStoreToStackF32(paramOp.Result, value2b, x86Block);
            preHandledOps.Add(storeF32);
          }
          // Handle bool (i1) parameters - must also be stored immediately
          var storeI1 = srcBlock.Operations.OfType<StdStoreI1Op>()
            .FirstOrDefault(s => s.Value.Equals(paramOp.Result) && s.VarName == paramOp.Name);
          if (storeI1 != null && varOffsets.TryGetValue(storeI1.VarName, out int value3)) {
            regManager.EmitStoreToStack(paramOp.Result, value3, 1, x86Block);
            preHandledOps.Add(storeI1);
          }
          var storeI32 = srcBlock.Operations.OfType<StdStoreI32Op>()
            .FirstOrDefault(s => s.Value.Equals(paramOp.Result) && s.VarName == paramOp.Name);
          if (storeI32 != null && varOffsets.TryGetValue(storeI32.VarName, out int value4)) {
            regManager.EmitStoreToStack(paramOp.Result, value4, 4, x86Block);
            preHandledOps.Add(storeI32);
          }
        }
      }

      // Pre-scan: compute register hints for values consumed by return ops
      foreach (var op in srcBlock.Operations) {
        if (op is StdReturnOp retHint && retHint.ReturnValue != null) {
          if (retHint.ReturnValue is StdPtr)
            regManager.SetRegisterHint(retHint.ReturnValue, X86Register.Rax);
          else if (retHint.ReturnValue is StdI64 or StdI32 or StdBool)
            regManager.SetRegisterHint(retHint.ReturnValue, X86Register.Rax);
        } else if (op is StdErrorReturnOp errRetHint) {
          regManager.SetRegisterHint(errRetHint.ErrorFlag, X86Register.Rdx);
        }
      }

      // Pre-scan: detect or-of-two-cmps → condBr patterns for two-jump optimization.
      // When both cmps are integer, single-use, and feed an Or whose result is
      // the condBr condition, we can emit two cmp+jcc instead of setcc+setcc+or+test+je.
      var twoJumpCondBrs = new Dictionary<StdCondBrOp, (StandardOp Cmp1, string Pred1, ComparisonKind Kind1, StandardOp Cmp2, string Pred2, ComparisonKind Kind2)>();
      var twoJumpSkipOps = new HashSet<StandardOp>();
      {
        var opList = srcBlock.Operations;
        var resultToOp = new Dictionary<int, StandardOp>();
        foreach (var scanOp in opList) {
          if (scanOp.AnyResultId >= 0)
            resultToOp[scanOp.AnyResultId] = scanOp;
        }
        foreach (var scanOp in opList) {
          if (scanOp is not StdCondBrOp condBrScan) continue;
          if (!resultToOp.TryGetValue(condBrScan.Condition.Id, out var orCandidate)) continue;
          if (orCandidate is not StdBinaryI1Op { Operator: StdBinaryOperator.Or } orOp) continue;
          if (!resultToOp.TryGetValue(orOp.Lhs.Id, out var cmp1Op)) continue;
          if (!resultToOp.TryGetValue(orOp.Rhs.Id, out var cmp2Op)) continue;

          static (string pred, ComparisonKind kind)? GetCmpInfo(StandardOp c) => c switch {
            StdCmpI64Op i64 => (i64.Predicate, ComparisonKind.Integer),
            StdCmpU64Op u64 => (u64.Predicate, ComparisonKind.UnsignedInteger),
            StdCmpI32Op i32 => (i32.Predicate, ComparisonKind.Integer),
            StdCmpU32Op u32 => (u32.Predicate, ComparisonKind.UnsignedInteger),
            _ => null
          };

          var info1 = GetCmpInfo(cmp1Op);
          var info2 = GetCmpInfo(cmp2Op);
          if (info1 == null || info2 == null) continue;

          twoJumpCondBrs[condBrScan] = (cmp1Op, info1.Value.pred, info1.Value.kind, cmp2Op, info2.Value.pred, info2.Value.kind);
          twoJumpSkipOps.Add(cmp1Op);
          twoJumpSkipOps.Add(cmp2Op);
          twoJumpSkipOps.Add(orOp);
        }
      }

      foreach (var op in srcBlock.Operations) {
        if (preHandledOps.Contains(op) || twoJumpSkipOps.Contains(op)) { currentOpIndex++; continue; }
        // If there's a pending comparison result and this op is NOT a condBr
        // that uses it, materialize the comparison into a register via setcc.
        if (lastCmpResult != null && !(op is StdCondBrOp cb && cb.Condition == lastCmpResult)) {
          if (lastCmpKind!.Value == ComparisonKind.Float) {
            regManager.EmitFloatSetcc(lastCmpResult, lastCmpPredicate!, x86Block);
          } else {
            var setccCond = lastCmpKind!.Value switch {
              ComparisonKind.Integer => IntegerPredicateToSetcc(lastCmpPredicate!),
              ComparisonKind.UnsignedInteger => UnsignedPredicateToSetcc(lastCmpPredicate!),
              _ => throw new InvalidOperationException($"Unsupported comparison kind for setcc: {lastCmpKind}")
            };
            regManager.EmitSetcc(lastCmpResult, setccCond, x86Block);
          }
          lastCmpResult = null;
          lastCmpKind = null;
          lastCmpPredicate = null;
        }

        // Skip pure ops whose result is never used — dead code that survived DCE
        if (op.PureResultId >= 0 && !usedValueIds.Contains(op.PureResultId)) {
          currentOpIndex++;
          continue;
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

          case StdBinaryI64Op binOp:
            switch (binOp.Operator) {
              case StdBinaryOperator.Add:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86AddRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex),
                  useLeaForAdd: true);
                break;
              case StdBinaryOperator.Sub:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86SubRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.Mul:
                regManager.EmitMultiply(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.DivSigned:
                regManager.EmitDivision(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block);
                break;
              case StdBinaryOperator.RemSigned:
                regManager.EmitRemainder(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block);
                break;
              case StdBinaryOperator.DivUnsigned:
                regManager.EmitUnsignedDivision(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block);
                break;
              case StdBinaryOperator.RemUnsigned:
                regManager.EmitUnsignedRemainder(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block);
                break;
              case StdBinaryOperator.And:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86AndRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.Or:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86OrRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.Xor:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86XorRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.Shl:
                regManager.EmitShift(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  dest => new X86ShlRegClOp(dest));
                break;
              case StdBinaryOperator.ShrSigned:
                regManager.EmitShift(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  dest => new X86SarRegClOp(dest));
                break;
              case StdBinaryOperator.ShrUnsigned:
                regManager.EmitShift(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  dest => new X86ShrRegClOp(dest));
                break;
              default:
                throw new InvalidOperationException($"Unsupported I64 binary operator: {binOp.Operator}");
            }
            break;

          // === I32 Arithmetic ===

          case StdConstI32Op constI32Op:
            regManager.EmitLoadImmediate(constI32Op.Result, constI32Op.Value, x86Block);
            break;

          case StdBinaryI32Op binOp:
            switch (binOp.Operator) {
              case StdBinaryOperator.Add:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86AddRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex),
                  useLeaForAdd: true);
                break;
              case StdBinaryOperator.Sub:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86SubRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.Mul:
                regManager.EmitMultiply(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.DivSigned:
                regManager.EmitDivision32(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block);
                break;
              case StdBinaryOperator.RemSigned:
                regManager.EmitRemainder32(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block);
                break;
              case StdBinaryOperator.DivUnsigned:
                regManager.EmitUnsignedDivision32(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block);
                break;
              case StdBinaryOperator.RemUnsigned:
                regManager.EmitUnsignedRemainder32(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block);
                break;
              case StdBinaryOperator.And:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86AndRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.Or:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86OrRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.Xor:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86XorRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.Shl:
                regManager.EmitShift(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  dest => new X86ShlRegClOp(dest));
                break;
              case StdBinaryOperator.ShrSigned:
                regManager.EmitShift(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  dest => new X86SarRegClOp(dest));
                break;
              case StdBinaryOperator.ShrUnsigned:
                regManager.EmitShift(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  dest => new X86ShrRegClOp(dest));
                break;
              default:
                throw new InvalidOperationException($"Unsupported I32 binary operator: {binOp.Operator}");
            }
            break;

          case StdCmpI32Op cmpI32Op:
            regManager.EmitIntegerCompare(cmpI32Op.Lhs, cmpI32Op.Rhs, x86Block);
            lastCmpResult = cmpI32Op.Result;
            lastCmpKind = ComparisonKind.Integer;
            lastCmpPredicate = cmpI32Op.Predicate;
            break;

          case StdCmpU32Op cmpU32Op:
            regManager.EmitIntegerCompare(cmpU32Op.Lhs, cmpU32Op.Rhs, x86Block);
            lastCmpResult = cmpU32Op.Result;
            lastCmpKind = ComparisonKind.UnsignedInteger;
            lastCmpPredicate = cmpU32Op.Predicate;
            break;

          // === I32 Store/Load ===

          case StdStoreI32Op storeI32Op:
            if (!deadStoreOps.Contains(op))
              regManager.EmitStoreToStack(storeI32Op.Value, varOffsets[storeI32Op.VarName], 4, x86Block);
            break;

          case StdLoadI32Op loadI32Op:
            regManager.EmitLoadFromStack(loadI32Op.Result, varOffsets[loadI32Op.VarName], 4, x86Block);
            break;

          // === I32 Width Conversion ===

          case StdExtI32ToI64Op extOp:
            if (extOp.SignExtend)
              regManager.EmitSignExtendI32ToI64(extOp.Input, extOp.Result, x86Block);
            else
              regManager.EmitZeroExtendI32ToI64(extOp.Input, extOp.Result, x86Block);
            break;

          case StdTruncI64ToI32Op truncOp:
            regManager.EmitTruncI64ToI32(truncOp.Input, truncOp.Result, x86Block);
            break;

          // === I32 Float Conversion ===

          case StdSiToFpI32Op siToFpI32Op:
            regManager.EmitCvtSi2Sd(siToFpI32Op.Input, siToFpI32Op.Result, x86Block);
            break;

          case StdUiToFpI32Op uiToFpI32Op:
            regManager.EmitCvtSi2Sd(uiToFpI32Op.Input, uiToFpI32Op.Result, x86Block);
            break;

          case StdBinaryF64Op binOp:
            switch (binOp.Operator) {
              case StdBinaryOperator.Add:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86AddXmmOp(l, r, FloatPrecision.F64));
                break;
              case StdBinaryOperator.Sub:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86SubXmmOp(l, r, FloatPrecision.F64));
                break;
              case StdBinaryOperator.Mul:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86MulXmmOp(l, r, FloatPrecision.F64));
                break;
              case StdBinaryOperator.DivSigned:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86DivXmmOp(l, r, FloatPrecision.F64));
                break;
              case StdBinaryOperator.Min:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86MinXmmOp(l, r, FloatPrecision.F64));
                break;
              case StdBinaryOperator.Max:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86MaxXmmOp(l, r, FloatPrecision.F64));
                break;
              default:
                throw new InvalidOperationException($"Unsupported F64 binary operator: {binOp.Operator}");
            }
            break;

          // === F32 Arithmetic ===

          case StdBinaryF32Op binOp:
            switch (binOp.Operator) {
              case StdBinaryOperator.Add:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86AddXmmOp(l, r, FloatPrecision.F32));
                break;
              case StdBinaryOperator.Sub:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86SubXmmOp(l, r, FloatPrecision.F32));
                break;
              case StdBinaryOperator.Mul:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86MulXmmOp(l, r, FloatPrecision.F32));
                break;
              case StdBinaryOperator.DivSigned:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86DivXmmOp(l, r, FloatPrecision.F32));
                break;
              case StdBinaryOperator.Min:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86MinXmmOp(l, r, FloatPrecision.F32));
                break;
              case StdBinaryOperator.Max:
                regManager.EmitXmmBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86MaxXmmOp(l, r, FloatPrecision.F32));
                break;
              default:
                throw new InvalidOperationException($"Unsupported F32 binary operator: {binOp.Operator}");
            }
            break;

          case StdBinaryI1Op binOp:
            switch (binOp.Operator) {
              case StdBinaryOperator.And:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86AndRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.Or:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86OrRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              case StdBinaryOperator.Xor:
                regManager.EmitBinaryRegReg(binOp.Lhs, binOp.Rhs, binOp.Result, x86Block,
                  (l, r) => new X86XorRegRegOp(l, r),
                  lhsConsumed: IsLastUse(lastUseOfValue, binOp.Lhs, currentOpIndex));
                break;
              default:
                throw new InvalidOperationException($"Unsupported I1 binary operator: {binOp.Operator}");
            }
            break;

          case StdFpToSiOp fpToSiOp:
            regManager.EmitCvttSd2Si(fpToSiOp.Input, fpToSiOp.Result, x86Block);
            break;

          case StdBitcastF64ToI64Op bitcastOp:
            regManager.EmitMovqXmmToGpr(bitcastOp.Input, bitcastOp.Result, x86Block);
            break;

          case StdSiToFpOp siToFpOp:
            regManager.EmitCvtSi2Sd(siToFpOp.Input, siToFpOp.Result, x86Block);
            break;

          case StdUiToFpOp uiToFpOp:
            // For values < 2^63, CVTSI2SD works correctly for unsigned
            regManager.EmitCvtSi2Sd(uiToFpOp.Input, uiToFpOp.Result, x86Block);
            break;

          case StdFpToUiOp fpToUiOp:
            // For values < 2^63, CVTTSD2SI works correctly for unsigned
            regManager.EmitCvttSd2Si(fpToUiOp.Input, fpToUiOp.Result, x86Block);
            break;

          // === F32 Conversion ===

          case StdFpToSiF32Op fpToSiF32Op:
            regManager.EmitCvttSs2Si(fpToSiF32Op.Input, fpToSiF32Op.Result, x86Block);
            break;

          case StdFpToUiF32Op fpToUiF32Op:
            regManager.EmitCvttSs2Si(fpToUiF32Op.Input, fpToUiF32Op.Result, x86Block);
            break;

          case StdSiToFpF32Op siToFpF32Op:
            regManager.EmitCvtSi2Ss(siToFpF32Op.Input, siToFpF32Op.Result, x86Block);
            break;

          case StdUiToFpF32Op uiToFpF32Op:
            regManager.EmitCvtSi2Ss(uiToFpF32Op.Input, uiToFpF32Op.Result, x86Block);
            break;

          case StdF64ToF32Op f64ToF32Op:
            regManager.EmitCvtSd2Ss(f64ToF32Op.Input, f64ToF32Op.Result, x86Block);
            break;

          case StdF32ToF64Op f32ToF64Op:
            regManager.EmitCvtSs2Sd(f32ToF64Op.Input, f32ToF64Op.Result, x86Block);
            break;

          case StdAbsF64Op absOp: {
            var maskLabel = GetOrCreateAbsMask(outputModule);
            regManager.EmitAbsF64(absOp.Input, absOp.Result, maskLabel, x86Block);
            break;
          }

          case StdSqrtF64Op sqrtOp:
            regManager.EmitXmmUnaryRegReg(sqrtOp.Input, sqrtOp.Result, x86Block,
              (d, s) => new X86SqrtXmmOp(d, s, FloatPrecision.F64));
            break;

          case StdFloorF64Op floorOp:
            regManager.EmitXmmUnaryRegReg(floorOp.Input, floorOp.Result, x86Block,
              (d, s) => new X86RoundXmmOp(d, s, 0x01, FloatPrecision.F64));
            break;

          case StdCeilF64Op ceilOp:
            regManager.EmitXmmUnaryRegReg(ceilOp.Input, ceilOp.Result, x86Block,
              (d, s) => new X86RoundXmmOp(d, s, 0x02, FloatPrecision.F64));
            break;

          case StdRoundF64Op roundOp:
            regManager.EmitXmmUnaryRegReg(roundOp.Input, roundOp.Result, x86Block,
              (d, s) => new X86RoundXmmOp(d, s, 0x00, FloatPrecision.F64));
            break;

          // === F32 Math ===

          case StdAbsF32Op absF32Op:
            regManager.EmitAbsF32(absF32Op.Input, absF32Op.Result, absF32MaskLabel, x86Block);
            break;

          case StdSqrtF32Op sqrtF32Op:
            regManager.EmitXmmUnaryRegReg(sqrtF32Op.Input, sqrtF32Op.Result, x86Block,
              (d, s) => new X86SqrtXmmOp(d, s, FloatPrecision.F32));
            break;

          case StdFloorF32Op floorF32Op:
            regManager.EmitXmmUnaryRegReg(floorF32Op.Input, floorF32Op.Result, x86Block,
              (d, s) => new X86RoundXmmOp(d, s, 0x01, FloatPrecision.F32));
            break;

          case StdCeilF32Op ceilF32Op:
            regManager.EmitXmmUnaryRegReg(ceilF32Op.Input, ceilF32Op.Result, x86Block,
              (d, s) => new X86RoundXmmOp(d, s, 0x02, FloatPrecision.F32));
            break;

          case StdRoundF32Op roundF32Op:
            regManager.EmitXmmUnaryRegReg(roundF32Op.Input, roundF32Op.Result, x86Block,
              (d, s) => new X86RoundXmmOp(d, s, 0x00, FloatPrecision.F32));
            break;

          case StdConstF64Op floatOp: {
            var label = GetOrCreateFloatLabel(floatOp.Value, outputModule, floatConstants);
            regManager.EmitXmmLoadFromRipRelative(floatOp.Result, label, x86Block);
            break;
          }

          case StdConstF32Op floatF32Op: {
            var label = GetOrCreateFloat32Label(floatF32Op.Value, outputModule, float32Constants);
            regManager.EmitXmmLoadFromRipRelativeF32(floatF32Op.Result, label, x86Block);
            break;
          }

          case StdBulkZeroOp bulkZeroOp: {
            if (bulkZeroOp.ZeroInit) {
              var baseOffset = FindFirstFieldOffset(bulkZeroOp.Tag, varOffsets);
              regManager.EmitBulkZero(baseOffset, bulkZeroOp.QwordCount, x86Block);
            }
            // When !ZeroInit, stack space is reserved by field offsets but not cleared
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

          case StdStoreF32Op storeF32Op:
            if (!deadStoreOps.Contains(op))
              regManager.EmitXmmStoreToStackF32(storeF32Op.Value, varOffsets[storeF32Op.VarName], x86Block);
            break;

          case StdLoadF32Op loadF32Op:
            regManager.EmitXmmLoadFromStackF32(loadF32Op.Result, varOffsets[loadF32Op.VarName], x86Block);
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

          case StdCmpU64Op cmpU64Op:
            regManager.EmitIntegerCompare(cmpU64Op.Lhs, cmpU64Op.Rhs, x86Block);
            lastCmpResult = cmpU64Op.Result;
            lastCmpKind = ComparisonKind.UnsignedInteger;
            lastCmpPredicate = cmpU64Op.Predicate;
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

          case StdCmpF32Op cmpF32Op:
            regManager.EmitXmmCompareF32(cmpF32Op.Lhs, cmpF32Op.Rhs, x86Block);
            lastCmpResult = cmpF32Op.Result;
            lastCmpKind = ComparisonKind.Float;
            lastCmpPredicate = cmpF32Op.Predicate;
            break;

          case StdCondBrOp condBr: {
            var scopedElse = $"{func.Name}.{condBr.ElseBlock}";
            if (twoJumpCondBrs.TryGetValue(condBr, out var tj)) {
              // Two-jump optimization: emit cmp+jcc for each bound, jumping to
              // the then (panic) block. Explicit jmp to else (ok) after both pass.
              // Emit upper check (cmp2) first so each constant load is adjacent to its cmp.
              var scopedThen = $"{func.Name}.{condBr.ThenBlock}";
              EmitCmpFromOp(tj.Cmp2, regManager, x86Block);
              x86Block.AddOp(new X86JccOp(IntegerPredicateToJcc(tj.Pred2, tj.Kind2), scopedThen));
              EmitCmpFromOp(tj.Cmp1, regManager, x86Block);
              x86Block.AddOp(new X86JccOp(IntegerPredicateToJcc(tj.Pred1, tj.Kind1), scopedThen));
              x86Block.AddOp(new X86JmpOp(scopedElse));
              // Free the cmp operand values that have reached their last use
              foreach (var cmpOp in new[] { tj.Cmp1, tj.Cmp2 })
                FreeDeadValues(regManager, lastUseOfValue, currentOpIndex, cmpOp.ReadValues);
            } else if (lastCmpResult != null && condBr.Condition == lastCmpResult) {
              switch (lastCmpKind!.Value) {
                case ComparisonKind.Integer:
                  x86Block.AddOp(new X86JccOp(InvertIntegerPredicate(lastCmpPredicate!), scopedElse));
                  break;
                case ComparisonKind.UnsignedInteger:
                  x86Block.AddOp(new X86JccOp(InvertUnsignedPredicate(lastCmpPredicate!), scopedElse));
                  break;
                case ComparisonKind.Float:
                  EmitFloatCondBranch(lastCmpPredicate!, scopedElse, x86Block, newFunc);
                  break;
                default:
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

          case StdSwitchOp switchOp: {
            var indexReg = regManager.LoadToRegister(switchOp.Scrutinee, x86Block);
            var tableLabel = $"__jt_{func.Name}_{NextLabelId()}";
            var scopedTargets = switchOp.CaseTargets
                .Select(t => $"{func.Name}.{t}").ToArray();
            var scopedDefault = $"{func.Name}.{switchOp.DefaultTarget}";

            x86Block.AddOp(new X86JumpTableOp(indexReg, switchOp.CaseTargets.Length,
                tableLabel, scopedDefault, scopedTargets));

            outputModule.RdataEntries.Add((tableLabel, new byte[switchOp.CaseTargets.Length * 4], 4));
            break;
          }

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

          case StdLeaSymdataOp leaSymdataOp: {
            regManager.EmitLeaSymdataRelative(leaSymdataOp.Result, leaSymdataOp.SymdataLabel, x86Block);
            break;
          }

          case StdLeaUcddataOp leaUcddataOp: {
            regManager.EmitLeaUcddataRelative(leaUcddataOp.Result, leaUcddataOp.UcddataLabel, x86Block);
            break;
          }

          case StdStoreIndirectOp storeIndOp:
            regManager.EmitStoreIndirect(storeIndOp.Value, storeIndOp.BasePtr, storeIndOp.FieldOffset, storeIndOp.FieldType, x86Block);
            break;

          case StdLoadIndirectOp loadIndOp:
            regManager.EmitLoadIndirect(loadIndOp.Result, loadIndOp.BasePtr, loadIndOp.FieldOffset, loadIndOp.FieldType, x86Block);
            break;

          case StdNullSafeLoadI64Op nsLoadOp:
            regManager.EmitNullSafeLoadI64(nsLoadOp.Result, nsLoadOp.BasePtr, nsLoadOp.FieldOffset, x86Block);
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

          case StdGlobalLoadF32Op globalLoadF32:
            regManager.EmitXmmGlobalLoadF32(globalLoadF32.Result, globalLoadF32.GlobalName, x86Block);
            break;

          case StdGlobalStoreF32Op globalStoreF32:
            regManager.EmitXmmGlobalStoreF32(globalStoreF32.Value, globalStoreF32.GlobalName, x86Block);
            break;

          case StdGlobalLoadI1Op globalLoadI1:
            regManager.EmitGlobalLoad(globalLoadI1.Result, globalLoadI1.GlobalName, x86Block, size: 1);
            break;

          case StdGlobalStoreI1Op globalStoreI1:
            regManager.EmitGlobalStore(globalStoreI1.Value, globalStoreI1.GlobalName, x86Block, size: 1);
            break;

          case StdGlobalLoadI8Op globalLoadI8:
            regManager.EmitGlobalLoad(globalLoadI8.Result, globalLoadI8.GlobalName, x86Block, size: 1);
            break;

          case StdGlobalStoreI8Op globalStoreI8:
            regManager.EmitGlobalStore(globalStoreI8.Value, globalStoreI8.GlobalName, x86Block, size: 1);
            break;

          case StdGlobalLoadI16Op globalLoadI16:
            regManager.EmitGlobalLoad(globalLoadI16.Result, globalLoadI16.GlobalName, x86Block, size: 2);
            break;

          case StdGlobalStoreI16Op globalStoreI16:
            regManager.EmitGlobalStore(globalStoreI16.Value, globalStoreI16.GlobalName, x86Block, size: 2);
            break;

          case StdReturnOp retOp: {
            if (tailCalls.TryGetValue(op, out var tailCallOp)) {
              regManager.EmitTailCall(tailCallOp.Callee, tailCallOp.Args, x86Block);
              break;
            }
            if (retOp.ReturnValue != null) {
              if (retOp.ReturnValue is StdF64 or StdF32) {
                regManager.EnsureInXmm0ForReturn(retOp.ReturnValue, x86Block);
              } else if (retOp.ReturnValue is StdPtr) {
                // Pointers use 64-bit RAX
                regManager.EnsureInSpecificRegister(retOp.ReturnValue, X86Register.Rax, x86Block);
              } else if (retOp.ReturnValue is StdI64 or StdI32 or StdBool) {
                regManager.EnsureInSpecificRegister(retOp.ReturnValue, X86Register.Rax, x86Block);
              } else {
                throw new InvalidOperationException($"StandardToX86: unsupported return type {retOp.ReturnValue.GetType().Name}");
              }
            }
            // In throwing functions, a normal return means success: set error flag (RDX) to 0
            if (func.ThrowsType != null) {
              x86Block.AddOp(new X86XorRegRegOp(X86Register.Rdx, X86Register.Rdx));
            }

            x86Block.AddOp(new X86EpilogueOp());
            x86Block.AddOp(new X86RetOp());
            break;
          }

          case StdErrorReturnOp errRetOp: {
            // Put error flag into RDX, zero into RAX (dummy return value)
            regManager.EnsureInSpecificRegister(errRetOp.ErrorFlag, X86Register.Rdx, x86Block);
            x86Block.AddOp(new X86XorRegRegOp(X86Register.Rax, X86Register.Rax));
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

          case StdTryCallRuntimeOp tryRuntimeCallOp: {
            // Runtime try-calls: result in RAX, error flag in RDX (same convention as StdTryCallOp)
            regManager.EmitTryCall(tryRuntimeCallOp.Callee, tryRuntimeCallOp.Args, tryRuntimeCallOp.Result, tryRuntimeCallOp.ErrorFlag, x86Block,
              ConsumedArgs(tryRuntimeCallOp.Args, lastUseOfValue, currentOpIndex));
            break;
          }

          case StdCallRuntimeIfNonnullOp guardedCallOp: {
            // Null-guarded runtime call: skip if first arg is null.
            // Spill all live register values before the branch so values
            // remain accessible when the branch skips the call body.
            regManager.SpillAllLiveRegisters(x86Block);
            var skipPrefix = _inStdlib ? "__stdlib_nn_skip" : "__nonnull_skip";
            var skipLabel = $"{skipPrefix}_{_nonnullSkipCounter++}";
            regManager.EmitBoolTest(guardedCallOp.Args[0], x86Block);
            x86Block.AddOp(new X86JccOp("z", skipLabel));
            regManager.EmitCall(guardedCallOp.Callee, guardedCallOp.Args, guardedCallOp.Result, x86Block,
              ConsumedArgs(guardedCallOp.Args, lastUseOfValue, currentOpIndex));
            x86Block.AddOp(new X86LabelDefOp(skipLabel));
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

          case StdMemCopyReverseOp memCopyRevOp: {
            // Copy byteCount bytes from src to dst backwards (for overlapping shift-right)
            regManager.EmitMemCopyReverse(memCopyRevOp.SrcPtr, memCopyRevOp.DstPtr, memCopyRevOp.ByteCount, x86Block);
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

      prevX86Block = x86Block;
      prevBlockIdx = blockIdx;
    }

    // Insert prologue/epilogue only when a stack frame is needed.
    // Any function with calls needs a frame for correct stack unwinding (e.g., panic stack traces).
    int stackSize = regManager.TotalStackSize;
    bool hasCalls = newFunc.Body.Blocks.Any(b => b.Operations.Any(op => op is X86CallDirectOp));
    if (hasCalls && stackSize == 0) stackSize = 16;
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

  private static string InvertUnsignedPredicate(string predicate) => predicate switch {
    "eq" => "ne",
    "ne" => "e",
    "ult" => "ae",
    "uge" => "b",
    "ule" => "a",
    "ugt" => "be",
    _ => throw new InvalidOperationException($"Unknown unsigned comparison predicate: {predicate}")
  };

  private static string UnsignedPredicateToSetcc(string predicate) => predicate switch {
    "eq" => "e",
    "ne" => "ne",
    "ult" => "b",
    "ugt" => "a",
    "ule" => "be",
    "uge" => "ae",
    _ => throw new InvalidOperationException($"Unknown unsigned comparison predicate: {predicate}")
  };

  /// Map a comparison predicate to a jcc condition code (non-inverted — jump when true).
  private static string IntegerPredicateToJcc(string predicate, ComparisonKind kind) => kind switch {
    ComparisonKind.Integer => IntegerPredicateToSetcc(predicate),
    ComparisonKind.UnsignedInteger => UnsignedPredicateToSetcc(predicate),
    _ => throw new InvalidOperationException($"Unsupported comparison kind for two-jump: {kind}")
  };

  /// Emit a cmp instruction from a previously-skipped comparison op.
  private static void EmitCmpFromOp(StandardOp cmpOp, RegisterManager regManager, MlirBlock<X86Op> block) {
    var (lhs, rhs) = cmpOp switch {
      StdCmpI64Op c => ((StdValue)c.Lhs, (StdValue)c.Rhs),
      StdCmpU64Op c => (c.Lhs, c.Rhs),
      StdCmpI32Op c => (c.Lhs, c.Rhs),
      StdCmpU32Op c => (c.Lhs, c.Rhs),
      _ => throw new InvalidOperationException($"Unsupported cmp op in two-jump pattern: {cmpOp.GetType().Name}")
    };
    regManager.EmitIntegerCompare(lhs, rhs, block);
  }

  /// <summary>
  /// Emit conditional branch(es) for a float comparison.
  /// ucomisd sets CF/ZF/PF flags. We jump to elseLabel when the condition is FALSE.
  /// For ordered comparisons, unordered (NaN) falls through to else.
  /// </summary>
  private static void EmitFloatCondBranch(string predicate, string elseLabel,
      MlirBlock<X86Op> block, MlirFunction<X86Op> func) {
    // ucomisd(A, B) flag results:
    //   A > B:  ZF=0, PF=0, CF=0
    //   A < B:  ZF=0, PF=0, CF=1
    //   A == B: ZF=1, PF=0, CF=0
    //   NaN:    ZF=1, PF=1, CF=1
    switch (predicate) {
      case "eq":
        // false when ZF=0 or PF=1 (not-equal or NaN)
        block.AddOp(new X86JccOp("ne", elseLabel));
        block.AddOp(new X86JccOp("p", elseLabel));
        break;
      case "ne": {
        // true when ZF=0 (not-equal) or PF=1 (NaN). false only when ZF=1 and PF=0.
        // jp skip; je else; skip: — PF=1 (NaN) skips the je, so NaN != x is true
        var skipBlockName = $"__fcmp_ne_skip_{_floatBranchCounter++}";
        var scopedSkip = $"{func.Name}.{skipBlockName}";
        block.AddOp(new X86JccOp("p", scopedSkip));
        block.AddOp(new X86JccOp("e", elseLabel));
        func.Body.AddBlock(skipBlockName);
        break;
      }
      case "gt":
        // false when CF=1 or ZF=1 (below-or-equal, or NaN)
        block.AddOp(new X86JccOp("be", elseLabel));
        break;
      case "ge":
        // false when CF=1 (below, or NaN)
        block.AddOp(new X86JccOp("b", elseLabel));
        break;
      case "lt":
        // false when PF=1 (NaN) or CF=0 (above-or-equal)
        block.AddOp(new X86JccOp("p", elseLabel));
        block.AddOp(new X86JccOp("ae", elseLabel));
        break;
      case "le":
        // false when PF=1 (NaN) or (CF=0 and ZF=0) (above)
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
    // Check if the variable itself has a direct offset (primitive/scalar variables)
    if (varOffsets.TryGetValue(structVarName, out var directOffset))
      return directOffset;
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

  private static string GetOrCreateFloat32Label(float value, MlirModule<X86Op> module, Dictionary<float, string> float32Constants) {
    if (!float32Constants.TryGetValue(value, out var label)) {
      label = $"__float32_{value.ToString(CultureInfo.InvariantCulture)}";
      float32Constants[value] = label;
      module.RdataEntries.Add((label, BitConverter.GetBytes(value), 1));
    }
    return label;
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

}
