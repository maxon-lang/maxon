using System.Reflection;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Core;

/// <summary>
/// Copies Maxon ops, the SSA values they reference, and the TYPES they reference, so a cloned module
/// owns all three outright.
///
/// It is the second half of the same fact <see cref="TypeGraphCopier"/> states about types: a compile
/// WRITES to the IR it is handed, and the module it is handed comes from a cache that outlives it.
/// <c>IrFunction.DeepClone</c> copied the block LISTS but shared the op OBJECTS, and ops and values
/// carry per-compile conclusions — <see cref="MaxonStruct.TypeName"/> above all, which
/// monomorphization refines as it resolves a generic call to a concrete instance. Sharing it meant
/// compile B started from compile A's conclusions: measured on board row A4r, where
/// <c>stdlib.__roundedToLength</c>'s receiver read <c>__Array_DecimalDigit</c> when its program was
/// compiled alone and <c>DigitString</c> when another program had been compiled first in the same
/// process, and the two emitted different binaries from identical sources.
///
/// ⚠ The value map is keyed by SSA ID, not by object, and that is the point: the copy of a value is
/// the SAME SSA value in a different module, so every op that referenced id N must end up referencing
/// the one copy of id N.
///
/// ⚠ TYPES are rebound through the module's ONE <see cref="TypeGraphCopier"/>, not a private one.
/// An op's type reference is a reference into the type graph exactly as a TypeDef's is, and
/// <see cref="TypeGraphCopier"/> already refuses to distinguish them: it copies a container type
/// solely because "a reference into the ORIGINAL graph reached through one of these is exactly as
/// contaminating as a direct one". <c>MaxonTryCallOp.ThrowsType</c> and <c>MaxonPromise.ErrorType</c>
/// reach a MUTABLE <see cref="IrEnumType"/>, and thirteen more op fields reach the graph through an
/// <see cref="IrFunctionType"/>. Sharing the ONE copier is also what keeps reference identity intact
/// across the whole clone: an op's type and the TypeDef of the same name stay the same object, which
/// is what <c>RefreshTypeAliasTypeParams</c>'s <c>currentType != paramType</c> test reads.
/// </summary>
sealed class OpGraphCopier(TypeGraphCopier types) {
  private readonly Dictionary<int, MaxonValue> _values = [];

  /// <summary>
  /// How one field of an op or a value must be treated when it is copied. Built once per declaring
  /// TYPE and cached: the reflection cost is paid once per op class per process, not once per op.
  /// </summary>
  private enum FieldRebind { Scalar, RebindableList, TupleList }

  // Concurrent because the spec runner clones a stdlib module on each of its workers, and this is read
  // once per OP — a lock here would serialize every worker's clone on a table that only ever grows to
  // the number of op and value classes.
  private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, (FieldInfo Field, FieldRebind Rebind)[]> _plans = new();

  public MaxonOp Copy(MaxonOp op) => RebindFields(op.ShallowCopy());

  private MaxonValue? CopyValue(MaxonValue? value) {
    if (value == null) return null;
    if (_values.TryGetValue(value.Id, out var existing)) {
      if (existing.GetType() != value.GetType())
        throw new InvalidOperationException(
          $"SSA id {value.Id} appears as both {existing.GetType().Name} and {value.GetType().Name}; " +
          "one id must denote one value");
      return existing;
    }

    // Memoised BEFORE its own fields are rebound, so a value that reaches itself terminates.
    var copy = value.CopyKeepingId();
    _values[value.Id] = copy;
    return RebindFields(copy);
  }

  private T RebindFields<T>(T copy) where T : notnull {
    foreach (var (field, rebind) in PlanFor(copy.GetType())) {
      switch (rebind) {
        case FieldRebind.Scalar:
          field.SetValue(copy, Rebind(field.GetValue(copy)));
          break;
        case FieldRebind.RebindableList:
          field.SetValue(copy, CopyRebindableList((System.Collections.IList?)field.GetValue(copy), field.FieldType));
          break;
        case FieldRebind.TupleList:
          field.SetValue(copy, CopyTupleList((System.Collections.IList?)field.GetValue(copy), field.FieldType));
          break;
        default: throw new InvalidOperationException($"unhandled field rebind '{rebind}'");
      }
    }
    return copy;
  }

  /// The one place that says what "rebind" MEANS, so the value path and the type path cannot drift:
  /// a value becomes this module's copy of that SSA id, a type becomes this module's copy of that
  /// type, and anything else is a shape <see cref="Classify"/> should never have admitted.
  private object? Rebind(object? item) => item switch {
    null => null,
    MaxonValue value => CopyValue(value),
    IrType type => types.Copy(type),
    _ => throw new InvalidOperationException(
      $"OpGraphCopier cannot rebind a '{item.GetType()}' — Classify admitted a shape Rebind does not know")
  };

