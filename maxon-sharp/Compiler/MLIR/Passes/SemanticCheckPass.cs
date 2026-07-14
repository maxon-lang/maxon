using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

public static class SemanticCheckPass {
  public static void Run(IrModule<MaxonOp> module) {
    // E3001: entry function must exist. Walk Functions once instead of
    // materializing a List<> just to read its first two entries.
    var entryName = module.EntryFunctionName;
    IrFunction<MaxonOp>? mainFunc = null;
    foreach (var f in module.Functions) {
      if (f.Name != entryName) continue;
      if (mainFunc == null) {
        mainFunc = f;
      } else {
        throw new CompileError(ErrorCode.SemanticDuplicateDefinition,
          $"Multiple '{entryName}' functions found", f.SourceLine, f.SourceColumn) {
          FilePath = f.SourceFilePath
        };
      }
    }
    if (mainFunc == null)
      throw new CompileError(ErrorCode.SemanticNoMain, $"No '{entryName}' function found");

    // E3002: entry function must return ExitCode
    if (mainFunc.ReturnType is not IrRangedPrimitiveType { Name: "ExitCode" }) {
      throw new CompileError(ErrorCode.SemanticMainWrongReturnType, $"Function '{entryName}' must return ExitCode");
    }

    // E054: entry function cannot throw
    if (mainFunc.ThrowsType != null) {
      throw new CompileError(ErrorCode.SemanticMainCannotThrow, $"{entryName} cannot throw: '{entryName}'", mainFunc.SourceLine, mainFunc.SourceColumn);
    }

    // Non-main entry functions (from "maxon run") must be exported
    if (entryName != "main" && !mainFunc.IsExported) {
      throw new CompileError(ErrorCode.SemanticNoMain, $"Function '{entryName}' is not exported", mainFunc.SourceLine, mainFunc.SourceColumn);
    }

    // Check discarded function results
    CheckDiscardedResults(module);

    // Check async calls target yielding functions
    CheckAsyncYielding(module);

    // Check that a throwing promise is awaited with `try await`, not a bare `await`
    CheckPlainAwaitOfThrowingPromise(module);

    // Check that no promise is awaited twice — `await` is linear
    CheckLinearAwait(module);

    // Check for redundant `if x.contains(k) ... try x.get(k) otherwise ...` pattern
    CheckRedundantContainsGet(module);
  }

  private static void CheckDiscardedResults(IrModule<MaxonOp> module) {
    var funcLookup = new Dictionary<string, IrFunction<MaxonOp>>();
    foreach (var func in module.Functions) {
      funcLookup[func.Name] = func;
    }

    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is not MaxonCallOp call) continue;
          if (call.Result == null) continue;
          if (!call.IsDiscardedResult && !call.IsLetDiscardResult) continue;
          if (!funcLookup.TryGetValue(call.Callee, out var callee)) continue;

          // Chainable methods (returning own type) can always be discarded
          if (IsChainable(callee)) continue;

          if (callee.IsPure) {
            throw new CompileError(ErrorCode.SemanticDiscardedPureResult,
              $"result of pure function '{FormatCalleeName(call.Callee)}' must be used",
              call.CallLine, call.CallColumn) { FilePath = func.SourceFilePath };
          }

