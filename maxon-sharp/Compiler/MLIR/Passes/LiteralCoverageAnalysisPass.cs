using System.Text;
using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Whole-program escape analysis deciding which managed sites — string, byte-string, character and
/// constant-array LITERALS, and the CALL that returns a constant empty container — are provably
/// never written through, and may therefore be lowered to ONE shared immortal record instead of a
/// per-evaluation heap allocation. It ALWAYS runs: its result is `IrModule.StaticEligibleLiteralIds`,
/// which the lowering reads. Only the coverage REPORT it prints to stderr is behind the
/// `--literal-coverage` flag.
///
/// A managed literal is "static-eligible" when its value never flows to an in-place
/// mutation of its backing record. Mutation reaches a value through:
///   - a dedicated mutating __ManagedMemory op (set/grow/setLength/clear/shift/remove/
///     byteSet/append) with the value as its receiver;
///   - a `__managed_mem_*` throwing builtin call with the value as arg 0;
///   - being passed at a parameter position the callee mutates (computed by a
///     call-graph fixpoint);
///   - being returned from a function whose result some caller mutates;
///   - being captured by / passed to a closure, indirect, or async call
///     (conservatively assumed to mutate — counts AGAINST static-eligibility);
///   - being STORED INTO A HEAP PLACE — a struct field, an array slot, an enum payload,
///     a mutable global — from where anything can fetch it back out and write through it.
///     See AddEscapeSink, which lists what every one of those doors was measured doing.
///
/// Flow WITHIN a function is tracked by a Steensgaard-style union-find over both SSA
/// values and named variables. Assignments, var loads, `.managed` field access, and
/// managed struct-literal construction (the zero-copy alias behind String.from /
/// toByteArray / fromOwnedBytes) all union their operands, so a literal reaching any
/// aliasing path shares a component with the mutation and is counted as mutated.
///
/// The union-based intraprocedural model over-approximates flow (it is symmetric),
/// and closure captures / unresolved callees are handled conservatively, so the
/// reported eligibility is a LOWER BOUND: the true fraction is at least what it says.
///
/// It is closely related to ParameterMutationAnalysisPass (which answers a narrower,
/// ABI-facing question about `self` and reassignment); this pass deliberately keeps
/// its own value-level model because it must classify individual literal sites and
/// attribute a rejection reason, which the ABI pass does not expose.
/// </summary>
public static class LiteralCoverageAnalysisPass {
  // __ManagedMemory throwing builtins whose arg 0 is the mutated receiver. These
  // reach the pipeline as MaxonTryCallOp/MaxonCallOp (emitted by the parser), not as
  // dedicated ops. append/clear stay dedicated ops but are included for safety in
  // case any path lowers them to calls.
  private static readonly HashSet<string> MutatingBuiltinCallees = [
    "__managed_mem_set", "__managed_mem_set_byte", "__managed_mem_set_length",
    "__managed_mem_grow", "__managed_mem_shift_right", "__managed_mem_shift_left",
    "__managed_mem_swap", "__managed_mem_remove",
    "__managed_mem_append", "__managed_mem_clear",
  ];

  // The one builtin in that set that STORES A VALUE into a container slot, and where that value sits.
  // `set_byte` stores a primitive and `append` copies its source's bytes rather than storing it, so
  // neither puts a record anywhere the value graph cannot follow.
  private const string ElementStoreBuiltinCallee = "__managed_mem_set";
  private const int ElementStoreValueArgIndex = 2;

  // The field on a fused managed wrapper (String/Character/Array) that IS the record itself since the
  // envelope collapse: reading or writing it is aliasing, not a store into a place.
  private const string ManagedWrapperFieldName = "managed";

  // The kinds of site whose record can be shared. EmptyContainer is a CALL rather than a literal
  // op: `Array.create()` is the only way a program can spell an empty container (the language
  // refuses `Array{}` outside the type), and the record such a factory returns is a compile-time
  // constant.
  private enum LitKind { String, ByteString, Char, Array, EmptyContainer }

  private enum Reason { Eligible, MutatingIntrinsicTarget, PassedToMutatingParam, ConservativeIndirect, Aliased }