  private System.Collections.IList? CopyRebindableList(System.Collections.IList? source, Type listType) {
    if (source == null) return null;

    var copy = (System.Collections.IList)Activator.CreateInstance(listType, source.Count)!;
    foreach (var item in source) copy.Add(Rebind(item));
    return copy;
  }

  /// <summary>
  /// A list of tuples with a rebindable slot (a struct literal's field list, an interpolation's
  /// parts — whose slots hold a value AND a type). ValueTuple's slots are public FIELDS, so a boxed
  /// element can be rebound in place and re-added — which is why this needs no per-shape code and no
  /// knowledge of the tuple's arity. Enumerating a non-generic <c>IList</c> boxes each element
  /// afresh, so writing to the box never reaches the source list.
  /// </summary>
  private System.Collections.IList? CopyTupleList(System.Collections.IList? source, Type listType) {
    if (source == null) return null;

    var slots = RebindableSlotsOf(listType.GetGenericArguments()[0]);
    var copy = (System.Collections.IList)Activator.CreateInstance(listType, source.Count)!;
    foreach (var item in source) {
      object boxed = item!;
      foreach (var slot in slots) slot.SetValue(boxed, Rebind(slot.GetValue(boxed)));
      copy.Add(boxed);
    }
    return copy;
  }

  /// <summary>
  /// The fields of <paramref name="declaringType"/> that hold an SSA value or a type, in any shape
  /// this copier knows how to rebind — and a THROW for any field that reaches one in a shape it does
  /// not. Silence there would be the original bug in miniature: one field left pointing into the
  /// template, with nothing to say so.
  /// </summary>
  private static (FieldInfo, FieldRebind)[] PlanFor(Type declaringType) => _plans.GetOrAdd(declaringType, static type => {
    var plan = new List<(FieldInfo, FieldRebind)>();
    for (var declaring = type; declaring != null && declaring != typeof(object); declaring = declaring.BaseType) {
      foreach (var field in declaring.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
        if (Classify(field) is { } rebind) plan.Add((field, rebind));
        else if (ReachesRebindable(field.FieldType, [])) throw UnknownShape(type, field, field.FieldType);
      }
    }
    return [.. plan];
  });

  /// The ONE refusal, so the two shapes that can trigger it cannot describe themselves differently.
  private static InvalidOperationException UnknownShape(Type declaringType, FieldInfo field, Type culprit) =>
    new($"{declaringType.Name}.{field.Name} reaches an SSA value or a type through '{culprit}', a shape " +
        "OpGraphCopier cannot rebind — teach it that shape, or a cloned module would share that field " +
        "with the module it was cloned from");

  private static bool IsRebindable(Type type) =>
    typeof(MaxonValue).IsAssignableFrom(type) || typeof(IrType).IsAssignableFrom(type);

  private static FieldRebind? Classify(FieldInfo field) {
    var fieldType = field.FieldType;
    if (IsRebindable(fieldType)) return FieldRebind.Scalar;
    if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(List<>)) return null;

    var element = fieldType.GetGenericArguments()[0];
    if (IsRebindable(element)) return FieldRebind.RebindableList;
    if (!IsValueTuple(element) || RebindableSlotsOf(element).Length == 0) return null;

    // An 8-or-more-ary ValueTuple hides its 8th slot onwards inside `Rest`, which RebindableSlotsOf
    // cannot see through. Refusing beats rebinding the first seven and leaving the rest pointing at
    // the template — which is the silence this whole guard exists to prevent, one slot deep.
    foreach (var slot in element.GetFields())
      if (!IsRebindable(slot.FieldType) && ReachesRebindable(slot.FieldType, []))
        throw UnknownShape(field.DeclaringType!, field, slot.FieldType);

    return FieldRebind.TupleList;
  }

  private static bool IsValueTuple(Type type) =>
    type.IsGenericType && type.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true;

  private static FieldInfo[] RebindableSlotsOf(Type tupleType) =>
    [.. tupleType.GetFields().Where(f => IsRebindable(f.FieldType))];

  /// Whether a value or a type is reachable through <paramref name="type"/> at all — through an array
  /// element, through its generic arguments, or through the fields of a type this compiler itself
  /// declares. The visited set is what makes a recursive type (a node holding a list of nodes)
  /// terminate.
  private static bool ReachesRebindable(Type type, HashSet<Type> visited) {
    if (IsRebindable(type)) return true;
    if (!visited.Add(type)) return false;

    // An array is not IsGenericType and has no fields of its own, so without this it reads as
    // reaching nothing at all — and a `MaxonValue[]` field would be shared in silence.
    if (type.IsArray) return ReachesRebindable(type.GetElementType()!, visited);

    if (type.IsGenericType && type.GetGenericArguments().Any(a => ReachesRebindable(a, visited))) return true;
    if (type.Assembly != typeof(MaxonValue).Assembly) return false;

    return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
      .Any(f => ReachesRebindable(f.FieldType, visited));
  }
}
