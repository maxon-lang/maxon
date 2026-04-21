using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Rewrites simple `for x in arr` loops (where arr is an Array-aliased type) to use
/// direct index-based access into the underlying __ManagedMemory, eliminating the
/// ArrayIterator allocation and the per-iteration advance()/current() function calls.
///
/// The parser emits for-in loops with a rigid 4-block CFG (entry/preamble/header/body/exit).
/// After MonomorphizationPass resolves the deferred iterator ops to concrete callees
/// like __ArrayIterator_Int.advance / .current, this pass recognizes that shape when
/// the iterator type name starts with __ArrayIterator_ or its source alias is
/// ArrayIterator, and rewrites to:
///
///   entry:
///     <original ops up to and including the createIterator try_call>
///     len = arr.managed.length  (field_access)
///     idx = 0
///     br header
///
///   header:
///     cond = idx < len
///     cond_br cond [body, exit]
///
///   body:
///     elem = managed_mem_get(arr.managed, idx)  [bounds-check-safe]
///     <original body, minus the iterator current() load>
///     idx = idx + 1
///     br header
///
/// The preamble block becomes unreachable and is removed. The iterator variable
/// (__for_iter_N) is dropped from any MaxonScopeEndOp that references it, since
/// it no longer exists.
///
/// Bails out (leaves the loop unchanged) whenever the shape doesn't match exactly
/// or the iterator variable is referenced outside the protocol calls (e.g. iter.index()).
/// </summary>
public static class ForLoopIteratorElisionPass {
  public static void Run(IrModule<MaxonOp> module) {
    int loopsElided = 0;
    foreach (var func in module.Functions) {
      // Newly minted MaxonValues during rewrite belong to the function's id namespace
      // (stdlib bit set when func.IsStdlib). Without this flip, rewriting a stdlib
      // for-loop would advance the user-side counter even though the new ops live in
      // a stdlib function.
      var prevMode = IrContext.Current.StdlibLoweringMode;
      IrContext.Current.StdlibLoweringMode = func.IsStdlib;
      try {
        loopsElided += TransformFunction(module, func);
      } finally {
        IrContext.Current.StdlibLoweringMode = prevMode;
      }
    }
    if (loopsElided > 0)
      Logger.Debug(LogCategory.Ir, $"ForLoopIteratorElision: rewrote {loopsElided} array for-loop(s)");
  }

  private static int TransformFunction(IrModule<MaxonOp> module, IrFunction<MaxonOp> func) {
    int elided = 0;
    // Walk blocks by index; a successful rewrite may invalidate the blocks-after-entry
    // (preamble removed), so we re-scan from the current position when we transform.
    for (int bi = 0; bi < func.Body.Blocks.Count; bi++) {
      var entryBlock = func.Body.Blocks[bi];
      var match = TryMatchArrayForLoop(module, func, entryBlock);
      if (match == null) continue;
      RewriteLoop(func, match);
      elided++;
      // Stay at the same index; the entry block is still there, just with new terminator.
      // The preamble has been removed from Blocks, shifting subsequent indices down by one.
    }
    return elided;
  }

  // ================== Pattern match ==================

  private class LoopMatch {
    public IrBlock<MaxonOp> EntryBlock = null!;
    public IrBlock<MaxonOp> PreambleBlock = null!;
    public IrBlock<MaxonOp> HeaderBlock = null!;
    public IrBlock<MaxonOp> BodyBlock = null!;
    // Index in entry block where the createIterator try_call lives (inclusive start
    // of the block suffix we replace).
    public int CreateCallIndex;
    // Iterator variable name (e.g. __for_iter_1).
    public string IterVarName = "";
    // The iterable value passed to createIterator (e.g. a MaxonStruct for `arr`).
    public MaxonValue IterableValue = null!;
    // Concrete struct type name of the iterable (e.g. IntArray).
    public string IterableTypeName = "";
    // Element kind (from the ArrayIterator's current() signature).
    public MaxonValueKind ElementKind;
    // TypeParamName when element is TypeParameter.
    public string? ElementTypeParamName;
    // Body op indices delimiting the [struct_var_ref iter .. assign __forin_result_N]
    // range we replace with a managed_mem_get + assign.
    public int BodyIterRefIdx;
    public int BodyForinResultAssignIdx;
    // The __forin_result_N variable name (so we can redirect item's source).
    public string ForinResultVarName = "";
    // Kind metadata on the __forin_result_N assign (to preserve typing).
    public MaxonValueKind ForinResultKind;
  }

