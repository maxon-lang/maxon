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
    Core.IrType? errorType = null,
    MaxonValue? errorIsHeapPtrRuntime = null) : MaxonValue(id) {
  public MaxonValueKind? InnerKind { get; } = innerKind;
  public string? InnerStructTypeName { get; } = innerStructTypeName;
  /// Whether the spawned function is a throwing function (requires try await).
  public bool Throws { get; } = throws;
  /// The awaited thunk's declared `throws` type — the type an `otherwise (e)`
  /// binding must take. Carrying the TYPE (not a bit distilled from it) is what
  /// lets `try await p otherwise (e)` bind `e` as the error the thunk actually
  /// throws instead of as the promise's raw i64 representation.
  ///
  /// Null in exactly two cases, which are not the same thing:
  ///   - the spawned callee does not throw (Throws == false);
  ///   - the promise came out of storage as a bare `Promise with T`, whose type
  ///     has no slot for the error (Throws == true, ErrorType == null). Use
  ///     `Promise with (T, E)` to keep E across storage.
  public Core.IrType? ErrorType { get; } = errorType;

  /// Whether an error flag of this type is a heap POINTER — an owned associated-value
  /// enum payload the error path must mm_decref — rather than a plain ordinal.
  ///
  /// This is the single answer to that question. It is asked in three places that must
  /// agree or the program either leaks (a payload nobody releases) or faults (an ordinal
  /// mm_decref'd as a pointer): here, when boxing a promise into a `Promise with T`; in
  /// the parser, when deciding whether an `otherwise` path needs a cleanup branch; and
  /// in the parser again, when emitting that branch. They are not three tests of the
  /// same shape any more — they are three callers of this one.
  public static bool ErrorTypeIsHeapPtr(Core.IrType? errorType) =>
      errorType is Core.IrEnumType { HasAssociatedValues: true };

  /// True iff the error flag is a heap pointer that the `otherwise` path must mm_decref.
  /// Derived from ErrorType rather than stored: a second, independently-passed bit is a
  /// second thing to forget, and it HAS been forgotten (the cross-block promise re-tag
  /// used to drop it).
  public bool ErrorIsHeapPtr => ErrorTypeIsHeapPtr(ErrorType);
  /// Runtime SSA bool loaded from the boxed Promise struct's `errorIsHeapPtr`
  /// field. Non-null only when the error TYPE was erased by storage in a bare
  /// `Promise with T` (ReconstructPromiseFromStruct) — the one bit of the type
  /// that survives boxing. The otherwise emitter branches on it to decide the
  /// decref; a `Promise with (T, E)` needs no such branch because E is static.
  public MaxonValue? ErrorIsHeapPtrRuntime { get; } = errorIsHeapPtrRuntime;
}
