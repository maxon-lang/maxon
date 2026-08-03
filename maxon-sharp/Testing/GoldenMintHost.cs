using MaxonSharp.Compiler;

namespace MaxonSharp.Testing;

/// <summary>
/// WHICH TARGETS THIS HOST MAY MINT A COMMITTED GOLDEN FOR — one rule, asked at every door that writes
/// one.
///
/// <para>⚖ USER RULING, 2026-08-02: a golden minted from a host whose OS differs from the target's is
/// REFUSED, naming both. Cross-compiling itself is untouched and always was: this bootstrap emits a
/// real PE32+ from a Mac and a real Mach-O from Windows, and that is a supported thing to do. What is
/// withdrawn is the claim that the output of such a compile may be COMMITTED as the target's
/// reference.</para>
///
/// <para>⭐ WHY THE OS AND NOT SOMETHING ELSE. A golden is only worth what validated it, and in this
/// compiler exactly one thing does: the test RAN and produced the expected answer. `maxon-sharp`
/// launches every test binary with <c>Process.Start</c> and has no runner, no emulator and no VM — so
/// a foreign-OS binary cannot be executed here at all, and every golden a cross-OS run could write
/// would be minted from a compile nobody ever ran.</para>
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
/// and would have written had it differed. That is the whole of what this rule refuses.</para>
///
/// <para>⚠ ARCHITECTURE IS DELIBERATELY NOT PART OF THIS RULE, and it is not an oversight. A host
/// commonly runs a foreign architecture's binaries for its own OS — Windows-on-ARM runs x64 under
/// emulation — so refusing on arch would refuse mints that are perfectly validated. The one case it
/// leaves open is an x64 Mac minting <c>arm64-macos</c>, where the emulation runs the other way; that
/// is a real hole, and a narrower rule than "OS" cannot state it (see the report on PLAN row G11).
/// It is not silently absorbed here: it would have to be its own decision.</para>
///
/// <para>⭐ THE MECHANISM THIS ROW WAS FILED AGAINST IS ALREADY FIXED, and this rule is what stops it
/// RECURRING rather than what repairs it. Until <c>bc21be3e1</c> (row A1u, 2026-07-31) the stdlib was
/// parsed once for the BUILD MACHINE and cached, so <c>stdlib/Process.maxon</c>'s
/// <c>#if os(Windows)</c> <c>ExitCode</c> reached every cross-compile with the host's range — a
/// Windows-hosted <c>--target=arm64-macos</c> mint would have written the Windows bound into the
/// macOS lane. That door is shut and machine-checked on every build
/// (<see cref="Compiler.StdlibTargetSelfTest"/>). This one is shut against the NEXT such leak, which
/// no amount of frontend correctness can promise: a golden nothing executed is a golden nothing
/// checked.</para>
/// </summary>
public static class GoldenMintHost {
  /// <summary>
  /// The refusal for minting <paramref name="target"/>'s goldens on this machine, or null when the
  /// mint is allowed.
  ///
  /// <para>Returns the message rather than printing it, for the same reason
  /// <see cref="CompileTarget.Unsupported"/> returns an error: it is asked at two doors that report
  /// differently — the <c>--update-required</c> flag, which refuses the whole run before anything
  /// happens, and the golden WRITE itself, which records a per-golden failure — and a rule that
  /// printed could only serve one of them.</para>
  /// </summary>
  public static string? RefusalFor(CompileTarget target) {
    var host = CompileTarget.Native;
    if (host.Os == target.Os) return null;

    return $"this host runs {host.Os} and the goldens asked for are {target.Os}'s ({target.Triple}). "
      + "A committed golden is only worth the run that validated it, and this compiler cannot execute "
      + $"a {target.Os} binary on a {host.Os} host — so every golden a mint here produced would come "
      + "from a compile nobody ran. Mint them on a "
      + $"{target.Os} host. Cross-compiling for {target.Triple} from here is unaffected and still works.";
  }
}