  /// Runs the whole-program escape analysis and returns the SET of static-eligible literal
  /// site ids (the MaxonValue result id of each provably-never-mutated string/byte/char
  /// literal). This is the sound lower bound the static-literal lowering consumes: a site in
  /// the set is safe to emit as one shared immortal record; a site absent from it falls back
  /// to a per-evaluation heap allocation. When <paramref name="report"/> is set, also prints
  /// the coverage report to stderr (the `--literal-coverage` measurement path).
  public static HashSet<int> Run(IrModule<MaxonOp> module, bool report) {
    var analysis = new Analysis(module);
    analysis.BuildGraphs();
    analysis.Solve();
    if (report) analysis.Report();
    return analysis.CollectEligible();
  }

  private sealed class Analysis {
    private readonly IrModule<MaxonOp> _module;
    private readonly Dictionary<string, IrFunction<MaxonOp>> _funcByName;

    // Union-find over nodes. Value nodes are keyed by SSA value id; variable nodes
    // by "<function>\0<varName>". Node ids are dense indices into _parent/_rank.
    private readonly List<int> _parent = [];
    private readonly List<int> _rank = [];
    private readonly Dictionary<int, int> _valueNode = [];
    private readonly Dictionary<string, int> _varNode = [];

    private readonly List<FuncCtx> _ctxs = [];

    // Interprocedural facts (all monotonic — grow only — so the fixpoint terminates).
    // `_callerMutatesResult` runs the propagation BACKWARD: when a caller mutates a
    // call's result, the callee's return value (and any param its return aliases) is
    // marked mutated. That backward edge is what makes both `x = returnsLiteral();
    // x.mutate()` AND `literal.toByteArray().set()` come out non-eligible — the callee's
    // internal `.managed` / struct-literal / param aliasing already puts its return and
    // the aliased param in one union component, so marking the return mutated marks the
    // param too. (An earlier FORWARD variant — union the call result with the arg at each
    // return-aliasing position — was removed: it added no soundness over this backward
    // edge but formed a feedback loop with it that spuriously poisoned every call site of
    // a return-aliasing helper, e.g. flagging every `StringBuilder.append("literal")`.)
    private readonly Dictionary<string, HashSet<int>> _mutatingParams = [];
    private readonly HashSet<string> _callerMutatesResult = [];

    // Callee SPELLING -> the function name the lowering will actually call. MaxonToStandardConversion's
    // ResolveCallee takes an exact function name, and failing that ANY function whose name ends in
    // `.<callee>` — the cross-namespace call under directory-as-module, where `String.clone` reaches
    // `stdlib.String.clone`. This analysis has to resolve a callee the same way or its facts land under
    // a name nothing reads: a mutating parameter would go unrecorded and a value written through it
    // would come out static-eligible. Every entry is seeded once, so a lookup stays O(1) — the walk
    // ResolveCallee does per unresolved call would be a scan of every function at every call site.
    private readonly Dictionary<string, string> _canonicalCallee = [];

    public Analysis(IrModule<MaxonOp> module) {
      _module = module;
      _funcByName = new Dictionary<string, IrFunction<MaxonOp>>(module.Functions.Count);
      foreach (var f in module.Functions) {
        _funcByName[f.Name] = f;
        _mutatingParams[f.Name] = [];
        _canonicalCallee[f.Name] = f.Name;
      }
      // Suffixes second, and only where no function OWNS that spelling — an exact name always wins,
      // exactly as ResolveCallee tries the dictionary before the suffix walk. TryAdd then keeps
      // module order, which is the order ResolveCallee's FirstOrDefault picks from.
      foreach (var f in module.Functions) {
        for (int dot = f.Name.IndexOf('.'); dot >= 0; dot = f.Name.IndexOf('.', dot + 1)) {
          _canonicalCallee.TryAdd(f.Name[(dot + 1)..], f.Name);
        }
      }
    }

    /// The function name <paramref name="callee"/> denotes, or the spelling itself when this module
    /// has no such function — a managed-memory/socket/file builtin, which the lowering intercepts
    /// before it ever resolves a callee, and which this pass classifies by name of its own.
    private string CanonicalCallee(string callee) =>
      _canonicalCallee.TryGetValue(callee, out var name) ? name : callee;

    // ---- union-find ----
    private int Find(int x) {
      while (_parent[x] != x) {
        _parent[x] = _parent[_parent[x]];
        x = _parent[x];
      }
      return x;
    }

