namespace MaxonSharp.Compiler.Ir.Dialects;

public abstract class MaxonValue(int id) {
  public int Id { get; } = id;
  public override string ToString() => Core.IrContext.IsStdlibId(Id)
    ? $"%s{Id & ~Core.IrContext.StdlibIdBit}"
    : $"%{Id}";
  public override bool Equals(object? obj) => obj is MaxonValue other && Id == other.Id;
  public override int GetHashCode() => Id;
}

public sealed class MaxonInteger(int id) : MaxonValue(id);
public sealed class MaxonCString(int id) : MaxonValue(id);
public sealed class MaxonFloat(int id) : MaxonValue(id);
public sealed class MaxonBool(int id) : MaxonValue(id);
public sealed class MaxonByte(int id) : MaxonValue(id);
public sealed class MaxonShort(int id) : MaxonValue(id);
public sealed class MaxonStruct(int id, string typeName) : MaxonValue(id) {
  public string TypeName { get; set; } = typeName;
}
public sealed class MaxonEnum(int id, string typeName) : MaxonValue(id) {
  public string TypeName { get; } = typeName;
}
public sealed class MaxonFunctionPtr(int id) : MaxonValue(id);

/// Represents a promise (green thread handle) from an async call.
/// Carries type info about the eventual result so await can produce the right type.
public sealed class MaxonPromise(
    int id,
    MaxonValueKind? innerKind,
    string? innerStructTypeName,
    bool throws = false,
    bool errorIsHeapPtr = false,
    MaxonValue? errorIsHeapPtrRuntime = null) : MaxonValue(id) {
  public MaxonValueKind? InnerKind { get; } = innerKind;
  public string? InnerStructTypeName { get; } = innerStructTypeName;
  /// Whether the spawned function is a throwing function (requires try await).
  public bool Throws { get; } = throws;
  /// Compile-time-known: true iff the spawned callee's ThrowsType is an
  /// associated-value enum (i.e. the error flag is a heap pointer that needs
  /// mm_decref on the otherwise path). Direct-await path only.
  public bool ErrorIsHeapPtr { get; } = errorIsHeapPtr;
  /// Runtime SSA bool loaded from the boxed Promise struct's `errorIsHeapPtr`
  /// field. Non-null only on the storage-sourced path (ReconstructPromiseFromStruct).
  /// When non-null, ErrorIsHeapPtr is ignored — the otherwise emitter branches
  /// on this runtime bit instead.
  public MaxonValue? ErrorIsHeapPtrRuntime { get; } = errorIsHeapPtrRuntime;
}