  private static LoopMatch? TryMatchArrayForLoop(IrModule<MaxonOp> module, IrFunction<MaxonOp> func, IrBlock<MaxonOp> entry) {
    // We're looking for the entry-block tail:
    //   ... %iter, %errflag = maxon.try_call @<X>.createIterator <iterable>
    //   maxon.assign %iter {var = __for_iter_N} {decl=1, mut=1}
    //   %zero = maxon.literal 0
    //   %cmp = maxon.binop %errflag, %zero {op = eq}
    //   maxon.cond_br %cmp [then: <preamble>, else: <exit>]
    // where <preamble> has a single `maxon.br <body>`,
    // and <body>.header has a parallel shape around advance().
    var ops = entry.Operations;
    if (ops.Count < 5) return null;
    if (ops[^1] is not MaxonCondBrOp entryCondBr) return null;

    // Walk backwards to find the try_call + assign pair that creates the iterator var.
    // Expected tail (in order): try_call, assign, literal(0), binop(eq), cond_br.
    int condBrIdx = ops.Count - 1;
    if (ops[condBrIdx - 1] is not MaxonBinOp entryCmp) return null;
    if (entryCmp.Operator != MaxonBinOperator.Eq) return null;
    if (ops[condBrIdx - 2] is not MaxonLiteralOp zeroLit) return null;
    if (zeroLit.ValueKind != MaxonValueKind.Integer || zeroLit.IntValue != 0) return null;

    // The assign of the iterator var comes just before the literal(0).
    if (ops[condBrIdx - 3] is not MaxonAssignOp iterAssign) return null;
    if (!iterAssign.IsDeclaration || !iterAssign.IsMutable) return null;
    if (!iterAssign.VarName.StartsWith("__for_iter_")) return null;

    // The try_call must be just before the assign (or, if the parser ever inserts
    // extra ops between them, we search a small window).
    MaxonTryCallOp? createCall = null;
    int createCallIdx = -1;
    for (int k = condBrIdx - 4; k >= 0 && k >= condBrIdx - 8; k--) {
      if (ops[k] is MaxonTryCallOp tc && tc.Result != null && tc.Result.Id == iterAssign.Value.Id) {
        createCall = tc;
        createCallIdx = k;
        break;
      }
    }
    if (createCall == null) return null;
    // The error flag tested by entryCmp must be the one produced by createCall.
    if (entryCmp.Lhs.Id != createCall.ErrorFlag.Id && entryCmp.Rhs.Id != createCall.ErrorFlag.Id) return null;
    if (entryCondBr.Condition.Id != entryCmp.Result.Id) return null;

    // The callee must be <X>.createIterator where X resolves to an Array-aliased type.
    if (!createCall.Callee.EndsWith(".createIterator")) return null;
    var iterableTypeName = createCall.Callee[..^".createIterator".Length];

    // Resolve alias to ensure this is an Array.
    if (!IsArrayAlias(module, iterableTypeName, out var arrayStructType)) return null;
    if (arrayStructType.GetField("managed") is null) return null;

    // Determine element kind from the Array's Element type parameter.
    if (!arrayStructType.TypeParams.TryGetValue("Element", out var elementType)) return null;
    var (elemKind, _, elemTypeParam) = ClassifyElementType(elementType);
    // Initial scope: skip struct/enum/typeparam elements — they require incref + ownership
    // threading, which the existing cursor-based path handles centrally but which we'd need
    // to duplicate here. Leave those loops on the unoptimized path.
    if (elemKind != MaxonValueKind.Integer && elemKind != MaxonValueKind.Float
        && elemKind != MaxonValueKind.Float32 && elemKind != MaxonValueKind.Bool
        && elemKind != MaxonValueKind.Byte && elemKind != MaxonValueKind.Short) {
      return null;
    }

    // Find preamble block by name (cond_br's "then" target). The preamble is a
    // single-op block that jumps to the body; the header derives its label from
    // the body label.
    var preamble = FindBlock(func, entryCondBr.ThenBlock);
    if (preamble == null) return null;
    if (preamble.Operations.Count != 1) return null;
    if (preamble.Operations[0] is not MaxonBrOp preambleBr) return null;
    var bodyBlockName = preambleBr.Target;
    var headerBlockName = $"{bodyBlockName}.header";
    var header = FindBlock(func, headerBlockName);
    if (header == null) return null;

    // Header shape: struct_var_ref iterVar, try_call @<...>.advance %iterRef, assign __try_error_N,
    // literal 0, binop eq, cond_br [body, exit].
    var hops = header.Operations;
    if (hops.Count < 6) return null;
    if (hops[^1] is not MaxonCondBrOp headerCondBr) return null;
    if (headerCondBr.ThenBlock != bodyBlockName) return null;
    if (headerCondBr.ElseBlock != entryCondBr.ElseBlock) return null;
    if (hops[^2] is not MaxonBinOp hCmp) return null;
    if (hCmp.Operator != MaxonBinOperator.Eq) return null;
    if (hops[^3] is not MaxonLiteralOp hZero) return null;
    if (hZero.ValueKind != MaxonValueKind.Integer || hZero.IntValue != 0) return null;
    if (hops[^4] is not MaxonAssignOp hErrAssign) return null;
    if (!hErrAssign.VarName.StartsWith("__try_error_")) return null;

    // The advance call must produce hErrAssign.Value (the error flag) and take the iterator as arg.
    MaxonTryCallOp? advanceCall = null;
    for (int k = hops.Count - 5; k >= 0; k--) {
      if (hops[k] is MaxonTryCallOp tc
          && tc.ErrorFlag.Id == hErrAssign.Value.Id) {
        advanceCall = tc;
        break;
      }
    }
    if (advanceCall == null) return null;
    if (!advanceCall.Callee.Contains(".advance")) return null;
    if (advanceCall.Args.Count != 1) return null;

    // The advance call's arg must be a struct_var_ref to the iterator variable, produced
    // somewhere earlier in the header.
    if (!IsStructVarRefTo(header, advanceCall.Args[0], iterAssign.VarName)) return null;

    // Body shape: struct_var_ref iterVar, call @<...>.current %iterRef, assign __forin_result_N,
    // var_ref __forin_result_N, assign item, ..., br header.
    var body = FindBlock(func, bodyBlockName);
    if (body == null) return null;
    var bops = body.Operations;
    if (bops.Count < 4) return null;

    // Locate: the struct_var_ref(__for_iter_N), the current() call, and the forin_result assign.
    int bIterRefIdx = -1;
    int bCurrentCallIdx = -1;
    int bForinResultAssignIdx = -1;
    MaxonCallOp? currentCall = null;
    MaxonAssignOp? forinResultAssign = null;
    for (int k = 0; k < bops.Count; k++) {
      if (bIterRefIdx < 0 && bops[k] is MaxonStructVarRefOp svr && svr.VarName == iterAssign.VarName) {
        bIterRefIdx = k;
      } else if (bIterRefIdx >= 0 && bCurrentCallIdx < 0 && bops[k] is MaxonCallOp cc
                 && cc.Callee.EndsWith(".current") && cc.Args.Count == 1
                 && cc.Args[0].Id == ((MaxonStructVarRefOp)bops[bIterRefIdx]).Result.Id) {
        currentCall = cc;
        bCurrentCallIdx = k;
      } else if (bCurrentCallIdx >= 0 && bForinResultAssignIdx < 0 && bops[k] is MaxonAssignOp ba
                 && ba.IsDeclaration && ba.VarName.StartsWith("__forin_result_")
                 && ba.Value.Id == currentCall!.Result!.Id) {
        forinResultAssign = ba;
        bForinResultAssignIdx = k;
        break;
      }
    }
    if (currentCall == null || forinResultAssign == null) return null;

    // The iterator variable must not be referenced anywhere else in the function
    // (outside entry/header/body's recognized ops). If it is, bail — the iterator
    // escapes the simple for-loop protocol (e.g. someone calls iter.index()).
    if (IteratorVarIsReferencedElsewhere(func, iterAssign.VarName,
          allowedEntry: entry, allowedHeader: header, allowedBody: body,
          allowedBodyIndices: [bIterRefIdx],
          allowedEntryAssignIdx: condBrIdx - 3)) {
      return null;
    }

    return new LoopMatch {
      EntryBlock = entry,
      PreambleBlock = preamble,
      HeaderBlock = header,
      BodyBlock = body,
      CreateCallIndex = createCallIdx,
      IterVarName = iterAssign.VarName,
      IterableValue = createCall.Args[0],
      IterableTypeName = iterableTypeName,
      ElementKind = elemKind,
      ElementTypeParamName = elemTypeParam,
      BodyIterRefIdx = bIterRefIdx,
      BodyForinResultAssignIdx = bForinResultAssignIdx,
      ForinResultVarName = forinResultAssign.VarName,
      ForinResultKind = forinResultAssign.ValueKind,
    };
  }

