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

    // Type table: primitives, a struct with two fields, and an enum with two cases. Type ids are
    // add-order; fields/locals reference them by that id.
    uint tVoid = w.AddType("void", MxdbgTypeKind.Primitive, 0, 1);
    uint tI64 = w.AddType("i64", MxdbgTypeKind.Primitive, 8, 8);
    uint tPoint = w.AddType("Point", MxdbgTypeKind.Struct, 16, 8);
    w.AddField("x", 0, tI64);
    w.AddField("y", 8, tI64);
    uint tColor = w.AddType("Color", MxdbgTypeKind.Enum, 8, 8);
    w.AddField("red", 0, tVoid);   // enum case: offset = ordinal, type = payload (none → void)
    w.AddField("green", 1, tVoid);

    // Function A occupies [0,100); B occupies [100,200). Lines are added OUT OF ORDER on purpose,
    // to prove Build sorts them and still assigns each function its contiguous window.
    uint fnWithdraw = w.AddFunction("withdraw", codeStart: 0, codeEnd: 100, frameSize: 0x40, paramCount: 1);
    uint fnParseBatch = w.AddFunction("parseBatch", codeStart: 100, codeEnd: 200, frameSize: 0x20, paramCount: 2);

    // Locals belong to a function and reference the type table; a NEGATIVE rbp-relative slot must
    // survive the u32 round-trip.
    w.AddLocal(fnWithdraw, "amount", MxdbgLocKind.StackSlotRbpRel, -8, tI64, 0, 100);
    w.AddLocal(fnWithdraw, "p", MxdbgLocKind.StackSlotRbpRel, -24, tPoint, 0, 100);
    w.AddLocal(fnParseBatch, "count", MxdbgLocKind.Register, 3, tI64, 100, 200);

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
    Check(fa is { Name: "withdraw", CodeStart: 0, CodeEnd: 100, FrameSize: 0x40, LineCount: 3, LocalCount: 2 },
      "function A range + frame + line/local windows");
    var fb = r.FunctionAt(150);
    Check(fb is { Name: "parseBatch", CodeStart: 100, CodeEnd: 200, FrameSize: 0x20, LineCount: 2, LocalCount: 1 },
      "function B range + frame + line/local windows");
    Check(r.FunctionAt(250) is null, "no function in a gap");

    // Type table round-trip.
    Check(r.TypeCount == 4, "type count");
    Check(r.FieldCount == 4, "field count");
    Check(r.Type(tPoint) is { Name: "Point", Kind: MxdbgTypeKind.Struct, Size: 16, FieldCount: 2 }, "struct type");
    Check(r.Type(tColor) is { Name: "Color", Kind: MxdbgTypeKind.Enum, FieldCount: 2 }, "enum type");
    var pointType = r.Type(tPoint);
    var fx = r.Field(pointType.FieldFirst);
    var fy = r.Field(pointType.FieldFirst + 1);
    Check(fx is { Name: "x", Offset: 0 } && r.TypeName(fx.TypeId) == "i64", "struct field x : i64");
    Check(fy is { Name: "y", Offset: 8 } && r.TypeName(fy.TypeId) == "i64", "struct field y : i64");
    var colorType = r.Type(tColor);
    var caseGreen = r.Field(colorType.FieldFirst + 1);
    Check(caseGreen is { Name: "green", Offset: 1 }, "enum case green = ordinal 1");

    // Local table round-trip, including the negative slot and the func→local window.
    Check(r.LocalCount == 3, "local count");
    var fnW = r.FunctionAt(10)!.Value;
    var lAmount = r.Local(fnW.LocalFirst);
    var lP = r.Local(fnW.LocalFirst + 1);
    Check(lAmount is { Name: "amount", LocKind: MxdbgLocKind.StackSlotRbpRel, LocValue: -8 }
      && r.TypeName(lAmount.TypeId) == "i64", "local amount @ [rbp-8] : i64");
    Check(lP is { Name: "p", LocValue: -24 } && r.TypeName(lP.TypeId) == "Point", "local p @ [rbp-24] : Point");
    var fnP = r.FunctionAt(150)!.Value;
    var lCount = r.Local(fnP.LocalFirst);
    Check(lCount is { Name: "count", LocKind: MxdbgLocKind.Register, LocValue: 3 }, "local count in register 3");

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
      Console.WriteLine($"mxdbg-selftest OK ({image.Length} bytes, {r.FunctionCount} funcs, "
        + $"{r.LineCount} lines, {r.TypeCount} types, {r.FieldCount} fields, {r.LocalCount} locals)");
      return 0;
    }

    Console.Error.WriteLine($"mxdbg-selftest: {failures} failure(s)");
    return 1;
  }
}
