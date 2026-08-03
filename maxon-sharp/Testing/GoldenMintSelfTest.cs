using MaxonSharp.Compiler;

namespace MaxonSharp.Testing;

/// <summary>
/// The self-test for <see cref="TargetRunHost.MintRefusalFor"/>, wired as `maxon golden-mint-selftest` and run by
/// every `dotnet build`, in the same idiom as `stdlib-target-selftest` and for a related reason.
///
/// <para>NO SPEC CASE CAN REACH THIS RULE. A spec case is Maxon source that the runner compiles; it
/// cannot ask the runner which host it is on, and the suite is only ever run natively anyway
/// (`scripts/cross-target-gate.sh` reaches arm64-macOS by ssh-ing to a Mac and running there), so on
/// every machine that runs the corpus host == target and the rule's refusing arm is never taken. A
/// case would pass for exactly the reason it passed before the rule existed.</para>
///
/// <para>⚠ WHAT THIS PINS AND WHAT IT DOES NOT. It pins the RULE — which targets are refused, which
/// are allowed, and that the refusal names both operating systems so the reader can act on it. It does
/// not pin the two CALL SITES, and deliberately does not try: the only way to observe those is to
/// invoke `spec-test` itself, and a guard that did so would run a whole foreign-target suite on the
/// build machine the moment the wiring it was checking went missing — a check whose failure mode is
/// worse than the defect. The sites are covered instead by there being TWO of them, at different
/// depths (the `--update-required` flag in <c>Program.RunSpecTests</c>, and every golden write in
/// <c>TestRunner.CheckFragmentGolden</c>), so a mint cannot escape by losing one.</para>
///
/// <para>⭐ IT PINS THE SHARED TABLE, ONCE. <see cref="TargetRunHost.MintRefusalFor"/> returns null
/// exactly when <see cref="TargetRunHost.WhyCannotRun"/> does, so the rows below classify BOTH
/// consequences and <see cref="SpecRunSelfTest"/> does not repeat them — that guard pins only what is
/// its own, which is the VERDICT a test earns when the fact says no.</para>
/// </summary>
public static class GoldenMintSelfTest {
  public static int Run() {
    var host = CompileTarget.Native;
    var failures = 0;

    foreach (var (target, _) in CompileTarget.Supported) {
      var refusal = TargetRunHost.MintRefusalFor(target);

      if (target.Os == host.Os) {
        // ASCII only, here and below: this reports through MSBuild's `Exec`, whose console encoding
        // mangles an em dash into replacement characters - including in the failure text, which is the
        // one message that has to survive.
        if (refusal == null) continue;
        Console.Error.WriteLine(
          $"golden-mint-selftest FAIL: {target.Triple} shares this host's OS ({host.Os}) and must be "
          + $"mintable, but was refused: {refusal}");
        failures++;
        continue;
      }

      failures += CheckRefused(target, host, refusal);
    }

    // The rule is about the OS and ONLY the OS, so a target that differs from the host in ARCHITECTURE
    // alone must still be mintable. Constructed rather than taken from the roster because no supported
    // pair has that shape - both this compiler emits for differ from each other in BOTH halves - and
    // because leaving the arm untested is how "OS, deliberately, not the triple" would quietly become
    // "the triple" under a later edit. It is also the one row that is non-vacuous on EVERY host: a
    // Linux box shares its OS with neither supported target.
    var foreignArch = host.Arch == CompileTarget.X64Arch ? CompileTarget.Arm64Arch : CompileTarget.X64Arch;
    var archOnly = new CompileTarget(foreignArch, host.Os);
    if (TargetRunHost.MintRefusalFor(archOnly) is { } archRefusal) {
      Console.Error.WriteLine(
        $"golden-mint-selftest FAIL: {archOnly.Triple} differs from this host in ARCHITECTURE only, "
        + $"which this rule does not refuse, but it was refused: {archRefusal}");
      failures++;
    }

    if (failures == 0)
      Console.WriteLine(
        $"golden-mint-selftest: OK - {CompileTarget.Supported.Count()} targets classified against host {host.Triple}");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// A target whose OS is not the host's must be refused, and the refusal must NAME BOTH — that is the
  /// whole content of the ruling. A refusal that said only "wrong host" would leave the reader to
  /// work out which machine to go to, which is the one thing the message exists to answer.
  /// </summary>
  private static int CheckRefused(CompileTarget target, CompileTarget host, string? refusal) {
    if (refusal == null) {
      Console.Error.WriteLine(
        $"golden-mint-selftest FAIL: {target.Triple} is a {target.Os} target on a {host.Os} host and "
        + "must be refused, but the mint was allowed.");
      return 1;
    }

    var failures = 0;
    foreach (var owed in new[] { host.Os, target.Os, target.Triple }) {
      if (refusal.Contains(owed, StringComparison.Ordinal)) continue;
      Console.Error.WriteLine(
        $"golden-mint-selftest FAIL: {target.Triple}: the refusal does not name '{owed}': {refusal}");
      failures++;
    }

    return failures;
  }
}