  // ================== Rewrite ==================

  private static void RewriteLoop(IrFunction<MaxonOp> func, LoopMatch m) {
    // Naming: pull a unique-within-function counter off the iterator variable name.
    // __for_iter_1 → "1". Reuse as "__for_idx_1" / "__for_len_1".
    var suffix = m.IterVarName["__for_iter_".Length..];
    var idxVar = $"__for_idx_{suffix}";
    var lenVar = $"__for_len_{suffix}";

    // ----- Entry block rewrite -----
    // Drop ops from CreateCallIndex..end (the try_call, assign to __for_iter, literal(0),
    // binop eq, cond_br). Append: field_access(.managed), field_access(.length),
    // assign __for_len_N = length, assign __for_idx_N = 0, br header.
    var entryOps = m.EntryBlock.Operations;
    entryOps.RemoveRange(m.CreateCallIndex, entryOps.Count - m.CreateCallIndex);

    // %managed = field_access .managed <iterable>
    var managedAccess = new MaxonFieldAccessOp(m.IterableValue, m.IterableTypeName, "managed",
      MaxonValueKind.Struct, "__ManagedMemory");
    entryOps.Add(managedAccess);
    // We also need to make the managed value addressable as a struct variable so the
    // MaxonManagedMem* lowering can use ResolveManagedVarName to look it up. Mirror
    // what the parser does when a .managed field flows into ManagedMem ops: assign it
    // into a named var, then emit a struct_var_ref before each use.
    var managedVar = $"__for_mm_{suffix}";
    entryOps.Add(new MaxonAssignOp(managedVar, managedAccess.Result,
      isDeclaration: true, isMutable: false, MaxonValueKind.Struct));
    // Length = managed.length (field access on __ManagedMemory struct).
    var managedRefForLen = new MaxonStructVarRefOp(managedVar, "__ManagedMemory");
    entryOps.Add(managedRefForLen);
    var lenAccess = new MaxonFieldAccessOp(managedRefForLen.Result, "__ManagedMemory", "length",
      MaxonValueKind.Integer);
    entryOps.Add(lenAccess);
    entryOps.Add(new MaxonAssignOp(lenVar, lenAccess.Result,
      isDeclaration: true, isMutable: false, MaxonValueKind.Integer));
    // idx = 0.
    var zeroLit = new MaxonLiteralOp(0L);
    entryOps.Add(zeroLit);
    entryOps.Add(new MaxonAssignOp(idxVar, zeroLit.Result,
      isDeclaration: true, isMutable: true, MaxonValueKind.Integer));
    // br header.
    entryOps.Add(new MaxonBrOp(m.HeaderBlock.Name));

    // ----- Header block rewrite -----
    // Replace with: idx_val = var_ref idx; len_val = var_ref len; cmp = idx_val < len_val;
    // cond_br cmp [body, exit].
    var hOps = m.HeaderBlock.Operations;
    // Preserve the target labels from the original cond_br.
    var origHeaderCondBr = (MaxonCondBrOp)hOps[^1];
    hOps.Clear();
    var idxRef = new MaxonVarRefOp(idxVar, MaxonValueKind.Integer);
    hOps.Add(idxRef);
    var lenRef = new MaxonVarRefOp(lenVar, MaxonValueKind.Integer);
    hOps.Add(lenRef);
    var cmp = new MaxonBinOp(MaxonBinOperator.Lt, idxRef.Result, lenRef.Result,
      MaxonValueKind.Integer);
    hOps.Add(cmp);
    hOps.Add(new MaxonCondBrOp(cmp.Result, origHeaderCondBr.ThenBlock, origHeaderCondBr.ElseBlock));

    // ----- Body block rewrite -----
    // Replace: [struct_var_ref iterVar @ BodyIterRefIdx .. forin_result assign @ BodyForinResultAssignIdx]
    // with:    struct_var_ref managedVar; idx_ref; managed_mem_get(managed, idx); assign forin_result.
    // Keep the forin_result's assign VarName so everything downstream (item = forin_result) works.
    var bOps = m.BodyBlock.Operations;
    int replaceFrom = m.BodyIterRefIdx;
    int replaceCount = m.BodyForinResultAssignIdx - m.BodyIterRefIdx + 1;
    bOps.RemoveRange(replaceFrom, replaceCount);

    var newOps = new List<MaxonOp>();
    var mmRef = new MaxonStructVarRefOp(managedVar, "__ManagedMemory");
    newOps.Add(mmRef);
    var bodyIdxRef = new MaxonVarRefOp(idxVar, MaxonValueKind.Integer);
    newOps.Add(bodyIdxRef);
    var getOp = new MaxonManagedMemGetOp(mmRef.Result, bodyIdxRef.Result, m.ElementKind) {
      IsBoundsCheckSafe = true,
      TypeParamName = m.ElementTypeParamName,
    };
    newOps.Add(getOp);
    newOps.Add(new MaxonAssignOp(m.ForinResultVarName, getOp.Result,
      isDeclaration: true, isMutable: true, m.ForinResultKind));
    for (int k = 0; k < newOps.Count; k++) {
      bOps.Insert(replaceFrom + k, newOps[k]);
    }

    // Insert `idx += 1` before every back-edge to the header. The body may have
    // internal control flow (e.g. `if v` inside the loop), so the actual back-edge
    // can live in a successor block, not the body block itself. We scan every
    // block reachable from the body (excluding entry/header/exit and the body-
    // internal preheader we just replaced) for a `maxon.br <headerName>` terminator
    // and insert the increment just before it.
    InsertIncrementAtBackEdges(func, m.HeaderBlock.Name, m.EntryBlock, idxVar);

    // ----- Preamble block removal -----
    func.Body.Blocks.Remove(m.PreambleBlock);

    // ----- Replace iterator var with the managed-memory var in ScopeEndOp cleanup lists -----
    // The iterator variable no longer exists. In its place we introduced __for_mm_N which
    // holds an incref'd __ManagedMemory reference that must be decref'd at scope exit.
    // Scope_end lists that previously mentioned the iterator get the managed-memory var
    // substituted in its position, preserving cleanup ordering.
    ReplaceVarInScopeEnds(func, m.IterVarName, managedVar, "__ManagedMemory");
    // The advance()'s error flag var (__try_error_N) becomes dead — only written in the
    // header (now rewritten) and consumed by the header's comparison (also rewritten).
    // Leave it in scope_ends: scope_end lowering filters out vars that aren't managed.
  }

