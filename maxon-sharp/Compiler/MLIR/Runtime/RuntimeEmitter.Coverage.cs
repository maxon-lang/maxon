using MaxonSharp.Debug;

namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// The `--coverage` runtime: the counter image the instrumented code increments, and the dump that
/// writes it out as a `.mxcov` file when the program ends.
///
/// Emitted only under `--coverage`, so every other binary is byte-identical to one built before this
/// existed — the headline invariant of the whole debugger workstream (docs/DEBUGGER_DESIGN.md).
/// </summary>
public partial class RuntimeEmitter {
  /// <summary>
  /// The whole `.mxcov` file, in the data section: a <see cref="MxcovFormat.HeaderSize"/>-byte header
  /// followed by one u64 counter per coverage point.
  ///
  /// Header and counters are ONE global rather than two adjacent ones on purpose. The dump is then a
  /// single write of a single contiguous range, with no assumption about how the data section happens
  /// to lay two globals out — an assumption that would hold until someone defined a third global
  /// between them and would fail by writing a file with a hole in it.
  ///
  /// The compiler fills magic, version, build-id and point count at BUILD time (see
  /// <c>MxcovFormat.BuildHeader</c>); the running program writes only the status word. So the header
  /// cannot disagree with the binary it came from — there is no code that could compute it wrongly.
  /// </summary>
  public const string CoverageImageGlobal = "__cov_image";

  /// <summary>
  /// The output path, baked in at build time as a read-only string.
  ///
  /// The program does NOT derive it from its own executable path at run time. That would mean calling
  /// `maxon_executable_path` (a heap allocation to give back, on the panic path too) and a copy loop,
  /// for a fact the compiler already knows exactly. gcov bakes its output path in for the same
  /// reason. The trade is the same one gcov makes: a binary MOVED after it was built writes to where
  /// it was built, and a report that then finds no data says so rather than inventing any.
  /// </summary>
  private const string CoveragePathSymdata = "__cov_out_path";

  /// <summary>
  /// The per-backend file writer: `__cov_write_file(pathCstr, buf, len)`. Each backend emits it (x64
  /// CreateFileA/WriteFile/CloseHandle, arm64 open/write/close), because it needs a real runtime
  /// frame: on x64 for the system-stack switch a heavy kernel call requires, and to re-align RSP
  /// before it (see the note at the x64 body). That is the same reason `maxon_write_stderr` has one
  /// body per backend.
  /// </summary>
  public const string CoverageWriteFileLabel = "__cov_write_file";

  /// `__cov_dump(status)` — the exit-time dump. Two callers: the normal end of `mrt_start` (status
  /// COMPLETED) and the panic path (status ABORTED).
  public const string CoverageDumpLabel = "__cov_dump";

  /// Reserve the counter image and bake in the output path. Sized here, once, from the parser's
  /// point count.
  private void EmitCoverageGlobals(int pointCount, string dataPath) {
    _b.DefineGlobal(CoverageImageGlobal,
      MxcovFormat.HeaderSize + pointCount * MxcovFormat.CounterSize, 0);
    _b.DefineSymdata(CoveragePathSymdata, System.Text.Encoding.UTF8.GetBytes(dataPath + "\0"));
  }

  /// <summary>
  /// `__cov_dump(status)`: stamp the run status into the pre-filled header and write the image out.
  ///
  /// It does no allocation and takes no lock, which is what makes it safe to call from the panic path
  /// as well as the normal one — a dump that allocated would have to give the memory back before the
  /// leak gate reads its counters, on both paths, and would be a second thing that can fail while the
  /// program is already dying.
  ///
  /// A write failure is SILENT, and that is not a swallowed error: instrumentation must never change
  /// what the program under measurement prints or returns. The absence of the file IS the report, and
  /// the driver says "the program wrote none, so the run did not complete" rather than the program
  /// saying anything on its own stderr.
  /// </summary>
  private void EmitCoverageDump(int pointCount) {
    _b.FunctionStart(CoverageDumpLabel, 1, 0x30);

    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LeaGlobal(VReg.Scratch1, CoverageImageGlobal);
    _b.StoreIndirect(VReg.Scratch1, MxcovFormat.OffStatus, VReg.Scratch0);

    _b.LeaSymdata(VReg.Arg0, CoveragePathSymdata);
    _b.LeaGlobal(VReg.Arg1, CoverageImageGlobal);
    _b.MovRegImm(VReg.Arg2, MxcovFormat.HeaderSize + pointCount * MxcovFormat.CounterSize);
    _b.Call(CoverageWriteFileLabel);

    _b.FunctionEnd();
  }
}
