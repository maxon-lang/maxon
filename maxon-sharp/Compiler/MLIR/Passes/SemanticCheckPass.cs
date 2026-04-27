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