  // ================== Helpers ==================

  private static bool IsArrayAlias(IrModule<MaxonOp> module, string typeName, out IrStructType arrayStruct) {
    arrayStruct = null!;
    // Follow the alias chain to find the source type; it must be "Array" (stdlib).
    var current = typeName;
    for (int hop = 0; hop < 8; hop++) {
      if (module.TypeAliasSources.TryGetValue(current, out var info)) {
        current = info.SourceTypeName;
        if (current == "Array") break;
        continue;
      }
      // Not an alias — could be the monomorphized type itself if TypeDefs has it.
      break;
    }
    // Look up the original type (the alias carries the type-param bindings; the original
    // Array struct carries the field/interface metadata).
    if (!module.TypeDefs.TryGetValue(typeName, out var typeDef)) {
      // Try the aliased source.
      if (!module.TypeDefs.TryGetValue(current, out typeDef)) return false;
    }
    if (typeDef is not IrStructType st) return false;
    // Require an Iterable conformance (Array implements Iterable with (Element, ArrayIter)).
    // This filters out non-array types that happen to have a `.managed` field.
    if (!st.ConformingInterfaces.Contains("Iterable")
        && !st.ConformingInterfaces.Contains("BuiltinArrayLiteral")) return false;
    if (st.GetField("managed") == null) return false;
    arrayStruct = st;
    return true;
  }