    private bool Union(int a, int b) {
      int ra = Find(a), rb = Find(b);
      if (ra == rb) return false;
      if (_rank[ra] < _rank[rb]) (ra, rb) = (rb, ra);
      _parent[rb] = ra;
      if (_rank[ra] == _rank[rb]) _rank[ra]++;
      return true;
    }

    private int NewNode() {
      _parent.Add(_parent.Count);
      _rank.Add(0);
      return _parent.Count - 1;
    }

    private int ValueNode(int valueId) {
      if (!_valueNode.TryGetValue(valueId, out var n)) {
        n = NewNode();
        _valueNode[valueId] = n;
      }
      return n;
    }

    private int VarNode(string funcName, string varName) {
      var key = funcName + "\0" + varName;
      if (!_varNode.TryGetValue(key, out var n)) {
        n = NewNode();
        _varNode[key] = n;
      }
      return n;
    }

    private sealed class CallSite {
      public required string Callee;
      public required int[] ArgNodes;
      public required int[] ArgValueIds;
      public int ResultNode = -1;
      public int ResultValueId = -1;
    }

    private sealed class FuncCtx {
      public required IrFunction<MaxonOp> Func;
      public readonly List<CallSite> Calls = [];
      // Nodes mutated regardless of interprocedural facts.
      public readonly List<int> IntrinsicSinks = [];
      public readonly List<int> IndirectSinks = [];
      // Value ids that sit DIRECTLY in a sink position — used for reason attribution.
      public readonly HashSet<int> IntrinsicSinkValueIds = [];
      public readonly HashSet<int> IndirectSinkValueIds = [];
      // param index -> node (-1 for a param with no SSA value in this body).
      public int[] ParamNodes = [];
      // Union-find node of the `self` receiver (index 0 named "self"), else -1. Used
      // to bind destructured self-field variables (bare `managed` == self.managed).
      public int SelfParamNode = -1;
      public readonly List<int> ReturnNodes = [];
      // Literal sites in this function. Preview is the literal text (truncated),
      // kept only for the optional LITCOV_DUMP diagnostic.
      public readonly List<(LitKind Kind, int ValueId, string Preview)> Literals = [];
    }

    public void BuildGraphs() {
      foreach (var f in _module.Functions) {
        var ctx = new FuncCtx {
          Func = f,
          ParamNodes = new int[f.ParamNames.Count],
        };
        Array.Fill(ctx.ParamNodes, -1);

        foreach (var block in f.Body.Blocks) {
          foreach (var op in block.Operations) {
            BuildOp(f, ctx, op);
          }
        }

        _ctxs.Add(ctx);
      }
    }

