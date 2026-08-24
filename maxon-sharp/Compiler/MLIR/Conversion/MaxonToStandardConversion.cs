using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
  [ThreadStatic] private static IrModule<StandardOp>? _resultModule;
  // The Maxon module being lowered. Read where a lowering has to ask a question about the WHOLE
  // program rather than about the op in front of it — today, which function a managed element is
  // deep-cloned through (ManagedElementCopy), a verdict that must match the one dead-function
  // elimination already pinned.
  [ThreadStatic] private static IrModule<MaxonOp>? _sourceModule;
  // Target the conversion is lowering for; drives platform-specific decisions
  // such as the Win32-vs-POSIX errno→ordinal mapping table used by the throwing
  // __ManagedFile / __ManagedDirectory builtins.
  [ThreadStatic] private static CompileTarget? _currentTarget;
  [ThreadStatic] private static Dictionary<string, string>? _rdataStringCache;
  // Maps param name -> ref pointer var name for the current function being lowered
  [ThreadStatic] private static Dictionary<string, string>? _refParamPtrVars;
  // Tracks struct parameter names for the current function (not owned by us, no cleanup needed)
  [ThreadStatic] private static HashSet<string>? _structParamNames;
  [ThreadStatic] private static Dictionary<string, string>? _stackVarTags;
  // Stack-allocated struct variables of the function being lowered, and the module's
  // stack-eligibility verdicts. Both are needed by the call lowering (a two-register value
  // tuple materialises into stack slots when its result does not escape), which lives in a
  // sibling partial and does not otherwise see the per-function conversion state.
  [ThreadStatic] private static HashSet<string>? _stackAllocatedVars;
  [ThreadStatic] private static HashSet<int>? _stackEligibleStructs;
  // ValueTupleAbiPass's verdict: which functions hand their result back in two registers.
  // Empty when that pass has not run, which reads as "every function returns a heap record" —
  // the convention that predates this ABI, so both ends still agree.
  [ThreadStatic] private static HashSet<string>? _valueTupleReturnFunctions;
  // Halves of a to-be-returned value tuple, read at scope_end while the record is still live.
  // Keyed by the returned MaxonValue's id. See the scope_end lowering for why.
  [ThreadStatic] private static Dictionary<int, (StdValue Low, StdValue High)>? _valueTupleReturnStash;
  [ThreadStatic] private static int _nextRdataId;
  [ThreadStatic] private static int _nextStdlibRdataId;
  [ThreadStatic] private static bool _rdataStdlibPhase;
  [ThreadStatic] private static HashSet<string>? _loadedUcdLabels;
  [ThreadStatic] private static string? _currentFuncName;
  [ThreadStatic] private static string? _currentFuncSourceFile;
  [ThreadStatic] private static int? _currentFuncSourceLine;
  [ThreadStatic] private static Dictionary<string, string>? _symdataContextCache;
  // Tracks type destructor functions that need to be generated (one per concrete type).
  [ThreadStatic] private static Dictionary<string, DestructorRequest>? _destructorRequests;
  private static string NextRdataId() =>
    _rdataStdlibPhase ? $"s{_nextStdlibRdataId++}" : $"{_nextRdataId++}";

  public static IrModule<StandardOp> Run(IrModule<MaxonOp> module, CompileTarget? target = null) {
    _sourceModule = module;
    _currentTarget = target ?? CompileTarget.Default;
    _valueTupleReturnFunctions = module.ValueTupleReturnFunctions;
    _rdataStringCache = [];
    _symdataTagCache = [];
    _tagIndexMap = [];
    _nextTagIndex = 1;
    ResetDebugStreamNames();
    _symdataContextCache = [];
    _destructorRequests = [];
    _destructorLabelCache = [];
    _loadedUcdLabels = [];
    _rdataStdlibPhase = true;
    _nextStdlibRdataId = 0;
    _nextRdataId = 0;
    ResetStaticLiteralState(module.StaticEligibleLiteralIds);
    var result = new IrModule<StandardOp>();
    _resultModule = result;
    result.EntryFunctionName = module.EntryFunctionName;
    result.RdataEntries.AddRange(module.RdataEntries);
    result.Globals.AddRange(module.Globals);
    foreach (var (k, v) in module.TypeDefs) result.TypeDefs[k] = v;
    foreach (var (k, v) in module.TypeAliasSources) result.TypeAliasSources.TryAdd(k, v);

    // Resolve ranged primitive types so lowering sees base types (i64/f64/i8)
    foreach (var (_, typeDef) in module.TypeDefs) {
      if (typeDef is IrStructType st)
        foreach (var field in st.Fields)
          field.Type = IrType.Resolve(field.Type);
    }
    foreach (var func in module.Functions) {
      if (func.ReturnType is IrRangedPrimitiveType rptRet)
        func.ReturnType = rptRet.OptimalType;
      for (int i = 0; i < func.ParamTypes.Count; i++)
        if (func.ParamTypes[i] is IrRangedPrimitiveType rptParam)
          func.ParamTypes[i] = rptParam.BaseType;
    }

    // Build a lookup of functions by name for struct-aware call lowering
    var funcLookup = module.Functions.ToDictionary(f => f.Name);

    // Parameter mutation analysis is done by ParameterMutationAnalysisPass (runs earlier in pipeline).
    // ReassignedParams, MutatedParams, and MutatedParamIndices are already set on each function.

    bool hasResetAfterStdlib = false;

    foreach (var func in module.Functions) {
      // Skip synthetic builtin method stubs — they exist for type validation only
      if (func.IsBuiltinSynthetic) continue;

      // Skip generic functions that still have unresolved type parameters —
      // these are source templates that were monomorphized into concrete specializations.
      if (HasUnresolvedTypeParameters(func, module)) {
        continue;
      }
      // Wrap each function's lowering in try/catch so an unexpected exception
      // (typically an InvalidCastException from a mistyped StdValue) traces
      // back to a specific function instead of surfacing as a bare runtime
      // cast at the top of `Run`. The body keeps its existing indent — the
      // try is a thin wrapper around the entire loop body.
      try {
      // Bias newly-minted ids during stdlib function lowering with the stdlib bit so
      // they stay disjoint from user-side ids in any per-function valueMap. The cached
      // stdlib's parser-emitted MaxonValues already carry the bit (stdlib parsed inside
      // an isStdlibContext IrContext); flipping the mode makes lowering-time MaxonValues
      // and StdValues conform to the same namespace.
      IrContext.Current.StdlibLoweringMode = func.IsStdlib;

      // At the stdlib/user boundary, also reset the rdata bookkeeping so user-code
      // string literals get small, stable ids (`0`, `1`, ...) instead of continuing
      // from the stdlib phase counter.
      if (!hasResetAfterStdlib && !func.IsStdlib) {
        _rdataStdlibPhase = false;
        _nextRdataId = 0;
        _rdataStringCache = [];
        hasResetAfterStdlib = true;
      }

      var retStructType = ResolveStructReturnType(func.ReturnType, module.TypeDefs);
      _currentFuncName = func.Name;
      _currentFuncSourceFile = func.SourceFilePath;
      _currentFuncSourceLine = func.SourceLine;
      bool isStructInstanceMethod = IsStructInstanceMethod(func);
      bool isEnumInstanceMethod = IsEnumInstanceMethod(func);
      bool isInstanceMethod = isStructInstanceMethod || isEnumInstanceMethod;
      var selfStructType = isStructInstanceMethod ? ResolveStructType((IrStructType)func.ParamTypes[0], module.TypeDefs) : null;

      // Only reassigned params get pointer indirection; others stay by-value for zero overhead
      var refParamPtrVars = new Dictionary<string, string>();
      if (func.ReassignedParams != null) {
        for (int i = 0; i < func.ParamNames.Count; i++) {
          if (func.ParamNames[i] == "self") continue;
          if (func.ReassignedParams.Contains(func.ParamNames[i])) {
            refParamPtrVars[func.ParamNames[i]] = $"__ref_{func.ParamNames[i]}";
          }
        }
      }

      // Build the new function signature:
      // - Struct instance method 'self' param is passed as a heap pointer (i64)
      // - Simple enum/union instance method 'self' is passed as a scalar
      // - Associated-value union instance method 'self' is passed as a heap
      //   pointer (i64) — same shape as a non-self assoc-value enum/union arg,
      //   because the receiver is heap-allocated (its associated values live
      //   off the stack).
      // - Other struct params are passed as heap pointers (i64)
      // - Simple enum params are passed as scalars
      // - Associated-value enum/union params are passed as heap pointers (i64)
      // - Struct return is an i64 heap pointer returned normally
      var newParamNames = new List<string>();
      var newParamTypes = new List<IrType>();

      // Map from original struct param index to its flat param index (pointer slot)
      var structParamPtrIndex = new Dictionary<int, int>();
      // Map from original param index to flat param index (for all params)
      var paramFlatIndex = new Dictionary<int, int>();
      int flatIdx = newParamNames.Count;

      for (int i = 0; i < func.ParamNames.Count; i++) {
        paramFlatIndex[i] = flatIdx;
        if (isStructInstanceMethod && i == 0) {
          // Struct instance method self param: pass as pointer (i64)
          newParamNames.Add("__self_ptr");
          newParamTypes.Add(IrType.I64);
          flatIdx++;
        } else if (isEnumInstanceMethod && i == 0) {
          // Enum/union instance method self param. Simple enums pass as a
          // scalar (i64/f64 backing); unions with associated values pass as
          // a heap pointer (i64) so the receiver carries its payload.
          var enumType = (IrEnumType)func.ParamTypes[0];
          if (enumType.HasAssociatedValues) {
            structParamPtrIndex[i] = flatIdx;
            newParamNames.Add("self");
            newParamTypes.Add(IrType.I64);
          } else {
            var backingIrType = ResolveEnumBackingIrType(enumType);
            newParamNames.Add("self");
            newParamTypes.Add(backingIrType);
          }
          flatIdx++;
        } else if (func.ParamTypes[i] is IrEnumType { HasAssociatedValues: true }) {
          // Associated-value enum param: pass as heap pointer (i64), like structs
          structParamPtrIndex[i] = flatIdx;
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(IrType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is IrEnumType enumParamType) {
          // Simple enum param: pass as scalar (or i64 pointer if mutated)
          var backingIrType = ResolveEnumBackingIrType(enumParamType);
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(refParamPtrVars.ContainsKey(func.ParamNames[i]) ? IrType.I64 : backingIrType);
          flatIdx++;
        } else if (func.ParamTypes[i] is IrStructType or IrInterfaceType) {
          // Non-self struct/interface param: pass as pointer (i64)
          structParamPtrIndex[i] = flatIdx;
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(IrType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is IrFunctionType) {
          // Function-typed param: fn_ptr + hidden env_ptr (2 slots)
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(IrType.I64);
          flatIdx++;
          newParamNames.Add(ClosureEnvSlotName(func.ParamNames[i]));
          newParamTypes.Add(IrType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is not IrStructType and not IrEnumType) {
          newParamNames.Add(func.ParamNames[i]);
          // Mutated params receive a pointer (i64) instead of the original type
          newParamTypes.Add(refParamPtrVars.ContainsKey(func.ParamNames[i]) ? IrType.I64 : func.ParamTypes[i]);
          flatIdx++;
        } else {
          throw new InvalidOperationException($"Unhandled parameter type: {func.ParamTypes[i].GetType().Name} for param '{func.ParamNames[i]}'");
        }
      }

      IrType? newReturnType;
      if (retStructType != null) {
        // Struct return: return heap pointer as i64
        newReturnType = IrType.I64;
      } else if (func.ReturnType is IrInterfaceType) {
        // Interface return: the returned value is a heap pointer to a
        // concrete implementation, same ABI as a struct return.
        newReturnType = IrType.I64;
      } else if (func.ReturnType is IrEnumType { HasAssociatedValues: true }) {
        // Associated-value enum return: return heap pointer as i64
        newReturnType = IrType.I64;
      } else if (func.ReturnType is IrEnumType retEnumType) {
        newReturnType = ResolveEnumBackingIrType(retEnumType);
      } else if (func.ReturnType is not IrStructType and not IrEnumType) {
        newReturnType = func.ReturnType;
      } else {
        throw new InvalidOperationException($"Unhandled return type: {func.ReturnType.GetType().Name} in function '{func.Name}'");
      }
      var newFunc = new IrFunction<StandardOp>(func.Name, newParamNames, newParamTypes, newReturnType, func.ThrowsType) {
        IsStdlib = func.IsStdlib
      };
      newFunc.CopySourceAnchorFrom(func);
      var valueMap = new Dictionary<MaxonValue, StdValue>();
      var literalMap = new Dictionary<MaxonValue, MaxonLiteralOp>();
      var varTypes = new Dictionary<string, string>();
      // Maps function pointer StdValue IDs to the variable name holding the env_ptr
      var fnEnvVarNames = new Dictionary<int, string>();
      // Direct env_ptr values (avoids store/load when value is already in a register)
      var fnEnvDirectValues = new Dictionary<int, StdI64>();
      // Bindings whose closure-env slot holds a reference THIS function took, and must drop at the
      // binding's scope_end. A function-typed PARAMETER's slot is deliberately absent: it names the
      // caller's environment, which is borrowed for the call and released by whoever owns it.
      var ownedEnvSlots = new HashSet<string>();
      // Maps variable names to their resolved struct prefix (for cross-block references)
      var varNameToStructPrefix = new Dictionary<string, string>();
      // Maps variable names to their struct type name (for monomorphized type parameter vars)
      var varNameToStructType = new Dictionary<string, string>();
      // Variables that are stack-allocated structs (skip refcounting and scope cleanup)
      var stackAllocatedVars = new HashSet<string>();
      // Maps stack-allocated variable name to its BulkZero tag (for direct field access)
      var stackVarTags = new Dictionary<string, string>();
      _stackVarTags = stackVarTags;
      _stackAllocatedVars = stackAllocatedVars;
      _stackEligibleStructs = module.StackEligibleStructs;
      _valueTupleReturnStash = [];
      _varNameToStructType = varNameToStructType;
      // Debug-info (--debug-info only): the per-function local NAME -> SOURCE type name table, filled
      // by EmitStore as the body lowers and joined with the machine slot offsets at emit. Gated on
      // !IsStdlib to match the line/span capture (which is `DebugInfo && !isStdlib`) — only user code
      // gets local records; stdlib/runtime internals are not the user's debugging surface.
      //
      // Seed params with their ORIGINAL type, before the ABI erases a struct/enum param to an i64
      // pointer — the one place a struct/enum PARAMETER's real type is still spelled out (ranged-alias
      // params were already flattened to their base at the top of Run, so those honestly bind to base).
      // Sealing the param names makes their later i64-pointer store a no-op for capture, so it is not
      // mistaken for a conflicting redefinition of the slot.
      bool captureDebugLocals = Compiler.DebugInfo && !func.IsStdlib;
      var debugLocalTypes = captureDebugLocals ? new Dictionary<string, string>() : null;
      var debugSealedLocalNames = captureDebugLocals ? new HashSet<string>() : null;
      _debugLocalTypes = debugLocalTypes;
      _debugSealedLocalNames = debugSealedLocalNames;
      if (debugLocalTypes != null) {
        for (int pi = 0; pi < func.ParamNames.Count; pi++) {
          debugLocalTypes[func.ParamNames[pi]] = func.ParamTypes[pi].Name;
          debugSealedLocalNames!.Add(func.ParamNames[pi]);
        }
      }
      var temps = new VarRegistry();
      // Use pre-computed constant array literal metadata from ConstantArrayAnalysisPass
      // Key: struct literal result ID, Value: ConstantArrayLiteralInfo

      _refParamPtrVars = refParamPtrVars;
      _structParamNames = [];

      // Tracks parameter names (used to distinguish params from locals in some paths)
      var structParamNames = new HashSet<string>();

      // Cache self-field loads: maps "fieldName" → temp var name for struct-typed self fields.
      // Avoids redundant load_indirect from self's heap pointer for the same field within a block.
      // Reset per-block since a cached var stored in one branch may not be defined in another.
      var selfFieldCache = new Dictionary<string, string>();

      // Tracks temp vars created for self-field accesses (e.g. __field_1234 for keys).
      // When a sibling method call may mutate self-fields, these temps must also be reloaded.
      var selfFieldTempVars = new Dictionary<string, string>();

      // Lazy-static init blocks, collected here and emitted after every source block is converted.
      // They cannot be emitted where they are discovered — see the emit site for why.
      var pendingLazyInits = new List<(string InitLabel, string InitFuncName, string MergeLabel)>();

      foreach (var block in func.Body.Blocks) {
        selfFieldCache.Clear();
        var newBlock = newFunc.Body.AddBlock(block.Name);

        // Debug-info span propagation (metadata only). Record, per Maxon op, where its lowered
        // Standard ops begin in newBlock; ranges between marks inherit the op's span. Recorded at
        // the top of the op loop so a `continue` inside the switch cannot skip it.
        var spanMarks = Compiler.DebugInfo ? new List<(int, SourceSpan)>() : null;

        // The block those marks are indexes INTO, which is not always `newBlock`: lowering a single
        // Maxon op can replace `newBlock` with a fresh merge block (a bounds check, a divide-by-zero
        // guard, a `try` error edge — see the `ref newBlock` helpers), and marks taken before that
        // belong to the block being left behind. Tracked so the switch can be seen and stamped at;
        // see DebugSpanFlow's remarks for what interpreting a mark against the wrong block cost.
        var spanMarkBlock = newBlock;

        // Snapshot cross-block dictionaries before this block starts processing.
        // Some lowering paths mutate entries to reflect block-local context (e.g.
        // updating valueMap[%arg] to a freshly-loaded SSA value after a call
        // mutated the caller's local). Those mutations are correct for uses within
        // the current block but invalid for sibling blocks: the new SSA value is
        // defined here and doesn't dominate siblings. After processing this block,
        // restore any pre-existing entries so a later sibling sees the original
        // dominating definition rather than a non-dominating one from this block.
        // New entries (defined inside this block) are kept since their key is a
        // unique SSA Result that didn't exist before.
        var valueMapSnapshot = new Dictionary<MaxonValue, StdValue>(valueMap);
        var varNameToStructPrefixSnapshot = new Dictionary<string, string>(varNameToStructPrefix);
        var selfFieldTempVarsSnapshot = new Dictionary<string, string>(selfFieldTempVars);

        // Pre-scan: find heap-allocating ops immediately consumed by declaration assigns
        // so they can store directly into the target variable, avoiding a temp.
        // Only for declarations — reassignments need managed cleanup of the old value
        // before the new fields are stored, so they must use an intermediate.
        var inlineTargets = new Dictionary<int, string>();
        for (int oi = 0; oi < block.Operations.Count - 1; oi++) {
          int? resultId = block.Operations[oi] switch {
            MaxonStructLiteralOp s => s.Result.Id,
            MaxonStringLiteralOp s => s.Result.Id,
            MaxonByteStringLiteralOp b => b.Result.Id,
            MaxonCharLiteralOp c => c.Result.Id,
            MaxonStringInterpOp i => i.Result.Id,
            MaxonCStringToManagedOp c => c.Result.Id,
            MaxonEnumConstructOp e => e.Result.Id,
            MaxonManagedListCreateOp c => c.Result.Id,
            _ => null
          };
          if (resultId != null
            && block.Operations[oi + 1] is MaxonAssignOp assign
            && assign.Value.Id == resultId
            && assign.IsDeclaration
            && !module.StackEligibleStructs.Contains(resultId.Value)
            // Array-literal element slots (`__arr_<tag>.<i>`) must end up at
            // contiguous stack offsets so `lea` + `memcpy` over `[rbp-N..rbp-N+count*8)`
            // hits every element. Inlining a literal directly into one of these
            // slots only works for the assign that fires last in source order
            // (its slot is allocated AFTER all the in-between literal temps);
            // for any earlier assign in the reversed `__arr_<tag>.<i--..0>` loop,
            // the slot would land before the other temps, leaving a hole in
            // the buffer. Skip inlining for `__arr_*` targets entirely so the
            // copy path lays slots out contiguously.
            && !assign.VarName.StartsWith("__arr_")) {
            inlineTargets[resultId.Value] = assign.VarName;
          }
        }

        // Pre-scan: find struct literal result IDs consumed as field values of another
        // struct literal. These get incref'd by the parent field store (line ~700) so they
        // must NOT receive a second incref or scope cleanup registration.
        var structLitFieldValueIds = new HashSet<int>();
        // Pre-scan: find struct literal / enum construct result IDs directly returned or thrown.
        // LowerReturn/LowerThrow handle their ownership transfer, so they must not be
        // incref'd or cleaned up here.
        var structLitReturnIds = new HashSet<int>();
        foreach (var op in block.Operations) {
          if (op is MaxonStructLiteralOp parentLit) {
            foreach (var (_, fieldVal) in parentLit.FieldValues) {
              structLitFieldValueIds.Add(fieldVal.Id);
            }
          } else if (op is MaxonReturnOp retOp && retOp.Value != null) {
            structLitReturnIds.Add(retOp.Value.Id);
          } else if (op is MaxonThrowOp throwOp) {
            structLitReturnIds.Add(throwOp.ErrorValue.Id);
          }
        }

        // Pre-scan: envelope collapse for fused Array/Vector. An array literal `[a,b,c]` and an
        // empty `Array.create()` both lower as `Array{managed: M}` where M is a FRESH __ManagedMemory
        // struct literal (holding element_size and, for a literal, the element buffer). Since the
        // fused Array IS its __ManagedMemory, M's separate allocation is redundant: the wrapper's
        // construction writes M's fields inline (and sets up the buffer on itself). Map the wrapper's
        // result id -> M's op, and mark M for suppression so it never allocates. `Self{managed: X}`
        // where X is a REAL source (init/clone/slice) is NOT absorbed — it becomes a view instead.
        var absorbedManagedLit = new Dictionary<int, MaxonStructLiteralOp>();
        var suppressedStructLitIds = new HashSet<int>();
        // Integer literal constants by result id — lets the array-fusion decision read an absorbed
        // managed literal's compile-time `element_size` (and hence the inline byte budget) directly.
        var intLiteralByResult = new Dictionary<int, long>();
        foreach (var op in block.Operations)
          if (op is MaxonLiteralOp lit && lit.ValueKind == MaxonValueKind.Integer)
            intLiteralByResult[lit.Result.Id] = lit.IntValue;
        {
          var structLitByResult = new Dictionary<int, MaxonStructLiteralOp>();
          foreach (var op in block.Operations)
            if (op is MaxonStructLiteralOp sl) structLitByResult[sl.Result.Id] = sl;
          foreach (var op in block.Operations) {
            if (op is not MaxonStructLiteralOp wrapper) continue;
            if (!module.TypeDefs.TryGetValue(wrapper.TypeName, out var wtd)
                || wtd is not IrStructType wst
                || !wst.ConformingInterfaces.Contains("BuiltinArrayLiteral")) continue;
            MaxonValue? managedVal = null;
            foreach (var (fn, fv) in wrapper.FieldValues) if (fn == "managed") managedVal = fv;
            if (managedVal != null
                && structLitByResult.TryGetValue(managedVal.Id, out var innerLit)
                && TypeAliasInfo.IsManagedMemoryType(innerLit.TypeName, module.TypeAliasSources)) {
              absorbedManagedLit[wrapper.Result.Id] = innerLit;
              suppressedStructLitIds.Add(innerLit.Result.Id);
            }
          }
        }

        // Pre-scan: detect contiguous zero-init array element sequences that can
        // be replaced with a single StdBulkZeroOp during lowering.
        var bulkZeroSkipOps = new HashSet<MaxonOp>();
        var bulkZeroEmitPoints = new Dictionary<MaxonOp, (string tag, int count)>();
        {
          int i = 0;
          var ops = block.Operations;
          while (i < ops.Count - 1) {
            // Look for: MaxonLiteralOp(0) + MaxonAssignOp(__tag.N, isDecl).
            // Bit-packed bool slots (ValueKind == Bool) are excluded: the bit-packed
            // buffer-patch branch reads each `__arr_*.i` slot by name to pack it, so
            // collapsing them into a StdBulkZeroOp would erase the vars it loads —
            // a sized bool Vector.create() (>= 8 zero-init bool slots) otherwise fails
            // to lower ("key '__arr_*.0' was not present in the dictionary").
            if (ops[i] is MaxonLiteralOp lit0
                && lit0.ValueKind == MaxonValueKind.Integer && lit0.IntValue == 0
                && ops[i + 1] is MaxonAssignOp assign0
                && assign0.Value.Id == lit0.Result.Id
                && assign0.IsDeclaration
                && assign0.ValueKind != MaxonValueKind.Bool) {
              var dotIdx = assign0.VarName.IndexOf('.');
              if (dotIdx >= 0 && assign0.VarName.StartsWith("__arr_")) {
                var tag = assign0.VarName[..dotIdx];
                int groupStart = i;
                int count = 0;
                // Collect all consecutive zero-init pairs with the same tag
                while (i < ops.Count - 1
                    && ops[i] is MaxonLiteralOp litN
                    && litN.ValueKind == MaxonValueKind.Integer && litN.IntValue == 0
                    && ops[i + 1] is MaxonAssignOp assignN
                    && assignN.Value.Id == litN.Result.Id
                    && assignN.IsDeclaration
                    && assignN.ValueKind != MaxonValueKind.Bool
                    && assignN.VarName.StartsWith($"{tag}.")) {
                  count++;
                  i += 2;
                }
                if (count >= 8) {
                  // Mark all ops in the group for skipping
                  for (int j = groupStart; j < groupStart + count * 2; j++)
                    bulkZeroSkipOps.Add(ops[j]);
                  // First op in group triggers bulk zero emission
                  bulkZeroEmitPoints[ops[groupStart]] = (tag, count);
                }
                continue;
              }
            }
            i++;
          }
        }

        foreach (var op in block.Operations) {
          // A block lowering has moved past is COMPLETE — nothing appends to it again — so its final
          // size is the last mark's end and stamping it here loses nothing by being early.
          if (spanMarks != null && !ReferenceEquals(spanMarkBlock, newBlock)) {
            DebugSpanFlow.AssignRange(newFunc, spanMarkBlock, spanMarks);
            spanMarks.Clear();
            spanMarkBlock = newBlock;
          }

          DebugSpanFlow.Mark(spanMarks, func, op, newBlock);

          if (bulkZeroSkipOps.Contains(op)) {
            if (bulkZeroEmitPoints.TryGetValue(op, out var bzInfo))
              newBlock.AddOp(new StdBulkZeroOp(bzInfo.tag, bzInfo.count));
            continue;
          }
          switch (op) {
            case MaxonParamOp paramOp: {
              // A generic type-parameter monomorphized to a function type stays a
              // MaxonParamOp carrying a Function kind (MaxonValueKind has no
              // function-type payload) rather than becoming a MaxonFunctionParamOp.
              // Its lowered ABI still occupies two slots (fn ptr + hidden env ptr),
              // exactly as the signature builder allocates for an IrFunctionType
              // param. Route it through the same two-slot expansion; without it the
              // env StdParamOp is never emitted and every trailing param shifts down
              // one register (reading the env instead of its own value).
              if (paramOp.Index < func.ParamTypes.Count && func.ParamTypes[paramOp.Index] is IrFunctionType) {
                LowerFunctionParam(paramOp.Index, paramOp.Name, paramOp.Result, newBlock, valueMap, varTypes, fnEnvVarNames, fnEnvDirectValues, paramFlatIndex);
                break;
              }
              if (refParamPtrVars.TryGetValue(paramOp.Name, out string? value)) {
                // Mutated param: receive reference pointer, dereference for initial local copy
                var ptrVal = new StdI64(IrContext.Current.NextStdId());
                int pFlatIdx = paramFlatIndex.GetValueOrDefault(paramOp.Index, paramOp.Index);
                newBlock.AddOp(new StdParamOp(pFlatIdx, paramOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, value, varTypes);
                // Dereference to get the initial value
                var loadRef = new StdLoadI64Op(value);
                newBlock.AddOp(loadRef);
                var origType = func.ParamTypes[paramOp.Index];
                var derefType = origType is IrRangedPrimitiveType rpt ? rpt.BaseType : origType;
                var deref = new StdLoadIndirectOp(loadRef.Result, 0, derefType);
                newBlock.AddOp(deref);
                valueMap[paramOp.Result] = deref.Result;
                EmitStore(newBlock, deref.Result, paramOp.Name, varTypes);
              } else {
                // Non-mutated param: existing behavior
                var stdResult = paramOp.ValueKind.CreateStdValue();
                int pFlatIdx = paramFlatIndex.GetValueOrDefault(paramOp.Index, paramOp.Index);
                newBlock.AddOp(new StdParamOp(pFlatIdx, paramOp.Name, stdResult));
                valueMap[paramOp.Result] = stdResult;
                EmitStore(newBlock, stdResult, paramOp.Name, varTypes);
              }
              break;
            }
            case MaxonStructParamOp structParamOp: {
              if (isStructInstanceMethod && structParamOp.Name == "self") {
                // Instance method self: receive heap pointer as parameter, store as "self"
                var selfPtrVal = new StdI64(IrContext.Current.NextStdId());
                newBlock.AddOp(new StdParamOp(0, "self", selfPtrVal));
                EmitStore(newBlock, selfPtrVal, "self", varTypes);
                valueMap[structParamOp.Result] = new StdHeapPtr(structParamOp.Result.Id, structParamOp.StructTypeName, "self");
              } else if (refParamPtrVars.TryGetValue(structParamOp.Name, out string? value)) {
                // Mutated struct param: receive pointer-to-heap-pointer, dereference for local copy
                int ptrFlatIdx = structParamPtrIndex[structParamOp.Index];
                var ptrVal = new StdI64(IrContext.Current.NextStdId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, structParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, value, varTypes);
                // Dereference: load pointer-to-slot, then load the heap pointer from the slot
                var loadRef = new StdLoadI64Op(value);
                newBlock.AddOp(loadRef);
                var deref = new StdLoadIndirectOp(loadRef.Result, 0, IrType.I64);
                newBlock.AddOp(deref);
                EmitStore(newBlock, (StdI64)deref.Result, structParamOp.Name, varTypes);
                valueMap[structParamOp.Result] = new StdHeapPtr(structParamOp.Result.Id, structParamOp.StructTypeName, structParamOp.Name);
              } else {
                // Non-self struct param: receive heap pointer, store under the param name
                int ptrFlatIdx = structParamPtrIndex[structParamOp.Index];
                var ptrVal = new StdHeapPtr(IrContext.Current.NextStdId(), structParamOp.StructTypeName, structParamOp.Name);
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, structParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, structParamOp.Name, varTypes);
                valueMap[structParamOp.Result] = ptrVal;
              }
              structParamNames.Add(structParamOp.Name);
              _structParamNames?.Add(structParamOp.Name);
              break;
            }
            case MaxonEnumLiteralOp enumLitOp: {
              if (enumLitOp.BackingKind == MaxonValueKind.Float) {
                var newOp = new StdConstF64Op(enumLitOp.FloatValue);
                newBlock.AddOp(newOp);
                valueMap[enumLitOp.Result] = newOp.Result;
              } else if (enumLitOp.BackingKind == MaxonValueKind.Integer) {
                var newOp = new StdConstI64Op(enumLitOp.IntValue);
                newBlock.AddOp(newOp);
                valueMap[enumLitOp.Result] = newOp.Result;
              } else {
                throw new InvalidOperationException($"Unsupported enum backing kind: {enumLitOp.BackingKind}");
              }
              break;
            }
            case MaxonEnumConstructOp enumConstructOp: {
              // Heap-allocate the enum: [tag:i64 @ 0, payload_0:i64 @ 8, payload_1:i64 @ 16, ...]
              var tempName = inlineTargets.TryGetValue(enumConstructOp.Result.Id, out var enumInlineTarget)
                ? enumInlineTarget
                : temps.CreateTemp("enum", enumConstructOp.Result.Id, enumConstructOp.EnumTypeName, OwnershipFlags.None);
              var enumTypeDef = (IrEnumType)module.TypeDefs[enumConstructOp.EnumTypeName];
              int maxPayload = GetMaxFlatPayloadSlots(enumTypeDef);
              int heapSize = UnionPayloadOffset(maxPayload);
              var enumPtr = EmitAlloc(newBlock, heapSize, enumConstructOp.EnumTypeName, scopeName: func.Name);
              EmitStore(newBlock, enumPtr, tempName, varTypes);

              var tagOp = new StdConstI64Op(enumConstructOp.TagValue);
              newBlock.AddOp(tagOp);
              newBlock.AddOp(new StdStoreIndirectOp(tagOp.Result, enumPtr, UnionFieldTag, IrType.I64));

              // Store associated values as payload slots via indirect stores
              int slotIdx = 0;
              for (int ai = 0; ai < enumConstructOp.Args.Count; ai++) {
                int byteOffset = UnionPayloadOffset(slotIdx);
                if (valueMap.TryGetValue(enumConstructOp.Args[ai], out var ecArgSv) && ecArgSv is StdHeapPtr ecArgHp) {
                  // Heap-pointer payload: store and incref — enum holds a reference
                  var childHeapPtr = (StdI64)EmitLoad(newBlock, ecArgHp.VarName!, varTypes);
                  newBlock.AddOp(new StdStoreIndirectOp(childHeapPtr, enumPtr, byteOffset, IrType.I64));
                  EmitIncrefValue(newBlock, childHeapPtr, scopeName: func.Name);
                  slotIdx++;
                } else {
                  // Scalar payload: store directly
                  var argStdVal = valueMap[enumConstructOp.Args[ai]];
                  newBlock.AddOp(new StdStoreIndirectOp(argStdVal, enumPtr, byteOffset, IrType.I64));
                  slotIdx++;
                }
              }
              // Zero-fill any unused payload slots
              for (int ai = slotIdx; ai < maxPayload; ai++) {
                var zeroOp = new StdConstI64Op(0);
                newBlock.AddOp(zeroOp);
                newBlock.AddOp(new StdStoreIndirectOp(zeroOp.Result, enumPtr, UnionPayloadOffset(ai), IrType.I64));
              }

              valueMap[enumConstructOp.Result] = new StdHeapPtr(enumConstructOp.Result.Id, enumConstructOp.EnumTypeName, tempName);

              // Orphan enum construct temps need incref + scope cleanup when they are not
              // consumed by a named variable (inlineTargets) or returned directly.
              // Mirrors the struct literal orphan pattern — without this, enum values
              // passed as borrowed function arguments leak when the callee doesn't consume them.
              if (!inlineTargets.ContainsKey(enumConstructOp.Result.Id)
                  && !structLitFieldValueIds.Contains(enumConstructOp.Result.Id)
                  && !structLitReturnIds.Contains(enumConstructOp.Result.Id)) {
                EmitIncrefValue(newBlock, enumPtr, scopeName: func.Name);
                varNameToStructType[tempName] = enumConstructOp.EnumTypeName;
                temps.MarkTempOrphan(tempName);
              }

              break;
            }
            case MaxonEnumTagOp enumTagOp: {
              if (valueMap.TryGetValue(enumTagOp.EnumValue, out var enumPrefixSv) && enumPrefixSv is StdHeapPtr enumPrefixHp) {
                // Associated-value union: the tag lives in the heap record
                valueMap[enumTagOp.Result] = EmitUnionTagLoad(enumPrefixHp, newBlock, varTypes);
              } else {
                // Simple enums without associated values pass the ordinal directly
                valueMap[enumTagOp.Result] = valueMap[enumTagOp.EnumValue];
              }
              break;
            }
            case MaxonEnumPayloadOp enumPayloadOp: {
              // Load a payload value from the heap-allocated enum via indirect load
              var enumVarName = ((StdHeapPtr)valueMap[enumPayloadOp.EnumValue]).VarName!;
              var heapPtr = (StdI64)EmitLoad(newBlock, enumVarName, varTypes);
              int byteOffset = UnionPayloadOffset(enumPayloadOp.PayloadIndex);

              if (enumPayloadOp.ResultKind == MaxonValueKind.Struct && enumPayloadOp.ResultStructTypeName != null) {
                // Struct-typed payload: load heap pointer from payload slot
                var tempStructName = temps.CreateTemp("enum_payload", enumPayloadOp.Result.Id, enumPayloadOp.ResultStructTypeName, OwnershipFlags.Borrowed);
                var loadOp = new StdLoadIndirectOp(heapPtr, byteOffset, IrType.I64);
                newBlock.AddOp(loadOp);
                EmitStore(newBlock, (StdI64)loadOp.Result, tempStructName, varTypes);
                valueMap[enumPayloadOp.Result] = new StdHeapPtr(enumPayloadOp.Result.Id, enumPayloadOp.ResultStructTypeName, tempStructName);
              } else if (enumPayloadOp.ResultKind == MaxonValueKind.Enum
                         && enumPayloadOp.ResultStructTypeName != null
                         && module.TypeDefs.TryGetValue(enumPayloadOp.ResultStructTypeName, out var payloadEnumDef)
                         && payloadEnumDef is IrEnumType payloadEnumType && payloadEnumType.HasAssociatedValues) {
                // Associated-value enum payload: load heap pointer (no unpacking needed)
                var tempName = temps.CreateTemp("enum_payload", enumPayloadOp.Result.Id, enumPayloadOp.ResultStructTypeName, OwnershipFlags.Borrowed);
                var loadOp = new StdLoadIndirectOp(heapPtr, byteOffset, IrType.I64);
                newBlock.AddOp(loadOp);
                EmitStore(newBlock, (StdI64)loadOp.Result, tempName, varTypes);
                valueMap[enumPayloadOp.Result] = new StdHeapPtr(enumPayloadOp.Result.Id, enumPayloadOp.ResultStructTypeName, tempName);
              } else {
                var loadOp = new StdLoadIndirectOp(heapPtr, byteOffset, IrType.I64);
                newBlock.AddOp(loadOp);
                if (enumPayloadOp.ResultKind == MaxonValueKind.Bool) {
                  // Bool payloads are stored as i64 in heap slots; convert to i1 via != 0
                  var zeroLit = new StdConstI64Op(0);
                  newBlock.AddOp(zeroLit);
                  var cmpOp = new StdCmpI64Op("ne", (StdI64)loadOp.Result, zeroLit.Result);
                  newBlock.AddOp(cmpOp);
                  valueMap[enumPayloadOp.Result] = cmpOp.Result;
                } else {
                  valueMap[enumPayloadOp.Result] = loadOp.Result;
                }
              }
              break;
            }
            case MaxonEnumParamOp enumParamOp: {
              // Check if this is an associated-value enum (passed as heap pointer)
              if (module.TypeDefs.TryGetValue(enumParamOp.EnumTypeName, out var epType)
                  && epType is IrEnumType epEnumType && epEnumType.HasAssociatedValues) {
                // Receive heap pointer — no unpacking needed, heap pointer IS the enum value
                int ptrFlatIdx = structParamPtrIndex[enumParamOp.Index];
                var ptrVal = new StdI64(IrContext.Current.NextStdId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, enumParamOp.Name, ptrVal));
                if (refParamPtrVars.TryGetValue(enumParamOp.Name, out string? value)) {
                  // Mutated assoc-value enum: receive pointer-to-heap-pointer
                  EmitStore(newBlock, ptrVal, value, varTypes);
                  var loadRef = new StdLoadI64Op(value);
                  newBlock.AddOp(loadRef);
                  var deref = new StdLoadIndirectOp(loadRef.Result, 0, IrType.I64);
                  newBlock.AddOp(deref);
                  EmitStore(newBlock, (StdI64)deref.Result, enumParamOp.Name, varTypes);
                } else {
                  EmitStore(newBlock, ptrVal, enumParamOp.Name, varTypes);
                }

                valueMap[enumParamOp.Result] = new StdHeapPtr(enumParamOp.Result.Id, enumParamOp.EnumTypeName, enumParamOp.Name);
                _structParamNames?.Add(enumParamOp.Name);
              } else if (refParamPtrVars.TryGetValue(enumParamOp.Name, out string? value)) {
                // Mutated simple enum: receive i64 pointer, dereference for local copy
                var ptrVal = new StdI64(IrContext.Current.NextStdId());
                int pFlatIdx = paramFlatIndex.GetValueOrDefault(enumParamOp.Index, enumParamOp.Index);
                newBlock.AddOp(new StdParamOp(pFlatIdx, enumParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, value, varTypes);
                var loadRef = new StdLoadI64Op(value);
                newBlock.AddOp(loadRef);
                var enumBackingType = enumParamOp.BackingKind == MaxonValueKind.Float ? IrType.F64 : IrType.I64;
                var deref = new StdLoadIndirectOp(loadRef.Result, 0, enumBackingType);
                newBlock.AddOp(deref);
                valueMap[enumParamOp.Result] = deref.Result;
                EmitStore(newBlock, deref.Result, enumParamOp.Name, varTypes);
              } else if (enumParamOp.BackingKind == MaxonValueKind.Float) {
                var stdResult = new StdF64(IrContext.Current.NextStdId());
                newBlock.AddOp(new StdParamOp(enumParamOp.Index, enumParamOp.Name, stdResult));
                valueMap[enumParamOp.Result] = stdResult;
                EmitStore(newBlock, stdResult, enumParamOp.Name, varTypes);
              } else if (enumParamOp.BackingKind == MaxonValueKind.Integer) {
                var stdResult = new StdI64(IrContext.Current.NextStdId());
                newBlock.AddOp(new StdParamOp(enumParamOp.Index, enumParamOp.Name, stdResult));
                valueMap[enumParamOp.Result] = stdResult;
                EmitStore(newBlock, stdResult, enumParamOp.Name, varTypes);
              } else {
                throw new InvalidOperationException($"Unsupported enum backing kind: {enumParamOp.BackingKind}");
              }
              break;
            }
            case MaxonEnumVarRefOp enumVarRef: {
              // Check if this is an associated-value enum (stored as flat vars)
              if (module.TypeDefs.TryGetValue(enumVarRef.EnumTypeName, out var evType)
                  && evType is IrEnumType evEnumType && evEnumType.HasAssociatedValues) {
                // Resolve the struct prefix: either from varNameToStructPrefix
                // (set by a prior field-access on self), the var as a local, or
                // load it from self's heap pointer when it names a union-typed
                // self field that hasn't been hoisted into a local yet. Without
                // this last branch a `return status.code()` body in an
                // instance method on `CollectedOutput` produces a StdLoadI64Op
                // against a variable name that was never stored to, and the
                // register allocator chokes on the dangling value.
                string resolvedPrefix;
                if (varNameToStructPrefix.TryGetValue(enumVarRef.VarName, out var existingPrefix)) {
                  resolvedPrefix = existingPrefix;
                } else if (!varTypes.ContainsKey(enumVarRef.VarName)
                    && IsSelfField(isStructInstanceMethod, selfStructType, enumVarRef.VarName)) {
                  var field = selfStructType!.GetField(enumVarRef.VarName)!;
                  var tempVarName = temps.CreateTemp("selfunion", enumVarRef.Result.Id, enumVarRef.EnumTypeName, OwnershipFlags.Borrowed);
                  var enumPtr = EmitStructFieldLoad(newBlock, "self", field.Offset, IrType.I64, varTypes);
                  EmitStore(newBlock, enumPtr, tempVarName, varTypes);
                  resolvedPrefix = tempVarName;
                  varNameToStructPrefix[enumVarRef.VarName] = tempVarName;
                } else {
                  resolvedPrefix = enumVarRef.VarName;
                }
                valueMap[enumVarRef.Result] = new StdHeapPtr(enumVarRef.Result.Id, enumVarRef.EnumTypeName, resolvedPrefix);
              } else if (IsSelfField(isStructInstanceMethod, selfStructType, enumVarRef.VarName)) {
                // Simple enum stored as a self field — load from self's heap pointer
                var field = selfStructType!.GetField(enumVarRef.VarName)!;
                var loaded = EmitStructFieldLoad(newBlock, "self", field.Offset, field.Type, varTypes);
                valueMap[enumVarRef.Result] = loaded;
              } else {
                var loaded = EmitLoad(newBlock, enumVarRef.VarName, varTypes);
                valueMap[enumVarRef.Result] = loaded;
              }
              break;
            }
            case MaxonEnumPayloadAssignOp payloadAssign: {
              // Write a value back to a specific payload slot via heap-pointer indirection
              var resolvedPrefix = varNameToStructPrefix.GetValueOrDefault(payloadAssign.EnumVarName, payloadAssign.EnumVarName);
              var enumHeapPtr = (StdI64)EmitLoad(newBlock, resolvedPrefix, varTypes);
              int byteOffset = UnionPayloadOffset(payloadAssign.PayloadIndex);

              if (valueMap.TryGetValue(payloadAssign.NewValue, out var newStructSrcSv) && newStructSrcSv is StdHeapPtr newStructSrcHp) {
                // Heap-pointer payload: decref old value, store new, incref new
                var oldPayloadLoad = new StdLoadIndirectOp(enumHeapPtr, byteOffset, IrType.I64);
                newBlock.AddOp(oldPayloadLoad);
                EmitDecrefValueIfNonnull(newBlock, (StdI64)oldPayloadLoad.Result, scopeName: func.Name);
                var childHeapPtr = (StdI64)EmitLoad(newBlock, newStructSrcHp.VarName!, varTypes);
                var enumHeapPtrReload = (StdI64)EmitLoad(newBlock, resolvedPrefix, varTypes);
                newBlock.AddOp(new StdStoreIndirectOp(childHeapPtr, enumHeapPtrReload, byteOffset, IrType.I64));
                EmitIncrefValue(newBlock, childHeapPtr, scopeName: func.Name);
              } else {
                var newStdVal = valueMap[payloadAssign.NewValue];
                newBlock.AddOp(new StdStoreIndirectOp(newStdVal, enumHeapPtr, byteOffset, IrType.I64));
              }
              break;
            }
            case MaxonEnumRawValueOp rawValueOp: {
              var enumStdVal = valueMap[rawValueOp.EnumValue];
              if (enumStdVal is StdHeapPtr rawHp) {
                // Associated-value union: the tag lives in the heap record
                valueMap[rawValueOp.Result] = EmitUnionTagLoad(rawHp, newBlock, varTypes);
              } else {
                // Simple enum: the backing value IS the raw value - just pass through
                valueMap[rawValueOp.Result] = enumStdVal;
              }
              break;
            }
            case MaxonErrorFlagToEnumOp errToEnumOp: {
              if (errToEnumOp.HasAssociatedValues) {
                // Associated-value error: the error flag IS the heap pointer.
                // The throw site (LowerThrow) transferred an owned reference (rc>=1)
                // via the error-return ABI. Mark this temp with OwnsRef so the
                // subsequent assign-to-binding consumes the existing ownership
                // instead of incref'ing again — otherwise the binding ends up
                // with one extra ref that nobody decrefs.
                var heapPtr = (StdI64)valueMap[errToEnumOp.ErrorFlag];
                var retVarName = temps.CreateTemp("error_enum", errToEnumOp.Result.Id, errToEnumOp.EnumTypeName, OwnershipFlags.OwnsRef);
                EmitStore(newBlock, heapPtr, retVarName, varTypes);
                // No unpacking — heap pointer IS the enum value
                valueMap[errToEnumOp.Result] = new StdHeapPtr(errToEnumOp.Result.Id, errToEnumOp.EnumTypeName, retVarName);
              } else {
                // Simple error enum: subtract 1 from error flag to recover ordinal
                var errorFlagVal = (StdI64)valueMap[errToEnumOp.ErrorFlag];
                var oneOp = new StdConstI64Op(1);
                newBlock.AddOp(oneOp);
                var subOp = new StdSubI64Op(errorFlagVal, oneOp.Result);
                newBlock.AddOp(subOp);
                valueMap[errToEnumOp.Result] = subOp.Result;
              }
              break;
            }
            case MaxonEnumStringRawValueOp strRawOp: {
              var enumType = (IrEnumType)module.TypeDefs[strRawOp.EnumTypeName];
              var ordinalValue = (StdI64)valueMap[strRawOp.EnumValue];
              var (buf, len) = EmitStringEnumToString(enumType, ordinalValue, newBlock, result);
              var isString = !strRawOp.IsChar;
              var rawValTypeName = isString ? "String" : "Character";
              var tempName = temps.CreateTemp("enum_rawval", strRawOp.Result.Id, rawValTypeName, OwnershipFlags.None);
              var strRawHp = EmitManagedStructFromBufLen(tempName, buf, len,
                isString, newBlock, varTypes,
                allocTag: rawValTypeName);
              valueMap[strRawOp.Result] = strRawHp;
              break;
            }
            case MaxonEnumFunctionRawValueOp fnRawOp: {
              var enumType = (IrEnumType)module.TypeDefs[fnRawOp.EnumTypeName];
              var stdValue = valueMap[fnRawOp.EnumValue];

              // Extract the case ordinal from the enum value. Function-backed
              // enums don't carry payload, so the std value is the ordinal i64.
              var ordinalValue = (StdI64)stdValue;

              // Build a select chain: starting from a null i64, for each case
              // compare-eq the enum ordinal against its declared ordinal and
              // pick that case's func-ref (reinterpreted as i64). The final
              // value is the function pointer for whichever ordinal matched.
              // No-match falls through to 0; the parser ensures only declared
              // enum values reach this op, so the path is unreachable in practice.
              var nullConst = new StdConstI64Op(0);
              newBlock.AddOp(nullConst);
              StdI64 currentFn = nullConst.Result;
              foreach (var enumCase in enumType.Cases) {
                if (enumCase.RawValue is not string funcName) continue;
                var ordConst = new StdConstI64Op(enumCase.Ordinal);
                newBlock.AddOp(ordConst);
                var cmpOp = new StdCmpI64Op("eq", ordinalValue, ordConst.Result);
                newBlock.AddOp(cmpOp);
                var refOp = new StdFuncRefOp(funcName);
                newBlock.AddOp(refOp);
                var ptrAsI64 = new StdPtrToI64Op(refOp.Result);
                newBlock.AddOp(ptrAsI64);
                var selectOp = new StdSelectI64Op(cmpOp.Result, ptrAsI64.Result, currentFn);
                newBlock.AddOp(selectOp);
                currentFn = selectOp.Result;
              }
              valueMap[fnRawOp.Result] = currentFn;
              break;
            }
            case MaxonEnumStructRawValueOp structRawOp: {
              var enumType = (IrEnumType)module.TypeDefs[structRawOp.EnumTypeName];
              var structType = (IrStructType)module.TypeDefs[structRawOp.StructTypeName];
              var ordinalValue = EmitEnumOrdinalOperand(newBlock, valueMap[structRawOp.EnumValue], varTypes);

              // Allocate struct on the heap
              var tempName = temps.CreateTemp("enum_rawval", structRawOp.Result.Id, structRawOp.StructTypeName, OwnershipFlags.None);
              var structPtr = EmitAlloc(newBlock, structType.SizeInBytes, structRawOp.StructTypeName, scopeName: func.Name);
              EmitStore(newBlock, structPtr, tempName, varTypes);

              // For each struct field, emit a select chain mapping ordinal -> field value
              EmitStructRawValueFields(newBlock, structType, enumType, ordinalValue,
                tempName, "", temps, varTypes, func.Name, module.TypeDefs);

              valueMap[structRawOp.Result] = new StdHeapPtr(structRawOp.Result.Id, structRawOp.StructTypeName, tempName);
              break;
            }
            case MaxonEnumStructRawFieldOp rawFieldOp: {
              // `e.rawValue.field` — the field's ordinal→constant select chain and nothing else.
              // No allocation: the raw values are compile-time constants, and the other fields'
              // chains are not emitted at all because nobody asked for them.
              var enumType = (IrEnumType)module.TypeDefs[rawFieldOp.EnumTypeName];
              var ordinalValue = EmitEnumOrdinalOperand(newBlock, valueMap[rawFieldOp.EnumValue], varTypes);
              var selected = EmitStructRawValueFieldSelect(newBlock, enumType, ordinalValue, rawFieldOp.FieldName);

              // The chain yields the field's constant in an i64. A bool leaf is the 0/1 in that
              // register and an enum leaf is its ordinal — the same SSA value, differently typed.
              // The parser only fuses these three kinds (TryEmitFusedStructRawField); anything
              // else reaching here means the two disagree about what a leaf can be.
              valueMap[rawFieldOp.Result] = rawFieldOp.ResultKind switch {
                MaxonValueKind.Bool => new StdBool(selected.Id),
                MaxonValueKind.Integer or MaxonValueKind.Enum => selected,
                _ => throw new InvalidOperationException(
                  $"enum_struct_rawfield: field '{rawFieldOp.FieldName}' of '{rawFieldOp.StructTypeName}' "
                  + $"has kind {rawFieldOp.ResultKind}, which the parser should not have fused")
              };
              break;
            }
            case MaxonEnumOrdinalOp ordinalOp: {
              var enumType = (IrEnumType)module.TypeDefs[ordinalOp.EnumTypeName];
              var stdValue = valueMap[ordinalOp.EnumValue];
              StdI64 ordinalValue;
              if (stdValue is StdHeapPtr ordHp) {
                // Associated-value enum: load tag from heap, then convert to ordinal
                var heapPtr = (StdI64)EmitLoad(newBlock, ordHp.VarName!, varTypes);
                var tagLoad = new StdLoadIndirectOp(heapPtr, 0, IrType.I64);
                newBlock.AddOp(tagLoad);
                if (enumType.BackingType == IrType.I64) {
                  ordinalValue = EmitIntEnumToPositionIndex(enumType, (StdI64)tagLoad.Result, newBlock);
                } else {
                  // Tag is the ordinal for non-int-backed or auto-incremented enums
                  ordinalValue = (StdI64)tagLoad.Result;
                }
              } else if (enumType.BackingType == IrType.I64) {
                ordinalValue = EmitIntEnumToPositionIndex(enumType, (StdI64)stdValue, newBlock);
              } else if (enumType.BackingType == IrType.F64) {
                ordinalValue = EmitFloatEnumToPositionIndex(enumType, (StdF64)stdValue, newBlock);
              } else {
                // Simple enums (no backing type) and string/char-backed enums store ordinals directly
                ordinalValue = (StdI64)stdValue;
              }
              valueMap[ordinalOp.Result] = ordinalValue;
              break;
            }
            case MaxonEnumNameOp enumNameOp: {
              var enumType = (IrEnumType)module.TypeDefs[enumNameOp.EnumTypeName];
              var stdValue = valueMap[enumNameOp.EnumValue];
              StdI64 ordinalValue;
              if (stdValue is StdHeapPtr nameHp) {
                // Associated-value enum: load tag from heap, then convert to ordinal for name lookup
                var heapPtr = (StdI64)EmitLoad(newBlock, nameHp.VarName!, varTypes);
                var tagLoad = new StdLoadIndirectOp(heapPtr, 0, IrType.I64);
                newBlock.AddOp(tagLoad);
                if (enumType.BackingType == IrType.I64) {
                  ordinalValue = EmitIntEnumToOrdinal(enumType, (StdI64)tagLoad.Result, newBlock);
                } else {
                  ordinalValue = (StdI64)tagLoad.Result;
                }
              } else if (enumType.BackingType == IrType.I64) {
                ordinalValue = EmitIntEnumToOrdinal(enumType, (StdI64)stdValue, newBlock);
              } else if (enumType.BackingType == IrType.F64) {
                ordinalValue = EmitFloatEnumToOrdinal(enumType, (StdF64)stdValue, newBlock);
              } else {
                // Simple enums (no backing type) and string/char-backed enums store ordinals
                ordinalValue = (StdI64)stdValue;
              }
              var (nameBuf, nameLen) = EmitEnumNameLookup(enumType, ordinalValue, newBlock, result);
              var tempName = temps.CreateTemp("enum_name", enumNameOp.Result.Id, "String", OwnershipFlags.None);
              var enumNameHp = EmitManagedStructFromBufLen(tempName, nameBuf, nameLen,
                true, newBlock, varTypes,
                allocTag: "String");
              valueMap[enumNameOp.Result] = enumNameHp;
              break;
            }
            case MaxonLiteralOp litOp: {
              literalMap[litOp.Result] = litOp;
              switch (litOp.ValueKind) {
                case MaxonValueKind.Integer: {
                  var newOp = new StdConstI64Op(litOp.IntValue);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                case MaxonValueKind.Float: {
                  var newOp = new StdConstF64Op(litOp.FloatValue);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                case MaxonValueKind.Float32: {
                  var newOp = new StdConstF32Op((float)litOp.FloatValue);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                case MaxonValueKind.Bool: {
                  var newOp = new StdConstI1Op(litOp.BoolValue);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                case MaxonValueKind.Byte: {
                  var newOp = new StdConstI64Op(litOp.IntValue & 0xFF);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                case MaxonValueKind.Short: {
                  var newOp = new StdConstI64Op(litOp.IntValue & 0xFFFF);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                default:
                  throw new InvalidOperationException($"Unsupported literal kind: {litOp.ValueKind}");
              }
              break;
            }
            case MaxonStructLiteralOp structLitOp: {
              if (module.TypeDefs[structLitOp.TypeName] is not IrStructType structType)
                throw new InvalidOperationException($"StructLiteral type '{structLitOp.TypeName}' resolved to {module.TypeDefs[structLitOp.TypeName].GetType().Name} in func '{func.Name}'");

              // Suppressed inner __ManagedMemory struct literal of a fused Array/Vector literal /
              // create() (see the absorb pre-scan). Its wrapper writes its fields inline and owns
              // the buffer, so this record must never allocate.
              if (suppressedStructLitIds.Contains(structLitOp.Result.Id)) break;

              // Envelope collapse: a fused String/Character/Array IS its __ManagedMemory. Construction
              // does not allocate a nested record — it writes the managed fields inline. Intercept
              // before the stack/heap generic paths, which would wrongly store a POINTER at offset 0.
              //
              //  - An absorbed Array/Vector literal or create() (`Array{managed: M}`, M a fresh
              //    __ManagedMemory struct literal) falls through to the HEAP path below, which now
              //    treats a fused array as buffer-direct and stores M's fields inline into self.
              //  - Everything else — String/Character, and `Array{managed: X}` where X is a real
              //    source — is a zero-copy slice VIEW (fresh record, capacity=-1, parent=X, incref X).
              bool isFusedArrayLiteral = false;
              MaxonStructLiteralOp? absorbedInnerManaged = null;
              if (structType.ConformsToBuiltinManagedWrapper) {
                if (structType.ConformingInterfaces.Contains("BuiltinArrayLiteral")
                    && absorbedManagedLit.TryGetValue(structLitOp.Result.Id, out var inner)) {
                  isFusedArrayLiteral = true;
                  absorbedInnerManaged = inner;
                } else {
                  LowerFusedWrapperConstruction(structLitOp,
                    structType.ConformingInterfaces.Contains("BuiltinStringLiteral"),
                    structType.ConformingInterfaces.Contains("BuiltinArrayLiteral"),
                    newBlock, valueMap, varTypes, temps, inlineTargets, func.Name);
                  break;
                }
              }

              // Static literals: a CONSTANT array literal proved never-mutated becomes a shared
              // immortal record (zero allocations), the array analogue of a static string literal.
              // Its elements are already compile-time constants in rdata; the escape analysis gates
              // it — a mutated array is absent from the eligible set and falls through to the heap
              // path below (which allocates and, for a var, permits push/set). Managed-element arrays
              // are never ConstantArrayLiterals, so this only fires for primitive-element arrays.
              // `IsMutable` marks a constant array bound to a MUTABLE GLOBAL. The escape analysis
              // tracks per-function variable flow, so it cannot see a global mutated in another
              // function; excluding mutable globals keeps the static path sound (a mutable global
              // array stays heap and COWs on first write). Local mutations are still caught by the
              // escape analysis, and an immutable (`let`) binding cannot be mutated at all.
              if (module.ConstantArrayLiterals.TryGetValue(structLitOp.Result.Id, out var staticArrInfo)
                  && !staticArrInfo.IsMutable
                  && IsStaticEligibleLiteral(structLitOp.Result.Id)) {
                inlineTargets.TryGetValue(structLitOp.Result.Id, out var staticArrTarget);
                valueMap[structLitOp.Result] = EmitStaticArrayLiteral(
                  staticArrInfo, structLitOp.TypeName, structLitOp.Result.Id,
                  newBlock, varTypes, result, temps, staticArrTarget);
                break;
              }

              // 3c: a MANAGED-ELEMENT array literal (`["a","b"]`) whose elements are ALL static
              // string/char literals, and which is never mutated, becomes a shared immortal record
              // too — its inline pointer table references the elements' own static records (filled at
              // __module_init). Not a ConstantArrayLiteral (managed pointers aren't compile-time
              // constants), so eligibility is the escape analysis plus every element being static.
              if (structLitOp.ArrayLiteralTag != null && structLitOp.ArrayLiteralCount > 0
                  && !structLitOp.IsBitPacked
                  && !module.ConstantArrayLiterals.ContainsKey(structLitOp.Result.Id)
                  && IsStaticEligibleLiteral(structLitOp.Result.Id)) {
                var elementLabels = CollectStaticElementLabels(structLitOp, block);
                if (elementLabels != null) {
                  inlineTargets.TryGetValue(structLitOp.Result.Id, out var smArrTarget);
                  valueMap[structLitOp.Result] = EmitStaticManagedArrayLiteral(
                    elementLabels, structLitOp.TypeName, structLitOp.Result.Id,
                    newBlock, varTypes, result, temps, smArrTarget);
                  break;
                }
              }

              // Byte-fusion: a small OWNED array/vector literal stores its elements INLINE in the
              // record's own allocation (buffer = self + recordSize, parent_ptr = MmParentInline)
              // instead of taking a second heap buffer. Only when the buffer is writable (not an
              // rdata constant or stack scratch) and the element bytes fit MmInlineCapBytes — arrays
              // grow geometrically, so a larger one keeps an external buffer and the first push
              // detaches. Decided here (compile-time) so the record is allocated at the fused size;
              // wired into the buffer set-up below (arrayInlineBytes > 0 == fuse).
              int arrayInlineBytes = 0;
              if (isFusedArrayLiteral && structLitOp.ArrayLiteralTag != null
                  && !module.ConstantArrayLiterals.ContainsKey(structLitOp.Result.Id)
                  && !structLitOp.SkipZeroInit) {
                bool fusedBitPacked = structLitOp.IsBitPacked || (absorbedInnerManaged?.IsBitPacked ?? false);
                int fusedCount = structLitOp.ArrayLiteralCount;
                int totalInlineBytes;
                if (fusedBitPacked) {
                  totalInlineBytes = (fusedCount + 7) / 8;
                } else {
                  long elemBytes = 0;
                  foreach (var (mfName, mfVal) in absorbedInnerManaged!.FieldValues)
                    if (mfName == "element_size" && intLiteralByResult.TryGetValue(mfVal.Id, out var esz)) elemBytes = esz;
                  totalInlineBytes = (int)(fusedCount * elemBytes);
                }
                if (totalInlineBytes > 0 && totalInlineBytes <= MmInlineCapBytes)
                  arrayInlineBytes = totalInlineBytes;
              }

              // Stack allocation path: decompose struct into named field variables.
              // Fused array literals are refcounted heap records — never stack-eligible.
              if (!isFusedArrayLiteral && module.StackEligibleStructs.Contains(structLitOp.Result.Id)) {
                // Stack-allocate: reserve contiguous stack space and use a pointer,
                // identical to heap structs but without mm_alloc/refcounting.
                // Find the target variable name from the immediately following declaration assign
                // so we store the pointer directly as 't' instead of an intermediate '__stack_N'.
                string? tempName2 = null;
                var blockOps2 = block.Operations;
                for (int si = 0; si < blockOps2.Count - 1; si++) {
                  if (ReferenceEquals(blockOps2[si], structLitOp)
                      && blockOps2[si + 1] is MaxonAssignOp sa && sa.IsDeclaration && sa.Value.Id == structLitOp.Result.Id) {
                    tempName2 = sa.VarName;
                    break;
                  }
                }
                tempName2 ??= temps.CreateTemp("stack", structLitOp.Result.Id, structLitOp.TypeName, OwnershipFlags.None);
                var stackTag = $"__stk_{tempName2}";
                // QWORDS, not fields — StdBulkZeroOp reserves 8-byte slots, and the two agree only
                // while every field is one qword wide (see StackSlotCount).
                var slotCount = StackSlotCount(structType);

                // Reserve stack space (skip zero-init — fields are immediately overwritten)
                newBlock.AddOp(new StdBulkZeroOp(stackTag, slotCount, zeroInit: false));

                // Store each field directly to the BulkZero slots (no pointer indirection).
                // Fields are stored in reverse slot order so that the LEA (which returns the
                // lowest stack address) produces a pointer where [ptr+0] = field 0.
                foreach (var (fieldName, fieldVal) in structLitOp.FieldValues) {
                  var field = structType.GetField(fieldName)!;
                  var mappedVal = valueMap[fieldVal];
                  EmitStore(newBlock, mappedVal, StackSlotName(stackTag, structType, field.Offset), varTypes);
                }

                // No LEA/pointer store here — the pointer is emitted lazily
                // in FlattenCallArgs only when the struct is actually passed to a function.

                valueMap[structLitOp.Result] = new StdStackPtr(structLitOp.Result.Id, structLitOp.TypeName, tempName2);
                stackAllocatedVars.Add(tempName2);
                stackVarTags[tempName2] = stackTag;
                break;
              }

              // Heap-allocate the struct and store each field via indirect stores.
              // If this literal is immediately assigned, use the target variable name for the heap pointer.
              var tempName = inlineTargets.TryGetValue(structLitOp.Result.Id, out var inlineTarget)
                ? inlineTarget
                : temps.CreateTemp("struct", structLitOp.Result.Id, structLitOp.TypeName, OwnershipFlags.None);

              // Allocate memory for the struct on the heap. A byte-fused array literal reserves its
              // inline element bytes right after the record fields in the SAME allocation.
              var recordAllocSize = structType.SizeInBytes + arrayInlineBytes;
              var structPtr = EmitAlloc(newBlock, recordAllocSize, structLitOp.TypeName, scopeName: func.Name);
              EmitStore(newBlock, structPtr, tempName, varTypes);

              foreach (var (fieldName, fieldVal) in structLitOp.FieldValues) {
                // Fused Array/Vector: the inline `managed` __ManagedMemory occupies offsets 0..32 of
                // self, not an 8-byte pointer. Write the absorbed inner struct literal's five fields
                // (buffer/length/capacity/element_size/parent_ptr) directly into self; the buffer and
                // capacity are then overwritten by the buffer set-up below (for a tagged literal).
                if (isFusedArrayLiteral && fieldName == "managed") {
                  foreach (var (mfName, mfVal) in absorbedInnerManaged!.FieldValues)
                    EmitStructFieldStore(newBlock, valueMap[mfVal], tempName, ManagedFieldOffsetByName(mfName), IrType.I64, varTypes);
                  continue;
                }
                var field = structType.GetField(fieldName)!;
                if (valueMap.TryGetValue(fieldVal, out var nestedStructNameSv) && nestedStructNameSv is StdHeapPtr nestedStructNameHp) {
                  // Struct or associated-value enum field: both are heap pointers now
                  var nestedHeapPtr = EmitLoad(newBlock, nestedStructNameHp.VarName!, varTypes);
                  EmitStructFieldStore(newBlock, nestedHeapPtr, tempName, field.Offset, IrType.I64, varTypes);
                  // Incref — the struct holds a reference to this nested value
                  EmitIncrefValue(newBlock, (StdI64)nestedHeapPtr, scopeName: func.Name);
                } else {
                  var mappedVal = valueMap[fieldVal];
                  // Heap-allocated fields (structs, associated-value unions) are stored as
                  // i64 heap pointers regardless of the source-level field type.
                  var litFieldStoreType = field.Type.IsHeapAllocated ? IrType.I64 : field.Type;
                  EmitStructFieldStore(newBlock, mappedVal, tempName, field.Offset, litFieldStoreType, varTypes);
                  // If the field is heap-allocated but the value wasn't tracked as a StdHeapPtr
                  // (e.g. runtime call results), we still need to incref
                  if (field.Type.IsHeapAllocated && mappedVal is StdI64 stdI64)
                    EmitIncrefValue(newBlock, stdI64, scopeName: func.Name);
                }
              }

              // A fused Array/Vector literal has the same __ManagedMemory layout as a bare
              // __ManagedMemory (buffer@0 … parent_ptr@32), so the buffer/element_size machinery
              // below operates on `self` directly rather than on a nested `managed` pointer.
              bool managedMemoryLayout = TypeAliasInfo.IsManagedMemoryType(structLitOp.TypeName, module.TypeAliasSources)
                || isFusedArrayLiteral;
              // A bit-packed bool buffer uses element_size==0 as its sentinel. For an absorbed array
              // the flag lives on the inner managed literal; for a bare literal it is on this op.
              bool isBitPackedLayout = structLitOp.IsBitPacked || (absorbedInnerManaged?.IsBitPacked ?? false);

              // Runtime guard: panic if __ManagedMemory is created with element_size == 0
              // (skip for bit-packed bools where element_size = 0 is the valid sentinel)
              if (managedMemoryLayout && !isBitPackedLayout) {
                var elemSizeCheck = (StdI64)EmitStructFieldLoad(newBlock, tempName, ManagedFieldElementSize, IrType.I64, varTypes);
                var zeroConst = new StdConstI64Op(0);
                newBlock.AddOp(zeroConst);
                // bounds_check(0, element_size, msg) panics if 0 >= element_size, i.e. element_size == 0
                EmitBoundsCheck(newBlock, zeroConst.Result, elemSizeCheck, "__mm_panic_element_size_zero");
              }

              // For array/vector literals, patch the buffer field to point to element data
              if (structLitOp.ArrayLiteralTag != null) {
                // Access buffer field through heap pointer indirection.
                // For __ManagedMemory, buffer is directly a field. For outer structs (Array, Vector),
                // the managed field is a nested struct whose heap pointer contains the buffer field.
                StdI64 rdataPtr;
                if (module.ConstantArrayLiterals.TryGetValue(structLitOp.Result.Id, out var constArrayInfo)) {
                  byte[] rdataBytes;
                  int rdataAlignment;
                  if (constArrayInfo.IsBitPacked) {
                    // Bit-packed bools: pack values as individual bits
                    int byteCount = (constArrayInfo.Values.Length + 7) / 8;
                    rdataBytes = new byte[byteCount];
                    for (int i = 0; i < constArrayInfo.Values.Length; i++) {
                      if (constArrayInfo.Values[i] != 0)
                        rdataBytes[i >> 3] |= (byte)(1 << (i & 7));
                    }
                    rdataAlignment = 1;
                  } else {
                    int elemSize = constArrayInfo.ElementSize;
                    rdataBytes = new byte[constArrayInfo.Values.Length * elemSize];
                    for (int i = 0; i < constArrayInfo.Values.Length; i++) {
                      switch (elemSize) {
                        case 1:
                          rdataBytes[i] = (byte)constArrayInfo.Values[i];
                          break;
                        case 2:
                          BitConverter.GetBytes((ushort)constArrayInfo.Values[i]).CopyTo(rdataBytes, i * elemSize);
                          break;
                        case 4:
                          BitConverter.GetBytes((int)constArrayInfo.Values[i]).CopyTo(rdataBytes, i * elemSize);
                          break;
                        case 8:
                          BitConverter.GetBytes(constArrayInfo.Values[i]).CopyTo(rdataBytes, i * elemSize);
                          break;
                        default:
                          throw new InvalidOperationException($"Unsupported constant array element size: {elemSize}");
                      }
                    }
                    rdataAlignment = elemSize;
                  }
                  result.RdataEntries.Add((constArrayInfo.RdataLabel, rdataBytes, rdataAlignment));
                  var leaRdataOp = new StdLeaRdataOp(constArrayInfo.RdataLabel);
                  newBlock.AddOp(leaRdataOp);
                  var rdataPtrOp = new StdPtrToI64Op(leaRdataOp.Result);
                  newBlock.AddOp(rdataPtrOp);
                  rdataPtr = rdataPtrOp.Result;
                } else if (structLitOp.SkipZeroInit) {
                  // Large scratch buffers with skipZeroInit stay on the stack — they are
                  // temporary within a single function and not stored to heap objects.
                  newBlock.AddOp(new StdBulkZeroOp(structLitOp.ArrayLiteralTag, structLitOp.ArrayLiteralCount, zeroInit: false));
                  var leaOp = new StdLeaOp(structLitOp.ArrayLiteralTag);
                  newBlock.AddOp(leaOp);
                  var castOp = new StdPtrToI64Op(leaOp.Result);
                  newBlock.AddOp(castOp);
                  rdataPtr = castOp.Result;
                } else {
                  // Heap-allocate the buffer. Stack buffers are unsafe because
                  // __gt_morestack can relocate the stack, making embedded stack pointers stale.
                  var leaOp = new StdLeaOp(structLitOp.ArrayLiteralTag);
                  newBlock.AddOp(leaOp);
                  var stackPtr = new StdPtrToI64Op(leaOp.Result);
                  newBlock.AddOp(stackPtr);

                  // Load element_size from the managed memory struct that was already lowered
                  StdI64 elemSizeVal;
                  if (managedMemoryLayout) {
                    elemSizeVal = (StdI64)EmitStructFieldLoad(newBlock, tempName, ManagedFieldElementSize, IrType.I64, varTypes);
                  } else {
                    var managedFieldForSize = structType.GetField("managed")!;
                    var managedPtrForSize = (StdI64)EmitStructFieldLoad(newBlock, tempName, managedFieldForSize.Offset, IrType.I64, varTypes);
                    var loadElemSize = new StdLoadIndirectOp(managedPtrForSize, ManagedFieldElementSize, IrType.I64);
                    newBlock.AddOp(loadElemSize);
                    elemSizeVal = (StdI64)loadElemSize.Result;
                  }

                  var countOp = new StdConstI64Op(structLitOp.ArrayLiteralCount);
                  newBlock.AddOp(countOp);
                  StdI64 totalSize;
                  StdI64 copySize;
                  if (isBitPackedLayout) {
                    // Bit-packed bools: byte size = (count + 7) >> 3
                    totalSize = ComputeBitPackedByteSize(newBlock, countOp.Result);
                    // Stack still has 1 byte per element, so copy count bytes from stack
                    copySize = countOp.Result;
                  } else {
                    var mulOp = new StdMulI64Op(countOp.Result, elemSizeVal);
                    newBlock.AddOp(mulOp);
                    totalSize = mulOp.Result;
                    copySize = mulOp.Result;
                  }

                  // Byte-fusion: a small owned array puts its elements INLINE (buffer = self +
                  // recordSize) in the record's own allocation; otherwise take a separate raw buffer.
                  StdI64 heapBuf = arrayInlineBytes > 0
                    ? EmitInlineBufferPtr(newBlock, tempName, structType.SizeInBytes, varTypes)
                    : EmitRawAlloc(newBlock, totalSize, label: "cow.buf", scopeName: _currentFuncName);
                  if (isBitPackedLayout) {
                    // Pack bool values from stack (byte-per-element) into bit-packed heap buffer.
                    // The inline region comes from mm_alloc (zeroed); a raw buffer needs no pre-zero
                    // because every bit is set below. Since count is known at compile time, unroll.
                    for (int bi = 0; bi < structLitOp.ArrayLiteralCount; bi++) {
                      var elemVar = $"{structLitOp.ArrayLiteralTag}.{bi}";
                      var elemVal = (StdI64)EmitLoad(newBlock, elemVar, varTypes);
                      var bitIndex = new StdConstI64Op(bi);
                      newBlock.AddOp(bitIndex);
                      EmitBitSet(newBlock, heapBuf, bitIndex.Result, elemVal);
                    }
                  } else {
                    var copyResult = new StdI64(IrContext.Current.NextStdId());
                    newBlock.AddOp(new StdCallRuntimeOp("maxon_memcpy", [heapBuf, stackPtr.Result, copySize], copyResult));
                  }

                  // Incref struct elements — the array holds references to them
                  var firstElemVar = $"{structLitOp.ArrayLiteralTag}.0";
                  if (varNameToStructType.ContainsKey(firstElemVar)) {
                    for (int ei = 0; ei < structLitOp.ArrayLiteralCount; ei++) {
                      var elemVar = $"{structLitOp.ArrayLiteralTag}.{ei}";
                      EmitIncrefValue(newBlock, (StdI64)EmitLoad(newBlock, elemVar, varTypes), scopeName: func.Name);
                    }
                  }

                  rdataPtr = heapBuf;
                }

                // Writable (non-constant) buffers get capacity=count so COW check passes.
                // Constant (rdata) and skipZeroInit (stack scratch) buffers get capacity=-2
                // (rdata sentinel: read-only for COW, skipped by destructor to avoid freeing non-heap memory).
                bool isConstantBuffer = module.ConstantArrayLiterals.ContainsKey(structLitOp.Result.Id);
                // skipZeroInit buffers are stack-allocated (not heap) — capacity must be -2 so the destructor
                // does not call mm_raw_free on a stack address (which would corrupt the process heap).
                bool bufferIsWritable = !isConstantBuffer && !structLitOp.SkipZeroInit;
                if (managedMemoryLayout) {
                  // buffer is directly on this record at offset 0 (bare __ManagedMemory or fused Array)
                  EmitStructFieldStore(newBlock, rdataPtr, tempName, ManagedFieldBuffer, IrType.I64, varTypes);
                  var capOp = new StdConstI64Op(bufferIsWritable ? structLitOp.ArrayLiteralCount : MmCapacityRdata);
                  newBlock.AddOp(capOp);
                  EmitStructFieldStore(newBlock, capOp.Result, tempName, ManagedFieldCapacity, IrType.I64, varTypes);
                  // A byte-fused array marks its inline buffer so the destructor skips the raw free
                  // and the first grow detaches (the absorbed managed literal wrote parent_ptr = 0).
                  if (arrayInlineBytes > 0) {
                    var inlineParentOp = new StdConstI64Op(MmParentInline);
                    newBlock.AddOp(inlineParentOp);
                    EmitStructFieldStore(newBlock, inlineParentOp.Result, tempName, ManagedFieldParentPtr, IrType.I64, varTypes);
                  }
                } else {
                  // Outer struct (Array, Vector): load the managed field's heap pointer, then store buffer on it
                  var managedField = structType.GetField("managed")!;
                  var managedHeapPtr = (StdI64)EmitStructFieldLoad(newBlock, tempName, managedField.Offset, IrType.I64, varTypes);
                  // Store buffer on the __ManagedMemory heap object
                  var managedType = (IrStructType)managedField.Type;
                  var bufferField = managedType.GetField("buffer")!;
                  newBlock.AddOp(new StdStoreIndirectOp(rdataPtr, managedHeapPtr, bufferField.Offset, IrType.I64));
                  var capOp = new StdConstI64Op(bufferIsWritable ? structLitOp.ArrayLiteralCount : MmCapacityRdata);
                  newBlock.AddOp(capOp);
                  var capField = managedType.GetField("capacity")!;
                  newBlock.AddOp(new StdStoreIndirectOp(capOp.Result, managedHeapPtr, capField.Offset, IrType.I64));
                }
              }

              valueMap[structLitOp.Result] = new StdHeapPtr(structLitOp.Result.Id, structLitOp.TypeName, tempName);

              // Orphan struct literal temps (__struct_N) need incref + scope cleanup when they
              // are not consumed by another construct that manages their lifetime:
              //  - inlineTargets: inlined into a named variable (parser handles cleanup)
              //  - structLitFieldValueIds: nested field value (parent field store handles incref)
              //  - structLitReturnIds: returned directly (LowerReturn handles incref + transfer)
              if (!inlineTargets.ContainsKey(structLitOp.Result.Id)
                  && !structLitFieldValueIds.Contains(structLitOp.Result.Id)
                  && !structLitReturnIds.Contains(structLitOp.Result.Id)) {
                // Orphan: not consumed by a named var. Incref to establish scope reference,
                // scope_end's mm_decref will release it.
                EmitIncrefValue(newBlock, structPtr, scopeName: func.Name);
                varNameToStructType[tempName] = structLitOp.TypeName;
                temps.MarkTempOrphan(tempName);
              }

              break;
            }
            case MaxonAssignOp assignOp: {
              // Associated-value enums now use heap pointers (like structs) and fall
              // through to the struct assignment path below.
              // Check valueMap for StdHeapPtr as the authoritative source: managed list ops like
              // managed_list_node_value report MaxonStruct result type even for primitives,
              // so ValueKind alone is not reliable.
              if (valueMap.TryGetValue(assignOp.Value, out var assignSv) && assignSv is StdHeapPtr assignHp) {
                // Struct assignment: copy the heap pointer (alias, not deep copy)
                // Copy-by-default cloning is handled at the parser level via clone() calls.
                var srcName = assignHp.VarName
                  ?? throw new InvalidOperationException($"MaxonAssignOp: StdHeapPtr missing VarName for value {assignOp.Value.Id} for assign to '{assignOp.VarName}' (ValueKind={assignOp.ValueKind}) in func {func.Name}");
                var dstName = assignOp.VarName;
                var structTypeName = assignHp.TypeName
                  ?? (assignOp.Value is MaxonStruct ms
                    ? ms.TypeName
                    : throw new InvalidOperationException($"No struct type info for value #{assignOp.Value.Id} in assign to '{assignOp.VarName}'"));
                // Stack pointer: no refcounting needed (no refcount header on stack memory).
                // Skip incref/decref; decref old value only if dst was heap-allocated.
                if (assignHp is StdStackPtr || stackAllocatedVars.Contains(srcName)) {
                  if (srcName != dstName) {
                    // If dst previously held a heap pointer, decref it before overwriting
                    if (varTypes.ContainsKey(dstName) && !stackAllocatedVars.Contains(dstName)) {
                      var oldHeapPtr = (StdI64)EmitLoad(newBlock, dstName, varTypes);
                      EmitDecrefValueIfNonnull(newBlock, oldHeapPtr, scopeName: func.Name);
                    }
                  }
                  stackAllocatedVars.Add(dstName);
                  // Propagate stack tag so aliases resolve to the same BulkZero slots
                  if (stackVarTags.TryGetValue(srcName, out var srcTag))
                    stackVarTags[dstName] = srcTag;
                } else {
                  if (srcName != dstName) {
                    // Decref old value before overwriting. Guarded by varTypes
                    // so the first store skips the decref (no previous value);
                    // reassignments and loop-header re-stores release the old ref.
                    //
                    // ⭐ A DECLARATION NEVER RELEASES A PREVIOUS VALUE, and the test may not
                    // consult `varNameToStructType` to decide that. That map is populated in
                    // BLOCK-WALK ORDER with no model of control flow, so for two MUTUALLY
                    // EXCLUSIVE paths that declare the same name it reports "seen already" on
                    // the second one — which at RUNTIME is still a first store into an
                    // uninitialized slot. `EmitDecrefValueIfNonnull` guards only NULL, not
                    // garbage, so the emitted decref released whatever stale pointer the slot
                    // happened to hold.
                    //
                    // MEASURED on `typealias Bad = int(i8.min to i32.max)` compiled by shv2 on
                    // arm64-macOS: `reportParseError`'s `match` binds a payload to `got` in many
                    // arms; the FIRST arm was correct and every later arm decref'd a live
                    // `KeywordInfo.helpText` String belonging to the global keyword map, which
                    // then died in `__maxon_global_cleanup` with `mm_decref: refcount underflow
                    // (already zero)`. It presented as an unowned "growing a union is a landmine"
                    // defect because what the stale slot holds depends on frame layout: on x64 it
                    // read 0 and the null guard hid it, and `--mm-trace` hid it too.
                    //
                    // A declaration's own value is released at SCOPE EXIT (each match arm ends by
                    // decrefing its bindings), so skipping here drops no release.
                    if (varTypes.ContainsKey(dstName) && !assignOp.IsDeclaration) {
                      if (!varNameToStructType.ContainsKey(dstName))
                        varNameToStructType[dstName] = structTypeName;
                      var oldHeapPtr = (StdI64)EmitLoad(newBlock, dstName, varTypes);
                      EmitDecrefValueIfNonnull(newBlock, oldHeapPtr, scopeName: func.Name);
                    }
                    var srcHeapPtr = EmitLoad(newBlock, srcName, varTypes);
                    EmitStore(newBlock, srcHeapPtr, dstName, varTypes);
                  }
                  // Incref for the new reference (rc=0 at alloc, every assignment increfs).
                  // Skip incref when ownership was transferred from a callee return —
                  // but NOT for SelfReturn (borrowed reference that needs its own incref).
                  var isSelfReturn = temps.TempHasFlag(srcName, OwnershipFlags.SelfReturn);
                  var isOwnsRef = temps.TempHasFlag(srcName, OwnershipFlags.OwnsRef);
                  var isCallRetTransfer = !isSelfReturn
                      && (assignOp.OwnerFlags?.HasFlag(OwnershipFlags.CallReturn) == true
                          || temps.IsCallReturnTransfer(srcName));
                  // Transferring the source's reference — no incref, and its scope-end release
                  // cancelled — is sound because every construct acquires a value on exactly the
                  // paths that go on to store it. A ternary once broke that: it evaluated BOTH arms
                  // ahead of the branch, so the arm that lost had already allocated, and handed its
                  // reference to a store that never ran. The parser no longer hoists — each arm is
                  // emitted inside its own branch — so the acquisition is once again conditional on
                  // the very same thing the store is.
                  if (isCallRetTransfer || isOwnsRef) {
                    temps.ConsumeTempOwnership(srcName);
                  } else if (assignOp.IsDeclaration || srcName != dstName) {
                    EmitIncref(newBlock, assignOp.VarName, varTypes, scopeName: func.Name);
                  }
                }
                varNameToStructType[assignOp.VarName] = structTypeName;
                if (IsSelfField(isStructInstanceMethod, selfStructType, assignOp.VarName)) {
                  var field = selfStructType!.GetField(assignOp.VarName);
                  if (field != null) {
                    // Self-field write-through: just store the new value to self's heap field.
                    // The regular assign path above already handled decref/incref for the
                    // local variable, which aliases the self field.
                    var heapPtr2 = EmitLoad(newBlock, dstName, varTypes);
                    EmitStructFieldStore(newBlock, heapPtr2, "self", field.Offset, IrType.I64, varTypes);
                  }
                }
                valueMap[assignOp.Value] = stackAllocatedVars.Contains(dstName)
                  ? new StdStackPtr(assignOp.Value.Id, structTypeName, dstName)
                  : new StdHeapPtr(assignOp.Value.Id, structTypeName, dstName);
                varNameToStructPrefix[assignOp.VarName] = dstName;
              } else {
                if (!valueMap.TryGetValue(assignOp.Value, out var mappedValue_))
                  throw new InvalidOperationException($"assign value %{assignOp.Value.Id} (kind={assignOp.Value.GetType().Name}) not in valueMap; assigning to '{assignOp.VarName}' in function '{func.Name}'");
                var mappedValue = mappedValue_;
                // Widen I32/U32 to I64 when the variable was previously stored as I64
                // (e.g., try...otherwise where the default is I64 but the call result is U32)
                if (mappedValue is StdI32 && varTypes.TryGetValue(assignOp.VarName, out var prevType) && prevType == "i64") {
                  mappedValue = EnsureI64(mappedValue, newBlock);
                }
                // A re-declaration of a name that a previous scope registered as
                // managed (e.g. two sibling `try ... otherwise (e) 'a'/'b'` blocks
                // with different error-enum types) needs to clear the stale
                // varNameToStructType entry: the slot is storing a fresh,
                // non-struct value, and a later EmitLoad on this slot would
                // otherwise fabricate a StdHeapPtr from the old registration.
                if (assignOp.IsDeclaration && assignOp.Value is not MaxonStruct) {
                  varNameToStructType.Remove(assignOp.VarName);
                }
                // For self fields, store through self's heap pointer only.
                // Cross-block references load from the heap pointer directly.
                if (IsSelfField(isStructInstanceMethod, selfStructType, assignOp.VarName)) {
                  var field = selfStructType!.GetField(assignOp.VarName);
                  if (field != null)
                    EmitStructFieldStore(newBlock, mappedValue, "self", field.Offset, field.Type, varTypes);
                } else {
                  EmitStore(newBlock, mappedValue, assignOp.VarName, varTypes);
                }
                // A function value is a PAIR — a pointer and the environment its captures live in —
                // and a variable must carry both halves or neither. Binding only the pointer is what
                // made `let f = <closure>` work when called from the binding's own block (the call
                // reused the `closure_create` SSA value, which still knew its environment) and
                // nil-deref when called from any OTHER block, where the only route left is this slot.
                if (assignOp.ValueKind == MaxonValueKind.Function) {
                  var boundEnvPtr = ResolveClosureEnvPtr(mappedValue.Id, newBlock, varTypes, fnEnvVarNames, fnEnvDirectValues);
                  // Rebinding to a value with NO environment must CLEAR the slot, not leave the
                  // previous closure's: the reader cannot tell a live environment from a stale one.
                  if (boundEnvPtr == null && varTypes.ContainsKey(ClosureEnvSlotName(assignOp.VarName))) {
                    var noEnv = new StdConstI64Op(0);
                    newBlock.AddOp(noEnv);
                    boundEnvPtr = noEnv.Result;
                  }
                  if (boundEnvPtr != null)
                    BindClosureEnvSlot(newBlock, boundEnvPtr, assignOp.VarName, varTypes, ownedEnvSlots, func.Name);
                }
                // For struct-typed values that bypassed the StdHeapPtr path (e.g., try-await
                // results which are raw StdI64 heap pointers), register in varNameToStructType
                // so that scope_end emits mm_decref.
                if (assignOp.Value is MaxonStruct msNonHp) {
                  varNameToStructType[assignOp.VarName] = msNonHp.TypeName;
                }
              }
              // Write back through reference pointer for reassigned mutated parameters
              if (!assignOp.IsDeclaration && _refParamPtrVars != null
                  && _refParamPtrVars.TryGetValue(assignOp.VarName, out var refVarNameForWriteBack)) {
                var refPtr = (StdI64)EmitLoad(newBlock, refVarNameForWriteBack, varTypes);
                var localVal = EmitLoad(newBlock, assignOp.VarName, varTypes);
                var writeBackType = varTypes.TryGetValue(assignOp.VarName, out var vt2) ? VarTypeToIrType(vt2) : IrType.I64;
                newBlock.AddOp(new StdStoreIndirectOp(localVal, refPtr, 0, writeBackType));
              }
              break;
            }
            case MaxonVarRefOp varRef: {
              var resolvedVarName = varRef.VarName;
              // In instance methods, self fields are always loaded from self's heap pointer.
              // They don't exist as local variables.
              if (isStructInstanceMethod && selfStructType != null) {
                var field = selfStructType.GetField(resolvedVarName);
                if (field != null && !field.Type.IsHeapAllocated) {
                  // Load scalar self field via heap pointer. Heap-allocated fields
                  // (structs, associated-value unions) go through the StructVarRef /
                  // EnumVarRef paths instead, which manage refcounts on the loaded
                  // pointer.
                  var loaded = EmitStructFieldLoad(newBlock, "self", field.Offset, field.Type, varTypes);
                  valueMap[varRef.Result] = loaded;
                  break;
                }
              }
              // After monomorphization, a VarRefOp originally typed as Integer may
              // actually refer to a struct variable. If the variable is a struct prefix,
              // handle it as a struct reference.
              if (!varTypes.ContainsKey(resolvedVarName) && varNameToStructPrefix.TryGetValue(resolvedVarName, out string? structPrefix)) {
                var resolvedType = varNameToStructType.TryGetValue(resolvedVarName, out var stType)
                  ? stType
                  : (valueMap.TryGetValue(varRef.Result, out var existSv) && existSv is StdHeapPtr existHp ? existHp.TypeName : "unknown");
                valueMap[varRef.Result] = new StdHeapPtr(varRef.Result.Id, resolvedType, structPrefix);
                break;
              }
              var loaded2 = EmitLoad(newBlock, resolvedVarName, varTypes);
              valueMap[varRef.Result] = loaded2;
              break;
            }
            case MaxonStructVarRefOp structVarRef: {
              // With heap refs, self fields are accessed via indirect loads from self's heap pointer.
              // For struct-typed self fields, load the nested heap pointer and store in a temp var.
              // Cache the load so repeated references to the same self field reuse the temp var.
              string resolvedName;
              // Fused String/Character/Array: a bare `managed` reference IS `self` (the inline
              // __ManagedMemory sits at offset 0), so it resolves to the receiver pointer itself,
              // typed as the owner so construction can view it as a 48/40-byte record.
              if (structVarRef.VarName == "managed" && isStructInstanceMethod && selfStructType != null
                  && selfStructType.ConformsToBuiltinManagedWrapper) {
                valueMap[structVarRef.Result] = new StdHeapPtr(structVarRef.Result.Id, selfStructType.Name, "self");
                break;
              }
              if (IsSelfField(isStructInstanceMethod, selfStructType, structVarRef.VarName)) {
                if (selfFieldCache.TryGetValue(structVarRef.VarName, out var cachedName)) {
                  resolvedName = cachedName;
                } else {
                  var field = selfStructType!.GetField(structVarRef.VarName)!;
                  var tempVarName = temps.CreateTemp("selfref", structVarRef.Result.Id, structVarRef.StructTypeName, OwnershipFlags.Borrowed);
                  var nestedPtr = EmitStructFieldLoad(newBlock, "self", field.Offset, IrType.I64, varTypes);
                  EmitStore(newBlock, nestedPtr, tempVarName, varTypes);
                  resolvedName = tempVarName;
                  selfFieldCache[structVarRef.VarName] = tempVarName;
                }
              } else {
                resolvedName = varNameToStructPrefix.GetValueOrDefault(structVarRef.VarName, structVarRef.VarName);
              }
              // Prefer the canonical type from varNameToStructType (set during struct
              // assignment with resolved types) over the StructTypeName from the parser
              // which may contain stale inner alias names (e.g. "Entry" instead of
              // "StringIntPair") when the call-site rewrite preserved the old Result type.
              var resolvedTypeName = varNameToStructType.TryGetValue(structVarRef.VarName, out var vt)
                ? vt
                : structVarRef.StructTypeName;
              valueMap[structVarRef.Result] = stackAllocatedVars.Contains(resolvedName)
                ? new StdStackPtr(structVarRef.Result.Id, resolvedTypeName, resolvedName)
                : new StdHeapPtr(structVarRef.Result.Id, resolvedTypeName, resolvedName);
              break;
            }
            case MaxonFieldAccessOp fieldAccess: {
              var structName = ((StdHeapPtr)valueMap[fieldAccess.StructValue]).VarName!;
              // Resolve the field type and offset from the struct type definition
              var parentTypeName = valueMap.TryGetValue(fieldAccess.StructValue, out var ptnSv2) && ptnSv2 is StdHeapPtr ptnHp2 ? ptnHp2.TypeName : null;
              IrStructType? parentStructType = null;
              if (parentTypeName != null && module.TypeDefs.TryGetValue(parentTypeName, out var ptDef) && ptDef is IrStructType pst)
                parentStructType = pst;
              var fieldDef = parentStructType?.GetField(fieldAccess.FieldName);
              // If the field has an unresolved type parameter type (e.g., Entry._1 = Value),
              // resolve by finding a concrete alias with the same source type.
              if (fieldDef != null && fieldDef.Type is IrTypeParameterType && parentTypeName != null
                  && module.TypeAliasSources.TryGetValue(parentTypeName, out var parentAliasInfo)) {
                foreach (var (candidateName, candidateInfo) in module.TypeAliasSources) {
                  if (candidateName == parentTypeName) continue;
                  if (candidateInfo.SourceTypeName != parentAliasInfo.SourceTypeName) continue;
                  if (candidateInfo.TypeParams == null || candidateInfo.TypeParams.Values.Any(t => t is IrTypeParameterType)) continue;
                  if (module.TypeDefs.TryGetValue(candidateName, out var candidateDef) && candidateDef is IrStructType candidateSt) {
                    var resolvedField = candidateSt.GetField(fieldAccess.FieldName);
                    if (resolvedField != null && resolvedField.Type is not IrTypeParameterType) {
                      fieldDef = resolvedField;
                      break;
                    }
                  }
                }
              }

              // Fused String/Character/Array: `self.managed` IS `self`. The managed __ManagedMemory
              // is embedded at offset 0, so its address equals the receiver pointer — yield that
              // pointer rather than loading a nested one. Typed as the owner (String/Character/Array)
              // so slice sizing, construction and cursor creation can tell it is a 48/40-byte record,
              // not a bare 40-byte __ManagedMemory. For an Array the type carries the real `Element`
              // param, so element-typed managed ops (push/get/decref) see the correct stride.
              if (parentStructType != null && fieldAccess.FieldName == "managed"
                  && parentStructType.ConformsToBuiltinManagedWrapper) {
                var managedTempName = temps.CreateTemp("managed", fieldAccess.Result.Id, parentTypeName!, OwnershipFlags.Borrowed);
                var selfPtr = EmitLoad(newBlock, structName, varTypes);
                EmitStore(newBlock, selfPtr, managedTempName, varTypes);
                valueMap[fieldAccess.Result] = new StdHeapPtr(fieldAccess.Result.Id, parentTypeName!, managedTempName);
                if (structName == "self" && !varTypes.ContainsKey(fieldAccess.FieldName)) {
                  varNameToStructPrefix[fieldAccess.FieldName] = managedTempName;
                }
                break;
              }

              // Fused String/Character/Array expose their inline __ManagedMemory fields (length,
              // buffer, capacity, ...) at the same offsets, but the wrapper type itself declares
              // only `managed` (+ `singleByteGraphemesFlag` for String). Resolve those fields against the op's
              // declared parent type (__ManagedMemory), whose layout matches the embedded record.
              if (fieldDef == null && parentStructType != null
                  && parentStructType.ConformsToBuiltinManagedWrapper
                  && module.TypeDefs.TryGetValue(fieldAccess.TypeName, out var declaredMmDef)
                  && declaredMmDef is IrStructType declaredMmStruct) {
                fieldDef = declaredMmStruct.GetField(fieldAccess.FieldName);
              }

              if (fieldAccess.ResultKind == MaxonValueKind.Struct) {
                // Struct-typed field: load the nested struct's heap pointer and store it in a temp var
                var fieldTypeName = fieldDef?.Type is IrStructType fst ? fst.Name : (fieldAccess.ResultStructTypeName ?? "unknown");
                var tempVarName = temps.CreateTemp("field", fieldAccess.Result.Id, fieldTypeName, OwnershipFlags.Borrowed);
                if (fieldDef != null) {
                  var nestedPtr = EmitStructFieldLoad(newBlock, structName, fieldDef.Offset, IrType.I64, varTypes);
                  EmitStore(newBlock, nestedPtr, tempVarName, varTypes);
                  // For self fields accessed via self, also initialize the field name variable so that
                  // later code referencing it by name (across conditional blocks) gets the correct value.
                  // Only do this when the field access is on self, not on another struct that happens
                  // to have the same field name (e.g., other.managed vs self.managed in append).
                  if (structName == "self" && IsSelfField(isStructInstanceMethod, selfStructType, fieldAccess.FieldName)) {
                    EmitStore(newBlock, nestedPtr, fieldAccess.FieldName, varTypes);
                    // Track this temp so ReloadSelfFieldLocals can update it after calls
                    selfFieldTempVars[fieldAccess.FieldName] = tempVarName;
                  }
                } else {
                  // Fallback: try loading as a named variable (legacy path)
                  var loaded = EmitLoad(newBlock, $"{structName}.{fieldAccess.FieldName}", varTypes);
                  EmitStore(newBlock, loaded, tempVarName, varTypes);
                }
                // Propagate type info for the nested struct field
                var resolvedFieldType = fieldDef?.Type is IrStructType fieldStructType ? fieldStructType.Name : fieldTypeName;
                valueMap[fieldAccess.Result] = new StdHeapPtr(fieldAccess.Result.Id, resolvedFieldType, tempVarName);
                // The prefix install lets later bare-name references to a self
                // field resolve to the temp holding the loaded field pointer.
                // Gate on `self`: a non-self access like `obj.name` would
                // otherwise shadow a later local `name` in a sibling block
                // with the field tempvar (which only lives for that op).
                if (structName == "self" && !varTypes.ContainsKey(fieldAccess.FieldName)) {
                  varNameToStructPrefix[fieldAccess.FieldName] = tempVarName;
                }
              } else if (fieldAccess.ResultKind == MaxonValueKind.Enum
                         && fieldAccess.ResultStructTypeName != null
                         && module.TypeDefs.TryGetValue(fieldAccess.ResultStructTypeName, out var faEnumDef)
                         && faEnumDef is IrEnumType faEnumType && faEnumType.HasAssociatedValues) {
                // Associated-value enum field: load heap pointer (no unpacking needed)
                var tempVarName = temps.CreateTemp("field", fieldAccess.Result.Id, fieldAccess.ResultStructTypeName!, OwnershipFlags.Borrowed);
                if (fieldDef != null) {
                  var enumPtr = EmitStructFieldLoad(newBlock, structName, fieldDef.Offset, IrType.I64, varTypes);
                  EmitStore(newBlock, enumPtr, tempVarName, varTypes);
                } else {
                  var loaded = EmitLoad(newBlock, $"{structName}.{fieldAccess.FieldName}", varTypes);
                  EmitStore(newBlock, loaded, tempVarName, varTypes);
                }
                valueMap[fieldAccess.Result] = new StdHeapPtr(fieldAccess.Result.Id, fieldAccess.ResultStructTypeName!, tempVarName);
                // Same gating as the struct-field branch above: only install the
                // prefix mapping for self fields, otherwise a non-self
                // `obj.fieldName` access would shadow a later local of the same
                // name in a sibling block.
                if (structName == "self" && !varTypes.ContainsKey(fieldAccess.FieldName)) {
                  varNameToStructPrefix[fieldAccess.FieldName] = tempVarName;
                }
                // For self fields, store the heap pointer under the field name so that
                // later code referencing it by name (across conditional blocks) gets the correct value.
                if (IsSelfField(isStructInstanceMethod, selfStructType, fieldAccess.FieldName)) {
                  EmitStore(newBlock, EmitLoad(newBlock, tempVarName, varTypes), fieldAccess.FieldName, varTypes);
                  varNameToStructPrefix[fieldAccess.FieldName] = fieldAccess.FieldName;
                  // Track this temp so ReloadSelfFieldLocals can update it after calls
                  // that may mutate self.<field> (e.g. inner method writes self.pending,
                  // freeing the previously-aliased heap pointer).
                  selfFieldTempVars[fieldAccess.FieldName] = tempVarName;
                }
              } else {
                // Scalar field access
                StdValue loaded;
                if (fieldDef != null && valueMap[fieldAccess.StructValue] is StdStackPtr stackPtr
                    && stackPtr.VarName != null && stackVarTags.TryGetValue(stackPtr.VarName, out var faTag)) {
                  // Stack struct: load directly from BulkZero slot (no pointer indirection)
                  var faStructType = (IrStructType)module.TypeDefs[stackPtr.TypeName];
                  loaded = EmitLoad(newBlock, StackSlotName(faTag, faStructType, fieldDef.Offset), varTypes);
                } else if (fieldDef != null) {
                  loaded = EmitStructFieldLoad(newBlock, structName, fieldDef.Offset, fieldDef.Type, varTypes);
                } else {
                  // Fallback: try loading as a named variable (legacy path)
                  loaded = EmitLoad(newBlock, $"{structName}.{fieldAccess.FieldName}", varTypes);
                }
                // THE CAPACITY SENTINEL STOPS HERE. A record that borrows its buffer stores a
                // NEGATIVE capacity (-2 rdata, -1 view) to mark it non-owned, and
                // `__ManagedMemory.capacity()` is the one read that hands that value to Maxon
                // source — as a declared `int(0 to u64.max)` that `Array.reserve` and
                // `ensureCapacity` immediately do arithmetic on. `minCapacity > cap` read -2 as
                // "smaller than anything", so `[10, 20, 30].resize(0)` concluded it had to GROW
                // to zero slots and the allocator refused the zero-byte request. A borrowed
                // buffer owns no writable slot, so the answer that keeps every caller's
                // arithmetic sound is 0.
                if (fieldAccess.ClampNegativeSentinel) {
                  // The clamp is signed arithmetic on the loaded word, so a narrower field would
                  // silently mean something else here. Name the mismatch instead of letting the
                  // cast raise InvalidCastException from inside a lowering.
                  if (loaded is not StdI64 capacityWord)
                    throw new InvalidOperationException(
                      $"ClampNegativeSentinel is set on '{fieldAccess.TypeName}.{fieldAccess.FieldName}', "
                      + $"which loads as {loaded.GetType().Name}; only an i64 field carries the negative "
                      + "non-owned sentinel this clamp exists to remove");
                  valueMap[fieldAccess.Result] = EmitClampCapacityNonNeg(newBlock, capacityWord);
                } else {
                  valueMap[fieldAccess.Result] = loaded;
                }
              }
              break;
            }
            case MaxonFieldAssignOp fieldAssign: {
              var structName = ((StdHeapPtr)valueMap[fieldAssign.StructValue]).VarName!;

              // Resolve the field type and offset from the struct type definition
              var faParentTypeName = valueMap.TryGetValue(fieldAssign.StructValue, out var faptnSv2) && faptnSv2 is StdHeapPtr faptnHp2 ? faptnHp2.TypeName : null;
              IrStructType? faParentStructType = null;
              if (faParentTypeName != null && module.TypeDefs.TryGetValue(faParentTypeName, out var faptDef) && faptDef is IrStructType fapst)
                faParentStructType = fapst;
              var faFieldDef = faParentStructType?.GetField(fieldAssign.FieldName);

              if (!valueMap.TryGetValue(fieldAssign.NewValue, out StdValue? mappedVal)) {
                throw new InvalidOperationException($"MaxonFieldAssignOp: NewValue %{fieldAssign.NewValue.Id} not in valueMap for {structName}.{fieldAssign.FieldName} in func {func.Name}");
              }
              // StdHeapPtr values must be loaded from their temp variable
              if (mappedVal is StdHeapPtr newValHp) {
                mappedVal = EmitLoad(newBlock, newValHp.VarName!, varTypes);
              }

              if (faFieldDef != null && valueMap[fieldAssign.StructValue] is StdStackPtr faStackPtr
                  && faStackPtr.VarName != null && stackVarTags.TryGetValue(faStackPtr.VarName, out var fsTag)
                  && !faFieldDef.Type.IsHeapAllocated) {
                // Stack struct with primitive field: store directly to BulkZero slot
                var fsStructType = (IrStructType)module.TypeDefs[faStackPtr.TypeName];
                EmitStore(newBlock, mappedVal, StackSlotName(fsTag, fsStructType, faFieldDef.Offset), varTypes);
              } else if (faFieldDef != null) {
                var isHeapField = faFieldDef.Type.IsHeapAllocated;
                var storeType = isHeapField ? IrType.I64 : faFieldDef.Type;
                if (isHeapField) {
                  // Decref old field value before overwriting (may be null if field not yet initialized)
                  var oldFieldVal = (StdI64)EmitStructFieldLoad(newBlock, structName, faFieldDef.Offset, IrType.I64, varTypes);
                  EmitDecrefValueIfNonnull(newBlock, oldFieldVal, scopeName: func.Name);
                }
                EmitStructFieldStore(newBlock, mappedVal, structName, faFieldDef.Offset, storeType, varTypes);
                if (isHeapField) {
                  // Incref new value — the struct field holds a reference
                  EmitIncrefValue(newBlock, (StdI64)mappedVal, scopeName: func.Name);
                }
              } else {
                EmitStore(newBlock, mappedVal, $"{structName}.{fieldAssign.FieldName}", varTypes);
              }
              // No write-through needed: self is a heap pointer, and all field stores
              // go through the heap pointer directly, so the caller sees changes.
              if (structName == "self") selfFieldCache.Remove(fieldAssign.FieldName);
              break;
            }
            case MaxonBinOp binOp: {
              if (TryAlgebraicIdentity(binOp, literalMap, valueMap, newBlock, out var identityResult)) {
                valueMap[binOp.Result] = identityResult;
                break;
              }

              // Load operand from valueMap; fall back to StdHeapPtr for type-parameter
              // fields promoted to Struct kind (they store heap pointers as i64 values)
              if (!valueMap.TryGetValue(binOp.Lhs, out StdValue? lhs)) {
                throw new InvalidOperationException($"BinOp LHS %{binOp.Lhs.Id} not in valueMap in func {func.Name} block {block.Name}, op: {binOp.Operator} {binOp.OperandKind}");
              }
              if (lhs is StdHeapPtr lhsHpBinOp)
                lhs = EmitLoad(newBlock, lhsHpBinOp.VarName!, varTypes);
              if (!valueMap.TryGetValue(binOp.Rhs, out StdValue? rhs)) {
                throw new InvalidOperationException($"BinOp RHS %{binOp.Rhs.Id} not in valueMap in func {func.Name} block {block.Name}, op: {binOp.Operator} {binOp.OperandKind}");
              }
              if (rhs is StdHeapPtr rhsHpBinOp)
                rhs = EmitLoad(newBlock, rhsHpBinOp.VarName!, varTypes);

              // EVERY integer shift is built by EmitShift, and none of them reaches the width
              // dispatch below. A shift is 64 bits wide (ShiftSemantics' width bullet): a ranged
              // left operand decides how the shift FILLS and never how WIDE it is, and the two
              // questions were conflated here. A constant count in 0..63 used to fall through to
              // the narrowing, which truncated the shift's VALUE — `(0-8) shl 29` on an
              // `int(-2^31 to 2^31-1)` answered 0, where the same shift by a count the compiler
              // could not see answered -4294967296. (The count survived only by luck: a 32-bit
              // shift op MEANS a 5-bit count mask, but the x86/arm64 lowering of one emitted a
              // 64-bit `shl reg, cl` regardless, so the mask that would have made `x shr 33` into
              // `x shr 1` never bit. A latent wrong answer, one op away from a real one.)
              if (IsIntegerShift(binOp)) {
                valueMap[binOp.Result] = EmitShift(binOp, lhs, rhs, literalMap, newBlock);
                break;
              }

              // Use OptimalType to select narrower/unsigned ops
              if (binOp.OperandKind == MaxonValueKind.Integer && binOp.OptimalType is IrType ot) {
                var signedOt = ot.ToSigned();
                if (signedOt == IrType.I32 || signedOt == IrType.I8) {
                  var i32Lhs = EnsureI32(lhs, newBlock);
                  var i32Rhs = EnsureI32(rhs, newBlock);
                  var (i32Op, i32Result) = ot.IsUnsigned
                    ? CreateUnsignedI32BinOp(binOp.Operator, i32Lhs, i32Rhs)
                    : CreateSignedI32BinOp(binOp.Operator, i32Lhs, i32Rhs);
                  newBlock.AddOp(i32Op);
                  valueMap[binOp.Result] = ot.IsUnsigned && i32Result is StdI32 ? new StdU32(i32Result.Id) : i32Result;
                  break;
                }
                if (ot.IsUnsigned) {
                  var i64Lhs = EnsureI64(lhs, newBlock, signExtend: false);
                  var i64Rhs = EnsureI64(rhs, newBlock, signExtend: false);
                  var (unsignedOp, unsignedResult) = CreateUnsignedIntBinOp(binOp.Operator, i64Lhs, i64Rhs);
                  newBlock.AddOp(unsignedOp);
                  valueMap[binOp.Result] = unsignedResult;
                  break;
                }
              }

              // Widen narrowed operands back to i64 for full-width integer ops
              if (binOp.OperandKind == MaxonValueKind.Integer) {
                if (lhs is StdI32 or StdU32) lhs = EnsureI64(lhs, newBlock);
                if (rhs is StdI32 or StdU32) rhs = EnsureI64(rhs, newBlock);
              }

              // Enums are compared as integers at the standard level
              var baseKind = binOp.OperandKind == MaxonValueKind.Enum ? MaxonValueKind.Integer : binOp.OperandKind;
              // F32 values arrive with Float kind from Maxon dialect; dispatch to Float32 ops
              var effectiveKind = baseKind == MaxonValueKind.Float && (lhs is StdF32 || rhs is StdF32)
                ? MaxonValueKind.Float32 : baseKind;
              if (effectiveKind == MaxonValueKind.Float32) {
                if (lhs is StdF64 lhsF64) { var cvt = new StdF64ToF32Op(lhsF64); newBlock.AddOp(cvt); lhs = cvt.Result; }
                if (rhs is StdF64 rhsF64) { var cvt = new StdF64ToF32Op(rhsF64); newBlock.AddOp(cvt); rhs = cvt.Result; }
              }
              var key = (binOp.Operator, effectiveKind);
              if (!BinOpFactories.TryGetValue(key, out var factory))
                throw new InvalidOperationException($"Unsupported binop: {binOp.Operator} on {binOp.OperandKind} in func {func.Name} block {block.Name}");

              var (newOp, factoryResult) = factory(lhs, rhs);
              newBlock.AddOp(newOp);
              valueMap[binOp.Result] = factoryResult;
              break;
            }
            case MaxonRefEqOp refEq: {
              // Struct values are tracked by StdHeapPtr in valueMap — load their heap pointers
              var lhsVarName = ((StdHeapPtr)valueMap[refEq.Lhs]).VarName!;
              var rhsVarName = ((StdHeapPtr)valueMap[refEq.Rhs]).VarName!;
              var lhsPtr = (StdI64)EmitLoad(newBlock, lhsVarName, varTypes);
              var rhsPtr = (StdI64)EmitLoad(newBlock, rhsVarName, varTypes);
              var predicate = refEq.Negate ? "ne" : "eq";
              var cmpOp = new StdCmpI64Op(predicate, lhsPtr, rhsPtr);
              newBlock.AddOp(cmpOp);
              valueMap[refEq.Result] = cmpOp.Result;
              break;
            }
            case MaxonCondBrOp condBr: {
              var cond = (StdBool)valueMap[condBr.Condition];
              newBlock.AddOp(new StdCondBrOp(cond, condBr.ThenBlock, condBr.ElseBlock));
              break;
            }
            case MaxonBrOp br: {
              newBlock.AddOp(new StdBrOp(br.Target));
              break;
            }
            case MaxonCovPointOp covPoint: {
              newBlock.AddOp(new StdCovPointOp(covPoint.PointId));
              break;
            }
            case MaxonSwitchOp switchOp: {
              LowerSwitch(switchOp, newFunc, newBlock);
              break;
            }
            case MaxonScopeEndOp scopeEnd: {
              // A value-tuple return hands the caller COPIES of the two halves, so it transfers
              // NOTHING — and this cleanup runs BEFORE the return op that reads them. Both facts
              // matter, and together they make this the one return shape that wants no help from
              // the machinery below:
              //
              //  - Read the halves HERE, while the record is still guaranteed live. Once they
              //    are SSA values, nothing the cleanup goes on to do to the record — decref,
              //    free, destroy the parent that owns it — can reach them. That removes the need
              //    for the pre-incref that keeps a returned Borrowed temp alive past cleanup.
              //  - Ignore KeepVars. It suppresses the decref of whatever the return hands over,
              //    which is right for a returned POINTER and wrong for a returned pair of
              //    scalars: nothing is handed over, so every binding this scope holds dies here.
              //    Honouring it leaks exactly the record the halves were just read out of.
              //
              // Only a HEAP record needs the read hoisted. A stack-promoted one — the common
              // case, and the whole point of the ABI — has no refcount and survives cleanup
              // untouched, so LowerReturn reads its slots directly.
              bool valueTupleReturn = _valueTupleReturnFunctions?.Contains(func.Name) == true;
              var keep = valueTupleReturn ? null : scopeEnd.KeepVars;

              if (valueTupleReturn && retStructType != null) {
                foreach (var retId in structLitReturnIds) {
                  foreach (var (mv, sv) in valueMap) {
                    if (mv.Id != retId || sv is StdStackPtr || sv is not StdHeapPtr recordHp
                        || recordHp.VarName == null) continue;

                    _valueTupleReturnStash![retId] = (
                      EmitValueTupleHalfLoad(newBlock, recordHp, retStructType, 0, varTypes),
                      EmitValueTupleHalfLoad(newBlock, recordHp, retStructType, 1, varTypes));
                    break;
                  }
                }
              }

              // Pre-incref Borrowed field temps that are being returned.
              // When returning `structVar.field`, the field is loaded into a Borrowed temp.
              // The scope cleanup decrefs structVar (whose destructor decrefs the field),
              // but the Borrowed temp still holds a pointer to the field.
              // Incref the field BEFORE scope cleanup so it survives the destructor.
              foreach (var retId in valueTupleReturn ? [] : structLitReturnIds) {
                foreach (var (mv, sv) in valueMap) {
                  if (mv.Id == retId && sv is StdHeapPtr retFieldHp && retFieldHp.VarName != null
                      && temps.TempHasFlag(retFieldHp.VarName, OwnershipFlags.Borrowed)) {
                    var fieldPtr = (StdI64)EmitLoad(newBlock, retFieldHp.VarName, varTypes);
                    EmitIncrefValueIfNonnull(newBlock, fieldPtr, scopeName: func.Name);
                    // Mark as SelfReturn so LowerReturn doesn't incref again
                    temps.SetTempFlag(retFieldHp.VarName, OwnershipFlags.SelfReturn);
                    break;
                  }
                }
              }

              // Process in reverse order: variables declared later (containers,
              // iterators) must be freed before their backing stores (array
              // element slots) so destructors can still read live element data.
              var varsToClean = scopeEnd.VarsToClean.ToList();
              varsToClean.Reverse();
              foreach (var v in varsToClean) {
                if (keep != null && keep.Contains(v)) {
                  // Ownership transferred to caller — skip decref but emit trace if enabled.
                  if (Compiler.MmTrace && varNameToStructType.ContainsKey(v)) {
                    var transferPtr = EmitLoad(newBlock, v, varTypes);
                    var transferScopePtr = EmitTagPtr(newBlock, func.Name);
                    newBlock.AddOp(new StdCallRuntimeOp("mm_trace_transfer", [transferPtr, transferScopePtr], null));
                  }
                  continue;
                }
                // A function-typed binding is not "managed" — it holds a code pointer, and every
                // check below skips it — but the capture ENVIRONMENT paired with it is heap, and
                // this is the scope that ends its reachability. It must be released here and not
                // by the orphan sweep below, which would drop it at whichever scope_end runs first
                // however many scopes early that is.
                ReleaseClosureEnvSlot(newBlock, v, varTypes, ownedEnvSlots, func.Name);

                if (_structParamNames != null && _structParamNames.Contains(v)) continue;
                // Self fields are owned by the heap-allocated struct; the struct destructor
                // handles their cleanup when self is freed. Decref'ing them here would
                // double-free after a field reassignment (assign decrefs old, scope_end
                // would then decref the new value still held by the field).
                if (IsSelfField(isStructInstanceMethod, selfStructType, v)) continue;
                // Stack-allocated structs need no refcount cleanup — stack reclaims them
                if (stackAllocatedVars.Contains(v)) continue;
                // Parser-attached metadata is authoritative about this binding's
                // type at scope-exit, which matters when two sibling scopes reuse
                // the same name with different kinds (e.g. `try ... otherwise (e) 'a' ... end 'a'`
                // with an assoc-value enum followed by a `try ... otherwise (e) 'b' ... end 'b'`
                // with a simple-enum error). Trust VarMetadata over the stale
                // varNameToStructType registration that a prior scope may have
                // left behind: skip decref when the metadata names a non-managed
                // type (no StructTypeName, or a simple enum with no associated
                // values — those are just integer ordinals). Also clear
                // varNameToStructType so that later EmitLoads on the same slot
                // don't return a fabricated StdHeapPtr — once the scope that
                // owned the managed value ends, the slot is back to being plain
                // storage for whatever the next scope stores there.
                if (scopeEnd.VarMetadata != null
                    && scopeEnd.VarMetadata.TryGetValue(v, out var meta)) {
                  bool notManaged = meta.StructTypeName == null
                    || (module.TypeDefs.TryGetValue(meta.StructTypeName, out var metaTy)
                        && metaTy is IrEnumType metaEnumTy
                        && !metaEnumTy.HasAssociatedValues);
                  if (notManaged) {
                    varNameToStructType.Remove(v);
                    continue;
                  }
                }
                // Only decref if this var is actually managed (has a struct type)
                if (!varNameToStructType.ContainsKey(v)) continue;
                // Simple mm_decref — destructors handle field cleanup when rc reaches 0
                var heapPtr = (StdI64)EmitLoad(newBlock, v, varTypes);
                EmitDecrefValueIfNonnull(newBlock, heapPtr, scopeName: func.Name);
                // Zero the slot so other paths see NULL (null-guarded decref skips it)
                var zeroOp = new StdConstI64Op(0);
                newBlock.AddOp(zeroOp);
                newBlock.AddOp(new StdStoreI64Op(zeroOp.Result, v));
              }
              // Build set of orphan temps that back a returned value — these must
              // survive scope cleanup so LowerReturn can read and transfer them.
              var returnedOrphanTemps = new HashSet<string>();
              foreach (var retId in structLitReturnIds) {
                // Find the temp name for this returned value via valueMap StdHeapPtr
                foreach (var (mv, sv) in valueMap) {
                  if (mv.Id == retId && sv is StdHeapPtr retHpTemp && retHpTemp.VarName != null
                      && temps.TempHasFlag(retHpTemp.VarName, OwnershipFlags.Orphan)) {
                    returnedOrphanTemps.Add(retHpTemp.VarName);
                    break;
                  }
                }
              }
              // Decref orphan temps created during lowering (not in parser scope tracking)
              foreach (var tempName in temps.OrphanTemps) {
                if (returnedOrphanTemps.Contains(tempName)) continue;
                var orphanPtr = (StdI64)EmitLoad(newBlock, tempName, varTypes);
                EmitDecrefValueIfNonnull(newBlock, orphanPtr, scopeName: func.Name);
                var zeroGlobal = new StdConstI64Op(0);
                newBlock.AddOp(zeroGlobal);
                newBlock.AddOp(new StdStoreI64Op(zeroGlobal.Result, tempName));
              }
              break;
            }
            case MaxonTruncOp truncOp: {
              var mappedInput = valueMap[truncOp.Input];
              if (mappedInput is StdF64 f64Input) {
                var stdOp = new StdFpToSiOp(f64Input);
                newBlock.AddOp(stdOp);
                valueMap[truncOp.Result] = stdOp.Result;
              } else if (mappedInput is StdF32 f32Input) {
                var stdOp = new StdFpToSiF32Op(f32Input);
                newBlock.AddOp(stdOp);
                valueMap[truncOp.Result] = stdOp.Result;
              } else if (mappedInput is StdI64 or StdI32) {
                // Ranged int types resolve to integer standard values; truncation only applies to float-to-int
                valueMap[truncOp.Result] = mappedInput;
              } else {
                throw new InvalidOperationException($"MaxonTruncOp: unexpected input type {mappedInput.GetType().Name}");
              }
              break;
            }
            case MaxonBitcastF64ToI64Op bitcastOp: {
              var input = (StdF64)valueMap[bitcastOp.Input];
              var stdOp = new StdBitcastF64ToI64Op(input);
              newBlock.AddOp(stdOp);
              valueMap[bitcastOp.Result] = stdOp.Result;
              break;
            }
            case MaxonBitcastI64ToF64Op bitcastOp: {
              var input = (StdI64)valueMap[bitcastOp.Input];
              var stdOp = new StdBitcastI64ToF64Op(input);
              newBlock.AddOp(stdOp);
              valueMap[bitcastOp.Result] = stdOp.Result;
              break;
            }
            case MaxonIntToFloatOp intToFloatOp: {
              var input = (StdI64)valueMap[intToFloatOp.Input];
              var stdOp = new StdSiToFpOp(input);
              newBlock.AddOp(stdOp);
              valueMap[intToFloatOp.Result] = stdOp.Result;
              break;
            }
            case MaxonSizeofOp sizeofOp: {
              var sizeofType = ResolveSizeofType(sizeofOp.TypeName, module);
              var constOp = new StdConstI64Op((long)sizeofType.SizeInBytes);
              newBlock.AddOp(constOp);
              valueMap[sizeofOp.Result] = constOp.Result;
              break;
            }
            case MaxonCountofOp countofOp: {
              var constOp = new StdConstI64Op(ResolveCountofElementCount(countofOp, module));
              newBlock.AddOp(constOp);
              valueMap[countofOp.Result] = constOp.Result;
              break;
            }
            case MaxonAbsOp absOp:
              LowerUnaryFloat(valueMap, newBlock, absOp.Input, absOp.Result, i => new StdAbsF32Op(i), i => new StdAbsF64Op(i));
              break;
            case MaxonSqrtOp sqrtOp:
              LowerUnaryFloat(valueMap, newBlock, sqrtOp.Input, sqrtOp.Result, i => new StdSqrtF32Op(i), i => new StdSqrtF64Op(i));
              break;
            case MaxonFloorOp floorOp:
              LowerUnaryFloat(valueMap, newBlock, floorOp.Input, floorOp.Result, i => new StdFloorF32Op(i), i => new StdFloorF64Op(i));
              break;
            case MaxonCeilOp ceilOp:
              LowerUnaryFloat(valueMap, newBlock, ceilOp.Input, ceilOp.Result, i => new StdCeilF32Op(i), i => new StdCeilF64Op(i));
              break;
            case MaxonRoundOp roundOp:
              LowerUnaryFloat(valueMap, newBlock, roundOp.Input, roundOp.Result, i => new StdRoundF32Op(i), i => new StdRoundF64Op(i));
              break;
            case MaxonMinOp minOp:
              LowerBinaryFloat(valueMap, newBlock, minOp.Lhs, minOp.Rhs, minOp.Result, (l, r) => new StdMinF32Op(l, r), (l, r) => new StdMinF64Op(l, r));
              break;
            case MaxonMaxOp maxOp:
              LowerBinaryFloat(valueMap, newBlock, maxOp.Lhs, maxOp.Rhs, maxOp.Result, (l, r) => new StdMaxF32Op(l, r), (l, r) => new StdMaxF64Op(l, r));
              break;
            case MaxonCastOp castOp: {
              var input = valueMap[castOp.Input];
              switch (castOp.TargetKind) {
                case MaxonValueKind.Byte: {
                  // Cast to byte: convert to i64 if needed, then mask with 0xFF
                  StdI64 intInput;
                  if (input is StdI64 alreadyI64) {
                    intInput = alreadyI64;
                  } else if (input is StdI32 i32Input) {
                    throw new InvalidOperationException("i32 to byte conversion not yet implemented");
                  } else if (input is StdF64 f64Input) {
                    var fpToSi = new StdFpToSiOp(f64Input);
                    newBlock.AddOp(fpToSi);
                    intInput = fpToSi.Result;
                  } else if (input is StdF32 f32Input) {
                    var fpToSi = new StdFpToSiF32Op(f32Input);
                    newBlock.AddOp(fpToSi);
                    intInput = fpToSi.Result;
                  } else if (input is StdBool boolInput) {
                    // Bool to byte: bool is already 0 or 1, just reinterpret as i64
                    // Create a StdI64 that shares the same ID (reinterpretation)
                    intInput = new StdI64(boolInput.Id);
                  } else if (input is StdPtr) {
                    throw new InvalidOperationException("Cannot cast pointer to byte");
                  } else {
                    throw new InvalidOperationException($"Cannot cast {input.GetType().Name} to byte");
                  }
                  var maskOp = new StdConstI64Op(0xFF);
                  newBlock.AddOp(maskOp);
                  var andOp = new StdAndI64Op(intInput, maskOp.Result);
                  newBlock.AddOp(andOp);
                  valueMap[castOp.Result] = andOp.Result;
                  break;
                }
                case MaxonValueKind.Integer: {
                  // Byte/short/int to int: pass through (sub-word types are stored as I64)
                  if (input is StdI64 i64) {
                    valueMap[castOp.Result] = i64;
                  } else if (input is StdI32 i32) {
                    throw new InvalidOperationException("i32 to int conversion not yet implemented");
                  } else if (input is StdF64 f64) {
                    var fpToSi = new StdFpToSiOp(f64);
                    newBlock.AddOp(fpToSi);
                    valueMap[castOp.Result] = fpToSi.Result;
                  } else if (input is StdF32 f32) {
                    var fpToSi = new StdFpToSiF32Op(f32);
                    newBlock.AddOp(fpToSi);
                    valueMap[castOp.Result] = fpToSi.Result;
                  } else if (input is StdBool boolInput) {
                    // Bool to int: bool is already 0 or 1, reinterpret as i64
                    valueMap[castOp.Result] = new StdI64(boolInput.Id);
                  } else if (input is StdPtr) {
                    throw new InvalidOperationException("Cannot cast pointer to int (use explicit ptr_to_i64 operation)");
                  } else {
                    throw new InvalidOperationException($"Unsupported cast to int from: {input.GetType().Name}");
                  }
                  break;
                }
                case MaxonValueKind.Float: {
                  if (input is StdI64 i64) {
                    var sourceIsUnsigned = castOp.SourceIsUnsigned;
                    if (sourceIsUnsigned) {
                      var uiToFp = new StdUiToFpOp(i64);
                      newBlock.AddOp(uiToFp);
                      valueMap[castOp.Result] = uiToFp.Result;
                    } else {
                      var siToFp = new StdSiToFpOp(i64);
                      newBlock.AddOp(siToFp);
                      valueMap[castOp.Result] = siToFp.Result;
                    }
                  } else if (input is StdI32 i32) {
                    // i32 to float: need to convert i32 to i64 first, then to float
                    throw new InvalidOperationException("i32 to float conversion not yet implemented");
                  } else if (input is StdF64 f64) {
                    valueMap[castOp.Result] = f64;
                  } else if (input is StdF32 f32) {
                    // f32 to f64: widen
                    var promote = new StdF32ToF64Op(f32);
                    newBlock.AddOp(promote);
                    valueMap[castOp.Result] = promote.Result;
                  } else if (input is StdBool boolInput) {
                    // Bool to float: convert bool (0 or 1) to float
                    var asI64 = new StdI64(boolInput.Id);
                    var siToFp = new StdSiToFpOp(asI64);
                    newBlock.AddOp(siToFp);
                    valueMap[castOp.Result] = siToFp.Result;
                  } else if (input is StdPtr) {
                    throw new InvalidOperationException("Cannot cast pointer to float");
                  } else {
                    throw new InvalidOperationException($"Unsupported cast to float from: {input.GetType().Name}");
                  }
                  break;
                }
                case MaxonValueKind.Float32: {
                  if (input is StdI64 i64) {
                    var sourceIsUnsigned = castOp.SourceIsUnsigned;
                    if (sourceIsUnsigned) {
                      var uiToFp = new StdUiToFpF32Op(i64);
                      newBlock.AddOp(uiToFp);
                      valueMap[castOp.Result] = uiToFp.Result;
                    } else {
                      var siToFp = new StdSiToFpF32Op(i64);
                      newBlock.AddOp(siToFp);
                      valueMap[castOp.Result] = siToFp.Result;
                    }
                  } else if (input is StdF32 f32) {
                    valueMap[castOp.Result] = f32;
                  } else if (input is StdF64 f64) {
                    // f64 to f32: narrow
                    var narrow = new StdF64ToF32Op(f64);
                    newBlock.AddOp(narrow);
                    valueMap[castOp.Result] = narrow.Result;
                  } else if (input is StdBool boolInput) {
                    var asI64 = new StdI64(boolInput.Id);
                    var siToFp = new StdSiToFpF32Op(asI64);
                    newBlock.AddOp(siToFp);
                    valueMap[castOp.Result] = siToFp.Result;
                  } else {
                    throw new InvalidOperationException($"Unsupported cast to f32 from: {input.GetType().Name}");
                  }
                  break;
                }
                case MaxonValueKind.Short: {
                  // Cast to short: pass through (short uses i64 at standard level)
                  if (input is StdI64 i64) {
                    valueMap[castOp.Result] = i64;
                  } else {
                    throw new InvalidOperationException($"Unsupported cast to short from: {input.GetType().Name}");
                  }
                  break;
                }
                case MaxonValueKind.Bool:
                case MaxonValueKind.Struct:
                case MaxonValueKind.Enum:
                case MaxonValueKind.Function:
                case MaxonValueKind.TypeParameter:
                  throw new InvalidOperationException($"Unsupported cast target kind: {castOp.TargetKind}");
              }
              break;
            }
            case MaxonGlobalLoadOp globalLoad: {
              // Lazy static field: emit guard check and conditional init call
              if (globalLoad.LazyGuardName != null && globalLoad.LazyInitFuncName != null) {
                var guardLoad = new StdGlobalLoadI1Op(globalLoad.LazyGuardName);
                newBlock.AddOp(guardLoad);

                // Branch: if guard is true, skip init; if false, call init
                var initBlockLabel = $"__lazy_init_{globalLoad.Result.Id}";
                var mergeBlockLabel = $"__lazy_merge_{globalLoad.Result.Id}";

                newBlock.AddOp(new StdCondBrOp(guardLoad.Result, mergeBlockLabel, initBlockLabel));

                // The guard's "already initialized" edge is a FALL-THROUGH — the lowered form is
                // `je <init>` with no jump for the taken case — so the merge block must be the
                // physically next block.
                newBlock = newFunc.Body.AddBlock(mergeBlockLabel);

                // The init block is DEFERRED to the end of the function instead of being added
                // here. Added here it lands immediately after this merge block, and this merge
                // block is exactly where the NEXT lazy guard's cond_br gets emitted — so that
                // guard's fall-through would run into THIS init block, which ends by branching
                // back to THIS merge block. That is an infinite loop, and it needs two loads of
                // one lazy static to show up, which is why one load always looked fine.
                // Measured: `var s = "{V.k}x"` followed by `s.append("{V.k}y")` never terminated.
                pendingLazyInits.Add((initBlockLabel, globalLoad.LazyInitFuncName, mergeBlockLabel));
              }

              StandardOp loadOp = globalLoad.ValueKind switch {
                MaxonValueKind.Integer or MaxonValueKind.Enum => new StdGlobalLoadI64Op(globalLoad.GlobalName),
                MaxonValueKind.Float => new StdGlobalLoadF64Op(globalLoad.GlobalName),
                MaxonValueKind.Float32 => new StdGlobalLoadF32Op(globalLoad.GlobalName),
                MaxonValueKind.Bool => new StdGlobalLoadI1Op(globalLoad.GlobalName),
                MaxonValueKind.Byte => new StdGlobalLoadI8Op(globalLoad.GlobalName),
                MaxonValueKind.Short => new StdGlobalLoadI16Op(globalLoad.GlobalName),
                MaxonValueKind.Struct => new StdGlobalLoadI64Op(globalLoad.GlobalName),
                MaxonValueKind.Function or MaxonValueKind.TypeParameter or _ =>
                  throw new InvalidOperationException($"Cannot use {globalLoad.ValueKind} as global variable type"),
              };
              newBlock.AddOp(loadOp);
              valueMap[globalLoad.Result] = loadOp switch {
                StdGlobalLoadI64Op i64 => i64.Result,
                StdGlobalLoadF64Op f64 => f64.Result,
                StdGlobalLoadF32Op f32 => f32.Result,
                StdGlobalLoadI1Op i1 => i1.Result,
                StdGlobalLoadI8Op i8 => i8.Result,
                StdGlobalLoadI16Op i16 => i16.Result,
                _ => throw new InvalidOperationException()
              };
              // A managed slot yields a POINTER, and every reader of one (field access, a union's
              // tag/payload read, a call argument) resolves it through a temp variable rather than
              // as a bare SSA integer. Materializing that temp here is what makes the two kinds of
              // managed global indistinguishable downstream — without it a boxed union's tag read
              // fell through to its scalar-enum arm and passed the POINTER through as the tag.
              if (GlobalSlotHoldsManagedRecord(module, globalLoad.ValueKind, globalLoad.EnumTypeName)) {
                var recordTypeName = globalLoad.StructTypeName ?? globalLoad.EnumTypeName;
                var tempName = $"__global_{globalLoad.GlobalName}_{globalLoad.Result.Id}";
                temps.RegisterTemp(tempName, recordTypeName ?? "unknown", OwnershipFlags.Orphan);
                EmitStore(newBlock, valueMap[globalLoad.Result], tempName, varTypes);
                EmitIncref(newBlock, tempName, varTypes, scopeName: func.Name);
                valueMap[globalLoad.Result] = new StdHeapPtr(globalLoad.Result.Id, recordTypeName ?? "unknown", tempName);
                if (recordTypeName != null) {
                  varNameToStructType[tempName] = recordTypeName;
                }
              }
              break;
            }
            case MaxonGlobalStoreOp globalStore: {
              if (GlobalSlotHoldsManagedRecord(module, globalStore.ValueKind, globalStore.EnumTypeName)) {
                // Resolve the new heap pointer -- check StdHeapPtr before StdI64
                // since StdHeapPtr extends StdI64 and needs a load from its temp variable.
                // An StdHeapPtr carrying a VarName is a HANDLE, not an SSA value: its Id is the
                // producing Maxon op's id, which names nothing in the Std function. Using it as
                // an operand is what emitted `global_store @hold %1` against `mm_alloc`'s size
                // argument, and — where no Std id happened to collide — reached the register
                // allocator as `E9001: value %N has no register and no stack home`.
                StdI64 newHeapPtr;
                if (valueMap.TryGetValue(globalStore.Value, out var mv) && mv is StdHeapPtr srcNameHp) {
                  newHeapPtr = (StdI64)EmitLoad(newBlock, srcNameHp.VarName!, varTypes);
                } else if (mv is StdI64 i64Val) {
                  newHeapPtr = i64Val;
                } else {
                  throw new InvalidOperationException($"Cannot store managed value to global '{globalStore.GlobalName}': no heap tracking info");
                }

                bool isModuleInit = func.Name == "__module_init";
                if (!isModuleInit) {
                  // Decref old global value before storing new one (may be null if not yet assigned).
                  var oldGlobalLoad = new StdGlobalLoadI64Op(globalStore.GlobalName);
                  newBlock.AddOp(oldGlobalLoad);
                  EmitDecrefValueIfNonnull(newBlock, oldGlobalLoad.Result, scopeName: func.Name);
                }

                // Incref the new value — the global holds a reference
                EmitIncrefValue(newBlock, newHeapPtr, scopeName: func.Name);
                newBlock.AddOp(new StdGlobalStoreI64Op(newHeapPtr, globalStore.GlobalName));
              } else {
                var mappedValue = valueMap[globalStore.Value];
                var storeOp = globalStore.ValueKind switch {
                  MaxonValueKind.Integer or MaxonValueKind.Enum =>
                    (StandardOp)new StdGlobalStoreI64Op((StdI64)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Float =>
                    new StdGlobalStoreF64Op((StdF64)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Float32 =>
                    new StdGlobalStoreF32Op((StdF32)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Bool =>
                    new StdGlobalStoreI1Op((StdBool)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Byte =>
                    new StdGlobalStoreI8Op((StdI64)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Short =>
                    new StdGlobalStoreI16Op((StdI64)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Struct or MaxonValueKind.Function or MaxonValueKind.TypeParameter or _ =>
                    throw new InvalidOperationException($"Cannot use {globalStore.ValueKind} as global variable type"),
                };
                newBlock.AddOp(storeOp);
              }
              break;
            }
            case MaxonTryCallOp tryCallOp:
              LowerTryCall(tryCallOp, funcLookup, newFunc, ref newBlock, valueMap, varTypes, module.TypeDefs, temps,
                fnEnvVarNames: fnEnvVarNames, fnEnvDirectValues: fnEnvDirectValues);
              if (isStructInstanceMethod) {
                selfFieldCache.Clear();
                ReloadSelfFieldLocals(selfStructType!, newBlock, varTypes, selfFieldTempVars);
              }
              break;
            case MaxonAsyncCallOp asyncCallOp:
              LowerAsyncCall(asyncCallOp, newBlock, valueMap, varTypes);
              break;
            case MaxonAwaitOp awaitOp:
              LowerAwait(awaitOp, newBlock, valueMap);
              break;
            case MaxonTryAwaitOp tryAwaitOp:
              LowerTryAwait(tryAwaitOp, newBlock, valueMap);
              break;
            case MaxonCancelPromiseOp cancelOp:
              LowerCancelPromise(cancelOp, newBlock, valueMap);
              break;
            case MaxonCallOp callOp:
              // A call to a constant empty-container factory whose result is never written through
              // needs neither the call nor an allocation: it IS the shared immortal record. Nothing
              // else about the call has to happen — no argument to flatten (the factory takes none),
              // and no self-field cache to invalidate below, because no call is made.
              if (callOp.Result != null
                  && module.ConstantEmptyContainerFactories.TryGetValue(callOp.Callee, out var emptyContainerInfo)
                  && IsStaticEligibleLiteral(callOp.Result.Id)) {
                valueMap[callOp.Result] = EmitStaticEmptyContainer(
                  emptyContainerInfo, callOp.Result.Id, newBlock, varTypes, result, temps);
                break;
              }
              if (TryLowerPrimitiveMethod(callOp, newBlock, valueMap)) break;
              LowerCall(callOp, funcLookup, newFunc, ref newBlock, valueMap, varTypes, module.TypeDefs,
                fnEnvVarNames: fnEnvVarNames, fnEnvDirectValues: fnEnvDirectValues, temps: temps);
              // Method calls may mutate self-fields (e.g. grow() reallocates arrays),
              // so cached self-field loads must be invalidated and struct-typed
              // field locals must be reloaded from the self pointer
              if (isStructInstanceMethod) {
                selfFieldCache.Clear();
                ReloadSelfFieldLocals(selfStructType!, newBlock, varTypes, selfFieldTempVars);
              }
              // After a call that passes variables by reference, reload those variables
              // so subsequent uses see the mutated values instead of stale SSA values.
              //
              // The reloaded SSA value lives in this block, so writing it into the
              // shared valueMap would shadow the original dominating definition for
              // sibling blocks. The block-scoped snapshot/restore around the loop
              // body reverts these entries once we leave the block, so siblings still
              // see the dominating definition.
              if (callOp.ArgVarNames != null
                  && funcLookup.TryGetValue(callOp.Callee, out var calleeForReload)
                  && calleeForReload.ReassignedParams != null) {
                for (int ai = 0; ai < callOp.Args.Count && ai < callOp.ArgVarNames.Count; ai++) {
                  var argVarName = callOp.ArgVarNames[ai];
                  if (argVarName == null) continue;
                  if (ai >= calleeForReload.ParamNames.Count) continue;
                  var calleeParamName = calleeForReload.ParamNames[ai];
                  if (!calleeForReload.ReassignedParams.Contains(calleeParamName)) continue;
                  if (!varTypes.ContainsKey(argVarName)) continue;
                  // If we forwarded the ref pointer, the callee modified the original location,
                  // not our local copy. Reload the local from the ref pointer first.
                  if (_refParamPtrVars != null && _refParamPtrVars.TryGetValue(argVarName, out var refPtrForReload)) {
                    var refPtr = (StdI64)EmitLoad(newBlock, refPtrForReload, varTypes);
                    var varType = varTypes.TryGetValue(argVarName, out var vt) ? VarTypeToIrType(vt) : IrType.I64;
                    var loadIndirect = new StdLoadIndirectOp(refPtr, 0, varType);
                    newBlock.AddOp(loadIndirect);
                    EmitStore(newBlock, loadIndirect.Result, argVarName, varTypes);
                  }
                  var reloaded = EmitLoad(newBlock, argVarName, varTypes);
                  valueMap[callOp.Args[ai]] = reloaded;
                }
              }
              break;
            case MaxonFunctionRefOp fnRefOp:
              LowerFunctionRef(fnRefOp, newBlock, valueMap);
              break;
            case MaxonClosureCreateOp closureCreateOp:
              LowerClosureCreate(closureCreateOp, newBlock, valueMap, varTypes, fnEnvVarNames, varNameToStructType, temps);
              break;
            case MaxonClosureEnvLoadOp envLoadOp:
              LowerClosureEnvLoad(envLoadOp, newBlock, valueMap, varTypes, temps);
              break;
            case MaxonFunctionParamOp fnParamOp:
              LowerFunctionParam(fnParamOp.Index, fnParamOp.Name, fnParamOp.Result, newBlock, valueMap, varTypes, fnEnvVarNames, fnEnvDirectValues, paramFlatIndex);
              break;
            case MaxonFunctionVarRefOp fnVarRefOp:
              LowerFunctionVarRef(fnVarRefOp, newBlock, valueMap, varTypes, fnEnvVarNames);
              break;
            case MaxonIndirectCallOp indirectCallOp:
              LowerIndirectCall(indirectCallOp, newBlock, valueMap, varTypes, module.TypeDefs, fnEnvVarNames, fnEnvDirectValues, temps);
              if (isStructInstanceMethod) {
                selfFieldCache.Clear();
                ReloadSelfFieldLocals(selfStructType!, newBlock, varTypes, selfFieldTempVars);
              }
              break;
            case MaxonReturnOp retOp: {
              LowerReturn(retOp, retStructType, newBlock, valueMap, varTypes, module.TypeDefs, func.Name, temps, func.ReturnsSelf,
                usesValueTupleReturn: module.ValueTupleReturnFunctions.Contains(func.Name));
              break;
            }
            case MaxonThrowOp throwOp: {
              LowerThrow(throwOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            }
            case MaxonManagedMemGetOp memGetOp:
              // Reachable via ForLoopIteratorElisionPass (which emits the dedicated op
              // with IsBoundsCheckSafe = true to skip the throw on iterator-driven
              // accesses). The throwing variant is dispatched as MaxonTryCallOp through
              // TryLowerManagedMemBuiltin instead.
              LowerManagedMemGet(memGetOp, newFunc, ref newBlock, valueMap, varTypes, temps);
              if (valueMap[memGetOp.Result] is StdHeapPtr memGetHp) {
                valueMap[memGetOp.Result] = new StdHeapPtr(memGetOp.Result.Id, memGetHp.TypeName, memGetHp.VarName!);
              }
              break;
            case MaxonManagedMemClearOp memClearOp:
              LowerManagedMemClear(memClearOp, newBlock, valueMap, varTypes);
              break;
            case MaxonUcdByteLoadOp ucdByteOp:
              LowerUcdByteLoad(ucdByteOp, newBlock, valueMap, result);
              break;
            case MaxonUcdI64LoadOp ucdI64Op:
              LowerUcdI64Load(ucdI64Op, newBlock, valueMap, result);
              break;
            case MaxonByteRangePanicOp byteRangePanicOp:
              LowerByteRangePanic(byteRangePanicOp, newBlock, valueMap);
              break;
            case MaxonCStringToManagedOp fromCStringOp:
              LowerCStringToManaged(fromCStringOp, newBlock, valueMap, varTypes, temps,
                inlineTargets.GetValueOrDefault(fromCStringOp.Result.Id));
              if (valueMap[fromCStringOp.Result] is StdHeapPtr fromCStringHp) {
                valueMap[fromCStringOp.Result] = new StdHeapPtr(fromCStringOp.Result.Id, fromCStringHp.TypeName, fromCStringHp.VarName!);
              }
              break;
            case MaxonManagedToCStringOp toCStringOp:
              LowerManagedToCString(toCStringOp, newFunc, ref newBlock, valueMap, varTypes);
              break;
            case MaxonManagedWriteStdoutOp managedWriteStdoutOp:
              LowerManagedWriteStdout(managedWriteStdoutOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedWriteStderrOp managedWriteStderrOp:
              LowerManagedWriteStderr(managedWriteStderrOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedReadStdinOp managedReadStdinOp:
              LowerManagedReadStdin(managedReadStdinOp, newFunc, ref newBlock, valueMap, varTypes, temps);
              break;
            case MaxonPanicOp panicOp:
              LowerPanic(panicOp, newBlock, result);
              break;
            case MaxonPanicDynamicOp panicDynOp:
              LowerPanicDynamic(panicDynOp, newBlock, valueMap, varTypes);
              break;
            case MaxonStringLiteralOp stringLitOp:
              LowerStringLiteral(stringLitOp, newBlock, valueMap, varTypes, result, temps,
                inlineTargets.GetValueOrDefault(stringLitOp.Result.Id));
              break;
            case MaxonByteStringLiteralOp byteStringLitOp:
              LowerByteStringLiteral(byteStringLitOp, newBlock, valueMap, varTypes, result, temps,
                inlineTargets.GetValueOrDefault(byteStringLitOp.Result.Id));
              break;
            case MaxonCharLiteralOp charLitOp:
              LowerCharLiteral(charLitOp, newBlock, valueMap, varTypes, result, temps,
                inlineTargets.GetValueOrDefault(charLitOp.Result.Id));
              break;
            case MaxonStringInterpOp interpOp:
              LowerStringInterp(interpOp, newBlock, valueMap, varTypes, result, temps,
                inlineTargets.GetValueOrDefault(interpOp.Result.Id));
              break;
            case MaxonManagedMemAppendOp memAppendOp:
              LowerManagedMemAppend(memAppendOp, newFunc, ref newBlock, valueMap, varTypes);
              break;
            case MaxonMakeCharFromBytesOp makeCharOp:
              LowerMakeCharFromBytes(makeCharOp, newBlock, valueMap, varTypes, temps);
              break;
            // __ManagedMemoryCursor operations (non-throwing — throwing ops go through MaxonCallOp)
            case MaxonCursorCurrentOp cursorCurrentOp:
              LowerCursorCurrent(cursorCurrentOp, newBlock, valueMap, varTypes, temps);
              break;
            case MaxonCursorIndexOp cursorIndexOp:
              LowerCursorIndex(cursorIndexOp, newBlock, valueMap, varTypes);
              break;
            // __DebugStream: the builtin that lets user Maxon source emit into the ring.
            // Every emitting op below is a NO-OP when DebugStream is off at compile time.
            case MaxonDebugStreamEnabledOp dsEnabledOp:
              LowerDebugStreamEnabled(dsEnabledOp, newBlock, valueMap);
              break;
            case MaxonDebugStreamNameIdOp dsNameIdOp:
              LowerDebugStreamNameId(dsNameIdOp, newBlock, valueMap);
              break;
            case MaxonDebugStreamPhaseOp dsPhaseOp:
              LowerDebugStreamPhase(dsPhaseOp, newBlock, valueMap);
              break;
            case MaxonDebugStreamEventOp dsEventOp:
              LowerDebugStreamEvent(dsEventOp, newBlock, valueMap);
              break;
            case MaxonDebugStreamTextOp dsTextOp:
              LowerDebugStreamText(dsTextOp, newBlock, valueMap, varTypes);
              break;
            case MaxonCallRuntimeOp callRtOp: {
              var stdArgs = callRtOp.Args.Select(a => {
                if (valueMap.TryGetValue(a, out var mapped)) {
                  if (mapped is StdHeapPtr hp && hp.VarName != null) {
                    var typeName = hp.TypeName;
                    // Load buffer from managed struct via heap pointer indirection. A fused
                    // String/Character/Array IS its own __ManagedMemory (buffer at offset 0), so it
                    // is handled identically to a bare __ManagedMemory here.
                    if (TypeAliasInfo.IsManagedMemoryType(typeName, module.TypeAliasSources)
                        || IsFusedManagedWrapper(typeName)) {
                      // hp.VarName IS the __ManagedMemory heap pointer, buffer at offset 0
                      return (StdValue)(StdI64)EmitStructFieldLoad(newBlock, hp.VarName, ManagedFieldBuffer, IrType.I64, varTypes);
                    } else if (typeName == "__ManagedFile") {
                      // Pass the __ManagedFile heap pointer itself; runtime (maxon_file_close)
                      // reads _handle at offset 0 and zeros it before submitting close.
                      return (StdValue)(StdI64)EmitLoad(newBlock, hp.VarName, varTypes);
                    } else {
                      throw new InvalidOperationException(
                        $"MaxonCallRuntimeOp struct arg has unexpected type '{typeName}' -- " +
                        "only __ManagedMemory struct args are supported (extract fields before passing to runtime calls)");
                    }
                  }
                  return (StdValue)(StdI64)mapped;
                }
                throw new InvalidOperationException($"MaxonCallRuntimeOp arg {a} not found in valueMap");
              }).ToList();
              // When tracing, mm_free/mm_raw_free take 2 params (ptr, scope) — add NULL scope if caller only passes ptr
              if (Compiler.MmTrace && (callRtOp.FunctionName == "mm_free" || callRtOp.FunctionName == "mm_raw_free") && stdArgs.Count == 1) {
                var nullScope = new StdConstI64Op(0);
                newBlock.AddOp(nullScope);
                stdArgs.Add(nullScope.Result);
              }
              if (callRtOp.Result != null) {
                var rtResult = new StdI64(IrContext.Current.NextStdId());
                newBlock.AddOp(new StdCallRuntimeOp(callRtOp.FunctionName, stdArgs, rtResult));
                valueMap[callRtOp.Result] = rtResult;
              } else {
                newBlock.AddOp(new StdCallRuntimeOp(callRtOp.FunctionName, stdArgs, null));
              }
              break;
            }
            // ManagedList (doubly-linked list) operations
            case MaxonManagedListCreateOp managedListCreateOp:
              LowerManagedListCreate(managedListCreateOp, newBlock, valueMap, varTypes, temps,
                inlineTargets.GetValueOrDefault(managedListCreateOp.Result.Id));
              break;
            case MaxonManagedListInsertValueOp managedListInsertOp:
              LowerManagedListInsertValue(managedListInsertOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            case MaxonManagedListInsertRelativeValueOp managedListInsertRelOp:
              LowerManagedListInsertRelativeValue(managedListInsertRelOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            case MaxonManagedListDetachOp managedListDetachOp:
              LowerManagedListDetach(managedListDetachOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedListRemoveOp managedListRemoveOp:
              LowerManagedListRemove(managedListRemoveOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            case MaxonManagedListCountOp managedListCountOp:
              LowerManagedListCount(managedListCountOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedListNodeValueOp managedListNodeValueOp:
              LowerManagedListNodeValue(managedListNodeValueOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            case MaxonManagedListNodeSetValueOp managedListNodeSetValueOp:
              LowerManagedListNodeSetValue(managedListNodeSetValueOp, newBlock, valueMap, varTypes, module.TypeDefs);
              break;
            case MaxonManagedListClearOp managedListClearOp:
              LowerManagedListClear(managedListClearOp, newBlock, valueMap, varTypes, module.TypeDefs);
              break;
            case MaxonManagedListCursorResetOp cursorResetOp:
              LowerManagedListCursorReset(cursorResetOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedListCursorValueOp cursorValueOp:
              LowerManagedListCursorValue(cursorValueOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            case MaxonManagedListHeadPtrOp headPtrOp:
              LowerManagedListHeadPtr(headPtrOp, newBlock, valueMap, varTypes, temps);
              break;
            case MaxonManagedListNodePtrNextOp nodePtrNextOp:
              LowerManagedListNodePtrNext(nodePtrNextOp, newBlock, valueMap, varTypes, temps);
              break;
            case MaxonManagedListNodePtrValueOp nodePtrValueOp:
              LowerManagedListNodePtrValue(nodePtrValueOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            default:
              throw new InvalidOperationException($"No MaxonToStandard conversion for: {op.GetType().Name} ({op.Mnemonic})");
          }
        }

        // `spanMarkBlock`, not `newBlock`: the LAST op lowered may itself have switched blocks, and
        // the marks still in hand were measured before it did.
        if (spanMarks != null) DebugSpanFlow.AssignRange(newFunc, spanMarkBlock, spanMarks);

        // Restore entries that existed before this block. Entries created inside
        // this block stay (their key is still the unique definition). Entries that
        // existed before but were overwritten get reverted so sibling blocks see
        // the dominating definition.
        foreach (var (key, originalValue) in valueMapSnapshot) {
          valueMap[key] = originalValue;
        }
        foreach (var (key, originalValue) in varNameToStructPrefixSnapshot) {
          varNameToStructPrefix[key] = originalValue;
        }
        foreach (var (key, originalValue) in selfFieldTempVarsSnapshot) {
          selfFieldTempVars[key] = originalValue;
        }
      }

      // The deferred lazy-static init blocks. Each is entered only by an explicit branch from its
      // guard and left only by an explicit branch to its merge, so it has no fall-through edge in
      // either direction and the end of the function is a safe home for it. Putting them all here
      // is what keeps every guard physically adjacent to its own merge block.
      foreach (var (initLabel, initFuncName, mergeLabel) in pendingLazyInits) {
        var initBlock = newFunc.Body.AddBlock(initLabel);
        initBlock.AddOp(new StdCallOp(initFuncName, []));
        initBlock.AddOp(new StdBrOp(mergeLabel));
      }

      // Zero-initialize stack slots for all vars that scope_end will decref,
      // so paths that skip the scope (e.g. untaken if-branches) see NULL.
      var allScopeVars = new HashSet<string>();
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonScopeEndOp seo) {
            foreach (var v in seo.VarsToClean)
              allScopeVars.Add(v);
          }
        }
      }
      // Zero-initialize all managed scope vars and orphan temps in the entry block
      // so that unreached conditional paths see NULL instead of garbage
      var orphanTempNames = temps.OrphanTemps.ToHashSet();
      foreach (var orphan in orphanTempNames)
        allScopeVars.Add(orphan);
      if (allScopeVars.Count > 0 && newFunc.Body.Blocks.Count > 0) {
        var entryBlock = newFunc.Body.Blocks[0];
        int insertIdx = 0;
        foreach (var v in allScopeVars) {
          if (_structParamNames != null && _structParamNames.Contains(v)) continue;
          if (IsSelfField(isStructInstanceMethod, selfStructType, v)) continue;
          if (!varNameToStructType.ContainsKey(v) && !temps.IsTempManaged(v)) continue;  // only managed vars need zeroing
          var zeroOp = new StdConstI64Op(0);
          var storeOp = new StdStoreI64Op(zeroOp.Result, v);
          entryBlock.Operations.Insert(insertIdx, zeroOp);
          insertIdx++;
          entryBlock.Operations.Insert(insertIdx, storeOp);
          insertIdx++;
        }
      }

      // Attach the captured local-type table (debug info only). Transfer ownership: the ThreadStatic
      // cursor is cleared after the loop so the post-loop synthetic-function generators (which call
      // EmitStore) cannot mutate a function's already-attached map.
      if (debugLocalTypes != null) newFunc.SetLocalSourceTypes(debugLocalTypes);

      result.AddFunction(newFunc);
      } catch (CompileError) {
        // CompileError carries a typed code and source position; never wrap
        // it — propagate so the top-level handler reports the user-facing
        // diagnostic instead of an InvalidOperationException stack dump.
        throw;
      } catch (Exception ex) {
        throw new InvalidOperationException($"Lowering function '{func.Name}' failed: {ex.Message}", ex);
      }
    }

    // The per-function debug-local cursors must not point at the last function's (now attached) map
    // while the synthetic global-cleanup/destructor functions below lower through EmitStore.
    _debugLocalTypes = null;
    _debugSealedLocalNames = null;

    // Reset the per-function lowering mode so the post-loop helpers and any subsequent
    // pass (StandardToX86 etc.) mint user-side ids by default.
    IrContext.Current.StdlibLoweringMode = false;

    // Materialize the shared immortal records for every static-eligible literal site into
    // __module_init (creating it if there were no deferred globals). Must run after the
    // function loop so it has seen every eligible literal.
    MaterializeStaticLiteralRecords(result);

    // Generate __maxon_global_cleanup to release module-level struct variables at exit
    GenerateGlobalCleanup(module, result);

    // Generate per-element-type destructor functions for containers whose elements
    // Generate per-type destructor functions (called by mm_decref when rc reaches 0)
    GenerateTypeDestructors(result);

    // Build tag table for mm-trace (maps tag_index -> symdata label)
    EmitTagTable(result);

    // Build the interned-name table the `__DebugStream` Log events index into (MXDS_STRS).
    EmitDebugStreamNameTable(result);

    // The coverage points the parser minted travel with the module: the emitter sizes
    // `__cov_image` from their count and the sidecar's coverage table is written from them.
    result.CoveragePoints = module.CoveragePoints;
    result.CoverageDataPath = module.CoverageDataPath;

    return result;
  }

  /// <summary>
  /// True when a global's `.data` slot holds a POINTER to a refcounted record rather than a
  /// scalar written inline — so the slot OWNS its occupant, and the load must retain, the store
  /// must release the old occupant before retaining the new one, and process exit must decref
  /// what is left. Struct-kinded globals always do; an Enum-kinded one does exactly when its
  /// union is heap-allocated (some case carries a payload), because a payload-free union and a
  /// plain `enum` are bare discriminants. Three readers reach one slot from three directions and
  /// they must never disagree — a union global that was INITIALIZED as an ordinal and ASSIGNED as
  /// a pointer is what this single predicate exists to prevent.
  /// </summary>
  private static bool GlobalSlotHoldsManagedRecord(IrModule<MaxonOp> module, MaxonValueKind kind, string? enumTypeName) =>
    kind switch {
      MaxonValueKind.Struct => true,
      MaxonValueKind.Enum => enumTypeName != null
        && module.TypeDefs.TryGetValue(enumTypeName, out var enumType)
        && enumType is IrEnumType { IsHeapAllocated: true },
      _ => false
    };

  private static void GenerateGlobalCleanup(IrModule<MaxonOp> module, IrModule<StandardOp> result) {
    // A slot is released at exit exactly when it owns its occupant, and the type it names must be
    // known — an untyped struct global has no record to release.
    bool OwnsOccupant(GlobalVarMetadata meta) =>
      GlobalSlotHoldsManagedRecord(module, meta.Kind, meta.EnumTypeName)
      && (meta.TypeName ?? meta.EnumTypeName) != null;

    if (!module.GlobalVarInfos.Any(kv => OwnsOccupant(kv.Value))) return;

    var cleanupFunc = new IrFunction<StandardOp>("__maxon_global_cleanup", [], [], null, null);
    var block = cleanupFunc.Body.AddBlock("entry");

    foreach (var (varName, meta) in module.GlobalVarInfos) {
      if (!OwnsOccupant(meta)) continue;

      if (meta.IsLazy) {
        // Only decref lazy statics that were actually initialized
        var guardName = $"{varName}.__initialized";
        var guardLoad = new StdGlobalLoadI1Op(guardName);
        block.AddOp(guardLoad);
        var skipLabel = $"__cleanup_skip_{varName.Replace('.', '_')}";
        var cleanupLabel = $"__cleanup_{varName.Replace('.', '_')}";
        block.AddOp(new StdCondBrOp(guardLoad.Result, cleanupLabel, skipLabel));
        block = cleanupFunc.Body.AddBlock(cleanupLabel);
        var globalLoad = new StdGlobalLoadI64Op(varName);
        block.AddOp(globalLoad);
        EmitDecrefValueIfNonnull(block, globalLoad.Result, scopeName: "__maxon_global_cleanup");
        block.AddOp(new StdBrOp(skipLabel));
        block = cleanupFunc.Body.AddBlock(skipLabel);
      } else {
        var globalLoad = new StdGlobalLoadI64Op(varName);
        block.AddOp(globalLoad);
        EmitDecrefValueIfNonnull(block, globalLoad.Result, scopeName: "__maxon_global_cleanup");
      }
    }

    block.AddOp(new StdReturnOp(null));
    result.AddFunction(cleanupFunc);
  }

  /// <summary>
  /// Checks if a type that uses an Element type parameter has a resolved, heap-allocated
  /// Element type. First checks the type's own TypeParams, then falls back to searching
  /// wrapper types that contain this type as a field.
  /// </summary>
  /// Resolves an IrType through TypeDefs to get the canonical definition,
  /// catching stale placeholders (e.g., IrStructType registered for a ranged primitive).
  private static IrType ResolveCanonicalType(IrType type) {
    return _resultModule!.TypeDefs.TryGetValue(type.Name, out var canonical) ? canonical : type;
  }

  /// The Element type parameters BOUND to a managed container name, in the order the destructor
  /// decision consults them: the alias's own binding first (may be resolved directly), then the
  /// resolved struct's. An UNBOUND parameter — still a type variable — is not a candidate: it
  /// names no element, so it cannot decide whether a destructor must decref what it holds.
  ///
  /// Read by HasManagedElementType, which asks whether any candidate is heap-allocated, and by
  /// RequireElementBearingListName, which asks whether there is a candidate AT ALL. Those two
  /// questions must be asked of the SAME candidates: a name the refusal admits but this lookup
  /// cannot bind is exactly the W154 defect — a silent primitive clear that leaks every element.
  private static IEnumerable<IrType> BoundElementTypes(string typeName, IrStructType resolved) {
    var typeAliasSources = _resultModule!.TypeAliasSources;

    if (typeAliasSources.TryGetValue(typeName, out var aliasInfo)
        && aliasInfo.TypeParams != null
        && aliasInfo.TypeParams.TryGetValue("Element", out var aliasElemType)
        && aliasElemType is not IrTypeParameterType)
      yield return aliasElemType;

    if (resolved.TypeParams.TryGetValue("Element", out var selfElemType)
        && selfElemType is not IrTypeParameterType)
      yield return selfElemType;
  }

  /// Refuses an allocation whose DECLARED type name no type definition resolves.
  ///
  /// EmitAlloc's `typeName` is the declared type the allocated block carries, and it is the sole
  /// input from which that block's destructor is chosen. An unresolvable name therefore does not
  /// mean "this type needs no destructor" — it means the decision could not be TAKEN, and the
  /// header would carry a null destructor whose only symptom is a leak counted much later, with
  /// nothing pointing back at the name that lost its definition. That is the W154 defect one level
  /// up: a pass dropped the resolvable spelling between the parser and here.
  ///
  /// What keeps this refusal honest is that a SYNTHETIC allocation — one the compiler mints for
  /// itself, with no declared type at all, such as a closure environment — does not come through
  /// here. It passes `typeName: null` and names itself with EmitAlloc's `tag`, which is the
  /// mm-trace label and carries no destructor claim. So reaching this refusal means a name was
  /// CLAIMED to be a declared type and is not one.
  private static IrType RequireDeclaredAllocationType(string typeName) {
    if (!_resultModule!.TypeDefs.TryGetValue(typeName, out var typeDef))
      throw new InvalidOperationException(
        $"an allocation reached lowering carrying the declared type name '{typeName}', which no "
        + "type definition resolves, so its destructor cannot be chosen and the block would carry "
        + "none at all. The resolvable spelling was dropped between the parser and here. An "
        + "allocation that has no declared type must name itself with EmitAlloc's tag instead.");
    return typeDef;
  }

  /// Refuses a managed-list allocation whose type name cannot decide the list's destructor.
  ///
  /// The destructor is chosen from the name the ALLOCATION is tagged with, so a name that has
  /// lost its element is not a cosmetic defect — it is a leak the run reports as exit 101 and
  /// nothing else explains. Three ways the name can arrive unusable, each a pass having dropped
  /// the element-bearing spelling between the parser and here, and each silent without this:
  ///   - Element still unbound (the bare `__ManagedList`, or an alias no pass substituted):
  ///     HasManagedElementType reads false, maxon_managed_list_clear frees the nodes and every
  ///     element leaks — the W154 defect itself. The bare spelling is named by its own arm, ahead
  ///     of the general one that also covers it, because it is the only name the compiler mints
  ///     for itself and so is the one case whose message can say exactly what went wrong;
  ///   - no TypeDefs entry: refused by RequireDeclaredAllocationType, which every allocation
  ///     answers to and which this check therefore does not re-decide;
  ///   - not a __ManagedList at all: the generic struct-field destructor runs over a chain
  ///     header, reading its head/tail/count words as if they were fields — or, when the
  ///     definition is not a struct, no destructor is emitted at all.
  private static void RequireElementBearingListName(string typeName) {
    if (typeName == BareManagedListTypeName)
      throw ManagedListNameRefusal(typeName,
        "it is the un-parameterized builtin spelling, which binds no Element");

    var reason =
      RequireDeclaredAllocationType(typeName) is not IrStructType structType
        ? "its type definition is not a struct, so its allocation would carry no destructor at all"
      : !TypeAliasInfo.IsManagedListType(typeName, _resultModule!.TypeAliasSources)
        ? "it does not name a __ManagedList, so a chain header would get a struct destructor"
      : !BoundElementTypes(typeName, ResolveStructType(structType, _resultModule!.TypeDefs)).Any()
        ? "its Element is still an unbound type parameter"
      : null;

    if (reason != null) throw ManagedListNameRefusal(typeName, reason);
  }

  private static InvalidOperationException ManagedListNameRefusal(string typeName, string reason) =>
    new($"managed_list_create reached lowering with the type name '{typeName}', from which this "
      + $"list's destructor cannot be chosen: {reason}. The element-bearing spelling was dropped "
      + "between the parser and here.");

  private static bool HasManagedElementType(string typeName, IrStructType resolved) {
    var typeAliasSources = _resultModule!.TypeAliasSources;

    foreach (var elemType in BoundElementTypes(typeName, resolved)) {
      if (ResolveCanonicalType(elemType).IsHeapAllocated) return true;
    }

    // Fall back: find the managed memory alias's element type from alias sources
    // (e.g., ByteMemory -> __ManagedMemory with Byte -> Element = Byte)
    if (typeAliasSources.TryGetValue(typeName, out var mmAlias) && mmAlias.TypeParams != null) {
      foreach (var (_, paramType) in mmAlias.TypeParams) {
        if (paramType is not IrTypeParameterType) {
          if (ResolveCanonicalType(paramType).IsHeapAllocated) return true;
        }
      }
    }
    return false;
  }

  /// <summary>
  /// True when a managed-memory-shaped type's Element is genuinely heap-allocated, so its buffer
  /// holds pointers that must be mm_decref'd before the buffer is freed. Pairs the element-type
  /// probe (HasManagedElementType) with a resolve-through-TypeDefs cross-check that rejects stale
  /// non-heap placeholders (e.g. RegInt registered as an IrStructType rather than a ranged prim).
  /// Shared by the bare-__ManagedMemory destructor and the fused Array/Vector destructor.
  /// </summary>
  private static bool ComputeNeedsManagedElementCleanup(string typeName, IrStructType resolved) {
    if (!HasManagedElementType(typeName, resolved)) return false;
    var typeAliasSources = _resultModule!.TypeAliasSources;
    IrType? elemType = null;
    if (typeAliasSources.TryGetValue(typeName, out var mmInfo) && mmInfo.TypeParams != null
        && mmInfo.TypeParams.TryGetValue("Element", out var et))
      elemType = et;
    if (elemType == null && resolved.TypeParams.TryGetValue("Element", out var selfEt))
      elemType = selfEt;
    return elemType == null || ResolveCanonicalType(elemType).IsHeapAllocated;
  }

  /// <summary>
  /// Registers a type for destructor generation. Looks up the type in typeDefs and
  /// records its managed fields so a destructor function can be synthesized.
  /// </summary>
  private static void RegisterTypeForDestructor(string typeName) {
    var typeDefs = _resultModule!.TypeDefs;
    var typeAliasSources = _resultModule!.TypeAliasSources;
    _destructorRequests ??= [];
    if (_destructorRequests.ContainsKey(typeName)) return;

    var typeDef = RequireDeclaredAllocationType(typeName);

    // These types have hand-written runtime destructors — skip synthesis
    // to avoid emitting a duplicate (no-op) synthesized destructor that
    // would shadow the real one.
    if (typeName is "__ManagedSocket" or "__ManagedDirectory" or "__ManagedFile") return;

    if (typeDef is IrStructType structType) {
      // Envelope collapse: a fused String/Character IS its own __ManagedMemory (buffer@0,
      // capacity@16, parent_ptr@32). Its destructor is the __ManagedMemory raw-buffer dispatch
      // run on `self`: capacity==-1 → decref parent; ==-2 → nothing; >=0 → free buffer. The
      // `managed` field must NOT be treated as a heap pointer (offset 0 is the buffer, not a ptr).
      if (structType.ConformingInterfaces.Contains("BuiltinStringLiteral")
          || structType.ConformingInterfaces.Contains("BuiltinCharLiteral")) {
        _destructorRequests[typeName] = new DestructorRequest(typeName,
          [(ManagedFieldBuffer, "raw_buffer", true)],
          NeedsManagedElementCleanup: false);
        return;
      }

      // Envelope collapse: a fused Array/Vector IS its own __ManagedMemory too, so its destructor
      // is the SAME raw-buffer dispatch on `self`. Unlike String (raw bytes), an Array's buffer may
      // hold heap POINTERS (Array with String), which must be mm_decref'd before the buffer is
      // freed — so NeedsManagedElementCleanup is COMPUTED from the Element type, not hardcoded false.
      if (structType.ConformingInterfaces.Contains("BuiltinArrayLiteral")) {
        var resolvedArr = ResolveStructType(structType, typeDefs);
        _destructorRequests[typeName] = new DestructorRequest(typeName,
          [(ManagedFieldBuffer, "raw_buffer", true)],
          NeedsManagedElementCleanup: ComputeNeedsManagedElementCleanup(typeName, resolvedArr));
        return;
      }

      var resolved = ResolveStructType(structType, typeDefs);
      bool isManagedMemory = TypeAliasInfo.IsManagedMemoryType(typeName, typeAliasSources);
      bool isManagedList = TypeAliasInfo.IsManagedListType(typeName, typeAliasSources);

      // __ManagedList types: destructor calls managed_list_clear or managed_list_clear_managed to walk nodes.
      // managed_list_clear_managed decrefs each node's value before decrefing the node itself.
      if (isManagedList) {
        bool hasManagedElems = HasManagedElementType(typeName, resolved);
        var clearFunc = hasManagedElems ? "maxon_managed_list_clear_managed" : "maxon_managed_list_clear";
        _destructorRequests[typeName] = new DestructorRequest(typeName, [], ManagedListClearFunc: clearFunc);
        return;
      }

      // Check if this __ManagedMemory type holds heap-allocated elements (needs per-element decref).
      bool needsManagedElementCleanup = isManagedMemory && ComputeNeedsManagedElementCleanup(typeName, resolved);

      bool isManagedCursor = TypeAliasInfo.IsManagedCursorType(typeName, typeAliasSources);

      var managedFields = new List<(int Offset, string FieldTypeName, bool IsRawBuffer)>();
      foreach (var field in resolved.Fields) {
        if (IsFieldHeapAllocated(field, typeDefs)) {
          var fieldTypeName = (field.Type as IrStructType)?.Name ?? field.Type.Name;
          managedFields.Add((field.Offset, fieldTypeName, false));
        } else if (isManagedMemory && field.Name == "buffer") {
          // __ManagedMemory.buffer is a raw pointer (I64) that needs mm_raw_free
          managedFields.Add((field.Offset, "raw_buffer", true));
        } else if (isManagedCursor && field.Name == "source_ptr") {
          // __ManagedMemoryCursor.source_ptr is a heap pointer to the source __ManagedMemory that needs mm_decref
          managedFields.Add((field.Offset, "__ManagedMemory", false));
        }
      }
      _destructorRequests[typeName] = new DestructorRequest(typeName, managedFields,
        NeedsManagedElementCleanup: needsManagedElementCleanup);
    } else if (typeDef is IrEnumType enumType && enumType.HasAssociatedValues) {
      // Enum types with associated values — the destructor dispatches on tag
      _destructorRequests[typeName] = new DestructorRequest(typeName, []);
    }
  }

  /// <summary>
  /// Generates destructor functions for all registered types. Each destructor takes a
  /// raw user pointer and mm_decrefs all managed fields. Called by mm_decref when rc reaches 0.
  /// </summary>
  private static void GenerateTypeDestructors(IrModule<StandardOp> result) {
    if (_destructorRequests == null || _destructorRequests.Count == 0) return;

    foreach (var (typeName, request) in _destructorRequests) {
      var destructorName = $"__destruct_{typeName}";
      var func = new IrFunction<StandardOp>(destructorName, ["ptr"], [IrType.I64], null, null);
      var entry = func.Body.AddBlock("entry");

      var paramOp = new StdParamOp(0, "ptr", new StdI64(IrContext.Current.NextStdId()));
      entry.AddOp(paramOp);
      var ptr = (StdI64)paramOp.Result;
      entry.AddOp(new StdStoreI64Op(ptr, "__destr_ptr"));

      if (result.TypeDefs.TryGetValue(typeName, out var typeDef) && typeDef is IrEnumType enumType && enumType.HasAssociatedValues) {
        // Enum destructor: load tag, dispatch to per-case cleanup
        // For each case with managed payloads, check tag and mm_decref them
        for (int ci = 0; ci < enumType.Cases.Count; ci++) {
          var caseInfo = enumType.Cases[ci];
          var managedPayloads = new List<(int slotIndex, IrType type)>();
          if (caseInfo.AssociatedValues != null) {
            for (int pi = 0; pi < caseInfo.AssociatedValues.Count; pi++) {
              if (caseInfo.AssociatedValues[pi].Type.IsHeapAllocated)
                managedPayloads.Add((pi, caseInfo.AssociatedValues[pi].Type));
            }
          }
          if (managedPayloads.Count == 0) continue;

          // Re-load tag in each check block to avoid cross-block value references
          var ptrLoad = new StdLoadI64Op("__destr_ptr");
          entry.AddOp(ptrLoad);
          var tagLoad = EmitUnionTagLoadFrom(ptrLoad.Result, entry);
          // Compare against the case's TagValue, not its list index: construction stores TagValue
          // (see MaxonEnumConstructOp), and the two diverge as soon as any case carries an explicit
          // raw value — auto-increment resumes from it, so `c` in `union { a = 5, b = 9, c(s String) }`
          // is stored as 10 while its index is 2, and an index comparison could never match.
          var tagConst = new StdConstI64Op(caseInfo.TagValue);
          entry.AddOp(tagConst);
          var tagCmp = new StdCmpI64Op("eq", tagLoad, tagConst.Result);
          entry.AddOp(tagCmp);
          var caseBlock = $"case_{ci}";
          var nextBlock = ci < enumType.Cases.Count - 1 ? $"check_{ci + 1}" : "done";
          entry.AddOp(new StdCondBrOp(tagCmp.Result, caseBlock, nextBlock));

          var caseBody = func.Body.AddBlock(caseBlock);
          foreach (var (slotIndex, _) in managedPayloads) {
            var casePtr = new StdLoadI64Op("__destr_ptr");
            caseBody.AddOp(casePtr);
            int byteOffset = UnionPayloadOffset(slotIndex);
            var payloadLoad = new StdLoadIndirectOp(casePtr.Result, byteOffset, IrType.I64);
            caseBody.AddOp(payloadLoad);
            EmitDecrefValueIfNonnull(caseBody, (StdI64)payloadLoad.Result, $"~{typeName}");
          }
          caseBody.AddOp(new StdBrOp("done"));

          // Continue checking for next case
          if (ci < enumType.Cases.Count - 1) {
            entry = func.Body.AddBlock(nextBlock);
          }
        }

        // If we fell through all cases without a match, jump to done
        if (entry.Operations.Count == 0 || entry.Operations[^1] is not StdBrOp and not StdCondBrOp) {
          entry.AddOp(new StdBrOp("done"));
        }
      } else if (request.ManagedListClearFunc != null) {
        // ManagedList destructor: call managed_list_clear or managed_list_clear_managed to walk and free all nodes
        var managedListPtr = new StdLoadI64Op("__destr_ptr");
        entry.AddOp(managedListPtr);
        entry.AddOp(new StdCallRuntimeOp(request.ManagedListClearFunc, [managedListPtr.Result], null));
        entry.AddOp(new StdBrOp("done"));
      } else {
        // Struct destructor: mm_decref each managed field, mm_raw_free raw buffers
        var destructorScope = $"~{typeName}";

        int fieldIdx = 0;
        foreach (var (offset, fieldTypeName, isRawBuffer) in request.ManagedFields) {
          var fieldPtrLoad = new StdLoadI64Op("__destr_ptr");
          entry.AddOp(fieldPtrLoad);
          var fieldLoad = new StdLoadIndirectOp(fieldPtrLoad.Result, offset, IrType.I64);
          entry.AddOp(fieldLoad);
          if (isRawBuffer) {
            // Raw buffer inside __ManagedMemory: three modes based on capacity
            //   capacity == -1  (slice): mm_decref(parentPtr) — buffer belongs to parent
            //   capacity == -2  (rdata): nothing — static data, no cleanup
            //   capacity >= 0   (owned): mm_raw_free(buffer) — we own the buffer
            var capPtrLoad = new StdLoadI64Op("__destr_ptr");
            entry.AddOp(capPtrLoad);
            var capLoad = new StdLoadIndirectOp(capPtrLoad.Result, ManagedFieldCapacity, IrType.I64);
            entry.AddOp(capLoad);

            // Check for slice mode: capacity == -1
            var negOne = new StdConstI64Op(-1);
            entry.AddOp(negOne);
            var isSlice = new StdCmpI64Op("eq", (StdI64)capLoad.Result, negOne.Result);
            entry.AddOp(isSlice);
            var sliceCleanupBlock = $"slice_cleanup_{fieldIdx}";
            var checkOwnedBlock = $"check_owned_{fieldIdx}";
            var skipBlock = $"skip_buf_{fieldIdx}";
            entry.AddOp(new StdCondBrOp(isSlice.Result, sliceCleanupBlock, checkOwnedBlock));

            // Slice cleanup: mm_decref(parentPtr)
            var sliceBody = func.Body.AddBlock(sliceCleanupBlock);
            var parentPtrLoad = new StdLoadI64Op("__destr_ptr");
            sliceBody.AddOp(parentPtrLoad);
            var parentLoad = new StdLoadIndirectOp(parentPtrLoad.Result, ManagedFieldParentPtr, IrType.I64);
            sliceBody.AddOp(parentLoad);
            EmitDecrefValueIfNonnull(sliceBody, (StdI64)parentLoad.Result, $"~{typeName}");
            sliceBody.AddOp(new StdBrOp(skipBlock));

            // Check owned mode: capacity != -2 (rdata sentinel)
            var ownedEntry = func.Body.AddBlock(checkOwnedBlock);
            var capReload = new StdLoadI64Op("__destr_ptr");
            ownedEntry.AddOp(capReload);
            var capReloadVal = new StdLoadIndirectOp(capReload.Result, ManagedFieldCapacity, IrType.I64);
            ownedEntry.AddOp(capReloadVal);
            var negTwo = new StdConstI64Op(MmCapacityRdata);
            ownedEntry.AddOp(negTwo);
            var capNeRdata = new StdCmpI64Op("ne", (StdI64)capReloadVal.Result, negTwo.Result);
            ownedEntry.AddOp(capNeRdata);
            var freeBlock = $"free_buf_{fieldIdx}";
            ownedEntry.AddOp(new StdCondBrOp(capNeRdata.Result, freeBlock, skipBlock));

            var freeBody = func.Body.AddBlock(freeBlock);

            // Heap-backed buffer with managed elements: decref each element
            // before freeing (COW copy owns its own element references).
            // Runs for both external and inline buffers — the elements are references either way.
            if (request.NeedsManagedElementCleanup) {
              var selfPtr = new StdLoadI64Op("__destr_ptr");
              freeBody.AddOp(selfPtr);
              freeBody.AddOp(new StdCallRuntimeOp("mm_decref_managed_elements", [selfPtr.Result], null));
            }

            // Byte-fusion: an INLINE buffer (parent_ptr == MmParentInline) lives in the record's
            // own allocation (self + recordSize), so there is no separate raw buffer to free — it
            // dies with the record's slot. Only an EXTERNAL owned buffer is mm_raw_free'd. The
            // "should free" predicate is the branch's TRUE target so rawFreeBlock (created next) is
            // the fallthrough, matching the capacity-!= -2 dispatch above.
            var inlParentLoad = new StdLoadI64Op("__destr_ptr");
            freeBody.AddOp(inlParentLoad);
            var inlParentVal = new StdLoadIndirectOp(inlParentLoad.Result, ManagedFieldParentPtr, IrType.I64);
            freeBody.AddOp(inlParentVal);
            var inlineSentinel = new StdConstI64Op(MmParentInline);
            freeBody.AddOp(inlineSentinel);
            var notInline = new StdCmpI64Op("ne", (StdI64)inlParentVal.Result, inlineSentinel.Result);
            freeBody.AddOp(notInline);
            var rawFreeBlock = $"raw_free_{fieldIdx}";
            freeBody.AddOp(new StdCondBrOp(notInline.Result, rawFreeBlock, skipBlock));

            var rawFreeBody = func.Body.AddBlock(rawFreeBlock);
            var bufPtrLoad = new StdLoadI64Op("__destr_ptr");
            rawFreeBody.AddOp(bufPtrLoad);
            var bufLoad = new StdLoadIndirectOp(bufPtrLoad.Result, offset, IrType.I64);
            rawFreeBody.AddOp(bufLoad);
            EmitRawFree(rawFreeBody, (StdI64)bufLoad.Result);
            rawFreeBody.AddOp(new StdBrOp(skipBlock));

            entry = func.Body.AddBlock(skipBlock);
          } else {
            // Heap-allocated field: decref triggers the field's own destructor
            // (self-contained cleanup — managed element fields handle their own buffer walk)
            EmitDecrefValueIfNonnull(entry, (StdI64)fieldLoad.Result, destructorScope);
          }
          fieldIdx++;
        }
        entry.AddOp(new StdBrOp("done"));
      }

      // done: return
      var done = func.Body.AddBlock("done");
      done.AddOp(new StdReturnOp(null));

      result.AddFunction(func);
    }
  }

  private static void EnsureUcddataLoaded(string label, IrModule<StandardOp> module) {
    if (_loadedUcdLabels!.Contains(label)) return;
    if (module.UcddataEntries.Any(e => e.label == label)) {
      _loadedUcdLabels.Add(label);
      return;
    }
    var binName = label.TrimStart('_') + ".bin";
    var stdlibPath = StdlibLoader.FindStdlibPath() ?? throw new InvalidOperationException($"Cannot find stdlib path for ucd data '{label}'");
    var binPath = Path.Combine(stdlibPath, "helpers", "string", binName);
    if (!File.Exists(binPath)) throw new InvalidOperationException($"UCD binary file not found: {binPath}");
    module.UcddataEntries.Add((label, File.ReadAllBytes(binPath), 8));
    _loadedUcdLabels.Add(label);
  }

  private static void LowerUcdByteLoad(MaxonUcdByteLoadOp op, IrBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap, IrModule<StandardOp> result) {
    EnsureUcddataLoaded(op.UcddataLabel, result);
    var leaOp = new StdLeaUcddataOp(op.UcddataLabel);
    block.AddOp(leaOp);
    var ptrOp = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrOp);
    var index = (StdI64)valueMap[op.ByteOffset];
    var addrOp = new StdAddI64Op(ptrOp.Result, index);
    block.AddOp(addrOp);
    // UCD byte loads are unsigned bytes from a static data table; zero-extend on load
    // so callers that compare against Unicode property values (0..127 for most) get the
    // raw byte value, not a sign-extended negative.
    var loadOp = new StdLoadIndirectOp(addrOp.Result, 0, IrType.U8);
    block.AddOp(loadOp);
    valueMap[op.Result] = loadOp.Result;
  }

  private static void LowerUcdI64Load(MaxonUcdI64LoadOp op, IrBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap, IrModule<StandardOp> result) {
    EnsureUcddataLoaded(op.UcddataLabel, result);
    var leaOp = new StdLeaUcddataOp(op.UcddataLabel);
    block.AddOp(leaOp);
    var ptrOp = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrOp);
    var index = (StdI64)valueMap[op.Index];
    var scaleOp = new StdConstI64Op(8);
    block.AddOp(scaleOp);
    var byteOffOp = new StdMulI64Op(index, scaleOp.Result);
    block.AddOp(byteOffOp);
    var addrOp = new StdAddI64Op(ptrOp.Result, byteOffOp.Result);
    block.AddOp(addrOp);
    var loadOp = new StdLoadIndirectOp(addrOp.Result, 0, IrType.I64);
    block.AddOp(loadOp);
    valueMap[op.Result] = loadOp.Result;
  }

}