  private static (MaxonValueKind Kind, string? StructTypeName, string? TypeParamName) ClassifyElementType(IrType elementType) {
    if (elementType is IrTypeParameterType tp) {
      // Unresolved type parameter — treat as TypeParameter kind.
      return (MaxonValueKind.TypeParameter, null, tp.ParameterName);
    }
    if (elementType is IrStructType st) return (MaxonValueKind.Struct, st.Name, null);
    if (elementType is IrEnumType en) return (MaxonValueKind.Enum, en.Name, null);
    // Ranged typealiases (e.g. `Offset = int(0 to u64.max)`) wrap a primitive;
    // classify by the underlying base type.
    var baseType = IrType.Resolve(elementType);
    if (baseType == IrType.I64) return (MaxonValueKind.Integer, null, null);
    if (baseType == IrType.I32) return (MaxonValueKind.Integer, null, null);
    if (baseType == IrType.I16) return (MaxonValueKind.Short, null, null);
    if (baseType == IrType.I8) return (MaxonValueKind.Byte, null, null);
    if (baseType == IrType.I1) return (MaxonValueKind.Bool, null, null);
    if (baseType == IrType.F64) return (MaxonValueKind.Float, null, null);
    if (baseType == IrType.F32) return (MaxonValueKind.Float32, null, null);
    // Anything else (Void, Fn, unsigned variants, or future types) — bail to
    // the unoptimized iterator path rather than miscompiling. Returning the
    // Struct kind forces the caller's element-kind filter to reject the match.
    return (MaxonValueKind.Struct, null, null);
  }