    private void BuildOp(IrFunction<MaxonOp> f, FuncCtx ctx, MaxonOp op) {
      switch (op) {
        // --- literal sites ---
        case MaxonStringLiteralOp s:
          ctx.Literals.Add((LitKind.String, s.Result.Id, Preview(s.Value)));
          break;
        case MaxonByteStringLiteralOp b:
          ctx.Literals.Add((LitKind.ByteString, b.Result.Id, Preview(b.Value)));
          break;
        case MaxonCharLiteralOp c:
          ctx.Literals.Add((LitKind.Char, c.Result.Id, Preview(c.Value)));
          break;

        // --- parameters ---
        // A parameter is readable by its own name (via struct_var_ref/var_ref), so
        // bind the param value to its name-variable. `self` is recorded so its
        // destructured field variables (bare `managed` == self.managed) can be bound.
        case MaxonStructParamOp sp:
          BindParam(f, ctx, sp.Index, sp.Name, sp.Result.Id);
          break;
        case MaxonParamOp p:
          BindParam(f, ctx, p.Index, p.Name, p.Result.Id);
          break;
        case MaxonFunctionParamOp fp:
          BindParam(f, ctx, fp.Index, fp.Name, fp.Result.Id);
          break;

        // --- aliasing edges ---
        case MaxonAssignOp a:
          // The stored value flows into the variable.
          Union(ValueNode(a.Value.Id), VarNode(f.Name, a.VarName));
          break;

        case MaxonGlobalStoreOp gs:
          // A value stored into a MUTABLE GLOBAL escapes this per-function analysis: another
          // function can mutate the global in place (`g.append(...)`), which the local var graph
          // cannot see. Treat it as mutated so its literal is never made a shared immortal record
          // (which would leak the COW buffer and corrupt other occurrences of the literal). A `let`
          // global is immutable, so its initializer stays eligible. Mirrors the IsMutable guard on
          // constant array literals.
          if (_module.GlobalVarInfos.TryGetValue(gs.GlobalName, out var gvi) && gvi.Mutable) {
            AddSink(ctx, gs.Value.Id);
          }
          break;
        case MaxonFieldAccessOp fa when fa.Result != null:
          if (fa.FieldName == ManagedWrapperFieldName) {
            // self.managed IS the record (post envelope-collapse).
            Union(ValueNode(fa.StructValue.Id), ValueNode(fa.Result.Id));
          }
          // Destructured self-field: a bare `F` inside a method reads self.F, so bind
          // the field-access-of-self result to the field-name variable. Restricted to
          // self so a peer struct's `other.F` never merges into self's `F`.
          if (ctx.SelfParamNode >= 0 && Find(ValueNode(fa.StructValue.Id)) == Find(ctx.SelfParamNode)) {
            Union(ValueNode(fa.Result.Id), VarNode(f.Name, fa.FieldName));
            // That destructured name IS the field, so it is a heap PLACE, and `F = v` inside the
            // method stores into it — but it is spelled as a plain assign to a local (there is no
            // MaxonFieldAssignOp to sink at; see the IR for `function reset() -> items = X`). The
            // sink therefore goes on the binding, which is the only op that names the field at all.
            // MEASURED: two `Holder`s whose `reset()` assigned `items = IntArray.create()` shared ONE
            // immortal record, and pushing through one published the other's count as 1, and leaked.
            // A method that never WRITES the field pays nothing for this, and needs no guard saying
            // so: only an assign unions a value into the name's component, so with no assign there is
            // no literal site in it to mark. (Guarding on it was tried and MEASURED to change nothing
            // — 3295 golden `mm_alloc` sites either way.) `managed` is excluded above for the usual
            // reason: a wrapper IS its record, so that access is aliasing rather than a place.
            if (fa.FieldName != ManagedWrapperFieldName) AddSink(ctx, fa.Result.Id);
          }
          break;
        // --- enum payload slots: a heap place, exactly like a struct field ---
        case MaxonEnumConstructOp ec:
          // MEASURED: `var a = Box.named("tag")` then `match a { named(n) then n.append("!") }` made an
          // untouched `let t = "tag"` read "tag!", and the same shape holding an empty `Array.create()`
          // published an untouched sibling's count as 1. Both leaked. See AddEscapeSink.
          foreach (var payload in ec.Args) AddEscapeSink(ctx, payload);
          break;
        case MaxonEnumPayloadAssignOp epa:
          AddEscapeSink(ctx, epa.NewValue);
          break;

        case MaxonFieldAssignOp fasgn:
          // `h.field = v` is the same store the struct literal below performs, one syntax over — and
          // it was the door the empty-container record walked through: `StringBuilder.build()` resets
          // with `self.bytes = ByteArray.create()`. `managed` is the exception for the same reason it
          // is there: post envelope-collapse a wrapper IS its record, so writing it is aliasing.
          if (fasgn.FieldName == ManagedWrapperFieldName) {
            Union(ValueNode(fasgn.StructValue.Id), ValueNode(fasgn.NewValue.Id));
          } else {
            AddEscapeSink(ctx, fasgn.NewValue);
          }
          break;

        case MaxonStructLiteralOp sl:
          foreach (var (fieldName, value) in sl.FieldValues) {
            if (fieldName == ManagedWrapperFieldName) {
              // String{managed: X} / ByteArray{managed: X} aliases X (zero-copy). The wrapper IS its
              // __ManagedMemory since the envelope collapse, so this is identity, not storage.
              Union(ValueNode(sl.Result.Id), ValueNode(value.Id));
            } else {
              // ...but stored into ANY OTHER field, the value escapes into a heap place — see
              // AddEscapeSink, which states the rule and what every door of it was measured doing.
              AddEscapeSink(ctx, value);
            }
          }
          // An ARRAY literal is a literal site too: a never-mutated one can be a shared immortal
          // record — a CONSTANT primitive array (`[1,2,3]`, elements packed in rdata; 3b) or a
          // MANAGED-element array (`["a","b"]`, whose elements are themselves static literals; 3c).
          // A constant array bound to a MUTABLE GLOBAL is skipped here: this per-function analysis
          // cannot see a global mutated in another function (the lowering enforces the same
          // IsMutable guard). A managed array in a mutable global is instead caught by the
          // GlobalStore sink below, so it needs no special case here.
          if (sl.ArrayLiteralTag != null) {
            // An array literal's ELEMENTS are stored into the array at construction, which is the same
                  // escape a `set` is (see AddEscapeSink) — `["red","green"].get(0).append("!")`
            // grew the shared literal and made an untouched `let r = "red"` read "red!". The elements
            // reach the block as assigns to `<tag>.<i>` variables, so the SLOT is what gets sunk; a
            // primitive element's slot costs nothing, since a primitive is never a literal site.
            for (int slot = 0; slot < sl.ArrayLiteralCount; slot++) {
              ctx.IntrinsicSinks.Add(VarNode(f.Name, $"{sl.ArrayLiteralTag}.{slot}"));
            }

            bool constMutableGlobal =
              _module.ConstantArrayLiterals.TryGetValue(sl.Result.Id, out var cai) && cai.IsMutable;
            if (!constMutableGlobal)
              ctx.Literals.Add((LitKind.Array, sl.Result.Id, Preview(sl.ArrayLiteralTag)));
          }
          break;

        // --- returns ---
        case MaxonReturnOp r when r.Value != null:
          ctx.ReturnNodes.Add(ValueNode(r.Value.Id));
          break;

        // --- dedicated mutating managed-mem ops: receiver is operand 0 ---
        case MaxonManagedMemSetOp setOp:
          AddSink(ctx, setOp.ManagedStruct.Id);
          AddEscapeSink(ctx, setOp.Value);
          break;

        case MaxonManagedMemGrowOp:
        case MaxonManagedMemSetLengthOp:
        case MaxonManagedMemClearOp:
        case MaxonManagedMemShiftOp:
        case MaxonManagedMemRemoveOp:
        case MaxonManagedMemByteSetOp:
        case MaxonManagedMemAppendOp:
          if (op.Operands.Count > 0) AddSink(ctx, op.Operands[0].Id);
          break;

        // --- indirect / async: every arg conservatively mutated ---
        case MaxonIndirectCallOp ic:
          foreach (var arg in ic.Args) {
            ctx.IndirectSinks.Add(ValueNode(arg.Id));
            ctx.IndirectSinkValueIds.Add(arg.Id);
          }
          break;
        case MaxonAsyncCallOp ac:
          foreach (var arg in ac.Args) {
            ctx.IndirectSinks.Add(ValueNode(arg.Id));
            ctx.IndirectSinkValueIds.Add(arg.Id);
          }
          break;
        case MaxonClosureCreateOp cc:
          // A captured managed value may be mutated through the closure body.
          foreach (var cap in cc.CapturedValues) {
            ctx.IndirectSinks.Add(ValueNode(cap.Id));
            ctx.IndirectSinkValueIds.Add(cap.Id);
          }
          break;

        // --- direct calls ---
        case MaxonCallOp call:
          BuildDirectCall(ctx, call);
          break;

        default:
          // Var loads (VarRef/StructVarRef/FunctionVarRef/EnumVarRef): the loaded
          // value flows out of the variable. Handled generically so a new loader op
          // participates without a code change here.
          if (op is IReadsVarByName reader) {
            foreach (var res in op.Results) {
              Union(VarNode(f.Name, reader.ReadVarName), ValueNode(res.Id));
            }
          }
          break;
      }
    }

