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
  byte[] Symdata,
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
  public static CodeEmitResult Emit(MlirModule<X86Op> module) {
    Logger.Debug(LogCategory.Codegen, "Emitting machine code");

    var emitter = new X86CodeEmitter();

    // Define rdata constants from module's RdataEntries (populated during StandardToX86 conversion)
    foreach (var (label, rdataBytes, alignment) in module.RdataEntries) {
      emitter.DefineRdata(label, rdataBytes, alignment);
    }

    // Define symdata entries (panic messages go in .symtab section, not .rdata)
    foreach (var (label, symdataBytes, alignment) in module.SymdataEntries) {
      emitter.DefineSymdata(label, symdataBytes, alignment);
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

    // Verify main exists (check for exact match or suffix match)
    var mainFunc = module.Functions.FirstOrDefault(f => f.Name == "main" || f.Name.EndsWith(".main"))
      ?? throw new InvalidOperationException("No 'main' function found at code emission stage — semantic check should have caught this");

    // Emit _start wrapper first (entry point at offset 0)
    // _start calls main and then ExitProcess
    var globalCleanupName = module.Functions
      .Where(f => f.Name == "__maxon_global_cleanup" || f.Name.EndsWith(".__maxon_global_cleanup"))
      .Select(f => f.Name)
      .FirstOrDefault();
    var moduleInitName = module.Functions
      .Where(f => f.Name == "__module_init" || f.Name.EndsWith(".__module_init"))
      .Select(f => f.Name)
      .FirstOrDefault();
    emitter.EmitStartWrapper(mainFunc.Name, globalCleanupName, moduleInitName);

    // Emit all functions (main and others)
    EmitFunction(emitter, mainFunc);
    foreach (var func in module.Functions.Where(f => f != mainFunc)) {
      EmitFunction(emitter, func);
    }

    // Emit __chkstk runtime function (for large stack allocations)
    emitter.EmitChkstk();

    // Emit runtime helper functions (string conversion, I/O, process management, etc.)
    emitter.EmitRuntimeFunctions();

    // Emit scope-based memory manager functions (mm_alloc, mm_free, mm_scope_enter/exit, etc.)
    emitter.EmitMemoryManagerFunctions();

    // Patch all __chkstk call sites
    emitter.PatchChkstkCalls();

    // Build function symbol table for stack traces
    var symbolEntries = new List<(string name, int codeOffset)>();
    // Add _start so the stack walker knows where to stop
    var startOffset = emitter.GetLabelOffset("_start");
    if (startOffset >= 0) symbolEntries.Add(("_start", startOffset));
    foreach (var func in module.Functions) {
      var offset = emitter.GetLabelOffset(func.Name);
      if (offset >= 0) symbolEntries.Add((func.Name, offset));
    }
    emitter.EmitSymbolTable(symbolEntries);

    emitter.ResolveLabels();

    var codeSize = (uint)emitter.GetCode().Length;
    var codeSizeVirtual = AlignUp(codeSize, 0x1000);

    // Section order: .text -> .rdata -> .data -> .symtab -> .idata
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

    var dataSize = (uint)emitter.GetData().Length;
    var dataSizeVirtual = emitter.HasGlobals ? AlignUp(dataSize, 0x1000) : 0;

    // Resolve symdata references (symtab section comes after data)
    if (emitter.HasSymdata) {
      var symdataRvaOffset = (int)(codeSizeVirtual - codeSize + rdataSizeVirtual + dataSizeVirtual);
      emitter.ResolveSymdata(symdataRvaOffset);
    }

    var symdataSize = (uint)emitter.GetSymdata().Length;
    var symdataSizeVirtual = emitter.HasSymdata ? AlignUp(symdataSize, 0x1000) : 0;

    // Resolve import references
    // IAT comes after symtab section
    if (emitter.HasImports) {
      var iatRvaOffset = (int)(codeSizeVirtual - codeSize + rdataSizeVirtual + dataSizeVirtual + symdataSizeVirtual);
      emitter.ResolveImports(iatRvaOffset);
    }

    var code = emitter.GetCode();
    var rdata = emitter.GetRdata();
    var data = emitter.GetData();
    var symdata = emitter.GetSymdata();
    var imports = emitter.Imports;

    Logger.Debug(LogCategory.Codegen, $"Emitted {code.Length} bytes code, {rdata.Length} bytes rdata, {data.Length} bytes data, {symdata.Length} bytes symdata, {imports.Count} imports");

    return new CodeEmitResult(code, rdata, data, symdata, imports);
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