  private static IrBlock<MaxonOp>? FindBlock(IrFunction<MaxonOp> func, string name) {
    foreach (var b in func.Body.Blocks) if (b.Name == name) return b;
    return null;
  }

  private static bool IsStructVarRefTo(IrBlock<MaxonOp> block, MaxonValue val, string varName) {
    foreach (var op in block.Operations) {
      if (op is MaxonStructVarRefOp svr && svr.Result.Id == val.Id)
        return svr.VarName == varName;
    }
    return false;
  }

  private static bool IteratorVarIsReferencedElsewhere(
      IrFunction<MaxonOp> func, string iterVarName,
      IrBlock<MaxonOp> allowedEntry, IrBlock<MaxonOp> allowedHeader, IrBlock<MaxonOp> allowedBody,
      int[] allowedBodyIndices, int allowedEntryAssignIdx) {
    foreach (var block in func.Body.Blocks) {
      for (int k = 0; k < block.Operations.Count; k++) {
        var op = block.Operations[k];
        // struct_var_ref to the iterator var: only allowed at specific indices.
        if (op is MaxonStructVarRefOp svr && svr.VarName == iterVarName) {
          if (block == allowedHeader) {
            // The advance() call's struct_var_ref sits in the header — allowed.
            continue;
          }
          if (block == allowedBody && allowedBodyIndices.Contains(k)) continue;
          return true;
        }
        // var_ref to the iterator var — disallowed anywhere (iterator is a struct, but
        // robustly check both forms).
        if (op is MaxonVarRefOp vr && vr.VarName == iterVarName) return true;
        // Assignment to the iterator var outside the entry allowed slot — disallowed.
        if (op is MaxonAssignOp a && a.VarName == iterVarName) {
          if (block == allowedEntry && k == allowedEntryAssignIdx) continue;
          return true;
        }
      }
    }
    return false;
  }

