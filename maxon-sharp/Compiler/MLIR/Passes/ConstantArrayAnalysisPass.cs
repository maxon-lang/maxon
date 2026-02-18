using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Identifies array literals with all-constant elements and tags them for .rdata placement.
/// This is pure analysis on Maxon ops that runs before MaxonToStandardConversion.
/// Results are stored in module.ConstantArrayLiterals for consumption by the conversion.
/// </summary>
public static class ConstantArrayAnalysisPass {
  public static void Run(MlirModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      AnalyzeFunction(func, module);
    }
  }

  private static void AnalyzeFunction(MlirFunction<MaxonOp> func, MlirModule<MaxonOp> module) {
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
        // Accept any struct with ArrayLiteralTag (Array, Vector aliases, etc.)
        if (arrayStructLit.ArrayLiteralTag == null) continue;

        var tag = arrayStructLit.ArrayLiteralTag;
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
        if (allConstant) {
          // Extract element_size from the __ManagedMemory struct
          MaxonStructLiteralOp? managedStruct = (arrayStructLit.TypeName == "__ManagedMemory"
              ? arrayStructLit
              : FindManagedMemoryStruct(arrayStructLit, structLiterals)) ?? throw new InvalidOperationException(
                  $"ConstantArrayAnalysisPass: cannot find __ManagedMemory struct for array '{assignOp.VarName}' in {func.Name}");

          var (FieldName, Value) = managedStruct.FieldValues.FirstOrDefault(f => f.FieldName == "element_size");

          if (Value == null || !literalValues.TryGetValue(Value.Id, out var elemSizeVal)) {
            throw new InvalidOperationException(
              $"ConstantArrayAnalysisPass: cannot determine element_size for array '{assignOp.VarName}' in {func.Name}");
          }
          int elementSize = (int)elemSizeVal;

          // Mutable structs with stack-sized element buffers keep data on the stack
          // so .set() can write directly without COW heap-copying
          if (assignOp.IsMutable && HasStackAllocatableBuffer(arrayStructLit.TypeName, module.TypeDefs)) {
            continue;
          }

          // Include function name in rdata label to avoid conflicts
          var rdataLabel = $"__const_array_{func.Name}_{assignOp.VarName}";
          module.ConstantArrayLiterals[arrayStructLit.Result.Id] =
            new ConstantArrayLiteralInfo(rdataLabel, elementValues, assignOp.IsMutable, elementSize);
        }
      }
    }
  }

  private static bool HasStackAllocatableBuffer(string typeName, Dictionary<string, MlirType> typeDefs) {
    if (!typeDefs.TryGetValue(typeName, out var typeDef)) return false;
    if (typeDef is not MlirStructType structType) return false;
    return structType.HasStackAllocatableBuffer;
  }

  /// <summary>
  /// Find the nested __ManagedMemory struct within an Array/Vector struct literal.
  /// Arrays have a 'managed' field that contains the __ManagedMemory struct.
  /// </summary>
  private static MaxonStructLiteralOp? FindManagedMemoryStruct(
    MaxonStructLiteralOp arrayStruct,
    Dictionary<int, MaxonStructLiteralOp> structLiterals) {
    var (FieldName, Value) = arrayStruct.FieldValues.FirstOrDefault(f => f.FieldName == "managed");
    if (Value != null && structLiterals.TryGetValue(Value.Id, out var managedStruct)) {
      return managedStruct;
    }
    return null;
  }
}
