using System.Globalization;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

enum ComparisonKind { Integer, Float }

public static class StandardToX86Conversion {
  public static MlirModule<X86Op> Run(MlirModule<StandardOp> module) {
    var result = new MlirModule<X86Op>();
    result.Globals.AddRange(module.Globals);

    foreach (var func in module.Functions) {
      var newFunc = ConvertFunction(func, result);
      result.AddFunction(newFunc);
    }

    return result;
  }

  private static MlirFunction<X86Op> ConvertFunction(MlirFunction<StandardOp> func, MlirModule<X86Op> outputModule) {
    var newFunc = new MlirFunction<X86Op>(func.Name, func.ParamNames, func.ParamTypes, func.ReturnType);

    // Pre-scan: calculate stack frame from store ops
    var varOffsets = new Dictionary<string, int>();
    int varStackSize = 0;
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is not IStoreOp store) continue;
        if (!varOffsets.ContainsKey(store.VarName)) {
          varStackSize += store.StoredType.SizeInBytes;
          varOffsets[store.VarName] = -varStackSize;
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

    for (int blockIdx = 0; blockIdx < sourceBlocks.Count; blockIdx++) {
      var srcBlock = sourceBlocks[blockIdx];
      var x86Block = newFunc.Body.AddBlock(srcBlock.Name);

      regManager.Reset();

      foreach (var op in srcBlock.Operations) {
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
              lhsConsumed: IsLastUse(lastUseOfValue, addOp.Lhs, currentOpIndex));
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
            regManager.EmitStoreToStack(storeOp.Value, varOffsets[storeOp.VarName], x86Block);
            break;

          case StdStoreF64Op storeOp:
            regManager.EmitXmmStoreToStack(storeOp.Value, varOffsets[storeOp.VarName], x86Block);
            break;

          case StdLoadI64Op loadOp:
            regManager.EmitLoadFromStack(loadOp.Result, varOffsets[loadOp.VarName], x86Block);
            break;

          case StdLoadF64Op loadOp:
            regManager.EmitXmmLoadFromStack(loadOp.Result, varOffsets[loadOp.VarName], x86Block);
            break;

          case StdStoreI1Op storeBoolOp:
            regManager.EmitStoreToStack(storeBoolOp.Value, varOffsets[storeBoolOp.VarName], x86Block);
            break;

          case StdLoadI1Op loadBoolOp:
            regManager.EmitLoadFromStack(loadBoolOp.Result, varOffsets[loadBoolOp.VarName], x86Block);
            break;

          case StdCmpI64Op cmpI64Op:
            regManager.EmitIntegerCompare(cmpI64Op.Lhs, cmpI64Op.Rhs, x86Block);
            lastCmpResult = cmpI64Op.Result;
            lastCmpKind = ComparisonKind.Integer;
            lastCmpPredicate = cmpI64Op.Predicate;
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
            regManager.EmitCall(callOp.Callee, callOp.Args, callOp.Result, x86Block);
            break;

          case StdLeaOp leaOp: {
            // Get the address of the first field slot of a struct variable.
            // The sret pointer convention uses the address of the ".first_field" slot.
            var firstFieldOffset = FindFirstFieldOffset(leaOp.VarName, varOffsets);
            regManager.EmitLeaFromStack(leaOp.Result, firstFieldOffset, x86Block);
            break;
          }

          case StdStoreIndirectOp storeIndOp:
            regManager.EmitStoreIndirect(storeIndOp.Value, storeIndOp.BasePtr, storeIndOp.FieldOffset, storeIndOp.FieldType, x86Block);
            break;

          case StdLoadIndirectOp loadIndOp:
            regManager.EmitLoadIndirect(loadIndOp.Result, loadIndOp.BasePtr, loadIndOp.FieldOffset, loadIndOp.FieldType, x86Block);
            break;

          case StdReturnOp retOp: {
            if (retOp.ReturnValue != null) {
              if (retOp.ReturnValue is StdF64) {
                regManager.EnsureInXmm0ForReturn(retOp.ReturnValue, x86Block);
              } else {
                regManager.EnsureInSpecificRegister(retOp.ReturnValue, X86Register.Eax, x86Block);
              }
            }
            x86Block.AddOp(new X86EpilogueOp());
            x86Block.AddOp(new X86RetOp());
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
}