    private void AddSink(FuncCtx ctx, int valueId) {
      ctx.IntrinsicSinks.Add(ValueNode(valueId));
      ctx.IntrinsicSinkValueIds.Add(valueId);
    }

    /// A managed value STORED INTO A HEAP PLACE — a struct field, a container slot, a mutable global —
    /// escapes this per-function value graph. Whoever holds the place can fetch the value back out and
    /// mutate it IN PLACE (`h.name.append("!")`, `arr.get(0).append("!")`), and the graph cannot follow
    /// that: it models values and named bindings, not places, and it cannot even tell which slot came
    /// back. Left eligible, such a value stays a shared immortal record and the write GROWS it:
    /// `ensure_cap` sees a non-owning capacity, detaches, and writes the fresh buffer INTO THE SHARED
    /// RECORD. Every other occurrence of that value then reads the mutated bytes, and the buffer leaks,
    /// because an immortal record's destructor is 0.
    ///
    /// The .rodata safety net the plan specified does not exist to catch this — static records live in
    /// WRITABLE .data, since a data->data pointer cannot be baked under ASLR (__module_init fills
    /// buffer@0 with a RIP-relative lea). The write succeeds silently. These sinks are the guard, and
    /// every one of them is a MEASURED corruption, not a precaution:
    ///   struct-literal field  `var h = Holder.create("fld"); h.name.append("!")` made an untouched
    ///                         `let x = "fld"` read "fld!" and exited 101;
    ///   container slot        `arr.push("lit")` then `arr.get(0).append("!")` did the same to
    ///                         `let t = "lit"`;
    ///   array-literal slot    `["red","green"]`, `get(0).append("!")` did the same to `let r = "red"`;
    ///   field assign          two `StringBuilder`s shared the empty buffer `build()` resets to, and
    ///                         appending to ONE published the OTHER's `byteLength()` as 3.
    ///
    /// ⇒ AN OP THAT STORES A MANAGED VALUE INTO A HEAP PLACE MUST SINK IT HERE. The graph cannot
    /// discover such a store by itself, and what a missed one produces is silent heap corruption
    /// rather than a diagnostic.
    ///
    /// A primitive is never a record and never a literal site, so only record-kind values are taken —
    /// which also keeps an index, a count and a byte out of the mutated components entirely. And note
    /// what is NOT a store: `sb.append(other)` COPIES its source's bytes rather than putting the source
    /// anywhere, so only `set` puts a value in a container slot.
    private void AddEscapeSink(FuncCtx ctx, MaxonValue value) {
      if (value is MaxonStruct or MaxonEnum) AddSink(ctx, value.Id);
    }

