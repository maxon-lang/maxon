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

    // Check that no `Array.resize` would expose slots that hold no element
    CheckDenseArrayGrowth(module);
  }

  /// The stdlib method that lengthens an array by publishing a longer length over zeroed slots.
  private const string ArrayResizeCallee = "stdlib.Array.resize";

  /// <summary>
  /// E3106: `Array.resize` on an array whose ELEMENT is a heap pointer.
  ///
  /// `resize` grows by publishing a longer length over slots the allocator zeroed, and a zero is a
  /// real element only while the element lives INLINE in the buffer. A managed element — a struct,
  /// a String, a nested container, a boxed union — lives there as a refcounted POINTER, so those
  /// slots are NULL: an absence, not a value. Maxon has no default constructor, so `resize` has
  /// nothing correct to put in them, and what it hands back is an array whose `count()` answers N
  /// while `get(0)` throws.
  ///
  /// This runs BEFORE MonomorphizationPass on purpose, and that ordering IS the rule's scope.
  /// Here a user's `Array with Pair` is already concrete, while the same call inside a generic body
  /// still reads `Array with Element` — and only the first is a claim about a known element type.
  /// One generic body serves every instantiation, and the sparse slot tables in stdlib/Map.maxon
  /// and stdlib/Set.maxon are built through exactly that path: they track occupancy in a parallel
  /// `states` column and never read a slot they did not write.
  /// <see cref="ManagedElementInfo.FromElementType"/> already draws that same line — it answers
  /// Unresolved for an unbound type parameter — which is why it is the predicate here rather than
  /// a second one written out again.
  /// </summary>
  private static void CheckDenseArrayGrowth(IrModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is not MaxonCallOp call || call.Callee != ArrayResizeCallee) continue;
          if (call.Args.Count == 0 || call.Args[0] is not MaxonStruct receiver) continue;
          var element = ResolveArrayElementType(module, receiver.TypeName);
          if (element == null || !ManagedElementInfo.FromElementType(element).IsStructElement) continue;

          throw new CompileError(ErrorCode.SemanticArrayResizeManagedElement,
            $"'resize' cannot grow an array of '{element.Name}': a grown slot holds NO element — "
            + "Maxon has no default constructor — so 'count()' would not agree with 'get()'. "
            + "Append with 'push(value)', grow with 'growFilled(newLength, value:)', "
            + "shrink with 'truncate(newLength)'",
            call.CallLine, call.CallColumn) { FilePath = func.SourceFilePath };
        }
      }
    }
  }

  /// <summary>
  /// The type an `Array with X` instantiation binds to its element parameter, or null when
  /// <paramref name="arrayTypeName"/> is not such an instantiation. Resolved through TypeDefs
  /// because an alias records the element as it was WRITTEN: a name first met during pre-scanning
  /// is recorded as a placeholder, and only TypeDefs turns it back into the real definition. Skip
  /// that step and a ranged primitive registered as a struct reads as managed.
  /// </summary>
  private static IrType? ResolveArrayElementType(IrModule<MaxonOp> module, string arrayTypeName) {
    if (!module.TypeAliasSources.TryGetValue(arrayTypeName, out var alias)) return null;
    // "Element" is the name stdlib/Array.maxon gives the parameter (`type Array uses Element`).
    if (alias.TypeParams == null || !alias.TypeParams.TryGetValue("Element", out var element)) return null;
    // An UNBOUND type parameter is not a type to look up, and must be rejected BEFORE the TypeDefs
    // resolution below: its name is whatever the generic declared ("Key", "Value", "T"), so a user
    // type of that name would answer for it and gate an instantiation that never binds it — the
    // `Array with Key` inside a `Map` would read as managed the moment a program declared `type Key`.
    if (element is IrTypeParameterType) return null;
    return module.TypeDefs.TryGetValue(element.Name, out var canonical) ? canonical : element;
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

  /// ⭐ THE SEEDS OF THE E3073 YIELD CLOSURE: runtime entries (never user-defined functions) a call to
  /// which can HAND THIS OS THREAD TO ANOTHER GREEN THREAD. Every yielding stdlib entry point —
  /// `File.exists`, `Subprocess.run`, `Runtime.yield` — is DERIVED from these by the fixed point in
  /// CheckAsyncYielding walking the call graph down to one of them.
  ///
  /// ⚠ IT IS NOT AN I/O ROSTER, THOUGH IT WAS CALLED `IoStubs` AND ITS HEADER CLAIMED THESE "call
  /// __io_submit_*" — false of `maxon_sleep` (a timer park), false of `maxon_yield` (a run-queue
  /// rotation), and false of `maxon_parallel_boundary` (not a suspension at all, and listed anyway).
  /// The membership question is the one above, and each entry below states its own answer to it.
  /// Reading the old name as the rule is what makes a yield look like an odd guest here rather than
  /// the most obvious member there is.
  ///
  /// Entries are the LOWERED runtime symbols and the synthetic `__managed_*` callee names, because
  /// CheckAsyncYielding matches MaxonCallRuntimeOp.FunctionName and MaxonCallOp.Callee — not the
  /// `__Builtins.foo` spelling (the self-hosted mirror, SemanticCheck.ioYieldBuiltinSet, keys off the
  /// qualified name instead and is therefore spelled differently on purpose).
  private static readonly HashSet<string> YieldingRuntimeEntries = [
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
    // Cooperative yield. `Runtime.yield()` lowers through `__Builtins.yield()` to this runtime
    // symbol, which puts the caller on the BACK of the run queue and hands the M to the next
    // runnable green thread (RuntimeEmitter.EmitMaxonYield). The plainest possible yes to the
    // header's question.
    "maxon_yield",
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
              if (YieldingRuntimeEntries.Contains(call.Callee))
                yields.Add(func.Name);
              else
                callees.Add(call.Callee);
              break;
            case MaxonCallRuntimeOp rtCall:
              if (YieldingRuntimeEntries.Contains(rtCall.FunctionName))
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
      if (!yields.Contains(asyncOp.Callee) && !YieldingRuntimeEntries.Contains(asyncOp.Callee)) {
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
  ///
  /// GreenThreadId is WHICH THREAD is consumed — the key linearity is a property of. VarName is
  /// WHICH BINDING it was read from — what re-arms it. Two different facts; see MaxonTryAwaitOp.
  private readonly record struct AwaitSite(int GreenThreadId, string VarName, int? Line, int? Column);

  /// Both await forms answer IMaxonAwaitOp, so this asks the INTERFACE rather than naming the two
  /// op classes: a third await form, or a fifth fact, cannot be silently missed here.
  private static AwaitSite? AsAwaitSite(MaxonOp op) =>
    op is IMaxonAwaitOp { PromiseGreenThreadId: { } greenThreadId, PromiseVarName: { } varName } awaitOp
      ? new AwaitSite(greenThreadId, varName, awaitOp.AwaitLine, awaitOp.AwaitColumn)
      : null;

  /// E3100: a green thread awaited a SECOND time.
  ///
  /// `await` is LINEAR. The thunk owns its result and hands it over at the await, so a second
  /// await takes a second reference to a payload the thunk only ever owned once: the two releases
  /// underflow the refcount and free it twice. Making the second await a compile error is what
  /// makes that double-free unrepresentable, rather than something the runtime survives.
  ///
  /// It keys on the GREEN THREAD, not on the identifier text. Keying on the name was a hole you
  /// could drive an alias through — `let q = p` gives one thread two names, and `await p; await q`
  /// compiled clean and double-freed at runtime. MaxonPromise.GreenThreadId is minted once at the
  /// `async` spawn and carried through every alias and every cross-block re-tag, so the two names
  /// resolve to one key.
  ///
  /// The check is FLOW-SENSITIVE, and it has to be, in both directions:
  ///
  ///   - two awaits of one promise in mutually exclusive branches are each the only await on
  ///     their own path. A lexical "seen it before" check would reject them, and they are fine;
  ///   - ONE await, sitting in a loop, over a promise spawned OUTSIDE the loop, awaits the same
  ///     green thread on every iteration. A lexical check sees a single await and misses it.
  ///
  /// So: from each await, walk the CFG forward and report any await of the same THREAD that is
  /// REACHABLE from it — including itself, which is what catches the loop.
  ///
  /// The walk is KILLED when the thread has no name left to be awaited through: every binding
  /// that awaits it has been REASSIGNED on this path, and a reassigned binding holds a different
  /// thread. That is what keeps `for p in promises 'each' … await p … end` legal — the loop
  /// re-arms `p` every iteration, so its single `await` is one await per promise rather than N
  /// awaits of one — and it is why the kill tracks the whole set of awaiting names rather than
  /// just the one this walk started from: with an alias in play, rebinding `p` does not end the
  /// thread's life while `q` still names it, and `await q` afterwards is still a double free.
  ///
  /// ⚠ BOUNDARY — what this does NOT catch, deliberately. It sees awaits of BINDINGS inside ONE
  /// function's CFG. A promise that ESCAPES that is beyond it, and awaiting such a promise twice
  /// still double-frees at runtime:
  ///
  ///   - the same container SLOT twice (`await arr[0]; await arr[0]`), or a struct FIELD
  ///     (`await h.pr`) — the box holds a runtime handle, and which thread is in it is not a
  ///     static fact;
  ///   - a promise passed as a CALL ARGUMENT to a callee that awaits it, and awaited in the
  ///     caller too — the second await is in another frame, so no CFG path joins them.
  ///
  /// All of these need ownership tracked THROUGH storage and across frames, which the bootstrap
  /// does not have; it is shv2's ownership milestone (P1.5). They are missed, never mis-reported:
  /// a promise out of storage gets a fresh GreenThreadId, so it is never spuriously EQUAL to
  /// another and the check stays silent rather than guessing. Do not "fix" that by widening the
  /// key — an over-rejection here would break `for p in promises`, which is the central idiom.
  private static void CheckLinearAwait(IrModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      // Most functions contain no await at all; collecting the sites IS the pre-pass.
      var sites = new List<(IrBlock<MaxonOp> Block, int Index, AwaitSite Site)>();
      foreach (var block in func.Body.Blocks) {
        for (int i = 0; i < block.Operations.Count; i++) {
          if (AsAwaitSite(block.Operations[i]) is { } site) sites.Add((block, i, site));
        }
      }
      if (sites.Count == 0) continue;

      var blocksByName = func.Body.Blocks.ToDictionary(b => b.Name);

      // Every binding through which a given thread is awaited. A binding that is never awaited
      // cannot host a second await, so it cannot keep the thread alive for this check either.
      var awaitingNames = new Dictionary<int, List<string>>();
      foreach (var (_, _, site) in sites) {
        var names = awaitingNames.TryGetValue(site.GreenThreadId, out var existing)
          ? existing
          : awaitingNames[site.GreenThreadId] = [];
        if (!names.Contains(site.VarName)) names.Add(site.VarName);
      }

      foreach (var (block, index, first) in sites) {
        var names = awaitingNames[first.GreenThreadId];
        if (FindReachableAwaitOf(first.GreenThreadId, names, block, index + 1, blocksByName) is not { } second)
          continue;

        throw new CompileError(ErrorCode.SemanticPromiseAlreadyAwaited,
          "this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice",
          second.Line, second.Column) {
          FilePath = func.SourceFilePath
        };
      }
    }
  }

  /// Forward CFG search from (startBlock, startIdx) for another await of green thread
  /// `greenThreadId`. `awaitingNames` is every binding that awaits that thread anywhere in this
  /// function; a path dies once ALL of them have been reassigned along it, because from there on
  /// the thread has no name left through which a second await could reach it.
  ///
  /// The live set is carried in the search state, not just tracked per-block: the same block
  /// reached with a different set of bindings still live is a different question, and answering
  /// it once for both would either miss a double await or invent one.
  private static AwaitSite? FindReachableAwaitOf(int greenThreadId, List<string> awaitingNames,
      IrBlock<MaxonOp> startBlock, int startIdx, Dictionary<string, IrBlock<MaxonOp>> blocksByName) {
    // Live names as an index set over `awaitingNames`, canonicalised to a string so the visited
    // set can dedupe on it. It only ever SHRINKS along a path, which is what bounds the walk.
    var allLive = Enumerable.Range(0, awaitingNames.Count).ToHashSet();

    static string LiveKey(HashSet<int> live) => string.Join(',', live.Order());

    var queue = new Queue<(IrBlock<MaxonOp> Block, int Index, HashSet<int> Live)>();
    queue.Enqueue((startBlock, startIdx, allLive));
    // A block may legitimately be entered once from mid-block (the start) and once from its top
    // (around a loop), and the second entry can see ops the first scan started past — including
    // the assignment that kills the path, and the await that proves it double.
    var visited = new HashSet<(string, int, string)> { (startBlock.Name, startIdx, LiveKey(allLive)) };

    while (queue.Count > 0) {
      var (block, start, live) = queue.Dequeue();
      bool killed = false;

      for (int i = start; i < block.Operations.Count; i++) {
        var op = block.Operations[i];

        if (AsAwaitSite(op) is { } site && site.GreenThreadId == greenThreadId) return site;

        if (op is MaxonAssignOp assign) {
          int slot = awaitingNames.IndexOf(assign.VarName);
          if (slot < 0) continue;

          // Copy-on-write: sibling paths out of this block must not see this path's kill.
          live = [.. live];
          live.Remove(slot);
          if (live.Count == 0) { killed = true; break; }
        }
      }
      if (killed) continue;

      foreach (var successor in SuccessorNames(block)) {
        if (!blocksByName.TryGetValue(successor, out var next)) continue;
        if (visited.Add((next.Name, 0, LiveKey(live)))) queue.Enqueue((next, 0, live));
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
      case MaxonSwitchOp switchOp:
        foreach (var target in switchOp.Intervals.Select(i => i.TargetBlock).Distinct())
          yield return target;
        yield return switchOp.DefaultBlock;
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
