using System.Text;
using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// MEASUREMENT-ONLY pass (behind the `--literal-coverage` flag) that estimates what
/// fraction of string / byte-string / character LITERAL sites are provably
/// never-mutated, and could therefore be lowered to an immortal record in read-only
/// data instead of a heap allocation. It prints a report to stderr and changes
/// NOTHING about the emitted program — it sets no properties the lowering reads.
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
///     (conservatively assumed to mutate — counts AGAINST static-eligibility).
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

  private enum LitKind { String, ByteString, Char, Array }

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

    public Analysis(IrModule<MaxonOp> module) {
      _module = module;
      _funcByName = new Dictionary<string, IrFunction<MaxonOp>>(module.Functions.Count);
      foreach (var f in module.Functions) {
        _funcByName[f.Name] = f;
        _mutatingParams[f.Name] = [];
      }
    }

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
            ctx.IntrinsicSinks.Add(ValueNode(gs.Value.Id));
            ctx.IntrinsicSinkValueIds.Add(gs.Value.Id);
          }
          break;
        case MaxonFieldAccessOp fa when fa.Result != null:
          if (fa.FieldName == "managed") {
            // self.managed IS the record (post envelope-collapse).
            Union(ValueNode(fa.StructValue.Id), ValueNode(fa.Result.Id));
          }
          // Destructured self-field: a bare `F` inside a method reads self.F, so bind
          // the field-access-of-self result to the field-name variable. Restricted to
          // self so a peer struct's `other.F` never merges into self's `F`.
          if (ctx.SelfParamNode >= 0 && Find(ValueNode(fa.StructValue.Id)) == Find(ctx.SelfParamNode)) {
            Union(ValueNode(fa.Result.Id), VarNode(f.Name, fa.FieldName));
          }
          break;
        case MaxonStructLiteralOp sl:
          // String{managed: X} / ByteArray{managed: X} aliases X (zero-copy).
          foreach (var (fieldName, value) in sl.FieldValues) {
            if (fieldName == "managed") Union(ValueNode(sl.Result.Id), ValueNode(value.Id));
          }
          // A CONSTANT array literal (`[1,2,3]`, `sha256KTable = [...]`) is a literal site too: its
          // elements are already compile-time constants in rdata, so a never-mutated one can be a
          // shared immortal record. Only constant arrays are recorded — a runtime-valued array
          // literal has nothing static to share. Managed-element arrays (`["a","b"]`) are NOT
          // constant arrays and are left to the heap path (3c, not covered here). A constant array
          // bound to a MUTABLE GLOBAL (IsMutable) is skipped: this per-function analysis cannot see
          // a global mutated in another function, so those stay on the heap (the lowering enforces
          // the same guard).
          if (_module.ConstantArrayLiterals.TryGetValue(sl.Result.Id, out var cai) && !cai.IsMutable) {
            ctx.Literals.Add((LitKind.Array, sl.Result.Id, Preview(sl.ArrayLiteralTag ?? "[array]")));
          }
          break;

        // --- returns ---
        case MaxonReturnOp r when r.Value != null:
          ctx.ReturnNodes.Add(ValueNode(r.Value.Id));
          break;

        // --- dedicated mutating managed-mem ops: receiver is operand 0 ---
        case MaxonManagedMemSetOp:
        case MaxonManagedMemGrowOp:
        case MaxonManagedMemSetLengthOp:
        case MaxonManagedMemClearOp:
        case MaxonManagedMemShiftOp:
        case MaxonManagedMemRemoveOp:
        case MaxonManagedMemByteSetOp:
        case MaxonManagedMemAppendOp:
          if (op.Operands.Count > 0) {
            var recv = op.Operands[0].Id;
            ctx.IntrinsicSinks.Add(ValueNode(recv));
            ctx.IntrinsicSinkValueIds.Add(recv);
          }
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
          if (op is IReadsVarByName reader && op is not MaxonEnumPayloadAssignOp) {
            foreach (var res in op.Results) {
              Union(VarNode(f.Name, reader.ReadVarName), ValueNode(res.Id));
            }
          }
          break;
      }
    }

    private void BindParam(IrFunction<MaxonOp> f, FuncCtx ctx, int index, string name, int valueId) {
      var node = ValueNode(valueId);
      Union(node, VarNode(f.Name, name));
      if (index < ctx.ParamNodes.Length) ctx.ParamNodes[index] = node;
      if (index == 0 && name == "self") ctx.SelfParamNode = node;
    }

    private void BuildDirectCall(FuncCtx ctx, MaxonCallOp call) {
      // Known mutating builtins: arg 0 is the mutated receiver.
      if (MutatingBuiltinCallees.Contains(call.Callee)) {
        if (call.Args.Count > 0) {
          var recv = call.Args[0].Id;
          ctx.IntrinsicSinks.Add(ValueNode(recv));
          ctx.IntrinsicSinkValueIds.Add(recv);
        }
        return;
      }

      var site = new CallSite {
        Callee = call.Callee,
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
        $"(strings {t.StrStatic}/{t.StrTotal}, bytestrings {t.ByteStatic}/{t.ByteTotal}, chars {t.CharStatic}/{t.CharTotal}, constarrays {t.ArrStatic}/{t.ArrTotal})");
      if (t.Total > t.Static) {
        sb.AppendLine(
          $"  rejected: mutating-intrinsic-target={t.RejIntrinsic} passed-to-mutating-param={t.RejParam} " +
          $"aliased={t.RejAliased} conservative-indirect-call={t.RejIndirect}");
      }
    }

    private sealed class Tally {
      public int Total, Static;
      public int StrTotal, StrStatic, ByteTotal, ByteStatic, CharTotal, CharStatic, ArrTotal, ArrStatic;
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