    private void BindParam(IrFunction<MaxonOp> f, FuncCtx ctx, int index, string name, int valueId) {
      var node = ValueNode(valueId);
      Union(node, VarNode(f.Name, name));
      if (index < ctx.ParamNodes.Length) ctx.ParamNodes[index] = node;
      if (index == 0 && name == "self") ctx.SelfParamNode = node;
    }

    private void BuildDirectCall(FuncCtx ctx, MaxonCallOp call) {
      // A call to a constant empty-container factory is a literal SITE as well as a call: the record
      // it returns is a compile-time constant (see ConstantArrayAnalysisPass), so a site whose result
      // is never written through can share one immortal copy of it. The site is the CALL, not the
      // `Self{}` inside the shared factory body — deciding it there would let one caller's `push`
      // disqualify every other caller. The call still becomes a CallSite below, so a caller that DOES
      // write through its result still disqualifies the factory's own literal.
      if (call.Result != null && _module.ConstantEmptyContainerFactories.ContainsKey(call.Callee)) {
        ctx.Literals.Add((LitKind.EmptyContainer, call.Result.Id, Preview(call.Callee)));
      }

      // Known mutating builtins: arg 0 is the mutated receiver, and `set`'s arg 2 is a value stored
      // INTO the container, which escapes for the reason AddEscapeSink states.
      if (MutatingBuiltinCallees.Contains(call.Callee)) {
        if (call.Args.Count > 0) AddSink(ctx, call.Args[0].Id);
        if (call.Callee == ElementStoreBuiltinCallee && call.Args.Count > ElementStoreValueArgIndex)
          AddEscapeSink(ctx, call.Args[ElementStoreValueArgIndex]);
        return;
      }

      var site = new CallSite {
        Callee = CanonicalCallee(call.Callee),
        ArgNodes = new int[call.Args.Count],
        ArgValueIds = new int[call.Args.Count],
      };
      for (int i = 0; i < call.Args.Count; i++) {
        site.ArgNodes[i] = ValueNode(call.Args[i].Id);
        site.ArgValueIds[i] = call.Args[i].Id;
      }
      if (call.Result != null) {
        site.ResultNode = ValueNode(call.Result.Id);
        site.ResultValueId = call.Result.Id;
      }
      ctx.Calls.Add(site);
    }

