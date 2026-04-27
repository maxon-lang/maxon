using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;

namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// Shared (cross-backend) implementation of CPU-fault handling for green threads.
/// The OS delivers SIGSEGV / SIGFPE / EXCEPTION_ACCESS_VIOLATION / etc. to the worker
/// thread; the per-backend fault thunk (registered by InstallFaultHandler) extracts the
/// fault context and tail-calls __gt_fault_handler below. The shared handler decides
/// what to do, then control returns to the per-backend epilog which rewrites the
/// OS-provided register context and resumes the thread.
///
/// Resume policy in this phase: every fault becomes process death with a clean
/// diagnostic. Forward-compatible with later panic/recover work — only the redirect
/// target inside __gt_fault_handler changes (from "diagnostic" to "unwinder").
/// </summary>
public partial class RuntimeEmitter {

  // Static panic message strings — referenced by `LeaSymdata` from the diagnostic.
  // Defined unconditionally so the runtime always has them available, even for
  // fault codes the per-backend handler does not currently produce.
  public void EmitFaultHandlerData() {
    _b.DefineSymdata("__gt_panic_msg_nil_deref",
      "panic: nil pointer or invalid memory access\n\0"u8.ToArray());
    _b.DefineSymdata("__gt_panic_msg_div_zero",
      "panic: integer divide by zero\n\0"u8.ToArray());
    _b.DefineSymdata("__gt_panic_msg_int_overflow",
      "panic: integer overflow\n\0"u8.ToArray());
    _b.DefineSymdata("__gt_panic_msg_stack_overflow",
      "panic: stack overflow\n\0"u8.ToArray());
    _b.DefineSymdata("__gt_panic_msg_other",
      "panic: unhandled CPU fault\n\0"u8.ToArray());
  }

  /// <summary>
  /// Shared fault-handler body. The per-backend fault thunk packs the OS context into
  /// (Arg0=faultCode, Arg1=faultRip, Arg2=faultRsp, Arg3=faultFp) and tail-calls here.
  ///
  /// On entry the worker thread is running on the gsignal/system stack (macOS sigaltstack
  /// or Windows VEH callback). Argument registers carry the faulting context — the
  /// faulting gt's own stack is NOT the active stack at this point.
  ///
  /// Returns in VReg.Ret one of:
  ///   0                              — recover via gt.fault_redirect_{rip,rsp,fp}
  ///   GtLayout.FaultCodeDontRecover  — chain to OS default disposition
  /// </summary>
  public void EmitGtFaultHandler() {
    // __gt_fault_handler(faultCode, faultRip, faultRsp, faultFp) -> sentinel
    // Frame slots: 0..3 = spilled args, 4 = gt, 5 = msg pointer.
    _b.FunctionStart("__gt_fault_handler", 4, 0x60);

    EmitChooseFaultMsg();

    // gt = P->currentGt; null means no gt is running on this worker, which is
    // impossible-by-construction — chain to the OS default rather than nil-deref.
    _b.LoadCurrentP(VReg.Scratch0);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, POffCurrentGt);
    _b.StoreLocal(4, VReg.Scratch0);
    var gtOkLabel = UniqueLabel("fault_gt_ok");
    _b.JumpIfNonZero(VReg.Scratch0, gtOkLabel);
    _b.MovRegImm(VReg.Ret, FaultCodeDontRecover);
    _b.FunctionEnd();
    _b.DefineLabel(gtOkLabel);

    // Stash diagnostic info on the gt: { fault_rip, fault_msg }.
    _b.LoadLocal(VReg.Scratch0, 4);
    _b.LoadLocal(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch0, GtOffFaultRip, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch2, 5);
    _b.StoreIndirect(VReg.Scratch0, GtOffFaultMsg, VReg.Scratch2);