  private static void InsertIncrementAtBackEdges(IrFunction<MaxonOp> func, string headerBlockName, IrBlock<MaxonOp> entryBlock, string idxVar) {
    foreach (var block in func.Body.Blocks) {
      if (block == entryBlock) continue; // we just wrote entry's `br header` — not a back-edge
      if (block.Name == headerBlockName) continue; // header never branches to itself
      var ops = block.Operations;
      for (int k = ops.Count - 1; k >= 0; k--) {
        if (ops[k] is not MaxonBrOp br || br.Target != headerBlockName) continue;
        // Found a back-edge. Insert idx += 1 just before it, but after any
        // trailing scope_end so cleanup ordering is preserved.
        int insertPoint = k;
        while (insertPoint - 1 >= 0 && ops[insertPoint - 1] is MaxonScopeEndOp) insertPoint--;
        var idxRef = new MaxonVarRefOp(idxVar, MaxonValueKind.Integer);
        var oneLit = new MaxonLiteralOp(1L);
        var addOp = new MaxonBinOp(MaxonBinOperator.Add, idxRef.Result, oneLit.Result,
          MaxonValueKind.Integer);
        ops.Insert(insertPoint, idxRef);
        ops.Insert(insertPoint + 1, oneLit);
        ops.Insert(insertPoint + 2, addOp);
        ops.Insert(insertPoint + 3, new MaxonAssignOp(idxVar, addOp.Result,
          isDeclaration: false, isMutable: true, MaxonValueKind.Integer));
        // Only one back-edge per block (the parser doesn't emit multiple); stop.
        break;
      }
    }
  }

  private static void ReplaceVarInScopeEnds(IrFunction<MaxonOp> func, string oldVar, string newVar, string newStructTypeName) {
    for (int bi = 0; bi < func.Body.Blocks.Count; bi++) {
      var block = func.Body.Blocks[bi];
      for (int k = 0; k < block.Operations.Count; k++) {
        if (block.Operations[k] is not MaxonScopeEndOp se) continue;
        if (!se.VarsToClean.Contains(oldVar)) continue;
        var newList = se.VarsToClean.Select(v => v == oldVar ? newVar : v).ToList();
        Dictionary<string, (OwnershipFlags, string?)>? newMeta = null;
        if (se.VarMetadata != null) {
          newMeta = [];
          foreach (var kv in se.VarMetadata) {
            if (kv.Key == oldVar) {
              // Carry the same ownership flags so cleanup behavior matches (the
              // managed-memory var was produced by a field access assigned into a
              // named variable, which ends up treated as an owned managed struct).
              newMeta[newVar] = (kv.Value.Flags, newStructTypeName);
            } else {
              newMeta[kv.Key] = kv.Value;
            }
          }
        }
        var replacement = new MaxonScopeEndOp(newList, se.KeepVars) {
          VarMetadata = newMeta,
        };
        block.Operations[k] = replacement;
      }
    }
  }
}
