using MaxonSharp.Compiler.Mlir;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

/// <summary>
/// Stage 5 (ARM64): Code emission.
/// Converts ARM64 dialect operations to machine code bytes for Mach-O output.
/// </summary>
public class ARM64CodeEmitterStage {
  public static CodeEmitResult Emit(MlirModule<ARM64Op> module) {
    Logger.Debug(LogCategory.Codegen, "Emitting ARM64 machine code");

    var emitter = new ARM64CodeEmitter();

    // Define rdata constants
    foreach (var (label, rdataBytes, alignment) in module.RdataEntries) {
      emitter.DefineRdata(label, rdataBytes, alignment);
    }

    // Define symdata entries
    foreach (var (label, symdataBytes, alignment) in module.SymdataEntries) {
      emitter.DefineSymdata(label, symdataBytes, alignment);
    }

    // Define ucddata entries
    foreach (var (label, ucddataBytes, alignment) in module.UcddataEntries) {
      emitter.DefineUcddata(label, ucddataBytes, alignment);
    }

    // Emit globals
    foreach (var global in module.Globals.OrderByDescending(g => g.Type.SizeInBytes)) {
      var size = global.Type.SizeInBytes;
      long initValue = 0;
      if (global.InitValue is IntegerAttr intAttr) {
        initValue = intAttr.Value;
      } else if (global.InitValue is FloatAttr floatAttr) {
        initValue = BitConverter.DoubleToInt64Bits(floatAttr.Value);
      }
      emitter.DefineGlobal(global.Name, size, initValue);
    }

    // Find main function
    var mainFunc = module.Functions.FirstOrDefault(f => f.Name == "main" || f.Name.EndsWith(".main"))
      ?? throw new InvalidOperationException("No 'main' function found");

    // Emit _start wrapper
    var globalCleanupName = module.Functions
      .Where(f => f.Name == "__maxon_global_cleanup" || f.Name.EndsWith(".__maxon_global_cleanup"))
      .Select(f => f.Name)
      .FirstOrDefault();
    var moduleInitName = module.Functions
      .Where(f => f.Name == "__module_init" || f.Name.EndsWith(".__module_init"))
      .Select(f => f.Name)
      .FirstOrDefault();
    emitter.EmitStartWrapper(mainFunc.Name, globalCleanupName, moduleInitName);

    // Emit all functions
    EmitFunction(emitter, mainFunc);
    foreach (var func in module.Functions.Where(f => f != mainFunc)) {
      EmitFunction(emitter, func);
    }

    // Emit runtime functions
    emitter.EmitRuntimeFunctions();
    var rt = new Mlir.Runtime.RuntimeEmitter(emitter.CreateBackend());
    rt.EmitMmGlobals(Compiler.MmTrace, Compiler.MmDebug);
    rt.EmitMmTraceFunctions(Compiler.MmTrace, module.TagTable ?? []);
    rt.EmitMmAlloc(Compiler.MmTrace, Compiler.MmDebug);
    rt.EmitMmRealloc(Compiler.MmTrace, Compiler.MmDebug);
    rt.EmitMmFree(Compiler.MmTrace, Compiler.MmDebug);
    rt.EmitMmIncref(Compiler.MmTrace);
    rt.EmitMmDecref(Compiler.MmTrace);
    rt.EmitMmManagedElementsFunctions(Compiler.MmTrace);
    rt.EmitMmLeakCheck();
    rt.EmitMmValidatePtr();
    rt.EmitManagedListFunctions(Compiler.MmTrace);

    // Build symbol table
    var symbolEntries = new List<(string name, int codeOffset)>();
    var startOffset = emitter.GetLabelOffset("_start");
    if (startOffset >= 0) symbolEntries.Add(("_start", startOffset));
    foreach (var func in module.Functions) {
      var offset = emitter.GetLabelOffset(func.Name);
      if (offset >= 0) symbolEntries.Add((func.Name, offset));
    }
    emitter.EmitSymbolTable(symbolEntries);

    // Resolve internal branch/call labels
    emitter.ResolveLabels();

    // Compute Mach-O layout for ADRP fixup resolution
    var codeBytes = emitter.GetCode();
    var rdataRaw = emitter.GetRdata();
    var dataRaw = emitter.GetData();
    var ucddataRaw = emitter.GetUcddata();
    var symdataRaw = emitter.GetSymdata();
    var gotRaw = emitter.GetGot();
    var importNamesList = emitter.GetImportNames();

    var constSize = (uint)(rdataRaw.Length + ucddataRaw.Length + symdataRaw.Length);
    var layout = MachOLayout.Compute(
      codeSize: (uint)codeBytes.Length,
      constSize: constSize,
      dataSize: (uint)dataRaw.Length,
      gotSize: (uint)gotRaw.Length,
      hasImports: emitter.HasImports);

    // Resolve ADRP fixups using the computed layout
    var rdataSectionFileOffset = layout.ConstSectionOffset;
    var ucddataSectionFileOffset = layout.ConstSectionOffset + (uint)rdataRaw.Length;
    var symdataSectionFileOffset = layout.ConstSectionOffset + (uint)rdataRaw.Length + (uint)ucddataRaw.Length;

    emitter.ResolveAdrpFixups(
      textSectionFileOffset: layout.TextSectionOffset,
      textSegmentVMAddr: MachOLayout.TextSegmentVMAddr,
      rdataSectionFileOffset: rdataSectionFileOffset,
      symdataSectionFileOffset: symdataSectionFileOffset,
      ucddataSectionFileOffset: ucddataSectionFileOffset,
      dataSegmentVMAddr: layout.DataSegmentVMAddr
    );

    if (emitter.HasImports) {
      emitter.ResolveGotFixups(layout.TextSectionOffset, MachOLayout.TextSegmentVMAddr, layout.GotSectionVMAddr);
    }

    var code = emitter.GetCode();
    var rdata = emitter.GetRdata();
    var data = emitter.GetData();
    var ucddata = emitter.GetUcddata();
    var symdata = emitter.GetSymdata();

    // Build COFF symbol list
    var coffSymbols = symbolEntries
      .OrderBy(e => e.codeOffset)
      .Select(e => new CoffSymbol(e.name, e.codeOffset))
      .ToList();

    Logger.Debug(LogCategory.Codegen, $"ARM64: Emitted {code.Length} bytes code, {rdata.Length} bytes rdata, {data.Length} bytes data, {ucddata.Length} bytes ucddata, {symdata.Length} bytes symdata");

    return new CodeEmitResult(code, rdata, data, ucddata, symdata, [], coffSymbols,
      emitter.HasImports ? gotRaw : null,
      emitter.HasImports ? importNamesList : null);
  }

  private static void EmitFunction(ARM64CodeEmitter emitter, MlirFunction<ARM64Op> func) {
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
}