    // Redirect target: pc=__gt_fault_diagnostic, fp=0, sp=faultRsp (intact stack)
    // OR gt.stack_base+4096 for stack-overflow (exhausted stack needs a clean spot).
    _b.MovRegImm(VReg.Scratch2, 0);
    _b.StoreIndirect(VReg.Scratch0, GtOffFaultRedirectFp, VReg.Scratch2);
    _b.LeaGlobal(VReg.Scratch2, "__gt_fault_diagnostic_addr");
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch2, 0);
    _b.StoreIndirect(VReg.Scratch0, GtOffFaultRedirectRip, VReg.Scratch2);

    var notStackOvfLabel = UniqueLabel("fault_not_stkovf");
    var rspChosenLabel   = UniqueLabel("fault_rsp_chosen");
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.CmpRegImm(VReg.Scratch1, FaultCodeStackOverflow);
    _b.JumpIf(Condition.NotEqual, notStackOvfLabel);

    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, GtOffStackBase);
    _b.AddRegImm(VReg.Scratch2, 4096);
    _b.StoreIndirect(VReg.Scratch0, GtOffFaultRedirectRsp, VReg.Scratch2);
    _b.Jump(rspChosenLabel);

    _b.DefineLabel(notStackOvfLabel);
    _b.LoadLocal(VReg.Scratch2, 2);
    _b.StoreIndirect(VReg.Scratch0, GtOffFaultRedirectRsp, VReg.Scratch2);

    _b.DefineLabel(rspChosenLabel);

    // Recover sentinel — the per-backend epilog rewrites the OS context with the
    // values we just wrote into gt.fault_redirect_*.
    _b.MovRegImm(VReg.Ret, 0);
    _b.FunctionEnd();
  }

  /// <summary>
  /// Match faultCode (slot 0) against each known FaultCode*, store the matching
  /// __gt_panic_msg_* address in slot 5. Falls back to the "other" message for
  /// fault codes outside the known set.
  /// </summary>
  private void EmitChooseFaultMsg() {
    var chosenLabel = UniqueLabel("fault_msg_chosen");
    var cases = new (long code, string symdata)[] {
      (FaultCodeNilDeref,      "__gt_panic_msg_nil_deref"),
      (FaultCodeDivZero,       "__gt_panic_msg_div_zero"),
      (FaultCodeIntOverflow,   "__gt_panic_msg_int_overflow"),
      (FaultCodeStackOverflow, "__gt_panic_msg_stack_overflow"),
    };

    _b.LoadLocal(VReg.Scratch1, 0);
    var matchLabels = new string[cases.Length];
    for (int i = 0; i < cases.Length; i++) {
      matchLabels[i] = UniqueLabel($"fault_msg_{i}");
      _b.CmpRegImm(VReg.Scratch1, cases[i].code);
      _b.JumpIf(Condition.Equal, matchLabels[i]);
    }

    _b.LeaSymdata(VReg.Scratch1, "__gt_panic_msg_other");
    _b.Jump(chosenLabel);

    for (int i = 0; i < cases.Length; i++) {
      _b.DefineLabel(matchLabels[i]);
      _b.LeaSymdata(VReg.Scratch1, cases[i].symdata);
      if (i != cases.Length - 1) _b.Jump(chosenLabel);
    }

    _b.DefineLabel(chosenLabel);
    _b.StoreLocal(5, VReg.Scratch1);
  }

  /// <summary>
  /// Diagnostic printer. Reached by the OS resuming the worker thread at this address
  /// after the per-backend fault-handler epilog rewrote the context.
  ///
  /// Writes gt.fault_msg to stderr and exits with status 1. Intentionally does NOT
  /// call mrt_panic to avoid mrt_panic's stack walk — the RBP chain is fresh
  /// (we redirected with FP=0), so any walk produces meaningless garbage. Once panic/
  /// recover lands, this is the function that gets replaced with a defer-chain unwinder.
  /// </summary>
  public void EmitGtFaultDiagnostic() {
    // Stand up a normal frame so RBP is valid for any helper that walks it.
    _b.FunctionStart("__gt_fault_diagnostic", 0, 0x20);

    // Load gt = P->currentGt.
    _b.LoadCurrentP(VReg.Scratch0);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, POffCurrentGt);

    // Write the panic message to stderr.
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, GtOffFaultMsg);
    _b.Call(_b.WriteStderrLabel);

    // Exit the process with status 1.
    _b.MovRegImm(VReg.Arg0, 1);
    _b.CallImport("os_exit");
    // os_exit does not return.
    _b.FunctionEnd();
  }

  /// <summary>
  /// Define the global that holds the absolute address of __gt_fault_diagnostic.
  /// The fault handler reads it to compute the redirect RIP. We store it in a global
  /// (resolved at link time via a runtime-startup write) rather than via LeaGlobal of
  /// __gt_fault_diagnostic itself, because we need an ABSOLUTE address (RIP for the
  /// kernel to resume at), not a PC-relative LEA result.
  /// </summary>
  public void EmitGtFaultDiagnosticAddrGlobal() {
    _b.DefineGlobal("__gt_fault_diagnostic_addr", 8, 0);
  }
}
