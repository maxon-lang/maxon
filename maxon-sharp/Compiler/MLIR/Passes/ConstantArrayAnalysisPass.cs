using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Identifies array constructions whose CONTENTS are known at compile time. Pure analysis on Maxon
/// ops, running before MaxonToStandardConversion, with two results:
///   - an array literal with all-constant elements (`[1, 2, 3]`) is tagged for .rdata placement in
///     module.ConstantArrayLiterals;
///   - a zero-argument factory whose whole body builds and returns an EMPTY container
///     (`Array.create()`) is recorded in module.ConstantEmptyContainerFactories — an array literal
///     with zero elements is constant in the same sense, and the empty record is then a constant
///     too.
/// Both let the conversion emit ONE shared immortal record instead of allocating per evaluation;
/// which SITES may use it is decided separately by LiteralCoverageAnalysisPass.
/// </summary>
public static class ConstantArrayAnalysisPass {
  // The two __ManagedMemory-shaped field names this pass reads by name: the wrapper's nested record,
  // and the element width inside it.
  private const string ManagedFieldName = "managed";
  private const string ElementSizeFieldName = "element_size";

  public static void Run(IrModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      AnalyzeFunction(func, module);
      TryTagConstantEmptyContainerFactory(func, module);
    }
  }

  /// <summary>
  /// Recognize a CONSTANT EMPTY container factory: a zero-argument function whose entire body builds
  /// one empty managed-wrapper record and returns it — `Array.create()`'s `return Self{}`. Every
  /// field of that record is a compile-time constant (the element width, and zero everywhere else),
  /// so the record itself is a constant and every call whose result is never written through can
  /// share ONE immortal copy of it rather than allocating.
  ///
  /// What is recognized is the FUNCTION, not the `Self{}` literal inside it, for two reasons. The
  /// language refuses `Array{}` outside the type's own methods (E3076), so a CALL is the only way a
  /// program can spell an empty container. And the factory body is SHARED by every caller: deciding
  /// at the literal would let one caller's `push` cost every other caller its record, which is
  /// exactly the distinction the rung exists to make.
  /// </summary>
  private static void TryTagConstantEmptyContainerFactory(IrFunction<MaxonOp> func, IrModule<MaxonOp> module) {
    if (func.ParamNames.Count != 0 || func.Body.Blocks.Count != 1) return;

    // The body may do nothing but name constants and assemble the two records — the inner
    // __ManagedMemory and the wrapper around it. Any other op and the result is not a constant.
    var constants = new Dictionary<int, long>();
    MaxonStructLiteralOp? inner = null;
    MaxonStructLiteralOp? wrapper = null;
    int returnedId = -1;
    foreach (var op in func.Body.Blocks[0].Operations) {
      switch (op) {
        case MaxonLiteralOp { ValueKind: MaxonValueKind.Integer } lit:
          constants[lit.Result.Id] = lit.IntValue;
          break;
        case MaxonStructLiteralOp structLit:
          if (wrapper != null) return;
          if (inner == null) inner = structLit; else wrapper = structLit;
          break;
        case MaxonScopeEndOp { VarsToClean.Count: 0 }:
          break;  // nothing was bound, so nothing is released
        case MaxonReturnOp { Value: not null } ret:
          if (returnedId >= 0) return;
          returnedId = ret.Value.Id;
          break;
        default:
          return;
      }
    }
    if (wrapper == null || returnedId != wrapper.Result.Id) return;

    // The wrapper is a fused array record holding nothing but its __ManagedMemory. A wrapper with
    // any other field would need that field materialized into the static blob too; none of today's
    // array types has one, and a future one falls back to the ordinary allocating path.
    if (!module.TypeDefs.TryGetValue(wrapper.TypeName, out var wrapperDef)
        || wrapperDef is not IrStructType wrapperType
        || !wrapperType.ConformingInterfaces.Contains("BuiltinArrayLiteral")) return;
    if (wrapper.ArrayLiteralTag != null || wrapper.ArrayLiteralCount != 0
        || wrapper.SkipZeroInit || wrapper.IsBitPacked) return;
    if (wrapper.FieldValues.Count != 1) return;
    var (wrapperFieldName, wrapperFieldValue) = wrapper.FieldValues[0];
    if (wrapperFieldName != ManagedFieldName || wrapperFieldValue.Id != inner!.Result.Id) return;

    if (!TypeAliasInfo.IsManagedMemoryType(inner.TypeName, module.TypeAliasSources)) return;
    if (inner.ArrayLiteralTag != null || inner.ArrayLiteralCount != 0 || inner.SkipZeroInit) return;

    // EMPTY is exactly "constant everywhere, and zero everywhere the element width is not": no
    // buffer to point at, no length, no capacity, no parent record. Asking it that way rather than
    // by field name keeps this from re-encoding the __ManagedMemory layout a second time.
    long? elementSize = null;
    foreach (var (fieldName, fieldValue) in inner.FieldValues) {
      if (!constants.TryGetValue(fieldValue.Id, out var fieldConst)) return;
      if (fieldName == ElementSizeFieldName) elementSize = fieldConst;
      else if (fieldConst != 0) return;
    }
    if (elementSize is not (>= 0 and <= int.MaxValue)) return;
    // A zero element width is the bit-packed-bool sentinel and nothing else. Anywhere else it is
    // the state the allocating path PANICS on at run time, and a static record must not answer
    // where the path it replaces panics.
    if (elementSize == 0 && !inner.IsBitPacked) return;

    module.ConstantEmptyContainerFactories[func.Name] =
      new ConstantEmptyContainerInfo(wrapper.TypeName, (int)elementSize);
  }

  private static void AnalyzeFunction(IrFunction<MaxonOp> func, IrModule<MaxonOp> module) {
    foreach (var block in func.Body.Blocks) {
      // Collect MaxonLiteralOp results and MaxonStructLiteralOp results
      var literalValues = new Dictionary<int, long>();
      var structLiterals = new Dictionary<int, MaxonStructLiteralOp>();
      foreach (var op in block.Operations) {
        if (op is MaxonLiteralOp lit) {
          // Collect literals that can be represented as long for rdata storage
          switch (lit.ValueKind) {
            case MaxonValueKind.Integer:
              literalValues[lit.Result.Id] = lit.IntValue;
              break;
            case MaxonValueKind.Bool:
              literalValues[lit.Result.Id] = lit.BoolValue ? 1L : 0L;
              break;
            case MaxonValueKind.Byte:
            case MaxonValueKind.Short:
              literalValues[lit.Result.Id] = lit.IntValue;
              break;
            case MaxonValueKind.Float:
            case MaxonValueKind.Float32:
              // Float literals can't be stored as long - skip (array won't be marked as all-constant)
              break;
            case MaxonValueKind.Struct:
              // Struct literals are tracked in structLiterals - skip
              break;
            case MaxonValueKind.Enum:
            case MaxonValueKind.Function:
              // Enum/Function literals can't be in constant arrays - skip
              break;
            default:
              throw new InvalidOperationException(
                $"ConstantArrayAnalysisPass: unhandled literal value kind '{lit.ValueKind}' in {func.Name}");
          }
        }
        // Track cast operations - "X as byte" or "X as bool" with constant input
        if (op is MaxonCastOp castOp && literalValues.TryGetValue(castOp.Input.Id, out var inputVal)) {
          switch (castOp.TargetKind) {
            case MaxonValueKind.Byte:
              literalValues[castOp.Result.Id] = inputVal & 0xFF;
              break;
            case MaxonValueKind.Bool:
              literalValues[castOp.Result.Id] = inputVal != 0 ? 1L : 0L;
              break;
            case MaxonValueKind.Short:
              literalValues[castOp.Result.Id] = inputVal & 0xFFFF;
              break;
            case MaxonValueKind.Integer:
              literalValues[castOp.Result.Id] = inputVal;
              break;
            case MaxonValueKind.Float:
            case MaxonValueKind.Float32:
              // Cast to float can't be stored as long - don't track
              break;
            case MaxonValueKind.Struct:
            case MaxonValueKind.Enum:
            case MaxonValueKind.Function:
              // These shouldn't occur as cast targets, but skip if they do
              break;
            default:
              throw new InvalidOperationException(
                $"ConstantArrayAnalysisPass: unhandled cast target kind '{castOp.TargetKind}' in {func.Name}");
          }
        }
        if (op is MaxonStructLiteralOp slit)
          structLiterals[slit.Result.Id] = slit;
      }

      // Find array/vector assigns with all-constant elements (both let and var)
      foreach (var op in block.Operations) {
        if (op is not MaxonAssignOp { ValueKind: MaxonValueKind.Struct } assignOp) continue;
        if (!structLiterals.TryGetValue(assignOp.Value.Id, out var arrayStructLit)) continue;
        if (arrayStructLit.ArrayLiteralTag == null) continue;

        // Mutable structs with stack-sized element buffers keep data on the stack
        // so .set() can write directly without COW heap-copying
        if (assignOp.IsMutable && HasStackAllocatableBuffer(arrayStructLit.TypeName, module.TypeDefs))
          continue;

        TryTagConstantArray(block, arrayStructLit, assignOp.VarName, assignOp.IsMutable,
          func, module, literalValues, structLiterals);
      }

      // Find global stores of array literals with all-constant elements (top-level let/var arrays)
      foreach (var op in block.Operations) {
        if (op is not MaxonGlobalStoreOp { ValueKind: MaxonValueKind.Struct } globalStore) continue;
        if (!structLiterals.TryGetValue(globalStore.Value.Id, out var arrayStructLit)) continue;
        if (arrayStructLit.ArrayLiteralTag == null) continue;

        bool isMutable = module.GlobalVarInfos.TryGetValue(globalStore.GlobalName, out var info) && info.Mutable;

        if (isMutable && HasStackAllocatableBuffer(arrayStructLit.TypeName, module.TypeDefs))
          continue;

        TryTagConstantArray(block, arrayStructLit, globalStore.GlobalName, isMutable,
          func, module, literalValues, structLiterals);
      }
    }
  }

  /// <summary>
  /// Check if all elements of an array literal are constants and tag for .rdata placement.
  /// Used for both local assigns and global stores.
  /// </summary>
  private static void TryTagConstantArray(
      IrBlock<MaxonOp> block,
      MaxonStructLiteralOp arrayStructLit,
      string ownerName,
      bool isMutable,
      IrFunction<MaxonOp> func,
      IrModule<MaxonOp> module,
      Dictionary<int, long> literalValues,
      Dictionary<int, MaxonStructLiteralOp> structLiterals) {
    var tag = arrayStructLit.ArrayLiteralTag!;
    int count = arrayStructLit.ArrayLiteralCount;
    // Collect element values from the element assign ops
    var elementValues = new long[count];
    bool allConstant = true;
    foreach (var elemOp in block.Operations) {
      if (elemOp is not MaxonAssignOp elemAssign) continue;
      if (!elemAssign.VarName.StartsWith($"{tag}.")) continue;
      var indexStr = elemAssign.VarName[($"{tag}.".Length)..];
      if (!int.TryParse(indexStr, out var idx)) continue;
      if (!literalValues.TryGetValue(elemAssign.Value.Id, out var val)) {
        allConstant = false;
        break;
      }
      elementValues[idx] = val;
    }
    if (!allConstant) return;

    // Extract element_size from the __ManagedMemory struct
    MaxonStructLiteralOp? managedStruct = (TypeAliasInfo.IsManagedMemoryType(arrayStructLit.TypeName, module.TypeAliasSources)
        ? arrayStructLit
        : FindManagedMemoryStruct(arrayStructLit, structLiterals)) ?? throw new InvalidOperationException(
            $"ConstantArrayAnalysisPass: cannot find __ManagedMemory struct for array '{ownerName}' in {func.Name}");

    var (FieldName, Value) = managedStruct.FieldValues.FirstOrDefault(f => f.FieldName == ElementSizeFieldName);

    if (Value == null || !literalValues.TryGetValue(Value.Id, out var elemSizeVal)) {
      throw new InvalidOperationException(
        $"ConstantArrayAnalysisPass: cannot determine element_size for array '{ownerName}' in {func.Name}");
    }
    int elementSize = (int)elemSizeVal;
    bool isBitPacked = elementSize == 0; // sentinel: elementSize=0 means bit-packed bools
    if (isBitPacked) elementSize = 1; // rdata still uses 1 byte per element for individual values

    // Include function name in rdata label to avoid conflicts
    var rdataLabel = $"__const_array_{func.Name}_{ownerName}";
    module.ConstantArrayLiterals[arrayStructLit.Result.Id] =
      new ConstantArrayLiteralInfo(rdataLabel, elementValues, isMutable, elementSize, isBitPacked);
  }

  private static bool HasStackAllocatableBuffer(string typeName, Dictionary<string, IrType> typeDefs) {
    if (!typeDefs.TryGetValue(typeName, out var typeDef)) return false;
    if (typeDef is not IrStructType structType) return false;
    return structType.HasStackAllocatableBuffer;
  }

  /// <summary>
  /// Find the nested __ManagedMemory struct within an Array/Vector struct literal.
  /// Arrays have a 'managed' field that contains the __ManagedMemory struct.
  /// </summary>
  private static MaxonStructLiteralOp? FindManagedMemoryStruct(
    MaxonStructLiteralOp arrayStruct,
    Dictionary<int, MaxonStructLiteralOp> structLiterals) {
    var (FieldName, Value) = arrayStruct.FieldValues.FirstOrDefault(f => f.FieldName == ManagedFieldName);
    if (Value != null && structLiterals.TryGetValue(Value.Id, out var managedStruct)) {
      return managedStruct;
    }
    return null;
  }
}
