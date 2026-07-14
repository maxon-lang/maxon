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

/// The `Promise` facade type declared in stdlib/Builtins.maxon, named in one place so the
/// compiler's promise paths key off a constant rather than a scattered string literal.
///
/// `Promise uses Element, ErrorType`, and `ErrorType` is an OPTIONAL trailing type parameter:
///   `Promise with T`       — the thunk does not throw; ErrorType is absent from TypeParams.
///   `Promise with (T, E)`  — the thunk throws E; ErrorType is bound to E and SURVIVES storage.
public static class PromiseType {
  public const string TypeName = "Promise";
  /// The type of the value the thunk returns.
  public const string ElementParam = "Element";
  /// The type of the error the thunk throws. Absent iff the thunk does not throw.
  public const string ErrorParam = "ErrorType";
  /// `ErrorType` may be omitted — that omission is what spells "non-throwing".
  public const int OptionalTrailingTypeParams = 1;
  /// The green-thread handle. The box's only field; see the type's doc comment for why the
  /// `throws_` and `errorIsHeapPtr` bits that used to accompany it are gone.
  public const string InnerField = "inner";
}

/// Represents a promise (green thread handle) from an async call.
/// Carries type info about the eventual result so await can produce the right type.
public sealed class MaxonPromise(
    int id,
    MaxonValueKind? innerKind,
    string? innerStructTypeName,
    Core.IrType? errorType = null,
    int? greenThreadId = null) : MaxonValue(id) {
  public MaxonValueKind? InnerKind { get; } = innerKind;
  public string? InnerStructTypeName { get; } = innerStructTypeName;

  /// The GREEN THREAD this promise names — the identity in which `await` is LINEAR (E3100).
  ///
  /// It is NOT `Id`. `Id` identifies the SSA VALUE, and one green thread is named by MANY
  /// values: every cross-block read of a promise variable re-tags a fresh MaxonPromise around
  /// the same thread, and `let q = p` binds a second name to it. Keying linearity on `Id` would
  /// see those as unrelated promises and let a second await through — which is a DOUBLE FREE,
  /// because the thunk hands its result over once and two awaits release it twice.
  ///
  /// A thread comes into existence at exactly one place — the `async` spawn — so a promise
  /// minted there names itself, and every value derived from it CARRIES this id forward. The
  /// one derivation that cannot is a promise reconstructed out of STORAGE (an array element, a
  /// struct field): the box holds a runtime handle, and which thread it is is not a static fact.
  /// Such a promise gets a fresh id, so it is never spuriously EQUAL to another — the check
  /// stays silent there rather than guessing. See CheckLinearAwait for that boundary.
  public int GreenThreadId { get; } = greenThreadId ?? id;

  /// The awaited thunk's declared `throws` type, or null if the thunk does not throw.
  ///
  /// This is the WHOLE of what a promise knows about errors. It used to be accompanied by
  /// two bits distilled from it — `Throws` and `ErrorIsHeapPtr` — and the distillation was
  /// the bug: a bit cannot say which enum an ordinal belongs to, so an `otherwise (e)`
  /// binding fell back to `int`, an associated-value payload went un-decref'd, and a thunk
  /// throwing A could be awaited inside a function throwing B with A's ordinals silently
  /// reinterpreted as B's tags. Both bits are now DERIVED from this one field, so there is
  /// no second copy of the fact to drift out of agreement with it.
  ///
  /// It survives storage. `Promise with (T, E)` names the error type in the type itself, so
  /// boxing a promise into an array or a struct field keeps E; `Promise with T` is the type
  /// of a NON-throwing promise, and a throwing one cannot be stored in it (E3098 says so and
  /// names the two-parameter form). That is why there is no longer an `errorIsHeapPtr` bit
  /// riding along in the box to paper over an erased type — nothing is erased.
  public Core.IrType? ErrorType { get; } = errorType;

  /// Whether the spawned function throws — i.e. whether `try await` is required.
  /// Derived: a promise throws exactly when it has an error type to throw.
  public bool Throws => ErrorType != null;

  /// Whether an error flag of this type is a heap POINTER — an owned associated-value
  /// enum payload the error path must mm_decref — rather than a plain ordinal.
  ///
  /// This is the single answer to that question. It is asked in the places that must agree
  /// or the program either leaks (a payload nobody releases) or faults (an ordinal
  /// mm_decref'd as a pointer): when the parser decides whether an `otherwise` path needs a
  /// cleanup branch, and when it emits that branch. They are not separate tests of the same
  /// shape — they are callers of this one.
  public static bool ErrorTypeIsHeapPtr(Core.IrType? errorType) =>
      errorType is Core.IrEnumType { HasAssociatedValues: true };

  /// True iff the error flag is a heap pointer that the `otherwise` path must mm_decref.
  /// Statically known at every await site, boxed or not, because ErrorType is.
  public bool ErrorIsHeapPtr => ErrorTypeIsHeapPtr(ErrorType);
}
