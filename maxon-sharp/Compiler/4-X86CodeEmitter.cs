using MaxonSharp.Compiler.Ir;
using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler;

/// <summary>
/// Result of code emission. <see cref="DebugInfo"/> is populated only under --debug-info; it carries
/// the observed line/function/file tables for the compiler to finalize into a `.mxdbg` sidecar. It
/// influences no emitted byte — a build with it null and one with it populated are byte-identical.
///
/// <see cref="BuildId"/> is the FNV-1a hash of <see cref="Code"/>, computed ONCE here where the code
/// is final. Both consumers — the sidecar's header and (under `--coverage`) the `.mxcov` header
/// stamped into the binary's own data — read it from this one field rather than each hashing the
/// same bytes again.
/// </summary>
public record CodeEmitResult(
  byte[] Code,
  byte[] Rdata,
  byte[] Data,
  byte[] Ucddata,
  byte[] Symdata,
  IReadOnlyList<ImportEntry> Imports,
  ulong BuildId,
  byte[]? Got = null,
  IReadOnlyList<string>? ImportNames = null,
  MaxonSharp.Debug.MxdbgWriter? DebugInfo = null
);

/// <summary>
/// Stage 5: Code emission.
/// Converts X86 dialect operations to machine code bytes.
/// </summary>
public class X86CodeEmitter {
  /// <summary>
  /// Emits machine code from an IR module containing X86 dialect operations.
  /// </summary>
  /// <param name="module">The IR module with X86 operations</param>
  public static CodeEmitResult Emit(IrModule<X86Op> module) {
    Logger.Debug(LogCategory.Codegen, "Emitting machine code");

    var emitter = new Ir.X86CodeEmitter();

    // Debug-info capture (--debug-info only). A pure observer of the emitted code: it records line
    // rows against code offsets as each function is emitted. Null on a release build, and never
    // consulted by emission, so `.text` is identical either way.
    var dbg = Compiler.DebugInfo ? new MaxonSharp.Debug.DebugInfoBuilder() : null;

    // Define rdata constants from module's RdataEntries (populated during StandardToX86 conversion)
    foreach (var (label, rdataBytes, alignment) in module.RdataEntries) {
      emitter.DefineRdata(label, rdataBytes, alignment);
    }

    // Define symdata entries (panic messages go in .symtab section, not .rdata)
    foreach (var (label, symdataBytes, alignment) in module.SymdataEntries) {
      emitter.DefineSymdata(label, symdataBytes, alignment);
    }

    // Define ucddata entries (Unicode Character Database tables go in .ucd section)
    foreach (var (label, ucddataBytes, alignment) in module.UcddataEntries) {
      emitter.DefineUcddata(label, ucddataBytes, alignment);
    }

    // Emit globals largest-first to eliminate alignment padding
    foreach (var global in module.Globals.OrderByDescending(g => g.Type.SizeInBytes)) {
      var size = global.Type.SizeInBytes;
      long initValue = 0;
      if (global.InitValue is IntegerAttr intAttr) {
        initValue = intAttr.Value;
      } else if (global.InitValue is FloatAttr floatAttr) {
        // Store IEEE 754 double bits as long for the data section
        initValue = BitConverter.DoubleToInt64Bits(floatAttr.Value);
      }
      emitter.DefineGlobal(global.Name, size, initValue);
    }

    var mainFunc = module.Functions.FirstOrDefault(f => f.Name == module.EntryFunctionName)
      ?? throw new InvalidOperationException($"No '{module.EntryFunctionName}' function found at code emission stage — semantic check should have caught this");

    // Emit mrt_start wrapper first (entry point at offset 0)
    // mrt_start calls main and then ExitProcess
    var globalCleanupName = module.Functions
      .Where(f => f.Name == "__maxon_global_cleanup")
      .Select(f => f.Name)
      .FirstOrDefault();
    var moduleInitName = module.Functions
      .Where(f => f.Name == "__module_init")
      .Select(f => f.Name)
      .FirstOrDefault();
    emitter.EmitStartWrapper(mainFunc.Name, globalCleanupName, moduleInitName);

    // Emit all functions (main and others)
    EmitFunction(emitter, mainFunc, dbg);
    foreach (var func in module.Functions.Where(f => f != mainFunc)) {
      EmitFunction(emitter, func, dbg);
    }

    // Emit __chkstk runtime function (for large stack allocations)
    emitter.EmitChkstk();

    // Runtime helpers must be emitted before user code so call targets are resolved
    emitter.EmitRuntimeFunctions();
    var rt = new Ir.Runtime.RuntimeEmitter(emitter.CreateBackend());
    rt.EmitAllMemoryManagerFunctions(Compiler.MmTrace, Compiler.MmDebug, module.TagTable, module.TagNames,
      module.DebugStreamNames, module.CoveragePoints.Count, module.CoverageDataPath);

    // Patch all __chkstk call sites
    emitter.PatchChkstkCalls();

    // Build function symbol table for stack traces
    var symbolEntries = new List<(string name, int codeOffset)>();
    // Add mrt_start so the stack walker knows where to stop
    var startOffset = emitter.GetLabelOffset("mrt_start");
    if (startOffset >= 0) symbolEntries.Add(("mrt_start", startOffset));
    foreach (var func in module.Functions) {
      var offset = emitter.GetLabelOffset(func.Name);
      if (offset >= 0) symbolEntries.Add((func.Name, offset));
    }
    foreach (var label in emitter.RuntimeFunctionLabels) {
      var offset = emitter.GetLabelOffset(label);
      if (offset >= 0) symbolEntries.Add((label, offset));
    }
    emitter.EmitSymbolTable(symbolEntries);

    emitter.ResolveLabels();

    var codeSize = (uint)emitter.GetCode().Length;
    var codeSizeVirtual = AlignUp(codeSize, 0x1000);

    // Section order: .text -> .rdata -> .data -> .ucd -> .symtab -> .idata
    // Calculate virtual section sizes for RVA calculations
    var rdataSize = (uint)emitter.GetRdata().Length;
    var rdataSizeVirtual = emitter.HasRdata ? AlignUp(rdataSize, 0x1000) : 0;

    // Resolve rdata references (rdata comes directly after code)
    if (emitter.HasRdata) {
      var rdataRvaOffset = (int)(codeSizeVirtual - codeSize);
      emitter.ResolveRdata(rdataRvaOffset);
      emitter.ResolveJumpTableFixups(rdataRvaOffset);
    }

    // Resolve global references (data section comes after rdata)
    if (emitter.HasGlobals) {
      var dataRvaOffset = (int)(codeSizeVirtual - codeSize + rdataSizeVirtual);
      emitter.ResolveGlobals(dataRvaOffset);
    }

    var dataSize = (uint)emitter.GetData().Length;
    var dataSizeVirtual = emitter.HasGlobals ? AlignUp(dataSize, 0x1000) : 0;

    // Resolve ucddata references (ucd section comes after data)
    if (emitter.HasUcddata) {
      var ucddataRvaOffset = (int)(codeSizeVirtual - codeSize + rdataSizeVirtual + dataSizeVirtual);
      emitter.ResolveUcddata(ucddataRvaOffset);
    }

    var ucddataSize = (uint)emitter.GetUcddata().Length;
    var ucddataSizeVirtual = emitter.HasUcddata ? AlignUp(ucddataSize, 0x1000) : 0;

    // Resolve symdata references (symtab section comes after ucd)
    if (emitter.HasSymdata) {
      var symdataRvaOffset = (int)(codeSizeVirtual - codeSize + rdataSizeVirtual + dataSizeVirtual + ucddataSizeVirtual);
      emitter.ResolveSymdata(symdataRvaOffset);
    }

    var symdataSize = (uint)emitter.GetSymdata().Length;
    var symdataSizeVirtual = emitter.HasSymdata ? AlignUp(symdataSize, 0x1000) : 0;

    // Resolve import references
    // IAT comes after symtab section
    if (emitter.HasImports) {
      var iatRvaOffset = (int)(codeSizeVirtual - codeSize + rdataSizeVirtual + dataSizeVirtual + ucddataSizeVirtual + symdataSizeVirtual);
      emitter.ResolveImports(iatRvaOffset);
    }

    var code = emitter.GetCode();
    ulong buildId = MaxonSharp.Debug.MxdbgFormat.ComputeBuildId(code);

    // Stamp the `.mxcov` header into the binary's own counter image, now that `.text` is final and
    // its hash is knowable. The running program then writes only the status word — it cannot
    // disagree with the build it came from about what it is.
    if (Compiler.Coverage) {
      emitter.PatchGlobalBytes(Ir.Runtime.RuntimeEmitter.CoverageImageGlobal, 0,
        MaxonSharp.Debug.MxcovFormat.BuildHeader(buildId, module.CoveragePoints.Count));
    }

    var rdata = emitter.GetRdata();
    var data = emitter.GetData();
    var ucddata = emitter.GetUcddata();
    var symdata = emitter.GetSymdata();
    var imports = emitter.Imports;

    // Types first: a local record references its type by the id RegisterTypes fixes, and each local's
    // source type is folded into the type table there. The prologue op the emitter turned into
    // `sub rsp, N` carries the real frame size; a function with no frame has no prologue op, reports 0.
    dbg?.RegisterTypes(module);
    dbg?.RegisterFunctions(module, emitter.GetLabelOffset, symbolEntries, code.Length,
        f => MaxonSharp.Debug.DebugInfoBuilder.FrameSizeFromPrologue(
            f, op => op is X86PrologueOp p ? (uint)p.StackSize : null));
    // Last: the point table's records point at the offsets NoteOp collected while the functions
    // above were emitted, and at the file ids RegisterFunctions registered.
    dbg?.RegisterCoveragePoints(module.CoveragePoints);

    Logger.Debug(LogCategory.Codegen, $"Emitted {code.Length} bytes code, {rdata.Length} bytes rdata, {data.Length} bytes data, {ucddata.Length} bytes ucddata, {symdata.Length} bytes symdata, {imports.Count} imports");

    return new CodeEmitResult(code, rdata, data, ucddata, symdata, imports, buildId, DebugInfo: dbg?.Writer);
  }

  /// <summary>
  /// Emits machine code for a single function. When <paramref name="dbg"/> is set, records a line
  /// row at each op whose source span differs from the previous op's (pure observation).
  /// </summary>
  private static void EmitFunction(Ir.X86CodeEmitter emitter, IrFunction<X86Op> func,
      MaxonSharp.Debug.DebugInfoBuilder? dbg) {
    emitter.DefineLabel(func.Name);
    emitter.SetCurrentFunction(func.Name);
    dbg?.BeginFunction();

    foreach (var block in func.Body.Blocks) {
      if (block.Name != "entry") {
        emitter.DefineLabel(emitter.ScopedLabel(block.Name));
      }

      foreach (var op in block.Operations) {
        dbg?.NoteOp(emitter.CurrentCodeOffset, func, op);
        emitter.Emit(op);
      }
    }
  }

  private static uint AlignUp(uint value, uint alignment) {
    return (value + alignment - 1) & ~(alignment - 1);
  }

}