          // Impure: explicit `_ =` is allowed, bare discard is not
          if (call.IsDiscardedResult && !call.IsLetDiscardResult) {
            throw new CompileError(ErrorCode.SemanticDiscardedImpureResult,
              $"result of '{FormatCalleeName(call.Callee)}' is not used (use '_ = expr' to discard)",
              call.CallLine, call.CallColumn) { FilePath = func.SourceFilePath };
          }
        }
      }
    }
  }

  private static bool IsChainable(IrFunction<MaxonOp> func) {
    if (func.ParamNames.Count == 0 || func.ParamNames[0] != "self") return false;
    if (func.ReturnType == null) return false;
    var selfType = func.ParamTypes[0];
    return func.ReturnType.Name == selfType.Name;
  }

  private static string FormatCalleeName(string callee) {
    // Strip first namespace segment: "ns.Type.method" -> "Type.method", "ns.func" -> "func"
    var dot = callee.IndexOf('.');
    return dot >= 0 ? callee[(dot + 1)..] : callee;
  }

  /// Known I/O runtime stubs that cause the calling green thread to yield.
  /// These are runtime functions (not user-defined) that call __io_submit_*.
  private static readonly HashSet<string> IoStubs = [
    "maxon_file_read",
    "maxon_managed_file_write",
    "maxon_file_exists",
    "maxon_file_delete",
    "maxon_managed_dir_open_search",
    "maxon_find_next_file",
    "maxon_directory_exists",
    "maxon_create_directory",
    "maxon_get_current_directory",
    "maxon_managed_file_open_read",
    "maxon_managed_file_open_write",
    "maxon_managed_file_open_write_executable",
    "maxon_net_tcp_connect",
    "maxon_net_send",
    "maxon_net_recv",
    "maxon_net_close",
    "maxon_sleep",
    // Phase 3 subprocess builtins — these are the I/O yield points in the
    // new contract. Listed here so `async Subprocess.run(...)` and friends
    // pass the async-yielding analysis once the real runtime (Phase 3.2 +
    // 3.3) wires them to IOCP / kqueue / pidfd. The stubs landed in
    // Phase 3.1 don't yield, but listing them now avoids a churn pass when
    // the real implementation lands. The list intentionally excludes
    // `subprocessResolveOnPath` and the `*Result*` accessors — those are
    // synchronous lookups against in-memory state, not I/O.
    "maxon_subprocess_spawn",
    "maxon_subprocess_wait_collect",
    "maxon_subprocess_kill",
    "maxon_subprocess_send_signal",
    "maxon_subprocess_detach",
    // Streaming subprocess builtins (Phase 3.2 persistent-worker support).
    // Each performs blocking pipe / process I/O on the green thread and yields
    // via the runtime's overlapped-pipe + IOCP plumbing (see
    // __subp_create_overlapped_pipe + maxon_pipe_overlapped_read / _write).
    "maxon_subprocess_spawn_streaming",
    "maxon_subprocess_write_stdin_all",
    "maxon_subprocess_read_stdout_line",
    "maxon_subprocess_read_stderr_line",
    "maxon_subprocess_close_stdin",
    "maxon_subprocess_wait_exit",
    // Synthetic __ManagedSocket builtin callees (MaxonCallOp/MaxonTryCallOp names) that
    // ultimately invoke the above runtime stubs. Keep in sync with TryLowerManagedSocketBuiltin.
    "__managed_socket_send", "__managed_socket_recv", "__managed_socket_close",
    "__managed_socket_tcp_connect",
    // Synthetic __ManagedFile builtin callees (MaxonCallOp/MaxonTryCallOp names) that
    // ultimately invoke the above runtime stubs. Keep in sync with TryLowerManagedFileBuiltin.
    "__managed_file_size", "__managed_file_read", "__managed_file_write",
    "__managed_file_close", "__managed_file_exists",
    "__managed_file_open_read", "__managed_file_open_write",
    "__managed_file_open_write_executable",
    "__managed_file_delete", "__managed_file_stat",
    // Synthetic __ManagedDirectory builtin callees (MaxonCallOp/MaxonTryCallOp names) that
    // ultimately invoke the above runtime stubs. Keep in sync with TryLowerManagedDirectoryBuiltin.
    "__managed_directory_open_search", "__managed_directory_create",
    "__managed_directory_current_path", "__managed_directory_next",
    "__managed_directory_filename", "__managed_directory_close",
    "__managed_directory_exists",
    // CPU-parallel marker (parallel-codegen). `__Builtins.parallelBoundary()`
    // lowers to this empty-bodied runtime stub (see EmitMaxonParallelBoundary).
    // It is not I/O, but hand-written CPU-bound `async` task functions must still
    // satisfy the E3073 "this function legitimately yields" contract, so the
    // lowered runtime-symbol name is listed here — CheckAsyncYielding matches
    // MaxonCallRuntimeOp.FunctionName, which carries this symbol, not the
    // "__Builtins.parallelBoundary" spelling (the self-hosted mirror keys off the
    // qualified name instead). `maxon_cpu_count` is deliberately NOT listed: it
    // does not yield; only parallelBoundary is a yield marker.
    "maxon_parallel_boundary",
  ];

  /// Checks that every `async f()` call targets a function that can yield.
  /// A function yields if it contains await/try_await ops, calls a known I/O stub,
  /// or transitively calls a function that yields.
  private static void CheckAsyncYielding(IrModule<MaxonOp> module) {
    // Collect all async call ops first — if none exist, skip the analysis
    var asyncCalls = new List<(MaxonAsyncCallOp Op, IrFunction<MaxonOp> ContainingFunc)>();
    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonAsyncCallOp asyncOp)
            asyncCalls.Add((asyncOp, func));
        }
      }
    }
    if (asyncCalls.Count == 0) return;

    // Build the yields set using fixed-point iteration
    var yields = new HashSet<string>();

    // Build call graph: funcName -> set of callees
    var callGraph = new Dictionary<string, HashSet<string>>();

    foreach (var func in module.Functions) {
      var callees = new HashSet<string>();
      callGraph[func.Name] = callees;
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          switch (op) {
            case MaxonAwaitOp:
            case MaxonTryAwaitOp:
            case MaxonAsyncCallOp:
              yields.Add(func.Name);
              break;
            case MaxonCallOp call:
              // MaxonTryCallOp inherits from MaxonCallOp, so both are handled here.
              if (IoStubs.Contains(call.Callee))
                yields.Add(func.Name);
              else
                callees.Add(call.Callee);
              break;
            case MaxonCallRuntimeOp rtCall:
              if (IoStubs.Contains(rtCall.FunctionName))
                yields.Add(func.Name);
              break;
          }
        }
      }
    }

    // Fixed-point propagation: if a function calls a yielding function, it yields too
    bool changed = true;
    while (changed) {
      changed = false;
      foreach (var (funcName, callees) in callGraph) {
        if (yields.Contains(funcName)) continue;
        foreach (var callee in callees) {
          if (yields.Contains(callee)) {
            yields.Add(funcName);
            changed = true;
            break;
          }
        }
      }
    }

    // Check each async call: callee must be in the yields set or be a known I/O stub
    foreach (var (asyncOp, containingFunc) in asyncCalls) {
      if (!yields.Contains(asyncOp.Callee) && !IoStubs.Contains(asyncOp.Callee)) {
        var sourceText = asyncOp.CallSourceText ?? $"async {asyncOp.Callee}(...)";
        throw new CompileError(
          ErrorCode.AsyncNonYielding,
          $"'{sourceText}' \u2014 function never yields; 'async' is for I/O-concurrent work only",
          asyncOp.CallLine,
          asyncOp.CallColumn) {
          FilePath = containingFunc.SourceFilePath
        };
      }
    }
  }

  /// E3057: a PLAIN `await` on a promise whose thunk throws.
  ///
  /// A throwing thunk hands the awaiting frame an OWNED error on its error path. A plain
  /// `await` has nowhere to put it: the value it yields is the undefined success slot,
  /// and an associated-value payload is released by nobody — the program leaks and exits
  /// 101. `try await` is the only form that can receive the error, which is what
  /// specs/async-await.md has always said. Now it is enforced.
  ///
  /// This is checked HERE rather than in the parser because here it is EXACT: a
  /// MaxonAwaitOp only survives parsing if it is a plain await (a `try await` removes it
  /// and emits a MaxonTryAwaitOp in its place). The parser would have to infer the same
  /// thing from `_inTryContext`, which is true across the whole of `try f(await p)` and
  /// so misses the plain, leaking await sitting in its arguments.
  ///
  /// The gate is the error TYPE, and `Throws` is now derived from it, so the two say the same
  /// thing. They did not always: a promise reconstructed out of a `Promise with T` used to report
  /// Throws=true unconditionally, because its real throws-ness died with the box, and gating on
  /// that bit would have rejected `await` on every STORED non-throwing promise. `Promise with
  /// (T, E)` keeps the error type across storage, so there is one answer again.
  private static void CheckPlainAwaitOfThrowingPromise(IrModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is not MaxonAwaitOp { Promise: MaxonPromise { ErrorType: { } errorType } } awaitOp) continue;

          throw new CompileError(ErrorCode.SemanticThrowingFunctionRequiresTry,
            $"throwing function requires try: 'await' on a promise from a function that throws '{errorType.Name}' drops the error and leaks its payload — use 'try await'",
            awaitOp.AwaitLine,
            awaitOp.AwaitColumn) {
            FilePath = func.SourceFilePath
          };
        }
      }
    }
  }
  /// The await ops, of both forms, reduced to what the linear-await check needs.
  /// `try await` is an await: it consumes the promise's result exactly as a plain one does.
  private readonly record struct AwaitSite(string VarName, int? Line, int? Column);

  private static AwaitSite? AsAwaitSite(MaxonOp op) => op switch {
    MaxonAwaitOp { PromiseVarName: { } name } a => new AwaitSite(name, a.AwaitLine, a.AwaitColumn),
    MaxonTryAwaitOp { PromiseVarName: { } name } t => new AwaitSite(name, t.AwaitLine, t.AwaitColumn),
    _ => null
  };

  /// E3099: a promise awaited a SECOND time.
  ///
  /// `await` is LINEAR. The thunk owns its result and hands it over at the await, so a second
  /// await takes a second reference to a payload the thunk only ever owned once: the two releases
  /// underflow the refcount and free it twice. Making the second await a compile error is what
  /// makes that double-free unrepresentable, rather than something the runtime survives.
  ///
  /// The check is FLOW-SENSITIVE, and it has to be, in both directions:
  ///
  ///   - two awaits of one promise in mutually exclusive branches are each the only await on
  ///     their own path. A lexical "seen it before" check would reject them, and they are fine;
  ///   - ONE await, sitting in a loop, over a promise spawned OUTSIDE the loop, awaits the same
  ///     green thread on every iteration. A lexical check sees a single await and misses it.
  ///
  /// So: from each await, walk the CFG forward and report any await of the same binding that is
  /// REACHABLE from it — including itself, which is what catches the loop. The walk is KILLED at
  /// any ASSIGNMENT to that binding, because assigning it puts a different green thread in it.
  /// That is what makes the central idiom `for p in promises 'each' ... await p ... end` legal:
  /// the loop assigns `p` the next element on every iteration, so its single `await` is one await
  /// per promise, not N awaits of one. It is also what makes the name safe to key on across
  /// scopes — a re-declaration of `p` in a later scope is itself an assignment, and kills.
  private static void CheckLinearAwait(IrModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      // Cheap pre-pass: most functions contain no await at all.
      bool hasAwait = false;
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (AsAwaitSite(op) != null) { hasAwait = true; break; }
        }
        if (hasAwait) break;
      }
      if (!hasAwait) continue;

      var blocksByName = func.Body.Blocks.ToDictionary(b => b.Name);

      foreach (var block in func.Body.Blocks) {
        for (int i = 0; i < block.Operations.Count; i++) {
          if (AsAwaitSite(block.Operations[i]) is not { } first) continue;
          if (FindReachableAwaitOf(first.VarName, block, i + 1, blocksByName) is not { } second) continue;

          throw new CompileError(ErrorCode.SemanticPromiseAlreadyAwaited,
            "this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice",
            second.Line, second.Column) {
            FilePath = func.SourceFilePath
          };
        }
      }
    }
  }

  /// Forward CFG search from (startBlock, startIdx) for another await of the binding `varName`,
  /// killing any path that ASSIGNS it (a re-armed binding holds a new promise, not the awaited one).
  private static AwaitSite? FindReachableAwaitOf(string varName, IrBlock<MaxonOp> startBlock, int startIdx,
      Dictionary<string, IrBlock<MaxonOp>> blocksByName) {
    var queue = new Queue<(IrBlock<MaxonOp> Block, int Index)>();
    queue.Enqueue((startBlock, startIdx));
    // A block may legitimately be entered once from mid-block (the start) and once from its top
    // (around a loop), and the second entry can see ops the first scan started past — including
    // the assignment that kills the path, and the await that proves it double.
    var visited = new HashSet<(string, int)> { (startBlock.Name, startIdx) };

    while (queue.Count > 0) {
      var (block, start) = queue.Dequeue();
      bool killed = false;

      for (int i = start; i < block.Operations.Count; i++) {
        var op = block.Operations[i];

        if (AsAwaitSite(op) is { } site && site.VarName == varName) return site;

        if (op is MaxonAssignOp assign && assign.VarName == varName) { killed = true; break; }
      }
      if (killed) continue;

      foreach (var successor in SuccessorNames(block)) {
        if (!blocksByName.TryGetValue(successor, out var next)) continue;
        if (visited.Add((next.Name, 0))) queue.Enqueue((next, 0));
      }
    }
    return null;
  }

  private static IEnumerable<string> SuccessorNames(IrBlock<MaxonOp> block) {
    if (block.Operations.Count == 0) yield break;
    switch (block.Operations[^1]) {
      case MaxonBrOp br:
        yield return br.Target;
        break;
      case MaxonCondBrOp condBr:
        yield return condBr.ThenBlock;
        yield return condBr.ElseBlock;
        break;
    }
  }

  /// Detects the redundant double-lookup pattern:
  ///   if x.contains(k) 'lbl'
  ///     ... try x.get(k) otherwise <anything> ...
  ///   end 'lbl'
  /// and suggests rewriting as `if let/var v = try x.get(k) 'lbl'`.
  ///
  /// Receivers and keys are matched structurally by canonicalizing each side's
  /// SSA def-use chain into a path string (e.g. "self.byBareName", "p.db.cache",
  /// "bareName"). This catches both bare-local and field-chain receivers/keys.
  ///
  /// The lint suppresses when any intervening op in the then-block could have
  /// invalidated the membership check: a method call on the same receiver path,
  /// a reassignment of any variable in the receiver/key path, or a field-store
  /// to any field in those paths.
  private static void CheckRedundantContainsGet(IrModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      Dictionary<string, IrBlock<MaxonOp>>? blocksByName = null;

      foreach (var block in func.Body.Blocks) {
        if (block.Operations.Count < 2) continue;
        if (block.Operations[^1] is not MaxonCondBrOp condBr) continue;

        // Find the contains call that produced the condition value.
        MaxonCallOp? containsCall = null;
        int containsIdx = -1;
        for (int i = block.Operations.Count - 2; i >= 0; i--) {
          if (block.Operations[i] is MaxonCallOp c && c.Result != null && c.Result.Id == condBr.Condition.Id) {
            containsCall = c;
            containsIdx = i;
            break;
          }
        }
        if (containsCall == null) continue;
        if (containsCall is MaxonTryCallOp) continue;
        if (!HasMethodSuffix(containsCall.Callee, "contains")) continue;
        if (containsCall.Args.Count < 2) continue;

        var containsReceiverPath = BuildAccessPath(containsCall.Args[0], block, containsIdx);
        var containsKeyPath = BuildAccessPath(containsCall.Args[1], block, containsIdx);
        if (containsReceiverPath == null || containsKeyPath == null) continue;

        blocksByName ??= func.Body.Blocks.ToDictionary(b => b.Name);
        if (!blocksByName.TryGetValue(condBr.ThenBlock, out var thenBlock)) continue;

        // Build guard sets of variable names and field names used by the
        // receiver/key paths so we can detect intervening reassignments cheaply.
        var guardedNames = new HashSet<string>();
        var guardedFields = new HashSet<string>();
        SplitPath(containsReceiverPath, guardedNames, guardedFields);
        SplitPath(containsKeyPath, guardedNames, guardedFields);

        for (int i = 0; i < thenBlock.Operations.Count; i++) {
          var op = thenBlock.Operations[i];

          // Reassignment (not initial declaration) of a guarded variable.
          if (op is MaxonAssignOp assign && !assign.IsDeclaration && guardedNames.Contains(assign.VarName)) {
            break;
          }
          // Field-store to a guarded field name.
          if (op is MaxonFieldAssignOp fieldAssign && guardedFields.Contains(fieldAssign.FieldName)) {
            break;
          }

          if (op is not MaxonCallOp innerCall) continue;
          if (innerCall.Args.Count == 0) continue;

          var innerReceiverPath = BuildAccessPath(innerCall.Args[0], thenBlock, i);
          // Unresolvable receiver: be conservative and stop scanning.
          // The receiver might alias the contains() receiver (e.g. when both
          // ultimately reference the same variable but through different SSA
          // paths) — without proof of independence, suppress the lint.
          if (innerReceiverPath == null) break;
          if (innerReceiverPath != containsReceiverPath) continue;

          if (innerCall is MaxonTryCallOp tryGet
              && HasMethodSuffix(tryGet.Callee, "get")
              && tryGet.Args.Count >= 2) {
            var innerKeyPath = BuildAccessPath(tryGet.Args[1], thenBlock, i);
            if (innerKeyPath == containsKeyPath) {
              var containsName = StripCalleeNamespace(containsCall.Callee);
              var getName = StripCalleeNamespace(tryGet.Callee);
              throw new CompileError(ErrorCode.SemanticRedundantContainsGet,
                $"redundant '{containsName}' followed by '{getName}' on '{containsReceiverPath}': use 'if let v = try {containsReceiverPath}.get({containsKeyPath})' (or 'if var') instead \u2014 performs one lookup instead of two",
                containsCall.CallLine, containsCall.CallColumn) {
                FilePath = func.SourceFilePath
              };
            }
          }

          // Any other call on the same receiver path suppresses the lint.
          break;
        }
      }
    }
  }

  /// Walks SSA backward from `value` through the operations of `block` (only
  /// considering ops at index < limitIdx) to produce a canonical access path
  /// like "p.db.cache" or "bareName". Returns null if the chain hits an op
  /// that isn't a var-ref / param / field-access (e.g. a call result or
  /// arithmetic expression \u2014 those aren't safely matchable).
  private static string? BuildAccessPath(MaxonValue value, IrBlock<MaxonOp> block, int limitIdx) {
    // Find the producer in this block.
    MaxonOp? producer = null;
    for (int i = limitIdx - 1; i >= 0; i--) {
      var op = block.Operations[i];
      if (OpProducesValue(op, value.Id)) { producer = op; break; }
    }
    if (producer == null) return null;

    return producer switch {
      MaxonStructVarRefOp svr => svr.VarName,
      MaxonVarRefOp vr => vr.VarName,
      MaxonStructParamOp sp => sp.Name,
      MaxonFieldAccessOp fa => BuildAccessPath(fa.StructValue, block, limitIdx) is string root ? $"{root}.{fa.FieldName}" : null,
      _ => null
    };
  }

  private static bool OpProducesValue(MaxonOp op, int id) => op switch {
    MaxonStructVarRefOp svr => svr.Result.Id == id,
    MaxonVarRefOp vr => vr.Result.Id == id,
    MaxonStructParamOp sp => sp.Result.Id == id,
    MaxonFieldAccessOp fa => fa.Result.Id == id,
    _ => false
  };

  /// Splits an access path "root.f1.f2" into root variable name (-> roots)
  /// and trailing field names (-> fields). For a bare "root", only roots is updated.
  private static void SplitPath(string path, HashSet<string> roots, HashSet<string> fields) {
    int dot = path.IndexOf('.');
    if (dot < 0) {
      roots.Add(path);
      return;
    }
    roots.Add(path[..dot]);
    foreach (var seg in path[(dot + 1)..].Split('.')) fields.Add(seg);
  }

  /// Matches `<Type>.<method>` or `<Type>.<method>$<arg>` (named-arg overload variants).
  private static bool HasMethodSuffix(string callee, string method) {
    int dot = callee.LastIndexOf('.');
    if (dot < 0) return false;
    var tail = callee.AsSpan(dot + 1);
    if (tail.SequenceEqual(method.AsSpan())) return true;
    return tail.StartsWith(method.AsSpan()) && tail.Length > method.Length && tail[method.Length] == '$';
  }

  /// Drops the leading namespace segment (e.g. `stdlib.`) and the `$key` / `$index`
  /// named-arg suffix used in overload-resolved callee names. Mirrors FormatCalleeName.
  private static string StripCalleeNamespace(string callee) {
    int dollar = callee.IndexOf('$');
    var trimmed = dollar >= 0 ? callee[..dollar] : callee;
    int dot = trimmed.IndexOf('.');
    return dot >= 0 ? trimmed[(dot + 1)..] : trimmed;
  }
}