    public void Solve() {
      bool changed = true;
      while (changed) {
        changed = false;
        foreach (var ctx in _ctxs) {
          changed |= StepFunction(ctx);
        }
      }
    }

    private bool StepFunction(FuncCtx ctx) {
      var fname = ctx.Func.Name;
      bool changed = false;

      // Collect the roots of every mutated component.
      var mutatedRoots = new HashSet<int>();
      foreach (var n in ctx.IntrinsicSinks) mutatedRoots.Add(Find(n));
      foreach (var n in ctx.IndirectSinks) mutatedRoots.Add(Find(n));
      foreach (var call in ctx.Calls) {
        if (_mutatingParams.TryGetValue(call.Callee, out var mp)) {
          foreach (var i in mp) {
            if (i < call.ArgNodes.Length) mutatedRoots.Add(Find(call.ArgNodes[i]));
          }
        }
      }
      if (_callerMutatesResult.Contains(fname)) {
        foreach (var rn in ctx.ReturnNodes) mutatedRoots.Add(Find(rn));
      }

      // A mutated param propagates to the mutatingParams fact.
      var mps = _mutatingParams[fname];
      for (int i = 0; i < ctx.ParamNodes.Length; i++) {
        if (ctx.ParamNodes[i] >= 0 && mutatedRoots.Contains(Find(ctx.ParamNodes[i])) && mps.Add(i)) changed = true;
      }

      // A call result that is mutated here means the callee's return is mutated by a caller.
      foreach (var call in ctx.Calls) {
        if (call.ResultNode >= 0 && mutatedRoots.Contains(Find(call.ResultNode))
            && _callerMutatesResult.Add(call.Callee)) {
          changed = true;
        }
      }

      return changed;
    }

    // Final per-function mutated-root computation, plus the mutating-param sink value
    // ids (which depend on the solved facts) for reason attribution.
    private (HashSet<int> mutatedRoots, HashSet<int> mutatingParamSinkValueIds) FinalMutation(FuncCtx ctx) {
      var mutatedRoots = new HashSet<int>();
      var mpSinkValueIds = new HashSet<int>();
      foreach (var n in ctx.IntrinsicSinks) mutatedRoots.Add(Find(n));
      foreach (var n in ctx.IndirectSinks) mutatedRoots.Add(Find(n));
      foreach (var call in ctx.Calls) {
        if (_mutatingParams.TryGetValue(call.Callee, out var mp)) {
          foreach (var i in mp) {
            if (i < call.ArgNodes.Length) {
              mutatedRoots.Add(Find(call.ArgNodes[i]));
              mpSinkValueIds.Add(call.ArgValueIds[i]);
            }
          }
        }
      }
      if (_callerMutatesResult.Contains(ctx.Func.Name)) {
        foreach (var rn in ctx.ReturnNodes) mutatedRoots.Add(Find(rn));
      }
      return (mutatedRoots, mpSinkValueIds);
    }

    /// The static-eligible literal site ids — every literal whose value never reaches a
    /// mutation, computed with the exact same Classify the report counts. A LOWER BOUND
    /// (all imprecision rejects), so the lowering that reads it can only ever be conservative.
    public HashSet<int> CollectEligible() {
      var eligible = new HashSet<int>();
      foreach (var ctx in _ctxs) {
        if (ctx.Literals.Count == 0) continue;
        var (mutatedRoots, mpSinkValueIds) = FinalMutation(ctx);
        foreach (var (_, valueId, _) in ctx.Literals) {
          if (Classify(ctx, valueId, mutatedRoots, mpSinkValueIds) == Reason.Eligible) {
            eligible.Add(valueId);
          }
        }
      }
      return eligible;
    }

