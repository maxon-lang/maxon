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
}
