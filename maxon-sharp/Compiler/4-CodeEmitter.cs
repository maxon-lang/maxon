using MaxonSharp.Compiler.Mlir;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

/// <summary>
/// Result of code emission.
/// </summary>
public record CodeEmitResult(
  byte[] Code,
  byte[] Rdata,
  byte[] Data,
  IReadOnlyList<ImportEntry> Imports
);

/// <summary>
/// Stage 5: Code emission.
/// Converts X86 dialect operations to machine code bytes.
/// </summary>
public class CodeEmitter {
  /// <summary>
  /// Emits machine code from an MLIR module containing X86 dialect operations.
  /// </summary>
  /// <param name="module">The MLIR module with X86 operations</param>
  public static CodeEmitResult Emit(MlirModule<X86Op> module, bool trackAllocs = false) {
    Logger.Debug(LogCategory.Codegen, "Emitting machine code");

    var emitter = new X86CodeEmitter(trackAllocs);

    // Define rdata constants from module's RdataEntries (populated during StandardToX86 conversion)
    foreach (var (label, rdataBytes, alignment) in module.RdataEntries) {
      emitter.DefineRdata(label, rdataBytes, alignment);
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

    // Define tracking globals when --track-allocs is enabled
    if (trackAllocs) {
      emitter.DefineGlobal("__track_next_id", 8, 0);
      emitter.DefineGlobal("__track_alloc_bytes", 8, 0);
      emitter.DefineGlobal("__track_freed_bytes", 8, 0);
      emitter.DefineGlobal("__track_move_count", 8, 0);
      emitter.DefineGlobal("__track_incref_count", 8, 0);
      emitter.DefineGlobal("__track_decref_count", 8, 0);
      emitter.DefineGlobal("__track_copy_count", 8, 0);
      emitter.DefineGlobal("__track_cleanup_count", 8, 0);
      // 256 entries * 24 bytes each (ptr, size, id)
      emitter.DefineGlobal("__track_table", 256 * 24, 0);
    }

    // Verify main exists (check for exact match or suffix match)
    var mainFunc = module.Functions.FirstOrDefault(f => f.Name == "main" || f.Name.EndsWith(".main"))
      ?? throw new InvalidOperationException("No 'main' function found at code emission stage — semantic check should have caught this");

    // Emit _start wrapper first (entry point at offset 0)
    // _start calls main and then ExitProcess
    emitter.EmitStartWrapper(mainFunc.Name);

    // Emit all functions (main and others)
    EmitFunction(emitter, mainFunc);
    foreach (var func in module.Functions.Where(f => f != mainFunc)) {
      EmitFunction(emitter, func);
    }

    // Emit __chkstk runtime function (for large stack allocations)
    emitter.EmitChkstk();

    // Emit runtime allocation functions (maxon_alloc, maxon_free)
    emitter.EmitRuntimeFunctions();

    // Patch all __chkstk call sites
    emitter.PatchChkstkCalls();

    emitter.ResolveLabels();

    var codeSize = (uint)emitter.GetCode().Length;
    var codeSizeVirtual = AlignUp(codeSize, 0x1000);

    // Section order: .text -> .rdata -> .data -> .idata
    // Calculate virtual section sizes for RVA calculations
    var rdataSize = (uint)emitter.GetRdata().Length;
    var rdataSizeVirtual = emitter.HasRdata ? AlignUp(rdataSize, 0x1000) : 0;

    // Resolve rdata references (rdata comes directly after code)
    if (emitter.HasRdata) {
      var rdataRvaOffset = (int)(codeSizeVirtual - codeSize);
      emitter.ResolveRdata(rdataRvaOffset);
    }

    // Resolve global references (data section comes after rdata)
    if (emitter.HasGlobals) {
      var dataRvaOffset = (int)(codeSizeVirtual - codeSize + rdataSizeVirtual);
      emitter.ResolveGlobals(dataRvaOffset);
    }

    // Resolve import references
    // IAT comes after data section (if present) or directly after rdata/code
    if (emitter.HasImports) {
      var dataSize = (uint)emitter.GetData().Length;
      var dataSizeVirtual = emitter.HasGlobals ? AlignUp(dataSize, 0x1000) : 0;
      // IAT is at the start of the .idata section, which follows .data
      var iatRvaOffset = (int)(codeSizeVirtual - codeSize + rdataSizeVirtual + dataSizeVirtual);
      emitter.ResolveImports(iatRvaOffset);
    }

    var code = emitter.GetCode();
    var rdata = emitter.GetRdata();
    var data = emitter.GetData();
    var imports = emitter.Imports;

    Logger.Debug(LogCategory.Codegen, $"Emitted {code.Length} bytes code, {rdata.Length} bytes rdata, {data.Length} bytes data, {imports.Count} imports");

    return new CodeEmitResult(code, rdata, data, imports);
  }

  /// <summary>
  /// Emits machine code for a single function.
  /// </summary>
  private static void EmitFunction(X86CodeEmitter emitter, MlirFunction<X86Op> func) {
    emitter.DefineLabel(func.Name);
    emitter.SetCurrentFunction(func.Name);

    foreach (var block in func.Body.Blocks) {
      if (block.Name != "entry") {
        emitter.DefineLabel(emitter.ScopedLabel(block.Name));
      }

      foreach (var op in block.Operations) {
        emitter.Emit(op);
      }
    }
  }

  private static uint AlignUp(uint value, uint alignment) {
    return (value + alignment - 1) & ~(alignment - 1);
  }
}