    public void Report() {
      var all = new Tally();
      var user = new Tally();
      var stdlib = new Tally();

      // Optional per-site diagnostic: LITCOV_DUMP=<path> writes every non-eligible
      // literal (function, reason, text) so classifications can be hand-audited.
      var dumpPath = Environment.GetEnvironmentVariable("LITCOV_DUMP");
      StreamWriter? dump = dumpPath != null ? new StreamWriter(dumpPath) : null;

      foreach (var ctx in _ctxs) {
        if (ctx.Literals.Count == 0) continue;
        var (mutatedRoots, mpSinkValueIds) = FinalMutation(ctx);
        var tally = ctx.Func.IsStdlib ? stdlib : user;
        var scopeName = ctx.Func.IsStdlib ? "stdlib" : "user";

        foreach (var (kind, valueId, preview) in ctx.Literals) {
          var reason = Classify(ctx, valueId, mutatedRoots, mpSinkValueIds);
          all.Add(kind, reason);
          tally.Add(kind, reason);
          if (dump != null && reason != Reason.Eligible) {
            dump.WriteLine($"{scopeName}\t{reason}\t{kind}\t{ctx.Func.Name}\t\"{preview}\"");
          }
        }
      }

      dump?.Dispose();

      var sb = new StringBuilder();
      sb.AppendLine("=== literal-coverage (static-eligibility of managed literal sites) ===");
      AppendScope(sb, "user", user);
      AppendScope(sb, "stdlib", stdlib);
      AppendScope(sb, "all", all);
      Console.Error.Write(sb.ToString());
    }

    private static string Preview(string value) {
      var oneLine = value.Replace("\n", "\\n").Replace("\t", "\\t").Replace("\"", "\\\"");
      return oneLine.Length <= 40 ? oneLine : oneLine[..40] + "...";
    }

    private Reason Classify(FuncCtx ctx, int valueId, HashSet<int> mutatedRoots, HashSet<int> mpSinkValueIds) {
      if (!_valueNode.TryGetValue(valueId, out var node) || !mutatedRoots.Contains(Find(node))) {
        return Reason.Eligible;
      }
      if (ctx.IntrinsicSinkValueIds.Contains(valueId)) return Reason.MutatingIntrinsicTarget;
      if (mpSinkValueIds.Contains(valueId)) return Reason.PassedToMutatingParam;
      if (ctx.IndirectSinkValueIds.Contains(valueId)) return Reason.ConservativeIndirect;
      return Reason.Aliased;
    }

    private static void AppendScope(StringBuilder sb, string name, Tally t) {
      sb.AppendLine(
        $"literal-coverage [{name}]: {t.Static}/{t.Total} static-eligible " +
        $"(strings {t.StrStatic}/{t.StrTotal}, bytestrings {t.ByteStatic}/{t.ByteTotal}, chars {t.CharStatic}/{t.CharTotal}, " +
        $"constarrays {t.ArrStatic}/{t.ArrTotal}, emptycontainers {t.EmptyStatic}/{t.EmptyTotal})");
      if (t.Total > t.Static) {
        sb.AppendLine(
          $"  rejected: mutating-intrinsic-target={t.RejIntrinsic} passed-to-mutating-param={t.RejParam} " +
          $"aliased={t.RejAliased} conservative-indirect-call={t.RejIndirect}");
      }
    }

    private sealed class Tally {
      public int Total, Static;
      public int StrTotal, StrStatic, ByteTotal, ByteStatic, CharTotal, CharStatic, ArrTotal, ArrStatic,
                 EmptyTotal, EmptyStatic;
      public int RejIntrinsic, RejParam, RejAliased, RejIndirect;

      public void Add(LitKind kind, Reason reason) {
        Total++;
        bool ok = reason == Reason.Eligible;
        if (ok) Static++;
        switch (kind) {
          case LitKind.String: StrTotal++; if (ok) StrStatic++; break;
          case LitKind.ByteString: ByteTotal++; if (ok) ByteStatic++; break;
          case LitKind.Char: CharTotal++; if (ok) CharStatic++; break;
          case LitKind.Array: ArrTotal++; if (ok) ArrStatic++; break;
          case LitKind.EmptyContainer: EmptyTotal++; if (ok) EmptyStatic++; break;
        }
        switch (reason) {
          case Reason.MutatingIntrinsicTarget: RejIntrinsic++; break;
          case Reason.PassedToMutatingParam: RejParam++; break;
          case Reason.Aliased: RejAliased++; break;
          case Reason.ConservativeIndirect: RejIndirect++; break;
        }
      }
    }
  }
}
