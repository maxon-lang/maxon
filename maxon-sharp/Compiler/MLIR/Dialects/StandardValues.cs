namespace MaxonSharp.Compiler.Ir.Dialects;

public abstract class StdValue(int id) {
  public int Id { get; } = id;
  public override string ToString() => Core.IrContext.IsStdlibId(Id)
    ? $"%s{Id & ~Core.IrContext.StdlibIdBit}"
    : $"%{Id}";
  public override bool Equals(object? obj) => obj is StdValue other && Id == other.Id;
  public override int GetHashCode() => Id;
}

public class StdI64(int id) : StdValue(id);
public class StdI32(int id) : StdValue(id);
public class StdU64(int id) : StdI64(id);
public class StdHeapPtr(int id, string typeName, string? varName = null) : StdI64(id) {
  public string TypeName { get; } = typeName;
  /// <summary>
  /// The variable name prefix used for EmitStore/EmitLoad (e.g. "__callret_5").
  /// Null when the heap pointer is an SSA value not yet stored to a variable.
  /// </summary>
  public string? VarName { get; } = varName;
}
/// <summary>
/// Pointer to a stack-allocated struct. Behaves like StdHeapPtr for field access
/// and function passing, but has no refcount header — incref/decref must be skipped.
/// </summary>
public class StdStackPtr(int id, string typeName, string? varName = null) : StdHeapPtr(id, typeName, varName);
public class StdU32(int id) : StdI32(id);
public class StdF32(int id) : StdValue(id);
public class StdF64(int id) : StdValue(id);
public class StdBool(int id) : StdValue(id);
public class StdPtr(int id) : StdValue(id);
