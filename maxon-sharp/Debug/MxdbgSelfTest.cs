namespace MaxonSharp.Debug;

/// <summary>
/// Round-trips a `.mxdbg` image through <see cref="MxdbgWriter"/> and <see cref="MxdbgReader"/> and
/// asserts the reader recovers exactly what the writer put in. Wired as `maxon mxdbg-selftest`
/// (mirroring `batch-rewriter-test`) so the format contract is checkable without a compiled program —
/// the writer has no compiler feeding it yet at P1. This becomes a proper spec fragment once emission
/// lands, but the round-trip invariant is worth guarding on its own.
/// </summary>
public static class MxdbgSelfTest {
  public static int Run() {
    int failures = 0;

    void Check(bool cond, string what) {
      if (cond) return;
      Console.Error.WriteLine($"mxdbg-selftest FAIL: {what}");
      failures++;
    }

    // Build-id is a pure function of the bytes.
    var id1 = MxdbgFormat.ComputeBuildId("hello"u8);
    var id2 = MxdbgFormat.ComputeBuildId("hello"u8);
    var id3 = MxdbgFormat.ComputeBuildId("hellp"u8);
    Check(id1 == id2, "build-id is deterministic");
    Check(id1 != id3, "build-id distinguishes different .text");

    var w = new MxdbgWriter();
    uint fileA = w.AddFile("account.maxon");
    uint fileB = w.AddFile("io.maxon");

    // Function A occupies [0,100); B occupies [100,200). Lines are added OUT OF ORDER on purpose,
    // to prove Build sorts them and still assigns each function its contiguous window.
    w.AddFunction("withdraw", codeStart: 0, codeEnd: 100, frameSize: 0x40, paramCount: 1);
    w.AddFunction("parseBatch", codeStart: 100, codeEnd: 200, frameSize: 0x20, paramCount: 2);

    w.AddLine(40, fileA, 12, 5, MxdbgFormat.LineFlagStatement);
    w.AddLine(0, fileA, 10, 1, MxdbgFormat.LineFlagStatement);
    w.AddLine(120, fileB, 6, 9, MxdbgFormat.LineFlagStatement);
    w.AddLine(16, fileA, 11, 5, MxdbgFormat.LineFlagStatement);
    w.AddLine(100, fileB, 5, 1, MxdbgFormat.LineFlagStatement);

    var image = w.Build(id1, "x64-windows");
    var r = new MxdbgReader(image);

    Check(r.BuildId == id1, "build-id round-trips");
    Check(r.Triple == "x64-windows", "triple round-trips");
    Check(r.FileCount == 2, "file count");
    Check(r.FunctionCount == 2, "function count");
    Check(r.LineCount == 5, "line count");
    Check(r.FileName(fileA) == "account.maxon", "file name A");
    Check(r.FileName(fileB) == "io.maxon", "file name B");

    var fa = r.FunctionAt(0);
    Check(fa is { Name: "withdraw", CodeStart: 0, CodeEnd: 100, LineCount: 3 }, "function A range + line window");
    var fb = r.FunctionAt(150);
    Check(fb is { Name: "parseBatch", CodeStart: 100, CodeEnd: 200, LineCount: 2 }, "function B range + line window");
    Check(r.FunctionAt(250) is null, "no function in a gap");

    void Line(uint pc, uint expLine, string expFile) {
      var li = r.PcToLine(pc);
      Check(li is { } l && l.Line == expLine && l.File == expFile,
        $"PcToLine({pc}) → {expFile}:{expLine} (got {(r.PcToLine(pc) is { } g ? $"{g.File}:{g.Line}" : "null")})");
    }

    Line(0, 10, "account.maxon");   // exact start
    Line(8, 10, "account.maxon");   // between rows → greatest ≤
    Line(16, 11, "account.maxon");  // exact
    Line(50, 12, "account.maxon");  // within last row of A
    Line(99, 12, "account.maxon");  // last byte of A
    Line(100, 5, "io.maxon");       // first byte of B (function boundary)
    Line(130, 6, "io.maxon");       // within B
    Check(r.PcToLine(250) is null, "PcToLine in a gap is null");

    // A mismatched build-id is the driver's refusal signal; here just prove the reader surfaces it.
    var rMismatch = new MxdbgReader(image);
    Check(rMismatch.BuildId != MxdbgFormat.ComputeBuildId("different"u8), "build-id mismatch is detectable");

    if (failures == 0) {
      Console.WriteLine($"mxdbg-selftest OK ({image.Length} bytes, {r.FunctionCount} funcs, {r.LineCount} lines)");
      return 0;
    }

    Console.Error.WriteLine($"mxdbg-selftest: {failures} failure(s)");
    return 1;
  }
}
