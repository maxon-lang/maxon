using MaxonSharp.Compiler;

namespace MaxonSharp.Testing;

/// <summary>
/// WHICH TARGETS' BINARIES THIS HOST CAN EXECUTE — one fact, asked by everything that follows from it.
///
/// <para>⭐ THE FACT AND ITS CONSEQUENCES ARE SEPARATE MEMBERS ON PURPOSE, because they are asked by
/// different callers for different reasons and must never become two rules.
/// <see cref="WhyCannotRun"/> is the fact; <see cref="MintRefusalFor"/> is what the fact means for a
/// COMMITTED GOLDEN, and the spec runner asks the fact directly to decide what a test that could not
/// be run is worth. "This run cannot happen" and "this golden may not be minted" are the same machine
/// limit read twice, and this project's signature bug is one fact written down twice.</para>
///
/// <para>⚖ USER RULING, 2026-08-02 (the mint half): a golden minted from a host whose OS differs from
/// the target's is REFUSED, naming both. Cross-compiling itself is untouched and always was: this
/// bootstrap emits a real PE32+ from a Mac and a real Mach-O from Windows, and that is a supported
/// thing to do. What is withdrawn is the claim that the output of such a compile may be COMMITTED as
/// the target's reference.</para>
///
/// <para>⭐ WHY THE OS AND NOT SOMETHING ELSE. <c>maxon-sharp</c> launches every test binary with
/// <c>Process.Start</c> and has no runner, no emulator and no VM — so a foreign-OS binary cannot be
/// executed here at all. ⚠ THAT IS THE ANSWER FOR THIS COMPILER, NOT A GENERAL LAW: "can I spawn
/// this?" is not the same question as "is the OS the same?". <c>maxon-shv2</c> answers it with a
/// TABLE (<c>externalRunnerFor</c>) and correctly ALLOWS <c>arm64-linux</c> (OrbStack) and
/// <c>wasm32-wasi</c> (wasmtime) — cross-OS targets it genuinely can execute. Should this compiler
/// ever learn to reach a runner, the table goes HERE, in this one function, and every consequence
/// below follows without being told.</para>
///
/// <para>MEASURED on an arm64-macOS host at <c>70861315c</c>, before this rule existed, and stated as
/// what it actually was — twice, on a clean tree: <c>spec-test --target=x64-windows --update-required</c>
/// regenerated every <c>RequiredIR</c>/<c>maxoncstderr</c> block whose regeneration needs only a
/// COMPILE, then ABORTED with an unhandled <c>Win32Exception (13) Permission denied</c> — exit 134, a
/// stack trace where a diagnostic belongs — at the first block whose regeneration has to RUN the
/// program. ⚠ Nothing was committed by that run, and the reason matters: <c>UpdateRequiredInSpecFiles</c>
/// writes a spec file only when the regenerated block DIFFERS, and none did, because the frontend
/// really does follow <c>--target</c> since <c>bc21be3e1</c>. So the measured damage is not bytes on
/// disk — it is that the compare which decided "no change" was made against output nobody could run,
/// and would have written had it differed. That is the whole of what the mint rule refuses.</para>
///
/// <para>⚠ ARCHITECTURE IS DELIBERATELY NOT PART OF THIS RULE, and it is not an oversight. A host
/// commonly runs a foreign architecture's binaries for its own OS — Windows-on-ARM runs x64 under
/// emulation — so refusing on arch would refuse runs and mints that are perfectly valid. The one case
/// it leaves open is an x64 Mac minting <c>arm64-macos</c>, where the emulation runs the other way;
/// that is a real hole, and a narrower rule than "OS" cannot state it (see the report on PLAN row
/// G11). It is not silently absorbed here: it would have to be its own decision.</para>
///
/// <para>⭐ THE MECHANISM THE MINT RULE WAS FILED AGAINST IS ALREADY FIXED, and the rule is what stops
/// it RECURRING rather than what repairs it. Until <c>bc21be3e1</c> (row A1u, 2026-07-31) the stdlib
/// was parsed once for the BUILD MACHINE and cached, so <c>stdlib/Process.maxon</c>'s
/// <c>#if os(Windows)</c> <c>ExitCode</c> reached every cross-compile with the host's range — a
/// Windows-hosted <c>--target=arm64-macos</c> mint would have written the Windows bound into the
/// macOS lane. That door is shut and machine-checked on every build
/// (<see cref="Compiler.StdlibTargetSelfTest"/>). This one is shut against the NEXT such leak, which
/// no amount of frontend correctness can promise: a golden nothing executed is a golden nothing
/// checked.</para>
/// </summary>
public static class TargetRunHost {
  /// <summary>
  /// Why this host cannot EXECUTE <paramref name="target"/>'s binaries, or null when it can.
  ///
  /// <para>Returns the reason rather than printing or throwing, because its callers act on it
  /// differently and none of them wants a message on a stream: the spec runner folds it into a
  /// per-test outcome, the mint rule wraps it in what it means for a golden, and the two build
  /// self-tests assert on its text.</para>
  /// </summary>
  public static string? WhyCannotRun(CompileTarget target) {
    var host = CompileTarget.Native;
    if (host.Os == target.Os) return null;

    return $"this host runs {host.Os} and {target.Triple} is a {target.Os} binary. This compiler "
      + "launches every test binary with Process.Start and owns no emulator, VM or external runner, "
      + $"so nothing here can execute one — only a {target.Os} host can";
  }

  /// <summary>
  /// The refusal for minting <paramref name="target"/>'s goldens on this machine, or null when the
  /// mint is allowed. A CONSEQUENCE of <see cref="WhyCannotRun"/>, never a second predicate.
  ///
  /// <para>Returns the message rather than printing it, for the same reason
  /// <see cref="CompileTarget.Unsupported"/> returns an error: it is asked at two doors that report
  /// differently — the <c>--update-required</c> flag, which refuses the whole run before anything
  /// happens, and the golden WRITE itself, which records a per-golden failure — and a rule that
  /// printed could only serve one of them.</para>
  /// </summary>
  public static string? MintRefusalFor(CompileTarget target) {
    if (WhyCannotRun(target) is not { } why) return null;

    return $"a committed golden is only worth the run that validated it, and {why}. Every golden a "
      + "mint here produced would therefore come from a compile nobody ran. Cross-compiling for "
      + $"{target.Triple} from here is unaffected and still works.";
  }
}
